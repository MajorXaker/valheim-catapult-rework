using HarmonyLib;
using UnityEngine;

namespace CatapultRework
{
    /// <summary>
    /// Makes catapult damage work on far targets.
    ///
    /// Damage to world objects (building pieces, rocks, trees...) is sent as an RPC to the owner of the
    /// object's ZDO, and only the owner applies it. Valheim assigns owners only inside a player's active
    /// area (about 1.5 zones = ~96 m around the player's zone). Aimed shots land farther than that, where
    /// objects are loaded but have no owner: the RPC is then broadcast, every client ignores it because
    /// none of them is the owner, and the hit does nothing. Vanilla catapult range (~55 m) rarely reaches
    /// that far, which is why the problem only shows up with this mod.
    ///
    /// Fix: right before a catapult projectile or explosion damages an unowned object, the shooter claims
    /// it, so the damage is applied locally. The game hands ownership back by itself (ZDOMan releases
    /// objects outside the owner's active area), so nothing lingers.
    /// </summary>
    internal static class Ownership
    {
        internal static void ClaimIfUnowned(Collider collider, string ammo)
        {
            if (collider == null || ZNet.instance == null) return;
            GameObject go = Projectile.FindHitObject(collider);
            if (go == null || go.GetComponent<Heightmap>() != null) return;
            if (go.GetComponent<Character>() != null) return; // characters always have an owner nearby
            ZNetView nview = go.GetComponentInParent<ZNetView>();
            if (nview == null || !nview.IsValid() || nview.HasOwner()) return;
            nview.ClaimOwnership();
            ModLog.Debug($"  '{go.name}' had no owner (outside every player's active area): claimed it so the {ammo} hit is applied");
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.OnHit))]
    internal static class Ownership_AoeOnHit_Patch
    {
        [HarmonyPriority(Priority.High)]
        private static void Prefix(Aoe __instance, Collider collider)
        {
            var tag = __instance.GetComponent<CatapultBlastTag>();
            if (tag != null) Ownership.ClaimIfUnowned(collider, tag.Ammo + " explosion");
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class Ownership_ProjectileOnHit_Patch
    {
        [HarmonyPriority(Priority.High)]
        private static void Prefix(Projectile __instance, Collider collider, bool water)
        {
            if (water || __instance.m_didHit) return;
            var tag = __instance.GetComponent<CatapultShotTag>();
            if (tag != null) Ownership.ClaimIfUnowned(collider, tag.Ammo + " impact");
        }
    }
}
