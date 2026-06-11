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

        var rng = new SystemRng(cfg.Seed);
        var rollAGenerator = new StubPieGenerator(cfg);
        var rollBGenerator = new RollBStubPieGenerator(cfgB);
        var rollCGenerator = new RollCStubPieGenerator(cfgC);
        var rollDGenerator = new RollDStubPieGenerator(cfgD);
        var rollEGenerator = new RollEStubPieGenerator(cfgE);

        // The half's foul tracker carries the config-driven bonus thresholds.
        var fouls = new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);
        var game = new GameState(fouls);  // arrow starts Off — first jump ball is the tip

        var resolver = new Resolver(
            rollBGenerator,
            rollCGenerator,
            rollDGenerator,
            rollEGenerator,
            game,
            rng,
            new PlayerActionStub(),
            new ResumeInboundStub(),
            new ResolveFreeThrowsStub());

        var state = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        Console.WriteLine("=== Project Charm :: Roll A -> B -> C -> D -> E Chain ===\n");

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
                Terminal => EntryOutcome.ShotClockViolation,
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

        // Note: with Roll C live, turnovers now end the possession (terminal)
        // instead of routing to a stub. So "ended" now includes them.
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
}
