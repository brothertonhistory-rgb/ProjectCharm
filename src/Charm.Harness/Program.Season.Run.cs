using System.Globalization;
using System.Reflection;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S123 — THE SEASON THAT CAN STOP (save design, Phase A).
//
//  Until this session a season was one call: set up, play November 1 to the
//  championship Monday, close. Now it is an object:
//
//      var run = SeasonBegin(world, seed, configPath, verbose, options);
//      run.AdvanceThrough(new DateOnly(2026, 12, 15));
//      run.AdvanceThrough(new DateOnly(2027, 1, 3));
//      run.AdvanceThrough(run.LastLegalDay);
//      var outcome = run.Finish();
//
//  NOTHING ABOUT THE BASKETBALL CHANGES (K8). The set-up, the game and the close are
//  S122's code, moved: the set-up into the constructor, the game (`PlayOneGame`, S98's
//  local function) into an instance method, the close into `Finish`. Every value the
//  game or the close read from the set-up is a FIELD, named exactly as the local it was.
//
//  ★ S124 (B1a) — A SEASON WRITES NOTHING UNTIL IT CLOSES.
//    SeasonBegin    — writes nothing. The numbers are held in the career store's memory;
//                     the event record's NotPlayed body and the log's roster are built
//                     in memory.
//    AdvanceThrough — writes nothing.
//    Finish         — every write, in this order, each only for a career season and under
//                     the same condition as before: (1) ★ S126 check that every name it
//                     will write is free (an unfinished close's files are listed by no save,
//                     so the career's OPEN set them aside and nothing reads them), (2) the log, (3) the
//                     scouting file, (4) the event record, written once, (5) ★ S125 the
//                     season's league state, (6) THE SAVE — the career file listing every
//                     file above by hash, swapped in with the previous save kept as its
//                     backup — LAST. The outcome is built before (1), so once (6) has
//                     replaced the career file nothing after it can fail: the season is
//                     committed. A season stopped anywhere before (6) leaves the career file
//                     byte-identical and is played again, identically, next run.
//
//  ★ THE INVENTORY (K6). Every instance field of this class, of SeasonCalendarRun and
//  of the five game sources is named in Phase 114's frozen list, by name, type and
//  category; a field added or removed without updating the list fails the suite. The
//  category comment on each field below is the same one the list carries. The list is
//  a TOP-LEVEL inventory: it does not see inside the stats accumulator, behind a
//  reference, or into a callback. Saving completeness is B2's to prove.
//
//  ★ LIFECYCLE (K9). A precondition refusal (backwards, past the end, Finish early or
//  twice) is raised before anything plays and leaves the run usable. Any exception
//  while playing or closing faults the run: the original propagates, every later call
//  is refused by name, and the run is never retried in memory. ★ S124 — the run holds
//  no file during play (the season log's writer lives only inside the close), and it
//  never touches the career store's lifetime, which belongs to the caller; the career
//  lock is released when the caller disposes the store. An abandoned or faulted run
//  leaves the career file exactly as it was before the season.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ S123 (K7) — RunSeasonCore's optional parameters as one record. Every default is
    /// the parameter's default, so an absent option is the method as it was.</summary>
    private sealed record SeasonRunOptions(
        HistoryStore? History = null,
        bool RetainGameLog = false,
        int? RoadShaveOverride = null,
        int? DebtWindowOverride = null,
        IReadOnlyDictionary<int, int>? ContractChoiceOverride = null,
        bool BuyGamesOffForTest = false,
        DivvyResult? RostersInHand = null,
        bool BootstrapPeopleForTest = false,
        int? SeasonYear = null,
        SeasonPlayOrder? PlayOrderForTest = null,
        Action<string>? CloseStepForTest = null);

    /// <summary>★ S123 — the season's set-up, run now; the season is then played by
    /// <see cref="SeasonRun.AdvanceThrough"/> and closed by <see cref="SeasonRun.Finish"/>.</summary>
    private static SeasonRun SeasonBegin(
        WorldFile world, long seasonSeed, string engineConfigPath, bool verbose, SeasonRunOptions? options = null)
        => new SeasonRun(world, seasonSeed, engineConfigPath, verbose, options ?? new SeasonRunOptions());

    private sealed class SeasonRun : IDisposable
    {
        // ── Immutable set-up data (persist, or rebuild from verified inputs) ─────────
        private readonly HistoryStore? history;                            // resource: the caller's career store; never disposed here
        private readonly long pendingSeasonId;
        private readonly int seasonYearOfRun;
        private readonly MteHistory eventHistory;
        private readonly ContractSeasonOutcome contracts;
        private readonly EventSeatingOutcome seating;
        private readonly NonConferenceReport nonConference;
        private readonly MatchingReport matching;
        private readonly List<SeasonGame> schedule;
        private readonly SeasonMemoryOutcome memoryOutcome;
        private readonly SeasonRotationOutcome rotationOutcome;
        private readonly NonConDateReport nonConferenceDates;
        private readonly byte[]? eventRecordBody;                         // ★ S124 — the NotPlayed record, built at set-up, written at the close
        private readonly bool keepsLog;                                   // ★ S124 — was `gameLog is not null`
        private readonly List<RosterEntryV2>? logRoster;                  // ★ S124 — the log's roster, built at set-up, written at the close
        private readonly bool verbose;                                    // the season's printed reports
        private readonly DivvyResult divvy;
        private readonly PeopleSummary? people;
        private readonly DevelopmentStep? development;
        private readonly DevState[]? devStates;
        private readonly Dictionary<int, List<GenPlayerRow>> rowsBySchool;
        private readonly GenEngineConfigs cfgs;
        private readonly int roadShave;
        private readonly int baseSeed;
        private readonly long seasonSeed;                                 // ★ S127 — the seed as given; a save records it

        // ── Derived values (recompute only where equivalence is proven) ─────────────
        private readonly string fingerprint;
        private readonly string datedFingerprint;
        private readonly int eventGameCount;
        private readonly int leaguesTooSmall;
        private readonly int totalGames;

        // ── Mutable simulation data (persist and restore exactly) ───────────────────
        private readonly SeasonLeagueStats league;                         // ONE entry; its nested inventory is B2's
        private readonly Dictionary<int, int> wins;
        private readonly Dictionary<int, int> losses;
        private int ties;
        private int hostedRoadSidesShaved;
        private int leagueRoadSidesShaved;
        private readonly Dictionary<int, long> secondsByPool;
        private readonly Dictionary<int, int> teamGames;
        private readonly int?[] possessionSlots;
        private readonly SidePossessionCount?[] sideSlots;
        private readonly GamePutbackAudit?[] putbackSlots;
        private readonly GameLaneAudit?[] laneSlots;
        private readonly PlayedSeasonGame?[] playedSlots;
        private readonly (GameBlockFactsV1 Facts, List<PerGameStatRowV1> Rows)?[] logSlots;
        private readonly List<MteBracketSource> bracketSources;            // each source's own fields are inventoried
        private readonly MteShowcaseSource showcaseSource;
        private readonly List<ConfTourneySource> confTourneySources;
        private readonly BuyGameSource buySource;
        private readonly SeasonCalendarRun calendar;                       // the walk; its own fields are inventoried

        // ── Shared reference (restore the relationship, not just the values) ────────
        //  ★ The SAME list every ConfTourneySource reads its league's results from. Created once
        //    in the set-up, before the sources, and never replaced (K5).
        private readonly List<SeasonGameResult?> resultSlots;

        // ── Resources and callbacks (recreate or rebind; never data) ────────────────
        //  ★ S124 — the season log's writer is no longer a field: it is a local of the close.
        private readonly Action<string>? closeStepForTest;                 // ★ S124 (K6) — null on every production path
        private readonly WorldFile world;                                   // ★ S125 — the close compares and copies its prestige (K5)
        private readonly string engineConfigPath;                           // ★ S125 — the settings file the season's rules entry hashes (K4)

        // ── The run's lifecycle (K9) ────────────────────────────────────────────────
        private bool finished;
        private bool disposed;
        private string? faultedOn;
        private bool advancing;                                           // ★ S127 (K2) — a day or the close is running; no save

        public SeasonRun(WorldFile world, long seasonSeed, string engineConfigPath, bool verbose, SeasonRunOptions options)
        {
            //  The options, under the parameter names the set-up has always read.
            history = options.History;
            this.seasonSeed = seasonSeed;
            this.verbose = verbose;
            this.world = world;
            this.engineConfigPath = engineConfigPath;
            closeStepForTest = options.CloseStepForTest;
            var retainGameLog = options.RetainGameLog;
            var roadShaveOverride = options.RoadShaveOverride;
            var debtWindowOverride = options.DebtWindowOverride;
            var contractChoiceOverride = options.ContractChoiceOverride;
            var buyGamesOffForTest = options.BuyGamesOffForTest;
            var rostersInHand = options.RostersInHand;
            var bootstrapPeopleForTest = options.BootstrapPeopleForTest;
            var seasonYear = options.SeasonYear;

            // ★ S114 — rosters handed in (the turnover's season two) skip the draft and nothing
            //   else: every consumer below reads Pool / Rosters / PersonIds and the pool by index,
            //   and the turnover hands over a dense pool. Omitted — every existing caller — the
            //   method is the method it was (Phase 104 C10). ★ S115 retired the S114 refusal of
            //   (rosters in hand + history): rosters handed in on a career must simply carry their
            //   identity map, which the S89 contract check below enforces.
            //
            // ★ S115 — THE RETAINED LOG IS MANDATORY ON A CAREER. A career's people live in its
            //   season logs, so a history-bound season that kept no log would be a year the career
            //   cannot continue from. The `season` command has passed retainGameLog: true since S90;
            //   the only callers that bind a history without a log are suite checks about schedules,
            //   events, contracts and memory, which say so by name with `bootstrapPeopleForTest` — and
            //   that flag also means "build fresh people this season, as every season did before
            //   S115", so those checks keep their exact behaviour. The command never sets it.
            if (history is not null && !retainGameLog && !bootstrapPeopleForTest)
                throw new InvalidOperationException(
                    "S115: a career season keeps its people in its season log; history mode requires the retained log. " +
                    "(A suite check that wants fresh people every season says so with bootstrapPeopleForTest.)");
            // ══════════════════════════════════════════════════════════════════════════
            //  ★ S97 — THE SEASON PIPELINE, IN THIS ORDER, AND THE ORDER IS THE CONTRACT.
            //
            //    1. peek the season about to be scheduled   (a read; nothing is spent)
            //    2. read the last four seasons' event records
            //    3. draw activation and seat every active field
            //    4. build the conference slate                        (unchanged)
            //    5. date it                                           (unchanged)
            //    6. refuse a seated school double-booked in its window
            //    7. (S97's refusal of a record already claiming the pending season is
            //       retired at S124: the close sets such a file aside instead)
            //    8. take the season and game numbers                  (held in memory)
            //    9. build the permanent record's NotPlayed body       (in memory)
            //
            //    ★ S124 — NOTHING here writes. The numbers are spent, and the record and
            //    every other season file written, only at the close, with the career file
            //    LAST; a season that stops anywhere before then has spent nothing.
            // ══════════════════════════════════════════════════════════════════════════
            pendingSeasonId = history?.PeekNextSeasonId ?? 0;
            // ★ S119 — THE SEASON'S YEAR, resolved ONCE, at step 1, and handed to every dater.
            //   A career's year is read off its file (startYear + seasons already played), never
            //   supplied by the caller — so a caller cannot play a career's season in the wrong
            //   year, and naming one alongside a career is refused here rather than ignored. A
            //   season with no career plays the year it was given, else the default (2026), which is
            //   every run before S119. A career whose next season would open past the last season the
            //   calendar holds is refused HERE, before a single number is spent.
            if (history is not null)
            {
                if (seasonYear is not null)
                    throw new InvalidOperationException(
                        "S119: a career's season year comes off its career file; RunSeasonCore was handed one as well.");
                var y = (long)history.StartYear + (pendingSeasonId - 1);
                if (y > SeasonMaxStartYear)
                    throw new HistoryException(HistoryError.YearOutOfDomain,
                        $"this career started in {history.StartYear.ToString(CultureInfo.InvariantCulture)} and its next " +
                        $"season would open in {y.ToString(CultureInfo.InvariantCulture)}; the last season the calendar " +
                        $"holds opens in {SeasonMaxStartYear.ToString(CultureInfo.InvariantCulture)}. " +
                        "Nothing was played or written.");
                seasonYearOfRun = (int)y;
            }
            else
            {
                seasonYearOfRun = seasonYear ?? SeasonDefaultStartYear;
                if (seasonYearOfRun < SeasonMinStartYear || seasonYearOfRun > SeasonMaxStartYear)
                    throw new InvalidOperationException(
                        $"S119: season year {seasonYearOfRun.ToString(CultureInfo.InvariantCulture)} is outside " +
                        $"{SeasonMinStartYear}..{SeasonMaxStartYear}.");
            }
            // ★ S115 — LAST SEASON'S PEOPLE, read here at step 1 so every refusal (no log, a pre-S115
            //   log, a damaged roster) stops the run before a single number is spent. prev == 0 is the
            //   first season of a career: the bootstrap pool, as today.
            CareerPeople? careerPeople = null;
            if (history is not null && rostersInHand is null && !bootstrapPeopleForTest && pendingSeasonId - 1 >= 1)
                careerPeople = ReadCareerPeople(history, world, pendingSeasonId - 1);
            // ★ S121 — THE CAMP'S INPUTS, read at step 1 on the same terms: the Development dials (an
            //   edit that breaks an equation refuses here, before a number is spent) and, on a career's
            //   second season onward, last season's scouting file — ★ S126 the one the career LISTS.
            //   A missing or damaged file refuses by name; potential is never re-rolled.
            DevelopmentConfig? devCfg = null;
            IReadOnlyList<ScoutingRecord>? previousScouting = null;
            if (history is not null && retainGameLog)
            {
                devCfg = DevelopmentConfig.Load(engineConfigPath);
                if (careerPeople is not null)
                    previousScouting = ScoutingFile.Read(history.ListedPath(CareerFileKind.Scouting, careerPeople.PreviousSeasonId), new ScoutingBindings(
                        history.HistoryId, history.WorldFingerprint, careerPeople.PreviousSeasonId));
            }
            eventHistory = MteReadHistory(history, pendingSeasonId);
            // ★ S103 — last season's promises, read from EXACTLY season N-1's record.
            var contractLoad = ReadLiveContracts(history, pendingSeasonId);
            seating = MteSeatSeason(world, seasonSeed, eventHistory, startYear: seasonYearOfRun);
            // ★ S103 — the contract phase, ONCE, before anything else touches a school's
            //   slate (R23): terminate → discover → reserve → validate globally → commit.
            //   The step itself is pure over supplied state, so phase ownership is a
            //   property of this spine rather than a discipline the step must trust.
            contracts = RunContractSeason(world, contractLoad, seating, contractChoiceOverride);

            // ★ S101 — classes and requests. Pure by signature (world + seating, nothing
            //   else), computed here because the event exemption needs the seating and the
            //   seating exists only past this line. Nothing downstream reads it — it rides
            //   out on the outcome and reaches the page and Phase 92, which is what makes
            //   the S101 zero-path byte-identity claim provable by construction.
            nonConference = BuildNonConferenceRequests(world, seating, contracts.Charges);

            // ★ S102 — the matching. Pure by signature (the world and S101's report, nothing
            //   else): the requests are CONSUMED here, never recomputed, and the report object
            //   is read rather than written. Nothing downstream reads the result — it rides
            //   out on the outcome and reaches the page and Phase 93, which is what keeps the
            //   zero-path byte-identity claim provable by construction.
            matching = BuildNonConferenceMatching(world, nonConference, contracts.UsedPairs);

            schedule = BuildSeasonSchedule(
                world, seasonSeed, history, deferNumbering: true,
                out memoryOutcome, out rotationOutcome, debtWindowOverride);
            fingerprint = ScheduleFingerprint(schedule);
            // ★ S94 — every game gains its night. Purely additive: the structural fingerprint
            //   above is computed from the four named fields and cannot see the date.
            datedFingerprint = SeasonDateSchedule(world, schedule, seasonYearOfRun);

            // ★ S106 — every non-conference pairing gains its night. It runs HERE, after the
            //   league slate is dated, because the buy-game calendar is carved out of the
            //   league one: nothing knows which nights a school has free until the conference
            //   dater has spoken. Purely additive — it reads the dated schedule, the seating
            //   and the matching report and writes only its own page cargo, so no earlier
            //   fingerprint can move because of it.
            nonConferenceDates = DateNonConferenceGames(
                world, matching, contracts, seating, schedule, seasonYearOfRun);

            // ★ S111 — every dated pairing becomes a game here, BEFORE the commit, because its id
            //   must be reserved at the commit. Nothing here changes which games, who hosts or what
            //   night: the site comes from the pairing's own kind (or its contract leg), never from
            //   whether a host id happens to be present — a Neutral pairing's "host" field is only
            //   the lower school id.
            //   `buyGamesOffForTest` is Phase 101's zero-path switch and nothing else sets it.
            var buyPlans = buyGamesOffForTest
                ? (IReadOnlyList<BuyGamePlan>)Array.Empty<BuyGamePlan>()
                : BuyBuildPlans(world, nonConferenceDates, matching, contracts);

            MteRefuseOverlap(world, seating, schedule, seasonYearOfRun);

            eventRecordBody = null;
            // ★ S98 — legacy mode plays its tournaments too. The fixtures simply stay unnumbered,
            //   exactly as legacy conference fixtures always have: basketball does not require a
            //   career. So the reservation table is empty here and the bracket factory asks for an
            //   id only when there is a season to hang it on.
            IReadOnlyDictionary<BracketSlotKey, GameId> reservations =
                new Dictionary<BracketSlotKey, GameId>();
            // ★ S110 — the conference tournaments' own reservation table, on exactly the same terms:
            //   the pairings are unknown at the commit (round two is round one's result), so ids are
            //   reserved by COUNT and belong to a POSITION rather than to whoever wins it.
            IReadOnlyDictionary<ConfTourneySlotKey, GameId> confTourneyReservations =
                new Dictionary<ConfTourneySlotKey, GameId>();
            IReadOnlyList<GameId> buyReservations = Array.Empty<GameId>();
            SeasonId? seasonIdOfRun = null;
            if (history is not null)
            {
                var numbering = NumberSeasonSchedule(
                    schedule, history, seating, ConfTourneyExpectedSlots(world), buyPlans.Count);
                if (numbering.SeasonNumber != pendingSeasonId)
                    throw new InvalidOperationException(
                        $"SEASON INVARIANT VIOLATED: peeked season {pendingSeasonId} but reserved " +
                        $"{numbering.SeasonNumber}.");
                reservations = numbering.Reservations;
                confTourneyReservations = numbering.ConfTourneyReservations;
                buyReservations = numbering.BuyReservations;
                seasonIdOfRun = numbering.SeasonId;
                // ★ S124 (K3) — the exact bytes S97 published here, held for the close.
                eventRecordBody = MteRecordBytes(history, numbering.SeasonNumber, seasonSeed, world, seating,
                                                 contracts.Survivors, BuildPairingLog(contracts, matching));
            }

            // ★ S115 — three ways to get this season's people, in this order: rosters handed in
            //   (the stacked command), the career turnover (season two onward of a career), the
            //   bootstrap draft (legacy mode, a career's first season, and the test flag).
            people = null;
            if (rostersInHand is not null)
            {
                divvy = rostersInHand;
            }
            else if (careerPeople is not null)
            {
                (divvy, people) = CareerTurnover(world, careerPeople, seasonSeed, pendingSeasonId, history!);
            }
            else
            {
                divvy = RunDivvyDraft(world, seasonSeed, history);
                if (history is not null) people = PeopleSummary.Bootstrap;
            }
            // ★ S121 — THE CAMP, its own step after the people are numbered (G4): the returners camp,
            //   the freshmen are rolled; on a career's first season the whole bootstrap pool is rolled.
            //   Only a career keeps this state (in its scouting file); the stacked command develops in
            //   memory through StackedTurnoverAndCamp.
            development = null;
            devStates = null;
            if (devCfg is not null)
            {
                if (careerPeople is not null)
                {
                    development = CareerDevelop(divvy, people!.Returned, careerPeople, previousScouting!, seasonSeed, devCfg);
                    divvy = development.Divvy;
                    devStates = development.States;
                }
                else if (rostersInHand is null)
                {
                    devStates = DevBootstrap(divvy, seasonSeed, devCfg).States;
                }
                if (verbose && development is not null) PrintDevelopmentReport(development);
            }
            if (verbose && people is not null) Console.WriteLine(people.Line);

            // ★ S89 — history mode's contract, validated ONCE, here. Past this line every
            // history-backed path may assume identities are present; none of them re-check.
            if (history is not null)
            {
                if (divvy.PersonIds is null || divvy.PersonIds.Count != divvy.Pool.Count)
                    throw new HistoryException(HistoryError.MissingIdentity,
                        $"history mode: {divvy.PersonIds?.Count ?? 0} identities for " +
                        $"{divvy.Pool.Count} admitted people.");
                foreach (var sg in schedule)
                {
                    IdentityGuard.Require(sg.SeasonId, "a scheduled fixture");
                    IdentityGuard.Require(sg.GameId, "a scheduled fixture");
                }
                // Nothing after schedule construction issues an identity, so reservations close
                // here. ★ S124 (K2) — the career LOCK is kept: the close publishes under it, and a
                // second run must not start this season while it plays.
                history.CloseReservations();
            }

            rowsBySchool = BuildSeasonRows(divvy, world, verbose);
            AssertSeasonIdentitiesDistinct(rowsBySchool);
            cfgs = LoadGenEngineConfigs(engineConfigPath);
            // ★ S95 — the home-court dial, read ONCE here and carried into the loop as a
            //   plain value. The loop never touches disk; 5,205 games do not re-read a file
            //   to learn the same integer 5,205 times.
            roadShave = roadShaveOverride ?? HomeCourtConfig.Load(engineConfigPath).RoadShave;

            wins = world.Schools.ToDictionary(s => s.Id, _ => 0);
            losses = world.Schools.ToDictionary(s => s.Id, _ => 0);
            //  ★ S122 — EVERY PER-GAME OUTPUT IS ADDRESSED BY FIXTURE ORDINAL FROM THE MOMENT THE GAME
            //    IS PLAYED (K3). Games no longer play in ordinal order, so nothing here is appended:
            //    slot g holds fixture ordinal g's game, empty until it plays, and the finished lists are
            //    materialized after the loop. No reader — during the season or after — ever sees a list
            //    whose index means anything but the fixture ordinal. Sized once the season's sources
            //    are known (below); `resultSlots` is a list because the conference tournaments hold a
            //    reference to it before then, to read their league's results the morning they open.
            resultSlots = new List<SeasonGameResult?>();
            possessionSlots = Array.Empty<int?>();
            // ★ S112 — each side's own possessions, carried rather than halved (see SidePossessions).
            sideSlots = Array.Empty<SidePossessionCount?>();
            putbackSlots = Array.Empty<GamePutbackAudit?>();   // ★ S118, Phase 108 only
            laneSlots = Array.Empty<GameLaneAudit?>();           // ★ S120, Phase 111 only
            playedSlots = Array.Empty<PlayedSeasonGame?>();
            //  ★ S122 (K2) — the log stays in fixture order for now: each game's block is built when it
            //    is played and held here, then appended in ordinal order after the season. The log's
            //    bytes are unchanged; the save session redesigns it around play order (O-134).
            logSlots =
                Array.Empty<(GameBlockFactsV1, List<PerGameStatRowV1>)?>();
            // ★ S121 — each man's seconds and each school's games, for the next camp's minutes share.
            secondsByPool = new Dictionary<int, long>();
            teamGames = new Dictionary<int, int>();
            hostedRoadSidesShaved = 0;
            leagueRoadSidesShaved = 0;
            ties = 0;
            league = new SeasonLeagueStats();
            // ★ S89 — the map goes to the accumulator ONCE, before the loop, rather than being
            // threaded through both accumulator signatures every game. `RecordFor` is its only
            // consumer and it already holds the pool slot.
            league.PersonIds = divvy.PersonIds;
            baseSeed = unchecked((int)seasonSeed);

            //  ── S90: the retention log. ────────────────────────────────────────────
            //  The roster is built here, before the first tip, because the rows it lists are
            //  these rows — the start-of-season card (R8). ★ S124 — it is WRITTEN at the close,
            //  with the scouting file, and the writer validates the whole roster there before it
            //  touches the filesystem (K8: a bad name refuses at the close with nothing published).
            keepsLog = retainGameLog && history is not null;
            logRoster = keepsLog ? BuildRetentionRoster(rowsBySchool, divvy) : null;

            // ══════════════════════════════════════════════════════════════════════════
            //  ★ S122 — THE SEASON ON THE CALENDAR (Program.Season.Calendar.cs).
            //
            //  Every game keeps the fixture ordinal it has always had — the league slate at
            //  0 .. N-1, then the brackets, the showcases, the conference tournaments and the
            //  buy games, each kind numbered after the last — so `baseSeed + 2g` hands every
            //  game the exact seeds it had before this session existed. What changed is the
            //  order they are PLAYED in: one day at a time from November 1 to the championship
            //  Monday, each day's games in ordinal order. From S98 to S121 they were played in
            //  ordinal order, November tournaments after March; that is retired.
            //
            //  The sources are built here, before the first tip: every game's ordinal and date
            //  is known now, even a bracket's later rounds, whose sides depend on results but
            //  whose dates do not.
            // ══════════════════════════════════════════════════════════════════════════
            var prestigeById = world.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
            var leagueSource = new SeasonStaticSource(
                "the league slate",
                schedule.Select((sg, i) => new PlayedSeasonGame(sg, i)).ToList());
            bracketSources = MteBracketSources(
                seating, prestigeById, reservations, seasonIdOfRun, schedule.Count, seasonYearOfRun);
            var bracketGameCount = bracketSources.Sum(b => b.Count);
            //  ★ S104 — THE SHOWCASES, NUMBERED AFTER EVERY BRACKET (see MteExpectedBracketSlots — the
            //    reservation walk and this numbering are the same order, and they must stay that way).
            showcaseSource = new MteShowcaseSource(
                seating, reservations, seasonIdOfRun, schedule.Count + bracketGameCount, seasonYearOfRun);
            eventGameCount = bracketGameCount + showcaseSource.Count;
            //  ★ S110 — THE CONFERENCE TOURNAMENTS, NUMBERED AFTER EVERY EVENT GAME, ascending
            //    conference id then topology game index 0..6, as the reservation walk
            //    (ConfTourneyExpectedSlots) spends them. Each reads its league's results from
            //    `resultSlots` the morning it opens — league results only, by ordinal, so a
            //    tournament win can never become a league win.
            (confTourneySources, leaguesTooSmall) = ConfTourneySources(
                world, seasonYearOfRun, schedule, resultSlots,
                confTourneyReservations, seasonIdOfRun, schedule.Count + eventGameCount);
            var confTourneyGameCount = confTourneySources.Sum(c => c.Count);
            //  ★ S111 — THE BUY GAMES, NUMBERED AFTER EVERY CONFERENCE TOURNAMENT GAME, in the dater's
            //    pairing order, which is also the reservation order.
            buySource = new BuyGameSource(
                buyPlans, buyReservations, seasonIdOfRun,
                schedule.Count + eventGameCount + confTourneyGameCount);

            totalGames = schedule.Count + eventGameCount + confTourneyGameCount + buySource.Count;
            resultSlots.AddRange(Enumerable.Repeat<SeasonGameResult?>(null, totalGames));
            possessionSlots = new int?[totalGames];
            sideSlots = new SidePossessionCount?[totalGames];
            putbackSlots = new GamePutbackAudit?[totalGames];
            laneSlots = new GameLaneAudit?[totalGames];
            playedSlots = new PlayedSeasonGame?[totalGames];
            logSlots = new (GameBlockFactsV1, List<PerGameStatRowV1>)?[totalGames];

            var sources = new List<SeasonGameSource> { leagueSource };
            sources.AddRange(bracketSources);
            sources.Add(showcaseSource);
            sources.AddRange(confTourneySources);
            sources.Add(buySource);
            calendar = new SeasonCalendarRun(
                sources, seasonYearOfRun, options.PlayOrderForTest ?? SeasonPlayOrder.Calendar,
                PlayOneGame,
                SeasonMonthLine(verbose));
        }

        /// <summary>★ S127 — the page's month line, one definition for a new season and a resumed one.</summary>
        private static Action<string, int>? SeasonMonthLine(bool verbose)
            => verbose ? (month, n) => Console.WriteLine(FormattableString.Invariant($"  ... {month}: {n:N0} games")) : null;

        public DateOnly FirstLegalDay => calendar.FirstLegalDay;
        public DateOnly LastLegalDay => calendar.LastLegalDay;

        /// <summary>The first day not yet played.</summary>
        public DateOnly NextDay { get { RefuseIfDeadOrFaulted(nameof(NextDay)); return calendar.NextDay; } }

        /// <summary>True exactly when every day of the season has been played.</summary>
        public bool IsComplete { get { RefuseIfDeadOrFaulted(nameof(IsComplete)); return calendar.IsComplete; } }

        /// <summary>How many games have been played so far.</summary>
        public int GamesPlayed { get { RefuseIfDeadOrFaulted(nameof(GamesPlayed)); return calendar.GamesPlayed; } }

        private void RefuseIfDeadOrFaulted(string call)
        {
            if (disposed)
                throw new InvalidOperationException(
                    $"SEASON RUN: {call} was called on a season run that has been disposed.");
            if (faultedOn is not null)
                throw new InvalidOperationException(
                    $"SEASON RUN: {call} refused — this season run failed on {faultedOn} and cannot continue in memory.");
        }

        /// <summary>Play every day from <see cref="NextDay"/> through <paramref name="day"/>.</summary>
        public void AdvanceThrough(DateOnly day)
        {
            RefuseIfDeadOrFaulted(nameof(AdvanceThrough));
            advancing = true;
            try
            {
                calendar.AdvanceThrough(day);
            }
            catch
            {
                //  A precondition refusal leaves the calendar unfaulted, and so the run.
                if (calendar.FaultedOn is { } on) faultedOn = on;
                throw;
            }
            finally
            {
                advancing = false;
            }
        }

        //  ★ S98 — ONE GAME, RUN ONCE, WHATEVER KIND IT IS. This body is the pre-S98 loop
        //  body lifted whole into a local function so that a tournament fixture goes through
        //  the identical path: same side construction, same seed arithmetic, same
        //  accumulators, same retention block. The ONLY thing that differs between a
        //  conference game and a bracket game is what the fixture says about itself.
        //
        //  `pg.FixtureOrdinal` replaces the old loop variable `g` and is the same number for
        //  every conference game it ever was, which is what keeps the conference half of the
        //  season byte-identical.
        private (int HomeScore, int AwayScore) PlayOneGame(PlayedSeasonGame pg)
        {
            var sg = pg.Game;
            var g = pg.FixtureOrdinal;
            // HomeSchool -> the engine's Home side, AwaySchool -> Away: the §1b
            // invariant. Home rows stamp PlayerIds 1..RosterShape.Size, away rows the
            // next Size — at S75's 13-man roster that is 1-13 and 14-26 (the comment
            // here said 1-10 / 11-20 until S77; the numbers had been wrong since S75
            // and sat directly above the code that maps them). Ids need uniqueness only
            // within a game; sides are stamped per matchup, never cached across games
            // where a school flips sides.
            var seatedHome = BuildSeasonSide(rowsBySchool[sg.HomeId], 0);
            var seatedAway = BuildSeasonSide(rowsBySchool[sg.AwayId], RosterShape.AwayIdOffset);
            // ★ S98 — home court, now read from the SCHEDULE'S OWN SITE FACT. This was a
            //   literal `true` from S95 until this session, with a note saying the tournament
            //   layer would replace it and nothing else here would move. That is exactly what
            //   happened: a conference game still says it has a host, a bracket game says it
            //   has none, and the shave path returns both sides untouched for the latter.
            var (sideHome, sideAway, awayShaved) =
                PrepareSeasonGameSides(seatedHome, seatedAway, roadShave, hasHost: sg.HasHost);
            if (awayShaved)
            {
                hostedRoadSidesShaved++;
                if (pg.IsLeagueGame) leagueRoadSidesShaved++;
            }
            // Everything below — the engine, the identity bundle, the occupancy walk —
            // reads the PREPARED sides, so the men who are attributed are the men who
            // played. Name and PlayerId survive the shave untouched, which is what keeps
            // the S77 Gate 2 identity comparison two independent paths to one person.
            var identity = new SeasonGameIdentity(
                sg.HomeId, sg.AwayId,
                rowsBySchool[sg.HomeId], rowsBySchool[sg.AwayId], sideHome, sideAway);
            // Snapshot BEFORE the first accumulator. The three calls below write different
            // halves of a man's line, so the boundary has to enclose all of them.
            // ★ S121 — the credits each of this game's men held before it, so the game's own
            //   credits (and through the S117 rule, his seconds) can be read after.
            var creditsBefore = new Dictionary<int, long>(2 * RosterShape.Size);
            foreach (var rows in new[] { identity.HomeRows, identity.AwayRows })
                foreach (var row in rows)
                    creditsBefore[row.PoolId] = league.PlayerSeasons.TryGetValue(row.PoolId, out var rec0) ? rec0.Credits : 0;
            var retentionBefore = !keepsLog ? null
                                : RetentionSnapshotBefore(league, identity);

            var (game, result, attributed, policy) = RunSingleGenGame(
                cfgs, sideHome, sideAway, TeamSide.Home, TeamSide.Away,
                resolverSeed: unchecked(baseSeed + 2 * g),
                governorSeed: unchecked(baseSeed + 2 * g + 1));

            // Session 31: keep the attribution the loop used to discard and feed the
            // calibration accumulator. Nothing else about the loop changes.
            league.Accumulate(game, result, attributed, identity);
            // S87: the foul-out layer — read from the tracker that ran and the policy that
            // enforced it, never re-derived.
            league.AccumulateFouling(game, result, policy);

            // S75: cross-position occupancy. Seat position is the seat's STARTER's
            // position and is fixed for the game (SlotPos), so it is read straight off
            // the side data rather than from the policy.
            var storedPos = new Dictionary<int, string>();
            var seatPos = new Dictionary<(TeamSide, int), string>();
            var seatH = new Dictionary<(TeamSide, int), int>();
            foreach (var (sd, sdSide) in new[] { (sideHome, TeamSide.Home), (sideAway, TeamSide.Away) })
            {
                for (var k = 0; k < sd.Starters.Length; k++)
                {
                    storedPos[sd.Starters[k].PlayerId] = sd.StarterPositions[k];
                    seatPos[(sdSide, k + 1)] = sd.StarterPositions[k];
                    seatH[(sdSide, k + 1)] = sd.Starters[k].Height;
                }
                for (var k = 0; k < sd.Reserves.Length; k++)
                    storedPos[sd.Reserves[k].PlayerId] = sd.ReservePositions[k];
            }
            league.NoteOccupancy(result.Possessions, game, storedPos, seatPos, seatH, identity);

            // ★ S121 — his seconds this game, by the same integer rule a career reads off its log.
            foreach (var (poolId, before) in creditsBefore)
            {
                var after = league.PlayerSeasons.TryGetValue(poolId, out var rec1) ? rec1.Credits : 0;
                var secs = after == before ? 0 : DevGameSeconds(after - before, result.Possessions.Count, result.OvertimePeriods);
                secondsByPool[poolId] = (secondsByPool.TryGetValue(poolId, out var sofar) ? sofar : 0) + secs;
            }
            teamGames[sg.HomeId] = (teamGames.TryGetValue(sg.HomeId, out var hg) ? hg : 0) + 1;
            teamGames[sg.AwayId] = (teamGames.TryGetValue(sg.AwayId, out var ag) ? ag : 0) + 1;

            // ...and diff AFTER the last one. Emission is the games-played delta, never a
            // credits delta read after the fact. ★ S122 — built now, appended after the season (K2).
            if (keepsLog && retentionBefore is not null)
                logSlots[g] = (
                    new GameBlockFactsV1(
                        sg.GameId!.Value, g, sg.HomeId, sg.AwayId,
                        string.Equals(sg.Kind, "conf", StringComparison.Ordinal),
                        game.HomeScore, game.AwayScore, (short)result.OvertimePeriods,
                        result.Possessions.Count,
                        // ★ S116 — a conference tournament game counts as a conference game for the
                        //   PLAYER and never toward the standings (Emmett, 2026-10-06), so it is its
                        //   own kind on the log, never folded into the league flag above.
                        IsConferenceTournamentGame: string.Equals(sg.Kind, "ctourney", StringComparison.Ordinal),
                        // ★ S116 — the date and the site, as the fixture already carries them. The
                        //   writer refuses a game with no date rather than filing one without it.
                        Date: sg.Date, HasHost: sg.HasHost,
                        // ★ S117 — the score by half and overtime, summed off this game's possessions.
                        //   The writer refuses it unless it sums to the final above.
                        Periods: ScoreByPeriod(result.Possessions.Select(r => (r.Half, r.Offense == TeamSide.Home, r.Points)),
                                               result.OvertimePeriods)),
                    RetentionRowsAfter(league, retentionBefore, g));

            // GameState.HomeScore is credited to HomeSchool, AwayScore to AwaySchool,
            // full stop (a flipped attribution passes conservation and determinism —
            // Phase 55's replay check exists to catch exactly that).
            // S89: the result carries the FIXTURE's numbers — the same game, not a new one.
            if (resultSlots[g] is not null)
                throw new InvalidOperationException(
                    $"SEASON INVARIANT VIOLATED: fixture ordinal {g} was played twice.");
            resultSlots[g] = new SeasonGameResult(
                sg.HomeId, sg.AwayId, game.HomeScore, game.AwayScore, result.OvertimePeriods,
                sg.SeasonId, sg.GameId);
            possessionSlots[g] = result.Possessions.Count;
            // ★ S112 — every possession record already names who had the ball, so the split is a
            //   COUNT, never total/2. The record builder asserts the two sides sum to the total.
            sideSlots[g] = (new SidePossessionCount(
                result.Possessions.Count(r => r.Offense == TeamSide.Home),
                result.Possessions.Count(r => r.Offense == TeamSide.Away)));
            // ★ S118 -- the putback audit (Phase 108). Pure arithmetic on records already in hand.
            {
                int Sum(TeamSide side, Func<PossessionRecord, int> f) =>
                    result.Possessions.Where(r => r.Offense == side).Sum(f);
                putbackSlots[g] = (new GamePutbackAudit(
                    result.Possessions.Sum(r => r.PutbackFtShooterChanged),
                    Sum(TeamSide.Home, r => r.Fga), Sum(TeamSide.Away, r => r.Fga),
                    Sum(TeamSide.Home, r => r.Fta), Sum(TeamSide.Away, r => r.Fta),
                    Sum(TeamSide.Home, r => r.SlotUnattributedFga), Sum(TeamSide.Away, r => r.SlotUnattributedFga),
                    Sum(TeamSide.Home, r => r.FtaBySlot.Unattr), Sum(TeamSide.Away, r => r.FtaBySlot.Unattr),
                    result.Possessions.Sum(r => r.ScrambleFtShooterStamped)));
            }
            laneSlots[g] = BuildLaneAudit(result.Possessions);   // ★ S120 -- Phase 111
            playedSlots[g] = pg;
            if (game.HomeScore > game.AwayScore) { wins[sg.HomeId]++; losses[sg.AwayId]++; }
            else if (game.AwayScore > game.HomeScore) { wins[sg.AwayId]++; losses[sg.HomeId]++; }
            else ties++;   // assumption-1 says impossible; counted so it can never hide

            return (game.HomeScore, game.AwayScore);
        }

        /// <summary>The close: legal once, and only when the season is complete.</summary>
        public SeasonRunOutcome Finish()
        {
            RefuseIfDeadOrFaulted(nameof(Finish));
            if (finished)
                throw new InvalidOperationException("SEASON RUN: Finish was called twice; a season closes once.");
            if (!calendar.IsComplete)
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"SEASON RUN: Finish before the season is complete; it has played through {calendar.NextDay.AddDays(-1):yyyy-MM-dd} ") +
                    FormattableString.Invariant($"and its last day is {calendar.LastLegalDay:yyyy-MM-dd}."));
            advancing = true;
            try
            {
                var outcome = Close();
                finished = true;
                return outcome;
            }
            catch
            {
                faultedOn ??= calendar.FaultedOn
                    ?? "its close, after " + calendar.LastLegalDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                throw;
            }
            finally
            {
                advancing = false;
            }
        }

        private SeasonRunOutcome Close()
        {
            var calendarPlay = calendar.Finish();

            //  The finished lists, in fixture-ordinal order — every slot filled, or the season refuses.
            static List<T> Materialize<T>(IReadOnlyList<T?> slots, string what) where T : class
                => slots.Select((x, i) => x ?? throw new InvalidOperationException(
                       $"SEASON INVARIANT VIOLATED: fixture ordinal {i} has no {what}.")).ToList();
            static List<T> MaterializeValue<T>(IReadOnlyList<T?> slots, string what) where T : struct
                => slots.Select((x, i) => x ?? throw new InvalidOperationException(
                       $"SEASON INVARIANT VIOLATED: fixture ordinal {i} has no {what}.")).ToList();
            var results = Materialize(resultSlots, "result");
            var possessionCounts = MaterializeValue(possessionSlots, "possession count");
            var sidePossessions = Materialize(sideSlots, "possession split");
            var putbackAudits = Materialize(putbackSlots, "putback audit");
            var laneAudits = Materialize(laneSlots, "lane audit");
            var playedGames = Materialize(playedSlots, "played game");

            var brackets = MteBracketOutcome(bracketSources);
            var showcases = showcaseSource.Outcome();
            var confTourneys = ConfTourneyOutcome(confTourneySources, leaguesTooSmall);
            var buyGames = buySource.Outcome();

            // ★ ONE played-fact map across both kinds. The record's seat-identity round-trip is a
            //   single code path, so a showcase's seats are validated exactly as rigorously as a
            //   bracket's rather than through a second, softer route.
            var seatsPlayedAll = new Dictionary<int, IReadOnlyDictionary<int, int>>();
            foreach (var kv in brackets.SeatsPlayed) seatsPlayedAll[kv.Key] = kv.Value;
            foreach (var kv in showcases.SeatsPlayed) seatsPlayedAll[kv.Key] = kv.Value;

            //  ★ S124 (K3) — EVERY CHECK THAT CAN BE MADE IN MEMORY IS MADE BEFORE THE FIRST WRITE: every
            //    log block present, the record's finishes validated against the field that played, every
            //    game placed, the outcome built. Then the writes, with the career file last.
            if (keepsLog)
                for (var g = 0; g < totalGames; g++)
                    if (logSlots[g] is null)
                        throw new InvalidOperationException(
                            $"SEASON INVARIANT VIOLATED: fixture ordinal {g} played but built no log block.");

            //  ★ S104 — the gate is "did ANY event play", not "are there finishes". A season whose
            //  only events were showcases produces no placement at all and still must have its
            //  record completed — keying on the finishes would have left those events reading
            //  NotPlayed forever, with every check green. ★ S124 — the record is written ONCE: the
            //  final bytes when any event played, otherwise the NotPlayed body, which is exactly
            //  what the file used to end up as.
            var anyEventPlayed = seatsPlayedAll.Count > 0;
            byte[]? recordBytes = null;
            if (history is not null)
                recordBytes = anyEventPlayed
                    ? MteFinalRecordBytes(eventRecordBody!, history.HistoryId, pendingSeasonId,
                                          brackets.FinishBySeat, seatsPlayedAll, showcases.Results)
                    : eventRecordBody!;

            var eventOutcome = new EventSeasonOutcome(
                seating,
                history is not null ? EventRecordStatus.Written : EventRecordStatus.NotApplicable,
                eventHistory.Diagnostics,
                history is not null && anyEventPlayed ? EventRecordStatus.Written : EventRecordStatus.NotApplicable);

            // ★ S109 — BOUNDARY TWO. Bracket games are built round by round DURING play, so no
            //   complete played-game list exists before the first tip; the event half can only be
            //   proven here, once. Every game that played carries the place it was ruled to have
            //   (S111: a neutral buy game carries none).
            AssertEveryGamePlaced(playedGames.Select(p => p.Game), "SeasonRunOutcome");

            var outcome = new SeasonRunOutcome
            {
                Schedule = schedule, Fingerprint = fingerprint, Results = results,
                Wins = wins, Losses = losses, Divvy = divvy, League = league, Ties = ties,
                People = people,
                PossessionCounts = possessionCounts,
                SidePossessions = sidePossessions,
                PutbackAudits = putbackAudits,
                LaneAudits = laneAudits,
                SecondsByPool = secondsByPool,
                Development = development,
                DevStates = devStates,
                TeamGames = teamGames,
                HostedRoadSidesShaved = hostedRoadSidesShaved,
                RoadShave = roadShave,
                DatedFingerprint = datedFingerprint,
                SeasonYear = seasonYearOfRun,
                Memory = memoryOutcome,
                Rotation = rotationOutcome,
                Events = eventOutcome,
                NonConference = nonConference,
                Matching = matching,
                NonConferenceDates = nonConferenceDates,
                Contracts = contracts,
                PlayedGames = playedGames,
                PlayOrder = calendarPlay.PlayOrder,
                FirstLegalDay = calendarPlay.FirstLegalDay,
                LastLegalDay = calendarPlay.LastLegalDay,
                ConferenceGameCount = schedule.Count,
                TournamentGameCount = eventGameCount,
                ShowcaseGameCount = showcases.GameCount,
                ShowcaseResults = showcases.Results,
                EventGamesFingerprint =
                    MteEventGamesFingerprint(playedGames, results, possessionCounts),
                EventFinishes = brackets.FinishBySeat,
                ConferenceTournaments = confTourneys,
                ConferenceTournamentGameCount = confTourneys.GameCount,
                ConferenceTournamentFingerprint = ConfTourneyFingerprint(confTourneys.Rows),
                BuyGameCount = buyGames.GameCount,
                BuyGames = buyGames.Rows,
                BuyGamesFingerprint = BuyGamesFingerprint(buyGames.Rows),
                LeagueRoadSidesShaved = leagueRoadSidesShaved,
            };

            if (history is not null)
                WriteCareerSeason(history, recordBytes!, calendarPlay.FirstLegalDay);
            return outcome;
        }

        /// <summary>★ S124 (K3), ★ S125 (K6), ★ S126 (K3) — every write a career season makes, in this
        /// order: the names check, the log, the scouting file, the event record, the league state, the
        /// SAVE. Before any of it, the league state on disk is checked against the world (K5). THE COMMIT
        /// BOUNDARY: the save is the last step that can fail; an exception from it means the career file
        /// was not replaced, and anything before it leaves the career file exactly as it was — the season
        /// is played again next run, its stray files (listed by no save) set aside by that run's open.</summary>
        private void WriteCareerSeason(HistoryStore store, byte[] recordBytes, DateOnly firstLegalDay)
        {
            void Step(string name) => closeStepForTest?.Invoke(name);

            //  ★ S125 (K5) — the B1 equality assertion at the close, before a single write.
            AssertLeagueMatchesWorld(store, world, "closed");

            //  ★ S126 (K3) — every name this close will write is free, checked before any writer runs.
            Step("names");
            CareerCheckNamesFree(store.Path, pendingSeasonId, keepsLog, devStates is not null);

            var written = new List<string>();
            if (keepsLog)
            {
                Step("log");
                var gameLog = GameLogWriter.Create(
                    store.Path, store.HistoryId, store.WorldFingerprint, fingerprint,
                    schedule[0].SeasonId!.Value, logRoster!);
                try
                {
                    Step("log-open");
                    //  One block per fixture PLAYED and no other, appended in fixture order (K2). The
                    //  writer refuses to publish a partial season rather than leaving a plausible-looking
                    //  short file behind.
                    for (var g = 0; g < totalGames; g++)
                    {
                        var block = logSlots[g]!.Value;
                        gameLog.AppendGame(block.Facts, block.Rows);
                        logSlots[g] = null;
                    }
                    gameLog.Finalize(totalGames);
                }
                finally
                {
                    gameLog.Dispose();
                }
                written.Add(GameLogWriter.FinalPathFor(store.Path, pendingSeasonId));
            }

            // ★ S121 — this season's people's hidden state, written whole once, beside the log.
            if (devStates is not null)
            {
                Step("scouting");
                written.Add(ScoutingFile.Write(store.Path, store.HistoryId, store.WorldFingerprint,
                                               schedule[0].SeasonId!.Value, DevScoutingRecords(divvy, devStates)));
            }

            Step("record");
            MteWriteRecord(store.Path, pendingSeasonId, recordBytes);
            written.Add(MteRecordPathFor(store.Path, pendingSeasonId));

            //  ★ S125 (K5) — the season's league state: prestige by school, copied from the world.
            Step("league");
            written.Add(store.WriteLeagueState(pendingSeasonId, LeaguePrestigeOf(world)));

            //  ★ S125 (K2) — THE SAVE: every file above listed by hash, the season's rules recorded,
            //  `.new` written, then one swap with the previous save kept as the backup.
            Step("counters");
            store.PublishSeason(written, CareerRulesFor(firstLegalDay, engineConfigPath));
            Step("published");
        }

        // ══════════════════════════════════════════════════════════════════════════════
        //  ★ S127 (B2a) — A SEASON SAVED MID-WAY.
        //
        //  SaveSnapshot — the season's whole state as bytes (Program.Season.Snapshot.cs). Legal only
        //    at a completed day boundary: between two advances, never while a day or the close runs
        //    (`advancing`), never on a run that is finished, faulted, disposed, in fixture order, or
        //    bound to no career. A refused save builds nothing.
        //  SaveProgress — SaveSnapshot, written under the season's next save name and published as the
        //    career's `progress` (the career's counters untouched; the held ones in the block).
        //  Resume — the run built back from a career's saved season, checked (K8) before it is handed
        //    to anyone: the rules, the header against `progress`, the decoding, and the restored season
        //    against `progress`. Only then does the store take back the held numbers.
        // ══════════════════════════════════════════════════════════════════════════════

        private void RefuseSave(string why)
            => throw new InvalidOperationException("SEASON RUN: SaveSnapshot refused — " + why);

        /// <summary>The header a save made now carries.</summary>
        private SeasonSnapshotHeader SnapshotHeaderNow()
        {
            var rules = HistoryStore.EffectiveRules(CareerRulesFor(calendar.NextDay, engineConfigPath));
            return new SeasonSnapshotHeader(
                rules.RulesStamp, rules.ConfigSha256, history!.HistoryId, pendingSeasonId,
                calendar.NextDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), seasonSeed, history.HeldCounters);
        }

        public byte[] SaveSnapshot() => SaveSnapshot(out _);

        private byte[] SaveSnapshot(out SeasonSnapshotHeader header)
        {
            if (disposed) RefuseSave("the season run has been disposed.");
            if (faultedOn is not null) RefuseSave($"this season run failed on {faultedOn}.");
            if (advancing) RefuseSave("a day or the season's close is being played; a save is made only between days.");
            if (finished) RefuseSave("the season has closed; it is saved by its close.");
            if (calendar.PlaysInFixtureOrder) RefuseSave("the season is played in fixture order (a check's replay), which has no days to stop between.");
            if (history is null) RefuseSave("the season is bound to no career; there is nowhere to resume it from.");
            header = SnapshotHeaderNow();
            return SeasonSnapshot.Write(header, this, SeasonSnapshot.IsRebound, SeasonSnapshot.Reachable(world));
        }

        /// <summary>The season saved to its career: the snapshot written under its new name, then the career file
        /// swapped in with `progress` naming it. Returns the block published.</summary>
        public ProgressBlock SaveProgress()
        {
            var bytes = SaveSnapshot(out var h);
            var save = (history!.Progress?.Save ?? 0) + 1;
            var path = history.WriteProgressSnapshot(pendingSeasonId, save, bytes);
            var block = new ProgressBlock(h.Season, h.NextDay, h.SeasonSeed, save,
                                          ProgressBlock.SnapshotPathFor(h.Season, save), h.Held, h.ConfigSha256, h.RulesStamp);
            history.PublishProgress(path, block);
            return history.Progress!;
        }

        /// <summary>★ S127 (K8) — the season a career saved mid-way, built back and checked; the store then holds
        /// exactly the numbers the season held. Every refusal is by name, before anything plays, and leaves the
        /// career file as it was.</summary>
        public static SeasonRun Resume(byte[] bytes, WorldFile world, HistoryStore history, string engineConfigPath,
                                       bool verbose, Action<string>? closeStepForTest = null)
        {
            var block = history.Progress
                ?? throw new HistoryException(HistoryError.ProgressInvalid, "this career holds no season in progress to resume.");
            static HistoryException Mismatch(string field, string saved, string career)
                => new(HistoryError.SnapshotMismatch,
                       $"the season's save disagrees with the career file on its {field} (the save says {saved}, the career " +
                       $"{career}). Nothing was played; the career is unchanged.");
            static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

            //  K8 step 1 — the rules this run plays under against the save's, then the header against the block.
            var now = HistoryStore.EffectiveRules(CareerRulesFor(DateOnly.Parse(block.NextDay, CultureInfo.InvariantCulture), engineConfigPath));
            if (!string.Equals(now.RulesStamp, block.RulesStamp, StringComparison.Ordinal)
                || !string.Equals(now.ConfigSha256, block.ConfigSha256, StringComparison.Ordinal))
                throw new HistoryException(HistoryError.ProgressRulesChanged,
                    $"season {I(block.Season)} was saved on {block.NextDay} under " +
                    (now.RulesStamp != block.RulesStamp ? "another build of the game" : "other settings") +
                    " than this run's; B2a resumes a season only under the build and settings that saved it. " +
                    "Nothing was played; the career is unchanged.");
            var (h, bodyAt) = SeasonSnapshot.ReadHeader(bytes);
            if (h.RulesStamp != block.RulesStamp) throw Mismatch("build stamp", h.RulesStamp[..12], block.RulesStamp[..12]);
            if (h.ConfigSha256 != block.ConfigSha256) throw Mismatch("settings hash", h.ConfigSha256[..12], block.ConfigSha256[..12]);
            if (h.HistoryId != history.HistoryId) throw Mismatch("career id", h.HistoryId, history.HistoryId);
            if (h.Season != block.Season) throw Mismatch("season", I(h.Season), I(block.Season));
            if (h.NextDay != block.NextDay) throw Mismatch("next day", h.NextDay, block.NextDay);
            if (h.SeasonSeed != block.SeasonSeed) throw Mismatch("season seed", I(h.SeasonSeed), I(block.SeasonSeed));
            if (h.Held != block.Held) throw Mismatch("held numbers", h.Held.ToString(), block.Held.ToString());

            //  K8 step 2 — bounded decoding.
            if (SeasonSnapshot.ReadBody(bytes, bodyAt) is not SeasonRun run)
                throw new HistoryException(HistoryError.SnapshotMalformed,
                    "the season's save does not hold a season. Nothing was played; the career is unchanged.");

            //  The caller's resources, rebound (never stored).
            void Set(object target, Type t, string name, object? value)
                => t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
            Set(run, typeof(SeasonRun), nameof(history), history);
            Set(run, typeof(SeasonRun), nameof(world), world);
            Set(run, typeof(SeasonRun), nameof(engineConfigPath), engineConfigPath);
            Set(run, typeof(SeasonRun), nameof(verbose), verbose);
            Set(run, typeof(SeasonRun), nameof(closeStepForTest), closeStepForTest);
            run.calendar.Rebind(run.PlayOneGame, SeasonMonthLine(verbose));

            //  K8 step 3 — the restored season against the block.
            if (run.pendingSeasonId != block.Season) throw Mismatch("restored season", I(run.pendingSeasonId), I(block.Season));
            if (run.seasonSeed != block.SeasonSeed) throw Mismatch("restored seed", I(run.seasonSeed), I(block.SeasonSeed));
            if (run.finished || run.disposed || run.faultedOn is not null || run.advancing)
                throw Mismatch("restored lifecycle", "a season that is closed, disposed, failed or mid-day", "a season between days");
            var nextDay = DateOnly.Parse(block.NextDay, CultureInfo.InvariantCulture);
            if (run.calendar.ResumeFault(nextDay, run.totalGames, g => run.resultSlots[g] is not null) is { } fault)
                throw Mismatch("restored season", fault, $"season {I(block.Season)} next playing {block.NextDay}");

            history.ResumeHeld();
            return run;
        }

        /// <summary>Ends the run. ★ S124 — the run holds no file (the close's log writer is disposed
        /// inside the close), and it never touches the career store, which belongs to the caller.
        /// Idempotent; after it every call is refused by name.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            calendar.Dispose();
        }
    }
}
