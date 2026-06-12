namespace Charm.Engine;

/// <summary>
/// Roll K — Offensive-Rebound Resolution. The node the offense lands at the instant
/// it secures its own miss (Roll I's <see cref="ReboundOutcome.OffensiveRebound"/>),
/// replacing the parked <c>OffensiveReboundStub</c> on the existing
/// <see cref="ContinuationKind.ResolveOffensiveRebound"/> edge — the same stub→roll
/// swap every prior session ran (C, D, E, F, G, H, I, J). Closest structural
/// sibling: Roll I (a gate with mixed terminals and continues that also feeds the
/// charge-and-fork).
///
/// It is the engine's highest-volume open node and the FIRST possession-EXTENDING
/// roll: <see cref="OffensiveReboundOutcome.PutBack"/> and
/// <see cref="OffensiveReboundOutcome.ResetOffense"/> keep the SAME possession alive
/// and loop it back up the chain — and that loop happens entirely INSIDE the
/// resolver's <c>while</c> walk. The Governor never sees a reset; it only hears
/// about a possession when it ENDS, so its one-in-one-out invariant
/// (<c>terminal + parked == cap</c>) is untouched, and the possession count does NOT
/// increment on a reset or a putback.
///
/// Seven arms (<see cref="OffensiveReboundOutcome"/>), routed:
/// <list type="bullet">
///   <item><b>PutBack</b> — immediate go-back-up. CONTINUE into Roll H with the zone
///   FORCED to <see cref="ShotLocation.Rim"/> and the <see cref="Continue.Putback"/>
///   ticket set, so Roll H's generator selects its distinct putback pie. The
///   selected slot rides through untouched (the deferred same-player rebound tilt
///   reads it). A missed putback re-enters Roll I on the flat pie — the re-entrant
///   scrum that converges probabilistically (proven in the harness).</item>
///   <item><b>JumpBall</b> — CONTINUE to the shared jump-ball node.</item>
///   <item><b>DefensiveFoul</b> — charge the defense, bonus-fork. The FOURTH feeder
///   into the shared charge-and-fork (after D, I, J) — copied verbatim.</item>
///   <item><b>OffensiveFoul</b> — TERMINAL. Dead ball to the other team at Roll A.
///   No foul charged (Roll C's OffensiveFoul precedent).</item>
///   <item><b>DeadBallTurnover</b> — TERMINAL. Same consequence as OffensiveFoul.</item>
///   <item><b>LiveBallTurnover</b> — TERMINAL, PARKED: a plain
///   <see cref="PossessionConsequence.TransitionTo"/> with no context ticket, so the
///   resolver temp-routes the spawn through Roll A exactly as steals do now. Its real
///   home is the transition module via the steal feeder.</item>
///   <item><b>ResetOffense</b> — CONTINUE back to Roll E on a BLANK slate (slot,
///   zone, result wiped): a fresh play at the inherent selection odds. The
///   possession stays alive; the count does not increment. Re-enters at E, not Roll
///   B — the offensive-rebound pie already absorbed the turnover/foul/jumpball
///   hazards, so routing through B would double-charge them.</item>
/// </list>
///
/// Signature <c>(state, pie, game, rng)</c> — the Roll D / I / J shape — because the
/// <see cref="OffensiveReboundOutcome.DefensiveFoul"/> arm mutates
/// <see cref="GameState"/> (charges the defensive team foul). The other six arms read
/// nothing off <see cref="GameState"/>.
///
/// What Roll K does NOT do: the actual putback make/foul percentages (Roll H's
/// putback pie — a future basketball call), the same-player rebound tilt on a missed
/// putback (the attribute layer, read off the still-stamped slot), the
/// LiveBallTurnover→transition wiring (the steal feeder), and any stat logging
/// (events flow visibly through the loop, nothing is tallied). All documented seams,
/// none built here.
/// </summary>
public static class RollK
{
    public static RollResult Execute(
        PossessionState state, Pie<OffensiveReboundOutcome> pie, GameState game, IRng rng)
    {
        // 1. Roll the seven-way pie to an offensive-rebound outcome.
        var outcome = pie.Roll(rng.NextUnitInterval());

        // 2. Route per slice — mixed Terminal/Continue (the Roll I shape).
        return outcome switch
        {
            // Go straight back up at the rim. Same possession stays alive — CONTINUE.
            // Force the zone to Rim and stamp the putback ticket so Roll H selects its
            // distinct putback pie. The carried slot rides through untouched (the
            // future same-player rebound tilt reads it); Roll H overwrites Result.
            OffensiveReboundOutcome.PutBack =>
                new Continue(ContinuationKind.IntoShotResolution,
                    state with { ShotType = ShotLocation.Rim })
                {
                    Putback = true
                },

            // Tie-up on the board -> shared jump-ball node. CONTINUE.
            OffensiveReboundOutcome.JumpBall =>
                new Continue(ContinuationKind.ResolveJumpBall, state),

            // Foul on the defense in the scrum. Offense retains — CONTINUE.
            // Charge the defensive team foul and read the bonus, exactly as Roll I.
            OffensiveReboundOutcome.DefensiveFoul =>
                ResolveFoulOnDefense(state, game),

            // Offensive foul in the scrum. Ball switches teams — TERMINAL.
            // No foul charged (Roll C's OffensiveFoul precedent).
            OffensiveReboundOutcome.OffensiveFoul =>
                new Terminal("OffensiveFoul", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            // Dead-ball turnover off the board. Ball switches teams — TERMINAL.
            // Same consequence as the offensive foul: dead-ball inbound at Roll A.
            OffensiveReboundOutcome.DeadBallTurnover =>
                new Terminal("DeadBallTurnover", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            // Live-ball turnover off the board. Ball to the defense on a live push —
            // TERMINAL. PARKED: a plain TransitionTo with NO context ticket, so the
            // resolver temp-routes the spawn through Roll A exactly as steals do now.
            // The steal-feeder session flips this to the transition entry in one line.
            OffensiveReboundOutcome.LiveBallTurnover =>
                new Terminal("LiveBallTurnover", state,
                    PossessionConsequence.TransitionTo(state.Defense)),

            // Kick it back out and run a fresh play. Same possession stays alive —
            // CONTINUE back to Roll E (player selection). Wipe the prior shot's facts
            // so the reset draws the inherent selection odds on a blank slate; the
            // possession number does NOT increment (this is resolver-internal).
            OffensiveReboundOutcome.ResetOffense =>
                new Continue(ContinuationKind.IntoPlayerSelection,
                    state with { SelectedSlot = null, ShotType = null, Result = null }),

            _ => throw new InvalidOperationException($"Unhandled offensive-rebound outcome '{outcome}'.")
        };
    }

    /// <summary>
    /// The defensive-foul arm: charge the defensive team foul via
    /// <see cref="FoulTracker"/>, read the resulting bonus, and fork to the
    /// sideline-inbound (below bonus) or free-throw (in bonus) continuation. Copied
    /// verbatim from Roll I's charge-and-read (itself from Roll D) — the fourth
    /// feeder into the shared fork.
    /// </summary>
    private static RollResult ResolveFoulOnDefense(PossessionState state, GameState game)
    {
        // Charge the foul to the fouling team = the defense this possession.
        var foulingTeam = state.Defense;
        game.Fouls.Increment(foulingTeam);

        // Read the bonus the fouling team is now in — a state read, not a roll.
        var bonus = game.Fouls.BonusFor(foulingTeam);

        if (bonus == BonusType.None)
        {
            // Below bonus: offense inbounds from the sideline. Same possession.
            return new Continue(ContinuationKind.ResolveSidelineInbound, state)
            {
                Bonus = bonus
            };
        }

        // In bonus (OneAndOne or Double): bonus free throws. Same possession. Bonus
        // type rides as functional payload, exactly as Roll D / I / J.
        return new Continue(ContinuationKind.ResolveFreeThrows, state)
        {
            Bonus = bonus
        };
    }
}
