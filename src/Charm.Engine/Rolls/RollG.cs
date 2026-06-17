namespace Charm.Engine;

/// <summary>
/// Roll G — Shot Location. The beat right after a clean shot attempt gets off
/// (Roll F's ShotAttempt): stamps WHERE the shot comes from — one of five zones
/// (Three / Long / Mid / Short / Rim) — onto the possession, then hands off to the
/// (future) make/miss beat that resolves whether it goes in.
///
/// Structurally Roll E, not Roll F: like Roll E, every outcome stamps a fact and
/// continues to the SAME next beat. The only thing that differs per outcome is
/// WHICH zone gets stamped. (Roll E stamped a SelectedSlot and all five slots
/// emitted IntoPlayerAction; Roll G stamps a ShotType and all five zones emit
/// IntoShotResolution.) It is NOT a gate like Roll F, whose outcomes branch to
/// different nodes.
///
/// Unlike Roll E, it needs NO GameState: a shot zone is just an enum value, with
/// nothing to look up on persistent state. So its signature is (state, pie, rng) —
/// the same shape as Roll F, not Roll E.
///
/// Follows the uniform roll contract: receives state + a finished pie, rolls
/// against it, returns one typed result, names no successor (the resolver maps the
/// kind).
///
/// What it does:
///   1. Rolls the five-way pie to a <see cref="ShotLocation"/>.
///   2. Stamps the chosen zone onto the possession as a per-possession fact
///      (<c>state with { ShotType = zone }</c>) — the SECOND per-possession fact,
///      after Roll E's SelectedSlot.
///   3. Returns <c>Continue(IntoShotResolution)</c> carrying that updated state.
///      All five zones emit the SAME kind.
///
/// Why the zone lands on PossessionState (not on the Continue as payload): like
/// the selected slot, it is a DURABLE per-possession fact the future make/miss
/// roll (Roll H) reads — Roll H resolves the matchup from BOTH SelectedSlot AND
/// ShotType. That is PossessionState's job. (Roll D's Bonus rides the Continue
/// because it is transient routing input consumed by the very next node and never
/// persists — the opposite case.)
///
/// What it does NOT do: tilt the odds. WHICH zone a player shoots from (shot
/// selection, role, defensive pressure) is the deferred player/attribute model,
/// delivered later as a smarter generator that hands Roll G a non-flat pie over
/// the same enum. Roll G itself never changes when that lands. It also does NOT
/// resolve make/miss, fouls, or shot quality — all of that is Roll H.
/// </summary>
public static class RollG
{
    public static RollResult Execute(
        PossessionState state, Pie<ShotLocation> pie, double residualPressure, IRng rng)
    {
        // 1. Roll the pie to a shot location.
        var zone = pie.Roll(rng.NextUnitInterval());

        // 2. Stamp both the chosen zone AND the residual pressure onto the possession
        //    as per-possession facts (ShotType and UsageResidualPressure). One `with`
        //    keeps both writes atomic — mirrors how Roll E stamps SelectedSlot and
        //    UsagePressure together.
        var stampedState = state with
        {
            ShotType              = zone,
            UsageResidualPressure = residualPressure,
        };

        // 3. Hand off to the make/miss beat (Roll H). Names the KIND, not
        //    the node; the resolver maps it.
        return new Continue(ContinuationKind.IntoShotResolution, stampedState);
    }
}
