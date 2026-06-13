namespace Charm.Engine;

/// <summary>
/// Roll J — Transition Entry (run-or-not gate). The node a LIVE-BALL possession
/// enters when it starts off a defensive rebound: the new ball-handler decides
/// whether to PUSH or PULL IT OUT — grabbing a board deep in the backcourt does not
/// mean you run. This replaces Session 15's temp-route of a Transition start
/// through Roll A: a defensive rebound now produces a real live-ball entry.
///
/// Roll J decides ONLY whether we run. What the fast break PRODUCES (numbers
/// advantage, leak-outs, transition shot mix) is a separate later build that
/// <see cref="TransitionOutcome.Push"/> parks at via the new TransitionStub.
///
/// FIVE arms (<see cref="TransitionOutcome"/>), ALL continues — Roll J names no
/// terminal of its own; its two "ending" flavors resolve at shared downstream nodes:
///   <list type="bullet">
///   <item><see cref="TransitionOutcome.Settle"/> — pull it out, run a halfcourt set.
///   CONTINUE via <see cref="ContinuationKind.IntoPlayerSelection"/> (Roll E).</item>
///   <item><see cref="TransitionOutcome.Push"/> — we run. CONTINUE into player
///   selection (Roll E) via <see cref="ContinuationKind.IntoPlayerSelection"/> — the
///   SAME node <see cref="TransitionOutcome.Settle"/> uses — but STAMPING
///   <c>FastBreak=true</c> on the carried state so Roll E's generator draws the
///   transition selection pie. (Contextification #1: the old IntoTransition park is
///   retired; a fast break now produces a shot through the shared rolls, tilted by a
///   context.)</item>
///   <item><see cref="TransitionOutcome.Turnover"/> — coughed it up. CONTINUE to the
///   shared turnover node via <see cref="ContinuationKind.ResolveTurnoverType"/>,
///   STAMPING <see cref="TurnoverContext.Transition"/> on the ticket so Roll C
///   selects its transition turnover pie. The first station to stamp a non-default
///   turnover context — the forcing case for the whole ticket/station mechanism.</item>
///   <item><see cref="TransitionOutcome.DefensiveFoul"/> — fouled on the push. Charges
///   the rebound-LOSING team (= the new defense, <c>state.Defense</c>) via
///   <see cref="FoulTracker"/>, reads the bonus, and forks: below ->
///   <see cref="ContinuationKind.ResolveSidelineInbound"/>; in bonus ->
///   <see cref="ContinuationKind.ResolveFreeThrows"/> with the <see cref="BonusType"/>
///   payload. The THIRD feeder into the charge-and-fork, after Roll D and Roll I —
///   copied, not reinvented.</item>
///   <item><see cref="TransitionOutcome.JumpBall"/> — tie-up. CONTINUE to the shared
///   jump-ball node via <see cref="ContinuationKind.ResolveJumpBall"/>.</item>
///   </list>
///
/// Signature <c>(state, pie, game, rng)</c> — the Roll D / Roll I shape — because the
/// <see cref="TransitionOutcome.DefensiveFoul"/> arm mutates <see cref="GameState"/>
/// (charges the defensive team foul). The other four arms read nothing off
/// <see cref="GameState"/>.
///
/// FLAT-PIE MODIFIER SEAMS (documented, NOT built — the Push/Settle split is where
/// two SEPARATE, INDEPENDENT future inputs land, never fused into one pre-blended
/// weight, per the locked "strategy and matchup modifiers stay independent" rule):
///   1. REBOUNDER TILT (attribute): WHO grabbed the board nudges push vs. settle — a
///      guard pushes more than a center. Lands in the attribute layer, read off the
///      rebounder slot once selection/attribution names it.
///   2. COACH TEMPO (strategy): the team's up-tempo / low-tempo setting nudges push
///      vs. settle. Lands in the strategy layer.
/// Both attach at the GENERATOR (a smarter pie), exactly like the height-driven tip
/// contest and Roll C's pressure wire; Roll J itself never changes when they do.
/// </summary>
public static class RollJ
{
    public static RollResult Execute(
        PossessionState state, Pie<TransitionOutcome> pie, GameState game, IRng rng)
    {
        // Roll the five-way pie to a run-or-not outcome.
        var outcome = pie.Roll(rng.NextUnitInterval());

        return outcome switch
        {
            // Pull it out -> run a halfcourt set. CONTINUE to player selection.
            // The "proceed" analog; reads nothing off GameState.
            TransitionOutcome.Settle =>
                new Continue(ContinuationKind.IntoPlayerSelection, state),

            // We run -> player selection (Roll E), the SAME node Settle uses, but
            // STAMPED FastBreak=true so Roll E's generator draws the transition
            // selection pie. The marker rides on the carried state (it persists for
            // Roll G/H's deferred transition tilts), not as a Continue payload. The
            // old IntoTransition/TransitionStub park is retired (kept in the corner).
            TransitionOutcome.Push =>
                new Continue(ContinuationKind.IntoPlayerSelection, state with { FastBreak = true }),

            // Coughed it up -> shared turnover node, STAMPED Transition so Roll C
            // selects its transition pie. The context rides the ticket exactly as
            // Bonus/Flavor do; Roll C reads it and never queries Roll J.
            TransitionOutcome.Turnover =>
                new Continue(ContinuationKind.ResolveTurnoverType, state)
                {
                    TurnoverContext = TurnoverContext.Transition
                },

            // Fouled on the push -> charge the new defense (the rebound-losing team,
            // = state.Defense) and fork on the bonus via the shared charge-and-fork;
            // below bonus -> a sideline throw-in (no flavor).
            TransitionOutcome.DefensiveFoul =>
                DefensiveFoulCharge.Resolve(state, game, ContinuationKind.ResolveSidelineInbound),

            // Tie-up -> shared jump-ball node (consults the arrow). CONTINUE.
            TransitionOutcome.JumpBall =>
                new Continue(ContinuationKind.ResolveJumpBall, state),

            _ => throw new InvalidOperationException($"Unhandled transition outcome '{outcome}'.")
        };
    }
}
