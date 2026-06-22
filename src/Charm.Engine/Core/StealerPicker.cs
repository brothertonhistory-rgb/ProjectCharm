namespace Charm.Engine;

/// <summary>
/// Picks WHICH defensive player earned a steal on a live-ball turnover —
/// <b>BadPassIntercepted</b> or <b>LostBallLiveBall</b> — events where
/// perimeter defenders are disproportionately likely steal earners (Phase 34).
///
/// <para><b>Weight formula — Steals-weighted, perimeter-gated.</b> Each
/// defensive player's pick weight is
/// <c>max(1, Steals × perimeterMult(defPostness, defLineupMeanPostness))</c>.
/// The perimeter multiplier slides from ~1.0 for a low-postness (guard) defender
/// down toward <see cref="MatchupConfig.StealerPostFloor"/> for a high-postness
/// (post) defender. This is the <b>same direction</b> as
/// <see cref="TurnoverCommitterPicker"/> — guards favored — applied to the
/// <b>defensive</b> lineup.</para>
///
/// <para><b>Formula detail.</b>
/// <c>raw  = tanh((defPostness[i] − defLineupMean) / StealerPostnessScale)</c>  // −1..+1
/// <c>mult = StealerPostFloor + (1 − PostFloor) × (1 − (raw + 1) / 2)</c>       // guards favored
/// A guard (raw &lt; 0) gets <c>1 − (raw+1)/2 &gt; 0.5</c> → mult high.
/// A post (raw &gt; 0) gets <c>1 − (raw+1)/2 &lt; 0.5</c> → mult near PostFloor.</para>
///
/// <para><b>RNG stream.</b> Consumes exactly one <see cref="IRng.NextUnitInterval"/>
/// draw from the engine RNG on every live-ball turnover possession. Called inside
/// the Terminal stamp block immediately after <c>turnoverWasLiveBall</c> is
/// confirmed true. Documented stream shift: every downstream engine draw on those
/// possessions shifts (same consequence as Phase 31 and Phase 33). The corpus hash
/// changes; same-seed reproducibility within Phase 34 holds.</para>
/// </summary>
public static class StealerPicker
{
    /// <summary>
    /// Picks and returns the defensive <see cref="Slot"/> that earned the steal.
    /// Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated defensive player:
    /// <c>max(1, Steals × perimeterMult(defPostness, defLineupMeanPostness))</c>,
    /// normalized among the five slots. Null slots contribute 0.
    /// Throws <see cref="InvalidOperationException"/> if no defensive slot is
    /// populated — a steal with zero defenders on the floor is an unreachable,
    /// loud bug.</para>
    /// </summary>
    /// <param name="state">Current possession state. Provides the defensive side.</param>
    /// <param name="game">Live game state — provides the defensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the Postness coefficients
    /// and the Phase 34 stealer-gating parameters
    /// (<see cref="MatchupConfig.StealerPostFloor"/>,
    /// <see cref="MatchupConfig.StealerPostnessScale"/>).</param>
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

        // ── Stage 1: compute postness for each populated defensive player ─────────
        // Reuses the same Matchup.Postness the rebound battle and committer pickers
        // use — same coefficients, same lineup-mean baseline, applied to the
        // defensive lineup.
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
                "StealerPicker: no defensive players populated — " +
                "a steal with zero defenders on the floor is an unreachable bug.");

        var meanPostness = 0.0;
        for (var i = 0; i < 5; i++)
            if (populated[i]) meanPostness += postnesses[i];
        meanPostness /= playerCount;

        // ── Stage 2: compute per-player pick weights ──────────────────────────────
        // weight = max(1, Steals × perimeterMult(defPostness, defLineupMean))
        // perimeterMult: identical formula to TurnoverCommitterPicker (guards favored).
        //   raw  = tanh((defPostness[i] − defLineupMean) / StealerPostnessScale)  // −1..+1
        //   mult = StealerPostFloor + (1 − PostFloor) × (1 − (raw + 1) / 2)
        // A guard (raw < 0) → 1 − (raw+1)/2 > 0.5 → mult high (guards favored).
        // A post (raw > 0) → 1 − (raw+1)/2 < 0.5 → mult near PostFloor (posts suppressed).
        // The floor of 1 ensures every populated slot has a positive draw probability
        // even for a Steals=0 player or a maximally-suppressed post.
        var weights     = new double[5];
        var totalWeight = 0.0;

        var postFloor     = matchupCfg.StealerPostFloor;
        var postnessScale = matchupCfg.StealerPostnessScale;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;

            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot)!;   // non-null: populated[i] is true

            var raw  = Math.Tanh((postnesses[i] - meanPostness) / postnessScale);
            var mult = postFloor + (1.0 - postFloor) * (1.0 - (raw + 1.0) / 2.0);

            // Phase 45: per-player Hustle tilt (tanh). A higher-Hustle defender earns a
            // larger share of his team's steals; centered at 1.0 for a 50-Hustle player.
            var hm   = 1.0 + matchupCfg.HustleStealerSteepness
                           * Math.Tanh((p.Hustle - 50.0) / matchupCfg.HustleStealerScale);

            weights[i]   = Math.Max(1.0, p.Steals * mult * hm);
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
