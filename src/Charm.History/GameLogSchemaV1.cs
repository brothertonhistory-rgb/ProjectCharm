using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Charm.History;

// ============================================================================
//  S90 — THE RETENTION LOG'S ON-DISK CONTRACT.
//
//  One season, one file: who was on every roster, and every game every one of
//  them played. Kept forever, at full detail.
//
//  ★ WHY A CUSTOM FIXED-WIDTH FORMAT, honestly. The size argument that used to
//  be given for this was MEASURED AND FOUND WRONG — compact JSON is roughly the
//  same size, and smaller if zeros are omitted, because a per-game row is mostly
//  zeros and single digits while a fixed-width row spends eight bytes on each.
//  What survives measurement:
//    1. Row n of block b sits at a COMPUTABLE offset. The future almanac will
//       build an index; a fixed-width row lets that index store integers and
//       resolve to bytes with no parsing.
//    2. The file size is exact arithmetic, so a structural check can assert it
//       to the byte (Phase 81 A9). No text format can do that.
//    3. Zero external dependencies, which all three projects still have.
//
//  ★ WHY THE CODEC LIVES IN THIS ASSEMBLY AND NOT THE HARNESS. Writing a
//  PersonId to disk needs its raw long. That accessor is `internal` and this
//  project grants no `InternalsVisibleTo` — that ABSENCE is S89's seam. A codec
//  in the harness could not compile without opening the wall, and opening it
//  unseals every guarantee Phase 80 asserts. So the seam decided where this
//  file lives; it was not a preference.
//
//  ★ WHAT THIS FORMAT DELIBERATELY DOES NOT KNOW. Nothing about basketball, and
//  nothing about THIS league. It validates that a school id is a non-negative
//  integer; it does not know which schools exist, how many players a pool holds,
//  or how long a roster is. Those are facts about one world, and a permanent
//  archive format must outlive any particular world. Contextual validation is
//  the harness's job (see GameLogReader's class header).
// ============================================================================

/// <summary>One man's stat line for one game — the durable row. Converted from
/// snapshot deltas at the season loop, never a reflection-driven mirror of the
/// in-memory season record: refactoring that record must not become a storage
/// migration.
///
/// <para>★ `GamesPlayed` is deliberately NOT here. It is row CARDINALITY — a man
/// played the games he has rows for. Storing it too would create a second copy of
/// the same fact, and two copies can disagree.</para>
///
/// <para>★ No rates, no percentages, no points. Every one of those is computed at
/// read time from these primitives (the counters-only ruling). A stored rate is a
/// stored rounding error, and it freezes a formula that will be improved.</para>
///
/// <para>★ S116 — ROW SCHEMA v2 adds `Started`: he was one of the five who tipped this
/// game. It is a FACT about the game, not a counter, so it is never summed into anything
/// but GS. On disk it is one more long after the 21 counters (0 or 1; the format refuses any
/// other value), so the row stays all-longs: 196 bytes, 26 fields. A v1 row (188 / 25) reads
/// back with `Started = false` — honestly false: v1 never recorded it.</para></summary>
public sealed record PerGameStatRowV1(
    PersonId PersonId,
    int SchoolId,
    int PoolId,
    int AcquisitionIndex,
    long Credits,
    long OffensiveCredits,
    long Fga, long Fgm,
    long Tpa, long Tpm,
    long Fta, long Ftm,
    long OReb, long DReb,
    long Ast, long Stl, long Blk, long To,
    long ShFoul, long NsFoul, long OffFoul,
    long FbBlk,
    long OpponentTwoPaOnFloor,
    long SecuredBoardsOnFloor,
    long OffensiveTeamFgmOnFloor,
    bool Started);

/// <summary>Where a man was listed on the floor. One byte on disk; the format
/// defines the three values and refuses any other.</summary>
public enum RosterPosition : byte
{
    Guard = 0,
    Wing  = 1,
    Big   = 2,
}

/// <summary>Who a man WAS in one season — written once, before the first game.
///
/// <para>★ WHY THIS SECTION EXISTS AT ALL. Game rows say what a man did and
/// nothing about him: not his name, not his position, not his ratings. And a man
/// who never got off the bench has no rows, so without this he would not appear
/// in the archive at all. A player card thirty years later needs both halves.</para>
///
/// <para>★ RATINGS ARE STAMPED AT THE START OF THE SEASON (ruled). Nobody
/// develops mid-season today, so the choice is currently inert; it is fixed now
/// because the FORMAT is fixed now. The reason is that a card should read as one
/// coherent thing — this is who he was, and this is what he did. Stamping at
/// season end would show what he grew into beside statistics he produced as
/// somebody else. When development lands, a session may add an end-of-season
/// entry as a NEW roster schema version; it may not redefine what this one
/// meant.</para>
///
/// <para>★ WHAT IS DELIBERATELY ABSENT: his ceiling. The generator computes a
/// latent card, a runway and an arrival stage for every player and the season
/// layer drops all of it. Emmett ruled it out of the archive — a ceiling is what
/// a man might have become, and this is a record of what happened. Arrival goes
/// with it, being a fraction OF that ceiling and meaningless without it. See
/// O-73: the engine gap is real and separate.</para></summary>
public record RosterEntryV1(
    PersonId PersonId,
    int SchoolId,
    int PoolId,
    int AcquisitionIndex,
    string Name,
    string Role,
    RosterPosition Position,
    bool IsStarter,
    short HierarchyRank,
    double ScoutRank,
    IReadOnlyList<short> Ratings);

/// <summary>★ S115 — ROSTER SCHEMA v2: everything v1 said about a man, plus the three facts
/// the next season needs to take him back off the file. Emmett's ruling 2 (2026-10-05): what
/// the file knows about a player at rest is his number, his ratings as they stand, his
/// position, his role, his class and his school — later layers ADD fields, never redefine
/// these.
///
/// <para>`Class` is his year in school as an ordinal (0 Fr, 1 So, 2 Jr, 3 Sr) — the format
/// pins the order and refuses any other value. `DefensivePlane` and `OffensiveRole` are the
/// generator's two labels that the season page and the divvy read; without them a man read
/// back from the file would be a different row from the one that was written.</para>
///
/// <para>The entry grows from 216 to 256 bytes. The writer emits v2 from S115 on; the
/// reader still accepts v1 (so host memory can read any season's game facts) and returns
/// a plain <see cref="RosterEntryV1"/> for it — which is exactly what lets the turnover
/// refuse a v1 roster by name: it has no class to advance.</para></summary>
public sealed record RosterEntryV2(
    PersonId PersonId,
    int SchoolId,
    int PoolId,
    int AcquisitionIndex,
    string Name,
    string Role,
    RosterPosition Position,
    bool IsStarter,
    short HierarchyRank,
    double ScoutRank,
    IReadOnlyList<short> Ratings,
    byte Class,
    double DefensivePlane,
    string OffensiveRole)
    : RosterEntryV1(PersonId, SchoolId, PoolId, AcquisitionIndex, Name, Role, Position,
                    IsStarter, HierarchyRank, ScoutRank, Ratings);

/// <summary>The game-level facts the engine actually produces, carried on the block
/// header so a future reader reconstructs everything attributed.
///
/// <para>★ `PossessionCount` is load-bearing and not decoration: it is the
/// DENOMINATOR for a man's minutes in this game (`credits x 40 / possessionCount`).
/// The season page's league-level conversion has no per-game equivalent, so without
/// this number a stored row cannot say how long anybody played.</para>
///
/// <para>★ S116 — BLOCK SCHEMA v2: what kind of game this was, three ways. The kind byte is
/// 0 a LEAGUE game (the conference slate), 2 a CONFERENCE TOURNAMENT game, 1 anything else
/// (holiday events, buy games). `IsConferenceGame` keeps its exact pre-S116 meaning — the
/// league slate only — because host memory and the standings rest on it; a conference
/// tournament game is NOT a league game (Emmett's ruling, 2026-10-06: it counts as a
/// conference game for the player, never toward the conference standings). A block-schema-1
/// file wrote kind 1 for a tournament game too; it reads back with
/// `IsConferenceTournamentGame = false`, and `GameLogV1.RecordsConferenceTournaments` says so.</para>
///
/// <para>★ S116 — BLOCK SCHEMA v2 also carries the game's DATE and whether it had a HOST
/// (Emmett, 2026-10-06: the game log needs the date and home / away / neutral). Both were
/// already known when the game was played and simply not written. With a host, the home
/// school was home (H) and the visitor away (A); with no host, both were neutral (N) and the
/// home/away ids are a box-score ordering only. The writer refuses a game with no date. A
/// block-schema-1 file reads both back as null — unknown, never guessed.</para></summary>
public sealed record GameBlockFactsV1(
    GameId GameId,
    int FixtureOrdinal,
    int HomeSchoolId,
    int AwaySchoolId,
    bool IsConferenceGame,
    int HomeScore,
    int AwayScore,
    short OvertimePeriods,
    long PossessionCount,
    bool IsConferenceTournamentGame = false,
    DateOnly? Date = null,
    bool? HasHost = null);

internal static class GameLogSchemaV1
{
    // ── Fixed sizes. Every one of these is asserted by Phase 81's A9. ────────
    internal const int FileHeaderSize   = 128;
    internal const int RosterHeaderSize = 32;
    internal const int RosterEntrySize  = 216;   // roster schema 1 (read-only since S115)
    /// <summary>★ S115 — roster schema 2: the 204 bytes v1 used, plus class (1), defensive
    /// plane (8) and offensive role (32) = 245, padded to 256 (11 reserved-zero bytes).</summary>
    internal const int RosterEntrySizeV2 = 256;
    internal const int RosterTrailerSize = 8;
    internal const int BlockHeaderSizeV1 = 48;   // block schema 1 (read-only since S116)
    /// <summary>★ S116 — block schema 2: v1's 48 bytes unchanged, then the date as a day number
    /// (int32), the site (1 byte: 0 hosted, 1 neutral) and 3 reserved-zero bytes = 56.</summary>
    internal const int BlockHeaderSize  = 56;
    /// <summary>★ S116 — row schema 2: 4 ids (20 bytes) + 21 counters + started, all longs = 196.</summary>
    internal const int RowSize          = 196;
    internal const int RowSizeV1        = 188;   // row schema 1 (read-only since S116)
    internal const int BlockTrailerSize = 8;
    internal const int FooterSize       = 64;

    internal const int RowFieldCount   = 26;   // 4 ids + 21 counters + started (row schema 2)
    internal const int RowFieldCountV1 = 25;   // 4 ids + 21 counters (row schema 1)
    internal const int RatingCount   = 38;

    internal const int NameBytes = 64;
    internal const int RoleBytes = 32;
    internal const int OffensiveRoleBytes = 32;   // S115, same encoding as Role

    internal const short FileFormatVersion   = 1;
    internal const short RosterSchemaVersionV1 = 1;
    /// <summary>★ S115 — what the writer emits. The reader accepts both.</summary>
    internal const short RosterSchemaVersion = 2;
    /// <summary>★ S115 — the largest class ordinal the format defines (0 Fr .. 3 Sr).</summary>
    internal const byte MaxClassOrdinal = 3;
    internal const short BlockSchemaVersionV1 = 1;
    /// <summary>★ S116 — what the writer emits: kind 0 league / 1 other / 2 conference tournament.</summary>
    internal const short BlockSchemaVersion  = 2;
    internal const short RowSchemaVersionV1  = 1;
    /// <summary>★ S116 — what the writer emits. The reader accepts both.</summary>
    internal const short RowSchemaVersion    = 2;

    internal static ReadOnlySpan<byte> Magic       => "CHRMGLOG"u8;
    internal static ReadOnlySpan<byte> RosterMarker => "RSTR"u8;
    internal static ReadOnlySpan<byte> BlockMarker  => "GBLK"u8;
    internal static ReadOnlySpan<byte> FooterMarker => "SFTR"u8;

    internal const int MaxRowCount   = 65535;
    internal const int MaxEntryCount = 65535;

    /// <summary>The world fingerprint's self-describing label. A future scheme defines
    /// sha256-v2 and a new FILE FORMAT VERSION; it never silently reinterprets these bytes.</summary>
    internal const string WorldFingerprintPrefix = "sha256-v1:";

    // ── Digests ─────────────────────────────────────────────────────────────
    //  The FIRST eight bytes of the SHA-256 digest, in digest order, treated as
    //  eight OPAQUE bytes — never read as a number, so there is no endianness
    //  question to get wrong on a future reader in another language.
    internal static void Checksum8(ReadOnlySpan<byte> payload, Span<byte> dest8)
    {
        Span<byte> full = stackalloc byte[32];
        SHA256.HashData(payload, full);
        full[..8].CopyTo(dest8);
    }

    // ── Hex ─────────────────────────────────────────────────────────────────
    //  Canonical decoding: each consecutive PAIR of hex characters, left to
    //  right, becomes one byte. Deliberately NOT Guid.ToByteArray(), whose
    //  mixed-endian layout a non-.NET reader should not have to reproduce.
    internal static bool TryDecodeHex(string hex, int expectedBytes, Span<byte> dest)
    {
        if (hex.Length != expectedBytes * 2) return false;
        for (var i = 0; i < expectedBytes; i++)
        {
            if (!TryNibble(hex[2 * i], out var hi) || !TryNibble(hex[2 * i + 1], out var lo)) return false;
            dest[i] = (byte)((hi << 4) | lo);
        }
        return true;

        static bool TryNibble(char c, out int v)
        {
            // Lowercase only: the canonical forms this project emits are lowercase,
            // and accepting both would let two spellings of one digest exist.
            if (c >= '0' && c <= '9') { v = c - '0'; return true; }
            if (c >= 'a' && c <= 'f') { v = c - 'a' + 10; return true; }
            v = 0; return false;
        }
    }

    /// <summary>Strip and validate the world fingerprint's label, then decode.
    /// ★ This is a WRITER-side check and can only be one: the binary file stores the
    /// 32 decoded bytes and no label, so a reader physically cannot see whether a
    /// label was ever there.</summary>
    internal static byte[] DecodeWorldFingerprint(string fingerprint)
    {
        if (fingerprint is null || !fingerprint.StartsWith(WorldFingerprintPrefix, StringComparison.Ordinal))
            throw new GameLogException(GameLogError.MalformedWorldFingerprint,
                $"world fingerprint must start with '{WorldFingerprintPrefix}' (got '{Trunc(fingerprint)}').");
        var hex = fingerprint[WorldFingerprintPrefix.Length..];
        var bytes = new byte[32];
        if (!TryDecodeHex(hex, 32, bytes))
            throw new GameLogException(GameLogError.MalformedWorldFingerprint,
                "world fingerprint must be 64 lowercase hex characters after the label.");
        return bytes;
    }

    internal static byte[] DecodeScheduleFingerprint(string fingerprint)
    {
        var bytes = new byte[32];
        if (fingerprint is null || !TryDecodeHex(fingerprint, 32, bytes))
            throw new GameLogException(GameLogError.MalformedScheduleFingerprint,
                "schedule fingerprint must be 64 lowercase hex characters, unlabelled.");
        return bytes;
    }

    internal static byte[] DecodeHistoryId(string historyId)
    {
        var bytes = new byte[16];
        if (historyId is null || !TryDecodeHex(historyId, 16, bytes))
            throw new GameLogException(GameLogError.MalformedHistoryId,
                "history id must be 32 lowercase hex characters.");
        return bytes;
    }

    private static string Trunc(string? s)
        => s is null ? "<null>" : s.Length <= 24 ? s : s[..24] + "…";

    // ── Fixed-width strings ─────────────────────────────────────────────────
    //  Zero-padded, strict UTF-8, no mandatory terminator.
    //
    //  ★ WHY NO TERMINATOR. The width is fixed and known, so the length is
    //  recoverable without one; requiring a terminator would spend a byte to make
    //  a full-width value illegal for nothing. The padding rule below rejects
    //  every ambiguous encoding a terminator rule would.
    //
    //  ★ OVERFLOW REFUSES, IT NEVER TRUNCATES, and it refuses BEFORE any file
    //  exists (the writer validates the whole roster in memory first). Truncating
    //  could split a multibyte character or make two men serialize identically —
    //  in an archive whose entire purpose is being the only record of who
    //  somebody was.
    internal static void WriteFixedString(string value, Span<byte> dest, string fieldName)
    {
        dest.Clear();
        if (string.IsNullOrEmpty(value)) return;
        if (value.Contains('\0'))
            throw new GameLogException(GameLogError.MalformedString,
                $"{fieldName} contains an embedded NUL.");
        var needed = Encoding.UTF8.GetByteCount(value);
        if (needed > dest.Length)
            throw new GameLogException(GameLogError.StringTooLong,
                $"{fieldName} needs {needed} encoded bytes but the field holds {dest.Length}; " +
                "the archive never truncates a name.");
        Encoding.UTF8.GetBytes(value, dest);
    }

    internal static string ReadFixedString(ReadOnlySpan<byte> src, string fieldName)
    {
        var end = src.IndexOf((byte)0);
        if (end < 0) end = src.Length;                 // full-width value, legal
        for (var i = end; i < src.Length; i++)
            if (src[i] != 0)
                throw new GameLogException(GameLogError.MalformedString,
                    $"{fieldName} has a nonzero byte after its zero padding began.");
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(src[..end]);
        }
        catch (DecoderFallbackException ex)
        {
            throw new GameLogException(GameLogError.MalformedString,
                $"{fieldName} is not valid UTF-8.", ex);
        }
    }

    // ── Little-endian primitives ────────────────────────────────────────────
    internal static void W64(Span<byte> d, ref int o, long v) { BinaryPrimitives.WriteInt64LittleEndian(d[o..], v); o += 8; }
    internal static void W32(Span<byte> d, ref int o, int v)  { BinaryPrimitives.WriteInt32LittleEndian(d[o..], v); o += 4; }
    internal static void W16(Span<byte> d, ref int o, short v){ BinaryPrimitives.WriteInt16LittleEndian(d[o..], v); o += 2; }
    internal static void WU16(Span<byte> d, ref int o, ushort v){ BinaryPrimitives.WriteUInt16LittleEndian(d[o..], v); o += 2; }
    internal static void W8(Span<byte> d, ref int o, byte v)  { d[o] = v; o += 1; }

    internal static long R64(ReadOnlySpan<byte> s, ref int o) { var v = BinaryPrimitives.ReadInt64LittleEndian(s[o..]); o += 8; return v; }
    internal static int  R32(ReadOnlySpan<byte> s, ref int o) { var v = BinaryPrimitives.ReadInt32LittleEndian(s[o..]); o += 4; return v; }
    internal static short R16(ReadOnlySpan<byte> s, ref int o){ var v = BinaryPrimitives.ReadInt16LittleEndian(s[o..]); o += 2; return v; }
    internal static ushort RU16(ReadOnlySpan<byte> s, ref int o){ var v = BinaryPrimitives.ReadUInt16LittleEndian(s[o..]); o += 2; return v; }
    internal static byte R8(ReadOnlySpan<byte> s, ref int o)  { var v = s[o]; o += 1; return v; }

    /// <summary>A double written as its raw little-endian bit pattern, with -0.0
    /// normalised to +0.0 so a permanent golden never depends on a sign bit the
    /// source does not distinguish. NaN and infinity refuse.</summary>
    internal static void WDouble(Span<byte> d, ref int o, double v, string fieldName)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            throw new GameLogException(GameLogError.NonFiniteValue,
                $"{fieldName} must be finite (got {v.ToString(CultureInfo.InvariantCulture)}).");
        if (v == 0.0) v = 0.0;                          // collapses -0.0 to +0.0
        BinaryPrimitives.WriteDoubleLittleEndian(d[o..], v); o += 8;
    }

    internal static double RDouble(ReadOnlySpan<byte> s, ref int o, string fieldName)
    {
        var v = BinaryPrimitives.ReadDoubleLittleEndian(s[o..]); o += 8;
        if (double.IsNaN(v) || double.IsInfinity(v))
            throw new GameLogException(GameLogError.NonFiniteValue, $"{fieldName} is not finite.");
        return v;
    }

    /// <summary>Reserved bytes are validated as zero EVERYWHERE they appear. A
    /// reserved field quietly carrying data is how a format grows a second, undocumented
    /// meaning that no version number describes.</summary>
    internal static void RequireZero(ReadOnlySpan<byte> s, ref int o, int count, string where)
    {
        for (var i = 0; i < count; i++)
            if (s[o + i] != 0)
                throw new GameLogException(GameLogError.NonZeroReserved,
                    $"reserved bytes in {where} are not zero.");
        o += count;
    }

    internal static void Skip(ref int o, int count) => o += count;
}
