using Charm.Engine;

namespace Charm.Harness;

internal static class Program
{
    private static int Main()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfg = RollAConfig.Load(configPath);
        var cfgB = RollBConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var rollAGenerator = new StubPieGenerator(cfg);
        var rollBGenerator = new RollBStubPieGenerator(cfgB);

        var resolver = new Resolver(
            rollBGenerator,
            rng,
            new TurnoverTypeResolverStub(),
            new FoulTypeResolverStub(),
            new JumpBallResolverStub(),
            new PlayerSelectionStub());

        var state = new PossessionState(
            PossessionNumber: 1,
            Offense: "HOME",
            Defense: "AWAY",
            Entry: EntryType.DeadBallInbound);

        Console.WriteLine("=== Project Charm :: Roll A -> Roll B Chain ===\n");

        ShowSamples(cfg, rollAGenerator, resolver, state, rng);
        var ok = BatchCheck(cfg, cfgB, rollAGenerator, rollBGenerator, resolver, state);
        ok &= PhysicalitySignalCheck(cfgB, rollBGenerator, state);

        Console.WriteLine(ok ? "\nALL CHECKS PASSED." : "\nCHECKS FAILED.");
        return ok ? 0 : 1;
    }

    // --- Observability: print a few full A->B chains. ---
    private static void ShowSamples(
        RollAConfig cfg, StubPieGenerator genA, Resolver resolver,
        PossessionState state, IRng rng)
    {
        Console.WriteLine("--- Observability: sample possessions (seeded, A->B chain) ---");
        const double pressure = 0.5;

        for (var i = 0; i < 8; i++)
        {
            var pie = genA.Generate(state, pressure);
            var result = RollA.Execute(state, pie, rng, cfg);
            var routing = resolver.Route(result);

            var kind = result is Terminal ? "TERMINAL" : "CONTINUE";
            var elapsed = result.ElapsedSeconds is { } s ? $"{s:0}s" : "deferred";
            Console.WriteLine(
                $"  result={kind,-8} | elapsed={elapsed,-8} | route -> {routing.Destination}" +
                (routing.PossessionEnded ? " (possession ended)" : ""));
        }
        Console.WriteLine();
    }

    // --- Batch: confirm A->B rates and clean hand-offs throughout. ---
    private static bool BatchCheck(
        RollAConfig cfg, RollBConfig cfgB,
        StubPieGenerator genA, RollBStubPieGenerator genB,
        Resolver resolver, PossessionState state)
    {
        Console.WriteLine($"--- Batch: {cfg.BatchSize:N0} possessions (A->B chain, physicality=0.00) ---");
        var rng = new SystemRng(cfg.Seed);
        const double pressure = 0.0;
        var pieA = genA.Generate(state, pressure);

        // Count Roll A outcomes.
        var aCounts = new Dictionary<EntryOutcome, int>();
        foreach (var o in Enum.GetValues<EntryOutcome>()) aCounts[o] = 0;

        // Count Roll B outcomes (only possessions that reached B).
        var bCounts = new Dictionary<HalfcourtOutcome, int>();
        foreach (var o in Enum.GetValues<HalfcourtOutcome>()) bCounts[o] = 0;

        var ended = 0;
        var routedToStub = 0;
        var unrouted = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollA.Execute(state, pieA, rng, cfg);

            // Classify Roll A outcome before handing to resolver.
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

            // For clean entries, Run Roll B independently to count its outcomes.
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

            // Walk the full chain through the resolver.
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

        var handoffOk = unrouted == 0 && (ended + routedToStub) == cfg.BatchSize;
        Console.WriteLine($"\n  handoff: ended={ended:N0}, routed-to-stub={routedToStub:N0}, unrouted={unrouted} -> {(handoffOk ? "ok" : "FAIL")}");

        return ratesOk && handoffOk;
    }

    // --- Prove Roll B's seam carries signal: foul rate must rise with physicality. ---
    private static bool PhysicalitySignalCheck(RollBConfig cfgB, RollBStubPieGenerator genB, PossessionState state)
    {
        Console.WriteLine("\n--- Seam signal: Roll B foul rate vs. physicality ---");
        var rng = new SystemRng(42);
        var low = FoulRate(cfgB, genB, state, rng, physicality: 0.0);
        rng = new SystemRng(42);
        var high = FoulRate(cfgB, genB, state, rng, physicality: 1.0);

        Console.WriteLine($"  foul rate @ physicality 0.00 = {low:P3}");
        Console.WriteLine($"  foul rate @ physicality 1.00 = {high:P3}");

        var moved = high > low;
        Console.WriteLine($"  signal is live (high > low): {(moved ? "ok" : "FAIL")}");
        return moved;
    }

    private static double FoulRate(
        RollBConfig cfgB, RollBStubPieGenerator genB,
        PossessionState state, IRng rng, double physicality)
    {
        var pie = genB.Generate(state, physicality);
        var fouls = 0;
        const int n = 100_000;
        for (var i = 0; i < n; i++)
        {
            if (RollB.Execute(state, pie, rng) is Continue { Next: ContinuationKind.ResolveFoulType })
                fouls++;
        }
        return fouls / (double)n;
    }
}
