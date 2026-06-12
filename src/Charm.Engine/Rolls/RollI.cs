namespace Charm.Engine;

/// <summary>
/// Roll I — Rebound Resolution. The node a MISSED SHOT (Roll H's <c>Miss</c>)
/// drains into. Replaces the parked <c>ReboundStub</c> on the existing
/// <c>ResolveRebound</c> edge, exactly the way every prior stub→roll swap has
/// gone (C, D, E, F, G, H).
///
/// Classifies the rebound into four outcomes and routes:
/// <list type="bullet">
///   <item><see cref="ReboundOutcome.DefensiveRebound"/> — defense secures the
///   board; ball switches teams; possession ENDS (Terminal). Next entry is a live
///   push into the future transition roll — recorded as design knowledge, not
///   routed here.</item>
///   <item><see cref="ReboundOutcome.OffensiveRebound"/> — offense secures the
///   board; same possession stays alive; Continue to
///   <see cref="ContinuationKind.ResolveOffensiveRebound"/> →
///   <c>OffensiveReboundStub</c>.</item>
///   <item><see cref="ReboundOutcome.LooseBallFoulOnDefense"/> — foul on the
///   defense in the scramble; offense retains. Charges the defensive team foul via
///   <see cref="FoulTracker"/>; reads the bonus; forks: None →
///   <see cref="ContinuationKind.ResolveSidelineInbound"/>; OneAndOne/Double →
///   <see cref="ContinuationKind.ResolveFreeThrows"/> with the
///   <see cref="BonusType"/> payload. Continue.</item>
///   <item><see cref="ReboundOutcome.LooseBallFoulOnOffense"/> — foul on the
///   offense; ball switches teams; possession ENDS (Terminal). No foul charged
///   (Roll C's <c>OffensiveFoul</c> precedent). Next entry is a dead-ball inbound
///   at Roll A — recorded as design knowledge, not routed here.</item>
/// </list>
///
/// This is the first roll in the engine whose job includes handing the ball to
/// the OTHER team, making it the first roll where terminals carry the
/// possession-end flag that will later trigger stat accumulation.
///
/// Signature <c>(state, pie, game, rng)</c> — the same shape as Roll D — because
/// the <see cref="ReboundOutcome.LooseBallFoulOnDefense"/> arm mutates
/// <see cref="GameState"/> (charges the defensive team foul). The other three
/// arms read nothing off <see cref="GameState"/>.
///
/// Stamps NO new <see cref="PossessionState"/> fact. Which slot grabbed the board
/// is the deferred attribution layer. The terminal reason names the outcome (Roll C
/// pattern); the stub labels record the continue landings.
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
