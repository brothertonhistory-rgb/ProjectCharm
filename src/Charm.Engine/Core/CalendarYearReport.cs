using System.Globalization;
using System.Text;

namespace Charm.Engine;

// ============================================================================
//  S91 — THE PRINTED YEAR.
//
//  The sign-off artifact: months laid out with weekdays, the season boundaries
//  marked, Selection Sunday and championship day named. Emmett should be able to
//  look at November and say whether it starts in the right place.
//
//  ★ IT IS NOT THE PROOF OF CORRECTNESS. A visually plausible month can hide a
//  wrong season label or an off-by-one boundary. Phase 82 asserts the underlying
//  values FIRST and then asserts that this renderer reflects them.
//
//  ★ CULTURE-INVARIANT BY CONSTRUCTION, or Emmett's machine and the sandbox
//  print two different valid calendars and neither of us can tell which. So:
//  weekday and month names are HARDCODED ENGLISH ABBREVIATIONS and never asked
//  of the platform; the week always starts on Sunday and never on the machine's
//  first-day-of-week; every number is formatted with the invariant culture; and
//  newlines are literal "\n", NEVER Environment.NewLine. Phase 82's A12 renders
//  the same year under a non-English, non-Sunday-first culture and requires the
//  two strings to be byte-identical.
// ============================================================================

/// <summary>Renders one complete civil year as text. Pure: takes a year, returns a string,
/// reads no clock and no culture.</summary>
public static class CalendarYearReport
{
    private static readonly string[] WeekdayAbbr =
        { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    private static readonly string[] MonthAbbr =
        { "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
          "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    /// <summary>Marker characters, documented once here and legended in the output.</summary>
    private const char MarkOffseason  = ' ';
    private const char MarkLegal      = '.';
    private const char MarkSelection  = 'S';
    private const char MarkTournament = 'T';
    private const char MarkChampion   = 'C';

    public static string Render(int year) => Render(year, CalendarTimeline.Empty);

    public static string Render(int year, CalendarTimeline periods)
    {
        CharmCalendar.RequireCivilYear(year);
        ArgumentNullException.ThrowIfNull(periods);

        var sb = new StringBuilder();

        sb.Append("=== CALENDAR ").Append(Num(year, "0000"))
          .Append(" (proleptic Gregorian) ===\n");
        sb.Append("leap year: ").Append(CharmCalendar.IsLeapYear(year) ? "yes" : "no ")
          .Append("    days in year: ").Append(Num(CharmCalendar.DaysInYear(year), "0"))
          .Append('\n');
        sb.Append('\n');

        // ── the two seasons that touch this civil year ──────────────────────
        //  There is NO single season name for a calendar year: January-April belongs to
        //  the season that started last November, and November-December belongs to the
        //  next one. Both are named, or the reason neither can be is.
        sb.Append("seasons touching this year:\n");
        AppendSeasonLine(sb, year - 1, year, "Jan 01", ChampEndLabel(year));
        AppendSeasonLine(sb, year, year, "Nov 01", "Dec 31");
        sb.Append('\n');

        var spines = new List<BasketballSeasonCalendar>();
        foreach (var start in new[] { year - 1, year })
            if (TryMakeSeason(start, out var cal))
                spines.Add(cal!);

        foreach (var cal in spines)
        {
            AppendSpine(sb, cal);
            sb.Append('\n');
        }

        // ── the twelve months ───────────────────────────────────────────────
        for (var month = 1; month <= 12; month++)
            AppendMonth(sb, year, month, spines);

        sb.Append("legend:  ").Append(MarkLegal).Append(" legal game day   ")
          .Append(MarkSelection).Append(" Selection Sunday   ")
          .Append(MarkTournament).Append(" D1 tournament date (reference only)   ")
          .Append(MarkChampion).Append(" championship\n");
        sb.Append("         a blank day is the offseason. EVERY day from Nov 01 to the\n");
        sb.Append("         championship is legal -- the ten D1 dates permit nothing extra.\n");

        if (periods.Count > 0)
        {
            sb.Append('\n');
            sb.Append("registered periods (canonical order):\n");
            foreach (var p in periods.All)
                sb.Append("  ").Append(p.ToString()).Append('\n');
        }

        return sb.ToString();
    }

    // ── pieces ──────────────────────────────────────────────────────────────

    private static void AppendSeasonLine(StringBuilder sb, int startYear, int printedYear,
                                         string fromInYear, string toInYear)
    {
        if (!TryMakeSeason(startYear, out var cal))
        {
            sb.Append("  ").Append(Num(startYear, "0000")).Append("-")
              .Append(Num(startYear + 1, "0000"))
              .Append("   NOT REPRESENTABLE (a season needs both years inside 0001..9999)\n");
            return;
        }

        sb.Append("  ").Append(cal!.Label).Append("   ")
          .Append(Long(cal.FirstLegalDay)).Append(" -> ").Append(Long(cal.ChampionshipDay))
          .Append("    (in ").Append(Num(printedYear, "0000")).Append(": ")
          .Append(fromInYear).Append(" - ").Append(toInYear).Append(")\n");
    }

    private static void AppendSpine(StringBuilder sb, BasketballSeasonCalendar cal)
    {
        var d = cal.D1TournamentDates;
        sb.Append("season ").Append(cal.Label).Append(" spine:\n");
        sb.Append("  first legal day     ").Append(Long(cal.FirstLegalDay)).Append('\n');
        sb.Append("  Selection Sunday    ").Append(Long(cal.SelectionSunday)).Append('\n');
        sb.Append("  weekend 1           ")
          .Append(Short(d[0])).Append(" / ").Append(Short(d[1])).Append(" / ")
          .Append(Short(d[2])).Append(" / ").Append(Short(d[3])).Append('\n');
        sb.Append("  weekend 2           ")
          .Append(Short(d[4])).Append(" / ").Append(Short(d[5])).Append(" / ")
          .Append(Short(d[6])).Append(" / ").Append(Short(d[7])).Append('\n');
        sb.Append("  Final Four          ").Append(Long(d[8])).Append('\n');
        sb.Append("  championship        ").Append(Long(d[9])).Append('\n');
        sb.Append("  legal days Nov 01 -> championship: ")
          .Append(Num(cal.LegalDayCount, "0"))
          .Append("   (Nov 01 -> Selection Sunday: ")
          .Append(Num(cal.DaysToSelectionSunday, "0")).Append(")\n");
    }

    private static void AppendMonth(StringBuilder sb, int year, int month,
                                    List<BasketballSeasonCalendar> spines)
    {
        sb.Append(MonthAbbr[month - 1]).Append(' ').Append(Num(year, "0000")).Append('\n');
        for (var i = 0; i < 7; i++)
            sb.Append(WeekdayAbbr[i]).Append(' ');
        sb.Append('\n');

        var first = new DateOnly(year, month, 1);
        var lead = (int)first.DayOfWeek;             // Sunday == 0, always, never the locale's
        for (var i = 0; i < lead; i++) sb.Append("    ");

        var days = CharmCalendar.DaysInMonth(year, month);
        var column = lead;
        for (var day = 1; day <= days; day++)
        {
            var date = new DateOnly(year, month, day);
            sb.Append(Num(day, "0").PadLeft(3)).Append(MarkerFor(date, spines));
            column++;
            if (column == 7) { sb.Append('\n'); column = 0; }
        }
        if (column != 0) sb.Append('\n');

        if (month == 2 && CharmCalendar.IsLeapYear(year))
            sb.Append("  Feb 29 -- leap day\n");

        sb.Append('\n');
    }

    private static char MarkerFor(DateOnly date, List<BasketballSeasonCalendar> spines)
    {
        foreach (var cal in spines)
        {
            if (!cal.Contains(date)) continue;
            if (date == cal.ChampionshipDay) return MarkChampion;
            if (date == cal.SelectionSunday) return MarkSelection;
            foreach (var t in cal.D1TournamentDates)
                if (t == date) return MarkTournament;
            return MarkLegal;
        }
        return MarkOffseason;
    }

    private static bool TryMakeSeason(int startYear, out BasketballSeasonCalendar? cal)
    {
        if (startYear is < CharmCalendar.MinSeasonStartYear or > CharmCalendar.MaxSeasonStartYear)
        {
            cal = null;
            return false;
        }
        cal = new BasketballSeasonCalendar(startYear);
        return true;
    }

    private static string ChampEndLabel(int printedYear)
    {
        if (printedYear - 1 < CharmCalendar.MinSeasonStartYear) return "n/a";
        var c = new BasketballSeasonCalendar(printedYear - 1).ChampionshipDay;
        return MonthTitle(c.Month) + " " + Num(c.Day, "00");
    }

    // ── formatting, all invariant ───────────────────────────────────────────

    private static string Num(int v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

    /// <summary>"Sun Mar 21 2027".</summary>
    private static string Long(DateOnly d)
        => WeekdayAbbr[(int)d.DayOfWeek] + " " + MonthTitle(d.Month) + " "
           + Num(d.Day, "00") + " " + Num(d.Year, "0000");

    /// <summary>"Thu Mar 25".</summary>
    private static string Short(DateOnly d)
        => WeekdayAbbr[(int)d.DayOfWeek] + " " + MonthTitle(d.Month) + " " + Num(d.Day, "00");

    private static string MonthTitle(int month)
    {
        var m = MonthAbbr[month - 1];
        return m[0] + m.Substring(1).ToLowerInvariant();
    }
}
