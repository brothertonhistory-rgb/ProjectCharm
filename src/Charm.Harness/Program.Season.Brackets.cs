using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Session 98 — THE BRACKETS PLAY.
//
//  S97 authored the pool, drew which events run, and seated every field. This
//  file plays them: each active COMPLETE field is seeded by prestige, played out
//  to a full placement on the window's own nights, and its finishes handed back
//  for the permanent record.
//
//  ★ EXECUTED LAST, DATED FIRST — the single most important structural fact here,
//  and the one most likely to be "tidied" later by somebody who has not read this
//  paragraph. The season loop's index `g` is BOTH the engine seed input
//  (base + 2g, base + 2g + 1) and the retention log's fixture ordinal. Inserting
//  tournament games in NOVEMBER ORDER — before most of the conference slate —
//  would shift every conference index and re-roll the entire season's basketball.
//  So the brackets are appended after the last conference game and carry November
//  DATES. Play order and calendar order are different things and this session is
//  the first to make them differ.
//
//  That is only safe because season game execution is independent game to game:
//  both sides are rebuilt from the per-school row tables for every game, the two
//  seeds come from the season seed alone, and no accumulator feeds later play.
//  See the note above SeasonFingerprint in Program.Season.HomeCourt.cs.
//
//  ★ THE NEUTRAL FLOOR. A tournament game is hosted by NOBODY. Both sides are
//  handed to the engine unshaved — the higher seed is the nominal home side, which
//  is a box-score ordering and a PlayerId stamping order, never a venue and never
//  an edge. The crowd model is unbuilt and nothing here anticipates it.
//
//  ★ A TOURNAMENT GAME IS AN ORDINARY REGULAR-SEASON GAME. It counts in the
//  record, it is logged as non-conference, and it feeds nothing else: prestige does
//  not move until the season rolls over, and a finish is recorded and read by
//  nobody. That is deliberate, not an omission.
// ============================================================================

internal static partial class Program
{
    /// <summary>A bracket POSITION, which is what a reserved game number belongs to. Never a
    /// team: the pairings are unknown when the numbers are spent, and an id that belonged to
    /// whoever happened to win would be unassignable before the first tip.</summary>
    private readonly record struct BracketSlotKey(int EventTier, int EventId, int BracketGameIndex);

    /// <summary>One row of a route table. <c>SeedA</c>/<c>SeedB</c> are non-zero only in round
    /// one, where the pairing comes from the seeding rather than from a result; every later
    /// game is fed entirely by <c>WinnerTo</c>/<c>LoserTo</c> arrows pointing at it. A terminal
    /// game routes nowhere and awards two places instead.</summary>
    private sealed record BracketRoute(
        int GameIndex, int Round, int SeedA, int SeedB,
        int WinnerToGame, int WinnerToSlot,
        int LoserToGame, int LoserToSlot,
        int WinnerPlace, int LoserPlace);

    // ═════════════════════════════════════════════════════════════════════════════
    //  ★ THE ROUTE TABLES — LITERAL AND NORMATIVE.
    //
    //  These ARE the spec. The prose ("winners' semis, losers' bracket, final,
    //  third, fifth, seventh") does not uniquely determine an eight-team
    //  consolation topology, so nothing derives these and nothing infers them.
    //
    //  EVERY TEAM PLAYS EVERY ROUND. That is the NCAA rule the pool's design brief
    //  recorded, and it is what makes the consolation side compulsory rather than
    //  decorative: an eight-team field is three games for all eight, a four-team
    //  field two games for all four, and every place from first to last is decided
    //  on the floor.
    //
    //  Slot 0 is the A side of the destination game, slot 1 the B side.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>Eight teams, three rounds, twelve games, places 1st-8th.
    /// <code>
    /// R1  G0  seed1 v seed8      W -> G4.A     L -> G6.A
    ///     G1  seed4 v seed5      W -> G4.B     L -> G6.B
    ///     G2  seed2 v seed7      W -> G5.A     L -> G7.A
    ///     G3  seed3 v seed6      W -> G5.B     L -> G7.B
    /// R2  G4  championship semi  W -> G8.A     L -> G9.A
    ///     G5  championship semi  W -> G8.B     L -> G9.B
    ///     G6  consolation semi   W -> G10.A    L -> G11.A
    ///     G7  consolation semi   W -> G10.B    L -> G11.B
    /// R3  G8  FINAL              W = 1st       L = 2nd
    ///     G9  third place        W = 3rd       L = 4th
    ///     G10 fifth place        W = 5th       L = 6th
    ///     G11 seventh place      W = 7th       L = 8th
    /// </code></summary>
    private static readonly BracketRoute[] BracketRoutes8 =
    {
        new( 0, 0, 1, 8,   4, 0,   6, 0,  0, 0),
        new( 1, 0, 4, 5,   4, 1,   6, 1,  0, 0),
        new( 2, 0, 2, 7,   5, 0,   7, 0,  0, 0),
        new( 3, 0, 3, 6,   5, 1,   7, 1,  0, 0),
        new( 4, 1, 0, 0,   8, 0,   9, 0,  0, 0),
        new( 5, 1, 0, 0,   8, 1,   9, 1,  0, 0),
        new( 6, 1, 0, 0,  10, 0,  11, 0,  0, 0),
        new( 7, 1, 0, 0,  10, 1,  11, 1,  0, 0),
        new( 8, 2, 0, 0,  -1,-1,  -1,-1,  1, 2),
        new( 9, 2, 0, 0,  -1,-1,  -1,-1,  3, 4),
        new(10, 2, 0, 0,  -1,-1,  -1,-1,  5, 6),
        new(11, 2, 0, 0,  -1,-1,  -1,-1,  7, 8),
    };

    /// <summary>Four teams, two rounds, four games, places 1st-4th.
    /// <code>
    /// R1  G0  seed1 v seed4      W -> G2.A     L -> G3.A
    ///     G1  seed2 v seed3      W -> G2.B     L -> G3.B
    /// R2  G2  FINAL              W = 1st       L = 2nd
    ///     G3  third place        W = 3rd       L = 4th
    /// </code></summary>
    private static readonly BracketRoute[] BracketRoutes4 =
    {
        new(0, 0, 1, 4,   2, 0,   3, 0,  0, 0),
        new(1, 0, 2, 3,   2, 1,   3, 1,  0, 0),
        new(2, 1, 0, 0,  -1,-1,  -1,-1,  1, 2),
        new(3, 1, 0, 0,  -1,-1,  -1,-1,  3, 4),
    };

    /// <summary>The route table for a field size, or a loud refusal. Only 8 and 4 are
    /// authorable (the world validator ties the window length to exactly these two), so a
    /// third size reaching here is a schema failure and not a case to be handled.</summary>
    private static BracketRoute[] BracketRoutesFor(int fieldSize) => fieldSize switch
    {
        8 => BracketRoutes8,
        4 => BracketRoutes4,
        _ => throw new InvalidOperationException(
                 $"BRACKET: no route table for a field of {fieldSize.ToString(CultureInfo.InvariantCulture)}; " +
                 "only 8 and 4 are authorable."),
    };

    /// <summary>How many games a complete field plays. Needed BEFORE the pairings exist,
    /// because the game numbers are spent before the first tip.</summary>
    private static int BracketGameCount(int fieldSize) => BracketRoutesFor(fieldSize).Length;

    /// <summary>Whether this seated event will actually play. A dormant event is not here at
    /// all; a SHORT event is here and does NOT play — it holds no reservations, spends no
    /// ids, produces no games, stays NotPlayed and carries no finishes. Per the S98 ruling a
    /// short field is a data error rather than a modelled state, so nothing is built for it
    /// beyond refusing to pretend it can play.</summary>
    private static bool MteEventPlays(SeatedEvent e)
        => e.SeatingStatus == EventSeatingStatus.Complete && e.Seats.Count == e.FieldSize;

    /// <summary>★ THE CANONICAL ORDER, DEFINED ONCE. Event by (tier, id), then bracket game
    /// index. Assigning reservations, running games, building the fingerprint and comparing in
    /// the determinism check all walk THIS, so none of them leans on a dictionary's enumeration
    /// order.</summary>
    private static List<BracketSlotKey> MteExpectedBracketSlots(EventSeatingOutcome seating)
    {
        var slots = new List<BracketSlotKey>();
        foreach (var e in seating.Active.OrderBy(x => x.Tier).ThenBy(x => x.EventId))
        {
            if (!MteEventPlays(e)) continue;
            for (var i = 0; i < BracketGameCount(e.FieldSize); i++)
                slots.Add(new BracketSlotKey(e.Tier, e.EventId, i));
        }
        return slots;
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────

    /// <summary>A field with its seeds settled, ready to play.
    ///
    /// <para>★ SEAT ORDER IS NOT SEED ORDER. A seat records which authored slot a school
    /// filled; a seed records how good it is. The permanent record keeps SEATS, the bracket
    /// uses SEEDS, and nothing re-derives one from the other — which is why both maps are
    /// carried here explicitly rather than one being recomputed from the other later.</para></summary>
    private sealed record BracketPlan(
        int EventTier, int EventId, string EventName, int FieldSize,
        DateOnly FirstDay, DateOnly LastDay,
        IReadOnlyList<int> SchoolBySeed,   // index 0 is seed 1
        IReadOnlyList<int> SeatBySeed);    // index 0 is seed 1

    /// <summary>Seed 1..N by current prestige descending, ties broken by LOWER SCHOOL ID —
    /// the canonical tie-break everywhere in this codebase, and never a draw.
    ///
    /// <para>★ ORIGINAL SEEDS TRAVEL. A team carries the seed it is given here for the whole
    /// bracket. Nothing re-ranks between rounds and no seed is ever recomputed after play
    /// begins, so a late-round game between two teams that arrived down different paths is
    /// still ordered by the numbers they started with.</para></summary>
    private static BracketPlan MteSeedField(SeatedEvent e, IReadOnlyDictionary<int, int> prestige)
    {
        var order = e.Seats
            .OrderByDescending(s => prestige[s.SchoolId])
            .ThenBy(s => s.SchoolId)
            .ToList();
        return new BracketPlan(
            e.Tier, e.EventId, e.Name, e.FieldSize,
            MteWindowDate(e.FirstDay), MteWindowDate(e.LastDay),
            order.Select(s => s.SchoolId).ToList(),
            order.Select(s => s.Seat).ToList());
    }

    /// <summary>The night a round plays on. Rounds run on the window's own days, in order:
    /// an eight-team field on firstDay, firstDay+1 and lastDay; a four-team field on firstDay
    /// and lastDay. The world validator already refuses any window whose length is not exactly
    /// the number of playing days, so this arithmetic cannot run off the end — and the
    /// equality below says so rather than assuming it.</summary>
    private static DateOnly MteRoundDate(BracketPlan plan, int round, int rounds)
    {
        var date = plan.FirstDay.AddDays(round);
        if (round == rounds - 1 && date != plan.LastDay)
            throw new InvalidOperationException(
                $"BRACKET: {plan.EventName}'s window {plan.FirstDay:MM-dd}..{plan.LastDay:MM-dd} does not " +
                $"hold {rounds.ToString(CultureInfo.InvariantCulture)} back-to-back rounds.");
        return date;
    }

    // ── The one factory ──────────────────────────────────────────────────────────

    /// <summary>★ EVERY TOURNAMENT GAME IS AN ORDINARY <c>SeasonGame</c>, BUILT IN ONE PLACE.
    /// This is the only site permitted to construct one.
    ///
    /// <para><c>HasHost</c> defaults to <c>true</c> so that all pre-S98 construction is
    /// untouched — which means a forgotten <c>false</c> would silently HOST a tournament game
    /// and the harness would stay green. Centralising construction is what removes that
    /// failure mode, and the check exercises this factory's output rather than a hand-built
    /// probe.</para>
    ///
    /// <para>The nominal home side is the better ORIGINAL seed — the lower seed number. That
    /// is cosmetic and deterministic: it decides box-score ordering and which side's PlayerIds
    /// are stamped first, and it is never read as a venue.</para></summary>
    private static SeasonGame MteBuildTournamentGame(
        BracketPlan plan, BracketRoute route, int homeSeed, int awaySeed,
        IReadOnlyDictionary<BracketSlotKey, GameId> reservations, SeasonId? seasonId)
    {
        if (homeSeed >= awaySeed)
            throw new InvalidOperationException(
                $"BRACKET: {plan.EventName} game {route.GameIndex.ToString(CultureInfo.InvariantCulture)} " +
                "was handed its sides out of seed order; the better original seed is the nominal home side.");

        // ★ Legacy mode — no career, so no season number and no game numbers, exactly as
        //   legacy CONFERENCE fixtures have always behaved. Basketball does not require a
        //   career file. In history mode the reservation must exist: the lock shuts before the
        //   first tip, so a missing id cannot be repaired anywhere later in the run.
        var key = new BracketSlotKey(plan.EventTier, plan.EventId, route.GameIndex);
        GameId? gameId = null;
        if (seasonId is not null)
        {
            if (!reservations.TryGetValue(key, out var reserved))
                throw new InvalidOperationException(
                    $"BRACKET: no game number was reserved for {plan.EventName} slot " +
                    $"{route.GameIndex.ToString(CultureInfo.InvariantCulture)}. The lock shuts before play, " +
                    "so this cannot be repaired inside the run.");
            gameId = reserved;
        }

        return new SeasonGame(
            "mte",
            plan.SchoolBySeed[homeSeed - 1],
            plan.SchoolBySeed[awaySeed - 1],
            seasonId,
            gameId,
            MteRoundDate(plan, route.Round, BracketRoutesFor(plan.FieldSize).Max(r => r.Round) + 1),
            HasHost: false);
    }

    // ── Playing them ─────────────────────────────────────────────────────────────

    /// <summary>What the brackets did. <c>FinishBySeat</c> and <c>SeatsPlayed</c> are both keyed
    /// by event id and then by the S97 SEAT — never by seed and never by school.</summary>
    private sealed record BracketPlayOutcome(
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> FinishBySeat,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> SeatsPlayed,
        int GameCount);

    /// <summary>Play every complete active field to a full placement.
    ///
    /// <para><paramref name="play"/> runs one prepared fixture through the ordinary season-game
    /// execution path and hands back its score. Keeping the engine on the other side of a
    /// delegate is what lets this file own the bracket and own nothing else.</para>
    ///
    /// <para>Seeds are resolved into occupancy as results arrive: a round-one game reads its
    /// two seeds straight from the route table, and every later game is filled by the arrows
    /// pointing at it. A game whose two slots are not both filled when it comes up is a
    /// malformed route table, and it says so rather than playing a placeholder.</para></summary>
    private static BracketPlayOutcome MtePlayBrackets(
        EventSeatingOutcome seating,
        IReadOnlyDictionary<int, int> prestige,
        IReadOnlyDictionary<BracketSlotKey, GameId> reservations,
        SeasonId? seasonId,
        int conferenceGameCount,
        Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> play)
    {
        var finishes = new Dictionary<int, IReadOnlyDictionary<int, int>>();
        var seatsPlayed = new Dictionary<int, IReadOnlyDictionary<int, int>>();
        var eventGameOrdinal = 0;

        foreach (var e in seating.Active.OrderBy(x => x.Tier).ThenBy(x => x.EventId))
        {
            if (!MteEventPlays(e)) continue;

            var plan = MteSeedField(e, prestige);
            var routes = BracketRoutesFor(plan.FieldSize);
            var occupancy = new int[routes.Length][];
            for (var i = 0; i < routes.Length; i++) occupancy[i] = new[] { 0, 0 };

            var placeBySeat = new Dictionary<int, int>();

            foreach (var route in routes)
            {
                if (route.SeedA > 0) { occupancy[route.GameIndex][0] = route.SeedA; }
                if (route.SeedB > 0) { occupancy[route.GameIndex][1] = route.SeedB; }
                var a = occupancy[route.GameIndex][0];
                var b = occupancy[route.GameIndex][1];
                if (a == 0 || b == 0)
                    throw new InvalidOperationException(
                        $"BRACKET: {plan.EventName} slot " +
                        $"{route.GameIndex.ToString(CultureInfo.InvariantCulture)} came up with an empty " +
                        "side; the route table does not fill every game before it is played.");

                var homeSeed = Math.Min(a, b);
                var awaySeed = Math.Max(a, b);
                var game = MteBuildTournamentGame(plan, route, homeSeed, awaySeed, reservations, seasonId);

                //  fixtureOrdinal = conferenceGameCount + eventGameOrdinal, stated literally so
                //  nothing depends on reusing a loop variable correctly. The SAME ordinal feeds
                //  the engine seeds, the retention log and PlayedGames.
                var fixtureOrdinal = conferenceGameCount + eventGameOrdinal;
                var played = new PlayedSeasonGame(
                    game, fixtureOrdinal,
                    plan.EventTier, plan.EventId, route.GameIndex, homeSeed, awaySeed);

                var (homeScore, awayScore) = play(played);
                eventGameOrdinal++;

                //  A tie is impossible — the engine plays overtime until somebody wins — and if
                //  one ever arrived it must not silently advance the nominal home side.
                if (homeScore == awayScore)
                    throw new InvalidOperationException(
                        $"BRACKET: {plan.EventName} slot " +
                        $"{route.GameIndex.ToString(CultureInfo.InvariantCulture)} ended level; a bracket " +
                        "game has no route for a tie.");

                var winner = homeScore > awayScore ? homeSeed : awaySeed;
                var loser  = homeScore > awayScore ? awaySeed : homeSeed;

                if (route.WinnerPlace > 0)
                {
                    placeBySeat[plan.SeatBySeed[winner - 1]] = route.WinnerPlace;
                    placeBySeat[plan.SeatBySeed[loser  - 1]] = route.LoserPlace;
                }
                else
                {
                    occupancy[route.WinnerToGame][route.WinnerToSlot] = winner;
                    occupancy[route.LoserToGame][route.LoserToSlot]   = loser;
                }
            }

            if (placeBySeat.Count != plan.FieldSize)
                throw new InvalidOperationException(
                    $"BRACKET: {plan.EventName} finished with " +
                    $"{placeBySeat.Count.ToString(CultureInfo.InvariantCulture)} placings for a field of " +
                    $"{plan.FieldSize.ToString(CultureInfo.InvariantCulture)}.");

            finishes[plan.EventId] = placeBySeat;
            seatsPlayed[plan.EventId] = e.Seats.ToDictionary(s => s.Seat, s => s.SchoolId);
        }

        return new BracketPlayOutcome(finishes, seatsPlayed, eventGameOrdinal);
    }

    // ── The event-games fingerprint ──────────────────────────────────────────────

    /// <summary>★ The OTHER half of the season, hashed. After this session the schedule
    /// fingerprint is conference-only and no longer describes the whole year — a bracket
    /// cannot be built before it is played, so tournament fixtures cannot exist when that hash
    /// is computed. This is what keeps the page honest, and both lines are relabelled to say
    /// which half they cover.
    ///
    /// <para>Shape mirrors <c>SeasonFingerprint</c> and <c>ScheduleFingerprint</c> deliberately
    /// (the sibling rule): ordered lines, '|' between fields, LF terminator including the last,
    /// invariant culture, UTF-8 without a BOM, SHA-256, lowercase hex.</para></summary>
    private static string MteEventGamesFingerprint(
        IReadOnlyList<PlayedSeasonGame> playedGames,
        IReadOnlyList<SeasonGameResult> results,
        IReadOnlyList<int> possessionCounts)
    {
        var rows = playedGames.Where(p => p.IsTournament)
                              .OrderBy(p => p.EventTier).ThenBy(p => p.EventId)
                              .ThenBy(p => p.BracketGameIndex)
                              .ToList();
        var sb = new StringBuilder();
        foreach (var p in rows)
        {
            var r = results[p.FixtureOrdinal];
            sb.Append(p.EventTier!.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.EventId!.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.BracketGameIndex!.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(p.Game.GameId?.ToString() ?? "none").Append('|')
              .Append(p.Game.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none").Append('|')
              .Append(r.HomeId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.AwayId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.HomeScore.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.AwayScore.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(possessionCounts[p.FixtureOrdinal].ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
