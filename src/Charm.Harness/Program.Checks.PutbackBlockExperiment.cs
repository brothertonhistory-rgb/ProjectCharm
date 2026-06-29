using System;
using System.IO;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  PUTBACK BLOCK LADDER — exploratory instrument, NOT part of the validation
    //  suite. The acceptance gate for the putback BLOCK-rate wire (the door that
    //  swaps the flat PutbackBlocked constant for the finisher-vs-defender rim
    //  length / shot-blocking matchup — a long, rangy rim protector swats more
    //  putbacks; a small defender swats fewer).
    //
    //  WHAT IT READS — the BLOCKED slice of the REAL RollHGenerator's rebuilt
    //    putback pie (W(pie, ShotResult.Blocked) × 100). This is the rebuilt putback
    //    PATH, not a re-derived formula: every axis seats a game, stamps ReboundSlot,
    //    and calls genH.Generate(state, putback: true). It is NOT game-level block
    //    frequency and NOT the located-shot block path.
    //
    //  SIBLING of the Session 21 PutbackConversionExperiment (pbtest), which reads the
    //    conditional MAKE rate off the same rebuilt pie. Same seating; this reads the
    //    Blocked slice instead.
    //
    //  WHY ITS OWN FIXTURE FACTORY — the S21 Mk(...) factory exposes only finishing /
    //    postRim / ath and HARD-CODES Height and Wingspan at 50 (and folds Vertical into
    //    athleticism), so it cannot sweep the length composite the block rate reads. This
    //    instrument defines MkB(...), exposing finishing, the two rim-defense inputs
    //    (PostDefense, RimProtection), and the three length dimensions (Height, Wingspan,
    //    Vertical) separately — every OTHER attribute held at 50, including Strength /
    //    Speed / Quickness / FirstStep, which the block rate never reads (length, not the
    //    athleticism composite, blocks shots).
    //
    //  HOW IT VALIDATES — five acceptance checks:
    //    1. Defender block ladder — average rebounder vs a matched defender swept weak→
    //                               elite on length + rim defense together: block RISES.
    //    2. Finisher length ladder— fixed average matched defender, finisher swept on
    //                               length only (Finishing held 50): block FALLS — a longer
    //                               finisher is harder to block.
    //    3. Even anchor           — average vs average == PutbackBlocked exactly: the
    //                               baseline is untouched by the wire.
    //    4. Contester routing     — the LOAD-BEARING wiring proof. Seat the rebounder in one
    //                               offensive slot; put an elite long rim protector on the
    //                               MATCHED defensive slot → block RISES; move that same
    //                               defender to a NON-matched slot → block returns to exactly
    //                               PutbackBlocked. Proves the contest is keyed off
    //                               ReboundSlot (PickForOffensiveSlot), not SelectedSlot, a
    //                               neutral fallback, or a team defender.
    //    5. Wingspan isolation    — raise the defender's WINGSPAN only (90, all else 50) vs an
    //                               average rebounder → block rises above baseline. Proves
    //                               Emmett's named attribute is live through the length
    //                               composite, not just "some defender attribute."
    //
    //    Each make-of-the-matchup axis ALSO cross-checks the pie-read Blocked slice against an
    //    independent DIRECT read of Matchup.BlockWeight(...) (the same formula); matching to
    //    the decimal proves the generator computes the matchup block (and matches the
    //    session's Python model).
    //
    //  Output: console (the ladders + the routing proof + PASS/FAIL) and
    //          putback_block_ladder.csv in the binary output directory.
    //
    //  Invoked:  dotnet run --project src/Charm.Harness -- pbblocktest
    // ─────────────────────────────────────────────────────────────────────────

    private static void RunPutbackBlockExperiment(string configPath)
    {
        var cfgM   = MatchupConfig.Load(configPath);
        var cfgH   = RollHConfig.Load(configPath);
        var cfgD   = RollDConfig.Load(configPath);
        var cfgFat = FatigueConfig.Load(configPath);

        // Never-accrued tracker → EffectiveAthleticism == raw composite (fresh); athleticism
        // does not feed the block rate, but the seated path constructs the generator the same
        // way the make instrument does.
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

        void Seat(GameState g, TeamSide side, Player[] five)
        {
            var roster = g.RosterFor(side);
            var lineup = g.LineupFor(side);
            for (var i = 0; i < 5; i++) roster.SetStarter(lineup.SlotAt(i + 1), five[i]);
        }

        // Length-capable fixture. `finishing` sets the Rim offense baseline; `postDef` and
        // `rimProt` set the defender's rim-defense blend inputs; `height` / `wingspan` /
        // `vertical` set the length composite. Strength/Speed/Quickness/FirstStep are held
        // at 50 (the block rate never reads the athleticism composite). Everything else 50.
        static Player MkB(string id, int finishing = 50, int postDef = 50, int rimProt = 50,
                          int height = 50, int wingspan = 50, int vertical = 50)
        {
            var f  = Math.Clamp(finishing, 0, 99);
            var pd = Math.Clamp(postDef,   0, 99);
            var rp = Math.Clamp(rimProt,   0, 99);
            var h  = Math.Clamp(height,    0, 99);
            var w  = Math.Clamp(wingspan,  0, 99);
            var v  = Math.Clamp(vertical,  0, 99);
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
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = pd,
                RimProtection       = rp, DefensiveRebounding = 50, Steals = 50,
                HelpDefense         = 50, OffBallDefense = 50,
                Height              = h,  Wingspan = w,
                Weight              = 50,
                Strength            = 50, Speed = 50, Quickness = 50, FirstStep = 50, Vertical = v,
                Endurance           = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
            };
        }

        // Defender swept on length + rim defense together (check 1 / the elite blocker).
        Player DefenderRung(int level) => MkB($"DEF{level}", postDef: level, rimProt: level,
                                              height: level, wingspan: level, vertical: level);
        // Finisher swept on LENGTH only; Finishing stays 50 (check 2 isolates length).
        Player FinisherLen(int level)  => MkB($"FIN{level}", height: level, wingspan: level, vertical: level);
        Player Neutral(string id)      => MkB(id);

        // The BLOCKED slice THROUGH THE REAL GENERATOR. Seats both lineups, stamps ReboundSlot
        // at `offSlot` (SelectedSlot left null — the putback path reads ReboundSlot), and reads
        // the rebuilt putback pie's Blocked weight.
        double PieBlock(Player[] offense, Player[] defense, int offSlot)
        {
            var g    = new GameState(Fouls(), ArrowState.Off, fatigue);
            Seat(g, TeamSide.Home, offense);
            Seat(g, TeamSide.Away, defense);
            var genH = new RollHGenerator(cfgH, cfgM, g);
            var st   = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                           ReboundSlot: g.HomeLineup.SlotAt(offSlot));
            return W(genH.Generate(st, putback: true), ShotResult.Blocked) * 100.0;
        }

        // Independent DIRECT read of the same matchup formula the generator uses (rebounder vs
        // matched defender at the Rim, off the flat PutbackBlocked baseline). Matching PieBlock
        // to the decimal proves the generator computes the matchup block.
        double DirectBlock(Player shooter, Player defender)
            => Matchup.BlockWeight(Rim, shooter, defender, cfgH.PutbackBlocked, cfgM) * 100.0;

        // Convenience: one finisher in slot 1, one matched defender in slot 1, rest neutral.
        double Block1v1(Player shooter, Player defender) =>
            PieBlock(
                new[] { shooter, Neutral("h2"), Neutral("h3"), Neutral("h4"), Neutral("h5") },
                new[] { defender, Neutral("a2"), Neutral("a3"), Neutral("a4"), Neutral("a5") },
                offSlot: 1);

        const double Tol = 1e-6;   // "to the decimal" — pie-read vs direct-read (same formula)

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Putback Block Ladder (block rate, Rim) ===");
        Console.WriteLine($"  PutbackBlocked baseline = {cfgH.PutbackBlocked * 100.0:F2}%  (the even-matchup anchor)");
        Console.WriteLine("  Blocked slice read THROUGH the real putback pie.");
        Console.WriteLine("  pie  = W(RollHGenerator.Generate(putback:true), Blocked);  direct = independent Matchup.BlockWeight read.");
        Console.WriteLine();

        // ── CHECK 1 — Defender block ladder ──────────────────────────────────
        Console.WriteLine("  ── CHECK 1: defender ladder (avg rebounder vs matched defender swept length+rim-D) — must RISE ──");
        Console.WriteLine($"    {"defender",-14}  {"pie%",7}  {"direct%",8}  {"match",6}");
        var prevC1 = -1.0; var mono1 = true;
        foreach (var (lab, lvl) in new[] { ("weak  30", 30), ("avg   50", 50), ("good  70", 70), ("elite 90", 90) })
        {
            var pie = Block1v1(Neutral("reb"), DefenderRung(lvl));
            var dir = DirectBlock(Neutral("reb"), DefenderRung(lvl));
            var m   = Math.Abs(pie - dir) < Tol;
            Console.WriteLine($"    {lab,-14}  {pie,6:F2}%  {dir,7:F2}%  {(m ? "ok" : "FAIL")}");
            mono1 &= pie > prevC1 + Tol; prevC1 = pie; pass &= m;
        }
        Console.WriteLine($"    monotonic rise: {(mono1 ? "ok" : "FAIL")}   (expect ~6.10 → 7.00 → 13.92 → 25.35)");
        pass &= mono1;
        Console.WriteLine();

        // ── CHECK 2 — Finisher length ladder ─────────────────────────────────
        Console.WriteLine("  ── CHECK 2: finisher length ladder (length swept, Finishing held 50; vs avg defender) — must FALL ──");
        Console.WriteLine($"    {"finisher",-14}  {"pie%",7}  {"direct%",8}  {"match",6}");
        var prevC2 = 1e9; var mono2 = true;
        foreach (var (lab, lvl) in new[] { ("short 30", 30), ("avg   50", 50), ("tall  90", 90) })
        {
            var pie = Block1v1(FinisherLen(lvl), Neutral("def"));
            var dir = DirectBlock(FinisherLen(lvl), Neutral("def"));
            var m   = Math.Abs(pie - dir) < Tol;
            Console.WriteLine($"    {lab,-14}  {pie,6:F2}%  {dir,7:F2}%  {(m ? "ok" : "FAIL")}");
            mono2 &= pie < prevC2 - Tol; prevC2 = pie; pass &= m;
        }
        Console.WriteLine($"    monotonic fall: {(mono2 ? "ok" : "FAIL")}   (expect ~12.27 → 7.00 → 5.03)");
        pass &= mono2;
        Console.WriteLine();

        // ── CHECK 3 — Even anchor ────────────────────────────────────────────
        Console.WriteLine("  ── CHECK 3: even anchor (average vs average) ──");
        var even      = Block1v1(Neutral("reb"), Neutral("def"));
        var baseline0 = cfgH.PutbackBlocked * 100.0;
        var anchorOk  = Math.Abs(even - baseline0) < Tol;
        Console.WriteLine($"    even putback block% = {even:F2}%   PutbackBlocked = {baseline0:F2}%   {(anchorOk ? "ok" : "FAIL")}");
        pass &= anchorOk;
        Console.WriteLine();

        // ── CHECK 4 — Contester routing (the load-bearing wiring proof) ──────
        Console.WriteLine("  ── CHECK 4: contester routing — rebounder in slot 3; elite long rim protector moved on/off the MATCHED slot ──");
        var elite    = DefenderRung(90);
        // Rebounder seated in OFFENSIVE slot 3 → matched defender is DEFENSIVE slot 3.
        var offense3 = new[] { Neutral("h1"), Neutral("h2"), Neutral("reb3"), Neutral("h4"), Neutral("h5") };
        // Pass A — elite on the MATCHED slot (defense slot 3): block must RISE.
        var defElite3 = new[] { Neutral("a1"), Neutral("a2"), elite, Neutral("a4"), Neutral("a5") };
        // Pass B — same elite on a NON-matched slot (defense slot 1), slot 3 neutral: block must RETURN to baseline.
        var defElite1 = new[] { elite, Neutral("a2"), Neutral("a3"), Neutral("a4"), Neutral("a5") };

        var baseline    = PieBlock(offense3, new[] { Neutral("a1"), Neutral("a2"), Neutral("a3"), Neutral("a4"), Neutral("a5") }, offSlot: 3);
        var matchedRise = PieBlock(offense3, defElite3, offSlot: 3);
        var movedAway   = PieBlock(offense3, defElite1, offSlot: 3);

        var riseOk   = matchedRise > baseline + Tol;          // elite on the matched slot lifts the block rate
        var returnOk = Math.Abs(movedAway - baseline) < Tol;  // elite off the matched slot → exactly baseline
        Console.WriteLine($"    baseline (slot-3 defender average)        : {baseline:F2}%");
        Console.WriteLine($"    elite on MATCHED slot 3                    : {matchedRise:F2}%   rises above baseline: {(riseOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    same elite on NON-matched slot 1          : {movedAway:F2}%   returns to baseline: {(returnOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    → contest keyed off ReboundSlot (not SelectedSlot / neutral / team): {((riseOk && returnOk) ? "ok" : "FAIL")}");
        pass &= riseOk && returnOk;
        Console.WriteLine();

        // ── CHECK 5 — Wingspan isolation ─────────────────────────────────────
        Console.WriteLine("  ── CHECK 5: wingspan isolation — defender Wingspan only (90, all else 50) vs avg rebounder — must rise above baseline ──");
        var wingOnly = MkB("WING", wingspan: 90);
        var wingPie  = Block1v1(Neutral("reb"), wingOnly);
        var wingDir  = DirectBlock(Neutral("reb"), wingOnly);
        var wingM    = Math.Abs(wingPie - wingDir) < Tol;
        var wingUp   = wingPie > baseline0 + Tol;
        Console.WriteLine($"    wingspan-only defender                    : {wingPie:F2}%   direct {wingDir:F2}%   match {(wingM ? "ok" : "FAIL")}");
        Console.WriteLine($"    above PutbackBlocked baseline ({baseline0:F2}%)     : {(wingUp ? "ok" : "FAIL")}   (expect ~9.63)");
        pass &= wingM && wingUp;
        Console.WriteLine();

        // ── CSV ──────────────────────────────────────────────────────────────
        var csvPath = Path.Combine(AppContext.BaseDirectory, "putback_block_ladder.csv");
        using (var w = new StreamWriter(csvPath))
        {
            w.WriteLine("Axis,Level,PieBlock,DirectBlock");
            foreach (var lvl in new[] { 30, 50, 70, 90 })
                w.WriteLine($"defender,{lvl},{Block1v1(Neutral("reb"), DefenderRung(lvl)):F4},{DirectBlock(Neutral("reb"), DefenderRung(lvl)):F4}");
            foreach (var lvl in new[] { 30, 50, 90 })
                w.WriteLine($"finisher,{lvl},{Block1v1(FinisherLen(lvl), Neutral("def")):F4},{DirectBlock(FinisherLen(lvl), Neutral("def")):F4}");
        }

        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine($"  PUTBACK BLOCK LADDER: {(pass ? "PASS" : "FAIL")}");
        Console.WriteLine();
    }
}
