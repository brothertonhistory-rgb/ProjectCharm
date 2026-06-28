using System;
using System.Collections.Generic;
using System.IO;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  DEFENDER LADDER — exploratory instrument, NOT part of the validation suite.
    //
    //  Third and last leg of the MAKE door. After the shooter's own skill (Session 17)
    //  and the shooter-vs-defender athletic edge (Session 18), this measures the
    //  remaining wire: how much a great ON-BALL DEFENDER takes off a shot. It answers
    //  ONE question — is the on-ball defender's make% suppression enough for an elite
    //  defender, and (if it is light) which wire is light, skill or athleticism?
    //
    //  HOW IT READS — direct, no games (same as the athleticism ladder).
    //    Make% is a deterministic function of the matchup-adjusted rating, so this reads
    //    it DIRECTLY through the real engine code —
    //        eff   = Matchup.EffectiveRating(Three, shooter, defender, cfg, offAthl, defAthl)
    //        make% = RollHConfig.MakeProbability(Three, eff)
    //    — no Governor, no possessions, no RNG. Fresh by construction (a never-accrued
    //    FatigueTracker returns the raw composite). The real fatigue path is still called;
    //    it simply applies a zero discount.
    //
    //  THE TWO ON-BALL DEFENDER WIRES (and nothing else).
    //    The matched on-ball defender touches make% through exactly two terms inside
    //    EffectiveRating: his per-zone defensive blend (at Three = 100% PerimeterDefense)
    //    and his athleticism (5-attr composite). Length is NOT on the make door (it feeds
    //    blocks only). C6/C7 in RollHGenerator are the OTHER four off-ball defenders'
    //    team contribution, applied after this base read — not the matched defender — so
    //    reading the base make% captures the full on-ball contest cleanly.
    //
    //  WHY THREE ONLY.
    //    Three is the perimeter defender's home, and the one zone where varying
    //    PerimeterDefense fully controls the skill wire (DefenseRating blends 100%
    //    perimeter at Three; 50% at Mid; 0% at Rim). Reading other zones with only
    //    PerimeterDefense varied would mix a controlled wire with an uncontrolled one,
    //    so the headline is read at Three and Three alone.
    //
    //  WHAT IS HELD / VARIED.
    //    • Nine SHOOTERS held fixed, one per cell of a 3x3 grid: shooting (Outside)
    //      {good 75 / average 50 / below-avg 25} x athleticism {good 75 / average 50 /
    //      below-avg 25}. The shooter's shooting level sets where he sits on the make-
    //      curve (how many POINTS a given suppression is worth); his athleticism sets how
    //      hard the defender's athletic wire bites him.
    //    • The DEFENDER swept turnstile(20) -> lockdown(90), step 5, in THREE passes:
    //        skill-only : PerimeterDefense = rung, athleticism held 50  (isolates skill)
    //        ath-only   : PerimeterDefense held 50, athleticism = rung  (isolates athletic)
    //        combined   : both = rung                                   (the real defender)
    //      The combined pass is the headline ("how much does an elite defender take off
    //      this shooter?"); the two single-wire passes are the diagnostic (WHY a combined
    //      drop is light — skill wire underpowered, or athleticism carrying it?).
    //
    //  OUTPUT LABEL — conditional make% only. Every number here is the conditional,
    //    pre-block, pre-foul matchup make% (the logistic output) at Three. It is NOT
    //    final FG%, shot value, or overall defensive effect: after the logistic, Roll H
    //    still applies openness, gravity, usage, passing, screening, off-ball/help
    //    defense, transition and IQ, then carves block/foul. This instrument reads only
    //    the on-ball contest.
    //
    //  Output: console (summary grid + decomposition + per-shooter sweep) and
    //          defender_ladder.csv in the binary output directory.
    //
    //  Invoked:  dotnet run --project src/Charm.Harness -- deftest
    // ─────────────────────────────────────────────────────────────────────────

    // Shooting/athleticism rungs: good / average / below-average.
    private static readonly int[] DefLevels = { 75, 50, 25 };
    private static string DefLabel(int level) => level switch
    {
        75 => "good",
        50 => "avg",
        25 => "weak",
        _  => level.ToString()
    };

    // Named reference defenders inside the sweep.
    private const int Turnstile = 20;   // weakest defender (the "open look" reference)
    private const int AvgDef    = 50;   // league-average defender (the neutral reference)
    private const int Lockdown  = 90;   // elite defender

    private static void RunDefenderExperiment(string configPath)
    {
        var cfgM   = MatchupConfig.Load(configPath);
        var cfgH   = RollHConfig.Load(configPath);
        var cfgFat = FatigueConfig.Load(configPath);

        // Never-accrued tracker → EffectiveAthleticism == raw composite (fresh), through
        // the real read-site path with a zero fatigue discount.
        var fatigue = new FatigueTracker(cfgFat);

        const ShotLocation Z = ShotLocation.Three;   // headline zone

        // Defender sweep: turnstile(20) → lockdown(90), step 5.
        var rungs = new List<int>();
        for (var r = Turnstile; r <= Lockdown; r += 5) rungs.Add(r);

        // The real engine make% at Three for a given shooter vs a given defender.
        double Make(Player shooter, Player defender)
        {
            var offAthl = fatigue.EffectiveAthleticism(shooter,  isDefense: false);
            var defAthl = fatigue.EffectiveAthleticism(defender, isDefense: true);
            var eff     = Matchup.EffectiveRating(Z, shooter, defender, cfgM, offAthl, defAthl);
            return cfgH.MakeProbability(Z, eff) * 100.0;
        }

        // Build a defender for a given pass at a given rung.
        // skill-only: PD sweeps, athleticism held 50.  ath-only: PD held 50, athleticism
        // sweeps.  combined: both sweep.
        Player Defender(string pass, int rung) => pass switch
        {
            "skill"    => MakeDefDefender(perimeterDefense: rung,   ath: AvgDef, id: 2),
            "ath"      => MakeDefDefender(perimeterDefense: AvgDef, ath: rung,   id: 2),
            "combined" => MakeDefDefender(perimeterDefense: rung,   ath: rung,   id: 2),
            _          => throw new InvalidOperationException($"Unknown pass '{pass}'.")
        };

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Defender Ladder (make door, Three) ===");
        Console.WriteLine("  Direct make-door read | 9 shooters (shoot x athleticism) | on-ball defender swept turnstile→lockdown | fresh");
        Console.WriteLine("  Conditional make% only (pre-block, pre-foul). Three = the perimeter defender's home.");
        Console.WriteLine();

        // ── SUMMARY — combined pass, the eyeball ─────────────────────────────
        // For each shooter: make% vs turnstile(20) [open], vs average(50), vs lockdown(90)
        // [elite], and the open→elite drop. This is the headline judgment surface.
        Console.WriteLine("  ── SUMMARY (combined pass): open = vs turnstile(20), elite = vs lockdown(90) ──");
        Console.WriteLine(
            $"  {"shooter (shoot/ath)",-22}  {"open%",6}  {"vs avg%",7}  {"elite%",6}  {"DROP",6}");
        Console.WriteLine($"  {new string('-', 60)}");
        foreach (var sh in DefLevels)
        foreach (var at in DefLevels)
        {
            var shooter = MakeDefShooter(outside: sh, ath: at, id: 1);
            var opn = Make(shooter, Defender("combined", Turnstile));
            var avg = Make(shooter, Defender("combined", AvgDef));
            var elt = Make(shooter, Defender("combined", Lockdown));
            Console.WriteLine(
                $"  {DefLabel(sh) + " shot / " + DefLabel(at) + " ath",-22}  " +
                $"{opn,5:F1}%  {avg,6:F1}%  {elt,5:F1}%  {opn - elt,5:F1}");
        }
        Console.WriteLine();

        // ── DECOMPOSITION — at the lockdown defender(90) ─────────────────────
        // Which wire carries the suppression? Reference = vs an average defender(50/50).
        Console.WriteLine("  ── DECOMPOSITION at lockdown(90): where does the suppression come from? ──");
        Console.WriteLine(
            $"  {"shooter (shoot/ath)",-22}  {"vs avg%",7}  {"skillOnly%",10}  {"athOnly%",8}  {"combined%",9}");
        Console.WriteLine($"  {new string('-', 66)}");
        foreach (var sh in DefLevels)
        foreach (var at in DefLevels)
        {
            var shooter = MakeDefShooter(outside: sh, ath: at, id: 1);
            var refA = Make(shooter, MakeDefDefender(AvgDef, AvgDef, 2));
            var sko  = Make(shooter, Defender("skill",    Lockdown));
            var ao   = Make(shooter, Defender("ath",      Lockdown));
            var comb = Make(shooter, Defender("combined", Lockdown));
            Console.WriteLine(
                $"  {DefLabel(sh) + " shot / " + DefLabel(at) + " ath",-22}  " +
                $"{refA,6:F1}%  {sko,9:F1}%  {ao,7:F1}%  {comb,8:F1}%");
        }
        Console.WriteLine();

        // ── PER-SHOOTER SWEEP — the full ladder, all three passes per rung ────
        Console.WriteLine("  ── FULL SWEEP per shooter (defender 20→90; make% at Three) ──");
        foreach (var sh in DefLevels)
        foreach (var at in DefLevels)
        {
            var shooter = MakeDefShooter(outside: sh, ath: at, id: 1);
            Console.WriteLine();
            Console.WriteLine($"  {DefLabel(sh)} shot ({sh}) / {DefLabel(at)} ath ({at}):");
            Console.WriteLine($"    {"defRung",7}  {"skill%",6}  {"ath%",6}  {"combined%",9}");
            foreach (var rung in rungs)
            {
                var sko  = Make(shooter, Defender("skill",    rung));
                var ao   = Make(shooter, Defender("ath",      rung));
                var comb = Make(shooter, Defender("combined", rung));
                Console.WriteLine($"    {rung,7}  {sko,5:F1}%  {ao,5:F1}%  {comb,8:F1}%");
            }
        }

        // ── CSV ──────────────────────────────────────────────────────────────
        var csvPath = Path.Combine(AppContext.BaseDirectory, "defender_ladder.csv");
        using (var w = new StreamWriter(csvPath))
        {
            w.WriteLine("ShooterShoot,ShooterAth,Pass,DefenderRung,MakeThree");
            foreach (var sh in DefLevels)
            foreach (var at in DefLevels)
            {
                var shooter = MakeDefShooter(outside: sh, ath: at, id: 1);
                foreach (var pass in new[] { "skill", "ath", "combined" })
                foreach (var rung in rungs)
                    w.WriteLine($"{sh},{at},{pass},{rung},{Make(shooter, Defender(pass, rung)):F3}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine("  Read: 'open' (vs turnstile) is the shooter winning the matchup; 'elite' (vs");
        Console.WriteLine("  lockdown) is a great on-ball defender suppressing the shot. DROP = how much the");
        Console.WriteLine("  defender takes off. Decomposition: compare skillOnly vs athOnly at lockdown — the");
        Console.WriteLine("  larger drop is the wire doing the work. Saturation: at the widest gaps the make-");
        Console.WriteLine("  curve floor/ceiling caps the result.");
        Console.WriteLine();
    }

    // A shooter: Outside = `outside`, all five athletic components = `ath`, everything
    // else neutral 50 (so player.Athleticism == ath, and the Three baseline == Outside).
    private static Player MakeDefShooter(int outside, int ath, int id)
    {
        var o = Math.Clamp(outside, 0, 99);
        var v = Math.Clamp(ath, 0, 99);
        return new Player($"SHOOT{outside}/{ath}")
        {
            PlayerId            = id,
            HierarchyRank       = 5,
            Close               = 50, Mid = 50, Outside = o, Finishing = 50,
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

    // A defender: PerimeterDefense = `perimeterDefense`, all five athletic components =
    // `ath`, everything else neutral 50. At Three, DefenseRating = 100% PerimeterDefense,
    // so PostDefense/RimProtection (held 50) do not enter the headline read.
    private static Player MakeDefDefender(int perimeterDefense, int ath, int id)
    {
        var p = Math.Clamp(perimeterDefense, 0, 99);
        var v = Math.Clamp(ath, 0, 99);
        return new Player($"DEF{perimeterDefense}/{ath}")
        {
            PlayerId            = id,
            HierarchyRank       = 5,
            Close               = 50, Mid = 50, Outside = 50, Finishing = 50,
            FreeThrow           = 50, FoulDrawing = 50,
            RimTendency         = 50, ShortTendency = 50, MidTendency = 50,
            LongTendency        = 50, ThreeTendency = 50,
            BallHandling        = 50, Passing = 50, Playmaking = 50, SelfCreation = 50,
            PostMoves           = 50, OffBallMovement = 50, Screening = 50,
            OffensiveRebounding = 50, PerimeterDefense = p, PostDefense = 50,
            RimProtection       = 50, DefensiveRebounding = 50, Steals = 50,
            HelpDefense         = 50, OffBallDefense = 50,
            Height              = 50, Wingspan = 50,
            Weight              = 50,
            Strength            = v, Speed = v, Quickness = v, FirstStep = v, Vertical = v,
            Endurance           = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
        };
    }
}
