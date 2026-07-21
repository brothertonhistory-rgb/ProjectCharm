using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 68 (Session 62) — per-man NON-SHOOTING (reach-in) foul model
//  (Discipline Effect B).
//
//  The rate. Each defender carries his own reach-in propensity — Discipline
//  PRIMARY (symmetric about 50, low D -> more fouls), a SMALL athleticism
//  secondary, a SLIGHT perimeter lean. Rolls A/B/F scale their pressure-bent
//  foul share by the five defenders' aggregate propensity (exactly 1.0 at
//  five-average, so today's rate is preserved; a hack-happy lineup ADDS fouls).
//  The retired Hustle foul nudge no longer touches the rate.
//
//  The committer. Every non-shooting foul emits one attributable event at the
//  sole increment site (DefensiveFoulCharge.Resolve). The harness draws the
//  culprit post-hoc: reach-in fouls (A/B/F) in proportion to the full
//  propensity; situational fouls (I/J/K/M) on the Discipline factor alone.
//
//  Golden fixture tools/nonshooting_foul_golden.json is emitted by
//  tools/nonshooting_foul_oracle.py. Parity binds to the ENGINE's named statics
//  (Matchup.ReachIn*), never a formula copy — a disagreement is a PORT BUG, and
//  THE ORACLE WINS. Magnitudes are page-tuned later, never suite-asserted, so the
//  golden is replayed under the FIXTURE's config block (decoupled from the live
//  config.json tuning); the real-path checks below load the LIVE config.
//
//  Sub-checks:
//    (1) Golden parity — every factor/orientation/propensity/aggregate/committer
//        row, |Δ| <= 1e-12, against the engine statics.
//    (2) Formula invariants — symmetric about 50, neutral at 50, monotone,
//        strictly positive.
//    (3) Anchor — five identical average defenders -> per-man aggregate == 1.0.
//    (4) ★ SUM property — swapping one average defender for a hacker RAISES the
//        team aggregate above 1.0 (adds fouls; does NOT merely redistribute).
//    (5) Stackable-linear — two hackers give ~2x the one-hacker delta.
//    (6) Draw ∝ propensity — Monte-Carlo the real committer draw: the hacker is
//        drawn most, the lockdown least, and reach-in frequencies track the
//        propensity proportions.
//    (7) Completeness — every DefensiveFoulCharge.Resolve emits exactly one event
//        with IsReachIn == flavor.HasValue, below AND across the bonus.
//    (8) Config guards — out-of-range spans/luckfloor/scale throw; defaults load.
// ============================================================================
internal static partial class Program
{
    private static bool Phase68NonShootingFoulCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 68: per-man non-shooting (reach-in) foul model (golden parity + invariants + anchor + SUM + stackable + draw + completeness + config guards) ---");
        var pass = true;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var liveCfg = MatchupConfig.Load(configPath);
        var cfgD    = RollDConfig.Load(configPath);

        // A uniform-body template: every physical/skill rating at 50, so a lineup of these
        // shares one postness -> perimeter orientation is exactly 0.5 for all, isolating the
        // Discipline effect in the aggregate checks. Discipline is set per-copy.
        static Player Body(int discipline) => new Player("nsf")
        {
            Height=50, Wingspan=50, Strength=50, Vertical=50,
            DefensiveRebounding=50, OffensiveRebounding=50, RimProtection=50, Finishing=50, FreeThrow=50,
            Outside=50, ThreeTendency=50, RimTendency=50, BallHandling=50, FoulDrawing=50,
            Close=50, Mid=50, ShortTendency=50, MidTendency=50, LongTendency=50,
            Passing=50, Playmaking=50, SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
            PerimeterDefense=50, PostDefense=50, Steals=50,
            Weight=50, Speed=50, Quickness=50, FirstStep=50,
            Endurance=50, Hustle=50, BasketballIQ=50, Discipline=discipline, HelpDefense=50, OffBallDefense=50,
        };

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/nonshooting_foul_golden.json.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity (all rows, |Δ| <= 1e-12; binds to Matchup.ReachIn* statics):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "nonshooting_foul_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var cfgBlock = root.GetProperty("config");

            // Rebuild the fixture's config so parity binds the FORMULA, not live tuning.
            var goldCfg = new MatchupConfig
            {
                ReachInDiscSpan      = cfgBlock.GetProperty("ReachInDiscSpan").GetDouble(),
                ReachInAthSpan       = cfgBlock.GetProperty("ReachInAthSpan").GetDouble(),
                ReachInPerimSpan     = cfgBlock.GetProperty("ReachInPerimSpan").GetDouble(),
                ReachInLuckFloor     = cfgBlock.GetProperty("ReachInLuckFloor").GetDouble(),
                ReachInPostnessScale = cfgBlock.GetProperty("ReachInPostnessScale").GetDouble(),
            };
            const double Tol = 1e-12;
            var worst = 0.0; var rows = 0;

            void Row(double got, double want) { worst = Math.Max(worst, Math.Abs(got - want)); rows++; }

            foreach (var c in root.GetProperty("discFactor").EnumerateArray())
                Row(Matchup.ReachInDisciplineFactor(c.GetProperty("D").GetDouble(), goldCfg), c.GetProperty("want").GetDouble());
            foreach (var c in root.GetProperty("athFactor").EnumerateArray())
                Row(Matchup.ReachInAthFactor(c.GetProperty("A").GetDouble(), goldCfg), c.GetProperty("want").GetDouble());
            foreach (var c in root.GetProperty("perimFactor").EnumerateArray())
                Row(Matchup.ReachInPerimFactor(c.GetProperty("o").GetDouble(), goldCfg), c.GetProperty("want").GetDouble());
            foreach (var c in root.GetProperty("perimOrient").EnumerateArray())
                Row(Matchup.ReachInPerimOrientation(c.GetProperty("P").GetDouble(), c.GetProperty("mean").GetDouble(), goldCfg), c.GetProperty("want").GetDouble());
            foreach (var c in root.GetProperty("propensity").EnumerateArray())
                Row(Matchup.ReachInPropensity(c.GetProperty("D").GetDouble(), c.GetProperty("A").GetDouble(), c.GetProperty("o").GetDouble(), goldCfg), c.GetProperty("want").GetDouble());

            // Aggregate rows: the ratio built from the named propensity + reference statics
            // (direct-o form, matching the oracle; the Postness-derived path is checked in (3)-(5)).
            var refProp = Matchup.ReachInReferencePropensity(goldCfg);
            foreach (var c in root.GetProperty("aggregate").EnumerateArray())
            {
                var defs = c.GetProperty("defenders");
                var sum = 0.0; var n = 0;
                foreach (var d in defs.EnumerateArray())
                {
                    sum += Matchup.ReachInPropensity(d.GetProperty("D").GetDouble(), d.GetProperty("A").GetDouble(), d.GetProperty("o").GetDouble(), goldCfg);
                    n++;
                }
                Row(sum / (n * refProp), c.GetProperty("want").GetDouble());
            }
            foreach (var c in root.GetProperty("committerReachIn").EnumerateArray())
                Row(Matchup.ReachInPropensity(c.GetProperty("D").GetDouble(), c.GetProperty("A").GetDouble(), c.GetProperty("o").GetDouble(), goldCfg), c.GetProperty("want").GetDouble());
            foreach (var c in root.GetProperty("committerSituational").EnumerateArray())
                Row(Matchup.ReachInDisciplineFactor(c.GetProperty("D").GetDouble(), goldCfg), c.GetProperty("want").GetDouble());

            Check($"golden parity, {rows} rows", worst <= Tol, $"worst |Δ| = {worst:e2} (tol {Tol:e0})");
        }
        catch (Exception ex)
        {
            Check("golden parity", false, ex.Message);
        }

        // ----------------------------------------------------------------
        // (2) Formula invariants (against the LIVE config).
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) Formula invariants (live config):");
        {
            // Symmetric about 50: discFactor(50+k) + discFactor(50-k) == 2.
            var symmetric = new[] { 1, 10, 25, 40, 49 }
                .All(k => Math.Abs(Matchup.ReachInDisciplineFactor(50 + k, liveCfg)
                                 + Matchup.ReachInDisciplineFactor(50 - k, liveCfg) - 2.0) < 1e-15);
            Check("discipline factor symmetric about 50: f(50+k) + f(50-k) == 2", symmetric);

            // Neutral at 50 / o=0.5: every factor == 1, propensity == refProp.
            var neutral = Matchup.ReachInDisciplineFactor(50, liveCfg) == 1.0
                       && Matchup.ReachInAthFactor(50, liveCfg) == 1.0
                       && Matchup.ReachInPerimFactor(0.5, liveCfg) == 1.0
                       && Math.Abs(Matchup.ReachInPropensity(50, 50, 0.5, liveCfg) - Matchup.ReachInReferencePropensity(liveCfg)) < 1e-15;
            Check("average defender (D50,A50,o0.5) is neutral -> factors == 1, propensity == refProp", neutral);

            // Monotone: propensity strictly DECREASES as Discipline rises (more restraint).
            var mono = Matchup.ReachInPropensity(0, 50, 0.5, liveCfg)
                     > Matchup.ReachInPropensity(50, 50, 0.5, liveCfg)
                     && Matchup.ReachInPropensity(50, 50, 0.5, liveCfg)
                     > Matchup.ReachInPropensity(99, 50, 0.5, liveCfg);
            Check("propensity strictly decreasing in Discipline (hacker > average > lockdown)", mono,
                  $"D0={Matchup.ReachInPropensity(0,50,0.5,liveCfg):F4} > D50={Matchup.ReachInPropensity(50,50,0.5,liveCfg):F4} > D99={Matchup.ReachInPropensity(99,50,0.5,liveCfg):F4}");

            // Strictly positive everywhere (>= LuckFloor) — no defender is ever un-drawable.
            var positive = true;
            foreach (var d in new[] { 0.0, 25, 50, 75, 99 })
                foreach (var a in new[] { 0.0, 50, 99 })
                    foreach (var o in new[] { 0.0, 0.5, 1.0 })
                        positive = positive && Matchup.ReachInPropensity(d, a, o, liveCfg) > 0.0;
            Check("propensity strictly positive across the grid (>= LuckFloor)", positive);
        }

        // ----------------------------------------------------------------
        // (3)-(5) Per-man aggregate on REAL Players (the Postness-derived path).
        // ----------------------------------------------------------------
        Console.WriteLine("  (3)-(5) Per-man aggregate (real players, live config):");
        {
            var five = new Player?[] { Body(50), Body(50), Body(50), Body(50), Body(50) };
            var aggAvg = Matchup.ReachInPerManAggregate(five, liveCfg);
            Check("(3) ANCHOR: five average defenders -> aggregate == 1.0 (BIT-exact)", aggAvg == 1.0,
                  $"aggregate = {aggAvg:R}");

            // Swap slot 1 to a hacker (D=0), same body -> lineup mean postness unchanged, o stays 0.5.
            var oneHack = new Player?[] { Body(0), Body(50), Body(50), Body(50), Body(50) };
            var aggHack1 = Matchup.ReachInPerManAggregate(oneHack, liveCfg);
            Check("(4) ★ SUM: one hacker RAISES the team aggregate above 1.0 (adds, not redistributes)",
                  aggHack1 > 1.0 + 1e-9, $"aggregate = {aggHack1:F6} (> 1.0)");

            // Swap slot 1 to a lockdown (D=99) -> aggregate BELOW 1.0 (sheds fouls).
            var oneLock = new Player?[] { Body(99), Body(50), Body(50), Body(50), Body(50) };
            var aggLock1 = Matchup.ReachInPerManAggregate(oneLock, liveCfg);
            Check("(4b) one lockdown LOWERS the team aggregate below 1.0", aggLock1 < 1.0 - 1e-9,
                  $"aggregate = {aggLock1:F6} (< 1.0)");

            // Stackable-linear: two hackers give ~2x the one-hacker delta above 1.0.
            var twoHack = new Player?[] { Body(0), Body(0), Body(50), Body(50), Body(50) };
            var aggHack2 = Matchup.ReachInPerManAggregate(twoHack, liveCfg);
            var d1 = aggHack1 - 1.0; var d2 = aggHack2 - 1.0;
            Check("(5) stackable-linear: two hackers ~ 2x the one-hacker delta", Math.Abs(d2 - 2.0 * d1) < 1e-12,
                  $"Δ1={d1:F6}, Δ2={d2:F6}, 2·Δ1={2*d1:F6}");
        }

        // ----------------------------------------------------------------
        // (6) Draw ∝ propensity — the REAL harness committer draw.
        // ----------------------------------------------------------------
        Console.WriteLine("  (6) Committer draw ∝ propensity (real DrawNonShootingFouler, 200k draws):");
        {
            const int DrawN = 200_000;
            const double Tol = 0.01;

            // Slot 1 = hacker (D0), slot 2 = lockdown (D99), slots 3-5 = average. Uniform bodies.
            var dtGame  = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var lineup  = dtGame.LineupFor(TeamSide.Home);
            var roster  = dtGame.RosterFor(TeamSide.Home);
            roster.SetStarter(lineup.SlotAt(1), StampPlayerId(Body(0),  1));
            roster.SetStarter(lineup.SlotAt(2), StampPlayerId(Body(99), 2));
            roster.SetStarter(lineup.SlotAt(3), StampPlayerId(Body(50), 3));
            roster.SetStarter(lineup.SlotAt(4), StampPlayerId(Body(50), 4));
            roster.SetStarter(lineup.SlotAt(5), StampPlayerId(Body(50), 5));

            // Expected proportions from the named statics (same weights the draw uses; uniform
            // bodies -> o=0.5 for all, so propensity depends only on Discipline here).
            double W(int d) => Matchup.ReachInPropensity(d, 50, 0.5, liveCfg);
            var wSum = W(0) + W(99) + 3 * W(50);
            var expHack = W(0) / wSum; var expLock = W(99) / wSum; var expAvg = W(50) / wSum;

            var rng = new Random(68001);
            var counts = new long[6];
            for (var n = 0; n < DrawN; n++)
            {
                var s = DrawNonShootingFouler(rng, TeamSide.Home, roster, isReachIn: true, atPossession: 1, cfg: liveCfg);
                if (s >= 1 && s <= 5) counts[s]++;
            }
            double f1 = counts[1] / (double)DrawN, f2 = counts[2] / (double)DrawN;
            double f3 = counts[3] / (double)DrawN, f4 = counts[4] / (double)DrawN, f5 = counts[5] / (double)DrawN;

            Console.WriteLine($"    hacker(1)={f1:F4} (exp {expHack:F4})  lockdown(2)={f2:F4} (exp {expLock:F4})  avg(3,4,5)≈{expAvg:F4}");
            var ordered  = f1 > f3 && f3 > f2;   // hacker > average > lockdown
            var matched  = Math.Abs(f1 - expHack) <= Tol && Math.Abs(f2 - expLock) <= Tol
                        && Math.Abs(f3 - expAvg) <= Tol && Math.Abs(f4 - expAvg) <= Tol && Math.Abs(f5 - expAvg) <= Tol;
            Check("reach-in draw ordered: hacker > average > lockdown", ordered);
            Check("reach-in draw frequencies match propensity proportions (±0.01)", matched);

            // Situational draw uses the Discipline factor only — same Discipline ordering.
            var rng2 = new Random(68002);
            var sc = new long[6];
            for (var n = 0; n < DrawN; n++)
            {
                var s = DrawNonShootingFouler(rng2, TeamSide.Home, roster, isReachIn: false, atPossession: 1, cfg: liveCfg);
                if (s >= 1 && s <= 5) sc[s]++;
            }
            var sitOrdered = sc[1] > sc[3] && sc[3] > sc[2];
            Check("situational draw ordered by Discipline: hacker > average > lockdown", sitOrdered,
                  $"hacker={sc[1]/(double)DrawN:F4}  avg={sc[3]/(double)DrawN:F4}  lockdown={sc[2]/(double)DrawN:F4}");
        }

        // ----------------------------------------------------------------
        // (7) Completeness — every charge emits exactly one event, correct flag,
        //     below AND across the bonus.
        // ----------------------------------------------------------------
        Console.WriteLine("  (7) Completeness (DefensiveFoulCharge.Resolve emits one event, correct IsReachIn, below + across bonus):");
        {
            GameState Fresh() => new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var state = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);

            // Reach-in (Roll D carries a flavor): IsReachIn == true, below bonus.
            var gR = Fresh();
            var cR = (Continue)DefensiveFoulCharge.Resolve(state, gR, ContinuationKind.ResumeInbound, FoulFlavor.ReachIn);
            var reachOk = cR.NonShootingFoul is { IsReachIn: true };
            Check("reach-in charge emits exactly one event with IsReachIn == true", reachOk);

            // Situational (no flavor): IsReachIn == false.
            var gS = Fresh();
            var cS = (Continue)DefensiveFoulCharge.Resolve(state, gS, ContinuationKind.ResolveSidelineInbound, null);
            var sitOk = cS.NonShootingFoul is { IsReachIn: false };
            Check("situational charge emits exactly one event with IsReachIn == false", sitOk);

            // Across the bonus: the emit is BEFORE the fork, so it fires on every charge —
            // drive charges up to and past the bonus threshold and confirm each one emits.
            var gB = Fresh();
            var allEmit = true; var everReachOnBonus = false;
            for (var i = 0; i < cfgD.DoubleBonusThreshold + 2; i++)
            {
                var c = (Continue)DefensiveFoulCharge.Resolve(state, gB, ContinuationKind.ResumeInbound, FoulFlavor.ReachIn);
                allEmit = allEmit && c.NonShootingFoul is { IsReachIn: true };
                if (c.Bonus != BonusType.None && c.NonShootingFoul is { IsReachIn: true }) everReachOnBonus = true;
            }
            Check("every charge across the bonus emits its event (fork branch does not drop it)", allEmit && everReachOnBonus,
                  $"drove {cfgD.DoubleBonusThreshold + 2} charges through both bonus branches");
        }

        // ----------------------------------------------------------------
        // (8) Config guards — out-of-range values throw; defaults load cleanly.
        // ----------------------------------------------------------------
        Console.WriteLine("  (8) Config guards (Load rejects out-of-range spans/luckfloor/scale):");
        {
            bool Throws(Action a) { try { a(); return false; } catch (InvalidOperationException) { return true; } }
            var baseJson = File.ReadAllText(configPath);

            bool GuardThrows(string key, double bad)
            {
                var tmp = Path.Combine(Path.GetTempPath(), $"charm_nsf_guard_{key}_{Guid.NewGuid():N}.json");
                try
                {
                    using var d = JsonDocument.Parse(baseJson);
                    var node = System.Text.Json.Nodes.JsonNode.Parse(baseJson)!;
                    node["Matchup"]![key] = bad;
                    File.WriteAllText(tmp, node.ToJsonString());
                    return Throws(() => MatchupConfig.Load(tmp));
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            Check("ReachInDiscSpan >= 1.0 rejected",  GuardThrows("ReachInDiscSpan", 1.0));
            Check("ReachInDiscSpan < 0 rejected",     GuardThrows("ReachInDiscSpan", -0.01));
            Check("ReachInPerimSpan >= 1.0 rejected", GuardThrows("ReachInPerimSpan", 1.5));
            Check("ReachInLuckFloor < 0 rejected",    GuardThrows("ReachInLuckFloor", -0.01));
            Check("ReachInPostnessScale <= 0 rejected", GuardThrows("ReachInPostnessScale", 0.0));

            // Defaults / live config load cleanly.
            Check("live config loads cleanly (no false guard trips)", !Throws(() => MatchupConfig.Load(configPath)));
        }

        Console.WriteLine($"  Phase 68 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
