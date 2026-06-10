using Charm.Engine;

namespace Charm.Harness;

internal static class Program
{
    private static int Main()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfg = RollAConfig.Load(configPath);

        var generator = new StubPieGenerator(cfg);
        var resolver = new Resolver(new HalfcourtSetStub(), new TurnoverTypeResolverStub());
        var state = new PossessionState(PossessionNumber: 1, Offense: "HOME", Defense: "AWAY", Entry: EntryType.DeadBallInbound);

        Console.WriteLine("=== Project Charm :: Roll A — Entry: Inbounds (Dead Ball) ===\n");

        ShowSamples(cfg, generator, resolver, state);
        var ok = BatchCheck(cfg, generator, resolver, state);
        ok &= PressureSignalCheck(cfg, generator, resolver, state);

        Console.WriteLine(ok ? "\nALL CHECKS PASSED." : "\nCHECKS FAILED.");
        return ok ? 0 : 1;
    }

    // --- Observability: print a few pies with their inputs and outcomes. ---
    private static void ShowSamples(RollAConfig cfg, StubPieGenerator gen, Resolver resolver, PossessionState state)
    {
        Console.WriteLine("--- Observability: sample possessions (seeded) ---");
        var rng = new SystemRng(cfg.Seed);
        const double pressure = 0.5;

        for (var i = 0; i < 6; i++)
        {
            var pie = gen.Generate(state, pressure);
            var result = RollA.Execute(state, pie, rng, cfg);
            var routing = resolver.Route(result);

            var kind = result is Terminal ? "TERMINAL" : "CONTINUE";
            var elapsed = result.ElapsedSeconds is { } s ? $"{s:0}s" : "deferred";
            Console.WriteLine(
                $"  pressure={pressure:0.00} | pie[{pie}] | result={kind} " +
                $"| elapsed={elapsed} | route -> {routing.Destination}" +
                (routing.PossessionEnded ? " (possession ended)" : ""));
        }
        Console.WriteLine();
    }

    // --- Batch: fire BatchSize times, confirm rates match the pie and every
    //     exit hands off cleanly (terminals end, continues route to a stub). ---
    private static bool BatchCheck(RollAConfig cfg, StubPieGenerator gen, Resolver resolver, PossessionState state)
    {
        Console.WriteLine($"--- Batch: {cfg.BatchSize:N0} possessions (pressure=0.00, pure base pie) ---");
        var rng = new SystemRng(cfg.Seed);
        const double pressure = 0.0;
        var pie = gen.Generate(state, pressure);

        var counts = new Dictionary<string, int> { ["clean"] = 0, ["turnover"] = 0, ["violation"] = 0 };
        var routedToStub = 0;
        var ended = 0;
        var unrouted = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollA.Execute(state, pie, rng, cfg);
            var routing = resolver.Route(result);

            switch (result)
            {
                case Continue { Next: ContinuationKind.IntoHalfcourtSet }: counts["clean"]++; break;
                case Continue { Next: ContinuationKind.ResolveTurnoverType }: counts["turnover"]++; break;
                case Terminal: counts["violation"]++; break;
            }

            if (routing.PossessionEnded) ended++;
            else if (routing.Destination.StartsWith("STUB:")) routedToStub++;
            else unrouted++;
        }

        var n = (double)cfg.BatchSize;
        var expected = new Dictionary<string, double>
        {
            ["clean"] = pie.Slices.First(s => s.Outcome == EntryOutcome.CleanEntry).Weight,
            ["turnover"] = pie.Slices.First(s => s.Outcome == EntryOutcome.Turnover).Weight,
            ["violation"] = pie.Slices.First(s => s.Outcome == EntryOutcome.ShotClockViolation).Weight,
        };

        var ratesOk = true;
        foreach (var key in counts.Keys)
        {
            var observed = counts[key] / n;
            var gap = Math.Abs(observed - expected[key]);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"  {key,-10} observed={observed:P3}  expected={expected[key]:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var handoffOk = unrouted == 0 && (ended + routedToStub) == cfg.BatchSize;
        Console.WriteLine($"  handoff: ended={ended:N0}, routed-to-stub={routedToStub:N0}, unrouted={unrouted} -> {(handoffOk ? "ok" : "FAIL")}");

        return ratesOk && handoffOk;
    }

    // --- Prove the seam carries signal: turnover rate must rise with pressure. ---
    private static bool PressureSignalCheck(RollAConfig cfg, StubPieGenerator gen, Resolver resolver, PossessionState state)
    {
        Console.WriteLine("\n--- Seam signal: turnover rate vs. pressure ---");
        var low = TurnoverRate(cfg, gen, state, pressure: 0.0);
        var high = TurnoverRate(cfg, gen, state, pressure: 1.0);

        Console.WriteLine($"  turnover rate @ pressure 0.00 = {low:P3}");
        Console.WriteLine($"  turnover rate @ pressure 1.00 = {high:P3}");

        var moved = high > low;
        Console.WriteLine($"  signal is live (high > low): {(moved ? "ok" : "FAIL")}");
        return moved;
    }

    private static double TurnoverRate(RollAConfig cfg, StubPieGenerator gen, PossessionState state, double pressure)
    {
        var rng = new SystemRng(cfg.Seed);
        var pie = gen.Generate(state, pressure);
        var turnovers = 0;
        for (var i = 0; i < cfg.BatchSize; i++)
        {
            if (RollA.Execute(state, pie, rng, cfg) is Continue { Next: ContinuationKind.ResolveTurnoverType })
                turnovers++;
        }
        return turnovers / (double)cfg.BatchSize;
    }
}
