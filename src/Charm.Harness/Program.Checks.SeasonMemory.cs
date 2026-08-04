using System.Globalization;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 87 (Session 96) — HOST MEMORY.
//
//  What this phase proves:
//    C1  the zero path is the OLD SCHEDULE, against a fingerprint captured from
//        the pre-S96 tree before a line of production wiring existed;
//    C2  every way memory can come up empty is a VALUE, with its state table
//        asserted status by status — including a valid source that legitimately
//        remembers nothing;
//    C3  the flip is right at four separated layers: pure, slate, whole
//        schedule, and disk — each with a control that fails if memory is inert;
//    C4  R3 survives — every school still hosts exactly half its league season;
//    C5  the theorem, in four numbered parts, over every playing league of the
//        stock world, plus the odd-game refusal it leans on, by name;
//    C6  a pair whose parity CHANGED is dropped, never refused;
//    C7  foreign, malformed and self-referential memory is skipped or refused
//        deterministically;
//    C8  the career lifecycle on real disk — three retained seasons alternate,
//        an unlogged year yields nothing, a damaged candidate yields nothing,
//        and in neither case is an OLDER log reached for;
//    C9  determinism, and the peek's contract proven against a real reservation;
//    C10 isolation, proven behaviourally: two careers whose games were played
//        differently produce the identical memory.
//
//  ── The trap this phase is built around ────────────────────────────────────
//  The dangerous failure is not "memory does nothing" — that shows up loudly.
//  It is memory reaching the WRONG SEASON: enumerate the folder, take the
//  highest log, and a career that skipped retention for a year silently flips a
//  two-year-old schedule while every conservation check, every quota check and
//  every determinism check stays green. C8(ii) and C8(iii) are the only
//  assertions in this file that discriminate on it, and they do it the same way
//  both times: an older log SITS RIGHT THERE, valid and inviting, and the
//  correct answer is to produce no memory at all.
//
//  ── What this phase deliberately does not prove ────────────────────────────
//  That 526 pairs flip in the stock world. That is a measurement of one world
//  file, not a property of the engine; C5 proves the RELATIONSHIP (each school
//  wins exactly half its odd pairs) numerically over every league instead, which
//  stays true when the world is edited. The page prints the measured number and
//  the suite never asserts it.
// ============================================================================

internal static partial class Program
{
    // ── The golden, captured from the PRE-S96 TREE ────────────────────────────
    //
    //  PROVENANCE: emitted on 2026-08-03 from a pristine pull of `main` at the
    //  pre-S96 commit, by running the season page against the tiny fixture and
    //  reading its printed "Schedule fingerprint" line — the production
    //  `ScheduleFingerprint` helper this check calls, never a hand serialization.
    //
    //  ★ WHY THIS HASH IS THE RIGHT ONE. It hashes index|kind|home|away, so the
    //  ORIENTATION of every game is inside it. A flipped host is exactly what it
    //  moves, which is what makes "no memory changed nothing" a real assertion
    //  rather than a count comparison.
    //
    //  The game count is asserted BEFORE the hash, deliberately: "160 games,
    //  wrong hash" and "23 games" are different failures and should not arrive
    //  wearing the same face.
    private const long MemoryGoldenSeed = 20260703;
    private const int MemoryGoldenGameCount = 160;
    private const string MemoryGoldenScheduleSha256 =
        "51c8e88c202e9eb663f69dd2d317ca5d213a3faf98623496598a8e7e06684f54";

    /// <summary>The world identity the golden was taken against, recorded beside the hash so a
    /// future session can tell "the fixture changed" from "the engine changed".</summary>
    private const string MemoryGoldenWorldNote = "fixture-tiny: 20 schools, 4 leagues of 5, 16 games, skip 0";

    private static bool Phase87SeasonMemoryCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 87 — Host memory (S96: a season reads season N-1's retained log and " +
                          "flips its single-meeting hosts — zero-path identity, the five statuses as " +
                          "values, four-layer flip correctness, R3, the d/2 theorem, parity change, " +
                          "foreign memory, the career lifecycle on disk, determinism, isolation) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var scratch = Path.Combine(Path.GetTempPath(),
            "charm-s96-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(scratch);
            var tiny = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json"));
            var memWorld = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-memory.world.json"));
            var stock = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "stock-d1.world.json"));

            // ════════════════════════════════════════════════════════════════════
            //  C1 — the zero path is the pre-S96 schedule, exactly.
            // ════════════════════════════════════════════════════════════════════
            var zero = BuildSeasonSchedule(tiny, MemoryGoldenSeed, null, out var zeroMemory);
            Check($"C1a: the no-history schedule is the recorded shape ({MemoryGoldenWorldNote})",
                  zero.Count == MemoryGoldenGameCount,
                  $"{zero.Count} games, want {MemoryGoldenGameCount}");
            var zeroFp = ScheduleFingerprint(zero);
            Check("C1b: the no-history schedule matches the PRE-S96 golden, orientation included",
                  zeroFp == MemoryGoldenScheduleSha256,
                  zeroFp == MemoryGoldenScheduleSha256 ? zeroFp[..16] + "…" : $"got {zeroFp}");
            Check("C1c: with no history the season reports no memory and no flips",
                  zeroMemory.Status == HostMemoryStatus.NoHistory
                  && zeroMemory.ResidualsFlipped == 0 && zeroMemory.LeaguesWithResidualsFlipped == 0,
                  $"{zeroMemory.Status}, {zeroMemory.ResidualsFlipped} flipped");

            // ════════════════════════════════════════════════════════════════════
            //  C2 — every no-memory state is a value, and the state table holds.
            // ════════════════════════════════════════════════════════════════════
            void CheckState(string label, HostMemory m, HostMemoryStatus status,
                            long? source, long? attempted, HostMemoryProblem problem)
                => Check(label,
                         m.Status == status && m.SourceSeasonId == source
                         && m.AttemptedSeasonId == attempted && m.Problem == problem
                         && (status == HostMemoryStatus.Loaded || m.PreviousResidualHost.Count == 0)
                         && (status == HostMemoryStatus.Loaded || m.ResidualPairsRemembered == 0),
                         $"{m.Status}/src {m.SourceSeasonId?.ToString(CultureInfo.InvariantCulture) ?? "-"}" +
                         $"/att {m.AttemptedSeasonId?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{m.Problem}");

            CheckState("C2a: no history — source null, attempt null, problem None",
                       ReadHostMemory(null), HostMemoryStatus.NoHistory, null, null, HostMemoryProblem.None);

            var c2Path = Path.Combine(scratch, "c2", "career.history.json");
            using (var store = HistoryStore.Open(c2Path, WorldFingerprint(tiny)))
            {
                CheckState("C2b: first season — attempt null, because season 0 was never a candidate",
                           ReadHostMemory(store), HostMemoryStatus.FirstSeason, null, null, HostMemoryProblem.None);
                store.ReserveSeason();      // season 1 spent, no log published
                CheckState("C2c: a season number spent with no log — attempt named, no problem",
                           ReadHostMemory(store), HostMemoryStatus.NoPublishedLog, null, 1, HostMemoryProblem.None);
            }

            // Two different kinds of bad candidate, kept apart on purpose — they are not the
            // same failure and must not arrive wearing the same word. A stub too short to hold
            // even a header has been CUT OFF; a full-length file that is not a Charm log at all
            // is a FORMAT refusal, and only the second one reaches the magic check.
            var c2LogDir = GameLogWriter.LogFolderFor(c2Path);
            Directory.CreateDirectory(c2LogDir);
            var c2Candidate = GameLogWriter.FinalPathFor(c2Path, 1);
            File.WriteAllBytes(c2Candidate, new byte[64]);
            using (var store = HistoryStore.Open(c2Path, WorldFingerprint(tiny)))
            {
                CheckState("C2d: a candidate too short to hold a header — Truncated",
                           ReadHostMemory(store), HostMemoryStatus.Unreadable, null, 1,
                           HostMemoryProblem.Truncated);
            }
            File.WriteAllBytes(c2Candidate, new byte[512]);
            using (var store = HistoryStore.Open(c2Path, WorldFingerprint(tiny)))
            {
                CheckState("C2e: a full-length candidate that is not a Charm log — UnsupportedVersion",
                           ReadHostMemory(store), HostMemoryStatus.Unreadable, null, 1,
                           HostMemoryProblem.UnsupportedVersion);
            }

            // ── The loaded-zero-residual source: a REAL career on a world whose pairs all
            //    meet an even number of times. "Loaded" must mean "a valid source log", not
            //    "at least one residual was found".
            var evenPath = Path.Combine(scratch, "even", "career.history.json");
            var even1 = PlayRetainedSeason(tiny, MemoryGoldenSeed, evenPath, configPath);
            HostMemory evenMemory;
            List<SeasonGame> even2;
            SeasonMemoryOutcome even2Outcome;
            using (var store = HistoryStore.Open(evenPath, WorldFingerprint(tiny)))
            {
                evenMemory = ReadHostMemory(store);
                even2 = BuildSeasonSchedule(tiny, MemoryGoldenSeed, store, out even2Outcome);
            }
            Check("C2f: a valid source with only even-split pairs LOADS and remembers nothing",
                  evenMemory.Status == HostMemoryStatus.Loaded && evenMemory.SourceSeasonId == 1
                  && evenMemory.AttemptedSeasonId == 1 && evenMemory.Problem == HostMemoryProblem.None
                  && evenMemory.ResidualPairsRemembered == 0
                  && evenMemory.ConferenceGamesRead == MemoryGoldenGameCount,
                  $"{evenMemory.Status}, {evenMemory.ResidualPairsRemembered} residuals, " +
                  $"{evenMemory.ConferenceGamesRead} games read");
            Check("C2g: loaded-but-inapplicable leaves the schedule exactly as it was (C8 iv)",
                  even2Outcome.Status == HostMemoryStatus.Loaded && even2Outcome.SourceSeasonId == 1
                  && even2Outcome.ResidualsFlipped == 0
                  && ScheduleFingerprint(even2) == MemoryGoldenScheduleSha256
                  && ScheduleFingerprint(even1.Schedule) == MemoryGoldenScheduleSha256,
                  $"{even2Outcome.ResidualsFlipped} flipped");

            // ════════════════════════════════════════════════════════════════════
            //  C3 — the flip, at four separated layers.
            // ════════════════════════════════════════════════════════════════════
            //  (a) PURE. A hand-built memory against hand-built meetings, exercising every
            //      one of the five conditions plus the inversion itself.
            var pureMembers = new List<int> { 10, 20, 30, 40 };
            var pureMeetings = new Dictionary<(int Lo, int Hi), int>
            {
                [(10, 20)] = 1,   // odd — flips
                [(10, 30)] = 3,   // odd — flips
                [(10, 40)] = 2,   // even — no residual to decide
                [(20, 30)] = 1,   // odd, but memory names a host outside the pair
                [(20, 40)] = 1,   // odd, but no memory entry at all
                [(30, 40)] = 1,   // odd — flips
            };
            var pureMemory = LoadedMemory(new Dictionary<(int Lo, int Hi), int>
            {
                [(10, 20)] = 10,   // 10 hosted -> 20 must host
                [(10, 30)] = 30,   // 30 hosted -> 10 must host
                [(10, 40)] = 40,   // parity changed: skipped
                [(20, 30)] = 99,   // host not in the pair: skipped
                [(30, 40)] = 40,   // 40 hosted -> 30 must host
                [(50, 60)] = 50,   // both schools foreign: skipped
                [(20, 55)] = 20,   // one school foreign: skipped
                [(70, 70)] = 70,   // not a normalized pair: skipped
            });
            var pureFlips = ResidualsToFlip(pureMemory, pureMeetings, pureMembers);
            var pureWant = new[]
            {
                new FixedResidualHost(10, 20, 20),
                new FixedResidualHost(10, 30, 10),
                new FixedResidualHost(30, 40, 30),
            };
            Check("C3a: the pure flip inverts exactly the legal entries and silently skips the rest",
                  pureFlips.Count == pureWant.Length
                  && pureFlips.Zip(pureWant).All(p => p.First == p.Second),
                  pureFlips.Count == pureWant.Length
                    ? string.Join(" ", pureFlips.Select(f => $"({f.LowSchoolId},{f.HighSchoolId})->{f.HostSchoolId}"))
                    : $"{pureFlips.Count} flips, want {pureWant.Length}");

            //  (b) SLATE. Production builds a league, memory is derived from that league's OWN
            //      oriented games, production builds it again — and every odd pair inverts.
            var memLeague = LeagueMembers(memWorld, 1);
            var memConf = memWorld.Conferences.Single(c => c.Id == 1);
            var slate1 = BuildConferenceSlate(memLeague, memConf.Games, memConf.Skip,
                                              new List<(int, int)>(), "c3b ");
            var derived = LoadedMemory(ResidualHostsOf(slate1));
            var slate2 = BuildConferenceSlate(memLeague, memConf.Games, memConf.Skip,
                                              new List<(int, int)>(), "c3b ", memory: derived);
            var hosts1 = ResidualHostsOf(slate1);
            var hosts2 = ResidualHostsOf(slate2);
            Check("C3b: every single-meeting pair in a real league changes host in season 2",
                  slate2.Verdict == SlateVerdict.Feasible && hosts1.Count > 0
                  && hosts1.Count == hosts2.Count
                  && hosts1.All(kv => hosts2.TryGetValue(kv.Key, out var h) && h != kv.Value),
                  $"{hosts1.Count} odd pairs, {slate2.MemoryFixedHosts} venues supplied");

            //  The control that makes C3b mean something: with memory that remembers nothing,
            //  the second slate must be the FIRST one, game for game. Without this, "the hosts
            //  changed" could be the solver wandering rather than the memory acting.
            var slateControl = BuildConferenceSlate(memLeague, memConf.Games, memConf.Skip,
                                                    new List<(int, int)>(), "c3b ",
                                                    memory: LoadedMemory(new Dictionary<(int Lo, int Hi), int>()));
            Check("C3b control: empty memory reproduces season 1's league exactly — the flip is the memory's doing",
                  slateControl.Verdict == SlateVerdict.Feasible
                  && slateControl.MemoryFixedHosts == 0
                  && slateControl.Games.SequenceEqual(slate1.Games),
                  $"{slateControl.Games.Count} games");

            //  (c) SCHEDULE — C1's golden, already asserted above.
            //  (d) DISK — C8, below.

            // ════════════════════════════════════════════════════════════════════
            //  C4 — R3 survives the flip: every school still hosts exactly games/2.
            // ════════════════════════════════════════════════════════════════════
            var r3Bad = new List<string>();
            var r3Leagues = 0;
            foreach (var c in stock.Conferences.OrderBy(c => c.Id))
            {
                var members = LeagueMembers(stock, c.Id);
                if (members.Count == 0 || c.Games == 0) continue;
                var rivalries = ActiveRivalries(members, StockRivals(stock), c.Games);
                var a = BuildConferenceSlate(members, c.Games, c.Skip, rivalries, $"c4 {c.Id} ");
                var b = BuildConferenceSlate(members, c.Games, c.Skip, rivalries, $"c4 {c.Id} ",
                                             memory: LoadedMemory(ResidualHostsOf(a)));
                r3Leagues++;
                if (b.Verdict != SlateVerdict.Feasible) { r3Bad.Add($"{c.ShortName} {b.Verdict}"); continue; }
                var homes = members.ToDictionary(s => s, _ => 0);
                foreach (var (home, _) in b.Games) homes[home]++;
                foreach (var s in members)
                    if (homes[s] != c.Games / 2) r3Bad.Add($"{c.ShortName} school {s} hosts {homes[s]}");
            }
            Check($"C4: after the flip every school still hosts exactly half its league season ({r3Leagues} leagues)",
                  r3Bad.Count == 0, r3Bad.Count == 0 ? "quota exact everywhere" : string.Join("; ", r3Bad.Take(4)));

            // ════════════════════════════════════════════════════════════════════
            //  C5 — the theorem, four parts, over every playing league of the stock world.
            //
            //  For a school in d odd pairs: its even-split pairs hand it (G-d)/2 homes, so it
            //  must win exactly d/2 residuals — every season, whatever memory says. Inverting
            //  every residual re-awards exactly d/2. That is why the flip cannot break R3, and
            //  it is verified numerically here rather than argued.
            // ════════════════════════════════════════════════════════════════════
            var t1 = new List<string>(); var t2 = new List<string>();
            var t3 = new List<string>(); var t4 = new List<string>();
            foreach (var c in stock.Conferences.OrderBy(c => c.Id))
            {
                var members = LeagueMembers(stock, c.Id);
                if (members.Count == 0 || c.Games == 0) continue;
                var rivalries = ActiveRivalries(members, StockRivals(stock), c.Games);
                var a = BuildConferenceSlate(members, c.Games, c.Skip, rivalries, $"c5 {c.Id} ");
                var oddCount = members.ToDictionary(s => s, _ => 0);
                foreach (var (pair, m) in a.Meetings)
                    if (m % 2 == 1) { oddCount[pair.Lo]++; oddCount[pair.Hi]++; }

                var hostsA = ResidualHostsOf(a);
                var winsA = members.ToDictionary(s => s, _ => 0);
                foreach (var h in hostsA.Values) winsA[h]++;
                var winsInv = members.ToDictionary(s => s, _ => 0);
                foreach (var (pair, h) in hostsA) winsInv[h == pair.Lo ? pair.Hi : pair.Lo]++;

                var b = BuildConferenceSlate(members, c.Games, c.Skip, rivalries, $"c5 {c.Id} ",
                                             memory: LoadedMemory(hostsA));
                var homesB = members.ToDictionary(s => s, _ => 0);
                foreach (var (home, _) in b.Games) homesB[home]++;

                foreach (var s in members)
                {
                    if (oddCount[s] % 2 != 0) t1.Add($"{c.ShortName}/{s} d={oddCount[s]}");
                    if (winsA[s] != oddCount[s] / 2) t2.Add($"{c.ShortName}/{s} {winsA[s]}≠{oddCount[s] / 2}");
                    if (winsInv[s] != oddCount[s] / 2) t3.Add($"{c.ShortName}/{s} {winsInv[s]}≠{oddCount[s] / 2}");
                    if (homesB[s] != c.Games / 2) t4.Add($"{c.ShortName}/{s} {homesB[s]}≠{c.Games / 2}");
                }
            }
            Check("C5a: every school's odd-pair count is EVEN", t1.Count == 0,
                  t1.Count == 0 ? "no school owes half a residual" : string.Join("; ", t1.Take(4)));
            Check("C5b: season 1 awards every school exactly half its odd pairs", t2.Count == 0,
                  t2.Count == 0 ? "exact" : string.Join("; ", t2.Take(4)));
            Check("C5c: inverting every residual re-awards exactly half", t3.Count == 0,
                  t3.Count == 0 ? "exact" : string.Join("; ", t3.Take(4)));
            Check("C5d: season 2 lands every school at exactly half its league season", t4.Count == 0,
                  t4.Count == 0 ? "exact" : string.Join("; ", t4.Take(4)));

            //  The re-pointed control. C5a is only reachable because an odd GAME COUNT cannot
            //  load: d has the same parity as G, so an odd d needs an odd G. Rather than invent
            //  a second refusal for a state no world can reach, this asserts the guard that
            //  makes it unreachable, by name.
            var oddG = ConferenceStaticLegality(15, 0);
            var oddSlate = BuildConferenceSlate(new List<int> { 1, 2, 3, 4 }, 15, 0,
                                                new List<(int, int)>(), "c5 control ");
            Check("C5e: an odd conference game count is refused by production, and says so",
                  oddG is not null && oddG.Contains("odd", StringComparison.Ordinal)
                  && oddSlate.Verdict == SlateVerdict.InvalidConfiguration,
                  oddG ?? "no refusal");

            // ════════════════════════════════════════════════════════════════════
            //  C6 — a pair whose parity CHANGED is dropped here, never left for the slate.
            //
            //  This matters because OrientConferenceSlate correctly REFUSES a fixed host on an
            //  even pair — and a refusal at that point takes the whole season down. A league
            //  changing size between careers is ordinary; a season failing to schedule because
            //  of it is not.
            // ════════════════════════════════════════════════════════════════════
            var evenPairMemory = LoadedMemory(new Dictionary<(int Lo, int Hi), int>
            {
                [(memLeague[0], memLeague[1])] = memLeague[0],
            });
            var evenMeetings = new Dictionary<(int Lo, int Hi), int> { [(memLeague[0], memLeague[1])] = 2 };
            var dropped = ResidualsToFlip(evenPairMemory, evenMeetings, memLeague);
            var tinyLeague = LeagueMembers(tiny, 1);
            var tinyConf = tiny.Conferences.Single(c => c.Id == 1);
            var tinySlate = BuildConferenceSlate(
                tinyLeague, tinyConf.Games, tinyConf.Skip, new List<(int, int)>(), "c6 ",
                memory: LoadedMemory(tinyLeague.SelectMany(
                    a => tinyLeague.Where(b => b > a).Select(b => ((Lo: a, Hi: b), a)))
                    .ToDictionary(x => x.Item1, x => x.Item2)));
            Check("C6: memory naming a now-even pair is filtered out, and the league still builds",
                  dropped.Count == 0 && tinySlate.Verdict == SlateVerdict.Feasible
                  && tinySlate.MemoryFixedHosts == 0,
                  $"{dropped.Count} flips, {tinySlate.Verdict}");

            // ════════════════════════════════════════════════════════════════════
            //  C7 — foreign, malformed and self-referential memory.
            // ════════════════════════════════════════════════════════════════════
            var foreign = ResidualsToFlip(
                LoadedMemory(new Dictionary<(int Lo, int Hi), int>
                {
                    [(90001, 90002)] = 90001,                 // schools that do not exist
                    [(memLeague[0], 90003)] = memLeague[0],    // one real school, one foreign
                    [(tinyLeague[0], tinyLeague[1])] = tinyLeague[0],  // real schools, other league
                }),
                slate1.Meetings, memLeague);
            Check("C7a: memory naming schools this league does not have is skipped, not refused",
                  foreign.Count == 0, $"{foreign.Count} flips");

            var selfPair = Aggregate(
                new List<(int Home, int Away, bool Conference)> { (7, 7, true) }, 4);
            Check("C7b: a source game whose two sides are the same school refuses the WHOLE source",
                  selfPair.Status == HostMemoryStatus.Unreadable
                  && selfPair.Problem == HostMemoryProblem.InconsistentPairFacts
                  && selfPair.PreviousResidualHost.Count == 0,
                  $"{selfPair.Status}/{selfPair.Problem}");

            //  Host counts that cannot represent an even split plus at most one residual.
            var impossible = Aggregate(
                new List<(int Home, int Away, bool Conference)> { (1, 2, true), (1, 2, true), (1, 2, true) }, 4);
            Check("C7c: a pair where one school hosted all three meetings refuses the whole source",
                  impossible.Status == HostMemoryStatus.Unreadable
                  && impossible.Problem == HostMemoryProblem.InconsistentPairFacts
                  && impossible.ConferenceGamesRead == 3,
                  $"{impossible.Status}/{impossible.Problem}, {impossible.ConferenceGamesRead} read");

            // ════════════════════════════════════════════════════════════════════
            //  C8 — the career lifecycle, on real disk, through the production path.
            //
            //  ★ Every log here is produced by scheduling, PLAYING and finalizing a season, in
            //  strict order — season 1 is on disk before season 2 reads it. Hand-written files
            //  appear only where forging damage is the whole point.
            // ════════════════════════════════════════════════════════════════════
            var lifePath = Path.Combine(scratch, "life", "career.history.json");
            var life1 = PlayRetainedSeason(memWorld, MemoryGoldenSeed, lifePath, configPath);
            var life2 = PlayRetainedSeason(memWorld, MemoryGoldenSeed, lifePath, configPath);
            var life3 = PlayRetainedSeason(memWorld, MemoryGoldenSeed, lifePath, configPath);
            var fp1 = ScheduleFingerprint(life1.Schedule);
            var fp2 = ScheduleFingerprint(life2.Schedule);
            var fp3 = ScheduleFingerprint(life3.Schedule);
            Check("C8i-a: three retained seasons read seasons -, 1, 2 — the source id is asserted, never assumed",
                  life1.Memory.Status == HostMemoryStatus.FirstSeason
                  && life2.Memory.Status == HostMemoryStatus.Loaded && life2.Memory.SourceSeasonId == 1
                  && life3.Memory.Status == HostMemoryStatus.Loaded && life3.Memory.SourceSeasonId == 2,
                  $"{life1.Memory.Status}, {life2.Memory.SourceSeasonId}, {life3.Memory.SourceSeasonId}");
            //  ★ NARROWED AT S99, AND THE OLD FORM WAS NOT WRONG — IT WAS OBSOLETE. This read
            //  "season 2 flips season 1, and season 3 flips BACK to season 1's schedule". That
            //  return trip was never what C8 is about; it was a CONSEQUENCE of the extra-meeting
            //  graph being frozen for the life of a career, which is exactly what S99 removes.
            //  On this fixture the two possible extra graphs on five schools are disjoint, so a
            //  rotated year can legitimately share no odd pair with the year before it and have
            //  nothing to flip at all.
            //
            //  What C8i-b actually protects survives intact and is now asserted directly: WHERE
            //  A PAIR IS ODD IN TWO CONSECUTIVE SEASONS, THE HOST ALTERNATES. That is the whole
            //  basketball of S96, it is independent of which pairs the scheduler chose, and —
            //  unlike a fingerprint comparison — it cannot decay into comparing two identical
            //  seasons, because the pairs it ranges over are found at runtime.
            //  ★ AND IT IS MEASURED ON A LEAGUE WHERE ODD PAIRS CAN RECUR. On this fixture's
            //  five-school leagues the extra graph is a five-cycle and its complement is the
            //  OTHER five-cycle, so a rotated year shares no odd pair with the year before it
            //  and the alternation assertion would range over nothing at all. The sixteen-school
            //  rig moves three pairs a year out of twelve, so most residual pairs recur and the
            //  alternation is observable — which is the only condition under which asserting it
            //  means anything.
            var altWorld = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds",
                                                  "fixture-rotation.world.json"));
            var altPath = Path.Combine(scratch, "alt", "career.history.json");
            var alt1 = PlayRetainedSeason(altWorld, MemoryGoldenSeed, altPath, configPath);
            var alt2 = PlayRetainedSeason(altWorld, MemoryGoldenSeed, altPath, configPath);
            var alt3 = PlayRetainedSeason(altWorld, MemoryGoldenSeed, altPath, configPath);
            var alternated = 0;
            var repeated = 0;
            foreach (var (before, after) in new[] { (alt1, alt2), (alt2, alt3) })
            {
                var a = ResidualHostsOfSchedule(before.Schedule);
                var b = ResidualHostsOfSchedule(after.Schedule);
                foreach (var (pair, host) in a)
                {
                    if (!b.TryGetValue(pair, out var next)) continue;   // no longer an odd pair
                    repeated++;
                    if (next != host) alternated++;
                }
            }
            //  ★ THE CLAIM IS ONE-DIRECTIONAL, AND THE FIRST DRAFT OF IT WAS WRONG. It asserted
            //  the exact identity "recurring == alternated + given up", which the run refused:
            //  152 recurring, 87 alternated, 88 given up. The arithmetic was never going to
            //  close, because a pair whose venue was given up is not thereby PINNED — the flow
            //  still chooses its host freely and lands on the other school often enough to
            //  alternate anyway. Swaps therefore always EXCEED applied reversals.
            //
            //  What is true, and what host memory actually promises, is the one direction:
            //  EVERY REVERSAL THAT WAS APPLIED HAPPENED. So at least (recurring - given up)
            //  pairs must alternate. This still fails loudly if the flip stops being applied —
            //  the alternation count would fall through the floor — and it cannot be satisfied
            //  by a fraction, because the floor is computed from a counter kept at the point the
            //  venue is surrendered rather than chosen to fit the result.
            var givenUp = alt2.Rotation.MemoryVenuesGivenUp + alt3.Rotation.MemoryVenuesGivenUp;
            Check("C8i-b: where a pair owns the residual in two consecutive seasons the host ALTERNATES — " +
                  "every reversal that was applied happened, so at least (recurring - given up) pairs swap",
                  repeated > 0 && alternated > 0 && alternated >= repeated - givenUp && fp2 != fp1,
                  $"{repeated} recurring residual pairs, {givenUp} venue(s) given up, so at least " +
                  $"{Math.Max(0, repeated - givenUp)} must swap — {alternated} did");

            //  (ii) T1 — a history-bound season that retained nothing. The next season must
            //  find no log for N-1 and STOP. Season 1's perfectly good log is sitting right
            //  there; reaching it would be stale alternation wearing the right face.
            var gapPath = Path.Combine(scratch, "gap", "career.history.json");
            PlayRetainedSeason(memWorld, MemoryGoldenSeed, gapPath, configPath);          // season 1, logged
            PlaySeasonWithoutRetention(memWorld, MemoryGoldenSeed, gapPath, configPath);   // season 2, no log
            List<SeasonGame> gap3; SeasonMemoryOutcome gap3Memory;
            using (var store = HistoryStore.Open(gapPath, WorldFingerprint(memWorld)))
                gap3 = BuildSeasonSchedule(memWorld, MemoryGoldenSeed, store, out gap3Memory);
            //  ★ NARROWED AT S99. The old form also demanded the schedule equal season 1's,
            //  which was the frozen graph speaking again. Season 3 now ROTATES off season 1's
            //  log while refusing to take HOSTS from it — which is the S99 divergence landing
            //  exactly on C8's own trap, and is asserted below rather than assumed.
            var gapNoCareer = BuildSeasonSchedule(memWorld, MemoryGoldenSeed, null);
            Check("C8ii: an unlogged year yields NO memory — season 1's log is provably not reached for " +
                  "HOSTS, even while the rotation half reads it two years back",
                  gap3Memory.Status == HostMemoryStatus.NoPublishedLog
                  && gap3Memory.AttemptedSeasonId == 2 && gap3Memory.SourceSeasonId is null
                  && gap3Memory.ResidualsFlipped == 0
                  && ScheduleFingerprint(gap3) != ScheduleFingerprint(gapNoCareer)
                  && File.Exists(GameLogWriter.FinalPathFor(gapPath, 1)),
                  $"{gap3Memory.Status}, attempted {gap3Memory.AttemptedSeasonId}, " +
                  $"{gap3Memory.ResidualsFlipped} flipped, and the slate moved off the no-career one");

            //  (iii) T2 — the candidate is damaged. Same shape, same trap, different cause.
            var badPath = Path.Combine(scratch, "bad", "career.history.json");
            PlayRetainedSeason(memWorld, MemoryGoldenSeed, badPath, configPath);
            PlayRetainedSeason(memWorld, MemoryGoldenSeed, badPath, configPath);
            var victim = GameLogWriter.FinalPathFor(badPath, 2);
            var whole = File.ReadAllBytes(victim);
            File.WriteAllBytes(victim, whole[..(whole.Length / 2)]);   // truncated mid-file
            List<SeasonGame> bad3; SeasonMemoryOutcome bad3Memory;
            using (var store = HistoryStore.Open(badPath, WorldFingerprint(memWorld)))
                bad3 = BuildSeasonSchedule(memWorld, MemoryGoldenSeed, store, out bad3Memory);
            //  ★ NARROWED AT S99, for the same reason as C8ii and with the same replacement.
            Check("C8iii: a damaged candidate yields NO memory — never the valid season-1 log beside it, " +
                  "however far back the rotation half is willing to read",
                  bad3Memory.Status == HostMemoryStatus.Unreadable
                  && bad3Memory.Problem == HostMemoryProblem.Truncated
                  && bad3Memory.AttemptedSeasonId == 2 && bad3Memory.SourceSeasonId is null
                  && bad3Memory.ResidualsFlipped == 0
                  && ScheduleFingerprint(bad3) != ScheduleFingerprint(gapNoCareer)
                  && File.Exists(GameLogWriter.FinalPathFor(badPath, 1)),
                  $"{bad3Memory.Status}/{bad3Memory.Problem}, {bad3Memory.ResidualsFlipped} flipped");

            //  A log from a different career, sitting at exactly the right path.
            var alienPath = Path.Combine(scratch, "alien", "career.history.json");
            PlayRetainedSeason(memWorld, MemoryGoldenSeed, alienPath, configPath);
            var stranger = Path.Combine(scratch, "stranger", "career.history.json");
            PlayRetainedSeason(memWorld, MemoryGoldenSeed, stranger, configPath);
            File.Copy(GameLogWriter.FinalPathFor(stranger, 1),
                      GameLogWriter.FinalPathFor(alienPath, 2));
            using (var store = HistoryStore.Open(alienPath, WorldFingerprint(memWorld)))
            {
                store.ReserveSeason();      // pretend season 2 happened
                var alien = ReadHostMemory(store);
                Check("C8iv: a log from another career at the right path is refused by lineage",
                      alien.Status == HostMemoryStatus.Unreadable
                      && alien.Problem is HostMemoryProblem.WrongCareer or HostMemoryProblem.WrongSeason,
                      $"{alien.Status}/{alien.Problem}");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C9 — determinism, and the peek's contract against a real reservation.
            // ════════════════════════════════════════════════════════════════════
            using (var store = HistoryStore.Open(lifePath, WorldFingerprint(memWorld)))
            {
                var readA = ReadHostMemory(store);
                var readB = ReadHostMemory(store);
                Check("C9a: reading the same source twice gives the same memory, and reading never mutates",
                      readA.Status == readB.Status && readA.SourceSeasonId == readB.SourceSeasonId
                      && readA.PreviousResidualHost.Count == readB.PreviousResidualHost.Count
                      && readA.PreviousResidualHost.All(kv =>
                             readB.PreviousResidualHost.TryGetValue(kv.Key, out var v) && v == kv.Value),
                      $"{readA.ResidualPairsRemembered} residuals, both reads");

                var flipsA = ResidualsToFlip(readA, slate1.Meetings, memLeague);
                var flipsB = ResidualsToFlip(readB, slate1.Meetings, memLeague);
                Check("C9b: the flip list is order-stable — same entries, same order, every time",
                      flipsA.SequenceEqual(flipsB), $"{flipsA.Count} venues");

                //  ★ The peek is proven HONEST rather than named honest: the file's bytes do
                //  not move, and the number the next reservation actually hands back is the
                //  number the peek promised.
                var before = File.ReadAllBytes(store.Path);
                var peeked = store.PeekNextSeasonId;
                var afterPeek = File.ReadAllBytes(store.Path);
                var reserved = store.ReserveSeason();
                // ★ Compared through the identity's OWN rendering, not by prying its number
                //   out. S89 refuses to hand the raw value to domain code and this check has
                //   no business being the exception; "season:4" is proof enough.
                var expected = "season:" + peeked.ToString(CultureInfo.InvariantCulture);
                Check("C9c: peeking moves nothing on disk, and the peek IS the next reservation",
                      before.AsSpan().SequenceEqual(afterPeek)
                      && reserved.ToString() == expected
                      && store.PeekNextSeasonId == peeked + 1,
                      $"peeked {peeked}, reserved {reserved}");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10 — isolation, proven behaviourally rather than by reading the source.
            //
            //  The log carries scores, overtimes, possession counts, every roster card and
            //  every man's stat line. If ANY of it reached the scheduler, two careers whose
            //  games were played differently would remember differently. Same world, same
            //  schedule, different seeds — the basketball diverges completely and the memory
            //  must be identical, key for key.
            // ════════════════════════════════════════════════════════════════════
            var isoA = Path.Combine(scratch, "isoA", "career.history.json");
            var isoB = Path.Combine(scratch, "isoB", "career.history.json");
            var runA = PlayRetainedSeason(memWorld, MemoryGoldenSeed, isoA, configPath);
            var runB = PlayRetainedSeason(memWorld, MemoryGoldenSeed + 7717, isoB, configPath);
            HostMemory memA, memB;
            using (var s = HistoryStore.Open(isoA, WorldFingerprint(memWorld))) memA = ReadHostMemory(s);
            using (var s = HistoryStore.Open(isoB, WorldFingerprint(memWorld))) memB = ReadHostMemory(s);
            var basketballDiverged = !runA.Results.Select(r => r.HomeScore).SequenceEqual(
                                          runB.Results.Select(r => r.HomeScore));
            Check("C10a: the two careers really did play different basketball (the discriminator)",
                  basketballDiverged,
                  basketballDiverged ? "scores differ" : "identical scores — the check proves nothing");
            Check("C10b: and they remember the identical hosts — no box-score field reaches the scheduler",
                  memA.Status == HostMemoryStatus.Loaded && memB.Status == HostMemoryStatus.Loaded
                  && memA.ResidualPairsRemembered > 0
                  && memA.PreviousResidualHost.Count == memB.PreviousResidualHost.Count
                  && memA.PreviousResidualHost.All(kv =>
                         memB.PreviousResidualHost.TryGetValue(kv.Key, out var v) && v == kv.Value),
                  $"{memA.ResidualPairsRemembered} residuals, identical");
        }
        catch (Exception ex)
        {
            Check("Phase 87 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
            catch { /* a leftover temp folder is not a test failure */ }
        }

        Console.WriteLine(pass ? "  Phase 87: PASS" : "  Phase 87: FAIL");
        return pass;
    }

    // ── Helpers, kept here rather than in production ──────────────────────────────

    /// <summary>A Loaded memory over a hand-supplied or derived residual map. Test-side only:
    /// production builds these by reading a log.</summary>
    private static HostMemory LoadedMemory(Dictionary<(int Lo, int Hi), int> residuals)
        => new(residuals, HostMemoryStatus.Loaded, 1, 1, HostMemoryProblem.None,
               0, residuals.Count);

    /// <summary>Who hosted the residual of each odd pair, derived from a slate's oriented games.
    ///
    /// <para>★ DELIBERATELY RE-DERIVED rather than calling production's aggregation. C3b compares
    /// production against this; if it called production's own routine the comparison would be a
    /// routine agreeing with itself.</para></summary>
    private static Dictionary<(int Lo, int Hi), int> ResidualHostsOf(ConferenceSlate slate)
    {
        var lo = new Dictionary<(int Lo, int Hi), int>();
        var hi = new Dictionary<(int Lo, int Hi), int>();
        foreach (var (home, away) in slate.Games)
        {
            var key = (Lo: Math.Min(home, away), Hi: Math.Max(home, away));
            lo.TryAdd(key, 0); hi.TryAdd(key, 0);
            if (home == key.Lo) lo[key]++; else hi[key]++;
        }
        var result = new Dictionary<(int Lo, int Hi), int>();
        foreach (var key in lo.Keys)
            if (lo[key] != hi[key]) result[key] = lo[key] > hi[key] ? key.Lo : key.Hi;
        return result;
    }

    /// <summary>★ S99 — who hosted each ODD pair, read off a whole season's oriented games
    /// rather than off one league's slate. Deliberately re-derived here: C8i-b compares two
    /// seasons of production output, and calling production's own aggregation would be a
    /// routine agreeing with itself.</summary>
    private static Dictionary<(int Lo, int Hi), int> ResidualHostsOfSchedule(List<SeasonGame> schedule)
    {
        var lo = new Dictionary<(int Lo, int Hi), int>();
        var hi = new Dictionary<(int Lo, int Hi), int>();
        foreach (var g in schedule)
        {
            var key = (Lo: Math.Min(g.HomeId, g.AwayId), Hi: Math.Max(g.HomeId, g.AwayId));
            lo.TryAdd(key, 0); hi.TryAdd(key, 0);
            if (g.HomeId == key.Lo) lo[key]++; else hi[key]++;
        }
        var result = new Dictionary<(int Lo, int Hi), int>();
        foreach (var key in lo.Keys)
            if (lo[key] != hi[key]) result[key] = lo[key] > hi[key] ? key.Lo : key.Hi;
        return result;
    }

    private static List<int> LeagueMembers(WorldFile world, int conferenceId)
        => world.Schools.Where(s => s.ConferenceId == conferenceId)
                        .Select(s => s.Id).OrderBy(x => x).ToList();

    private static Dictionary<int, int?> StockRivals(WorldFile world)
        => world.Schools.ToDictionary(s => s.Id, s => s.RivalId);

    /// <summary>Schedule, play and finalize one season against a career, through exactly the
    /// production path the season page uses. The store is opened and closed around it, so the
    /// next season sees the file the way a separate run would.</summary>
    private static SeasonRunOutcome PlayRetainedSeason(
        WorldFile world, long seed, string historyPath, string configPath)
    {
        using var store = HistoryStore.Open(historyPath, WorldFingerprint(world));
        return RunSeasonCore(world, seed, configPath, verbose: false, store, retainGameLog: true);
    }

    /// <summary>A season bound to the career that publishes no log — the T1 case. It still
    /// spends a season number, which is exactly what makes the next season's arithmetic land
    /// on a year with nothing to read.</summary>
    private static SeasonRunOutcome PlaySeasonWithoutRetention(
        WorldFile world, long seed, string historyPath, string configPath)
    {
        using var store = HistoryStore.Open(historyPath, WorldFingerprint(world));
        return RunSeasonCore(world, seed, configPath, verbose: false, store, retainGameLog: false);
    }
}
