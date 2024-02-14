using System;
using BepInEx.Logging;

namespace MoreEmotesPatch
{
    internal class Log
    {
        public Log(ManualLogSource logSrc, bool debug = false)
        {
            Src = logSrc;
            Dbg = debug;
        }
        public static void Info(object message)
        {
            Src?.LogInfo(message);
        }

        public static void Warn(object message)
        {
            Src?.LogWarning(message);
        }

        public static void Error(object message)
        {
            Src?.LogError(message);
        }

        public static void Debug(object message)
        {
            if (Dbg)
            {
                Src?.LogDebug(message);
            }
        }

        private static bool Dbg;

        private static ManualLogSource Src;
    }
}
