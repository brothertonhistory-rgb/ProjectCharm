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
///   • Arrow OFF (opening tip / overtime tip) — a real contest. Resolved as a
///     pure 50/50 coin flip for now. The winner gets the ball; the arrow is
///     turned ON pointing at the LOSER (NCAA: the tip-loser is owed the next
///     alternating-possession award).
///
///   • Arrow ON — a routine alternating-possession situation. The team the
///     arrow points at is awarded the ball, then the arrow flips away from them.
///     Deterministic; no randomness.
///
/// FUTURE SEAM — height-driven tip contest. The 50/50 coin flip is a placeholder
/// for the one true contest in this node. The intended model: tip-win
/// probability driven by the centers' height differential, non-linear (a 1" edge
/// is a near-negligible bump; a large gap, ~8", approaches near-certainty) — an
/// S-curve on height-diff, not linear. It plugs in exactly here, consuming the
/// center matchup once a player/attribute layer exists. Nothing else in this
/// node changes when it does: the node still returns "which team won," the arrow
/// still consumes it. Same seam discipline as the stub pie generators.
/// </summary>
public static class JumpBall
{
    /// <summary>Resolve a jump-ball situation against the game's arrow state.
    /// Mutates <paramref name="game"/> (sets or flips the arrow).</summary>
    public static JumpBallAward Resolve(GameState game, IRng rng)
    {
        if (game.PossessionArrow == ArrowState.Off)
        {
            // Opening / OT tip — a real contest. Placeholder: 50/50 coin flip.
            // FUTURE: height-differential S-curve (see class summary).
            var homeWinsTip = rng.NextUnitInterval() < 0.5;
            var winner = homeWinsTip ? TeamSide.Home : TeamSide.Away;
            var loser = homeWinsTip ? TeamSide.Away : TeamSide.Home;

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
}
