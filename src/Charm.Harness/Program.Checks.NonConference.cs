namespace Charm.Harness;

// ============================================================================
//  Phase 92 (Session 101) — CLASSES AND REQUESTS.
//
//  What this phase proves:
//    C1   every school gets exactly one class; the five classes partition
//         every committed world;
//    C2   the tier→floor mapping as a table, the class ORDER asserted
//         directly, and class-never-below-floor school by school;
//    C3   ★ three-point synthetic monotonicity — within-class raise, a
//         band-boundary cross, and the floor's negative control;
//    C4a  same world, same seating → identical report;
//    C4b  ★ seed-independence — three seeds, identical report on an eventless
//         world; identical CLASSES on the stock world (targets legitimately
//         follow the seating, which follows the seed by design);
//    C5   conservation: HOME + NEUTRAL + ROAD == OPEN, exactly, everywhere;
//    C6   individual validity: the clamp chain's invariants, every school,
//         every world, and zero impossible/compressed on committed worlds;
//    C7   the 31/3 exemption applies to EXACTLY the seated set, both
//         directions, as set membership;
//    C8   ★ a lopsided world REPORTS, it does not throw — the national gap is
//         measured and printed, never asserted to a value;
//    C9   ★ zero-path identity on the FULL stock bundle: conference
//         fingerprint, dated fingerprint, and the results+possessions
//         fingerprint, against goldens captured from the pre-S101 tree;
//    C10  the report's totals equal the per-school sums, recomputed
//         independently;
//    C11  ★ the rank formula, exactly — the two published sequences, the
//         class of one, and the id tie-break.
//
//  ── Two concepts, kept separate on purpose ─────────────────────────────────
//  INDIVIDUAL VALIDITY (C5/C6) holds universally: every targeted school on
//  every world has a self-consistent request. NATIONAL BALANCE (C8) is only
//  reported: a world can be arbitrarily lopsided while every school's own
//  request is valid — that is the stock world's actual state, and the gap is
//  the finding, not a defect.
//
//  ── What this phase deliberately does not prove ────────────────────────────
//  Any basketball value. No class count, no average, no gap number from any
//  world is asserted — page-only calibration holds. The counts C1 checks are
//  partition arithmetic (they sum to the school count), not target values.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ THE PRE-S101 GOLDENS — stock world, seed 20260720, captured from the
    /// committed tree before this session's first edit (conference and dated hashes
    /// verified against the committed season.txt at capture time). If any of the three
    /// moves, S101's second wall is breached and the cause is in RunSeasonCore.</summary>
    private const string NonConGoldenConferenceFp =
        "6f79d6636e291866d51387f93979d817011f7903ddc64e67d4ebcebf087cb5c3";
    private const string NonConGoldenDatedFp =
        "7515df7d72f801f49d264ff52d6472911ac87d0996d44269d113b0ef83cb632a";
    /// <summary>★ S104 — RECAPTURED. The season now plays 24 showcase games on top of its
    /// tournaments, and seven tournaments seat different fields because a showcase took a
    /// school on an overlapping night (and because a tournament that loses a candidate
    /// changes what is left for every later one). So the results half of the season is
    /// deliberately a different season. The pre-S104 value was 6abd62b0…, and it is NOT
    /// recoverable by subtraction the way the event-games hash is — this fingerprint covers
    /// the conference games too, and those are byte-identical; what moved is the event half
    /// inside the same hash. Emmett's machine is the commit-of-record for this value.</summary>
    private const string NonConGoldenResultsFp =
        "898d9fe8e75a353bca1fa89296d96f8cceafb72e66c2a6718eb6eb0b2553742b";

    private const long NonConStockSeed = 20260720;

    private static bool Phase92NonConferenceCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 92 — Classes and requests (S101: every school gets a class read " +
                          "from prestige with its conference tier as a floor, and a target November " +
                          "in games — home set, neutral allowed, road the remainder; nothing is " +
                          "scheduled — partition, floor and order, three-point monotonicity, " +
                          "seed-independence, conservation, individual validity, the exemption as " +
                          "set membership, lopsided worlds reporting, full-bundle zero-path " +
                          "identity, total reconciliation, and the rank formula exactly) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            string WorldPath(string file) =>
                Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var fixtures = new[]
            {
                ("fixture-format", LoadWorld(WorldPath("fixture-format.world.json"))),
                ("fixture-memory", LoadWorld(WorldPath("fixture-memory.world.json"))),
                ("fixture-mte", LoadWorld(WorldPath("fixture-mte.world.json"))),
                ("fixture-rotation", LoadWorld(WorldPath("fixture-rotation.world.json"))),
                ("fixture-schedule", LoadWorld(WorldPath("fixture-schedule.world.json"))),
                ("fixture-tiny", LoadWorld(WorldPath("fixture-tiny.world.json"))),
            };

            // ★ One stock season run serves C7, C9 and the stock arm of everything else.
            //   Legacy mode (no career), exactly the committed page's own command. This is
            //   the phase's whole runtime cost (~half a minute) — flagged to Emmett at the
            //   gate and accepted.
            var stockRun = RunSeasonCore(stock, NonConStockSeed, configPath, verbose: false);
            var stockReport = stockRun.NonConference;

            var eventless = fixtures.Select(f => (f.Item1, f.Item2,
                Report: BuildNonConferenceRequests(f.Item2, EventSeatingOutcome.Empty))).ToList();
            var allWorlds = eventless
                .Select(e => (Name: e.Item1, World: e.Item2, e.Report))
                .Append((Name: "stock", World: stock, Report: stockReport)).ToList();

            // ════════════════════════════════════════════════════════════════════
            //  C1 — PARTITION. Exactly one class per school, five classes total.
            // ════════════════════════════════════════════════════════════════════
            {
                var ok = true; var detail = "";
                var legal = new[] { "Selling", "Working", "Solid", "Marquee" };
                foreach (var (name, world, report) in allWorlds)
                {
                    var ids = report.Schools.Select(s => s.SchoolId).ToList();
                    var covers = ids.Count == world.Schools.Count
                                 && ids.Distinct().Count() == ids.Count
                                 && world.Schools.All(s => ids.Contains(s.Id));
                    var classesLegal = report.Schools.All(s => legal.Contains(s.ClassName));
                    if (!(covers && classesLegal)) { ok = false; detail = name; break; }
                }
                Check("C1: every school in every world holds exactly one class, and the five " +
                      "classes partition the school list", ok, detail.Length > 0 ? detail :
                      $"{allWorlds.Sum(w => w.Report.Schools.Count)} schools across " +
                      $"{allWorlds.Count} worlds");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — THE FLOOR TABLE, THE ORDER, AND NEVER-BELOW, SCHOOL BY SCHOOL.
            // ════════════════════════════════════════════════════════════════════
            {
                // The mapping asserted as a table — all four ids by name, never inherited
                // from declaration order.
                var table = NonConTierFloor("power") == 3 && NonConClassNames[3] == "Marquee"
                         && NonConTierFloor("highMid") == 2 && NonConClassNames[2] == "Solid"
                         && NonConTierFloor("lowMid") == 1 && NonConClassNames[1] == "Working"
                         && NonConTierFloor("low") == 0 && NonConClassNames[0] == "Selling";
                // The order asserted directly: each prestige band's class name sits where
                // the ordinal says (Selling < Working < Solid < Marquee).
                var order = NonConPrestigeClass(0) == 0 && NonConPrestigeClass(24) == 0
                         && NonConPrestigeClass(25) == 1 && NonConPrestigeClass(54) == 1
                         && NonConPrestigeClass(55) == 2 && NonConPrestigeClass(79) == 2
                         && NonConPrestigeClass(80) == 3 && NonConPrestigeClass(99) == 3;
                var floorHeld = true; var culprit = "";
                foreach (var (name, world, report) in allWorlds)
                {
                    var confById = world.Conferences.ToDictionary(c => c.Id);
                    foreach (var s in world.Schools)
                    {
                        var conf = confById[s.ConferenceId];
                        var row = report.Schools.Single(r => r.SchoolId == s.Id);
                        if (conf.Games == 0)
                        {
                            if (!row.IsIndependent) { floorHeld = false; culprit = $"{name}/{s.Name}"; }
                            continue;
                        }
                        var expected = Math.Max(NonConTierFloor(conf.TierId),
                                                NonConPrestigeClass(s.CurrentPrestige));
                        if (row.ClassName != NonConClassNames[expected])
                        { floorHeld = false; culprit = $"{name}/{s.Name}"; }
                    }
                }
                Check("C2: the tier→floor mapping holds as a table, the class order is " +
                      "asserted directly, and no school sits below its league's floor",
                      table && order && floorHeld, culprit);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — ★ THREE-POINT SYNTHETIC MONOTONICITY on a mutated fixture-tiny.
            // ════════════════════════════════════════════════════════════════════
            {
                var tiny = fixtures.Single(f => f.Item1 == "fixture-tiny").Item2;
                NonConferenceReport Mutated(int schoolId, int prestige)
                {
                    var w = new WorldFile
                    {
                        SchemaVersion = tiny.SchemaVersion, Kind = tiny.Kind,
                        EraLabel = tiny.EraLabel, Division = tiny.Division,
                        WorldSeed = tiny.WorldSeed, Tiers = tiny.Tiers,
                        Conferences = tiny.Conferences, Places = tiny.Places,
                        Events = tiny.Events,
                        Schools = tiny.Schools
                            .Select(s => s.Id == schoolId ? s with { CurrentPrestige = prestige } : s)
                            .ToList(),
                    };
                    return BuildNonConferenceRequests(w, EventSeatingOutcome.Empty);
                }
                NonConSchoolRequest Row(NonConferenceReport r, int id) =>
                    r.Schools.Single(s => s.SchoolId == id);
                var baseline = BuildNonConferenceRequests(tiny, EventSeatingOutcome.Empty);

                // (i) Within-class raise: Prairie Wind (id 11, lowMid, prestige 45 →
                //     Working). Raise to 54 — still Working; HOME must not decrease.
                var b1 = Row(baseline, 11); var m1 = Row(Mutated(11, 54), 11);
                var p1 = b1.ClassName == "Working" && m1.ClassName == "Working"
                         && m1.Home >= b1.Home;

                // (ii) Boundary cross: raise the same school to 60 — Solid now.
                var m2 = Row(Mutated(11, 60), 11);
                var p2 = m2.ClassName == "Solid";

                // (iii) The floor's negative control: Old Colony (id 5, POWER league,
                //       prestige 47, the lowest-prestige Marquee school — rank 0). Drop
                //       to 1: still Marquee, still rank 0, HOME unchanged.
                var b3 = Row(baseline, 5); var m3 = Row(Mutated(5, 1), 5);
                var p3 = b3.ClassName == "Marquee" && m3.ClassName == "Marquee"
                         && m3.Home == b3.Home;

                Check("C3: raising prestige within a class never lowers the home request, " +
                      "crossing a band boundary raises the class, and dropping below the " +
                      "league floor moves nothing",
                      p1 && p2 && p3,
                      $"within {b1.Home}->{m1.Home}, cross {b1.ClassName}->{m2.ClassName}, " +
                      $"floor {b3.Home}->{m3.Home}");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4a/C4b — DETERMINISM AND SEED-INDEPENDENCE.
            // ════════════════════════════════════════════════════════════════════
            {
                string Flat(NonConferenceReport r) => string.Join(";",
                    r.Schools.Select(s => $"{s.SchoolId}:{s.ClassName}:{s.Open}:{s.Home}:{s.Neutral}:{s.Road}"));
                var tiny = fixtures.Single(f => f.Item1 == "fixture-tiny").Item2;
                var a = Flat(BuildNonConferenceRequests(tiny, EventSeatingOutcome.Empty));
                var b = Flat(BuildNonConferenceRequests(tiny, EventSeatingOutcome.Empty));
                Check("C4a: same world, same seating — identical report", a == b);

                // ★ C4b, worded precisely. The seam takes no seed, so seed-independence is
                //   a claim about what feeds it. On an EVENTLESS world every seed produces
                //   the same (empty) seating, so the whole report must be identical. On the
                //   stock world the seating legitimately follows the seed BY DESIGN (R2's
                //   exemption follows the seating), so the seed-independent claim is the
                //   CLASSES — asserted school by school across three seeds.
                var seeds = new long[] { NonConStockSeed, 7, 990001 };
                var eventlessSame = seeds
                    .Select(s => Flat(BuildNonConferenceRequests(tiny,
                        MteSeatSeason(tiny, s, MteReadHistory(null, 0)))))
                    .Distinct().Count() == 1;
                var stockClasses = seeds
                    .Select(s => string.Join(";",
                        BuildNonConferenceRequests(stock,
                            MteSeatSeason(stock, s, MteReadHistory(null, 0)))
                        .Schools.Select(r => $"{r.SchoolId}:{r.ClassName}")))
                    .Distinct().Count() == 1;
                Check("C4b: three different season seeds — identical report on an eventless " +
                      "world, identical classes on the stock world (targets follow the " +
                      "seating by design)", eventlessSame && stockClasses);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5/C6 — CONSERVATION AND INDIVIDUAL VALIDITY, EVERY SCHOOL, EVERY WORLD.
            // ════════════════════════════════════════════════════════════════════
            {
                var conserve = true; var valid = true;
                var committedClean = true; var culprit = "";
                foreach (var (name, world, report) in allWorlds)
                {
                    var confById = world.Conferences.ToDictionary(c => c.Id);
                    foreach (var s in report.Conventional)
                    {
                        if (s.Impossible)
                        {
                            committedClean = false; culprit = $"{name}/{s.SchoolName} impossible";
                            continue;
                        }
                        // ★ S104 — the identity gains its third term. A showcase game is one
                        //   of the school's OWN games, charged away rather than added (R26), so
                        //   the tokens still to arrange are OPEN minus what is already spoken
                        //   for. Written as a sum rather than folded into OPEN on purpose:
                        //   OPEN is what the season holds, and the charge is what has already
                        //   been promised out of it.
                        if (s.Home + s.Neutral + s.Road + s.ShowcaseGames != s.Open)
                        { conserve = false; culprit = $"{name}/{s.SchoolName}"; }
                        var confGames = confById.Values
                            .Single(c => c.Id == world.Schools.Single(x => x.Id == s.SchoolId).ConferenceId).Games;
                        var expectedOpen =
                            (s.Seated ? NonConSeasonGamesSeated : NonConSeasonGamesUnseated)
                            - confGames - (s.Seated ? NonConEventGames : 0);
                        if (!(s.Open == expectedOpen
                              && s.Home >= 0 && s.Home <= s.Open
                              && s.Neutral >= 0 && s.Neutral <= s.Open - s.Home
                              && s.Road >= 0
                              && s.ShowcaseGames >= 0 && s.ShowcaseGames <= 1))
                        { valid = false; culprit = $"{name}/{s.SchoolName}"; }
                        if (s.Compressed)
                        { committedClean = false; culprit = $"{name}/{s.SchoolName} compressed"; }
                    }
                }
                Check("C5: HOME + NEUTRAL + ROAD + SHOWCASE GAMES == OPEN, exactly, for every " +
                      "targeted school on every world", conserve, conserve ? "" : culprit);
                Check("C6: the clamp chain's invariants hold everywhere, OPEN is the exact " +
                      "remainder, and no committed world holds a compressed or impossible " +
                      "school", valid && committedClean, (valid && committedClean) ? "" : culprit);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — THE EXEMPTION IS EXACTLY THE SEATED SET, BOTH DIRECTIONS.
            // ════════════════════════════════════════════════════════════════════
            {
                // ★ S104 — TOURNAMENT seats only. The exemption this check is about is the one
                //   that buys a 31-game season, and a showcase seat does not buy it. Reading
                //   every seat here would compare the report's tournament set against a set
                //   that also holds 48 showcase schools and go red for the right reason with
                //   the wrong explanation.
                var seatedFromSeating = stockRun.Events.Seating.Active
                    .Where(e => !e.IsShowcase)
                    .SelectMany(e => e.Seats).Select(s => s.SchoolId).ToHashSet();
                var seatedFromReport = stockReport.Conventional
                    .Where(s => s.Seated).Select(s => s.SchoolId).ToHashSet();
                var exempted31 = stockReport.Conventional.Where(s => s.Seated)
                    .All(s => s.Open == NonConSeasonGamesSeated
                              - s.ConferenceGames - NonConEventGames);
                var plain29 = stockReport.Conventional.Where(s => !s.Seated)
                    .All(s => s.Open == NonConSeasonGamesUnseated - s.ConferenceGames);
                // ★ THE DISCRIMINATOR: the stock world really does seat showcases, so the
                //   filter above is doing work rather than being a no-op on a slate that has
                //   no showcases to exclude.
                var showcaseSeatsExist = stockRun.Events.Seating.Active.Any(e => e.IsShowcase && e.Seats.Count > 0);
                Check("C7: the 31-with-3-exempt pair applies to exactly the schools the " +
                      "seating seated, both directions, as set membership",
                      seatedFromSeating.SetEquals(seatedFromReport) && exempted31 && plain29
                      && showcaseSeatsExist,
                      $"{seatedFromReport.Count} seated (read from the seating outcome, " +
                      "never a constant)");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — ★ A LOPSIDED WORLD REPORTS, IT DOES NOT THROW.
            // ════════════════════════════════════════════════════════════════════
            {
                var tinyReport = eventless.Single(e => e.Item1 == "fixture-tiny").Report;
                var schedReport = eventless.Single(e => e.Item1 == "fixture-schedule").Report;
                var formatReport = eventless.Single(e => e.Item1 == "fixture-format").Report;
                // Reaching this line at all IS the no-throw half — every fixture report
                // above was built without an exception. The rest: the two worlds that can
                // hold a market are lopsided and say so; their gap is MEASURED and printed
                // here, never asserted to a value (page-only calibration).
                var ok = tinyReport.HostGap != 0 && tinyReport.Conventional.Any()
                      && schedReport.HostGap != 0 && schedReport.Conventional.Any()
                      && !formatReport.Conventional.Any()
                      && formatReport.Schools.Count == 1
                      && formatReport.Schools[0].IsIndependent;
                Check("C8: worlds too small to balance produce a complete report and no " +
                      "exception; the one-school world is a lone Independent with no target",
                      ok,
                      $"tiny gap {tinyReport.HostGap:+#;-#;0}, schedule gap " +
                      $"{schedReport.HostGap:+#;-#;0} — measured, not asserted");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C9 — ★ ZERO-PATH IDENTITY ON THE FULL STOCK BUNDLE.
            // ════════════════════════════════════════════════════════════════════
            {
                var resultsFp = SeasonFingerprint(stockRun.Results, stockRun.PossessionCounts);
                Check("C9: the stock season reproduces the pre-S101 tree exactly — " +
                      "conference fingerprint, dated fingerprint, and the results+" +
                      "possessions fingerprint",
                      stockRun.Fingerprint == NonConGoldenConferenceFp
                      && stockRun.DatedFingerprint == NonConGoldenDatedFp
                      && resultsFp == NonConGoldenResultsFp,
                      $"conf {stockRun.Fingerprint[..8]}…, dated {stockRun.DatedFingerprint[..8]}…, " +
                      $"results {resultsFp[..8]}…");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10 — THE TOTALS ARE THE SUMS, RECOMPUTED INDEPENDENTLY.
            // ════════════════════════════════════════════════════════════════════
            {
                var ok = true; var culprit = "";
                foreach (var (name, _, report) in allWorlds)
                {
                    if (report.HomeTotal != report.Schools.Sum(s => s.Home)
                        || report.NeutralTotal != report.Schools.Sum(s => s.Neutral)
                        || report.RoadTotal != report.Schools.Sum(s => s.Road)
                        || report.HostGap != report.RoadTotal - report.HomeTotal)
                    { ok = false; culprit = name; }
                }
                Check("C10: every report's three totals and its gap equal the per-school " +
                      "sums, recomputed independently", ok, culprit);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C11 — ★ THE RANK FORMULA, EXACTLY.
            // ════════════════════════════════════════════════════════════════════
            {
                var five710 = NonConHomeSpread(7, 10, 5).SequenceEqual(new[] { 7, 8, 9, 9, 10 });
                var five02 = NonConHomeSpread(0, 2, 5).SequenceEqual(new[] { 0, 1, 1, 2, 2 });
                var one = NonConHomeSpread(3, 5, 1).SequenceEqual(new[] { 3 });

                // The id tie-break, through the report — and it must DISCRIMINATE: the
                // equal-prestige pair is placed at ranks whose spread values DIFFER, so a
                // reversed tie-break provably fails. Set Flint Hills (id 13) and Timberline
                // Institute (id 16) both to prestige 40: Working sorts to
                // [11, 19, 36, 40(id 13), 40(id 16), 45], band 3–5 spreads to
                // [3, 3, 4, 4, 5, 5] — ascending id puts 13 at rank 3 (home 4) and 16 at
                // rank 4 (home 5). Strict inequality asserted; equal homes would mean the
                // fixture stopped discriminating, and the check must say so.
                var tiny = fixtures.Single(f => f.Item1 == "fixture-tiny").Item2;
                var w = new WorldFile
                {
                    SchemaVersion = tiny.SchemaVersion, Kind = tiny.Kind,
                    EraLabel = tiny.EraLabel, Division = tiny.Division,
                    WorldSeed = tiny.WorldSeed, Tiers = tiny.Tiers,
                    Conferences = tiny.Conferences, Places = tiny.Places,
                    Events = tiny.Events,
                    Schools = tiny.Schools
                        .Select(s => s.Id is 13 or 16 ? s with { CurrentPrestige = 40 } : s)
                        .ToList(),
                };
                var r = BuildNonConferenceRequests(w, EventSeatingOutcome.Empty);
                var lo = r.Schools.Single(s => s.SchoolId == 13);
                var hi = r.Schools.Single(s => s.SchoolId == 16);
                var tie = lo.ClassName == "Working" && hi.ClassName == "Working"
                          && lo.Home < hi.Home;

                Check("C11: the rank spread lands exactly on [7,8,9,9,10] and [0,1,1,2,2], " +
                      "a class of one takes the band floor, and equal prestige breaks by " +
                      "ascending id", five710 && five02 && one && tie,
                      $"tie-break homes {lo.Home} (id 13) vs {hi.Home} (id 16)");
            }
        }
        catch (Exception ex)
        {
            Check("Phase 92 completed without an unexpected exception", false, ex.Message);
        }

        Console.WriteLine($"  Phase 92: {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
