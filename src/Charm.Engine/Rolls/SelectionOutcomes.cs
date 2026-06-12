namespace Charm.Engine;

/// <summary>
/// The outcomes Roll E (player selection) can resolve to: which of the five
/// on-court offensive slots gets the action this possession. Declaration order
/// is significant: <see cref="Pie{TOutcome}"/> walks slices in this order, so the
/// same RNG draw always maps to the same outcome (reproducibility).
///
/// These are SLOT NUMBERS, not roles: <c>Slot1</c> is not "the point guard." The
/// member maps to a slot number 1–5 (its declaration position), which Roll E
/// resolves against the offense's lineup via <c>LineupFor(offense).SlotAt(n)</c>.
/// Identity, not substance — same discipline as <see cref="Slot"/> itself.
///
/// The pie is FLAT this session: 20% each, no signal yet. What eventually tilts
/// these odds (usage, hierarchy, ball-dominance, attributes, coaching) is the
/// player/attribute model — a later, smarter generator that produces a non-flat
/// pie WITHOUT this enum, the roll, or the resolver changing. The selector never
/// changes; only the odds it is handed do.
/// </summary>
public enum SelectionOutcome
{
    /// <summary>Slot 1 gets the action.</summary>
    Slot1,

    /// <summary>Slot 2 gets the action.</summary>
    Slot2,

    /// <summary>Slot 3 gets the action.</summary>
    Slot3,

    /// <summary>Slot 4 gets the action.</summary>
    Slot4,

    /// <summary>Slot 5 gets the action.</summary>
    Slot5,
}
