using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Tunable numbers for the defensive attention generator (Phase 27). Loaded from
/// the <c>"Attention"</c> section of <c>config.json</c>.
///
/// <para><b>Attention is a relative 100-point allocation</b> across the five
/// offensive players — where the defense spends, NOT how scared it is. Absolute
/// offensive danger lives in the team gravity/spacing aggregates and the openness
/// interaction.</para>
///
/// <para><b>Gravity+usage blend.</b> Both drivers are normalized to [0,1] before
/// blending so gravity (raw [0,100]) cannot overwhelm usage (raw [0,1]) ~100×.
/// Blend weights are non-negative calibration placeholders.</para>
///
/// <para><b>Asymmetric team-openness interaction (gravity enables spacing).</b>
/// GravitySource uses a sigmoid gate centered at <see cref="GravitySigmoidCenter"/>:
/// a dominant rim threat (above ~60) activates the defense-scrambling effect;
/// a moderate-gravity player contributes little. SpacingField accumulates across
/// the four non-primary-gravity players. BaseOpenness = GravitySource ×
/// (α + β × SpacingField), where α + β = 1, β &gt; α (spacing weighted higher):
/// no gravity → near-zero openness regardless of spacing; high gravity + high
/// spacing → peak openness.</para>
///
/// <para><b>All magnitudes are CALIBRATION PLACEHOLDERS.</b> The architectural
/// shapes (sigmoid gate, gravity-enables-spacing multiplication, equal-share
/// neutral for C1/C3, full-array return, normalization) are locked audit
/// corrections, not placeholders.</para>
/// </summary>
public sealed class AttentionConfig
{
    // ── Gravity+usage blend weights ──────────────────────────────────────────
    // Both normalized to [0,1] before blending (gravity /100, usage already [0,1]).
    // Non-negative; need not sum to 1 (the attention score is renormalized anyway).

    /// <summary>Weight on the gravity signal (gravity/100) in the per-player
    /// attention score. Non-negative. [CALIBRATION PLACEHOLDER]</summary>
    public double GravityBlendWeight { get; set; } = 0.5;

    /// <summary>Weight on the usage signal (FinalShares[i]) in the per-player
    /// attention score. Non-negative. [CALIBRATION PLACEHOLDER]</summary>
    public double UsageBlendWeight { get; set; } = 0.5;

    // ── Attention floor ──────────────────────────────────────────────────────

    /// <summary>Minimum attention share any populated offensive slot receives.
    /// Mirrors Roll E's UsageFloor — even a Rodman-type forces the defense to
    /// respect him at some baseline level. Invariant: ≥ 0 and
    /// 5 × AttentionFloor &lt; 1.0.</summary>
    public double AttentionFloor { get; set; } = 0.05;

    // ── Team-openness interaction parameters ─────────────────────────────────

    /// <summary>Center of the sigmoid gate for the top-gravity player.
    /// A player above this threshold strongly activates the gravity-source term;
    /// below it, the effect diminishes rapidly.
    /// Invariant: &gt; 0. [CALIBRATION PLACEHOLDER]</summary>
    public double GravitySigmoidCenter { get; set; } = 60.0;

    /// <summary>Steepness of the sigmoid gate (divisor of (top − center)).
    /// Smaller = steeper (threshold more binary); larger = gentler.
    /// Invariant: &gt; 0. [CALIBRATION PLACEHOLDER]</summary>
    public double GravitySigmoidSteepness { get; set; } = 15.0;

    /// <summary>Fraction of the gravity-source term contributed by the second-
    /// highest gravity player. Represents diminishing returns on additional creators
    /// (the rim absorbs only so much gravity). Range [0, 1).
    /// Invariant: ≥ 0 and &lt; 1. [CALIBRATION PLACEHOLDER]</summary>
    public double SecondGravityFraction { get; set; } = 0.12;

    /// <summary>Fraction of BaseOpenness gravity alone yields (α in the
    /// gravity-enables-spacing formula). When GravitySource is maxed and
    /// SpacingField = 0, openness = GravitySource × α.
    /// Invariant: ≥ 0; SpacingMultiplier must equal (1 − GravityAloneYield) to
    /// sum to 1. [CALIBRATION PLACEHOLDER]</summary>
    public double GravityAloneYield { get; set; } = 0.25;

    /// <summary>Multiplier on SpacingField in the openness formula (β in the
    /// gravity-enables-spacing formula). Spacing weighted higher than gravity
    /// alone (β &gt; α). Invariant: &gt; GravityAloneYield and
    /// GravityAloneYield + SpacingMultiplier = 1.0. [CALIBRATION PLACEHOLDER]
    /// </summary>
    public double SpacingMultiplier { get; set; } = 0.75;

    /// <summary>Tolerance for the attention-share sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static AttentionConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Attention");
        var cfg = JsonSerializer.Deserialize<AttentionConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (cfg is null)
            throw new InvalidOperationException($"Could not parse Attention config at {path}.");

        if (cfg.GravityBlendWeight < 0)
            throw new InvalidOperationException("AttentionConfig: GravityBlendWeight must be >= 0.");
        if (cfg.UsageBlendWeight < 0)
            throw new InvalidOperationException("AttentionConfig: UsageBlendWeight must be >= 0.");
        if (cfg.AttentionFloor < 0)
            throw new InvalidOperationException("AttentionConfig: AttentionFloor must be >= 0.");
        if (5.0 * cfg.AttentionFloor >= 1.0)
            throw new InvalidOperationException(
                $"AttentionConfig: 5 × AttentionFloor ({5.0*cfg.AttentionFloor:F4}) >= 1.0 — " +
                "a full five-man roster cannot satisfy the floor constraint. Lower AttentionFloor.");
        if (cfg.GravitySigmoidCenter <= 0)
            throw new InvalidOperationException("AttentionConfig: GravitySigmoidCenter must be > 0.");
        if (cfg.GravitySigmoidSteepness <= 0)
            throw new InvalidOperationException("AttentionConfig: GravitySigmoidSteepness must be > 0.");
        if (cfg.SecondGravityFraction < 0 || cfg.SecondGravityFraction >= 1.0)
            throw new InvalidOperationException("AttentionConfig: SecondGravityFraction must be in [0,1).");
        if (cfg.GravityAloneYield < 0)
            throw new InvalidOperationException("AttentionConfig: GravityAloneYield must be >= 0.");
        if (cfg.SpacingMultiplier <= cfg.GravityAloneYield)
            throw new InvalidOperationException(
                "AttentionConfig: SpacingMultiplier must be > GravityAloneYield (spacing weighted higher).");
        if (Math.Abs(cfg.GravityAloneYield + cfg.SpacingMultiplier - 1.0) > 1e-9)
            throw new InvalidOperationException(
                $"AttentionConfig: GravityAloneYield + SpacingMultiplier must equal 1.0 " +
                $"(got {cfg.GravityAloneYield + cfg.SpacingMultiplier:F6}).");

        return cfg;
    }
}
