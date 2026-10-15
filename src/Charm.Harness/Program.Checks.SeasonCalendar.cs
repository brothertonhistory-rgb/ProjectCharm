using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 113 — S122: THE SEASON ON THE CALENDAR.
//
//  What must be proven:
//    C1  the season is played on the calendar — every game once, dates never going
//        backwards along the play order (ties by ordinal), every game inside November 1
//        .. the championship Monday, no school twice on one day; on the stock career's
//        two seasons and a fixture-mte career. Control: the old order (ordinals
//        ascending), and the old order actually played, both go backwards.
//    C2  ★ nothing moved — pinned from the S121 build: season one's game blocks (every
//        game, every stat line, decoded — integers only), its event record and the career
//        file after two seasons; and on THIS machine, the same two-season career played
//        on the calendar and in the old order writes every file byte for byte, logs and
//        scouting files included. Controls: one changed score moves the digest; one
//        flipped byte fails the file comparison.
//    C3  the day the calendar says — every day's games are exactly the games dated that
//        day, in ordinal order. Control: the old order's first day is not the first
//        day's slate.
//    C4  later rounds wait, tournaments seed on their day — every bracket and conference
//        tournament game dated and played after both feeders; every conference
//        tournament seeded after its league's last game, from its own league's games,
//        onto the field the national record gives. Controls: a tournament opening before
//        its league finished is refused before any of its games plays; a later round on
//        its feeder's own day is refused before anything plays.
//    C5  the day page — six days print exactly what an independent read of the log says;
//        a July date belongs to the season opening that year; five refusals by name.
//    C6  every path plays by day — the legacy season, the stacked season two, a career
//        season — and the legacy season is the career's season one and the S120 capture.
//    C7  page-only: days with games, the busiest day, games per month, the first and
//        last game day, the longest stretch without a game.
//
//  ★ WHY C2 PINS DECODED BLOCKS AND NOT FILE BYTES (CONVENTIONS §2, S81.3). The roster
//  section and the scouting file carry raw double bits (scout rank, defensive plane,
//  hidden progress), and a Linux-captured raw-bits fixture may not be compared bit for
//  bit on Windows. So the cross-platform pin covers what is integers by construction —
//  the game blocks, the event record, the career file — and the full-file byte identity
//  is proven on the machine that runs it, against the old order played by the same build.
// ============================================================================

internal static partial class Program
{
    private const long CalendarCheckSeed = 20260720;
    private const string CalendarFixedHistoryId = "c122c122c122c122c122c122c122c122";

    /// <summary>★ CAPTURED FROM THE S121 BUILD (the gate's read-only probe, before any S122 code
    /// existed): the stock career's season one under the fixed history id, every game block
    /// decoded through <c>CalendarBlocksDigest</c>.</summary>
    private const string CalendarGoldenSeasonOneBlocks =
        "d7c990883a316f48256e5a48fac5fcc7bae708cbe261f8cffedc18e1f89ca256";

    /// <summary>★ CAPTURED FROM THE S121 BUILD: season one's event record (career.events/season-1.json),
    /// raw bytes — integers and strings only, LF newlines by construction.</summary>
    private const string CalendarGoldenSeasonOneEvents =
        "85b6acf3a35f9be567f3d98c358fb46f66439031f0896d87379a3bb0c03338a0";

    /// <summary>★ CAPTURED FROM THE S125 BUILD (re-pinned from the S121 build's v3 bytes, 2045cb6c…,
    /// when the career file became v4): the career file after two seasons, as its PORTABLE digest
    /// (<see cref="PortableCareerDigest"/>) — the next person, season and game numbers every game of both
    /// seasons spent, every file the two seasons wrote listed (the integer-only ones by hash), and both
    /// seasons' rules under the fixed-rules seam. Every season file it lists is still the S121 build's
    /// (C2a, C2b).</summary>
    private const string CalendarGoldenCareerFile =
        "29a106515979788c4bf17d2e6c32012a4e40ecca72578e20e718554dac8e7309";

    /// <summary>Every game block of a log — the facts and every man's stat line — as text, hashed.
    /// Integers, dates and flags only, so the same games hash the same on any machine.</summary>
    private static string CalendarBlocksDigest(GameLogV1 log)
    {
        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        foreach (var b in log.Blocks)
        {
            var f = b.Facts;
            sb.Append(I(f.FixtureOrdinal)).Append('|').Append(f.GameId.ToString()).Append('|')
              .Append(I(f.HomeSchoolId)).Append('|').Append(I(f.AwaySchoolId)).Append('|')
              .Append(f.IsConferenceGame ? 'L' : f.IsConferenceTournamentGame ? 'T' : 'N').Append('|')
              .Append(I(f.HomeScore)).Append('|').Append(I(f.AwayScore)).Append('|')
              .Append(I(f.OvertimePeriods)).Append('|').Append(I(f.PossessionCount)).Append('|')
              .Append(f.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-").Append('|')
              .Append(f.HasHost is null ? "-" : f.HasHost.Value ? "H" : "N").Append('|')
              .Append(string.Join(',', (f.Periods ?? Array.Empty<PeriodScoreV1>()).Select(p => I(p.Home) + "-" + I(p.Away))))
              .Append('\n');
            foreach (var r in b.Rows)
                sb.Append("  ").Append(r.PersonId.ToString()).Append('|').Append(I(r.SchoolId)).Append('|')
                  .Append(I(r.PoolId)).Append('|').Append(I(r.AcquisitionIndex)).Append('|')
                  .Append(string.Join(',', new[] { r.Credits, r.OffensiveCredits, r.Fga, r.Fgm, r.Tpa, r.Tpm, r.Fta, r.Ftm,
                      r.OReb, r.DReb, r.Ast, r.Stl, r.Blk, r.To, r.ShFoul, r.NsFoul, r.OffFoul, r.FbBlk,
                      r.OpponentTwoPaOnFloor, r.SecuredBoardsOnFloor, r.OffensiveTeamFgmOnFloor }.Select(I)))
                  .Append('|').Append(r.Started ? '1' : '0').Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>Two finished career folders, every file byte for byte: null when they match, else the
    /// first difference by name. ★ S123 — lifted from Phase 113 so Phase 114 uses the same copy. Read
    /// with the ordinary file read, so only for folders no live writer holds.</summary>
    private static string? CalendarFilesDiffer(string dirA, string dirB)
    {
        var a = Directory.GetFiles(dirA, "*", SearchOption.AllDirectories)
                         .Select(f => Path.GetRelativePath(dirA, f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var b = Directory.GetFiles(dirB, "*", SearchOption.AllDirectories)
                         .Select(f => Path.GetRelativePath(dirB, f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (!a.SequenceEqual(b)) return $"file lists differ: [{string.Join(", ", a)}] vs [{string.Join(", ", b)}]";
        foreach (var rel in a)
            if (!File.ReadAllBytes(Path.Combine(dirA, rel)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dirB, rel))))
                return $"{rel} differs";
        return null;
    }

    /// <summary>C1's predicate, on any order: every game once, dates never backwards (ties by
    /// ordinal), inside the span, no school twice on a day. Null when it holds; otherwise the
    /// first thing that does not.</summary>
    private static string? CalendarOrderFault(
        IReadOnlyList<PlayedSeasonGame> games, IReadOnlyList<int> order, DateOnly first, DateOnly last)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        if (order.Count != games.Count || order.Distinct().Count() != games.Count
            || order.Any(g => g < 0 || g >= games.Count))
            return Inv($"the order holds {order.Count} entries ({order.Distinct().Count()} distinct) for {games.Count} games");
        for (var i = 0; i < order.Count; i++)
        {
            var g = games[order[i]];
            if (g.FixtureOrdinal != order[i]) return Inv($"played game {i} is ordinal {g.FixtureOrdinal}, listed as {order[i]}");
            if (g.Game.Date is not { } d) return Inv($"ordinal {order[i]} has no date");
            if (d < first || d > last) return Inv($"ordinal {order[i]} on {d:yyyy-MM-dd} is outside {first:yyyy-MM-dd}..{last:yyyy-MM-dd}");
            if (i > 0)
            {
                var prev = games[order[i - 1]];
                var pd = prev.Game.Date!.Value;
                if (d < pd) return Inv($"ordinal {order[i]} ({d:yyyy-MM-dd}) is played after ordinal {order[i - 1]} ({pd:yyyy-MM-dd})");
                if (d == pd && order[i] < order[i - 1]) return Inv($"on {d:yyyy-MM-dd} ordinal {order[i]} is played after ordinal {order[i - 1]}");
            }
        }
        foreach (var day in games.GroupBy(g => g.Game.Date!.Value))
        {
            var seen = new HashSet<int>();
            foreach (var g in day)
                if (!seen.Add(g.Game.HomeId) || !seen.Add(g.Game.AwayId))
                    return Inv($"a school plays twice on {day.Key:yyyy-MM-dd}");
        }
        return null;
    }

    private static bool Phase113CalendarCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 113 — S122: the season on the calendar. Every game played on its day from November 1 to the " +
                          "championship, each day's games in ordinal order; nothing moved (the S121 capture, and the old order " +
                          "byte for byte); later rounds wait; tournaments seed the morning they open; the day page ==");
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
        static string? Refusal(Action a)
        {
            try { a(); return null; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }
        static (int Code, string Text) Run(Func<int> command)
        {
            var old = Console.Out;
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Console.SetOut(sw);
            int code;
            try { code = command(); } finally { Console.SetOut(old); }
            return (code, sw.ToString());
        }
        static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s122-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stockPath = WorldPath("stock-d1.world.json");
            var stock = LoadWorld(stockPath);
            var mte = LoadWorld(WorldPath("fixture-mte.world.json"));
            var seedTwo = SeasonTwoSeed(CalendarCheckSeed);

            //  The two-season stock career, under the fixed history id, played in a given order.
            (SeasonRunOutcome One, SeasonRunOutcome Two, string Path) TwoSeasons(string folder, SeasonPlayOrder order)
            {
                var path = Path.Combine(scratch, folder, "career.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fixedId = HistoryStore.UseFixedHistoryIdForTests(CalendarFixedHistoryId);
                // ★ S125 — the career file now records each season's rules, whose build stamp changes with
                //   every build; a career file pinned across sessions pins fixed rules instead.
                using var fixedRules = HistoryStore.UseFixedRulesForTests(CheckFixedConfigSha, CheckFixedRulesStamp);
                SeasonRunOutcome Season(long seed)
                {
                    using var store = OpenCareer(path, stock);
                    return RunSeasonCore(stock, seed, configPath, verbose: false, store, retainGameLog: true,
                                         playOrderForTest: order);
                }
                var one = Season(CalendarCheckSeed);
                var two = Season(seedTwo);
                return (one, two, path);
            }
            GameLogV1 ReadLog(string path, long season)
            {
                using var store = OpenCareer(path, stock);
                return GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(path, season),
                    new GameLogBindings(store.HistoryId, store.WorldFingerprint, season));
            }

            var cal = TwoSeasons("calendar", SeasonPlayOrder.Calendar);
            var old = TwoSeasons("fixture-order", SeasonPlayOrder.FixtureOrdinalForTest);
            var one = cal.One;
            var logOne = ReadLog(cal.Path, 1);

            // ════════════════════════════════════════════════════════════════════
            //  C1 — THE SEASON IS PLAYED ON THE CALENDAR.
            // ════════════════════════════════════════════════════════════════════
            {
                string? FaultOf(SeasonRunOutcome r) => CalendarOrderFault(r.PlayedGames, r.PlayOrder, r.FirstLegalDay, r.LastLegalDay);
                var f1 = FaultOf(one);
                Check("C1a: ★ stock season one — every game played exactly once, dates never going backwards (ties by " +
                      "ordinal), every game inside November 1 .. the championship Monday, no school twice on one day",
                      f1 is null, f1 ?? Inv($"{one.PlayOrder.Count:N0} games, {one.FirstLegalDay:yyyy-MM-dd}..{one.LastLegalDay:yyyy-MM-dd}"));
                var f2 = FaultOf(cal.Two);
                Check("C1b: stock season two (a career's second year, new rosters, 2027-28) — the same",
                      f2 is null, f2 ?? Inv($"{cal.Two.PlayOrder.Count:N0} games, {cal.Two.FirstLegalDay:yyyy-MM-dd}..{cal.Two.LastLegalDay:yyyy-MM-dd}"));
                var mteRun = Career(mte, CalendarCheckSeed, Path.Combine(scratch, "mte", "career.json"));
                var f3 = FaultOf(mteRun);
                Check("C1c: a fixture-mte career season — the same, brackets and showcases included",
                      f3 is null && mteRun.TournamentGameCount > 0,
                      f3 ?? Inv($"{mteRun.PlayOrder.Count} games, {mteRun.TournamentGameCount} event games"));

                var ascending = Enumerable.Range(0, one.PlayedGames.Count).ToList();
                var fOld = CalendarOrderFault(one.PlayedGames, ascending, one.FirstLegalDay, one.LastLegalDay);
                Check("C1d: NEGATIVE CONTROL — the old order (ordinals ascending) FAILS: dates go backwards, the November " +
                      "games after March",
                      fOld is not null && fOld.Contains("is played after", StringComparison.Ordinal), fOld ?? "NO FAULT");
                var fOldPlayed = CalendarOrderFault(old.One.PlayedGames, old.One.PlayOrder, old.One.FirstLegalDay, old.One.LastLegalDay);
                Check("C1e: NEGATIVE CONTROL — and the old order ACTUALLY PLAYED (the same build, fixture order) fails the same test",
                      fOldPlayed is not null && old.One.PlayOrder.SequenceEqual(ascending), fOldPlayed ?? "NO FAULT");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — ★ NOTHING MOVED.
            // ════════════════════════════════════════════════════════════════════
            {
                var digestOne = CalendarBlocksDigest(logOne);
                Check("C2a: ★ season one's every game block — teams, date, site, score by period, possessions, every man's " +
                      "stat line — is the S121 build's, decoded (captured from the S121 build)",
                      digestOne == CalendarGoldenSeasonOneBlocks, Inv($"{logOne.Blocks.Count:N0} blocks, {digestOne[..16]}"));
                var eventsOne = Path.Combine(Path.GetDirectoryName(cal.Path)!, "career.events", "season-1.json");
                var eventsSha = File.Exists(eventsOne) ? Sha(File.ReadAllBytes(eventsOne)) : "ABSENT";
                Check("C2b: season one's event record, byte for byte, is the S121 build's — every bracket finish and " +
                      "showcase result where it was", eventsSha == CalendarGoldenSeasonOneEvents, eventsSha[..Math.Min(16, eventsSha.Length)]);
                var careerSha = PortableCareerDigest(File.ReadAllBytes(cal.Path));
                Check("C2c: the career file after two seasons is the S125 capture — the same person, season and game numbers " +
                      "spent, every file listed, the integer-only ones by hash (★ S125: v4, re-pinned from the S121 build's v3; " +
                      "the portable digest, since the log and scouting files hold machine-dependent decimals)",
                      careerSha == CalendarGoldenCareerFile, careerSha);

                var diff = CalendarFilesDiffer(Path.GetDirectoryName(cal.Path)!, Path.GetDirectoryName(old.Path)!);
                Check("C2d: ★ ON THIS MACHINE — the same two-season career played on the calendar and in the old order " +
                      "writes EVERY file byte for byte: both season logs (roster sections included), both scouting files, " +
                      "both event records, the career file",
                      diff is null,
                      diff ?? Inv($"{Directory.GetFiles(Path.GetDirectoryName(cal.Path)!, "*", SearchOption.AllDirectories).Length} files"));
                Check("C2e: and both seasons' games are the old order's game for game (results and possessions)",
                      GamesDigest(one) == GamesDigest(old.One) && GamesDigest(cal.Two) == GamesDigest(old.Two));

                //  Controls: the comparisons can see one game and one byte.
                var b0 = logOne.Blocks[0];
                var nudged = logOne with
                {
                    Blocks = logOne.Blocks.Select((b, i) => i == 0 ? b with { Facts = b.Facts with { HomeScore = b.Facts.HomeScore + 1 } } : b).ToList(),
                };
                Check("C2f: NEGATIVE CONTROL — one game's score changed by one point moves the block digest (it can see a " +
                      "single game; this does not show the change did anything)",
                      CalendarBlocksDigest(nudged) != digestOne, Inv($"ordinal {b0.Facts.FixtureOrdinal}"));
                var copy = Path.Combine(scratch, "fixture-order-flipped");
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(old.Path)!, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(copy, Path.GetRelativePath(Path.GetDirectoryName(old.Path)!, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(f, to);
                }
                var scout = Path.Combine(copy, "career.gamelog", "season-2.scout");
                var bytes = File.ReadAllBytes(scout);
                bytes[bytes.Length / 2] ^= 0x01;
                File.WriteAllBytes(scout, bytes);
                var flipped = CalendarFilesDiffer(Path.GetDirectoryName(cal.Path)!, copy);
                Check("C2g: NEGATIVE CONTROL — one flipped byte in season two's scouting file fails the file comparison",
                      flipped is not null && flipped.Contains("season-2.scout", StringComparison.Ordinal), flipped ?? "NO DIFFERENCE");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — THE DAY THE CALENDAR SAYS.
            // ════════════════════════════════════════════════════════════════════
            {
                var dated = one.PlayedGames.GroupBy(p => p.Game.Date!.Value)
                               .ToDictionary(g => g.Key, g => g.Select(p => p.FixtureOrdinal).OrderBy(x => x).ToList());
                var playedOn = new Dictionary<DateOnly, List<int>>();
                foreach (var g in one.PlayOrder)
                {
                    var d = one.PlayedGames[g].Game.Date!.Value;
                    if (!playedOn.TryGetValue(d, out var l)) playedOn[d] = l = new List<int>();
                    l.Add(g);
                }
                var days = 0;
                var mismatch = (DateOnly?)null;
                for (var d = one.FirstLegalDay; d <= one.LastLegalDay; d = d.AddDays(1))
                {
                    days++;
                    var want = dated.TryGetValue(d, out var w) ? w : new List<int>();
                    var got = playedOn.TryGetValue(d, out var p) ? p : new List<int>();
                    if (!want.SequenceEqual(got)) { mismatch = d; break; }
                }
                Check("C3a: ★ for every day of the stock season, the games played that day are EXACTLY the games dated " +
                      "that day, in ordinal order",
                      mismatch is null && playedOn.Count == dated.Count,
                      mismatch is { } m ? Inv($"first mismatch {m:yyyy-MM-dd}") : Inv($"{days} days, {dated.Count} with games"));
                var firstDay = dated.Keys.Min();
                var oldFirst = Enumerable.Range(0, dated[firstDay].Count).ToList();
                Check("C3b: NEGATIVE CONTROL — a day built from the old play order (its first N games) is NOT the first " +
                      "day's slate",
                      !oldFirst.SequenceEqual(dated[firstDay]),
                      Inv($"{firstDay:yyyy-MM-dd}: {dated[firstDay].Count} games, first ordinal {dated[firstDay][0]}"));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — LATER ROUNDS WAIT; TOURNAMENTS SEED ON THEIR DAY.
            // ════════════════════════════════════════════════════════════════════
            {
                var position = new int[one.PlayOrder.Count];
                for (var i = 0; i < one.PlayOrder.Count; i++) position[one.PlayOrder[i]] = i;
                DateOnly DateOf(PlayedSeasonGame p) => p.Game.Date!.Value;

                var fedChecked = 0;
                string? fedFault = null;
                foreach (var ev in one.PlayedGames.Where(p => p.IsEventGame).GroupBy(p => p.EventId!.Value))
                {
                    var games = ev.OrderBy(p => p.BracketGameIndex).ToList();
                    if (games.Count is not (12 or 4)) continue;   // a showcase: two stand-alone games, no feeders
                    var routes = BracketRoutesFor(games.Count == 12 ? 8 : 4);
                    foreach (var r in routes)
                        foreach (var dest in new[] { r.WinnerToGame, r.LoserToGame }.Where(x => x >= 0))
                        {
                            var feeder = games[r.GameIndex];
                            var later = games[dest];
                            fedChecked++;
                            if (DateOf(later) <= DateOf(feeder) || position[later.FixtureOrdinal] <= position[feeder.FixtureOrdinal])
                                fedFault ??= Inv($"event {ev.Key} game {dest} vs feeder {r.GameIndex}");
                        }
                }
                var shape = BuildKnockoutBracket(ConfTourneyFieldSize, Enumerable.Range(1, ConfTourneyFieldSize).ToList());
                foreach (var ct in one.PlayedGames.Where(p => p.IsConferenceTournamentGame).GroupBy(p => p.ConferenceTournamentId!.Value))
                {
                    var games = ct.OrderBy(p => p.ConfTourneyGameIndex).ToList();
                    foreach (var g in shape.Games.Where(g => g.WinnerTo is not null))
                    {
                        var feeder = games[g.GameIndex];
                        var later = games[g.WinnerTo!.Value];
                        fedChecked++;
                        if (DateOf(later) <= DateOf(feeder) || position[later.FixtureOrdinal] <= position[feeder.FixtureOrdinal])
                            fedFault ??= Inv($"conference {ct.Key} game {g.WinnerTo} vs feeder {g.GameIndex}");
                    }
                }
                Check("C4a: ★ every bracket and conference-tournament game is dated, and played, strictly after BOTH its feeders",
                      fedFault is null && fedChecked > 0, fedFault ?? Inv($"{fedChecked} feeder edges"));

                var confOf = stock.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
                var leagueLast = new Dictionary<int, (int Position, DateOnly Date)>();
                for (var g = 0; g < one.ConferenceGameCount; g++)
                {
                    var cid = confOf[one.PlayedGames[g].Game.HomeId];
                    var here = (position[g], DateOf(one.PlayedGames[g]));
                    if (!leagueLast.TryGetValue(cid, out var cur) || here.Item1 > cur.Position) leagueLast[cid] = here;
                }
                string? seedFault = null;
                var leagues = 0;
                foreach (var ct in one.PlayedGames.Where(p => p.IsConferenceTournamentGame).GroupBy(p => p.ConferenceTournamentId!.Value))
                {
                    leagues++;
                    var firstPos = ct.Min(p => position[p.FixtureOrdinal]);
                    var firstDate = ct.Min(DateOf);
                    if (!leagueLast.TryGetValue(ct.Key, out var last) || firstPos <= last.Position || firstDate <= last.Date)
                        seedFault ??= Inv($"conference {ct.Key}");
                }
                Check("C4b: ★ every conference tournament's first game is played after its league's LAST game, on a later day",
                      seedFault is null && leagues == one.ConferenceTournaments.LeaguesPlayed && leagues > 0,
                      seedFault ?? Inv($"{leagues} leagues"));

                //  The field the morning it opened, against the field the whole finished season gives.
                var national = ConfTourneyConferenceRecords(stock, one.Schedule, one.Results);
                var membersOf = stock.Schools.GroupBy(s => s.ConferenceId).ToDictionary(g => g.Key, g => g.Select(s => s.Id).OrderBy(x => x).ToList());
                var fixturesOf = one.Schedule.Select((sg, i) => (i, sg)).GroupBy(t => confOf[t.sg.HomeId])
                                    .ToDictionary(g => g.Key, g => g.ToList());
                var nullable = one.Results.Select(r => (SeasonGameResult?)r).ToList();
                var recordsAgree = true;
                var fieldsAgree = true;
                foreach (var champ in one.ConferenceTournaments.Champions)
                {
                    var members = membersOf[champ.ConferenceId];
                    var league = ConfTourneyLeagueRecords(champ.ConferenceId, members, fixturesOf[champ.ConferenceId], nullable,
                                                          one.LastLegalDay);
                    if (members.Any(m => league[m] != national[m])) recordsAgree = false;
                    var byNational = members.ToList();
                    byNational.Sort(ConfTourneySeedOrder(national));
                    if (!byNational.Take(ConfTourneyFieldSize).SequenceEqual(champ.SchoolBySeed)) fieldsAgree = false;
                }
                Check("C4c: each league's own-games record equals the national record for every member, in every league",
                      recordsAgree, Inv($"{one.ConferenceTournaments.Champions.Count} leagues"));
                Check("C4d: ★ every field was seated, seed for seed, exactly as the national record seats it — the field the " +
                      "S121 build seeded (its fingerprint is pinned by the wall: Phases 99, 102)", fieldsAgree);

                //  ── Controls, on constructed sources through the real day loop, with a stand-in for the engine. ──
                //  A league of eight playing one game a day from November 2, and its tournament.
                var members8 = Enumerable.Range(1, 8).ToList();
                var pairs = new List<(int, int)>();
                for (var a = 1; a <= 8; a++) for (var b = a + 1; b <= 8; b++) pairs.Add((a, b));
                var place = members8.ToDictionary(id => id, id => 100 + id);
                var noIds = new Dictionary<ConfTourneySlotKey, GameId>();
                (string? Refused, int LeaguePlayed, int TourneyPlayed) Synthetic(DateOnly opens)
                {
                    var league = pairs.Select((p, i) => new PlayedSeasonGame(
                        new SeasonGame("conf", p.Item1, p.Item2, Date: new DateOnly(2026, 11, 2).AddDays(i), PlaceId: 1), i)).ToList();
                    var results = new List<SeasonGameResult?>(Enumerable.Repeat<SeasonGameResult?>(null, league.Count + ConfTourneyGamesPerLeague));
                    var ct = new ConfTourneySource(9, "Constructed", members8, opens, league.Count,
                                                   league.Select(p => (p.FixtureOrdinal, p.Game)).ToList(), results, place, noIds, null);
                    int leaguePlayed = 0, tourneyPlayed = 0;
                    var refused = Refusal(() => SeasonPlayCalendar(
                        new SeasonGameSource[] { new SeasonStaticSource("constructed league", league), ct }, 2026,
                        SeasonPlayOrder.Calendar,
                        pg =>
                        {
                            if (pg.IsConferenceTournamentGame) tourneyPlayed++; else leaguePlayed++;
                            results[pg.FixtureOrdinal] = new SeasonGameResult(pg.Game.HomeId, pg.Game.AwayId, 80, 70, 0);
                            return (80, 70);
                        }));
                    return (refused, leaguePlayed, tourneyPlayed);
                }
                var early = Synthetic(new DateOnly(2026, 11, 20));   // the league plays through November 29
                Check("C4e: ★ NEGATIVE CONTROL — a conference tournament constructed to open before its league's last game " +
                      "REFUSES the season by name, before any of its games is played",
                      early.Refused is { } er && er.Contains("has not been played", StringComparison.Ordinal) && early.TourneyPlayed == 0,
                      early.Refused is { } e2 ? e2[..Math.Min(140, e2.Length)] + Inv($" ({early.LeaguePlayed} league games had played)") : "NO REFUSAL");
                var onTime = Synthetic(new DateOnly(2026, 12, 5));
                Check("C4f: ...and the same tournament opening after its league finished plays all seven games",
                      onTime.Refused is null && onTime.TourneyPlayed == ConfTourneyGamesPerLeague && onTime.LeaguePlayed == pairs.Count,
                      onTime.Refused ?? Inv($"{onTime.LeaguePlayed} + {onTime.TourneyPlayed} games"));

                //  A real stock bracket, its round-two games moved onto round one's day.
                var prestige = stock.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
                var ev8 = MteEventPlayOrder(one.Events.Seating).First(e => !e.IsShowcase && e.FieldSize == 8);
                var plan8 = MteSeedField(ev8, prestige, one.SeasonYear);
                var routes8 = MteBracketRoutesFor(ev8);
                var realDates = routes8.Select(r => MteRoundDate(plan8, r.Round, 3)).ToArray();
                var sameDay = routes8.Select(r => r.Round == 1 ? realDates[0] : realDates[r.GameIndex]).ToArray();
                var noSlots = new Dictionary<BracketSlotKey, GameId>();
                var bracketPlays = 0;
                var sameDayRefused = Refusal(() => SeasonPlayCalendar(
                    new SeasonGameSource[] { new MteBracketSource(ev8, plan8, 0, noSlots, null, sameDay) }, one.SeasonYear,
                    SeasonPlayOrder.Calendar, pg => { bracketPlays++; return (80, 70); }));
                Check("C4g: ★ NEGATIVE CONTROL — a bracket constructed with its round-two games on round one's own day is " +
                      "REFUSED by name before anything plays — even though each feeder's lower ordinal would have played it " +
                      "first that day",
                      sameDayRefused is { } sr && sr.Contains("strictly after", StringComparison.Ordinal) && bracketPlays == 0,
                      Inv($"{ev8.Name}: ") + (sameDayRefused?[..Math.Min(120, sameDayRefused.Length)] ?? "NO REFUSAL"));
                var realSource = new MteBracketSource(ev8, plan8, 0, noSlots, null);
                var realRefused = Refusal(() => SeasonPlayCalendar(new SeasonGameSource[] { realSource }, one.SeasonYear,
                    SeasonPlayOrder.Calendar, pg => (pg.HomeOriginalSeed!.Value % 2 == 0 ? 70 : 80, 75)));
                Check("C4h: ...and the same bracket on its real dates plays all twelve games and places all eight",
                      realRefused is null && realSource.FinishBySeat.Count == 8, realRefused ?? "12 games, 8 places");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — THE DAY PAGE.
            // ════════════════════════════════════════════════════════════════════
            {
                var names = stock.Schools.ToDictionary(s => s.Id, s => s.Name);
                var dfi = CultureInfo.InvariantCulture.DateTimeFormat;
                //  An independent read: the log's blocks on that date, by ordinal, written out here.
                string Expected(DateOnly d)
                {
                    var header = dfi.GetDayName(d.DayOfWeek) + ", " + dfi.GetMonthName(d.Month) + " "
                               + d.Day.ToString(CultureInfo.InvariantCulture) + ", " + d.Year.ToString(CultureInfo.InvariantCulture);
                    var blocks = logOne.Blocks.Where(b => b.Facts.Date == d).OrderBy(b => b.Facts.FixtureOrdinal).ToList();
                    if (blocks.Count == 0) return "No games on " + header + ".\n";
                    var sb = new StringBuilder(header).Append('\n');
                    foreach (var b in blocks)
                    {
                        var f = b.Facts;
                        var ot = f.OvertimePeriods == 0 ? "" : f.OvertimePeriods == 1 ? " (OT)" : $" ({f.OvertimePeriods}OT)";
                        sb.Append(names[f.AwaySchoolId]).Append(' ').Append(f.AwayScore)
                          .Append(f.HasHost == true ? " @ " : " vs. ")
                          .Append(names[f.HomeSchoolId]).Append(' ').Append(f.HomeScore).Append(ot)
                          .Append(f.HasHost == true ? "" : "   (neutral)").Append('\n');
                    }
                    return sb.Append(blocks.Count).Append(blocks.Count == 1 ? " game\n" : " games\n").ToString();
                }
                (int Code, string Text) Day(string date) => Run(() => RunDay(new[] { "day", stockPath, date, "--history", cal.Path }));
                static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                var byDate = logOne.Blocks.GroupBy(b => b.Facts.Date!.Value).ToDictionary(g => g.Key, g => g.ToList());
                var busiest = byDate.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key).First().Key;
                var neutral = byDate.Where(kv => kv.Value.Any(b => b.Facts.HasHost == false)).Min(kv => kv.Key);
                var overtimeDays = byDate.Where(kv => kv.Value.Any(b => b.Facts.OvertimePeriods > 0)).Select(kv => kv.Key).ToList();
                var overtime = overtimeDays.Where(d => d != neutral && d != busiest).DefaultIfEmpty(overtimeDays.Min()).Min();
                var christmas = new DateOnly(one.SeasonYear, 12, 25);
                var empty = byDate.ContainsKey(christmas)
                    ? Enumerable.Range(0, 400).Select(i => one.FirstLegalDay.AddDays(i)).First(d => !byDate.ContainsKey(d))
                    : christmas;
                var cases = new (string Label, DateOnly Date)[]
                {
                    ("the busiest day", busiest), ("a day with a neutral game", neutral), ("a day with an overtime game", overtime),
                    ("a day with no games", empty), ("November 1", one.FirstLegalDay), ("the championship Monday", one.LastLegalDay),
                };
                foreach (var (label, date) in cases)
                {
                    var (code, text) = Day(Iso(date));
                    var want = Expected(date);
                    Check(Inv($"C5: {label} ({date:yyyy-MM-dd}) prints, line for line, the log's blocks on that date in ordinal order"),
                          code == 0 && text == want,
                          text == want ? Inv($"{(byDate.TryGetValue(date, out var l) ? l.Count : 0)} games") : "first line: " + text.Split('\n')[0]);
                }

                Check("C5g: a date in July belongs to the season that opens that year; June to the one that opened the year before",
                      DaySeasonOf(new DateOnly(2026, 7, 1), 2026) == 1 && DaySeasonOf(new DateOnly(2027, 6, 30), 2026) == 1
                      && DaySeasonOf(new DateOnly(2027, 7, 1), 2026) == 2 && DaySeasonOf(new DateOnly(2026, 6, 30), 2026) == 0);
                var july = Day("2027-07-15");
                Check("C5h: July 15, 2027 (season two's summer, before its first game) prints that it has no games",
                      july.Code == 0 && july.Text == "No games on Thursday, July 15, 2027.\n", july.Text.Trim());

                var noHistory = Run(() => RunDay(new[] { "day", stockPath, "2026-11-18" }));
                Check("C5i: REFUSED BY NAME — no --history",
                      noHistory.Code == 1 && noHistory.Text.Contains("--history <career.json> is required", StringComparison.Ordinal), noHistory.Text.Trim());
                var badDate = Day("2026-13-01");
                Check("C5j: REFUSED BY NAME — a date that is not a date",
                      badDate.Code == 1 && badDate.Text.Contains("is not a date", StringComparison.Ordinal), badDate.Text.Trim());
                var before = Day("2026-06-30");
                Check("C5k: REFUSED BY NAME — a date before the career's first season",
                      before.Code == 1 && before.Text.Contains("is before this career", StringComparison.Ordinal), before.Text.Trim());
                var past = Day("2028-07-01");
                Check("C5l: REFUSED BY NAME — a date past the last played season",
                      past.Code == 1 && past.Text.Contains("is past this career", StringComparison.Ordinal), past.Text.Trim());
                var logPath = GameLogWriter.FinalPathFor(cal.Path, 1);
                var original = File.ReadAllBytes(logPath);
                (int Code, string Text) unreadable;
                try
                {
                    var damaged = (byte[])original.Clone();
                    damaged[damaged.Length / 2] ^= 0x01;
                    File.WriteAllBytes(logPath, damaged);
                    RelistCareerForCheck(cal.Path);   // ★ S125 — saved that way, so the READER is what refuses it
                    unreadable = Day("2026-11-18");
                }
                finally { File.WriteAllBytes(logPath, original); RelistCareerForCheck(cal.Path); }
                Check("C5m: REFUSED BY NAME — a season with no record (its log made unreadable, restored after)",
                      unreadable.Code == 1 && unreadable.Text.Contains("season 1 has no record", StringComparison.Ordinal),
                      unreadable.Text.Trim());
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — EVERY PATH PLAYS BY DAY.
            // ════════════════════════════════════════════════════════════════════
            {
                var legacy = RunSeasonCore(stock, CalendarCheckSeed, configPath, verbose: false);
                var fl = CalendarOrderFault(legacy.PlayedGames, legacy.PlayOrder, legacy.FirstLegalDay, legacy.LastLegalDay);
                Check("C6a: the legacy `season` plays by day (C1's test)", fl is null, fl ?? Inv($"{legacy.PlayOrder.Count:N0} games"));
                Check("C6b: ★ the legacy season is the career's season one, game for game, and the S120 capture",
                      GamesDigest(legacy) == GamesDigest(one) && GamesDigest(legacy) == RatingGoldenPreS112GameDigest,
                      GamesDigest(legacy)[..16]);
                var stacked = StackedTurnoverAndCamp(stock, legacy, CalendarCheckSeed, seedTwo, DevelopmentConfig.Load(configPath));
                var two = RunSeasonCore(stock, seedTwo, configPath, verbose: false, rostersInHand: stacked.SeasonTwo,
                                        seasonYear: SeasonDefaultStartYear + 1);
                var ft = CalendarOrderFault(two.PlayedGames, two.PlayOrder, two.FirstLegalDay, two.LastLegalDay);
                Check("C6c: the `seasons` command's season two plays by day", ft is null,
                      ft ?? Inv($"{two.PlayOrder.Count:N0} games, {two.FirstLegalDay:yyyy-MM-dd}.."));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — PAGE-ONLY: the shape of the stock season on the calendar. Never asserted.
            // ════════════════════════════════════════════════════════════════════
            {
                var byDay = one.PlayedGames.GroupBy(p => p.Game.Date!.Value).ToDictionary(g => g.Key, g => g.Count());
                var first = byDay.Keys.Min();
                var last = byDay.Keys.Max();
                var busiest = byDay.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
                var longest = 0;
                var longestFrom = first;
                var run = 0;
                for (var d = first; d <= last; d = d.AddDays(1))
                {
                    if (byDay.ContainsKey(d)) { run = 0; continue; }
                    run++;
                    if (run > longest) { longest = run; longestFrom = d.AddDays(1 - run); }
                }
                Console.WriteLine(Inv($"  [page] {byDay.Count} days with games, {first:yyyy-MM-dd} to {last:yyyy-MM-dd}; busiest {busiest.Key:ddd yyyy-MM-dd} with {busiest.Value} games"));
                Console.WriteLine("  [page] games per month: " + string.Join(", ",
                    one.PlayedGames.GroupBy(p => (p.Game.Date!.Value.Year, p.Game.Date!.Value.Month)).OrderBy(g => g.Key)
                       .Select(g => Inv($"{new DateOnly(g.Key.Year, g.Key.Month, 1):MMM} {g.Count():N0}"))));
                Console.WriteLine(Inv($"  [page] longest stretch without a game: {longest} day(s), from {longestFrom:yyyy-MM-dd}"));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 113 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 113 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;

        SeasonRunOutcome Career(WorldFile w, long seed, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var store = OpenCareer(path, w);
            return RunSeasonCore(w, seed, configPath, verbose: false, store, retainGameLog: true);
        }
    }
}
