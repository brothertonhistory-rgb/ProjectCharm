using System.Globalization;

namespace Charm.Engine;

// ============================================================================
//  S91 — NAMED PERIODS.
//
//  R9, Emmett: "Eventually there will be stretches of time, some which overlap —
//  coaching carousel, recruiting, transfers, awards, summer workouts. But those
//  will come much later."
//
//  ★ THE OVERLAP IS LOAD-BEARING FOR THE STRUCTURE EVEN THOUGH NOTHING USES IT
//  YET. A day cannot BE "recruiting" if it is also "summer workouts" and also
//  "coaching carousel". So a period is a RANGE LAID OVER the calendar, and
//  arbitrarily many cover one day. A day-level "what phase is this" field would
//  have to pick a winner, and there is no winner to pick — the same error the
//  prompt caught three times over: one competition's schedule mistaken for a
//  property of the date.
//
//  ★ S91 REGISTERS NO PERIODS IN PRODUCTION. This is the shape, proven by
//  fixture periods in Phase 82, and nothing more. Naming them before anything
//  needs them is exactly the premature crystallisation the process forbids.
// ============================================================================

/// <summary>A named stretch of time laid over the calendar.
///
/// <para>★ PERIODS ARE CLOSED — both endpoints inclusive — and that is DELIBERATELY
/// INCONSISTENT with the half-open windows the calendar uses internally. The reason is
/// human: someone writing "August 1 to August 31" means both ends. Calendar windows abut
/// each other and so must be half-open to compose without an off-by-one; periods do not
/// abut anything. Stated here rather than discovered later.</para></summary>
public sealed class CalendarPeriod
{
    public CalendarPeriod(string name, DateOnly start, DateOnly end)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("a period name must be non-empty.", nameof(name));
        if (start > end)
            throw new ArgumentException(
                "a period must start on or before it ends (got "
                + Iso(start) + " -> " + Iso(end) + ").", nameof(start));

        Name = name;
        Start = start;
        End = end;
    }

    public string Name { get; }

    /// <summary>Inclusive.</summary>
    public DateOnly Start { get; }

    /// <summary>Inclusive. Equal to <see cref="Start"/> for a legal one-day period.</summary>
    public DateOnly End { get; }

    /// <summary>Total days covered, both ends inclusive. A one-day period is 1.</summary>
    public int DayCount => End.DayNumber - Start.DayNumber + 1;

    /// <summary>Periods may cross New Year's — a recruiting window running December into
    /// January is one period, not two.</summary>
    public bool CrossesNewYear => Start.Year != End.Year;

    public bool Covers(DateOnly date) => date >= Start && date <= End;

    public override string ToString()
        => Name + " [" + Iso(Start) + " .. " + Iso(End) + "]";

    private static string Iso(DateOnly d)
        => d.Year.ToString("0000", CultureInfo.InvariantCulture) + "-"
           + d.Month.ToString("00", CultureInfo.InvariantCulture) + "-"
           + d.Day.ToString("00", CultureInfo.InvariantCulture);
}

/// <summary>An immutable collection of periods, answering "what covers this day".
///
/// <para>★ CANONICAL ORDER IS PINNED, because two implementations could both be
/// deterministic and disagree: <b>Start ascending, then End ascending, then Name by
/// ORDINAL comparison, then registration ordinal.</b> The final tiebreak exists because
/// duplicate names AND exact duplicate ranges are both legal — two recruiting windows in
/// one year is a real thing — so a registration ordinal is what makes the order TOTAL.
/// Ordinal name comparison, never culture-aware: a save file must not re-sort itself
/// because the machine's language changed.</para></summary>
public sealed class CalendarTimeline
{
    private readonly CalendarPeriod[] _periods;

    public CalendarTimeline(IEnumerable<CalendarPeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);

        // The registration ordinal is the enumeration order handed in, captured once here.
        var indexed = periods.Select((p, i) => (Period: p, Ordinal: i)).ToList();
        foreach (var (p, _) in indexed)
            ArgumentNullException.ThrowIfNull(p, nameof(periods));

        _periods = indexed
            .OrderBy(x => x.Period.Start)
            .ThenBy(x => x.Period.End)
            .ThenBy(x => x.Period.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Ordinal)
            .Select(x => x.Period)
            .ToArray();
    }

    public static CalendarTimeline Empty { get; } = new(Array.Empty<CalendarPeriod>());

    public int Count => _periods.Length;

    /// <summary>Every period in canonical order, whether or not it covers anything.</summary>
    public IReadOnlyList<CalendarPeriod> All => _periods;

    /// <summary>EVERY period covering this date, in canonical order. Zero, one, or many —
    /// the many is the point.</summary>
    public IReadOnlyList<CalendarPeriod> GetPeriods(DateOnly date)
    {
        var hits = new List<CalendarPeriod>();
        foreach (var p in _periods)
            if (p.Covers(date))
                hits.Add(p);
        return hits;
    }
}
