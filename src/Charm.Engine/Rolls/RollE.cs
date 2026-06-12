namespace Charm.Engine;

/// <summary>
/// Roll E — Player Selection. The first roll whose output is "an identity to
/// attribute to" rather than "a branch in the chain." It picks WHICH of the five
/// on-court offensive slots the possession runs through this time, then hands off
/// to the (future) player-action sequence (shot creation / shot quality /
/// make-miss / rebound / shooting foul) that will resolve what happens TO that
/// player.
///
/// Follows the uniform roll contract: receives state + a finished pie, rolls
/// against it, returns one typed result, names no successor (the resolver maps
/// the kind). Like Roll D, it also takes <see cref="GameState"/> — not to mutate
/// it, but to reach the offense's lineup via <c>LineupFor</c> and name a real
/// slot. Reaching persistent game state is what a selection IS; the uniform shape
/// (receive state + pie, roll, name no successor) holds.
///
/// What it does:
///   1. Rolls the flat five-way pie to a <see cref="SelectionOutcome"/>.
///   2. Maps that outcome to a slot number 1–5 and names the real slot on the
///      OFFENSE's lineup (<c>game.LineupFor(state.Offense).SlotAt(n)</c>).
///   3. Stamps the chosen slot onto the possession as a per-possession fact
///      (<c>state with { SelectedSlot = slot }</c>) — a slot REFERENCE into the
///      game-scoped lineup, never an owned or attribute-bearing thing.
///   4. Returns <c>Continue(IntoPlayerAction)</c> carrying that updated state.
///
/// Why the slot lands on PossessionState (not on the Continue as payload): the
/// selected slot is a DURABLE per-possession fact that several future rolls (shot
/// creation, quality, make-miss, rebound) and the attribution layer all read,
/// across multiple chain hops — the same shape as <c>Offense</c>/<c>Defense</c>.
/// That is PossessionState's job. (Roll D's <c>Bonus</c> rides the Continue
/// because it is transient routing input consumed by the very next node and never
/// persists — the opposite case.)
///
/// What it does NOT do: tilt the odds. WHO is more likely to get the ball (usage,
/// hierarchy, ball-dominance, attributes, coaching) is the deferred
/// player/attribute model, delivered later as a smarter generator that hands Roll
/// E a non-flat pie. Roll E itself never changes when that lands.
/// </summary>
public static class RollE
{
    public static RollResult Execute(
        PossessionState state, Pie<SelectionOutcome> pie, GameState game, IRng rng)
    {
        // 1. Roll the pie to a selection outcome.
        var outcome = pie.Roll(rng.NextUnitInterval());

        // 2. Map outcome -> slot number 1–5 (declaration position), then name the
        //    real slot on the OFFENSE's lineup. This walks the seam Session 7
        //    left: possession role -> LineupFor -> SlotAt -> a nameable slot.
        var slotNumber = (int)outcome + 1;                       // Slot1 -> 1, ... Slot5 -> 5
        var slot = game.LineupFor(state.Offense).SlotAt(slotNumber);

        // 3. Stamp the chosen slot onto the possession as a per-possession fact.
        //    PossessionState is an immutable record, so this is a `with`-expression
        //    producing a NEW state (not a mutation) — exactly how the resolver
        //    already threads state through its loop.
        var selectedState = state with { SelectedSlot = slot };

        // 4. Hand off to the (future) player-action sequence. Names the KIND, not
        //    the node; the resolver maps it. The selected slot rides on the
        //    carried state, not as a Continue payload.
        return new Continue(ContinuationKind.IntoPlayerAction, selectedState);
    }
}
