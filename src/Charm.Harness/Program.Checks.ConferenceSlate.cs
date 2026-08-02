namespace Charm.Harness;

// ============================================================================
// Phase 84 — THE CONFERENCE SLATE (Session 93).
//
// ★ THE ONE ASSERTION THAT DISCRIMINATES IS A9, AND EVERY OTHER LINE IN THIS
// FILE SHOULD BE READ IN ITS SHADOW. Measured before this session changed
// anything: all 347 stock schools already sat at exactly 8 home and 8 away in
// conference play, at eight different seeds, in all 32 leagues. The pre-S93
// orientation was ONE Eulerian circuit over every game, and every degree was
// even, so out == in at every school by accident of arithmetic. That means an
// assertion that "every team is exactly even" PASSES ON THE OLD CODE and is
// therefore worthless as evidence that the new orientation works. A3 is in this
// file because the promise matters, not because it proves anything.
//
// What the flow buys, and what A9 exercises, is PRE-FIXED VENUES: Session 94
// will hand the orientation a set of games whose host is already decided by
// memory of who hosted last time, and a Eulerian walk cannot honour those. A9's
// three arms are the discriminating ones —
//   (a) hosts sampled from a known-legal orientation, all honoured, everyone
//       still exactly even;
//   (b) an over-committed set refused by the cheap quota guard BEFORE any flow
//       structure exists, naming the school;
//   (c) ★ a set that passes the cheap guard and is still impossible — three
//       schools each owed one home game, two of the three residuals already
//       decided against one school, so the totals reconcile and the last home
//       game has nowhere to live. Only the flow itself can refuse this one.
// A Eulerian walk fails all three.
//
// The other axis worth naming: A1, A2, A3 and A5 are asserted PER SCHOOL, never
// as a league average. Seventeen of the 32 stock leagues have zero unbalanced
// games — every pair meets an even number of times, so their orientation is
// free — and a check that looked at league-wide averages would be dominated by
// leagues where nothing had to be decided.
//
// What this phase does NOT prove: that the slates are good basketball. Who
// should be doubled and who should be skipped is a design question with a
// coach's answer, and this session's only tie-break is the deterministic one.
// ============================================================================

internal static partial class Program
{
    // ★ Oracle exports (tools/schedule_oracle.py). The schedule consumes no randomness,
    //   so these are a function of the WORLD alone.
    private const string SlateOracleFixtureFp = "5698eb1b2532c2003c920fb647fa57be26eeb2ad36e5335fb645a43445e0db52";

    private static bool Phase84ConferenceSlateCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 84 — The conference slate (authored counts, skips, rivalries, whose gym) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var schedPath = Path.Combine(baseDir, "worlds", "fixture-schedule.world.json");
            var fixture = LoadWorld(schedPath);
            var stock = ConvertWorld(
                Path.Combine(baseDir, "data", "teams.csv"),
                Path.Combine(baseDir, "data", "conf.csv"),
                Path.Combine(baseDir, "data", "places.csv"));
            const long seed = 20260703;

            // The fixture's design, stated where the assertions live (see the file header of
            // worlds/fixture-schedule.world.json's generator in the journal):
            //   1 Odd Valley  n=13 G=16 k=2 -> p=10 q=1 r=6   odd-sized WITH skips; q odd, so
            //                                                 most opponents are met an odd
            //                                                 number of times and the flow has
            //                                                 26 residuals to decide
            //   2 Skipline    n= 8 G=10 k=2 -> p= 5 q=2 r=0   r = 0 WITH skips
            //   3 Duo        n= 2 G=16 k=0 -> p= 1 q=16 r=0   the two-school conference
            //   4 Idle        n= 3 G= 0 k=0                   the zero-game conference (R14)
            //   5 Plain       n= 6 G=10 k=0 -> p= 5 q=2 r=0   every pair meets the same number
            // Rivalries: 1-12 (Odd Valley, on a pair the baseline does NOT double),
            //            14-15 (Skipline, on a pair the baseline DOES skip),
            //            24-25 (both inside Idle — dormant, zero-game league),
            //            26-27 (Idle to Plain — dormant, cross-conference),
            //            28-29 (Plain — a league where the rivalry must change nothing).

            static Dictionary<int, List<int>> ByConference(WorldFile w)
            {
                var d = new Dictionary<int, List<int>>();
                foreach (var s in w.Schools.OrderBy(x => x.Id))
                {
                    if (!d.TryGetValue(s.ConferenceId, out var l)) d[s.ConferenceId] = l = new List<int>();
                    l.Add(s.Id);
                }
                return d;
            }
            static Dictionary<(int, int), int> PairCounts(List<SeasonGame> games)
            {
                var p = new Dictionary<(int, int), int>();
                foreach (var g in games)
                {
                    var k = (Math.Min(g.HomeId, g.AwayId), Math.Max(g.HomeId, g.AwayId));
                    p[k] = p.GetValueOrDefault(k) + 1;
                }
                return p;
            }

            var schedule = BuildSeasonSchedule(fixture, seed);
            var pairs = PairCounts(schedule);
            var byConf = ByConference(fixture);
            var confById = fixture.Conferences.ToDictionary(c => c.Id);
            var played = fixture.Schools.ToDictionary(s => s.Id, _ => 0);
            var home = fixture.Schools.ToDictionary(s => s.Id, _ => 0);
            foreach (var g in schedule) { played[g.HomeId]++; played[g.AwayId]++; home[g.HomeId]++; }

            // ── A1 — the histogram, branched, PER SCHOOL ────────────────────────────
            var a1 = true; var a1d = "";
            foreach (var (cid, members) in byConf.OrderBy(kv => kv.Key))
            {
                var c = confById[cid];
                foreach (var x in members)
                {
                    var counts = members.Where(y => y != x)
                        .Select(y => pairs.GetValueOrDefault((Math.Min(x, y), Math.Max(x, y)))).ToList();
                    if (c.Games == 0)
                    {
                        // ★ THE ZERO-GAME BRANCH IS NOT THE k/q/r FORMULA. Every mate is met
                        //   zero times, and k = 0 does NOT mean "zero opponents skipped" here.
                        if (counts.Any(v => v != 0))
                        { a1 = false; a1d = $"idle conf {cid}: school {x} met a mate"; break; }
                        continue;
                    }
                    var p = members.Count - 1 - c.Skip; var q = c.Games / p; var r = c.Games - q * p;
                    if (counts.Count(v => v == 0) != c.Skip || counts.Count(v => v == q + 1) != r
                        || counts.Count(v => v == q) != p - r)
                    {
                        a1 = false;
                        a1d = $"conf {cid} school {x}: {counts.Count(v => v == 0)}/{c.Skip} skipped, " +
                              $"{counts.Count(v => v == q + 1)}/{r} at {q + 1}, {counts.Count(v => v == q)}/{p - r} at {q}";
                        break;
                    }
                }
                if (!a1) break;
            }
            Check("A1 every school meets k opponents never, r opponents q+1 times and the rest q " +
                  "times — and a zero-game league meets ALL its mates never", a1, a1d);

            // ── A2 — the count ──────────────────────────────────────────────────────
            Check("A2 every school plays exactly its conference's authored Games",
                  fixture.Schools.All(s => played[s.Id] == confById[s.ConferenceId].Games),
                  string.Join(", ", fixture.Schools.Where(s => played[s.Id] != confById[s.ConferenceId].Games)
                      .Take(3).Select(s => $"{s.Id} plays {played[s.Id]} of {confById[s.ConferenceId].Games}")));

            // ── A3 — exact even home/away, per school ──────────────────────────────
            Check("A3 exactly even home/away for every school (Games/2 each) — R3. " +
                  "★ PASSES ON THE OLD CODE TOO; see A9 for the assertion that discriminates",
                  fixture.Schools.All(s => home[s.Id] == confById[s.ConferenceId].Games / 2),
                  string.Join(", ", fixture.Schools.Where(s => home[s.Id] != confById[s.ConferenceId].Games / 2)
                      .Take(3).Select(s => $"{s.Id} hosts {home[s.Id]}")));

            // ── A4 — edge-disjointness ─────────────────────────────────────────────
            var a4 = true; var a4d = "";
            foreach (var (cid, members) in byConf.OrderBy(kv => kv.Key))
            {
                var c = confById[cid];
                if (c.Games == 0) continue;
                var p = members.Count - 1 - c.Skip; var q = c.Games / p;
                for (var i = 0; i < members.Count - 1 && a4; i++)
                    for (var j = i + 1; j < members.Count; j++)
                    {
                        var m = pairs.GetValueOrDefault((members[i], members[j]));
                        if (m != 0 && m != q && m != q + 1)
                        { a4 = false; a4d = $"conf {cid} pair ({members[i]},{members[j]}) meets {m}"; break; }
                    }
                if (!a4) break;
            }
            Check("A4 no pair is both skipped and doubled — every meeting count is 0, q or q+1",
                  a4, a4d);

            // ── A5 — rivalry placement, all three arms ─────────────────────────────
            var rivalOf = fixture.Schools.ToDictionary(s => s.Id, s => s.RivalId);
            Check("A5a where r > 0, an active rivalry sits at the TOP meeting count q+1 " +
                  "(Odd Valley: 1-12)",
                  pairs.GetValueOrDefault((1, 12)) == 2, $"meets {pairs.GetValueOrDefault((1, 12))}");
            Check("A5b where r = 0, an active rivalry is simply NOT the one skipped " +
                  "(Skipline: 14-15)",
                  pairs.GetValueOrDefault((14, 15)) > 0, $"meets {pairs.GetValueOrDefault((14, 15))}");
            Check("A5c where every pair already meets the same number of times, a rivalry changes " +
                  "NOTHING — asserted so it cannot quietly acquire a meaning (Plain: 28-29)",
                  pairs.GetValueOrDefault((28, 29)) == 2 &&
                  byConf[5].Where(x => x != 28)
                           .All(y => pairs.GetValueOrDefault((Math.Min(28, y), Math.Max(28, y))) == 2),
                  $"28-29 meets {pairs.GetValueOrDefault((28, 29))}");

            // ── A6 — dormancy, BOTH kinds ─────────────────────────────────────────
            Check("A6a a CROSS-CONFERENCE rivalry loads, validates and schedules without error, " +
                  "and constrains nothing (26 in Idle names 27 in Plain)",
                  rivalOf[26] == 27 && rivalOf[27] == 26
                    && pairs.GetValueOrDefault((26, 27)) == 0);
            Check("A6b a rivalry between two members of a ZERO-GAME conference is dormant, never " +
                  "an error — it cannot be placed in a shape that does not exist (24-25 in Idle)",
                  rivalOf[24] == 25 && rivalOf[25] == 24
                    && pairs.GetValueOrDefault((24, 25)) == 0
                    && played[24] == 0 && played[25] == 0);

            // ── A7 — the zero-game conference ─────────────────────────────────────
            Check("A7 a conference authored at zero games plays no conference season: its schools " +
                  "appear in no game, and the season still builds (R14)",
                  byConf[4].All(s => played[s] == 0 && home[s] == 0)
                    && schedule.All(g => !byConf[4].Contains(g.HomeId) && !byConf[4].Contains(g.AwayId)));

            // ── A7b — the identity ────────────────────────────────────────────────
            var expected = fixture.Conferences.Sum(c => byConf.GetValueOrDefault(c.Id, new()).Count * c.Games / 2);
            Check("A7b the season is exactly the sum of the authored league slates, derived from " +
                  "the world rather than written down",
                  schedule.Count == expected, $"{schedule.Count} games, world says {expected}");

            // ── A8 — legality is a predicate with holes ────────────────────────────
            var table = new (int N, int G, int K, bool Legal, string Needle)[]
            {
                (5,  6, 1, false, "odd on both"),      // n*k = 5 is odd: no 1-regular graph on 5
                (9, 16, 1, false, "odd on both"),      // an ODD-sized league can never carry an odd k
                (9, 16, 2, true,  ""),
                (13, 16, 2, true, ""),
                (2, 16, 0, true,  ""),
                (1, 16, 0, false, "needs an opponent"),
                (1,  0, 0, true,  ""),                 // a one-school conference of independents
                (10, 15, 0, false, "odd"),             // an odd game count
                (10, 32, 0, false, "maximum"),         // past the regular-season maximum
                (10,  0, 2, false, "requires Skip 0"), // a suspended league carries a canonical k
                (10, 18, 9, false, "leaves no opponent"),   // k > n-2: nobody left to play
                (12,  4, 0, false, "every played opponent must get a game"),
            };
            var a8 = true; var a8d = "";
            foreach (var (n, g, k, legal, needle) in table)
            {
                var reason = ConferenceSlateLegality(n, g, k);
                var ok = legal ? reason is null
                               : reason is not null && reason.Contains(needle, StringComparison.Ordinal);
                if (!ok) { a8 = false; a8d = $"(n={n},G={g},k={k}) -> {reason ?? "legal"}"; break; }
            }
            Check($"A8 the legality predicate returns the exact verdict on all {table.Length} table " +
                  "cases, each rejection named by its reason", a8, a8d);

            // The precedence order, exercised: a LEGAL configuration above the cap reports the
            // size, not infeasibility — and an ILLEGAL oversized one reports the illegality.
            var over = BuildConferenceSlate(Enumerable.Range(1, 21).ToList(), 20, 0, new(), "over ");
            var overBad = BuildConferenceSlate(Enumerable.Range(1, 21).ToList(), 15, 0, new(), "over ");
            Check("A8b precedence: a LEGAL configuration above the size cap reports " +
                  "UnsupportedConferenceSize; one that is BOTH illegal and oversized reports " +
                  "InvalidConfiguration",
                  over.Verdict == SlateVerdict.UnsupportedConferenceSize
                    && overBad.Verdict == SlateVerdict.InvalidConfiguration,
                  $"{over.Verdict} / {overBad.Verdict}");

            // ── A9 — ★ THE DISCRIMINATING ASSERTION, three arms ────────────────────
            var l1 = byConf[1];
            var c1 = confById[1];
            var baseline = BuildConferenceSlate(l1, c1.Games, c1.Skip,
                ActiveRivalries(l1, rivalOf, c1.Games), "A9 ");
            // Recover the baseline's residual host decisions: a residual is the LAST game of a
            // pair that meets an odd number of times.
            var residuals = new List<FixedResidualHost>();
            var pos = 0;
            for (var i = 0; i < l1.Count - 1; i++)
                for (var j = i + 1; j < l1.Count; j++)
                {
                    var m = baseline.Meetings[(l1[i], l1[j])];
                    for (var t = 0; t < m; t++)
                    {
                        if (m % 2 == 1 && t == m - 1)
                            residuals.Add(new FixedResidualHost(l1[i], l1[j], baseline.Games[pos].Home));
                        pos++;
                    }
                }
            // ★ SAMPLED FROM THE BASELINE, so feasibility is guaranteed by construction and a
            //   failure indicts the solver, never the test data.
            var sample = residuals.Where((_, idx) => idx % 3 == 0).ToList();
            var touched = sample.SelectMany(f => new[] { f.LowSchoolId, f.HighSchoolId }).Distinct().Count();
            var fixedRun = BuildConferenceSlate(l1, c1.Games, c1.Skip,
                ActiveRivalries(l1, rivalOf, c1.Games), "A9 ", sample);
            var honoured = fixedRun.Verdict == SlateVerdict.Feasible;
            var evenAfter = true;
            if (honoured)
            {
                var h = l1.ToDictionary(s => s, _ => 0);
                pos = 0;
                var byPair = sample.ToDictionary(f => (f.LowSchoolId, f.HighSchoolId), f => f.HostSchoolId);
                for (var i = 0; i < l1.Count - 1; i++)
                    for (var j = i + 1; j < l1.Count; j++)
                    {
                        var m = fixedRun.Meetings[(l1[i], l1[j])];
                        for (var t = 0; t < m; t++)
                        {
                            h[fixedRun.Games[pos].Home]++;
                            if (m % 2 == 1 && t == m - 1
                                && byPair.TryGetValue((l1[i], l1[j]), out var want)
                                && fixedRun.Games[pos].Home != want) honoured = false;
                            pos++;
                        }
                    }
                evenAfter = l1.All(s => h[s] == c1.Games / 2);
            }
            Check($"A9a ★ {sample.Count} of {residuals.Count} pre-fixed venues " +
                  $"({100 * sample.Count / Math.Max(1, residuals.Count)}% across {touched} schools) are " +
                  "ALL honoured and every school is still exactly even — a Eulerian walk cannot do this",
                  honoured && evenAfter && sample.Count * 4 >= residuals.Count && touched >= 3,
                  honoured ? (evenAfter ? "" : "a school came out uneven") : $"{fixedRun.Verdict}: {fixedRun.Reason}");

            // (b) The cheap overcommit: refused BEFORE any flow structure exists.
            var tri = new List<int> { 1, 2, 3 };
            var overCommit = BuildConferenceSlate(tri, 2, 0, new(), "A9b ",
                new List<FixedResidualHost> { new(1, 2, 1), new(1, 3, 1) });
            Check("A9b an over-committed fixed set is refused BEFORE the flow, naming the school",
                  overCommit.Verdict == SlateVerdict.InfeasibleUnderConstraints
                    && overCommit.RejectedBeforeFlow
                    && overCommit.Reason.Contains("school 1", StringComparison.Ordinal),
                  $"{overCommit.Verdict} / rejectedBeforeFlow {overCommit.RejectedBeforeFlow} / {overCommit.Reason}");

            // (c) ★ The quota-consistent Hall deficit. No individual quota goes negative and the
            //     totals reconcile, so it sails past the cheap guard — but the only free residual
            //     is 2-3 and school 1's last home game has nowhere to live. THE FLOW proves this.
            var hall = BuildConferenceSlate(tri, 2, 0, new(), "A9c ",
                new List<FixedResidualHost> { new(1, 2, 2), new(1, 3, 3) });
            Check("A9c ★ a quota-consistent but impossible fixed set is refused by the FLOW, not " +
                  "the doorman (InfeasibleUnderConstraints with RejectedBeforeFlow false)",
                  hall.Verdict == SlateVerdict.InfeasibleUnderConstraints && !hall.RejectedBeforeFlow,
                  $"{hall.Verdict} / rejectedBeforeFlow {hall.RejectedBeforeFlow} / {hall.Reason}");

            // ── A10 — determinism, with discriminating mutations ──────────────────
            var again = BuildSeasonSchedule(fixture, seed);
            Check("A10 the same world builds the identical schedule twice (sequence + fingerprint)",
                  again.SequenceEqual(schedule)
                    && ScheduleFingerprint(again) == ScheduleFingerprint(schedule));

            // ★ THE BEFORE-STATE IS CONSTRUCTED, NOT ASSUMED: the committed fixture already
            //   carries both designated rivalries, so the baseline is the fixture with both
            //   cleared MUTUALLY, and each mutation restores one.
            WorldFile WithRivals(params (int A, int B)[] keep)
            {
                var live = new Dictionary<int, int>();
                foreach (var (a, b) in keep) { live[a] = b; live[b] = a; }
                return new WorldFile
                {
                    SchemaVersion = fixture.SchemaVersion, Kind = fixture.Kind,
                    EraLabel = fixture.EraLabel, Division = fixture.Division,
                    WorldSeed = fixture.WorldSeed, Tiers = fixture.Tiers,
                    Conferences = fixture.Conferences, Places = fixture.Places,
                    Schools = fixture.Schools
                        .Select(s => s with { RivalId = live.TryGetValue(s.Id, out var r) ? r : null })
                        .ToList(),
                };
            }
            var cleared = PairCounts(BuildSeasonSchedule(WithRivals((24, 25), (26, 27), (28, 29)), seed));
            Check("A10a baseline (both designated rivalries cleared): 1-12 sits at q, and 14-15 is " +
                  "the pair the shape skips — so the mutations below have somewhere to move FROM",
                  cleared.GetValueOrDefault((1, 12)) == 1 && cleared.GetValueOrDefault((14, 15)) == 0,
                  $"1-12 meets {cleared.GetValueOrDefault((1, 12))}, 14-15 meets {cleared.GetValueOrDefault((14, 15))}");

            var mutA = PairCounts(BuildSeasonSchedule(WithRivals((1, 12), (24, 25), (26, 27), (28, 29)), seed));
            Check("A10b restoring the Odd Valley rivalry moves 1-12 from q to q+1 — the named pair's " +
                  "change is the assertion, the fingerprint moving is only the echo",
                  mutA.GetValueOrDefault((1, 12)) == 2, $"meets {mutA.GetValueOrDefault((1, 12))}");
            var mutB = PairCounts(BuildSeasonSchedule(WithRivals((14, 15), (24, 25), (26, 27), (28, 29)), seed));
            Check("A10c restoring the Skipline rivalry un-skips 14-15",
                  mutB.GetValueOrDefault((14, 15)) > 0, $"meets {mutB.GetValueOrDefault((14, 15))}");

            // ── A11 — golden parity against the re-locked Python oracle, EXACT ─────
            //     Everything compared is an integer or a string; there is no tolerance because
            //     "close" has no meaning for who hosts. S81.3's ULP lesson is about floating
            //     point and this surface has none.
            var schedFp = ScheduleFingerprint(schedule);
            Check("A11 the fixture schedule fingerprint matches the Python oracle EXACTLY",
                  schedFp == SlateOracleFixtureFp, schedFp == SlateOracleFixtureFp ? schedFp : $"got {schedFp}");

            // ── A12 — the size cap, pinned, with a reported benchmark ─────────────
            Check($"A12a the hard size cap is at least 20 (it is {SeasonConferenceSizeCap})",
                  SeasonConferenceSizeCap >= 20, $"cap {SeasonConferenceSizeCap}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hard = BuildConferenceSlate(Enumerable.Range(1, SeasonConferenceSizeCap).ToList(),
                                            22, 3, new(), "A12 ");
            sw.Stop();
            Check($"A12b a deliberately hard LEGAL configuration at the cap terminates: " +
                  $"n={SeasonConferenceSizeCap} G=22 k=3 -> {hard.Verdict}, " +
                  $"{hard.SearchNodes:N0} search nodes, {sw.Elapsed.TotalMilliseconds:F0} ms",
                  hard.Verdict == SlateVerdict.Feasible);
            Check("A12c above the cap the solver refuses WITHOUT searching (zero nodes explored)",
                  over.Verdict == SlateVerdict.UnsupportedConferenceSize && over.SearchNodes == 0,
                  $"{over.Verdict}, {over.SearchNodes} nodes");

            // ── The stock world, end to end ───────────────────────────────────────
            var stockSchedule = BuildSeasonSchedule(stock, seed);
            var stockConf = stock.Conferences.ToDictionary(c => c.Id);
            var stockPlayed = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            var stockHome = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            foreach (var g in stockSchedule)
            { stockPlayed[g.HomeId]++; stockPlayed[g.AwayId]++; stockHome[g.HomeId]++; }
            Check("A13 the stock world builds: every school plays its league's authored number and " +
                  "hosts exactly half, all 347, no exceptions",
                  stock.Schools.All(s => stockPlayed[s.Id] == stockConf[s.ConferenceId].Games
                                      && stockHome[s.Id] == stockConf[s.ConferenceId].Games / 2));
            Check("A13b nothing is authored as a rivalry in the stock world — the column ships " +
                  "empty for Emmett, and the rivalry machinery is proven on the fixture only",
                  stock.Schools.All(s => s.RivalId is null),
                  $"{stock.Schools.Count(s => s.RivalId is not null)} authored");
        }
        catch (Exception ex)
        {
            Check("Phase 84 completed without exceptions", false, ex.Message);
        }

        return pass;
    }
}
