namespace Charm.Engine;

/// <summary>
/// A bare on-court identity: a stable, numbered position on a team that a roll
/// can NAME and the (future) attribution layer can point a stat AT. Mirrors
/// <see cref="TeamSide"/> — pure identity, zero attributes, owned by nothing.
///
/// Numbered 1–5 to mimic basketball's addressing, but the number is IDENTITY,
/// NOT ROLE: slot 1 is not "the point guard." What kind of player belongs in a
/// slot is a lineup-assignment decision made later, above this layer. Keeping
/// role out of the slot is what lets management nodes (lineup-setting, subs,
/// rotations, matchup assignment) stack on top as clean consumers without any
/// of them fighting a meaning baked into the slot.
///
/// The number is INTRINSIC and STABLE: "Home slot 3" is the same position all
/// game. A substitution swaps WHO fills slot 3, never what slot 3 IS — so a stat
/// attributed to Home slot 3 stays coherent across subs. (Same reason Roll D
/// charges the fixed TeamSide identity, not a moving ball-handler.)
///
/// Deliberately empty. No fill, no rating, no modifier hook — not even an inert
/// one. The rated player that fills this is data that flows in later; it
/// attaches TO the slot, the slot carries nothing for it now.
/// </summary>
public readonly record struct Slot(TeamSide Side, int Number);
