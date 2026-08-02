using System.Globalization;
using System.Text.Json;

namespace Charm.History;

// ============================================================================
//  S90 — THE HISTORY FILE, version 2: the same three counters, plus a name for
//  the career itself.
//
//  ★ WHY v1 WAS NOT ENOUGH, and it is a narrow, real hole. A v1 history binds to
//  a WORLD. Two careers started from the same world legally share the world
//  fingerprint AND start at person 1, season 1, game 1 — so a retention log
//  copied from one into the other passes every check v1 could make, and one
//  career silently absorbs another's games. The `historyId` closes it: a random
//  128-bit label minted once, at creation, naming this career lineage.
//
//  ★ WHAT IT PROVES, HONESTLY. Two histories created INDEPENDENTLY can never
//  exchange logs undetected. A history file COPIED at the filesystem carries its
//  label with it, so the two branches are indistinguishable to S90 and this makes
//  no claim otherwise — that is the same trust boundary S89 already draws, since
//  a copied history also duplicates every counter. Telling branches apart needs
//  identity minted by a controlled clone operation, which is save-branch
//  management and a different session.
//
//  ★ IT IS A LABEL, NOT AN IDENTITY. No counter, no ordering, no meaning beyond
//  "this lineage". It is drawn from Guid.NewGuid() — the idiom five suite files
//  already use — and explicitly NOT from any simulation RNG: an allocator that
//  drew from a game stream would change the basketball by saving.
// ============================================================================

/// <summary>The complete persisted state, version 2. `Next*` means NEXT UNISSUED.</summary>
public sealed record HistoryStateV2(
    string HistoryId,
    string WorldFingerprint,
    long NextPersonId,
    long NextSeasonId,
    long NextGameId)
{
    public const string FormatTag = "charm-history";
    public const int SchemaVersion = 2;

    public static HistoryStateV2 Fresh(string historyId, string worldFingerprint)
        => new(historyId, worldFingerprint, 1, 1, 1);

    /// <summary>A v1 file gains a lineage label and nothing else — every counter is
    /// carried across untouched, because a migration that moved a counter would be
    /// reissuing numbers that are already on people.</summary>
    public static HistoryStateV2 FromV1(HistoryStateV1 v1, string historyId)
        => new(historyId, v1.WorldFingerprint, v1.NextPersonId, v1.NextSeasonId, v1.NextGameId);
}

internal static class HistorySchemaV2
{
    private static readonly string[] RootKeys =
        { "format", "schemaVersion", "historyId", "worldFingerprint",
          "nextPersonId", "nextSeasonId", "nextGameId" };

    /// <summary>Read only `schemaVersion`, so the loader can route to the right parser
    /// instead of a v1 parser rejecting a v2 file as "unknown key historyId" — which is
    /// a true statement and a completely misleading error.</summary>
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
    //  historyId, worldFingerprint, then the three counters. 2-space indent,
    //  "\n" newlines, UTF-8 with no BOM, one final newline — identical
    //  discipline to v1, so the two goldens differ only where the schema does.
    internal static byte[] Serialize(HistoryStateV2 s)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteString("format", HistoryStateV2.FormatTag);
            w.WriteNumber("schemaVersion", HistoryStateV2.SchemaVersion);
            w.WriteString("historyId", s.HistoryId);
            w.WriteString("worldFingerprint", s.WorldFingerprint);
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

    internal static HistoryStateV2 Parse(byte[] bytes)
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
            if (!string.Equals(format, HistoryStateV2.FormatTag, StringComparison.Ordinal))
                throw new HistoryException(HistoryError.WrongFormat,
                    $"'format' must be '{HistoryStateV2.FormatTag}' (got '{format}') — this is not a Charm history file.");

            var version = RequireLong(root, "schemaVersion");
            if (version != HistoryStateV2.SchemaVersion)
                throw new HistoryException(HistoryError.UnsupportedVersion,
                    $"unsupported history schemaVersion {version.ToString(CultureInfo.InvariantCulture)} " +
                    "(this build reads 1 and 2).");

            var historyId = RequireString(root, "historyId");
            if (!IsCanonicalHistoryId(historyId))
                throw new HistoryException(HistoryError.WrongType,
                    "'historyId' must be exactly 32 lowercase hex characters.");

            var fingerprint = RequireString(root, "worldFingerprint");
            var person = RequireCounter(root, "nextPersonId");
            var season = RequireCounter(root, "nextSeasonId");
            var game   = RequireCounter(root, "nextGameId");

            return new HistoryStateV2(historyId, fingerprint, person, season, game);
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
