using System.Globalization;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S119 — PHASE 110: A CAREER CAN START IN ANY YEAR, AND EVERY SEASON KNOWS ITS OWN.
//
//  Emmett (2026-10-09): "I'd like the option for the user to be able to start in any
//  year they want ... If they want to start in 1950 with whatever slate of teams, they
//  can." And: "The scheduling doesn't change." So today's calendar holds in every year
//  — the November 1 floor, Selection Sunday as the third Sunday in March, the Christmas
//  week, the conference tournaments before Selection Sunday — and this phase proves the
//  stock world fits it in every shape a year can take.
//
//  ★ C1 IS THE WHOLE DATING PIPELINE ON THE STOCK WORLD, IN EVERY CALENDAR SHAPE. A
//  shape is (November 1's weekday, whether the spring is a leap year): fourteen of
//  them, computed here rather than listed, plus year 1 and year 9998. Nothing is
//  played — every dating stage takes its inputs before the first tip — and C2 proves
//  the sweep's pipeline IS the season's by matching the two pinned 2026 fingerprints.
//
//  ★ WHAT WAS BROKEN BEFORE S119, so the negative control has something to bite: the
//  non-conference curve's fourteen weeks were plain dates that are Mondays only in a
//  2026-shaped year, so every other year crashed on its first buy-game pairing; and
//  every event window was resolved onto 2026 whatever season was being played.
//
//  Page-only: per shape, the seated and unseated pairing counts and which curve week
//  fell in Christmas week are printed, never asserted.
// ============================================================================

internal static partial class Program
{
    private const long YearCheckSeed = 20260720;

    /// <summary>One season's dates, produced WITHOUT playing: the same stages, in the same order
    /// and with the same inputs, as <see cref="RunSeasonCore"/> steps 1-6 in legacy mode (no
    /// career). Phase 110 C2 pins this to the season runner's own 2026 fingerprints, so the
    /// sweep cannot quietly drift from the thing it sweeps.</summary>
    private sealed record YearDating(
        int Year, List<SeasonGame> League, EventSeatingOutcome Seating, NonConDateReport NonCon,
        IReadOnlyList<(int ConferenceId, IReadOnlyList<int> Members, DateOnly Open)> Tourneys,
        string DatedFingerprint);

    private static YearDating YearDateWithoutPlaying(WorldFile world, long seed, int year)
    {
        var seating = MteSeatSeason(world, seed, MteReadHistory(null, 0), startYear: year);
        var contracts = RunContractSeason(world, ReadLiveContracts(null, 0), seating, null);
        var requests = BuildNonConferenceRequests(world, seating, contracts.Charges);
        var matching = BuildNonConferenceMatching(world, requests, contracts.UsedPairs);
        var schedule = BuildSeasonSchedule(world, seed, null, deferNumbering: true, out _, out _);
        var datedFp = SeasonDateSchedule(world, schedule, year);
        var nonCon = DateNonConferenceGames(world, matching, contracts, seating, schedule, year);
        MteRefuseOverlap(world, seating, schedule, year);
        var confById = world.Conferences.ToDictionary(c => c.Id);
        var tourneys = ConfTourneyFields(world).Seating
            .Select(x => (x.ConferenceId, x.Members,
                          ConfTourneyOpen(year, x.ConferenceId, confById[x.ConferenceId].TourneyOffsetDays)))
            .ToList();
        return new YearDating(year, schedule, seating, nonCon, tourneys, datedFp);
    }

    private static bool Phase110YearCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 110 — S119: a career can start in any year. Every calendar shape dates the stock world, " +
                          "2026 is unmoved, a career keeps its years, and the edges refuse by name ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static string Capture(Action run)
        {
            var old = Console.Out;
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Console.SetOut(sw);
            try { run(); } finally { Console.SetOut(old); }
            return sw.ToString();
        }
        static string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
        static Dictionary<string, byte[]> Snapshot(string dir)
            => Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".lock", StringComparison.Ordinal))
                        .ToDictionary(f => f, File.ReadAllBytes);
        static bool SameSnapshot(Dictionary<string, byte[]> a, Dictionary<string, byte[]> b)
            => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v.SequenceEqual(kv.Value));

        var scratch = Path.Combine(Path.GetTempPath(), $"charm_p110_{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        string Fresh(string name)
        {
            var dir = Path.Combine(scratch, name);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "career.json");
        }

        try
        {
            var stockPath = WorldPath("stock-d1.world.json");
            var tinyPath = WorldPath("fixture-tiny.world.json");
            var mtePath = WorldPath("fixture-mte.world.json");
            var stock = LoadWorld(stockPath);
            var tiny = LoadWorld(tinyPath);
            var mte = LoadWorld(mtePath);

            // ══ C1 — EVERY CALENDAR SHAPE DATES THE STOCK WORLD ══════════════════════════════
            {
                var shapes = new List<int>();
                var seen = new HashSet<(DayOfWeek, bool)>();
                for (var y = SeasonDefaultStartYear; seen.Count < 14; y++)
                    if (seen.Add((new DateOnly(y, 11, 1).DayOfWeek, DateTime.IsLeapYear(y + 1)))) shapes.Add(y);
                Check("C1a: fourteen calendar shapes found (November 1's weekday × a leap spring or not)",
                      shapes.Count == 14, string.Join(",", shapes));
                var years = shapes.Concat(new[] { CharmCalendar.MinSeasonStartYear, CharmCalendar.MaxSeasonStartYear }).ToList();

                Console.WriteLine("  page-only: year  Nov1  spring  league  seated  unseated  short  Christmas-week curve row");
                foreach (var year in years)
                {
                    YearDating d;
                    try { d = YearDateWithoutPlaying(stock, YearCheckSeed, year); }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        Check(Inv($"C1 {year}: the stock world dates with no refusal"), false,
                              ex.GetType().Name + ": " + ex.Message[..Math.Min(160, ex.Message.Length)]);
                        continue;
                    }
                    var cal = new BasketballSeasonCalendar(year);

                    // Every night each school is committed to: league games, every night of its
                    // event window (window length == rounds, so a seated school plays them all),
                    // its dated non-conference games, and — a deliberate SUPERSET — every night of
                    // its league's tournament, whether or not it makes the field of eight.
                    var nights = new List<(int School, DateOnly Date, string Kind)>();
                    foreach (var g in d.League)
                        if (g.Date is { } dt) { nights.Add((g.HomeId, dt, "league")); nights.Add((g.AwayId, dt, "league")); }
                    var leagueUndated = d.League.Count(g => g.Date is null);
                    foreach (var e in d.Seating.Active)
                    {
                        var f = MteWindowDate(e.FirstDay, year);
                        var l = MteWindowDate(e.LastDay, year);
                        foreach (var seat in e.Seats)
                            for (var x = f; x <= l; x = x.AddDays(1)) nights.Add((seat.SchoolId, x, "event"));
                    }
                    foreach (var g in d.NonCon.Games)
                    {
                        nights.Add((g.HostSchoolId, g.Date, "non-conference"));
                        nights.Add((g.VisitorSchoolId, g.Date, "non-conference"));
                    }
                    foreach (var (_, members, open) in d.Tourneys)
                        foreach (var m in members)
                            for (var r = 0; r < 3; r++) nights.Add((m, open.AddDays(r), "conference tournament"));

                    var outside = nights.Where(n => n.Date < cal.FirstLegalDay || n.Date >= cal.SelectionSunday).ToList();
                    var doubled = nights.GroupBy(n => (n.School, n.Date)).Where(g => g.Count() > 1).ToList();

                    Check(Inv($"C1 {year}: the stock world dates with no refusal — every league game dated, ") +
                          "the non-conference dater seats at least one pairing",
                          leagueUndated == 0 && d.NonCon.Games.Count > 0,
                          Inv($"{d.League.Count} league games, {leagueUndated} undated, {d.NonCon.Games.Count} pairings seated"));
                    Check(Inv($"C1 {year}: every date lies in the season (on or after {cal.FirstLegalDay:yyyy-MM-dd}, ") +
                          Inv($"before Selection Sunday {cal.SelectionSunday:yyyy-MM-dd}) — league, event, buy game, conference tournament"),
                          outside.Count == 0,
                          outside.Count == 0 ? Inv($"{nights.Count} school-nights")
                              : Inv($"{outside.Count} outside, first: school {outside[0].School} {outside[0].Kind} {outside[0].Date:yyyy-MM-dd}"));
                    Check(Inv($"C1 {year}: no school plays twice on one date"),
                          doubled.Count == 0,
                          doubled.Count == 0 ? "" : Inv($"{doubled.Count} collisions, first: school {doubled[0].Key.School} on {doubled[0].Key.Date:yyyy-MM-dd} (") +
                                                     string.Join(" + ", doubled[0].Select(n => n.Kind)) + ")");

                    var xmas = NonConChristmasWeek(year);
                    var xmasRow = NonConCurve.Where(r => NonConCurveMonday(r.Month, r.Day, year) == xmas)
                                             .Select(r => Inv($"{r.Month:00}-{r.Day:00} (weight {r.Weight})")).FirstOrDefault() ?? "none";
                    Console.WriteLine(Inv($"  page-only: {year,4}  {new DateOnly(year, 11, 1).DayOfWeek.ToString()[..3]}   ") +
                                      (DateTime.IsLeapYear(year + 1) ? "leap  " : "      ") +
                                      Inv($"  {d.League.Count,6}  {d.NonCon.Games.Count,6}  {d.NonCon.Unseated.Count,8}  {d.NonCon.AllocationShortfalls.Count,5}  {xmasRow}"));
                    if (d.NonCon.Unseated.Count > 0)
                    {
                        var indie = stock.Schools.Where(s => stock.Conferences.First(c => c.Id == s.ConferenceId).Games == 0)
                                                 .Select(s => s.Id).ToHashSet();
                        var byClass = string.Join(", ", d.NonCon.Unseated.GroupBy(u => u.Class)
                                                    .Select(g => Inv($"{g.Key} {g.Count()}")));
                        var schools = d.NonCon.Unseated.SelectMany(u => new[] { u.HostId, u.VisitorId }).Distinct().ToList();
                        Console.WriteLine(Inv($"  page-only:       unseated by reason: {byClass}; {schools.Count} schools short a game, ") +
                                          Inv($"{schools.Count(indie.Contains)} of them Independents"));
                    }
                }
            }

            // ══ C2 — 2026 IS THE IDENTITY ═══════════════════════════════════════════════════
            {
                var rowsSame = NonConCurve.All(r =>
                    NonConCurveMonday(r.Month, r.Day, SeasonDefaultStartYear)
                        == new DateOnly(r.Month >= 7 ? SeasonDefaultStartYear : SeasonDefaultStartYear + 1, r.Month, r.Day));
                Check("C2a: all fourteen curve rows resolve in 2026 to the very dates they always named (nearest Monday == the row)",
                      rowsSame && NonConCurve.Length == 14);
                var d = YearDateWithoutPlaying(stock, YearCheckSeed, SeasonDefaultStartYear);
                Check("C2b: ★ the sweep's pipeline is the season's — stock 2026's conference dated fingerprint equals the pinned " +
                      "MatchGoldenDatedFp (unchanged, never re-captured)",
                      d.DatedFingerprint == MatchGoldenDatedFp, d.DatedFingerprint[..8] + "…");
                Check("C2c: ★ ...and its non-conference dated fingerprint equals the pinned KnockoutGoldenNonConDatedFp",
                      d.NonCon.DatedFingerprint == KnockoutGoldenNonConDatedFp, d.NonCon.DatedFingerprint[..8] + "…");
            }

            // ══ C3 — A CAREER KEEPS ITS YEARS (and C5's mismatch edges, between its seasons) ═══
            {
                var path = Fresh("c3_1950");
                var mteFp = WorldFingerprint(mte);
                SeasonRunOutcome one, two;
                using (var store = OpenHistoryFor(mte, path, 1950)!)
                    one = RunSeasonCore(mte, YearCheckSeed, configPath, verbose: false, store, retainGameLog: true);

                static (DateOnly Lo, DateOnly Hi) Season(int y)
                    => (new BasketballSeasonCalendar(y).FirstLegalDay, new BasketballSeasonCalendar(y).SelectionSunday);
                static bool AllIn(SeasonRunOutcome r, int y)
                {
                    var (lo, hi) = Season(y);
                    return r.PlayedGames.All(p => p.Game.Date is { } dt && dt >= lo && dt < hi);
                }
                var oneEvents = one.PlayedGames.Where(p => p.IsEventGame).ToList();
                Check("C3a: season 1 of a career started with --year 1950 is played in 1950-51 — every game dated, every date in it",
                      one.SeasonYear == 1950 && AllIn(one, 1950), Inv($"{one.PlayedGames.Count} games"));
                Check("C3b: ★ its event games are dated in November 1950, not 2026 (the window threading)",
                      oneEvents.Count > 0 && oneEvents.All(p => p.Game.Date!.Value.Year == 1950),
                      Inv($"{oneEvents.Count} event games, first {oneEvents.FirstOrDefault()?.Game.Date:yyyy-MM-dd}"));
                Check("C3c: the career file says 1950", File.ReadAllText(path).Contains("\"startYear\": 1950", StringComparison.Ordinal));

                // C5 — the mismatch edges, on this career, before its second season.
                var before = File.ReadAllBytes(path);
                int same;
                using (var store = OpenHistoryFor(mte, path, 1950)!) same = store.StartYear;
                HistoryException? mismatch = null;
                try { using var s = OpenHistoryFor(mte, path, 1951); }
                catch (HistoryException hx) { mismatch = hx; }
                Check("C5a: on a 1950 career about to play 1951, --year 1950 is accepted and changes nothing",
                      same == 1950 && File.ReadAllBytes(path).SequenceEqual(before));
                Check("C5b: ...--year 1951 is refused by name (StartYearMismatch: 'this career started in 1950'), the file untouched",
                      mismatch?.Error == HistoryError.StartYearMismatch
                      && mismatch.Message.Contains("this career started in 1950", StringComparison.Ordinal)
                      && File.ReadAllBytes(path).SequenceEqual(before),
                      mismatch?.Message ?? "no refusal");

                using (var store = OpenHistoryFor(mte, path, null)!)
                    two = RunSeasonCore(mte, YearCheckSeed + 1, configPath, verbose: false, store, retainGameLog: true);
                var twoEvents = two.PlayedGames.Where(p => p.IsEventGame).ToList();
                Check("C3d: ★ season 2, its year read back off the file by a fresh open, is played in 1951-52 — every game, every date",
                      two.SeasonYear == 1951 && AllIn(two, 1951), Inv($"{two.PlayedGames.Count} games"));
                Check("C3e: ★ its event games are dated in November 1951 — a 1951 season with tournaments dated 1950 would fail here",
                      twoEvents.Count > 0 && twoEvents.All(p => p.Game.Date!.Value.Year == 1951),
                      Inv($"{twoEvents.Count} event games, first {twoEvents.FirstOrDefault()?.Game.Date:yyyy-MM-dd}"));
                var page = Capture(() => PrintSeasonPage(two, mte, null, SeasonDefaultMinuteFloor));
                Check("C3f: the page prints 'Dated: season 1951-1952'",
                      page.Contains("Dated: season 1951-1952,", StringComparison.Ordinal));
                Check("C3g: the career file still says 1950 after two seasons — the first year never moves",
                      File.ReadAllText(path).Contains("\"startYear\": 1950", StringComparison.Ordinal));
            }

            // ══ C4 — THE DEFAULTS ════════════════════════════════════════════════════════════
            {
                var p = Fresh("c4_default");
                int made;
                using (var store = HistoryStore.Open(p, WorldFingerprint(tiny))) made = store.StartYear;
                Check("C4a: a career created with no year starts in 2026 — the career file's default IS the season default",
                      made == SeasonDefaultStartYear && HistoryStateV3.DefaultStartYear == SeasonDefaultStartYear,
                      Inv($"{made}"));
                var legacy = RunSeasonCore(tiny, YearCheckSeed, configPath, verbose: false);
                Check("C4b: a season with no career and no year is 2026", legacy.SeasonYear == SeasonDefaultStartYear);
                var plain = Capture(() => RunSeasons(configPath, new[] { "seasons", tinyPath, "20260720" }));
                var at1950 = Capture(() => RunSeasons(configPath, new[] { "seasons", tinyPath, "20260720", "--year", "1950" }));
                static int At(string s, string t) => s.IndexOf(t, StringComparison.Ordinal);
                Check("C4c: `seasons` plays 2026 then 2027",
                      At(plain, "Dated: season 2026-2027,") >= 0
                      && At(plain, "Dated: season 2027-2028,") > At(plain, "Dated: season 2026-2027,"));
                Check("C4d: `seasons --year 1950` plays 1950 then 1951",
                      At(at1950, "Dated: season 1950-1951,") >= 0
                      && At(at1950, "Dated: season 1951-1952,") > At(at1950, "Dated: season 1950-1951,"));
                var last = Capture(() => RunSeasons(configPath, new[] { "seasons", tinyPath, "20260720", "--year", "9998" }));
                Check("C4e: `seasons --year 9998` is refused by name before anything plays — season two would open in 9999",
                      last.Contains("SEASONS ERROR [YearOutOfDomain]", StringComparison.Ordinal)
                      && !last.Contains("Dated: season", StringComparison.Ordinal));
            }

            // ══ C5 — THE EDGES ═══════════════════════════════════════════════════════════════
            {
                Check("C5c: the career file's year bounds are the calendar's (1..9998)",
                      HistoryStateV3.MinStartYear == CharmCalendar.MinSeasonStartYear
                      && HistoryStateV3.MaxStartYear == CharmCalendar.MaxSeasonStartYear);
                foreach (var (label, value, want) in new[]
                {
                    ("--year 0", "0", "[YearOutOfDomain]"),
                    ("--year 9999", "9999", "[YearOutOfDomain]"),
                    ("a malformed year", "19x0", "[WrongType]"),
                })
                {
                    var p = Fresh("c5_" + value);
                    var outp = Capture(() => RunSeason(configPath,
                        new[] { "season", tinyPath, "20260720", "--history", p, "--year", value }));
                    Check(Inv($"C5 {label} is refused by name and nothing is written — no career file, no log folder"),
                          outp.Contains("SEASON ERROR " + want, StringComparison.Ordinal)
                          && !File.Exists(p) && !Directory.Exists(GameLogWriter.LogFolderFor(p))
                          && !outp.Contains("Dated: season", StringComparison.Ordinal),
                          outp.Trim().Split('\n')[0]);
                }
                var none = Capture(() => RunSeason(configPath, new[] { "season", tinyPath, "20260720", "--year" }));
                Check("C5 --year with no year after it is refused by name",
                      none.Contains("SEASON ERROR [WrongType]", StringComparison.Ordinal), none.Trim());

                var v2 = Fresh("c5_v2");
                File.WriteAllText(v2, RawHistoryV2(WorldFingerprint(tiny), 4001, 7, 900));
                var v2Before = File.ReadAllBytes(v2);
                var v2Out = Capture(() => RunSeason(configPath, new[] { "season", tinyPath, "20260720", "--history", v2 }));
                Check("C5 a v2 career is refused by the standing sentence, the file byte-identical, no log folder",
                      v2Out.Contains(OlderCareerSentence, StringComparison.Ordinal)
                      && File.ReadAllBytes(v2).SequenceEqual(v2Before)
                      && !Directory.Exists(GameLogWriter.LogFolderFor(v2)),
                      v2Out.Trim().Split('\n')[0]);

                // ★ The last year: a career started in 9998 plays its season (into 9999), and its
                //   second season is refused by name with every file it owns byte-identical.
                var p9998 = Fresh("c5_9998");
                SeasonRunOutcome lastSeason;
                using (var store = OpenHistoryFor(tiny, p9998, 9998)!)
                    lastSeason = RunSeasonCore(tiny, YearCheckSeed, configPath, verbose: false, store, retainGameLog: true);
                var (lo, hi) = (new BasketballSeasonCalendar(9998).FirstLegalDay, new BasketballSeasonCalendar(9998).SelectionSunday);
                Check("C5 a career started in 9998 plays its season, its dates running into 9999",
                      lastSeason.SeasonYear == 9998
                      && lastSeason.PlayedGames.All(g => g.Game.Date is { } dt && dt >= lo && dt < hi)
                      && lastSeason.PlayedGames.Any(g => g.Game.Date!.Value.Year == 9999),
                      Inv($"{lastSeason.PlayedGames.Count} games"));
                var dir = Path.GetDirectoryName(p9998)!;
                var snap = Snapshot(dir);
                var refused = Capture(() => RunSeason(configPath, new[] { "season", tinyPath, "20260721", "--history", p9998 }));
                Check("C5 ★ ...its second season (it would open in 9999) is refused by name — the career file, the season logs and " +
                      "the event records byte-identical, nothing new written",
                      refused.Contains("SEASON ERROR [YearOutOfDomain]", StringComparison.Ordinal)
                      && refused.Contains("would open in 9999", StringComparison.Ordinal)
                      && SameSnapshot(snap, Snapshot(dir)),
                      Inv($"{snap.Count} files compared"));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 110 ran to completion", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        Console.WriteLine(Inv($"  Phase 110: {(pass ? "PASS" : "FAIL")} ({assertions} assertions)"));
        return pass;
    }
}
