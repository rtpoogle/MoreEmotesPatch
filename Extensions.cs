using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MoreEmotesPatch
{
    internal static class Extensions
    {
        // Used in both patch types
        public static T _get<T>(Type type, string pof)
        {
            return (T)AccessTools.Property(type, pof).GetValue(null);
        }

        public static int CurrentTime
        {
            get
            {
                return (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            }
        }

        public static bool StopAnyEmotes(PlayerControllerB __instance)
        {
            __instance.performingEmote = false;
            __instance.StopPerformingEmoteServerRpc();
            __instance.timeSinceStartingEmote = 0f;
            return false;
        }


        // Custom implementation of Harmony.PatchCategory until BepInEx updates HarmonyX
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        internal class HarmonyPatchCategory(string category) : Attribute
        {
            public string Category { get; } = category;
        }

        /// https://github.com/BepInEx/HarmonyX/blob/master/Harmony/Public/Harmony.cs#L195-L213
        /// <summary>Searches an assembly for Harmony annotations with a specific category and uses them to create patches</summary>
        /// <param name="category">Name of patch category</param>
        ///
        public static void PatchCategory(this Harmony harmony, string category)
        {
            var method = new StackTrace().GetFrame(1).GetMethod();
            var assembly = method.ReflectedType.Assembly;
            PatchClassProcessor[] patchClasses = AccessTools.GetTypesFromAssembly(assembly).Select(harmony.CreateClassProcessor).ToArray();
            patchClasses.DoIf(patchClass => ((HarmonyPatchCategory)patchClass.containerType.GetCustomAttribute(typeof(HarmonyPatchCategory), true))?.Category == category, patchClass => patchClass.Patch());
        }

        /// https://github.com/BepInEx/HarmonyX/blob/master/Harmony/Public/Harmony.cs#L177-L193
        /// <summary>Searches an assembly for Harmony-annotated classes without category annotations and uses them to create patches</summary>
        /// <param name="assembly">The assembly</param>
        ///
        public static void PatchAllUncategorized(this Harmony harmony)
        {
            var method = new StackTrace().GetFrame(1).GetMethod();
            var assembly = method.ReflectedType.Assembly;
            PatchClassProcessor[] patchClasses = AccessTools.GetTypesFromAssembly(assembly).Select(harmony.CreateClassProcessor).ToArray();
            patchClasses.DoIf(patchClass => string.IsNullOrEmpty(((HarmonyPatchCategory)patchClass.containerType.GetCustomAttribute(typeof(HarmonyPatchCategory), true))?.Category), patchClass => patchClass.Patch());
        }
    }
}
