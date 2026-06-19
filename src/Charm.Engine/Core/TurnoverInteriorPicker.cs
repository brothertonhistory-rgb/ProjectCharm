namespace Charm.Engine;

/// <summary>
/// Picks WHICH offensive player committed an interior-type turnover —
/// <b>ThreeSecondViolation</b>, <b>OffensiveGoaltending</b>, or
/// <b>OffensiveFoul</b> — events where post players are disproportionately
/// likely committers (Phase 34).
///
/// <para><b>Weight formula — post-weighted, guard-floored.</b> Each offensive
/// player's pick weight is
/// <c>max(1, Strength × interiorMult(postness, lineupMeanPostness))</c>.
/// The interior multiplier slides from ~1.0 for a high-postness (post) player
/// up toward 1.0, and floors near
/// <see cref="MatchupConfig.TurnoverInteriorGuardFloor"/> for a low-postness
/// (guard) player. This is the <b>inversion</b> of
/// <see cref="TurnoverCommitterPicker"/>: where that class suppresses posts,
/// this class suppresses guards.</para>
///
/// <para><b>Formula detail.</b>
/// <c>raw  = tanh((postness[i] − lineupMean) / TurnoverInteriorPostnessScale)</c>  // −1..+1
/// <c>mult = TurnoverInteriorGuardFloor + (1 − GuardFloor) × ((raw + 1) / 2)</c>   // inverted direction
/// A post (raw &gt; 0) gets <c>(raw+1)/2 &gt; 0.5</c> → mult high.
/// A guard (raw &lt; 0) gets <c>(raw+1)/2 &lt; 0.5</c> → mult near GuardFloor.</para>
///
/// <para><b>RNG stream.</b> Consumes exactly one <see cref="IRng.NextUnitInterval"/>
/// draw from the engine RNG on every possession routed to this picker. Called only
/// when <see cref="PossessionState.SelectedSlot"/> is null (pre-selection path);
/// post-selection possessions credit the already-selected slot directly with no
/// RNG draw.</para>
/// </summary>
public static class TurnoverInteriorPicker
{
    /// <summary>
    /// Picks and returns the offensive <see cref="Slot"/> that committed an
    /// interior-type turnover (ThreeSecondViolation, OffensiveGoaltending,
    /// OffensiveFoul). Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated offensive player:
    /// <c>max(1, Strength × interiorMult(postness, lineupMeanPostness))</c>,
    /// normalized among the five slots. Null slots contribute 0.
    /// Throws <see cref="InvalidOperationException"/> if no offensive slot is
    /// populated — an interior turnover with zero players on the floor is an
    /// unreachable, loud bug.</para>
    /// </summary>
    /// <param name="state">Current possession state. Provides the offensive side.</param>
    /// <param name="game">Live game state — provides the offensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the Postness coefficients
    /// and the Phase 34 interior-gating parameters
    /// (<see cref="MatchupConfig.TurnoverInteriorGuardFloor"/>,
    /// <see cref="MatchupConfig.TurnoverInteriorPostnessScale"/>).</param>
    /// <param name="rng">RNG source. Consumes exactly one NextUnitInterval draw.</param>
    public static Slot Pick(
        PossessionState state,
        GameState       game,
        MatchupConfig   matchupCfg,
        IRng            rng)
    {
        var offense = state.Offense;
        var lineup  = game.LineupFor(offense);
        var roster  = game.RosterFor(offense);

        // ── Stage 1: compute postness for each populated offensive player ─────────
        // Reuses the same Matchup.Postness the rebound battle and TurnoverCommitterPicker
        // use — same coefficients, same lineup-mean baseline.
        var postnesses  = new double[5];
        var populated   = new bool[5];
        var playerCount = 0;

        for (var i = 0; i < 5; i++)
        {
            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot);
            if (p is null) continue;
            postnesses[i] = Matchup.Postness(p, matchupCfg);
            populated[i]  = true;
            playerCount++;
        }

        if (playerCount == 0)
            throw new InvalidOperationException(
                "TurnoverInteriorPicker: no offensive players populated — " +
                "an interior turnover with zero players on the floor is an unreachable bug.");

        var meanPostness = 0.0;
        for (var i = 0; i < 5; i++)
            if (populated[i]) meanPostness += postnesses[i];
        meanPostness /= playerCount;

        // ── Stage 2: compute per-player pick weights ──────────────────────────────
        // weight = max(1, Strength × interiorMult(postness, lineupMean))
        // interiorMult: tanh-based, inverted from TurnoverCommitterPicker.
        //   raw  = tanh((postness[i] − lineupMean) / InteriorPostnessScale)  // −1..+1
        //   mult = GuardFloor + (1 − GuardFloor) × ((raw + 1) / 2)           // NOT inverted
        // A post (raw > 0) → (raw+1)/2 > 0.5 → mult high (posts favored).
        // A guard (raw < 0) → (raw+1)/2 < 0.5 → mult near GuardFloor (guards suppressed).
        // The floor of 1 ensures every populated slot has a positive draw probability
        // even for a Strength=0 player or a maximally-suppressed guard.
        var weights     = new double[5];
        var totalWeight = 0.0;

        var guardFloor    = matchupCfg.TurnoverInteriorGuardFloor;
        var postnessScale = matchupCfg.TurnoverInteriorPostnessScale;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;

            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot)!;   // non-null: populated[i] is true

            var raw  = Math.Tanh((postnesses[i] - meanPostness) / postnessScale);
            var mult = guardFloor + (1.0 - guardFloor) * ((raw + 1.0) / 2.0);

            weights[i]   = Math.Max(1.0, p.Strength * mult);
            totalWeight += weights[i];
        }

        // ── Stage 3: one RNG draw — cumulative walk to chosen slot ───────────────
        // Same shape as TurnoverCommitterPicker: walk the cumulative sum, return
        // the first slot whose cumulative weight exceeds the draw. The final
        // populated slot is the implicit fallback (absorbs floating-point shortfall).
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
