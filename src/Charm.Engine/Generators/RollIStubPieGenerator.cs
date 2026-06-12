namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll I (rebound resolution). Builds a flat four-way
/// pie directly from config weights — no signal argument, no attribute model.
///
/// The real attribute-driven generator (which will tilt offensive-rebound rate
/// based on matchup, fatigue, etc.) replaces this without touching Roll I or the
/// resolver. The <see cref="Pie{TOutcome}"/> validates sum-to-one on construction,
/// so any misconfigured weights fail loudly here rather than silently warping odds.
/// </summary>
public sealed class RollIStubPieGenerator
{
    private readonly RollIConfig _config;

    public RollIStubPieGenerator(RollIConfig config) => _config = config;

    /// <summary>Generate the four-way rebound pie. No signal argument — flat
    /// weights only. The <see cref="Pie{TOutcome}"/> constructor walks the enum in
    /// declaration order, so slice order is fixed for reproducibility regardless of
    /// dictionary iteration order.</summary>
    public Pie<ReboundOutcome> Generate()
    {
        var weights = new Dictionary<ReboundOutcome, double>
        {
            [ReboundOutcome.DefensiveRebound]       = _config.DefensiveRebound,
            [ReboundOutcome.OffensiveRebound]        = _config.OffensiveRebound,
            [ReboundOutcome.LooseBallFoulOnDefense]  = _config.LooseBallFoulOnDefense,
            [ReboundOutcome.LooseBallFoulOnOffense]  = _config.LooseBallFoulOnOffense,
        };

        // The Pie constructor validates the sum is 1 within Epsilon, so any
        // misconfigured weights fail loud here rather than rolling skewed.
        return new Pie<ReboundOutcome>(weights, _config.Epsilon);
    }
}
