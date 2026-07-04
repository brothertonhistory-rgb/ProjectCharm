using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 56 — Roll G matchup displacement (Session 36).
//
// The executable spec is tools/displacement_oracle.py (LOCKED SPEC ORACLE v1,
// 2026-07-04); the committed golden fixture tools/displacement_golden.json is
// its byte-for-byte emission. If the C# and the oracle ever disagree, the
// oracle wins — a parity failure here is a PORT BUG, never a tolerance to
// widen and never a fixture to regenerate casually (regeneration is the
// oracle-first tuning flow: new approved calibration → re-emit → sync C#
// defaults AND config.json).
//
// Sub-checks:
//   (1) Golden parity, stage-wise — fixture contract validated loudly, then
//       every vector runs through Matchup.DeriveDisplacement with
//       CONFIG-LOADED constants; every stage compared at cross-language
//       tolerance. Config drift from the locked constants fails HERE.
//   (2) No-displacement equivalence at equal level — a shooter whose
//       diet-weighted skill level is exactly 0 and whose athleticism equals
//       the lineup mean must reproduce the OLD pipeline (renorm(base ·
//       mult(rawGap))) within 1e-12: evenly-matched possessions are
//       numerically pre-build.
//   (3) Ablation — DisplacementMaxMagnitude = 0 (in-memory copy) must equal
//       the pure shape-bend path. NOTE: 0 does NOT undo Route B.
//   (4) Direction + gate spot-checks — structure only, no magnitudes.
// ============================================================================
internal static partial class Program
{
    private static bool Phase56DisplacementCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 56: Roll G matchup displacement (Route B + gated ladder, golden parity) ---");
        var pass = true;

        var cfgMatchup = MatchupConfig.Load(configPath);

        // Zone order everywhere: Rim, Short, Mid, Long, Three (the locked contract).
        string[] zones = { "Rim", "Short", "Mid", "Long", "Three" };

        // Cross-language float parity holds at tolerance, not equality:
        // |a-b| <= max(1e-9 * max(|a|,|b|), 1e-12) — Python ** / math.tanh and
        // Math.Pow / Math.Tanh may differ by ULPs (the tendency-parity precedent).
        static bool Near(double x, double y) =>
            Math.Abs(x - y) <= Math.Max(1e-9 * Math.Max(Math.Abs(x), Math.Abs(y)), 1e-12);
        static bool NearTight(double x, double y) => Math.Abs(x - y) <= 1e-12;

        // Helper: an all-baseline player with the derivation-relevant attributes set.
        static Player Shooter(int fin, int close, int mid, int outside, int ath)
            => new Player("shooter")
            {
                Outside = outside, Mid = mid, Close = close, Finishing = fin, FreeThrow = 50,
                FoulDrawing = 50,
                BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50,
                PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                DefensiveRebounding = 50, Steals = 50,
                Height = 50, Wingspan = 50, Weight = 50,
                Strength = ath, Speed = ath, Quickness = ath, FirstStep = ath, Vertical = ath,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50,
                OffBallDefense = 50,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20,
                ThreeTendency = 20,
            };

        static IReadOnlyList<DisplacementDefender> UniformDefense(double def, double ath)
        {
            var d = new DisplacementDefender(def, def, def, ath);
            return new[] { d, d, d, d, d };
        }

        // ----------------------------------------------------------------
        // (1) Golden parity, stage-wise.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity vs tools/displacement_golden.json (stage-wise):");
        bool p1 = true;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "displacement_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // ── fixture contract — validated loudly BEFORE trusting a single number ──
            if (!root.TryGetProperty("zoneOrder", out var zo) || zo.GetArrayLength() != 5)
                throw new InvalidOperationException("golden fixture rejected: missing/short zoneOrder.");
            for (var i = 0; i < 5; i++)
                if (zo[i].GetString() != zones[i])
                    throw new InvalidOperationException(
                        $"golden fixture rejected: zoneOrder[{i}] is '{zo[i].GetString()}', expected '{zones[i]}'. " +
                        "The fixture does not match the locked contract (Rim, Short, Mid, Long, Three).");

            if (!root.TryGetProperty("vectors", out var vectors) || vectors.GetArrayLength() == 0)
                throw new InvalidOperationException("golden fixture rejected: no vectors.");

            string[] shooterKeys  = { "Finishing", "Close", "Mid", "Outside",
                                      "Strength", "Speed", "Quickness", "FirstStep", "Vertical" };
            string[] defenderKeys = { "PerimeterDefense", "PostDefense", "RimProtection",
                                      "Strength", "Speed", "Quickness", "FirstStep", "Vertical" };
            string[] traceVecFields    = { "base", "gaps", "residuals", "bentShapeOnly", "ladder", "final" };
            string[] traceScalarFields = { "skillLevel", "physLevel", "level", "mag" };

            // Per-zone dict → array in the locked zone order.
            static double[] ZoneArray(JsonElement el, string[] zoneOrder, string what)
            {
                var vals = new double[5];
                for (var i = 0; i < 5; i++)
                {
                    if (!el.TryGetProperty(zoneOrder[i], out var v))
                        throw new InvalidOperationException(
                            $"golden fixture rejected: {what} lacks zone '{zoneOrder[i]}'.");
                    vals[i] = v.GetDouble();
                }
                return vals;
            }

            var run = 0;
            foreach (var vec in vectors.EnumerateArray())
            {
                var name = vec.GetProperty("name").GetString() ?? "(unnamed)";

                // Shooter: attributes must be integral — Player attributes are ints,
                // and the oracle authors shooters on the int rating scale. (Defenders
                // are NOT required to be integral: the level-matched vector solves
                // PostDefense to a fraction by construction — that is exactly why the
                // derivation consumes DisplacementDefender doubles.)
                var shooterEl = vec.GetProperty("shooter");
                var sh = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var key in shooterKeys)
                {
                    if (!shooterEl.TryGetProperty(key, out var v))
                        throw new InvalidOperationException(
                            $"golden fixture rejected: vector '{name}' shooter lacks '{key}'.");
                    var raw = v.GetDouble();
                    var asInt = (int)Math.Round(raw);
                    if (Math.Abs(raw - asInt) > 0.0)
                        throw new InvalidOperationException(
                            $"golden fixture rejected: vector '{name}' shooter.{key}={raw} is not " +
                            "integral — Player attributes are ints; the oracle authors shooters on " +
                            "the int scale.");
                    sh[key] = asInt;
                }
                // The oracle's shooter athleticism keys are all authored equal per
                // archetype; the Player composite is their mean either way.
                var shooter = Shooter(sh["Finishing"], sh["Close"], sh["Mid"], sh["Outside"], sh["Strength"]);
                // Guard the "authored equal" assumption rather than silently averaging:
                if (sh["Speed"] != sh["Strength"] || sh["Quickness"] != sh["Strength"]
                    || sh["FirstStep"] != sh["Strength"] || sh["Vertical"] != sh["Strength"])
                    throw new InvalidOperationException(
                        $"golden fixture rejected: vector '{name}' shooter athleticism keys are not " +
                        "uniform — the harness Player builder assumes one athleticism value per shooter. " +
                        "Extend the builder before accepting this fixture.");

                // Defenders: the FULL five-man lineup, mandatory — a single-defender
                // fixture cannot catch a wrong aggregation (top-three skill blend vs
                // five-man physical mean).
                var defsEl = vec.GetProperty("defenders");
                if (defsEl.GetArrayLength() != 5)
                    throw new InvalidOperationException(
                        $"golden fixture rejected: vector '{name}' carries {defsEl.GetArrayLength()} " +
                        "defenders — the contract requires the full five-man lineup.");
                var defenders = new List<DisplacementDefender>(5);
                foreach (var dEl in defsEl.EnumerateArray())
                {
                    foreach (var key in defenderKeys)
                        if (!dEl.TryGetProperty(key, out _))
                            throw new InvalidOperationException(
                                $"golden fixture rejected: vector '{name}' defender lacks '{key}'.");
                    var athMean = (dEl.GetProperty("Strength").GetDouble()
                                 + dEl.GetProperty("Speed").GetDouble()
                                 + dEl.GetProperty("Quickness").GetDouble()
                                 + dEl.GetProperty("FirstStep").GetDouble()
                                 + dEl.GetProperty("Vertical").GetDouble()) / 5.0;
                    defenders.Add(new DisplacementDefender(
                        dEl.GetProperty("PerimeterDefense").GetDouble(),
                        dEl.GetProperty("PostDefense").GetDouble(),
                        dEl.GetProperty("RimProtection").GetDouble(),
                        athMean));
                }

                var diet = ZoneArray(vec.GetProperty("diet"), zones, $"vector '{name}' diet");
                var usage = vec.GetProperty("usagePressure").GetDouble();
                if (usage < 0.0)
                    throw new InvalidOperationException(
                        $"golden fixture rejected: vector '{name}' usagePressure={usage} < 0.");

                // Trace contract: all ten stage fields present and well-shaped.
                if (!vec.TryGetProperty("trace", out var traceEl))
                    throw new InvalidOperationException(
                        $"golden fixture rejected: vector '{name}' has no trace — stage-wise parity " +
                        "requires the full-trace fixture (re-run tools/displacement_oracle.py).");
                var expVec = new Dictionary<string, double[]>(StringComparer.Ordinal);
                foreach (var f in traceVecFields)
                {
                    if (!traceEl.TryGetProperty(f, out var fe))
                        throw new InvalidOperationException(
                            $"golden fixture rejected: vector '{name}' trace.{f} missing.");
                    expVec[f] = ZoneArray(fe, zones, $"vector '{name}' trace.{f}");
                }
                var expScalar = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var f in traceScalarFields)
                {
                    if (!traceEl.TryGetProperty(f, out var fe))
                        throw new InvalidOperationException(
                            $"golden fixture rejected: vector '{name}' trace.{f} missing.");
                    expScalar[f] = fe.GetDouble();
                }

                // ── run the pure derivation with CONFIG-LOADED constants ──
                var t = Matchup.DeriveDisplacement(diet, shooter, defenders, usage, cfgMatchup);

                void AssertStage(string stage, double[] act, double[] exp)
                {
                    for (var i = 0; i < 5; i++)
                        if (!Near(act[i], exp[i]))
                            throw new InvalidOperationException(
                                $"GOLDEN PARITY FAILURE — vector '{name}', stage {stage}, zone {zones[i]}:\n" +
                                $"  expected  {exp[i]:R}\n" +
                                $"  actual    {act[i]:R}\n" +
                                "The C# port disagrees with the locked oracle at this stage. The oracle " +
                                "wins — fix the port (or, if config drifted from the locked constants, " +
                                "restore the constants; tuning goes through the oracle-first flow).");
                }
                void AssertScalar(string stage, double act, double exp)
                {
                    if (!Near(act, exp))
                        throw new InvalidOperationException(
                            $"GOLDEN PARITY FAILURE — vector '{name}', scalar {stage}: " +
                            $"expected {exp:R}, actual {act:R}. The oracle wins — fix the port.");
                }

                AssertStage("base",          t.Base,          expVec["base"]);
                AssertStage("gaps",          t.Gaps,          expVec["gaps"]);
                AssertScalar("skillLevel",   t.SkillLevel,    expScalar["skillLevel"]);
                AssertScalar("physLevel",    t.PhysLevel,     expScalar["physLevel"]);
                AssertScalar("level",        t.Level,         expScalar["level"]);
                AssertStage("residuals",     t.Residuals,     expVec["residuals"]);
                AssertStage("bentShapeOnly", t.BentShapeOnly, expVec["bentShapeOnly"]);
                AssertScalar("mag",          t.Mag,           expScalar["mag"]);
                AssertStage("ladder",        t.Ladder,        expVec["ladder"]);
                AssertStage("final",         t.Final,         expVec["final"]);
                run++;
            }

            Console.WriteLine($"    fixture contract accepted (zone order, five-defender lineups, full traces).");
            Console.WriteLine($"    OK — {run} vectors, all stages within tolerance (config-loaded constants).");
        }
        catch (Exception ex) { p1 = false; Console.WriteLine($"  FAIL  (1) {ex.Message}"); }
        pass &= p1;

        // ----------------------------------------------------------------
        // (2) No-displacement equivalence at equal level (§6.5's build
        //     obligation). Shooter: fin 60 / close 40 / mid 50 / out 50 with a
        //     flat diet vs a uniform all-50 defense → diet-weighted skill level
        //     = 0.2·(+10 − 10 + 0 + 0 + 0) = 0 and athleticism equals the
        //     lineup mean → residuals = raw gaps, mag ≈ 0 — the new pipeline
        //     must equal the OLD form renorm(base·mult(rawGap)) within 1e-12,
        //     with usage DELIBERATELY > 0 so the equivalence is level-driven,
        //     not usage-driven.
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) No-displacement equivalence at equal level:");
        bool p2 = true;
        try
        {
            var shooter = Shooter(fin: 60, close: 40, mid: 50, outside: 50, ath: 50);
            var defense = UniformDefense(50.0, 50.0);
            var diet    = new double[] { 20, 20, 20, 20, 20 };
            var t = Matchup.DeriveDisplacement(diet, shooter, defense, usagePressure: 0.15, cfgMatchup);

            // Level exactly-zero premises, checked at tight tolerance (float sums).
            if (Math.Abs(t.SkillLevel) > 1e-12 || Math.Abs(t.PhysLevel) > 1e-12 || Math.Abs(t.Mag) > 1e-12)
                throw new InvalidOperationException(
                    $"fixture construction broken: skillLevel={t.SkillLevel:R} physLevel={t.PhysLevel:R} " +
                    $"mag={t.Mag:R} — all should be ~0 for this shooter/defense.");
            for (var i = 0; i < 5; i++)
                if (!NearTight(t.Residuals[i], t.Gaps[i]))
                    throw new InvalidOperationException(
                        $"residual[{zones[i]}]={t.Residuals[i]:R} != gap {t.Gaps[i]:R} at zero level.");

            // The OLD pipeline, computed inline: renorm(base · mult(rawGap)).
            var old = new double[5];
            var oldSum = 0.0;
            for (var i = 0; i < 5; i++)
            {
                old[i]  = t.Base[i] * Matchup.LocationMultiplierFromGap(t.Gaps[i], cfgMatchup);
                oldSum += old[i];
            }
            for (var i = 0; i < 5; i++) old[i] /= oldSum;

            for (var i = 0; i < 5; i++)
                if (!NearTight(t.Final[i], old[i]))
                    throw new InvalidOperationException(
                        $"equivalence broken at {zones[i]}: new={t.Final[i]:R} old={old[i]:R} " +
                        "— evenly-matched possessions must be numerically pre-build.");

            Console.WriteLine("    OK — equal-level output equals the old pipeline within 1e-12 (usage 0.15).");
        }
        catch (Exception ex) { p2 = false; Console.WriteLine($"  FAIL  (2) {ex.Message}"); }
        pass &= p2;

        // ----------------------------------------------------------------
        // (3) Ablation: DisplacementMaxMagnitude = 0 (in-memory config copy) on
        //     a mismatched, HIGH-usage fixture → output equals the pure shape
        //     bend (displacement contributes exactly zero; the Phase 17
        //     widening is downstream either way). NOTE: 0 does NOT undo Route B
        //     — the residualized bend is the ruled structure, not a dial.
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Ablation (DisplacementMaxMagnitude = 0):");
        bool p3 = true;
        try
        {
            var cfgAblate = MatchupConfig.Load(configPath);
            cfgAblate.DisplacementMaxMagnitude = 0.0;

            var shooter = Shooter(fin: 46, close: 44, mid: 52, outside: 74, ath: 46);
            var defense = UniformDefense(40.0, 40.0);   // clearly advantaged shooter
            var diet    = new double[] { 8, 6, 12, 8, 66 };

            var tReal   = Matchup.DeriveDisplacement(diet, shooter, defense, 0.30, cfgMatchup);
            var tAblate = Matchup.DeriveDisplacement(diet, shooter, defense, 0.30, cfgAblate);

            if (tAblate.Mag != 0.0)
                throw new InvalidOperationException($"ablated mag = {tAblate.Mag:R}, expected exactly 0.");
            if (!(Math.Abs(tReal.Mag) > 0.01))
                throw new InvalidOperationException(
                    $"real-config mag = {tReal.Mag:R} — fixture not actually mismatched+high-usage.");
            for (var i = 0; i < 5; i++)
            {
                if (!NearTight(tAblate.Final[i], tAblate.BentShapeOnly[i]))
                    throw new InvalidOperationException(
                        $"ablation broken at {zones[i]}: final={tAblate.Final[i]:R} " +
                        $"bentShapeOnly={tAblate.BentShapeOnly[i]:R}.");
                if (!NearTight(tAblate.BentShapeOnly[i], tReal.BentShapeOnly[i]))
                    throw new InvalidOperationException(
                        $"bend not independent of the displacement dial at {zones[i]}: " +
                        $"ablated bent={tAblate.BentShapeOnly[i]:R} real bent={tReal.BentShapeOnly[i]:R}.");
            }
            Console.WriteLine("    OK — ablated output equals the pure shape bend; the bend itself is dial-independent.");
        }
        catch (Exception ex) { p3 = false; Console.WriteLine($"  FAIL  (3) {ex.Message}"); }
        pass &= p3;

        // ----------------------------------------------------------------
        // (4) Direction + gate spot-checks (structure only, no magnitudes).
        // ----------------------------------------------------------------
        Console.WriteLine("  (4) Direction + gate spot-checks:");
        bool p4 = true;
        try
        {
            // (4a) Overmatched, high usage → Three up, Rim down vs base.
            {
                var shooter = Shooter(fin: 48, close: 48, mid: 55, outside: 72, ath: 50);
                var defense = UniformDefense(70.0, 70.0);
                var diet    = new double[] { 18, 10, 18, 9, 45 };
                var t = Matchup.DeriveDisplacement(diet, shooter, defense, 0.15, cfgMatchup);
                var ok = t.Level < 0.0 && t.Mag < 0.0
                      && t.Final[4] > t.Base[4] && t.Final[0] < t.Base[0];
                Console.WriteLine($"    (4a) overmatched high-usage: level={t.Level:F1} mag={t.Mag:F3} " +
                                  $"Three {t.Base[4]:P1}->{t.Final[4]:P1} Rim {t.Base[0]:P1}->{t.Final[0]:P1}  {(ok ? "OK" : "FAIL")}");
                p4 &= ok;
            }

            // (4b) Advantaged, high usage, real finisher → Rim up vs base.
            {
                var shooter = Shooter(fin: 78, close: 66, mid: 80, outside: 78, ath: 70);
                var defense = UniformDefense(50.0, 55.0);
                var diet    = new double[] { 30, 15, 22, 11, 22 };
                var t = Matchup.DeriveDisplacement(diet, shooter, defense, 0.15, cfgMatchup);
                var ok = t.Level > 0.0 && t.Mag > 0.0 && t.Final[0] > t.Base[0];
                Console.WriteLine($"    (4b) advantaged finisher: level={t.Level:F1} mag={t.Mag:F3} " +
                                  $"Rim {t.Base[0]:P1}->{t.Final[0]:P1}  {(ok ? "OK" : "FAIL")}");
                p4 &= ok;
            }

            // (4c) Advantaged NON-finisher (Finishing below RimGateLow) → the Rim
            //      rung of the ladder is exactly 0 (invitation fully declined).
            {
                var finBelowGate = (int)cfgMatchup.DisplacementRimGateLow - 8;   // 30 at default 38
                var shooter = Shooter(fin: finBelowGate, close: 44, mid: 52, outside: 74, ath: 50);
                var defense = UniformDefense(40.0, 40.0);
                var diet    = new double[] { 8, 6, 12, 8, 66 };
                var t = Matchup.DeriveDisplacement(diet, shooter, defense, 0.30, cfgMatchup);
                var ok = t.Mag > 0.0 && t.Ladder[0] == 0.0;
                Console.WriteLine($"    (4c) advantaged non-finisher (fin={finBelowGate} < gate {cfgMatchup.DisplacementRimGateLow}): " +
                                  $"mag={t.Mag:F3} rim ladder={t.Ladder[0]:F3}  {(ok ? "OK — invitation declined" : "FAIL")}");
                p4 &= ok;
            }

            // (4d) Zero usage → mag exactly 0 → final equals the pure shape bend.
            {
                var shooter = Shooter(fin: 78, close: 66, mid: 80, outside: 78, ath: 70);
                var defense = UniformDefense(50.0, 55.0);
                var diet    = new double[] { 30, 15, 22, 11, 22 };
                var t = Matchup.DeriveDisplacement(diet, shooter, defense, 0.0, cfgMatchup);
                var ok = t.Mag == 0.0;
                for (var i = 0; i < 5; i++) ok &= NearTight(t.Final[i], t.BentShapeOnly[i]);
                Console.WriteLine($"    (4d) zero usage: mag={t.Mag:F3} final==bentShapeOnly  {(ok ? "OK" : "FAIL")}");
                p4 &= ok;
            }
        }
        catch (Exception ex) { p4 = false; Console.WriteLine($"  FAIL  (4) threw: {ex.Message}"); }
        pass &= p4;

        Console.WriteLine(pass ? "  Phase 56 PASSED." : "  Phase 56 FAILED.");
        return pass;
    }
}
