namespace Charm.Engine;

/// <summary>
/// The outcomes Roll I (rebound resolution) can resolve to. This enum defines
/// the slices of Roll I's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
///
/// The key split is on which team ends up with the ball:
/// <list type="bullet">
///   <item><see cref="DefensiveRebound"/> and <see cref="LooseBallFoulOnOffense"/>
///   flip the ball to the defense — both are TERMINALS (possession ends).</item>
///   <item><see cref="OffensiveRebound"/> and <see cref="LooseBallFoulOnDefense"/>
///   keep the ball with the offense — both are CONTINUES (possession lives).</item>
/// </list>
///
/// The live-vs-dead axis mirrors Roll C's turnover classification: a defensive
/// rebound is a live flip (next possession enters via transition), while an
/// offensive loose-ball foul is a dead flip (next possession is a dead-ball
/// inbound at Roll A).
/// </summary>
public enum ReboundOutcome
{
    /// <summary>The defense secures the board. Ball switches teams — live ball.
    /// Next possession enters via the transition node (future). -> TERMINAL.</summary>
    DefensiveRebound,

    /// <summary>The offense secures the board. Same possession stays alive.
    /// -> CONTINUE to the offensive-rebound node (stub).</summary>
    OffensiveRebound,

    /// <summary>A loose-ball foul on the defense in the scramble. Offense retains.
    /// The defensive team foul is charged; bonus is read: below bonus -> sideline
    /// inbound; in bonus -> bonus free throws. -> CONTINUE.</summary>
    LooseBallFoulOnDefense,

    /// <summary>A loose-ball foul on the offense (over-the-back, push-off). Ball
    /// switches teams — dead ball. No foul is charged (Roll C's OffensiveFoul
    /// precedent). Next possession is a dead-ball inbound at Roll A. -> TERMINAL.</summary>
    LooseBallFoulOnOffense
}
