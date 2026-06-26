using System;
using System.Collections.Generic;
using System.IO;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  SIZE LADDER — exploratory instrument, NOT part of the validation suite.
    //
    //  Skill and athleticism are flat at 50 for every player on both teams.
    //  Only Height and Wingspan vary (Wingspan = Height throughout — pure size
    //  signal, no arms-vs-height split yet).
    //
    //  Slots map to positions:  1=PG  2=SG  3=SF  4=PF  5=C
    //
    //  Height scale (absolute, position-agnostic):
    //    anchor  6'3" = 50  (D1 average SG / overall midpoint)
    //    each inch ≈ 3.5 rating points
    //    floor   5'6" ≈ 19   |   ceiling  7'4" ≈ 95
    //
    //  Seven named tiers — per-slot heights [PG, SG, SF, PF, C]:
    //    FLOOR   smallest viable D1 rotation player per position
    //    SMALL   well below D1 average
    //    AVERAGE D1 average height per position
    //    BIG     above D1 average
    //    ELITE   near top of real range (rare air)
    //    MAX     absolute maximum (historic outlier territory)
    //    MIN     absolute minimum (smallest viable, any position)
    //
    //  13 rows: 5 equal-matchup controls (margins must ≈ 0),
    //           6 gap rows (+1 and +2 tiers), 1 three-tier gap, 1 extreme.
    //
    //  Output: console table + size_ladder.csv in the binary output directory.
    //
    //  Invoked:  dotnet run --project src/Charm.Harness -- sizetest
    // ─────────────────────────────────────────────────────────────────────────

    // Per-position height ratings [PG, SG, SF, PF, C]
    // Formula: rating = 50 + (height_inches - 75) * 3.5
    private static readonly Dictionary<string, int[]> SizeTiers = new()
    {
        // FLOOR — smallest viable rotation player at each spot
        // PG 5'7"=22  SG 5'8"=26  SF 5'10"=33  PF 6'1"=43  C 6'5"=57
        ["FLOOR"]   = new[] { 22, 26, 33, 43, 57 },

        // SMALL — well below D1 average
        // PG 5'10"=33  SG 5'11"=36  SF 6'1"=43  PF 6'3"=50  C 6'6"=61
        ["SMALL"]   = new[] { 33, 36, 43, 50, 61 },

        // AVERAGE — D1 average height per position
        // PG 6'2"=47  SG 6'3"=50  SF 6'5.5"=59  PF 6'7"=64  C 6'9.5"=73
        ["AVERAGE"] = new[] { 47, 50, 59, 64, 73 },

        // BIG — above D1 average; bigger-than-normal starter at each spot
        // PG 6'4"=54  SG 6'5"=57  SF 6'7"=64  PF 6'9"=71  C 7'0"=82
        ["BIG"]     = new[] { 54, 57, 64, 71, 82 },

        // ELITE — near top of real range; the kind that draws NBA scouts
        // PG 6'6"=61  SG 6'7"=64  SF 6'9"=71  PF 7'1"=85  C 7'3"=92
        ["ELITE"]   = new[] { 61, 64, 71, 85, 92 },

        // MAX — absolute ceiling; historic outlier territory
        // PG 6'7"=64  SG 6'8"=68  SF 7'0"=82  PF 7'2"=89  C 7'4"=95
        ["MAX"]     = new[] { 64, 68, 82, 89, 95 },

        // MIN — absolute floor; smallest possible player at each position
        // PG 5'6"=19  SG 5'7"=22  SF 5'9"=29  PF 6'0"=40  C 6'4"=54
        ["MIN"]     = new[] { 19, 22, 29, 40, 54 },
    };

    // When true, Strength tracks Height (a bigger team is also a stronger team —
    // realistic, since size and strength correlate). When false, Strength stays flat
    // at 50 so the run isolates pure height. Strength is the heaviest single factor in
    // the rebound composite, so this toggle is the difference between the conservative
    // height-only floor and the realistic combined-size margin.
    private const bool SizeStrengthTracksHeight = true;

    private static void RunSizeExperiment(string configPath)
    {
        const int gamesPerRow = 1000;

        // (label, home tier, away tier)
        var rows = new (string Label, string Home, string Away)[]
        {
            // ── Equal-matchup controls — margins must be near zero ──
            ("FLOOR vs FLOOR",   "FLOOR",   "FLOOR"),
            ("SMALL vs SMALL",   "SMALL",   "SMALL"),
            ("AVG   vs AVG",     "AVERAGE", "AVERAGE"),
            ("BIG   vs BIG",     "BIG",     "BIG"),
            ("ELITE vs ELITE",   "ELITE",   "ELITE"),

            // ── One-tier gap ──
            ("BIG   vs AVG",     "BIG",     "AVERAGE"),
            ("AVG   vs SMALL",   "AVERAGE", "SMALL"),
            ("SMALL vs FLOOR",   "SMALL",   "FLOOR"),

            // ── Two-tier gap ──
            ("ELITE vs AVG",     "ELITE",   "AVERAGE"),
            ("ELITE vs SMALL",   "ELITE",   "SMALL"),
            ("BIG   vs FLOOR",   "BIG",     "FLOOR"),

            // ── Three-tier gap ──
            ("ELITE vs FLOOR",   "ELITE",   "FLOOR"),

            // ── The extreme ──
            ("MAX   vs MIN",     "MAX",     "MIN"),
        };

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Size Ladder ===");
        Console.WriteLine($"  Position-realistic heights | skill + athleticism flat at 50 | wingspan = height | strength {(SizeStrengthTracksHeight ? "= height" : "flat at 50")}");
        Console.WriteLine($"  {gamesPerRow:N0} games / row  |  {rows.Length} rows  |  {gamesPerRow * rows.Length:N0} total games");
        Console.WriteLine();

        var results = new List<(string Label, string Home, string Away, SizeRowTotals T)>();

        foreach (var (label, home, away) in rows)
        {
            Console.Write($"  {label,-16} ");
            var t = RunSizeRow(configPath, gamesPerRow, SizeTiers[home], SizeTiers[away]);
            results.Add((label, home, away, t));
            Console.WriteLine(" done");
        }

        // ── Console table ──────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine($"  {"Matchup",-16}  {"H.Reb",5}  {"A.Reb",5}  {"RebΔ",5}  {"H.ORb",5}  {"A.ORb",5}  {"OrbΔ",5}  {"H.Blk",5}  {"A.Blk",5}  {"BlkΔ",5}");
        Console.WriteLine($"  {new string('-', 82)}");

        foreach (var (label, home, away, t) in results)
        {
            double g    = t.Games;
            double hReb = (t.HomeOReb + t.HomeDReb) / g;
            double aReb = (t.AwayOReb + t.AwayDReb) / g;
            double hOrb = t.HomeOReb / g;
            double aOrb = t.AwayOReb / g;
            double hBlk = t.HomeBlk  / g;
            double aBlk = t.AwayBlk  / g;
            var rebDelta = hReb - aReb;
            var orbDelta = hOrb - aOrb;
            var blkDelta = hBlk - aBlk;
            Console.WriteLine(
                $"  {label,-16}  {hReb,5:F1}  {aReb,5:F1}  {rebDelta,+5:F1}  " +
                $"{hOrb,5:F1}  {aOrb,5:F1}  {orbDelta,+5:F1}  " +
                $"{hBlk,5:F2}  {aBlk,5:F2}  {blkDelta,+5:F2}");
        }

        // ── CSV ────────────────────────────────────────────────────────────
        var csvPath = Path.Combine(AppContext.BaseDirectory, "size_ladder.csv");
        WriteSizeLadderCsv(csvPath, results);
        Console.WriteLine();
        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine("  Controls (same tier both teams) should show margins near zero.");
        Console.WriteLine("  Gap rows: Home is the bigger team throughout — positive margins = size biting.");
        Console.WriteLine("  Two-tier gap vs one-tier gap: does doubling the gap more than double the margin?");
        Console.WriteLine("  MAX vs MIN: the absolute ceiling on what size alone can produce.");
        Console.WriteLine();
    }

    // ── Per-row accumulator ────────────────────────────────────────────────
    private sealed class SizeRowTotals
    {
        public long HomeOReb, HomeDReb, HomeBlk;
        public long AwayOReb, AwayDReb, AwayBlk;
        public int  Games;
    }

    // ── Run one row ────────────────────────────────────────────────────────
    private static SizeRowTotals RunSizeRow(
        string configPath, int games, int[] homeHeights, int[] awayHeights)
    {
        var cfg        = RollAConfig.Load(configPath);
        var cfgB       = RollBConfig.Load(configPath);
        var cfgC       = RollCConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);
        var cfgE       = RollEConfig.Load(configPath);
        var cfgF       = RollFConfig.Load(configPath);
        var cfgG       = RollGConfig.Load(configPath);
        var cfgH       = RollHConfig.Load(configPath);
        var cfgI       = RollIConfig.Load(configPath);
        var cfgJ       = RollJConfig.Load(configPath);
        var cfgK       = RollKConfig.Load(configPath);
        var cfgL       = RollLConfig.Load(configPath);
        var cfgM       = RollMConfig.Load(configPath);
        var cfgOffFoul = RollOffensiveFoulConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        var cfgGov     = GovernorConfig.Load(configPath);
        var cfgClock   = RollClockConfig.Load(configPath);
        var cfgEndHalf = EndOfHalfConfig.Load(configPath);
        var cfgAtten   = AttentionConfig.Load(configPath);

        var totals = new SizeRowTotals { Games = games };

        for (var seed = 1; seed <= games; seed++)
        {
            if (seed % 250 == 0) Console.Write(".");

            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));

            SeatSizeTeam(game, TeamSide.Home, idBase: 1, homeHeights);
            SeatSizeTeam(game, TeamSide.Away, idBase: 6, awayHeights);

            var governorRng = new SystemRng(seed + 1);

            var resolver = new Resolver(
                new RollAGenerator(cfg, cfgMatchup, game),
                cfg,
                new RollBGenerator(cfgB, cfgMatchup, game),
                new RollCGenerator(cfgC),
                cfgC,
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, game),
                new AttentionGenerator(cfgAtten, game),
                new RollFGenerator(cfgF, cfgMatchup, game),
                new RollGGenerator(cfgG, cfgMatchup, game),
                new RollHGenerator(cfgH, cfgMatchup, game),
                new RollIGenerator(cfgI, cfgMatchup, game),
                new RollJGenerator(cfgJ, cfgMatchup, game),
                new RollKGenerator(cfgK, cfgMatchup, game),
                new RollLGenerator(cfgL, game),
                new RollMGenerator(cfgM, cfgMatchup, game),
                new RollOffensiveFoulGenerator(cfgOffFoul),
                cfgMatchup,
                game,
                new SystemRng(seed));

            var governor = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndHalf);
            var first    = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1);
            var result   = governor.Run(first);

            foreach (var r in result.Possessions)
            {
                if (r.Offense == TeamSide.Home) totals.HomeOReb += r.OrbWon;
                else                            totals.AwayOReb += r.OrbWon;

                if (r.EndLabel == "DefensiveRebound")
                {
                    if (r.Defense == TeamSide.Home) totals.HomeDReb++;
                    else                            totals.AwayDReb++;
                }

                if (r.Defense == TeamSide.Home) totals.HomeBlk += r.BlkCount;
                else                            totals.AwayBlk += r.BlkCount;
            }
        }

        return totals;
    }

    // Seat a position-realistic team. slots[i] = height for position i (0=PG ... 4=C).
    // Wingspan = height. Everything else flat at 50.
    private static void SeatSizeTeam(
        GameState game, TeamSide side, int idBase, int[] heights)
    {
        var lineup = game.LineupFor(side);
        var roster = game.RosterFor(side);
        string[] pos = { "PG", "SG", "SF", "PF", "C" };

        for (var i = 0; i < 5; i++)
        {
            var h = Math.Clamp(heights[i], 0, 99);
            roster.SetStarter(
                lineup.SlotAt(i + 1),
                new Player($"{side}{pos[i]}")
                {
                    PlayerId            = idBase + i,
                    HierarchyRank       = 5,
                    Close               = 50, Mid = 50, Outside = 50, Finishing = 50,
                    FreeThrow           = 50, FoulDrawing = 50,
                    RimTendency         = 50, ShortTendency = 50, MidTendency = 50,
                    LongTendency        = 50, ThreeTendency = 50,
                    BallHandling        = 50, Passing = 50, Playmaking = 50, SelfCreation = 50,
                    PostMoves           = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50,
                    RimProtection       = 50, DefensiveRebounding = 50, Steals = 50,
                    HelpDefense         = 50, OffBallDefense = 50,
                    Height              = h,  Wingspan = h,
                    Weight              = 50, Strength = SizeStrengthTracksHeight ? h : 50,
                    Speed               = 50, Quickness = 50, FirstStep = 50, Vertical = 50,
                    Endurance           = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                });
        }
    }

    private static void WriteSizeLadderCsv(
        string path,
        List<(string Label, string Home, string Away, SizeRowTotals T)> rows)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("Matchup,HomeTier,AwayTier,Games," +
                    "HomeReb,AwayReb,RebMargin," +
                    "HomeOReb,AwayOReb,OrebMargin," +
                    "HomeBlk,AwayBlk,BlkMargin");

        foreach (var (label, home, away, t) in rows)
        {
            double g    = t.Games;
            double hReb = (t.HomeOReb + t.HomeDReb) / g;
            double aReb = (t.AwayOReb + t.AwayDReb) / g;
            double hOrb = t.HomeOReb / g;
            double aOrb = t.AwayOReb / g;
            double hBlk = t.HomeBlk  / g;
            double aBlk = t.AwayBlk  / g;
            w.WriteLine($"{label},{home},{away},{t.Games}," +
                        $"{hReb:F3},{aReb:F3},{hReb - aReb:F3}," +
                        $"{hOrb:F3},{aOrb:F3},{hOrb - aOrb:F3}," +
                        $"{hBlk:F4},{aBlk:F4},{hBlk - aBlk:F4}");
        }
    }
}
