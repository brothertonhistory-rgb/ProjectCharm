namespace Charm.Engine;

/// <summary>
/// The outcomes Roll K (offensive-rebound resolution) can resolve to — the slices
/// of Roll K's pie. This is the node the offense lands at the instant it secures
/// its own miss (Roll I's <see cref="ReboundOutcome.OffensiveRebound"/>). It is the
/// engine's highest-volume open node and the FIRST possession-EXTENDING roll: one
/// arm keeps the SAME possession alive and loops it back up the chain.
///
/// Declaration order is significant: <see cref="Pie{TOutcome}"/> walks slices in
/// this order, so the same RNG draw always maps to the same outcome
/// (reproducibility), exactly as every prior roll's enum does.
///
/// The split that matters is what happens to the SAME possession:
/// <list type="bullet">
///   <item><see cref="PutBack"/> and <see cref="ResetOffense"/> keep the ball with
///   the offense — both are CONTINUES (possession lives, count does NOT
///   increment). A putback goes straight back up at the rim; a reset kicks it out
///   and runs a fresh play.</item>
///   <item><see cref="DefensiveFoul"/> keeps the offense's ball too (CONTINUE) —
///   it charges the defense and forks on the bonus, the Roll D / I / J pattern.</item>
///   <item><see cref="OffensiveFoul"/>, <see cref="DeadBallTurnover"/>, and
///   <see cref="LiveBallTurnover"/> flip the ball to the defense — all TERMINALS
///   (possession ends).</item>
///   <item><see cref="JumpBall"/> is a CONTINUE to the shared jump-ball node, which
///   resolves the award against the arrow and ends the current possession there.</item>
/// </list>
/// </summary>
public enum OffensiveReboundOutcome
{
    /// <summary>An immediate go-back-up at the rim. The same possession stays alive.
    /// CONTINUE into Roll H (make/miss) with the zone FORCED to
    /// <see cref="ShotLocation.Rim"/> and a PUTBACK ticket stamped on the
    /// continuation, so Roll H's generator selects a distinct putback pie (its own
    /// make/foul/and-1 numbers — a future basketball call). A missed putback
    /// re-enters the rebound node (Roll I) on the existing flat pie, creating a
    /// re-entrant scrum under the basket that converges probabilistically.
    /// -> CONTINUE.</summary>
    PutBack,

    /// <summary>A tie-up on the rebound. -> CONTINUE to the shared jump-ball node
    /// (consults the possession arrow).</summary>
    JumpBall,

    /// <summary>A foul on the defense in the scrum. Offense retains. Charges the
    /// defensive team foul and reads the bonus: below bonus -> sideline inbound; in
    /// bonus -> bonus free throws. The FOURTH feeder into the shared charge-and-fork
    /// (after Roll D, Roll I, Roll J) — copied, not reinvented. -> CONTINUE.</summary>
    DefensiveFoul,

    /// <summary>An offensive foul in the scrum (over-the-back, push-off). Ball
    /// switches teams — dead ball. No foul is charged (Roll C's OffensiveFoul
    /// precedent). Next possession is a dead-ball inbound at Roll A. -> TERMINAL.</summary>
    OffensiveFoul,

    /// <summary>A dead-ball turnover off the board (travel, pass out of bounds).
    /// Same consequence as <see cref="OffensiveFoul"/>: ball to the other team on a
    /// dead-ball inbound at Roll A. -> TERMINAL.</summary>
    DeadBallTurnover,

    /// <summary>A live-ball turnover off the board (stripped, lost live). Ball to
    /// the defense on a live push. PARKED this session: emitted as a plain
    /// <see cref="PossessionConsequence.TransitionTo"/> with NO context ticket, so
    /// the resolver temp-routes the spawned possession through Roll A — EXACTLY how
    /// steals are handled now. Its real home is the transition module via the steal
    /// feeder's contextual wiring; that lands with the steal-feeder session as a
    /// one-line routing flip. -> TERMINAL.</summary>
    LiveBallTurnover,

    /// <summary>Kick it back out and run a fresh play. The same possession stays
    /// alive and the count does NOT increment. CONTINUE back to Roll E (player
    /// selection) on a BLANK slate — the prior shot's facts (selected slot, zone,
    /// result) are wiped, so the reset draws the inherent selection odds. Re-enters
    /// at E, NOT Roll B: the offensive-rebound pie already absorbed the
    /// turnover/foul/jumpball risk, so routing through B would double-charge those
    /// same hazards. -> CONTINUE.</summary>
    ResetOffense
}
