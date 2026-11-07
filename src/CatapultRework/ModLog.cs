using System.Globalization;
using UnityEngine;

namespace CatapultRework
{
    /// <summary>
    /// Logging helper. Debug lines are written at Info level (BepInEx hides Debug by default)
    /// with a [CR] prefix, and only when "DebugLogging" is enabled in the client config.
    /// </summary>
    internal static class ModLog
    {
        public static bool DebugEnabled => ModConfig.DebugLogging != null && ModConfig.DebugLogging.Value;

        public static void Debug(string msg)
        {
            if (DebugEnabled) Plugin.Log.LogInfo("[CR] " + msg);
        }

        public static void Info(string msg) => Plugin.Log.LogInfo(msg);
        public static void Warn(string msg) => Plugin.Log.LogWarning(msg);

        public static string F(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", v.x, v.y, v.z);

        public static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
