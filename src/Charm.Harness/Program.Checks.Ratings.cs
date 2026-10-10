using System.Security.Cryptography;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 102 — S112: THE SEASON RECORD AND THE FIRST RATING.
//
//  The session asserts WIRING, never basketball: no school, band or conference is
//  required to land anywhere. What is proven:
//    C1  the record reconciles (every game twice, records, points, possessions, categories)
//    C2  the possession split is a real count, and the stock season can catch a /2 shortcut
//    C3  ★ A0 — home court comes from the home-and-homes, and the naive fit differs
//    C4  three sites are three: neutral games are untouched
//    C5  convergence is reached, reported, and a capped run is refused
//    C6  order invariance, bit for bit
//    C7  the adjustment adjusts (synthetic worlds with a known truth)
//    C8  A6 connectivity, and the empty school
//    C9  identity: the national averages, and the rated means after renormalization
//    C10 determinism, and the rating sees no game id
//    C11 the fingerprint wall — this session plays no basketball
//    C12 negative controls, each firing the rule it names
// ============================================================================
internal static partial class Program
{
    private const long RatingCheckSeed = 20260720;

    /// <summary>★ PRE-EDIT CAPTURE (S112 gate probe, before any S112 code existed): every game of
    /// the stock season, "ordinal|home|away|homeScore|awayScore|possessions\n", SHA-256.</summary>
    // ★ S118.1 — the scramble foul's man (O-117); declared before the build; was d4c356fbc4b6542c759127f785cc6379a9226b2fd6d00a850c347ea326230a5b
    // ★ S120 — the free-throw lane (O-118); captured from the S120 build; was a34fda284c0a3b44415ba2450a32123f390caeb860c854cd38935b49d32a3a7b
    private const string RatingGoldenPreS112GameDigest =
        "50f297da4510618fbfc1a3343a775fee89fe0023056f605097e55dab19029af7";

    /// <summary>★ PRE-EDIT CAPTURE of each side's possessions, counted independently by the gate
    /// probe: "ordinal|home|away\n", SHA-256. The carried split must equal it.</summary>
    // ★ S118.1 — the scramble foul's man (O-117); declared before the build; was 1c31ab849664fc79ed7030966eeb1323faf26478bb698616aff5eacaa41f7979
    // ★ S120 — the free-throw lane (O-118); captured from the S120 build; was c5a3465b1737a00ce90cfaf3cd4899e1288e6704c5db166041ff4ecb5af8bd00
    private const string RatingGoldenPreS112SplitDigest =
        "93f10ce45ff132fa31100557b1970a4045caa793cb4f6fc0b11ce64afb6acaf3";

    /// <summary>The seventh fingerprint as S111 shipped it (journal: bcb4b5d8…), pinned in full.</summary>
    // ★ S118.1 — the scramble foul's man (O-117); declared before the build; was f59b6e5910eff99eee22401d4a4611aaf98af24f296074fd5c487a37a64137f9
    // ★ S120 — the free-throw lane (O-118); captured from the S120 build; was 51be2b2c80f7096208ad1a1191e759be7af88d7fa0a5a72fe0b4e5d4c3398e8b
    private const string RatingGoldenBuyGamesFp =
        "68b4347739cc1bfd7da184d1ab5e5f130c34bfa9d2be6cc1b741d27464d326e6";

    private static string RatingSha(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>A small synthetic league with a KNOWN truth: offense a against defense b scores
    /// <paramref name="eff"/>(a,b) per 100 on a neutral floor, times h at home and ÷h on the road.
    /// Possessions alternate 70/71 so the two sides differ. No RNG.</summary>
    private static List<RecordGameInput> RatingSynthGames(
        IEnumerable<(int Home, int Away, bool HasHost)> fixtures, Func<int, int, double> eff, double h)
    {
        var games = new List<RecordGameInput>();
        var i = 0;
        foreach (var (home, away, hasHost) in fixtures)
        {
            var hp = 70 + (i % 2); var ap = 70 + ((i + 1) % 2);
            var hm = hasHost ? h : 1.0; var am = hasHost ? 1.0 / h : 1.0;
            games.Add(new RecordGameInput(i, home, away, hasHost, RecordCatLeague, null,
                (int)Math.Round(hp * eff(home, away) * hm / 100.0, MidpointRounding.AwayFromZero),
                (int)Math.Round(ap * eff(away, home) * am / 100.0, MidpointRounding.AwayFromZero),
                hp, ap, 0));
            i++;
        }
        return games;
    }

    /// <summary>Every pair home-and-home, plus a neutral meeting where (a+b) % 3 == 0.</summary>
    private static List<(int, int, bool)> RatingSynthFixtures(IReadOnlyList<int> ids, Func<int, int, bool>? plays = null)
    {
        var f = new List<(int, int, bool)>();
        for (var x = 0; x < ids.Count; x++)
            for (var y = x + 1; y < ids.Count; y++)
            {
                int a = ids[x], b = ids[y];
                if (plays is not null && !plays(a, b)) continue;
                f.Add((a, b, true)); f.Add((b, a, true));
                if ((a + b) % 3 == 0) f.Add((a, b, false));
            }
        return f;
    }

    private static bool Phase102RatingsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 102 — S112: the season record and the first rating. The record reconciles, the " +
                          "possession split is counted not halved, home court is fitted from the home-and-homes and " +
                          "differs from the naive figure, neutral is neutral, convergence is owned and refused when " +
                          "capped, order invariance, the adjustment adjusts against a known truth, connectivity and " +
                          "the empty school, identity, the fingerprint wall, and a negative control per rule ==");
        var pass = true;
        var assertions = 0;

        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        string? Refusal(Action act)
        {
            try { act(); return null; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }

        bool Fires(string? fault, string rule) => fault is not null && fault.StartsWith(rule + ":", StringComparison.Ordinal);

        static bool SameRatings(SeasonRatings a, SeasonRatings b)
            => a.Ranked.Count == b.Ranked.Count
               && a.Ranked.Zip(b.Ranked).All(p => p.First == p.Second)
               && a.HomeFactor.Equals(b.HomeFactor) && a.Iterations == b.Iterations;

        static double MaxNetMove(SeasonRatings a, SeasonRatings b)
        {
            var m = b.Ranked.ToDictionary(r => r.SchoolId, r => r.AdjNet);
            return a.Ranked.Max(r => Math.Abs(r.AdjNet - m[r.SchoolId]));
        }

        try
        {
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var run = RunSeasonCore(stock, RatingCheckSeed, configPath, verbose: false);
            var schoolIds = stock.Schools.Select(s => s.Id).ToList();
            var inputs = RecordInputsFromRun(run);
            var rec = BuildSeasonRecord(inputs, schoolIds);
            var r = ComputeSeasonRatings(rec);
            var n = run.PlayedGames.Count;

            // ── C1: the record reconciles ───────────────────────────────────────
            {
                var fault = SeasonRecordFault(rec);
                Check("C1a: every game appears exactly twice, once per side, and the two sides mirror",
                      fault is null, fault ?? $"{n} games, {rec.Schools.Sum(id => rec.Games[id].Count)} entries");
                var wlOk = schoolIds.All(id =>
                    rec.Games[id].Count(e => e.Won) == run.Wins[id]
                    && rec.Games[id].Count(e => !e.Won) == run.Losses[id]);
                Check("C1b: every school's derived W-L equals the season's own standings", wlOk && run.Ties == 0);
                long pf = rec.Schools.Sum(id => rec.Games[id].Sum(e => (long)e.PointsFor));
                long pts = run.Results.Sum(x => (long)x.HomeScore + x.AwayScore);
                long pp = rec.Schools.Sum(id => rec.Games[id].Sum(e => (long)e.PossessionsFor));
                long poss = run.PossessionCounts.Sum(x => (long)x);
                Check("C1c: points and possessions reconcile to the outcome object", pf == pts && pp == poss,
                      $"{pts} points, {poss} possessions");
                var cats = inputs.GroupBy(g => g.Category).ToDictionary(g => g.Key, g => g.Count());
                int C(string k) => cats.GetValueOrDefault(k, 0);
                Check("C1d: the four categories match the season's own counts and sum to every game played",
                      C(RecordCatLeague) == run.ConferenceGameCount
                      && C(RecordCatEvent) == run.TournamentGameCount
                      && C(RecordCatConfTourney) == run.ConferenceTournamentGameCount
                      && C(RecordCatBuy) == run.BuyGameCount
                      && cats.Values.Sum() == n,
                      $"league {C(RecordCatLeague)}, event {C(RecordCatEvent)}, ctourney {C(RecordCatConfTourney)}, " +
                      $"buy {C(RecordCatBuy)} = {n}");
                Check("C1e: sites from the site fact — neutral games have no home team",
                      inputs.Count(g => !g.HasHost) == run.PlayedGames.Count(p => !p.Game.HasHost)
                      && rec.Schools.All(id => rec.Games[id].All(e =>
                          (e.Site == RecordSite.Neutral) == !inputs[e.Ordinal].HasHost)),
                      $"{inputs.Count(g => !g.HasHost)} neutral");
            }

            // ── C2: the possession split ────────────────────────────────────────
            {
                Check("C2a: the two sides sum to the recorded total in every game",
                      run.SidePossessions.Zip(run.PossessionCounts).All(p => p.First.Home + p.First.Away == p.Second));
                var unequal = run.SidePossessions.Count(s => s.Home != s.Away);
                Check("C2b: ★ the stock season has games where the sides are UNEQUAL, so a /2 shortcut is catchable",
                      unequal > 0, $"{unequal} of {n}");
                var split = RatingSha(string.Concat(run.SidePossessions.Select((s, i) => $"{i}|{s.Home}|{s.Away}\n")));
                Check("C2c: the carried split equals the S120 capture of each side's possessions, game for game",
                      split == RatingGoldenPreS112SplitDigest, split[..16]);
                var halved = inputs.Select(g => g with
                {
                    HomePossessions = (g.HomePossessions + g.AwayPossessions) / 2,
                    AwayPossessions = (g.HomePossessions + g.AwayPossessions) / 2,
                }).ToList();
                Check("C2d: NEGATIVE CONTROL — halving the total moves the ratings",
                      MaxNetMove(ComputeSeasonRatings(BuildSeasonRecord(halved, schoolIds)), r) > 0.01);
            }

            // ── C3: ★ A0 — home court ───────────────────────────────────────────
            {
                Console.WriteLine($"    home factor {r.HomeFactor:F5} from {r.HomePairs} pairs " +
                                  $"({r.HomePairsLeague} conference, {r.HomePairs - r.HomePairsLeague} non-conference); " +
                                  $"naive {r.NaiveHomeFactor:F5}");
                Check("C3a: home court is fitted from BOTH kinds of home-and-home (Emmett's ruling)",
                      r.HomePairsLeague > 0 && r.HomePairs > r.HomePairsLeague);
                Check("C3b: the fitted figure is a home advantage, not a penalty", r.HomeFactor > 1.0);
                Check("C3c: ★ the fair fit and the naive home-vs-road figure DIFFER on the stock season",
                      Math.Abs(r.NaiveHomeFactor - r.HomeFactor) > 0.005,
                      $"{r.HomeFactor:F4} vs {r.NaiveHomeFactor:F4}");
                var naive = ComputeSeasonRatings(rec, new RatingOptions(NaiveHomeFit: true));
                Check("C3d: NEGATIVE CONTROL — fitting the naive way moves the ratings",
                      MaxNetMove(naive, r) > 0.1, $"largest net move {MaxNetMove(naive, r):F2}");
                // A pair with a neutral meeting only is never a home-and-home.
                var synthIds = Enumerable.Range(1, 6).ToList();
                var oneLeg = RatingSynthGames(new[] { (1, 2, true), (2, 1, false), (3, 4, true), (4, 3, true),
                                                      (1, 3, true), (2, 4, true), (5, 6, false), (5, 1, true),
                                                      (6, 2, true) }, (a, b) => 100, 1.03);
                Check("C3e: a home game plus a NEUTRAL rematch is not a home-and-home",
                      RatingHomeFit(BuildSeasonRecord(oneLeg, synthIds)).Pairs == 1);
            }

            // ── C4: three sites are three ───────────────────────────────────────
            {
                var nh = ComputeSeasonRatings(rec, new RatingOptions(NeutralAsHome: true));
                Check("C4a: NEGATIVE CONTROL — treating neutral games as home games moves the ratings",
                      MaxNetMove(nh, r) > 0.01, $"largest net move {MaxNetMove(nh, r):F2}");
                // A neutral-only game between two equal teams leaves them equal.
                var ids = new[] { 1, 2, 3 };
                var g = RatingSynthGames(new[] { (1, 2, true), (2, 1, true), (1, 3, false), (2, 3, false),
                                                 (3, 1, true), (1, 3, true) }, (a, b) => 100, 1.05);
                var s = ComputeSeasonRatings(BuildSeasonRecord(g, ids));
                Check("C4b: a 1.05 home factor planted in a league with neutral games mixed in is recovered " +
                      "from the home-and-homes alone",
                      Math.Abs(s.HomeFactor - 1.05) < 0.01, $"{s.HomeFactor:F4}");
            }

            // ── C5: convergence ─────────────────────────────────────────────────
            {
                Check("C5a: converged inside the cap, rounds reported",
                      r.Iterations < RatingMaxIterations && r.TempoIterations < RatingMaxIterations,
                      $"{r.Iterations} efficiency, {r.TempoIterations} tempo");
                var capped = Refusal(() => ComputeSeasonRatings(rec, new RatingOptions(MaxIterations: 5)));
                Check("C5b: ★ a run capped below convergence is REFUSED, never returned half-settled",
                      Fires(capped, "RATING-NOT-CONVERGED"), capped ?? "returned");
            }

            // ── C6: order invariance ────────────────────────────────────────────
            {
                var rng = new Random(112);
                var shuffled = inputs.OrderBy(_ => rng.Next()).ToList();
                var ids = schoolIds.OrderBy(_ => rng.Next()).ToList();
                var again = ComputeSeasonRatings(BuildSeasonRecord(shuffled, ids));
                Check("C6a: ★ shuffling the games and the schools changes nothing, bit for bit", SameRatings(again, r));
            }

            // ── C7: the adjustment adjusts — a known truth ──────────────────────
            var twelve = Enumerable.Range(1, 12).ToList();
            double TrueO(int i) => 89 + 2 * ((i * 7) % 12);
            double TrueD(int i) => 89 + 2 * ((i * 5) % 12);
            double Eff(int a, int b) => TrueO(a) * TrueD(b) / 100.0;
            {
                var truth = BuildSeasonRecord(RatingSynthGames(RatingSynthFixtures(twelve), Eff, 1.03), twelve);
                double MaxErr(SeasonRatings s)
                {
                    var mo = twelve.Average(TrueO); var md = twelve.Average(TrueD);
                    return s.Ranked.Max(x => Math.Max(
                        Math.Abs(x.AdjO - TrueO(x.SchoolId) * s.NationalEfficiency / mo),
                        Math.Abs(x.AdjD - TrueD(x.SchoolId) * s.NationalEfficiency / md)));
                }
                var t = ComputeSeasonRatings(truth);
                Check("C7a: a synthetic league's true offense and defense are recovered within a point",
                      MaxErr(t) < 1.0 && Math.Abs(t.HomeFactor - 1.03) < 0.005,
                      $"worst error {MaxErr(t):F3}, home {t.HomeFactor:F4}");
                var flipped = ComputeSeasonRatings(truth, new RatingOptions(FlipAdjustmentSide: true));
                Check("C7b: NEGATIVE CONTROL — judging offense against the opponent's OFFENSE fails recovery",
                      MaxErr(flipped) > 1.0, $"worst error {MaxErr(flipped):F3}");

                // School 1 plays only 2..7. Strengthen 2..7 ONLY in games not involving school 1;
                // school 1's own games are byte-identical.
                bool Plays(int a, int b) => (a != 1 && b != 1) || Math.Max(a, b) <= 7;
                var fixtures = RatingSynthFixtures(twelve, Plays);
                bool Faced(int x) => x is >= 2 and <= 7;
                var baseRec = BuildSeasonRecord(RatingSynthGames(fixtures, Eff, 1.03), twelve);
                var toughD = BuildSeasonRecord(RatingSynthGames(fixtures,
                    (a, b) => a != 1 && b != 1 && Faced(b) ? Eff(a, b) * 0.9 : Eff(a, b), 1.03), twelve);
                var toughO = BuildSeasonRecord(RatingSynthGames(fixtures,
                    (a, b) => a != 1 && b != 1 && Faced(a) ? Eff(a, b) * 1.1 : Eff(a, b), 1.03), twelve);
                var own = baseRec.Games[1].SequenceEqual(toughD.Games[1]) && baseRec.Games[1].SequenceEqual(toughO.Games[1]);
                SchoolRating One(SeasonRecord x, RatingOptions? o = null) =>
                    ComputeSeasonRatings(x, o).Ranked.Single(s => s.SchoolId == 1);
                var b0 = One(baseRec); var bD = One(toughD); var bO = One(toughO);
                Check("C7c: ★ same scoring against tougher DEFENSES raises adjusted offense (raw does not move)",
                      own && bD.AdjO > b0.AdjO && bD.RawO == b0.RawO, $"AdjO {b0.AdjO:F2} → {bD.AdjO:F2}");
                Check("C7d: ★ same defending against tougher OFFENSES improves adjusted defense",
                      own && bO.AdjD < b0.AdjD && bO.RawD == b0.RawD, $"AdjD {b0.AdjD:F2} → {bO.AdjD:F2}");
                var fl = new RatingOptions(FlipAdjustmentSide: true);
                Check("C7e: NEGATIVE CONTROL — with the sides flipped, tougher defenses do NOT raise adjusted offense",
                      !(One(toughD, fl).AdjO > One(baseRec, fl).AdjO + 0.01),
                      $"{One(baseRec, fl).AdjO:F2} → {One(toughD, fl).AdjO:F2}");

                // SOS: one entry per game, so an opponent played twice counts twice.
                var s = ComputeSeasonRatings(truth);
                var net = s.Ranked.ToDictionary(x => x.SchoolId, x => x.AdjNet);
                var sosOk = s.Ranked.All(x => Math.Abs(x.Sos - truth.Games[x.SchoolId].Average(e => net[e.Opponent])) < 1e-9);
                Check("C7f: SOS is the average opponent net rating, one entry per game played", sosOk);
            }

            // ── C8: connectivity and the empty school ───────────────────────────
            {
                Check("C8a: the stock schedule is one connected group", RatingComponents(rec, r.Ranked.Select(x => x.SchoolId).ToList()) == 1);
                var split = RatingSynthGames(RatingSynthFixtures(new[] { 1, 2, 3, 4 })
                    .Concat(RatingSynthFixtures(new[] { 5, 6, 7, 8 })), (a, b) => 100, 1.03);
                var refused = Refusal(() => ComputeSeasonRatings(BuildSeasonRecord(split, Enumerable.Range(1, 8))));
                Check("C8b: ★ A6 CONTROL — a world split into two groups is REFUSED, never silently ranked",
                      Fires(refused, "RATING-DISCONNECTED"), refused ?? "ranked");
                var withIdle = ComputeSeasonRatings(BuildSeasonRecord(
                    RatingSynthGames(RatingSynthFixtures(twelve), Eff, 1.03), twelve.Append(99)));
                Check("C8c: ★ a school that played nothing is EXCLUDED by name, not rated 0.0 and ranked last",
                      withIdle.Excluded.SequenceEqual(new[] { 99 }) && withIdle.Ranked.All(x => x.SchoolId != 99));
                Check("C8d: every stock school played and is rated", r.Excluded.Count == 0 && r.Ranked.Count == schoolIds.Count,
                      $"{r.Ranked.Count} rated");
            }

            // ── C9: identity ────────────────────────────────────────────────────
            {
                var natl = 100.0 * run.Results.Sum(x => (long)x.HomeScore + x.AwayScore) / run.PossessionCounts.Sum(x => (long)x);
                Check("C9a: national efficiency = total points ÷ total possessions", Math.Abs(natl - r.NationalEfficiency) < 1e-9,
                      $"{r.NationalEfficiency:F3}");
                Check("C9b: after renormalization the rated means equal it, both sides of the ball",
                      Math.Abs(r.Ranked.Average(x => x.AdjO) - natl) < 1e-9
                      && Math.Abs(r.Ranked.Average(x => x.AdjD) - natl) < 1e-9);
                Check("C9c: adjusted tempo averages back to the national tempo",
                      Math.Abs(r.Ranked.Average(x => x.AdjT) - r.NationalTempo) < 1e-9, $"{r.NationalTempo:F2}");
            }

            // ── C10: determinism, and no game id ────────────────────────────────
            {
                Check("C10a: the same record rates identically twice", SameRatings(ComputeSeasonRatings(rec), r));
                var idTypes = typeof(RecordGameInput).GetProperties().Concat(typeof(RecordEntry).GetProperties())
                    .Count(p => p.PropertyType == typeof(GameId) || p.PropertyType == typeof(GameId?)
                             || p.PropertyType == typeof(SeasonId) || p.PropertyType == typeof(SeasonId?));
                Check("C10b: the rating's inputs carry no game or season id, so a career run and a legacy run " +
                      "cannot rate differently", idTypes == 0);
            }

            // ── C11: the fingerprint wall ───────────────────────────────────────
            {
                var prefix = run.ConferenceGameCount + run.TournamentGameCount;
                var resultsFp = SeasonFingerprint(run.Results.Take(prefix).ToList(), run.PossessionCounts.Take(prefix).ToList());
                Check("C11a: #1 conference schedule UNMOVED", run.Fingerprint == MatchGoldenConferenceFp);
                Check("C11b: #2 conference dated UNMOVED", run.DatedFingerprint == MatchGoldenDatedFp);
                Check("C11c: #3 event games UNMOVED", run.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C11d: #4 results+possessions UNMOVED over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C11e: #5 non-conference dated UNMOVED", run.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C11f: #6 conference tournaments UNMOVED", run.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C11g: #7 buy games UNMOVED", run.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                var games = RatingSha(string.Concat(run.Results.Select((x, i) =>
                    $"{i}|{x.HomeId}|{x.AwayId}|{x.HomeScore}|{x.AwayScore}|{run.PossessionCounts[i]}\n")));
                Check("C11h: ★ every one of the season's games identical to the S120 capture — teams, scores, " +
                      "possessions", games == RatingGoldenPreS112GameDigest, $"{n} games, {games[..16]}");
            }

            // ── C12: negative controls on the record ────────────────────────────
            {
                var victim = rec.Schools.First(id => rec.Games[id].Count > 0);
                var dropped = new SeasonRecord
                {
                    Schools = rec.Schools, Inputs = rec.Inputs,
                    Games = rec.Games.ToDictionary(kv => kv.Key,
                        kv => kv.Key == victim ? (IReadOnlyList<RecordEntry>)kv.Value.Skip(1).ToList() : kv.Value),
                };
                var f1 = SeasonRecordFault(dropped);
                Check("C12a: NEGATIVE CONTROL — one game dropped from one school's record is caught",
                      Fires(f1, "RECORD-RECONCILE"), f1 ?? "accepted");
                var f1b = Refusal(() => ComputeSeasonRatings(dropped));
                Check("C12b: ...and the rating refuses to rate it", Fires(f1b, "RECORD-RECONCILE"));
                var zeroed = inputs.Select((g, i) => i == 0 ? g with { HomePossessions = 0 } : g);
                var f2 = Refusal(() => BuildSeasonRecord(zeroed, schoolIds));
                Check("C12c: NEGATIVE CONTROL — a game with its possessions zeroed is refused",
                      Fires(f2, "RECORD-POSSESSIONS"), f2 ?? "accepted");
                var pw = ComputeSeasonRatings(rec, new RatingOptions(PossessionWeighted: true));
                Check("C12d: NEGATIVE CONTROL — possession weighting instead of one-game-one-vote moves the table",
                      MaxNetMove(pw, r) > 0.01, $"largest net move {MaxNetMove(pw, r):F3}");
            }
        }
        catch (InvalidOperationException ex)
        {
            Check("Phase 102 ran to completion", false, ex.Message);
        }

        Console.WriteLine($"  Phase 102: {assertions} assertions — {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
