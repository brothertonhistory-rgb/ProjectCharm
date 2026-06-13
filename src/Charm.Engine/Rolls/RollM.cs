namespace Charm.Engine;

/// <summary>
/// Roll M — Free-Throw Rebound Resolution. The node a MISSED FINAL free throw drains
/// into: Roll L's FT-sequence driver routes a live last-shot miss to
/// <see cref="ContinuationKind.ResolveFTRebound"/>, which now executes Roll M instead
/// of parking — the same stub→roll swap every prior session ran (C, D, E, F, G, H, I,
/// J, K). Closest structural sibling: Roll I (a board-battle gate with mixed terminals
/// and continues that also feeds the shared charge-and-fork).
///
/// Roll M is Roll I's shape with two differences — a more DEFENSIVE board population
/// (everyone lined along the lane off a free throw, no shooter crashing in) and an
/// added OUT-OF-BOUNDS pair — and it opens NO new stub: every arm routes to a node
/// that already exists. Seven arms (<see cref="FreeThrowReboundOutcome"/>), routed:
/// <list type="bullet">
///   <item><b>DefensiveRebound</b> — TERMINAL. A live transition start to the defense,
///   carrying the <see cref="TransitionSource.FreeThrowRebound"/> ticket so the
///   resolver routes the spawned possession to Roll J and Roll J selects its tamer,
///   conservative run-or-not pie.</item>
///   <item><b>OffensiveRebound</b> — CONTINUE to the offensive-rebound node (Roll K),
///   stamping the <see cref="OffensiveReboundSource.FreeThrow"/> source on the
///   continuation so Roll K selects its FT-specific pie (more putback, point-blank).
///   Every existing feeder (Roll I) stamps nothing → reads as
///   <see cref="OffensiveReboundSource.LiveBall"/>, so the field-goal path is
///   byte-for-byte unchanged.</item>
///   <item><b>LooseBallFoulOnDefense</b> — CONTINUE. Charges the defense and forks on
///   the bonus: below -> sideline inbound; in bonus -> free throws (Roll L). The FIFTH
///   feeder into the shared charge-and-fork (after D / I / J / K) — copied verbatim.</item>
///   <item><b>LooseBallFoulOnOffense</b> and <b>OutOfBoundsOffOffense</b> — TERMINALS.
///   Dead ball to the defense at Roll A, NO foul charged (Roll C's OffensiveFoul
///   precedent). Same routing, different reason label: the OOB arm is the foul arm
///   minus the whistle.</item>
///   <item><b>OutOfBoundsOffDefense</b> — CONTINUE to the sideline-inbound node, NO
///   charge and NO bonus fork. Because no foul is charged there is no bonus question,
///   so this is ALWAYS a plain sideline inbound (unlike the loose-ball-defense arm,
///   which forks).</item>
///   <item><b>JumpBall</b> — CONTINUE to the shared jump-ball node (consults the
///   arrow), exactly as Roll K's tie-up arm.</item>
/// </list>
///
/// Signature <c>(state, pie, game, rng)</c> — the Roll D / I / J / K shape — because
/// the <see cref="FreeThrowReboundOutcome.LooseBallFoulOnDefense"/> arm mutates
/// <see cref="GameState"/> (charges the defensive team foul). The other six arms read
/// nothing off <see cref="GameState"/>.
///
/// Roll M fires ONCE per FT trip: an offensive board goes to Roll K, and if a
/// resulting putback misses, that is a live FIELD-GOAL miss → Roll I, NOT back to Roll
/// M. So Roll M adds no new convergence loop; the existing Roll K loop proof holds.
/// Stamps NO new <see cref="PossessionState"/> fact; which slot grabbed the board is
/// the deferred attribution layer.
/// </summary>
public static class RollM
{
    public static RollResult Execute(
        PossessionState state, Pie<FreeThrowReboundOutcome> pie, GameState game, IRng rng)
    {
        // 1. Roll the seven-way pie to a free-throw-rebound outcome.
        var outcome = pie.Roll(rng.NextUnitInterval());

        // 2. Route per slice — mixed Terminal/Continue (the Roll I shape).
        return outcome switch
        {
            // Defense secures the board. Ball switches teams on a LIVE board —
            // TERMINAL. A transition start to the defense, carrying the conservative
            // FT-rebound context so the resolver routes it to Roll J and Roll J picks
            // its tamer run-or-not pie. (Off an FT the made/missed shot gave everyone
            // time to get back, so the break is less likely to run — that tilt lives
            // in Roll J's FT pie, selected by this ticket.)
            FreeThrowReboundOutcome.DefensiveRebound =>
                new Terminal("DefensiveRebound", state,
                    PossessionConsequence.TransitionFreeThrowReboundTo(state.Defense)),

            // Offense secures its own missed FT. Same possession stays alive —
            // CONTINUE to Roll K, stamping the FreeThrow source so Roll K selects its
            // FT-specific pie. The legacy (Roll I) feeder stamps nothing → LiveBall.
            FreeThrowReboundOutcome.OffensiveRebound =>
                new Continue(ContinuationKind.ResolveOffensiveRebound, state)
                {
                    OffensiveReboundSource = OffensiveReboundSource.FreeThrow
                },

            // Loose-ball foul on the defense along the lane. Offense retains —
            // CONTINUE. Charge the defensive team foul and read the bonus, exactly as
            // Roll D / I / J / K — the fifth feeder into the shared fork.
            FreeThrowReboundOutcome.LooseBallFoulOnDefense =>
                ResolveFoulOnDefense(state, game),

            // Loose-ball foul on the offense. Ball switches teams — TERMINAL.
            // No foul charged (Roll C's OffensiveFoul precedent). Dead-ball inbound at
            // Roll A for the defense.
            FreeThrowReboundOutcome.LooseBallFoulOnOffense =>
                new Terminal("LooseBallFoulOnOffense", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            // Ball out of bounds last off the OFFENSE. Ball switches teams — TERMINAL.
            // Same routing as the offensive foul (defense's ball at Roll A), NO foul
            // charged — only the reason label differs.
            FreeThrowReboundOutcome.OutOfBoundsOffOffense =>
                new Terminal("OutOfBoundsOffOffense", state,
                    PossessionConsequence.DeadBallTo(state.Defense)),

            // Ball out of bounds last off the DEFENSE. Offense retains — CONTINUE to
            // the sideline-inbound node. NO charge, NO bonus fork: no foul means no
            // bonus question, so this is always a plain sideline inbound.
            FreeThrowReboundOutcome.OutOfBoundsOffDefense =>
                new Continue(ContinuationKind.ResolveSidelineInbound, state),

            // Tie-up on the loose ball. -> shared jump-ball node (consults the arrow).
            FreeThrowReboundOutcome.JumpBall =>
                new Continue(ContinuationKind.ResolveJumpBall, state),

            _ => throw new InvalidOperationException($"Unhandled free-throw rebound outcome '{outcome}'.")
        };
    }

    /// <summary>
    /// The loose-ball-foul-on-defense arm: charge the defensive team foul via
    /// <see cref="FoulTracker"/>, read the resulting bonus, and fork to the
    /// sideline-inbound (below bonus) or free-throw (in bonus) continuation. Copied
    /// verbatim from Roll I / J / K's charge-and-read (itself from Roll D) — the FIFTH
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
        // type rides as functional payload, exactly as Roll D / I / J / K.
        return new Continue(ContinuationKind.ResolveFreeThrows, state)
        {
            Bonus = bonus
        };
    }
}
