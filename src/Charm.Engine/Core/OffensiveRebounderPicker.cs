namespace Charm.Engine;

/// <summary>
/// Picks WHICH offensive player secured an offensive rebound, conditional on
/// Roll I already awarding the board to the offense (Phase 31, v1; Phase 35
/// adds the wingspan factor).
///
/// <para><b>Weight formula — echoes BOTH of the team battle's terms (S46).</b> Each
/// offensive player's pick weight is
/// <c>(Luck + OffensiveRebounding × PositionalWeight(Postness) × ReboundWingspanMultiplier
/// × HustleMultiplier + BodyPull × max(0, ReboundPhysical − lineupMean)
/// + FloorCeiling × tanh(max(0, ReboundPhysical − FloorReference) / FloorScale)) × shooterNerf</c>.
/// The skill product is the identical per-player term that
/// <see cref="Matchup.OffensiveReboundShare"/> sums into its skill shift; the S46
/// additive body term mirrors the team battle's SIZE term (same
/// <see cref="Matchup.ReboundPhysical"/> composite), so the individual pick now
/// reflects the same body-and-skill mix that won the board — MORE consistent with the
/// team battle than the old skill-only echo. The body term is one-sided (a below-mean
/// body gets zero, never a second penalty) and stands alone at rating 0, which the old
/// multiplier-on-the-rating shape structurally could not provide (S45 finding). The
/// S46b saturating body FLOOR adds a second, absolute channel — raw size vs a fixed
/// reference — so a bigger body claims more random loose balls regardless of teammates,
/// tanh-saturated so a genuine big does not balloon. The luck weight is every slot's
/// equal claim on uncontested bounces; it replaces the retired floor of 1. The shooter nerf fires only on perimeter zones (Three / Long /
/// Mid) and multiplies the WHOLE weight — luck and body included — per the S46 ruling
/// that the nerf models reduced availability after shooting, not a skill-specific
/// penalty.</para>
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
    /// <c>(Luck + OffensiveRebounding × PositionalWeight(Postness) × ReboundWingspanMultiplier
    /// × HustleMultiplier + BodyPull × max(0, ReboundPhysical − lineupMean)
    /// + FloorCeiling × tanh(max(0, ReboundPhysical − FloorReference) / FloorScale)) × shooterNerf</c>,
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
        var physicals   = new double[5];   // S46: ReboundPhysical per player (body pull)
        var populated   = new bool[5];
        var playerCount = 0;

        for (var i = 0; i < 5; i++)
        {
            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot);
            if (p is null) continue;
            postnesses[i] = Matchup.Postness(p, matchupCfg);
            wingspans[i]  = p.Wingspan;
            physicals[i]  = Matchup.ReboundPhysical(p, matchupCfg);
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

        // S46: lineup-mean ReboundPhysical — the body pull is centered on it, mirroring
        // the wingspan multiplier's lineup-mean pattern (same body composite the team
        // battle uses, so "body" means one thing in both rebound steps).
        var meanPhysical = 0.0;
        for (var i = 0; i < 5; i++)
            if (populated[i]) meanPhysical += physicals[i];
        meanPhysical /= playerCount;

        // ── Stage 2: compute per-player pick weights ──────────────────────────────
        // weight = (Luck + OffensiveRebounding × PositionalWeight(postness)
        //                                      × ReboundWingspanMultiplier
        //                                      × HustleMultiplier
        //                + BodyPull × max(0, ReboundPhysical − lineupMean)) × shooterNerf
        // S46: the luck weight (every slot's equal claim on uncontested bounces)
        // replaces the retired floor of 1 and keeps every populated slot's draw
        // probability positive (weight ≥ Luck × nerf > 0). The one-sided body term
        // gives a big body standalone pull independent of the rating (block-picker
        // parallel). The nerf multiplies the WHOLE weight — luck and body included —
        // per the S46 ruling: it models reduced availability after shooting.
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
            // offensive boards; centered at 1.0 for a 50-Hustle player. The luck
            // weight below keeps even a low-Hustle player drawing (S46).
            var hm         = 1.0 + matchupCfg.HustleRebounderSteepness
                                 * Math.Tanh((p.Hustle - 50.0) / matchupCfg.HustleRebounderScale);

            var bodyPull   = matchupCfg.ReboundBodyPullWeight
                           * Math.Max(0.0, physicals[i] - meanPhysical);

            // S46b: saturating "big-target" loose-ball floor — ABSOLUTE size vs a FIXED
            // reference (not the lineup mean), so a bigger body earns more of the random
            // caroms regardless of teammates; tanh saturates so a genuine big does not
            // balloon. Separates the mushy bottom of the height ladder. INSIDE the
            // shooterNerf grouping (S46 ruling: the nerf reduces the whole weight — luck,
            // body, and floor included — modeling reduced availability after shooting).
            var absFloor   = matchupCfg.ReboundBodyFloorCeiling
                           * Math.Tanh(Math.Max(0.0, physicals[i] - matchupCfg.ReboundBodyFloorReference)
                                       / matchupCfg.ReboundBodyFloorScale);

            weights[i]   = (matchupCfg.ReboundLuckWeight
                           + p.OffensiveRebounding * pw * wm * hm
                           + bodyPull
                           + absFloor) * shooterNerf;
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
