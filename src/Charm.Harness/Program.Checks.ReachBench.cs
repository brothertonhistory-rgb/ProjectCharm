using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S83 — REACH-TERM STRESS BENCH. Exploratory instrument, NOT part of the
//  validation suite (it asserts basketball TARGET values — a ceiling and a
//  floor Emmett ruled — and the page-only calibration principle keeps those
//  out of the suite forever).
//
//  Two benches, one machine:
//    (A) CEILING  — Pool_4244 (6'8", the real card) with Finishing swept
//        37/57/70/80/90/99, finishing over a league-median rim defender.
//        NO ceiling is asserted (Emmett's ruling, 2026-07-29): a frozen best-case
//        matchup is allowed to run past the real world's season mark. Only the
//        ORDERING is asserted here; the 77-79% mark is read on the season page.
//    (B) GUARD FLOOR — Pool_973 (5'10", real card, real Finishing 98, NO
//        overrides) against the same defender. Emmett's floor: "We don't want
//        the guards who can finish to be worthless" — made checkable at >= 50%.
//
//  ACTUAL-CARD CONTRACT. The three players are REAL pool players, regenerated
//  deterministically from the canonical world + seed at run time and asserted
//  key-for-key against the cards recorded in the S83 build prompt BEFORE a
//  single pie is read. Athleticism is DERIVED (the flat mean of Strength,
//  Speed, Quickness, FirstStep, Vertical) and is printed as a readout, never
//  authored — the Player class forbids authoring it.
//
//  RESOLVED, NOT ARRANGED. Matchup resolution is slot-guards-slot. The bench
//  stamps ten lineup slots and then ASSERTS that the engine's own pickers
//  resolve the intended shooter, the intended matched defender, and the
//  intended four helpers. A bench that arranges players and assumes the picker
//  agrees is not a bench.
//
//  CHANNEL SEPARATION. The block and foul doors do not read the reach term or
//  any of the five config values S83 moved. They DO independently read Height,
//  Wingspan and Vertical through LengthRating — so the guard's block carve is
//  legitimately HIGHER than the big's. Each row therefore prints the make term
//  and the block/foul carve separately, and (given a pre-change baseline file)
//  asserts the carve is EXACTLY unmoved. Any movement there is a bug.
//
//  Usage:
//    dotnet run --project src\Charm.Harness\Charm.Harness.csproj -- reachbench
//    dotnet run --project ... -- reachbench <world.json> <seed> [baseline.tsv]
//  Writes reach_bench.tsv beside the working directory for the pre/post join.
// ============================================================================
internal static partial class Program
{
    private const string ReachBenchWorld = "worlds/stock-d1.world.json";
    private const long   ReachBenchSeed  = 20260720L;

    // The v1 (pre-S83) term, recomputed from its own historical constants. Used only to
    // print the reach shift the engine USED to produce for the same two bodies; every
    // other pre-change column comes from an actual run of the pre-change tree.
    private const double ReachBenchV1MaxBonus = 15.0;
    private const double ReachBenchV1RimWeight = 1.00;

    private sealed record ReachBenchRow(
        string Bench, string Label, double ReachGap,
        double ShiftPre, double ShiftPost,
        double SettledMake, double Block, double Foul, double ScoreboardFgm);

    private static void RunReachBench(string engineConfigPath, string[] args)
    {
        var worldPath = args.Length > 1 ? args[1] : ReachBenchWorld;
        var seed      = args.Length > 2 && long.TryParse(args[2], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var s) ? s : ReachBenchSeed;
        var baseline  = args.Length > 3 ? args[3] : null;

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: S83 reach-term stress bench ===");
        Console.WriteLine($"  world {worldPath} | seed {seed} | live Roll H path, production Generate");
        Console.WriteLine();

        var cfgH = RollHConfig.Load(engineConfigPath);
        var cfgM = MatchupConfig.Load(engineConfigPath);

        WorldFile world;
        try { world = LoadWorld(worldPath); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        { Console.WriteLine($"REACHBENCH ERROR: {ex.Message}"); return; }

        var (pool, _) = BuildDivvyPool(world.Schools.Count, seed);   // S89: legacy mode, no identities
        Console.WriteLine($"  pool rebuilt: {pool.Count} players from {world.Schools.Count} schools");

        // ── The actual-card contract ─────────────────────────────────────────
        var ok = true;
        Player Card(int poolId, Dictionary<string, int> expected)
        {
            var p = pool.Single(x => x.PoolId == poolId).Player;
            var bad = new List<string>();
            foreach (var (k, v) in expected)
                if (ReachBenchAttr(p, k) != v)
                    bad.Add($"{k} {ReachBenchAttr(p, k)} != {v}");
            if (bad.Count > 0)
            {
                ok = false;
                Console.WriteLine($"  [FAIL] Pool_{poolId} card mismatch: {string.Join(", ", bad)}");
            }
            else
            {
                Console.WriteLine($"  [OK] Pool_{poolId} card matches key-for-key " +
                                  $"(33 keys) | derived Athleticism {p.Athleticism:F1} " +
                                  $"| reach {Matchup.Reach(p):F1}");
            }
            return p;
        }

        var shooterBig = Card(4244, ReachBenchPool4244);
        var defender   = Card(2886, ReachBenchPool2886);
        var shooterSm  = Card(973,  ReachBenchPool973);
        if (!ok) { Console.WriteLine("  card contract FAILED — bench aborted."); return; }

        var gapBig = Matchup.Reach(shooterBig) - Matchup.Reach(defender);
        var gapSm  = Matchup.Reach(shooterSm)  - Matchup.Reach(defender);
        Console.WriteLine($"  reach gaps: Pool_4244 {gapBig:+0.0;-0.0}  |  Pool_973 {gapSm:+0.0;-0.0}");
        if (gapBig != 14.0 || gapSm != -11.0)
        {
            Console.WriteLine("  [FAIL] reach gaps are not the ruled +14.0 / -11.0 — bench aborted.");
            return;
        }
        Console.WriteLine();

        var rows = new List<ReachBenchRow>();

        // ── Bench A: the ceiling ─────────────────────────────────────────────
        Console.WriteLine("  (A) CEILING — Pool_4244 (6'8\") over the median rim defender, Finishing swept:");
        ReachBenchHeader();
        foreach (var f in new[] { 37, 57, 70, 80, 90, 99 })
        {
            var row = ReachBenchRun("ceiling", $"Fin {f}", ReachBenchWithFinishing(shooterBig, f),
                                    defender, cfgH, cfgM);
            rows.Add(row);
            ReachBenchPrint(row);
        }

        // ── Bench B: the guard floor ─────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  (B) GUARD FLOOR — Pool_973 (5'10\", real Finishing 98, no overrides):");
        ReachBenchHeader();
        var guard = ReachBenchRun("guardfloor", "Fin 98 (real)", shooterSm, defender, cfgH, cfgM);
        rows.Add(guard);
        ReachBenchPrint(guard);

        // ── Hard assertions ──────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  Hard assertions:");
        var pass = true;
        void Check(string name, bool good, string detail = "")
        {
            Console.WriteLine($"    [{(good ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && good;
        }

        var ladder = rows.Where(r => r.Bench == "ceiling").ToArray();
        var rising = ladder.Zip(ladder.Skip(1), (a, b) => b.ScoreboardFgm > a.ScoreboardFgm).All(x => x);
        Check("ceiling: scoreboard rim FG% strictly increasing across the Finishing ladder", rising);

        // NO CEILING IS ASSERTED HERE — Emmett's ruling, 2026-07-29, on seeing this bench read
        // 87.4% against the real world's 77-79%: "The engine should allow for the absurd
        // extremes. If I put an all american team against the worst team possible, it should
        // 'break' the engine so to speak." The real-world mark is a SEASON figure earned against
        // a schedule of varied opponents; this bench is one frozen best case — the league's best
        // finisher, a four-inch reach edge and a 40-inch vertical, against a middling rim
        // protector, every rep. Capping it would be capping the mismatch the engine exists to
        // express. The season page is where the 77-79% mark is read (S83's run: the league's best
        // full-season shooter at 75.9%). The number is printed here for eyes, never asserted.
        Console.WriteLine($"    [--] ceiling readout (NOT asserted, per ruling): top row " +
                          $"{ladder[^1].ScoreboardFgm:P1} in the best matchup available; " +
                          $"the 77-79% real-world mark is a SEASON figure, read on the season page.");

        var fin57 = ladder.Single(r => r.Label == "Fin 57");
        Check("guard floor: not worthless — scoreboard rim FG% at or above 50%",
              guard.ScoreboardFgm >= 0.50, $"{guard.ScoreboardFgm:P1}");
        Check("guard floor: strictly below the big's Finishing-57 row (archetype ordering, " +
              "NOT a single-term attribution — the two cards differ on many axes)",
              guard.ScoreboardFgm < fin57.ScoreboardFgm,
              $"{guard.ScoreboardFgm:P1} vs {fin57.ScoreboardFgm:P1}");
        // The block carve falls as Finishing rises (visible down the ladder), so the guard
        // (Finishing 98) must be compared against the big's TOP row, not the Fin-57 row —
        // otherwise the comparison is a Finishing gap wearing a length gap's clothes.
        Check("channel separation: at like-for-like Finishing the guard's block carve is HIGHER " +
              "than the big's (untouched LengthRating door, printed so his loss is visibly the make term)",
              guard.Block > ladder[^1].Block, $"{guard.Block:P2} (Fin 98) vs {ladder[^1].Block:P2} (Fin 99)");

        // ── Cross-run: the carve must be EXACTLY unmoved by S83 ──────────────
        if (baseline is not null && File.Exists(baseline))
        {
            var pre = File.ReadAllLines(baseline).Skip(1)
                .Select(l => l.Split('\t'))
                .ToDictionary(c => c[0] + "|" + c[1],
                              c => (Block: double.Parse(c[6], CultureInfo.InvariantCulture),
                                    Foul:  double.Parse(c[7], CultureInfo.InvariantCulture),
                                    Fgm:   double.Parse(c[8], CultureInfo.InvariantCulture)),
                              StringComparer.Ordinal);
            var carveOk = true; var missing = 0;
            foreach (var r in rows)
            {
                if (!pre.TryGetValue(r.Bench + "|" + r.Label, out var b)) { missing++; continue; }
                if (b.Block != r.Block || b.Foul != r.Foul) carveOk = false;
            }
            Check($"block and foul EXACTLY equal pre-vs-post at every row ({rows.Count - missing} joined)",
                  carveOk && missing == 0, missing > 0 ? $"{missing} rows missing from baseline" : "bit-equal");

            Console.WriteLine();
            Console.WriteLine("  Pre-change vs post-change scoreboard rim FG%:");
            Console.WriteLine($"    {"row",-22}{"pre",10}{"post",10}{"delta",10}");
            foreach (var r in rows)
                if (pre.TryGetValue(r.Bench + "|" + r.Label, out var b))
                    Console.WriteLine($"    {r.Bench + " " + r.Label,-22}{b.Fgm,10:P1}{r.ScoreboardFgm,10:P1}{r.ScoreboardFgm - b.Fgm,10:+0.0%;-0.0%}");
        }
        else
        {
            Console.WriteLine("    (no baseline file given — run this bench on the PRE-CHANGE tree first,");
            Console.WriteLine("     then pass its reach_bench.tsv as the third argument to join pre/post.)");
        }

        // ── Emit for the join ────────────────────────────────────────────────
        var outPath = Path.Combine(Directory.GetCurrentDirectory(), "reach_bench.tsv");
        using (var w = new StreamWriter(outPath))
        {
            w.WriteLine("bench\tlabel\treachGap\tshiftPre\tshiftPost\tsettledMake\tblock\tfoul\tfgm");
            foreach (var r in rows)
                w.WriteLine(string.Join('\t', new[]
                {
                    r.Bench, r.Label,
                    r.ReachGap.ToString("R", CultureInfo.InvariantCulture),
                    r.ShiftPre.ToString("R", CultureInfo.InvariantCulture),
                    r.ShiftPost.ToString("R", CultureInfo.InvariantCulture),
                    r.SettledMake.ToString("R", CultureInfo.InvariantCulture),
                    r.Block.ToString("R", CultureInfo.InvariantCulture),
                    r.Foul.ToString("R", CultureInfo.InvariantCulture),
                    r.ScoreboardFgm.ToString("R", CultureInfo.InvariantCulture),
                }));
        }
        Console.WriteLine();
        Console.WriteLine($"  wrote {outPath}");
        Console.WriteLine(pass ? "  REACH BENCH: ALL ASSERTIONS OK" : "  REACH BENCH: FAILURES ABOVE");
    }

    private static void ReachBenchHeader() =>
        Console.WriteLine($"    {"row",-16}{"shift v1",10}{"shift S83",11}{"eff delta",11}" +
                          $"{"make%",9}{"block",9}{"foul",9}{"FG%",9}");

    private static void ReachBenchPrint(ReachBenchRow r) =>
        Console.WriteLine($"    {r.Label,-16}{r.ShiftPre,10:F2}{r.ShiftPost,11:F2}" +
                          $"{r.ShiftPost - r.ShiftPre,11:F2}{r.SettledMake,9:P1}" +
                          $"{r.Block,9:P2}{r.Foul,9:P2}{r.ScoreboardFgm,9:P1}");

    /// <summary>Builds the frozen scene, ASSERTS the engine resolves the intended matchup,
    /// runs the production Roll H generator, and decomposes the resulting pie.</summary>
    private static ReachBenchRow ReachBenchRun(string bench, string label, Player shooter,
                                               Player defender, RollHConfig cfgH, MatchupConfig cfgM)
    {
        var game = new GameState(new FoulTracker(7, 10));
        var flat = new Player[4];
        for (var i = 2; i <= 5; i++)
        {
            flat[i - 2] = ReachBenchFlat50($"flat{i}", 5000 + i);
            game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), flat[i - 2]);
        }
        game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(1), shooter);
        for (var i = 1; i <= 5; i++)
            game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), defender);

        var state = new PossessionState(
            PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound,
            SelectedSlot: game.HomeLineup.SlotAt(1),
            ShotType: ShotLocation.Rim,
            FastBreak: false,
            ShooterAttentionShare: 0.20,
            TeamBaseOpenness: 0.0,
            TeamSpacingLevel: 0.35,
            TeamGravityLevel: 0.35,
            UsagePressure: 0.0,
            UsageResidualPressure: 0.0,
            UsageRelief: 0.0,
            TeamConversionQuality: 0.0);

        // ── Pre-bench resolution assertions: the engine's own pickers, not the bench. ──
        var resolvedShooter = game.RosterFor(state.Offense).PlayerAt(state.SelectedSlot!.Value);
        if (!ReferenceEquals(resolvedShooter, shooter))
            throw new InvalidOperationException("reachbench: resolved shooter is not the intended card.");

        var defSlot = DefenderPicker.Pick(state);
        if (defSlot.Number != 1)
            throw new InvalidOperationException(
                $"reachbench: DefenderPicker resolved defensive slot {defSlot.Number}, expected 1.");
        if (!ReferenceEquals(game.RosterFor(state.Defense).PlayerAt(defSlot), defender))
            throw new InvalidOperationException("reachbench: matched defender is not the intended card.");

        // BlockerPicker.ResolveOffensiveLineup is INTERNAL to Charm.Engine, so the bench
        // cannot call it. It reads offRoster.PlayerAt(offLineup.SlotAt(i + 1)) for i in 0..4
        // (source-verified this session, BlockerPicker.cs) and returns null on a fast break —
        // this possession is halfcourt. The bench therefore asserts the exact mapping that
        // helper reads, through the public surface, rather than the helper itself. That is a
        // re-derivation, not the engine's own call: it proves the ten slots are stamped as
        // intended, and it would NOT catch a change inside the helper.
        var offRoster = game.RosterFor(state.Offense);
        var offLineup = game.LineupFor(state.Offense);
        if (!ReferenceEquals(offRoster.PlayerAt(offLineup.SlotAt(1)), shooter))
            throw new InvalidOperationException("reachbench: gate counterpart at slot 1 is not the shooter.");
        for (var i = 2; i <= 5; i++)
            if (!ReferenceEquals(offRoster.PlayerAt(offLineup.SlotAt(i)), flat[i - 2]))
                throw new InvalidOperationException(
                    $"reachbench: gate counterpart at slot {i} is not the flat-50 teammate.");

        // ── The production path ──────────────────────────────────────────────
        var pie = new RollHGenerator(cfgH, cfgM, game).Generate(state);
        double W(ShotResult o) => pie.Slices.First(s => s.Outcome == o).Weight;

        var made       = W(ShotResult.Made);
        var blocked    = W(ShotResult.Blocked);
        var maf        = W(ShotResult.MadeAndFouled);
        var missFouled = W(ShotResult.MissFouled);
        var nonBnf     = 1.0 - blocked - maf - missFouled;

        var gap       = Matchup.Reach(shooter) - Matchup.Reach(defender);
        var shiftPost = Matchup.HeightOverDefenderShift(ShotLocation.Rim, shooter, defender, cfgM);
        var shiftPre  = ReachBenchV1RimWeight * ReachBenchV1MaxBonus
                        * Math.Tanh(Math.Max(0.0, gap) / cfgM.HeightReferenceScale);

        return new ReachBenchRow(bench, label, gap, shiftPre, shiftPost,
            SettledMake: nonBnf > 1e-9 ? made / nonBnf : 0.0,
            Block: blocked,
            Foul: maf + missFouled,
            // An and-one is a made field goal; a shooting foul on a miss is not an attempt.
            ScoreboardFgm: (made + maf) / (1.0 - missFouled));
    }

    private static Player ReachBenchFlat50(string name, int id) => new Player(name)
    {
        PlayerId = id, HierarchyRank = 5,
        Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
        RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
        BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
        OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, DefensiveRebounding = 50,
        PerimeterDefense = 50, PostDefense = 50, RimProtection = 50, Steals = 50,
        HelpDefense = 50, OffBallDefense = 50,
        Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50, Quickness = 50,
        FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
        BasketballIQ = 50, Discipline = 50,
    };

    /// <summary>The Finishing sweep. Every other authored value is carried across verbatim —
    /// Athleticism is derived from the five physicals and therefore rides along untouched.</summary>
    private static Player ReachBenchWithFinishing(Player p, int finishing) => new Player(p.Name)
    {
        PlayerId = p.PlayerId, HierarchyRank = p.HierarchyRank,
        Close = p.Close, Mid = p.Mid, Outside = p.Outside, Finishing = finishing,
        FreeThrow = p.FreeThrow, FoulDrawing = p.FoulDrawing,
        RimTendency = p.RimTendency, ShortTendency = p.ShortTendency, MidTendency = p.MidTendency,
        LongTendency = p.LongTendency, ThreeTendency = p.ThreeTendency,
        BallHandling = p.BallHandling, Passing = p.Passing, Playmaking = p.Playmaking,
        SelfCreation = p.SelfCreation, PostMoves = p.PostMoves, OffBallMovement = p.OffBallMovement,
        Screening = p.Screening, OffensiveRebounding = p.OffensiveRebounding,
        DefensiveRebounding = p.DefensiveRebounding, PerimeterDefense = p.PerimeterDefense,
        PostDefense = p.PostDefense, RimProtection = p.RimProtection, Steals = p.Steals,
        HelpDefense = p.HelpDefense, OffBallDefense = p.OffBallDefense,
        Height = p.Height, Wingspan = p.Wingspan, Weight = p.Weight, Strength = p.Strength,
        Speed = p.Speed, Quickness = p.Quickness, FirstStep = p.FirstStep, Vertical = p.Vertical,
        Endurance = p.Endurance, Hustle = p.Hustle,
        BasketballIQ = p.BasketballIQ, Discipline = p.Discipline,
        Arrival = p.Arrival, PlayerClass = p.PlayerClass,
    };

    private static int ReachBenchAttr(Player p, string key) => key switch
    {
        "Height" => p.Height, "Wingspan" => p.Wingspan, "Weight" => p.Weight,
        "Strength" => p.Strength, "Speed" => p.Speed, "Quickness" => p.Quickness,
        "FirstStep" => p.FirstStep, "Vertical" => p.Vertical, "Endurance" => p.Endurance,
        "Hustle" => p.Hustle, "Close" => p.Close, "Mid" => p.Mid, "Outside" => p.Outside,
        "Finishing" => p.Finishing, "FreeThrow" => p.FreeThrow, "FoulDrawing" => p.FoulDrawing,
        "BallHandling" => p.BallHandling, "Passing" => p.Passing, "Playmaking" => p.Playmaking,
        "SelfCreation" => p.SelfCreation, "PostMoves" => p.PostMoves,
        "OffBallMovement" => p.OffBallMovement, "Screening" => p.Screening,
        "OffensiveRebounding" => p.OffensiveRebounding,
        "DefensiveRebounding" => p.DefensiveRebounding,
        "PerimeterDefense" => p.PerimeterDefense, "PostDefense" => p.PostDefense,
        "RimProtection" => p.RimProtection, "Steals" => p.Steals,
        "HelpDefense" => p.HelpDefense, "OffBallDefense" => p.OffBallDefense,
        "BasketballIQ" => p.BasketballIQ, "Discipline" => p.Discipline,
        _ => throw new InvalidOperationException($"reachbench: unknown card key '{key}'."),
    };

    // ── The three cards, as recorded in the S83 build prompt ─────────────────
    private static readonly Dictionary<string, int> ReachBenchPool4244 = new(StringComparer.Ordinal)
    {
        ["Height"] = 74, ["Wingspan"] = 76, ["Weight"] = 83, ["Strength"] = 63, ["Speed"] = 75,
        ["Quickness"] = 76, ["FirstStep"] = 63, ["Vertical"] = 99, ["Endurance"] = 71,
        ["Hustle"] = 85, ["Close"] = 38, ["Mid"] = 43, ["Outside"] = 39, ["Finishing"] = 53,
        ["FreeThrow"] = 62, ["FoulDrawing"] = 20, ["BallHandling"] = 14, ["Passing"] = 21,
        ["Playmaking"] = 15, ["SelfCreation"] = 11, ["PostMoves"] = 20, ["OffBallMovement"] = 59,
        ["Screening"] = 55, ["OffensiveRebounding"] = 44, ["DefensiveRebounding"] = 25,
        ["PerimeterDefense"] = 53, ["PostDefense"] = 28, ["RimProtection"] = 42, ["Steals"] = 45,
        ["HelpDefense"] = 61, ["OffBallDefense"] = 30, ["BasketballIQ"] = 63, ["Discipline"] = 63,
    };

    private static readonly Dictionary<string, int> ReachBenchPool2886 = new(StringComparer.Ordinal)
    {
        ["Height"] = 58, ["Wingspan"] = 64, ["Weight"] = 65, ["Strength"] = 59, ["Speed"] = 55,
        ["Quickness"] = 64, ["FirstStep"] = 60, ["Vertical"] = 57, ["Endurance"] = 52,
        ["Hustle"] = 56, ["Close"] = 82, ["Mid"] = 20, ["Outside"] = 25, ["Finishing"] = 58,
        ["FreeThrow"] = 62, ["FoulDrawing"] = 69, ["BallHandling"] = 23, ["Passing"] = 20,
        ["Playmaking"] = 45, ["SelfCreation"] = 38, ["PostMoves"] = 46, ["OffBallMovement"] = 18,
        ["Screening"] = 78, ["OffensiveRebounding"] = 30, ["DefensiveRebounding"] = 34,
        ["PerimeterDefense"] = 29, ["PostDefense"] = 29, ["RimProtection"] = 26, ["Steals"] = 30,
        ["HelpDefense"] = 40, ["OffBallDefense"] = 20, ["BasketballIQ"] = 41, ["Discipline"] = 56,
    };

    private static readonly Dictionary<string, int> ReachBenchPool973 = new(StringComparer.Ordinal)
    {
        ["Height"] = 48, ["Wingspan"] = 52, ["Weight"] = 68, ["Strength"] = 76, ["Speed"] = 81,
        ["Quickness"] = 83, ["FirstStep"] = 81, ["Vertical"] = 80, ["Endurance"] = 81,
        ["Hustle"] = 67, ["Close"] = 95, ["Mid"] = 32, ["Outside"] = 18, ["Finishing"] = 98,
        ["FreeThrow"] = 70, ["FoulDrawing"] = 27, ["BallHandling"] = 33, ["Passing"] = 15,
        ["Playmaking"] = 26, ["SelfCreation"] = 34, ["PostMoves"] = 44, ["OffBallMovement"] = 51,
        ["Screening"] = 98, ["OffensiveRebounding"] = 10, ["DefensiveRebounding"] = 9,
        ["PerimeterDefense"] = 14, ["PostDefense"] = 13, ["RimProtection"] = 13, ["Steals"] = 20,
        ["HelpDefense"] = 60, ["OffBallDefense"] = 25, ["BasketballIQ"] = 77, ["Discipline"] = 55,
    };
}
