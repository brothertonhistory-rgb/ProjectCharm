namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll K (offensive-rebound resolution). Builds a flat
/// seven-way pie directly from config weights — no signal argument, no attribute
/// model. Structurally identical to <see cref="RollIStubPieGenerator"/>.
///
/// The real attribute-driven generator (which will tilt the putback-vs-reset
/// share, and the putback rate itself, on WHO grabbed the board — size,
/// athleticism, rim rating, the matchup) replaces this without touching Roll K or
/// the resolver. The <see cref="Pie{TOutcome}"/> validates sum-to-one on
/// construction, so any misconfigured weights fail loudly here rather than silently
/// warping odds.
/// </summary>
public sealed class RollKStubPieGenerator
{
    private readonly RollKConfig _config;

    public RollKStubPieGenerator(RollKConfig config) => _config = config;

    /// <summary>Generate the seven-way offensive-rebound pie. No signal argument —
    /// flat weights only. The <see cref="Pie{TOutcome}"/> constructor walks the enum
    /// in declaration order, so slice order is fixed for reproducibility regardless
    /// of dictionary iteration order.</summary>
    public Pie<OffensiveReboundOutcome> Generate()
    {
        var weights = new Dictionary<OffensiveReboundOutcome, double>
        {
            [OffensiveReboundOutcome.PutBack]          = _config.PutBack,
            [OffensiveReboundOutcome.JumpBall]         = _config.JumpBall,
            [OffensiveReboundOutcome.DefensiveFoul]    = _config.DefensiveFoul,
            [OffensiveReboundOutcome.OffensiveFoul]    = _config.OffensiveFoul,
            [OffensiveReboundOutcome.DeadBallTurnover] = _config.DeadBallTurnover,
            [OffensiveReboundOutcome.LiveBallTurnover] = _config.LiveBallTurnover,
            [OffensiveReboundOutcome.ResetOffense]     = _config.ResetOffense,
        };

        // The Pie constructor validates the sum is 1 within Epsilon, so any
        // misconfigured weights fail loud here rather than rolling skewed.
        return new Pie<OffensiveReboundOutcome>(weights, _config.Epsilon);
    }
}
