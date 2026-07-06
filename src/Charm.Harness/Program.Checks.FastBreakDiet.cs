using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    /// <summary>
    /// Forced Roll H generator for the Phase 58 accounting fixtures — returns a Made=1.0
    /// pie (every other <see cref="ShotResult"/> weight 0.0), built by looping the enum so
    /// it stays valid if the enum grows. A made shot terminates the possession immediately,
    /// which is all we need to exercise the fast-break accounting edge at IntoShotResolution.
    /// The putback flag is ignored on purpose — the shot outcome is fixed either way.
    /// </summary>
    private sealed class ForcedMadeRollH : IRollHPieGenerator
    {
        public Pie<ShotResult> Generate(PossessionState state, bool putback = false)
        {
            var w = new Dictionary<ShotResult, double>();
            foreach (var v in Enum.GetValues<ShotResult>()) w[v] = 0.0;
            w[ShotResult.Made] = 1.0;
            return new Pie<ShotResult>(w, 1e-9);   // sums to exactly 1.0 → any positive epsilon
        }
    }

    // A baseline player with the given rim/three tendencies (all other attributes flat).
    // Mirrors the Phase 16 Mk16 helper; kept local so this file is self-contained.
    private static Player Mk58(int b, int rim, int three) =>
        new Player("p58")
        {
            Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
            FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
            SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
            OffensiveRebounding = b,
            PerimeterDefense = b, PostDefense = b, RimProtection = b,
            DefensiveRebounding = b, Steals = b,
            Height = b, Wingspan = b, Weight = b,
            Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = b,
            Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
            HelpDefense = b, OffBallDefense = b,
            RimTendency = rim, ShortTendency = b, MidTendency = b, LongTendency = b,
            ThreeTendency = three,
        };

    // A five-and-five game with the supplied shooter at Home slot 1 and flat fillers
    // elsewhere. Only Home slot 1 matters for the generator's fast-break path (it returns
    // before reading defenders), but a full roster keeps the state legitimate.
    private static GameState BuildGame58(Player homeSlot1)
    {
        var g = new GameState(new FoulTracker(7, 10));
        g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), homeSlot1);
        for (var i = 2; i <= 5; i++)
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), Mk58(50, 20, 20));
        for (var i = 1; i <= 5; i++)
            g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), Mk58(50, 20, 20));
        return g;
    }

    /// <summary>
    /// Phase 58 — fast-break shot diet. Three parts: (1) golden parity of the locked-spec
    /// port <see cref="RollGGenerator.DeriveFastBreakPie"/> against tools/fastbreak_golden.json
    /// (reproduced under BOTH the built-in defaults and the loaded config, at cross-language
    /// tolerance); (2) ShotSelectionBias isolation — coach shot-philosophy must NOT move the
    /// break diet, only PaceBias may; (3) resolver accounting — a fast-break three increments
    /// all three counters, a halfcourt three touches none, and a Roll-K putback (which carries
    /// FastBreak forward) is excluded from the fast-break FGA count.
    /// </summary>
    private static bool Phase58FastBreakDietCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 58: fast-break shot diet (shooter-bent, PaceBias-tilted; golden parity + accounting) ---");
        var pass = true;

        string[] zones = { "Rim", "Short", "Mid", "Long", "Three" };
        const double Eps = 1e-9;

        // Parity holds at tolerance, not equality — for TWO reasons: cross-language ULP
        // drift (Math.Pow vs Python **), and — the one that matters at these magnitudes —
        // the committed fixture stores values ROUNDED TO 10 DECIMALS (the oracle writes
        // round(x, 10)), so absolute diffs up to ~5e-11 are pure fixture rounding, not math
        // error. An absolute floor of 1e-8 absorbs that while still catching every real
        // constant/formula bug (those shift values by >= 1e-5, five orders of magnitude larger).
        static bool Near(double x, double y) =>
            Math.Abs(x - y) <= 1e-8 + 1e-9 * Math.Max(Math.Abs(x), Math.Abs(y));

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/fastbreak_golden.json — defaults AND loaded config.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity vs tools/fastbreak_golden.json (defaults + loaded, both paces):");
        bool p1 = true;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "fastbreak_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // ── fixture contract — validated loudly BEFORE trusting a single number ──
            if (!root.TryGetProperty("zones", out var zo) || zo.GetArrayLength() != 5)
                throw new InvalidOperationException("fastbreak fixture rejected: missing/short zones.");
            for (var i = 0; i < 5; i++)
                if (zo[i].GetString() != zones[i])
                    throw new InvalidOperationException(
                        $"fastbreak fixture rejected: zones[{i}] is '{zo[i].GetString()}', expected '{zones[i]}'. " +
                        "The fixture does not match the locked contract (Rim, Short, Mid, Long, Three).");
            if (!root.TryGetProperty("vectors", out var vectors) || vectors.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("fastbreak fixture rejected: vectors is not an object.");

            // Both big extremes must be present — a stretch-shooting big and a non-shooting
            // big — since they are what prove the diet bends across the full identity range.
            var names = vectors.EnumerateObject().Select(pp => pp.Name).ToList();
            bool Has(string sub) => names.Any(n => n.Contains(sub, StringComparison.OrdinalIgnoreCase));
            if (!Has("Stretch big") || !Has("non-shooter big"))
                throw new InvalidOperationException(
                    "fastbreak fixture rejected: expected both a 'Stretch big' and a 'non-shooter big' vector.");

            var cfgDefault = new RollGConfig();
            var cfgLoaded  = RollGConfig.Load(configPath);

            // (C) defaults == loaded for every field the derivation reads. If config drifts
            //     from the compiled defaults, parity below would be validating the wrong math.
            bool fieldsMatch =
                cfgDefault.FastBreakRim          == cfgLoaded.FastBreakRim &&
                cfgDefault.FastBreakShort        == cfgLoaded.FastBreakShort &&
                cfgDefault.FastBreakMid          == cfgLoaded.FastBreakMid &&
                cfgDefault.FastBreakLong         == cfgLoaded.FastBreakLong &&
                cfgDefault.FastBreakThree        == cfgLoaded.FastBreakThree &&
                cfgDefault.FastBreakShooterPull  == cfgLoaded.FastBreakShooterPull &&
                cfgDefault.FastBreakRatioCapLow  == cfgLoaded.FastBreakRatioCapLow &&
                cfgDefault.FastBreakRatioCapHigh == cfgLoaded.FastBreakRatioCapHigh &&
                cfgDefault.FastBreakPaceTilt     == cfgLoaded.FastBreakPaceTilt &&
                cfgDefault.FastBreakMeanRim      == cfgLoaded.FastBreakMeanRim &&
                cfgDefault.FastBreakMeanShort    == cfgLoaded.FastBreakMeanShort &&
                cfgDefault.FastBreakMeanMid      == cfgLoaded.FastBreakMeanMid &&
                cfgDefault.FastBreakMeanLong     == cfgLoaded.FastBreakMeanLong &&
                cfgDefault.FastBreakMeanThree    == cfgLoaded.FastBreakMeanThree;
            if (!fieldsMatch)
            {
                p1 = false;
                Console.WriteLine("    [FAIL] config-vs-default drift in a fast-break field — parity would be meaningless.");
            }

            double Wt(Pie<ShotLocation> p, int z) =>
                p.Slices.First(s => s.Outcome == Enum.Parse<ShotLocation>(zones[z])).Weight;

            int vecs = 0, mism = 0;
            foreach (var vp in vectors.EnumerateObject())
            {
                var name = vp.Name;
                var v    = vp.Value;

                var tendEl = v.GetProperty("tendency");
                if (tendEl.GetArrayLength() != 5)
                    throw new InvalidOperationException($"fastbreak fixture rejected: '{name}' tendency not length 5.");
                var t = new int[5];
                for (var i = 0; i < 5; i++)
                {
                    var raw = tendEl[i].GetDouble();
                    var asInt = (int)Math.Round(raw);
                    if (Math.Abs(raw - asInt) > 0.0)
                        throw new InvalidOperationException(
                            $"fastbreak fixture rejected: '{name}' tendency[{i}]={raw} not integral (tendencies are ints).");
                    t[i] = asInt;
                }

                foreach (var (paceKey, pace) in new[] { ("fb_pace5", 5.0), ("fb_pace8", 8.0) })
                {
                    var exp = v.GetProperty(paceKey);
                    if (exp.GetArrayLength() != 5)
                        throw new InvalidOperationException($"fastbreak fixture rejected: '{name}' {paceKey} not length 5.");

                    var pieDef = RollGGenerator.DeriveFastBreakPie(t[0], t[1], t[2], t[3], t[4], pace, cfgDefault);
                    var pieLd  = RollGGenerator.DeriveFastBreakPie(t[0], t[1], t[2], t[3], t[4], pace, cfgLoaded);
                    for (var z = 0; z < 5; z++)
                    {
                        var e = exp[z].GetDouble();
                        if (!Near(Wt(pieDef, z), e) || !Near(Wt(pieLd, z), e))
                        {
                            mism++;
                            if (mism <= 3)
                                Console.WriteLine(
                                    $"    [FAIL] '{name}' {paceKey} zone {zones[z]}: def={Wt(pieDef, z):R} ld={Wt(pieLd, z):R} exp={e:R}");
                        }
                    }
                }
                vecs++;
            }
            if (mism > 0) p1 = false;
            Console.WriteLine($"    reproduced {vecs} vectors × 2 paces via DeriveFastBreakPie (defaults+loaded); mismatches={mism}");
        }
        catch (Exception ex) { p1 = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }
        Console.WriteLine($"  (1) {(p1 ? "ok" : "FAIL")}");
        pass &= p1;

        // ----------------------------------------------------------------
        // (2) ShotSelectionBias isolation — coach shot-philosophy must NOT touch the break
        //     diet. The break path reads RAW tendencies (not CoachingPull.Apply); only
        //     PaceBias tilts it. Swing ShotSelectionBias to its extremes at fixed pace 5.
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) ShotSelectionBias does not move the fast-break diet (only PaceBias does):");
        bool p2 = true;
        try
        {
            var cfgG = RollGConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var game = BuildGame58(Mk58(50, rim: 20, three: 60));   // a three-leaning shooter
            var gen  = new RollGGenerator(cfgG, cfgM, game);
            var slot = game.HomeLineup.SlotAt(1);
            var st   = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                          Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound,
                          SelectedSlot: slot, FastBreak: true);

            game.SetCoach(TeamSide.Home, new CoachProfile(shotSelectionBias: 1.0, paceBias: 5.0));
            var pieLow = gen.Generate(st);
            game.SetCoach(TeamSide.Home, new CoachProfile(shotSelectionBias: 10.0, paceBias: 5.0));
            var pieHigh = gen.Generate(st);

            var allEqual = pieLow.Slices.All(s =>
                Math.Abs(s.Weight - pieHigh.Slices.First(x => x.Outcome == s.Outcome).Weight) < Eps);
            p2 = allEqual;
            Console.WriteLine($"    ShotSelectionBias 1 vs 10 at pace 5 → identical break diet: {allEqual}");
        }
        catch (Exception ex) { p2 = false; Console.WriteLine($"  FAIL  (2) threw: {ex.Message}"); }
        Console.WriteLine($"  (2) {(p2 ? "ok" : "FAIL")}");
        pass &= p2;

        // ----------------------------------------------------------------
        // (3) Resolver accounting — seeded fixtures routed through IntoShotResolution with a
        //     forced-made Roll H. The accounting itself reads only state fields + the shot
        //     result, but a MADE shot also credits an assist (AssistPicker needs eligible
        //     teammates), so the game must be POPULATED — an empty roster throws.
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Resolver fast-break accounting (seeded fixtures + putback exclusion):");
        bool p3 = true;
        try
        {
            var cfgM  = MatchupConfig.Load(configPath);
            // Populated roster: a made shot credits an assist and AssistPicker requires
            // eligible non-shooter teammates. The accounting reads state fields, not players,
            // so which players are present does not change the counter assertions below.
            var game  = BuildGame58(Mk58(50, rim: 20, three: 60));
            var slot  = game.HomeLineup.SlotAt(1);

            Resolver MakeResolver() => new Resolver(
                new RollAGenerator(RollAConfig.Load(configPath), cfgM, game),   // never invoked on this path
                RollAConfig.Load(configPath),
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new ForcedMadeRollH(),                                          // forced make
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJStubPieGenerator(RollJConfig.Load(configPath)),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM,
                game,
                new SystemRng(20260706));

            // (i) fast-break three that makes → all three fast-break counters == 1.
            var stFbThree = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                                Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound,
                                SelectedSlot: slot, FastBreak: true, ShotType: ShotLocation.Three);
            var oFb = MakeResolver().Route(new Continue(ContinuationKind.IntoShotResolution, stFbThree));
            var fbThreeOk = oFb.Fga == 1 && oFb.ThreePa == 1 && oFb.ThreePm == 1
                            && oFb.FastBreakFga == 1 && oFb.FastBreakThreePa == 1 && oFb.FastBreakThreePm == 1;

            // (ii) halfcourt three that makes → fast-break counters all 0 (FGA/3PA/3PM still 1).
            var stHcThree = stFbThree with { FastBreak = false };
            var oHc = MakeResolver().Route(new Continue(ContinuationKind.IntoShotResolution, stHcThree));
            var hcOk = oHc.Fga == 1 && oHc.ThreePa == 1 && oHc.ThreePm == 1
                       && oHc.FastBreakFga == 0 && oHc.FastBreakThreePa == 0 && oHc.FastBreakThreePm == 0;

            // (iii) putback regression — Roll K's PutBack output verbatim (RollK.cs L76-81:
            //       ShotType forced to Rim, Putback=true, FastBreak carried forward). The
            //       attempt is a real FGA (Fga increments) but never touched the fast-break
            //       diet, so FastBreakFga must stay 0. This is the exact contamination the
            //       `!c.Putback` guard prevents.
            var stPutback = stFbThree with { ShotType = ShotLocation.Rim };
            var oPb = MakeResolver().Route(
                new Continue(ContinuationKind.IntoShotResolution, stPutback) { Putback = true });
            var putbackOk = oPb.Fga == 1 && oPb.FastBreakFga == 0
                            && oPb.FastBreakThreePa == 0 && oPb.FastBreakThreePm == 0;

            // (iv) the subset chain holds on every fixture: fb3PM ≤ fb3PA ≤ fbFGA ≤ FGA.
            static bool Chain(RoutingOutcome o) =>
                o.FastBreakThreePm <= o.FastBreakThreePa &&
                o.FastBreakThreePa <= o.FastBreakFga &&
                o.FastBreakFga     <= o.Fga;
            var chainOk = Chain(oFb) && Chain(oHc) && Chain(oPb);

            p3 = fbThreeOk && hcOk && putbackOk && chainOk;
            Console.WriteLine($"    (i)   FB three make → fbFGA={oFb.FastBreakFga} fb3PA={oFb.FastBreakThreePa} fb3PM={oFb.FastBreakThreePm} (FGA={oFb.Fga}): {fbThreeOk}");
            Console.WriteLine($"    (ii)  halfcourt three → fbFGA={oHc.FastBreakFga} (FGA={oHc.Fga} 3PA={oHc.ThreePa}): {hcOk}");
            Console.WriteLine($"    (iii) putback (FastBreak carried) → FGA={oPb.Fga} fbFGA={oPb.FastBreakFga}: {putbackOk}");
            Console.WriteLine($"    (iv)  subset chain fb3PM≤fb3PA≤fbFGA≤FGA on all fixtures: {chainOk}");
        }
        catch (Exception ex) { p3 = false; Console.WriteLine($"  FAIL  (3) threw: {ex.Message}"); }
        Console.WriteLine($"  (3) {(p3 ? "ok" : "FAIL")}");
        pass &= p3;

        Console.WriteLine($"Phase 58: {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
