using System;

namespace CatapultRework
{
    /// <summary>
    /// Pure 2D ballistics that mirror Projectile.FixedUpdate exactly:
    ///   v += down * g * dt
    ///   v += -v.normalized * |v|^2 * drag * dt
    ///   p += v * dt
    /// The flight is solved in the vertical plane that contains the launch point
    /// and the target: x is horizontal distance, y is height.
    /// No Unity dependencies, so it can be unit tested outside the game.
    /// </summary>
    public static class Ballistics
    {
        public struct Params
        {
            public float Gravity;      // Projectile.m_gravity
            public float Drag;         // Projectile.m_drag
            public float Dt;           // Time.fixedDeltaTime
            public float MaxTime;      // Projectile.m_ttl (0 = unlimited, capped by MaxSimTime)
            public float ElevationRad; // fixed launch elevation (vanilla catapult arc)
        }

        public const float MaxSimTime = 30f;

        /// <summary>
        /// Simulates a shot at the given speed and returns the horizontal distance at which
        /// the projectile descends through height <paramref name="targetDy"/> (relative to launch).
        /// Returns false if it never gets there within the time limit (lifetime ran out,
        /// or the apex is below the target height).
        /// </summary>
        public static bool RangeAtHeight(in Params p, float speed, float targetDy, out float range, out float flightTime)
        {
            float vx = speed * (float)Math.Cos(p.ElevationRad);
            float vy = speed * (float)Math.Sin(p.ElevationRad);
            float x = 0f, y = 0f, t = 0f;
            float limit = p.MaxTime > 0f ? Math.Min(p.MaxTime, MaxSimTime) : MaxSimTime;

            while (t < limit)
            {
                float px = x, py = y;
                Step(p, ref vx, ref vy);
                x += vx * p.Dt;
                y += vy * p.Dt;
                t += p.Dt;

                if (vy < 0f && y <= targetDy && py > targetDy)
                {
                    float k = (py - targetDy) / (py - y);
                    range = px + (x - px) * k;
                    flightTime = t - p.Dt + p.Dt * k;
                    return true;
                }
            }
            range = 0f;
            flightTime = t;
            return false;
        }

        /// <summary>
        /// Finds the launch speed (at the fixed elevation) that lands at horizontal
        /// distance <paramref name="targetDx"/> and relative height <paramref name="targetDy"/>.
        /// Range grows monotonically with speed at a fixed angle, so bisection is reliable.
        /// </summary>
        public static bool SolveSpeed(in Params p, float targetDx, float targetDy, float maxSpeed, out float speed, out float flightTime)
        {
            speed = 0f;
            flightTime = 0f;
            if (targetDx <= 0f || maxSpeed <= 0f) return false;

            if (!RangeAtHeight(p, maxSpeed, targetDy, out float maxRange, out float maxT) || maxRange < targetDx)
                return false;

            float lo = 0f, hi = maxSpeed;
            float bestT = maxT;
            for (int i = 0; i < 40; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (RangeAtHeight(p, mid, targetDy, out float r, out float tt) && r >= targetDx)
                {
                    hi = mid;
                    bestT = tt;
                }
                else
                {
                    lo = mid;
                }
                if (hi - lo < 1e-4f) break;
            }
            speed = hi;
            flightTime = bestT;
            return true;
        }

        private static void Step(in Params p, ref float vx, ref float vy)
        {
            vy -= p.Gravity * p.Dt;
            float mag = (float)Math.Sqrt(vx * vx + vy * vy);
            if (mag > 1e-6f)
            {
                float dragDv = mag * mag * p.Drag * p.Dt;
                vx -= vx / mag * dragDv;
                vy -= vy / mag * dragDv;
            }
        }

        /// <summary>Samples trajectory points (x, y) for preview drawing.</summary>
        public static int Sample(in Params p, float speed, float[] xs, float[] ys, float stopBelowDy)
        {
            float vx = speed * (float)Math.Cos(p.ElevationRad);
            float vy = speed * (float)Math.Sin(p.ElevationRad);
            float x = 0f, y = 0f, t = 0f;
            float limit = p.MaxTime > 0f ? Math.Min(p.MaxTime, MaxSimTime) : MaxSimTime;
            int n = 0;
            xs[n] = 0f; ys[n] = 0f; n++;
            int stride = 2;
            int step = 0;
            while (t < limit && n < xs.Length)
            {
                Step(p, ref vx, ref vy);
                x += vx * p.Dt;
                y += vy * p.Dt;
                t += p.Dt;
                step++;
                bool done = vy < 0f && y <= stopBelowDy;
                if (step % stride == 0 || done)
                {
                    xs[n] = x; ys[n] = y; n++;
                }
                if (done) break;
            }
            return n;
        }

        /// <summary>
        /// Uniformly distributed random point inside a disc, given two uniform [0,1) numbers.
        /// </summary>
        public static void RandomInDisc(double u1, double u2, float radius, out float dx, out float dz)
        {
            double r = radius * Math.Sqrt(u1);
            double a = u2 * 2.0 * Math.PI;
            dx = (float)(r * Math.Cos(a));
            dz = (float)(r * Math.Sin(a));
        }
    }
}
