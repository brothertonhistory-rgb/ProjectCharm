using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 115 — S124: A SEASON LEAVES NO TRACE UNTIL IT CLOSES (save design B1a).
//
//  A career season now writes nothing before its close, and the close writes the
//  career file LAST. This phase proves what that buys: a season stopped anywhere —
//  mid-season or at any step of its close — leaves the career file byte-identical,
//  and the next run (a FRESH PROCESS, the `season` command) plays it again byte for
//  byte, setting aside whatever the unfinished close had written.
//
//    C1  nothing before the close — after the set-up, after every advance and after a
//        run disposed without finishing, the career folder is what it was before the
//        season; the career lock is held through play and freed with the store; a store
//        whose close failed cannot host another season.
//    C2  ★ an interrupted season leaves the career untouched and plays again
//        identically in a fresh process — mid-season, and at each close step (log,
//        log-open, scouting, record, counters): every file outside career.orphans
//        equals an uninterrupted career's, and career.orphans holds exactly what the
//        unfinished close wrote. An interruption just AFTER the publish is a committed
//        season: nothing is replayed and nothing set aside, then or the season after.
//        A first-season case on each side of the play (mid-season, counters). The
//        child's verdict is explicit (the `season` command exits 0 even when it fails),
//        with a negative control.
//    C3  a number is never published twice — game numbers once each across the career;
//        a man kept across seasons is the same man; the reference's numbers exactly.
//    C4  the set-aside — the recognised kinds (★ S125: five) of this season or later, moved
//        byte-untouched; earlier seasons and unrecognised files never touched; a blocked
//        orphans folder refuses the close by name with nothing published.
//    C5  the close's order, observed step by step, with a negative control.
//    C6  page-only: the fresh-process re-run times and what each set aside.
//
//  All on fixture-mte, under the fixed history id, with the suite's season seeds.
// ============================================================================

internal static partial class Program
{
    /// <summary>The recognised season-N files of one season, by career-relative path ('/'), each
    /// with its SHA-256. Read allowing writers (a log writer may hold the `.inprogress` open).</summary>
    private static SortedDictionary<string, string> SeasonCloseSeasonFiles(string careerDir, long season)
    {
        var n = season.ToString(CultureInfo.InvariantCulture);
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in new[]
                 {
                     "career.events/season-" + n + ".json",
                     "career.gamelog/season-" + n + ".log",
                     "career.gamelog/season-" + n + ".log.inprogress",
                     "career.gamelog/season-" + n + ".scout",
                     "career.league/league-" + n + ".json",          // ★ S125
                 })
        {
            var path = Path.Combine(careerDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                found[rel] = Convert.ToHexString(SHA256.HashData(SeasonReadShared(path))).ToLowerInvariant();
        }
        return found;
    }

    /// <summary>C5's comparison: the season files on disk at a close step are exactly the earlier
    /// steps', and the career file is still the pre-season bytes. Null when it holds.</summary>
    private static string? SeasonCloseOrderFault(
        string step, IReadOnlyCollection<string> files, byte[] career, byte[] preSeasonCareer, long season)
    {
        var n = season.ToString(CultureInfo.InvariantCulture);
        string Log() => "career.gamelog/season-" + n + ".log";
        string Prog() => "career.gamelog/season-" + n + ".log.inprogress";
        string Scout() => "career.gamelog/season-" + n + ".scout";
        string Rec() => "career.events/season-" + n + ".json";
        string League() => "career.league/league-" + n + ".json";
        string[]? expected = step switch
        {
            "set-aside" or "log" => Array.Empty<string>(),
            "log-open" => new[] { Prog() },
            "scouting" => new[] { Log() },
            "record" => new[] { Log(), Scout() },
            "league" => new[] { Rec(), Log(), Scout() },                // ★ S125
            "counters" => new[] { Rec(), Log(), Scout(), League() },
            _ => null,
        };
        if (expected is null) return $"{step}: not a close step this comparison knows";
        var want = expected.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var got = files.OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (!want.SequenceEqual(got))
            return $"{step}: season files [{string.Join(", ", got)}], expected [{string.Join(", ", want)}]";
        if (!career.AsSpan().SequenceEqual(preSeasonCareer))
            return $"{step}: career.json differs from its pre-season bytes";
        return null;
    }

    /// <summary>A finished child run's verdict — null when it is complete. The `season` command prints
    /// SEASON ERROR and still exits 0, so the exit code alone proves nothing.</summary>
    private static string? SeasonCloseChildVerdict(
        int exitCode, string output, string careerPath, long season, HistoryCounters expected)
    {
        if (exitCode != 0) return $"exit code {exitCode.ToString(CultureInfo.InvariantCulture)}";
        if (output.Contains("SEASON ERROR", StringComparison.Ordinal))
        {
            var line = output.Split('\n').First(l => l.Contains("SEASON ERROR", StringComparison.Ordinal)).Trim();
            return "printed SEASON ERROR: " + line[..Math.Min(160, line.Length)];
        }
        var got = PeekState(careerPath);
        if (got != expected)
            return Inv115($"counters {got.NextPersonId}/{got.NextSeasonId}/{got.NextGameId}, expected {expected.NextPersonId}/{expected.NextSeasonId}/{expected.NextGameId}");
        var dir = Path.GetDirectoryName(careerPath)!;
        var files = SeasonCloseSeasonFiles(dir, season);
        var n = season.ToString(CultureInfo.InvariantCulture);
        foreach (var rel in new[] { "career.events/season-" + n + ".json", "career.gamelog/season-" + n + ".log",
                                    "career.gamelog/season-" + n + ".scout", "career.league/league-" + n + ".json" })
            if (!files.ContainsKey(rel)) return "missing " + rel;
        return null;
    }

    private static string Inv115(FormattableString f) => FormattableString.Invariant(f);

    private static bool Phase115SeasonCloseCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 115 — S124: a season leaves no trace until it closes. Nothing is written before the close and " +
                          "the career file is written last; a season interrupted anywhere leaves the career untouched and plays again " +
                          "in a fresh process byte for byte, its unfinished files set aside; no number is published twice ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static string? Refusal(Action a)
        {
            try { a(); return null; }
            catch (Exception ex) { return ex.Message; }
        }
        static string Short(string? s) => s is null ? "NO REFUSAL" : s[..Math.Min(150, s.Length)];
        var timings = new List<string>();

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s124-" + Guid.NewGuid().ToString("N"));
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
            void CopyDir(string from, string to)
            {
                foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(to, Path.GetRelativePath(from, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest);
                }
            }
            //  The whole folder, minus career.orphans (and anything else named): lock sidecars by existence.
            SortedDictionary<string, string> Outside(string name, params string[] alsoSkip)
            {
                var snap = SeasonFolderSnapshot(Dir(name));
                foreach (var k in snap.Keys.Where(k => k.StartsWith("career.orphans/", StringComparison.Ordinal)
                                                       || alsoSkip.Contains(k)).ToList())
                    snap.Remove(k);
                return snap;
            }
            SortedDictionary<string, string> Orphans(string name)
            {
                var root = Path.Combine(Dir(name), "career.orphans");
                var snap = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (!Directory.Exists(root)) return snap;
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    snap[Path.GetRelativePath(root, f).Replace('\\', '/')] =
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant();
                return snap;
            }
            SeasonRunOptions Opts(HistoryStore store, Action<string>? step = null)
                => new(History: store, RetainGameLog: true, CloseStepForTest: step);
            void PlayInProcess(string name, long seed, Action<string>? step = null)
            {
                using var store = OpenCareer(CareerOf(name), mte);
                using var run = SeasonBegin(mte, seed, configPath, verbose: false, Opts(store, step));
                run.AdvanceThrough(run.LastLegalDay);
                run.Finish();
            }
            //  A season interrupted IN THIS PROCESS: mid-season (half the days played, then disposed), or by a
            //  throw from the close's test seam at the named step. Returns the interruption's own message.
            string? Interrupt(string name, long seed, string? atStep)
            {
                using var store = OpenCareer(CareerOf(name), mte);
                using var run = SeasonBegin(mte, seed, configPath, verbose: false, Opts(store,
                    atStep is null ? null : s => { if (s == atStep) throw new InvalidOperationException("CONSTRUCTED INTERRUPTION at " + s); }));
                if (atStep is null)
                {
                    run.AdvanceThrough(run.FirstLegalDay.AddDays((run.LastLegalDay.DayNumber - run.FirstLegalDay.DayNumber) / 2));
                    return "mid-season";
                }
                run.AdvanceThrough(run.LastLegalDay);
                return Refusal(() => run.Finish());
            }
            //  The `season` command in a FRESH PROCESS, bound to the career.
            (int Exit, string Output, double Seconds) Child(string name, long seed)
            {
                var sw = Stopwatch.StartNew();
                var host = Environment.ProcessPath ?? throw new InvalidOperationException("no process path to launch a child");
                var psi = new ProcessStartInfo(host)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
                    psi.ArgumentList.Add(typeof(Program).Assembly.Location);
                foreach (var a in new[] { "season", worldPath, seed.ToString(CultureInfo.InvariantCulture), "--history", CareerOf(name) })
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(15 * 60 * 1000))
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    return (-1, "TIMED OUT", sw.Elapsed.TotalSeconds);
                }
                var output = outTask.Result + errTask.Result;
                return (p.ExitCode, output, sw.Elapsed.TotalSeconds);
            }

            // ════════════════════════════════════════════════════════════════════
            //  The reference: one career, season one, then season two with the close OBSERVED (C5).
            // ════════════════════════════════════════════════════════════════════
            Directory.CreateDirectory(Dir("fresh"));
            using (OpenCareer(CareerOf("fresh"), mte)) { }       // a new career, as Open creates it
            CopyDir(Dir("fresh"), Dir("master"));
            PlayInProcess("master", seedOne);
            var masterCareer = File.ReadAllBytes(CareerOf("master"));
            var afterOne = PeekState(CareerOf("master"));

            CopyDir(Dir("master"), Dir("ref"));
            var observed = new List<(string Step, List<string> Files, byte[] Career)>();
            PlayInProcess("ref", seedTwo, step =>
                observed.Add((step, SeasonCloseSeasonFiles(Dir("ref"), 2).Keys.ToList(), File.ReadAllBytes(CareerOf("ref")))));
            var refSnap = Outside("ref");
            var afterTwo = PeekState(CareerOf("ref"));

            // ════════════════════════════════════════════════════════════════════
            //  C1 — NOTHING BEFORE THE CLOSE (season two, on a copy of the season-one career).
            // ════════════════════════════════════════════════════════════════════
            CopyDir(Dir("master"), Dir("mid"));
            {
                var lockPath = CareerOf("mid") + ".lock";
                var faults = new List<string>();
                var watched = 0;
                bool heldDuringPlay = true;
                SortedDictionary<string, string> before, afterBegin, afterDispose;
                var store = OpenCareer(CareerOf("mid"), mte);
                try
                {
                    before = SeasonFolderSnapshot(Dir("mid"));
                    var run = SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store));
                    afterBegin = SeasonFolderSnapshot(Dir("mid"));
                    heldDuringPlay &= SeasonLockHeld(lockPath);
                    var stop = run.FirstLegalDay.AddDays((run.LastLegalDay.DayNumber - run.FirstLegalDay.DayNumber) / 2);
                    while (run.NextDay <= stop)
                    {
                        var to = run.NextDay.AddDays(6) > stop ? stop : run.NextDay.AddDays(6);
                        run.AdvanceThrough(to);
                        watched++;
                        var d = SeasonSnapshotDiff(before, SeasonFolderSnapshot(Dir("mid")));
                        if (d is not null) faults.Add(Inv115($"after {to:yyyy-MM-dd}: {d}"));
                        heldDuringPlay &= SeasonLockHeld(lockPath);
                    }
                    run.Dispose();
                    afterDispose = SeasonFolderSnapshot(Dir("mid"));
                    heldDuringPlay &= SeasonLockHeld(lockPath);
                }
                finally { store.Dispose(); }
                var freeAfterStore = !SeasonLockHeld(lockPath);
                Check("C1a: ★ after the set-up, after every advance, and after a run disposed without finishing: the career " +
                      "folder is exactly what it was before the season — the career file byte-identical, nothing added",
                      SeasonSnapshotDiff(before, afterBegin) is null && faults.Count == 0 && watched > 1
                      && SeasonSnapshotDiff(before, afterDispose) is null
                      && File.ReadAllBytes(CareerOf("mid")).AsSpan().SequenceEqual(masterCareer),
                      SeasonSnapshotDiff(before, afterBegin) ?? (faults.Count > 0 ? string.Join("; ", faults.Take(2)) : null)
                      ?? SeasonSnapshotDiff(before, afterDispose) ?? Inv115($"{watched} advances watched, {before.Count} files"));
                Check("C1b: ★ the career lock is held through play (a competing open fails) and free once the store is disposed",
                      heldDuringPlay && freeAfterStore, Inv115($"held {heldDuringPlay}, free after {freeAfterStore}"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — THE INTERRUPTIONS, each re-run in a fresh process.
            // ════════════════════════════════════════════════════════════════════
            var cases = new (string Name, string? Step, string[] SetAside)[]
            {
                ("mid", null, Array.Empty<string>()),
                ("at-log", "log", Array.Empty<string>()),
                ("at-log-open", "log-open", new[] { "career.gamelog/season-2.log.inprogress" }),
                ("at-scouting", "scouting", new[] { "career.gamelog/season-2.log" }),
                ("at-record", "record", new[] { "career.gamelog/season-2.log", "career.gamelog/season-2.scout" }),
                ("at-league", "league", new[] { "career.events/season-2.json", "career.gamelog/season-2.log", "career.gamelog/season-2.scout" }),   // ★ S125
                ("at-counters", "counters", new[] { "career.events/season-2.json", "career.gamelog/season-2.log", "career.gamelog/season-2.scout",
                                                    "career.league/league-2.json" }),
            };
            var rerunCareers = new List<(string Name, long Seasons)>();
            foreach (var (name, step, setAside) in cases)
            {
                if (name != "mid") CopyDir(Dir("master"), Dir(name));
                var why = name == "mid" ? "mid-season" : Interrupt(name, seedTwo, step);
                var untouched = File.ReadAllBytes(CareerOf(name)).AsSpan().SequenceEqual(masterCareer);
                var leftBehind = SeasonCloseSeasonFiles(Dir(name), 2);
                var child = Child(name, seedTwo);
                var verdict = SeasonCloseChildVerdict(child.Exit, child.Output, CareerOf(name), 2, afterTwo);
                var outsideDiff = verdict is null ? SeasonSnapshotDiff(refSnap, Outside(name)) : null;
                var orphans = Orphans(name);
                var orphansOk = orphans.Keys.SequenceEqual(setAside.OrderBy(x => x, StringComparer.Ordinal))
                                && setAside.All(rel => leftBehind.TryGetValue(rel, out var h) && orphans[rel] == h)
                                && (setAside.Length > 0 || !Directory.Exists(Path.Combine(Dir(name), "career.orphans")));
                var interruptedRight = step is null || (why is not null && why.Contains("CONSTRUCTED INTERRUPTION at " + step, StringComparison.Ordinal));
                timings.Add(Inv115($"{name}: fresh-process re-run {child.Seconds:F1}s; set aside [{string.Join(", ", orphans.Keys)}]"));
                Check(Inv115($"C2 {name}: ★ interrupted ({(step is null ? "half the season played, then stopped" : "at the close's " + step + " step")}): ") +
                      "the career file is byte-identical to after season one; a fresh process plays season two again and every file " +
                      "outside career.orphans equals the uninterrupted career's byte for byte; career.orphans holds exactly " +
                      (setAside.Length == 0 ? "nothing (no folder)" : "[" + string.Join(", ", setAside) + "], byte-untouched"),
                      interruptedRight && untouched && verdict is null && outsideDiff is null && orphansOk,
                      !interruptedRight ? "interruption: " + Short(why)
                      : !untouched ? "the career file moved"
                      : verdict is not null ? "child: " + verdict
                      : outsideDiff ?? (orphansOk ? Inv115($"{child.Seconds:F1}s") : "orphans [" + string.Join(", ", orphans.Keys) + "]"));
                rerunCareers.Add((name, 2));
            }

            //  ── just AFTER the publish: the season is committed. ──
            CopyDir(Dir("master"), Dir("at-published"));
            {
                var why = Interrupt("at-published", seedTwo, "published");
                var committed = PeekState(CareerOf("at-published")) == afterTwo;
                var sameAsRef = SeasonSnapshotDiff(refSnap, SeasonFolderSnapshot(Dir("at-published")));
                CopyDir(Dir("ref"), Dir("ref3"));
                var three = Child("ref3", seedThree);
                var afterThree = PeekState(CareerOf("ref3"));
                var refVerdict = SeasonCloseChildVerdict(three.Exit, three.Output, CareerOf("ref3"), 3, afterThree);
                var pubThree = Child("at-published", seedThree);
                var pubVerdict = SeasonCloseChildVerdict(pubThree.Exit, pubThree.Output, CareerOf("at-published"), 3, afterThree);
                var threeDiff = SeasonSnapshotDiff(SeasonFolderSnapshot(Dir("ref3")), SeasonFolderSnapshot(Dir("at-published")));
                timings.Add(Inv115($"at-published: season three in a fresh process {pubThree.Seconds:F1}s (the reference's {three.Seconds:F1}s)"));
                Check("C2 at-published: ★ interrupted just AFTER the publish, the season is COMMITTED: reopened, the career shows " +
                      "season two played, every season-two file in place and byte-identical to the uninterrupted career's, and nothing " +
                      "set aside; season three then plays in a fresh process, sets nothing aside, and equals the reference's season three",
                      why is not null && why.Contains("CONSTRUCTED INTERRUPTION at published", StringComparison.Ordinal)
                      && committed && sameAsRef is null && refVerdict is null && pubVerdict is null && threeDiff is null
                      && !Directory.Exists(Path.Combine(Dir("at-published"), "career.orphans")),
                      !committed ? "not committed" : sameAsRef ?? refVerdict ?? pubVerdict ?? threeDiff ?? Inv115($"{pubThree.Seconds:F1}s"));
                rerunCareers.Add(("at-published", 3));
            }

            //  ── the first season, on each side of the play. ──
            foreach (var (name, step, setAside) in new (string, string?, string[])[]
                     {
                         ("first-mid", null, Array.Empty<string>()),
                         ("first-counters", "counters", new[] { "career.events/season-1.json", "career.gamelog/season-1.log", "career.gamelog/season-1.scout",
                                                                 "career.league/league-1.json" }),
                     })
            {
                CopyDir(Dir("fresh"), Dir(name));
                var freshCareer = File.ReadAllBytes(CareerOf(name));
                Interrupt(name, seedOne, step);
                var untouched = File.ReadAllBytes(CareerOf(name)).AsSpan().SequenceEqual(freshCareer);
                var leftBehind = SeasonCloseSeasonFiles(Dir(name), 1);
                var child = Child(name, seedOne);
                var verdict = SeasonCloseChildVerdict(child.Exit, child.Output, CareerOf(name), 1, afterOne);
                var outsideDiff = verdict is null ? SeasonSnapshotDiff(SeasonFolderSnapshot(Dir("master")), Outside(name)) : null;
                var orphans = Orphans(name);
                var orphansOk = orphans.Keys.SequenceEqual(setAside.OrderBy(x => x, StringComparer.Ordinal))
                                && setAside.All(rel => leftBehind.TryGetValue(rel, out var h) && orphans[rel] == h);
                timings.Add(Inv115($"{name}: fresh-process re-run {child.Seconds:F1}s; set aside [{string.Join(", ", orphans.Keys)}]"));
                Check(Inv115($"C2 {name}: ★ a career's FIRST season interrupted ({step ?? "mid-season"}): the new career file is untouched, and a ") +
                      "fresh process plays season one again identical to the uninterrupted career — the bootstrap people's numbers included",
                      untouched && verdict is null && outsideDiff is null && orphansOk,
                      !untouched ? "the career file moved" : verdict is not null ? "child: " + verdict
                      : outsideDiff ?? (orphansOk ? Inv115($"{child.Seconds:F1}s") : "orphans [" + string.Join(", ", orphans.Keys) + "]"));
                rerunCareers.Add((name, 1));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — A NUMBER IS NEVER PUBLISHED TWICE.
            // ════════════════════════════════════════════════════════════════════
            {
                (List<GameId> Games, Dictionary<PersonId, string>[] Rosters) Ids(string name, long seasons)
                {
                    var games = new List<GameId>();
                    var rosters = new Dictionary<PersonId, string>[seasons];
                    string historyId;
                    using (var s = OpenCareer(CareerOf(name), mte)) historyId = s.HistoryId;
                    for (var n = 1L; n <= seasons; n++)
                    {
                        var log = GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(CareerOf(name), n),
                                                              new GameLogBindings(historyId, fp, n));
                        games.AddRange(log.Blocks.Select(b => b.Facts.GameId));
                        rosters[n - 1] = log.RosterV2().ToDictionary(e => e.PersonId, e => e.Name);
                    }
                    return (games, rosters);
                }
                var refIds = Ids("ref3", 3);
                var faults = new List<string>();
                foreach (var (name, seasons) in rerunCareers)
                {
                    var ids = Ids(name, seasons);
                    if (ids.Games.Distinct().Count() != ids.Games.Count) faults.Add(name + ": a game number appears twice");
                    for (var n = 1; n < seasons; n++)
                        foreach (var (who, nameThen) in ids.Rosters[n - 1])
                            if (ids.Rosters[n].TryGetValue(who, out var nameNow) && nameNow != nameThen)
                                faults.Add(Inv115($"{name}: {who} names {nameThen} in season {n} and {nameNow} in season {n + 1}"));
                    if (!ids.Games.SequenceEqual(refIds.Games.Take(ids.Games.Count)))
                        faults.Add(name + ": game numbers differ from the reference's");
                    for (var n = 0; n < seasons; n++)
                        if (!ids.Rosters[n].Keys.ToHashSet().SetEquals(refIds.Rosters[n].Keys))
                            faults.Add(Inv115($"{name}: season {n + 1}'s people differ from the reference's"));
                }
                var refGamesOnce = refIds.Games.Distinct().Count() == refIds.Games.Count;
                Check("C3: ★ in every re-run career, and in the reference over three seasons: every game number appears once in the " +
                      "career, a man kept from one season to the next is the same man, and the game numbers and each season's people " +
                      "are the reference's exactly",
                      faults.Count == 0 && refGamesOnce && rerunCareers.Count == 10,
                      faults.Count > 0 ? string.Join("; ", faults.Take(3))
                                       : Inv115($"{rerunCareers.Count} careers; the reference: {refIds.Games.Count} game numbers over three seasons"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — THE SET-ASIDE, AND ITS REFUSAL.
            // ════════════════════════════════════════════════════════════════════
            CopyDir(Dir("master"), Dir("plant"));
            {
                string P(string rel) => Path.Combine(Dir("plant"), rel.Replace('/', Path.DirectorySeparatorChar));
                var planted = new Dictionary<string, byte[]>
                {
                    ["career.events/season-2.json"] = "{ \"planted\": \"record\" }"u8.ToArray(),
                    ["career.gamelog/season-2.log.inprogress"] = "planted unfinished log"u8.ToArray(),
                    ["career.gamelog/season-4.scout"] = "planted scouting file, two seasons past"u8.ToArray(),
                    ["career.league/league-3.json"] = "planted league state, a season past"u8.ToArray(),   // ★ S125 — the fifth kind
                };
                var notes = new[] { "career.events/notes.txt", "career.gamelog/notes.txt", "career.league/notes.txt" };
                foreach (var (rel, bytes) in planted) File.WriteAllBytes(P(rel), bytes);
                foreach (var rel in notes) File.WriteAllText(P(rel), "an unrecognised file");
                var notesBefore = notes.ToDictionary(r => r, r => File.ReadAllBytes(P(r)));
                var seasonOneBefore = SeasonCloseSeasonFiles(Dir("master"), 1);
                var msg = Refusal(() => PlayInProcess("plant", seedTwo));
                var orphans = Orphans("plant");
                var movedOk = orphans.Keys.SequenceEqual(planted.Keys.OrderBy(x => x, StringComparer.Ordinal))
                              && planted.All(kv => orphans[kv.Key] == Convert.ToHexString(SHA256.HashData(kv.Value)).ToLowerInvariant());
                var published = PeekState(CareerOf("plant")) == afterTwo;
                var sameAsRef = SeasonSnapshotDiff(refSnap, Outside("plant", notes));
                Check("C4a: ★ a planted record and unfinished log for the pending season, a scouting file two seasons past it, and " +
                      "★ S125 a league state a season past it " +
                      "are SET ASIDE into career.orphans — each one's SHA-256 the same after the move — and the season publishes, " +
                      "identical to the uninterrupted career",
                      msg is null && movedOk && published && sameAsRef is null,
                      msg ?? sameAsRef ?? "[" + string.Join(", ", orphans.Keys) + "]");
                Check("C4b: season one's files (below the pending season) are never touched, and an unrecognised file in each " +
                      "career folder is left where it was, byte-identical",
                      SeasonCloseSeasonFiles(Dir("plant"), 1).SequenceEqual(seasonOneBefore)
                      && notes.All(r => File.Exists(P(r)) && File.ReadAllBytes(P(r)).AsSpan().SequenceEqual(notesBefore[r])),
                      Inv115($"{seasonOneBefore.Count} season-one files, {notes.Length} unrecognised files"));
            }

            //  ── NEGATIVE CONTROL: the orphans folder blocked by a regular file. Run as a CHILD, which is also C2's
            //     negative control: a child that fails and still exits 0 must be rejected by the verdict. ──
            CopyDir(Dir("master"), Dir("blocked"));
            {
                var record = Path.Combine(Dir("blocked"), "career.events", "season-2.json");
                File.WriteAllText(record, "{ \"planted\": \"record\" }");
                var recordBefore = File.ReadAllBytes(record);
                File.WriteAllText(CareerOrphansFolderFor(CareerOf("blocked")), "a file standing where the orphans folder goes");
                var child = Child("blocked", seedTwo);
                var verdict = SeasonCloseChildVerdict(child.Exit, child.Output, CareerOf("blocked"), 2, afterTwo);
                var refusedByName = child.Output.Contains("SEASON CLOSE REFUSED", StringComparison.Ordinal)
                                    && child.Output.Contains(Path.Combine("career.events", "season-2.json"), StringComparison.Ordinal);
                Check("C4c: NEGATIVE CONTROL — a regular file standing where career.orphans must be created: the close is refused by " +
                      "name, naming the file it could not move; nothing is published (the career file byte-identical) and the season " +
                      "file stays where it was",
                      refusedByName && File.ReadAllBytes(CareerOf("blocked")).AsSpan().SequenceEqual(masterCareer)
                      && File.Exists(record) && File.ReadAllBytes(record).AsSpan().SequenceEqual(recordBefore),
                      refusedByName ? "refused" : "output: " + Short(child.Output.Split('\n').LastOrDefault(l => l.Contains("ERROR", StringComparison.Ordinal))));
                Check("C2 NEGATIVE CONTROL — that child exits 0 and prints SEASON ERROR; the verdict rejects it, naming the reason",
                      child.Exit == 0 && verdict is not null && verdict.StartsWith("printed SEASON ERROR", StringComparison.Ordinal),
                      Inv115($"exit {child.Exit}; verdict: ") + (verdict ?? "ACCEPTED"));
            }

            //  The same store, after a close that failed, cannot host another season.
            CopyDir(Dir("master"), Dir("one-store"));
            {
                string? second;
                using (var store = OpenCareer(CareerOf("one-store"), mte))
                {
                    using (var run = SeasonBegin(mte, seedTwo, configPath, verbose: false,
                               Opts(store, s => { if (s == "counters") throw new InvalidOperationException("CONSTRUCTED INTERRUPTION at " + s); })))
                    {
                        run.AdvanceThrough(run.LastLegalDay);
                        Refusal(() => run.Finish());
                    }
                    second = Refusal(() => store.ReservePersons(1));
                }
                Check("C1c: after a close that failed, a further reservation on the same store is refused by name (one store, one season)",
                      second is not null && second.Contains("reservations are closed", StringComparison.Ordinal), Short(second));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — THE CLOSE'S ORDER (observed on the reference's season two).
            // ════════════════════════════════════════════════════════════════════
            {
                var steps = observed.Select(o => o.Step).ToList();
                var expectedSteps = new[] { "set-aside", "log", "log-open", "scouting", "record", "league", "counters", "published" };
                var faults = observed.Where(o => o.Step != "published")
                                     .Select(o => SeasonCloseOrderFault(o.Step, o.Files, o.Career, masterCareer, 2))
                                     .Where(f => f is not null).ToList();
                var afterClose = File.ReadAllBytes(CareerOf("ref"));
                Check("C5a: ★ at each close step the season files on disk are exactly the earlier steps' — none at set-aside and log; " +
                      "the unfinished log at log-open; the log at scouting; the log and scouting file at record; all three at league; " +
                      "★ S125 those and the league state at counters — " +
                      "and the career file is the pre-season bytes at every step; after the close it is not",
                      steps.SequenceEqual(expectedSteps) && faults.Count == 0 && !afterClose.AsSpan().SequenceEqual(masterCareer),
                      faults.Count > 0 ? faults[0]! : "[" + string.Join(" → ", steps) + "]");
                var counters = observed.FirstOrDefault(o => o.Step == "counters");
                string? control = null;
                if (counters.Career is not null)
                {
                    var doctored = (byte[])counters.Career.Clone();
                    doctored[doctored.Length / 2] ^= 0x01;
                    control = SeasonCloseOrderFault("counters", counters.Files, doctored, masterCareer, 2);
                }
                Check("C5b: NEGATIVE CONTROL — the same comparison, handed the counters step's snapshot with one byte of career.json " +
                      "changed, fails and names career.json",
                      control is not null && control.Contains("career.json", StringComparison.Ordinal), control ?? "NO FAULT");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — PAGE-ONLY.
            // ════════════════════════════════════════════════════════════════════
            foreach (var t in timings) Console.WriteLine("  [page] " + t);
        }
        catch (Exception ex)
        {
            Check("Phase 115 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 115 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }
}
