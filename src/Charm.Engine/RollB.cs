namespace Charm.Engine;

/// <summary>
/// Roll B — Halfcourt Initiation. The first beat after the offense is cleanly
/// into its halfcourt set. Decides whether the possession advances to player
/// selection or is interrupted by a foul or dead-ball turnover.
///
/// Follows the uniform roll contract: receives state + a finished pie, rolls
/// against it, returns one typed result, names no successor.
/// </summary>
public static class RollB
{
    public static RollResult Execute(PossessionState state, Pie<HalfcourtOutcome> pie, IRng rng)
    {
        var outcome = pie.Roll(rng.NextUnitInterval());

        return outcome switch
        {
            // Possession proceeds — hand off to player selection.
            HalfcourtOutcome.Proceed =>
                new Continue(ContinuationKind.IntoPlayerSelection, state),

            // Foul before any action — hand off to the shared foul-type resolver.
            HalfcourtOutcome.Foul =>
                new Continue(ContinuationKind.ResolveFoulType, state),

            // Dead-ball turnover in the frontcourt — hand off to turnover-type resolver.
            HalfcourtOutcome.DeadBallTurnover =>
                new Continue(ContinuationKind.ResolveTurnoverType, state),

            _ => throw new InvalidOperationException($"Unhandled halfcourt outcome '{outcome}'.")
        };
    }
}
