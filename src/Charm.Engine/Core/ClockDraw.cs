namespace Charm.Engine;

/// <summary>
/// The game-clock time draw — the engine's first CONTINUOUS roll (every other roll
/// draws a discrete slice from a <see cref="Pie{TOutcome}"/>; this samples a real
/// number of seconds). A truncated normal: a Box-Muller standard-normal scaled to
/// (center, sd), reject-and-resampled into <c>[floor, ceiling)</c>. The ceiling is
/// EXCLUSIVE so the upper tail thins smoothly toward the shot clock and nothing piles
/// at exactly the cap — the exact-clock "no shot" case is owned by the shot-clock
/// VIOLATION terminal (its own invariant ElapsedSeconds), not by this draw.
/// <para>The clamp at the clock is what produces the asymmetry: a low (fast) center
/// keeps a real right tail toward the cap, a high (slow) center gets its right tail
/// clipped by the cap and leans short. Center is the pace knob; sd tunes the tails.
/// A future coach pace (1-10) attribute shifts the center; today it is a config stub.</para>
/// </summary>
public static class ClockDraw
{
    /// <summary>One truncated-normal draw of seconds in <c>[floor, ceiling)</c>,
    /// centered at <paramref name="center"/> with spread <paramref name="sd"/>.</summary>
    public static double Sample(IRng rng, double center, double sd, double floor, double ceiling)
    {
        // Box-Muller + reject-resample. With sane params (center well inside the band)
        // the rejection rate is tiny; the attempt guard only exists so a pathological
        // config can never spin forever — it falls back to the clamped center.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var u1 = rng.NextUnitInterval();
            var u2 = rng.NextUnitInterval();
            if (u1 < 1e-12) u1 = 1e-12; // guard the log
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            var x = center + sd * z;
            if (x >= floor && x < ceiling) return x;
        }
        return Math.Clamp(center, floor, Math.BitDecrement(ceiling));
    }
}
