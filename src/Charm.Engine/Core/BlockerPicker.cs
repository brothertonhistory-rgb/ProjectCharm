namespace Charm.Engine;

/// <summary>
/// Picks WHICH defensive player earned a block — stamped on-walk at every
/// <c>ShotResult.Blocked</c> exit (Phase 36), retiring the last harness
/// <c>WeightedDraw</c> (BLK).
///
/// <para><b>Zone-aware weight formula.</b> Each defensive player's pick weight is
/// <c>max(1, Matchup.BlockerWeight(zone, player, cfg))</c>, where
/// <see cref="Matchup.BlockerWeight"/> is a straight weighted sum of six blocking
/// attributes (RimProtection, PerimeterDefense, PostDefense, Height, Wingspan,
/// Vertical) with per-zone coefficients from <see cref="MatchupConfig"/>.
/// A block on a Rim or Short shot favors bigs (RimProtection, Height lead); a block
/// on a Three favors perimeter defenders (PerimeterDefense leads, Wingspan still
/// meaningful throughout). The floor of 1 ensures every populated defensive slot has
/// a nonzero draw probability.</para>
///
/// <para><b>All five defenders eligible on every block.</b> BlockerPicker is a pure
/// all-five-defenders weighted draw — it does not slot-match or reference
/// <c>SelectedSlot</c> (which is offense-side shooter attribution). On a putback,
/// Roll K's PutBack arm stamps <c>ShotType = ShotLocation.Rim</c> before re-entering
/// Roll H, so <c>ShotType</c> is always Rim at a blocked putback. A null
/// <c>ShotType</c> guard is included as a defensive fallback (also resolves to Rim),
/// matching the putback zone.</para>
///
/// <para><b>BLK can fire multiple times per possession.</b>
/// Unlike <see cref="StealerPicker"/> and <see cref="DefensiveRebounderPicker"/>
/// (at most once each), a possession can contain multiple blocked attempts
/// (putback is blocked, and so on). <c>BlkBySlot</c> is therefore a
/// <see cref="SlotGroup"/>, not <c>int?</c>, mirroring <c>OrbBySlot</c>. Each block
/// fires one picker call, accumulating into <c>blkBySlot</c> with
/// <c>WithSlot(slot, 1)</c>.</para>
///
/// <para><b>RNG stream.</b> Consumes exactly one <see cref="IRng.NextUnitInterval"/>
/// draw per block. Called inside the <c>case ContinuationKind.IntoShotResolution</c>
/// stamp block immediately after <c>ShotResult.Blocked</c> is confirmed. Documented
/// stream shift: every downstream engine draw on possessions containing a block
/// shifts (same consequence as Phases 31, 33, 34, and 35). Corpus hash changes;
/// same-seed reproducibility within Phase 36 holds.</para>
/// </summary>
public static class BlockerPicker
{
    /// <summary>
    /// Picks and returns the defensive <see cref="Slot"/> that earned the block.
    /// Consumes exactly one <paramref name="rng"/> draw.
    ///
    /// <para>Weight per populated defensive player:
    /// <c>max(1, Matchup.BlockerWeight(zone, player, matchupCfg))</c>,
    /// where <c>zone</c> is <c>state.ShotType ?? ShotLocation.Rim</c>. Null slots
    /// contribute 0. Throws <see cref="InvalidOperationException"/> if no defensive
    /// slot is populated — a block with zero defenders on the floor is an unreachable,
    /// loud bug.</para>
    /// </summary>
    /// <param name="state">Current possession state. Provides the defensive side and
    /// <c>ShotType</c> for zone-aware blending. Null <c>ShotType</c> falls back to
    /// <c>ShotLocation.Rim</c> (correct for putbacks, where Roll K forces Rim).</param>
    /// <param name="game">Live game state — provides the defensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the 30 per-zone blocker
    /// attribute weights (<c>BlkRimProtection</c>, <c>BlkPerimeterDefense</c>, etc.).</param>
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

        // null ShotType → Rim fallback. Roll K's PutBack arm stamps ShotLocation.Rim
        // before re-entering Roll H, so this guard fires only on defensive code paths
        // not yet imagined. Rim is the correct fallback: putbacks are forced Rim.
        var zone = state.ShotType ?? ShotLocation.Rim;

        // ── Stage 1: collect populated defensive players ──────────────────────
        // BlockerPicker reads ShotType (zone) and six blocking attributes per player.
        // It never reads SelectedSlot — that is offense-side shooter attribution.
        var populated   = new bool[5];
        var players     = new Player?[5];
        var playerCount = 0;

        for (var i = 0; i < 5; i++)
        {
            var slot = lineup.SlotAt(i + 1);
            var p    = roster.PlayerAt(slot);
            if (p is null) continue;
            players[i]   = p;
            populated[i] = true;
            playerCount++;
        }

        if (playerCount == 0)
            throw new InvalidOperationException(
                "BlockerPicker: no defensive players populated — " +
                "a block with zero defenders on the floor is an unreachable bug.");

        // ── Stage 2: compute per-player pick weights ──────────────────────────
        // weight = max(1, BlockerWeight(zone, player, matchupCfg))
        // The floor of 1 ensures every populated slot has a nonzero draw probability
        // even for a player with all blocking attributes near zero.
        var weights     = new double[5];
        var totalWeight = 0.0;

        for (var i = 0; i < 5; i++)
        {
            if (!populated[i]) continue;
            weights[i]   = Math.Max(1.0, Matchup.BlockerWeight(zone, players[i]!, matchupCfg));
            totalWeight += weights[i];
        }

        // ── Stage 3: one RNG draw — cumulative walk to chosen slot ───────────
        // Same shape as DefensiveRebounderPicker and StealerPicker: walk the
        // cumulative sum, return the first slot whose cumulative weight exceeds
        // the draw. The final populated slot is the implicit fallback (absorbs
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
