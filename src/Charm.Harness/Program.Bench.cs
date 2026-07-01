using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// The player-generation lab bench — Phase 1: the dialed matchup + channel readout.
//
// A HARNESS-ONLY experimental instrument. No engine file changes; the bench reuses
// the same roster seam, the same per-game Resolver/Governor wiring, and the same
// PossessionRecord channels the stress test reads. What is new here is (a) a flat
// all-50 team-builder that a plain-text config dials on top of, (b) a strict config
// reader, and (c) a single-matchup readout: an "Applied Dials" echo plus a per-team
// channel breakdown.
//
// Dispatched from Program.cs by the `bench` token; it returns before the validation
// suite, so the bench is never part of the default run.
//
//   Initial compile:  dotnet build src/Charm.Harness
//   Run (explicit):   dotnet run --no-build --project src/Charm.Harness -- bench path/to/bench.json
//   Run (bare):       dotnet run --no-build --project src/Charm.Harness -- bench
//                     (resolves "bench.json" from the current directory and prints the
//                      resolved path before parsing)
//
// --no-build is what makes "edit one text file, rerun, see the change" literally true:
// the bench config is read fresh from the given path, never copied to the build output.
// ============================================================================

internal static partial class Program
{
    // ── The dialable-field whitelist (single source of truth) ──────────────────
    // Every 0–99 authored Player attribute, INCLUDING the five zone tendencies.
    // HierarchyRank is the sole special-range field ([1,10], baseline 5) and is held
    // separately below. Deliberately excluded: PlayerId (attribution identity), Name,
    // and the derived computed properties (Athleticism, GravityContribution,
    // SpacingContribution). This list is NOT built by reflection — the exclusions are
    // the whole point, and reflection cannot set init-only properties anyway.
    private static readonly string[] BenchRatingFields =
    {
        "Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
        "RimTendency", "ShortTendency", "MidTendency", "LongTendency", "ThreeTendency",
        "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
        "OffBallMovement", "Screening", "OffensiveRebounding",
        "PerimeterDefense", "PostDefense", "RimProtection", "DefensiveRebounding",
        "Steals", "HelpDefense", "OffBallDefense",
        "Height", "Wingspan", "Weight", "Strength", "Speed", "Quickness",
        "FirstStep", "Vertical", "Endurance", "Hustle", "BasketballIQ", "Discipline",
    };
    private const string BenchHierarchyField    = "HierarchyRank";
    private const int    BenchRatingBaseline    = 50;   // flat neutral for every 0–99 rating
    private const int    BenchHierarchyBaseline = 5;    // HierarchyRank's own neutral (NOT 50)

    // Whitelist as an ordinal (case-sensitive) set: "RimProtection" is valid,
    // "rimProtection" is an unknown-field error like any other typo.
    private static readonly HashSet<string> BenchDialableFields =
        new(new List<string>(BenchRatingFields) { BenchHierarchyField }, StringComparer.Ordinal);

    // ── Parsed config model ────────────────────────────────────────────────────

    /// <summary>One explicit dial from the config: (slot, field, op, raw value).
    /// Op is "set" (absolute) or "add" (delta in rating points).</summary>
    private sealed record BenchDial(int Slot, string Field, string Op, int Value);

    /// <summary>The dials for one logical team. An empty list is a flat team.</summary>
    private sealed class BenchTeamSpec
    {
        public readonly List<BenchDial> Dials = new();
    }

    private sealed class BenchConfig
    {
        public int GameCount { get; init; }
        public int BaseSeed  { get; init; }
        public BenchTeamSpec TeamA { get; init; } = new();
        public BenchTeamSpec TeamB { get; init; } = new();
    }

    // ── Entry point (called from the Program.cs `bench` dispatch) ───────────────

    private static void RunBench(string engineConfigPath, string? benchPathArg)
    {
        // Resolve the bench config path. Explicit arg wins; bare `bench` resolves
        // "bench.json" relative to the process current working directory and prints
        // the resolved path, so there is no invisible "repo root" assumption.
        string benchPath;
        if (!string.IsNullOrWhiteSpace(benchPathArg))
        {
            benchPath = Path.GetFullPath(benchPathArg);
        }
        else
        {
            benchPath = Path.GetFullPath("bench.json");
            Console.WriteLine("No bench path given; resolving 'bench.json' from the current directory:");
            Console.WriteLine($"  {benchPath}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Project Charm :: Player-Generation Lab Bench (Phase 1) ===");
        Console.WriteLine($"Bench config: {benchPath}");
        Console.WriteLine();

        if (!File.Exists(benchPath))
        {
            Console.WriteLine($"Bench config not found at: {benchPath}");
            Console.WriteLine("Pass an explicit path, e.g.:");
            Console.WriteLine("  dotnet run --no-build --project src/Charm.Harness -- bench path/to/bench.json");
            return;
        }

        // Parse. A config error is reported plainly and stops before any game runs.
        BenchConfig config;
        try
        {
            config = ParseBenchConfig(File.ReadAllText(benchPath));
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("BENCH CONFIG ERROR:");
            Console.WriteLine("  " + ex.Message);
            return;
        }

        // Build both teams from the flat baseline + dials. Any invalid dial (out of
        // range after set/add, all-zero tendencies, HierarchyRank outside [1,10])
        // fails loudly here, naming the team/slot/field/value, before any game runs.
        Player[] teamAPlayers, teamBPlayers;
        List<BenchAppliedDial> appliedA, appliedB;
        try
        {
            (teamAPlayers, appliedA) = BuildBenchTeam(config.TeamA, "Team A");
            (teamBPlayers, appliedB) = BuildBenchTeam(config.TeamB, "Team B");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("BENCH BUILD ERROR:");
            Console.WriteLine("  " + ex.Message);
            return;
        }

        // Stamp PlayerId by LOGICAL team (A → 1–5, B → 6–10), once, before seating.
        // The stamped arrays are what every game seats, so the ID is stable across the
        // home/away flip and the box-score attribution indexes correctly.
        for (var i = 0; i < 5; i++) teamAPlayers[i] = StampPlayerId(teamAPlayers[i], i + 1);
        for (var i = 0; i < 5; i++) teamBPlayers[i] = StampPlayerId(teamBPlayers[i], i + 6);

        Console.WriteLine($"Running {config.GameCount} games on one matchup (base seed {config.BaseSeed}) ...");
        Console.WriteLine();

        var stats = RunBenchMatchup(config, teamAPlayers, teamBPlayers, engineConfigPath);

        PrintAppliedDials(appliedA, appliedB);
        PrintBenchChannels(stats, teamAPlayers, teamBPlayers);
    }

    // ── Strict config parser (tree-walk; unknown + duplicate keys rejected) ─────
    //
    // Uses JsonDocument rather than JsonSerializer.Deserialize on purpose: default
    // deserialization silently keeps the last of a duplicated key and silently drops
    // unknown keys — either of which turns a typo into a silent flat-team run. A
    // tree-walk with explicit membership + duplicate detection at every level closes
    // that trap. (JsonDocument preserves duplicate property names on enumeration, so
    // the seen-set below actually sees them.)

    private static BenchConfig ParseBenchConfig(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException jx)
        {
            throw new InvalidOperationException($"bench config is not valid JSON — {jx.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("bench config root must be a JSON object.");

            RejectUnknownOrDuplicateKeys(root, "root", "gameCount", "baseSeed", "teamA", "teamB");

            var gameCount = RequireIntProperty(root, "gameCount", "root");
            if (gameCount <= 0)
                throw new InvalidOperationException(
                    $"gameCount must be a positive integer (got {gameCount}); a zero-game run has nothing to report.");

            var baseSeed = RequireIntProperty(root, "baseSeed", "root");

            var teamA = ParseBenchTeam(root, "teamA");
            var teamB = ParseBenchTeam(root, "teamB");

            return new BenchConfig
            {
                GameCount = gameCount,
                BaseSeed  = baseSeed,
                TeamA     = teamA,
                TeamB     = teamB,
            };
        }
    }

    private static BenchTeamSpec ParseBenchTeam(JsonElement root, string teamName)
    {
        var spec = new BenchTeamSpec();

        if (!root.TryGetProperty(teamName, out var teamEl))
            return spec;   // team absent → flat
        if (teamEl.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"'{teamName}' must be an object.");

        RejectUnknownOrDuplicateKeys(teamEl, teamName, "slots");

        if (!teamEl.TryGetProperty("slots", out var slotsEl))
            return spec;   // no slots → flat
        if (slotsEl.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"'{teamName}.slots' must be an object.");

        // Slot keys are dynamic ("1".."5"): detect duplicates, and validate each names
        // a real lineup spot.
        var seenSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slotProp in slotsEl.EnumerateObject())
        {
            if (!seenSlots.Add(slotProp.Name))
                throw new InvalidOperationException($"{teamName}.slots has duplicate slot '{slotProp.Name}'.");
            if (!int.TryParse(slotProp.Name, out var slotNum) || slotNum < 1 || slotNum > 5)
                throw new InvalidOperationException(
                    $"{teamName}.slots has invalid slot '{slotProp.Name}' (must be 1–5).");
            ParseBenchSlot(slotProp.Value, spec, teamName, slotNum);
        }

        return spec;
    }

    private static void ParseBenchSlot(JsonElement slotEl, BenchTeamSpec spec, string teamName, int slot)
    {
        if (slotEl.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{teamName} slot {slot} must be an object.");

        RejectUnknownOrDuplicateKeys(slotEl, $"{teamName} slot {slot}", "set", "add");

        var setFields = ParseBenchFieldMap(slotEl, "set", spec, teamName, slot);
        var addFields = ParseBenchFieldMap(slotEl, "add", spec, teamName, slot);

        // A field may be set OR added on one slot, never both.
        foreach (var f in setFields)
            if (addFields.Contains(f))
                throw new InvalidOperationException(
                    $"{teamName} slot {slot} field '{f}' appears in both 'set' and 'add' (one operation per field).");
    }

    // Adds this map's dials to the spec and returns the field names it added (so the
    // caller can detect a set/add conflict on the same field).
    private static HashSet<string> ParseBenchFieldMap(
        JsonElement slotEl, string op, BenchTeamSpec spec, string teamName, int slot)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);

        if (!slotEl.TryGetProperty(op, out var mapEl))
            return fields;
        if (mapEl.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{teamName} slot {slot} '{op}' must be an object.");

        foreach (var prop in mapEl.EnumerateObject())
        {
            if (!fields.Add(prop.Name))
                throw new InvalidOperationException(
                    $"{teamName} slot {slot} '{op}' has duplicate field '{prop.Name}'.");
            if (!BenchDialableFields.Contains(prop.Name))
                throw new InvalidOperationException(
                    $"{teamName} slot {slot} '{op}' has unknown field '{prop.Name}' " +
                    "(not a dialable rating; field names are case-sensitive).");
            if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out var val))
                throw new InvalidOperationException(
                    $"{teamName} slot {slot} '{op}' field '{prop.Name}' must be an integer (got {prop.Value.GetRawText()}).");

            spec.Dials.Add(new BenchDial(slot, prop.Name, op, val));
        }

        return fields;
    }

    // Rejects any duplicate key and any key not in the allowed set, at this object
    // level. Ordinal (case-sensitive) throughout.
    private static void RejectUnknownOrDuplicateKeys(JsonElement obj, string ctx, params string[] allowed)
    {
        var allow = new HashSet<string>(allowed, StringComparer.Ordinal);
        var seen  = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in obj.EnumerateObject())
        {
            if (!seen.Add(prop.Name))
                throw new InvalidOperationException($"duplicate key '{prop.Name}' in {ctx}.");
            if (!allow.Contains(prop.Name))
                throw new InvalidOperationException(
                    $"unknown key '{prop.Name}' in {ctx} (allowed: {string.Join(", ", allowed)}; case-sensitive).");
        }
    }

    private static int RequireIntProperty(JsonElement obj, string name, string ctx)
    {
        if (!obj.TryGetProperty(name, out var el))
            throw new InvalidOperationException($"missing required '{name}' in {ctx}.");
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var v))
            throw new InvalidOperationException($"'{name}' in {ctx} must be an integer (got {el.GetRawText()}).");
        return v;
    }

    // ── The flat-team-plus-dials builder ───────────────────────────────────────
    //
    // Player's authored properties are init-only (settable only in the object
    // initializer, never mutated, no reflection path), so the dials are resolved on a
    // mutable spec first, then a Player is constructed once from the validated spec.

    /// <summary>One row of the Applied Dials echo: the field, its operation, the raw
    /// dialed value, its own baseline (50 for ratings, 5 for HierarchyRank), and the
    /// resolved final value.</summary>
    private sealed record BenchAppliedDial(int Slot, string Field, string Op, int Value, int Baseline, int Final);

    private static (Player[] Players, List<BenchAppliedDial> Applied) BuildBenchTeam(
        BenchTeamSpec spec, string teamLabel)
    {
        var players = new Player[5];
        var applied = new List<BenchAppliedDial>();

        for (var slot = 1; slot <= 5; slot++)
        {
            // 1. Mutable spec seeded at neutral baselines.
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in BenchRatingFields) values[f] = BenchRatingBaseline;
            values[BenchHierarchyField] = BenchHierarchyBaseline;

            // 2. Apply `set` (absolute) then `add` (delta on the post-set value).
            var slotDials = spec.Dials.Where(d => d.Slot == slot).ToList();
            foreach (var d in slotDials.Where(d => d.Op == "set")) values[d.Field]  = d.Value;
            foreach (var d in slotDials.Where(d => d.Op == "add")) values[d.Field] += d.Value;

            // 3. Validate the spec (bench-owned rules) BEFORE constructing anything.
            ValidateBenchSpec(values, teamLabel, slot);

            // 4. Construct the Player once via the typed object initializer.
            var player = BenchSpecToPlayer(values, $"{teamLabel.Replace(" ", "")}_Slot{slot}");

            // 5. Post-construction assertion: the spec was already validated, so any
            //    error here means the typed construction did not faithfully express it.
            var errs = player.Validate();
            if (errs.Count > 0)
                throw new InvalidOperationException(
                    $"builder bug — {teamLabel} slot {slot} constructed player failed Player.Validate():\n  " +
                    string.Join("\n  ", errs));

            players[slot - 1] = player;

            // Echo every explicitly dialed field with its own baseline and final value.
            foreach (var d in slotDials)
            {
                var baseline = d.Field == BenchHierarchyField ? BenchHierarchyBaseline : BenchRatingBaseline;
                applied.Add(new BenchAppliedDial(slot, d.Field, d.Op, d.Value, baseline, values[d.Field]));
            }
        }

        return (players, applied);
    }

    // The bench-owned validity rules. These are real engine-validity rules surfaced at
    // config time, NOT realism guardrails — nothing is softened or clamped.
    private static void ValidateBenchSpec(Dictionary<string, int> values, string teamLabel, int slot)
    {
        // 0–99 range on every rating (a set or add can push a value out of bounds).
        foreach (var f in BenchRatingFields)
        {
            var v = values[f];
            if (v < 0 || v > 99)
                throw new InvalidOperationException(
                    $"{teamLabel} slot {slot} field '{f}' = {v} is outside the 0–99 range " +
                    "(a set or add pushed it out of bounds).");
        }

        // HierarchyRank [1,10] — bench-owned, because Player.Validate() does not check
        // it (RollEGenerator would otherwise only catch it at usage time, mid-game).
        var hr = values[BenchHierarchyField];
        if (hr < 1 || hr > 10)
            throw new InvalidOperationException(
                $"{teamLabel} slot {slot} field 'HierarchyRank' = {hr} is outside the [1, 10] range.");

        // The five zone tendencies cannot all be zero (Roll G could not build a shot pie).
        var tendencySum = values["RimTendency"] + values["ShortTendency"] + values["MidTendency"]
                        + values["LongTendency"] + values["ThreeTendency"];
        if (tendencySum <= 0)
            throw new InvalidOperationException(
                $"{teamLabel} slot {slot} has all five zone tendencies at 0 (tendency sum must be > 0). " +
                "Any individual tendency may be 0, but leave at least one nonzero.");
    }

    // Typed object initializer reading every field from the validated spec. Mirrors the
    // shape of PlayerConfig.ToPlayer / StampPlayerId. PlayerId is intentionally NOT set
    // here — it is stamped by logical team after construction.
    private static Player BenchSpecToPlayer(Dictionary<string, int> v, string name) => new Player(name)
    {
        Close               = v["Close"],
        Mid                 = v["Mid"],
        Outside             = v["Outside"],
        Finishing           = v["Finishing"],
        FreeThrow           = v["FreeThrow"],
        FoulDrawing         = v["FoulDrawing"],
        RimTendency         = v["RimTendency"],
        ShortTendency       = v["ShortTendency"],
        MidTendency         = v["MidTendency"],
        LongTendency        = v["LongTendency"],
        ThreeTendency       = v["ThreeTendency"],
        BallHandling        = v["BallHandling"],
        Passing             = v["Passing"],
        Playmaking          = v["Playmaking"],
        SelfCreation        = v["SelfCreation"],
        PostMoves           = v["PostMoves"],
        OffBallMovement     = v["OffBallMovement"],
        Screening           = v["Screening"],
        OffensiveRebounding = v["OffensiveRebounding"],
        PerimeterDefense    = v["PerimeterDefense"],
        PostDefense         = v["PostDefense"],
        RimProtection       = v["RimProtection"],
        DefensiveRebounding = v["DefensiveRebounding"],
        Steals              = v["Steals"],
        HelpDefense         = v["HelpDefense"],
        OffBallDefense      = v["OffBallDefense"],
        Height              = v["Height"],
        Wingspan            = v["Wingspan"],
        Weight              = v["Weight"],
        Strength            = v["Strength"],
        Speed               = v["Speed"],
        Quickness           = v["Quickness"],
        FirstStep           = v["FirstStep"],
        Vertical            = v["Vertical"],
        Endurance           = v["Endurance"],
        Hustle              = v["Hustle"],
        BasketballIQ        = v["BasketballIQ"],
        Discipline          = v["Discipline"],
        HierarchyRank       = v["HierarchyRank"],
    };

    // ── The single-matchup driver ──────────────────────────────────────────────
    //
    // Mirrors the stress test's per-game loop exactly (same config loads, same Resolver
    // wiring, same seed derivation, same deterministic side alternation), but for one
    // matchup and one accumulator. Every channel is accumulated back into LOGICAL Team
    // A / Team B, not physical Home / Away.

    private static BenchStats RunBenchMatchup(
        BenchConfig config, Player[] teamAPlayers, Player[] teamBPlayers, string engineConfigPath)
    {
        // Load engine configs once (immutable after load).
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

        var stats = new BenchStats();

        for (var i = 0; i < config.GameCount; i++)
        {
            int gameSeed = config.BaseSeed + i;

            // Deterministic side balancing (D4): logical Team A is Home on even indices,
            // Away on odd. The same index always yields the same seed AND the same side,
            // so any future home/away asymmetry splits evenly across A and B instead of
            // becoming a confound. Every channel below re-maps physical → logical.
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

            var governor = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndOfHalf);
            var firstState = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1);

            // Literal philosophy: a dialed matchup runs exactly as built. If a valid
            // (in-range, nonzero-tendency) config crashes a game, that is a real engine
            // finding, so the exception is left to surface with its stack trace rather
            // than swallowed.
            var result  = governor.Run(firstState);
            var records = result.Possessions;

            // Per-player attribution — used only for the turnover reconciliation.
            var attributed = AttributeGame(result, game, gameSeed);

            stats.Accumulate(records, game, attributed, teamASide, teamBSide);
        }

        return stats;
    }

    // ── The single-matchup accumulator (logical Team A / Team B) ────────────────
    //
    // Reads the same PossessionRecord channels the stress test reads, plus two the
    // stress test's VariantStats does not carry: per-zone MAKES (needed to print zone
    // FG%) and per-team turnovers (the new per-team channel). Kept as a focused
    // bench-local accumulator so Program.Stress.cs stays untouched.

    private sealed class BenchStats
    {
        public int Games;
        public int TeamAWins, TeamBWins, Ties;
        public readonly List<int> TeamAScores = new();
        public readonly List<int> TeamBScores = new();
        public readonly List<int> Margins     = new();   // A − B

        public long AOffPoss, BOffPoss, APoints, BPoints;
        public long AFga, BFga, AFgm, BFgm;
        public long ARimA,   BRimA,   ARimM,   BRimM;
        public long AShortA, BShortA, AShortM, BShortM;
        public long AMidA,   BMidA,   AMidM,   BMidM;
        public long ALongA,  BLongA,  ALongM,  BLongM;
        public long A3pa, B3pa, A3pm, B3pm;
        public long AFta, BFta, AFtm, BFtm;
        public long AOrbC, BOrbC, AOrbW, BOrbW;
        public long ATrans, BTrans;
        public long ATurnovers,   BTurnovers;     // all turnover possessions (incl. team violations)
        public long ACommitterTo, BCommitterTo;   // turnovers with an individual committer
        public readonly long[] ASlotFga = new long[5];
        public readonly long[] BSlotFga = new long[5];

        // Per-player turnovers from attribution (index 0–4 = Team A ids 1–5, 5–9 = Team B ids 6–10).
        public readonly long[] PlayerTo = new long[10];

        public void Accumulate(
            IReadOnlyList<PossessionRecord> records, GameState game,
            PlayerBoxTotals attributed, TeamSide teamASide, TeamSide teamBSide)
        {
            Games++;

            int aScore = teamASide == TeamSide.Home ? game.HomeScore : game.AwayScore;
            int bScore = teamASide == TeamSide.Home ? game.AwayScore : game.HomeScore;
            TeamAScores.Add(aScore);
            TeamBScores.Add(bScore);
            Margins.Add(aScore - bScore);
            if (aScore > bScore) TeamAWins++;
            else if (bScore > aScore) TeamBWins++;
            else Ties++;

            long SumA(Func<PossessionRecord, int> f) => records.Where(r => r.Offense == teamASide).Sum(r => (long)f(r));
            long SumB(Func<PossessionRecord, int> f) => records.Where(r => r.Offense == teamBSide).Sum(r => (long)f(r));

            AOffPoss += records.Count(r => r.Offense == teamASide);
            BOffPoss += records.Count(r => r.Offense == teamBSide);
            APoints  += SumA(r => r.Points);   BPoints  += SumB(r => r.Points);
            AFga += SumA(r => r.Fga);           BFga += SumB(r => r.Fga);
            AFgm += SumA(r => r.Fgm);           BFgm += SumB(r => r.Fgm);

            ARimA   += SumA(r => r.RimFga);     BRimA   += SumB(r => r.RimFga);
            ARimM   += SumA(r => r.RimFgm);     BRimM   += SumB(r => r.RimFgm);
            AShortA += SumA(r => r.ShortFga);   BShortA += SumB(r => r.ShortFga);
            AShortM += SumA(r => r.ShortFgm);   BShortM += SumB(r => r.ShortFgm);
            AMidA   += SumA(r => r.MidFga);     BMidA   += SumB(r => r.MidFga);
            AMidM   += SumA(r => r.MidFgm);     BMidM   += SumB(r => r.MidFgm);
            ALongA  += SumA(r => r.LongFga);    BLongA  += SumB(r => r.LongFga);
            ALongM  += SumA(r => r.LongFgm);    BLongM  += SumB(r => r.LongFgm);

            A3pa += SumA(r => r.ThreePa);       B3pa += SumB(r => r.ThreePa);
            A3pm += SumA(r => r.ThreePm);       B3pm += SumB(r => r.ThreePm);
            AFta += SumA(r => r.Fta);           BFta += SumB(r => r.Fta);
            AFtm += SumA(r => r.Ftm);           BFtm += SumB(r => r.Ftm);
            AOrbC += SumA(r => r.OrbChances);   BOrbC += SumB(r => r.OrbChances);
            AOrbW += SumA(r => r.OrbWon);       BOrbW += SumB(r => r.OrbWon);

            ATrans += records.Count(r => r.Offense == teamASide && r.Entry == EntryType.Transition);
            BTrans += records.Count(r => r.Offense == teamBSide && r.Entry == EntryType.Transition);

            ATurnovers   += records.Count(r => r.Offense == teamASide && IsTurnoverPossession(r));
            BTurnovers   += records.Count(r => r.Offense == teamBSide && IsTurnoverPossession(r));
            ACommitterTo += records.Count(r => r.Offense == teamASide && IsTurnoverPossession(r) && r.TurnoverOffSlot != null);
            BCommitterTo += records.Count(r => r.Offense == teamBSide && IsTurnoverPossession(r) && r.TurnoverOffSlot != null);

            for (var s = 0; s < 5; s++)
            {
                ASlotFga[s] += SumA(r => GetSlotFga(r, s + 1));
                BSlotFga[s] += SumB(r => GetSlotFga(r, s + 1));
            }

            for (var i = 0; i < 10; i++) PlayerTo[i] += attributed.To[i];
        }
    }

    // ── Readout: the two proofs ─────────────────────────────────────────────────

    private static void PrintAppliedDials(List<BenchAppliedDial> appliedA, List<BenchAppliedDial> appliedB)
    {
        Console.WriteLine("--- APPLIED DIALS (configuration proof: did the bench build exactly what was asked) ---");
        PrintTeamDials("Team A", appliedA);
        PrintTeamDials("Team B", appliedB);
        Console.WriteLine();
    }

    private static void PrintTeamDials(string label, List<BenchAppliedDial> applied)
    {
        if (applied.Count == 0)
        {
            Console.WriteLine($"{label} applied dials: (none — flat)");
            return;
        }
        Console.WriteLine($"{label} applied dials:");
        foreach (var slot in applied.Select(d => d.Slot).Distinct().OrderBy(s => s))
        {
            var parts = applied
                .Where(d => d.Slot == slot)
                .Select(d => d.Op == "set"
                    ? $"{d.Field} set {d.Value} ({d.Baseline} → {d.Final})"
                    : $"{d.Field} add {(d.Value >= 0 ? "+" : "")}{d.Value} ({d.Baseline} → {d.Final})");
            Console.WriteLine($"  Slot {slot}: {string.Join("; ", parts)}");
        }
    }

    private static void PrintBenchChannels(BenchStats s, Player[] teamAPlayers, Player[] teamBPlayers)
    {
        Console.WriteLine("--- CHANNEL BREAKDOWN (roster-shape + outcome proof) ---");
        Console.WriteLine($"Games: {s.Games}");
        Console.WriteLine();

        PrintTeamChannels("Team A", s, isA: true);
        PrintTeamChannels("Team B", s, isA: false);

        // Turnover reconciliation: per-player attribution counts only committer
        // turnovers (team violations like shot-clock / 5-second carry no individual
        // credit), so the check is player-sum == committer possessions, with team
        // violations reported alongside. A MISMATCH would mean the physical → logical
        // mapping inverted for some games.
        long aPlayerTo = 0, bPlayerTo = 0;
        for (var i = 0; i < 5; i++) aPlayerTo += s.PlayerTo[i];
        for (var i = 5; i < 10; i++) bPlayerTo += s.PlayerTo[i];
        bool aOk = aPlayerTo == s.ACommitterTo;
        bool bOk = bPlayerTo == s.BCommitterTo;
        Console.WriteLine("Turnover reconciliation (per-player attribution vs. committer possessions):");
        Console.WriteLine($"  Team A: players={aPlayerTo}  committer={s.ACommitterTo}  team-violations={s.ATurnovers - s.ACommitterTo}  [{(aOk ? "OK" : "MISMATCH")}]");
        Console.WriteLine($"  Team B: players={bPlayerTo}  committer={s.BCommitterTo}  team-violations={s.BTurnovers - s.BCommitterTo}  [{(bOk ? "OK" : "MISMATCH")}]");
        Console.WriteLine();

        PrintFingerprint("Team A fingerprint", ComputeFingerprint(teamAPlayers));
        PrintFingerprint("Team B fingerprint", ComputeFingerprint(teamBPlayers));
        Console.WriteLine();
    }

    private static void PrintTeamChannels(string label, BenchStats s, bool isA)
    {
        long offPoss = isA ? s.AOffPoss : s.BOffPoss;
        long points  = isA ? s.APoints  : s.BPoints;
        long fga  = isA ? s.AFga  : s.BFga;   long fgm  = isA ? s.AFgm  : s.BFgm;
        long rimA = isA ? s.ARimA : s.BRimA;  long rimM = isA ? s.ARimM : s.BRimM;
        long shA  = isA ? s.AShortA : s.BShortA; long shM = isA ? s.AShortM : s.BShortM;
        long midA = isA ? s.AMidA : s.BMidA;  long midM = isA ? s.AMidM : s.BMidM;
        long lgA  = isA ? s.ALongA : s.BLongA; long lgM = isA ? s.ALongM : s.BLongM;
        long tpa  = isA ? s.A3pa  : s.B3pa;   long tpm  = isA ? s.A3pm  : s.B3pm;
        long fta  = isA ? s.AFta  : s.BFta;   long ftm  = isA ? s.AFtm  : s.BFtm;
        long orbC = isA ? s.AOrbC : s.BOrbC;  long orbW = isA ? s.AOrbW : s.BOrbW;
        long trans = isA ? s.ATrans : s.BTrans;
        long turns = isA ? s.ATurnovers : s.BTurnovers;
        long[] slotFga = isA ? s.ASlotFga : s.BSlotFga;
        int wins = isA ? s.TeamAWins : s.TeamBWins;
        var scores = isA ? s.TeamAScores : s.TeamBScores;
        double avgMargin = s.Margins.Count > 0 ? s.Margins.Average() * (isA ? 1 : -1) : 0.0;

        double Pct(long m, long a) => a > 0 ? 100.0 * m / a : 0.0;
        double Rate(long n, long d) => d > 0 ? (double)n / d : 0.0;
        double winPct = s.Games > 0 ? 100.0 * wins / s.Games : 0.0;
        double avgScore = scores.Count > 0 ? scores.Average() : 0.0;

        Console.WriteLine($"{label}:");
        Console.WriteLine($"  Result:     win% {winPct:F1}   avgScore {avgScore:F1}   avgMargin {avgMargin:+0.0;-0.0}   PPP {Rate(points, offPoss):F3}");
        Console.WriteLine($"  Shooting:   FG% {Pct(fgm, fga):F1}   Rim {Pct(rimM, rimA):F1}   Short {Pct(shM, shA):F1}   Mid {Pct(midM, midA):F1}   Long {Pct(lgM, lgA):F1}   Three {Pct(tpm, tpa):F1}   FT% {Pct(ftm, fta):F1}");
        Console.WriteLine($"  Shot mix:   Rim {Pct(rimA, fga):F1}%   Short {Pct(shA, fga):F1}%   Mid {Pct(midA, fga):F1}%   Long {Pct(lgA, fga):F1}%   Three {Pct(tpa, fga):F1}%");
        Console.WriteLine($"  Glass:      ORB% {Pct(orbW, orbC):F1}   (won {orbW} of {orbC} chances)");
        Console.WriteLine($"  Turnovers:  TO rate {Rate(turns, offPoss):F3}   ({turns} in {offPoss} off. poss)");
        Console.WriteLine($"  Transition: freq {Rate(trans, offPoss):F3}   ({trans} of {offPoss})");
        Console.WriteLine($"  Free throw: FTA/FGA {Rate(fta, fga):F3}   (FTA {fta})");
        Console.WriteLine($"  Usage:      slot FGA   1:{slotFga[0]}   2:{slotFga[1]}   3:{slotFga[2]}   4:{slotFga[3]}   5:{slotFga[4]}");
        Console.WriteLine();
    }
}
