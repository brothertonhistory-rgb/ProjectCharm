using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // --- Batch: Roll C's (Halfcourt) rates match its pie, and every exit is a clean
    //     terminal. As of #6 the Halfcourt context is the full live 15-way loss set,
    //     so this now exercises every halfcourt-natural turnover type, not just five. ---
    private static bool RollCBatchCheck(
        RollAConfig cfg, RollCConfig cfgC, RollCGenerator genC, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} turnovers through Roll C ---");
        var rng = new SystemRng(cfg.Seed);
        var pieC = genC.Generate(state);

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


    // --- Context selection: Roll C builds the RIGHT pie for each turnover context,
    //     and the resolved rates from each match. Proves the ticket/station seam end
    //     to end: a Halfcourt ticket selects the live 15-way halfcourt loss set; a
    //     Transition ticket selects 25/15/20/35/5. Each selected pie's five MAIN
    //     slices are asserted equal to their configured weights (selection correct),
    //     then driven through Roll C (consumption correct). As of #6 the Halfcourt
    //     context is no longer the legacy 30/22/18/20/10 — the five mains now read
    //     24/18/14/16/9 with the expanded minor types live alongside them. ---
    private static bool RollCContextCheck(
        RollAConfig cfg, RollCConfig cfgC, RollCGenerator genC, PossessionState state)
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
            (TurnoverContext.EntryBackcourt, new[]
            {
                (TurnoverOutcome.BadPassDeadBall,    cfgC.EntryBackcourtBadPassDeadBall),
                (TurnoverOutcome.BadPassIntercepted, cfgC.EntryBackcourtBadPassIntercepted),
                (TurnoverOutcome.LostBallDeadBall,   cfgC.EntryBackcourtLostBallDeadBall),
                (TurnoverOutcome.LostBallLiveBall,   cfgC.EntryBackcourtLostBallLiveBall),
                (TurnoverOutcome.ShotClockViolation, cfgC.EntryBackcourtShotClockViolation),
                (TurnoverOutcome.FiveSecondInbound,  cfgC.EntryBackcourtFiveSecondInbound),
                (TurnoverOutcome.TenSecondBackcourt, cfgC.EntryBackcourtTenSecondBackcourt),
            }),
        };

        var ok = true;
        foreach (var (ctx, expected) in contexts)
        {
            var pie = genC.Generate(state, context: ctx);
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
        RollAConfig cfg, RollCConfig cfgC, RollCGenerator genC, PossessionState state)
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
        var pieEB = genC.Generate(state, context: TurnoverContext.EntryBackcourt);
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

}
