using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Roll E's tunable numbers, loaded from the <c>"RollE"</c> section of
/// <c>config.json</c>. Mirrors <c>RollCConfig</c> / <c>RollDConfig</c>: a plain
/// settings record with a <see cref="Load"/> that reads its own section.
///
/// The five base weights are FLAT (0.20 each) this session — the explicit,
/// visible, tunable expression of "no signal yet." They are written out one per
/// slot (rather than computed as 1/5) so the seam is real: the person can see and
/// change each weight, and a future attribute-driven generator overwrites these
/// numbers without any "uniform mode" flag to flip.
///
/// There is NO live-wire scalar here (unlike Roll B's physicality and Roll C's
/// pressure). Selection's first real signal is usage, which is part of the
/// deferred attribute model — so, like Roll D's flavor generator, there is
/// nothing functional for a signal to move yet, and adding one would falsely
/// imply a signal exists.
/// </summary>
public sealed class RollEConfig
{
    public double BaseSlot1 { get; init; }
    public double BaseSlot2 { get; init; }
    public double BaseSlot3 { get; init; }
    public double BaseSlot4 { get; init; }
    public double BaseSlot5 { get; init; }

    // Transition selection weights — the pie Roll E draws when the possession carries
    // FastBreak=true (Roll J pushed). PLACEHOLDER this session and deliberately NOT
    // flat: visibly different from the Base* 20s so the harness can PROVE the break
    // path draws its own pie. The real speed/athleticism tilt is the deferred attribute
    // seam (a smarter generator), exactly like the flat Base* weights.
    public double TransitionSlot1 { get; init; }
    public double TransitionSlot2 { get; init; }
    public double TransitionSlot3 { get; init; }
    public double TransitionSlot4 { get; init; }
    public double TransitionSlot5 { get; init; }

    public double Epsilon { get; init; }

    // ── Usage-driven selection parameters (Phase 15) ─────────────────────────
    // All are calibration placeholders; tune against the harness's Roll E batch
    // check and observed usage distributions.

    /// <summary>Sharpening exponent applied to raw usage scores before normalizing.
    /// Higher values tilt the distribution harder toward the best scorer.
    /// Invariant: &gt; 0.
    /// Calibration anchor: at 2.0, a realistic D1 alpha lands ~35% selection share.
    /// Coupled with <see cref="UsageFloor"/> — raise the floor and the alpha's ceiling
    /// falls even at a fixed exponent; calibrate together.</summary>
    public double UsageExponent { get; init; }

    /// <summary>Minimum guaranteed selection share for any populated slot.
    /// Anchored to the Rodman-era NBA floor (~8–9% USG); college compresses toward
    /// the mean so ~9–10% is the D1 floor. For a normal starter the floor never
    /// binds — attributes carry him above it.
    /// Invariant: ≥ 0. Feasibility invariant: 5 * UsageFloor &lt; 1.0.</summary>
    public double UsageFloor { get; init; }

    /// <summary>Hard cap on any single slot's selection share. Only ever reached by
    /// absurd talent gaps (one elite among four scrubs). Realistic rosters never
    /// approach it — the floor hands teammates enough shots to suppress the alpha's
    /// raw ceiling naturally.
    /// Invariant: &gt; UsageFloor and ≤ 1.0.</summary>
    public double UsageRail { get; init; }

    /// <summary>Strictly-positive guard applied to the raw usage score before the
    /// sharpening exponent. Prevents a degenerate all-zero test player from collapsing
    /// the power-law math (score = 0 raised to any positive exponent = 0, which is
    /// fine, but max(score, MinUsageScore) keeps things well-behaved).
    /// Invariant: &gt; 0.</summary>
    public double MinUsageScore { get; init; }

    /// <summary>Load the <c>"RollE"</c> section from the config file at
    /// <paramref name="path"/>. Mirrors the other rolls' loaders.</summary>
    public static RollEConfig Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var e = doc.RootElement.GetProperty("RollE");

        var cfg = new RollEConfig
        {
            BaseSlot1 = e.GetProperty("BaseSlot1").GetDouble(),
            BaseSlot2 = e.GetProperty("BaseSlot2").GetDouble(),
            BaseSlot3 = e.GetProperty("BaseSlot3").GetDouble(),
            BaseSlot4 = e.GetProperty("BaseSlot4").GetDouble(),
            BaseSlot5 = e.GetProperty("BaseSlot5").GetDouble(),
            TransitionSlot1 = e.GetProperty("TransitionSlot1").GetDouble(),
            TransitionSlot2 = e.GetProperty("TransitionSlot2").GetDouble(),
            TransitionSlot3 = e.GetProperty("TransitionSlot3").GetDouble(),
            TransitionSlot4 = e.GetProperty("TransitionSlot4").GetDouble(),
            TransitionSlot5 = e.GetProperty("TransitionSlot5").GetDouble(),
            Epsilon       = e.GetProperty("Epsilon").GetDouble(),
            UsageExponent = e.GetProperty("UsageExponent").GetDouble(),
            UsageFloor    = e.GetProperty("UsageFloor").GetDouble(),
            UsageRail     = e.GetProperty("UsageRail").GetDouble(),
            MinUsageScore = e.GetProperty("MinUsageScore").GetDouble(),
        };

        // ── Invariant validation — fail loud on bad config ───────────────────
        if (cfg.UsageExponent <= 0)
            throw new InvalidOperationException(
                $"RollEConfig: UsageExponent must be > 0 (got {cfg.UsageExponent}).");
        if (cfg.UsageFloor < 0)
            throw new InvalidOperationException(
                $"RollEConfig: UsageFloor must be >= 0 (got {cfg.UsageFloor}).");
        if (cfg.UsageRail <= cfg.UsageFloor)
            throw new InvalidOperationException(
                $"RollEConfig: UsageRail ({cfg.UsageRail}) must be > UsageFloor ({cfg.UsageFloor}).");
        if (cfg.UsageRail > 1.0)
            throw new InvalidOperationException(
                $"RollEConfig: UsageRail must be <= 1.0 (got {cfg.UsageRail}).");
        if (cfg.MinUsageScore <= 0)
            throw new InvalidOperationException(
                $"RollEConfig: MinUsageScore must be > 0 (got {cfg.MinUsageScore}).");
        if (5 * cfg.UsageFloor >= 1.0)
            throw new InvalidOperationException(
                $"RollEConfig: 5 * UsageFloor ({5 * cfg.UsageFloor:F4}) >= 1.0 — " +
                "a full five-man roster cannot satisfy the floor constraint. Lower UsageFloor.");

        return cfg;
    }
}
