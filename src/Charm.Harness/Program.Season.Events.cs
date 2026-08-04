using System.Globalization;
using System.Text;
using System.Text.Json;
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

    private enum EventSeatingStatus { Complete, SeatedShort }

    /// <summary>★ Whether the field has PLAYED, which is a different question from whether it
    /// is full. S97 writes NotPlayed on everything; S98 replaces the file with Completed and
    /// the finishes. Kept as an extensible string in the record so S98 can add a third word
    /// without a format break.</summary>
    private const string MtePlayStatusNotPlayed = "NotPlayed";

    private sealed record EventSeat(
        int Seat, int SchoolId, string SchoolName, int SlotIndex, EventSeatFallback Fallback);

    private sealed record SeatedEvent(
        int EventId, string Name, int Tier, int PlaceId, string PlaceName,
        string FirstDay, string LastDay, int FieldSize,
        EventSeatingStatus SeatingStatus, IReadOnlyList<EventSeat> Seats);

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

    private sealed record EventSeasonOutcome(
        EventSeatingOutcome Seating, EventRecordStatus RecordStatus,
        string? RecordDiagnostic, IReadOnlyList<string> HistoryDiagnostics)
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

        var seatedThisSeason = new HashSet<int>();
        var seatedEvents = new List<SeatedEvent>();

        foreach (var e in active.OrderBy(x => x.Tier).ThenBy(x => x.Id))
        {
            var seats = new List<EventSeat>();
            var capKeysInField = new HashSet<long>();

            for (var slotIndex = 0; slotIndex < e.Slots.Count; slotIndex++)
            {
                var slot = e.Slots[slotIndex];
                EventSeat? filled = null;

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
                        if (seatedThisSeason.Contains(s.Id)) continue;             // absolute 1
                        if (capKeysInField.Contains(capKeyOf[s.Id])) continue;     // absolute 2
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
                    var pick = preferred[0];
                    if (preferred.Count > 1)
                    {
                        var best = -1;
                        for (var k = 0; k < seatPull; k++)
                        {
                            var idx = (int)(MteHash64(seasonSeed, MteSeatDomain, e.Id, slotIndex, level, k)
                                            % (ulong)preferred.Count);
                            if (best < 0 || MteStronger(preferred[idx], preferred[best])) best = idx;
                        }
                        pick = preferred[best];
                    }

                    filled = new EventSeat(
                        seats.Count + 1, pick.Id, pick.Name, slotIndex, (EventSeatFallback)level);
                    seatedThisSeason.Add(pick.Id);
                    capKeysInField.Add(capKeyOf[pick.Id]);
                }

                if (filled is not null) seats.Add(filled);
            }

            var place = placeById[e.PlaceId];
            seatedEvents.Add(new SeatedEvent(
                e.Id, e.Name, e.Tier, e.PlaceId, place.Name,
                e.FirstDay, e.LastDay, e.FieldSize,
                seats.Count == e.FieldSize ? EventSeatingStatus.Complete : EventSeatingStatus.SeatedShort,
                seats));
        }

        return new EventSeatingOutcome
        {
            Active = seatedEvents.OrderBy(x => x.Tier).ThenBy(x => x.EventId).ToList(),
            Dormant = dormant,
        };
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

    private const int MteRecordFormatVersion = 1;

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
                if (version != MteRecordFormatVersion)
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
        WorldFile world, EventSeatingOutcome seating)
    {
        var folder = MteRecordFolderFor(history.Path);
        Directory.CreateDirectory(folder);
        var final = MteRecordPathFor(history.Path, seasonId);
        var temp = Path.Combine(folder, $".charm-events-{Guid.NewGuid():N}.tmp");

        var bytes = MteRecordBytes(history, seasonId, seasonSeed, world, seating);
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
        WorldFile world, EventSeatingOutcome seating)
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

    // ── The page ─────────────────────────────────────────────────────────────────

    /// <summary>★ WHEN THE AUTHORED POOL IS EMPTY THIS PRINTS NOTHING — no heading, no blank
    /// line — which is what makes the zero-path byte-identity claim honest rather than
    /// approximately true. A pool that exists but drew every event dormant prints only the
    /// dormant line: a different case, and a different assertion.
    ///
    /// <para>Page-only throughout. No field composition is ever suite-asserted.</para></summary>
    private static IReadOnlyList<string> MtePageLines(EventSeasonOutcome outcome)
    {
        var seating = outcome.Seating;
        if (seating.PoolIsEmpty) return Array.Empty<string>();

        var lines = new List<string>();
        foreach (var e in seating.Active)
        {
            var sb = new StringBuilder();
            sb.Append($"  {e.Name} (tier {e.Tier}, {e.PlaceName}, {e.FirstDay}..{e.LastDay}): ");
            sb.Append(e.Seats.Count == 0
                ? "no field"
                : string.Join(", ", e.Seats.Select(s =>
                {
                    var note = MteFallbackPageNote(s.Fallback);
                    return note is null ? s.SchoolName : $"{s.SchoolName} [{note}]";
                })));
            if (e.SeatingStatus == EventSeatingStatus.SeatedShort)
                sb.Append($" — SHORT ({e.Seats.Count}/{e.FieldSize})");
            lines.Add(sb.ToString());
        }
        if (seating.Dormant.Count > 0)
            lines.Add($"  Dormant this season: {string.Join(", ", seating.Dormant.Select(d => d.Name))}");
        foreach (var d in outcome.HistoryDiagnostics)
            lines.Add($"  Event history hole — {d}");
        if (outcome.RecordStatus == EventRecordStatus.WriteFailed)
            lines.Add($"  EVENT RECORD NOT WRITTEN — {outcome.RecordDiagnostic} " +
                      "(the season is valid and played; this year is a hole in event history)");
        return lines;
    }
}
