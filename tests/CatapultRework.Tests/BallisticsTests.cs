using System;
using CatapultRework;
using Xunit;

public class BallisticsTests
{
    private static Ballistics.Params P(float drag = 0f, float ttl = 0f, float elevDeg = 45f, float g = 9.81f) => new Ballistics.Params
    {
        Gravity = g, Drag = drag, Dt = 0.02f, MaxTime = ttl, ElevationRad = elevDeg * (float)Math.PI / 180f,
    };

    [Fact]
    public void NoDrag_MatchesAnalyticRange()
    {
        var p = P();
        float v = 30f;
        Assert.True(Ballistics.RangeAtHeight(p, v, 0f, out float r, out _));
        double analytic = v * v * Math.Sin(2 * p.ElevationRad) / p.Gravity; // ~91.7 m
        Assert.InRange(r, analytic * 0.98, analytic * 1.02);
    }

    [Theory]
    [InlineData(0f, 60f, 0f)]
    [InlineData(0.001f, 50f, -5f)]
    [InlineData(0.002f, 40f, 8f)]
    [InlineData(0.0005f, 80f, -20f)]
    public void Solver_HitsTarget(float drag, float dx, float dy)
    {
        var p = P(drag, 0f, 35f);
        Assert.True(Ballistics.SolveSpeed(p, dx, dy, 200f, out float speed, out _));
        Assert.True(Ballistics.RangeAtHeight(p, speed, dy, out float r, out _));
        Assert.InRange(r, dx - 0.05f, dx + 0.05f);
    }

    [Fact]
    public void Range_IsMonotonicInSpeed()
    {
        var p = P(0.002f, 0f, 40f);
        float last = 0f;
        for (float v = 5f; v <= 80f; v += 5f)
        {
            Assert.True(Ballistics.RangeAtHeight(p, v, 0f, out float r, out _));
            Assert.True(r > last, $"range must grow with speed (v={v}, r={r}, last={last})");
            last = r;
        }
    }

    [Fact]
    public void BeyondMaxSpeed_IsUnreachable()
    {
        var p = P();
        Ballistics.RangeAtHeight(p, 30f, 0f, out float maxR, out _);
        Assert.False(Ballistics.SolveSpeed(p, maxR + 5f, 0f, 30f, out _, out _));
        Assert.True(Ballistics.SolveSpeed(p, maxR - 5f, 0f, 30f, out _, out _));
    }

    [Fact]
    public void Uphill_ReducesReach()
    {
        var p = P();
        Ballistics.RangeAtHeight(p, 30f, 0f, out float flat, out _);
        Ballistics.RangeAtHeight(p, 30f, 15f, out float up, out _);
        Assert.True(up < flat);
        Assert.False(Ballistics.SolveSpeed(p, flat - 1f, 15f, 30f, out _, out _));
    }

    [Fact]
    public void AboveApex_IsUnreachable()
    {
        var p = P();
        // apex for v=20 at 45°: (20*sin45)^2/(2g) ~ 10.2 m
        Assert.False(Ballistics.RangeAtHeight(p, 20f, 12f, out _, out _));
    }

    [Fact]
    public void ProjectileLifetime_LimitsRange()
    {
        // At 45° and 60 m/s the flight takes ~8.6 s; with a 4 s lifetime it must be rejected.
        var p = P(0f, 4f);
        Assert.False(Ballistics.RangeAtHeight(p, 60f, 0f, out _, out _));
        Assert.True(Ballistics.RangeAtHeight(p, 25f, 0f, out _, out float t)); // ~3.6 s
        Assert.True(t < 4f);
    }

    [Fact]
    public void RandomInDisc_StaysInsideAndCoversArea()
    {
        var rng = new Random(1);
        int inner = 0, n = 20000;
        for (int i = 0; i < n; i++)
        {
            Ballistics.RandomInDisc(rng.NextDouble(), rng.NextDouble(), 5f, out float x, out float z);
            float r = (float)Math.Sqrt(x * x + z * z);
            Assert.True(r <= 5.0001f);
            if (r < 2.5f) inner++;
        }
        // Uniform over area: a quarter of points fall inside half the radius.
        Assert.InRange(inner / (double)n, 0.23, 0.27);
    }
}
