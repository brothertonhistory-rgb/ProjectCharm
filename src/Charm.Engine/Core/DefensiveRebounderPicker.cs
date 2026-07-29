namespace Charm.Engine;

/// <summary>
/// Picks WHICH defensive player earned a defensive rebound — stamped on-walk at the
/// <c>Terminal("DefensiveRebound")</c> exit (Phase 35), retiring the last post-hoc
/// harness <c>WeightedDraw</c> for rebounding.
///
/// <para><b>Weight formula — mirrors <see cref="OffensiveRebounderPicker"/> exactly,
/// defensive side, no shooterNerf (S46 shape).</b> Each defensive player's pick weight is
/// <c>ReboundLuckWeight + DefensiveRebounding × PositionalWeight(Postness) × ReboundWingspanMultiplier
/// × HustleMultiplier + ReboundBodyPullWeight × max(0, ReboundPhysical − lineupMean)
/// + ReboundBodyFloorCeiling × tanh(max(0, ReboundPhysical − ReboundBodyFloorReference) / ReboundBodyFloorScale)</c>.
/// The <see cref="Matchup.ReboundWingspanMultiplier"/> is the same shared helper used by
/// the offensive picker. The S46 additive body term (the block-picker parallel) gives a
/// big body standalone individual rebounding pull independent of the rating — one-sided,
/// so a below-mean body gets zero, never a second penalty. The S46b saturating body
/// FLOOR is a second, absolute channel — raw size vs a fixed reference (not the lineup
/// mean) — so a bigger body claims more of the random loose balls regardless of
/// teammates; tanh saturation keeps a genuine big from ballooning. The luck weight is
/// every populated slot's equal claim on uncontested bounces; it replaces the retired
/// floor of 1 and guarantees a nonzero draw probability (weight ≥ Luck &gt; 0).</para>
///
/// <para><b>No shooterNerf.</b> The offense has a shooter whose body is still moving away
/// from the basket; the defense does not. Confirmed in adversarial check #5: there is no
/// defense-side "shooter" concept, so the formula is the offensive picker minus that term.
/// </para>
///
/// <para><b>RNG stream.</b> Consumes exactly one <see cref="IRng.NextUnitInterval"/>
/// draw from the engine RNG on every <c>DefensiveRebound</c> terminal. Called inside
/// the <c>case Terminal t:</c> stamp block immediately after the reason is confirmed
/// <c>"DefensiveRebound"</c>. Documented stream shift: every downstream engine draw
/// on those possessions shifts (same consequence as Phases 31, 33, and 34). The corpus
/// hash changes; same-seed reproducibility within Phase 35 holds.</para>
/// </summary>
public static class DefensiveRebounderPicker
{
    /// <summary>
    /// Picks and returns the defensive <see cref="Slot"/> that earned the rebound.
    /// Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated defensive player:
    /// <c>Luck + DefensiveRebounding × PositionalWeight(Postness) × ReboundWingspanMultiplier
    /// × HustleMultiplier + BodyPull × max(0, ReboundPhysical − lineupMean)
    /// + FloorCeiling × tanh(max(0, ReboundPhysical − FloorReference) / FloorScale)</c>,
    /// normalized among the five slots. Null slots contribute 0.
    /// Throws <see cref="InvalidOperationException"/> if no defensive slot is
    /// populated — a defensive rebound with zero defenders on the floor is an
    /// unreachable, loud bug.</para>
    /// </summary>
    /// <param name="state">Current possession state. Provides the defensive side.</param>
    /// <param name="game">Live game state — provides the defensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the Postness coefficients,
    /// PositionalWeight swing/scale, and the Phase 35 wingspan parameters
    /// (<see cref="MatchupConfig.ReboundWingspanSwing"/>,
    /// <see cref="MatchupConfig.ReboundWingspanScale"/>).</param>
    /// <param name="rng">RNG source. Consumes exactly one NextUnitInterval draw.</param>
    public static Slot Pick(
        PossessionState state,
        GameState       game,
        MatchupConfig   matchupCfg,
        IRng            rng)
    {
        var defense = state.Defense;
        var lineup  = game.LineupFor(defense);
        var roster  = game.RosterFor(defense);

        // ── Stage 1: compute postness and wingspan for each populated defensive player ──
        // Mirrors OffensiveRebounderPicker's Stage 1 exactly, applied to the defensive
        // lineup. Same Matchup statics, same lineup-mean baseline.
        var postnesses  = new double[5];
        var wingspans   = new double[5];
        var physicals   = new double[5];   // S46: ReboundPhysical per player (body pull)
        var verticals   = new double[5];   // S81.2: the leap, for the within-team attribution tilt
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
            verticals[i]  = p.Vertical;
            populated[i]  = true;
            playerCount++;
        }

        if (playerCount == 0)
            throw new InvalidOperationException(
                "DefensiveRebounderPicker: no defensive players populated — " +
                "a defensive rebound with zero defenders on the floor is an unreachable bug.");

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

        // S81.2: lineup-mean hops — the leap tilt is TEAMMATE-relative, so this is its
        // neutral point. Over POPULATED players only (playerCount, never a fixed five),
        // matching every other mean in this method and the live rebound convention.
        // Computed ONCE here, BEFORE any candidate weight — a mean recomputed inside the
        // weight loop would make each weight depend on itself.
        var meanVertical = 0.0;
        for (var i = 0; i < 5; i++)
            if (populated[i]) meanVertical += verticals[i];
        meanVertical /= playerCount;

        // ── Stage 2: compute per-player pick weights ──────────────────────────────
        // weight = Luck + DefensiveRebounding × PositionalWeight(postness)
        //                                     × ReboundWingspanMultiplier
        //                                     × HustleMultiplier
        //               + BodyPull × max(0, ReboundPhysical − lineupMean)
        // No shooterNerf — the defense has no shooter (adversarial check #5).
        // S46: the luck weight (every slot's equal claim on uncontested bounces)
        // replaces the retired floor of 1 and keeps every populated slot's draw
        // probability nonzero (weight ≥ Luck > 0). The one-sided body term gives a
        // big body standalone pull independent of the rating (block-picker parallel).
        var weights     = new double[5];
        var totalWeight = 0.0;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;

            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot)!;   // non-null: populated[i] is true

            var pw = Matchup.PositionalWeight(postnesses[i], meanPostness, matchupCfg);
            var wm = Matchup.ReboundWingspanMultiplier(wingspans[i], meanWingspan, matchupCfg);

            // Phase 45: per-player Hustle tilt (tanh, same shape as the offensive picker).
            // A higher-Hustle defender absorbs a larger share of his team's defensive
            // boards; centered at 1.0 for a 50-Hustle player.
            var hm = 1.0 + matchupCfg.HustleRebounderSteepness
                         * Math.Tanh((p.Hustle - 50.0) / matchupCfg.HustleRebounderScale);

            var bodyPull  = matchupCfg.ReboundBodyPullWeight
                          * Math.Max(0.0, physicals[i] - meanPhysical);

            // S46b: saturating "big-target" loose-ball floor — ABSOLUTE size vs a FIXED
            // reference (not the lineup mean), so a bigger body earns more of the random
            // caroms regardless of teammates; tanh saturates so a genuine big does not
            // balloon. Separates the mushy bottom of the height ladder.
            var absFloor  = matchupCfg.ReboundBodyFloorCeiling
                          * Math.Tanh(Math.Max(0.0, physicals[i] - matchupCfg.ReboundBodyFloorReference)
                                      / matchupCfg.ReboundBodyFloorScale);

            // S81.2: the leap joins the SKILL PRODUCT only — same rule as the offensive
            // picker. Luck, bodyPull and absFloor stay independent of the rebounding rating.
            var vm = Matchup.ReboundVerticalMultiplier(verticals[i], meanVertical, matchupCfg);

            weights[i]   = matchupCfg.ReboundLuckWeight
                         + p.DefensiveRebounding * pw * wm * hm * vm
                         + bodyPull
                         + absFloor;
            totalWeight += weights[i];
        }

        // ── Stage 3: one RNG draw — cumulative walk to chosen slot ───────────────
        // Same shape as OffensiveRebounderPicker and StealerPicker: walk the
        // cumulative sum, return the first slot whose cumulative weight exceeds the
        // draw. The final populated slot is the implicit fallback (absorbs
        // floating-point shortfall).
        var draw          = rng.NextUnitInterval() * totalWeight;
        var cumulative    = 0.0;
        var lastPopulated = -1;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;
            lastPopulated = i;
            cumulative   += weights[i];
            if (draw <= cumulative)
                return lineup.SlotAt(i + 1);
        }

        // Fallback: floating-point edge — return the last populated slot.
        return lineup.SlotAt(lastPopulated + 1);
    }
}
