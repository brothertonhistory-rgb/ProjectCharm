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
//  ★ NUMBERED LAST, PLAYED ON THEIR DATES — the single most important structural
//  fact here. A game's fixture ordinal is BOTH the engine seed input (base + 2g,
//  base + 2g + 1) and the retention log's fixture ordinal. Numbering tournament games
//  in NOVEMBER ORDER — before most of the conference slate — would shift every
//  conference index and re-roll the entire season's basketball. So the brackets are
//  NUMBERED after the last conference game and carry November DATES. From S98 to S121
//  they were also PLAYED last; since S122 the season's day loop plays every game on
//  its date (Program.Season.Calendar.cs), and the numbering is untouched.
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

    /// <summary>★ S104 — HOW MANY GAMES A COMPLETE FIELD OWES, DISPATCHED ON THE KIND AND
    /// NEVER ON THE FIELD SIZE. A showcase plays two stand-alone games; a bracket plays what
    /// its route table says. Both fields hold four schools, and that coincidence is exactly
    /// the trap A3 names: routing a showcase's four into <c>BracketRoutes4</c> produces a
    /// plausible three-game bracket with a champion, and every structural check stays
    /// green.</summary>
    private static int MteEventGameCount(SeatedEvent e)
        => e.IsShowcase ? MteShowcaseGamesPerEvent : BracketGameCount(e.FieldSize);

    /// <summary>★ THE KIND OWNS THE PLAY PATH. A showcase reaching a bracket route table is
    /// impossible by construction rather than by convention: this is the only door, and it is
    /// shut by name.</summary>
    private static BracketRoute[] MteBracketRoutesFor(SeatedEvent e)
    {
        if (e.IsShowcase)
            throw new InvalidOperationException(
                $"BRACKET: '{e.Name}' is a SHOWCASE and has no bracket. It plays two stand-alone " +
                "games from its stored seats — no advancement, no placement, no champion. A showcase " +
                "reaching a route table is a dispatch failure, never a four-team field.");
        return BracketRoutesFor(e.FieldSize);
    }

    /// <summary>★ THE CANONICAL ORDER, DEFINED ONCE. Event by (tier, id), then bracket game
    /// index. Assigning reservations, running games, building the fingerprint and comparing in
    /// the determinism check all walk THIS, so none of them leans on a dictionary's enumeration
    /// order.</summary>
    /// <para>★ S104 — TOURNAMENTS FIRST, THEN SHOWCASES, and this ordering is load-bearing
    /// enough to be worth a paragraph. The reservation index and the fixture ordinal are the
    /// same walk, and the fixture ordinal feeds the ENGINE SEED. Interleaving showcases into
    /// (tier, id) order would slide every tournament game's ordinal along and re-roll every
    /// tournament result in the country — which is precisely the byte-identity §3.3 exists to
    /// protect. Appending the new kind keeps every pre-S104 tournament game on the seed it
    /// has always had. Numbering order and calendar order are different things here (see
    /// this file's header); this is the same trick applied to a second kind.</para>
    private static List<BracketSlotKey> MteExpectedBracketSlots(EventSeatingOutcome seating)
    {
        var slots = new List<BracketSlotKey>();
        foreach (var e in MteEventPlayOrder(seating))
            for (var i = 0; i < MteEventGameCount(e); i++)
                slots.Add(new BracketSlotKey(e.Tier, e.EventId, i));
        return slots;
    }

    /// <summary>★ THE CANONICAL NUMBERING ORDER, DEFINED ONCE AND WALKED BY EVERYTHING —
    /// reservations, fixture ordinals, and the fingerprint. Tournaments in (tier, id), then
    /// showcases in (tier, id). ★ S122: no longer the order games are PLAYED in — the day loop
    /// plays each on its date — but still the order they are numbered in.</summary>
    private static IEnumerable<SeatedEvent> MteEventPlayOrder(EventSeatingOutcome seating)
        => seating.Active.Where(MteEventPlays)
                  .OrderBy(e => e.IsShowcase ? 1 : 0)
                  .ThenBy(e => e.Tier).ThenBy(e => e.EventId);

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
        int PlaceId,                       // ★ S109 — the event's city, threaded so the factory can resolve it
        IReadOnlyList<int> SchoolBySeed,   // index 0 is seed 1
        IReadOnlyList<int> SeatBySeed);    // index 0 is seed 1

    /// <summary>Seed 1..N by current prestige descending, ties broken by LOWER SCHOOL ID —
    /// the canonical tie-break everywhere in this codebase, and never a draw.
    ///
    /// <para>★ ORIGINAL SEEDS TRAVEL. A team carries the seed it is given here for the whole
    /// bracket. Nothing re-ranks between rounds and no seed is ever recomputed after play
    /// begins, so a late-round game between two teams that arrived down different paths is
    /// still ordered by the numbers they started with.</para></summary>
    private static BracketPlan MteSeedField(SeatedEvent e, IReadOnlyDictionary<int, int> prestige, int startYear)
    {
        var order = e.Seats
            .OrderByDescending(s => prestige[s.SchoolId])
            .ThenBy(s => s.SchoolId)
            .ToList();
        return new BracketPlan(
            e.Tier, e.EventId, e.Name, e.FieldSize,
            MteWindowDate(e.FirstDay, startYear), MteWindowDate(e.LastDay, startYear),
            e.PlaceId,
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
            HasHost: false,
            PlaceId: plan.PlaceId);   // ★ S109 — a neutral game is played in its event's city
    }

    // ── Playing them ─────────────────────────────────────────────────────────────

    /// <summary>What the brackets did. <c>FinishBySeat</c> and <c>SeatsPlayed</c> are both keyed
    /// by event id and then by the S97 SEAT — never by seed and never by school.</summary>
    private sealed record BracketPlayOutcome(
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> FinishBySeat,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> SeatsPlayed,
        int GameCount);

    /// <summary>★ S122 — ONE BRACKET EVENT AS A CALENDAR SOURCE. The S98 walk, turned inside out:
    /// the season's day loop decides when each game plays, and this class owns everything else —
    /// the seeds, the route table, who fills each later slot, the placings.
    ///
    /// <para>Seeds are resolved into occupancy as results arrive: a round-one game reads its two
    /// seeds straight from the route table, and every later game is filled by the arrows pointing
    /// at it. A game whose two slots are not both filled when it comes up is refused rather than
    /// played with a placeholder — which, now that every later round is dated after its feeders
    /// (asserted on the slot table), can only mean a malformed route table.</para>
    ///
    /// <para>The fixture ordinal is <c>firstOrdinal + route index</c>, the same number the S98
    /// walk gave it — events in <c>MteEventPlayOrder</c>, each event's games in route order — so
    /// every bracket game keeps the engine seed it has always had.</para></summary>
    private sealed class MteBracketSource : SeasonGameSource
    {
        private readonly SeatedEvent _event;
        private readonly BracketPlan _plan;
        private readonly BracketRoute[] _routes;
        private readonly int _firstOrdinal;
        private readonly DateOnly[] _dates;
        private readonly int[][] _occupancy;
        private readonly bool[] _played;
        private readonly Dictionary<int, int> _placeBySeat = new();
        private readonly IReadOnlyDictionary<BracketSlotKey, GameId> _reservations;
        private readonly SeasonId? _seasonId;

        /// <param name="datesForTest">Phase 113 only: replaces the round dates so a later round
        /// can be constructed on its feeder's own day. Null on every production path.</param>
        public MteBracketSource(
            SeatedEvent e, BracketPlan plan, int firstOrdinal,
            IReadOnlyDictionary<BracketSlotKey, GameId> reservations, SeasonId? seasonId,
            DateOnly[]? datesForTest = null)
        {
            _event = e;
            _plan = plan;
            _routes = MteBracketRoutesFor(e);
            _firstOrdinal = firstOrdinal;
            _reservations = reservations;
            _seasonId = seasonId;
            for (var i = 0; i < _routes.Length; i++)
                if (_routes[i].GameIndex != i)
                    throw new InvalidOperationException(
                        $"BRACKET: {plan.EventName}'s route {i.ToString(CultureInfo.InvariantCulture)} is numbered " +
                        $"{_routes[i].GameIndex.ToString(CultureInfo.InvariantCulture)}; a route table is indexed by its own game numbers.");
            var rounds = _routes.Max(r => r.Round) + 1;
            _dates = datesForTest ?? _routes.Select(r => MteRoundDate(plan, r.Round, rounds)).ToArray();
            _occupancy = new int[_routes.Length][];
            for (var i = 0; i < _routes.Length; i++) _occupancy[i] = new[] { 0, 0 };
            _played = new bool[_routes.Length];
        }

        public int EventId => _plan.EventId;
        public IReadOnlyDictionary<int, int> FinishBySeat => _placeBySeat;
        public IReadOnlyDictionary<int, int> SeatsPlayed => _event.Seats.ToDictionary(s => s.Seat, s => s.SchoolId);

        public override string Name => _plan.EventName;
        public override int Count => _routes.Length;
        public override int Ordinal(int local) => _firstOrdinal + local;
        public override DateOnly? Date(int local) => _dates[local];

        public override IEnumerable<int> Feeders(int local)
            => _routes.Where(r => r.WinnerToGame == local || r.LoserToGame == local).Select(r => r.GameIndex);

        public override PlayedSeasonGame Release(int local)
        {
            var route = _routes[local];
            if (route.SeedA > 0) { _occupancy[local][0] = route.SeedA; }
            if (route.SeedB > 0) { _occupancy[local][1] = route.SeedB; }
            var a = _occupancy[local][0];
            var b = _occupancy[local][1];
            if (a == 0 || b == 0)
                throw new InvalidOperationException(
                    $"BRACKET: {_plan.EventName} slot " +
                    $"{route.GameIndex.ToString(CultureInfo.InvariantCulture)} came up with an empty " +
                    "side; the route table does not fill every game before it is played.");

            var homeSeed = Math.Min(a, b);
            var awaySeed = Math.Max(a, b);
            var game = MteBuildTournamentGame(_plan, route, homeSeed, awaySeed, _reservations, _seasonId);
            return new PlayedSeasonGame(
                game, Ordinal(local),
                _plan.EventTier, _plan.EventId, route.GameIndex, homeSeed, awaySeed);
        }

        public override void Finish(int local, PlayedSeasonGame played, int homeScore, int awayScore)
        {
            var route = _routes[local];
            //  A tie is impossible — the engine plays overtime until somebody wins — and if
            //  one ever arrived it must not silently advance the nominal home side.
            if (homeScore == awayScore)
                throw new InvalidOperationException(
                    $"BRACKET: {_plan.EventName} slot " +
                    $"{route.GameIndex.ToString(CultureInfo.InvariantCulture)} ended level; a bracket " +
                    "game has no route for a tie.");

            var homeSeed = played.HomeOriginalSeed!.Value;
            var awaySeed = played.AwayOriginalSeed!.Value;
            var winner = homeScore > awayScore ? homeSeed : awaySeed;
            var loser  = homeScore > awayScore ? awaySeed : homeSeed;

            if (route.WinnerPlace > 0)
            {
                _placeBySeat[_plan.SeatBySeed[winner - 1]] = route.WinnerPlace;
                _placeBySeat[_plan.SeatBySeed[loser  - 1]] = route.LoserPlace;
            }
            else
            {
                _occupancy[route.WinnerToGame][route.WinnerToSlot] = winner;
                _occupancy[route.LoserToGame][route.LoserToSlot]   = loser;
            }
            _played[local] = true;
        }

        public override bool? HasPlayed(int local) => _played[local];

        public override void AssertComplete()
        {
            if (_played.Any(p => !p) || _placeBySeat.Count != _plan.FieldSize)
                throw new InvalidOperationException(
                    $"BRACKET: {_plan.EventName} finished with " +
                    $"{_placeBySeat.Count.ToString(CultureInfo.InvariantCulture)} placings for a field of " +
                    $"{_plan.FieldSize.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    /// <summary>★ S122 — every complete active bracket field as a calendar source, in
    /// <c>MteEventPlayOrder</c> (tournaments only — showcases are their own source), each
    /// starting at the ordinal the S98 walk gave it.</summary>
    private static List<MteBracketSource> MteBracketSources(
        EventSeatingOutcome seating,
        IReadOnlyDictionary<int, int> prestige,
        IReadOnlyDictionary<BracketSlotKey, GameId> reservations,
        SeasonId? seasonId,
        int conferenceGameCount,
        int startYear)
    {
        var sources = new List<MteBracketSource>();
        var next = conferenceGameCount;
        //  ★ S104 — TOURNAMENTS ONLY, and the filter is on the KIND. Showcases follow every
        //    bracket in the ordinals, which is what keeps every tournament fixture ordinal (and
        //    therefore every tournament engine seed) exactly where it was.
        foreach (var e in MteEventPlayOrder(seating).Where(x => !x.IsShowcase))
        {
            var source = new MteBracketSource(e, MteSeedField(e, prestige, startYear), next, reservations, seasonId);
            sources.Add(source);
            next += source.Count;
        }
        return sources;
    }

    /// <summary>★ S122 — what the brackets did, assembled after the season in event order (the
    /// order the S98 walk produced it in), so nothing downstream sees a play-order artifact.</summary>
    private static BracketPlayOutcome MteBracketOutcome(IReadOnlyList<MteBracketSource> sources)
    {
        var finishes = new Dictionary<int, IReadOnlyDictionary<int, int>>();
        var seatsPlayed = new Dictionary<int, IReadOnlyDictionary<int, int>>();
        foreach (var s in sources)
        {
            finishes[s.EventId] = s.FinishBySeat;
            seatsPlayed[s.EventId] = s.SeatsPlayed;
        }
        return new BracketPlayOutcome(finishes, seatsPlayed, sources.Sum(s => s.Count));
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
        var rows = playedGames.Where(p => p.IsEventGame)
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
