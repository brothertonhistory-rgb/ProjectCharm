using System.Globalization;
using System.Text.Json;

namespace Charm.History;

// ============================================================================
//  S119 — THE HISTORY FILE, version 3: v2 plus the year the career began.
//
//  ★ WHY THE YEAR LIVES HERE. A career can start in any year the player picks
//  (Emmett, 2026-10-09: "If they want to start in 1950 with whatever slate of
//  teams, they can"). Season N of a career is played in startYear + (N - 1), and
//  the only thing that knows N is this file's season counter. So the first year
//  is stored beside the counter, once, at creation, and is never written again.
//  It is read back off the file every season — never carried in memory — which
//  is what makes a career closed in 1950 and reopened later still play 1951.
//
//  ★ v1 AND v2 ARE REFUSED BY NAME, NEVER UPGRADED (Emmett's ruling C-60: the
//  game is the product, not its save files). A v2 career has no start year, and
//  guessing one (2026) would be a quiet call about somebody's career.
//
//  ★ v2's REASONS STILL HOLD, carried forward unchanged: the `historyId` is a
//  random 128-bit lineage label minted once at creation, so two careers built
//  independently from one world can never exchange logs undetected. A copied
//  file carries its label with it — that trust boundary is S89's and S90's, not
//  a new one. The label is drawn from Guid.NewGuid() and explicitly NOT from any
//  simulation RNG: an allocator that drew from a game stream would change the
//  basketball by saving.
// ============================================================================

/// <summary>The complete persisted state, version 3. `Next*` means NEXT UNISSUED;
/// <see cref="StartYear"/> is the civil year the career's FIRST season opens in.</summary>
public sealed record HistoryStateV3(
    string HistoryId,
    string WorldFingerprint,
    int StartYear,
    long NextPersonId,
    long NextSeasonId,
    long NextGameId)
{
    public const string FormatTag = "charm-history";
    public const int SchemaVersion = 3;

    /// <summary>★ The year a career starts in when none is named. It is the season every
    /// run played before S119, so a career created without a year is the career it always
    /// was. Phase 110 C4 pins it equal to the harness's own default — one number, asserted
    /// in the one place both sides can see it, because this assembly cannot see the season.</summary>
    public const int DefaultStartYear = 2026;

    /// <summary>★ The calendar's season bounds, restated because this assembly deliberately
    /// references nothing (see Charm.History.csproj). A season crosses New Year's, so the last
    /// one that can exist starts in 9998. Phase 110 C5 pins both against
    /// <c>CharmCalendar.MinSeasonStartYear</c> / <c>MaxSeasonStartYear</c>.</summary>
    public const int MinStartYear = 1;

    /// <inheritdoc cref="MinStartYear"/>
    public const int MaxStartYear = 9998;

    public static bool IsStartYearInDomain(long year) => year >= MinStartYear && year <= MaxStartYear;

    public static HistoryStateV3 Fresh(string historyId, string worldFingerprint, int startYear)
        => new(historyId, worldFingerprint, startYear, 1, 1, 1);
}

internal static class HistorySchemaV3
{
    private static readonly string[] RootKeys =
        { "format", "schemaVersion", "historyId", "worldFingerprint", "startYear",
          "nextPersonId", "nextSeasonId", "nextGameId" };

    /// <summary>Read only `schemaVersion`, so the loader can refuse an older career for
    /// being older (S118.2) instead of letting this parser reject it as "missing key
    /// historyId" — which is a true statement and a completely misleading error.</summary>
    internal static int PeekVersion(byte[] bytes)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes); }
        catch (JsonException jx)
        {
            throw new HistoryException(HistoryError.MalformedJson,
                $"history file is not valid JSON — {jx.Message}", jx);
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new HistoryException(HistoryError.WrongType, "history file root must be a JSON object.");
            if (!doc.RootElement.TryGetProperty("schemaVersion", out var el))
                throw new HistoryException(HistoryError.MissingKey,
                    "missing required key 'schemaVersion' in history file.");
            if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var v))
                throw new HistoryException(HistoryError.WrongType, "'schemaVersion' must be an integer.");
            return v;
        }
    }

    // ── Canonical serialization ─────────────────────────────────────────────
    //  Key order fixed here and pinned by a golden: format, schemaVersion,
    //  historyId, worldFingerprint, startYear, then the three counters. 2-space
    //  indent, "\n" newlines, UTF-8 with no BOM, one final newline — the discipline
    //  S89 set for v1, pinned by `tools/history_v3_golden.json`.
    internal static byte[] Serialize(HistoryStateV3 s)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteString("format", HistoryStateV3.FormatTag);
            w.WriteNumber("schemaVersion", HistoryStateV3.SchemaVersion);
            w.WriteString("historyId", s.HistoryId);
            w.WriteString("worldFingerprint", s.WorldFingerprint);
            w.WriteNumber("startYear", s.StartYear);
            w.WriteNumber("nextPersonId", s.NextPersonId);
            w.WriteNumber("nextSeasonId", s.NextSeasonId);
            w.WriteNumber("nextGameId", s.NextGameId);
            w.WriteEndObject();
        }
        var body = stream.ToArray();
        var out_ = new byte[body.Length + 1];
        Array.Copy(body, out_, body.Length);
        out_[body.Length] = (byte)'\n';
        return out_;
    }

    internal static HistoryStateV3 Parse(byte[] bytes)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes); }
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

            RejectUnknownOrDuplicateKeys(root);

            var format = RequireString(root, "format");
            if (!string.Equals(format, HistoryStateV3.FormatTag, StringComparison.Ordinal))
                throw new HistoryException(HistoryError.WrongFormat,
                    $"'format' must be '{HistoryStateV3.FormatTag}' (got '{format}') — this is not a Charm history file.");

            var version = RequireLong(root, "schemaVersion");
            if (version != HistoryStateV3.SchemaVersion)
                throw new HistoryException(HistoryError.UnsupportedVersion,
                    $"unsupported history schemaVersion {version.ToString(CultureInfo.InvariantCulture)} " +
                    "(this build reads 3).");

            var historyId = RequireString(root, "historyId");
            if (!IsCanonicalHistoryId(historyId))
                throw new HistoryException(HistoryError.WrongType,
                    "'historyId' must be exactly 32 lowercase hex characters.");

            var fingerprint = RequireString(root, "worldFingerprint");
            var startYear = RequireLong(root, "startYear");
            if (!HistoryStateV3.IsStartYearInDomain(startYear))
                throw new HistoryException(HistoryError.YearOutOfDomain,
                    $"'startYear' is {startYear.ToString(CultureInfo.InvariantCulture)}, outside the years a " +
                    $"season can start in ({HistoryStateV3.MinStartYear}..{HistoryStateV3.MaxStartYear}).");
            var person = RequireCounter(root, "nextPersonId");
            var season = RequireCounter(root, "nextSeasonId");
            var game   = RequireCounter(root, "nextGameId");

            return new HistoryStateV3(historyId, fingerprint, (int)startYear, person, season, game);
        }
    }

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
    /// form is the hex string, never Guid's own mixed-endian byte layout.</summary>
    internal static string MintHistoryId() => Guid.NewGuid().ToString("N");

    private static void RejectUnknownOrDuplicateKeys(JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in root.EnumerateObject())
        {
            if (!RootKeys.Contains(p.Name, StringComparer.Ordinal))
                throw new HistoryException(HistoryError.UnknownKey,
                    $"unknown key '{p.Name}' in history file (this schema defines: {string.Join(", ", RootKeys)}).");
            if (!seen.Add(p.Name))
                throw new HistoryException(HistoryError.DuplicateKey,
                    $"duplicate key '{p.Name}' in history file.");
        }
        foreach (var k in RootKeys)
            if (!seen.Contains(k))
                throw new HistoryException(HistoryError.MissingKey,
                    $"missing required key '{k}' in history file.");
    }

    private static string RequireString(JsonElement root, string name)
    {
        var el = root.GetProperty(name);
        if (el.ValueKind != JsonValueKind.String)
            throw new HistoryException(HistoryError.WrongType, $"'{name}' must be a string.");
        return el.GetString() ?? "";
    }

    private static long RequireLong(JsonElement root, string name)
    {
        var el = root.GetProperty(name);
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
