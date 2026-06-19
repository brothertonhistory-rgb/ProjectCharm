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
/// <para>FUTURE SEAM — coach pace attribute. Live in Phase 30: <see cref="PaceCenterScale"/>
/// controls how far the center shifts per coach pace unit. Calibration placeholder default.</para>
/// </summary>
public sealed class RollClockConfig
{
    /// <summary>The pace-5 (average) center of the full-clock draw in seconds.
    /// Shifted per-possession by the offensive coach's PaceBias (via
    /// <see cref="PaceCenterScale"/>) before passing to <see cref="ClockDraw.Sample"/>.</summary>
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

    /// <summary>Maximum center adjustment (seconds) applied by the offensive coach's
    /// <see cref="CoachProfile.PaceBias"/>. At PaceBias=10 (fastest): center shifts
    /// by −1.0 × PaceCenterScale. At PaceBias=1 (slowest): center shifts by +0.8 ×
    /// PaceCenterScale. At PaceBias=5 (neutral): no shift.
    /// Invariant: &gt;= 0. [CALIBRATION PLACEHOLDER]</summary>
    public double PaceCenterScale { get; set; } = 1.5;

    public static RollClockConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Clock");
        var cfg = JsonSerializer.Deserialize<RollClockConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not parse Clock config at {path}.");
        if (cfg.PaceCenterScale < 0)
            throw new InvalidOperationException(
                $"Clock PaceCenterScale must be >= 0 (got {cfg.PaceCenterScale}).");
        return cfg;
    }
}
