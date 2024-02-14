using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine.InputSystem;
using System.Reflection.Emit;
using System.Reflection;
using DunGen;
using UnityEngine.InputSystem.Controls;
using BepInEx.Configuration;
using LethalConfig;
using LethalConfig.ConfigItems.Options;
using LethalConfig.ConfigItems;
using static MoreEmotesPatch.Extensions;

namespace MoreEmotesPatch
{

    [BepInPlugin(GUID, NAME, VER)]
    [BepInDependency(LethalConfig.PluginInfo.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("MoreEmotes", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("BetterEmotes", BepInDependency.DependencyFlags.SoftDependency)]
    public class MoreEmotesPatchPlugin : BaseUnityPlugin
    {
        // Init
        private enum InitType
        {
            None = 0x0,
            BetterEmotes = 0x1,
            MoreEmotes = 0x2,
            Both = 0x3
        }
        private static InitType init_mode = InitType.None;
        private readonly Harmony harmony = new Harmony(GUID);

        // Constants
        public const string GUID = "xyz.poogle.moreemotespatch";
        public const string NAME = "More Emotes Patch";
        public const string VER = "1.1.0";

        // Config
        internal static ConfigEntry<int> maxEmoteTime;

        // BetterEmotes mode
        internal static Type BE_emoteDefs = AccessTools.TypeByName("BetterEmote.Utils.EmoteDefs");
        internal static Type BE_emotes = AccessTools.TypeByName("BetterEmote.Utils.Emote");
        internal static Type BE_doubleEmotes = AccessTools.TypeByName("BetterEmote.Utils.DoubleEmote");
        internal static Type BE_altEmotes = AccessTools.TypeByName("BetterEmote.Utils.AltEmote");

        // MoreEmotes mode
        internal static Type ME_emotePatch = AccessTools.TypeByName("MoreEmotes.Patch.EmotePatch");
        internal static Type ME_emotes = AccessTools.TypeByName("MoreEmotes.Patch.Emotes");

        void Awake()
        {
            // Debug logger
            new Log(BepInEx.Logging.Logger.CreateLogSource(GUID), true);

            // Config init
            maxEmoteTime = Config.Bind("General", "MaxEmoteTime", 30, "The max time for any emote to last before auto cancelled in seconds.\n(0 will never cancel.)");

            LethalConfigManager.AddConfigItem(new IntSliderConfigItem(maxEmoteTime, new IntSliderOptions
            {
                RequiresRestart = false,
                Min = 0,
                Max = 120
            }));


            // if BetterEmote.Utils.EmoteDefs is not null &&
            // if BetterEmote.Utils.Emote is not null
            // if BetterEmote.Utils.DoubleEmote is not null
            // if BetterEmote.Utils.AltEmote is not null
            if (BE_emoteDefs != null && BE_emotes != null && BE_doubleEmotes != null && BE_altEmotes != null)
            {
                Log.Warn("Found BetterEmotes for patching!");

                if (Harmony.HasAnyPatches("BetterEmotes"))
                {
                    init_mode |= InitType.BetterEmotes;
                    BE_Patches.InitPatches(harmony);
                }
                else
                    Log.Warn("BetterEmotes doesn't have a harmony instance to patch.");
            }

            // if MoreEmotes.Patch.Emotes is not null &&
            // if MoreEmotes.Patch.EmotePatch is not null
            if (ME_emotes != null && ME_emotePatch != null)
            {
                Log.Warn("Using legacy MoreEmotes patch, you should switch to BetterEmotes!");

                if(Harmony.HasAnyPatches("MoreEmotes"))
                {
                    init_mode |= InitType.MoreEmotes;
                    ME_Patches.InitPatches(harmony);
                }
                else
                    Log.Warn("MoreEmotes doesn't have a harmony instance to patch.");
            }

            if (init_mode != InitType.None)
            {
                Log.Info($"We have initialized with patches: {init_mode}");
                return;
            }

            Log.Error("Could not initialize as no compatible mod was found.");
        }
    }

    internal class Common_Patches
    {
        [HarmonyPatch(typeof(PlayerControllerB))]
        [HarmonyPatch("CheckConditionsForEmote")]
        public static class PlayerControllerB_CheckConditionsForEmote_Patch
        {

            // anything put into the result will make the emote stop
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(PlayerControllerB __instance, ref bool __result)
            {
                // the __result check here is used for convenience in the editor, only needing to add or remove a comment, it does not affect the output
                __result = !(false
                    || __instance.inSpecialInteractAnimation
                    || __instance.isPlayerDead
                    // || __instance.isJumping
                    // || __instance.isWalking
                    || __instance.isCrouching
                    || __instance.isClimbingLadder
                    // || __instance.isGrabbingObjectAnimation
                    || __instance.inTerminalMenu
                    // || __instance.isTypingChat
                    );
            }
        }
    }

    internal class BE_Patches
    {
        internal static void InitPatches(Harmony harmony)
        {
            harmony.PatchAllUncategorized();
            harmony.PatchCategory("BetterEmotes");
        }

        static int LastEmoteTime = -1;
        internal static int BE_Offset = ((int[])Enum.GetValues(MoreEmotesPatchPlugin.BE_doubleEmotes))[0] - (int)AccessTools.Method(MoreEmotesPatchPlugin.BE_emoteDefs, "normalizeEmoteNumber").Invoke(null, new object[] { ((int[])Enum.GetValues(MoreEmotesPatchPlugin.BE_doubleEmotes))[0] });
        internal static int[] BE_Doubles = ((int[])Enum.GetValues(MoreEmotesPatchPlugin.BE_doubleEmotes)).AddRangeToArray((int[])Enum.GetValues(MoreEmotesPatchPlugin.BE_altEmotes)).Select(x => x - BE_Offset).ToArray();

        [HarmonyPatch(typeof(PlayerControllerB))]
        [HarmonyPatch("PerformEmote")]
        [HarmonyPatchCategory("BetterEmotes")]
        public static class PlayerControllerB_PerformEmote_Patch
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(PlayerControllerB __instance, int emoteID)
            {
                int last_emote = __instance.playerBodyAnimator.GetInteger("emoteNumber");

                // if we are already performing an emote
                if (__instance.performingEmote)
                {
                    // if this is a "double" emote from BetterEmotes
                    if (BE_Doubles.Contains(emoteID))
                    {
                        // if already on "double" emote stage
                        if (last_emote == emoteID + BE_Offset )
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

        // I can definitely deduplicate this code using a generic method with a int[] argument
        [HarmonyPatch(typeof(PlayerControllerB))]
        [HarmonyPatch("Update")]
        [HarmonyPatchCategory("BetterEmotes")]
        public static class PlayerControllerB_Update_Patch
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(PlayerControllerB __instance)
            {
                if (MoreEmotesPatchPlugin.maxEmoteTime.Value > 0 && __instance.performingEmote)
                {
                    // specific patch for Sign from ~~MoreEmotes~~ BetterEmotes!
                    int CurrentEmoteOffset = __instance.playerBodyAnimator.GetInteger("emoteNumber");
                    // if not specifically creating a sign
                    if (CurrentEmoteOffset != (int)Enum.Parse(MoreEmotesPatchPlugin.BE_emotes, "Sign"))
                    {
                        // if the emote has been running for longer than the timeout
                        if (LastEmoteTime != -1 && CurrentTime - LastEmoteTime > MoreEmotesPatchPlugin.maxEmoteTime.Value)
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
    }

    internal class ME_Patches
    {
        internal static void InitPatches(Harmony harmony)
        {
            harmony.PatchAllUncategorized();
            harmony.PatchCategory("MoreEmotes");
        }

        [HarmonyPatch]
        [HarmonyPatchCategory("MoreEmotes")]
        public static class MoreEmotes_EmotePatch_CheckEmoteInput_Patch
        {
            static MethodBase TargetMethod()
            {
                return MoreEmotesPatchPlugin.ME_emotePatch.GetMethod("CheckEmoteInput", AccessTools.all);
            }
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
            {
                Log.Info("Patching CheckEmoteInput!");
                MethodInfo isPressed = AccessTools.Method(typeof(InputControlExtensions), nameof(InputControlExtensions.IsPressed));
                MethodInfo wasPressedThisFrame = AccessTools.PropertyGetter(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame));
                var code = new List<CodeInstruction>(instructions);

                for (int i = 0; i < code.Count - 1; i++) // -1 since we will be checking i + 1
                {
                    if (code[i].opcode == OpCodes.Ldc_R4 && (float)code[i].operand == 0.0f && code[i + 1].Calls(isPressed))
                    {
                        code[i] = new CodeInstruction(OpCodes.Castclass, typeof(ButtonControl));
                        code[i + 1] = new CodeInstruction(OpCodes.Callvirt, wasPressedThisFrame);
                        Log.Info("Patched CheckEmoteInput!");
                        return code;
                    }
                }

                Log.Error("Could not find IL to transpile, bailing patch!");
                return code;
            }
        }

        static int LastEmoteTime = -1;
        internal static int MO_Offset = _get<int>(MoreEmotesPatchPlugin.ME_emotePatch, "_AlternateEmoteIDOffset");
        internal static int[] MO_Doubles = ((int[])Enum.GetValues(MoreEmotesPatchPlugin.ME_emotes)).Where(x => x - MO_Offset > 0).Select(x => x - MO_Offset).ToArray();


        [HarmonyPatch(typeof(PlayerControllerB))]
        [HarmonyPatch("PerformEmote")]
        [HarmonyPatchCategory("MoreEmotes")]
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
        [HarmonyPatchCategory("MoreEmotes")]
        public static class PlayerControllerB_Update_Patch
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(PlayerControllerB __instance)
            {
                if (MoreEmotesPatchPlugin.maxEmoteTime.Value > 0 && __instance.performingEmote)
                {
                    // specific patch for Sign from MoreEmotes
                    int CurrentEmoteOffset = __instance.playerBodyAnimator.GetInteger("emoteNumber");
                    // if not specifically creating a sign
                    // sidenote: please keep this enum consistent moreemotes modder person
                    if (CurrentEmoteOffset != (int)Enum.Parse(MoreEmotesPatchPlugin.ME_emotes, "Sign"))
                    {
                        // if the emote has been running for longer than the timeout
                        if (LastEmoteTime != -1 && CurrentTime - LastEmoteTime > MoreEmotesPatchPlugin.maxEmoteTime.Value)
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
    }
}
