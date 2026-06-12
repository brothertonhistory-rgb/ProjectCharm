namespace Charm.Engine;

/// <summary>
/// The outcomes Roll J (transition-entry run-or-not gate) can resolve to — the
/// slices of Roll J's pie. Declaration order is significant: <see cref="Pie{TOutcome}"/>
/// walks slices in this order, so the same RNG draw always maps to the same
/// outcome (reproducibility).
///
/// Roll J decides ONLY whether a live-ball possession runs or pulls it out; what a
/// fast break PRODUCES (numbers advantage, leak-outs, transition threes vs. layups)
/// is a SEPARATE later roll that <see cref="Push"/> parks at. Every slice here is a
/// CONTINUE — Roll J names no terminal of its own; its two "ending" flavors
/// (<see cref="JumpBall"/> and the <see cref="DefensiveFoul"/> fork) resolve at
/// shared downstream nodes, not here.
/// </summary>
public enum TransitionOutcome
{
    /// <summary>Pull it out and run a halfcourt set — the "proceed" analog (cf.
    /// Roll A CleanEntry, Roll B Proceed). -> CONTINUE to player selection (Roll E).</summary>
    Settle,

    /// <summary>We run. -> CONTINUE to the parked transition stub (the future
    /// transition roll's holding pen).</summary>
    Push,

    /// <summary>Coughed it up on the outlet/push. -> CONTINUE to the shared turnover
    /// node (Roll C), STAMPED with the Transition turnover context so Roll C selects
    /// its transition pie (more live strips going the other way).</summary>
    Turnover,

    /// <summary>Fouled on the push. Charges the rebound-losing team (the new defense)
    /// and forks on the bonus: below -> sideline inbound; in bonus -> free throws.
    /// -> CONTINUE (the Roll I / Roll D charge-and-fork pattern).</summary>
    DefensiveFoul,

    /// <summary>Tie-up on the rebound/outlet. -> CONTINUE to the shared jump-ball
    /// node (consults the possession arrow).</summary>
    JumpBall
}
