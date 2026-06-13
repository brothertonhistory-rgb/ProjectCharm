namespace Charm.Engine;

/// <summary>
/// Roll A — Entry: Inbounds (Dead Ball). The first node of the engine and the
/// template every later roll follows: receive state + a finished pie, roll
/// against the pie, return exactly one typed result, name no successor.
/// </summary>
public static class RollA
{
    public static RollResult Execute(PossessionState state, Pie<EntryOutcome> pie, IRng rng, RollAConfig cfg)
    {
        // The pie is valid by construction (see Pie&lt;T&gt;), so receiving it is
        // already the "validate on receipt" guarantee — an invalid pie could not
        // have been built and handed here. (`cfg` is retained on the signature for
        // call-site parity; Roll A no longer reads it now that the violation
        // terminals — its only former config readers — moved to Roll C in #6.)
        var draw = rng.NextUnitInterval();
        var outcome = pie.Roll(draw);

        return outcome switch
        {
            // Clean entry -> CONTINUE. The offense crossed into the set, so LATCH the
            // court-state to frontcourt on the way to Roll B: from here the
            // backcourt-only ways to lose it are gone. The halfcourt set is opaque to
            // Roll A.
            EntryOutcome.CleanEntry =>
                new Continue(ContinuationKind.IntoHalfcourtSet, state with { Frontcourt = true }),

            // Turnover -> CONTINUE. Stamp the loss CONTEXT by the current court-state:
            // a backcourt bring-up routes to Roll C's EntryBackcourt pie (where the
            // 5-second inbound, 10-second backcourt and backcourt shot-clock live), a
            // frontcourt re-inbound to the Halfcourt pie (where those cannot happen).
            // Roll C picks the actual type; Roll A only names the context.
            EntryOutcome.Turnover =>
                new Continue(ContinuationKind.ResolveTurnoverType, state)
                {
                    TurnoverContext = state.Frontcourt
                        ? TurnoverContext.Halfcourt
                        : TurnoverContext.EntryBackcourt
                },

            // Offensive foul -> CONTINUE. A player-control foul (charge / illegal
            // screen): the ball goes to the other team on a dead-ball restart, with no
            // free throws and no bonus credit. The resolver maps this kind straight to
            // the offensive-foul loss terminal — no pie, no Roll D charge.
            EntryOutcome.OffensiveFoul =>
                new Continue(ContinuationKind.ResolveOffensiveFoul, state),

            // Defensive foul -> CONTINUE. A non-shooting defensive foul: hand off to
            // Roll D, which charges the team foul and forks on the bonus (below bonus
            // the offense keeps the ball and re-inbounds in the CURRENT court-state; in
            // bonus it goes to the line). Real variance, never resolved here.
            EntryOutcome.DefensiveFoul =>
                new Continue(ContinuationKind.ResolveFoulType, state),

            // Jump ball -> CONTINUE. A future jump-ball resolver consults the
            // possession arrow on GameState.
            EntryOutcome.JumpBall =>
                new Continue(ContinuationKind.ResolveJumpBall, state),

            _ => throw new InvalidOperationException($"Unhandled entry outcome '{outcome}'.")
        };
    }
}
