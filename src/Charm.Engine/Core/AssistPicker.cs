namespace Charm.Engine;

/// <summary>
/// Picks WHICH offensive player earned an assist — stamped on-walk at every eligible
/// made field goal (Phase 39), using the same conditional-credit philosophy as
/// <see cref="StealerPicker"/>, <see cref="BlockerPicker"/>, and
/// <see cref="DefensiveRebounderPicker"/>.
///
/// <para><b>Draw, not trace.</b> An assist is a probabilistic credit on a made bucket,
/// not a tracked pass. The same philosophy as STL/BLK/DRB: given a trigger event, who
/// gets credit?</para>
///
/// <para><b>Two-stage roll.</b> The caller first draws an assisted/not RNG roll using
/// <see cref="LineupPassingFactor"/> and the zone base rate. Only when assisted does it
/// call <see cref="Pick"/> for a second draw. This picker is never called on unassisted
/// makes — zero cost on the majority of possessions.</para>
///
/// <para><b>Zone × lineup factor.</b> The base rate comes from the shot zone
/// (<see cref="MatchupConfig.AssistedRate(ShotLocation)"/>), scaled by the offensive
/// lineup's collective passing quality (<see cref="LineupPassingFactor"/>). This ensures
/// team assist rate responds to passing — a Minnesota-style (71%) vs W-Illinois-style
/// (37%) separation. Without the factor, passing would only decide *who* gets credited,
/// not the team rate.</para>
///
/// <para><b>Shooter excluded.</b> The shooter cannot assist their own bucket.
/// AssistWeight is forced to zero for the slot matching
/// <c>state.SelectedSlot</c> (offense-side shooter attribution). Every other
/// populated offensive player is eligible; the floor of 1 ensures non-zero
/// probability for low-attribute players.</para>
///
/// <para><b>Putbacks excluded by the caller.</b> A putback means no pass after the
/// offensive rebound, so an assist is impossible by definition. The caller guards with
/// <c>!c.Putback</c> before calling <see cref="LineupPassingFactor"/> or
/// <see cref="Pick"/>. This picker does not need to re-check.</para>
///
/// <para><b>AssistWeight coefficients sum to 1.0.</b> Unlike
/// <see cref="Matchup.BlockerWeight"/> and the rebound positional weights — which
/// intentionally do <em>not</em> sum to one because the picker normalizes among
/// players, making absolute scale irrelevant — the assist coefficients must sum to 1.0.
/// That keeps <c>AssistWeight</c> on the 0–100 attribute scale, which is what makes
/// <see cref="MatchupConfig.AssistPassMidpoint"/> = 50 the league-average reference
/// for <see cref="LineupPassingFactor"/>. The sum-to-one invariant is correct here and
/// is <em>not</em> an inconsistency to "fix" against the block/rebound convention.</para>
///
/// <para><b>RNG stream.</b> <see cref="LineupPassingFactor"/> is deterministic (no RNG).
/// The caller consumes one <see cref="IRng.NextUnitInterval"/> draw for the assisted/not
/// roll. <see cref="Pick"/> consumes exactly one additional draw only when the make is
/// assisted. Every downstream engine draw on possessions containing an eligible made FG
/// therefore shifts (same pattern as Phases 31, 33–36). Corpus hash changes;
/// same-seed reproducibility within Phase 39 holds.</para>
/// </summary>
public static class AssistPicker
{
    /// <summary>
    /// Computes the lineup passing factor — a deterministic multiplier on the per-zone
    /// base assisted rate. No RNG consumed.
    ///
    /// <para>Formula (§3b):
    /// <c>1.0 + AssistPassSwing × tanh((meanAssistWeight − AssistPassMidpoint) / AssistPassScale)</c>,
    /// where <c>meanAssistWeight</c> is the mean of <see cref="AssistWeight"/> over the
    /// <b>five populated offensive players</b> (the shooter is included here — this is a
    /// team property; shooter exclusion applies only in <see cref="Pick"/>).</para>
    ///
    /// <para>Range: (1 − AssistPassSwing, 1 + AssistPassSwing) = (0.75, 1.25) with
    /// the §3 defaults. A league-average lineup ≈ 1.0.</para>
    /// </summary>
    public static double LineupPassingFactor(PossessionState state, GameState game, MatchupConfig cfg)
    {
        var offense  = state.Offense;
        var lineup   = game.LineupFor(offense);
        var roster   = game.RosterFor(offense);

        var weightSum = 0.0;
        var count     = 0;

        for (var i = 0; i < 5; i++)
        {
            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot);
            if (p is null) continue;
            weightSum += AssistWeight(p, cfg);
            count++;
        }

        var mean = count > 0 ? weightSum / count : cfg.AssistPassMidpoint;
        return 1.0 + cfg.AssistPassSwing
                   * Math.Tanh((mean - cfg.AssistPassMidpoint) / cfg.AssistPassScale);
    }

    /// <summary>
    /// Picks and returns the offensive <see cref="Slot"/> that earned the assist.
    /// Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated offensive player:
    /// <c>max(1, AssistWeight(p, cfg))</c> for all slots except the shooter;
    /// the shooter's slot gets weight 0 (excluded). Throws
    /// <see cref="InvalidOperationException"/> if no eligible non-shooter offensive
    /// player is populated — an assist with zero eligible teammates is an unreachable,
    /// loud bug.</para>
    /// </summary>
    /// <param name="state">Current possession state.
    /// <c>SelectedSlot</c> identifies the shooter (excluded from the pick).</param>
    /// <param name="game">Live game state — provides the offensive lineup and roster.</param>
    /// <param name="cfg">Matchup configuration — supplies the assist attribute weights.</param>
    /// <param name="rng">RNG source. Consumes exactly one NextUnitInterval draw.</param>
    public static Slot Pick(
        PossessionState state,
        GameState       game,
        MatchupConfig   cfg,
        IRng            rng)
    {
        var offense  = state.Offense;
        var lineup   = game.LineupFor(offense);
        var roster   = game.RosterFor(offense);

        var weights     = new double[5];
        var populated   = new bool[5];
        var totalWeight = 0.0;
        var playerCount = 0;

        for (var i = 0; i < 5; i++)
        {
            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot);
            if (p is null) continue;

            populated[i] = true;
            playerCount++;

            // Shooter cannot assist their own make.
            var isShooter = state.SelectedSlot is { } sel
                            && sel.Side   == offense
                            && sel.Number == slot.Number;
            if (isShooter)
            {
                weights[i] = 0.0;
                continue;
            }

            weights[i]   = Math.Max(1.0, AssistWeight(p, cfg));
            totalWeight += weights[i];
        }

        if (totalWeight <= 0.0)
            throw new InvalidOperationException(
                "AssistPicker: no eligible non-shooter offensive players — " +
                "an assist credit with zero eligible teammates is an unreachable bug.");

        // One RNG draw — cumulative walk to chosen slot.
        // Same shape as BlockerPicker and DefensiveRebounderPicker: walk the cumulative
        // sum, return the first slot whose cumulative weight exceeds the draw. The final
        // eligible slot is the implicit fallback (absorbs floating-point shortfall).
        var draw          = rng.NextUnitInterval() * totalWeight;
        var cumulative    = 0.0;
        var lastEligible  = -1;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i] || weights[i] <= 0.0) continue;
            lastEligible = i;
            cumulative  += weights[i];
            if (draw <= cumulative)
                return lineup.SlotAt(i + 1);
        }

        // Fallback: floating-point edge — return the last eligible slot.
        return lineup.SlotAt(lastEligible + 1);
    }

    /// <summary>
    /// Per-player assist weight: a weighted sum of <see cref="Player.Passing"/>,
    /// <see cref="Player.Playmaking"/>, and <see cref="Player.BasketballIQ"/> using
    /// coefficients from <paramref name="cfg"/>. Coefficients sum to 1.0, keeping
    /// the result on the 0–100 attribute scale.
    /// </summary>
    private static double AssistWeight(Player p, MatchupConfig cfg)
        => cfg.AssistPassingWeight    * p.Passing
         + cfg.AssistPlaymakingWeight * p.Playmaking
         + cfg.AssistIqWeight         * p.BasketballIQ;
}
