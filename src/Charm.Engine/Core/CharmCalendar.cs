using System.Globalization;

namespace Charm.Engine;

// ============================================================================
//  S91 — THE CALENDAR.
//
//  Before this file the engine had no concept of time: a season was 5,205 games
//  in an arbitrary order and nothing carried from one to the next. This is the
//  clock. It answers "what day is it"; it does NOT answer "who plays whom" —
//  that is the scheduler's job (S92), and building the two together is how a
//  calendar quietly bends around whatever the scheduler needed that day.
//
//  ★ THE RULE THAT SURVIVED THREE PROMPT REVISIONS: the calendar says what day
//  it is; a competition says when it plays. D1 is ONE LAYER of the sport. The
//  NIT plays the midweek nights between D1 weekends; D2, D3 and JUCO run their
//  own brackets in the same window. So legality is ONE CONTINUOUS SPAN from
//  November 1 to the D1 championship Monday, and the ten D1 tournament dates are
//  REFERENCE DATA that permit and forbid nothing. Emmett, 2026-08-02: "Every
//  single day from Nov. 1st to the D1 championship needs to be a legal gameday,
//  regardless of how many games end up on it, be it 0 or 1,000."
//
//  ★ NOTHING IS AUTHORED AND NOTHING IS SHIPPED AS DATA. The rules are the same
//  in every century, so 1850 and 2400 both come out right from one code path
//  (R1). Every boundary is DERIVED from two anchors — November 1 and the third
//  Sunday in March — never stored.
//
//  ★ NOT A MATERIALISED DAY GRAPH. "Build the calendar" does not mean 365
//  persistent day objects per year. These are rules over a `DateOnly`.
//
//  ★ NO WALL CLOCK, ANYWHERE, DIRECT OR INDIRECT. There is no parameterless
//  "current calendar" entry point in this file and there must never be one: a
//  career simulation whose calendar can read the host machine's date is one
//  timezone away from a non-deterministic save. Every entry point below demands
//  an explicit year or an explicit date. Phase 82's A11 enforces this by
//  reflection AND by scanning the source.
// ============================================================================

/// <summary>How a date relates to a basketball season. Three outcomes, and the third
/// exists because "this date is in the offseason" and "the season this date needs cannot
/// be represented" are DIFFERENT FACTS. One silently standing in for the other is exactly
/// the kind of thing that surfaces as a wrong answer decades into a career.</summary>
public enum SeasonMembership
{
    /// <summary>The date falls inside a representable season's legal span.</summary>
    InSeason,

    /// <summary>A valid answer: the date is between one championship and the next
    /// November 1. Recruiting, development and the rest eventually live here.</summary>
    Offseason,

    /// <summary>The date would belong to a season whose start year falls outside
    /// <see cref="CharmCalendar.MinSeasonStartYear"/>..<see cref="CharmCalendar.MaxSeasonStartYear"/>.
    /// Only reachable in January–April of year 0001 and November–December of year 9999.
    /// Classified rather than null, deliberately.</summary>
    SeasonOutsideSupportedRange,
}

/// <summary>The answer to "which season does this date belong to". Carries the start year
/// only when one exists; <see cref="Label"/> is empty otherwise.</summary>
public readonly record struct SeasonLookupResult(SeasonMembership Membership, int StartYear)
{
    public bool IsInSeason => Membership == SeasonMembership.InSeason;

    /// <summary>R3 — a season is named `XXXX-XXXX` and never a single year, because it
    /// crosses New Year's and carries both. Zero-padded so 0001-0002 sorts and prints
    /// like every other season.</summary>
    public string Label => IsInSeason
        ? StartYear.ToString("0000", CultureInfo.InvariantCulture) + "-"
          + (StartYear + 1).ToString("0000", CultureInfo.InvariantCulture)
        : string.Empty;
}

/// <summary>Universal civil-date facts and the global bounds of legal play. Every method
/// takes an explicit year or date — see the wall-clock note in the file header.
///
/// <para>★ CALENDAR CONVENTION: all dates are PROLEPTIC GREGORIAN — modern Gregorian rules
/// applied backward and forward uniformly. The game does not model regional
/// Julian-to-Gregorian adoption. That is what makes a deterministic custom world starting
/// in 1850 possible, and it avoids pretending there was one universal civil calendar
/// everywhere before 1582.</para>
///
/// <para>★ THE CIVIL ARITHMETIC IS THE PLATFORM'S, ON PURPOSE. Weekday, month length and
/// leap status come from <see cref="DateOnly"/> / <see cref="DateTime.IsLeapYear"/>. This
/// file's value is basketball calendar POLICY; a hand-written Zeller-style formula would
/// be a second implementation of a solved problem with its own bugs. Phase 82 therefore
/// checks the platform against weekdays sourced INDEPENDENTLY of it — a table computed by
/// the same library would only prove the library agrees with itself.</para></summary>
public static class CharmCalendar
{
    /// <summary>The civil calendar supports the full representable range. Weekday, month
    /// length, leap status and the printed year all work at both ends.</summary>
    public const int MinYear = 1;

    /// <inheritdoc cref="MinYear"/>
    public const int MaxYear = 9999;

    /// <summary>A SEASON is narrower than the civil calendar at both ends, and for one
    /// reason: a season needs the FOLLOWING year to exist, because it crosses New Year's.
    /// So the last constructible season starts in 9998.</summary>
    public const int MinSeasonStartYear = 1;

    /// <inheritdoc cref="MinSeasonStartYear"/>
    public const int MaxSeasonStartYear = 9998;

    /// <summary>R4 — November 1 is the FIRST LEGAL DAY for games. A FLOOR, not a start
    /// line: nothing is forced onto it, the scheduler simply cannot place a game earlier.
    /// Chosen over "the first Monday in November" because a weekday anchor would pin
    /// opening night to a fixed weekday for no benefit and produce a differently-shaped
    /// calendar every year.</summary>
    public const int FirstLegalMonth = 11;

    /// <inheritdoc cref="FirstLegalMonth"/>
    public const int FirstLegalDay = 1;

    /// <summary>R6 — the season ends on the championship Monday, 22 days after Selection
    /// Sunday. Everything in the postseason derives from that one anchor.</summary>
    public const int ChampionshipOffset = 22;

    // ── civil facts ─────────────────────────────────────────────────────────

    /// <summary>Divisible by 4 EXCEPT centuries, EXCEPT centuries divisible by 400.
    /// 1900 no, 2000 yes, 1850 no, 2400 yes. A naive divisible-by-4 rule is right for
    /// every year anyone tests casually and wrong three times in four centuries, and a
    /// career that drifts one day has every weekday in it wrong thereafter.</summary>
    public static bool IsLeapYear(int year)
    {
        RequireCivilYear(year);
        return DateTime.IsLeapYear(year);
    }

    /// <summary>R2 — a COMPLETE civil year: 365 days normally, 366 in a leap year. The
    /// season is one stretch inside a year that runs all the way round.</summary>
    public static int DaysInYear(int year) => IsLeapYear(year) ? 366 : 365;

    public static int DaysInMonth(int year, int month)
    {
        RequireCivilYear(year);
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), month, "month must be 1..12.");
        return DateTime.DaysInMonth(year, month);
    }

    public static DayOfWeek WeekdayOf(DateOnly date) => date.DayOfWeek;

    /// <summary>R5 — Selection Sunday is the THIRD SUNDAY IN MARCH: the one date the whole
    /// sport agrees on. Given the CIVIL year the March falls in — so the season that ends
    /// here started the previous November.</summary>
    public static DateOnly ThirdSundayInMarch(int marchYear)
    {
        RequireCivilYear(marchYear);
        var first = new DateOnly(marchYear, 3, 1);
        var toSunday = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(toSunday + 14);
    }

    /// <summary>The last legal day of the season whose March falls in this civil year.
    /// Selection Sunday is always March 15–21, so this is always April 6–12 and can never
    /// run off the end of the year.</summary>
    public static DateOnly ChampionshipDayInMarchYear(int marchYear)
        => ThirdSundayInMarch(marchYear).AddDays(ChampionshipOffset);

    // ── legal play, and season membership: THE SAME SPAN ────────────────────

    /// <summary>Whether basketball may legally be played on this date.
    ///
    /// <para>★ ONE CONTINUOUS SPAN, `[November 1, championship Monday]`, inclusive at both
    /// ends. Every day in it is a legal game day — zero games or a thousand, the calendar
    /// does not care and must not. Not the rest days between D1 weekends, not Selection
    /// Sunday, not the Sunday before the final: the NIT is on those nights, and so are D2,
    /// D3 and JUCO.</para>
    ///
    /// <para>★ THIS ANSWERS AT BOTH EDGES OF THE CIVIL RANGE, where the season LABEL
    /// cannot. November 1850 is legal whether or not anyone can name its season; the only
    /// thing that becomes unrepresentable at the edges is the `XXXX-XXXX` name, which is
    /// what <see cref="SeasonOf"/> reports separately.</para></summary>
    public static bool IsLegalGameDate(DateOnly date)
        => date.Month >= FirstLegalMonth
           || date <= ChampionshipDayInMarchYear(date.Year);

    /// <summary>Identical to <see cref="IsLegalGameDate"/> — one span, one meaning. r3 had
    /// these as two different things to justify making legality discrete; with legality
    /// continuous there is nothing left to separate, and two identical spans under two
    /// names is the kind of duplication that drifts apart later.</summary>
    public static bool IsInBasketballSeason(DateOnly date) => IsLegalGameDate(date);

    /// <summary>Which season this date belongs to, DATE-CENTRIC — because one printed civil
    /// year touches TWO seasons. January–April of 2027 belongs to `2026-2027`;
    /// November–December of 2027 belongs to `2027-2028`; the middle months belong to none.
    /// So there is no single season name for a year.</summary>
    public static SeasonLookupResult SeasonOf(DateOnly date)
    {
        if (date.Month >= FirstLegalMonth)
        {
            var start = date.Year;
            return start > MaxSeasonStartYear
                ? new SeasonLookupResult(SeasonMembership.SeasonOutsideSupportedRange, 0)
                : new SeasonLookupResult(SeasonMembership.InSeason, start);
        }

        if (date <= ChampionshipDayInMarchYear(date.Year))
        {
            var start = date.Year - 1;
            return start < MinSeasonStartYear
                ? new SeasonLookupResult(SeasonMembership.SeasonOutsideSupportedRange, 0)
                : new SeasonLookupResult(SeasonMembership.InSeason, start);
        }

        return new SeasonLookupResult(SeasonMembership.Offseason, 0);
    }

    internal static void RequireCivilYear(int year)
    {
        if (year is < MinYear or > MaxYear)
            throw new ArgumentOutOfRangeException(
                nameof(year), year,
                "year must be " + MinYear.ToString(CultureInfo.InvariantCulture) + ".."
                + MaxYear.ToString(CultureInfo.InvariantCulture) + ".");
    }
}

/// <summary>One season, anchored by its START year. `new BasketballSeasonCalendar(2026)`
/// derives November 1 2026, Selection Sunday in March 2027, the championship at +22, and
/// the label `2026-2027`.
///
/// <para>★ ANCHORED BY THE START YEAR ON PURPOSE. "The 2027 calendar" could mean the civil
/// year, the season ending in 2027, or the season beginning in it — three different
/// answers. The start year removes the ambiguity. A printed civil year queries BOTH
/// seasons that touch it.</para></summary>
public sealed class BasketballSeasonCalendar
{
    /// <summary>R6 — the D1 spine, as offsets from Selection Sunday. The first two weekends
    /// run Thu–Fri–Sat–Sun down to the Final Four, which is the two games on Saturday, and
    /// the championship is the Monday after.
    ///
    /// <para>★ THESE ARE ANCHORS, NOT PERMISSIONS. `+8` is not a D1 tournament date and IS
    /// a legal game day. They are exposed so a future scheduler can place THAT bracket;
    /// they gate nothing for the NIT, D2, D3 or JUCO.</para></summary>
    public static readonly IReadOnlyList<int> D1TournamentOffsets =
        new[] { 4, 5, 6, 7, 11, 12, 13, 14, 20, 22 };

    private readonly DateOnly[] _tournamentDates;

    public BasketballSeasonCalendar(int startYear)
    {
        if (startYear is < CharmCalendar.MinSeasonStartYear or > CharmCalendar.MaxSeasonStartYear)
            throw new ArgumentOutOfRangeException(
                nameof(startYear), startYear,
                "a season start year must be "
                + CharmCalendar.MinSeasonStartYear.ToString(CultureInfo.InvariantCulture) + ".."
                + CharmCalendar.MaxSeasonStartYear.ToString(CultureInfo.InvariantCulture)
                + " — a season needs the following year to exist.");

        StartYear = startYear;
        FirstLegalDay = new DateOnly(startYear, CharmCalendar.FirstLegalMonth, CharmCalendar.FirstLegalDay);
        SelectionSunday = CharmCalendar.ThirdSundayInMarch(startYear + 1);
        ChampionshipDay = SelectionSunday.AddDays(CharmCalendar.ChampionshipOffset);

        _tournamentDates = new DateOnly[D1TournamentOffsets.Count];
        for (var i = 0; i < D1TournamentOffsets.Count; i++)
            _tournamentDates[i] = SelectionSunday.AddDays(D1TournamentOffsets[i]);
    }

    public int StartYear { get; }

    public int EndYear => StartYear + 1;

    /// <summary>R3 — `XXXX-XXXX`, never a single year.</summary>
    public string Label
        => StartYear.ToString("0000", CultureInfo.InvariantCulture) + "-"
           + EndYear.ToString("0000", CultureInfo.InvariantCulture);

    /// <summary>November 1. A floor, never a requirement — nothing is forced onto it.</summary>
    public DateOnly FirstLegalDay { get; }

    /// <summary>The third Sunday in March. ★ A LEGAL PLAYING DAY. It is D1's announcement
    /// day and means nothing to a JUCO team, which may be playing that night.</summary>
    public DateOnly SelectionSunday { get; }

    /// <summary>The championship Monday, +22. The last legal day of the season, inclusive.</summary>
    public DateOnly ChampionshipDay { get; }

    /// <inheritdoc cref="ChampionshipDay"/>
    public DateOnly LastLegalDay => ChampionshipDay;

    /// <summary>The ten D1 tournament dates, in order. REFERENCE DATA — see
    /// <see cref="D1TournamentOffsets"/>.</summary>
    public IReadOnlyList<DateOnly> D1TournamentDates => _tournamentDates;

    /// <summary>The total number of legal days in this season, both ends inclusive.
    /// REPORTED, never judged: whether ~30 games and a conference bracket FIT in it is the
    /// scheduler's question, asked when it knows its own constraints.</summary>
    public int LegalDayCount => ChampionshipDay.DayNumber - FirstLegalDay.DayNumber + 1;

    /// <summary>Legal days from November 1 up to and including Selection Sunday — the
    /// regular-season-and-conference-tournament stretch, reported for the same reason.</summary>
    public int DaysToSelectionSunday => SelectionSunday.DayNumber - FirstLegalDay.DayNumber + 1;

    /// <summary>Whether this date falls in THIS season's one continuous legal span.</summary>
    public bool Contains(DateOnly date)
        => date >= FirstLegalDay && date <= ChampionshipDay;

    /// <inheritdoc cref="Contains"/>
    public bool IsLegalGameDate(DateOnly date) => Contains(date);
}
