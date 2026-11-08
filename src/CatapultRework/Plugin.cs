using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;

namespace CatapultRework
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Every client and the server must run the same minor version of the mod.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    // Admin-only (server) config entries are always locked to the server's values,
    // even when the mod is missing on the server (in that case multiplayer is refused anyway).
    [SynchronizationMode(AdminOnlyStrictness.Always)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.majorxaker.catapultrework";
        public const string PluginName = "CatapultRework";
        public const string PluginVersion = "0.1.3";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
