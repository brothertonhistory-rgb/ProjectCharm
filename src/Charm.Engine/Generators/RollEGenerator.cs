namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll E generator (Phase 15). Replaces the flat 20%
/// halfcourt pie with a usage-weighted selection pie: a better scorer/creator
/// naturally commands a larger share of shot attempts; a Rodman-type falls to
/// a floor; five comparable players stay near-even.
///
/// <para><b>FastBreak passthrough — unchanged.</b> When
/// <see cref="PossessionState.FastBreak"/> is true, the generator returns the
/// existing transition pie (cfg.TransitionSlot1..5) byte-for-byte — exactly
/// what <see cref="RollEStubPieGenerator"/> does today. Only the halfcourt
/// branch is attribute-driven.</para>
///
/// <para><b>Usage score formula.</b>
/// score = 0.35 * SelfCreation
///       + 0.30 * (Close + PostMoves) / 2
///       + 0.35 * (Outside + Mid + Finishing) / 3
/// All inputs are 0–99 authored attributes. The (Close + PostMoves) / 2 term
/// gives a high-post-moves, high-close center meaningful usage even when his
/// SelfCreation and perimeter numbers are modest — Emmett's basketball call.
/// Passing, Playmaking, BallHandling, and BasketballIQ are intentionally
/// excluded (Roll E is who TAKES the shot, not who RUNS the offense).</para>
///
/// <para><b>Tilt: sharpening exponent.</b> Raw scores are raised to
/// cfg.UsageExponent before normalizing. At exponent = 1 the distribution
/// is proportional to raw scores (mild tilt); higher exponents sharpen the
/// gap between players. At the calibrated default a realistic D1 alpha lands
/// ~35% and a Rodman-type is held by the floor.</para>
///
/// <para><b>Floor and rail — hard constraints via constrained redistribution.</b>
/// The floor (cfg.UsageFloor) is a guaranteed minimum for every populated slot;
/// the rail (cfg.UsageRail) is a hard cap on any single slot. Both are enforced
/// by iterative constrained redistribution (water-filling), not naive double-
/// renormalization — naive renorm can push a low slot back under the floor after
/// the rail clamps a high one. See <c>ApplyFloorAndRail</c>.</para>
///
/// <para><b>Null-slot handling.</b> Null / unpopulated slots receive 0.0 and can
/// never be selected. Floor and rail apply only to populated slots. A real game
/// always has all five offense slots populated; the partial and empty paths are
/// test-only guards.</para>
///
/// <para><b>Rail feasibility standdown.</b> When
/// populatedCount * cfg.UsageRail &lt; 1.0 (only possible with a thin test
/// roster — never in a real five-man game), the rail is skipped entirely for
/// that call.</para>
///
/// Implements <see cref="IRollEPieGenerator"/>.
/// </summary>
public sealed class RollEGenerator : IRollEGenerationProvider
{
    private readonly RollEConfig _cfg;
    private readonly GameState   _game;

    public RollEGenerator(RollEConfig cfg, GameState game)
    {
        _cfg  = cfg  ?? throw new ArgumentNullException(nameof(cfg));
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    /// <summary>
    /// Generate the selection pie AND per-slot volume pressures in one pass.
    /// Pressure[i] = max(0, finalShares[i] − 1.0/populatedCount). Zero for
    /// null/FastBreak slots. This is the method the resolver calls; the base
    /// interface's <see cref="Generate"/> delegates here.
    /// </summary>
    public RollEGeneration GenerateWithPressure(PossessionState state)
    {
        // FastBreak passthrough — all pressures zero (no volume load on a transition).
        if (state.FastBreak)
        {
            var transWeights = new Dictionary<SelectionOutcome, double>
            {
                [SelectionOutcome.Slot1] = _cfg.TransitionSlot1,
                [SelectionOutcome.Slot2] = _cfg.TransitionSlot2,
                [SelectionOutcome.Slot3] = _cfg.TransitionSlot3,
                [SelectionOutcome.Slot4] = _cfg.TransitionSlot4,
                [SelectionOutcome.Slot5] = _cfg.TransitionSlot5,
            };
            var transPie = new Pie<SelectionOutcome>(transWeights, _cfg.Epsilon);
            // Extract shares from the transition pie for the FinalShares array
            var outcomes    = Enum.GetValues<SelectionOutcome>();
            var transShares = new double[5];
            for (var i = 0; i < 5; i++)
                transShares[i] = transWeights[outcomes[i]];
            return new RollEGeneration(transPie, transShares, new double[5]);
        }

        // Read five offense players
        var offRoster = _game.RosterFor(state.Offense);
        var offLineup = _game.LineupFor(state.Offense);

        var players = new Player?[]
        {
            offRoster.PlayerAt(offLineup.SlotAt(1)),
            offRoster.PlayerAt(offLineup.SlotAt(2)),
            offRoster.PlayerAt(offLineup.SlotAt(3)),
            offRoster.PlayerAt(offLineup.SlotAt(4)),
            offRoster.PlayerAt(offLineup.SlotAt(5)),
        };

        // Count populated slots
        var populated = 0;
        foreach (var p in players) if (p is not null) populated++;

        if (populated == 0)
        {
            var baseWeights = new Dictionary<SelectionOutcome, double>
            {
                [SelectionOutcome.Slot1] = _cfg.BaseSlot1,
                [SelectionOutcome.Slot2] = _cfg.BaseSlot2,
                [SelectionOutcome.Slot3] = _cfg.BaseSlot3,
                [SelectionOutcome.Slot4] = _cfg.BaseSlot4,
                [SelectionOutcome.Slot5] = _cfg.BaseSlot5,
            };
            var basePie      = new Pie<SelectionOutcome>(baseWeights, _cfg.Epsilon);
            var outcomes2    = Enum.GetValues<SelectionOutcome>();
            var baseShares   = new double[5];
            for (var i = 0; i < 5; i++)
                baseShares[i] = baseWeights[outcomes2[i]];
            return new RollEGeneration(basePie, baseShares, new double[5]);
        }

        // Compute raw usage scores
        var rawScores = new double[5];
        for (var i = 0; i < 5; i++)
        {
            if (players[i] is Player p)
            {
                var score = p.SelfCreation * 0.35
                          + (p.Close + p.PostMoves) / 2.0 * 0.30
                          + (p.Outside + p.Mid + p.Finishing) / 3.0 * 0.35;
                rawScores[i] = Math.Max(score, _cfg.MinUsageScore);
            }
        }

        // ── Hierarchy blend (Phase 29 Session 1) ─────────────────────────────
        // Derive the hierarchy exponent from the offensive team's coach bias.
        // Bias 1.0 → exponent 0 (egalitarian: all weights = 1.0, attributes only).
        // Bias 5.0 → exponent HierarchyExponentNeutral (standard expression).
        // Bias 10.0 → exponent HierarchyExponentMax (full heliocentric).
        // Piecewise-linear interpolation; monotone and continuous through bias = 5.
        var coach = _game.CoachFor(state.Offense);
        var bias  = coach.HeliocentricBias;
        var hierarchyExponent = bias <= 5.0
            ? _cfg.HierarchyExponentNeutral * (bias - 1.0) / 4.0
            : _cfg.HierarchyExponentNeutral
              + (_cfg.HierarchyExponentMax - _cfg.HierarchyExponentNeutral)
              * (bias - 5.0) / 5.0;

        // Multiply each populated raw score by (HierarchyRank / 5.0)^hierarchyExponent.
        // At rank 5: weight = 1.0 for any exponent → regression anchor.
        // At exponent 0 (bias 1.0): weight = 1.0 for all ranks → attributes only.
        // A rank-1 player's post-MinUsageScore score may be pushed downward here —
        // this is intentional; the floor/rail machinery is the participation
        // protection. Do NOT reapply MinUsageScore after this multiply.
        for (var i = 0; i < 5; i++)
        {
            if (rawScores[i] > 0.0 && players[i] is Player ph)
            {
                if (ph.HierarchyRank < 1 || ph.HierarchyRank > 10)
                    throw new InvalidOperationException(
                        $"Player '{ph.Name}' has HierarchyRank {ph.HierarchyRank} " +
                        "outside [1, 10]. Check authored player data.");
                var weight  = Math.Pow(ph.HierarchyRank / 5.0, hierarchyExponent);
                rawScores[i] *= weight;
            }
        }
        // ─────────────────────────────────────────────────────────────────────

        // Apply sharpening exponent
        var expScores = new double[5];
        var expTotal  = 0.0;
        for (var i = 0; i < 5; i++)
        {
            if (rawScores[i] > 0.0)
            {
                expScores[i] = Math.Pow(rawScores[i], _cfg.UsageExponent);
                expTotal    += expScores[i];
            }
        }

        // Normalize to initial shares
        var shares = new double[5];
        for (var i = 0; i < 5; i++)
            shares[i] = expTotal > 0.0 ? expScores[i] / expTotal : 0.0;

        // Apply floor + rail (constrained redistribution)
        shares = ApplyFloorAndRail(shares, expScores, populated);

        // ── Compute volume pressures (one pass, same shares array) ────────────
        // pressure[i] = max(0, finalShare[i] − equalShare)
        // equalShare  = 1.0 / populated. Null/empty slots stay 0.
        var comfortShare = 1.0 / populated;
        var pressures    = new double[5];
        for (var i = 0; i < 5; i++)
            pressures[i] = players[i] is not null ? Math.Max(0.0, shares[i] - comfortShare) : 0.0;

        // Build the pie
        var allOutcomes = Enum.GetValues<SelectionOutcome>();
        var weights     = new Dictionary<SelectionOutcome, double>();
        for (var i = 0; i < 5; i++)
            weights[allOutcomes[i]] = shares[i];

        var pie = new Pie<SelectionOutcome>(weights, _cfg.Epsilon);
        return new RollEGeneration(pie, shares, pressures);
    }

    /// <inheritdoc cref="IRollEPieGenerator.Generate"/>
    public Pie<SelectionOutcome> Generate(PossessionState state) =>
        GenerateWithPressure(state).Pie;

    /// <summary>
    /// Phase 27 Session 2 — selection tilt. Bends the usage pie by the gap between
    /// usage intent (<paramref name="gen"/>.FinalShares) and defensive attention
    /// (<paramref name="attentionShares"/>), then re-enforces the floor/rail constraint
    /// using the TILTED weights as the redistribution basis (not the original expScores —
    /// that would partially undo the tilt).
    ///
    /// <para>Tilt math: <c>multiplier[i] = exp(log(MaxTiltMultiplier) × tanh(gap[i] /
    /// TiltReferenceShift))</c> where <c>gap[i] = FinalShares[i] − attentionShares[i]</c>.
    /// Strictly bounded in (1/MaxTiltMultiplier, MaxTiltMultiplier); exactly 1.0 at zero
    /// gap (neutral anchor: usage == attention reproduces the pre-tilt pie).</para>
    ///
    /// <para>Phase 44 — selection compression. After tilt + floor/rail, a second pass
    /// compresses the tilt toward above-equal-share offensive focal points using defensive
    /// OffBallDefense (perimeter focal points) and HelpDefense (interior focal points).
    /// Freed mass redistributes to below-equal-share slots. Floor/rail re-applied after
    /// compression. Full contract: tilt → floor/rail → compression → redistribute →
    /// normalize → floor/rail.</para>
    ///
    /// <para>Halfcourt-only: caller must NOT call this on the FastBreak branch. The
    /// FastBreak branch passes <paramref name="gen"/>.Pie directly into RollE.Execute
    /// untilted.</para>
    ///
    /// <para>One-pass: attention (<paramref name="attentionShares"/>) is computed from
    /// the pre-tilt FinalShares and never recomputed from the result. Pressures
    /// (<paramref name="gen"/>.Pressures) are pre-tilt and passed unchanged to
    /// RollE.Execute — the tilt changes WHICH slot is rolled, not the pressure each
    /// slot carries.</para>
    /// </summary>
    public Pie<SelectionOutcome> BendByAttention(
        RollEGeneration gen,
        double[] attentionShares,
        GameState game,
        MatchupConfig matchupCfg,
        PossessionState state)
    {
        var finalShares = gen.FinalShares;

        // Per-slot gap and bounded multiplier
        var tilted = new double[5];
        for (var i = 0; i < 5; i++)
        {
            var gap        = finalShares[i] - attentionShares[i];
            var multiplier = Math.Exp(Math.Log(_cfg.MaxTiltMultiplier)
                                    * Math.Tanh(gap / _cfg.TiltReferenceShift));
            tilted[i] = finalShares[i] * multiplier;
        }

        // Normalize
        var total = 0.0;
        foreach (var v in tilted) total += v;
        if (total > 0.0)
            for (var i = 0; i < 5; i++) tilted[i] /= total;

        // Count populated slots (same definition as GenerateWithPressure)
        var populatedCount = 0;
        for (var i = 0; i < 5; i++) if (finalShares[i] > 0.0) populatedCount++;
        if (populatedCount == 0) populatedCount = 5;

        // Re-apply floor/rail using TILTED weights as the redistribution basis —
        // NOT the original expScores. Using the original would pull mass back toward
        // pre-tilt proportions and partially undo the tilt.
        var constrained = ApplyFloorAndRail(tilted, tilted, populatedCount);

        // Phase 44 — selection compression.
        // OffBallDefense compresses tilt on perimeter focal points (low postness).
        // HelpDefense compresses tilt on interior focal points (high postness).
        // Both fully independent; freed mass redistributes to below-equal-share slots.
        var equalShare = populatedCount > 0 ? 1.0 / populatedCount : 0.2;

        // Compute defensive aggregates (all five defenders, fixed denominator 5.0).
        var defRoster = game.RosterFor(state.Defense);
        var defLineup = game.LineupFor(state.Defense);
        var offBallDefSum = 0.0;
        var helpDefSum    = 0.0;
        for (var i = 1; i <= 5; i++)
        {
            var defender = defRoster.PlayerAt(defLineup.SlotAt(i));
            if (defender is null) continue;
            offBallDefSum += defender.OffBallDefense / 100.0;
            helpDefSum    += defender.HelpDefense    / 100.0;
        }
        var offBallDefAgg = Math.Pow(offBallDefSum / 5.0, matchupCfg.OffBallDefenseCompressionExponent)
                          * matchupCfg.OffBallDefenseCompressionScale;
        var helpDefAgg    = Math.Pow(helpDefSum    / 5.0, matchupCfg.HelpDefenseCompressionExponent)
                          * matchupCfg.HelpDefenseCompressionScale;

        // Offensive lineup postness (for identifying focal point type).
        var offRoster = game.RosterFor(state.Offense);
        var offLineup = game.LineupFor(state.Offense);

        var freedMass  = 0.0;
        var compressed = (double[])constrained.Clone();
        for (var i = 0; i < 5; i++)
        {
            if (constrained[i] <= equalShare) continue;   // only compress above-equal-share slots

            var offPlayer = offRoster.PlayerAt(offLineup.SlotAt(i + 1));
            if (offPlayer is null) continue;

            var postness = Matchup.Postness(offPlayer, matchupCfg);
            // perimeterWeight: 1.0 for pure guard (postness=0), 0.0 at or above PostnessNeutral
            var perimeterWeight = Math.Max(0.0, 1.0 - postness / matchupCfg.PostnessNeutral);
            // interiorWeight: 0.0 for pure guard, approaches 1.0 for high-postness player
            var interiorWeight  = Math.Max(0.0, Math.Min(1.0, postness / matchupCfg.PostnessNeutral - 1.0));

            var compressionFraction = offBallDefAgg * perimeterWeight + helpDefAgg * interiorWeight;
            compressionFraction     = Math.Min(compressionFraction, 1.0);   // never compress more than 100%

            var excess = constrained[i] - equalShare;
            var freed  = excess * compressionFraction;
            compressed[i] -= freed;
            freedMass     += freed;
        }

        // Redistribute freed mass proportionally to below-equal-share slots.
        if (freedMass > 0.0)
        {
            var belowTotal = 0.0;
            for (var i = 0; i < 5; i++)
                if (compressed[i] < equalShare && constrained[i] > 0.0) belowTotal += equalShare - compressed[i];

            if (belowTotal > 0.0)
            {
                for (var i = 0; i < 5; i++)
                {
                    if (compressed[i] < equalShare && constrained[i] > 0.0)
                    {
                        var gap = equalShare - compressed[i];
                        compressed[i] += freedMass * (gap / belowTotal);
                    }
                }
            }
            else
            {
                // All slots at or above equal share — redistribute evenly to populated slots.
                // Defensive fallback only — unreachable under a valid normalized populated lineup.
                for (var i = 0; i < 5; i++)
                    if (constrained[i] > 0.0) compressed[i] += freedMass / populatedCount;
            }

            // Re-normalize after redistribution.
            var compTotal = 0.0;
            foreach (var v in compressed) compTotal += v;
            if (compTotal > 0.0)
                for (var i = 0; i < 5; i++) compressed[i] /= compTotal;
        }

        // Compression is a post-tilt adjustment, not an exemption from Roll E's existing
        // floor/rail contract. Re-apply ApplyFloorAndRail a second time before building
        // the final pie. Full contract: tilt → floor/rail → compression → redistribute
        // → normalize → floor/rail.
        // Use the compressed/normalized array as both candidate shares and redistribution
        // basis (same discipline as the first call above: tilted weights as basis,
        // not the original expScores).
        var finalConstrained = freedMass > 0.0
            ? ApplyFloorAndRail(compressed, compressed, populatedCount)
            : constrained;

        // Build the new pie from the constrained tilted shares
        var allOutcomes = Enum.GetValues<SelectionOutcome>();
        var weights     = new Dictionary<SelectionOutcome, double>();
        for (var i = 0; i < 5; i++)
            weights[allOutcomes[i]] = finalConstrained[i];

        return new Pie<SelectionOutcome>(weights, _cfg.Epsilon);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constrained redistribution (water-filling)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enforces floor and rail as hard constraints via iterative constrained
    /// redistribution. Null slots (rawExpScores[i] == 0) remain 0 and are
    /// excluded from all constraint logic.
    ///
    /// <para><b>Floor pass:</b> Pins every populated slot at or above
    /// cfg.UsageFloor. Excess mass is redistributed proportionally (by raw
    /// exponentiated score) among above-floor slots. Iterates until stable.</para>
    ///
    /// <para><b>Rail pass:</b> Clamps any slot above cfg.UsageRail down to the
    /// rail. Excess is redistributed proportionally among non-railed slots,
    /// respecting the floor. Iterates until stable. Rail is skipped entirely
    /// when populatedCount * rail &lt; 1.0 (mathematically infeasible for thin
    /// test rosters).</para>
    /// </summary>
    private double[] ApplyFloorAndRail(double[] shares, double[] expScores, int populatedCount)
    {
        var s = (double[])shares.Clone();
        var floor = _cfg.UsageFloor;
        var rail  = _cfg.UsageRail;

        // ── Floor: iterative constrained redistribution ──────────────────────
        for (var iter = 0; iter < 50; iter++)
        {
            // Identify slots pinned at floor
            var pinned = new bool[5];
            var pinnedCount = 0;
            for (var i = 0; i < 5; i++)
            {
                if (expScores[i] > 0.0 && s[i] <= floor)
                {
                    pinned[i] = true;
                    pinnedCount++;
                }
            }

            if (pinnedCount == 0) break;  // all above floor — stable

            var floorMass = floor * pinnedCount;
            var freeMass  = 1.0 - floorMass;
            if (freeMass <= 0.0) break;   // infeasible guard (config invariant prevents this)

            var freeExpTotal = 0.0;
            for (var i = 0; i < 5; i++)
                if (expScores[i] > 0.0 && !pinned[i]) freeExpTotal += expScores[i];

            if (freeExpTotal <= 0.0) break;

            var changed = false;
            for (var i = 0; i < 5; i++)
            {
                double newVal;
                if (expScores[i] <= 0.0)
                    newVal = 0.0;  // null slot: stay 0
                else if (pinned[i])
                    newVal = floor;
                else
                    newVal = freeMass * (expScores[i] / freeExpTotal);

                if (Math.Abs(newVal - s[i]) > 1e-12) changed = true;
                s[i] = newVal;
            }
            if (!changed) break;
        }

        // ── Rail: water-filling (skip if infeasible for thin test rosters) ───
        var useRail = (populatedCount * rail >= 1.0);
        if (!useRail) return s;

        for (var iter = 0; iter < 20; iter++)
        {
            var anyOver = false;
            var excess  = 0.0;
            var railed  = new bool[5];
            for (var i = 0; i < 5; i++)
            {
                if (s[i] > rail)
                {
                    excess   += s[i] - rail;
                    s[i]      = rail;
                    railed[i] = true;
                    anyOver   = true;
                }
            }
            if (!anyOver) break;

            // Distribute excess to non-railed populated slots proportionally,
            // clamping to floor.
            var freeExpTotal = 0.0;
            for (var i = 0; i < 5; i++)
                if (expScores[i] > 0.0 && !railed[i]) freeExpTotal += expScores[i];

            for (var i = 0; i < 5; i++)
            {
                if (expScores[i] <= 0.0 || railed[i]) continue;
                s[i] += freeExpTotal > 0.0
                    ? excess * (expScores[i] / freeExpTotal)
                    : excess / (populatedCount - railed.Count(r => r));
                s[i] = Math.Max(s[i], floor);
            }

            // Renormalize to restore exact sum = 1
            var total = 0.0;
            foreach (var v in s) total += v;
            if (total > 0.0)
                for (var i = 0; i < 5; i++) s[i] /= total;
        }

        return s;
    }
}
