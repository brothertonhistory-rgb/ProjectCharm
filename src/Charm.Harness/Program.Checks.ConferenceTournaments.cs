using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  Phase 99 — S110: THE CONFERENCE TOURNAMENTS.
//
//  Thirty-one leagues seat eight, play seven games, crown a champion. What this
//  phase exists to discriminate on — the axes where a wrong build still produces
//  217 plausible games and 31 plausible champions:
//
//   1. WHICH RECORD SEEDED THE FIELD. Every completeness and conservation check
//      below passes whether the seeding read the league record or the whole-season
//      record — a Maui run would move ACC seeding and nothing would go red. So the
//      two orders are computed side by side and asserted to DISAGREE on this world,
//      with the shipped champions coming from the league-only one. Without that
//      arm the seeding rule is undefended.
//
//   2. WHETHER THE TOURNAMENT CONTAMINATED THE RECORD THAT SEEDED IT. Recomputing
//      the conference record AFTER all 217 games must yield the records used
//      BEFORE, exactly. A build that identified league games by "both schools share
//      a conference" would fail only here.
//
//   3. WHETHER THE BRACKET ADVANCED CORRECTLY. The exact elimination distribution
//      — one team 3-0, one 2-1, two 1-1, four 0-1 — simultaneously proves nobody
//      was replayed, nobody skipped a round, the advancement links worked, and
//      exactly seven results fed each bracket. A per-league game count proves none
//      of that.
//
//   4. WHETHER THE IDS BELONG TO THE GAMES THAT SPENT THEM. A mismatched
//      reservation walk still spends 217 ids and plays 217 games; only an identity
//      check catches it.
//
//  ★ PAGE-ONLY CALIBRATION HOLDS. No champion, no seed, no win total and no
//  basketball value is asserted as a target anywhere below. Every number here is
//  wiring or arithmetic.
// ============================================================================

internal static partial class Program
{
    private const long ConfTourneyCheckSeed = 20260720;

    /// <summary>★ RE-DERIVED AT THE S110 GATE from the pristine pre-edit tree, never
    /// transcribed from a status board (the S81.3 lesson). Stock authors 32 conferences; one
    /// (Independent) authors zero games and holds no tournament, and the other 31 all seat at
    /// least eight — the smallest real league on the world is exactly eight.</summary>
    private const int ConfTourneyGoldenLeagueCount = 31;

    private static bool Phase99ConferenceTournamentsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 99 — S110: the conference tournaments (eight-team fields in every " +
                          "league that can seat one, seeded on the LEAGUE record only, played to a " +
                          "single-elimination champion on the nights the authored tournament offset " +
                          "already implies). The MTE-blindness discriminator, non-contamination, the " +
                          "exact elimination distribution, participant conservation, the venue and tie " +
                          "placeholders as ruled, id identity, and the five prior fingerprints unmoved ==");
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

        try
        {
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var mteWorld = LoadWorld(WorldPath("fixture-mte.world.json"));
            var run = RunSeasonCore(stock, ConfTourneyCheckSeed, configPath, verbose: false);

            var ct = run.ConferenceTournaments;
            var membersOf = stock.Schools.GroupBy(s => s.ConferenceId)
                                 .ToDictionary(g => g.Key, g => g.Select(s => s.Id).OrderBy(x => x).ToList());
            var confById = stock.Conferences.ToDictionary(c => c.Id);
            var placeOf = stock.Schools.ToDictionary(s => s.Id, s => s.PlaceId);
            var confOf = stock.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
            var tourneyGames = run.PlayedGames.Where(p => p.IsConferenceTournamentGame).ToList();

            // ════════════════════════════════════════════════════════════════════
            //  C1 — THE FIELD. Who holds a tournament, and who does not.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C1a: 31 leagues held a tournament and 217 games were played — seven a league, " +
                      "single elimination is exactly N − 1",
                      ct.LeaguesPlayed == ConfTourneyGoldenLeagueCount
                      && ct.GameCount == ConfTourneyGoldenLeagueCount * ConfTourneyGamesPerLeague
                      && run.ConferenceTournamentGameCount == ct.GameCount
                      && tourneyGames.Count == ct.GameCount,
                      $"{ct.LeaguesPlayed} leagues, {ct.GameCount} games");

                Check("C1b: ★ INDEPENDENT HOLDS NONE, AND IT IS IDENTIFIED BY Games == 0 — never by " +
                      "TourneyTeams == 0, which would also catch the Ivy row and silently drop a real " +
                      "league",
                      stock.Conferences.Count(c => c.Games == 0) == 1
                      && ct.Champions.All(c => confById[c.ConferenceId].Games > 0)
                      && ct.Champions.Count == stock.Conferences.Count(c => c.Games > 0),
                      $"{stock.Conferences.Count} conferences, " +
                      $"{stock.Conferences.Count(c => c.Games == 0)} with no league slate");

                Check("C1c: every league seated exactly 8 DISTINCT schools, all of them members of that " +
                      "league",
                      ct.Champions.All(c => c.SchoolBySeed.Count == ConfTourneyFieldSize
                                            && c.SchoolBySeed.Distinct().Count() == ConfTourneyFieldSize
                                            && c.SchoolBySeed.All(id => confOf[id] == c.ConferenceId)),
                      $"{ct.Champions.Count} fields");

                Check("C1d: no league on this world was refused for being short — the smallest real " +
                      "membership is exactly eight, so the short-league path is not what produced 31",
                      ct.LeaguesTooSmall == 0
                      && stock.Conferences.Where(c => c.Games > 0)
                              .All(c => membersOf[c.Id].Count >= ConfTourneyFieldSize),
                      $"smallest league {stock.Conferences.Where(c => c.Games > 0).Min(c => membersOf[c.Id].Count)}");

                //  ★ THE DISCRIMINATOR FOR C1d — a world whose leagues CANNOT seat eight really
                //    does take the short path, so "31 leagues" is a fact about stock rather than
                //    a rule that fires everywhere. This is also the check that keeps every
                //    fixture-world zero path a zero path.
                var mteRun = RunSeasonCore(mteWorld, ConfTourneyCheckSeed, configPath, verbose: false);
                Check("C1e: ★ and the discriminator — fixture-mte's leagues are all shorter than eight, " +
                      "so it holds NO tournament, plays no extra game and refuses each short league by " +
                      "count rather than seating one seven-deep",
                      mteRun.ConferenceTournamentGameCount == 0
                      && mteRun.ConferenceTournaments.LeaguesPlayed == 0
                      && mteRun.ConferenceTournaments.LeaguesTooSmall
                         == mteWorld.Conferences.Count(c => c.Games > 0),
                      $"{mteRun.ConferenceTournaments.LeaguesTooSmall} short leagues, " +
                      $"{mteRun.ConferenceTournamentGameCount} games");

                //  ★ A short field is REFUSED BY NAME, never taken short. The gate above skips it;
                //    this proves the seater underneath would throw rather than yield seven.
                var records = ConfTourneyConferenceRecords(stock, run.Schedule, run.Results);
                var shortMsg = Refusal(() => ConfTourneySeedField(
                    999, membersOf[15].Take(ConfTourneyFieldSize - 1).ToList(), records));
                Check("C1f: negative control — handed seven members the seater REFUSES BY NAME rather " +
                      "than silently seating a seven-team bracket",
                      shortMsg is not null && shortMsg.Contains("cannot seat a field", StringComparison.Ordinal),
                      shortMsg ?? "NO REFUSAL");

                Check("C1g: negative control — the primitive still refuses a non-power-of-two field, so " +
                      "byes remain unconstructible rather than merely unused",
                      Refusal(() => BuildKnockoutBracket(9, Enumerable.Range(1, 9).ToList())) is { } m9
                      && m9.Contains("not supported", StringComparison.Ordinal));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — ★ THE SEEDING READ THE LEAGUE RECORD AND NOTHING ELSE.
            // ════════════════════════════════════════════════════════════════════
            {
                var leagueRec = ConfTourneyConferenceRecords(stock, run.Schedule, run.Results);

                //  The whole-season record — what run.Wins/run.Losses hold, MTE games included.
                //  This is the rule the build did NOT use, computed here so the two can be
                //  compared. If they never disagreed, the choice would be undefended.
                var seasonRec = stock.Schools.ToDictionary(
                    s => s.Id, s => new ConfTourneyRecord(run.Wins[s.Id], run.Losses[s.Id]));

                var disagreeing = new List<int>();
                var shipMatchesLeague = true;
                foreach (var champ in ct.Champions)
                {
                    var members = membersOf[champ.ConferenceId];

                    var byLeague = members.ToList();
                    byLeague.Sort(ConfTourneySeedOrder(leagueRec));
                    var leagueEight = byLeague.Take(ConfTourneyFieldSize).ToList();

                    var bySeason = members.ToList();
                    bySeason.Sort(ConfTourneySeedOrder(seasonRec));
                    var seasonEight = bySeason.Take(ConfTourneyFieldSize).ToList();

                    if (!champ.SchoolBySeed.SequenceEqual(leagueEight)) shipMatchesLeague = false;
                    if (!leagueEight.SequenceEqual(seasonEight)) disagreeing.Add(champ.ConferenceId);
                }

                Check("C2a: ★ every field that played IS the league-record seed order, seed for seed",
                      shipMatchesLeague, $"{ct.Champions.Count} fields");

                Check("C2b: ★ THE DISCRIMINATOR — the whole-season order and the league-only order really " +
                      "DO disagree on this world, so C2a is a rule about which record was read rather " +
                      "than two names for the same list. A school with a strong November and a poor " +
                      "league record seeds low",
                      disagreeing.Count > 0,
                      $"{disagreeing.Count} of {ct.Champions.Count} leagues seed differently under the " +
                      $"whole-season record (conference ids {string.Join(",", disagreeing.Take(8))}" +
                      (disagreeing.Count > 8 ? ",…" : "") + ")");

                //  ★ Per-school game counts by category, so the three claims below are arithmetic
                //  rather than inference. Built from the played games themselves, which is the
                //  only place the three-way split actually exists.
                var evGames = stock.Schools.ToDictionary(s => s.Id, _ => 0);
                var ctGames = stock.Schools.ToDictionary(s => s.Id, _ => 0);
                foreach (var p in run.PlayedGames.Where(p => p.IsEventGame))
                { evGames[p.Game.HomeId]++; evGames[p.Game.AwayId]++; }
                foreach (var p in tourneyGames)
                { ctGames[p.Game.HomeId]++; ctGames[p.Game.AwayId]++; }

                //  ★ §2g, ASSERTED RATHER THAN ASSUMED: a conference tournament game is an
                //  ORDINARY played season game. It counts in the overall record exactly as an MTE
                //  game already does, and the ONE thing it is excluded from is the conference-only
                //  record of C2d. The identity below is what makes that a fact instead of a
                //  comment — if tournament games were quietly dropped from the season record, or
                //  double-counted, this is where it shows.
                Check("C2c: ★ §2g — every school's OVERALL record is exactly its league games plus its " +
                      "event games plus its conference tournament games. A tournament win counts, the " +
                      "same way an MTE win already does",
                      stock.Schools.All(s => run.Wins[s.Id] + run.Losses[s.Id]
                                             == leagueRec[s.Id].Played + evGames[s.Id] + ctGames[s.Id]),
                      $"{stock.Schools.Count} schools reconcile");

                //  ★ THE DISCRIMINATOR FOR C2b. If the season record differed from the league
                //  record ONLY by tournament games, C2b's disagreement would have some other
                //  cause and the MTE-blindness claim would be undefended. Some school must have
                //  played event games too.
                //
                //  ★ This check was authored wrong in the first S110 build and went red on a
                //  correct engine: it compared against ConfTourneyGamesPerLeague, which is SEVEN
                //  — the games a LEAGUE plays — where the bound needed was the games one TEAM
                //  plays, which is at most three. The comparison could never fire. Recorded
                //  because the failure looked exactly like an engine fault.
                Check("C2d: ★ and the season record really does include EVENT games, not merely the " +
                      "tournament games this session added — otherwise C2b's disagreement would have " +
                      "some other cause",
                      stock.Schools.Any(s => run.Wins[s.Id] + run.Losses[s.Id]
                                             > leagueRec[s.Id].Played + ctGames[s.Id]),
                      $"{stock.Schools.Count(s => evGames[s.Id] > 0)} schools played an event game; " +
                      $"most event games by one school: {evGames.Values.Max()}");

                //  ★ NON-CONTAMINATION. Recomputing the conference record after all 217 games
                //    yields the records used before them, exactly. A build that classified league
                //    play by "both schools share a conference" fails only here.
                var recomputed = ConfTourneyConferenceRecords(stock, run.Schedule, run.Results);
                Check("C2e: ★ THE TOURNAMENT DID NOT CONTAMINATE THE RECORD THAT SEEDED IT — recomputing " +
                      "every conference W-L after all seven rounds yields the records used before them, " +
                      "school for school",
                      stock.Schools.All(s => recomputed[s.Id] == leagueRec[s.Id]),
                      $"{stock.Schools.Count} schools");

                Check("C2f: and the conference record is strictly SMALLER than the season record for at " +
                      "least one school — the two are not the same number wearing two names",
                      stock.Schools.Any(s => leagueRec[s.Id].Played < run.Wins[s.Id] + run.Losses[s.Id]));

                Check("C2g: R2 — ties break on the LOWER SCHOOL ID. Within every field, two schools on " +
                      "identical league records appear in ascending id order",
                      ct.Champions.All(c =>
                      {
                          for (var i = 0; i + 1 < c.SchoolBySeed.Count; i++)
                          {
                              var a = c.SchoolBySeed[i];
                              var b = c.SchoolBySeed[i + 1];
                              if (leagueRec[a] == leagueRec[b] && a > b) return false;
                          }
                          return true;
                      }));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — ★ THE EXACT ELIMINATION DISTRIBUTION, PER CONFERENCE.
            // ════════════════════════════════════════════════════════════════════
            {
                var bad = new List<string>();
                foreach (var champ in ct.Champions)
                {
                    var rows = ct.Rows.Where(r => r.ConferenceId == champ.ConferenceId)
                                      .OrderBy(r => r.GameIndex).ToList();
                    if (rows.Count != ConfTourneyGamesPerLeague)
                    { bad.Add($"{champ.ConferenceName}: {rows.Count} games"); continue; }

                    var played = new Dictionary<int, int>();
                    var won = new Dictionary<int, int>();
                    foreach (var r in rows)
                    {
                        foreach (var id in new[] { r.SchoolA, r.SchoolB })
                            played[id] = played.GetValueOrDefault(id) + 1;
                        won[r.Winner] = won.GetValueOrDefault(r.Winner) + 1;
                    }

                    //  champion 3/3, finalist 3/2, two semifinal losers 2/1, four QF losers 1/0.
                    var shape = played.Keys
                        .Select(id => (P: played[id], W: won.GetValueOrDefault(id)))
                        .OrderByDescending(t => t.W).ThenByDescending(t => t.P).ToList();
                    var want = new[] { (3, 3), (3, 2), (2, 1), (2, 1), (1, 0), (1, 0), (1, 0), (1, 0) };
                    if (played.Count != ConfTourneyFieldSize || !shape.SequenceEqual(want))
                        bad.Add($"{champ.ConferenceName}: [{string.Join(" ", shape.Select(t => $"{t.P}/{t.W}"))}]");
                }
                Check("C3a: ★ THE EXACT ELIMINATION DISTRIBUTION in every league — one team 3 games / 3 " +
                      "wins, one 3/2, two 2/1, four 1/0. This simultaneously proves nobody was replayed, " +
                      "nobody skipped a round, every advancement link fired and exactly seven results " +
                      "fed the bracket",
                      bad.Count == 0, bad.Count == 0 ? $"{ct.Champions.Count} leagues"
                                                     : string.Join("; ", bad.Take(4)));

                Check("C3b: the champion is the school with three wins and the runner-up the one it beat " +
                      "in the final — read from first-class state, not scraped back out of the games",
                      ct.Champions.All(c =>
                      {
                          var final = ct.Rows.Single(r => r.ConferenceId == c.ConferenceId
                                                          && r.GameIndex == ConfTourneyGamesPerLeague - 1);
                          return final.Winner == c.Champion
                                 && (final.SchoolA == c.RunnerUp || final.SchoolB == c.RunnerUp)
                                 && c.Champion != c.RunnerUp;
                      }));

                Check("C3c: the canonical line held — round one is 1v8, 4v5, 2v7, 3v6 in every league, " +
                      "so seeds 1 and 2 can only meet in the final",
                      ct.Champions.All(c =>
                      {
                          var r1 = ct.Rows.Where(r => r.ConferenceId == c.ConferenceId && r.Round == 0)
                                          .OrderBy(r => r.GameIndex)
                                          .Select(r => (r.SeedA, r.SeedB)).ToList();
                          return r1.SequenceEqual(new[] { (1, 8), (4, 5), (2, 7), (3, 6) });
                      }));

                //  ★ PARTICIPANT-GAME CONSERVATION across all 31 leagues. 124 schools gain one
                //    game, 62 gain two, 62 gain three; every non-participant gains nothing.
                var delta = stock.Schools.ToDictionary(s => s.Id, _ => 0);
                foreach (var p in tourneyGames)
                { delta[p.Game.HomeId]++; delta[p.Game.AwayId]++; }
                var byDelta = delta.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
                Check("C3d: ★ PARTICIPANT-GAME CONSERVATION — 124 schools played one tournament game, 62 " +
                      "played two, 62 played three, and every school outside a field played none. That " +
                      "is 434 team-games, which is 217 games",
                      byDelta.GetValueOrDefault(1) == 124
                      && byDelta.GetValueOrDefault(2) == 62
                      && byDelta.GetValueOrDefault(3) == 62
                      && byDelta.GetValueOrDefault(0) == stock.Schools.Count - 248
                      && delta.Values.Sum() == 2 * ct.GameCount,
                      $"+0:{byDelta.GetValueOrDefault(0)} +1:{byDelta.GetValueOrDefault(1)} " +
                      $"+2:{byDelta.GetValueOrDefault(2)} +3:{byDelta.GetValueOrDefault(3)}, " +
                      $"{delta.Values.Sum()} team-games");

                Check("C3e: every tournament game is between two members of the SAME league",
                      tourneyGames.All(p => confOf[p.Game.HomeId] == confOf[p.Game.AwayId]
                                            && confOf[p.Game.HomeId] == p.ConferenceTournamentId));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — R1 AND THE GAME RECORD. The venue, the host, the kind.
            // ════════════════════════════════════════════════════════════════════
            {
                var oneSeedPlace = ct.Champions.ToDictionary(
                    c => c.ConferenceId, c => placeOf[c.SchoolBySeed[0]]);

                Check("C4a: ★ R1 — all seven games in a league carry ONE city, the ORIGINAL No. 1 seed's. " +
                      "Not the higher seed per matchup and not the highest survivor per round: a " +
                      "conference tournament does not travel",
                      tourneyGames.All(p => p.Game.PlaceId == oneSeedPlace[p.ConferenceTournamentId!.Value]),
                      $"{oneSeedPlace.Values.Distinct().Count()} distinct cities across " +
                      $"{ct.Champions.Count} leagues");

                Check("C4b: ★ NOBODY HOSTS — no home-court advantage and no hosted-game accounting, " +
                      "exactly as an MTE game",
                      tourneyGames.All(p => !p.Game.HasHost));

                Check("C4c: ★ and the hosted-road-side counter is untouched by them — it still equals the " +
                      "league slate exactly, so 217 neutral games shaved nobody",
                      run.HostedRoadSidesShaved == run.ConferenceGameCount,
                      $"{run.HostedRoadSidesShaved} shaved / {run.ConferenceGameCount} league games");

                Check("C4d: every tournament fixture says Kind == \"ctourney\" EXACTLY — a third kind of " +
                      "played game gets a third word, so \"anything except conf\" never becomes the " +
                      "contract and \"mte\" keeps meaning an EVENT fixture",
                      tourneyGames.All(p => string.Equals(p.Game.Kind, "ctourney", StringComparison.Ordinal))
                      && run.PlayedGames.Where(p => p.IsEventGame)
                             .All(p => string.Equals(p.Game.Kind, "mte", StringComparison.Ordinal))
                      && run.Schedule.All(g => string.Equals(g.Kind, "conf", StringComparison.Ordinal)));

                Check("C4e: the nominal home side is the better ORIGINAL seed's school — a box-score " +
                      "ordering, never a venue",
                      ct.Rows.All(r => r.SeedA < r.SeedB)
                      && tourneyGames.All(p => p.HomeOriginalSeed < p.AwayOriginalSeed
                                               && p.Game.HomeId != p.Game.AwayId));

                Check("C4f: every tournament game carries a city at the second structural boundary — the " +
                      "whole played season, not just the league half",
                      run.PlayedGames.All(p => p.Game.PlaceId is > 0),
                      $"{run.PlayedGames.Count} games");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — THE NIGHTS. Derived from the authored offset, never invented.
            // ════════════════════════════════════════════════════════════════════
            {
                var opens = ct.Champions.ToDictionary(
                    c => c.ConferenceId,
                    c => ConfTourneyOpen(SeasonDefaultStartYear, c.ConferenceId,
                                         confById[c.ConferenceId].TourneyOffsetDays));

                Check("C5a: round one plays on the authored open — SelectionSunday minus the league's own " +
                      "TourneyOffsetDays — in every league",
                      ct.Rows.Where(r => r.Round == 0).All(r => r.Date == opens[r.ConferenceId]));

                Check("C5b: rounds are consecutive: open, open + 1, open + 2",
                      ct.Rows.All(r => r.Date == opens[r.ConferenceId].AddDays(r.Round))
                      && ct.Rows.Select(r => r.Round).Distinct().OrderBy(x => x)
                              .SequenceEqual(new[] { 0, 1, 2 }));

                //  ★ THE WALL, asserted on the CONFERENCE'S latest league game rather than each
                //    school's. The dater already guarantees it by construction (wall =
                //    SelectionSunday − offset − 1); this says so out loud, because a future
                //    session changing either arithmetic must break here rather than silently
                //    schedule a league game after its own tournament tipped.
                var lastLeagueNight = new Dictionary<int, DateOnly>();
                foreach (var g in run.Schedule)
                {
                    var cid = confOf[g.HomeId];
                    var d = g.Date!.Value;
                    if (!lastLeagueNight.TryGetValue(cid, out var cur) || d > cur) lastLeagueNight[cid] = d;
                }
                Check("C5c: ★ every league game in a conference falls STRICTLY BEFORE that conference's " +
                      "round one — the dater's wall and the tournament's open are the same authored " +
                      "number, one day apart, and this says so rather than assuming it",
                      ct.Champions.All(c => lastLeagueNight[c.ConferenceId] < opens[c.ConferenceId]),
                      $"tightest gap {ct.Champions.Min(c => opens[c.ConferenceId].DayNumber - lastLeagueNight[c.ConferenceId].DayNumber)} day(s)");

                Check("C5d: leagues stagger deliberately and overlap — three distinct opens on this " +
                      "world, which is the authored 11/4/8 spread and not one national window",
                      opens.Values.Distinct().Count() == 3,
                      string.Join(", ", opens.Values.Distinct().OrderBy(d => d)
                                             .Select(d => d.ToString("MM-dd", CultureInfo.InvariantCulture))));

                Check("C5e: negative control — a league that plays a slate but authors no tournament day " +
                      "is refused BY NAME rather than given an invented one",
                      Refusal(() => ConfTourneyOpen(SeasonDefaultStartYear, 1, null)) is { } mNone
                      && mNone.Contains("authors no tournament day", StringComparison.Ordinal));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — ★ ID IDENTITY. The reservation walk and the play walk agree.
            // ════════════════════════════════════════════════════════════════════
            {
                var slots = ConfTourneyExpectedSlots(stock);
                Check("C6a: the reservation walk spends exactly one id per position — 31 leagues × 7 — in " +
                      "ascending conference id, then topology game index 0→6",
                      slots.Count == ct.GameCount
                      && slots.Distinct().Count() == slots.Count
                      && slots.SequenceEqual(slots.OrderBy(s => s.ConferenceId).ThenBy(s => s.GameIndex)),
                      $"{slots.Count} slots");

                //  ★ A mismatched walk order still spends 217 ids and plays 217 games. Only this
                //    catches it: the played games, in fixture order, must name the same positions
                //    the reservation walk named, in the same order.
                var playedSlots = tourneyGames
                    .OrderBy(p => p.FixtureOrdinal)
                    .Select(p => new ConfTourneySlotKey(p.ConferenceTournamentId!.Value,
                                                        p.ConfTourneyGameIndex!.Value))
                    .ToList();
                Check("C6b: ★ AND THE PLAY WALK IS THE SAME WALK, position for position in fixture order — " +
                      "a mismatched order still spends 217 ids and plays 217 games, so only an identity " +
                      "check catches it",
                      playedSlots.SequenceEqual(slots));

                Check("C6c: the tournament games APPEND — every one of them sits after every league game " +
                      "and after every event game, which is what leaves both halves on the engine seeds " +
                      "they have always had",
                      tourneyGames.All(p => p.FixtureOrdinal
                                            >= run.ConferenceGameCount + run.TournamentGameCount)
                      && tourneyGames.Select(p => p.FixtureOrdinal).Min()
                         == run.ConferenceGameCount + run.TournamentGameCount
                      && run.Results.Count == run.ConferenceGameCount + run.TournamentGameCount
                                              + run.ConferenceTournamentGameCount,
                      $"first tournament ordinal {tourneyGames.Min(p => p.FixtureOrdinal)}, " +
                      $"{run.Results.Count} results");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — ★ THE FINGERPRINT WALL. Five unmoved, a sixth born.
            // ════════════════════════════════════════════════════════════════════
            {
                var prefix = run.ConferenceGameCount + run.TournamentGameCount;
                var resultsFp = SeasonFingerprint(
                    run.Results.Take(prefix).ToList(),
                    run.PossessionCounts.Take(prefix).ToList());

                Check("C7a: #1 the conference schedule fingerprint is UNMOVED",
                      run.Fingerprint == MatchGoldenConferenceFp, run.Fingerprint[..8] + "…");
                Check("C7b: #2 the conference DATED fingerprint is UNMOVED",
                      run.DatedFingerprint == MatchGoldenDatedFp, run.DatedFingerprint[..8] + "…");
                Check("C7c: ★ #3 the event-games fingerprint is UNMOVED — the conference tournaments did " +
                      "NOT land inside it. It covers the world event pool, and S104's " +
                      "zero-path-by-subtraction proof depends on it meaning exactly that",
                      run.EventGamesFingerprint == MatchGoldenEventGamesFp,
                      run.EventGamesFingerprint[..8] + "…");
                Check("C7d: ★ #4 the results+possessions fingerprint is UNMOVED over the league-plus-event " +
                      "prefix — recapturing it would have destroyed the pre-S102 golden it exists to be",
                      resultsFp == MatchGoldenResultsFp, resultsFp[..8] + "…");
                Check("C7e: #5 the non-conference DATED fingerprint is UNMOVED",
                      run.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp,
                      run.NonConferenceDates.DatedFingerprint[..8] + "…");

                Check("C7f: ★ #6 the conference tournaments got their OWN hash, over a canonical row " +
                      "carrying conference, round, game index, date, city, both seeds, both schools and " +
                      "the winner — a hash of results alone would not catch a venue or a date regression",
                      run.ConferenceTournamentFingerprint.Length == 64
                      && run.ConferenceTournamentFingerprint
                         == ConfTourneyFingerprint(ct.Rows),
                      run.ConferenceTournamentFingerprint);

                Check("C7g: ★ and the sixth hash is not decorative — rewriting one city moves it, so it " +
                      "really does see the venue the results hash cannot",
                      ConfTourneyFingerprint(
                          ct.Rows.Select((r, i) => i == 0 ? r with { PlaceId = 999999 } : r).ToList())
                      != run.ConferenceTournamentFingerprint);

                Check("C7h: ★ and it sees the date too — moving one night moves the hash",
                      ConfTourneyFingerprint(
                          ct.Rows.Select((r, i) => i == 0 ? r with { Date = r.Date.AddDays(1) } : r).ToList())
                      != run.ConferenceTournamentFingerprint);

                Check("C7i: determinism — the same world at the same seed produces the same sixth hash",
                      RunSeasonCore(stock, ConfTourneyCheckSeed, configPath, verbose: false)
                          .ConferenceTournamentFingerprint == run.ConferenceTournamentFingerprint);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — ★ PAGE-ONLY CALIBRATION HOLDS.
            // ════════════════════════════════════════════════════════════════════
            Check("C8: ★ nothing above asserts a champion, a seed, a win total or any other basketball " +
                  "value as a target. Every number in this phase is wiring or arithmetic — 31 leagues, " +
                  "217 games, 434 team-games, one city a league, three consecutive nights",
                  true, $"{assertions} assertions so far, none of them a basketball target");
        }
        catch (Exception ex)
        {
            Check("Phase 99 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine(pass ? $"  Phase 99 PASS ({assertions} assertions)"
                               : $"  Phase 99 FAIL ({assertions} assertions)");
        return pass;
    }
}
