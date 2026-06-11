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
