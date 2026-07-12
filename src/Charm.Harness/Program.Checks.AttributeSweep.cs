using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // ─────────────────────────────────────────────────────────────────────────
    //  ATTRIBUTE SWEEP — the general findings bench. NOT part of the validation suite.
    //
    //  Purpose: learn empirically what a rating DOES across its range. It walks ONE
    //  attribute on ONE player up its 0–99 range (or runs a set of named stress rows),
    //  runs N real games per rung through the SAME Resolver/Governor wiring every other
    //  instrument and the stress test use, and tabulates the real outcome. Every number
    //  is engine truth, not browser arithmetic.
    //
    //  This is a GENERALIZATION of the hardcoded SizeExperiment / AthleticismExperiment
    //  pattern into one parameterized bench: WHICH attribute to sweep and WHICH player
    //  carries it are inputs (a small JSON config), not baked in. It reuses the lab
    //  bench's flat-50 team-builder pieces (BenchRatingFields, BenchSpecToPlayer,
    //  ValidateBenchSpec), the shared StampPlayerId / SeatRoster / AttributeGame seam,
    //  and the same per-game Resolver construction the bench uses.
    //
    //  ISOLATION DISCIPLINE (an Emmett ruling): every dial except the one under study is
    //  pinned to the flat all-50 baseline — no random-rolled context around the swept
    //  dial. The curve is therefore the swept attribute's pure, exactly-repeatable signal.
    //
    //  The swept player is a single slot on logical Team A (default slot 5 = the C).
    //  The opponent (Team B) is flat all-50. Per-player rebound attribution is read from
    //  the engine-stamped picks (the same source the box score uses), so the readout
    //  answers "does *he* personally rebound?", not only "does the lineup win the glass?".
    //
    //  Dispatched from Program.cs by the `sweep` token; it returns before the validation
    //  suite, so it is never part of the default run.
    //
    //    Initial compile:  dotnet build src/Charm.Harness
    //    Run (explicit):   dotnet run --no-build --project src/Charm.Harness -- sweep path/to/sweep.json
    //    Run (bare):       dotnet run --no-build --project src/Charm.Harness -- sweep
    //                      (resolves "sweep.json" from the current directory)
    //
    //  --no-build keeps "edit one text file, rerun, see the change" literally true: the
    //  sweep config is read fresh from the given path, never copied to the build output.
    // ─────────────────────────────────────────────────────────────────────────

    // ── Parsed config model ────────────────────────────────────────────────────

    /// <summary>Walk mode: step one field from Start to Stop by Step. If the final step
    /// does not land exactly on Stop, Stop is appended as a last rung (so 0..99 by 5
    /// yields 0,5,…,95,99 — 21 rungs).</summary>
    private sealed record SweepWalk(string Field, int Start, int Stop, int Step);

    /// <summary>Cases mode: one named row. Dials are absolute values applied to the
    /// swept slot on top of the flat-50 baseline (every unmentioned field stays 50).</summary>
    private sealed record SweepCase(string Label, Dictionary<string, int> Dials);

    private sealed class SweepConfig
    {
        public int GamesPerRung { get; init; }
        public int BaseSeed     { get; init; }
        public int SweptSlot    { get; init; }          // 1–5 on logical Team A
        public string Mode      { get; init; } = "walk"; // "walk" | "cases"
        public string OutputName { get; init; } = "sweep";
        public SweepWalk? Walk   { get; init; }
        public List<SweepCase> Cases { get; init; } = new();
    }

    // A resolved rung: the row label shown in the table + CSV, and the absolute dials
    // to apply to the swept slot (empty = pure flat-50 control).
    private sealed record SweepRung(string Label, Dictionary<string, int> Dials);

    // ── Entry point (called from the Program.cs `sweep` dispatch) ───────────────

    private static void RunAttributeSweep(string engineConfigPath, string? sweepPathArg)
    {
        string sweepPath;
        if (!string.IsNullOrWhiteSpace(sweepPathArg))
        {
            sweepPath = Path.GetFullPath(sweepPathArg);
        }
        else
        {
            sweepPath = Path.GetFullPath("sweep.json");
            Console.WriteLine("No sweep path given; resolving 'sweep.json' from the current directory:");
            Console.WriteLine($"  {sweepPath}");
        }

        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Attribute Sweep (findings bench) ===");
        Console.WriteLine($"Sweep config: {sweepPath}");
        Console.WriteLine();

        if (!File.Exists(sweepPath))
        {
            Console.WriteLine($"Sweep config not found at: {sweepPath}");
            Console.WriteLine("Pass an explicit path, e.g.:");
            Console.WriteLine("  dotnet run --no-build --project src/Charm.Harness -- sweep path/to/sweep.json");
            return;
        }

        SweepConfig config;
        List<SweepRung> rungs;
        try
        {
            config = ParseSweepConfig(File.ReadAllText(sweepPath));
            rungs  = BuildRungs(config);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("SWEEP CONFIG ERROR:");
            Console.WriteLine("  " + ex.Message);
            return;
        }

        int totalGames = rungs.Count * config.GamesPerRung;
        Console.WriteLine($"Mode: {config.Mode}  |  swept slot: {config.SweptSlot} (logical Team A)  |  opponent: flat all-50");
        Console.WriteLine($"{config.GamesPerRung:N0} games / rung  |  {rungs.Count} rungs  |  {totalGames:N0} total games");
        if (config.Mode == "walk" && config.Walk is { } w)
            Console.WriteLine($"Sweeping {w.Field}: {w.Start}..{w.Stop} step {w.Step} (everything else frozen at 50)");
        Console.WriteLine();

        var results = new List<(SweepRung Rung, SweepRowTotals T)>();
        foreach (var rung in rungs)
        {
            Console.Write($"  {rung.Label,-26} ");
            var t = RunSweepRung(config, rung, engineConfigPath);
            results.Add((rung, t));
            Console.WriteLine(" done");
        }

        PrintSweepTable(config, results);
        var csvPath = Path.Combine(AppContext.BaseDirectory, $"attribute_{config.OutputName}_sweep.csv");
        WriteSweepCsv(csvPath, config, results);
        Console.WriteLine();
        Console.WriteLine($"  CSV  →  {csvPath}");
        Console.WriteLine();
        Console.WriteLine("  Read the swept slot's own ORB/DRB columns to see whether the rating moves HIM,");
        Console.WriteLine("  not just the team. Interaction rows move body and rating together on purpose —");
        Console.WriteLine("  compare TERRIBLE_BODY_GREAT_REB vs FREAK_BODY_ZERO_REB to see which wins.");
        Console.WriteLine();
    }

    // ── Rung construction ───────────────────────────────────────────────────────

    private static List<SweepRung> BuildRungs(SweepConfig config)
    {
        if (config.Mode == "walk")
        {
            if (config.Walk is not { } w)
                throw new InvalidOperationException("mode is 'walk' but no 'walk' block was given.");

            var vals = BuildWalkValues(w.Start, w.Stop, w.Step);
            var rungs = new List<SweepRung>(vals.Count);
            foreach (var v in vals)
            {
                var dials = new Dictionary<string, int>(StringComparer.Ordinal) { [w.Field] = v };
                rungs.Add(new SweepRung($"{w.Field}={v}", dials));
            }
            return rungs;
        }

        if (config.Mode == "cases")
        {
            if (config.Cases.Count == 0)
                throw new InvalidOperationException("mode is 'cases' but the 'cases' array is empty.");
            return config.Cases.Select(c => new SweepRung(c.Label, c.Dials)).ToList();
        }

        throw new InvalidOperationException($"unknown mode '{config.Mode}' (allowed: walk, cases).");
    }

    // Inclusive walk from Start to Stop by Step; Stop always appears as the final rung.
    private static List<int> BuildWalkValues(int start, int stop, int step)
    {
        if (step <= 0)
            throw new InvalidOperationException($"walk 'step' must be a positive integer (got {step}).");

        var vals = new List<int>();
        if (stop >= start)
            for (var v = start; v <= stop; v += step) vals.Add(v);
        else
            for (var v = start; v >= stop; v -= step) vals.Add(v);

        if (vals.Count == 0 || vals[^1] != stop) vals.Add(stop);
        return vals;
    }

    // ── Per-rung accumulator ────────────────────────────────────────────────────
    //
    // ORB chances/won come from the possession records (the true ORB-rate denominator
    // OrbWon/OrbChances). Per-player boards come from AttributeGame — the SAME engine-
    // stamped attribution the box score uses — so team totals and per-slot credit are
    // one reconciled source. Indices 0–4 = Team A slots 1–5; 5–9 = Team B slots 1–5.
    private sealed class SweepRowTotals
    {
        public int Games;
        public long AOrbChances, AOrbWon, BOrbChances, BOrbWon;
        public readonly long[] AOReb = new long[5];
        public readonly long[] ADReb = new long[5];
        public readonly long[] BOReb = new long[5];
        public readonly long[] BDReb = new long[5];
    }

    private static SweepRowTotals RunSweepRung(SweepConfig config, SweepRung rung, string engineConfigPath)
    {
        // Build the two teams once for this rung. Team A: swept slot dialed, the rest
        // flat 50. Team B: flat 50 everywhere. Then stamp PlayerIds by logical team.
        var teamAPlayers = BuildSweepTeam(rung.Dials, config.SweptSlot, "TeamA");
        var teamBPlayers = BuildSweepTeam(new Dictionary<string, int>(), sweptSlot: 0, "TeamB");
        for (var i = 0; i < 5; i++) teamAPlayers[i] = StampPlayerId(teamAPlayers[i], i + 1);
        for (var i = 0; i < 5; i++) teamBPlayers[i] = StampPlayerId(teamBPlayers[i], i + 6);

        // Load engine configs once (immutable after load) — copied from RunBenchMatchup.
        var cfg          = RollAConfig.Load(engineConfigPath);
        var cfgB         = RollBConfig.Load(engineConfigPath);
        var cfgC         = RollCConfig.Load(engineConfigPath);
        var cfgD         = RollDConfig.Load(engineConfigPath);
        var cfgE         = RollEConfig.Load(engineConfigPath);
        var cfgF         = RollFConfig.Load(engineConfigPath);
        var cfgG         = RollGConfig.Load(engineConfigPath);
        var cfgH         = RollHConfig.Load(engineConfigPath);
        var cfgI         = RollIConfig.Load(engineConfigPath);
        var cfgJ         = RollJConfig.Load(engineConfigPath);
        var cfgK         = RollKConfig.Load(engineConfigPath);
        var cfgL         = RollLConfig.Load(engineConfigPath);
        var cfgM         = RollMConfig.Load(engineConfigPath);
        var cfgOffFoul   = RollOffensiveFoulConfig.Load(engineConfigPath);
        var cfgGov       = GovernorConfig.Load(engineConfigPath);
        var cfgClock     = RollClockConfig.Load(engineConfigPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(engineConfigPath);
        var cfgMatchup   = MatchupConfig.Load(engineConfigPath);
        var cfgAttention = AttentionConfig.Load(engineConfigPath);

        var totals = new SweepRowTotals { Games = config.GamesPerRung };

        for (var i = 0; i < config.GamesPerRung; i++)
        {
            if (i % 500 == 0 && i > 0) Console.Write(".");

            int gameSeed = config.BaseSeed + i;

            // Deterministic side balancing (matches the bench): logical Team A is Home
            // on even indices, Away on odd, so any home/away asymmetry splits evenly.
            bool teamAIsHome = (i % 2 == 0);
            TeamSide teamASide = teamAIsHome ? TeamSide.Home : TeamSide.Away;
            TeamSide teamBSide = teamAIsHome ? TeamSide.Away : TeamSide.Home;

            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            SeatRoster(game, teamASide, teamAPlayers);
            SeatRoster(game, teamBSide, teamBPlayers);

            var resolverRng = new SystemRng(gameSeed);
            var governorRng = new SystemRng(gameSeed + 1);

            var resolver = new Resolver(
                new RollAGenerator(cfg, cfgMatchup, game),
                cfg,
                new RollBGenerator(cfgB, cfgMatchup, game),
                new RollCGenerator(cfgC),
                cfgC,
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, game),
                new AttentionGenerator(cfgAttention, game),
                new RollFGenerator(cfgF, cfgMatchup, game),
                new RollGGenerator(cfgG, cfgMatchup, game),
                new RollHGenerator(cfgH, cfgMatchup, game),
                new RollIGenerator(cfgI, cfgMatchup, game),
                new RollJGenerator(cfgJ, cfgMatchup, game),
                new RollKGenerator(cfgK, cfgMatchup, game),
                new RollLGenerator(cfgL, game),
                new RollMGenerator(cfgM, cfgMatchup, game),
                new RollOffensiveFoulGenerator(cfgOffFoul),
                cfgMatchup,
                game,
                resolverRng);

            var governor   = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndOfHalf);
            var firstState = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1);
            var result     = governor.Run(firstState);
            var records    = result.Possessions;

            // ORB chances/won by logical team (the ORB-rate denominator/numerator).
            totals.AOrbChances += records.Where(r => r.Offense == teamASide).Sum(r => (long)r.OrbChances);
            totals.AOrbWon     += records.Where(r => r.Offense == teamASide).Sum(r => (long)r.OrbWon);
            totals.BOrbChances += records.Where(r => r.Offense == teamBSide).Sum(r => (long)r.OrbChances);
            totals.BOrbWon     += records.Where(r => r.Offense == teamBSide).Sum(r => (long)r.OrbWon);

            // Per-player boards — engine-stamped attribution (same source as the box score).
            var attributed = AttributeGame(result, game, gameSeed);
            for (var s = 0; s < 5; s++)
            {
                totals.AOReb[s] += attributed.OReb[s];
                totals.ADReb[s] += attributed.DReb[s];
                totals.BOReb[s] += attributed.OReb[s + 5];
                totals.BDReb[s] += attributed.DReb[s + 5];
            }
        }

        return totals;
    }

    // ── The flat-team-plus-one-dialed-slot builder ──────────────────────────────
    //
    // Reuses the bench's flat-50 baseline, its dialable-field whitelist, its per-slot
    // validity rules, and its typed constructor. The ONLY difference from the bench
    // builder is that dials land on a single named slot rather than a per-slot config.
    private static Player[] BuildSweepTeam(Dictionary<string, int> dials, int sweptSlot, string teamLabel)
    {
        var players = new Player[5];

        for (var slot = 1; slot <= 5; slot++)
        {
            // 1. Mutable spec seeded at neutral baselines.
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in BenchRatingFields) values[f] = BenchRatingBaseline;
            values[BenchHierarchyField] = BenchHierarchyBaseline;

            // 2. Apply absolute dials only on the swept slot.
            if (slot == sweptSlot)
            {
                foreach (var kv in dials)
                {
                    if (!BenchDialableFields.Contains(kv.Key))
                        throw new InvalidOperationException(
                            $"{teamLabel} slot {slot}: unknown dial field '{kv.Key}' " +
                            "(must be an authored 0–99 rating or HierarchyRank; case-sensitive).");
                    values[kv.Key] = kv.Value;
                }
            }

            // 3. Validate the spec (bench-owned engine-validity rules) before constructing.
            ValidateBenchSpec(values, teamLabel, slot);

            // 4. Construct once via the shared typed initializer.
            var player = BenchSpecToPlayer(values, $"{teamLabel}_Slot{slot}");

            // 5. Post-construction assertion (builder-bug guard).
            var errs = player.Validate();
            if (errs.Count > 0)
                throw new InvalidOperationException(
                    $"builder bug — {teamLabel} slot {slot} constructed player failed Player.Validate():\n  " +
                    string.Join("\n  ", errs));

            players[slot - 1] = player;
        }

        return players;
    }

    // ── Readout: console table ──────────────────────────────────────────────────

    private static void PrintSweepTable(SweepConfig config, List<(SweepRung Rung, SweepRowTotals T)> results)
    {
        int sw = config.SweptSlot;
        string sOrbHdr = $"S{sw}.ORB";
        string sDrbHdr = $"S{sw}.DRB";
        Console.WriteLine();
        Console.WriteLine(
            $"  {"Row",-26}  {"A.ORB%",6}  {"A.DRB%",6}  " +
            $"{sOrbHdr,7}  {sDrbHdr,7}  {"A.TotR",6}  {"B.TotR",6}  {"RebΔ",6}");
        Console.WriteLine($"  {new string('-', 88)}");

        foreach (var (rung, t) in results)
        {
            double g = t.Games;

            double aOrbRate = t.AOrbChances > 0 ? 100.0 * t.AOrbWon / t.AOrbChances : 0.0;
            double aDrbRate = t.BOrbChances > 0 ? 100.0 * (t.BOrbChances - t.BOrbWon) / t.BOrbChances : 0.0;

            double sweptOrb = t.AOReb[sw - 1] / g;
            double sweptDrb = t.ADReb[sw - 1] / g;

            double aTot = (t.AOReb.Sum() + t.ADReb.Sum()) / g;
            double bTot = (t.BOReb.Sum() + t.BDReb.Sum()) / g;

            Console.WriteLine(
                $"  {rung.Label,-26}  {aOrbRate,6:F1}  {aDrbRate,6:F1}  " +
                $"{sweptOrb,7:F2}  {sweptDrb,7:F2}  {aTot,6:F1}  {bTot,6:F1}  {aTot - bTot,+6:F1}");
        }
    }

    // ── Readout: CSV ────────────────────────────────────────────────────────────

    private static void WriteSweepCsv(
        string path, SweepConfig config, List<(SweepRung Rung, SweepRowTotals T)> results)
    {
        using var w = new StreamWriter(path);
        w.WriteLine(
            "RowLabel,Games,TeamA_ORB_pg,TeamB_ORB_pg,TeamA_DRB_pg,TeamB_DRB_pg," +
            "TeamA_TotalReb_pg,TeamB_TotalReb_pg,TeamA_ORB_rate,TeamA_DRB_rate," +
            "Slot1_ORB,Slot1_DRB,Slot1_TotalReb,Slot2_ORB,Slot2_DRB,Slot2_TotalReb," +
            "Slot3_ORB,Slot3_DRB,Slot3_TotalReb,Slot4_ORB,Slot4_DRB,Slot4_TotalReb," +
            "Slot5_ORB,Slot5_DRB,Slot5_TotalReb");

        foreach (var (rung, t) in results)
        {
            double g = t.Games;

            double aOrbPg = t.AOReb.Sum() / g;
            double bOrbPg = t.BOReb.Sum() / g;
            double aDrbPg = t.ADReb.Sum() / g;
            double bDrbPg = t.BDReb.Sum() / g;
            double aTotPg = aOrbPg + aDrbPg;
            double bTotPg = bOrbPg + bDrbPg;

            double aOrbRate = t.AOrbChances > 0 ? (double)t.AOrbWon / t.AOrbChances : 0.0;
            double aDrbRate = t.BOrbChances > 0 ? (double)(t.BOrbChances - t.BOrbWon) / t.BOrbChances : 0.0;

            var sb = new System.Text.StringBuilder();
            sb.Append($"{rung.Label},{t.Games},");
            sb.Append($"{aOrbPg:F3},{bOrbPg:F3},{aDrbPg:F3},{bDrbPg:F3},");
            sb.Append($"{aTotPg:F3},{bTotPg:F3},{aOrbRate:F4},{aDrbRate:F4}");
            for (var s = 0; s < 5; s++)
            {
                double orb = t.AOReb[s] / g;
                double drb = t.ADReb[s] / g;
                sb.Append($",{orb:F3},{drb:F3},{orb + drb:F3}");
            }
            w.WriteLine(sb.ToString());
        }
    }

    // ── Strict config parser (tree-walk; unknown + duplicate keys rejected) ─────

    private static SweepConfig ParseSweepConfig(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException jx)
        {
            throw new InvalidOperationException($"sweep config is not valid JSON — {jx.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("sweep config root must be a JSON object.");

            RejectUnknownOrDuplicateKeys(
                root, "root",
                "gamesPerRung", "baseSeed", "sweptSlot", "mode", "outputName", "walk", "cases");

            var gamesPerRung = RequireIntProperty(root, "gamesPerRung", "root");
            if (gamesPerRung <= 0)
                throw new InvalidOperationException(
                    $"gamesPerRung must be a positive integer (got {gamesPerRung}).");

            var baseSeed = RequireIntProperty(root, "baseSeed", "root");

            var sweptSlot = root.TryGetProperty("sweptSlot", out _)
                ? RequireIntProperty(root, "sweptSlot", "root")
                : 5;
            if (sweptSlot < 1 || sweptSlot > 5)
                throw new InvalidOperationException(
                    $"sweptSlot must be 1–5 (got {sweptSlot}); it names the Team A slot that carries the swept rating.");

            if (!root.TryGetProperty("mode", out var modeEl) || modeEl.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("missing required string 'mode' in root (allowed: walk, cases).");
            var mode = modeEl.GetString()!;
            if (mode != "walk" && mode != "cases")
                throw new InvalidOperationException($"unknown mode '{mode}' (allowed: walk, cases).");

            string outputName = "sweep";
            if (root.TryGetProperty("outputName", out var onEl))
            {
                if (onEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(onEl.GetString()))
                    throw new InvalidOperationException("'outputName', if present, must be a non-empty string.");
                outputName = onEl.GetString()!;
            }

            SweepWalk? walk = null;
            var cases = new List<SweepCase>();

            if (mode == "walk")
            {
                if (!root.TryGetProperty("walk", out var walkEl) || walkEl.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("mode 'walk' requires a 'walk' object.");
                RejectUnknownOrDuplicateKeys(walkEl, "walk", "field", "start", "stop", "step");

                if (!walkEl.TryGetProperty("field", out var fEl) || fEl.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("walk 'field' must be a string naming the rating to sweep.");
                var field = fEl.GetString()!;
                if (!BenchDialableFields.Contains(field))
                    throw new InvalidOperationException(
                        $"walk 'field' = '{field}' is not a dialable rating (case-sensitive).");

                var start = RequireIntProperty(walkEl, "start", "walk");
                var stop  = RequireIntProperty(walkEl, "stop", "walk");
                var step  = RequireIntProperty(walkEl, "step", "walk");
                if (field != BenchHierarchyField && (start < 0 || start > 99 || stop < 0 || stop > 99))
                    throw new InvalidOperationException(
                        $"walk start/stop must be within 0–99 for rating '{field}' (got start={start}, stop={stop}).");

                walk = new SweepWalk(field, start, stop, step);
                if (outputName == "sweep") outputName = field;   // default CSV name = swept field
            }
            else // cases
            {
                if (!root.TryGetProperty("cases", out var casesEl) || casesEl.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("mode 'cases' requires a 'cases' array.");

                var seenLabels = new HashSet<string>(StringComparer.Ordinal);
                int idx = 0;
                foreach (var caseEl in casesEl.EnumerateArray())
                {
                    string ctx = $"cases[{idx}]";
                    if (caseEl.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException($"{ctx} must be an object.");
                    RejectUnknownOrDuplicateKeys(caseEl, ctx, "label", "dials");

                    if (!caseEl.TryGetProperty("label", out var lEl) || lEl.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(lEl.GetString()))
                        throw new InvalidOperationException($"{ctx} requires a non-empty string 'label'.");
                    var label = lEl.GetString()!;
                    if (!seenLabels.Add(label))
                        throw new InvalidOperationException($"duplicate case label '{label}'.");

                    var dials = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (caseEl.TryGetProperty("dials", out var dialsEl))
                    {
                        if (dialsEl.ValueKind != JsonValueKind.Object)
                            throw new InvalidOperationException($"{ctx} 'dials' must be an object of field→value.");
                        var seenFields = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var d in dialsEl.EnumerateObject())
                        {
                            if (!seenFields.Add(d.Name))
                                throw new InvalidOperationException($"{ctx}: duplicate dial field '{d.Name}'.");
                            if (!BenchDialableFields.Contains(d.Name))
                                throw new InvalidOperationException(
                                    $"{ctx}: unknown dial field '{d.Name}' (case-sensitive).");
                            if (d.Value.ValueKind != JsonValueKind.Number || !d.Value.TryGetInt32(out var dv))
                                throw new InvalidOperationException(
                                    $"{ctx}: dial '{d.Name}' must be an integer (got {d.Value.GetRawText()}).");
                            dials[d.Name] = dv;
                        }
                    }

                    cases.Add(new SweepCase(label, dials));
                    idx++;
                }
                if (outputName == "sweep") outputName = "cases";
            }

            return new SweepConfig
            {
                GamesPerRung = gamesPerRung,
                BaseSeed     = baseSeed,
                SweptSlot    = sweptSlot,
                Mode         = mode,
                OutputName   = outputName,
                Walk         = walk,
                Cases        = cases,
            };
        }
    }
}
