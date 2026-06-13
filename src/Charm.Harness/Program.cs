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

        var rng = new SystemRng(cfg.Seed);
        var rollAGenerator = new StubPieGenerator(cfg);
        var rollBGenerator = new RollBStubPieGenerator(cfgB);
        var rollCGenerator = new RollCStubPieGenerator(cfgC);
        var rollDGenerator = new RollDStubPieGenerator(cfgD);
        var rollEGenerator = new RollEStubPieGenerator(cfgE);
        var rollFGenerator = new RollFStubPieGenerator(cfgF);
        var rollGGenerator = new RollGStubPieGenerator(cfgG);
        var rollHGenerator = new RollHStubPieGenerator(cfgH);
        var rollIGenerator = new RollIStubPieGenerator(cfgI);
        var rollJGenerator = new RollJStubPieGenerator(cfgJ);
        var rollKGenerator = new RollKStubPieGenerator(cfgK);
        var rollLGenerator = new RollLStubPieGenerator(cfgL);
        var rollMGenerator = new RollMStubPieGenerator(cfgM);
        var offensiveFoulGenerator = new RollOffensiveFoulStubPieGenerator(cfgOffFoul);

        // The half's foul tracker carries the config-driven bonus thresholds.
        var fouls = new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);
        var game = new GameState(fouls);  // arrow starts Off — first jump ball is the tip

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
            game,
            rng,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

        var state = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        Console.WriteLine("=== Project Charm :: Roll A -> B -> C -> D -> E -> F -> G -> H -> I -> J -> K Chain ===\n");

        ShowSamples(cfg, cfgE, rollAGenerator, rollEGenerator, resolver, game, state, rng);
        var ok = BatchCheck(cfg, cfgB, rollAGenerator, rollBGenerator, resolver, state);
        ok &= RollCBatchCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollDFlavorBatchCheck(cfg, cfgD, rollDGenerator, state);
        ok &= RollDBonusRoutingCheck(cfgD, rollDGenerator, state);
        ok &= DefensiveFoulChargeCheck(cfgD, state);
        ok &= PhysicalitySignalCheck(cfgB, rollBGenerator, state);
        ok &= PressureSignalCheck(cfgC, rollCGenerator, state);
        ok &= JumpBallCheck(cfg);
        ok &= SlotLayerCheck(game);
        ok &= RollESelectionBatchCheck(cfg, cfgE, rollEGenerator, game, state);
        ok &= RollFActionBatchCheck(cfg, cfgF, rollFGenerator, state);
        ok &= RollFHandoffCheck(cfg, game, state);
        ok &= RollGLocationBatchCheck(cfg, cfgG, rollGGenerator, state);
        ok &= RollGHandoffCheck(cfg, state);
        ok &= RollHResolutionBatchCheck(cfg, cfgH, rollHGenerator, state);
        ok &= RollHHandoffCheck(cfg, state);
        ok &= RollIReboundBatchCheck(cfg, cfgI, rollIGenerator, game, state);
        ok &= RollIBonusForkCheck(cfg, cfgD, cfgI, rollIGenerator, state);
        ok &= RollIBlockReboundBatchCheck(cfg, cfgI, rollIGenerator, state);
        ok &= RollIBlockContextSelectionCheck(cfg, cfgI, state);
        ok &= RollJBatchCheck(cfg, cfgD, cfgJ, rollJGenerator, state);
        ok &= RollJBonusForkCheck(cfg, cfgD, cfgJ, state);
        ok &= RollJStealBatchCheck(cfg, cfgD, cfgJ, rollJGenerator, state);
        ok &= RollKReboundBatchCheck(cfg, cfgK, rollKGenerator, game, state);
        ok &= RollKPutbackPieCheck(cfg, cfgH, state);
        ok &= RollKBonusForkCheck(cfg, cfgD, cfgK, rollKGenerator, state);
        ok &= RollLFreeThrowCheck(cfg, state);
        ok &= RollMReboundBatchCheck(cfg, cfgM, rollMGenerator, game, state);
        ok &= RollMContextSelectionCheck(cfg, cfgK, cfgJ, state);
        ok &= OffensiveReboundConvergenceCheck(cfg, state);
        ok &= RollCContextCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollCExpansionCheck(cfg, cfgC, rollCGenerator, state);
        ok &= GovernorLoopCheck(cfg, cfgD, cfgGov, cfgClock);

        Console.WriteLine(ok ? "\nALL CHECKS PASSED." : "\nCHECKS FAILED.");
        return ok ? 0 : 1;
    }

    // --- Observability: print a few full A->...->terminal chains. ---
    private static void ShowSamples(
        RollAConfig cfg, RollEConfig cfgE, StubPieGenerator genA,
        RollEStubPieGenerator genE, Resolver resolver,
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
        var pieIObs = genI.Generate(ReboundSource.LiveBall);
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
        StubPieGenerator genA, RollBStubPieGenerator genB,
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
    //     with no signal, each of the five slots is equally likely. A future
    //     generator tilts these odds without this check's roll changing. ---
    private static bool RollESelectionBatchCheck(
        RollAConfig cfg, RollEConfig cfgE, RollEStubPieGenerator genE,
        GameState game, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} selections through Roll E (flat pie) ---");
        var rng = new SystemRng(cfg.Seed);
        var pieE = genE.Generate(state);

        var counts = new Dictionary<SelectionOutcome, int>();
        foreach (var o in Enum.GetValues<SelectionOutcome>()) counts[o] = 0;

        var anomalies = 0;   // anything that isn't a clean, slot-stamped continue

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollE.Execute(state, pieE, game, rng);

            // Must be a Continue, into the player-action sequence, carrying a
            // selected slot that belongs to the offense and is numbered 1–5.
            if (result is not Continue { Next: ContinuationKind.IntoPlayerAction } c
                || c.State.SelectedSlot is not { } slot
                || slot.Side != state.Offense
                || slot.Number < 1 || slot.Number > Lineup.Size)
            {
                anomalies++;
                continue;
            }

            // Map the named slot back to its outcome bucket for the rate tally.
            counts[(SelectionOutcome)(slot.Number - 1)]++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll E selections (expected 20.000% each):");
        foreach (var (outcome, weight) in pieE.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-8} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var cleanOk = anomalies == 0;
        Console.WriteLine($"\n  every exit a clean slot-stamped IntoPlayerAction: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");

        // --- FastBreak selects the transition pie (Contextification #1). The generator
        //     reads ONE field — PossessionState.FastBreak — to choose between the flat
        //     halfcourt pie and the transition pie. Build both states and assert each
        //     pie's slices equal the configured weights. This is the direct proof that
        //     the transition selection pie is drawn ONLY on the Push (FastBreak) path;
        //     the flat batch above already covered the halfcourt path's RATES.
        var halfcourtPie = genE.Generate(state with { FastBreak = false });
        var transitionPie = genE.Generate(state with { FastBreak = true });

        var halfcourtExpected = new Dictionary<SelectionOutcome, double>
        {
            [SelectionOutcome.Slot1] = cfgE.BaseSlot1, [SelectionOutcome.Slot2] = cfgE.BaseSlot2,
            [SelectionOutcome.Slot3] = cfgE.BaseSlot3, [SelectionOutcome.Slot4] = cfgE.BaseSlot4,
            [SelectionOutcome.Slot5] = cfgE.BaseSlot5,
        };
        var transitionExpected = new Dictionary<SelectionOutcome, double>
        {
            [SelectionOutcome.Slot1] = cfgE.TransitionSlot1, [SelectionOutcome.Slot2] = cfgE.TransitionSlot2,
            [SelectionOutcome.Slot3] = cfgE.TransitionSlot3, [SelectionOutcome.Slot4] = cfgE.TransitionSlot4,
            [SelectionOutcome.Slot5] = cfgE.TransitionSlot5,
        };

        bool PieMatches(Pie<SelectionOutcome> pie, Dictionary<SelectionOutcome, double> expected) =>
            pie.Slices.All(s => Math.Abs(s.Item2 - expected[s.Item1]) <= cfgE.Epsilon);

        var halfcourtPieOk = PieMatches(halfcourtPie, halfcourtExpected);
        var transitionPieOk = PieMatches(transitionPie, transitionExpected);
        // The two pies MUST differ — otherwise "transition pie selected" would be
        // unobservable. The placeholder transition weights are non-flat for this reason.
        var piesDiffer = transitionPie.Slices.Any(s =>
            Math.Abs(s.Item2 - halfcourtExpected[s.Item1]) > cfgE.Epsilon);

        Console.WriteLine("\n  FastBreak pie selection:");
        Console.WriteLine($"    FastBreak=false -> halfcourt pie (flat 20s): {(halfcourtPieOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    FastBreak=true  -> transition pie (30/30/25/10/5): {(transitionPieOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    the two pies differ (selection is observable): {(piesDiffer ? "ok" : "FAIL")}");

        return ratesOk && cleanOk && halfcourtPieOk && transitionPieOk && piesDiffer;
    }

    // --- Batch: Roll F's four-way action distribution converges within tolerance,
    //     and every exit is a clean Continue carrying one of the four expected
    //     kinds. The pie is flat-ish (no signal); a future attribute generator
    //     tilts it without this roll changing. Mirrors the Roll E batch check.
    //     (Block left Roll F in Session 13 — it is now a per-zone slice of Roll
    //     H.) ---
    private static bool RollFActionBatchCheck(
        RollAConfig cfg, RollFConfig cfgF, RollFStubPieGenerator genF, PossessionState state)
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
            game,
            rng,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

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
        RollAConfig cfg, RollGConfig cfgG, RollGStubPieGenerator genG, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} locations through Roll G (flat-ish pie) ---");
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
            game,
            rng,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

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
        RollAConfig cfg, RollHConfig cfgH, RollHStubPieGenerator genH, PossessionState state)
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
            var pieH = genH.Generate(preH);

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
        var makeScale = 1.0 - blendedBlock;

        var expected = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made] = cfgH.BaseMade * makeScale,
            [ShotResult.MadeAndFouled] = cfgH.BaseMadeAndFouled * makeScale,
            [ShotResult.Miss] = cfgH.BaseMiss * makeScale,
            [ShotResult.MissFouled] = cfgH.BaseMissFouled * makeScale,
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
            game,
            rng,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

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
        var pieI = genI.Generate(ReboundSource.LiveBall);

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
        var pieI = genI.Generate(ReboundSource.Block);

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
            genI.Generate(s).Slices.First(x => x.Outcome == ReboundOutcome.DefensiveRebound).Weight;

        foreach (var (src, expected) in contexts)
        {
            var pie = genI.Generate(src);
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
            game,
            rngR,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

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

    // --- Session 15: the thin Governor's possession-to-possession loop. ---
    // The FIRST check whose whole point is state persisting across iterations: it
    // shares ONE GameState across the entire loop, so foul counts climb and CROSS
    // THE BONUS mid-loop (CONVENTIONS §2a). The Governor handles every stub-park
    // through ONE default-consequence path (keyed only on "no terminal"), so the
    // Session-14 "only handled one landing" bug class cannot recur — the per-stub
    // breakdown is observability, never routing.
    private static bool GovernorLoopCheck(RollAConfig cfg, RollDConfig cfgD, GovernorConfig cfgGov, RollClockConfig cfgClock)
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
            game,
            rng,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

        var governor = new Governor(resolver, game, cfgGov, cfgClock, new SystemRng(cfg.Seed + 1));

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
        var noLostOk = result.TerminalEnded + result.Parked == records.Count;

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
            $"| terminal+parked==total -> {(noLostOk ? "ok" : "FAIL")}");
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

        // Score: FG-rule deterministic check (no RNG), then accumulation integrity.
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

        var expected = new[]
        {
            (ShotResult.Made, cfgH.PutbackMade),
            (ShotResult.MadeAndFouled, cfgH.PutbackMadeAndFouled),
            (ShotResult.Miss, cfgH.PutbackMiss),
            (ShotResult.MissFouled, cfgH.PutbackMissFouled),
            (ShotResult.MissOutOfBoundsLost, cfgH.PutbackMissOutOfBoundsLost),
            (ShotResult.MissOutOfBoundsRetained, cfgH.PutbackMissOutOfBoundsRetained),
            (ShotResult.Blocked, cfgH.PutbackBlocked),
        };

        // 1) Selection: the putback pie equals its configured Putback* weights.
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
            game,
            rng,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

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
        var pieM = genM.Generate();

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
            game,
            rng,
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

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

        var seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        Console.WriteLine($"  seed: {seed}");
        Console.WriteLine();

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
            game,
            new SystemRng(seed),
            new ResumeInboundStub(),
            new BlockRecoveryStub(),
            new SidelineInboundStub(),
            new TransitionStub());

        game.SetPossessionArrow(TeamSide.Home);
        var governor = new Governor(resolver, game, cfgGov, cfgClock, new SystemRng(seed + 1));

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

            Console.WriteLine($"  {r.Number,-4} {offTeam,-5} {r.Entry,-18} {outcome,-28} {pts,-4} {score,-20} {r.Elapsed:F0}s");
        }

        Console.WriteLine();
        Console.WriteLine($"  ==========================================");
        Console.WriteLine($"  FINAL:  Home {game.HomeScore,3}  -  Away {game.AwayScore,3}");
        Console.WriteLine($"  ==========================================");
        Console.WriteLine();
        Console.WriteLine($"  {records.Count} possessions  |  APL {result.TotalSeconds / records.Count:F1}s  |  total {result.TotalSeconds:N0}s");
    }

}
