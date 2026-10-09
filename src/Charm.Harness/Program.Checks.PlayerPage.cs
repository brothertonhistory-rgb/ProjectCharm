using Charm.History;
using System.Globalization;
using System.Security.Cryptography;

namespace Charm.Harness;

// ============================================================================
//  Phase 106 — S116: THE STATS SURVIVE UNDER HIS NUMBER.
//
//  What must be proven (page-only; no basketball value is asserted):
//    C1 row schema v2 round-trips Started both ways; a started value of 2 is
//       refused by the format. (★ S117 — the row-schema-1 reading checks C1c/C1d
//       retired with that reader, C-60; Phase 107 C2 proves the refusal.)
//    C2 GS is the lineup that started: on the stock season one log, every block
//       has exactly five started rows per side and they are that school's five
//       starters; a starter who did not play is refused at write time (control).
//    C3 the page's arithmetic is the log's: every man of a three-season mte
//       career, every counter summed independently, G, GS, conference split,
//       per-game rounding, Career = the sum of seasons.
//    C4 minutes: every row ≤ the game's length; credits sum to five men's worth
//       per side per game; a man with credits equal to the possession count reads
//       exactly 40 / 45 / 50.
//    C5 the two-season man and the departed senior.
//    C6 the lookup door: every issued number resolves to the identity the roster
//       carries or to null, bijectively onto the roster; a number at or above the
//       high-water resolves to null in every season; the commands refuse by name.
//    C7 the three states: a missing season and an unreadable season each print a
//       "no record" line and star the career; a freshman has no row before he
//       arrived; a departed senior none after he left; a full career has no star.
//    C9 the conference tournament is its own kind of game: every tournament game
//       marked and no other; the league flag still means the league slate only (what
//       the standings and host memory read); the player's conference table counts
//       both; a game marked both ways is refused. (★ S117 — C9c/C9d, the
//       block-schema-1 checks, retired with that reader, C-60.)
//    C10 every game carries its date and site: equal to the fixture that was played,
//       game by game; H / A / N read off it; the writer refuses a game with no date.
//       (★ S117 — C10c retired with the block-schema-1 reader.)
//    C8 the fingerprint wall: the career season one is the pre-S112 capture game
//       for game; the legacy season's seven fingerprints and every game unmoved.
// ============================================================================

internal static partial class Program
{
    private const long PlayerPageCheckSeed = 20260720;   // Phase 105's seed: the same stock career season one

    private static bool Phase106PlayerPageCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 106 — S116: the stats survive. Row schema v2 records who started, the player page " +
                          "is the log's arithmetic under one number, the three states of the walk, and the legacy season is unmoved ==");
        var pass = true;
        var assertions = 0;

        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static string GamesDigest(SeasonRunOutcome run) => RatingSha(string.Concat(run.Results.Select((x, i) =>
            $"{i}|{x.HomeId}|{x.AwayId}|{x.HomeScore}|{x.AwayScore}|{run.PossessionCounts[i]}\n")));
        static Exception? Refusal(Action a)
        {
            try { a(); return null; }
            catch (InvalidOperationException ex) { return ex; }
            catch (GameLogException ex) { return ex; }
            catch (HistoryException ex) { return ex; }
        }
        static string Blame(Exception? ex) => ex?.Message is { } m ? m[..Math.Min(110, m.Length)] : "(no refusal)";
        static string Capture(Func<int> run)
        {
            var old = Console.Out;
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Console.SetOut(sw);
            try { run(); } finally { Console.SetOut(old); }
            return sw.ToString();
        }

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s116-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var mte = LoadWorld(WorldPath("fixture-mte.world.json"));
            var mteFp = WorldFingerprint(mte);

            SeasonRunOutcome Career(WorldFile w, long seed, string path)
            {
                using var store = HistoryStore.Open(path, WorldFingerprint(w));
                return RunSeasonCore(w, seed, configPath, verbose: false, store, retainGameLog: true);
            }
            GameLogV1 ReadLog(WorldFile w, string path, long season)
            {
                using var store = HistoryStore.Open(path, WorldFingerprint(w));
                return GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(path, season),
                    new GameLogBindings(store.HistoryId, store.WorldFingerprint, season));
            }

            // ── The mte career every later section reads: three seasons, all logged ──
            var mtePath = Path.Combine(scratch, "mte", "career.json");
            var seedTwo = SeasonTwoSeed(PlayerPageCheckSeed);
            Career(mte, PlayerPageCheckSeed, mtePath);
            Career(mte, seedTwo, mtePath);
            Career(mte, seedTwo + 1, mtePath);
            var mteLogs = new[] { 1L, 2L, 3L }.Select(s => ReadLog(mte, mtePath, s)).ToArray();
            var highWater = PeekState(mtePath).NextPersonId;

            // ── C1: row schema v2 round-trip, the started domain, a v1 file ────────
            {
                var p = Path.Combine(scratch, "c1", "career.json");
                var sched = new string('d', 64);
                using var h = HistoryStore.Open(p, mteFp);
                var sid = h.ReserveSeason();
                var people = h.ReservePersons(2);
                var roster = people.Select((who, i) => new RosterEntryV2(who, 1, i, i + 1, $"C1_{i}", "", RosterPosition.Guard,
                                                                          i == 0, 5, 1.0, new short[38], 0, 0.0, "")).ToList();
                var facts = new GameBlockFactsV1(h.ReserveGames(1)[0], 0, 1, 2, true, 70, 68, 0, 140,
                                                 Date: new DateOnly(2026, 11, 10), HasHost: true,
                                                 Periods: new PeriodScoreV1[] { new(35, 34), new(35, 34) });   // ★ S117
                var rows = new List<PerGameStatRowV1>
                {
                    new(people[0], 1, 0, 1, 140, 70, 5, 2, 1, 0, 2, 2, 1, 3, 1, 1, 0, 2, 1, 1, 0, 0, 12, 20, 9, Started: true),
                    new(people[1], 1, 1, 2, 40, 20, 3, 1, 2, 1, 0, 0, 0, 1, 2, 0, 0, 1, 0, 0, 1, 1, 4, 6, 3, Started: false),
                };
                using (var w = GameLogWriter.Create(p, h.HistoryId, mteFp, sched, sid, roster))
                {
                    w.AppendGame(facts, rows);
                    w.Finalize(1);
                }
                var raw = RawOf(sid);
                var finalPath = GameLogWriter.FinalPathFor(p, raw);
                var bind = new GameLogBindings(h.HistoryId, mteFp, raw, sched);
                var back = GameLogReader.ReadFinalized(finalPath, bind);
                Check("C1a: a row with Started true and one with false round-trip — all fields identical (record equality over the 26)",
                      back.Blocks.Count == 1 && back.Blocks[0].Rows.Count == 2
                      && back.Blocks[0].Rows[0] == rows[0] && back.Blocks[0].Rows[1] == rows[1]
                      && back.Blocks[0].Rows[0].Started && !back.Blocks[0].Rows[1].Started);

                // A started value of 2: patch the first row's started long, re-seal the block and the digest.
                var bytes = File.ReadAllBytes(finalPath);
                var blockStart = 128 + 32 + roster.Count * 256 + 8;
                BitConverter.GetBytes(2L).CopyTo(bytes, blockStart + 56 + 2 * 8 + 188);   // ★ S117: past the two halves
                ResealCheckLog(bytes, blockStart, rows.Count, 196);
                var badPath = Path.Combine(scratch, "c1", "bad.log");
                File.WriteAllBytes(badPath, bytes);
                var two = Refusal(() => GameLogReader.ReadFinalized(badPath, bind));
                Check("C1b: a started value of 2 is refused by the FORMAT (re-sealed, so only the value can be at fault)",
                      two is GameLogException { Error: GameLogError.InvalidRow } && two.Message.Contains("started value 2", StringComparison.Ordinal),
                      Blame(two));
            }

            // ── C2 + C4 (stock) + C8a: the stock career's season one ──────────────
            SeasonRunOutcome stockOne;
            {
                var path = Path.Combine(scratch, "stock", "career.json");
                stockOne = Career(stock, PlayerPageCheckSeed, path);
                Check("C8a: ★ the career's season one plays the S118.1 capture game for game (the row change moved no game)",
                      GamesDigest(stockOne) == RatingGoldenPreS112GameDigest, $"{stockOne.PlayedGames.Count} games");
                var log = ReadLog(stock, path, 1);
                var starters = log.Roster.Where(e => e.IsStarter).GroupBy(e => e.SchoolId)
                                  .ToDictionary(g => g.Key, g => g.Select(e => e.PersonId).ToHashSet());
                var badBlocks = 0;
                foreach (var b in log.Blocks)
                    foreach (var school in new[] { b.Facts.HomeSchoolId, b.Facts.AwaySchoolId })
                    {
                        var started = b.Rows.Where(r => r.SchoolId == school && r.Started).Select(r => r.PersonId).ToHashSet();
                        if (started.Count != 5 || !started.SetEquals(starters[school])) badBlocks++;
                    }
                Check("C2a: ★ every block, every side: exactly five rows marked started, and they are that school's five starters",
                      badBlocks == 0, $"{log.Blocks.Count} games, {badBlocks} sides wrong");
                var benchStarts = log.Blocks.Sum(b => b.Rows.Count(r => r.Started && !starters[r.SchoolId].Contains(r.PersonId)));
                Check("C2b: no reserve is ever marked started", benchStarts == 0);
                Console.WriteLine(Inv($"  (page) stock season one: {log.TotalRowCount:N0} rows, {log.TotalRowCount * 8:N0} bytes more than row schema 1 would have written; GS {log.Blocks.Sum(b => b.Rows.Count(r => r.Started)):N0} = 10 x {log.Blocks.Count:N0} games"));

                // Negative control: a man marked started who did not play is refused at write time.
                var someone = stockOne.League.PlayerSeasons.Keys.OrderBy(k => k).First();
                var still = new Dictionary<int, RetentionSnapshot> { [someone] = RetentionSnapshot.Of(stockOne.League.PlayerSeasons[someone]) };
                var ghost = Refusal(() => RetentionRowsAfter(stockOne.League, new RetentionBefore(still, new HashSet<int> { someone }), -1));
                Check("C2c: NEGATIVE CONTROL — a starter with no row is refused at write time (GS can never silently undercount)",
                      ghost is GameLogException { Error: GameLogError.InvalidRow } && ghost.Message.Contains("men started but", StringComparison.Ordinal),
                      Blame(ghost));

                // C4 — minutes.
                var over = 0; var unbalanced = 0; var maxShare = 0.0;
                foreach (var b in log.Blocks)
                {
                    foreach (var r in b.Rows)
                    {
                        if (CareerGameMinutes(r, b.Facts) > CareerGameLength(b.Facts) + 1e-9) over++;
                        maxShare = Math.Max(maxShare, (double)r.Credits / b.Facts.PossessionCount);
                    }
                    foreach (var school in new[] { b.Facts.HomeSchoolId, b.Facts.AwaySchoolId })
                        if (b.Rows.Where(r => r.SchoolId == school).Sum(r => r.Credits) != 5 * b.Facts.PossessionCount) unbalanced++;
                }
                Check("C4a: every row's minutes are at most the game's length", over == 0, Inv($"{log.TotalRowCount:N0} rows; the busiest man played {maxShare:P1} of a game"));
                Check("C4b: per side per game, credits sum to exactly five men x the possession count — so minutes sum to 5 x the game's length",
                      unbalanced == 0, $"{unbalanced} sides off");
                var any = log.Blocks[0];
                var full = any.Rows[0] with { Credits = any.Facts.PossessionCount };
                Check("C4c: a man with credits equal to the possession count reads exactly 40, and 45 / 50 with one / two overtimes",
                      CareerGameMinutes(full, any.Facts with { OvertimePeriods = 0 }) == 40.0
                      && CareerGameMinutes(full, any.Facts with { OvertimePeriods = 1 }) == 45.0
                      && CareerGameMinutes(full, any.Facts with { OvertimePeriods = 2 }) == 50.0);

                // ── C9: the conference tournament, marked on the log ─────────────────
                var schoolConf = stock.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
                var tourney = log.Blocks.Where(b => b.Facts.IsConferenceTournamentGame).ToList();
                var league = log.Blocks.Count(b => b.Facts.IsConferenceGame);
                Check("C9a: ★ every conference tournament game is marked as one and nothing else is — and the league flag still " +
                      "marks exactly the league slate (what the standings and next season's host memory read)",
                      tourney.Count == stockOne.ConferenceTournamentGameCount && league == stockOne.ConferenceGameCount
                      && !log.Blocks.Any(b => b.Facts.IsConferenceGame && b.Facts.IsConferenceTournamentGame)
                      && tourney.All(b => schoolConf[b.Facts.HomeSchoolId] == schoolConf[b.Facts.AwaySchoolId]),
                      $"{tourney.Count} tournament games, {league} league games");
                var oneSeason = new List<(long, GameLogV1?, string?)> { (1L, log, null) };
                var tourneyMan = tourney.SelectMany(b => b.Rows).Select(r => r.PersonId).First();
                var tPage = ReadPlayerCareer(oneSeason, stock, l => l.Roster.Any(e => e.PersonId == tourneyMan) ? tourneyMan : null, "x");
                var leagueRows = log.Blocks.Where(b => b.Facts.IsConferenceGame).Count(b => b.Rows.Any(r => r.PersonId == tourneyMan));
                var tourneyRows = tourney.Count(b => b.Rows.Any(r => r.PersonId == tourneyMan));
                Check("C9b: ★ the player's conference table counts his league games AND his conference tournament games",
                      tourneyRows > 0 && tPage.Seasons[0].Conf.G == leagueRows + tourneyRows,
                      $"{tPage.Person}: {leagueRows} league + {tourneyRows} tournament = {tPage.Seasons[0].Conf.G}");

                var both = Refusal(() =>
                {
                    var p2 = Path.Combine(scratch, "both", "career.json");
                    using var h2 = HistoryStore.Open(p2, mteFp);
                    var sid2 = h2.ReserveSeason();
                    var who2 = h2.ReservePersons(1);
                    var ros = new List<RosterEntryV2> { new(who2[0], 1, 0, 1, "B", "", RosterPosition.Guard, true, 5, 1.0, new short[38], 0, 0.0, "") };
                    using var w2 = GameLogWriter.Create(p2, h2.HistoryId, mteFp, new string('d', 64), sid2, ros);
                    w2.AppendGame(new GameBlockFactsV1(h2.ReserveGames(1)[0], 0, 1, 2, true, 70, 68, 0, 140, IsConferenceTournamentGame: true,
                                                         Date: new DateOnly(2026, 11, 10), HasHost: true,
                                                         Periods: new PeriodScoreV1[] { new(35, 34), new(35, 34) }),   // ★ S117
                                  new List<PerGameStatRowV1> { new(who2[0], 1, 0, 1, 40, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Started: true) });
                });
                Check("C9e: NEGATIVE CONTROL — a game marked both league and conference tournament is refused at write time",
                      both is GameLogException { Error: GameLogError.DomainViolation }, Blame(both));

                // ── C10: the date and the site ───────────────────────────────────────
                var played = stockOne.PlayedGames;
                var dateWrong = 0; var siteWrong = 0;
                foreach (var b in log.Blocks)
                {
                    var fx = played[b.Facts.FixtureOrdinal].Game;
                    if (b.Facts.Date is null || b.Facts.Date != fx.Date) dateWrong++;
                    if (b.Facts.HasHost != fx.HasHost) siteWrong++;
                }
                var neutralGames = log.Blocks.Count(b => b.Facts.HasHost == false);
                Check("C10a: ★ every game in the log carries the date and the site of the fixture that was played, game by game",
                      log.Blocks.Count == played.Count && dateWrong == 0 && siteWrong == 0,
                      Inv($"{log.Blocks.Count} games, {neutralGames} neutral; {log.Blocks.Min(b => b.Facts.Date):yyyy-MM-dd} .. {log.Blocks.Max(b => b.Facts.Date):yyyy-MM-dd}"));
                var hanWrong = 0; var h = 0; var a = 0; var n = 0;
                foreach (var b in log.Blocks)
                    foreach (var r in b.Rows)
                    {
                        var site = CareerSite(r, b.Facts);
                        var expect = b.Facts.HasHost == false ? "N" : r.SchoolId == b.Facts.HomeSchoolId ? "H" : "A";
                        if (site != expect) hanWrong++;
                        if (site == "H") h++; else if (site == "A") a++; else if (site == "N") n++;
                    }
                Check("C10b: H / A / N per man per game — hosted: the home school H and the visitor A; no host: both N",
                      hanWrong == 0 && h > 0 && a > 0 && n > 0, Inv($"{h:N0} H / {a:N0} A / {n:N0} N rows"));
                var noDate = Refusal(() =>
                {
                    var p3 = Path.Combine(scratch, "nodate", "career.json");
                    using var h3 = HistoryStore.Open(p3, mteFp);
                    var sid3 = h3.ReserveSeason();
                    var who3 = h3.ReservePersons(1);
                    var ros = new List<RosterEntryV2> { new(who3[0], 1, 0, 1, "D", "", RosterPosition.Guard, true, 5, 1.0, new short[38], 0, 0.0, "") };
                    using var w3 = GameLogWriter.Create(p3, h3.HistoryId, mteFp, new string('d', 64), sid3, ros);
                    w3.AppendGame(new GameBlockFactsV1(h3.ReserveGames(1)[0], 0, 1, 2, true, 70, 68, 0, 140, HasHost: true,
                                                       Periods: new PeriodScoreV1[] { new(35, 34), new(35, 34) }),   // ★ S117
                                  new List<PerGameStatRowV1> { new(who3[0], 1, 0, 1, 40, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Started: true) });
                });
                Check("C10d: NEGATIVE CONTROL — a game with no date is refused at write time, by name",
                      noDate is GameLogException { Error: GameLogError.DomainViolation } && noDate.Message.Contains("has no date", StringComparison.Ordinal),
                      Blame(noDate));
            }

            // ── C3: the page's arithmetic is the log's (every man of the mte career) ─
            var walk = new[] { 1L, 2L, 3L }.Select((s, i) => (SeasonId: s, Log: (GameLogV1?)mteLogs[i], Reason: (string?)null)).ToList();
            PlayerCareer PageOf(PersonId who, List<(long, GameLogV1?, string?)> logs)
                => ReadPlayerCareer(logs, mte, l => l.Roster.Any(e => e.PersonId == who) ? who : null, "not found");
            var everyone = mteLogs.SelectMany(l => l.Roster.Select(e => e.PersonId)).Distinct().ToList();
            var pages = everyone.ToDictionary(who => who, who => PageOf(who, walk));
            {
                var wrong = new List<string>();
                foreach (var (who, page) in pages)
                {
                    foreach (var row in page.Seasons)
                    {
                        var log = mteLogs[row.SeasonId - 1];
                        foreach (var (line, onlyConf) in new[] { (row.All, false), (row.Conf, true) })
                        {
                            var mine = log.Blocks.Where(b => !onlyConf || b.Facts.IsConferenceGame || b.Facts.IsConferenceTournamentGame)
                                          .SelectMany(b => b.Rows.Where(r => r.PersonId == who)).ToList();
                            var sums = new long[21];
                            foreach (var r in mine) { var c = CountersOf(r); for (var i = 0; i < 21; i++) sums[i] += c[i]; }
                            if (line.G != mine.Count || line.Gs != mine.Count(r => r.Started) || !line.Counters.SequenceEqual(sums))
                                wrong.Add($"{who} season {row.SeasonId}{(onlyConf ? " conf" : "")}");
                            // Per-game rounding, recomputed here: points, rebounds, minutes, PF, FG%.
                            var cells = CareerCells(line, perGame: true);
                            static string D1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);
                            if (mine.Count > 0)
                            {
                                var pts = mine.Sum(r => 2 * r.Fgm + r.Tpm + r.Ftm);
                                var trb = mine.Sum(r => r.OReb + r.DReb);
                                var pf = mine.Sum(r => r.ShFoul + r.NsFoul + r.OffFoul);
                                var mp = log.Blocks.Where(b => !onlyConf || b.Facts.IsConferenceGame || b.Facts.IsConferenceTournamentGame)
                                            .Sum(b => b.Rows.Where(r => r.PersonId == who)
                                                       .Sum(r => r.Credits * (40.0 + 5 * b.Facts.OvertimePeriods) / b.Facts.PossessionCount));
                                var fga = mine.Sum(r => r.Fga);
                                var fgPct = fga == 0 ? "" : (mine.Sum(r => r.Fgm) / (double)fga).ToString(".000", CultureInfo.InvariantCulture);
                                if (cells[26] != D1(pts / (double)mine.Count) || cells[17] != D1(trb / (double)mine.Count)
                                    || cells[22] != D1(pf / (double)mine.Count) || cells[2] != D1(mp / mine.Count) || cells[5] != fgPct)
                                    wrong.Add($"{who} season {row.SeasonId}{(onlyConf ? " conf" : "")} rounding");
                            }
                        }
                    }
                    var gSum = page.Seasons.Sum(s => s.All.G);
                    var cSum = new long[21];
                    foreach (var s in page.Seasons) for (var i = 0; i < 21; i++) cSum[i] += s.All.Counters[i];
                    if (page.CareerAll.G != gSum || page.CareerAll.Gs != page.Seasons.Sum(s => s.All.Gs)
                        || !page.CareerAll.Counters.SequenceEqual(cSum)
                        || page.CareerConf.G != page.Seasons.Sum(s => s.Conf.G))
                        wrong.Add($"{who} career");
                }
                Check("C3a: ★ for every man of a three-season career: each season's totals are the sum of his rows (21 counters, " +
                      "summed here independently), G = rows, GS = started rows, conference = league plus conference tournament blocks, " +
                      "per-game = totals ÷ G as the page rounds, Career = the sum of seasons",
                      wrong.Count == 0, wrong.Count == 0 ? $"{pages.Count} men" : string.Join("; ", wrong.Take(4)));
                var anyConf = pages.Values.Any(p => p.Seasons.Any(s => s.Conf.G > 0 && s.Conf.G < s.All.G));
                Check("C3b: the conference split discriminates (some season has both conference and other games)", anyConf);
                var anyCareerOver1 = pages.Values.Count(p => p.Seasons.Count > 1);
                Check("C3c: the arithmetic was exercised across seasons (men with more than one season row exist)", anyCareerOver1 > 0, $"{anyCareerOver1} men");
            }

            // ── C5: the two-season man and the departed senior ────────────────────
            var r1 = mteLogs[0].RosterV2(); var r2 = mteLogs[1].RosterV2(); var r3 = mteLogs[2].RosterV2();
            var in2 = r2.Select(e => e.PersonId).ToHashSet(); var in3 = r3.Select(e => e.PersonId).ToHashSet();
            {
                var twoMan = r1.Where(e => in2.Contains(e.PersonId))
                               .FirstOrDefault(e => pages[e.PersonId].Seasons.Count(s => s.SeasonId <= 2 && s.All.G > 0) == 2);
                var tp = twoMan is null ? null : pages[twoMan.PersonId];
                Check("C5a: a returner has a season-one row and a season-two row under one number, and Career G is their sum",
                      tp is not null && tp.Seasons.Any(s => s.SeasonId == 1) && tp.Seasons.Any(s => s.SeasonId == 2)
                      && tp.CareerAll.G == tp.Seasons.Sum(s => s.All.G) && !tp.Starred,
                      tp is null ? "(none)" : Inv($"{tp.Person} ({tp.Name}): {string.Join(", ", tp.Seasons.Select(s => $"season {s.SeasonId} {s.All.G} G"))}, career {tp.CareerAll.G}"));
                var senior = r1.FirstOrDefault(e => e.Class == 3 && pages[e.PersonId].Seasons.Single().All.G > 0);
                var sp = senior is null ? null : pages[senior.PersonId];
                Check("C5b: a departed senior has one season row and nothing after — ABSENT, not a zero-game season, no star",
                      sp is not null && sp.Seasons.Count == 1 && sp.Seasons[0].SeasonId == 1 && sp.Entries.Count == 1 && !sp.Starred,
                      sp is null ? "(none)" : Inv($"{sp.Person}: season 1 only, {sp.CareerAll.G} G"));
            }

            // ── C6: the lookup door ───────────────────────────────────────────────
            {
                var resolvesWrong = 0; var notBijective = 0;
                var seen = new List<HashSet<PersonId>>();
                foreach (var log in mteLogs)
                {
                    var hits = new HashSet<PersonId>();
                    for (var n = 1L; n < highWater; n++)
                        if (log.PersonNumbered(n) is { } who)
                        {
                            if (!log.Roster.Any(e => e.PersonId == who)) resolvesWrong++;
                            if (!hits.Add(who)) notBijective++;
                        }
                    if (!hits.SetEquals(log.Roster.Select(e => e.PersonId))) notBijective++;
                    seen.Add(hits);
                }
                Check("C6a: every issued number resolves, season by season, to an identity that season's roster carries or to null — " +
                      "one number per man, covering the roster exactly",
                      resolvesWrong == 0 && notBijective == 0, Inv($"numbers 1..{highWater - 1} x 3 seasons"));
                var cannotMint = mteLogs.All(l => l.PersonNumbered(highWater) is null && l.PersonNumbered(highWater + 1_000) is null
                                                  && l.PersonNumbered(0) is null && l.PersonNumbered(-1) is null);
                Check("C6b: ★ the door cannot mint — the high-water, past it, zero and negative resolve to null in every season", cannotMint,
                      Inv($"high-water {highWater}"));

                // Find the C5 returner's number through the door, then drive the commands.
                var twoNumber = Enumerable.Range(1, (int)highWater - 1).Select(n => (long)n)
                    .First(n => mteLogs[0].PersonNumbered(n) is { } p && in2.Contains(p) && pages[p].Seasons.Count >= 2);
                var world = WorldPath("fixture-mte.world.json");
                var page = Capture(() => RunPlayer(new[] { "player", world, twoNumber.ToString(CultureInfo.InvariantCulture), "--history", mtePath }));
                Check("C6c: `player` prints the four tables and a Career row under each for a man found through the door",
                      page.Contains("Per game, all games\n", StringComparison.Ordinal) && page.Contains("Per game, conference games\n", StringComparison.Ordinal)
                      && page.Contains("Totals, all games\n", StringComparison.Ordinal) && page.Contains("Totals, conference games\n", StringComparison.Ordinal)
                      && page.Split('\n').Count(l => l.StartsWith("Career ", StringComparison.Ordinal)) == 4
                      && !page.Contains("Career*", StringComparison.Ordinal));
                var page2 = Capture(() => RunPlayer(new[] { "player", world, "person:" + twoNumber.ToString(CultureInfo.InvariantCulture), "--history", mtePath }));
                Check("C6d: the number typed as `people` prints it (person:N) is the same page", page2 == page);
                Console.WriteLine(Inv($"  (page) player {twoNumber}, first table:"));
                foreach (var l in page.Split('\n').SkipWhile(l => !l.StartsWith("Per game, all", StringComparison.Ordinal)).Take(6)) Console.WriteLine("    " + l);
                var unknown = Capture(() => RunPlayer(new[] { "player", world, highWater.ToString(CultureInfo.InvariantCulture), "--history", mtePath }));
                Check("C6e: a number no roster lists is refused by name (the high-water — issued to nobody yet)",
                      unknown.StartsWith("PLAYER ERROR: no season roster in this career lists number", StringComparison.Ordinal), unknown.Trim());
                var noHist = Capture(() => RunPlayer(new[] { "player", world, "2" }));
                var noPeopleHist = Capture(() => RunPeople(new[] { "people", world }));
                Check("C6f: `player` and `people` without --history are refused by name", noHist.Contains("--history <career.json> is required", StringComparison.Ordinal)
                      && noPeopleHist.Contains("--history <career.json> is required", StringComparison.Ordinal));
                var ghostCareer = Path.Combine(scratch, "nobody", "career.json");
                var missing = Capture(() => RunPlayer(new[] { "player", world, "2", "--history", ghostCareer }));
                Check("C6g: a page never starts a career — a missing career file is refused and nothing is created",
                      missing.Contains("there is no career at", StringComparison.Ordinal) && !File.Exists(ghostCareer) && !Directory.Exists(Path.GetDirectoryName(ghostCareer)));
                var people = Capture(() => RunPeople(new[] { "people", world, "--history", mtePath }));
                var listed = people.Split('\n').Count(l => l.StartsWith("person:", StringComparison.Ordinal));
                Check("C6h: `people` lists season three's roster, one line per man", people.StartsWith("People — season 3 roster", StringComparison.Ordinal)
                      && listed == r3.Count, $"{listed} of {r3.Count}");
            }

            // ── C7: the three states ──────────────────────────────────────────────
            {
                var freshman3 = r3.First(e => e.Class == 0);
                var fp = pages[freshman3.PersonId];
                Check("C7a: a season-three freshman's page has no row for seasons 1 or 2, and no star",
                      fp.Seasons.Count == 1 && fp.Seasons[0].SeasonId == 3 && fp.Entries.Count == 1 && !fp.Starred);
                var spanner = r1.First(e => e.Class <= 1 && in3.Contains(e.PersonId));
                Check("C7b: NEGATIVE CONTROL — a three-season man with every season on record carries no star",
                      pages[spanner.PersonId].Seasons.Count == 3 && !pages[spanner.PersonId].Starred
                      && !RenderPlayerPage(pages[spanner.PersonId], "x").Contains("Career*", StringComparison.Ordinal));

                // Season two made unreadable (one byte flipped inside a block), then missing.
                var s2Path = GameLogWriter.FinalPathFor(mtePath, 2);
                var original = File.ReadAllBytes(s2Path);
                var flipped = (byte[])original.Clone();
                var firstBlock = 128 + 32 + r2.Count * 256 + 8;
                // ★ S117 — past the 56-byte header and the period list (two halves + any overtimes): inside the first row.
                var at = firstBlock + 56 + (2 + BitConverter.ToInt16(original, firstBlock + 36)) * 8 + 40;
                flipped[at] ^= 0x01;
                File.WriteAllBytes(s2Path, flipped);
                PlayerCareer Live(PersonId who)
                {
                    using var h = HistoryStore.Open(mtePath, mteFp);
                    return ReadPlayerCareer(h, mte, who);
                }
                var unreadable = Live(spanner.PersonId);
                var expectAll = new CareerLine();
                expectAll.AddLine(pages[spanner.PersonId].Seasons[0].All);
                expectAll.AddLine(pages[spanner.PersonId].Seasons[2].All);
                var text = RenderPlayerPage(unreadable, "x");
                Check("C7c: ★ an UNREADABLE season prints a no-record line naming the reader's reason, stars the career and names it, " +
                      "and Career sums seasons 1 and 3 only",
                      unreadable.Starred && unreadable.SeasonsWithoutRecord.SequenceEqual(new[] { 2L })
                      && unreadable.Seasons.Select(s => s.SeasonId).SequenceEqual(new[] { 1L, 3L })
                      && unreadable.CareerAll.G == expectAll.G && unreadable.CareerAll.Counters.SequenceEqual(expectAll.Counters)
                      && text.Contains("Season 2: no record — the log could not be read (BlockChecksumMismatch).", StringComparison.Ordinal)
                      && text.Split('\n').Count(l => l.StartsWith("Career* ", StringComparison.Ordinal)) == 4
                      && text.Contains("no record exists for season 2.", StringComparison.Ordinal),
                      Inv($"career {unreadable.CareerAll.G} G = {pages[spanner.PersonId].Seasons[0].All.G} + {pages[spanner.PersonId].Seasons[2].All.G}"));
                File.Delete(s2Path);
                var gone = Live(spanner.PersonId);
                Check("C7d: a MISSING season does the same, naming that no log was kept — never skipped, never fatal",
                      gone.Starred && gone.SeasonsWithoutRecord.SequenceEqual(new[] { 2L })
                      && RenderPlayerPage(gone, "x").Contains("Season 2: no record — no season log was kept.", StringComparison.Ordinal)
                      && gone.CareerAll.Counters.SequenceEqual(expectAll.Counters));
                File.WriteAllBytes(s2Path, original);
                var restored = Live(spanner.PersonId);
                Check("C7e: the season restored, the star is gone and the page is the full career again",
                      !restored.Starred && restored.CareerAll.Counters.SequenceEqual(pages[spanner.PersonId].CareerAll.Counters));
            }

            // ── C8: the fingerprint wall — the stock legacy season ────────────────
            {
                var legacy = RunSeasonCore(stock, PlayerPageCheckSeed, configPath, verbose: false);
                var prefix = legacy.ConferenceGameCount + legacy.TournamentGameCount;
                var resultsFp = SeasonFingerprint(legacy.Results.Take(prefix).ToList(), legacy.PossessionCounts.Take(prefix).ToList());
                Check("C8b: #1 conference schedule UNMOVED", legacy.Fingerprint == MatchGoldenConferenceFp);
                Check("C8c: #2 conference dated UNMOVED", legacy.DatedFingerprint == MatchGoldenDatedFp);
                Check("C8d: #3 event games UNMOVED", legacy.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C8e: #4 results+possessions UNMOVED over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C8f: #5 non-conference dated UNMOVED", legacy.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C8g: #6 conference tournaments UNMOVED", legacy.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C8h: #7 buy games UNMOVED", legacy.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                Check("C8i: ★ every legacy game identical to the S118.1 capture", GamesDigest(legacy) == RatingGoldenPreS112GameDigest);
                Check("C8j: the career's season one and the legacy season are the same games", GamesDigest(stockOne) == GamesDigest(legacy));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 106 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 106 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }

    /// <summary>Re-seal one block of a finalized log after a byte edit: its 8-byte checksum (the
    /// first 8 bytes of SHA-256 over header + rows) and the footer's payload digest (SHA-256 of
    /// everything after the file header up to the footer). The format's own rules, restated
    /// here so a check can construct a defect the format must catch on its VALUE.</summary>
    private static void ResealCheckLog(byte[] file, int blockStart, int rowCount, int rowBytes)
    {
        const int header = 128, footer = 64;
        // ★ S117 — block schema 3: the 56-byte header, then two halves plus one entry per overtime.
        var blockHeader = 56 + (2 + BitConverter.ToInt16(file, blockStart + 36)) * 8;
        var trailerAt = blockStart + blockHeader + rowCount * rowBytes;
        SHA256.HashData(file.AsSpan(blockStart, trailerAt - blockStart))[..8].CopyTo(file, trailerAt);
        SHA256.HashData(file.AsSpan(header, file.Length - footer - header)).CopyTo(file, file.Length - footer + 16);
    }
}
