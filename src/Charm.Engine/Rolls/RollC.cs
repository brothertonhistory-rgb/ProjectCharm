namespace Charm.Engine;

/// <summary>
/// Roll C — Turnover Classification. The shared turnover node: every roll in the
/// engine that produces a turnover routes here. Roll C decides *what kind* of
/// turnover it was and ends the possession. It never knows or cares who fed it —
/// "many feeders, one node."
///
/// Unlike Rolls A and B, every outcome here is a TERMINAL. The possession is
/// over the moment a turnover occurs; the ball changes hands regardless of slice.
/// Roll C is therefore the first terminal-producing roll: the resolver executes
/// it and feeds the resulting Terminal back through its loop, exactly as it
/// already does for Roll B.
///
/// What Roll C does NOT do: assign the turnover (or steal) to an individual
/// player. Player attribution for counting stats is a separate layer that runs
/// over outcomes — it reads who was involved (the offensive ball-handler is
/// already selected upstream; the crediting defender is named by matchup) and
/// assigns credit. That layer is future infrastructure and is orthogonal to the
/// possession chain. Roll C only classifies type and terminates.
///
/// Follows the uniform roll contract: receives state + a finished pie, rolls
/// against it, returns one typed result, names no successor.
/// </summary>
public static class RollC
{
    public static RollResult Execute(PossessionState state, Pie<TurnoverOutcome> pie, IRng rng)
    {
        var outcome = pie.Roll(rng.NextUnitInterval());

        // Every slice is a terminal; the Reason string carries the classification
        // (including the dead/live distinction by name) for the future entry roll
        // and attribution layer to consume. Elapsed time defers to the future
        // time roll (null) — a turnover has real path variance in how long it
        // took, unlike the invariant shot-clock violation.
        //
        // The consequence makes the dead/live axis FUNCTIONAL, not just named: the
        // ball goes to the other team (state.Defense) on every slice, but a dead-ball
        // slice restarts at Roll A while a live-ball slice (intercepted / stripped
        // live) is tagged Transition — the new offense pushing the other way. (The
        // thin Governor temp-routes Transition through Roll A this session; the tag
        // is honest for when the live-ball entry node lands.)
        return outcome switch
        {
            TurnoverOutcome.BadPassDeadBall =>
                new Terminal("BadPassDeadBall", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            TurnoverOutcome.BadPassIntercepted =>
                new Terminal("BadPassIntercepted", state,
                    PossessionConsequence.TransitionTo(state.Defense)),

            TurnoverOutcome.LostBallDeadBall =>
                new Terminal("LostBallDeadBall", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            TurnoverOutcome.LostBallLiveBall =>
                new Terminal("LostBallLiveBall", state,
                    PossessionConsequence.TransitionTo(state.Defense)),

            TurnoverOutcome.OffensiveFoul =>
                new Terminal("OffensiveFoul", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            _ => throw new InvalidOperationException($"Unhandled turnover outcome '{outcome}'.")
        };
    }
}
