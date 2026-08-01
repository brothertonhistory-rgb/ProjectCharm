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
    private HistoryStateV1 _state;
    private bool _reservationsClosed;

    private HistoryStore(string path, string lockPath, FileStream lockHandle, HistoryStateV1 state)
    {
        _path = path;
        _lockPath = lockPath;
        _lockHandle = lockHandle;
        _state = state;
    }

    /// <summary>The world this history is bound to. A history opened against a different
    /// world is refused, never silently rebound.</summary>
    public string WorldFingerprint => _state.WorldFingerprint;

    /// <summary>The normalized path actually used. Concurrent safety assumes every writer
    /// resolves to the same normalized path; symlink aliases are out of scope.</summary>
    public string Path => _path;

    // ── Opening ──────────────────────────────────────────────────────────────

    /// <summary>Take the lock, then load-or-create and verify. Opening an EXISTING history
    /// writes nothing: validation is read-only, so a run that fails before its first
    /// reservation leaves the file byte-identical.</summary>
    public static HistoryStore Open(string path, string worldFingerprint)
    {
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
            HistoryStateV1 state;
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
                state = HistorySchemaV1.Parse(bytes);

                if (!string.Equals(state.WorldFingerprint, worldFingerprint, StringComparison.Ordinal))
                    throw new HistoryException(HistoryError.FingerprintMismatch,
                        $"this history belongs to a different world. History '{full}' is bound to " +
                        $"{state.WorldFingerprint}, the world given is {worldFingerprint}.");
            }
            else
            {
                // First creation is an atomic publication too — never streamed straight
                // to the final path, so a half-written history can never be found there.
                state = HistoryStateV1.Fresh(worldFingerprint);
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
    private static void PublishAtomically(string path, HistoryStateV1 state)
    {
        var bytes = HistorySchemaV1.Serialize(state);
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
