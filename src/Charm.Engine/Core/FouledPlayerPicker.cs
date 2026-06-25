namespace Charm.Engine;

/// <summary>
/// Picks WHICH offensive player drew a non-shooting foul on a pre-Roll-E bonus
/// trip — the "who drew the foul" question the engine could not previously answer
/// (Phase 51). When a team is in the bonus and a player is fouled before the
/// offense has run a play, no shooter has been selected
/// (<see cref="PossessionState.SelectedSlot"/> is null), so the engine knew
/// someone went to the line but not who. This picker names him, so the trip is
/// shot at his real <see cref="Player.FreeThrow"/> rating and credited to him in
/// the box.
///
/// <para><b>It is a "who," not a "how many."</b> The bonus rules already decide
/// 1-and-1 vs. two vs. double bonus; this draw does not touch the shot count. It
/// is offense-side only — which <i>defender</i> committed the foul, and
/// per-defender foul trouble, are out of scope.</para>
///
/// <para><b>Weight formula — rating-driven, not position-hardcoded.</b> Each
/// populated offensive player's pick weight blends three authored channels, each
/// normalized to [0,1] first so the three config weights share one interpretable
/// unit:
/// <list type="bullet">
///   <item><b>FoulDrawing</b> (<c>FoulDrawing / 99</c>) — the contact channel:
///         drivers who attack and posts who bang for position rate high, perimeter
///         parkers low. The same attribute drives the Roll H shooting-foul
///         <i>rate</i> (how often a foul is drawn); here it answers a different
///         question — <i>which</i> of the five drew a non-shooting foul, given one
///         happened. No double-count.</item>
///   <item><b>Planned usage</b> (<c>(HierarchyRank − 1) / 9</c>) — the authored
///         shot-priority knob (1–10; higher = more featured). Anyone more featured
///         draws more.</item>
///   <item><b>BallHandling</b> (<c>BallHandling / 99</c>) — the
///         early-reach-in-on-the-handler aspect; spreads the share among the
///         guards.</item>
/// </list>
/// <c>weight = max(floor, w_fd·n(FoulDrawing) + w_use·n(usage) + w_bh·n(BallHandling))</c>,
/// normalized across the populated slots. The floor (strictly &gt; 0, enforced in
/// <see cref="MatchupConfig"/> Load) keeps every populated player nonzero — the
/// parked perimeter shooter can still get grabbed, rare but never zero. The lead
/// guard tops the list emergently (highest BallHandling <i>and</i> usage), not by
/// rule; a genuinely featured high-usage big also draws heavily. The coefficients
/// are calibration placeholders; the shape is what matters.</para>
///
/// <para><b>RNG stream.</b> Consumes exactly one
/// <see cref="IRng.NextUnitInterval"/> draw, once per qualifying bonus trip, before
/// the FT spins — mirroring the rest of the picker family
/// (<see cref="StealerPicker"/>, <see cref="OffensiveRebounderPicker"/>, etc.).
/// Documented stream shift: every downstream engine draw on those trips shifts
/// (same precedent as Phase 31/33/34/45). The corpus hash changes; same-seed
/// reproducibility holds.</para>
/// </summary>
public static class FouledPlayerPicker
{
    /// <summary>
    /// Picks and returns the offensive <see cref="Slot"/> that drew the foul.
    /// Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated offensive player:
    /// <c>max(floor, w_fd·n(FoulDrawing) + w_use·n(usage) + w_bh·n(BallHandling))</c>,
    /// normalized among the five slots. Null slots contribute 0. Throws
    /// <see cref="InvalidOperationException"/> if no offensive slot is populated —
    /// the caller gates on "≥1 populated offensive slot," so reaching here with an
    /// empty offense is an unreachable, loud bug.</para>
    /// </summary>
    /// <param name="state">Current possession state. Provides the offensive side.</param>
    /// <param name="game">Live game state — provides the offensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the foul-draw channel
    /// weights and the floor
    /// (<see cref="MatchupConfig.FouledPlayerPickFoulDrawingWeight"/>,
    /// <see cref="MatchupConfig.FouledPlayerPickUsageWeight"/>,
    /// <see cref="MatchupConfig.FouledPlayerPickBallHandlingWeight"/>,
    /// <see cref="MatchupConfig.FouledPlayerPickFloor"/>).</param>
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

        var wFoulDrawing  = matchupCfg.FouledPlayerPickFoulDrawingWeight;
        var wUsage        = matchupCfg.FouledPlayerPickUsageWeight;
        var wBallHandling = matchupCfg.FouledPlayerPickBallHandlingWeight;
        var floor         = matchupCfg.FouledPlayerPickFloor;

        // ── Per-populated-player pick weight ────────────────────────────────────
        // Each channel normalized to [0,1] first (FoulDrawing/99, usage from
        // HierarchyRank, BallHandling/99), then the weighted additive blend, then
        // the floor. The floor (> 0) guarantees every populated slot is eligible.
        var weights       = new double[5];
        var populated     = new bool[5];
        var totalWeight   = 0.0;
        var lastPopulated = -1;

        for (var i = 0; i < 5; i++)
        {
            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot);
            if (p is null) continue;

            var nFoulDrawing  = p.FoulDrawing  / 99.0;          // contact channel
            var nUsage        = (p.HierarchyRank - 1) / 9.0;    // planned-usage channel (1–10 → 0..1)
            var nBallHandling = p.BallHandling / 99.0;          // handler channel

            var blend = wFoulDrawing  * nFoulDrawing
                      + wUsage        * nUsage
                      + wBallHandling * nBallHandling;

            weights[i]    = Math.Max(floor, blend);
            populated[i]  = true;
            lastPopulated = i;
            totalWeight  += weights[i];
        }

        if (lastPopulated < 0)
            throw new InvalidOperationException(
                "FouledPlayerPicker: no offensive players populated — the caller " +
                "gates on ≥1 populated offensive slot, so this is an unreachable bug.");

        // Degenerate-config guard: with a strictly-positive floor and ≥1 populated
        // slot the sum is always positive; a non-positive sum means the floor guard
        // was bypassed — fail loud rather than mis-draw.
        if (totalWeight <= 0.0)
            throw new InvalidOperationException(
                $"FouledPlayerPicker: summed pick weight over populated slots was " +
                $"{totalWeight} (must be > 0). The foul-draw floor must be strictly " +
                "positive (enforced in MatchupConfig.Load).");

        // ── One RNG draw — cumulative walk to the chosen slot ────────────────────
        // Same shape as the rest of the picker family: walk the cumulative sum and
        // return the first slot whose cumulative weight exceeds the draw. The final
        // populated slot is the implicit fallback (absorbs floating-point shortfall).
        var draw       = rng.NextUnitInterval() * totalWeight;
        var cumulative = 0.0;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;
            cumulative += weights[i];
            if (draw <= cumulative)
                return lineup.SlotAt(i + 1);
        }

        // Fallback: floating-point edge — return the last populated slot.
        return lineup.SlotAt(lastPopulated + 1);
    }
}
