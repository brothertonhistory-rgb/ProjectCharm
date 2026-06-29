using System;
using System.IO;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  PUTBACK CONVERSION LADDER — exploratory instrument, NOT part of the
    //  validation suite. The acceptance gate for the Session 21 putback make-rate
    //  wire (the door that swaps the flat putback conversion for the finisher-vs-
    //  defender rim matchup, penalized by PutbackMakePenalty — shipped at 0, i.e.
    //  putbacks ride the FULL rim make rate).
    //
    //  WHAT IT READS — the CONDITIONAL putback make rate (make given NOT blocked and
    //    NOT fouled), recovered from the REAL RollHGenerator's rebuilt putback pie as
    //    Made / (1 − block − foul). This is the rebuilt putback PATH, not a re-derived
    //    formula: every axis seats a game, stamps ReboundSlot, and calls
    //    genH.Generate(state, putback: true). It is NOT game-level putback frequency,
    //    NOT points, and NOT the putback ATTEMPT rate (that is Roll K).
    //
    //  HOW IT VALIDATES — five acceptance checks:
    //    1. Finisher ladder  — finisher (finishing + athleticism) weak→elite vs a fixed
    //                          average matched defender: conditional make% RISES.
    //    2. Defender ladder  — fixed average finisher vs a matched defender swept weak→
    //                          elite (rim-defense + athleticism): conditional make% FALLS.
    //    3. Even anchor      — average vs average == the raw rim make% minus the penalty;
    //                          at the shipped penalty 0, == the raw rim make% exactly.
    //    4. Contester routing— the LOAD-BEARING wiring proof. Seat the rebounder in one
    //                          offensive slot; put an elite rim protector on the MATCHED
    //                          defensive slot → make% drops; move that same defender to a
    //                          NON-matched slot → make% returns to baseline. Proves the
    //                          contest is keyed off ReboundSlot (PickForOffensiveSlot),
    //                          not SelectedSlot, a neutral fallback, or a team defender.
    //                          A broken resolver still yields a clean finisher ladder, so
    //                          this is the proof that actually validates the wire.
    //    5. Picker non-regress— DIRECT identity (not a make%-axis): for an ordinary seated
    //                          shot, DefenderPicker.Pick(state) and
    //                          PickForOffensiveSlot(state, SelectedSlot) resolve the
    //                          IDENTICAL defender. Guards the one real blast radius — a
    //                          shared utility every normal shot uses.
    //
    //    Each make%-axis ALSO cross-checks the pie-read against an independent DIRECT read
    //    of the same matchup formula; matching to the decimal proves the generator computes
    //    the matchup make% (and matches the session's Python model).
    //
    //  Output: console (the four ladders + the picker identity + PASS/FAIL) and
    //          putback_conversion_ladder.csv in the binary output directory.
    //
    //  Invoked:  dotnet run --project src/Charm.Harness -- pbtest
    // ─────────────────────────────────────────────────────────────────────────

    private static void RunPutbackConversionExperiment(string configPath)
    {
        var cfgM    = MatchupConfig.Load(configPath);
        var cfgH    = RollHConfig.Load(configPath);
        var cfgD    = RollDConfig.Load(configPath);
        var cfgFat  = FatigueConfig.Load(configPath);

        // Never-accrued tracker → EffectiveAthleticism == raw composite (fresh), through the
        // real read-site path with a zero fatigue discount (same as the make-door ladders).
        var fatigue = new FatigueTracker(cfgFat);

        const ShotLocation Rim = ShotLocation.Rim;
        var pass = true;

        // ── local helpers ────────────────────────────────────────────────────
        FoulTracker Fouls() => new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);

        // Pie slice weight (no LINQ).
        static double W(Pie<ShotResult> pie, ShotResult o)
        {
            foreach (var s in pie.Slices)
                if (s.Outcome == o) return s.Weight;
            return 0.0;
        }

        // Recover the conditional (pre-carve) make% from a Roll H pie: Made / (1 − block − foul).
        static double PreCarve(Pie<ShotResult> pie)
        {
            var block = W(pie, ShotResult.Blocked);
            var foul  = W(pie, ShotResult.MadeAndFouled) + W(pie, ShotResult.MissFouled);
            var nbnf  = 1.0 - block - foul;
            return nbnf > 0.0 ? W(pie, ShotResult.Made) / nbnf : 0.0;
        }

        void Seat(GameState g, TeamSide side, Player[] five)
        {
            var roster = g.RosterFor(side);
            var lineup = g.LineupFor(side);
            for (var i = 0; i < 5; i++) roster.SetStarter(lineup.SlotAt(i + 1), five[i]);
        }

        // Full-attribute fixture. `finishing` sets the Rim baseline; `postRim` sets the
        // defender's rim-defense blend inputs (PostDefense + RimProtection); `ath` sets all
        // five athletic components (so player.Athleticism == ath). Everything else neutral 50.
        static Player Mk(string id, int finishing = 50, int postRim = 50, int ath = 50)
        {
            var f = Math.Clamp(finishing, 0, 99);
            var d = Math.Clamp(postRim,   0, 99);
            var a = Math.Clamp(ath,       0, 99);
            return new Player(id)
            {
                PlayerId            = 1,
                HierarchyRank       = 5,
                Close               = 50, Mid = 50, Outside = 50, Finishing = f,
                FreeThrow           = 50, FoulDrawing = 50,
                RimTendency         = 50, ShortTendency = 50, MidTendency = 50,
                LongTendency        = 50, ThreeTendency = 50,
                BallHandling        = 50, Passing = 50, Playmaking = 50, SelfCreation = 50,
                PostMoves           = 50, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = d,
                RimProtection       = d,  DefensiveRebounding = 50, Steals = 50,
                HelpDefense         = 50, OffBallDefense = 50,
                Height              = 50, Wingspan = 50,
                Weight              = 50,
                Strength            = a, Speed = a, Quickness = a, FirstStep = a, Vertical = a,
                Endurance           = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
            };
        }

        Player Finisher(int level) => Mk($"FIN{level}", finishing: level, ath: level);
        Player Defender(int level) => Mk($"DEF{level}", postRim: level, ath: level);
        Player Neutral(string id)  => Mk(id);

        // The conditional putback make% THROUGH THE REAL GENERATOR. Seats the offense and
        // defense lineups, stamps ReboundSlot at `offSlot` (SelectedSlot left null — the
        // putback path reads ReboundSlot, not SelectedSlot), and reads the rebuilt putback pie.
        double PieCond(Player[] offense, Player[] defense, int offSlot)
        {
            var g = new GameState(Fouls(), ArrowState.Off, fatigue);
            Seat(g, TeamSide.Home, offense);
            Seat(g, TeamSide.Away, defense);
            var genH = new RollHGenerator(cfgH, cfgM, g);
            var st   = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                           ReboundSlot: g.HomeLineup.SlotAt(offSlot));
            return PreCarve(genH.Generate(st, putback: true)) * 100.0;
        }

        // Independent DIRECT read of the same matchup formula the generator uses (finisher vs
        // defender at the Rim, penalized, clamped). Matching PieCond to the decimal proves the
        // generator's putback path computes the matchup make%, and matches the Python model.
        double DirectCond(Player finisher, Player defender)
        {
            var oa  = fatigue.EffectiveAthleticism(finisher, isDefense: false);
            var da  = fatigue.EffectiveAthleticism(defender, isDefense: true);
            var eff = Matchup.EffectiveRating(Rim, finisher, defender, cfgM, oa, da);
            return Math.Clamp(cfgH.MakeProbability(Rim, eff) - cfgH.PutbackMakePenalty, 0.0, 1.0) * 100.0;
        }

        // Convenience: one finisher in slot 1, one matched defender in slot 1, rest neutral.
        double Cond1v1(Player finisher, Player defender) =>
            PieCond(
                new[] { finisher, Neutral("h2"), Neutral("h3"), Neutral("h4"), Neutral("h5") },
                new[] { defender, Neutral("a2"), Neutral("a3"), Neutral("a4"), Neutral("a5") },
                offSlot: 1);

        const double Tol = 1e-6;   // "to the decimal" — pie-read vs direct-read (same formula)

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Putback Conversion Ladder (make rate, Rim) ===");
        Console.WriteLine($"  PutbackMakePenalty = {cfgH.PutbackMakePenalty:F4}  (0 = putback rides the full rim make rate)");
        Console.WriteLine("  Conditional make% (given not blocked, not fouled), read THROUGH the real putback pie.");
        Console.WriteLine("  pie  = recovered from RollHGenerator.Generate(putback:true);  direct = independent matchup read.");
        Console.WriteLine();

        // ── CHECK 1 — Finisher ladder ────────────────────────────────────────
        Console.WriteLine("  ── CHECK 1: finisher ladder (finisher vs FIXED average matched defender) — must RISE ──");
        Console.WriteLine($"    {"finisher",-14}  {"pie%",7}  {"direct%",8}  {"match",6}");
        var prevC1 = -1.0; var mono1 = true;
        foreach (var (lab, lvl) in new[] { ("weak  30", 30), ("avg   50", 50), ("good  70", 70), ("elite 90", 90) })
        {
            var pie = Cond1v1(Finisher(lvl), Defender(50));
            var dir = DirectCond(Finisher(lvl), Defender(50));
            var m   = Math.Abs(pie - dir) < Tol;
            Console.WriteLine($"    {lab,-14}  {pie,6:F2}%  {dir,7:F2}%  {(m ? "ok" : "FAIL")}");
            mono1 &= pie > prevC1 + Tol; prevC1 = pie; pass &= m;
        }
        Console.WriteLine($"    monotonic rise: {(mono1 ? "ok" : "FAIL")}");
        pass &= mono1;
        Console.WriteLine();

        // ── CHECK 2 — Defender ladder ────────────────────────────────────────
        Console.WriteLine("  ── CHECK 2: defender ladder (FIXED average finisher vs swept matched defender) — must FALL ──");
        Console.WriteLine($"    {"defender",-14}  {"pie%",7}  {"direct%",8}  {"match",6}");
        var prevC2 = 1e9; var mono2 = true;
        foreach (var (lab, lvl) in new[] { ("weak  30", 30), ("avg   50", 50), ("elite 90", 90) })
        {
            var pie = Cond1v1(Finisher(50), Defender(lvl));
            var dir = DirectCond(Finisher(50), Defender(lvl));
            var m   = Math.Abs(pie - dir) < Tol;
            Console.WriteLine($"    {lab,-14}  {pie,6:F2}%  {dir,7:F2}%  {(m ? "ok" : "FAIL")}");
            mono2 &= pie < prevC2 - Tol; prevC2 = pie; pass &= m;
        }
        Console.WriteLine($"    monotonic fall: {(mono2 ? "ok" : "FAIL")}");
        pass &= mono2;
        Console.WriteLine();

        // ── CHECK 3 — Even anchor ────────────────────────────────────────────
        Console.WriteLine("  ── CHECK 3: even anchor (average vs average) ──");
        var even      = Cond1v1(Finisher(50), Defender(50));
        var rawRim    = cfgH.MakeProbability(Rim, 50.0) * 100.0;            // raw rim make% at rating 50
        var expectEvn = Math.Clamp(cfgH.MakeProbability(Rim, 50.0) - cfgH.PutbackMakePenalty, 0.0, 1.0) * 100.0;
        var anchorOk  = Math.Abs(even - expectEvn) < Tol;
        var penZeroOk = Math.Abs(even - rawRim) < Tol;                      // holds because the shipped penalty is 0
        Console.WriteLine($"    even putback make% = {even:F2}%   target (rawRim − penalty) = {expectEvn:F2}%   {(anchorOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    raw rim make% (rating 50) = {rawRim:F2}%   penalty 0 → reproduces raw rim make%: {(penZeroOk ? "ok" : "FAIL")}");
        pass &= anchorOk && penZeroOk;
        Console.WriteLine();

        // ── CHECK 4 — Contester routing (the load-bearing wiring proof) ───────
        Console.WriteLine("  ── CHECK 4: contester routing — rebounder in slot 3; elite rim protector moved on/off the MATCHED slot ──");
        var avgFin   = Finisher(50);
        var elite    = Defender(90);
        // Rebounder seated in OFFENSIVE slot 3 → matched defender is DEFENSIVE slot 3.
        var offense3 = new[] { Neutral("h1"), Neutral("h2"), avgFin, Neutral("h4"), Neutral("h5") };
        // Pass A — elite on the MATCHED slot (defense slot 3): make% must DROP.
        var defElite3 = new[] { Neutral("a1"), Neutral("a2"), elite, Neutral("a4"), Neutral("a5") };
        // Pass B — same elite on a NON-matched slot (defense slot 1), slot 3 neutral: make% must RETURN to baseline.
        var defElite1 = new[] { elite, Neutral("a2"), Neutral("a3"), Neutral("a4"), Neutral("a5") };

        var baseline    = PieCond(offense3, new[] { Neutral("a1"), Neutral("a2"), Neutral("a3"), Neutral("a4"), Neutral("a5") }, offSlot: 3);
        var matchedDrop = PieCond(offense3, defElite3, offSlot: 3);
        var movedAway   = PieCond(offense3, defElite1, offSlot: 3);

        var dropOk   = matchedDrop < baseline - Tol;          // elite on the matched slot suppresses
        var returnOk = Math.Abs(movedAway - baseline) < Tol;  // elite off the matched slot → exactly baseline
        Console.WriteLine($"    baseline (slot-3 defender average)        : {baseline:F2}%");
        Console.WriteLine($"    elite on MATCHED slot 3                    : {matchedDrop:F2}%   drops below baseline: {(dropOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    same elite on NON-matched slot 1          : {movedAway:F2}%   returns to baseline: {(returnOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    → contest keyed off ReboundSlot (not SelectedSlot / neutral / team): {((dropOk && returnOk) ? "ok" : "FAIL")}");
        pass &= dropOk && returnOk;
        Console.WriteLine();

        // ── CHECK 5 — Picker non-regression (direct identity) ────────────────
        Console.WriteLine("  ── CHECK 5: picker non-regression — Pick == PickForOffensiveSlot(SelectedSlot) for ordinary shots ──");
        var pickerOk = true;
        {
            var g = new GameState(Fouls(), ArrowState.Off, fatigue);
            Seat(g, TeamSide.Home, new[] { Neutral("h1"), Neutral("h2"), Neutral("h3"), Neutral("h4"), Neutral("h5") });
            Seat(g, TeamSide.Away, new[] { Neutral("a1"), Neutral("a2"), Neutral("a3"), Neutral("a4"), Neutral("a5") });
            foreach (var n in new[] { 1, 2, 3, 4, 5 })
            {
                var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                             SelectedSlot: g.HomeLineup.SlotAt(n), ShotType: ShotLocation.Mid);
                var viaPick = DefenderPicker.Pick(st);
                var viaSlot = DefenderPicker.PickForOffensiveSlot(st, st.SelectedSlot!.Value);
                var same    = viaPick == viaSlot;   // Slot is a record struct → value equality
                pickerOk &= same;
                Console.WriteLine($"    SelectedSlot=Home {n}: Pick→{viaPick.Side} {viaPick.Number}, PickForOffensiveSlot→{viaSlot.Side} {viaSlot.Number}  {(same ? "ok" : "FAIL")}");
            }
        }
        pass &= pickerOk;
        Console.WriteLine();

        // ── CSV ──────────────────────────────────────────────────────────────
        var csvPath = Path.Combine(AppContext.BaseDirectory, "putback_conversion_ladder.csv");
        using (var w = new StreamWriter(csvPath))
        {
            w.WriteLine("Axis,Level,PieCond,DirectCond");
            foreach (var lvl in new[] { 30, 50, 70, 90 })
                w.WriteLine($"finisher,{lvl},{Cond1v1(Finisher(lvl), Defender(50)):F4},{DirectCond(Finisher(lvl), Defender(50)):F4}");
            foreach (var lvl in new[] { 30, 50, 90 })
                w.WriteLine($"defender,{lvl},{Cond1v1(Finisher(50), Defender(lvl)):F4},{DirectCond(Finisher(50), Defender(lvl)):F4}");
        }

        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine($"  PUTBACK CONVERSION LADDER: {(pass ? "PASS" : "FAIL")}");
        Console.WriteLine();
    }
}
