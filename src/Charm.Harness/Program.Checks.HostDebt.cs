using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 91 (Session 100) — WHO IS OWED THE HOME GAME.
//
//  What this phase proves:
//    C1  the zero path is untouched — no career, a career's first season, and a
//        career with no readable year all reproduce the pre-S98 schedule;
//    C2  ★ THE PRIMARY DISCRIMINATOR — single, then home-and-home, then single:
//        the year-one host must NOT host again, with the one-hop rule run as a
//        negative control that has to fail;
//    C3  the balance arithmetic as a unit — positive, negative, level, unknown,
//        a hole that does not compress time, an eight-year span, and 2-0 proven
//        across both a hole and a doubled year;
//    C4  R3 survives — every school hosts exactly half its league season, every
//        season, over twelve;
//    C5  ★ SURRENDER PRIORITY, DIRECTLY — the weakest claim is the one that pays,
//        stronger claims survive where a feasible subset exists, and equal claims
//        break by pair id;
//    C6  ★ LONG-RUN BALANCE, MEASURED FROM PLAYED GAMES, against the isolating
//        one-hop control;
//    C7  ★ THE DEBT IS READ FROM WHAT HAPPENED — the engine's debt equals the
//        debt of the schedules that played, key for key, INCLUDING every pair
//        whose venue the flow surrendered;
//    C8  determinism over a bundle that includes both page lines;
//    C9  the rotation is undisturbed — the deep rig still covers by season 7;
//    C10 O-90 measured on the five-school rig and reported, never asserted.
//
//  ── The trap this phase is built around ────────────────────────────────────
//  Every venue assertion in Phase 87 passes on BOTH rules in season two, because
//  with one year of history they ARE the same rule: a single year's balance is
//  exactly +/-1, and "behind" is exactly "did not host last time". A two-season
//  test therefore proves nothing about this session. C2 is the only check here
//  that needs a doubled year in the middle, and it is the only one whose negative
//  control is the pre-S100 engine.
//
//  ── What this phase deliberately does not prove ────────────────────────────
//  Any particular imbalance number. C6's bound is set from a measurement of one
//  fixture and reported alongside its control; the page prints nothing new and
//  the suite asserts no basketball target value.
// ============================================================================

internal static partial class Program
{
    private const long HostDebtCheckSeed = 20260720;
    private const int HostDebtCareerSeasons = 12;

    /// <summary>★ MEASURED, NOT CHOSEN. Over twelve seasons of `fixture-rotation` the worst pair
    /// finishes three home games apart under S100; the same twelve seasons with the debt capped
    /// to one hop and the rotation left ON finish SIX apart, on twelve pairs. The bound is set
    /// from the S100 run and the control's number is printed beside it in the same line, and the
    /// check additionally requires the control to be strictly worse — so the bar can never
    /// quietly become decorative, and a build that regresses to the one-hop rule blows through
    /// it immediately.</summary>
    private const int HostDebtMaxPairImbalance = 3;

    private static bool Phase91HostDebtCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 91 — Who is owed the home game (S100: the alternation stops looking one " +
                          "year back and counts residual home games across the same window the rotation " +
                          "already reads — zero-path identity, the doubled-year discriminator with the " +
                          "one-hop rule as its negative control, the balance as arithmetic, R3, surrender " +
                          "priority, long-run balance measured from played games, and the debt read from " +
                          "what happened rather than what was intended) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s100-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(scratch);
            var mte = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-mte.world.json"));
            var memWorld = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-memory.world.json"));
            var rot = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-rotation.world.json"));

            // ════════════════════════════════════════════════════════════════════
            //  C1 — THE ZERO PATH. Three ways of having no debt, one schedule.
            // ════════════════════════════════════════════════════════════════════
            //  ★ The goldens are the PRE-S98 constants already in this suite, captured from a
            //    pristine S97 tree two sessions before this code was conceived. Reusing them is
            //    strictly stronger than capturing new ones from today's tree, and S99's Phase 90
            //    already covers the same three states — this is the S100 arm of the same claim.
            //  ★ WORDED PRECISELY: S100 supplies NO memory-derived venues on these paths, so
            //    pre-S100 orientation reproduces exactly. It is NOT a claim that S100 pins the
            //    opponent schedule; that is S99's zero-history path.
            var noCareer = RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false);
            Check("C1a: with no career at all, the conference schedule is the pre-S98 golden, orientation " +
                  "included — S100 supplied no venues, so nothing moved",
                  noCareer.ConferenceGameCount == BracketsPreS98ConferenceGameCount
                  && noCareer.Fingerprint == BracketsPreS98ConferenceScheduleSha,
                  $"{noCareer.ConferenceGameCount} games / {noCareer.Fingerprint[..16]}…");

            var firstPath = Path.Combine(scratch, "first.charm");
            List<SeasonGame> first;
            SeasonMemoryOutcome firstMemory;
            using (var store = HistoryStore.Open(firstPath, WorldFingerprint(mte)))
                first = BuildSeasonSchedule(mte, MteCheckSeed, store, deferNumbering: true,
                                            out firstMemory, out _);
            Check("C1b: season one of a career — no log can exist, so there is no debt and the schedule is " +
                  "the same one",
                  ScheduleFingerprint(first) == BracketsPreS98ConferenceScheduleSha
                  && firstMemory.Status == HostMemoryStatus.FirstSeason
                  && firstMemory.ResidualsFlipped == 0,
                  $"{ScheduleFingerprint(first)[..16]}… / {firstMemory.Status}");

            var unloggedPath = Path.Combine(scratch, "unlogged.charm");
            PlaySeasonWithoutRetention(mte, MteCheckSeed, unloggedPath, configPath);
            List<SeasonGame> afterUnlogged;
            SeasonMemoryOutcome unloggedMemory;
            using (var store = HistoryStore.Open(unloggedPath, WorldFingerprint(mte)))
                afterUnlogged = BuildSeasonSchedule(mte, MteCheckSeed, store, deferNumbering: true,
                                                    out unloggedMemory, out _);
            Check("C1c: a career whose only season published no log has no readable year anywhere in the " +
                  "window, so the debt is empty and the schedule is still the same one",
                  ScheduleFingerprint(afterUnlogged) == BracketsPreS98ConferenceScheduleSha
                  && unloggedMemory.Status == HostMemoryStatus.NoPublishedLog
                  && unloggedMemory.ResidualsFlipped == 0,
                  $"{unloggedMemory.Status} / {unloggedMemory.ResidualsFlipped} venues");

            //  ★ C1d — THE FOURTH ZERO PATH, AND IT IS A RULING RATHER THAN AN ACCIDENT. S96's
            //  fail-closed rule is that any failure to find, read or validate season N-1 disables
            //  host memory for the run, and NO other log is opened. S100 keeps that for the DEBT
            //  too: a damaged N-1 empties the window rather than letting the older years supply
            //  venues on their own. The reason is the page — it reports N-1's status beside the
            //  count of venues applied, and those two must describe the same thing.
            //  ★ ASSERTED DIRECTLY because the fixtures pass it by luck. On the five-school rig
            //  a pair odd two years ago is EVEN this year, so a build with no rule at all emits
            //  nothing here and looks correct. The assertion below reads the record, not the
            //  schedule, so it cannot be satisfied that way.
            var damagedPath = Path.Combine(scratch, "damaged.charm");
            for (var s = 0; s < 3; s++) PlayRetainedSeason(rot, HostDebtCheckSeed, damagedPath, configPath);
            var wounded = GameLogWriter.FinalPathFor(damagedPath, 3);
            var bytes = File.ReadAllBytes(wounded);
            File.WriteAllBytes(wounded, bytes[..(bytes.Length / 2)]);
            HostDebtHistory closedDebt; HostMemory closedHosts; RotationHistory survivingRotation;
            using (var store = HistoryStore.Open(damagedPath, WorldFingerprint(rot)))
            {
                var career = ReadCareerMemory(store, RotationWindowSeasons);
                closedDebt = career.Debt; closedHosts = career.Hosts; survivingRotation = career.Rotation;
            }
            Check("C1d: ★ a damaged season N-1 empties the DEBT as well as the hosts — S96's fail-closed " +
                  "rule holds, and the two older logs sitting right there are not reached for venues, " +
                  "while the ROTATION still reads them",
                  closedHosts.Status == HostMemoryStatus.Unreadable
                  && closedDebt.SeasonsRead == 0
                  && survivingRotation.SeasonsRead > 0,
                  $"{closedHosts.Status}/{closedHosts.Problem}, {closedDebt.SeasonsRead} debt year(s), " +
                  $"{survivingRotation.SeasonsRead} rotation year(s) still read");

            // ════════════════════════════════════════════════════════════════════
            //  C2 — ★ THE PRIMARY DISCRIMINATOR. Single, home-and-home, single.
            // ════════════════════════════════════════════════════════════════════
            //  On the five-school rig the extra graph is a five-cycle and its complement is the
            //  OTHER five-cycle, so every pair runs single / doubled / single / doubled for the
            //  life of a career. Under the one-hop rule year three looks back at a doubled year,
            //  finds nothing to say, and the flow is free to repeat year one's host — which,
            //  measured over twelve seasons, it does every single time. That is exactly what
            //  makes this the right fixture for the control: the control cannot pass by luck.
            //
            //  Both season-three schedules are built from the SAME two logs. The only thing that
            //  differs is how far back the debt is allowed to look.
            var discPath = Path.Combine(scratch, "disc.charm");
            var disc1 = PlayRetainedSeason(memWorld, HostDebtCheckSeed, discPath, configPath);
            var disc2 = PlayRetainedSeason(memWorld, HostDebtCheckSeed, discPath, configPath);
            List<SeasonGame> disc3Debt, disc3OneHop;
            SeasonRotationOutcome disc3Rotation;
            using (var store = HistoryStore.Open(discPath, WorldFingerprint(memWorld)))
            {
                disc3Debt = BuildSeasonSchedule(memWorld, HostDebtCheckSeed, store, deferNumbering: true,
                                                out _, out disc3Rotation);
                disc3OneHop = BuildSeasonSchedule(memWorld, HostDebtCheckSeed, store, deferNumbering: true,
                                                  out _, out _, debtWindowOverride: 1);
            }

            var h1 = ResidualHostsOfSchedule(disc1.Schedule);
            var h2 = ResidualHostsOfSchedule(disc2.Schedule);
            var h3 = ResidualHostsOfSchedule(disc3Debt);
            var h3One = ResidualHostsOfSchedule(disc3OneHop);
            // The pairs this session is about: odd in year one, DOUBLED in year two, odd again
            // in year three. Found at runtime, never named by hand.
            var holePairs = h1.Keys.Where(p => !h2.ContainsKey(p) && h3.ContainsKey(p)).OrderBy(p => p.Lo)
                                   .ThenBy(p => p.Hi).ToList();
            var debtSwapped = holePairs.Count(p => h3[p] != h1[p]);
            var oneHopRepeated = holePairs.Count(p => h3One.TryGetValue(p, out var x) && x == h1[p]);
            Check("C2a: ★ SINGLE, HOME-AND-HOME, SINGLE — the year-one host does NOT host again. The " +
                  "doubled year no longer erases the debt",
                  holePairs.Count > 0 && debtSwapped == holePairs.Count,
                  $"{holePairs.Count} pairs took a home-and-home year in between, {debtSwapped} swapped, " +
                  $"{disc3Rotation.MemoryVenuesGivenUp} venue(s) surrendered");
            Check("C2b: ★ AND THE ONE-HOP RULE FAILS THIS, ON THE SAME TWO LOGS. Window 1 sees the doubled " +
                  "year, has nothing to say, and year one's host takes it again",
                  holePairs.Count > 0 && oneHopRepeated == holePairs.Count,
                  $"{oneHopRepeated} of {holePairs.Count} repeated year one's host at window 1");

            // ════════════════════════════════════════════════════════════════════
            //  C3 — THE BALANCE ARITHMETIC, AS A UNIT. No world, no disk.
            // ════════════════════════════════════════════════════════════════════
            //  Offsets are ABSOLUTE calendar distances. Note the deliberate hole at 2 and 3, and
            //  the deliberate doubled year at 5 — the pair says nothing in either, and neither
            //  may slide an older year forward or wipe what is already counted.
            {
                var span = WindowedDebt(
                    (1, new Dictionary<(int Lo, int Hi), int> { [(10, 20)] = 10, [(30, 40)] = 40 }),
                    // 2 and 3: HOLES — unreadable years, no entry at all
                    (4, new Dictionary<(int Lo, int Hi), int> { [(10, 20)] = 10, [(30, 40)] = 30 }),
                    // 5: a year that validated and had no odd pair for these schools
                    (5, new Dictionary<(int Lo, int Hi), int>()),
                    (6, new Dictionary<(int Lo, int Hi), int> { [(10, 20)] = 20, [(50, 60)] = 50 }),
                    (8, new Dictionary<(int Lo, int Hi), int> { [(30, 40)] = 30, [(50, 60)] = 60 }));
                var bal = HostDebtBalances(span);
                Check("C3a: POSITIVE, NEGATIVE, LEVEL and UNKNOWN, over an eight-year span",
                      bal[(10, 20)] == 1 && bal[(30, 40)] == 1 && bal[(50, 60)] == 0
                      && !bal.ContainsKey((70, 80)),
                      $"(10,20)=+{bal[(10, 20)]} Lo ahead, (30,40)=+{bal[(30, 40)]}, " +
                      $"(50,60)={bal[(50, 60)]} level, (70,80) unknown");

                //  ★ 2-0 ACROSS BOTH A HOLE AND A DOUBLED YEAR. School 30 takes the residual in
                //  two NON-CONSECUTIVE years against none for 40, with an unreadable year and a
                //  doubled year sitting between them. Neither may compress or erase.
                var twoNil = WindowedDebt(
                    (1, new Dictionary<(int Lo, int Hi), int> { [(30, 40)] = 30 }),
                    (2, new Dictionary<(int Lo, int Hi), int>()),          // doubled: says nothing
                    // 3: a HOLE
                    (4, new Dictionary<(int Lo, int Hi), int> { [(30, 40)] = 30 }));
                var twoNilBal = HostDebtBalances(twoNil);
                var twoNilVenues = ResidualsToFlip(
                    twoNil, new Dictionary<(int Lo, int Hi), int> { [(30, 40)] = 1 },
                    new List<int> { 30, 40 });
                Check("C3b: ★ 2-0 ACROSS BOTH A HOLE AND A DOUBLED YEAR — the count is two, and the venue " +
                      "names the school that has hosted neither",
                      twoNilBal[(30, 40)] == 2 && twoNilVenues.Count == 1
                      && twoNilVenues[0].HostSchoolId == 40,
                      $"balance {twoNilBal[(30, 40)]}, venue -> {twoNilVenues[0].HostSchoolId}");

                //  A hole must not slide an older year forward. Read through window 3, the
                //  offset-4 year is OUTSIDE the window and the balance must fall to one.
                Check("C3c: a hole does not compress time — narrowing the window to three drops the " +
                      "offset-four year rather than sliding it up",
                      HostDebtBalances(twoNil.Within(3))[(30, 40)] == 1,
                      $"balance {HostDebtBalances(twoNil.Within(3))[(30, 40)]} at window 3");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 / C6 / C9 — one twelve-season career, and its isolating control.
            // ════════════════════════════════════════════════════════════════════
            //  ★ THE CONTROL IS ROTATION ON, DEBT WINDOW 1 — the only thing that differs is the
            //    depth of the debt read. A rotation-OFF control would confound: freezing which
            //    pairs are doubled changes how often a pair plays a single game at all, so its
            //    imbalance number would be measuring a different world.
            var careerPath = Path.Combine(scratch, "career.charm");
            var controlPath = Path.Combine(scratch, "control.charm");
            var schedules = new List<List<SeasonGame>>();
            var controls = new List<List<SeasonGame>>();
            var venuesGivenUp = 0;
            for (var s = 0; s < HostDebtCareerSeasons; s++)
            {
                var run = PlayRetainedSeason(rot, HostDebtCheckSeed, careerPath, configPath);
                schedules.Add(run.Schedule);
                venuesGivenUp += run.Rotation.MemoryVenuesGivenUp;
                var ctl = PlayRetainedSeasonAtDebtWindow(rot, HostDebtCheckSeed, controlPath, configPath, 1);
                controls.Add(ctl.Schedule);
            }

            var quotaBad = new List<string>();
            for (var s = 0; s < schedules.Count; s++)
                if (!RotationHomeAwayEven(schedules[s], rot)) quotaBad.Add($"season {s + 1}");
            Check($"C4: R3 HOLDS — every school hosts exactly half its league season, every one of " +
                  $"{HostDebtCareerSeasons} seasons, with the debt deciding venues",
                  quotaBad.Count == 0,
                  quotaBad.Count == 0 ? "quota exact everywhere" : string.Join("; ", quotaBad.Take(4)));

            //  ── C6 — the long run, measured from the games that PLAYED ──
            static (int Max, int AtMax, int TwoPlus, int Pairs, string Hist) PairImbalance(
                List<List<SeasonGame>> career)
            {
                var lo = new Dictionary<(int Lo, int Hi), int>();
                var hi = new Dictionary<(int Lo, int Hi), int>();
                foreach (var season in career)
                    foreach (var g in season)
                    {
                        var key = (Lo: Math.Min(g.HomeId, g.AwayId), Hi: Math.Max(g.HomeId, g.AwayId));
                        lo.TryAdd(key, 0); hi.TryAdd(key, 0);
                        if (g.HomeId == key.Lo) lo[key]++; else hi[key]++;
                    }
                var buckets = new Dictionary<int, int>();
                foreach (var key in lo.Keys)
                {
                    var d = Math.Abs(lo[key] - hi[key]);
                    buckets[d] = buckets.GetValueOrDefault(d) + 1;
                }
                var max = buckets.Keys.Max();
                return (max, buckets[max], buckets.Where(b => b.Key >= 2).Sum(b => b.Value), lo.Count,
                        string.Join(" ", buckets.OrderBy(b => b.Key).Select(b => $"{b.Key}:{b.Value}")));
            }

            var debtImb = PairImbalance(schedules);
            var oneHopImb = PairImbalance(controls);
            Check($"C6: ★ LONG-RUN BALANCE FROM PLAYED GAMES — over {HostDebtCareerSeasons} seasons no pair's " +
                  $"home-game gap exceeds {HostDebtMaxPairImbalance}, against the one-hop control measured " +
                  "on the same world with the rotation left ON",
                  debtImb.Max <= HostDebtMaxPairImbalance && debtImb.Max < oneHopImb.Max,
                  $"S100 max {debtImb.Max} across {debtImb.AtMax} of {debtImb.Pairs} pairs " +
                  $"({debtImb.TwoPlus} at 2+), distribution [{debtImb.Hist}] — ONE-HOP CONTROL max " +
                  $"{oneHopImb.Max} across {oneHopImb.AtMax} pairs ({oneHopImb.TwoPlus} at 2+), " +
                  $"[{oneHopImb.Hist}]; {venuesGivenUp} venue(s) surrendered over the career");

            //  ── C9 — the rotation is undisturbed ──
            var deep = LeagueMembers(rot, RotationDeepConference);
            var deepWalk = RotationCoverage(schedules, deep, q: 1);
            Check($"C9: the rotation still covers — every school on the deep rig has played every opponent " +
                  $"twice by season {RotationDeepCoverBySeason}, exactly as Phase 90 requires",
                  deepWalk.CoveredBy is not null && deepWalk.CoveredBy <= RotationDeepCoverBySeason
                  && deepWalk.MinDistinct == deepWalk.Opponents,
                  $"covered by season {deepWalk.CoveredBy?.ToString() ?? "never"}, " +
                  $"min distinct {deepWalk.MinDistinct}/{deepWalk.Opponents}");

            // ════════════════════════════════════════════════════════════════════
            //  C7 — ★ THE DEBT IS READ FROM WHAT HAPPENED, NOT WHAT WAS INTENDED.
            // ════════════════════════════════════════════════════════════════════
            //  The flow surrenders venues it cannot orient. A surrendered venue is not a lost
            //  instruction: the game is played somewhere, the log records the actual host, and
            //  the next season's debt reads THAT. So the engine's own debt, read off disk, must
            //  equal the debt of the schedules that actually played — key for key, including
            //  every pair whose venue was given up. If any ledger of intentions existed, the two
            //  would disagree exactly there.
            {
                HostDebtHistory fromDisk;
                using (var store = HistoryStore.Open(careerPath, WorldFingerprint(rot)))
                    fromDisk = ReadCareerMemory(store, RotationWindowSeasons).Debt;

                var fromPlayed = WindowedDebt(
                    Enumerable.Range(1, Math.Min(RotationWindowSeasons, schedules.Count))
                              .Select(k => (k, ResidualHostsOfSchedule(schedules[^k])))
                              .ToArray());
                var diskBal = HostDebtBalances(fromDisk);
                var playedBal = HostDebtBalances(fromPlayed);
                var mismatch = playedBal.Count(kv =>
                    !diskBal.TryGetValue(kv.Key, out var v) || v != kv.Value);
                Check("C7a: ★ the engine's debt equals the debt of the schedules that PLAYED, key for key — " +
                      "there is no ledger of intentions to disagree with",
                      diskBal.Count == playedBal.Count && mismatch == 0 && playedBal.Count > 0,
                      $"{playedBal.Count} pairs, {mismatch} mismatched, {diskBal.Count} read from disk");
                Check("C7a control: ★ AND THE CAREER REALLY DID SURRENDER VENUES, so the assertion above " +
                      "ranges over pairs that were played AGAINST what the debt asked for",
                      venuesGivenUp > 0, $"{venuesGivenUp} venue(s) surrendered across the career");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — ★ SURRENDER PRIORITY, DIRECTLY.
            // ════════════════════════════════════════════════════════════════════
            //  Without this the build can compute the balance perfectly and keep the old drop
            //  order, and every other check in this file stays green.
            {
                //  The pure order first, so a failure below diagnoses itself.
                var ordMembers = new List<int> { 10, 20, 30, 40, 50, 60 };
                var ordMeetings = new Dictionary<(int Lo, int Hi), int>
                {
                    [(10, 20)] = 1, [(10, 30)] = 1, [(10, 40)] = 1, [(20, 30)] = 1, [(20, 40)] = 1,
                };
                var ordDebt = WindowedDebt(
                    (1, new Dictionary<(int Lo, int Hi), int> { [(10, 20)] = 20, [(10, 30)] = 30, [(10, 40)] = 40, [(20, 30)] = 30, [(20, 40)] = 40 }),
                    (2, new Dictionary<(int Lo, int Hi), int> { [(10, 20)] = 20, [(10, 30)] = 30, [(20, 30)] = 30 }),
                    (3, new Dictionary<(int Lo, int Hi), int> { [(10, 20)] = 20 }));
                //  balances: (10,20) -3, (10,30) -2, (10,40) -1, (20,30) -2, (20,40) -1
                var ordered = ResidualsToFlip(ordDebt, ordMeetings, ordMembers);
                var wantOrder = new[]
                {
                    new FixedResidualHost(10, 20, 10),   // |3|
                    new FixedResidualHost(10, 30, 10),   // |2|, ties by ascending pair
                    new FixedResidualHost(20, 30, 20),   // |2|
                    new FixedResidualHost(10, 40, 10),   // |1|
                    new FixedResidualHost(20, 40, 20),   // |1|
                };
                Check("C5a: the emitted list is strongest claim first, and equal claims break by ascending " +
                      "pair id — deterministically",
                      ordered.SequenceEqual(wantOrder),
                      string.Join(" ", ordered.Select(f => $"({f.LowSchoolId},{f.HighSchoolId})->{f.HostSchoolId}")));

                //  ── The slate level. A school named as host in MORE of its odd pairs than its
                //     residual home budget can absorb: the flow must refuse the whole set and
                //     the loop must give up the WEAKEST claims first.
                //  ★ Which pairs are odd is read off a real slate rather than assumed, so the
                //    construction cannot drift if the solver's extra graph ever moves.
                var sm = new List<int> { 10, 20, 30, 40, 50, 60 };
                var baseSlate = BuildConferenceSlate(sm, 6, 0, new List<(int, int)>(), "c5 base ");
                var oddOf10 = baseSlate.Meetings.Where(kv => kv.Value % 2 == 1
                                                    && (kv.Key.Lo == 10 || kv.Key.Hi == 10))
                                                .Select(kv => kv.Key)
                                                .OrderBy(k => k.Lo).ThenBy(k => k.Hi).ToList();
                var budget = oddOf10.Count / 2;
                //  Give school 10 a claim on every one of its odd pairs, with descending strength
                //  so the surrender order is observable: 4, 3, 2, 1, …
                var years = new List<(int, Dictionary<(int Lo, int Hi), int>)>();
                for (var k = 1; k <= oddOf10.Count; k++)
                {
                    var year = new Dictionary<(int Lo, int Hi), int>();
                    for (var i = 0; i + k <= oddOf10.Count; i++)
                        year[oddOf10[i]] = oddOf10[i].Lo == 10 ? oddOf10[i].Hi : oddOf10[i].Lo;
                    years.Add((k, year));
                }
                var pressDebt = WindowedDebt(years.ToArray());
                var pressed = BuildConferenceSlate(sm, 6, 0, new List<(int, int)>(), "c5 press ",
                                                   debt: pressDebt);
                var wanted = ResidualsToFlip(pressDebt, baseSlate.Meetings, sm);
                var pressedHosts = ResidualHostsOf(pressed);
                var survivors = wanted.Count(f =>
                    pressedHosts.TryGetValue((f.LowSchoolId, f.HighSchoolId), out var h)
                    && h == f.HostSchoolId);
                var strongestHeld = wanted.Take(budget).All(f =>
                    pressedHosts.TryGetValue((f.LowSchoolId, f.HighSchoolId), out var h)
                    && h == f.HostSchoolId);
                Check($"C5b: ★ over-committed by construction — school 10 is owed all {oddOf10.Count} of its " +
                      $"odd pairs and can host {budget} — the league still builds and the STRONGEST claims " +
                      "are the ones that survive",
                      pressed.Verdict == SlateVerdict.Feasible && wanted.Count > budget
                      && pressed.MemoryFlipsDropped > 0 && strongestHeld,
                      $"{wanted.Count} venues asked, {pressed.MemoryFixedHosts} supplied, " +
                      $"{pressed.MemoryFlipsDropped} surrendered, {survivors} honoured");
                Check("C5b control: ★ AND THE WEAKEST CLAIM IS THE ONE THAT PAID. The pair with the " +
                      "smallest debt is not among the venues that were honoured",
                      !pressedHosts.TryGetValue((wanted[^1].LowSchoolId, wanted[^1].HighSchoolId), out var last)
                      || last != wanted[^1].HostSchoolId,
                      $"weakest claim ({wanted[^1].LowSchoolId},{wanted[^1].HighSchoolId}) " +
                      $"asked for {wanted[^1].HostSchoolId}");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — determinism over a bundle that includes both page lines.
            // ════════════════════════════════════════════════════════════════════
            {
                var twinA = Path.Combine(scratch, "twinA.charm");
                var twinB = Path.Combine(scratch, "twinB.charm");
                string BundleOf(string path)
                {
                    for (var s = 0; s < 3; s++) PlayRetainedSeason(memWorld, HostDebtCheckSeed, path, configPath);
                    var last = PlayRetainedSeason(memWorld, HostDebtCheckSeed, path, configPath);
                    return string.Join("|",
                        ScheduleFingerprint(last.Schedule),
                        HostMemoryPageLine(last.Memory) ?? "(none)",
                        RotationPageLine(last.Rotation) ?? "(none)",
                        last.Rotation.MemoryVenuesGivenUp.ToString());
                }
                var bundleA = BundleOf(twinA);
                var bundleB = BundleOf(twinB);
                Check("C8: two identical careers produce the identical fourth season — schedule, both page " +
                      "lines unchanged in format, and the surrender count",
                      bundleA == bundleB, bundleA.Split('|')[1]);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10 — O-90, MEASURED AND REPORTED. Never asserted.
            // ════════════════════════════════════════════════════════════════════
            //  The five-school league is the degenerate case: the extra graph is a five-cycle
            //  and its complement is the other five-cycle, so a pair alternates doubled and
            //  single forever and NO pair is ever odd two years running. Under the one-hop rule
            //  that meant host memory had nothing to say at all. Recording what it looks like
            //  now is the deliverable; it may still be imperfect and saying so is the point.
            {
                var o90Path = Path.Combine(scratch, "o90.charm");
                var o90 = new List<List<SeasonGame>>();
                for (var s = 0; s < HostDebtCareerSeasons; s++)
                    o90.Add(PlayRetainedSeason(memWorld, HostDebtCheckSeed, o90Path, configPath).Schedule);
                var o90Imb = PairImbalance(o90);
                var o90Control = Path.Combine(scratch, "o90c.charm");
                var o90c = new List<List<SeasonGame>>();
                for (var s = 0; s < HostDebtCareerSeasons; s++)
                    o90c.Add(PlayRetainedSeasonAtDebtWindow(memWorld, HostDebtCheckSeed, o90Control,
                                                            configPath, 1).Schedule);
                var o90cImb = PairImbalance(o90c);
                Check("C10: O-90 measured on the five-school rig — reported, not asserted",
                      true,
                      $"S100 max {o90Imb.Max} across {o90Imb.AtMax} of {o90Imb.Pairs} pairs " +
                      $"[{o90Imb.Hist}] vs ONE-HOP max {o90cImb.Max} across {o90cImb.AtMax} pairs " +
                      $"[{o90cImb.Hist}]");
            }
        }
        catch (Exception ex)
        {
            Check("Phase 91 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
            catch { /* a leftover temp folder is not a test failure */ }
        }

        Console.WriteLine(pass ? "  Phase 91: PASS" : "  Phase 91: FAIL");
        return pass;
    }

    /// <summary>★ S100 — a retained season played with the debt capped to a shallower window.
    /// The isolating control for C6 and C10: the rotation keeps its full depth, so the only
    /// thing that differs between the two careers is how far back the host debt may look.</summary>
    private static SeasonRunOutcome PlayRetainedSeasonAtDebtWindow(
        WorldFile world, long seed, string historyPath, string configPath, int debtWindow)
    {
        using var store = HistoryStore.Open(historyPath, WorldFingerprint(world));
        return RunSeasonCore(world, seed, configPath, verbose: false, store, retainGameLog: true,
                             debtWindowOverride: debtWindow);
    }
}
