using System.Globalization;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S91 — `dotnet run -- calendar [year ...]`
//
//  ★ ITS OWN COMMAND, DELIBERATELY, and it returns before the validation suite
//  ever loads a config. The season page must stay byte-identical to its pre-S91
//  self (Phase 82 A10), and the cheapest way to guarantee that is for the
//  calendar to have no way to reach it. Same shape as `season`, `divvy` and
//  `world`.
//
//  ★ NO BASKETBALL IS PLAYED HERE and no world is loaded. This prints days.
// ============================================================================

internal static partial class Program
{
    /// <summary>The five years §4.8 asks for by default: a recent year, a leap year,
    /// 1900 (a century that is NOT leap), 2000 (a century that IS), and 1850 — the year
    /// R1 names as the thing a completely custom world must get right with no shipped
    /// data.</summary>
    private static readonly int[] DefaultPrintedYears = { 2027, 2028, 1900, 2000, 1850 };

    private static void RunCalendar(string[] args)
    {
        var years = new List<int>();
        for (var i = 1; i < args.Length; i++)
        {
            if (!int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                || y < CharmCalendar.MinYear || y > CharmCalendar.MaxYear)
            {
                Console.WriteLine("usage: dotnet run -- calendar [year ...]   "
                                  + "(years " + CharmCalendar.MinYear.ToString(CultureInfo.InvariantCulture)
                                  + ".." + CharmCalendar.MaxYear.ToString(CultureInfo.InvariantCulture) + ")");
                Console.WriteLine("  bad year: " + args[i]);
                return;
            }
            years.Add(y);
        }

        if (years.Count == 0) years.AddRange(DefaultPrintedYears);

        foreach (var y in years)
        {
            // Written, not WriteLine'd: the report carries its own literal "\n" newlines
            // so that it is byte-identical on every platform (A12). WriteLine would append
            // Environment.NewLine and undo that at the very last step.
            Console.Out.Write(CalendarYearReport.Render(y));
            Console.Out.Write("\n");
        }
        Console.Out.Flush();
    }
}
