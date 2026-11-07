using BepInEx.Configuration;
using UnityEngine;

namespace CatapultRework
{
    /// <summary>
    /// Two groups of settings:
    ///  - Gameplay (server-authoritative): synced from the server by Jötunn, locked for players.
    ///  - Client: camera and control feel, each player chooses their own.
    /// </summary>
    internal static class ModConfig
    {
        // ---- Gameplay (server-synced, admin only) ----
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> AimConeDegrees;
        public static ConfigEntry<float> MinRangePercent;
        public static ConfigEntry<float> MaxRangePercent;
        public static ConfigEntry<float> SpreadConeDegrees;
        public static ConfigEntry<float> MaxRangeMultiplier;
        public static ConfigEntry<bool> ExitOnDamage;
        public static ConfigEntry<bool> FireOnLegsUp;
        public static ConfigEntry<bool> OnlyCatapultAmmo;
        public static ConfigEntry<string> ExtraAllowedAmmo;

        // ---- Client (local) ----
        public static ConfigEntry<float> CameraHeight;
        public static ConfigEntry<float> CameraMinHeight;
        public static ConfigEntry<float> CameraMaxHeight;
        public static ConfigEntry<float> CameraPitch;
        public static ConfigEntry<float> CameraSmoothing;
        public static ConfigEntry<float> MouseSensitivity;
        public static ConfigEntry<float> KeyboardAimSpeed;
        public static ConfigEntry<bool> ShowTrajectory;
        public static ConfigEntry<bool> ShowInfoText;
        public static ConfigEntry<bool> DebugLogging;

        private const string Gameplay = "1 - Gameplay (server)";
        private const string Client = "2 - Client";

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind(Gameplay, "Enabled", true,
                Synced("Enables the aimed firing mode when the catapult legs are down. When disabled, the catapult is fully vanilla."));

            AimConeDegrees = cfg.Bind(Gameplay, "AimConeDegrees", 10f,
                Synced("Half-angle of the aiming sector, in degrees, around the catapult's firing direction. 10 means ±10°.",
                    new AcceptableValueRange<float>(0f, 60f)));

            MinRangePercent = cfg.Bind(Gameplay, "MinRangePercent", 50f,
                Synced("Closest allowed target, as a percentage of the catapult's maximum range.",
                    new AcceptableValueRange<float>(0f, 100f)));

            MaxRangePercent = cfg.Bind(Gameplay, "MaxRangePercent", 100f,
                Synced("Farthest allowed target, as a percentage of the catapult's maximum range.",
                    new AcceptableValueRange<float>(1f, 100f)));

            SpreadConeDegrees = cfg.Bind(Gameplay, "SpreadConeDegrees", 1.5f,
                Synced("Shot dispersion: half-angle, in degrees, of a cone around the aim line. The shell lands at a random point inside the circle this cone makes on the ground, so the circle grows with distance. 0 = pin-point.",
                    new AcceptableValueRange<float>(0f, 15f)));

            MaxRangeMultiplier = cfg.Bind(Gameplay, "MaxRangeMultiplier", 1f,
                Synced("Multiplies the vanilla maximum launch speed. 1 = vanilla maximum range. Higher values reach farther (limited by projectile lifetime).",
                    new AcceptableValueRange<float>(0.25f, 3f)));

            ExitOnDamage = cfg.Bind(Gameplay, "ExitOnDamage", true,
                Synced("Leave the aiming view when the aiming player takes damage."));

            FireOnLegsUp = cfg.Bind(Gameplay, "FireOnLegsUp", true,
                Synced("If ammo is still loaded when the legs are raised, fire it the vanilla way (fixed arc). Otherwise the ammo stays loaded until the legs are lowered and it is fired by aiming."));

            OnlyCatapultAmmo = cfg.Bind(Gameplay, "OnlyCatapultAmmo", true,
                Synced("Only real catapult payloads can be loaded (the catapult's own ammo list: Grausten, Explosive and Bloodgold payloads). Vanilla lets almost any item be loaded - tools and weapons included - and flings it without damage. Applies with the legs up too."));

            ExtraAllowedAmmo = cfg.Bind(Gameplay, "ExtraAllowedAmmo", "",
                Synced("Comma-separated prefab names that may also be loaded when OnlyCatapultAmmo is on, e.g. \"Stone,BombOoze\". They fly the vanilla way: no damage unless vanilla gives them any."));

            CameraHeight = cfg.Bind(Client, "CameraHeight", 60f,
                Local("Starting camera height above the target, in metres. Change it in-game with the scroll wheel.",
                    new AcceptableValueRange<float>(10f, 300f)));

            CameraMinHeight = cfg.Bind(Client, "CameraMinHeight", 20f,
                Local("Lowest camera height when zooming in, in metres.", new AcceptableValueRange<float>(5f, 300f)));

            CameraMaxHeight = cfg.Bind(Client, "CameraMaxHeight", 150f,
                Local("Highest camera height when zooming out, in metres.", new AcceptableValueRange<float>(10f, 500f)));

            CameraPitch = cfg.Bind(Client, "CameraPitch", 65f,
                Local("Camera tilt in the aiming view, in degrees. 90 = straight down.", new AcceptableValueRange<float>(30f, 90f)));

            CameraSmoothing = cfg.Bind(Client, "CameraSmoothing", 8f,
                Local("How quickly the camera follows the marker. Higher = snappier.", new AcceptableValueRange<float>(1f, 30f)));

            MouseSensitivity = cfg.Bind(Client, "MouseSensitivity", 1f,
                Local("Marker movement speed with the mouse.", new AcceptableValueRange<float>(0.1f, 5f)));

            KeyboardAimSpeed = cfg.Bind(Client, "KeyboardAimSpeed", 30f,
                Local("Marker movement speed with WASD or the gamepad stick, in metres per second.", new AcceptableValueRange<float>(1f, 200f)));

            ShowTrajectory = cfg.Bind(Client, "ShowTrajectory", true,
                Local("Draw the predicted flight path in the aiming view."));

            ShowInfoText = cfg.Bind(Client, "ShowInfoText", true,
                Local("Show distance, ammo state and controls on screen in the aiming view."));

            DebugLogging = cfg.Bind(Client, "DebugLogging", false,
                Local("Write detailed [CR] lines to the BepInEx log: entering/leaving the aiming view (with the reason), ballistics, shots, impacts and what each explosion hit and damaged, network messages. Useful for bug reports."));
        }

        private static ConfigDescription Synced(string desc, AcceptableValueBase range = null) =>
            new ConfigDescription(desc, range, new ConfigurationManagerAttributes { IsAdminOnly = true });

        private static ConfigDescription Local(string desc, AcceptableValueBase range = null) =>
            new ConfigDescription(desc, range);

        public static float MinRangeFraction => Mathf.Clamp01(Mathf.Min(MinRangePercent.Value, MaxRangePercent.Value) / 100f);
        public static float MaxRangeFraction => Mathf.Clamp01(Mathf.Max(MinRangePercent.Value, MaxRangePercent.Value) / 100f);
    }
}
