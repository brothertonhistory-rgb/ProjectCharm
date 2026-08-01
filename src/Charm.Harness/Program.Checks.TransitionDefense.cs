using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ─────────────────────────────────────────────────────────────────────────────
//  PHASE 79 (Session 88) — WHO GOT BACK. The transition-defence model.
//
//  NO BASKETBALL TARGET IS ASSERTED ANYWHERE IN THIS FILE. Every magnitude the
//  session introduces is a calibration placeholder living on the season page
//  (page-only calibration principle). What is asserted here is parity against the
//  locked oracle, the wiring invariants, and the handful of relationships that a
//  green conservation check cannot see.
//
//  The three assertions that matter most are the ones a mis-wire would slip past:
//    A3  the man whose ratings set the block rate is the man credited for the block
//    A5c the pairing is by SLOT NUMBER, not by position in a compacted list
//    A9  a negative control proving A4 actually rejects the mis-wire it targets
//  Everything else is either parity or hygiene.
// ─────────────────────────────────────────────────────────────────────────────

internal static partial class Program
{
    private static bool Phase79TransitionDefenseCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 79: who got back — transition defence (golden parity + pairing + lifecycle + guards) ---");
        var pass = true;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgM = MatchupConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // A player builder that names only what this session reads; everything else is 50.
        static Player Mk(string n, int speed = 50, int hustle = 50, int rimP = 50,
                         int height = 50, int wingspan = 50, int vertical = 50,
                         int strength = 50, int postD = 50, int iq = 50,
                         int fin = 50, int outside = 50)
            => new Player(n)
            {
                Outside = outside, Mid = 50, Close = 50, Finishing = fin, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = postD,
                RimProtection = rimP, DefensiveRebounding = 50, Steals = 50,
                Height = height, Wingspan = wingspan, Weight = 50, Strength = strength,
                Speed = speed, Quickness = 50, FirstStep = 50, Vertical = vertical,
                Endurance = 50, Hustle = hustle, BasketballIQ = iq, Discipline = 50,
                HelpDefense = 50, OffBallDefense = 50,
                RimTendency = 50, ShortTendency = 50, MidTendency = 50,
                LongTendency = 50, ThreeTendency = 50,
            };

        // A man whose Postness lands exactly on a wanted value. Postness blends Height,
        // PostDefense and Strength at a third each (weights summing to 1.0), so setting all
        // three to the same number puts Postness exactly there — no inversion needed.
        static Player MkPost(string n, int postness, int speed = 50, int hustle = 50)
            => Mk(n, speed: speed, hustle: hustle, height: postness, postD: postness, strength: postness);

        const double Tight = 1e-12;

        // ── A1 — golden parity against the LOCKED oracle ─────────────────────
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "transition_defense_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var tol  = root.GetProperty("float_tolerance").GetDouble();

            // Fixture-validity guard FIRST. A fixture emitted against different constants
            // would make every row agree with a WRONG engine — reject before comparing.
            var consts = root.GetProperty("constants");
            var constMismatch = new List<string>();
            void Con(string key, double live)
            {
                var fixtureValue = consts.GetProperty(key).GetDouble();
                if (Math.Abs(fixtureValue - live) > 1e-15)
                    constMismatch.Add($"{key} fixture={fixtureValue} live={live}");
            }
            Con("TransitionGotBackLuckFloor",   cfgM.TransitionGotBackLuckFloor);
            Con("TransitionLegsSpan",           cfgM.TransitionLegsSpan);
            Con("TransitionDepthSpan",          cfgM.TransitionDepthSpan);
            Con("TransitionEffortSpeedShare",   cfgM.TransitionEffortSpeedShare);
            Con("TransitionPostnessScale",      cfgM.TransitionPostnessScale);
            Con("TransitionArrivalSpan",        cfgM.TransitionArrivalSpan);
            Con("TransitionContestDiscount",    cfgM.TransitionContestDiscount);
            Con("TransitionBaseBreakMake",      cfgM.TransitionBaseBreakMake);
            Con("TransitionBaseBreakBlock",     cfgM.TransitionBaseBreakBlock);
            Con("TransitionRimProtectionSwing", cfgM.TransitionRimProtectionSwing);
            Con("TransitionTeamPresenceSwing",  cfgM.TransitionTeamPresenceSwing);
            Con("TransitionChaseSwing",         cfgM.TransitionChaseSwing);
            Con("TransitionChaseLengthWeight",  cfgM.TransitionChaseLengthWeight);
            Con("TransitionChaseRimProtWeight", cfgM.TransitionChaseRimProtWeight);
            Con("TransitionChaseSpeedSwing",    cfgM.TransitionChaseSpeedSwing);
            Con("TransitionShooterZoneRim",     cfgM.TransitionShooterZoneRim);
            Con("TransitionShooterZoneShort",   cfgM.TransitionShooterZoneShort);
            Con("TransitionShooterZoneMid",     cfgM.TransitionShooterZoneMid);
            Con("TransitionShooterZoneLong",    cfgM.TransitionShooterZoneLong);
            Con("TransitionShooterZoneThree",   cfgM.TransitionShooterZoneThree);
            Check("A1 fixture constants match the live config", constMismatch.Count == 0,
                  constMismatch.Count == 0 ? "20 constants" : string.Join("; ", constMismatch.Take(3)));

            var worst = 0.0; var n = 0; var worstKind = "";
            foreach (var c in root.GetProperty("cases").EnumerateArray())
            {
                var kind = c.GetProperty("kind").GetString()!;
                var expected = c.GetProperty("expected").GetDouble();
                double actual;
                switch (kind)
                {
                    case "gotback":
                    {
                        var zoneText = c.GetProperty("zone");
                        ShotLocation? zone = zoneText.ValueKind == JsonValueKind.Null
                            ? null
                            : Enum.Parse<ShotLocation>(zoneText.GetString()!);
                        actual = TransitionDefense.GotBack(
                            c.GetProperty("speed").GetDouble(), c.GetProperty("hustle").GetDouble(),
                            c.GetProperty("oppPostness").GetDouble(), c.GetProperty("oppMean").GetDouble(),
                            zone, cfgM);
                        break;
                    }
                    case "make":
                        actual = TransitionDefense.BreakMakePct(
                            c.GetProperty("rimprot").GetDouble(), c.GetProperty("gotBack").GetDouble(),
                            c.GetProperty("aggregate").GetDouble(), cfgM);
                        break;
                    case "block":
                        actual = TransitionDefense.BreakBlockPct(
                            c.GetProperty("rimprot").GetDouble(), c.GetProperty("length").GetDouble(),
                            c.GetProperty("gotBack").GetDouble(), cfgM);
                        break;
                    default:
                        throw new InvalidOperationException($"unknown golden case kind '{kind}'");
                }
                var d = Math.Abs(actual - expected);
                if (d > worst) { worst = d; worstKind = kind; }
                n++;
            }
            Check($"A1 golden parity — {n} cases, bound {tol:0.0e+0} absolute", n == 228 && worst <= tol,
                  $"worst |Δ| {worst:0.0e+00}" + (worst > 0 ? $" ({worstKind})" : ""));
        }
        catch (Exception ex)
        {
            Check("A1 golden parity", false, ex.Message);
        }

        // ── A2 — THE ANCHOR ──────────────────────────────────────────────────
        // Five average defenders against five average men must reproduce the configured base
        // rates EXACTLY. A sign error anywhere shows up here while every archetype table
        // still looks perfectly plausible. Bound is 1e-12 rather than bitwise: the neutral
        // path runs through tanh and clamps that cancel mathematically but are not
        // bit-guaranteed across platforms.
        {
            var avgDef = Enumerable.Range(1, 5).Select(i => (Player?)Mk($"ad{i}")).ToArray();
            var avgOff = Enumerable.Range(1, 5).Select(i => (Player?)Mk($"ao{i}")).ToArray();

            // No shooter zone: the pure neutral read.
            var w = TransitionDefense.LineupGotBack(avgDef, avgOff, null, null, cfgM);
            var agg = TransitionDefense.TeamAggregate(w, cfgM);
            var mk = TransitionDefense.BreakMakePct(avgDef[0]!, w[0], agg, cfgM);
            var bk = TransitionDefense.BreakBlockPct(avgDef[0]!, w[0], cfgM);

            Check("A2 anchor — aggregate is exactly 1.0 at five average men",
                  Math.Abs(agg - 1.0) < Tight, $"aggregate {agg:F15}");
            Check("A2 anchor — break make reproduces the configured base",
                  Math.Abs(mk - cfgM.TransitionBaseBreakMake) < Tight,
                  $"{mk:F15} vs {cfgM.TransitionBaseBreakMake:F15}");
            Check("A2 anchor — break block reproduces the configured base",
                  Math.Abs(bk - cfgM.TransitionBaseBreakBlock) < Tight,
                  $"{bk:F15} vs {cfgM.TransitionBaseBreakBlock:F15}");

            // The Mid zone multiplier is exactly 1.0, so the anchor must survive a Mid shot
            // untouched. Every other zone deliberately moves it — that is R7 working.
            var wMid = TransitionDefense.LineupGotBack(avgDef, avgOff, 3, ShotLocation.Mid, cfgM);
            var aggMid = TransitionDefense.TeamAggregate(wMid, cfgM);
            Check("A2 anchor — a Mid-zone break leaves the anchor untouched (zone mult 1.0)",
                  Math.Abs(aggMid - 1.0) < Tight, $"aggregate {aggMid:F15}");

            var wThree = TransitionDefense.LineupGotBack(avgDef, avgOff, 3, ShotLocation.Three, cfgM);
            var aggThree = TransitionDefense.TeamAggregate(wThree, cfgM);
            Check("A2 anchor — a Three-zone break DOES move it (R7 live, not inert)",
                  aggThree > 1.0 + 1e-9, $"aggregate {aggThree:F6} > 1.0");
        }

        // ── A4 — R2 DISCRIMINATING, ZONE-CONTROLLED ──────────────────────────
        // Five IDENTICAL defenders against a normal offence must still give five DIFFERENT
        // got-back numbers, ordered by the post-ness of the man each is guarding — because
        // depth is read off the OPPOSING lineup, never off the defender's own body. If it
        // were read off his own body these five would be identical, which is the S81.1
        // mistake in a new place. The no-zone path is called deliberately: a shooter-zone
        // multiplier on one seat can legitimately reorder the five and would make a correct
        // port fail this.
        double[] A4Weights(MatchupConfig cfg)
        {
            var def = Enumerable.Range(1, 5).Select(i => (Player?)Mk($"id{i}", speed: 60, hustle: 60)).ToArray();
            var off = new Player?[]
            {
                MkPost("g1", 25), MkPost("g2", 38), MkPost("w3", 52), MkPost("f4", 70), MkPost("c5", 88),
            };
            return TransitionDefense.LineupGotBack(def, off, null, null, cfg);
        }
        {
            var w = A4Weights(cfgM);
            var strictlyDecreasing = true;
            for (var i = 1; i < 5; i++) if (!(w[i] < w[i - 1] - 1e-12)) strictlyDecreasing = false;
            Check("A4 five identical defenders give five DIFFERENT got-back numbers",
                  w.Distinct().Count() == 5,
                  string.Join(" / ", w.Select(x => x.ToString("F4", CultureInfo.InvariantCulture))));
            Check("A4 strictly monotone decreasing in the opponent's post-ness (R2)",
                  strictlyDecreasing);
            Check("A4 the man on their centre is MUTED but strictly positive (R1)",
                  w[4] > 0.0 && w[4] < w[0], $"centre {w[4]:F4} vs point guard {w[0]:F4}");
        }

        // ── A9 — NEGATIVE CONTROL for A4 ─────────────────────────────────────
        // Construct the mis-wire A4 exists to catch — depth read off the DEFENDER'S OWN body
        // against his own lineup mean — and prove A4 rejects it. Without this, A4 is a check
        // whose teeth are unverified.
        {
            var def = Enumerable.Range(1, 5).Select(i => Mk($"id{i}", speed: 60, hustle: 60)).ToArray();
            var off = new[] { MkPost("g1", 25), MkPost("g2", 38), MkPost("w3", 52), MkPost("f4", 70), MkPost("c5", 88) };
            var ownMean = def.Average(p => Matchup.Postness(p, cfgM));
            var misWired = def.Select(p => cfgM.TransitionGotBackLuckFloor
                                         + TransitionDefense.LegsFactor(p.Speed, p.Hustle, cfgM)
                                         * TransitionDefense.DepthFactor(Matchup.Postness(p, cfgM), ownMean, cfgM))
                              .ToArray();
            var misWiredAllDifferent = misWired.Distinct().Count() == 5;
            var misWiredDecreasing = true;
            for (var i = 1; i < 5; i++) if (!(misWired[i] < misWired[i - 1] - 1e-12)) misWiredDecreasing = false;
            Check("A9 negative control — the own-body mis-wire produces ZERO spread across identical defenders",
                  !misWiredAllDifferent && !misWiredDecreasing,
                  $"all five = {misWired[0]:F6} (A4 would reject this)");
            // Also prove the offence is what makes the difference, not the defenders.
            Check("A9 negative control — and A4's real weights DO differ from the mis-wired ones",
                  Math.Abs(A4Weights(cfgM)[0] - misWired[0]) > 1e-6);
        }

        // ── A5 — A-4 DISCRIMINATING: the aggregate is emergent, not a team rating ──
        // Defence held FIXED, the offence changed underneath it. A team aggregate built from a
        // mean of DEFENSIVE ratings — the scalar this session deletes — cannot move at all
        // here, because the defenders never change. The got-back aggregate must.
        //
        // ★ WHAT THIS DELIBERATELY DOES NOT ASSERT (Emmett's ruling, S88). An earlier draft
        // asserted that going two-bigs → five-out makes the aggregate RISE, on the reasoning
        // that a defence is not stranded under the rim against a team with no post. Measured,
        // that is not what the model does and cannot be: depth is read against the OFFENCE'S
        // OWN lineup average, so every offence has a relative post and somebody is always the
        // last man back. The offence's absolute SIZE is invisible — five 6'0" guards and five
        // seven-footers produce the same aggregate to twelve places — and only its SHAPE
        // registers. Which way five-out moves the number then depends on which of the five
        // defenders happens to be stuck on their biggest man, so its SIGN is not stable and
        // asserting a direction would be asserting an accident. Ruled: keep the relative read,
        // assert the property that is real.
        {
            var def = new Player?[]
            {
                Mk("d1", speed: 78), Mk("d2", speed: 66), Mk("d3", speed: 55),
                Mk("d4", speed: 47), Mk("d5", speed: 36),
            };
            var twoBigs   = new Player?[] { MkPost("a", 28), MkPost("b", 36), MkPost("c", 50), MkPost("d", 76), MkPost("e", 90) };
            var fiveOut   = new Player?[] { MkPost("a", 30), MkPost("b", 33), MkPost("c", 36), MkPost("d", 39), MkPost("e", 42) };
            var fiveOutXL = new Player?[] { MkPost("a", 70), MkPost("b", 73), MkPost("c", 76), MkPost("d", 79), MkPost("e", 82) };

            double Agg(Player?[] o) => TransitionDefense.TeamAggregate(
                TransitionDefense.LineupGotBack(def, o, null, null, cfgM), cfgM);

            var aTwoBigs = Agg(twoBigs); var aFiveOut = Agg(fiveOut); var aFiveOutXL = Agg(fiveOutXL);

            Check("A5 the aggregate RESPONDS to the offence (a mean of defensive ratings could not)",
                  Math.Abs(aFiveOut - aTwoBigs) > 1e-6,
                  $"two-bigs {aTwoBigs:F6} vs five-out {aFiveOut:F6}");
            Check("A5 it responds to the offence's SHAPE, not its SIZE (the relative read, ruled)",
                  Math.Abs(aFiveOutXL - aFiveOut) < Tight,
                  $"small five-out {aFiveOut:F6} == huge five-out {aFiveOutXL:F6}");

            // And the defence is what it is measured against: swap which defender is stuck on
            // their big and the same offence reads differently. That is the assignment model
            // working, and it is why no direction is asserted above.
            var defInverted = def.Reverse().ToArray();
            var aInverted = TransitionDefense.TeamAggregate(
                TransitionDefense.LineupGotBack(defInverted, twoBigs, null, null, cfgM), cfgM);
            Check("A5 WHICH defender is stuck on their big changes the answer (assignment is live)",
                  Math.Abs(aInverted - aTwoBigs) > 1e-6,
                  $"{aTwoBigs:F6} vs {aInverted:F6} with the defence reversed");
        }

        // ── A5b — PAIRING, depth component, ARBITRARY defenders ──────────────
        // Swap the offensive point guard and centre between offensive slots 1 and 5, with the
        // shooter parked in an unaffected seat. The opponent-derived DEPTH FACTOR for slots 1
        // and 5 swaps; each defender's own legs factor stays attached to his own slot, so the
        // FULL got-back values do NOT swap — they recombine. Slots 2–4 are unchanged because
        // their opponents are unchanged AND the occupied offensive player SET is unchanged,
        // so oppMean is unchanged; a failure in 2–4 points at oppMean, not at the pairing.
        {
            var def = new Player?[]
            {
                Mk("d1", speed: 84), Mk("d2", speed: 70), Mk("d3", speed: 55),
                Mk("d4", speed: 45), Mk("d5", speed: 30),
            };
            var pg = MkPost("pg", 25); var c5 = MkPost("c5", 88);
            var before = new Player?[] { pg, MkPost("o2", 40), MkPost("o3", 55), MkPost("o4", 70), c5 };
            var after  = new Player?[] { c5, MkPost("o2", 40), MkPost("o3", 55), MkPost("o4", 70), pg };

            var wB = TransitionDefense.LineupGotBack(def, before, 3, ShotLocation.Mid, cfgM);
            var wA = TransitionDefense.LineupGotBack(def, after,  3, ShotLocation.Mid, cfgM);

            var meanB = TransitionDefense.OpponentMeanPostness(before, cfgM)!.Value;
            var meanA = TransitionDefense.OpponentMeanPostness(after,  cfgM)!.Value;
            var depthB1 = TransitionDefense.DepthFactor(Matchup.Postness(pg, cfgM), meanB, cfgM);
            var depthA1 = TransitionDefense.DepthFactor(Matchup.Postness(c5, cfgM), meanA, cfgM);
            var depthB5 = TransitionDefense.DepthFactor(Matchup.Postness(c5, cfgM), meanB, cfgM);
            var depthA5 = TransitionDefense.DepthFactor(Matchup.Postness(pg, cfgM), meanA, cfgM);

            Check("A5b oppMean is unchanged by the swap (the offensive player set is the same)",
                  Math.Abs(meanA - meanB) < Tight);
            Check("A5b the opponent-derived DEPTH FACTOR swaps between slots 1 and 5",
                  Math.Abs(depthA1 - depthB5) < Tight && Math.Abs(depthA5 - depthB1) < Tight,
                  $"slot1 {depthB1:F6}↔{depthA1:F6}  slot5 {depthB5:F6}↔{depthA5:F6}");
            Check("A5b slots 2–4 are completely unchanged",
                  Math.Abs(wA[1] - wB[1]) < Tight && Math.Abs(wA[2] - wB[2]) < Tight
                  && Math.Abs(wA[3] - wB[3]) < Tight);
            // The FULL values must NOT swap — each defender keeps his own legs.
            Check("A5b the FULL got-back values do NOT swap — legs stay with their own slot",
                  Math.Abs(wA[0] - wB[4]) > 1e-6 && Math.Abs(wA[4] - wB[0]) > 1e-6,
                  $"slot1 {wB[0]:F4}→{wA[0]:F4}  slot5 {wB[4]:F4}→{wA[4]:F4}");
        }

        // ── A5c — PAIRING, full weights, IDENTICAL defenders + asymmetric occupancy ──
        // With the defenders identical, the legs factor is common, so the same swap DOES make
        // slots 1 and 5 take each other's pre-swap values exactly. Then repeat with offensive
        // slot 3 EMPTY and assert occupancy matrix row 3. A port that pairs by compacted-list
        // order passes all 228 golden cases and fails right here.
        {
            var def = Enumerable.Range(1, 5).Select(i => (Player?)Mk($"id{i}", speed: 60, hustle: 60)).ToArray();
            var pg = MkPost("pg", 25); var c5 = MkPost("c5", 88);
            var o2 = MkPost("o2", 40); var o3 = MkPost("o3", 55); var o4 = MkPost("o4", 70);
            var before = new Player?[] { pg, o2, o3, o4, c5 };
            var after  = new Player?[] { c5, o2, o3, o4, pg };

            // Shooter parked in slot 2 — an unaffected seat.
            var wB = TransitionDefense.LineupGotBack(def, before, 2, ShotLocation.Mid, cfgM);
            var wA = TransitionDefense.LineupGotBack(def, after,  2, ShotLocation.Mid, cfgM);

            Check("A5c identical defenders: slots 1 and 5 take each other's values exactly",
                  Math.Abs(wA[0] - wB[4]) < Tight && Math.Abs(wA[4] - wB[0]) < Tight,
                  $"{wB[0]:F4} ↔ {wB[4]:F4}");
            Check("A5c slots 2–4 unchanged within 1e-12",
                  Math.Abs(wA[1] - wB[1]) < Tight && Math.Abs(wA[2] - wB[2]) < Tight
                  && Math.Abs(wA[3] - wB[3]) < Tight);

            // ── Matrix ROW 3 — the ruled rule: an empty offensive seat means nobody to guard.
            var gappy = new Player?[] { pg, o2, null, o4, c5 };
            var wG = TransitionDefense.LineupGotBack(def, gappy, null, null, cfgM);
            var legsOnly = cfgM.TransitionGotBackLuckFloor
                         + TransitionDefense.LegsFactor(60, 60, cfgM);   // depth exactly 1.0
            Check("A5c row 3 — the defender opposite an empty seat gets NEUTRAL depth (legs alone)",
                  Math.Abs(wG[2] - legsOnly) < Tight, $"{wG[2]:F12} vs legs-only {legsOnly:F12}");
            Check("A5c row 3 — he stays in the draw (strictly positive weight)", wG[2] > 0.0);

            var meanOccupied = TransitionDefense.OpponentMeanPostness(gappy, cfgM)!.Value;
            var meanByHand = new[] { pg, o2, o4, c5 }.Average(p => Matchup.Postness(p, cfgM));
            Check("A5c row 3 — oppMean EXCLUDES the empty seat (four men, not five)",
                  Math.Abs(meanOccupied - meanByHand) < Tight,
                  $"{meanOccupied:F6} over 4 occupied seats");

            // The compacted-list mis-wire: pairing defender 3 with offensive slot 4 and so on.
            var compacted = new[] { pg, o2, o4, c5 };
            var misWired3 = TransitionDefense.GotBack(def[2]!, compacted[2], meanOccupied, null, cfgM);
            Check("A5c row 3 — a compacted-list pairing would give a DIFFERENT answer (the test has teeth)",
                  Math.Abs(misWired3 - wG[2]) > 1e-6,
                  $"slot-number {wG[2]:F4} vs compacted {misWired3:F4}");
        }

        // ── A6b — THE DENOMINATOR ────────────────────────────────────────────
        // Three occupied defenders and three occupied opponents. The aggregate divides by the
        // men actually on the floor, not by a fixed five. Both numbers come from the locked
        // oracle's own emission rather than from prose, and BOTH are asserted: the right one
        // must match and the wrong one must not.
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "transition_defense_golden.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var f = doc.RootElement.GetProperty("denominator_fixture");
            var expectedOccupied = f.GetProperty("aggregate_occupied_count").GetDouble();
            var expectedFixedFive = f.GetProperty("aggregate_fixed_five").GetDouble();

            var players = f.GetProperty("players").EnumerateArray().ToArray();
            var slots   = f.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToArray();

            var def = new Player?[5];
            var off = new Player?[5];
            for (var i = 0; i < 3; i++)
            {
                var p = players[i];
                var man = MkPost($"m{i}", p.GetProperty("post").GetInt32(),
                                 speed: p.GetProperty("speed").GetInt32(),
                                 hustle: p.GetProperty("hustle").GetInt32());
                def[slots[i] - 1] = man;
                off[slots[i] - 1] = man;
            }

            var w = TransitionDefense.LineupGotBack(def, off, null, null, cfgM);
            var occupiedWeights = Enumerable.Range(0, 5).Where(i => def[i] is not null)
                                            .Select(i => w[i]).ToList();
            var agg = TransitionDefense.TeamAggregate(occupiedWeights, cfgM);

            Check("A6b three-man aggregate uses the OCCUPIED count",
                  Math.Abs(agg - expectedOccupied) < 1e-9,
                  $"{agg:F6} (oracle {expectedOccupied:F6})");
            Check("A6b a fixed-five denominator would be REJECTED",
                  Math.Abs(agg - expectedFixedFive) > 1e-6,
                  $"fixed-five would give {expectedFixedFive:F6}");

            // Picker totality across every non-empty defensive shape: the draw must always
            // land on an OCCUPIED seat, whichever seats those are.
            var totalityOk = true; var shapesTested = 0;
            for (var mask = 1; mask < 32; mask++)
            {
                var d = new Player?[5]; var o = new Player?[5];
                for (var i = 0; i < 5; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    d[i] = Mk($"d{i}", speed: 40 + 10 * i);
                    o[i] = MkPost($"o{i}", 30 + 12 * i);
                }
                // Ensure the offence is never fully empty (matrix row 2 is a separate case).
                if (o.All(x => x is null)) continue;
                var ww = TransitionDefense.LineupGotBack(d, o, null, null, cfgM);
                var lineup = new Lineup(TeamSide.Away);
                for (var t = 0; t < 40; t++)
                {
                    var slot = TransitionDefenderPicker.Pick(TeamSide.Away, lineup, ww, new SystemRng(9000 + mask * 41 + t));
                    if (d[slot.Number - 1] is null) totalityOk = false;
                }
                shapesTested++;
            }
            Check($"A6b picker totality across all {shapesTested} non-empty defensive shapes",
                  totalityOk, "every draw landed on an occupied seat");
        }
        catch (Exception ex)
        {
            Check("A6b denominator", false, ex.Message);
        }

        // ── A6 — OCCUPANCY MATRIX rows 1 and 2 (behaviour, through the real generator) ──
        // Row 1: nobody on defence. Row 2: nobody on offence. In both, the got-back path is
        // never entered, no context is built, no slot is fabricated, and the shot keeps the
        // behaviour it has with no contest at all.
        {
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 1; i <= 5; i++)
                game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"h{i}", speed: 50 + 6 * i));
            // AWAY (defence) deliberately left EMPTY — row 1.
            var gen = new RollHGenerator(cfgH, cfgM, game);
            var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                SelectedSlot: game.HomeLineup.SlotAt(1), ShotType: ShotLocation.Rim, FastBreak: true);

            var noContest = gen.Generate(st, putback: false, contest: null);
            var made  = noContest.Slices.First(s => s.Outcome == ShotResult.Made).Weight;
            var blocked = noContest.Slices.First(s => s.Outcome == ShotResult.Blocked).Weight;
            Check("A6 row 1 — a break with nobody on defence still resolves (no fabricated slot)",
                  made > 0.0 && blocked > 0.0,
                  $"made {made:F6}, blocked {blocked:F6} — flat configured block weight, own-rating make");

            // And the credit path for that shot is UNCHANGED from today: it throws rather than
            // inventing a blocker. Asserting today's real behaviour, not a fallback that does
            // not exist.
            var threw = false;
            try { BlockerPicker.Pick(st, game, cfgM, new SystemRng(1), putback: false); }
            catch (InvalidOperationException) { threw = true; }
            Check("A6 row 1 — the block-credit path is unchanged (still refuses to invent a blocker)",
                  threw);

            // Row 2 — nobody on offence.
            var game2 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 1; i <= 5; i++)
                game2.AwayRoster.SetStarter(game2.AwayLineup.SlotAt(i), Mk($"a{i}"));
            var emptyOff = new Player?[5];
            Check("A6 row 2 — oppMean over an empty offence is never computed",
                  TransitionDefense.OpponentMeanPostness(emptyOff, cfgM) is null);
            var row2Threw = false;
            try { TransitionDefense.LineupGotBack(new Player?[] { Mk("d") , null, null, null, null }, emptyOff, null, null, cfgM); }
            catch (InvalidOperationException) { row2Threw = true; }
            Check("A6 row 2 — the got-back path refuses to run against an empty offence",
                  row2Threw);
        }

        // ── A7 — MONOTONICITY, one axis at a time, at the parameter interface ──
        // Mutate a single raw attribute and freeze everything else: the opponent lineup, the
        // selected slot, and the other three ratings. NOTE: "length" is not a raw attribute —
        // it is Height, Wingspan and Vertical blended — so the length axis is driven through
        // Wingspan with Height and Vertical frozen.
        {
            var off = new Player?[] { MkPost("o1", 30), MkPost("o2", 42), MkPost("o3", 55), MkPost("o4", 70), MkPost("o5", 86) };
            double[] TeamAt(int speed)
            {
                var d = Enumerable.Range(1, 5).Select(i => (Player?)Mk($"d{i}", speed: speed)).ToArray();
                return TransitionDefense.LineupGotBack(d, off, null, null, cfgM);
            }
            double MakeAtTeamSpeed(int speed)
            {
                var w = TeamAt(speed);
                var agg = TransitionDefense.TeamAggregate(w, cfgM);
                return TransitionDefense.BreakMakePct(50, TransitionDefense.ReferenceGotBack(cfgM), agg, cfgM);
            }
            var speeds = new[] { 10, 30, 50, 70, 90 };
            var makeFalls = speeds.Zip(speeds.Skip(1)).All(p => MakeAtTeamSpeed(p.Second) < MakeAtTeamSpeed(p.First) - 1e-12);
            Check("A7 break FG% FALLS as team speed rises (more of them got back)",
                  makeFalls,
                  $"{MakeAtTeamSpeed(10):F5} → {MakeAtTeamSpeed(90):F5}");

            var wings = new[] { 10, 30, 50, 70, 90 };
            double BlockAtWingspan(int wing)
            {
                var p = Mk("d", wingspan: wing);
                return TransitionDefense.BreakBlockPct(p, TransitionDefense.ReferenceGotBack(cfgM), cfgM);
            }
            var blockRisesLength = wings.Zip(wings.Skip(1)).All(p => BlockAtWingspan(p.Second) > BlockAtWingspan(p.First) + 1e-12);
            Check("A7 block% RISES in length (wingspan axis; height and vertical frozen)",
                  blockRisesLength, $"{BlockAtWingspan(10):F5} → {BlockAtWingspan(90):F5}");

            double BlockAtOwnSpeed(int speed)
            {
                var p = Mk("d", speed: speed);
                var g = TransitionDefense.GotBack(p, MkPost("m", 55), 55, null, cfgM);
                return TransitionDefense.BreakBlockPct(p, g, cfgM);
            }
            var blockRisesSpeed = speeds.Zip(speeds.Skip(1)).All(p => BlockAtOwnSpeed(p.Second) > BlockAtOwnSpeed(p.First) + 1e-12);
            Check("A7 block% RISES in the chaser's own speed (R3, the chase-down)",
                  blockRisesSpeed, $"{BlockAtOwnSpeed(10):F5} → {BlockAtOwnSpeed(90):F5}");

            double MakeAtWingspan(int wing)
            {
                var p = Mk("d", wingspan: wing);
                return TransitionDefense.BreakMakePct(p, TransitionDefense.ReferenceGotBack(cfgM), 1.0, cfgM);
            }
            var makeFlatInLength = wings.All(w => Math.Abs(MakeAtWingspan(w) - MakeAtWingspan(50)) < Tight);
            Check("A7 break FG% is FLAT in length (length is a block tool, not a make tool)",
                  makeFlatInLength);
        }

        // ── A3 — ONE DEFENDER, ONE MAN (lifecycle, through whole games) ──────
        // The discriminating property, and one no conservation check can see: the man whose
        // ratings set the block rate must be the man credited for the block.
        //
        // The fixture makes the two candidate answers pull HARD in opposite directions. Seat 1
        // is a small sprinter — he gets back constantly but has nothing to block with, so the
        // halfcourt credit picker would almost never name him. Seat 5 is a huge plodder — the
        // halfcourt credit picker's favourite, but he rarely gets back. If break blocks credit
        // seat 1, credit is following got-back. If they credit seat 5, BlockerPicker is still
        // in the loop and S88's rate and credit disagree about who was there.
        {
            var creditBySlot = new long[6];
            long breakBlocks = 0, breakFga = 0;
            const int Games = 24;

            for (var gi = 0; gi < Games; gi++)
            {
                var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                for (var i = 1; i <= 5; i++)
                    game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"o{i}", speed: 55, height: 40 + 10 * i,
                                                                             postD: 40 + 10 * i, strength: 40 + 10 * i));
                // Seat 1: sprinter, no tools. Seat 5: tools, no legs. Seats 2-4 neutral.
                game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(1),
                    Mk("sprinter", speed: 99, hustle: 99, rimP: 1, height: 1, wingspan: 1, vertical: 1));
                for (var i = 2; i <= 4; i++)
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"d{i}", speed: 50, rimP: 50));
                game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(5),
                    Mk("plodder", speed: 1, hustle: 1, rimP: 99, height: 99, wingspan: 99, vertical: 99));

                var rng = new SystemRng(880000 + gi);
                var resolver = new Resolver(
                    new RollAGenerator(RollAConfig.Load(configPath), cfgM, game), RollAConfig.Load(configPath),
                    new RollBGenerator(RollBConfig.Load(configPath), cfgM, game),
                    new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                    new RollDGenerator(cfgD),
                    new RollEGenerator(RollEConfig.Load(configPath), game),
                    new AttentionGenerator(AttentionConfig.Load(configPath), game),
                    new RollFGenerator(RollFConfig.Load(configPath), cfgM, game),
                    new RollGGenerator(RollGConfig.Load(configPath), cfgM, game),
                    new RollHGenerator(cfgH, cfgM, game),
                    new RollIGenerator(RollIConfig.Load(configPath), cfgM, game),
                    new RollJGenerator(RollJConfig.Load(configPath), cfgM, game),
                    new RollKGenerator(RollKConfig.Load(configPath), cfgM, game),
                    new RollLGenerator(RollLConfig.Load(configPath), game),
                    new RollMGenerator(RollMConfig.Load(configPath), cfgM, game),
                    new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                    cfgM, game, rng);

                var governor = new Governor(resolver, game, GovernorConfig.Load(configPath),
                                            RollClockConfig.Load(configPath), new SystemRng(881000 + gi),
                                            EndOfHalfConfig.Load(configPath));
                var first = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
                foreach (var r in governor.Run(first).Possessions)
                {
                    breakFga += r.FastBreakFga;
                    breakBlocks += r.FastBreakBlk;
                    for (var s = 1; s <= 5; s++) creditBySlot[s] += r.FastBreakBlkBySlot[s];
                }
            }

            var haveSample = breakBlocks >= 50;
            Check($"A3 fixture produced a usable break sample ({breakFga} break FGA, {breakBlocks} break blocks)",
                  haveSample);
            if (haveSample)
            {
                var sprinter = creditBySlot[1];
                var plodder  = creditBySlot[5];
                Check("A3 break blocks credit the man who GOT BACK, not the halfcourt credit picker's favourite",
                      sprinter > plodder,
                      $"seat 1 sprinter {sprinter} vs seat 5 plodder {plodder} " +
                      $"(BlockerPicker would invert this)");
                var total = Enumerable.Range(1, 5).Sum(s => creditBySlot[s]);
                Check("A3 every break block is credited to exactly one seat",
                      total == breakBlocks, $"{total} credited vs {breakBlocks} blocks");
            }
        }

        // ── A3 (picker) — EXACTLY ONE DRAW ───────────────────────────────────
        {
            var counting = new CountingRng(new SystemRng(4242));
            var lineup = new Lineup(TeamSide.Away);
            var weights = new[] { 1.2, 0.9, 1.0, 0.8, 1.1 };
            var before = counting.Count;
            TransitionDefenderPicker.Pick(TeamSide.Away, lineup, weights, counting);
            Check("A3 the defender draw consumes EXACTLY one RNG value",
                  counting.Count - before == 1, $"{counting.Count - before} draw(s)");
        }

        // ── A10 — SCOPE ISOLATION, formula AND attribution ───────────────────
        // Halfcourt shots and BREAK PUTBACKS must be untouched. The frozen fixture was emitted
        // from the pre-S88 tree, so "unchanged" here is measured against the old engine rather
        // than against the new one agreeing with itself.
        //
        // Bound is 1e-12 absolute, NOT bitwise: this fixture crosses platforms and Math.Pow is
        // not bit-portable between Windows and Linux libm (S81.3 shipped exactly that bug and
        // produced a red suite with nothing wrong in the engine). The negative control below
        // measures what a real scope leak looks like, so the bound cannot become decorative.
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "transition_scope_frozen_s88.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"scope fixture not found: {path}");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            // Identical, attribute for attribute, to the men the frozen emitter seated.
            var off = new[]
            {
                Mk("o1", 78, 71, 22, 30, 32, 74, 28, 20, 72, 66, 78),
                Mk("o2", 66, 58, 35, 44, 48, 61, 42, 34, 61, 58, 66),
                Mk("o3", 55, 64, 51, 55, 57, 52, 55, 52, 55, 55, 52),
                Mk("o4", 47, 52, 68, 70, 72, 44, 71, 69, 48, 62, 33),
                Mk("o5", 36, 46, 87, 88, 91, 38, 86, 88, 42, 71, 18),
            };
            var def = new[]
            {
                Mk("d1", 84, 66, 25, 33, 35, 71, 31, 24, 58, 55, 62),
                Mk("d2", 70, 74, 38, 46, 44, 66, 45, 37, 63, 52, 58),
                Mk("d3", 55, 50, 54, 58, 61, 55, 58, 55, 50, 50, 50),
                Mk("d4", 45, 61, 70, 72, 76, 41, 74, 72, 47, 48, 30),
                Mk("d5", 30, 55, 92, 90, 94, 35, 88, 90, 40, 60, 15),
            };
            for (var i = 0; i < 5; i++)
            {
                game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i + 1), off[i]);
                game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i + 1), def[i]);
            }

            var gen = new RollHGenerator(cfgH, cfgM, game);
            var worst = 0.0; var rows = 0; var worstLabel = "";
            foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
            {
                var zone = Enum.Parse<ShotLocation>(row.GetProperty("zone").GetString()!);
                var shooter = row.GetProperty("shooter").GetInt32();
                var putback = row.GetProperty("putback").GetBoolean();
                var fastBreak = row.GetProperty("fastBreak").GetBoolean();
                var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                    SelectedSlot: game.HomeLineup.SlotAt(shooter), ShotType: zone, FastBreak: fastBreak)
                    { ReboundSlot = game.HomeLineup.SlotAt(shooter) };
                // A defaulted context — S88 must not reach into a break putback or a halfcourt shot.
                var pie = gen.Generate(st, putback);
                foreach (var (outcome, weight) in pie.Slices)
                {
                    var expected = double.Parse(row.GetProperty("weights").GetProperty(outcome.ToString()).GetString()!,
                                                CultureInfo.InvariantCulture);
                    var d = Math.Abs(weight - expected);
                    if (d > worst) { worst = d; worstLabel = $"{zone}/{shooter}/pb={putback}/fb={fastBreak}/{outcome}"; }
                }
                rows++;
            }
            Check($"A10 halfcourt + break-putback pies unchanged vs the pre-S88 tree ({rows} fixtures)",
                  rows == 45 && worst < Tight,
                  $"worst |Δ| {worst:0.0e+00}" + (worst > 0 ? $" at {worstLabel}" : ""));

            // Negative control for the bound: construct the leak this check exists to catch —
            // a contest reaching a shot it has no business touching — and measure its size.
            var leakState = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                SelectedSlot: game.HomeLineup.SlotAt(1), ShotType: ShotLocation.Rim, FastBreak: false);
            var clean = gen.Generate(leakState, putback: false);
            var leaked = gen.Generate(leakState, putback: false,
                contest: new TransitionContest(game.AwayLineup.SlotAt(5), def[4], 1.0, 1.0));
            var leakSize = clean.Slices.Zip(leaked.Slices)
                                .Max(p => Math.Abs(p.First.Weight - p.Second.Weight));
            Check("A10 negative control — a real scope leak is orders larger than the bound",
                  leakSize > 1e-4,
                  $"leak {leakSize:0.0e+00} vs bound {Tight:0.0e+00} " +
                  $"({Math.Log10(leakSize / Tight):F0} orders of margin)");
        }
        catch (Exception ex)
        {
            Check("A10 scope isolation", false, ex.Message);
        }

        // ── A11 — RETIRED-WIRE ABSENCE ───────────────────────────────────────
        {
            var liveKeys = new List<string>();
            using (var doc = JsonDocument.Parse(File.ReadAllText(configPath)))
                foreach (var p in doc.RootElement.GetProperty("Matchup").EnumerateObject())
                    if (p.Name.StartsWith("HustleTransitionDefense", StringComparison.Ordinal))
                        liveKeys.Add(p.Name);
            Check("A11 no HustleTransitionDefense* key survives in config.json",
                  liveKeys.Count == 0, liveKeys.Count == 0 ? "all four retired" : string.Join(", ", liveKeys));

            var props = typeof(MatchupConfig).GetProperties()
                            .Where(p => p.Name.StartsWith("HustleTransitionDefense", StringComparison.Ordinal))
                            .Select(p => p.Name).ToList();
            Check("A11 no HustleTransitionDefense* property survives on MatchupConfig",
                  props.Count == 0, props.Count == 0 ? "all four retired" : string.Join(", ", props));

            // The general Hustle helpers SURVIVE — they still serve the rebound and pressure
            // doors. Retiring them would be a silent regression in two unrelated places.
            var gapAlive = Math.Abs(Matchup.HustleGap(
                new Player?[] { Mk("a", hustle: 80) }, new Player?[] { Mk("b", hustle: 20) })) > 0.0;
            Check("A11 Matchup.HustleGap / HustleGapShift SURVIVE (rebound + pressure doors)",
                  gapAlive);
        }

        // ── A12 — GUARDS AND NEUTRALITY, separately ──────────────────────────
        // (a) Each property loads at every INCLUDED boundary and rejects the nearest EXCLUDED
        //     one. [0,1) admits 0 and rejects 1; (0,1] rejects 0 and admits 1 — asserted
        //     per-property rather than uniformly, because "throws at its boundary" is not true
        //     of all of them.
        {
            var baseJson = File.ReadAllText(configPath);
            bool Loads(string key, double value)
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(baseJson)!;
                node["Matchup"]![key] = value;
                var tmp = Path.Combine(Path.GetTempPath(), $"charm_s88_{Guid.NewGuid():N}.json");
                try
                {
                    File.WriteAllText(tmp, node.ToJsonString());
                    MatchupConfig.Load(tmp);
                    return true;
                }
                catch (InvalidOperationException) { return false; }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            var guardFailures = new List<string>();
            void Guard(string key, double value, bool shouldLoad)
            {
                if (Loads(key, value) != shouldLoad)
                    guardFailures.Add($"{key}={value} should {(shouldLoad ? "LOAD" : "REJECT")}");
            }

            // [0,1) — admits 0, rejects 1.
            foreach (var k in new[] { "TransitionLegsSpan", "TransitionDepthSpan", "TransitionArrivalSpan" })
            { Guard(k, 0.0, true); Guard(k, 1.0, false); Guard(k, -1e-9, false); Guard(k, 2.0, false); }
            // [0,1] — admits both ends.
            Guard("TransitionEffortSpeedShare", 0.0, true);
            Guard("TransitionEffortSpeedShare", 1.0, true);
            Guard("TransitionEffortSpeedShare", -1e-9, false);
            Guard("TransitionEffortSpeedShare", 1.0 + 1e-9, false);
            // (0,1] — rejects 0, admits 1.
            Guard("TransitionContestDiscount", 0.0, false);
            Guard("TransitionContestDiscount", 1.0, true);
            Guard("TransitionContestDiscount", 1.0 + 1e-9, false);
            // (0,1) — rejects both ends.
            foreach (var k in new[] { "TransitionBaseBreakMake", "TransitionBaseBreakBlock" })
            { Guard(k, 0.0, false); Guard(k, 1.0, false); }
            // >= 0 — admits 0, rejects negative.
            foreach (var k in new[] { "TransitionGotBackLuckFloor", "TransitionRimProtectionSwing",
                                      "TransitionTeamPresenceSwing", "TransitionChaseSwing",
                                      "TransitionChaseSpeedSwing" })
            { Guard(k, 0.0, true); Guard(k, -1e-9, false); }
            // > 0 — rejects 0.
            foreach (var k in new[] { "TransitionPostnessScale", "TransitionShooterZoneRim",
                                      "TransitionShooterZoneShort", "TransitionShooterZoneMid",
                                      "TransitionShooterZoneLong", "TransitionShooterZoneThree" })
            { Guard(k, 0.0, false); Guard(k, -1.0, false); }
            // The chase weights must still sum to 1.
            Guard("TransitionChaseLengthWeight", 0.5, false);   // 0.5 + 0.30 != 1

            Check($"A12(a) config guards at every boundary ({(guardFailures.Count == 0 ? "all" : "some")} correct)",
                  guardFailures.Count == 0,
                  guardFailures.Count == 0 ? "included boundaries load, excluded ones reject"
                                           : string.Join("; ", guardFailures.Take(4)));

            // (b) THE NEUTRAL VECTOR. No single dial is a kill switch — measured, not intuited.
            // The complete vector is the four OUTCOME swings; with all four at zero the four
            // rates are the configured bases for EVERY lineup.
            var neutral = MatchupConfig.Load(configPath);
            neutral.TransitionRimProtectionSwing = 0.0;
            neutral.TransitionTeamPresenceSwing  = 0.0;
            neutral.TransitionChaseSwing         = 0.0;
            neutral.TransitionChaseSpeedSwing    = 0.0;

            var worstFg = 0.0; var worstBlk = 0.0; var lineupsTested = 0;
            var rnd = new Random(88088);
            for (var t = 0; t < 360; t++)
            {
                var d = Enumerable.Range(0, 5).Select(i => (Player?)Mk($"d{i}",
                            speed: rnd.Next(1, 100), hustle: rnd.Next(1, 100), rimP: rnd.Next(1, 100),
                            height: rnd.Next(1, 100), wingspan: rnd.Next(1, 100), vertical: rnd.Next(1, 100))).ToArray();
                var o = Enumerable.Range(0, 5).Select(i => (Player?)MkPost($"o{i}", rnd.Next(1, 100))).ToArray();
                var w = TransitionDefense.LineupGotBack(d, o, null, null, neutral);
                var agg = TransitionDefense.TeamAggregate(w, neutral);
                for (var i = 0; i < 5; i++)
                {
                    worstFg = Math.Max(worstFg, Math.Abs(
                        TransitionDefense.BreakMakePct(d[i]!, w[i], agg, neutral) - neutral.TransitionBaseBreakMake));
                    worstBlk = Math.Max(worstBlk, Math.Abs(
                        TransitionDefense.BreakBlockPct(d[i]!, w[i], neutral) - neutral.TransitionBaseBreakBlock));
                }
                lineupsTested++;
            }
            Check($"A12(b) the four-value neutral vector returns the base rates for every lineup ({lineupsTested})",
                  worstFg < Tight && worstBlk < Tight,
                  $"worst |ΔFG| {worstFg:0.000e+00}, worst |ΔBLK| {worstBlk:0.000e+00}");

            // And prove no SINGLE dial is a kill switch — otherwise (b) is asserting nothing.
            var oneDial = MatchupConfig.Load(configPath);
            oneDial.TransitionLegsSpan = 0.0;
            var stillMoves = false;
            var rnd2 = new Random(88089);
            for (var t = 0; t < 60 && !stillMoves; t++)
            {
                var d = Enumerable.Range(0, 5).Select(i => (Player?)Mk($"d{i}",
                            speed: rnd2.Next(1, 100), rimP: rnd2.Next(1, 100))).ToArray();
                var o = Enumerable.Range(0, 5).Select(i => (Player?)MkPost($"o{i}", rnd2.Next(1, 100))).ToArray();
                var w = TransitionDefense.LineupGotBack(d, o, null, null, oneDial);
                var agg = TransitionDefense.TeamAggregate(w, oneDial);
                if (Math.Abs(TransitionDefense.BreakMakePct(d[0]!, w[0], agg, oneDial) - oneDial.TransitionBaseBreakMake) > 1e-6)
                    stillMoves = true;
            }
            Check("A12(b) no SINGLE dial is a kill switch (legs span alone at zero still moves the game)",
                  stillMoves);
        }

        // ── A13 — CONSERVATION HYGIENE ───────────────────────────────────────
        // These cannot detect A-1, A-2 or A-3. They are hygiene, not evidence, and are
        // labelled as such so no future session mistakes a green line here for proof that
        // the lifecycle is right.
        {
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 1; i <= 5; i++)
            {
                game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"o{i}", speed: 40 + 8 * i, height: 40 + 9 * i, postD: 40 + 9 * i, strength: 40 + 9 * i));
                game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"d{i}", speed: 80 - 9 * i, rimP: 30 + 12 * i, height: 40 + 9 * i, wingspan: 40 + 9 * i));
            }
            var rng = new SystemRng(883000);
            var resolver = new Resolver(
                new RollAGenerator(RollAConfig.Load(configPath), cfgM, game), RollAConfig.Load(configPath),
                new RollBGenerator(RollBConfig.Load(configPath), cfgM, game),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(cfgD),
                new RollEGenerator(RollEConfig.Load(configPath), game),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFGenerator(RollFConfig.Load(configPath), cfgM, game),
                new RollGGenerator(RollGConfig.Load(configPath), cfgM, game),
                new RollHGenerator(cfgH, cfgM, game),
                new RollIGenerator(RollIConfig.Load(configPath), cfgM, game),
                new RollJGenerator(RollJConfig.Load(configPath), cfgM, game),
                new RollKGenerator(RollKConfig.Load(configPath), cfgM, game),
                new RollLGenerator(RollLConfig.Load(configPath), game),
                new RollMGenerator(RollMConfig.Load(configPath), cfgM, game),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);
            var governor = new Governor(resolver, game, GovernorConfig.Load(configPath),
                                        RollClockConfig.Load(configPath), new SystemRng(884000),
                                        EndOfHalfConfig.Load(configPath));
            var recs = governor.Run(new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound)).Possessions;

            var seatTotalOk = recs.All(r => r.FastBreakBlkBySlot.Total == r.FastBreakBlk);
            Check("A13 (hygiene) FastBreakBlkBySlot.Total == FastBreakBlk on every possession", seatTotalOk);
            var shotsOk = recs.All(r => r.FastBreakFga + r.BreakPutbackFga + r.NonBreakFga == r.Fga);
            Check("A13 (hygiene) the three shot buckets sum to FGA", shotsOk);
            var blocksOk = recs.All(r => r.FastBreakBlk + r.BreakPutbackBlk + r.NonBreakBlk == r.BlkCount);
            Check("A13 (hygiene) the three block buckets sum to BlkCount", blocksOk);
        }

        Console.WriteLine($"\n  Phase 79 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
