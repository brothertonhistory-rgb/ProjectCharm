namespace Charm.Engine;

/// <summary>
/// The five shot LOCATIONS a clean attempt can come from — the only thing Roll G
/// stamps. Declaration order is significant: <see cref="Pie{TOutcome}"/> walks
/// slices in this order, so the same RNG draw always maps to the same zone
/// (reproducibility), exactly as Roll A's and Roll E's enums do.
///
/// Location ONLY — no open-vs-contested, no assisted-vs-unassisted. Those belong
/// to shot QUALITY, which is NOT its own beat: it is folded into the make/miss
/// percentage at the future Roll H. Keeping each zone to one clean meaning is what
/// gives every bucket a real-world FG% to calibrate against later; a bucket that
/// smuggled in a second axis would have no clean reference number.
/// </summary>
public enum ShotLocation
{
    /// <summary>Three-point attempt. ONE bucket for now — corner vs above-the-break
    /// is a real efficiency gap but a cheap future slice-split, deliberately not
    /// front-loaded.</summary>
    Three,

    /// <summary>Long two — the INEFFICIENT shot. Kept its own bucket on purpose:
    /// it is what lets shot selection matter (lots of long twos should visibly
    /// bleed efficiency). Never collapsed into Mid.</summary>
    Long,

    /// <summary>Mid-range pull-up jumpers (guards). A distinct shot population from
    /// Short — the split is where slot identity starts to express.</summary>
    Mid,

    /// <summary>Short — floaters, runners, hooks (bigs). A distinct population from
    /// Mid; kept separate for the same slot-identity reason.</summary>
    Short,

    /// <summary>At the rim. The highest-efficiency bucket.</summary>
    Rim
}
