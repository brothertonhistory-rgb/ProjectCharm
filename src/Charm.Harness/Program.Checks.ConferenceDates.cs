using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  Phase 85 — CONFERENCE DATES (Session 94)
//
//  ★ C2 IS THE ONE THAT MATTERS — S105.2 rewrote it to DISCRIMINATE: in any
//  Monday-to-Sunday week a team plays at most ONE weekday (Mon-Fri) game and
//  at most ONE weekend (Sat-Sun) game — Emmett's "strict weekday game/weekend
//  game/weekday game process". Two ceilings of one subsume the original
//  never-three-in-a-week absolute ("any instance of that is abject failure,
//  zero exceptions"), whose standalone test is deleted as sediment, not kept
//  beside the new one. C2 carries a battery of controls: constructed weekday
//  and weekend doubles REJECTED, the Mon/Wed/Sat triple still rejected, the
//  real UConn Sun/Wed/Sat rolling pattern ACCEPTED, the Ivy/Big Sky
//  Friday+Saturday pair ACCEPTED (A1: Friday is a weekday), a Sunday-then-
//  Monday consecutive pair ACCEPTED (different weeks), and the weekend
//  definition itself asserted over all seven days on both sides.
//
//  Golden parity: the oracle's dated fingerprints are asserted EXACTLY —
//  integers and dates, no tolerance, no platform hazard (nothing here touches
//  Math.Pow; see the S81.3 note in CONVENTIONS §2).
// ============================================================================

internal static partial class Program
{
    // tools/schedule_oracle.py exports (functions of the WORLD + START YEAR alone).
    private const string DatesOracleStockFp =
        "46d89bf88e4a33c8bc388886c179cc0bf4e4bba0fe5674748d07e2243caf646a";
    private const string DatesOracleTinyFp =
        "93e27e5b663c87483e28aa67359123f1cf0421e206dc60bc62b72739e6f7fcf0";

    private static bool Phase85ConferenceDatesCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 85 — Conference dates (loose windows, Mon-Sun week cap, "
                          + "rotation-spaced rematches) ==");
        var pass = true;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}"
                              + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var stock = LoadWorld(Path.Combine(baseDir, "worlds", "stock-d1.world.json"));
            var tiny = LoadWorld(Path.Combine(baseDir, "worlds", "fixture-tiny.world.json"));
            var sched = LoadWorld(Path.Combine(baseDir, "worlds", "fixture-schedule.world.json"));
            const long seed = 20260720;
            const int year = SeasonDefaultStartYear;

            // ── C1 — the S93 layer is untouched, WITH a discriminating signal ──────
            var games = BuildSeasonSchedule(stock, seed);
            var structuralBefore = ScheduleFingerprint(games);
            var datedFp = SeasonDateSchedule(stock, games, year);
            var structuralAfter = ScheduleFingerprint(games);
            Check("C1 structural fingerprint identical before and after dating "
                  + "(the four named fields cannot see the fifth)",
                  structuralBefore == structuralAfter
                  && structuralBefore.StartsWith("6f79d663", StringComparison.Ordinal),
                  structuralBefore[..8]);
            var tinyGames = BuildSeasonSchedule(tiny, seed);
            var tinyDatedFp = SeasonDateSchedule(tiny, tinyGames, year);
            Check("C12 ★ golden parity with the oracle, EXACT — stock and fixture-tiny "
                  + "dated fingerprints",
                  datedFp == DatesOracleStockFp && tinyDatedFp == DatesOracleTinyFp,
                  $"stock {datedFp[..8]}… tiny {tinyDatedFp[..8]}…");

            // per-league scaffolding used by everything below
            var confOf = stock.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
            var byConf = stock.Schools.GroupBy(s => s.ConferenceId)
                              .ToDictionary(g => g.Key, g => g.Select(s => s.Id).OrderBy(x => x).ToList());
            var idxByConf = new Dictionary<int, List<int>>();
            for (var i = 0; i < games.Count; i++)
            {
                var cid = confOf[games[i].HomeId];
                if (!idxByConf.TryGetValue(cid, out var l)) idxByConf[cid] = l = new();
                l.Add(i);
            }

            var maxWdLoad = 0; var maxWeLoad = 0;
            var windowBad = 0; var nightBad = 0; var totalsBad = 0; var xmasBad = 0;
            var edgeBad = 0; var wallWeekBad = 0; var capBad = 0; var idleBad = 0;
            var adjBad = 0; var quarterBad = 0; var budgetBad = 0; var decOutside20AndRuled = 0;
            foreach (var c in stock.Conferences.OrderBy(c => c.Id))
            {
                if (c.Games == 0 || !byConf.ContainsKey(c.Id)) continue;
                var members = byConf[c.Id];
                var n = members.Count;
                var active = SeasonActiveNights(c.Nights, n, "");
                var (window, wall) = SeasonLeagueWindow(year, c.Weeks, c.TourneyOffsetDays, active, "");
                var targets = SeasonWeeklyTargets(n, c.Games, c.Weeks, "");
                var idxs = idxByConf[c.Id];
                var xmas = SeasonMonday(new DateOnly(year, 12, 25));
                var wkCount = new Dictionary<DateOnly, int>();
                var teamWeek = new Dictionary<(int, DateOnly), int>();
                var teamHalf = new Dictionary<(int, DateOnly, bool), int>();
                var byDate = new Dictionary<DateOnly, int>();
                var teamGames = members.ToDictionary(t => t, _ => new List<(DateOnly D, int I)>());
                foreach (var i in idxs)
                {
                    var d = games[i].Date!.Value;
                    var wm = SeasonMonday(d);
                    if (d < new DateOnly(year, 11, 1) || d > wall || !window.Contains(wm)) windowBad++;
                    if (!active.Contains(((int)d.DayOfWeek + 6) % 7)) nightBad++;
                    if (wm == xmas) xmasBad++;
                    wkCount[wm] = wkCount.GetValueOrDefault(wm) + 1;
                    byDate[d] = byDate.GetValueOrDefault(d) + 1;
                    var half = SeasonIsWeekend(d);
                    teamWeek[(games[i].HomeId, wm)] = teamWeek.GetValueOrDefault((games[i].HomeId, wm)) + 1;
                    teamWeek[(games[i].AwayId, wm)] = teamWeek.GetValueOrDefault((games[i].AwayId, wm)) + 1;
                    teamHalf[(games[i].HomeId, wm, half)] = teamHalf.GetValueOrDefault((games[i].HomeId, wm, half)) + 1;
                    teamHalf[(games[i].AwayId, wm, half)] = teamHalf.GetValueOrDefault((games[i].AwayId, wm, half)) + 1;
                    teamGames[games[i].HomeId].Add((d, i));
                    teamGames[games[i].AwayId].Add((d, i));
                }
                for (var w = 0; w < window.Count; w++)
                    if (wkCount.GetValueOrDefault(window[w]) != targets[w]) totalsBad++;
                if (wkCount.GetValueOrDefault(window[0]) == 0
                    || wkCount.GetValueOrDefault(window[^1]) == 0) edgeBad++;
                var wallWeek = SeasonMonday(wall);
                if (wallWeek != window[^1] && wkCount.GetValueOrDefault(wallWeek) != 0) wallWeekBad++;
                foreach (var ((_, _, hf), v) in teamHalf)
                    if (hf) maxWeLoad = Math.Max(maxWeLoad, v);
                    else maxWdLoad = Math.Max(maxWdLoad, v);
                foreach (var v in byDate.Values) if (v > n / 2) capBad++;
                var idle = 2 * c.Weeks - c.Games;
                foreach (var t in members)
                {
                    var mine = teamGames[t].OrderBy(x => x.D).ThenBy(x => x.I).ToList();
                    if (mine.Count != c.Games) budgetBad++;
                    var played = window.Sum(wm => teamWeek.GetValueOrDefault((t, wm)));
                    if (2 * c.Weeks - played != idle + (c.Games - mine.Count)) idleBad++;
                    var seen = new Dictionary<int, int>();
                    for (var si = 0; si < mine.Count; si++)
                    {
                        var g2 = games[mine[si].I];
                        var opp = g2.HomeId == t ? g2.AwayId : g2.HomeId;
                        if (si > 0)
                        {
                            var pg = games[mine[si - 1].I];
                            var prevOpp = pg.HomeId == t ? pg.AwayId : pg.HomeId;
                            if (prevOpp == opp) adjBad++;
                        }
                        if (seen.TryGetValue(opp, out var prevIdx)
                            && SeasonQuarterOf(prevIdx, c.Games) == SeasonQuarterOf(si, c.Games))
                            quarterBad++;
                        seen[opp] = si;
                    }
                }
            }
            Check("C2 ★ AT MOST ONE WEEKDAY (Mon-Fri) AND ONE WEEKEND (Sat-Sun) CONFERENCE "
                  + "GAME PER TEAM PER MONDAY-TO-SUNDAY WEEK — every team, every game "
                  + "(S105.2; subsumes and replaces the never-three-in-a-week test)",
                  maxWdLoad <= 1 && maxWeLoad <= 1,
                  $"max weekday {maxWdLoad}, max weekend {maxWeLoad}");
            Check("C3 every game inside its league's window, on or before the wall, "
                  + "on an active authored night", windowBad == 0 && nightBad == 0);
            Check("C4 the schedule USES the whole window: exact weekly totals (heavier "
                  + "weeks latest), non-empty edge weeks, Christmas week empty, and the "
                  + "wall's own week (when distinct) holds zero games",
                  totalsBad == 0 && edgeBad == 0 && xmasBad == 0 && wallWeekBad == 0,
                  $"totals {totalsBad} edges {edgeBad} xmas {xmasBad} wallWeek {wallWeekBad}");
            Check("C5 idle slots conserve at 2·weeks − games per team (a conservation "
                  + "line, not evidence — R5's real content is the shared window, C4)",
                  idleBad == 0);
            Check("C6 the budget closes: every team dated exactly its S93 count; no date "
                  + "above ⌊n/2⌋", budgetBad == 0 && capBad == 0);
            Check("C7 rematch spacing: never adjacent in either team's own sequence, and "
                  + "ZERO same-quarter collisions — the whole stock world",
                  adjBad == 0 && quarterBad == 0, $"adjacent {adjBad} sameQuarter {quarterBad}");

            // ── C2's controls — the checker itself is what is under test. The rule
            //    checker mirrors C2 exactly: per Mon-Sun week, per half, at most one. ──
            static bool BreaksWeekdayWeekendRule(IEnumerable<DateOnly> ds)
            {
                var byHalf = new Dictionary<(DateOnly, bool), int>();
                foreach (var d in ds)
                {
                    var k = (SeasonMonday(d), SeasonIsWeekend(d));
                    byHalf[k] = byHalf.GetValueOrDefault(k) + 1;
                }
                return byHalf.Values.Any(v => v > 1);
            }
            var wdDouble = new[] { new DateOnly(2027, 1, 5), new DateOnly(2027, 1, 6) };
            Check("C2 ★ negative control (§4.2): a constructed Tuesday+Wednesday week — "
                  + "two weekday games, legal under the OLD rule — is REJECTED",
                  BreaksWeekdayWeekendRule(wdDouble));
            var weDouble = new[] { new DateOnly(2027, 1, 9), new DateOnly(2027, 1, 10) };
            Check("C2 ★ negative control (§4.3): a Saturday+Sunday pair — the weekend "
                  + "double Emmett ruled out by name — is REJECTED",
                  BreaksWeekdayWeekendRule(weDouble));
            var violation = new[] { new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 6),
                                    new DateOnly(2027, 1, 9) };            // Mon/Wed/Sat: one week
            Check("C2 negative control: the Mon/Wed/Sat triple in ONE calendar week is "
                  + "still rejected — now for a second reason (two weekday games) on top "
                  + "of its three-game total", BreaksWeekdayWeekendRule(violation));
            var uconn = new[] { new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 7),
                                new DateOnly(2026, 1, 10) };               // Sun/Wed/Sat: real, legal
            Check("C2 ★ acceptance control (§4.4): the real UConn Sun-Jan-4 / Wed-Jan-7 / "
                  + "Sat-Jan-10 pattern — three in seven ROLLING days, the Sunday in the "
                  + "week before — is ACCEPTED, so the rolling-window reading cannot return",
                  !BreaksWeekdayWeekendRule(uconn));
            var ivyPair = new[] { new DateOnly(2027, 1, 8), new DateOnly(2027, 1, 9) };
            Check("C2 ★ acceptance control (§4.5): the Ivy League and Big Sky "
                  + "Friday+Saturday pair is LEGAL — A1: Friday is the weekday game",
                  !BreaksWeekdayWeekendRule(ivyPair));
            var sunMon = new[] { new DateOnly(2027, 1, 10), new DateOnly(2027, 1, 11) };
            Check("C2 acceptance control (§4.7): a Sunday-then-Monday consecutive pair is "
                  + "ACCEPTED — different Mon-Sun weeks, one weekend then one weekday game "
                  + "(the Ivy's one-day turnaround wearing the other order; synthetic, no "
                  + "stock league authors both nights — flagged as a consequence of the "
                  + "rule, not a decision)", !BreaksWeekdayWeekendRule(sunMon));
            var wk0 = new DateOnly(2027, 1, 4);                            // a Monday
            Check("C16 ★ THE WEEKEND DEFINITION OVER ALL SEVEN DAYS (§4.6) — Mon-Fri "
                  + "false, Sat/Sun true; the oracle asserts the same seven, so A1 is one "
                  + "line to change on each side and the two sides cannot drift apart",
                  Enumerable.Range(0, 7).All(i =>
                      SeasonIsWeekend(wk0.AddDays(i)) == i >= 5));

            // ── C4's negative control: empty an edge week, the checker must reject ──
            {
                var asun = stock.Conferences.First(c => c.Games == 20);
                var mems = byConf[asun.Id];
                var act = SeasonActiveNights(asun.Nights, mems.Count, "");
                var (win, _) = SeasonLeagueWindow(year, asun.Weeks, asun.TourneyOffsetDays, act, "");
                var tg = SeasonWeeklyTargets(mems.Count, asun.Games, asun.Weeks, "");
                var counts = win.ToDictionary(w => w, w => 0);
                for (var w = 0; w < win.Count; w++) counts[win[w]] = tg[w];
                counts[win[^1]] = 0;                       // r9's bug: pack early, coast
                counts[win[0]] += tg[^1];
                var rejected = false;
                for (var w = 0; w < win.Count; w++)
                    if (counts[win[w]] != tg[w]) { rejected = true; break; }
                Check("C4 negative control: a valid-count schedule with its final authored "
                      + "week emptied into the front is rejected by the exact-totals rule "
                      + "(the r9 loose-container bug cannot ship green)", rejected);
            }

            // ── the atomic-week discriminator (r14/r15): the completed sorted week is
            //    legal; the obsolete mid-week reading of the SAME state rejects it ──
            {
                // committed sequence: team A's last game was vs B. This week: A hosts C on
                // Wednesday (D2) and B on Saturday (D1). Priority order tries Saturday
                // first, so the PARTIAL state is [.. B] + [Sat vs B] — adjacent.
                var committed = new List<int> { 2 };                     // A's seq: last opp B(id 2)
                var week = new[] { (Date: new DateOnly(2027, 1, 13), Opp: 3),   // Wed vs C
                                   (Date: new DateOnly(2027, 1, 16), Opp: 2) }; // Sat vs B
                var completedOk = true;
                var seqOpp = new List<int>(committed);
                foreach (var g2 in week.OrderBy(x => x.Date))
                {
                    if (seqOpp[^1] == g2.Opp) completedOk = false;
                    seqOpp.Add(g2.Opp);
                }
                var midWeekRejects = committed[^1] == week[1].Opp;       // Sat tried first
                Check("C7 ★ atomic-week discriminator: the completed Wed→Sat week is "
                      + "ACCEPTED (the Wednesday game separates the rematch) while the "
                      + "obsolete mid-week reading of the same partial state rejects it",
                      completedOk && midWeekRejects);
            }

            // ── C8 — the zero-game league gets no dates, and the season still builds ──
            Check("C8 the fourteen Independents are dated nowhere",
                  games.Where(g2 => confOf[g2.HomeId] ==
                                    stock.Conferences.First(c => c.Games == 0).Id).Count() == 0);

            // ── C9 — determinism, and ★ THE YEAR IS A DIAL (C13) ──────────────────
            var again = BuildSeasonSchedule(stock, seed);
            var againFp = SeasonDateSchedule(stock, again, year);
            var otherSeed = BuildSeasonSchedule(stock, seed + 1);
            var otherSeedFp = SeasonDateSchedule(stock, otherSeed, year);
            Check("C9 determinism: same world, same dates, twice — and identical at a "
                  + "different seed (the schedule consumes no randomness)",
                  againFp == datedFp && otherSeedFp == datedFp);
            var y2031 = BuildSeasonSchedule(stock, seed);
            var fp2031 = SeasonDateSchedule(stock, y2031, 2031);
            var sameStructure = y2031.Count == games.Count
                && Enumerable.Range(0, games.Count).All(i =>
                       y2031[i].HomeId == games[i].HomeId && y2031[i].AwayId == games[i].AwayId
                       && y2031[i].Date is not null);
            Check("C13 ★ THE YEAR IS A DIAL, NOT A CONSTANT: 2031 builds the identical "
                  + "structure with different dates and a different dated fingerprint",
                  sameStructure && fp2031 != datedFp, fp2031[..8]);

            // ── C10 — the refusals fire, each citing its own reason ───────────────
            var refusals = new (string Name, Action Act, string Frag)[]
            {
                ("weeks<G/2", () => SeasonWeeklyTargets(10, 18, 8, "x "), "at two a week"),
                ("weeks>nG/2", () => SeasonWeeklyTargets(4, 2, 5, "x "), "empty week"),
                ("negative offset", () => SeasonLeagueWindow(year, 9, -1, new List<int> { 5 }, "x "),
                 "negative"),
                ("pre-Nov-1 window", () => SeasonLeagueWindow(year, 25, 4, new List<int> { 5, 2 }, "x "),
                 "November 1 floor"),
                ("bad night", () => SeasonActiveNights(new[] { "sat", "xyz", "mon" }, 9, "x "),
                 "unrecognised"),
                ("dup night", () => SeasonActiveNights(new[] { "sat", "sat", "mon" }, 9, "x "),
                 "duplicate"),
                ("single opponent", () => SeasonDateConference(
                     new List<int> { 1, 2 }, 4, 3, 4, new[] { "sat", "wed", "mon" },
                     new List<(int, int)> { (1, 2), (2, 1), (1, 2), (2, 1) }, year, "x "),
                 "two played opponents"),
            };
            var refusalDetail = "";
            var refusalsOk = true;
            foreach (var (name, act, frag) in refusals)
            {
                try { act(); refusalsOk = false; refusalDetail += $"{name} MISSING; "; }
                catch (InvalidOperationException ex)
                {
                    if (!ex.Message.Contains(frag, StringComparison.Ordinal))
                    { refusalsOk = false; refusalDetail += $"{name} wrong words; "; }
                }
            }
            Check("C10 seven refusals fire by name, via direct calls — the two-sided week "
                  + "bound, the calendar faults, the night faults, and the single-opponent "
                  + "league (★ the capacity condition is deliberately absent: it is §4.3's "
                  + "theorem, unreachable once nights validate)", refusalsOk, refusalDetail);

            // Duo through the fixture (the standing single-opponent case), and the
            // whole-world path through a constructed negative world file.
            var duoRefused = false;
            try
            {
                var duo = sched.Conferences.First(c => c.Name == "Duo Conference");
                var mems = byConf.Count > 0 ? sched.Schools.Where(s => s.ConferenceId == duo.Id)
                                                   .Select(s => s.Id).OrderBy(x => x).ToList() : new();
                var duoGames = BuildSeasonSchedule(sched, seed)
                    .Where(g2 => sched.Schools.First(s => s.Id == g2.HomeId).ConferenceId == duo.Id)
                    .Select(g2 => (g2.HomeId, g2.AwayId)).ToList();
                SeasonDateConference(mems, duo.Games, duo.Weeks, duo.TourneyOffsetDays,
                                     duo.Nights, duoGames, year, "'Duo Conference' ");
            }
            catch (InvalidOperationException ex)
            { duoRefused = ex.Message.Contains("two played opponents", StringComparison.Ordinal); }
            Check("C10 the fixture's Duo Conference is the standing single-opponent refusal",
                  duoRefused);
            var negPath = Path.Combine(Path.GetTempPath(), $"charm_s94_neg_{Guid.NewGuid():N}.json");
            var negRefused = false; var negMsg = "";
            try
            {
                var text = File.ReadAllText(Path.Combine(baseDir, "worlds", "fixture-tiny.world.json"))
                    .Replace("\"weeks\": 9", "\"weeks\": 3", StringComparison.Ordinal);
                File.WriteAllText(negPath, text);
                try { LoadWorld(negPath); }
                catch (InvalidOperationException ex) { negRefused = true; negMsg = ex.Message; }
            }
            finally { if (File.Exists(negPath)) File.Delete(negPath); }
            Check("C10 the whole-world path: a league authored too few weeks is refused "
                  + "at load, by name", negRefused
                                        && negMsg.Contains("at two a week", StringComparison.Ordinal),
                  negMsg.Length > 60 ? negMsg[..60] : negMsg);

            // ── C11 — the showcase skeleton is wired and inert ────────────────────
            var showcase = SelectShowcaseGames(stock, games);
            Check("C11 the showcase skeleton is reachable, selects zero games, and dates "
                  + "moved for none of it", showcase.Count == 0
                                            && SeasonDateSchedule(stock,
                                                   BuildSeasonSchedule(stock, seed), year) == datedFp);

            // ── C15-adjacent honesty line: December's real composition ────────────
            var decByLeague = games.Where(g2 => g2.Date is { Month: 12 })
                .GroupBy(g2 => confOf[g2.HomeId])
                .ToDictionary(g2 => g2.Key, g2 => g2.Count());
            var deepDecember = games.Count(g2 => g2.Date is { Month: 12, Day: < 22 });
            Check("C14 December reads as ruled: the 20-game league owns every date before "
                  + "Dec 22; everything else is the ruled-fine Dec 28–31 openers (R12)",
                  games.Where(g2 => g2.Date is { Month: 12, Day: < 22 })
                       .All(g2 => stock.Conferences.First(c => c.Id == confOf[g2.HomeId]).Games == 20),
                  $"{deepDecember} deep-December games, {decByLeague.Count} leagues touch December");
        }
        catch (Exception ex)
        {
            Check("Phase 85 completed without an unexpected exception", false, ex.Message);
        }

        Console.WriteLine($"  Phase 85 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
