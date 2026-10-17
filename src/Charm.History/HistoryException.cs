namespace Charm.History;

// ============================================================================
//  S89 — EVERY WAY A HISTORY CAN REFUSE TO OPEN, each with its own name.
//
//  ★ Why a reason CODE rather than ten exception classes: the suite has to assert
//  the classification, and asserting on message strings is how a check quietly
//  stops meaning anything the first time someone improves the wording. The code
//  is the contract; the message is for the human reading the terminal.
//
//  ★ Why every one of these STOPS rather than recovering: a history file is the
//  only thing standing between a career and two players sharing a number. A
//  loader that treats "I could not read this" as "there is nothing here" starts
//  a fresh history at 1 on top of a real one, and every number it then issues
//  collides with a number already attached to a person. Corrupt must be loud.
// ============================================================================

public enum HistoryError
{
    /// <summary>The file is not valid JSON at all.</summary>
    MalformedJson,
    /// <summary>A required key is absent.</summary>
    MissingKey,
    /// <summary>The same key appears twice.</summary>
    DuplicateKey,
    /// <summary>A key that this schema does not define.</summary>
    UnknownKey,
    /// <summary>`format` is not "charm-history" — this is somebody else's file.</summary>
    WrongFormat,
    /// <summary>A key is present but holds the wrong JSON type.</summary>
    WrongType,
    /// <summary>`schemaVersion` is a version this build cannot read.</summary>
    UnsupportedVersion,
    /// <summary>A stored next-value is outside the valid domain (zero, negative, or at the ceiling).</summary>
    CounterOutOfDomain,
    /// <summary>This history was bound to a different world.</summary>
    FingerprintMismatch,
    /// <summary>The history path names an existing directory.</summary>
    PathIsDirectory,
    /// <summary>Another process (or another part of this one) holds the history lock.</summary>
    LockUnavailable,
    /// <summary>The reservation could not be made durable; NO identities were issued.</summary>
    PersistFailed,
    /// <summary>A negative reservation count.</summary>
    NegativeCount,
    /// <summary>A reservation that would run past the end of the number line.</summary>
    ExhaustedRange,
    /// <summary>An identity was constructed, or arrived somewhere, outside its valid domain.</summary>
    InvalidIdentity,
    /// <summary>History mode is on and something crossed a boundary without an identity.</summary>
    MissingIdentity,
    /// <summary>★ S119 — a year outside 1..9998: a stored or requested start year, or a
    /// career whose next season would open past the last season the calendar holds.</summary>
    YearOutOfDomain,
    /// <summary>★ S119 — a year was named for an existing career that started in a different
    /// one. A career's first year is fixed at creation and never moves.</summary>
    StartYearMismatch,

    // ── ★ S125 (B1b) — the career file v4: its fingerprint, its list, its backup. ──

    /// <summary>The career file's trailer is missing or does not match its bytes — half-written,
    /// or edited by hand. Recovered from the backup when the backup is whole.</summary>
    Torn,
    /// <summary>A file the career lists is not on disk.</summary>
    ListedFileMissing,
    /// <summary>A file the career lists is on disk, but its bytes are not the ones listed.</summary>
    ListedFileChanged,
    /// <summary>The career file is missing or torn and its backup cannot stand in for it (missing,
    /// torn, damaged, or one of the files it lists is missing or changed).</summary>
    BackupUnusable,
    /// <summary>A listed path is not a file a career owns: absolute, `..`, `\`, out of order, or
    /// outside the six kinds (which also refuses the career file, its backup, its lock and orphans).</summary>
    ListedPathInvalid,
    /// <summary>A path is listed twice.</summary>
    ListedPathDuplicate,
    /// <summary>A listed hash is not 64 lowercase hex characters.</summary>
    ListedHashInvalid,
    /// <summary>The season list is out of order, has a gap or a repeat, disagrees with the season
    /// counter, or carries a malformed rules entry.</summary>
    SeasonsInvalid,
    /// <summary>The list and the seasons disagree: a season file for an unpublished season, or no
    /// `league-0`.</summary>
    ManifestMismatch,
    /// <summary>★ S127 — the mid-season save block is malformed, or does not fit the career: a snapshot
    /// listed with no block, a block naming another season or snapshot, held numbers below the published
    /// ones. (Replaces S125's `ProgressNotNull`, from when a save existed only at a season's close.)</summary>
    ProgressInvalid,
    /// <summary>A league state file is malformed, belongs to another career or another season.</summary>
    LeagueStateInvalid,
    /// <summary>The league state disagrees with the world's prestige (the B1 equality assertion).</summary>
    LeagueStateMismatch,
    /// <summary>A save would write a file under a name the career already lists (K8: a listed file
    /// is never written to, renamed or deleted by the game).</summary>
    ListedNameTaken,

    // ── ★ S127 (B2a) — a season saved mid-way. ──

    /// <summary>The career holds a season saved mid-way under another build or other settings; B2a
    /// refuses to resume it (B2b offers to restart the season).</summary>
    ProgressRulesChanged,
    /// <summary>A mid-season save disagrees with the career file that lists it: its header, or the
    /// season it restores to (the season, the seed, the next day, the held numbers, the games played).</summary>
    SnapshotMismatch,
    /// <summary>A mid-season save hashes as listed but cannot be decoded: truncated, a dangling
    /// reference, an unknown type, a length past its end, bytes after its end.</summary>
    SnapshotMalformed,
}

/// <summary>Every history failure, carrying its classification. Tests assert
/// <see cref="Error"/>, never the message text.</summary>
public sealed class HistoryException : Exception
{
    public HistoryError Error { get; }

    public HistoryException(HistoryError error, string message, Exception? inner = null)
        : base(message, inner) => Error = error;
}
