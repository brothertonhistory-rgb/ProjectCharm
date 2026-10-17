using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 114 — S123: THE SEASON THAT CAN STOP.
//
//  The season runner is now an object: set up, advance a day or a week or any
//  stretch at a time, close. This phase proves that stopping changes nothing.
//
//    C1  ★ chunked equals one call — day by day, week by week and random chunks,
//        against one call, on four seasons (stock career one and two, legacy stock,
//        fixture-mte career): every game, every count, all seven fingerprints, the
//        events, the tournaments, the buy games, and every file byte for byte. The
//        month lines the page prints are the same in every mode. Negative control:
//        one piece of live state dropped between chunks is seen, by name.
//    C2  the contract — backwards, past the end, Finish early, Finish twice, advance
//        after Finish, a partial advance in fixture order: each refused by name and
//        each leaving the run usable; a fault mid-season (constructed) faults the run
//        and every later call is refused naming the day; Dispose in every state.
//    C3  ★ the state inventory — every field of the run, the walk and the five game
//        sources, by name, type and category, against the classes themselves in both
//        directions, with two negative controls.
//    C4  ★ nothing on disk moved — the S121 pins on a career played in random chunks
//        both seasons; the S120 capture; and at every stage boundary the career
//        folder's files. ★ S124 (B1a): after the set-up and after every advance the
//        folder is exactly what it was before the season (the career file and its
//        lock), with the career lock held; after the close, the one-call season's
//        folder, and the career file is the S122 capture — now written by the close.
//    C5  page-only: calls and time per mode.
//
//  ★ S124 — the S122 probe's pre-play files (the six the set-up used to write) are retired
//  with the writes themselves: a season now writes nothing before its close. The one probe
//  value that survives is the career file, because the counters the set-up used to publish
//  ARE the counters the close now publishes, byte for byte.
//
//  ★ READING A FOLDER WHILE A FILE IN IT IS HELD (Windows). A reader opens every file allowing
//  writing and deletion too, or Windows refuses the open (Linux does not care, which is how
//  this would hide in the sandbox). The one-byte control runs on a copy.
// ============================================================================

internal static partial class Program
{
    private const int SeasonRunChunkSeed = 123123;

    // ── ★ CAPTURED FROM THE S125 BUILD (re-pinned from the S122 build's v3 bytes, 9c892e97…, when the
    //    career file became v4): the stock career's season one under the fixed history id and fixed rules. ──
    /// <summary>The career file after season one's close, as its portable digest (<see cref="PortableCareerDigest"/>):
    /// its numbers, every file season one wrote listed (the integer-only ones by hash), and its rules. Every season
    /// file it lists is still the S121 build's (C4a).</summary>
    private const string SeasonRunSeasonOneCareer =
        "de2705107f1a6d8a270518cb4325f8b63e8796d326d7aa7022bbf6cc56783276";

    // ══════════════════════════════════════════════════════════════════════════════
    //  ★ THE STATE INVENTORY (K6). Every instance field of the season run, the walk and the
    //  five game sources. A field added or removed without updating this list fails Phase 114
    //  C3, and the failure says the field needs a category and, from B2, a place in the save.
    //
    //  The category is documentation; the check requires it but cannot verify it:
    //    Mutable           — persist and restore exactly
    //    ImmutableSetup    — persist, or rebuild from verified inputs
    //    Derived           — recompute only where equivalence is proven
    //    SharedReference   — restore the relationship, not just the values
    //    ResourceOrCallback — recreate or rebind; never serialize as data
    //
    //  ★ THIS IS A TOP-LEVEL LIST. It proves no field was added unnoticed. It does not see
    //  inside the stats accumulator (one entry), behind a reference, or into a callback. The
    //  save's completeness is B2's to prove: a snapshot restored in a fresh process finishing
    //  identical to an uninterrupted season.
    // ══════════════════════════════════════════════════════════════════════════════
    private enum SeasonStateCategory { Mutable, ImmutableSetup, Derived, SharedReference, ResourceOrCallback }

    private sealed record SeasonStateEntry(
        string Type, string Field, string FieldType, SeasonStateCategory Category, string Note = "");

    private static readonly Type[] SeasonStateTypes =
    {
        typeof(SeasonRun), typeof(SeasonCalendarRun),
        typeof(SeasonStaticSource), typeof(MteBracketSource), typeof(MteShowcaseSource),
        typeof(ConfTourneySource), typeof(BuyGameSource),
    };

    private static readonly SeasonStateEntry[] SeasonStateInventory =
    {
        // ── SeasonRun ──────────────────────────────────────────────────────────────
        new("SeasonRun", "history", "HistoryStore", SeasonStateCategory.ResourceOrCallback, "the caller's career store; rebound, never disposed by the run"),
        new("SeasonRun", "pendingSeasonId", "long", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "seasonYearOfRun", "int", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "eventHistory", "MteHistory", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "contracts", "ContractSeasonOutcome", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "seating", "EventSeatingOutcome", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "nonConference", "NonConferenceReport", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "matching", "MatchingReport", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "schedule", "List<SeasonGame>", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "memoryOutcome", "SeasonMemoryOutcome", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "rotationOutcome", "SeasonRotationOutcome", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "nonConferenceDates", "NonConDateReport", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "eventRecordBody", "Byte[]", SeasonStateCategory.ImmutableSetup, "★ S124 — the NotPlayed record, written at the close"),
        new("SeasonRun", "keepsLog", "bool", SeasonStateCategory.ImmutableSetup, "★ S124"),
        new("SeasonRun", "logRoster", "List<RosterEntryV2>", SeasonStateCategory.ImmutableSetup, "★ S124 — the log's roster, written at the close"),
        new("SeasonRun", "verbose", "bool", SeasonStateCategory.ImmutableSetup, "rebound from the caller (★ S127 B2a)"),
        new("SeasonRun", "divvy", "DivvyResult", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "people", "PeopleSummary", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "development", "DevelopmentStep", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "devStates", "DevState[]", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "rowsBySchool", "Dictionary<int, List<GenPlayerRow>>", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "cfgs", "GenEngineConfigs", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "roadShave", "int", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "baseSeed", "int", SeasonStateCategory.ImmutableSetup),
        new("SeasonRun", "seasonSeed", "long", SeasonStateCategory.ImmutableSetup, "★ S127 — the seed as given; a mid-season save records it"),
        new("SeasonRun", "fingerprint", "string", SeasonStateCategory.Derived, "the schedule's hash"),
        new("SeasonRun", "datedFingerprint", "string", SeasonStateCategory.Derived, "the dated schedule's hash"),
        new("SeasonRun", "eventGameCount", "int", SeasonStateCategory.Derived),
        new("SeasonRun", "leaguesTooSmall", "int", SeasonStateCategory.Derived),
        new("SeasonRun", "totalGames", "int", SeasonStateCategory.Derived),
        new("SeasonRun", "league", "SeasonLeagueStats", SeasonStateCategory.Mutable, "ONE entry; its nested inventory is B2's"),
        new("SeasonRun", "wins", "Dictionary<int, int>", SeasonStateCategory.Mutable),
        new("SeasonRun", "losses", "Dictionary<int, int>", SeasonStateCategory.Mutable),
        new("SeasonRun", "ties", "int", SeasonStateCategory.Mutable),
        new("SeasonRun", "hostedRoadSidesShaved", "int", SeasonStateCategory.Mutable),
        new("SeasonRun", "leagueRoadSidesShaved", "int", SeasonStateCategory.Mutable),
        new("SeasonRun", "secondsByPool", "Dictionary<int, long>", SeasonStateCategory.Mutable),
        new("SeasonRun", "teamGames", "Dictionary<int, int>", SeasonStateCategory.Mutable),
        new("SeasonRun", "possessionSlots", "int?[]", SeasonStateCategory.Mutable),
        new("SeasonRun", "sideSlots", "SidePossessionCount[]", SeasonStateCategory.Mutable),
        new("SeasonRun", "putbackSlots", "GamePutbackAudit[]", SeasonStateCategory.Mutable),
        new("SeasonRun", "laneSlots", "GameLaneAudit[]", SeasonStateCategory.Mutable),
        new("SeasonRun", "playedSlots", "PlayedSeasonGame[]", SeasonStateCategory.Mutable),
        new("SeasonRun", "logSlots", "(GameBlockFactsV1, List<PerGameStatRowV1>)?[]", SeasonStateCategory.Mutable, "held blocks, appended in ordinal order at the close"),
        new("SeasonRun", "bracketSources", "List<MteBracketSource>", SeasonStateCategory.Mutable, "each source's fields are inventoried below"),
        new("SeasonRun", "showcaseSource", "MteShowcaseSource", SeasonStateCategory.Mutable),
        new("SeasonRun", "confTourneySources", "List<ConfTourneySource>", SeasonStateCategory.Mutable),
        new("SeasonRun", "buySource", "BuyGameSource", SeasonStateCategory.Mutable),
        new("SeasonRun", "calendar", "SeasonCalendarRun", SeasonStateCategory.Mutable, "the walk; its fields are inventoried below"),
        new("SeasonRun", "resultSlots", "List<SeasonGameResult>", SeasonStateCategory.SharedReference, "the SAME list every ConfTourneySource reads"),
        new("SeasonRun", "closeStepForTest", "Action<string>", SeasonStateCategory.ResourceOrCallback, "★ S124 — the close's test seam; null in production"),
        new("SeasonRun", "world", "WorldFile", SeasonStateCategory.SharedReference, "★ S125 — the world the season was set up from; the close compares and copies its prestige"),
        new("SeasonRun", "engineConfigPath", "string", SeasonStateCategory.ImmutableSetup, "★ S125 — the settings file the season's rules entry hashes"),
        new("SeasonRun", "finished", "bool", SeasonStateCategory.Mutable, "lifecycle; a save is only taken of an open run"),
        new("SeasonRun", "disposed", "bool", SeasonStateCategory.Mutable, "lifecycle"),
        new("SeasonRun", "faultedOn", "string", SeasonStateCategory.Mutable, "lifecycle"),
        new("SeasonRun", "advancing", "bool", SeasonStateCategory.Mutable, "★ S127 — lifecycle: a day or the close is running, so no save"),

        // ── SeasonCalendarRun ──────────────────────────────────────────────────────
        new("SeasonCalendarRun", "_sources", "IReadOnlyList<SeasonGameSource>", SeasonStateCategory.SharedReference, "the same source objects the season run holds"),
        new("SeasonCalendarRun", "_order", "SeasonPlayOrder", SeasonStateCategory.ImmutableSetup),
        new("SeasonCalendarRun", "_play", "Func<PlayedSeasonGame, (int, int)>", SeasonStateCategory.ResourceOrCallback, "the season run's PlayOneGame"),
        new("SeasonCalendarRun", "_monthDone", "Action<string, int>", SeasonStateCategory.ResourceOrCallback, "the page's month line"),
        new("SeasonCalendarRun", "_firstLegalDay", "DateOnly", SeasonStateCategory.ImmutableSetup),
        new("SeasonCalendarRun", "_lastLegalDay", "DateOnly", SeasonStateCategory.ImmutableSetup),
        new("SeasonCalendarRun", "_total", "int", SeasonStateCategory.Derived),
        new("SeasonCalendarRun", "_slots", "(SeasonGameSource, int, DateOnly)?[]", SeasonStateCategory.Derived, "built from the sources; holds references to them"),
        new("SeasonCalendarRun", "_byDay", "Dictionary<DateOnly, List<int>>", SeasonStateCategory.Derived),
        new("SeasonCalendarRun", "_playOrder", "List<int>", SeasonStateCategory.Mutable),
        new("SeasonCalendarRun", "_played", "bool[]", SeasonStateCategory.Mutable),
        new("SeasonCalendarRun", "_schoolsToday", "HashSet<int>", SeasonStateCategory.Mutable),
        new("SeasonCalendarRun", "_currentDay", "DateOnly?", SeasonStateCategory.Mutable),
        new("SeasonCalendarRun", "_currentMonth", "string", SeasonStateCategory.Mutable),
        new("SeasonCalendarRun", "_monthGames", "int", SeasonStateCategory.Mutable),
        new("SeasonCalendarRun", "_nextDay", "DateOnly", SeasonStateCategory.Mutable),
        new("SeasonCalendarRun", "_finished", "bool", SeasonStateCategory.Mutable, "lifecycle"),
        new("SeasonCalendarRun", "_disposed", "bool", SeasonStateCategory.Mutable, "lifecycle"),
        new("SeasonCalendarRun", "_faultedOn", "string", SeasonStateCategory.Mutable, "lifecycle"),

        // ── SeasonStaticSource (the league slate) ──────────────────────────────────
        new("SeasonStaticSource", "_name", "string", SeasonStateCategory.ImmutableSetup),
        new("SeasonStaticSource", "_games", "IReadOnlyList<PlayedSeasonGame>", SeasonStateCategory.ImmutableSetup),

        // ── MteBracketSource ───────────────────────────────────────────────────────
        new("MteBracketSource", "_event", "SeatedEvent", SeasonStateCategory.ImmutableSetup),
        new("MteBracketSource", "_plan", "BracketPlan", SeasonStateCategory.ImmutableSetup),
        new("MteBracketSource", "_routes", "BracketRoute[]", SeasonStateCategory.Derived, "the route table for the field size"),
        new("MteBracketSource", "_firstOrdinal", "int", SeasonStateCategory.ImmutableSetup),
        new("MteBracketSource", "_dates", "DateOnly[]", SeasonStateCategory.ImmutableSetup),
        new("MteBracketSource", "_occupancy", "int[][]", SeasonStateCategory.Mutable),
        new("MteBracketSource", "_played", "bool[]", SeasonStateCategory.Mutable),
        new("MteBracketSource", "_placeBySeat", "Dictionary<int, int>", SeasonStateCategory.Mutable),
        new("MteBracketSource", "_reservations", "IReadOnlyDictionary<BracketSlotKey, GameId>", SeasonStateCategory.ImmutableSetup),
        new("MteBracketSource", "_seasonId", "SeasonId?", SeasonStateCategory.ImmutableSetup),

        // ── MteShowcaseSource ──────────────────────────────────────────────────────
        new("MteShowcaseSource", "_games", "List<(SeatedEvent, ShowcasePairing, PlayedSeasonGame)>", SeasonStateCategory.ImmutableSetup),
        new("MteShowcaseSource", "_events", "List<SeatedEvent>", SeasonStateCategory.ImmutableSetup),
        new("MteShowcaseSource", "_results", "ShowcaseResult[]", SeasonStateCategory.Mutable),

        // ── ConfTourneySource ──────────────────────────────────────────────────────
        new("ConfTourneySource", "_conferenceId", "int", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_conferenceName", "string", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_members", "IReadOnlyList<int>", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_open", "DateOnly", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_firstOrdinal", "int", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_leagueFixtures", "IReadOnlyList<(int, SeasonGame)>", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_resultsByOrdinal", "IReadOnlyList<SeasonGameResult>", SeasonStateCategory.SharedReference, "the season run's resultSlots, the same list"),
        new("ConfTourneySource", "_placeOfSchool", "IReadOnlyDictionary<int, int>", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_reservations", "IReadOnlyDictionary<ConfTourneySlotKey, GameId>", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_seasonId", "SeasonId?", SeasonStateCategory.ImmutableSetup),
        new("ConfTourneySource", "_shape", "KnockoutBracket", SeasonStateCategory.Derived, "the topology for eight"),
        new("ConfTourneySource", "_played", "bool[]", SeasonStateCategory.Mutable),
        new("ConfTourneySource", "_rows", "ConfTourneyGameRow[]", SeasonStateCategory.Mutable),
        new("ConfTourneySource", "_plan", "ConfTourneyPlan", SeasonStateCategory.Mutable, "seeded the morning it opens"),
        new("ConfTourneySource", "_bracket", "KnockoutBracket", SeasonStateCategory.Mutable, "seeded the morning it opens"),
        new("ConfTourneySource", "_occupancy", "int[][]", SeasonStateCategory.Mutable),
        new("ConfTourneySource", "_champion", "int", SeasonStateCategory.Mutable),
        new("ConfTourneySource", "_runnerUp", "int", SeasonStateCategory.Mutable),

        // ── BuyGameSource ──────────────────────────────────────────────────────────
        new("BuyGameSource", "_games", "List<(int, PlayedSeasonGame)>", SeasonStateCategory.ImmutableSetup),
        new("BuyGameSource", "_rows", "BuyGameRow[]", SeasonStateCategory.Mutable),
    };

    /// <summary>A field's declared type as C# reads (nullable reference marks are invisible to
    /// reflection and are not part of the name; tuple element names likewise).</summary>
    private static string SeasonStateTypeName(Type t)
    {
        if (t.IsArray) return SeasonStateTypeName(t.GetElementType()!) + "[]";
        if (Nullable.GetUnderlyingType(t) is { } u) return SeasonStateTypeName(u) + "?";
        if (t.IsGenericType)
        {
            var args = t.GetGenericArguments().Select(SeasonStateTypeName);
            if (t.Name.StartsWith("ValueTuple`", StringComparison.Ordinal)) return "(" + string.Join(", ", args) + ")";
            return t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(", ", args) + ">";
        }
        return t.Name switch
        {
            "Int32" => "int", "Int64" => "long", "Boolean" => "bool", "String" => "string", "Double" => "double",
            _ => t.Name,
        };
    }

    /// <summary>Every instance field of a type and its bases, public or not, compiler-made included.</summary>
    private static IEnumerable<(string Field, string FieldType)> SeasonStateReflect(Type t)
    {
        for (var x = t; x is not null && x != typeof(object); x = x.BaseType)
            foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return (f.Name, SeasonStateTypeName(f.FieldType));
    }

    /// <summary>Null when the list and the classes agree, both directions, by name and type, with a
    /// category on every entry; otherwise every disagreement, by name.</summary>
    private static string? SeasonStateInventoryFault(IReadOnlyList<SeasonStateEntry> list, IReadOnlyList<Type> types)
    {
        var faults = new List<string>();
        foreach (var t in types)
        {
            var reflected = SeasonStateReflect(t).ToDictionary(f => f.Field, f => f.FieldType, StringComparer.Ordinal);
            var listed = list.Where(e => e.Type == t.Name).ToList();
            foreach (var (field, type) in reflected)
            {
                var e = listed.FirstOrDefault(x => x.Field == field);
                if (e is null)
                    faults.Add($"{t.Name}.{field} ({type}) is not in the inventory — give it a category and, from B2, a place in the save");
                else if (e.FieldType != type)
                    faults.Add($"{t.Name}.{field} is listed as {e.FieldType} but declared {type}");
                else if (!Enum.IsDefined(e.Category))
                    faults.Add($"{t.Name}.{field} has no category");
            }
            foreach (var e in listed.Where(e => !reflected.ContainsKey(e.Field)))
                faults.Add($"{t.Name}.{e.Field} is listed but the class has no such field");
        }
        return faults.Count == 0 ? null : string.Join("; ", faults);
    }

#pragma warning disable CS0169, CS0414, IDE0051   // fields that exist only to be reflected
    /// <summary>C3's control: a type with one field the control list leaves out.</summary>
    private sealed class SeasonStateControlType
    {
        private int _alpha;
        private string _beta = "";
    }
#pragma warning restore CS0169, CS0414, IDE0051

    /// <summary>C2's constructed fault: a source whose games throw on one chosen day.</summary>
    private sealed class SeasonFaultingSource : SeasonGameSource
    {
        private readonly IReadOnlyList<PlayedSeasonGame> _games;
        private readonly DateOnly _faultDay;
        public SeasonFaultingSource(IReadOnlyList<PlayedSeasonGame> games, DateOnly faultDay)
        {
            _games = games;
            _faultDay = faultDay;
        }
        public override string Name => "the constructed faulting source";
        public override int Count => _games.Count;
        public override int Ordinal(int local) => _games[local].FixtureOrdinal;
        public override DateOnly? Date(int local) => _games[local].Game.Date;
        public override PlayedSeasonGame Release(int local)
            => _games[local].Game.Date == _faultDay
                ? throw new InvalidOperationException("CONSTRUCTED FAULT: this source refuses its game on " +
                                                      _faultDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".")
                : _games[local];
    }

    private enum SeasonChunkMode { OneCall, DayByDay, Weekly, Random }

    /// <summary>Every file in a career folder: the SHA-256 of each readable file, and the bare
    /// existence of each lock sidecar (held with no sharing while its writer lives). A live log is
    /// opened allowing writers, which Windows requires; Linux does not.</summary>
    private static SortedDictionary<string, string> SeasonFolderSnapshot(string dir)
    {
        var snap = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
            if (rel.EndsWith(".lock", StringComparison.Ordinal)) { snap[rel] = "(lock)"; continue; }
            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            snap[rel] = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        return snap;
    }

    private static string? SeasonSnapshotDiff(SortedDictionary<string, string> a, SortedDictionary<string, string> b)
    {
        if (!a.Keys.SequenceEqual(b.Keys))
            return $"file lists differ: [{string.Join(", ", a.Keys)}] vs [{string.Join(", ", b.Keys)}]";
        foreach (var (rel, hash) in a)
            if (b[rel] != hash) return $"{rel} differs";
        return null;
    }

    private static byte[] SeasonReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>True when another handle holds the file with no sharing (a competing exclusive open fails).</summary>
    private static bool SeasonLockHeld(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
    }

    /// <summary>The first property in which two seasons differ, by name; null when every compared
    /// property agrees.</summary>
    private static string? SeasonOutcomeDiff(SeasonRunOutcome a, SeasonRunOutcome b)
    {
        static bool DictEq<TK, TV>(IReadOnlyDictionary<TK, TV> x, IReadOnlyDictionary<TK, TV> y) where TK : notnull
            => x.Count == y.Count && x.All(kv => y.TryGetValue(kv.Key, out var v) && EqualityComparer<TV>.Default.Equals(kv.Value, v));
        if (!a.Results.SequenceEqual(b.Results)) return "Results";
        if (!a.PossessionCounts.SequenceEqual(b.PossessionCounts)) return "PossessionCounts";
        if (!a.SidePossessions.SequenceEqual(b.SidePossessions)) return "SidePossessions";
        if (!a.PlayOrder.SequenceEqual(b.PlayOrder)) return "PlayOrder";
        if (!DictEq(a.Wins, b.Wins)) return "Wins";
        if (!DictEq(a.Losses, b.Losses)) return "Losses";
        if (a.Ties != b.Ties) return "Ties";
        if (!DictEq(a.SecondsByPool, b.SecondsByPool)) return "SecondsByPool";
        if (!DictEq(a.TeamGames, b.TeamGames)) return "TeamGames";
        if (a.Fingerprint != b.Fingerprint) return "fingerprint #1 (conference schedule)";
        if (a.DatedFingerprint != b.DatedFingerprint) return "fingerprint #2 (conference dated)";
        if (a.EventGamesFingerprint != b.EventGamesFingerprint) return "fingerprint #3 (event games)";
        if (SeasonFingerprint(a.Results, a.PossessionCounts) != SeasonFingerprint(b.Results, b.PossessionCounts))
            return "fingerprint #4 (results + possessions)";
        if (a.NonConferenceDates.DatedFingerprint != b.NonConferenceDates.DatedFingerprint) return "fingerprint #5 (non-conference dated)";
        if (a.ConferenceTournamentFingerprint != b.ConferenceTournamentFingerprint) return "fingerprint #6 (conference tournaments)";
        if (a.BuyGamesFingerprint != b.BuyGamesFingerprint) return "fingerprint #7 (buy games)";
        if (a.EventFinishes.Count != b.EventFinishes.Count
            || a.EventFinishes.Any(kv => !b.EventFinishes.TryGetValue(kv.Key, out var o) || !DictEq(kv.Value, o)))
            return "EventFinishes";
        if (!a.ConferenceTournaments.Rows.SequenceEqual(b.ConferenceTournaments.Rows)) return "conference tournament rows";
        if (a.ConferenceTournaments.Champions.Count != b.ConferenceTournaments.Champions.Count
            || a.ConferenceTournaments.Champions.Zip(b.ConferenceTournaments.Champions).Any(p =>
                   p.First.ConferenceId != p.Second.ConferenceId || p.First.Champion != p.Second.Champion
                   || p.First.RunnerUp != p.Second.RunnerUp || !p.First.SchoolBySeed.SequenceEqual(p.Second.SchoolBySeed)))
            return "conference tournament champions";
        if (!a.BuyGames.SequenceEqual(b.BuyGames)) return "BuyGames";
        return null;
    }

    private static bool Phase114SeasonRunCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 114 — S123: the season that can stop. The runner is an object that advances a day at a time " +
                          "and closes on command; chunked equals one call, the contract refuses by name, every piece of live state " +
                          "is listed, and nothing on disk moved at any stage ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static string? Refusal(Action a)
        {
            try { a(); return null; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }
        static bool Says(string? refusal, string phrase) => refusal is not null && refusal.Contains(phrase, StringComparison.Ordinal);
        static string Short(string? s) => s is null ? "NO REFUSAL" : s[..Math.Min(130, s.Length)];
        static string GamesDigest(SeasonRunOutcome run) => RatingSha(string.Concat(run.Results.Select((x, i) =>
            $"{i}|{x.HomeId}|{x.AwayId}|{x.HomeScore}|{x.AwayScore}|{run.PossessionCounts[i]}\n")));
        static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var monthLine = new Regex(@"^  \.\.\. [A-Z][a-z]+: [\d,]+ games(?=\r?$)", RegexOptions.Multiline);   // \r: Windows line ends
        var timings = new List<string>();

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s123-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var mte = LoadWorld(WorldPath("fixture-mte.world.json"));
            var seedTwo = SeasonTwoSeed(CalendarCheckSeed);
            using var fixedId = HistoryStore.UseFixedHistoryIdForTests(CalendarFixedHistoryId);
            // ★ S125 — the career files pinned here record each season's rules under the fixed-rules seam.
            using var fixedRules = HistoryStore.UseFixedRulesForTests(CheckFixedConfigSha, CheckFixedRulesStamp);

            int Advance(SeasonRun run, SeasonChunkMode mode, Action<SeasonRun>? afterChunk)
            {
                var calls = 0;
                void Step(DateOnly d) { run.AdvanceThrough(d); calls++; afterChunk?.Invoke(run); }
                DateOnly Cap(DateOnly d) => d > run.LastLegalDay ? run.LastLegalDay : d;
                switch (mode)
                {
                    case SeasonChunkMode.OneCall:
                        Step(run.LastLegalDay);
                        break;
                    case SeasonChunkMode.DayByDay:
                        while (!run.IsComplete) Step(run.NextDay);
                        break;
                    case SeasonChunkMode.Weekly:
                        while (!run.IsComplete) Step(Cap(run.NextDay.AddDays(6)));
                        break;
                    case SeasonChunkMode.Random:
                        var rng = new SystemRng(SeasonRunChunkSeed);
                        var n = 0;
                        while (!run.IsComplete)
                        {
                            var len = 1 + (int)(rng.NextUnitInterval() * 30);   // 1..30 days
                            Step(Cap(run.NextDay.AddDays(len - 1)));
                            if (++n % 3 == 0) { run.AdvanceThrough(run.NextDay.AddDays(-1)); calls++; }   // the repeated no-op
                        }
                        break;
                }
                return calls;
            }

            //  One season through the season run, verbose so the page's month lines can be read back;
            //  the console is captured for the run and restored before any Check prints.
            (SeasonRunOutcome Outcome, List<string> Months) Play(
                string label, WorldFile w, long seed, string? career, SeasonChunkMode mode,
                Action<SeasonRun>? afterBegin = null, Action<SeasonRun>? afterChunk = null, Action? afterFinish = null,
                Action? beforeBegin = null)
            {
                var sw = Stopwatch.StartNew();
                var store = career is null ? null : OpenCareer(career, w);
                beforeBegin?.Invoke();
                var old = Console.Out;
                var text = new StringWriter(CultureInfo.InvariantCulture);
                SeasonRunOutcome outcome;
                int calls;
                try
                {
                    Console.SetOut(text);
                    using var run = SeasonBegin(w, seed, configPath, verbose: true,
                        new SeasonRunOptions(History: store, RetainGameLog: store is not null));
                    afterBegin?.Invoke(run);
                    calls = Advance(run, mode, afterChunk);
                    outcome = run.Finish();
                    afterFinish?.Invoke();   // before Dispose, so the close's own release is what is seen
                }
                finally
                {
                    Console.SetOut(old);
                    store?.Dispose();
                }
                timings.Add(Inv($"{label}, {mode}: {calls} AdvanceThrough call(s), {sw.Elapsed.TotalSeconds:F1}s"));
                return (outcome, monthLine.Matches(text.ToString()).Select(m => m.Value).ToList());
            }
            string Career(string folder) => Path.Combine(scratch, folder, "career.json");
            string Folder(string folder) => Path.Combine(scratch, folder);
            GameLogV1 ReadLog(string career, long season)
            {
                using var store = OpenCareer(career, stock);
                return GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(career, season),
                    new GameLogBindings(store.HistoryId, store.WorldFingerprint, season));
            }
            var modes = new[] { SeasonChunkMode.OneCall, SeasonChunkMode.DayByDay, SeasonChunkMode.Weekly, SeasonChunkMode.Random };
            var monthFaults = new List<string>();
            void SameMonths(string label, List<string> a, List<string> b)
            {
                if (a.Count == 0 || !a.SequenceEqual(b)) monthFaults.Add(Inv($"{label}: {a.Count} vs {b.Count} lines"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C1 and C4 — the stock career, season one, in four modes. The random-chunk run is
            //  the instrumented one: its folder is read after the set-up, after every advance, and
            //  after the close.
            // ════════════════════════════════════════════════════════════════════
            var s1 = new Dictionary<SeasonChunkMode, (SeasonRunOutcome Outcome, List<string> Months)>();
            SortedDictionary<string, string>? beforeSeason = null, afterBegin = null;
            var lockHeldAtBegin = false;
            var s1CareerLockHeldAfterFinish = false;
            var advanceFaults = new List<string>();
            var advancesWatched = 0;
            string? copyControl = null;
            SortedDictionary<string, string>? s1FinishSnap = null;
            byte[]? s1FinishCareer = null;   // ★ S125 — the career file's bytes at the close, for its portable digest
            var s1LockFreeAfterFinish = false;
            foreach (var mode in modes)
            {
                var folder = "s1-" + mode;
                Directory.CreateDirectory(Folder(folder));
                var logLock = Path.Combine(Folder(folder), "career.gamelog", "season-1.log.lock");
                var careerLock = Career(folder) + ".lock";
                Action<SeasonRun>? begin = null, chunk = null;
                Action? finish = null, before = null;
                if (mode == SeasonChunkMode.Random)
                {
                    before = () => beforeSeason = SeasonFolderSnapshot(Folder(folder));
                    begin = _ =>
                    {
                        afterBegin = SeasonFolderSnapshot(Folder(folder));
                        lockHeldAtBegin = SeasonLockHeld(careerLock);
                    };
                    chunk = run =>
                    {
                        advancesWatched++;
                        var diff = SeasonSnapshotDiff(beforeSeason!, SeasonFolderSnapshot(Folder(folder)));
                        if (diff is not null) advanceFaults.Add(Inv($"after advancing through {run.NextDay.AddDays(-1):yyyy-MM-dd}: {diff}"));
                        if (!SeasonLockHeld(careerLock)) advanceFaults.Add(Inv($"career lock free after {run.NextDay.AddDays(-1):yyyy-MM-dd}"));
                        if (copyControl is null)
                        {
                            //  The control's copy, taken once, from the live folder. The live log is never written.
                            copyControl = Path.Combine(scratch, "c4-control");
                            foreach (var f in Directory.GetFiles(Folder(folder), "*", SearchOption.AllDirectories))
                            {
                                var to = Path.Combine(copyControl, Path.GetRelativePath(Folder(folder), f));
                                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                                File.WriteAllBytes(to, f.EndsWith(".lock", StringComparison.Ordinal) ? Array.Empty<byte>() : SeasonReadShared(f));
                            }
                        }
                    };
                    finish = () =>
                    {
                        s1FinishSnap = SeasonFolderSnapshot(Folder(folder));
                        s1FinishCareer = File.ReadAllBytes(Career(folder));
                        s1LockFreeAfterFinish = !SeasonLockHeld(logLock);
                        s1CareerLockHeldAfterFinish = SeasonLockHeld(careerLock);
                    };
                }
                s1[mode] = Play("stock career season one", stock, CalendarCheckSeed, Career(folder), mode, begin, chunk, finish, before);
            }
            //  The one-call career as season one left it (season two runs in the same folder below).
            var s1OneCallSnap = SeasonFolderSnapshot(Folder("s1-OneCall"));

            // ── C1a ──
            {
                var one = s1[SeasonChunkMode.OneCall];
                var oneDigest = CalendarBlocksDigest(ReadLog(Career("s1-OneCall"), 1));
                foreach (var mode in modes.Skip(1))
                {
                    var r = s1[mode];
                    var diff = SeasonOutcomeDiff(one.Outcome, r.Outcome);
                    var files = CalendarFilesDiffer(Folder("s1-OneCall"), Folder("s1-" + mode));
                    var digest = CalendarBlocksDigest(ReadLog(Career("s1-" + mode), 1));
                    Check(Inv($"C1a: ★ stock career season one, {mode} equals one call — every game, count, fingerprint, event, ") +
                          "tournament and buy game; every file byte for byte; every game block decoded",
                          diff is null && files is null && digest == oneDigest,
                          diff ?? files ?? (digest == oneDigest ? Inv($"{r.Outcome.PlayOrder.Count:N0} games, blocks {digest[..16]}") : "block digests differ"));
                    SameMonths(Inv($"stock season one {mode}"), one.Months, r.Months);
                }
            }

            // ── C1b — season two of the same careers: one call against random chunks. ──
            var s2One = Play("stock career season two", stock, seedTwo, Career("s1-OneCall"), SeasonChunkMode.OneCall);
            var s2Random = Play("stock career season two", stock, seedTwo, Career("s1-Random"), SeasonChunkMode.Random);
            {
                var diff = SeasonOutcomeDiff(s2One.Outcome, s2Random.Outcome);
                var files = CalendarFilesDiffer(Folder("s1-OneCall"), Folder("s1-Random"));
                Check("C1b: ★ stock career season two (the turnover, the camp and the scouting file in play), random chunks equal " +
                      "one call — and after two seasons every file of both careers byte for byte",
                      diff is null && files is null && s2One.Outcome.People is not null,
                      diff ?? files ?? Inv($"{s2Random.Outcome.PlayOrder.Count:N0} games"));
                SameMonths("stock season two", s2One.Months, s2Random.Months);
            }

            // ── C1c — the legacy stock season. ──
            var legacyOne = Play("legacy stock season", stock, CalendarCheckSeed, null, SeasonChunkMode.OneCall);
            var legacyDay = Play("legacy stock season", stock, CalendarCheckSeed, null, SeasonChunkMode.DayByDay);
            {
                var diff = SeasonOutcomeDiff(legacyOne.Outcome, legacyDay.Outcome);
                Check("C1c: the legacy stock season, day by day equals one call", diff is null,
                      diff ?? Inv($"{legacyDay.Outcome.PlayOrder.Count:N0} games"));
                SameMonths("legacy stock season", legacyOne.Months, legacyDay.Months);
            }

            // ── C1d — a fixture-mte career season, four modes. ──
            var mteRuns = new Dictionary<SeasonChunkMode, (SeasonRunOutcome Outcome, List<string> Months)>();
            foreach (var mode in modes)
            {
                Directory.CreateDirectory(Folder("mte-" + mode));
                mteRuns[mode] = Play("fixture-mte career season", mte, CalendarCheckSeed, Career("mte-" + mode), mode);
            }
            var mteOne = mteRuns[SeasonChunkMode.OneCall].Outcome;
            foreach (var mode in modes.Skip(1))
            {
                var diff = SeasonOutcomeDiff(mteOne, mteRuns[mode].Outcome);
                var files = CalendarFilesDiffer(Folder("mte-OneCall"), Folder("mte-" + mode));
                Check(Inv($"C1d: a fixture-mte career season (brackets and showcases), {mode} equals one call, every file byte for byte"),
                      diff is null && files is null && mteOne.TournamentGameCount > 0,
                      diff ?? files ?? Inv($"{mteRuns[mode].Outcome.PlayOrder.Count} games, {mteOne.TournamentGameCount} event games"));
                SameMonths(Inv($"fixture-mte {mode}"), mteRuns[SeasonChunkMode.OneCall].Months, mteRuns[mode].Months);
            }

            // ── C1e — the month lines. ──
            Check("C1e: ★ the month lines the page prints (month and games) are identical in every mode of every season above",
                  monthFaults.Count == 0,
                  monthFaults.Count > 0 ? string.Join("; ", monthFaults)
                      : Inv($"stock season one: {string.Join(" | ", s1[SeasonChunkMode.OneCall].Months.Select(m => m.Trim()))}"));

            // ── C1f — NEGATIVE CONTROL: one lost piece of live state is seen. ──
            {
                Directory.CreateDirectory(Folder("mte-control"));
                using var store = OpenCareer(Career("mte-control"), mte);
                SeasonRunOutcome dropped;
                int heldBefore;
                using (var run = SeasonBegin(mte, CalendarCheckSeed, configPath, verbose: false,
                           new SeasonRunOptions(History: store, RetainGameLog: true)))
                {
                    run.AdvanceThrough(run.FirstLegalDay.AddDays(29));
                    var field = typeof(SeasonRun).GetField("secondsByPool", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    heldBefore = ((Dictionary<int, long>)field.GetValue(run)!).Count;
                    field.SetValue(run, new Dictionary<int, long>());
                    run.AdvanceThrough(run.LastLegalDay);
                    dropped = run.Finish();
                }
                var diff = SeasonOutcomeDiff(mteOne, dropped);
                Check("C1f: NEGATIVE CONTROL — after one chunk, the season's seconds-by-man emptied (by reflection); the comparison " +
                      "goes red and names it (it can see one lost piece of state; this does not show anything was lost)",
                      diff == "SecondsByPool" && heldBefore > 0, Inv($"{heldBefore} men held seconds; diff: {diff ?? "NONE"}"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — THE CONTRACT.
            // ════════════════════════════════════════════════════════════════════
            {
                Directory.CreateDirectory(Folder("mte-contract"));
                var store = OpenCareer(Career("mte-contract"), mte);
                var run = SeasonBegin(mte, CalendarCheckSeed, configPath, verbose: false,
                    new SeasonRunOptions(History: store, RetainGameLog: true));
                var startsFirst = run.NextDay == run.FirstLegalDay && !run.IsComplete && run.GamesPlayed == 0;
                var early = Refusal(() => run.Finish());
                var past = Refusal(() => run.AdvanceThrough(run.LastLegalDay.AddDays(1)));
                run.AdvanceThrough(run.FirstLegalDay.AddDays(20));
                var playedThen = run.GamesPlayed;
                var back = Refusal(() => run.AdvanceThrough(run.FirstLegalDay.AddDays(5)));
                run.AdvanceThrough(run.NextDay.AddDays(-1));   // the no-op
                var noop = run.GamesPlayed == playedThen && run.NextDay == run.FirstLegalDay.AddDays(21);
                Check("C2a: a new run's next day is the season's first legal day, nothing played, not complete", startsFirst,
                      Inv($"{run.FirstLegalDay:yyyy-MM-dd}..{run.LastLegalDay:yyyy-MM-dd}"));
                Check("C2b: REFUSED BY NAME — Finish before the season is complete", Says(early, "Finish before the season is complete"), Short(early));
                Check("C2c: REFUSED BY NAME — an advance past the last day", Says(past, "past the last day"), Short(past));
                Check("C2d: REFUSED BY NAME — an advance backwards", Says(back, "goes backwards"), Short(back));
                Check("C2e: an advance through the day already played is a no-op: nothing plays, the next day stays",
                      noop && playedThen > 0, Inv($"{playedThen} games through {run.FirstLegalDay.AddDays(20):yyyy-MM-dd}"));
                run.AdvanceThrough(run.LastLegalDay);
                var outcome = run.Finish();
                //  ★ S124 — the career lock is the store's, held until it is disposed; the folder is compared
                //    byte for byte (its lock sidecar included), so the store is released first.
                store.Dispose();
                var diff = SeasonOutcomeDiff(mteOne, outcome);
                var files = CalendarFilesDiffer(Folder("mte-OneCall"), Folder("mte-contract"));
                Check("C2f: ★ after every refusal above the same run advanced and finished identical to C1d's one-call run, " +
                      "every file byte for byte — a precondition refusal leaves the run usable",
                      diff is null && files is null, diff ?? files ?? Inv($"{outcome.PlayOrder.Count} games"));
                var twice = Refusal(() => run.Finish());
                var after = Refusal(() => run.AdvanceThrough(run.LastLegalDay));
                Check("C2g: REFUSED BY NAME — Finish twice, and an advance after Finish",
                      Says(twice, "Finish was called twice") && Says(after, "after Finish"), Short(twice) + " / " + Short(after));
                Exception? disposeFault = null;
                try { run.Dispose(); run.Dispose(); } catch (Exception ex) { disposeFault = ex; }
                var deadAdvance = Refusal(() => run.AdvanceThrough(run.LastLegalDay));
                var deadComplete = Refusal(() => _ = run.IsComplete);
                var deadFinish = Refusal(() => run.Finish());
                Check("C2h: Dispose after Finish succeeds (twice); every call after it is refused by name",
                      disposeFault is null && Says(deadAdvance, "disposed") && Says(deadComplete, "disposed") && Says(deadFinish, "disposed"),
                      disposeFault?.Message ?? Short(deadAdvance));
            }
            {
                //  A run set up and never advanced, disposed: ★ S124 — the career folder is exactly what it was
                //  before the season began (the career file byte-identical, no other file).
                Directory.CreateDirectory(Folder("mte-abandoned"));
                using var store = OpenCareer(Career("mte-abandoned"), mte);
                var before = SeasonFolderSnapshot(Folder("mte-abandoned"));
                var run = SeasonBegin(mte, CalendarCheckSeed, configPath, verbose: false,
                    new SeasonRunOptions(History: store, RetainGameLog: true));
                Exception? disposeFault = null;
                try { run.Dispose(); } catch (Exception ex) { disposeFault = ex; }
                var after = SeasonFolderSnapshot(Folder("mte-abandoned"));
                var refused = Refusal(() => run.AdvanceThrough(run.LastLegalDay));
                Check("C2i: ★ S124 — a run never advanced, disposed: Dispose succeeds, the career folder is exactly what it was " +
                      "before the season (the career file byte-identical, its lock and ★ S125 its league-0, no other file), and every call after is refused by name",
                      disposeFault is null && SeasonSnapshotDiff(before, after) is null
                      && after.Keys.SequenceEqual(new[] { "career.json", "career.json.lock", "career.league/league-0.json" })
                      && Says(refused, "disposed"),
                      disposeFault?.Message ?? SeasonSnapshotDiff(before, after) ?? Inv($"[{string.Join(", ", after.Keys)}]; {Short(refused)}"));
            }
            {
                //  Fixture order: one call or nothing.
                using var run = SeasonBegin(mte, CalendarCheckSeed, configPath, verbose: false,
                    new SeasonRunOptions(PlayOrderForTest: SeasonPlayOrder.FixtureOrdinalForTest));
                var partial = Refusal(() => run.AdvanceThrough(run.FirstLegalDay.AddDays(10)));
                var usable = run.GamesPlayed == 0 && run.NextDay == run.FirstLegalDay;
                run.AdvanceThrough(run.LastLegalDay);
                var all = run.GamesPlayed;
                run.AdvanceThrough(run.LastLegalDay);   // K3's no-op once complete
                var outcome = run.Finish();
                Check("C2j: REFUSED BY NAME — a partial advance in fixture order; the run stays usable, then plays every game in " +
                      "ordinal order in one call, and a second call through the last day is the no-op",
                      Says(partial, "fixture order") && usable && run.GamesPlayed == all
                      && outcome.PlayOrder.SequenceEqual(Enumerable.Range(0, outcome.PlayOrder.Count)),
                      Short(partial) + Inv($" ({all} games)"));
            }
            {
                //  A fault on the walk itself: a constructed source that refuses its game on November 15.
                var pairs = new List<(int, int)>();
                for (var a = 1; a <= 8; a++) for (var b = a + 1; b <= 8; b++) pairs.Add((a, b));
                var games = pairs.Select((p, i) => new PlayedSeasonGame(
                    new SeasonGame("conf", p.Item1, p.Item2, Date: new DateOnly(2026, 11, 2).AddDays(i), PlaceId: 1), i)).ToList();
                var faultDay = new DateOnly(2026, 11, 15);
                var cal = new SeasonCalendarRun(new SeasonGameSource[] { new SeasonFaultingSource(games, faultDay) }, 2026,
                                                SeasonPlayOrder.Calendar, pg => (80, 70));
                cal.AdvanceThrough(new DateOnly(2026, 11, 10));
                var before = cal.GamesPlayed;
                var thrown = Refusal(() => cal.AdvanceThrough(new DateOnly(2026, 11, 20)));
                var later = new[]
                {
                    Refusal(() => cal.AdvanceThrough(new DateOnly(2026, 11, 21))),
                    Refusal(() => cal.Finish()),
                    Refusal(() => _ = cal.IsComplete),
                };
                Exception? disposeFault = null;
                try { cal.Dispose(); } catch (Exception ex) { disposeFault = ex; }
                Check("C2k: ★ a fault mid-season (a constructed source refusing its game on 2026-11-15): the advance that reaches it " +
                      "throws the source's own refusal; every later advance, Finish and IsComplete is refused by name, naming the day; " +
                      "Dispose succeeds",
                      before == 9 && Says(thrown, "CONSTRUCTED FAULT") && later.All(r => Says(r, "failed on 2026-11-15"))
                      && disposeFault is null,
                      Short(thrown) + " / " + Short(later[1]));
            }
            {
                //  The same fault at the season-run level: the walk's play callback replaced by reflection
                //  (test-only; no production code reads anything for it), throwing on a chosen game day.
                Directory.CreateDirectory(Folder("mte-fault"));
                var store = OpenCareer(Career("mte-fault"), mte);
                var run = SeasonBegin(mte, CalendarCheckSeed, configPath, verbose: false,
                    new SeasonRunOptions(History: store, RetainGameLog: true));
                run.AdvanceThrough(run.FirstLegalDay.AddDays(9));
                var faultDay = mteOne.PlayedGames.Select(p => p.Game.Date!.Value).Where(d => d > run.FirstLegalDay.AddDays(9)).Min();
                var cal = typeof(SeasonRun).GetField("calendar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(run)!;
                var playField = typeof(SeasonCalendarRun).GetField("_play", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var original = (Func<PlayedSeasonGame, (int HomeScore, int AwayScore)>)playField.GetValue(cal)!;
                playField.SetValue(cal, new Func<PlayedSeasonGame, (int HomeScore, int AwayScore)>(pg =>
                    pg.Game.Date == faultDay ? throw new InvalidOperationException("CONSTRUCTED ENGINE FAULT") : original(pg)));
                var thrown = Refusal(() => run.AdvanceThrough(run.LastLegalDay));
                var day = faultDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var later = new[]
                {
                    Refusal(() => run.AdvanceThrough(run.LastLegalDay)),
                    Refusal(() => run.Finish()),
                    Refusal(() => _ = run.IsComplete),
                };
                var careerLock = Career("mte-fault") + ".lock";
                var heldWhileFaulted = SeasonLockHeld(careerLock);
                var noLog = !Directory.Exists(Path.Combine(Folder("mte-fault"), "career.gamelog"));
                Exception? disposeFault = null;
                try { run.Dispose(); } catch (Exception ex) { disposeFault = ex; }
                var heldAfterRunDisposed = SeasonLockHeld(careerLock);
                store.Dispose();
                var freeAfterStore = !SeasonLockHeld(careerLock);
                Check("C2l: ★ the same fault in a real career season: the advance throws the original; every later call is refused " +
                      "naming the day; ★ S124 — no season log exists during play, and the CAREER lock is held through the fault, " +
                      "until the store is disposed, which then frees it",
                      Says(thrown, "CONSTRUCTED ENGINE FAULT") && later.All(r => Says(r, "failed on " + day))
                      && noLog && heldWhileFaulted && disposeFault is null && heldAfterRunDisposed && freeAfterStore,
                      Inv($"fault day {day}: ") + Short(later[0]));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — THE STATE INVENTORY.
            // ════════════════════════════════════════════════════════════════════
            {
                var fault = SeasonStateInventoryFault(SeasonStateInventory, SeasonStateTypes);
                Check("C3a: ★ the frozen inventory names every field of the season run, the walk and the five game sources — by " +
                      "name and type, with a category, in both directions",
                      fault is null,
                      fault ?? Inv($"{SeasonStateInventory.Length} fields across {SeasonStateTypes.Length} types: ") + string.Join(", ",
                          Enum.GetValues<SeasonStateCategory>().Select(c => Inv($"{c} {SeasonStateInventory.Count(e => e.Category == c)}"))));
                var extra = SeasonStateInventoryFault(
                    new[] { new SeasonStateEntry("SeasonStateControlType", "_alpha", "int", SeasonStateCategory.Mutable) },
                    new[] { typeof(SeasonStateControlType) });
                Check("C3b: NEGATIVE CONTROL — a type with one field the list leaves out fails, naming it",
                      extra is not null && extra.Contains("SeasonStateControlType._beta", StringComparison.Ordinal), extra ?? "NO FAULT");
                var dropped = SeasonStateInventory.Where(e => !(e.Type == "SeasonRun" && e.Field == "teamGames")).ToList();
                var missing = SeasonStateInventoryFault(dropped, SeasonStateTypes);
                Check("C3c: NEGATIVE CONTROL — the real list with one entry missing fails, naming the field",
                      missing is not null && missing.Contains("SeasonRun.teamGames", StringComparison.Ordinal), missing ?? "NO FAULT");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — NOTHING ON DISK MOVED.
            // ════════════════════════════════════════════════════════════════════
            {
                //  The S121 pins, on the career played in random chunks both seasons. Season one's pins were
                //  read after season one (its log and event record are untouched by season two).
                var digestOne = CalendarBlocksDigest(ReadLog(Career("s1-Random"), 1));
                var events = Sha(File.ReadAllBytes(Path.Combine(Folder("s1-Random"), "career.events", "season-1.json")));
                Check("C4a: ★ season one, played in random chunks: every game block is the S121 build's (decoded; captured " +
                      "from the S121 build), and its event record byte for byte",
                      digestOne == CalendarGoldenSeasonOneBlocks && events == CalendarGoldenSeasonOneEvents,
                      Inv($"blocks {digestOne[..16]}, events {events[..16]}"));
                var careerSha = PortableCareerDigest(File.ReadAllBytes(Career("s1-Random")));
                Check("C4b: ★ after season two, also in random chunks: the career file is the S125 capture (Phase 113's portable digest)",
                      careerSha == CalendarGoldenCareerFile, careerSha[..16]);
                Check("C4c: ★ the legacy stock season played day by day is the S120 capture",
                      GamesDigest(legacyDay.Outcome) == RatingGoldenPreS112GameDigest, GamesDigest(legacyDay.Outcome)[..16]);

                //  The stage boundaries. ★ S124 (B1a) — a season writes nothing until it closes.
                var beforeDiff = beforeSeason is null || afterBegin is null ? "NO SNAPSHOT" : SeasonSnapshotDiff(beforeSeason, afterBegin);
                Check("C4d: ★ S124 — after the set-up: the career folder is exactly what it was before the season (the career " +
                      "file byte-identical, its lock sidecar and ★ S125 the league-0 it was born with, nothing else), and the career lock is held",
                      beforeDiff is null && afterBegin!.Keys.SequenceEqual(new[] { "career.json", "career.json.lock", "career.league/league-0.json" }) && lockHeldAtBegin,
                      beforeDiff ?? Inv($"[{string.Join(", ", afterBegin!.Keys)}]"));
                //  (C4e, the unfinished log's prefix relation, is retired at S124: there is no unfinished log.)
                Check("C4f: ★ after every advance (random chunks): still exactly the folder from before the season, and the " +
                      "career lock held",
                      advanceFaults.Count == 0 && advancesWatched > 1,
                      advanceFaults.Count > 0 ? string.Join("; ", advanceFaults.Take(3)) : Inv($"{advancesWatched} advances watched"));
                //  After the close (read before the store is disposed): the one-call season's folder as season one left it.
                var finishFault = s1FinishSnap is null ? "NO SNAPSHOT" : SeasonSnapshotDiff(s1OneCallSnap, s1FinishSnap);
                var careerSeen = s1FinishCareer is null ? "ABSENT" : PortableCareerDigest(s1FinishCareer);
                var careerWritten = careerSeen == SeasonRunSeasonOneCareer;
                Check("C4g: ★ after the close: the file list and every file the one-call season's byte for byte; the career file " +
                      "is the S125 capture (★ v4: every file season one wrote listed by hash, and its backup beside it); the season log's lock released by the close and the career " +
                      "lock still held (it is the store's, released when the store is disposed)",
                      finishFault is null && careerWritten && s1LockFreeAfterFinish && s1CareerLockHeldAfterFinish
                      && s1FinishSnap!.ContainsKey("career.gamelog/season-1.log") && !s1FinishSnap.ContainsKey("career.gamelog/season-1.log.inprogress")
                      && s1FinishSnap.ContainsKey("career.json.bak") && s1FinishSnap.ContainsKey("career.league/league-1.json"),
                      finishFault ?? (careerWritten ? Inv($"{s1FinishSnap!.Count} files") : "career file " + careerSeen));

                //  NEGATIVE CONTROL, on a copy of the folder taken mid-season (the live folder is never written).
                var copyCareer = Path.Combine(copyControl ?? scratch, "career.json");
                string? controlDiff = null;
                if (copyControl is not null)
                {
                    var before = SeasonSnapshotDiff(beforeSeason!, SeasonFolderSnapshot(copyControl));
                    using (var fs = new FileStream(copyCareer, FileMode.Append, FileAccess.Write)) fs.WriteByte(0x2A);
                    controlDiff = SeasonSnapshotDiff(beforeSeason!, SeasonFolderSnapshot(copyControl));
                    if (before is not null) controlDiff = "the copy differed BEFORE the byte: " + before;
                }
                Check("C4h: NEGATIVE CONTROL — a copy of the live folder with one byte appended to the career file fails the " +
                      "comparison, naming that file",
                      controlDiff is not null && controlDiff == "career.json differs", controlDiff ?? "NO DIFFERENCE");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — PAGE-ONLY: what chunking costs. Never asserted.
            // ════════════════════════════════════════════════════════════════════
            foreach (var t in timings) Console.WriteLine("  [page] " + t);
        }
        catch (Exception ex)
        {
            Check("Phase 114 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 114 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }
}
