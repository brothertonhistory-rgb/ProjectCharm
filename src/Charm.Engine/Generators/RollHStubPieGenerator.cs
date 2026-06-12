namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll H. Returns the configured base weights as a
/// finished six-way pie over <see cref="ShotResult"/>. Realistic placeholder
/// weights this session, and — like Roll E, F, and G — with NO live-wire scalar.
///
/// Why no live wire: the only thing that tilts Roll H's pie is the deferred
/// player/attribute model — the shooter-vs-defender matchup, the other-four
/// defensive-attention (gravity) term, the skill/athleticism gates (athleticism
/// gates skill, not additive), the bounded logistic make/miss mapping, and shot
/// quality folded into the make percentage. Roll H sits exactly where that model
/// expresses, so a placeholder wire here would pantomime the precise signal being
/// deferred. The stub is also location-BLIND: it does not read ShotType, so the
/// flat-ish weights are an average across zones, not a per-zone FG%. So, like the
/// E/F/G generators, this generator takes no signal argument.
///
/// This is the seam, real and flat: the Pie validates on construction (bad
/// weights fail loud), and the real attribute-driven generator replaces this
/// class later WITHOUT touching Roll H or the resolver — it just hands back a
/// non-flat pie over the same enum (and, when it lands, one that reads the carried
/// SelectedSlot + ShotType to tilt the make %).
/// </summary>
public sealed class RollHStubPieGenerator
{
    private readonly RollHConfig _cfg;

    public RollHStubPieGenerator(RollHConfig cfg) => _cfg = cfg;

    /// <param name="state">Carried for signature parity with real generators;
    /// the stub does not read it yet. The real generator will use it (the selected
    /// slot it carries and the stamped shot zone) to weight by the matchup's
    /// attributes.</param>
    public Pie<ShotResult> Generate(PossessionState state)
    {
        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made] = _cfg.BaseMade,
            [ShotResult.MadeAndFouled] = _cfg.BaseMadeAndFouled,
            [ShotResult.Miss] = _cfg.BaseMiss,
            [ShotResult.MissFouled] = _cfg.BaseMissFouled,
            [ShotResult.MissOutOfBoundsLost] = _cfg.BaseMissOutOfBoundsLost,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.BaseMissOutOfBoundsRetained,
        };

        // No nudge, so no renormalize step is strictly needed — but the Pie
        // constructor still validates the sum is 1 within Epsilon, so weights
        // that don't add up fail loud rather than rolling skewed.
        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }
}
