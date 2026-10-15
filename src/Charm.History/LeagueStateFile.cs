using System.Globalization;
using System.Text.Json;

namespace Charm.History;

// ============================================================================
//  S125 — THE LEAGUE STATE (save design B1b, brief §6; K5).
//
//  The world file is the starting universe and is never rewritten. The league
//  state is the universe NOW: one file per season boundary,
//  `<career>.league/league-N.json`, listed in the career file by hash. `league-0`
//  is written when the career is born; `league-N` at season N's close.
//
//  ★ ITS ONE FACT IS PRESTIGE BY SCHOOL, copied from the world. Nothing plays from
//  it until phase C. Until then the harness asserts at every open and every close
//  that it equals the world's prestige for every school, so the two copies cannot
//  disagree. Nothing more generic than prestige is built here.
//
//  ★ This assembly references nothing, so it cannot see the world: the harness hands
//  the pairs in. The bytes are made here, once, by one canonical writer, and a file
//  read back must equal its own re-encoding — so two spellings of one league state
//  cannot both exist.
// ============================================================================

/// <summary>One school's prestige in a league state.</summary>
public readonly record struct SchoolPrestige(int SchoolId, int Value);

/// <summary>A league state as read back: its lineage, the season boundary it records, and
/// prestige by school in school-id order.</summary>
public sealed record LeagueState(string HistoryId, long Season, IReadOnlyList<SchoolPrestige> Prestige);

public static class LeagueStateFile
{
    public const string FormatTag = "charm-league";
    public const int SchemaVersion = 1;

    private static readonly string[] RootKeys = { "format", "schemaVersion", "historyId", "season", "prestige" };

    /// <summary>The name-free path the career file lists.</summary>
    public static string ListedPathFor(long season)
        => "league/league-" + season.ToString(CultureInfo.InvariantCulture) + ".json";

    /// <summary>The folder a career's league states live in — named for the career FILE, as the
    /// event-record and log folders are.</summary>
    public static string FolderFor(string historyPath) => HistoryStore.SiblingFolderFor(historyPath, "league");

    public static string PathFor(string historyPath, long season)
        => Path.Combine(FolderFor(historyPath),
                        "league-" + season.ToString(CultureInfo.InvariantCulture) + ".json");

    /// <summary>The canonical bytes. Pairs are sorted by school id here; a school named twice is
    /// refused.</summary>
    public static byte[] Encode(string historyId, long season, IEnumerable<SchoolPrestige> prestige)
    {
        if (!HistorySchemaV4.IsCanonicalHistoryId(historyId))
            throw new HistoryException(HistoryError.LeagueStateInvalid,
                "a league state needs the career's 32-hex lineage label.");
        if (season < 0)
            throw new HistoryException(HistoryError.LeagueStateInvalid, "a league state's season cannot be negative.");
        var ordered = prestige.OrderBy(p => p.SchoolId).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].SchoolId == ordered[i - 1].SchoolId)
                throw new HistoryException(HistoryError.LeagueStateInvalid,
                    $"school {ordered[i].SchoolId.ToString(CultureInfo.InvariantCulture)} appears twice in a league state.");

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteString("format", FormatTag);
            w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("historyId", historyId);
            w.WriteNumber("season", season);
            w.WriteStartArray("prestige");
            foreach (var p in ordered)
            {
                w.WriteStartObject();
                w.WriteNumber("schoolId", p.SchoolId);
                w.WriteNumber("value", p.Value);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var body = stream.ToArray();
        var all = new byte[body.Length + 1];
        Array.Copy(body, all, body.Length);
        all[body.Length] = (byte)'\n';
        return all;
    }

    /// <summary>Read and validate a league state: the format, this career's lineage, the season it
    /// claims, and that the bytes are exactly their own canonical re-encoding. Any failure is
    /// <see cref="HistoryError.LeagueStateInvalid"/>, named.</summary>
    public static LeagueState Read(string path, string expectedHistoryId, long expectedSeason)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(HistoryError.LeagueStateInvalid,
                $"could not read the league state '{path}' — {ex.Message}", ex);
        }
        LeagueState state;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new HistoryException(HistoryError.LeagueStateInvalid, "the league state's root is not an object.");
            HistorySchemaV4.RejectUnknownOrDuplicateKeys(root, RootKeys, "a league state");
            if (HistorySchemaV4.RequireString(root, "format") != FormatTag
                || HistorySchemaV4.RequireLong(root, "schemaVersion") != SchemaVersion)
                throw new HistoryException(HistoryError.LeagueStateInvalid,
                    $"'{path}' is not a version-{SchemaVersion} league state.");
            var id = HistorySchemaV4.RequireString(root, "historyId");
            var season = HistorySchemaV4.RequireLong(root, "season");
            var arr = root.GetProperty("prestige");
            if (arr.ValueKind != JsonValueKind.Array)
                throw new HistoryException(HistoryError.LeagueStateInvalid, "the league state's 'prestige' is not an array.");
            var list = new List<SchoolPrestige>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new HistoryException(HistoryError.LeagueStateInvalid, "a prestige entry is not an object.");
                HistorySchemaV4.RejectUnknownOrDuplicateKeys(item, new[] { "schoolId", "value" }, "a prestige entry");
                var sid = HistorySchemaV4.RequireLong(item, "schoolId");
                var v = HistorySchemaV4.RequireLong(item, "value");
                if (sid is < int.MinValue or > int.MaxValue || v is < int.MinValue or > int.MaxValue)
                    throw new HistoryException(HistoryError.LeagueStateInvalid, "a prestige entry is out of range.");
                list.Add(new SchoolPrestige((int)sid, (int)v));
            }
            state = new LeagueState(id, season, list);
        }
        catch (JsonException jx)
        {
            throw new HistoryException(HistoryError.LeagueStateInvalid, $"'{path}' is not valid JSON — {jx.Message}", jx);
        }
        catch (HistoryException hx) when (hx.Error != HistoryError.LeagueStateInvalid)
        {
            throw new HistoryException(HistoryError.LeagueStateInvalid, $"'{path}': {hx.Message}", hx);
        }

        if (!string.Equals(state.HistoryId, expectedHistoryId, StringComparison.Ordinal))
            throw new HistoryException(HistoryError.LeagueStateInvalid,
                $"'{path}' belongs to a different career (lineage {state.HistoryId}, this career is {expectedHistoryId}).");
        if (state.Season != expectedSeason)
            throw new HistoryException(HistoryError.LeagueStateInvalid,
                $"'{path}' says it is season {state.Season.ToString(CultureInfo.InvariantCulture)}'s league state, " +
                $"not season {expectedSeason.ToString(CultureInfo.InvariantCulture)}'s.");
        if (!bytes.AsSpan().SequenceEqual(Encode(state.HistoryId, state.Season, state.Prestige)))
            throw new HistoryException(HistoryError.LeagueStateInvalid,
                $"'{path}' is not in the canonical form the game writes.");
        return state;
    }
}
