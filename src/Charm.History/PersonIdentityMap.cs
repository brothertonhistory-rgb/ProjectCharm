using System.Collections.Frozen;
using System.Globalization;

namespace Charm.History;

// ============================================================================
//  S89 — THE TRANSPORT.
//
//  The people are numbered where they come into existence, and that is a
//  different place from where their statistics are counted. Between the two sits
//  this: one map, built once at the moment the pool is made, frozen, and then
//  read-only for the rest of the run.
//
//  ★ IT IS A BIJECTION AND THAT IS CHECKED, NOT ASSUMED. No pool slot may appear
//  twice, no person number may appear twice, and every admitted person must be in
//  it. A duplicate on either side means two men are sharing a career; a gap means
//  somebody's season lands nowhere.
//
//  ★ A MISS IS AN ERROR, NEVER A SKIP. Asking for a man who is not in the map
//  throws. The tempting alternative — return nothing and move on — is exactly how
//  a season quietly finishes with 400 players' statistics missing and every
//  conservation check still green, because the totals only ever counted the men
//  who were found.
// ============================================================================

public sealed class PersonIdentityMap
{
    private readonly FrozenDictionary<int, PersonId> _byKey;

    private PersonIdentityMap(FrozenDictionary<int, PersonId> byKey) => _byKey = byKey;

    public int Count => _byKey.Count;

    /// <summary>Build and freeze. `pairs` must cover every admitted person exactly once.</summary>
    public static PersonIdentityMap Freeze(IEnumerable<KeyValuePair<int, PersonId>> pairs)
    {
        var forward = new Dictionary<int, PersonId>();
        var seenIds = new HashSet<PersonId>();
        foreach (var (key, id) in pairs)
        {
            IdentityGuard.Require(id, $"the identity map entry for pool slot {key.ToString(CultureInfo.InvariantCulture)}");
            if (!forward.TryAdd(key, id))
                throw new HistoryException(HistoryError.InvalidIdentity,
                    $"pool slot {key.ToString(CultureInfo.InvariantCulture)} appears twice in the identity map.");
            if (!seenIds.Add(id))
                throw new HistoryException(HistoryError.InvalidIdentity,
                    $"{id} is assigned to more than one pool slot — two men would share one career.");
        }
        return new PersonIdentityMap(forward.ToFrozenDictionary());
    }

    /// <summary>The identity of an admitted person. A miss throws — see the header.</summary>
    public PersonId this[int key]
        => _byKey.TryGetValue(key, out var id)
            ? id
            : throw new HistoryException(HistoryError.MissingIdentity,
                $"pool slot {key.ToString(CultureInfo.InvariantCulture)} has no identity in the frozen map; " +
                "history mode requires every admitted person to carry one.");

    public bool Contains(int key) => _byKey.ContainsKey(key);

    /// <summary>Every pool slot in the map, for coverage assertions.</summary>
    public IEnumerable<int> Keys => _byKey.Keys;
}

// ============================================================================
//  S89 — THE CONSTRUCTION BOUNDARY.
//
//  A struct cannot forbid `default`, so `default(PersonId)` exists and is
//  invalid. Rather than inventing a sentinel and teaching every reader what it
//  means, the domain simply starts at 1 — which makes the C# default harmless
//  everywhere EXCEPT at the doors where an identity is attached to something
//  real. Those doors check.
//
//  Absence is expressed by `PersonId?` being null, never by a zero identity.
// ============================================================================
public static class IdentityGuard
{
    public static PersonId Require(PersonId id, string where)
        => id.IsValid ? id : throw new HistoryException(HistoryError.MissingIdentity,
            $"{where} has no valid person identity (an uninitialised identity reached a boundary).");

    public static SeasonId Require(SeasonId id, string where)
        => id.IsValid ? id : throw new HistoryException(HistoryError.MissingIdentity,
            $"{where} has no valid season identity (an uninitialised identity reached a boundary).");

    public static GameId Require(GameId id, string where)
        => id.IsValid ? id : throw new HistoryException(HistoryError.MissingIdentity,
            $"{where} has no valid game identity (an uninitialised identity reached a boundary).");

    /// <summary>History mode's once-per-run contract check: a nullable identity that must
    /// be present here. Downstream code does NOT re-check — that repeated `is not null`
    /// noise is exactly what §4.2 exists to prevent.</summary>
    public static PersonId Require(PersonId? id, string where)
        => id is { } v ? Require(v, where) : throw new HistoryException(HistoryError.MissingIdentity,
            $"{where} is missing its person identity in history mode.");

    public static SeasonId Require(SeasonId? id, string where)
        => id is { } v ? Require(v, where) : throw new HistoryException(HistoryError.MissingIdentity,
            $"{where} is missing its season identity in history mode.");

    public static GameId Require(GameId? id, string where)
        => id is { } v ? Require(v, where) : throw new HistoryException(HistoryError.MissingIdentity,
            $"{where} is missing its game identity in history mode.");
}
