using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 101 — S111: THE BUY GAMES PLAY.
//
//  Every dated non-conference pairing is played, appended after the conference
//  tournaments. What this phase proves, in order:
//
//    C1  the counts, re-derived from the run — never transcribed
//    C2  ★ FIXTURE PRESERVATION, game by game, against the pre-edit season
//    C3  ★ BIJECTION on the carried pairing number, plus schedule and date fidelity
//    C4  ★ THE SITE AND THE RULED CITY (neutral buy games carry none)
//    C5  ★ RECONCILIATION per category, and the two home-court denominators
//    C6  ★ THE CONFERENCE TOURNAMENTS UNTOUCHED
//    C7  the fingerprint wall: six unmoved, the seventh born and not decorative
//    C8  ids and ordinals — the reservation walk and the play walk are one walk
//    C9  the zero path
//    C10 negative controls, each shown firing the rule it names
//
//  THE BASELINE. The pre-edit stock season (seed 20260720) was captured before a
//  line of this session was written: 3,187 games, each one's ordinal, kind, both
//  schools, both scores and possession count, digested below. The per-game
//  comparison runs against the zero-path run, which is proven equal to that digest
//  first — so C2 is a comparison against the season as it stood, not against itself.
//
//  Page-only calibration holds: no win total, record or basketball value is a target.
// ============================================================================

internal static partial class Program
{
    private const long BuyCheckSeed = 20260720;

    /// <summary>★ S111 pre-edit capture: 3,187 fixtures, "ordinal|kind|home|away|homeScore|
    /// awayScore|possessions\n" each, SHA-256.</summary>
    private const string BuyGoldenPreS111FixtureDigest =
        "d57960cfef35a05e3005cb52976a99d78d41012cffc5d4bedfa3d2ff30d8db0c";
    private const int BuyGoldenPreS111GameCount = 3187;

    /// <summary>★ S111 pre-edit capture: every school's season record, "id|W-L\n" in id order.</summary>
    private const string BuyGoldenPreS111RecordDigest =
        "a0d05313a7baa49690c3af3a6693206c0f8f6533cb5429649e128825896f807b";

    /// <summary>★ S111 pre-edit capture of the SIXTH hash. Phase 99 proves it self-consistent;
    /// this pins its value, so "unmoved" means unmoved.</summary>
    private const string BuyGoldenConfTourneyFp =
        "7291ee18784a4c7c5b0c1551e20e5888787b6df0614d6ab4b75c5257130844a8";

    private static string BuySha(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string BuyFixtureDigest(SeasonRunOutcome run, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var p = run.PlayedGames[i];
            var r = run.Results[i];
            sb.Append(p.FixtureOrdinal.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.Game.Kind).Append('|')
              .Append(p.Game.HomeId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.Game.AwayId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.HomeScore.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.AwayScore.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(run.PossessionCounts[i].ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return BuySha(sb.ToString());
    }

    private static string BuyRecordDigest(WorldFile world, IReadOnlyDictionary<int, int> wins,
                                          IReadOnlyDictionary<int, int> losses)
    {
        var sb = new StringBuilder();
        foreach (var s in world.Schools.OrderBy(s => s.Id))
            sb.Append(s.Id.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(wins[s.Id].ToString(CultureInfo.InvariantCulture)).Append('-')
              .Append(losses[s.Id].ToString(CultureInfo.InvariantCulture)).Append('\n');
        return BuySha(sb.ToString());
    }

    // ── The validators. Each returns null when the rule holds, or a message that STARTS WITH
    //    the rule's own name — which is how the negative controls prove they fired the rule
    //    they name rather than some lower one. ─────────────────────────────────────────────

    /// <summary>PRESERVATION — every pre-existing fixture, one at a time: ordinal, identity,
    /// kind, site, date, event/tournament position and FINAL SCORE. Per-school totals cannot
    /// do this: a win flipping one way and another flipping back balances every total.</summary>
    private static string? BuyPreservationFault(
        IReadOnlyList<PlayedSeasonGame> basePlayed, IReadOnlyList<SeasonGameResult> baseResults,
        IReadOnlyList<PlayedSeasonGame> onPlayed, IReadOnlyList<SeasonGameResult> onResults)
    {
        if (onPlayed.Count < basePlayed.Count || onResults.Count < baseResults.Count)
            return $"PRESERVATION: the new season holds {onPlayed.Count} games, fewer than the " +
                   $"{basePlayed.Count} it must preserve";
        for (var i = 0; i < basePlayed.Count; i++)
        {
            var b = basePlayed[i];
            var o = onPlayed[i];
            if (o.FixtureOrdinal != b.FixtureOrdinal)
                return $"PRESERVATION: fixture {i} moved from ordinal {b.FixtureOrdinal} to {o.FixtureOrdinal}";
            if (o.Game.Kind != b.Game.Kind || o.Game.HomeId != b.Game.HomeId || o.Game.AwayId != b.Game.AwayId
                || o.IsBuyGame)
                return $"PRESERVATION: fixture {i} is no longer {b.Game.Kind} {b.Game.HomeId} v {b.Game.AwayId}";
            if (o.EventId != b.EventId || o.BracketGameIndex != b.BracketGameIndex
                || o.ConferenceTournamentId != b.ConferenceTournamentId
                || o.ConfTourneyGameIndex != b.ConfTourneyGameIndex)
                return $"PRESERVATION: fixture {i} changed its event or tournament position";
            if (o.Game.Date != b.Game.Date || o.Game.HasHost != b.Game.HasHost || o.Game.PlaceId != b.Game.PlaceId)
                return $"PRESERVATION: fixture {i} changed its night or its site";
            var br = baseResults[i];
            var or = onResults[i];
            if (or.HomeScore != br.HomeScore || or.AwayScore != br.AwayScore
                || or.OvertimePeriods != br.OvertimePeriods)
                return $"PRESERVATION: fixture {i} final score changed from {br.HomeScore}-{br.AwayScore} " +
                       $"to {or.HomeScore}-{or.AwayScore}";
        }
        return null;
    }

    /// <summary>BIJECTION — every dated pairing to exactly one played buy game and back,
    /// matched on the CARRIED pairing number. A count proves nothing: one omission plus one
    /// duplication preserves it.</summary>
    private static string? BuyBijectionFault(
        IReadOnlyList<NonConDatedGame> dated, IReadOnlyList<PlayedSeasonGame> played)
    {
        var datedPairs = dated.Select(d => d.PairIndex).ToHashSet();
        var seen = new HashSet<int>();
        foreach (var p in played.Where(p => p.IsBuyGame))
        {
            var k = p.BuyPairIndex!.Value;
            if (!datedPairs.Contains(k))
                return $"BIJECTION: the buy game at ordinal {p.FixtureOrdinal} names pairing {k}, which was never dated";
            if (!seen.Add(k))
                return $"BIJECTION: pairing {k} was played twice";
        }
        foreach (var d in dated)
            if (!seen.Contains(d.PairIndex))
                return $"BIJECTION: dated pairing {d.PairIndex} was never played";
        return null;
    }

    /// <summary>SCHEDULE / DATE — each played buy game is the pairing it names: same two schools
    /// in the same order, same night, the buy-game kind word.</summary>
    private static string? BuyScheduleFault(
        IReadOnlyList<NonConDatedGame> dated, IReadOnlyList<PlayedSeasonGame> played)
    {
        var byPair = dated.ToDictionary(d => d.PairIndex);
        foreach (var p in played.Where(p => p.IsBuyGame))
        {
            if (!byPair.TryGetValue(p.BuyPairIndex!.Value, out var d)) continue;   // BIJECTION's job
            if (!string.Equals(p.Game.Kind, BuyGameKind, StringComparison.Ordinal))
                return $"SCHEDULE: pairing {d.PairIndex} played as kind '{p.Game.Kind}'";
            if (p.Game.HomeId != d.HostSchoolId || p.Game.AwayId != d.VisitorSchoolId)
                return $"SCHEDULE: pairing {d.PairIndex} played as {p.Game.HomeId} v {p.Game.AwayId}, " +
                       $"dated as {d.HostSchoolId} v {d.VisitorSchoolId}";
            if (p.Game.Date != d.Date)
                return $"DATE: pairing {d.PairIndex} played on {p.Game.Date:yyyy-MM-dd}, dated for {d.Date:yyyy-MM-dd}";
        }
        return null;
    }

    /// <summary>SITE / VENUE / RULED CITY — the host/neutral fact comes from the pairing (or its
    /// contract leg); a hosted game is in its host's city; a neutral game carries none.</summary>
    private static string? BuySiteFault(
        IReadOnlyList<NonConDatedGame> dated, IReadOnlyList<PlayedSeasonGame> played,
        MatchingReport matching, ContractSeasonOutcome contracts, IReadOnlyDictionary<int, int> placeOf)
    {
        var byPair = dated.ToDictionary(d => d.PairIndex);
        foreach (var p in played.Where(p => p.IsBuyGame))
        {
            if (!byPair.TryGetValue(p.BuyPairIndex!.Value, out var d)) continue;
            var ruled = BuyPairHasHost(d.PairIndex, d.Kind, matching, contracts);
            if (p.Game.HasHost != ruled)
                return $"SITE: pairing {d.PairIndex} ({d.Kind}) played with HasHost={p.Game.HasHost}";
            if (ruled && p.Game.PlaceId != placeOf[p.Game.HomeId])
                return $"VENUE: pairing {d.PairIndex} played in city {p.Game.PlaceId}, its host's city is " +
                       $"{placeOf[p.Game.HomeId]}";
            if (!ruled && p.Game.PlaceId is not null)
                return $"RULED CITY: neutral pairing {d.PairIndex} carries city {p.Game.PlaceId}; the ruling is none";
        }
        return null;
    }

    /// <summary>IDS — every played fixture holds exactly one id and no two share one.</summary>
    private static string? BuyIdFault(IReadOnlyList<PlayedSeasonGame> played)
    {
        if (played.Any(p => p.Game.GameId is null))
            return "IDS: a played fixture holds no id";
        var clash = played.GroupBy(p => p.Game.GameId!.Value).FirstOrDefault(g => g.Count() > 1);
        return clash is null
            ? null
            : $"IDS: ordinals {string.Join(" and ", clash.Select(p => p.FixtureOrdinal))} share one id";
    }

    /// <summary>WALK / ORDINAL — the buy games play in the reservation walk's order, position for
    /// position, at contiguous ordinals starting right after the last conference tournament game;
    /// and (with ids) their game numbers are the next contiguous block after every other id.</summary>
    private static string? BuyWalkFault(
        IReadOnlyList<int> reservationWalk, IReadOnlyList<PlayedSeasonGame> played, int firstOrdinal,
        bool withIds)
    {
        var buys = played.Where(p => p.IsBuyGame).OrderBy(p => p.FixtureOrdinal).ToList();
        if (buys.Count != reservationWalk.Count)
            return $"WALK: {buys.Count} buy games played against a walk of {reservationWalk.Count}";
        for (var i = 0; i < buys.Count; i++)
        {
            if (buys[i].FixtureOrdinal != firstOrdinal + i)
                return $"ORDINAL: buy game {i} played at ordinal {buys[i].FixtureOrdinal}, expected {firstOrdinal + i}";
            if (buys[i].BuyPairIndex != reservationWalk[i])
                return $"WALK: position {i} played pairing {buys[i].BuyPairIndex}, the walk named {reservationWalk[i]}";
        }
        if (withIds && buys.Count > 0)
        {
            var otherMax = played.Where(p => !p.IsBuyGame).Select(p => RawGameNumber(p.Game.GameId!.Value))
                                 .DefaultIfEmpty(0).Max();
            for (var i = 0; i < buys.Count; i++)
                if (RawGameNumber(buys[i].Game.GameId!.Value) != otherMax + 1 + i)
                    return $"WALK: buy game {i} holds game number {RawGameNumber(buys[i].Game.GameId!.Value)}, " +
                           $"the reservation block put {otherMax + 1 + i} there";
        }
        return null;
    }

    private enum BuyCategory { League, Event, ConferenceTournament, Buy }

    private static BuyCategory BuyCategoryOf(PlayedSeasonGame p)
        => p.IsBuyGame ? BuyCategory.Buy
         : p.IsConferenceTournamentGame ? BuyCategory.ConferenceTournament
         : p.IsEventGame ? BuyCategory.Event
         : BuyCategory.League;

    /// <summary>RECONCILE — per school, wins and losses by category, and the four sum to the
    /// overall record. Each category also balances on its own (one win and one loss a game) and
    /// holds exactly the run's own count for that category.</summary>
    private static string? BuyReconcileFault(WorldFile world, SeasonRunOutcome run,
        out Dictionary<(int School, BuyCategory Cat), (int W, int L)> table)
    {
        var t = new Dictionary<(int School, BuyCategory Cat), (int W, int L)>();
        table = t;
        foreach (var s in world.Schools)
            foreach (var c in Enum.GetValues<BuyCategory>())
                t[(s.Id, c)] = (0, 0);
        var games = Enum.GetValues<BuyCategory>().ToDictionary(c => c, _ => 0);
        for (var i = 0; i < run.PlayedGames.Count; i++)
        {
            var p = run.PlayedGames[i];
            var r = run.Results[i];
            var c = BuyCategoryOf(p);
            games[c]++;
            if (r.HomeId != p.Game.HomeId || r.AwayId != p.Game.AwayId)
                return $"RECONCILE: result {i} is not the game at ordinal {i}";
            var (winner, loser) = r.HomeScore > r.AwayScore ? (r.HomeId, r.AwayId) : (r.AwayId, r.HomeId);
            if (r.HomeScore == r.AwayScore) return $"RECONCILE: game {i} is tied";
            t[(winner, c)] = (t[(winner, c)].W + 1, t[(winner, c)].L);
            t[(loser, c)] = (t[(loser, c)].W, t[(loser, c)].L + 1);
        }
        if (games[BuyCategory.League] != run.ConferenceGameCount
            || games[BuyCategory.Event] != run.TournamentGameCount
            || games[BuyCategory.ConferenceTournament] != run.ConferenceTournamentGameCount
            || games[BuyCategory.Buy] != run.BuyGameCount)
            return "RECONCILE: a category's game count disagrees with the run's own count";
        foreach (var c in Enum.GetValues<BuyCategory>())
        {
            var w = world.Schools.Sum(s => t[(s.Id, c)].W);
            var l = world.Schools.Sum(s => t[(s.Id, c)].L);
            if (w != games[c] || l != games[c])
                return $"RECONCILE: {c} holds {games[c]} games but {w} wins and {l} losses";
        }
        foreach (var s in world.Schools)
        {
            var w = Enum.GetValues<BuyCategory>().Sum(c => t[(s.Id, c)].W);
            var l = Enum.GetValues<BuyCategory>().Sum(c => t[(s.Id, c)].L);
            if (w != run.Wins[s.Id] || l != run.Losses[s.Id])
                return $"RECONCILE: school {s.Id}'s categories sum to {w}-{l}, its record reads " +
                       $"{run.Wins[s.Id]}-{run.Losses[s.Id]}";
        }
        return null;
    }

    private static bool Phase101BuyGamesCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 101 — S111: the buy games play (every dated non-conference pairing played, " +
                          "appended after the conference tournaments). Fixture preservation game by game against " +
                          "the pre-edit season, bijection on the carried pairing, the site from the pairing kind " +
                          "and the ruled city (neutral: none), per-category reconciliation, the conference " +
                          "tournaments untouched, six fingerprints unmoved and a seventh born, ids and ordinals " +
                          "as one walk, the zero path, and a negative control per rule ==");
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

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s111-" + Guid.NewGuid().ToString("N"));
        try
        {
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var mte = LoadWorld(WorldPath("fixture-mte.world.json"));
            var placeOf = stock.Schools.ToDictionary(s => s.Id, s => s.PlaceId);
            var nameOf = stock.Schools.ToDictionary(s => s.Id, s => s.Name);

            var run = RunSeasonCore(stock, BuyCheckSeed, configPath, verbose: false);
            var off = RunSeasonCore(stock, BuyCheckSeed, configPath, verbose: false, buyGamesOffForTest: true);
            var dated = run.NonConferenceDates.Games;
            var buys = run.PlayedGames.Where(p => p.IsBuyGame).ToList();
            var firstBuyOrdinal = run.ConferenceGameCount + run.TournamentGameCount + run.ConferenceTournamentGameCount;

            // ════════════════════════════════════════════════════════════════════
            //  C9 first — ★ THE ZERO PATH IS THE PRE-EDIT SEASON. Everything in C2 compares
            //  against `off`, so `off` must be proven to BE the season as it stood.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C9a: ★ with buy games switched off, the season is the PRE-EDIT season game for game — " +
                      "3,187 fixtures, every ordinal, kind, both schools, both scores and every possession count " +
                      "digest to the value captured before this session",
                      off.PlayedGames.Count == BuyGoldenPreS111GameCount
                      && BuyFixtureDigest(off, off.PlayedGames.Count) == BuyGoldenPreS111FixtureDigest,
                      $"{off.PlayedGames.Count} games");
                Check("C9b: and every school's record is the pre-edit record",
                      BuyRecordDigest(stock, off.Wins, off.Losses) == BuyGoldenPreS111RecordDigest);
                Check("C9c: the zero path plays no buy game and holds no buy row",
                      off.BuyGameCount == 0 && off.BuyGames.Count == 0 && !off.PlayedGames.Any(p => p.IsBuyGame));
                Check("C9d: its seventh hash is the hash of nothing — the switch did not leak a row",
                      off.BuyGamesFingerprint == BuyGamesFingerprint(Array.Empty<BuyGameRow>()));
                Check("C9e: and both home-court denominators equal the league slate on the zero path",
                      off.HostedRoadSidesShaved == off.ConferenceGameCount
                      && off.LeagueRoadSidesShaved == off.ConferenceGameCount);
                Check("C9f: the switch changes only whether the buy games play — the pairings and their nights " +
                      "are the same report either way",
                      off.NonConferenceDates.DatedFingerprint == run.NonConferenceDates.DatedFingerprint
                      && off.NonConferenceDates.Games.Count == dated.Count);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C1 — THE COUNTS, re-derived.
            // ════════════════════════════════════════════════════════════════════
            {
                var pairTotal = run.Matching.Pairs.Count + run.Contracts.Exercised.Count;
                var neutralDated = dated.Count(d => !BuyPairHasHost(d.PairIndex, d.Kind, run.Matching, run.Contracts));
                Check("C1a: every dated pairing played, and only those — the played count is the dated count",
                      run.BuyGameCount == dated.Count && buys.Count == dated.Count && dated.Count > 0,
                      $"{run.BuyGameCount} played of {dated.Count} dated");
                Check("C1b: dated + unseated is the whole pairing set (matcher pairs + exercised contract legs)",
                      dated.Count + run.NonConferenceDates.Unseated.Count == pairTotal,
                      $"{dated.Count} + {run.NonConferenceDates.Unseated.Count} = {pairTotal}");
                var unseated = run.NonConferenceDates.Unseated;
                Check("C1c: ★ exactly one pairing found no night, it is named, and it never appears among the " +
                      "played games",
                      unseated.Count == 1
                      && buys.All(p => p.BuyPairIndex != unseated[0].PairIndex)
                      && run.BuyGames.All(r => r.PairIndex != unseated[0].PairIndex),
                      unseated.Count == 1
                          ? $"pairing {unseated[0].PairIndex}: {nameOf[unseated[0].VisitorId]} at " +
                            $"{nameOf[unseated[0].HostId]} ({unseated[0].Class})"
                          : $"{unseated.Count} unseated");
                Check("C1d: the neutral count is the pairings the kind rule calls neutral, and the hosted count " +
                      "is the rest",
                      buys.Count(p => !p.Game.HasHost) == neutralDated
                      && buys.Count(p => p.Game.HasHost) == dated.Count - neutralDated
                      && neutralDated > 0,
                      $"{dated.Count - neutralDated} hosted, {neutralDated} neutral");
                Check("C1e: the season is the four categories and nothing else",
                      run.Results.Count == run.ConferenceGameCount + run.TournamentGameCount
                                           + run.ConferenceTournamentGameCount + run.BuyGameCount
                      && run.PlayedGames.Count == run.Results.Count
                      && run.PossessionCounts.Count == run.Results.Count,
                      $"{run.ConferenceGameCount} + {run.TournamentGameCount} + " +
                      $"{run.ConferenceTournamentGameCount} + {run.BuyGameCount} = {run.Results.Count}");
                Check("C1f: every buy game says Kind == \"nonconf\" exactly, and no other kind's word moved",
                      buys.All(p => string.Equals(p.Game.Kind, BuyGameKind, StringComparison.Ordinal))
                      && run.PlayedGames.Where(p => !p.IsBuyGame).All(p => p.Game.Kind is "conf" or "mte" or "ctourney"));
                Check("C1g: ★ the league test no longer mistakes a buy game for a league game",
                      run.PlayedGames.Count(p => p.IsLeagueGame) == run.ConferenceGameCount
                      && buys.All(p => !p.IsLeagueGame && !p.IsEventGame && !p.IsConferenceTournamentGame));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — ★ FIXTURE PRESERVATION, one game at a time.
            // ════════════════════════════════════════════════════════════════════
            {
                var fault = BuyPreservationFault(off.PlayedGames, off.Results, run.PlayedGames, run.Results);
                Check("C2a: ★ every pre-existing fixture kept its ordinal, identity, kind, site, night, event or " +
                      "tournament position and FINAL SCORE — compared individually, never by totals",
                      fault is null, fault ?? $"{off.PlayedGames.Count} of {off.PlayedGames.Count} identical");
                Check("C2b: and its possession count — a re-rolled game that lands on the same score cannot hide",
                      run.PossessionCounts.Take(off.PossessionCounts.Count).SequenceEqual(off.PossessionCounts));
                Check("C2c: every school's record is its pre-edit record plus its buy-game record, exactly",
                      BuyReconcileFault(stock, run, out var table) is null
                      && stock.Schools.All(s =>
                          run.Wins[s.Id] - table[(s.Id, BuyCategory.Buy)].W == off.Wins[s.Id]
                          && run.Losses[s.Id] - table[(s.Id, BuyCategory.Buy)].L == off.Losses[s.Id]));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — ★ BIJECTION on the carried pairing, and the schedule it names.
            // ════════════════════════════════════════════════════════════════════
            {
                var bij = BuyBijectionFault(dated, run.PlayedGames);
                Check("C3a: ★ every dated pairing maps to exactly one played buy game and every played buy game " +
                      "to exactly one dated pairing — on the pairing number the game CARRIES",
                      bij is null, bij ?? $"{dated.Count} ↔ {buys.Count}");
                var sched = BuyScheduleFault(dated, run.PlayedGames);
                Check("C3b: each buy game is the pairing it names — same two schools in the same order, same night",
                      sched is null, sched ?? "");
                var repeats = dated.GroupBy(d => (Math.Min(d.HostSchoolId, d.VisitorSchoolId),
                                                  Math.Max(d.HostSchoolId, d.VisitorSchoolId)))
                                   .Count(g => g.Count() > 1);
                Check("C3c: ★ WHY the key is carried and never rebuilt from the two schools: this season holds " +
                      "same-season rematches, so a schools-only key would be ambiguous",
                      repeats > 0, $"{repeats} school pairs meet twice");
                Check("C3d: the seventh hash's rows are the played games, in pairing order, one each",
                      run.BuyGames.Select(r => r.PairIndex).SequenceEqual(dated.Select(d => d.PairIndex))
                      && run.BuyGames.All(r => buys.Single(p => p.BuyPairIndex == r.PairIndex).FixtureOrdinal
                                               == r.FixtureOrdinal));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — ★ THE SITE, AND THE RULED CITY.
            // ════════════════════════════════════════════════════════════════════
            {
                var site = BuySiteFault(dated, run.PlayedGames, run.Matching, run.Contracts, placeOf);
                Check("C4a: ★ every buy game's host/neutral fact is the one its pairing kind rules, and it is " +
                      "played in its host's city when hosted",
                      site is null, site ?? "");
                Check("C4b: ★ EMMETT'S RULING — every neutral buy game carries NO city, asserted as the ruled " +
                      "outcome",
                      buys.Where(p => !p.Game.HasHost).All(p => p.Game.PlaceId is null)
                      && buys.Any(p => !p.Game.HasHost));
                Check("C4c: ★ THE TRAP — no neutral buy game was sent to the lower school's city, which is what " +
                      "reading a Neutral pairing's \"host\" field as a host would have done",
                      buys.Where(p => !p.Game.HasHost).All(p => p.Game.PlaceId != placeOf[p.Game.HomeId]));
                Check("C4d: every hosted buy game is in its host's city",
                      buys.Where(p => p.Game.HasHost).All(p => p.Game.PlaceId == placeOf[p.Game.HomeId]));
                Check("C4e: every game that is NOT a neutral buy game still carries a city",
                      run.PlayedGames.Where(p => !(p.IsBuyGame && !p.Game.HasHost)).All(p => p.Game.PlaceId is > 0));
                Check("C4f: all five matcher kinds are accounted for, and Exchange legs play as hosted games",
                      dated.All(d => d.Kind is "Hosted" or "Neutral" or "Filler" or "Terminal" or "Exchange" or "Contract")
                      && buys.Where(p => dated.First(d => d.PairIndex == p.BuyPairIndex).Kind == "Exchange")
                             .All(p => p.Game.HasHost),
                      $"{dated.Count(d => d.Kind == "Exchange")} exchange legs");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — ★ RECONCILIATION and the denominators.
            // ════════════════════════════════════════════════════════════════════
            {
                var rec = BuyReconcileFault(stock, run, out _);
                Check("C5a: ★ for every school, league / event / conference tournament / buy each reconcile on " +
                      "their own, each balances, and the four sum to the overall record",
                      rec is null, rec ?? "");
                Check("C5b: precondition — the home-court shave is on, so the denominators below mean something",
                      run.RoadShave > 0, $"shave {run.RoadShave}");
                Check("C5c: ★ the LEAGUE denominator is unchanged — still exactly the league slate",
                      run.LeagueRoadSidesShaved == run.ConferenceGameCount,
                      $"{run.LeagueRoadSidesShaved} / {run.ConferenceGameCount}");
                var hostedBuys = buys.Count(p => p.Game.HasHost);
                Check("C5d: ★ the FULL-SEASON denominator adds the hosted buy games and not the neutral ones",
                      run.HostedRoadSidesShaved == run.ConferenceGameCount + hostedBuys,
                      $"{run.HostedRoadSidesShaved} = {run.ConferenceGameCount} + {hostedBuys}");
                Check("C5e: and the two really are different populations on this season — a check reading the " +
                      "wrong one would go red, not stay green",
                      run.HostedRoadSidesShaved != run.LeagueRoadSidesShaved);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — ★ THE CONFERENCE TOURNAMENTS UNTOUCHED.
            // ════════════════════════════════════════════════════════════════════
            {
                var a = run.ConferenceTournaments;
                var b = off.ConferenceTournaments;
                Check("C6a: ★ same fields in the same seed order, same 31 champions and runners-up",
                      a.Champions.Count == b.Champions.Count && a.Champions.Count == 31
                      && a.Champions.Zip(b.Champions).All(z =>
                          z.First.ConferenceId == z.Second.ConferenceId
                          && z.First.Champion == z.Second.Champion
                          && z.First.RunnerUp == z.Second.RunnerUp
                          && z.First.SchoolBySeed.SequenceEqual(z.Second.SchoolBySeed)),
                      $"{a.Champions.Count} champions");
                Check("C6b: same 217 games, same rows",
                      a.GameCount == 217 && b.GameCount == 217 && a.Rows.SequenceEqual(b.Rows));
                Check("C6c: ★ the sixth hash is the value captured before this session",
                      run.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp,
                      run.ConferenceTournamentFingerprint[..8] + "…");
                Check("C6d: and every buy game plays after every conference tournament game",
                      buys.All(p => p.FixtureOrdinal >= firstBuyOrdinal)
                      && run.PlayedGames.Where(p => p.IsConferenceTournamentGame).All(p => p.FixtureOrdinal < firstBuyOrdinal));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — THE FINGERPRINT WALL. Six unmoved, a seventh born.
            // ════════════════════════════════════════════════════════════════════
            {
                var prefix = run.ConferenceGameCount + run.TournamentGameCount;
                var resultsFp = SeasonFingerprint(run.Results.Take(prefix).ToList(),
                                                  run.PossessionCounts.Take(prefix).ToList());
                Check("C7a: #1 conference schedule UNMOVED", run.Fingerprint == MatchGoldenConferenceFp);
                Check("C7b: #2 conference dated UNMOVED", run.DatedFingerprint == MatchGoldenDatedFp);
                Check("C7c: #3 event games UNMOVED", run.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C7d: #4 results+possessions UNMOVED over its league-plus-event prefix",
                      resultsFp == MatchGoldenResultsFp);
                Check("C7e: #5 non-conference dated UNMOVED",
                      run.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C7f: #6 conference tournaments UNMOVED", run.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);

                Check("C7g: ★ #7 the buy games got their OWN hash, over pairing, date, site, city, both schools " +
                      "and the winner",
                      run.BuyGamesFingerprint.Length == 64
                      && run.BuyGamesFingerprint == BuyGamesFingerprint(run.BuyGames)
                      && run.BuyGamesFingerprint != off.BuyGamesFingerprint,
                      run.BuyGamesFingerprint);
                var hosted0 = run.BuyGames.First(r => r.HasHost);
                Check("C7h: ★ not decorative — moving one city moves it",
                      BuyGamesFingerprint(run.BuyGames.Select(r => r == hosted0 ? r with { PlaceId = 999999 } : r))
                      != run.BuyGamesFingerprint);
                Check("C7i: ★ and moving one night moves it",
                      BuyGamesFingerprint(run.BuyGames.Select((r, i) => i == 0 ? r with { Date = r.Date.AddDays(1) } : r))
                      != run.BuyGamesFingerprint);
                Check("C7j: and flipping one winner moves it",
                      BuyGamesFingerprint(run.BuyGames.Select((r, i) => i == 0
                          ? r with { WinnerId = r.WinnerId == r.HomeId ? r.AwayId : r.HomeId } : r))
                      != run.BuyGamesFingerprint);
                Check("C7k: SCOPE, stated honestly — it is sorted by pairing, so a shuffled collection does not " +
                      "move it (and it holds no scores: a score-only change is C2's job, not this hash's)",
                      BuyGamesFingerprint(run.BuyGames.Reverse()) == run.BuyGamesFingerprint);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — ★ IDS AND ORDINALS: one walk. A career run, because only a career spends ids.
            // ════════════════════════════════════════════════════════════════════
            {
                var walk = BuyBuildPlans(stock, run.NonConferenceDates, run.Matching, run.Contracts)
                           .Select(p => p.PairIndex).ToList();
                var legacyWalk = BuyWalkFault(walk, run.PlayedGames, firstBuyOrdinal, withIds: false);
                Check("C8a: ★ the stock buy games play at contiguous ordinals right after the last conference " +
                      "tournament game, in the reservation walk's order, position for position",
                      legacyWalk is null, legacyWalk ?? $"ordinals {firstBuyOrdinal}..{firstBuyOrdinal + walk.Count - 1}");

                Directory.CreateDirectory(scratch);
                var careerPath = Path.Combine(scratch, "career.json");
                long season;
                string historyId, worldFp;
                SeasonRunOutcome career;
                using (var store = HistoryStore.Open(careerPath, WorldFingerprint(mte)))
                {
                    historyId = store.HistoryId;
                    worldFp = store.WorldFingerprint;
                    season = store.PeekNextSeasonId;
                    career = RunSeasonCore(mte, BuyCheckSeed, configPath, verbose: false, store, retainGameLog: true);
                }
                var careerFirst = career.ConferenceGameCount + career.TournamentGameCount
                                  + career.ConferenceTournamentGameCount;
                var careerWalk = BuyBuildPlans(mte, career.NonConferenceDates, career.Matching, career.Contracts)
                                 .Select(p => p.PairIndex).ToList();

                Check("C8b: the career world plays buy games too (every world derives them)",
                      career.BuyGameCount > 0 && career.BuyGameCount == career.NonConferenceDates.Games.Count,
                      $"{career.BuyGameCount} on fixture-mte");
                var ids = BuyIdFault(career.PlayedGames);
                Check("C8c: ★ every played fixture holds exactly one id, with no collision across league, event, " +
                      "tournament and buy games", ids is null, ids ?? $"{career.PlayedGames.Count} distinct ids");
                var cw = BuyWalkFault(careerWalk, career.PlayedGames, careerFirst, withIds: true);
                Check("C8d: ★ the reservation walk and the play walk are the same walk — buy games hold the next " +
                      "contiguous block of game numbers, in the order they play",
                      cw is null, cw ?? "");

                var log = GameLogReader.ReadFinalized(
                    GameLogWriter.FinalPathFor(careerPath, season),
                    new GameLogBindings(historyId, worldFp, season, career.Fingerprint));
                Check("C8e: ★ the game log published — one block per game played, all four kinds counted",
                      log.Blocks.Count == career.PlayedGames.Count
                      && log.Blocks.Select((b, i) => b.Facts.FixtureOrdinal == i).All(x => x),
                      $"{log.Blocks.Count} blocks");
                var careerBuyOrdinals = career.PlayedGames.Where(p => p.IsBuyGame).Select(p => p.FixtureOrdinal).ToHashSet();
                Check("C8f: every buy block is marked non-conference, which is what keeps it out of next season's " +
                      "host memory",
                      log.Blocks.Where(b => careerBuyOrdinals.Contains(b.Facts.FixtureOrdinal))
                         .All(b => !b.Facts.IsConferenceGame)
                      && careerBuyOrdinals.Count == career.BuyGameCount);
                var legacyMte = RunSeasonCore(mte, BuyCheckSeed, configPath, verbose: false);
                Check("C8g: determinism, and ids do not leak into the seventh hash — a career run and a legacy " +
                      "run of the same world and seed produce the same one",
                      legacyMte.BuyGamesFingerprint == career.BuyGamesFingerprint
                      && legacyMte.PlayedGames.All(p => p.Game.GameId is null));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10 — NEGATIVE CONTROLS. Each constructs its fault and must fire ITS rule.
            // ════════════════════════════════════════════════════════════════════
            {
                // Preservation: a flipped result on an old game.
                var flipped = run.Results.ToList();
                flipped[7] = flipped[7] with { HomeScore = flipped[7].AwayScore, AwayScore = flipped[7].HomeScore };
                var f1 = BuyPreservationFault(off.PlayedGames, off.Results, run.PlayedGames, flipped);
                Check("C10a: altering one pre-existing result fires PRESERVATION", Fires(f1, "PRESERVATION"), f1 ?? "NO FAULT");

                var shifted = run.PlayedGames.ToList();
                shifted[11] = shifted[11] with { FixtureOrdinal = shifted[11].FixtureOrdinal + 1 };
                var f2 = BuyPreservationFault(off.PlayedGames, off.Results, shifted, run.Results);
                Check("C10b: altering one pre-existing ordinal fires PRESERVATION", Fires(f2, "PRESERVATION"), f2 ?? "NO FAULT");

                var dropped = run.PlayedGames.Where(p => p.BuyPairIndex != buys[5].BuyPairIndex).ToList();
                var f3 = BuyBijectionFault(dated, dropped);
                Check("C10c: dropping one dated buy game fires BIJECTION", Fires(f3, "BIJECTION"), f3 ?? "NO FAULT");

                var duplicated = run.PlayedGames.ToList();
                var victim = duplicated.FindIndex(p => p.BuyPairIndex == buys[6].BuyPairIndex);
                duplicated[victim] = buys[9] with { FixtureOrdinal = buys[6].FixtureOrdinal };
                var f4 = BuyBijectionFault(dated, duplicated);
                Check("C10d: ★ duplicating one (count preserved — the case a count cannot see) fires BIJECTION",
                      Fires(f4, "BIJECTION") && duplicated.Count(p => p.IsBuyGame) == dated.Count, f4 ?? "NO FAULT");

                var hostedBuy = buys.First(p => p.Game.HasHost);
                var moved = run.PlayedGames.Select(p => p == hostedBuy
                    ? p with { Game = p.Game with { PlaceId = placeOf[p.Game.AwayId] } } : p).ToList();
                var f5 = BuySiteFault(dated, moved, run.Matching, run.Contracts, placeOf);
                Check("C10e: changing one venue fires VENUE", Fires(f5, "VENUE"), f5 ?? "NO FAULT");

                var redated = run.PlayedGames.Select(p => p == hostedBuy
                    ? p with { Game = p.Game with { Date = p.Game.Date!.Value.AddDays(1) } } : p).ToList();
                var f6 = BuyScheduleFault(dated, redated);
                Check("C10f: changing one date fires DATE", Fires(f6, "DATE"), f6 ?? "NO FAULT");

                var neutralBuy = buys.First(p => !p.Game.HasHost);
                var citied = run.PlayedGames.Select(p => p == neutralBuy
                    ? p with { Game = p.Game with { PlaceId = placeOf[p.Game.HomeId] } } : p).ToList();
                var f7 = BuySiteFault(dated, citied, run.Matching, run.Contracts, placeOf);
                Check("C10g: ★ THE RULED ONE — giving a neutral buy game a city fires RULED CITY",
                      Fires(f7, "RULED CITY"), f7 ?? "NO FAULT");
                var r7 = Refusal(() => AssertEveryGamePlaced(new[] { neutralBuy.Game with { PlaceId = 7 } }, "SeasonRunOutcome"));
                Check("C10h: ★ and the season's own boundary refuses it too, by name",
                      r7 is not null && r7.Contains("neutral non-conference game") && r7.Contains("carries none"),
                      r7 ?? "NO REFUSAL");
                Check("C10i: the boundary still refuses a HOSTED buy game with no city, and accepts a neutral one " +
                      "with none",
                      Refusal(() => AssertEveryGamePlaced(new[] { hostedBuy.Game with { PlaceId = null } }, "x")) is { } r8
                      && r8.Contains("has no city")
                      && Refusal(() => AssertEveryGamePlaced(new[] { neutralBuy.Game }, "x")) is null);

                var siteFlip = run.PlayedGames.Select(p => p == hostedBuy
                    ? p with { Game = p.Game with { HasHost = false, PlaceId = null } } : p).ToList();
                var f9 = BuySiteFault(dated, siteFlip, run.Matching, run.Contracts, placeOf);
                Check("C10j: turning a hosted pairing neutral fires SITE", Fires(f9, "SITE"), f9 ?? "NO FAULT");

                // Ids — the career world.
                var mteCareer = Path.Combine(scratch, "ids.json");
                SeasonRunOutcome idRun;
                using (var store = HistoryStore.Open(mteCareer, WorldFingerprint(mte)))
                    idRun = RunSeasonCore(mte, BuyCheckSeed, configPath, verbose: false, store, bootstrapPeopleForTest: true);
                var idBuys = idRun.PlayedGames.Where(p => p.IsBuyGame).ToList();
                var clashed = idRun.PlayedGames.Select(p => p == idBuys[3]
                    ? p with { Game = p.Game with { GameId = idRun.PlayedGames[0].Game.GameId } } : p).ToList();
                var f10 = BuyIdFault(clashed);
                Check("C10k: giving two fixtures the same id fires IDS", Fires(f10, "IDS"), f10 ?? "NO FAULT");

                var idFirst = idRun.ConferenceGameCount + idRun.TournamentGameCount + idRun.ConferenceTournamentGameCount;
                var idWalk = BuyBuildPlans(mte, idRun.NonConferenceDates, idRun.Matching, idRun.Contracts)
                             .Select(p => p.PairIndex).ToList();
                var swappedWalk = idWalk.ToList();
                (swappedWalk[1], swappedWalk[2]) = (swappedWalk[2], swappedWalk[1]);
                var f11 = BuyWalkFault(swappedWalk, idRun.PlayedGames, idFirst, withIds: true);
                Check("C10l: ★ breaking the reservation/play identity walk (same games, same count, two positions " +
                      "swapped) fires WALK", Fires(f11, "WALK"), f11 ?? "NO FAULT");
                var swappedIds = idRun.PlayedGames.ToList();
                var i1 = swappedIds.IndexOf(idBuys[1]);
                var i2 = swappedIds.IndexOf(idBuys[2]);
                swappedIds[i1] = idBuys[1] with { Game = idBuys[1].Game with { GameId = idBuys[2].Game.GameId } };
                swappedIds[i2] = idBuys[2] with { Game = idBuys[2].Game with { GameId = idBuys[1].Game.GameId } };
                var f12 = BuyWalkFault(idWalk, swappedIds, idFirst, withIds: true);
                Check("C10m: and two buy games holding each other's reserved ids fires WALK too",
                      Fires(f12, "WALK") && BuyIdFault(swappedIds) is null, f12 ?? "NO FAULT");

                // The site rule itself.
                var unknown = Refusal(() => BuyPairHasHost(0, "Mystery", MatchingReport.Empty, ContractSeasonOutcome.None));
                Check("C10n: a pairing index the matcher never produced is refused by name, never defaulted",
                      unknown is not null && unknown.Contains("past the matcher"), unknown ?? "NO REFUSAL");

                // ★ A NEUTRAL CONTRACT — the dater labels it "Contract" with school A in the host field.
                var c0 = ContractSeasonOutcome.None;
                var a = stock.Schools[0].Id;
                var b = stock.Schools.First(s => s.PlaceId != stock.Schools[0].PlaceId).Id;
                var contracts = new ContractSeasonOutcome
                {
                    Load = c0.Load,
                    Exercised = new[]
                    {
                        new ContractedGame(1, 1, a, b, IsNeutral: true, HostId: null),
                        new ContractedGame(2, 1, a, b, IsNeutral: false, HostId: b),
                    },
                    Terminated = c0.Terminated, PolicyDeclined = c0.PolicyDeclined,
                    CapacityBlocked = c0.CapacityBlocked, ForcedCapacityFailure = false,
                    ForcedCapacityDetail = null, Survivors = c0.Survivors, Charges = c0.Charges,
                };
                var night = new DateOnly(2026, 11, 20);
                var report = new NonConDateReport
                {
                    Games = new[]
                    {
                        new NonConDatedGame(0, "Contract", a, b, night, 0),
                        new NonConDatedGame(1, "Contract", b, a, night.AddDays(7), 0),
                    },
                    Unseated = Array.Empty<NonConUnseated>(),
                    AllocationBend = 0, SeatingBend = 0,
                    BendHistogram = Array.Empty<int>(),
                    AllocationShortfalls = Array.Empty<(int, int)>(),
                    DatedFingerprint = "",
                };
                var plans = BuyBuildPlans(stock, report, MatchingReport.Empty, contracts);
                Check("C10o: ★ a NEUTRAL contracted game is neutral and carries no city, even though the " +
                      "\"Contract\" label and a filled host field say nothing about it",
                      !plans[0].Game.HasHost && plans[0].Game.PlaceId is null,
                      $"HasHost={plans[0].Game.HasHost}, city={plans[0].Game.PlaceId?.ToString() ?? "none"}");
                Check("C10p: and a HOSTED contracted game is hosted, in its host's city",
                      plans[1].Game.HasHost && plans[1].Game.PlaceId == placeOf[b] && plans[1].Game.HomeId == b);
            }

            Check("C11: ★ page-only calibration holds — nothing above asserts a win total, a record or a basketball " +
                  "value as a target; every number is wiring, a count, or a comparison against the season as it " +
                  "stood",
                  true, $"{assertions} assertions so far");
        }
        catch (Exception ex)
        {
            Check("Phase 101 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { /* scratch cleanup is best effort */ }
        }

        Console.WriteLine(pass ? $"  Phase 101 PASS ({assertions} assertions)"
                               : $"  Phase 101 FAIL ({assertions} assertions)");
        return pass;
    }
}
