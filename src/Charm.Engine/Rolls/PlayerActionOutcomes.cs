namespace Charm.Engine;

/// <summary>
/// The outcomes Roll F (player action) can resolve to — what the selected
/// player's action BECOMES. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
///
/// Roll F is a flat GATE, exactly like Roll B: every outcome is a CONTINUE,
/// because each one has downstream work. THREE route to nodes that already
/// exist (turnover, foul, jump ball — the "many feeders, one node" payoff);
/// TWO open new pipes (blocked, shot attempt). What TILTS these odds — the
/// handle, the defender's hands/length, rim protection, shot selection — is the
/// deferred player/attribute model, delivered later as a smarter generator. The
/// roll never changes when that lands.
/// </summary>
public enum PlayerActionOutcome
{
    /// <summary>A clean shot attempt gets off. -> CONTINUE (to the shot-type
    /// node). This is the one outcome that proceeds DEEPER into the shot
    /// sequence; the others route to shared sinks. The dominant slice.</summary>
    ShotAttempt,

    /// <summary>The ball-handler coughs it up (live turnover by the selected
    /// player). -> CONTINUE (to the shared turnover-type resolver, Roll C). The
    /// 10-second/shot-clock backcourt violations are NOT reachable here — those
    /// are Roll A terminals, not Roll C slices, so routing already excludes
    /// them.</summary>
    Turnover,

    /// <summary>A non-shooting defensive foul is drawn before the shot gets off.
    /// -> CONTINUE (to the shared foul-type resolver, Roll D). NON-shooting by
    /// construction: no shot is up yet at this beat, so this fits Roll D's
    /// existing pre-shot definition exactly. The shooting foul is a deliberately
    /// SEPARATE home in the future make/miss roll (Roll H) — kept apart on
    /// purpose.</summary>
    NonShootingFoul,

    /// <summary>The attempt is blocked. -> CONTINUE (to the new block-recovery
    /// node). A block is a LIVE-BALL event with its own fan-out (out of bounds
    /// off defense / off offense / scramble recovered by either team), so it is
    /// a continue into its own future roll, NOT a terminal.</summary>
    Blocked,

    /// <summary>A tie-up / held ball at the action beat (trapped handler, gang
    /// rebound). -> CONTINUE (to the existing jump-ball node, via the possession
    /// arrow). A real live-ball event here, exactly as on Roll A and Roll
    /// B.</summary>
    JumpBall
}
