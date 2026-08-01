using System.Globalization;

namespace Charm.History;

// ============================================================================
//  S89 — ONE FIXTURE in one world history.
//
//  World-unique, not season-unique: game numbers do not restart in November.
//  The season the fixture belongs to rides alongside as parent data, never
//  encoded inside the number — a game number cannot be divided, masked or
//  compared to recover its season, and that is deliberate.
//
//  See PersonId.cs for the full reasoning on ordering, conversion and equality.
// ============================================================================

public readonly record struct GameId
{
    private readonly long _value;

    private GameId(long value) => _value = value;

    internal static GameId FromRaw(long value)
    {
        if (value < 1)
            throw new HistoryException(HistoryError.InvalidIdentity,
                $"a game id must be 1 or greater (got {value.ToString(CultureInfo.InvariantCulture)}).");
        return new GameId(value);
    }

    internal long Raw => _value;

    public bool IsValid => _value >= 1;

    public override string ToString()
        => _value >= 1 ? "game:" + _value.ToString(CultureInfo.InvariantCulture) : "game:invalid";
}
