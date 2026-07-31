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

    // S87: the dedicated committer-selection stream. Separate from _rng so foul
    // attribution can never perturb the gameplay sequence — see the constructor note.
    private readonly IRng _foulRng;

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
        IRng rng,
        IRng? foulRng = null)
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
        // S87: committer selection draws from its OWN stream, never from _rng. This is
        // what makes the whole session inert-by-construction: no foul draw can shift the
        // gameplay sequence by a single number, so with the disqualification threshold
        // raised out of reach the game replays exactly as it did before S87. A caller
        // that does not care about foul attribution passes nothing and gets a fixed
        // stream — deterministic, and equally unable to touch _rng.
        _foulRng = foulRng ?? new SystemRng(DefaultFoulStreamSeed);
    }

    /// <summary>The foul stream a caller that supplies none falls back to. Fixed rather
    /// than derived, so an un-seeded harness check is still reproducible run to run.</summary>
    private const int DefaultFoulStreamSeed = 8787;

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
        // Session 85, PAGE-ONLY: which arm of Roll J's run-or-not pie opened this walk.
        // Captured HERE, before the loop reassigns `result` — the incoming result is the one
        // Roll J returned, and Roll J is the only roll that stamps the mark. It is therefore
        // non-null exactly on a transition-entry possession and null on every other, which is
        // what makes the outcome-split counters fire only where they should by representation
        // rather than by an entry test the page has to remember to write.
        var transitionArm = result.TransitionArm;
        var putbackAttempts = 0;
        var freeThrowSpins = 0;
        var points = 0;
        var shotClockPeriods = 1;
        var fga = 0;
        var fgm = 0;
        var threePa = 0;
        var threePm = 0;
        // Session 38: fast-break shot-diet accounting. An ordinary Roll-G-selected FGA whose
        // stamped state has FastBreak = true; EXCLUDES Roll K putbacks (see the gated block
        // in the IntoShotResolution case). Nested so 0 ≤ fbThreePm ≤ fbThreePa ≤ fbFga ≤ fga.
        var fastBreakFga = 0;
        var fastBreakThreePa = 0;
        var fastBreakThreePm = 0;
        // Session 85, PAGE-ONLY: the THREE-WAY shot partition. Every FGA on this walk lands
        // in exactly one of three buckets, decided by the two flags in scope at the Roll H
        // chokepoint below — `FastBreak` on the stamped state and `Putback` on the
        // continuation:
        //   fast-break      FastBreak && !Putback   (== the S38 counter above, same gate)
        //   break putback   FastBreak &&  Putback   a second-chance shot on a LIVE break:
        //                                           Roll K's PutBack arm does NOT clear
        //                                           FastBreak (only ResetOffense does), so
        //                                           these attempts are break-stamped while
        //                                           being excluded from the S38 diet count
        //   non-break       !FastBreak              everything else, INCLUDING a shot taken
        //                                           after Roll K kicked it back out — the
        //                                           break is over and the flag is cleared
        // The buckets are wired as one if / else-if / else so the partition is exhaustive and
        // exclusive by construction; Phase 76 asserts the three sum to Fga, which is what
        // catches a mis-scoped gate. `fastBreakFgm` is new because the S38 counters carry a
        // three-point make but never a bucket-wide make, so neither break FG% nor break
        // points could be read.
        var fastBreakFgm = 0;
        var breakPutbackFga = 0; var breakPutbackFgm = 0;
        var nonBreakFga     = 0; var nonBreakFgm     = 0;
        // Blocks in the SAME three buckets, banked at the one block site further down (both
        // flags are already in scope there). A two-way break/non-break block line beside a
        // three-way shot partition would invite the false reading that two rates exhaust every
        // blocked attempt, so all three are counted.
        var fastBreakBlk = 0; var breakPutbackBlk = 0; var nonBreakBlk = 0;
        // Per-slot fast-break blocks — the subset of BlkBySlot taken on a live break that is
        // not a putback. Feeds the "who gets break blocks" concentration board; the harness
        // maps slot -> man through the SAME path ordinary blocks already take, so a man's
        // break blocks can never exceed his blocks.
        var fastBreakBlkBySlot = new SlotGroup();
        // Session 36: displacement-context bucket counters — read-only observation
        // instrumentation. Every FGA whose state carries a populated
        // ShotDisplacementLevel lands in exactly one of three buckets
        // (level < −5 / |level| ≤ 5 / level > +5); a null level is EXCLUDED,
        // never counted as neutral (FastBreak, stub, zero-defender paths, and
        // bonus-FT putbacks where Roll G never ran on this state).
        var dispLowFga = 0;   var dispLowThreePa = 0;   var dispLowThreePm = 0;
        var dispMidFga = 0;   var dispMidThreePa = 0;   var dispMidThreePm = 0;
        var dispHighFga = 0;  var dispHighThreePa = 0;  var dispHighThreePm = 0;
        var shotResolutions = 0;
        var missFouled = 0;
        var fta = 0;
        var ftm = 0;
        // Phase 51: FTA-source classification — every FTA on this walk lands in exactly
        // one of five buckets, incremented at the two FT entry edges using the trip's
        // actual spin count. They reconcile to `fta`
        // (ftaBonusPicker + ftaBonusSelected + ftaBonusUnattributed + ftaShootingSelected
        //  + ftaShootingNoSlot == fta) — asserted per-record and aggregate in Observation.
        var ftaBonusPicker      = 0;   // bonus trip, shooter named by FouledPlayerPicker (the new path)
        var ftaBonusSelected    = 0;   // bonus trip, Roll E shooter already selected (post-Roll-E bonus)
        var ftaBonusUnattributed = 0;  // bonus trip, no shooter at all (empty roster — the residual 72%)
        var ftaShootingSelected = 0;   // shooting-foul trip, normal selected shooter
        var ftaShootingNoSlot   = 0;   // shooting-foul trip, no-slot exception (post-FT-rebound putback)
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
        // Session 79, PAGE-ONLY: was the credited blocker the man guarding the shooter?
        var blkMatchedNear = 0; var blkHelperNear = 0;
        var blkMatchedOut  = 0; var blkHelperOut  = 0;
        // Phase 36: per-slot block accumulator. Total == BlkCount on every possession.
        var blkBySlot     = new SlotGroup();
        // Phase 39: per-slot assist accumulator. Total <= FGM on every possession.
        var astBySlot     = new SlotGroup();
        // Session 84, PAGE-ONLY: the lineup passing factor the assist door applied, summed
        // over this possession's assist-eligible makes, with the matching event count. Read
        // off the SAME local the probability uses, so the page can never report a factor the
        // engine did not apply.
        var assistPassFactorSum    = 0.0;
        var assistPassFactorEvents = 0;
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
        var nonShootingFouls = new List<NonShootingFoulEvent>();   // Session 62
        var offensiveFouls   = new List<OffensiveFoulEvent>();     // S87
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
                    // ── S87: the third foul ledger — offensive fouls ────────────────
                    // Both kinds count toward the committer's five and NEITHER touches
                    // the team-foul stream (Emmett's ruling: an offensive foul is charged
                    // to the man, never the team). No Fouls.Increment appears anywhere in
                    // this block, and the harness asserts that rather than assuming it.
                    if (t.Reason == "OffensiveFoul")
                    {
                        // The CHARGE family (push-off, illegal screen, player-control),
                        // reaching here from the entry, the turnover pie, or the rebound
                        // scrum. The engine has ALREADY named this man immediately above —
                        // turnoverOffSlot is the selected shooter or the interior picker's
                        // choice. S87 reuses him rather than inventing a second answer to
                        // "who charged?", so no new draw is taken and no stream moves.
                        var chargeSlot = turnoverOffSlot ?? 0;
                        var chargeMan  = chargeSlot >= 1
                            ? _game.RosterFor(t.State.Offense).PlayerAt(_game.LineupFor(t.State.Offense).SlotAt(chargeSlot))
                            : null;
                        var chargeId = chargeMan?.PlayerId ?? 0;
                        _game.PersonalFouls.Increment(chargeId);
                        offensiveFouls.Add(new OffensiveFoulEvent(chargeSlot, chargeId, IsLooseBall: false));
                    }
                    else if (t.Reason == "LooseBallFoulOnOffense")
                    {
                        // The SCRUM foul — the one foul in the game that landed on nobody
                        // before S87. Drawn on the same interior weighting the charge uses,
                        // from the dedicated foul stream.
                        var lbf = PickLooseBallOffensiveFouler(t.State);
                        _game.PersonalFouls.Increment(lbf.PlayerId);
                        offensiveFouls.Add(new OffensiveFoulEvent(lbf.Slot, lbf.PlayerId, IsLooseBall: true));
                    }
                    // Phase 35: defensive-rebound attribution — stamp which defender got it.
                    if (t.Reason == "DefensiveRebound")
                        defensiveRebounderSlot = DefensiveRebounderPicker.Pick(t.State, _game, _matchup, _rng).Number;
                    // S86: name WHO WON THE BALL on the outgoing transition ticket, so Roll J
                    // can read his legs and his outlet pass one possession later. This is the
                    // only place both facts are in hand: the emitting rolls stamped the ticket
                    // before either picker ran, and the pickers above have just produced a slot.
                    // The two are mutually exclusive at a single terminal (one Reason, and both
                    // are set only here, in the case that returns immediately), so the coalesce
                    // cannot pick the wrong one. Enrich only when a picker actually fired, and
                    // never overwrite a slot already on the ticket — no emitter sets one today,
                    // but the field's meaning must not depend on that staying true.
                    if ((defensiveRebounderSlot ?? stealerSlot) is { } ballHandlerSlot &&
                        t.Consequence.TransitionContext is { BallHandlerSlot: null } transitionTicket)
                    {
                        t = t with
                        {
                            Consequence = t.Consequence with
                            {
                                TransitionContext = transitionTicket with { BallHandlerSlot = ballHandlerSlot }
                            }
                        };
                    }
                    return new RoutingOutcome(PossessionEnded: true, Destination: $"END:{t.Reason}")
                        { EndedOn = t, PutbackAttempts = putbackAttempts, FreeThrowSpins = freeThrowSpins, Points = points, ShotClockPeriods = shotClockPeriods,
                          Fga = fga, Fgm = fgm, ThreePa = threePa, ThreePm = threePm,
                          FastBreakFga = fastBreakFga, FastBreakThreePa = fastBreakThreePa, FastBreakThreePm = fastBreakThreePm,
                          DispLowFga = dispLowFga, DispLowThreePa = dispLowThreePa, DispLowThreePm = dispLowThreePm,
                          DispMidFga = dispMidFga, DispMidThreePa = dispMidThreePa, DispMidThreePm = dispMidThreePm,
                          DispHighFga = dispHighFga, DispHighThreePa = dispHighThreePa, DispHighThreePm = dispHighThreePm,
                          ShotResolutions = shotResolutions, MissFouled = missFouled,
                          Fta = fta, Ftm = ftm, OrbChances = orbChances, OrbWon = orbWon,
                          FtaBonusPicker = ftaBonusPicker, FtaBonusSelected = ftaBonusSelected,
                          FtaBonusUnattributed = ftaBonusUnattributed,
                          FtaShootingSelected = ftaShootingSelected, FtaShootingNoSlot = ftaShootingNoSlot,
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
                          BlkMatchedNear = blkMatchedNear,
                          BlkHelperNear  = blkHelperNear,
                          BlkMatchedOut  = blkMatchedOut,
                          BlkHelperOut   = blkHelperOut,
                          TurnoverOffSlot     = turnoverOffSlot,
                          TurnoverWasLiveBall = turnoverWasLiveBall,
                          ShootingFouls  = shootingFouls.ToArray(),
                          NonShootingFouls = nonShootingFouls.ToArray(),
                          OffensiveFouls   = offensiveFouls.ToArray(),
                          OrbBySlot      = orbBySlot,
                          StealerSlot    = stealerSlot,
                          DefensiveRebounderSlot = defensiveRebounderSlot,
                          AstBySlot      = astBySlot,
                          AssistPassFactorSum    = assistPassFactorSum,
                          AssistPassFactorEvents = assistPassFactorEvents,
                          TransitionArm          = transitionArm,
                          FastBreakFgm           = fastBreakFgm,
                          BreakPutbackFga        = breakPutbackFga,
                          BreakPutbackFgm        = breakPutbackFgm,
                          NonBreakFga            = nonBreakFga,
                          NonBreakFgm            = nonBreakFgm,
                          FastBreakBlk           = fastBreakBlk,
                          BreakPutbackBlk        = breakPutbackBlk,
                          NonBreakBlk            = nonBreakBlk,
                          FastBreakBlkBySlot     = fastBreakBlkBySlot };

                case Continue c:
                    // Session 62: harvest any non-shooting foul this continuation carries,
                    // before routing on Next. Every foul charge (reach-in A/B/F or situational
                    // I/J/K/M) rides out on a Continue from DefensiveFoulCharge, so this single
                    // point captures them all regardless of the bonus branch taken.
                    // S87: the charge helper emits the event bare (it is static and draws
                    // no randomness, and stays that way). The committer is named HERE —
                    // the one point where the foul, the live game and the defense are all
                    // in hand — and the man is charged his fifth.
                    if (c.NonShootingFoul is { } nsf)
                    {
                        var nsCommitter = PickNonShootingFouler(c.State.Defense, nsf.IsReachIn);
                        _game.PersonalFouls.Increment(nsCommitter.PlayerId);
                        nonShootingFouls.Add(nsf with
                        {
                            CommitterSlot     = nsCommitter.Slot,
                            CommitterPlayerId = nsCommitter.PlayerId
                        });
                    }
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
                                    breakGenE.Reliefs,
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
                            // Pre-tilt pressures AND reliefs passed unchanged — tilt changes WHICH slot
                            // is rolled, not the load each slot carries (one-pass, no feedback loop).
                            // Both halves of the usage curve read the same pre-tilt basis, so the tilt
                            // cannot make the tax and the relief disagree about the pivot.
                            var tiltedPieE = _rollEGenerator.BendByAttention(genE, attn.AttentionShares, _game, _matchup, c.State);
                            result = RollE.Execute(c.State, tiltedPieE, genE.Pressures, genE.Reliefs,
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
                                    : PossessionConsequence.BallAdvancedTo(c.State.Defense))
                                { TimeProfile = c.State.Frontcourt
                                    ? PossessionTimeProfile.FrontcourtTurnover
                                    : PossessionTimeProfile.BackcourtTurnover };
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
                            // Phase 51: a pre-Roll-E bonus foul arrives with no shooter
                            // selected (SelectedSlot null). Name WHO drew it via the
                            // foul-draw picker and stamp it onto a LOCAL trip state, so
                            // the trip is shot at his real FreeThrow rating and credited
                            // to him. The stamp lives ONLY on this local ftState (A2
                            // isolation): c.State is never mutated, and the picker fires
                            // only when a draw is actually needed — SelectedSlot still
                            // null AND ≥1 populated offensive slot (an empty-roster
                            // isolation game falls through to the flat fallback, never
                            // throwing).
                            var ftState = c.State;
                            if (ftState.SelectedSlot is null && AnyOffensivePlayer(ftState))
                                ftState = ftState with
                                {
                                    FreeThrowShooterSlot = FouledPlayerPicker.Pick(ftState, _game, _matchup, _rng)
                                };
                            result = DriveFreeThrows(
                                ftState,
                                shots: c.Bonus == BonusType.Double ? 2 : 1,
                                oneAndOne: c.Bonus == BonusType.OneAndOne,
                                out var bonusFtSpins, out var bonusFtPoints);
                            freeThrowSpins += bonusFtSpins;
                            points        += bonusFtPoints;
                            fta           += bonusFtSpins;   // each spin is one attempt
                            ftm           += bonusFtPoints;  // ftPoints == ftMakes (verified)
                            // Phase 23 + Phase 51: FTA/FTM per slot for bonus free throws —
                            // credit the foul-draw pick when set, else the existing selected
                            // shooter, else slot 0 (unattributed, empty-roster only).
                            {
                                var ftSlot = (ftState.FreeThrowShooterSlot ?? ftState.SelectedSlot)?.Number ?? 0;
                                ftaBySlot = ftaBySlot.WithSlot(ftSlot, bonusFtSpins);
                                ftmBySlot = ftmBySlot.WithSlot(ftSlot, bonusFtPoints);
                            }
                            // Phase 51: FTA-source classification for this bonus trip. Read
                            // the LOCAL ftState (the stamp lives here, not on c.State).
                            if (ftState.FreeThrowShooterSlot != null) ftaBonusPicker       += bonusFtSpins;
                            else if (ftState.SelectedSlot     != null) ftaBonusSelected     += bonusFtSpins;
                            else                                       ftaBonusUnattributed += bonusFtSpins;
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
                            result = RollG.Execute(c.State, genG.Pie, genG.ResidualPressure, _rng, genG.DisplacementLevel);
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
                                    // Session 36: displacement-context bucket (level-populated FGA only).
                                    if (shotSt.ShotDisplacementLevel is double dispLevel)
                                    {
                                        var isThree = shotSt.ShotType == ShotLocation.Three;
                                        var isMake  = shotSt.Result is ShotResult.Made or ShotResult.MadeAndFouled;
                                        if (dispLevel < -5.0)
                                        {
                                            dispLowFga++;
                                            if (isThree) { dispLowThreePa++; if (isMake) dispLowThreePm++; }
                                        }
                                        else if (dispLevel > 5.0)
                                        {
                                            dispHighFga++;
                                            if (isThree) { dispHighThreePa++; if (isMake) dispHighThreePm++; }
                                        }
                                        else
                                        {
                                            dispMidFga++;
                                            if (isThree) { dispMidThreePa++; if (isMake) dispMidThreePm++; }
                                        }
                                    }
                                    // Session 38: fast-break shot-diet accounting. Counts an
                                    // ordinary Roll-G-selected FGA whose stamped state has
                                    // FastBreak = true, EXCLUDING Roll K putbacks. Roll K's
                                    // PutBack arm carries FastBreak forward (only ResetOffense
                                    // wipes it) but forced the shot to the rim / Roll H resolved
                                    // a putback pie — that attempt never touched the fast-break
                                    // diet, so counting it would inflate fast-break FGA with
                                    // rim-forced shots and drag the reported three-rate below
                                    // its true value. The nesting keeps the reconciliation
                                    // 0 ≤ fbThreePm ≤ fbThreePa ≤ fbFga ≤ fga.
                                    // Session 85 extends this from a single gate to the
                                    // exhaustive three-way partition: the same fast-break
                                    // condition is now the first arm of an if/else-if/else,
                                    // so every FGA lands in exactly one bucket by wiring
                                    // rather than by three independent conditions that could
                                    // drift apart. The S38 counters keep their exact prior
                                    // gate and values.
                                    var s85Make = shotSt.Result is ShotResult.Made or ShotResult.MadeAndFouled;
                                    if (shotSt.FastBreak && !c.Putback)
                                    {
                                        fastBreakFga++;
                                        if (s85Make) fastBreakFgm++;
                                        if (shotSt.ShotType == ShotLocation.Three)
                                        {
                                            fastBreakThreePa++;
                                            if (s85Make)
                                                fastBreakThreePm++;
                                        }
                                    }
                                    else if (shotSt.FastBreak)
                                    {
                                        // Break putback: Roll K forced the zone to Rim, so a
                                        // break putback three is structurally impossible and
                                        // no three-point sibling is carried here.
                                        breakPutbackFga++;
                                        if (s85Make) breakPutbackFgm++;
                                    }
                                    else
                                    {
                                        nonBreakFga++;
                                        if (s85Make) nonBreakFgm++;
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
                                        // Session 79: the putback flag lives on the continuation,
                                        // not on PossessionState, and the two block doors have
                                        // different credit rules — the putback rate is a
                                        // five-defender stack with no matched man. Pass it through.
                                        var blkSlot = BlockerPicker.Pick(shotSt, _game, _matchup,
                                                                         _rng, c.Putback).Number;
                                        blkBySlot = blkBySlot.WithSlot(blkSlot, 1);

                                        // Session 85, PAGE-ONLY: the same three-way partition
                                        // the attempt landed in, applied to the block. The
                                        // condition is written identically to the attempt
                                        // partition above so the two can never disagree about
                                        // which bucket a shot was in. Only the fast-break arm
                                        // carries a per-slot accumulator — press-born and
                                        // putback break blocks are a sliver of a sliver, and
                                        // per-team percentiles on that sample would be noise.
                                        if (shotSt.FastBreak && !c.Putback)
                                        {
                                            fastBreakBlk++;
                                            fastBreakBlkBySlot = fastBreakBlkBySlot.WithSlot(blkSlot, 1);
                                        }
                                        else if (shotSt.FastBreak) breakPutbackBlk++;
                                        else                       nonBreakBlk++;

                                        // PAGE-ONLY tally. A putback has no matched man by
                                        // construction, so it always counts as help.
                                        var blkZone = shotSt.ShotType ?? ShotLocation.Rim;
                                        var near    = blkZone is ShotLocation.Rim or ShotLocation.Short;
                                        var wasMatched = !c.Putback
                                                      && shotSt.SelectedSlot is not null
                                                      && blkSlot == shotSt.SelectedSlot.Value.Number;
                                        if (near) { if (wasMatched) blkMatchedNear++; else blkHelperNear++; }
                                        else      { if (wasMatched) blkMatchedOut++;  else blkHelperOut++;  }
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
                                        // Session 84, PAGE-ONLY: bank the factor the door is
                                        // about to use. Placed here, not after the draw, so it
                                        // counts EVERY eligible make rather than only the
                                        // assisted ones — the page's denominator is chances,
                                        // not conversions, which is what makes the realized
                                        // mean comparable to the calibration fit. Pure
                                        // arithmetic on an already-computed local: no RNG, no
                                        // branch, nothing downstream reads it.
                                        assistPassFactorSum += passFactor;
                                        assistPassFactorEvents++;
                                        // Session 57 — PostMoves interior assist discount. Read the shooter's
                                        // PostMoves off SelectedSlot (non-null per the guard above). PostAssistFactor
                                        // returns exactly 1.0 on every identity case (span 0, PostMoves <= 50, or a
                                        // non-interior zone), so we run today's exact two-factor expression with NO
                                        // ×1 reassociation there — the kill switch reproduces today bit-for-bit.
                                        var shooter    = _game.RosterFor(shotSt.Offense).PlayerAt(shotSt.SelectedSlot.Value);
                                        var postFactor = shooter is null
                                            ? 1.0
                                            : _matchup.PostAssistFactor(shotSt.ShotType!.Value, shooter.PostMoves, passFactor);
                                        var assistProb = postFactor == 1.0
                                            ? Math.Clamp(zoneBase * passFactor,
                                                         _matchup.AssistRateFloor,
                                                         _matchup.AssistRateCeiling)
                                            : Math.Clamp(zoneBase * passFactor * postFactor,
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
                            // S87: name the man AT THE WHISTLE and charge his fifth. The
                            // draw is the Session 62 weighting moved here unchanged; it
                            // runs on the dedicated foul stream, so the gameplay sequence
                            // below this line is untouched.
                            {
                                var shooterSlotForFoul = c.State.SelectedSlot?.Number ?? 0;
                                var sfCommitter = PickShootingFouler(
                                    c.State.Defense, c.State.ShotType!.Value, shooterSlotForFoul);
                                _game.PersonalFouls.Increment(sfCommitter.PlayerId);
                                shootingFouls.Add(new ShootingFoulEvent(
                                    c.State.ShotType!.Value,
                                    shooterSlotForFoul)
                                {
                                    CommitterSlot     = sfCommitter.Slot,
                                    CommitterPlayerId = sfCommitter.PlayerId
                                });
                            }
                            // Session 40: a shooting foul is a defensive team foul like any
                            // other — it counts toward the opponent's bonus. Non-shooting
                            // fouls already do this via DefensiveFoulCharge (the sole other
                            // Fouls.Increment site); the shooting path previously did not,
                            // starving the 7th-foul bonus. Charge the DEFENSE directly here.
                            // Deliberately NOT DefensiveFoulCharge.Resolve — that helper reads
                            // the bonus and forks, which would wrongly convert this shooting
                            // trip into a one-and-one; the shooter's own trip must stay the
                            // shot-derived 1/2/3 count (oneAndOne: false below). Increment
                            // draws no RNG, so the pre-bonus stream is unchanged; only future
                            // possessions' bonus reads move, which is the intended effect.
                            _game.Fouls.Increment(c.State.Defense);
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
                            // Phase 51: FTA-source classification for this shooting-foul trip.
                            // The foul-draw picker is NOT wired here — a shooting foul with no
                            // selected shooter is the existing no-slot exception (a post-FT-rebound
                            // putback, Roll E never ran), which keeps the flat fallback untouched.
                            if (c.State.SelectedSlot != null) ftaShootingSelected += shootingFtSpins;
                            else                              ftaShootingNoSlot   += shootingFtSpins;
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
    /// <summary>Phase 51: true if the offensive side has at least one populated slot —
    /// the gate the bonus FT edge checks before invoking <see cref="FouledPlayerPicker"/>.
    /// An empty-roster isolation game has zero populated offensive slots, so the picker
    /// (which throws on zero) is skipped and the trip falls through to Roll L's flat
    /// fallback. Mirrors the picker's own Stage-1 population scan.</summary>
    private bool AnyOffensivePlayer(PossessionState state)
    {
        var lineup = _game.LineupFor(state.Offense);
        var roster = _game.RosterFor(state.Offense);
        for (var i = 1; i <= 5; i++)
            if (roster.PlayerAt(lineup.SlotAt(i)) is not null)
                return true;
        return false;
    }

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
    /// the FT-rebound node.
    /// <para>Phase 51: the live-ball exit NULLS
    /// <see cref="PossessionState.FreeThrowShooterSlot"/> — this is the one and only
    /// live-ball exit from a free-throw trip, so clearing it here guarantees a foul-draw
    /// stamp can never carry past the trip into a later live-ball continuation (the
    /// bonus-miss → FT-rebound → putback route, where a second shooting-foul trip would
    /// otherwise read the stale stamp). The made-FT exit ends the possession, so its
    /// stamp dies with the terminal — no clear needed there.</para></summary>
    private static RollResult LastShot(PossessionState state, FreeThrowOutcome outcome) =>
        outcome == FreeThrowOutcome.Make
            ? new Terminal("FreeThrowsMade", state, PossessionConsequence.DeadBallTo(state.Defense))
            : new Continue(ContinuationKind.ResolveFTRebound, state with { FreeThrowShooterSlot = null });

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

    // ═════════════════════════════════════════════════════════════════════════════
    // S87 — COMMITTER SELECTION. Every whistle names a man, at the whistle.
    //
    // The three helpers below decide WHO committed each kind of foul. Two of them are
    // the Session 62 attribution draws moved house VERBATIM — same weight tables, same
    // interior proxy, same reach-in propensity, same cumulative walk. The foul
    // DISTRIBUTION keeps its exact character; what changes is that the answer is now
    // decided while the game is running (and can therefore have consequences) instead
    // of being re-drawn afterwards over a reconstructed lineup.
    //
    // All three draw from _foulRng, never _rng. The third kind — the offensive foul —
    // is handled at its terminal and mostly needs no draw at all, because the engine
    // already names the man who commits a charge.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>The occupied seats of <paramref name="side"/> right now — the men and the
    /// slot numbers they sit in, as two parallel lists. Empty only on the degenerate
    /// harness path where a foul node is driven against a game with no roster seated.</summary>
    private (List<Player> Men, List<int> Slots) OccupiedSeats(TeamSide side)
    {
        var lineup = _game.LineupFor(side);
        var roster = _game.RosterFor(side);
        var men    = new List<Player>(5);
        var slots  = new List<int>(5);
        for (var s = 1; s <= 5; s++)
        {
            var p = roster.PlayerAt(lineup.SlotAt(s));
            if (p != null) { men.Add(p); slots.Add(s); }
        }
        return (men, slots);
    }

    /// <summary>WHO FOULED THE SHOOTER — <see cref="FoulCommitter"/>'s shooting draw over
    /// the defense's occupied seats, on the dedicated foul stream.</summary>
    private (int Slot, int PlayerId) PickShootingFouler(TeamSide defense, ShotLocation zone, int shooterSlot)
    {
        var (men, slots) = OccupiedSeats(defense);
        if (men.Count == 0) return DegenerateNoOneOnFloor();

        var idx = FoulCommitter.CumulativeDraw(
            FoulCommitter.ShootingWeights(men, slots, zone, shooterSlot), _foulRng);
        return (slots[idx], men[idx].PlayerId);
    }

    /// <summary>WHO REACHED IN — <see cref="FoulCommitter"/>'s non-shooting draw over the
    /// defense's occupied seats, on the dedicated foul stream.</summary>
    private (int Slot, int PlayerId) PickNonShootingFouler(TeamSide defense, bool isReachIn)
    {
        var (men, slots) = OccupiedSeats(defense);
        if (men.Count == 0) return DegenerateNoOneOnFloor();

        var idx = FoulCommitter.CumulativeDraw(
            FoulCommitter.NonShootingWeights(men, isReachIn, _matchup), _foulRng);
        return (slots[idx], men[idx].PlayerId);
    }

    /// <summary>
    /// WHO WAS IN THE SCRUM. The committer of a loose-ball foul on the OFFENSE — the shove
    /// or hook fighting for a rebound, which before S87 was the one foul in the game that
    /// landed on nobody at all.
    ///
    /// <para>Emmett's ruling: the men in the scrum, not the guard standing at the top of
    /// the key. So it reuses the interior weighting the engine already applies to a charge
    /// (<see cref="TurnoverInteriorPicker"/>) rather than inventing a second, disagreeing
    /// answer to "who commits an offensive foul". The draw runs on the foul stream, so the
    /// picker's usual draw from the gameplay stream is NOT taken here.</para>
    /// </summary>
    private (int Slot, int PlayerId) PickLooseBallOffensiveFouler(PossessionState state)
    {
        var (men, _) = OccupiedSeats(state.Offense);
        if (men.Count == 0) return DegenerateNoOneOnFloor();

        var slot = TurnoverInteriorPicker.Pick(state, _game, _matchup, _foulRng);
        var p    = _game.RosterFor(state.Offense).PlayerAt(slot);
        return (slot.Number, p?.PlayerId ?? 0);
    }

    /// <summary>
    /// The degenerate case: a foul resolved with ZERO seats occupied. Harness-only — it
    /// arises when a check drives a foul node directly against a game whose rosters were
    /// never seated. No real game path reaches it (every season and full-game path seats
    /// all ten men before the tip), which the harness asserts rather than assumes.
    ///
    /// <para>Records a slot so the ledger still has a shape, a PlayerId of 0 meaning "no
    /// man", and charges nobody — there is no one on the floor to charge. Consumes one
    /// draw from the foul stream so the stream's position does not depend on whether a
    /// roster happened to be seated.</para>
    /// </summary>
    private (int Slot, int PlayerId) DegenerateNoOneOnFloor()
    {
        var slot = 1 + (int)(_foulRng.NextUnitInterval() * 5.0);
        return (Math.Clamp(slot, 1, 5), 0);
    }
}
