using System.Globalization;
using System.Security.Cryptography;

namespace Charm.History;

// ============================================================================
//  S89 — THE ALLOCATOR AND ITS FILE.
//
//  ★ HIGH-WATER, NEVER A FREE LIST. There is no record of which numbers were
//  issued and no way to hand one back. The three counters ARE the proof: every
//  value below a counter is spent, full stop, whether it ended up on a person or
//  was burned by a season that failed to build. Holes are permanent and cost
//  nothing — there are more numbers than there will ever be careers.
//
//  ★ S124 — NUMBERS ARE HELD UNTIL THE SEASON CLOSES (B1a, K1). A reservation
//  moves the in-memory counters and writes nothing; the season's close makes them
//  durable, once, as its LAST write. A store disposed without publishing has
//  issued nothing: its numbers were never on disk. A held number reaches disk only
//  inside the season's own files, written at the close BEFORE the career file; if
//  the close does not finish, the next close sets every file of that season or
//  later aside before it writes. So no published number is ever handed out again.
//
//  ★ S125 — THE CAREER FILE IS THE AUTHORITY (B1b). It lists every file the career
//  owns with each one's SHA-256, carries a fingerprint of its own (a trailer), and
//  is published in exactly three ways (brief §4b):
//
//    CREATION  — the folder holds neither the career file nor its backup. A stray
//                `league-0` from a creation that died is set aside; then league-0
//                is written, then `.new` listing it, then `.new` is moved into
//                place WITHOUT overwriting.
//    A SAVE    — `PublishSeason`: the season's files are already written under
//                names no listed file holds; the store hashes them, writes `.new`,
//                and `File.Replace(.new, career file, .bak)`. Called only when this
//                process loaded and verified the current career file, so `.bak` is
//                always the last good save.
//    RECOVERY  — the career file is missing or torn and `.bak` verifies WHOLE (its
//                trailer, the contract, and every file it lists): the torn file
//                moves to the orphans folder, `.bak` is copied to `.new` and the
//                copy verified, `.new` moves into place without overwriting. `.bak`
//                is never touched, so an interruption at any step leaves "no career
//                file + a whole backup", and the next open runs the same recovery.
//
//  `.new` is never authoritative: overwritten before use, ignored on load.
//
//  ★ K8 — A FILE THE CURRENT SAVE OR `.bak` LISTS IS NEVER WRITTEN TO, RENAMED OR
//  DELETED BY THE GAME. New content goes under a name no listed file holds, and a
//  save that fails leaves both saves loadable. A save that would list a name already
//  listed is refused by name.
//
//  ★ Q6 — a career file that verifies but lists a file that is missing or changed is
//  REFUSED BY NAME, never rolled back to `.bak`: the backup is for a torn career
//  file, not for a damaged season.
//
//  ★ THE LOCK IS A SIDECAR, NOT THE FILE ITSELF (S89): replacing a file that is held
//  open does not work on Windows. It is taken FIRST, before existence is checked,
//  and (S124 K2) held until the store is disposed.
//
//  ★ NEVER A FALLBACK. If the career file cannot be made durable the season is not
//  published and the run stops; it is played again next run.
//
//  Durability is claimed against ordinary process failure and normal atomic
//  filesystem behaviour. Not against a machine losing power mid-write.
// ============================================================================

public sealed class HistoryStore : IDisposable
{
    private readonly string _path;
    private readonly string _lockPath;
    private FileStream? _lockHandle;
    private HistoryStateV4 _state;
    private bool _reservationsClosed;

    private HistoryStore(string path, string lockPath, FileStream lockHandle, HistoryStateV4 state,
                         IReadOnlyList<string> openReport)
    {
        _path = path;
        _lockPath = lockPath;
        _lockHandle = lockHandle;
        _state = state;
        OpenReport = openReport;
    }

    /// <summary>The world this history is bound to. A history opened against a different
    /// world is refused, never silently rebound.</summary>
    public string WorldFingerprint => _state.WorldFingerprint;

    /// <summary>This career lineage's label — 32 lowercase hex.</summary>
    public string HistoryId => _state.HistoryId;

    /// <summary>★ S119 — the civil year this career's FIRST season opens in, fixed at creation
    /// and read back off the file on every open. Season N is played in StartYear + (N - 1).</summary>
    public int StartYear => _state.StartYear;

    /// <summary>★ S125 — every file this career owns, verified at open (name-free paths, sorted).
    /// No reader takes its paths from here yet (B1c).</summary>
    public IReadOnlyList<ListedFile> CareerFiles => _state.Files;

    /// <summary>★ S125 — the published seasons and the rules each was played under.</summary>
    public IReadOnlyList<SeasonEntry> Seasons => _state.Seasons;

    /// <summary>★ S125 — what opening this career did besides loading it (a recovery from the
    /// backup, a stray file set aside), one line each, for the caller to print. Empty when the
    /// open only read.</summary>
    public IReadOnlyList<string> OpenReport { get; }

    // ★ The id source is a seam for ONE reason: goldens. Production mints from Guid.NewGuid(),
    // which by design produces a different file every run.
    private static Func<string> _idSource = HistorySchemaV4.MintHistoryId;

    /// <summary>Pin the lineage label for the duration of the returned scope. Public, a deliberate
    /// widening: it sets a label and exposes no number. Nothing on a production path may call it.</summary>
    public static IDisposable UseFixedHistoryIdForTests(string id)
    {
        if (!HistorySchemaV4.IsCanonicalHistoryId(id))
            throw new HistoryException(HistoryError.WrongType,
                "a test history id must be 32 lowercase hex characters.");
        var previous = _idSource;
        _idSource = () => id;
        return new Restore(() => _idSource = previous);
    }

    // ★ S125 (K4) — the rules stamp changes with every build, so no golden of a career file could
    // hold across sessions. The checks that pin a career file's bytes pin these two values instead.
    private static (string ConfigSha256, string RulesStamp)? _fixedRules;

    /// <summary>★ S125 — every season published inside the returned scope records these two values
    /// in place of the real settings hash and build stamp. Same terms as the fixed history id: a
    /// test seam, never called on a production path.</summary>
    public static IDisposable UseFixedRulesForTests(string configSha256, string rulesStamp)
    {
        if (!HistorySchemaV4.IsLowerHex64(configSha256) || !HistorySchemaV4.IsLowerHex64(rulesStamp))
            throw new HistoryException(HistoryError.WrongType, "fixed rules must be 64 lowercase hex each.");
        var previous = _fixedRules;
        _fixedRules = (configSha256, rulesStamp);
        return new Restore(() => _fixedRules = previous);
    }

    private static Action<string>? _publishStepForTest;
    private static Action<string>? _recoveryStepForTest;

    /// <summary>★ S125 — a throw hook inside the save, at "new-written" (`.new` flushed, the swap
    /// not yet made). Test seam only.</summary>
    public static IDisposable UsePublishStepForTests(Action<string> step)
    {
        var previous = _publishStepForTest;
        _publishStepForTest = step;
        return new Restore(() => _publishStepForTest = previous);
    }

    /// <summary>★ S125 — a throw hook inside a recovery, at "primary-set-aside" (the torn career
    /// file moved to the orphans folder) and "new-written" (the copy of `.bak` flushed and
    /// verified, not yet moved into place). Test seam only.</summary>
    public static IDisposable UseRecoveryStepForTests(Action<string> step)
    {
        var previous = _recoveryStepForTest;
        _recoveryStepForTest = step;
        return new Restore(() => _recoveryStepForTest = previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action _undo;
        public Restore(Action undo) => _undo = undo;
        public void Dispose() => _undo();
    }

    /// <summary>The normalized path actually used. Concurrent safety assumes every writer
    /// resolves to the same normalized path; symlink aliases are out of scope.</summary>
    public string Path => _path;

    /// <summary>★ S96 — the season number the NEXT <see cref="ReserveSeason"/> will return. A read
    /// only: while this run holds the lock, the value returned here IS the value the next season
    /// reservation hands back. Deliberately NOT a `SeasonId`.</summary>
    public long PeekNextSeasonId => _state.NextSeasonId;

    // ── Names ────────────────────────────────────────────────────────────────

    /// <summary>A career's sibling folder of the given kind: the career file's path with `.json`
    /// stripped, then `.kind` — `career.gamelog`, `career.events`, `career.league`,
    /// `career.orphans`. The same rule the log, record and set-aside folders have always used.</summary>
    public static string SiblingFolderFor(string historyPath, string kind)
    {
        var full = System.IO.Path.GetFullPath(historyPath);
        if (full.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) full = full[..^5];
        return full + "." + kind;
    }

    /// <summary>Where a listed (name-free) path lives on disk for the career at <paramref name="historyPath"/>.</summary>
    public static string ResolveListed(string historyPath, string listed)
    {
        var slash = listed.IndexOf('/');
        var kind = listed[..slash];
        var rest = listed[(slash + 1)..];
        return System.IO.Path.Combine(SiblingFolderFor(historyPath, kind), rest);
    }

    /// <summary>The listed path for a file on disk, or null when the file is not one a career owns.</summary>
    private string? ListedPathOf(string fullPath)
    {
        var full = System.IO.Path.GetFullPath(fullPath);
        var dir = System.IO.Path.GetDirectoryName(full);
        var name = System.IO.Path.GetFileName(full);
        foreach (var kind in new[] { "events", "gamelog", "league" })
        {
            if (!string.Equals(dir, SiblingFolderFor(_path, kind), StringComparison.Ordinal)) continue;
            var listed = kind + "/" + name;
            return HistorySchemaV4.ClassifyListedPath(listed) is null ? null : listed;
        }
        return null;
    }

    private static string BackupPathFor(string full) => full + ".bak";
    private static string NewPathFor(string full) => full + ".new";

    /// <summary>The SHA-256 of a file's bytes as they are on disk, lowercase hex.</summary>
    public static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    // ── Opening ──────────────────────────────────────────────────────────────

    /// <summary>Take the lock, then load (recovering a torn career file from its backup when the
    /// backup is whole) or create, and verify.
    ///
    /// <para>Opening a career whose file verifies writes nothing. Every refusal happens before any
    /// write, so a refused career is left exactly as it was.</para>
    ///
    /// <para>★ v1, v2 and v3 careers are refused by name, never upgraded (C-60).</para>
    ///
    /// <para>★ S125 — <paramref name="leagueIfNew"/> is prestige by school, copied from the world,
    /// and is read ONLY when a career is created: it becomes the career's `league-0`. An existing
    /// career's league state comes off its files. <paramref name="startYearIfNew"/> likewise.</para></summary>
    public static HistoryStore Open(string path, string worldFingerprint, IEnumerable<SchoolPrestige> leagueIfNew,
                                    int startYearIfNew = HistoryStateV4.DefaultStartYear)
    {
        if (!HistoryStateV4.IsStartYearInDomain(startYearIfNew))
            throw new HistoryException(HistoryError.YearOutOfDomain,
                $"a career cannot start in {startYearIfNew.ToString(CultureInfo.InvariantCulture)} — " +
                $"a season can start in {HistoryStateV4.MinStartYear}..{HistoryStateV4.MaxStartYear}.");
        ArgumentNullException.ThrowIfNull(leagueIfNew);

        if (string.IsNullOrWhiteSpace(path))
            throw new HistoryException(HistoryError.PathIsDirectory, "history path is empty.");

        var full = System.IO.Path.GetFullPath(path);
        if (Directory.Exists(full))
            throw new HistoryException(HistoryError.PathIsDirectory,
                $"history path '{full}' is an existing directory, not a file.");

        // The parent folder is created because the history argument was supplied
        // explicitly — a named career may live in a folder that does not exist yet.
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new HistoryException(HistoryError.PersistFailed,
                    $"could not create the folder for history '{full}' — {ex.Message}", ex);
            }
        }

        var lockPath = full + ".lock";
        FileStream lockHandle;
        try
        {
            lockHandle = new FileStream(lockPath, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(HistoryError.LockUnavailable,
                $"could not take the history lock '{lockPath}' — another run may hold it. {ex.Message}", ex);
        }

        try
        {
            var report = new List<string>();
            var state = LoadRecoverOrCreate(full, worldFingerprint, leagueIfNew, startYearIfNew, report);
            return new HistoryStore(full, lockPath, lockHandle, state, report);
        }
        catch
        {
            lockHandle.Dispose();
            throw;
        }
    }

    /// <summary>K3's table, in order.</summary>
    private static HistoryStateV4 LoadRecoverOrCreate(
        string full, string worldFingerprint, IEnumerable<SchoolPrestige> leagueIfNew, int startYearIfNew,
        List<string> report)
    {
        var bak = BackupPathFor(full);
        var primaryExists = File.Exists(full);
        var bakExists = File.Exists(bak);

        HistoryException? torn = null;
        if (primaryExists)
        {
            try
            {
                // A verified career file whose listed file is missing or changed is refused here
                // (Q6) — never rolled back to the backup.
                return LoadVerified(full, full, worldFingerprint);
            }
            catch (HistoryException hx) when (hx.Error == HistoryError.Torn)
            {
                torn = hx;
            }
        }

        if (!bakExists)
        {
            if (torn is not null)
                throw new HistoryException(HistoryError.Torn,
                    torn.Message + " There is no backup to recover from; nothing was written.", torn);
            return Create(full, worldFingerprint, leagueIfNew, startYearIfNew, report);
        }

        // The career file is missing or torn and a backup exists: an existing career with a lost
        // primary. Recovery, never creation.
        HistoryStateV4 recovered;
        byte[] bakBytes;
        try
        {
            bakBytes = ReadAll(bak);
            recovered = LoadVerified(bak, full, worldFingerprint, bakBytes);
        }
        catch (HistoryException hx) when (hx.Error != HistoryError.FingerprintMismatch)
        {
            throw new HistoryException(HistoryError.BackupUnusable,
                $"the career file '{full}' is {(primaryExists ? "torn" : "missing")} and its backup cannot stand in " +
                $"for it — {hx.Message} Nothing was written.", hx);
        }

        try
        {
            if (primaryExists)
            {
                var moved = MoveToOrphans(full, full, System.IO.Path.GetFileName(full));
                report.Add($"The career file was torn; set aside as '{moved}'.");
                _recoveryStepForTest?.Invoke("primary-set-aside");
            }
            var newPath = NewPathFor(full);
            WriteFlushed(newPath, bakBytes, FileMode.Create);
            if (!ReadAll(newPath).AsSpan().SequenceEqual(bakBytes))
                throw new IOException($"the copy of the backup at '{newPath}' does not read back identical.");
            _recoveryStepForTest?.Invoke("new-written");
            File.Move(newPath, full, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(HistoryError.PersistFailed,
                $"could not recover the career file '{full}' from its backup — {ex.Message} The backup is untouched; " +
                "the next open tries again.", ex);
        }
        report.Add($"Recovered the career from its backup ('{System.IO.Path.GetFileName(bak)}'): it is at its last good save.");
        return recovered;
    }

    /// <summary>Read, verify the trailer, parse, check the contract, the world, and every listed
    /// file's hash. <paramref name="careerPath"/> is the career file the listed paths resolve
    /// against (the backup's list resolves against the career, not against `.bak`).</summary>
    private static HistoryStateV4 LoadVerified(string file, string careerPath, string worldFingerprint, byte[]? bytes = null)
    {
        bytes ??= ReadAll(file);

        var payload = HistorySchemaV4.SplitTrailer(bytes, out var verified);
        if (payload is null)
        {
            // No trailer. An OLDER career is recognised by its version first (C-60) — it never had
            // one. Anything else without a trailer is torn.
            int? version = null;
            try { version = HistorySchemaV3.PeekVersion(bytes); }
            catch (HistoryException) { /* not JSON, or no readable version: torn */ }
            if (version is 1 or 2 or 3)
                throw new HistoryException(HistoryError.UnsupportedVersion,
                    $"{GameLogSchemaV1.OlderVersionSentence} (history '{file}' is schemaVersion " +
                    $"{version.Value.ToString(CultureInfo.InvariantCulture)}; this build reads 4).");
            if (version is { } v && v != HistoryStateV4.SchemaVersion)
                throw new HistoryException(HistoryError.UnsupportedVersion,
                    $"unsupported history schemaVersion {v.ToString(CultureInfo.InvariantCulture)} (this build reads 4).");
            throw new HistoryException(HistoryError.Torn,
                $"the career file '{file}' has no fingerprint line at its end — it was not finished being written, " +
                "or it was edited by hand.");
        }
        if (!verified)
            throw new HistoryException(HistoryError.Torn,
                $"the career file '{file}' does not match its own fingerprint — it was not finished being written, " +
                "or it was edited by hand.");

        var state = HistorySchemaV4.Parse(payload);

        if (!string.Equals(state.WorldFingerprint, worldFingerprint, StringComparison.Ordinal))
            throw new HistoryException(HistoryError.FingerprintMismatch,
                $"this history belongs to a different world. History '{file}' is bound to " +
                $"{state.WorldFingerprint}, the world given is {worldFingerprint}.");

        foreach (var f in state.Files)
        {
            var onDisk = ResolveListed(careerPath, f.Path);
            if (!File.Exists(onDisk))
                throw new HistoryException(HistoryError.ListedFileMissing,
                    $"the career lists '{f.Path}' but '{onDisk}' is not there. The career is refused; nothing was written.");
            string actual;
            try { actual = HashFile(onDisk); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new HistoryException(HistoryError.ListedFileMissing,
                    $"the career lists '{f.Path}' but '{onDisk}' cannot be read — {ex.Message}", ex);
            }
            if (!string.Equals(actual, f.Sha256, StringComparison.Ordinal))
                throw new HistoryException(HistoryError.ListedFileChanged,
                    $"the career lists '{f.Path}', but '{onDisk}' is not the file it saved (its bytes have changed). " +
                    "The career is refused; nothing was written.");
        }
        return state;
    }

    /// <summary>CREATION (K2): a stray league-0 set aside; league-0 written; `.new` listing it;
    /// `.new` moved into place without overwriting.</summary>
    private static HistoryStateV4 Create(string full, string worldFingerprint, IEnumerable<SchoolPrestige> leagueIfNew,
                                         int startYearIfNew, List<string> report)
    {
        var id = _idSource();
        var leagueBytes = LeagueStateFile.Encode(id, 0, leagueIfNew);
        var leaguePath = LeagueStateFile.PathFor(full, 0);
        try
        {
            if (File.Exists(leaguePath))
            {
                var folder = System.IO.Path.GetFileName(LeagueStateFile.FolderFor(full));
                var moved = MoveToOrphans(full, leaguePath, folder + "/" + System.IO.Path.GetFileName(leaguePath));
                report.Add($"Set aside a league state left by a career that was never created: '{moved}'.");
            }
            PublishNewFile(leaguePath, leagueBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(HistoryError.PersistFailed,
                $"could not write the new career's league state '{leaguePath}' — {ex.Message}", ex);
        }

        var state = new HistoryStateV4(id, worldFingerprint, startYearIfNew, 1, 1, 1,
            new[] { new ListedFile(LeagueStateFile.ListedPathFor(0), HistorySchemaV4.Sha256Hex(leagueBytes)) },
            Array.Empty<SeasonEntry>());
        var bytes = HistorySchemaV4.Serialize(state);
        var newPath = NewPathFor(full);
        try
        {
            WriteFlushed(newPath, bytes, FileMode.Create);
            File.Move(newPath, full, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(HistoryError.PersistFailed,
                $"could not create the career file '{full}' — {ex.Message}", ex);
        }
        return state;
    }

    // ── Reservation ──────────────────────────────────────────────────────────

    public PersonId[] ReservePersons(int count)
    {
        var start = Reserve(count, Counter.Person);
        var ids = new PersonId[count];
        for (var i = 0; i < count; i++) ids[i] = PersonId.FromRaw(start + i);
        return ids;
    }

    public SeasonId ReserveSeason()
    {
        var start = Reserve(1, Counter.Season);
        return SeasonId.FromRaw(start);
    }

    public GameId[] ReserveGames(int count)
    {
        var start = Reserve(count, Counter.Game);
        var ids = new GameId[count];
        for (var i = 0; i < count; i++) ids[i] = GameId.FromRaw(start + i);
        return ids;
    }

    private enum Counter { Person, Season, Game }

    /// <summary>Checked half-open arithmetic: `start = next`, `end = next + n`, hold
    /// `next = end` in memory, issue `[start, end)`. An oversized batch rejects the WHOLE
    /// reservation with the counters untouched — never a partial advance.
    ///
    /// <para>★ S124 (K1) — nothing is written here. The numbers are provisional until
    /// <see cref="PublishSeason"/>; a store disposed before that discards them, and
    /// the next open hands the same numbers out again (see the header).</para></summary>
    private long Reserve(int count, Counter which)
    {
        if (_lockHandle is null)
            throw new HistoryException(HistoryError.PersistFailed,
                "the history lock has already been released; no further identities may be issued.");
        if (_reservationsClosed)
            throw new HistoryException(HistoryError.PersistFailed,
                "reservations are closed for this run.");
        if (count < 0)
            throw new HistoryException(HistoryError.NegativeCount,
                $"cannot reserve {count.ToString(CultureInfo.InvariantCulture)} identities.");

        var next = which switch
        {
            Counter.Person => _state.NextPersonId,
            Counter.Season => _state.NextSeasonId,
            _              => _state.NextGameId,
        };

        // Zero is a no-op that writes nothing at all.
        if (count == 0) return next;

        long end;
        try { end = checked(next + count); }
        catch (OverflowException)
        {
            throw new HistoryException(HistoryError.ExhaustedRange,
                $"a reservation of {count.ToString(CultureInfo.InvariantCulture)} runs past the end of " +
                "the number line; the history is unmodified and nothing was issued.");
        }
        if (end < 1 || end == long.MaxValue)
            throw new HistoryException(HistoryError.ExhaustedRange,
                "a reservation would leave the counter outside its valid domain; " +
                "the history is unmodified and nothing was issued.");

        var advanced = which switch
        {
            Counter.Person => _state with { NextPersonId = end },
            Counter.Season => _state with { NextSeasonId = end },
            _              => _state with { NextGameId   = end },
        };

        _state = advanced;                     // ★ S124 — held; published at the close
        return next;
    }

    /// <summary>★ S124 (K2) — no further reservation may be made on this store. The lock is
    /// NOT released: the season's close still needs it to publish, and a second run must not
    /// start the same season while this one plays. <see cref="Dispose"/> releases it.
    /// Nothing clears this flag, so one store serves one season.</summary>
    public void CloseReservations()
    {
        _reservationsClosed = true;
    }

    // ── The league state at a close (K5, K6) ─────────────────────────────────

    /// <summary>★ S125 — write season <paramref name="season"/>'s league state under its final name
    /// (a whole temp file, then one no-overwrite move) and return where it went. Refused by name when
    /// the career already lists that name (K8). The file is unlisted until <see cref="PublishSeason"/>
    /// lists it; a close that stops in between leaves it for the next close to set aside.</summary>
    public string WriteLeagueState(long season, IEnumerable<SchoolPrestige> prestige)
    {
        RequireLock("a league state cannot be written");
        var listed = LeagueStateFile.ListedPathFor(season);
        if (_state.Files.Any(f => string.Equals(f.Path, listed, StringComparison.OrdinalIgnoreCase)))
            throw new HistoryException(HistoryError.ListedNameTaken,
                $"the career already lists '{listed}'; a listed file is never written again.");
        var bytes = LeagueStateFile.Encode(_state.HistoryId, season, prestige);
        var path = LeagueStateFile.PathFor(_path, season);
        try { PublishNewFile(path, bytes); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(HistoryError.PersistFailed,
                $"could not write the league state '{path}' — {ex.Message} Nothing was published.", ex);
        }
        return path;
    }

    /// <summary>The latest league state this career lists, read and validated (lineage, season,
    /// canonical bytes). Every career lists at least `league-0`.</summary>
    public LeagueState ReadLatestLeagueState()
    {
        long latest = -1;
        foreach (var f in _state.Files)
            if (HistorySchemaV4.ClassifyListedPath(f.Path) is { Kind: "league" } c && c.N > latest)
                latest = c.N;
        if (latest < 0)
            throw new HistoryException(HistoryError.ManifestMismatch, "the career lists no league state.");
        return LeagueStateFile.Read(LeagueStateFile.PathFor(_path, latest), _state.HistoryId, latest);
    }

    // ── A save (K2) ──────────────────────────────────────────────────────────

    /// <summary>★ S125 — THE SAVE. Replaces S124's counters-only publication: the caller has
    /// already written <paramref name="newFiles"/> under their final names (full paths, each a file a
    /// career owns, none already listed — K8); the store hashes each, adds them to the list, adds the
    /// season's rules when the season counter moved, writes `.new`, flushes, and
    /// `File.Replace(.new, career file, .bak)`.
    ///
    /// <para>A publish that moves no season (the suite's hand reservations) passes no rules. A publish
    /// that moves exactly one season must pass them; more than one season per publish is refused.</para>
    ///
    /// <para>An exception means the career file was not replaced and this store still holds the
    /// previous save. (On Windows, `File.Replace` can fail after renaming the career file to its
    /// backup: the next open then finds no career file and a whole backup, and recovers to the
    /// previous save — the save did not happen.)</para></summary>
    public void PublishSeason(IReadOnlyList<string> newFiles, SeasonRules? rules)
    {
        RequireLock("the save cannot be published");

        var files = _state.Files.ToList();
        foreach (var file in newFiles)
        {
            var listed = ListedPathOf(file)
                ?? throw new HistoryException(HistoryError.ListedPathInvalid,
                       $"'{file}' is not a file this career can own.");
            if (files.Any(f => string.Equals(f.Path, listed, StringComparison.OrdinalIgnoreCase)))
                throw new HistoryException(HistoryError.ListedNameTaken,
                    $"the career already lists '{listed}'; a listed file is never written again.");
            string sha;
            try { sha = HashFile(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new HistoryException(HistoryError.PersistFailed,
                    $"could not read '{file}' to fingerprint it — {ex.Message} Nothing was published.", ex);
            }
            files.Add(new ListedFile(listed, sha));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var seasons = _state.Seasons.ToList();
        var newSeasons = _state.NextSeasonId - 1 - seasons.Count;
        if (newSeasons == 1)
        {
            if (rules is null)
                throw new HistoryException(HistoryError.SeasonsInvalid,
                    "a save that publishes a season must record the rules it was played under.");
            var effective = _fixedRules is { } fixedRules
                ? rules with { ConfigSha256 = fixedRules.ConfigSha256, RulesStamp = fixedRules.RulesStamp }
                : rules;
            seasons.Add(new SeasonEntry(_state.NextSeasonId - 1, new[] { effective }));
        }
        else if (newSeasons == 0)
        {
            if (rules is not null)
                throw new HistoryException(HistoryError.SeasonsInvalid,
                    "rules were given for a save that publishes no season.");
        }
        else
        {
            throw new HistoryException(HistoryError.SeasonsInvalid,
                $"a save publishes at most one season; {newSeasons.ToString(CultureInfo.InvariantCulture)} were held.");
        }

        var next = _state with { Files = files, Seasons = seasons };
        HistorySchemaV4.ValidateContract(next);
        var bytes = HistorySchemaV4.Serialize(next);

        var newPath = NewPathFor(_path);
        try
        {
            WriteFlushed(newPath, bytes, FileMode.Create);
            _publishStepForTest?.Invoke("new-written");
            File.Replace(newPath, _path, BackupPathFor(_path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new HistoryException(HistoryError.PersistFailed,
                $"could not save the career at '{_path}' — {ex.Message} The previous save stands.", ex);
        }
        _state = next;
    }

    public void Dispose()
    {
        _reservationsClosed = true;
        ReleaseLock();
    }

    private void ReleaseLock()
    {
        var h = _lockHandle;
        _lockHandle = null;
        h?.Dispose();
    }

    private void RequireLock(string what)
    {
        if (_lockHandle is null)
            throw new HistoryException(HistoryError.PersistFailed,
                $"the history lock has already been released; {what}.");
    }

    // ── File primitives ──────────────────────────────────────────────────────
    //  Temp names come from Guid.NewGuid() — deliberately NOT any simulation RNG: an
    //  allocator that drew from a game stream would change the basketball by saving.

    private static byte[] ReadAll(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException(HistoryError.PersistFailed, $"could not read '{path}' — {ex.Message}", ex);
        }
    }

    private static void WriteFlushed(string path, byte[] bytes, FileMode mode)
    {
        using var fs = new FileStream(path, mode, FileAccess.Write, FileShare.None);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true);
    }

    /// <summary>A whole temp file in the same folder, flushed, then moved to its final name without
    /// overwriting (so an existing file is never replaced).</summary>
    private static void PublishNewFile(string final, byte[] bytes)
    {
        var folder = System.IO.Path.GetDirectoryName(final)!;
        Directory.CreateDirectory(folder);
        var temp = System.IO.Path.Combine(folder, $".charm-new-{Guid.NewGuid():N}.tmp");
        try
        {
            WriteFlushed(temp, bytes, FileMode.CreateNew);
            File.Move(temp, final, overwrite: false);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>Move a file into the career's orphans folder, keeping <paramref name="relative"/>; a
    /// name already taken there gets `.1`, `.2`, … Returns the relative name used. Byte for byte:
    /// a rename within the career's folder, never a delete.</summary>
    private static string MoveToOrphans(string careerFull, string source, string relative)
    {
        var orphans = SiblingFolderFor(careerFull, "orphans");
        var target = System.IO.Path.Combine(orphans, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        var free = target;
        var used = relative;
        for (var k = 1; File.Exists(free) || Directory.Exists(free); k++)
        {
            free = target + "." + k.ToString(CultureInfo.InvariantCulture);
            used = relative + "." + k.ToString(CultureInfo.InvariantCulture);
        }
        File.Move(source, free, overwrite: false);
        return System.IO.Path.GetFileName(orphans) + "/" + used;
    }
}
