using System.Globalization;
using System.Security.Cryptography;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S89 — WHERE THE GAME MEETS ITS SAVE FILE.
//
//  Two small jobs live here and nothing else: turning a world into the
//  fingerprint that binds a history to it, and reading the `--history` argument
//  off a command line.
//
//  ★ THE HISTORY IS NAMED, NEVER DEFAULTED. There is deliberately no fallback
//  path. A career is a thing Emmett names and knows the location of; a hidden
//  file that appears next to the binary the first time a season is run is how
//  somebody ends up with three careers they cannot tell apart, and how a
//  throwaway test run permanently burns four thousand person numbers out of a
//  real career. No argument means no history, no file, no allocator, nothing
//  touched — which is exactly how every session before this one behaved.
//
//  ★ WHY THE FINGERPRINT IS THE WHOLE WORLD, not a chosen subset. A history is
//  only meaningful against the league it was built from, and guessing which
//  fields "can affect generation" is precisely the kind of omission that bites
//  three seasons later when somebody edits a prestige value and the careers
//  quietly continue against a different league. The world is small and does not
//  change, so hashing all of it costs nothing and cannot be wrong. A future
//  format change defines sha256-v2 rather than redefining what v1 meant.
// ============================================================================

internal static partial class Program
{
    private const string HistoryArgFlag = "--history";

    /// <summary>★ S119 — the year a career (or a single season with no career) starts in.</summary>
    private const string YearArgFlag = "--year";

    /// <summary>The world's canonical fingerprint, self-describing so a later scheme can
    /// be told apart from this one at a glance rather than by length.</summary>
    private static string WorldFingerprint(WorldFile world)
    {
        var hash = SHA256.HashData(CanonicalWorldBytes(world));
        return "sha256-v1:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Pull `--history &lt;path&gt;` out of a command line, or null for legacy mode.
    /// Scanned as a NAMED pair rather than a position, because `season` already spends its
    /// fourth slot on the minutes floor.</summary>
    private static string? ParseHistoryArg(string[] args, int firstOptional)
    {
        for (var i = firstOptional; i < args.Length; i++)
        {
            if (!string.Equals(args[i], HistoryArgFlag, StringComparison.Ordinal)) continue;
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new HistoryException(HistoryError.PathIsDirectory,
                    $"{HistoryArgFlag} needs a file path after it. There is no default history path — " +
                    "a career is named explicitly.");
            return args[i + 1];
        }
        return null;
    }

    /// <summary>True for an argument that belongs to a named flag (`--history` or `--year`),
    /// so a positional scan (the minutes floor) skips over it instead of trying to parse it.</summary>
    private static bool IsHistoryArgAt(string[] args, int index)
    {
        foreach (var flag in new[] { HistoryArgFlag, YearArgFlag })
        {
            if (string.Equals(args[index], flag, StringComparison.Ordinal)) return true;
            if (index > 0 && string.Equals(args[index - 1], flag, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>★ S119 — pull `--year &lt;YYYY&gt;` off a command line, or null when it is left
    /// off. Named, like `--history`, because `season` spends its fourth slot on the minutes
    /// floor. A year outside the seasons the calendar holds (1..9998) or one that is not a whole
    /// number is refused HERE, before any world is loaded or any file is touched.
    /// <para>`--year` always names a career's FIRST season — never the season about to be played.
    /// On an existing career it must agree with the file (see <see cref="OpenHistoryFor"/>).</para></summary>
    private static int? ParseYearArg(string[] args, int firstOptional)
    {
        for (var i = firstOptional; i < args.Length; i++)
        {
            if (!string.Equals(args[i], YearArgFlag, StringComparison.Ordinal)) continue;
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new HistoryException(HistoryError.WrongType,
                    $"{YearArgFlag} needs a year after it — the year the first season opens in, e.g. {YearArgFlag} 1950.");
            var raw = args[i + 1];
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                throw new HistoryException(HistoryError.WrongType,
                    $"{YearArgFlag} '{raw}' is not a year — give the year the first season opens in, e.g. 1950.");
            if (year < SeasonMinStartYear || year > SeasonMaxStartYear)
                throw new HistoryException(HistoryError.YearOutOfDomain,
                    $"{YearArgFlag} {raw}: a season can open in {SeasonMinStartYear}.." +
                    $"{SeasonMaxStartYear} (it ends in the following year, and the calendar " +
                    "stops at 9999).");
            return year;
        }
        return null;
    }

    /// <summary>Open the history for a run, bound to this world. Returns null in legacy
    /// mode — no file is read, no folder is touched, no allocator exists.
    /// <para>★ S119 — a NEW career is created starting in <paramref name="startYear"/> (2026 when
    /// none is given). An EXISTING career keeps the year it was created with: naming that same
    /// year is accepted and changes nothing; naming any other is refused by name, and the file
    /// is left exactly as it was (opening an existing career writes nothing).</para></summary>
    private static HistoryStore? OpenHistoryFor(WorldFile world, string? historyPath, int? startYear = null)
    {
        if (historyPath is null) return null;
        var store = HistoryStore.Open(historyPath, WorldFingerprint(world), startYear ?? SeasonDefaultStartYear);
        if (startYear is { } named && named != store.StartYear)
        {
            var started = store.StartYear;
            store.Dispose();
            throw new HistoryException(HistoryError.StartYearMismatch,
                $"this career started in {started.ToString(CultureInfo.InvariantCulture)}; {YearArgFlag} names a " +
                $"career's first season, not the one about to be played. Leave {YearArgFlag} off to play its next season.");
        }
        return store;
    }
}
