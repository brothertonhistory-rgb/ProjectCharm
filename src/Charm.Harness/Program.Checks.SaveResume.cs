using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 118 — S127: A SEASON SAVED MID-WAY RESUMES EXACTLY (save design B2a).
//
//  The proof is one comparison, made over and over: a season saved on a day and
//  resumed finishes BYTE-IDENTICAL to the same season played straight through —
//  every game, the career file, every file it lists, and the close's page. No new
//  pin: every comparison is against a season played straight through in this run.
//
//    C1  ★ resume at every day, in this process — season two of a fixture-mte career,
//        saved to its career at the end of EVERY day from October 31 to the last (the
//        save made by the run that keeps playing), each save copied out and resumed to
//        the close; and each save, restored and saved again, byte-identical to itself.
//        ★ Gate finding 1: fixture-mte plays no conference tournaments (a league needs
//        eight teams; its leagues have five), so the tournament weeks are cut on the
//        stock world: mid-February, the day before they open, the day they open,
//        mid-tournament, their last day. The runs that saved every day also finish
//        identical.
//    C2  ★ resume in a fresh program — the `season` command as a child process, saved
//        with --through and resumed by a second run, at the days that stress state
//        (fixture-mte: before the first game, after the first day, mid-bracket,
//        Christmas, mid-February, after the last game; stock: mid-February and
//        mid-tournament), and chained (four saves, then the close). ★ Gate finding 2:
//        what is compared is the career file, every file it lists, and that nothing
//        unlisted sits outside the saves and set-aside folders — not the whole folder,
//        whose backup is (correctly) the mid-season save.
//    C3  the save on disk — `progress` names the season, the next day, the seed, the
//        save and its snapshot by hash; the published counters do not move; no season
//        file is written; a second save is save 2 under a new name and save 1 stays (the
//        backup lists it); after the close `progress` is null and no snapshot is
//        listed; one byte changed in a snapshot refuses the open.
//    C4  the refusals — another seed, other settings, another build, --through with no
//        career, --through before the next day: each by name, nothing played, the career
//        byte-identical. A page opened on a saved career reads it and changes nothing.
//    C5  negative controls — each turns the comparison red: a restore that drops one
//        bracket's occupancy; the shared results list restored as a copy (stock, cut
//        before the conference tournaments seed); a dictionary restored in reverse order
//        where the season reads it in order. The walker refuses a planted world object
//        and a planted delegate, by name and path.
//    C6  page-only — a stock March save's size, save and load times, every type met.
//    C7  a save that hashes right but is wrong inside — truncated, a dangling reference,
//        an unknown type, a length past the end, a header whose next day disagrees, a body
//        whose restored next day disagrees: each refused by its own name, nothing played,
//        the career byte-identical. Control: the same re-listing of a sound save resumes.
//    C8  saves at illegal moments — from inside a day (the month line), from inside the
//        close (its step seam), after a fault, after the close: each refused by name,
//        nothing written, nothing published.
//
//  All in-process seasons run under the fixed history id and the REAL rules (nothing
//  here is pinned), so a child process's career is comparable byte for byte.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ S127 — C5's planted objects: a check-only holder the walker is pointed at.</summary>
    private sealed class SnapshotPlantHolder { public object? Payload; }

    private static bool Phase118SeasonResumeCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 118 — S127: a season saved mid-way resumes exactly. Saved on any day and resumed, in this " +
                          "process and in a fresh one, it finishes byte-identical to the season played straight through ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        static string Short(string? s) => s is null ? "NONE" : s.Replace('\n', ' ')[..Math.Min(170, s.Length)];
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static FieldInfo FieldOf(Type t, string name)
        {
            for (var b = t; b is not null; b = b.BaseType)
                if (b.GetField(name, F | BindingFlags.DeclaredOnly) is { } f) return f;
            throw new InvalidOperationException("no field " + name + " on " + t.Name);
        }
        static object? Get(object o, string name) => FieldOf(o.GetType(), name).GetValue(o);
        static void Set(object o, string name, object? v) => FieldOf(o.GetType(), name).SetValue(o, v);
        var timings = new List<string>();
        const string SabotageBroke = "THE CONTROL ITSELF BROKE — ";

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s127-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var mtePath = WorldPath("fixture-mte.world.json");
            var stockPath = WorldPath("stock-d1.world.json");
            var mte = LoadWorld(mtePath);
            var stock = LoadWorld(stockPath);
            var seedOne = CalendarCheckSeed;
            var seedTwo = SeasonTwoSeed(seedOne);
            const long stockSeed = 20260720;
            using var fixedId = HistoryStore.UseFixedHistoryIdForTests(CalendarFixedHistoryId);

            string Dir(string name) => Path.Combine(scratch, name);
            string CareerOf(string name) => Path.Combine(Dir(name), "career.json");
            void Wipe(string name) { try { if (Directory.Exists(Dir(name))) Directory.Delete(Dir(name), true); } catch { /* best effort */ } }

            //  The files a career lists, read off its file (name-free paths).
            static List<(string Path, string Sha)> Listed(string career)
            {
                var b = File.ReadAllBytes(career);
                var node = JsonNode.Parse(b.AsSpan(0, b.Length - 72))!.AsObject();
                return node["files"]!.AsArray().Select(f => ((string)f!["path"]!, (string)f["sha256"]!)).ToList();
            }
            static JsonObject CareerJson(string career)
            {
                var b = File.ReadAllBytes(career);
                return JsonNode.Parse(b.AsSpan(0, b.Length - 72))!.AsObject();
            }
            //  A career as it stands: its file and every file it lists (no backup, no lock, nothing unlisted).
            void CopyCareer(string from, string to)
            {
                Wipe(to);
                Directory.CreateDirectory(Dir(to));
                File.Copy(CareerOf(from), CareerOf(to));
                foreach (var (p, _) in Listed(CareerOf(from)))
                {
                    var dst = HistoryStore.ResolveListed(CareerOf(to), p);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(HistoryStore.ResolveListed(CareerOf(from), p), dst);
                }
            }
            static SortedDictionary<string, string> NoLocks(SortedDictionary<string, string> s)
                => new(s.Where(kv => !kv.Key.EndsWith(".lock", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal);
            SortedDictionary<string, string> Folder(string name) => NoLocks(SeasonFolderSnapshot(Dir(name)));

            static string ExtractPage(string text)
            {
                var lines = text.Replace("\r\n", "\n").Split('\n');
                var at = Array.FindIndex(lines, l => l.StartsWith("Conference schedule fingerprint:", StringComparison.Ordinal));
                return at < 0 ? "(NO PAGE)" : string.Join("\n", lines.Skip(at).Where(l => !l.StartsWith("History:", StringComparison.Ordinal)));
            }
            static T Quiet<T>(Func<T> f)
            {
                var old = Console.Out;
                Console.SetOut(TextWriter.Null);
                try { return f(); } finally { Console.SetOut(old); }
            }
            string PageOf(SeasonRunOutcome o, WorldFile w, HistoryStore h)
            {
                var old = Console.Out;
                var sw = new StringWriter(CultureInfo.InvariantCulture);
                Console.SetOut(sw);
                try { PrintSeasonPage(o, w, h, SeasonDefaultMinuteFloor); } finally { Console.SetOut(old); }
                return ExtractPage(sw.ToString());
            }
            SeasonRunOptions Opts(HistoryStore store, Action<string>? step = null)
                => new(History: store, RetainGameLog: true, CloseStepForTest: step);

            //  One season straight through on the named career; its outcome and its page.
            (SeasonRunOutcome Outcome, string Page) Straight(string name, WorldFile w, long seed)
            {
                using var store = OpenCareer(CareerOf(name), w);
                var outcome = Quiet(() =>
                {
                    using var run = SeasonBegin(w, seed, configPath, verbose: true, Opts(store));
                    run.AdvanceThrough(run.LastLegalDay);
                    return run.Finish();
                });
                return (outcome, PageOf(outcome, w, store));
            }

            //  The career's saved season, resumed in this process and played to the close. `sabotage` is a
            //  negative control's constructed defect, applied to the restored run before it plays.
            (SeasonRunOutcome? Outcome, string Page, string? Thrown, string? SelfFault, double Load) ResumeToClose(
                string name, WorldFile w, Action<SeasonRun>? sabotage = null)
            {
                try
                {
                    using var store = OpenCareer(CareerOf(name), w);
                    var bytes = store.ReadProgressSnapshot();
                    var sw = Stopwatch.StartNew();
                    using var run = SeasonRun.Resume(bytes, w, store, configPath, verbose: true);
                    var load = sw.Elapsed.TotalSeconds;
                    var again = run.SaveSnapshot();
                    var self = again.AsSpan().SequenceEqual(bytes) ? null
                        : Inv($"restored and saved again, {again.Length:N0} bytes against {bytes.Length:N0}, first difference at byte ") +
                          Enumerable.Range(0, Math.Min(again.Length, bytes.Length)).FirstOrDefault(i => again[i] != bytes[i]).ToString(CultureInfo.InvariantCulture);
                    //  A control's constructed defect that itself throws is the CONTROL broken, never the
                    //  comparison red: it is reported as such and the control's check fails.
                    try { sabotage?.Invoke(run); }
                    catch (Exception sx) { return (null, "", SabotageBroke + sx.GetType().Name + ": " + sx.Message, self, load); }
                    var outcome = Quiet(() => { run.AdvanceThrough(run.LastLegalDay); return run.Finish(); });
                    return (outcome, PageOf(outcome, w, store), null, self, load);
                }
                catch (Exception ex)
                {
                    return (null, "", ex.GetType().Name + ": " + ex.Message, null, 0);
                }
            }

            //  The comparison: the first game that differs, then the career file and its files, then the page.
            static string? GamesFault(SeasonRunOutcome a, SeasonRunOutcome b)
            {
                if (a.Results.Count != b.Results.Count) return Inv($"{b.Results.Count} games against {a.Results.Count}");
                for (var g = 0; g < a.Results.Count; g++)
                    if (!Equals(a.Results[g], b.Results[g]))
                        return Inv($"first game that differs: ordinal {g} ({a.PlayedGames[g].Game.Kind}, {a.PlayedGames[g].Game.Date:yyyy-MM-dd}): ") +
                               Inv($"{b.Results[g].HomeScore}-{b.Results[g].AwayScore} against {a.Results[g].HomeScore}-{a.Results[g].AwayScore}");
                return null;
            }
            string? CareerFault(string refName, string name)
            {
                var a = File.ReadAllBytes(CareerOf(refName));
                var b = File.ReadAllBytes(CareerOf(name));
                if (!a.AsSpan().SequenceEqual(b)) return "the career file differs";
                foreach (var (p, sha) in Listed(CareerOf(name)))
                {
                    var file = HistoryStore.ResolveListed(CareerOf(name), p);
                    if (!File.Exists(file) || HistoryStore.HashFile(file) != sha) return p + " is not the file listed";
                    if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(HistoryStore.ResolveListed(CareerOf(refName), p))))
                        return p + " differs";
                }
                var listed = Listed(CareerOf(name)).Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
                foreach (var (rel, _) in Folder(name))
                {
                    if (rel is "career.json" or "career.json.bak") continue;
                    if (rel.StartsWith("career.progress/", StringComparison.Ordinal) || rel.StartsWith("career.orphans/", StringComparison.Ordinal)) continue;
                    var p = rel["career.".Length..];
                    if (!listed.Contains(p)) return rel + " is on disk but not listed";
                }
                return null;
            }
            static string? PageFault(string a, string b)
            {
                if (a == b) return null;
                var la = a.Split('\n'); var lb = b.Split('\n');
                for (var i = 0; i < Math.Max(la.Length, lb.Length); i++)
                    if (i >= la.Length || i >= lb.Length || la[i] != lb[i])
                        return Inv($"the page differs at line {i + 1}: '{Short(i < lb.Length ? lb[i] : "(none)")}'");
                return "the page differs";
            }
            string? ResumeFault(SeasonRunOutcome refOutcome, string refPage, string refName, string name,
                                (SeasonRunOutcome? Outcome, string Page, string? Thrown, string? SelfFault, double Load) got)
                => got.Thrown is not null ? "the resumed season failed: " + Short(got.Thrown)
                 : GamesFault(refOutcome, got.Outcome!) ?? CareerFault(refName, name) ?? PageFault(refPage, got.Page);

            //  The `season` command in a FRESH PROCESS (Phase 115's form).
            (int Exit, string Output) Child(params string[] args)
            {
                var host = Environment.ProcessPath ?? throw new InvalidOperationException("no process path to launch a child");
                var psi = new ProcessStartInfo(host)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
                    psi.ArgumentList.Add(typeof(Program).Assembly.Location);
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(20 * 60 * 1000))
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    return (-1, "TIMED OUT");
                }
                return (p.ExitCode, outTask.Result + errTask.Result);
            }
            (int Exit, string Output) SeasonChild(string world, long seed, string name, DateOnly? through = null)
                => through is { } t
                    ? Child("season", world, seed.ToString(CultureInfo.InvariantCulture), "--history", CareerOf(name), "--through", Day(t))
                    : Child("season", world, seed.ToString(CultureInfo.InvariantCulture), "--history", CareerOf(name));

            // ════════════════════════════════════════════════════════════════════
            //  The references, played straight through.
            // ════════════════════════════════════════════════════════════════════
            var sw0 = Stopwatch.StartNew();
            Directory.CreateDirectory(Dir("master"));
            Straight("master", mte, seedOne);                              // fixture-mte season one
            CopyCareer("master", "ref2");
            var (ref2, ref2Page) = Straight("ref2", mte, seedTwo);         // fixture-mte season two, straight
            Directory.CreateDirectory(Dir("sfresh"));
            using (OpenCareer(CareerOf("sfresh"), stock)) { }               // a new stock career
            CopyCareer("sfresh", "sref");
            var (sref, srefPage) = Straight("sref", stock, stockSeed);     // stock season one, straight
            timings.Add(Inv($"references (fixture-mte seasons one and two, stock season one): {sw0.Elapsed.TotalSeconds:F1}s"));

            //  The days that stress state.
            var bracketFrom = ref2.ConferenceGameCount;
            var bracketTo = ref2.ConferenceGameCount + ref2.TournamentGameCount - ref2.ShowcaseGameCount;
            var bracketDays = ref2.PlayedGames.Where(p => p.FixtureOrdinal >= bracketFrom && p.FixtureOrdinal < bracketTo)
                                              .Select(p => p.Game.Date!.Value).Distinct().OrderBy(d => d).ToList();
            var midBracket = bracketDays.Count > 1 ? bracketDays[0] : (DateOnly?)null;   // round one played, later rounds not
            var mteLastGame = ref2.PlayedGames.Max(p => p.Game.Date!.Value);
            var ctDays = sref.PlayedGames.Where(p => string.Equals(p.Game.Kind, "ctourney", StringComparison.Ordinal))
                                         .Select(p => p.Game.Date!.Value).Distinct().OrderBy(d => d).ToList();
            Check("C0: the stress days exist — fixture-mte season two has a bracket with a later round, and stock plays " +
                  "conference tournaments over more than two days",
                  midBracket is not null && ctDays.Count > 2,
                  Inv($"bracket days {string.Join(", ", bracketDays.Select(Day))}; stock tournaments {(ctDays.Count > 0 ? Day(ctDays[0]) + ".." + Day(ctDays[^1]) : "none")}"));
            var ctOpen = ctDays[0];
            var ctMid = ctDays[ctDays.Count / 2];
            var ctLast = ctDays[^1];
            var mteFirst = ref2.FirstLegalDay;
            var stockFeb = new DateOnly(sref.FirstLegalDay.Year + 1, 2, 10);
            var mteFeb = new DateOnly(mteFirst.Year + 1, 2, 10);
            var mteXmas = new DateOnly(mteFirst.Year, 12, 25);

            // ════════════════════════════════════════════════════════════════════
            //  C1 — RESUME AT EVERY DAY, IN THIS PROCESS (fixture-mte season two).
            // ════════════════════════════════════════════════════════════════════
            var keepFor = new Dictionary<DateOnly, string>();   // days whose copies later controls use
            if (midBracket is { } mb) keepFor[mb] = "c5-occupancy";
            keepFor[mteXmas] = "c5-reverse";
            {
                var sw = Stopwatch.StartNew();
                CopyCareer("master", "c1-live");
                var faults = new List<string>();
                var selfFaults = new List<string>();
                var days = 0;
                using (var store = OpenCareer(CareerOf("c1-live"), mte))
                {
                    var outcome = Quiet(() =>
                    {
                        using var run = SeasonBegin(mte, seedTwo, configPath, verbose: true, Opts(store));
                        for (var d = run.FirstLegalDay.AddDays(-1); d <= run.LastLegalDay; d = d.AddDays(1))
                        {
                            run.AdvanceThrough(d);
                            run.SaveProgress();
                            CopyCareer("c1-live", "c1-day");
                            var got = ResumeToClose("c1-day", mte);
                            if (ResumeFault(ref2, ref2Page, "ref2", "c1-day", got) is { } f) faults.Add(Day(d) + ": " + f);
                            if (got.SelfFault is { } sf) selfFaults.Add(Day(d) + ": " + sf);
                            if (keepFor.TryGetValue(d, out var keep)) CopyCareer("c1-live", keep);
                            days++;
                        }
                        return run.Finish();
                    });
                    Check("C1a: ★ fixture-mte season two saved at the end of EVERY day (October 31 to the last, empty days " +
                          "included) and resumed: every game, the career file, every file it lists and the close's page " +
                          "byte-identical to the season played straight through",
                          faults.Count == 0 && days > 150,
                          faults.Count > 0 ? Inv($"{faults.Count} of {days} days: ") + string.Join("; ", faults.Take(3)) : Inv($"{days} days"));
                    Check("C1b: ★ every one of those saves, restored and saved again, is byte-identical to itself",
                          selfFaults.Count == 0, selfFaults.Count > 0 ? string.Join("; ", selfFaults.Take(3)) : Inv($"{days} saves"));
                    Check("C1c: the season that saved itself every day finished identical too (saving changes nothing)",
                          GamesFault(ref2, outcome) is null && CareerFault("ref2", "c1-live") is null,
                          GamesFault(ref2, outcome) ?? CareerFault("ref2", "c1-live") ?? "identical");
                }
                Wipe("c1-day");
                timings.Add(Inv($"C1 fixture-mte, {days} days saved and resumed: {sw.Elapsed.TotalSeconds:F1}s"));
            }

            //  ★ Gate finding 1 — the tournament weeks, on stock (season one of a fresh career).
            byte[]? marchSave = null;
            double marchSaveSeconds = 0, marchLoadSeconds = 0;
            {
                var sw = Stopwatch.StartNew();
                var cuts = new (string Label, DateOnly Day, string? Keep)[]
                {
                    ("mid-February", stockFeb, "c5-shared"),
                    ("the day before the conference tournaments open", ctOpen.AddDays(-1), null),
                    ("the day they open", ctOpen, null),
                    ("mid-tournament", ctMid, null),
                    ("their last day", ctLast, null),
                };
                CopyCareer("sfresh", "c1s-live");
                var faults = new List<string>();
                var selfFaults = new List<string>();
                using (var store = OpenCareer(CareerOf("c1s-live"), stock))
                {
                    var outcome = Quiet(() =>
                    {
                        using var run = SeasonBegin(stock, stockSeed, configPath, verbose: true, Opts(store));
                        foreach (var (label, day, keep) in cuts)
                        {
                            run.AdvanceThrough(day);
                            var t = Stopwatch.StartNew();
                            run.SaveProgress();
                            var saveSeconds = t.Elapsed.TotalSeconds;
                            CopyCareer("c1s-live", "c1s-day");
                            if (keep is not null) CopyCareer("c1s-live", keep);
                            var got = ResumeToClose("c1s-day", stock);
                            if (ResumeFault(sref, srefPage, "sref", "c1s-day", got) is { } f) faults.Add(label + ": " + f);
                            if (got.SelfFault is { } sf) selfFaults.Add(label + ": " + sf);
                            if (day == ctOpen)
                            {
                                marchSave = File.ReadAllBytes(HistoryStore.ResolveListed(CareerOf("c1s-day"), store.Progress!.Snapshot));
                                marchSaveSeconds = saveSeconds;
                                marchLoadSeconds = got.Load;
                            }
                        }
                        run.AdvanceThrough(run.LastLegalDay);
                        return run.Finish();
                    });
                    Check("C1d: ★ gate finding 1 — the stock season saved mid-February, the day before its conference tournaments " +
                          Inv($"open ({Day(ctOpen.AddDays(-1))}), the day they open, mid-tournament ({Day(ctMid)}) and their last day ({Day(ctLast)}), ") +
                          "each resumed: byte-identical to the season played straight through, and each save restored and saved again identical to itself",
                          faults.Count == 0 && selfFaults.Count == 0,
                          faults.Concat(selfFaults).Any() ? string.Join("; ", faults.Concat(selfFaults).Take(3)) : Inv($"{cuts.Length} cuts"));
                    Check("C1e: the stock season that saved itself at those days finished identical too",
                          GamesFault(sref, outcome) is null && CareerFault("sref", "c1s-live") is null,
                          GamesFault(sref, outcome) ?? CareerFault("sref", "c1s-live") ?? "identical");
                }
                Wipe("c1s-day");
                Wipe("c1s-live");
                timings.Add(Inv($"C1 stock, {cuts.Length} cuts saved and resumed: {sw.Elapsed.TotalSeconds:F1}s"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — RESUME IN A FRESH PROGRAM.
            // ════════════════════════════════════════════════════════════════════
            {
                var sw = Stopwatch.StartNew();
                var cuts = new List<(string Label, DateOnly Day)>
                {
                    ("before the first game", mteFirst.AddDays(-1)), ("after the first day", mteFirst),
                    ("mid-bracket", midBracket!.Value), ("Christmas", mteXmas), ("mid-February", mteFeb),
                    ("after the last game", mteLastGame),
                };
                var faults = new List<string>();
                foreach (var (label, day) in cuts)
                {
                    CopyCareer("master", "c2");
                    var save = SeasonChild(mtePath, seedTwo, "c2", day);
                    var resume = SeasonChild(mtePath, seedTwo, "c2");
                    var said = save.Output.Contains(Inv($"Saved: season 2 through {Day(day)} (save 1)."), StringComparison.Ordinal)
                               && resume.Output.Contains(Inv($"Resuming season 2 on {Day(day.AddDays(1))} (save 1)."), StringComparison.Ordinal);
                    var f = !said || save.Exit != 0 || resume.Exit != 0
                        ? "the save or the resume did not say so: " + Short(save.Output[^Math.Min(300, save.Output.Length)..] + " / " + resume.Output[..Math.Min(300, resume.Output.Length)])
                        : CareerFault("ref2", "c2") ?? PageFault(ref2Page, ExtractPage(resume.Output));
                    if (f is not null) faults.Add(label + " (" + Day(day) + "): " + f);
                }
                Check("C2a: ★ fixture-mte season two saved by the `season` command with --through and resumed by a SECOND " +
                      "program, at " + string.Join(", ", cuts.Select(c => c.Label)) + ": the career file, every file it lists " +
                      "and the close's page byte-identical to the season played straight through, and nothing unlisted outside " +
                      "the saves and set-aside folders (★ gate finding 2)",
                      faults.Count == 0, faults.Count > 0 ? string.Join("; ", faults.Take(2)) : Inv($"{cuts.Count} cuts"));

                //  Chained: four saves in four programs, then a fifth that closes.
                CopyCareer("master", "c2-chain");
                var chain = new[] { mteFirst, mteXmas, mteFeb, mteLastGame };
                var chainFault = (string?)null;
                for (var k = 0; k < chain.Length && chainFault is null; k++)
                {
                    var o = SeasonChild(mtePath, seedTwo, "c2-chain", chain[k]);
                    var want = Inv($"Saved: season 2 through {Day(chain[k])} (save {k + 1}).");
                    if (!o.Output.Contains(want, StringComparison.Ordinal)) chainFault = "missing '" + want + "': " + Short(o.Output[^Math.Min(200, o.Output.Length)..]);
                }
                var last = SeasonChild(mtePath, seedTwo, "c2-chain");
                chainFault ??= !last.Output.Contains("(save 4).", StringComparison.Ordinal) ? "the close did not resume save 4"
                             : CareerFault("ref2", "c2-chain") ?? PageFault(ref2Page, ExtractPage(last.Output));
                Check("C2b: ★ chained — saved, resumed and saved again in four programs (" + string.Join(", ", chain.Select(Day)) +
                      "), then closed by a fifth: identical", chainFault is null, chainFault ?? "saves 1..4 then the close");

                //  Stock: mid-February and mid-tournament.
                var stockFaults = new List<string>();
                foreach (var (label, day) in new[] { ("mid-February", stockFeb), ("mid-tournament", ctMid) })
                {
                    CopyCareer("sfresh", "c2s");
                    var save = SeasonChild(stockPath, stockSeed, "c2s", day);
                    var resume = SeasonChild(stockPath, stockSeed, "c2s");
                    var f = !save.Output.Contains("Saved: season 1 through", StringComparison.Ordinal) || !resume.Output.Contains("Resuming season 1 on", StringComparison.Ordinal)
                        ? "the save or the resume did not say so"
                        : CareerFault("sref", "c2s") ?? PageFault(srefPage, ExtractPage(resume.Output));
                    if (f is not null) stockFaults.Add(label + ": " + f);
                }
                Check("C2c: ★ stock season one, saved in one program and resumed in another, mid-February and mid-tournament " +
                      Inv($"({Day(stockFeb)}, {Day(ctMid)}): identical"),
                      stockFaults.Count == 0, stockFaults.Count > 0 ? string.Join("; ", stockFaults) : "2 cuts");
                Wipe("c2"); Wipe("c2s"); Wipe("c2-chain");
                timings.Add(Inv($"C2 child processes: {sw.Elapsed.TotalSeconds:F1}s"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — THE SAVE ON DISK.
            // ════════════════════════════════════════════════════════════════════
            var dec10 = new DateOnly(mteFirst.Year, 12, 10);
            var jan10 = new DateOnly(mteFirst.Year + 1, 1, 10);
            {
                CopyCareer("master", "c3");
                var before = CareerJson(CareerOf("master"));
                string? firstFault = null, secondFault = null;
                using (var store = OpenCareer(CareerOf("c3"), mte))
                using (var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store))))
                {
                    run.AdvanceThrough(dec10);
                    run.SaveProgress();
                    var j = CareerJson(CareerOf("c3"));
                    var p = j["progress"] as JsonObject;
                    var snap = HistoryStore.ResolveListed(CareerOf("c3"), "progress/season-2-1.snap");
                    var listedSha = Listed(CareerOf("c3")).FirstOrDefault(x => x.Path == "progress/season-2-1.snap").Sha;
                    var held = store.HeldCounters;
                    var noSeasonFile = !File.Exists(Path.Combine(Dir("c3"), "career.gamelog", "season-2.log"))
                                       && !File.Exists(Path.Combine(Dir("c3"), "career.gamelog", "season-2.scout"))
                                       && !File.Exists(Path.Combine(Dir("c3"), "career.events", "season-2.json"))
                                       && !File.Exists(Path.Combine(Dir("c3"), "career.league", "league-2.json"));
                    firstFault =
                        p is null ? "no progress block"
                        : (long)p["season"]! != 2 || (string)p["nextDay"]! != Day(dec10.AddDays(1)) || (long)p["seasonSeed"]! != seedTwo
                          || (long)p["save"]! != 1 || (string)p["snapshot"]! != "progress/season-2-1.snap" ? "the block says " + Short(p.ToJsonString())
                        : listedSha is null || listedSha != HistoryStore.HashFile(snap) ? "the snapshot is not listed by its hash"
                        : (long)j["nextPersonId"]! != (long)before["nextPersonId"]! || (long)j["nextSeasonId"]! != (long)before["nextSeasonId"]!
                          || (long)j["nextGameId"]! != (long)before["nextGameId"]! ? "the published counters moved"
                        : (long)p["held"]!["nextPersonId"]! != held.NextPersonId || (long)p["held"]!["nextSeasonId"]! != 3
                          || (long)p["held"]!["nextGameId"]! != held.NextGameId || held.NextGameId <= (long)before["nextGameId"]! ? "the held numbers are not the season's"
                        : !noSeasonFile ? "a season-2 file was written"
                        : null;
                    run.AdvanceThrough(jan10);
                    run.SaveProgress();
                    var j2 = CareerJson(CareerOf("c3"));
                    var bak = CareerJson(CareerOf("c3") + ".bak");
                    var snaps2 = Listed(CareerOf("c3")).Where(x => x.Path.StartsWith("progress/", StringComparison.Ordinal)).Select(x => x.Path).ToList();
                    secondFault =
                        (long)j2["progress"]!["save"]! != 2 ? "the second save is not save 2"
                        : !(snaps2.Count == 1 && snaps2[0] == "progress/season-2-2.snap") ? "the career lists " + string.Join(", ", snaps2)
                        : !File.Exists(snap) ? "save 1's file is gone"
                        : !bak["files"]!.AsArray().Any(f => (string)f!["path"]! == "progress/season-2-1.snap") ? "the backup does not list save 1"
                        : null;
                }
                Check("C3a: ★ after --through's save: `progress` names season 2, the next day, the seed, save 1 and its snapshot " +
                      "(listed by its hash); the published counters did not move and the held ones are recorded; no season file was written",
                      firstFault is null, firstFault ?? Inv($"next day {Day(dec10.AddDays(1))}"));
                Check("C3b: a second save is save 2 under a new name; save 1 stays on disk and the backup still lists it",
                      secondFault is null, secondFault ?? "season-2-2.snap listed, season-2-1.snap kept");

                //  One byte changed in the snapshot of a copy: the open refuses it.
                CopyCareer("c3", "c3-byte");
                var snapFile = HistoryStore.ResolveListed(CareerOf("c3-byte"), "progress/season-2-2.snap");
                var sb = File.ReadAllBytes(snapFile); sb[sb.Length / 2] ^= 0x01; File.WriteAllBytes(snapFile, sb);
                var beforeByte = Folder("c3-byte");
                HistoryError? openErr = null;
                try { using var s = OpenCareer(CareerOf("c3-byte"), mte); }
                catch (HistoryException hx) { openErr = hx.Error; }
                Check("C3c: one byte changed in the snapshot → the career is refused at open (ListedFileChanged), nothing loaded, " +
                      "the folder untouched", openErr == HistoryError.ListedFileChanged && SeasonSnapshotDiff(beforeByte, Folder("c3-byte")) is null,
                      openErr?.ToString() ?? "OPENED");

                var got = ResumeToClose("c3", mte);
                var after = CareerJson(CareerOf("c3"));
                var fault = ResumeFault(ref2, ref2Page, "ref2", "c3", got);
                Check("C3d: resumed to the close: identical to the season played straight through; `progress` null and no snapshot listed",
                      fault is null && after["progress"] is null && !Listed(CareerOf("c3")).Any(x => x.Path.StartsWith("progress/", StringComparison.Ordinal)),
                      fault ?? "closed");
                Wipe("c3"); Wipe("c3-byte");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — THE REFUSALS.
            // ════════════════════════════════════════════════════════════════════
            {
                CopyCareer("master", "c4base");
                using (var store = OpenCareer(CareerOf("c4base"), mte))
                using (var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store))))
                {
                    run.AdvanceThrough(dec10);
                    run.SaveProgress();
                }
                (bool Refused, bool Unchanged, string Said) Case(string name, Func<string> attempt, Func<string, bool> refusedBy)
                {
                    CopyCareer("c4base", name);
                    var b = Folder(name);
                    var said = attempt();
                    return (refusedBy(said), SeasonSnapshotDiff(b, Folder(name)) is null, said);
                }
                static string Caught(Action a)
                {
                    try { a(); return "NO REFUSAL"; }
                    catch (HistoryException hx) { return "[" + hx.Error + "] " + hx.Message; }
                    catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
                }

                var seed = Case("c4-seed", () => SeasonChild(mtePath, seedTwo + 1, "c4-seed").Output,
                    s => s.Contains(Inv($"under seed {seedTwo}"), StringComparison.Ordinal) && s.Contains(Inv($"given seed {seedTwo + 1}"), StringComparison.Ordinal)
                         && !s.Contains("Resuming", StringComparison.Ordinal));
                Check("C4a: a resume with another seed is refused by name, naming both seeds; nothing played, the career byte-identical",
                      seed.Refused && seed.Unchanged, Short(seed.Said.Trim()));

                var editedConfig = Path.Combine(scratch, "config-edited.json");
                File.WriteAllBytes(editedConfig, File.ReadAllBytes(configPath).Concat(new[] { (byte)'\n' }).ToArray());
                var settings = Case("c4-settings", () => Caught(() =>
                {
                    using var s = OpenCareer(CareerOf("c4-settings"), mte);
                    SeasonRun.Resume(s.ReadProgressSnapshot(), mte, s, editedConfig, verbose: false).Dispose();
                }), s => s.StartsWith("[ProgressRulesChanged]", StringComparison.Ordinal) && s.Contains("other settings", StringComparison.Ordinal));
                Check("C4b: a resume under other settings (the settings file with one byte added) is refused by name " +
                      "(ProgressRulesChanged); nothing played, the career byte-identical", settings.Refused && settings.Unchanged, Short(settings.Said));

                //  Another build: saved and resumed under the fixed-rules seam with different stamps.
                CopyCareer("master", "c4-stamp-base");
                using (HistoryStore.UseFixedRulesForTests(CheckFixedConfigSha, CheckFixedRulesStamp))
                using (var store = OpenCareer(CareerOf("c4-stamp-base"), mte))
                using (var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store))))
                {
                    run.AdvanceThrough(dec10);
                    run.SaveProgress();
                }
                CopyCareer("c4-stamp-base", "c4-stamp");
                var stampBefore = Folder("c4-stamp");
                string stampSaid;
                using (HistoryStore.UseFixedRulesForTests(CheckFixedConfigSha, new string('e', 64)))
                    stampSaid = Caught(() =>
                    {
                        using var s = OpenCareer(CareerOf("c4-stamp"), mte);
                        SeasonRun.Resume(s.ReadProgressSnapshot(), mte, s, configPath, verbose: false).Dispose();
                    });
                Check("C4c: a resume under another build stamp (the fixed-rules seam) is refused by name (ProgressRulesChanged); " +
                      "nothing played, the career byte-identical",
                      stampSaid.StartsWith("[ProgressRulesChanged]", StringComparison.Ordinal) && stampSaid.Contains("another build", StringComparison.Ordinal)
                      && SeasonSnapshotDiff(stampBefore, Folder("c4-stamp")) is null, Short(stampSaid));

                var noCareer = Child("season", mtePath, seedTwo.ToString(CultureInfo.InvariantCulture), "--through", Day(dec10));
                Check("C4d: --through with no career is refused by name before anything plays",
                      noCareer.Output.Contains("needs --history", StringComparison.Ordinal) && !noCareer.Output.Contains("games", StringComparison.Ordinal),
                      Short(noCareer.Output.Trim()));

                var early = Case("c4-early", () => SeasonChild(mtePath, seedTwo, "c4-early", dec10.AddDays(-5)).Output,
                    s => s.Contains("before season 2's next day", StringComparison.Ordinal) && !s.Contains("Resuming", StringComparison.Ordinal));
                Check("C4e: --through before the saved season's next day is refused by name; nothing played, the career byte-identical",
                      early.Refused && early.Unchanged, Short(early.Said.Trim()));

                var page = Case("c4-page", () =>
                {
                    var old = Console.Out; var w = new StringWriter(CultureInfo.InvariantCulture); Console.SetOut(w);
                    int code;
                    try { code = RunPeople(new[] { "people", mtePath, "--history", CareerOf("c4-page") }); } finally { Console.SetOut(old); }
                    return code.ToString(CultureInfo.InvariantCulture) + "|" + w;
                }, s => s.StartsWith("0|", StringComparison.Ordinal) && !s.Contains("ERROR", StringComparison.Ordinal));
                Check("C4f: a page (people) opened on a career holding a saved season reads its published seasons and leaves " +
                      "the career — `progress` and its snapshot included — exactly as it was",
                      page.Refused && page.Unchanged && CareerJson(CareerOf("c4-page"))["progress"] is JsonObject, Short(page.Said.Split('\n')[0]));
                foreach (var n in new[] { "c4-seed", "c4-settings", "c4-stamp", "c4-stamp-base", "c4-early", "c4-page" }) Wipe(n);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — NEGATIVE CONTROLS.
            // ════════════════════════════════════════════════════════════════════
            {
                //  (a) A restore that drops one field: one bracket's occupancy for a later round already filled.
                var dropped = (string?)null;
                var occ = ResumeToClose("c5-occupancy", mte, run =>
                {
                    foreach (var src in (System.Collections.IEnumerable)Get(run, "bracketSources")!)
                    {
                        var played = (bool[])Get(src, "_played")!;
                        var occupancy = (int[][])Get(src, "_occupancy")!;
                        for (var l = 0; l < played.Length && dropped is null; l++)
                            if (!played[l] && (occupancy[l][0] != 0 || occupancy[l][1] != 0))
                            { occupancy[l] = new[] { 0, 0 }; dropped = Inv($"{src.GetType().GetProperty("Name")!.GetValue(src)} game {l}"); }
                        if (dropped is not null) break;
                    }
                });
                var occFault = ResumeFault(ref2, ref2Page, "ref2", "c5-occupancy", occ);
                Check("C5a: NEGATIVE CONTROL — a restore that drops one bracket's occupancy (a later round already filled) turns " +
                      "the comparison red, naming where", dropped is not null && occFault is not null && !occFault.Contains(SabotageBroke, StringComparison.Ordinal),
                      (dropped ?? "NOTHING DROPPED") + " → " + Short(occFault ?? "STILL IDENTICAL"));

                //  (b) The shared results list restored as a copy (stock, cut mid-February, before any tournament seeds).
                var copied = 0;
                var shared = ResumeToClose("c5-shared", stock, run =>
                {
                    var cache = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
                    foreach (var src in (System.Collections.IEnumerable)Get(run, "confTourneySources")!)
                    {
                        var list = Get(src, "_resultsByOrdinal")!;
                        if (!cache.TryGetValue(list, out var copy)) cache[list] = copy = new List<SeasonGameResult?>((IEnumerable<SeasonGameResult?>)list);
                        Set(src, "_resultsByOrdinal", copy);
                        copied++;
                    }
                });
                var sharedFault = ResumeFault(sref, srefPage, "sref", "c5-shared", shared);
                Check("C5b: NEGATIVE CONTROL — the results list every conference tournament reads, restored as a copy (stock, " +
                      Inv($"cut {Day(stockFeb)}, before any tournament seeds), turns the comparison red"),
                      copied > 0 && sharedFault is not null && !sharedFault.Contains(SabotageBroke, StringComparison.Ordinal), Inv($"{copied} tournaments → ") + Short(sharedFault ?? "STILL IDENTICAL"));

                //  (c) A dictionary restored in reverse order where the season reads it in order. Every dictionary the
                //    season changes during play (the gate's table) is a candidate; the first whose reversal turns the
                //    comparison red is the control. If none does, the control is NOT CONSTRUCTIBLE and is recorded as
                //    such on the page — never counted as a pass.
                string? reversedName = null, reversedFault = null;
                var tried = new List<string>();
                foreach (var (owner, field) in new[]
                         {
                             ("league", "PlayerSeasons"), ("league", "PlayerUsage"), ("league", "TeamUsage"),
                             ("league", "OtherByLabel"), ("league", "XCredits"), ("", "secondsByPool"), ("", "teamGames"),
                             ("", "wins"), ("", "losses"),
                         })
                {
                    CopyCareer("c5-reverse", "c5-rev-try");
                    var got = ResumeToClose("c5-rev-try", mte, run =>
                    {
                        var host = owner.Length == 0 ? run : Get(run, owner)!;
                        var d = (System.Collections.IDictionary)Get(host, field)!;
                        var entries = new List<(object Key, object? Value)>();
                        var e = d.GetEnumerator();
                        while (e.MoveNext()) entries.Add((e.Key, e.Value));
                        entries.Reverse();
                        var comparer = d.GetType().GetProperty("Comparer")!.GetValue(d);
                        var fresh = (System.Collections.IDictionary)Activator.CreateInstance(d.GetType(), comparer)!;
                        foreach (var (k, v) in entries) fresh.Add(k, v);
                        Set(host, field, fresh);
                    });
                    var name = (owner.Length == 0 ? "" : owner + ".") + field;
                    var f = ResumeFault(ref2, ref2Page, "ref2", "c5-rev-try", got);
                    tried.Add(name + (f is null ? " (no effect)" : f.Contains(SabotageBroke, StringComparison.Ordinal) ? " (control broke)" : " (red)"));
                    if (f is not null && !f.Contains(SabotageBroke, StringComparison.Ordinal)) { reversedName = name; reversedFault = f; break; }
                }
                if (reversedFault is not null)
                    Check("C5c: NEGATIVE CONTROL — a dictionary the season reads in order, restored in reverse order (cut Christmas), " +
                          "turns the comparison red", true, reversedName + " → " + Short(reversedFault) + " (tried " + string.Join(", ", tried) + ")");
                else
                    Console.WriteLine("  [page] C5c NOT CONSTRUCTIBLE — no dictionary the season changes during play is read in its " +
                                      "insertion order by anything the season writes or prints (each reversed, each identical): " + string.Join(", ", tried));

                //  (d) The walker refuses, by name and path, a planted world object and a planted delegate.
                var header = new SeasonSnapshotHeader("x", "x", "x", 0, "x", 0, new ProgressHeld(1, 1, 1));
                string Plant(object payload)
                {
                    try { SeasonSnapshot.Write(header, new SnapshotPlantHolder { Payload = payload }, SeasonSnapshot.IsRebound, SeasonSnapshot.Reachable(mte)); return "NO REFUSAL"; }
                    catch (InvalidOperationException ex) { return ex.Message; }
                }
                var plantedSchool = Plant(mte.Schools[0]);
                var plantedDelegate = Plant(new Action(() => { }));
                Check("C5d: the walker refuses a planted world school, by name and path",
                      plantedSchool.Contains("belongs to the world", StringComparison.Ordinal) && plantedSchool.Contains("SnapshotPlantHolder.Payload", StringComparison.Ordinal),
                      Short(plantedSchool));
                Check("C5e: the walker refuses a planted delegate, by name and path",
                      plantedDelegate.Contains("a delegate", StringComparison.Ordinal) && plantedDelegate.Contains("SnapshotPlantHolder.Payload", StringComparison.Ordinal),
                      Short(plantedDelegate));
                foreach (var n in new[] { "c5-occupancy", "c5-shared", "c5-reverse", "c5-rev-try" }) Wipe(n);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — PAGE-ONLY.
            // ════════════════════════════════════════════════════════════════════
            if (marchSave is not null)
            {
                var layout = SeasonSnapshot.Layout(marchSave);
                Console.WriteLine(Inv($"  [page] a stock save made the morning the conference tournaments open ({Day(ctOpen)}): ") +
                                  Inv($"{marchSave.Length / 1048576.0:F1} MB, {layout.TableAt.Count:N0} objects, saved in {marchSaveSeconds:F1}s, ") +
                                  Inv($"loaded and checked in {marchLoadSeconds:F1}s"));
                static string Nice(Type t) => t.IsArray ? Nice(t.GetElementType()!) + "[]"
                    : !t.IsGenericType ? t.Name
                    : t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(",", t.GetGenericArguments().Select(Nice)) + ">";
                Console.WriteLine("  [page] every type the walker met: " + string.Join(", ", layout.Types.Select(t =>
                    Type.GetType(t.Name) is { } tt ? Nice(tt) : "?")));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — A SAVE THAT HASHES RIGHT BUT IS WRONG INSIDE.
            // ════════════════════════════════════════════════════════════════════
            {
                CopyCareer("master", "c7base");
                using (var store = OpenCareer(CareerOf("c7base"), mte))
                using (var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store))))
                {
                    run.AdvanceThrough(dec10);
                    run.SaveProgress();
                }
                byte[] other;
                CopyCareer("master", "c7other");
                using (var store = OpenCareer(CareerOf("c7other"), mte))
                using (var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store))))
                {
                    run.AdvanceThrough(dec10.AddDays(10));
                    other = run.SaveSnapshot();
                }
                const string snapName = "progress/season-2-1.snap";
                var good = File.ReadAllBytes(HistoryStore.ResolveListed(CareerOf("c7base"), snapName));
                var lay = SeasonSnapshot.Layout(good);
                static byte[] Varint(int v) { using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms)) w.Write7BitEncodedInt(v); return ms.ToArray(); }
                static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
                byte[] Patch(byte[] src, long from, string find, string replace)
                {
                    var f = Encoding.ASCII.GetBytes(find); var r = Encoding.ASCII.GetBytes(replace);
                    var copy = (byte[])src.Clone();
                    var at = copy.AsSpan((int)from).IndexOf(f);
                    if (at < 0 || f.Length != r.Length) return copy;
                    r.CopyTo(copy, (int)from + at);
                    return copy;
                }
                var n = lay.TableAt.Count;
                var typed = lay.Types.First(t => t.Name.Contains("Charm.", StringComparison.Ordinal));
                var cases = new (string Label, byte[] Bytes, HistoryError Want, string Phrase)[]
                {
                    ("a truncated body", good[..^200], HistoryError.SnapshotMalformed, "ends before"),
                    ("a dangling reference (the last object dropped)",
                     Cat(good[..(int)lay.ObjectCountAt], Varint(n - 1), good[(int)lay.TableAt[0]..(int)lay.TableAt[n - 1]],
                         good[(int)lay.ContentAt[0]..(int)lay.ContentAt[n - 1]]),
                     HistoryError.SnapshotMalformed, "dangling"),
                    ("an unknown type name", Patch(good, typed.At, "Charm.", "Charq."), HistoryError.SnapshotMalformed, "does not have"),
                    ("a length past the end",
                     Cat(good[..(int)lay.ObjectCountAt], Varint(1_000_000_000), good[(int)lay.TableAt[0]..]),
                     HistoryError.SnapshotMalformed, "runs past the end"),
                    ("a header whose next day disagrees with the block", Patch(good, 0, Day(dec10.AddDays(1)), Day(dec10.AddDays(2))),
                     HistoryError.SnapshotMismatch, "next day"),
                    ("a body whose restored next day disagrees (another day's body under this header)",
                     Cat(good[..lay.BodyAt], other[SeasonSnapshot.ReadHeader(other).BodyAt..]),
                     HistoryError.SnapshotMismatch, "restored season"),
                };
                var faults = new List<string>();
                foreach (var (label, bytes, want, phrase) in cases)
                {
                    CopyCareer("c7base", "c7");
                    File.WriteAllBytes(HistoryStore.ResolveListed(CareerOf("c7"), snapName), bytes);
                    RelistCareerForCheck(CareerOf("c7"));
                    var before = Folder("c7");
                    HistoryException? got = null;
                    string? other2 = null;
                    try
                    {
                        using var s = OpenCareer(CareerOf("c7"), mte);
                        SeasonRun.Resume(s.ReadProgressSnapshot(), mte, s, configPath, verbose: false).Dispose();
                    }
                    catch (HistoryException hx) { got = hx; }
                    catch (Exception ex) { other2 = ex.GetType().Name + ": " + ex.Message; }
                    var unchanged = SeasonSnapshotDiff(before, Folder("c7")) is null;
                    if (got is null || got.Error != want || !got.Message.Contains(phrase, StringComparison.Ordinal) || !unchanged)
                        faults.Add(label + ": " + (got is null ? Short(other2 ?? "NO REFUSAL") : "[" + got.Error + "] " + Short(got.Message)) + (unchanged ? "" : " (career changed)"));
                }
                Check("C7a: ★ a save that hashes right but is wrong inside — " + string.Join("; ", cases.Select(c => c.Label)) +
                      " — each refused by its own name (SnapshotMalformed / SnapshotMismatch), nothing played, the career byte-identical",
                      faults.Count == 0, faults.Count > 0 ? string.Join(" | ", faults.Take(3)) : Inv($"{cases.Length} cases"));

                CopyCareer("c7base", "c7");
                RelistCareerForCheck(CareerOf("c7"));
                var control = (string?)null;
                try
                {
                    using var s = OpenCareer(CareerOf("c7"), mte);
                    using var r = SeasonRun.Resume(s.ReadProgressSnapshot(), mte, s, configPath, verbose: false);
                    Quiet(() => { r.AdvanceThrough(r.NextDay); return 0; });
                }
                catch (Exception ex) { control = ex.Message; }
                Check("C7b: control — the same re-listing of a sound save resumes and plays on", control is null, Short(control ?? "resumed"));
                foreach (var nm in new[] { "c7", "c7base", "c7other" }) Wipe(nm);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — SAVES AT ILLEGAL MOMENTS.
            // ════════════════════════════════════════════════════════════════════
            {
                string Try(SeasonRun r)
                {
                    try { r.SaveProgress(); return "NO REFUSAL"; }
                    catch (Exception ex) { return ex.Message; }
                }
                bool NoSave(string name) => CareerJson(CareerOf(name))["progress"] is null
                                            && !Directory.Exists(HistoryStore.SiblingFolderFor(CareerOf(name), "progress"));

                //  From inside a day: the month line.
                CopyCareer("master", "c8a");
                var inDay = new List<string>();
                SortedDictionary<string, string> beforeA;
                using (var store = OpenCareer(CareerOf("c8a"), mte))
                {
                    beforeA = Folder("c8a");
                    using var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store)));
                    var cal = Get(run, "calendar")!;
                    Set(cal, "_monthDone", new Action<string, int>((_, _) => inDay.Add(Try(run))));
                    run.AdvanceThrough(new DateOnly(mteFirst.Year, 12, 31));
                }
                Check("C8a: a save from inside a day (the month line) is refused by name; nothing written, nothing published",
                      inDay.Count > 0 && inDay.All(s => s.Contains("being played", StringComparison.Ordinal))
                      && SeasonSnapshotDiff(beforeA, Folder("c8a")) is null && NoSave("c8a"),
                      Short(inDay.FirstOrDefault() ?? "THE MONTH LINE NEVER FIRED"));

                //  From inside the close (its step seam), then after the close.
                CopyCareer("master", "c8b");
                var inClose = new List<string>();
                string afterClose;
                SortedDictionary<string, string> closed;
                using (var store = OpenCareer(CareerOf("c8b"), mte))
                {
                    SeasonRun? runRef = null;
                    using var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false,
                        Opts(store, step => { if (step == "names" && runRef is not null) inClose.Add(Try(runRef)); })));
                    runRef = run;
                    run.AdvanceThrough(run.LastLegalDay);
                    Quiet(() => run.Finish());
                    closed = Folder("c8b");
                    afterClose = Try(run);
                }
                Check("C8b: a save from inside the close (its step seam) is refused by name; the close then publishes exactly " +
                      "the season played straight through, with no `progress`",
                      inClose.Count == 1 && inClose[0].Contains("being played", StringComparison.Ordinal)
                      && CareerFault("ref2", "c8b") is null && NoSave("c8b"),
                      Short(inClose.FirstOrDefault() ?? "THE SEAM NEVER FIRED") + " / " + (CareerFault("ref2", "c8b") ?? "identical"));
                Check("C8c: a save after the close is refused by name; nothing written",
                      afterClose.Contains("has closed", StringComparison.Ordinal) && SeasonSnapshotDiff(closed, Folder("c8b")) is null && NoSave("c8b"),
                      Short(afterClose));

                //  After a fault.
                CopyCareer("master", "c8c");
                string afterFault;
                SortedDictionary<string, string> beforeC;
                using (var store = OpenCareer(CareerOf("c8c"), mte))
                {
                    beforeC = Folder("c8c");
                    using var run = Quiet(() => SeasonBegin(mte, seedTwo, configPath, verbose: false, Opts(store)));
                    ((SeasonCalendarRun)Get(run, "calendar")!).Rebind(
                        _ => throw new InvalidOperationException("CONSTRUCTED ENGINE FAULT"), null);
                    try { run.AdvanceThrough(mteFirst.AddDays(5)); } catch (InvalidOperationException) { /* the constructed fault */ }
                    afterFault = Try(run);
                }
                Check("C8d: a save after a fault is refused by name; nothing written, nothing published",
                      afterFault.Contains("failed on", StringComparison.Ordinal) && SeasonSnapshotDiff(beforeC, Folder("c8c")) is null && NoSave("c8c"),
                      Short(afterFault));
                foreach (var nm in new[] { "c8a", "c8b", "c8c" }) Wipe(nm);
            }

            foreach (var t in timings) Console.WriteLine("  [page] " + t);
        }
        catch (Exception ex)
        {
            Check("Phase 118 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 118 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }
}
