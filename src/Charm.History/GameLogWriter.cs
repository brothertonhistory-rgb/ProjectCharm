using System.Globalization;
using System.Security.Cryptography;

namespace Charm.History;

// ============================================================================
//  S90 — WRITING ONE SEASON'S RETENTION LOG.
//
//  ★ THE DURABLE UNIT IS ONE GAME, NOT ONE ROW. A game's rows are built and
//  validated in memory, serialized into one complete buffer, and appended in a
//  single write. That single write is NOT claimed to be physically atomic —
//  safety comes from the FRAMING: a physically partial write leaves an
//  incomplete final block, and the reader refuses to expose an incomplete block
//  as complete. A structurally valid half-game cannot exist.
//
//  ★ THE ROSTER IS FULLY VALIDATED BEFORE THE FILE EXISTS. Names, encodings,
//  ordering, uniqueness, every numeric domain, and the exact serialized size —
//  all checked while the writer has touched nothing. Only then does it take the
//  lock and create the file. The alternative (create, then discover an overlong
//  name, then delete) leaves a window in which a half-valid artifact is on disk
//  and a crash strands it. Prevalidation makes roster publication a single
//  deliberate transition.
//
//  ★ THE LOCK IS TAKEN BEFORE EXISTENCE IS CHECKED. Checking first IS the
//  creation race — two processes both see "no log here" and both create one.
//  Same rule S89's history lock follows, for the same reason.
//
//  ★ AN EXISTING LOG IS A REFUSAL, NEVER A RESUME. S89 does not persist the
//  schedule, so a second process cannot verify that the prefix it found belongs
//  to the season it is about to write. Appending to a log you cannot verify is
//  how two different seasons end up interleaved in one file. Resume is a future
//  session's feature; today it stops.
// ============================================================================

/// <summary>The writer's lifecycle. Every transition is checked; an operation in the
/// wrong state refuses rather than doing something approximately right.</summary>
public enum GameLogWriterState
{
    /// <summary>Roster built and validated in memory. No filesystem contact yet.</summary>
    RosterPrepared,
    /// <summary>Lock held, `.inprogress` created, header written.</summary>
    FileCreated,
    /// <summary>Roster section and its checksum are on disk.</summary>
    RosterWritten,
    /// <summary>Accepting game blocks.</summary>
    GamesAppending,
    /// <summary>Footer written and the file renamed to `.log`. Immutable.</summary>
    Finalized,
}

public sealed class GameLogWriter : IDisposable
{
    private readonly string _inProgressPath;
    private readonly string _finalPath;
    private readonly string _lockPath;
    private readonly byte[] _historyId;
    private readonly byte[] _worldDigest;
    private readonly byte[] _scheduleDigest;
    private readonly long _seasonId;

    private FileStream? _lockHandle;
    private FileStream? _file;
    private readonly IncrementalHash _payload = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    private readonly HashSet<long> _rosterPersons = new();
    private readonly HashSet<long> _seenGameIds = new();
    private int _nextOrdinal;
    private int _blockCount;
    private long _rowCount;

    public GameLogWriterState State { get; private set; }

    /// <summary>Where the finalized log will land. Exposed so the suite and the
    /// season page can name it without re-deriving the path rule.</summary>
    public string FinalPath => _finalPath;

    private GameLogWriter(string inProgress, string final, string lockPath,
                          byte[] historyId, byte[] worldDigest, byte[] scheduleDigest, long seasonId)
    {
        _inProgressPath = inProgress;
        _finalPath = final;
        _lockPath = lockPath;
        _historyId = historyId;
        _worldDigest = worldDigest;
        _scheduleDigest = scheduleDigest;
        _seasonId = seasonId;
        State = GameLogWriterState.RosterPrepared;
    }

    // ── Paths ───────────────────────────────────────────────────────────────

    /// <summary>Strip ONLY the final `.json` and append `.gamelog`:
    /// `career.history.json` -> `career.history.gamelog/`. A history named without
    /// `.json` gets `.gamelog` appended whole.
    ///
    /// <para>★ The folder is named for the history FILE, which is what stops two
    /// careers sitting side by side from sharing a log directory and colliding on
    /// `season-1`.</para></summary>
    public static string LogFolderFor(string historyPath)
    {
        var full = Path.GetFullPath(historyPath);
        if (full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            full = full[..^5];
        return full + ".gamelog";
    }

    public static string FinalPathFor(string historyPath, long seasonId)
        => Path.Combine(LogFolderFor(historyPath),
                        "season-" + seasonId.ToString(CultureInfo.InvariantCulture) + ".log");

    // ── Creation ────────────────────────────────────────────────────────────

    /// <summary>Validate the whole roster, then take the lock, then create the file, then
    /// publish the roster section. In that order, and the order is the contract.</summary>
    public static GameLogWriter Create(
        string historyPath,
        string historyId,
        string worldFingerprint,
        string scheduleFingerprint,
        SeasonId seasonId,
        IReadOnlyList<RosterEntryV1> roster)
    {
        IdentityGuardSeason(seasonId);

        // ── RosterPrepared: nothing has been touched on disk yet. ───────────
        var hid   = GameLogSchemaV1.DecodeHistoryId(historyId);
        var world = GameLogSchemaV1.DecodeWorldFingerprint(worldFingerprint);
        var sched = GameLogSchemaV1.DecodeScheduleFingerprint(scheduleFingerprint);

        if (roster is null || roster.Count == 0)
            throw new GameLogException(GameLogError.InvalidRosterEntry,
                "a season's roster section may not be empty.");
        if (roster.Count > GameLogSchemaV1.MaxEntryCount)
            throw new GameLogException(GameLogError.DomainViolation,
                $"roster of {roster.Count} exceeds the {GameLogSchemaV1.MaxEntryCount} the format holds.");

        // ★ Sorted HERE, not by the caller. PersonId deliberately has no ordering — S89
        // refuses to let domain code write "person 4001 is older than person 4000" — so the
        // harness physically cannot hand these over sorted. Ordering is a serialization
        // concern, and this assembly is the only one that may see the raw number.
        var ordered = roster.OrderBy(e => e.PersonId.IsValid ? e.PersonId.Raw : -1).ToList();
        var rosterBytes = SerializeRosterSection(ordered, out var persons);

        // ── FileCreated ─────────────────────────────────────────────────────
        var folder = LogFolderFor(historyPath);
        var season = seasonId.Raw;
        var name   = "season-" + season.ToString(CultureInfo.InvariantCulture);
        var final  = Path.Combine(folder, name + ".log");
        var prog   = Path.Combine(folder, name + ".log.inprogress");
        var lockPath = Path.Combine(folder, name + ".log.lock");

        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameLogException(GameLogError.LogPersistFailed,
                $"could not create the log folder '{folder}' — {ex.Message}", ex);
        }

        FileStream lockHandle;
        try
        {
            // The sidecar's EXISTENCE is never a refusal — only failure to obtain the
            // exclusive handle is. A crash-orphaned empty .lock is silently reused,
            // exactly as S89's history lock behaves.
            lockHandle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameLogException(GameLogError.LogLockUnavailable,
                $"could not take the season log lock '{lockPath}' — another run may hold it. {ex.Message}", ex);
        }

        var w = new GameLogWriter(prog, final, lockPath, hid, world, sched, season);
        try
        {
            // Existence is checked ONLY under the lock.
            if (File.Exists(final))
                throw new GameLogException(GameLogError.LogAlreadyExists,
                    $"a retention log already exists for season {season}: '{final}'. " +
                    "S90 does not support resume or overwrite.");
            if (File.Exists(prog))
                throw new GameLogException(GameLogError.CrashedLogPresent,
                    $"a crashed retention log is present for season {season}: '{prog}'. " +
                    "It is never silently continued or truncated.");

            w._file = new FileStream(prog, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            w.WriteFileHeader();
            w.State = GameLogWriterState.FileCreated;

            // ── RosterWritten ───────────────────────────────────────────────
            w.AppendPayload(rosterBytes);
            w._file.Flush();
            foreach (var p in persons) w._rosterPersons.Add(p);
            w.State = GameLogWriterState.RosterWritten;
            w.State = GameLogWriterState.GamesAppending;
            return w;
        }
        catch
        {
            w._file?.Dispose();
            w._file = null;
            // A failure at or after creation leaves the .inprogress behind deliberately:
            // it is evidence, and §5.2 refuses to silently continue from one anyway.
            lockHandle.Dispose();
            w._lockHandle = null;
            throw;
        }
        finally
        {
            if (w._file is not null) w._lockHandle = lockHandle;
        }
    }

    private static void IdentityGuardSeason(SeasonId seasonId)
    {
        if (!seasonId.IsValid)
            throw new HistoryException(HistoryError.InvalidIdentity,
                "a retention log cannot be opened for an unissued season id.");
    }

    // ── The file header ─────────────────────────────────────────────────────

    private void WriteFileHeader()
    {
        var buf = new byte[GameLogSchemaV1.FileHeaderSize];
        var s = buf.AsSpan();
        var o = 0;
        GameLogSchemaV1.Magic.CopyTo(s[o..]); o += 8;
        GameLogSchemaV1.W16(s, ref o, GameLogSchemaV1.FileFormatVersion);
        GameLogSchemaV1.W16(s, ref o, GameLogSchemaV1.BlockSchemaVersion);
        GameLogSchemaV1.W16(s, ref o, GameLogSchemaV1.RowSchemaVersion);
        GameLogSchemaV1.W16(s, ref o, (short)GameLogSchemaV1.FileHeaderSize);
        _historyId.CopyTo(s[o..]); o += 16;
        _worldDigest.CopyTo(s[o..]); o += 32;
        GameLogSchemaV1.W64(s, ref o, _seasonId);
        _scheduleDigest.CopyTo(s[o..]); o += 32;
        GameLogSchemaV1.W32(s, ref o, GameLogSchemaV1.RowSize);
        GameLogSchemaV1.W32(s, ref o, GameLogSchemaV1.RowFieldCount);
        // 16 reserved bytes stay zero.
        if (o != GameLogSchemaV1.FileHeaderSize - 16)
            throw new GameLogException(GameLogError.DomainViolation, "file header layout arithmetic is wrong.");
        _file!.Write(buf, 0, buf.Length);
        // ★ The file header is deliberately OUTSIDE the payload digest: the digest
        // covers everything the writer appends after it, which is exactly the region
        // that grows, and keeping the header out is what lets it never be rewritten.
    }

    // ── The roster section ──────────────────────────────────────────────────

    private static byte[] SerializeRosterSection(IReadOnlyList<RosterEntryV1> roster, out List<long> persons)
    {
        persons = new List<long>(roster.Count);
        var total = GameLogSchemaV1.RosterHeaderSize
                  + roster.Count * GameLogSchemaV1.RosterEntrySize
                  + GameLogSchemaV1.RosterTrailerSize;
        var buf = new byte[total];
        var s = buf.AsSpan();

        var o = 0;
        GameLogSchemaV1.RosterMarker.CopyTo(s[o..]); o += 4;
        GameLogSchemaV1.W16(s, ref o, GameLogSchemaV1.RosterSchemaVersion);
        GameLogSchemaV1.W16(s, ref o, (short)GameLogSchemaV1.RosterEntrySize);
        GameLogSchemaV1.W32(s, ref o, roster.Count);
        GameLogSchemaV1.W16(s, ref o, GameLogSchemaV1.RatingCount);
        o = GameLogSchemaV1.RosterHeaderSize;   // remaining 18 bytes reserved-zero

        long previous = 0;
        for (var i = 0; i < roster.Count; i++)
        {
            var e = roster[i];
            if (!e.PersonId.IsValid)
                throw new GameLogException(GameLogError.InvalidRosterEntry,
                    $"roster entry {i} carries an unissued person id.");
            var raw = e.PersonId.Raw;
            // ★ Strictly ascending, which does two jobs: it makes a duplicate
            // impossible to express, and it gives a future reader a binary search
            // over the section without an index.
            if (raw <= previous)
                throw new GameLogException(GameLogError.InvalidRosterEntry,
                    $"roster must be strictly ascending by person id; entry {i} is {raw} after {previous}.");
            previous = raw;
            persons.Add(raw);

            if (e.SchoolId < 0 || e.PoolId < 0 || e.AcquisitionIndex <= 0)
                throw new GameLogException(GameLogError.InvalidRosterEntry,
                    $"roster entry {i} has a context id outside the format's domain.");
            if (e.HierarchyRank < 1 || e.HierarchyRank > 10)
                throw new GameLogException(GameLogError.InvalidRosterEntry,
                    $"roster entry {i} hierarchy rank {e.HierarchyRank} is outside 1..10.");
            if (e.Ratings is null || e.Ratings.Count != GameLogSchemaV1.RatingCount)
                throw new GameLogException(GameLogError.InvalidRosterEntry,
                    $"roster entry {i} carries {e.Ratings?.Count ?? 0} ratings; the schema pins {GameLogSchemaV1.RatingCount}.");

            var entryStart = o;
            GameLogSchemaV1.W64(s, ref o, raw);
            GameLogSchemaV1.W32(s, ref o, e.SchoolId);
            GameLogSchemaV1.W32(s, ref o, e.PoolId);
            GameLogSchemaV1.W32(s, ref o, e.AcquisitionIndex);
            GameLogSchemaV1.WriteFixedString(e.Name, s.Slice(o, GameLogSchemaV1.NameBytes), $"roster entry {i} name");
            o += GameLogSchemaV1.NameBytes;
            GameLogSchemaV1.WriteFixedString(e.Role, s.Slice(o, GameLogSchemaV1.RoleBytes), $"roster entry {i} role");
            o += GameLogSchemaV1.RoleBytes;
            if (e.Position is not (RosterPosition.Guard or RosterPosition.Wing or RosterPosition.Big))
                throw new GameLogException(GameLogError.InvalidPosition,
                    $"roster entry {i} has position {(byte)e.Position}, which the format does not define.");
            GameLogSchemaV1.W8(s, ref o, (byte)e.Position);
            GameLogSchemaV1.W8(s, ref o, e.IsStarter ? (byte)1 : (byte)0);
            GameLogSchemaV1.W16(s, ref o, e.HierarchyRank);
            GameLogSchemaV1.WDouble(s, ref o, e.ScoutRank, $"roster entry {i} scoutRank");
            for (var r = 0; r < GameLogSchemaV1.RatingCount; r++)
            {
                var v = e.Ratings[r];
                if (v < 0 || v > 99)
                    throw new GameLogException(GameLogError.InvalidRosterEntry,
                        $"roster entry {i} rating slot {r} is {v}, outside the authored 0..99 scale.");
                GameLogSchemaV1.W16(s, ref o, v);
            }
            o = entryStart + GameLogSchemaV1.RosterEntrySize;   // 12 reserved bytes stay zero
        }

        GameLogSchemaV1.Checksum8(s[..o], s.Slice(o, GameLogSchemaV1.RosterTrailerSize));
        return buf;
    }

    // ── Game blocks ─────────────────────────────────────────────────────────

    /// <summary>Append one completed game. Rows are validated as a set before a byte
    /// is serialized, so a rejected game leaves the file exactly as it was.</summary>
    public void AppendGame(GameBlockFactsV1 facts, IReadOnlyList<PerGameStatRowV1> rows)
    {
        Require(GameLogWriterState.GamesAppending, nameof(AppendGame));
        if (rows is null || rows.Count == 0)
            throw new GameLogException(GameLogError.InvalidRow,
                "a completed game with no participating player is impossible; refusing to write an empty block.");
        if (rows.Count > GameLogSchemaV1.MaxRowCount)
            throw new GameLogException(GameLogError.DomainViolation,
                $"{rows.Count} rows exceeds the {GameLogSchemaV1.MaxRowCount} a block holds.");
        if (facts.FixtureOrdinal != _nextOrdinal)
            throw new GameLogException(GameLogError.BlockOutOfOrder,
                $"expected fixture ordinal {_nextOrdinal}, got {facts.FixtureOrdinal}. " +
                "Blocks are contiguous from zero by construction.");
        if (!facts.GameId.IsValid)
            throw new GameLogException(GameLogError.BlockOutOfOrder, "a game block needs an issued game id.");
        if (!_seenGameIds.Add(facts.GameId.Raw))
            throw new GameLogException(GameLogError.BlockOutOfOrder,
                $"game id {facts.GameId.Raw} appears twice in one season log.");
        if (facts.PossessionCount <= 0)
            throw new GameLogException(GameLogError.DomainViolation,
                "a completed game has a positive possession count; it is the minutes denominator.");

        // Sorted for the same reason the roster is, and with a second benefit: a block's
        // bytes no longer depend on the order the caller happened to enumerate its rows in,
        // so the golden fixture pins one canonical encoding rather than one lucky one.
        rows = rows.OrderBy(r => r.PersonId.IsValid ? r.PersonId.Raw : -1).ToList();

        var seen = new HashSet<long>(rows.Count);
        foreach (var r in rows)
        {
            if (!r.PersonId.IsValid)
                throw new GameLogException(GameLogError.InvalidRow, "a game row carries an unissued person id.");
            if (!seen.Add(r.PersonId.Raw))
                throw new GameLogException(GameLogError.InvalidRow,
                    $"person {r.PersonId.Raw} appears twice in one game block.");
            // ★ Intrinsic: a row must name somebody this file's roster section knows.
            // Both facts are inside the file, so a future reader can re-check it with
            // nothing but the file.
            if (!_rosterPersons.Contains(r.PersonId.Raw))
                throw new GameLogException(GameLogError.RowPersonNotInRoster,
                    $"person {r.PersonId.Raw} played but is not in this season's roster section.");
            if (r.Credits <= 0)
                throw new GameLogException(GameLogError.InvalidRow,
                    $"person {r.PersonId.Raw} has a row but no floor credit; the participation " +
                    "predicate emits a row only for positive credit.");
        }

        var size = GameLogSchemaV1.BlockHeaderSize + rows.Count * GameLogSchemaV1.RowSize
                 + GameLogSchemaV1.BlockTrailerSize;
        var buf = new byte[size];
        var s = buf.AsSpan();
        var o = 0;
        GameLogSchemaV1.BlockMarker.CopyTo(s[o..]); o += 4;
        GameLogSchemaV1.W64(s, ref o, facts.GameId.Raw);
        GameLogSchemaV1.W32(s, ref o, facts.FixtureOrdinal);
        GameLogSchemaV1.W32(s, ref o, facts.HomeSchoolId);
        GameLogSchemaV1.W32(s, ref o, facts.AwaySchoolId);
        GameLogSchemaV1.W8(s, ref o, facts.IsConferenceGame ? (byte)0 : (byte)1);
        GameLogSchemaV1.Skip(ref o, 1);                       // reserved
        GameLogSchemaV1.WU16(s, ref o, (ushort)rows.Count);
        GameLogSchemaV1.W32(s, ref o, facts.HomeScore);
        GameLogSchemaV1.W32(s, ref o, facts.AwayScore);
        GameLogSchemaV1.W16(s, ref o, facts.OvertimePeriods);
        GameLogSchemaV1.Skip(ref o, 2);                       // reserved
        GameLogSchemaV1.W64(s, ref o, facts.PossessionCount);
        if (o != GameLogSchemaV1.BlockHeaderSize)
            throw new GameLogException(GameLogError.DomainViolation, "block header layout arithmetic is wrong.");

        foreach (var r in rows) WriteRow(s, ref o, r);

        GameLogSchemaV1.Checksum8(s[..o], s.Slice(o, GameLogSchemaV1.BlockTrailerSize));

        AppendPayload(buf);
        _file!.Flush();          // managed buffers only; a crash loses at most this block
        _nextOrdinal++;
        _blockCount++;
        _rowCount += rows.Count;
    }

    private static void WriteRow(Span<byte> s, ref int o, PerGameStatRowV1 r)
    {
        var start = o;
        GameLogSchemaV1.W64(s, ref o, r.PersonId.Raw);
        GameLogSchemaV1.W32(s, ref o, r.SchoolId);
        GameLogSchemaV1.W32(s, ref o, r.PoolId);
        GameLogSchemaV1.W32(s, ref o, r.AcquisitionIndex);
        // The 21 counters, in the order the golden pins. Every one is a COUNT —
        // no rate, no percentage, no points. Points are 2*fgm + tpm + ftm at read time.
        Span<long> c = stackalloc long[21]
        {
            r.Credits, r.OffensiveCredits, r.Fga, r.Fgm, r.Tpa, r.Tpm, r.Fta, r.Ftm,
            r.OReb, r.DReb, r.Ast, r.Stl, r.Blk, r.To, r.ShFoul, r.NsFoul, r.OffFoul,
            r.FbBlk, r.OpponentTwoPaOnFloor, r.SecuredBoardsOnFloor, r.OffensiveTeamFgmOnFloor,
        };
        for (var i = 0; i < c.Length; i++)
        {
            if (c[i] < 0)
                throw new GameLogException(GameLogError.InvalidRow,
                    $"person {r.PersonId.Raw} has a negative counter at slot {i}; a game delta is never negative.");
            GameLogSchemaV1.W64(s, ref o, c[i]);
        }
        if (o - start != GameLogSchemaV1.RowSize)
            throw new GameLogException(GameLogError.DomainViolation, "row layout arithmetic is wrong.");
    }

    // ── Finalization ────────────────────────────────────────────────────────

    /// <summary>Append the footer, flush to disk, then atomically rename to `.log`.
    /// The rename IS publication: a `.log` that exists is complete by construction.</summary>
    public void Finalize(int expectedGameCount)
    {
        Require(GameLogWriterState.GamesAppending, nameof(Finalize));
        if (_blockCount != expectedGameCount)
            throw new GameLogException(GameLogError.BlockOutOfOrder,
                $"the season scheduled {expectedGameCount} fixtures but {_blockCount} blocks were written; " +
                "a finalized log holds one block per fixture and no other.");
        if (_blockCount == 0)
            throw new GameLogException(GameLogError.InvalidWriterState,
                "refusing to finalize a season log with no games in it.");

        var buf = new byte[GameLogSchemaV1.FooterSize];
        var s = buf.AsSpan();
        var o = 0;
        GameLogSchemaV1.FooterMarker.CopyTo(s[o..]); o += 4;
        // ★ Both counts come from the writer's OWN appended-block state, never from a
        // caller — and the reader recomputes both rather than trusting them. A count
        // that is only ever written and read is a comment, not a check.
        GameLogSchemaV1.W32(s, ref o, _blockCount);
        GameLogSchemaV1.W64(s, ref o, _rowCount);

        // The digest covers every physical byte after the file header, INCLUDING the
        // roster section, its trailer, and every block checksum — and it is computed
        // incrementally as the file is written, so finalizing never re-reads the file.
        var digest = _payload.GetCurrentHash();
        digest.CopyTo(s[o..]); o += 32;
        // 16 reserved bytes stay zero.

        _file!.Write(buf, 0, buf.Length);
        _file.Flush(flushToDisk: true);          // the one OS-level flush, at publication
        _file.Dispose();
        _file = null;

        // ★ Destination-exists refuses without touching either file. There are no
        // overwrite semantics anywhere in the log path.
        if (File.Exists(_finalPath))
            throw new GameLogException(GameLogError.LogAlreadyExists,
                $"'{_finalPath}' appeared while this season was being written; refusing to overwrite it. " +
                $"The complete log is at '{_inProgressPath}'.");
        try
        {
            File.Move(_inProgressPath, _finalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GameLogException(GameLogError.LogPersistFailed,
                $"could not publish the season log to '{_finalPath}' — {ex.Message}", ex);
        }
        State = GameLogWriterState.Finalized;
        ReleaseLock();
    }

    private void AppendPayload(byte[] bytes)
    {
        _file!.Write(bytes, 0, bytes.Length);
        _payload.AppendData(bytes);
    }

    private void Require(GameLogWriterState expected, string op)
    {
        if (State != expected)
            throw new GameLogException(GameLogError.InvalidWriterState,
                $"{op} requires state {expected}; the writer is {State}.");
    }

    private void ReleaseLock()
    {
        var h = _lockHandle;
        _lockHandle = null;
        h?.Dispose();
    }

    public void Dispose()
    {
        _file?.Dispose();
        _file = null;
        _payload.Dispose();
        ReleaseLock();
    }
}
