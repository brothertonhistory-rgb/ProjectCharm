namespace Charm.Engine;

/// <summary>
/// The outcomes Roll A (dead-ball inbound entry) can resolve to. This enum
/// defines the slices of Roll A's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
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
    ShotClockViolation,

    /// <summary>A foul on the inbound/entry. -> CONTINUE (to foul-type resolver,
    /// which will decide defensive non-shooting vs. offensive and what it
    /// triggers). Has real variance, so it is never resolved here.</summary>
    Foul,

    /// <summary>A tie-up / held ball on the inbound. -> CONTINUE (to jump-ball
    /// resolver, which consults the possession arrow on GameState). Rare but
    /// real on virtually every pie going forward.</summary>
    JumpBall
}

/// <summary>
/// The semantic category of a CONTINUE result. A roll classifies *what kind*
/// of continuation it produced; it never names the successor node. The
/// <see cref="Resolver"/> owns the mapping from a kind to the actual next node.
/// Adding a node later changes only that mapping, never the roll that emitted
/// the continuation. This is the seam that lets any roll be built before its
/// successors exist.
/// </summary>
public enum ContinuationKind
{
    /// <summary>Clean entry: hand off to Roll B (halfcourt initiation).</summary>
    IntoHalfcourtSet,

    /// <summary>Turnover: hand off to the (stubbed) turnover-type resolver.</summary>
    ResolveTurnoverType,

    /// <summary>Foul: hand off to the (stubbed) foul-type resolver.</summary>
    ResolveFoulType,

    /// <summary>Jump ball: hand off to the (stubbed) jump-ball resolver.</summary>
    ResolveJumpBall,

    /// <summary>Halfcourt possession proceeds: hand off to the (stubbed)
    /// player-selection roll.</summary>
    IntoPlayerSelection,
}
