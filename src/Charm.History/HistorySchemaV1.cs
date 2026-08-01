using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Charm.History;

// ============================================================================
//  S89 — THE HISTORY FILE, version 1.
//
//  It holds THREE COUNTERS AND A WORLD BINDING, and deliberately nothing else.
//  There are no players in it, no seasons, no box scores. What it guarantees is
//  narrow and total: every number below a stored counter is spent forever.
//
//  The retention session adds the entity tables. What it inherits from here is
//  that every number those tables will store is trustworthy, because nothing was
//  ever reused underneath them.
//
//  ★ THE LOADER IS AS STRICT AS THE WORLD LOADER, on purpose. Unknown key,
//  duplicate key, missing key, wrong type, wrong format, wrong version,
//  out-of-domain counter — each is its own named refusal. `Program.World.cs`
//  set that standard for reading a file this project cannot afford to guess at,
//  and a save file deserves it more than a world file does: a misread world
//  produces a wrong league, a misread history produces two men with one number.
// ============================================================================

/// <summary>The complete persisted state, version 1. `Next*` means NEXT UNISSUED:
/// every value below it is permanently unavailable, and the stored value itself has
/// not been handed out yet.</summary>
public sealed record HistoryStateV1(
    string WorldFingerprint,
    long NextPersonId,
    long NextSeasonId,
    long NextGameId)
{
    public const string FormatTag = "charm-history";
    public const int SchemaVersion = 1;

    /// <summary>A brand-new history for a world: nothing issued, everything starts at 1.</summary>
    public static HistoryStateV1 Fresh(string worldFingerprint)
        => new(worldFingerprint, 1, 1, 1);
}

internal static class HistorySchemaV1
{
    private static readonly string[] RootKeys =
        { "format", "schemaVersion", "worldFingerprint", "nextPersonId", "nextSeasonId", "nextGameId" };

    // ── Canonical serialization ──────────────────────────────────────────────
    //  Specified ONCE, here: the key order above, 2-space indent, "\n" newlines,
    //  UTF-8 with NO byte-order mark, one final newline. A golden fixture pins it,
    //  so a later session cannot drift the format without the suite saying so.
    internal static byte[] Serialize(HistoryStateV1 s)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteString("format", HistoryStateV1.FormatTag);
            w.WriteNumber("schemaVersion", HistoryStateV1.SchemaVersion);
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

    // ── Strict parse ─────────────────────────────────────────────────────────
    internal static HistoryStateV1 Parse(byte[] bytes)
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
                throw new HistoryException(HistoryError.WrongType,
                    "history file root must be a JSON object.");

            RejectUnknownOrDuplicateKeys(root);

            var format = RequireString(root, "format");
            if (!string.Equals(format, HistoryStateV1.FormatTag, StringComparison.Ordinal))
                throw new HistoryException(HistoryError.WrongFormat,
                    $"'format' must be '{HistoryStateV1.FormatTag}' (got '{format}') — this is not a Charm history file.");

            var version = RequireLong(root, "schemaVersion");
            if (version != HistoryStateV1.SchemaVersion)
                throw new HistoryException(HistoryError.UnsupportedVersion,
                    $"unsupported history schemaVersion {version.ToString(CultureInfo.InvariantCulture)} " +
                    $"(this build reads {HistoryStateV1.SchemaVersion}).");

            var fingerprint = RequireString(root, "worldFingerprint");
            var person = RequireCounter(root, "nextPersonId");
            var season = RequireCounter(root, "nextSeasonId");
            var game   = RequireCounter(root, "nextGameId");

            return new HistoryStateV1(fingerprint, person, season, game);
        }
    }

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

    /// <summary>A next-value must be a real place on the number line: at least 1 (issuance
    /// starts there, so 0 and negatives are not "nothing issued", they are corruption), and
    /// strictly below `long.MaxValue` so that at minimum one more identity is reachable.</summary>
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
