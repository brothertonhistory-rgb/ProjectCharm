namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll G. Returns the configured base weights as a
/// finished five-way pie over <see cref="ShotLocation"/>. Realistic placeholder
/// weights this session (roughly real D1 attempt shares), and — like Roll E and
/// Roll F — with NO live-wire scalar.
///
/// Why no live wire: the only thing that tilts Roll G's pie is the deferred
/// player/attribute model (shot selection, role, defensive pressure). A
/// placeholder wire here would pantomime the exact signal that is deliberately
/// deferred. So, exactly like Roll E's selection generator and Roll F's action
/// generator, this generator takes no signal argument.
///
/// This is the seam, real and flat-ish: the Pie validates on construction (bad
/// weights fail loud), and the real attribute-driven generator replaces this class
/// later WITHOUT touching Roll G or the resolver — it just hands back a non-flat
/// pie over the same enum.
/// </summary>
public sealed class RollGStubPieGenerator : IRollGGenerationProvider
{
    private readonly RollGConfig _cfg;

    public RollGStubPieGenerator(RollGConfig cfg) => _cfg = cfg;

    /// <summary>
    /// Richer provider method — returns the existing flat pie plus residual 0.0.
    /// Called by the resolver so all 20 harness Resolver construction sites compile
    /// against the widened interface type. The stub has no usage-pressure logic;
    /// residual is always zero.
    /// </summary>
    public RollGGeneration GenerateWithResidual(PossessionState state) =>
        new(Generate(state), 0.0);

    /// <param name="state">Carried for signature parity with real generators; the
    /// stub does not read it yet. The real generator will use it (and the selected
    /// slot it carries) to weight zones by the shooter's role and the matchup.</param>
    public Pie<ShotLocation> Generate(PossessionState state)
    {
        var weights = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Three] = _cfg.BaseThree,
            [ShotLocation.Long] = _cfg.BaseLong,
            [ShotLocation.Mid] = _cfg.BaseMid,
            [ShotLocation.Short] = _cfg.BaseShort,
            [ShotLocation.Rim] = _cfg.BaseRim,
        };

        // No nudge, so no renormalize step is strictly needed — but the Pie
        // constructor still validates the sum is 1 within Epsilon, so weights that
        // don't add up fail loud rather than rolling skewed.
        return new Pie<ShotLocation>(weights, _cfg.Epsilon);
    }
}
