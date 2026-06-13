namespace Charm.Engine;

/// <summary>
/// Roll H — Make/Miss. The beat right after a shot's location is stamped (Roll G's
/// IntoShotResolution): resolves the located shot into one of seven outcomes —
/// made, made-and-fouled (and-1), miss, miss-and-fouled, miss-out-of-bounds-lost,
/// miss-out-of-bounds-retained, or blocked — stamps that outcome onto the
/// possession, then either ends the possession (Terminal) or hands off to the
/// right downstream node.
///
/// Structurally a WELD of three earlier rolls:
///   - Roll F's GATE skeleton: switch on the rolled outcome, route each arm.
///   - Roll A's MIXED arms: some outcomes are Terminal (the possession ends), some
///     are Continue — unlike Roll F (all-continue) or Roll C (all-terminal).
///   - Roll G's STAMP-A-FACT: it writes its own outcome onto PossessionState
///     (<c>state with { Result = outcome }</c>) — the THIRD durable per-possession
///     fact, after Roll E's SelectedSlot and Roll G's ShotType. The two terminals
///     carry the stamped state too, so the future Governor reads Result + ShotType
///     off the terminal.
///
/// Like Roll F and Roll G, it reads NOTHING off GameState and its signature is
/// (state, pie, rng). It reads no stamps either: it does NOT inspect SelectedSlot
/// or ShotType. Those ride forward on the carried state untouched. (The
/// "H reads both stamps" idea is forward-looking — that belongs to H's deferred
/// GENERATOR, which will read the shooter-vs-defender matchup to tilt make/miss.
/// This skeleton reads only its pie. As of Session 13 the GENERATOR does read one
/// stamp — the shot ZONE — but only to size the block slice per zone; the roll
/// itself still reads nothing but the finished pie handed to it.)
///
/// What it does NOT do (the #1 scope firewall): it computes NO points, charges NO
/// fouls, tracks NO stats, and resolves NO free throws or rebounds. The 2/3 point
/// value, the team-foul charge, and the 1/2/3 free-throw count are all DOWNSTREAM
/// derivations from (Result, ShotType), added when the scoring / free-throw /
/// Governor layers exist. Because of that, H takes no GameState. And it does NOT
/// tilt its own odds: the entire attribute/modifier model (the other-four gravity
/// term, the shooter-vs-defender matchup, the skill/athleticism gates, the bounded
/// logistic mapping) is the DEFERRED generator, delivered later as a smarter
/// generator handing H a non-flat pie over the same enum. Roll H itself never
/// changes when that lands.
///
/// Follows the uniform roll contract: receives state + a finished pie, rolls
/// against it, returns one typed result, names no successor (the resolver maps the
/// kind).
/// </summary>
public static class RollH
{
    public static RollResult Execute(PossessionState state, Pie<ShotResult> pie, IRng rng)
    {
        // 1. Roll the six-way pie to a make/miss outcome.
        var outcome = pie.Roll(rng.NextUnitInterval());

        // 2. Stamp the outcome onto the possession as a per-possession fact — the
        //    THIRD, after SelectedSlot (Roll E) and ShotType (Roll G). Immutable
        //    record, so this is a `with`-expression producing a NEW state, exactly
        //    how Roll G stamped ShotType. Every arm below carries this state, so no
        //    exit can leave Result null.
        var stamped = state with { Result = outcome };

        // 3. Route per slice — Roll A's mixed Terminal/Continue pattern. Terminals
        //    carry the stamped state so the future Governor reads Result + ShotType
        //    off them. Elapsed time defers to the future time roll on both terminals
        //    (ElapsedSeconds stays null — neither is an invariant-time outcome).
        return outcome switch
        {
            // Make -> TERMINAL. The basket counts; its 2/3 value is a downstream
            // derivation from Result + ShotType, not computed here. Consequence:
            // ball to the other team on a DEAD-BALL restart — a made basket is
            // inbounded under the hoop (the ball is out of bounds with an inbounding
            // player), so it is a dead ball, not a live push.
            ShotResult.Made =>
                new Terminal("Made", stamped,
                    PossessionConsequence.DeadBallTo(stamped.Defense)),

            // And-1 -> CONTINUE to the shooting-free-throw node (stub). The basket
            // and the single free throw are downstream derivations; H records
            // neither.
            ShotResult.MadeAndFouled =>
                new Continue(ContinuationKind.ResolveShootingFreeThrows, stamped),

            // Miss -> CONTINUE to the rebound node (stub). The common case. An
            // offensive board keeps the SAME possession — the rebound roll's job.
            ShotResult.Miss =>
                new Continue(ContinuationKind.ResolveRebound, stamped),

            // Shooting foul on a miss -> CONTINUE to the shooting-free-throw node
            // (stub). The free-throw count (2, or 3 on a fouled three) is derived
            // downstream from Result + ShotType.
            ShotResult.MissFouled =>
                new Continue(ContinuationKind.ResolveShootingFreeThrows, stamped),

            // Miss sails OOB off the offense -> TERMINAL. Defense's ball.
            // Consequence: ball to the other team, dead-ball restart (sideline
            // inbound for the defense).
            ShotResult.MissOutOfBoundsLost =>
                new Terminal("MissOutOfBoundsLost", stamped,
                    PossessionConsequence.DeadBallTo(stamped.Defense)),

            // Miss deflects OOB off the defender -> CONTINUE to the sideline-inbound
            // node (stub). Offense keeps it, inbounds from the side.
            ShotResult.MissOutOfBoundsRetained =>
                new Continue(ContinuationKind.ResolveSidelineInbound, stamped),

            // Blocked -> CONTINUE into ROLL I, the rebound / loose-ball resolver,
            // stamping ReboundSource.Block so Roll I's generator selects the
            // block-recovery pie. A blocked shot IS a loose-ball scramble — the same
            // battle a missed-shot rebound is — so it reuses the existing
            // ResolveRebound edge (the #1 IntoPlayerSelection precedent: one edge, a
            // payload selects the pie) rather than its own node. The block weight is
            // still per-zone (carved off the top of H's pie by the generator), but the
            // ROUTING is zone-blind: every block enters Roll I. (Retires ResolveBlock /
            // BlockRecoveryStub from the live chain — kept in the corner, not deleted.)
            ShotResult.Blocked =>
                new Continue(ContinuationKind.ResolveRebound, stamped)
                {
                    ReboundSource = ReboundSource.Block
                },

            _ => throw new InvalidOperationException($"Unhandled shot result '{outcome}'.")
        };
    }
}
