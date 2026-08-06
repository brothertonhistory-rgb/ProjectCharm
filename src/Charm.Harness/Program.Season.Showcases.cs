using Charm.History;
using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  S104 — SHOWCASES PLAY.
//
//  A showcase invites four schools out and stages TWO STAND-ALONE GAMES IN ONE
//  DAY. Nobody advances, nobody places, nobody wins the event. Seats 1-2 are the
//  headliner and seats 3-4 the undercard, and those roles come from the STORED
//  SEAT NUMBERS (R27) — never from list position, never from prestige, and never
//  re-derived from a result. An upset must not relabel which game was authored
//  as the nightcap.
//
//  ★ MATERIALIZATION AND SIMULATION ARE DIFFERENT EVENTS, SEPARATED EVERYWHERE.
//  The two pairings exist the moment the field completes at seating — long
//  before anything plays — because a school's November is CHARGED for them, and
//  the charge has to happen before requests are built. This file is only the
//  simulation half: it takes pairings that already exist and plays them.
//
//  ★ PLAYED AFTER EVERY BRACKET, NOT INTERLEAVED. See MteExpectedBracketSlots:
//  the fixture ordinal is the engine seed, so appending the new kind is what
//  keeps every tournament game bit-identical to its pre-S104 self.
//
//  ★ THE NEUTRAL FLOOR, exactly as a tournament game has it. Nobody hosts a
//  showcase game; the lower SEAT number is the nominal home side, which is a
//  box-score ordering and a PlayerId stamping order, never a venue.
// ============================================================================

internal static partial class Program
{
    /// <summary>Two games, always. A showcase is one night's programme and the field is
    /// exactly four, so this is a constant rather than arithmetic over the field size —
    /// arithmetic would invite a "showcase of six" that nothing else in the design
    /// supports.</summary>
    private const int MteShowcaseGamesPerEvent = 2;

    /// <summary>★ S104 — the third word <c>playStatus</c> may hold... except it is NOT a third
    /// word. A showcase that played is <c>Completed</c>, exactly like a tournament: the
    /// difference between them is that a showcase carries <c>finishBySeat</c> null forever,
    /// because there is no placement to record. Reusing the word is deliberate — "this event
    /// happened" is the same fact for both kinds, and inventing a ceremonial status would
    /// make every future reader ask which of two words means played.</summary>
    private sealed record ShowcaseResult(
        int EventId, int GameIndex, int SeatA, int SeatB,
        int SchoolAId, int SchoolBId, int ScoreA, int ScoreB);

    /// <summary>What the showcases did. <c>SeatsPlayed</c> mirrors the bracket outcome's
    /// shape — keyed by event id then by SEAT — so the record's seat-identity round-trip is
    /// one code path for both kinds rather than two that can drift apart.</summary>
    private sealed record ShowcasePlayOutcome(
        IReadOnlyList<ShowcaseResult> Results,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> SeatsPlayed,
        int GameCount);

    private static readonly ShowcasePlayOutcome ShowcasePlayNone = new(
        Array.Empty<ShowcaseResult>(),
        new Dictionary<int, IReadOnlyDictionary<int, int>>(),
        0);

    /// <summary>Play every complete active showcase.
    ///
    /// <para><paramref name="play"/> runs one prepared fixture through the ordinary
    /// season-game execution path and hands back its score — the same delegate seam the
    /// brackets use, and for the same reason: the engine stays on the other side of it so
    /// this file owns the showcase and owns nothing else.</para>
    ///
    /// <para><paramref name="firstOrdinal"/> is where this run's showcase games start, which
    /// is after the conference slate AND after every bracket game. It is passed in rather
    /// than recomputed so there is exactly one arithmetic for it.</para></summary>
    private static ShowcasePlayOutcome MtePlayShowcases(
        EventSeatingOutcome seating,
        IReadOnlyDictionary<BracketSlotKey, GameId> reservations,
        SeasonId? seasonId,
        int firstOrdinal,
        Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> play)
    {
        var results = new List<ShowcaseResult>();
        var seatsPlayed = new Dictionary<int, IReadOnlyDictionary<int, int>>();
        var ordinal = 0;

        foreach (var e in MteEventPlayOrder(seating).Where(x => x.IsShowcase))
        {
            var day = MteWindowDate(e.FirstDay);
            foreach (var pairing in MteShowcasePairingsOf(e))
            {
                var game = MteBuildShowcaseGame(e, pairing, day, reservations, seasonId);
                var played = new PlayedSeasonGame(
                    game, firstOrdinal + ordinal,
                    e.Tier, e.EventId, pairing.GameIndex,
                    pairing.SeatA, pairing.SeatB);

                var (homeScore, awayScore) = play(played);
                ordinal++;

                results.Add(new ShowcaseResult(
                    e.EventId, pairing.GameIndex, pairing.SeatA, pairing.SeatB,
                    pairing.SchoolAId, pairing.SchoolBId, homeScore, awayScore));
            }

            // ★ THE WHOLE FIELD, not just the four who happened to appear in a pairing — the
            //   record's round-trip compares this map against the seats on disk as a whole,
            //   so a swapped pair is caught as readily as a missing school.
            seatsPlayed[e.EventId] = e.Seats.ToDictionary(s => s.Seat, s => s.SchoolId);
        }

        return new ShowcasePlayOutcome(results, seatsPlayed, ordinal);
    }

    /// <summary>★ EVERY SHOWCASE GAME IS AN ORDINARY <c>SeasonGame</c>, BUILT IN ONE PLACE —
    /// the same discipline <c>MteBuildTournamentGame</c> follows, and this is the only site
    /// permitted to construct one.
    ///
    /// <para>★ A1, THE FOUR SEPARATE FACTS. This game is a regular-season NON-CONFERENCE game
    /// for standings and stats; a NEUTRAL-FLOOR game for the engine (<c>HasHost: false</c>);
    /// an EVENT-RECORD member for the page and for history; and it is NEVER a trigger for the
    /// tournament exemption — a showcase seat does not make a school's season bigger, it
    /// spends one of the games that school already had. "Ordinary" must not strip the event
    /// identity and "event game" must not smuggle in the 31/3 arithmetic.</para>
    ///
    /// <para>The nominal home side is the LOWER SEAT number, which is cosmetic and
    /// deterministic in exactly the way the bracket's better-original-seed rule is. It is
    /// never read as a venue: nobody hosts this.</para></summary>
    private static SeasonGame MteBuildShowcaseGame(
        SeatedEvent e, ShowcasePairing pairing, DateOnly day,
        IReadOnlyDictionary<BracketSlotKey, GameId> reservations, SeasonId? seasonId)
    {
        if (pairing.SeatA >= pairing.SeatB)
            throw new InvalidOperationException(
                $"SHOWCASE: {e.Name} game " +
                $"{pairing.GameIndex.ToString(CultureInfo.InvariantCulture)} was handed its sides out " +
                "of seat order; the lower stored seat is the nominal home side.");

        // ★ Legacy mode — no career, so no season number and no game numbers, exactly as
        //   legacy conference and tournament fixtures behave. Basketball does not require a
        //   career file. In history mode the reservation must exist: the lock shuts before
        //   the first tip, so a missing id cannot be repaired anywhere later in the run.
        var key = new BracketSlotKey(e.Tier, e.EventId, pairing.GameIndex);
        GameId? gameId = null;
        if (seasonId is not null)
        {
            if (!reservations.TryGetValue(key, out var reserved))
                throw new InvalidOperationException(
                    $"SHOWCASE: no game number was reserved for {e.Name} game " +
                    $"{pairing.GameIndex.ToString(CultureInfo.InvariantCulture)}. The lock shuts before " +
                    "play, so this cannot be repaired inside the run.");
            gameId = reserved;
        }

        return new SeasonGame(
            "mte",
            pairing.SchoolAId,
            pairing.SchoolBId,
            seasonId,
            gameId,
            day,
            HasHost: false);
    }

    // ── The charge ───────────────────────────────────────────────────────────────

    /// <summary>★ R26 — A SHOWCASE COSTS ONE OF YOUR GAMES, CHARGED NEUTRAL → ROAD → HOME.
    ///
    /// <para>Season totals never change: 31 with a tournament, 29 without, regardless of
    /// showcases. The invited school spends a game it already had, and the chain says which
    /// one. Neutral first because a showcase game IS a neutral-floor game; then road, because
    /// a school that travelled to a sponsored event gave up a road trip — which is exactly
    /// right for an invited bottom school, and is why the Selling neutral allowance stays 0
    /// rather than sending sixty schools shopping for neutral games they never wanted.</para>
    ///
    /// <para>Applied AFTER the contract charges, per the ruled priority. The capacity gate
    /// upstream guarantees the buckets can pay, so an exhausted chain is an invariant
    /// violation rather than a case to handle.</para></summary>
    private static (int Home, int Neutral, int Road) ApplyShowcaseCharges(
        int home, int neutral, int road, int games)
    {
        for (var i = 0; i < games; i++)
        {
            if (neutral > 0) neutral -= 1;
            else if (road > 0) road -= 1;
            else if (home > 0) home -= 1;
            else throw new InvalidOperationException(
                "SHOWCASE INVARIANT VIOLATED: a showcase game has nothing to charge — the seating " +
                "capacity filter should have refused this school an invitation before the request " +
                "builder ran.");
        }
        return (home, neutral, road);
    }
}
