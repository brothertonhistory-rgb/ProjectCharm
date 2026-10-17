using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 117 — S126: NOTHING READS WHAT THE CAREER DOES NOT LIST (save design B1c).
//
//  Every reader of an old season's file now asks the store for it and gets it only
//  when the career lists it; files the career does not list are set aside at every
//  open; the close checks that every name it will write is free. This phase proves it.
//
//    C1  ★ an unlisted file is never read — for each reader (the pages' log walk, last
//        season's people, host memory, contracts, tournament memory, the camp's scouting
//        read, the league state), a file the career does not list, planted at its
//        arithmetic name AFTER the open, valid and then corrupt: both take the reader's
//        ordinary "not there" branch, never a parse error. Control: the valid file
//        listed is read.
//    C2  quarantine at open — planted files of all five kinds (an earlier season, the
//        pending one, a later one) move into career.orphans byte-untouched, each
//        reported; unrecognised files, lock sidecars, temp files, `.new` files and a
//        directory with a recognised name stay; the invariant holds; a second open moves
//        nothing; a page's open does the same.
//    C3  a source scan — the career path builders and directory listings appear only at
//        their counted sites (the writers, the store, the close's names check); the
//        re-list helper and the sealer appear in no production file. Control: a
//        constructed reader line is named.
//    C4  the backup's protection — a verifying backup that lists more protects it, and
//        the close that needs its name refuses; a torn backup protects nothing.
//    C5  names not free; files that cannot move — the orphans folder blocked (every move
//        warned, the open succeeds); one destination blocked (that file warned, the rest
//        moved); a warned file is read by nothing; a directory at a close's output name
//        refuses the close by name before any season file is written.
//    C6  page-only: what each case set aside; the open's time clean and with fifty
//        unlisted recognised files.
//
//  All on fixture-mte, under the fixed history id, with the suite's season seeds.
//  No new pin: the no-change proof is Phase 113 C2, Phase 116 C1 and the wall.
// ============================================================================

internal static partial class Program
{
    private static string Inv117(FormattableString f) => FormattableString.Invariant(f);

    /// <summary>★ C3's table — every place a career path builder or a directory listing may appear in a
    /// production file, with its count (line comments ignored). Every site is a writer, the store, or
    /// the close's names check; none is a reader. A new site anywhere — a reader building a path — moves
    /// a count and names the file.</summary>
    private static readonly (string File, string Needle, int Count)[] ReadBoundarySites =
    {
        ("Program.Season.Events.cs", "FinalPathFor(", 1),              // the names check
        ("Program.Season.Events.cs", "ScoutingFile.PathFor(", 1),      // the names check
        ("Program.Season.Events.cs", "MteRecordPathFor(", 3),          // its definition, the writer, the names check
        ("Program.Season.Events.cs", "MteRecordFolderFor(", 3),        // its definition, the writer (folder + temp)
        ("Program.Season.Events.cs", "LeagueStateFile.PathFor(", 1),   // the names check
        ("Program.Season.Run.cs", "FinalPathFor(", 1),                 // the close lists the log it wrote
        ("Program.Season.Run.cs", "MteRecordPathFor(", 1),             // the close lists the record it wrote
        ("GameLogWriter.cs", "FinalPathFor(", 1),                      // its definition
        ("GameLogWriter.cs", "LogFolderFor(", 3),
        ("HistoryStore.cs", "LeagueStateFile.PathFor(", 2),            // creation and the league writer
        ("HistoryStore.cs", "LeagueStateFile.FolderFor(", 1),
        ("HistoryStore.cs", "ResolveListed(", 5),                      // ★ S127 +2: the mid-season save's writer and its read door
        ("HistoryStore.cs", "SiblingFolderFor(", 5),
        ("HistoryStore.cs", "Directory.GetFiles(", 1),                 // quarantine
        ("LeagueStateFile.cs", " PathFor(", 1),
        ("LeagueStateFile.cs", "SiblingFolderFor(", 1),
        ("ScoutingFile.cs", " PathFor(", 2),                           // its definition and the writer
        ("ScoutingFile.cs", "LogFolderFor(", 1),
    };

    private static readonly string[] ReadBoundaryNeedles =
    {
        "FinalPathFor(", "ScoutingFile.PathFor(", " PathFor(", "MteRecordPathFor(", "MteRecordFolderFor(",
        "LeagueStateFile.PathFor(", "LeagueStateFile.FolderFor(", "LogFolderFor(", "ResolveListed(", "SiblingFolderFor(",
        "Directory.GetFiles(", "Directory.EnumerateFiles(", "Directory.EnumerateFileSystemEntries(",
        "RelistCareerFor" + "Check(", "SealCa" + "reer(",
    };

    /// <summary>C3's comparison: null when the scanned files hold exactly the table's sites, else the
    /// first differences, each naming the file.</summary>
    private static string? ReadBoundaryScanFault(IEnumerable<(string Name, string Text)> files)
    {
        var got = new Dictionary<(string, string), int>();
        foreach (var (name, text) in files)
        {
            var code = string.Join("\n", text.Split('\n').Select(l => { var i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));
            foreach (var n in ReadBoundaryNeedles)
            {
                var c = 0;
                for (var at = code.IndexOf(n, StringComparison.Ordinal); at >= 0; at = code.IndexOf(n, at + 1, StringComparison.Ordinal)) c++;
                if (c > 0) got[(name, n)] = c;
            }
        }
        var want = ReadBoundarySites.ToDictionary(s => (s.File, s.Needle), s => s.Count);
        var faults = new List<string>();
        foreach (var (key, c) in got)
            if (!want.TryGetValue(key, out var w) || w != c) faults.Add(Inv117($"{key.Item1}: '{key.Item2.Trim()}' ×{c} (allowed {(want.TryGetValue(key, out var a) ? a : 0)})"));
        foreach (var (key, w) in want)
            if (!got.ContainsKey(key)) faults.Add(Inv117($"{key.Item1}: '{key.Item2.Trim()}' ×0 (expected {w})"));
        return faults.Count == 0 ? null : string.Join("; ", faults.OrderBy(f => f, StringComparer.Ordinal).Take(4));
    }

    private static bool Phase117ReadBoundaryCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 117 — S126: nothing reads what the career does not list. Every reader takes its path from the " +
                          "career's list; unlisted files are set aside at every open; the close checks that every name it will write " +
                          "is free ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static Exception? Thrown(Action a)
        {
            try { a(); return null; }
            catch (Exception ex) { return ex; }
        }
        static string Capture(Func<int> run)
        {
            var old = Console.Out;
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Console.SetOut(sw);
            try { run(); } finally { Console.SetOut(old); }
            return sw.ToString();
        }
        static string Short(string? s) => s is null ? "NONE" : s[..Math.Min(160, s.Length)];
        static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s126-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            var worldPath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-mte.world.json");
            var mte = LoadWorld(worldPath);
            var fp = WorldFingerprint(mte);
            var seedOne = CalendarCheckSeed;
            var seedTwo = SeasonTwoSeed(seedOne);
            var seedThree = SeasonTwoSeed(seedTwo);
            using var fixedId = HistoryStore.UseFixedHistoryIdForTests(CalendarFixedHistoryId);

            string Dir(string name) => Path.Combine(scratch, name);
            string CareerOf(string name) => Path.Combine(scratch, name, "career.json");
            string P(string name, string rel) => Path.Combine(Dir(name), rel.Replace('/', Path.DirectorySeparatorChar));
            void CopyDir(string from, string to)
            {
                foreach (var f in Directory.GetFiles(Dir(from), "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(Dir(to), Path.GetRelativePath(Dir(from), f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest);
                }
            }
            SortedDictionary<string, string> Snap(string name) => SeasonFolderSnapshot(Dir(name));
            SortedDictionary<string, string> Orphans(string name)
            {
                var s = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var (k, v) in Snap(name))
                    if (k.StartsWith("career.orphans/", StringComparison.Ordinal)) s[k["career.orphans/".Length..]] = v;
                return s;
            }
            SortedDictionary<string, string> Outside(string name)
            {
                var s = Snap(name);
                foreach (var k in s.Keys.Where(k => k.StartsWith("career.orphans/", StringComparison.Ordinal)).ToList()) s.Remove(k);
                return s;
            }
            SeasonRunOutcome PlayInProcess(string name, long seed, Action<string>? afterOpen = null)
            {
                using var store = OpenCareer(CareerOf(name), mte);
                afterOpen?.Invoke(name);
                using var run = SeasonBegin(mte, seed, configPath, verbose: false,
                                            new SeasonRunOptions(History: store, RetainGameLog: true));
                run.AdvanceThrough(run.LastLegalDay);
                return run.Finish();
            }
            List<string> OpenReport(string name)
            {
                using var s = OpenCareer(CareerOf(name), mte);
                return s.OpenReport.ToList();
            }
            //  The career's list (and the backup's, when its trailer verifies), read off disk WITHOUT opening.
            HashSet<string>? ListIn(string file)
            {
                if (!File.Exists(file)) return null;
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length < 73) return null;
                var payload = bytes.AsSpan(0, bytes.Length - 72).ToArray();
                if (Encoding.ASCII.GetString(bytes, bytes.Length - 72, 72) != "sha256:" + Hex(payload) + "\n") return null;
                try
                {
                    return JsonNode.Parse(payload)!["files"]!.AsArray().Select(f => (string)f!["path"]!)
                                   .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception) { return null; }
            }
            static bool Recognised(string listed)
                => RelistSeasonOf(listed) is not null
                   || (listed.EndsWith(".log.inprogress", StringComparison.Ordinal)
                       && RelistSeasonOf(listed[..^".inprogress".Length]) is not null)
                   || RelistSnapshotOf(listed) is not null;   // ★ S127 — the sixth kind, a mid-season save
            //  ★ THE K2 INVARIANT: every recognised file left in the three folders is listed by a protecting
            //    manifest or named in a warning. Null when it holds.
            string? InvariantFault(string name, IEnumerable<string> report)
            {
                var protectedNames = ListIn(CareerOf(name)) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in ListIn(CareerOf(name) + ".bak") ?? new HashSet<string>()) protectedNames.Add(l);
                var warned = report.Where(l => l.StartsWith("WARNING: could not set aside '", StringComparison.Ordinal))
                                   .Select(l => l["WARNING: could not set aside '".Length..].Split('\'')[0]).ToHashSet();
                foreach (var kind in new[] { "events", "gamelog", "league", "progress" })   // ★ S127 the saves folder too
                {
                    var folder = HistoryStore.SiblingFolderFor(CareerOf(name), kind);
                    if (!Directory.Exists(folder)) continue;
                    foreach (var f in Directory.GetFiles(folder))
                    {
                        var listed = kind + "/" + Path.GetFileName(f);
                        if (!Recognised(listed) || protectedNames.Contains(listed)) continue;
                        if (!warned.Contains(Path.GetFileName(folder) + "/" + Path.GetFileName(f))) return listed + " is unlisted, unwarned and still there";
                    }
                }
                return null;
            }
            //  Move a listed file out of the career (kept for planting) and drop it from the list.
            byte[] Unlist(string name, string rel)
            {
                var bytes = File.ReadAllBytes(P(name, rel));
                File.Delete(P(name, rel));
                RelistCareerForCheck(CareerOf(name));
                return bytes;
            }
            var junk = Encoding.UTF8.GetBytes("this is not a file the game wrote — if a reader opened it, it would say so\n");

            // ════════════════════════════════════════════════════════════════════
            //  The reference: season one ("master"), season two ("two").
            // ════════════════════════════════════════════════════════════════════
            Directory.CreateDirectory(Dir("fresh"));
            using (OpenCareer(CareerOf("fresh"), mte)) { }
            CopyDir("fresh", "master");
            PlayInProcess("master", seedOne);
            CopyDir("master", "two");
            PlayInProcess("two", seedTwo);
            var masterCareer = File.ReadAllBytes(CareerOf("master"));
            var twoCareer = File.ReadAllBytes(CareerOf("two"));
            var page = new List<string>();

            // ════════════════════════════════════════════════════════════════════
            //  C1 — ★ AN UNLISTED FILE IS NEVER READ.
            // ════════════════════════════════════════════════════════════════════
            {
                //  (i) Season two's LOG unlisted: the pages' walk, last season's people, host memory.
                CopyDir("two", "c1-log");
                var log2 = "career.gamelog/season-2.log";
                var logBytes = Unlist("c1-log", log2);
                var logFaults = new List<string>();
                foreach (var (label, bytes) in new[] { ("valid", logBytes), ("corrupt", junk) })
                {
                    using var s = OpenCareer(CareerOf("c1-log"), mte);
                    File.WriteAllBytes(P("c1-log", log2), bytes);   // planted AFTER the open: quarantine has run
                    var walk = ReadCareerLogs(s);
                    if (walk.Count != 2 || walk[1].Log is not null || walk[1].Reason != "no season log was kept")
                        logFaults.Add($"pages ({label}): {walk.ElementAtOrDefault(1).Reason ?? "READ"}");
                    var people = Thrown(() => ReadCareerPeople(s, mte, 2));
                    if (people is not InvalidOperationException || !people.Message.Contains("kept no season log", StringComparison.Ordinal))
                        logFaults.Add($"people ({label}): {Short(people?.GetType().Name + " " + people?.Message)}");
                    var hosts = ReadHostMemory(s);
                    if (hosts.Status != HostMemoryStatus.NoPublishedLog)
                        logFaults.Add($"host memory ({label}): {hosts.Status}");
                    File.Delete(P("c1-log", log2));
                }
                Check("C1a: ★ season two's log NOT LISTED, planted at its name after the open — valid, then corrupt: the pages say " +
                      "\"no season log was kept\", last season's people refuse with the S115 sentence, host memory is NoPublishedLog " +
                      "(never a parse error: the reader never opened it)",
                      logFaults.Count == 0, logFaults.Count == 0 ? "3 readers × 2 plants" : string.Join("; ", logFaults));
                File.WriteAllBytes(P("c1-log", log2), logBytes);
                RelistCareerForCheck(CareerOf("c1-log"));
                using (var s = OpenCareer(CareerOf("c1-log"), mte))
                {
                    var walk = ReadCareerLogs(s);
                    var people = Thrown(() => ReadCareerPeople(s, mte, 2));
                    var hosts = ReadHostMemory(s);
                    Check("C1b: CONTROL — the same valid log LISTED: the pages read it, last season's people read, host memory loads",
                          walk.Count == 2 && walk[1].Log is not null && people is null && hosts.Status == HostMemoryStatus.Loaded,
                          Inv117($"{walk.ElementAtOrDefault(1).Reason ?? "read"}; people {people?.Message ?? "read"}; hosts {hosts.Status}"));
                }

                //  (ii) Season two's EVENT RECORD unlisted: contracts and tournament memory.
                CopyDir("two", "c1-rec");
                var rec2 = "career.events/season-2.json";
                var recBytes = Unlist("c1-rec", rec2);
                int baseline;
                using (var s = OpenCareer(CareerOf("c1-rec"), mte)) baseline = MteReadHistory(s, 3).Appearances.Values.Sum();
                var recFaults = new List<string>();
                foreach (var (label, bytes) in new[] { ("valid", recBytes), ("corrupt", junk) })
                {
                    using var s = OpenCareer(CareerOf("c1-rec"), mte);
                    File.WriteAllBytes(P("c1-rec", rec2), bytes);
                    var contracts = ReadLiveContracts(s, 3);
                    if (contracts.Status != ContractLoadStatus.NoRecord) recFaults.Add($"contracts ({label}): {contracts.Status}");
                    var memory = MteReadHistory(s, 3);
                    if (memory.Diagnostics.Any(d => d.StartsWith("season 2", StringComparison.Ordinal))
                        || memory.Appearances.Values.Sum() != baseline)
                        recFaults.Add($"tournament memory ({label}): {memory.Appearances.Values.Sum()} appearances, " +
                                      string.Join(" | ", memory.Diagnostics));
                    File.Delete(P("c1-rec", rec2));
                }
                Check("C1c: ★ season two's event record NOT LISTED, planted after the open — valid, then corrupt: contracts are " +
                      "NoRecord and tournament memory has the season-two hole exactly as if no file were there",
                      recFaults.Count == 0, recFaults.Count == 0 ? Inv117($"2 readers × 2 plants; {baseline} appearances from season one") : string.Join("; ", recFaults));
                File.WriteAllBytes(P("c1-rec", rec2), recBytes);
                RelistCareerForCheck(CareerOf("c1-rec"));
                using (var s = OpenCareer(CareerOf("c1-rec"), mte))
                {
                    var contracts = ReadLiveContracts(s, 3);
                    var withTwo = MteReadHistory(s, 3).Appearances.Values.Sum();
                    Check("C1d: CONTROL — the same record LISTED: contracts load and tournament memory gains season two",
                          contracts.Status == ContractLoadStatus.Loaded && withTwo > baseline,
                          Inv117($"contracts {contracts.Status}; appearances {baseline} → {withTwo}"));
                }

                //  (iii) Season two's SCOUTING file unlisted: the camp's read at the season's set-up (the production path).
                CopyDir("two", "c1-scout");
                var scout2 = "career.gamelog/season-2.scout";
                var scoutBytes = Unlist("c1-scout", scout2);
                var scoutFaults = new List<string>();
                foreach (var (label, bytes) in new[] { ("valid", scoutBytes), ("corrupt", junk) })
                {
                    using var s = OpenCareer(CareerOf("c1-scout"), mte);
                    File.WriteAllBytes(P("c1-scout", scout2), bytes);
                    var refused = Thrown(() => RunSeasonCore(mte, seedThree, configPath, verbose: false, s, retainGameLog: true));
                    if (refused is not ScoutingException { Error: ScoutingError.Missing })
                        scoutFaults.Add($"{label}: {Short(refused?.GetType().Name + " " + refused?.Message)}");
                    File.Delete(P("c1-scout", scout2));
                }
                Check("C1e: ★ season two's scouting file NOT LISTED, planted after the open — valid, then corrupt: season three's " +
                      "set-up refuses with Missing (never Damaged: the file was never opened)",
                      scoutFaults.Count == 0, scoutFaults.Count == 0 ? "2 plants" : string.Join("; ", scoutFaults));
                File.WriteAllBytes(P("c1-scout", scout2), scoutBytes);
                RelistCareerForCheck(CareerOf("c1-scout"));
                using (var s = OpenCareer(CareerOf("c1-scout"), mte))
                {
                    var read = Thrown(() => ScoutingFile.Read(s.ListedPath(CareerFileKind.Scouting, 2),
                                                              new ScoutingBindings(s.HistoryId, s.WorldFingerprint, 2)));
                    Check("C1f: CONTROL — the same scouting file LISTED: the read the set-up makes returns it whole",
                          read is null, read?.Message ?? "read");
                }

                //  (iv) Season two's LEAGUE STATE unlisted: the store's own read of the latest league state.
                CopyDir("two", "c1-league");
                var league2 = "career.league/league-2.json";
                var leagueBytes = Unlist("c1-league", league2);
                var leagueFaults = new List<string>();
                foreach (var (label, bytes) in new[] { ("valid", leagueBytes), ("corrupt", junk) })
                {
                    using var s = OpenCareer(CareerOf("c1-league"), mte);
                    File.WriteAllBytes(P("c1-league", league2), bytes);
                    long? season = null;
                    var err = Thrown(() => season = s.ReadLatestLeagueState().Season);
                    if (err is not null || season != 1) leagueFaults.Add($"{label}: {err?.Message ?? Inv117($"season {season}")}");
                    File.Delete(P("c1-league", league2));
                }
                Check("C1g: ★ season two's league state NOT LISTED, planted after the open — valid, then corrupt: the latest league " +
                      "state read is season one's, the listed one",
                      leagueFaults.Count == 0, leagueFaults.Count == 0 ? "2 plants" : string.Join("; ", leagueFaults));
                File.WriteAllBytes(P("c1-league", league2), leagueBytes);
                RelistCareerForCheck(CareerOf("c1-league"));
                using (var s = OpenCareer(CareerOf("c1-league"), mte))
                {
                    long? season = null;
                    var err = Thrown(() => season = s.ReadLatestLeagueState().Season);
                    Check("C1h: CONTROL — the same league state LISTED: it is the latest read", err is null && season == 2,
                          err?.Message ?? Inv117($"season {season}"));
                }
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — QUARANTINE AT OPEN.
            // ════════════════════════════════════════════════════════════════════
            var recognisedPlants = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["career.gamelog/season-1.log.inprogress"] = "an earlier season's unfinished log"u8.ToArray(),
                ["career.events/season-3.json"] = "{ \"planted\": \"the pending season's record\" }"u8.ToArray(),
                ["career.gamelog/season-3.log"] = "the pending season's log"u8.ToArray(),
                ["career.gamelog/season-3.scout"] = "the pending season's scouting file"u8.ToArray(),
                ["career.league/league-5.json"] = "a league state two seasons past"u8.ToArray(),
            };
            var otherPlants = new[]
            {
                "career.events/notes.txt", "career.gamelog/season-3.log.lock", "career.events/.charm-events-0123abcd.tmp",
                "career.gamelog/season-3.log.new", "career.json.new", "career.gamelog/season-4.log/inside.txt",
            };
            void PlantC2(string name)
            {
                foreach (var (rel, bytes) in recognisedPlants) File.WriteAllBytes(P(name, rel), bytes);
                foreach (var rel in otherPlants)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(P(name, rel))!);
                    File.WriteAllText(P(name, rel), "left where it is: " + rel);
                }
            }
            {
                CopyDir("two", "c2");
                PlantC2("c2");
                var before = Snap("c2");
                var report = OpenReport("c2");
                var want = recognisedPlants.Keys
                    .OrderBy(k => Array.IndexOf(new[] { "career.events", "career.gamelog", "career.league" }, k.Split('/')[0]))
                    .ThenBy(k => k, StringComparer.Ordinal)
                    .Select(k => $"Set aside a file this career does not list: 'career.orphans/{k}'.").ToList();
                var orphans = Orphans("c2");
                var movedOk = orphans.Keys.SequenceEqual(recognisedPlants.Keys)
                              && recognisedPlants.All(kv => orphans[kv.Key] == Hex(kv.Value));
                var stayed = new SortedDictionary<string, string>(before, StringComparer.Ordinal);
                foreach (var k in recognisedPlants.Keys) stayed.Remove(k);
                var outsideFault = SeasonSnapshotDiff(stayed, Outside("c2"));
                Check("C2a: ★ the open moves exactly the five planted files of the five recognised kinds — an earlier season's, the " +
                      "pending season's and a later one's — each into career.orphans under its own path, byte-untouched, one report line " +
                      "each; every listed file, the unrecognised file, the lock sidecar, the temp file, both `.new` files and the " +
                      "directory with a recognised name stay exactly as they were",
                      report.SequenceEqual(want) && movedOk && outsideFault is null
                      && Directory.Exists(P("c2", "career.gamelog/season-4.log")),
                      outsideFault ?? (movedOk ? string.Join(" | ", report) : "[" + string.Join(", ", orphans.Keys) + "]"));
                var inv = InvariantFault("c2", report);
                var afterFirst = Snap("c2");
                var second = OpenReport("c2");
                Check("C2b: the K2 invariant holds after the open; a second open moves nothing and reports nothing",
                      inv is null && second.Count == 0 && SeasonSnapshotDiff(afterFirst, Snap("c2")) is null,
                      inv ?? (second.Count > 0 ? string.Join(" | ", second) : "nothing moved"));
                page.Add("C2 set aside: " + string.Join(", ", orphans.Keys));

                CopyDir("two", "c2-page");
                PlantC2("c2-page");
                var people = Capture(() => RunPeople(new[] { "people", worldPath, "--history", CareerOf("c2-page") }));
                var pageOrphans = Orphans("c2-page");
                Check("C2c: a page's open does the same — the `people` page sets the same five files aside, prints each line, and " +
                      "still prints its page",
                      pageOrphans.SequenceEqual(orphans) && want.All(l => people.Contains(l, StringComparison.Ordinal))
                      && !people.Contains("ERROR", StringComparison.Ordinal),
                      "[" + string.Join(", ", pageOrphans.Keys) + "]");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — NO READER BUILDS A CAREER PATH.
            // ════════════════════════════════════════════════════════════════════
            if (!TryFindRepoRoot(out var root))
            {
                Check("C3 source root located (walked up from the binary)", false, "no repo root above " + AppContext.BaseDirectory);
            }
            else
            {
                var production = Directory.GetFiles(Path.Combine(root!, "src", "Charm.Harness"), "*.cs")
                    .Where(f => !Path.GetFileName(f).StartsWith("Program.Checks.", StringComparison.Ordinal))
                    .Concat(Directory.GetFiles(Path.Combine(root!, "src", "Charm.History"), "*.cs"))
                    .Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToList();
                var fault = ReadBoundaryScanFault(production);
                Check("C3a: ★ in the production harness and the history library, the career path builders and directory listings " +
                      "appear only at their counted sites (the writers, the store, the close's names check), and the re-list helper " +
                      "and the career sealer appear nowhere",
                      fault is null, fault ?? Inv117($"{production.Count} files, {ReadBoundarySites.Length} sites"));
                var planted = production.Append(("Program.Season.Fake.cs",
                    "        var path = GameLogWriter." + "FinalPathFor(history.Path, season);\n")).ToList();
                var caught = ReadBoundaryScanFault(planted);
                var relisted = production.Append(("Program.Season.Fake.cs", "        Relist" + "CareerForCheck(history.Path);\n")).ToList();
                var caughtRelist = ReadBoundaryScanFault(relisted);
                Check("C3b: NEGATIVE CONTROL — a constructed production file with a reader's path line is named, and so is one calling " +
                      "the re-list helper",
                      caught is not null && caught.Contains("Program.Season.Fake.cs", StringComparison.Ordinal)
                      && caughtRelist is not null && caughtRelist.Contains("Program.Season.Fake.cs", StringComparison.Ordinal),
                      Short(caught) + " / " + Short(caughtRelist));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — THE BACKUP'S PROTECTION.
            // ════════════════════════════════════════════════════════════════════
            {
                //  (a) A hand-made state: the career file at season one, a VERIFYING backup listing season two too.
                CopyDir("two", "c4a");
                File.WriteAllBytes(CareerOf("c4a"), masterCareer);
                File.WriteAllBytes(CareerOf("c4a") + ".bak", twoCareer);
                var before = Snap("c4a");
                var report = OpenReport("c4a");
                var inv = InvariantFault("c4a", report);
                var keptAll = SeasonSnapshotDiff(before, Snap("c4a")) is null;
                var refused = Thrown(() => PlayInProcess("c4a", seedTwo));
                Check("C4a: ★ a verifying backup that lists season two's files protects them: the open moves nothing; the season-two " +
                      "close that needs those names refuses BY NAME, nothing published",
                      report.Count == 0 && keptAll && inv is null
                      && refused is InvalidOperationException && refused.Message.StartsWith("SEASON CLOSE REFUSED", StringComparison.Ordinal)
                      && refused.Message.Contains(Path.Combine("career.gamelog", "season-2.log"), StringComparison.Ordinal)
                      && File.ReadAllBytes(CareerOf("c4a")).AsSpan().SequenceEqual(masterCareer),
                      inv ?? Short(refused?.Message));

                //  (b) A torn backup beside a valid career file at season one.
                CopyDir("two", "c4b");
                File.WriteAllBytes(CareerOf("c4b"), masterCareer);
                File.WriteAllBytes(CareerOf("c4b") + ".bak", twoCareer[..(twoCareer.Length / 2)]);
                var seasonTwo = new[]
                {
                    "career.events/season-2.json", "career.gamelog/season-2.log", "career.gamelog/season-2.scout",
                    "career.league/league-2.json",
                };
                var twoHashes = seasonTwo.ToDictionary(r => r, r => Hex(File.ReadAllBytes(P("c4b", r))));
                var listedBefore = ListIn(CareerOf("c4b"))!;
                var outsideBefore = Snap("c4b");
                var tornReport = OpenReport("c4b");
                var tornOrphans = Orphans("c4b");
                var listedStay = listedBefore.All(l => File.Exists(HistoryStore.ResolveListed(CareerOf("c4b"), l))
                                                       && outsideBefore[Path.GetRelativePath(Dir("c4b"), HistoryStore.ResolveListed(CareerOf("c4b"), l)).Replace('\\', '/')]
                                                          == Hex(File.ReadAllBytes(HistoryStore.ResolveListed(CareerOf("c4b"), l))));
                Check("C4b: ★ a TORN backup protects nothing: every file the career file lists stays, byte-identical; season two's four " +
                      "files, listed only by the torn backup, are set aside byte-untouched (moved, never deleted); the invariant holds",
                      listedStay && tornOrphans.Keys.SequenceEqual(seasonTwo.OrderBy(x => x, StringComparer.Ordinal))
                      && seasonTwo.All(r => tornOrphans[r] == twoHashes[r]) && InvariantFault("c4b", tornReport) is null,
                      "[" + string.Join(", ", tornOrphans.Keys) + "]");
                page.Add("C4b set aside: " + string.Join(", ", tornOrphans.Keys));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — NAMES NOT FREE; FILES THAT CANNOT MOVE.
            // ════════════════════════════════════════════════════════════════════
            {
                //  (a) The orphans folder blocked by a regular file: nothing can move.
                CopyDir("two", "c5a");
                var rec2 = "career.events/season-2.json";
                var recBytes = Unlist("c5a", rec2);
                File.WriteAllBytes(P("c5a", rec2), recBytes);                    // valid, unlisted: what a reader would want
                File.WriteAllText(P("c5a", "career.gamelog/season-3.log"), "the pending season's log");
                File.WriteAllText(HistoryStore.SiblingFolderFor(CareerOf("c5a"), "orphans"), "a file standing where the orphans folder goes");
                List<string> report;
                ContractLoad contracts;
                MteHistory memory;
                using (var s = OpenCareer(CareerOf("c5a"), mte))
                {
                    report = s.OpenReport.ToList();
                    contracts = ReadLiveContracts(s, 3);
                    memory = MteReadHistory(s, 3);
                }
                var warnings = report.Where(l => l.StartsWith("WARNING: could not set aside '", StringComparison.Ordinal)).ToList();
                Check("C5a: the orphans folder blocked by a regular file: the open SUCCEEDS and warns once per file, naming each; " +
                      "both files stay where they are; the invariant holds",
                      report.Count == 2 && warnings.Count == 2
                      && warnings[0].Contains("'career.events/season-2.json'", StringComparison.Ordinal)
                      && warnings[1].Contains("'career.gamelog/season-3.log'", StringComparison.Ordinal)
                      && File.Exists(P("c5a", rec2)) && File.Exists(P("c5a", "career.gamelog/season-3.log"))
                      && InvariantFault("c5a", report) is null,
                      string.Join(" | ", report.Select(l => Short(l))));
                Check("C5b: ★ a file left behind by a warning is read by nothing — season two's record, valid and at its name: " +
                      "contracts NoRecord, tournament memory without season two",
                      contracts.Status == ContractLoadStatus.NoRecord
                      && !memory.Diagnostics.Any(d => d.StartsWith("season 2", StringComparison.Ordinal))
                      && memory.Appearances.Values.Sum() > 0,
                      Inv117($"contracts {contracts.Status}; {memory.Appearances.Values.Sum()} appearances"));

                //  (a′) ONE destination blocked — a regular file where the events orphans folder goes — so only the
                //       events file cannot move; the log-folder files after it still move.
                CopyDir("two", "c5a2");
                File.WriteAllText(P("c5a2", "career.events/season-3.json"), "the pending season's record");
                File.WriteAllText(P("c5a2", "career.gamelog/season-3.log"), "the pending season's log");
                File.WriteAllText(P("c5a2", "career.gamelog/season-3.scout"), "the pending season's scouting file");
                Directory.CreateDirectory(HistoryStore.SiblingFolderFor(CareerOf("c5a2"), "orphans"));
                File.WriteAllText(P("c5a2", "career.orphans/career.events"), "a file standing where one orphans folder goes");
                var oneReport = OpenReport("c5a2");
                Check("C5c: ONE file that cannot move (its orphans folder blocked) stays with a warning, and the files after it " +
                      "still move — one attempt each, no retry; the invariant holds",
                      oneReport.Count == 3
                      && oneReport[0].StartsWith("WARNING: could not set aside 'career.events/season-3.json'", StringComparison.Ordinal)
                      && oneReport[1] == "Set aside a file this career does not list: 'career.orphans/career.gamelog/season-3.log'."
                      && oneReport[2] == "Set aside a file this career does not list: 'career.orphans/career.gamelog/season-3.scout'."
                      && File.Exists(P("c5a2", "career.events/season-3.json"))
                      && InvariantFault("c5a2", oneReport) is null,
                      string.Join(" | ", oneReport.Select(l => Short(l))));

                //  (c) A DIRECTORY at one of the close's output names, made after the open.
                CopyDir("master", "c5c");
                var c5cBefore = File.ReadAllBytes(CareerOf("c5c"));
                var blockedRecord = P("c5c", "career.events/season-2.json");
                var refused = Thrown(() => PlayInProcess("c5c", seedTwo, _ => Directory.CreateDirectory(blockedRecord)));
                var nothingWritten = !File.Exists(P("c5c", "career.gamelog/season-2.log"))
                                     && !File.Exists(P("c5c", "career.gamelog/season-2.log.inprogress"))
                                     && !File.Exists(P("c5c", "career.gamelog/season-2.scout"))
                                     && !File.Exists(P("c5c", "career.league/league-2.json"));
                Check("C5d: ★ a DIRECTORY standing at the season's record name: the close refuses BY NAME at its first step, " +
                      "before any season file is written; the career file byte-identical",
                      refused is InvalidOperationException && refused.Message.StartsWith("SEASON CLOSE REFUSED", StringComparison.Ordinal)
                      && refused.Message.Contains(blockedRecord, StringComparison.Ordinal)
                      && nothingWritten && File.ReadAllBytes(CareerOf("c5c")).AsSpan().SequenceEqual(c5cBefore),
                      Short(refused?.Message));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — PAGE-ONLY.
            // ════════════════════════════════════════════════════════════════════
            {
                double TimeOpen(string name)
                {
                    var sw = Stopwatch.StartNew();
                    using (OpenCareer(CareerOf(name), mte)) { }
                    return sw.Elapsed.TotalMilliseconds;
                }
                CopyDir("two", "c6-clean");
                TimeOpen("c6-clean");
                var clean = TimeOpen("c6-clean");
                CopyDir("two", "c6-fifty");
                for (var n = 10; n < 60; n++)
                    File.WriteAllText(P("c6-fifty", Inv117($"career.events/season-{n}.json")), "unlisted");
                var fifty = TimeOpen("c6-fifty");
                var again = TimeOpen("c6-fifty");
                foreach (var line in page) Console.WriteLine("  [page] " + line);
                Console.WriteLine(Inv117($"  [page] the open: {clean:F1} ms on a clean two-season career; {fifty:F1} ms setting aside fifty unlisted files ({Orphans("c6-fifty").Count} moved); {again:F1} ms reopened"));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 117 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 117 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }
}
