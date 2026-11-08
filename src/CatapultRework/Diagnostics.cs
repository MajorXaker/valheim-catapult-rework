using System.Text;
using HarmonyLib;
using UnityEngine;

namespace CatapultRework
{
    /// <summary>Marks a projectile fired by a catapult (used for damage ownership and debug logging).</summary>
    internal sealed class CatapultShotTag : MonoBehaviour
    {
        public string Ammo;
        public bool Aimed;
        public Vector3 AimedTarget;
    }

    /// <summary>Marks an explosion (Aoe) spawned by a catapult projectile (used for damage ownership and debug logging).</summary>
    internal sealed class CatapultBlastTag : MonoBehaviour
    {
        public string Ammo;
        public int Targets;
    }

    /// <summary>
    /// Debug-only tracing of what a catapult shot does after release: where it lands, what it hits,
    /// and for each explosion target the damage, tool tier and resistances. Read-only: nothing here
    /// changes gameplay. Shots and blasts are always tagged (Ownership.cs needs the tags); the rest
    /// only runs when DebugLogging is on.
    /// </summary>
    internal static class Diagnostics
    {
        internal static Catapult Firing;
        internal static CatapultShotTag Spawning;

        internal static string Describe(HitData.DamageTypes d)
        {
            var sb = new StringBuilder();
            void Add(string n, float v) { if (v != 0f) sb.Append(sb.Length > 0 ? ", " : "").Append(n).Append(' ').Append(ModLog.F(v)); }
            Add("pure", d.m_damage); Add("blunt", d.m_blunt); Add("slash", d.m_slash); Add("pierce", d.m_pierce);
            Add("chop", d.m_chop); Add("pickaxe", d.m_pickaxe); Add("fire", d.m_fire); Add("frost", d.m_frost);
            Add("lightning", d.m_lightning); Add("poison", d.m_poison); Add("spirit", d.m_spirit);
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        /// <summary>Kind, minimum tool tier and resistances of a damageable world object.</summary>
        internal static bool TargetInfo(GameObject go, out string kind, out int minTier, out HitData.DamageModifiers mods)
        {
            kind = null; minTier = 0; mods = default;
            if (go.TryGetComponent(out TreeBase tb)) { kind = "tree"; minTier = tb.m_minToolTier; mods = tb.m_damageModifiers; }
            else if (go.TryGetComponent(out TreeLog tl)) { kind = "log"; minTier = tl.m_minToolTier; mods = tl.m_damages; }
            else if (go.TryGetComponent(out MineRock5 m5)) { kind = "rock (MineRock5)"; minTier = m5.m_minToolTier; mods = m5.m_damageModifiers; }
            else if (go.TryGetComponent(out MineRock m)) { kind = "rock (MineRock)"; minTier = m.m_minToolTier; mods = m.m_damageModifiers; }
            else if (go.TryGetComponent(out WearNTear w)) { kind = "building piece"; minTier = w.m_minToolTier; mods = w.m_damages; }
            else if (go.TryGetComponent(out Destructible de)) { kind = "destructible"; minTier = de.m_minToolTier; mods = de.m_damages; }
            return kind != null;
        }
    }

    [HarmonyPatch(typeof(Catapult), nameof(Catapult.ShootProjectile))]
    internal static class Diag_ShootProjectile_Patch
    {
        private static void Prefix(Catapult __instance) => Diagnostics.Firing = __instance;
        private static void Postfix() => Diagnostics.Firing = null;
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class Diag_ProjectileSetup_Patch
    {
        private static void Postfix(Projectile __instance, HitData hitData, ItemDrop.ItemData item)
        {
            Catapult c = Diagnostics.Firing;
            if (c == null) return;
            var tag = __instance.gameObject.AddComponent<CatapultShotTag>();
            tag.Ammo = item?.m_shared.m_name ?? "?";
            CatapultNet net = c.GetComponent<CatapultNet>();
            tag.Aimed = net != null && net.HasPendingTarget && c.m_lockedLegs;
            if (tag.Aimed) tag.AimedTarget = net.PendingTarget;
            if (!ModLog.DebugEnabled) return;
            string spawn = __instance.m_spawnOnHit != null ? __instance.m_spawnOnHit.name : "nothing";
            ModLog.Debug($"Shot: {tag.Ammo} as '{__instance.name}', impact damage [{Diagnostics.Describe(__instance.m_damage)}], " +
                         $"tool tier {(hitData != null ? hitData.m_toolTier : 0)}, spawns on hit: {spawn}");
        }
    }

    /// <summary>Where the projectile struck and what it was.</summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class Diag_ProjectileOnHit_Patch
    {
        private static void Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water)
        {
            if (!ModLog.DebugEnabled || __instance.m_didHit) return;
            var tag = __instance.GetComponent<CatapultShotTag>();
            if (tag == null) return;
            string what;
            if (water) what = "water";
            else if (collider == null) what = "nothing";
            else
            {
                GameObject go = Projectile.FindHitObject(collider);
                bool terrain = collider.GetComponent<Heightmap>() != null;
                what = terrain ? "terrain" : $"'{(go ? go.name : collider.name)}' (layer {LayerMask.LayerToName(collider.gameObject.layer)})";
            }
            string miss = tag.Aimed ? $", {ModLog.F(Vector3.Distance(hitPoint, tag.AimedTarget))} m from the aimed point" : "";
            bool lava = ZoneSystem.instance != null && ZoneSystem.instance.IsLava(hitPoint);
            ModLog.Debug($"Impact: {tag.Ammo} hit {what} at {ModLog.F(hitPoint)}{miss}{(lava ? " (lava)" : "")}");
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.SpawnOnHit))]
    internal static class Diag_SpawnOnHit_Patch
    {
        private static void Prefix(Projectile __instance) =>
            Diagnostics.Spawning = __instance.GetComponent<CatapultShotTag>();
        private static void Postfix() => Diagnostics.Spawning = null;
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
    internal static class Diag_AoeSetup_Patch
    {
        private static void Postfix(Aoe __instance)
        {
            CatapultShotTag shot = Diagnostics.Spawning;
            if (shot == null) return;
            var tag = __instance.gameObject.AddComponent<CatapultBlastTag>();
            tag.Ammo = shot.Ammo;
            if (!ModLog.DebugEnabled) return;
            ModLog.Debug($"Explosion '{__instance.gameObject.name}': radius {ModLog.F(__instance.m_radius)} m, damage [{Diagnostics.Describe(__instance.GetDamage())}], " +
                         $"tool tier {__instance.m_toolTier}, hits props {__instance.m_hitProps}, terrain {__instance.m_hitTerrain}, characters {__instance.m_hitCharacters}");
        }
    }

    /// <summary>Each object the explosion touched, and how much of the damage it should take.</summary>
    [HarmonyPatch(typeof(Aoe), nameof(Aoe.OnHit))]
    internal static class Diag_AoeOnHit_Patch
    {
        private static void Postfix(Aoe __instance, Collider collider, bool __result)
        {
            if (!ModLog.DebugEnabled) return;
            var tag = __instance.GetComponent<CatapultBlastTag>();
            if (tag == null || collider == null) return;
            GameObject go = Projectile.FindHitObject(collider);
            if (go == null) return;
            if (go.GetComponent<Heightmap>() != null) { ModLog.Debug("  blast hit terrain"); return; }
            tag.Targets++;
            string name = go.name;
            if (Diagnostics.TargetInfo(go, out string kind, out int minTier, out HitData.DamageModifiers mods))
            {
                var h = new HitData { m_damage = __instance.GetDamage() };
                h.ApplyResistance(mods, out _);
                bool tierOk = __instance.m_toolTier >= minTier;
                ModLog.Debug($"  blast hit {kind} '{name}': needs tool tier {minTier} (blast {__instance.m_toolTier}{(tierOk ? ", ok" : ", TOO HARD")}), " +
                             $"after resistances [{Diagnostics.Describe(h.m_damage)}] = {ModLog.F(tierOk ? h.m_damage.GetTotalDamage() : 0f)} total");
            }
            else
            {
                Character ch = go.GetComponent<Character>();
                ModLog.Debug($"  blast hit {(ch ? "character" : "object")} '{name}' (handled {__result})");
            }
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.CheckHits))]
    internal static class Diag_AoeCheckHits_Patch
    {
        private static void Postfix(Aoe __instance)
        {
            if (!ModLog.DebugEnabled) return;
            var tag = __instance.GetComponent<CatapultBlastTag>();
            if (tag != null && tag.Targets == 0)
                ModLog.Debug($"  blast from {tag.Ammo} reached no damageable objects within {ModLog.F(__instance.m_radius)} m");
        }
    }
}
