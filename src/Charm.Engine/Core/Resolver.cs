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

    /// <summary>
    /// How many PUTBACK attempts this possession's walk made before it ended or
    /// parked — the re-entrant-loop depth counter (PutBack → Roll H → miss → Roll I →
    /// OffensiveRebound → PutBack …). Zero on the overwhelming majority of
    /// possessions. The harness reads this to PROVE the nested putback↔rebound loop
    /// converges (a decaying tail and a bounded max). Init-only with a 0 default, so
    /// every existing construction is untouched — a pure append, like <see cref="EndedOn"/>.
    /// </summary>
    public int PutbackAttempts { get; init; }

    /// <summary>
    /// How many times Roll L was spun resolving this possession's trip to the line —
    /// the FT-loop spin count, an observability counter exactly parallel to
    /// <see cref="PutbackAttempts"/>. Zero on the overwhelming majority of possessions
    /// (no foul trip). And-1 = 1, fouled two / double bonus = 2, fouled three = 3,
    /// 1-and-1 = 1 (front miss) or 2 (front make). The harness reads this to PROVE the
    /// shot count derived at the FT entry edge is correct per trip type and that the
    /// hard ≤ 3 bound holds observably (not only via the in-engine assert). Init-only
    /// with a 0 default, so every existing construction is untouched — a pure append,
    /// like <see cref="EndedOn"/> and <see cref="PutbackAttempts"/>.
    /// </summary>
    public int FreeThrowSpins { get; init; }
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
    private readonly RollKStubPieGenerator _rollKGenerator;
    private readonly RollLStubPieGenerator _rollLGenerator;
    private readonly GameState _game;
    private readonly IRng _rng;
    private readonly IContinuationNode _resumeInbound;
    private readonly IContinuationNode _resolveBlock;
    private readonly IContinuationNode _sidelineInbound;
    // Roll L's FT-sequence driver parks a missed FINAL free throw here — the future
    // FT-rebound roll's holding pen.
    private readonly IContinuationNode _ftRebound;
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
        RollKStubPieGenerator rollKGenerator,
        RollLStubPieGenerator rollLGenerator,
        GameState game,
        IRng rng,
        IContinuationNode resumeInbound,
        IContinuationNode resolveBlock,
        IContinuationNode sidelineInbound,
        IContinuationNode ftRebound,
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
        _rollKGenerator = rollKGenerator;
        _rollLGenerator = rollLGenerator;
        _game = game;
        _rng = rng;
        _resumeInbound = resumeInbound;
        _resolveBlock = resolveBlock;
        _sidelineInbound = sidelineInbound;
        _ftRebound = ftRebound;
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
        // Re-entrant-loop instrumentation (Session 17). PutBack and ResetOffense keep
        // the same possession alive INSIDE this walk, so a single Route call can now
        // cycle: PutBack → Roll H → miss → Roll I → OffensiveRebound → PutBack … and
        // reset → Roll E → … . `putbackAttempts` counts the putback shots taken (the
        // depth the convergence check watches). `iterations` is a LOUD safety guard:
        // a converging possession bleeds out in a handful of cycles, so the ceiling is
        // far above any real walk; reaching it means a possession is NOT converging,
        // which is a real bug — it throws rather than silently breaking, and the
        // harness asserts it is never hit.
        var putbackAttempts = 0;
        var freeThrowSpins = 0;
        var iterations = 0;
        const int IterationCeiling = 10_000;

        while (true)
        {
            if (++iterations > IterationCeiling)
                throw new InvalidOperationException(
                    $"Resolver walk exceeded {IterationCeiling} iterations — a possession is not " +
                    $"converging (putback attempts so far: {putbackAttempts}). This is a real " +
                    "non-convergence bug, not something to swallow.");

            switch (result)
            {
                case Terminal t:
                    return new RoutingOutcome(PossessionEnded: true, Destination: $"END:{t.Reason}")
                        { EndedOn = t, PutbackAttempts = putbackAttempts, FreeThrowSpins = freeThrowSpins };

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
                            return new RoutingOutcome(false, _resumeInbound.Receive(c)) { PutbackAttempts = putbackAttempts };

                        // Bonus fork (Roll D/I/J/K), opponent in bonus -> the Roll L
                        // FT loop. The Bonus token IS the shot count: Double is a flat
                        // two, OneAndOne is a conditional two (miss the front and it is
                        // the last shot, the second forfeited). The driver loops Roll L
                        // and hands back a Terminal (last make -> opponent's ball) or a
                        // Continue(ResolveFTRebound) (last miss -> live board); feed it
                        // back into this switch.
                        case ContinuationKind.ResolveFreeThrows:
                            result = DriveFreeThrows(
                                c.State,
                                shots: c.Bonus == BonusType.Double ? 2 : 1,
                                oneAndOne: c.Bonus == BonusType.OneAndOne,
                                out var bonusFtSpins);
                            freeThrowSpins += bonusFtSpins;
                            continue;

                        // Blocked shot (from Roll H) -> block-recovery node (stub).
                        // A live-ball event with its own future fan-out. The block
                        // weight is sized per zone upstream in Roll H's generator,
                        // but the routing is zone-blind: every block lands here.
                        // Reuses the ResolveBlock kind Roll F used to emit — Session
                        // 13 moved the feed point from F to H, leaving this edge
                        // untouched. Chain ends here for now.
                        case ContinuationKind.ResolveBlock:
                            return new RoutingOutcome(false, _resolveBlock.Receive(c)) { PutbackAttempts = putbackAttempts };

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
                            // A putback ticket (Roll K's PutBack arm) selects Roll H's
                            // distinct putback pie and counts toward this possession's
                            // putback depth — the re-entrant loop's accumulation.
                            if (c.Putback) putbackAttempts++;
                            var pieH = _rollHGenerator.Generate(c.State, c.Putback);
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

                        // Roll I, offense secures the offensive board -> execute
                        // Roll K (offensive-rebound resolution), loop. Roll K is a
                        // GATE with mixed ends (the Roll I shape): TERMINALS
                        // (OffensiveFoul / DeadBallTurnover / LiveBallTurnover — the
                        // ball flips) and CONTINUES (PutBack → Roll H with a putback
                        // ticket + Rim forced; ResetOffense → Roll E on a blank slate;
                        // DefensiveFoul → the charge-and-fork; JumpBall → the arrow
                        // node). PutBack and ResetOffense keep the SAME possession
                        // alive — the loop lives in THIS walk, the Governor never sees
                        // it, the count never increments. Roll K mutates GameState (its
                        // DefensiveFoul arm charges the defensive team foul), hence it
                        // takes _game — the Roll D / I / J shape. OffensiveReboundStub
                        // is retired from the live chain; this edge now executes Roll K.
                        case ContinuationKind.ResolveOffensiveRebound:
                            var pieK = _rollKGenerator.Generate();
                            result = RollK.Execute(c.State, pieK, _game, _rng);
                            continue;

                        // Roll H, shooting foul (and-1 or fouled miss) -> the Roll L FT
                        // loop. The shot count is plain sequencing read off the stamped
                        // (Result, ShotType): and-1 = 1, fouled two = 2, fouled three =
                        // 3 — never a 1-and-1. The driver loops Roll L and hands back a
                        // Terminal (last make -> opponent's ball) or a
                        // Continue(ResolveFTRebound) (last miss -> live board); feed it
                        // back into this switch. A made and-1 basket already banked its
                        // points upstream; the single FT here only sets the consequence.
                        case ContinuationKind.ResolveShootingFreeThrows:
                            result = DriveFreeThrows(c.State, ShootingFoulShots(c.State), oneAndOne: false, out var shootingFtSpins);
                            freeThrowSpins += shootingFtSpins;
                            continue;

                        // Roll H, miss deflected OOB off the defender -> sideline-
                        // inbound node (stub); offense retains and inbounds. Chain
                        // ends here for now.
                        case ContinuationKind.ResolveSidelineInbound:
                            return new RoutingOutcome(false, _sidelineInbound.Receive(c)) { PutbackAttempts = putbackAttempts };

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
                            return new RoutingOutcome(false, _transition.Receive(c)) { PutbackAttempts = putbackAttempts };

                        // Roll L's FT loop, last shot missed (live ball) -> the parked
                        // FT-rebound node (stub). The future FT-rebound roll's holding
                        // pen — the offensive/defensive board off a missed FT plus any
                        // foul on that rebound. Chain ends here for this session.
                        case ContinuationKind.ResolveFTRebound:
                            return new RoutingOutcome(false, _ftRebound.Receive(c)) { PutbackAttempts = putbackAttempts, FreeThrowSpins = freeThrowSpins };

                        default:
                            throw new InvalidOperationException($"No route for continuation '{c.Next}'.");
                    }

                default:
                    throw new InvalidOperationException($"Unknown result type '{result.GetType().Name}'.");
            }
        }
    }

    /// <summary>
    /// The FT-sequence driver — the conductor-owned loop arithmetic for a trip to the
    /// line. Both FT entry edges (Roll H's shooting fouls and the Roll D/I/J/K bonus
    /// fork) converge here; they differ ONLY in the shot count they hand it. Roll L
    /// itself never sees the sequence: this method spins it once per attempt and
    /// applies the uniform dead-intermediate / live-last routing.
    /// <para>Per spin: an INTERMEDIATE shot (any shot before the last in a fixed 2- or
    /// 3-shot set) is DEAD regardless of make or miss — it just retriggers the next
    /// attempt; the ball never goes live between shots. The LAST shot evaluates
    /// live/dead via <see cref="LastShot"/>: make ends the possession (opponent's
    /// ball, like a made field goal), miss leaves the ball live (-> FT-rebound).</para>
    /// <para>A 1-and-1 is the one conditional: the FRONT end is conditionally the last
    /// shot — miss it and it IS the last shot (the second is forfeited), make it and a
    /// now-last second shot follows the normal rule. An and-1 is a fixed 1-shot set,
    /// so its single shot is the last shot.</para>
    /// <para>The loop is HARD-BOUNDED (≤ 3 spins; 1-and-1 ≤ 2), so it needs no
    /// 10,000-iteration guard like the main walk — but it asserts the spin count never
    /// exceeds 3, surfacing a shot-count derivation bug loud. No score is wired here: a
    /// made FT is 1 point, a downstream derivation the future points pass reads off the
    /// make/miss fact, exactly as a field goal's 2/3 is.</para>
    /// </summary>
    private RollResult DriveFreeThrows(PossessionState state, int shots, bool oneAndOne, out int spinCount)
    {
        var pie = _rollLGenerator.Generate();
        var spins = 0;

        // Spin Roll L once, count it, and assert the hard bound. A trip to the line is
        // at most a fouled three (3 shots); more than 3 spins is a derivation bug.
        FreeThrowOutcome Spin()
        {
            var outcome = RollL.Execute(pie, _rng);
            if (++spins > 3)
                throw new InvalidOperationException(
                    $"Free-throw sequence spun {spins} times — exceeds the hard bound of 3. " +
                    "A trip to the line is at most a fouled three; this is a shot-count " +
                    "derivation bug.");
            return outcome;
        }

        RollResult result;
        if (oneAndOne)
        {
            // Front end is conditionally last: a miss forfeits the second and is the
            // last shot (live -> FT-rebound); a make brings a now-last second shot.
            result = Spin() == FreeThrowOutcome.Miss
                ? LastShot(state, FreeThrowOutcome.Miss)
                : LastShot(state, Spin());
        }
        else
        {
            // Fixed 1-, 2-, or 3-shot set: every shot before the last is a dead
            // intermediate that just retriggers; only the last evaluates live/dead.
            var last = FreeThrowOutcome.Make;
            for (var i = 1; i <= shots; i++)
                last = Spin();
            result = LastShot(state, last);
        }

        spinCount = spins;
        return result;
    }

    /// <summary>The uniform last-shot rule: a made final free throw ENDS the possession
    /// (opponent inbounds and starts at Roll A — the same dead-ball consequence as a
    /// made field goal); a missed final free throw leaves the ball LIVE and routes to
    /// the FT-rebound node.</summary>
    private static RollResult LastShot(PossessionState state, FreeThrowOutcome outcome) =>
        outcome == FreeThrowOutcome.Make
            ? new Terminal("FreeThrowsMade", state, PossessionConsequence.DeadBallTo(state.Defense))
            : new Continue(ContinuationKind.ResolveFTRebound, state);

    /// <summary>Derive the shot count for a SHOOTING foul from the stamped facts —
    /// plain sequencing the conductor reads at the entry edge, never a stamp Roll L
    /// sees. And-1 (a made-and-fouled basket) = 1; a fouled miss = 2, or 3 if the
    /// fouled shot was a three. Never a 1-and-1 (that is bonus-only).</summary>
    private static int ShootingFoulShots(PossessionState state) => state switch
    {
        { Result: ShotResult.MadeAndFouled } => 1,
        { Result: ShotResult.MissFouled, ShotType: ShotLocation.Three } => 3,
        { Result: ShotResult.MissFouled } => 2,
        _ => throw new InvalidOperationException(
            $"ResolveShootingFreeThrows reached with a non-shooting-foul result " +
            $"'{state.Result}' (zone '{state.ShotType}').")
    };
}
