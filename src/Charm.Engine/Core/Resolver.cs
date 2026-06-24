namespace Charm.Engine;


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
    private readonly IRollAPieGenerator _rollAGenerator;
    private readonly RollAConfig _rollAConfig;
    private readonly IRollBPieGenerator _rollBGenerator;
    private readonly RollCGenerator _rollCGenerator;
    private readonly RollCConfig _rollCConfig;
    private readonly RollDGenerator _rollDGenerator;
    private readonly IRollEGenerationProvider _rollEGenerator;
    private readonly AttentionGenerator _attentionGenerator;
    private readonly IRollFPieGenerator _rollFGenerator;
    private readonly IRollGGenerationProvider _rollGGenerator;
    private readonly IRollHPieGenerator _rollHGenerator;
    private readonly IRollIPieGenerator _rollIGenerator;
    private readonly IRollJPieGenerator _rollJGenerator;
    private readonly IRollKPieGenerator _rollKGenerator;
    private readonly IRollLPieGenerator _rollLGenerator;
    private readonly IRollMPieGenerator _rollMGenerator;
    private readonly RollOffensiveFoulGenerator _offensiveFoulGenerator;
    private readonly MatchupConfig _matchup;
    private readonly GameState _game;
    private readonly IRng _rng;

    public Resolver(
        IRollAPieGenerator rollAGenerator,
        RollAConfig rollAConfig,
        IRollBPieGenerator rollBGenerator,
        RollCGenerator rollCGenerator,
        RollCConfig rollCConfig,
        RollDGenerator rollDGenerator,
        IRollEGenerationProvider rollEGenerator,
        AttentionGenerator attentionGenerator,
        IRollFPieGenerator rollFGenerator,
        IRollGGenerationProvider rollGGenerator,
        IRollHPieGenerator rollHGenerator,
        IRollIPieGenerator rollIGenerator,
        IRollJPieGenerator rollJGenerator,
        IRollKPieGenerator rollKGenerator,
        IRollLPieGenerator rollLGenerator,
        IRollMPieGenerator rollMGenerator,
        RollOffensiveFoulGenerator offensiveFoulGenerator,
        MatchupConfig matchup,
        GameState game,
        IRng rng)
    {
        _rollAGenerator = rollAGenerator;
        _rollAConfig = rollAConfig;
        _rollBGenerator = rollBGenerator;
        _rollCGenerator = rollCGenerator;
        _rollCConfig = rollCConfig;
        _rollDGenerator = rollDGenerator;
        _rollEGenerator = rollEGenerator;
        _attentionGenerator = attentionGenerator;
        _rollFGenerator = rollFGenerator;
        _rollGGenerator = rollGGenerator;
        _rollHGenerator = rollHGenerator;
        _rollIGenerator = rollIGenerator;
        _rollJGenerator = rollJGenerator;
        _rollKGenerator = rollKGenerator;
        _rollLGenerator = rollLGenerator;
        _rollMGenerator = rollMGenerator;
        _offensiveFoulGenerator = offensiveFoulGenerator;
        _matchup = matchup;
        _game = game;
        _rng = rng;
    }

    /// <summary>
    /// Run ONE whole possession from its start <paramref name="start"/>: route the
    /// start state to its ENTRY node, execute that node (the top of the chain), then
    /// walk the rest via <see cref="Route"/>. The single entry the Governor calls — so
    /// the Governor drops a START STATE at the top of the chain and never names a roll.
    /// <para>Entry routing is a single localized switch on the start state, mirroring
    /// how <see cref="Route"/> switches on <see cref="ContinuationKind"/> — entry logic
    /// is not scattered. A start that began on a defensive rebound or a steal (a
    /// Transition entry carrying a <see cref="TransitionSource"/> ticket) enters Roll J,
    /// the live transition-entry gate; the ticket's source selects Roll J's pie. Every
    /// other start — every dead-ball inbound — enters Roll A, exactly as before. As of
    /// Contextification #3 every transition consequence carries a recognized source, so
    /// a Transition entry can never reach the legacy branch (it fails loud if one ever
    /// does — a wiring-bug tripwire).</para>
    /// <para>Pressure is a flat 0.0 (the neutral baseline the batch harness uses): the
    /// Governor does not model defensive pressure this session.</para>
    /// </summary>
    public RoutingOutcome RunPossession(PossessionState start)
    {
        RollResult result;

        if (start is { Entry: EntryType.Transition,
                       TransitionContext: { Source: TransitionSource.Rebound
                                                  or TransitionSource.FreeThrowRebound
                                                  or TransitionSource.Steal } ctx })
        {
            // Rebound- OR steal-born transition: Roll J owns the top of the chain. The
            // arriving ticket's Source selects Roll J's run-or-not pie — Rebound and
            // FreeThrowRebound pick the rebound pies, Steal picks the most run-happy pie.
            // Roll J takes _game because its DefensiveFoul arm charges a team foul (the
            // Roll D / Roll I shape).
            var pieJ = _rollJGenerator.Generate(ctx);
            result = RollJ.Execute(start, pieJ, _game, _rng);
        }
        else if (start.Entry == EntryType.BallAdvanced)
        {
            // The other team lost the ball dead in the backcourt — the new offense
            // starts already across and skips Roll A's bring-up entirely. Drop straight
            // into Roll B (halfcourt initiation). Backcourt-only violations and the
            // 10-second count are unreachable; the team still faces Roll B's normal
            // foul / turnover / jump-ball chances before getting a shot.
            var pieB = _rollBGenerator.Generate(start, physicality: 0.0);
            result = RollB.Execute(start, pieB, _rng);
        }
        else
        {
            // A Transition entry must ALWAYS carry a recognized source (every transition
            // consequence — TransitionReboundTo / TransitionFreeThrowReboundTo /
            // TransitionStealTo — stamps one), so it can never legitimately reach this
            // legacy branch. A null-context Transition is no longer produced by anything
            // (Contextification #3 retired the bare helper); if one ever shows up it is a
            // wiring bug, so fail LOUD here rather than silently halfcourt-routing it.
            if (start.Entry == EntryType.Transition)
                throw new InvalidOperationException(
                    "A Transition-entry possession reached the legacy (Roll A) branch without a " +
                    "recognized TransitionContext source. Every transition consequence must carry " +
                    "Rebound, FreeThrowRebound, or Steal — a null-context transition is a wiring bug.");

            // Legacy entry: Roll A (the generator + config produce its pie).
            // ── Per-possession press roll (Phase 15) ─────────────────────────
            // The defending team's frequency dial maps to a per-possession press
            // probability (pure helper on MatchupConfig — no math in the Resolver).
            // One RNG draw decides: pressed → Standard; not pressed → None.
            // The stamp is written to the state BEFORE Generate is called so the
            // generator reads a finished decision, never rolls itself.
            // (§2a: one new RNG draw per dead-ball possession — accounted for.)
            var probability = _matchup.PressProbabilityFor(start.Defense);
            var mode        = _rng.NextUnitInterval() < probability ? PressMode.Standard : PressMode.None;
            start           = start with { PressMode = mode };
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
        var points = 0;
        var shotClockPeriods = 1;
        var fga = 0;
        var fgm = 0;
        var threePa = 0;
        var threePm = 0;
        var shotResolutions = 0;
        var missFouled = 0;
        var fta = 0;
        var ftm = 0;
        var orbChances = 0;
        var orbWon = 0;
        var rimFga = 0;
        var rimFgm = 0;
        var shortFga = 0;
        var shortFgm = 0;
        var midFga = 0;
        var midFgm = 0;
        var longFga = 0;
        var longFgm = 0;
        var slot1Fga = 0;
        var slot2Fga = 0;
        var slot3Fga = 0;
        var slot4Fga = 0;
        var slot5Fga = 0;
        var slotUnattributedFga = 0;
        var slot1Fgm = 0;
        var slot2Fgm = 0;
        var slot3Fgm = 0;
        var slot4Fgm = 0;
        var slot5Fgm = 0;
        var slotUnattributedFgm = 0;
        var iterations = 0;
        const int IterationCeiling = 10_000;

        // Phase 23 per-slot accumulators (exact counters — no IRng)
        var threePaBySlot = new SlotGroup();
        var threePmBySlot = new SlotGroup();
        var ftaBySlot     = new SlotGroup();
        var ftmBySlot     = new SlotGroup();
        var blkCount      = 0;
        // Phase 36: per-slot block accumulator. Total == BlkCount on every possession.
        var blkBySlot     = new SlotGroup();
        // Phase 39: per-slot assist accumulator. Total <= FGM on every possession.
        var astBySlot     = new SlotGroup();
        int? turnoverOffSlot   = null;
        var turnoverWasLiveBall = false;
        int? stealerSlot        = null;
        int? defensiveRebounderSlot = null;
        // Phase 31: per-slot offensive-rebound accumulator (one entry per picker fire,
        // i.e. one per ResolveOffensiveRebound case hit). Total == OrbWon at possession end.
        var orbBySlot = new SlotGroup();
        // Phase 25: shooting-foul events (one per MadeAndFouled / MissFouled edge hit).
        // A possession with no shooting foul stays empty; a putback possession can carry two.
        var shootingFouls = new List<ShootingFoulEvent>();
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
                    // Stamp offensive-foul flavor at the single chokepoint where all
                    // three OffensiveFoul emitters (Roll C, Roll K, ResolveOffensiveFoul)
                    // converge. Theater only — never read for routing.
                    if (t.Reason == "OffensiveFoul")
                    {
                        var flavorPie = _offensiveFoulGenerator.Generate(t.State);
                        var flavor    = flavorPie.Roll(_rng.NextUnitInterval());
                        t = t with { Flavor = flavor };
                    }
                    // A clean made field goal banks its 2/3 here (the and-1 basket banks
                    // at the shooting-FT edge instead, since it is a Continue, not a
                    // terminal). ShotType is non-null on a Made terminal — Roll G stamped
                    // it upstream before Roll H could resolve a make.
                    if (t.Reason == "Made")
                        points += Scoring.FieldGoalPoints(t.State.ShotType!.Value);
                    // Phase 23: TO metadata — set only for Roll C turnover terminals.
                    // Phase 34: type-aware committer dispatch.
                    // Team violations → null (no individual credit; team turnover only).
                    // Interior violations + offensive foul → TurnoverInteriorPicker (post-weighted).
                    // Ball-handler violations → TurnoverCommitterPicker (Phase 33, unchanged).
                    if (t.Reason is "FiveSecondInbound" or "TenSecondBackcourt" or "ShotClockViolation")
                    {
                        // Team violation: the team committed this, not one player.
                        // TurnoverOffSlot stays null; no individual credit issued.
                        turnoverWasLiveBall = false;   // team violations are never live-ball steals
                    }
                    else if (t.Reason is "ThreeSecondViolation" or "OffensiveGoaltending" or "OffensiveFoul")
                    {
                        // Interior / post-skewed: post-weighted picker.
                        turnoverOffSlot = t.State.SelectedSlot?.Number
                            ?? TurnoverInteriorPicker.Pick(t.State, _game, _matchup, _rng).Number;
                        turnoverWasLiveBall = false;   // interior violations are never live-ball steals
                    }
                    else if (t.Reason is "BadPassDeadBall" or "BadPassIntercepted"
                                      or "LostBallDeadBall" or "LostBallLiveBall"
                                      or "Travel" or "DoubleDribble" or "Carry"
                                      or "FiveSecondCloselyGuarded" or "BackcourtViolation")
                    {
                        // Ball-handler violations: Phase 33 handling-weighted picker (unchanged).
                        turnoverOffSlot = t.State.SelectedSlot?.Number
                            ?? TurnoverCommitterPicker.Pick(t.State, _game, _matchup, _rng).Number;
                        turnoverWasLiveBall =
                            t.Reason is "BadPassIntercepted" or "LostBallLiveBall";
                        if (turnoverWasLiveBall)
                            stealerSlot = StealerPicker.Pick(t.State, _game, _matchup, _rng).Number;
                    }
                    // Phase 35: defensive-rebound attribution — stamp which defender got it.
                    if (t.Reason == "DefensiveRebound")
                        defensiveRebounderSlot = DefensiveRebounderPicker.Pick(t.State, _game, _matchup, _rng).Number;
                    return new RoutingOutcome(PossessionEnded: true, Destination: $"END:{t.Reason}")
                        { EndedOn = t, PutbackAttempts = putbackAttempts, FreeThrowSpins = freeThrowSpins, Points = points, ShotClockPeriods = shotClockPeriods,
                          Fga = fga, Fgm = fgm, ThreePa = threePa, ThreePm = threePm,
                          ShotResolutions = shotResolutions, MissFouled = missFouled,
                          Fta = fta, Ftm = ftm, OrbChances = orbChances, OrbWon = orbWon,
                          RimFga = rimFga, RimFgm = rimFgm, ShortFga = shortFga, ShortFgm = shortFgm,
                          MidFga = midFga, MidFgm = midFgm, LongFga = longFga, LongFgm = longFgm,
                          Slot1Fga = slot1Fga, Slot2Fga = slot2Fga, Slot3Fga = slot3Fga,
                          Slot4Fga = slot4Fga, Slot5Fga = slot5Fga,
                          SlotUnattributedFga = slotUnattributedFga,
                          Slot1Fgm = slot1Fgm, Slot2Fgm = slot2Fgm, Slot3Fgm = slot3Fgm,
                          Slot4Fgm = slot4Fgm, Slot5Fgm = slot5Fgm,
                          SlotUnattributedFgm = slotUnattributedFgm,
                          ThreePaBySlot  = threePaBySlot,
                          ThreePmBySlot  = threePmBySlot,
                          FtaBySlot      = ftaBySlot,
                          FtmBySlot      = ftmBySlot,
                          BlkCount       = blkCount,
                          BlkBySlot      = blkBySlot,
                          TurnoverOffSlot     = turnoverOffSlot,
                          TurnoverWasLiveBall = turnoverWasLiveBall,
                          ShootingFouls  = shootingFouls.ToArray(),
                          OrbBySlot      = orbBySlot,
                          StealerSlot    = stealerSlot,
                          DefensiveRebounderSlot = defensiveRebounderSlot,
                          AstBySlot      = astBySlot };

                case Continue c:
                    switch (c.Next)
                    {
                        // Roll A's clean entry -> execute Roll B, loop.
                        case ContinuationKind.IntoHalfcourtSet:
                            if (c.State.PressMode == PressMode.Standard)
                            {
                                // Phase 16: press beaten — fast break fires. Consume the press stamp so
                                // later re-inbounds in the same possession cannot re-trigger this gate.
                                var breakState = c.State with { FastBreak = true, PressMode = PressMode.None };
                                var breakGenE  = _rollEGenerator.GenerateWithPressure(breakState);
                                var breakAttn  = _attentionGenerator.Generate(breakState, breakGenE.FinalShares);
                                result = RollE.Execute(breakState, breakGenE.Pie, breakGenE.Pressures,
                                    breakAttn.AttentionShares, breakAttn.TeamBaseOpenness,
                                    breakAttn.TeamGravityLevel, breakAttn.TeamSpacingLevel,
                                    breakAttn.TeamConversionQuality, _game, _rng);
                                continue;
                            }
                            // Normal halfcourt path.
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
                            // carries. Roll A now stamps EntryBackcourt (backcourt
                            // bring-up) or Halfcourt (frontcourt re-inbound); Roll J's
                            // Turnover arm stamps Transition; Roll B and Roll F stamp
                            // nothing, and a null reads as Halfcourt — so their pie is
                            // byte-for-byte unchanged. The RollCConfig is passed (it was
                            // not before #6): the now-LIVE violation arms read their
                            // invariant elapsed through it, and would FAIL LOUD without it.
                            // The pressure parameter has been retired (Phase 37): pressure
                            // changes turnover RATE (Roll A/B/F), not turnover TYPE.
                            var pieC = _rollCGenerator.Generate(
                                c.State,
                                context: c.TurnoverContext ?? TurnoverContext.Halfcourt);
                            result = RollC.Execute(c.State, pieC, _rng, _rollCConfig);
                            continue;

                        // Roll B's proceed -> execute Roll E (player selection),
                        // loop. Roll E returns a CONTINUE (IntoPlayerAction)
                        // carrying the selected slot stamped on its state — so
                        // feeding it back re-enters this switch and lands on the
                        // IntoPlayerAction case below (now Roll F). Roll E reaches
                        // GameState to name a real slot on the offense's lineup.
                        // FEEDERS into this edge: Roll B Proceed, Roll J Settle (both
                        // halfcourt), Roll J Push (FastBreak=true — Roll E's generator
                        // reads it and draws the transition selection pie), and Roll K
                        // ResetOffense (FastBreak cleared — a fresh halfcourt play). The
                        // generator selects the pie from the carried state; the edge is
                        // marker-blind, exactly the Roll C/K ticket pattern.
                        case ContinuationKind.IntoPlayerSelection:
                            var genE  = _rollEGenerator.GenerateWithPressure(c.State);
                            var attn  = _attentionGenerator.Generate(c.State, genE.FinalShares);
                            // Phase 27 Session 2 — selection tilt (halfcourt only).
                            // Bends the usage pie by the (usage intent − defensive attention) gap.
                            // FastBreak: not reached here (handled at IntoHalfcourtSet above).
                            // Pre-tilt pressures passed unchanged — tilt changes WHICH slot is rolled,
                            // not the pressure each slot carries (one-pass, no feedback loop).
                            var tiltedPieE = _rollEGenerator.BendByAttention(genE, attn.AttentionShares, _game, _matchup, c.State);
                            result = RollE.Execute(c.State, tiltedPieE, genE.Pressures,
                                attn.AttentionShares, attn.TeamBaseOpenness,
                                attn.TeamGravityLevel, attn.TeamSpacingLevel,
                                attn.TeamConversionQuality, _game, _rng);
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

                        // Offensive foul on the entry (Roll A) -> a dead-ball loss to
                        // the other team. Deterministic: a player-control foul yields no
                        // free throws and no bonus credit, so it maps straight to the
                        // same OffensiveFoul terminal Roll C names for an offensive foul
                        // (ball to the defense, dead-ball restart) — no pie, no Roll D
                        // charge. "One node names the loss": the reason string and
                        // consequence match Roll C's OffensiveFoul arm exactly. (A future
                        // flavor tag — charge / off-arm / illegal screen — plugs in here.)
                        case ContinuationKind.ResolveOffensiveFoul:
                            // Spot-flip: an offensive foul during the backcourt bring-up
                            // hands the defense the ball already advanced (they skip Roll A).
                            // A frontcourt offensive foul is a normal dead-ball restart.
                            result = new Terminal("OffensiveFoul", c.State,
                                c.State.Frontcourt
                                    ? PossessionConsequence.DeadBallTo(c.State.Defense)
                                    : PossessionConsequence.BallAdvancedTo(c.State.Defense));
                            continue;

                        // Roll D, opponent not in bonus -> the offense keeps the ball and
                        // RE-INBOUNDS. As of #6 this no longer parks: it re-runs Roll A
                        // carrying the CURRENT court-state (a backcourt entry foul resumes
                        // backcourt and must still cross; a frontcourt foul resumes
                        // frontcourt, where the backcourt losses are unreachable). The
                        // re-entry feeds the loop exactly like IntoHalfcourtSet, so a
                        // foul on the re-inbound charges another team foul and can cross
                        // the bonus MID-LOOP — then this same kind routes to
                        // ResolveFreeThrows instead. The IterationCeiling guard + the
                        // dominant CleanEntry weight keep it converging in a few hops.
                        // (The resolver no longer holds an inbound stub; the harness
                        // builds its own for the direct fact-echo checks.)
                        case ContinuationKind.ResumeInbound:
                        {
                            // Phase 16: backcourt re-inbound preserves the active press stamp so the
                            // press can still be beaten on the next Roll A. Frontcourt re-inbound
                            // clears both markers — dead ball in the frontcourt ends any break context
                            // and the press decision cannot reach this far anyway.
                            var inboundState = c.State.Frontcourt
                                ? c.State with { FastBreak = false, PressMode = PressMode.None }
                                : c.State;
                            var pieAResume = _rollAGenerator.Generate(inboundState, pressure: 0.0);
                            result = RollA.Execute(inboundState, pieAResume, _rng, _rollAConfig);
                            continue;
                        }

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
                                out var bonusFtSpins, out var bonusFtPoints);
                            freeThrowSpins += bonusFtSpins;
                            points        += bonusFtPoints;
                            fta           += bonusFtSpins;   // each spin is one attempt
                            ftm           += bonusFtPoints;  // ftPoints == ftMakes (verified)
                            // Phase 23: FTA/FTM per slot for bonus free throws.
                            {
                                var ftSlot = c.State.SelectedSlot?.Number ?? 0;
                                ftaBySlot = ftaBySlot.WithSlot(ftSlot, bonusFtSpins);
                                ftmBySlot = ftmBySlot.WithSlot(ftSlot, bonusFtPoints);
                            }
                            continue;

                        // RETIRED (Contextification #2): Roll H's Blocked no longer emits
                        // ResolveBlock — a blocked shot routes into ResolveRebound carrying
                        // ReboundSource.Block, and Roll I's block pie resolves it. Nothing
                        // should ever route here; fail loud if something does.
                        case ContinuationKind.ResolveBlock:
                            throw new InvalidOperationException(
                                "ResolveBlock is retired (Contextification #2): a blocked shot routes into ResolveRebound with ReboundSource.Block. Nothing should route here.");

                        // Roll F, clean attempt got off -> execute Roll G (shot
                        // location), loop. Roll G is structurally Roll E: it stamps
                        // a ShotType onto its state and returns a CONTINUE
                        // (IntoShotResolution) for all five zones — so feeding it
                        // back re-enters this switch and lands on the
                        // IntoShotResolution case below. Roll G reads nothing off
                        // GameState (a zone is just an enum value), so it takes only
                        // (state, pie, rng) — like Roll F, not Roll E.
                        case ContinuationKind.IntoShotType:
                            var genG  = _rollGGenerator.GenerateWithResidual(c.State);
                            result = RollG.Execute(c.State, genG.Pie, genG.ResidualPressure, _rng);
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
                            // FGA/FGM/3PA/3PM counters — the single Roll H chokepoint every
                            // field-goal attempt passes through, including putbacks. Read the
                            // stamped ShotResult and ShotLocation off the returned result's
                            // State (both Terminal and Continue expose .State).
                            {
                                var shotSt = result is Terminal tH ? tH.State : ((Continue)result).State;
                                shotResolutions++;
                                if (shotSt.Result == ShotResult.MissFouled)
                                {
                                    // MissFouled is NOT an FGA (box-score definition): shooting
                                    // foul on a missed shot sends the shooter to the line with
                                    // no FGA charged. Track separately for the denominator guard.
                                    missFouled++;
                                }
                                else
                                {
                                    // All six remaining outcomes are a field-goal attempt.
                                    // Bin the attempt into its zone (each FGA lands in exactly
                                    // one of the five zones; the harness asserts the bins sum to FGA).
                                    fga++;
                                    switch (shotSt.ShotType)
                                    {
                                        case ShotLocation.Three: threePa++; break;
                                        case ShotLocation.Long:  longFga++;  break;
                                        case ShotLocation.Mid:   midFga++;   break;
                                        case ShotLocation.Short: shortFga++; break;
                                        case ShotLocation.Rim:   rimFga++;   break;
                                    }
                                    if (shotSt.Result is ShotResult.Made or ShotResult.MadeAndFouled)
                                    {
                                        fgm++;
                                        switch (shotSt.ShotType)
                                        {
                                            case ShotLocation.Three: threePm++; break;
                                            case ShotLocation.Long:  longFgm++;  break;
                                            case ShotLocation.Mid:   midFgm++;   break;
                                            case ShotLocation.Short: shortFgm++; break;
                                            case ShotLocation.Rim:   rimFgm++;   break;
                                        }
                                        // Per-slot FGM: credit the shooter's slot on a make.
                                        // Mirrors the per-slot FGA switch; same null-slot handling
                                        // (bonus-FT putback where Roll E never ran → unattributed).
                                        switch (shotSt.SelectedSlot?.Number)
                                        {
                                            case 1: slot1Fgm++; break;
                                            case 2: slot2Fgm++; break;
                                            case 3: slot3Fgm++; break;
                                            case 4: slot4Fgm++; break;
                                            case 5: slot5Fgm++; break;
                                            default: slotUnattributedFgm++; break; // SelectedSlot null — bonus-FT putback make
                                        }
                                        // Phase 23: 3PM per slot — subset of per-slot FGM for three-point makes.
                                        if (shotSt.ShotType == ShotLocation.Three)
                                        {
                                            var s = shotSt.SelectedSlot?.Number ?? 0;
                                            threePmBySlot = threePmBySlot.WithSlot(s, 1);
                                        }
                                    }
                                    // Per-slot FGA: credit the shooter's slot.
                                    // On a normal possession: SelectedSlot was stamped by Roll E.
                                    // On a putback: SelectedSlot carries the original Roll E
                                    // selection untouched (Roll K PutBack arm by design — same-
                                    // player rebound tilt). Null guard is defensive; should not
                                    // fire in a fully-routed possession.
                                    switch (shotSt.SelectedSlot?.Number)
                                    {
                                        case 1: slot1Fga++; break;
                                        case 2: slot2Fga++; break;
                                        case 3: slot3Fga++; break;
                                        case 4: slot4Fga++; break;
                                        case 5: slot5Fga++; break;
                                        default: slotUnattributedFga++; break; // SelectedSlot null — bonus-FT putback (Roll E never ran)
                                    }
                                    // Phase 23: 3PA per slot — subset of per-slot FGA for three-point attempts.
                                    if (shotSt.ShotType == ShotLocation.Three)
                                    {
                                        var s = shotSt.SelectedSlot?.Number ?? 0;
                                        threePaBySlot = threePaBySlot.WithSlot(s, 1);
                                    }
                                    // Phase 36: count blocks and stamp per-slot blocker attribution on-walk.
                                    if (shotSt.Result == ShotResult.Blocked)
                                    {
                                        blkCount++;
                                        blkBySlot = blkBySlot.WithSlot(
                                            BlockerPicker.Pick(shotSt, _game, _matchup, _rng).Number, 1);
                                    }
                                    // Phase 39: assist attribution on-walk. Ordinary putbacks carry
                                    // the original shooter slot forward but are self-created (no pass
                                    // after the board) → excluded by !c.Putback. The bonus-FT putback
                                    // edge (Roll E never ran) has SelectedSlot null → excluded by the
                                    // null check (and would crash the picker). One rng draw on the
                                    // assisted/not roll; a second inside AssistPicker.Pick only when
                                    // assisted. Applies to both Made and MadeAndFouled (and-1 basket).
                                    if (!c.Putback && shotSt.SelectedSlot is not null
                                        && shotSt.Result is ShotResult.Made or ShotResult.MadeAndFouled)
                                    {
                                        var zoneBase   = _matchup.AssistedRate(shotSt.ShotType!.Value);
                                        var passFactor = AssistPicker.LineupPassingFactor(shotSt, _game, _matchup);
                                        var assistProb = Math.Clamp(zoneBase * passFactor,
                                                                    _matchup.AssistRateFloor,
                                                                    _matchup.AssistRateCeiling);
                                        if (_rng.NextUnitInterval() < assistProb)
                                            astBySlot = astBySlot.WithSlot(
                                                AssistPicker.Pick(shotSt, _game, _matchup, _rng).Number, 1);
                                    }
                                }
                            }
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
                            // Select Roll I's pie by the source the loose ball arrived
                            // with. A null stamp — every legacy feeder (Roll H's Miss
                            // arm, and a missed putback re-entering here) stamps nothing
                            // — reads as LiveBall, so the live-miss path is byte-for-byte
                            // unchanged. Roll H's Blocked arm stamps Block for the
                            // block-recovery pie. The routing in Roll I is identical for
                            // both; only the weights differ.
                            var pieI = _rollIGenerator.Generate(
                                c.State,
                                c.ReboundSource ?? ReboundSource.LiveBall);
                            result = RollI.Execute(c.State, pieI, _game, _rng);
                            // ORB counters — tallied exactly once per Roll I resolution.
                            // Terminal("DefensiveRebound") = board secured by defense.
                            // Continue(ResolveOffensiveRebound) = board secured by offense.
                            // All other arms (fouls, OOB, jump-ball) are NOT a secured
                            // board and are excluded (matches box-score ORB% convention).
                            {
                                if (result is Terminal tI && tI.Reason == "DefensiveRebound")
                                    orbChances++;
                                else if (result is Continue cI && cI.Next == ContinuationKind.ResolveOffensiveRebound)
                                { orbChances++; orbWon++; }
                            }
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
                            // An offensive rebound resets the shot clock to 20 and starts a new period.
                            shotClockPeriods++;
                            // Phase 31: pick WHICH offensive player secured the board, conditional
                            // on Roll I already awarding it to the offense. Echoes the team math's
                            // per-player weight (OffensiveRebounding × PositionalWeight × shooterNerf)
                            // so the individual pick agrees with the team battle by construction.
                            // Covers both feeders (Roll I, Roll M) at this one shared node.
                            // Does NOT overwrite SelectedSlot (the shooter). Consumes one _rng draw
                            // — stream shifts vs Phase 30 (expected; documented in A5).
                            var picked31 = OffensiveRebounderPicker.Pick(c.State, _game, _matchup, _rng);
                            orbBySlot = orbBySlot.WithSlot(picked31.Number, 1);
                            var reboundState31 = c.State with { ReboundSlot = picked31 };
                            // Select Roll K's pie by the source the board arrived with. A
                            // null stamp — every legacy feeder (Roll I) stamps nothing —
                            // reads as LiveBall, so the field-goal path is byte-for-byte
                            // unchanged. Roll M stamps FreeThrow for its FT-specific pie.
                            var pieK = _rollKGenerator.Generate(
                                reboundState31,
                                c.OffensiveReboundSource ?? OffensiveReboundSource.LiveBall);
                            result = RollK.Execute(reboundState31, pieK, _game, _rng);
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
                            // An and-1 (MadeAndFouled) banks its made basket's 2/3 here:
                            // the basket counts, and this edge is hit exactly once per
                            // shooting foul, with Result distinguishing and-1 from a
                            // fouled miss (which scores no FG). ShotType is non-null —
                            // the shot resolved, so Roll G stamped the zone.
                            if (c.State.Result == ShotResult.MadeAndFouled)
                                points += Scoring.FieldGoalPoints(c.State.ShotType!.Value);
                            // Phase 25: record the shooting-foul event. ShotType is non-null
                            // (Roll G stamped the zone before Roll H resolved the foul).
                            // SelectedSlot MAY be null on a bonus-FT putback (Roll E never
                            // ran) — 0 is the "no matched man" sentinel, NOT a throw, because
                            // a bonus-FT-putback shot is a legitimate game path. The ?? 0
                            // here matches the existing Phase 23 FTA/FTM slot reads below.
                            shootingFouls.Add(new ShootingFoulEvent(
                                c.State.ShotType!.Value,
                                c.State.SelectedSlot?.Number ?? 0));
                            result = DriveFreeThrows(c.State, ShootingFoulShots(c.State), oneAndOne: false, out var shootingFtSpins, out var shootingFtPoints);
                            freeThrowSpins += shootingFtSpins;
                            points         += shootingFtPoints;
                            fta            += shootingFtSpins;   // each spin is one attempt
                            ftm            += shootingFtPoints;  // ftPoints == ftMakes (verified)
                            // Phase 23: FTA/FTM per slot for shooting-foul free throws.
                            // SelectedSlot is non-null here (shooting foul requires Roll E fired first).
                            {
                                var ftSlotS = c.State.SelectedSlot?.Number ?? 0;
                                ftaBySlot = ftaBySlot.WithSlot(ftSlotS, shootingFtSpins);
                                ftmBySlot = ftmBySlot.WithSlot(ftSlotS, shootingFtPoints);
                            }
                            continue;

                        // OOB off the defender, offense RETAINS (Roll H's
                        // MissOutOfBoundsRetained, and the I/J/K/M OutOfBoundsOffDefense
                        // + below-bonus loose-ball-defense fork): a sideline throw-in.
                        // As of #6 this no longer parks: it re-runs Roll A carrying the
                        // current court-state. These all arrive post-cross (frontcourt is
                        // already latched), so the re-inbound runs the frontcourt entry —
                        // the backcourt losses are unreachable and it almost always
                        // CleanEntry's back into the set. Same loop shape as ResumeInbound;
                        // the same guard + dominant CleanEntry weight keep it converging.
                        // (The resolver no longer holds an inbound stub; the harness
                        // builds its own for the direct fact-echo checks.)
                        case ContinuationKind.ResolveSidelineInbound:
                        {
                            // Phase 16: dead-ball re-inbound ends both the live-break context and any
                            // active press. FastBreak=true from a prior break must not carry into the
                            // new halfcourt set (Phase 16 makes Roll G read FastBreak, so leaking
                            // would give the wrong location pie). PressMode consumed here too —
                            // the press decision does not survive a dead ball.
                            var inboundState = c.State with { FastBreak = false, PressMode = PressMode.None };
                            var pieASideline = _rollAGenerator.Generate(inboundState, pressure: 0.0);
                            result = RollA.Execute(inboundState, pieASideline, _rng, _rollAConfig);
                            continue;
                        }

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

                        // RETIRED (Contextification #1): Roll J's Push no longer emits
                        // IntoTransition — it routes into IntoPlayerSelection with FastBreak
                        // stamped, so a break produces a shot through the shared rolls.
                        // Nothing should ever route here; fail loud if something does.
                        case ContinuationKind.IntoTransition:
                            throw new InvalidOperationException(
                                "IntoTransition is retired (Contextification #1): a break routes into IntoPlayerSelection with FastBreak stamped. Nothing should route here.");

                        // Roll L's FT loop, last shot missed (live ball) -> execute Roll M
                        // (free-throw rebound resolution), loop. Roll M is a GATE with
                        // mixed ends (the Roll I shape): TERMINALS (DefensiveRebound ->
                        // transition to the defense; LooseBallFoulOnOffense /
                        // OutOfBoundsOffOffense -> dead ball to the defense) and CONTINUES
                        // (OffensiveRebound -> Roll K with the FreeThrow source;
                        // LooseBallFoulOnDefense -> the charge-and-fork; OutOfBoundsOffDefense
                        // -> sideline inbound; JumpBall -> the arrow node). It mutates
                        // GameState (its LooseBallFoulOnDefense arm charges the defensive
                        // team foul), hence it takes _game — the Roll D / I / J / K shape.
                        // This edge now
                        // executes Roll M. Roll M fires ONCE per FT trip — a missed putback
                        // off its offensive board re-enters Roll I, not Roll M, so it adds
                        // no new convergence loop.
                        case ContinuationKind.ResolveFTRebound:
                            var pieM = _rollMGenerator.Generate(c.State);
                            result = RollM.Execute(c.State, pieM, _game, _rng);
                            // ORB counters — same shape as ResolveRebound (Roll I).
                            // Roll M fires once per FT trip; a missed putback off its
                            // offensive board re-enters Roll I, not Roll M, so there is
                            // no double-count with the ResolveRebound site.
                            {
                                if (result is Terminal tM && tM.Reason == "DefensiveRebound")
                                    orbChances++;
                                else if (result is Continue cM && cM.Next == ContinuationKind.ResolveOffensiveRebound)
                                { orbChances++; orbWon++; }
                            }
                            continue;

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
    private RollResult DriveFreeThrows(PossessionState state, int shots, bool oneAndOne, out int spinCount, out int ftPoints)
    {
        var pie = _rollLGenerator.Generate(state);
        var spins = 0;
        var ftMakes = 0;

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
            if (outcome == FreeThrowOutcome.Make) ftMakes++;
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
        ftPoints = ftMakes;
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
