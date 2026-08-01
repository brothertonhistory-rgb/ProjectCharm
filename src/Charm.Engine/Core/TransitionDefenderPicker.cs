namespace Charm.Engine;

/// <summary>
/// S88 — WHO GOT BACK. Draws the ONE defender who is there on this break, weighted by each
/// man's got-back number (<see cref="TransitionDefense.LineupGotBack"/>).
///
/// <para><b>R1 — a weighted draw, not a threshold.</b> Faster players are MORE LIKELY to be
/// the one there; nobody is ever impossible. That is the whole reason there is a luck floor
/// inside the got-back number and a draw here rather than "the fastest man gets back".</para>
///
/// <para><b>One draw, before resolution, independent of outcome.</b> The Resolver draws the
/// defender once per in-scope break shot and passes him into Roll H on a
/// <see cref="TransitionContest"/>; crediting a block consumes no further draw, because the
/// man credited is the man who was already drawn. That is what keeps the contested man and
/// the credited man the same person — an invariant no conservation check can see.</para>
///
/// <para><b>No all-zero-weight branch, and that is proven rather than assumed.</b> A got-back
/// number is <c>floor + legs × depth × zone</c>; both spans are guarded below 1 and every
/// zone multiplier above 0, so all three factors are strictly positive and the product cannot
/// reach zero even with the luck floor legally set to 0. Swept against the locked oracle over
/// the legal span corners against extreme players at a zero floor, the minimum got-back
/// number is 2.52e-08 — small, strictly positive. A zero-total branch here would be dead
/// code, so there is not one; the guard below is loud rather than silent if that ever
/// changes.</para>
///
/// <para>Structural sibling of <see cref="BlockerPicker"/> and
/// <see cref="TurnoverInteriorPicker"/>: gather the occupied seats, one
/// <see cref="IRng.NextUnitInterval"/> draw, cumulative walk, last occupied seat absorbs the
/// floating-point edge.</para>
/// </summary>
public static class TransitionDefenderPicker
{
    /// <summary>
    /// Draw the defender who got back, weighted by the supplied got-back numbers.
    /// </summary>
    /// <param name="defense">Which side is defending — the drawn <see cref="Slot"/> carries
    /// it, so the side is never inferred downstream.</param>
    /// <param name="lineup">The DEFENSIVE lineup, so the returned slot is a real seat
    /// identity rather than a fabricated one.</param>
    /// <param name="weights">Five got-back numbers by slot number (index <c>i</c> = slot
    /// <c>i + 1</c>), exactly as returned by
    /// <see cref="TransitionDefense.LineupGotBack"/>: <b>0.0 marks an empty seat</b> and is
    /// never a candidate.</param>
    /// <param name="rng">RNG source. Consumes exactly one
    /// <see cref="IRng.NextUnitInterval"/> draw.</param>
    /// <returns>The defensive slot of the man who got back.</returns>
    public static Slot Pick(
        TeamSide            defense,
        Lineup              lineup,
        IReadOnlyList<double> weights,
        IRng                rng)
    {
        if (lineup.Side != defense)
            throw new InvalidOperationException(
                $"TransitionDefenderPicker: lineup side {lineup.Side} does not match the defending side {defense}.");

        var totalWeight = 0.0;
        var occupied    = 0;
        for (var i = 0; i < 5; i++)
        {
            if (weights[i] <= 0.0) continue;   // empty seat
            totalWeight += weights[i];
            occupied++;
        }

        // Matrix row 1 is handled UPSTREAM: with nobody on the floor the got-back path is
        // never entered and no context is built, so no slot is ever fabricated here.
        if (occupied == 0)
            throw new InvalidOperationException(
                "TransitionDefenderPicker: no defenders on the floor — a break contest with an " +
                "empty defensive lineup must be short-circuited before the draw (matrix row 1).");

        if (totalWeight <= 0.0)
            throw new InvalidOperationException(
                $"TransitionDefenderPicker: total got-back weight {totalWeight} across {occupied} " +
                "occupied seats — every factor is strictly positive, so this should be unreachable.");

        var draw         = rng.NextUnitInterval() * totalWeight;
        var cumulative   = 0.0;
        var lastOccupied = -1;

        for (var i = 0; i < 5; i++)
        {
            if (weights[i] <= 0.0) continue;
            lastOccupied = i;
            cumulative  += weights[i];
            if (draw <= cumulative)
                return lineup.SlotAt(i + 1);
        }

        // Floating-point edge — the last occupied seat absorbs it (same as the sibling pickers).
        return lineup.SlotAt(lastOccupied + 1);
    }
}
