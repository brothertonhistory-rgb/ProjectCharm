using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 61 (Session 55; SIGNED since S83) — the height-over-defender make term.
//
//  The term: heightShift = HeightZoneWeight(zone) * HeightMaxBonus
//                        * tanh((shooterReach - defenderReach) / HeightReferenceScale),
//  reach = (Height + Wingspan) / 2.0, added to Matchup.EffectiveRating (make door only).
//
//  S83 removed v1's max(0, ...) clamp, so the gap is SIGNED: the undersized shooter is
//  docked by the same curve the oversized shooter is paid. The symmetry is the oddness of
//  tanh, not a branch — which is why section (2) asserts the mirror to a tolerance rather
//  than bit-exactly (Math.Tanh is not promised bit-portable; the S81.3 fixture lesson).
//  Section (4) is the S83 addition: it proves the four compensating config weights left
//  every non-rim zone's POSITIVE side exactly where v1 had it, so the non-rim season
//  movement is the new penalty arm and nothing else.
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
    /// <summary>Writes a temp copy of the live config with one Matchup key overridden.
    /// Used by section (3) to prove the loader rejects out-of-range values, and by section
    /// (4)'s negative control to prove the preservation invariant is not decorative.</summary>
    private static string HodMutatedConfig(string configPath, string key, double value)
    {
        var node = JsonNode.Parse(File.ReadAllText(configPath))!;
        node["Matchup"]![key] = value;
        var tmp = Path.Combine(Path.GetTempPath(), $"hod_cfg_{key}_{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, node.ToJsonString());
        return tmp;
    }

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

            var ratingOk = true; var makeOk = true; var zeroOk = true; var longOk = true; var negOk = true;
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

                // Exact-zero = the HEIGHT CONTRIBUTION, not the total rating. S83: equal
                // reach and Three are the ONLY exact zeros; SMALL_ON_BIG used to be a third
                // and is now the penalty arm, asserted strictly negative just below.
                var mustBeZero = arch == "POST_VS_POST" || zone == ShotLocation.Three;
                if (mustBeZero && (hShift != 0.0 || make - makeNoTerm != 0.0))
                { zeroOk = false; Console.WriteLine($"      exact-zero miss {arch}/{zone}: hShift={hShift:R}, makeDelta={make - makeNoTerm:R}"); }

                // S83 penalty arm: the undersized shooter is docked at every zone that
                // carries weight. Three stays exactly zero (handled above).
                if (arch == "SMALL_ON_BIG" && zone != ShotLocation.Three
                    && !(hShift < 0.0 && make - makeNoTerm < 0.0))
                { negOk = false; Console.WriteLine($"      penalty-arm miss {arch}/{zone}: hShift={hShift:R}, makeDelta={make - makeNoTerm:R}"); }

                // Long must move in the DIRECTION OF THE GAP whenever the gap is nonzero —
                // the only proof HeightWeightLong and its accessor branch are wired. S83:
                // "positive" became "signed", so the check follows the gap's sign.
                var gapSign = Math.Sign(Matchup.Reach(shooter) - Matchup.Reach(defender));
                if (zone == ShotLocation.Long && gapSign != 0
                    && !(Math.Sign(hShift) == gapSign && Math.Sign(make - makeNoTerm) == gapSign))
                { longOk = false; Console.WriteLine($"      Long miss {arch}: hShift={hShift:R} (expected sign {gapSign})"); }
            }
            Check($"effective rating parity, all 25 cases within {tolRating:0e0}", ratingOk, $"worst {worstRating:0.0e0}");
            Check($"make probability parity, all 25 cases within {tolMake:0e0}", makeOk, $"worst {worstMake:0.0e0}");
            Check("exact-zero height contribution: POST_VS_POST + every Three (9 cases)", zeroOk);
            Check("S83 penalty arm: SMALL_ON_BIG docked at Rim/Short/Mid/Long (4 cases)", negOk);
            Check("Long moves with the sign of the reach gap (HeightWeightLong wired, both arms)", longOk);
        }
        catch (Exception ex) { pass = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }

        // ----------------------------------------------------------------
        // (2) Helper-level tests — the implementation boundary.
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) Helpers (reach, S83 signed arms + symmetry, zone order, saturation):");
        {
            // reach: even sums and the deliberate ODD-SUM guard (fails if integer-truncated).
            Check("Reach(90,94) == 92.0", Matchup.Reach(Mk(900, 90, 94, 50)) == 92.0);
            Check("Reach(38,42) == 40.0", Matchup.Reach(Mk(901, 38, 42, 50)) == 40.0);
            Check("Reach(85,88) == 86.5 (odd sum — float divide, never 86)",
                Matchup.Reach(Mk(902, 85, 88, 50)) == 86.5);

            // ── S83 primitive probes: a wiring or sign error must die HERE, not be inferred
            //    from a population mix. Two bodies at reach 50 ± d, so the gap is exactly ±2d.
            var tall  = Mk(910, 60, 60, 50);
            var even  = Mk(911, 50, 50, 50);
            var short_ = Mk(912, 40, 40, 50);
            Check("positive gap -> positive shift",
                Matchup.HeightOverDefenderShift(ShotLocation.Rim, tall, short_, cfgM) > 0.0);
            Check("zero gap -> exactly 0",
                Matchup.HeightOverDefenderShift(ShotLocation.Rim, even, even, cfgM) == 0.0);
            Check("negative gap -> NEGATIVE shift (S83: the term is signed)",
                Matchup.HeightOverDefenderShift(ShotLocation.Rim, short_, tall, cfgM) < 0.0);

            // Symmetry, monotone-in-|gap| on both arms, and the open asymptote on both arms.
            // Tolerance-bounded, not bit-exact: Math.Tanh is not promised bit-portable across
            // platforms, and a bar that cannot hold on Emmett's machine is worse than no bar.
            var gaps = new[] { 2.0, 6.0, 12.0, 20.0, 35.0 };
            var allZones = new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
                                   ShotLocation.Long, ShotLocation.Three };
            // Reach 50 ± g/2 gives a signed gap of exactly ±g (g even -> integer ratings).
            Player At(double reach, int id) => Mk(id, (int)reach, (int)reach, 50);
            double Shift(ShotLocation z, double gap)
                => Matchup.HeightOverDefenderShift(z, At(50 + gap / 2, 950), At(50 - gap / 2, 951), cfgM);

            var symWorst = 0.0; var symOk = true;
            foreach (var z in allZones)
                foreach (var g in gaps)
                {
                    var d = Math.Abs(Shift(z, g) + Shift(z, -g));
                    symWorst = Math.Max(symWorst, d);
                    symOk = symOk && d <= 1e-12;
                }
            Check("SYMMETRY: shift(+gap) + shift(-gap) == 0 within 1e-12, every zone",
                symOk, $"worst {symWorst:0.0e0}");

            var monoOk = true;
            foreach (var z in new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid, ShotLocation.Long })
                for (var i = 1; i < gaps.Length; i++)
                    monoOk = monoOk
                        && Shift(z, gaps[i]) > Shift(z, gaps[i - 1])
                        && Shift(z, -gaps[i]) < Shift(z, -gaps[i - 1]);
            Check("MONOTONE: larger |gap| -> larger |shift|, both arms, every weighted zone", monoOk);

            // Extreme gap uses the widest LEGAL bodies (ratings live in [0, 99]), so reach 99
            // against reach 0 — a gap of 99, tanh(5.5) = 0.99997 of the zone magnitude.
            var maxBody = Mk(960, 99, 99, 50);
            var minBody = Mk(961,  0,  0, 50);
            var asymOk = true;
            foreach (var z in allZones)
            {
                var zcap = cfgM.HeightMaxBonus * cfgM.HeightZoneWeight(z);
                asymOk = asymOk
                    && Math.Abs(Matchup.HeightOverDefenderShift(z, maxBody, minBody, cfgM)) < zcap + 1e-12
                    && Math.Abs(Matchup.HeightOverDefenderShift(z, minBody, maxBody, cfgM)) < zcap + 1e-12;
            }
            Check("OPEN ASYMPTOTE: |shift| stays below zoneWeight x HeightMaxBonus, both arms", asymOk);

            var threeOk = Matchup.HeightOverDefenderShift(ShotLocation.Three, maxBody, minBody, cfgM) == 0.0
                       && Matchup.HeightOverDefenderShift(ShotLocation.Three, minBody, maxBody, cfgM) == 0.0;
            foreach (var g in new[] { 2.0, 20.0, 60.0 })
                threeOk = threeOk && Shift(ShotLocation.Three, g) == 0.0
                                  && Shift(ShotLocation.Three, -g) == 0.0;
            Check("Three: exactly 0 for EITHER sign at every gap", threeOk);

            // zone ordering for the same positive gap: Rim > Short > Mid > Long > 0; Three == 0.
            var big   = Mk(920, 90, 90, 50);
            var small = Mk(921, 40, 40, 50);
            var sRim   = Matchup.HeightOverDefenderShift(ShotLocation.Rim,   big, small, cfgM);
            var sShort = Matchup.HeightOverDefenderShift(ShotLocation.Short, big, small, cfgM);
            var sMid   = Matchup.HeightOverDefenderShift(ShotLocation.Mid,   big, small, cfgM);
            var sLong  = Matchup.HeightOverDefenderShift(ShotLocation.Long,  big, small, cfgM);
            Check("zone order Rim > Short > Mid > Long > 0",
                sRim > sShort && sShort > sMid && sMid > sLong && sLong > 0.0,
                $"{sRim:F3} > {sShort:F3} > {sMid:F3} > {sLong:F3}");
            // The rim arm must actually APPROACH its magnitude, not merely stay under it —
            // the one property the "below cap" bound alone cannot see.
            var cap = cfgM.HeightMaxBonus * cfgM.HeightZoneWeight(ShotLocation.Rim);
            var gMax = Matchup.HeightOverDefenderShift(ShotLocation.Rim, maxBody, minBody, cfgM);
            Check("saturation: the widest legal gap reaches >99% of the rim magnitude",
                gMax > cap * 0.99 && gMax < cap, $"{gMax:F3} of {cap:F1}");
        }

        // ----------------------------------------------------------------
        // (3) Config guards — Load throws on out-of-range; 0 max bonus stays legal.
        //     Range-only by design: NO monotonic-zone-weight enforcement (era experiments
        //     may reshape the profile; the loader must not freeze a design choice).
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Config guards (range-only Load validation):");
        {
            static bool Throws(string path)
            {
                try { MatchupConfig.Load(path); return false; }
                catch (InvalidOperationException) { return true; }
                finally { try { File.Delete(path); } catch { /* temp cleanup best-effort */ } }
            }

            Check("negative HeightMaxBonus throws",       Throws(HodMutatedConfig(configPath, "HeightMaxBonus", -1.0)));
            Check("zero HeightReferenceScale throws",     Throws(HodMutatedConfig(configPath, "HeightReferenceScale", 0.0)));
            Check("negative HeightReferenceScale throws", Throws(HodMutatedConfig(configPath, "HeightReferenceScale", -18.0)));
            Check("zone weight < 0 throws",               Throws(HodMutatedConfig(configPath, "HeightWeightMid", -0.1)));
            Check("zone weight > 1 throws",               Throws(HodMutatedConfig(configPath, "HeightWeightShort", 1.5)));

            // HeightMaxBonus = 0 must remain LEGAL — the clean kill switch.
            var killPath = HodMutatedConfig(configPath, "HeightMaxBonus", 0.0);
            var killOk = false; MatchupConfig? cfgKill = null;
            try { cfgKill = MatchupConfig.Load(killPath); killOk = true; }
            catch (InvalidOperationException) { killOk = false; }
            finally { try { File.Delete(killPath); } catch { /* temp cleanup best-effort */ } }
            Check("HeightMaxBonus = 0 loads (kill switch stays legal)", killOk);
            if (killOk)
            {
                var tall2  = Mk(940, 90, 90, 50);
                var small2 = Mk(941, 40, 40, 50);
                var killOkAll = true;
                foreach (var z in new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
                                          ShotLocation.Long, ShotLocation.Three })
                    killOkAll = killOkAll
                        && Matchup.HeightOverDefenderShift(z, tall2, small2, cfgKill!) == 0.0
                        && Matchup.HeightOverDefenderShift(z, small2, tall2, cfgKill!) == 0.0;
                Check("kill switch: shift is exactly 0 at every zone, BOTH signs", killOkAll);
            }
        }

        // ----------------------------------------------------------------
        // (4) S83 — POSITIVE-SIDE PRESERVATION at Short, Mid and Long.
        //     S83 raised the rim magnitude from 15 to 110 and divided the three non-rim
        //     weights by the same 110/15, so each non-rim zone's ABSOLUTE magnitude is
        //     untouched. That is the whole reason the non-rim season decline can be read as
        //     the new penalty arm rather than a preservation failure — so it is asserted,
        //     not assumed. The v1 constants below are literals ON PURPOSE: they are the
        //     historical values this check compares against, not a second copy of a live dial.
        // ----------------------------------------------------------------
        Console.WriteLine("  (4) S83 positive-side preservation (v1 magnitudes held at Short/Mid/Long):");
        {
            const double V1MaxBonus = 15.0;
            var v1Weight = new Dictionary<ShotLocation, double>
            {
                [ShotLocation.Short] = 0.80,
                [ShotLocation.Mid]   = 0.30,
                [ShotLocation.Long]  = 0.05,
            };

            static Player Body(int reach, int id) => new Player($"hodp{id}")
            {
                PlayerId = id,
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
                PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
                OffBallDefense = 50, Height = reach, Wingspan = reach, Weight = 50,
                Strength = 50, Speed = 50, Quickness = 50, FirstStep = 50, Vertical = 50,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
            };

            // A ladder of POSITIVE gaps. Reach 50 + g/2 vs 50 - g/2, so the gap is exactly g.
            var ladder = new[] { 2, 6, 12, 20, 34 };
            var worst = 0.0; var preserved = true;
            foreach (var (zone, w1) in v1Weight)
                foreach (var g in ladder)
                {
                    var shooter  = Body(50 + g / 2, 970 + g);
                    var defender = Body(50 - g / 2, 980 + g);
                    var now = Matchup.HeightOverDefenderShift(zone, shooter, defender, cfgM);
                    var v1  = w1 * V1MaxBonus * Math.Tanh(g / cfgM.HeightReferenceScale);
                    var d   = Math.Abs(now - v1);
                    worst = Math.Max(worst, d);
                    preserved = preserved && d <= 1e-12;
                }
            Check("positive-side shift matches v1 within 1e-12 at Short/Mid/Long, 5-gap ladder",
                preserved, $"worst {worst:0.0e0}");

            // The config invariant behind it: weight x magnitude is unmoved per zone.
            var prodWorst = 0.0; var prodOk = true;
            foreach (var (zone, w1) in v1Weight)
            {
                var d = Math.Abs(cfgM.HeightZoneWeight(zone) * cfgM.HeightMaxBonus - w1 * V1MaxBonus);
                prodWorst = Math.Max(prodWorst, d);
                prodOk = prodOk && d <= 1e-12;
            }
            Check("config invariant: zoneWeight x HeightMaxBonus unmoved at Short/Mid/Long",
                prodOk, $"worst {prodWorst:0.0e0}");

            // NEGATIVE CONTROL — a bound that cannot fail is decorative (S81.3). Perturb one
            // weight by a hair and confirm the invariant above actually rejects it.
            var perturbedPath = HodMutatedConfig(configPath, "HeightWeightMid",
                                               cfgM.HeightWeightMid * 1.001);
            var rejected = false;
            try
            {
                var bad = MatchupConfig.Load(perturbedPath);
                var d = Math.Abs(bad.HeightZoneWeight(ShotLocation.Mid) * bad.HeightMaxBonus - 0.30 * V1MaxBonus);
                rejected = d > 1e-12;
            }
            catch (InvalidOperationException) { rejected = true; }
            finally { try { File.Delete(perturbedPath); } catch { /* best-effort */ } }
            Check("negative control: a 0.1% perturbation of HeightWeightMid IS rejected", rejected);
        }

        Console.WriteLine(pass
            ? "  Phase 61 height-over-defender: ALL OK"
            : "  Phase 61 height-over-defender: FAILURES ABOVE");
        return pass;
    }
}
