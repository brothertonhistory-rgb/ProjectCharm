namespace Charm.Engine;

/// <summary>
/// The outcomes Roll B (halfcourt initiation) can resolve to. Declaration order
/// is significant: <see cref="Pie{TOutcome}"/> walks slices in this order, so
/// the same RNG draw always maps to the same outcome (reproducibility).
/// </summary>
public enum HalfcourtOutcome
{
    /// <summary>Possession advances to player selection. -> CONTINUE.</summary>
    Proceed,

    /// <summary>A foul before any action. -> CONTINUE (to foul-type resolver,
    /// which decides offensive vs. defensive non-shooting and what it triggers).
    /// Has real variance, so it is never resolved here.</summary>
    Foul,

    /// <summary>A dead-ball turnover in the frontcourt (held ball, violation,
    /// etc.) before any action. -> CONTINUE (to turnover-type resolver).</summary>
    DeadBallTurnover,

    /// <summary>A tie-up / held ball while the offense is initiating (ball being
    /// worked/advanced into the set). -> CONTINUE (to the jump-ball node, which
    /// consults the possession arrow). A held ball is reachable from every
    /// live-ball action beat; this mirrors Roll A's and Roll F's jump-ball slice
    /// — "many feeders, one node." A small sliver.</summary>
    JumpBall,
}
