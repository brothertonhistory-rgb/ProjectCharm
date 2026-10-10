using Charm.Engine;
using Charm.History;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Charm.Harness;

// ============================================================================
//  Phase 105 — S115: THE PEOPLE SURVIVE.
//
//  A career goes year to year with the same men. What must be proven:
//    C1 the round-trip — pool row -> roster entry (schema v2) -> pool row is the
//       identity on all 38 ratings, the card key by key, position, role, rank,
//       class, plane, orientation and the identity map; a perturbed rating is caught.
//    C2 a career's first season is unchanged — season one's games are the pre-S112
//       capture and the person numbers issued are the numbers they were.
//    C3 season two on a career — every returner carries the same number as in
//       season one's log, every departed senior's number is absent, every
//       freshman's number is new (the allocator's high-water moved by exactly the
//       class size, never reissued), classes advanced per man, and the roster
//       section equals the turnover's rosters school for school. On fixture-mte
//       and once on stock.
//    C4 the career turnover is the in-memory turnover — the same rosters from the
//       log-read season one as from the in-memory one.
//    C5 the first career stat line — one man, two seasons, one number.
//    C6 refusals, each by name.
//    C7 determinism — two careers, two seasons each, identical.
//    C8 the fingerprint wall — the stock legacy season unmoved.
//  Page-only calibration holds: no basketball value is asserted.
// ============================================================================

internal static partial class Program
{
    private const long PeopleCheckSeed = 20260720;

    private static bool Phase105PeopleCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 105 — S115: the people survive. Roster schema v2 round-trips, a career's second " +
                          "season turns last season's people over under their own numbers, every refusal is by name, " +
                          "two careers agree, and the legacy season is unmoved ==");
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
        static bool SameMan(PoolPlayer a, PoolPlayer b)
            => a.Player.Name == b.Player.Name && a.Pos == b.Pos && a.Role == b.Role
               && a.DefensivePlane == b.DefensivePlane && a.OffensiveRole == b.OffensiveRole
               && a.ScoutRank == b.ScoutRank && a.Class == b.Class
               && a.Player.HierarchyRank == b.Player.HierarchyRank
               && RatingsOf(a.Player).SequenceEqual(RatingsOf(b.Player))
               && a.Ratings.Count == b.Ratings.Count
               && a.Ratings.All(kv => b.Ratings.TryGetValue(kv.Key, out var v) && v == kv.Value);
        static string Blame(Exception? ex) => ex?.Message is { } m ? m[..Math.Min(110, m.Length)] : "(no refusal)";
        static Exception? Refusal(Action a)
        {
            try { a(); return null; }
            catch (InvalidOperationException ex) { return ex; }
            catch (GameLogException ex) { return ex; }
            catch (HistoryException ex) { return ex; }
        }
        static bool RefusedWith(Exception? ex, string phrase) => ex is not null && ex.Message.Contains(phrase, StringComparison.Ordinal);
        static string RosterSectionDigest(GameLogV1 log) => RatingSha(string.Concat(log.RosterV2().Select(e =>
            Inv($"{e.PersonId}|{e.SchoolId}|{e.PoolId}|{e.AcquisitionIndex}|{e.Name}|{e.Role}|{(byte)e.Position}|{e.IsStarter}|{e.HierarchyRank}|{e.ScoutRank:R}|{string.Join(',', e.Ratings)}|{e.Class}|{e.DefensivePlane:R}|{e.OffensiveRole}\n"))));

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s115-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var mte = LoadWorld(WorldPath("fixture-mte.world.json"));
            var seedTwo = SeasonTwoSeed(PeopleCheckSeed);

            // Play one season on a career through the production path (retained log, as the command does).
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

            // ── C1: the round-trip on the stock pool ───────────────────────────
            {
                var rtPath = Path.Combine(scratch, "rt", "career.json");
                DivvyResult divvy;
                using (var store = HistoryStore.Open(rtPath, WorldFingerprint(stock)))
                    divvy = RunDivvyDraft(stock, PeopleCheckSeed, store);
                var rows = BuildSeasonRows(divvy, stock, verbose: false);
                var entries = BuildRetentionRoster(rows, divvy);
                var back = PoolRowsFromRoster(entries, stock);
                // ★ The read-back pool is laid out school by school in acquisition order; the bootstrap
                //   pool is laid out in draft order. The MAN is the key, never the index: match by number.
                var origByPerson = Enumerable.Range(0, divvy.Pool.Count).ToDictionary(i => divvy.PersonIds![i], i => divvy.Pool[i]);
                PoolPlayer Orig(int backIndex) => origByPerson[back.PersonIds![backIndex]];
                var sameRows = back.Pool.Count == divvy.Pool.Count
                               && Enumerable.Range(0, back.Pool.Count).All(i => SameMan(Orig(i), back.Pool[i]));
                var sameIds = back.PersonIds!.Count == divvy.Pool.Count
                              && Enumerable.Range(0, back.Pool.Count).Select(i => back.PersonIds![i]).Distinct().Count() == divvy.Pool.Count;
                var sameRosters = stock.Schools.All(s =>
                    back.Rosters[s.Id].Select(pid => back.PersonIds![pid]).SequenceEqual(divvy.Rosters[s.Id].Select(pid => divvy.PersonIds![pid])));
                var dense = Enumerable.Range(0, back.Pool.Count).All(i => back.Pool[i].PoolId == i);
                Check("C1a: every stock pool row -> RosterEntryV2 -> row is the identity on the man: all 38 ratings in the pinned order, " +
                      "the card key by key, name, position, role, hierarchy rank, scout rank, class, plane, orientation",
                      sameRows, $"{divvy.Pool.Count} rows");
                Check("C1b: the identity map pairs every read-back index with the number the man already carries, each number once",
                      sameIds && dense);
                Check("C1c: every school's roster comes back in acquisition order, man for man", sameRosters);
                Check("C1d: the entries written are schema-2 entries carrying class, plane and orientation",
                      entries.Count == divvy.Pool.Count
                      && entries.All(e => e.Class <= 3 && e.OffensiveRole.Length > 0)
                      && entries.Zip(entries.Select(e => divvy.Pool[e.PoolId])).All(z =>
                             z.First.Class == (byte)z.Second.Class && z.First.DefensivePlane == z.Second.DefensivePlane
                             && z.First.OffensiveRole == z.Second.OffensiveRole));
                // Negative control: one rating perturbed on one entry is caught.
                var victim = entries[137];
                var perturbed = victim.Ratings.ToArray(); perturbed[11] = (short)(perturbed[11] == 99 ? 98 : perturbed[11] + 1);
                var bent = entries.Select(e => ReferenceEquals(e, victim) ? e with { Ratings = perturbed } : e).ToList();
                var bentBack = PoolRowsFromRoster(bent, stock);
                var caught = Enumerable.Range(0, bentBack.Pool.Count).Count(i => !SameMan(origByPerson[bentBack.PersonIds![i]], bentBack.Pool[i])) == 1
                             && Enumerable.Range(0, bentBack.Pool.Count).Single(i => !SameMan(origByPerson[bentBack.PersonIds![i]], bentBack.Pool[i]))
                                is var bi && bentBack.PersonIds![bi] == victim.PersonId;
                Check("C1e: NEGATIVE CONTROL — one rating moved by one on one man is caught, and only that man differs", caught);
                // The five shot-diet numbers come back as STORED, never re-derived.
                var tendencies = Enumerable.Range(0, back.Pool.Count).All(i =>
                    GenTendencies.All(t => back.Pool[i].Ratings[t] == Orig(i).Ratings[t]));
                Check("C1f: the five shot-diet numbers are the stored numbers (no re-derivation drift)", tendencies);
            }

            // ── C2 + C3 (stock): a two-season career ────────────────────────────
            SeasonRunOutcome stockOne, stockTwo;
            {
                var path = Path.Combine(scratch, "stock", "career.json");
                stockOne = Career(stock, PeopleCheckSeed, path);
                var h1 = PeekState(path);
                Check("C2a: ★ a career's first season plays the S120 capture game for game",
                      GamesDigest(stockOne) == RatingGoldenPreS112GameDigest, $"{stockOne.PlayedGames.Count} games");
                Check("C2b: the person numbers issued are the numbers they were — the high-water after season one is 4,512 (1..4,511 spent)",
                      h1.NextPersonId == 4512 && h1.NextSeasonId == 2, $"nextPersonId {h1.NextPersonId}");
                Check("C2c: the page line for season one is the bootstrap line",
                      stockOne.People is { IsBootstrap: true } && stockOne.People.Line == "People: first season — the bootstrap pool");

                stockTwo = Career(stock, seedTwo, path);
                var h2 = PeekState(path);
                var log1 = ReadLog(stock, path, 1);
                var log2 = ReadLog(stock, path, 2);
                var r1 = log1.RosterV2(); var r2 = log2.RosterV2();
                var by1 = r1.ToDictionary(e => e.PersonId);
                var seniors1 = r1.Where(e => e.Class == 3).Select(e => e.PersonId).ToHashSet();
                var returners = r2.Where(e => by1.ContainsKey(e.PersonId)).ToList();
                var freshmen = r2.Where(e => !by1.ContainsKey(e.PersonId)).ToList();
                Check("C3a (stock): every non-senior of season one is in season two under the SAME number, at the same school, one class on, " +
                      "same name, same 38 ratings, same orientation",
                      returners.Count == r1.Count - seniors1.Count
                      && returners.All(e => by1[e.PersonId].Class + 1 == e.Class && by1[e.PersonId].SchoolId == e.SchoolId
                                            && by1[e.PersonId].Name == e.Name && by1[e.PersonId].Ratings.SequenceEqual(e.Ratings)
                                            && by1[e.PersonId].OffensiveRole == e.OffensiveRole),
                      $"{returners.Count} returned");
                Check("C3b (stock): every departed senior's number is absent from season two", !r2.Any(e => seniors1.Contains(e.PersonId)),
                      $"{seniors1.Count} departed");
                Check("C3c (stock): every freshman is a Freshman with a number new to the career, and the high-water moved by exactly the class size",
                      freshmen.All(e => e.Class == 0) && freshmen.Count == seniors1.Count
                      && h2.NextPersonId - h1.NextPersonId == freshmen.Count
                      && freshmen.Select(e => e.PersonId).Distinct().Count() == freshmen.Count,
                      $"{freshmen.Count} arrived; high-water {h1.NextPersonId} -> {h2.NextPersonId}");
                Check("C3d (stock): season two's roster section equals the turnover's rosters school for school, man for man",
                      stock.Schools.All(s =>
                          r2.Where(e => e.SchoolId == s.Id).OrderBy(e => e.AcquisitionIndex).Select(e => e.PersonId)
                            .SequenceEqual(stockTwo.Divvy.Rosters[s.Id].Select(pid => stockTwo.Divvy.PersonIds![pid]))));
                Check("C3e (stock): the page line names the counts and the source season",
                      stockTwo.People is { FromSeason: 1 } p && p.Returned == returners.Count && p.Arrived == freshmen.Count
                      && p.Departed == seniors1.Count, stockTwo.People?.Line ?? "(none)");
                var census = new[] { 0, 1, 2, 3 }.Select(c => r2.Count(e => e.Class == c)).ToArray();
                Console.WriteLine(Inv($"  (page) stock season two: Fr {census[0]} / So {census[1]} / Jr {census[2]} / Sr {census[3]}; freshman class {freshmen.Count}; high-water {h1.NextPersonId} -> {h2.NextPersonId}; season-two digest {GamesDigest(stockTwo)[..16]}"));
                Check("C3f (stock): classes advanced per man — season two's So/Jr/Sr counts are season one's Fr/So/Jr counts",
                      census[1] == r1.Count(e => e.Class == 0) && census[2] == r1.Count(e => e.Class == 1) && census[3] == r1.Count(e => e.Class == 2));

                // ── C5: the first career stat line ────────────────────────────────
                var rows1 = log1.Blocks.SelectMany(b => b.Rows).GroupBy(r => r.PersonId).ToDictionary(g => g.Key, g => g.Count());
                var rows2 = log2.Blocks.SelectMany(b => b.Rows).GroupBy(r => r.PersonId).ToDictionary(g => g.Key, g => g.Count());
                var twoSeasonMan = returners.FirstOrDefault(e => rows1.ContainsKey(e.PersonId) && rows2.ContainsKey(e.PersonId));
                var goneMan = seniors1.FirstOrDefault(id => rows1.ContainsKey(id));
                Check("C5a: a returner's season-one rows and season-two rows share ONE person number — the career stat line exists",
                      twoSeasonMan is not null && rows1[twoSeasonMan.PersonId] > 0 && rows2[twoSeasonMan.PersonId] > 0,
                      twoSeasonMan is null ? "(none)" : $"{twoSeasonMan.PersonId} ({twoSeasonMan.Name}): {rows1[twoSeasonMan.PersonId]} games, then {rows2[twoSeasonMan.PersonId]}");
                Check("C5b: a departed senior has season-one rows and none in season two",
                      goneMan.IsValid && rows1[goneMan] > 0 && !rows2.ContainsKey(goneMan),
                      goneMan.IsValid ? $"{goneMan}: {rows1[goneMan]} games, then 0" : "(none)");
                var allRowsKnown = log2.Blocks.All(b => b.Rows.All(r => r2.Any(e => e.PersonId == r.PersonId)));
                Check("C5c: every season-two game row names a man in season two's roster section", allRowsKnown);
            }

            // ── C3 (mte) + C4 + C7: fixture-mte careers ──────────────────────────
            {
                var pathA = Path.Combine(scratch, "mteA", "career.json");
                var pathB = Path.Combine(scratch, "mteB", "career.json");
                var a1 = Career(mte, PeopleCheckSeed, pathA);
                var hA1 = PeekState(pathA);
                var a2 = Career(mte, seedTwo, pathA);
                var hA2 = PeekState(pathA);
                var logA1 = ReadLog(mte, pathA, 1); var logA2 = ReadLog(mte, pathA, 2);
                var rA1 = logA1.RosterV2(); var rA2 = logA2.RosterV2();
                var byA1 = rA1.ToDictionary(e => e.PersonId);
                var seniorsA = rA1.Where(e => e.Class == 3).Select(e => e.PersonId).ToHashSet();
                var retA = rA2.Where(e => byA1.ContainsKey(e.PersonId)).ToList();
                var freshA = rA2.Where(e => !byA1.ContainsKey(e.PersonId)).ToList();
                Check("C3g (mte): returners under the same number one class on; seniors absent; freshmen new and exactly the vacancies",
                      retA.Count == rA1.Count - seniorsA.Count
                      && retA.All(e => byA1[e.PersonId].Class + 1 == e.Class && byA1[e.PersonId].SchoolId == e.SchoolId)
                      && !rA2.Any(e => seniorsA.Contains(e.PersonId))
                      && freshA.All(e => e.Class == 0) && freshA.Count == seniorsA.Count
                      && hA2.NextPersonId - hA1.NextPersonId == freshA.Count,
                      $"{retA.Count} returned, {freshA.Count} arrived, {seniorsA.Count} departed; high-water {hA1.NextPersonId} -> {hA2.NextPersonId}");
                Check("C3h (mte): a freshman's name is keyed on the season he arrived in and collides with nobody",
                      freshA.All(e => e.Name.StartsWith("Pool_s2_", StringComparison.Ordinal))
                      && rA2.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() == rA2.Count);
                Check("C3i (mte): season two's roster section equals the turnover's rosters school for school",
                      mte.Schools.All(s =>
                          rA2.Where(e => e.SchoolId == s.Id).OrderBy(e => e.AcquisitionIndex).Select(e => e.PersonId)
                             .SequenceEqual(a2.Divvy.Rosters[s.Id].Select(pid => a2.Divvy.PersonIds![pid]))));

                // C4: the turnover on the log-read season one is the turnover on the in-memory one.
                CareerPeople fromLog;
                using (var store = HistoryStore.Open(pathA, WorldFingerprint(mte)))
                    fromLog = ReadCareerPeople(store, mte, 1);
                string Name(int k) => $"Pool_s2_{k}";
                var tMem = RunTurnover(mte, a1.Divvy, seedTwo, Name);
                var tLog = RunTurnover(mte, fromLog.Previous, seedTwo, Name);
                var sameTurnover = mte.Schools.All(s =>
                    tMem.SeasonTwo.Rosters[s.Id].Count == tLog.SeasonTwo.Rosters[s.Id].Count
                    && tMem.SeasonTwo.Rosters[s.Id].Zip(tLog.SeasonTwo.Rosters[s.Id])
                          .All(z => SameMan(tMem.SeasonTwo.Pool[z.First], tLog.SeasonTwo.Pool[z.Second])));
                Check("C4a: ★ the same world, season-one rosters and seed -> RunTurnover on the LOG-READ rosters produces the same " +
                      "rosters as on the in-memory DivvyResult, matched by (school, acquisition order, man)",
                      sameTurnover && tMem.ReturnerCount == tLog.ReturnerCount && tMem.Freshmen.Count == tLog.Freshmen.Count,
                      $"{tMem.ReturnerCount} returners, {tMem.Freshmen.Count} freshmen");
                var memByPerson = Enumerable.Range(0, a1.Divvy.Pool.Count).ToDictionary(i => a1.Divvy.PersonIds![i], i => a1.Divvy.Pool[i]);
                Check("C4b: the log-read season one is the in-memory season one, man for man by number, and roster for roster",
                      fromLog.Previous.Pool.Count == a1.Divvy.Pool.Count
                      && Enumerable.Range(0, fromLog.Previous.Pool.Count).All(i =>
                             memByPerson.TryGetValue(fromLog.Previous.PersonIds![i], out var m) && SameMan(m, fromLog.Previous.Pool[i]))
                      && mte.Schools.All(s => fromLog.Previous.Rosters[s.Id].Select(pid => fromLog.Previous.PersonIds![pid])
                                                 .SequenceEqual(a1.Divvy.Rosters[s.Id].Select(pid => a1.Divvy.PersonIds![pid]))));
                Check("C4c: the turnover reads nothing from the identity map — its output carries none (identity is supplied after)",
                      tLog.SeasonTwo.PersonIds is null && tMem.SeasonTwo.PersonIds is null);

                // C7: a second career from the same world and seeds.
                var b1 = Career(mte, PeopleCheckSeed, pathB);
                var b2 = Career(mte, seedTwo, pathB);
                var logB1 = ReadLog(mte, pathB, 1); var logB2 = ReadLog(mte, pathB, 2);
                Check("C7a: ★ two careers from the same world and seeds, two seasons each: identical roster sections — same numbers, same men",
                      RosterSectionDigest(logA1) == RosterSectionDigest(logB1) && RosterSectionDigest(logA2) == RosterSectionDigest(logB2));
                Check("C7b: identical game digests, both seasons",
                      GamesDigest(a1) == GamesDigest(b1) && GamesDigest(a2) == GamesDigest(b2),
                      $"season two {GamesDigest(a2)[..16]}");
                var tOther = RunTurnover(mte, fromLog.Previous, seedTwo + 1, Name);
                Check("C7c: a different second seed moves season two (the freshman class and its draft read the seed)",
                      mte.Schools.Any(s => !tOther.SeasonTwo.Rosters[s.Id].Zip(tLog.SeasonTwo.Rosters[s.Id])
                          .All(z => SameMan(tOther.SeasonTwo.Pool[z.First], tLog.SeasonTwo.Pool[z.Second]))));

                // ── C6: refusals, each by name ──────────────────────────────────
                // (i) previous season kept no log: season one on the test flag (no log), season two normally.
                var noLog = Path.Combine(scratch, "nolog", "career.json");
                using (var store = HistoryStore.Open(noLog, WorldFingerprint(mte)))
                    RunSeasonCore(mte, PeopleCheckSeed, configPath, verbose: false, store, retainGameLog: false, bootstrapPeopleForTest: true);
                var spentBefore = PeekState(noLog);
                Exception? exNoLog;
                using (var store = HistoryStore.Open(noLog, WorldFingerprint(mte)))
                    exNoLog = Refusal(() => RunSeasonCore(mte, seedTwo, configPath, verbose: false, store, retainGameLog: true));
                var spentAfter = PeekState(noLog);
                Check("C6a: previous season kept no log -> refused by name, and nothing was spent",
                      RefusedWith(exNoLog, "kept no season log") && spentAfter.NextSeasonId == spentBefore.NextSeasonId
                      && spentAfter.NextPersonId == spentBefore.NextPersonId && spentAfter.NextGameId == spentBefore.NextGameId,
                      Blame(exNoLog));
                // (ii) a career season that keeps no log is refused unless a check says so.
                Exception? exNoRetain;
                using (var store = HistoryStore.Open(Path.Combine(scratch, "noretain", "career.json"), WorldFingerprint(mte)))
                    exNoRetain = Refusal(() => RunSeasonCore(mte, PeopleCheckSeed, configPath, verbose: false, store, retainGameLog: false));
                Check("C6b: history mode without the retained log is refused by name (the log is the career's people)",
                      RefusedWith(exNoRetain, "requires the retained log"), Blame(exNoRetain));
                // (iii) previous log is v1: season one's log rewritten to roster schema 1 (every v1 byte of each entry kept,
                //       the v2 tail dropped, the roster checksum and the payload digest recomputed), then season two.
                //       ★ S117 — C-60: the reader no longer reads roster schema 1 at all; an older career is refused
                //       whole, by the standing sentence, at the reader and so at the turnover.
                var v1Path = Path.Combine(scratch, "v1", "career.json");
                Career(mte, PeopleCheckSeed, v1Path);
                var v1Log = GameLogWriter.FinalPathFor(v1Path, 1);
                File.WriteAllBytes(v1Log, DowngradeRosterToV1(File.ReadAllBytes(v1Log)));
                var readV1 = Refusal(() => ReadLog(mte, v1Path, 1));
                Check("C6c-pre: ★ S117 — a roster-schema-1 log is refused by the READER, by the standing sentence (C-60)",
                      readV1 is GameLogException { Error: GameLogError.UnsupportedLogVersion }
                      && RefusedWith(readV1, "saved by an older version of the game — start a new career"), Blame(readV1));
                Exception? exV1;
                using (var store = HistoryStore.Open(v1Path, WorldFingerprint(mte)))
                    exV1 = Refusal(() => RunSeasonCore(mte, seedTwo, configPath, verbose: false, store, retainGameLog: true));
                Check("C6c: previous log is roster schema 1 (pre-S115, no class) -> the next season is refused by the same sentence",
                      RefusedWith(exV1, "saved by an older version of the game — start a new career"), Blame(exV1));
                // (iv)..(ix): structural refusals on the roster section, fed to the read-back directly.
                var good = rA1.ToList();
                var s0 = mte.Schools.OrderBy(s => s.Id).First().Id;
                var s1 = mte.Schools.OrderBy(s => s.Id).Skip(1).First().Id;
                var school0 = good.Where(e => e.SchoolId == s0).OrderBy(e => e.AcquisitionIndex).ToList();
                List<RosterEntryV2> Without(RosterEntryV2 e) => good.Where(x => !ReferenceEquals(x, e)).ToList();
                List<RosterEntryV2> Replace(RosterEntryV2 e, RosterEntryV2 by) => good.Select(x => ReferenceEquals(x, e) ? by : x).ToList();
                var twelve = Refusal(() => PoolRowsFromRoster(Without(school0[12]), mte));
                Check("C6d: a school with 12 entries -> refused by name", RefusedWith(twelve, "has 12 players"), Blame(twelve));
                var guardOut = school0.First(e => e.Position == RosterPosition.Wing);
                var sixThree = Refusal(() => PoolRowsFromRoster(Replace(guardOut, guardOut with { Position = RosterPosition.Guard }), mte));
                Check("C6e: a school at 6 G / 3 W / 4 B -> refused by name", RefusedWith(sixThree, "carries 6 G / 3 W / 4 B"), Blame(sixThree));
                var dupAcq = Refusal(() => PoolRowsFromRoster(Replace(school0[7], school0[7] with { AcquisitionIndex = 3 }), mte));
                Check("C6f: a duplicated acquisition place (13 entries, 13 men, two at place 3) -> refused by name — the count cannot see it",
                      RefusedWith(dupAcq, "two men at acquisition place 3"), Blame(dupAcq));
                var gapAcq = Refusal(() => PoolRowsFromRoster(Replace(school0[7], school0[7] with { AcquisitionIndex = 14 }), mte));
                Check("C6g: a missing acquisition place -> refused by name", RefusedWith(gapAcq, "nobody at acquisition place 8"), Blame(gapAcq));
                var school1 = good.Where(e => e.SchoolId == s1).OrderBy(e => e.AcquisitionIndex).ToList();
                var moved = school1.First(e => e.Position == school0[0].Position);
                var twoSchools = Refusal(() => PoolRowsFromRoster(Replace(moved, moved with { PersonId = school0[0].PersonId }), mte));
                Check("C6h: one person on two schools -> refused by name", RefusedWith(twoSchools, "AND school"), Blame(twoSchools));
                var dupPerson = Refusal(() => PoolRowsFromRoster(Replace(school0[1], school0[1] with { PersonId = school0[0].PersonId }), mte));
                Check("C6i: a duplicate person number on one school -> refused by name", RefusedWith(dupPerson, "appears twice on school"), Blame(dupPerson));
                var alien = Refusal(() => PoolRowsFromRoster(Replace(school0[0], school0[0] with { SchoolId = 999_999 }), mte));
                Check("C6j: an unknown school id -> refused by name", RefusedWith(alien, "not a school in this world"), Blame(alien));
                Check("C6k: the untouched roster section is accepted (the controls above fire on their defect, not on the fixture)",
                      Refusal(() => PoolRowsFromRoster(good, mte)) is null);
                Check("C6l: a class byte outside 0..3 is refused by the FORMAT itself",
                      RefusedWith(Refusal(() =>
                      {
                          using var store = HistoryStore.Open(Path.Combine(scratch, "cls", "career.json"), WorldFingerprint(mte));
                          var sid = store.ReserveSeason();
                          var bad = new List<RosterEntryV2> { new(store.ReservePersons(1)[0], 1, 0, 1, "X", "", RosterPosition.Guard, true, 5, 1.0, new short[38], 4, 0.0, "") };
                          GameLogWriter.Create(Path.Combine(scratch, "cls", "career.json"), store.HistoryId, store.WorldFingerprint, new string('a', 64), sid, bad);
                      }), "class ordinal 4"));
            }

            // ── C8: the fingerprint wall — the stock legacy season ──────────────
            {
                var legacy = RunSeasonCore(stock, PeopleCheckSeed, configPath, verbose: false);
                var prefix = legacy.ConferenceGameCount + legacy.TournamentGameCount;
                var resultsFp = SeasonFingerprint(legacy.Results.Take(prefix).ToList(), legacy.PossessionCounts.Take(prefix).ToList());
                Check("C8a: #1 conference schedule UNMOVED", legacy.Fingerprint == MatchGoldenConferenceFp);
                Check("C8b: #2 conference dated UNMOVED", legacy.DatedFingerprint == MatchGoldenDatedFp);
                Check("C8c: #3 event games UNMOVED", legacy.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C8d: #4 results+possessions UNMOVED over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C8e: #5 non-conference dated UNMOVED", legacy.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C8f: #6 conference tournaments UNMOVED", legacy.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C8g: #7 buy games UNMOVED", legacy.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                Check("C8h: ★ every legacy game identical to the S120 capture", GamesDigest(legacy) == RatingGoldenPreS112GameDigest);
                Check("C8i: legacy mode prints no people line and carries no identity map",
                      legacy.People is null && legacy.Divvy.PersonIds is null);
                Check("C8j: the career's season one and the legacy season are the same games (the career path adds nothing to season one)",
                      GamesDigest(stockOne) == GamesDigest(legacy));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 105 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 105 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }

    /// <summary>Rewrite a finalized log's roster section from schema 2 (256-byte entries) to schema 1
    /// (216-byte entries): the first 204 bytes of every entry are the v1 layout, so each entry is its
    /// prefix plus 12 zero bytes; the header's version and entry size move, the roster checksum and
    /// the footer's payload digest are recomputed, and every game block is carried unchanged. This is
    /// the ONLY way a pre-S115 log can exist in the suite now that the writer emits v2.</summary>
    private static byte[] DowngradeRosterToV1(byte[] file)
    {
        const int header = 128, rosterHeader = 32, trailer = 8, v2 = 256, v1 = 216, keep = 204, footer = 64;
        var count = BitConverter.ToInt32(file, header + 8);
        var v2Section = rosterHeader + count * v2 + trailer;
        var v1Section = rosterHeader + count * v1 + trailer;
        var outLen = file.Length - v2Section + v1Section;
        var o = new byte[outLen];
        Array.Copy(file, 0, o, 0, header + rosterHeader);
        BitConverter.GetBytes((short)1).CopyTo(o, header + 4);          // roster schema version
        BitConverter.GetBytes((short)v1).CopyTo(o, header + 6);         // entry size
        for (var i = 0; i < count; i++)
            Array.Copy(file, header + rosterHeader + i * v2, o, header + rosterHeader + i * v1, keep);
        var rosterEnd = header + rosterHeader + count * v1;
        SHA256.HashData(o.AsSpan(header, rosterEnd - header))[..8].CopyTo(o, rosterEnd);
        Array.Copy(file, header + v2Section, o, header + v1Section, file.Length - header - v2Section);
        // Footer payload digest: SHA-256 of everything after the file header up to the footer.
        var digestAt = outLen - footer + 4 + 4 + 8;
        SHA256.HashData(o.AsSpan(header, outLen - footer - header)).CopyTo(o, digestAt);
        return o;
    }
}
