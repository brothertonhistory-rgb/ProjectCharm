namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll K (offensive-rebound resolution). Builds a flat
/// seven-way pie from config weights, selecting the weight set by the
/// <see cref="OffensiveReboundSource"/> the board arrived with (LiveBall off a
/// field-goal miss, FreeThrow off a missed FT — the latter more putback). No
/// attribute model yet. The same ticket/station selection pattern Roll J uses for its
/// transition source and Roll C uses for its turnover context.
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
    /// <summary>Generate the seven-way offensive-rebound pie for the arriving SOURCE.
    /// A null stamp (every legacy Roll I feeder) is mapped to
    /// <see cref="OffensiveReboundSource.LiveBall"/> by the resolver before calling, so
    /// the live-ball path is byte-for-byte unchanged; Roll M stamps
    /// <see cref="OffensiveReboundSource.FreeThrow"/> for its FT-specific pie. The
    /// ROUTING in Roll K is identical for both — only the weights differ. The
    /// <see cref="Pie{TOutcome}"/> constructor walks the enum in declaration order, so
    /// slice order is fixed for reproducibility regardless of dictionary iteration
    /// order, and validates sum-to-one so any misconfigured weights fail loud here.</summary>
    /// <param name="source">Which board the offense secured — selects the weight set.</param>
    public Pie<OffensiveReboundOutcome> Generate(OffensiveReboundSource source)
    {
        var weights = source switch
        {
            OffensiveReboundSource.LiveBall => new Dictionary<OffensiveReboundOutcome, double>
            {
                [OffensiveReboundOutcome.PutBack]          = _config.PutBack,
                [OffensiveReboundOutcome.JumpBall]         = _config.JumpBall,
                [OffensiveReboundOutcome.DefensiveFoul]    = _config.DefensiveFoul,
                [OffensiveReboundOutcome.OffensiveFoul]    = _config.OffensiveFoul,
                [OffensiveReboundOutcome.DeadBallTurnover] = _config.DeadBallTurnover,
                [OffensiveReboundOutcome.LiveBallTurnover] = _config.LiveBallTurnover,
                [OffensiveReboundOutcome.ResetOffense]     = _config.ResetOffense,
            },

            OffensiveReboundSource.FreeThrow => new Dictionary<OffensiveReboundOutcome, double>
            {
                [OffensiveReboundOutcome.PutBack]          = _config.FreeThrowPutBack,
                [OffensiveReboundOutcome.JumpBall]         = _config.FreeThrowJumpBall,
                [OffensiveReboundOutcome.DefensiveFoul]    = _config.FreeThrowDefensiveFoul,
                [OffensiveReboundOutcome.OffensiveFoul]    = _config.FreeThrowOffensiveFoul,
                [OffensiveReboundOutcome.DeadBallTurnover] = _config.FreeThrowDeadBallTurnover,
                [OffensiveReboundOutcome.LiveBallTurnover] = _config.FreeThrowLiveBallTurnover,
                [OffensiveReboundOutcome.ResetOffense]     = _config.FreeThrowResetOffense,
            },

            _ => throw new InvalidOperationException(
                $"No Roll K pie for offensive-rebound source '{source}'.")
        };

        // The Pie constructor validates the sum is 1 within Epsilon, so any
        // misconfigured weights fail loud here rather than rolling skewed.
        return new Pie<OffensiveReboundOutcome>(weights, _config.Epsilon);
    }
}
