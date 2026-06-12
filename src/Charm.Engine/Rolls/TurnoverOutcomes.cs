namespace Charm.Engine;

/// <summary>
/// The outcomes Roll C (turnover classification) can resolve to. This enum
/// defines the slices of Roll C's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
///
/// The split is along the dead-ball vs. live-ball axis, because that is the
/// distinction that drives what the *next* possession looks like: a dead-ball
/// turnover resumes on an inbound (a future entry roll picks the spot), while a
/// live-ball turnover hands the defense the ball live, in transition. A future
/// entry roll and a future attribution layer both consume this classification;
/// Roll C only names the type and ends the possession.
///
/// Every outcome here is a TERMINAL — the possession is over and the ball
/// changes hands regardless of which slice lands. ShotClockViolation is NOT a
/// slice here: Roll A already terminates it (invariant, full clock), so
/// duplicating it would be wrong.
/// </summary>
public enum TurnoverOutcome
{
    /// <summary>Errant pass that sails out of bounds. Dead ball: the next
    /// possession resumes on an inbound. -> TERMINAL.</summary>
    BadPassDeadBall,

    /// <summary>Errant pass picked off by a defender. Live ball: the defense has
    /// it in transition. A steal is attributable to the defense (the attribution
    /// layer, not Roll C, decides which defender). -> TERMINAL.</summary>
    BadPassIntercepted,

    /// <summary>Ball lost off the dribble and knocked out of bounds. Dead ball:
    /// next possession resumes on an inbound. -> TERMINAL.</summary>
    LostBallDeadBall,

    /// <summary>Ball stripped off the dribble and corralled live by the defense.
    /// Live ball: defense in transition. A steal is attributable to the defense.
    /// -> TERMINAL.</summary>
    LostBallLiveBall,

    /// <summary>Charge, illegal screen, etc. Always a dead ball; no steal is
    /// credited to the defense. -> TERMINAL.</summary>
    OffensiveFoul
}

/// <summary>
/// The CONTEXT TICKET a turnover carries into Roll C — the within-possession ticket
/// memory that selects WHICH turnover pie Roll C uses. The first instance of the
/// ticket/station mechanism: a feeding station stamps this on the <see cref="Continue"/>
/// that routes to <see cref="ContinuationKind.ResolveTurnoverType"/>, and Roll C's
/// generator reads it to pick a parameter set. Roll C NEVER queries who fed it —
/// "many feeders, one node," now with route-specific weights.
///
/// Declaration order is significant: <see cref="Halfcourt"/> is FIRST so it is the
/// default/legacy context. The ticket is carried as an optional payload
/// (<see cref="Continue.TurnoverContext"/>); a null/absent stamp reads as
/// <see cref="Halfcourt"/>, so EVERY existing feeder (Roll A, Roll B, Roll F),
/// which stamps nothing, keeps today's exact behavior byte-for-byte.
/// </summary>
public enum TurnoverContext
{
    /// <summary>A turnover in a settled halfcourt possession — the default/legacy
    /// context (the unchanged 30/22/18/20/10 pie). Every pre-Roll-J feeder lands
    /// here by stamping nothing.</summary>
    Halfcourt,

    /// <summary>A turnover on a transition outlet/push — stamped by Roll J's
    /// <see cref="TransitionOutcome.Turnover"/> arm. Selects Roll C's transition pie:
    /// more often LIVE and going the other way (live strips up, offensive fouls down).</summary>
    Transition
}
