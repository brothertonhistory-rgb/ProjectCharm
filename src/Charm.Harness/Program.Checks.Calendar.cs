using System.Globalization;
using System.Reflection;
using System.Text;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S91 — PHASE 82: THE CALENDAR.
//
//  ★ PAGE-ONLY PRINCIPLE HOLDS: no basketball target and no count of anything
//  the simulation produces is asserted anywhere in this phase. Day counts are
//  REPORTED. Whether ~30 games and a conference bracket FIT in the window is
//  S92's question, asked when it knows its own constraints; asserting a "plausible
//  rhythm" here would be a scheduler in disguise.
//
//  ★ THE EXPECTED WEEKDAYS ARE SOURCED INDEPENDENTLY OF .NET. Every hardcoded
//  date below was computed with Python's `datetime` — a separate implementation
//  of the proleptic Gregorian calendar — and pasted in as literals. A table
//  computed by the same library the production code calls would only prove the
//  library agrees with itself.
//
//  ★ THE ONE THING THIS PHASE MOST NEEDS TO CATCH is a calendar that is right
//  for 2020-2030 and wrong before 1900. That passes every test written casually,
//  so the century boundaries are the discriminating cases and they are all here:
//  1900 (century, NOT leap), 2000 (century, leap), 2100, 2200, 2300, 2400.
//
//  ★ THE SECOND THING is r3's error returning: D1's bracket mistaken for the
//  definition of when basketball is legal. The NIT plays the midweek nights; D2,
//  D3 and JUCO run their own brackets in the same window. So A5 walks EVERY day
//  from November 1 to the championship and requires zero holes, and A5b builds
//  the gated implementation and requires the walk to reject it.
// ============================================================================

internal static partial class Program
{
    private static bool Phase82CalendarCheck()
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 82 — the calendar ==");

        var ok = true;
        void Check(string label, bool pass, string? why = null)
        {
            Console.WriteLine($"    {(pass ? "ok  " : "FAIL")} {label}"
                              + (why is null ? "" : $"  ({why})"));
            ok &= pass;
        }
        void Report(string label, string value)
            => Console.WriteLine($"    --   {label}: {value}");

        // ── A1 — WEEKDAY CORRECTNESS ACROSS CENTURIES ────────────────────────
        //  Independently sourced. See the file header.
        var weekdayTable = new (DateOnly Date, DayOfWeek Expected)[]
        {
            (new DateOnly(1, 1, 1), DayOfWeek.Monday),
            (new DateOnly(1, 12, 31), DayOfWeek.Monday),
            (new DateOnly(1850, 1, 1), DayOfWeek.Tuesday),
            (new DateOnly(1850, 2, 28), DayOfWeek.Thursday),
            (new DateOnly(1850, 3, 1), DayOfWeek.Friday),
            (new DateOnly(1850, 11, 1), DayOfWeek.Friday),
            (new DateOnly(1850, 12, 31), DayOfWeek.Tuesday),
            (new DateOnly(1899, 12, 31), DayOfWeek.Sunday),
            (new DateOnly(1900, 1, 1), DayOfWeek.Monday),
            (new DateOnly(1900, 2, 28), DayOfWeek.Wednesday),
            (new DateOnly(1900, 3, 1), DayOfWeek.Thursday),
            (new DateOnly(1900, 12, 31), DayOfWeek.Monday),
            (new DateOnly(1901, 1, 1), DayOfWeek.Tuesday),
            (new DateOnly(1904, 2, 29), DayOfWeek.Monday),
            (new DateOnly(1970, 1, 1), DayOfWeek.Thursday),
            (new DateOnly(1999, 12, 31), DayOfWeek.Friday),
            (new DateOnly(2000, 1, 1), DayOfWeek.Saturday),
            (new DateOnly(2000, 2, 28), DayOfWeek.Monday),
            (new DateOnly(2000, 2, 29), DayOfWeek.Tuesday),
            (new DateOnly(2000, 3, 1), DayOfWeek.Wednesday),
            (new DateOnly(2000, 12, 31), DayOfWeek.Sunday),
            (new DateOnly(2001, 1, 1), DayOfWeek.Monday),
            (new DateOnly(2026, 8, 1), DayOfWeek.Saturday),
            (new DateOnly(2027, 3, 21), DayOfWeek.Sunday),
            (new DateOnly(2027, 4, 12), DayOfWeek.Monday),
            (new DateOnly(2099, 12, 31), DayOfWeek.Thursday),
            (new DateOnly(2100, 1, 1), DayOfWeek.Friday),
            (new DateOnly(2100, 2, 28), DayOfWeek.Sunday),
            (new DateOnly(2100, 3, 1), DayOfWeek.Monday),
            (new DateOnly(2199, 12, 31), DayOfWeek.Tuesday),
            (new DateOnly(2200, 3, 1), DayOfWeek.Saturday),
            (new DateOnly(2299, 12, 31), DayOfWeek.Sunday),
            (new DateOnly(2300, 3, 1), DayOfWeek.Thursday),
            (new DateOnly(2399, 12, 31), DayOfWeek.Friday),
            (new DateOnly(2400, 2, 29), DayOfWeek.Tuesday),
            (new DateOnly(2400, 3, 1), DayOfWeek.Wednesday),
            (new DateOnly(2400, 12, 31), DayOfWeek.Sunday),
            (new DateOnly(9999, 12, 31), DayOfWeek.Friday),
        };

        var wdBad = 0;
        DateOnly wdFirstBad = default;
        foreach (var (date, expected) in weekdayTable)
            if (CharmCalendar.WeekdayOf(date) != expected)
            {
                if (wdBad == 0) wdFirstBad = date;
                wdBad++;
            }
        Check("A1 every independently-sourced weekday from 0001 to 9999 is right",
              wdBad == 0,
              wdBad == 0 ? $"{weekdayTable.Length} dates, 6 century boundaries"
                         : $"first miss {CalIso(wdFirstBad)}");

        // NEGATIVE CONTROL: a deliberately NAIVE divisible-by-4 calendar, test-only and
        // never production. It is right for every year anyone tests casually and wrong
        // three times in four centuries; the table above must reject it. This does not
        // model a bug the production code can HAVE — the civil arithmetic is the
        // platform's — it proves the TABLE is sharp enough to catch a wrong calendar.
        var naiveBad = 0;
        var naiveBadAfter1900 = 0;
        foreach (var (date, expected) in weekdayTable)
            if (NaiveWeekday(date) != expected)
            {
                naiveBad++;
                if (date.Year > 1900) naiveBadAfter1900++;
            }
        Check("A1 negative control: a divisible-by-4 calendar FAILS the same table",
              naiveBad > 0 && naiveBadAfter1900 > 0,
              $"{naiveBad} of {weekdayTable.Length} wrong, {naiveBadAfter1900} of them after 1900");

        // ── A2 — THE LEAP RULE, EXACTLY ──────────────────────────────────────
        Check("A2 1900 is NOT a leap year", !CharmCalendar.IsLeapYear(1900));
        Check("A2 2000 IS a leap year", CharmCalendar.IsLeapYear(2000));
        Check("A2 1850 is NOT a leap year", !CharmCalendar.IsLeapYear(1850));
        Check("A2 2400 IS a leap year", CharmCalendar.IsLeapYear(2400));
        Check("A2 2100, 2200, 2300 are NOT leap years",
              !CharmCalendar.IsLeapYear(2100) && !CharmCalendar.IsLeapYear(2200)
              && !CharmCalendar.IsLeapYear(2300));
        Check("A2 February is 28 or 29 to match, at every century case",
              CharmCalendar.DaysInMonth(1900, 2) == 28 && CharmCalendar.DaysInMonth(2000, 2) == 29
              && CharmCalendar.DaysInMonth(1850, 2) == 28 && CharmCalendar.DaysInMonth(2400, 2) == 29
              && CharmCalendar.DaysInMonth(2100, 2) == 28);

        var yearLenBad = 0;
        var leapCount = 0;
        for (var y = CharmCalendar.MinYear; y <= CharmCalendar.MaxYear; y++)
        {
            var len = CharmCalendar.DaysInYear(y);
            if (len != 365 && len != 366) yearLenBad++;
            var summed = 0;
            for (var m = 1; m <= 12; m++) summed += CharmCalendar.DaysInMonth(y, m);
            if (summed != len) yearLenBad++;
            if (CharmCalendar.IsLeapYear(y)) leapCount++;
        }
        Check("A2 every year 0001-9999 is 365 or 366 days and its months add up",
              yearLenBad == 0, $"{CharmCalendar.MaxYear} years walked");
        Report("A2 leap years in 0001-9999", leapCount.ToString(CultureInfo.InvariantCulture));

        // ── A3 — SELECTION SUNDAY ────────────────────────────────────────────
        //  Independently sourced, same as A1. (marchYear, Selection Sunday, championship)
        var spineTable = new (int MarchYear, DateOnly Selection, DateOnly Champ)[]
        {
            (1850, new DateOnly(1850, 3, 17), new DateOnly(1850, 4, 8)),
            (1899, new DateOnly(1899, 3, 19), new DateOnly(1899, 4, 10)),
            (1900, new DateOnly(1900, 3, 18), new DateOnly(1900, 4, 9)),
            (1901, new DateOnly(1901, 3, 17), new DateOnly(1901, 4, 8)),
            (1904, new DateOnly(1904, 3, 20), new DateOnly(1904, 4, 11)),
            (1970, new DateOnly(1970, 3, 15), new DateOnly(1970, 4, 6)),
            (1999, new DateOnly(1999, 3, 21), new DateOnly(1999, 4, 12)),
            (2000, new DateOnly(2000, 3, 19), new DateOnly(2000, 4, 10)),
            (2024, new DateOnly(2024, 3, 17), new DateOnly(2024, 4, 8)),
            (2025, new DateOnly(2025, 3, 16), new DateOnly(2025, 4, 7)),
            (2026, new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 6)),
            (2027, new DateOnly(2027, 3, 21), new DateOnly(2027, 4, 12)),
            (2028, new DateOnly(2028, 3, 19), new DateOnly(2028, 4, 10)),
            (2099, new DateOnly(2099, 3, 15), new DateOnly(2099, 4, 6)),
            (2100, new DateOnly(2100, 3, 21), new DateOnly(2100, 4, 12)),
            (2200, new DateOnly(2200, 3, 16), new DateOnly(2200, 4, 7)),
            (2300, new DateOnly(2300, 3, 18), new DateOnly(2300, 4, 9)),
            (2399, new DateOnly(2399, 3, 21), new DateOnly(2399, 4, 12)),
            (2400, new DateOnly(2400, 3, 19), new DateOnly(2400, 4, 10)),
            (9998, new DateOnly(9998, 3, 15), new DateOnly(9998, 4, 6)),
        };

        var selBad = 0;
        foreach (var (marchYear, selection, champ) in spineTable)
        {
            var cal = new BasketballSeasonCalendar(marchYear - 1);
            if (cal.SelectionSunday != selection) selBad++;
            if (cal.ChampionshipDay != champ) selBad++;
        }
        Check("A3 Selection Sunday and championship match an independently-sourced table",
              selBad == 0, $"{spineTable.Length} seasons, 1849-1850 through 9997-9998");

        var thirdBad = 0;
        for (var y = 2; y <= CharmCalendar.MaxYear; y++)
        {
            var s = CharmCalendar.ThirdSundayInMarch(y);
            if (s.DayOfWeek != DayOfWeek.Sunday) { thirdBad++; continue; }
            if (s.Month != 3) { thirdBad++; continue; }
            // Exactly two Sundays precede it in March.
            var precedingSundays = 0;
            for (var d = 1; d < s.Day; d++)
                if (new DateOnly(y, 3, d).DayOfWeek == DayOfWeek.Sunday) precedingSundays++;
            if (precedingSundays != 2) thirdBad++;
        }
        Check("A3 it is a Sunday in March with exactly two Sundays before it, every year",
              thirdBad == 0, $"{CharmCalendar.MaxYear - 1} years walked");

        // ── A4 — THE D1 SPINE, AS REFERENCE DATA ─────────────────────────────
        //  The COMPLETE weekend sequence, not just the championship: a correct final date
        //  paired with a wrong weekend table must not pass.
        var wantWeekday = new (int Offset, DayOfWeek Day)[]
        {
            (4, DayOfWeek.Thursday), (5, DayOfWeek.Friday),
            (6, DayOfWeek.Saturday), (7, DayOfWeek.Sunday),
            (11, DayOfWeek.Thursday), (12, DayOfWeek.Friday),
            (13, DayOfWeek.Saturday), (14, DayOfWeek.Sunday),
            (20, DayOfWeek.Saturday), (22, DayOfWeek.Monday),
        };

        var spineBad = 0;
        var selRangeBad = 0;
        var champRangeBad = 0;
        for (var start = CharmCalendar.MinSeasonStartYear; start <= CharmCalendar.MaxSeasonStartYear; start++)
        {
            var cal = new BasketballSeasonCalendar(start);
            var dates = cal.D1TournamentDates;
            if (dates.Count != wantWeekday.Length) { spineBad++; continue; }
            for (var i = 0; i < wantWeekday.Length; i++)
            {
                var expectedDate = cal.SelectionSunday.AddDays(wantWeekday[i].Offset);
                if (dates[i] != expectedDate || dates[i].DayOfWeek != wantWeekday[i].Day) spineBad++;
            }
            var s = cal.SelectionSunday;
            if (s.Month != 3 || s.Day < 15 || s.Day > 21) selRangeBad++;
            var c = cal.ChampionshipDay;
            if (c.Month != 4 || c.Day < 6 || c.Day > 12 || c.Year != start + 1) champRangeBad++;
        }
        Check("A4 all ten D1 dates land on the ruled weekday, every supported season",
              spineBad == 0, $"{CharmCalendar.MaxSeasonStartYear} seasons x 10 dates");
        Check("A4 Selection Sunday is always March 15-21", selRangeBad == 0);
        Check("A4 the championship is always April 6-12, in the season's END year",
              champRangeBad == 0);

        // ── A5 — LEGALITY IS CONTINUOUS ──────────────────────────────────────
        //  These are the checks r3 got backwards.
        var s2026 = new BasketballSeasonCalendar(2026);
        Check("A5 Oct 31 is NOT a legal game day",
              !CharmCalendar.IsLegalGameDate(new DateOnly(2026, 10, 31)));
        Check("A5 Nov 1 IS a legal game day",
              CharmCalendar.IsLegalGameDate(new DateOnly(2026, 11, 1)));
        // Nov 1 is a FLOOR, not a start line. Proven structurally: the calendar's whole
        // public surface is dates, counts, flags and names. It cannot require a game on any
        // day because it has no way to say the word "game" -- it owns no schedule, no team
        // and no fixture. This is also the §2 wall: a date is not "conference", a GAME is.
        var allowedReturns = new[]
        {
            typeof(DateOnly), typeof(int), typeof(bool), typeof(string), typeof(void),
            typeof(IReadOnlyList<DateOnly>), typeof(IReadOnlyList<int>),
            typeof(SeasonLookupResult), typeof(SeasonMembership), typeof(DayOfWeek),
        };
        var foreignReturns = new List<string>();
        foreach (var t in new[] { typeof(BasketballSeasonCalendar), typeof(CharmCalendar) })
        {
            foreach (var pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance
                                               | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (!allowedReturns.Contains(pr.PropertyType))
                    foreignReturns.Add(t.Name + "." + pr.Name);
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                           | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (!m.IsSpecialName && !allowedReturns.Contains(m.ReturnType))
                    foreignReturns.Add(t.Name + "." + m.Name);
        }
        Check("A5 Nov 1 is a floor, not a fixture: the calendar's whole surface is dates, "
              + "counts and flags -- it owns no game, team or schedule and cannot require one",
              foreignReturns.Count == 0,
              foreignReturns.Count == 0 ? "surface clean"
                                        : string.Join(" | ", foreignReturns));

        Check("A5 SELECTION SUNDAY IS A LEGAL PLAYING DAY "
              + "(the one check that separates r4 from r2 and r3)",
              CharmCalendar.IsLegalGameDate(s2026.SelectionSunday));

        var offsetIllegal = new List<int>();
        for (var off = 1; off <= 22; off++)
            if (!CharmCalendar.IsLegalGameDate(s2026.SelectionSunday.AddDays(off)))
                offsetIllegal.Add(off);
        Check("A5 every offset +1..+22 after Selection Sunday is legal, asserted individually "
              + "-- the NIT, D2, D3 and JUCO play the days r3 called illegal",
              offsetIllegal.Count == 0,
              offsetIllegal.Count == 0 ? "+1..+22 all legal"
                                       : "illegal: " + string.Join(",", offsetIllegal));

        Check("A5 championship Monday is legal; the day after is not",
              CharmCalendar.IsLegalGameDate(s2026.ChampionshipDay)
              && !CharmCalendar.IsLegalGameDate(s2026.ChampionshipDay.AddDays(1)));

        // NO GAPS: walked day by day across several years. A single hole is exactly the
        // r3 defect returning.
        var holeYears = new[] { 1850, 1899, 1900, 1999, 2000, 2026, 2027, 2099, 2100, 2399, 2400 };
        var holes = 0;
        var walked = 0;
        DateOnly firstHole = default;
        foreach (var start in holeYears)
        {
            var cal = new BasketballSeasonCalendar(start);
            for (var d = cal.FirstLegalDay; d <= cal.ChampionshipDay; d = d.AddDays(1))
            {
                walked++;
                if (!CharmCalendar.IsLegalGameDate(d))
                {
                    if (holes == 0) firstHole = d;
                    holes++;
                }
            }
        }
        Check("A5 no gaps: every single date from Nov 1 to championship Monday is legal",
              holes == 0,
              holes == 0 ? $"{walked:N0} days walked across {holeYears.Length} seasons"
                         : $"first hole {CalIso(firstHole)}");

        // ── A5b — THE D1 SPINE PERMITS NOTHING ───────────────────────────────
        var plusEight = s2026.SelectionSunday.AddDays(8);
        Check("A5b +8 is NOT a D1 tournament date and IS a legal game day",
              !s2026.D1TournamentDates.Contains(plusEight)
              && CharmCalendar.IsLegalGameDate(plusEight),
              CalIso(plusEight));
        Check("A5b the ten dates are exposed, in order, and are all inside the legal span",
              s2026.D1TournamentDates.Count == 10
              && s2026.D1TournamentDates.SequenceEqual(s2026.D1TournamentDates.OrderBy(x => x))
              && s2026.D1TournamentDates.All(s2026.Contains));

        // NEGATIVE CONTROL: r3's implementation, rebuilt here. Continuous up to Selection
        // Sunday, then only the ten D1 dates. The no-gaps walk must reject it.
        var gatedHoles = 0;
        foreach (var start in holeYears)
        {
            var cal = new BasketballSeasonCalendar(start);
            for (var d = cal.FirstLegalDay; d <= cal.ChampionshipDay; d = d.AddDays(1))
                if (!GatedOnD1Dates(cal, d)) gatedHoles++;
        }
        Check("A5b negative control: an implementation that gates legality on the ten D1 "
              + "dates punches holes the walk rejects",
              gatedHoles > 0, $"{gatedHoles:N0} days it would have called illegal");

        // ── A6 — DAY COUNTS, REPORTED NOT JUDGED ─────────────────────────────
        //  ★ Nothing here is asserted. Whether 30 games and a 16-team bracket FIT is S92's
        //  question. r1 asserted the window "holds ~30 games at a plausible rhythm", which
        //  is not executable and is a scheduler in disguise.
        var minLegal = int.MaxValue; var maxLegal = 0;
        var minToSel = int.MaxValue; var maxToSel = 0;
        for (var start = CharmCalendar.MinSeasonStartYear; start <= CharmCalendar.MaxSeasonStartYear; start++)
        {
            var cal = new BasketballSeasonCalendar(start);
            minLegal = Math.Min(minLegal, cal.LegalDayCount);
            maxLegal = Math.Max(maxLegal, cal.LegalDayCount);
            minToSel = Math.Min(minToSel, cal.DaysToSelectionSunday);
            maxToSel = Math.Max(maxToSel, cal.DaysToSelectionSunday);
        }
        Report("A6 legal days Nov 1 -> championship (min/max over every supported season)",
               $"{minLegal} / {maxLegal}");
        Report("A6 legal days Nov 1 -> Selection Sunday (min/max)", $"{minToSel} / {maxToSel}");
        Report("A6 season 2026-2027", $"{s2026.LegalDayCount} legal days, "
               + $"{s2026.DaysToSelectionSunday} of them on or before Selection Sunday");

        // ── A7 — SEASON MEMBERSHIP ───────────────────────────────────────────
        var dec = CharmCalendar.SeasonOf(new DateOnly(2026, 12, 14));
        var feb = CharmCalendar.SeasonOf(new DateOnly(2027, 2, 14));
        Check("A7 a December date and the following February date share one season",
              dec.IsInSeason && feb.IsInSeason && dec.StartYear == feb.StartYear
              && dec.Label == "2026-2027", $"{dec.Label} / {feb.Label}");
        Check("A7 a June date belongs to no season, and that is a valid answer",
              CharmCalendar.SeasonOf(new DateOnly(2027, 6, 15)).Membership
              == SeasonMembership.Offseason);
        Check("A7 the boundary days themselves are in season",
              CharmCalendar.SeasonOf(s2026.FirstLegalDay).Label == "2026-2027"
              && CharmCalendar.SeasonOf(s2026.ChampionshipDay).Label == "2026-2027");
        Check("A7 the days just outside both boundaries are not",
              CharmCalendar.SeasonOf(s2026.FirstLegalDay.AddDays(-1)).Membership
                  == SeasonMembership.Offseason
              && CharmCalendar.SeasonOf(s2026.ChampionshipDay.AddDays(1)).Membership
                  == SeasonMembership.Offseason);

        var printed2027 = CalendarYearReport.Render(2027);
        Check("A7 ONE printed calendar year carries BOTH season labels "
              + "-- there is no single season name for a year",
              printed2027.Contains("2026-2027", StringComparison.Ordinal)
              && printed2027.Contains("2027-2028", StringComparison.Ordinal));
        Check("A7 a season is anchored by its START year: 2026 gives Nov 2026, "
              + "March 2027 and the label 2026-2027",
              s2026.FirstLegalDay.Year == 2026 && s2026.SelectionSunday.Year == 2027
              && s2026.Label == "2026-2027");

        // ── A8 — OVERLAPPING PERIODS ─────────────────────────────────────────
        //  ★ S91 registers NO periods in production. These are fixtures.
        var oneDay   = new CalendarPeriod("awards", new DateOnly(2027, 4, 5), new DateOnly(2027, 4, 5));
        var crossing = new CalendarPeriod("transfer window", new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 10));
        var recruitA = new CalendarPeriod("recruiting", new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 30));
        var recruitB = new CalendarPeriod("recruiting", new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 30));
        var carousel = new CalendarPeriod("coaching carousel", new DateOnly(2027, 4, 1), new DateOnly(2027, 5, 15));
        var timeline = new CalendarTimeline(new[] { carousel, recruitB, oneDay, crossing, recruitA });

        var apr5 = timeline.GetPeriods(new DateOnly(2027, 4, 5));
        Check("A8 four periods cover April 5 and all four come back",
              apr5.Count == 4, string.Join(" | ", apr5.Select(p => p.Name)));
        //  Start ascending first: the three April 1 periods precede the April 5 one-day
        //  period. Then End ascending: the two Apr 1 -> Apr 30 windows precede the one
        //  running to May 15. Then, with identical start, end AND name, only the
        //  REGISTRATION ordinal can break the tie — recruitB was handed in before recruitA.
        Check("A8 they come back in canonical order "
              + "(start, then end, then name ordinal, then registration)",
              ReferenceEquals(apr5[0], recruitB) && ReferenceEquals(apr5[1], recruitA)
              && ReferenceEquals(apr5[2], carousel) && ReferenceEquals(apr5[3], oneDay),
              "duplicate ranges AND duplicate names both present; "
              + "registration ordinal is what makes the order total");
        Check("A8 a period crossing New Year's is one period, covering both sides",
              crossing.CrossesNewYear
              && timeline.GetPeriods(new DateOnly(2026, 12, 31)).Contains(crossing)
              && timeline.GetPeriods(new DateOnly(2027, 1, 2)).Contains(crossing));
        Check("A8 a one-day period is legal and covers exactly its one day",
              oneDay.DayCount == 1 && oneDay.Covers(new DateOnly(2027, 4, 5))
              && !oneDay.Covers(new DateOnly(2027, 4, 4))
              && !oneDay.Covers(new DateOnly(2027, 4, 6)));
        Check("A8 periods are CLOSED: both endpoints are covered, and the days immediately "
              + "outside are not",
              crossing.Covers(crossing.Start) && crossing.Covers(crossing.End)
              && !crossing.Covers(crossing.Start.AddDays(-1))
              && !crossing.Covers(crossing.End.AddDays(1)));
        Check("A8 a day covered by nothing returns an empty list, never null",
              timeline.GetPeriods(new DateOnly(2027, 8, 1)) is { Count: 0 });

        // NEGATIVE CONTROL: a structure that can hold only ONE period per day. This is the
        // day-level "what phase is this" field the design forbids, and it silently loses
        // three of the four.
        var oneSlot = new Dictionary<DateOnly, CalendarPeriod>();
        foreach (var p in new[] { carousel, recruitB, oneDay, crossing, recruitA })
            for (var d = p.Start; d <= p.End; d = d.AddDays(1))
                oneSlot[d] = p;
        var oneSlotKeptOnApr5 = oneSlot.TryGetValue(new DateOnly(2027, 4, 5), out var kept) ? 1 : 0;
        Check("A8 negative control: a one-period-per-day structure keeps 1 where the "
              + "timeline keeps 4 -- a day cannot BE one phase",
              oneSlotKeptOnApr5 == 1 && apr5.Count == 4,
              "it kept only \"" + kept!.Name + "\"");

        // ── A9 — THE YEAR EDGES, ALL THREE OUTCOMES DISTINGUISHED ────────────
        var earlyEdge = CharmCalendar.SeasonOf(new DateOnly(1, 1, 15));
        var lateEdge  = CharmCalendar.SeasonOf(new DateOnly(9999, 11, 15));
        Check("A9 January of year 0001 is classified SeasonOutsideSupportedRange, never null "
              + "and never confused with the offseason",
              earlyEdge.Membership == SeasonMembership.SeasonOutsideSupportedRange
              && earlyEdge.Label.Length == 0);
        Check("A9 November of year 9999 is classified the same way",
              lateEdge.Membership == SeasonMembership.SeasonOutsideSupportedRange);
        Check("A9 all three outcomes are distinguishable from one another",
              earlyEdge.Membership != SeasonMembership.Offseason
              && CharmCalendar.SeasonOf(new DateOnly(2027, 6, 15)).Membership
                 != SeasonMembership.InSeason);
        Check("A9 LEGALITY still answers at both edges -- only the NAME is unrepresentable",
              CharmCalendar.IsLegalGameDate(new DateOnly(1, 1, 15))
              && CharmCalendar.IsLegalGameDate(new DateOnly(9999, 11, 15)));
        Check("A9 civil operations still work at 0001-01-01 and 9999-12-31",
              CharmCalendar.WeekdayOf(new DateOnly(1, 1, 1)) == DayOfWeek.Monday
              && CharmCalendar.WeekdayOf(new DateOnly(9999, 12, 31)) == DayOfWeek.Friday
              && CharmCalendar.DaysInYear(1) == 365 && CharmCalendar.DaysInYear(9999) == 365);
        Check("A9 a season cannot be built outside 0001..9998, and says so",
              CalendarThrows(() => new BasketballSeasonCalendar(0))
              && CalendarThrows(() => new BasketballSeasonCalendar(9999))
              && !CalendarThrows(() => new BasketballSeasonCalendar(9998)));

        // ── A10 — ISOLATION ──────────────────────────────────────────────────
        //  ★ The BYTE-IDENTICAL season page is proven by Emmett's run, not here: the suite
        //  has no pre-S91 page to diff against. What IS proven here, and is the thing that
        //  would break it, is that nothing on the season path can reach the calendar. If a
        //  future session wires it in, this goes red.
        if (!TryFindRepoRoot(out var root))
        {
            Check("A10/A11 source root located (walked up from the binary)", false,
                  "no directory containing src/Charm.Engine/Core/Slot.cs found above "
                  + AppContext.BaseDirectory);
        }
        else
        {
            Report("A10/A11 source root", root!);

            var seasonPath = new[]
            {
                "src/Charm.Harness/Program.Season.cs",
                "src/Charm.Harness/Program.Season.Stats.cs",
                "src/Charm.Harness/Program.Season.Calibration.cs",
                "src/Charm.Harness/Program.Season.Retention.cs",
                "src/Charm.Harness/Program.Checks.Season.cs",
            };
            var calendarTypes = new[]
            {
                "CharmCalendar", "BasketballSeasonCalendar",
                "CalendarYearReport", "CalendarTimeline", "CalendarPeriod",
            };
            var leaked = new List<string>();
            foreach (var rel in seasonPath)
            {
                var full = Path.Combine(root!, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) { leaked.Add(rel + " MISSING"); continue; }
                var text = File.ReadAllText(full);
                foreach (var t in calendarTypes)
                    if (text.Contains(t, StringComparison.Ordinal)) leaked.Add(rel + " -> " + t);
            }
            Check("A10 no file on the season path names any calendar type -- S91 cannot "
                  + "have moved the season page",
                  leaked.Count == 0,
                  leaked.Count == 0 ? $"{seasonPath.Length} files scanned"
                                    : string.Join(" | ", leaked));

            // ── A11 — PURITY: NO WALL CLOCK, DIRECT OR INDIRECT ──────────────
            //  ★ The needles are ASSEMBLED AT RUNTIME so that this file does not trip its
            //  own scan. The spelling the prompt named appears nowhere in the tree -- the one real
            //  wall-clock read is a different spelling, which is why the scan covers all
            //  five and not the one the prompt named.
            var dt = "DateTime";
            var dto = "DateTimeOffset";
            var clockNeedles = new[]
            {
                dt + ".Now", dt + ".UtcNow", dt + ".Today",
                dto + ".Now", dto + ".UtcNow",
                "TimeZoneInfo" + ".Local", "TimeProvider" + ".System",
                "DateOnly" + ".FromDateTime",
            };
            var clockHits = new List<string>();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root!, "src"), "*.cs",
                                                          SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var n in clockNeedles)
                    if (text.Contains(n, StringComparison.Ordinal))
                        clockHits.Add(Path.GetFileName(file) + " -> " + n);
            }
            Check("A11 exactly ONE wall-clock read exists in the whole tree, and it is the "
                  + "pre-existing ad-hoc game seed",
                  clockHits.Count == 1 && clockHits[0].StartsWith("Program.Game.cs", StringComparison.Ordinal),
                  clockHits.Count == 0 ? "none found -- the scan may be broken"
                                       : string.Join(" | ", clockHits));

            // Culture defaults, over the production surface S91 adds. The check file itself
            // is deliberately outside this scope and says so: A12 installs a foreign culture
            // on purpose.
            var cultureNeedles = new[]
            {
                "CultureInfo" + ".CurrentCulture", "CultureInfo" + ".CurrentUICulture",
                "CultureInfo" + ".InstalledUICulture", "CultureInfo" + ".DefaultThreadCurrentCulture",
            };
            var cultureScope = Directory
                .EnumerateFiles(Path.Combine(root!, "src", "Charm.Engine"), "*.cs", SearchOption.AllDirectories)
                .Append(Path.Combine(root!, "src", "Charm.Harness", "Program.Calendar.cs"))
                .ToList();
            var cultureHits = new List<string>();
            foreach (var file in cultureScope)
            {
                if (!File.Exists(file)) { cultureHits.Add(Path.GetFileName(file) + " MISSING"); continue; }
                var text = File.ReadAllText(file);
                foreach (var n in cultureNeedles)
                    if (text.Contains(n, StringComparison.Ordinal))
                        cultureHits.Add(Path.GetFileName(file) + " -> " + n);
            }
            Check("A11 the engine and the calendar command read no culture default "
                  + "(the suite's own check file is outside this scope by design -- A12 "
                  + "installs a foreign culture deliberately)",
                  cultureHits.Count == 0,
                  cultureHits.Count == 0 ? $"{cultureScope.Count} files scanned"
                                         : string.Join(" | ", cultureHits));
        }

        // A11 by reflection: there is no way to ask the calendar what today is.
        Check("A11 BasketballSeasonCalendar has no parameterless constructor -- there is no "
              + "'current season' to construct",
              typeof(BasketballSeasonCalendar).GetConstructor(Type.EmptyTypes) is null);
        var noArgEntries = new List<string>();
        foreach (var t in new[] { typeof(CharmCalendar), typeof(CalendarYearReport) })
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (m.GetParameters().Length == 0) noArgEntries.Add(t.Name + "." + m.Name);
        Check("A11 every calendar entry point demands an explicit year or date",
              noArgEntries.Count == 0,
              noArgEntries.Count == 0 ? "no parameterless entry point"
                                      : string.Join(" | ", noArgEntries));

        // ── A12 — RENDERER INVARIANCE ────────────────────────────────────────
        var baseline = CalendarYearReport.Render(2028);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        string foreign;
        bool cultureWasReallyDifferent;
        try
        {
            CultureInfo seed;
            try { seed = CultureInfo.GetCultureInfo("de-DE"); }
            catch (CultureNotFoundException) { seed = CultureInfo.InvariantCulture; }
            var german = (CultureInfo)seed.Clone();
            german.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
            german.DateTimeFormat.AbbreviatedDayNames =
                new[] { "SO", "MO", "DI", "MI", "DO", "FR", "SA" };
            german.DateTimeFormat.AbbreviatedMonthNames =
                new[] { "JAN", "FEB", "MÄR", "APR", "MAI", "JUN",
                        "JUL", "AUG", "SEP", "OKT", "NOV", "DEZ", "" };
            german.NumberFormat.NegativeSign = "!";
            CultureInfo.CurrentCulture = german;
            CultureInfo.CurrentUICulture = german;

            cultureWasReallyDifferent =
                CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek == DayOfWeek.Monday
                && CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[0] == "SO";

            foreign = CalendarYearReport.Render(2028);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
        Check("A12 the installed foreign culture really is foreign "
              + "(Monday-first, translated weekday names) -- or the next check is vacuous",
              cultureWasReallyDifferent);
        Check("A12 the printed year is byte-identical under a non-English, "
              + "non-Sunday-first culture",
              string.Equals(baseline, foreign, StringComparison.Ordinal),
              $"{baseline.Length:N0} chars");
        Check("A12 the report uses literal \\n and never a platform newline",
              !baseline.Contains("\r", StringComparison.Ordinal));

        // The renderer REFLECTS the underlying values rather than being the proof of them.
        var s2027 = new BasketballSeasonCalendar(2027);
        Check("A12 the printed year names the same Selection Sunday and championship the "
              + "spine holds",
              printed2027.Contains("Sun Mar 21 2027", StringComparison.Ordinal)
              && printed2027.Contains("Mon Apr 12 2027", StringComparison.Ordinal)
              && s2026.SelectionSunday == new DateOnly(2027, 3, 21)
              && s2026.ChampionshipDay == new DateOnly(2027, 4, 12));
        Check("A12 a leap February prints 29 days and marks the leap day",
              CalendarYearReport.Render(2028).Contains("Feb 29 -- leap day", StringComparison.Ordinal)
              && !CalendarYearReport.Render(1900).Contains("leap day", StringComparison.Ordinal));
        Report("A12 season 2027-2028 championship", s2027.ChampionshipDay.ToString("yyyy-MM-dd",
               CultureInfo.InvariantCulture));

        Console.WriteLine(ok ? "  Phase 82 PASS" : "  Phase 82 FAIL");
        return ok;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string CalIso(DateOnly d)
        => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool CalendarThrows(Action a)
    {
        try { a(); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }

    /// <summary>TEST-ONLY. A calendar that treats every year divisible by 4 as a leap year —
    /// right for every year anyone tests casually, wrong three times in four centuries.
    /// Counts days forward from 0001-01-01, which was a Monday, and never touches the
    /// platform's date arithmetic. Exists so the A1 table has something wrong to reject.</summary>
    private static DayOfWeek NaiveWeekday(DateOnly date)
    {
        long days = 0;
        for (var y = 1; y < date.Year; y++) days += (y % 4 == 0) ? 366 : 365;
        var monthLen = new[] { 31, (date.Year % 4 == 0) ? 29 : 28, 31, 30, 31, 30,
                               31, 31, 30, 31, 30, 31 };
        for (var m = 1; m < date.Month; m++) days += monthLen[m - 1];
        days += date.Day - 1;
        return (DayOfWeek)(((days + (long)DayOfWeek.Monday) % 7 + 7) % 7);
    }

    /// <summary>TEST-ONLY. r3's rejected implementation: legal up to Selection Sunday, then
    /// only on the ten D1 tournament dates. It locks out every other layer of the sport —
    /// the NIT, D2, D3, JUCO — and A5b requires the no-gaps walk to reject it.</summary>
    private static bool GatedOnD1Dates(BasketballSeasonCalendar cal, DateOnly date)
    {
        if (date < cal.FirstLegalDay) return false;
        if (date < cal.SelectionSunday) return true;
        return cal.D1TournamentDates.Contains(date);
    }

    /// <summary>Walks up from the running binary looking for the repo root. Used by the two
    /// source-scanning assertions. A hard failure rather than a silent skip: a check that
    /// quietly does nothing is decoration.</summary>
    private static bool TryFindRepoRoot(out string? root)
    {
        var probe = Path.Combine("src", "Charm.Engine", "Core", "Slot.cs");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, probe)))
            {
                root = dir.FullName;
                return true;
            }
        root = null;
        return false;
    }
}
