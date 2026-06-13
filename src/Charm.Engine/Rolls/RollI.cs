namespace Charm.Engine;

/// <summary>
/// Roll I — Rebound / Loose-Ball Resolution. The node a MISSED SHOT (Roll H's
/// <c>Miss</c>) and a BLOCKED SHOT (Roll H's <c>Blocked</c>) both drain into, on the
/// shared <c>ResolveRebound</c> edge — a blocked shot is a loose-ball scramble, which
/// is the same battle a missed-shot rebound already is. Replaces the parked
/// <c>ReboundStub</c> the way every prior stub→roll swap has gone (C, D, E, F, G, H).
///
/// Classifies the loose ball into SEVEN outcomes (the same vocabulary Roll M, the
/// free-throw-board resolution, carries) and routes — every arm to a node that already
/// exists, opening NO new stub:
/// <list type="bullet">
///   <item><see cref="ReboundOutcome.DefensiveRebound"/> — defense secures the board on
///   a LIVE board; ball switches teams; TERMINAL, a transition start to the defense
///   (carries the <see cref="TransitionContext.Rebound"/> ticket → Roll J).</item>
///   <item><see cref="ReboundOutcome.OffensiveRebound"/> — offense secures the board;
///   same possession; CONTINUE to <see cref="ContinuationKind.ResolveOffensiveRebound"/>
///   (Roll K).</item>
///   <item><see cref="ReboundOutcome.LooseBallFoulOnDefense"/> — foul on the defense;
///   offense retains; charges the defensive team foul and forks on the bonus. CONTINUE.</item>
///   <item><see cref="ReboundOutcome.LooseBallFoulOnOffense"/> and
///   <see cref="ReboundOutcome.OutOfBoundsOffOffense"/> — ball to the defense on a DEAD
///   ball; TERMINALS, a dead-ball inbound at Roll A. No foul charged (Roll C's
///   <c>OffensiveFoul</c> precedent); they differ only in the reason label.</item>
///   <item><see cref="ReboundOutcome.OutOfBoundsOffDefense"/> — offense retains on a
///   DEAD ball; CONTINUE to <see cref="ContinuationKind.ResolveSidelineInbound"/>, NO
///   charge and NO fork.</item>
///   <item><see cref="ReboundOutcome.JumpBall"/> — tie-up; CONTINUE to the shared
///   jump-ball node (consults the arrow).</item>
/// </list>
///
/// Which pie those seven arms are weighted by is selected by the
/// <see cref="ReboundSource"/> the loose ball arrived with — read by Roll I's GENERATOR,
/// never by this roll. A null/<see cref="ReboundSource.LiveBall"/> stamp (every legacy
/// miss feeder) draws the live-miss pie; <see cref="ReboundSource.Block"/> (Roll H's
/// Blocked arm) draws the block pie. The ROUTING below is identical for both — only the
/// weights differ.
///
/// Signature <c>(state, pie, game, rng)</c> — the same shape as Roll D / M — because
/// the <see cref="ReboundOutcome.LooseBallFoulOnDefense"/> arm mutates
/// <see cref="GameState"/> (charges the defensive team foul). The other six arms read
/// nothing off <see cref="GameState"/>.
///
/// Stamps NO new <see cref="PossessionState"/> fact. Which slot grabbed the board (or
/// got the block) is the deferred attribution layer. The terminal reason names the
/// outcome (Roll C pattern); the stub labels record the continue landings.
/// </summary>
public static class RollI
{
    public static RollResult Execute(
        PossessionState state, Pie<ReboundOutcome> pie, GameState game, IRng rng)
    {
        // 1. Roll the four-way pie to a rebound outcome.
        var outcome = pie.Roll(rng.NextUnitInterval());

        // 2. Route per slice — mixed Terminal/Continue pattern (Roll H shape).
        return outcome switch
        {
            // Defense secures the board. Ball switches teams — TERMINAL.
            // Consequence: ball to the rebounding team (= the defense this
            // possession) on a LIVE-BALL transition push, carrying the Rebound
            // context ticket. As of Session 16 the resolver routes this to Roll J
            // (the real transition entry), no longer temp-routing it through Roll A.
            ReboundOutcome.DefensiveRebound =>
                new Terminal("DefensiveRebound", state,
                    PossessionConsequence.TransitionReboundTo(state.Defense)),

            // Offense secures the board. Same possession stays alive — CONTINUE.
            ReboundOutcome.OffensiveRebound =>
                new Continue(ContinuationKind.ResolveOffensiveRebound, state),

            // Loose-ball foul on the defense. Offense retains — CONTINUE.
            // Charge the defensive team foul and read the bonus, exactly as Roll D.
            ReboundOutcome.LooseBallFoulOnDefense =>
                ResolveFoulOnDefense(state, game),

            // Loose-ball foul on the offense. Ball switches teams — TERMINAL.
            // No foul charged (Roll C's OffensiveFoul precedent).
            // Consequence: ball to the other team on a dead-ball inbound at Roll A.
            ReboundOutcome.LooseBallFoulOnOffense =>
                new Terminal("LooseBallFoulOnOffense", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            // Ball out of bounds last off the OFFENSE (a carom off the rim and out, a
            // fumbled board). Ball switches teams — TERMINAL. Same routing as the
            // offensive loose-ball foul (defense's ball at Roll A), NO foul charged —
            // only the reason label differs. Distinct from DefensiveRebound so the
            // defense starts DEAD (inbound under the far basket) rather than on a live
            // push. Mirrors Roll M's OutOfBoundsOffOffense arm.
            ReboundOutcome.OutOfBoundsOffOffense =>
                new Terminal("OutOfBoundsOffOffense", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            // Ball out of bounds last off the DEFENSE (a swatted block out, a fumbled
            // loose ball). Offense retains — CONTINUE to the sideline-inbound node.
            // NO charge, NO bonus fork: no foul means no bonus question, so this is
            // always a plain sideline inbound. Mirrors Roll M's OutOfBoundsOffDefense
            // arm. (The own-side inbound modifiers are the inbound node's job, landing
            // with the Roll A reshape.)
            ReboundOutcome.OutOfBoundsOffDefense =>
                new Continue(ContinuationKind.ResolveSidelineInbound, state),

            // Tie-up on the loose ball. -> shared jump-ball node (consults the arrow),
            // exactly as Roll K's and Roll M's tie-up arms. The current possession ends
            // at that node; the awarded team's ensuing possession is a new one.
            ReboundOutcome.JumpBall =>
                new Continue(ContinuationKind.ResolveJumpBall, state),

            _ => throw new InvalidOperationException($"Unhandled rebound outcome '{outcome}'.")
        };
    }

    /// <summary>
    /// The loose-ball-foul-on-defense arm: charge the defensive team foul via
    /// <see cref="FoulTracker"/>, read the resulting bonus, and fork to the
    /// sideline-inbound (below bonus) or free-throw (in bonus) continuation.
    /// Copied verbatim from Roll D's charge-and-read pattern.
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
        else
        {
            // In bonus (OneAndOne or Double): bonus free throws. Same possession.
            // Bonus type rides as functional payload, exactly as Roll D.
            return new Continue(ContinuationKind.ResolveFreeThrows, state)
            {
                Bonus = bonus
            };
        }
    }
}
