namespace Charm.Engine;

/// <summary>
/// Resolves which defender contests the offense's selected shooter (Phase 6, v1).
///
/// <para><b>v1 logic — slot-guards-slot.</b> The defender is the player in the SAME
/// slot number on the defense's side. The selection roll stamps an offense-side
/// <see cref="PossessionState.SelectedSlot"/>; the matched defender is that same number
/// on <see cref="PossessionState.Defense"/>.</para>
///
/// <para><b>Leanest seam (DEC-1).</b> The pick is deterministic and currently has a
/// single consumer (the make door), so the defender is derived here at generate-time
/// rather than carried on <see cref="PossessionState"/>. This stays a distinct, named,
/// swappable unit so the eventual mismatch-hunting picker drops in here.</para>
///
/// <para><b>Flagged promotion (out of scope).</b> The moment a second door consumes the
/// defender, or the pick becomes non-deterministic (mismatch-hunting), the defender must
/// be promoted to a carried <c>PossessionState.DefenderSlot</c> stamped once after Roll E
/// so every door in a possession shares one coherent pick.</para>
/// </summary>
public static class DefenderPicker
{
    /// <summary>
    /// The defending slot for the offense's selected shooter — same number, defense side.
    /// Throws if no slot has been selected (the selection roll must run before the make door).
    /// </summary>
    public static Slot Pick(PossessionState state)
    {
        var selected = state.SelectedSlot
            ?? throw new InvalidOperationException(
                "DefenderPicker requires a stamped SelectedSlot — the selection roll must run before the make door.");

        return new Slot(state.Defense, selected.Number);
    }
}
