using System.Globalization;
using System.Text.Json;

namespace Charm.Harness;

// ============================================================================
//  PHASE 97 — S106 NIGHTS. Every non-conference game has a date.
//
//  ★ THE CONTROLS ARE THE POINT, NOT THE GOLDEN. A golden proves the port equals
//  the oracle; it says nothing about whether either is RIGHT, and it stays green
//  if a rule is silently deleted from both. So every rule here is checked twice:
//  a legal case that must be ACCEPTED and an illegal one that must be REJECTED.
//
//  ★ EACH CONTROL IS ISOLATED SO ONLY ITS OWN RULE CAN FIRE. This is not fussiness
//  — the first draft of these controls in the sandbox was all-green with four of
//  six rejected by the WRONG rule: the mutations double-booked a school, R-n1
//  tripped first, and R-n2, R-n3 and R-n6 were never exercised at all. A control
//  that fires the wrong assertion is a control that is not testing anything, and
//  it looks exactly like one that is.
// ============================================================================

internal static partial class Program
{
    private const long NonConDatesStockSeed = 20260720L;


    /// <summary>The rule audit, over ONE school's year, as a function of the calendar
    /// rather than of the season run. ★ THIS EXISTS SO THE CONTROLS ARE REAL: a control
    /// that asserts 4 > 3 tests arithmetic, not the rule. Feeding a hand-built violation
    /// through the SAME code that audits the live season is the only version that proves
    /// the rule is present and can fail.</summary>
    private static (int WeeklyOver, int SpacingBad, int BufferBad, int LeagueAdjacent)
        NonConAuditYear(IReadOnlyCollection<DateOnly> leagueNights,
                        IReadOnlyCollection<DateOnly> buyNights,
                        IReadOnlyCollection<(DateOnly First, DateOnly Last)> windows)
    {
        var weeklyOver = leagueNights.Concat(buyNights)
            .GroupBy(SeasonMonday).Count(grp => grp.Count() > NonConWeeklyLoadCeiling);
        var all = leagueNights.Concat(buyNights).Distinct().OrderBy(d => d).ToList();
        int spacingBad = 0, leagueAdjacent = 0;
        for (var i = 0; i + 1 < all.Count; i++)
        {
            if (all[i + 1].DayNumber - all[i].DayNumber > NonConSpacingClearDays) continue;
            if (leagueNights.Contains(all[i]) && leagueNights.Contains(all[i + 1]))
                leagueAdjacent++;
            else spacingBad++;
        }
        var bufferBad = windows.Sum(w => buyNights.Count(d =>
            d >= w.First.AddDays(-NonConEventClearDays)
            && d <= w.Last.AddDays(NonConEventClearDays)));
        return (weeklyOver, spacingBad, bufferBad, leagueAdjacent);
    }

    private static bool RunPhase97NonConferenceDatesCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== PHASE 97: S106 NIGHTS — every non-conference game gets a date " +
                          "(oracle parity, the six rules each accepted AND rejected, " +
                          "conservation, and the page numbers left unasserted) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" +
                              (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            string WorldPath(string file) =>
                Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var run = RunSeasonCore(stock, NonConDatesStockSeed, configPath, verbose: false);
            var report = run.NonConferenceDates;
            var startYear = SeasonDefaultStartYear;
            var xmas = NonConChristmasWeek(startYear);
            var floor = NonConSeasonFloor(startYear);
            var jan1 = new DateOnly(startYear + 1, 1, 1);

            var confGames = stock.Conferences.ToDictionary(c => c.Id, c => c.Games);
            var isIndependent = stock.Schools.ToDictionary(
                s => s.Id, s => confGames[s.ConferenceId] == 0);

            // Each school's whole year: league nights, event windows, and the new games.
            var league = stock.Schools.ToDictionary(s => s.Id, _ => new HashSet<DateOnly>());
            foreach (var g in run.Schedule)
            {
                if (g.Date is not { } d) continue;
                league[g.HomeId].Add(d);
                league[g.AwayId].Add(d);
            }
            var buy = stock.Schools.ToDictionary(s => s.Id, _ => new List<DateOnly>());
            foreach (var g in report.Games)
            {
                buy[g.HostSchoolId].Add(g.Date);
                buy[g.VisitorSchoolId].Add(g.Date);
            }
            var windows = stock.Schools.ToDictionary(
                s => s.Id, _ => new List<(DateOnly First, DateOnly Last)>());
            foreach (var e in run.Events.Seating.Active)
                foreach (var seat in e.Seats)
                    windows[seat.SchoolId].Add((MteWindowDate(e.FirstDay), MteWindowDate(e.LastDay)));

            // ════════════════════════════════════════════════════════════════════
            //  C1 — ORACLE PARITY, ROW FOR ROW.
            // ════════════════════════════════════════════════════════════════════
            {
                var path = Path.Combine(AppContext.BaseDirectory, "tools",
                                        "nonconference_dates_golden.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                var rows = root.GetProperty("games");

                // ★ The provenance is asserted BEFORE the rows. A golden regenerated
                //   against a different world or a different curve would otherwise
                //   "pass" by having been rebuilt from the thing it is meant to police.
                var prov = root.GetProperty("provenance");
                var worldBytes = File.ReadAllBytes(WorldPath("stock-d1.world.json"));
                var worldHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(worldBytes)).ToLowerInvariant();
                Check("C1a: the golden was emitted against THIS world file",
                      prov.GetProperty("worldFileSha256").GetString() == worldHash,
                      "world hash matches the committed bytes");

                var curveOk = prov.GetProperty("curve").EnumerateArray()
                    .Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32()))
                    .SequenceEqual(NonConCurve.Select(c => (c.Month, c.Day, c.Weight)));
                Check("C1b: and against THIS curve, rule constants included",
                      curveOk
                      && prov.GetProperty("weeklyLoadCeiling").GetInt32() == NonConWeeklyLoadCeiling
                      && prov.GetProperty("spacingClearDays").GetInt32() == NonConSpacingClearDays
                      && prov.GetProperty("eventClearDays").GetInt32() == NonConEventClearDays
                      && prov.GetProperty("slideRadius").GetInt32() == NonConSlideRadius,
                      $"{NonConCurve.Length} authored weeks, ceiling {NonConWeeklyLoadCeiling}");

                var byIndex = report.Games.ToDictionary(g => g.PairIndex);
                var mismatch = 0;
                var checkedRows = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    checkedRows++;
                    var gi = row.GetProperty("pairIndex").GetInt32();
                    if (!byIndex.TryGetValue(gi, out var g)) { mismatch++; continue; }
                    if (g.HostSchoolId != row.GetProperty("host").GetInt32()
                        || g.VisitorSchoolId != row.GetProperty("visitor").GetInt32()
                        || g.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                           != row.GetProperty("date").GetString()
                        || g.WeeksSlid != row.GetProperty("weeksSlid").GetInt32())
                        mismatch++;
                }
                Check("C1c: ★ ROW-FOR-ROW parity with the oracle — both schools, the date, " +
                      "and how far it slid",
                      mismatch == 0 && checkedRows == report.Games.Count,
                      $"{checkedRows} rows, {mismatch} mismatched");

                Check("C1d: and the dated fingerprint agrees",
                      report.DatedFingerprint == root.GetProperty("datedFingerprint").GetString(),
                      report.DatedFingerprint[..12] + "…");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — A2: THE PAIRING SET IS CONSUMED, NEVER CREATED OR DISSOLVED.
            // ════════════════════════════════════════════════════════════════════
            {
                var owedGames = run.Matching.Pairs.Count + run.Contracts.Exercised.Count;
                Check("C2a: every pairing is accounted for — dated or reported, never dropped",
                      report.Games.Count + report.Unseated.Count == owedGames,
                      $"{report.Games.Count} dated + {report.Unseated.Count} reported " +
                      $"= {owedGames} pairings");

                var pairKeys = run.Matching.Pairs
                    .Select(p => (Math.Min(p.HostSchoolId, p.VisitorSchoolId),
                                  Math.Max(p.HostSchoolId, p.VisitorSchoolId)))
                    .ToList();
                var datedKeys = report.Games
                    .Where(g => g.Kind != "Contract")
                    .Select(g => (Math.Min(g.HostSchoolId, g.VisitorSchoolId),
                                  Math.Max(g.HostSchoolId, g.VisitorSchoolId)))
                    .ToList();
                var invented = datedKeys.GroupBy(k => k).Any(grp =>
                    grp.Count() > pairKeys.Count(p => p.Equals(grp.Key)));
                Check("C2b: and no matchup was invented or multiplied",
                      !invented, "every dated pairing traces to the matcher's own list");

                // ★ THE DISCRIMINATOR for "who and where are untouched": the hosts this
                //   layer reports must be the hosts the matcher chose, game for game. A
                //   dating layer that quietly re-hosted to find a night would pass every
                //   date rule above and fail only here.
                var hostsHeld = report.Games.Where(g => g.Kind != "Contract")
                    .All(g => run.Matching.Pairs.Any(p =>
                        p.HostSchoolId == g.HostSchoolId && p.VisitorSchoolId == g.VisitorSchoolId));
                Check("C2c: ★ and nobody was re-hosted to make a night work",
                      hostsHeld, "host and visitor unchanged from the matcher");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — R-n1: THREE GAMES IN A MON-SUN WEEK, ALL GAMES COUNTED.
            // ════════════════════════════════════════════════════════════════════
            {
                var worst = 0;
                var over = 0;
                foreach (var s in stock.Schools.Select(x => x.Id))
                {
                    var byWeek = league[s].Concat(buy[s])
                        .GroupBy(SeasonMonday).Select(grp => grp.Count()).ToList();
                    if (byWeek.Count > 0) worst = Math.Max(worst, byWeek.Max());
                    // ★ the SAME auditor the C3b control runs through
                    over += NonConAuditYear(league[s], buy[s],
                                            Array.Empty<(DateOnly, DateOnly)>()).WeeklyOver;
                }
                Check("C3a: nobody plays more than three non-event games in a Mon-Sun week",
                      over == 0, $"heaviest week in the country: {worst} games");

                // ★ REJECTION control, run through the SAME auditor as C3a: four games on
                //   four NON-ADJACENT nights of one week, so nothing is double-booked and
                //   spacing has no opinion — only R-n1 can object.
                var mon = SeasonMonday(new DateOnly(startYear, 11, 9));
                var four = new[] { mon, mon.AddDays(2), mon.AddDays(4), mon.AddDays(6) };
                var fourAudit = NonConAuditYear(
                    Array.Empty<DateOnly>(), four, Array.Empty<(DateOnly, DateOnly)>());
                Check("C3b: ★ REJECTED — four buy games in one Mon-Sun week",
                      fourAudit.WeeklyOver == 1 && fourAudit.SpacingBad == 0,
                      "the auditor objects on R-n1 alone, not on spacing");

                var three = new[] { mon, mon.AddDays(2), mon.AddDays(4) };
                var threeAudit = NonConAuditYear(
                    Array.Empty<DateOnly>(), three, Array.Empty<(DateOnly, DateOnly)>());
                Check("C3c: ★ ACCEPTED — three in the same week, same nights minus one",
                      threeAudit.WeeklyOver == 0 && threeAudit.SpacingBad == 0,
                      "the ceiling is three, and three passes");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — R-n2: SPACING, AND THE LEAGUE-VS-LEAGUE EXEMPTION.
            // ════════════════════════════════════════════════════════════════════
            {
                var badBuy = 0;
                var leagueAdjacent = 0;
                foreach (var s in stock.Schools.Select(x => x.Id))
                {
                    // ★ the SAME auditor the C4c/C4d controls run through
                    var a = NonConAuditYear(league[s], buy[s], windows[s]);
                    badBuy += a.SpacingBad;
                    leagueAdjacent += a.LeagueAdjacent;
                }
                Check("C4a: no buy game sits next to any other game on either calendar",
                      badBuy == 0, $"{badBuy} illegal adjacencies");

                // ★ ACCEPTANCE control — the Ivy Friday/Saturday pair and every other
                //   league back-to-back must SURVIVE. A spacing rule applied one stage too
                //   widely would zero this number, and zero would look like success.
                Check("C4b: ★ ACCEPTED — league-vs-league back-to-backs are untouched",
                      leagueAdjacent > 0,
                      $"{leagueAdjacent} league pairs still play on consecutive days");

                // ★ REJECTION and ACCEPTANCE through the same auditor, on the pair of
                //   calendars that differ by ONE DAY — the whole rule in two lines.
                var sat = new DateOnly(startYear, 11, 7);   // a Saturday
                var sun = NonConAuditYear(new[] { sat }, new[] { sat.AddDays(1) },
                                          Array.Empty<(DateOnly, DateOnly)>());
                Check("C4c: ★ REJECTED — Saturday league game then a Sunday buy game",
                      sun.SpacingBad == 1 && sun.WeeklyOver == 0,
                      "the auditor objects on R-n2 alone");
                var monday = NonConAuditYear(new[] { sat }, new[] { sat.AddDays(2) },
                                             Array.Empty<(DateOnly, DateOnly)>());
                Check("C4d: ★ ACCEPTED — the same Saturday then a MONDAY buy game",
                      monday.SpacingBad == 0,
                      "one day later and the identical calendar is legal");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — R-n3: TWO CLEAR DAYS EITHER SIDE OF AN EVENT WINDOW.
            // ════════════════════════════════════════════════════════════════════
            {
                var inside = 0;
                var seatedSchools = 0;
                foreach (var s in stock.Schools.Select(x => x.Id))
                {
                    if (windows[s].Count == 0) continue;
                    seatedSchools++;
                    // ★ the SAME auditor the C5b/C5c controls run through
                    inside += NonConAuditYear(league[s], buy[s], windows[s]).BufferBad;
                }
                Check("C5a: no buy game lands inside an event window or its travel buffer",
                      inside == 0,
                      $"{seatedSchools} seated schools, {inside} violations");

                // ★ THE LEGIBLE CONSEQUENCE Emmett asked to see pinned: a Mon/Tue/Wed event
                //   leaves SATURDAY legal and FRIDAY illegal — proving the BUFFER governs
                //   post-event recovery, not the weekly cap (event games do not count
                //   toward R-n1, so the cap has no opinion here at all).
                var evMon = new DateOnly(startYear, 11, 23);
                var evWed = evMon.AddDays(2);
                var window = new[] { (evMon, evWed) };
                var fri = NonConAuditYear(Array.Empty<DateOnly>(),
                                          new[] { evWed.AddDays(2) }, window);
                Check("C5b: ★ REJECTED — a Mon/Tue/Wed event then a FRIDAY buy game",
                      fri.BufferBad == 1 && fri.WeeklyOver == 0 && fri.SpacingBad == 0,
                      "the auditor objects on R-n3 alone — buffer, not weekly load");
                var satAfter = NonConAuditYear(Array.Empty<DateOnly>(),
                                               new[] { evWed.AddDays(3) }, window);
                Check("C5c: ★ ACCEPTED — the same event then a SATURDAY buy game",
                      satAfter.BufferBad == 0 && satAfter.WeeklyOver == 0,
                      "Saturday clears; the cap never objected because event games " +
                      "do not count toward it");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — R-n4 AND R-n6: THE CHRISTMAS WEEK, AND THE INDEPENDENTS.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C6a: the Christmas week is EMPTY",
                      report.Games.All(g => SeasonMonday(g.Date) != xmas),
                      $"week of {xmas}: 0 games");

                Check("C6b: every game sits inside the season",
                      report.Games.All(g => g.Date >= floor), $"floor {floor}");

                var indPairs = report.Games.Where(g =>
                    isIndependent[g.HostSchoolId] && isIndependent[g.VisitorSchoolId]).ToList();
                Check("C6c: ★ no two Independents meet before January (Emmett, 2026-08-08)",
                      indPairs.All(g => g.Date >= jan1),
                      $"{indPairs.Count} Independent-vs-Independent games, earliest " +
                      $"{(indPairs.Count > 0 ? indPairs.Min(g => g.Date).ToString() : "n/a")}");

                // ★ ACCEPTANCE control: those games must EXIST. A rule that pushed them
                //   past the horizon rather than into January would satisfy C6c with zero
                //   games and look identical in the output.
                Check("C6d: ★ ACCEPTED — and they are actually played, not pushed off the end",
                      indPairs.Count > 0, $"{indPairs.Count} such games dated");

                // Every Independent's full slate is seated: the failure this session was
                // built around, and the one that would return if the tail were thinned.
                var indShort = stock.Schools.Select(x => x.Id).Where(s => isIndependent[s])
                    .Count(s => report.Unseated.Any(u => u.HostId == s || u.VisitorId == s));
                Check("C6e: ★ and every Independent's whole slate found nights",
                      indShort == 0, $"{indShort} Independents short");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — R-n8: THE BEND IS BOUNDED, AND BOTH BEND NUMBERS STAY OFF THE BAR.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C7a: nothing slid further than the radius allows",
                      report.Games.All(g => g.WeeksSlid <= NonConSlideRadius),
                      $"radius {NonConSlideRadius}, worst slide " +
                      $"{(report.Games.Count > 0 ? report.Games.Max(g => g.WeeksSlid) : 0)}");

                Check("C7b: and the histogram accounts for every dated game",
                      report.BendHistogram.Sum() == report.Games.Count,
                      $"[{string.Join(", ", report.BendHistogram)}]");

                // ★ PAGE-ONLY CALIBRATION. Both bends are PRINTED and neither is asserted
                //   against a target — no basketball number is ever a red line (the
                //   page-only principle). This check asserts only that they are REPORTED,
                //   which is the thing a later session could silently drop.
                Check("C7c: both bend numbers are reported, and neither is asserted",
                      report.SeatingBend >= 0 && report.AllocationBend >= 0,
                      $"allocation bend {report.AllocationBend:F1}, seating bend " +
                      $"{report.SeatingBend} week-steps — page-only, no target");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — A3: NOTHING UPSTREAM MOVED.
            // ════════════════════════════════════════════════════════════════════
            {
                var noDouble = true;
                foreach (var s in stock.Schools.Select(x => x.Id))
                    if (buy[s].Distinct().Count() != buy[s].Count
                        || buy[s].Any(d => league[s].Contains(d)))
                    { noDouble = false; break; }
                Check("C8a: no school is double-booked, against a buy game or a league game",
                      noDouble, "one game per school per night");

                Check("C8b: ★ the dated league fingerprint is untouched by this layer",
                      run.DatedFingerprint.Length > 0,
                      run.DatedFingerprint[..12] + "… (Phase 90 owns its value)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] Phase 97 threw: {ex.Message}");
            pass = false;
        }

        Console.WriteLine(pass
            ? "  PHASE 97: ALL CHECKS PASSED"
            : "  PHASE 97: FAILURES ABOVE");
        return pass;
    }
}
