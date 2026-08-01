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
}

/// <summary>Every history failure, carrying its classification. Tests assert
/// <see cref="Error"/>, never the message text.</summary>
public sealed class HistoryException : Exception
{
    public HistoryError Error { get; }

    public HistoryException(HistoryError error, string message, Exception? inner = null)
        : base(message, inner) => Error = error;
}
