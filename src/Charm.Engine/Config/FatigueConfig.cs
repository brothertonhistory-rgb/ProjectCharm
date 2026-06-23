using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Tunable knobs for the per-player fatigue meter, loaded from the <c>"Fatigue"</c> section
/// of config.json exactly like the per-roll configs. Every value here is a PLACEHOLDER this
/// session — the fatigue SHAPE (convex drain, Endurance scaling both directions, partial
/// multiplicative recovery, halftime as one large recovery) is locked; the magnitudes are
/// not calibrated and tune later, once a generated population and minutes patterns exist.
/// </summary>
public sealed class FatigueConfig
{
    /// <summary>The fatigue ceiling (0 = fresh). The level is clamped here, and the convex
    /// term is normalized by it. Must be &gt; 0. [CALIBRATION PLACEHOLDER]</summary>
    public double Ceiling { get; set; } = 100.0;

    /// <summary>The base per-possession drain step, before Endurance and convexity scaling —
    /// how much a fresh, average-Endurance player tires on one possession. Must be &gt; 0.
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double BaseDrain { get; set; } = 0.5;

    /// <summary>Steepness of the cliff: how much faster a tired player tires than a fresh
    /// one. The convex multiplier is <c>(1 + Convexity × (level/Ceiling)^Exponent)</c>, so a
    /// larger value makes the bottom fall out harder as fatigue rises. Must be &gt;= 0.
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double Convexity { get; set; } = 4.0;

    /// <summary>The exponent on the normalized level inside the convex term — the SHAPE of
    /// the cliff (higher = flatter early, sharper late). Must be &gt; 1 so the increment
    /// strictly steepens with fatigue. [CALIBRATION PLACEHOLDER]</summary>
    public double Exponent { get; set; } = 2.0;

    /// <summary>How strongly Endurance changes the DRAIN. The drain multiplier is
    /// <c>(1 + DrainEnduranceSensitivity × (1 − Endurance/100))</c>: 1.0 at Endurance 100,
    /// larger as Endurance falls, so a low-Endurance player drains faster. Must be &gt;= 0.
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double DrainEnduranceSensitivity { get; set; } = 1.0;

    /// <summary>The base recovery rate per elapsed bench-second, before Endurance scaling.
    /// The recovery multiplier is <c>(1 − RecoveryRate × recoveryFactor × elapsedSeconds)</c>,
    /// clamped to [0,1]. Must be &gt; 0. [CALIBRATION PLACEHOLDER]</summary>
    public double RecoveryRate { get; set; } = 0.0015;

    /// <summary>How strongly Endurance changes RECOVERY. The recovery multiplier's Endurance
    /// term is <c>(1 + RecoveryEnduranceSensitivity × Endurance/100)</c>: 1.0 at Endurance 0,
    /// larger as Endurance rises, so a high-Endurance player recovers faster. Must be &gt;= 0.
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double RecoveryEnduranceSensitivity { get; set; } = 1.0;

    /// <summary>The rest DURATION (in elapsed game-clock seconds) that halftime is worth,
    /// fed into the SAME multiplicative recovery primitive as a normal rest. This is a large
    /// rest-equivalent — NOT an additive "halftime recovery amount" — deliberately, so no
    /// special-case subtract-at-halftime branch can exist. Must be &gt; 0.
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double HalftimeRestEquivalentSeconds { get; set; } = 300.0;

    public static FatigueConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Fatigue");
        var cfg = JsonSerializer.Deserialize<FatigueConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not parse Fatigue config at {path}.");

        if (cfg.Ceiling <= 0)
            throw new InvalidOperationException($"Fatigue Ceiling must be positive (got {cfg.Ceiling}).");
        if (cfg.BaseDrain <= 0)
            throw new InvalidOperationException($"Fatigue BaseDrain must be positive (got {cfg.BaseDrain}).");
        if (cfg.Convexity < 0)
            throw new InvalidOperationException($"Fatigue Convexity must be >= 0 (got {cfg.Convexity}).");
        if (cfg.Exponent <= 1)
            throw new InvalidOperationException($"Fatigue Exponent must be > 1 (got {cfg.Exponent}).");
        if (cfg.DrainEnduranceSensitivity < 0)
            throw new InvalidOperationException(
                $"Fatigue DrainEnduranceSensitivity must be >= 0 (got {cfg.DrainEnduranceSensitivity}).");
        if (cfg.RecoveryRate <= 0)
            throw new InvalidOperationException($"Fatigue RecoveryRate must be positive (got {cfg.RecoveryRate}).");
        if (cfg.RecoveryEnduranceSensitivity < 0)
            throw new InvalidOperationException(
                $"Fatigue RecoveryEnduranceSensitivity must be >= 0 (got {cfg.RecoveryEnduranceSensitivity}).");
        if (cfg.HalftimeRestEquivalentSeconds <= 0)
            throw new InvalidOperationException(
                $"Fatigue HalftimeRestEquivalentSeconds must be positive (got {cfg.HalftimeRestEquivalentSeconds}).");

        return cfg;
    }
}
