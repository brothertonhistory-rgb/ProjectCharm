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
    private const int MemoryGoldenGameCount = 120;
    private const string MemoryGoldenScheduleSha256 =
        "6fc122dd3bc4f48a6f7c8b3787dcc236603536d4d610bf53ad0934480b189981";

    /// <summary>The world identity the golden was taken against, recorded beside the hash so a
    /// future session can tell "the fixture changed" from "the engine changed".
    /// ★ S105.2: the fixture changed — Emmett's ruling took the five-team leagues
    /// from 16 conference games to 12, so this golden was recaptured then.</summary>
    private const string MemoryGoldenWorldNote = "fixture-tiny: 20 schools, 4 leagues of 5, 12 games, skip 0";

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
            //    meet an even number of times. ★ S105.2: tiny no longer qualifies — the
            //    12-game ruling makes every tiny pair meet THREE times (all odd) — so the
            //    even world is RIGGED from tiny here: same 20 schools, same four leagues,
            //    8 games apiece (a clean double round robin, every pair meets exactly 2).
            var evenWorld = new WorldFile
            {
                SchemaVersion = WorldSchemaVersion, Kind = tiny.Kind, EraLabel = tiny.EraLabel,
                Division = tiny.Division, WorldSeed = tiny.WorldSeed, Tiers = tiny.Tiers,
                Places = tiny.Places,
                Conferences = tiny.Conferences.Select(c => c with { Games = 8 }).ToList(),
                Schools = tiny.Schools,
            };
            var evenGameCount = evenWorld.Conferences.Sum(
                c => evenWorld.Schools.Count(s => s.ConferenceId == c.Id) * c.Games / 2);
            var evenPath = Path.Combine(scratch, "even", "career.history.json");
            var even1 = PlayRetainedSeason(evenWorld, MemoryGoldenSeed, evenPath, configPath);
            HostMemory evenMemory;
            List<SeasonGame> even2;
            SeasonMemoryOutcome even2Outcome;
            using (var store = HistoryStore.Open(evenPath, WorldFingerprint(evenWorld)))
            {
                evenMemory = ReadHostMemory(store);
                even2 = BuildSeasonSchedule(evenWorld, MemoryGoldenSeed, store, out even2Outcome);
            }
            Check("C2f: a valid source with only even-split pairs LOADS and remembers nothing "
                  + "(rigged 8-game double round robin — S105.2 made tiny all-odd)",
                  evenMemory.Status == HostMemoryStatus.Loaded && evenMemory.SourceSeasonId == 1
                  && evenMemory.AttemptedSeasonId == 1 && evenMemory.Problem == HostMemoryProblem.None
                  && evenMemory.ResidualPairsRemembered == 0
                  && evenMemory.ConferenceGamesRead == evenGameCount,
                  $"{evenMemory.Status}, {evenMemory.ResidualPairsRemembered} residuals, " +
                  $"{evenMemory.ConferenceGamesRead} games read");
            Check("C2g: loaded-but-inapplicable leaves the schedule exactly as it was (C8 iv) — "
                  + "the with-memory build is byte-identical to the same world and seed's "
                  + "no-memory build",
                  even2Outcome.Status == HostMemoryStatus.Loaded && even2Outcome.SourceSeasonId == 1
                  && even2Outcome.ResidualsFlipped == 0
                  && ScheduleFingerprint(even2) == ScheduleFingerprint(even1.Schedule),
                  $"{even2Outcome.ResidualsFlipped} flipped");

            // ════════════════════════════════════════════════════════════════════
            //  C3 — the flip, at four separated layers.
            // ════════════════════════════════════════════════════════════════════
            //  (a) PURE. A hand-built record against hand-built meetings, exercising every one
            //      of the five silent skips plus the emission rule itself.
            //  ★ REWRITTEN AT S100. This asserted the exact INVERSION of a one-season memory.
            //      The rule is now "a venue is emitted only when the windowed balance is
            //      non-zero, and it names the school that is behind", so all four signs are
            //      asserted — positive, negative, ZERO and UNKNOWN — with the five skips intact.
            var pureMembers = new List<int> { 10, 20, 30, 40, 50 };
            var pureMeetings = new Dictionary<(int Lo, int Hi), int>
            {
                [(10, 20)] = 1,   // odd, balance +2 -> 20 hosts, strongest claim
                [(10, 30)] = 3,   // odd, balance -1 -> 10 hosts
                [(10, 40)] = 2,   // even this season — no venue, but the balance is NOT erased
                [(10, 50)] = 1,   // odd, balance 0 (level) -> nothing
                [(20, 30)] = 1,   // odd, every entry names a school outside the pair -> nothing
                [(20, 40)] = 1,   // odd, no entry in any readable year (unknown) -> nothing
                [(30, 40)] = 1,   // odd, balance -1 -> 30 hosts
            };
            var pureDebt = WindowedDebt(
                (1, new Dictionary<(int Lo, int Hi), int>
                {
                    [(10, 20)] = 10, [(10, 30)] = 30, [(10, 40)] = 40, [(10, 50)] = 10,
                    [(20, 30)] = 99,                 // host not in the pair: contributes nothing
                    [(30, 40)] = 40,
                    [(50, 60)] = 50,                 // both schools foreign: skipped
                    [(20, 55)] = 20,                 // one school foreign: skipped
                    [(70, 70)] = 70,                 // not a normalized pair: skipped
                }),
                // a HOLE at offset 2 — no entry at all, and it must not slide anything forward
                (3, new Dictionary<(int Lo, int Hi), int>
                {
                    [(10, 20)] = 10,                 // 10 again -> balance +2
                    [(10, 50)] = 50,                 // squares the pair -> balance 0
                    [(20, 30)] = 99,
                }));
            var pureFlips = ResidualsToFlip(pureDebt, pureMeetings, pureMembers);
            var pureWant = new[]
            {
                new FixedResidualHost(10, 20, 20),   // |2| — strongest, first in the list
                new FixedResidualHost(10, 30, 10),   // |1| — ties break by ascending pair
                new FixedResidualHost(30, 40, 30),   // |1|
            };
            Check("C3a: a venue is emitted only where the windowed balance is non-zero and it names the " +
                  "school that is behind — positive, negative, level and unknown, all five skips intact, " +
                  "strongest claim first",
                  pureFlips.Count == pureWant.Length
                  && pureFlips.Zip(pureWant).All(p => p.First == p.Second),
                  pureFlips.Count == pureWant.Length
                    ? string.Join(" ", pureFlips.Select(f => $"({f.LowSchoolId},{f.HighSchoolId})->{f.HostSchoolId}"))
                    : $"{pureFlips.Count} venues, want {pureWant.Length}");

            //  ★ AND THE BALANCE ITSELF, so a failure above diagnoses itself. Level and unknown
            //    are DIFFERENT internally even though they behave identically at emission.
            var pureBalance = HostDebtBalances(pureDebt);
            Check("C3a-ii: the balance is the residual-host difference, level is present-with-zero and " +
                  "unknown is absent, and a host outside its own pair counts for neither side",
                  pureBalance[(10, 20)] == 2 && pureBalance[(10, 30)] == -1
                  && pureBalance[(10, 40)] == -1 && pureBalance[(10, 50)] == 0
                  && pureBalance[(20, 30)] == 0 && !pureBalance.ContainsKey((20, 40))
                  && !pureBalance.ContainsKey((70, 70)),
                  $"(10,20)={pureBalance[(10, 20)]}, (10,30)={pureBalance[(10, 30)]}, " +
                  $"(10,40)={pureBalance[(10, 40)]} kept through an even season, " +
                  $"(10,50)={pureBalance[(10, 50)]} level, (20,40) unknown");

            //  (b) SLATE. Production builds a league, memory is derived from that league's OWN
            //      oriented games, production builds it again — and every odd pair inverts.
            var memLeague = LeagueMembers(memWorld, 1);
            var memConf = memWorld.Conferences.Single(c => c.Id == 1);
            var slate1 = BuildConferenceSlate(memLeague, memConf.Games, memConf.Skip,
                                              new List<(int, int)>(), "c3b ");
            var derived = OneSeasonDebt(ResidualHostsOf(slate1));
            var slate2 = BuildConferenceSlate(memLeague, memConf.Games, memConf.Skip,
                                              new List<(int, int)>(), "c3b ", debt: derived);
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
                                                    debt: OneSeasonDebt(new Dictionary<(int Lo, int Hi), int>()));
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
                                             debt: OneSeasonDebt(ResidualHostsOf(a)));
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
                                             debt: OneSeasonDebt(hostsA));
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
            //  ★ S100 CHECKED THIS BEFORE REWRITING IT, and it is NOT a property of the
            //    constructed fixture — it is arithmetic that follows from C5a and C5b. A school
            //    sits in an even number d of odd pairs (C5a) and wins d/2 of them (C5b), so the
            //    complement it wins after a full inversion is d - d/2 = d/2 whatever the world
            //    looks like. It is kept as the step a reader needs between C5b and C5d.
            //  ★ WHAT IT NO LONGER CLAIMS, and this is S100's real change: it describes the
            //    INVERT-EVERYTHING rule, which is what one readable year of debt still reduces
            //    to. It is not a promise about the windowed rule, which emits venues only where
            //    a balance is non-zero and therefore CAN over-commit a school's home quota. The
            //    flow surrenders the surplus; Phase 91 C5 is where that order is asserted.
            Check("C5c: inverting every residual re-awards exactly half — arithmetic from C5a and C5b, and " +
                  "true of the one-hop rule that a single readable year still reduces to",
                  t3.Count == 0,
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
            //  ★ REWRITTEN AT S100, NOT NARROWED — AND THIS IS THE MOST IMPORTANT ONE IN THE
            //    FILE. "Even this season" now has two levels and they are asserted separately:
            //
            //      EMISSION — no venue. There is no residual to orient, and handing one to
            //                 OrientConferenceSlate would take the whole season down.
            //      MEMORY   — the pair's balance is NOT erased. When the pair turns odd again
            //                 that balance is what decides the host.
            //
            //    The old check only ever proved the first. The second is the entire content of
            //    O-89, and a build that computed the balance perfectly and still wiped it at an
            //    even year would have passed the old form.
            var evenPair = (Lo: memLeague[0], Hi: memLeague[1]);
            var evenPairDebt = OneSeasonDebt(new Dictionary<(int Lo, int Hi), int>
            {
                [evenPair] = evenPair.Lo,
            });
            var evenMeetings = new Dictionary<(int Lo, int Hi), int> { [evenPair] = 2 };
            var dropped = ResidualsToFlip(evenPairDebt, evenMeetings, memLeague);
            // ★ S105.2 — the real-league half reads the RIGGED even world (see C2f): tiny's
            //   own pairs meet three times now, so a tiny slate WOULD fix hosts, correctly.
            var evenLeague = LeagueMembers(evenWorld, 1);
            var evenConf = evenWorld.Conferences.Single(c => c.Id == 1);
            var evenSlate = BuildConferenceSlate(
                evenLeague, evenConf.Games, evenConf.Skip, new List<(int, int)>(), "c6 ",
                debt: OneSeasonDebt(evenLeague.SelectMany(
                    a => evenLeague.Where(b => b > a).Select(b => ((Lo: a, Hi: b), a)))
                    .ToDictionary(x => x.Item1, x => x.Item2)));
            Check("C6a: EMISSION — a pair even THIS season gets no venue, and the league still builds",
                  dropped.Count == 0 && evenSlate.Verdict == SlateVerdict.Feasible
                  && evenSlate.MemoryFixedHosts == 0,
                  $"{dropped.Count} venues, {evenSlate.Verdict}");

            //  ★ MEMORY. The same pair, the same balance, read through a window in which the
            //    MOST RECENT year is the even one. The balance must survive it untouched, and
            //    the venue must appear the moment the pair is odd again.
            var throughEven = WindowedDebt(
                (1, new Dictionary<(int Lo, int Hi), int>()),                       // the doubled year
                (2, new Dictionary<(int Lo, int Hi), int> { [evenPair] = evenPair.Lo }));
            var oddAgain = new Dictionary<(int Lo, int Hi), int> { [evenPair] = 1 };
            var revived = ResidualsToFlip(throughEven, oddAgain, memLeague);
            var carried = HostDebtBalances(throughEven);
            var oneHop = ResidualsToFlip(throughEven.Within(1), oddAgain, memLeague);
            Check("C6b: ★ MEMORY — a home-and-home year in between does NOT erase the debt: the balance " +
                  "survives it and the school that is behind hosts when the pair turns odd again",
                  carried.TryGetValue(evenPair, out var carriedBalance) && carriedBalance == 1
                  && revived.Count == 1 && revived[0].HostSchoolId == evenPair.Hi,
                  $"balance {(carried.TryGetValue(evenPair, out var cb) ? cb : 0)} through the doubled year, " +
                  $"{revived.Count} venue -> {(revived.Count == 1 ? revived[0].HostSchoolId : 0)}");
            Check("C6b control: ★ AND THE PRE-S100 RULE IS SILENT ON EXACTLY THIS PAIR — one hop back sees " +
                  "the doubled year, has nothing to say, and emits nothing. This is the hole O-89 named",
                  oneHop.Count == 0, $"{oneHop.Count} venues at window 1");

            // ════════════════════════════════════════════════════════════════════
            //  C7 — foreign, malformed and self-referential memory.
            // ════════════════════════════════════════════════════════════════════
            var tinyLeague = LeagueMembers(tiny, 1);   // real schools, other league (C7a)
            var foreign = ResidualsToFlip(
                OneSeasonDebt(new Dictionary<(int Lo, int Hi), int>
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

            //  ★ STRENGTHENED AT S100, FROM SWAPS TO BALANCE. The check above counts host
            //  CHANGES, which is the one-hop question. S100's promise is about the running
            //  total, so this reads the debt off the two seasons that were actually PLAYED and
            //  requires season three's host to be the school that balance says is behind —
            //  everywhere the venue was not surrendered. That is the A3 rule in miniature:
            //  nothing here consults what was requested, only what the schedules did.
            {
                var played = WindowedDebt(
                    (1, ResidualHostsOfSchedule(alt2.Schedule)),
                    (2, ResidualHostsOfSchedule(alt1.Schedule)));
                var owedBy = HostDebtBalances(played);
                var thirdHosts = ResidualHostsOfSchedule(alt3.Schedule);
                var honoured = 0; var contradicted = 0;
                foreach (var (pair, host) in thirdHosts)
                {
                    if (!owedBy.TryGetValue(pair, out var bal) || bal == 0) continue;
                    if (host == (bal > 0 ? pair.Hi : pair.Lo)) honoured++; else contradicted++;
                }
                Check("C8i-b-ii: ★ AND THE DEBT IS READ FROM WHAT HAPPENED. Season three's hosts are checked " +
                      "against the balance computed from the two seasons actually PLAYED, and every " +
                      "contradiction is a venue the flow surrendered",
                      honoured > 0 && contradicted <= alt3.Rotation.MemoryVenuesGivenUp,
                      $"{honoured} pairs hosted by the school that was behind, {contradicted} did not, " +
                      $"{alt3.Rotation.MemoryVenuesGivenUp} venue(s) surrendered that season");
            }

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

                var flipsA = ResidualsToFlip(OneSeasonDebt(readA.PreviousResidualHost), slate1.Meetings, memLeague);
                var flipsB = ResidualsToFlip(OneSeasonDebt(readB.PreviousResidualHost), slate1.Meetings, memLeague);
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

    /// <summary>★ S100 — a debt record over ONE readable year. The pre-S100 sibling built a
    /// one-season <c>HostMemory</c>; every venue-emission check wants this instead, and a
    /// single readable year is exactly the pre-S100 rule, which is what lets the S96 checks
    /// carry over unchanged in meaning. Test-side only: production builds these by reading
    /// logs.</summary>
    private static HostDebtHistory OneSeasonDebt(IReadOnlyDictionary<(int Lo, int Hi), int> residuals)
        => new(new Dictionary<int, IReadOnlyDictionary<(int Lo, int Hi), int>> { [1] = residuals });

    /// <summary>★ S100 — a debt record over several readable years, by ABSOLUTE season offset.
    /// Offsets are supplied explicitly rather than by list position, so a check can leave a
    /// HOLE and prove it does not compress time.</summary>
    private static HostDebtHistory WindowedDebt(
        params (int Offset, Dictionary<(int Lo, int Hi), int> Residuals)[] years)
        => new(years.ToDictionary(
                   y => y.Offset,
                   y => (IReadOnlyDictionary<(int Lo, int Hi), int>)y.Residuals));

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
