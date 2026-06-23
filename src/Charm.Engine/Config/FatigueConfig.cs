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

    /// <summary>How much a fully-gassed player's EFFECTIVE athleticism is discounted on
    /// OFFENSE. The discount is linear in the meter: <c>1 − OffenseAthleticismDrop ×
    /// (level/Ceiling)</c>, so a fresh player plays at full athleticism and a fully-gassed
    /// one bottoms at <c>(1 − OffenseAthleticismDrop)</c> of his authored athleticism (never
    /// below, never zero). The convex trickle-then-cliff lives in the METER, so this stays a
    /// straight line. Must be &gt;= 0 and &lt; 1. Must be &lt; <see cref="DefenseAthleticismDrop"/>
    /// (defense degrades faster) — except the all-zero inertness control. [CALIBRATION PLACEHOLDER]</summary>
    public double OffenseAthleticismDrop { get; set; } = 0.10;

    /// <summary>How much a fully-gassed player's EFFECTIVE athleticism is discounted on
    /// DEFENSE — STEEPER than offense, because a tired player loses a first step and a slide
    /// on defense faster than he loses them on offense. Same linear-in-meter shape:
    /// <c>1 − DefenseAthleticismDrop × (level/Ceiling)</c>, bottoming at
    /// <c>(1 − DefenseAthleticismDrop)</c>. Must be &gt;= 0 and &lt; 1, and must EXCEED
    /// <see cref="OffenseAthleticismDrop"/> (structural invariant, not a calibration choice) —
    /// except the all-zero inertness control. [CALIBRATION PLACEHOLDER]</summary>
    public double DefenseAthleticismDrop { get; set; } = 0.20;

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

        // Athleticism-drop bounds: each must be in [0, 1) so its floor (1 − drop) is positive
        // — a fully-gassed player is a step slow, never a statue, never below zero.
        if (cfg.OffenseAthleticismDrop < 0)
            throw new InvalidOperationException(
                $"Fatigue OffenseAthleticismDrop must be >= 0 (got {cfg.OffenseAthleticismDrop}).");
        if (cfg.DefenseAthleticismDrop < 0)
            throw new InvalidOperationException(
                $"Fatigue DefenseAthleticismDrop must be >= 0 (got {cfg.DefenseAthleticismDrop}).");
        if (cfg.OffenseAthleticismDrop >= 1)
            throw new InvalidOperationException(
                $"Fatigue OffenseAthleticismDrop must be < 1 (got {cfg.OffenseAthleticismDrop}).");
        if (cfg.DefenseAthleticismDrop >= 1)
            throw new InvalidOperationException(
                $"Fatigue DefenseAthleticismDrop must be < 1 (got {cfg.DefenseAthleticismDrop}).");

        // Defense degrades FASTER than offense — a structural invariant, not a calibration
        // choice. The ONLY permitted exception is the all-zero pair, the inertness control
        // used to prove zero-drop equivalence with the Phase-48 baseline (effect fully off).
        var inert = cfg.OffenseAthleticismDrop == 0.0 && cfg.DefenseAthleticismDrop == 0.0;
        if (!inert && cfg.DefenseAthleticismDrop <= cfg.OffenseAthleticismDrop)
            throw new InvalidOperationException(
                "DefenseAthleticismDrop must exceed OffenseAthleticismDrop unless both drops are 0 (inertness control).");

        return cfg;
    }
}
