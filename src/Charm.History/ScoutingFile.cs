using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Charm.History;

// ============================================================================
//  S121 — THE SCOUTING FILE: what the staff knows about a man and he does not show.
//
//  Emmett's ruling B (2026-10-10): potential lives in a SEPARATE file that holds only
//  current players and forgets each man when he leaves. The season logs stay a pure
//  record of what happened (S90's ruling) — nothing hidden is ever written into them.
//
//  ★ ONE FILE PER SEASON, beside that season's log (`season-N.scout` in the same folder),
//    holding THAT season's people only. Season N+1's camp reads season N's log AND season
//    N's scouting file — the same pair a retry after a failed (burned) season reads again,
//    so a burned season can never leave the camp reading state that never played.
//
//  ★ WRITTEN WHOLE, ONCE (write-then-rename). An existing file is a refusal, never an
//    overwrite — the same rule the season log follows.
//
//  ★ A MISSING OR DAMAGED FILE REFUSES BY NAME. It is never re-rolled: re-rolling would hand
//    every player new potential, which is a different career pretending to be this one.
//
//  ★ PERSON NUMBERS STAY BEHIND THE WALL. The raw number is written and read here, inside
//    this assembly (S89's seam), exactly as the season log does it.
//
//  Format (text, UTF-8, LF). Line 1 the magic, line 2 the version, line 3 the SHA-256 of
//  every byte after line 3; then the bindings, the counts, and one line per man. Every
//  double is written as its 64 raw bits, so hidden progress round-trips EXACTLY.
// ============================================================================

public enum ScoutingError
{
    /// <summary>No scouting file where the season's arithmetic says one must be.</summary>
    Missing,
    /// <summary>A file that is not a scouting file, fails its checksum, or cannot be parsed.</summary>
    Damaged,
    /// <summary>A version this build does not write (C-60: refused whole, never read half-blind).</summary>
    UnsupportedVersion,
    /// <summary>A file from another career.</summary>
    HistoryIdMismatch,
    /// <summary>A file from another world.</summary>
    WorldMismatch,
    /// <summary>A file from another season of this career.</summary>
    SeasonMismatch,
    /// <summary>A scouting file already exists for the season being written.</summary>
    AlreadyExists,
    /// <summary>A record outside its domain (tier, progress, work ethic, a duplicate man, ragged lengths).</summary>
    InvalidRecord,
    /// <summary>A filesystem operation failed.</summary>
    PersistFailed,
}

public sealed class ScoutingException : Exception
{
    public ScoutingError Error { get; }
    public ScoutingException(ScoutingError error, string message, Exception? inner = null)
        : base(message, inner) => Error = error;
}

/// <summary>One man's hidden development state. The arrays are the caller's canonical order.</summary>
public sealed record ScoutingRecord(
    PersonId Person, ulong DevSeed, int WorkEthic, int ArrivalIq, int ArrivalDiscipline,
    IReadOnlyList<int> Tiers, IReadOnlyList<int> Streaks, IReadOnlyList<double> Progress);

public sealed record ScoutingBindings(string HistoryId, string WorldFingerprint, long SeasonId);

public static class ScoutingFile
{
    public const int Version = 1;
    private const string MagicLine = "CHRMSCOUT";

    public static string PathFor(string historyPath, long seasonId)
        => Path.Combine(GameLogWriter.LogFolderFor(historyPath),
                        "season-" + seasonId.ToString(CultureInfo.InvariantCulture) + ".scout");

    // ── Writing ─────────────────────────────────────────────────────────────

    /// <summary>Validates every record, then writes the whole file once through a temporary name
    /// and a rename. An existing file for this season is refused.</summary>
    public static string Write(string historyPath, string historyId, string worldFingerprint,
                               SeasonId seasonId, IReadOnlyList<ScoutingRecord> records)
    {
        if (!seasonId.IsValid)
            throw new ScoutingException(ScoutingError.InvalidRecord, "a scouting file needs an issued season.");
        var text = Encode(historyId, worldFingerprint, seasonId.Raw, records);
        var final = PathFor(historyPath, seasonId.Raw);
        var folder = Path.GetDirectoryName(final)!;
        var tmp = Path.Combine(folder, ".scout-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(final))
                throw new ScoutingException(ScoutingError.AlreadyExists,
                    $"a scouting file already exists for season {seasonId.Raw.ToString(CultureInfo.InvariantCulture)}: '{final}'. " +
                    "It is written once and never overwritten.");
            File.WriteAllBytes(tmp, Encoding.UTF8.GetBytes(text));
            File.Move(tmp, final, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            if (File.Exists(final) && ex is IOException && !File.Exists(tmp))
                throw new ScoutingException(ScoutingError.AlreadyExists,
                    $"a scouting file appeared for season {seasonId.Raw.ToString(CultureInfo.InvariantCulture)} while writing: '{final}'.", ex);
            throw new ScoutingException(ScoutingError.PersistFailed, $"could not write the scouting file '{final}' — {ex.Message}", ex);
        }
        return final;
    }

    /// <summary>The file's exact text. Records are ordered by person number here, inside the
    /// wall — PersonId deliberately has no ordering anywhere else.</summary>
    internal static string Encode(string historyId, string worldFingerprint, long seasonId, IReadOnlyList<ScoutingRecord> records)
    {
        GameLogSchemaV1.DecodeHistoryId(historyId);           // shape-checked by the log's own rules
        GameLogSchemaV1.DecodeWorldFingerprint(worldFingerprint);
        ValidateRecords(records, "write");
        var ci = CultureInfo.InvariantCulture;
        var body = new StringBuilder();
        body.Append("history ").Append(historyId).Append('\n');
        body.Append("world ").Append(worldFingerprint).Append('\n');
        body.Append("season ").Append(seasonId.ToString(ci)).Append('\n');
        var (t, s, p) = records.Count == 0 ? (0, 0, 0) : (records[0].Tiers.Count, records[0].Streaks.Count, records[0].Progress.Count);
        body.Append("counts ").Append(t.ToString(ci)).Append(' ').Append(s.ToString(ci)).Append(' ').Append(p.ToString(ci)).Append('\n');
        body.Append("records ").Append(records.Count.ToString(ci)).Append('\n');
        foreach (var r in records.OrderBy(r => r.Person.Raw))
        {
            body.Append(r.Person.Raw.ToString(ci)).Append(' ')
                .Append(r.DevSeed.ToString("x16", ci)).Append(' ')
                .Append(r.WorkEthic.ToString(ci)).Append(' ')
                .Append(r.ArrivalIq.ToString(ci)).Append(' ')
                .Append(r.ArrivalDiscipline.ToString(ci)).Append(' ')
                .Append(string.Join(',', r.Tiers.Select(x => x.ToString(ci)))).Append(' ')
                .Append(string.Join(',', r.Streaks.Select(x => x.ToString(ci)))).Append(' ')
                .Append(string.Join(',', r.Progress.Select(x => BitConverter.DoubleToInt64Bits(x).ToString("x16", ci))))
                .Append('\n');
        }
        var bodyText = body.ToString();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyText))).ToLowerInvariant();
        return MagicLine + "\n" + "version " + Version.ToString(ci) + "\n" + "sha256 " + digest + "\n" + bodyText;
    }

    // ── Reading ─────────────────────────────────────────────────────────────

    /// <summary>A complete, valid scouting file for exactly this career, world and season — or a
    /// refusal by name. Never a partial list.</summary>
    public static IReadOnlyList<ScoutingRecord> Read(string historyPath, ScoutingBindings bindings)
    {
        var path = PathFor(historyPath, bindings.SeasonId);
        if (!File.Exists(path))
            throw new ScoutingException(ScoutingError.Missing,
                $"season {bindings.SeasonId.ToString(CultureInfo.InvariantCulture)} of this career kept no scouting file at '{path}', " +
                "so its players' potential is unknown. Potential is never re-rolled — " +
                GameLogSchemaV1.OlderVersionSentence + " if this career predates the offseason camp.");
        string text;
        try { text = Encoding.UTF8.GetString(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ScoutingException(ScoutingError.PersistFailed, $"could not read the scouting file '{path}' — {ex.Message}", ex);
        }
        return Decode(text, bindings, path);
    }

    internal static IReadOnlyList<ScoutingRecord> Decode(string text, ScoutingBindings bindings, string path)
    {
        ScoutingException Damaged(string why) => new(ScoutingError.Damaged, $"the scouting file '{path}' is damaged: {why}.");
        var ci = CultureInfo.InvariantCulture;

        var nl1 = text.IndexOf('\n');
        if (nl1 < 0 || text[..nl1] != MagicLine) throw Damaged("it does not begin with the scouting magic");
        var nl2 = text.IndexOf('\n', nl1 + 1);
        if (nl2 < 0) throw Damaged("it ends before its version");
        var versionLine = text[(nl1 + 1)..nl2];
        if (!versionLine.StartsWith("version ", StringComparison.Ordinal)
            || !int.TryParse(versionLine[8..], NumberStyles.None, ci, out var version))
            throw Damaged("its version line is unreadable");
        if (version != Version)
            throw new ScoutingException(ScoutingError.UnsupportedVersion,
                $"the scouting file '{path}' is version {version.ToString(ci)}; this build reads version {Version.ToString(ci)} — " +
                GameLogSchemaV1.OlderVersionSentence + ".");
        var nl3 = text.IndexOf('\n', nl2 + 1);
        if (nl3 < 0) throw Damaged("it ends before its checksum");
        var shaLine = text[(nl2 + 1)..nl3];
        var body = text[(nl3 + 1)..];
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        if (shaLine != "sha256 " + digest) throw Damaged("its checksum does not match its contents");

        var lines = body.Split('\n');
        if (lines.Length < 6 || lines[^1].Length != 0) throw Damaged("it is truncated");
        string Field(int i, string key)
        {
            if (!lines[i].StartsWith(key + " ", StringComparison.Ordinal)) throw Damaged($"line '{key}' is missing");
            return lines[i][(key.Length + 1)..];
        }
        var hid = Field(0, "history");
        var world = Field(1, "world");
        if (!long.TryParse(Field(2, "season"), NumberStyles.None, ci, out var season)) throw Damaged("its season is unreadable");
        var counts = Field(3, "counts").Split(' ');
        if (counts.Length != 3 || !int.TryParse(counts[0], NumberStyles.None, ci, out var nt)
            || !int.TryParse(counts[1], NumberStyles.None, ci, out var ns) || !int.TryParse(counts[2], NumberStyles.None, ci, out var np))
            throw Damaged("its counts are unreadable");
        if (!int.TryParse(Field(4, "records"), NumberStyles.None, ci, out var n)) throw Damaged("its record count is unreadable");

        if (hid != bindings.HistoryId)
            throw new ScoutingException(ScoutingError.HistoryIdMismatch, $"the scouting file '{path}' belongs to another career.");
        if (world != bindings.WorldFingerprint)
            throw new ScoutingException(ScoutingError.WorldMismatch, $"the scouting file '{path}' belongs to another world.");
        if (season != bindings.SeasonId)
            throw new ScoutingException(ScoutingError.SeasonMismatch,
                $"the scouting file '{path}' is season {season.ToString(ci)}'s, not season {bindings.SeasonId.ToString(ci)}'s.");
        if (lines.Length != 5 + n + 1) throw Damaged($"it declares {n.ToString(ci)} records and holds {(lines.Length - 6).ToString(ci)}");

        var records = new List<ScoutingRecord>(n);
        for (var i = 0; i < n; i++)
        {
            var f = lines[5 + i].Split(' ');
            if (f.Length != 8) throw Damaged($"record {i.ToString(ci)} has {f.Length.ToString(ci)} fields");
            try
            {
                var person = long.Parse(f[0], NumberStyles.None, ci);
                if (person < 1) throw new FormatException("person");
                var seed = ulong.Parse(f[1], NumberStyles.AllowHexSpecifier, ci);
                var tiers = f[5].Length == 0 ? Array.Empty<int>() : f[5].Split(',').Select(x => int.Parse(x, NumberStyles.None, ci)).ToArray();
                var streaks = f[6].Length == 0 ? Array.Empty<int>() : f[6].Split(',').Select(x => int.Parse(x, NumberStyles.None, ci)).ToArray();
                var prog = f[7].Length == 0 ? Array.Empty<double>()
                    : f[7].Split(',').Select(x => BitConverter.Int64BitsToDouble(long.Parse(x, NumberStyles.AllowHexSpecifier, ci))).ToArray();
                if (tiers.Length != nt || streaks.Length != ns || prog.Length != np)
                    throw new FormatException("ragged record");
                records.Add(new ScoutingRecord(PersonId.FromRaw(person), seed,
                    int.Parse(f[2], NumberStyles.None, ci), int.Parse(f[3], NumberStyles.None, ci),
                    int.Parse(f[4], NumberStyles.None, ci), tiers, streaks, prog));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or HistoryException)
            {
                throw Damaged($"record {i.ToString(ci)} is unreadable ({ex.Message})");
            }
        }
        ValidateRecords(records, "read");
        return records;
    }

    private static void ValidateRecords(IReadOnlyList<ScoutingRecord> records, string when)
    {
        ScoutingException Bad(string why) => new(ScoutingError.InvalidRecord, $"scouting {when} refused: {why}.");
        var seen = new HashSet<long>();
        if (records.Count == 0) return;
        int nt = records[0].Tiers.Count, ns = records[0].Streaks.Count, np = records[0].Progress.Count;
        foreach (var r in records)
        {
            if (!r.Person.IsValid) throw Bad("a record without an issued person number");
            if (!seen.Add(r.Person.Raw)) throw Bad($"{r.Person} appears twice");
            if (r.Tiers.Count != nt || r.Streaks.Count != ns || r.Progress.Count != np) throw Bad($"{r.Person}'s record is ragged");
            if (r.Tiers.Any(t => t is < 0 or > 4)) throw Bad($"{r.Person} carries a tier outside 0..4");
            if (r.Streaks.Any(s => s < 0)) throw Bad($"{r.Person} carries a negative streak");
            if (r.Progress.Any(p => !double.IsFinite(p) || p < 0 || p >= 1)) throw Bad($"{r.Person} carries hidden progress outside [0, 1)");
            if (r.WorkEthic is < 1 or > 99) throw Bad($"{r.Person}'s work ethic is outside 1..99");
            if (r.ArrivalIq is < 0 or > 99 || r.ArrivalDiscipline is < 0 or > 99) throw Bad($"{r.Person}'s arrival ratings are outside 0..99");
        }
    }
}
