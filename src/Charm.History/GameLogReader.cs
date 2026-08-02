using System.Globalization;
using System.Security.Cryptography;

namespace Charm.History;

// ============================================================================
//  S90 — READING A RETENTION LOG BACK.
//
//  ★ TWO MODES, AND THEY ARE SEPARATE METHODS ON PURPOSE. `ReadFinalized` is
//  all-or-nothing: a complete valid file, or a classified refusal, and NEVER
//  partial data. `ReadInProgressPrefix` returns the complete valid prefix of a
//  crashed file, with a status saying why it stopped. One permissive reader
//  steered by the filename would be easier to call by accident, and the accident
//  is silently treating a truncated career as a complete one.
//
//  ★ WHAT THIS READER CAN AND CANNOT CHECK — the intrinsic/contextual line.
//  INTRINSIC is everything provable from the file plus the bindings the caller
//  hands in: magic, versions, the three digests, checksums, ordering, uniqueness,
//  domains, footer counts, EOF, and every row naming somebody in this file's own
//  roster section. All of that is still provable in thirty years by a program
//  that knows nothing about basketball.
//  CONTEXTUAL is everything needing the live season: that a school id is a real
//  school, that a pool id belongs to the current population, that the blocks match
//  the schedule fixture for fixture. This reader deliberately CANNOT do those, and
//  it does not pretend to — a permanent archive format must outlive the league it
//  was written from, so no stock-world constant is compiled in here.
//
//  ★ THE FOOTER'S COUNTS ARE VALIDATED, NEVER TRUSTED. The reader recomputes the
//  block count and the summed row count from the bytes it decoded and refuses on
//  disagreement. A stored count that is only ever read back is a comment.
// ============================================================================

/// <summary>Why a prefix read stopped. `Complete` means the payload ended in a valid
/// footer — which in an `.inprogress` file is the crash between flush and rename.</summary>
public enum GameLogPrefixStatus
{
    /// <summary>Every block decoded and the payload ended exactly at a block boundary.</summary>
    OpenAtBlockBoundary,
    /// <summary>Physical EOF partway through a section. The prefix before it is sound.</summary>
    IncompleteTail,
    /// <summary>The payload ends in a valid footer but the file was never renamed.</summary>
    FinalizedButNotPublished,
}

public sealed record GameLogBlockV1(GameBlockFactsV1 Facts, IReadOnlyList<PerGameStatRowV1> Rows);

public sealed record GameLogV1(
    long SeasonId,
    IReadOnlyList<RosterEntryV1> Roster,
    IReadOnlyList<GameLogBlockV1> Blocks)
{
    public long TotalRowCount => Blocks.Sum(b => (long)b.Rows.Count);
}

public sealed record GameLogPrefixV1(GameLogV1 Log, GameLogPrefixStatus Status);

/// <summary>The bindings a caller must supply. A log proves it belongs to this career,
/// this world and this season, or it refuses — it cannot know those things alone.</summary>
public sealed record GameLogBindings(string HistoryId, string WorldFingerprint, long SeasonId,
                                     string? ScheduleFingerprint = null);

public static class GameLogReader
{
    /// <summary>A complete, valid, finalized log — or an exception. Never a prefix.</summary>
    public static GameLogV1 ReadFinalized(string path, GameLogBindings bindings)
    {
        var bytes = ReadAll(path);
        var (log, consumed, footer) = Decode(bytes, bindings, requireFooter: true);
        if (footer is null)
            throw new GameLogException(GameLogError.MissingFooter,
                $"'{path}' is a finalized log with no season footer; it is not complete.");
        if (consumed != bytes.Length)
            throw new GameLogException(GameLogError.TrailingData,
                $"'{path}' has {bytes.Length - consumed} bytes after the season footer.");
        return log;
    }

    /// <summary>The complete valid block prefix of a crashed file, and why it stopped.</summary>
    public static GameLogPrefixV1 ReadInProgressPrefix(string path, GameLogBindings bindings)
    {
        var bytes = ReadAll(path);
        var (log, consumed, footer) = Decode(bytes, bindings, requireFooter: false);
        var status = footer is not null
            ? GameLogPrefixStatus.FinalizedButNotPublished
            : consumed == bytes.Length
                ? GameLogPrefixStatus.OpenAtBlockBoundary
                : GameLogPrefixStatus.IncompleteTail;
        if (footer is not null && consumed != bytes.Length)
            throw new GameLogException(GameLogError.TrailingData,
                $"'{path}' has bytes after its season footer.");
        return new GameLogPrefixV1(log, status);
    }

    private static byte[] ReadAll(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameLogException(GameLogError.LogReadFailed,
                $"could not read the retention log '{path}' — {ex.Message}", ex);
        }
    }

    // ── The decoder ─────────────────────────────────────────────────────────

    private sealed record Footer(int GameCount, long RowCount);

    private static (GameLogV1 Log, int Consumed, Footer? Footer) Decode(
        byte[] bytes, GameLogBindings bindings, bool requireFooter)
    {
        var s = new ReadOnlySpan<byte>(bytes);
        if (s.Length < GameLogSchemaV1.FileHeaderSize)
            throw new GameLogException(GameLogError.IncompleteTail,
                "the file is shorter than a retention log header.");

        var seasonId = DecodeFileHeader(s, bindings);

        // Everything after the header is the digest's payload region.
        var payloadStart = GameLogSchemaV1.FileHeaderSize;
        var o = payloadStart;

        var roster = DecodeRosterSection(s, ref o, out var rosterPersons);

        var blocks = new List<GameLogBlockV1>();
        var seenGames = new HashSet<long>();
        Footer? footer = null;
        var expectedOrdinal = 0;

        while (true)
        {
            if (o == s.Length) break;                       // clean block boundary
            if (s.Length - o < 4)
                { if (requireFooter) throw Incomplete("a section marker"); break; }

            var marker = s.Slice(o, 4);
            if (marker.SequenceEqual(GameLogSchemaV1.FooterMarker))
            {
                if (s.Length - o < GameLogSchemaV1.FooterSize)
                    { if (requireFooter) throw Incomplete("the season footer"); break; }
                footer = DecodeFooter(s, ref o);
                // Anything after a footer is malformed, not a tail.
                if (o != s.Length)
                {
                    if (s.Length - o >= 4 && (s.Slice(o, 4).SequenceEqual(GameLogSchemaV1.FooterMarker)
                                           || s.Slice(o, 4).SequenceEqual(GameLogSchemaV1.BlockMarker)))
                        throw new GameLogException(GameLogError.MalformedStructure,
                            "a second footer or a game block follows the season footer.");
                    throw new GameLogException(GameLogError.TrailingData,
                        $"{s.Length - o} bytes follow the season footer.");
                }
                break;
            }
            if (!marker.SequenceEqual(GameLogSchemaV1.BlockMarker))
                throw new GameLogException(GameLogError.MalformedStructure,
                    $"expected a game block or the season footer at byte {o}.");

            if (s.Length - o < GameLogSchemaV1.BlockHeaderSize)
                { if (requireFooter) throw Incomplete("a game block header"); break; }

            var probe = o;
            var block = DecodeBlock(s, ref probe, rosterPersons, expectedOrdinal, seenGames, requireFooter);
            if (block is null) break;                       // physical tail in .inprogress
            o = probe;
            blocks.Add(block);
            expectedOrdinal++;
        }

        if (requireFooter && footer is null)
            return (new GameLogV1(seasonId, roster, blocks), o, null);

        if (footer is not null)
        {
            // Validated, never trusted.
            if (footer.GameCount != blocks.Count)
                throw new GameLogException(GameLogError.FooterGameCountMismatch,
                    $"the footer claims {footer.GameCount} games; {blocks.Count} decoded.");
            var rows = blocks.Sum(b => (long)b.Rows.Count);
            if (footer.RowCount != rows)
                throw new GameLogException(GameLogError.FooterRowCountMismatch,
                    $"the footer claims {footer.RowCount} rows; {rows} decoded.");
            var digestEnd = o - GameLogSchemaV1.FooterSize;
            Span<byte> actual = stackalloc byte[32];
            SHA256.HashData(s[payloadStart..digestEnd], actual);
            var stored = s.Slice(digestEnd + 16, 32);
            if (!actual.SequenceEqual(stored))
                throw new GameLogException(GameLogError.PayloadDigestMismatch,
                    "the season footer's payload digest does not match the file's payload.");
        }

        return (new GameLogV1(seasonId, roster, blocks), o, footer);

        static GameLogException Incomplete(string what)
            => new(GameLogError.IncompleteTail, $"the file ends partway through {what}.");
    }

    private static long DecodeFileHeader(ReadOnlySpan<byte> s, GameLogBindings b)
    {
        var o = 0;
        if (!s[..8].SequenceEqual(GameLogSchemaV1.Magic))
            throw new GameLogException(GameLogError.WrongMagic, "this is not a Charm retention log.");
        o = 8;
        var fileV   = GameLogSchemaV1.R16(s, ref o);
        var blockV  = GameLogSchemaV1.R16(s, ref o);
        var rowV    = GameLogSchemaV1.R16(s, ref o);
        var hdrSize = GameLogSchemaV1.R16(s, ref o);
        if (fileV != GameLogSchemaV1.FileFormatVersion || blockV != GameLogSchemaV1.BlockSchemaVersion
            || rowV != GameLogSchemaV1.RowSchemaVersion || hdrSize != GameLogSchemaV1.FileHeaderSize)
            throw new GameLogException(GameLogError.UnsupportedLogVersion,
                $"unsupported log versions (file {fileV}, block {blockV}, row {rowV}, header {hdrSize}).");

        var hid = GameLogSchemaV1.DecodeHistoryId(b.HistoryId);
        if (!s.Slice(o, 16).SequenceEqual(hid))
            throw new GameLogException(GameLogError.HistoryIdMismatch,
                "this log belongs to a different career lineage.");
        o += 16;

        var world = GameLogSchemaV1.DecodeWorldFingerprint(b.WorldFingerprint);
        if (!s.Slice(o, 32).SequenceEqual(world))
            throw new GameLogException(GameLogError.WorldDigestMismatch,
                "this log was written against a different world.");
        o += 32;

        var seasonId = GameLogSchemaV1.R64(s, ref o);
        if (seasonId != b.SeasonId)
            throw new GameLogException(GameLogError.SeasonIdMismatch,
                $"this log is season {seasonId}, not season {b.SeasonId}.");

        if (b.ScheduleFingerprint is not null)
        {
            var sched = GameLogSchemaV1.DecodeScheduleFingerprint(b.ScheduleFingerprint);
            if (!s.Slice(o, 32).SequenceEqual(sched))
                throw new GameLogException(GameLogError.ScheduleDigestMismatch,
                    "this log was written against a different schedule.");
        }
        o += 32;

        var rowSize = GameLogSchemaV1.R32(s, ref o);
        var fields  = GameLogSchemaV1.R32(s, ref o);
        if (rowSize != GameLogSchemaV1.RowSize || fields != GameLogSchemaV1.RowFieldCount)
            throw new GameLogException(GameLogError.UnsupportedLogVersion,
                $"the header declares a {rowSize}-byte row of {fields} fields; this build writes " +
                $"{GameLogSchemaV1.RowSize} of {GameLogSchemaV1.RowFieldCount}.");
        GameLogSchemaV1.RequireZero(s, ref o, 16, "the file header");
        return seasonId;
    }

    private static IReadOnlyList<RosterEntryV1> DecodeRosterSection(
        ReadOnlySpan<byte> s, ref int o, out HashSet<long> persons)
    {
        var sectionStart = o;
        if (s.Length - o < GameLogSchemaV1.RosterHeaderSize)
            throw new GameLogException(GameLogError.IncompleteTail, "the file ends inside the roster header.");
        if (!s.Slice(o, 4).SequenceEqual(GameLogSchemaV1.RosterMarker))
            throw new GameLogException(GameLogError.MalformedStructure, "the roster section marker is missing.");
        var p = o + 4;
        var schemaV  = GameLogSchemaV1.R16(s, ref p);
        var entrySize = GameLogSchemaV1.R16(s, ref p);
        var count     = GameLogSchemaV1.R32(s, ref p);
        var ratings   = GameLogSchemaV1.R16(s, ref p);
        if (schemaV != GameLogSchemaV1.RosterSchemaVersion || entrySize != GameLogSchemaV1.RosterEntrySize
            || ratings != GameLogSchemaV1.RatingCount)
            throw new GameLogException(GameLogError.UnsupportedLogVersion,
                $"unsupported roster schema (version {schemaV}, entry {entrySize}, ratings {ratings}).");
        if (count <= 0 || count > GameLogSchemaV1.MaxEntryCount)
            throw new GameLogException(GameLogError.DomainViolation,
                $"roster entry count {count} is outside 1..{GameLogSchemaV1.MaxEntryCount}.");
        GameLogSchemaV1.RequireZero(s, ref p, 2, "the roster header");
        GameLogSchemaV1.RequireZero(s, ref p, 16, "the roster header");

        // Checked arithmetic BEFORE any allocation — a hostile count must never
        // become an allocation request.
        long need;
        try
        {
            need = checked(GameLogSchemaV1.RosterHeaderSize
                         + (long)count * GameLogSchemaV1.RosterEntrySize
                         + GameLogSchemaV1.RosterTrailerSize);
        }
        catch (OverflowException ex)
        {
            throw new GameLogException(GameLogError.DomainViolation, "roster size arithmetic overflows.", ex);
        }
        if (s.Length - sectionStart < need)
            throw new GameLogException(GameLogError.IncompleteTail,
                "the file ends inside the roster section.");

        var entriesStart = sectionStart + GameLogSchemaV1.RosterHeaderSize;
        var trailerAt = entriesStart + count * GameLogSchemaV1.RosterEntrySize;
        Span<byte> expect = stackalloc byte[8];
        GameLogSchemaV1.Checksum8(s[sectionStart..trailerAt], expect);
        if (!s.Slice(trailerAt, 8).SequenceEqual(expect))
            throw new GameLogException(GameLogError.RosterChecksumMismatch,
                "the roster section's checksum does not match its bytes. This section is the ONLY " +
                "record of names, ratings and men who never played, so a mismatch is fatal everywhere.");

        var list = new List<RosterEntryV1>(count);
        persons = new HashSet<long>(count);
        long previous = 0;
        for (var i = 0; i < count; i++)
        {
            var q = entriesStart + i * GameLogSchemaV1.RosterEntrySize;
            var start = q;
            var raw = GameLogSchemaV1.R64(s, ref q);
            if (raw < 1)
                throw new GameLogException(GameLogError.DomainViolation,
                    $"roster entry {i} has person id {raw}; zero is not a person.");
            if (raw <= previous)
                throw new GameLogException(GameLogError.InvalidRosterEntry,
                    $"roster entry {i} breaks strict ascending order ({raw} after {previous}).");
            previous = raw;
            persons.Add(raw);

            var schoolId = GameLogSchemaV1.R32(s, ref q);
            var poolId   = GameLogSchemaV1.R32(s, ref q);
            var acq      = GameLogSchemaV1.R32(s, ref q);
            if (schoolId < 0 || poolId < 0 || acq <= 0)
                throw new GameLogException(GameLogError.DomainViolation,
                    $"roster entry {i} carries a context id outside the format's domain.");

            var name = GameLogSchemaV1.ReadFixedString(s.Slice(q, GameLogSchemaV1.NameBytes), $"roster entry {i} name");
            q += GameLogSchemaV1.NameBytes;
            var role = GameLogSchemaV1.ReadFixedString(s.Slice(q, GameLogSchemaV1.RoleBytes), $"roster entry {i} role");
            q += GameLogSchemaV1.RoleBytes;

            var posByte = GameLogSchemaV1.R8(s, ref q);
            if (posByte > (byte)RosterPosition.Big)
                throw new GameLogException(GameLogError.InvalidPosition,
                    $"roster entry {i} has position byte {posByte}, which the format does not define.");
            var starter = GameLogSchemaV1.R8(s, ref q);
            if (starter > 1)
                throw new GameLogException(GameLogError.DomainViolation,
                    $"roster entry {i} has starter byte {starter}; it is exactly 0 or 1.");
            var rank = GameLogSchemaV1.R16(s, ref q);
            if (rank < 1 || rank > 10)
                throw new GameLogException(GameLogError.DomainViolation,
                    $"roster entry {i} hierarchy rank {rank} is outside 1..10.");
            var scout = GameLogSchemaV1.RDouble(s, ref q, $"roster entry {i} scoutRank");

            var vals = new short[GameLogSchemaV1.RatingCount];
            for (var r = 0; r < vals.Length; r++)
            {
                vals[r] = GameLogSchemaV1.R16(s, ref q);
                if (vals[r] < 0 || vals[r] > 99)
                    throw new GameLogException(GameLogError.DomainViolation,
                        $"roster entry {i} rating slot {r} is {vals[r]}, outside the authored 0..99 scale.");
            }
            GameLogSchemaV1.RequireZero(s, ref q, start + GameLogSchemaV1.RosterEntrySize - q, $"roster entry {i}");

            list.Add(new RosterEntryV1(PersonId.FromRaw(raw), schoolId, poolId, acq, name, role,
                                       (RosterPosition)posByte, starter == 1, rank, scout, vals));
        }

        o = trailerAt + GameLogSchemaV1.RosterTrailerSize;
        return list;
    }

    private static GameLogBlockV1? DecodeBlock(
        ReadOnlySpan<byte> s, ref int o, HashSet<long> rosterPersons,
        int expectedOrdinal, HashSet<long> seenGames, bool requireFooter)
    {
        var blockStart = o;
        var p = o + 4;                              // marker already matched
        var gameId  = GameLogSchemaV1.R64(s, ref p);
        var ordinal = GameLogSchemaV1.R32(s, ref p);
        var home    = GameLogSchemaV1.R32(s, ref p);
        var away    = GameLogSchemaV1.R32(s, ref p);
        var kind    = GameLogSchemaV1.R8(s, ref p);
        GameLogSchemaV1.RequireZero(s, ref p, 1, "a game block header");
        var rowCount = GameLogSchemaV1.RU16(s, ref p);
        var hScore  = GameLogSchemaV1.R32(s, ref p);
        var aScore  = GameLogSchemaV1.R32(s, ref p);
        var ot      = GameLogSchemaV1.R16(s, ref p);
        GameLogSchemaV1.RequireZero(s, ref p, 2, "a game block header");
        var poss    = GameLogSchemaV1.R64(s, ref p);

        // Domain first, and only then any length arithmetic.
        if (gameId < 1)
            throw new GameLogException(GameLogError.DomainViolation, $"block {ordinal} has game id {gameId}.");
        if (kind > 1)
            throw new GameLogException(GameLogError.DomainViolation, $"block {ordinal} has fixture kind {kind}.");
        if (rowCount == 0)
            throw new GameLogException(GameLogError.DomainViolation,
                $"block {ordinal} has zero rows; a completed game always has a participant.");
        if (ot < 0 || poss <= 0 || home < 0 || away < 0)
            throw new GameLogException(GameLogError.DomainViolation, $"block {ordinal} carries an out-of-domain fact.");
        if (ordinal != expectedOrdinal)
            throw new GameLogException(GameLogError.BlockOutOfOrder,
                $"expected fixture ordinal {expectedOrdinal}, found {ordinal}; ordinals are contiguous from zero.");
        if (!seenGames.Add(gameId))
            throw new GameLogException(GameLogError.BlockOutOfOrder,
                $"game id {gameId} appears twice in one season log.");

        long need;
        try
        {
            need = checked(GameLogSchemaV1.BlockHeaderSize
                         + (long)rowCount * GameLogSchemaV1.RowSize
                         + GameLogSchemaV1.BlockTrailerSize);
        }
        catch (OverflowException ex)
        {
            throw new GameLogException(GameLogError.DomainViolation, "block size arithmetic overflows.", ex);
        }
        if (s.Length - blockStart < need)
        {
            // Physical EOF. In a finalized file that is fatal; in an .inprogress it is
            // the tail, and the prefix before it stands.
            if (requireFooter)
                throw new GameLogException(GameLogError.IncompleteTail,
                    $"the file ends partway through block {ordinal}.");
            return null;
        }

        var rowsAt = blockStart + GameLogSchemaV1.BlockHeaderSize;
        var trailerAt = rowsAt + rowCount * GameLogSchemaV1.RowSize;
        Span<byte> expect = stackalloc byte[8];
        GameLogSchemaV1.Checksum8(s[blockStart..trailerAt], expect);
        if (!s.Slice(trailerAt, 8).SequenceEqual(expect))
            // ★ A COMPLETE block with a bad checksum is corruption, and it is fatal even
            // in an .inprogress file. Only PHYSICAL truncation is ever tolerated.
            throw new GameLogException(GameLogError.BlockChecksumMismatch,
                $"block {ordinal} is complete but its checksum does not match its bytes.");

        var rows = new List<PerGameStatRowV1>(rowCount);
        var seenPersons = new HashSet<long>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var q = rowsAt + i * GameLogSchemaV1.RowSize;
            var raw = GameLogSchemaV1.R64(s, ref q);
            if (raw < 1)
                throw new GameLogException(GameLogError.DomainViolation, $"a row in block {ordinal} has person id {raw}.");
            if (!seenPersons.Add(raw))
                throw new GameLogException(GameLogError.InvalidRow,
                    $"person {raw} appears twice in block {ordinal}.");
            if (!rosterPersons.Contains(raw))
                throw new GameLogException(GameLogError.RowPersonNotInRoster,
                    $"person {raw} has a row in block {ordinal} but is absent from this season's roster section.");
            var schoolId = GameLogSchemaV1.R32(s, ref q);
            var poolId   = GameLogSchemaV1.R32(s, ref q);
            var acq      = GameLogSchemaV1.R32(s, ref q);
            if (schoolId < 0 || poolId < 0 || acq <= 0)
                throw new GameLogException(GameLogError.DomainViolation,
                    $"a row in block {ordinal} carries a context id outside the format's domain.");
            var c = new long[21];
            for (var k = 0; k < c.Length; k++)
            {
                c[k] = GameLogSchemaV1.R64(s, ref q);
                if (c[k] < 0)
                    throw new GameLogException(GameLogError.DomainViolation,
                        $"a row in block {ordinal} has a negative counter at slot {k}.");
            }
            if (c[0] <= 0)
                throw new GameLogException(GameLogError.InvalidRow,
                    $"person {raw} has a row in block {ordinal} with no floor credit.");
            rows.Add(new PerGameStatRowV1(PersonId.FromRaw(raw), schoolId, poolId, acq,
                c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8], c[9], c[10],
                c[11], c[12], c[13], c[14], c[15], c[16], c[17], c[18], c[19], c[20]));
        }

        o = trailerAt + GameLogSchemaV1.BlockTrailerSize;
        return new GameLogBlockV1(
            new GameBlockFactsV1(GameId.FromRaw(gameId), ordinal, home, away, kind == 0,
                                 hScore, aScore, ot, poss),
            rows);
    }

    private static Footer DecodeFooter(ReadOnlySpan<byte> s, ref int o)
    {
        var p = o + 4;
        var games = GameLogSchemaV1.R32(s, ref p);
        var rows  = GameLogSchemaV1.R64(s, ref p);
        if (games < 0 || rows < 0)
            throw new GameLogException(GameLogError.DomainViolation, "the season footer carries a negative count.");
        p += 32;                                   // digest, checked by the caller
        GameLogSchemaV1.RequireZero(s, ref p, 16, "the season footer");
        o += GameLogSchemaV1.FooterSize;
        return new Footer(games, rows);
    }
}
