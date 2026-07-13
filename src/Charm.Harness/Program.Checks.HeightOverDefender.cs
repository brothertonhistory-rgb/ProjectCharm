using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 61 (Session 55) — the height-over-defender make term: golden parity.
//
//  The term: heightShift = HeightZoneWeight(zone) * HeightMaxBonus
//                        * tanh(max(0, shooterReach - defenderReach) / HeightReferenceScale),
//  reach = (Height + Wingspan) / 2.0, added to Matchup.EffectiveRating (make door only).
//
//  The committed golden fixture tools/height_over_defender_golden.json is emitted by
//  tools/height_over_defender_oracle.py (LOCKED constants, signed on the Session 54
//  archetype table; make%% column regenerated against the LIVE recentered curve at
//  Session 55). 25 cases: 5 archetypes x 5 zones (including Long — the only case that
//  proves HeightWeightLong and its accessor branch are wired).
//
//  Dual tolerance per case: |effRating - expected| <= 1e-6 AND
//  |makeProb - expected| <= 1e-9. Rating-only parity proves the shift entered; the
//  make-%% tolerance additionally catches wrong-curve-after-right-rating, zone-mapping,
//  and rounding-between-stages errors. Exact-zero refers to the HEIGHT CONTRIBUTION
//  (heightShift == 0 and make_delta_vs_no_term == 0), never the total rating — the
//  skill/physical shifts still apply (SMALL_ON_BIG legitimately sits at 61.5 from the
//  athletic edge).
//
//  NOTE: the fixture's make%% column is only valid against the live config.json curve
//  (the recentered midpoints). The constants block is cross-checked against the loaded
//  MatchupConfig before a single case is trusted, so silent drift between fixture and
//  config fails loudly, not as 25 mysterious mismatches.
// ============================================================================
internal static partial class Program
{
    private static bool Phase61HeightOverDefenderCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 61: height-over-defender make term (golden parity + helpers + config guards) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgM = MatchupConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);

        // A uniform all-50 player overriding only the body (Height/Wingspan) and the five
        // athletic attributes — mirrors the Shooting check's Mk so skill = 50 in every zone
        // and the defensive blend reads exactly 50. Isolates the reach + athleticism reads.
        static Player Mk(int id, int height, int wingspan, int ath) => new Player($"hod{id}")
        {
            PlayerId = id,
            Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
            RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
            BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
            OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
            PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
            OffBallDefense = 50, Height = height, Wingspan = wingspan, Weight = 50,
            Strength = ath, Speed = ath, Quickness = ath, FirstStep = ath, Vertical = ath,
            Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
        };

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/height_over_defender_golden.json.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity (25 cases, dual tolerance):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "height_over_defender_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // ── fixture contract — validated loudly BEFORE trusting a single number ──
            var kc = root.GetProperty("constants");
            if (kc.GetProperty("HeightMaxBonus").GetDouble() != cfgM.HeightMaxBonus ||
                kc.GetProperty("HeightReferenceScale").GetDouble() != cfgM.HeightReferenceScale)
                throw new InvalidOperationException(
                    "golden fixture rejected: HeightMaxBonus/HeightReferenceScale do not match the " +
                    $"loaded MatchupConfig ({kc.GetProperty("HeightMaxBonus").GetDouble()}/{kc.GetProperty("HeightReferenceScale").GetDouble()} " +
                    $"vs {cfgM.HeightMaxBonus}/{cfgM.HeightReferenceScale}). Regenerate the fixture or fix the config.");
            var zw = kc.GetProperty("HeightZoneWeight");
            foreach (var (zoneName, zone) in new[]
                     {
                         ("Rim", ShotLocation.Rim), ("Short", ShotLocation.Short), ("Mid", ShotLocation.Mid),
                         ("Long", ShotLocation.Long), ("Three", ShotLocation.Three),
                     })
                if (zw.GetProperty(zoneName).GetDouble() != cfgM.HeightZoneWeight(zone))
                    throw new InvalidOperationException(
                        $"golden fixture rejected: HeightZoneWeight.{zoneName} " +
                        $"({zw.GetProperty(zoneName).GetDouble()}) does not match the loaded config " +
                        $"({cfgM.HeightZoneWeight(zone)}). Regenerate the fixture or fix the config.");

            var tolRating = root.GetProperty("tolerance").GetProperty("effective_rating").GetDouble();
            var tolMake   = root.GetProperty("tolerance").GetProperty("make_probability").GetDouble();

            var cases = root.GetProperty("cases");
            if (cases.GetArrayLength() != 25)
                throw new InvalidOperationException(
                    $"golden fixture rejected: expected 25 cases (5 archetypes x 5 zones), got {cases.GetArrayLength()}.");

            var ratingOk = true; var makeOk = true; var zeroOk = true; var longOk = true;
            var worstRating = 0.0; var worstMake = 0.0;
            var id = 0;
            foreach (var c in cases.EnumerateArray())
            {
                var arch = c.GetProperty("archetype").GetString()!;
                var zone = Enum.Parse<ShotLocation>(c.GetProperty("zone").GetString()!);
                var shooter  = Mk(++id, c.GetProperty("shooter_height").GetInt32(),
                                        c.GetProperty("shooter_wingspan").GetInt32(),
                                        c.GetProperty("shooter_ath").GetInt32());
                var defender = Mk(++id, c.GetProperty("defender_height").GetInt32(),
                                        c.GetProperty("defender_wingspan").GetInt32(),
                                        c.GetProperty("defender_ath").GetInt32());

                var eff  = Matchup.EffectiveRating(zone, shooter, defender, cfgM);
                var make = cfgH.MakeProbability(zone, eff);
                var hShift = Matchup.HeightOverDefenderShift(zone, shooter, defender, cfgM);
                var makeNoTerm = cfgH.MakeProbability(zone, eff - hShift);

                var dRating = Math.Abs(eff  - c.GetProperty("effective_rating").GetDouble());
                var dMake   = Math.Abs(make - c.GetProperty("make_probability").GetDouble());
                worstRating = Math.Max(worstRating, dRating);
                worstMake   = Math.Max(worstMake,   dMake);
                if (dRating > tolRating) { ratingOk = false; Console.WriteLine($"      rating miss {arch}/{zone}: {eff:R} vs {c.GetProperty("effective_rating").GetDouble():R}"); }
                if (dMake   > tolMake)   { makeOk   = false; Console.WriteLine($"      make% miss {arch}/{zone}: {make:R} vs {c.GetProperty("make_probability").GetDouble():R}"); }

                // Exact-zero = the HEIGHT CONTRIBUTION, not the total rating.
                var mustBeZero = arch == "POST_VS_POST" || arch == "SMALL_ON_BIG" || zone == ShotLocation.Three;
                if (mustBeZero && (hShift != 0.0 || make - makeNoTerm != 0.0))
                { zeroOk = false; Console.WriteLine($"      exact-zero miss {arch}/{zone}: hShift={hShift:R}, makeDelta={make - makeNoTerm:R}"); }

                // Long must be small-but-positive on positive reach gaps — the only proof
                // HeightWeightLong and its accessor branch are wired.
                if (zone == ShotLocation.Long && !mustBeZero && !(hShift > 0.0 && make - makeNoTerm > 0.0))
                { longOk = false; Console.WriteLine($"      Long miss {arch}: hShift={hShift:R} (expected small-positive)"); }
            }
            Check($"effective rating parity, all 25 cases within {tolRating:0e0}", ratingOk, $"worst {worstRating:0.0e0}");
            Check($"make probability parity, all 25 cases within {tolMake:0e0}", makeOk, $"worst {worstMake:0.0e0}");
            Check("exact-zero height contribution: POST_VS_POST, SMALL_ON_BIG, every Three (13 cases)", zeroOk);
            Check("Long small-but-positive on positive gaps (HeightWeightLong wired)", longOk);
        }
        catch (Exception ex) { pass = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }

        // ----------------------------------------------------------------
        // (2) Helper-level tests — the implementation boundary.
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) Helpers (reach, one-sided clamp, zone order, saturation):");
        {
            // reach: even sums and the deliberate ODD-SUM guard (fails if integer-truncated).
            Check("Reach(90,94) == 92.0", Matchup.Reach(Mk(900, 90, 94, 50)) == 92.0);
            Check("Reach(38,42) == 40.0", Matchup.Reach(Mk(901, 38, 42, 50)) == 40.0);
            Check("Reach(85,88) == 86.5 (odd sum — float divide, never 86)",
                Matchup.Reach(Mk(902, 85, 88, 50)) == 86.5);

            // one-sided clamp.
            var tall  = Mk(910, 60, 60, 50);
            var even  = Mk(911, 50, 50, 50);
            var short_ = Mk(912, 40, 40, 50);
            Check("positive gap -> positive shift",
                Matchup.HeightOverDefenderShift(ShotLocation.Rim, tall, short_, cfgM) > 0.0);
            Check("zero gap -> exactly 0",
                Matchup.HeightOverDefenderShift(ShotLocation.Rim, even, even, cfgM) == 0.0);
            Check("negative gap -> exactly 0 (one-sided)",
                Matchup.HeightOverDefenderShift(ShotLocation.Rim, short_, tall, cfgM) == 0.0);

            // zone ordering for the same positive gap: Rim > Short > Mid > Long > 0; Three == 0.
            var big   = Mk(920, 90, 90, 50);
            var small = Mk(921, 40, 40, 50);
            var sRim   = Matchup.HeightOverDefenderShift(ShotLocation.Rim,   big, small, cfgM);
            var sShort = Matchup.HeightOverDefenderShift(ShotLocation.Short, big, small, cfgM);
            var sMid   = Matchup.HeightOverDefenderShift(ShotLocation.Mid,   big, small, cfgM);
            var sLong  = Matchup.HeightOverDefenderShift(ShotLocation.Long,  big, small, cfgM);
            var sThree = Matchup.HeightOverDefenderShift(ShotLocation.Three, big, small, cfgM);
            Check("zone order Rim > Short > Mid > Long > 0",
                sRim > sShort && sShort > sMid && sMid > sLong && sLong > 0.0,
                $"{sRim:F3} > {sShort:F3} > {sMid:F3} > {sLong:F3}");
            Check("Three == 0 exactly", sThree == 0.0);

            // saturation: monotone, strictly below the zone cap, approaches it.
            var cap = cfgM.HeightMaxBonus * cfgM.HeightZoneWeight(ShotLocation.Rim);
            var g1 = Matchup.HeightOverDefenderShift(ShotLocation.Rim, Mk(930, 60, 60, 50), Mk(931, 40, 40, 50), cfgM);
            var g2 = Matchup.HeightOverDefenderShift(ShotLocation.Rim, Mk(932, 90, 90, 50), Mk(933, 40, 40, 50), cfgM);
            var g3 = Matchup.HeightOverDefenderShift(ShotLocation.Rim, Mk(934, 99, 99, 50), Mk(935,  0,  0, 50), cfgM);
            Check("saturation: monotone and strictly below cap",
                0.0 < g1 && g1 < g2 && g2 < g3 && g3 < cap, $"{g1:F3} < {g2:F3} < {g3:F3} < {cap:F1}");
        }

        // ----------------------------------------------------------------
        // (3) Config guards — Load throws on out-of-range; 0 max bonus stays legal.
        //     Range-only by design: NO monotonic-zone-weight enforcement (era experiments
        //     may reshape the profile; the loader must not freeze a design choice).
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Config guards (range-only Load validation):");
        {
            // Mutate a copy of the live config's Matchup section, write to temp, expect Load to throw.
            static string MutatedConfig(string configPath, string key, double value)
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["Matchup"]![key] = value;
                var tmp = Path.Combine(Path.GetTempPath(), $"hod_cfg_{key}_{Guid.NewGuid():N}.json");
                File.WriteAllText(tmp, node.ToJsonString());
                return tmp;
            }

            static bool Throws(string path)
            {
                try { MatchupConfig.Load(path); return false; }
                catch (InvalidOperationException) { return true; }
                finally { try { File.Delete(path); } catch { /* temp cleanup best-effort */ } }
            }

            Check("negative HeightMaxBonus throws",       Throws(MutatedConfig(configPath, "HeightMaxBonus", -1.0)));
            Check("zero HeightReferenceScale throws",     Throws(MutatedConfig(configPath, "HeightReferenceScale", 0.0)));
            Check("negative HeightReferenceScale throws", Throws(MutatedConfig(configPath, "HeightReferenceScale", -18.0)));
            Check("zone weight < 0 throws",               Throws(MutatedConfig(configPath, "HeightWeightMid", -0.1)));
            Check("zone weight > 1 throws",               Throws(MutatedConfig(configPath, "HeightWeightShort", 1.5)));

            // HeightMaxBonus = 0 must remain LEGAL — the clean kill switch.
            var killPath = MutatedConfig(configPath, "HeightMaxBonus", 0.0);
            var killOk = false; MatchupConfig? cfgKill = null;
            try { cfgKill = MatchupConfig.Load(killPath); killOk = true; }
            catch (InvalidOperationException) { killOk = false; }
            finally { try { File.Delete(killPath); } catch { /* temp cleanup best-effort */ } }
            Check("HeightMaxBonus = 0 loads (kill switch stays legal)", killOk);
            if (killOk)
            {
                var tall2  = Mk(940, 90, 90, 50);
                var small2 = Mk(941, 40, 40, 50);
                Check("kill switch: shift is exactly 0 at every zone",
                    Matchup.HeightOverDefenderShift(ShotLocation.Rim,   tall2, small2, cfgKill!) == 0.0 &&
                    Matchup.HeightOverDefenderShift(ShotLocation.Short, tall2, small2, cfgKill!) == 0.0);
            }
        }

        Console.WriteLine(pass
            ? "  Phase 61 height-over-defender: ALL OK"
            : "  Phase 61 height-over-defender: FAILURES ABOVE");
        return pass;
    }
}
