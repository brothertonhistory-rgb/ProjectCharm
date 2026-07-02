using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Charm.Harness;

internal static partial class Program
{
    // =====================================================================================
    // World Structure Pass 1 — the static skeleton (Session 28).
    //
    // The era-file schema (schools / conferences / tiers / two prestige values), the
    // csv -> stock-world converter, the count-agnostic pyramid seeder, the world-integrity
    // validator, and the distribution readout. Data + tooling + proof only: no seasons, no
    // dynamics, no engine change, no roster generation.
    //
    //   dotnet run -- world <file>                          validate + report
    //   dotnet run -- world report <file>                   same
    //   dotnet run -- world convert <teams.csv> <conf.csv> <out.json>
    //   dotnet run -- world seed <in.json> <seed> <out.json>
    //
    // Standing rules honored here (docs/world-structure-brief.md):
    //   - Authored files load AS WRITTEN. The seeder is tooling, never a load-time
    //     mutation; the report shows an authored file's deviation from the target
    //     pyramid, it never "corrects" it.
    //   - Count-agnostic: the pyramid is proportions; nothing assumes 347.
    //   - Floors are honored BY CONSTRUCTION of the seeder's assignment (greedy over
    //     ascending values), never by a post-hoc clamp that would distort band counts.
    //   - Generated worlds are reproducible: file + worldSeed reproduce every value.
    //   - Prestige scale is 0-99. The roster generator's floor is 1; any future pass
    //     that feeds a school's prestige to generation treats 0 as 1 at that boundary
    //     (recorded rule — nothing in Pass 1 exercises the seam).
    //   - Historical prestige = current prestige everywhere this pass (stock authored
    //     file and generated worlds alike).
    // =====================================================================================

    // ── The pyramid (brief §3b), top-down. Percents sum to 100. ────────────────────────
    private sealed record WorldBand(string Label, int Lo, int Hi, double Percent);

    private static readonly WorldBand[] WorldBands =
    {
        new("95+",   95, 99,  1.0),
        new("85-94", 85, 94,  6.0),
        new("75-84", 75, 84,  9.0),
        new("60-74", 60, 74, 14.0),
        new("40-59", 40, 59, 23.0),
        new("20-39", 20, 39, 21.0),
        new("<20",    0, 19, 26.0),
    };

    // ── Canonical tier table (placeholders; floor is the load-bearing value this pass;
    //    equilibrium + pullbackIntensity are carried schema for Pass 3, consumed there). ──
    private static readonly (string Id, int Floor, int Equilibrium, double Pullback)[] WorldTierDefaults =
    {
        ("power",   40, 75, 0.25),
        ("highMid", 20, 55, 0.45),
        ("lowMid",   8, 35, 0.65),
        ("low",      0, 18, 0.85),
    };

    private const double WorldStationJitter = 30.0;  // triangular +/-30 around tier equilibrium

    // ── Schema types ────────────────────────────────────────────────────────────────────
    private sealed record WorldTier(string Id, int Floor, int Equilibrium, double PullbackIntensity);
    private sealed record WorldConference(int Id, string Name, string ShortName, string TierId);
    private sealed record WorldSchool(
        int Id, string Name, string Abbr, string City, string State, string Color,
        double Lat, double Long, int ConferenceId, string Division,
        int CurrentPrestige, int HistoricalPrestige);

    private sealed class WorldFile
    {
        public int SchemaVersion { get; init; } = 1;
        public string Kind { get; init; } = "authored";      // "authored" | "generated"
        public string EraLabel { get; init; } = "";
        public string Division { get; init; } = "D1";
        public long? WorldSeed { get; init; }                 // present iff generated
        public List<WorldTier> Tiers { get; init; } = new();
        public List<WorldConference> Conferences { get; init; } = new();
        public List<WorldSchool> Schools { get; init; } = new();
    }

    // ── Deterministic PRNG (SplitMix64) — explicit so a world file is reproducible on
    //    any runtime, never dependent on System.Random's version-specific algorithm.
    //    Mirrors the Python oracle bit-for-bit. ───────────────────────────────────────────
    private sealed class WorldRng
    {
        private ulong _state;
        public WorldRng(long seed) => _state = unchecked((ulong)seed);

        public ulong NextU64()
        {
            _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
            var z = _state;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        // [0,1), 53-bit — same derivation as the oracle.
        public double NextDouble() => (NextU64() >> 11) * (1.0 / (1L << 53));
    }

    // =====================================================================================
    // CLI dispatch
    // =====================================================================================
    private static int RunWorld(string[] args)
    {
        try
        {
            if (args.Length == 2)                          // world <file>
            {
                ReportWorld(LoadWorld(args[1]), args[1]);
                return 0;
            }
            switch (args.Length > 1 ? args[1] : "")
            {
                case "report" when args.Length == 3:
                    ReportWorld(LoadWorld(args[2]), args[2]);
                    return 0;
                case "convert" when args.Length == 5:
                {
                    var world = ConvertWorld(args[2], args[3]);
                    ValidateWorld(world);
                    WriteWorld(world, args[4]);
                    Console.WriteLine($"converted {world.Schools.Count} schools / {world.Conferences.Count} conferences -> {args[4]}");
                    ReportWorld(world, args[4]);
                    return 0;
                }
                case "seed" when args.Length == 5:
                {
                    var input = LoadWorld(args[2]);
                    var seed = long.Parse(args[3], CultureInfo.InvariantCulture);
                    var world = SeedWorld(input, seed);
                    ValidateWorld(world);
                    WriteWorld(world, args[4]);
                    Console.WriteLine($"seeded {world.Schools.Count} schools (worldSeed {seed}) -> {args[4]}");
                    ReportWorld(world, args[4]);
                    return 0;
                }
                default:
                    Console.WriteLine("usage: world <file> | world report <file> | " +
                                      "world convert <teams.csv> <conf.csv> <out.json> | " +
                                      "world seed <in.json> <seed> <out.json>");
                    return 1;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        {
            Console.WriteLine($"WORLD ERROR: {ex.Message}");
            return 1;
        }
    }

    // =====================================================================================
    // Strict reader (the bench-reader standard: walk the tree, refuse anything unknown,
    // name the exact school/conference/field in every failure)
    // =====================================================================================
    private static WorldFile LoadWorld(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"world file not found: {path}");
        var world = ParseWorld(File.ReadAllText(path));
        ValidateWorld(world);
        return world;
    }

    private static WorldFile ParseWorld(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException jx)
        {
            throw new InvalidOperationException($"world file is not valid JSON — {jx.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("world file root must be a JSON object.");
            RejectUnknownOrDuplicateKeys(root, "root",
                "schemaVersion", "metadata", "tiers", "conferences", "schools");

            var schemaVersion = RequireIntProperty(root, "schemaVersion", "root");

            if (!root.TryGetProperty("metadata", out var meta) || meta.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("'metadata' object is required at root.");
            RejectUnknownOrDuplicateKeys(meta, "metadata", "kind", "eraLabel", "division", "worldSeed");
            var kind = WorldRequireString(meta, "kind", "metadata");
            var eraLabel = WorldRequireString(meta, "eraLabel", "metadata");
            var division = WorldRequireString(meta, "division", "metadata");
            long? worldSeed = null;
            if (meta.TryGetProperty("worldSeed", out var seedEl))
            {
                if (seedEl.ValueKind != JsonValueKind.Number || !seedEl.TryGetInt64(out var s))
                    throw new InvalidOperationException("metadata.worldSeed must be an integer.");
                worldSeed = s;
            }

            var tiers = new List<WorldTier>();
            foreach (var el in WorldRequireArray(root, "tiers"))
            {
                RejectUnknownOrDuplicateKeys(el, "tiers[]", "id", "floor", "equilibrium", "pullbackIntensity");
                tiers.Add(new WorldTier(
                    WorldRequireString(el, "id", "tiers[]"),
                    RequireIntProperty(el, "floor", "tiers[]"),
                    RequireIntProperty(el, "equilibrium", "tiers[]"),
                    WorldRequireDouble(el, "pullbackIntensity", "tiers[]")));
            }

            var conferences = new List<WorldConference>();
            foreach (var el in WorldRequireArray(root, "conferences"))
            {
                RejectUnknownOrDuplicateKeys(el, "conferences[]", "id", "name", "shortName", "tierId");
                conferences.Add(new WorldConference(
                    RequireIntProperty(el, "id", "conferences[]"),
                    WorldRequireString(el, "name", "conferences[]"),
                    WorldRequireString(el, "shortName", "conferences[]"),
                    WorldRequireString(el, "tierId", "conferences[]")));
            }

            var schools = new List<WorldSchool>();
            foreach (var el in WorldRequireArray(root, "schools"))
            {
                RejectUnknownOrDuplicateKeys(el, "schools[]",
                    "id", "name", "abbr", "city", "state", "color", "lat", "long",
                    "conferenceId", "division", "currentPrestige", "historicalPrestige");
                var name = WorldRequireString(el, "name", "schools[]");
                var ctx = $"school '{name}'";
                schools.Add(new WorldSchool(
                    RequireIntProperty(el, "id", ctx),
                    name,
                    WorldRequireString(el, "abbr", ctx),
                    WorldRequireString(el, "city", ctx),
                    WorldRequireString(el, "state", ctx),
                    WorldRequireString(el, "color", ctx),
                    WorldRequireDouble(el, "lat", ctx),
                    WorldRequireDouble(el, "long", ctx),
                    RequireIntProperty(el, "conferenceId", ctx),
                    WorldRequireString(el, "division", ctx),
                    RequireIntProperty(el, "currentPrestige", ctx),
                    RequireIntProperty(el, "historicalPrestige", ctx)));
            }

            return new WorldFile
            {
                SchemaVersion = schemaVersion, Kind = kind, EraLabel = eraLabel,
                Division = division, WorldSeed = worldSeed,
                Tiers = tiers, Conferences = conferences, Schools = schools,
            };
        }
    }

    private static IEnumerable<JsonElement> WorldRequireArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"'{name}' array is required at root.");
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"every element of '{name}' must be an object.");
            yield return el;
        }
    }

    private static string WorldRequireString(JsonElement obj, string name, string ctx)
    {
        if (!obj.TryGetProperty(name, out var el))
            throw new InvalidOperationException($"missing required '{name}' in {ctx}.");
        if (el.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"'{name}' in {ctx} must be a string (got {el.GetRawText()}).");
        return el.GetString() ?? "";
    }

    private static double WorldRequireDouble(JsonElement obj, string name, string ctx)
    {
        if (!obj.TryGetProperty(name, out var el))
            throw new InvalidOperationException($"missing required '{name}' in {ctx}.");
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out var v))
            throw new InvalidOperationException($"'{name}' in {ctx} must be a number (got {el.GetRawText()}).");
        return v;
    }

    // =====================================================================================
    // Validator — runs on every load, every command. Loud, specific, before any output.
    // =====================================================================================
    private static void ValidateWorld(WorldFile w)
    {
        if (w.SchemaVersion != 1)
            throw new InvalidOperationException($"unsupported schemaVersion {w.SchemaVersion} (this build reads 1).");
        if (w.Kind != "authored" && w.Kind != "generated")
            throw new InvalidOperationException($"metadata.kind must be 'authored' or 'generated' (got '{w.Kind}').");
        if (w.Kind == "generated" && w.WorldSeed is null)
            throw new InvalidOperationException("a generated world must carry metadata.worldSeed (brief rule 7).");
        if (w.Kind == "authored" && w.WorldSeed is not null)
            throw new InvalidOperationException("an authored world must not carry metadata.worldSeed.");

        // Tiers: exactly the four canonical ids, each once, sane numbers.
        var canonical = WorldTierDefaults.Select(t => t.Id).ToArray();
        var tierIds = w.Tiers.Select(t => t.Id).ToList();
        if (tierIds.Count != canonical.Length || tierIds.Distinct(StringComparer.Ordinal).Count() != tierIds.Count
            || canonical.Any(c => !tierIds.Contains(c, StringComparer.Ordinal)))
            throw new InvalidOperationException(
                $"tiers must be exactly [{string.Join(", ", canonical)}], each once (got [{string.Join(", ", tierIds)}]).");
        var tierById = new Dictionary<string, WorldTier>(StringComparer.Ordinal);
        foreach (var t in w.Tiers)
        {
            if (t.Floor is < 0 or > 99)
                throw new InvalidOperationException($"tier '{t.Id}' floor {t.Floor} out of 0-99.");
            if (t.Equilibrium is < 0 or > 99)
                throw new InvalidOperationException($"tier '{t.Id}' equilibrium {t.Equilibrium} out of 0-99.");
            if (t.Floor > t.Equilibrium)
                throw new InvalidOperationException($"tier '{t.Id}' floor {t.Floor} exceeds its equilibrium {t.Equilibrium}.");
            if (t.PullbackIntensity <= 0.0 || t.PullbackIntensity > 1.0)
                throw new InvalidOperationException($"tier '{t.Id}' pullbackIntensity {t.PullbackIntensity} must be in (0, 1].");
            tierById[t.Id] = t;
        }

        // Conferences: unique ids, real tiers.
        if (w.Conferences.Count == 0)
            throw new InvalidOperationException("world has no conferences.");
        var confById = new Dictionary<int, WorldConference>();
        foreach (var c in w.Conferences)
        {
            if (!confById.TryAdd(c.Id, c))
                throw new InvalidOperationException($"duplicate conference id {c.Id} ('{c.Name}').");
            if (!tierById.ContainsKey(c.TierId))
                throw new InvalidOperationException($"conference '{c.Name}' points at unknown tier '{c.TierId}'.");
            if (c.Name.Length == 0 || c.ShortName.Length == 0)
                throw new InvalidOperationException($"conference id {c.Id} has an empty name or shortName.");
        }

        // Schools: unique ids, real conferences, division matches, values in bounds,
        // and the tier-membership floor check (holds for authored AND generated files:
        // membership guarantees a minimum — a file that starts below it is incoherent).
        if (w.Schools.Count == 0)
            throw new InvalidOperationException("world has no schools.");
        var schoolIds = new HashSet<int>();
        foreach (var s in w.Schools)
        {
            if (!schoolIds.Add(s.Id))
                throw new InvalidOperationException($"duplicate school id {s.Id} ('{s.Name}').");
            if (!confById.TryGetValue(s.ConferenceId, out var conf))
                throw new InvalidOperationException($"school '{s.Name}' points at unknown conference id {s.ConferenceId}.");
            if (!string.Equals(s.Division, w.Division, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"school '{s.Name}' division '{s.Division}' does not match metadata.division '{w.Division}'.");
            if (s.CurrentPrestige is < 0 or > 99)
                throw new InvalidOperationException($"school '{s.Name}' currentPrestige {s.CurrentPrestige} out of 0-99.");
            if (s.HistoricalPrestige is < 0 or > 99)
                throw new InvalidOperationException($"school '{s.Name}' historicalPrestige {s.HistoricalPrestige} out of 0-99.");
            var floor = tierById[conf.TierId].Floor;
            if (s.CurrentPrestige < floor)
                throw new InvalidOperationException(
                    $"school '{s.Name}' currentPrestige {s.CurrentPrestige} is below conference '{conf.Name}' " +
                    $"({conf.TierId}) floor {floor}.");
        }

        WorldFeasibilityCheck(w, tierById, confById);
    }

    // The special check (brief rule 6): given member counts and floors, the target
    // pyramid and the floors must be simultaneously satisfiable. Cumulative, per
    // distinct floor descending: schools whose floor forces them to/above F must not
    // exceed the pyramid slots in bands whose upper bound reaches F. Canonical
    // (seed-independent) apportionment — the seeder's own greedy is the final guard
    // for the seeded-tie-break margin.
    private static void WorldFeasibilityCheck(
        WorldFile w, Dictionary<string, WorldTier> tierById, Dictionary<int, WorldConference> confById)
    {
        var counts = WorldApportion(w.Schools.Count, null);
        var floors = w.Schools.Select(s => tierById[confById[s.ConferenceId].TierId].Floor).ToList();
        foreach (var f in floors.Where(f => f > 0).Distinct().OrderByDescending(f => f))
        {
            var required = floors.Count(x => x >= f);
            var available = WorldBands.Select((b, i) => b.Hi >= f ? counts[i] : 0).Sum();
            if (required > available)
                throw new InvalidOperationException(
                    $"infeasible world: floors and the target pyramid cannot both hold — " +
                    $"{required} schools' conference floors force them to prestige {f}+, but the pyramid " +
                    $"provides only {available} slots at {f}+ for n={w.Schools.Count}.");
        }
    }

    // =====================================================================================
    // Largest-remainder apportionment over the band proportions. Deterministic; ties on
    // the fractional remainder break by seeded permutation when rng is given (seeder),
    // by canonical top-down band order when rng is null (validator).
    // =====================================================================================
    private static int[] WorldApportion(int n, WorldRng? rng)
    {
        var quotas = WorldBands.Select(b => n * b.Percent / 100.0).ToArray();
        var counts = quotas.Select(q => (int)q).ToArray();
        var leftover = n - counts.Sum();
        var tieKey = new double[WorldBands.Length];
        for (var i = 0; i < tieKey.Length; i++)
            tieKey[i] = rng?.NextDouble() ?? i * 1e-9;
        var order = Enumerable.Range(0, WorldBands.Length)
            .OrderByDescending(i => quotas[i] - counts[i])
            .ThenBy(i => tieKey[i])
            .ToArray();
        for (var k = 0; k < leftover; k++)
            counts[order[k]] += 1;
        return counts;
    }

    // =====================================================================================
    // The pyramid seeder. Generates a NEW world from an existing world's structure
    // (schools / conferences / tiers — incoming prestige ignored entirely): same
    // skeleton, currentPrestige reseeded to the target pyramid, historicalPrestige set
    // equal to it, kind "generated", the seed stamped. Never mutates its input; never
    // invoked at load time.
    //
    // RNG consumption order (fixed — the reproducibility contract, mirrored by the
    // oracle): 7 tie-break doubles, then two jitter doubles per school in ascending
    // school-id order, then one double per band slot, bands top-down.
    //
    // Assignment: station = tier equilibrium + triangular jitter (+/-30) — roughly
    // tier-ordered with real overlap. Values sorted ascending; each value goes to the
    // unassigned school with the LOWEST station whose conference floor permits it.
    // Floors are honored by this construction; band counts stay exact; a value no
    // school can take means the validator missed an infeasibility — fail loudly.
    // =====================================================================================
    private static WorldFile SeedWorld(WorldFile input, long worldSeed)
    {
        var rng = new WorldRng(worldSeed);
        var counts = WorldApportion(input.Schools.Count, rng);

        var tierById = input.Tiers.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var confById = input.Conferences.ToDictionary(c => c.Id);

        var station = new Dictionary<int, double>();
        foreach (var s in input.Schools.OrderBy(s => s.Id))
        {
            var eq = tierById[confById[s.ConferenceId].TierId].Equilibrium;
            station[s.Id] = eq + (rng.NextDouble() + rng.NextDouble() - 1.0) * WorldStationJitter;
        }

        var values = new List<int>(input.Schools.Count);
        for (var i = 0; i < WorldBands.Length; i++)
            for (var k = 0; k < counts[i]; k++)
                values.Add(WorldBands[i].Lo + (int)(rng.NextDouble() * (WorldBands[i].Hi - WorldBands[i].Lo + 1)));
        values.Sort();

        var remaining = input.Schools.OrderBy(s => station[s.Id]).ThenBy(s => s.Id).ToList();
        var assigned = new Dictionary<int, int>();
        foreach (var v in values)
        {
            var idx = remaining.FindIndex(s => tierById[confById[s.ConferenceId].TierId].Floor <= v);
            if (idx < 0)
                throw new InvalidOperationException(
                    $"seeder could not place pyramid value {v}: every unassigned school's conference floor " +
                    "exceeds it (an infeasibility the validator should have named — validator gap).");
            assigned[remaining[idx].Id] = v;
            remaining.RemoveAt(idx);
        }

        return new WorldFile
        {
            SchemaVersion = input.SchemaVersion,
            Kind = "generated",
            EraLabel = input.EraLabel + "-seeded",
            Division = input.Division,
            WorldSeed = worldSeed,
            Tiers = input.Tiers.ToList(),
            Conferences = input.Conferences.ToList(),
            Schools = input.Schools
                .Select(s => s with { CurrentPrestige = assigned[s.Id], HistoricalPrestige = assigned[s.Id] })
                .ToList(),
        };
    }

    // =====================================================================================
    // Converter: the two reference csvs -> a stock authored world. Deterministic (same
    // inputs -> byte-identical output via the fixed-order writer). Tier mapping from
    // conf.csv's 1-5 conference rating: 5 -> power, 4 -> highMid, 3 -> lowMid,
    // 1/2 -> low. teams.csv's 'Division' column is the intra-conference East/West
    // split, NOT the NCAA division — dropped; every school is stamped division "D1".
    // Historical prestige = current (the Pass 1 rule).
    // =====================================================================================
    private static WorldFile ConvertWorld(string teamsCsvPath, string confCsvPath)
    {
        var confRows = ReadWorldCsv(confCsvPath,
            new[] { "ID", "Name", "ShortName", "Games", "Prestige", "Divisions", "DivisionOne", "DivisionTwo",
                    "TourneyTeams", "TDay1", "TDay2", "TDay3", "TDay4", "TDay5", "SeedDiv", "D1", "D2", "D3",
                    "TourneyBirth", "TourneyType" });
        var teamRows = ReadWorldCsv(teamsCsvPath,
            new[] { "ID", "Name", "Mascot", "Prestige", "Abbr", "Logo", "City", "State", "Conference",
                    "Division", "TeamColor", "TravelPart", "Lat", "Long", "Academics" });

        var conferences = new List<WorldConference>();
        var confIds = new HashSet<int>();
        foreach (var r in confRows)
        {
            var id = WorldCsvInt(r[0], confCsvPath, "conference ID");
            if (!confIds.Add(id))
                throw new InvalidOperationException($"{confCsvPath}: duplicate conference id {id}.");
            var rating = WorldCsvInt(r[4], confCsvPath, $"conference '{r[1]}' Prestige");
            var tierId = rating switch
            {
                5 => "power", 4 => "highMid", 3 => "lowMid", 1 or 2 => "low",
                _ => throw new InvalidOperationException(
                    $"{confCsvPath}: conference '{r[1]}' rating {rating} outside the known 1-5 scale."),
            };
            conferences.Add(new WorldConference(id, r[1], r[2], tierId));
        }

        var schools = new List<WorldSchool>();
        var schoolIds = new HashSet<int>();
        foreach (var r in teamRows)
        {
            var id = WorldCsvInt(r[0], teamsCsvPath, "school ID");
            if (!schoolIds.Add(id))
                throw new InvalidOperationException($"{teamsCsvPath}: duplicate school id {id}.");
            var confId = WorldCsvInt(r[8], teamsCsvPath, $"school '{r[1]}' Conference");
            if (!confIds.Contains(confId))
                throw new InvalidOperationException(
                    $"{teamsCsvPath}: school '{r[1]}' references conference id {confId}, which is not in {confCsvPath}.");
            var prestige = WorldCsvInt(r[3], teamsCsvPath, $"school '{r[1]}' Prestige");
            if (prestige is < 0 or > 99)
                throw new InvalidOperationException($"{teamsCsvPath}: school '{r[1]}' prestige {prestige} out of 0-99.");
            schools.Add(new WorldSchool(
                Id: id, Name: r[1], Abbr: r[4], City: r[6], State: r[7], Color: r[10],
                Lat: WorldCsvDouble(r[12], teamsCsvPath, $"school '{r[1]}' Lat"),
                Long: WorldCsvDouble(r[13], teamsCsvPath, $"school '{r[1]}' Long"),
                ConferenceId: confId, Division: "D1",
                CurrentPrestige: prestige, HistoricalPrestige: prestige));
        }

        return new WorldFile
        {
            SchemaVersion = 1,
            Kind = "authored",
            EraLabel = "stock-d1",
            Division = "D1",
            WorldSeed = null,
            Tiers = WorldTierDefaults.Select(t => new WorldTier(t.Id, t.Floor, t.Equilibrium, t.Pullback)).ToList(),
            Conferences = conferences.OrderBy(c => c.Id).ToList(),
            Schools = schools.OrderBy(s => s.Id).ToList(),
        };
    }

    private static int WorldCsvInt(string raw, string file, string what)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            throw new InvalidOperationException($"{file}: {what} must be an integer (got '{raw}').");
        return v;
    }

    private static double WorldCsvDouble(string raw, string file, string what)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new InvalidOperationException($"{file}: {what} must be a number (got '{raw}').");
        return v;
    }

    // Quote-aware csv reader (never string.Split(',')): handles quoted fields with
    // embedded commas and doubled quotes, CRLF/LF, the reference files' leading count
    // line, and trims every decoded field. Fails loudly on malformed quoting, wrong
    // column count, a count line that disagrees with the rows read, or a header that
    // is not exactly the expected one. Returns data rows only.
    private static List<string[]> ReadWorldCsv(string path, string[] expectedHeader)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"csv not found: {path}");
        var lines = SplitWorldCsvRecords(File.ReadAllText(path), path);
        if (lines.Count < 2)
            throw new InvalidOperationException($"{path}: expected a count line, a header, and data rows.");

        // Line 1: the count line (a single bare integer).
        var countCells = ParseWorldCsvRecord(lines[0], path, 1);
        if (countCells.Length != 1 || !int.TryParse(countCells[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var declared))
            throw new InvalidOperationException($"{path}: line 1 must be the bare row-count line (got '{lines[0]}').");

        // Line 2: the header, exactly as expected.
        var header = ParseWorldCsvRecord(lines[1], path, 2);
        if (header.Length != expectedHeader.Length ||
            header.Where((h, i) => !string.Equals(h, expectedHeader[i], StringComparison.Ordinal)).Any())
            throw new InvalidOperationException(
                $"{path}: header mismatch — expected [{string.Join(",", expectedHeader)}], got [{string.Join(",", header)}].");

        var rows = new List<string[]>();
        for (var i = 2; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) continue;   // tolerate a trailing blank line only
            var cells = ParseWorldCsvRecord(lines[i], path, i + 1);
            if (cells.Length != expectedHeader.Length)
                throw new InvalidOperationException(
                    $"{path}: line {i + 1} has {cells.Length} fields, expected {expectedHeader.Length}.");
            rows.Add(cells);
        }
        if (rows.Count != declared)
            throw new InvalidOperationException(
                $"{path}: count line declares {declared} rows but {rows.Count} data rows were read.");
        return rows;
    }

    // Records split on newlines OUTSIDE quotes (a quoted field may contain a newline).
    private static List<string> SplitWorldCsvRecords(string text, string path)
    {
        var records = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"') { inQuotes = !inQuotes; sb.Append(ch); }
            else if (!inQuotes && (ch == '\n' || ch == '\r'))
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                records.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(ch);
        }
        if (inQuotes)
            throw new InvalidOperationException($"{path}: unterminated quoted field at end of file.");
        if (sb.Length > 0) records.Add(sb.ToString());
        return records;
    }

    private static string[] ParseWorldCsvRecord(string line, string path, int lineNo)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"')
            {
                if (sb.Length > 0)
                    throw new InvalidOperationException($"{path}: line {lineNo} has a quote inside an unquoted field.");
                inQuotes = true;
            }
            else if (ch == ',') { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        if (inQuotes)
            throw new InvalidOperationException($"{path}: line {lineNo} has an unterminated quoted field.");
        cells.Add(sb.ToString().Trim());
        return cells.ToArray();
    }

    // =====================================================================================
    // Writer — deterministic on purpose: canonical tier order, conferences and schools
    // by id, fixed property order, "\n" newlines, 2-space indent, invariant numeric
    // formatting (Utf8JsonWriter is culture-invariant; doubles round-trip shortest).
    // =====================================================================================
    private static void WriteWorld(WorldFile w, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", w.SchemaVersion);

            writer.WriteStartObject("metadata");
            writer.WriteString("kind", w.Kind);
            writer.WriteString("eraLabel", w.EraLabel);
            writer.WriteString("division", w.Division);
            if (w.WorldSeed is not null) writer.WriteNumber("worldSeed", w.WorldSeed.Value);
            writer.WriteEndObject();

            writer.WriteStartArray("tiers");
            foreach (var id in WorldTierDefaults.Select(t => t.Id))
            {
                var t = w.Tiers.Single(x => x.Id == id);
                writer.WriteStartObject();
                writer.WriteString("id", t.Id);
                writer.WriteNumber("floor", t.Floor);
                writer.WriteNumber("equilibrium", t.Equilibrium);
                writer.WriteNumber("pullbackIntensity", t.PullbackIntensity);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("conferences");
            foreach (var c in w.Conferences.OrderBy(c => c.Id))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", c.Id);
                writer.WriteString("name", c.Name);
                writer.WriteString("shortName", c.ShortName);
                writer.WriteString("tierId", c.TierId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("schools");
            foreach (var s in w.Schools.OrderBy(s => s.Id))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", s.Id);
                writer.WriteString("name", s.Name);
                writer.WriteString("abbr", s.Abbr);
                writer.WriteString("city", s.City);
                writer.WriteString("state", s.State);
                writer.WriteString("color", s.Color);
                writer.WriteNumber("lat", s.Lat);
                writer.WriteNumber("long", s.Long);
                writer.WriteNumber("conferenceId", s.ConferenceId);
                writer.WriteString("division", s.Division);
                writer.WriteNumber("currentPrestige", s.CurrentPrestige);
                writer.WriteNumber("historicalPrestige", s.HistoricalPrestige);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        File.WriteAllBytes(path, stream.ToArray());
    }

    // =====================================================================================
    // The distribution readout — the pyramid on the page. A report of deviation for
    // authored files, never a correction.
    // =====================================================================================
    private static void ReportWorld(WorldFile w, string sourceLabel)
    {
        var inv = CultureInfo.InvariantCulture;
        var tierById = w.Tiers.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var confById = w.Conferences.ToDictionary(c => c.Id);
        var n = w.Schools.Count;

        Console.WriteLine();
        Console.WriteLine($"=== WORLD REPORT: {sourceLabel} ===");
        Console.WriteLine(
            $"kind {w.Kind} | era {w.EraLabel} | division {w.Division} | schools {n} | " +
            $"conferences {w.Conferences.Count}" +
            (w.WorldSeed is not null ? $" | worldSeed {w.WorldSeed.Value}" : ""));

        Console.WriteLine();
        Console.WriteLine("BAND HISTOGRAM (actual vs target pyramid)");
        Console.WriteLine($"  {"Band",-7} {"Count",5} {"Actual%",8} {"Target%",8} {"Dev",7}");
        for (var i = 0; i < WorldBands.Length; i++)
        {
            var b = WorldBands[i];
            var count = w.Schools.Count(s => s.CurrentPrestige >= b.Lo && s.CurrentPrestige <= b.Hi);
            var actual = 100.0 * count / n;
            Console.WriteLine(string.Format(inv, "  {0,-7} {1,5} {2,7:F1}% {3,7:F1}% {4,7:+0.0;-0.0;+0.0}",
                b.Label, count, actual, b.Percent, actual - b.Percent));
        }

        Console.WriteLine();
        Console.WriteLine("TIER ROLLUP");
        Console.WriteLine($"  {"Tier",-8} {"Confs",5} {"Schools",7} {"Floor",5} {"Min",4} {"Med",6} {"Mean",6} {"Max",4}");
        foreach (var (id, _, _, _) in WorldTierDefaults)
        {
            var confIds = w.Conferences.Where(c => c.TierId == id).Select(c => c.Id).ToHashSet();
            var vals = w.Schools.Where(s => confIds.Contains(s.ConferenceId)).Select(s => s.CurrentPrestige).ToList();
            if (vals.Count == 0) continue;
            Console.WriteLine(string.Format(inv, "  {0,-8} {1,5} {2,7} {3,5} {4,4} {5,6:F1} {6,6:F1} {7,4}",
                id, confIds.Count, vals.Count, tierById[id].Floor,
                vals.Min(), WorldMedian(vals), vals.Average(), vals.Max()));
        }

        Console.WriteLine();
        Console.WriteLine("CONFERENCES");
        Console.WriteLine($"  {"Conference",-36} {"Tier",-8} {"n",3} {"Min",4} {"Med",6} {"Mean",6} {"Max",4}");
        foreach (var c in w.Conferences.OrderBy(c => c.Id))
        {
            var vals = w.Schools.Where(s => s.ConferenceId == c.Id).Select(s => s.CurrentPrestige).ToList();
            if (vals.Count == 0) continue;
            Console.WriteLine(string.Format(inv, "  {0,-36} {1,-8} {2,3} {3,4} {4,6:F1} {5,6:F1} {6,4}",
                c.Name.Length > 36 ? c.Name[..36] : c.Name, c.TierId, vals.Count,
                vals.Min(), WorldMedian(vals), vals.Average(), vals.Max()));
        }
        Console.WriteLine();
    }

    // Median: middle value for odd n, mean of the two middle sorted values for even n.
    private static double WorldMedian(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
