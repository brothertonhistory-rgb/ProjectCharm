namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll M (free-throw rebound resolution). Builds a flat
/// seven-way pie directly from config weights — no matchup math. Structurally
/// identical to <see cref="RollIStubPieGenerator"/> (and
/// <see cref="RollKStubPieGenerator"/>'s live-ball arm).
///
/// The real attribute-driven generator (<see cref="RollMGenerator"/>) replaces this
/// in the live resolver field without touching Roll M or the resolver's routing.
/// The <see cref="Pie{TOutcome}"/> validates sum-to-one on construction, so any
/// misconfigured weights fail loudly here rather than silently warping odds.
///
/// Implements <see cref="IRollMPieGenerator"/>.
/// </summary>
public sealed class RollMStubPieGenerator : IRollMPieGenerator
{
    private readonly RollMConfig _config;

    public RollMStubPieGenerator(RollMConfig config) => _config = config;

    /// <summary>Generate the seven-way free-throw-rebound pie. <paramref name="state"/>
    /// is accepted to satisfy <see cref="IRollMPieGenerator"/> but is intentionally
    /// IGNORED — the stub returns the flat config weights regardless of possession
    /// context. Use <see cref="RollMGenerator"/> for the matchup-aware path.
    /// The <see cref="Pie{TOutcome}"/> constructor walks the enum in declaration
    /// order, so slice order is fixed for reproducibility regardless of dictionary
    /// iteration order.</summary>
    public Pie<FreeThrowReboundOutcome> Generate(PossessionState state)
    {
        var weights = new Dictionary<FreeThrowReboundOutcome, double>
        {
            [FreeThrowReboundOutcome.DefensiveRebound]       = _config.DefensiveRebound,
            [FreeThrowReboundOutcome.OffensiveRebound]       = _config.OffensiveRebound,
            [FreeThrowReboundOutcome.LooseBallFoulOnDefense] = _config.LooseBallFoulOnDefense,
            [FreeThrowReboundOutcome.LooseBallFoulOnOffense] = _config.LooseBallFoulOnOffense,
            [FreeThrowReboundOutcome.OutOfBoundsOffOffense]  = _config.OutOfBoundsOffOffense,
            [FreeThrowReboundOutcome.OutOfBoundsOffDefense]  = _config.OutOfBoundsOffDefense,
            [FreeThrowReboundOutcome.JumpBall]               = _config.JumpBall,
        };

        // The Pie constructor validates the sum is 1 within Epsilon, so any
        // misconfigured weights fail loud here rather than rolling skewed.
        return new Pie<FreeThrowReboundOutcome>(weights, _config.Epsilon);
    }
}
