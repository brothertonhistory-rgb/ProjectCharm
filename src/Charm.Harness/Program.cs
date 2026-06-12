using Charm.Engine;

namespace Charm.Harness;

internal static class Program
{
    private static int Main()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfg = RollAConfig.Load(configPath);
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var rollAGenerator = new StubPieGenerator(cfg);
        var rollBGenerator = new RollBStubPieGenerator(cfgB);
        var rollCGenerator = new RollCStubPieGenerator(cfgC);
        var rollDGenerator = new RollDStubPieGenerator(cfgD);
        var rollEGenerator = new RollEStubPieGenerator(cfgE);
        var rollFGenerator = new RollFStubPieGenerator(cfgF);
        var rollGGenerator = new RollGStubPieGenerator(cfgG);
        var rollHGenerator = new RollHStubPieGenerator(cfgH);

        // The half's foul tracker carries the config-driven bonus thresholds.
        var fouls = new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);
        var game = new GameState(fouls);  // arrow starts Off — first jump ball is the tip

        var resolver = new Resolver(
            rollBGenerator,
            rollCGenerator,
            rollDGenerator,
            rollEGenerator,
            rollFGenerator,
            rollGGenerator,
            rollHGenerator,
            game,
            rng,
            new ResumeInboundStub(),
            new ResolveFreeThrowsStub(),
            new BlockRecoveryStub(),
            new ReboundStub(),
            new ShootingFreeThrowsStub(),
            new SidelineInboundStub());

        var state = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        Console.WriteLine("=== Project Charm :: Roll A -> B -> C -> D -> E -> F -> G -> H Chain ===\n");

        ShowSamples(cfg, cfgE, rollAGenerator, rollEGenerator, resolver, game, state, rng);
        var ok = BatchCheck(cfg, cfgB, rollAGenerator, rollBGenerator, resolver, state);
        ok &= RollCBatchCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollDFlavorBatchCheck(cfg, cfgD, rollDGenerator, state);
        ok &= RollDBonusRoutingCheck(cfgD, rollDGenerator, state);
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
        var genC = new RollCStubPieGenerator(RollCConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "config.json")));
        var pieC = genC.Generate(state, pressure: 0.0);
        Console.WriteLine($"  pie: {pieC}");
        var sampleRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 5; i++)
        {
            var r = RollC.Execute(state, pieC, sampleRng);
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
                Terminal { Reason: "ShotClockViolation" } => EntryOutcome.ShotClockViolation,
                Terminal { Reason: "FiveSecondInbound" } => EntryOutcome.FiveSecondInbound,
                Terminal { Reason: "TenSecondBackcourt" } => EntryOutcome.TenSecondBackcourt,
                Continue { Next: ContinuationKind.IntoHalfcourtSet } => EntryOutcome.CleanEntry,
                Continue { Next: ContinuationKind.ResolveTurnoverType } => EntryOutcome.Turnover,
                Continue { Next: ContinuationKind.ResolveFoulType } => EntryOutcome.Foul,
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

        // Note: with Roll C and Roll F live, turnovers and jump balls now end the
        // possession (terminal) instead of routing to a stub. So "ended" includes
        // them; the player-action shot exits and the foul exits route to stubs.
        // (A blocked shot also routes to a stub, but it is now a Roll H outcome,
        // not a Roll F exit.)
        var handoffOk = unrouted == 0 && (ended + routedToStub) == cfg.BatchSize;
        Console.WriteLine($"\n  handoff: ended={ended:N0}, routed-to-stub={routedToStub:N0}, unrouted={unrouted} -> {(handoffOk ? "ok" : "FAIL")}");

        return ratesOk && handoffOk;
    }

    // --- Batch: Roll C's five rates match its pie, and every exit is a clean terminal. ---
    private static bool RollCBatchCheck(
        RollAConfig cfg, RollCConfig cfgC, RollCStubPieGenerator genC, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} turnovers through Roll C (pressure=0.00) ---");
        var rng = new SystemRng(cfg.Seed);
        var pieC = genC.Generate(state, pressure: 0.0);

        var counts = new Dictionary<TurnoverOutcome, int>();
        foreach (var o in Enum.GetValues<TurnoverOutcome>()) counts[o] = 0;

        var nonTerminal = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollC.Execute(state, pieC, rng);
            if (result is not Terminal t)
            {
                nonTerminal++;
                continue;
            }
            var outcome = t.Reason switch
            {
                "BadPassDeadBall" => TurnoverOutcome.BadPassDeadBall,
                "BadPassIntercepted" => TurnoverOutcome.BadPassIntercepted,
                "LostBallDeadBall" => TurnoverOutcome.LostBallDeadBall,
                "LostBallLiveBall" => TurnoverOutcome.LostBallLiveBall,
                "OffensiveFoul" => TurnoverOutcome.OffensiveFoul,
                _ => throw new InvalidOperationException($"Unmapped Roll C reason '{t.Reason}'.")
            };
            counts[outcome]++;
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

        return ratesOk && terminalOk;
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
        var low = RollCLiveStripRate(genC, state, rng, pressure: 0.0);
        rng = new SystemRng(42);
        var high = RollCLiveStripRate(genC, state, rng, pressure: 1.0);

        Console.WriteLine($"  live-strip rate @ pressure 0.00 = {low:P3}");
        Console.WriteLine($"  live-strip rate @ pressure 1.00 = {high:P3}");

        var moved = high > low;
        Console.WriteLine($"  signal is live (high > low): {(moved ? "ok" : "FAIL")}");
        return moved;
    }

    private static double RollCLiveStripRate(
        RollCStubPieGenerator genC, PossessionState state, IRng rng, double pressure)
    {
        var pie = genC.Generate(state, pressure);
        var strips = 0;
        const int n = 100_000;
        for (var i = 0; i < n; i++)
            if (RollC.Execute(state, pie, rng) is Terminal { Reason: "LostBallLiveBall" })
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

        return ratesOk && cleanOk;
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
    //     zero unrouted: turnover -> Roll C terminal, non-shooting foul -> Roll D
    //     (ResumeInbound/ResolveFreeThrows stub), shot -> Roll G -> Roll H ->
    //     resolved (any of its six destinations, including the block stub), jump
    //     ball -> jump-ball terminal. A fresh local game + resolver keeps this
    //     self-contained; the shared foul count climbing into the bonus mid-run is
    //     expected (it just shifts some foul exits from ResumeInbound to
    //     ResolveFreeThrows — both clean). (Block left Roll F in Session 13; it is
    //     now a Roll H slice, so there is no F-stage block destination here.) ---
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
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            new RollDStubPieGenerator(cfgD),
            genE,
            genF,
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            game,
            rng,
            new ResumeInboundStub(),
            new ResolveFreeThrowsStub(),
            new BlockRecoveryStub(),
            new ReboundStub(),
            new ShootingFreeThrowsStub(),
            new SidelineInboundStub());

        var pieE = genE.Generate(state);
        var pieF = genF.Generate(state);

        var destClasses = new Dictionary<string, int>
        {
            ["turnover -> Roll C terminal"] = 0,
            ["foul -> Roll D stub"] = 0,
            ["shot -> Roll G -> Roll H -> resolved"] = 0,
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
            // Roll H's resolved shot landings (checked BEFORE the generic END:
            // catch, since Made / MissOutOfBoundsLost are also END: terminals):
            // a shot now flows F -> G -> H to one of six make/miss destinations
            // (the sixth, blocked, is a Roll H slice routing to the block stub).
            else if (d == "END:Made" || d == "END:MissOutOfBoundsLost"
                     || d.StartsWith("STUB:Rebound")
                     || d.StartsWith("STUB:ShootingFreeThrows")
                     || d.StartsWith("STUB:SidelineInbound")
                     || d.StartsWith("STUB:BlockRecovery"))
                destClasses["shot -> Roll G -> Roll H -> resolved"]++;
            else if (d.StartsWith("END:")) destClasses["turnover -> Roll C terminal"]++;
            else if (d.StartsWith("STUB:ResumeInbound") || d.StartsWith("STUB:ResolveFreeThrows")) destClasses["foul -> Roll D stub"]++;
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
        Console.WriteLine($"  all four F exits reached their destination: {(allHit ? "ok" : "FAIL")}");

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

    // --- G->H integration: route a batch of IntoShotType exits through the
    //     resolver. With Roll H now live, an IntoShotType ticket flows G (stamps a
    //     zone) -> H (stamps a result) and lands at one of Roll H's six make/miss
    //     destinations (block recovery added Session 13). This check confirms the
    //     WHOLE post-shot chain routes: zero unrouted, all six destinations
    //     reached, and on the four stub landings the zone Roll G stamped still
    //     rides through (parsed from the STUB:{node}:{Side}slot{N}:{Zone}:{Result}
    //     label). The two terminal landings (Made / MissOutOfBoundsLost) carry no
    //     zone in their END: label and are counted as terminal landings. (Roll H in
    //     isolation is checked by RollHHandoffCheck, which feeds IntoShotResolution
    //     directly.) A fresh local game + resolver keeps it self-contained. ---
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
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            new RollDStubPieGenerator(cfgD),
            genE,
            new RollFStubPieGenerator(cfgF),
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            game,
            rng,
            new ResumeInboundStub(),
            new ResolveFreeThrowsStub(),
            new BlockRecoveryStub(),
            new ReboundStub(),
            new ShootingFreeThrowsStub(),
            new SidelineInboundStub());

        var pieE = genE.Generate(state);

        // The six destinations a shot can land at after G -> H (block recovery
        // added in Session 13: a located shot can now be blocked, zone-weighted).
        var destHits = new Dictionary<string, int>
        {
            ["END:Made"] = 0,
            ["END:MissOutOfBoundsLost"] = 0,
            ["STUB:Rebound"] = 0,
            ["STUB:ShootingFreeThrows"] = 0,
            ["STUB:SidelineInbound"] = 0,
            ["STUB:BlockRecovery"] = 0,
        };
        // Zone seen on the stub landings (the labels that carry it). Terminals
        // omit the zone, so they don't contribute here.
        var zonesSeenOnStubs = new Dictionary<ShotLocation, int>();
        foreach (var z in Enum.GetValues<ShotLocation>()) zonesSeenOnStubs[z] = 0;

        var unrecognized = 0;
        var missingFact = 0;   // any NO_SLOT / NO_ZONE / NO_RESULT on a stub landing

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Select a real slot (Roll E), then hand the resolver a clean
            // IntoShotType continuation — exactly what Roll F emits on a ShotAttempt.
            var selected = ((Continue)RollE.Execute(state, pieE, game, rng)).State;
            var shotTicket = new Continue(ContinuationKind.IntoShotType, selected);
            var d = resolver.Route(shotTicket).Destination;

            if (d == "END:Made") { destHits["END:Made"]++; continue; }
            if (d == "END:MissOutOfBoundsLost") { destHits["END:MissOutOfBoundsLost"]++; continue; }

            // The four stub landings: STUB:{node}:{Side}slot{N}:{Zone}:{Result}
            string node;
            if (d.StartsWith("STUB:Rebound")) node = "STUB:Rebound";
            else if (d.StartsWith("STUB:ShootingFreeThrows")) node = "STUB:ShootingFreeThrows";
            else if (d.StartsWith("STUB:SidelineInbound")) node = "STUB:SidelineInbound";
            else if (d.StartsWith("STUB:BlockRecovery")) node = "STUB:BlockRecovery";
            else { unrecognized++; continue; }

            if (d.EndsWith("NO_SLOT") || d.EndsWith("NO_ZONE") || d.EndsWith("NO_RESULT"))
            {
                missingFact++;
                continue;
            }

            // Zone is the second-to-last colon token; result is the last.
            var parts = d.Split(':');
            var zoneText = parts[^2];
            if (Enum.TryParse<ShotLocation>(zoneText, out var z)) { zonesSeenOnStubs[z]++; destHits[node]++; }
            else unrecognized++;
        }

        var landed = destHits.Values.Sum();
        Console.WriteLine($"  landed past G->H: {landed:N0}");
        Console.WriteLine("  destinations reached:");
        var allDests = true;
        foreach (var (label, count) in destHits)
        {
            var hit = count > 0;
            allDests &= hit;
            Console.WriteLine($"    {label,-26} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var allZones = true;
        Console.WriteLine("  all five zones rode through to the stub landings:");
        foreach (var z in Enum.GetValues<ShotLocation>())
        {
            var hit = zonesSeenOnStubs[z] > 0;
            allZones &= hit;
            Console.WriteLine($"    {z,-6} {zonesSeenOnStubs[z],8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        var factOk = missingFact == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  slot+zone+result intact on every stub landing: missing={missingFact} -> {(factOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all six G->H destinations reached: {(allDests ? "ok" : "FAIL")}");
        Console.WriteLine($"  all five zones reached the stub landings: {(allZones ? "ok" : "FAIL")}");

        return routedOk && factOk && allDests && allZones;
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
    //     Miss -> rebound stub, MadeAndFouled/MissFouled -> shooting-FT stub,
    //     MissOutOfBoundsRetained -> sideline-inbound stub, Blocked -> block-
    //     recovery stub (Session 13). Isolates the Roll H hop exactly as
    //     RollGHandoffCheck isolated Roll G: the resolver executes Roll H on an
    //     IntoShotResolution continuation (carrying a real slot + zone) and lands at
    //     a terminal or one of the four stubs. On the stub landings it confirms
    //     slot, zone, AND result all rode through. A fresh local game + resolver
    //     keeps it self-contained. ---
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
            new RollBStubPieGenerator(cfgB),
            new RollCStubPieGenerator(cfgC),
            new RollDStubPieGenerator(cfgD),
            genE,
            new RollFStubPieGenerator(cfgF),
            genG,
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            game,
            rng,
            new ResumeInboundStub(),
            new ResolveFreeThrowsStub(),
            new BlockRecoveryStub(),
            new ReboundStub(),
            new ShootingFreeThrowsStub(),
            new SidelineInboundStub());

        var pieE = genE.Generate(state);

        var destHits = new Dictionary<string, int>
        {
            ["END:Made"] = 0,
            ["END:MissOutOfBoundsLost"] = 0,
            ["STUB:Rebound"] = 0,
            ["STUB:ShootingFreeThrows"] = 0,
            ["STUB:SidelineInbound"] = 0,
            ["STUB:BlockRecovery"] = 0,
        };
        var resultsSeenOnStubs = new Dictionary<ShotResult, int>();
        foreach (var r in Enum.GetValues<ShotResult>()) resultsSeenOnStubs[r] = 0;

        var unrecognized = 0;
        var missingFact = 0;

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

            string node;
            if (d.StartsWith("STUB:Rebound")) node = "STUB:Rebound";
            else if (d.StartsWith("STUB:ShootingFreeThrows")) node = "STUB:ShootingFreeThrows";
            else if (d.StartsWith("STUB:SidelineInbound")) node = "STUB:SidelineInbound";
            else if (d.StartsWith("STUB:BlockRecovery")) node = "STUB:BlockRecovery";
            else { unrecognized++; continue; }

            if (d.EndsWith("NO_SLOT") || d.EndsWith("NO_ZONE") || d.EndsWith("NO_RESULT"))
            {
                missingFact++;
                continue;
            }

            // Result is the last colon token on a stub landing.
            var resultText = d[(d.LastIndexOf(':') + 1)..];
            if (Enum.TryParse<ShotResult>(resultText, out var r)) { resultsSeenOnStubs[r]++; destHits[node]++; }
            else unrecognized++;
        }

        var landed = destHits.Values.Sum();
        Console.WriteLine($"  landed past Roll H: {landed:N0}");
        Console.WriteLine("  destinations reached:");
        var allDests = true;
        foreach (var (label, count) in destHits)
        {
            var hit = count > 0;
            allDests &= hit;
            Console.WriteLine($"    {label,-26} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        // The continue-outcomes that reach a stub: Miss -> Rebound, MadeAndFouled
        // + MissFouled -> ShootingFreeThrows, MissOutOfBoundsRetained ->
        // SidelineInbound, Blocked -> BlockRecovery (Session 13). Confirm each rode
        // its result through.
        var continueResults = new[]
        {
            ShotResult.MadeAndFouled, ShotResult.Miss,
            ShotResult.MissFouled, ShotResult.MissOutOfBoundsRetained,
            ShotResult.Blocked
        };
        var allResults = true;
        Console.WriteLine("  each continue-outcome's result rode through to its stub:");
        foreach (var r in continueResults)
        {
            var hit = resultsSeenOnStubs[r] > 0;
            allResults &= hit;
            Console.WriteLine($"    {r,-24} {resultsSeenOnStubs[r],8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        var factOk = missingFact == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  slot+zone+result intact on every stub landing: missing={missingFact} -> {(factOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all six Roll H destinations reached: {(allDests ? "ok" : "FAIL")}");
        Console.WriteLine($"  every continue-outcome's result reached its stub: {(allResults ? "ok" : "FAIL")}");

        return routedOk && factOk && allDests && allResults;
    }
}
