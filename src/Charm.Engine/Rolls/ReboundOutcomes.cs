namespace Charm.Engine;

/// <summary>
/// The outcomes Roll I (rebound / loose-ball resolution) can resolve to. This enum
/// defines the slices of Roll I's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility). The three arms added in the
/// block-recovery session (<see cref="JumpBall"/>, <see cref="OutOfBoundsOffOffense"/>,
/// <see cref="OutOfBoundsOffDefense"/>) are APPENDED last on purpose: the original
/// four keep their declaration positions, so the cumulative ranges that already
/// existed are untouched and the pre-existing draws map exactly as before.
///
/// The key split is on which team ends up with the ball AND on how the ball ends up
/// there — live (the next possession runs) vs. dead (the next possession inbounds):
/// <list type="bullet">
///   <item><see cref="DefensiveRebound"/> flips the ball to the defense on a LIVE
///   board — TERMINAL, a transition start.</item>
///   <item><see cref="LooseBallFoulOnOffense"/> and <see cref="OutOfBoundsOffOffense"/>
///   flip the ball to the defense on a DEAD ball — both TERMINALS, a dead-ball inbound
///   at Roll A (no foul charged on either; they differ only in the reason label).</item>
///   <item><see cref="OffensiveRebound"/> keeps the ball with the offense on a LIVE
///   board — CONTINUE to the offensive-rebound node (Roll K).</item>
///   <item><see cref="LooseBallFoulOnDefense"/> keeps the ball with the offense and
///   forks on the bonus — CONTINUE.</item>
///   <item><see cref="OutOfBoundsOffDefense"/> keeps the ball with the offense on a
///   DEAD ball — CONTINUE to the sideline-inbound node (no charge, no fork).</item>
///   <item><see cref="JumpBall"/> is a tie-up on the loose ball — CONTINUE to the
///   shared jump-ball node (consults the possession arrow).</item>
/// </list>
///
/// This is the same seven-arm vocabulary Roll M (the free-throw-board resolution)
/// already carries — Roll I is the LIVE field-goal-side loose-ball resolver, Roll M
/// the free-throw-side one. The arms route to nodes that already exist; no new stub
/// is opened. The block context (<see cref="ReboundSource.Block"/>) is a REWEIGHT of
/// these same seven arms (more stays with the swatting defense, more squirts OOB, a
/// higher offensive-recovery rate than a clean miss, a minuscule jump-ball sliver) —
/// it never changes where an arm routes.
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
    LooseBallFoulOnOffense,

    /// <summary>The ball goes out of bounds last touched by the OFFENSE in the
    /// scramble (a shot caroms off the rim and out, a rebounder fumbles it out off
    /// his own hands). Ball switches teams — dead ball, NO foul charged. Lands exactly
    /// where the offensive loose-ball foul lands (defense's ball on a dead-ball inbound
    /// at Roll A), a different reason label for the same routing. NOT a turnover (no
    /// true possession was established), but it starts the defense's next possession
    /// dead — underneath the far basket — rather than on a live push, which is the
    /// whole point of keeping it distinct from <see cref="DefensiveRebound"/>.
    /// -> TERMINAL.</summary>
    OutOfBoundsOffOffense,

    /// <summary>The ball goes out of bounds last touched by the DEFENSE in the
    /// scramble (a blocked shot swatted out, a defender fumbling the loose ball out).
    /// Offense retains and inbounds from the side. NO foul charged, so NO bonus fork —
    /// it is always a plain sideline inbound (no foul means no bonus question), unlike
    /// the loose-ball-defense arm. Kept distinct from <see cref="OffensiveRebound"/>
    /// because the offense restarts DEAD from the sideline (its own-side inbound
    /// modifiers are the inbound node's job, landing with the Roll A reshape) rather
    /// than on a live putback/reset. -> CONTINUE.</summary>
    OutOfBoundsOffDefense,

    /// <summary>A tie-up on the loose ball — two players come up with it at once.
    /// Always possible on a live scramble (a normal rebound or a blocked-shot fight).
    /// -> CONTINUE to the shared jump-ball node (consults the possession arrow),
    /// exactly as Roll K's and Roll M's tie-up arms.</summary>
    JumpBall
}

/// <summary>
/// The SOURCE axis of a rebound / loose-ball scramble — HOW the loose ball came to be.
/// This is the within-possession ticket memory that selects Roll I's pie: the same
/// ticket/station pattern as Roll K's <see cref="OffensiveReboundSource"/> and Roll C's
/// turnover context (a station stamps it at write time; Roll I's generator reads it to
/// pick a weight set; the generator NEVER queries the stamping station). A labeled tag,
/// not a bool, so it GROWS BY APPEND if a third loose-ball source ever feeds in, rather
/// than forcing a second bool or a teardown.
///
/// <para>It rides on the <see cref="Continue.ReboundSource"/> field of the
/// <see cref="ContinuationKind.ResolveRebound"/> continuation. A NULL stamp reads as
/// <see cref="LiveBall"/>: every legacy feeder (Roll H's <c>Miss</c> arm, and a missed
/// putback re-entering Roll I) stamps nothing, so the live-miss path is byte-for-byte
/// unchanged. Only Roll H's <c>Blocked</c> arm stamps <see cref="Block"/>.</para>
/// </summary>
public enum ReboundSource
{
    /// <summary>The loose ball came off a LIVE field-goal miss (Roll H's <c>Miss</c>
    /// arm, and a missed putback re-entering Roll I). The default — every legacy feeder
    /// reads as this, so a null stamp maps here and the live-miss pie is byte-for-byte
    /// unchanged.</summary>
    LiveBall,

    /// <summary>The loose ball came off a BLOCKED shot (Roll H's <c>Blocked</c> arm).
    /// Selects Roll I's block-recovery pie: more of the swatted ball stays with the
    /// blocking (defense) side or squirts out of bounds, a higher offensive-recovery
    /// rate than a clean carom, and a minuscule jump-ball sliver. Same seven arms, same
    /// routes — only the weights differ.</summary>
    Block
}
