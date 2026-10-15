using System.Text.Json;

namespace Charm.History;

// ============================================================================
//  S119 — THE HISTORY FILE, version 3 — RETIRED AT S125 (C-60).
//
//  v3 was v2 plus the year the career began. S125 replaced it with v4 (the list of
//  every file a career owns, each season's rules, and the file's own fingerprint —
//  see HistorySchemaV4.cs). Its serializer and parser are retired with the format:
//  a v3 career is refused by name, never upgraded (Emmett's ruling C-60: the game is
//  the product, not its save files).
//
//  What stays is the VERSION PEEK, because it is how an older career is recognised
//  as older — a v3 file has no trailer, and without the peek it would be called torn,
//  which is a true statement and a completely misleading one.
// ============================================================================

internal static class HistorySchemaV3
{
    /// <summary>Read only `schemaVersion`, so the loader can refuse an older career for
    /// being older (S118.2) instead of for whatever the current parser trips on first.</summary>
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
}
