using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S111 — THE BUY GAMES PLAY.
//
//  Every non-conference pairing that S106 dated becomes a played game. Nothing here
//  decides WHICH games exist, who hosts, or what night: the matcher, the contract layer
//  and the dater have already spoken, and this file only converts and plays.
//
//  THE SITE. It comes from the pairing's KIND, never from whether a host id is present:
//    Hosted | Filler | Terminal | Exchange  -> the named host hosts
//    Neutral                                 -> nobody hosts; the "host" field is merely the
//                                               lower school id (Program.Season.Matching.cs)
//    Contract                                -> read from the exercised contract leg, because
//                                               the dater labels every contracted game
//                                               "Contract" whether or not anyone hosts it
//  Any other kind word refuses by name rather than defaulting.
//
//  THE CITY (Emmett's ruling, S111): a hosted buy game is played in its host's city; a
//  neutral buy game carries NO city. There is no venue recorded for a one-off neutral
//  pairing, and inventing one is a basketball decision nobody has made.
//
//  HOME / AWAY on a neutral game is a box-score ordering only, never a venue — exactly as
//  S110 ruled for conference tournament games.
//
//  IDENTITY. Each played game carries its pairing index. It is NEVER reconstructed from
//  the two schools: a same-season home-and-home (S105) puts the same two schools on the
//  floor twice.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ S111 — the fourth played-game kind word. Not "mte", not "ctourney", not
    /// "conf": a new kind gets a new word, so "anything except conf" never becomes the
    /// contract.</summary>
    private const string BuyGameKind = "nonconf";

    /// <summary>One converted pairing, before it is numbered or played.</summary>
    private sealed record BuyGamePlan(int PairIndex, SeasonGame Game);

    /// <summary>★ S111 — one played buy game's canonical row: what the seventh fingerprint
    /// hashes, plus the ordinal it played at (which the hash deliberately does not see —
    /// the ordinal is proven by Phase 101's walk checks instead).</summary>
    private sealed record BuyGameRow(
        int PairIndex, DateOnly Date, bool HasHost, int? PlaceId,
        int HomeId, int AwayId, int WinnerId, int FixtureOrdinal);

    private sealed record BuySeasonResult(int GameCount, IReadOnlyList<BuyGameRow> Rows);

    /// <summary>★ THE SITE FACT for one pairing. Derived from the kind (and, for a contract,
    /// from its leg) — never from the presence of a host id.</summary>
    private static bool BuyPairHasHost(
        int pairIndex, string kind, MatchingReport matching, ContractSeasonOutcome contracts)
    {
        if (pairIndex < 0)
            throw new InvalidOperationException(
                $"SEASON INVARIANT VIOLATED: non-conference pairing index {pairIndex} is negative.");
        if (pairIndex < matching.Pairs.Count)
        {
            var pair = matching.Pairs[pairIndex];
            if (!string.Equals(pair.Kind, kind, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"SEASON INVARIANT VIOLATED: dated pairing {pairIndex} says '{kind}' but the matcher " +
                    $"produced '{pair.Kind}'.");
            return kind switch
            {
                "Hosted" or "Filler" or "Terminal" or "Exchange" => true,
                "Neutral" => false,
                _ => throw new InvalidOperationException(
                    $"SEASON INVARIANT VIOLATED: pairing {pairIndex} has kind '{kind}', which has no " +
                    "ruled site. A new pairing kind must say whether anyone hosts it."),
            };
        }

        var leg = pairIndex - matching.Pairs.Count;
        if (!string.Equals(kind, "Contract", StringComparison.Ordinal) || leg >= contracts.Exercised.Count)
            throw new InvalidOperationException(
                $"SEASON INVARIANT VIOLATED: pairing {pairIndex} ('{kind}') is past the matcher's " +
                $"{matching.Pairs.Count} pairs but is not one of the {contracts.Exercised.Count} exercised " +
                "contract legs.");
        return !contracts.Exercised[leg].IsNeutral;
    }

    /// <summary>★ S111 — converts every dated pairing, in the dater's order, into a game. The
    /// undated pairing is simply absent: it is not dated, so it is not played and holds no id.</summary>
    private static IReadOnlyList<BuyGamePlan> BuyBuildPlans(
        WorldFile world, NonConDateReport dates, MatchingReport matching, ContractSeasonOutcome contracts)
    {
        if (dates.Games.Count == 0) return Array.Empty<BuyGamePlan>();
        var placeOf = world.Schools.ToDictionary(s => s.Id, s => s.PlaceId);
        var plans = new List<BuyGamePlan>(dates.Games.Count);
        var previous = -1;
        foreach (var d in dates.Games)
        {
            // The dater emits in ascending pairing order; the reservation walk and the play
            // walk both lean on that, so it is asserted rather than assumed.
            if (d.PairIndex <= previous)
                throw new InvalidOperationException(
                    $"SEASON INVARIANT VIOLATED: dated pairing {d.PairIndex} follows {previous}; the dated " +
                    "list must be strictly ascending by pairing.");
            previous = d.PairIndex;

            var hasHost = BuyPairHasHost(d.PairIndex, d.Kind, matching, contracts);
            plans.Add(new BuyGamePlan(d.PairIndex, new SeasonGame(
                BuyGameKind, d.HostSchoolId, d.VisitorSchoolId,
                Date: d.Date,
                HasHost: hasHost,
                PlaceId: hasHost ? placeOf[d.HostSchoolId] : null)));   // ★ ruling: neutral -> none
        }
        return plans;
    }

    /// <summary>★ S111 — plays every plan once, in order, at ordinals starting at
    /// <paramref name="firstOrdinal"/>. Ids come from the commit's own block, position for
    /// position; legacy mode (no season) leaves them absent, exactly as every other fixture.</summary>
    private static BuySeasonResult BuyPlaySeason(
        IReadOnlyList<BuyGamePlan> plans, IReadOnlyList<GameId> reserved, SeasonId? seasonId,
        int firstOrdinal, Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> play)
    {
        if (seasonId is not null && reserved.Count != plans.Count)
            throw new InvalidOperationException(
                $"SEASON INVARIANT VIOLATED: {plans.Count} non-conference games but {reserved.Count} " +
                "reserved ids.");

        var rows = new List<BuyGameRow>(plans.Count);
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var game = seasonId is null
                ? plan.Game
                : plan.Game with { SeasonId = seasonId, GameId = reserved[i] };
            var ordinal = firstOrdinal + i;
            var (home, away) = play(new PlayedSeasonGame(game, ordinal, BuyPairIndex: plan.PairIndex));
            var winner = home > away ? game.HomeId : away > home ? game.AwayId : 0;
            rows.Add(new BuyGameRow(plan.PairIndex, game.Date!.Value, game.HasHost, game.PlaceId,
                                    game.HomeId, game.AwayId, winner, ordinal));
        }
        return new BuySeasonResult(plans.Count, rows);
    }

    /// <summary>★ S111 — THE SEVENTH FINGERPRINT. One row per played buy game, sorted by
    /// pairing (never by collection order): pairing | date | H or N | city or - | home | away |
    /// winner. Winner in, scores out.</summary>
    private static string BuyGamesFingerprint(IEnumerable<BuyGameRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.PairIndex))
            sb.Append(r.PairIndex.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.HasHost ? 'H' : 'N').Append('|')
              .Append(r.PlaceId is { } p ? p.ToString(CultureInfo.InvariantCulture) : "-").Append('|')
              .Append(r.HomeId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.AwayId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.WinnerId.ToString(CultureInfo.InvariantCulture)).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
