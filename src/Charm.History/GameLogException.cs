namespace Charm.History;

// ============================================================================
//  S90 — EVERY WAY A RETENTION LOG CAN REFUSE, each with its own name.
//
//  Same discipline as HistoryError, for the same reason: the suite asserts the
//  CODE, never the message text, so improving the wording can never quietly
//  stop a check from meaning anything.
//
//  ★ THE ONE RULE THAT SHAPES THIS WHOLE ENUM: a reader must never hand back
//  data it is not certain about. There is no "probably fine" classification and
//  no partial return from `ReadFinalized`. A retention log is the only copy of
//  what happened — a season page can be regenerated from a seed, a career
//  cannot — so an unreadable byte is a refusal, never a best effort.
// ============================================================================

public enum GameLogError
{
    // ── Writer-side refusals ─────────────────────────────────────────────────
    /// <summary>A finalized `.log` already exists for this season. No resume, no overwrite.</summary>
    LogAlreadyExists,
    /// <summary>An `.inprogress` file is present — a crashed run. Never silently continued.</summary>
    CrashedLogPresent,
    /// <summary>Another writer holds this season's lock.</summary>
    LogLockUnavailable,
    /// <summary>A filesystem operation failed; the log is not durable.</summary>
    LogPersistFailed,
    /// <summary>An operation was attempted in the wrong writer state (§4.4's machine).</summary>
    InvalidWriterState,
    /// <summary>A world fingerprint string that is not `sha256-v1:` + 64 lowercase hex.</summary>
    MalformedWorldFingerprint,
    /// <summary>A schedule fingerprint that is not 64 lowercase hex.</summary>
    MalformedScheduleFingerprint,
    /// <summary>A history id that is not 32 lowercase hex.</summary>
    MalformedHistoryId,
    /// <summary>A string field whose encoded form does not fit its fixed width. NEVER truncated.</summary>
    StringTooLong,
    /// <summary>A game block offered out of order, or a duplicate ordinal/gameId.</summary>
    BlockOutOfOrder,
    /// <summary>A row that the participation predicate says should not exist, or a duplicate person.</summary>
    InvalidRow,
    /// <summary>A roster entry that is out of order, duplicated, or outside a domain.</summary>
    InvalidRosterEntry,

    // ── Reader-side refusals ─────────────────────────────────────────────────
    /// <summary>The file does not begin with "CHRMGLOG".</summary>
    WrongMagic,
    /// <summary>A schema version this build cannot read (file, roster, block or row).</summary>
    UnsupportedLogVersion,
    /// <summary>The header's history id is not the one the caller expects.</summary>
    HistoryIdMismatch,
    /// <summary>The header's world digest is not the one the caller expects.</summary>
    WorldDigestMismatch,
    /// <summary>The header's season id is not the one the caller expects.</summary>
    SeasonIdMismatch,
    /// <summary>The header's schedule digest is not the one the caller expects.</summary>
    ScheduleDigestMismatch,
    /// <summary>A reserved field is not zero.</summary>
    NonZeroReserved,
    /// <summary>A count or size outside its representable/permitted domain.</summary>
    DomainViolation,
    /// <summary>A fixed-width string that is not valid UTF-8, or has a nonzero byte after a zero.</summary>
    MalformedString,
    /// <summary>A position byte that is not a defined enum value.</summary>
    InvalidPosition,
    /// <summary>A scoutRank that is NaN or infinite.</summary>
    NonFiniteValue,
    /// <summary>The roster section's checksum does not match its bytes. Fatal everywhere.</summary>
    RosterChecksumMismatch,
    /// <summary>A complete game block whose checksum does not match. Fatal everywhere.</summary>
    BlockChecksumMismatch,
    /// <summary>The footer's payload digest does not match the payload.</summary>
    PayloadDigestMismatch,
    /// <summary>The footer's game count disagrees with the blocks actually decoded.</summary>
    FooterGameCountMismatch,
    /// <summary>The footer's row count disagrees with the rows actually decoded.</summary>
    FooterRowCountMismatch,
    /// <summary>Bytes after the season footer.</summary>
    TrailingData,
    /// <summary>A second footer, or a block after a footer.</summary>
    MalformedStructure,
    /// <summary>A finalized `.log` with no footer at all.</summary>
    MissingFooter,
    /// <summary>Physical EOF partway through a section. The ONLY tolerated truncation, and
    /// only in `.inprogress`; the valid prefix is readable via ReadInProgressPrefix.</summary>
    IncompleteTail,
    /// <summary>An `.inprogress` whose payload ends in a valid footer — the crash between
    /// flush and rename. A recovery tool may publish it; S90's writer will not.</summary>
    FinalizedButNotPublished,
    /// <summary>A game row naming a person who is not in this file's roster section.</summary>
    RowPersonNotInRoster,
    /// <summary>A read failed at the filesystem.</summary>
    LogReadFailed,
}

/// <summary>Every retention-log failure, carrying its classification. Tests assert
/// <see cref="Error"/>, never the message text.</summary>
public sealed class GameLogException : Exception
{
    public GameLogError Error { get; }

    public GameLogException(GameLogError error, string message, Exception? inner = null)
        : base(message, inner) => Error = error;
}
