namespace Charm.Engine;

/// <summary>
/// The three outcomes Roll A (dead-ball inbound entry) can resolve to.
/// This enum defines the slices of Roll A's pie. Declaration order is
/// significant: <see cref="Pie{TOutcome}"/> walks slices in this order,
/// so the same RNG draw always maps to the same outcome (reproducibility).
/// </summary>
public enum EntryOutcome
{
    /// <summary>Offense gets cleanly into its halfcourt set. -> CONTINUE.</summary>
    CleanEntry,

    /// <summary>Offense coughs it up on the entry. -> CONTINUE (to turnover-type resolver).</summary>
    Turnover,

    /// <summary>
    /// No shot, full shot clock burned. -> TERMINAL. Invariant: a shot-clock
    /// violation is always the full clock off, never more or less, so its
    /// elapsed time is known here and needs no separate time roll.
    /// </summary>
    ShotClockViolation
}

/// <summary>
/// The semantic category of a CONTINUE result. A roll classifies *what kind*
/// of continuation it produced; it never names the successor node. The
/// <see cref="Resolver"/> owns the mapping from a kind to the actual next node.
/// Adding a node later changes only that mapping, never the roll that emitted
/// the continuation. This is the seam that lets Roll A be built before its
/// successors exist.
/// </summary>
public enum ContinuationKind
{
    /// <summary>Clean entry: hand off to the (stubbed) halfcourt-set node.</summary>
    IntoHalfcourtSet,

    /// <summary>Turnover: hand off to the (stubbed) turnover-type resolver.</summary>
    ResolveTurnoverType
}
