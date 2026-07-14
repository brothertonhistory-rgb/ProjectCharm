using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 63 (Session 57) — PostMoves interior self-creation.
//
//  Two diet wires (Roll G) + one assist wire (Resolver), all reading the shooter's
//  PostMoves, all anchored at PostMoves 50 and upside-only (PM <= 50 is exact identity):
//    1. DIET tilt — high PostMoves multiplies the coached Rim + Short shares (ratio
//       preserved) so he hunts more interior shots. Never touches OffenseRating -> no make%.
//    2. PRESSURE resistance — a strong post player resists being displaced OFF an interior
//       spot: the requested diet shift is shrunk (never the intrinsicCapacity cap).
//    3. ASSIST discount — a passing-scaled discount on the Rim + Short assisted rate:
//       interior buckets are credited as assisted less often, most on a pass-dead lineup,
//       least beside elite passers.
//
//  Tests (mirrors the Phase 61 two-layer shape: golden parity + helper-level + guards):
//    (1) per-zone make% LEAK GUARD (bit-identical EffectiveRating as PM walks 0->99)
//    (2) assist golden parity vs tools/post_assist_golden.json (|Δ| <= 1e-12)
//    (3) anchors + three kill switches, EXACT-BIT
//    (4) diet locked-shape invariants (on the pure tilt helper, pre-bend) + end-to-end wire
//    (5) displacement resistance monotonicity on the intermediate shift/absorbed
//    (6) assist interaction + zone gate
//    (7) bounds + config guards (Load throws; every span = 0 loads)
// ============================================================================
internal static partial class Program
{
    private static bool Phase63PostMovesInteriorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 63: PostMoves interior self-creation (diet tilt + resistance + assist discount) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgG = RollGConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);

        // A uniform all-50 player overriding only the body/athletic axes and PostMoves,
        // so skill = 50 in every zone and the defensive blend reads exactly 50.
        static Player Mk(int id, int postMoves, int height = 50, int wingspan = 50, int ath = 50)
            => new Player($"pmi{id}")
            {
                PlayerId = id,
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = postMoves,
                OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
                PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
                OffBallDefense = 50, Height = height, Wingspan = wingspan, Weight = 50,
                Strength = ath, Speed = ath, Quickness = ath, FirstStep = ath, Vertical = ath,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
            };

        // ----------------------------------------------------------------
        // (1) Per-zone make% LEAK GUARD (load-bearing). EffectiveRating(Rim,…) and
        //     EffectiveRating(Short,…) must be BIT-IDENTICAL for a fixed shooter/defender as
        //     PostMoves walks 0->99 — proof the diet tilt never leaked through OffenseRating.
        //     (Blended interior FG% WOULD move legitimately with the Rim:Short mix; that is
        //     why this checks per-zone, not the blend.)
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Per-zone make% leak guard (EffectiveRating bit-identical across PostMoves):");
        {
            var defender = Mk(1000, 50, 55, 55, 55);   // a distinct defender so the shift is nonzero-ish
            var rim0   = Matchup.EffectiveRating(ShotLocation.Rim,   Mk(1, 0),  defender, cfgM);
            var short0 = Matchup.EffectiveRating(ShotLocation.Short, Mk(2, 0),  defender, cfgM);
            var rimOk = true; var shortOk = true;
            for (var pm = 0; pm <= 99; pm++)
            {
                var shooter = Mk(2000 + pm, pm, 50, 50, 50);
                if (Matchup.EffectiveRating(ShotLocation.Rim,   shooter, defender, cfgM) != rim0)   rimOk   = false;
                if (Matchup.EffectiveRating(ShotLocation.Short, shooter, defender, cfgM) != short0) shortOk = false;
            }
            Check("Rim EffectiveRating identical for PostMoves 0..99 (no OffenseRating leak)", rimOk);
            Check("Short EffectiveRating identical for PostMoves 0..99 (no OffenseRating leak)", shortOk);
        }

        // ----------------------------------------------------------------
        // (2) Assist golden parity vs tools/post_assist_golden.json (|Δ| <= 1e-12).
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) Assist golden parity (30 rows, |Δ| <= 1e-12):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "post_assist_golden.json");
            if (!File.Exists(path)) throw new InvalidOperationException($"golden fixture not found: {path}");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // Fixture contract — validated BEFORE trusting a number: the oracle's params must
            // match the loaded config, else the fixture is stale.
            var p = root.GetProperty("params");
            bool ParamOk(string key, double live) => p.GetProperty(key).GetDouble() == live;
            if (!(ParamOk("PostAssistSpan", cfgM.PostAssistSpan) &&
                  ParamOk("DampFloor", cfgM.PostAssistDampFloor) &&
                  ParamOk("PfLo", cfgM.PostAssistPfLo) &&
                  ParamOk("PfHi", cfgM.PostAssistPfHi) &&
                  ParamOk("BaseRim", cfgM.AssistedRateRim) &&
                  ParamOk("BaseShort", cfgM.AssistedRateShort) &&
                  ParamOk("Floor", cfgM.AssistRateFloor) &&
                  ParamOk("Ceiling", cfgM.AssistRateCeiling)))
                throw new InvalidOperationException(
                    "golden fixture rejected: params do not match the loaded MatchupConfig. " +
                    "Regenerate tools/post_assist_golden.json or fix config.json.");

            var rows = root.GetProperty("rows");
            var boundsOk = true; var parityOk = true; var worst = 0.0;
            foreach (var r in rows.EnumerateArray())
            {
                var zone = Enum.Parse<ShotLocation>(r.GetProperty("zone").GetString()!);
                var pm   = r.GetProperty("pm").GetInt32();
                var pf   = r.GetProperty("pf").GetDouble();
                var expected = r.GetProperty("assistProb").GetDouble();

                var baseRate   = cfgM.AssistedRate(zone);
                var postFactor = cfgM.PostAssistFactor(zone, pm, pf);
                // Same "no ×1 reassociation on identity" branch as the Resolver site.
                var assistProb = postFactor == 1.0
                    ? Math.Clamp(baseRate * pf, cfgM.AssistRateFloor, cfgM.AssistRateCeiling)
                    : Math.Clamp(baseRate * pf * postFactor, cfgM.AssistRateFloor, cfgM.AssistRateCeiling);

                var d = Math.Abs(assistProb - expected);
                worst = Math.Max(worst, d);
                if (d > 1e-12) { parityOk = false; Console.WriteLine($"      miss {zone}/PM{pm}/pf{pf}: {assistProb:R} vs {expected:R}"); }
                if (assistProb < cfgM.AssistRateFloor - 1e-12 || assistProb > cfgM.AssistRateCeiling + 1e-12) boundsOk = false;
            }
            Check("all 30 rows within 1e-12 of oracle", parityOk, $"worst {worst:0.0e0}");
            Check("every discounted assistProb within [Floor, Ceiling]", boundsOk);
        }
        catch (Exception ex) { pass = false; Console.WriteLine($"  FAIL  (2) threw: {ex.Message}"); }

        // ----------------------------------------------------------------
        // (3) Anchors + three kill switches — EXACT-BIT (not tolerance).
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Anchors + kill switches (exact-bit identity):");
        {
            // A representative non-flat coached vector so a ×1 renormalize would perturb bits.
            var v = (0.30, 0.15, 0.10, 0.05, 0.40);
            bool SameTuple((double, double, double, double, double) a,
                           (double, double, double, double, double) b)
                => a.Item1 == b.Item1 && a.Item2 == b.Item2 && a.Item3 == b.Item3 &&
                   a.Item4 == b.Item4 && a.Item5 == b.Item5;

            // DIET tilt anchor: PM = 50 -> tuple bit-identical to input (no multiply/renormalize).
            Check("diet tilt: PostMoves 50 returns the coached vector bit-for-bit",
                SameTuple(RollGGenerator.TiltInteriorDiet(v.Item1, v.Item2, v.Item3, v.Item4, v.Item5, 50, cfgG.PostDietSpan), v));
            // DIET tilt kill switch: span = 0 -> bit-identical even at PM 99.
            Check("diet tilt: PostDietSpan 0 returns bit-for-bit at PostMoves 99",
                SameTuple(RollGGenerator.TiltInteriorDiet(v.Item1, v.Item2, v.Item3, v.Item4, v.Item5, 99, 0.0), v));

            // RESISTANCE anchor + kill switch: unchanged at PM 50, span 0, and perimeter-dom.
            const double req = 0.16;
            Check("resistance: PostMoves 50 leaves requestedShift unchanged",
                RollGGenerator.ResistPressureShift(req, 0, 50, cfgG.PostPressureResistanceSpan) == req);
            Check("resistance: span 0 leaves requestedShift unchanged at PostMoves 99",
                RollGGenerator.ResistPressureShift(req, 0, 99, 0.0) == req);
            Check("resistance: perimeter-dominant (idx 4) leaves requestedShift unchanged",
                RollGGenerator.ResistPressureShift(req, 4, 99, cfgG.PostPressureResistanceSpan) == req);

            // ASSIST kill switches: factor is EXACTLY 1.0 on every identity case.
            Check("assist: PostMoves 50 -> factor exactly 1.0",
                cfgM.PostAssistFactor(ShotLocation.Rim, 50, 1.0) == 1.0);
            var cfgAssistOff = LoadWithMatchupOverride(configPath, ("PostAssistSpan", 0.0));
            Check("assist: PostAssistSpan 0 -> factor exactly 1.0 at PostMoves 99",
                cfgAssistOff.PostAssistFactor(ShotLocation.Rim, 99, 0.80) == 1.0);
        }

        // ----------------------------------------------------------------
        // (4) Diet locked-shape invariants — on the pure tilt helper (pre-bend), plus an
        //     end-to-end wire check that the tilt actually reaches the final pie.
        // ----------------------------------------------------------------
        Console.WriteLine("  (4) Diet locked-shape invariants (pre-bend helper) + end-to-end wire:");
        {
            // Base coached vector with distinct Rim/Short so ratio preservation is testable.
            (double r, double s, double m, double l, double t) B = (0.24, 0.16, 0.12, 0.08, 0.40);
            (double r, double s, double m, double l, double t) T(int pm)
            {
                var (r, s, m, l, t) = RollGGenerator.TiltInteriorDiet(B.r, B.s, B.m, B.l, B.t, pm, cfgG.PostDietSpan);
                return (r, s, m, l, t);
            }

            // Interior share rises monotonically for PM > 50.
            double Interior((double r, double s, double m, double l, double t) x) => x.r + x.s;
            var i50 = Interior(T(50)); var i60 = Interior(T(60)); var i85 = Interior(T(85)); var i99 = Interior(T(99));
            Check("interior share rises monotonically (PM 50<60<85<99)",
                i50 < i60 && i60 < i85 && i85 < i99, $"{i50:F3} < {i60:F3} < {i85:F3} < {i99:F3}");

            // Rim:Short ratio preserved by the tilt.
            var t99 = T(99);
            var ratioOk = Math.Abs(t99.r / t99.s - B.r / B.s) < 1e-12;
            Check("Rim:Short ratio preserved by the tilt", ratioOk, $"{t99.r / t99.s:F6} vs {B.r / B.s:F6}");

            // Zero authored Rim / Short stays zero (multiplicative).
            var zeroRim   = RollGGenerator.TiltInteriorDiet(0.0, 0.20, 0.20, 0.20, 0.40, 99, cfgG.PostDietSpan);
            var zeroShort = RollGGenerator.TiltInteriorDiet(0.30, 0.0, 0.20, 0.10, 0.40, 99, cfgG.PostDietSpan);
            Check("zero authored Rim stays zero", zeroRim.rim == 0.0);
            Check("zero authored Short stays zero", zeroShort.shortT == 0.0);

            // Mid/Long/Three shed share only PROPORTIONALLY (their mutual ratios preserved).
            var mlOk = Math.Abs(t99.m / t99.l - B.m / B.l) < 1e-12 &&
                       Math.Abs(t99.l / t99.t - B.l / B.t) < 1e-12;
            Check("Mid/Long/Three shed share only proportionally (mutual ratios preserved)", mlOk);

            // End-to-end: under real usage pressure, a higher-PostMoves shooter's final pie
            // carries MORE interior share — proof the tilt is wired into Generate, not just
            // the helper. Neutral defenders, fixed pressure, walk PostMoves.
            var (game50, slot50, gen) = BuildRollGGame(configPath, Mk(3050, 50), cfgG, cfgM);
            double InteriorShareUnderPressure(int pm)
            {
                var (g, slot, gG) = BuildRollGGame(configPath, Mk(4000 + pm, pm), cfgG, cfgM);
                var st = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                    EntryType.DeadBallInbound, SelectedSlot: slot, UsagePressure: 0.32, UsageResidualPressure: 0.0);
                var pie = gG.Generate(st);
                return pie.Slices.First(s => s.Outcome == ShotLocation.Rim).Weight
                     + pie.Slices.First(s => s.Outcome == ShotLocation.Short).Weight;
            }
            var e50 = InteriorShareUnderPressure(50);
            var e85 = InteriorShareUnderPressure(85);
            var e99 = InteriorShareUnderPressure(99);
            Check("end-to-end: final-pie interior share rises with PostMoves under pressure",
                e50 < e85 && e85 < e99, $"{e50:F3} < {e85:F3} < {e99:F3}");
        }

        // ----------------------------------------------------------------
        // (5) Displacement resistance monotonicity on the intermediate shift/absorbed.
        //     In a non-saturated interior-dominant reference case the requested shift is the
        //     binding cap, so absorbed == the reduced shift and tracks it exactly.
        // ----------------------------------------------------------------
        Console.WriteLine("  (5) Resistance monotonicity (absorbed shrinks with PostMoves, interior only):");
        {
            const double req = 0.16;
            var s50 = RollGGenerator.ResistPressureShift(req, 0, 50, cfgG.PostPressureResistanceSpan);
            var s85 = RollGGenerator.ResistPressureShift(req, 0, 85, cfgG.PostPressureResistanceSpan);
            var s99 = RollGGenerator.ResistPressureShift(req, 0, 99, cfgG.PostPressureResistanceSpan);
            Check("interior-dominant: requestedShift(99) < (85) < (50), strict for PM>50",
                s99 < s85 && s85 < s50 && s50 == req, $"{s99:F4} < {s85:F4} < {s50:F4}");

            // No effect for a perimeter-dominant shooter (idx 2/3/4) at any PostMoves.
            var perimOk = RollGGenerator.ResistPressureShift(req, 2, 99, cfgG.PostPressureResistanceSpan) == req &&
                          RollGGenerator.ResistPressureShift(req, 3, 99, cfgG.PostPressureResistanceSpan) == req &&
                          RollGGenerator.ResistPressureShift(req, 4, 99, cfgG.PostPressureResistanceSpan) == req;
            Check("no effect for a perimeter-dominant zone at any PostMoves", perimOk);

            // Zero pressure -> requestedShift is 0 upstream, so absorbed is 0 regardless.
            Check("zero requested shift stays zero (no effect under zero pressure)",
                RollGGenerator.ResistPressureShift(0.0, 0, 99, cfgG.PostPressureResistanceSpan) == 0.0);
        }

        // ----------------------------------------------------------------
        // (6) Assist interaction + zone gate.
        // ----------------------------------------------------------------
        Console.WriteLine("  (6) Assist interaction + zone gate:");
        {
            // Falls MOST at weak passing, LEAST at elite (factor smaller = bigger discount).
            var weak  = cfgM.PostAssistFactor(ShotLocation.Rim, 85, 0.80);
            var avg   = cfgM.PostAssistFactor(ShotLocation.Rim, 85, 1.00);
            var elite = cfgM.PostAssistFactor(ShotLocation.Rim, 85, 1.20);
            Check("interior discount largest at weak passing, smallest at elite",
                weak < avg && avg < elite && elite < 1.0, $"factors {weak:F3} < {avg:F3} < {elite:F3}");

            // PM <= 50 unchanged at every passing level.
            var pm50Ok = cfgM.PostAssistFactor(ShotLocation.Rim, 50, 0.80) == 1.0 &&
                         cfgM.PostAssistFactor(ShotLocation.Rim, 50, 1.00) == 1.0 &&
                         cfgM.PostAssistFactor(ShotLocation.Rim, 50, 1.20) == 1.0 &&
                         cfgM.PostAssistFactor(ShotLocation.Rim, 30, 1.00) == 1.0;
            Check("PostMoves <= 50 unchanged at every passing level", pm50Ok);

            // Mid/Long/Three assist rates untouched at all PostMoves.
            var perimZonesOk = true;
            foreach (var z in new[] { ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three })
                for (var pm = 0; pm <= 99; pm++)
                    if (cfgM.PostAssistFactor(z, pm, 0.80) != 1.0) perimZonesOk = false;
            Check("Mid/Long/Three assist factor == 1.0 at all PostMoves (zone-gated)", perimZonesOk);
        }

        // ----------------------------------------------------------------
        // (7) Bounds + config guards. Load throws on out-of-range; every span = 0 loads.
        // ----------------------------------------------------------------
        Console.WriteLine("  (7) Config guards (Load throws; kill switches legal):");
        {
            Check("RollG PostPressureResistanceSpan > 1 throws",
                RollGLoadThrows(configPath, ("PostPressureResistanceSpan", 1.5)));
            Check("RollG PostPressureResistanceSpan < 0 throws",
                RollGLoadThrows(configPath, ("PostPressureResistanceSpan", -0.1)));
            Check("RollG PostDietSpan < 0 throws",
                RollGLoadThrows(configPath, ("PostDietSpan", -0.1)));
            Check("Matchup PostAssistSpan > 1 throws",
                MatchupLoadThrows(configPath, ("PostAssistSpan", 1.5)));
            Check("Matchup PostAssistSpan < 0 throws",
                MatchupLoadThrows(configPath, ("PostAssistSpan", -0.1)));
            Check("Matchup PostAssistDampFloor = 0 throws (must be in (0,1])",
                MatchupLoadThrows(configPath, ("PostAssistDampFloor", 0.0)));
            Check("Matchup PostAssistDampFloor > 1 throws",
                MatchupLoadThrows(configPath, ("PostAssistDampFloor", 1.2)));
            Check("Matchup PostAssistPfHi <= PfLo throws",
                MatchupLoadThrows(configPath, ("PostAssistPfHi", 0.5)));

            // Every span = 0 must load (kill switches legal). PostDietSpan 0 needs no upper
            // bound; the resistance/assist spans at 0 are within [0,1].
            var killG = RollGLoads(configPath, ("PostDietSpan", 0.0), ("PostPressureResistanceSpan", 0.0));
            var killM = MatchupLoads(configPath, ("PostAssistSpan", 0.0));
            Check("RollG PostDietSpan = 0 and PostPressureResistanceSpan = 0 both load", killG);
            Check("Matchup PostAssistSpan = 0 loads", killM);
        }

        Console.WriteLine(pass
            ? "  Phase 63 PostMoves interior self-creation: ALL OK"
            : "  Phase 63 PostMoves interior self-creation: FAILURES ABOVE");
        return pass;
    }

    // ---- helpers local to Phase 63 -------------------------------------------

    // Build a game with the shooter in Home slot 1 and 5 neutral all-50 defenders in Away,
    // returning the game, the shooter's slot, and a RollGGenerator over it.
    private static (GameState game, Slot slot, RollGGenerator gen) BuildRollGGame(
        string configPath, Player shooter, RollGConfig cfgG, MatchupConfig cfgM)
    {
        var fouls = new FoulTracker(7, 10);
        var game  = new GameState(fouls);
        game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(1), shooter);
        Player Def(int id) => new Player($"pmidef{id}")
        {
            PlayerId = id,
            Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
            RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
            BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
            OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
            PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
            OffBallDefense = 50, Height = 50, Wingspan = 50, Weight = 50,
            Strength = 50, Speed = 50, Quickness = 50, FirstStep = 50, Vertical = 50,
            Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
        };
        for (var i = 1; i <= 5; i++)
            game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Def(500 + i));
        var gen = new RollGGenerator(cfgG, cfgM, game);
        return (game, game.HomeLineup.SlotAt(1), gen);
    }

    private static MatchupConfig LoadWithMatchupOverride(string configPath, params (string key, double val)[] overrides)
    {
        var node = JsonNode.Parse(File.ReadAllText(configPath))!;
        foreach (var (k, v) in overrides) node["Matchup"]![k] = v;
        var tmp = Path.Combine(Path.GetTempPath(), $"pmi_m_{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, node.ToJsonString());
        try { return MatchupConfig.Load(tmp); }
        finally { try { File.Delete(tmp); } catch { /* best-effort */ } }
    }

    private static bool RollGLoadThrows(string configPath, params (string key, double val)[] overrides)
        => MutatedLoadThrows(configPath, "RollG", overrides, p => { RollGConfig.Load(p); });

    private static bool MatchupLoadThrows(string configPath, params (string key, double val)[] overrides)
        => MutatedLoadThrows(configPath, "Matchup", overrides, p => { MatchupConfig.Load(p); });

    private static bool RollGLoads(string configPath, params (string key, double val)[] overrides)
        => MutatedLoads(configPath, "RollG", overrides, p => { RollGConfig.Load(p); });

    private static bool MatchupLoads(string configPath, params (string key, double val)[] overrides)
        => MutatedLoads(configPath, "Matchup", overrides, p => { MatchupConfig.Load(p); });

    private static bool MutatedLoadThrows(string configPath, string section,
                                          (string key, double val)[] overrides, Action<string> load)
    {
        var path = WriteMutated(configPath, section, overrides);
        try { load(path); return false; }
        catch (InvalidOperationException) { return true; }
        finally { try { File.Delete(path); } catch { /* best-effort */ } }
    }

    private static bool MutatedLoads(string configPath, string section,
                                     (string key, double val)[] overrides, Action<string> load)
    {
        var path = WriteMutated(configPath, section, overrides);
        try { load(path); return true; }
        catch (InvalidOperationException) { return false; }
        finally { try { File.Delete(path); } catch { /* best-effort */ } }
    }

    private static string WriteMutated(string configPath, string section, (string key, double val)[] overrides)
    {
        var node = JsonNode.Parse(File.ReadAllText(configPath))!;
        foreach (var (k, v) in overrides) node[section]![k] = v;
        var tmp = Path.Combine(Path.GetTempPath(), $"pmi_{section}_{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, node.ToJsonString());
        return tmp;
    }
}
