namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll C. Returns the configured weights for the turnover
/// CONTEXT the ticket arrived with — the ticket/station mechanism: a feeding
/// station stamps <see cref="TurnoverContext"/> on the <see cref="Continue"/>, and
/// this node reads it to pick a parameter set, NEVER querying who fed it.
///
/// <para>Two context sets this session: <see cref="TurnoverContext.Halfcourt"/>
/// (the legacy/default 30/22/18/20/10 — every pre-Roll-J feeder, stamping nothing,
/// lands here) and <see cref="TurnoverContext.Transition"/> (Roll J's outlet/push
/// set: more live strips going the other way). The Halfcourt path is byte-for-byte
/// unchanged.</para>
///
/// <para>One live wire on top of the selected set: a single 0–1 <c>pressure</c>
/// scalar nudges the live-strip slice (then renormalizes) — placeholder proving the
/// generator->roll seam carries signal, NOT basketball logic. The real
/// attribute-driven generator replaces this without touching Roll C or the
/// resolver.</para>
/// </summary>
public sealed class RollCStubPieGenerator
{
    private readonly RollCConfig _cfg;

    public RollCStubPieGenerator(RollCConfig cfg) => _cfg = cfg;

    /// <param name="state">Carried for signature parity with real generators;
    /// the stub does not read it yet.</param>
    /// <param name="pressure">0–1 live wire. Higher pressure nudges the
    /// live-strip (LostBallLiveBall) slice up before renormalization. Applies to
    /// whichever context set was selected.</param>
    /// <param name="context">The turnover CONTEXT TICKET that selects the base
    /// weight set. Defaults to <see cref="TurnoverContext.Halfcourt"/> so a feeder
    /// that stamps nothing (a null ticket on the <see cref="Continue"/>) gets the
    /// legacy pie — placed last with a default so the legacy call sites need no
    /// change.</param>
    public Pie<TurnoverOutcome> Generate(
        PossessionState state,
        double pressure = 0.0,
        TurnoverContext context = TurnoverContext.Halfcourt)
    {
        var clamped = Math.Clamp(pressure, 0.0, 1.0);

        // Select the base weight set by the ticket's context. The node reads the
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
                // #5a expanded set, DORMANT here (all 0.0 — pie requires every member).
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
                // #5a expanded set, DORMANT here (all 0.0 — pie requires every member).
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

            // #5a: the Entry/Backcourt context (DORMANT — nothing routes here yet).
            // The only context giving the new types real weight; exercised solely by
            // the isolation check until #5b wires Roll A's loss exit into it. Lists
            // every member, as Pie requires.
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

        // Live wire: pressure nudges the live-strip slice up on the SELECTED set,
        // then renormalize so the pie sums to 1 (the Pie constructor validates it).
        weights[TurnoverOutcome.LostBallLiveBall] += clamped * _cfg.PressureLostBallLiveBallNudge;

        var total = weights.Values.Sum();
        foreach (var key in weights.Keys.ToList())
            weights[key] /= total;

        return new Pie<TurnoverOutcome>(weights, _cfg.Epsilon);
    }
}
