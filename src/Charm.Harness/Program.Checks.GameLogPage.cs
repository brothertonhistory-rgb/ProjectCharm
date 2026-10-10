using Charm.History;
using System.Globalization;
using System.Security.Cryptography;

namespace Charm.Harness;

// ============================================================================
//  Phase 107 — S117: THE GAME LOG, THE BOX SCORE, CAREER HIGHS.
//
//  What must be proven (page-only; no basketball value is asserted):
//    C1 the period list: every stock game's periods sum to its final, the list is
//       2 + OT long; the writer refuses a short list, a long list and a list off by
//       one point, and the reader refuses a sealed wrong score (negative controls);
//       a possession stamped outside 1..2+OT is refused where the list is built.
//    C2 the refusal (C-60): a log in the S116 layout (block schema 2, built here
//       from a real log) and a log declaring row schema 1 are each refused by the
//       standing sentence — by the reader and by every page command.
//    C3 the game log is the team's schedule: for every man of the stock season,
//       exactly his school's games in date order, Gtm 1..n, phases contiguous, his
//       played lines his rows, his DNPs the rest, Gcar only on played games; a
//       zero-line man has a full-DNP log; the bottom line is his season row and his
//       team's record. Two constructed schedules (two games on one date, a regular
//       season game inside the tournament) are refused by name. On a three-season
//       career Gcar continues across seasons, prints — after a season with no
//       record (unreadable, then missing), and the bottom line equals the player
//       page's season row cell for cell (minutes aside, which print as m:ss).
//    C4 the box score is the game: for every stock game, each team's lines are its
//       rows, its starters its five started rows, its DNPs its roster minus its rows,
//       its totals the sum of its lines, and its points plus its uncredited points
//       its final — the Uncredited line printed exactly where that is above zero.
//       ★ S118: that count is now a wall at zero (O-115 shipped; before: 686 games).
//    C5 career highs: for every man of a three-season career, each high is the
//       maximum over his games (recomputed here, minutes in decimal arithmetic), the
//       count is how many games reach it, the listed games exactly those; a 0 high
//       prints —; a season made unreadable stars the section and names it, and with
//       every season on record it is not starred (control).
//    C6 the commands refuse by name: no --history, a missing career (nothing is
//       created), a season the career does not have, a season he was not on, an
//       unknown reference.
//    C7 the fingerprint wall: the career's season one is the pre-S112 capture game
//       for game; the legacy season's seven fingerprints and every game unmoved.
// ============================================================================

internal static partial class Program
{
    private const long GameLogPageCheckSeed = 20260720;   // Phase 105's and 106's seed: the same stock career season one

    private static bool Phase107GameLogPageCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 107 — S117: the game log, the box score, career highs. Block schema 3 records the score by " +
                          "period, an older log is refused by name, every page is the log's arithmetic, and the legacy season is unmoved ==");
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
        static string Blame(Exception? ex) => ex?.Message is { } m ? m[..Math.Min(130, m.Length)] : "(no refusal)";
        static string Capture(Func<int> run)
        {
            var old = Console.Out;
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Console.SetOut(sw);
            try { run(); } finally { Console.SetOut(old); }
            return sw.ToString();
        }
        const string Older = "this career was saved by an older version of the game — start a new career";

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s117-" + Guid.NewGuid().ToString("N"));
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
            Func<GameLogV1, PersonId?> Is(PersonId who) => l => l.Roster.Any(e => e.PersonId == who) ? who : null;

            // ── The stock career season one every stock section reads ──────────────
            var stockPath = Path.Combine(scratch, "stock", "career.json");
            var stockOne = Career(stock, GameLogPageCheckSeed, stockPath);
            var stockLogPath = GameLogWriter.FinalPathFor(stockPath, 1);
            var log = ReadLog(stock, stockPath, 1);
            var oneSeason = new List<(long, GameLogV1?, string?)> { (1L, log, null) };

            // ── C1: the score by period ─────────────────────────────────────────────
            {
                var wrongLength = 0; var negative = 0; var wrongSum = 0; var halftimeIsFinal = 0;
                foreach (var b in log.Blocks)
                {
                    var p = b.Facts.Periods!;
                    if (p.Count != 2 + b.Facts.OvertimePeriods) wrongLength++;
                    if (p.Any(x => x.Home < 0 || x.Away < 0)) negative++;
                    if (p.Sum(x => x.Home) != b.Facts.HomeScore || p.Sum(x => x.Away) != b.Facts.AwayScore) wrongSum++;
                    if (p[0].Home == b.Facts.HomeScore && p[0].Away == b.Facts.AwayScore) halftimeIsFinal++;
                }
                var otGames = log.Blocks.Count(b => b.Facts.OvertimePeriods > 0);
                var otPeriods = log.Blocks.Sum(b => b.Facts.OvertimePeriods);
                Check("C1a: ★ every stock game lists two halves plus one entry per overtime, none negative, summing to its final",
                      wrongLength == 0 && negative == 0 && wrongSum == 0 && log.Blocks.Count == stockOne.PlayedGames.Count,
                      Inv($"{log.Blocks.Count:N0} games, {otGames} to overtime, {otPeriods} overtime periods; {halftimeIsFinal} game(s) whose halftime score was the final (printed, not a bar)"));
                Console.WriteLine(Inv($"  (page) stock career season one log: {new FileInfo(stockLogPath).Length:N0} bytes"));

                // Where the list is built: a possession stamped outside the game's periods is refused.
                var p3NoOt = Refusal(() => ScoreByPeriod(new[] { (1, true, 2), (3, false, 3) }, 0));
                var p0 = Refusal(() => ScoreByPeriod(new[] { (0, true, 2) }, 0));
                var okOt = ScoreByPeriod(new[] { (1, true, 2), (2, false, 3), (3, true, 1) }, 2);
                Check("C1b: NEGATIVE CONTROL — a possession stamped period 3 in a game with no overtime, or period 0, is refused " +
                      "where the list is built; with overtime it is filed, and a scoreless overtime is listed as 0-0",
                      p3NoOt is InvalidOperationException && p0 is InvalidOperationException
                      && okOt.Count == 4 && okOt[0] == new PeriodScoreV1(2, 0) && okOt[1] == new PeriodScoreV1(0, 3)
                      && okOt[2] == new PeriodScoreV1(1, 0) && okOt[3] == new PeriodScoreV1(0, 0),
                      Blame(p3NoOt));

                Exception? WriteWith(IReadOnlyList<PeriodScoreV1>? periods, string tag) => Refusal(() =>
                {
                    var p = Path.Combine(scratch, "w-" + tag, "career.json");
                    using var h = HistoryStore.Open(p, mteFp);
                    var sid = h.ReserveSeason();
                    var who = h.ReservePersons(1);
                    var ros = new List<RosterEntryV2> { new(who[0], 1, 0, 1, "P", "", RosterPosition.Guard, true, 5, 1.0, new short[38], 0, 0.0, "") };
                    using var w = GameLogWriter.Create(p, h.HistoryId, mteFp, new string('d', 64), sid, ros);
                    w.AppendGame(new GameBlockFactsV1(h.ReserveGames(1)[0], 0, 1, 2, true, 70, 68, 0, 140,
                                                      Date: new DateOnly(2026, 11, 10), HasHost: true, Periods: periods),
                                 new List<PerGameStatRowV1> { new(who[0], 1, 0, 1, 40, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Started: true) });
                    w.Finalize(1);
                });
                var good = WriteWith(new PeriodScoreV1[] { new(35, 34), new(35, 34) }, "good");
                var shortList = WriteWith(new PeriodScoreV1[] { new(70, 68) }, "short");
                var longList = WriteWith(new PeriodScoreV1[] { new(35, 34), new(35, 34), new(0, 0) }, "long");
                var offByOne = WriteWith(new PeriodScoreV1[] { new(35, 34), new(35, 33) }, "off");
                var none = WriteWith(null, "none");
                Check("C1c: the writer files a game whose two halves sum to its final (the control the next three are measured against)",
                      good is null, Blame(good));
                Check("C1d: NEGATIVE CONTROL — a one-entry list for a game with no overtime is refused at write time, by name",
                      shortList is GameLogException { Error: GameLogError.DomainViolation } && shortList.Message.Contains("lists 1 periods", StringComparison.Ordinal),
                      Blame(shortList));
                Check("C1e: NEGATIVE CONTROL — a three-entry list for a game with no overtime is refused at write time, by name",
                      longList is GameLogException { Error: GameLogError.DomainViolation } && longList.Message.Contains("lists 3 periods", StringComparison.Ordinal),
                      Blame(longList));
                Check("C1f: NEGATIVE CONTROL — a list one point off the final is refused at write time, by name (and so is no list at all)",
                      offByOne is GameLogException { Error: GameLogError.DomainViolation } && offByOne.Message.Contains("sum to 70-67, not the final 70-68", StringComparison.Ordinal)
                      && none is GameLogException && none.Message.Contains("no score by period", StringComparison.Ordinal),
                      Blame(offByOne));

                // The reader re-checks it on its value: a sealed file whose first half is one point off.
                var bytes = File.ReadAllBytes(stockLogPath);
                var rosterEntries = BitConverter.ToInt32(bytes, 128 + 8);
                var firstBlock = 128 + 32 + rosterEntries * 256 + 8;
                var firstRows = BitConverter.ToUInt16(bytes, firstBlock + 26);
                BitConverter.GetBytes(BitConverter.ToInt32(bytes, firstBlock + 56) + 1).CopyTo(bytes, firstBlock + 56);
                ResealCheckLog(bytes, firstBlock, firstRows, 196);
                var forgedPath = Path.Combine(scratch, "stock", "forged.log");
                File.WriteAllBytes(forgedPath, bytes);
                Exception? forged;
                using (var store = HistoryStore.Open(stockPath, WorldFingerprint(stock)))
                    forged = Refusal(() => GameLogReader.ReadFinalized(forgedPath, new GameLogBindings(store.HistoryId, store.WorldFingerprint, 1)));
                Check("C1g: NEGATIVE CONTROL — a re-sealed log whose first half is one point off its final is refused by the READER on its value",
                      forged is GameLogException { Error: GameLogError.DomainViolation } && forged.Message.Contains("periods sum to", StringComparison.Ordinal),
                      Blame(forged));
            }

            // ── C2: an older log is refused, by the standing sentence ───────────────
            {
                var oldPath = Path.Combine(scratch, "old", "career.json");
                Career(mte, GameLogPageCheckSeed, oldPath);
                var oldLog = GameLogWriter.FinalPathFor(oldPath, 1);
                var current = File.ReadAllBytes(oldLog);
                var s116 = DowngradeToBlockSchema2(current);
                File.WriteAllBytes(oldLog, s116);
                var blockTwo = Refusal(() => ReadLog(mte, oldPath, 1));
                Check("C2a: ★ a log in the S116 layout (block schema 2, no score by period — built here from a real log) is refused " +
                      "by the reader, whole, with the standing sentence",
                      blockTwo is GameLogException { Error: GameLogError.UnsupportedLogVersion }
                      && blockTwo.Message.StartsWith(Older, StringComparison.Ordinal),
                      Inv($"{s116.Length:N0} bytes, {current.Length - s116.Length:N0} fewer than block schema 3: ") + Blame(blockTwo));
                var world = WorldPath("fixture-mte.world.json");
                var player = Capture(() => RunPlayer(new[] { "player", world, "1", "--history", oldPath }));
                var people = Capture(() => RunPeople(new[] { "people", world, "--history", oldPath }));
                var gamelog = Capture(() => RunGameLog(new[] { "gamelog", world, "1", "1", "--history", oldPath }));
                var box = Capture(() => RunBox(new[] { "box", world, "1", "0", "--history", oldPath }));
                Check("C2b: every page command refuses that career by the same sentence — never a starred season, never \"unknown\"",
                      player.StartsWith("PLAYER ERROR: season 1: " + Older, StringComparison.Ordinal)
                      && people.StartsWith("PEOPLE ERROR: season 1: " + Older, StringComparison.Ordinal)
                      && gamelog.StartsWith("GAMELOG ERROR: season 1: " + Older, StringComparison.Ordinal)
                      && box.StartsWith("BOX ERROR: season 1: " + Older, StringComparison.Ordinal),
                      player.Trim()[..Math.Min(120, player.Trim().Length)]);
                var rowOne = (byte[])current.Clone();
                BitConverter.GetBytes((short)1).CopyTo(rowOne, 12);
                BitConverter.GetBytes(188).CopyTo(rowOne, 104);
                BitConverter.GetBytes(25).CopyTo(rowOne, 108);
                File.WriteAllBytes(oldLog, rowOne);
                var rowRefused = Refusal(() => ReadLog(mte, oldPath, 1));
                Check("C2c: a log declaring row schema 1 is refused by the same sentence",
                      rowRefused is GameLogException { Error: GameLogError.UnsupportedLogVersion }
                      && rowRefused.Message.StartsWith(Older, StringComparison.Ordinal), Blame(rowRefused));
                File.WriteAllBytes(oldLog, current);
                var restored = Refusal(() => ReadLog(mte, oldPath, 1));
                Check("C2d: the same file put back reads clean (the refusals above are the version, not the file)", restored is null, Blame(restored));
            }

            // ── C3: the game log is the team's schedule (every man of the stock season) ─
            {
                var bySchool = new Dictionary<int, List<GameLogBlockV1>>();
                foreach (var b in log.Blocks)
                    foreach (var s in new[] { b.Facts.HomeSchoolId, b.Facts.AwaySchoolId })
                        (bySchool.TryGetValue(s, out var l) ? l : bySchool[s] = new List<GameLogBlockV1>()).Add(b);
                var rowsOf = log.Blocks.SelectMany(b => b.Rows.Select(r => (b, r))).GroupBy(x => x.r.PersonId)
                                .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.b.Facts.FixtureOrdinal, x => x.r));
                var wrong = new List<string>();
                long dnps = 0; var zeroLine = 0; var zeroLineFullDnp = 0; var withTourney = 0; long dnpPrinted = 0;
                SeasonGameLog? sample = null;
                foreach (var e in log.Roster)
                {
                    var who = e.PersonId;
                    var g = ReadSeasonGameLog(oneSeason, stock, Is(who), "x", 1);
                    var team = bySchool[e.SchoolId];
                    var mine = rowsOf.TryGetValue(who, out var m) ? m : new Dictionary<int, PerGameStatRowV1>();
                    var lines = g.Lines.ToList();
                    var ok = lines.Count == team.Count
                        && lines.Select(l => l.Facts.FixtureOrdinal).ToHashSet().SetEquals(team.Select(b => b.Facts.FixtureOrdinal))
                        && lines.Select((l, i) => l.Gtm == i + 1).All(x => x)
                        && lines.Zip(lines.Skip(1)).All(z => z.First.Facts.Date!.Value < z.Second.Facts.Date!.Value);
                    var firstCt = lines.FindIndex(l => l.Facts.IsConferenceTournamentGame);
                    if (firstCt >= 0) { withTourney++; ok &= lines.Skip(firstCt).All(l => l.Facts.IsConferenceTournamentGame); }
                    // Played lines are his rows (the same row objects), DNPs the rest, Gcar 1..k on played lines only.
                    var k = 0L;
                    foreach (var l in lines)
                    {
                        var has = mine.TryGetValue(l.Facts.FixtureOrdinal, out var r);
                        ok &= has ? ReferenceEquals(l.Row, r) && l.Gcar == ++k : l.Row is null && l.Gcar is null;
                    }
                    ok &= k == mine.Count;
                    var dnp = lines.Count(l => l.Row is null);
                    dnps += dnp;
                    if (mine.Count == 0) { zeroLine++; if (dnp == lines.Count && lines.Count > 0) zeroLineFullDnp++; }
                    // The bottom line: his rows summed here, and his team's record counted here.
                    var expect = new CareerLine();
                    foreach (var r in mine.Values) expect.AddGame(r, 0.0);
                    var wins = team.Count(b => (b.Facts.HomeSchoolId == e.SchoolId) == (b.Facts.HomeScore > b.Facts.AwayScore));
                    ok &= g.Totals.G == expect.G && g.Totals.Gs == expect.Gs && g.Totals.Counters.SequenceEqual(expect.Counters)
                          && g.Wins == wins && g.Losses == team.Count - wins
                          && g.TotalSeconds == lines.Where(l => l.Row is not null).Sum(l => CareerGameSeconds(l.Row!, l.Facts));
                    var text = RenderSeasonGameLog(g, "x");
                    var printedDnp = text.Split('\n').Count(t => t.Contains("Did Not Play", StringComparison.Ordinal));
                    dnpPrinted += printedDnp;
                    ok &= printedDnp == dnp && text.Contains("Conference tournament\n", StringComparison.Ordinal) == (firstCt >= 0);
                    if (!ok) wrong.Add($"{who}");
                    if (sample is null && dnp > 0 && mine.Count > 0 && firstCt >= 0) sample = g;
                }
                Check("C3a: ★ for every man of the stock season: his game log is exactly his school's games, in date order, Gtm 1..n, " +
                      "phases contiguous; his played lines are his rows, his DNPs the rest, Gcar on played games only; the bottom line " +
                      "is his rows summed and his team's record",
                      wrong.Count == 0, wrong.Count == 0 ? Inv($"{log.Roster.Count:N0} men, {withTourney:N0} with a conference tournament") : string.Join("; ", wrong.Take(4)));
                Check("C3b: every DNP prints as a Did Not Play line, and a man with no line all season has his team's whole schedule as DNPs",
                      dnpPrinted == dnps && zeroLine > 0 && zeroLineFullDnp == zeroLine,
                      Inv($"{dnps:N0} DNP lines; {zeroLine} men with no line all season, {zeroLineFullDnp} of them full-DNP logs"));
                if (sample is not null)
                {
                    Console.WriteLine(Inv($"  (page) gamelog, {sample.Name} ({sample.School}), first lines:"));
                    foreach (var l in RenderSeasonGameLog(sample, "x").Split('\n').Take(12)) Console.WriteLine("    " + l);
                }

                // Negative controls on a constructed schedule: the page refuses; it never invents an order.
                var school = bySchool.Keys.OrderBy(s => s).First(s => bySchool[s].Any(b => b.Facts.IsConferenceTournamentGame));
                var victim = log.Roster.First(e => e.SchoolId == school).PersonId;
                var ordered = bySchool[school].OrderBy(b => b.Facts.Date!.Value).ToList();
                var reg = ordered.First(b => !b.Facts.IsConferenceTournamentGame);
                var lastDay = ordered[^1].Facts.Date!.Value;
                List<(long, GameLogV1?, string?)> Moved(GameLogBlockV1 moved, DateOnly to)
                    => new() { (1L, log with { Blocks = log.Blocks.Select(b => ReferenceEquals(b, moved) ? b with { Facts = b.Facts with { Date = to } } : b).ToList() }, null) };
                var late = Refusal(() => ReadSeasonGameLog(Moved(reg, lastDay.AddDays(1)), stock, Is(victim), "x", 1));
                Check("C3c: NEGATIVE CONTROL — a regular-season game dated after the conference tournament began is refused by name, not reordered",
                      late is InvalidOperationException && late.Message.Contains("after its conference tournament began", StringComparison.Ordinal), Blame(late));
                var twice = Refusal(() => ReadSeasonGameLog(Moved(ordered[1], ordered[0].Facts.Date!.Value), stock, Is(victim), "x", 1));
                Check("C3d: NEGATIVE CONTROL — a school with two games on one date is refused by name, not given an invented order",
                      twice is InvalidOperationException && twice.Message.Contains("will not invent an order", StringComparison.Ordinal), Blame(twice));
            }

            // ── The three-season mte career C3 (Gcar), C5 and C6 read ───────────────
            var mtePath = Path.Combine(scratch, "mte", "career.json");
            var seedTwo = SeasonTwoSeed(GameLogPageCheckSeed);
            Career(mte, GameLogPageCheckSeed, mtePath);
            Career(mte, seedTwo, mtePath);
            Career(mte, seedTwo + 1, mtePath);
            var mteLogs = new[] { 1L, 2L, 3L }.Select(s => ReadLog(mte, mtePath, s)).ToArray();
            var walk = new[] { 1L, 2L, 3L }.Select((s, i) => (SeasonId: s, Log: (GameLogV1?)mteLogs[i], Reason: (string?)null)).ToList();
            var r1 = mteLogs[0].RosterV2(); var r3 = mteLogs[2].RosterV2();
            var in3 = r3.Select(e => e.PersonId).ToHashSet();
            var spanner = r1.First(e => e.Class <= 1 && in3.Contains(e.PersonId)
                                        && mteLogs.All(l => l.Blocks.Any(b => b.Rows.Any(r => r.PersonId == e.PersonId)))).PersonId;
            PlayerCareer Live(PersonId who)
            {
                using var h = HistoryStore.Open(mtePath, mteFp);
                return ReadPlayerCareer(h, mte, who);
            }
            SeasonGameLog LiveLog(PersonId who, long season)
            {
                using var h = HistoryStore.Open(mtePath, mteFp);
                return ReadSeasonGameLog(ReadCareerLogs(h), mte, Is(who), "x", season);
            }

            // ── C3 (career): Gcar across seasons, and the bottom line is the player page's row ─
            {
                var wrong = new List<string>();
                var everyone = mteLogs.SelectMany(l => l.Roster.Select(e => e.PersonId)).Distinct().ToList();
                foreach (var who in everyone)
                {
                    var page = ReadPlayerCareer(walk, mte, Is(who), "x");
                    long before = 0;
                    foreach (var row in page.Seasons)
                    {
                        var g = ReadSeasonGameLog(walk, mte, Is(who), "x", row.SeasonId);
                        var played = g.Lines.Where(l => l.Row is not null).ToList();
                        var gcarOk = played.Select((l, i) => l.Gcar == before + i + 1).All(x => x);
                        var pageCells = CareerCells(row.All, perGame: false).Skip(1).ToArray();
                        var logCells = CareerCells(g.Totals, perGame: false).Skip(1).ToArray();
                        pageCells[1] = logCells[1] = "";   // minutes print as m:ss on the log; every other cell must match
                        if (!gcarOk || !pageCells.SequenceEqual(logCells)) wrong.Add($"{who} season {row.SeasonId}");
                        before += played.Count;
                    }
                }
                Check("C3e: ★ on a three-season career, for every man and season: Gcar continues from his earlier seasons, and the " +
                      "game log's bottom line is his season row on the player page, cell for cell (minutes aside)",
                      wrong.Count == 0, wrong.Count == 0 ? Inv($"{everyone.Count} men") : string.Join("; ", wrong.Take(4)));

                var s1Played = mteLogs[0].Blocks.Count(b => b.Rows.Any(r => r.PersonId == spanner));
                var s2 = LiveLog(spanner, 2);
                Check("C3f: his season-two log starts its career numbering after his season-one games",
                      s2.Lines.First(l => l.Row is not null).Gcar == s1Played + 1 && s2.GapSeasons.Count == 0,
                      Inv($"season one {s1Played} games; season two's first Gcar {s2.Lines.First(l => l.Row is not null).Gcar}"));

                var s1Path = GameLogWriter.FinalPathFor(mtePath, 1);
                var original = File.ReadAllBytes(s1Path);
                var flipped = (byte[])original.Clone();
                var firstBlock = 128 + 32 + r1.Count * 256 + 8;
                flipped[firstBlock + 56 + (2 + BitConverter.ToInt16(original, firstBlock + 36)) * 8 + 40] ^= 0x01;
                File.WriteAllBytes(s1Path, flipped);
                var unreadable = LiveLog(spanner, 2);
                var unreadableText = RenderSeasonGameLog(unreadable, "x");
                File.Delete(s1Path);
                var missing = LiveLog(spanner, 2);
                var refusedSeason = Refusal(() => LiveLog(spanner, 1));
                File.WriteAllBytes(s1Path, original);
                var back = LiveLog(spanner, 2);
                Check("C3g: ★ with season one unreadable, then missing, his season-two Gcar prints — (never restarted, never inferred) " +
                      "and the page names the season; season one itself is refused by name; restored, the numbers return",
                      unreadable.Lines.Where(l => l.Row is not null).All(l => l.Gcar is null) && unreadable.GapSeasons.SequenceEqual(new[] { 1L })
                      && unreadableText.Contains("Career game numbers are incomplete: no record exists for season 1", StringComparison.Ordinal)
                      && missing.Lines.Where(l => l.Row is not null).All(l => l.Gcar is null) && missing.GapSeasons.SequenceEqual(new[] { 1L })
                      && refusedSeason is InvalidOperationException && refusedSeason.Message.Contains("season 1 has no record", StringComparison.Ordinal)
                      && back.Lines.First(l => l.Row is not null).Gcar == s1Played + 1,
                      Blame(refusedSeason));
            }

            // ── C4: the box score is the game (every stock game) ───────────────────────
            {
                var rosterBySchool = log.Roster.GroupBy(e => e.SchoolId).ToDictionary(g => g.Key, g => g.Select(e => e.PersonId).ToHashSet());
                var wrong = new List<int>();
                string? otSample = null;
                var uncreditedSides = 0; var uncreditedGames = 0; long uncreditedPoints = 0; var linesWrong = 0;
                for (var i = 0; i < log.Blocks.Count; i++)
                {
                    var b = log.Blocks[i];
                    var box = ReadBoxScore(log, stock, 1, i);
                    var ok = box.Visitor.SchoolId == b.Facts.AwaySchoolId && box.Home.SchoolId == b.Facts.HomeSchoolId;
                    foreach (var (team, final) in new[] { (box.Visitor, b.Facts.AwayScore), (box.Home, b.Facts.HomeScore) })
                    {
                        var rows = b.Rows.Where(r => r.SchoolId == team.SchoolId).ToList();
                        var lines = team.Starters.Concat(team.Reserves).ToList();
                        ok &= lines.Count == rows.Count && lines.All(l => rows.Any(r => ReferenceEquals(r, l.Row)))
                              && team.Starters.Count == 5 && team.Starters.All(l => l.Row.Started) && team.Reserves.All(l => !l.Row.Started)
                              && team.Starters.Zip(team.Starters.Skip(1)).All(z => z.First.Seconds >= z.Second.Seconds)
                              && team.Reserves.Zip(team.Reserves.Skip(1)).All(z => z.First.Seconds >= z.Second.Seconds);
                        var played = rows.Select(r => r.PersonId).ToHashSet();
                        ok &= team.DidNotPlay.Select(e => e.PersonId).ToHashSet().SetEquals(rosterBySchool[team.SchoolId].Where(p => !played.Contains(p)))
                              && team.DidNotPlay.Count + rows.Count == rosterBySchool[team.SchoolId].Count;
                        var sums = new long[21];
                        foreach (var r in rows) { var c = CountersOf(r); for (var k = 0; k < 21; k++) sums[k] += c[k]; }
                        ok &= team.Totals.Counters.SequenceEqual(sums) && team.Points == rows.Sum(r => 2 * r.Fgm + r.Tpm + r.Ftm)
                              && team.Uncredited >= 0 && team.Points + team.Uncredited == final;
                        if (team.Uncredited > 0) { uncreditedSides++; uncreditedPoints += team.Uncredited; }
                    }
                    if (box.Visitor.Uncredited > 0 || box.Home.Uncredited > 0) uncreditedGames++;
                    var text = RenderBoxScore(box);
                    var printed = text.Split('\n').Count(l => l.StartsWith("Uncredited: ", StringComparison.Ordinal));
                    if (printed != (box.Visitor.Uncredited > 0 ? 1 : 0) + (box.Home.Uncredited > 0 ? 1 : 0)) linesWrong++;
                    ok &= text.Contains("\nLine score\n", StringComparison.Ordinal) && text.Split('\n').Count(l => l == "Starters") == 2;
                    if (!ok) wrong.Add(i);
                    if (otSample is null && b.Facts.OvertimePeriods > 0) otSample = text;
                }
                Check("C4a: ★ for every stock game: each team's lines are its rows, its starters its five started rows (most minutes first), " +
                      "its DNPs its roster minus its rows, its totals the sum of its lines, its points plus its uncredited points its final",
                      wrong.Count == 0, wrong.Count == 0 ? Inv($"{log.Blocks.Count:N0} games") : string.Join(", ", wrong.Take(6)));
                //  ★ S118 — O-115 shipped: a putback is the rebounder's, its free throws included, so no
                //    stock side holds a point no man is credited with. Before S118 this read 686 games
                //    (713 sides, 1,513 points) and asserted > 0; it is now a wall. The Uncredited line's
                //    code stays — if points ever go uncredited again the page says so (C4c's control).
                Check("C4b: ★ no stock side holds a point no man is credited with, and no Uncredited line prints anywhere " +
                      "(S118: the putback is the rebounder's)",
                      linesWrong == 0 && uncreditedSides == 0 && uncreditedGames == 0,
                      Inv($"{uncreditedGames:N0} games ({uncreditedSides:N0} sides, {uncreditedPoints:N0} points); before S118: 686 games (713 sides, 1,513 points)"));
                var anyBlock = log.Blocks[0];
                var inflated = log with { Blocks = log.Blocks.Select(b => ReferenceEquals(b, anyBlock)
                    ? b with { Facts = b.Facts with { HomeScore = b.Rows.Where(r => r.SchoolId == b.Facts.HomeSchoolId).Sum(r => (int)(2 * r.Fgm + r.Tpm + r.Ftm)) - 1 } }
                    : b).ToList() };
                var over = Refusal(() => ReadBoxScore(inflated, stock, 1, 0));
                Check("C4c: NEGATIVE CONTROL — a game whose men are credited with more than its final is refused by name, never printed",
                      over is InvalidOperationException && over.Message.Contains("more than its final", StringComparison.Ordinal), Blame(over));
                if (otSample is not null)
                {
                    Console.WriteLine("  (page) box score of the first overtime game, top:");
                    foreach (var l in otSample.Split('\n').Take(16)) Console.WriteLine("    " + l);
                }
            }

            // ── C5: career highs (every man of the three-season career) ──────────────
            {
                var wrong = new List<string>();
                var zeroShown = 0; var ties = 0; var mpTies = 0;
                var everyone = mteLogs.SelectMany(l => l.Roster.Select(e => e.PersonId)).Distinct().ToList();
                foreach (var who in everyone)
                {
                    var page = ReadPlayerCareer(walk, mte, Is(who), "x");
                    var mine = walk.SelectMany(w => w.Log!.Blocks.SelectMany(b => b.Rows.Where(r => r.PersonId == who)
                                   .Select(r => (Season: w.SeasonId, b.Facts, Row: r)))).ToList();
                    // Independent recomputation: decimal arithmetic for minutes, plain sums for the rest.
                    long Seconds(GameBlockFactsV1 f, PerGameStatRowV1 r)
                        => (long)Math.Round((decimal)r.Credits * (40 + 5 * f.OvertimePeriods) * 60 / f.PossessionCount, MidpointRounding.AwayFromZero);
                    var expect = new (string, Func<(long Season, GameBlockFactsV1 Facts, PerGameStatRowV1 Row), long>)[]
                    {
                        ("PTS", x => 2 * x.Row.Fgm + x.Row.Tpm + x.Row.Ftm), ("TRB", x => x.Row.OReb + x.Row.DReb),
                        ("AST", x => x.Row.Ast), ("STL", x => x.Row.Stl), ("BLK", x => x.Row.Blk), ("3P", x => x.Row.Tpm),
                        ("FG", x => x.Row.Fgm), ("FT", x => x.Row.Ftm), ("MP", x => Seconds(x.Facts, x.Row)),
                    };
                    var highs = CareerHighsOf(page);
                    var text = RenderPlayerPage(page, "x");
                    var ok = highs.Count == 9;
                    foreach (var ((label, value), h) in expect.Zip(highs))
                    {
                        var best = mine.Count == 0 ? 0 : mine.Max(value);
                        var at = best == 0 ? new HashSet<(long, int)>() : mine.Where(x => value(x) == best).Select(x => (x.Season, x.Facts.FixtureOrdinal)).ToHashSet();
                        ok &= h.Label == label && h.Value == best && h.Games.Count == at.Count
                              && h.Games.Select(g => (g.SeasonId, g.Facts.FixtureOrdinal)).ToHashSet().SetEquals(at);
                        if (best == 0) { ok &= text.Contains("\n" + label.PadRight(4) + " —\n", StringComparison.Ordinal); zeroShown++; }
                        if (h.Games.Count > 1) { ties++; if (label == "MP") mpTies++; }
                    }
                    ok &= text.Contains("\nSingle-game highs\n", StringComparison.Ordinal) && !text.Contains("Single-game highs*", StringComparison.Ordinal);
                    if (!ok) wrong.Add($"{who}");
                }
                Check("C5a: ★ for every man of a three-season career: each of the nine highs is the maximum over his games (recomputed " +
                      "here; minutes in decimal arithmetic, compared as printed), the count is how many games reach it, the listed games exactly those",
                      wrong.Count == 0, wrong.Count == 0 ? Inv($"{everyone.Count} men; {ties} tied highs, {mpTies} of them minutes") : string.Join("; ", wrong.Take(4)));
                Check("C5b: a high of 0 prints — and lists nothing", zeroShown > 0, Inv($"{zeroShown} zero highs shown as —"));

                // A listed game opens the box score that holds the high.
                var sp = ReadPlayerCareer(walk, mte, Is(spanner), "x");
                var pts = CareerHighsOf(sp)[0];
                var g0 = pts.Games[0];
                var box = ReadBoxScore(mteLogs[g0.SeasonId - 1], mte, g0.SeasonId, g0.Facts.FixtureOrdinal);
                var inBox = box.Visitor.Starters.Concat(box.Visitor.Reserves).Concat(box.Home.Starters).Concat(box.Home.Reserves)
                               .Single(l => l.Man.PersonId == spanner);
                Check("C5c: a high's listed reference opens the box score of that game, and his line there is the high",
                      2 * inBox.Row.Fgm + inBox.Row.Tpm + inBox.Row.Ftm == pts.Value,
                      Inv($"{sp.Name}: {pts.Value} points, season {g0.SeasonId} game {g0.Facts.FixtureOrdinal}"));

                var s2Path = GameLogWriter.FinalPathFor(mtePath, 2);
                var original = File.ReadAllBytes(s2Path);
                var flipped = (byte[])original.Clone();
                var firstBlock = 128 + 32 + mteLogs[1].Roster.Count * 256 + 8;
                flipped[firstBlock + 56 + (2 + BitConverter.ToInt16(original, firstBlock + 36)) * 8 + 40] ^= 0x01;
                File.WriteAllBytes(s2Path, flipped);
                var starred = RenderPlayerPage(Live(spanner), "x");
                File.WriteAllBytes(s2Path, original);
                var whole = RenderPlayerPage(Live(spanner), "x");
                Check("C5d: ★ with season two unreadable the section is titled \"Single-game highs*\" and names the season",
                      starred.Contains("\nSingle-game highs*\n", StringComparison.Ordinal)
                      && starred.Contains("* Highs over the seasons on record only; no record exists for season 2.", StringComparison.Ordinal));
                Check("C5e: NEGATIVE CONTROL — with every season on record the same man's highs carry no star",
                      whole.Contains("\nSingle-game highs\n", StringComparison.Ordinal) && !whole.Contains("Single-game highs*", StringComparison.Ordinal));
                Console.WriteLine(Inv($"  (page) player {sp.Name}, single-game highs:"));
                foreach (var l in whole.Split('\n').SkipWhile(l => !l.StartsWith("Single-game highs", StringComparison.Ordinal)).Take(14))
                    Console.WriteLine("    " + l);
            }

            // ── C6: the commands refuse by name ─────────────────────────────────────
            {
                var world = WorldPath("fixture-mte.world.json");
                var number = Enumerable.Range(1, (int)PeekState(mtePath).NextPersonId - 1).Select(n => (long)n)
                    .First(n => mteLogs[0].PersonNumbered(n) == spanner).ToString(CultureInfo.InvariantCulture);
                var freshman = r3.First(e => e.Class == 0).PersonId;
                var freshNumber = Enumerable.Range(1, (int)PeekState(mtePath).NextPersonId - 1).Select(n => (long)n)
                    .First(n => mteLogs[2].PersonNumbered(n) == freshman).ToString(CultureInfo.InvariantCulture);
                var gl = Capture(() => RunGameLog(new[] { "gamelog", world, number, "2", "--history", mtePath }));
                var bx = Capture(() => RunBox(new[] { "box", world, "2", "0", "--history", mtePath }));
                Check("C6a: `gamelog` and `box` print their pages for a real man and a real game (the control the refusals are measured against)",
                      gl.StartsWith("Game log — ", StringComparison.Ordinal) && bx.StartsWith("Box score — season 2, game 0", StringComparison.Ordinal));
                var noHistLog = Capture(() => RunGameLog(new[] { "gamelog", world, number, "1" }));
                var noHistBox = Capture(() => RunBox(new[] { "box", world, "1", "0" }));
                Check("C6b: both refuse without --history, by name",
                      noHistLog.Contains("--history <career.json> is required", StringComparison.Ordinal)
                      && noHistBox.Contains("--history <career.json> is required", StringComparison.Ordinal));
                var ghost = Path.Combine(scratch, "nobody", "career.json");
                var ghostLog = Capture(() => RunGameLog(new[] { "gamelog", world, number, "1", "--history", ghost }));
                var ghostBox = Capture(() => RunBox(new[] { "box", world, "1", "0", "--history", ghost }));
                Check("C6c: a missing career is refused by both and nothing is created",
                      ghostLog.Contains("there is no career at", StringComparison.Ordinal) && ghostBox.Contains("there is no career at", StringComparison.Ordinal)
                      && !Directory.Exists(Path.GetDirectoryName(ghost)));
                var notOn = Capture(() => RunGameLog(new[] { "gamelog", world, freshNumber, "1", "--history", mtePath }));
                Check("C6d: a season he was not on a roster for is refused by name (a season-three freshman, season one)",
                      notOn.StartsWith($"GAMELOG ERROR: number {freshNumber} was not on a roster in season 1", StringComparison.Ordinal), notOn.Trim());
                var noSeason = Capture(() => RunGameLog(new[] { "gamelog", world, number, "4", "--history", mtePath }));
                var noSeasonBox = Capture(() => RunBox(new[] { "box", world, "4", "0", "--history", mtePath }));
                Check("C6e: a season the career does not have is refused by name, by both",
                      noSeason.Contains("there is no season 4", StringComparison.Ordinal) && noSeasonBox.Contains("there is no season 4", StringComparison.Ordinal),
                      noSeason.Trim());
                var badRef = mteLogs[1].Blocks.Count.ToString(CultureInfo.InvariantCulture);
                var unknownRef = Capture(() => RunBox(new[] { "box", world, "2", badRef, "--history", mtePath }));
                Check("C6f: an unknown reference is refused by name (one past the season's last game)",
                      unknownRef.StartsWith($"BOX ERROR: season 2 has no game {badRef}", StringComparison.Ordinal), unknownRef.Trim());
            }

            // ── C7: the fingerprint wall — the stock legacy season ────────────────
            {
                Check("C7a: ★ the career's season one plays the S120 capture game for game (the score by period moved no game)",
                      GamesDigest(stockOne) == RatingGoldenPreS112GameDigest, $"{stockOne.PlayedGames.Count} games");
                var legacy = RunSeasonCore(stock, GameLogPageCheckSeed, configPath, verbose: false);
                var prefix = legacy.ConferenceGameCount + legacy.TournamentGameCount;
                var resultsFp = SeasonFingerprint(legacy.Results.Take(prefix).ToList(), legacy.PossessionCounts.Take(prefix).ToList());
                Check("C7b: #1 conference schedule UNMOVED", legacy.Fingerprint == MatchGoldenConferenceFp);
                Check("C7c: #2 conference dated UNMOVED", legacy.DatedFingerprint == MatchGoldenDatedFp);
                Check("C7d: #3 event games UNMOVED", legacy.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C7e: #4 results+possessions UNMOVED over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C7f: #5 non-conference dated UNMOVED", legacy.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C7g: #6 conference tournaments UNMOVED", legacy.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C7h: #7 buy games UNMOVED", legacy.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                Check("C7i: ★ every legacy game identical to the S120 capture", GamesDigest(legacy) == RatingGoldenPreS112GameDigest);
                Check("C7j: the career's season one and the legacy season are the same games", GamesDigest(stockOne) == GamesDigest(legacy));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 107 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 107 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }

    /// <summary>Rewrite a finalized block-schema-3 log into the S116 layout: the header's block
    /// version moves to 2 and every block drops its period list (the 56-byte header and the rows are
    /// kept), with every block checksum and the footer's payload digest recomputed. The ONLY way a
    /// block-schema-2 log can exist in the suite now that the writer emits block schema 3 — built so
    /// C2 proves a genuine S116 file is refused, not merely a damaged one.</summary>
    private static byte[] DowngradeToBlockSchema2(byte[] file)
    {
        const int header = 128, rosterHeader = 32, trailer = 8, footer = 64, blockHeader = 56, row = 196;
        var entrySize = BitConverter.ToInt16(file, header + 6);
        var count = BitConverter.ToInt32(file, header + 8);
        var rosterEnd = header + rosterHeader + count * entrySize + trailer;
        var head = file.AsSpan(0, rosterEnd).ToArray();
        BitConverter.GetBytes((short)2).CopyTo(head, 10);                 // block schema version
        var output = new List<byte>(head);
        var p = rosterEnd;
        while (p < file.Length - footer)
        {
            var rows = BitConverter.ToUInt16(file, p + 26);
            var periods = (2 + BitConverter.ToInt16(file, p + 36)) * 8;
            var block = file.AsSpan(p, blockHeader).ToArray()
                .Concat(file.AsSpan(p + blockHeader + periods, rows * row).ToArray()).ToArray();
            output.AddRange(block);
            output.AddRange(SHA256.HashData(block)[..8]);
            p += blockHeader + periods + rows * row + trailer;
        }
        output.AddRange(file.AsSpan(file.Length - footer, footer).ToArray());
        var result = output.ToArray();
        SHA256.HashData(result.AsSpan(header, result.Length - footer - header)).CopyTo(result, result.Length - footer + 16);
        return result;
    }
}
