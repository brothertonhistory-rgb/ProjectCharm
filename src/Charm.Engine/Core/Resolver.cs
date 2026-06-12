namespace Charm.Engine;

/// <summary>What the resolver did with one result — for observability/harness.</summary>
/// <param name="PossessionEnded">True if the possession ended on a terminal; false
/// if it parked at a stub. Unchanged — every existing check reads this.</param>
/// <param name="Destination">The destination label ("END:{reason}" on a terminal,
/// "STUB:…" on a park). Unchanged — every existing check reads this.</param>
public readonly record struct RoutingOutcome(bool PossessionEnded, string Destination)
{
    /// <summary>
    /// The actual terminal the possession ENDED on — the object itself, carrying its
    /// <see cref="Terminal.State"/> and its <see cref="Terminal.Consequence"/> — so a
    /// caller (the Governor) can spawn the next possession without parsing the
    /// destination string. NULL when the possession PARKED at a stub (no terminal was
    /// reached); the Governor reads that null as "apply the default consequence."
    /// <para>Init-only with a null default, so every existing positional construction
    /// (<c>new RoutingOutcome(false, "STUB:…")</c>) and every existing read of
    /// <see cref="Destination"/> / <see cref="PossessionEnded"/> stays untouched —
    /// this is a pure append to the seam.</para>
    /// </summary>
    public Terminal? EndedOn { get; init; }
}

/// <summary>
/// The conductor. It walks the chain: route a ticket -> run that station ->
/// take its new ticket -> route again -> until a terminal ends the possession.
///
/// It owns all routing: a Terminal ends the possession; a Continue is mapped —
/// by its <see cref="ContinuationKind"/>, the only place that mapping lives —
/// to the next station. When a real station replaces a stub, only this mapping
/// changes; the rolls that emit tickets never reopen.
/// </summary>
public sealed class Resolver
{
    // Roll A entry: the resolver owns the TOP of the chain too, so a caller (the
    // Governor) can ask it to run a whole possession from a start state without
    // ever naming a roll itself. The generator + config produce Roll A's pie; the
    // resolver then walks the chain via the existing Route loop.
    private readonly StubPieGenerator _rollAGenerator;
    private readonly RollAConfig _rollAConfig;
    private readonly RollBStubPieGenerator _rollBGenerator;
    private readonly RollCStubPieGenerator _rollCGenerator;
    private readonly RollDStubPieGenerator _rollDGenerator;
    private readonly RollEStubPieGenerator _rollEGenerator;
    private readonly RollFStubPieGenerator _rollFGenerator;
    private readonly RollGStubPieGenerator _rollGGenerator;
    private readonly RollHStubPieGenerator _rollHGenerator;
    private readonly RollIStubPieGenerator _rollIGenerator;
    private readonly RollJStubPieGenerator _rollJGenerator;
    private readonly GameState _game;
    private readonly IRng _rng;
    private readonly IContinuationNode _resumeInbound;
    private readonly IContinuationNode _resolveFreeThrows;
    private readonly IContinuationNode _resolveBlock;
    private readonly IContinuationNode _offensiveRebound;
    private readonly IContinuationNode _resolveShootingFreeThrows;
    private readonly IContinuationNode _sidelineInbound;
    // Roll J's Push parks here — the future transition roll's holding pen.
    private readonly IContinuationNode _transition;

    public Resolver(
        StubPieGenerator rollAGenerator,
        RollAConfig rollAConfig,
        RollBStubPieGenerator rollBGenerator,
        RollCStubPieGenerator rollCGenerator,
        RollDStubPieGenerator rollDGenerator,
        RollEStubPieGenerator rollEGenerator,
        RollFStubPieGenerator rollFGenerator,
        RollGStubPieGenerator rollGGenerator,
        RollHStubPieGenerator rollHGenerator,
        RollIStubPieGenerator rollIGenerator,
        RollJStubPieGenerator rollJGenerator,
        GameState game,
        IRng rng,
        IContinuationNode resumeInbound,
        IContinuationNode resolveFreeThrows,
        IContinuationNode resolveBlock,
        IContinuationNode offensiveRebound,
        IContinuationNode resolveShootingFreeThrows,
        IContinuationNode sidelineInbound,
        IContinuationNode transition)
    {
        _rollAGenerator = rollAGenerator;
        _rollAConfig = rollAConfig;
        _rollBGenerator = rollBGenerator;
        _rollCGenerator = rollCGenerator;
        _rollDGenerator = rollDGenerator;
        _rollEGenerator = rollEGenerator;
        _rollFGenerator = rollFGenerator;
        _rollGGenerator = rollGGenerator;
        _rollHGenerator = rollHGenerator;
        _rollIGenerator = rollIGenerator;
        _rollJGenerator = rollJGenerator;
        _game = game;
        _rng = rng;
        _resumeInbound = resumeInbound;
        _resolveFreeThrows = resolveFreeThrows;
        _resolveBlock = resolveBlock;
        _offensiveRebound = offensiveRebound;
        _resolveShootingFreeThrows = resolveShootingFreeThrows;
        _sidelineInbound = sidelineInbound;
        _transition = transition;
    }

    /// <summary>
    /// Run ONE whole possession from its start <paramref name="start"/>: route the
    /// start state to its ENTRY node, execute that node (the top of the chain), then
    /// walk the rest via <see cref="Route"/>. The single entry the Governor calls — so
    /// the Governor drops a START STATE at the top of the chain and never names a roll.
    /// <para>Entry routing is a single localized switch on the start state, mirroring
    /// how <see cref="Route"/> switches on <see cref="ContinuationKind"/> — entry logic
    /// is not scattered. A start that began on a defensive rebound (a Transition entry
    /// carrying the <see cref="TransitionSource.Rebound"/> ticket) enters Roll J, the
    /// live transition-entry gate. Every other start — every dead-ball inbound, and
    /// (this session) every not-yet-wired steal, which carries no context ticket —
    /// enters Roll A, exactly as before. When the steal feeder lands and its terminals
    /// carry a Steal context, this same switch routes every Transition start to Roll J.</para>
    /// <para>Pressure is a flat 0.0 (the neutral baseline the batch harness uses): the
    /// Governor does not model defensive pressure this session.</para>
    /// </summary>
    public RoutingOutcome RunPossession(PossessionState start)
    {
        RollResult result;

        if (start is { Entry: EntryType.Transition,
                       TransitionContext: { Source: TransitionSource.Rebound } ctx })
        {
            // Rebound-born transition: Roll J owns the top of the chain. The arriving
            // ticket selects Roll J's run-or-not pie. Roll J takes _game because its
            // DefensiveFoul arm charges a team foul (the Roll D / Roll I shape).
            var pieJ = _rollJGenerator.Generate(ctx);
            result = RollJ.Execute(start, pieJ, _game, _rng);
        }
        else
        {
            // Legacy entry: Roll A (the generator + config produce its pie).
            var pieA = _rollAGenerator.Generate(start, pressure: 0.0);
            result = RollA.Execute(start, pieA, _rng, _rollAConfig);
        }

        return Route(result);
    }

    /// <summary>Walk the chain from <paramref name="result"/> until a terminal
    /// ends the possession. Returns the final routing outcome.</summary>
    public RoutingOutcome Route(RollResult result)
    {
        while (true)
        {
            switch (result)
            {
                case Terminal t:
                    return new RoutingOutcome(PossessionEnded: true, Destination: $"END:{t.Reason}")
                        { EndedOn = t };

                case Continue c:
                    switch (c.Next)
                    {
                        // Roll A's clean entry -> execute Roll B, loop.
                        case ContinuationKind.IntoHalfcourtSet:
                            var pieB = _rollBGenerator.Generate(c.State, physicality: 0.0);
                            result = RollB.Execute(c.State, pieB, _rng);
                            continue;

                        // Turnover (from any feeder: Roll A, Roll B, Roll F) ->
                        // execute Roll C, loop. Roll C always returns a Terminal,
                        // so the loop's Terminal case ends the possession on the
                        // next pass. Roll C integrates exactly like Roll B
                        // (execute + feed result back), not like a stub.
                        case ContinuationKind.ResolveTurnoverType:
                            // Select Roll C's pie by the turnover context the ticket
                            // carries. A null stamp — every legacy feeder (Roll A, B,
                            // F) stamps nothing — reads as Halfcourt, so the legacy
                            // pie is byte-for-byte unchanged. Roll J's Turnover arm
                            // stamps Transition for its outlet/push pie.
                            var pieC = _rollCGenerator.Generate(
                                c.State,
                                pressure: 0.0,
                                context: c.TurnoverContext ?? TurnoverContext.Halfcourt);
                            result = RollC.Execute(c.State, pieC, _rng);
                            continue;

                        // Roll B's proceed -> execute Roll E (player selection),
                        // loop. Roll E returns a CONTINUE (IntoPlayerAction)
                        // carrying the selected slot stamped on its state — so
                        // feeding it back re-enters this switch and lands on the
                        // IntoPlayerAction case below (now Roll F). Roll E reaches
                        // GameState to name a real slot on the offense's lineup.
                        case ContinuationKind.IntoPlayerSelection:
                            var pieE = _rollEGenerator.Generate(c.State);
                            result = RollE.Execute(c.State, pieE, _game, _rng);
                            continue;

                        // Roll E's selection -> execute Roll F (player action),
                        // loop. Roll F is a flat gate: it returns a CONTINUE
                        // (IntoShotType / ResolveTurnoverType / ResolveFoulType /
                        // ResolveJumpBall), never a terminal of its own, so feeding
                        // it back re-enters this switch and lands on the matching
                        // case. Roll F reads nothing off GameState and stamps
                        // nothing, so it takes only (state, pie, rng) — like Roll B,
                        // not Roll D/E. This is the "many feeders, one node" payoff:
                        // Roll F becomes a third feeder into C and D (and a feeder
                        // into the jump-ball node) at once. (Block left Roll F in
                        // Session 13 — it now lives in Roll H, zone-weighted.)
                        case ContinuationKind.IntoPlayerAction:
                            var pieF = _rollFGenerator.Generate(c.State);
                            result = RollF.Execute(c.State, pieF, _rng);
                            continue;

                        // Foul (from any feeder: Roll A entry, Roll B halfcourt,
                        // Roll F player action) -> execute Roll D, loop. Roll D
                        // returns a CONTINUE (ResumeInbound or ResolveFreeThrows),
                        // not a terminal — so feeding it back re-enters this switch
                        // and lands on the matching stub below. Roll D mutates
                        // GameState (it charges the team foul), hence it takes _game.
                        case ContinuationKind.ResolveFoulType:
                            var pieD = _rollDGenerator.Generate(c.State);
                            result = RollD.Execute(c.State, pieD, _game, _rng);
                            continue;

                        // Roll D, opponent not in bonus -> offense keeps the ball
                        // and inbounds (stub). Chain ends here for now.
                        case ContinuationKind.ResumeInbound:
                            return new RoutingOutcome(false, _resumeInbound.Receive(c));

                        // Roll D, opponent in bonus -> free-throw node (stub). The
                        // Bonus payload on c is the FT node's input. Chain ends
                        // here for now.
                        case ContinuationKind.ResolveFreeThrows:
                            return new RoutingOutcome(false, _resolveFreeThrows.Receive(c));

                        // Blocked shot (from Roll H) -> block-recovery node (stub).
                        // A live-ball event with its own future fan-out. The block
                        // weight is sized per zone upstream in Roll H's generator,
                        // but the routing is zone-blind: every block lands here.
                        // Reuses the ResolveBlock kind Roll F used to emit — Session
                        // 13 moved the feed point from F to H, leaving this edge
                        // untouched. Chain ends here for now.
                        case ContinuationKind.ResolveBlock:
                            return new RoutingOutcome(false, _resolveBlock.Receive(c));

                        // Roll F, clean attempt got off -> execute Roll G (shot
                        // location), loop. Roll G is structurally Roll E: it stamps
                        // a ShotType onto its state and returns a CONTINUE
                        // (IntoShotResolution) for all five zones — so feeding it
                        // back re-enters this switch and lands on the
                        // IntoShotResolution case below. Roll G reads nothing off
                        // GameState (a zone is just an enum value), so it takes only
                        // (state, pie, rng) — like Roll F, not Roll E.
                        case ContinuationKind.IntoShotType:
                            var pieG = _rollGGenerator.Generate(c.State);
                            result = RollG.Execute(c.State, pieG, _rng);
                            continue;

                        // Roll G's stamped shot -> execute Roll H (make/miss), loop.
                        // Roll H is a GATE with mixed ends: it stamps a ShotResult
                        // onto its state and returns EITHER a Terminal (Made,
                        // MissOutOfBoundsLost — the loop ends it on the next pass)
                        // OR a CONTINUE (ResolveShootingFreeThrows / ResolveRebound /
                        // ResolveSidelineInbound / ResolveBlock) that re-enters this
                        // switch and lands on the matching stub below. Roll H reads
                        // nothing off GameState and only its pie, so it takes
                        // (state, pie, rng) — like Roll F and Roll G. (Its GENERATOR
                        // reads the stamped zone to size the per-zone block slice,
                        // but the roll itself does not.)
                        case ContinuationKind.IntoShotResolution:
                            var pieH = _rollHGenerator.Generate(c.State);
                            result = RollH.Execute(c.State, pieH, _rng);
                            continue;

                        // Roll H, missed shot (live) -> execute Roll I (rebound
                        // resolution), loop. Roll I is a GATE with mixed ends: it
                        // returns EITHER a Terminal (DefensiveRebound,
                        // LooseBallFoulOnOffense — possession ends, ball switches
                        // teams) OR a Continue (ResolveOffensiveRebound /
                        // ResolveSidelineInbound / ResolveFreeThrows) that
                        // re-enters this switch and lands on the matching stub
                        // below. Roll I mutates GameState (it charges the
                        // defensive team foul on its LooseBallFoulOnDefense arm),
                        // hence it takes _game — the same shape as Roll D.
                        // ReboundStub is retired; this edge now executes Roll I.
                        case ContinuationKind.ResolveRebound:
                            var pieI = _rollIGenerator.Generate();
                            result = RollI.Execute(c.State, pieI, _game, _rng);
                            continue;

                        // Roll I, offense secures the offensive board -> offensive-
                        // rebound node (stub). Same possession stays alive. The real
                        // offensive-rebound roll (its own odds, loop back to
                        // halfcourt → player selection) is a later session. Chain
                        // ends here for now.
                        case ContinuationKind.ResolveOffensiveRebound:
                            return new RoutingOutcome(false, _offensiveRebound.Receive(c));

                        // Roll H, shooting foul (and-1 or fouled miss) -> shooting-
                        // free-throw node (stub), SEPARATE from Roll D's bonus FT
                        // path. The FT count is a downstream derivation from
                        // (Result, ShotType). Chain ends here for now.
                        case ContinuationKind.ResolveShootingFreeThrows:
                            return new RoutingOutcome(false, _resolveShootingFreeThrows.Receive(c));

                        // Roll H, miss deflected OOB off the defender -> sideline-
                        // inbound node (stub); offense retains and inbounds. Chain
                        // ends here for now.
                        case ContinuationKind.ResolveSidelineInbound:
                            return new RoutingOutcome(false, _sidelineInbound.Receive(c));

                        // Jump ball (from any feeder: Roll A, Roll B, Roll F) ->
                        // resolve against the possession arrow, then END the
                        // possession. A held ball ends the current possession; the
                        // awarded team's ensuing possession is a NEW possession
                        // (future work), not a continuation of this one. Mutates
                        // the arrow as a side effect (sets it on the opening tip,
                        // flips it otherwise).
                        case ContinuationKind.ResolveJumpBall:
                            var award = JumpBall.Resolve(_game, _rng);
                            var reason = award.WasTipContest
                                ? $"JumpBallTip:{award.AwardedTo}"
                                : $"JumpBallArrow:{award.AwardedTo}";
                            // Consequence: the AWARDED team gets the ball next (NOT
                            // necessarily the current defense — this is the one
                            // terminal whose next offense is set by the arrow/tip,
                            // not by "the other team"), on a dead-ball restart.
                            result = new Terminal(reason, c.State,
                                PossessionConsequence.DeadBallTo(award.AwardedTo));
                            continue;

                        // Roll J's Push -> the parked transition node (stub): the
                        // possession decided to RUN. Where the future transition roll
                        // (what the fast break PRODUCES — numbers, leak-outs, shot mix)
                        // will land. An Into* hand-off that parks for now, like the
                        // other stub edges. Chain ends here for this session.
                        case ContinuationKind.IntoTransition:
                            return new RoutingOutcome(false, _transition.Receive(c));

                        default:
                            throw new InvalidOperationException($"No route for continuation '{c.Next}'.");
                    }

                default:
                    throw new InvalidOperationException($"Unknown result type '{result.GetType().Name}'.");
            }
        }
    }
}
