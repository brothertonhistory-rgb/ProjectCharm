using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S110 — THE CONFERENCE TOURNAMENTS. Thirty-one leagues, thirty-one champions.
//
//  S109 built the single-elimination primitive and left it dormant. This file is
//  its first consumer: every league seats a field of eight from its OWN league
//  record, hands that order to BuildKnockoutBracket, plays the topology round by
//  round, and crowns a champion.
//
//  ── EMMETT'S RULING (2026-09-07): EIGHT TEAMS IN EVERY LEAGUE ──────────────
//  "All I want is eight everywhere. We will deal with byes and all the complicated
//  conference tournaments later."  So `TourneyTeams` is NOT read — nineteen leagues
//  author a non-power-of-two field and all of them play eight. Byes are out of
//  scope; the primitive refuses non-power-of-two fields BY NAME and that refusal
//  is not weakened here.
//
//  ── ★ EXECUTED LAST, AFTER THE SHOWCASES ──────────────────────────────────
//  The fixture ordinal is BOTH the engine seed input (base + 2g, base + 2g + 1)
//  and the retention log's ordinal. Conference games hold 0..N-1, MTE brackets
//  and showcases append after them, and these games append after THOSE. Slotting
//  them anywhere earlier would slide every event game's ordinal along and re-roll
//  every tournament result in the country. Do not "fix" the ordering.
//
//  ── ★ THE RECORD THAT SEEDS IS THE LEAGUE RECORD, NOT THE SEASON RECORD ────
//  `run.Wins` / `run.Losses` are WHOLE-SEASON and already include MTE games, so a
//  Maui run would move ACC seeding. Seeding therefore runs on records derived from
//  the season's own conference schedule — and derived from the SCHEDULE, never from
//  `schoolA.ConferenceId == schoolB.ConferenceId`, which would classify these very
//  games as league play the moment they are played.
//
//  ── ★ TWO RULED PLACEHOLDERS, BOTH TEMPORARY BY EMMETT'S EXPLICIT INTENT ───
//  Emmett, 2026-09-07: "Those are fine with me for now, we will add in neutral
//  locations and tiebreakers later."
//
//   R1 — THE VENUE. The original No. 1 seed's city, resolved ONCE per conference
//        and frozen: all seven games carry that one PlaceId. Not the higher seed
//        per matchup, not the highest survivor per round — a conference tournament
//        does not travel. Nobody hosts (HasHost: false), so no home-court advantage
//        and no hosted-game accounting. SUCCESSOR: real neutral sites, authored per
//        conference in the world file. Boarded as an open item; it does not fade.
//
//   R2 — SEEDING TIES. Conference win percentage, then the LOWER SCHOOL ID. This is
//        the canonical tie-break everywhere in this codebase and it is deterministic
//        — and it is an ACKNOWLEDGED PLACEHOLDER. The head-to-head ladder that
//        status.md's "Next approved candidate" block described as "already ruled" is
//        in NO ruling: nothing in journal.md or design.md corroborates any
//        head-to-head procedure, tied-block handling, or deterministic draw. It was
//        an unproven hypothesis written from a session summary. SUCCESSOR: real
//        conference tiebreaking, its own design object. Boarded too.
//
//  ★ Neither ruling may be quietly widened. A city that varies by round, or a tie
//  broken by anything other than school id, is out of scope even if it looks more
//  realistic.
//
//  ── ★ A LEAGUE THAT CANNOT SEAT EIGHT HOLDS NO TOURNAMENT ──────────────────
//  Every fixture world in the tree has leagues of five and six; stock's smallest is
//  eight. A hard "every league seats eight" assertion would throw on the first
//  fixture-world season and take the whole suite down with it. So a league with
//  fewer than eight members holds NO tournament, and the count of such leagues is
//  carried out and reported rather than vanishing.
//
//  This is NOT the failure A3 names. `.Take(8)` can never yield seven, because the
//  gate is checked before the take and the take is asserted afterwards. A short
//  field is refused, never played short.
// ============================================================================

internal static partial class Program
{
    /// <summary>Emmett's ruling: eight everywhere. Not read from <c>TourneyTeams</c>, which
    /// stays authored and unread exactly as <c>TDay1..5</c> do.</summary>
    private const int ConfTourneyFieldSize = 8;

    /// <summary>Single elimination is exactly N − 1.</summary>
    private const int ConfTourneyGamesPerLeague = ConfTourneyFieldSize - 1;

    /// <summary>A tournament POSITION, which is what a reserved game number belongs to — never
    /// a team. The pairings are unknown when the numbers are spent (round two is round one's
    /// result), so an id belongs to a slot exactly as <c>BracketSlotKey</c> does for the MTE
    /// fields.</summary>
    private readonly record struct ConfTourneySlotKey(int ConferenceId, int GameIndex);

    /// <summary>One school's record over its CONFERENCE slate only.</summary>
    private readonly record struct ConfTourneyRecord(int Wins, int Losses)
    {
        public int Played => Wins + Losses;
    }

    /// <summary>A league's field with its seeds settled and its one frozen city.
    /// <c>SchoolBySeed</c> index 0 is seed 1.</summary>
    private sealed record ConfTourneyPlan(
        int ConferenceId, string ConferenceName, DateOnly Open, int PlaceId,
        IReadOnlyList<int> SchoolBySeed);

    /// <summary>One played tournament game, in the canonical row shape the fingerprint hashes.
    /// <c>SeedA</c>/<c>SchoolA</c> is the nominal home side — the better ORIGINAL seed.</summary>
    private sealed record ConfTourneyGameRow(
        int ConferenceId, int Round, int GameIndex, DateOnly Date, int PlaceId,
        int SeedA, int SeedB, int SchoolA, int SchoolB, int Winner);

    /// <summary>★ A CHAMPION IS FIRST-CLASS STATE, NOT SOMETHING SCRAPED BACK OUT OF SEVEN
    /// GAMES. Held on the run outcome and read by the page; nothing reconstructs it by
    /// inspecting results.</summary>
    private sealed record ConfTourneyChampion(
        int ConferenceId, string ConferenceName, int Champion, int RunnerUp,
        IReadOnlyList<int> SchoolBySeed);

    /// <summary>What the conference tournaments did this season.</summary>
    private sealed record ConfTourneySeasonOutcome(
        IReadOnlyList<ConfTourneyChampion> Champions,
        IReadOnlyList<ConfTourneyGameRow> Rows,
        int GameCount, int LeaguesPlayed, int LeaguesTooSmall)
    {
        internal static readonly ConfTourneySeasonOutcome None = new(
            Array.Empty<ConfTourneyChampion>(), Array.Empty<ConfTourneyGameRow>(), 0, 0, 0);
    }

    /// <summary>Which leagues hold a tournament, and how many were refused for being short.</summary>
    private sealed record ConfTourneyFieldSet(
        IReadOnlyList<(int ConferenceId, IReadOnlyList<int> Members)> Seating, int TooSmall);

    // ── Step 1: which leagues hold one at all ──────────────────────────────────

    /// <summary>★ THE CANONICAL WALK: ASCENDING CONFERENCE ID, INDEPENDENT EXCLUDED BY NAME.
    /// Reservations, play and the fingerprint all walk THIS, so none of them leans on a
    /// dictionary's enumeration order. Ascending conference id is already the codebase's
    /// convention at thirteen sites.
    ///
    /// <para>★ INDEPENDENT IS IDENTIFIED BY <c>Games == 0</c>, THE WAY THE SEASON ALREADY DOES
    /// IT (see <c>Program.Checks.Independents.cs</c> and <c>SeasonDateSchedule</c>). It is
    /// deliberately NOT identified by <c>TourneyTeams == 0</c> — that test would also catch the
    /// Ivy row and silently drop a real league — and not by a null tournament offset either,
    /// though Independent is in fact the only league authoring one.</para></summary>
    private static ConfTourneyFieldSet ConfTourneyFields(WorldFile world)
    {
        var byConf = new Dictionary<int, List<int>>();
        foreach (var s in world.Schools)
        {
            if (!byConf.TryGetValue(s.ConferenceId, out var l)) byConf[s.ConferenceId] = l = new();
            l.Add(s.Id);
        }

        var seating = new List<(int, IReadOnlyList<int>)>();
        var tooSmall = 0;
        foreach (var c in world.Conferences.OrderBy(c => c.Id))
        {
            if (c.Games == 0) continue;                                  // a conference of Independents
            if (!byConf.TryGetValue(c.Id, out var members)) continue;    // authored with no schools
            if (members.Count < ConfTourneyFieldSize) { tooSmall++; continue; }
            seating.Add((c.Id, members.OrderBy(x => x).ToList()));
        }
        return new ConfTourneyFieldSet(seating, tooSmall);
    }

    /// <summary>Every position a game number must be reserved for, in the canonical walk order.
    /// The COUNT is known before the first tip even though the pairings are not: seven per
    /// seating league. <c>CloseReservations()</c> runs before play, so an id not spent here
    /// cannot be spent anywhere.</summary>
    private static List<ConfTourneySlotKey> ConfTourneyExpectedSlots(WorldFile world)
    {
        var slots = new List<ConfTourneySlotKey>();
        foreach (var (cid, _) in ConfTourneyFields(world).Seating)
            for (var i = 0; i < ConfTourneyGamesPerLeague; i++)
                slots.Add(new ConfTourneySlotKey(cid, i));
        return slots;
    }

    // ── Step 2: the league record, from the schedule and never from the rosters ─

    /// <summary>★ EVERY SCHOOL'S RECORD OVER THE CONFERENCE SLATE, DERIVED FROM THE SCHEDULE
    /// THAT PLAYED. <c>schedule</c> is conference-only by construction (a bracket cannot exist
    /// before it is played), and <c>results[i]</c> describes <c>schedule[i]</c> — the alignment
    /// is asserted below rather than assumed, because it is the one premise this whole layer
    /// rests on.
    ///
    /// <para>★ WHY NOT "both schools are in the same conference": that test would classify the
    /// tournament's own games as league play the moment they are played, and the recomputation
    /// check in Phase 99 would then be comparing a contaminated number to itself. The
    /// source-derived slate is authoritative and the tournament games are simply not in
    /// it.</para></summary>
    private static Dictionary<int, ConfTourneyRecord> ConfTourneyConferenceRecords(
        WorldFile world, IReadOnlyList<SeasonGame> schedule, IReadOnlyList<SeasonGameResult> results)
    {
        if (results.Count < schedule.Count)
            throw new InvalidOperationException(
                $"CONFERENCE TOURNAMENT: the league slate holds " +
                $"{schedule.Count.ToString(CultureInfo.InvariantCulture)} games but only " +
                $"{results.Count.ToString(CultureInfo.InvariantCulture)} results exist; the conference " +
                "record cannot be derived from a season that has not finished its league slate.");

        var wins = world.Schools.ToDictionary(s => s.Id, _ => 0);
        var losses = world.Schools.ToDictionary(s => s.Id, _ => 0);
        for (var i = 0; i < schedule.Count; i++)
        {
            var r = results[i];
            if (r.HomeId != schedule[i].HomeId || r.AwayId != schedule[i].AwayId)
                throw new InvalidOperationException(
                    $"CONFERENCE TOURNAMENT: result {i.ToString(CultureInfo.InvariantCulture)} describes " +
                    $"{r.HomeId.ToString(CultureInfo.InvariantCulture)} v " +
                    $"{r.AwayId.ToString(CultureInfo.InvariantCulture)} but league fixture " +
                    $"{i.ToString(CultureInfo.InvariantCulture)} is " +
                    $"{schedule[i].HomeId.ToString(CultureInfo.InvariantCulture)} v " +
                    $"{schedule[i].AwayId.ToString(CultureInfo.InvariantCulture)}; the fixture ordinal " +
                    "is not the results index and the conference record would be a fiction.");
            if (r.HomeScore > r.AwayScore) { wins[r.HomeId]++; losses[r.AwayId]++; }
            else if (r.AwayScore > r.HomeScore) { wins[r.AwayId]++; losses[r.HomeId]++; }
        }
        return world.Schools.ToDictionary(s => s.Id, s => new ConfTourneyRecord(wins[s.Id], losses[s.Id]));
    }

    // ── Step 3: the seed order ─────────────────────────────────────────────────

    /// <summary>★ THE SEED ORDER — the same MECHANISM as <c>SeasonStandingsOrder</c>, fed
    /// league-only records. Win PERCENTAGE by INTEGER CROSS-MULTIPLICATION, never a float
    /// division, widened to <c>long</c> deliberately: the ordering is then exact and
    /// platform-independent, which a double comparison is not.
    ///
    /// <para>R2: ties break on the LOWER SCHOOL ID. A school with no league games sorts below
    /// everyone who played, and two of them break by id — the same shape the standings use, so
    /// a league that somehow played nothing still produces a total order rather than
    /// throwing.</para></summary>
    private static Comparison<int> ConfTourneySeedOrder(IReadOnlyDictionary<int, ConfTourneyRecord> records)
        => (a, b) =>
        {
            var ra = records[a];
            var rb = records[b];
            if (ra.Played == 0 || rb.Played == 0)
                return ra.Played == rb.Played ? a.CompareTo(b) : (ra.Played == 0 ? 1 : -1);
            var cmp = ((long)rb.Wins * ra.Played).CompareTo((long)ra.Wins * rb.Played);
            return cmp != 0 ? cmp : a.CompareTo(b);
        };

    /// <summary>The top eight of one league, best league record first.</summary>
    private static List<int> ConfTourneySeedField(
        int conferenceId, IReadOnlyList<int> members,
        IReadOnlyDictionary<int, ConfTourneyRecord> records)
    {
        if (members.Count < ConfTourneyFieldSize)
            throw new InvalidOperationException(
                $"CONFERENCE TOURNAMENT: conference {conferenceId.ToString(CultureInfo.InvariantCulture)} " +
                $"has {members.Count.ToString(CultureInfo.InvariantCulture)} members and cannot seat a field " +
                $"of {ConfTourneyFieldSize.ToString(CultureInfo.InvariantCulture)}; a short league holds no " +
                "tournament and must never be taken short.");

        var order = members.ToList();
        order.Sort(ConfTourneySeedOrder(records));
        var field = order.Take(ConfTourneyFieldSize).ToList();

        // ★ The take is asserted rather than trusted — this is the whole point of the gate
        //   above, and a silent seven is exactly the failure it exists to prevent.
        if (field.Count != ConfTourneyFieldSize || field.Distinct().Count() != ConfTourneyFieldSize)
            throw new InvalidOperationException(
                $"CONFERENCE TOURNAMENT: conference {conferenceId.ToString(CultureInfo.InvariantCulture)} " +
                $"seated {field.Distinct().Count().ToString(CultureInfo.InvariantCulture)} distinct schools " +
                $"for {ConfTourneyFieldSize.ToString(CultureInfo.InvariantCulture)} seeds.");
        return field;
    }

    // ── Step 4: the nights ─────────────────────────────────────────────────────

    /// <summary>★ THE TOURNAMENT'S DATES ARE NOT INVENTED — they fall out of a number the world
    /// already authors. <c>TourneyOffsetDays</c> is live: the conference dater already walls
    /// every league's last league game at <c>SelectionSunday − offset − 1</c>. So the tournament
    /// opens at <c>SelectionSunday − offset</c>, and the league's last legal game is the day
    /// before it BY CONSTRUCTION rather than by a check.
    ///
    /// <para>Rounds are consecutive: open, open + 1, open + 2. <c>TDay1..5</c> stay authored and
    /// unread. Leagues stagger deliberately (13 at 11 days, 12 at 4, 6 at 8 on stock) and
    /// overlap freely — they are on separate floors.</para></summary>
    private static DateOnly ConfTourneyOpen(int startYear, int conferenceId, int? offsetDays)
    {
        if (offsetDays is null)
            throw new InvalidOperationException(
                $"CONFERENCE TOURNAMENT: conference {conferenceId.ToString(CultureInfo.InvariantCulture)} " +
                "plays a league slate but authors no tournament day ('none'); a league that plays cannot " +
                "have nowhere to put its tournament. Fix the world, not this arithmetic.");
        if (offsetDays < 0)
            throw new InvalidOperationException(
                $"CONFERENCE TOURNAMENT: conference {conferenceId.ToString(CultureInfo.InvariantCulture)} " +
                $"authors a negative tournament offset {offsetDays.Value.ToString(CultureInfo.InvariantCulture)}.");
        return CharmCalendar.ThirdSundayInMarch(startYear + 1).AddDays(-offsetDays.Value);
    }

    // ── Step 5: the one factory ────────────────────────────────────────────────

    /// <summary>★ EVERY CONFERENCE TOURNAMENT GAME IS AN ORDINARY <c>SeasonGame</c>, BUILT IN
    /// ONE PLACE. This is the only site permitted to construct one.
    ///
    /// <para><c>HasHost</c> defaults to <c>true</c>, so a forgotten <c>false</c> would silently
    /// HOST a neutral-floor game and every check would stay green. Centralising construction is
    /// what removes that failure mode. There is no host-school field on <c>SeasonGame</c> — the
    /// host/venue distinction is carried entirely by this bool, which is the existing
    /// convention the MTE and showcase factories already follow.</para>
    ///
    /// <para>The nominal home side is the better ORIGINAL seed — the lower seed number — which
    /// is cosmetic and deterministic: it decides box-score ordering and which side's PlayerIds
    /// are stamped first. It is NEVER read as a venue. Nobody hosts this.</para>
    ///
    /// <para>The Kind is <c>"ctourney"</c> and not <c>"conf"</c>: the retention log's
    /// conference flag keys on that literal string, and <c>"mte"</c> is asserted by name to mean
    /// an EVENT fixture. A third kind of played game gets a third word.</para></summary>
    private static SeasonGame ConfTourneyBuildGame(
        ConfTourneyPlan plan, int round, int gameIndex, int homeSeed, int awaySeed,
        IReadOnlyDictionary<ConfTourneySlotKey, GameId> reservations, SeasonId? seasonId)
    {
        if (homeSeed >= awaySeed)
            throw new InvalidOperationException(
                $"CONFERENCE TOURNAMENT: {plan.ConferenceName} game " +
                $"{gameIndex.ToString(CultureInfo.InvariantCulture)} was handed its sides out of seed " +
                "order; the better original seed is the nominal home side.");

        // ★ Legacy mode — no career, so no season number and no game numbers, exactly as legacy
        //   conference, tournament and showcase fixtures all behave. Basketball does not require
        //   a career file. In history mode the reservation must exist: the lock shuts before the
        //   first tip, so a missing id cannot be repaired anywhere later in the run.
        GameId? gameId = null;
        if (seasonId is not null)
        {
            var key = new ConfTourneySlotKey(plan.ConferenceId, gameIndex);
            if (!reservations.TryGetValue(key, out var reserved))
                throw new InvalidOperationException(
                    $"CONFERENCE TOURNAMENT: no game number was reserved for {plan.ConferenceName} slot " +
                    $"{gameIndex.ToString(CultureInfo.InvariantCulture)}. The lock shuts before play, so " +
                    "this cannot be repaired inside the run.");
            gameId = reserved;
        }

        return new SeasonGame(
            "ctourney",
            plan.SchoolBySeed[homeSeed - 1],
            plan.SchoolBySeed[awaySeed - 1],
            seasonId,
            gameId,
            plan.Open.AddDays(round),
            HasHost: false,
            PlaceId: plan.PlaceId);   // R1 — the original No. 1 seed's city, frozen for all seven
    }

    // ── Step 6: playing them ───────────────────────────────────────────────────

    /// <summary>Seat, seed and play every league's tournament.
    ///
    /// <para><paramref name="play"/> runs one prepared fixture through the ordinary season-game
    /// execution path and hands back its score — the identical path a conference game takes.
    /// Keeping the engine on the other side of a delegate is what lets this file own the
    /// tournament and own nothing else.</para>
    ///
    /// <para>The topology comes from <c>BuildKnockoutBracket</c> and is NOT re-derived here: the
    /// canonical seed line is the contract, and it already produces 1v8, 4v5, 2v7, 3v6 and folds
    /// so that 1 and 2 can meet only in the final. Seeds are resolved into occupancy as results
    /// arrive — round one reads its two seeds off the topology, every later game is filled by
    /// the winner arrow pointing at it.</para></summary>
    private static ConfTourneySeasonOutcome ConfTourneyPlaySeason(
        WorldFile world, int startYear,
        IReadOnlyList<SeasonGame> schedule, IReadOnlyList<SeasonGameResult> results,
        IReadOnlyDictionary<ConfTourneySlotKey, GameId> reservations,
        SeasonId? seasonId, int firstFixtureOrdinal,
        Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> play)
    {
        var fields = ConfTourneyFields(world);
        if (fields.Seating.Count == 0)
            return ConfTourneySeasonOutcome.None with { LeaguesTooSmall = fields.TooSmall };

        var records = ConfTourneyConferenceRecords(world, schedule, results);
        var confById = world.Conferences.ToDictionary(c => c.Id);
        var placeOfSchool = world.Schools.ToDictionary(s => s.Id, s => s.PlaceId);

        var champions = new List<ConfTourneyChampion>();
        var rows = new List<ConfTourneyGameRow>();
        var ordinal = firstFixtureOrdinal;

        foreach (var (cid, members) in fields.Seating)
        {
            var conf = confById[cid];
            var field = ConfTourneySeedField(cid, members, records);
            var plan = new ConfTourneyPlan(
                cid, conf.Name,
                ConfTourneyOpen(startYear, cid, conf.TourneyOffsetDays),
                placeOfSchool[field[0]],          // R1 — the No. 1 seed's city, resolved ONCE
                field);

            var bracket = BuildKnockoutBracket(ConfTourneyFieldSize, field);
            var occupancy = new int[bracket.Games.Count][];
            for (var i = 0; i < occupancy.Length; i++) occupancy[i] = new[] { 0, 0 };

            var champion = 0;
            var runnerUp = 0;

            foreach (var g in bracket.Games)
            {
                if (g.A is KnockoutSide.Seed sa) occupancy[g.GameIndex][0] = sa.Number;
                if (g.B is KnockoutSide.Seed sb) occupancy[g.GameIndex][1] = sb.Number;
                var a = occupancy[g.GameIndex][0];
                var b = occupancy[g.GameIndex][1];
                if (a == 0 || b == 0)
                    throw new InvalidOperationException(
                        $"CONFERENCE TOURNAMENT: {plan.ConferenceName} game " +
                        $"{g.GameIndex.ToString(CultureInfo.InvariantCulture)} came up with an empty side; " +
                        "the topology does not fill every game before it is played.");

                var homeSeed = Math.Min(a, b);
                var awaySeed = Math.Max(a, b);
                var game = ConfTourneyBuildGame(
                    plan, g.Round, g.GameIndex, homeSeed, awaySeed, reservations, seasonId);

                var played = new PlayedSeasonGame(
                    game, ordinal,
                    HomeOriginalSeed: homeSeed, AwayOriginalSeed: awaySeed,
                    ConferenceTournamentId: cid, ConfTourneyGameIndex: g.GameIndex);

                var (homeScore, awayScore) = play(played);
                ordinal++;

                //  A tie is impossible — the engine plays overtime until somebody wins — and if
                //  one ever arrived it must not silently advance the nominal home side.
                if (homeScore == awayScore)
                    throw new InvalidOperationException(
                        $"CONFERENCE TOURNAMENT: {plan.ConferenceName} game " +
                        $"{g.GameIndex.ToString(CultureInfo.InvariantCulture)} ended level; a knockout game " +
                        "has no route for a tie.");

                var winnerSeed = homeScore > awayScore ? homeSeed : awaySeed;
                var loserSeed = homeScore > awayScore ? awaySeed : homeSeed;

                rows.Add(new ConfTourneyGameRow(
                    cid, g.Round, g.GameIndex, game.Date!.Value, plan.PlaceId,
                    homeSeed, awaySeed,
                    plan.SchoolBySeed[homeSeed - 1], plan.SchoolBySeed[awaySeed - 1],
                    plan.SchoolBySeed[winnerSeed - 1]));

                if (g.WinnerTo is { } to)
                {
                    occupancy[to][g.WinnerToSlot!.Value] = winnerSeed;
                }
                else
                {
                    champion = bracket.ParticipantOfSeed(winnerSeed);
                    runnerUp = bracket.ParticipantOfSeed(loserSeed);
                }
            }

            if (champion == 0)
                throw new InvalidOperationException(
                    $"CONFERENCE TOURNAMENT: {plan.ConferenceName} played its whole bracket and crowned " +
                    "nobody; the topology's final routed a winner somewhere.");

            champions.Add(new ConfTourneyChampion(cid, conf.Name, champion, runnerUp, field));
        }

        var played1 = ordinal - firstFixtureOrdinal;
        if (played1 != fields.Seating.Count * ConfTourneyGamesPerLeague)
            throw new InvalidOperationException(
                $"CONFERENCE TOURNAMENT: {fields.Seating.Count.ToString(CultureInfo.InvariantCulture)} leagues " +
                $"played {played1.ToString(CultureInfo.InvariantCulture)} games; single elimination is exactly " +
                $"{ConfTourneyGamesPerLeague.ToString(CultureInfo.InvariantCulture)} a league.");

        return new ConfTourneySeasonOutcome(
            champions, rows, played1, fields.Seating.Count, fields.TooSmall);
    }

    // ── The sixth fingerprint ──────────────────────────────────────────────────

    /// <summary>★ THE CONFERENCE TOURNAMENTS GET THEIR OWN HASH, AND THEY DO NOT LAND INSIDE
    /// ANYBODY ELSE'S. The event-games hash covers the world event pool and S104's
    /// zero-path-by-subtraction proof depends on it meaning exactly that; if these games landed
    /// in it, that proof would quietly stop meaning anything while every check stayed green.
    /// The same argument applies to the results+possessions hash, which is why Phase 93 and
    /// Phase 99 assert that one over its pre-S110 PREFIX rather than recapturing it.
    ///
    /// <para>The canonical row is stated here so the hash's meaning is decided rather than
    /// inherited: conference, round, game index, date, place, both seeds, both schools, winner.
    /// A hash of results alone would not catch a venue or a date regression.</para>
    ///
    /// <para>Shape mirrors <c>SeasonFingerprint</c>, <c>ScheduleFingerprint</c> and
    /// <c>MteEventGamesFingerprint</c> deliberately (the sibling rule): ordered lines, '|'
    /// between fields, LF terminator including the last, invariant culture, UTF-8 without a BOM,
    /// SHA-256, lowercase hex.</para></summary>
    private static string ConfTourneyFingerprint(IReadOnlyList<ConfTourneyGameRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.ConferenceId).ThenBy(r => r.GameIndex))
            sb.Append(r.ConferenceId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Round.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.GameIndex.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.PlaceId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.SeedA.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.SeedB.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.SchoolA.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.SchoolB.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Winner.ToString(CultureInfo.InvariantCulture)).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
