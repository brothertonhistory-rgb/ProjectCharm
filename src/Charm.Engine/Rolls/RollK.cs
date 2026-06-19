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
///   <item><b>LiveBallTurnover</b> — TERMINAL. As of Contextification #3 a
///   <see cref="PossessionConsequence.TransitionStealTo"/> carrying the Steal context,
///   so the resolver routes the spawned possession to Roll J on the steal pie — the
///   third caller of the shared steal helper, alongside Roll C's two live arms.</item>
///   <item><b>ResetOffense</b> — CONTINUE back to Roll E on a BLANK slate (slot,
///   zone, result, AND FastBreak wiped): a fresh HALFCOURT play at the inherent
///   selection odds. The FastBreak wipe is the marker leak-guard — a reset off a
///   missed break must not redraw the transition selection pie. The
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
/// putback (the attribute layer, read off the still-stamped slot), the steal
/// ATTRIBUTION (which defender got the live turnover — the deferred attribution
/// layer; the LiveBallTurnover→Roll J routing itself is wired as of #3), and any stat
/// logging (events flow visibly through the loop, nothing is tallied). All documented
/// seams, none built here.
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
            // Charge the defensive team foul and fork on the bonus via the shared
            // charge-and-fork; below bonus -> a sideline throw-in (no flavor).
            OffensiveReboundOutcome.DefensiveFoul =>
                DefensiveFoulCharge.Resolve(state, game, ContinuationKind.ResolveSidelineInbound),

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

            // Phase 28: stamp FrontcourtVictim — a live turnover off an offensive rebound
            // happens in the frontcourt (state.Frontcourt == true): the offense had already
            // crossed halfcourt, shot, and rebounded before losing the ball live. The new
            // defense (thief) must go the full court → low-run odds. High-run requires proof;
            // a putback-traffic turnover is not a pick-six.
            OffensiveReboundOutcome.LiveBallTurnover =>
                new Terminal("LiveBallTurnover", state,
                    PossessionConsequence.TransitionStealTo(state.Defense, StealOrigin.FrontcourtVictim)),

            // Kick it back out and run a fresh play. Same possession stays alive —
            // CONTINUE back to Roll E (player selection). Wipe the prior shot's facts
            // so the reset draws the inherent selection odds on a blank slate; the
            // possession number does NOT increment (this is resolver-internal).
            OffensiveReboundOutcome.ResetOffense =>
                new Continue(ContinuationKind.IntoPlayerSelection,
                    state with { SelectedSlot = null, ShotType = null, Result = null, FastBreak = false, UsagePressure = null, UsageResidualPressure = null, ShooterAttentionShare = null, TeamBaseOpenness = null, TeamGravityLevel = null, TeamSpacingLevel = null, TeamConversionQuality = null, ReboundSlot = null }),

            _ => throw new InvalidOperationException($"Unhandled offensive-rebound outcome '{outcome}'.")
        };
    }
}
