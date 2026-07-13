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
    /// swept slot on top of the flat-50 baseline (every unmentioned field stays 50).
    /// S54: a case may instead carry a per-slot dial map (SlotDials) that dials any
    /// subset of Team A's five slots, each with its own dial set. A case uses EITHER
    /// the legacy single-slot Dials OR SlotDials, never both (enforced by the parser).</summary>
    private sealed record SweepCase(
        string Label,
        Dictionary<string, int> Dials,
        Dictionary<int, Dictionary<string, int>>? SlotDials = null);

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
    // to apply to the swept slot (empty = pure flat-50 control). S54: SlotDials, when
    // non-null, is the per-slot dial map (dials any subset of Team A's five slots) and
    // supersedes the single-slot Dials-on-SweptSlot path for that rung.
    private sealed record SweepRung(
        string Label,
        Dictionary<string, int> Dials,
        Dictionary<int, Dictionary<string, int>>? SlotDials = null);

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

        var results = new List<(SweepRung Rung, SweepRungResult R)>();
        foreach (var rung in rungs)
        {
            Console.Write($"  {rung.Label,-26} ");
            var r = RunSweepRung(config, rung, engineConfigPath);
            results.Add((rung, r));
            Console.WriteLine(" done");
        }

        PrintSweepTable(config, results);
        PrintSweepBoxHeadline(config, results);
        PrintPerManBlock(config, results);
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
            return config.Cases.Select(c => new SweepRung(c.Label, c.Dials, c.SlotDials)).ToList();
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

    // ── S47: the full per-player box, read out of the SAME AttributeGame call the
    // rebound totals already make. Indices 0–4 = Team A slots 1–5; 5–9 = Team B slots
    // 1–5 (identical to SweepRowTotals). OReb/DReb are deliberately NOT duplicated here —
    // they stay in SweepRowTotals as the single reconciled rebound source, and the box
    // readout reads them from there. Every field below is engine-attributed by
    // AttributeGame (blocks/steals/DRB credited to the DEFENDER, assists/ORB to the
    // offense) — there is no re-keying by r.Offense for these; that keys only the team
    // zone-mix arrays, which are the OFFENSE's own shooting that possession.
    private sealed class SweepBoxTotals
    {
        public readonly long[] Fga    = new long[10];
        public readonly long[] Fgm    = new long[10];
        public readonly long[] Tpa    = new long[10];
        public readonly long[] Tpm    = new long[10];
        public readonly long[] Fta    = new long[10];
        public readonly long[] Ftm    = new long[10];
        public readonly long[] Blk    = new long[10];
        public readonly long[] Stl    = new long[10];
        public readonly long[] To     = new long[10];
        public readonly long[] ShFoul = new long[10];   // shooting fouls COMMITTED (defensive stat)
        public readonly long[] Ast    = new long[10];

        // Team shot mix, keyed by which logical team was on offense.
        // Row 0 = Team A offense, row 1 = Team B offense.
        // Column order: 0 Rim, 1 Short, 2 Mid, 3 Long, 4 Three.
        // The five columns partition Fga (each is a ShotLocation subset of Fga), so their
        // row sum is the team's total FGA on its own offensive possessions.
        public readonly long[,] ZoneFga = new long[2, 5];
        public readonly long[,] ZoneFgm = new long[2, 5];
    }

    // What one rung produces: the legacy rebound totals (untouched) plus the S47 box.
    private sealed record SweepRungResult(SweepRowTotals Reb, SweepBoxTotals Box);

    private static SweepRungResult RunSweepRung(SweepConfig config, SweepRung rung, string engineConfigPath)
    {
        // Build the two teams once for this rung. Team A: dialed slots per the rung's
        // per-slot map (legacy single-slot rungs resolve to a one-entry map on SweptSlot);
        // every other slot flat 50. Team B: flat 50 everywhere. Then stamp PlayerIds.
        var teamADials = rung.SlotDials
            ?? new Dictionary<int, Dictionary<string, int>> { [config.SweptSlot] = rung.Dials };
        var teamAPlayers = BuildSweepTeam(teamADials, "TeamA");
        var teamBPlayers = BuildSweepTeam(new Dictionary<int, Dictionary<string, int>>(), "TeamB");
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
        var box    = new SweepBoxTotals();

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

            // S47: the other eleven per-player fields out of the SAME attributed call —
            // OReb/DReb already read above, so they are not re-summed here. Ownership
            // (blocks/steals/DRB to the defender) is already resolved by AttributeGame;
            // we do NOT re-key by r.Offense for the per-player box.
            for (var p = 0; p < 10; p++)
            {
                box.Fga[p]    += attributed.Fga[p];
                box.Fgm[p]    += attributed.Fgm[p];
                box.Tpa[p]    += attributed.Tpa[p];
                box.Tpm[p]    += attributed.Tpm[p];
                box.Fta[p]    += attributed.Fta[p];
                box.Ftm[p]    += attributed.Ftm[p];
                box.Blk[p]    += attributed.Blk[p];
                box.Stl[p]    += attributed.Stl[p];
                box.To[p]     += attributed.To[p];
                box.ShFoul[p] += attributed.ShFoul[p];
                box.Ast[p]    += attributed.Ast[p];
            }

            // S47: team shot mix, keyed by which logical team was on offense (the same
            // r.Offense split the ORB-chance denominator uses). This is the offense's own
            // shooting, so r.Offense is the correct key. Rim/Short/Mid/Long are the four
            // two-point zones; Three is carried separately (ThreePa/ThreePm). The five
            // together partition the possession's Fga.
            foreach (var r in records)
            {
                int ti = r.Offense == teamASide ? 0 : 1;
                box.ZoneFga[ti, 0] += r.RimFga;   box.ZoneFgm[ti, 0] += r.RimFgm;
                box.ZoneFga[ti, 1] += r.ShortFga; box.ZoneFgm[ti, 1] += r.ShortFgm;
                box.ZoneFga[ti, 2] += r.MidFga;   box.ZoneFgm[ti, 2] += r.MidFgm;
                box.ZoneFga[ti, 3] += r.LongFga;  box.ZoneFgm[ti, 3] += r.LongFgm;
                box.ZoneFga[ti, 4] += r.ThreePa;  box.ZoneFgm[ti, 4] += r.ThreePm;
            }
        }

        return new SweepRungResult(totals, box);
    }

    // ── The flat-team-plus-dialed-slots builder ─────────────────────────────────
    //
    // Reuses the bench's flat-50 baseline, its dialable-field whitelist, its per-slot
    // validity rules, and its typed constructor. S54: dials are supplied per slot as a
    // map (slot 1–5 → field→value). The legacy single-slot case is a one-entry map, so a
    // walk or a legacy `dials` case builds byte-identically to before; a stack case dials
    // several slots. Slots absent from the map stay flat 50.
    private static Player[] BuildSweepTeam(
        IReadOnlyDictionary<int, Dictionary<string, int>> slotDials, string teamLabel)
    {
        var players = new Player[5];

        for (var slot = 1; slot <= 5; slot++)
        {
            // 1. Mutable spec seeded at neutral baselines.
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in BenchRatingFields) values[f] = BenchRatingBaseline;
            values[BenchHierarchyField] = BenchHierarchyBaseline;

            // 2. Apply absolute dials for this slot, if any are mapped to it.
            if (slotDials.TryGetValue(slot, out var dials))
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

    private static void PrintSweepTable(SweepConfig config, List<(SweepRung Rung, SweepRungResult R)> results)
    {
        int sw = config.SweptSlot;
        string sOrbHdr = $"S{sw}.ORB";
        string sDrbHdr = $"S{sw}.DRB";
        Console.WriteLine();
        Console.WriteLine(
            $"  {"Row",-26}  {"A.ORB%",6}  {"A.DRB%",6}  " +
            $"{sOrbHdr,7}  {sDrbHdr,7}  {"A.TotR",6}  {"B.TotR",6}  {"RebΔ",6}");
        Console.WriteLine($"  {new string('-', 88)}");

        foreach (var (rung, rr) in results)
        {
            var t = rr.Reb;
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
        string path, SweepConfig config, List<(SweepRung Rung, SweepRungResult R)> results)
    {
        using var w = new StreamWriter(path);

        // Legacy rebound header — VERBATIM (parity anchor). The S47 box groups are
        // appended after it; the legacy column names/order/values are unchanged.
        string legacyHeader =
            "RowLabel,Games,TeamA_ORB_pg,TeamB_ORB_pg,TeamA_DRB_pg,TeamB_DRB_pg," +
            "TeamA_TotalReb_pg,TeamB_TotalReb_pg,TeamA_ORB_rate,TeamA_DRB_rate," +
            "Slot1_ORB,Slot1_DRB,Slot1_TotalReb,Slot2_ORB,Slot2_DRB,Slot2_TotalReb," +
            "Slot3_ORB,Slot3_DRB,Slot3_TotalReb,Slot4_ORB,Slot4_DRB,Slot4_TotalReb," +
            "Slot5_ORB,Slot5_DRB,Slot5_TotalReb";

        // Box column names are rung-independent; build them once from the first rung.
        var headerCols = BuildBoxColumns(config, results[0].Rung, results[0].R.Reb, results[0].R.Box);
        w.WriteLine(legacyHeader + "," + string.Join(",", headerCols.Select(c => c.Name)));

        foreach (var (rung, rr) in results)
        {
            var t = rr.Reb;
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

            // S47 box groups appended after the legacy columns (same order as the header).
            var cells = BuildBoxColumns(config, rung, rr.Reb, rr.Box);
            foreach (var c in cells) sb.Append(',').Append(c.Val);

            w.WriteLine(sb.ToString());
        }
    }

    // ── S47: the appended box columns, header/row aligned by construction ─────────
    //
    // Returns the ordered (columnName, formattedValue) pairs for one rung. WriteSweepCsv
    // emits the Name of the first rung as the header and every rung's Val as a row, so a
    // column and its heading can never drift. Counting stats emit BOTH _total (cumulative
    // rung total) and _pg (= total / Games); rates emit once. Zero denominators produce
    // 0.0, never NaN/∞. All per-player stats come from AttributeGame's already-correct
    // attribution; only the team zone-mix is keyed by offense.
    private static List<(string Name, string Val)> BuildBoxColumns(
        SweepConfig config, SweepRung rung, SweepRowTotals t, SweepBoxTotals box)
    {
        double g = t.Games;
        int sw0 = config.SweptSlot - 1;                 // 0–4, Team A
        var cols = new List<(string, string)>();

        long TeamA(long[] a) { long s = 0; for (var i = 0; i < 5; i++) s += a[i];     return s; }
        long TeamB(long[] a) { long s = 0; for (var i = 5; i < 10; i++) s += a[i];    return s; }

        void Cnt(string name, long total)
        {
            cols.Add(($"{name}_total", total.ToString()));
            cols.Add(($"{name}_pg", (total / g).ToString("F3")));
        }
        void Pct(string name, long made, long att) =>
            cols.Add(($"{name}_pct", (att > 0 ? 100.0 * made / att : 0.0).ToString("F2")));
        void Rate(string name, double num, double den) =>
            cols.Add(($"{name}_rate", (den > 0 ? num / den : 0.0).ToString("F4")));

        // ── meta: walk-traceability (empty in cases mode) ────────────────────────
        string metaField = "", metaValue = "";
        if (config.Mode == "walk" && config.Walk is { } wk)
        {
            metaField = wk.Field;
            metaValue = rung.Dials.TryGetValue(wk.Field, out var mv) ? mv.ToString() : "";
        }
        cols.Add(("meta_swept_field", metaField));
        cols.Add(("meta_swept_value", metaValue));

        // ── swept slot: full box + derived rates ─────────────────────────────────
        long sFga = box.Fga[sw0], sFgm = box.Fgm[sw0];
        long s3pa = box.Tpa[sw0], s3pm = box.Tpm[sw0];
        long sFta = box.Fta[sw0], sFtm = box.Ftm[sw0];
        long sOrb = t.AOReb[sw0], sDrb = t.ADReb[sw0];  // legacy rebound source
        long sReb = sOrb + sDrb;
        long sAst = box.Ast[sw0], sStl = box.Stl[sw0], sBlk = box.Blk[sw0];
        long sTo  = box.To[sw0],  sSfl = box.ShFoul[sw0];
        long sPts = 2 * sFgm + s3pm + sFtm;
        double sUse = sFga + 0.44 * sFta + sTo;

        Cnt("swept_pts", sPts); Cnt("swept_fga", sFga); Cnt("swept_fgm", sFgm);
        Cnt("swept_3pa", s3pa); Cnt("swept_3pm", s3pm);
        Cnt("swept_fta", sFta); Cnt("swept_ftm", sFtm);
        Cnt("swept_orb", sOrb); Cnt("swept_drb", sDrb); Cnt("swept_reb", sReb);
        Cnt("swept_ast", sAst); Cnt("swept_stl", sStl); Cnt("swept_blk", sBlk);
        Cnt("swept_to",  sTo);  Cnt("swept_sfl", sSfl);
        Pct("swept_fg", sFgm, sFga); Pct("swept_3p", s3pm, s3pa); Pct("swept_ft", sFtm, sFta);
        Rate("swept_3pa", s3pa, sFga);          // 3PA / FGA
        Rate("swept_ftr", sFta, sFga);          // FTr = FTA / FGA
        cols.Add(("swept_possession_use_pg", (sUse / g).ToString("F3")));

        // ── team offense/defense boxes ───────────────────────────────────────────
        void TeamBox(string tag, Func<long[], long> team, long orb, long drb)
        {
            long fga = team(box.Fga), fgm = team(box.Fgm);
            long p3a = team(box.Tpa), p3m = team(box.Tpm);
            long fta = team(box.Fta), ftm = team(box.Ftm);
            long ast = team(box.Ast), to  = team(box.To);
            long blk = team(box.Blk), stl = team(box.Stl), sfl = team(box.ShFoul);
            long pts = 2 * fgm + p3m + ftm;

            Cnt($"{tag}_off_pts", pts); Cnt($"{tag}_off_fga", fga); Cnt($"{tag}_off_fgm", fgm);
            Cnt($"{tag}_off_3pa", p3a); Cnt($"{tag}_off_3pm", p3m);
            Cnt($"{tag}_off_fta", fta); Cnt($"{tag}_off_ftm", ftm);
            Cnt($"{tag}_off_ast", ast); Cnt($"{tag}_off_to",  to); Cnt($"{tag}_off_orb", orb);
            Pct($"{tag}_off_fg", fgm, fga); Pct($"{tag}_off_3p", p3m, p3a); Pct($"{tag}_off_ft", ftm, fta);
            Rate($"{tag}_off_3pa", p3a, fga);
            Rate($"{tag}_off_ftr", fta, fga);

            Cnt($"{tag}_def_blk", blk); Cnt($"{tag}_def_stl", stl);
            Cnt($"{tag}_def_drb", drb); Cnt($"{tag}_def_sfl", sfl);
        }

        long aOrb = t.AOReb.Sum(), aDrb = t.ADReb.Sum();
        long bOrb = t.BOReb.Sum(), bDrb = t.BDReb.Sum();

        // PossessionUseShare for the swept slot uses its OWN team's (Team A) use.
        double teamAUse = TeamA(box.Fga) + 0.44 * TeamA(box.Fta) + TeamA(box.To);
        cols.Add(("swept_possession_use_share",
            (teamAUse > 0 ? sUse / teamAUse : 0.0).ToString("F4")));

        TeamBox("teamA", TeamA, aOrb, aDrb);
        TeamBox("teamB", TeamB, bOrb, bDrb);

        // ── S54: Team B per-man shooting lines (mirror-slot opponent proxy) ──────
        // Each Team B slot's OWN attributed shooting line, read from the same box the
        // team aggregates use (box indices 5–9). It is the mirror-slot opponent's line:
        // a primary-matchup proxy that INCLUDES all attributed attempts (putbacks are
        // counted, A2). Counts emit _total and _pg; FG%/3P%/2P% and FTr (=FTA/FGA) derive
        // from the summed counts. Zero denominators emit 0.0 here — same CSV convention as
        // every other rate column (the console block uses a dash sentinel instead).
        void PerMan(int bSlot)                       // bSlot 1..5 → box index bSlot+4
        {
            int bi = bSlot + 4;
            long fga = box.Fga[bi], fgm = box.Fgm[bi];
            long p3a = box.Tpa[bi], p3m = box.Tpm[bi];
            long fta = box.Fta[bi], ftm = box.Ftm[bi];
            long f2a = fga - p3a,   f2m = fgm - p3m;   // two-point attempts/makes
            string tag = $"teamB_man{bSlot}";
            Cnt($"{tag}_fga", fga); Cnt($"{tag}_fgm", fgm);
            Cnt($"{tag}_3pa", p3a); Cnt($"{tag}_3pm", p3m);
            Cnt($"{tag}_fta", fta); Cnt($"{tag}_ftm", ftm);
            Pct($"{tag}_fg", fgm, fga);
            Pct($"{tag}_3p", p3m, p3a);
            Pct($"{tag}_2p", f2m, f2a);
            Rate($"{tag}_ftr", fta, fga);
        }
        for (var b = 1; b <= 5; b++) PerMan(b);

        // ── team zone mix (offense-keyed shot shares + zone FG%) ─────────────────
        string[] zoneNames = { "rim", "short", "mid", "long", "three" };
        void ZoneMix(string tag, int ti)
        {
            long tot = 0; for (var z = 0; z < 5; z++) tot += box.ZoneFga[ti, z];
            for (var z = 0; z < 5; z++)
                Rate($"{tag}_zone_{zoneNames[z]}", box.ZoneFga[ti, z], tot);
            for (var z = 0; z < 5; z++)
                Pct($"{tag}_zone_{zoneNames[z]}", box.ZoneFgm[ti, z], box.ZoneFga[ti, z]);
        }
        ZoneMix("teamA", 0);
        ZoneMix("teamB", 1);

        return cols;
    }

    // ── S47: console headline — scannable, NOT a duplicate of the CSV width ───────
    private static void PrintSweepBoxHeadline(
        SweepConfig config, List<(SweepRung Rung, SweepRungResult R)> results)
    {
        int sw = config.SweptSlot;

        Console.WriteLine();
        Console.WriteLine($"  Box headline — swept slot (S{sw}) + team scoring");
        Console.WriteLine(
            $"  {"Row",-26}  {"PTS",5}  {"FG%",5}  {"3P%",5}  {"FT%",5}  " +
            $"{"REB",5}  {"AST",4}  {"STL",4}  {"BLK",4}  {"TO",4}  {"Use%",5}  {"A.PTS",6}  {"B.PTS",6}");
        Console.WriteLine($"  {new string('-', 108)}");

        foreach (var (rung, rr) in results)
        {
            var t = rr.Reb; var box = rr.Box;
            double g = t.Games;
            int sw0 = sw - 1;

            long sFgm = box.Fgm[sw0], s3pm = box.Tpm[sw0], sFtm = box.Ftm[sw0];
            long sFga = box.Fga[sw0], s3pa = box.Tpa[sw0], sFta = box.Fta[sw0];
            long sReb = t.AOReb[sw0] + t.ADReb[sw0];
            long sPts = 2 * sFgm + s3pm + sFtm;
            double sUse = sFga + 0.44 * sFta + box.To[sw0];

            double FgP = sFga > 0 ? 100.0 * sFgm / sFga : 0.0;
            double TpP = s3pa > 0 ? 100.0 * s3pm / s3pa : 0.0;
            double FtP = sFta > 0 ? 100.0 * sFtm / sFta : 0.0;

            long aFgm = 0, a3pm = 0, aFtm = 0, bFgm = 0, b3pm = 0, bFtm = 0;
            double teamAUse = 0;
            for (var i = 0; i < 5; i++)
            {
                aFgm += box.Fgm[i]; a3pm += box.Tpm[i]; aFtm += box.Ftm[i];
                teamAUse += box.Fga[i] + 0.44 * box.Fta[i] + box.To[i];
            }
            for (var i = 5; i < 10; i++) { bFgm += box.Fgm[i]; b3pm += box.Tpm[i]; bFtm += box.Ftm[i]; }
            double aPts = (2 * aFgm + a3pm + aFtm) / g;
            double bPts = (2 * bFgm + b3pm + bFtm) / g;
            double useShare = teamAUse > 0 ? 100.0 * sUse / teamAUse : 0.0;

            Console.WriteLine(
                $"  {rung.Label,-26}  {sPts / g,5:F1}  {FgP,5:F1}  {TpP,5:F1}  {FtP,5:F1}  " +
                $"{sReb / g,5:F1}  {box.Ast[sw0] / g,4:F1}  {box.Stl[sw0] / g,4:F1}  " +
                $"{box.Blk[sw0] / g,4:F1}  {box.To[sw0] / g,4:F1}  {useShare,5:F1}  {aPts,6:F1}  {bPts,6:F1}");
        }

        // Team shot mix (attempt share, %) — Team A then Team B.
        Console.WriteLine();
        Console.WriteLine("  Team shot mix — attempt share % (Rim / Short / Mid / Long / 3PA)");
        Console.WriteLine(
            $"  {"Row",-26}  {"A:Rim",6}  {"Short",6}  {"Mid",6}  {"Long",6}  {"3PA",6}  " +
            $"{"B:Rim",6}  {"Short",6}  {"Mid",6}  {"Long",6}  {"3PA",6}");
        Console.WriteLine($"  {new string('-', 108)}");

        foreach (var (rung, rr) in results)
        {
            var box = rr.Box;
            string Row(int ti)
            {
                long tot = 0; for (var z = 0; z < 5; z++) tot += box.ZoneFga[ti, z];
                var parts = new double[5];
                for (var z = 0; z < 5; z++) parts[z] = tot > 0 ? 100.0 * box.ZoneFga[ti, z] / tot : 0.0;
                return $"{parts[0],6:F1}  {parts[1],6:F1}  {parts[2],6:F1}  {parts[3],6:F1}  {parts[4],6:F1}";
            }
            Console.WriteLine($"  {rung.Label,-26}  {Row(0)}  {Row(1)}");
        }
    }

    // ── S54: per-man console block — the mirror-slot opponent's own shooting line ─
    //
    // Reads the SAME Team B per-slot box (indices 5–9) the CSV per-man columns use. It is
    // the primary-matchup proxy: a Team B slot's line is guarded by his mirror on his own
    // primary shots, but INCLUDES all attributed attempts (a putback he took is contested
    // by his own mirror too; a putback contest on another slot stays a team channel — A2).
    //
    // Walk mode prints just the covered man (Team B slot == SweptSlot, whose defender is
    // the swept slot) — the one undiluted read the walk exists to produce. Cases mode
    // prints all five Team B slots per rung so covered-vs-uncovered is visible side by side.
    // Zero-denominator rates print as a dash, never NaN/∞/0.0.
    private static void PrintPerManBlock(
        SweepConfig config, List<(SweepRung Rung, SweepRungResult R)> results)
    {
        Console.WriteLine();
        Console.WriteLine(
            "  Per-man — mirror-slot opponent shooting line " +
            "(primary-matchup proxy; incl. all attributed attempts)");
        Console.WriteLine(
            $"  {"Row",-26}  {"B.slot",6}  {"FGA",5}  {"FG%",5}  {"3PA",5}  {"3P%",5}  " +
            $"{"2P%",5}  {"FTA",5}  {"FTr",5}");
        Console.WriteLine($"  {new string('-', 92)}");

        bool walk = config.Mode == "walk";

        foreach (var (rung, rr) in results)
        {
            var box = rr.Box;
            double g = rr.Reb.Games;
            int lo = walk ? config.SweptSlot : 1;
            int hi = walk ? config.SweptSlot : 5;

            for (var bSlot = lo; bSlot <= hi; bSlot++)
            {
                int bi = bSlot + 4;
                long fga = box.Fga[bi], fgm = box.Fgm[bi];
                long p3a = box.Tpa[bi], p3m = box.Tpm[bi];
                long fta = box.Fta[bi];
                long f2a = fga - p3a,  f2m = fgm - p3m;

                string fgP  = fga > 0 ? (100.0 * fgm / fga).ToString("F1") : "—";
                string tpP  = p3a > 0 ? (100.0 * p3m / p3a).ToString("F1") : "—";
                string twoP = f2a > 0 ? (100.0 * f2m / f2a).ToString("F1") : "—";
                string ftr  = fga > 0 ? ((double)fta / fga).ToString("F2") : "—";

                // Row label prints once per rung (on the first slot line).
                string label = bSlot == lo ? rung.Label : "";

                Console.WriteLine(
                    $"  {label,-26}  {bSlot,6}  {fga / g,5:F1}  {fgP,5}  {p3a / g,5:F1}  {tpP,5}  " +
                    $"{twoP,5}  {fta / g,5:F1}  {ftr,5}");
            }

            if (!walk) Console.WriteLine();   // blank line between rungs in cases mode
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
                    RejectUnknownOrDuplicateKeys(caseEl, ctx, "label", "dials", "slotDials");

                    if (!caseEl.TryGetProperty("label", out var lEl) || lEl.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(lEl.GetString()))
                        throw new InvalidOperationException($"{ctx} requires a non-empty string 'label'.");
                    var label = lEl.GetString()!;
                    if (!seenLabels.Add(label))
                        throw new InvalidOperationException($"duplicate case label '{label}'.");

                    bool hasDials     = caseEl.TryGetProperty("dials", out var dialsEl);
                    bool hasSlotDials = caseEl.TryGetProperty("slotDials", out var slotDialsEl);
                    if (hasDials && hasSlotDials)
                        throw new InvalidOperationException(
                            $"{ctx}: a case uses EITHER 'dials' (single slot) OR 'slotDials' (per-slot map), not both.");

                    // Local: parse one field→value object into a validated dial dict.
                    Dictionary<string, int> ParseDialObject(JsonElement obj, string dctx)
                    {
                        if (obj.ValueKind != JsonValueKind.Object)
                            throw new InvalidOperationException($"{dctx} must be an object of field→value.");
                        var d = new Dictionary<string, int>(StringComparer.Ordinal);
                        var seenFields = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var kv in obj.EnumerateObject())
                        {
                            if (!seenFields.Add(kv.Name))
                                throw new InvalidOperationException($"{dctx}: duplicate dial field '{kv.Name}'.");
                            if (!BenchDialableFields.Contains(kv.Name))
                                throw new InvalidOperationException(
                                    $"{dctx}: unknown dial field '{kv.Name}' (case-sensitive).");
                            if (kv.Value.ValueKind != JsonValueKind.Number || !kv.Value.TryGetInt32(out var dv))
                                throw new InvalidOperationException(
                                    $"{dctx}: dial '{kv.Name}' must be an integer (got {kv.Value.GetRawText()}).");
                            d[kv.Name] = dv;
                        }
                        return d;
                    }

                    var dials = new Dictionary<string, int>(StringComparer.Ordinal);
                    Dictionary<int, Dictionary<string, int>>? slotDials = null;

                    if (hasDials)
                    {
                        dials = ParseDialObject(dialsEl, $"{ctx} 'dials'");
                    }
                    else if (hasSlotDials)
                    {
                        if (slotDialsEl.ValueKind != JsonValueKind.Object)
                            throw new InvalidOperationException(
                                $"{ctx} 'slotDials' must be an object of slot(\"1\"–\"5\")→dial-object.");
                        slotDials = new Dictionary<int, Dictionary<string, int>>();
                        var seenSlots = new HashSet<int>();
                        foreach (var sEntry in slotDialsEl.EnumerateObject())
                        {
                            if (!int.TryParse(sEntry.Name, out var slotNum) || slotNum < 1 || slotNum > 5)
                                throw new InvalidOperationException(
                                    $"{ctx} 'slotDials': key '{sEntry.Name}' must be a slot number \"1\"–\"5\".");
                            if (!seenSlots.Add(slotNum))
                                throw new InvalidOperationException(
                                    $"{ctx} 'slotDials': duplicate slot '{slotNum}'.");
                            slotDials[slotNum] = ParseDialObject(sEntry.Value, $"{ctx} 'slotDials'[{slotNum}]");
                        }
                        if (slotDials.Count == 0)
                            throw new InvalidOperationException(
                                $"{ctx} 'slotDials' must dial at least one slot (empty map is not a valid case).");
                    }

                    cases.Add(new SweepCase(label, dials, slotDials));
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
