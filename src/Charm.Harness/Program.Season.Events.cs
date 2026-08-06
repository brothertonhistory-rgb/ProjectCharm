using System.Globalization;
using System.Text;
using System.Text.Json;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  SESSION 97 — THE MTE POOL: EVENTS EXIST AND FIELDS ARE SEATED
//
//  A world may author bracketed early-season tournaments. Each season, every
//  event draws against its own persistence to decide whether it happens at all,
//  and the ones that do seat their fields in tier order — the best tournament
//  picks first, from the whole country, and the next picks from what is left.
//
//  ★ NO TOURNAMENT GAME PLAYS HERE. S97 decides WHO IS IN. Bracket play,
//    seeding, places on the calendar and the neutral-floor fact are S98. The
//    split is licensed by the calendar: every window ends in November and the
//    earliest conference game in the stock world is December 7.
//
//  ★ TWO ABSOLUTES, HELD AT EVERY FALLBACK LEVEL, NEVER RELAXED:
//      1. A school plays in at most ONE event per season.
//      2. A field holds at most one school per conferenceCapKey.
//    Everything else — the prestige band, the power/mid scope, the four-year
//    rule — relaxes in a fixed order when a seat cannot otherwise be filled,
//    and the record says which level each seat needed.
//
//  ★ THE SOFT PREFERENCE IS NEVER DROPPED. Even at the last fallback level,
//    the school with the fewest recent appearances wins before any draw runs.
//    That is what stops the same names circulating forever once the hard rules
//    stop biting.
//
//  ★ RNG: KEYED SUBSTREAMS ONLY. Every draw is a hash of (seasonSeed, a
//    compile-time domain constant, and the ids that name the draw). Nothing
//    consumes a stream, so adding, removing or forcing one event never moves
//    another event's draw, and one slot's fallback never shifts a later slot's.
//    Runtime GetHashCode and string hashing are forbidden here: a career
//    reopened tomorrow must seat the identical field.
// ============================================================================

internal static partial class Program
{
    // ── Keyed hashing ────────────────────────────────────────────────────────────

    /// <summary>Domain separation constants. Compile-time integers, never strings: a
    /// string hash is a runtime detail and this value reaches a saved career.</summary>
    private const int MteActivateDomain = 1;
    private const int MteSeatDomain = 2;

    /// <summary>SplitMix64's finalizer, the same mixing the world seeder uses. Kept local
    /// and explicit rather than reaching for anything in the framework, because the whole
    /// contract is that this number is identical on every platform and every run.</summary>
    private static ulong MteMix(ulong z)
    {
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>A keyed 64-bit hash over fixed-width integer fields. Not a stream: calling
    /// it never advances anything, so two draws are independent by construction rather than
    /// by careful ordering.</summary>
    private static ulong MteHash64(long seasonSeed, int domain, long a = 0, long b = 0, long c = 0, long d = 0)
    {
        var h = MteMix(unchecked((ulong)seasonSeed) ^ 0x9E3779B97F4A7C15UL);
        h = MteMix(h ^ unchecked((ulong)domain));
        h = MteMix(h ^ unchecked((ulong)a));
        h = MteMix(h ^ unchecked((ulong)b));
        h = MteMix(h ^ unchecked((ulong)c));
        h = MteMix(h ^ unchecked((ulong)d));
        return h;
    }

    /// <summary>★ S97 — THE PULL: how hard a seat reaches for the best team it is allowed to
    /// take. Emmett's ruling, 2026-08-03.
    ///
    /// <para>The problem it solves: a seat's prestige band was the ONLY expression of what
    /// kind of team it wanted, and inside that band every qualifying school was equally
    /// likely. So a headline seat authored at [80,94] was as likely to land a it-just-scrapes-
    /// in program as the best team in the country, and measured across the whole pool that
    /// produced fields which looked right one at a time while barely any elite program played
    /// in a tournament all November.</para>
    ///
    /// <para>The mechanism: draw this many candidates and seat the strongest of them. It is
    /// deliberately NOT "take the best available" — that would hand the same event the same
    /// flagship every season, which is the failure the four-year rule exists to prevent and
    /// the reason the whole memory layer was built first. A repeated draw leans hard without
    /// ever guaranteeing, so a good program usually gets a good event and occasionally
    /// doesn't, which is what the real participation records look like.</para>
    ///
    /// <para>★ ONE is the flat draw — the pre-ruling behaviour — and it is kept reachable on
    /// purpose so Phase 88 can prove the pull does something rather than assuming it.</para>
    ///
    /// <para>Ties break on the lower school id, never on the draw, so the pull adds no second
    /// source of randomness of its own.</para></summary>
    private const int MteSeatPull = 5;


    /// <summary>2^53, as a double, for the persistence threshold. Named because it appears
    /// twice and a mistyped digit here would be a silently biased draw.</summary>
    private const double MteTwoPow53 = 9007199254740992.0;

    /// <summary>★ Persistence becomes an INTEGER THRESHOLD once, at load, and the float
    /// never touches a draw again. That is what makes persistence 0 mean "never" and 1 mean
    /// "always" as facts rather than as very-likely: 0 maps to a threshold nothing can fall
    /// below, 1 to a threshold everything falls below, and both are asserted at every seed
    /// rather than reasoned about.</summary>
    private static ulong MtePersistenceThreshold53(double persistence)
        => (ulong)Math.Round(persistence * MteTwoPow53, MidpointRounding.ToEven);

    // ── What seating produces ────────────────────────────────────────────────────

    /// <summary>Which of the three soft rules had to be relaxed to fill a seat. The order is
    /// fixed and cumulative: the band goes first (a school slightly outside the intended
    /// quality range is the smallest lie), then the power/mid scope, and the four-year rule
    /// last, because bringing the same school back early is the most visible break.
    ///
    /// <para>★ Stored and read AS THE WORD, never the number. An ordinal is fragile across a
    /// reorder, and this value lands in a permanent career record.</para></summary>
    private enum EventSeatFallback
    {
        None = 0,
        Band = 1,
        BandAndScope = 2,
        BandScopeAndFourYear = 3,
    }

    private static string MteFallbackWord(EventSeatFallback f) => f switch
    {
        EventSeatFallback.None => "None",
        EventSeatFallback.Band => "Band",
        EventSeatFallback.BandAndScope => "BandAndScope",
        EventSeatFallback.BandScopeAndFourYear => "BandScopeAndFourYear",
        _ => "None",
    };

    private static EventSeatFallback MteFallbackFromWord(string w) => w switch
    {
        "Band" => EventSeatFallback.Band,
        "BandAndScope" => EventSeatFallback.BandAndScope,
        "BandScopeAndFourYear" => EventSeatFallback.BandScopeAndFourYear,
        _ => EventSeatFallback.None,
    };

    /// <summary>How the page says it out loud. Nothing prints a raw level number.</summary>
    private static string? MteFallbackPageNote(EventSeatFallback f) => f switch
    {
        EventSeatFallback.Band => "band relaxed",
        EventSeatFallback.BandAndScope => "band and scope relaxed",
        EventSeatFallback.BandScopeAndFourYear => "four-year rule also relaxed",
        _ => null,
    };

    /// <summary>★ S104 — HOW FAR THIS SEAT HAD TO REACH, and it is an ORTHOGONAL dimension to
    /// the fallback above rather than four more fallback words.
    ///
    /// <para>Fusing them would multiply the vocabulary combinatorially (Band-at-Plus200,
    /// BandAndScope-at-Plus400, …) and — worse — would make the existing lenient
    /// <see cref="MteFallbackFromWord"/> silently read every one of the new compound words as
    /// <c>None</c>. Two independent facts, stored as two independent words.</para>
    ///
    /// <para><c>National</c> is what every tournament and every National-drawn showcase
    /// records: the seat had no radius to widen, which is a different fact from "the seat
    /// found somebody at the authored radius".</para></summary>
    private enum EventSeatRadiusStep
    {
        National = 0,
        Base = 1,
        Plus200 = 2,
        Plus400 = 3,
    }

    /// <summary>★ R28(c) — the radius widens in AUTHORED STEPS and never goes national.
    /// Offsets in miles from the event's authored draw.</summary>
    private static readonly int[] MteRadiusStepOffsets = { 0, 200, 400 };

    private static string MteRadiusStepWord(EventSeatRadiusStep s) => s switch
    {
        EventSeatRadiusStep.Base => "Base",
        EventSeatRadiusStep.Plus200 => "Plus200",
        EventSeatRadiusStep.Plus400 => "Plus400",
        _ => "National",
    };

    /// <summary>★ STRICT, unlike <see cref="MteFallbackFromWord"/> — and the asymmetry is
    /// deliberate rather than an inconsistency. The fallback reader is lenient because it
    /// must keep reading pre-S104 records that legitimately predate several of its words; the
    /// radius reader is new, so every value it will ever see was written by this code, and an
    /// unrecognised word there means the file is damaged. Absence is NOT damage: a pre-S104
    /// record has no radius field and its seats were National, which is what it says.</summary>
    private static EventSeatRadiusStep MteRadiusStepFromWord(string? w) => w switch
    {
        null => EventSeatRadiusStep.National,
        "National" => EventSeatRadiusStep.National,
        "Base" => EventSeatRadiusStep.Base,
        "Plus200" => EventSeatRadiusStep.Plus200,
        "Plus400" => EventSeatRadiusStep.Plus400,
        _ => throw new InvalidOperationException($"unknown radius step word '{w}'"),
    };

    /// <summary>The page's word for a seat that had to widen. Silent at the authored radius
    /// and silent for National — a note exists to say something happened.</summary>
    private static string? MteRadiusStepPageNote(EventSeatRadiusStep s) => s switch
    {
        EventSeatRadiusStep.Plus200 => "+200mi",
        EventSeatRadiusStep.Plus400 => "+400mi",
        _ => null,
    };

    /// <summary>★ The same quantisation the matcher uses (<c>MatchDistanceKey</c>): floor of
    /// miles plus a half, NOT <c>Math.Round</c>, which is ties-to-even. One ruler for the
    /// whole engine, so a school 300.4 miles out is inside a 300-mile draw here exactly as it
    /// would be anywhere else.</summary>
    private static int MteDistanceKey(double miles) => (int)Math.Floor(miles + 0.5);

    private enum EventSeatingStatus { Complete, SeatedShort }

    /// <summary>★ Whether the field has PLAYED, which is a different question from whether it
    /// is full. S97 writes NotPlayed on everything; S98 replaces the file with Completed and
    /// the finishes. Kept as an extensible string in the record so S98 can add a third word
    /// without a format break.</summary>
    private const string MtePlayStatusNotPlayed = "NotPlayed";

    private sealed record EventSeat(
        int Seat, int SchoolId, string SchoolName, int SlotIndex, EventSeatFallback Fallback,
        EventSeatRadiusStep RadiusStep = EventSeatRadiusStep.National);

    /// <summary>★ S104 — <c>Kind</c> travels with the seated event because every downstream
    /// consumer needs it and NONE of them may infer it. A four-seat field is a showcase or a
    /// four-team bracket depending on this word and on nothing else; inferring it from
    /// <c>FieldSize</c> is precisely the bug A3 exists to prevent.</summary>
    private sealed record SeatedEvent(
        int EventId, string Name, int Tier, int PlaceId, string PlaceName,
        string FirstDay, string LastDay, int FieldSize,
        EventSeatingStatus SeatingStatus, IReadOnlyList<EventSeat> Seats,
        string Kind = WorldEventKindTournament, int? Draw = null)
    {
        public bool IsShowcase => string.Equals(Kind, WorldEventKindShowcase, StringComparison.Ordinal);
    }

    /// <summary>★ S104 — ONE OF A SHOWCASE'S TWO GAMES, MATERIALIZED AT SEATING and long
    /// before anything plays. This is the whole point of separating materialization from
    /// simulation: the fixed obligation must EXIST before requests are built, because it is
    /// what a school's November is charged for.
    ///
    /// <para>★ R27 — the roles come from the STORED SEAT NUMBERS. Game 0 is seats 1 and 2,
    /// the headliner; game 1 is seats 3 and 4, the undercard. Never list position, never
    /// prestige-sorting, and never re-derived from a result — an upset must not relabel which
    /// game was authored as the nightcap.</para></summary>
    private sealed record ShowcasePairing(
        int EventId, int GameIndex, int SeatA, int SeatB, int SchoolAId, int SchoolBId);

    /// <summary>The two pairings a COMPLETE showcase owes, read off the stored seats. A short
    /// showcase owes none at all (R30), which is why this is only ever called on a field that
    /// completed.</summary>
    private static IReadOnlyList<ShowcasePairing> MteShowcasePairingsOf(SeatedEvent e)
    {
        if (!e.IsShowcase || e.SeatingStatus != EventSeatingStatus.Complete)
            return Array.Empty<ShowcasePairing>();
        var bySeat = e.Seats.ToDictionary(s => s.Seat);
        if (!bySeat.ContainsKey(1) || !bySeat.ContainsKey(2)
            || !bySeat.ContainsKey(3) || !bySeat.ContainsKey(4))
            throw new InvalidOperationException(
                $"SHOWCASE INVARIANT VIOLATED: {e.Name} is complete but does not hold seats 1-4; " +
                "the two games are named by stored seat number and cannot be assembled.");
        return new[]
        {
            new ShowcasePairing(e.EventId, 0, 1, 2, bySeat[1].SchoolId, bySeat[2].SchoolId),
            new ShowcasePairing(e.EventId, 1, 3, 4, bySeat[3].SchoolId, bySeat[4].SchoolId),
        };
    }

    /// <summary>Every showcase pairing this season owes, in the canonical (tier, id, game)
    /// order. The one place anything downstream asks "what did the showcases commit to".</summary>
    private static IReadOnlyList<ShowcasePairing> MteAllShowcasePairings(EventSeatingOutcome seating)
        => seating.Active.OrderBy(e => e.Tier).ThenBy(e => e.EventId)
                  .SelectMany(MteShowcasePairingsOf).ToList();

    /// <summary>★ How many showcase games each school owes this season — 0 or 1, because R25
    /// caps a school at one showcase. Read by the contract phase (so its capacity gate sees a
    /// fixed obligation that already exists) and by the request builder (so the game is
    /// charged rather than added).</summary>
    private static IReadOnlyDictionary<int, int> MteShowcaseObligations(EventSeatingOutcome seating)
    {
        var obligations = new Dictionary<int, int>();
        foreach (var p in MteAllShowcasePairings(seating))
        {
            obligations[p.SchoolAId] = obligations.GetValueOrDefault(p.SchoolAId, 0) + 1;
            obligations[p.SchoolBId] = obligations.GetValueOrDefault(p.SchoolBId, 0) + 1;
        }
        return obligations;
    }

    private sealed record DormantEvent(int EventId, string Name);

    /// <summary>Everything this season's draw decided. <c>PoolIsEmpty</c> is the zero path:
    /// a world with no authored events produces this and the page prints nothing at all.</summary>
    private sealed class EventSeatingOutcome
    {
        public required IReadOnlyList<SeatedEvent> Active { get; init; }
        public required IReadOnlyList<DormantEvent> Dormant { get; init; }
        public bool PoolIsEmpty => Active.Count == 0 && Dormant.Count == 0;

        public static readonly EventSeatingOutcome Empty = new()
        {
            Active = Array.Empty<SeatedEvent>(),
            Dormant = Array.Empty<DormantEvent>(),
        };
    }

    /// <summary>What happened to the permanent record. <c>NotApplicable</c> is legacy mode —
    /// no career, so there is nothing for a record to be about.</summary>
    private enum EventRecordStatus { NotApplicable, Written, WriteFailed }

    /// <summary>★ S98 — <c>FinishStatus</c> is the SECOND write of the season and it is a
    /// different event from the first. S97's <c>RecordStatus</c> says whether the NotPlayed
    /// record was published at the commit; this says whether the finishes were later written
    /// over it. A failure here leaves a season that was validly played and a year of event
    /// history that cannot say who won — the same deliberate hole S97 already models, and
    /// there is no retry inside the run.</summary>
    private sealed record EventSeasonOutcome(
        EventSeatingOutcome Seating, EventRecordStatus RecordStatus,
        string? RecordDiagnostic, IReadOnlyList<string> HistoryDiagnostics,
        EventRecordStatus FinishStatus = EventRecordStatus.NotApplicable,
        string? FinishDiagnostic = null)
    {
        public static readonly EventSeasonOutcome None = new(
            EventSeatingOutcome.Empty, EventRecordStatus.NotApplicable, null, Array.Empty<string>());
    }

    // ── What the last four seasons say ───────────────────────────────────────────

    /// <summary>The only two facts seating asks of history, and nothing wider is read.
    ///
    /// <para><c>SeatedInEvent</c> is the HARD exclusion — this school sat in THIS event
    /// inside the window. <c>Appearances</c> is the SOFT preference — how many events of any
    /// kind this school has been in. A hole year contributes zero to both: never a penalty,
    /// never an unknown.</para></summary>
    private sealed class MteHistory
    {
        public HashSet<(int EventId, int SchoolId)> SeatedInEvent { get; } = new();
        public Dictionary<int, int> Appearances { get; } = new();
        public List<string> Diagnostics { get; } = new();
        public static readonly MteHistory Empty = new();
    }

    // ── The conference cap key ───────────────────────────────────────────────────

    /// <summary>★ ONE SCHOOL PER LEAGUE PER FIELD, expressed as a key rather than as a rule
    /// with an exception. A school in a league that actually plays games keys on its
    /// conference; a school whose "conference" plays no games at all — the Independent
    /// container, fourteen schools in the stock world — keys on ITSELF.
    ///
    /// <para>The cap exists to force league variety into a field. An administrative container
    /// holding every unaffiliated school in the country is not a league, and treating it as
    /// one would let exactly one independent per tournament nationwide — for the schools
    /// whose ONLY basketball this is. Conference ids are positive and school ids are
    /// positive, so the school key is negated and the two can never collide.</para></summary>
    private static long MteConferenceCapKey(WorldSchool school, WorldConference conf)
        => conf.Games > 0 ? conf.Id : -(long)school.Id;

    /// <summary>Which of two drawn candidates a seat prefers. Prestige, and on a tie the lower
    /// school id — deliberately NOT a second draw, so the pull introduces no randomness of its
    /// own and a tie resolves the same way on every machine and every run.</summary>
    private static bool MteStronger(WorldSchool a, WorldSchool b)
        => a.CurrentPrestige != b.CurrentPrestige
            ? a.CurrentPrestige > b.CurrentPrestige
            : a.Id < b.Id;

    // ── Seating ──────────────────────────────────────────────────────────────────

    /// <summary>★ The whole draw, deterministic given (world, seasonSeed, history).
    ///
    /// <para>Activation is evaluated for EVERY event first, then the active ones sort by
    /// (tier, id) and seat one at a time. That ordering is load-bearing and is why activation
    /// cannot be folded into the seating loop: a tier-1 event must take its pick of the whole
    /// country before a tier-2 event touches it, regardless of the order they were authored
    /// in or what their ids happen to be.</para></summary>
    /// <param name="pull">Defaults to <see cref="MteSeatPull"/>. Overridable ONLY so Phase 88
    /// can run the flat draw as a negative control and prove the pull moves the league —
    /// nothing in production passes it.</param>
    private static EventSeatingOutcome MteSeatSeason(
        WorldFile world, long seasonSeed, MteHistory history, int? pull = null)
    {
        var seatPull = pull ?? MteSeatPull;
        if (world.Events.Count == 0) return EventSeatingOutcome.Empty;

        var confById = world.Conferences.ToDictionary(c => c.Id);
        var tierById = world.Tiers.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var placeById = world.Places.ToDictionary(p => p.PlaceId);

        // The pool, in canonical order once. Every candidate scan below walks THIS list, so
        // "sorted by school id" is a property of the pool rather than a sort repeated at
        // every level and possibly forgotten at one of them.
        var pool = world.Schools.OrderBy(s => s.Id).ToList();
        var capKeyOf = pool.ToDictionary(s => s.Id, s => MteConferenceCapKey(s, confById[s.ConferenceId]));
        var scopeOf = pool.ToDictionary(s => s.Id, s => tierById[confById[s.ConferenceId].TierId].EventScope);

        // ★ S104 — HAS THIS SCHOOL GOT A GAME TO GIVE? A showcase invitation costs one of the
        //   school's own games (R26), so a school with no open game cannot accept one. The
        //   floor is computed PESSIMISTICALLY — as if the school will also take a tournament
        //   seat — because seating order means a showcase can be offered before we know
        //   whether a later tournament will claim the same school, and a capacity answer that
        //   changes depending on what happens next is not an answer.
        //
        //   (28 − conference games) is that floor: a tournament-seated school plays 31 and
        //   spends 3 in its event, which is one FEWER open game than staying home with 29.
        //   No committed world comes near zero; this is the guard, not a live constraint.
        var openFloorOf = pool.ToDictionary(
            s => s.Id,
            s => NonConSeasonGamesSeated - NonConEventGames - confById[s.ConferenceId].Games);

        var active = new List<WorldEvent>();
        var dormant = new List<DormantEvent>();
        foreach (var e in world.Events.OrderBy(e => e.Id))
        {
            bool isActive;
            if (e.ForcedActive is { } forced) isActive = forced;
            else
            {
                var draw53 = MteHash64(seasonSeed, MteActivateDomain, e.Id) >> 11;
                isActive = draw53 < MtePersistenceThreshold53(e.Persistence);
            }
            if (isActive) active.Add(e);
            else dormant.Add(new DormantEvent(e.Id, e.Name));
        }

        // ★ S104 / R25 — TWO WALLS, ONE PER KIND. A school may play one tournament AND one
        //   showcase in the same season; it may never play two of either.
        var seatedByKind = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
        {
            [WorldEventKindTournament] = new(),
            [WorldEventKindShowcase] = new(),
        };
        // ★ THE DOUBLE-BOOKING EXCLUSION, newly possible because of R25. A school's
        //   tournament window and its showcase day can now collide, and nobody is in two
        //   places on one night. Seating order — (tier, id) — is the deterministic priority:
        //   whoever seats first keeps the school, and the later event looks elsewhere.
        //   Emmett's ruling (2026-08-06): "teams have to make choices."
        var seatedWindows = new Dictionary<int, List<(DateOnly First, DateOnly Last)>>();
        var seatedEvents = new List<SeatedEvent>();

        foreach (var e in active.OrderBy(x => x.Tier).ThenBy(x => x.Id))
        {
            var eventFirst = MteWindowDate(e.FirstDay);
            var eventLast = MteWindowDate(e.LastDay);
            var wall = seatedByKind[e.Kind];

            // ★ THE DRAW, resolved once per event. National events compute nothing at all,
            //   which is what keeps every pre-S104 world's seating byte-identical: no
            //   distance is measured, so no distance can round differently.
            Dictionary<int, int>? distanceKey = null;
            if (e.Draw is not null)
            {
                var home = placeById[e.PlaceId].Coordinate;
                distanceKey = pool.ToDictionary(
                    s => s.Id,
                    s => MteDistanceKey(GeoDistance.DistanceMiles(home, placeById[s.PlaceId].Coordinate)));
            }

            // ★ R28(c) — the radius steps, in order. A National event has exactly ONE step
            //   and it filters nothing; a radius event runs authored → +200 → +400 and then
            //   seats short. It never goes national: locality survives a bad year, and the
            //   bad year is visible instead of Arizona playing in Brooklyn.
            var radiusSteps = e.Draw is { } authored
                ? MteRadiusStepOffsets
                    .Select((off, i) => ((EventSeatRadiusStep)(i + 1), authored + off)).ToArray()
                : new[] { (EventSeatRadiusStep.National, int.MaxValue) };

            var seats = new List<EventSeat>();
            var capKeysInField = new HashSet<long>();
            // ★ R30 — PROVISIONAL. Seats accumulate here and reach the season-wide wall only
            //   when the field completes. For a TOURNAMENT the commit below is unconditional,
            //   which reproduces S97's per-seat immediate add exactly: within one field this
            //   set stands in for those adds, and across fields the commit happens before the
            //   next event begins. For a SHOWCASE a short field releases everything.
            var pending = new HashSet<int>();

            for (var slotIndex = 0; slotIndex < e.Slots.Count; slotIndex++)
            {
                var slot = e.Slots[slotIndex];
                EventSeat? filled = null;

                // ★ EACH SEAT RUNS ITS OWN LADDER FROM THE AUTHORED RADIUS. One seat widening
                //   never retroactively widens a seat that already filled — the seat that
                //   needed the reach is the seat that records it.
                foreach (var (stepWord, radius) in radiusSteps)
                {
                    // ★ THE BAND EXHAUSTS INSIDE EACH RADIUS STEP BEFORE THE NEXT OPENS
                    //   (R28c): a local showcase invites a weaker neighbour before it ever
                    //   reaches further. R31 — the full ladder runs to the floor, four-year
                    //   rule included, before the radius widens.
                    for (var level = 0; level <= 3 && filled is null; level++)
                    {
                        var applyBand = level < 1;
                        var applyScope = level < 2;
                        var applyFourYear = level < 3;

                        // ★ RECOMPUTED FROM THE FULL POOL AT EVERY LEVEL, never narrowed down
                        //   from the previous level's survivors. Relaxing the band must be able
                        //   to admit a school the band excluded, which a progressive filter
                        //   could never do.
                        var qualifiers = new List<WorldSchool>();
                        foreach (var s in pool)
                        {
                            // ── THE HARD CONSTRAINTS. None of these gives at any radius step
                            //    or any fallback level.
                            if (wall.Contains(s.Id) || pending.Contains(s.Id)) continue;  // absolute 1, per kind
                            if (capKeysInField.Contains(capKeyOf[s.Id])) continue;        // absolute 2
                            if (MteWindowTaken(seatedWindows, s.Id, eventFirst, eventLast)) continue;
                            // ★ Open-game capacity — HARD, and it gives at no radius step and
                            //   no fallback level. A school with nothing to spend cannot be
                            //   invited to spend it.
                            if (e.IsShowcase && openFloorOf[s.Id] < 1) continue;
                            // ★ Geography must be PRESENT: an event that measures distance
                            //   cannot seat a school it cannot locate. The world validator
                            //   guarantees every school's place exists, so a miss here is an
                            //   invariant failure rather than a case.
                            if (distanceKey is not null && distanceKey[s.Id] > radius) continue;

                            // ── THE SOFT LADDER, in the ruled order.
                            if (applyFourYear && history.SeatedInEvent.Contains((e.Id, s.Id))) continue;
                            if (applyScope && slot.Scope != "any" && scopeOf[s.Id] != slot.Scope) continue;
                            if (applyBand && (s.CurrentPrestige < slot.BandLo || s.CurrentPrestige > slot.BandHi)) continue;
                            qualifiers.Add(s);
                        }
                        if (qualifiers.Count == 0) continue;

                        // ★ THE SOFT PREFERENCE, APPLIED AT EVERY LEVEL INCLUDING THE LAST.
                        //   Fewest recent appearances wins outright, and the pull below only
                        //   chooses among schools who are equally overdue. Emmett's ruling
                        //   (2026-08-03): this stays. An MTE is something a programme does every
                        //   few years rather than annually, and the national turn-taking is what
                        //   expresses that. When it produced too few good teams in later seasons
                        //   the cause was the EVENTS ASKING TOO HIGH — hoovering up every elite
                        //   programme in one year and starving the next — not the rule.
                        var fewest = qualifiers.Min(s => history.Appearances.GetValueOrDefault(s.Id, 0));
                        var preferred = qualifiers
                            .Where(s => history.Appearances.GetValueOrDefault(s.Id, 0) == fewest)
                            .ToList();

                        // ★ THE PULL. Draw MteSeatPull times and seat the strongest drawn — an
                        //   event reaching for the best team it is allowed to take, without ever
                        //   being guaranteed it.
                        //
                        //   ★ THE KEY CARRIES THE RADIUS STEP, packed into the level slot as
                        //   `level + 4*step`. The hash takes four payload fields and all four
                        //   were already spent, so packing is how a widened seat gets its own
                        //   stream instead of replaying the draw it just failed. National is
                        //   step 0, so `level + 0` IS the pre-S104 key — every existing
                        //   tournament's draw is bit-identical by construction, not by luck.
                        var drawKey = level + 4 * (int)stepWord;
                        var pick = preferred[0];
                        if (preferred.Count > 1)
                        {
                            var best = -1;
                            for (var k = 0; k < seatPull; k++)
                            {
                                var idx = (int)(MteHash64(seasonSeed, MteSeatDomain, e.Id, slotIndex, drawKey, k)
                                                % (ulong)preferred.Count);
                                if (best < 0 || MteStronger(preferred[idx], preferred[best])) best = idx;
                            }
                            pick = preferred[best];
                        }

                        filled = new EventSeat(
                            seats.Count + 1, pick.Id, pick.Name, slotIndex,
                            (EventSeatFallback)level, stepWord);
                        pending.Add(pick.Id);
                        capKeysInField.Add(capKeyOf[pick.Id]);
                    }
                    if (filled is not null) break;   // this seat is done; the next starts at Base
                }

                if (filled is not null) seats.Add(filled);
            }

            var place = placeById[e.PlaceId];
            var status = seats.Count == e.FieldSize
                ? EventSeatingStatus.Complete
                : EventSeatingStatus.SeatedShort;

            // ★ R30 — THE COMMIT POINT, AND IT IS THE DESIGN.
            //   A tournament commits unconditionally: S97's behaviour, where a short field
            //   keeps its partial seating and its schools stay consumed, is untouched.
            //   A SHOWCASE that could not fill releases EVERYTHING — no school is consumed,
            //   no wall spent, no four-year clock burned — which is what makes the standby
            //   showcases real replacements rather than decoration. The record and the page
            //   still carry the attempted seating as diagnostics; the schools keep their
            //   season.
            var commits = !e.IsShowcase || status == EventSeatingStatus.Complete;
            if (commits)
            {
                foreach (var id in pending)
                {
                    wall.Add(id);
                    if (!seatedWindows.TryGetValue(id, out var windows))
                        seatedWindows[id] = windows = new List<(DateOnly, DateOnly)>();
                    windows.Add((eventFirst, eventLast));
                }
            }

            seatedEvents.Add(new SeatedEvent(
                e.Id, e.Name, e.Tier, e.PlaceId, place.Name,
                e.FirstDay, e.LastDay, e.FieldSize,
                status, commits ? seats : Array.Empty<EventSeat>(),
                e.Kind, e.Draw));
        }

        return new EventSeatingOutcome
        {
            Active = seatedEvents.OrderBy(x => x.Tier).ThenBy(x => x.EventId).ToList(),
            Dormant = dormant,
        };
    }

    /// <summary>★ Is this school already committed to a night inside this window? Closed
    /// intervals, so a one-day showcase collides with a tournament exactly when its day sits
    /// inside that tournament's window.
    ///
    /// <para>Only ever true ACROSS kinds in practice — the per-kind wall has already removed
    /// same-kind repeats — but it is written generally rather than assuming that, because an
    /// assumption about which pairs can reach here is exactly the kind of thing a later
    /// session invalidates silently.</para></summary>
    private static bool MteWindowTaken(
        IReadOnlyDictionary<int, List<(DateOnly First, DateOnly Last)>> seatedWindows,
        int schoolId, DateOnly first, DateOnly last)
    {
        if (!seatedWindows.TryGetValue(schoolId, out var windows)) return false;
        foreach (var (f, l) in windows)
            if (!(last < f || first > l)) return true;
        return false;
    }

    // ── The overlap refusal ──────────────────────────────────────────────────────

    /// <summary>★ A SEATED SCHOOL CANNOT HAVE A LEAGUE GAME INSIDE ITS EVENT'S WINDOW.
    ///
    /// <para>Unreachable on the stock world — the earliest conference night in the country is
    /// December 7 and every window ends in November — but the date layer permits a league to
    /// open as early as November 1, so the door is genuinely unlocked and this is the lock.
    /// It runs AFTER dating, because a window conflict is a fact about nights and nothing
    /// knows a night until the calendar has been laid over the slate.</para>
    ///
    /// <para>Conference games only, and the scope is deliberate: nothing else is on the
    /// schedule yet. When S98 puts tournament games on the calendar this widens.</para></summary>
    private static void MteRefuseOverlap(
        WorldFile world, EventSeatingOutcome seating, List<SeasonGame> dated)
    {
        if (seating.Active.Count == 0) return;

        var seatOf = new Dictionary<int, SeatedEvent>();
        foreach (var ev in seating.Active)
            foreach (var s in ev.Seats)
                seatOf[s.SchoolId] = ev;
        if (seatOf.Count == 0) return;

        var nameOf = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        foreach (var g in dated)
        {
            if (g.Date is not { } date) continue;
            foreach (var (schoolId, opponentId) in new[] { (g.HomeId, g.AwayId), (g.AwayId, g.HomeId) })
            {
                if (!seatOf.TryGetValue(schoolId, out var ev)) continue;
                var first = MteWindowDate(ev.FirstDay);
                var last = MteWindowDate(ev.LastDay);
                if (date < first || date > last) continue;
                throw new InvalidOperationException(
                    $"SEASON EVENT OVERLAP: {nameOf[schoolId]} is seated in {ev.Name} " +
                    $"({ev.FirstDay}..{ev.LastDay}) but has a conference game vs {nameOf[opponentId]} " +
                    $"on {date:MM-dd}. A school cannot be in two places on one night.");
            }
        }
    }

    /// <summary>A window endpoint resolved onto the season spine — the same halves the world
    /// validator used, so a date that loaded is a date that compares.</summary>
    private static DateOnly MteWindowDate(string monthDay)
    {
        var month = int.Parse(monthDay.AsSpan(0, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(monthDay.AsSpan(3, 2), CultureInfo.InvariantCulture);
        var year = month >= 7 ? SeasonDefaultStartYear : SeasonDefaultStartYear + 1;
        return new DateOnly(year, month, day);
    }

    // ── The permanent record ─────────────────────────────────────────────────────

    /// <summary>★ S103 — v2 adds two collections: the live contracts (forward state)
    /// and the non-conference pairing log (played... paired facts). The reader accepts
    /// BOTH versions: a v1 file is a pre-contract career, and bumping the constant
    /// without widening the read would silently erase every existing career's
    /// tournament memory — the four-year rule would stop working with every check
    /// green. A v1 record contributes its events exactly as before and reads as an
    /// EMPTY contract collection, never as unknown.</summary>
    private const int MteRecordFormatVersion = 2;
    private static readonly int[] MteSupportedRecordVersions = { 1, 2 };

    /// <summary>The folder is named for the history FILE, exactly as the game log's is, so two
    /// careers side by side cannot share a record directory and collide on season-1.</summary>
    private static string MteRecordFolderFor(string historyPath)
    {
        var full = Path.GetFullPath(historyPath);
        if (full.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) full = full[..^5];
        return full + ".events";
    }

    private static string MteRecordPathFor(string historyPath, long seasonId)
        => Path.Combine(MteRecordFolderFor(historyPath),
                        "season-" + seasonId.ToString(CultureInfo.InvariantCulture) + ".json");

    /// <summary>★ Read the four seasons the rules care about. Every failure is a HOLE — a
    /// missing file, a damaged one, a file from another career, a file that says it is a
    /// different season. A hole contributes zero facts and never disables its neighbours;
    /// one bad year does not cost a career its whole memory.
    ///
    /// <para>★ THE RECORD BINDS TO THE CAREER, NOT THE WORLD. The world fingerprint is
    /// written as provenance and deliberately never validated: a record is historical truth,
    /// and a school or event that no longer exists in the current world simply never matches
    /// a qualifier. Validating it would mean any world edit erases every field ever
    /// seated.</para></summary>
    private static MteHistory MteReadHistory(HistoryStore? history, long pendingSeasonId)
    {
        if (history is null) return MteHistory.Empty;
        var result = new MteHistory();
        for (var back = 1; back <= 4; back++)
        {
            var seasonId = pendingSeasonId - back;
            if (seasonId < 1) continue;
            var path = MteRecordPathFor(history.Path, seasonId);
            if (!File.Exists(path)) continue;                       // silent hole
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                { result.Diagnostics.Add($"season {seasonId}: malformed"); continue; }

                if (!root.TryGetProperty("formatVersion", out var fv) || !fv.TryGetInt32(out var version))
                { result.Diagnostics.Add($"season {seasonId}: malformed"); continue; }
                if (!MteSupportedRecordVersions.Contains(version))
                { result.Diagnostics.Add($"season {seasonId}: unsupported record version {version}"); continue; }

                if (!root.TryGetProperty("historyId", out var hid) || hid.ValueKind != JsonValueKind.String
                    || !string.Equals(hid.GetString(), history.HistoryId, StringComparison.Ordinal))
                { result.Diagnostics.Add($"season {seasonId}: record belongs to another career"); continue; }

                if (!root.TryGetProperty("seasonId", out var sid) || !sid.TryGetInt64(out var embedded)
                    || embedded != seasonId)
                { result.Diagnostics.Add($"season {seasonId}: record names a different season"); continue; }

                if (!root.TryGetProperty("events", out var evs) || evs.ValueKind != JsonValueKind.Array)
                { result.Diagnostics.Add($"season {seasonId}: malformed"); continue; }

                foreach (var ev in evs.EnumerateArray())
                {
                    if (!ev.TryGetProperty("eventId", out var eid) || !eid.TryGetInt32(out var eventId)) continue;
                    if (!ev.TryGetProperty("seats", out var seats) || seats.ValueKind != JsonValueKind.Array) continue;
                    foreach (var seat in seats.EnumerateArray())
                    {
                        if (!seat.TryGetProperty("schoolId", out var scid) || !scid.TryGetInt32(out var schoolId))
                            continue;
                        result.SeatedInEvent.Add((eventId, schoolId));
                        result.Appearances[schoolId] = result.Appearances.GetValueOrDefault(schoolId, 0) + 1;
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                result.Diagnostics.Add($"season {seasonId}: unreadable ({ex.GetType().Name})");
            }
        }
        return result;
    }

    /// <summary>★ The collision precheck, run BEFORE the season number is spent. A permanent
    /// record is never silently overwritten, and a season id is never burned beside a file
    /// this run did not produce — a stale record left standing is worse than a hole, because
    /// next season would read its fields as history that never happened.</summary>
    private static void MteRefuseExistingRecord(HistoryStore? history, long pendingSeasonId)
    {
        if (history is null) return;
        var path = MteRecordPathFor(history.Path, pendingSeasonId);
        if (File.Exists(path))
            throw new InvalidOperationException(
                $"SEASON EVENT RECORD COLLISION: '{path}' already exists for the season about to be " +
                "scheduled. Nothing has been reserved and nothing has been written. A permanent event " +
                "record is never overwritten — move or delete that file if the season is genuinely to " +
                "be replayed.");
    }

    /// <summary>Publish the season's record atomically and WITHOUT overwriting. The precheck
    /// above gives the actionable early refusal; this is the guard against the impossible
    /// late collision, and it fails loudly as an invariant violation rather than being
    /// classified as an ordinary write failure.</summary>
    private static void MtePublishRecord(
        HistoryStore history, long seasonId, long seasonSeed,
        WorldFile world, EventSeatingOutcome seating,
        IReadOnlyList<LiveContract> survivingContracts,
        IReadOnlyList<NonConPairingEntry> nonConferencePairings)
    {
        var folder = MteRecordFolderFor(history.Path);
        Directory.CreateDirectory(folder);
        var final = MteRecordPathFor(history.Path, seasonId);
        var temp = Path.Combine(folder, $".charm-events-{Guid.NewGuid():N}.tmp");

        var bytes = MteRecordBytes(history, seasonId, seasonSeed, world, seating,
                                   survivingContracts, nonConferencePairings);
        File.WriteAllBytes(temp, bytes);
        try
        {
            // overwrite:false — File.Move throws if the destination exists, which is exactly
            // the no-overwrite atomic publication this needs.
            File.Move(temp, final, overwrite: false);
        }
        catch (IOException) when (File.Exists(final))
        {
            try { File.Delete(temp); } catch (IOException) { /* best effort */ }
            throw new InvalidOperationException(
                $"SEASON EVENT RECORD INVARIANT VIOLATED: '{final}' appeared between the collision " +
                "precheck and publication. The season id has been spent and no record was written.");
        }
    }

    private static byte[] MteRecordBytes(
        HistoryStore history, long seasonId, long seasonSeed,
        WorldFile world, EventSeatingOutcome seating,
        IReadOnlyList<LiveContract> survivingContracts,
        IReadOnlyList<NonConPairingEntry> nonConferencePairings)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", MteRecordFormatVersion);
            w.WriteString("historyId", history.HistoryId);
            // ★ Provenance only, never validated on read. See MteReadHistory.
            w.WriteString("worldFingerprint", history.WorldFingerprint);
            w.WriteNumber("seasonId", seasonId);
            w.WriteNumber("seasonSeed", seasonSeed);

            // ★ S103 — the two v2 collections, ALWAYS written even when empty: the
            //   contract reader treats a missing array as damage, never as "no
            //   contracts", so absence can never be mistaken for emptiness (A2).
            WriteLiveContracts(w, survivingContracts);
            WriteNonConferencePairings(w, nonConferencePairings);

            w.WriteStartArray("dormantEvents");
            foreach (var d in seating.Dormant.OrderBy(x => x.EventId))
            {
                w.WriteStartObject();
                w.WriteNumber("eventId", d.EventId);
                w.WriteString("name", d.Name);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("events");
            foreach (var e in seating.Active)
            {
                w.WriteStartObject();
                // ★ EVERY DISPLAY FACT IS SNAPSHOTTED. A permanent history page must never
                //   need the current world to reconstruct what it said years ago.
                w.WriteNumber("eventId", e.EventId);
                w.WriteString("name", e.Name);
                w.WriteNumber("tier", e.Tier);
                w.WriteNumber("placeId", e.PlaceId);
                w.WriteString("placeName", e.PlaceName);
                w.WriteString("firstDay", e.FirstDay);
                w.WriteString("lastDay", e.LastDay);
                w.WriteNumber("fieldSize", e.FieldSize);
                w.WriteString("seatingStatus", e.SeatingStatus.ToString());
                w.WriteString("playStatus", MtePlayStatusNotPlayed);
                w.WriteStartArray("seats");
                foreach (var s in e.Seats)
                {
                    w.WriteStartObject();
                    w.WriteNumber("seat", s.Seat);
                    w.WriteNumber("schoolId", s.SchoolId);
                    w.WriteString("schoolName", s.SchoolName);
                    w.WriteNumber("slotIndex", s.SlotIndex);
                    w.WriteString("fallback", MteFallbackWord(s.Fallback));
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteNull("finishBySeat");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    // ── S98: the record is REPLACED, never rebuilt ───────────────────────────────

    /// <summary>★ S98 — the second word <c>playStatus</c> may hold. It gains no third: a field
    /// either played to a full placement or it did not play at all (a dormant or short event
    /// simply has no games). See the S98 ruling — a short field is a data error, not a
    /// modelled state.</summary>
    private const string MtePlayStatusCompleted = "Completed";

    /// <summary>★ S98 — REPLACED, NEVER REBUILT. The file on disk is the authority for who was
    /// in the tournament; this reads it, validates it, and writes it back with the finishes
    /// filled in. It never re-seats from the current world, because a world edited between the
    /// commit and now would silently rewrite history.
    ///
    /// <para>Validated before a byte is written: the format version, the career it belongs to,
    /// the season it names, that every event still says <c>NotPlayed</c>, that no finishes are
    /// already present, and that the seats on disk are the seats the games were actually
    /// played between. The first five are corruption tripwires and throw; there is no reopen
    /// path for a completed season (a career-bound season always reserves a NEW id, and S97's
    /// collision precheck refuses before spending anything), so a record already marked
    /// Completed means something is wrong rather than something is finished.</para>
    ///
    /// <para>Atomic by the same means as the first write: a whole temp file, then one rename.
    /// A failure before the rename leaves the NotPlayed record byte-identical and the season
    /// still valid and played — the page says the finishes could not be persisted, and there
    /// is no retry inside the run.</para>
    ///
    /// <para><paramref name="replace"/> is the INJECTED SEAM, and it exists so a check can
    /// force a failure at the rename without reaching for file permissions, an invalid path,
    /// OS locking or a global mutable switch — every one of which would prove something about
    /// the filesystem rather than about this method.</para></summary>
    /// <param name="showcaseResults">★ S104 — the showcase half. A showcase COMPLETES with two
    /// results and NO PLACEMENT: its <c>finishBySeat</c> stays null forever, which is the
    /// honest record of an event nobody wins. The alternative the review named — synthesising
    /// a fake placement of winner-1/loser-2 twice — passes the existing validator and quietly
    /// invents a champion, so "played" is carried by <paramref name="seatsPlayedByEvent"/>
    /// rather than by the presence of finishes.</param>
    private static void MteReplaceRecordWithFinishes(
        HistoryStore history, long seasonId,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> finishBySeatByEvent,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> seatsPlayedByEvent,
        IReadOnlyList<ShowcaseResult>? showcaseResults = null,
        Action<string, string>? replace = null)
    {
        var final = MteRecordPathFor(history.Path, seasonId);
        var existing = File.ReadAllBytes(final);

        using var doc = JsonDocument.Parse(existing);
        var root = doc.RootElement;

        void Refuse(string why) => throw new InvalidOperationException(
            $"SEASON EVENT RECORD REFUSED: '{final}' {why}. Nothing was written and the record on " +
            "disk is untouched.");

        if (root.ValueKind != JsonValueKind.Object) Refuse("is not an object");
        if (!root.TryGetProperty("formatVersion", out var fv) || !fv.TryGetInt32(out var version)
            || version != MteRecordFormatVersion)
            Refuse($"is not format version {MteRecordFormatVersion.ToString(CultureInfo.InvariantCulture)}");
        if (!root.TryGetProperty("historyId", out var hid) || hid.ValueKind != JsonValueKind.String
            || !string.Equals(hid.GetString(), history.HistoryId, StringComparison.Ordinal))
            Refuse("belongs to another career");
        if (!root.TryGetProperty("seasonId", out var sid) || !sid.TryGetInt64(out var embedded)
            || embedded != seasonId)
            Refuse("names a different season");
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            Refuse("has no events array");

        var seen = new HashSet<int>();
        foreach (var ev in events.EnumerateArray())
        {
            if (!ev.TryGetProperty("eventId", out var eid) || !eid.TryGetInt32(out var eventId))
                Refuse("holds an event with no id");
            else seen.Add(eventId);

            if (!ev.TryGetProperty("playStatus", out var ps) || ps.ValueKind != JsonValueKind.String
                || !string.Equals(ps.GetString(), MtePlayStatusNotPlayed, StringComparison.Ordinal))
                Refuse($"event {eid.GetInt32().ToString(CultureInfo.InvariantCulture)} is not " +
                       $"{MtePlayStatusNotPlayed} — a completed season is never reopened");
            if (!ev.TryGetProperty("finishBySeat", out var fin) || fin.ValueKind != JsonValueKind.Null)
                Refuse($"event {eid.GetInt32().ToString(CultureInfo.InvariantCulture)} already carries finishes");

            if (!seatsPlayedByEvent.TryGetValue(eid.GetInt32(), out var played)) continue;

            // ★ The seats on disk ARE the field that played. Compared as a whole map, so a
            //   swapped pair — same schools, different seats — is caught as readily as a
            //   missing one.
            var onDisk = new Dictionary<int, int>();
            if (ev.TryGetProperty("seats", out var seats) && seats.ValueKind == JsonValueKind.Array)
                foreach (var s in seats.EnumerateArray())
                    if (s.TryGetProperty("seat", out var sn) && sn.TryGetInt32(out var seatNo)
                        && s.TryGetProperty("schoolId", out var sc) && sc.TryGetInt32(out var schoolId))
                        onDisk[seatNo] = schoolId;
            if (onDisk.Count != played.Count
                || played.Any(kv => !onDisk.TryGetValue(kv.Key, out var got) || got != kv.Value))
                Refuse($"event {eid.GetInt32().ToString(CultureInfo.InvariantCulture)}'s seats on disk " +
                       "are not the field these games were played between");
        }

        foreach (var eventId in finishBySeatByEvent.Keys)
            if (!seen.Contains(eventId))
                Refuse($"carries no event {eventId.ToString(CultureInfo.InvariantCulture)} to finish");
        foreach (var eventId in seatsPlayedByEvent.Keys)
            if (!seen.Contains(eventId))
                Refuse($"carries no event {eventId.ToString(CultureInfo.InvariantCulture)} that played");

        var bytes = MteRecordBytesWithFinishes(
            root, finishBySeatByEvent, seatsPlayedByEvent, showcaseResults);

        var folder = MteRecordFolderFor(history.Path);
        var temp = Path.Combine(folder, $".charm-events-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temp, bytes);
        try
        {
            (replace ?? MteDefaultReplace)(temp, final);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { /* best effort */ }
            throw;
        }
    }

    /// <summary>The real rename. Overwrite is correct here and only here: the destination is
    /// this run's own NotPlayed record, published minutes earlier at the commit.</summary>
    private static void MteDefaultReplace(string temp, string final)
        => File.Move(temp, final, overwrite: true);

    /// <summary>Re-emit the record with the finishes filled in. Every property that is not
    /// <c>playStatus</c> or <c>finishBySeat</c> is copied through verbatim, so nothing this
    /// session did not decide can change — including the world fingerprint, which stays
    /// provenance and stays unvalidated.</summary>
    private static byte[] MteRecordBytesWithFinishes(
        JsonElement root,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> finishBySeatByEvent,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> seatsPlayedByEvent,
        IReadOnlyList<ShowcaseResult>? showcaseResults)
    {
        var resultsByEvent = (showcaseResults ?? Array.Empty<ShowcaseResult>())
            .GroupBy(r => r.EventId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.GameIndex).ToList());

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (!string.Equals(prop.Name, "events", StringComparison.Ordinal))
                {
                    prop.WriteTo(w);
                    continue;
                }
                w.WriteStartArray("events");
                foreach (var ev in prop.Value.EnumerateArray())
                {
                    var eventId = ev.GetProperty("eventId").GetInt32();
                    finishBySeatByEvent.TryGetValue(eventId, out var finishes);
                    // ★ PLAYED IS ITS OWN FACT, carried by the seats-played map rather than
                    //   inferred from the presence of a placement — which is exactly how a
                    //   showcase records "this happened, and nobody won it".
                    var played = seatsPlayedByEvent.ContainsKey(eventId);
                    resultsByEvent.TryGetValue(eventId, out var games);
                    w.WriteStartObject();
                    foreach (var f in ev.EnumerateObject())
                    {
                        if (string.Equals(f.Name, "playStatus", StringComparison.Ordinal))
                        {
                            w.WriteString("playStatus",
                                played ? MtePlayStatusCompleted : MtePlayStatusNotPlayed);
                        }
                        else if (string.Equals(f.Name, "finishBySeat", StringComparison.Ordinal))
                        {
                            if (finishes is null)
                            {
                                w.WriteNull("finishBySeat");
                                // ★ The showcase's two results ride BESIDE the null placement,
                                //   never in place of it. A reader that wants "who won the
                                //   event" gets null and is right; a reader that wants "what
                                //   happened that night" gets both games.
                                if (games is not null)
                                {
                                    w.WriteStartArray("games");
                                    foreach (var g in games)
                                    {
                                        w.WriteStartObject();
                                        w.WriteNumber("game", g.GameIndex);
                                        w.WriteNumber("seatA", g.SeatA);
                                        w.WriteNumber("seatB", g.SeatB);
                                        w.WriteNumber("scoreA", g.ScoreA);
                                        w.WriteNumber("scoreB", g.ScoreB);
                                        w.WriteEndObject();
                                    }
                                    w.WriteEndArray();
                                }
                                continue;
                            }
                            // ★ An ARRAY of seat/place pairs ordered by seat, not an object with
                            //   numeric keys: the seats above are an array of objects and this is
                            //   the same fact keyed the same way, so the file stays one shape.
                            w.WriteStartArray("finishBySeat");
                            foreach (var kv in finishes.OrderBy(x => x.Key))
                            {
                                w.WriteStartObject();
                                w.WriteNumber("seat", kv.Key);
                                w.WriteNumber("place", kv.Value);
                                w.WriteEndObject();
                            }
                            w.WriteEndArray();
                        }
                        else f.WriteTo(w);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    // ── The page ─────────────────────────────────────────────────────────────────

    /// <summary>★ WHEN THE AUTHORED POOL IS EMPTY THIS PRINTS NOTHING — no heading, no blank
    /// line — which is what makes the zero-path byte-identity claim honest rather than
    /// approximately true. A pool that exists but drew every event dormant prints only the
    /// dormant line: a different case, and a different assertion.
    ///
    /// <para>Page-only throughout. No field composition is ever suite-asserted.</para></summary>
    private static IReadOnlyList<string> MtePageLines(
        EventSeasonOutcome outcome,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>>? finishes = null,
        IReadOnlyList<ShowcaseResult>? showcaseResults = null)
    {
        var seating = outcome.Seating;
        if (seating.PoolIsEmpty) return Array.Empty<string>();

        var resultsByEvent = (showcaseResults ?? Array.Empty<ShowcaseResult>())
            .GroupBy(r => r.EventId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.GameIndex).ToList());

        var lines = new List<string>();
        foreach (var e in seating.Active)
        {
            var sb = new StringBuilder();
            // ★ S104 — the kind is SAID OUT LOUD, and the draw with it. A reader must never
            //   have to count seats to work out whether they are looking at a four-team
            //   bracket or a showcase.
            var kindNote = e.IsShowcase
                ? (e.Draw is { } r
                    ? $"showcase, {r.ToString(CultureInfo.InvariantCulture)}mi"
                    : "showcase, national")
                : $"tier {e.Tier}";
            sb.Append($"  {e.Name} ({kindNote}, {e.PlaceName}, ");
            sb.Append(e.IsShowcase ? $"{e.FirstDay}): " : $"{e.FirstDay}..{e.LastDay}): ");

            IReadOnlyDictionary<int, int>? placeBySeat = null;
            finishes?.TryGetValue(e.EventId, out placeBySeat);

            if (e.IsShowcase && resultsByEvent.TryGetValue(e.EventId, out var games))
            {
                // ★ PLAYED. Roles from the STORED SEATS (R27), never from the scores: the
                //   headliner is the headliner even when the undercard was the better game.
                var nameOf = e.Seats.ToDictionary(s => s.Seat, s => s.SchoolName);
                sb.Append(string.Join("; ", games.Select(g =>
                {
                    var label = g.GameIndex == 0 ? "headliner" : "undercard";
                    return $"{label} {nameOf[g.SeatA]} {g.ScoreA.ToString(CultureInfo.InvariantCulture)}" +
                           $"-{g.ScoreB.ToString(CultureInfo.InvariantCulture)} {nameOf[g.SeatB]}";
                })));
            }
            else if (placeBySeat is { Count: > 0 })
            {
                // ★ S98 — once a bracket has PLAYED the page prints the finish order.
                sb.Append(string.Join(", ", e.Seats
                    .Where(s => placeBySeat.ContainsKey(s.Seat))
                    .OrderBy(s => placeBySeat[s.Seat])
                    .Select(s => $"{placeBySeat[s.Seat]}. {s.SchoolName}")));
            }
            else
            {
                sb.Append(e.Seats.Count == 0
                    ? "no field"
                    : string.Join(", ", e.Seats.Select(s =>
                    {
                        // ★ TWO INDEPENDENT NOTES, because they are two independent facts:
                        //   how far the standards dropped, and how far the map widened.
                        var notes = new[] { MteFallbackPageNote(s.Fallback), MteRadiusStepPageNote(s.RadiusStep) }
                            .Where(n => n is not null).ToList();
                        return notes.Count == 0
                            ? s.SchoolName
                            : $"{s.SchoolName} [{string.Join(", ", notes)}]";
                    })));
            }

            if (e.SeatingStatus == EventSeatingStatus.SeatedShort)
                sb.Append(e.IsShowcase
                    // ★ R30 — a short showcase says so AND says what it cost, which is
                    //   nothing. That distinction is the whole ruling and the page carries it.
                    ? $" — SHORT — NOT PLAYED, field released (nobody consumed)"
                    : $" — SHORT ({e.Seats.Count}/{e.FieldSize}) — NOT PLAYED");
            lines.Add(sb.ToString());
        }
        if (seating.Dormant.Count > 0)
            lines.Add($"  Dormant this season: {string.Join(", ", seating.Dormant.Select(d => d.Name))}");
        foreach (var d in outcome.HistoryDiagnostics)
            lines.Add($"  Event history hole — {d}");
        if (outcome.RecordStatus == EventRecordStatus.WriteFailed)
            lines.Add($"  EVENT RECORD NOT WRITTEN — {outcome.RecordDiagnostic} " +
                      "(the season is valid and played; this year is a hole in event history)");
        if (outcome.FinishStatus == EventRecordStatus.WriteFailed)
            lines.Add($"  EVENT FINISHES NOT PERSISTED — {outcome.FinishDiagnostic} " +
                      "(the tournaments were played and the games count; the permanent record " +
                      "cannot say who won)");
        return lines;
    }
}
