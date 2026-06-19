namespace Charm.Engine;

/// <summary>
/// Real pie generator for Roll C. Returns the configured weights for the turnover
/// CONTEXT the ticket arrived with — the ticket/station mechanism: a feeding
/// station stamps <see cref="TurnoverContext"/> on the <see cref="Continue"/>, and
/// this node reads it to pick a parameter set, NEVER querying who fed it.
///
/// <para>Three context sets: <see cref="TurnoverContext.Halfcourt"/> (the default;
/// every feeder that stamps nothing lands here), <see cref="TurnoverContext.Transition"/>
/// (Roll J's outlet/push set: more live strips), and
/// <see cref="TurnoverContext.EntryBackcourt"/> (the backcourt bring-up: bad passes,
/// live strips, and the three backcourt-only violations — 5-second inbound,
/// 10-second backcourt, shot-clock on the way up). The Halfcourt and Transition
/// paths are byte-for-byte unchanged from the stub.</para>
///
/// <para>Flat weights: no player-attribute tilt, no pressure parameter. Pressure
/// changes how often a team turns it over (Roll A / Roll B / Roll F), not what
/// KIND of turnover results. A player who rarely turns it over turns it over the
/// same way as one who does — just less often. The pressure parameter that existed
/// in the stub was a seam-test placeholder and has been retired.</para>
/// </summary>
public sealed class RollCGenerator
{
    private readonly RollCConfig _cfg;

    public RollCGenerator(RollCConfig cfg) => _cfg = cfg;

    /// <param name="state">Carried for signature parity with real generators;
    /// not read by this generator.</param>
    /// <param name="context">The turnover CONTEXT TICKET that selects the base
    /// weight set. Defaults to <see cref="TurnoverContext.Halfcourt"/> so a feeder
    /// that stamps nothing (a null ticket on the <see cref="Continue"/>) gets the
    /// legacy pie.</param>
    public Pie<TurnoverOutcome> Generate(
        PossessionState state,
        TurnoverContext context = TurnoverContext.Halfcourt)
    {
        // Select the weight set by the ticket's context. The node reads the
        // stamped context; it never queries the feeding station.
        var weights = context switch
        {
            TurnoverContext.Halfcourt => new Dictionary<TurnoverOutcome, double>
            {
                [TurnoverOutcome.BadPassDeadBall]    = _cfg.BaseBadPassDeadBall,
                [TurnoverOutcome.BadPassIntercepted] = _cfg.BaseBadPassIntercepted,
                [TurnoverOutcome.LostBallDeadBall]   = _cfg.BaseLostBallDeadBall,
                [TurnoverOutcome.LostBallLiveBall]   = _cfg.BaseLostBallLiveBall,
                [TurnoverOutcome.OffensiveFoul]      = _cfg.BaseOffensiveFoul,
                // expanded set, dormant in Halfcourt (all 0.0 — pie requires every member).
                [TurnoverOutcome.Travel]                   = _cfg.BaseTravel,
                [TurnoverOutcome.DoubleDribble]            = _cfg.BaseDoubleDribble,
                [TurnoverOutcome.Carry]                    = _cfg.BaseCarry,
                [TurnoverOutcome.ThreeSecondViolation]     = _cfg.BaseThreeSecondViolation,
                [TurnoverOutcome.FiveSecondCloselyGuarded] = _cfg.BaseFiveSecondCloselyGuarded,
                [TurnoverOutcome.OffensiveGoaltending]     = _cfg.BaseOffensiveGoaltending,
                [TurnoverOutcome.BackcourtViolation]       = _cfg.BaseBackcourtViolation,
                [TurnoverOutcome.ShotClockViolation]       = _cfg.BaseShotClockViolation,
                [TurnoverOutcome.FiveSecondInbound]        = _cfg.BaseFiveSecondInbound,
                [TurnoverOutcome.TenSecondBackcourt]       = _cfg.BaseTenSecondBackcourt,
            },

            TurnoverContext.Transition => new Dictionary<TurnoverOutcome, double>
            {
                [TurnoverOutcome.BadPassDeadBall]    = _cfg.TransitionBadPassDeadBall,
                [TurnoverOutcome.BadPassIntercepted] = _cfg.TransitionBadPassIntercepted,
                [TurnoverOutcome.LostBallDeadBall]   = _cfg.TransitionLostBallDeadBall,
                [TurnoverOutcome.LostBallLiveBall]   = _cfg.TransitionLostBallLiveBall,
                [TurnoverOutcome.OffensiveFoul]      = _cfg.TransitionOffensiveFoul,
                // expanded set, dormant in Transition (all 0.0 — pie requires every member).
                [TurnoverOutcome.Travel]                   = _cfg.TransitionTravel,
                [TurnoverOutcome.DoubleDribble]            = _cfg.TransitionDoubleDribble,
                [TurnoverOutcome.Carry]                    = _cfg.TransitionCarry,
                [TurnoverOutcome.ThreeSecondViolation]     = _cfg.TransitionThreeSecondViolation,
                [TurnoverOutcome.FiveSecondCloselyGuarded] = _cfg.TransitionFiveSecondCloselyGuarded,
                [TurnoverOutcome.OffensiveGoaltending]     = _cfg.TransitionOffensiveGoaltending,
                [TurnoverOutcome.BackcourtViolation]       = _cfg.TransitionBackcourtViolation,
                [TurnoverOutcome.ShotClockViolation]       = _cfg.TransitionShotClockViolation,
                [TurnoverOutcome.FiveSecondInbound]        = _cfg.TransitionFiveSecondInbound,
                [TurnoverOutcome.TenSecondBackcourt]       = _cfg.TransitionTenSecondBackcourt,
            },

            // EntryBackcourt: the backcourt bring-up context, now live.
            // Gives real weight to the three backcourt-only violations (5-second
            // inbound, 10-second backcourt, shot-clock on the way up). Halfcourt-only
            // types (travel, 3-second, carry, offensive foul, offensive goaltending)
            // and over-and-back are 0.0 — you haven't crossed halfcourt yet.
            TurnoverContext.EntryBackcourt => new Dictionary<TurnoverOutcome, double>
            {
                [TurnoverOutcome.BadPassDeadBall]    = _cfg.EntryBackcourtBadPassDeadBall,
                [TurnoverOutcome.BadPassIntercepted] = _cfg.EntryBackcourtBadPassIntercepted,
                [TurnoverOutcome.LostBallDeadBall]   = _cfg.EntryBackcourtLostBallDeadBall,
                [TurnoverOutcome.LostBallLiveBall]   = _cfg.EntryBackcourtLostBallLiveBall,
                [TurnoverOutcome.OffensiveFoul]      = _cfg.EntryBackcourtOffensiveFoul,
                [TurnoverOutcome.Travel]                   = _cfg.EntryBackcourtTravel,
                [TurnoverOutcome.DoubleDribble]            = _cfg.EntryBackcourtDoubleDribble,
                [TurnoverOutcome.Carry]                    = _cfg.EntryBackcourtCarry,
                [TurnoverOutcome.ThreeSecondViolation]     = _cfg.EntryBackcourtThreeSecondViolation,
                [TurnoverOutcome.FiveSecondCloselyGuarded] = _cfg.EntryBackcourtFiveSecondCloselyGuarded,
                [TurnoverOutcome.OffensiveGoaltending]     = _cfg.EntryBackcourtOffensiveGoaltending,
                [TurnoverOutcome.BackcourtViolation]       = _cfg.EntryBackcourtBackcourtViolation,
                [TurnoverOutcome.ShotClockViolation]       = _cfg.EntryBackcourtShotClockViolation,
                [TurnoverOutcome.FiveSecondInbound]        = _cfg.EntryBackcourtFiveSecondInbound,
                [TurnoverOutcome.TenSecondBackcourt]       = _cfg.EntryBackcourtTenSecondBackcourt,
            },

            _ => throw new InvalidOperationException(
                $"No Roll C pie for turnover context '{context}'.")
        };

        return new Pie<TurnoverOutcome>(weights, _cfg.Epsilon);
    }
}
