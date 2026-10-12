using System.Globalization;
using System.Text;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S122 — THE DAY PAGE (Emmett, C-67).
//
//  `day <world.json> <YYYY-MM-DD> --history <career.json>` prints one day of a
//  career: the weekday and the date, then every game played that day, one line
//  each, in the order it was played —
//
//      Monday, November 18, 2024
//      Texas 71 @ LSU 68
//      Duke 77 vs. Arizona 70   (neutral)
//      ...
//      N games
//
//  The away side first, "@" when somebody hosted, "vs." on a floor with no host.
//  An overtime game adds " (OT)" / " (2OT)" as the game log does. A day with no
//  games says so.
//
//  ★ IT READS THE LOG, NOTHING ELSE. A career page, like `player`, `gamelog` and
//  `box`: a legacy season keeps no games, so --history is required. The season is
//  the one whose basketball year (July 1 to June 30) holds the date.
//
//  ★ "IN THE ORDER IT WAS PLAYED" IS ORDINAL ORDER. The log holds no play order
//  (S122 K2: blocks are appended in fixture order). Within one day the season
//  plays in fixture-ordinal order — Phase 113 C1 proves it — so the day's blocks
//  sorted by ordinal ARE the day as played.
// ============================================================================

internal static partial class Program
{
    /// <summary>One game on the day page.</summary>
    private sealed record DayLine(
        int FixtureOrdinal, string Away, int AwayScore, string Home, int HomeScore,
        bool HasHost, int OvertimePeriods);

    /// <summary>One day of a career, read off its log.</summary>
    private sealed record DayPage(DateOnly Date, long Season, IReadOnlyList<DayLine> Games);

    /// <summary>The season whose basketball year (July 1 to June 30) holds this date: a date in
    /// July–December belongs to the season opening that year, January–June to the one that opened
    /// the year before. Season N opens in StartYear + (N − 1).</summary>
    private static long DaySeasonOf(DateOnly date, int startYear)
    {
        var opens = date.Month >= 7 ? date.Year : date.Year - 1;
        return (long)opens - startYear + 1;
    }

    private static string DayHeader(DateOnly date)
        => date.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>Read one day. Refused by name: a date before the career's first season, a date past
    /// its last played season, a season with no record.</summary>
    private static DayPage ReadDayPage(
        List<(long SeasonId, GameLogV1? Log, string? Reason)> logs, WorldFile world, int startYear, DateOnly date)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        var season = DaySeasonOf(date, startYear);
        if (season < 1)
            throw new CareerRefusedException(Inv(
                $"{date:yyyy-MM-dd} is before this career: its first season opens July 1, {startYear}."));
        if (logs.Count == 0)
            throw new CareerRefusedException(Inv(
                $"this career has played no season yet, so it has no {date:yyyy-MM-dd}."));
        if (season > logs[^1].SeasonId)
        {
            var last = logs[^1].SeasonId;
            throw new CareerRefusedException(Inv(
                $"{date:yyyy-MM-dd} is past this career: it has played seasons 1..{last}, through June 30, {startYear + last}."));
        }
        var entry = logs.First(l => l.SeasonId == season);
        if (entry.Log is null)
            throw new CareerRefusedException(Inv($"season {season} has no record — {entry.Reason}."));

        var names = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        string Name(int id) => names.TryGetValue(id, out var n) ? n : Inv($"school {id}");
        var games = entry.Log.Blocks
            .Where(b => b.Facts.Date == date)
            .OrderBy(b => b.Facts.FixtureOrdinal)
            .Select(b => new DayLine(
                b.Facts.FixtureOrdinal,
                Name(b.Facts.AwaySchoolId), b.Facts.AwayScore,
                Name(b.Facts.HomeSchoolId), b.Facts.HomeScore,
                b.Facts.HasHost ?? throw new CareerRefusedException(Inv(
                    $"season {season} game {b.Facts.FixtureOrdinal} records no site.")),
                b.Facts.OvertimePeriods))
            .ToList();
        return new DayPage(date, season, games);
    }

    /// <summary>One game's line: away first, "@" when hosted, "vs." and "(neutral)" when not.</summary>
    private static string DayLineText(DayLine g)
    {
        var ot = g.OvertimePeriods switch
        {
            0 => "",
            1 => " (OT)",
            var n => $" ({n.ToString(CultureInfo.InvariantCulture)}OT)",
        };
        var away = $"{g.Away} {g.AwayScore.ToString(CultureInfo.InvariantCulture)}";
        var home = $"{g.Home} {g.HomeScore.ToString(CultureInfo.InvariantCulture)}";
        return g.HasHost ? $"{away} @ {home}{ot}" : $"{away} vs. {home}{ot}   (neutral)";
    }

    private static string RenderDayPage(DayPage page)
    {
        if (page.Games.Count == 0)
            return $"No games on {DayHeader(page.Date)}.\n";
        var sb = new StringBuilder();
        sb.Append(DayHeader(page.Date)).Append('\n');
        foreach (var g in page.Games) sb.Append(DayLineText(g)).Append('\n');
        sb.Append(page.Games.Count.ToString("N0", CultureInfo.InvariantCulture))
          .Append(page.Games.Count == 1 ? " game\n" : " games\n");
        return sb.ToString();
    }

    /// <summary>`day &lt;world.json&gt; &lt;YYYY-MM-DD&gt; --history &lt;career.json&gt;`.</summary>
    private static int RunDay(string[] args)
    {
        string? historyPath;
        try { historyPath = ParseHistoryArg(args, 1); }
        catch (HistoryException hx) { Console.WriteLine($"DAY ERROR: {hx.Message}"); return 1; }
        var positional = Enumerable.Range(1, args.Length - 1).Where(i => !IsHistoryArgAt(args, i)).Select(i => args[i]).ToList();
        if (positional.Count != 2)
        {
            Console.WriteLine("usage: day <world.json> <YYYY-MM-DD> --history <career.json>");
            Console.WriteLine("  Every game a career played on one day, in the order it was played.");
            return 1;
        }
        if (historyPath is null)
        {
            Console.WriteLine("DAY ERROR: a day reads a career — --history <career.json> is required. " +
                              "A legacy season keeps no games.");
            return 1;
        }
        if (!DateOnly.TryParseExact(positional[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var date))
        {
            Console.WriteLine($"DAY ERROR: '{positional[1]}' is not a date (YYYY-MM-DD, e.g. 2026-11-18).");
            return 1;
        }
        var world = LoadWorldFor("DAY", positional[0]);
        if (world is null) return 1;
        using var history = OpenCareerForReading("DAY", world, historyPath);
        if (history is null) return 1;
        try
        {
            Console.Write(RenderDayPage(ReadDayPage(ReadCareerLogs(history), world, history.StartYear, date)));
            return 0;
        }
        catch (CareerRefusedException rx)
        {
            Console.WriteLine($"DAY ERROR: {rx.Message}");
            return 1;
        }
    }
}
