using System.Globalization;
using System.Text.Json;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 89 (Session 98) — THE BRACKETS PLAY.
//
//  What this proves, and what it deliberately does NOT:
//
//  ★ PAGE-ONLY CALIBRATION HOLDS, unchanged from S97. Not one assertion here
//    says a field should look a certain way, that a given school should win a
//    given tournament, or what a home-court percentage should be. What is
//    asserted is MECHANISM: the route tables, the seeding rule, the dates, the
//    neutral floor, the reservation ledger, the log's kind byte, the record's
//    transaction, and the standings ORDER — never a basketball value.
//
//  ★ C1 IS THE STRONGEST CONTROL HERE AND ITS GOLDEN IS PRE-S98. It was captured
//    from the pristine S97 tree before a line of this session existed. A golden
//    taken afterwards would prove determinism and not preservation, which is the
//    lesson S95, S96 and S97 each learned in turn.
//
//  ★ AND C1 IS ASSERTED WITH THE BRACKETS ON. Running with every event forced
//    dormant appends nothing, so "the conference half is unchanged" would be a
//    comparison of two conference-only seasons — true, and about nothing. The
//    discriminating arm is the one where twenty-four extra games really were
//    played AFTER the conference slate and the conference half still reproduces
//    the pre-S98 hash exactly.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ Captured from the PRISTINE S97 TREE, before any S98 code existed. fixture-mte
    /// at seed 20260720, no career: sixty conference games, and these are the genuine pre-S98
    /// artifacts that C1 is the authority for.</summary>
    private const string BracketsPreS98ConferenceScheduleSha =
        "eee5e256b0c6fc871d565b8c27c2925824e3b3ba8e76a717a3fdae4c6c0b36dc";
    private const string BracketsPreS98ConferenceResultsSha =
        "95038e912bc82c77f378cda9fbfa8e70ad08a952a40375b1471ac7a2539111f4";
    private const int BracketsPreS98ConferenceGameCount = 60;

    private static bool Phase89BracketsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 89 — The brackets play (S98: seeded by prestige, played to a full " +
                          "placement on the window's own nights, neutral floor with nobody hosting, " +
                          "executed last and dated first — the pre-S98 conference golden reproduced " +
                          "WITH the brackets on, the route tables walked literally, the reservation " +
                          "ledger, the log's kind byte, host memory's blindness constructed so it " +
                          "would matter, the record's transaction and atomicity, and win percentage) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        static string? Refusal(Action a)
        {
            try { a(); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s98-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(scratch);
            var baseDir = AppContext.BaseDirectory;
            var mte = LoadWorld(Path.Combine(baseDir, "worlds", "fixture-mte.world.json"));

            WorldFile Swap(List<WorldEvent> evs) => new()
            {
                SchemaVersion = mte.SchemaVersion, Kind = mte.Kind, EraLabel = mte.EraLabel,
                Division = mte.Division, WorldSeed = mte.WorldSeed, Tiers = mte.Tiers,
                Conferences = mte.Conferences, Places = mte.Places, Schools = mte.Schools, Events = evs,
            };
            var allOn = Swap(mte.Events.Select(e => e with { ForcedActive = true }).ToList());
            var allOff = Swap(mte.Events.Select(e => e with { ForcedActive = false }).ToList());

            // ════════════════════════════════════════════════════════════════════
            //  C1 — PRESERVATION. The conference half is the pre-S98 season, exactly.
            // ════════════════════════════════════════════════════════════════════
            var on = RunSeasonCore(allOn, MteCheckSeed, configPath, verbose: false);
            var off = RunSeasonCore(allOff, MteCheckSeed, configPath, verbose: false);

            Check("C1a: the conference slate is the recorded shape and the schedule fingerprint is the " +
                  "pre-S98 golden — asserted BEFORE the results hash, so a mismatch names itself",
                  on.ConferenceGameCount == BracketsPreS98ConferenceGameCount
                  && on.Fingerprint == BracketsPreS98ConferenceScheduleSha,
                  $"{on.ConferenceGameCount} games / {on.Fingerprint[..16]}…");

            //  ★ The slice is named ONCE and it is valid because tournament games APPEND:
            //    conference games occupy ordinals 0..N-1 and nothing else does.
            Check("C1b: both collections hold at least the conference prefix, and the FIRST tournament " +
                  "game sits at exactly the conference game count",
                  on.Results.Count >= on.ConferenceGameCount
                  && on.PossessionCounts.Count >= on.ConferenceGameCount
                  && on.PlayedGames.Where(p => p.IsEventGame).Select(p => p.FixtureOrdinal).DefaultIfEmpty(-1)
                       .Min() == on.ConferenceGameCount,
                  $"first tournament ordinal " +
                  $"{on.PlayedGames.First(p => p.IsEventGame).FixtureOrdinal}");

            //  ★ ONE helper, not a second serialization. SeasonFingerprint's own note says it
            //    is the single definition used by both the golden capture and Phase 86,
            //    precisely so two formats cannot drift apart; inventing an S98 variant would
            //    be the exact failure it was written to prevent.
            var confOn = SeasonFingerprint(
                on.Results.Take(on.ConferenceGameCount).ToList(),
                on.PossessionCounts.Take(on.ConferenceGameCount).ToList());
            Check("C1c: ★ THE ZERO PATH, WITH THE BRACKETS ON. Twenty-four extra games were played " +
                  "after the conference slate and every conference score and possession count still " +
                  "reproduces a fingerprint captured from the PRISTINE S97 TREE",
                  confOn == BracketsPreS98ConferenceResultsSha,
                  confOn == BracketsPreS98ConferenceResultsSha
                      ? confOn[..16] + "…"
                      : $"got {confOn}, want {BracketsPreS98ConferenceResultsSha}");

            Check("C1d: ★ the discriminator — the brackets really did play, so C1c is a prefix of " +
                  "something longer rather than a whole season compared to itself",
                  on.TournamentGameCount == 24
                  && on.Results.Count == on.ConferenceGameCount + on.TournamentGameCount
                  && off.TournamentGameCount == 0,
                  $"{on.TournamentGameCount} tournament games on, {off.TournamentGameCount} off");

            var confOff = SeasonFingerprint(off.Results, off.PossessionCounts);
            Check("C1e: and with every event dormant the whole season is that same golden — a dormant " +
                  "pool spends nothing, plays nothing and appends nothing",
                  confOff == BracketsPreS98ConferenceResultsSha
                  && off.EventGamesFingerprint == MteEventGamesFingerprint(
                         new List<PlayedSeasonGame>(), off.Results, off.PossessionCounts));

            Check("C1f: the conference schedule fingerprint is UNMOVED by the site fact — it hashes " +
                  "index, kind, home and away BY NAME and cannot see HasHost",
                  on.Fingerprint == off.Fingerprint && on.DatedFingerprint == off.DatedFingerprint);

            // ════════════════════════════════════════════════════════════════════
            //  C2 — SEEDING. Prestige, then the lower school id. Never a draw.
            // ════════════════════════════════════════════════════════════════════
            {
                //  ★ SEAT ORDER AND SEED ORDER ARE MADE TO DISAGREE, so a check that confused
                //    them fails here rather than passing by luck. Seat 1 is the WORST team and
                //    seats 3/4 carry a deliberate prestige TIE that only the id can break.
                var seats = new List<EventSeat>
                {
                    new(1, 40, "Worst",   0, EventSeatFallback.None),
                    new(2, 30, "Best",    1, EventSeatFallback.None),
                    new(3, 22, "TieHigh", 2, EventSeatFallback.None),
                    new(4, 11, "TieLow",  3, EventSeatFallback.None),
                };
                var probe = new SeatedEvent(900, "Probe", 1, 1, "Nowhere", "11-20", "11-21", 4,
                                            EventSeatingStatus.Complete, seats);
                var prestige = new Dictionary<int, int> { [40] = 10, [30] = 99, [22] = 55, [11] = 55 };
                var plan = MteSeedField(probe, prestige);

                Check("C2a: seeds run 1..N by prestige descending",
                      plan.SchoolBySeed[0] == 30 && plan.SchoolBySeed[3] == 40,
                      string.Join(",", plan.SchoolBySeed));
                Check("C2b: ★ a prestige TIE breaks on the LOWER SCHOOL ID and never on a draw",
                      plan.SchoolBySeed[1] == 11 && plan.SchoolBySeed[2] == 22);
                Check("C2c: ★ SEAT ORDER IS NOT SEED ORDER — seat 1 is the four-seed here, and the " +
                      "seat map is carried rather than re-derived from the seed map",
                      plan.SeatBySeed[0] == 2 && plan.SeatBySeed[3] == 1,
                      "seat-by-seed " + string.Join(",", plan.SeatBySeed));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — THE ROUTE TABLES, WALKED LITERALLY.
            // ════════════════════════════════════════════════════════════════════
            foreach (var size in new[] { 8, 4 })
            {
                var routes = BracketRoutesFor(size);
                var expectedGames = size == 8 ? 12 : 4;
                var perTeam = size == 8 ? 3 : 2;
                Check($"C3a/{size}: exactly {expectedGames} games and {routes.Max(r => r.Round) + 1} rounds",
                      routes.Length == expectedGames
                      && routes.Max(r => r.Round) + 1 == perTeam,
                      $"{routes.Length} games");

                //  Walk every possible result: 2^games outcomes is 4096 at eight teams, so the
                //  whole space is enumerable and there is no reason to sample it.
                var placeFilled = new int[size + 1];
                var gamesPlayed = new int[size + 1];
                var r1WinnerPlaces = new HashSet<int>();
                var r1LoserPlaces = new HashSet<int>();
                var everyDestinationHit = true;
                for (var mask = 0; mask < (1 << expectedGames); mask++)
                {
                    var occ = new int[expectedGames][];
                    for (var i = 0; i < expectedGames; i++) occ[i] = new[] { 0, 0 };
                    var places = new int[size + 1];
                    var counts = new int[size + 1];
                    var r1Winners = new HashSet<int>();
                    foreach (var route in routes)
                    {
                        if (route.SeedA > 0) occ[route.GameIndex][0] = route.SeedA;
                        if (route.SeedB > 0) occ[route.GameIndex][1] = route.SeedB;
                        var a = occ[route.GameIndex][0];
                        var b = occ[route.GameIndex][1];
                        if (a == 0 || b == 0) { everyDestinationHit = false; break; }
                        counts[a]++; counts[b]++;
                        var aWins = (mask & (1 << route.GameIndex)) != 0;
                        var w = aWins ? a : b;
                        var l = aWins ? b : a;
                        if (route.Round == 0) r1Winners.Add(w);
                        if (route.WinnerPlace > 0) { places[w] = route.WinnerPlace; places[l] = route.LoserPlace; }
                        else
                        {
                            occ[route.WinnerToGame][route.WinnerToSlot] = w;
                            occ[route.LoserToGame][route.LoserToSlot] = l;
                        }
                    }
                    if (!everyDestinationHit) break;
                    for (var s = 1; s <= size; s++)
                    {
                        if (places[s] >= 1 && places[s] <= size) placeFilled[places[s]]++;
                        gamesPlayed[counts[s]]++;
                        if (r1Winners.Contains(s)) r1WinnerPlaces.Add(places[s]);
                        else r1LoserPlaces.Add(places[s]);
                    }
                }

                Check($"C3b/{size}: ★ every game is filled before it is played, down every one of the " +
                      $"{1 << expectedGames} possible result paths",
                      everyDestinationHit);
                Check($"C3c/{size}: EVERY TEAM PLAYS EXACTLY {perTeam} — the consolation side is what " +
                      "makes the field legal, not an optional extra",
                      gamesPlayed.Where((_, k) => k != perTeam).Sum() == 0
                      && gamesPlayed[perTeam] == size * (1 << expectedGames),
                      $"{gamesPlayed[perTeam]} team-tournaments at {perTeam} games");
                Check($"C3d/{size}: every place 1..{size} is filled exactly once in every outcome",
                      Enumerable.Range(1, size).All(p => placeFilled[p] == 1 << expectedGames));
                //  ★ r2's "a winner never appears in a consolation path" was FALSE — the winners
                //    of the two consolation semis go on to play for fifth. The true invariant is
                //    the placement floor and ceiling, and it is stated PER FIELD SIZE because the
                //    eight-team pair does not apply to a four.
                var wantWin = size == 8 ? new[] { 1, 2, 3, 4 } : new[] { 1, 2 };
                var wantLose = size == 8 ? new[] { 5, 6, 7, 8 } : new[] { 3, 4 };
                Check($"C3e/{size}: ★ an R1 winner finishes {wantWin[0]}-{wantWin[^1]} and an R1 loser " +
                      $"{wantLose[0]}-{wantLose[^1]} — a team beaten on the championship side can never " +
                      "play for first",
                      r1WinnerPlaces.OrderBy(x => x).SequenceEqual(wantWin)
                      && r1LoserPlaces.OrderBy(x => x).SequenceEqual(wantLose),
                      $"winners {string.Join(",", r1WinnerPlaces.OrderBy(x => x))} / " +
                      $"losers {string.Join(",", r1LoserPlaces.OrderBy(x => x))}");
            }

            Check("C3f: ★ ORIGINAL SEEDS TRAVEL — in every game the LOWER original seed number is the " +
                  "nominal home side, including late-round games whose teams arrived down different paths",
                  on.PlayedGames.Where(p => p.IsEventGame)
                    .All(p => p.HomeOriginalSeed < p.AwayOriginalSeed));

            // ════════════════════════════════════════════════════════════════════
            //  C4 — DATES. Exact equalities, because the window length is validated.
            // ════════════════════════════════════════════════════════════════════
            {
                var byEvent = on.PlayedGames.Where(p => p.IsEventGame).GroupBy(p => p.EventId!.Value);
                var seatedById = on.Events.Seating.Active.ToDictionary(e => e.EventId);
                var datesOk = true;
                var checkedEvents = 0;
                foreach (var grp in byEvent)
                {
                    var e = seatedById[grp.Key];
                    var first = MteWindowDate(e.FirstDay);
                    var last = MteWindowDate(e.LastDay);
                    var routes = BracketRoutesFor(e.FieldSize);
                    foreach (var p in grp)
                    {
                        var round = routes[p.BracketGameIndex!.Value].Round;
                        var want = round == routes.Max(r => r.Round) ? last : first.AddDays(round);
                        if (p.Game.Date != want) datesOk = false;
                    }
                    // The arithmetic stated as the concrete equality it is, not as a bound.
                    if (e.FieldSize == 8 && last != first.AddDays(2)) datesOk = false;
                    if (e.FieldSize == 4 && last != first.AddDays(1)) datesOk = false;
                    checkedEvents++;
                }
                Check("C4a: ★ an 8-field's rounds land on firstDay, firstDay+1 and lastDay, and a " +
                      "4-field's on firstDay and lastDay — exact equalities, because the world " +
                      "validator refuses any other window length",
                      datesOk && checkedEvents == 4, $"{checkedEvents} events dated");
                Check("C4b: ★ THE DATES SAY NOVEMBER while the play order says last — the two really " +
                      "do differ, which is the whole point of executing last and dating first",
                      on.PlayedGames.Where(p => p.IsEventGame).All(p => p.Game.Date is { Month: 11 })
                      && on.PlayedGames.Where(p => p.IsEventGame).All(p => p.FixtureOrdinal >= on.ConferenceGameCount));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — THE FACTORY'S WHOLE CONTRACT, so a failure names itself.
            // ════════════════════════════════════════════════════════════════════
            {
                var tourney = on.PlayedGames.Where(p => p.IsEventGame).ToList();
                Check("C5a: ★ every tournament fixture says Kind == \"mte\" EXACTLY — \"anything except " +
                      "conf\" must not become the contract",
                      tourney.All(p => string.Equals(p.Game.Kind, "mte", StringComparison.Ordinal))
                      && tourney.Count == 24);
                Check("C5b: ★ and every one of them HAS NO HOST, while every conference game does",
                      tourney.All(p => !p.Game.HasHost)
                      && on.PlayedGames.Where(p => !p.IsEventGame).All(p => p.Game.HasHost));
                Check("C5c: the nominal home side is the better ORIGINAL seed's school, and the away " +
                      "side the other — a box-score ordering, never a venue",
                      tourney.All(p => p.Game.HomeId != p.Game.AwayId));

                //  ★ The neutral floor asserted on the PREPARED SIDES rather than on a flag:
                //    both come back as the same references that went in, so nobody was shaved.
                var probeHome = HomeCourtProbeSide();
                var probeAway = HomeCourtProbeSide();
                var (nHome, nAway, nShaved) = PrepareSeasonGameSides(probeHome, probeAway, 3, hasHost: false);
                var (hHome, hAway, hShaved) = PrepareSeasonGameSides(probeHome, probeAway, 3, hasHost: true);
                Check("C5d: ★ A NEUTRAL FLOOR MEANS NOBODY GETS ANYTHING — both prepared sides are the " +
                      "very objects that went in and nothing was shaved",
                      ReferenceEquals(nHome, probeHome) && ReferenceEquals(nAway, probeAway) && !nShaved);
                Check("C5e: the discriminator — the SAME sides at the SAME shave with a host really do " +
                      "get transformed, so C5d is not passing on a build where the shave was deleted",
                      ReferenceEquals(hHome, probeHome) && !ReferenceEquals(hAway, probeAway) && hShaved);

                //  Together these two prove neutral games were played and did not touch the
                //  hosted wire. Phase 86 B8 compares against Schedule.Count, which stays
                //  conference-only, so it survives this session untouched.
                Check("C5f: ★ the hosted counter equals the CONFERENCE game count and the results list " +
                      "equals conference plus tournament — neutral games played, hosted wire untouched",
                      on.HostedRoadSidesShaved == on.ConferenceGameCount
                      && on.Results.Count == on.ConferenceGameCount + on.TournamentGameCount,
                      $"{on.HostedRoadSidesShaved} shaved / {on.ConferenceGameCount} conference, " +
                      $"{on.Results.Count} results");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10a — ALIGNMENT, asserted directly so a mismatch names itself.
            // ════════════════════════════════════════════════════════════════════
            Check("C10a: PlayedGames, Results and PossessionCounts are one aligned list and the index " +
                  "IS the fixture ordinal",
                  on.PlayedGames.Count == on.Results.Count
                  && on.Results.Count == on.PossessionCounts.Count
                  && on.PlayedGames.Select((p, i) => p.FixtureOrdinal == i).All(x => x),
                  $"{on.PlayedGames.Count} played / {on.Results.Count} results / " +
                  $"{on.PossessionCounts.Count} counts");

            // ════════════════════════════════════════════════════════════════════
            //  C6 / C8 / C10 — a real career on real disk.
            // ════════════════════════════════════════════════════════════════════
            //  ★ THE LEDGER, ON ITS OWN CAREER. The game counter has no public peek — S89 made
            //    the raw number internal on purpose and that seam holds against the suite too —
            //    so the ledger is measured the only way a caller can: take one id before, one
            //    after, and subtract. That probe SPENDS an id, which is exactly why it runs on a
            //    career of its own: doing it on the career C10e compares would shift every game
            //    number by one and produce a failure that is the check's own footprint.
            var ledgerPath = Path.Combine(scratch, "ledger.json");
            SeasonRunOutcome ledger;
            long spentBefore, spentAfter;
            using (var store = HistoryStore.Open(ledgerPath, WorldFingerprint(allOn)))
                spentBefore = RawGameNumber(store.ReserveGames(1)[0]);
            using (var store = HistoryStore.Open(ledgerPath, WorldFingerprint(allOn)))
                ledger = RunSeasonCore(allOn, MteCheckSeed, configPath, verbose: false, store);
            using (var store = HistoryStore.Open(ledgerPath, WorldFingerprint(allOn)))
                spentAfter = RawGameNumber(store.ReserveGames(1)[0]);

            Check("C10b: ★ THE LEDGER SPENT EXACTLY conference count + reservation count, and every " +
                  "reservation was consumed once — no unreserved id was used and no id was wasted",
                  spentAfter - spentBefore - 1 == ledger.ConferenceGameCount + ledger.TournamentGameCount
                  && ledger.PlayedGames.Select(p => p.Game.GameId!.Value.ToString()).Distinct().Count()
                     == ledger.PlayedGames.Count,
                  $"ledger advanced {spentAfter - spentBefore - 1} for " +
                  $"{ledger.ConferenceGameCount}+{ledger.TournamentGameCount}");

            //  ★ THE CAREER THE RECORD AND THE LOG ARE READ FROM USES THE WORLD AS AUTHORED,
            //    NOT every event forced on — because the record C8 round-trips must be MIXED,
            //    and the natural draw at this seed leaves one event dormant.
            var careerPath = Path.Combine(scratch, "career.json");
            SeasonRunOutcome career;
            long careerSeason;
            string careerHistoryId, careerWorldFp;
            using (var store = HistoryStore.Open(careerPath, WorldFingerprint(mte)))
            {
                careerHistoryId = store.HistoryId;
                careerWorldFp = store.WorldFingerprint;
                careerSeason = store.PeekNextSeasonId;
                career = RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false, store,
                                       retainGameLog: true);
            }

            Check("C10c: ★ dormant AND short events hold no reservations, spend no ids and produce no " +
                  "games — an id reserved for a field that never plays is wasted permanently",
                  MteExpectedBracketSlots(career.Events.Seating).Count == career.TournamentGameCount
                  && career.Events.Seating.Dormant.Count > 0
                  && career.Events.Seating.Active
                       .Where(e => !MteEventPlays(e))
                       .All(e => career.PlayedGames.All(p => p.EventId != e.EventId)),
                  $"{career.Events.Seating.Dormant.Count} dormant, " +
                  $"{career.TournamentGameCount} tournament games");

            //  ★ THE SHORT FIELD, which is a DATA ERROR and not a modelled state (Emmett's
            //    ruling). S98 adds no cancelled status and no validation branch: it simply has
            //    no games. S97's C4j had to construct a single-league world to make SeatedShort
            //    fire at all, and this borrows it.
            {
                var starved = MteStarvedWorld(mte);
                var starvedSeating = MteSeatSeason(starved, MteCheckSeed, MteHistory.Empty);
                var shortOnes = starvedSeating.Active.Where(e => !MteEventPlays(e)).ToList();
                Check("C10g: ★ a SHORT field holds no bracket slots at all — it cannot play, so it must " +
                      "not hold ids",
                      shortOnes.Count > 0
                      && MteExpectedBracketSlots(starvedSeating).Count == 0,
                      $"{shortOnes.Count} short event(s), " +
                      $"{MteExpectedBracketSlots(starvedSeating).Count} slots");
            }

            //  ── C6: the log ────────────────────────────────────────────────────
            {
                var logPath = GameLogWriter.FinalPathFor(careerPath, careerSeason);
                var log = GameLogReader.ReadFinalized(
                    logPath, new GameLogBindings(careerHistoryId, careerWorldFp, careerSeason,
                                                 career.Fingerprint));
                Check("C6a: ★ the reader ACCEPTS the file — a season log with non-conference blocks in " +
                      "it is a legal log, and the ordinals are contiguous from zero",
                      log.Blocks.Count == career.ConferenceGameCount + career.TournamentGameCount
                      && log.Blocks.Select((b, i) => b.Facts.FixtureOrdinal == i).All(x => x),
                      $"{log.Blocks.Count} blocks");
                //  The game object and the block byte asserted SEPARATELY, because the claim is
                //  that a non-conf Kind derives the byte with no production change (S97's writer
                //  already computes it) — one assertion covering both would prove neither.
                var tourneyOrdinals = career.PlayedGames.Where(p => p.IsEventGame)
                                            .Select(p => p.FixtureOrdinal).ToHashSet();
                Check("C6b: conference blocks carry the conference flag and tournament blocks do not",
                      log.Blocks.All(b => b.Facts.IsConferenceGame != tourneyOrdinals.Contains(b.Facts.FixtureOrdinal)));
                Check("C6c: every tournament GameId appears exactly once in the log",
                      career.PlayedGames.Where(p => p.IsEventGame)
                            .All(p => log.Blocks.Count(b => b.Facts.GameId.Equals(p.Game.GameId!.Value)) == 1));
            }

            //  ── C8: the record ─────────────────────────────────────────────────
            var recPath = MteRecordPathFor(careerPath, careerSeason);
            {
                Check("C8a: ★ the finishes were persisted and the round-trip is over a MIXED record",
                      career.Events.FinishStatus == EventRecordStatus.Written);

                using var doc = JsonDocument.Parse(File.ReadAllText(recPath));
                var evs = doc.RootElement.GetProperty("events").EnumerateArray().ToList();
                var completed = evs.Where(e => e.GetProperty("playStatus").GetString() == "Completed").ToList();
                var notPlayed = evs.Where(e => e.GetProperty("playStatus").GetString() == "NotPlayed").ToList();
                Check("C8b: complete ACTIVE events read back Completed with a FULL finish map — every " +
                      "place 1..N present exactly once, addressed by SEAT",
                      completed.Count == career.EventFinishes.Count
                      && completed.All(e =>
                      {
                          var size = e.GetProperty("fieldSize").GetInt32();
                          var places = e.GetProperty("finishBySeat").EnumerateArray()
                                        .Select(x => x.GetProperty("place").GetInt32()).OrderBy(x => x).ToList();
                          return places.SequenceEqual(Enumerable.Range(1, size));
                      }),
                      $"{completed.Count} completed, {notPlayed.Count} not played");
                Check("C8c: a dormant or short event is unchanged at NotPlayed with no finishes",
                      notPlayed.All(e => e.GetProperty("finishBySeat").ValueKind == JsonValueKind.Null)
                      && doc.RootElement.GetProperty("dormantEvents").GetArrayLength() >= 0);
                Check("C8d: the finish map is addressed BY SEAT, and seat order and seed order disagree " +
                      "in this field — a check that confused them would fail here",
                      career.EventFinishes.TryGetValue(10, out var palm)
                      && palm.Count == 8
                      && career.Events.Seating.Active.First(e => e.EventId == 10).Seats
                              .All(s => palm.ContainsKey(s.Seat)));

                //  ── the three refusals, each on its own doctored copy ───────────
                var pristine = File.ReadAllBytes(recPath);
                var empty = new Dictionary<int, IReadOnlyDictionary<int, int>>();
                var seatsOf = career.Events.Seating.Active
                    .Where(e => MteEventPlays(e))
                    .ToDictionary(e => e.EventId,
                                  e => (IReadOnlyDictionary<int, int>)e.Seats.ToDictionary(s => s.Seat, s => s.SchoolId));
                var finishesOf = career.EventFinishes;

                using (var store = HistoryStore.Open(careerPath, careerWorldFp))
                {
                    var msg = Refusal(() => MteReplaceRecordWithFinishes(
                        store, careerSeason, finishesOf, seatsOf));
                    Check("C8e: ★ the replacement REFUSES a record already marked Completed — there is " +
                          "no reopen path, so this is a corruption tripwire and not a lifecycle branch",
                          msg is not null && msg.Contains("is not NotPlayed"),
                          msg is null ? "NO REFUSAL" : "refused");
                    Check("C8f: and the record on disk is byte-untouched by the refusal",
                          File.ReadAllBytes(recPath).SequenceEqual(pristine));
                }

                //  A fresh NotPlayed record to attack, so the remaining refusals are not
                //  answered by the Completed guard firing first.
                var freshPath = Path.Combine(scratch, "fresh.json");
                using (var store = HistoryStore.Open(freshPath, WorldFingerprint(mte)))
                {
                    var freshSeason = store.PeekNextSeasonId;
                    RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false, store);
                    var freshRec = MteRecordPathFor(freshPath, freshSeason);
                    // That run completed it; rewrite the file back to NotPlayed to attack it.
                    File.WriteAllText(freshRec, File.ReadAllText(freshRec)
                        .Replace("\"playStatus\": \"Completed\"", "\"playStatus\": \"NotPlayed\"", StringComparison.Ordinal));
                    var reNotPlayed = File.ReadAllText(freshRec);
                    var withNulls = System.Text.RegularExpressions.Regex.Replace(
                        reNotPlayed, "\"finishBySeat\": \\[[^\\]]*\\]", "\"finishBySeat\": null");
                    File.WriteAllText(freshRec, withNulls);
                    var restored = File.ReadAllBytes(freshRec);

                    var badSeats = seatsOf.ToDictionary(
                        kv => kv.Key,
                        kv => (IReadOnlyDictionary<int, int>)kv.Value.ToDictionary(
                            s => s.Key, s => s.Key == 1 ? -99 : s.Value));
                    var msg2 = Refusal(() => MteReplaceRecordWithFinishes(
                        store, freshSeason, finishesOf, badSeats));
                    Check("C8g: ★ the replacement REFUSES when the seats on disk are not the field the " +
                          "games were played between",
                          msg2 is not null && msg2.Contains("seats on disk"),
                          msg2 is null ? "NO REFUSAL" : "refused");
                    Check("C8h: and that refusal too left the record byte-identical, and left no temp " +
                          "file behind",
                          File.ReadAllBytes(freshRec).SequenceEqual(restored)
                          && Directory.GetFiles(MteRecordFolderFor(freshPath), ".charm-events-*.tmp").Length == 0);

                    //  ★ ATOMICITY, NOT MERELY REFUSAL. The failure is forced through the INJECTED
                    //    replace delegate — never file permissions, an invalid path, OS locking or a
                    //    global mutable switch, every one of which would prove something about the
                    //    filesystem rather than about this method.
                    var msg3 = Refusal(() => MteReplaceRecordWithFinishes(
                        store, freshSeason, finishesOf, seatsOf,
                        replace: (_, _) => throw new IOException("injected")));
                    Check("C8i: ★ a failure forced AT THE RENAME leaves the NotPlayed record " +
                          "BYTE-IDENTICAL and leaves no temp file behind",
                          msg3 is not null && msg3.Contains("injected")
                          && File.ReadAllBytes(freshRec).SequenceEqual(restored)
                          && Directory.GetFiles(MteRecordFolderFor(freshPath), ".charm-events-*.tmp").Length == 0);

                    var msg4 = Refusal(() => MteReplaceRecordWithFinishes(
                        store, freshSeason, finishesOf, seatsOf));
                    Check("C8j: ★ and the SUCCESS path still works afterwards, leaving no temp file — " +
                          "the failure was a hole, not damage",
                          msg4 is null
                          && Directory.GetFiles(MteRecordFolderFor(freshPath), ".charm-events-*.tmp").Length == 0
                          && File.ReadAllText(freshRec).Contains("\"playStatus\": \"Completed\""));
                }
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — HOST MEMORY DOES NOT SEE THEM, and the leak is CONSTRUCTED.
            // ════════════════════════════════════════════════════════════════════
            {
                //  ★ THIS FIXTURE DELIBERATELY DOES NOT PASS THROUGH S97 SEATING. The
                //    conference cap key forbids two schools of one playing league sharing a
                //    field, which is exactly the field this check needs — so the retained block
                //    is supplied directly. Recorded so nobody burns an hour trying to coerce the
                //    seater into a state it is built to refuse.
                //
                //    The leak is built so that it WOULD matter: schools 1 and 2 meet ONCE in
                //    conference (home 1), which is a live single-meeting residual, and once more
                //    as a tournament game with the home side REVERSED. If the non-conference
                //    filter is not applied the pair reads 1-1, the gap closes, and the residual
                //    disappears — a total filtering failure, caught.
                var leakPath = Path.Combine(scratch, "leak.json");
                var fp = WorldFingerprint(allOn);
                long leakSeason;
                using (var store = HistoryStore.Open(leakPath, fp))
                {
                    var sid = store.ReserveSeason();
                    leakSeason = store.PeekNextSeasonId - 1;
                    var roster = MinimalRoster(store, "Leak");
                    var w = GameLogWriter.Create(leakPath, store.HistoryId, fp, new string('e', 64),
                                                 sid, roster);
                    var ids = store.ReserveGames(2);
                    w.AppendGame(new GameBlockFactsV1(ids[0], 0, 1, 2, true, 70, 68, 0, 140),
                                 MinimalRows(roster[0].PersonId));
                    w.AppendGame(new GameBlockFactsV1(ids[1], 1, 2, 1, false, 71, 69, 0, 141),
                                 MinimalRows(roster[0].PersonId));
                    w.Finalize(2);
                    w.Dispose();
                }
                using (var store = HistoryStore.Open(leakPath, fp))
                {
                    var mem = ReadHostMemory(store);
                    var has = mem.PreviousResidualHost.TryGetValue((1, 2), out var host);
                    Check("C7a: ★ HOST MEMORY DOES NOT SEE A TOURNAMENT GAME. The rigged non-conference " +
                          "block reverses the very pair that owns a live residual; the residual survives " +
                          "intact, which it could not if the filter were missing",
                          mem.Status == HostMemoryStatus.Loaded && has && host == 1
                          && mem.ConferenceGamesRead == 1,
                          $"{mem.ConferenceGamesRead} conference games read of 2 blocks, residual host {host}");
                    Check("C7b: and the leak really was present — two blocks were written, so C7a is not " +
                          "passing on a log that never contained the thing being filtered",
                          leakSeason >= 1
                          && GameLogReader.ReadFinalized(
                                 GameLogWriter.FinalPathFor(leakPath, leakSeason),
                                 new GameLogBindings(store.HistoryId, fp, leakSeason, null))
                             .Blocks.Count(b => !b.Facts.IsConferenceGame) == 1);
                }
            }

            // ════════════════════════════════════════════════════════════════════
            //  C9 — WIN PERCENTAGE, on the rule the page actually calls.
            // ════════════════════════════════════════════════════════════════════
            {
                //  A constructed table, so the discriminator is exact: under RAW WINS the 4-2
                //  school outranks the 3-1 school; under PERCENTAGE it does not.
                var probe = new SeasonRunOutcome
                {
                    Schedule = new List<SeasonGame>(), Fingerprint = "", Results = new List<SeasonGameResult>(),
                    Wins = new Dictionary<int, int> { [1] = 3, [2] = 4, [3] = 1, [4] = 2, [5] = 0, [6] = 0 },
                    Losses = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 6, [5] = 0, [6] = 0 },
                    Divvy = on.Divvy, League = new SeasonLeagueStats(),
                };
                var order = SeasonStandingsOrder(probe);
                var ids = new List<int> { 6, 5, 4, 3, 2, 1 };
                ids.Sort(order);

                Check("C9a: ★ THE DISCRIMINATOR — a 3-1 school outranks a 4-2 school. Under raw wins " +
                      "the 4-2 school wins, which is the distortion this ruling exists to remove",
                      ids.IndexOf(1) < ids.IndexOf(2),
                      "order " + string.Join(",", ids));
                Check("C9b: equal percentages break on the LOWER SCHOOL ID (1-3 and 2-6 are both .250)",
                      SeasonWinPctText(probe, 3) == SeasonWinPctText(probe, 4)
                      && ids.IndexOf(3) < ids.IndexOf(4));
                Check("C9c: ★ a school that played nothing prints an em dash, never .000 — it did not " +
                      "lose, it did not play",
                      SeasonWinPctText(probe, 5) == "—" && SeasonWinPctText(probe, 6) == "—");
                Check("C9d: both zero-game schools sort BELOW everyone who played, and break by school id",
                      ids.IndexOf(5) > ids.IndexOf(1) && ids.IndexOf(5) > ids.IndexOf(4)
                      && ids.IndexOf(5) < ids.IndexOf(6));

                //  ★ Emmett's ruling, asserted as MECHANISM: who is inside the denominator. The
                //    band VALUE is page-only and is never asserted.
                var band = SeasonBandWinPct(mte, on, out var counts);
                var playedSchools = mte.Schools.Count(s => SeasonGamesPlayed(on, s.Id) > 0);
                Check("C9e: ★ a school that never played is OUT of the band averages entirely (Emmett's " +
                      "ruling) — the counted population is exactly the schools that played",
                      counts.Values.Sum() == playedSchools
                      && playedSchools < mte.Schools.Count,
                      $"{counts.Values.Sum()} counted of {mte.Schools.Count} schools");
                Check("C9f: and the discriminator — this world really does contain a school with no " +
                      "games, so C9e is not a rule about an empty set",
                      mte.Schools.Any(s => SeasonGamesPlayed(on, s.Id) == 0) && band.Count > 0);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10 — DETERMINISM OVER A NAMED BUNDLE. "Identical everything" is not
            //  a contract, so the bundle is listed.
            // ════════════════════════════════════════════════════════════════════
            {
                var again = RunSeasonCore(allOn, MteCheckSeed, configPath, verbose: false);
                var confA = SeasonFingerprint(
                    on.Results.Take(on.ConferenceGameCount).ToList(),
                    on.PossessionCounts.Take(on.ConferenceGameCount).ToList());
                var confB = SeasonFingerprint(
                    again.Results.Take(again.ConferenceGameCount).ToList(),
                    again.PossessionCounts.Take(again.ConferenceGameCount).ToList());

                var orderA = mte.Schools.Select(s => s.Id).ToList(); orderA.Sort(SeasonStandingsOrder(on));
                var orderB = mte.Schools.Select(s => s.Id).ToList(); orderB.Sort(SeasonStandingsOrder(again));

                Check("C10d: ★ the named bundle reproduces — conference results fingerprint, event-games " +
                      "fingerprint, standings order, and the finish map",
                      confA == confB
                      && on.EventGamesFingerprint == again.EventGamesFingerprint
                      && orderA.SequenceEqual(orderB)
                      && on.EventFinishes.Count == again.EventFinishes.Count
                      && on.EventFinishes.All(kv =>
                             again.EventFinishes[kv.Key].Count == kv.Value.Count
                             && kv.Value.All(s => again.EventFinishes[kv.Key][s.Key] == s.Value)),
                      on.EventGamesFingerprint[..16] + "…");

                //  A second career, so the GameId assignments and the retained log's BYTES are
                //  compared too — the fixture ordinals and the non-conference byte are
                //  load-bearing, so repeated runs must persist the same log.
                var twinPath = Path.Combine(scratch, "twin.json");
                SeasonRunOutcome twin;
                long twinSeason;
                string twinId, twinFp;
                using (var store = HistoryStore.Open(twinPath, WorldFingerprint(mte)))
                {
                    twinId = store.HistoryId; twinFp = store.WorldFingerprint;
                    twinSeason = store.PeekNextSeasonId;
                    twin = RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false, store,
                                         retainGameLog: true);
                }
                //  ★ The LOG is compared by its decoded blocks, not by raw bytes. Two careers
                //    carry two different historyIds by design (S89), and that id is stamped in
                //    the file header — so byte-identity across careers is a claim about the
                //    identity layer rather than about this session, and it would be false for
                //    reasons that have nothing to do with brackets. What must reproduce is what
                //    S98 put in the file: the ordinal, the kind byte, the game number and the
                //    basketball.
                var logA = GameLogReader.ReadFinalized(
                    GameLogWriter.FinalPathFor(careerPath, careerSeason),
                    new GameLogBindings(careerHistoryId, careerWorldFp, careerSeason, career.Fingerprint));
                var logB = GameLogReader.ReadFinalized(
                    GameLogWriter.FinalPathFor(twinPath, twinSeason),
                    new GameLogBindings(twinId, twinFp, twinSeason, twin.Fingerprint));
                static string BlockLine(GameLogBlockV1 b)
                {
                    var f = b.Facts;
                    return string.Join("|", new[]
                    {
                        f.FixtureOrdinal.ToString(CultureInfo.InvariantCulture),
                        (f.IsConferenceGame ? 0 : 1).ToString(CultureInfo.InvariantCulture),
                        f.GameId.ToString(),
                        f.HomeSchoolId.ToString(CultureInfo.InvariantCulture),
                        f.AwaySchoolId.ToString(CultureInfo.InvariantCulture),
                        f.HomeScore.ToString(CultureInfo.InvariantCulture),
                        f.AwayScore.ToString(CultureInfo.InvariantCulture),
                        f.PossessionCount.ToString(CultureInfo.InvariantCulture),
                    });
                }

                Check("C10e: ★ a second career at the same seed assigns the same GameIds in the same " +
                      "slots, and its retained log persists the same ordinals, kind bytes and games",
                      twin.PlayedGames.Select(p => p.Game.GameId!.Value.ToString())
                          .SequenceEqual(career.PlayedGames.Select(p => p.Game.GameId!.Value.ToString()))
                      && logA.Blocks.Count == logB.Blocks.Count
                      && logA.Blocks.Select(BlockLine).SequenceEqual(logB.Blocks.Select(BlockLine)),
                      $"{twin.PlayedGames.Count} fixtures, {logA.Blocks.Count} blocks, " +
                      $"seasons {careerSeason}/{twinSeason}");
                Check("C10f: and the two careers really are separate files, so C10e is not comparing a " +
                      "log with itself",
                      !string.Equals(twinPath, careerPath, StringComparison.Ordinal)
                      && twinId.Length > 0 && twinFp.Length > 0);
            }
        }
        catch (Exception ex)
        {
            Check("Phase 89 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { /* scratch cleanup is best effort */ }
        }

        Console.WriteLine(pass ? "  Phase 89: PASS" : "  Phase 89: FAIL");
        return pass;
    }

    /// <summary>GameId hides its number by design (S89 made the raw accessor internal with no
    /// InternalsVisibleTo, and the seam holds against the suite too), so the ledger is measured
    /// the only way a caller can: out of the identity's own rendering. Clumsy on purpose — the
    /// alternative is opening the seam.</summary>
    private static long RawGameNumber(GameId id)
    {
        var s = id.ToString();
        return long.Parse(s[(s.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);
    }
}
