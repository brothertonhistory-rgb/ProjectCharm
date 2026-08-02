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
    private HistoryStateV2 _state;
    private bool _reservationsClosed;

    private HistoryStore(string path, string lockPath, FileStream lockHandle, HistoryStateV2 state)
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

    // ★ The id source is a seam for ONE reason: the migration golden. Production mints from
    // Guid.NewGuid(), which by design produces a different file every run, so the suite could
    // never pin migration byte-for-byte against a fixture. Injecting a fixed id lets the suite
    // drive the EXACT production migration writer and compare bytes — rather than the usual
    // alternative, which is a hand-authored "expected" file that proves only that somebody
    // typed what they expected.
    private static Func<string> _idSource = HistorySchemaV2.MintHistoryId;

    /// <summary>Pin the lineage label for the duration of the returned scope.
    ///
    /// <para>★ PUBLIC, AND THAT IS A DELIBERATE WIDENING WORTH NAMING. Everything else in
    /// this assembly is sealed against the harness on purpose. This one door is open because
    /// the migration golden has no other honest form: production mints from Guid.NewGuid(),
    /// so a v1-to-v2 migration produces a different file every run and could never be pinned
    /// byte-for-byte. The alternative is a hand-authored "expected" file, which proves only
    /// that somebody typed what they expected — it would not be driving the production
    /// writer at all.</para>
    ///
    /// <para>It carries no raw identity value out, so S89's actual seam is untouched: this
    /// sets a label, it does not expose a number. Nothing on a production path may call it,
    /// and Phase 81 is the only caller in the tree.</para></summary>
    public static IDisposable UseFixedHistoryIdForTests(string id)
    {
        if (!HistorySchemaV2.IsCanonicalHistoryId(id))
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

    // ── Opening ──────────────────────────────────────────────────────────────

    /// <summary>Take the lock, then load-or-create and verify.
    ///
    /// <para>★ S90 corrected this comment, which used to promise that opening an existing
    /// history writes nothing. That is true of a **v2** history — validation is read-only, so
    /// a run that fails before its first reservation leaves the file byte-identical. It is NOT
    /// true of a **v1** history: opening one performs exactly one atomic migration write to
    /// give the career its lineage label, and nothing else. A career must have its identity
    /// before any retention log can bind to it.</para>
    ///
    /// <para>A history CREATED here is born v2. S90 never writes a v1 file at any instant —
    /// creating one and migrating it on first open would mean two writes and a half-created
    /// state that no reader has a name for.</para></summary>
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
            HistoryStateV2 state;
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
                // The version is peeked FIRST so the right parser runs. Handing a v2 file to
                // the v1 parser would refuse it as "unknown key 'historyId'" — true, and
                // completely misleading about what is wrong.
                var version = HistorySchemaV2.PeekVersion(bytes);
                if (version == HistoryStateV2.SchemaVersion)
                {
                    state = HistorySchemaV2.Parse(bytes);
                }
                else if (version == HistoryStateV1.SchemaVersion)
                {
                    // ── MIGRATION: one way, once, under the lock already held. ──
                    // Counters cross untouched; only the lineage label is added. A
                    // migration that moved a counter would reissue numbers already worn.
                    var v1 = HistorySchemaV1.Parse(bytes);
                    if (!string.Equals(v1.WorldFingerprint, worldFingerprint, StringComparison.Ordinal))
                        throw new HistoryException(HistoryError.FingerprintMismatch,
                            $"this history belongs to a different world. History '{full}' is bound to " +
                            $"{v1.WorldFingerprint}, the world given is {worldFingerprint}.");
                    // ★ If minting or the atomic rewrite fails, PublishAtomically throws with
                    // the v1 file byte-identical and no counter advanced — so the run stops and
                    // a retry is possible. No log folder is created here, and none can be:
                    // the writer is constructed later, from a store this line has not returned.
                    state = HistoryStateV2.FromV1(v1, _idSource());
                    PublishAtomically(full, state);
                }
                else
                {
                    throw new HistoryException(HistoryError.UnsupportedVersion,
                        $"unsupported history schemaVersion {version.ToString(CultureInfo.InvariantCulture)} " +
                        "(this build reads 1 and 2).");
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
                // Born v2: the lineage label exists before the first number is issued.
                state = HistoryStateV2.Fresh(_idSource(), worldFingerprint);
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
    private static void PublishAtomically(string path, HistoryStateV2 state)
    {
        var bytes = HistorySchemaV2.Serialize(state);
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
