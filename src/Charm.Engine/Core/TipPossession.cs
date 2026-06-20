namespace Charm.Engine;

/// <summary>
/// Resolves a tip-off and creates the resulting possession state. Used for
/// both the regulation opening tip and each overtime opening tip.
///
/// Contract: <paramref name="game"/> must have <see cref="GameState.PossessionArrow"/>
/// in state <see cref="ArrowState.Off"/> — call <see cref="GameState.ResetPossessionArrow"/>
/// before each overtime tip. <see cref="JumpBall.Resolve"/> then conducts the
/// 50/50 contest, sets the arrow to the loser, and returns the winner.
/// </summary>
public static class TipPossession
{
    /// <summary>Resolve a tip-off and return the opening possession state.</summary>
    /// <param name="game">Game state with arrow Off (fresh game or after
    /// <see cref="GameState.ResetPossessionArrow"/> for overtime).</param>
    /// <param name="rng">RNG to consume for the 50/50 tip draw.</param>
    /// <param name="possessionNumber">The possession number for the returned state.
    /// Pass 1 for the regulation opening; pass the next sequential number for overtime.</param>
    public static PossessionState CreateFromTip(GameState game, IRng rng, int possessionNumber)
    {
        if (game.PossessionArrow != ArrowState.Off)
            throw new InvalidOperationException(
                "TipPossession.CreateFromTip requires PossessionArrow to be Off. " +
                "Call GameState.ResetPossessionArrow() before each overtime tip.");
        if (possessionNumber <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(possessionNumber),
                "Tip possession number must be positive.");
        var award = JumpBall.Resolve(game, rng);
        var offense = award.AwardedTo;
        return new PossessionState(
            PossessionNumber: possessionNumber,
            Offense: offense,
            Defense: offense == TeamSide.Home ? TeamSide.Away : TeamSide.Home,
            Entry: EntryType.DeadBallInbound);
    }
}
