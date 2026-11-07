using UnityEngine;

namespace CatapultRework
{
    /// <summary>Reads the vanilla catapult's geometry and projectile physics.</summary>
    internal static class CatapultMath
    {
        public static bool LegsReady(Catapult c) =>
            c != null && c.m_lockedLegs && !c.m_movingLegs;

        public static bool IsLoaded(Catapult c) =>
            c != null && c.m_loadedItem != null && c.m_armAnimTime == 0f;

        /// <summary>Vanilla launch vector (unnormalized): force point minus catapult origin.</summary>
        public static Vector3 VanillaLaunchVector(Catapult c) =>
            c.m_forceVector.transform.position - c.transform.position;

        /// <summary>Horizontal firing direction of the catapult ("front" for the aiming cone).</summary>
        public static Vector3 Front(Catapult c)
        {
            Vector3 v = VanillaLaunchVector(c);
            v.y = 0f;
            if (v.sqrMagnitude < 1e-6f) v = c.transform.forward;
            v.y = 0f;
            return v.normalized;
        }

        /// <summary>Elevation angle of the vanilla arc, kept fixed by the rework.</summary>
        public static float ElevationRad(Catapult c)
        {
            Vector3 v = VanillaLaunchVector(c);
            float h = new Vector2(v.x, v.z).magnitude;
            return Mathf.Atan2(v.y, Mathf.Max(h, 1e-4f));
        }

        /// <summary>
        /// Vanilla launch speed without the random +0..10% variation.
        /// Vanilla: velocity = launchVector * defaultAmmo.projectileVel * Random(1, 1 + variation).
        /// Note that vanilla always uses the catapult's default ammo for this, whatever is loaded.
        /// </summary>
        public static float VanillaSpeed(Catapult c)
        {
            float vel = c.m_defaultAmmo != null ? c.m_defaultAmmo.m_itemData.m_shared.m_attack.m_projectileVel : 1f;
            return VanillaLaunchVector(c).magnitude * vel;
        }

        public static float MaxSpeed(Catapult c) => VanillaSpeed(c) * ModConfig.MaxRangeMultiplier.Value;

        /// <summary>Same projectile selection as Catapult.ShootProjectile.</summary>
        public static Projectile ProjectileFor(Catapult c, ItemDrop.ItemData item)
        {
            Projectile projectile = c.m_projectile;
            if (item == null) return projectile;
            bool included = false;
            foreach (ItemDrop x in c.m_includeItemsOverride)
            {
                if (x != null && x.m_itemData.m_shared.m_name == item.m_shared.m_name) { included = true; break; }
            }
            if ((!c.m_onlyUseIncludedProjectiles || included) && item.m_shared.m_attack.m_attackProjectile != null)
            {
                Projectile p = item.m_shared.m_attack.m_attackProjectile.GetComponent<Projectile>();
                if (p != null) projectile = p;
            }
            return projectile;
        }

        public static Ballistics.Params ParamsFor(Catapult c, Projectile proj) => new Ballistics.Params
        {
            Gravity = proj != null ? proj.m_gravity : 9.81f,
            Drag = proj != null ? proj.m_drag : 0f,
            Dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f,
            MaxTime = proj != null ? proj.m_ttl : 0f,
            ElevationRad = ElevationRad(c),
        };

        /// <summary>
        /// Estimated world position of the shoot point at the moment of release, while the arm is at rest.
        /// Uses the actual release offset once one shot has been fired by this client, otherwise
        /// rotates the shoot point by the arm animation value at release time.
        /// </summary>
        public static Vector3 EstimatedLaunchPoint(Catapult c, CatapultNet net)
        {
            if (net != null && net.HasCachedLaunchOffset)
                return c.transform.TransformPoint(net.CachedLaunchOffset);

            Transform arm = c.m_arm.transform;
            Transform parent = arm.parent;
            float a = c.m_armAnimation != null ? c.m_armAnimation.Evaluate(c.m_releaseAnimationTime) : 1f;
            Vector3 rest = c.m_armRotation;
            Quaternion releaseLocal = Quaternion.Euler(rest.x + a * c.m_armAnimationDegrees, rest.y, rest.z);
            Quaternion parentRot = parent != null ? parent.rotation : Quaternion.identity;

            // Shoot point relative to the arm pivot, expressed in the arm's current frame.
            Vector3 rel = Quaternion.Inverse(arm.rotation) * (c.m_shootPoint.position - arm.position);
            return arm.position + parentRot * releaseLocal * rel;
        }

        public static float GroundHeight(Vector3 p)
        {
            if (ZoneSystem.instance != null)
            {
                if (ZoneSystem.instance.GetSolidHeight(p, out float h, 1000)) return h;
                if (ZoneSystem.instance.GetGroundHeight(p, out h)) return h;
            }
            return p.y;
        }

        /// <summary>Velocity that lands on <paramref name="target"/> from <paramref name="launch"/>.</summary>
        public static bool SolveVelocity(in Ballistics.Params p, Vector3 launch, Vector3 target, float maxSpeed,
            out Vector3 velocity, out float flightTime)
        {
            Vector3 d = target - launch;
            Vector3 dirH = new Vector3(d.x, 0f, d.z);
            float dx = dirH.magnitude;
            dirH = dx > 1e-4f ? dirH / dx : Vector3.forward;
            bool ok = Ballistics.SolveSpeed(p, dx, d.y, maxSpeed, out float speed, out flightTime);
            if (!ok) speed = maxSpeed;
            velocity = dirH * (speed * Mathf.Cos(p.ElevationRad)) + Vector3.up * (speed * Mathf.Sin(p.ElevationRad));
            return ok;
        }
    }
}
