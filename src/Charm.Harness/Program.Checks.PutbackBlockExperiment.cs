using System;
using System.IO;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  PUTBACK BLOCK LADDER — exploratory instrument, NOT part of the validation
    //  suite. The acceptance gate for the putback CONTESTER door: the door that
    //  turns the putback BLOCK rate from a single matched-defender DUEL into a
    //  FIVE-DEFENDER TEAM STACK. Every defender's length and rim defense
    //  contributes; the threats stack WITHOUT averaging (one elite rim protector is
    //  undiluted by four weak teammates), a weak defender adds nothing (per-defender
    //  floor — no drag), and the finisher's own length resists.
    //
    //  WHAT IT READS — the BLOCKED slice of the REAL RollHGenerator's rebuilt putback
    //    pie (W(pie, ShotResult.Blocked) × 100), seated through the actual putback PATH:
    //    every axis seats both lineups, stamps ReboundSlot, and calls
    //    genH.Generate(state, putback: true). It is NOT game-level block frequency and
    //    NOT the located-shot block path (Matchup.BlockWeight, unchanged).
    //
    //  SIBLING of the Session 21 PutbackConversionExperiment (pbtest), which reads the
    //    conditional MAKE rate off the same rebuilt pie. Same seating; this reads Blocked.
    //
    //  THE ORACLE IS THE PYTHON PRE-CHECK'S FIXED DECIMAL CONSTANTS, NOT THE METHOD.
    //    Each check asserts the pie-read Blocked slice against a HARD-CODED constant derived
    //    in the session's Python pre-check (re-derived under the live formula). Separately,
    //    each row cross-checks the pie-read against a DIRECT Matchup.PutbackBlockRate(...) read
    //    — a generator-to-method WIRING check only (both call the same method, so a sign error
    //    inside the method would mirror in both and pass). The fixed-constant assert catches a
    //    wrong formula; the method-vs-pie match catches wrong wiring. Both are required.
    //
    //  WHY ITS OWN FIXTURE FACTORY — the S21 Mk(...) factory hard-codes Height/Wingspan at 50
    //    and folds Vertical into athleticism, so it cannot sweep the length composite the block
    //    rate reads. MkB(...) exposes finishing, the two rim-defense inputs (PostDefense,
    //    RimProtection), and the three length dimensions (Height, Wingspan, Vertical) separately
    //    — every OTHER attribute held at 50, including Strength/Speed/Quickness/FirstStep, which
    //    the block rate never reads (length, not the athleticism composite, blocks shots).
    //
    //  HOW IT VALIDATES — five acceptance checks:
    //    1. Even anchor          — five average defenders vs an average finisher == PutbackBlocked
    //                              (7.00%) exactly: the baseline is untouched by the wire.
    //    2. Team stacking ladder — sweep the number of elite rim protectors 0→1→2→3→5 (rest
    //                              average), average finisher: block RISES 7.00 → 33.60 → 47.70 →
    //                              52.78 → 54.81% toward the team ceiling.
    //    3. No-averaging (A3) AND no-drag (A4) — TWO distinct asserts:
    //         • No-averaging: 1 elite + 4 average hits the EXACT 33.60% constant. An averaged
    //           (Sum/5) implementation lands ~12.96% — materially below. The cross-lineup
    //           equality below does NOT catch averaging on its own; only this absolute does.
    //         • No-drag: 1 elite + 4 STIFF == 1 elite + 4 average (33.60% both), and both ABOVE
    //           the 1-good-+-4-average rung (15.42%). The equality disproves weak-defender drag;
    //           without the per-defender floor the stiff row would land BELOW the average row.
    //    4. Finisher length ladder — five average defenders, finisher length swept (Finishing held
    //                              50): block FALLS 19.71 (short) → 7.00 (avg) → 5.74% (tall). A
    //                              longer finisher resists, below baseline.
    //    5. Long-perimeter contrib — three long-armed perimeter defenders (Wingspan/Height up,
    //                              rim defense at 50) + two average, average finisher → 34.83%,
    //                              above baseline. Length alone — no rim-defense skill — lifts the
    //                              block: "the block can come from everyone [long]."
    //
    //  NOTE ON THE RETIRED S22 ROUTING CHECK — the S22 instrument proved the block was keyed off
    //    the MATCHED defender (move an elite off the matched slot → block returns to baseline).
    //    That check is OBSOLETE here by design: the rate is now a team property, so an elite lifts
    //    the block from ANY defensive slot. Check 2's stacking ladder is the replacement proof.
    //
    //  Output: console (the ladders + PASS/FAIL) and putback_block_ladder.csv in the binary dir.
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

        // Length-capable fixture (same as the S22 instrument). `finishing` sets the Rim offense
        // baseline; `postDef` / `rimProt` set the defender's rim-defense blend inputs; `height` /
        // `wingspan` / `vertical` set the length composite. Strength/Speed/Quickness/FirstStep are
        // held at 50 (the block rate never reads the athleticism composite). Everything else 50.
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

        Player Neutral(string id)      => MkB(id);
        // Finisher swept on LENGTH only; Finishing stays 50 (check 4 isolates length).
        Player FinisherLen(int level)  => MkB($"FIN{level}", height: level, wingspan: level, vertical: level);
        // Elite rim protector: rim defense AND length all 90.  Stiff: those at 20.  Good: 70.
        Player Elite(string id) => MkB(id, postDef: 90, rimProt: 90, height: 90, wingspan: 90, vertical: 90);
        Player Stiff(string id) => MkB(id, postDef: 20, rimProt: 20, height: 20, wingspan: 20, vertical: 20);
        Player Good(string id)  => MkB(id, postDef: 70, rimProt: 70, height: 70, wingspan: 70, vertical: 70);
        // Long-armed perimeter defender: Wingspan/Height up, rim defense (PostDefense/RimProtection)
        // at 50, Vertical at 50 — length alone, no rim-defense skill.
        Player LongPerim(string id) => MkB(id, wingspan: 90, height: 90);

        // A defensive lineup: nElite elite rim protectors, then nStiff stiffs, rest average.
        Player[] Defense(int nElite, int nStiff = 0)
        {
            var arr = new Player[5];
            for (var i = 0; i < 5; i++)
                arr[i] = i < nElite          ? Elite($"E{i}")
                       : i < nElite + nStiff ? Stiff($"S{i}")
                                             : Neutral($"A{i}");
            return arr;
        }

        // The team putback BLOCK rate THROUGH THE REAL GENERATOR. Seats both lineups, stamps
        // ReboundSlot at offensive slot 1 (SelectedSlot left null — the putback path reads
        // ReboundSlot), and reads the rebuilt putback pie's Blocked weight. The Blocked slice is
        // the team-stack output and is independent of which slot any defender occupies.
        double PieTeam(Player rebounder, Player[] defense)
        {
            var g    = new GameState(Fouls(), ArrowState.Off, fatigue);
            Seat(g, TeamSide.Home,
                 new[] { rebounder, Neutral("o2"), Neutral("o3"), Neutral("o4"), Neutral("o5") });
            Seat(g, TeamSide.Away, defense);
            var genH = new RollHGenerator(cfgH, cfgM, g);
            var st   = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                           ReboundSlot: g.HomeLineup.SlotAt(1));
            return W(genH.Generate(st, putback: true), ShotResult.Blocked) * 100.0;
        }

        // Independent DIRECT read of the SAME team method the generator calls. Matching PieTeam to
        // the decimal proves the generator wires the rebounder + five defenders into the method.
        double DirectTeam(Player rebounder, Player[] defense)
            => Matchup.PutbackBlockRate(rebounder, defense, cfgH.PutbackBlocked, cfgM) * 100.0;

        const double WireTol = 1e-6;   // pie vs direct (same formula) — to the decimal
        const double AbsTol  = 0.05;   // pie vs Python-derived constant (rounded to 2 dp)

        // Python pre-check constants (re-derived under the live formula this session).
        const double EvenAnchor = 7.00;
        const double Elite1     = 33.60;   // 1 elite + 4 average  (an AVERAGED impl → ~12.96%)
        const double Elite2     = 47.70;
        const double Elite3     = 52.78;
        const double Elite5     = 54.81;   // five-elite wall → team ceiling
        const double Good1      = 15.42;   // 1 good (70) + 4 average — the no-drag comparison rung
        const double Stiff1     = 33.60;   // 1 elite + 4 stiff == 1 elite + 4 average (no drag)
        const double LongPerim3 = 34.83;   // 3 long-armed perimeter + 2 average (length alone)
        const double FinShort   = 19.71;   // short finisher (length 20)
        const double FinTall    = 5.74;    // tall finisher (length 90)

        var baseline0 = cfgH.PutbackBlocked * 100.0;

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Putback Block Ladder (TEAM stack, Rim) ===");
        Console.WriteLine($"  PutbackBlocked baseline  = {baseline0:F2}%   (the even-matchup anchor)");
        Console.WriteLine($"  PutbackBlockCeiling      = {cfgM.PutbackBlockCeiling * 100.0:F2}%   (five-elite-wall asymptote)");
        Console.WriteLine($"  PutbackBlockReferenceShift = {cfgM.PutbackBlockReferenceShift:F2}   (team saturation dial)");
        Console.WriteLine("  Blocked slice read THROUGH the real putback pie.");
        Console.WriteLine("  pie = W(RollHGenerator.Generate(putback:true), Blocked);  direct = independent Matchup.PutbackBlockRate read.");
        Console.WriteLine();

        // ── CHECK 1 — Even anchor ────────────────────────────────────────────
        Console.WriteLine("  ── CHECK 1: even anchor (five average defenders vs average finisher) ──");
        var evenPie = PieTeam(Neutral("reb"), Defense(0));
        var evenDir = DirectTeam(Neutral("reb"), Defense(0));
        var evenWire   = Math.Abs(evenPie - evenDir) < WireTol;
        var evenAnchor = Math.Abs(evenPie - EvenAnchor) < AbsTol && Math.Abs(evenPie - baseline0) < AbsTol;
        Console.WriteLine($"    even putback block% = {evenPie:F2}%   target {EvenAnchor:F2}%   pie==direct {(evenWire ? "ok" : "FAIL")}   anchor {(evenAnchor ? "ok" : "FAIL")}");
        pass &= evenWire && evenAnchor;
        Console.WriteLine();

        // ── CHECK 2 — Team stacking ladder ───────────────────────────────────
        Console.WriteLine("  ── CHECK 2: team stacking ladder (elite rim protectors 0→1→2→3→5, rest average) — must RISE ──");
        Console.WriteLine($"    {"# elite",-8}  {"pie%",7}  {"direct%",8}  {"target%",8}  {"match",6}");
        var ladder = new (int n, double target)[] { (0, EvenAnchor), (1, Elite1), (2, Elite2), (3, Elite3), (5, Elite5) };
        var prev = -1.0; var mono = true;
        foreach (var (n, target) in ladder)
        {
            var pie = PieTeam(Neutral("reb"), Defense(n));
            var dir = DirectTeam(Neutral("reb"), Defense(n));
            var wire = Math.Abs(pie - dir) < WireTol;
            var hit  = Math.Abs(pie - target) < AbsTol;
            Console.WriteLine($"    {n,-8}  {pie,6:F2}%  {dir,7:F2}%  {target,7:F2}%  {((wire && hit) ? "ok" : "FAIL")}");
            mono &= pie > prev + AbsTol; prev = pie; pass &= wire && hit;
        }
        Console.WriteLine($"    strictly monotonic rise: {(mono ? "ok" : "FAIL")}");
        pass &= mono;
        Console.WriteLine();

        // ── CHECK 3 — No-averaging (A3) AND no-drag (A4) ─────────────────────
        Console.WriteLine("  ── CHECK 3: no-averaging (A3) AND no-drag (A4) ──");
        var oneElite = PieTeam(Neutral("reb"), Defense(1));               // 1 elite + 4 average
        var oneStiff = PieTeam(Neutral("reb"),                            // 1 elite + 4 stiff
                          new[] { Elite("E0"), Stiff("S1"), Stiff("S2"), Stiff("S3"), Stiff("S4") });
        var oneGood  = PieTeam(Neutral("reb"),                            // 1 good (70) + 4 average
                          new[] { Good("G0"), Neutral("A1"), Neutral("A2"), Neutral("A3"), Neutral("A4") });

        // No-averaging: 1 elite + 4 average hits the EXACT 33.60% constant (an averaged Sum/5 impl
        // would land ~12.96%, materially below — the cross-lineup equality alone would NOT catch it).
        var noAvg = Math.Abs(oneElite - Elite1) < AbsTol;
        // No-drag: 1 elite + 4 stiff == 1 elite + 4 average (per-defender floor), both ABOVE 1-good.
        var noDragEq    = Math.Abs(oneStiff - oneElite) < AbsTol && Math.Abs(oneStiff - Stiff1) < AbsTol;
        var noDragAbove = oneStiff > oneGood + AbsTol && oneElite > oneGood + AbsTol;
        Console.WriteLine($"    1 elite + 4 average = {oneElite:F2}%   target {Elite1:F2}%   no-averaging {(noAvg ? "ok" : "FAIL")}   (averaged impl → ~12.96%)");
        Console.WriteLine($"    1 elite + 4 stiff   = {oneStiff:F2}%   == 1-elite-4-avg {(Math.Abs(oneStiff - oneElite) < AbsTol ? "ok" : "FAIL")}   (no drag)");
        Console.WriteLine($"    1 good  + 4 average = {oneGood:F2}%   target {Good1:F2}%   stiff & elite both above it: {(noDragAbove ? "ok" : "FAIL")}");
        pass &= noAvg && noDragEq && noDragAbove;
        Console.WriteLine();

        // ── CHECK 4 — Finisher length ladder ─────────────────────────────────
        Console.WriteLine("  ── CHECK 4: finisher length ladder (length swept, Finishing held 50; vs five average defenders) — must FALL ──");
        Console.WriteLine($"    {"finisher",-10}  {"pie%",7}  {"direct%",8}  {"target%",8}  {"match",6}");
        var finRungs = new (string lab, int lvl, double target)[]
            { ("short 20", 20, FinShort), ("avg   50", 50, EvenAnchor), ("tall  90", 90, FinTall) };
        var prevF = 1e9; var monoF = true;
        foreach (var (lab, lvl, target) in finRungs)
        {
            var pie = PieTeam(FinisherLen(lvl), Defense(0));
            var dir = DirectTeam(FinisherLen(lvl), Defense(0));
            var wire = Math.Abs(pie - dir) < WireTol;
            var hit  = Math.Abs(pie - target) < AbsTol;
            Console.WriteLine($"    {lab,-10}  {pie,6:F2}%  {dir,7:F2}%  {target,7:F2}%  {((wire && hit) ? "ok" : "FAIL")}");
            monoF &= pie < prevF - AbsTol; prevF = pie; pass &= wire && hit;
        }
        Console.WriteLine($"    monotonic fall: {(monoF ? "ok" : "FAIL")}   (a longer finisher resists, below baseline)");
        pass &= monoF;
        Console.WriteLine();

        // ── CHECK 5 — Long-perimeter contribution ────────────────────────────
        Console.WriteLine("  ── CHECK 5: long-perimeter contribution (3 long-armed perimeter defenders + 2 average; length alone, no rim skill) ──");
        var lpDefense = new[] { LongPerim("LP1"), LongPerim("LP2"), LongPerim("LP3"), Neutral("A1"), Neutral("A2") };
        var lpPie  = PieTeam(Neutral("reb"), lpDefense);
        var lpDir  = DirectTeam(Neutral("reb"), lpDefense);
        var lpWire = Math.Abs(lpPie - lpDir) < WireTol;
        var lpHit  = Math.Abs(lpPie - LongPerim3) < AbsTol;
        var lpUp   = lpPie > baseline0 + AbsTol;
        Console.WriteLine($"    3 long perimeter + 2 average = {lpPie:F2}%   direct {lpDir:F2}%   target {LongPerim3:F2}%   pie==direct {(lpWire ? "ok" : "FAIL")}   hit {(lpHit ? "ok" : "FAIL")}");
        Console.WriteLine($"    above baseline ({baseline0:F2}%): {(lpUp ? "ok" : "FAIL")}   (length contributes with no rim-defense skill)");
        pass &= lpWire && lpHit && lpUp;
        Console.WriteLine();

        // ── CSV ──────────────────────────────────────────────────────────────
        var csvPath = Path.Combine(AppContext.BaseDirectory, "putback_block_ladder.csv");
        using (var w = new StreamWriter(csvPath))
        {
            w.WriteLine("Axis,Level,PieBlock,DirectBlock");
            foreach (var n in new[] { 0, 1, 2, 3, 5 })
                w.WriteLine($"elite_count,{n},{PieTeam(Neutral("reb"), Defense(n)):F4},{DirectTeam(Neutral("reb"), Defense(n)):F4}");
            foreach (var lvl in new[] { 20, 50, 90 })
                w.WriteLine($"finisher_len,{lvl},{PieTeam(FinisherLen(lvl), Defense(0)):F4},{DirectTeam(FinisherLen(lvl), Defense(0)):F4}");
            w.WriteLine($"long_perimeter,3,{lpPie:F4},{lpDir:F4}");
        }

        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine($"  PUTBACK BLOCK LADDER: {(pass ? "PASS" : "FAIL")}");
        Console.WriteLine();
    }
}
