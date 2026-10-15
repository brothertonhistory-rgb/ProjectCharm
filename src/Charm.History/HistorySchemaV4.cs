using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Charm.History;

// ============================================================================
//  S125 — THE CAREER FILE, version 4: v3 plus the list of every file the career
//  owns (each with its SHA-256), each season's rules, a reserved `progress` slot,
//  and a fingerprint of its own (save design B1b; brief §2a, §4a).
//
//  ★ THE TRAILER. After the JSON's final newline comes one line,
//        sha256:<64 lowercase hex>\n
//  taken over the exact bytes before it. A file whose trailer is missing or does
//  not match is TORN — half-written, or edited by hand — and is never half-read.
//  The trailer proves the bytes are what the writer wrote; it does not prove they
//  make sense, which is the contract's job (below).
//
//  ★ LISTED PATHS ARE NAME-FREE (S125 check-in, call 1). A career's folders are
//  named after its file (`s125-career.json` keeps its logs in
//  `s125-career.gamelog/`), so the list holds `gamelog/season-1.log` and the store
//  resolves it next to the career file. The career file's bytes therefore do not
//  depend on what the file is called.
//
//  ★ THE CONTRACT (ChatGPT R1 §4). One set of rules, in this file, that B2 extends:
//    • every path is one of the five kinds, canonical, '/'-separated:
//        events/season-N.json
//        gamelog/season-N.log | season-N.log.inprogress | season-N.scout
//        league/league-N.json
//      (N a positive decimal with no leading zero; league-0 is the one zero) —
//      which by construction refuses an absolute path, a `..`, a `\`, the career
//      file, its `.bak` / `.new` / lock and anything under the orphans folder;
//    • no path twice (compared case-insensitively, so two spellings of one file on
//      Windows cannot both be listed); sorted by path, ordinal;
//    • every hash 64 lowercase hex;
//    • `seasons` is exactly 1, 2, ..., nextSeasonId − 1, each with at least one
//      rules entry, the rules' days strictly increasing;
//    • every listed season file names a listed season; `league-0` is listed; a
//      `league-N` names a listed season;
//    • `progress` is null (B2 widens it).
//
//  ★ v1, v2 AND v3 ARE REFUSED BY NAME, NEVER UPGRADED (C-60). v3 is recognised by
//  its version before anything else is asked of it.
// ============================================================================

/// <summary>One file the career owns: its name-free path and its SHA-256.</summary>
public sealed record ListedFile(string Path, string Sha256);

/// <summary>★ S125 (K4) — the rules a stretch of a season was played under: the first day they
/// applied, the SHA-256 of the settings file as read, and the build stamp. A build-identity
/// record, not a compatibility proof. Nothing reads it in B1b.</summary>
public sealed record SeasonRules(string FromDay, string ConfigSha256, string RulesStamp);

/// <summary>One published season and the rules it was played under (one entry in B1b).</summary>
public sealed record SeasonEntry(long Season, IReadOnlyList<SeasonRules> Rules);

/// <summary>The complete persisted state, version 4. `Next*` means NEXT UNISSUED;
/// <see cref="StartYear"/> is the civil year the career's FIRST season opens in.</summary>
public sealed record HistoryStateV4(
    string HistoryId,
    string WorldFingerprint,
    int StartYear,
    long NextPersonId,
    long NextSeasonId,
    long NextGameId,
    IReadOnlyList<ListedFile> Files,
    IReadOnlyList<SeasonEntry> Seasons)
{
    public const string FormatTag = "charm-history";
    public const int SchemaVersion = 4;

    /// <summary>★ The year a career starts in when none is named — the season every run played
    /// before S119. Phase 110 C4 pins it equal to the harness's own default.</summary>
    public const int DefaultStartYear = 2026;

    /// <summary>★ The calendar's season bounds, restated because this assembly references nothing.
    /// Phase 110 C5 pins both against the calendar's.</summary>
    public const int MinStartYear = 1;

    /// <inheritdoc cref="MinStartYear"/>
    public const int MaxStartYear = 9998;

    public static bool IsStartYearInDomain(long year) => year >= MinStartYear && year <= MaxStartYear;
}

internal static class HistorySchemaV4
{
    private static readonly string[] RootKeys =
        { "format", "schemaVersion", "historyId", "worldFingerprint", "startYear",
          "nextPersonId", "nextSeasonId", "nextGameId", "files", "seasons", "progress" };

    internal const string TrailerPrefix = "sha256:";
    internal const int TrailerLength = 7 + 64 + 1;   // "sha256:" + hex + "\n"

    // ── The trailer ─────────────────────────────────────────────────────────

    /// <summary>The payload with its trailer appended.</summary>
    internal static byte[] Seal(byte[] payload)
    {
        var hex = Sha256Hex(payload);
        var trailer = Encoding.ASCII.GetBytes(TrailerPrefix + hex + "\n");
        var all = new byte[payload.Length + trailer.Length];
        payload.CopyTo(all, 0);
        trailer.CopyTo(all, payload.Length);
        return all;
    }

    /// <summary>Split a file into payload and trailer. Returns the payload when the last line has
    /// the trailer's shape (whether or not its hash matches — see <paramref name="verified"/>), or
    /// null when there is no trailer-shaped last line at all.</summary>
    internal static byte[]? SplitTrailer(byte[] bytes, out bool verified)
    {
        verified = false;
        if (bytes.Length < TrailerLength + 1) return null;
        var start = bytes.Length - TrailerLength;
        if (bytes[^1] != (byte)'\n' || bytes[start - 1] != (byte)'\n') return null;
        var line = Encoding.ASCII.GetString(bytes, start, TrailerLength - 1);
        if (!line.StartsWith(TrailerPrefix, StringComparison.Ordinal)) return null;
        var hex = line[TrailerPrefix.Length..];
        if (!IsLowerHex64(hex)) return null;
        var payload = bytes.AsSpan(0, start).ToArray();
        verified = string.Equals(Sha256Hex(payload), hex, StringComparison.Ordinal);
        return payload;
    }

    internal static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static bool IsLowerHex64(string? s)
    {
        if (s is null || s.Length != 64) return false;
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    // ── Canonical serialization ─────────────────────────────────────────────
    //  Key order fixed here and pinned by `tools/history_v4_golden.json`: the v3 keys in
    //  their v3 order, then files, seasons, progress. 2-space indent, "\n", UTF-8 no BOM,
    //  one final newline, then the trailer.

    internal static byte[] Serialize(HistoryStateV4 s)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteString("format", HistoryStateV4.FormatTag);
            w.WriteNumber("schemaVersion", HistoryStateV4.SchemaVersion);
            w.WriteString("historyId", s.HistoryId);
            w.WriteString("worldFingerprint", s.WorldFingerprint);
            w.WriteNumber("startYear", s.StartYear);
            w.WriteNumber("nextPersonId", s.NextPersonId);
            w.WriteNumber("nextSeasonId", s.NextSeasonId);
            w.WriteNumber("nextGameId", s.NextGameId);
            w.WriteStartArray("files");
            foreach (var f in s.Files)
            {
                w.WriteStartObject();
                w.WriteString("path", f.Path);
                w.WriteString("sha256", f.Sha256);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("seasons");
            foreach (var e in s.Seasons)
            {
                w.WriteStartObject();
                w.WriteNumber("season", e.Season);
                w.WriteStartArray("rules");
                foreach (var r in e.Rules)
                {
                    w.WriteStartObject();
                    w.WriteString("fromDay", r.FromDay);
                    w.WriteString("configSha256", r.ConfigSha256);
                    w.WriteString("rulesStamp", r.RulesStamp);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNull("progress");
            w.WriteEndObject();
        }
        var body = stream.ToArray();
        var payload = new byte[body.Length + 1];
        Array.Copy(body, payload, body.Length);
        payload[body.Length] = (byte)'\n';
        return Seal(payload);
    }

    // ── Parsing (the payload, after the trailer has verified) ───────────────

    internal static HistoryStateV4 Parse(byte[] payload)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException jx)
        {
            throw new HistoryException(HistoryError.MalformedJson,
                $"history file is not valid JSON — {jx.Message}", jx);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new HistoryException(HistoryError.WrongType, "history file root must be a JSON object.");

            RejectUnknownOrDuplicateKeys(root, RootKeys, "history file");

            var format = RequireString(root, "format");
            if (!string.Equals(format, HistoryStateV4.FormatTag, StringComparison.Ordinal))
                throw new HistoryException(HistoryError.WrongFormat,
                    $"'format' must be '{HistoryStateV4.FormatTag}' (got '{format}') — this is not a Charm history file.");

            var version = RequireLong(root, "schemaVersion");
            if (version != HistoryStateV4.SchemaVersion)
                throw new HistoryException(HistoryError.UnsupportedVersion,
                    $"unsupported history schemaVersion {version.ToString(CultureInfo.InvariantCulture)} " +
                    "(this build reads 4).");

            var historyId = RequireString(root, "historyId");
            if (!IsCanonicalHistoryId(historyId))
                throw new HistoryException(HistoryError.WrongType,
                    "'historyId' must be exactly 32 lowercase hex characters.");

            var fingerprint = RequireString(root, "worldFingerprint");
            var startYear = RequireLong(root, "startYear");
            if (!HistoryStateV4.IsStartYearInDomain(startYear))
                throw new HistoryException(HistoryError.YearOutOfDomain,
                    $"'startYear' is {startYear.ToString(CultureInfo.InvariantCulture)}, outside the years a " +
                    $"season can start in ({HistoryStateV4.MinStartYear}..{HistoryStateV4.MaxStartYear}).");
            var person = RequireCounter(root, "nextPersonId");
            var season = RequireCounter(root, "nextSeasonId");
            var game   = RequireCounter(root, "nextGameId");

            var files = ParseFiles(root.GetProperty("files"));
            var seasons = ParseSeasons(root.GetProperty("seasons"));

            var progress = root.GetProperty("progress");
            if (progress.ValueKind != JsonValueKind.Null)
                throw new HistoryException(HistoryError.ProgressNotNull,
                    "'progress' must be null — this build saves only at a season's close.");

            var state = new HistoryStateV4(historyId, fingerprint, (int)startYear, person, season, game, files, seasons);
            ValidateContract(state);
            return state;
        }
    }

    private static List<ListedFile> ParseFiles(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            throw new HistoryException(HistoryError.WrongType, "'files' must be an array.");
        var list = new List<ListedFile>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new HistoryException(HistoryError.WrongType, "each 'files' entry must be an object.");
            RejectUnknownOrDuplicateKeys(item, new[] { "path", "sha256" }, "a 'files' entry");
            list.Add(new ListedFile(RequireString(item, "path"), RequireString(item, "sha256")));
        }
        return list;
    }

    private static List<SeasonEntry> ParseSeasons(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            throw new HistoryException(HistoryError.WrongType, "'seasons' must be an array.");
        var list = new List<SeasonEntry>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new HistoryException(HistoryError.WrongType, "each 'seasons' entry must be an object.");
            RejectUnknownOrDuplicateKeys(item, new[] { "season", "rules" }, "a 'seasons' entry");
            var n = RequireLong(item, "season");
            var rulesEl = item.GetProperty("rules");
            if (rulesEl.ValueKind != JsonValueKind.Array)
                throw new HistoryException(HistoryError.WrongType, "a season's 'rules' must be an array.");
            var rules = new List<SeasonRules>();
            foreach (var r in rulesEl.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object)
                    throw new HistoryException(HistoryError.WrongType, "each rules entry must be an object.");
                RejectUnknownOrDuplicateKeys(r, new[] { "fromDay", "configSha256", "rulesStamp" }, "a rules entry");
                rules.Add(new SeasonRules(RequireString(r, "fromDay"), RequireString(r, "configSha256"),
                                          RequireString(r, "rulesStamp")));
            }
            list.Add(new SeasonEntry(n, rules));
        }
        return list;
    }

    // ── The contract ────────────────────────────────────────────────────────

    /// <summary>The kinds of file a career may list. Returns the kind and the N it names, or null
    /// for a path that is not one of them.</summary>
    internal static (string Kind, long N)? ClassifyListedPath(string path)
    {
        static long? Number(string digits, bool zeroAllowed)
        {
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return null;
            if (digits == "0") return zeroAllowed ? 0 : null;
            if (digits[0] == '0') return null;
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        static (string, long)? Match(string path, string folder, string prefix, string suffix, string kind, bool zero)
        {
            var head = folder + "/" + prefix;
            if (!path.StartsWith(head, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal)) return null;
            if (path.Length <= head.Length + suffix.Length) return null;
            var n = Number(path[head.Length..^suffix.Length], zero);
            return n is { } v ? (kind, v) : null;
        }
        return Match(path, "events", "season-", ".json", "events", false)
            ?? Match(path, "gamelog", "season-", ".log.inprogress", "inprogress", false)
            ?? Match(path, "gamelog", "season-", ".log", "log", false)
            ?? Match(path, "gamelog", "season-", ".scout", "scout", false)
            ?? Match(path, "league", "league-", ".json", "league", true);
    }

    internal static void ValidateContract(HistoryStateV4 s)
    {
        // Paths: one of the five kinds, no duplicates, sorted.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previous = null;
        var kinds = new List<(string Kind, long N, string Path)>();
        foreach (var f in s.Files)
        {
            if (string.IsNullOrEmpty(f.Path) || f.Path.Contains('\\') || f.Path.StartsWith('/')
                || f.Path.Contains(':') || f.Path.Split('/').Any(seg => seg is ".." or "." or ""))
                throw new HistoryException(HistoryError.ListedPathInvalid,
                    $"the career lists '{f.Path}', which is not a plain path inside the career's folders " +
                    "(absolute, '..', '\\' and empty segments are never listed).");
            var c = ClassifyListedPath(f.Path);
            if (c is null)
                throw new HistoryException(HistoryError.ListedPathInvalid,
                    $"the career lists '{f.Path}', which is not a file a career owns (a season's event record, " +
                    "log, scouting file, or a league state). The career file, its backup, its lock and anything " +
                    "set aside are never listed.");
            if (!seen.Add(f.Path))
                throw new HistoryException(HistoryError.ListedPathDuplicate,
                    $"the career lists '{f.Path}' twice.");
            if (previous is not null && string.CompareOrdinal(previous, f.Path) >= 0)
                throw new HistoryException(HistoryError.ListedPathInvalid,
                    $"the career's file list is out of order at '{f.Path}' (it is sorted by path).");
            if (!IsLowerHex64(f.Sha256))
                throw new HistoryException(HistoryError.ListedHashInvalid,
                    $"the career lists '{f.Path}' with a hash that is not 64 lowercase hex characters.");
            previous = f.Path;
            kinds.Add((c.Value.Kind, c.Value.N, f.Path));
        }

        // Seasons: exactly 1..nextSeasonId-1, each with rules.
        for (var i = 0; i < s.Seasons.Count; i++)
        {
            var e = s.Seasons[i];
            if (e.Season != i + 1)
                throw new HistoryException(HistoryError.SeasonsInvalid,
                    $"the career's season list is out of order, has a gap or a repeat: entry {(i + 1).ToString(CultureInfo.InvariantCulture)} " +
                    $"is season {e.Season.ToString(CultureInfo.InvariantCulture)}.");
            if (e.Rules.Count == 0)
                throw new HistoryException(HistoryError.SeasonsInvalid,
                    $"season {e.Season.ToString(CultureInfo.InvariantCulture)} records no rules.");
            string? lastDay = null;
            foreach (var r in e.Rules)
            {
                if (!IsCanonicalDay(r.FromDay))
                    throw new HistoryException(HistoryError.SeasonsInvalid,
                        $"season {e.Season.ToString(CultureInfo.InvariantCulture)} has a rules entry whose day '{r.FromDay}' is not a yyyy-MM-dd date.");
                if (lastDay is not null && string.CompareOrdinal(lastDay, r.FromDay) >= 0)
                    throw new HistoryException(HistoryError.SeasonsInvalid,
                        $"season {e.Season.ToString(CultureInfo.InvariantCulture)}'s rules entries are not in day order.");
                if (!IsLowerHex64(r.ConfigSha256) || !IsLowerHex64(r.RulesStamp))
                    throw new HistoryException(HistoryError.SeasonsInvalid,
                        $"season {e.Season.ToString(CultureInfo.InvariantCulture)} has a rules entry whose hash is not 64 lowercase hex characters.");
                lastDay = r.FromDay;
            }
        }
        if (s.Seasons.Count != s.NextSeasonId - 1)
            throw new HistoryException(HistoryError.SeasonsInvalid,
                $"the career lists {s.Seasons.Count.ToString(CultureInfo.InvariantCulture)} season(s) but says its next " +
                $"season is {s.NextSeasonId.ToString(CultureInfo.InvariantCulture)}.");

        // Every listed file names a listed season; league-0 is always listed.
        var played = s.NextSeasonId - 1;
        var hasLeagueZero = false;
        foreach (var (kind, n, path) in kinds)
        {
            if (kind == "league" && n == 0) { hasLeagueZero = true; continue; }
            if (n < 1 || n > played)
                throw new HistoryException(HistoryError.ManifestMismatch,
                    $"the career lists '{path}', but season {n.ToString(CultureInfo.InvariantCulture)} is not a season it has published.");
        }
        if (!hasLeagueZero)
            throw new HistoryException(HistoryError.ManifestMismatch,
                "the career does not list league/league-0.json — every career is born with its league state.");
    }

    internal static bool IsCanonicalDay(string? s)
        => s is { Length: 10 }
           && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
           && d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == s;

    /// <summary>32 lowercase hex characters. Lowercase ONLY, because two spellings of one
    /// label would compare unequal while naming the same career.</summary>
    internal static bool IsCanonicalHistoryId(string? s)
    {
        if (s is null || s.Length != 32) return false;
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    /// <summary>Mint a fresh lineage label. Guid is the ENTROPY SOURCE ONLY — the canonical
    /// form is the hex string, never Guid's own mixed-endian byte layout. Deliberately NOT any
    /// simulation RNG: an allocator that drew from a game stream would change the basketball
    /// by saving.</summary>
    internal static string MintHistoryId() => Guid.NewGuid().ToString("N");

    internal static void RejectUnknownOrDuplicateKeys(JsonElement obj, string[] keys, string what)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
        {
            if (!keys.Contains(p.Name, StringComparer.Ordinal))
                throw new HistoryException(HistoryError.UnknownKey,
                    $"unknown key '{p.Name}' in {what} (this schema defines: {string.Join(", ", keys)}).");
            if (!seen.Add(p.Name))
                throw new HistoryException(HistoryError.DuplicateKey,
                    $"duplicate key '{p.Name}' in {what}.");
        }
        foreach (var k in keys)
            if (!seen.Contains(k))
                throw new HistoryException(HistoryError.MissingKey,
                    $"missing required key '{k}' in {what}.");
    }

    internal static string RequireString(JsonElement obj, string name)
    {
        var el = obj.GetProperty(name);
        if (el.ValueKind != JsonValueKind.String)
            throw new HistoryException(HistoryError.WrongType, $"'{name}' must be a string.");
        return el.GetString() ?? "";
    }

    internal static long RequireLong(JsonElement obj, string name)
    {
        var el = obj.GetProperty(name);
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt64(out var v))
            throw new HistoryException(HistoryError.WrongType, $"'{name}' must be an integer.");
        return v;
    }

    private static long RequireCounter(JsonElement root, string name)
    {
        var v = RequireLong(root, name);
        if (v < 1 || v == long.MaxValue)
            throw new HistoryException(HistoryError.CounterOutOfDomain,
                $"'{name}' is {v.ToString(CultureInfo.InvariantCulture)}, outside the valid domain " +
                "(1 .. long.MaxValue-1).");
        return v;
    }
}
