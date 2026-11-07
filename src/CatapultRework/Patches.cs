using HarmonyLib;
using UnityEngine;

namespace CatapultRework
{
    /// <summary>Attach the network component to every catapult.</summary>
    [HarmonyPatch(typeof(Catapult), nameof(Catapult.Start))]
    internal static class Catapult_Start_Patch
    {
        private static void Postfix(Catapult __instance)
        {
            if (__instance.GetComponent<CatapultNet>() != null) return;
            __instance.gameObject.AddComponent<CatapultNet>().Init(__instance, __instance.m_nview);
        }
    }

    /// <summary>
    /// Load point interaction.
    ///  - Empty-handed "Use" with legs down: enter the aiming view.
    ///  - Loading ammo with legs down: keep vanilla loading, but cancel the auto-fire timer.
    /// </summary>
    [HarmonyPatch(typeof(Catapult), nameof(Catapult.OnLoadPointUse))]
    internal static class Catapult_OnLoadPointUse_Patch
    {
        private static bool Prefix(Catapult __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (!ModConfig.Enabled.Value || item != null) return true;
            if (!CatapultMath.LegsReady(__instance)) return true;
            __result = AimController.TryStart(__instance, user as Player);
            return false;
        }

        private static void Postfix(Catapult __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!ModConfig.Enabled.Value || item == null || !__result) return;
            if (__instance.m_lockedLegs) __instance.CancelInvoke("Shoot");
        }
    }

    /// <summary>Add the "Aim" prompt to the load point tooltip when the legs are down.</summary>
    [HarmonyPatch(typeof(Catapult), nameof(Catapult.OnHoverLoadPoint))]
    internal static class Catapult_OnHoverLoadPoint_Patch
    {
        private static void Postfix(Catapult __instance, ref string __result)
        {
            if (!ModConfig.Enabled.Value || !CatapultMath.LegsReady(__instance)) return;
            string aim = Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] Aim");
            if (__instance.m_loadedItem != null)
                aim += "\n" + Localization.instance.Localize(__instance.m_loadedItem.m_shared.m_name) + " loaded";
            __result = string.IsNullOrEmpty(__result) ? aim : __result + "\n" + aim;
        }
    }

    /// <summary>
    /// Raising the legs with ammo still loaded: optionally fire it the vanilla way once the legs are up.
    /// Lowering the legs: cancel a pending vanilla auto-fire.
    /// Runs only on the client that toggled the legs, like vanilla Shoot().
    /// </summary>
    [HarmonyPatch(typeof(Catapult), nameof(Catapult.OnLegUse))]
    internal static class Catapult_OnLegUse_Patch
    {
        private static void Prefix(Catapult __instance, out bool __state) => __state = __instance.m_lockedLegs;

        private static void Postfix(Catapult __instance, bool __result, bool __state)
        {
            if (!ModConfig.Enabled.Value || !__result || __instance.m_loadedItem == null) return;
            bool wasLocked = __state;
            if (wasLocked)
            {
                if (ModConfig.FireOnLegsUp.Value)
                    __instance.Invoke("Shoot", __instance.m_legAnimationTime + __instance.m_shootAfterLoadDelay);
            }
            else
            {
                __instance.CancelInvoke("Shoot");
            }
        }
    }

    /// <summary>Do not throw the aiming player along with the shot.</summary>
    [HarmonyPatch(typeof(Catapult), nameof(Catapult.CollectLaunchCharacters))]
    internal static class Catapult_CollectLaunchCharacters_Patch
    {
        private static void Postfix(Catapult __instance)
        {
            AimController a = AimController.Active;
            Player p = Player.m_localPlayer;
            if (a == null || p == null || a.gameObject != __instance.gameObject) return;
            if (__instance.m_launchCharacters.Remove(p)) p.ReleaseTempParent();
        }
    }

    /// <summary>
    /// Marks the shot as aimed so Projectile.Setup can replace its velocity. Only the launch
    /// velocity changes; damage, ammo handling and effects stay vanilla.
    /// </summary>
    [HarmonyPatch(typeof(Catapult), nameof(Catapult.ShootProjectile))]
    internal static class Catapult_ShootProjectile_Patch
    {
        internal static Catapult Current;
        internal static CatapultNet CurrentNet;

        private static void Prefix(Catapult __instance)
        {
            Current = null;
            CurrentNet = null;
            CatapultNet net = __instance.GetComponent<CatapultNet>();
            if (net == null || !net.HasPendingTarget) return;
            if (!ModConfig.Enabled.Value || !__instance.m_lockedLegs)
            {
                net.HasPendingTarget = false;
                return;
            }
            Current = __instance;
            CurrentNet = net;
        }

        private static void Postfix(Catapult __instance)
        {
            CatapultNet net = __instance.GetComponent<CatapultNet>();
            if (net != null) net.HasPendingTarget = false;
            Current = null;
            CurrentNet = null;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class Projectile_Setup_Patch
    {
        private static void Prefix(Projectile __instance, ref Vector3 velocity)
        {
            Catapult c = Catapult_ShootProjectile_Patch.Current;
            CatapultNet net = Catapult_ShootProjectile_Patch.CurrentNet;
            if (c == null || net == null) return;

            Vector3 launch = __instance.transform.position;
            net.CachedLaunchOffset = c.transform.InverseTransformPoint(launch);
            net.HasCachedLaunchOffset = true;

            Ballistics.Params p = CatapultMath.ParamsFor(c, __instance);
            bool ok = CatapultMath.SolveVelocity(p, launch, net.PendingTarget, CatapultMath.MaxSpeed(c), out Vector3 v, out _);
            if (!ok)
                Plugin.Log.LogDebug("Aimed target unreachable from actual release point; firing at maximum speed.");
            velocity = v;
        }
    }

    /// <summary>While aiming, the mouse moves the marker instead of the player's view.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetMouseLook))]
    internal static class Player_SetMouseLook_Patch
    {
        private static bool Prefix(Player __instance, Vector2 mouseLook)
        {
            AimController a = AimController.Active;
            if (a == null || __instance != Player.m_localPlayer) return true;
            a.AddMouse(mouseLook);
            return false;
        }
    }

    /// <summary>
    /// While aiming: movement keys move the marker, primary attack fires.
    /// Secondary attack, jump and dodge are left alone, so vanilla releases the control (exit).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Player_SetControls_Patch
    {
        private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold)
        {
            AimController a = AimController.Active;
            if (a == null || __instance != Player.m_localPlayer) return;
            a.SetMove(movedir);
            movedir = Vector3.zero;
            if (attack) a.RequestFire();
            attack = false;
            attackHold = false;
        }
    }

    /// <summary>Overhead camera while aiming.</summary>
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
    internal static class GameCamera_UpdateCamera_Patch
    {
        private static bool Prefix(GameCamera __instance, float dt)
        {
            AimController a = AimController.Active;
            if (a == null || __instance.m_freeFly) return true;
            a.UpdateCamera(__instance, dt);
            return false;
        }
    }
}
