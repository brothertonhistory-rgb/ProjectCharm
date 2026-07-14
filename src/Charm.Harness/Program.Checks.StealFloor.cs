using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 64 (Session 58) — the live steal-forcing FLOOR: golden parity + wiring.
//
//  The term (Matchup.StealFloorShift, live at neutral pressure):
//      stealFloorShift = GapFn(athGap,   AthStealSteepness,   AthStealExponent,   AthStealScale)
//                      + GapFn(stealGap, StealFloorSteepness, StealFloorExponent, ReferenceScale)
//                      + WingStealWeight * tanh(wingSigned / WingStealScale)
//    athGap   = defAth − handlerAth,  ath = (Quickness+FirstStep)/2      (PRIMARY)
//    stealGap = defender.Steals − handler.BallHandling                    (secondary)
//    wingSigned = (defender.Wingspan − WingStealRef) × perimW
//    perimW   = Matchup.WingStealPerimWeight(Matchup.Postness(def), cfg)  (guard→1, big→PerimFloor)
//  It REPLACES the old pressure-gated skill contest, which was inert at today's neutral
//  pressure (pressureGate = 0). pressureLift (0 at neutral) stays for the coaching layer.
//
//  Golden fixture tools/steal_floor_golden.json is emitted by tools/steal_floor_oracle.py
//  (LOCKED shape; provisional magnitudes). 18 cases: 12 signed-off archetypes + 6 boundary
//  rows pinning the continuous perimeter gate (pivot / mid / range / both clamps / wing-ref
//  zero). Constants are cross-checked against the loaded MatchupConfig before a single number
//  is trusted, so silent drift between fixture and config fails loudly.
//
//  Per case: |postness|, |perimW|, and |stealFloorShift| all within 1e-12. The fixture locks
//  the SHIFT, not credited steals — the credited-steal RISE is proven separately below.
// ============================================================================
internal static partial class Program
{
    private static bool Phase64StealFloorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 64: live steal-forcing floor (golden parity + wiring + config guards) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgM = MatchupConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // A uniform all-50 player, overriding only the attributes the floor reads. Every
        // other rating is 50 so the isolated reads are unconfounded.
        static Player Mk(string id, int q = 50, int fs = 50, int steals = 50, int bh = 50,
                         int wing = 50, int height = 50, int postDef = 50, int strength = 50, int hustle = 50)
            => new Player(id)
            {
                PlayerId = Math.Abs(id.GetHashCode()) % 100000,
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                BallHandling = bh, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
                PostDefense = postDef, RimProtection = 50, DefensiveRebounding = 50, Steals = steals,
                HelpDefense = 50, OffBallDefense = 50, Height = height, Wingspan = wing, Weight = 50,
                Strength = strength, Speed = 50, Quickness = q, FirstStep = fs, Vertical = 50,
                Endurance = 50, Hustle = hustle, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
            };

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/steal_floor_golden.json.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity (18 cases, |Δ| <= 1e-12 on shift/postness/perimW):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "steal_floor_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // ── fixture contract — constants validated loudly BEFORE trusting a number ──
            var kc = root.GetProperty("constants");
            bool ConstOk(string key, double live) => kc.GetProperty(key).GetDouble() == live;
            if (!(ConstOk("AthStealSteepness", cfgM.AthStealSteepness) &&
                  ConstOk("AthStealExponent", cfgM.AthStealExponent) &&
                  ConstOk("AthStealScale", cfgM.AthStealScale) &&
                  ConstOk("StealFloorSteepness", cfgM.StealFloorSteepness) &&
                  ConstOk("StealFloorExponent", cfgM.StealFloorExponent) &&
                  ConstOk("ReferenceScale", cfgM.ReferenceScale) &&
                  ConstOk("WingStealWeight", cfgM.WingStealWeight) &&
                  ConstOk("WingStealScale", cfgM.WingStealScale) &&
                  ConstOk("WingStealRef", cfgM.WingStealRef) &&
                  ConstOk("WingStealPostnessPivot", cfgM.WingStealPostnessPivot) &&
                  ConstOk("WingStealPostnessRange", cfgM.WingStealPostnessRange) &&
                  ConstOk("WingStealPerimFloor", cfgM.WingStealPerimFloor)))
                throw new InvalidOperationException(
                    "golden fixture rejected: floor constants do not match the loaded MatchupConfig. " +
                    "Regenerate the fixture or fix the config.");

            var tol = root.GetProperty("tolerance").GetProperty("steal_floor_shift").GetDouble();
            var cases = root.GetProperty("cases");
            if (cases.GetArrayLength() != 18)
                throw new InvalidOperationException($"golden fixture rejected: expected 18 cases, got {cases.GetArrayLength()}.");

            var worstShift = 0.0; var worstPost = 0.0; var worstPerim = 0.0; var allOk = true;
            foreach (var c in cases.EnumerateArray())
            {
                var d = c.GetProperty("def");
                var h = c.GetProperty("handler");
                var def = Mk("d", q: d.GetProperty("Quickness").GetInt32(), fs: d.GetProperty("FirstStep").GetInt32(),
                             steals: d.GetProperty("Steals").GetInt32(), wing: d.GetProperty("Wingspan").GetInt32(),
                             height: d.GetProperty("Height").GetInt32(), postDef: d.GetProperty("PostDefense").GetInt32(),
                             strength: d.GetProperty("Strength").GetInt32());
                var handler = Mk("h", q: h.GetProperty("Quickness").GetInt32(), fs: h.GetProperty("FirstStep").GetInt32(),
                                 bh: h.GetProperty("BallHandling").GetInt32());

                var postness = Matchup.Postness(def, cfgM);
                var perimW   = Matchup.WingStealPerimWeight(postness, cfgM);
                var athGap   = ((double)def.Quickness + def.FirstStep) / 2.0
                             - ((double)handler.Quickness + handler.FirstStep) / 2.0;
                var stealGap = (double)def.Steals - handler.BallHandling;
                var wingSigned = ((double)def.Wingspan - cfgM.WingStealRef) * perimW;
                var shift    = Matchup.StealFloorShift(athGap, stealGap, wingSigned, cfgM);

                var dShift = Math.Abs(shift    - c.GetProperty("steal_floor_shift").GetDouble());
                var dPost  = Math.Abs(postness - c.GetProperty("postness").GetDouble());
                var dPerim = Math.Abs(perimW   - c.GetProperty("perimW").GetDouble());
                worstShift = Math.Max(worstShift, dShift);
                worstPost  = Math.Max(worstPost, dPost);
                worstPerim = Math.Max(worstPerim, dPerim);
                if (dShift > tol || dPost > tol || dPerim > tol)
                {
                    allOk = false;
                    Console.WriteLine($"      MISMATCH [{c.GetProperty("name").GetString()}] " +
                        $"dShift={dShift:E3} dPost={dPost:E3} dPerim={dPerim:E3}");
                }
            }
            Check($"golden parity (worst dShift={worstShift:E3}, dPost={worstPost:E3}, dPerim={worstPerim:E3})", allOk);
        }
        catch (Exception ex) { pass = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }

        // ----------------------------------------------------------------
        // (2) Floor shape — LIVE at neutral, elite handler resists, wingspan two-sided + gated.
        // ----------------------------------------------------------------
        {
            double Shift(Player def, Player handler)
            {
                var perimW = Matchup.WingStealPerimWeight(Matchup.Postness(def, cfgM), cfgM);
                var athGap = ((double)def.Quickness + def.FirstStep) / 2.0
                           - ((double)handler.Quickness + handler.FirstStep) / 2.0;
                var stealGap = (double)def.Steals - handler.BallHandling;
                var wingSigned = ((double)def.Wingspan - cfgM.WingStealRef) * perimW;
                return Matchup.StealFloorShift(athGap, stealGap, wingSigned, cfgM);
            }

            var quickLong = Mk("ql", q: 85, fs: 85, steals: 80, wing: 80, height: 48, postDef: 45, strength: 48);
            var plodder   = Mk("plod", q: 35, fs: 35, bh: 40);
            var elite     = Mk("elite", q: 70, fs: 70, bh: 90);
            var avgH      = Mk("avgH");

            Check("floor LIVE at neutral (quick/long vs plodder shift > 0)", Shift(quickLong, plodder) > 0.0,
                  $"shift={Shift(quickLong, plodder):F4}");
            Check("elite handler resists (vs elite < vs plodder)", Shift(quickLong, elite) < Shift(quickLong, plodder),
                  $"elite={Shift(quickLong, elite):F4} plod={Shift(quickLong, plodder):F4}");

            // Wingspan two-sided + perimeter-gated (guard, avg quicks): long > 0 > short.
            var longGuard  = Mk("lg", wing: 88);
            var shortGuard = Mk("sg", wing: 40);
            var shortBig   = Mk("sb", wing: 40, height: 80, postDef: 80, strength: 80); // postness high → gated
            Check("long-armed guard adds (shift > 0)", Shift(longGuard, avgH) > 0.0, $"shift={Shift(longGuard, avgH):F4}");
            Check("short-armed guard costs (shift < 0)", Shift(shortGuard, avgH) < 0.0, $"shift={Shift(shortGuard, avgH):F4}");
            Check("short-armed big barely moves (|shift| < short guard)",
                  Math.Abs(Shift(shortBig, avgH)) < Math.Abs(Shift(shortGuard, avgH)),
                  $"big={Shift(shortBig, avgH):F4} guard={Shift(shortGuard, avgH):F4}");
        }

        // ----------------------------------------------------------------
        // (3) Athleticism is PRIMARY — parameter-level invariant at g ∈ {10, 20, 40}.
        //     |GapFn(g, ath knobs)| > |GapFn(g, steal knobs)|, not merely archetype ordering.
        // ----------------------------------------------------------------
        {
            var athPrimary = true; var detail = "";
            foreach (var g in new[] { 10.0, 20.0, 40.0 })
            {
                var ath   = Math.Abs(Matchup.GapFn(g, cfgM.AthStealSteepness, cfgM.AthStealExponent, cfgM.AthStealScale));
                var steal = Math.Abs(Matchup.GapFn(g, cfgM.StealFloorSteepness, cfgM.StealFloorExponent, cfgM.ReferenceScale));
                if (!(ath > steal)) athPrimary = false;
                detail += $"g{g:F0}: ath={ath:F3}>steal={steal:F3}; ";
            }
            Check("athleticism primary at g∈{10,20,40}", athPrimary, detail.Trim());
        }

        // ----------------------------------------------------------------
        // (4) Kill switch reproduces today at neutral; floor moves the turnover share.
        //     Focused proof of A1 (the full-suite byte-for-byte parity is Emmett's run).
        // ----------------------------------------------------------------
        {
            var pressure = cfgM.PressureNeutral;   // 5.0 → pUnit 0 → today's flat behavior
            var baseTO   = 0.05; var baseFoul = 0.05;
            var quickLong = Mk("ql2", q: 85, fs: 85, steals: 80, wing: 80, height: 48, postDef: 45, strength: 48);
            var avgH      = Mk("avgH2");

            var live = MatchupConfig.Load(configPath);
            var (liveTO, _) = Matchup.DisruptionShares(avgH, quickLong, pressure, baseTO, baseFoul, live);

            var kill = MatchupConfig.Load(configPath);
            kill.AthStealSteepness = 0.0; kill.StealFloorSteepness = 0.0; kill.WingStealWeight = 0.0;
            var (killTO, _) = Matchup.DisruptionShares(avgH, quickLong, pressure, baseTO, baseFoul, kill);

            Check("kill switch = today's flat baseline at neutral (killTO == baseTO)",
                  killTO == baseTO, $"killTO={killTO:R} baseTO={baseTO:R}");
            Check("floor LIVE moves the share (liveTO > baseTO for quick/long def)",
                  liveTO > baseTO, $"liveTO={liveTO:F5} baseTO={baseTO:F5}");
        }

        // ----------------------------------------------------------------
        // (5) Credited steals RISE (via the real wiring): expected credited steals per
        //     turnover-routed possession = turnoverShare × Roll-C live-ball fraction
        //     (BadPassIntercepted + LostBallLiveBall). A quick/long defender lifts the
        //     turnover share, so more live-ball turnovers reach StealerPicker → more steals.
        //     The empirical corpus count is confirmed on the full-suite/season run.
        // ----------------------------------------------------------------
        {
            var liveFraction = cfgC.BaseBadPassIntercepted + cfgC.BaseLostBallLiveBall;
            var pressure = cfgM.PressureNeutral;
            var baseTO = 0.05; var baseFoul = 0.05;
            var quickLong = Mk("ql3", q: 85, fs: 85, steals: 80, wing: 80, height: 48, postDef: 45, strength: 48);
            var flatDef   = Mk("flat");
            var avgH      = Mk("avgH3");

            var (toQL, _)   = Matchup.DisruptionShares(avgH, quickLong, pressure, baseTO, baseFoul, cfgM);
            var (toFlat, _) = Matchup.DisruptionShares(avgH, flatDef,   pressure, baseTO, baseFoul, cfgM);
            var stealsQL   = toQL   * liveFraction;
            var stealsFlat = toFlat * liveFraction;

            Check($"expected credited steals rise (liveFrac={liveFraction:F3})", stealsQL > stealsFlat,
                  $"quick/long={stealsQL:F5} vs flat-50={stealsFlat:F5} per turnover-routed possession");
        }

        // ----------------------------------------------------------------
        // (6) Attribution tilt — StealerPicker, seeded batch. A longer-armed perimeter
        //     defender earns a slightly larger credited share than a short-armed one; the
        //     guard-favoring postness gate still dominates (a long-armed BIG stays low).
        // ----------------------------------------------------------------
        {
            const int N = 400_000;

            double ShareOfSlot1(Player slot1)
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                // Defense = Away; slot 1 is the varied perimeter defender, slots 2–5 flat guards.
                var def = new[] { slot1, Mk("d2"), Mk("d3"), Mk("d4"), Mk("d5") };
                var off = new[] { Mk("o1"), Mk("o2"), Mk("o3"), Mk("o4"), Mk("o5") };
                for (var i = 0; i < 5; i++) g.RosterFor(TeamSide.Home).SetStarter(g.LineupFor(TeamSide.Home).SlotAt(i + 1), off[i]);
                for (var i = 0; i < 5; i++) g.RosterFor(TeamSide.Away).SetStarter(g.LineupFor(TeamSide.Away).SlotAt(i + 1), def[i]);
                var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
                var rng = new SystemRng(20258);
                var hits = 0;
                for (var k = 0; k < N; k++)
                    if (StealerPicker.Pick(st, g, cfgM, rng).Number == 1) hits++;
                return (double)hits / N;
            }

            var longShare  = ShareOfSlot1(Mk("d1long",  wing: 88));   // guard, long arms
            var shortShare = ShareOfSlot1(Mk("d1short", wing: 40));   // guard, short arms
            var bigShare   = ShareOfSlot1(Mk("d1big",   wing: 88, height: 82, postDef: 80, strength: 80)); // long-armed BIG

            Check("longer arms earn a larger credited share (long > short)", longShare > shortShare,
                  $"long={longShare:F4} short={shortShare:F4}");
            Check("postness gate still dominates (long-armed big share < long-armed guard)", bigShare < longShare,
                  $"big={bigShare:F4} guard={longShare:F4}");
        }

        // ----------------------------------------------------------------
        // (7) Foul path independent (RAW) — no new attribute enters the foul calculation:
        //     the raw finalFoulShare is bit-identical across a wingspan/athleticism walk.
        //     (Any FINAL normalized foul-slice movement is a renorm ULP against the changed
        //     turnover share, not a foul wire — reported separately by the season run.)
        // ----------------------------------------------------------------
        {
            var pressure = cfgM.PressureNeutral;
            var baseTO = 0.05; var baseFoul = 0.05;
            var avgH = Mk("avgH4");
            var (_, foulRef) = Matchup.DisruptionShares(avgH, Mk("dref"), pressure, baseTO, baseFoul, cfgM);
            var foulOk = true; var detail = "";
            foreach (var (q, w) in new[] { (99, 99), (99, 40), (40, 99), (30, 30), (85, 88) })
            {
                var def = Mk("dw", q: q, fs: q, wing: w, steals: 80);
                var (_, foul) = Matchup.DisruptionShares(avgH, def, pressure, baseTO, baseFoul, cfgM);
                if (foul != foulRef) { foulOk = false; detail += $"(q{q},w{w})={foul:R} "; }
            }
            Check("raw finalFoulShare bit-identical across ath/wing walk", foulOk,
                  foulOk ? $"foulShare={foulRef:R}" : detail.Trim());
        }

        // ----------------------------------------------------------------
        // (8) Config guards — Load throws on each out-of-range/non-finite value; the
        //     all-off kill switch loads cleanly.
        // ----------------------------------------------------------------
        {
            static string MutatedConfig(string configPath, string key, double value)
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["Matchup"]![key] = value;
                var tmp = Path.Combine(Path.GetTempPath(), $"sf_cfg_{key}_{Guid.NewGuid():N}.json");
                File.WriteAllText(tmp, node.ToJsonString());
                return tmp;
            }
            static bool Throws(string path)
            {
                try { MatchupConfig.Load(path); return false; }
                catch (InvalidOperationException) { return true; }
                finally { try { File.Delete(path); } catch { /* best-effort */ } }
            }

            Check("negative AthStealSteepness throws",   Throws(MutatedConfig(configPath, "AthStealSteepness", -0.1)));
            Check("AthStealExponent = 0 throws",         Throws(MutatedConfig(configPath, "AthStealExponent", 0.0)));
            Check("AthStealScale = 0 throws",            Throws(MutatedConfig(configPath, "AthStealScale", 0.0)));
            Check("negative StealFloorSteepness throws", Throws(MutatedConfig(configPath, "StealFloorSteepness", -0.1)));
            Check("StealFloorExponent = 0 throws",       Throws(MutatedConfig(configPath, "StealFloorExponent", 0.0)));
            Check("negative WingStealWeight throws",     Throws(MutatedConfig(configPath, "WingStealWeight", -0.1)));
            Check("WingStealScale = 0 throws",           Throws(MutatedConfig(configPath, "WingStealScale", 0.0)));
            Check("WingStealPostnessRange = 0 throws",   Throws(MutatedConfig(configPath, "WingStealPostnessRange", 0.0)));
            Check("WingStealPerimFloor > 1 throws",      Throws(MutatedConfig(configPath, "WingStealPerimFloor", 1.5)));
            Check("WingStealPerimFloor < 0 throws",      Throws(MutatedConfig(configPath, "WingStealPerimFloor", -0.1)));
            Check("WingspanStealerScale = 0 throws",     Throws(MutatedConfig(configPath, "WingspanStealerScale", 0.0)));
            Check("WingspanStealerSteepness > 1 throws", Throws(MutatedConfig(configPath, "WingspanStealerSteepness", 1.5)));

            // All-off kill switch (three floor knobs 0) must remain LEGAL.
            var node = JsonNode.Parse(File.ReadAllText(configPath))!;
            node["Matchup"]!["AthStealSteepness"] = 0.0;
            node["Matchup"]!["StealFloorSteepness"] = 0.0;
            node["Matchup"]!["WingStealWeight"] = 0.0;
            var killPath = Path.Combine(Path.GetTempPath(), $"sf_kill_{Guid.NewGuid():N}.json");
            File.WriteAllText(killPath, node.ToJsonString());
            var killLoads = false;
            try { MatchupConfig.Load(killPath); killLoads = true; }
            catch { killLoads = false; }
            finally { try { File.Delete(killPath); } catch { /* best-effort */ } }
            Check("all-off kill switch loads cleanly", killLoads);
        }

        Console.WriteLine($"  Phase 64 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
