using System.Globalization;

namespace Charm.History;

// ============================================================================
//  S89 — A PERSON'S PERMANENT NUMBER.
//
//  One human being in one world history. Issued once, never reissued, never
//  reused — not even after the player he was attached to is thrown away.
//
//  ★ WHAT THIS TYPE REFUSES TO DO, AND WHY.
//
//  It will not compare. There is no `<`, no `>`, no `IComparable`, no sort.
//  A lower number does not mean older, better, taller, or drafted earlier — it
//  means nothing at all except "a different person". The moment ordering exists,
//  something in the game will sort by it and produce a leaderboard that looks
//  right and is meaningless.
//
//  It will not convert. There is no cast to `long`, no cast to `int`, and no
//  cast to `SeasonId` or `GameId`. A person number and a game number are not
//  the same kind of thing, and the compiler is what says so.
//
//  It WILL compare for equality, and it will hash. "Is this the same man?" is
//  the one question identity exists to answer, and dictionary lookup is how the
//  stat layer finds him. Record structs generate `==`, `!=`, `Equals` and
//  `GetHashCode` over every field including the private one below, which is
//  exactly the behaviour wanted.
//
//  ZERO IS NOT A PERSON. Issuance starts at 1, so `default(PersonId)` is invalid
//  rather than being person zero. A struct cannot forbid `default`, so the
//  enforcement lives at every construction boundary (the frozen map, the season
//  record) rather than here — see `IdentityGuard`.
// ============================================================================

public readonly record struct PersonId
{
    private readonly long _value;

    private PersonId(long value) => _value = value;

    /// <summary>The one door in. Internal: only the allocator and the serializer
    /// in this assembly may turn a number into an identity.</summary>
    internal static PersonId FromRaw(long value)
    {
        if (value < 1)
            throw new HistoryException(HistoryError.InvalidIdentity,
                $"a person id must be 1 or greater (got {value.ToString(CultureInfo.InvariantCulture)}).");
        return new PersonId(value);
    }

    /// <summary>The raw number, for persistence only. Internal — see the csproj header.</summary>
    internal long Raw => _value;

    /// <summary>True for an issued identity, false for `default`. Carries no number out.</summary>
    public bool IsValid => _value >= 1;

    /// <summary>Canonical display, stable enough for a log line and deliberately NOT a
    /// declared external format: there is no parse method this session, so nothing can
    /// round-trip a printed string back into an identity and skip the allocator.</summary>
    public override string ToString()
        => _value >= 1 ? "person:" + _value.ToString(CultureInfo.InvariantCulture) : "person:invalid";
}
