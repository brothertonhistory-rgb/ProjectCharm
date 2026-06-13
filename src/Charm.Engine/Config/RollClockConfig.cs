using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable tempo number lives here — nothing is hardcoded in logic. Loaded
/// from the <c>"Clock"</c> section of config.json, exactly like the per-roll configs.
///
/// <para>These are <b>starting knobs</b>, not calibrated values. Realized APL will run
/// a touch above <see cref="Center"/> because offensive-rebound reset periods add time
/// to roughly 12% of possessions; Emmett tunes <see cref="Center"/> down to hit a
/// target blended APL by watching the harness histogram.</para>
///
/// <para>FUTURE SEAM — coach pace attribute. A future pace (1–10) per team shifts the
/// center passed to <see cref="ClockDraw.Sample"/>: pace 1 → ~19s, pace 5 → ~17s,
/// pace 10 → ~14s. No engine change at that point; only the center received here
/// changes.</para>
/// </summary>
public sealed class RollClockConfig
{
    /// <summary>The pace-5 (average) center of the full-clock draw in seconds.
    /// The pace seam: a future coach pace attribute shifts this per team; today it
    /// is the stub average for all teams.</summary>
    public double Center { get; set; } = 17.0;

    /// <summary>Spread of the truncated normal; tunes how fat the tails are (how
    /// often a center-17 possession reaches the high-20s or dips below 10s).</summary>
    public double StdDev { get; set; } = 4.5;

    /// <summary>Minimum possession length in seconds. You cannot get a shot off
    /// in under a few seconds; this hard floor keeps the distribution honest.</summary>
    public double Floor { get; set; } = 4.0;

    /// <summary>The fresh shot-clock ceiling in seconds (exclusive). Period 1 of
    /// every possession draws from <c>[Floor, FullClockSeconds)</c>. Exclusive so
    /// the upper tail thins smoothly and nothing piles at exactly 30 — the exact-30
    /// case is the shot-clock-violation terminal, which carries its own invariant
    /// ElapsedSeconds and never reaches this draw.</summary>
    public double FullClockSeconds { get; set; } = 30.0;

    /// <summary>The offensive-rebound reset-clock ceiling in seconds. Each reset
    /// period (one per offensive rebound) draws from <c>[Floor, ResetClockSeconds)</c>
    /// with center and sd scaled by <c>ResetClockSeconds / FullClockSeconds</c>
    /// (≈ 0.667), so a ~17-of-30 segment becomes a ~11-of-20 segment.</summary>
    public double ResetClockSeconds { get; set; } = 20.0;

    public static RollClockConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Clock");
        var cfg = JsonSerializer.Deserialize<RollClockConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse Clock config at {path}.");
    }
}
