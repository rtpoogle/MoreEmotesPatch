using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameNetcodeStuff;
using UnityEngine.InputSystem;
using System.Reflection.Emit;
using BepInEx.Logging;
using UnityEngine.Diagnostics;
using DunGen.Graph;
using System.Reflection;
using MoreEmotes;
using UnityEngine;
using MoreEmotes.Patch;
using DunGen;
using UnityEngine.InputSystem.Controls;
using System.IO;
using BepInEx.Configuration;
using LethalConfig;
using LethalConfig.ConfigItems.Options;
using LethalConfig.ConfigItems;

namespace MoreEmotesPatch
{
    [BepInPlugin(GUID, NAME, VER)]
    [BepInDependency(MoreEmotes.PluginInfo.GUID)]
    [BepInDependency(LethalConfig.PluginInfo.Guid)]
    public class MoreEmotesPatchPlugin : BaseUnityPlugin
    {
        public const string GUID = "xyz.poogle.moreemotespatch";
        public const string NAME = "More Emotes Patch";
        public const string VER = "1.0.0";
        public readonly Harmony harmony = new Harmony(GUID);

        public ConfigEntry<int> maxEmoteTime;

        public ManualLogSource LogSrc;

        public static MoreEmotesPatchPlugin Instance;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by BepInEx on Awake")]
        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            maxEmoteTime = Config.Bind("General", "MaxEmoteTime", 30, "The max time for any emote to last before auto cancelled in seconds.\n(0 will never cancel.)");

            LethalConfigManager.AddConfigItem(new IntSliderConfigItem(maxEmoteTime, new IntSliderOptions
            {
                RequiresRestart = false,
                Min = 0,
                Max = 120
            }));

            LogSrc = BepInEx.Logging.Logger.CreateLogSource(GUID);

            if (Harmony.HasAnyPatches("MoreEmotes"))
            {
                Patches.InitPatches();

                if(maxEmoteTime.Value == 0)
                {
                    MethodInfo PCB_U = AccessTools.Method(typeof(PlayerControllerB), nameof(PlayerControllerB.Update));
                    harmony.Unpatch(PCB_U, HarmonyPatchType.Prefix, GUID);
                    LogSrc.LogWarning("Update patch was disabled with 0!");
                }

                LogSrc.LogInfo("Initialized!");
            } else
            {
                LogSrc.LogError("Could not find any MoreEmotes patches from harmony, we could not enable!");
            }

        }
    }

    internal class Patches
    {
        public static int CurrentTime {
            get {
                return (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            }
        }

        internal static void InitPatches()
        {
            MoreEmotesPatchPlugin.Instance.harmony.PatchAll();
        }

        [HarmonyPatch(typeof(PlayerControllerB))]
        [HarmonyPatch("CheckConditionsForEmote")]
        public static class PlayerControllerB_CheckConditionsForEmote_Patch
        {

            // anything put into the result will make the emote stop
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(PlayerControllerB __instance, ref bool __result)
            {
                // the false here is used for convenience in the editor, only needing to add or remove a comment, it does not affect the output
                // __result = __result || !( false
                __result = !( false
                    || __instance.inSpecialInteractAnimation
                    || __instance.isPlayerDead
                    // || __instance.isJumping
                    // || __instance.isWalking
                    || __instance.isCrouching
                    || __instance.isClimbingLadder
                    || __instance.isGrabbingObjectAnimation
                    || __instance.inTerminalMenu
                    //|| __instance.isTypingChat
                    );
            }
        }
        class MoreEmotesPatches
        {
            [HarmonyPatch(typeof(EmotePatch))]
            [HarmonyPatch("CheckEmoteInput")]
            public static class MoreEmotes_EmotePatch_CheckEmoteInput_Patch
            {
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
                {
                    MoreEmotesPatchPlugin.Instance.LogSrc.LogInfo("Patching CheckEmoteInput!");
                    MethodInfo isPressed = AccessTools.Method(typeof(InputControlExtensions), nameof(InputControlExtensions.IsPressed));
                    MethodInfo wasPressedThisFrame = AccessTools.PropertyGetter(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame));
                    var code = new List<CodeInstruction>(instructions);

                    int insertionIndex = -1;
                    for (int i = 0; i < code.Count - 1; i++) // -1 since we will be checking i + 1
                    {
                        if (code[i].opcode == OpCodes.Ldc_R4 && (float)code[i].operand == 0.0f && code[i + 1].Calls(isPressed))
                        {
                            insertionIndex = i;
                            break;
                        }
                    }

                    if (insertionIndex == -1)
                    {
                        MoreEmotesPatchPlugin.Instance.LogSrc.LogError("Could not find IL to transpile, bailing patch!");
                        return code;
                    }
                    code[insertionIndex] = new CodeInstruction(OpCodes.Castclass, typeof(ButtonControl));
                    code[insertionIndex + 1] = new CodeInstruction(OpCodes.Callvirt, wasPressedThisFrame);
                    MoreEmotesPatchPlugin.Instance.LogSrc.LogInfo("Patched CheckEmoteInput!");
                    return code;
                }
            }
        }

        static int LastEmoteTime = -1;
        internal static int MO_Offset = EmotePatch.AlternateEmoteIDOffset;
        internal static int[] MO_Doubles = ((int[])Enum.GetValues(typeof(Emotes))).Where(x => x - MO_Offset > 0).Select(x => x - MO_Offset).ToArray();

        [HarmonyPatch(typeof(PlayerControllerB))]
        [HarmonyPatch("PerformEmote")]
        public static class PlayerControllerB_PerformEmote_Patch
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(PlayerControllerB __instance, int emoteID)
            {
                int last_emote = __instance.playerBodyAnimator.GetInteger("emoteNumber");

                // if we are already performing an emote
                if (__instance.performingEmote)
                {
                    // if this is a "double" emote from MoreEmotes
                    if (MO_Doubles.Contains(emoteID))
                    {
                        // if already on "double" emote stage
                        if (last_emote == emoteID + MO_Offset)
                        {
                            return StopAnyEmotes(__instance);
                        }
                        // otherwise allow checks to run
                        return true;
                    }

                    // if the same emote button was pressed twice
                    if (last_emote == emoteID)
                    {
                        return StopAnyEmotes(__instance);
                    }
                }
                LastEmoteTime = CurrentTime;
                return true;
            }
        }

        [HarmonyPatch(typeof(PlayerControllerB))]
        [HarmonyPatch("Update")]
        public static class PlayerControllerB_Update_Patch
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(PlayerControllerB __instance)
            {
                if (MoreEmotesPatchPlugin.Instance.maxEmoteTime.Value > 0 && __instance.performingEmote)
                {
                    // specific patch for Sign from MoreEmotes
                    int CurrentEmoteOffset = __instance.playerBodyAnimator.GetInteger("emoteNumber");
                    // if not specifically creating a sign
                    // sidenote: please keep this enum consistent moreemotes modder person
                    if ((Emotes)CurrentEmoteOffset != Emotes.Sign)
                    {
                        // if the emote has been running for longer than the timeout
                        if (LastEmoteTime != -1 && CurrentTime - LastEmoteTime > MoreEmotesPatchPlugin.Instance.maxEmoteTime.Value)
                        {
                            StopAnyEmotes(__instance);
                        }
                    }

                    // if we arent allowed to emote right now
                    if (!__instance.CheckConditionsForEmote())
                    {
                        StopAnyEmotes(__instance);
                    }
                }

                return true;
            }
        }

        static bool StopAnyEmotes(PlayerControllerB __instance)
        {
            __instance.performingEmote = false;
            __instance.StopPerformingEmoteServerRpc();
            __instance.timeSinceStartingEmote = 0f;
            return false;
        }
    }
}
