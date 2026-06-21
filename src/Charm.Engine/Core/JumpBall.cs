namespace Charm.Engine;

/// <summary>The result of resolving a jump-ball situation: which team is awarded
/// the ball, and whether this was the opening/OT tip (a real contest) or a
/// routine alternating-possession arrow read.</summary>
public readonly record struct JumpBallAward(TeamSide AwardedTo, bool WasTipContest);

/// <summary>
/// The shared jump-ball node. Every roll's JumpBall exit routes here. There is
/// no pie: a jump ball is not a weighted-outcome roll, it is a state operation
/// on the possession arrow.
///
/// Two behaviors, by arrow state:
///
///   • Arrow OFF (opening tip / overtime tip) — a real contest. The jumper for
///     each team is the player with the highest <see cref="Player.Wingspan"/> in
///     the current lineup. Win probability is scaled by the gap between the two
///     jumpers' Wingspan ratings: a 7-rating-point gap on the 0–99 scale shifts
///     the probability by ±0.40, clamped to [0.10, 0.90] (no tip is ever a
///     guaranteed win). When no roster is populated for a side, that side falls
///     back to Wingspan 50, preserving 50/50 behavior for unpopulated games.
///     Wingspan is a 0–99 reach rating, not literal inches. Curve is
///     calibration-pending.
///
///   • Arrow ON — a routine alternating-possession situation. The team the
///     arrow points at is awarded the ball, then the arrow flips away from them.
///     Deterministic; no randomness.
/// </summary>
public static class JumpBall
{
    /// <summary>Resolve a jump-ball situation against the game's arrow state.
    /// Mutates <paramref name="game"/> (sets or flips the arrow).</summary>
    public static JumpBallAward Resolve(GameState game, IRng rng)
    {
        if (game.PossessionArrow == ArrowState.Off)
        {
            // Opening / OT tip — wingspan-driven contest.
            var homeMax  = MaxWingspan(game, TeamSide.Home);
            var awayMax  = MaxWingspan(game, TeamSide.Away);
            var homeWinsTip = rng.NextUnitInterval() < HomeWinProbability(homeMax, awayMax);
            var winner = homeWinsTip ? TeamSide.Home : TeamSide.Away;
            var loser  = homeWinsTip ? TeamSide.Away : TeamSide.Home;

            // Arrow turns ON pointing at the LOSER — they are owed the next award.
            game.SetPossessionArrow(loser);
            return new JumpBallAward(winner, WasTipContest: true);
        }

        // Arrow ON — routine alternating possession. Award to whoever it points
        // at, then flip away from them.
        var awarded = game.PossessionArrow == ArrowState.Home ? TeamSide.Home : TeamSide.Away;
        game.FlipPossessionArrow();
        return new JumpBallAward(awarded, WasTipContest: false);
    }

    /// <summary>
    /// The highest <see cref="Player.Wingspan"/> among populated slots 1–5 for
    /// <paramref name="side"/>. Returns 50 when no roster is populated — preserves
    /// 50/50 tip behavior only when BOTH sides are unpopulated; a real lineup facing
    /// an unpopulated side correctly holds the wingspan advantage.
    /// </summary>
    private static int MaxWingspan(GameState game, TeamSide side)
    {
        var roster  = game.RosterFor(side);
        var lineup  = game.LineupFor(side);
        var max     = -1;
        for (var slot = 1; slot <= 5; slot++)
        {
            var player = roster.PlayerAt(lineup.SlotAt(slot));
            if (player is not null && player.Wingspan > max)
                max = player.Wingspan;
        }
        return max >= 0 ? max : 50;
    }

    /// <summary>
    /// Home win probability from the wingspan gap. A 7-rating-point gap on the
    /// 0–99 scale shifts probability by ±0.40 from 50/50, clamped to [0.10, 0.90].
    /// Calibration-pending.
    /// </summary>
    private static double HomeWinProbability(int homeWingspan, int awayWingspan)
    {
        var gap = homeWingspan - awayWingspan;
        var raw = 0.50 + (gap / 7.0) * 0.40;
        return Math.Clamp(raw, 0.10, 0.90);
    }
}
