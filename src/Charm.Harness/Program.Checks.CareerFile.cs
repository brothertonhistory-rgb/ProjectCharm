using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 116 — S125: THE CAREER FILE BECOMES TRUSTWORTHY (save design B1b).
//
//  The career file now lists every file the career owns with each one's SHA-256,
//  ends with a fingerprint line of its own, is saved in one swap with the previous
//  save kept as a backup, records each season's rules, and is born with the league
//  state. This phase proves what that buys.
//
//    C1  no change — two career seasons through the new path: the game blocks and
//        event records are the S124 build's (captured from it, portable), and the
//        numbers spent are the S124 build's.
//    C2  the list is true — after each close it names exactly the files on disk and
//        every hash matches; the rules are recorded; a listed file deleted, and one
//        with one byte changed, are each refused BY NAME at the next open, and by the
//        `season` command before any season plays, nothing written. Control: the same
//        change to an unlisted file is not refused.
//    C3  ★ one-step save, the backup, recovery — a save interrupted after `.new` is
//        written leaves the career file and its backup as they were and plays again
//        identically in a fresh process; after two closes the backup is the first
//        season's save; a torn career file is recovered from a WHOLE backup (and a
//        backup whose files are damaged refuses); interrupted recoveries re-run; a
//        valid career with a damaged file is refused, never rolled back; a recovered
//        career plays its lost season again identically; a listed name is never
//        written again (K8).
//    C3b creation — a stray league-0 from a creation that died is set aside; a folder
//        with a backup and no career file is recovered, never re-created.
//    C3c the contract — every malformed list, re-sealed with a correct fingerprint
//        line, refused by its own error. Control: re-sealed unchanged, it loads.
//    C4  older careers — a v3 career written by the S124 build is refused by name at
//        `season` and at each of the five pages, byte-untouched.
//    C5  the league state — born with the career, written at every close, equal to
//        the world; a world edited in memory after open is refused by name.
//    C6  page-only: verification time per open, the career file's size.
//
//  All on fixture-mte, under the fixed history id, with the suite's season seeds.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ CAPTURED FROM THE S124 BUILD: two fixture-mte career seasons under the fixed history
    /// id and the suite's seeds — each season's decoded game blocks (<see cref="CalendarBlocksDigest"/>)
    /// and event-record bytes, as "blocks-1=…\nevents-1=…\nblocks-2=…\nevents-2=…", hashed. Integers,
    /// dates and strings only, so the same seasons hash the same on any machine.</summary>
    private const string CareerFileS124SeasonFiles =
        "93fd6f4c41bca508f5a4fe5f6f5095a42b97a92efc3aa6c72d0710e3231cfb71";

    /// <summary>★ CAPTURED FROM THE S124 BUILD: the numbers those two seasons spent.</summary>
    private static readonly HistoryCounters CareerFileS124Counters = new(426, 3, 783);

    private static string Inv116(FormattableString f) => FormattableString.Invariant(f);

    private static bool Phase116CareerFileCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 116 — S125: the career file becomes trustworthy. It lists every file the career owns by hash " +
                          "and carries its own; a save is one swap with the last save kept as a backup, recovered from when the " +
                          "career file is torn; each season records its rules; the league state is born with the career ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static HistoryException? RefusalOf(Action a)
        {
            try { a(); return null; }
            catch (HistoryException hx) { return hx; }
        }
        static string? Thrown(Action a)
        {
            try { a(); return null; }
            catch (Exception ex) { return ex.Message; }
        }
        static string Capture(Func<int> run)
        {
            var old = Console.Out;
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Console.SetOut(sw);
            try { run(); } finally { Console.SetOut(old); }
            return sw.ToString();
        }
        static string Short(string? s) => s is null ? "NONE" : s[..Math.Min(150, s.Length)];
        var timings = new List<string>();

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s125-" + Guid.NewGuid().ToString("N"));
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
            SortedDictionary<string, string> Outside(string name)
            {
                var s = Snap(name);
                foreach (var k in s.Keys.Where(k => k.StartsWith("career.orphans/", StringComparison.Ordinal)).ToList()) s.Remove(k);
                return s;
            }
            SeasonRunOutcome PlayInProcess(string name, long seed, WorldFile? world = null)
            {
                using var store = OpenCareer(CareerOf(name), mte);
                using var run = SeasonBegin(world ?? mte, seed, configPath, verbose: false,
                                            new SeasonRunOptions(History: store, RetainGameLog: true));
                run.AdvanceThrough(run.LastLegalDay);
                return run.Finish();
            }
            //  The `season` command in a FRESH PROCESS, bound to the career (Phase 115's form).
            (int Exit, string Output) Child(string name, long seed)
            {
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
                using var proc = Process.Start(psi)!;
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(15 * 60 * 1000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    return (-1, "TIMED OUT");
                }
                return (proc.ExitCode, outTask.Result + errTask.Result);
            }
            HistoryError? OpenError(string name) => RefusalOf(() => OpenCareer(CareerOf(name), mte).Dispose())?.Error;
            string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

            // ════════════════════════════════════════════════════════════════════
            //  The reference: a new career; season one ("master"); season two ("two").
            // ════════════════════════════════════════════════════════════════════
            Directory.CreateDirectory(Dir("fresh"));
            using (OpenCareer(CareerOf("fresh"), mte)) { }
            CopyDir("fresh", "master");
            var outOne = PlayInProcess("master", seedOne);
            CopyDir("master", "two");
            var outTwo = PlayInProcess("two", seedTwo);
            var masterCareer = File.ReadAllBytes(CareerOf("master"));
            var twoCareer = File.ReadAllBytes(CareerOf("two"));
            var twoSnap = Snap("two");

            // ════════════════════════════════════════════════════════════════════
            //  C1 — NO CHANGE.
            // ════════════════════════════════════════════════════════════════════
            {
                var lines = new List<string>();
                foreach (var n in new[] { 1L, 2L })
                {
                    var log = GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(CareerOf("two"), n),
                                                          new GameLogBindings(CalendarFixedHistoryId, fp, n));
                    lines.Add(Inv116($"blocks-{n}=") + CalendarBlocksDigest(log));
                    lines.Add(Inv116($"events-{n}=") + Hex(File.ReadAllBytes(P("two", Inv116($"career.events/season-{n}.json")))));
                }
                var digest = Hex(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
                Check("C1a: ★ two career seasons through the new close: every game block (decoded) and both event records are the " +
                      "S124 build's (captured from it) — saving moved no basketball",
                      digest == CareerFileS124SeasonFiles, digest);
                var counters = PeekState(CareerOf("two"));
                Check("C1b: the numbers the two seasons spent are the S124 build's — the next person, season and game",
                      counters == CareerFileS124Counters,
                      Inv116($"{counters.NextPersonId}/{counters.NextSeasonId}/{counters.NextGameId}"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — THE LIST IS TRUE.
            // ════════════════════════════════════════════════════════════════════
            {
                //  After each close the list names exactly the files on disk — outside the lock sidecars, the
                //  backup and the orphans folder — and every hash matches.
                string? ListFault(string name)
                {
                    using var store = OpenCareer(CareerOf(name), mte);
                    var listed = store.CareerFiles.ToDictionary(
                        f => Path.GetRelativePath(Dir(name), HistoryStore.ResolveListed(CareerOf(name), f.Path)).Replace('\\', '/'),
                        f => f.Sha256);
                    var onDisk = Snap(name).Where(kv => kv.Key != "career.json" && kv.Key != "career.json.bak"
                                                        && !kv.Key.EndsWith(".lock", StringComparison.Ordinal)
                                                        && !kv.Key.StartsWith("career.orphans/", StringComparison.Ordinal))
                                           .ToDictionary(kv => kv.Key, kv => kv.Value);
                    if (!listed.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(onDisk.Keys.OrderBy(k => k, StringComparer.Ordinal)))
                        return $"listed [{string.Join(", ", listed.Keys)}] vs on disk [{string.Join(", ", onDisk.Keys)}]";
                    foreach (var (rel, sha) in listed)
                        if (onDisk[rel] != sha) return rel + " hash differs";
                    return null;
                }
                var f1 = ListFault("master");
                var f2 = ListFault("two");
                int listedTwo;
                using (var s = OpenCareer(CareerOf("two"), mte)) listedTwo = s.CareerFiles.Count;
                Check("C2a: ★ after each close the career lists exactly the files on disk (outside its lock sidecars, its backup and " +
                      "the orphans folder), every hash matching — season one's 5, season two's 9",
                      f1 is null && f2 is null && listedTwo == 9, f1 ?? f2 ?? Inv116($"{listedTwo} files listed after season two"));

                //  The rules record: one entry per season, the first legal day, the settings file as read, this build.
                IReadOnlyList<SeasonEntry> seasons;
                using (var s = OpenCareer(CareerOf("two"), mte)) seasons = s.Seasons;
                var configSha = Hex(File.ReadAllBytes(configPath));
                var rulesOk = seasons.Count == 2
                              && seasons.Select(e => e.Season).SequenceEqual(new[] { 1L, 2L })
                              && seasons.All(e => e.Rules.Count == 1 && e.Rules[0].ConfigSha256 == configSha
                                                  && e.Rules[0].RulesStamp == RulesStamp())
                              && seasons[0].Rules[0].FromDay == outOne.FirstLegalDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                              && seasons[1].Rules[0].FromDay == outTwo.FirstLegalDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Check("C2b: each season records one rules entry — its first legal day, the SHA-256 of the settings file as read, and " +
                      "this build's stamp",
                      rulesOk, string.Join("; ", seasons.Select(e => Inv116($"season {e.Season} from {e.Rules[0].FromDay}"))));

                //  A listed file deleted, and one with one byte changed: refused by name at open, nothing written.
                foreach (var (label, rel, want) in new[]
                {
                    ("deleted", "career.gamelog/season-1.scout", HistoryError.ListedFileMissing),
                    ("changed", "career.events/season-2.json", HistoryError.ListedFileChanged),
                })
                {
                    var name = "c2-" + label;
                    CopyDir("two", name);
                    if (label == "deleted") File.Delete(P(name, rel));
                    else { var b = File.ReadAllBytes(P(name, rel)); b[b.Length / 2] ^= 0x01; File.WriteAllBytes(P(name, rel), b); }
                    var before = Snap(name);
                    var refusal = RefusalOf(() => OpenCareer(CareerOf(name), mte).Dispose());
                    var page = Capture(() => { RunSeason(configPath, new[] { "season", worldPath,
                        seedThree.ToString(CultureInfo.InvariantCulture), "--history", CareerOf(name) }); return 0; });
                    var after = Snap(name);
                    var listedName = rel.Replace("career.", "");
                    Check(Inv116($"C2c {label}: ★ a listed file {label} is refused by name at the next open ({want}), naming it; the ") +
                          "`season` command refuses before any season plays; nothing written",
                          refusal?.Error == want && refusal.Message.Contains(listedName, StringComparison.Ordinal)
                          && page.Contains("SEASON ERROR [" + want + "]", StringComparison.Ordinal)
                          && !page.Contains("Dated: season", StringComparison.Ordinal)
                          && SeasonSnapshotDiff(before, after) is null,
                          refusal is null ? "NOT REFUSED" : Short(refusal.Message));
                }
                CopyDir("two", "c2-notes");
                File.WriteAllText(P("c2-notes", "career.events/notes.txt"), "an unlisted file");
                var okBefore = OpenError("c2-notes");
                File.WriteAllText(P("c2-notes", "career.events/notes.txt"), "an unlisted file, changed");
                var okAfter = OpenError("c2-notes");
                Check("C2d: NEGATIVE CONTROL — an unlisted file beside the season files, then changed, is never refused",
                      okBefore is null && okAfter is null, Inv116($"{okBefore}/{okAfter}"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — ★ ONE-STEP SAVE, THE BACKUP, AND RECOVERY.
            // ════════════════════════════════════════════════════════════════════
            {
                var bak = ".bak";
                byte[]? Bytes(string name, string suffix = "") =>
                    File.Exists(CareerOf(name) + suffix) ? File.ReadAllBytes(CareerOf(name) + suffix) : null;
                static bool Same(byte[]? a, byte[]? b) => a is not null && b is not null && a.AsSpan().SequenceEqual(b);

                //  The first save interrupted after `.new` is written: the new career's file whole, no backup yet.
                CopyDir("fresh", "c3-first");
                var freshCareer = Bytes("fresh");
                string? why;
                using (HistoryStore.UsePublishStepForTests(s => { if (s == "new-written") throw new InvalidOperationException("CONSTRUCTED INTERRUPTION at " + s); }))
                    why = Thrown(() => PlayInProcess("c3-first", seedOne));
                var firstUntouched = Same(Bytes("c3-first"), freshCareer) && Bytes("c3-first", bak) is null
                                     && File.Exists(CareerOf("c3-first") + ".new");
                var child = Child("c3-first", seedOne);
                var replayDiff = SeasonSnapshotDiff(Snap("master"), Outside("c3-first"));
                Check("C3a: ★ the first save interrupted after `.new` is written and before the swap: the career file is byte-identical, " +
                      "no backup exists; a FRESH PROCESS plays the season again and every file outside the orphans folder — the career " +
                      "file and its new backup included — equals the uninterrupted career's",
                      why is not null && why.Contains("CONSTRUCTED INTERRUPTION at new-written", StringComparison.Ordinal)
                      && firstUntouched && child.Exit == 0 && !child.Output.Contains("SEASON ERROR", StringComparison.Ordinal)
                      && replayDiff is null,
                      !firstUntouched ? "the career file or its backup moved" : replayDiff ?? Short(why));

                //  The same, on a later save: the backup is the previous one, untouched.
                CopyDir("master", "c3-later");
                var masterBak = Bytes("master", bak);
                using (HistoryStore.UsePublishStepForTests(s => { if (s == "new-written") throw new InvalidOperationException("CONSTRUCTED INTERRUPTION at " + s); }))
                    Thrown(() => PlayInProcess("c3-later", seedTwo));
                Check("C3b: a later save interrupted the same way leaves the career file AND its backup exactly the previous save's",
                      Same(Bytes("c3-later"), masterCareer) && Same(Bytes("c3-later", bak), masterBak),
                      Inv116($"{Bytes("c3-later")?.Length} bytes"));

                Check("C3c: ★ after two closes the backup is the career file as season one left it, byte for byte; after one close it " +
                      "is the career file as it was born",
                      Same(Bytes("two", bak), masterCareer) && Same(Bytes("master", bak), freshCareer));

                //  Recovery: the career file torn mid-file.
                CopyDir("two", "c3-torn");
                var torn = twoCareer[..(twoCareer.Length / 2)];
                File.WriteAllBytes(CareerOf("c3-torn"), torn);
                var report = new List<string>();
                using (var s = OpenCareer(CareerOf("c3-torn"), mte)) report.AddRange(s.OpenReport);
                var tornKept = P("c3-torn", "career.orphans/career.json");
                Check("C3d: ★ a career file torn mid-file is recovered from the backup at the next open, which says so; the torn file " +
                      "is in the orphans folder byte for byte; afterwards the career file and the backup are identical, and the backup is " +
                      "untouched",
                      report.Any(l => l.Contains("Recovered the career from its backup", StringComparison.Ordinal))
                      && File.Exists(tornKept) && Same(File.ReadAllBytes(tornKept), torn)
                      && Same(Bytes("c3-torn"), masterCareer) && Same(Bytes("c3-torn", bak), masterCareer),
                      string.Join(" / ", report));
                //  It is now at season one; its lost save had published season two. The next close sets those files aside
                //  and season two plays again, identical — through the `season` command, which prints the recovery.
                CopyDir("two", "c3-replay");
                File.WriteAllBytes(CareerOf("c3-replay"), torn);
                var replayPage = Capture(() => { RunSeason(configPath, new[] { "season", worldPath,
                    seedTwo.ToString(CultureInfo.InvariantCulture), "--history", CareerOf("c3-replay") }); return 0; });
                var replayOrphans = Snap("c3-replay").Keys.Where(k => k.StartsWith("career.orphans/", StringComparison.Ordinal)).ToList();
                var wantOrphans = new[]
                {
                    "career.orphans/career.events/season-2.json", "career.orphans/career.gamelog/season-2.log",
                    "career.orphans/career.gamelog/season-2.scout", "career.orphans/career.json",
                    "career.orphans/career.league/league-2.json",
                };
                var replayed = SeasonSnapshotDiff(twoSnap, Outside("c3-replay"));
                Check("C3e: ★ a recovered career whose lost save had published season two: the `season` command prints the recovery, " +
                      "sets season two's files aside, and plays season two again — every file outside the orphans folder equals the " +
                      "uninterrupted career's, and the orphans folder holds the torn file and season two's four files",
                      replayPage.Contains("Recovered the career from its backup", StringComparison.Ordinal)
                      && !replayPage.Contains("SEASON ERROR", StringComparison.Ordinal)
                      && replayed is null && replayOrphans.SequenceEqual(wantOrphans),
                      replayed ?? "[" + string.Join(", ", replayOrphans) + "]");

                //  Both torn: refused by name, nothing written.
                CopyDir("two", "c3-both");
                File.WriteAllBytes(CareerOf("c3-both"), torn);
                File.WriteAllBytes(CareerOf("c3-both") + bak, masterCareer[..(masterCareer.Length / 3)]);
                var bothBefore = Snap("c3-both");
                var bothErr = OpenError("c3-both");
                Check("C3f: the career file and its backup both torn: refused by name (BackupUnusable), nothing written",
                      bothErr == HistoryError.BackupUnusable && SeasonSnapshotDiff(bothBefore, Snap("c3-both")) is null,
                      bothErr?.ToString() ?? "NOT REFUSED");

                //  The backup intact, one of the files it lists gone: recovery refused, the backup untouched.
                CopyDir("two", "c3-bakfile");
                File.Delete(CareerOf("c3-bakfile"));
                File.Delete(P("c3-bakfile", "career.gamelog/season-1.log"));
                var bakfileBefore = Snap("c3-bakfile");
                var bakfileErr = RefusalOf(() => OpenCareer(CareerOf("c3-bakfile"), mte).Dispose());
                Check("C3g: ★ the career file gone and the backup whole but one file it lists deleted: recovery is refused by name " +
                      "(BackupUnusable, naming the file), the backup untouched, nothing written",
                      bakfileErr?.Error == HistoryError.BackupUnusable
                      && bakfileErr.Message.Contains("gamelog/season-1.log", StringComparison.Ordinal)
                      && SeasonSnapshotDiff(bakfileBefore, Snap("c3-bakfile")) is null,
                      Short(bakfileErr?.Message));

                //  Recovery interrupted at each of its steps: the next open recovers.
                foreach (var step in new[] { "primary-set-aside", "new-written" })
                {
                    var name = "c3-int-" + step;
                    CopyDir("two", name);
                    File.WriteAllBytes(CareerOf(name), torn);
                    string? stopped;
                    using (HistoryStore.UseRecoveryStepForTests(s => { if (s == step) throw new InvalidOperationException("CONSTRUCTED INTERRUPTION at " + s); }))
                        stopped = Thrown(() => OpenCareer(CareerOf(name), mte).Dispose());
                    var midway = Bytes(name) is null && Same(Bytes(name, bak), masterCareer);
                    var again = OpenError(name);
                    Check(Inv116($"C3h {step}: a recovery interrupted at '{step}' leaves no career file and the backup whole; ") +
                          "the next open recovers, the career file and the backup identical",
                          stopped is not null && stopped.Contains("CONSTRUCTED INTERRUPTION", StringComparison.Ordinal)
                          && midway && again is null && Same(Bytes(name), masterCareer) && Same(Bytes(name, bak), masterCareer)
                          && !File.Exists(CareerOf(name) + ".new"),
                          !midway ? "midway state wrong" : again?.ToString() ?? Short(stopped));
                }

                //  Q6: a valid career file whose listed file is damaged is refused — never rolled back.
                CopyDir("two", "c3-q6");
                var victim = P("c3-q6", "career.gamelog/season-2.log");
                var vb = File.ReadAllBytes(victim); vb[vb.Length / 3] ^= 0x01; File.WriteAllBytes(victim, vb);
                var q6Err = OpenError("c3-q6");
                Check("C3i: ★ a career file that verifies but lists a damaged file is refused (ListedFileChanged) and NOT rolled back " +
                      "to the backup — the career file and the backup are exactly as they were",
                      q6Err == HistoryError.ListedFileChanged && Same(Bytes("c3-q6"), twoCareer) && Same(Bytes("c3-q6", bak), masterCareer),
                      q6Err?.ToString() ?? "NOT REFUSED");

                //  A stray `.new` is ignored on load and replaced by the next save.
                CopyDir("two", "c3-new");
                File.WriteAllText(CareerOf("c3-new") + ".new", "a stray, never authoritative");
                var newErr = OpenError("c3-new");
                PlayInProcess("c3-new", seedThree);
                Check("C3j: a stray `.new` is ignored on load, and the next save replaces it (none left after the swap)",
                      newErr is null && !File.Exists(CareerOf("c3-new") + ".new") && PeekState(CareerOf("c3-new")).NextSeasonId == 4,
                      newErr?.ToString() ?? "loaded");

                //  K8: a listed name is never written again.
                string? leagueAgain, publishAgain;
                using (var s = OpenCareer(CareerOf("two"), mte))
                {
                    leagueAgain = RefusalOf(() => s.WriteLeagueState(2, LeaguePrestigeOf(mte)))?.Error.ToString();
                    publishAgain = RefusalOf(() => s.PublishSeason(new[] { P("two", "career.events/season-1.json") }, null))?.Error.ToString();
                }
                Check("C3k: ★ K8 — a save that would write or list a name the career already lists is refused by name, and the career " +
                      "file is untouched",
                      leagueAgain == nameof(HistoryError.ListedNameTaken) && publishAgain == nameof(HistoryError.ListedNameTaken)
                      && Same(Bytes("two"), twoCareer),
                      Inv116($"{leagueAgain}/{publishAgain}"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3b — CREATION.
            // ════════════════════════════════════════════════════════════════════
            {
                Directory.CreateDirectory(P("c3b-stray", "career.league"));
                var stray = LeagueStateFile.Encode("ffffffffffffffffffffffffffffffff", 0, new[] { new SchoolPrestige(1, 1) });
                File.WriteAllBytes(P("c3b-stray", "career.league/league-0.json"), stray);
                File.WriteAllText(CareerOf("c3b-stray") + ".new", "a creation that died before its move");
                List<string> report;
                string id;
                using (var s = OpenCareer(CareerOf("c3b-stray"), mte)) { report = s.OpenReport.ToList(); id = s.HistoryId; }
                var kept = P("c3b-stray", "career.orphans/career.league/league-0.json");
                var born = LeagueStateFile.Read(P("c3b-stray", "career.league/league-0.json"), id, 0);
                Check("C3b-a: ★ a creation that died after writing league-0 (and a stray `.new`): the next open sets the stray league-0 " +
                      "aside byte for byte, says so, and creates a new career whose own league-0 is the world's",
                      File.Exists(kept) && File.ReadAllBytes(kept).AsSpan().SequenceEqual(stray)
                      && report.Any(l => l.Contains("Set aside a league state", StringComparison.Ordinal))
                      && born.Prestige.SequenceEqual(LeaguePrestigeOf(mte).OrderBy(p => p.SchoolId))
                      && !File.Exists(CareerOf("c3b-stray") + ".new") && PeekState(CareerOf("c3b-stray")).NextSeasonId == 1,
                      string.Join(" / ", report));

                CopyDir("two", "c3b-lost");
                File.Delete(CareerOf("c3b-lost"));
                var lostErr = OpenError("c3b-lost");
                Check("C3b-b: a folder with the backup and no career file is an existing career — recovered, never re-created",
                      lostErr is null && File.ReadAllBytes(CareerOf("c3b-lost")).AsSpan().SequenceEqual(masterCareer),
                      lostErr?.ToString() ?? Inv116($"next season {PeekState(CareerOf("c3b-lost")).NextSeasonId}"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3c — THE CONTRACT, on re-sealed career files.
            // ════════════════════════════════════════════════════════════════════
            {
                var payload = Encoding.UTF8.GetString(twoCareer[..(twoCareer.Length - 72)]);
                byte[] Reseal(Action<JsonObject> edit)
                {
                    var node = JsonNode.Parse(payload)!.AsObject();
                    edit(node);
                    var text = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
                    return SealCareer(text);
                }
                JsonArray Files(JsonObject o) => o["files"]!.AsArray();
                var cases = new (string Name, Action<JsonObject> Edit, HistoryError Want)[]
                {
                    ("a duplicate path", o => Files(o).Insert(0, Files(o)[0]!.DeepClone()), HistoryError.ListedPathDuplicate),
                    ("a '..' path", o => Files(o)[0]!["path"] = "gamelog/../season-1.log", HistoryError.ListedPathInvalid),
                    ("an absolute path", o => Files(o)[0]!["path"] = "/tmp/season-1.log", HistoryError.ListedPathInvalid),
                    ("the backup listed", o => Files(o).Insert(0, new JsonObject { ["path"] = "career.json.bak", ["sha256"] = new string('0', 64) }), HistoryError.ListedPathInvalid),
                    ("seasons out of order", o => { var s = o["seasons"]!.AsArray(); var first = s[0]!.DeepClone(); s.RemoveAt(0); s.Add(first); }, HistoryError.SeasonsInvalid),
                    ("league-0 removed", o => { var f = Files(o); f.RemoveAt(f.Select((x, i) => (x, i)).First(t => (string)t.x!["path"]! == "league/league-0.json").i); }, HistoryError.ManifestMismatch),
                    ("a bad hash string", o => Files(o)[0]!["sha256"] = ((string)Files(o)[0]!["sha256"]!).ToUpperInvariant(), HistoryError.ListedHashInvalid),
                    ("progress not null", o => o["progress"] = new JsonObject(), HistoryError.ProgressNotNull),
                };
                foreach (var (label, edit, want) in cases)
                {
                    var name = "c3c-" + label.Replace(' ', '-').Replace("'", "");
                    CopyDir("two", name);
                    File.WriteAllBytes(CareerOf(name), Reseal(edit));
                    var before = Snap(name);
                    var got = OpenError(name);
                    Check(Inv116($"C3c {label}: re-sealed with a correct fingerprint line, refused as {want}, nothing written"),
                          got == want && SeasonSnapshotDiff(before, Snap(name)) is null, got?.ToString() ?? "LOADED");
                }
                CopyDir("two", "c3c-control");
                File.WriteAllBytes(CareerOf("c3c-control"), Reseal(_ => { }));
                var control = OpenError("c3c-control");
                Check("C3c NEGATIVE CONTROL — the same file re-written with no change and re-sealed loads",
                      control is null, control?.ToString() ?? "loaded");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — OLDER CAREERS.
            // ════════════════════════════════════════════════════════════════════
            {
                var v3 = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tools", "history_v3_career", "career.json"));
                Directory.CreateDirectory(Dir("c4"));
                File.WriteAllBytes(CareerOf("c4"), v3);
                var h = CareerOf("c4");
                var outputs = new (string Command, string Output)[]
                {
                    ("season", Capture(() => { RunSeason(configPath, new[] { "season", worldPath, "1", "--history", h }); return 0; })),
                    ("player", Capture(() => RunPlayer(new[] { "player", worldPath, "1", "--history", h }))),
                    ("people", Capture(() => RunPeople(new[] { "people", worldPath, "--history", h }))),
                    ("gamelog", Capture(() => RunGameLog(new[] { "gamelog", worldPath, "1", "1", "--history", h }))),
                    ("box", Capture(() => RunBox(new[] { "box", worldPath, "1", "1", "--history", h }))),
                    ("day", Capture(() => RunDay(new[] { "day", worldPath, "2026-11-15", "--history", h }))),
                };
                var notRefused = outputs.Where(o => !o.Output.Contains(OlderCareerSentence, StringComparison.Ordinal)).Select(o => o.Command).ToList();
                var others = Directory.GetFiles(Dir("c4"), "*", SearchOption.AllDirectories)
                                      .Select(f => Path.GetRelativePath(Dir("c4"), f).Replace('\\', '/'))
                                      .Where(r => r != "career.json" && r != "career.json.lock").ToList();
                Check("C4: ★ a career saved by the S124 build (v3) is refused by name — 'saved by an older version of the game — start a " +
                      "new career' — at `season` and at each of the five pages; its file byte-untouched and nothing else written",
                      notRefused.Count == 0 && File.ReadAllBytes(h).AsSpan().SequenceEqual(v3) && others.Count == 0,
                      notRefused.Count > 0 ? "not refused by: " + string.Join(", ", notRefused) : others.Count > 0 ? "wrote " + string.Join(", ", others) : "6 refusals");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — THE LEAGUE STATE.
            // ════════════════════════════════════════════════════════════════════
            {
                var world = LeaguePrestigeOf(mte).OrderBy(p => p.SchoolId).ToList();
                LeagueState L(string name, long n) => LeagueStateFile.Read(P(name, Inv116($"career.league/league-{n}.json")), CalendarFixedHistoryId, n);
                Check("C5a: ★ league-0 is born with the career and league-1 and league-2 are written at the closes, each listed, each " +
                      "the world's prestige for every school",
                      new[] { 0L, 1L, 2L }.All(n => L("two", n).Prestige.SequenceEqual(world)) && world.Count == mte.Schools.Count,
                      Inv116($"{world.Count} schools"));

                //  The world edited IN MEMORY after the career is open: refused at the open check, and at the close by name.
                var school = mte.Schools.First(s => s.CurrentPrestige < 99);
                var edited = new WorldFile
                {
                    SchemaVersion = mte.SchemaVersion, Kind = mte.Kind, EraLabel = mte.EraLabel, Division = mte.Division,
                    WorldSeed = mte.WorldSeed, Tiers = mte.Tiers, Conferences = mte.Conferences, Places = mte.Places,
                    Events = mte.Events,
                    Schools = mte.Schools.Select(s => s.Id == school.Id ? s with { CurrentPrestige = s.CurrentPrestige + 1 } : s).ToList(),
                };
                CopyDir("master", "c5-edited");
                var masterSnap = Snap("master");
                string? atOpen, atClose;
                using (var s = OpenCareer(CareerOf("c5-edited"), mte))
                    atOpen = RefusalOf(() => AssertLeagueMatchesWorld(s, edited, "opened"))?.Message;
                atClose = Thrown(() => PlayInProcess("c5-edited", seedTwo, edited));
                var tag = Inv116($"school {school.Id}");
                Check("C5b: ★ a world whose prestige is edited in memory after the career is open is refused by name — at the open " +
                      "check, and at the close before a single write (the career folder exactly season one's)",
                      atOpen is not null && atOpen.Contains(tag, StringComparison.Ordinal)
                      && atClose is not null && atClose.Contains(tag, StringComparison.Ordinal)
                      && atClose.Contains("disagrees with the world", StringComparison.Ordinal)
                      && SeasonSnapshotDiff(masterSnap, Snap("c5-edited")) is null,
                      Short(atClose));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — PAGE-ONLY.
            // ════════════════════════════════════════════════════════════════════
            {
                var sw = Stopwatch.StartNew();
                const int opens = 5;
                for (var i = 0; i < opens; i++) using (OpenCareer(CareerOf("two"), mte)) { }
                var listedBytes = Directory.GetFiles(Dir("two"), "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".lock", StringComparison.Ordinal) && !f.EndsWith("career.json", StringComparison.Ordinal)
                                && !f.EndsWith("career.json.bak", StringComparison.Ordinal))
                    .Sum(f => new FileInfo(f).Length);
                timings.Add(Inv116($"verifying a two-season fixture-mte career at open: {sw.Elapsed.TotalMilliseconds / opens:F0} ms per open, ") +
                            Inv116($"{listedBytes / 1024.0 / 1024.0:F2} MB of listed files read"));
                timings.Add(Inv116($"the career file after two seasons: {twoCareer.Length:N0} bytes"));
                foreach (var t in timings) Console.WriteLine("  [page] " + t);
            }
        }
        catch (Exception ex)
        {
            Check("Phase 116 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 116 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }
}
