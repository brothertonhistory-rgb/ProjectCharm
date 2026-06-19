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
    public static RollResult Execute(
        PossessionState state, Pie<TurnoverOutcome> pie, IRng rng,
        RollCConfig? config = null)
    {
        var outcome = pie.Roll(rng.NextUnitInterval());

        // The three violation arms (and ONLY they) stamp an invariant elapsed read
        // from config. Added in Contextification #5a as an OPTIONAL parameter with a
        // default, mirroring the generator's optional context default: every legacy
        // call site (the batch/context/pressure checks, ShowSamples, the resolver)
        // is byte-for-byte unchanged, because the violation arms are DORMANT (zero
        // weight in every live context) and therefore never reached on the live
        // path. Only the isolation check, which deliberately weights the violations,
        // passes a config. A violation arm reached without one fails LOUD here rather
        // than dereferencing null — consistent with the engine's fail-at-the-seam rule.
        double Elapsed(Func<RollCConfig, double> pick) =>
            config is null
                ? throw new InvalidOperationException(
                    "Roll C reached a violation arm without a RollCConfig to supply its invariant elapsed time.")
                : pick(config);

        // Every slice is a terminal; the Reason string carries the classification
        // (including the dead/live distinction by name) for the future entry roll
        // and attribution layer to consume. Elapsed time defers to the future
        // time roll (null) — a turnover has real path variance in how long it
        // took, unlike the invariant shot-clock violation.
        //
        // The consequence makes the dead/live axis FUNCTIONAL, not just named: the
        // ball goes to the other team (state.Defense) on every slice, but a dead-ball
        // slice restarts at Roll A while a live-ball slice (intercepted / stripped
        // live) is tagged a STEAL transition — the new offense pushing the other way.
        // As of Contextification #3 those live arms carry TransitionContext.Steal, so
        // the resolver routes the spawned possession to Roll J (live transition entry)
        // on the most run-happy pie — no longer temp-routed through Roll A.
        //
        // Spot-flip (Session 27): on a DEAD-BALL turnover the new offense inbounds
        // from wherever the ball already was. Lost it in the backcourt (Frontcourt ==
        // false) -> BallAdvancedTo: they start already across, skip Roll A's bring-up.
        // Lost it in the frontcourt (Frontcourt == true) -> DeadBallTo: normal restart.
        // Live-ball arms (steal transitions) are unchanged — they route to Roll J.
        return outcome switch
        {
            TurnoverOutcome.BadPassDeadBall =>
                new Terminal("BadPassDeadBall", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            // Phase 28: stamp steal origin from the VICTIM's Frontcourt flag (role-flip).
            // Frontcourt == false (victim in backcourt) → thief near scoring basket → BackcourtVictim (high run).
            // Frontcourt == true  (victim in halfcourt set) → thief must go full court → FrontcourtVictim (low run).
            TurnoverOutcome.BadPassIntercepted =>
                new Terminal("BadPassIntercepted", state,
                    PossessionConsequence.TransitionStealTo(state.Defense,
                        state.Frontcourt ? StealOrigin.FrontcourtVictim : StealOrigin.BackcourtVictim)),

            TurnoverOutcome.LostBallDeadBall =>
                new Terminal("LostBallDeadBall", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            // Phase 28: same role-flip as BadPassIntercepted above.
            TurnoverOutcome.LostBallLiveBall =>
                new Terminal("LostBallLiveBall", state,
                    PossessionConsequence.TransitionStealTo(state.Defense,
                        state.Frontcourt ? StealOrigin.FrontcourtVictim : StealOrigin.BackcourtVictim)),

            TurnoverOutcome.OffensiveFoul =>
                new Terminal("OffensiveFoul", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            // --- Contextification #5a: the expanded loss set. Every arm below is
            //     a DEAD-ball loss -> the ball goes to the defense on a dead-ball
            //     restart (a future inbound). DORMANT this session: zero weight in
            //     every live context, so none of these fire on the live path.
            //
            //     The seven turnover types defer elapsed time (null), exactly like
            //     the five existing turnovers. The three violation types are the
            //     ONLY Roll C arms that stamp their own elapsed: invariant, known
            //     here, needing no time roll — mirroring Roll A's violation
            //     terminals (whose copies #5b consolidates away).

            TurnoverOutcome.Travel =>
                new Terminal("Travel", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            TurnoverOutcome.DoubleDribble =>
                new Terminal("DoubleDribble", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            TurnoverOutcome.Carry =>
                new Terminal("Carry", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            TurnoverOutcome.ThreeSecondViolation =>
                new Terminal("ThreeSecondViolation", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            TurnoverOutcome.FiveSecondCloselyGuarded =>
                new Terminal("FiveSecondCloselyGuarded", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            TurnoverOutcome.OffensiveGoaltending =>
                new Terminal("OffensiveGoaltending", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            TurnoverOutcome.BackcourtViolation =>
                new Terminal("BackcourtViolation", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense)),

            // Violations carry their own INVARIANT elapsed (the only timed arms here).
            TurnoverOutcome.ShotClockViolation =>
                new Terminal("ShotClockViolation", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense))
                    { ElapsedSeconds = Elapsed(c => c.ShotClockViolationElapsedSeconds) },

            TurnoverOutcome.FiveSecondInbound =>
                new Terminal("FiveSecondInbound", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense))
                    { ElapsedSeconds = Elapsed(c => c.FiveSecondInboundElapsedSeconds) },

            TurnoverOutcome.TenSecondBackcourt =>
                new Terminal("TenSecondBackcourt", state,
                    state.Frontcourt
                        ? PossessionConsequence.DeadBallTo(state.Defense)
                        : PossessionConsequence.BallAdvancedTo(state.Defense))
                    { ElapsedSeconds = Elapsed(c => c.TenSecondBackcourtElapsedSeconds) },

            _ => throw new InvalidOperationException($"Unhandled turnover outcome '{outcome}'.")
        };
    }
}
