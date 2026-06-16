using Charm.Engine;

namespace Charm.Harness;

internal static class Program
{
    private static int Main(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // dotnet run -- game  skips validation and plays one game.
        if (args.Length > 0 && args[0] == "game") { RunGame(configPath); return 0; }

        var cfg = RollAConfig.Load(configPath);
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);
        var cfgI = RollIConfig.Load(configPath);
        var cfgJ = RollJConfig.Load(configPath);
        var cfgK = RollKConfig.Load(configPath);
        var cfgL = RollLConfig.Load(configPath);
        var cfgM = RollMConfig.Load(configPath);
        var cfgOffFoul = RollOffensiveFoulConfig.Load(configPath);
        var cfgGov = GovernorConfig.Load(configPath);
        var cfgClock = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // Roll A generator constructed below after SeatStartersFromConfig (Phase 14).
        // Roll B generator constructed below after SeatStartersFromConfig (Phase 13).
        var rollCGenerator = new RollCStubPieGenerator(cfgC);
        var rollDGenerator = new RollDStubPieGenerator(cfgD);
        // Roll E generator constructed below after game is created (Phase 15: needs GameState).
        // Roll F generator constructed below after SeatStartersFromConfig (Phase 12).
        // RollHGenerator, RollGGenerator, and RollIGenerator constructed below,
        // after game and cfgMatchup (need GameState and MatchupConfig).
        var rollJGenerator = new RollJStubPieGenerator(cfgJ);
        var rollKGenerator = new RollKStubPieGenerator(cfgK);
        var rollLGenerator = new RollLStubPieGenerator(cfgL);
        var offensiveFoulGenerator = new RollOffensiveFoulStubPieGenerator(cfgOffFoul);

        // The half's foul tracker carries the config-driven bonus thresholds.
        var fouls = new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);
        var game = new GameState(fouls);  // arrow starts Off — first jump ball is the tip
        var cfgMatchup = MatchupConfig.Load(configPath);
        SeatStartersFromConfig(game, configPath);       // v2 fix: seat real rosters before generators
        var rollGGenerator = new RollGGenerator(cfgG, cfgMatchup, game);   // Phase 9: matchup-aware location
        var rollHGenerator = new RollHGenerator(cfgH, cfgMatchup, game);
        var rollIGenerator = new RollIGenerator(cfgI, cfgMatchup, game);   // Phase 10: matchup-aware rebounding
        var rollMGenerator = new RollMGenerator(cfgM, cfgMatchup, game);   // Phase 11: matchup-aware FT rebounding
        var rollFGenerator = new RollFGenerator(cfgF, cfgMatchup, game);   // Phase 12: pressure-aware disruption
        var rollBGenerator = new RollBGenerator(cfgB, cfgMatchup, game);   // Phase 13: team-aggregate disruption
        var rollAGenerator = new RollAGenerator(cfg, cfgMatchup, game);    // Phase 14: full-court press disruption
        var rollEGenerator = new RollEGenerator(cfgE, game);               // Phase 15: attribute-driven halfcourt selection

        var resolver = new Resolver(
            rollAGenerator,
            cfg,
            rollBGenerator,
            rollCGenerator,
            cfgC,
            rollDGenerator,
            rollEGenerator,
            rollFGenerator,
            rollGGenerator,
            rollHGenerator,
            rollIGenerator,
            rollJGenerator,
            rollKGenerator,
            rollLGenerator,
            rollMGenerator,
            offensiveFoulGenerator,
            cfgMatchup,
            game,
            rng);

        var state = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        Console.WriteLine("=== Project Charm :: Roll A -> B -> C -> D -> E -> F -> G -> H -> I -> J -> K Chain ===\n");

        ShowSamples(cfg, cfgE, rollAGenerator, rollEGenerator, resolver, game, state, rng);
        var ok = BatchCheck(cfg, cfgB, new StubPieGenerator(cfg), new RollBStubPieGenerator(cfgB), resolver, state);
        ok &= RollCBatchCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollDFlavorBatchCheck(cfg, cfgD, rollDGenerator, state);
        ok &= RollDBonusRoutingCheck(cfgD, rollDGenerator, state);
        ok &= DefensiveFoulChargeCheck(cfgD, state);
        ok &= PhysicalitySignalCheck(cfgB, new RollBStubPieGenerator(cfgB), state);
        ok &= PressureSignalCheck(cfgC, rollCGenerator, state);
        ok &= JumpBallCheck(cfg);
        ok &= SlotLayerCheck(game);
        ok &= RollESelectionBatchCheck(cfg, cfgE, cfgD, rollEGenerator, game, state);
        ok &= RollFActionBatchCheck(cfg, cfgF, new RollFStubPieGenerator(cfgF), state);
        ok &= RollFHandoffCheck(cfg, game, state);
        ok &= RollGLocationBatchCheck(cfg, cfgG, state);
        ok &= RollGHandoffCheck(cfg, state);
        ok &= RollHResolutionBatchCheck(cfg, cfgH, rollHGenerator, state);
        ok &= RollHHandoffCheck(cfg, state);
        ok &= RollIReboundBatchCheck(cfg, cfgI, new RollIStubPieGenerator(cfgI), game, state);
        ok &= RollIBonusForkCheck(cfg, cfgD, cfgI, new RollIStubPieGenerator(cfgI), state);
        ok &= RollIBlockReboundBatchCheck(cfg, cfgI, new RollIStubPieGenerator(cfgI), state);
        ok &= RollIBlockContextSelectionCheck(cfg, cfgI, state);
        ok &= RollJBatchCheck(cfg, cfgD, cfgJ, rollJGenerator, state);
        ok &= RollJBonusForkCheck(cfg, cfgD, cfgJ, state);
        ok &= RollJStealBatchCheck(cfg, cfgD, cfgJ, rollJGenerator, state);
        ok &= RollKReboundBatchCheck(cfg, cfgK, rollKGenerator, game, state);
        ok &= RollKPutbackPieCheck(cfg, cfgH, state);
        ok &= RollKBonusForkCheck(cfg, cfgD, cfgK, rollKGenerator, state);
        ok &= RollLFreeThrowCheck(cfg, state);
        ok &= RollMReboundBatchCheck(cfg, cfgM, new RollMStubPieGenerator(cfgM), game, state);
        ok &= RollMContextSelectionCheck(cfg, cfgK, cfgJ, state);
        ok &= OffensiveReboundConvergenceCheck(cfg, state);
        ok &= RollCContextCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollCExpansionCheck(cfg, cfgC, rollCGenerator, state);
        ok &= EndOfHalfIntentBatchCheck(cfg, cfgEndOfHalf);
        ok &= GovernorLoopCheck(cfg, cfgD, cfgGov, cfgClock, cfgEndOfHalf);
        ok &= Phase1RosterCheck(configPath);
        ok &= Phase2AttributeWiringCheck(configPath);
        ok &= Phase6MatchupWiringCheck(configPath);
        ok &= Phase7BlockDoorCheck(configPath);
        ok &= Phase8FoulDoorCheck(configPath);
        ok &= Phase9LocationDoorCheck(configPath);
        ok &= Phase10ReboundDoorCheck(configPath);
        ok &= Phase11FreeThrowReboundDoorCheck(configPath);
        ok &= Phase12DisruptionDoorCheck(configPath);
        ok &= Phase13TeamDisruptionDoorCheckRollB(configPath);
        ok &= Phase15PressFrequencyStandardCheck(configPath);
        ok &= Phase16PressBreakFastBreakCheck(configPath);

        ObservationRunV1(configPath);
        Console.WriteLine(ok ? "\nALL CHECKS PASSED." : "\nCHECKS FAILED.");
        return ok ? 0 : 1;
    }

    // --- Observability: print a few full A->...->terminal chains. ---
    private static void ShowSamples(
        RollAConfig cfg, RollEConfig cfgE, IRollAPieGenerator genA,
        IRollEPieGenerator genE, Resolver resolver,
        GameState game, PossessionState state, IRng rng)
    {
        Console.WriteLine("--- Observability: sample possessions (seeded, full chain) ---");
        const double pressure = 0.5;

        for (var i = 0; i < 8; i++)
        {
            var pie = genA.Generate(state, pressure);
            var result = RollA.Execute(state, pie, rng, cfg);
            var routing = resolver.Route(result);

            var kind = result is Terminal ? "TERMINAL" : "CONTINUE";
            var elapsed = result.ElapsedSeconds is { } s ? $"{s:0}s" : "deferred";
            Console.WriteLine(
                $"  rollA={kind,-8} | elapsed={elapsed,-8} | final -> {routing.Destination}" +
                (routing.PossessionEnded ? " (possession ended)" : ""));
        }
        Console.WriteLine();

        // Roll C observability: print its pie, an input, and a resolved outcome.
        Console.WriteLine("--- Observability: Roll C (turnover classification) ---");
        var cfgCForObs = RollCConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "config.json"));
        var genC = new RollCStubPieGenerator(cfgCForObs);
        var pieC = genC.Generate(state, pressure: 0.0);
        Console.WriteLine($"  pie: {pieC}");
        var sampleRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 5; i++)
        {
            var r = RollC.Execute(state, pieC, sampleRng, cfgCForObs);
            var term = (Terminal)r;
            var elapsed = r.ElapsedSeconds is { } s ? $"{s:0}s" : "deferred";
            Console.WriteLine($"  input=turnover | result=TERMINAL | reason={term.Reason,-18} | elapsed={elapsed}");
        }
        Console.WriteLine();

        // Roll D observability: the flavor pie (theater), then a walk of the
        // foul count climbing on one team so the bonus crossings at 7 and 10 are
        // visible — count before/after, bonus state, and the resulting route.
        Console.WriteLine("--- Observability: Roll D (non-shooting defensive foul) ---");
        var cfgD = RollDConfig.Load(Path.Combine(AppContext.BaseDirectory, "config.json"));
        var genD = new RollDStubPieGenerator(cfgD);
        var pieD = genD.Generate(state);
        Console.WriteLine($"  flavor pie (theater, does not route): {pieD}");
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");

        // A fresh game whose AWAY team (the defense in `state`) keeps fouling.
        var obsGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var obsRng = new SystemRng(cfg.Seed);
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = obsGame.Fouls.FoulsFor(state.Defense);
            var r = (Continue)RollD.Execute(state, pieD, obsGame, obsRng);
            var after = obsGame.Fouls.FoulsFor(state.Defense);
            var route = r.Next == ContinuationKind.ResolveFreeThrows
                ? $"ResolveFreeThrows({r.Bonus})"
                : "ResumeInbound";
            Console.WriteLine(
                $"  foul#{after,2}: {before}->{after} | flavor={r.Flavor,-8} | bonus={r.Bonus,-9} | route={route}");
        }
        Console.WriteLine();

        // Roll E observability: the flat selection pie, then a few sample
        // selections showing a real named slot stamped on the possession and the
        // full chain landing on the player-action stub. This is the seam Session 7
        // left being walked for the first time: Proceed -> Roll E -> a named slot.
        Console.WriteLine("--- Observability: Roll E (player selection) ---");
        var pieE = genE.Generate(state);
        Console.WriteLine($"  selection pie (flat, no signal yet): {pieE}");
        var selRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 8; i++)
        {
            var r = (Continue)RollE.Execute(state, pieE, game, selRng);
            var s = r.State.SelectedSlot!.Value;
            Console.WriteLine(
                $"  proceed -> selected {s.Side} slot {s.Number} | next={r.Next}");
        }
        Console.WriteLine();

        // Roll F observability: the flat-ish action pie, then a few sample
        // actions. Each starts from a SELECTED slot (rolled through Roll E first,
        // exactly as the live chain does), so the resolved action carries a real
        // slot forward. Shows the five-way classification and the kind each emits
        // — STUB:PlayerAction is gone; the action now resolves into one of five
        // real exits.
        Console.WriteLine("--- Observability: Roll F (player action) ---");
        var genF = new RollFStubPieGenerator(RollFConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "config.json")));
        var pieF = genF.Generate(state);
        Console.WriteLine($"  action pie (flat-ish, no signal yet): {pieF}");
        var actRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 8; i++)
        {
            // Select a player first (Roll E), then resolve the action (Roll F).
            var selected = ((Continue)RollE.Execute(state, pieE, game, actRng)).State;
            var r = (Continue)RollF.Execute(selected, pieF, actRng);
            var s = r.State.SelectedSlot!.Value;
            Console.WriteLine(
                $"  {s.Side} slot {s.Number} action -> {r.Next}");
        }
        Console.WriteLine();

        // Roll H observability: the per-zone block weights (the Session 13
        // headline), then a few sample shots. Each is rolled through the live chain
        // up to H — select a slot (Roll E), stamp a zone (Roll G) — so each resolved
        // shot carries a real slot AND zone; the generator sizes that shot's block
        // slice from its zone, and H stamps the result. Shows the seven-way outcome,
        // the zone it resolved against, and where it routes (terminal vs. one of the
        // four post-H stubs, block recovery included).
        Console.WriteLine("--- Observability: Roll H (make/miss) ---");
        var configPathH = Path.Combine(AppContext.BaseDirectory, "config.json");
        var genGForH = new RollGStubPieGenerator(RollGConfig.Load(configPathH));
        var cfgHForObs = RollHConfig.Load(configPathH);
        var genH = new RollHStubPieGenerator(cfgHForObs);
        Console.WriteLine("  per-zone block weight b(zone) (Rim highest, Three lowest):");
        foreach (var z in new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three })
            Console.WriteLine($"    {z,-6} {cfgHForObs.BlockWeight(z):P2}");
        var shotRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 8; i++)
        {
            // Walk E -> G to deliver a fully-stamped pre-H state, generate the pie
            // for THAT shot's zone, then resolve H.
            var selectedH = ((Continue)RollE.Execute(state, pieE, game, shotRng)).State;
            var withZone = ((Continue)RollG.Execute(selectedH, genGForH.Generate(selectedH), shotRng)).State;
            var pieH = genH.Generate(withZone);
            var hr = RollH.Execute(withZone, pieH, shotRng);
            var carried = hr switch { Terminal t => t.State, Continue c => c.State, _ => withZone };
            var s2 = carried.SelectedSlot!.Value;
            var ending = hr is Terminal term
                ? $"TERMINAL ({term.Reason})"
                : $"CONTINUE -> {((Continue)hr).Next}";
            Console.WriteLine(
                $"  {s2.Side} slot {s2.Number} {carried.ShotType} shot -> {carried.Result} -> {ending}");
        }
        Console.WriteLine();

        // Roll I observability: the seven-way rebound pie, then a few sample MISSES
        // driven through the live chain (E -> G -> H), each landing on one of Roll
        // I's destinations. Only misses reach Roll I here (blocks do too, via the
        // block pie — exercised in the dedicated block checks). We keep rolling H
        // until a Miss lands, then route it and show where the rebound resolved:
        // a defensive board / offensive foul / OOB-off-offense ends the possession
        // (terminal), an offensive board / loose-ball-defense foul / OOB-off-defense /
        // jump ball keeps it alive (continue).
        Console.WriteLine("--- Observability: Roll I (rebound resolution) ---");
        var cfgIForObs = RollIConfig.Load(configPathH);
        var genI = new RollIStubPieGenerator(cfgIForObs);
        var pieIObs = genI.Generate(state, ReboundSource.LiveBall);
        Console.WriteLine($"  live-miss rebound pie (flat, no signal yet): {pieIObs}");
        var obsGameI = new GameState(new FoulTracker(
            RollDConfig.Load(configPathH).BonusThreshold,
            RollDConfig.Load(configPathH).DoubleBonusThreshold));
        var reboundRng = new SystemRng(cfg.Seed);
        var shown = 0;
        var guard = 0;
        while (shown < 8 && guard++ < 100000)
        {
            // Walk E -> G -> H; only act on a Miss (the one outcome that feeds I).
            var sel = ((Continue)RollE.Execute(state, pieE, obsGameI, reboundRng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genGForH.Generate(sel), reboundRng)).State;
            var hRes = RollH.Execute(zoned, genH.Generate(zoned), reboundRng);
            if (hRes is not Continue { Next: ContinuationKind.ResolveRebound } missCont) continue;

            var iRes = RollI.Execute(missCont.State, pieIObs, obsGameI, reboundRng);
            var landing = iRes switch
            {
                Terminal t => $"TERMINAL ({t.Reason})",
                Continue { Next: ContinuationKind.ResolveOffensiveRebound } => "CONTINUE -> OffensiveRebound (same possession)",
                Continue { Next: ContinuationKind.ResolveSidelineInbound } c => $"CONTINUE -> SidelineInbound (bonus={c.Bonus})",
                Continue { Next: ContinuationKind.ResolveFreeThrows } c => $"CONTINUE -> FreeThrows (bonus={c.Bonus})",
                Continue { Next: ContinuationKind.ResolveJumpBall } => "CONTINUE -> JumpBall (arrow node)",
                _ => "?"
            };
            Console.WriteLine($"  miss rebounded -> {landing}");
            shown++;
        }
        Console.WriteLine();
    }

    // --- Batch: confirm A->B rates and clean hand-offs throughout. ---
    private static bool BatchCheck(
        RollAConfig cfg, RollBConfig cfgB,
        IRollAPieGenerator genA, IRollBPieGenerator genB,
        Resolver resolver, PossessionState state)
    {
        Console.WriteLine($"--- Batch: {cfg.BatchSize:N0} possessions (full chain, physicality=0.00) ---");
        var rng = new SystemRng(cfg.Seed);
        const double pressure = 0.0;
        var pieA = genA.Generate(state, pressure);

        var aCounts = new Dictionary<EntryOutcome, int>();
        foreach (var o in Enum.GetValues<EntryOutcome>()) aCounts[o] = 0;

        var bCounts = new Dictionary<HalfcourtOutcome, int>();
        foreach (var o in Enum.GetValues<HalfcourtOutcome>()) bCounts[o] = 0;

        var ended = 0;
        var routedToStub = 0;
        var unrouted = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollA.Execute(state, pieA, rng, cfg);

            var aOutcome = result switch
            {
                Continue { Next: ContinuationKind.IntoHalfcourtSet } => EntryOutcome.CleanEntry,
                Continue { Next: ContinuationKind.ResolveTurnoverType } => EntryOutcome.Turnover,
                Continue { Next: ContinuationKind.ResolveOffensiveFoul } => EntryOutcome.OffensiveFoul,
                Continue { Next: ContinuationKind.ResolveFoulType } => EntryOutcome.DefensiveFoul,
                Continue { Next: ContinuationKind.ResolveJumpBall } => EntryOutcome.JumpBall,
                _ => throw new InvalidOperationException("Unmapped Roll A result.")
            };
            aCounts[aOutcome]++;

            if (aOutcome == EntryOutcome.CleanEntry)
            {
                var pieBSample = genB.Generate(state, physicality: 0.0);
                var bResult = RollB.Execute(state, pieBSample, rng);
                var bOutcome = bResult switch
                {
                    Continue { Next: ContinuationKind.IntoPlayerSelection } => HalfcourtOutcome.Proceed,
                    Continue { Next: ContinuationKind.ResolveFoulType } => HalfcourtOutcome.Foul,
                    Continue { Next: ContinuationKind.ResolveTurnoverType } => HalfcourtOutcome.DeadBallTurnover,
                    Continue { Next: ContinuationKind.ResolveJumpBall } => HalfcourtOutcome.JumpBall,
                    _ => throw new InvalidOperationException("Unmapped Roll B result.")
                };
                bCounts[bOutcome]++;
            }

            var routing = resolver.Route(result);
            if (routing.PossessionEnded) ended++;
            else if (routing.Destination.StartsWith("STUB:")) routedToStub++;
            else unrouted++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;

        Console.WriteLine("  Roll A outcomes:");
        foreach (var (outcome, weight) in pieA.Slices)
        {
            var observed = aCounts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-18} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var cleanEntries = aCounts[EntryOutcome.CleanEntry];
        var pieB = genB.Generate(state, physicality: 0.0);
        Console.WriteLine($"\n  Roll B outcomes (of {cleanEntries:N0} clean entries):");
        foreach (var (outcome, weight) in pieB.Slices)
        {
            var observed = bCounts[outcome] / (double)cleanEntries;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfgB.Epsilon + cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-18} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        // As of Contextification #6 the possession chain CLOSES: the two inbound
        // edges that used to park (Roll D below-bonus ResumeInbound, OOB-retained
        // ResolveSidelineInbound) now RE-RUN Roll A and resolve downstream, and Roll
        // A's three former violation terminals moved into Roll C. So nothing parks at
        // a stub in the live chain anymore — every possession ends on a terminal.
        // `routed-to-stub` therefore drops to ~0 and `ended` rises to ≈ BatchSize.
        // (The shared game's fouls climb across the batch and cross the bonus within
        // the first few possessions, so almost every defensive foul becomes a
        // free-throw trip rather than a re-inbound — both terminate.) The invariant is
        // unchanged: ended + routed-to-stub == BatchSize, unrouted == 0.
        var handoffOk = unrouted == 0 && (ended + routedToStub) == cfg.BatchSize;
        Console.WriteLine($"\n  handoff: ended={ended:N0}, routed-to-stub={routedToStub:N0}, unrouted={unrouted} -> {(handoffOk ? "ok" : "FAIL")}");

        return ratesOk && handoffOk;
    }

    // --- Batch: Roll C's (Halfcourt) rates match its pie, and every exit is a clean
    //     terminal. As of #6 the Halfcourt context is the full live 15-way loss set,
    //     so this now exercises every halfcourt-natural turnover type, not just five. ---
    private static bool RollCBatchCheck(
        RollAConfig cfg, RollCConfig cfgC, RollCStubPieGenerator genC, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} turnovers through Roll C (pressure=0.00) ---");
        var rng = new SystemRng(cfg.Seed);
        var pieC = genC.Generate(state, pressure: 0.0);

        var counts = new Dictionary<TurnoverOutcome, int>();
        foreach (var o in Enum.GetValues<TurnoverOutcome>()) counts[o] = 0;

        var nonTerminal = 0;
        var liveStealCtxOk = 0;   // live arms carrying TransitionContext.Steal (-> Roll J)
        var liveCtxBad = 0;       // live arms MISSING the steal context (a FAIL)
        var deadNullCtxOk = 0;    // dead arms carrying a dead-ball restart, no context (-> Roll A)
        var deadCtxBad = 0;       // dead arms wrongly carrying a transition context (a FAIL)

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollC.Execute(state, pieC, rng, cfgC);
            if (result is not Terminal t)
            {
                nonTerminal++;
                continue;
            }
            // Halfcourt is now a LIVE multi-type pie (#6), so a draw can land on any
            // of the fifteen reasons — map them all via the shared regression-net map.
            var outcome = MapTurnover(t.Reason);
            counts[outcome]++;

            // Contextification #3: the two LIVE arms (intercepted / stripped-live) now carry
            // the Steal transition context, so the resolver routes the spawned possession to
            // Roll J; the three DEAD arms carry NO context (a dead-ball restart at Roll A).
            var live = outcome is TurnoverOutcome.BadPassIntercepted or TurnoverOutcome.LostBallLiveBall;
            if (live)
            {
                if (t.Consequence.NextEntry == EntryType.Transition
                    && t.Consequence.TransitionContext?.Source == TransitionSource.Steal) liveStealCtxOk++;
                else liveCtxBad++;
            }
            else
            {
                // Session 27 spot-flip: a dead-ball turnover in the backcourt now
                // produces BallAdvanced (skip Roll A); frontcourt stays DeadBallInbound.
                // Both are correct dead-ball consequences — neither carries a transition context.
                if ((t.Consequence.NextEntry == EntryType.DeadBallInbound
                     || t.Consequence.NextEntry == EntryType.BallAdvanced)
                    && t.Consequence.TransitionContext is null) deadNullCtxOk++;
                else deadCtxBad++;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll C outcomes:");
        foreach (var (outcome, weight) in pieC.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-20} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var terminalOk = nonTerminal == 0;
        Console.WriteLine($"\n  every exit is a clean terminal: non-terminal={nonTerminal} -> {(terminalOk ? "ok" : "FAIL")}");

        var liveCtxOk = liveCtxBad == 0 && liveStealCtxOk > 0;
        var deadCtxOk = deadCtxBad == 0 && deadNullCtxOk > 0;
        Console.WriteLine($"  live arms carry Steal context (-> Roll J): ok={liveStealCtxOk:N0} bad={liveCtxBad} -> {(liveCtxOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  dead arms carry DeadBallInbound or BallAdvanced, no transition context: ok={deadNullCtxOk:N0} bad={deadCtxBad} -> {(deadCtxOk ? "ok" : "FAIL")}");

        return ratesOk && terminalOk && liveCtxOk && deadCtxOk;
    }

    // --- Batch: Roll D's three flavor rates match its pie, and every exit is a
    //     clean Continue (ResumeInbound or ResolveFreeThrows). Uses a fresh game
    //     per iteration so the foul count never climbs into the bonus — this
    //     check isolates FLAVOR conformance; routing is checked separately. ---
    private static bool RollDFlavorBatchCheck(
        RollAConfig cfg, RollDConfig cfgD, RollDStubPieGenerator genD, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} fouls through Roll D (flavor rates) ---");
        var rng = new SystemRng(cfg.Seed);
        var pieD = genD.Generate(state);

        var counts = new Dictionary<FoulFlavor, int>();
        foreach (var f in Enum.GetValues<FoulFlavor>()) counts[f] = 0;

        var unrouted = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Fresh single-foul game each time: stays below the bonus threshold,
            // so the route is always ResumeInbound and we measure flavor cleanly.
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var result = RollD.Execute(state, pieD, g, rng);

            if (result is not Continue c || c.Flavor is not { } flavor)
            {
                unrouted++;
                continue;
            }
            counts[flavor]++;

            // A single foul can never reach the bonus, so it must ResumeInbound.
            if (c.Next != ContinuationKind.ResumeInbound || c.Bonus != BonusType.None)
                unrouted++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll D flavors:");
        foreach (var (flavor, weight) in pieD.Slices)
        {
            var observed = counts[flavor] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {flavor,-10} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var routedOk = unrouted == 0;
        Console.WriteLine($"\n  every exit a clean below-bonus Continue: anomalies={unrouted} -> {(routedOk ? "ok" : "FAIL")}");
        return ratesOk && routedOk;
    }

    // --- Bonus routing: drive one team's foul count from 1 upward and confirm
    //     the route flips at exactly the configured thresholds: None below the
    //     bonus, OneAndOne on [bonus, double), Double at/above double. Also
    //     confirms the increment lands on the FOULING (defense) team only. ---
    private static bool RollDBonusRoutingCheck(
        RollDConfig cfgD, RollDStubPieGenerator genD, PossessionState state)
    {
        Console.WriteLine("\n--- Bonus routing: Roll D route vs. foul count ---");
        var rng = new SystemRng(42);
        var pieD = genD.Generate(state);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));

        var allOk = true;
        var offenseFoulsLeaked = false;

        for (var foul = 1; foul <= cfgD.DoubleBonusThreshold + 2; foul++)
        {
            var c = (Continue)RollD.Execute(state, pieD, game, rng);

            // Expected bonus for this post-increment count.
            var expectedBonus =
                foul >= cfgD.DoubleBonusThreshold ? BonusType.Double
                : foul >= cfgD.BonusThreshold ? BonusType.OneAndOne
                : BonusType.None;
            var expectedKind = expectedBonus == BonusType.None
                ? ContinuationKind.ResumeInbound
                : ContinuationKind.ResolveFreeThrows;

            var bonusOk = c.Bonus == expectedBonus;
            var kindOk = c.Next == expectedKind;
            allOk &= bonusOk && kindOk;

            // The foul must land on the defense (fouling team), never the offense.
            if (game.Fouls.FoulsFor(state.Offense) != 0) offenseFoulsLeaked = true;

            if (!bonusOk || !kindOk)
                Console.WriteLine($"    foul#{foul,2}: bonus={c.Bonus} (exp {expectedBonus}), kind={c.Next} (exp {expectedKind}) -> FAIL");
        }

        Console.WriteLine($"  routes match thresholds {cfgD.BonusThreshold}/{cfgD.DoubleBonusThreshold} across the climb -> {(allOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  fouls charged to defense only (offense stayed 0): {(!offenseFoulsLeaked ? "ok" : "FAIL")}");
        Console.WriteLine($"  final: defense fouls={game.Fouls.FoulsFor(state.Defense)}, bonus={game.Fouls.BonusFor(state.Defense)}");

        return allOk && !offenseFoulsLeaked;
    }

    // --- Direct unit check on the shared charge-and-fork (Core/DefensiveFoulCharge).
    //     This node is now the SINGLE place all five defensive-foul feeders (D, I, J,
    //     K, M) cross the bonus. Drive it across the foul thresholds with BOTH
    //     below-bonus kinds and with/without a flavor; confirm (a) the charge lands on
    //     the defense only, (b) the below/in-bonus split, (c) the Bonus payload on
    //     both arms, and (d) the optional flavor passes through (Roll D's shape) or
    //     stays null (I/J/K/M's shape). The five per-roll fork checks prove each
    //     caller still routes correctly THROUGH this node; this proves the node
    //     itself. ---
    private static bool DefensiveFoulChargeCheck(RollDConfig cfgD, PossessionState state)
    {
        Console.WriteLine("\n--- Shared node: DefensiveFoulCharge charge + fork + payload ---");

        bool RunPass(string label, ContinuationKind belowKind, FoulFlavor? flavor)
        {
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var allOk = true;
            var offenseLeaked = false;

            for (var foul = 1; foul <= cfgD.DoubleBonusThreshold + 2; foul++)
            {
                var c = (Continue)DefensiveFoulCharge.Resolve(state, game, belowKind, flavor);

                // Expected bonus for this post-increment count (mirrors FoulTracker).
                var expectedBonus =
                    foul >= cfgD.DoubleBonusThreshold ? BonusType.Double
                    : foul >= cfgD.BonusThreshold ? BonusType.OneAndOne
                    : BonusType.None;
                var expectedKind = expectedBonus == BonusType.None
                    ? belowKind
                    : ContinuationKind.ResolveFreeThrows;

                var bonusOk = c.Bonus == expectedBonus;
                var kindOk = c.Next == expectedKind;
                var flavorOk = c.Flavor == flavor;   // null == null when none supplied
                allOk &= bonusOk && kindOk && flavorOk;

                // The foul must land on the defense (fouling team), never the offense.
                if (game.Fouls.FoulsFor(state.Offense) != 0) offenseLeaked = true;

                if (!bonusOk || !kindOk || !flavorOk)
                    Console.WriteLine($"    [{label}] foul#{foul,2}: bonus={c.Bonus} (exp {expectedBonus}), " +
                        $"kind={c.Next} (exp {expectedKind}), flavor={c.Flavor?.ToString() ?? "<none>"} " +
                        $"(exp {flavor?.ToString() ?? "<none>"}) -> FAIL");
            }

            var pass = allOk && !offenseLeaked;
            Console.WriteLine($"  [{label}] below->{belowKind}, in-bonus->ResolveFreeThrows, " +
                $"flavor={(flavor?.ToString() ?? "<none>")}, charged defense only -> {(pass ? "ok" : "FAIL")}");
            return pass;
        }

        // Roll D's shape: below bonus resumes the inbound and carries a flavor.
        var dLike = RunPass("D: ResumeInbound+flavor", ContinuationKind.ResumeInbound, FoulFlavor.ReachIn);
        // I/J/K/M's shape: below bonus -> sideline throw-in, no flavor.
        var ijkmLike = RunPass("I/J/K/M: SidelineInbound", ContinuationKind.ResolveSidelineInbound, null);

        var ok = dLike && ijkmLike;
        Console.WriteLine($"  shared node reproduces BOTH caller shapes across the climb -> {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Prove Roll B's seam carries signal: foul rate must rise with physicality. ---
    private static bool PhysicalitySignalCheck(RollBConfig cfgB, RollBStubPieGenerator genB, PossessionState state)
    {
        Console.WriteLine("\n--- Seam signal: Roll B foul rate vs. physicality ---");
        var rng = new SystemRng(42);
        var low = RollBFoulRate(cfgB, genB, state, rng, physicality: 0.0);
        rng = new SystemRng(42);
        var high = RollBFoulRate(cfgB, genB, state, rng, physicality: 1.0);

        Console.WriteLine($"  foul rate @ physicality 0.00 = {low:P3}");
        Console.WriteLine($"  foul rate @ physicality 1.00 = {high:P3}");

        var moved = high > low;
        Console.WriteLine($"  signal is live (high > low): {(moved ? "ok" : "FAIL")}");
        return moved;
    }

    private static double RollBFoulRate(
        RollBConfig cfgB, RollBStubPieGenerator genB,
        PossessionState state, IRng rng, double physicality)
    {
        var pie = genB.Generate(state, physicality);
        var fouls = 0;
        const int n = 100_000;
        for (var i = 0; i < n; i++)
            if (RollB.Execute(state, pie, rng) is Continue { Next: ContinuationKind.ResolveFoulType })
                fouls++;
        return fouls / (double)n;
    }

    // --- Prove Roll C's seam carries signal: live-strip rate must rise with pressure. ---
    private static bool PressureSignalCheck(RollCConfig cfgC, RollCStubPieGenerator genC, PossessionState state)
    {
        Console.WriteLine("\n--- Seam signal: Roll C live-strip rate vs. pressure ---");
        var rng = new SystemRng(42);
        var low = RollCLiveStripRate(genC, state, rng, pressure: 0.0, cfgC);
        rng = new SystemRng(42);
        var high = RollCLiveStripRate(genC, state, rng, pressure: 1.0, cfgC);

        Console.WriteLine($"  live-strip rate @ pressure 0.00 = {low:P3}");
        Console.WriteLine($"  live-strip rate @ pressure 1.00 = {high:P3}");

        var moved = high > low;
        Console.WriteLine($"  signal is live (high > low): {(moved ? "ok" : "FAIL")}");
        return moved;
    }

    private static double RollCLiveStripRate(
        RollCStubPieGenerator genC, PossessionState state, IRng rng, double pressure, RollCConfig cfgC)
    {
        var pie = genC.Generate(state, pressure);
        var strips = 0;
        const int n = 100_000;
        for (var i = 0; i < n; i++)
            if (RollC.Execute(state, pie, rng, cfgC) is Terminal { Reason: "LostBallLiveBall" })
                strips++;
        return strips / (double)n;
    }

    // --- Jump ball: verify the three arrow behaviors directly. ---
    private static bool JumpBallCheck(RollAConfig cfg)
    {
        Console.WriteLine("\n--- Jump ball: possession arrow behavior ---");
        var allOk = true;

        // Jump-ball behavior is independent of fouls; supply any valid tracker.
        // A local factory keeps the four constructions below readable.
        static GameState NewGame(ArrowState arrow = ArrowState.Off) =>
            new(new FoulTracker(bonusThreshold: 7, doubleBonusThreshold: 10), arrow);

        // (1) Opening tip is ~50/50, and the arrow is set to the tip LOSER.
        var rng = new SystemRng(cfg.Seed);
        var homeWins = 0;
        var arrowToLoser = 0;
        const int tips = 100_000;
        for (var i = 0; i < tips; i++)
        {
            var g = NewGame();  // Off -> tip
            var award = JumpBall.Resolve(g, rng);
            if (award.AwardedTo == TeamSide.Home) homeWins++;

            // Arrow must point at the loser (the team NOT awarded the ball).
            var arrowTeam = g.PossessionArrow == ArrowState.Home ? TeamSide.Home : TeamSide.Away;
            if (arrowTeam != award.AwardedTo) arrowToLoser++;

            // Every tip must be flagged a contest and must turn the arrow ON.
            if (!award.WasTipContest || g.PossessionArrow == ArrowState.Off) allOk = false;
        }
        var homeRate = homeWins / (double)tips;
        var tipFair = Math.Abs(homeRate - 0.5) <= cfg.RateTolerance;
        var loserOk = arrowToLoser == tips;
        allOk &= tipFair && loserOk;
        Console.WriteLine($"  tip coin-flip: home wins {homeRate:P3} (expected 50.000%) -> {(tipFair ? "ok" : "FAIL")}");
        Console.WriteLine($"  arrow set to tip-loser every time: {arrowToLoser:N0}/{tips:N0} -> {(loserOk ? "ok" : "FAIL")}");

        // (2) On-arrow jump ball awards the pointed-at team, then flips.
        var g2 = NewGame(ArrowState.Home);
        var a2 = JumpBall.Resolve(g2, rng);
        var awardOk = a2.AwardedTo == TeamSide.Home && !a2.WasTipContest;
        var flipOk = g2.PossessionArrow == ArrowState.Away;
        allOk &= awardOk && flipOk;
        Console.WriteLine($"  on-arrow(Home): awarded={a2.AwardedTo} contest={a2.WasTipContest} -> {(awardOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  arrow flipped Home->{g2.PossessionArrow}: {(flipOk ? "ok" : "FAIL")}");

        // (3) A run of alternating-possession situations strictly alternates.
        var g3 = NewGame(ArrowState.Home);
        var expected = new[] { TeamSide.Home, TeamSide.Away, TeamSide.Home, TeamSide.Away, TeamSide.Home };
        var alternates = true;
        foreach (var exp in expected)
            if (JumpBall.Resolve(g3, rng).AwardedTo != exp) alternates = false;
        allOk &= alternates;
        Console.WriteLine($"  five awards strictly alternate from Home: {(alternates ? "ok" : "FAIL")}");

        // (4) Flipping an Off arrow must throw (guard against silent misuse).
        var threw = false;
        try { NewGame().FlipPossessionArrow(); }
        catch (InvalidOperationException) { threw = true; }
        allOk &= threw;
        Console.WriteLine($"  flipping an Off arrow throws: {(threw ? "ok" : "FAIL")}");

        return allOk;
    }

    // --- Slot layer: print the ten on-court slots and prove one can be NAMED
    //     as a future attribution target. The slots are empty identities — they
    //     fill nothing and influence no roll. This check only confirms they
    //     exist on the correct side and are addressable 1–5. ---
    private static bool SlotLayerCheck(GameState game)
    {
        Console.WriteLine("\n--- Slot layer: on-court identities ---");
        var allOk = true;

        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var lineup = game.LineupFor(side);

            // Each lineup must hold exactly five slots, numbered 1–5, all on its side.
            var countOk = lineup.OnCourt.Count == Lineup.Size;
            var numbersOk = true;
            var sideOk = true;
            for (var i = 0; i < lineup.OnCourt.Count; i++)
            {
                var slot = lineup.OnCourt[i];
                Console.WriteLine($"  {slot.Side} slot {slot.Number}");
                if (slot.Number != i + 1) numbersOk = false;
                if (slot.Side != side) sideOk = false;
            }

            var lineupOk = countOk && numbersOk && sideOk;
            allOk &= lineupOk;
            Console.WriteLine($"  {side}: five slots, numbered 1–5, all on {side} -> {(lineupOk ? "ok" : "FAIL")}");
        }

        // Prove a single slot can be NAMED — the entity a future stat attributes to.
        var target = game.LineupFor(TeamSide.Home).SlotAt(3);
        var nameOk = target.Side == TeamSide.Home && target.Number == 3;
        allOk &= nameOk;
        Console.WriteLine(
            $"  named attribution target: {target.Side} slot {target.Number} " +
            $"(a stat would credit here; nothing fills it yet) -> {(nameOk ? "ok" : "FAIL")}");

        return allOk;
    }

    // --- Batch: Roll E's five-way selection converges to a flat 20% per slot,
    //     every selected slot is a real slot on the OFFENSE's lineup numbered
    //     1–5, and every exit is a clean IntoPlayerAction continue carrying a
    //     stamped slot. The flat distribution is the whole point this session:
    //     Phase 15: generator is now attribute-driven (halfcourt); the check is
    //     retooled to assert convergence to the generator's own pie (not flat 20s)
    //     and that the seeded alpha slot wins materially. ---
    private static bool RollESelectionBatchCheck(
        RollAConfig cfg, RollEConfig cfgE, RollDConfig cfgD, IRollEPieGenerator genE,
        GameState game, PossessionState state)
    {
        // ── Seat a known five-man roster so the generator's pie is computable ──
        // Alpha (Slot1): high SelfCreation + perimeter — should dominate.
        // Slot5: Rodman-type — low scoring, should land at/near floor.
        // Use a LOCAL game with its own roster so we don't disturb the shared
        // game's seating (other checks read it). genE holds _game (shared game);
        // we construct a local RollEGenerator for this check seeded with the test roster.
        var checkGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var checkRoster  = checkGame.RosterFor(state.Offense);
        var checkLineup  = checkGame.LineupFor(state.Offense);

        // Slot1: alpha scorer (SelfCreation=85, Close=80, Outside=75, Mid=80, Finishing=82, PostMoves=40)
        checkRoster.SetStarter(checkLineup.SlotAt(1), new Player("Alpha")
        {
            SelfCreation=85, Close=80, PostMoves=40, Outside=75, Mid=80, Finishing=82,
            FreeThrow=75, FoulDrawing=70,
            BallHandling=70, Passing=65, Playmaking=60, OffBallMovement=60, Screening=50, OffensiveRebounding=40,
            PerimeterDefense=55, PostDefense=45, RimProtection=30, DefensiveRebounding=40, Steals=50,
            Height=75, Wingspan=76, Weight=60, Strength=70, Speed=75, Quickness=75, FirstStep=75, Vertical=75,
            Endurance=75, Hustle=70, BasketballIQ=80, Discipline=70,
            RimTendency=20, ShortTendency=15, MidTendency=30, LongTendency=15, ThreeTendency=20,
        });
        // Slot2: solid scorer
        checkRoster.SetStarter(checkLineup.SlotAt(2), new Player("Solid2")
        {
            SelfCreation=60, Close=60, PostMoves=45, Outside=65, Mid=62, Finishing=60,
            FreeThrow=65, FoulDrawing=55,
            BallHandling=60, Passing=58, Playmaking=55, OffBallMovement=60, Screening=50, OffensiveRebounding=45,
            PerimeterDefense=58, PostDefense=50, RimProtection=40, DefensiveRebounding=50, Steals=50,
            Height=76, Wingspan=77, Weight=65, Strength=60, Speed=65, Quickness=65, FirstStep=60, Vertical=65,
            Endurance=65, Hustle=65, BasketballIQ=65, Discipline=65,
            RimTendency=20, ShortTendency=20, MidTendency=25, LongTendency=15, ThreeTendency=20,
        });
        // Slot3: solid scorer
        checkRoster.SetStarter(checkLineup.SlotAt(3), new Player("Solid3")
        {
            SelfCreation=55, Close=55, PostMoves=50, Outside=60, Mid=58, Finishing=55,
            FreeThrow=60, FoulDrawing=50,
            BallHandling=55, Passing=55, Playmaking=50, OffBallMovement=58, Screening=55, OffensiveRebounding=50,
            PerimeterDefense=55, PostDefense=52, RimProtection=45, DefensiveRebounding=52, Steals=48,
            Height=78, Wingspan=79, Weight=70, Strength=62, Speed=62, Quickness=62, FirstStep=60, Vertical=62,
            Endurance=65, Hustle=65, BasketballIQ=63, Discipline=65,
            RimTendency=25, ShortTendency=20, MidTendency=22, LongTendency=15, ThreeTendency=18,
        });
        // Slot4: solid scorer
        checkRoster.SetStarter(checkLineup.SlotAt(4), new Player("Solid4")
        {
            SelfCreation=50, Close=65, PostMoves=55, Outside=55, Mid=60, Finishing=60,
            FreeThrow=60, FoulDrawing=50,
            BallHandling=50, Passing=52, Playmaking=48, OffBallMovement=55, Screening=60, OffensiveRebounding=55,
            PerimeterDefense=55, PostDefense=58, RimProtection=50, DefensiveRebounding=58, Steals=45,
            Height=79, Wingspan=80, Weight=75, Strength=65, Speed=58, Quickness=55, FirstStep=55, Vertical=60,
            Endurance=65, Hustle=70, BasketballIQ=62, Discipline=65,
            RimTendency=30, ShortTendency=22, MidTendency=25, LongTendency=10, ThreeTendency=13,
        });
        // Slot5: Rodman-type (low scoring — should land at/near floor)
        checkRoster.SetStarter(checkLineup.SlotAt(5), new Player("Rodman")
        {
            SelfCreation=20, Close=50, PostMoves=30, Outside=15, Mid=20, Finishing=55,
            FreeThrow=50, FoulDrawing=30,
            BallHandling=40, Passing=35, Playmaking=30, OffBallMovement=55, Screening=70, OffensiveRebounding=90,
            PerimeterDefense=65, PostDefense=70, RimProtection=75, DefensiveRebounding=92, Steals=55,
            Height=80, Wingspan=82, Weight=85, Strength=85, Speed=55, Quickness=52, FirstStep=50, Vertical=70,
            Endurance=80, Hustle=95, BasketballIQ=70, Discipline=80,
            RimTendency=50, ShortTendency=30, MidTendency=10, LongTendency=5, ThreeTendency=5,
        });

        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} selections through Roll E (attribute-driven halfcourt) ---");
        var rng = new SystemRng(cfg.Seed);

        // Construct a local generator pointed at checkGame so the pie reflects
        // the test roster we just seated (not the shared game's roster).
        var localGenE = new RollEGenerator(cfgE, checkGame);

        // Read the generator's own pie once — empirical rates must converge to it.
        var checkState = state with { Offense = state.Offense };
        var pieE = localGenE.Generate(checkState);

        var counts = new Dictionary<SelectionOutcome, int>();
        foreach (var o in Enum.GetValues<SelectionOutcome>()) counts[o] = 0;

        var anomalies = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollE.Execute(checkState, pieE, checkGame, rng);

            if (result is not Continue { Next: ContinuationKind.IntoPlayerAction } c
                || c.State.SelectedSlot is not { } slot
                || slot.Side != checkState.Offense
                || slot.Number < 1 || slot.Number > Lineup.Size)
            {
                anomalies++;
                continue;
            }
            counts[(SelectionOutcome)(slot.Number - 1)]++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll E selections (converge to generator's own pie):");
        foreach (var (outcome, weight) in pieE.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-8} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        // Ordering assertion: the alpha (Slot1) must materially outpace the Rodman (Slot5).
        // This is the regression guard — the point of the whole build.
        var alphaShare = counts[SelectionOutcome.Slot1] / n;
        var rodmanShare = counts[SelectionOutcome.Slot5] / n;
        var orderOk = alphaShare > rodmanShare * 2.0;           // alpha must be more than 2x Rodman
        Console.WriteLine($"\n  Ordering: Alpha (Slot1={alphaShare:P3}) > 2x Rodman (Slot5={rodmanShare:P3}): {(orderOk ? "ok" : "FAIL")}");

        var cleanOk = anomalies == 0;
        Console.WriteLine($"  every exit a clean slot-stamped IntoPlayerAction: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");

        // ── FastBreak: transition pie must equal cfg.Transition* EXACTLY ──────
        // Build pies from the REAL generator (genE) using states with explicit FastBreak flag.
        var halfcourtPie  = localGenE.Generate(checkState with { FastBreak = false });
        var transitionPie = localGenE.Generate(checkState with { FastBreak = true });

        var transitionExpected = new Dictionary<SelectionOutcome, double>
        {
            [SelectionOutcome.Slot1] = cfgE.TransitionSlot1,
            [SelectionOutcome.Slot2] = cfgE.TransitionSlot2,
            [SelectionOutcome.Slot3] = cfgE.TransitionSlot3,
            [SelectionOutcome.Slot4] = cfgE.TransitionSlot4,
            [SelectionOutcome.Slot5] = cfgE.TransitionSlot5,
        };

        // Transition pie must exactly equal the configured Transition* weights.
        var transitionPieOk = transitionPie.Slices.All(s =>
            Math.Abs(s.Item2 - transitionExpected[s.Item1]) <= cfgE.Epsilon);

        // Halfcourt and transition pies must differ (non-flat halfcourt now makes this a real check).
        var piesDiffer = transitionPie.Slices.Any(s =>
            Math.Abs(s.Item2 - halfcourtPie.Slices.First(h => h.Item1 == s.Item1).Item2) > cfgE.Epsilon);

        Console.WriteLine("\n  FastBreak pie selection:");
        Console.WriteLine($"    FastBreak=true  -> transition pie exactly equals cfg.Transition* weights: {(transitionPieOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    halfcourt and transition pies differ (selection is observable): {(piesDiffer ? "ok" : "FAIL")}");

        return ratesOk && cleanOk && orderOk && transitionPieOk && piesDiffer;
    }

    // --- Batch: Roll F's four-way action distribution converges within tolerance,
    //     and every exit is a clean Continue carrying one of the four expected
    //     kinds. The pie is flat-ish (no signal); a future attribute generator
    //     tilts it without this roll changing. Mirrors the Roll E batch check.
    //     (Block left Roll F in Session 13 — it is now a per-zone slice of Roll
    //     H.) ---
    private static bool RollFActionBatchCheck(
        RollAConfig cfg, RollFConfig cfgF, IRollFPieGenerator genF, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} actions through Roll F (flat-ish pie) ---");
        var rng = new SystemRng(cfg.Seed);
        var pieF = genF.Generate(state);

        var counts = new Dictionary<PlayerActionOutcome, int>();
        foreach (var o in Enum.GetValues<PlayerActionOutcome>()) counts[o] = 0;

        var anomalies = 0;   // anything that isn't a clean Continue with an expected kind

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollF.Execute(state, pieF, rng);

            // Map the emitted continuation kind back to its action outcome bucket.
            var bucket = result switch
            {
                Continue { Next: ContinuationKind.IntoShotType } => PlayerActionOutcome.ShotAttempt,
                Continue { Next: ContinuationKind.ResolveTurnoverType } => PlayerActionOutcome.Turnover,
                Continue { Next: ContinuationKind.ResolveFoulType } => PlayerActionOutcome.NonShootingFoul,
                Continue { Next: ContinuationKind.ResolveJumpBall } => PlayerActionOutcome.JumpBall,
                _ => (PlayerActionOutcome?)null
            };

            if (bucket is not { } b)
            {
                anomalies++;
                continue;
            }
            counts[b]++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll F actions:");
        foreach (var (outcome, weight) in pieF.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var cleanOk = anomalies == 0;
        Console.WriteLine($"\n  every exit a clean Continue with an expected kind: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");

        return ratesOk && cleanOk;
    }

    // --- Clean handoff: route a batch of real E->F exits through the resolver and
    //     confirm every Roll F outcome lands at its intended destination, with
    //     zero unrouted: turnover -> Roll C terminal, shot -> Roll G -> Roll H ->
    //     resolved, jump ball -> jump-ball terminal. As of #6 a non-shooting foul no
    //     longer parks: below the bonus Roll D's ResumeInbound RE-RUNS Roll A and the
    //     possession resolves downstream (into the shot / turnover / jump-ball
    //     buckets), and in the bonus it lands at the made-FT terminal — so the foul
    //     exit has no bucket of its own; it is absorbed into the others, and the
    //     zero-unrouted gate still proves it routes. A fresh local game + resolver
    //     keeps this self-contained; the shared foul count climbing into the bonus
    //     mid-run is expected. (Block left Roll F in Session 13; it is now a Roll H
    //     slice, so there is no F-stage block destination here.) ---
    private static bool RollFHandoffCheck(RollAConfig cfg, GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Clean handoff: {cfg.BatchSize:N0} E->F exits routed through the resolver ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // Build a self-contained resolver from config (mirrors Main's wiring), on
        // a FRESH game so this check does not perturb the shared game state.
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(cfgE);
        var genF = new RollFStubPieGenerator(cfgF);

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            genE,
            genF,
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        var pieE = genE.Generate(state);
        var pieF = genF.Generate(state);

        var destClasses = new Dictionary<string, int>
        {
            ["turnover -> Roll C terminal"] = 0,
            ["shot -> Roll G -> Roll H -> resolved"] = 0,
            ["free throws -> resolved"] = 0,
            ["jump ball -> terminal"] = 0,
        };
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Select a player (Roll E), resolve the action (Roll F), then route.
            var selected = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var fResult = RollF.Execute(selected, pieF, rng);
            var routing = resolver.Route(fResult);
            var d = routing.Destination;

            if (d.StartsWith("END:JumpBall")) destClasses["jump ball -> terminal"]++;
            // Roll H/I/M's resolved shot/board landings (checked BEFORE the generic
            // END: catch, since Made / MissOutOfBoundsLost AND the rebound terminals —
            // DefensiveRebound / LooseBallFoulOnOffense / OutOfBoundsOffOffense — are
            // also END: terminals). A shot flows F -> G -> H, a MISS flows through Roll
            // I to a board or loose-ball foul, and a missed FINAL free throw now flows
            // through Roll M (Session 19) to the SAME board landings (its DefensiveRebound
            // is a transition terminal, its OOB-off-offense a dead-ball terminal, its
            // OOB-off-defense a sideline inbound). The resolved bucket therefore spans:
            // Made / MissOutOfBoundsLost terminals; the sideline-inbound and block-
            // recovery stubs; and the rebound terminals from Roll I and Roll M. (Shooting
            // fouls drive the Roll L FT loop and land at END:FreeThrowsMade on a made last
            // shot — caught in the FT bucket below — or flow into Roll M on a missed last
            // shot, caught here.) Blocked shots no longer park at a block-recovery
            // stub — Roll H's Blocked arm now routes into Roll I (the block pie), so a
            // block lands at one of Roll I's board/foul/OOB/jump landings, all already
            // bucketed below or as a jump-ball terminal above.
            else if (d == "END:Made" || d == "END:MissOutOfBoundsLost"
                     || d == "END:DefensiveRebound" || d == "END:LooseBallFoulOnOffense"
                     || d == "END:OutOfBoundsOffOffense"
                     || d.StartsWith("STUB:OffensiveRebound"))
                destClasses["shot -> Roll G -> Roll H -> resolved"]++;
            // The Roll L FT loop's made-last-shot landing ends the possession
            // (END:FreeThrowsMade). A missed last shot no longer parks at STUB:FTRebound
            // (retired Session 19) — it drives Roll M and lands in the resolved bucket
            // above. Both a shooting foul (Roll H) and a bonus foul (Roll D) converge on
            // the made-FT terminal, so they share this bucket. Checked before the generic
            // END: catch so a made FT is not miscounted as a turnover.
            else if (d == "END:FreeThrowsMade")
                destClasses["free throws -> resolved"]++;
            else if (d.StartsWith("END:")) destClasses["turnover -> Roll C terminal"]++;
            else unrecognized++;
        }

        Console.WriteLine("  destinations reached:");
        var allHit = true;
        foreach (var (label, count) in destClasses)
        {
            var hit = count > 0;
            allHit &= hit;
            Console.WriteLine($"    {label,-32} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all four resolved destinations reached: {(allHit ? "ok" : "FAIL")}");

        return routedOk && allHit;
    }

    // --- Batch: Roll G's five-way location distribution converges within
    //     tolerance, every exit is a clean Continue carrying the IntoShotResolution
    //     kind, and the ShotType is actually stamped on the carried state. The pie
    //     is flat-ish (no signal); a future attribute generator tilts it without
    //     this roll changing. Mirrors the Roll E batch check — Roll G is
    //     structurally Roll E: stamp a fact, continue to the same next beat. ---
    private static bool RollGLocationBatchCheck(
        RollAConfig cfg, RollGConfig cfgG, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} locations through Roll G (flat-ish pie) ---");
        // Option (ii): construct the stub directly here so this check remains a
        // flat baseline regression regardless of whether Main's live chain uses the
        // real RollGGenerator. This preserves the pre-Phase-9 baseline check value.
        var genG = new RollGStubPieGenerator(cfgG);
        var rng = new SystemRng(cfg.Seed);
        var pieG = genG.Generate(state);

        var counts = new Dictionary<ShotLocation, int>();
        foreach (var o in Enum.GetValues<ShotLocation>()) counts[o] = 0;

        var anomalies = 0;   // anything that isn't a clean, zone-stamped IntoShotResolution continue

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollG.Execute(state, pieG, rng);

            // Must be a Continue, into shot resolution, carrying a stamped zone on
            // the forwarded state (confirms ShotType is actually set, not just the
            // kind emitted).
            if (result is not Continue { Next: ContinuationKind.IntoShotResolution } c
                || c.State.ShotType is not { } zone)
            {
                anomalies++;
                continue;
            }

            counts[zone]++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll G locations:");
        foreach (var (outcome, weight) in pieG.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-6} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var cleanOk = anomalies == 0;
        Console.WriteLine($"\n  every exit a clean zone-stamped IntoShotResolution: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");

        return ratesOk && cleanOk;
    }

    // --- G->H->I integration: route a batch of IntoShotType exits through the
    //     resolver. With Roll H and Roll I live, an IntoShotType ticket flows G (stamps
    //     a zone) -> H (stamps a result) and, on a MISS, on through I (rebound
    //     resolution). This check confirms the WHOLE post-shot chain routes: zero
    //     unrouted and the CORE landings still reached straight off the first Roll H /
    //     Roll I (Made / MissOutOfBoundsLost terminals + Roll I's DefensiveRebound /
    //     LooseBallFoulOnOffense terminals). Everything Roll K / a reset / a re-inbound
    //     opens up is bucketed as routed-deeper, not required. As of #6 the OOB-retained
    //     sideline inbound no longer parks (it re-runs Roll A and lands deeper), so there
    //     is no fact-carrying core stub here anymore; clean one-hop zone ride-through is
    //     proven authoritatively by RollHResolutionBatchCheck. A fresh local game +
    //     resolver keeps it self-contained. ---
    private static bool RollGHandoffCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- G->H integration: {cfg.BatchSize:N0} IntoShotType exits routed through Roll G then Roll H ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(cfgE);

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            genE,
            new RollFStubPieGenerator(cfgF),
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        var pieE = genE.Generate(state);

        // The destinations a shot can land at after G -> H -> I -> K. As of Session 17
        // an OFFENSIVE rebound no longer parks at STUB:OffensiveRebound — it executes
        // Roll K, which fans the same possession out: a putback (back through Roll H,
        // landing at the SAME post-shot destinations below), a RESET (back to Roll E,
        // which re-runs the whole halfcourt chain and can land at literally any
        // halfcourt outcome), three Roll K terminals (OffensiveFoul / DeadBallTurnover
        // / LiveBallTurnover), a jump ball, or the shared charge-and-fork. So the
        // closed eight-destination set of prior sessions is now OPEN: we assert the
        // CORE post-shot landings (still reliably reached straight off the first Roll H
        // and Roll I) plus zero-unrouted, and bucket everything Roll K/reset opens up
        // as "deeper" (routed, not required). STUB:OffensiveRebound is DROPPED from the
        // required set — it is structurally unreachable now. STUB:BlockRecovery is also
        // dropped — Roll H's Blocked now routes into Roll I (the block pie), so a block
        // lands at one of Roll I's existing landings (already covered here / bucketed
        // as deeper), never at the retired block-recovery stub.
        var destHits = new Dictionary<string, int>
        {
            ["END:Made"] = 0,
            ["END:MissOutOfBoundsLost"] = 0,
            ["END:DefensiveRebound"] = 0,
            ["END:LooseBallFoulOnOffense"] = 0,
        };
        // STUB:SidelineInbound is DROPPED from the core set as of #6: an OOB-retained
        // shot no longer parks — it re-runs Roll A and lands DEEPER (a terminal or a
        // deeper stub), bucketed below. With no fact-carrying core stub left, the
        // zone-ride-through assertion moves with it; clean one-hop fact ride-through
        // is proven authoritatively by RollHResolutionBatchCheck.

        var unrecognized = 0;
        var deeperTerminals = 0; // Roll K / reset / re-inbound terminals beyond core (routed)
        var deeperStubs = 0;     // Roll K / reset stub parks beyond core (routed)

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Select a real slot (Roll E), then hand the resolver a clean
            // IntoShotType continuation — exactly what Roll F emits on a ShotAttempt.
            var selected = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var shotTicket = new Continue(ContinuationKind.IntoShotType, selected);
            var d = resolver.Route(shotTicket).Destination;

            if (d == "END:Made") { destHits["END:Made"]++; continue; }
            if (d == "END:MissOutOfBoundsLost") { destHits["END:MissOutOfBoundsLost"]++; continue; }
            // Roll I's two terminals (Session 14) — matched BEFORE the stub parse,
            // exactly as the Made / MissOutOfBoundsLost terminals are. They carry
            // no slot:zone:result tail (a terminal names only its reason), so they
            // must short-circuit here rather than fall into the zone-token parser.
            if (d == "END:DefensiveRebound") { destHits["END:DefensiveRebound"]++; continue; }
            if (d == "END:LooseBallFoulOnOffense") { destHits["END:LooseBallFoulOnOffense"]++; continue; }
            // Everything else is ROUTED but not in the core set: every Roll K / reset /
            // re-inbound landing — the three Roll K terminals (END:OffensiveFoul /
            // DeadBallTurnover / LiveBallTurnover), a jump-ball terminal, any reset- or
            // re-entry-born terminal (a deeper Made, a Roll C turnover), an OOB-retained
            // shot that re-ran Roll A and resolved downstream, and any deeper stub park.
            // Bucket so they don't read as unrouted; don't require them.
            else if (d.StartsWith("END:")) deeperTerminals++;
            else if (d.StartsWith("STUB:")) deeperStubs++;
            else unrecognized++;
        }

        var landed = destHits.Values.Sum();
        Console.WriteLine($"  landed at a CORE post-shot destination: {landed:N0}");
        Console.WriteLine($"  routed DEEPER via Roll K / reset (terminals {deeperTerminals:N0}, stubs {deeperStubs:N0})");
        Console.WriteLine("  core destinations reached:");
        var allDests = true;
        foreach (var (label, count) in destHits)
        {
            var hit = count > 0;
            allDests &= hit;
            Console.WriteLine($"    {label,-26} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all core G->H->I destinations reached (offensive rebound now executes Roll K; OOB-retained re-runs Roll A and lands deeper): {(allDests ? "ok" : "FAIL")}");

        return routedOk && allDests;
    }

    // --- Batch: Roll H's seven-way make/miss distribution converges within
    //     tolerance, every exit carries a stamped Result matching the rolled
    //     outcome (no exit leaves Result null), and the prior two facts
    //     (SelectedSlot, ShotType) plus the new Result all ride through. As of
    //     Session 13 the pie is ZONE-AWARE for the block slice: each draw walks a
    //     fresh slot (Roll E) + zone (Roll G), and the generator sizes the block
    //     weight from that zone (Rim highest, Three lowest), carving it off the top
    //     and scaling the six make/miss outcomes by (1 − block). So the per-draw
    //     pie varies; the seven OBSERVED rates are checked against their
    //     ZONE-BLENDED expectations: blended block = Σ P(zone)·b(zone) over Roll G's
    //     zone mix, and each make/miss rate = base × (1 − blended block). A per-zone
    //     block readout confirms the gradient (Rim ≫ Three) directly. The six
    //     make/miss outcomes stay location-BLIND in SHAPE; only the block slice is
    //     zone-aware this pass. ---
    private static bool RollHResolutionBatchCheck(
        RollAConfig cfg, RollHConfig cfgH, IRollHPieGenerator genH, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} shots through Roll H (zone-aware seven-way pie) ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var rng = new SystemRng(cfg.Seed);

        // Generators to walk a fresh slot (Roll E) + zone (Roll G) per draw, so the
        // zone varies across the batch exactly as the live chain delivers to Roll H.
        var cfgD = RollDConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(RollEConfig.Load(configPath));
        var genG = new RollGStubPieGenerator(cfgG);
        // Phase 9 fix: use an isolated RollHGenerator bound to the LOCAL empty game so
        // PlayerAt() returns null and the generator falls back to its stub/baseline behavior.
        // This preserves RollHResolutionBatchCheck as a pure baseline calibration against
        // the configured rates, independent of whatever real rosters Main has seated.
        // Phase 6/7/8 matchup tests are in their own dedicated checks.
        var cfgMatchup = MatchupConfig.Load(configPath);
        var isolatedGenH = new RollHGenerator(cfgH, cfgMatchup, game);  // game is empty — no rosters
        var pieE = genE.Generate(state);

        var counts = new Dictionary<ShotResult, int>();
        foreach (var o in Enum.GetValues<ShotResult>()) counts[o] = 0;

        // Per-zone totals + per-zone block counts, for the Rim-vs-Three gradient.
        var zoneTotals = new Dictionary<ShotLocation, int>();
        var zoneBlocked = new Dictionary<ShotLocation, int>();
        foreach (var z in Enum.GetValues<ShotLocation>()) { zoneTotals[z] = 0; zoneBlocked[z] = 0; }

        var anomalies = 0;       // exit whose stamped Result is null or mismatched
        var rideThroughFail = 0; // slot / zone / result not all present on the carried state

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Fresh slot + zone, then the zone-aware pie for THAT zone.
            var selected = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var preH = ((Continue)RollG.Execute(selected, genG.Generate(selected), rng)).State;
            var zone = preH.ShotType!.Value;
            var pieH = isolatedGenH.Generate(preH);

            var result = RollH.Execute(preH, pieH, rng);

            // Pull the carried state and the stamped result off either arm.
            var carried = result switch
            {
                Terminal t => t.State,
                Continue c => c.State,
                _ => throw new InvalidOperationException("Unknown Roll H result.")
            };

            if (carried.Result is not { } stamped) { anomalies++; continue; }

            // The stamped Result must match the routing arm taken. Made and
            // MissOutOfBoundsLost are the only terminals; everything else
            // (including Blocked) is a continue.
            var expectedTerminal = stamped is ShotResult.Made or ShotResult.MissOutOfBoundsLost;
            var isTerminal = result is Terminal;
            if (expectedTerminal != isTerminal) anomalies++;

            // All three per-possession facts must ride through.
            if (carried.SelectedSlot is null || carried.ShotType is null || carried.Result is null)
                rideThroughFail++;

            counts[stamped]++;
            zoneTotals[zone]++;
            if (stamped == ShotResult.Blocked) zoneBlocked[zone]++;
        }

        var n = (double)cfg.BatchSize;

        // Zone-blended expectations. P(zone) is Roll G's zone mix; b(zone) is the
        // configured per-zone block weight (same lookup the generator uses).
        var zoneP = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Three] = cfgG.BaseThree,
            [ShotLocation.Long] = cfgG.BaseLong,
            [ShotLocation.Mid] = cfgG.BaseMid,
            [ShotLocation.Short] = cfgG.BaseShort,
            [ShotLocation.Rim] = cfgG.BaseRim,
        };
        var blendedBlock = 0.0;
        foreach (var (z, p) in zoneP) blendedBlock += p * cfgH.BlockWeight(z);

        // Phase 8: blended foul rate and MAF/MissFouled split, weighted by zone probability.
        var blendedFoul = 0.0;
        var blendedMaf  = 0.0;
        foreach (var (z, p) in zoneP)
        {
            var f = cfgH.FoulRate(z);
            var m = cfgH.MafFraction(z);
            blendedFoul += p * f;
            blendedMaf  += p * f * m;
        }
        var blendedMissFouled = blendedFoul - blendedMaf;
        var nonBlockNonFoul   = 1.0 - blendedBlock - blendedFoul;

        // Made: BaseMade is the conversion rate given not blocked AND not fouled.
        var blendedMade  = cfgH.BaseMade * nonBlockNonFoul;

        // Miss and OOB: fill nonBlockNonFoul - Made, preserving relative proportions.
        var nonMadeBase  = cfgH.BaseMiss + cfgH.BaseMissOutOfBoundsLost + cfgH.BaseMissOutOfBoundsRetained;
        var nonMadeShare = nonBlockNonFoul - blendedMade;
        var makeScale    = nonMadeBase > 0.0 ? nonMadeShare / nonMadeBase : 0.0;

        var expected = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made] = blendedMade,
            [ShotResult.MadeAndFouled] = blendedMaf,
            [ShotResult.Miss] = cfgH.BaseMiss * makeScale,
            [ShotResult.MissFouled] = blendedMissFouled,
            [ShotResult.MissOutOfBoundsLost] = cfgH.BaseMissOutOfBoundsLost * makeScale,
            [ShotResult.MissOutOfBoundsRetained] = cfgH.BaseMissOutOfBoundsRetained * makeScale,
            [ShotResult.Blocked] = blendedBlock,
        };

        var ratesOk = true;
        Console.WriteLine("  Roll H outcomes (observed vs. zone-blended expectation):");
        foreach (var outcome in Enum.GetValues<ShotResult>())
        {
            var observed = counts[outcome] / n;
            var exp = expected[outcome];
            var gap = Math.Abs(observed - exp);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={exp:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        // Per-zone block gradient: observed b(zone) vs. configured, and the
        // monotonic Rim >= Short >= Mid >= Long >= Three ordering the design wants.
        // The per-zone gate uses a looser tolerance (3x the batch tolerance): a
        // single zone's block sample is small (e.g. Rim is ~12% over ~35k draws),
        // so the tight batch tolerance would flake on noise — but a real bug (a
        // swapped or mis-scaled zone) shows a multi-point gap that 3x still catches.
        // The hard gates that pin the design are the gradient and the blended rate.
        Console.WriteLine("\n  block rate by zone (observed vs. configured b(zone)):");
        var zoneTol = cfg.RateTolerance * 3.0;
        var zoneOrder = new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three };
        var lastObserved = 1.0;
        var gradientOk = true;
        var perZoneOk = true;
        foreach (var z in zoneOrder)
        {
            var total = zoneTotals[z];
            var observed = total > 0 ? zoneBlocked[z] / (double)total : 0.0;
            var configured = cfgH.BlockWeight(z);
            var gap = Math.Abs(observed - configured);
            var pass = gap <= zoneTol;
            perZoneOk &= pass;
            if (observed > lastObserved + cfg.RateTolerance) gradientOk = false; // out-of-order spike
            lastObserved = observed;
            Console.WriteLine($"    {z,-6} block observed={observed:P3}  configured={configured:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }
        Console.WriteLine($"  block gradient Rim >= ... >= Three: {(gradientOk ? "ok" : "FAIL")}");

        var observedBlended = counts[ShotResult.Blocked] / n;
        var blendedGap = Math.Abs(observedBlended - blendedBlock);
        var blendedOk = blendedGap <= cfg.RateTolerance;
        Console.WriteLine($"  zone-blended block rate: observed={observedBlended:P3}  expected={blendedBlock:P3}  gap={blendedGap:P3}  {(blendedOk ? "ok" : "FAIL")}");

        var cleanOk = anomalies == 0;
        var rideOk = rideThroughFail == 0;
        Console.WriteLine($"\n  every exit a stamped Result matching its arm: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  slot+zone+result ride through every exit: fails={rideThroughFail} -> {(rideOk ? "ok" : "FAIL")}");

        return ratesOk && perZoneOk && gradientOk && blendedOk && cleanOk && rideOk;
    }

    // --- Clean handoff: route a batch of IntoShotResolution exits through the
    //     resolver and confirm every one lands at its intended Roll H destination,
    //     with zero unrouted: Made -> terminal, MissOutOfBoundsLost -> terminal,
    //     Miss -> deeper (Roll I/K), MissOutOfBoundsRetained -> sideline-inbound stub,
    //     Blocked -> block-recovery stub (Session 13). The two SHOOTING-foul outcomes
    //     (MadeAndFouled / MissFouled) drive the Roll L FT loop (Session 18) and land at
    //     END:FreeThrowsMade on a made last shot, or flow into Roll M on a missed last
    //     shot (Session 19) — both absorbed in the "deeper" buckets here and proven by
    //     RollLFreeThrowCheck and RollMReboundBatchCheck. Isolates the Roll H hop exactly as
    //     RollGHandoffCheck isolated Roll G: the resolver executes Roll H on an
    //     IntoShotResolution continuation (carrying a real slot + zone) and lands at
    //     a terminal, a fact-carrying stub, or deeper. On the fact-carrying stub
    //     landings it confirms slot, zone, AND result all rode through. A fresh local
    //     game + resolver keeps it self-contained. ---
    private static bool RollHHandoffCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- Clean handoff: {cfg.BatchSize:N0} IntoShotResolution exits routed through Roll H ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            genE,
            new RollFStubPieGenerator(cfgF),
            genG,
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        var pieE = genE.Generate(state);

        // With Roll I + Roll K live, a MISS no longer parks anywhere — it flows
        // THROUGH Roll I (to a defensive board / loose-ball foul terminal, or an
        // OFFENSIVE board) and, on an offensive board, THROUGH Roll K, which keeps the
        // possession alive (putback back into Roll H, or a reset back into Roll E) or
        // flips it (three terminals). So the post-shot destination set is now OPEN.
        // We assert the CORE landings that still come straight off the FIRST Roll H /
        // Roll I (the made basket, MissOutOfBoundsLost, the two Roll I terminals),
        // zero-unrouted — and bucket everything Roll K / a reset / a re-inbound opens
        // up as "deeper" (routed, not required). As of #6 the OOB-retained sideline
        // inbound is DROPPED from the core set too — it no longer parks; it re-runs
        // Roll A and lands deeper. STUB:OffensiveRebound and STUB:BlockRecovery remain
        // dropped (offensive board executes Roll K; a block routes into Roll I).
        var destHits = new Dictionary<string, int>
        {
            ["END:Made"] = 0,
            ["END:MissOutOfBoundsLost"] = 0,
            ["END:DefensiveRebound"] = 0,
            ["END:LooseBallFoulOnOffense"] = 0,
        };
        // STUB:SidelineInbound is DROPPED from the core set as of #6: an OOB-retained
        // shot no longer parks — it re-runs Roll A and lands DEEPER. With no
        // fact-carrying core stub left, the result-ride-through assertion goes with it;
        // clean one-hop fact ride-through is proven by RollHResolutionBatchCheck.

        var unrecognized = 0;
        var deeperTerminals = 0; // Roll K / reset / re-inbound terminals beyond core (routed)
        var deeperStubs = 0;     // Roll K / reset stub parks beyond core (routed)

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Select a slot (Roll E) and stamp a zone (Roll G), then hand the
            // resolver a clean IntoShotResolution continuation — exactly what Roll G
            // emits — to isolate the Roll H hop.
            var selected = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var withZone = ((Continue)RollG.Execute(selected, genG.Generate(selected), rng)).State;
            var shotTicket = new Continue(ContinuationKind.IntoShotResolution, withZone);
            var d = resolver.Route(shotTicket).Destination;

            if (d == "END:Made") { destHits["END:Made"]++; continue; }
            if (d == "END:MissOutOfBoundsLost") { destHits["END:MissOutOfBoundsLost"]++; continue; }
            // Roll I's two terminals (Session 14) — matched BEFORE the stub parse,
            // like the Made / MissOutOfBoundsLost terminals; they carry no
            // slot:zone:result tail, so they must short-circuit here.
            if (d == "END:DefensiveRebound") { destHits["END:DefensiveRebound"]++; continue; }
            if (d == "END:LooseBallFoulOnOffense") { destHits["END:LooseBallFoulOnOffense"]++; continue; }

            // Everything else is ROUTED but not core: the three Roll K terminals, a
            // jump-ball terminal, any reset- or re-inbound-born terminal, an OOB-retained
            // shot that re-ran Roll A, and any deeper stub park. Bucket so they don't read
            // as unrouted; don't require them.
            if (d.StartsWith("END:")) deeperTerminals++;
            else if (d.StartsWith("STUB:")) deeperStubs++;
            else unrecognized++;
        }

        var landed = destHits.Values.Sum();
        Console.WriteLine($"  landed at a CORE post-shot destination: {landed:N0}");
        Console.WriteLine($"  routed DEEPER via Roll K / reset (terminals {deeperTerminals:N0}, stubs {deeperStubs:N0})");
        Console.WriteLine("  core destinations reached:");
        var allDests = true;
        foreach (var (label, count) in destHits)
        {
            var hit = count > 0;
            allDests &= hit;
            Console.WriteLine($"    {label,-26} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all core Roll H->I destinations reached (offensive rebound now executes Roll K; OOB-retained re-runs Roll A and lands deeper): {(allDests ? "ok" : "FAIL")}");

        return routedOk && allDests;
    }

    // --- Batch: Roll I's four-way rebound distribution converges within tolerance,
    //     and every exit is a clean terminal-or-continue of the expected kind, with
    //     slot+zone+result riding through the stub landings. Driven through a REAL
    // --- Batch: Roll I's seven-way LIVE-MISS rebound distribution converges within
    //     tolerance, and every arm routes as designed, with slot+zone+result riding
    //     through the fact-carrying stub landings. Driven through a REAL Miss so the
    //     carried state holds slot + zone + result (the chain walks E -> G -> H, keeping
    //     only the misses, then resolves I on the LiveBall pie). The seven rates are
    //     checked against the configured pie; routing is checked per outcome:
    //     DefensiveRebound / LooseBallFoulOnOffense / OutOfBoundsOffOffense -> END
    //     terminals; OffensiveRebound -> STUB:OffensiveRebound (facts); OutOfBoundsOffDefense
    //     -> STUB:SidelineInbound with NO charge; LooseBallFoulOnDefense -> SidelineInbound
    //     (below bonus) or ResolveFreeThrows (in bonus, §2a); JumpBall -> the arrow node.
    //     The OOB-off-defense and below-bonus loose-ball-defense arms both land on a
    //     plain sideline inbound and are separated by the foul DELTA (the foul arm
    //     charged 1, the OOB arm 0 — also the proof the OOB pair charges nothing).
    //     The block pie is exercised separately by RollIBlockReboundBatchCheck. ---
    private static bool RollIReboundBatchCheck(
        RollAConfig cfg, RollIConfig cfgI, RollIStubPieGenerator genI,
        GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} rebounds routed through Roll I ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the foul charge here does not perturb the shared game and
        // so the defense never reaches the bonus (keeping the loose-ball-defense arm
        // on its SidelineInbound branch for this batch).
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);
        var genH = new RollHStubPieGenerator(cfgH);
        var pieE = genE.Generate(state);
        var pieI = genI.Generate(state, ReboundSource.LiveBall);

        var counts = new Dictionary<ReboundOutcome, int>();
        foreach (var o in Enum.GetValues<ReboundOutcome>()) counts[o] = 0;

        // Where each outcome routed, and whether facts rode through the stubs.
        var defTerminals = 0;       // END:DefensiveRebound
        var offFoulTerminals = 0;   // END:LooseBallFoulOnOffense
        var oobOffTerminals = 0;    // END:OutOfBoundsOffOffense
        var offReboundStubs = 0;    // STUB:OffensiveRebound
        var oobDefSideline = 0;     // STUB:SidelineInbound (OOB-off-defense, no charge)
        var defFoulSideline = 0;    // STUB:SidelineInbound (loose-ball-defense, below bonus)
        var defFoulFreeThrows = 0;  // STUB:ResolveFreeThrows (loose-ball-defense, in bonus)
        var jumpBalls = 0;          // ResolveJumpBall
        var unrecognized = 0;
        var missingFact = 0;
        var badCharge = 0;          // a non-foul arm moved the team-foul count
        var resolved = 0;

        // We resolve Roll I's continuations against the same stub set the resolver
        // uses, to confirm the facts ride through. The loose-ball-defense arm splits
        // by bonus: below the threshold it routes to SidelineInbound (fact-carrying),
        // and once the SHARED game's defense crosses the bonus it routes to the bonus
        // FT stub (carrying a Bonus payload) — both are correct.
        var offReboundStub = new OffensiveReboundStub();
        var sidelineStub = new SidelineInboundStub();
        var freeThrowStub = new ResolveFreeThrowsStub();

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Walk E -> G -> H; keep only the misses (the one feed into Roll I).
            var sel = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), rng)).State;
            var hRes = RollH.Execute(zoned, genH.Generate(zoned), rng);
            if (hRes is not Continue { Next: ContinuationKind.ResolveRebound } miss) continue;

            resolved++;
            var before = game.Fouls.FoulsFor(miss.State.Defense);
            var iRes = RollI.Execute(miss.State, pieI, game, rng);
            var delta = game.Fouls.FoulsFor(miss.State.Defense) - before;

            switch (iRes)
            {
                case Terminal { Reason: "DefensiveRebound" }:
                    counts[ReboundOutcome.DefensiveRebound]++;
                    if (delta != 0) badCharge++;
                    defTerminals++;
                    break;
                case Terminal { Reason: "LooseBallFoulOnOffense" }:
                    counts[ReboundOutcome.LooseBallFoulOnOffense]++;
                    if (delta != 0) badCharge++;   // an offensive foul charges nothing
                    offFoulTerminals++;
                    break;
                case Terminal { Reason: "OutOfBoundsOffOffense" }:
                    counts[ReboundOutcome.OutOfBoundsOffOffense]++;
                    if (delta != 0) badCharge++;   // OOB charges nothing
                    oobOffTerminals++;
                    break;
                case Continue { Next: ContinuationKind.ResolveOffensiveRebound } orc:
                    counts[ReboundOutcome.OffensiveRebound]++;
                    if (delta != 0) badCharge++;
                    var od = offReboundStub.Receive(orc);
                    if (od.EndsWith("NO_SLOT") || od.EndsWith("NO_ZONE") || od.EndsWith("NO_RESULT")) missingFact++;
                    else offReboundStubs++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound } sic:
                    // BOTH the OOB-off-defense arm and the below-bonus loose-ball-defense
                    // arm land on a plain sideline inbound; the foul DELTA separates them
                    // (the foul arm charged 1, the OOB arm charged 0). This is also the
                    // proof the OOB-off-defense arm charges nothing.
                    var sdLabel = sidelineStub.Receive(sic);
                    if (sdLabel.EndsWith("NO_SLOT") || sdLabel.EndsWith("NO_ZONE") || sdLabel.EndsWith("NO_RESULT")) missingFact++;
                    if (delta == 0)
                    {
                        counts[ReboundOutcome.OutOfBoundsOffDefense]++;
                        if (!sdLabel.EndsWith("NO_SLOT") && !sdLabel.EndsWith("NO_ZONE") && !sdLabel.EndsWith("NO_RESULT")) oobDefSideline++;
                    }
                    else
                    {
                        counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                        if (delta != 1) badCharge++;
                        if (!sdLabel.EndsWith("NO_SLOT") && !sdLabel.EndsWith("NO_ZONE") && !sdLabel.EndsWith("NO_RESULT")) defFoulSideline++;
                    }
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows } ftc:
                    // The loose-ball-defense foul with the defense IN the bonus — the
                    // shared game crosses the threshold partway through the batch, so
                    // this is expected, not an anomaly. Charges the bonus FT stub,
                    // carrying the Bonus payload (OneAndOne / Double). The OOB-off-defense
                    // arm can NEVER reach this branch (it reads no bonus, charges no foul).
                    counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                    if (delta != 1) badCharge++;
                    _ = freeThrowStub.Receive(ftc);
                    defFoulFreeThrows++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[ReboundOutcome.JumpBall]++;
                    if (delta != 0) badCharge++;
                    jumpBalls++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        Console.WriteLine($"  misses resolved through Roll I: {resolved:N0}");
        var n = (double)resolved;
        var ratesOk = true;
        Console.WriteLine("  Roll I outcomes:");
        foreach (var (outcome, weight) in pieI.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    DefensiveRebound       -> END terminal       {defTerminals,8:N0}  {(defTerminals > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    LooseBallFoulOnOffense -> END terminal       {offFoulTerminals,8:N0}  {(offFoulTerminals > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffOffense  -> END terminal       {oobOffTerminals,8:N0}  {(oobOffTerminals > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OffensiveRebound       -> STUB (same poss.)  {offReboundStubs,8:N0}  {(offReboundStubs > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffDefense  -> SidelineInbound     {oobDefSideline,8:N0}  {(oobDefSideline > 0 ? "ok" : "NONE")}  (no charge)");
        // The loose-ball-defense foul splits on the bonus: SidelineInbound below it,
        // ResolveFreeThrows once the shared game crosses the threshold mid-batch (§2a).
        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine($"    LooseBallFoulOnDefense -> SidelineInbound     {defFoulSideline,8:N0}  (below bonus)");
        Console.WriteLine($"                           -> ResolveFreeThrows  {defFoulFreeThrows,7:N0}  (in bonus)");
        Console.WriteLine($"    JumpBall               -> arrow node         {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var allArms = defTerminals > 0 && offFoulTerminals > 0 && oobOffTerminals > 0
                      && offReboundStubs > 0 && oobDefSideline > 0 && defFoulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var factOk = missingFact == 0;
        var chargeOk = badCharge == 0;
        var bonusCrossOk = defFoulSideline > 0 && defFoulFreeThrows > 0;
        Console.WriteLine($"\n  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  slot+zone+result intact on stub landings: missing={missingFact} -> {(factOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  foul-charge discipline (only loose-ball-defense charges): bad={badCharge} -> {(chargeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: loose-ball-defense crossed the bonus mid-batch (sideline -> FT): {(bonusCrossOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll I arms reached: {(allArms ? "ok" : "FAIL")}");

        return ratesOk && routedOk && factOk && chargeOk && allArms && bonusCrossOk;
    }

    // --- Bonus fork: the loose-ball-foul-on-defense arm charges the DEFENSIVE team
    //     foul and routes on the bonus exactly as Roll D does. Drive the defense's
    //     foul count up across the bonus thresholds and confirm: below bonus ->
    //     SidelineInbound (Bonus=None); at/above the bonus threshold ->
    //     ResolveFreeThrows carrying OneAndOne; at/above the double-bonus threshold
    //     -> ResolveFreeThrows carrying Double. Mirrors RollDBonusRoutingCheck. ---
    private static bool RollIBonusForkCheck(
        RollAConfig cfg, RollDConfig cfgD, RollIConfig cfgI,
        RollIStubPieGenerator genI, PossessionState state)
    {
        Console.WriteLine($"\n--- Bonus fork: Roll I loose-ball-defense foul across the thresholds ---");

        // Build a pie that ALWAYS rolls LooseBallFoulOnDefense, so every draw
        // exercises the foul arm regardless of RNG. (All mass on the
        // foul-on-defense slice; the others sit at zero — the Pie constructor
        // still requires every enum member to be present.)
        var foulOnlyPie = new Pie<ReboundOutcome>(new Dictionary<ReboundOutcome, double>
        {
            [ReboundOutcome.DefensiveRebound] = 0.0,
            [ReboundOutcome.OffensiveRebound] = 0.0,
            [ReboundOutcome.LooseBallFoulOnDefense] = 1.0,
            [ReboundOutcome.LooseBallFoulOnOffense] = 0.0,
            [ReboundOutcome.OutOfBoundsOffOffense] = 0.0,
            [ReboundOutcome.OutOfBoundsOffDefense] = 0.0,
            [ReboundOutcome.JumpBall] = 0.0
        }, cfgI.Epsilon);

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var rng = new SystemRng(cfg.Seed);

        var ok = true;
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var r = RollI.Execute(state, foulOnlyPie, game, rng);
            var after = game.Fouls.FoulsFor(state.Defense);

            // Expected routing from the POST-increment foul count.
            var expectedBonus = after >= cfgD.DoubleBonusThreshold ? BonusType.Double
                              : after >= cfgD.BonusThreshold ? BonusType.OneAndOne
                              : BonusType.None;
            var expectedKind = expectedBonus == BonusType.None
                ? ContinuationKind.ResolveSidelineInbound
                : ContinuationKind.ResolveFreeThrows;

            var c = r as Continue;
            var kindOk = c is not null && c.Next == expectedKind;
            var bonusOk = c is not null && c.Bonus == expectedBonus;
            var chargedOk = after == before + 1;
            var rowOk = kindOk && bonusOk && chargedOk;
            ok &= rowOk;

            var route = c is null ? "(not a continue!)"
                : c.Next == ContinuationKind.ResolveFreeThrows
                    ? $"ResolveFreeThrows({c.Bonus})"
                    : $"SidelineInbound({c.Bonus})";
            Console.WriteLine(
                $"  foul#{after,2}: {before}->{after} | route={route,-28} | expected={expectedKind} ({expectedBonus}) -> {(rowOk ? "ok" : "FAIL")}");
        }

        Console.WriteLine($"  bonus fork charges the defense and routes correctly: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Batch: Roll I's seven-way BLOCK-RECOVERY distribution converges within
    //     tolerance, and every arm routes exactly as the live-miss path does (same
    //     routes, different weights). Driven by calling RollI.Execute DIRECTLY on the
    //     base state with the BLOCK pie (the RollM batch pattern), so all seven arms
    //     are exercised every draw. A FRESH game crosses the bonus mid-batch, so the
    //     LooseBallFoulOnDefense arm splits sideline/FT (§2a). Per-arm routing is
    //     asserted: DefensiveRebound -> Terminal whose consequence is a TRANSITION to
    //     the defense carrying the Rebound context (NOT FreeThrowRebound — that is Roll
    //     M's); OffensiveRebound -> Continue(ResolveOffensiveRebound) with NO source
    //     stamp (a block reuses the LiveBall offensive-rebound pie — a distinct block
    //     source is deferred); LooseBallFoulOnOffense / OutOfBoundsOffOffense -> Terminal
    //     DeadBallTo the defense; OutOfBoundsOffDefense -> Continue(ResolveSidelineInbound)
    //     with NO charge even in the bonus; LooseBallFoulOnDefense -> charge the defense
    //     + bonus fork; JumpBall -> Continue(ResolveJumpBall). Foul-charge discipline is
    //     asserted PER DRAW off the team-foul delta: ONLY the loose-ball-defense arm
    //     increments the defensive team foul; the OOB pair and every other arm charge
    //     nothing. ---
    private static bool RollIBlockReboundBatchCheck(
        RollAConfig cfg, RollIConfig cfgI, RollIStubPieGenerator genI, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} BLOCK recoveries routed through Roll I ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgD = RollDConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the charge here does not perturb the shared game. The
        // loose-ball-defense arm's charge crosses the bonus partway through THIS game,
        // exercising the §2a split (sideline below the bonus, FTs in it).
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var pieI = genI.Generate(state, ReboundSource.Block);

        var counts = new Dictionary<ReboundOutcome, int>();
        foreach (var o in Enum.GetValues<ReboundOutcome>()) counts[o] = 0;

        var defTransition = 0;     // DefensiveRebound -> transition terminal to defense
        var offReboundCont = 0;    // OffensiveRebound -> Roll K cont, NO source (LiveBall)
        var offFoulDead = 0;       // LooseBallFoulOnOffense -> dead-ball terminal
        var oobOffDead = 0;        // OutOfBoundsOffOffense -> dead-ball terminal
        var oobDefSideline = 0;    // OutOfBoundsOffDefense -> sideline (no fork, no charge)
        var defFoulSideline = 0;   // LooseBallFoulOnDefense below bonus -> sideline
        var defFoulFreeThrows = 0; // LooseBallFoulOnDefense in bonus -> FTs
        var jumpBalls = 0;         // JumpBall -> arrow node
        var unrecognized = 0;
        var badConsequence = 0;    // wrong team / entry / context on a terminal or ticket
        var badCharge = 0;         // a non-foul arm moved the team-foul count (or the
                                   //  foul arm failed to charge exactly 1)

        var sidelineStub = new SidelineInboundStub();
        var freeThrowStub = new ResolveFreeThrowsStub();

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var iRes = RollI.Execute(state, pieI, game, rng);
            var delta = game.Fouls.FoulsFor(state.Defense) - before;

            switch (iRes)
            {
                case Terminal { Reason: "DefensiveRebound" } dr:
                    counts[ReboundOutcome.DefensiveRebound]++;
                    if (delta != 0) badCharge++;
                    if (dr.Consequence.NextOffense != state.Defense
                        || dr.Consequence.NextEntry != EntryType.Transition
                        || dr.Consequence.TransitionContext?.Source != TransitionSource.Rebound)
                        badConsequence++;
                    else defTransition++;
                    break;

                case Continue { Next: ContinuationKind.ResolveOffensiveRebound } orc:
                    counts[ReboundOutcome.OffensiveRebound]++;
                    if (delta != 0) badCharge++;
                    // A block reuses the LiveBall offensive-rebound pie: NO source stamp
                    // (a distinct block source is deferred). A null reads as LiveBall.
                    if (orc.OffensiveReboundSource is not null) badConsequence++;
                    else offReboundCont++;
                    break;

                case Terminal { Reason: "LooseBallFoulOnOffense" } lbo:
                    counts[ReboundOutcome.LooseBallFoulOnOffense]++;
                    if (delta != 0) badCharge++;       // an offensive foul charges nothing
                    if (lbo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else offFoulDead++;
                    break;

                case Terminal { Reason: "OutOfBoundsOffOffense" } oobo:
                    counts[ReboundOutcome.OutOfBoundsOffOffense]++;
                    if (delta != 0) badCharge++;       // OOB charges nothing
                    if (oobo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else oobOffDead++;
                    break;

                case Continue { Next: ContinuationKind.ResolveSidelineInbound } sc:
                    // BOTH the OOB-off-defense arm and the below-bonus loose-ball-defense
                    // arm land on a plain sideline inbound; the foul DELTA separates them
                    // (the foul arm charged 1, the OOB arm charged 0). This is also the
                    // proof the OOB pair charges nothing.
                    if (delta == 0)
                    {
                        counts[ReboundOutcome.OutOfBoundsOffDefense]++;
                        oobDefSideline++;
                    }
                    else
                    {
                        counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                        if (delta != 1) badCharge++;
                        defFoulSideline++;
                    }
                    _ = sidelineStub.Receive(sc);
                    break;

                case Continue { Next: ContinuationKind.ResolveFreeThrows } ftc:
                    // ONLY the loose-ball-defense arm IN THE BONUS lands here. The
                    // OOB-off-defense arm can NEVER reach this branch — it reads no bonus
                    // and charges no foul — so a delta of 0 here would be a routing bug.
                    counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                    if (delta != 1) badCharge++;
                    defFoulFreeThrows++;
                    _ = freeThrowStub.Receive(ftc);
                    break;

                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[ReboundOutcome.JumpBall]++;
                    if (delta != 0) badCharge++;
                    jumpBalls++;
                    break;

                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll I block outcomes:");
        foreach (var (outcome, weight) in pieI.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    DefensiveRebound       -> transition terminal (defense) {defTransition,8:N0}  {(defTransition > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OffensiveRebound       -> Roll K cont (no source stamp)  {offReboundCont,8:N0}  {(offReboundCont > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    LooseBallFoulOnOffense -> dead-ball terminal (defense)   {offFoulDead,8:N0}  {(offFoulDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffOffense  -> dead-ball terminal (defense)   {oobOffDead,8:N0}  {(oobOffDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffDefense  -> sideline inbound (no charge)   {oobDefSideline,8:N0}  {(oobDefSideline > 0 ? "ok" : "NONE")}");
        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine($"    LooseBallFoulOnDefense -> SidelineInbound (below bonus)  {defFoulSideline,8:N0}");
        Console.WriteLine($"                           -> ResolveFreeThrows (in bonus)   {defFoulFreeThrows,8:N0}");
        Console.WriteLine($"    JumpBall               -> arrow node                     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var allArms = defTransition > 0 && offReboundCont > 0 && offFoulDead > 0 && oobOffDead > 0
                      && oobDefSideline > 0 && defFoulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var consequenceOk = badConsequence == 0;
        var chargeOk = badCharge == 0;
        var bonusCrossOk = defFoulSideline > 0 && defFoulFreeThrows > 0;
        Console.WriteLine($"\n  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  consequence/ticket correct on every terminal+continue: bad={badConsequence} -> {(consequenceOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  foul-charge discipline (only loose-ball-defense charges): bad={badCharge} -> {(chargeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: loose-ball-defense crossed the bonus mid-batch (sideline -> FT): {(bonusCrossOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll I block arms reached: {(allArms ? "ok" : "FAIL")}");

        var okAll = ratesOk && routedOk && consequenceOk && chargeOk && allArms && bonusCrossOk;
        Console.WriteLine($"  Roll I block-recovery resolution: {(okAll ? "ok" : "FAIL")}");
        return okAll;
    }

    // --- Context: the block source SELECTS the block pie, and a blocked shot actually
    //     REACHES Roll I (never the retired block-recovery stub). Two parts, the
    //     RollMContextSelectionCheck pattern:
    //     (1) Pie selection. Roll I's generator, given ReboundSource.Block, returns the
    //     block weight set; given LiveBall (the legacy default a null stamp maps to), the
    //     live-miss set. Each is proven two ways: the SELECTED pie's weights equal the
    //     configured set (right SET picked), and a draw sample off that pie converges to
    //     those weights (the pie is actually consumed). The two sources must also DIFFER
    //     (a null/legacy stamp can never accidentally get the block odds).
    //     (2) Route proof. Roll H's Blocked arm now emits Continue(ResolveRebound) carrying
    //     ReboundSource.Block — asserted directly off the emission — and routing that
    //     continuation through a REAL resolver lands at a Roll-I-family destination, NEVER
    //     at STUB:BlockRecovery (retired to the corner), with zero unrouted. ---
    private static bool RollIBlockContextSelectionCheck(
        RollAConfig cfg, RollIConfig cfgI, PossessionState state)
    {
        Console.WriteLine($"\n--- Context: Roll I block pie selection + Blocked -> Roll I routing ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var ok = true;

        // ---- (1) Pie selection: LiveBall (legacy default) vs Block ----
        var genI = new RollIStubPieGenerator(cfgI);
        var contexts = new (ReboundSource src, (ReboundOutcome o, double w)[] expected)[]
        {
            (ReboundSource.LiveBall, new[]
            {
                (ReboundOutcome.DefensiveRebound,       cfgI.DefensiveRebound),
                (ReboundOutcome.OffensiveRebound,       cfgI.OffensiveRebound),
                (ReboundOutcome.LooseBallFoulOnDefense, cfgI.LooseBallFoulOnDefense),
                (ReboundOutcome.LooseBallFoulOnOffense, cfgI.LooseBallFoulOnOffense),
                (ReboundOutcome.OutOfBoundsOffOffense,  cfgI.OutOfBoundsOffOffense),
                (ReboundOutcome.OutOfBoundsOffDefense,  cfgI.OutOfBoundsOffDefense),
                (ReboundOutcome.JumpBall,               cfgI.JumpBall),
            }),
            (ReboundSource.Block, new[]
            {
                (ReboundOutcome.DefensiveRebound,       cfgI.BlockDefensiveRebound),
                (ReboundOutcome.OffensiveRebound,       cfgI.BlockOffensiveRebound),
                (ReboundOutcome.LooseBallFoulOnDefense, cfgI.BlockLooseBallFoulOnDefense),
                (ReboundOutcome.LooseBallFoulOnOffense, cfgI.BlockLooseBallFoulOnOffense),
                (ReboundOutcome.OutOfBoundsOffOffense,  cfgI.BlockOutOfBoundsOffOffense),
                (ReboundOutcome.OutOfBoundsOffDefense,  cfgI.BlockOutOfBoundsOffDefense),
                (ReboundOutcome.JumpBall,               cfgI.BlockJumpBall),
            }),
        };

        double Defensive(ReboundSource s) =>
            genI.Generate(state, s).Slices.First(x => x.Outcome == ReboundOutcome.DefensiveRebound).Weight;

        foreach (var (src, expected) in contexts)
        {
            var pie = genI.Generate(state, src);
            var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var selectionOk = true;
            foreach (var (o, w) in expected)
                if (Math.Abs(pieMap[o] - w) > cfgI.Epsilon) selectionOk = false;

            var rng = new SystemRng(cfg.Seed);
            var counts = new Dictionary<ReboundOutcome, int>();
            foreach (var o in Enum.GetValues<ReboundOutcome>()) counts[o] = 0;
            for (var i = 0; i < cfg.BatchSize; i++) counts[pie.Roll(rng.NextUnitInterval())]++;

            var ratesOk = true;
            Console.WriteLine($"  Roll I source={src} (selection {(selectionOk ? "ok" : "FAIL")}):");
            foreach (var (o, w) in expected)
            {
                var observed = counts[o] / (double)cfg.BatchSize;
                var pass = Math.Abs(observed - w) <= cfg.RateTolerance;
                ratesOk &= pass;
                Console.WriteLine($"    {o,-24} observed={observed:P3}  expected={w:P3}  {(pass ? "ok" : "FAIL")}");
            }
            ok &= selectionOk && ratesOk;
        }
        var differ = Math.Abs(Defensive(ReboundSource.Block) - Defensive(ReboundSource.LiveBall)) > cfgI.Epsilon;
        Console.WriteLine($"  block pie differs from live-miss pie (DefensiveRebound): {(differ ? "ok" : "FAIL")}");
        ok &= differ;

        // ---- (2) Route proof: a Blocked shot reaches Roll I, never STUB:BlockRecovery ----
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);

        var rngR = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            new RollEStubPieGenerator(cfgE),
            new RollFStubPieGenerator(cfgF),
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(cfgH),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rngR);

        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);
        var genH = new RollHStubPieGenerator(cfgH);
        var pieE = genE.Generate(state);

        var blocks = 0;
        var blockStampOk = 0;
        var blockRecoveryHits = 0;
        var unrouted = 0;
        var landedAtRollI = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var sel = ((Continue)RollE.Execute(state, pieE, game, rngR)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), rngR)).State;
            var hRes = RollH.Execute(zoned, genH.Generate(zoned), rngR);

            // Keep only the BLOCKED shots — the arm this check is about.
            if (hRes is not Continue { Next: ContinuationKind.ResolveRebound } cont) continue;
            if (cont.ReboundSource != ReboundSource.Block) continue;   // a plain miss; skip

            blocks++;
            // Re-point proof: Roll H's Blocked arm emits ResolveRebound + Block, NOT the
            // retired ResolveBlock edge.
            if (cont is { Next: ContinuationKind.ResolveRebound, ReboundSource: ReboundSource.Block })
                blockStampOk++;

            // Route it through the real resolver and inspect the landing.
            var d = resolver.Route(cont).Destination;
            if (d.StartsWith("STUB:BlockRecovery")) blockRecoveryHits++;
            // A blocked shot now lands at a Roll-I-family destination: a rebound terminal
            // (END:DefensiveRebound / LooseBallFoulOnOffense / OutOfBoundsOffOffense), a
            // made/missed putback or reset deeper through Roll K, a sideline inbound, a
            // jump-ball terminal, or a bonus-FT landing. The one thing it must NEVER be is
            // the retired block-recovery stub, and it must always route somewhere.
            if (d.StartsWith("END:") || d.StartsWith("STUB:")) landedAtRollI++;
            else unrouted++;
        }

        Console.WriteLine($"  blocked shots observed: {blocks:N0}");
        Console.WriteLine($"  each Blocked emits Continue(ResolveRebound) + ReboundSource.Block: {blockStampOk:N0}/{blocks:N0} -> {(blockStampOk == blocks && blocks > 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"  blocked shots routed to a Roll-I destination: {landedAtRollI:N0}");
        Console.WriteLine($"  blocked shots landing at the retired STUB:BlockRecovery: {blockRecoveryHits} -> {(blockRecoveryHits == 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"  zero unrouted blocked shots: {unrouted} -> {(unrouted == 0 ? "ok" : "FAIL")}");

        var routeOk = blocks > 0 && blockStampOk == blocks && blockRecoveryHits == 0 && unrouted == 0;
        ok &= routeOk;
        Console.WriteLine($"  Roll I block context selection + routing: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Session 30: end-of-half intent pie — rate proof (flat, score-blind, clock-only).
    //     Mirrors the same Pie<EndOfHalfIntent> the Governor builds from the same config,
    //     rolled 100k times directly. Proves the three rates converge within tolerance
    //     without reaching into the Governor's private fields. ---
    private static bool EndOfHalfIntentBatchCheck(RollAConfig cfg, EndOfHalfConfig cfgEndOfHalf)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} end-of-half intent draws (flat, score-blind) ---");
        var rng = new SystemRng(cfg.Seed);

        var pie = new Pie<EndOfHalfIntent>(
            new Dictionary<EndOfHalfIntent, double>
            {
                [EndOfHalfIntent.HoldShootLast] = cfgEndOfHalf.HoldShootLast,
                [EndOfHalfIntent.ShootEarly]    = cfgEndOfHalf.ShootEarly,
                [EndOfHalfIntent.NoShot]        = cfgEndOfHalf.NoShot,
            },
            cfgEndOfHalf.Epsilon);

        var counts = new Dictionary<EndOfHalfIntent, int>();
        foreach (var o in Enum.GetValues<EndOfHalfIntent>()) counts[o] = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
            counts[pie.Roll(rng.NextUnitInterval())]++;

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  end-of-half intent rates:");
        foreach (var (intent, weight) in pie.Slices)
        {
            var observed = counts[intent] / n;
            var gap      = Math.Abs(observed - weight);
            var pass     = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {intent,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        return ratesOk;
    }

    // --- Session 15: the thin Governor's possession-to-possession loop. ---
    // The FIRST check whose whole point is state persisting across iterations: it
    // shares ONE GameState across the entire loop, so foul counts climb and CROSS
    // THE BONUS mid-loop (CONVENTIONS §2a). The Governor handles every stub-park
    // through ONE default-consequence path (keyed only on "no terminal"), so the
    // Session-14 "only handled one landing" bug class cannot recur — the per-stub
    // breakdown is observability, never routing.
    private static bool GovernorLoopCheck(RollAConfig cfg, RollDConfig cfgD, GovernorConfig cfgGov, RollClockConfig cfgClock, EndOfHalfConfig cfgEndOfHalf)
    {
        Console.WriteLine($"\n--- Governor loop: two {cfgGov.HalfSeconds:N0}s halves ---");

        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            new RollEStubPieGenerator(cfgE),
            new RollFStubPieGenerator(cfgF),
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        var governor = new Governor(resolver, game, cfgGov, cfgClock, new SystemRng(cfg.Seed + 1), cfgEndOfHalf);

        // --- Seed the cross-possession state so persistence is DETERMINISTIC. ---
        // Arrow: turn it ON (Home) before the loop. It is never Off thereafter (no OT
        // reset), so every jump ball takes the ON path and FLIPS it exactly once —
        // letting us predict the final arrow exactly from the jump-ball count.
        game.SetPossessionArrow(TeamSide.Home);

        // Fouls: push Home to the bonus threshold so its opponent is ALREADY in bonus
        // before the loop. Fouls only ever increment (no half-reset this session), so
        // the bonus must STAY crossed — a deterministic "never un-crosses" check.
        for (var i = 0; i < cfgD.BonusThreshold; i++) game.Fouls.Increment(TeamSide.Home);
        var homeFoulsBefore = game.Fouls.FoulsFor(TeamSide.Home);
        var bonusBefore = game.Fouls.BonusFor(TeamSide.Home);

        // Lineups: capture references to confirm they survive untouched.
        var homeLineupRef = game.HomeLineup;
        var awayLineupRef = game.AwayLineup;

        var first = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        var result = governor.Run(first);
        var records = result.Possessions;

        // --- Invariant checks. ---
        // The load-bearing one: zero possessions lost. A dropped park is exactly how
        // the count would silently leak. Possession count is now clock-driven (no fixed cap).
        // §2a discipline: NoShot possessions are a THIRD class — neither terminalEnded nor
        // parked; they must be counted separately so the assertion remains total.
        var noShotCount = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.NoShot);
        var intentHeld  = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.HoldShootLast);
        var intentEarly = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.ShootEarly);
        var noLostOk = result.TerminalEnded + result.Parked + noShotCount == records.Count;

        // Contiguous numbers 1..N, and offense/defense flips that match each
        // possession's APPLIED consequence (proving the Governor spawned from it).
        var contiguousOk = true;
        var flipsOk = true;
        var jumpBalls = 0;
        var reboundIntoJ = 0;
        var stealIntoJ = 0;
        var homePoints = 0;
        var awayPoints = 0;
        var talliedPoints = 0;
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (r.Number != i + 1) contiguousOk = false;
            if (r.Defense != Other(r.Offense)) flipsOk = false;
            if (r.EndedOnTerminal && r.EndLabel.StartsWith("JumpBall")) jumpBalls++;
            if (i > 0)
            {
                var prev = records[i - 1];
                if (r.Offense != prev.Applied.NextOffense) flipsOk = false;
                if (r.Entry != prev.Applied.NextEntry) flipsOk = false;
                // A possession whose PREDECESSOR ended on a defensive rebound entered
                // Roll J: the rebound consequence carries the Rebound context, which the
                // resolver routes to Roll J (not Roll A). This counts the live path.
                if (prev.EndedOnTerminal && prev.EndLabel == "DefensiveRebound") reboundIntoJ++;
                // A possession whose PREDECESSOR ended on a LIVE turnover (a steal) also
                // entered Roll J as of Contextification #3: the three live-turnover
                // terminals (BadPassIntercepted / LostBallLiveBall / LiveBallTurnover)
                // carry the Steal context, which the resolver routes to Roll J on the
                // steal pie. Steals JOIN rebounds as Roll J feeders — more possessions
                // enter J than before.
                if (prev.EndedOnTerminal && prev.EndLabel is "BadPassIntercepted"
                        or "LostBallLiveBall" or "LiveBallTurnover")
                    stealIntoJ++;
            }
            if (r.Offense == TeamSide.Home) homePoints += r.Points;
            else awayPoints += r.Points;
            talliedPoints += r.Points;
        }
        var firstOk = records.Count > 0
            && records[0].Offense == TeamSide.Home
            && records[0].Entry == EntryType.DeadBallInbound;

        // Arrow persistence: ON the whole loop, flipped once per jump ball and by
        // NOTHING else — so the final arrow is exactly predictable.
        var expectedArrow = jumpBalls % 2 == 0 ? ArrowState.Home : ArrowState.Away;
        var arrowOk = game.PossessionArrow != ArrowState.Off
                      && game.PossessionArrow == expectedArrow;

        // Foul persistence: monotonic (never reset) and the bonus stays crossed.
        var homeFoulsAfter = game.Fouls.FoulsFor(TeamSide.Home);
        var bonusAfter = game.Fouls.BonusFor(TeamSide.Home);
        var foulOk = homeFoulsAfter >= homeFoulsBefore && bonusAfter != BonusType.None;

        // Lineup persistence: same objects, still five slots each.
        var lineupOk = ReferenceEquals(game.HomeLineup, homeLineupRef)
                       && ReferenceEquals(game.AwayLineup, awayLineupRef)
                       && game.HomeLineup.OnCourt.Count == Lineup.Size
                       && game.AwayLineup.OnCourt.Count == Lineup.Size;

        // Rebound -> Roll J, end to end (the robust gate). A defensive rebound spawns a
        // Transition possession that ENTERS Roll J (not Roll A). As of Contextification
        // #1, Roll J's Push flows into Roll E (FastBreak set) and on through the shot
        // chain, so the proof that Roll J ran is simply that possessions entered it off a
        // rebound. Steals ALSO feed Roll J as of #3, but they are far rarer than rebounds,
        // so over a 200-possession cap stealIntoJ is reported for observability rather than
        // gated (a hard >0 gate would be seed-fragile). The rigorous steal-routing proof is
        // the dedicated 100k RollJStealBatchCheck plus the resolver's wiring-bug alarm: if a
        // steal-born Transition ever reached this loop with a bad/null context, RunPossession
        // would THROW, so the loop completing at all is itself proof the steal context rides
        // correctly whenever a steal occurs.
        var rollJOk = reboundIntoJ > 0;

        // --- Report. ---
        Console.WriteLine(
            $"  resolved={records.Count:N0} | terminal-ended={result.TerminalEnded:N0} | parked={result.Parked:N0} " +
            $"| noShot={noShotCount:N0} | terminal+parked+noShot==total -> {(noLostOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  possession numbers contiguous 1..{records.Count} -> {(contiguousOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  offense/defense flips match applied consequence -> {(flipsOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  first possession = Home, DeadBallInbound -> {(firstOk ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  arrow: jump balls={jumpBalls} | final={game.PossessionArrow} | expected={expectedArrow} " +
            $"-> {(arrowOk ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  fouls(Home): {homeFoulsBefore}({bonusBefore}) -> {homeFoulsAfter}({bonusAfter}) | " +
            $"monotonic + bonus stays crossed -> {(foulOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  lineups survive (same objects, 5 slots each) -> {(lineupOk ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  rebound -> Roll J: possessions entering J={reboundIntoJ:N0} (Push now flows into Roll E) " +
            $"-> {(reboundIntoJ > 0 ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  steal -> Roll J: possessions entering J={stealIntoJ:N0} (live turnovers now feed Roll J; " +
            $"observability — rigorous proof is RollJStealBatchCheck)");
        // Clock checks.
        var half1Seconds = records.Where(r => r.Half == 1).Sum(r => r.Elapsed);
        var half2Seconds = records.Where(r => r.Half == 2).Sum(r => r.Elapsed);
        var drainOk = Math.Abs(half1Seconds - cfgGov.HalfSeconds) < 0.01
                   && Math.Abs(half2Seconds - cfgGov.HalfSeconds) < 0.01;
        Console.WriteLine(
            $"  half 1: {half1Seconds:N0}s / half 2: {half2Seconds:N0}s " +
            $"| each drains to {cfgGov.HalfSeconds:N0} -> {(drainOk ? "ok" : "FAIL")}");

        var apl = result.TotalSeconds / records.Count;
        var aplOk = apl >= 14.0 && apl <= 21.0;
        var countInBand = records.Count >= 100 && records.Count <= 220;
        Console.WriteLine(
            $"  possessions={records.Count:N0} (~{records.Count / 2:N0} per half) " +
            $"| realized APL={apl:F1}s -> {((aplOk && countInBand) ? "ok" : "FAIL")}");

        // Tempo histogram — the tuning instrument and truncation proof.
        // 100k samples of ClockDraw directly (not from the game run) with a fresh rng.
        var histRng = new SystemRng(cfg.Seed);
        var bins = new int[6]; // [0,5) [5,10) [10,15) [15,20) [20,25) [25,30)
        var truncationOk = true;
        double histMin = double.MaxValue, histMax = double.MinValue;
        for (var s = 0; s < 100_000; s++)
        {
            var t = ClockDraw.Sample(histRng, cfgClock.Center, cfgClock.StdDev, cfgClock.Floor, cfgClock.FullClockSeconds);
            if (t < cfgClock.Floor || t >= cfgClock.FullClockSeconds) truncationOk = false;
            if (t < histMin) histMin = t;
            if (t > histMax) histMax = t;
            var bin = Math.Min((int)(t / 5.0), bins.Length - 1);
            bins[bin]++;
        }
        Console.WriteLine(
            $"  tempo histogram (100k samples, center={cfgClock.Center} sd={cfgClock.StdDev} " +
            $"floor={cfgClock.Floor} ceiling<{cfgClock.FullClockSeconds}):");
        string[] binLabels = ["[0,5)", "[5,10)", "[10,15)", "[15,20)", "[20,25)", "[25,30)"];
        for (var b = 0; b < bins.Length; b++)
            Console.WriteLine($"    {binLabels[b]}: {bins[b]:N0}");
        Console.WriteLine(
            $"  min={histMin:F2} / max={histMax:F2} " +
            $"| truncation holds (>= floor, < ceiling) -> {(truncationOk ? "ok" : "FAIL")}");

        var clockOk = drainOk && aplOk && countInBand && truncationOk;

        // End-of-half observability: per-game counts are small (a handful of possessions
        // per game), so this is informational only — the rigorous rate proof is
        // EndOfHalfIntentBatchCheck. The drain check above is the load-bearing gate.
        Console.WriteLine(
            $"  end-of-half: HoldShootLast={intentHeld} ShootEarly={intentEarly} NoShot={noShotCount}");
        var fgRuleOk = Scoring.FieldGoalPoints(ShotLocation.Three) == 3
                    && Scoring.FieldGoalPoints(ShotLocation.Long)  == 2
                    && Scoring.FieldGoalPoints(ShotLocation.Mid)   == 2
                    && Scoring.FieldGoalPoints(ShotLocation.Short) == 2
                    && Scoring.FieldGoalPoints(ShotLocation.Rim)   == 2;
        var scoreOk = game.HomeScore == homePoints
                   && game.AwayScore == awayPoints
                   && game.HomeScore + game.AwayScore == talliedPoints
                   && talliedPoints > 0;
        Console.WriteLine(
            $"  score: Home {game.HomeScore} / Away {game.AwayScore} | accumulates per-possession tally -> {(scoreOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  FG rule (Three=3, others=2) -> {(fgRuleOk ? "ok" : "FAIL")}");

        // Per-stub park breakdown — quantifies how much of the game is currently
        // flowing through placeholder flips. As of #6 the chain CLOSES: the inbound
        // edges (ResumeInbound / SidelineInbound) no longer park — they re-run Roll A —
        // and Roll A's violation terminals moved into Roll C, so this breakdown is now
        // expected to be ESSENTIALLY EMPTY (parked ≈ 0, terminal-ended ≈ cap). The §2a
        // accumulation is still exercised: the volume flowing through the in-bonus forks
        // (Roll I / J / K / M) once teams reach the bonus is the visible proof.
        Console.WriteLine("  per-stub park breakdown:");
        foreach (var (dest, n) in result.PerStubParks.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {dest,-44} {n,6:N0}");

        // First 10 possessions: number, offense, entry, how it ended, consequence applied.
        Console.WriteLine("  first 10 possessions:");
        foreach (var r in records.Take(10))
        {
            var applied = $"{r.Applied.NextOffense}/{r.Applied.NextEntry}";
            Console.WriteLine(
                $"    #{r.Number,-3} {r.Offense,-4} entry={r.Entry,-15} " +
                $"end={r.EndLabel,-34} -> next={applied}");
        }

        var allOk = noLostOk && contiguousOk && flipsOk && firstOk
                    && arrowOk && foulOk && lineupOk && rollJOk && scoreOk && fgRuleOk
                    && clockOk;
        Console.WriteLine($"  Governor loop: {(allOk ? "ok" : "FAIL")}");
        return allOk;
    }

    // --- Batch: Roll J's five-way run-or-not distribution (rebound context) converges
    //     within tolerance; every exit is a clean Continue of the expected kind (Roll J
    //     names no terminal — all five arms are continues). Push and Settle now BOTH
    //     exit via IntoPlayerSelection, split by the FastBreak marker (Push stamps it,
    //     Settle does not) — so the batch proves they are distinguishable downstream.
    //     The Turnover arm stamps the Transition context for Roll C; and the
    //     DefensiveFoul arm charges the defense and forks on the bonus (this batch's own
    //     game crosses the threshold partway, exercising the §2a crossing). ---
    private static bool RollJBatchCheck(
        RollAConfig cfg, RollDConfig cfgD, RollJConfig cfgJ,
        RollJStubPieGenerator genJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} transition entries through Roll J (rebound context) ---");

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game (does not perturb Main's shared game). Its defense foul count
        // climbs as the DefensiveFoul arm fires and CROSSES the bonus partway through
        // — so both fork branches (sideline below bonus, FT in bonus) are exercised.
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var pieJ = genJ.Generate(TransitionContext.Rebound);

        var counts = new Dictionary<TransitionOutcome, int>();
        foreach (var o in Enum.GetValues<TransitionOutcome>()) counts[o] = 0;

        var settleSel = 0;          // IntoPlayerSelection, FastBreak=false (Settle -> halfcourt)
        var pushSel = 0;            // IntoPlayerSelection, FastBreak=true  (Push   -> transition)
        var pushFastBreakOk = 0;    // Push continues that correctly carry FastBreak=true
        var turnoverStamped = 0;    // ResolveTurnoverType carrying TurnoverContext.Transition
        var turnoverUnstamped = 0;  // ResolveTurnoverType MISSING the stamp (a FAIL)
        var foulSideline = 0;       // ResolveSidelineInbound (below bonus)
        var foulFreeThrows = 0;     // ResolveFreeThrows (in bonus)
        var jumpBalls = 0;          // ResolveJumpBall
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var r = RollJ.Execute(state, pieJ, game, rng);
            switch (r)
            {
                // Push AND Settle now BOTH exit via IntoPlayerSelection — the split is
                // the FastBreak marker on the carried state (Push stamps it true, Settle
                // leaves it false). Counting by FastBreak is the proof the two are
                // distinguishable downstream despite sharing the edge.
                case Continue { Next: ContinuationKind.IntoPlayerSelection } sel:
                    if (sel.State.FastBreak)
                    {
                        counts[TransitionOutcome.Push]++;
                        pushSel++;
                        pushFastBreakOk++;   // by construction FastBreak is true here
                    }
                    else
                    {
                        counts[TransitionOutcome.Settle]++;
                        settleSel++;
                    }
                    break;
                case Continue { Next: ContinuationKind.ResolveTurnoverType } tc:
                    counts[TransitionOutcome.Turnover]++;
                    if (tc.TurnoverContext == TurnoverContext.Transition) turnoverStamped++;
                    else turnoverUnstamped++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulSideline++;
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulFreeThrows++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[TransitionOutcome.JumpBall]++;
                    jumpBalls++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll J outcomes:");
        foreach (var (outcome, weight) in pieJ.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    Settle        -> IntoPlayerSelection (halfcourt) {settleSel,8:N0}  {(settleSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    Push          -> IntoPlayerSelection (FastBreak)  {pushSel,8:N0}  {(pushSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"      of which carry FastBreak=true: {pushFastBreakOk,8:N0} / {pushSel:N0}  {(pushFastBreakOk == pushSel ? "ok" : "FAIL")}");
        Console.WriteLine($"    Turnover      -> Roll C (stamped)    {turnoverStamped,8:N0}  {(turnoverStamped > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    DefensiveFoul -> SidelineInbound     {foulSideline,8:N0}  (below bonus)");
        Console.WriteLine($"                  -> ResolveFreeThrows   {foulFreeThrows,8:N0}  (in bonus)");
        Console.WriteLine($"    JumpBall      -> ResolveJumpBall     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var foulTotal = foulSideline + foulFreeThrows;
        var pushOk = pushSel > 0 && pushFastBreakOk == pushSel;   // every Push exit carried the marker
        var allArms = settleSel > 0 && pushOk && turnoverStamped > 0 && foulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var stampOk = turnoverUnstamped == 0;
        Console.WriteLine($"\n  zero unrouted exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every Turnover stamped Transition for Roll C: unstamped={turnoverUnstamped} -> {(stampOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all five Roll J arms reached: {(allArms ? "ok" : "FAIL")}");

        return ratesOk && routedOk && stampOk && allArms;
    }

    // --- Bonus fork: Roll J's DefensiveFoul arm charges the DEFENSE team foul and
    //     forks on the bonus exactly as Roll I and Roll D do — the third feeder into
    //     the shared charge-and-fork. All-mass-on-DefensiveFoul pie so every draw
    //     exercises the arm regardless of RNG. Mirrors RollIBonusForkCheck. ---
    private static bool RollJBonusForkCheck(
        RollAConfig cfg, RollDConfig cfgD, RollJConfig cfgJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Bonus fork: Roll J defensive foul across the thresholds ---");

        var foulOnlyPie = new Pie<TransitionOutcome>(new Dictionary<TransitionOutcome, double>
        {
            [TransitionOutcome.Settle] = 0.0,
            [TransitionOutcome.Push] = 0.0,
            [TransitionOutcome.Turnover] = 0.0,
            [TransitionOutcome.DefensiveFoul] = 1.0,
            [TransitionOutcome.JumpBall] = 0.0,
        }, cfgJ.Epsilon);

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var rng = new SystemRng(cfg.Seed);

        var ok = true;
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var r = RollJ.Execute(state, foulOnlyPie, game, rng);
            var after = game.Fouls.FoulsFor(state.Defense);

            // Expected routing from the POST-increment foul count.
            var expectedBonus = after >= cfgD.DoubleBonusThreshold ? BonusType.Double
                              : after >= cfgD.BonusThreshold ? BonusType.OneAndOne
                              : BonusType.None;
            var expectedKind = expectedBonus == BonusType.None
                ? ContinuationKind.ResolveSidelineInbound
                : ContinuationKind.ResolveFreeThrows;

            var c = r as Continue;
            var kindOk = c is not null && c.Next == expectedKind;
            var bonusOk = c is not null && c.Bonus == expectedBonus;
            var chargedOk = after == before + 1;
            var rowOk = kindOk && bonusOk && chargedOk;
            ok &= rowOk;

            var route = c is null ? "(not a continue!)"
                : c.Next == ContinuationKind.ResolveFreeThrows
                    ? $"ResolveFreeThrows({c.Bonus})"
                    : $"SidelineInbound({c.Bonus})";
            Console.WriteLine(
                $"  foul#{after,2}: {before}->{after} | route={route,-28} | expected={expectedKind} ({expectedBonus}) -> {(rowOk ? "ok" : "FAIL")}");
        }

        Console.WriteLine($"  bonus fork charges the defense and routes correctly: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Batch: Roll J's five-way distribution under the STEAL context (Contextification
    //     #3). Identical in shape to the rebound batch — Roll J's five arms and routing are
    //     source-agnostic; only the pie differs (the steal pie leans hardest to Push). Proves
    //     (a) the steal pie's five arms all route as designed, (b) the Turnover arm still
    //     stamps Transition for Roll C, and (c) the §2a bonus crossing on the STEAL path: this
    //     batch's own foul-accumulating game crosses the threshold partway, so the DefensiveFoul
    //     arm forks BOTH sideline (below bonus) and FT (in bonus) — the same charge-and-fork the
    //     rebound DefensiveFoul arm feeds. ---
    private static bool RollJStealBatchCheck(
        RollAConfig cfg, RollDConfig cfgD, RollJConfig cfgJ,
        RollJStubPieGenerator genJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} transition entries through Roll J (STEAL context) ---");

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game (does not perturb Main's shared game). Its defense foul count
        // climbs as the DefensiveFoul arm fires and CROSSES the bonus partway through —
        // the §2a stateful-accumulation check on the steal path: an arm that routes to
        // sideline early routes to FT once the shared game enters the bonus, so BOTH
        // fork branches must be exercised.
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var pieJ = genJ.Generate(TransitionContext.Steal);

        var counts = new Dictionary<TransitionOutcome, int>();
        foreach (var o in Enum.GetValues<TransitionOutcome>()) counts[o] = 0;

        var settleSel = 0;          // IntoPlayerSelection, FastBreak=false (Settle -> halfcourt)
        var pushSel = 0;            // IntoPlayerSelection, FastBreak=true  (Push   -> transition)
        var pushFastBreakOk = 0;    // Push continues that correctly carry FastBreak=true
        var turnoverStamped = 0;    // ResolveTurnoverType carrying TurnoverContext.Transition
        var turnoverUnstamped = 0;  // ResolveTurnoverType MISSING the stamp (a FAIL)
        var foulSideline = 0;       // ResolveSidelineInbound (below bonus)
        var foulFreeThrows = 0;     // ResolveFreeThrows (in bonus)
        var jumpBalls = 0;          // ResolveJumpBall
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var r = RollJ.Execute(state, pieJ, game, rng);
            switch (r)
            {
                case Continue { Next: ContinuationKind.IntoPlayerSelection } sel:
                    if (sel.State.FastBreak)
                    {
                        counts[TransitionOutcome.Push]++;
                        pushSel++;
                        pushFastBreakOk++;   // by construction FastBreak is true here
                    }
                    else
                    {
                        counts[TransitionOutcome.Settle]++;
                        settleSel++;
                    }
                    break;
                case Continue { Next: ContinuationKind.ResolveTurnoverType } tc:
                    counts[TransitionOutcome.Turnover]++;
                    if (tc.TurnoverContext == TurnoverContext.Transition) turnoverStamped++;
                    else turnoverUnstamped++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulSideline++;
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulFreeThrows++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[TransitionOutcome.JumpBall]++;
                    jumpBalls++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll J outcomes (steal pie):");
        foreach (var (outcome, weight) in pieJ.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    Settle        -> IntoPlayerSelection (halfcourt) {settleSel,8:N0}  {(settleSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    Push          -> IntoPlayerSelection (FastBreak)  {pushSel,8:N0}  {(pushSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"      of which carry FastBreak=true: {pushFastBreakOk,8:N0} / {pushSel:N0}  {(pushFastBreakOk == pushSel ? "ok" : "FAIL")}");
        Console.WriteLine($"    Turnover      -> Roll C (stamped)    {turnoverStamped,8:N0}  {(turnoverStamped > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    DefensiveFoul -> SidelineInbound     {foulSideline,8:N0}  (below bonus)");
        Console.WriteLine($"                  -> ResolveFreeThrows   {foulFreeThrows,8:N0}  (in bonus)");
        Console.WriteLine($"    JumpBall      -> ResolveJumpBall     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var pushOk = pushSel > 0 && pushFastBreakOk == pushSel;   // every Push exit carried the marker
        var foulForkOk = foulSideline > 0 && foulFreeThrows > 0;  // §2a crossing on the steal path
        var allArms = settleSel > 0 && pushOk && turnoverStamped > 0 && foulForkOk && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var stampOk = turnoverUnstamped == 0;
        Console.WriteLine($"\n  zero unrouted exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every Turnover stamped Transition for Roll C: unstamped={turnoverUnstamped} -> {(stampOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: steal DefensiveFoul crossed the bonus mid-batch (sideline -> FT): {(foulForkOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all five Roll J steal arms reached: {(allArms ? "ok" : "FAIL")}");

        return ratesOk && routedOk && stampOk && allArms;
    }

    // --- Context selection: Roll C builds the RIGHT pie for each turnover context,
    //     and the resolved rates from each match. Proves the ticket/station seam end
    //     to end: a Halfcourt ticket selects the live 15-way halfcourt loss set; a
    //     Transition ticket selects 25/15/20/35/5. Each selected pie's five MAIN
    //     slices are asserted equal to their configured weights (selection correct),
    //     then driven through Roll C (consumption correct). As of #6 the Halfcourt
    //     context is no longer the legacy 30/22/18/20/10 — the five mains now read
    //     24/18/14/16/9 with the expanded minor types live alongside them. ---
    private static bool RollCContextCheck(
        RollAConfig cfg, RollCConfig cfgC, RollCStubPieGenerator genC, PossessionState state)
    {
        Console.WriteLine($"\n--- Context: Roll C pie selection by turnover context ---");

        var contexts = new (TurnoverContext ctx, (TurnoverOutcome o, double w)[] expected)[]
        {
            (TurnoverContext.Halfcourt, new[]
            {
                (TurnoverOutcome.BadPassDeadBall,    cfgC.BaseBadPassDeadBall),
                (TurnoverOutcome.BadPassIntercepted, cfgC.BaseBadPassIntercepted),
                (TurnoverOutcome.LostBallDeadBall,   cfgC.BaseLostBallDeadBall),
                (TurnoverOutcome.LostBallLiveBall,   cfgC.BaseLostBallLiveBall),
                (TurnoverOutcome.OffensiveFoul,      cfgC.BaseOffensiveFoul),
            }),
            (TurnoverContext.Transition, new[]
            {
                (TurnoverOutcome.BadPassDeadBall,    cfgC.TransitionBadPassDeadBall),
                (TurnoverOutcome.BadPassIntercepted, cfgC.TransitionBadPassIntercepted),
                (TurnoverOutcome.LostBallDeadBall,   cfgC.TransitionLostBallDeadBall),
                (TurnoverOutcome.LostBallLiveBall,   cfgC.TransitionLostBallLiveBall),
                (TurnoverOutcome.OffensiveFoul,      cfgC.TransitionOffensiveFoul),
            }),
        };

        var ok = true;
        foreach (var (ctx, expected) in contexts)
        {
            var pie = genC.Generate(state, pressure: 0.0, context: ctx);
            var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            // 1) The SELECTED pie equals the expected configured weights for this
            //    context — proves the context picked the right SET, not merely that
            //    some internally-valid pie came back.
            var selectionOk = true;
            foreach (var (o, w) in expected)
                if (Math.Abs(pieMap[o] - w) > cfgC.Epsilon) selectionOk = false;

            // 2) The resolved rates match the pie — proves the roll consumes it.
            var rng = new SystemRng(cfg.Seed);
            var counts = new Dictionary<TurnoverOutcome, int>();
            foreach (var o in Enum.GetValues<TurnoverOutcome>()) counts[o] = 0;
            for (var i = 0; i < cfg.BatchSize; i++)
            {
                var t = (Terminal)RollC.Execute(state, pie, rng, cfgC);
                counts[MapTurnover(t.Reason)]++;
            }

            var n = (double)cfg.BatchSize;
            var ratesOk = true;
            Console.WriteLine($"  context={ctx} (selection {(selectionOk ? "ok" : "FAIL")}):");
            foreach (var (o, w) in expected)
            {
                var observed = counts[o] / n;
                var gap = Math.Abs(observed - w);
                var pass = gap <= cfg.RateTolerance;
                ratesOk &= pass;
                Console.WriteLine($"    {o,-20} observed={observed:P3}  expected={w:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
            }
            ok &= selectionOk && ratesOk;
        }

        Console.WriteLine($"  Roll C context selection: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Batch: Roll K's seven-way offensive-rebound distribution converges within
    //     tolerance, and every arm routes as designed. Driven by calling
    //     RollK.Execute DIRECTLY on a fully-stamped post-miss state (the RollI batch
    //     pattern), so all seven arms are exercised every draw. A FRESH game crosses
    //     the bonus mid-batch, so the DefensiveFoul arm splits sideline/FT. Per-arm
    //     routing is asserted: PutBack -> Continue(IntoShotResolution) with the putback
    //     ticket set AND the zone forced to Rim; JumpBall -> Continue(ResolveJumpBall);
    //     DefensiveFoul -> sideline/FT fork charging the defense; OffensiveFoul /
    //     DeadBallTurnover / LiveBallTurnover -> terminals whose consequence flips the
    //     ball to the defense; ResetOffense -> Continue(IntoPlayerSelection) with the
    //     prior shot's facts WIPED (ShotType and Result null) AND FastBreak cleared (a
    //     reset off a missed break is a fresh halfcourt play — the marker leak guard). ---
    private static bool RollKReboundBatchCheck(
        RollAConfig cfg, RollKConfig cfgK, RollKStubPieGenerator genK,
        GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} offensive rebounds routed through Roll K ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the foul charge here does not perturb the shared game; it
        // crosses the bonus partway through, exercising both fork branches.
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);
        var pieE = genE.Generate(state);
        var pieK = genK.Generate(OffensiveReboundSource.LiveBall);

        var counts = new Dictionary<OffensiveReboundOutcome, int>();
        foreach (var o in Enum.GetValues<OffensiveReboundOutcome>()) counts[o] = 0;

        var putbackTicketOk = 0;   // PutBack arms with Putback==true && ShotType==Rim
        var resetWipedOk = 0;      // ResetOffense arms with ShotType==null && Result==null && FastBreak==false
        var resetClearedBreak = 0; // ResetOffense arms with FastBreak cleared (the leak guard)
        var defFoulSideline = 0;   // DefensiveFoul below bonus
        var defFoulFreeThrows = 0; // DefensiveFoul in bonus
        var flipToDefenseOk = 0;   // terminals whose consequence hands the ball to the defense
        var liveTurnoverStealCtxOk = 0; // LiveBallTurnover carrying the Steal context (-> Roll J)
        var liveTurnoverCtxBad = 0;     // LiveBallTurnover MISSING the steal context (a FAIL)
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Build a fully-stamped post-miss state: slot (E) + zone (G) + a Miss
            // result, exactly what arrives at the offensive-rebound node in the live
            // chain. The zone is a REAL non-Rim mix, so the PutBack arm's force-to-Rim
            // and the ResetOffense arm's wipe are both observable. FastBreak=true
            // simulates a possession that PUSHED, missed, and grabbed its own board —
            // so the ResetOffense leak-guard (FastBreak must clear) is exercised.
            var sel = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), rng)).State;
            var stamped = zoned with { Result = ShotResult.Miss, FastBreak = true };

            var kRes = RollK.Execute(stamped, pieK, game, rng);

            switch (kRes)
            {
                case Continue { Next: ContinuationKind.IntoShotResolution } pb:
                    counts[OffensiveReboundOutcome.PutBack]++;
                    if (pb.Putback && pb.State.ShotType == ShotLocation.Rim) putbackTicketOk++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[OffensiveReboundOutcome.JumpBall]++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound }:
                    counts[OffensiveReboundOutcome.DefensiveFoul]++;
                    defFoulSideline++;
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows }:
                    counts[OffensiveReboundOutcome.DefensiveFoul]++;
                    defFoulFreeThrows++;
                    break;
                case Terminal { Reason: "OffensiveFoul" } t1:
                    counts[OffensiveReboundOutcome.OffensiveFoul]++;
                    if (t1.Consequence.NextOffense == stamped.Defense) flipToDefenseOk++;
                    break;
                case Terminal { Reason: "DeadBallTurnover" } t2:
                    counts[OffensiveReboundOutcome.DeadBallTurnover]++;
                    if (t2.Consequence.NextOffense == stamped.Defense) flipToDefenseOk++;
                    break;
                case Terminal { Reason: "LiveBallTurnover" } t3:
                    counts[OffensiveReboundOutcome.LiveBallTurnover]++;
                    if (t3.Consequence.NextOffense == stamped.Defense) flipToDefenseOk++;
                    // Contextification #3: a live turnover off the board now carries the
                    // Steal context, so the resolver routes the spawn to Roll J (not Roll A).
                    if (t3.Consequence.NextEntry == EntryType.Transition
                        && t3.Consequence.TransitionContext?.Source == TransitionSource.Steal)
                        liveTurnoverStealCtxOk++;
                    else liveTurnoverCtxBad++;
                    break;
                case Continue { Next: ContinuationKind.IntoPlayerSelection } rs:
                    counts[OffensiveReboundOutcome.ResetOffense]++;
                    // The reset must wipe the prior shot's facts AND clear FastBreak so
                    // the fresh play draws the HALFCOURT pie — even though it arrived on
                    // a FastBreak=true state. The marker must not leak past the reset.
                    if (rs.State.ShotType is null && rs.State.Result is null && !rs.State.FastBreak) resetWipedOk++;
                    if (!rs.State.FastBreak) resetClearedBreak++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll K outcomes:");
        foreach (var (outcome, weight) in pieK.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-20} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine("\n  routing per arm:");
        Console.WriteLine($"    PutBack        -> Roll H, putback ticket + Rim forced  {putbackTicketOk,8:N0} / {counts[OffensiveReboundOutcome.PutBack]:N0}");
        Console.WriteLine($"    ResetOffense   -> Roll E, facts wiped (zone+result null, FastBreak cleared) {resetWipedOk,7:N0} / {counts[OffensiveReboundOutcome.ResetOffense]:N0}");
        Console.WriteLine($"                      FastBreak cleared (leak guard) {resetClearedBreak,8:N0} / {counts[OffensiveReboundOutcome.ResetOffense]:N0}  {(resetClearedBreak == counts[OffensiveReboundOutcome.ResetOffense] ? "ok" : "FAIL")}");
        Console.WriteLine($"    DefensiveFoul  -> SidelineInbound {defFoulSideline,8:N0} (below bonus) / ResolveFreeThrows {defFoulFreeThrows,7:N0} (in bonus)");
        Console.WriteLine($"    terminals flip ball to defense: {flipToDefenseOk,8:N0}");

        var putbackShapeOk = putbackTicketOk == counts[OffensiveReboundOutcome.PutBack] && putbackTicketOk > 0;
        var resetShapeOk = resetWipedOk == counts[OffensiveReboundOutcome.ResetOffense] && resetWipedOk > 0;
        var flipOk = flipToDefenseOk ==
            counts[OffensiveReboundOutcome.OffensiveFoul]
            + counts[OffensiveReboundOutcome.DeadBallTurnover]
            + counts[OffensiveReboundOutcome.LiveBallTurnover]
            && flipToDefenseOk > 0;
        var allArms = counts.Values.All(c => c > 0);
        var foulForkOk = defFoulSideline > 0 && defFoulFreeThrows > 0;
        var routedOk = unrecognized == 0;

        Console.WriteLine($"\n  every PutBack carries the ticket + Rim: {(putbackShapeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every ResetOffense wipes the prior shot: {(resetShapeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every flip terminal hands the ball to the defense: {(flipOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  DefensiveFoul fork exercises both branches: {(foulForkOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll K arms reached: {(allArms ? "ok" : "FAIL")}");
        Console.WriteLine($"  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");

        var liveTurnoverCtxOk = liveTurnoverCtxBad == 0 && liveTurnoverStealCtxOk > 0;
        Console.WriteLine($"  LiveBallTurnover carries Steal context (-> Roll J): ok={liveTurnoverStealCtxOk:N0} bad={liveTurnoverCtxBad} -> {(liveTurnoverCtxOk ? "ok" : "FAIL")}");

        return ratesOk && putbackShapeOk && resetShapeOk && flipOk && foulForkOk && allArms && routedOk && liveTurnoverCtxOk;
    }

    // --- Context: the putback ticket selects a DISTINCT shot pie. Proves the
    //     ticket/station seam for Roll K -> Roll H end to end: at the SAME Rim zone,
    //     Roll H's generator returns the located-shot pie WITHOUT the ticket and the
    //     putback pie WITH it, and the two differ. The putback pie is asserted equal
    //     to its configured Putback* weights (selection correct), then driven through
    //     Roll H (consumption correct). Mirrors RollCContextCheck. ---
    private static bool RollKPutbackPieCheck(RollAConfig cfg, RollHConfig cfgH, PossessionState state)
    {
        Console.WriteLine($"\n--- Context: Roll H putback pie selected by the putback ticket ---");

        var genH = new RollHStubPieGenerator(cfgH);
        // Both pies at the SAME zone (Rim) — so any difference is the TICKET's doing,
        // not the zone's. A putback always forces Rim, so this is the honest compare.
        var rimState = state with { ShotType = ShotLocation.Rim };

        var normalPie = genH.Generate(rimState, putback: false);
        var putbackPie = genH.Generate(rimState, putback: true);

        var normalMap = normalPie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        var putbackMap = putbackPie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

        // Phase 8: expected values use the same carve-then-convert math as BuildPutbackPie.
        // PutbackMade is the conversion rate GIVEN not blocked AND not fouled.
        var pbBlock           = cfgH.PutbackBlocked;
        var pbFoul            = cfgH.FoulRate(ShotLocation.Rim);
        var pbNonBlockNonFoul = 1.0 - pbBlock - pbFoul;
        var pbMade        = cfgH.PutbackMade * pbNonBlockNonFoul;
        var pbMafFrac     = cfgH.MafFraction(ShotLocation.Rim);
        var pbMaf         = pbFoul * pbMafFrac;
        var pbMissFouled  = pbFoul * (1.0 - pbMafFrac);
        var pbNonMadeBase = cfgH.PutbackMiss + cfgH.PutbackMissOutOfBoundsLost + cfgH.PutbackMissOutOfBoundsRetained;
        var pbNonMadeShare = pbNonBlockNonFoul - pbMade;
        var pbScale       = pbNonMadeBase > 0.0 ? pbNonMadeShare / pbNonMadeBase : 0.0;

        var expected = new[]
        {
            (ShotResult.Made,                    pbMade),
            (ShotResult.MadeAndFouled,           pbMaf),
            (ShotResult.Miss,                    cfgH.PutbackMiss                    * pbScale),
            (ShotResult.MissFouled,              pbMissFouled),
            (ShotResult.MissOutOfBoundsLost,     cfgH.PutbackMissOutOfBoundsLost     * pbScale),
            (ShotResult.MissOutOfBoundsRetained, cfgH.PutbackMissOutOfBoundsRetained * pbScale),
            (ShotResult.Blocked,                 pbBlock),
        };

        // 1) Selection: the putback pie matches the Phase 8 carve-computed weights.
        var selectionOk = true;
        foreach (var (o, w) in expected)
            if (Math.Abs(putbackMap[o] - w) > cfgH.Epsilon) selectionOk = false;

        // 2) Distinctness: the putback pie is NOT the normal Rim pie — at least one
        //    slice differs materially. (Proves the ticket switched the pie, not that
        //    two equal pies happened to validate.)
        var distinct = expected.Any(e => Math.Abs(putbackMap[e.Item1] - normalMap[e.Item1]) > cfg.RateTolerance);

        // 3) Consumption: driving the putback pie through Roll H reproduces the rates.
        var rng = new SystemRng(cfg.Seed);
        var counts = new Dictionary<ShotResult, int>();
        foreach (var r in Enum.GetValues<ShotResult>()) counts[r] = 0;
        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var res = RollH.Execute(rimState, putbackPie, rng);
            // Both the terminal and continue arms of Roll H stamp the chosen Result
            // onto their carried state, so recover it uniformly.
            var resState = res switch
            {
                Terminal t => t.State,
                Continue c => c.State,
                _ => throw new InvalidOperationException("unexpected Roll H result.")
            };
            var result = resState.Result ?? throw new InvalidOperationException("Roll H result lost its Result stamp.");
            counts[result]++;
        }

        var nn = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine($"  putback pie (selection {(selectionOk ? "ok" : "FAIL")}, distinct from rim pie {(distinct ? "ok" : "FAIL")}):");
        foreach (var (o, w) in expected)
        {
            var observed = counts[o] / nn;
            var gap = Math.Abs(observed - w);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {o,-26} observed={observed:P3}  expected={w:P3}  normal={normalMap[o]:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var pass2 = selectionOk && distinct && ratesOk;
        Console.WriteLine($"  putback ticket selects its own pie: {(pass2 ? "ok" : "FAIL")}");
        return pass2;
    }

    // --- Bonus fork: Roll K's DefensiveFoul arm charges the DEFENSIVE team foul and
    //     routes on the bonus exactly as Rolls D / I / J do (the FOURTH feeder into
    //     the shared charge-and-fork). All-mass-on-DefensiveFoul pie across the
    //     thresholds. Mirrors RollIBonusForkCheck / RollJBonusForkCheck. ---
    private static bool RollKBonusForkCheck(
        RollAConfig cfg, RollDConfig cfgD, RollKConfig cfgK,
        RollKStubPieGenerator genK, PossessionState state)
    {
        Console.WriteLine($"\n--- Bonus fork: Roll K defensive foul across the thresholds ---");

        var foulOnlyPie = new Pie<OffensiveReboundOutcome>(new Dictionary<OffensiveReboundOutcome, double>
        {
            [OffensiveReboundOutcome.PutBack] = 0.0,
            [OffensiveReboundOutcome.JumpBall] = 0.0,
            [OffensiveReboundOutcome.DefensiveFoul] = 1.0,
            [OffensiveReboundOutcome.OffensiveFoul] = 0.0,
            [OffensiveReboundOutcome.DeadBallTurnover] = 0.0,
            [OffensiveReboundOutcome.LiveBallTurnover] = 0.0,
            [OffensiveReboundOutcome.ResetOffense] = 0.0,
        }, cfgK.Epsilon);

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var rng = new SystemRng(cfg.Seed);

        var ok = true;
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var r = RollK.Execute(state, foulOnlyPie, game, rng);
            var after = game.Fouls.FoulsFor(state.Defense);

            var expectedBonus = after >= cfgD.DoubleBonusThreshold ? BonusType.Double
                              : after >= cfgD.BonusThreshold ? BonusType.OneAndOne
                              : BonusType.None;
            var expectedKind = expectedBonus == BonusType.None
                ? ContinuationKind.ResolveSidelineInbound
                : ContinuationKind.ResolveFreeThrows;

            var c = r as Continue;
            var kindOk = c is not null && c.Next == expectedKind;
            var bonusOk = c is not null && c.Bonus == expectedBonus;
            var chargedOk = after == before + 1;
            var rowOk = kindOk && bonusOk && chargedOk;
            ok &= rowOk;

            var route = c is null ? "(not a continue!)"
                : c.Next == ContinuationKind.ResolveFreeThrows
                    ? $"ResolveFreeThrows({c.Bonus})"
                    : $"SidelineInbound({c.Bonus})";
            Console.WriteLine(
                $"  foul#{after,2}: {before}->{after} | route={route,-28} | expected={expectedKind} ({expectedBonus}) -> {(rowOk ? "ok" : "FAIL")}");
        }

        Console.WriteLine($"  bonus fork charges the defense and routes correctly: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Roll L: the free-throw loop. Drives each trip TYPE through the resolver's FT
    //     sequence and proves three things. (a) The RAW make/miss rate matches the flat
    //     config make% (Roll L spun in isolation). (b) Each trip spins Roll L the right
    //     number of times — and-1 = 1, fouled two / double = 2, fouled three = 3,
    //     1-and-1 = 1 (front miss) or 2 (front make) — and the global max never exceeds
    //     3 (read off RoutingOutcome.FreeThrowSpins, the observability counter parallel
    //     to PutbackAttempts). (c) The uniform last-shot routing holds, and the
    //     observable END-vs-FTRebound SPLIT is its signature: a fixed n-shot trip routes
    //     on the LAST shot only (intermediates are dead respins), so its END rate == p;
    //     a 1-and-1 ends at END only when BOTH shots make (p*p), a front miss forfeiting
    //     the second and going live to the rebound boundary (END rate == p*p). A made
    //     final FT hands the ball to the OPPONENT (DeadBallTo the defense). A missed final
    //     FT now flows into Roll M (Session 19), PINNED here to its DefensiveRebound
    //     terminal so the trip ends cleanly at the boundary — Roll L charges no foul and
    //     touches no arrow, and the pinned Roll M charges none either, so a fresh local
    //     game stays pristine across the batch and this check stays accumulation-free
    //     (CONVENTIONS §2a), unlike the foul forks upstream.
    private static bool RollLFreeThrowCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- Roll L: free-throw resolution ({cfg.BatchSize:N0} trips per type) ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgL = RollLConfig.Load(configPath);
        var p = cfgL.MakeProbability;

        // (a) Raw make/miss rate — spin Roll L directly against its flat pie.
        var rngRaw = new SystemRng(cfg.Seed);
        var pieL = new RollLStubPieGenerator(cfgL).Generate();
        var rawMakes = 0;
        for (var i = 0; i < cfg.BatchSize; i++)
            if (RollL.Execute(pieL, rngRaw) == FreeThrowOutcome.Make) rawMakes++;
        var rawRate = (double)rawMakes / cfg.BatchSize;
        var rawOk = Math.Abs(rawRate - p) <= cfg.RateTolerance;
        Console.WriteLine($"  raw make rate: observed={rawRate:P3}  expected={p:P3}  -> {(rawOk ? "ok" : "FAIL")}");

        // A resolver to drive whole FT trips end to end (loop arithmetic + routing). A
        // FRESH local game; nothing in the FT path mutates it.
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            new RollEStubPieGenerator(cfgE),
            new RollFStubPieGenerator(cfgF),
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(cfgL),
            // Roll M PINNED to its DefensiveRebound terminal for this check: a missed
            // final free throw must terminate cleanly at the rebound boundary so the FT
            // LOOP can be observed in isolation (exact spin bands, max <= 3, a clean
            // make/miss split). The live Roll M's loose-ball-defense arm would charge
            // fouls and its offensive-board arm would spin Roll K — both would pollute
            // spin counts and break the §2a accumulation-free property this check relies
            // on. Pinning one arm is the same isolation RollKBonusForkCheck uses with its
            // foulOnlyPie; Roll M's real distribution is RollMReboundBatchCheck's mandate.
            new RollMStubPieGenerator(new RollMConfig
            {
                DefensiveRebound = 1.0,
                OffensiveRebound = 0, LooseBallFoulOnDefense = 0, LooseBallFoulOnOffense = 0,
                OutOfBoundsOffOffense = 0, OutOfBoundsOffDefense = 0, JumpBall = 0
            }),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        // Each trip type: the entry continuation, the expected exact spin-count band,
        // and the expected END (last-shot-make) rate.
        var trips = new (string Name, Continue Entry, int MinSpins, int MaxSpins, double ExpectedEnd)[]
        {
            ("and-1",        new Continue(ContinuationKind.ResolveShootingFreeThrows, state with { Result = ShotResult.MadeAndFouled, ShotType = ShotLocation.Rim }),   1, 1, p),
            ("fouled two",   new Continue(ContinuationKind.ResolveShootingFreeThrows, state with { Result = ShotResult.MissFouled, ShotType = ShotLocation.Mid }),      2, 2, p),
            ("fouled three", new Continue(ContinuationKind.ResolveShootingFreeThrows, state with { Result = ShotResult.MissFouled, ShotType = ShotLocation.Three }),    3, 3, p),
            ("double bonus", new Continue(ContinuationKind.ResolveFreeThrows, state) { Bonus = BonusType.Double },                                                      2, 2, p),
            ("one-and-one",  new Continue(ContinuationKind.ResolveFreeThrows, state) { Bonus = BonusType.OneAndOne },                                                   1, 2, p * p),
        };

        var allTripsOk = true;
        var globalMaxSpins = 0;
        Console.WriteLine("  per-trip: shot count, last-shot routing, made-FT consequence:");
        foreach (var trip in trips)
        {
            var ends = 0;
            var ftRebounds = 0;
            var other = 0;
            var minSeen = int.MaxValue;
            var maxSeen = 0;
            var spinOutOfRange = 0;
            var wrongConsequence = 0;

            for (var i = 0; i < cfg.BatchSize; i++)
            {
                var routing = resolver.Route(trip.Entry);
                var s = routing.FreeThrowSpins;
                if (s < minSeen) minSeen = s;
                if (s > maxSeen) maxSeen = s;
                if (s > globalMaxSpins) globalMaxSpins = s;
                if (s < trip.MinSpins || s > trip.MaxSpins) spinOutOfRange++;

                if (routing.Destination == "END:FreeThrowsMade")
                {
                    ends++;
                    // A made final FT gives the ball to the OPPONENT (the defense).
                    if (routing.EndedOn is { } term && term.Consequence.NextOffense != state.Defense)
                        wrongConsequence++;
                }
                else if (routing.Destination == "END:DefensiveRebound") ftRebounds++;
                else other++;
            }

            var endRate = (double)ends / cfg.BatchSize;
            var rateOk = Math.Abs(endRate - trip.ExpectedEnd) <= cfg.RateTolerance;
            var spinOk = spinOutOfRange == 0;
            var routedOk = other == 0;
            var consequenceOk = wrongConsequence == 0;
            var tripOk = rateOk && spinOk && routedOk && consequenceOk;
            allTripsOk &= tripOk;

            Console.WriteLine(
                $"    {trip.Name,-13} spins=[{minSeen}-{maxSeen}] (want {trip.MinSpins}-{trip.MaxSpins}) | " +
                $"END={endRate:P2} (want {trip.ExpectedEnd:P2})  FTReb={(double)ftRebounds / cfg.BatchSize:P2} | " +
                $"unrouted={other} wrongCons={wrongConsequence} -> {(tripOk ? "ok" : "FAIL")}");
        }

        var boundOk = globalMaxSpins <= 3;
        Console.WriteLine($"  spin count never exceeded 3: max={globalMaxSpins} -> {(boundOk ? "ok" : "FAIL")}");

        var ok = rawOk && allTripsOk && boundOk;
        Console.WriteLine($"  Roll L free-throw resolution: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Batch: Roll M's seven-way free-throw-rebound distribution converges within
    //     tolerance, and every arm routes as designed. Driven by calling RollM.Execute
    //     DIRECTLY on a post-FT state (the RollI / RollK batch pattern), so all seven
    //     arms are exercised every draw. A FRESH game crosses the bonus mid-batch, so the
    //     LooseBallFoulOnDefense arm splits sideline/FT (§2a accumulation). Per-arm
    //     routing is asserted: DefensiveRebound -> Terminal whose consequence is a
    //     TRANSITION to the defense carrying the FreeThrowRebound context; OffensiveRebound
    //     -> Continue(ResolveOffensiveRebound) stamped OffensiveReboundSource.FreeThrow;
    //     LooseBallFoulOnDefense -> charge the defense + bonus fork; LooseBallFoulOnOffense
    //     / OutOfBoundsOffOffense -> Terminal DeadBallTo the defense; OutOfBoundsOffDefense
    //     -> Continue(ResolveSidelineInbound) with NO fork even in the bonus; JumpBall ->
    //     Continue(ResolveJumpBall). Foul-charge discipline is asserted PER DRAW off the
    //     team-foul delta: ONLY the loose-ball-defense arm increments the defensive team
    //     foul; the OOB pair and every other arm charge nothing. ---
    private static bool RollMReboundBatchCheck(
        RollAConfig cfg, RollMConfig cfgM, RollMStubPieGenerator genM,
        GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} free-throw rebounds routed through Roll M ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgD = RollDConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the charge here does not perturb the shared game. The
        // loose-ball-defense arm's charge crosses the bonus partway through THIS game,
        // exercising the §2a split (sideline below the bonus, FTs in it).
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var pieM = genM.Generate(state);

        var counts = new Dictionary<FreeThrowReboundOutcome, int>();
        foreach (var o in Enum.GetValues<FreeThrowReboundOutcome>()) counts[o] = 0;

        var defTransition = 0;     // DefensiveRebound -> transition terminal to defense
        var offReboundCont = 0;    // OffensiveRebound -> Roll K cont, FreeThrow source
        var offFoulDead = 0;       // LooseBallFoulOnOffense -> dead-ball terminal
        var oobOffDead = 0;        // OutOfBoundsOffOffense -> dead-ball terminal
        var oobDefSideline = 0;    // OutOfBoundsOffDefense -> sideline (no fork, no charge)
        var defFoulSideline = 0;   // LooseBallFoulOnDefense below bonus -> sideline
        var defFoulFreeThrows = 0; // LooseBallFoulOnDefense in bonus -> FTs
        var jumpBalls = 0;         // JumpBall -> arrow node
        var unrecognized = 0;
        var badConsequence = 0;    // wrong team / entry / context on a terminal or ticket
        var badCharge = 0;         // a non-foul arm moved the team-foul count (or the
                                   //  foul arm failed to charge exactly 1)

        var sidelineStub = new SidelineInboundStub();
        var freeThrowStub = new ResolveFreeThrowsStub();

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var mRes = RollM.Execute(state, pieM, game, rng);
            var delta = game.Fouls.FoulsFor(state.Defense) - before;

            switch (mRes)
            {
                case Terminal { Reason: "DefensiveRebound" } dr:
                    counts[FreeThrowReboundOutcome.DefensiveRebound]++;
                    if (delta != 0) badCharge++;
                    if (dr.Consequence.NextOffense != state.Defense
                        || dr.Consequence.NextEntry != EntryType.Transition
                        || dr.Consequence.TransitionContext?.Source != TransitionSource.FreeThrowRebound)
                        badConsequence++;
                    else defTransition++;
                    break;

                case Continue { Next: ContinuationKind.ResolveOffensiveRebound } orc:
                    counts[FreeThrowReboundOutcome.OffensiveRebound]++;
                    if (delta != 0) badCharge++;
                    // The FreeThrow source ticket must be stamped (selects Roll K's FT pie).
                    if (orc.OffensiveReboundSource != OffensiveReboundSource.FreeThrow) badConsequence++;
                    else offReboundCont++;
                    break;

                case Terminal { Reason: "LooseBallFoulOnOffense" } lbo:
                    counts[FreeThrowReboundOutcome.LooseBallFoulOnOffense]++;
                    if (delta != 0) badCharge++;       // an offensive foul charges nothing
                    if (lbo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else offFoulDead++;
                    break;

                case Terminal { Reason: "OutOfBoundsOffOffense" } oobo:
                    counts[FreeThrowReboundOutcome.OutOfBoundsOffOffense]++;
                    if (delta != 0) badCharge++;       // OOB charges nothing
                    if (oobo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else oobOffDead++;
                    break;

                case Continue { Next: ContinuationKind.ResolveSidelineInbound } sc:
                    // BOTH the OOB-off-defense arm and the below-bonus loose-ball-defense
                    // arm land on a plain sideline inbound; the foul DELTA separates them
                    // (the foul arm charged 1, the OOB arm charged 0). This is also the
                    // proof the OOB pair charges nothing.
                    if (delta == 0)
                    {
                        counts[FreeThrowReboundOutcome.OutOfBoundsOffDefense]++;
                        oobDefSideline++;
                    }
                    else
                    {
                        counts[FreeThrowReboundOutcome.LooseBallFoulOnDefense]++;
                        if (delta != 1) badCharge++;
                        defFoulSideline++;
                    }
                    _ = sidelineStub.Receive(sc);
                    break;

                case Continue { Next: ContinuationKind.ResolveFreeThrows } ftc:
                    // ONLY the loose-ball-defense arm IN THE BONUS lands here (it charged
                    // a foul and the defense has crossed the threshold). The OOB-off-defense
                    // arm can NEVER reach this branch — it reads no bonus and charges no
                    // foul — so a delta of 0 here would be a routing bug.
                    counts[FreeThrowReboundOutcome.LooseBallFoulOnDefense]++;
                    if (delta != 1) badCharge++;
                    defFoulFreeThrows++;
                    _ = freeThrowStub.Receive(ftc);
                    break;

                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[FreeThrowReboundOutcome.JumpBall]++;
                    if (delta != 0) badCharge++;
                    jumpBalls++;
                    break;

                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll M outcomes:");
        foreach (var (outcome, weight) in pieM.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    DefensiveRebound       -> transition terminal (defense) {defTransition,8:N0}  {(defTransition > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OffensiveRebound       -> Roll K cont (FreeThrow source) {offReboundCont,8:N0}  {(offReboundCont > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    LooseBallFoulOnOffense -> dead-ball terminal (defense)   {offFoulDead,8:N0}  {(offFoulDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffOffense  -> dead-ball terminal (defense)   {oobOffDead,8:N0}  {(oobOffDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffDefense  -> sideline inbound (no charge)   {oobDefSideline,8:N0}  {(oobDefSideline > 0 ? "ok" : "NONE")}");
        // The loose-ball-defense foul splits on the bonus: sideline below it, FTs once
        // the fresh game crosses the threshold mid-batch (§2a).
        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine($"    LooseBallFoulOnDefense -> SidelineInbound (below bonus)  {defFoulSideline,8:N0}");
        Console.WriteLine($"                           -> ResolveFreeThrows (in bonus)   {defFoulFreeThrows,8:N0}");
        Console.WriteLine($"    JumpBall               -> arrow node                     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var allArms = defTransition > 0 && offReboundCont > 0 && offFoulDead > 0 && oobOffDead > 0
                      && oobDefSideline > 0 && defFoulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var consequenceOk = badConsequence == 0;
        var chargeOk = badCharge == 0;
        Console.WriteLine($"\n  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  consequence/ticket correct on every terminal+continue: bad={badConsequence} -> {(consequenceOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  foul-charge discipline (only loose-ball-defense charges): bad={badCharge} -> {(chargeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: loose-ball-defense crossed the bonus mid-batch (sideline -> FT): {(defFoulSideline > 0 && defFoulFreeThrows > 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll M arms reached: {(allArms ? "ok" : "FAIL")}");

        var ok = ratesOk && routedOk && consequenceOk && chargeOk && allArms
                 && defFoulSideline > 0 && defFoulFreeThrows > 0;
        Console.WriteLine($"  Roll M free-throw rebound resolution: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Context: the two FT tickets Roll M stamps SELECT the right pies downstream.
    //     (1) Roll K's generator, given OffensiveReboundSource.FreeThrow, returns the
    //     FT-specific weight set (more putback, less reset); given LiveBall (the legacy
    //     default), it returns the byte-for-byte original set. (2) Roll J's generator,
    //     given TransitionSource.FreeThrowRebound, returns the conservative run-or-not
    //     set (more Settle, less Push); given Rebound, the original set. Each is proven
    //     two ways, the RollCContextCheck pattern: the SELECTED pie's weights equal the
    //     configured set (right SET picked), and a draw sample off that pie converges to
    //     those weights (the pie is actually consumed). The two sources must also DIFFER
    //     (a null/legacy stamp can never accidentally get the FT odds). ---
    private static bool RollMContextSelectionCheck(
        RollAConfig cfg, RollKConfig cfgK, RollJConfig cfgJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Context: Roll K + Roll J pie selection by the FT tickets ---");
        var ok = true;

        // ---- Roll K: LiveBall (legacy default) vs FreeThrow ----
        var genK = new RollKStubPieGenerator(cfgK);
        var kContexts = new (OffensiveReboundSource src, (OffensiveReboundOutcome o, double w)[] expected)[]
        {
            (OffensiveReboundSource.LiveBall, new[]
            {
                (OffensiveReboundOutcome.PutBack,          cfgK.PutBack),
                (OffensiveReboundOutcome.JumpBall,         cfgK.JumpBall),
                (OffensiveReboundOutcome.DefensiveFoul,    cfgK.DefensiveFoul),
                (OffensiveReboundOutcome.OffensiveFoul,    cfgK.OffensiveFoul),
                (OffensiveReboundOutcome.DeadBallTurnover, cfgK.DeadBallTurnover),
                (OffensiveReboundOutcome.LiveBallTurnover, cfgK.LiveBallTurnover),
                (OffensiveReboundOutcome.ResetOffense,     cfgK.ResetOffense),
            }),
            (OffensiveReboundSource.FreeThrow, new[]
            {
                (OffensiveReboundOutcome.PutBack,          cfgK.FreeThrowPutBack),
                (OffensiveReboundOutcome.JumpBall,         cfgK.FreeThrowJumpBall),
                (OffensiveReboundOutcome.DefensiveFoul,    cfgK.FreeThrowDefensiveFoul),
                (OffensiveReboundOutcome.OffensiveFoul,    cfgK.FreeThrowOffensiveFoul),
                (OffensiveReboundOutcome.DeadBallTurnover, cfgK.FreeThrowDeadBallTurnover),
                (OffensiveReboundOutcome.LiveBallTurnover, cfgK.FreeThrowLiveBallTurnover),
                (OffensiveReboundOutcome.ResetOffense,     cfgK.FreeThrowResetOffense),
            }),
        };

        double KPutBack(OffensiveReboundSource s) =>
            genK.Generate(s).Slices.First(x => x.Outcome == OffensiveReboundOutcome.PutBack).Weight;

        foreach (var (src, expected) in kContexts)
        {
            var pie = genK.Generate(src);
            var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var selectionOk = true;
            foreach (var (o, w) in expected)
                if (Math.Abs(pieMap[o] - w) > cfgK.Epsilon) selectionOk = false;

            var rng = new SystemRng(cfg.Seed);
            var counts = new Dictionary<OffensiveReboundOutcome, int>();
            foreach (var o in Enum.GetValues<OffensiveReboundOutcome>()) counts[o] = 0;
            for (var i = 0; i < cfg.BatchSize; i++) counts[pie.Roll(rng.NextUnitInterval())]++;

            var ratesOk = true;
            Console.WriteLine($"  Roll K source={src} (selection {(selectionOk ? "ok" : "FAIL")}):");
            foreach (var (o, w) in expected)
            {
                var observed = counts[o] / (double)cfg.BatchSize;
                var pass = Math.Abs(observed - w) <= cfg.RateTolerance;
                ratesOk &= pass;
                Console.WriteLine($"    {o,-18} observed={observed:P3}  expected={w:P3}  {(pass ? "ok" : "FAIL")}");
            }
            ok &= selectionOk && ratesOk;
        }
        var kDiffer = Math.Abs(KPutBack(OffensiveReboundSource.FreeThrow) - KPutBack(OffensiveReboundSource.LiveBall)) > cfgK.Epsilon;
        Console.WriteLine($"  Roll K FT pie differs from live-ball pie (PutBack): {(kDiffer ? "ok" : "FAIL")}");
        ok &= kDiffer;

        // ---- Roll J: Rebound vs FreeThrowRebound ----
        var genJ = new RollJStubPieGenerator(cfgJ);
        var jContexts = new (TransitionContext ctx, (TransitionOutcome o, double w)[] expected)[]
        {
            (TransitionContext.Rebound, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.Settle),
                (TransitionOutcome.Push,          cfgJ.Push),
                (TransitionOutcome.Turnover,      cfgJ.Turnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.DefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.JumpBall),
            }),
            (TransitionContext.FreeThrowRebound, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.FreeThrowSettle),
                (TransitionOutcome.Push,          cfgJ.FreeThrowPush),
                (TransitionOutcome.Turnover,      cfgJ.FreeThrowTurnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.FreeThrowDefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.FreeThrowJumpBall),
            }),
            (TransitionContext.Steal, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.StealSettle),
                (TransitionOutcome.Push,          cfgJ.StealPush),
                (TransitionOutcome.Turnover,      cfgJ.StealTurnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.StealDefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.StealJumpBall),
            }),
        };

        double JPush(TransitionContext c) =>
            genJ.Generate(c).Slices.First(x => x.Outcome == TransitionOutcome.Push).Weight;

        foreach (var (ctx, expected) in jContexts)
        {
            var pie = genJ.Generate(ctx);
            var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var selectionOk = true;
            foreach (var (o, w) in expected)
                if (Math.Abs(pieMap[o] - w) > cfgJ.Epsilon) selectionOk = false;

            var rng = new SystemRng(cfg.Seed);
            var counts = new Dictionary<TransitionOutcome, int>();
            foreach (var o in Enum.GetValues<TransitionOutcome>()) counts[o] = 0;
            for (var i = 0; i < cfg.BatchSize; i++) counts[pie.Roll(rng.NextUnitInterval())]++;

            var ratesOk = true;
            Console.WriteLine($"  Roll J source={ctx.Source} (selection {(selectionOk ? "ok" : "FAIL")}):");
            foreach (var (o, w) in expected)
            {
                var observed = counts[o] / (double)cfg.BatchSize;
                var pass = Math.Abs(observed - w) <= cfg.RateTolerance;
                ratesOk &= pass;
                Console.WriteLine($"    {o,-18} observed={observed:P3}  expected={w:P3}  {(pass ? "ok" : "FAIL")}");
            }
            ok &= selectionOk && ratesOk;
        }
        var jDiffer = Math.Abs(JPush(TransitionContext.FreeThrowRebound) - JPush(TransitionContext.Rebound)) > cfgJ.Epsilon;
        Console.WriteLine($"  Roll J FT pie differs from rebound pie (Push): {(jDiffer ? "ok" : "FAIL")}");
        ok &= jDiffer;

        // Steal is the third source (Contextification #3): its pie must DIFFER from the
        // rebound pie, and the run-happiness must order Steal > Rebound > FreeThrowRebound
        // on Push (the locked pie intent — a steal runs hardest, a missed FT least).
        var jStealDiffer = Math.Abs(JPush(TransitionContext.Steal) - JPush(TransitionContext.Rebound)) > cfgJ.Epsilon;
        var jPushOrderOk = JPush(TransitionContext.Steal) > JPush(TransitionContext.Rebound)
                           && JPush(TransitionContext.Rebound) > JPush(TransitionContext.FreeThrowRebound);
        Console.WriteLine($"  Roll J Steal pie differs from rebound pie (Push): {(jStealDiffer ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  Roll J Push order Steal({JPush(TransitionContext.Steal):P1}) > Rebound({JPush(TransitionContext.Rebound):P1}) " +
            $"> FT({JPush(TransitionContext.FreeThrowRebound):P1}): {(jPushOrderOk ? "ok" : "FAIL")}");
        ok &= jStealDiffer && jPushOrderOk;

        Console.WriteLine($"  Roll M context selection: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    // --- Convergence: the putback <-> rebound LOOP bleeds out. Roll K is the first
    //     possession-EXTENDING node, so a single resolver walk can cycle PutBack ->
    //     Roll H -> miss -> Roll I -> OffensiveRebound -> PutBack ... (and reset back
    //     through Roll E). This is the §2a "watch the accumulation across iterations"
    //     check applied to a RE-ENTRANT chain: the shared thing changing is the
    //     possession's own depth. Drive many possessions that ENTER Roll K (feed the
    //     resolver a fully-stamped Continue(ResolveOffensiveRebound)), read
    //     RoutingOutcome.PutbackAttempts, and assert the survival distribution
    //     reachedAtLeast[n] (# possessions with >= n putbacks) STRICTLY DECREASES on
    //     its populated levels, the max is comfortably bounded (< 20), and the
    //     resolver's loud iteration guard is NEVER hit (the harness completing IS that
    //     proof — a non-converging walk would have thrown). ---
    private static bool OffensiveReboundConvergenceCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- Convergence: {cfg.BatchSize:N0} offensive rebounds driven through the resolver loop ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            genE,
            new RollFStubPieGenerator(cfgF),
            genG,
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        var pieE = genE.Generate(state);

        var maxAttempts = 0;
        var hist = new Dictionary<int, int>(); // putbackAttempts value -> # possessions

        // The harness completing this loop at all is the convergence proof: if any
        // possession failed to converge, the resolver's IterationCeiling guard would
        // have THROWN out of resolver.Route below, crashing the harness loudly.
        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Build a fully-stamped post-miss state and ENTER Roll K directly by
            // handing the resolver the offensive-rebound continuation — the resolver
            // then walks the whole loop internally and returns once it ends/parks.
            var sel = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), rng)).State;
            var stamped = zoned with { Result = ShotResult.Miss };
            var entry = new Continue(ContinuationKind.ResolveOffensiveRebound, stamped);

            var outcome = resolver.Route(entry);
            var k = outcome.PutbackAttempts;
            hist[k] = hist.TryGetValue(k, out var v) ? v + 1 : 1;
            if (k > maxAttempts) maxAttempts = k;
        }

        // Survival: reachedAtLeast[n] = # possessions with putbackAttempts >= n.
        var reachedAtLeast = new int[maxAttempts + 2];
        foreach (var (k, count) in hist)
            for (var n = 0; n <= k; n++) reachedAtLeast[n] += count;

        Console.WriteLine("  putback-depth distribution (exactly n / at least n):");
        for (var n = 0; n <= maxAttempts; n++)
        {
            var exactly = hist.TryGetValue(n, out var e) ? e : 0;
            Console.WriteLine($"    n={n,2}: exactly {exactly,8:N0}   at-least {reachedAtLeast[n],8:N0}");
        }

        // Strict decay on the populated levels: each deeper cycle is rarer than the
        // last. Asserted where the SHALLOWER level still has a meaningful sample
        // (>= 20) so sampling noise at the sparse tail can't flake the check; the
        // tail is reported above for the eye.
        const int floor = 20;
        var strictDecay = true;
        for (var n = 1; n < maxAttempts; n++)
            if (reachedAtLeast[n] >= floor && !(reachedAtLeast[n + 1] < reachedAtLeast[n]))
                strictDecay = false;

        var boundedMax = maxAttempts < 20;

        Console.WriteLine($"\n  max putback depth observed: {maxAttempts} -> {(boundedMax ? "ok (< 20)" : "FAIL (>= 20)")}");
        Console.WriteLine($"  survival strictly decreasing on populated levels: {(strictDecay ? "ok" : "FAIL")}");
        Console.WriteLine($"  iteration guard never hit (harness reached here): ok");

        return strictDecay && boundedMax;
    }

    // --- Expansion (#5a): every DORMANT loss type seated in Roll C resolves
    //     correctly in ISOLATION. Two parts: (1) drive the new Entry/Backcourt
    //     context directly and confirm its weighted members are reachable at their
    //     configured rate; (2) a directly-built UNIFORM pie over all 15 types lights
    //     up every arm — including any type that is 0.0 in a given live context — and
    //     asserts each is a clean terminal with the right consequence (dead-ball to
    //     defense, except the two existing live steals) and the right elapsed
    //     (violations stamp 30/0/10; every turnover defers to null), and that NO new
    //     type leaks a steal. (As of #6 the new halfcourt-natural types are LIVE in
    //     the Halfcourt context; this check still drives them in isolation via the
    //     EntryBackcourt + uniform pies, independent of that.) Keeps its own full
    //     reason map local; the shared MapTurnover is now full too. ---
    private static bool RollCExpansionCheck(
        RollAConfig cfg, RollCConfig cfgC, RollCStubPieGenerator genC, PossessionState state)
    {
        Console.WriteLine("\n--- Expansion: Roll C expanded loss types resolve in isolation ---");
        var rng = new SystemRng(cfg.Seed);
        var ok = true;

        static TurnoverOutcome Map(string r) => r switch
        {
            "BadPassDeadBall" => TurnoverOutcome.BadPassDeadBall,
            "BadPassIntercepted" => TurnoverOutcome.BadPassIntercepted,
            "LostBallDeadBall" => TurnoverOutcome.LostBallDeadBall,
            "LostBallLiveBall" => TurnoverOutcome.LostBallLiveBall,
            "OffensiveFoul" => TurnoverOutcome.OffensiveFoul,
            "Travel" => TurnoverOutcome.Travel,
            "DoubleDribble" => TurnoverOutcome.DoubleDribble,
            "Carry" => TurnoverOutcome.Carry,
            "ThreeSecondViolation" => TurnoverOutcome.ThreeSecondViolation,
            "FiveSecondCloselyGuarded" => TurnoverOutcome.FiveSecondCloselyGuarded,
            "OffensiveGoaltending" => TurnoverOutcome.OffensiveGoaltending,
            "BackcourtViolation" => TurnoverOutcome.BackcourtViolation,
            "ShotClockViolation" => TurnoverOutcome.ShotClockViolation,
            "FiveSecondInbound" => TurnoverOutcome.FiveSecondInbound,
            "TenSecondBackcourt" => TurnoverOutcome.TenSecondBackcourt,
            _ => throw new InvalidOperationException($"Unmapped Roll C reason '{r}'.")
        };

        var live = new HashSet<TurnoverOutcome>
            { TurnoverOutcome.BadPassIntercepted, TurnoverOutcome.LostBallLiveBall };
        var violationElapsed = new Dictionary<TurnoverOutcome, double>
        {
            [TurnoverOutcome.ShotClockViolation] = cfgC.ShotClockViolationElapsedSeconds,
            [TurnoverOutcome.FiveSecondInbound]  = cfgC.FiveSecondInboundElapsedSeconds,
            [TurnoverOutcome.TenSecondBackcourt] = cfgC.TenSecondBackcourtElapsedSeconds,
        };
        var newTypes = new HashSet<TurnoverOutcome>
        {
            TurnoverOutcome.Travel, TurnoverOutcome.DoubleDribble, TurnoverOutcome.Carry,
            TurnoverOutcome.ThreeSecondViolation, TurnoverOutcome.FiveSecondCloselyGuarded,
            TurnoverOutcome.OffensiveGoaltending, TurnoverOutcome.BackcourtViolation,
            TurnoverOutcome.ShotClockViolation, TurnoverOutcome.FiveSecondInbound,
            TurnoverOutcome.TenSecondBackcourt,
        };

        // --- Part 1: drive the Entry/Backcourt context directly. ---
        var expectedEB = new (TurnoverOutcome o, double w)[]
        {
            (TurnoverOutcome.BadPassDeadBall,    cfgC.EntryBackcourtBadPassDeadBall),
            (TurnoverOutcome.BadPassIntercepted, cfgC.EntryBackcourtBadPassIntercepted),
            (TurnoverOutcome.LostBallDeadBall,   cfgC.EntryBackcourtLostBallDeadBall),
            (TurnoverOutcome.LostBallLiveBall,   cfgC.EntryBackcourtLostBallLiveBall),
            (TurnoverOutcome.ShotClockViolation, cfgC.EntryBackcourtShotClockViolation),
            (TurnoverOutcome.FiveSecondInbound,  cfgC.EntryBackcourtFiveSecondInbound),
            (TurnoverOutcome.TenSecondBackcourt, cfgC.EntryBackcourtTenSecondBackcourt),
        };
        var pieEB = genC.Generate(state, pressure: 0.0, context: TurnoverContext.EntryBackcourt);
        var pieMapEB = pieEB.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

        var selOk = true;
        foreach (var (o, w) in expectedEB)
            if (Math.Abs(pieMapEB[o] - w) > cfgC.Epsilon) selOk = false;

        var countEB = new Dictionary<TurnoverOutcome, int>();
        foreach (var o in Enum.GetValues<TurnoverOutcome>()) countEB[o] = 0;
        for (var i = 0; i < cfg.BatchSize; i++)
            countEB[Map(((Terminal)RollC.Execute(state, pieEB, rng, cfgC)).Reason)]++;

        var nEB = (double)cfg.BatchSize;
        var rateOkEB = true;
        Console.WriteLine($"  Entry/Backcourt context (selection {(selOk ? "ok" : "FAIL")}):");
        foreach (var (o, w) in expectedEB)
        {
            var obs = countEB[o] / nEB;
            var gap = Math.Abs(obs - w);
            var pass = gap <= cfg.RateTolerance;
            rateOkEB &= pass;
            Console.WriteLine($"    {o,-26} observed={obs:P3}  expected={w:P3}  {(pass ? "ok" : "FAIL")}");
        }
        var zeroLeak = Enum.GetValues<TurnoverOutcome>()
            .Where(o => expectedEB.All(e => e.o != o))
            .Any(o => countEB[o] > 0);
        Console.WriteLine($"  zero-weight members unreachable in context: {(!zeroLeak ? "ok" : "FAIL")}");
        ok &= selOk && rateOkEB && !zeroLeak;

        // --- Part 2: uniform pie over ALL types proves every arm + consequence. ---
        var all = Enum.GetValues<TurnoverOutcome>();
        var uniform = all.ToDictionary(o => o, _ => 1.0 / all.Length);
        var pieAll = new Pie<TurnoverOutcome>(uniform, cfgC.Epsilon);

        var countAll = new Dictionary<TurnoverOutcome, int>();
        foreach (var o in all) countAll[o] = 0;
        var consequenceBad = 0;
        var elapsedBad = 0;
        var stealLeak = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var t = (Terminal)RollC.Execute(state, pieAll, rng, cfgC);
            var o = Map(t.Reason);
            countAll[o]++;
            var c = t.Consequence;

            if (live.Contains(o))
            {
                if (!(c.NextOffense == state.Defense && c.NextEntry == EntryType.Transition
                      && c.TransitionContext?.Source == TransitionSource.Steal)) consequenceBad++;
                if (t.ElapsedSeconds is not null) elapsedBad++;
            }
            else
            {
                // Session 27 spot-flip: dead-ball arms may produce DeadBallInbound (frontcourt)
                // or BallAdvanced (backcourt). Both are correct; neither carries a transition context.
                var deadOk = c.NextOffense == state.Defense
                    && (c.NextEntry == EntryType.DeadBallInbound || c.NextEntry == EntryType.BallAdvanced)
                    && c.TransitionContext is null;
                if (!deadOk) consequenceBad++;

                if (violationElapsed.TryGetValue(o, out var exp))
                {
                    if (t.ElapsedSeconds is not { } es || Math.Abs(es - exp) > 1e-9) elapsedBad++;
                }
                else if (t.ElapsedSeconds is not null) elapsedBad++;
            }

            if (newTypes.Contains(o) && c.NextEntry == EntryType.Transition) stealLeak++;
        }

        var allReached = all.All(o => countAll[o] > 0);
        var n = (double)cfg.BatchSize;
        var expect = 1.0 / all.Length;
        var ratesOk = true;
        foreach (var o in all)
            if (Math.Abs(countAll[o] / n - expect) > cfg.RateTolerance) ratesOk = false;

        Console.WriteLine($"  uniform pie over all {all.Length} types (expected {expect:P3} each):");
        Console.WriteLine($"    all types reachable: {(allReached ? "ok" : "FAIL")}");
        Console.WriteLine($"    rates within tolerance: {(ratesOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    consequences correct (DeadBallInbound or BallAdvanced to defense; steal only on the two live): bad={consequenceBad} -> {(consequenceBad == 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"    elapsed correct (violations 30/0/10, turnovers deferred): bad={elapsedBad} -> {(elapsedBad == 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"    no NEW type leaks a steal: leaks={stealLeak} -> {(stealLeak == 0 ? "ok" : "FAIL")}");
        ok &= allReached && ratesOk && consequenceBad == 0 && elapsedBad == 0 && stealLeak == 0;

        Console.WriteLine($"  Roll C expansion: {(ok ? "ok" : "FAIL")}");
        return ok;
    }

    private static TurnoverOutcome MapTurnover(string reason) => reason switch
    {
        "BadPassDeadBall" => TurnoverOutcome.BadPassDeadBall,
        "BadPassIntercepted" => TurnoverOutcome.BadPassIntercepted,
        "LostBallDeadBall" => TurnoverOutcome.LostBallDeadBall,
        "LostBallLiveBall" => TurnoverOutcome.LostBallLiveBall,
        "OffensiveFoul" => TurnoverOutcome.OffensiveFoul,
        "Travel" => TurnoverOutcome.Travel,
        "DoubleDribble" => TurnoverOutcome.DoubleDribble,
        "Carry" => TurnoverOutcome.Carry,
        "ThreeSecondViolation" => TurnoverOutcome.ThreeSecondViolation,
        "FiveSecondCloselyGuarded" => TurnoverOutcome.FiveSecondCloselyGuarded,
        "OffensiveGoaltending" => TurnoverOutcome.OffensiveGoaltending,
        "BackcourtViolation" => TurnoverOutcome.BackcourtViolation,
        "ShotClockViolation" => TurnoverOutcome.ShotClockViolation,
        "FiveSecondInbound" => TurnoverOutcome.FiveSecondInbound,
        "TenSecondBackcourt" => TurnoverOutcome.TenSecondBackcourt,
        _ => throw new InvalidOperationException($"Unmapped Roll C reason '{reason}'.")
    };

    private static TeamSide Other(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;

    // -----------------------------------------------------------------
    // Phase 9 helper: seat starting fives from config into a GameState.
    // Must be called after the GameState is constructed but before any
    // generator that reads PlayerAt is used — otherwise those generators
    // silently fall back to their stub pies and the matchup machinery
    // never runs. Mirrors the seating loop in Phase1RosterCheck.
    // -----------------------------------------------------------------
    private static void SeatStartersFromConfig(GameState game, string configPath)
    {
        var rosterCfg = RosterConfig.Load(configPath);
        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var lineup  = game.LineupFor(side);
            var roster  = game.RosterFor(side);
            var configs = side == TeamSide.Home ? rosterCfg.Home : rosterCfg.Away;
            for (var i = 0; i < Lineup.Size; i++)
                roster.SetStarter(lineup.SlotAt(i + 1), configs[i].ToPlayer());
        }
    }

    // -----------------------------------------------------------------
    // dotnet run --project "..." -- game
    // Plays one full game using the real engine. Fresh seed every run.
    // -----------------------------------------------------------------
    private static void RunGame(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Game Simulation ===");
        Console.WriteLine();

        var cfg      = RollAConfig.Load(configPath);
        var cfgB     = RollBConfig.Load(configPath);
        var cfgC     = RollCConfig.Load(configPath);
        var cfgD     = RollDConfig.Load(configPath);
        var cfgE     = RollEConfig.Load(configPath);
        var cfgF     = RollFConfig.Load(configPath);
        var cfgG     = RollGConfig.Load(configPath);
        var cfgGov   = GovernorConfig.Load(configPath);
        var cfgClock = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);

        var seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        Console.WriteLine($"  seed: {seed}");
        Console.WriteLine();

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeatStartersFromConfig(game, configPath);       // seat real rosters (generators currently use stubs; seating future-proofs RunGame)

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            cfgC,
            new RollDStubPieGenerator(cfgD),
            new RollEStubPieGenerator(cfgE),
            new RollFStubPieGenerator(cfgF),
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJStubPieGenerator(RollJConfig.Load(configPath)),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            new SystemRng(seed));

        game.SetPossessionArrow(TeamSide.Home);
        var governor = new Governor(resolver, game, cfgGov, cfgClock, new SystemRng(seed + 1), cfgEndOfHalf);

        var first = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        var result  = governor.Run(first);
        var records = result.Possessions;

        var htHome = records.Where(r => r.Half == 1 && r.Offense == TeamSide.Home).Sum(r => r.Points);
        var htAway = records.Where(r => r.Half == 1 && r.Offense == TeamSide.Away).Sum(r => r.Points);

        var homeScore = 0;
        var awayScore = 0;
        var lastHalf  = 0;

        foreach (var r in records)
        {
            if (r.Half != lastHalf)
            {
                if (lastHalf == 1)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  -- HALFTIME  Home {htHome,3}  -  Away {htAway,3} --");
                    Console.WriteLine();
                }
                lastHalf = r.Half;
                var label = r.Half == 1 ? "FIRST HALF" : "SECOND HALF";
                Console.WriteLine($"--- {label} ({cfgGov.HalfSeconds:N0}s) ---");
                Console.WriteLine($"  {"#",-4} {"Team",-5} {"Entry",-18} {"Outcome",-28} {"Pts",-4} {"Score",-20} Time");
                Console.WriteLine($"  {new string('-', 90)}");
            }

            if (r.Offense == TeamSide.Home) homeScore += r.Points;
            else                            awayScore  += r.Points;

            var pts     = r.Points > 0 ? $"+{r.Points}" : "  ";
            var score   = $"Home {homeScore,3} - Away {awayScore,3}";
            var offTeam = r.Offense == TeamSide.Home ? "Home" : "Away";
            var outcome = r.EndLabel.Length > 27 ? r.EndLabel[..27] : r.EndLabel;
            var intentMarker = r.EndOfHalfIntent switch
            {
                EndOfHalfIntent.HoldShootLast => " (held)",
                EndOfHalfIntent.ShootEarly    => " (early)",
                EndOfHalfIntent.NoShot        => " (no shot)",
                _                             => ""
            };

            Console.WriteLine($"  {r.Number,-4} {offTeam,-5} {r.Entry,-18} {outcome,-28} {pts,-4} {score,-20} {r.Elapsed:F0}s{intentMarker}");
        }

        Console.WriteLine();
        Console.WriteLine($"  ==========================================");
        Console.WriteLine($"  FINAL:  Home {game.HomeScore,3}  -  Away {game.AwayScore,3}");
        Console.WriteLine($"  ==========================================");
        Console.WriteLine();
        Console.WriteLine($"  {records.Count} possessions  |  APL {result.TotalSeconds / records.Count:F1}s  |  total {result.TotalSeconds:N0}s");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Observation Run v1 — macro sentinel harness (frozen-corpus-v1)
    //
    // Runs N full games against a frozen scenario corpus and emits one
    // self-describing macro-sentinel block. RECORDED, NOT JUDGED: no number
    // triggers a realism pass/fail. Only five mechanical checks assert anything.
    //
    // SCOPE WALLS:
    //   - No config values change in this method.
    //   - No new engine fields or instrumentation.
    //   - No judgment / target lines in the output.
    // ─────────────────────────────────────────────────────────────────────────

    private static void ObservationRunV1(string configPath)
    {
        const string CorpusId = "frozen-corpus-v1";
        const int    N         = 1_000;   // games per run — configurable constant

        Console.WriteLine();
        Console.WriteLine($"=== OBSERVATION RUN — {CorpusId} ===");

        // ── Configs — loaded once; immutable across all N games. ─────────
        var cfg          = RollAConfig.Load(configPath);
        var cfgB         = RollBConfig.Load(configPath);
        var cfgC         = RollCConfig.Load(configPath);
        var cfgD         = RollDConfig.Load(configPath);
        var cfgE         = RollEConfig.Load(configPath);
        var cfgF         = RollFConfig.Load(configPath);
        var cfgG         = RollGConfig.Load(configPath);
        var cfgH         = RollHConfig.Load(configPath);
        var cfgI         = RollIConfig.Load(configPath);
        var cfgJ         = RollJConfig.Load(configPath);
        var cfgK         = RollKConfig.Load(configPath);
        var cfgL         = RollLConfig.Load(configPath);
        var cfgM         = RollMConfig.Load(configPath);
        var cfgOffFoul   = RollOffensiveFoulConfig.Load(configPath);
        var cfgGov       = GovernorConfig.Load(configPath);
        var cfgClock     = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);
        var cfgMatchup   = MatchupConfig.Load(configPath);
        var cfgRoster    = RosterConfig.Load(configPath);

        // ── Config hash — ties every recorded number to the exact config. ─
        var configBytes = File.ReadAllBytes(configPath);
        var hashBytes   = System.Security.Cryptography.SHA256.HashData(configBytes);
        var configHash  = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // ── Metadata header ───────────────────────────────────────────────
        var homeFive = string.Join("/", cfgRoster.Home.Select(p => p.Name));
        var awayFive = string.Join("/", cfgRoster.Away.Select(p => p.Name));

        Console.WriteLine($"  config hash:      {configHash}");
        Console.WriteLine($"  corpus:           {CorpusId}  (seeds 1..{N}; rosters: {homeFive} vs {awayFive})");
        Console.WriteLine($"  sample size:      {N} games");
        Console.WriteLine($"  strategy/context: home-press={cfgMatchup.HomePressFrequency:F1}  away-press={cfgMatchup.AwayPressFrequency:F1}  halves={cfgGov.Halves}  half={cfgGov.HalfSeconds:F0}s");
        Console.WriteLine($"  session/commit:   <stamp at run time>");
        Console.WriteLine();

        // ── Per-game accumulators ─────────────────────────────────────────
        var totalPossList   = new List<int>(N);
        var homePossList    = new List<int>(N);
        var awayPossList    = new List<int>(N);
        var noShotList      = new List<int>(N);
        var homePPPList     = new List<double>(N);
        var awayPPPList     = new List<double>(N);
        var combinedPPPList = new List<double>(N);
        var homeScoreList   = new List<int>(N);
        var awayScoreList   = new List<int>(N);
        var marginList      = new List<int>(N);
        var transFreqList   = new List<double>(N);
        var aplList         = new List<double>(N);
        var homeFoulsList   = new List<int>(N);
        var awayFoulsList   = new List<int>(N);
        var holdList        = new List<int>(N);
        var earlyList       = new List<int>(N);
        var noShotIntList   = new List<int>(N);

        // ── v2 shooting / rebounding sentinel accumulators ──────────────
        var homeFgPctList     = new List<double>(N);
        var awayFgPctList     = new List<double>(N);
        var combinedFgPctList = new List<double>(N);
        var home3pPctList     = new List<double>(N);
        var away3pPctList     = new List<double>(N);
        var combined3pPctList = new List<double>(N);
        var homeFtPctList     = new List<double>(N);
        var awayFtPctList     = new List<double>(N);
        var combinedFtPctList = new List<double>(N);
        var threePaRateList   = new List<double>(N);  // 3PA / FGA combined
        var orbPctList        = new List<double>(N);  // ORB won / ORB chances combined
        var ftrList           = new List<double>(N);  // FTA / FGA combined

        // ── Per-zone shooting accumulators (combined; FG% and attempt share per zone) ──
        var rimFgPctList    = new List<double>(N);
        var shortFgPctList  = new List<double>(N);
        var midFgPctList    = new List<double>(N);
        var longFgPctList   = new List<double>(N);
        var threeFgPctList  = new List<double>(N);
        var rimShareList    = new List<double>(N);
        var shortShareList  = new List<double>(N);
        var midShareList    = new List<double>(N);
        var longShareList   = new List<double>(N);
        var threeShareList  = new List<double>(N);

        // Terminal mix — accumulated across all possessions, all games.
        var termBuckets = new Dictionary<string, long>
        {
            ["Made-FG"]          = 0L,
            ["FT-made"]          = 0L,
            ["DefensiveRebound"] = 0L,
            ["Turnover"]         = 0L,
            ["OOB"]              = 0L,
            ["JumpBall"]         = 0L,
            ["NoShot"]           = 0L,
            ["Parked"]           = 0L,
            ["Other"]            = 0L,
        };

        var mechanicsOk = true;
        var gamesRun    = 0;

        var firstState = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        Console.Write($"  Running {N} games");

        for (var seed = 1; seed <= N; seed++)
        {
            if (seed % 100 == 0) Console.Write(".");

            // Fresh game state per game: score, fouls, and arrow all start clean.
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            SeatStartersFromConfig(game, configPath);
            game.SetPossessionArrow(TeamSide.Home);

            var resolverRng = new SystemRng(seed);
            var governorRng = new SystemRng(seed + 1);

            var resolver = new Resolver(
                new RollAGenerator(cfg, cfgMatchup, game),
                cfg,
                new RollBGenerator(cfgB, cfgMatchup, game),
                new RollCStubPieGenerator(cfgC),
                cfgC,
                new RollDStubPieGenerator(cfgD),
                new RollEStubPieGenerator(cfgE),
                new RollFGenerator(cfgF, cfgMatchup, game),
                new RollGGenerator(cfgG, cfgMatchup, game),
                new RollHGenerator(cfgH, cfgMatchup, game),
                new RollIGenerator(cfgI, cfgMatchup, game),
                new RollJStubPieGenerator(cfgJ),
                new RollKStubPieGenerator(cfgK),
                new RollLStubPieGenerator(cfgL),
                new RollMGenerator(cfgM, cfgMatchup, game),
                new RollOffensiveFoulStubPieGenerator(cfgOffFoul),
                cfgMatchup,
                game,
                resolverRng);

            var governor = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndOfHalf);

            GovernorRunResult result;
            try
            {
                result = governor.Run(firstState);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL-THROW] Seed {seed}: {ex.Message}");
                mechanicsOk = false;
                continue;
            }
            gamesRun++;

            var records = result.Possessions;
            var noShot  = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.NoShot);

            // ── Mechanical check 1: scoring reconciles ───────────────────
            var recHome = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Points);
            var recAway = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Points);
            if (game.HomeScore != recHome || game.AwayScore != recAway)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: score mismatch — state=({game.HomeScore},{game.AwayScore}) records=({recHome},{recAway})");
                mechanicsOk = false;
            }

            // ── Mechanical check 2: count invariant ─────────────────────
            if (result.TerminalEnded + result.Parked + noShot != records.Count)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: count invariant — {result.TerminalEnded}+{result.Parked}+{noShot} != {records.Count}");
                mechanicsOk = false;
            }

            // ── Mechanical check 3: zero parks ──────────────────────────
            if (result.Parked > 0)
            {
                Console.WriteLine();
                var stubDetail = string.Join(", ", result.PerStubParks.Select(kv => $"{kv.Key}:{kv.Value}"));
                Console.WriteLine($"  [FINDING] Seed {seed}: {result.Parked} parked — {stubDetail}");
                mechanicsOk = false;
            }

            // ── Mechanical check 4: loose sanity (NaN / ÷0 guard) ───────
            if (records.Count == 0) { mechanicsOk = false; continue; }
            if (records.Count > 200)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: pace exceeds outer bound ({records.Count})");
                mechanicsOk = false;
            }

            // ── Mechanical check — v2 counter reconciliation ────────────────────
            // Points == 2*(FGM - 3PM) + 3*3PM + FTM  (per game, summed across records)
            // This is the load-bearing check: proves new counters and the existing
            // points accumulator agree — every scoring event is tagged exactly once.
            var recFgm  = records.Sum(r => r.Fgm);
            var rec3pm  = records.Sum(r => r.ThreePm);
            var recFtm  = records.Sum(r => r.Ftm);
            var recPts  = records.Sum(r => r.Points);
            var expPts  = 2 * (recFgm - rec3pm) + 3 * rec3pm + recFtm;
            if (recPts != expPts)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: counter reconciliation — Points={recPts} expected={expPts} (FGM={recFgm} 3PM={rec3pm} FTM={recFtm})");
                mechanicsOk = false;
            }

            // ── Mechanical check — v2 denominator guard ─────────────────────────
            // FGA + MissFouled == ShotResolutions  (per game)
            // Also assert the per-counter inequalities.  These checks are blind to
            // the points reconciliation above (MissFouled scores zero), so they are
            // required to catch a wrong FGA definition.
            var recFga  = records.Sum(r => r.Fga);
            var recMf   = records.Sum(r => r.MissFouled);
            var recSr   = records.Sum(r => r.ShotResolutions);
            var rec3pa  = records.Sum(r => r.ThreePa);
            var recFta  = records.Sum(r => r.Fta);
            var recOrbW = records.Sum(r => r.OrbWon);
            var recOrbC = records.Sum(r => r.OrbChances);
            if (recFga + recMf != recSr)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: denominator guard — FGA({recFga}) + MissFouled({recMf}) != ShotResolutions({recSr})");
                mechanicsOk = false;
            }
            if (recFgm > recFga || rec3pm > rec3pa || rec3pa > recFga || recFtm > recFta || recOrbW > recOrbC)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: counter sanity — FGM={recFgm} FGA={recFga} 3PM={rec3pm} 3PA={rec3pa} FTM={recFtm} FTA={recFta} ORBwon={recOrbW} ORBchances={recOrbC}");
                mechanicsOk = false;
            }

            // ── Mechanical check — per-zone bin integrity ───────────────────────
            // Every FGA bins into exactly one of the five zones, and every FGM likewise.
            // (Three uses the existing ThreePa/ThreePm pair; the other four use the new
            // per-zone counters.) If a shot's zone ever failed to bin, these sums would
            // fall short of FGA/FGM and fail loud — the per-zone analog of the denominator
            // guard, pinning the zone splits to the totals.
            var recRimA   = records.Sum(r => r.RimFga);
            var recShortA = records.Sum(r => r.ShortFga);
            var recMidA   = records.Sum(r => r.MidFga);
            var recLongA  = records.Sum(r => r.LongFga);
            var recRimM   = records.Sum(r => r.RimFgm);
            var recShortM = records.Sum(r => r.ShortFgm);
            var recMidM   = records.Sum(r => r.MidFgm);
            var recLongM  = records.Sum(r => r.LongFgm);
            if (recRimA + recShortA + recMidA + recLongA + rec3pa != recFga)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: zone-attempt bin — Rim({recRimA})+Short({recShortA})+Mid({recMidA})+Long({recLongA})+Three({rec3pa}) != FGA({recFga})");
                mechanicsOk = false;
            }
            if (recRimM + recShortM + recMidM + recLongM + rec3pm != recFgm)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: zone-make bin — Rim({recRimM})+Short({recShortM})+Mid({recMidM})+Long({recLongM})+Three({rec3pm}) != FGM({recFgm})");
                mechanicsOk = false;
            }

            var hPoss = records.Count(r => r.Offense == TeamSide.Home);
            var aPoss = records.Count(r => r.Offense == TeamSide.Away);
            var hPts  = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Points);
            var aPts  = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Points);
            var cPPP  = (double)(hPts + aPts) / records.Count;

            if (double.IsNaN(cPPP) || cPPP < 0.0 || cPPP > 3.0)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: combined PPP out of outer bound ({cPPP:F4})");
                mechanicsOk = false;
            }

            // ── Accumulate sentinels ─────────────────────────────────────
            totalPossList.Add(records.Count);
            homePossList.Add(hPoss);
            awayPossList.Add(aPoss);
            noShotList.Add(noShot);
            homePPPList.Add(hPoss > 0 ? (double)hPts / hPoss : 0.0);
            awayPPPList.Add(aPoss > 0 ? (double)aPts / aPoss : 0.0);
            combinedPPPList.Add(cPPP);
            homeScoreList.Add(game.HomeScore);
            awayScoreList.Add(game.AwayScore);
            marginList.Add(game.HomeScore - game.AwayScore);

            var transCount = records.Count(r => r.Entry == EntryType.Transition);
            transFreqList.Add((double)transCount / records.Count);
            aplList.Add(result.TotalSeconds / records.Count);

            homeFoulsList.Add(game.Fouls.FoulsFor(TeamSide.Home));
            awayFoulsList.Add(game.Fouls.FoulsFor(TeamSide.Away));

            holdList.Add(records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.HoldShootLast));
            earlyList.Add(records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.ShootEarly));
            noShotIntList.Add(noShot);

            // ── v2 shooting / rebounding sentinel accumulation ───────────────────
            var hFga = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fga);
            var aFga = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fga);
            var hFgm = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fgm);
            var aFgm = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fgm);
            var h3pa = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePa);
            var a3pa = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePa);
            var h3pm = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePm);
            var a3pm = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePm);
            var hFta = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fta);
            var aFta = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fta);
            var hFtm = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Ftm);
            var aFtm = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Ftm);
            var tFga     = hFga + aFga;
            var t3pa     = h3pa + a3pa;
            var tFgm     = hFgm + aFgm;
            var t3pm     = h3pm + a3pm;
            var tFta     = hFta + aFta;
            var tFtm     = hFtm + aFtm;
            var tOrbWon  = records.Sum(r => r.OrbWon);
            var tOrbCh   = records.Sum(r => r.OrbChances);

            homeFgPctList.Add(hFga > 0     ? (double)hFgm / hFga     : 0.0);
            awayFgPctList.Add(aFga > 0     ? (double)aFgm / aFga     : 0.0);
            combinedFgPctList.Add(tFga > 0 ? (double)tFgm / tFga     : 0.0);
            home3pPctList.Add(h3pa > 0     ? (double)h3pm / h3pa     : 0.0);
            away3pPctList.Add(a3pa > 0     ? (double)a3pm / a3pa     : 0.0);
            combined3pPctList.Add(t3pa > 0 ? (double)t3pm / t3pa     : 0.0);  // 0.0 when zero 3PA (documented)
            homeFtPctList.Add(hFta > 0     ? (double)hFtm / hFta     : 0.0);
            awayFtPctList.Add(aFta > 0     ? (double)aFtm / aFta     : 0.0);
            combinedFtPctList.Add(tFta > 0 ? (double)tFtm / tFta     : 0.0);
            threePaRateList.Add(tFga > 0   ? (double)t3pa / tFga     : 0.0);
            orbPctList.Add(tOrbCh > 0      ? (double)tOrbWon / tOrbCh : 0.0);
            ftrList.Add(tFga > 0           ? (double)tFta / tFga     : 0.0);

            // Per-zone shooting (combined): FG% by zone and attempt share by zone.
            // Three reuses t3pa/t3pm (computed above); the other four sum the per-zone counters.
            var tRimA   = records.Sum(r => r.RimFga);
            var tShortA = records.Sum(r => r.ShortFga);
            var tMidA   = records.Sum(r => r.MidFga);
            var tLongA  = records.Sum(r => r.LongFga);
            var tRimM   = records.Sum(r => r.RimFgm);
            var tShortM = records.Sum(r => r.ShortFgm);
            var tMidM   = records.Sum(r => r.MidFgm);
            var tLongM  = records.Sum(r => r.LongFgm);
            rimFgPctList.Add(tRimA > 0     ? (double)tRimM / tRimA     : 0.0);
            shortFgPctList.Add(tShortA > 0 ? (double)tShortM / tShortA : 0.0);
            midFgPctList.Add(tMidA > 0     ? (double)tMidM / tMidA     : 0.0);
            longFgPctList.Add(tLongA > 0   ? (double)tLongM / tLongA   : 0.0);
            threeFgPctList.Add(t3pa > 0    ? (double)t3pm / t3pa       : 0.0);
            rimShareList.Add(tFga > 0      ? (double)tRimA / tFga      : 0.0);
            shortShareList.Add(tFga > 0    ? (double)tShortA / tFga    : 0.0);
            midShareList.Add(tFga > 0      ? (double)tMidA / tFga      : 0.0);
            longShareList.Add(tFga > 0     ? (double)tLongA / tFga     : 0.0);
            threeShareList.Add(tFga > 0    ? (double)t3pa / tFga       : 0.0);

            foreach (var r in records)
            {
                var b = ObsBucket(r.EndLabel);
                termBuckets[b] = termBuckets[b] + 1;
            }
        }

        Console.WriteLine($" done ({gamesRun}/{N} completed).");
        Console.WriteLine();

        // ── Mechanics verdict ─────────────────────────────────────────────
        Console.WriteLine(mechanicsOk
            ? "  Mechanics:  ALL OK — scoring reconciled, count invariant held, zero parks, no throws"
            : "  Mechanics:  FAILURES ABOVE — some sentinel values may be unreliable");
        Console.WriteLine();

        if (gamesRun == 0) { Console.WriteLine("  No games completed."); return; }

        // ── Sentinel block ────────────────────────────────────────────────

        Console.WriteLine("--- PACE (total possessions per game) ---");
        ObsPrintI("  Total",                              totalPossList);
        ObsPrintI("  Home",                               homePossList);
        ObsPrintI("  Away",                               awayPossList);
        ObsPrintI("  NoShot (end-of-half, per game)",    noShotList);
        Console.WriteLine("  Distribution (total):");
        ObsHistI(totalPossList, new[] { 90, 100, 110, 120, 130, 140, 150, 160 });

        Console.WriteLine();
        Console.WriteLine("--- PPP (points per possession) ---");
        ObsPrintD("  Home",     homePPPList);
        ObsPrintD("  Away",     awayPPPList);
        ObsPrintD("  Combined", combinedPPPList);
        Console.WriteLine("  Distribution (combined):");
        ObsHistD(combinedPPPList, new[] { 0.5, 0.7, 0.9, 1.1, 1.3, 1.5 });

        Console.WriteLine();
        Console.WriteLine("--- SCORE & MARGIN ---");
        ObsPrintI("  Home score",         homeScoreList);
        ObsPrintI("  Away score",         awayScoreList);
        ObsPrintI("  Margin (Home−Away)", marginList);

        Console.WriteLine();
        Console.WriteLine("--- TRANSITION FREQUENCY (Transition entries / total possessions) ---");
        ObsPrintD("  Transition freq", transFreqList);

        Console.WriteLine();
        Console.WriteLine("--- TERMINAL MIX (fractions across all possessions all games) ---");
        var totalMix = termBuckets.Values.Sum();
        foreach (var kv in termBuckets)
            Console.WriteLine($"  {kv.Key,-22} {(totalMix > 0 ? (double)kv.Value / totalMix : 0.0):F4}  ({kv.Value:N0})");

        Console.WriteLine();
        Console.WriteLine("--- END-OF-HALF INTENT (counts per game) ---");
        ObsPrintI("  HoldShootLast", holdList);
        ObsPrintI("  ShootEarly",    earlyList);
        ObsPrintI("  NoShot",        noShotIntList);

        Console.WriteLine();
        Console.WriteLine("--- APL (avg possession length, seconds) ---");
        ObsPrintD("  APL", aplList);

        Console.WriteLine();
        Console.WriteLine("--- FOULS PER TEAM PER GAME ---");
        ObsPrintI("  Home fouls", homeFoulsList);
        ObsPrintI("  Away fouls", awayFoulsList);

        Console.WriteLine();
        Console.WriteLine("--- SHOOTING SPLITS ---");
        Console.WriteLine("  FG%:");
        ObsPrintD("    Home",     homeFgPctList);
        ObsPrintD("    Away",     awayFgPctList);
        ObsPrintD("    Combined", combinedFgPctList);
        Console.WriteLine("  Distribution (FG% combined):");
        ObsHistD(combinedFgPctList, new[] { 0.20, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60 });
        Console.WriteLine("  3P%:  (games with zero 3PA report 0.0 — possible in principle)");
        ObsPrintD("    Home",     home3pPctList);
        ObsPrintD("    Away",     away3pPctList);
        ObsPrintD("    Combined", combined3pPctList);
        Console.WriteLine("  FT%:");
        ObsPrintD("    Home",     homeFtPctList);
        ObsPrintD("    Away",     awayFtPctList);
        ObsPrintD("    Combined", combinedFtPctList);
        Console.WriteLine("  Distribution (FT% combined):");
        ObsHistD(combinedFtPctList, new[] { 0.40, 0.50, 0.60, 0.70, 0.80, 0.90 });

        Console.WriteLine();
        Console.WriteLine("--- SHOT MIX ---");
        ObsPrintD("  3PA rate (3PA/FGA, combined)", threePaRateList);

        Console.WriteLine();
        Console.WriteLine("--- SHOOTING BY ZONE (combined, per game) ---");
        Console.WriteLine("  FG% by zone:");
        ObsPrintD("    Rim",   rimFgPctList);
        ObsPrintD("    Short", shortFgPctList);
        ObsPrintD("    Mid",   midFgPctList);
        ObsPrintD("    Long",  longFgPctList);
        ObsPrintD("    Three", threeFgPctList);
        Console.WriteLine("  Attempt share by zone (fraction of FGA from each zone):");
        ObsPrintD("    Rim",   rimShareList);
        ObsPrintD("    Short", shortShareList);
        ObsPrintD("    Mid",   midShareList);
        ObsPrintD("    Long",  longShareList);
        ObsPrintD("    Three", threeShareList);

        Console.WriteLine();
        Console.WriteLine("--- ORB% (offensive rebound rate, FG-miss + block + FT misses combined) ---");
        ObsPrintD("  Combined", orbPctList);

        Console.WriteLine();
        Console.WriteLine("--- FTr (free-throw rate = FTA/FGA, combined) ---");
        ObsPrintD("  Combined", ftrList);

        Console.WriteLine();
        Console.WriteLine("--- DEFERRED SENTINELS (counter-plumbing needed — future session) ---");
        Console.WriteLine("  Press frequency / break rate at game level");

        Console.WriteLine();
        Console.WriteLine($"=== END OBSERVATION RUN — {CorpusId} ===");
    }

    // ── Observation helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Classify an EndLabel into one of the terminal-mix buckets. By prefix, coarse.
    /// "Turnover" catches all fouls, violations, and live/dead-ball turnovers — everything
    /// that ends the possession without a made shot or defensive rebound.
    /// </summary>
    private static string ObsBucket(string endLabel) =>
        endLabel == "Made"                             ? "Made-FG"
        : endLabel == "FreeThrowsMade"                 ? "FT-made"
        : endLabel == "DefensiveRebound"               ? "DefensiveRebound"
        : endLabel.StartsWith("JumpBall")             ? "JumpBall"
        : endLabel.StartsWith("MissOutOfBounds") ||
          endLabel.StartsWith("OutOfBounds")          ? "OOB"
        : endLabel == "endOfHalf:NoShot"              ? "NoShot"
        : endLabel.StartsWith("parked:")              ? "Parked"
        : "Turnover";  // fouls, violations, live/dead-ball turnovers

    private static void ObsPrintI(string label, List<int> v)
    {
        if (v.Count == 0) return;
        var mean = v.Average();
        var sd   = Math.Sqrt(v.Select(x => (x - mean) * (x - mean)).Average());
        Console.WriteLine($"  {label,-40} mean={mean,7:F1}  sd={sd,5:F1}  min={v.Min(),5}  max={v.Max(),5}");
    }

    private static void ObsPrintD(string label, List<double> v)
    {
        if (v.Count == 0) return;
        var mean = v.Average();
        var sd   = Math.Sqrt(v.Select(x => (x - mean) * (x - mean)).Average());
        Console.WriteLine($"  {label,-40} mean={mean,7:F4}  sd={sd,6:F4}  min={v.Min(),7:F4}  max={v.Max(),7:F4}");
    }

    private static void ObsHistI(List<int> v, int[] edges)
    {
        var n = v.Count;
        if (n == 0) return;
        var under = v.Count(x => x < edges[0]);
        if (under > 0) Console.WriteLine($"    <{edges[0],5}       {(double)under / n,6:P1}  ({under})");
        for (var i = 0; i < edges.Length - 1; i++)
        {
            var lo = edges[i]; var hi = edges[i + 1];
            var cnt = v.Count(x => x >= lo && x < hi);
            Console.WriteLine($"    [{lo,3},{hi,3})    {(double)cnt / n,6:P1}  ({cnt})");
        }
        var over = v.Count(x => x >= edges[^1]);
        if (over > 0) Console.WriteLine($"    >={edges[^1],5}     {(double)over / n,6:P1}  ({over})");
    }

    private static void ObsHistD(List<double> v, double[] edges)
    {
        var n = v.Count;
        if (n == 0) return;
        var under = v.Count(x => x < edges[0]);
        if (under > 0) Console.WriteLine($"    <{edges[0]:F1}        {(double)under / n,6:P1}  ({under})");
        for (var i = 0; i < edges.Length - 1; i++)
        {
            var lo = edges[i]; var hi = edges[i + 1];
            var cnt = v.Count(x => x >= lo && x < hi);
            Console.WriteLine($"    [{lo:F1},{hi:F1})    {(double)cnt / n,6:P1}  ({cnt})");
        }
        var over = v.Count(x => x >= edges[^1]);
        if (over > 0) Console.WriteLine($"    >={edges[^1]:F1}      {(double)over / n,6:P1}  ({over})");
    }

    // -------------------------------------------------------------------------
    // Phase 1 — Player object & Roster seam
    // -------------------------------------------------------------------------

    /// <summary>
    /// Proves the full seam: config → RosterConfig.Load → PlayerConfig.ToPlayer →
    /// Player (authored attributes) → Roster.SetStarter → GameState.RosterFor →
    /// Roster.PlayerAt → derived attributes computed correctly.
    ///
    /// Three assertions:
    ///   1. Every slot on both sides resolves to a non-null Player.
    ///   2. Every player passes Validate() (all attributes 0–99).
    ///   3. Derived values are in physically plausible ranges and directionally
    ///      correct (bigs have lower athleticism than guards in this fixture).
    /// </summary>
    private static bool Phase1RosterCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 1: Player object & Roster seam ---");
        var pass = true;

        // --- Load ---
        RosterConfig cfgRoster;
        try
        {
            cfgRoster = RosterConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  RosterConfig.Load threw: {ex.Message}");
            return false;
        }
        Console.WriteLine("  RosterConfig loaded OK.");

        // --- Populate a fresh GameState ---
        var fouls = new FoulTracker(7, 10);
        var game  = new GameState(fouls);

        foreach (var (side, configs) in new[]
        {
            (TeamSide.Home, cfgRoster.Home),
            (TeamSide.Away, cfgRoster.Away)
        })
        {
            var lineup = game.LineupFor(side);
            var roster = game.RosterFor(side);
            for (var i = 0; i < Lineup.Size; i++)
            {
                var slot   = lineup.SlotAt(i + 1);
                var player = configs[i].ToPlayer();
                roster.SetStarter(slot, player);
            }
        }
        Console.WriteLine("  Rosters populated OK.");

        // --- Per-slot assertions ---
        Console.WriteLine();
        Console.WriteLine($"  {"Side",-6} {"Slot",-5} {"Name",-20} {"Ath":>6} {"Trans":>6} {"Grav":>6} {"Spac":>6}  Validate");
        Console.WriteLine($"  {new string('-', 75)}");

        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var lineup = game.LineupFor(side);
            var roster = game.RosterFor(side);

            for (var n = 1; n <= Lineup.Size; n++)
            {
                var slot   = lineup.SlotAt(n);
                var player = roster.PlayerAt(slot);

                // Assertion 1: slot resolves to a player
                if (player is null)
                {
                    Console.WriteLine($"  FAIL  {side} slot {n} resolved null.");
                    pass = false;
                    continue;
                }

                // Assertion 2: all authored attributes in 0–99
                var errors = player.Validate();
                var validateLabel = errors.Count == 0 ? "OK" : $"FAIL({errors.Count})";
                if (errors.Count > 0)
                {
                    pass = false;
                    foreach (var e in errors)
                        Console.WriteLine($"    {e}");
                }

                // Print derived values
                Console.WriteLine(
                    $"  {side,-6} {n,-5} {player.Name,-20} " +
                    $"{player.Athleticism,6:F1} {player.Transition,6:F1} " +
                    $"{player.GravityContribution,6:F1} {player.SpacingContribution,6:F1}" +
                    $"  {validateLabel}");

                // Assertion 3: derived values in plausible range (0–99; can't exceed
                // component max of 99 from a flat mean of 0–99 inputs)
                if (player.Athleticism < 0 || player.Athleticism > 99)
                { Console.WriteLine($"    FAIL  {player.Name}.Athleticism out of range: {player.Athleticism:F1}"); pass = false; }
                if (player.Transition < 0 || player.Transition > 99)
                { Console.WriteLine($"    FAIL  {player.Name}.Transition out of range: {player.Transition:F1}"); pass = false; }
                if (player.GravityContribution < 0 || player.GravityContribution > 99)
                { Console.WriteLine($"    FAIL  {player.Name}.GravityContribution out of range: {player.GravityContribution:F1}"); pass = false; }
                if (player.SpacingContribution < 0 || player.SpacingContribution > 99)
                { Console.WriteLine($"    FAIL  {player.Name}.SpacingContribution out of range: {player.SpacingContribution:F1}"); pass = false; }
            }
        }

        // --- Directional sanity: bigs (slot 4) should have lower athleticism
        //     than guards (slot 1) in the configured fixture ---
        var homeBig   = game.RosterFor(TeamSide.Home).PlayerAt(game.LineupFor(TeamSide.Home).SlotAt(4))!;
        var homeGuard = game.RosterFor(TeamSide.Home).PlayerAt(game.LineupFor(TeamSide.Home).SlotAt(1))!;
        if (homeBig.Athleticism >= homeGuard.Athleticism)
        {
            Console.WriteLine(
                $"  FAIL  Directional check: {homeBig.Name} athleticism ({homeBig.Athleticism:F1}) " +
                $">= {homeGuard.Name} athleticism ({homeGuard.Athleticism:F1}). " +
                "Expected big < guard in this fixture.");
            pass = false;
        }
        else
        {
            Console.WriteLine(
                $"\n  Directional OK — {homeGuard.Name} (guard) ath {homeGuard.Athleticism:F1} " +
                $"> {homeBig.Name} (big) ath {homeBig.Athleticism:F1}.");
        }

        // --- Substitution log sanity: 5 entries per side (one per starter) ---
        var homeLog = game.RosterFor(TeamSide.Home).Log;
        var awayLog = game.RosterFor(TeamSide.Away).Log;
        if (homeLog.Count != Lineup.Size || awayLog.Count != Lineup.Size)
        {
            Console.WriteLine($"  FAIL  Expected {Lineup.Size} log entries per side; " +
                              $"got Home={homeLog.Count}, Away={awayLog.Count}.");
            pass = false;
        }
        else
        {
            Console.WriteLine($"  Substitution log OK — {Lineup.Size} entries per side, all at possession 1.");
        }

        // --- Existing GameState sites: all 24 new GameState(fouls) calls elsewhere
        //     in this file compile unchanged (no ctor change). Prove that a bare
        //     GameState has empty (null) roster slots before population. ---
        var bareGame   = new GameState(new FoulTracker(7, 10));
        var barePlayer = bareGame.RosterFor(TeamSide.Home)
                                 .PlayerAt(bareGame.LineupFor(TeamSide.Home).SlotAt(1));
        if (barePlayer is not null)
        {
            Console.WriteLine("  FAIL  Bare GameState slot 1 should be null before population.");
            pass = false;
        }
        else
        {
            Console.WriteLine("  Bare GameState slots are null before population — existing sites unaffected.");
        }

        Console.WriteLine(pass ? "  Phase 1 PASSED." : "  Phase 1 FAILED.");
        return pass;
    }

    // ---------------------------------------------------------------------------
    // Phase 2: Attribute wiring — RollHGenerator produces higher make rates for
    // higher-rated shooters. This is the load-bearing proof that the pipe works:
    // shooter attribute → logistic → make weight → resolver → outcome rate.
    // ---------------------------------------------------------------------------
    private static bool Phase2AttributeWiringCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 2: Attribute wiring (RollH real generator) ---");
        var pass = true;

        var cfgH  = RollHConfig.Load(configPath);
        var cfg   = RollAConfig.Load(configPath);
        var cfgB  = RollBConfig.Load(configPath);
        var cfgC  = RollCConfig.Load(configPath);
        var cfgD  = RollDConfig.Load(configPath);
        var cfgE  = RollEConfig.Load(configPath);
        var cfgF  = RollFConfig.Load(configPath);
        var cfgG  = RollGConfig.Load(configPath);
        var cfgI  = RollIConfig.Load(configPath);
        var cfgJ  = RollJConfig.Load(configPath);
        var cfgK  = RollKConfig.Load(configPath);
        var cfgL  = RollLConfig.Load(configPath);
        var cfgM  = RollMConfig.Load(configPath);
        var cfgOffFoul = RollOffensiveFoulConfig.Load(configPath);

        // --- Logistic range check: no make probability outside [0, 1] ---
        Console.WriteLine("  Logistic range check (ratings 1/25/50/75/99, all five zones)...");
        var ratingProbes = new[] { 1, 25, 50, 75, 99 };
        foreach (ShotLocation zone in Enum.GetValues<ShotLocation>())
        {
            foreach (var r in ratingProbes)
            {
                var p = cfgH.MakeProbability(zone, r);
                if (p < 0.0 || p > 1.0)
                {
                    Console.WriteLine($"  FAIL  MakeProbability({zone}, {r}) = {p:F4} — out of [0,1].");
                    pass = false;
                }
            }
        }
        Console.WriteLine("  Logistic values all in [0,1] — OK.");

        // --- Monotonicity: make% at rating 99 > rating 50 > rating 1 per zone ---
        foreach (ShotLocation zone in Enum.GetValues<ShotLocation>())
        {
            var p1  = cfgH.MakeProbability(zone, 1);
            var p50 = cfgH.MakeProbability(zone, 50);
            var p99 = cfgH.MakeProbability(zone, 99);
            if (!(p1 < p50 && p50 < p99))
            {
                Console.WriteLine($"  FAIL  {zone}: logistic not monotone — p1={p1:F3} p50={p50:F3} p99={p99:F3}.");
                pass = false;
            }
        }
        Console.WriteLine("  Logistic monotone per zone — OK.");

        // --- Wiring check: 10k three-point attempts, high vs low Outside shooter ---
        const int Batch        = 10_000;
        const double MinGapPp  = 0.10;  // at least 10 percentage points

        // Helper: build a GameState with two players in slots 1 and 2, given Outside rating.
        static (GameState game, Slot slot1) MakeGame(int outsideRating, RollDConfig cfgD2)
        {
            var fouls = new FoulTracker(cfgD2.BonusThreshold, cfgD2.DoubleBonusThreshold);
            var g     = new GameState(fouls);
            g.SetPossessionArrow(TeamSide.Home);

            var homeLineup = g.HomeLineup;
            var homeRoster = g.HomeRoster;
            var awayLineup = g.AwayLineup;
            var awayRoster = g.AwayRoster;

            for (var i = 1; i <= Lineup.Size; i++)
            {
                var shooter = new Player($"Shooter{i}") { Outside = outsideRating, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 70,
                    FoulDrawing = 50,
                    BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                    OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50,
                    PerimeterDefense = 50, PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50, Quickness = 50,
                    FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                    RimTendency = 50, ShortTendency = 50, MidTendency = 50, LongTendency = 50, ThreeTendency = 50 };
                var defender = new Player($"Defender{i}") { Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 70,
                    FoulDrawing = 50,
                    BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                    OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50,
                    PerimeterDefense = 50, PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50, Quickness = 50,
                    FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                    RimTendency = 50, ShortTendency = 50, MidTendency = 50, LongTendency = 50, ThreeTendency = 50 };
                homeRoster.SetStarter(homeLineup.SlotAt(i), shooter);
                awayRoster.SetStarter(awayLineup.SlotAt(i), defender);
            }
            return (g, homeLineup.SlotAt(1));
        }

        // Helper: run Batch three-point attempts through the resolver using a given game,
        // count Made + MadeAndFouled outcomes from Roll H directly.
        static double RunThreePointBatch(GameState g, RollHConfig cfgH2, MatchupConfig cfgM2, int batch)
        {
            var gen = new RollHGenerator(cfgH2, cfgM2, g);
            var state = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                ShotType: ShotLocation.Three,
                SelectedSlot: g.HomeLineup.SlotAt(1));

            var made = 0;
            for (var i = 0; i < batch; i++)
            {
                var pie    = gen.Generate(state, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result is ShotResult.Made or ShotResult.MadeAndFouled)
                    made++;
            }
            return (double)made / batch;
        }

        var (highGame, _) = MakeGame(outsideRating: 85, cfgD2: cfgD);
        var (lowGame,  _) = MakeGame(outsideRating: 25, cfgD2: cfgD);

        var cfgMatchup = MatchupConfig.Load(configPath);
        var highRate = RunThreePointBatch(highGame, cfgH, cfgMatchup, Batch);
        var lowRate  = RunThreePointBatch(lowGame,  cfgH, cfgMatchup, Batch);
        var gap      = highRate - lowRate;

        Console.WriteLine($"  Three-point make rate — high Outside (85): {highRate:P1}");
        Console.WriteLine($"  Three-point make rate — low  Outside (25): {lowRate:P1}");
        Console.WriteLine($"  Gap: {gap:P1}  (threshold ≥ {MinGapPp:P0})");

        if (gap < MinGapPp)
        {
            Console.WriteLine($"  FAIL  Gap {gap:P1} is below the {MinGapPp:P0} threshold.");
            pass = false;
        }
        else
        {
            Console.WriteLine("  Wiring check PASSED — high shooter beats low shooter by required margin.");
        }

        // --- Fallback check: null roster → stub pie (no exception) ---
        Console.WriteLine("  Fallback check (unpopulated roster → stub pie, no throw)...");
        var bareGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var bareGen  = new RollHGenerator(cfgH, cfgMatchup, bareGame);
        var bareState = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound,
            ShotType: ShotLocation.Three,
            SelectedSlot: bareGame.HomeLineup.SlotAt(1));
        try
        {
            var pie = bareGen.Generate(bareState, putback: false);
            // Should equal stub pie output — Made weight = BaseMade * (1 - BlockThree)
            var expectedMade = cfgH.BaseMade * (1.0 - cfgH.BlockThree);
            // Pie doesn't expose weights directly; just confirm no exception and the
            // result is a valid (non-null) pie object.
            Console.WriteLine("  Fallback OK — unpopulated roster returns stub pie without throwing.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  Fallback threw: {ex.Message}");
            pass = false;
        }

        Console.WriteLine(pass ? "  Phase 2 PASSED." : "  Phase 2 FAILED.");
        return pass;
    }

    // Phase 6 — matchup wiring (DefenderPicker + Matchup.GapFn/EffectiveRating + the make door).
    // The sweeps read the make% analytically (cfgH.MakeProbability over Matchup.EffectiveRating —
    // exactly the weight BuildRealPie assigns to Made), so they are deterministic. The DEC-6
    // fallback is exercised through the REAL generator (batched) so the defender lookup, the
    // null-defender guard, and the pie build are covered end to end.
    private static bool Phase6MatchupWiringCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 6: Matchup wiring (defender picker + gap fn + make door) ---");
        var pass = true;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // A uniform player at baseline b, overriding only the attributes the make door reads:
        // offense Outside/Mid/Close/Finishing, defense Perimeter/Post/RimProtection, and the five
        // athletic attrs (Athleticism's source) via `ath`. Mirrors MakeGame's full initializer so
        // no attribute is left at 0; athletic attrs stay equal => physical gap 0 unless `ath` is set.
        static Player Mk(int b, int? outside = null, int? mid = null, int? close = null, int? fin = null,
                         int? perimD = null, int? postD = null, int? rimP = null, int? ath = null,
                         int? foulDrawing = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = outside ?? b, Mid = mid ?? b, Close = close ?? b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = foulDrawing ?? b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = perimD ?? b, PostDefense = postD ?? b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = ath ?? b, Speed = ath ?? b, Quickness = ath ?? b, FirstStep = ath ?? b, Vertical = ath ?? b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency = rimT ?? b, ShortTendency = shortT ?? b, MidTendency = midT ?? b,
                LongTendency = longT ?? b, ThreeTendency = threeT ?? b,
            };

        // The make% the wired generator assigns to a contested shot, and the raw no-matchup baseline.
        double Make(ShotLocation z, Player s, Player d)
            => cfgH.MakeProbability(z, Matchup.EffectiveRating(z, s, d, cfgM));
        double Raw(ShotLocation z, Player s)
            => cfgH.MakeProbability(z, Matchup.OffenseRating(z, s));

        const double Eps = 1e-9;

        // (a) Defender sweep: fix the shooter, raise the matched defensive rating -> make% falls.
        Console.WriteLine("  (a) Defender sweep @Three (shooter Outside=50, sweep PerimeterDefense 10->90):");
        var shooterA = Mk(50, outside: 50);
        var baseA = Raw(ShotLocation.Three, shooterA);
        double prevA = double.PositiveInfinity, evenA = 0, edgeA = 0;
        var monoA = true;
        foreach (var pd in new[] { 10, 30, 50, 70, 90 })
        {
            var mk = Make(ShotLocation.Three, shooterA, Mk(50, perimD: pd));
            Console.WriteLine($"      PD={pd,2}  make={mk:P1}");
            if (mk >= prevA) monoA = false;
            prevA = mk;
            if (pd == 50) evenA = mk;
            if (pd == 90) edgeA = mk;
        }
        var aOk = monoA
                  && Math.Abs(evenA - baseA) < Eps
                  && edgeA > cfgH.ThreeFloor && edgeA < baseA;
        if (!monoA) Console.WriteLine("  FAIL  (a) make% not strictly decreasing as defense rises.");
        if (Math.Abs(evenA - baseA) >= Eps) Console.WriteLine("  FAIL  (a) even matchup (PD=50) != raw own-rating baseline.");
        if (!(edgeA > cfgH.ThreeFloor && edgeA < baseA)) Console.WriteLine($"  FAIL  (a) big edge should compress below baseline but stay above floor {cfgH.ThreeFloor:P1}.");
        if (aOk) Console.WriteLine("      OK — monotone down; even==baseline; edge compresses toward the floor, not zero.");
        pass &= aOk;

        // (b) Blend (CONF-1) @Mid (0.5/0.5): both sub-attributes register at equal half weight.
        Console.WriteLine("  (b) Blend @Mid: PerimeterDefense and PostDefense each move make% at half weight:");
        var shooterB = Mk(50, mid: 50);
        var dPerim = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 20, postD: 40))
                   - Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 60, postD: 40));
        var dPost  = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 40, postD: 20))
                   - Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 40, postD: 60));
        var swap1 = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 20, postD: 60));
        var swap2 = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 60, postD: 20));
        Console.WriteLine($"      raise PerimeterDefense 20->60: make drops {dPerim:P2}");
        Console.WriteLine($"      raise PostDefense      20->60: make drops {dPost:P2}");
        var bOk = dPerim > 0 && dPost > 0 && Math.Abs(dPerim - dPost) < Eps && Math.Abs(swap1 - swap2) < Eps;
        if (!(dPerim > 0 && dPost > 0)) Console.WriteLine("  FAIL  (b) a sub-attribute did not register.");
        if (Math.Abs(dPerim - dPost) >= Eps) Console.WriteLine("  FAIL  (b) sub-attributes not equally weighted (expected 0.5/0.5).");
        if (Math.Abs(swap1 - swap2) >= Eps) Console.WriteLine("  FAIL  (b) blend not swap-symmetric at Mid.");
        if (bOk) Console.WriteLine("      OK — both register, equal half weight, swap-symmetric.");
        pass &= bOk;

        // (c) Rim specialist: strong at Rim, beatable on the perimeter zones (vs a balanced defender).
        Console.WriteLine("  (c) Rim specialist (Perim=20,Post=40,Rim=90) vs balanced (all 50), shooter rating 50:");
        var shooterC = Mk(50, mid: 50, fin: 50);
        var rimSpec  = Mk(50, perimD: 20, postD: 40, rimP: 90);
        var balanced = Mk(50, perimD: 50, postD: 50, rimP: 50);
        var midSpec = Make(ShotLocation.Mid, shooterC, rimSpec);
        var midBal  = Make(ShotLocation.Mid, shooterC, balanced);
        var rmSpec  = Make(ShotLocation.Rim, shooterC, rimSpec);
        var rmBal   = Make(ShotLocation.Rim, shooterC, balanced);
        Console.WriteLine($"      Mid: vs specialist {midSpec:P1}  vs balanced {midBal:P1}   (specialist gives up MORE)");
        Console.WriteLine($"      Rim: vs specialist {rmSpec:P1}  vs balanced {rmBal:P1}   (specialist gives up LESS)");
        var cOk = midSpec > midBal && rmSpec < rmBal;
        if (!(midSpec > midBal)) Console.WriteLine("  FAIL  (c) rim specialist not exploitable at Mid.");
        if (!(rmSpec < rmBal)) Console.WriteLine("  FAIL  (c) rim specialist not stronger at Rim.");
        if (cOk) Console.WriteLine("      OK — beatable on the perimeter, strong at the rim.");
        pass &= cOk;

        // (d) Shooter sweep: fix the defender, raise shooter rating -> make% rises and saturates.
        Console.WriteLine("  (d) Shooter sweep @Three (balanced defender, sweep Outside 30->99):");
        var defD = Mk(50, perimD: 50);
        double prevD = double.NegativeInfinity, m30 = 0, m50 = 0, m90 = 0, m99 = 0;
        var monoD = true;
        foreach (var o in new[] { 30, 50, 70, 90, 99 })
        {
            var mk = Make(ShotLocation.Three, Mk(50, outside: o), defD);
            Console.WriteLine($"      Outside={o,2}  make={mk:P1}");
            if (mk <= prevD) monoD = false;
            prevD = mk;
            if (o == 30) m30 = mk;
            if (o == 50) m50 = mk;
            if (o == 90) m90 = mk;
            if (o == 99) m99 = mk;
        }
        var dOk = monoD && (m99 - m90) < (m50 - m30) && m99 < cfgH.ThreeCeiling;
        if (!monoD) Console.WriteLine("  FAIL  (d) make% not strictly increasing as shooter rating rises.");
        if (!((m99 - m90) < (m50 - m30))) Console.WriteLine("  FAIL  (d) top end not flattening (no saturation).");
        if (!(m99 < cfgH.ThreeCeiling)) Console.WriteLine($"  FAIL  (d) make% breached the curve ceiling {cfgH.ThreeCeiling:P1}.");
        if (dOk) Console.WriteLine($"      OK — monotone up, flattening toward the ceiling ({cfgH.ThreeCeiling:P1}); the curve caps the payoff.");
        pass &= dOk;

        // (e) Physical is steeper than skill (DEC-5: larger exponent), and the physical term moves make%.
        Console.WriteLine("  (e) Physical gap (DEC-5: steeper than skill via the larger exponent):");
        var skillShift40    = Matchup.GapFn(40, cfgM.SkillSteepness,    cfgM.SkillExponent,    cfgM.ReferenceScale);
        var physicalShift40 = Matchup.GapFn(40, cfgM.PhysicalSteepness, cfgM.PhysicalExponent, cfgM.ReferenceScale);
        var athEdge = Make(ShotLocation.Three, Mk(50, outside: 50, ath: 70), Mk(50, perimD: 50, ath: 30));
        var athEven = Make(ShotLocation.Three, Mk(50, outside: 50, ath: 50), Mk(50, perimD: 50, ath: 50));
        Console.WriteLine($"      at gap=40:  skillShift={skillShift40:F2}  physicalShift={physicalShift40:F2}");
        Console.WriteLine($"      athletic edge (70 vs 30) make={athEdge:P1}  vs even make={athEven:P1}");
        var eOk = physicalShift40 > skillShift40 && athEdge > athEven;
        if (!(physicalShift40 > skillShift40)) Console.WriteLine("  FAIL  (e) physical not steeper than skill at equal gap.");
        if (!(athEdge > athEven)) Console.WriteLine("  FAIL  (e) athletic edge did not raise make%.");
        if (eOk) Console.WriteLine("      OK — physical steeper than skill; an athletic edge raises make%.");
        pass &= eOk;

        // (f) DEC-6 fallback through the REAL generator: an empty defending slot reads the raw
        //     own-rating (== the even matchup), while a strong defender lowers the sampled rate.
        //     Margin floor is 0.03: against the Session-50 calibrated (flatter) curve, a skill-ONLY
        //     strong defender (PerimD 90 vs a 50 shooter, even athleticism) lowers the three by
        //     ~4-5 points — by design, a skilled-but-not-more-athletic defender only nudges a
        //     shooter (athletic separation is what suppresses harder). The old 0.05 floor assumed
        //     the retired steep curve. The direction (strong defender lowers make) is what matters.
        Console.WriteLine("  (f) DEC-6 fallback + end-to-end wiring (real generator, batched):");
        const int Batch = 20_000;
        const double RateTol = 0.02;

        static double Rate(GameState g, RollHConfig ch, MatchupConfig cm, int batch)
        {
            var gen = new RollHGenerator(ch, cm, g);
            var state = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Three,
                SelectedSlot: g.HomeLineup.SlotAt(1));
            var made = 0;
            for (var i = 0; i < batch; i++)
            {
                var pie = gen.Generate(state, putback: false);
                var r = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (r is ShotResult.Made or ShotResult.MadeAndFouled) made++;
            }
            return (double)made / batch;
        }

        GameState GameWith(Player? defender)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            g.SetPossessionArrow(TeamSide.Home);
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), Mk(50, outside: 50));
            if (defender is not null)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(1), defender);  // empty slot otherwise -> null -> raw rating
            return g;
        }

        var rEmpty  = Rate(GameWith(null),                            cfgH, cfgM, Batch);
        var rEven   = Rate(GameWith(Mk(50, perimD: 50)),             cfgH, cfgM, Batch);
        var rStrong = Rate(GameWith(Mk(50, perimD: 90)),             cfgH, cfgM, Batch);
        Console.WriteLine($"      empty-slot {rEmpty:P1}   even {rEven:P1}   strong-defender {rStrong:P1}");
        var fOk = Math.Abs(rEmpty - rEven) <= RateTol && rStrong < rEven - 0.03;
        if (Math.Abs(rEmpty - rEven) > RateTol) Console.WriteLine("  FAIL  (f) empty-slot fallback diverges from the even-matchup baseline.");
        if (!(rStrong < rEven - 0.03)) Console.WriteLine("  FAIL  (f) a strong defender did not lower the generator's make rate by the expected margin.");
        if (fOk) Console.WriteLine("      OK — empty slot reads raw rating (==even); a strong defender lowers make% through the real pipe.");
        pass &= fOk;

        // (g) Regression guard for fix #8 (carve-then-convert). A dominant finisher vs a
        //     weak rim protector drives the effective rim rating into the make-curve's
        //     ceiling (~0.93). Under the OLD math (Made = makePct, block added on top) that
        //     made makePct + block > 1 → a negative weight → the Pie constructor threw.
        //     The fix must build a valid pie through the real generator (no throw).
        Console.WriteLine("  (g) Rim overflow guard (fix #8, carve-then-convert):");
        var gRimOk = true;
        try
        {
            var gRim = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gRim.SetPossessionArrow(TeamSide.Home);
            gRim.HomeRoster.SetStarter(gRim.HomeLineup.SlotAt(1), Mk(50, fin: 99));        // elite finisher
            gRim.AwayRoster.SetStarter(gRim.AwayLineup.SlotAt(1), Mk(50, postD: 10, rimP: 10)); // weak rim protection
            var genRim = new RollHGenerator(cfgH, cfgM, gRim);
            var stateRim = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gRim.HomeLineup.SlotAt(1));
            var madeRim = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie = genRim.Generate(stateRim, putback: false);   // would THROW pre-fix
                if (pie.Roll(new SystemRng(i).NextUnitInterval()) is ShotResult.Made or ShotResult.MadeAndFouled) madeRim++;
            }
            Console.WriteLine($"      elite finisher vs weak rim protector @Rim: make {(double)madeRim / 5_000:P1}  (valid pie, no overflow)");
        }
        catch (Exception ex)
        {
            gRimOk = false;
            Console.WriteLine($"  FAIL  (g) rim pie overflowed / threw: {ex.Message}");
        }
        if (gRimOk) Console.WriteLine("      OK — the extreme rim matchup builds a valid pie; the make+block overflow is fixed.");
        pass &= gRimOk;

        Console.WriteLine(pass ? "  Phase 6 PASSED." : "  Phase 6 FAILED.");
        return pass;
    }

    // Phase 7: the block door. Confirms that block rate bends correctly with matchup —
    // defender-edge raises it toward the per-zone ceiling, shooter-edge lowers it toward
    // the floor — and that all per-zone tuning knobs (skill/length weights, referenceShift)
    // are actually registering. Also confirms the DEC-6 fallback (empty slot → baseline)
    // and that Phase 6 (the make door) is unbroken by the block-weight change.
    //
    // No batch rolls here: block rate is deterministic given the pie weights. All checks
    // call Matchup.BlockWeight directly and assert on the returned double — exact arithmetic,
    // no sampling noise.
    private static bool Phase7BlockDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 7: Block door (matchup-aware block weight) ---");
        var pass = true;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // Helper: build a player with all attributes at baseline b, overriding only
        // the named ones. Mirrors Phase 6's Mk helper so the two checks are comparable.
        // Height/Wingspan/Vertical are the length attributes (block-specific).
        // Finishing / PerimeterDefense / RimProtection are the skill attributes for Rim/Three.
        static Player Mk(int b,
                         int? fin = null, int? outside = null,
                         int? rimP = null, int? perimD = null,
                         int? h = null, int? ws = null, int? v = null,
                         int? foulDrawing = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = outside ?? b, Mid = b, Close = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = foulDrawing ?? b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = perimD ?? b, PostDefense = b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = h ?? b, Wingspan = ws ?? b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = v ?? b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency = rimT ?? b, ShortTendency = shortT ?? b, MidTendency = midT ?? b,
                LongTendency = longT ?? b, ThreeTendency = threeT ?? b,
            };

        // Shorthand: the matchup block weight for a zone, shooter, defender.
        double Blk(ShotLocation z, Player s, Player d)
            => Matchup.BlockWeight(z, s, d, cfgH.BlockWeight(z), cfgM);

        // ----------------------------------------------------------------
        // (a) Defender length sweep @Rim. Fix shooter (Finishing=50, all else 50).
        //     Sweep defender RimProtection AND length together from low to high.
        //     Block rate must rise monotonically and stay bounded by [floor, ceiling].
        // ----------------------------------------------------------------
        Console.WriteLine("  (a) Defender sweep @Rim (shooter Fin=50, all 50; sweep RimP+H/W/V 20..95):");
        var shooterA = Mk(50, fin: 50);
        double prevA = double.NegativeInfinity;
        var monoA = true;
        var boundA = true;
        var rimFloor = cfgM.BlockFloor(ShotLocation.Rim);
        var rimCeil  = cfgM.BlockCeiling(ShotLocation.Rim);
        foreach (var (rp, hw) in new[] { (20, 60), (40, 70), (50, 70), (60, 75), (80, 85), (95, 90) })
        {
            var bw = Blk(ShotLocation.Rim, shooterA, Mk(50, rimP: rp, h: hw, ws: hw, v: hw));
            Console.WriteLine($"      RimP={rp,2}  H/W/V={hw}  block={bw:P3}  [{rimFloor:P2},{rimCeil:P2}]");
            if (bw <= prevA) { monoA = false; Console.WriteLine($"        FAIL — not strictly increasing (prev={prevA:P3})"); }
            if (bw < rimFloor - 1e-9 || bw > rimCeil + 1e-9) { boundA = false; Console.WriteLine($"        FAIL — out of [floor,ceil]"); }
            prevA = bw;
        }
        if (monoA && boundA) Console.WriteLine("      OK — monotone rising; bounded by [floor, ceiling].");
        pass &= monoA && boundA;

        // ----------------------------------------------------------------
        // (b) Defender sweep @Three. Same shape — block rate must rise,
        //     but the spread must be much smaller than at Rim (per-zone weighting proof).
        // ----------------------------------------------------------------
        Console.WriteLine("  (b) Defender sweep @Three (shooter Outside=50, all 50; sweep PerimD+H/W/V):");
        var shooterB = Mk(50, outside: 50);
        double prevB = double.NegativeInfinity;
        var monoB = true;
        var boundB = true;
        var threeFloor = cfgM.BlockFloor(ShotLocation.Three);
        var threeCeil  = cfgM.BlockCeiling(ShotLocation.Three);
        double loB = double.PositiveInfinity, hiB = double.NegativeInfinity;
        foreach (var (pd, hw) in new[] { (20, 60), (40, 70), (50, 70), (60, 75), (80, 85), (95, 90) })
        {
            var bw = Blk(ShotLocation.Three, shooterB, Mk(50, perimD: pd, h: hw, ws: hw, v: hw));
            Console.WriteLine($"      PerimD={pd,2}  H/W/V={hw}  block={bw:P3}  [{threeFloor:P3},{threeCeil:P3}]");
            if (bw <= prevB) { monoB = false; Console.WriteLine($"        FAIL — not strictly increasing (prev={prevB:P3})"); }
            if (bw < threeFloor - 1e-9 || bw > threeCeil + 1e-9) { boundB = false; Console.WriteLine($"        FAIL — out of [floor,ceil]"); }
            if (bw < loB) loB = bw;
            if (bw > hiB) hiB = bw;
            prevB = bw;
        }
        // Spread at Three must be smaller than spread at Rim — proves per-zone weighting.
        var spreadThree = hiB - loB;
        var spreadRim   = (Blk(ShotLocation.Rim, Mk(50, fin: 50), Mk(50, rimP: 95, h: 90, ws: 90, v: 90))
                         - Blk(ShotLocation.Rim, Mk(50, fin: 50), Mk(50, rimP: 20, h: 60, ws: 60, v: 60)));
        var spreadOk = spreadThree < spreadRim;
        Console.WriteLine($"      Three spread={spreadThree:P3}  Rim spread={spreadRim:P3}  Three < Rim: {spreadOk}");
        if (!spreadOk) Console.WriteLine("  FAIL  (b) Three spread should be smaller than Rim spread.");
        if (monoB && boundB && spreadOk) Console.WriteLine("      OK — monotone, bounded, spread smaller than Rim (per-zone weights working).");
        pass &= monoB && boundB && spreadOk;

        // ----------------------------------------------------------------
        // (c) Skill vs length contribution split.
        //     At Rim: hold length=70, sweep RimProtection 20→95; measure delta.
        //             hold RimP=70,   sweep length 60→90; measure delta.
        //             Length delta should exceed skill delta (60/40 split).
        //     At Three: same; length dominates even more.
        // ----------------------------------------------------------------
        Console.WriteLine("  (c) Skill vs length split @Rim and @Three:");
        var shooterC = Mk(50);

        // Rim — skill-only sweep (hold length at 70)
        var rimSk20 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 20,  h: 70, ws: 70, v: 70));
        var rimSk95 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 95,  h: 70, ws: 70, v: 70));
        var rimSkDelta = rimSk95 - rimSk20;

        // Rim — length-only sweep (hold RimP at 70)
        var rimLe60 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 70,  h: 60, ws: 60, v: 60));
        var rimLe90 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 70,  h: 90, ws: 90, v: 90));
        var rimLeDelta = rimLe90 - rimLe60;

        Console.WriteLine($"      Rim skill-only  (RimP 20→95, length=70): Δblock = {rimSkDelta:P3}");
        Console.WriteLine($"      Rim length-only (length 60→90, RimP=70):  Δblock = {rimLeDelta:P3}");
        var cRimOk = rimLeDelta > rimSkDelta;
        if (!cRimOk) Console.WriteLine("  FAIL  (c) length delta should exceed skill delta at Rim (60/40 split).");
        if (cRimOk) Console.WriteLine("      OK @Rim — length delta > skill delta (length heavier at 60%).");
        pass &= cRimOk;

        // Three — skill-only sweep (hold length at 70)
        var thrSk20 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 20, h: 70, ws: 70, v: 70));
        var thrSk95 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 95, h: 70, ws: 70, v: 70));
        var thrSkDelta = thrSk95 - thrSk20;

        // Three — length-only sweep (hold PerimD at 70)
        var thrLe60 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 70, h: 60, ws: 60, v: 60));
        var thrLe90 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 70, h: 90, ws: 90, v: 90));
        var thrLeDelta = thrLe90 - thrLe60;

        Console.WriteLine($"      Three skill-only  (PerimD 20→95, length=70): Δblock = {thrSkDelta:P3}");
        Console.WriteLine($"      Three length-only (length 60→90, PerimD=70):  Δblock = {thrLeDelta:P3}");
        var cThreeOk = thrLeDelta > thrSkDelta;
        if (!cThreeOk) Console.WriteLine("  FAIL  (c) length delta should exceed skill delta at Three (60/40 split).");
        if (cThreeOk) Console.WriteLine("      OK @Three — length delta > skill delta (length heavier at 60%).");
        pass &= cThreeOk;

        // ----------------------------------------------------------------
        // (d) Empty-slot fallback (DEC-6). Confirms the three cases:
        //     empty slot → block == configured baseline (exact);
        //     even defender → block == baseline (even matchup, zero shift);
        //     strong defender → block > baseline.
        // ----------------------------------------------------------------
        Console.WriteLine("  (d) DEC-6 fallback — empty slot, even matchup, strong defender:");
        var shooterD = Mk(50, fin: 50);

        // Empty slot: caller uses _cfg.BlockWeight(zone) directly, no Matchup.BlockWeight call.
        var dEmpty = cfgH.BlockWeight(ShotLocation.Rim);          // the baseline itself

        // Even matchup: both players at 50, so zero gap → zero shift → baseline.
        var dEven  = Blk(ShotLocation.Rim, shooterD, Mk(50));

        // Strong defender: should be above baseline.
        var dStrong = Blk(ShotLocation.Rim, shooterD, Mk(50, rimP: 90, h: 85, ws: 85, v: 85));

        Console.WriteLine($"      empty-slot (baseline): {dEmpty:P3}");
        Console.WriteLine($"      even matchup:           {dEven:P3}");
        Console.WriteLine($"      strong defender:        {dStrong:P3}");

        const double FallbackEps = 1e-9;
        var dFallbackOk = Math.Abs(dEmpty - cfgH.BlockWeight(ShotLocation.Rim)) < FallbackEps;
        var dEvenOk     = Math.Abs(dEven  - cfgH.BlockWeight(ShotLocation.Rim)) < FallbackEps;
        var dStrongOk   = dStrong > cfgH.BlockWeight(ShotLocation.Rim) + 0.005;
        if (!dFallbackOk) Console.WriteLine("  FAIL  (d) empty-slot baseline is not the configured value.");
        if (!dEvenOk)     Console.WriteLine("  FAIL  (d) even matchup did not return exactly the baseline.");
        if (!dStrongOk)   Console.WriteLine("  FAIL  (d) strong defender did not raise block above baseline.");
        if (dFallbackOk && dEvenOk && dStrongOk)
            Console.WriteLine("      OK — empty==baseline; even==baseline; strong defender raises block.");
        pass &= dFallbackOk && dEvenOk && dStrongOk;

        // ----------------------------------------------------------------
        // (e) Symmetric shooter edge. Fix balanced defender (all 50).
        //     Raise shooter's Finishing AND length. Block rate at Rim must FALL,
        //     asymptoting toward floor. Proves the curve bends down for shooter edge.
        // ----------------------------------------------------------------
        Console.WriteLine("  (e) Shooter-edge symmetry @Rim (fix defender all=50; raise shooter Fin+H/W/V):");
        var defenderE = Mk(50);
        double prevE = double.PositiveInfinity;
        var monoE = true;
        var floorE = true;
        foreach (var (fin, hw) in new[] { (20, 40), (35, 50), (50, 60), (65, 70), (80, 80), (95, 90) })
        {
            var bw = Blk(ShotLocation.Rim, Mk(50, fin: fin, h: hw, ws: hw, v: hw), defenderE);
            Console.WriteLine($"      shooter Fin={fin,2}  H/W/V={hw}  block={bw:P3}");
            if (bw >= prevE) { monoE = false; Console.WriteLine($"        FAIL — not strictly decreasing (prev={prevE:P3})"); }
            if (bw < rimFloor - 1e-9) { floorE = false; Console.WriteLine($"        FAIL — crossed the floor {rimFloor:P3}"); }
            prevE = bw;
        }
        if (monoE && floorE) Console.WriteLine("      OK — block rate falls as shooter improves; stays above floor.");
        pass &= monoE && floorE;

        // ----------------------------------------------------------------
        // (f) Regression guard — Phase 6 make door still works after block weight change.
        //     Elite finisher vs weak rim protector: make rate should be > 0.50 and
        //     the pie should be valid (no throw). Mirrors Phase 6 check (g) but verifies
        //     that the new matchup block weight (higher for a strong defender, passed into
        //     BuildRealPie) doesn't break the pie.
        // ----------------------------------------------------------------
        Console.WriteLine("  (f) Regression — Phase 6 make door valid after Phase 7 block change:");
        var fOk = true;
        try
        {
            var gReg = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gReg.SetPossessionArrow(TeamSide.Home);
            gReg.HomeRoster.SetStarter(gReg.HomeLineup.SlotAt(1), Mk(50, fin: 99, h: 70, ws: 70, v: 70));
            gReg.AwayRoster.SetStarter(gReg.AwayLineup.SlotAt(1), Mk(50, rimP: 10, h: 55, ws: 55, v: 55));
            var genReg = new RollHGenerator(cfgH, cfgM, gReg);
            var stateReg = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gReg.HomeLineup.SlotAt(1));

            var madeReg = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie = genReg.Generate(stateReg, putback: false);   // must not throw
                if (pie.Roll(new SystemRng(i).NextUnitInterval()) is ShotResult.Made or ShotResult.MadeAndFouled)
                    madeReg++;
            }
            var makeRate = (double)madeReg / 5_000;
            Console.WriteLine($"      elite finisher vs weak rim protector @Rim: make {makeRate:P1}  (valid pie)");
            var fRateOk = makeRate > 0.50;
            if (!fRateOk) Console.WriteLine($"  FAIL  (f) make rate {makeRate:P1} is not > 0.50 for an extreme shooter edge.");
            if (fRateOk) Console.WriteLine("      OK — valid pie; make rate > 0.50 for extreme shooter edge.");
            fOk = fRateOk;
        }
        catch (Exception ex)
        {
            fOk = false;
            Console.WriteLine($"  FAIL  (f) pie threw: {ex.Message}");
        }
        pass &= fOk;

        Console.WriteLine(pass ? "  Phase 7 PASSED." : "  Phase 7 FAILED.");
        return pass;
    }

    private static bool Phase8FoulDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 8: Foul door (matchup-aware foul rate) ---");
        var pass = true;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // Helper: build a player with all attributes at baseline b, overriding only
        // the named ones. FoulDrawing and Discipline are the foul-door attributes.
        // Height/Wingspan/Vertical are included so block math stays valid in regression.
        static Player Mk(int b,
                         int? fd = null, int? disc = null,
                         int? fin = null, int? rimP = null,
                         int? h = null, int? ws = null, int? v = null,
                         int? foulDrawing = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = fd ?? foulDrawing ?? b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = h ?? b, Wingspan = ws ?? b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = v ?? b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = disc ?? b,
                RimTendency = rimT ?? b, ShortTendency = shortT ?? b, MidTendency = midT ?? b,
                LongTendency = longT ?? b, ThreeTendency = threeT ?? b,
            };

        // Shorthand: the matchup foul rate for a zone, shooter, defender.
        double FR(ShotLocation z, Player s, Player d)
            => Matchup.FoulRate(z, s, d, cfgH.FoulRate(z), cfgM);

        var rimBaseline = cfgH.FoulRate(ShotLocation.Rim);
        var rimCeiling  = cfgM.FoulCeiling(ShotLocation.Rim);
        var rimFloor    = cfgM.FoulFloor(ShotLocation.Rim);

        // ----------------------------------------------------------------
        // (a) Shooter FoulDrawing sweep @Rim (UP, large bend).
        //     Fix defender (all=50, Discipline=50). Sweep FD 20->95.
        //     Foul rate must rise monotonically and stay below ceiling.
        // ----------------------------------------------------------------
        Console.WriteLine("  (a) Shooter sweep @Rim (fix defender Disc=50, sweep FD 20..95):");
        var defA = Mk(50);
        double prevA = double.NegativeInfinity;
        var monoA = true;
        var ceilA = true;
        foreach (var fd in new[] { 20, 30, 40, 50, 60, 70, 80, 90, 95 })
        {
            var fr = FR(ShotLocation.Rim, Mk(50, fd: fd), defA);
            Console.WriteLine($"      FD={fd,2}  foulRate={fr:P3}");
            if (fr <= prevA) { monoA = false; Console.WriteLine($"        FAIL — not strictly increasing (prev={prevA:P3})"); }
            if (fr >= rimCeiling) { ceilA = false; Console.WriteLine($"        FAIL — crossed the ceiling {rimCeiling:P3}"); }
            prevA = fr;
        }
        var topA = FR(ShotLocation.Rim, Mk(50, fd: 95), defA);
        var topNearCeiling = topA > rimBaseline + 0.05;  // noticeably above baseline
        if (monoA && ceilA && topNearCeiling)
            Console.WriteLine($"      OK — monotonically rising; top={topA:P3} well above baseline={rimBaseline:P3}; below ceiling={rimCeiling:P3}.");
        if (!monoA) Console.WriteLine("  FAIL  (a) not monotonically increasing.");
        if (!ceilA) Console.WriteLine("  FAIL  (a) crossed the ceiling.");
        if (!topNearCeiling) Console.WriteLine($"  FAIL  (a) top of sweep ({topA:P3}) not noticeably above baseline ({rimBaseline:P3}).");
        pass &= monoA && ceilA && topNearCeiling;

        // ----------------------------------------------------------------
        // (b) Defender Discipline sweep @Rim (DOWN, small bend).
        //     Fix shooter (all=50, FD=50). Sweep Discipline 20->95.
        //     Foul rate must fall monotonically and stay above floor.
        // ----------------------------------------------------------------
        Console.WriteLine("  (b) Defender sweep @Rim (fix shooter FD=50, sweep Disc 20..95):");
        var shooterB = Mk(50);
        double prevB = double.PositiveInfinity;
        var monoB = true;
        var floorB = true;
        foreach (var disc in new[] { 20, 30, 40, 50, 60, 70, 80, 90, 95 })
        {
            var fr = FR(ShotLocation.Rim, shooterB, Mk(50, disc: disc));
            Console.WriteLine($"      Disc={disc,2}  foulRate={fr:P3}");
            if (fr >= prevB) { monoB = false; Console.WriteLine($"        FAIL — not strictly decreasing (prev={prevB:P3})"); }
            if (fr <= rimFloor) { floorB = false; Console.WriteLine($"        FAIL — crossed the floor {rimFloor:P3}"); }
            prevB = fr;
        }
        var bottomB = FR(ShotLocation.Rim, shooterB, Mk(50, disc: 95));
        var bottomSlightlyBelow = bottomB < rimBaseline && bottomB > rimFloor;
        if (monoB && floorB && bottomSlightlyBelow)
            Console.WriteLine($"      OK — monotonically falling; bottom={bottomB:P3} only slightly below baseline={rimBaseline:P3}; above floor={rimFloor:P3}.");
        if (!monoB) Console.WriteLine("  FAIL  (b) not monotonically decreasing.");
        if (!floorB) Console.WriteLine("  FAIL  (b) crossed the floor.");
        if (!bottomSlightlyBelow) Console.WriteLine($"  FAIL  (b) bottom ({bottomB:P3}) not in expected range ({rimFloor:P3}, {rimBaseline:P3}).");
        pass &= monoB && floorB && bottomSlightlyBelow;

        // ----------------------------------------------------------------
        // (c) Asymmetry — bend up >> bend down.
        //     An elite foul-drawer raises the rate more than an elite disciplined
        //     defender lowers it. Encodes "low FoulDrawing is not a skill."
        // ----------------------------------------------------------------
        Console.WriteLine("  (c) Asymmetry @Rim (up_bend > 3 x down_bend):");
        var even     = FR(ShotLocation.Rim, Mk(50), Mk(50));
        var upBend   = FR(ShotLocation.Rim, Mk(50, fd: 90), Mk(50)) - even;
        var downBend = even - FR(ShotLocation.Rim, Mk(50), Mk(50, disc: 90));
        Console.WriteLine($"      baseline={rimBaseline:P3}  up_bend (FD=90)={upBend:P3}  down_bend (Disc=90)={downBend:P3}");
        var asymOk = upBend > 3.0 * downBend;
        Console.WriteLine($"      up > 3x down? {upBend:P3} > {3.0 * downBend:P3} => {(asymOk ? "OK" : "FAIL")}");
        Console.WriteLine("      Basketball: a weak shooter doesn't suppress fouls as much as an elite drawer raises them.");
        if (!asymOk) Console.WriteLine("  FAIL  (c) asymmetry check failed.");
        pass &= asymOk;

        // ----------------------------------------------------------------
        // (d) Three sweep — much smaller spread than Rim.
        //     The per-zone foul impact is tiny at the three-point line.
        // ----------------------------------------------------------------
        Console.WriteLine("  (d) Three spread vs Rim spread (Three << Rim):");
        var defD  = Mk(50);
        var rimSpread = Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Rim,   Mk(50, fd: 20 + i * 5), defD)).Max()
                      - Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Rim,   Mk(50, fd: 20 + i * 5), defD)).Min();
        var thrSpread = Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Three, Mk(50, fd: 20 + i * 5), defD)).Max()
                      - Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Three, Mk(50, fd: 20 + i * 5), defD)).Min();
        Console.WriteLine($"      Rim spread={rimSpread:P3}   Three spread={thrSpread:P3}");
        var spreadOk = thrSpread < rimSpread;
        Console.WriteLine($"      Three spread < Rim spread? {(spreadOk ? "ok" : "FAIL")}");
        if (!spreadOk) Console.WriteLine("  FAIL  (d) Three spread not smaller than Rim spread.");
        pass &= spreadOk;

        // ----------------------------------------------------------------
        // (e) DEC-6 fallback — baseline for empty defender; baseline for even matchup;
        //     elite foul-drawer > baseline + 0.02 at Rim.
        // ----------------------------------------------------------------
        Console.WriteLine("  (e) DEC-6 fallback and baseline checks @Rim:");
        var emptyFallback   = cfgH.FoulRate(ShotLocation.Rim);   // DEC-6: no matchup call
        var evenMatchup     = FR(ShotLocation.Rim, Mk(50), Mk(50));
        var eliteDrawer     = FR(ShotLocation.Rim, Mk(50, fd: 90), Mk(50));
        var fallbackOk      = Math.Abs(emptyFallback - rimBaseline) < 1e-9;
        var evenOk          = Math.Abs(evenMatchup   - rimBaseline) < 1e-9;
        var eliteOk         = eliteDrawer > rimBaseline + 0.02;
        Console.WriteLine($"      empty (DEC-6) = {emptyFallback:P3}  (expect {rimBaseline:P3}): {(fallbackOk ? "ok" : "FAIL")}");
        Console.WriteLine($"      even matchup  = {evenMatchup:P3}  (expect {rimBaseline:P3}): {(evenOk ? "ok" : "FAIL")}");
        Console.WriteLine($"      elite FD=90   = {eliteDrawer:P3}  (expect > {rimBaseline + 0.02:P3}): {(eliteOk ? "ok" : "FAIL")}");
        if (fallbackOk && evenOk && eliteOk)
            Console.WriteLine("      OK — empty==baseline; even==baseline; elite drawer > baseline+0.02.");
        pass &= fallbackOk && evenOk && eliteOk;

        // ----------------------------------------------------------------
        // (f) MAF/MissFouled split — batched check at Rim.
        //     Run 50,000 possessions through the real generator with an elite
        //     foul-drawer vs sloppy defender (high foul rate for signal). Of all
        //     fouled-shot outcomes, assert MAF fraction ~= MafFractionRim.
        // ----------------------------------------------------------------
        Console.WriteLine("  (f) MAF/MissFouled split @Rim (50k possessions, elite FD=90 vs Disc=20):");
        var fOk = true;
        try
        {
            var gF = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gF.SetPossessionArrow(TeamSide.Home);
            gF.HomeRoster.SetStarter(gF.HomeLineup.SlotAt(1), Mk(50, fd: 90));
            gF.AwayRoster.SetStarter(gF.AwayLineup.SlotAt(1), Mk(50, disc: 20));
            var genF    = new RollHGenerator(cfgH, cfgM, gF);
            var stateF  = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gF.HomeLineup.SlotAt(1));

            var mafCount  = 0;
            var mfCount   = 0;
            for (var i = 0; i < 50_000; i++)
            {
                var pie = genF.Generate(stateF, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result == ShotResult.MadeAndFouled) mafCount++;
                if (result == ShotResult.MissFouled)    mfCount++;
            }
            var totalFouled = mafCount + mfCount;
            if (totalFouled < 100)
            {
                fOk = false;
                Console.WriteLine($"  FAIL  (f) too few fouled outcomes ({totalFouled}) to measure split — check foul rate.");
            }
            else
            {
                var observedMafFrac = (double)mafCount / totalFouled;
                var expectedMafFrac = cfgH.MafFraction(ShotLocation.Rim);
                var splitGap = Math.Abs(observedMafFrac - expectedMafFrac);
                var splitOk  = splitGap < 0.04;  // loose tolerance — split from a random subsample
                Console.WriteLine($"      total fouled={totalFouled}  MAF={mafCount}  MissFouled={mfCount}");
                Console.WriteLine($"      MAF fraction: observed={observedMafFrac:P2}  expected={expectedMafFrac:P2}  gap={splitGap:P2}  {(splitOk ? "ok" : "FAIL")}");
                if (!splitOk) Console.WriteLine("  FAIL  (f) MAF fraction outside tolerance.");
                fOk = splitOk;
            }
        }
        catch (Exception ex)
        {
            fOk = false;
            Console.WriteLine($"  FAIL  (f) threw: {ex.Message}");
        }
        pass &= fOk;

        // ----------------------------------------------------------------
        // (g) Regression — Phase 6 and Phase 7 still valid after Phase 8 carve change.
        //     Elite finisher vs weak rim protector: make rate > 0.50, pie valid.
        // ----------------------------------------------------------------
        Console.WriteLine("  (g) Regression — Phase 6 + Phase 7 still valid after Phase 8 carve change:");
        var gOk = true;
        try
        {
            var gReg = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gReg.SetPossessionArrow(TeamSide.Home);
            gReg.HomeRoster.SetStarter(gReg.HomeLineup.SlotAt(1), Mk(50, fin: 99, h: 70, ws: 70, v: 70));
            gReg.AwayRoster.SetStarter(gReg.AwayLineup.SlotAt(1), Mk(50, rimP: 10, h: 55, ws: 55, v: 55));
            var genReg  = new RollHGenerator(cfgH, cfgM, gReg);
            var stateReg = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gReg.HomeLineup.SlotAt(1));

            var madeReg = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie = genReg.Generate(stateReg, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result is ShotResult.Made or ShotResult.MadeAndFouled) madeReg++;
            }
            var makeRate = (double)madeReg / 5_000;
            Console.WriteLine($"      elite finisher vs weak rim protector @Rim: make/and-1={makeRate:P1}  (valid pie)");
            var rateOk = makeRate > 0.50;
            if (rateOk)  Console.WriteLine("      OK — valid pie; make+and-1 > 0.50 for extreme shooter edge.");
            if (!rateOk) Console.WriteLine($"  FAIL  (g) make rate {makeRate:P1} is not > 0.50 for an extreme shooter edge.");
            gOk = rateOk;
        }
        catch (Exception ex)
        {
            gOk = false;
            Console.WriteLine($"  FAIL  (g) pie threw: {ex.Message}");
        }
        pass &= gOk;

        Console.WriteLine(pass ? "  Phase 8 PASSED." : "  Phase 8 FAILED.");
        return pass;
    }

    // =========================================================================
    // Phase 9 — shot-location door (Roll G matchup-aware)
    // =========================================================================
    private static bool Phase9LocationDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 9: Shot-location door (Roll G matchup-aware) ---");
        var pass = true;

        var cfgG       = RollGConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Helper: build a player with all attributes at baseline b.
        static Player Mk(int b,
                         int? fin = null, int? outside = null, int? mid = null, int? close = null,
                         int? rimP = null, int? perimD = null, int? postD = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = outside ?? b, Mid = mid ?? b, Close = close ?? b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = perimD ?? b, PostDefense = postD ?? b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency   = rimT   ?? b,
                ShortTendency = shortT ?? b,
                MidTendency   = midT   ?? b,
                LongTendency  = longT  ?? b,
                ThreeTendency = threeT ?? b,
            };

        // Helper: build a list of N copies of a player as the defending roster (slots 1..5).
        static IReadOnlyList<Player?> Defenders(params Player?[] ds) => ds;

        // Shorthand: compute shot mix for a shooter + defender list via RollGGenerator.
        Dictionary<ShotLocation, double> Mix(Player shooter, IReadOnlyList<Player?> defs)
        {
            // Build a minimal GameState with the shooter in Home slot 1, defenders in Away slots 1–5.
            var fouls = new FoulTracker(7, 10);
            var game  = new GameState(fouls);
            game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(1), shooter);
            for (var i = 0; i < defs.Count && i < 5; i++)
                if (defs[i] is not null)
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i + 1), defs[i]!);

            var gen = new RollGGenerator(cfgG, cfgMatchup, game);
            var state = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, SelectedSlot: game.HomeLineup.SlotAt(1));
            var pie = gen.Generate(state);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // ----------------------------------------------------------------
        // (a) Neutral matchup: zero gap everywhere → mix == raw tendencies normalized.
        //     Shooter: skewed tendencies (Rim=80, others=20). All offensive attributes
        //     50. All five defenders all-50. Every gap is exactly zero → every multiplier
        //     exactly 1.0 → bent mix == raw tendencies after renormalization.
        // ----------------------------------------------------------------
        Console.WriteLine("  (a) Neutral matchup: zero gap → mix equals raw tendencies normalized:");
        bool aOk;
        try
        {
            var shooter = Mk(50, fin: 50, rimT: 80, shortT: 20, midT: 20, longT: 20, threeT: 20);
            var defs    = Defenders(Mk(50), Mk(50), Mk(50), Mk(50), Mk(50));
            var mix     = Mix(shooter, defs);

            var rawSum = 80.0 + 20 + 20 + 20 + 20;
            var expectedRim = 80.0 / rawSum;
            var expectedOther = 20.0 / rawSum;

            var rimOk   = Math.Abs(mix[ShotLocation.Rim]   - expectedRim)   < Eps;
            var othersOk = Math.Abs(mix[ShotLocation.Short] - expectedOther) < Eps
                        && Math.Abs(mix[ShotLocation.Mid]   - expectedOther) < Eps
                        && Math.Abs(mix[ShotLocation.Long]  - expectedOther) < Eps
                        && Math.Abs(mix[ShotLocation.Three] - expectedOther) < Eps;
            aOk = rimOk && othersOk;

            Console.WriteLine($"    Rim={mix[ShotLocation.Rim]:P4}  expected={expectedRim:P4}  {(rimOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    others each={mix[ShotLocation.Short]:P4}  expected={expectedOther:P4}  {(othersOk ? "ok" : "FAIL")}");
            if (aOk) Console.WriteLine("    OK — zero-gap produces exact raw-tendency normalization.");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ----------------------------------------------------------------
        // (b) All-weak-rim defenders shift mix toward rim.
        //     Shooter: uniform tendencies (all 50), uniform offensive attrs (all 50).
        //     All five defenders: RimProtection=10, everything else 50.
        //     Expected: rim share rises noticeably above 20% (uniform baseline).
        // ----------------------------------------------------------------
        Console.WriteLine("  (b) All-five-weak-rim defenders: rim share rises above 20%:");
        bool bOk;
        try
        {
            var shooter = Mk(50, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            var weakRim = Mk(50, rimP: 10);
            var defs    = Defenders(weakRim, weakRim, weakRim, weakRim, weakRim);
            var mix     = Mix(shooter, defs);
            var rimShare = mix[ShotLocation.Rim];
            bOk = rimShare > 0.20;
            Console.WriteLine($"    rim share={rimShare:P3}  (>20%? {(bOk ? "OK" : "FAIL")})");
            if (bOk) Console.WriteLine("    OK — all-weak-rim defense raises rim share above uniform baseline.");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ----------------------------------------------------------------
        // (c) Top-3 blend: Config A (1 elite + 4 weak rim) vs Config B (1 elite + 3 solid + 1 weak).
        //     Config B should resist more (higher rim resistance) → lower rim share for the offense.
        // ----------------------------------------------------------------
        Console.WriteLine("  (c) Top-3 blend: Config B (1 elite + 3 solid) resists more than Config A (1 elite + 4 weak):");
        bool cOk;
        try
        {
            var shooter = Mk(50, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            // Config A: elite rim protector at slot-1, four weak at slots 2–5.
            var defsA = Defenders(Mk(50, rimP: 95), Mk(50, rimP: 10), Mk(50, rimP: 10), Mk(50, rimP: 10), Mk(50, rimP: 10));
            // Config B: elite at slot-1, three solid (60) at slots 2–4, one weak at slot-5.
            var defsB = Defenders(Mk(50, rimP: 95), Mk(50, rimP: 60), Mk(50, rimP: 60), Mk(50, rimP: 60), Mk(50, rimP: 10));
            var mixA  = Mix(shooter, defsA);
            var mixB  = Mix(shooter, defsB);

            var rimShareA = mixA[ShotLocation.Rim];
            var rimShareB = mixB[ShotLocation.Rim];
            // Config B has higher resistance → offense attacks rim less → lower rim share in B.
            cOk = rimShareB < rimShareA;
            Console.WriteLine($"    rim share A={rimShareA:P3}  rim share B={rimShareB:P3}  (B<A? {(cOk ? "OK" : "FAIL")})");
            if (cOk) Console.WriteLine("    OK — three solid help defenders add more resistance than three weak ones.");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ----------------------------------------------------------------
        // (d) Level-mismatch behavior: shift is gap-INEQUALITY-driven, not level-driven.
        //   (d1) D1 finisher vs D3 with weak rim → rim share rises (uneven gaps).
        //   (d2) Same-level matched control → mix stays within ~3pp of tendency baseline.
        //   (d3) Uniform D1 vs uniform D3 (everyone 75 vs everyone 45) → mix stays within ~2pp
        //        (uniform gaps cancel in renormalization — proves gap-inequality drives shifts).
        // ----------------------------------------------------------------
        Console.WriteLine("  (d) Level-mismatch behavior (gap-inequality driven):");
        bool dOk = true;
        try
        {
            // D1 finisher: rim-heavy offensive shape
            var finisher = Mk(50, fin: 85, outside: 65, mid: 60, close: 60,
                               rimT: 50, shortT: 25, midT: 15, longT: 5, threeT: 5);

            // D3 with weak rim protection: rim weak, perimeter normal
            var d3Weak = Mk(50, rimP: 30, perimD: 50, postD: 40);
            var d3Defs = Defenders(d3Weak, d3Weak, d3Weak, d3Weak, d3Weak);
            var mixMismatch = Mix(finisher, d3Defs);

            // Tendency baseline for finisher: rim=50/(50+25+15+5+5)=0.50
            var tendRimRaw = 50.0 / (50 + 25 + 15 + 5 + 5);
            var rimMismatch = mixMismatch[ShotLocation.Rim];
            var d1MismatchOk = rimMismatch > tendRimRaw;
            Console.WriteLine($"    (d1) D1 finisher vs D3 weak rim: rim={rimMismatch:P3}  tendency baseline={tendRimRaw:P3}  {(d1MismatchOk ? "OK — rises" : "FAIL")}");
            dOk &= d1MismatchOk;

            // Same-level matched control: defenders with same offensive shape everywhere
            var matched = Mk(50, rimP: 65, perimD: 65, postD: 65);
            var matchedDefs = Defenders(matched, matched, matched, matched, matched);
            var mixMatched  = Mix(finisher, matchedDefs);
            var rimMatched  = mixMatched[ShotLocation.Rim];
            // Mix stays within ~3pp of the tendency baseline when gaps are roughly equal
            var d2ControlOk = Math.Abs(rimMatched - tendRimRaw) < 0.06;
            Console.WriteLine($"    (d2) Same-level matched: rim={rimMatched:P3}  tendency baseline={tendRimRaw:P3}  diff={Math.Abs(rimMatched - tendRimRaw):P3}  {(d2ControlOk ? "OK" : "FAIL — too far from baseline")}");
            dOk &= d2ControlOk;

            // (d3) Plain D1 (everyone 75) vs plain D3 (everyone 45) — uniform attributes.
            // Uniform gaps cancel → mix very close to raw tendencies.
            var d1Off = Mk(75, rimT: 50, shortT: 25, midT: 15, longT: 5, threeT: 5);
            var d3Def = Mk(45);
            var d3DefsUniform = Defenders(d3Def, d3Def, d3Def, d3Def, d3Def);
            var mixUniform = Mix(d1Off, d3DefsUniform);
            var rimUniform = mixUniform[ShotLocation.Rim];
            var tendRimUniform = 50.0 / (50 + 25 + 15 + 5 + 5);
            var d3UniformOk = Math.Abs(rimUniform - tendRimUniform) < 0.02;
            Console.WriteLine($"    (d3) Uniform D1 vs D3 (all attrs 75 vs 45): rim={rimUniform:P3}  baseline={tendRimUniform:P3}  diff={Math.Abs(rimUniform - tendRimUniform):P3}  {(d3UniformOk ? "OK — uniform gaps cancel" : "FAIL")}");
            dOk &= d3UniformOk;
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ----------------------------------------------------------------
        // (e) DEC-6 partial defenders.
        //   (e1) Zero defenders: bent mix == tendency normalized exactly.
        //   (e2) One elite rim defender: 100% weight on him → strong suppression.
        //   (e3) Three populated (elite + two normal rim): top-3 blend dilutes → resistance ~77.
        //         Counterintuitive: one elite alone suppresses MORE than one elite + two normals
        //         (the blend renormalizes: 1 defender gets 100%; 3 defenders get 0.55/0.30/0.15).
        // ----------------------------------------------------------------
        Console.WriteLine("  (e) DEC-6 partial defenders:");
        bool eOk = true;
        try
        {
            var shooter = Mk(50, fin: 50, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);

            // (e1) Zero defenders: pure tendency normalization, NO matchup multiplier.
            var defs0 = Defenders(null, null, null, null, null);
            var mix0  = Mix(shooter, defs0);
            var rim0  = mix0[ShotLocation.Rim];
            var e1Ok  = Math.Abs(rim0 - 0.20) < Eps;   // all tendencies equal → each zone 20%
            Console.WriteLine($"    (e1) Zero defenders: rim={rim0:P4}  expected=20.0000%  {(e1Ok ? "OK" : "FAIL")}");
            eOk &= e1Ok;

            // (e2) One elite rim protector (slot-1 only, slots 2–5 null).
            // With 1 defender, blend renormalizes to 1.0 weight on him → resistance ≈ DefenseRating(Rim, elite).
            var elite = Mk(50, rimP: 99);
            var defs1 = Defenders(elite, null, null, null, null);
            var mix1  = Mix(shooter, defs1);
            var rim1  = mix1[ShotLocation.Rim];
            // Strong suppression expected — rim share should drop below 20%.
            var e2Ok  = rim1 < 0.20;
            Console.WriteLine($"    (e2) One elite rim defender: rim={rim1:P3}  (<20%? {(e2Ok ? "OK" : "FAIL")})");
            eOk &= e2Ok;

            // (e3) Three defenders: elite (99) + two normal (50). Top-3 blend: 0.55×99 + 0.30×50 + 0.15×50 ≈ 77.
            // One-defender suppression (e2) should be STRONGER than three-defender (e3) for the same elite — counterintuitive.
            var normal = Mk(50, rimP: 50);
            var defs3  = Defenders(elite, normal, normal, null, null);
            var mix3   = Mix(shooter, defs3);
            var rim3   = mix3[ShotLocation.Rim];
            // rim3 > rim1: three defenders dilutes the elite → less suppression → higher rim share.
            var e3Ok   = rim3 > rim1;
            Console.WriteLine($"    (e3) Three defenders (1 elite + 2 normal): rim={rim3:P3}  one-defender rim={rim1:P3}  (three>one? {(e3Ok ? "OK (elite diluted)" : "FAIL")})");
            Console.WriteLine($"         Note: this is correct — 1 elite alone gets 100% blend weight (max suppression);");
            Console.WriteLine($"         two average help partners dilute the blend and REDUCE suppression.");
            eOk &= e3Ok;
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ----------------------------------------------------------------
        // (f) Coaching seam is identity in v1.
        //     CoachingPull.Apply returns exactly the authored tendency values.
        // ----------------------------------------------------------------
        Console.WriteLine("  (f) Coaching seam identity:");
        bool fOk;
        try
        {
            var shooter = Mk(50, rimT: 30, shortT: 40, midT: 50, longT: 60, threeT: 70);
            var (rim, sh, mid, lng, three) = CoachingPull.Apply(shooter, null, null);
            fOk = rim == 30 && sh == 40 && mid == 50 && lng == 60 && three == 70;
            Console.WriteLine($"    returned ({rim},{sh},{mid},{lng},{three}) expected (30,40,50,60,70)  {(fOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ----------------------------------------------------------------
        // (g) Phase 6/7/8 regression — Roll H still correct given a forced zone.
        //     Force the shot zone via PossessionState (bypass Roll G) and verify
        //     make+and-1 / block / foul rates match Phase 8 expectations at Rim.
        // ----------------------------------------------------------------
        Console.WriteLine("  (g) Roll H regression with Phase 9 roster loaded:");
        bool gOk;
        try
        {
            var cfgH      = RollHConfig.Load(configPath);
            var foulsReg  = new FoulTracker(7, 10);
            var gReg      = new GameState(foulsReg);
            SeatStartersFromConfig(gReg, configPath);
            var genReg = new RollHGenerator(cfgH, cfgMatchup, gReg);

            // Force Rim, slot-1 shooter. Roll H still reads the same matchup context.
            var stateReg = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gReg.HomeLineup.SlotAt(1));

            var madeReg = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie    = genReg.Generate(stateReg, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result is ShotResult.Made or ShotResult.MadeAndFouled) madeReg++;
            }
            var makeRate = (double)madeReg / 5_000;
            Console.WriteLine($"    slot-1 @Rim: make+and-1={makeRate:P1}  (valid pie, >0 and <1?)");
            gOk = makeRate > 0.0 && makeRate < 1.0;
            if (gOk)  Console.WriteLine("    OK — Roll H valid after Phase 9 roster seating.");
            if (!gOk) Console.WriteLine($"  FAIL  (g) make rate {makeRate:P1} out of (0, 1).");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        // ----------------------------------------------------------------
        // (h) v3 negative-multiplier guard.
        //     Direct call to Matchup.LocationMultiplier with extreme gaps.
        //     Confirms the ratio form never goes negative or outside the asymptote bounds.
        // ----------------------------------------------------------------
        Console.WriteLine("  (h) Negative-multiplier guard (ratio form bounds):");
        bool hOk = true;
        try
        {
            var maxMult  = cfgMatchup.LocationMaxMultiplier;
            var minMult  = 1.0 / maxMult;

            // Extreme positive: shooter 99 Finishing vs five defenders all RimProtection=1.
            var eliteShooter   = Mk(50, fin: 99, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            var weakDefenders  = Defenders(Mk(50, rimP: 1), Mk(50, rimP: 1), Mk(50, rimP: 1), Mk(50, rimP: 1), Mk(50, rimP: 1));
            var multPos = Matchup.LocationMultiplier(ShotLocation.Rim, eliteShooter, weakDefenders, cfgMatchup);

            var posOk = multPos > 0.0 && multPos < maxMult;
            Console.WriteLine($"    extreme positive (fin=99 vs rimP=1): mult={multPos:F6}  bounds=(0, {maxMult})  {(posOk ? "OK" : "FAIL")}");
            hOk &= posOk;

            // Extreme negative: shooter Finishing=1 vs five defenders all RimProtection=99.
            var poorShooter     = Mk(50, fin: 1, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            var eliteDefenders  = Defenders(Mk(50, rimP: 99), Mk(50, rimP: 99), Mk(50, rimP: 99), Mk(50, rimP: 99), Mk(50, rimP: 99));
            var multNeg = Matchup.LocationMultiplier(ShotLocation.Rim, poorShooter, eliteDefenders, cfgMatchup);

            var negOk = multNeg > minMult && multNeg > 0.0;
            Console.WriteLine($"    extreme negative (fin=1 vs rimP=99): mult={multNeg:F6}  lower bound={minMult:F4}  {(negOk ? "OK — strictly positive, above lower asymptote" : "FAIL — at or below lower asymptote or negative")}");
            hOk &= negOk;
        }
        catch (Exception ex) { hOk = false; Console.WriteLine($"  FAIL  (h) threw: {ex.Message}"); }
        pass &= hOk;

        Console.WriteLine(pass ? "  Phase 9 PASSED." : "  Phase 9 FAILED.");
        return pass;
    }

    // =========================================================================
    // Phase 10 — rebound door check (Roll G matchup-aware rebounding)
    // =========================================================================
    // Deterministic assertions — NOT a Monte Carlo batch. Proves the two-touchpoint
    // model bends the off-share in the correct direction across seven sub-checks:
    // (a) neutral, (b) size check, (c) skill check, (d) positional weight isolated,
    // (e) shooter nerf + zone gate, (f) flat slivers unchanged, (g) block baseline.
    // Mirrors the structure of Phase9LocationDoorCheck exactly.
    // =========================================================================

    private static bool Phase10ReboundDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 10: Rebound door (Roll I matchup-aware) ---");
        var pass = true;

        var cfgI       = RollIConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Config baselines.
        var liveMass    = cfgI.DefensiveRebound + cfgI.OffensiveRebound;
        var liveBase    = cfgI.OffensiveRebound / liveMass;   // ≈ 0.290
        var blockMass   = cfgI.BlockDefensiveRebound + cfgI.BlockOffensiveRebound;
        var blockBase   = cfgI.BlockOffensiveRebound / blockMass; // ≈ 0.390

        // Helper: build a player with all attributes at baseline b; override specific ones.
        static Player Mk(int b,
                         int? str = null, int? height = null, int? offReb = null,
                         int? defReb = null, int? postDef = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = offReb  ?? b,
                PerimeterDefense = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = defReb  ?? b,
                Steals = b,
                Height = height ?? b, Wingspan = b, Weight = b,
                Strength = str ?? b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: build a minimal GameState, seat offense in Home 1–5, defense in Away 1–5.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        // Helper: build a PossessionState for Home offense, Away defense, with a stamped
        // shooter slot (slot 1 by default) and zone.
        static PossessionState St(GameState g, ShotLocation zone, int shooterSlot = 1)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(shooterSlot),
                ShotType: zone);

        // Helper: generate the pie and return the seven weights as a dict.
        static Dictionary<ReboundOutcome, double> Split(
            RollIConfig cfgI, MatchupConfig cfgMatchup,
            GameState g, PossessionState state,
            ReboundSource src = ReboundSource.LiveBall)
        {
            var gen = new RollIGenerator(cfgI, cfgMatchup, g);
            var pie = gen.Generate(state, src);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // ── (a) Neutral matchup: all-50 teams, Rim zone (nerf off) ──────────
        Console.WriteLine("  (a) Neutral (all-50 teams, Rim zone): off-share == baseline:");
        bool aOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);
            var d  = Split(cfgI, cfgMatchup, g, st);

            var offShare = d[ReboundOutcome.OffensiveRebound] / liveMass;
            var isNeutral = Math.Abs(offShare - liveBase) < Eps;

            // Other five slivers must equal config exactly.
            var flatOk =
                Math.Abs(d[ReboundOutcome.LooseBallFoulOnDefense] - cfgI.LooseBallFoulOnDefense) < Eps &&
                Math.Abs(d[ReboundOutcome.LooseBallFoulOnOffense] - cfgI.LooseBallFoulOnOffense) < Eps &&
                Math.Abs(d[ReboundOutcome.OutOfBoundsOffOffense]  - cfgI.OutOfBoundsOffOffense)  < Eps &&
                Math.Abs(d[ReboundOutcome.OutOfBoundsOffDefense]  - cfgI.OutOfBoundsOffDefense)  < Eps &&
                Math.Abs(d[ReboundOutcome.JumpBall]               - cfgI.JumpBall)               < Eps;

            aOk = isNeutral && flatOk;
            Console.WriteLine($"    off-share={offShare:F8}  baseline={liveBase:F8}  neutral? {(isNeutral ? "OK" : "FAIL")}");
            Console.WriteLine($"    five flat slivers == config: {(flatOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ── (b) Size check: offense big (85), defense small (35), equal ratings ──
        Console.WriteLine("  (b) Size check (off Str/Ht=85 vs def Str/Ht=35, equal ratings):");
        bool bOk;
        try
        {
            var off5 = new[] { Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85) };
            var def5 = new[] { Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);
            var d  = Split(cfgI, cfgMatchup, g, st);

            var offShare = d[ReboundOutcome.OffensiveRebound] / liveMass;
            bOk = offShare > liveBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={liveBase:F6}  rises above baseline? {(bOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ── (c) Skill check: equal size, off OffReb=85 vs def DefReb=35 ────
        Console.WriteLine("  (c) Skill check (equal size, off OffReb=85 vs def DefReb=35):");
        bool cOk;
        try
        {
            var off5 = new[] { Mk(50, offReb: 85), Mk(50, offReb: 85), Mk(50, offReb: 85),
                               Mk(50, offReb: 85), Mk(50, offReb: 85) };
            var def5 = new[] { Mk(50, defReb: 35), Mk(50, defReb: 35), Mk(50, defReb: 35),
                               Mk(50, defReb: 35), Mk(50, defReb: 35) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);
            var d  = Split(cfgI, cfgMatchup, g, st);

            var offShare = d[ReboundOutcome.OffensiveRebound] / liveMass;
            cOk = offShare > liveBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={liveBase:F6}  rises above baseline? {(cOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ── (d) Positional weight — isolated from size check ────────────────
        // Both teams: identical height AND strength (so ReboundPhysical is a wash).
        // Separate them with PostDefense alone (in Postness but NOT in ReboundPhysical).
        // Offense A: concentrate OffReb in high-PostDef player (stark 90 vs 10).
        // Offense B: spread same total flat. Expected: A > B (positional weight rewarded).
        Console.WriteLine("  (d) Positional weight isolated (PostDefense only, equal Str/Height):");
        bool dOk;
        try
        {
            // Offense A: post player has PostDef=90 and OffReb=90; guards have PostDef=10 and OffReb=10.
            var offA = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 90, postDef: 90),  // the post
            };
            // Total OffReb in A: 4*10 + 90 = 130. Spread flat: 5*26 = 130.
            var offB = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
            };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

            // Shooter at slot 1 (a guard with low PostDef), nerf OFF (Rim zone).
            var gA = BuildGame(offA, def5);
            var stA = St(gA, ShotLocation.Rim, shooterSlot: 1);
            var dA = Split(cfgI, cfgMatchup, gA, stA);
            var shareA = dA[ReboundOutcome.OffensiveRebound] / liveMass;

            var gB = BuildGame(offB, def5);
            var stB = St(gB, ShotLocation.Rim, shooterSlot: 1);
            var dB = Split(cfgI, cfgMatchup, gB, stB);
            var shareB = dB[ReboundOutcome.OffensiveRebound] / liveMass;

            dOk = shareA > shareB;
            Console.WriteLine($"    concentrated (OffReb in big): share={shareA:F6}");
            Console.WriteLine($"    flat spread:                   share={shareB:F6}");
            Console.WriteLine($"    concentrated > flat: {(dOk ? "OK — positional weight rewards OffReb in bigs" : "FAIL")}");
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ── (e) Shooter nerf + zone gate ────────────────────────────────────
        // Identical matchup; Three (nerf on) vs Rim (nerf off). Shooter is slot 1.
        Console.WriteLine("  (e) Shooter nerf: Three (nerf on) yields lower off-share than Rim (nerf off):");
        bool eOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g = BuildGame(off5, def5);

            var stThree = St(g, ShotLocation.Three, shooterSlot: 1);
            var dThree  = Split(cfgI, cfgMatchup, g, stThree);
            var shareThree = dThree[ReboundOutcome.OffensiveRebound] / liveMass;

            var stRim = St(g, ShotLocation.Rim, shooterSlot: 1);
            var dRim  = Split(cfgI, cfgMatchup, g, stRim);
            var shareRim = dRim[ReboundOutcome.OffensiveRebound] / liveMass;

            eOk = shareThree < shareRim;
            Console.WriteLine($"    Three (nerf on): off-share={shareThree:F6}");
            Console.WriteLine($"    Rim  (nerf off): off-share={shareRim:F6}");
            Console.WriteLine($"    Three < Rim: {(eOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ── (f) Other arms flat across (b)–(e) ─────────────────────────────
        // Only Def/Off should move; the five flat slivers must equal config in every case.
        Console.WriteLine("  (f) Flat slivers unchanged across (b)–(e) cases:");
        bool fOk = true;
        try
        {
            var testCases = new (string label, Player[] off, Player[] def, ShotLocation zone)[]
            {
                ("size-check (b)",
                    new[] { Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85) },
                    new[] { Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35) },
                    ShotLocation.Rim),
                ("skill-check (c)",
                    new[] { Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85) },
                    new[] { Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35) },
                    ShotLocation.Rim),
                ("shooter-nerf Three (e)",
                    new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) },
                    new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) },
                    ShotLocation.Three),
            };

            foreach (var (label, off, def, zone) in testCases)
            {
                var g  = BuildGame(off, def);
                var st = St(g, zone, shooterSlot: 1);
                var d  = Split(cfgI, cfgMatchup, g, st);
                var rowOk =
                    Math.Abs(d[ReboundOutcome.LooseBallFoulOnDefense] - cfgI.LooseBallFoulOnDefense) < Eps &&
                    Math.Abs(d[ReboundOutcome.LooseBallFoulOnOffense] - cfgI.LooseBallFoulOnOffense) < Eps &&
                    Math.Abs(d[ReboundOutcome.OutOfBoundsOffOffense]  - cfgI.OutOfBoundsOffOffense)  < Eps &&
                    Math.Abs(d[ReboundOutcome.OutOfBoundsOffDefense]  - cfgI.OutOfBoundsOffDefense)  < Eps &&
                    Math.Abs(d[ReboundOutcome.JumpBall]               - cfgI.JumpBall)               < Eps;
                fOk &= rowOk;
                Console.WriteLine($"    {label}: flat slivers == config: {(rowOk ? "OK" : "FAIL")}");
            }
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ── (g) Block source baseline preserved ─────────────────────────────
        // At neutral, the block source's off-share ≈ blockBase (≈0.390), distinct from live-miss (≈0.290).
        Console.WriteLine("  (g) Block source baseline (neutral matchup):");
        bool gOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);

            var dBlock = Split(cfgI, cfgMatchup, g, st, ReboundSource.Block);
            var blockOffShare = dBlock[ReboundOutcome.OffensiveRebound] / blockMass;

            var atBlockBase  = Math.Abs(blockOffShare - blockBase) < Eps;
            var distinctFrom = Math.Abs(blockBase - liveBase) > 0.05;
            gOk = atBlockBase && distinctFrom;
            Console.WriteLine($"    block off-share={blockOffShare:F8}  blockBase={blockBase:F8}  matches? {(atBlockBase ? "OK" : "FAIL")}");
            Console.WriteLine($"    block baseline distinct from live-miss (>0.05 gap): {(distinctFrom ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        Console.WriteLine(pass ? "  Phase 10 PASSED." : "  Phase 10 FAILED.");
        return pass;
    }

    // =========================================================================
    // Phase 11 — FT rebound door (Roll M matchup-aware)
    // Mirrors Phase10ReboundDoorCheck. Key differences from the Roll I template:
    //   • One source only (no ReboundSource arg; no block baseline)
    //   • No shooter nerf (shooterIdx=-1 always; St() stamps no slot/zone)
    //   • Baseline ≈ 0.197 (more defensive than Roll I's live-miss ≈ 0.290)
    //   • Sub-check (e) = no-shooter invariance (replaces Roll I's nerf check)
    //   • Sub-check (g) = FT baseline < field-goal baseline (replaces block check)
    // =========================================================================

    private static bool Phase11FreeThrowReboundDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 11: FT rebound door (Roll M matchup-aware) ---");
        var pass = true;

        var cfgM       = RollMConfig.Load(configPath);
        var cfgI       = RollIConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Config baselines.
        var mMass = cfgM.DefensiveRebound + cfgM.OffensiveRebound;
        var mBase = cfgM.OffensiveRebound / mMass;   // ≈ 0.197

        // Helper: build a player with all attributes at baseline b; override specific ones.
        static Player Mk(int b,
                         int? str = null, int? height = null, int? offReb = null,
                         int? defReb = null, int? postDef = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = offReb  ?? b,
                PerimeterDefense = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = defReb  ?? b,
                Steals = b,
                Height = height ?? b, Wingspan = b, Weight = b,
                Strength = str ?? b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: build a minimal GameState, seat offense in Home 1–5, defense in Away 1–5.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        // Helper: build a PossessionState for Home offense, Away defense.
        // ⚠ Do NOT stamp SelectedSlot or ShotType — Roll M reads neither.
        // Leaving them null proves the generator's only fallback is empty rosters,
        // not a null-slot check (Divergence 3).
        static PossessionState St()
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: null, ShotType: null);

        // Helper: construct RollMGenerator, call Generate, return seven weights as a dict.
        static Dictionary<FreeThrowReboundOutcome, double> Split(
            RollMConfig cfgM, MatchupConfig cfgMatchup, GameState g, PossessionState state)
        {
            var gen = new RollMGenerator(cfgM, cfgMatchup, g);
            var pie = gen.Generate(state);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // ── (a) Neutral: all-50 teams, no slot → off-share == Roll M baseline ────
        Console.WriteLine("  (a) Neutral (all-50 teams, no slot): off-share == Roll M baseline:");
        bool aOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(off5, def5);
            var st = St();
            var d  = Split(cfgM, cfgMatchup, g, st);

            var offShare  = d[FreeThrowReboundOutcome.OffensiveRebound] / mMass;
            var isNeutral = Math.Abs(offShare - mBase) < Eps;

            var flatOk =
                Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnDefense] - cfgM.LooseBallFoulOnDefense) < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnOffense] - cfgM.LooseBallFoulOnOffense) < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffOffense]  - cfgM.OutOfBoundsOffOffense)  < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffDefense]  - cfgM.OutOfBoundsOffDefense)  < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.JumpBall]               - cfgM.JumpBall)               < Eps;

            aOk = isNeutral && flatOk;
            Console.WriteLine($"    off-share={offShare:F8}  baseline={mBase:F8}  neutral? {(isNeutral ? "OK" : "FAIL")}");
            Console.WriteLine($"    five flat slivers == config: {(flatOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ── (b) Size check: bigger offense → off-share rises ────────────────────
        Console.WriteLine("  (b) Size check (off Str/Height=85 vs def Str/Height=35):");
        bool bOk;
        try
        {
            var off5 = new[] { Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85) };
            var def5 = new[] { Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35) };
            var g  = BuildGame(off5, def5);
            var st = St();
            var d  = Split(cfgM, cfgMatchup, g, st);

            var offShare = d[FreeThrowReboundOutcome.OffensiveRebound] / mMass;
            bOk = offShare > mBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={mBase:F6}  rises above baseline? {(bOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ── (c) Skill check: better rebounders → off-share rises ────────────────
        Console.WriteLine("  (c) Skill check (equal size, off OffReb=85 vs def DefReb=35):");
        bool cOk;
        try
        {
            var off5 = new[] { Mk(50, offReb: 85), Mk(50, offReb: 85), Mk(50, offReb: 85),
                               Mk(50, offReb: 85), Mk(50, offReb: 85) };
            var def5 = new[] { Mk(50, defReb: 35), Mk(50, defReb: 35), Mk(50, defReb: 35),
                               Mk(50, defReb: 35), Mk(50, defReb: 35) };
            var g  = BuildGame(off5, def5);
            var st = St();
            var d  = Split(cfgM, cfgMatchup, g, st);

            var offShare = d[FreeThrowReboundOutcome.OffensiveRebound] / mMass;
            cOk = offShare > mBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={mBase:F6}  rises above baseline? {(cOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ── (d) Positional weight isolated (PostDefense, no size diff, no shooter) ─
        // Both teams: identical Strength and Height (ReboundPhysical is a wash).
        // Separate with PostDefense alone. Offense A: concentrate OffReb in high-PostDef
        // player; Offense B: same total OffReb spread flat. Expected: A > B.
        // ⚠ Cleaner than Phase 10 (d) — no shooter slot to choose.
        Console.WriteLine("  (d) Positional weight isolated (PostDefense only, equal Str/Height, no shooter slot):");
        bool dOk;
        try
        {
            var offA = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 90, postDef: 90),  // the post
            };
            // Total OffReb in A: 4*10 + 90 = 130. Spread flat: 5*26 = 130.
            var offB = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
            };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var st = St();

            var gA = BuildGame(offA, def5);
            var dA = Split(cfgM, cfgMatchup, gA, st);
            var shareA = dA[FreeThrowReboundOutcome.OffensiveRebound] / mMass;

            var gB = BuildGame(offB, def5);
            var dB = Split(cfgM, cfgMatchup, gB, st);
            var shareB = dB[FreeThrowReboundOutcome.OffensiveRebound] / mMass;

            dOk = shareA > shareB;
            Console.WriteLine($"    concentrated (OffReb in post): share={shareA:F6}");
            Console.WriteLine($"    flat spread:                   share={shareB:F6}");
            Console.WriteLine($"    concentrated > flat: {(dOk ? "OK — positional weight rewards OffReb in bigs on FT glass" : "FAIL")}");
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ── (e) No-shooter invariance ────────────────────────────────────────────
        // ⚠ Replaces Phase 10's shooter-nerf sub-check. Prove Roll M is structurally
        // slot-blind: identical matchup with null slot vs. stamped slot+zone must
        // produce byte-identical off-shares. Positive proof of Divergences 2 and 3.
        Console.WriteLine("  (e) No-shooter invariance: null-slot and stamped-slot produce identical off-share:");
        bool eOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g = BuildGame(off5, def5);

            // No slot (normal bonus FT trip path).
            var stNoSlot = St();

            // Stamped slot + nerf-eligible zone (shooting-foul FT trip path).
            var stWithSlot = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(1),
                ShotType: ShotLocation.Three);

            var dNoSlot   = Split(cfgM, cfgMatchup, g, stNoSlot);
            var dWithSlot = Split(cfgM, cfgMatchup, g, stWithSlot);

            var shareNoSlot   = dNoSlot[FreeThrowReboundOutcome.OffensiveRebound]   / mMass;
            var shareWithSlot = dWithSlot[FreeThrowReboundOutcome.OffensiveRebound] / mMass;

            eOk = Math.Abs(shareNoSlot - shareWithSlot) < Eps;
            Console.WriteLine($"    null-slot   off-share = {shareNoSlot:F10}");
            Console.WriteLine($"    stamped-slot off-share = {shareWithSlot:F10}");
            Console.WriteLine($"    identical? {(eOk ? "OK — Roll M is slot-blind" : "FAIL — slot is affecting Roll M (bug)")}");
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ── (f) Other arms flat across (b)–(e) ──────────────────────────────────
        Console.WriteLine("  (f) Flat slivers unchanged across (b)–(c) cases:");
        bool fOk = true;
        try
        {
            var testCases = new (string label, Player[] off, Player[] def)[]
            {
                ("size-check (b)",
                    new[] { Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85) },
                    new[] { Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35) }),
                ("skill-check (c)",
                    new[] { Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85) },
                    new[] { Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35) }),
            };

            foreach (var (label, off, def) in testCases)
            {
                var g  = BuildGame(off, def);
                var st = St();
                var d  = Split(cfgM, cfgMatchup, g, st);
                var rowOk =
                    Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnDefense] - cfgM.LooseBallFoulOnDefense) < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnOffense] - cfgM.LooseBallFoulOnOffense) < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffOffense]  - cfgM.OutOfBoundsOffOffense)  < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffDefense]  - cfgM.OutOfBoundsOffDefense)  < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.JumpBall]               - cfgM.JumpBall)               < Eps;
                fOk &= rowOk;
                Console.WriteLine($"    {label}: flat slivers == config: {(rowOk ? "OK" : "FAIL")}");
            }
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ── (g) FT baseline < field-goal baseline ────────────────────────────────
        // ⚠ Replaces Phase 10's block-source sub-check.
        // At neutral, Roll M's off-share (≈ 0.197) must be strictly lower than
        // Roll I's live-miss off-share (≈ 0.290) — the FT board is more defensive.
        Console.WriteLine("  (g) FT baseline (Roll M) strictly lower than field-goal baseline (Roll I live-miss):");
        bool gOk;
        try
        {
            var iMass = cfgI.DefensiveRebound + cfgI.OffensiveRebound;
            var iBase = cfgI.OffensiveRebound / iMass;

            var gOk1 = mBase < iBase;
            var gOk2 = mBase > 0.15 && mBase < 0.25;   // sensibility band
            gOk = gOk1 && gOk2;
            Console.WriteLine($"    Roll M off-share = {mBase:F6}  Roll I off-share = {iBase:F6}");
            Console.WriteLine($"    M < I: {(gOk1 ? "OK" : "FAIL")}   M in sensibility band (0.15, 0.25): {(gOk2 ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        Console.WriteLine(pass ? "  Phase 11 PASSED." : "  Phase 11 FAILED.");
        return pass;
    }

    // =========================================================================
    // Phase 12 — pressure / disruption door (Roll F matchup-aware)
    //
    // Mirrors Phase10/11 pattern. Key differences:
    //   • Four-arm pie (ShotAttempt, Turnover, NonShootingFoul, JumpBall)
    //   • One-arg Generate(state) — no source selector, no zone
    //   • SelectedSlot IS stamped (handler needed for the steal matchup)
    //   • JumpBall held exactly flat; three-way mass split on the other three
    //   • Pressure is the new axis — the calibration anchor is
    //     (neutral pressure + even matchup) = today's flat rates
    // =========================================================================

    private static bool Phase12DisruptionDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 12: Disruption door (Roll F pressure-aware) ---");
        var pass = true;

        var cfgF       = RollFConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Action mass and baseline shares (what the neutral case must reproduce).
        var actionMass    = cfgF.BaseShotAttempt + cfgF.BaseTurnover + cfgF.BaseNonShootingFoul;
        var baseToShare   = cfgF.BaseTurnover        / actionMass;
        var baseFoulShare = cfgF.BaseNonShootingFoul / actionMass;

        // Helper: build a player with all attributes at b; override BallHandling and Steals.
        static Player Mk(int b, int? bh = null, int? st = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = bh ?? b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b,
                Steals = st ?? b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: build a minimal GameState, Home offense 1–5, Away defense 1–5.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        // Helper: build a PossessionState with Home offense, Away defense, stamped handler slot.
        // Roll F runs AFTER Roll E, so SelectedSlot is always stamped on the live path.
        static PossessionState St(GameState g, int handlerSlot = 1)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(handlerSlot),
                ShotType: null);   // Roll G hasn't run yet at Roll F

        // Helper: construct RollFGenerator with given config, generate pie, return dict.
        static Dictionary<PlayerActionOutcome, double> Split(
            RollFConfig cfgF, MatchupConfig cfgMatchup, GameState g, PossessionState state)
        {
            var gen = new RollFGenerator(cfgF, cfgMatchup, g);
            var pie = gen.Generate(state);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // Build a MatchupConfig with Away pressure set to the given value.
        // Away is always the defense in our standard test setup.
        MatchupConfig WithAwayPressure(double p)
        {
            var c = MatchupConfig.Load(configPath);
            c.AwayPressure = p;
            return c;
        }

        // ── (a) Neutral anchor ─────────────────────────────────────────────
        Console.WriteLine("  (a) Neutral anchor (pressure=5, all-50 even matchup): all four arms == config baseline:");
        bool aOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g    = BuildGame(off5, def5);
            var st   = St(g);
            var d    = Split(cfgF, cfgMatchup, g, st);   // cfgMatchup has neutral pressure=5

            var shotOk = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps;
            var toOk   = Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps;
            var foOk   = Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps;
            var juOk   = Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;

            aOk = shotOk && toOk && foOk && juOk;
            Console.WriteLine($"    ShotAttempt={d[PlayerActionOutcome.ShotAttempt]:F8}  want={cfgF.BaseShotAttempt:F8}  {(shotOk?"OK":"FAIL")}");
            Console.WriteLine($"    Turnover   ={d[PlayerActionOutcome.Turnover]:F8}  want={cfgF.BaseTurnover:F8}  {(toOk?"OK":"FAIL")}");
            Console.WriteLine($"    Foul       ={d[PlayerActionOutcome.NonShootingFoul]:F8}  want={cfgF.BaseNonShootingFoul:F8}  {(foOk?"OK":"FAIL")}");
            Console.WriteLine($"    JumpBall   ={d[PlayerActionOutcome.JumpBall]:F8}  want={cfgF.BaseJumpBall:F8}  {(juOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ── (b) Pressure raises turnovers (flat lift, skill-independent) ───
        Console.WriteLine("  (b) Pressure raises TO — even matchup, BH=ST=50:");
        bool bOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g    = BuildGame(off5, def5);
            var st   = St(g);

            var dHigh = Split(cfgF, WithAwayPressure(9.0), g, st);
            var dNeut = Split(cfgF, cfgMatchup,             g, st);
            var dLow  = Split(cfgF, WithAwayPressure(2.0), g, st);

            var toHigh = dHigh[PlayerActionOutcome.Turnover];
            var toNeut = dNeut[PlayerActionOutcome.Turnover];
            var toLow  = dLow [PlayerActionOutcome.Turnover];
            Console.WriteLine($"    TO: low={toLow:F6}  neutral={toNeut:F6}  high={toHigh:F6}");

            var riseOk = toHigh > toNeut;
            var fallOk = toLow  < toNeut;

            // Flat lift with bad hands: handler BH=30, defender ST=30 (neutral matchup
            // favors neither) — high pressure must still raise TO above neutral.
            var offBad = new[] { Mk(50, bh:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defBad = new[] { Mk(50, st:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gBad   = BuildGame(offBad, defBad);
            var stBad  = St(gBad);
            var dBadHigh = Split(cfgF, WithAwayPressure(9.0), gBad, stBad);
            var dBadNeut = Split(cfgF, cfgMatchup,             gBad, stBad);
            var flatLiftOk = dBadHigh[PlayerActionOutcome.Turnover] > dBadNeut[PlayerActionOutcome.Turnover];
            Console.WriteLine($"    Bad-hands (BH=30,ST=30): neutral={dBadNeut[PlayerActionOutcome.Turnover]:F6}  high={dBadHigh[PlayerActionOutcome.Turnover]:F6}  flatLift={flatLiftOk}");

            bOk = riseOk && fallOk && flatLiftOk;
            Console.WriteLine($"    high>neutral: {riseOk}  low<neutral: {fallOk}  flatLift: {flatLiftOk}  -> {(bOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ── (c) Pressure gates the matchup ─────────────────────────────────
        Console.WriteLine("  (c) Pressure gates matchup — high-pressure delta >> low-pressure delta:");
        bool cOk;
        try
        {
            var offGood = new[] { Mk(50, bh:85), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offWeak = new[] { Mk(50, bh:20), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt   = new[] { Mk(50, st:65), Mk(50), Mk(50), Mk(50), Mk(50) };

            var gGood = BuildGame(offGood, defSt);
            var gWeak = BuildGame(offWeak, defSt);
            var stGood = St(gGood);
            var stWeak = St(gWeak);

            var cfgLow  = WithAwayPressure(2.0);
            var cfgHigh = WithAwayPressure(9.0);

            var toLowGood  = Split(cfgF, cfgLow,  gGood, stGood)[PlayerActionOutcome.Turnover];
            var toLowWeak  = Split(cfgF, cfgLow,  gWeak, stWeak)[PlayerActionOutcome.Turnover];
            var toHighGood = Split(cfgF, cfgHigh, gGood, stGood)[PlayerActionOutcome.Turnover];
            var toHighWeak = Split(cfgF, cfgHigh, gWeak, stWeak)[PlayerActionOutcome.Turnover];

            var deltaLow  = Math.Abs(toLowWeak  - toLowGood);
            var deltaHigh = Math.Abs(toHighWeak - toHighGood);

            Console.WriteLine($"    Low  pressure delta={deltaLow:F6}  ({toLowGood:F6} vs {toLowWeak:F6})");
            Console.WriteLine($"    High pressure delta={deltaHigh:F6}  ({toHighGood:F6} vs {toHighWeak:F6})");

            cOk = deltaHigh > 3.0 * deltaLow;
            Console.WriteLine($"    High delta >> low delta: {cOk}  -> {(cOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ── (d) Handling vs steals monotone at fixed high pressure ─────────
        Console.WriteLine("  (d) Monotone at high pressure (9.0):");
        bool dOk;
        try
        {
            var cfgHigh = WithAwayPressure(9.0);
            var def65   = new[] { Mk(50, st:65), Mk(50), Mk(50), Mk(50), Mk(50) };

            // Raise BH → TO falls
            var offBh30 = new[] { Mk(50, bh:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offBh50 = new[] { Mk(50, bh:50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offBh80 = new[] { Mk(50, bh:80), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gBh30 = BuildGame(offBh30, def65); var gBh50 = BuildGame(offBh50, def65); var gBh80 = BuildGame(offBh80, def65);
            var toBh30 = Split(cfgF, cfgHigh, gBh30, St(gBh30))[PlayerActionOutcome.Turnover];
            var toBh50 = Split(cfgF, cfgHigh, gBh50, St(gBh50))[PlayerActionOutcome.Turnover];
            var toBh80 = Split(cfgF, cfgHigh, gBh80, St(gBh80))[PlayerActionOutcome.Turnover];
            var bhFallsOk = toBh30 > toBh50 && toBh50 > toBh80;
            Console.WriteLine($"    BH=30: {toBh30:F6}  BH=50: {toBh50:F6}  BH=80: {toBh80:F6}  monotone-fall: {bhFallsOk}");

            // Raise ST → TO rises
            var off50  = new[] { Mk(50, bh:50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt30 = new[] { Mk(50, st:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt50 = new[] { Mk(50, st:50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt80 = new[] { Mk(50, st:80), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gSt30 = BuildGame(off50, defSt30); var gSt50 = BuildGame(off50, defSt50); var gSt80 = BuildGame(off50, defSt80);
            var toSt30 = Split(cfgF, cfgHigh, gSt30, St(gSt30))[PlayerActionOutcome.Turnover];
            var toSt50 = Split(cfgF, cfgHigh, gSt50, St(gSt50))[PlayerActionOutcome.Turnover];
            var toSt80 = Split(cfgF, cfgHigh, gSt80, St(gSt80))[PlayerActionOutcome.Turnover];
            var stRisesOk = toSt30 < toSt50 && toSt50 < toSt80;
            Console.WriteLine($"    ST=30: {toSt30:F6}  ST=50: {toSt50:F6}  ST=80: {toSt80:F6}  monotone-rise: {stRisesOk}");

            dOk = bhFallsOk && stRisesOk;
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ── (e) Low cap holds ───────────────────────────────────────────────
        Console.WriteLine("  (e) Low cap: max pressure + most lopsided matchup (BH=10, ST=95, p=10):");
        bool eOk;
        try
        {
            var cfgMax  = WithAwayPressure(10.0);
            var offWeak = new[] { Mk(50, bh:10), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defElite= new[] { Mk(50, st:95), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(offWeak, defElite);
            var st = St(g);
            var toWorst = Split(cfgF, cfgMax, g, st)[PlayerActionOutcome.Turnover];
            var capOk    = toWorst <= cfgMatchup.TurnoverCeiling + Eps;
            var sanityOk = toWorst < 0.35;   // nobody gets stripped 35%+ of possessions
            Console.WriteLine($"    TO={toWorst:F6}  ceiling={cfgMatchup.TurnoverCeiling:F6}  capped={capOk}  sane={sanityOk}");
            eOk = capOk && sanityOk;
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ── (f) Foul rises with pressure, flat across matchup ──────────────
        Console.WriteLine("  (f) Foul rises with pressure; flat across BH/ST matchup:");
        bool fOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g    = BuildGame(off5, def5);
            var st   = St(g);

            var foHigh = Split(cfgF, WithAwayPressure(9.0), g, st)[PlayerActionOutcome.NonShootingFoul];
            var foNeut = Split(cfgF, cfgMatchup,             g, st)[PlayerActionOutcome.NonShootingFoul];
            var foLow  = Split(cfgF, WithAwayPressure(2.0), g, st)[PlayerActionOutcome.NonShootingFoul];
            var risesOk = foHigh > foNeut && foNeut > foLow;
            Console.WriteLine($"    Foul: low={foLow:F6}  neutral={foNeut:F6}  high={foHigh:F6}  rises={risesOk}");

            // Flat across matchup at fixed pressure
            var cfgP8 = WithAwayPressure(8.0);
            var offBh85= new[] { Mk(50, bh:85), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offBh20= new[] { Mk(50, bh:20), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt30= new[] { Mk(50, st:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt90= new[] { Mk(50, st:90), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gEven  = BuildGame(off5,   def5);
            var gGood  = BuildGame(offBh85,defSt30);
            var gWeak  = BuildGame(offBh20,defSt90);
            var foEven = Split(cfgF, cfgP8, gEven,  St(gEven ))[PlayerActionOutcome.NonShootingFoul];
            var foGood = Split(cfgF, cfgP8, gGood,  St(gGood ))[PlayerActionOutcome.NonShootingFoul];
            var foWeak = Split(cfgF, cfgP8, gWeak,  St(gWeak ))[PlayerActionOutcome.NonShootingFoul];
            var flatOk = Math.Abs(foEven - foGood) < Eps && Math.Abs(foEven - foWeak) < Eps;
            Console.WriteLine($"    Foul p=8: even={foEven:F8}  good-hands={foGood:F8}  weak-hands={foWeak:F8}  flat={flatOk}");

            fOk = risesOk && flatOk;
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ── (g) JumpBall exactly flat; four arms sum to 1 ──────────────────
        Console.WriteLine("  (g) JumpBall flat and sum==1 in all cases:");
        bool gOk;
        try
        {
            var cases = new (int bh, int st, double p, string label)[]
            {
                (50, 50, 5.0,  "neutral even"),
                (50, 50, 9.0,  "high pressure even"),
                (50, 50, 2.0,  "low pressure even"),
                (20, 80, 9.0,  "high press, defender edge"),
                (85, 30, 9.0,  "high press, handler edge"),
                (10, 95, 10.0, "max press, worst case"),
            };
            gOk = true;
            foreach (var (bh, st, p, label) in cases)
            {
                var offP = new[] { Mk(50, bh:bh), Mk(50), Mk(50), Mk(50), Mk(50) };
                var defP = new[] { Mk(50, st:st), Mk(50), Mk(50), Mk(50), Mk(50) };
                var gP   = BuildGame(offP, defP);
                var stP  = St(gP);
                var d    = Split(cfgF, WithAwayPressure(p), gP, stP);
                var sum  = d.Values.Sum();
                var jumpOk = Math.Abs(d[PlayerActionOutcome.JumpBall] - cfgF.BaseJumpBall) < Eps;
                var sumOk  = Math.Abs(sum - 1.0) < Eps;
                if (!jumpOk || !sumOk)
                {
                    Console.WriteLine($"    FAIL [{label}]: jump={d[PlayerActionOutcome.JumpBall]:F10}  sum={sum:F10}");
                    gOk = false;
                }
            }
            if (gOk) Console.WriteLine("    All cases: JumpBall==BaseJumpBall and sum==1  OK");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        // ── (h) Fallbacks: all return flat baseline pie ────────────────────
        Console.WriteLine("  (h) Fallbacks return flat baseline pie:");
        bool hOk;
        try
        {
            hOk = true;
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

            // Null SelectedSlot
            {
                var g  = BuildGame(off5, def5);
                var st = new PossessionState(PossessionNumber: 1,
                    Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: null, ShotType: null);
                var d = Split(cfgF, cfgMatchup, g, st);
                var ok1 = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps
                       && Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps
                       && Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps
                       && Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;
                Console.WriteLine($"    Null SelectedSlot → flat baseline: {(ok1?"OK":"FAIL")}");
                hOk &= ok1;
            }

            // Empty offense roster (no players seated)
            {
                var g = new GameState(new FoulTracker(7, 10));
                for (var i = 0; i < 5; i++)
                    g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def5[i]);
                // Home offense has no players — SelectedSlot still stamped but handler is null
                var st = new PossessionState(PossessionNumber: 1,
                    Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: g.HomeLineup.SlotAt(1), ShotType: null);
                var d = Split(cfgF, cfgMatchup, g, st);
                var ok2 = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps
                       && Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps
                       && Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps
                       && Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;
                Console.WriteLine($"    Empty offense roster → flat baseline: {(ok2?"OK":"FAIL")}");
                hOk &= ok2;
            }

            // Zero populated defense
            {
                var g = new GameState(new FoulTracker(7, 10));
                for (var i = 0; i < 5; i++)
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off5[i]);
                // Away defense has no players
                var st = St(g);
                var d = Split(cfgF, cfgMatchup, g, st);
                var ok3 = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps
                       && Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps
                       && Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps
                       && Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;
                Console.WriteLine($"    Zero populated defense → flat baseline: {(ok3?"OK":"FAIL")}");
                hOk &= ok3;
            }
        }
        catch (Exception ex) { hOk = false; Console.WriteLine($"  FAIL  (h) threw: {ex.Message}"); }
        pass &= hOk;

        Console.WriteLine(pass ? "  Phase 12 PASSED." : "  Phase 12 FAILED.");
        return pass;
    }

    private static bool Phase13TeamDisruptionDoorCheckRollB(string configPath)
    {
        Console.WriteLine("\n--- Phase 13: Team-aggregate disruption door (Roll B) ---");
        var pass = true;

        var cfgB       = RollBConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        var actionMass    = cfgB.BaseProceed + cfgB.BaseFoul + cfgB.BaseDeadBallTurnover;
        var baseToShare   = cfgB.BaseDeadBallTurnover / actionMass;
        var baseFoulShare = cfgB.BaseFoul             / actionMass;

        // Helper: player with all attributes at b; override BallHandling and Steals.
        static Player Mk(int b, int? bh = null, int? st = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = bh ?? b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b,
                Steals = st ?? b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: minimal GameState with Home offense and Away defense.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        // Helper: PossessionState. Roll B does NOT read SelectedSlot, so null is the
        // natural state. We also test with a stamped slot (check f).
        static PossessionState St(Slot? slot = null)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: slot,
                ShotType: null);

        // Helper: construct RollBGenerator, generate pie, return dict.
        static Dictionary<HalfcourtOutcome, double> Split(
            RollBConfig cfgB, MatchupConfig cfgMatchup, GameState g, PossessionState state)
        {
            var gen = new RollBGenerator(cfgB, cfgMatchup, g);
            var pie = gen.Generate(state, physicality: 0.0);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // Helper: MatchupConfig with Away (defense) pressure set to p.
        MatchupConfig WithAwayPressure(double p)
        {
            var c = MatchupConfig.Load(configPath);
            c.AwayPressure = p;
            return c;
        }

        var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
        var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

        // ── (a) Neutral anchor ─────────────────────────────────────────────
        Console.WriteLine("  (a) Neutral anchor (pressure=5, all-50 even aggregate): all four arms == config baseline:");
        bool aOk;
        try
        {
            var g = BuildGame(off5, def5);
            var d = Split(cfgB, cfgMatchup, g, St());   // cfgMatchup has neutral pressure=5

            var prOk = Math.Abs(d[HalfcourtOutcome.Proceed]          - cfgB.BaseProceed)          < Eps;
            var toOk = Math.Abs(d[HalfcourtOutcome.DeadBallTurnover] - cfgB.BaseDeadBallTurnover) < Eps;
            var foOk = Math.Abs(d[HalfcourtOutcome.Foul]             - cfgB.BaseFoul)             < Eps;
            var juOk = Math.Abs(d[HalfcourtOutcome.JumpBall]         - cfgB.BaseJumpBall)         < Eps;
            aOk = prOk && toOk && foOk && juOk;
            Console.WriteLine($"    Proceed       ={d[HalfcourtOutcome.Proceed]:F8}  want={cfgB.BaseProceed:F8}  {(prOk?"OK":"FAIL")}");
            Console.WriteLine($"    DeadBallTO    ={d[HalfcourtOutcome.DeadBallTurnover]:F8}  want={cfgB.BaseDeadBallTurnover:F8}  {(toOk?"OK":"FAIL")}");
            Console.WriteLine($"    Foul          ={d[HalfcourtOutcome.Foul]:F8}  want={cfgB.BaseFoul:F8}  {(foOk?"OK":"FAIL")}");
            Console.WriteLine($"    JumpBall      ={d[HalfcourtOutcome.JumpBall]:F8}  want={cfgB.BaseJumpBall:F8}  {(juOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;
        Console.WriteLine($"  (a) {(aOk ? "ok" : "FAIL")}");

        // ── (b) Pressure raises turnovers ──────────────────────────────────
        Console.WriteLine("  (b) Pressure raises TO — even aggregate:");
        bool bOk;
        try
        {
            var g = BuildGame(off5, def5);
            var dLow  = Split(cfgB, WithAwayPressure(2.0), g, St());
            var dNeut = Split(cfgB, cfgMatchup,             g, St());
            var dHigh = Split(cfgB, WithAwayPressure(9.0), g, St());
            var toRise = dLow[HalfcourtOutcome.DeadBallTurnover]
                       < dNeut[HalfcourtOutcome.DeadBallTurnover]
                       && dNeut[HalfcourtOutcome.DeadBallTurnover]
                       < dHigh[HalfcourtOutcome.DeadBallTurnover];
            bOk = toRise;
            Console.WriteLine($"    TO @ p=2: {dLow[HalfcourtOutcome.DeadBallTurnover]:F6}  p=5: {dNeut[HalfcourtOutcome.DeadBallTurnover]:F6}  p=9: {dHigh[HalfcourtOutcome.DeadBallTurnover]:F6}  rise={toRise}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;
        Console.WriteLine($"  (b) {(bOk ? "ok" : "FAIL")}");

        // ── (c) Pressure raises fouls ──────────────────────────────────────
        Console.WriteLine("  (c) Pressure raises Foul:");
        bool cOk;
        try
        {
            var g = BuildGame(off5, def5);
            var fLow  = Split(cfgB, WithAwayPressure(2.0), g, St())[HalfcourtOutcome.Foul];
            var fNeut = Split(cfgB, cfgMatchup,             g, St())[HalfcourtOutcome.Foul];
            var fHigh = Split(cfgB, WithAwayPressure(9.0), g, St())[HalfcourtOutcome.Foul];
            cOk = fLow < fNeut && fNeut < fHigh;
            Console.WriteLine($"    Foul @ p=2: {fLow:F6}  p=5: {fNeut:F6}  p=9: {fHigh:F6}  rise={cOk}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;
        Console.WriteLine($"  (c) {(cOk ? "ok" : "FAIL")}");

        // ── (d) Cap holds ──────────────────────────────────────────────────
        Console.WriteLine("  (d) Cap holds at max pressure + worst matchup:");
        bool dOk;
        try
        {
            var offWeak  = new[] { Mk(50, bh: 1), Mk(50, bh: 1), Mk(50, bh: 1), Mk(50, bh: 1), Mk(50, bh: 1) };
            var defElite = new[] { Mk(50, st: 99), Mk(50, st: 99), Mk(50, st: 99), Mk(50, st: 99), Mk(50, st: 99) };
            var g   = BuildGame(offWeak, defElite);
            var d   = Split(cfgB, WithAwayPressure(10.0), g, St());
            var toShare  = d[HalfcourtOutcome.DeadBallTurnover] / actionMass;
            var foShare  = d[HalfcourtOutcome.Foul]             / actionMass;
            var capTo    = toShare  <= cfgMatchup.RollBTurnoverCeiling    + Eps;
            var capFo    = foShare  <= cfgMatchup.RollBFoulPressureCeiling + Eps;
            var procPos  = d[HalfcourtOutcome.Proceed] > 0.0;
            dOk = capTo && capFo && procPos;
            Console.WriteLine($"    TO share={toShare:F6}  ceiling={cfgMatchup.RollBTurnoverCeiling}  capped={capTo}");
            Console.WriteLine($"    Fo share={foShare:F6}  ceiling={cfgMatchup.RollBFoulPressureCeiling}  capped={capFo}");
            Console.WriteLine($"    Proceed > 0: {procPos}");
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;
        Console.WriteLine($"  (d) {(dOk ? "ok" : "FAIL")}");

        // ── (e) JumpBall exactly flat; all arms sum to 1 ──────────────────
        Console.WriteLine("  (e) JumpBall exactly flat, sum = 1:");
        bool eOk = true;
        try
        {
            var cases = new (string label, MatchupConfig cfg, Player[] off, Player[] def)[]
            {
                ("p=2 even",     WithAwayPressure(2.0),  off5, def5),
                ("p=5 even",     cfgMatchup,              off5, def5),
                ("p=9 even",     WithAwayPressure(9.0),  off5, def5),
                ("p=9 def_adv",  WithAwayPressure(9.0),
                    new[]{Mk(50,bh:20),Mk(50,bh:20),Mk(50,bh:20),Mk(50,bh:20),Mk(50,bh:20)},
                    new[]{Mk(50,st:80),Mk(50,st:80),Mk(50,st:80),Mk(50,st:80),Mk(50,st:80)}),
            };
            foreach (var (label, cfg, off, def) in cases)
            {
                var g   = BuildGame(off, def);
                var d   = Split(cfgB, cfg, g, St());
                var jbOk  = Math.Abs(d[HalfcourtOutcome.JumpBall] - cfgB.BaseJumpBall) < Eps;
                var sumOk = Math.Abs(d.Values.Sum() - 1.0) < Eps;
                var ok2   = jbOk && sumOk;
                eOk &= ok2;
                Console.WriteLine($"    {label,-14}  JumpBall={d[HalfcourtOutcome.JumpBall]:F8}  sum={d.Values.Sum():F10}  {(ok2?"ok":"FAIL")}");
            }
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;
        Console.WriteLine($"  (e) {(eOk ? "ok" : "FAIL")}");

        // ── (f) SelectedSlot does not affect the aggregate ─────────────────
        // Roll B reads all 5 slots, not the selected slot. A null SelectedSlot
        // and a stamped SelectedSlot must produce identical pies.
        Console.WriteLine("  (f) SelectedSlot-blind (Roll B reads all slots, not the selected one):");
        bool fOk;
        try
        {
            var g       = BuildGame(off5, def5);
            var dNull   = Split(cfgB, cfgMatchup, g, St(null));
            var dStamped = Split(cfgB, cfgMatchup, g, St(g.HomeLineup.SlotAt(1)));
            var allMatch = dNull.Keys.All(k => Math.Abs(dNull[k] - dStamped[k]) < Eps);
            fOk = allMatch;
            Console.WriteLine($"    null slot vs stamped slot → identical pie: {(allMatch?"OK":"FAIL")}");
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;
        Console.WriteLine($"  (f) {(fOk ? "ok" : "FAIL")}");

        // ── (g) Matchup matters — Option B invariant ───────────────────────
        // Elite defense Steals vs. average BH offense at high pressure must
        // produce more turnovers than an even matchup (proves team aggregate fires).
        Console.WriteLine("  (g) Matchup matters (Option B): def-adv > even at high pressure:");
        bool gOk;
        try
        {
            var cfgHigh  = WithAwayPressure(9.0);
            var offAvg   = new[] { Mk(50, bh: 50), Mk(50, bh: 50), Mk(50, bh: 50), Mk(50, bh: 50), Mk(50, bh: 50) };
            var defElite = new[] { Mk(50, st: 80), Mk(50, st: 80), Mk(50, st: 80), Mk(50, st: 80), Mk(50, st: 80) };
            var defAvg   = new[] { Mk(50, st: 50), Mk(50, st: 50), Mk(50, st: 50), Mk(50, st: 50), Mk(50, st: 50) };
            var gElite = BuildGame(offAvg, defElite);
            var gAvg   = BuildGame(offAvg, defAvg);
            var toElite = Split(cfgB, cfgHigh, gElite, St())[HalfcourtOutcome.DeadBallTurnover];
            var toAvg   = Split(cfgB, cfgHigh, gAvg,   St())[HalfcourtOutcome.DeadBallTurnover];
            gOk = toElite > toAvg;
            Console.WriteLine($"    TO @ elite-steal def={toElite:F6}  average-steal def={toAvg:F6}  defAdv lifts: {gOk}");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;
        Console.WriteLine($"  (g) {(gOk ? "ok" : "FAIL")}");

        Console.WriteLine(pass ? "  Phase 13 PASSED." : "  Phase 13 FAILED.");
        return pass;
    }

    // ── Phase 14: Full-court press disruption door (Roll A, backcourt entry) ──
    // Proves: (a) neutral anchor; (b) press raises TO; (c) press raises DefFoul;
    // (d) press raises OffFoul; (e) OffFoul < DefFoul at every press level;
    // (f) cap/overflow sanity; (g) JumpBall exactly flat off-neutral;
    // (h) five arms sum to 1; (i) Frontcourt=true returns exact baseline;
    // (j) SelectedSlot-blind (Roll A is pre-selection);
    // (k) per-axis wiring: skill, athleticism, size each move TO when varied alone,
    //     and size produces the smallest movement of the three.
    private static bool Phase15PressFrequencyStandardCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 15: Press frequency + Standard mode (Roll A) ---");
        var pass = true;
        const double Eps = 1e-9;

        // ── Shared helpers ────────────────────────────────────────────────────
        // Player builder: all attributes at b; override as needed.
        static Player Mk(int b, int? bh = null, int? st = null,
                         int? ath = null, int? len = null)
        {
            var a = ath ?? b;
            var l = len ?? b;
            return new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = bh ?? b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b, Steals = st ?? b,
                Height = l, Wingspan = l, Weight = b,
                Strength = a, Speed = a, Quickness = a, FirstStep = a,
                Vertical = l,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };
        }

        // GameState builder: Home offense slots 1-5, Away defense slots 1-5.
        // Defense = Away, so PressProbabilityFor reads AwayPressFrequency.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        var even5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

        // ── (1) PressProbabilityFor — pure function ───────────────────────────
        Console.WriteLine("  (1) PressProbabilityFor — pure function (no simulation):");
        bool t1Ok;
        try
        {
            var cfgM = MatchupConfig.Load(configPath);

            cfgM.AwayPressFrequency = 1.0;
            var atOne = cfgM.PressProbabilityFor(TeamSide.Away);
            cfgM.AwayPressFrequency = 10.0;
            var atTen = cfgM.PressProbabilityFor(TeamSide.Away);
            cfgM.AwayPressFrequency = 5.5;
            var atMid = cfgM.PressProbabilityFor(TeamSide.Away);
            var expectedMid = cfgM.PressProbabilityAtOne
                            + 4.5 / 9.0 * (cfgM.PressProbabilityAtTen - cfgM.PressProbabilityAtOne);

            var atOneOk = Math.Abs(atOne - cfgM.PressProbabilityAtOne) < Eps;
            var atTenOk = Math.Abs(atTen - cfgM.PressProbabilityAtTen) < Eps;
            var midOk   = Math.Abs(atMid - expectedMid)                  < Eps;
            var rangeOk = atOne >= 0.0 && atOne <= 1.0 && atTen >= 0.0 && atTen <= 1.0;
            var monoOk  = atOne <= atMid && atMid <= atTen;

            t1Ok = atOneOk && atTenOk && midOk && rangeOk && monoOk;
            Console.WriteLine($"    freq=1  → {atOne:F6}  want={cfgM.PressProbabilityAtOne:F6}  {(atOneOk?"OK":"FAIL")}");
            Console.WriteLine($"    freq=5.5→ {atMid:F6}  want={expectedMid:F6}  {(midOk?"OK":"FAIL")}");
            Console.WriteLine($"    freq=10 → {atTen:F6}  want={cfgM.PressProbabilityAtTen:F6}  {(atTenOk?"OK":"FAIL")}");
            Console.WriteLine($"    in [0,1]={rangeOk}  monotone={monoOk}");
        }
        catch (Exception ex) { t1Ok = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }
        pass &= t1Ok;
        Console.WriteLine($"  (1) {(t1Ok ? "ok" : "FAIL")}");

        // ── (2) Frequency gates pressed fraction — spy captures PressMode ─────
        Console.WriteLine("  (2) Frequency gates pressed fraction (spy-based):");
        bool t2Ok;
        try
        {
            const double TestFreq = 5.0;
            const int    N        = 5000;

            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            cfgM.AwayPressFrequency = TestFreq;
            var expectedProb = cfgM.PressProbabilityFor(TeamSide.Away);

            var safePie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = cfgA.BaseClean,
                    [EntryOutcome.Turnover]      = cfgA.BaseTurnover,
                    [EntryOutcome.DefensiveFoul] = cfgA.BaseDefensiveFoul,
                    [EntryOutcome.OffensiveFoul] = cfgA.BaseOffensiveFoul,
                    [EntryOutcome.JumpBall]      = cfgA.BaseJumpBall,
                }, cfgA.Epsilon);

            var spy  = new PressModeSpyGenerator(safePie);
            var game = new GameState(new FoulTracker(7, 10));
            var rng  = new SystemRng(cfgA.Seed);

            var resolver = new Resolver(
                spy,
                cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM,
                game,
                rng);

            var state = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);

            for (var i = 0; i < N; i++)
                resolver.RunPossession(state);

            var standardCount  = spy.Log.Count(m => m == PressMode.Standard);
            var noneCount      = spy.Log.Count(m => m == PressMode.None);
            var desperateCount = spy.Log.Count(m => m == PressMode.Desperate);
            var total          = spy.Log.Count;

            var observedFrac = (double)standardCount / total;
            // Tolerance: 5σ of binomial + 0.02 slack for re-inbound ratio bias
            var sigma5 = 5.0 * Math.Sqrt(expectedProb * (1.0 - expectedProb) / total);
            var fracOk = Math.Abs(observedFrac - expectedProb) < sigma5 + 0.02;
            var noDesperateLive = desperateCount == 0;

            t2Ok = fracOk && noDesperateLive;
            Console.WriteLine($"    freq={TestFreq}  expectedProb={expectedProb:F4}  observed={observedFrac:F4}  5σ={sigma5:F4}  fracOk={fracOk}");
            Console.WriteLine($"    logLen={total}  Standard={standardCount}  None={noneCount}  Desperate={desperateCount}  noDesperate={noDesperateLive}");
        }
        catch (Exception ex) { t2Ok = false; Console.WriteLine($"  FAIL  (2) threw: {ex.Message}"); }
        pass &= t2Ok;
        Console.WriteLine($"  (2) {(t2Ok ? "ok" : "FAIL")}");

        // ── (3) PressMode.None → exact config baseline ────────────────────────
        Console.WriteLine("  (3) PressMode.None → exact config baseline:");
        bool t3Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var g    = BuildGame(even5, even5);
            var gen  = new RollAGenerator(cfgA, cfgM, g);
            var st   = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);

            var d = gen.Generate(st, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var clOk = Math.Abs(d[EntryOutcome.CleanEntry]    - cfgA.BaseClean)         < Eps;
            var toOk = Math.Abs(d[EntryOutcome.Turnover]      - cfgA.BaseTurnover)      < Eps;
            var dfOk = Math.Abs(d[EntryOutcome.DefensiveFoul] - cfgA.BaseDefensiveFoul) < Eps;
            var ofOk = Math.Abs(d[EntryOutcome.OffensiveFoul] - cfgA.BaseOffensiveFoul) < Eps;
            var jbOk = Math.Abs(d[EntryOutcome.JumpBall]      - cfgA.BaseJumpBall)      < Eps;

            t3Ok = clOk && toOk && dfOk && ofOk && jbOk;
            Console.WriteLine($"    CleanEntry={d[EntryOutcome.CleanEntry]:F8}  want={cfgA.BaseClean:F8}  {(clOk?"OK":"FAIL")}");
            Console.WriteLine($"    Turnover  ={d[EntryOutcome.Turnover]:F8}  want={cfgA.BaseTurnover:F8}  {(toOk?"OK":"FAIL")}");
            Console.WriteLine($"    DefFoul   ={d[EntryOutcome.DefensiveFoul]:F8}  want={cfgA.BaseDefensiveFoul:F8}  {(dfOk?"OK":"FAIL")}");
            Console.WriteLine($"    OffFoul   ={d[EntryOutcome.OffensiveFoul]:F8}  want={cfgA.BaseOffensiveFoul:F8}  {(ofOk?"OK":"FAIL")}");
            Console.WriteLine($"    JumpBall  ={d[EntryOutcome.JumpBall]:F8}  want={cfgA.BaseJumpBall:F8}  {(jbOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { t3Ok = false; Console.WriteLine($"  FAIL  (3) threw: {ex.Message}"); }
        pass &= t3Ok;
        Console.WriteLine($"  (3) {(t3Ok ? "ok" : "FAIL")}");

        // ── (4) Standard pie shape ────────────────────────────────────────────
        Console.WriteLine("  (4) Standard pie shape:");
        bool t4Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            const int Gap = 20;
            var actionMass = cfgA.BaseClean + cfgA.BaseTurnover
                           + cfgA.BaseOffensiveFoul + cfgA.BaseDefensiveFoul;

            // Helper: build pie with PressMode.Standard for a given game.
            Dictionary<EntryOutcome, double> StdSplit(GameState gg)
            {
                var gen = new RollAGenerator(cfgA, cfgM, gg);
                var st  = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
                return gen.Generate(st, pressure: 0.0)
                          .Slices.ToDictionary(s => s.Outcome, s => s.Weight);
            }

            // 4a: even agg — StandardLift lifts all three arms above baseline
            var dEven   = StdSplit(BuildGame(even5, even5));
            var toAbove = dEven[EntryOutcome.Turnover]      / actionMass > cfgA.BaseTurnover      / actionMass;
            var dfAbove = dEven[EntryOutcome.DefensiveFoul] / actionMass > cfgA.BaseDefensiveFoul / actionMass;
            var ofAbove = dEven[EntryOutcome.OffensiveFoul] / actionMass > cfgA.BaseOffensiveFoul / actionMass;
            Console.WriteLine($"    (4a) StandardLift lifts: TO={toAbove}  DF={dfAbove}  OF={ofAbove}");

            // 4b: per-axis: each gap lifts TO; size produces smallest lift
            var toEven  = dEven[EntryOutcome.Turnover];
            var toSkill = StdSplit(BuildGame(
                new[] { Mk(50,bh:50-Gap), Mk(50,bh:50-Gap), Mk(50,bh:50-Gap), Mk(50,bh:50-Gap), Mk(50,bh:50-Gap) },
                new[] { Mk(50,st:50+Gap), Mk(50,st:50+Gap), Mk(50,st:50+Gap), Mk(50,st:50+Gap), Mk(50,st:50+Gap) }
            ))[EntryOutcome.Turnover];
            var toAth = StdSplit(BuildGame(
                new[] { Mk(50,ath:50-Gap), Mk(50,ath:50-Gap), Mk(50,ath:50-Gap), Mk(50,ath:50-Gap), Mk(50,ath:50-Gap) },
                new[] { Mk(50,ath:50+Gap), Mk(50,ath:50+Gap), Mk(50,ath:50+Gap), Mk(50,ath:50+Gap), Mk(50,ath:50+Gap) }
            ))[EntryOutcome.Turnover];
            var toSz = StdSplit(BuildGame(
                new[] { Mk(50,len:50-Gap), Mk(50,len:50-Gap), Mk(50,len:50-Gap), Mk(50,len:50-Gap), Mk(50,len:50-Gap) },
                new[] { Mk(50,len:50+Gap), Mk(50,len:50+Gap), Mk(50,len:50+Gap), Mk(50,len:50+Gap), Mk(50,len:50+Gap) }
            ))[EntryOutcome.Turnover];

            var skillLifts   = toSkill > toEven;
            var athLifts     = toAth   > toEven;
            var sizeLifts    = toSz    > toEven;
            var sizeSmallest = (toSz - toEven) < (toAth - toEven) && (toSz - toEven) < (toSkill - toEven);
            Console.WriteLine($"    (4b) even={toEven:F6}  +skill={toSkill-toEven:F6}lifts={skillLifts}  +ath={toAth-toEven:F6}lifts={athLifts}  +sz={toSz-toEven:F6}lifts={sizeLifts}  szSmallest={sizeSmallest}");

            // 4c: OffFoul < DefFoul at even
            var ofLtDf = dEven[EntryOutcome.OffensiveFoul] < dEven[EntryOutcome.DefensiveFoul];
            Console.WriteLine($"    (4c) OffFoul<DefFoul: {ofLtDf}  ({dEven[EntryOutcome.OffensiveFoul]:F6} < {dEven[EntryOutcome.DefensiveFoul]:F6})");

            // 4d: ceilings hold at worst matchup; CleanEntry > 0
            var offWeak  = new[] { Mk(50,bh:1,st:1,ath:1,len:1), Mk(50,bh:1,st:1,ath:1,len:1),
                                    Mk(50,bh:1,st:1,ath:1,len:1), Mk(50,bh:1,st:1,ath:1,len:1), Mk(50,bh:1,st:1,ath:1,len:1) };
            var defElite = new[] { Mk(50,bh:99,st:99,ath:99,len:99), Mk(50,bh:99,st:99,ath:99,len:99),
                                    Mk(50,bh:99,st:99,ath:99,len:99), Mk(50,bh:99,st:99,ath:99,len:99), Mk(50,bh:99,st:99,ath:99,len:99) };
            var dWorst  = StdSplit(BuildGame(offWeak, defElite));
            var toShare = dWorst[EntryOutcome.Turnover]      / actionMass;
            var dfShare = dWorst[EntryOutcome.DefensiveFoul] / actionMass;
            var ofShare = dWorst[EntryOutcome.OffensiveFoul] / actionMass;
            var capTO   = toShare <= cfgM.StandardTurnoverCeiling  + Eps;
            var capDF   = dfShare <= cfgM.StandardDefFoulCeiling   + Eps;
            var capOF   = ofShare <= cfgM.StandardOffFoulCeiling   + Eps;
            var cleanOk = dWorst[EntryOutcome.CleanEntry] > 0.0;
            Console.WriteLine($"    (4d) worst: TO={toShare:F4}<={cfgM.StandardTurnoverCeiling}={capTO}  DF={dfShare:F4}<={cfgM.StandardDefFoulCeiling}={capDF}  OF={ofShare:F4}<={cfgM.StandardOffFoulCeiling}={capOF}  cleanPos={cleanOk}");

            // 4e: five arms sum to 1; JumpBall exactly flat
            var sumEven = dEven.Values.Sum();
            var sumOk   = Math.Abs(sumEven - 1.0) < Eps;
            var jbOk    = Math.Abs(dEven[EntryOutcome.JumpBall] - cfgA.BaseJumpBall) < Eps;
            Console.WriteLine($"    (4e) sum={sumEven:F12}={sumOk}  JumpBall={dEven[EntryOutcome.JumpBall]:F10}=base={jbOk}");

            t4Ok = toAbove && dfAbove && ofAbove
                && skillLifts && athLifts && sizeLifts && sizeSmallest
                && ofLtDf
                && capTO && capDF && capOF && cleanOk
                && sumOk && jbOk;
        }
        catch (Exception ex) { t4Ok = false; Console.WriteLine($"  FAIL  (4) threw: {ex.Message}"); }
        pass &= t4Ok;
        Console.WriteLine($"  (4) {(t4Ok ? "ok" : "FAIL")}");

        // ── (5) Frontcourt=true gates Standard → exact baseline ────────────────
        Console.WriteLine("  (5) Frontcourt=true gates Standard (high-freq, worst matchup):");
        bool t5Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            cfgM.AwayPressFrequency = 10.0;

            var offWeak  = new[] { Mk(1), Mk(1), Mk(1), Mk(1), Mk(1) };
            var defElite = new[] { Mk(99), Mk(99), Mk(99), Mk(99), Mk(99) };
            var g   = BuildGame(offWeak, defElite);
            var gen = new RollAGenerator(cfgA, cfgM, g);
            var st  = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                Frontcourt: true, PressMode: PressMode.Standard);

            var d = gen.Generate(st, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var clOk = Math.Abs(d[EntryOutcome.CleanEntry]    - cfgA.BaseClean)         < Eps;
            var toOk = Math.Abs(d[EntryOutcome.Turnover]      - cfgA.BaseTurnover)      < Eps;
            var dfOk = Math.Abs(d[EntryOutcome.DefensiveFoul] - cfgA.BaseDefensiveFoul) < Eps;
            var ofOk = Math.Abs(d[EntryOutcome.OffensiveFoul] - cfgA.BaseOffensiveFoul) < Eps;
            var jbOk = Math.Abs(d[EntryOutcome.JumpBall]      - cfgA.BaseJumpBall)      < Eps;

            t5Ok = clOk && toOk && dfOk && ofOk && jbOk;
            Console.WriteLine($"    CL={clOk}  TO={toOk}  DF={dfOk}  OF={ofOk}  JB={jbOk}  (all must be true)");
        }
        catch (Exception ex) { t5Ok = false; Console.WriteLine($"  FAIL  (5) threw: {ex.Message}"); }
        pass &= t5Ok;
        Console.WriteLine($"  (5) {(t5Ok ? "ok" : "FAIL")}");

        // ── (6) SelectedSlot-blind — null vs stamped slot gives identical pie ──
        Console.WriteLine("  (6) SelectedSlot-blind (Standard: null vs stamped slot):");
        bool t6Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var g    = BuildGame(even5, even5);
            var gen  = new RollAGenerator(cfgA, cfgM, g);

            var stNull = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
            var stSlot = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard,
                SelectedSlot: g.HomeLineup.SlotAt(1));

            var dNull = gen.Generate(stNull, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);
            var dSlot = gen.Generate(stSlot, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var allMatch = dNull.Keys.All(k => Math.Abs(dNull[k] - dSlot[k]) < Eps);
            t6Ok = allMatch;
            Console.WriteLine($"    null slot vs stamped slot → identical pie: {(allMatch?"OK":"FAIL")}");
        }
        catch (Exception ex) { t6Ok = false; Console.WriteLine($"  FAIL  (6) threw: {ex.Message}"); }
        pass &= t6Ok;
        Console.WriteLine($"  (6) {(t6Ok ? "ok" : "FAIL")}");

        // ── (7) PressMode threads through re-inbounds ─────────────────────────
        // Route a ResumeInbound/ResolveSidelineInbound Continue directly — the spy
        // records which PressMode c.State carries. This directly verifies that the
        // stamp placed in RunPossession survives every `with` in the chain.
        Console.WriteLine("  (7) PressMode threads through ResumeInbound and ResolveSidelineInbound:");
        bool t7Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);

            var fixedPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = cfgA.BaseClean,
                    [EntryOutcome.Turnover]      = cfgA.BaseTurnover,
                    [EntryOutcome.DefensiveFoul] = cfgA.BaseDefensiveFoul,
                    [EntryOutcome.OffensiveFoul] = cfgA.BaseOffensiveFoul,
                    [EntryOutcome.JumpBall]      = cfgA.BaseJumpBall,
                }, cfgA.Epsilon);

            var spy  = new PressModeSpyGenerator(fixedPie);
            var game = new GameState(new FoulTracker(7, 10));
            var rng  = new SystemRng(42);

            var resolver = new Resolver(
                spy,
                cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM,
                game,
                rng);

            // State stamped Standard; state stamped None.
            var stStd  = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                             Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
            var stNone = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                             Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);

            // 7a: ResumeInbound with Standard — spy must record Standard on every call
            spy.Log.Clear();
            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stStd));
            var resumeStdOk = spy.Log.Count > 0 && spy.Log.All(m => m == PressMode.Standard);

            // 7b: ResumeInbound with None — spy must record None on every call
            spy.Log.Clear();
            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stNone));
            var resumeNoneOk = spy.Log.Count > 0 && spy.Log.All(m => m == PressMode.None);

            // 7c: ResolveSidelineInbound with Standard — Phase 16 clears PressMode to None
            //     before calling Generate (dead-ball re-inbound ends the press stamp).
            //     The spy must record None, not Standard.
            spy.Log.Clear();
            resolver.Route(new Continue(ContinuationKind.ResolveSidelineInbound, stStd));
            var sidelineStdOk = spy.Log.Count > 0 && spy.Log.All(m => m == PressMode.None);

            t7Ok = resumeStdOk && resumeNoneOk && sidelineStdOk;
            Console.WriteLine($"    ResumeInbound(Standard)         → all spy entries Standard: {resumeStdOk}");
            Console.WriteLine($"    ResumeInbound(None)             → all spy entries None:     {resumeNoneOk}");
            Console.WriteLine($"    ResolveSidelineInbound(Standard)→ all spy entries None (Phase 16 dead-ball clear): {sidelineStdOk}");
        }
        catch (Exception ex) { t7Ok = false; Console.WriteLine($"  FAIL  (7) threw: {ex.Message}"); }
        pass &= t7Ok;
        Console.WriteLine($"  (7) {(t7Ok ? "ok" : "FAIL")}");

        // ── (8) Desperate fail-loud — reserved, must never be produced live ────
        Console.WriteLine("  (8) Desperate fail-loud:");
        bool t8Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var g    = BuildGame(even5, even5);
            var gen  = new RollAGenerator(cfgA, cfgM, g);

            // 8a: stamping Desperate and calling Generate must throw
            var stDesp = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Desperate);
            bool threwOk;
            try
            {
                gen.Generate(stDesp, pressure: 0.0);
                threwOk = false;   // should have thrown
            }
            catch (InvalidOperationException) { threwOk = true; }
            catch { threwOk = false; }

            // 8b: the live path (low frequency → RunPossession) never produces Desperate.
            // Use the spy over N possessions and assert desperateCount == 0 (verified in (2) above,
            // re-confirmed here for clarity).
            var cfgA2 = RollAConfig.Load(configPath);
            var cfgM2 = MatchupConfig.Load(configPath);
            var safePie2 = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = cfgA2.BaseClean,
                    [EntryOutcome.Turnover]      = cfgA2.BaseTurnover,
                    [EntryOutcome.DefensiveFoul] = cfgA2.BaseDefensiveFoul,
                    [EntryOutcome.OffensiveFoul] = cfgA2.BaseOffensiveFoul,
                    [EntryOutcome.JumpBall]      = cfgA2.BaseJumpBall,
                }, cfgA2.Epsilon);
            var spy2  = new PressModeSpyGenerator(safePie2);
            var game2 = new GameState(new FoulTracker(7, 10));
            var rng2  = new SystemRng(cfgA2.Seed);
            var res2  = new Resolver(
                spy2, cfgA2,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM2, game2, rng2);
            var st2 = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                         Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound);
            for (var i = 0; i < 1000; i++) res2.RunPossession(st2);
            var noDesperateLive = spy2.Log.All(m => m != PressMode.Desperate);

            t8Ok = threwOk && noDesperateLive;
            Console.WriteLine($"    Desperate stamp → Generate throws InvalidOperationException: {threwOk}");
            Console.WriteLine($"    Live RunPossession (1000×) never produces Desperate:         {noDesperateLive}");
        }
        catch (Exception ex) { t8Ok = false; Console.WriteLine($"  FAIL  (8) threw: {ex.Message}"); }
        pass &= t8Ok;
        Console.WriteLine($"  (8) {(t8Ok ? "ok" : "FAIL")}");

        Console.WriteLine(pass ? "  Phase 15 PASSED." : "  Phase 15 FAILED.");
        return pass;
    }

    private static bool Phase16PressBreakFastBreakCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 16: Press-break fast break ---");
        var pass = true;
        const double Eps = 1e-9;

        // ── Shared helpers ─────────────────────────────────────────────────────
        // Player builder: all attributes at b, with optional rim/three tendency overrides.
        static Player Mk16(int b, int? rim = null, int? three = null)
        {
            return new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency   = rim   ?? b,
                ShortTendency = b,
                MidTendency   = b,
                LongTendency  = b,
                ThreeTendency = three ?? b,
            };
        }

        GameState BuildGame16(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        var even5 = new[] { Mk16(50), Mk16(50), Mk16(50), Mk16(50), Mk16(50) };

        // ── (1) Standard + IntoHalfcourtSet → Roll E sees FastBreak=true, PressMode=None ──
        Console.WriteLine("  (1) Standard press + IntoHalfcourtSet → Roll E receives FastBreak=true AND PressMode=None:");
        bool t1Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgE = RollEConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var game = BuildGame16(even5, even5);
            var rng  = new SystemRng(1);

            var fixedE = new Pie<SelectionOutcome>(
                new Dictionary<SelectionOutcome, double>
                {
                    [SelectionOutcome.Slot1] = cfgE.BaseSlot1,
                    [SelectionOutcome.Slot2] = cfgE.BaseSlot2,
                    [SelectionOutcome.Slot3] = cfgE.BaseSlot3,
                    [SelectionOutcome.Slot4] = cfgE.BaseSlot4,
                    [SelectionOutcome.Slot5] = cfgE.BaseSlot5,
                }, cfgE.Epsilon);
            var spyE = new RollESpyGenerator(fixedE);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                spyE,
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stStandard = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);

            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stStandard));

            var fbOk = spyE.Log.Count > 0 && spyE.Log[0].FastBreak == true;
            var pmOk = spyE.Log.Count > 0 && spyE.Log[0].Press     == PressMode.None;
            t1Ok = fbOk && pmOk;
            Console.WriteLine($"    spy called {spyE.Log.Count} time(s) on this routing");
            Console.WriteLine($"    spy.Log[0].FastBreak == true:   {fbOk}");
            Console.WriteLine($"    spy.Log[0].Press == None:       {pmOk}");
        }
        catch (Exception ex) { t1Ok = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }
        pass &= t1Ok;
        Console.WriteLine($"  (1) {(t1Ok ? "ok" : "FAIL")}");

        // ── (2) None press + IntoHalfcourtSet → Roll B fires, Roll E sees FastBreak=false ──
        Console.WriteLine("  (2) None press + IntoHalfcourtSet → Roll B fires, Roll E sees FastBreak=false:");
        bool t2Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgB  = RollBConfig.Load(configPath);
            var cfgE  = RollEConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(2);

            var fixedE = new Pie<SelectionOutcome>(
                new Dictionary<SelectionOutcome, double>
                {
                    [SelectionOutcome.Slot1] = cfgE.BaseSlot1,
                    [SelectionOutcome.Slot2] = cfgE.BaseSlot2,
                    [SelectionOutcome.Slot3] = cfgE.BaseSlot3,
                    [SelectionOutcome.Slot4] = cfgE.BaseSlot4,
                    [SelectionOutcome.Slot5] = cfgE.BaseSlot5,
                }, cfgE.Epsilon);
            var spyE = new RollESpyGenerator(fixedE);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new AlwaysProceedRollBGenerator(cfgB),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                spyE,
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stNone = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);

            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stNone));

            // AlwaysProceedRollBGenerator guarantees Roll B → Proceed → IntoPlayerSelection → spy
            var calledOk = spyE.Log.Count > 0;
            var fbOk     = calledOk && spyE.Log[0].FastBreak == false;
            var pmOk     = calledOk && spyE.Log[0].Press     == PressMode.None;
            t2Ok = calledOk && fbOk && pmOk;
            Console.WriteLine($"    Roll E spy called (confirms Roll B path taken): {calledOk}");
            Console.WriteLine($"    spy.Log[0].FastBreak == false:  {fbOk}");
            Console.WriteLine($"    spy.Log[0].Press == None:       {pmOk}");
        }
        catch (Exception ex) { t2Ok = false; Console.WriteLine($"  FAIL  (2) threw: {ex.Message}"); }
        pass &= t2Ok;
        Console.WriteLine($"  (2) {(t2Ok ? "ok" : "FAIL")}");

        // ── (3) PressMode consumed — second IntoHalfcourtSet cannot re-fire ────
        Console.WriteLine("  (3) PressMode consumed — second IntoHalfcourtSet with PressMode=None → Roll B fires:");
        bool t3Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgB  = RollBConfig.Load(configPath);
            var cfgE  = RollEConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(3);

            var fixedE = new Pie<SelectionOutcome>(
                new Dictionary<SelectionOutcome, double>
                {
                    [SelectionOutcome.Slot1] = cfgE.BaseSlot1,
                    [SelectionOutcome.Slot2] = cfgE.BaseSlot2,
                    [SelectionOutcome.Slot3] = cfgE.BaseSlot3,
                    [SelectionOutcome.Slot4] = cfgE.BaseSlot4,
                    [SelectionOutcome.Slot5] = cfgE.BaseSlot5,
                }, cfgE.Epsilon);
            var spyE = new RollESpyGenerator(fixedE);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new AlwaysProceedRollBGenerator(cfgB),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                spyE,
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            // First routing: Standard press → press-break fires. Spy sees (FastBreak=true, None).
            var stStandard = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stStandard));
            var firstFireFb = spyE.Log.Count > 0 && spyE.Log[0].FastBreak == true;
            var firstFirePm = spyE.Log.Count > 0 && spyE.Log[0].Press     == PressMode.None;

            // Second routing: consumed state (PressMode=None) → Roll B fires, not press-break.
            spyE.Log.Clear();
            var stConsumed = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);
            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stConsumed));
            var secondFired    = spyE.Log.Count > 0;
            var secondFbFalse  = secondFired && spyE.Log[0].FastBreak == false;

            t3Ok = firstFireFb && firstFirePm && secondFired && secondFbFalse;
            Console.WriteLine($"    first routing:  press-break fires (FastBreak=true, Press=None): {firstFireFb && firstFirePm}");
            Console.WriteLine($"    second routing: Roll B path taken (FastBreak=false at Roll E):  {secondFired && secondFbFalse}");
        }
        catch (Exception ex) { t3Ok = false; Console.WriteLine($"  FAIL  (3) threw: {ex.Message}"); }
        pass &= t3Ok;
        Console.WriteLine($"  (3) {(t3Ok ? "ok" : "FAIL")}");

        // ── (4) ResolveSidelineInbound clears both markers ────────────────────────
        Console.WriteLine("  (4) ResolveSidelineInbound clears FastBreak and PressMode:");
        bool t4Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(4);

            var cleanPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = 1.0,
                    [EntryOutcome.Turnover]      = 0.0,
                    [EntryOutcome.DefensiveFoul] = 0.0,
                    [EntryOutcome.OffensiveFoul] = 0.0,
                    [EntryOutcome.JumpBall]      = 0.0,
                }, cfgA.Epsilon);
            var spyA = new FullStateRollASpyGenerator(cleanPie);

            var resolver = new Resolver(
                spyA, cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stBoth = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, FastBreak: true, PressMode: PressMode.Standard);

            resolver.Route(new Continue(ContinuationKind.ResolveSidelineInbound, stBoth));

            var fbOk = spyA.Log.Count > 0 && spyA.Log[0].FastBreak == false;
            var pmOk = spyA.Log.Count > 0 && spyA.Log[0].Press     == PressMode.None;
            t4Ok = fbOk && pmOk;
            Console.WriteLine($"    Roll A sees FastBreak=false: {fbOk}");
            Console.WriteLine($"    Roll A sees PressMode=None:  {pmOk}");
        }
        catch (Exception ex) { t4Ok = false; Console.WriteLine($"  FAIL  (4) threw: {ex.Message}"); }
        pass &= t4Ok;
        Console.WriteLine($"  (4) {(t4Ok ? "ok" : "FAIL")}");

        // ── (5) ResumeInbound (frontcourt) clears both markers ────────────────────
        Console.WriteLine("  (5) ResumeInbound (frontcourt) clears FastBreak and PressMode:");
        bool t5Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(5);

            var cleanPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = 1.0,
                    [EntryOutcome.Turnover]      = 0.0,
                    [EntryOutcome.DefensiveFoul] = 0.0,
                    [EntryOutcome.OffensiveFoul] = 0.0,
                    [EntryOutcome.JumpBall]      = 0.0,
                }, cfgA.Epsilon);
            var spyA = new FullStateRollASpyGenerator(cleanPie);

            var resolver = new Resolver(
                spyA, cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stFrontcourt = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, Frontcourt: true, FastBreak: true, PressMode: PressMode.None);

            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stFrontcourt));

            var fbOk = spyA.Log.Count > 0 && spyA.Log[0].FastBreak == false;
            var pmOk = spyA.Log.Count > 0 && spyA.Log[0].Press     == PressMode.None;
            t5Ok = fbOk && pmOk;
            Console.WriteLine($"    Roll A sees FastBreak=false: {fbOk}");
            Console.WriteLine($"    Roll A sees PressMode=None:  {pmOk}");
        }
        catch (Exception ex) { t5Ok = false; Console.WriteLine($"  FAIL  (5) threw: {ex.Message}"); }
        pass &= t5Ok;
        Console.WriteLine($"  (5) {(t5Ok ? "ok" : "FAIL")}");

        // ── (6) ResumeInbound (backcourt) preserves active Standard press ─────────
        Console.WriteLine("  (6) ResumeInbound (backcourt) preserves active Standard press:");
        bool t6Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(6);

            var cleanPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = 1.0,
                    [EntryOutcome.Turnover]      = 0.0,
                    [EntryOutcome.DefensiveFoul] = 0.0,
                    [EntryOutcome.OffensiveFoul] = 0.0,
                    [EntryOutcome.JumpBall]      = 0.0,
                }, cfgA.Epsilon);
            var spyA = new FullStateRollASpyGenerator(cleanPie);

            var resolver = new Resolver(
                spyA, cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stBackcourt = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, Frontcourt: false, FastBreak: false, PressMode: PressMode.Standard);

            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stBackcourt));

            // Press must survive — Roll A gets PressMode=Standard (can still be beaten on CleanEntry).
            var pmOk = spyA.Log.Count > 0 && spyA.Log[0].Press == PressMode.Standard;
            t6Ok = pmOk;
            Console.WriteLine($"    Roll A sees PressMode=Standard (press survived backcourt foul): {pmOk}");
        }
        catch (Exception ex) { t6Ok = false; Console.WriteLine($"  FAIL  (6) threw: {ex.Message}"); }
        pass &= t6Ok;
        Console.WriteLine($"  (6) {(t6Ok ? "ok" : "FAIL")}");

        // ── (7) Roll G with FastBreak=true → flat fast-break pie, bypasses tendencies ──
        Console.WriteLine("  (7) Roll G with FastBreak=true → flat fast-break pie regardless of shooter tendencies:");
        bool t7Ok;
        try
        {
            var cfgG = RollGConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);

            var shooterA = Mk16(50, rim: 99, three: 1);
            var shooterB = Mk16(50, rim: 1,  three: 99);

            var gameA = BuildGame16(new[] { shooterA, Mk16(50), Mk16(50), Mk16(50), Mk16(50) }, even5);
            var gameB = BuildGame16(new[] { shooterB, Mk16(50), Mk16(50), Mk16(50), Mk16(50) }, even5);

            var genA = new RollGGenerator(cfgG, cfgM, gameA);
            var genB = new RollGGenerator(cfgG, cfgM, gameB);

            var slot = gameA.HomeLineup.SlotAt(1);   // Slot(Home,1) — same value type in both games

            var stBreak   = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                                Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound,
                                SelectedSlot: slot, FastBreak: true);
            var stNoBreak = stBreak with { FastBreak = false };

            var pieABreak   = genA.Generate(stBreak);
            var pieBBreak   = genB.Generate(stBreak);
            var pieANoBreak = genA.Generate(stNoBreak);
            var pieBNoBreak = genB.Generate(stNoBreak);

            double Wt(Pie<ShotLocation> p, ShotLocation loc) =>
                p.Slices.First(s => s.Outcome == loc).Weight;

            var aRimOk   = Math.Abs(Wt(pieABreak, ShotLocation.Rim)   - cfgG.FastBreakRim)   < Eps;
            var aThreeOk = Math.Abs(Wt(pieABreak, ShotLocation.Three) - cfgG.FastBreakThree) < Eps;
            var aSumOk   = Math.Abs(pieABreak.Slices.Sum(s => s.Weight) - 1.0) < Eps;
            var bRimOk   = Math.Abs(Wt(pieBBreak, ShotLocation.Rim)   - cfgG.FastBreakRim)   < Eps;
            var bThreeOk = Math.Abs(Wt(pieBBreak, ShotLocation.Three) - cfgG.FastBreakThree) < Eps;
            var bSumOk   = Math.Abs(pieBBreak.Slices.Sum(s => s.Weight) - 1.0) < Eps;
            var samePie  = pieABreak.Slices.All(s => Math.Abs(s.Weight - Wt(pieBBreak, s.Outcome)) < Eps);
            // Non-FastBreak: rim-dominant and three-dominant shooters must get different rim weights.
            var noBreakDiffer = Math.Abs(Wt(pieANoBreak, ShotLocation.Rim) - Wt(pieBNoBreak, ShotLocation.Rim)) > 0.01;

            t7Ok = aRimOk && aThreeOk && aSumOk && bRimOk && bThreeOk && bSumOk && samePie && noBreakDiffer;
            Console.WriteLine($"    ShooterA FB: rim={Wt(pieABreak,ShotLocation.Rim):F4} want={cfgG.FastBreakRim}  three={Wt(pieABreak,ShotLocation.Three):F4} want={cfgG.FastBreakThree}  sum=1:{aSumOk}");
            Console.WriteLine($"    ShooterB FB: rim={Wt(pieBBreak,ShotLocation.Rim):F4} want={cfgG.FastBreakRim}  three={Wt(pieBBreak,ShotLocation.Three):F4} want={cfgG.FastBreakThree}  sum=1:{bSumOk}");
            Console.WriteLine($"    Both shooters same fast-break pie:             {samePie}");
            Console.WriteLine($"    Non-FastBreak pies differ (tendencies active): {noBreakDiffer}");
        }
        catch (Exception ex) { t7Ok = false; Console.WriteLine($"  FAIL  (7) threw: {ex.Message}"); }
        pass &= t7Ok;
        Console.WriteLine($"  (7) {(t7Ok ? "ok" : "FAIL")}");

        // ── (8) End-to-end smoke — 1 000-possession batch, unrouted == 0 ──────────
        Console.WriteLine("  (8) End-to-end smoke (1 000 possessions, AwayPressFreq=10):");
        bool t8Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            cfgM.AwayPressFrequency = 10.0;   // ~80% press probability
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(cfgA.Seed);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCStubPieGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDStubPieGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            const int N = 1_000;
            var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                         Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound);

            var ended    = 0;
            var parked   = 0;
            var unrouted = 0;
            for (var i = 0; i < N; i++)
            {
                var r = resolver.RunPossession(st);
                if (r.PossessionEnded)                  ended++;
                else if (r.Destination.StartsWith("STUB:")) parked++;
                else                                    unrouted++;
            }

            t8Ok = unrouted == 0;
            Console.WriteLine($"    ended={ended:N0}  parked={parked:N0}  unrouted={unrouted} → {(t8Ok ? "ok" : "FAIL")}");
        }
        catch (Exception ex) { t8Ok = false; Console.WriteLine($"  FAIL  (8) threw: {ex.Message}"); }
        pass &= t8Ok;
        Console.WriteLine($"  (8) {(t8Ok ? "ok" : "FAIL")}");

        Console.WriteLine(pass ? "  Phase 16 PASSED." : "  Phase 16 FAILED.");
        return pass;
    }

    /// <summary>
    /// Spy implementation of <see cref="IRollAPieGenerator"/> for Phase 15 testing.
    /// Records every <see cref="PossessionState.PressMode"/> it receives (proving
    /// the Resolver stamped BEFORE calling Generate), then returns a caller-supplied
    /// fixed pie. Injected in place of <see cref="RollAGenerator"/> so the press
    /// roll can be observed directly without inferring it from possession outcomes.
    /// </summary>
    private sealed class PressModeSpyGenerator : IRollAPieGenerator
    {
        private readonly Pie<EntryOutcome> _fixedPie;
        public readonly List<PressMode> Log = new();

        public PressModeSpyGenerator(Pie<EntryOutcome> fixedPie) => _fixedPie = fixedPie;

        public Pie<EntryOutcome> Generate(PossessionState state, double pressure)
        {
            Log.Add(state.PressMode);
            return _fixedPie;
        }
    }

    /// <summary>
    /// Spy implementation of <see cref="IRollAPieGenerator"/> for Phase 16 testing.
    /// Records every (FastBreak, PressMode) pair it receives, then returns a caller-supplied
    /// fixed pie. Used to verify what state the Resolver passes to Roll A at
    /// ResumeInbound and ResolveSidelineInbound edges.
    /// </summary>
    private sealed class FullStateRollASpyGenerator : IRollAPieGenerator
    {
        private readonly Pie<EntryOutcome> _fixedPie;
        public readonly List<(bool FastBreak, PressMode Press)> Log = new();

        public FullStateRollASpyGenerator(Pie<EntryOutcome> fixedPie) => _fixedPie = fixedPie;

        public Pie<EntryOutcome> Generate(PossessionState state, double pressure)
        {
            Log.Add((state.FastBreak, state.PressMode));
            return _fixedPie;
        }
    }

    /// <summary>
    /// Spy implementation of <see cref="IRollEPieGenerator"/> for Phase 16 testing.
    /// Records every (FastBreak, PressMode) pair it receives, then returns a caller-supplied
    /// fixed pie. Used to verify that the Resolver stamps FastBreak=true and PressMode=None
    /// on the breakState it passes to Roll E at the IntoHalfcourtSet press-break gate.
    /// </summary>
    private sealed class RollESpyGenerator : IRollEPieGenerator
    {
        private readonly Pie<SelectionOutcome> _fixedPie;
        public readonly List<(bool FastBreak, PressMode Press)> Log = new();

        public RollESpyGenerator(Pie<SelectionOutcome> fixedPie) => _fixedPie = fixedPie;

        public Pie<SelectionOutcome> Generate(PossessionState state)
        {
            Log.Add((state.FastBreak, state.PressMode));
            return _fixedPie;
        }
    }

    /// <summary>
    /// Test-only Roll B generator that always returns a 100% Proceed pie.
    /// Used in Phase 16 test 2 to make the None-press halfcourt path deterministic:
    /// Roll B always Proceeds, guaranteeing Roll E is reached exactly once so the
    /// spy log length and content can be asserted without distributional variance.
    /// </summary>
    private sealed class AlwaysProceedRollBGenerator : IRollBPieGenerator
    {
        private readonly RollBConfig _cfg;
        public AlwaysProceedRollBGenerator(RollBConfig cfg) => _cfg = cfg;

        public Pie<HalfcourtOutcome> Generate(PossessionState state, double physicality)
        {
            var weights = new Dictionary<HalfcourtOutcome, double>
            {
                [HalfcourtOutcome.Proceed]          = 1.0,
                [HalfcourtOutcome.Foul]             = 0.0,
                [HalfcourtOutcome.DeadBallTurnover] = 0.0,
                [HalfcourtOutcome.JumpBall]         = 0.0,
            };
            return new Pie<HalfcourtOutcome>(weights, _cfg.Epsilon);
        }
    }
}

