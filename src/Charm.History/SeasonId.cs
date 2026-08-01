using System.Globalization;

namespace Charm.History;

// ============================================================================
//  S89 — ONE SCHEDULED SEASON in one world history.
//
//  Reserved and durably written BEFORE the schedule is built, so a season that
//  fails to construct burns its number permanently rather than handing it to the
//  next attempt. See PersonId.cs for the full reasoning on why this type refuses
//  to order, convert or expose its number.
// ============================================================================

public readonly record struct SeasonId
{
    private readonly long _value;

    private SeasonId(long value) => _value = value;

    internal static SeasonId FromRaw(long value)
    {
        if (value < 1)
            throw new HistoryException(HistoryError.InvalidIdentity,
                $"a season id must be 1 or greater (got {value.ToString(CultureInfo.InvariantCulture)}).");
        return new SeasonId(value);
    }

    internal long Raw => _value;

    public bool IsValid => _value >= 1;

    public override string ToString()
        => _value >= 1 ? "season:" + _value.ToString(CultureInfo.InvariantCulture) : "season:invalid";
}
