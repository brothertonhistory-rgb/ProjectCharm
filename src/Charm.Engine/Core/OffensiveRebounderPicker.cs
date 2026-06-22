namespace Charm.Engine;

/// <summary>
/// Picks WHICH offensive player secured an offensive rebound, conditional on
/// Roll I already awarding the board to the offense (Phase 31, v1; Phase 35
/// adds the wingspan factor).
///
/// <para><b>Weight formula — echoes the team math exactly.</b> Each offensive
/// player's pick weight is
/// <c>OffensiveRebounding × PositionalWeight(Postness) × ReboundWingspanMultiplier × shooterNerf</c> —
/// the identical per-player term that <see cref="Matchup.OffensiveReboundShare"/>
/// sums to produce the team weighted-mean, now extended with the same
/// <see cref="Matchup.ReboundWingspanMultiplier"/> added to the battle in Phase 35.
/// Using the same <see cref="Matchup"/> statics (not a re-implementation) keeps the
/// two layers provably consistent: whoever the team battle treats as the dominant
/// rebounder is also whom this pick favors. The shooter nerf fires only on perimeter
/// zones (Three / Long / Mid), matching the team math's offense loop exactly.</para>
///
/// <para><b>Conditional-within-side (Option A).</b> The pick fires DOWNSTREAM of
/// Roll I's offense-vs-defense verdict — it never re-litigates whether the offense
/// won the board, only who on the offense got it. This is one source of truth for
/// the offensive-rebound rate. Option B (unified ten-player contest replacing Roll
/// I's team share) is the named future architecture and is explicitly deferred.</para>
///
/// <para><b>Known limitation (record, do not fix here).</b> As a normalize-among-five
/// within-side share, a weak-rebounding offense that DOES secure a board will see
/// its share spread among five weak players, inflating the worst rebounder's share
/// above his true ~1% raw OR%. This is mitigated (not eliminated) by the conditional
/// structure: Roll I already makes "weak offense wins the board" rare. The full fix
/// is Option B, deferred. Phase 31 ships Option A and records this.</para>
///
/// <para><b>Seam.</b> A distinct, named, swappable unit — the offensive-rebound
/// analogue of <see cref="DefenderPicker"/>. A future positional / box-out model
/// drops in here without touching any consumer.</para>
/// </summary>
public static class OffensiveRebounderPicker
{
    /// <summary>
    /// Picks and returns the offensive <see cref="Slot"/> that secured the rebound.
    /// Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated offensive player:
    /// <c>max(1, OffensiveRebounding × PositionalWeight(Postness) × ReboundWingspanMultiplier × shooterNerf)</c>,
    /// normalized among the five slots. Null slots contribute 0.
    /// Throws <see cref="InvalidOperationException"/> if no offensive slot is
    /// populated — an offensive rebound with zero offensive players is an
    /// unreachable, loud bug.</para>
    /// </summary>
    /// <param name="state">Current possession state. <see cref="PossessionState.SelectedSlot"/>
    /// identifies the shooter (for the nerf); <see cref="PossessionState.ShotType"/>
    /// determines whether the shooter nerf applies (Three / Long / Mid zones only —
    /// null or Rim / Short means no nerf, matching the team math).</param>
    /// <param name="game">Live game state — provides the offensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the Postness coefficients,
    /// PositionalWeight swing/scale, ReboundShooterNerf, and the Phase 35 wingspan
    /// parameters (<see cref="MatchupConfig.ReboundWingspanSwing"/>,
    /// <see cref="MatchupConfig.ReboundWingspanScale"/>). Same config the team battle
    /// uses, so the two layers are definitionally consistent.</param>
    /// <param name="rng">RNG source. Consumes exactly one NextUnitInterval draw.</param>
    public static Slot Pick(
        PossessionState state,
        GameState       game,
        MatchupConfig   matchupCfg,
        IRng            rng)
    {
        var offense  = state.Offense;
        var lineup   = game.LineupFor(offense);
        var roster   = game.RosterFor(offense);

        // Zones where the shooter nerf applies — mirrors the team math's nerfZones check
        // in Matchup.OffensiveReboundShare exactly.
        var nerfZones = state.ShotType is ShotLocation.Three
                                       or ShotLocation.Long
                                       or ShotLocation.Mid;

        // ── Stage 1: compute postness and wingspan for each populated offensive player ──
        // Mirrors the offense loop in Matchup.OffensiveReboundShare: same per-player
        // term, same lineup-mean baseline, same Matchup statics.
        var postnesses  = new double[5];
        var wingspans   = new double[5];
        var populated   = new bool[5];
        var playerCount = 0;

        for (var i = 0; i < 5; i++)
        {
            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot);
            if (p is null) continue;
            postnesses[i] = Matchup.Postness(p, matchupCfg);
            wingspans[i]  = p.Wingspan;
            populated[i]  = true;
            playerCount++;
        }

        if (playerCount == 0)
            throw new InvalidOperationException(
                "OffensiveRebounderPicker: no offensive players populated — " +
                "an offensive rebound with zero players on the floor is an unreachable bug.");

        var meanPostness = 0.0;
        for (var i = 0; i < 5; i++)
            if (populated[i]) meanPostness += postnesses[i];
        meanPostness /= playerCount;

        var meanWingspan = 0.0;
        for (var i = 0; i < 5; i++)
            if (populated[i]) meanWingspan += wingspans[i];
        meanWingspan /= playerCount;

        // ── Stage 2: compute per-player pick weights ──────────────────────────────
        // weight = max(1, OffensiveRebounding × PositionalWeight(postness)
        //                                     × ReboundWingspanMultiplier
        //                                     × shooterNerf)
        // The floor of 1 ensures every populated slot has a positive (if tiny) draw
        // probability, matching the intent of the team math's positional weighting.
        var weights   = new double[5];
        var totalWeight = 0.0;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;

            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot)!;   // non-null: populated[i] is true

            var pw         = Matchup.PositionalWeight(postnesses[i], meanPostness, matchupCfg);
            var wm         = Matchup.ReboundWingspanMultiplier(wingspans[i], meanWingspan, matchupCfg);
            var isShooter  = state.SelectedSlot is { } sel
                             && sel.Side   == offense
                             && sel.Number == slot.Number;
            var shooterNerf = isShooter && nerfZones ? matchupCfg.ReboundShooterNerf : 1.0;

            // Phase 45: per-player Hustle tilt (tanh, same shape as the wingspan
            // multiplier). A higher-Hustle player absorbs a larger share of his team's
            // offensive boards; centered at 1.0 for a 50-Hustle player. The Math.Max(1)
            // floor below keeps even a low-Hustle player drawing.
            var hm         = 1.0 + matchupCfg.HustleRebounderSteepness
                                 * Math.Tanh((p.Hustle - 50.0) / matchupCfg.HustleRebounderScale);

            weights[i]   = Math.Max(1.0, p.OffensiveRebounding * pw * wm * hm * shooterNerf);
            totalWeight += weights[i];
        }

        // ── Stage 3: one RNG draw — cumulative walk to chosen slot ───────────────
        // Same shape as Pie<T>.Roll: walk the cumulative sum, return the first slot
        // whose cumulative weight exceeds the draw. The final populated slot is the
        // implicit fallback (absorbs floating-point shortfall).
        var draw      = rng.NextUnitInterval() * totalWeight;
        var cumulative = 0.0;
        var lastPopulated = -1;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;
            lastPopulated = i;
            cumulative += weights[i];
            if (draw <= cumulative)
                return lineup.SlotAt(i + 1);
        }

        // Fallback: floating-point edge — return the last populated slot.
        return lineup.SlotAt(lastPopulated + 1);
    }
}
