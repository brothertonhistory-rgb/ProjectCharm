using System;
using System.Collections.Generic;
using System.IO;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  ATHLETICISM LADDER — exploratory instrument, NOT part of the validation suite.
    //
    //  Companion to the Size Ladder, aimed at the MAKE door only (the sharpest place
    //  the shared physical curve governs athleticism). It answers ONE question:
    //  how much does an athleticism gap between shooter and defender move make%?
    //
    //  HOW IT READS — direct, no games.
    //    The size ladder runs full games because a rebound margin is emergent and can
    //    only be read that way. Make% is different: it is a deterministic function of
    //    the matchup-adjusted rating, so this instrument reads it DIRECTLY through the
    //    real engine code —
    //        effRating = Matchup.EffectiveRating(zone, shooter, defender, cfg, offAthl, defAthl)
    //        make%     = RollHConfig.MakeProbability(zone, effRating)
    //    — with no Governor, no possessions, no RNG. That isolates the make door from
    //    shot selection and from the other four athleticism doors (denial, transition,
    //    putback, entry), which is exactly the clean read calibration needs. Because no
    //    game runs, no rebound is ever contested, so Strength (which feeds rebounding)
    //    carries no bleed here — it varies with the rest of the composite, letting
    //    athleticism span its true range.
    //
    //  WHAT IS HELD / VARIED.
    //    • Skill flat at 50 on both sides → baseline 50, skillShift 0 at every zone, so
    //      the even-matchup row is a clean zero-check (make% = the documented average-
    //      shooter baseline per zone).
    //    • Size (Height/Wingspan) flat at 50 → no length term in play.
    //    • Athleticism VARIED as a whole: all five components (Strength, Speed, Quickness,
    //      FirstStep, Vertical) set equal to the rung's level, so the composite == level.
    //    • Fresh by construction: a never-accrued FatigueTracker returns level 0 for every
    //      player, so EffectiveAthleticism == the raw composite. The real fatigue path is
    //      still called (faithful), it simply applies a zero discount.
    //
    //  THE SWEEP — rail to rail, fine gaps, both extremes.
    //    Gap = offenseAthleticism − defenseAthleticism, stepped by 5 from −70 to +70.
    //    Realized so the even matchup sits at 50/50 (gap 0) and the extremes sit at the
    //    true elite-vs-weakest ends: 90 vs 20 (gap +70) and 20 vs 90 (gap −70). Only the
    //    gap drives make%, so the off/def split shown is illustrative; the gap is the truth.
    //
    //  Output: console table + athleticism_ladder.csv in the binary output directory.
    //
    //  Invoked:  dotnet run --project src/Charm.Harness -- athtest
    // ─────────────────────────────────────────────────────────────────────────

    // The five make-door zones, in declaration order.
    private static readonly ShotLocation[] AthZones =
        { ShotLocation.Three, ShotLocation.Long, ShotLocation.Mid, ShotLocation.Short, ShotLocation.Rim };

    private static void RunAthleticismExperiment(string configPath)
    {
        var cfgM   = MatchupConfig.Load(configPath);
        var cfgH   = RollHConfig.Load(configPath);
        var cfgFat = FatigueConfig.Load(configPath);

        // A never-accrued tracker: LevelFor returns 0 for any player → EffectiveAthleticism
        // is the raw composite. This is the real read-site path, with a zero fatigue discount.
        var fatigue = new FatigueTracker(cfgFat);

        // Build the rungs: gap from −70 to +70 in steps of 5.
        var rungs = new List<(int Off, int Def)>();
        for (var gap = -70; gap <= 70; gap += 5)
            rungs.Add(RealizeAthGap(gap));

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Athleticism Ladder (make door) ===");
        Console.WriteLine("  Direct make-door read | skill + size flat at 50 | athleticism (all 5 components) varied | fresh");
        Console.WriteLine($"  {rungs.Count} rungs | gap −70..+70 step 5 | even matchup = zero-check");
        Console.WriteLine();

        // Header
        Console.WriteLine(
            $"  {"Off",3}  {"Def",3}  {"Gap",4}  {"pShift",7}  {"effRtg",7}   " +
            $"{"Three",6}  {"Long",6}  {"Mid",6}  {"Short",6}  {"Rim",6}");
        Console.WriteLine($"  {new string('-', 86)}");

        var results = new List<(int Off, int Def, double PShift, double EffRtg, double[] Makes)>();

        foreach (var (off, def) in rungs)
        {
            var shooter  = MakeAthlete(off, id: 1, name: "OFF");
            var defender = MakeAthlete(def, id: 2, name: "DEF");

            var offAthl = fatigue.EffectiveAthleticism(shooter,  isDefense: false);
            var defAthl = fatigue.EffectiveAthleticism(defender, isDefense: true);

            // physicalShift is zone-independent; compute it once for display via the same primitive.
            var pShift = Matchup.GapFn(offAthl - defAthl,
                                       cfgM.PhysicalSteepness, cfgM.PhysicalExponent, cfgM.ReferenceScale);

            var makes  = new double[AthZones.Length];
            var effRtg = 0.0;
            for (var z = 0; z < AthZones.Length; z++)
            {
                var zone = AthZones[z];
                // The real matchup-aware effective rating, then the real make curve.
                effRtg   = Matchup.EffectiveRating(zone, shooter, defender, cfgM, offAthl, defAthl);
                makes[z] = cfgH.MakeProbability(zone, effRtg) * 100.0;
            }
            // With skill flat, effRtg is identical across zones (= 50 + pShift); the last is fine to show.

            results.Add((off, def, pShift, effRtg, makes));

            Console.WriteLine(
                $"  {off,3}  {def,3}  {off - def,+4}  {pShift,+7:F2}  {effRtg,7:F1}   " +
                $"{makes[0],5:F1}%  {makes[1],5:F1}%  {makes[2],5:F1}%  {makes[3],5:F1}%  {makes[4],5:F1}%");
        }

        // ── CSV ────────────────────────────────────────────────────────────
        var csvPath = Path.Combine(AppContext.BaseDirectory, "athleticism_ladder.csv");
        using (var w = new StreamWriter(csvPath))
        {
            w.WriteLine("OffAthl,DefAthl,Gap,PhysShift,EffRating,MakeThree,MakeLong,MakeMid,MakeShort,MakeRim");
            foreach (var (off, def, pShift, effRtg, makes) in results)
                w.WriteLine($"{off},{def},{off - def},{pShift:F4},{effRtg:F3}," +
                            $"{makes[0]:F2},{makes[1]:F2},{makes[2]:F2},{makes[3]:F2},{makes[4]:F2}");
        }

        Console.WriteLine();
        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine("  Even matchup (50/50, gap 0): make% should equal the average-shooter baseline per zone.");
        Console.WriteLine("  Positive gap = shooter more athletic than defender → make% rises.");
        Console.WriteLine("  Convexity check: small gaps should barely move make%; large gaps should accelerate.");
        Console.WriteLine("  Saturation: at the widest gaps effRtg runs past the curve, so make% flattens near the");
        Console.WriteLine("  zone ceiling — athleticism cannot push a shooter past the zone's make ceiling.");
        Console.WriteLine();
    }

    // Realize an athleticism gap as (offense, defense) levels.
    // Even matchup at 50/50; extremes at the true elite-vs-weakest ends (90 vs 20).
    // Only the gap drives make%; this split is illustrative.
    private static (int Off, int Def) RealizeAthGap(int gap)
    {
        if (gap >= 0)
        {
            var off = Math.Min(50 + gap, 90);
            var def = 50 - Math.Max(0, gap - 40);
            return (off, def);
        }
        else
        {
            var def = Math.Min(50 - gap, 90);
            var off = 50 - Math.Max(0, (-gap) - 40);
            return (off, def);
        }
    }

    // A player with all skill and size flat at 50 and all five athleticism components
    // (Strength included) set to `level`, so player.Athleticism == level exactly.
    private static Player MakeAthlete(int level, int id, string name)
    {
        var v = Math.Clamp(level, 0, 99);
        return new Player(name)
        {
            PlayerId            = id,
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
            Height              = 50, Wingspan = 50,
            Weight              = 50,
            Strength            = v, Speed = v, Quickness = v, FirstStep = v, Vertical = v,
            Endurance           = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
        };
    }
}
