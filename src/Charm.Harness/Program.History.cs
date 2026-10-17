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

    /// <summary>★ S127 — the day to play a season through and save it, mid-way, to its career.</summary>
    private const string ThroughArgFlag = "--through";

    /// <summary>★ S127 — pull `--through &lt;yyyy-MM-dd&gt;` off the `season` command line, or null. Only the
    /// `season` command reads it (the stacked command saves nothing).</summary>
    private static DateOnly? ParseThroughArg(string[] args, int firstOptional)
    {
        for (var i = firstOptional; i < args.Length; i++)
        {
            if (!string.Equals(args[i], ThroughArgFlag, StringComparison.Ordinal)) continue;
            if (i + 1 >= args.Length
                || !DateOnly.TryParseExact(args[i + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw new HistoryException(HistoryError.WrongType,
                    $"{ThroughArgFlag} needs a day after it, as yyyy-MM-dd — the last day to play before saving, e.g. {ThroughArgFlag} 2027-02-10.");
            return day;
        }
        return null;
    }

    private static bool IsThroughArgAt(string[] args, int index)
        => string.Equals(args[index], ThroughArgFlag, StringComparison.Ordinal)
           || (index > 0 && string.Equals(args[index - 1], ThroughArgFlag, StringComparison.Ordinal));

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
        var store = OpenCareer(historyPath, world, startYear ?? SeasonDefaultStartYear);
        foreach (var line in store.OpenReport) Console.WriteLine("  " + line);
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
    // ── ★ S125 (B1b) — every career is opened with its world's league state ───────────────
    //
    //  The store cannot see a world (Charm.History references nothing), so a career is born
    //  with `league-0` only if the harness hands the world's prestige in. Every career opener —
    //  the `season` command, the five pages and every suite check — goes through OpenCareer,
    //  so every career has one (S125 check-in, call 3). The suite's store-level checks that
    //  hand-build careers call HistoryStore.Open directly and say what league they mean.

    /// <summary>Prestige by school, copied from the world, in the shape the league state holds.</summary>
    private static List<SchoolPrestige> LeaguePrestigeOf(WorldFile world)
        => world.Schools.Select(s => new SchoolPrestige(s.Id, s.CurrentPrestige)).ToList();

    /// <summary>★ S125 — open (or create) a career bound to <paramref name="world"/>, born with the
    /// world's league state, and assert that its latest league state equals the world's prestige.
    /// A refusal disposes the store, so the lock is released.</summary>
    private static HistoryStore OpenCareer(string historyPath, WorldFile world,
                                           int startYearIfNew = SeasonDefaultStartYear)
    {
        var store = HistoryStore.Open(historyPath, WorldFingerprint(world), LeaguePrestigeOf(world), startYearIfNew);
        try { AssertLeagueMatchesWorld(store, world, "opened"); }
        catch { store.Dispose(); throw; }
        return store;
    }

    /// <summary>★ S125 (K5) — THE B1 EQUALITY ASSERTION. Until phase C the world is the authority for
    /// prestige and the league state is its copy; at every open and every close the two must agree
    /// for every school, or the career is refused by name. Retired in phase C, when the league state
    /// becomes the authority.</summary>
    private static void AssertLeagueMatchesWorld(HistoryStore store, WorldFile world, string when)
    {
        var league = store.ReadLatestLeagueState();
        var inLeague = league.Prestige.ToDictionary(p => p.SchoolId, p => p.Value);
        var inWorld = LeaguePrestigeOf(world).ToDictionary(p => p.SchoolId, p => p.Value);
        var where = $"league-{league.Season.ToString(CultureInfo.InvariantCulture)}";
        foreach (var (id, value) in inWorld.OrderBy(kv => kv.Key))
        {
            if (!inLeague.TryGetValue(id, out var held))
                throw new HistoryException(HistoryError.LeagueStateMismatch,
                    $"the career's league state disagrees with the world as it is {when}: school {id.ToString(CultureInfo.InvariantCulture)} " +
                    $"is in the world but not in {where}.");
            if (held != value)
                throw new HistoryException(HistoryError.LeagueStateMismatch,
                    $"the career's league state disagrees with the world as it is {when}: school {id.ToString(CultureInfo.InvariantCulture)}'s " +
                    $"prestige is {held.ToString(CultureInfo.InvariantCulture)} in {where} and " +
                    $"{value.ToString(CultureInfo.InvariantCulture)} in the world.");
        }
        foreach (var id in inLeague.Keys.OrderBy(k => k))
            if (!inWorld.ContainsKey(id))
                throw new HistoryException(HistoryError.LeagueStateMismatch,
                    $"the career's league state disagrees with the world as it is {when}: school {id.ToString(CultureInfo.InvariantCulture)} " +
                    $"is in {where} but not in the world.");
    }

    // ── ★ S125 (K4) — the rules a season was played under ───────────────────────────────

    private static string? _rulesStamp;

    /// <summary>★ S125 (K4) — the build stamp: SHA-256 over the on-disk bytes of the three local
    /// assemblies, Charm.Engine, Charm.History and Charm.Harness, in that order. A build-identity
    /// check, not a compatibility proof: any rebuild that changes a byte changes it, and an equal
    /// stamp says nothing about worlds or settings. Debug and Release stamp differently.</summary>
    private static string RulesStamp()
    {
        if (_rulesStamp is not null) return _rulesStamp;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var asm in new[] { typeof(Charm.Engine.GovernorConfig).Assembly, typeof(HistoryStore).Assembly, typeof(Program).Assembly })
            hash.AppendData(File.ReadAllBytes(asm.Location));
        return _rulesStamp = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>★ S125 (K4) — one season's rules entry: its first legal day, the settings file as
    /// read, the build stamp.</summary>
    private static SeasonRules CareerRulesFor(DateOnly firstLegalDay, string engineConfigPath)
        => new(firstLegalDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
               Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(engineConfigPath))).ToLowerInvariant(),
               RulesStamp());
}
