namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll M (free-throw rebound resolution). Builds a flat
/// seven-way pie directly from config weights — no signal argument, no attribute
/// model. Structurally identical to <see cref="RollIStubPieGenerator"/> (and
/// <see cref="RollKStubPieGenerator"/>'s live-ball arm).
///
/// The real attribute-driven generator (which will tilt the board split on size /
/// box-out / positioning along the lane — a more defensive population than a
/// field-goal board, with no shooter crashing) replaces this without touching Roll M
/// or the resolver. The <see cref="Pie{TOutcome}"/> validates sum-to-one on
/// construction, so any misconfigured weights fail loudly here rather than silently
/// warping odds.
/// </summary>
public sealed class RollMStubPieGenerator
{
    private readonly RollMConfig _config;

    public RollMStubPieGenerator(RollMConfig config) => _config = config;

    /// <summary>Generate the seven-way free-throw-rebound pie. No signal argument —
    /// flat weights only. The <see cref="Pie{TOutcome}"/> constructor walks the enum
    /// in declaration order, so slice order is fixed for reproducibility regardless of
    /// dictionary iteration order.</summary>
    public Pie<FreeThrowReboundOutcome> Generate()
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
