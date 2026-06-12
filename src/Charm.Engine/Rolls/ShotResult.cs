namespace Charm.Engine;

/// <summary>
/// The outcomes Roll H (make/miss) can resolve a located shot to. This enum
/// defines the slices of Roll H's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
///
/// This is the THIRD durable per-possession fact, stamped onto
/// <see cref="PossessionState.Result"/> after <see cref="PossessionState.SelectedSlot"/>
/// (Roll E) and <see cref="PossessionState.ShotType"/> (Roll G). The point value
/// (2 vs. 3) and the free-throw count are DOWNSTREAM derivations from this stamp
/// plus the shot's zone — Roll H records neither; it only resolves and stamps the
/// outcome, then routes.
///
/// Shot quality is NOT a slice here: a great look and a poor look differ only in
/// the make/miss PERCENTAGE (folded into the deferred attribute-driven generator),
/// never as a stored value — so there is no open/contested or assisted/unassisted
/// split.
/// </summary>
public enum ShotResult
{
    /// <summary>The shot goes in, clean. -> TERMINAL. The basket's point value
    /// (2 or 3) is derived later from this stamp + the carried ShotType.</summary>
    Made,

    /// <summary>The shot goes in AND a shooting foul is drawn (an and-1).
    /// -> CONTINUE to the shooting-free-throw node. The basket counts and one free
    /// throw is shot — both are DOWNSTREAM derivations from this stamp, recorded
    /// when the scoring / free-throw layers exist, never here.</summary>
    MadeAndFouled,

    /// <summary>The shot misses, live. -> CONTINUE to the rebound node. The common
    /// case. An offensive rebound keeps the SAME possession (the ~67–70 accounting
    /// anchor); that is the rebound roll's job, not Roll H's.</summary>
    Miss,

    /// <summary>The shot misses AND a shooting foul is drawn. -> CONTINUE to the
    /// shooting-free-throw node. The free-throw COUNT (2, or 3 on a fouled three)
    /// is derived later from this stamp + the carried ShotType, not encoded
    /// here.</summary>
    MissFouled,

    /// <summary>The shot misses and the ball sails out of bounds off the offense
    /// (last touch offense). -> TERMINAL. Defense's ball; the possession ends.</summary>
    MissOutOfBoundsLost,

    /// <summary>The shot misses and the ball deflects out of bounds off the
    /// defender. -> CONTINUE to the sideline-inbound node: the offense keeps it and
    /// inbounds from the side. (This node MAY eventually share a loose-ball /
    /// inbound node with block recovery — flagged, not merged.)</summary>
    MissOutOfBoundsRetained
}
