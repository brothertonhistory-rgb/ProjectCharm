using System.Globalization;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S112 — THE SEASON RECORD, AND THE FIRST RATING.
//
//  TWO LAYERS, DELIBERATELY SEPARATE.
//
//  THE RECORD is the substrate every future ranking reads: for every school, every
//  game it played — opponent, site (Home / Away / Neutral), category, date, points for
//  and against, possessions for and against, overtime periods. It computes no
//  basketball and decides nothing. Win/loss is DERIVED from the points, never stored,
//  so a later selection metric cannot disagree with the standings.
//
//  THE RATING is one reader of it: adjusted offensive and defensive efficiency, net,
//  adjusted tempo and strength of schedule. It is PREDICTIVE, NOT AN AWARD — a 33-1
//  team may sit below an 18-13 team and that is the system working. ★ It must never
//  seed the national bracket; at-large selection belongs to the quad metric.
//
//  EMMETT'S RULINGS (2026-10-04):
//    1. NO RECENCY WEIGHTING. Rosters are static — nobody develops, nobody is hurt — so
//       a late game says nothing an early one does not. A deliberate divergence from
//       Pomeroy, not an omission: do not "fix" it.
//    2. EVERY GAME COUNTS — league, event, showcase, conference tournament, buy game.
//    3. MARGIN IS UNCAPPED. A forty-point win counts as forty.
//    4. HOME COURT IS MEASURED FROM EVERY SAME-SEASON HOME-AND-HOME — conference
//       rematches AND non-conference home-and-homes. The rating measures what OUR engine
//       does at home; making the engine's home court realistic is the engine dial's job.
//
//  ★ WHY HOME COURT IS NOT FITTED FROM HOME-VS-ROAD. Most buy games put the stronger
//  school in its own gym (1,558 of 2,078 hosted buy games on the stock season), so a
//  naive home-vs-road split credits the venue with the opponent gap — about 7 points a
//  game against a measured 4. The home-and-home legs are the same two schools in both
//  gyms, so the opponent cancels. The naive figure is printed beside the fair one as a
//  page-only diagnostic and is never an input.
//
//  CALL MADE (reversible): a pair counts only when its HOSTED meetings are equal at each
//  gym. A neutral meeting (an event, a conference tournament) never makes a home-and-home.
//
//  ★ ORDER INVARIANCE IS BY CONSTRUCTION: every sum walks schools by id and games by
//  fixture ordinal, so shuffling the input changes nothing to the last bit.
// ============================================================================
internal static partial class Program
{
    private enum RecordSite { Home, Away, Neutral }

    /// <summary>The four played-game categories, by the same predicates the season uses.</summary>
    private const string RecordCatLeague = "league";
    private const string RecordCatEvent = "event";
    private const string RecordCatConfTourney = "ctourney";
    private const string RecordCatBuy = "buy";

    /// <summary>One played game, both sides, as the record builder receives it. The season
    /// adapter produces these from the run; the suite's synthetic worlds produce them directly,
    /// so both go through exactly one builder.</summary>
    private sealed record RecordGameInput(
        int Ordinal, int HomeId, int AwayId, bool HasHost, string Category, DateOnly? Date,
        int HomeScore, int AwayScore, int HomePossessions, int AwayPossessions, int OvertimePeriods);

    /// <summary>One game from ONE school's side.</summary>
    private sealed record RecordEntry(
        int Ordinal, int Opponent, RecordSite Site, string Category, DateOnly? Date,
        int PointsFor, int PointsAgainst, int PossessionsFor, int PossessionsAgainst,
        int OvertimePeriods)
    {
        public bool Won => PointsFor > PointsAgainst;
    }

    private sealed class SeasonRecord
    {
        /// <summary>Every school asked about, including any that played nothing.</summary>
        public required IReadOnlyList<int> Schools { get; init; }
        /// <summary>Each school's games in fixture-ordinal order. A school that played nothing
        /// maps to an empty list — present, never missing.</summary>
        public required IReadOnlyDictionary<int, IReadOnlyList<RecordEntry>> Games { get; init; }
        public required IReadOnlyList<RecordGameInput> Inputs { get; init; }
    }

    // ── The record ────────────────────────────────────────────────────────────────

    /// <summary>★ The season adapter. Results, side possessions and played games line up index
    /// for index and index IS the fixture ordinal — asserted here rather than trusted.</summary>
    private static IReadOnlyList<RecordGameInput> RecordInputsFromRun(SeasonRunOutcome run)
    {
        var n = run.PlayedGames.Count;
        if (run.Results.Count != n || run.PossessionCounts.Count != n || run.SidePossessions.Count != n)
            throw new InvalidOperationException(
                $"RECORD-MISALIGNED: {n} played games, {run.Results.Count} results, " +
                $"{run.PossessionCounts.Count} possession counts, {run.SidePossessions.Count} side splits.");
        var inputs = new List<RecordGameInput>(n);
        for (var i = 0; i < n; i++)
        {
            var pg = run.PlayedGames[i];
            var r = run.Results[i];
            var sp = run.SidePossessions[i];
            if (pg.FixtureOrdinal != i || r.HomeId != pg.Game.HomeId || r.AwayId != pg.Game.AwayId)
                throw new InvalidOperationException(
                    $"RECORD-MISALIGNED: index {i} holds ordinal {pg.FixtureOrdinal}, played " +
                    $"{pg.Game.HomeId}-{pg.Game.AwayId}, result {r.HomeId}-{r.AwayId}.");
            if (sp.Home + sp.Away != run.PossessionCounts[i])
                throw new InvalidOperationException(
                    $"RECORD-POSSESSIONS: game {i}: sides {sp.Home}+{sp.Away} do not sum to the recorded " +
                    $"{run.PossessionCounts[i]}.");
            var cat = pg.IsEventGame ? RecordCatEvent
                    : pg.IsConferenceTournamentGame ? RecordCatConfTourney
                    : pg.IsBuyGame ? RecordCatBuy
                    : RecordCatLeague;
            inputs.Add(new RecordGameInput(i, r.HomeId, r.AwayId, pg.Game.HasHost, cat, pg.Game.Date,
                                           r.HomeScore, r.AwayScore, sp.Home, sp.Away, r.OvertimePeriods));
        }
        return inputs;
    }

    private static SeasonRecord BuildSeasonRecord(IEnumerable<RecordGameInput> games, IEnumerable<int> schoolIds)
    {
        var schools = schoolIds.Distinct().OrderBy(x => x).ToList();
        var known = new HashSet<int>(schools);
        var lists = schools.ToDictionary(id => id, _ => new List<RecordEntry>());
        var inputs = games.OrderBy(g => g.Ordinal).ToList();
        for (var i = 1; i < inputs.Count; i++)
            if (inputs[i].Ordinal == inputs[i - 1].Ordinal)
                throw new InvalidOperationException($"RECORD-DUPLICATE: ordinal {inputs[i].Ordinal} appears twice.");
        foreach (var g in inputs)
        {
            if (!known.Contains(g.HomeId) || !known.Contains(g.AwayId) || g.HomeId == g.AwayId)
                throw new InvalidOperationException(
                    $"RECORD-SCHOOL: game {g.Ordinal} names {g.HomeId} and {g.AwayId}.");
            if (g.HomePossessions <= 0 || g.AwayPossessions <= 0)
                throw new InvalidOperationException(
                    $"RECORD-POSSESSIONS: game {g.Ordinal} has {g.HomePossessions}/{g.AwayPossessions} " +
                    "possessions; efficiency needs both sides to have the ball.");
            if (g.OvertimePeriods < 0 || g.HomeScore < 0 || g.AwayScore < 0)
                throw new InvalidOperationException($"RECORD-VALUE: game {g.Ordinal} carries a negative value.");
            var homeSite = g.HasHost ? RecordSite.Home : RecordSite.Neutral;
            var awaySite = g.HasHost ? RecordSite.Away : RecordSite.Neutral;
            lists[g.HomeId].Add(new RecordEntry(g.Ordinal, g.AwayId, homeSite, g.Category, g.Date,
                g.HomeScore, g.AwayScore, g.HomePossessions, g.AwayPossessions, g.OvertimePeriods));
            lists[g.AwayId].Add(new RecordEntry(g.Ordinal, g.HomeId, awaySite, g.Category, g.Date,
                g.AwayScore, g.HomeScore, g.AwayPossessions, g.HomePossessions, g.OvertimePeriods));
        }
        return new SeasonRecord
        {
            Schools = schools,
            Games = lists.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<RecordEntry>)kv.Value),
            Inputs = inputs,
        };
    }

    /// <summary>★ The reconciliation the suite asserts and the rating relies on: every game
    /// appears exactly twice, once from each side, and the two sides mirror. Null when whole.</summary>
    private static string? SeasonRecordFault(SeasonRecord rec)
    {
        var seen = new Dictionary<int, List<(int School, RecordEntry E)>>();
        foreach (var id in rec.Schools)
            foreach (var e in rec.Games[id])
            {
                if (!seen.TryGetValue(e.Ordinal, out var l)) seen[e.Ordinal] = l = new();
                l.Add((id, e));
            }
        foreach (var g in rec.Inputs)
        {
            if (!seen.TryGetValue(g.Ordinal, out var sides) || sides.Count != 2)
                return $"RECORD-RECONCILE: game {g.Ordinal} appears {(sides?.Count ?? 0)} time(s), not twice.";
            var (sa, a) = sides[0]; var (sb, b) = sides[1];
            if (a.Opponent != sb || b.Opponent != sa || a.PointsFor != b.PointsAgainst
                || a.PossessionsFor != b.PossessionsAgainst || a.OvertimePeriods != b.OvertimePeriods)
                return $"RECORD-RECONCILE: game {g.Ordinal}'s two sides do not mirror.";
        }
        if (seen.Count != rec.Inputs.Count)
            return $"RECORD-RECONCILE: {seen.Count} games in the record against {rec.Inputs.Count} played.";
        return null;
    }

    // ── The rating ────────────────────────────────────────────────────────────────

    private const double RatingTolerance = 1e-9;
    private const int RatingMaxIterations = 1000;
    private const double RegulationMinutes = 40.0;
    private const double OvertimeMinutes = 5.0;

    /// <summary>Switches that exist ONLY for Phase 102's negative controls. The page and every
    /// production caller use <see cref="Default"/>.</summary>
    private sealed record RatingOptions(
        bool NaiveHomeFit = false, bool NeutralAsHome = false, bool PossessionWeighted = false,
        bool FlipAdjustmentSide = false, int MaxIterations = RatingMaxIterations)
    {
        public static readonly RatingOptions Default = new();
    }

    private sealed record SchoolRating(
        int SchoolId, int Games, int Wins, int Losses,
        double RawO, double RawD, double AdjO, double AdjD, double AdjT, double Sos)
    {
        public double RawNet => RawO - RawD;
        public double AdjNet => AdjO - AdjD;
    }

    private sealed class SeasonRatings
    {
        public required double HomeFactor { get; init; }
        public required int HomePairs { get; init; }
        public required int HomePairsLeague { get; init; }
        public required double NaiveHomeFactor { get; init; }
        public required double NationalEfficiency { get; init; }
        public required double NationalTempo { get; init; }
        public required int Iterations { get; init; }
        public required int TempoIterations { get; init; }
        /// <summary>Ranked by adjusted net, best first; ties by lower school id.</summary>
        public required IReadOnlyList<SchoolRating> Ranked { get; init; }
        /// <summary>Schools that played nothing. Never rated, never ranked last at 0.0.</summary>
        public required IReadOnlyList<int> Excluded { get; init; }
    }

    private static double RecordTempo(RecordEntry e)
        => e.PossessionsFor * RegulationMinutes / (RegulationMinutes + OvertimeMinutes * e.OvertimePeriods);

    /// <summary>★ The fair home-court fit and the naive diagnostic. h² = (home points per
    /// possession) ÷ (road points per possession) over the chosen hosted games, so a home
    /// performance divided by h and a road one multiplied by h meet at neutral.</summary>
    private static (double Fair, int Pairs, int LeaguePairs, double Naive) RatingHomeFit(SeasonRecord rec)
    {
        var byPair = new SortedDictionary<(int Lo, int Hi), List<RecordGameInput>>();
        foreach (var g in rec.Inputs)
        {
            if (!g.HasHost) continue;
            var key = (Math.Min(g.HomeId, g.AwayId), Math.Max(g.HomeId, g.AwayId));
            if (!byPair.TryGetValue(key, out var l)) byPair[key] = l = new();
            l.Add(g);
        }
        static double Factor(IEnumerable<RecordGameInput> games)
        {
            long hp = 0, hpts = 0, rp = 0, rpts = 0;
            foreach (var g in games.OrderBy(x => x.Ordinal))
            { hpts += g.HomeScore; hp += g.HomePossessions; rpts += g.AwayScore; rp += g.AwayPossessions; }
            if (hp == 0 || rp == 0 || rpts == 0) return double.NaN;
            return Math.Sqrt(((double)hpts / hp) / ((double)rpts / rp));
        }
        var legs = new List<RecordGameInput>();
        int pairs = 0, leaguePairs = 0;
        foreach (var (key, games) in byPair)
        {
            var atLo = games.Count(x => x.HomeId == key.Lo);
            var atHi = games.Count - atLo;
            if (atLo == 0 || atLo != atHi) continue;
            pairs++;
            if (games.All(x => x.Category == RecordCatLeague)) leaguePairs++;
            legs.AddRange(games);
        }
        return (Factor(legs), pairs, leaguePairs, Factor(rec.Inputs.Where(x => x.HasHost)));
    }

    /// <summary>★ A6 — the opponent graph over rated schools. More than one component means the
    /// sets were never measured against each other, and cross-set ranks would mean nothing.</summary>
    private static int RatingComponents(SeasonRecord rec, IReadOnlyList<int> rated)
    {
        var parent = rated.ToDictionary(x => x, x => x);
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var id in rated)
            foreach (var e in rec.Games[id])
            {
                var a = Find(id); var b = Find(e.Opponent);
                if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
            }
        return rated.Select(Find).Distinct().Count();
    }

    private static SeasonRatings ComputeSeasonRatings(SeasonRecord rec, RatingOptions? options = null)
    {
        var opt = options ?? RatingOptions.Default;
        var fault = SeasonRecordFault(rec);
        if (fault is not null) throw new InvalidOperationException(fault);

        var rated = rec.Schools.Where(id => rec.Games[id].Count > 0).ToList();
        var excluded = rec.Schools.Where(id => rec.Games[id].Count == 0).ToList();
        if (rated.Count < 2)
            throw new InvalidOperationException($"RATING-EMPTY: {rated.Count} school(s) played a game; nothing to rate.");
        var components = RatingComponents(rec, rated);
        if (components != 1)
            throw new InvalidOperationException(
                $"RATING-DISCONNECTED: the schedule splits the {rated.Count} schools into {components} groups that " +
                "never played each other; ratings across groups would be meaningless, so none are produced.");

        var (fair, pairs, leaguePairs, naive) = RatingHomeFit(rec);
        if (pairs == 0 || double.IsNaN(fair))
            throw new InvalidOperationException(
                "RATING-NO-HOME-PAIRS: no school pair met once in each gym, so home court cannot be measured.");
        var h = opt.NaiveHomeFit ? naive : fair;

        long totalPts = 0, totalPoss = 0;
        foreach (var g in rec.Inputs)
        { totalPts += g.HomeScore + g.AwayScore; totalPoss += g.HomePossessions + g.AwayPossessions; }
        var avg = 100.0 * totalPts / totalPoss;

        // Per game, site-neutral efficiencies, in ordinal order.
        var obs = new Dictionary<int, (int Opp, double O, double D, double WO, double WD)[]>();
        foreach (var id in rated)
            obs[id] = rec.Games[id].OrderBy(e => e.Ordinal).Select(e =>
            {
                var oe = 100.0 * e.PointsFor / e.PossessionsFor;
                var de = 100.0 * e.PointsAgainst / e.PossessionsAgainst;
                var site = opt.NeutralAsHome && e.Site == RecordSite.Neutral ? RecordSite.Home : e.Site;
                if (site == RecordSite.Home) { oe /= h; de *= h; }
                else if (site == RecordSite.Away) { oe *= h; de /= h; }
                return (e.Opponent, oe, de,
                        opt.PossessionWeighted ? (double)e.PossessionsFor : 1.0,
                        opt.PossessionWeighted ? (double)e.PossessionsAgainst : 1.0);
            }).ToArray();

        var O = rated.ToDictionary(x => x, _ => avg);
        var D = rated.ToDictionary(x => x, _ => avg);
        var iterations = 0;
        for (var converged = false; !converged;)
        {
            if (++iterations > opt.MaxIterations)
                throw new InvalidOperationException(
                    $"RATING-NOT-CONVERGED: efficiency still moving after {opt.MaxIterations} rounds; " +
                    "a half-settled table is never returned.");
            var nO = new Dictionary<int, double>(); var nD = new Dictionary<int, double>();
            foreach (var id in rated)
            {
                double so = 0, sd = 0, wo = 0, wd = 0;
                foreach (var (opp, oe, de, w1, w2) in obs[id])
                {
                    // The offense is judged against the defense it faced, and vice versa.
                    var oppD = opt.FlipAdjustmentSide ? O[opp] : D[opp];
                    var oppO = opt.FlipAdjustmentSide ? D[opp] : O[opp];
                    so += w1 * oe * avg / oppD; wo += w1;
                    sd += w2 * de * avg / oppO; wd += w2;
                }
                nO[id] = so / wo; nD[id] = sd / wd;
            }
            // ★ Renormalize every round: the rated means equal the observed national efficiency.
            double mo = 0, md = 0;
            foreach (var id in rated) { mo += nO[id]; md += nD[id]; }
            mo /= rated.Count; md /= rated.Count;
            var change = 0.0;
            foreach (var id in rated)
            {
                nO[id] *= avg / mo; nD[id] *= avg / md;
                change = Math.Max(change, Math.Max(Math.Abs(nO[id] - O[id]), Math.Abs(nD[id] - D[id])));
            }
            O = nO; D = nD;
            converged = change < RatingTolerance;
        }

        // Tempo: the same multiplicative shape, its own loop, never an input to net.
        var tempoObs = rated.ToDictionary(id => id,
            id => rec.Games[id].OrderBy(e => e.Ordinal).Select(e => (e.Opponent, T: RecordTempo(e))).ToArray());
        double tSum = 0; long tN = 0;
        foreach (var id in rated) foreach (var (_, t) in tempoObs[id]) { tSum += t; tN++; }
        var avgT = tSum / tN;
        var T = rated.ToDictionary(x => x, _ => avgT);
        var tIterations = 0;
        for (var converged = false; !converged;)
        {
            if (++tIterations > opt.MaxIterations)
                throw new InvalidOperationException(
                    $"RATING-NOT-CONVERGED: tempo still moving after {opt.MaxIterations} rounds.");
            var nT = new Dictionary<int, double>();
            foreach (var id in rated)
            {
                double s = 0;
                foreach (var (opp, t) in tempoObs[id]) s += t * avgT / T[opp];
                nT[id] = s / tempoObs[id].Length;
            }
            var m = 0.0; foreach (var id in rated) m += nT[id]; m /= rated.Count;
            var change = 0.0;
            foreach (var id in rated)
            { nT[id] *= avgT / m; change = Math.Max(change, Math.Abs(nT[id] - T[id])); }
            T = nT;
            converged = change < RatingTolerance;
        }

        var ratings = new List<SchoolRating>();
        foreach (var id in rated)
        {
            var games = rec.Games[id].OrderBy(e => e.Ordinal).ToList();
            double ro = 0, rd = 0, sos = 0;
            foreach (var e in games)
            {
                ro += 100.0 * e.PointsFor / e.PossessionsFor;
                rd += 100.0 * e.PointsAgainst / e.PossessionsAgainst;
                sos += O[e.Opponent] - D[e.Opponent];   // ★ SOS: one entry per game, at neutral
            }
            var wins = games.Count(e => e.Won);
            ratings.Add(new SchoolRating(id, games.Count, wins, games.Count - wins,
                ro / games.Count, rd / games.Count, O[id], D[id], T[id], sos / games.Count));
        }
        return new SeasonRatings
        {
            HomeFactor = h, HomePairs = pairs, HomePairsLeague = leaguePairs, NaiveHomeFactor = naive,
            NationalEfficiency = avg, NationalTempo = avgT,
            Iterations = iterations, TempoIterations = tIterations,
            Ranked = ratings.OrderByDescending(r => r.AdjNet).ThenBy(r => r.SchoolId).ToList(),
            Excluded = excluded,
        };
    }

    // ── The page ──────────────────────────────────────────────────────────────────

    private static void PrintSeasonRatingsPage(SeasonRunOutcome run, WorldFile world)
    {
        var names = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        var confOf = world.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
        var confName = world.Conferences.ToDictionary(c => c.Id, c => c.ShortName);
        SeasonRatings r;
        try
        {
            r = ComputeSeasonRatings(BuildSeasonRecord(RecordInputsFromRun(run), world.Schools.Select(s => s.Id)));
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("--- EFFICIENCY RATINGS: not produced ---");
            Console.WriteLine($"  {ex.Message}");
            return;
        }
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine($"--- EFFICIENCY RATINGS (predictive, every game counts, margin uncapped; " +
                          $"not a selection metric) ---");
        Console.WriteLine(string.Format(ci,
            "  Home court: {0:F4} each way (home edge {1:F1}%), from {2} same-season home-and-homes " +
            "({3} conference, {4} non-conference). Naive home-vs-road would read {5:F4} ({6:F1}%) — diagnostic only.",
            r.HomeFactor, 100 * (r.HomeFactor * r.HomeFactor - 1), r.HomePairs, r.HomePairsLeague,
            r.HomePairs - r.HomePairsLeague, r.NaiveHomeFactor, 100 * (r.NaiveHomeFactor * r.NaiveHomeFactor - 1)));
        Console.WriteLine(string.Format(ci,
            "  National: {0:F1} points per 100 possessions, {1:F1} possessions per 40 minutes. " +
            "Settled in {2} rounds (efficiency), {3} (tempo).",
            r.NationalEfficiency, r.NationalTempo, r.Iterations, r.TempoIterations));
        if (r.Excluded.Count > 0)
            Console.WriteLine($"  Not rated (played no games): " +
                              string.Join(", ", r.Excluded.Select(id => names[id])));
        void Header() => Console.WriteLine(
            $"  {"Rk",-4}{"School",-26}{"Conf",-16}{"W-L",-7}{"Net",8}{"AdjO",8}{"AdjD",8}{"AdjT",7}{"SOS",8}");
        void Row(int rank, SchoolRating s) => Console.WriteLine(string.Format(ci,
            "  {0,-4}{1,-26}{2,-16}{3,-7}{4,8:+0.0;-0.0}{5,8:F1}{6,8:F1}{7,7:F1}{8,8:+0.0;-0.0}",
            rank, names[s.SchoolId], confName[confOf[s.SchoolId]], $"{s.Wins}-{s.Losses}",
            s.AdjNet, s.AdjO, s.AdjD, s.AdjT, s.Sos));
        Console.WriteLine();
        Console.WriteLine("  TOP 25");
        Header();
        for (var i = 0; i < Math.Min(25, r.Ranked.Count); i++) Row(i + 1, r.Ranked[i]);
        Console.WriteLine();
        Console.WriteLine($"  ALL {r.Ranked.Count} RATED SCHOOLS");
        Header();
        for (var i = 0; i < r.Ranked.Count; i++) Row(i + 1, r.Ranked[i]);
    }
}
