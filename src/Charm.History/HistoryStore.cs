using System.Globalization;

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
//  This is the whole reason a discarded player can never come back as somebody
//  else. A scheme that filled holes would have to KNOW which numbers were free,
//  which means a list, which means the list can be wrong.
//
//  ★ ORDER PER BATCH: reserve -> make it durable -> only then hand the numbers
//  out. Backwards would mean a crash between issuing and persisting leaves the
//  file believing those numbers are still available while people are already
//  wearing them. Losing a range to a crash is free; reissuing one is fatal.
//
//  ★ THE LOCK IS A SIDECAR, NOT THE FILE ITSELF. Holding the data file open
//  exclusively and then atomically replacing that same file does not work on
//  Windows — the replace needs the file not to be held. So the lock is a
//  separate `.lock` file next to it, taken FIRST, before existence is even
//  checked (checking first is the creation race), and held across load,
//  validation, every reservation, and every replacement.
//
//  ★ NEVER A FALLBACK. If the file cannot be made durable — read-only folder,
//  full disk, another process holding the lock — no identities are issued at
//  all and the run stops. An in-memory fallback would produce a season whose
//  numbers exist nowhere, which is worse than not running.
//
//  Durability is claimed against ordinary process failure and normal atomic
//  filesystem behaviour. Not against a machine losing power mid-write.
// ============================================================================

public sealed class HistoryStore : IDisposable
{
    private readonly string _path;
    private readonly string _lockPath;
    private FileStream? _lockHandle;
    private HistoryStateV3 _state;
    private bool _reservationsClosed;

    private HistoryStore(string path, string lockPath, FileStream lockHandle, HistoryStateV3 state)
    {
        _path = path;
        _lockPath = lockPath;
        _lockHandle = lockHandle;
        _state = state;
    }

    /// <summary>The world this history is bound to. A history opened against a different
    /// world is refused, never silently rebound.</summary>
    public string WorldFingerprint => _state.WorldFingerprint;

    /// <summary>This career lineage's label — 32 lowercase hex. Every retention log written
    /// against this history carries it, which is what stops a log from one career being read
    /// into another that happens to share a world.</summary>
    public string HistoryId => _state.HistoryId;

    /// <summary>★ S119 — the civil year this career's FIRST season opens in, fixed at creation
    /// and read back off the file on every open. Season N is played in StartYear + (N - 1).</summary>
    public int StartYear => _state.StartYear;

    // ★ The id source is a seam for ONE reason: the born-v3 golden. Production mints from
    // Guid.NewGuid(), which by design produces a different file every run, so the suite could
    // never pin a newly created history byte-for-byte against a fixture. Injecting a fixed id
    // lets the suite drive the EXACT production writer and compare bytes — rather than the
    // usual alternative, which is a hand-authored "expected" file that proves only that
    // somebody typed what they expected. (S118.2: it was first opened for the v1-to-v2
    // migration golden; the migration is retired, C-60, and this is what it is for now.)
    private static Func<string> _idSource = HistorySchemaV3.MintHistoryId;

    /// <summary>Pin the lineage label for the duration of the returned scope.
    ///
    /// <para>★ PUBLIC, AND THAT IS A DELIBERATE WIDENING WORTH NAMING. Everything else in
    /// this assembly is sealed against the harness on purpose. This one door is open because
    /// the born-v3 golden has no other honest form: production mints from Guid.NewGuid(),
    /// so creating a history produces a different file every run and could never be pinned
    /// byte-for-byte. The alternative is a hand-authored "expected" file, which proves only
    /// that somebody typed what they expected — it would not be driving the production
    /// writer at all.</para>
    ///
    /// <para>It carries no raw identity value out, so S89's actual seam is untouched: this
    /// sets a label, it does not expose a number. Nothing on a production path may call it,
    /// and Phase 80 B10 (the born-v3 golden) is the only caller in the tree.</para></summary>
    public static IDisposable UseFixedHistoryIdForTests(string id)
    {
        if (!HistorySchemaV3.IsCanonicalHistoryId(id))
            throw new HistoryException(HistoryError.WrongType,
                "a test history id must be 32 lowercase hex characters.");
        var previous = _idSource;
        _idSource = () => id;
        return new Restore(() => _idSource = previous);
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

    /// <summary>★ S96 — the season number the NEXT <see cref="ReserveSeason"/> will return.
    ///
    /// <para>A read, and only a read: no reservation, no counter movement, no write, no
    /// schema or persisted-format change. It exists because host memory must know which
    /// season is about to be scheduled BEFORE the schedule is built — and the schedule is
    /// deliberately built before any number is spent (a slate that fails to build must not
    /// have burned a season id), so the memory layer cannot wait for the reservation.</para>
    ///
    /// <para>★ THE CONTRACT, exactly, because the name understates it: while this run holds
    /// the lock, the value returned here IS the value the next season reservation hands
    /// back. `Reserve` reads this same counter as its start. That equality is what makes
    /// "the previous career season is this minus one" arithmetic rather than a guess, and
    /// Phase 87 C9 proves it by peeking and then reserving.</para>
    ///
    /// <para>Deliberately NOT a `SeasonId`: this is a counter reading, not an issued
    /// identity, and wrapping it in the identity type would let a caller carry an
    /// unreserved number around as though it had been allocated.</para></summary>
    public long PeekNextSeasonId => _state.NextSeasonId;

    // ── Opening ──────────────────────────────────────────────────────────────

    /// <summary>Take the lock, then load-or-create and verify.
    ///
    /// <para>Opening an existing history writes nothing: validation is read-only, so a run
    /// that fails before its first reservation leaves the file byte-identical.</para>
    ///
    /// <para>★ S118.2 — a **v1** history (saved before S90) is refused by name, never upgraded
    /// (Emmett's ruling C-60: the game is the product, not its save files). S90 to S118.1
    /// upgraded one on open with a single migration write; that code is retired, and the
    /// refusal happens before any write, so the old file is left exactly as it was.</para>
    ///
    /// <para>★ S119 — a **v2** history is refused the same way (it has no start year, and
    /// guessing one would be a quiet call about somebody's career). A history CREATED here is
    /// born v3: the lineage label and the first season's year exist before the first number is
    /// issued. <paramref name="startYearIfNew"/> is read ONLY when the file does not exist yet;
    /// an existing career's year comes off the file, never from the caller.</para></summary>
    public static HistoryStore Open(string path, string worldFingerprint,
                                    int startYearIfNew = HistoryStateV3.DefaultStartYear)
    {
        if (!HistoryStateV3.IsStartYearInDomain(startYearIfNew))
            throw new HistoryException(HistoryError.YearOutOfDomain,
                $"a career cannot start in {startYearIfNew.ToString(CultureInfo.InvariantCulture)} — " +
                $"a season can start in {HistoryStateV3.MinStartYear}..{HistoryStateV3.MaxStartYear}.");

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
            HistoryStateV3 state;
            if (File.Exists(full))
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(full); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new HistoryException(HistoryError.PersistFailed,
                        $"could not read history '{full}' — {ex.Message}", ex);
                }
                // A parse failure is NEVER treated as "no file". Starting fresh at 1 on top
                // of a corrupt-but-real history reissues every number in it.
                //
                // The version is peeked FIRST, so an older career is refused for being older
                // rather than for whatever key the current parser happens to trip on first
                // (a v1 file would read as "missing key 'historyId'" — true, and completely
                // misleading about what is wrong).
                var version = HistorySchemaV3.PeekVersion(bytes);
                if (version == HistoryStateV3.SchemaVersion)
                {
                    state = HistorySchemaV3.Parse(bytes);
                }
                else if (version is 1 or 2)
                {
                    // ★ S118.2 (v1) and S119 (v2), C-60 — refused by name, never upgraded.
                    // Nothing has been written: the file is exactly as it was, and no log folder
                    // exists, because the writer is constructed later from a store this line
                    // never returns.
                    throw new HistoryException(HistoryError.UnsupportedVersion,
                        $"{GameLogSchemaV1.OlderVersionSentence} (history '{full}' is schemaVersion " +
                        $"{version.ToString(CultureInfo.InvariantCulture)}; this build reads 3).");
                }
                else
                {
                    throw new HistoryException(HistoryError.UnsupportedVersion,
                        $"unsupported history schemaVersion {version.ToString(CultureInfo.InvariantCulture)} " +
                        "(this build reads 3).");
                }

                if (!string.Equals(state.WorldFingerprint, worldFingerprint, StringComparison.Ordinal))
                    throw new HistoryException(HistoryError.FingerprintMismatch,
                        $"this history belongs to a different world. History '{full}' is bound to " +
                        $"{state.WorldFingerprint}, the world given is {worldFingerprint}.");
            }
            else
            {
                // First creation is an atomic publication too — never streamed straight
                // to the final path, so a half-written history can never be found there.
                // Born v3: the lineage label and the first season's year exist before the
                // first number is issued.
                state = HistoryStateV3.Fresh(_idSource(), worldFingerprint, startYearIfNew);
                PublishAtomically(full, state);
            }

            return new HistoryStore(full, lockPath, lockHandle, state);
        }
        catch
        {
            lockHandle.Dispose();
            throw;
        }
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

    /// <summary>Checked half-open arithmetic: `start = next`, `end = next + n`, persist
    /// `next = end`, issue `[start, end)`. An oversized batch rejects the WHOLE
    /// reservation with the file untouched — never a partial advance.</summary>
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

        PublishAtomically(_path, advanced);   // durable BEFORE anything is handed out
        _state = advanced;
        return next;
    }

    /// <summary>Release the lock once every reservation the run will make is complete —
    /// before the long simulation, which issues nothing.</summary>
    public void CloseReservations()
    {
        _reservationsClosed = true;
        ReleaseLock();
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

    // ── The atomic write ─────────────────────────────────────────────────────
    //  Complete temp file in the SAME folder (so the move is a rename, not a copy
    //  across volumes), managed buffers flushed, an OS-level flush to disk
    //  requested, then moved into place over the old one.
    //
    //  Temp-file uniqueness comes from `Guid.NewGuid()`, which is the idiom five
    //  suite files already use. Deliberately NOT any simulation RNG: an allocator
    //  that drew from a game stream would change the basketball by saving.
    private static void PublishAtomically(string path, HistoryStateV3 state)
    {
        var bytes = HistorySchemaV3.Serialize(state);
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
        var temp = System.IO.Path.Combine(dir, $".charm-history-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            throw new HistoryException(HistoryError.PersistFailed,
                $"could not make the history durable at '{path}' — no identities were issued. {ex.Message}", ex);
        }
    }
}
