namespace Charm.Engine;

/// <summary>
/// Picks WHICH offensive player committed a turnover on the <b>pre-selection</b>
/// paths (Roll A entry/bring-up, Roll B halfcourt-initiation) — the paths where
/// Roll E had not yet named a shooter (Phase 33, v1).
///
/// <para><b>Weight formula — handling-weighted, perimeter-gated.</b> Each offensive
/// player's pick weight is
/// <c>max(1, BallHandling × perimeterMult(postness, lineupMeanPostness))</c>.
/// The perimeter multiplier slides from ~1.0 for a low-postness (guard) player
/// down toward <see cref="MatchupConfig.TurnoverCommitterPostFloor"/> for a
/// high-postness (post) player, using the same <see cref="Matchup.Postness"/>
/// coefficients the rebound battle uses — no new postness math, only a new consumer
/// that maps the multiplier the opposite direction (posts suppressed for turnovers
/// rather than favored for rebounding).</para>
///
/// <para><b>Post-selection turnovers are unchanged.</b> When Roll E had already run
/// and <see cref="PossessionState.SelectedSlot"/> is non-null, the caller
/// (<see cref="Resolver"/>) credits that slot directly with no RNG draw.
/// This picker is called ONLY when <c>SelectedSlot is null</c>.</para>
///
/// <para><b>RNG stream.</b> Consumes exactly one <see cref="IRng.NextUnitInterval"/>
/// draw from the engine RNG on every pre-selection turnover possession. Documented
/// stream shift: every downstream draw on those possessions shifts, matching the
/// identical Phase 31 shift when <see cref="OffensiveRebounderPicker"/> moved
/// on-walk. The corpus hash changes; same-seed reproducibility within Phase 33
/// holds.</para>
///
/// <para><b>Seam.</b> A future reason-aware committer model (illegal-screen →
/// screener, the Roll C Session 2 / Roll D foul-attribution work) drops in adjacent
/// to this picker without touching it.</para>
/// </summary>
public static class TurnoverCommitterPicker
{
    /// <summary>
    /// Picks and returns the offensive <see cref="Slot"/> that committed the
    /// turnover. Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated offensive player:
    /// <c>max(1, BallHandling × perimeterMult(postness, lineupMeanPostness))</c>,
    /// normalized among the five slots. Null slots contribute 0.
    /// Throws <see cref="InvalidOperationException"/> if no offensive slot is
    /// populated — a turnover with zero offensive players is an unreachable, loud
    /// bug.</para>
    /// </summary>
    /// <param name="state">Current possession state. Provides the offensive side;
    /// <see cref="PossessionState.SelectedSlot"/> must be null on this path
    /// (post-selection turnovers are credited directly by the caller).</param>
    /// <param name="game">Live game state — provides the offensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the Postness coefficients
    /// and the Phase 33 perimeter-gating parameters
    /// (<see cref="MatchupConfig.TurnoverCommitterPostFloor"/>,
    /// <see cref="MatchupConfig.TurnoverCommitterPostnessScale"/>).</param>
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
        // Uses the same Matchup.Postness static the rebound battle uses — same
        // coefficients, same lineup-mean baseline.
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
                "TurnoverCommitterPicker: no offensive players populated — " +
                "a turnover with zero players on the floor is an unreachable bug.");

        var meanPostness = 0.0;
        for (var i = 0; i < 5; i++)
            if (populated[i]) meanPostness += postnesses[i];
        meanPostness /= playerCount;

        // ── Stage 2: compute per-player pick weights ──────────────────────────────
        // weight = max(1, BallHandling × perimeterMult(postness, lineupMean))
        // perimeterMult: tanh-based, slides ~1.0 for guards → PostFloor for posts.
        //   raw  = tanh((postness[i] − lineupMean) / PostnessScale)   // −1..+1
        //   mult = PostFloor + (1 − PostFloor) × (1 − (raw + 1) / 2)
        // The floor of 1 ensures every populated slot has a positive draw probability
        // even for a BH=0 player or a maximally-suppressed post.
        var weights     = new double[5];
        var totalWeight = 0.0;

        var postFloor     = matchupCfg.TurnoverCommitterPostFloor;
        var postnessScale = matchupCfg.TurnoverCommitterPostnessScale;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;

            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot)!;   // non-null: populated[i] is true

            var raw  = Math.Tanh((postnesses[i] - meanPostness) / postnessScale);
            var mult = postFloor + (1.0 - postFloor) * (1.0 - (raw + 1.0) / 2.0);

            weights[i]   = Math.Max(1.0, p.BallHandling * mult);
            totalWeight += weights[i];
        }

        // ── Stage 3: one RNG draw — cumulative walk to chosen slot ───────────────
        // Same shape as Pie<T>.Roll and OffensiveRebounderPicker: walk the cumulative
        // sum, return the first slot whose cumulative weight exceeds the draw. The
        // final populated slot is the implicit fallback (absorbs floating-point shortfall).
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
