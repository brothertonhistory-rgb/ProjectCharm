using System;
using System.Collections.Generic;
using System.IO;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  TRANSITION LADDER — exploratory instrument, NOT part of the validation suite.
    //
    //  Companion to the make-door ladders (size / athleticism / defender). Those
    //  measured athleticism's FIRST door (make%). This measures its TRANSITION door:
    //  how much a team's athleticism edge makes it RUN (Push) instead of SETTLE into
    //  the halfcourt. One dial — the Roll J athleticism-gap → Push wire.
    //
    //  HOW IT READS — direct, no games.
    //    Roll J's run-or-not pie is a deterministic function of the team athleticism
    //    gap (offense five mean − defense five mean) once pace is held neutral. So,
    //    like the make-door ladders, this reads the answer DIRECTLY through the real
    //    engine code — it seats two fives, hands the real RollJGenerator a transition
    //    ticket per source, and reads the Push weight off the returned pie. No
    //    Governor, no possessions, no RNG. (Unlike the make-door ladders, the gap is
    //    not a parameter: the wire reads it from the seated rosters via
    //    GameState.RosterFor / LineupFor, so the ladder must SEAT lineups to realise
    //    each gap.) The Push weight read here is the CONDITIONAL Push% — the run rate
    //    of a possession that has already reached Roll J — NOT a game-level transition
    //    rate, fast-break frequency, or points.
    //
    //  WHAT IS HELD / VARIED.
    //    • Pace NEUTRAL: both coaches keep the default PaceBias 5.0, so the pace
    //      modifier contributes 0 and ALL signal is the athleticism gap. (The pace
    //      wire is independent and out of scope; it is exercised only in the
    //      combined-extreme safety rows below, never in the calibration table.)
    //    • Athleticism VARIED as a whole, per side: all five players on a side share
    //      the rung's level, all five athleticism components equal to it (via
    //      MakeAthlete), so each side's mean == its level and the gap == the level
    //      difference. Everything else flat at 50.
    //    • Fresh by construction: a never-accrued FatigueTracker returns level 0, so
    //      the fatigue discount is exactly 1.0 and offense/defense read symmetrically
    //      — the wire's internal gap equals the printed nominal gap to the decimal.
    //
    //  THE SWEEP — both signs, finer near zero.
    //    Gap = offense mean − defense mean, stepped −30..+30 (realistic team gaps are
    //    far narrower than the make door's individual −70..+70 sweep; a team mean
    //    compresses the spread). Both signs are load-bearing: a LESS athletic offense
    //    should run LESS, not merely fail to gain. Four sources, each with its own
    //    base Push the gap rides on: Rebound 0.30, FreeThrowRebound 0.08,
    //    BackcourtVictim-Steal 0.55, FrontcourtVictim-Steal 0.35.
    //
    //  BOUNDARY / SAFETY (the bounded-transfer proof).
    //    The Push↔Settle modifier is a single BOUNDED transfer: the combined delta is
    //    clamped to the room available, so the pie's mass is conserved at every gap
    //    and Pie never throws — even past a source's Settle/Push room. Proven by
    //    running BackcourtVictim (only 0.35 Settle to give) out past +43.75 and
    //    FreeThrowRebound (only 0.08 Push to give) out past −10, and by two
    //    combined-extreme rows where pace AND the gap push the same way. Reaching
    //    these rows at all — a valid pie, sum 1, no throw — is the proof.
    //
    //  Output: console tables + transition_ladder.csv in the binary output directory.
    //
    //  Invoked:  dotnet run --project src/Charm.Harness -- trtest
    // ─────────────────────────────────────────────────────────────────────────

    // The four live transition sources, in display order, each with its steal origin
    // (null for the two rebound sources). Labels are display-only.
    private static readonly (string Label, TransitionSource Source, StealOrigin? Origin)[] TrSources =
    {
        ("Rebound",      TransitionSource.Rebound,          null),
        ("FT-Reb",       TransitionSource.FreeThrowRebound, null),
        ("BCV-Steal",    TransitionSource.Steal,            StealOrigin.BackcourtVictim),
        ("FCV-Steal",    TransitionSource.Steal,            StealOrigin.FrontcourtVictim),
    };

    private static void RunTransitionExperiment(string configPath)
    {
        var cfgJ   = RollJConfig.Load(configPath);
        var cfgM   = MatchupConfig.Load(configPath);
        var cfgD   = RollDConfig.Load(configPath);
        var cfgFat = FatigueConfig.Load(configPath);

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Transition Ladder (run-or-not) ===");
        Console.WriteLine("  Direct Roll J read | pace neutral | team athleticism gap varied | fresh");
        Console.WriteLine($"  AthleticismGapScale = {cfgJ.AthleticismGapScale:F4} (linear; gap × scale) | conditional Push% per source");
        Console.WriteLine();

        // ── 1. Main calibration sweep: conditional Push% per source ───────────
        var gaps = new[] { -30, -15, -10, -5, 0, 5, 10, 15, 30 };

        Console.WriteLine(
            $"  {"Gap",4}   {TrSources[0].Label,9}  {TrSources[1].Label,9}  " +
            $"{TrSources[2].Label,9}  {TrSources[3].Label,9}");
        Console.WriteLine($"  {new string('-', 56)}");

        var rows = new List<(int Gap, double[] Push)>();
        foreach (var gap in gaps)
        {
            var (off, def) = RealizeTransitionGap(gap);
            var game = BuildTransitionGame(off, def, cfgFat, cfgD);
            var gen  = new RollJGenerator(cfgJ, cfgM, game);

            var push = new double[TrSources.Length];
            for (var s = 0; s < TrSources.Length; s++)
                push[s] = ReadJPie(gen, TrSources[s].Source, TrSources[s].Origin).Push * 100.0;

            rows.Add((gap, push));
            Console.WriteLine(
                $"  {gap,+4}   {push[0],8:F1}%  {push[1],8:F1}%  {push[2],8:F1}%  {push[3],8:F1}%");
        }

        // ── CSV ───────────────────────────────────────────────────────────────
        var csvPath = Path.Combine(AppContext.BaseDirectory, "transition_ladder.csv");
        using (var w = new StreamWriter(csvPath))
        {
            w.WriteLine("Gap,Rebound,FreeThrowRebound,BackcourtVictim,FrontcourtVictim");
            foreach (var (gap, push) in rows)
                w.WriteLine($"{gap},{push[0]:F2},{push[1]:F2},{push[2]:F2},{push[3]:F2}");
        }

        // ── 2. Boundary / safety — past a source's room, neutral pace ─────────
        Console.WriteLine();
        Console.WriteLine("  BOUNDARY / SAFETY (bounded-transfer proof) — Push, Settle, full-pie sum:");
        Console.WriteLine("    BackcourtVictim has only 0.35 Settle to give → exhausted at gap +43.75:");
        foreach (var gap in new[] { 40, 44, 50 })
            PrintBoundaryRow(cfgJ, cfgM, cfgD, cfgFat, gap,
                             TransitionSource.Steal, StealOrigin.BackcourtVictim, paceBias: 5.0);
        Console.WriteLine("    FreeThrowRebound has only 0.08 Push to give → floors at gap −10:");
        foreach (var gap in new[] { -10, -20, -30 })
            PrintBoundaryRow(cfgJ, cfgM, cfgD, cfgFat, gap,
                             TransitionSource.FreeThrowRebound, null, paceBias: 5.0);

        // ── 3. Combined-extreme — pace AND gap push the SAME way ──────────────
        // Proves the bounded transfer holds when BOTH independent modifiers stack
        // (the pace wire is held neutral everywhere else; here it is deliberately
        // driven to an extreme alongside the gap to stress the combined boundary).
        Console.WriteLine();
        Console.WriteLine("  COMBINED-EXTREME (both modifiers stacked — the combined boundary):");
        Console.WriteLine("    Fast coach (pace 10) + large POSITIVE gap, BackcourtVictim → Settle must floor at 0:");
        PrintBoundaryRow(cfgJ, cfgM, cfgD, cfgFat, 30,
                         TransitionSource.Steal, StealOrigin.BackcourtVictim, paceBias: 10.0);
        Console.WriteLine("    Slow coach (pace 1) + large NEGATIVE gap, FreeThrowRebound → Push must floor at 0:");
        PrintBoundaryRow(cfgJ, cfgM, cfgD, cfgFat, -30,
                         TransitionSource.FreeThrowRebound, null, paceBias: 1.0);

        Console.WriteLine();
        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine("  Zero-check: at gap 0 each source reads its base Push (Rebound 30.0%, FT-Reb 8.0%,");
        Console.WriteLine("    BCV 55.0%, FCV 35.0%) — confirms wiring, not magnitude.");
        Console.WriteLine("  Both signs: a less-athletic offense (negative gap) runs LESS, not merely flat.");
        Console.WriteLine("  Boundary rows reaching a valid pie (sum 1.000, no throw) IS the bounded-transfer proof.");
        Console.WriteLine();
    }

    // Realise a team athleticism gap as (offense, defense) levels, split symmetrically
    // around 50 so neither side clamps at the sweep extremes. offDelta + defDelta == gap
    // exactly (integer), so offense mean − defense mean == gap.
    private static (int Off, int Def) RealizeTransitionGap(int gap)
    {
        var defDelta = gap / 2;          // integer division truncates toward zero
        var offDelta = gap - defDelta;   // offDelta + defDelta == gap
        return (Math.Clamp(50 + offDelta, 0, 99), Math.Clamp(50 - defDelta, 0, 99));
    }

    // Build a fresh GameState with five OFF players (Home) and five DEF players (Away),
    // every athleticism component at the given level (everything else 50, via the shared
    // MakeAthlete builder). Never-accrued fatigue → zero discount. Pace neutral unless an
    // offense PaceBias is supplied (combined-extreme rows only).
    private static GameState BuildTransitionGame(
        int offLevel, int defLevel, FatigueConfig cfgFat, RollDConfig cfgD, double offensePaceBias = 5.0)
    {
        var fatigue = new FatigueTracker(cfgFat);   // never accrued → EffectiveAthleticism == raw composite
        var game = new GameState(
            new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold),
            fatigue: fatigue);

        if (offensePaceBias != 5.0)
            game.SetCoach(TeamSide.Home, new CoachProfile(paceBias: offensePaceBias));

        for (var i = 0; i < 5; i++)
        {
            game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i + 1), MakeAthlete(offLevel, id: i + 1, name: "OFF"));
            game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i + 1), MakeAthlete(defLevel, id: i + 6, name: "DEF"));
        }
        return game;
    }

    // Read Roll J's pie for one source/origin with the offense stamped as Home, and
    // return the Push weight, the Settle weight, and the full five-slice sum. Generate
    // THROWS if the bounded transfer ever lets the weights leave a valid simplex — so a
    // returned row is itself the no-throw proof.
    private static (double Push, double Settle, double Sum) ReadJPie(
        RollJGenerator gen, TransitionSource source, StealOrigin? origin)
    {
        var ctx = new TransitionContext(source) { Origin = origin, OffenseSide = TeamSide.Home };
        var pie = gen.Generate(ctx);

        double push = 0.0, settle = 0.0, sum = 0.0;
        foreach (var (outcome, weight) in pie.Slices)
        {
            sum += weight;
            if (outcome == TransitionOutcome.Push)        push   = weight;
            else if (outcome == TransitionOutcome.Settle) settle = weight;
        }
        return (push, settle, sum);
    }

    private static void PrintBoundaryRow(
        RollJConfig cfgJ, MatchupConfig cfgM, RollDConfig cfgD, FatigueConfig cfgFat,
        int gap, TransitionSource source, StealOrigin? origin, double paceBias)
    {
        var (off, def) = RealizeTransitionGap(gap);
        var game = BuildTransitionGame(off, def, cfgFat, cfgD, offensePaceBias: paceBias);
        var gen  = new RollJGenerator(cfgJ, cfgM, game);
        var (push, settle, sum) = ReadJPie(gen, source, origin);
        Console.WriteLine(
            $"      gap {gap,+4}: Push {push * 100,5:F1}% / Settle {settle * 100,5:F1}% | pie sum {sum:F3} (no throw)");
    }
}
