namespace Charm.Engine;

/// <summary>
/// Picks WHICH defensive player earned a block — stamped on-walk at every
/// <c>ShotResult.Blocked</c> exit, retiring the last harness <c>WeightedDraw</c> (BLK)
/// in Phase 36 and rebuilt on contribution in Session 79.
///
/// <para><b>Session 79 — credit follows the threat that produced the block.</b> Phase 36
/// weighted each defender by <c>max(1, Matchup.BlockerWeight(...))</c>, a straight sum of six
/// blocking attributes. Averaging six broadly-correlated ratings compressed the whole
/// population into a p99/median spread of 1.48x, so the best rim protector in the country took
/// 30% of his lineup's blocks against each guard's 17% — and because this picker NORMALIZES
/// the weights into shares, no re-tune of those coefficients could ever have changed that.
/// Weights now come from <see cref="Matchup.BlockCreditWeights"/> (located shots) or
/// <see cref="Matchup.PutbackBlockCreditWeights"/> (putbacks): a per-defender luck floor plus
/// his own positive blocking contribution.</para>
///
/// <para><b>Two formulas, one call site.</b> A putback's block rate has always been a
/// five-defender stack with no matched man (RollHGenerator's putback door), so its credit is
/// the per-defender shifts that stack already computes — no duel arm, no zone share, and no
/// need to resolve the rebounder. A located shot has a matched defender, who is credited off
/// his own tools while the other four are scaled by the zone's help share. The Resolver passes
/// <paramref name="putback"/> so this method knows which produced the block; both still stamp
/// at the same <c>ShotResult.Blocked</c> exit.</para>
///
/// <para><b>The shooter is deliberately absent.</b> He decides WHETHER a shot is blocked (he
/// is priced into the RATE through the duel arm); he does not decide WHICH defender got there.
/// Scoring the matched man against this particular shooter was measured to be exactly zero 44%
/// of the time, which pins the help arm at 100% of the credit in those cases.</para>
///
/// <para><b>Every populated slot stays drawable.</b> The luck floor inside the weight
/// functions replaces the old <c>max(1, ...)</c> and guarantees a strictly positive weight for
/// every populated defender at every zone, so the total is never zero and there is no fallback
/// branch to take.</para>
///
/// <para><b>BLK can fire multiple times per possession</b> (a putback is blocked, and so on),
/// so <c>BlkBySlot</c> is a <see cref="SlotGroup"/>, not <c>int?</c>, mirroring <c>OrbBySlot</c>.
/// Each block fires one picker call.</para>
///
/// <para><b>RNG stream.</b> Consumes exactly one <see cref="IRng.NextUnitInterval"/> draw per
/// block — unchanged by Session 79. Same-seed reproducibility holds within a fixed block count;
/// because S79 moves the block RATE, the number of blocks per season moves and every downstream
/// draw shifts with it (same consequence as Phases 31, 33, 34, 35).</para>
/// </summary>
public static class BlockerPicker
{
    /// <summary>
    /// Picks and returns the defensive <see cref="Slot"/> that earned the block.
    /// Consumes exactly one <paramref name="rng"/> draw.
    /// </summary>
    /// <param name="state">Current possession state — supplies the defensive side,
    /// <c>ShotType</c> for the zone, and <c>SelectedSlot</c> for the matched defender.
    /// Null <c>ShotType</c> falls back to <c>ShotLocation.Rim</c> (correct for putbacks,
    /// where Roll K forces Rim).</param>
    /// <param name="game">Live game state — provides the defensive lineup and roster.</param>
    /// <param name="matchupCfg">Matchup configuration — supplies the help share, the positional
    /// pair, the depth weights and the credit luck floor.</param>
    /// <param name="rng">RNG source. Consumes exactly one NextUnitInterval draw.</param>
    /// <param name="putback">True when the block came from Roll H's putback door, whose rate is
    /// a five-defender stack with no matched man. Passed by the Resolver from the continuation's
    /// <c>Putback</c> flag — it is not carried on <see cref="PossessionState"/>.</param>
    public static Slot Pick(
        PossessionState state,
        GameState       game,
        MatchupConfig   matchupCfg,
        IRng            rng,
        bool            putback = false)
    {
        var defense = state.Defense;
        var lineup  = game.LineupFor(defense);
        var roster  = game.RosterFor(defense);

        // null ShotType -> Rim fallback. Roll K's PutBack arm stamps ShotLocation.Rim before
        // re-entering Roll H, so this guard fires only on paths not yet imagined. Rim is the
        // correct fallback: putbacks are forced Rim.
        var zone = state.ShotType ?? ShotLocation.Rim;

        // ── Stage 1: collect populated defensive players ──────────────────────
        var players     = new Player?[5];
        var playerCount = 0;

        for (var i = 0; i < 5; i++)
        {
            var p = roster.PlayerAt(lineup.SlotAt(i + 1));
            if (p is null) continue;
            players[i] = p;
            playerCount++;
        }

        if (playerCount == 0)
            throw new InvalidOperationException(
                "BlockerPicker: no defensive players populated — " +
                "a block with zero defenders on the floor is an unreachable bug.");

        // ── Stage 2: contribution-based credit weights ────────────────────────
        // The matched defender is slot parity (DefenderPicker's man-to-man wiring), resolved
        // from the offense's selected shooter. It is -1 when no shooter slot was ever stamped
        // — the bonus-FT putback route, where Roll E never ran — and on the putback path,
        // which has no matched arm at all. A -1 makes every populated slot a helper.
        var matchedIndex = putback || state.SelectedSlot is null
            ? -1
            : state.SelectedSlot.Value.Number - 1;

        var weights = putback
            ? Matchup.PutbackBlockCreditWeights(players, matchupCfg)
            : Matchup.BlockCreditWeights(zone, players, matchedIndex, matchupCfg);

        var totalWeight = 0.0;
        for (var i = 0; i < 5; i++) totalWeight += weights[i];

        // The luck floor makes every populated slot strictly positive, so a zero total is
        // unreachable with playerCount > 0. Loud rather than silent if that ever changes.
        if (totalWeight <= 0.0)
            throw new InvalidOperationException(
                $"BlockerPicker: total credit weight {totalWeight} with {playerCount} populated " +
                "defenders — the luck floor should make this unreachable.");

        // ── Stage 3: one RNG draw — cumulative walk to chosen slot ───────────
        // Same shape as DefensiveRebounderPicker and StealerPicker. The final populated slot
        // is the implicit fallback (absorbs floating-point shortfall).
        var draw          = rng.NextUnitInterval() * totalWeight;
        var cumulative    = 0.0;
        var lastPopulated = -1;

        for (var i = 0; i < 5; i++)
        {
            if (players[i] is null) continue;
            lastPopulated = i;
            cumulative   += weights[i];
            if (draw <= cumulative)
                return lineup.SlotAt(i + 1);
        }

        // Fallback: floating-point edge — return the last populated slot.
        return lineup.SlotAt(lastPopulated + 1);
    }
}
