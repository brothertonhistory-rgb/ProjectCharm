using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 55 — Season checks (Session 30; rewritten at Session 93).
//
// What a green Phase 55 PROVES: the stock schedule is legal and matches the
// Python oracle's fingerprint; the builder is deterministic; the preflight
// rejects an impossible world by NAME (and does not over-reject a merely small
// one, nor an idle one); the season prep path serves all 347 stock schools
// without mutating the world; a full fixture season conserves results (every
// team its league's number, wins == losses == games, zero ties), credits scores
// to the right schools (the attribution replay), and reproduces exactly.
// Session 31 adds §3.8: the calibration accumulator conserves — points
// reconcile three independent ways, the ending buckets partition the records,
// per-game elapsed matches TotalSeconds — while asserting ZERO basketball
// target values (those are page-only by design; see Program.Season.Calibration.cs).
//
// ★ S93 — REWRITTEN, NOT EXTENDED. The flat 16-conference / 14-non-conference /
// 5,205-game / every-team-30 / exactly-15-home assertions were against a
// schedule that no longer exists. What survives is re-pointed at the authored
// numbers, and the generic checks DERIVE their totals from the loaded world so
// a fixture can never inherit a stock constant; the exact 2,818 is asserted
// once, as a stock-world golden.
//
// ★ AND ONE WARNING THAT MATTERS MORE THAN ANY CHECK HERE: the exactly-even
// home/away assertion PASSED BEFORE S93 TOO. The old Eulerian walk landed all
// 347 schools at 8 home and 8 away by accident of even degrees, at every seed
// tried. So a green R3 line here is a promise being kept, NOT evidence that the
// new orientation works. Phase 84's A9 — pre-fixed venues, which a Eulerian
// cannot honour — is the assertion that discriminates.
//
// What it does NOT prove: outcome realism, calibration direction, or that the
// prestige-vs-wins relationship on the page is basketball truth — those are
// page-level observations for Emmett, never suite assertions.
//
// Divergence from the build prompt, named loudly: §3.4's draft assumed the
// season path constructs GenPrograms (the smoke sim does, because
// RunGenMatchup's signature demands a GenConfig). The Session 30 extraction
// showed the game body never reads one — so the season path carries NO
// GenProgram at all. Prestige's only door into a season game is the roster the
// divvy drafted; there is no side door to clamp. §3.4 therefore asserts the
// stronger structural fact (world unmutated, prestige-0 schools still read 0,
// full legal prep for all 347) instead of a clamp on plumbing that does not
// exist on this path.
// ============================================================================
//
// Divergence from the build prompt, named loudly: §3.4's draft assumed the
// season path constructs GenPrograms (the smoke sim does, because
// RunGenMatchup's signature demands a GenConfig). The Session 30 extraction
// showed the game body never reads one — so the season path carries NO
// GenProgram at all. Prestige's only door into a season game is the roster the
// divvy drafted; there is no side door to clamp. §3.4 therefore asserts the
// stronger structural fact (world unmutated, prestige-0 schools still read 0,
// full legal prep for all 347) instead of a clamp on plumbing that does not
// exist on this path.
// ============================================================================

internal static partial class Program
{
    private static bool Phase55SeasonCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 55 — Season Pass 2 (minimal season loop: schedule + standings) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
            var tiny = LoadWorld(fixturePath);
            var stock = ConvertWorld(
                Path.Combine(AppContext.BaseDirectory, "data", "teams.csv"),
                Path.Combine(AppContext.BaseDirectory, "data", "conf.csv"),
                Path.Combine(AppContext.BaseDirectory, "data", "places.csv"));
            const long seed = 20260703;   // Session 30's fixed seed (Phase 54 used 20260702)

            // ★ S93 — the oracle exports. The schedule consumes no randomness, so these are
            //   a function of the WORLD alone and the seed no longer enters them.
            const string stockOracleFp   = "6f79d6636e291866d51387f93979d817011f7903ddc64e67d4ebcebf087cb5c3";
            const string fixtureOracleFp = "6fc122dd3bc4f48a6f7c8b3787dcc236603536d4d610bf53ad0934480b189981";   // S105.2: the 12-game ruling

            // What the world itself says the season is. ★ Generic checks DERIVE their totals
            // from the loaded world so a fixture can never inherit a stock constant; the exact
            // stock number is asserted ONCE below, as a golden.
            static int ConfSize(WorldFile w, int cid) => w.Schools.Count(s => s.ConferenceId == cid);
            static int ExpectedGames(WorldFile w) => w.Conferences.Sum(c => ConfSize(w, c.Id) * c.Games / 2);

            // ── §3.1 Stock schedule legality (schedule only — no games played). ──────
            var schedule = BuildSeasonSchedule(stock, seed);
            var expectedStock = ExpectedGames(stock);
            Check("stock: the season is exactly the conference slate, derived from the world",
                  schedule.Count == expectedStock, $"got {schedule.Count}, world says {expectedStock}");
            // ★ THE ONE STOCK GOLDEN. 2,818 is what the 32 authored game counts add up to; if
            //   this moves while the derived check above still passes, somebody edited conf.csv.
            Check("stock: 2818 conference games (the authored counts, as a stock-world golden)",
                  schedule.Count == 2818, $"got {schedule.Count}");

            var confOf = stock.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
            var gamesOf = stock.Conferences.ToDictionary(c => c.Id, c => c.Games);
            var skipOf = stock.Conferences.ToDictionary(c => c.Id, c => c.Skip);
            var byConf = new Dictionary<int, List<int>>();
            foreach (var s in stock.Schools.OrderBy(x => x.Id))
            {
                if (!byConf.TryGetValue(s.ConferenceId, out var list))
                    byConf[s.ConferenceId] = list = new List<int>();
                list.Add(s.Id);
            }

            var total = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            var homeN = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            var pairConf = new Dictionary<(int, int), int>();
            var selfGames = 0; var confCross = 0; var nonConfKind = 0;
            foreach (var g in schedule)
            {
                if (g.HomeId == g.AwayId) { selfGames++; continue; }
                if (g.Kind != "conf") { nonConfKind++; continue; }
                total[g.HomeId]++; total[g.AwayId]++; homeN[g.HomeId]++;
                if (confOf[g.HomeId] != confOf[g.AwayId]) confCross++;
                var key = (Math.Min(g.HomeId, g.AwayId), Math.Max(g.HomeId, g.AwayId));
                pairConf[key] = pairConf.GetValueOrDefault(key) + 1;
            }
            Check("stock: no self-games", selfGames == 0, $"{selfGames} found");
            Check("stock: every game is a conference game — no non-conference game exists",
                  nonConfKind == 0, $"{nonConfKind} found");
            Check("stock: every team plays exactly its own league's authored number of games",
                  stock.Schools.All(s => total[s.Id] == gamesOf[s.ConferenceId]),
                  string.Join(", ", stock.Schools.Where(s => total[s.Id] != gamesOf[s.ConferenceId])
                                                 .Take(3).Select(s => $"{s.Name} {total[s.Id]} vs {gamesOf[s.ConferenceId]}")));
            // ★ R3, THE HARD LINE. Note this PASSED before S93 too (the old Eulerian landed
            //   8/8 by accident of even degrees) — it is a promise now, not evidence. Phase 84
            //   A9 is the assertion that discriminates.
            Check("stock: exactly even home/away for every team (Games/2 each) — R3",
                  stock.Schools.All(s => homeN[s.Id] == gamesOf[s.ConferenceId] / 2),
                  string.Join(", ", stock.Schools.Where(s => homeN[s.Id] != gamesOf[s.ConferenceId] / 2)
                                                 .Take(3).Select(s => $"{s.Name} {homeN[s.Id]}")));
            Check("stock: conference games never cross conferences", confCross == 0, $"{confCross} crossed");

            // The fourteen Independent schools: authored at zero games, so they play none.
            var idle = stock.Schools.Where(s => gamesOf[s.ConferenceId] == 0).ToList();
            Check("stock: a conference authored at zero games plays no games at all (R14) — " +
                  "the 14 Independent schools appear in no game",
                  idle.Count == 14 && idle.All(s => total[s.Id] == 0),
                  $"{idle.Count} zero-game schools, max games {(idle.Count > 0 ? idle.Max(s => total[s.Id]) : 0)}");

            var meetingRuleOk = true; var meetingDetail = "";
            foreach (var (cid, members) in byConf.OrderBy(kv => kv.Key))
            {
                var g = gamesOf[cid]; var k = skipOf[cid];
                if (g == 0) continue;
                var p = members.Count - 1 - k; var q = g / p; var r = g - q * p;
                foreach (var x in members)
                {
                    var counts = members.Where(y => y != x)
                        .Select(y => pairConf.GetValueOrDefault((Math.Min(x, y), Math.Max(x, y))))
                        .ToList();
                    if (counts.Count(c => c == 0) != k || counts.Count(c => c == q + 1) != r
                        || counts.Count(c => c == q) != p - r)
                    {
                        meetingRuleOk = false;
                        meetingDetail = $"conf {cid} school {x}: {counts.Count(c => c == 0)} skipped " +
                                        $"(want {k}), {counts.Count(c => c == q + 1)} at {q + 1} (want {r}), " +
                                        $"{counts.Count(c => c == q)} at {q} (want {p - r})";
                        break;
                    }
                }
                if (!meetingRuleOk) break;
            }
            Check("stock: every school meets k opponents never, r opponents q+1 times and the " +
                  "rest q times — asserted per school, not per league average",
                  meetingRuleOk, meetingDetail);

            var fp = ScheduleFingerprint(schedule);
            Check("stock: schedule fingerprint matches the Python oracle",
                  fp == stockOracleFp, fp == stockOracleFp ? fp : $"got {fp}");
            Check("stock: game 0 matches the oracle export (conf, 6 at home vs 23)",
                  schedule[0].Kind == "conf" && schedule[0].HomeId == 6 && schedule[0].AwayId == 23,
                  $"got ({schedule[0].Kind}, {schedule[0].HomeId}, {schedule[0].AwayId})");

            // Per-game engine seeds: distinct within the season, resolver and governor
            // sets disjoint (the stride-2 scheme).
            var seasonBase = unchecked((int)seed);
            var resolverSeeds = new HashSet<int>();
            var governorSeeds = new HashSet<int>();
            for (var g = 0; g < schedule.Count; g++)
            {
                resolverSeeds.Add(unchecked(seasonBase + 2 * g));
                governorSeeds.Add(unchecked(seasonBase + 2 * g + 1));
            }
            Check($"stock: {schedule.Count} distinct resolver seeds, {schedule.Count} distinct " +
                  "governor seeds, sets disjoint",
                  resolverSeeds.Count == schedule.Count && governorSeeds.Count == schedule.Count
                    && !resolverSeeds.Overlaps(governorSeeds));

            // ── §3.2 Build determinism: the schedule is a pure function of the world. ──
            var schedule2 = BuildSeasonSchedule(stock, seed);
            Check("stock: building twice yields the identical schedule (fingerprint + sequence)",
                  ScheduleFingerprint(schedule2) == fp && schedule.SequenceEqual(schedule2));
            // ★ S93 — and a DIFFERENT seed yields the same schedule too, because no randomness
            //   enters the slate any more. Asserted rather than left implicit, so that the day
            //   a session wires a scheduler RNG this check goes red and says so.
            var scheduleOtherSeed = BuildSeasonSchedule(stock, seed + 1);
            Check("stock: a different seed yields the SAME schedule — the slate consumes no " +
                  "randomness (the seed still drives every outcome)",
                  ScheduleFingerprint(scheduleOtherSeed) == fp);

            // ── §3.3 Preflight: impossible world rejected BY NAME; legal ones are not. ──
            var lowTier = tiny.Tiers.OrderBy(t => t.Floor).First().Id;
            WorldFile Rig(int games, int skip, params int[] movedIdx)
            {
                var moved = movedIdx.ToHashSet();
                return new WorldFile
                {
                    // ★ S97 — the CONSTANT, not a literal. This rig existed to prove the
                    //   preflight rejects a one-school league; hardcoding the version meant
                    //   that the day v4 retired, the check went red saying the world was
                    //   old rather than saying the league was impossible.
                    SchemaVersion = WorldSchemaVersion, Kind = tiny.Kind, EraLabel = tiny.EraLabel,
                    Division = tiny.Division, WorldSeed = tiny.WorldSeed, Tiers = tiny.Tiers,
                    Places = tiny.Places,
                    Conferences = tiny.Conferences
                        .Append(new WorldConference(999, "Lonely", "LON", lowTier, games, skip,
                            new[] { "sat", "wed", "mon" }, Math.Max(1, games / 2), 8)).ToList(),
                    Schools = tiny.Schools
                        .Select((s, i) => moved.Contains(i) ? s with { ConferenceId = 999 } : s).ToList(),
                };
            }

            var lonely = Rig(16, 0, 0);   // one school alone in a conference that wants 16 games
            var lonelyValid = true; var lonelyValidMsg = "";
            try { ValidateWorld(lonely); }
            catch (InvalidOperationException ex) { lonelyValid = false; lonelyValidMsg = ex.Message; }
            Check("rigged one-school-conference world passes the Pass 1 validator " +
                  "(so the red below is the preflight's, not a load error)", lonelyValid, lonelyValidMsg);

            var rejected = false; var rejectMsg = "";
            try { SeasonPreflight(lonely); }
            catch (InvalidOperationException ex) { rejected = true; rejectMsg = ex.Message; }
            Check("preflight rejects the one-school conference, naming it",
                  rejected && rejectMsg.Contains("Lonely") && rejectMsg.Contains("needs an opponent"),
                  rejectMsg);

            var pair = Rig(16, 0, 0, 5);   // a TWO-school conference: legal (16 meetings of one opponent)
            var pairOk = true; var pairMsg = "";
            try { SeasonPreflight(pair); }
            catch (InvalidOperationException ex) { pairOk = false; pairMsg = ex.Message; }
            Check("preflight does NOT reject a two-school conference (n=2 is legal: q=16, r=0)",
                  pairOk, pairMsg);

            // ★ A one-school conference at ZERO games is legal — it is a conference of
            //   independents, and its size never matters because nobody plays.
            var lonelyIdle = Rig(0, 0, 0);
            var idleOk = true; var idleMsg = "";
            try { SeasonPreflight(lonelyIdle); }
            catch (InvalidOperationException ex) { idleOk = false; idleMsg = ex.Message; }
            Check("preflight does NOT reject a one-school conference authored at ZERO games " +
                  "(R14: it is a conference of independents, and size never binds)",
                  idleOk, idleMsg);

            // ── §3.4 Stock season prep (adapted — see the header note). ──────────────
            var prestigeBefore = stock.Schools.OrderBy(s => s.Id)
                .Select(s => (s.Id, s.CurrentPrestige)).ToList();
            var stockDivvy = RunDivvyDraft(stock, seed);
            var stockRows = BuildSeasonRows(stockDivvy, stock, verbose: false);
            var prestigeAfter = stock.Schools.OrderBy(s => s.Id)
                .Select(s => (s.Id, s.CurrentPrestige)).ToList();
            Check($"stock prep: rows built for all 347 schools, {RosterShape.Size} players each",
                  stockRows.Count == 347 && stockRows.Values.All(r => r.Count == RosterShape.Size));
            Check("stock prep: the loaded world is unmutated (every CurrentPrestige unchanged)",
                  prestigeBefore.SequenceEqual(prestigeAfter));

            var zeroes = stock.Schools.Where(s => s.CurrentPrestige == 0).OrderBy(s => s.Id).ToList();
            bool LegalFive(int schoolId)
            {
                var starters = stockRows[schoolId].Where(r => r.Starter).ToList();
                return starters.Count == 5
                    && starters.Count(r => r.Pos == "B") >= 1
                    && starters.Count(r => r.Pos == "G") >= 2
                    && starters.Count(r => r.Pos == "W") >= 1;   // 30.1 seating floor
            }
            Check("stock prep: exactly two prestige-0 schools, both still read 0 and prep " +
                  "a legal opening five (season path carries NO GenProgram — prestige's only " +
                  "door into a game is the divvied roster)",
                  zeroes.Count == 2 && zeroes.All(s => LegalFive(s.Id)),
                  string.Join(", ", zeroes.Select(s => s.Name)));

            // ── §3.5 Fixture season: conservation + the attribution replay. ──────────
            //     ★ S93 — the counts are DERIVED from the fixture, not written down. The tiny
            //     world's four five-team leagues each play 12 (S105.2's ruling; 16 before),
            //     so it is 120 games of 12 apiece rather than the old 300 of 30 — and a
            //     fixture whose composition changes must not need this file edited to stay honest.
            var tinyGames = ExpectedGames(tiny);
            var tinyPerTeam = tiny.Schools.ToDictionary(
                s => s.Id, s => tiny.Conferences.Single(c => c.Id == s.ConferenceId).Games);
            var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false);
            Check($"fixture: schedule fingerprint matches the oracle ({tiny.Schools.Count} schools, " +
                  $"{tinyGames} games)",
                  outcome.Fingerprint == fixtureOracleFp && outcome.Results.Count == tinyGames,
                  outcome.Fingerprint == fixtureOracleFp
                    ? $"{outcome.Results.Count} games" : $"got {outcome.Fingerprint}");
            Check("fixture: every team has exactly its league's authored number of results (W+L)",
                  tiny.Schools.All(s => outcome.Wins[s.Id] + outcome.Losses[s.Id] == tinyPerTeam[s.Id]));
            Check($"fixture: results conserve — total wins == total losses == {tinyGames}, zero ties",
                  outcome.Wins.Values.Sum() == tinyGames && outcome.Losses.Values.Sum() == tinyGames
                    && outcome.Ties == 0,
                  $"W {outcome.Wins.Values.Sum()}, L {outcome.Losses.Values.Sum()}, ties {outcome.Ties}");
            Check("fixture: every game has a strict winner (assumption 1: the OT loop never " +
                  "lets a tie survive)",
                  outcome.Results.All(r => r.HomeScore != r.AwayScore));

            // The replay: rebuild game 0's sides INDEPENDENTLY and rerun it with the
            // season's seeds. A season runner that flipped HomeScore/AwayScore credit
            // would pass every conservation check above — this is the check that
            // catches exactly that.
            var replayDivvy = RunDivvyDraft(tiny, seed);
            var replayRows = BuildSeasonRows(replayDivvy, tiny, verbose: false);
            var g0 = outcome.Schedule[0];
            var cfgs = LoadGenEngineConfigs(configPath);
            // ★ S95 — the replay must play the SAME GAME the season played, and since S95
            //   that game is played on a home floor: the road side is two points colder.
            //   Without this the two halves of the comparison would be simulating
            //   different basketball and the check would go red with nothing wrong.
            //
            //   This does not weaken the check; it sharpens it. The check exists to catch
            //   the home/away credit being flipped, and it now ALSO catches the shave
            //   landing on the home side — an independently seated pair, prepared the same
            //   way, must reproduce the recorded score exactly.
            var (replayHome, replayAway, _) = PrepareSeasonGameSides(
                BuildSeasonSide(replayRows[g0.HomeId], 0),
                BuildSeasonSide(replayRows[g0.AwayId], RosterShape.AwayIdOffset),
                outcome.RoadShave, hasHost: true);
            var (replayGame, _, _, _) = RunSingleGenGame(
                cfgs, replayHome, replayAway,
                TeamSide.Home, TeamSide.Away,
                resolverSeed: unchecked(seasonBase + 0),
                governorSeed: unchecked(seasonBase + 1));
            var r0 = outcome.Results[0];
            Check("fixture: independent replay of game 0 reproduces the recorded scores " +
                  "(HomeScore credited to the home SCHOOL — the §1b mapping, proven end to end)",
                  replayGame.HomeScore == r0.HomeScore && replayGame.AwayScore == r0.AwayScore,
                  $"replay {replayGame.HomeScore}-{replayGame.AwayScore}, recorded {r0.HomeScore}-{r0.AwayScore}");

            // The standings must be exactly the fold of the per-game results under the
            // same mapping — no second bookkeeping path.
            var recomputedW = tiny.Schools.ToDictionary(s => s.Id, _ => 0);
            foreach (var r in outcome.Results)
                recomputedW[r.HomeScore > r.AwayScore ? r.HomeId : r.AwayId]++;
            Check("fixture: standings are exactly the fold of the recorded results",
                  tiny.Schools.All(s => recomputedW[s.Id] == outcome.Wins[s.Id]));

            // ── §3.6 Fixture determinism: the full season is a pure function of ────────
            //        (world, seed, config). Run #1 is §3.5's outcome, reused.
            var outcome2 = RunSeasonCore(tiny, seed, configPath, verbose: false);
            // ★ S89 — the comparison is now FIELD-EXPLICIT, and this is load-bearing rather
            // than tidiness. `SequenceEqual` used the record's generated equality, which
            // silently absorbs any field ever added to `SeasonGameResult`. The two identity
            // fields added this session are exactly the kind that MUST differ between two
            // runs against one career — that is what "a number is never reused" means — so
            // the old form would have gone red with nothing wrong the first time this check
            // ever saw history mode. What is being asserted here is that the BASKETBALL is a
            // pure function of (world, seed, config); the numbering deliberately is not.
            static bool SameGame(SeasonGameResult a, SeasonGameResult b)
                => a.HomeId == b.HomeId && a.AwayId == b.AwayId
                && a.HomeScore == b.HomeScore && a.AwayScore == b.AwayScore
                && a.OvertimePeriods == b.OvertimePeriods;
            Check("fixture: a second full season run is identical (schedule, every score, standings)",
                  outcome2.Fingerprint == outcome.Fingerprint
                    && outcome2.Results.Count == outcome.Results.Count
                    && outcome2.Results.Zip(outcome.Results).All(p => SameGame(p.First, p.Second))
                    && tiny.Schools.All(s => outcome2.Wins[s.Id] == outcome.Wins[s.Id]
                                          && outcome2.Losses[s.Id] == outcome.Losses[s.Id]));

            // ── §3.8 Calibration instrument (Session 31): MACHINERY only. ────────────
            //        Conservation identities proving the accumulator counts what the
            //        engine produced, all on the §3.5 fixture outcome (zero additional
            //        games). No basketball target values are asserted, ever — the
            //        readout's sim-vs-reference verdicts are page-only by design.
            var lg = outcome.League;
            var scoreSum = outcome.Results.Sum(r => (long)r.HomeScore + r.AwayScore);
            Check("calibration: triple point reconciliation — recorded game scores == " +
                  "record Points == accumulator points",
                  scoreSum == lg.PointsFromRecords && scoreSum == lg.PointsFromScores,
                  $"results {scoreSum}, records {lg.PointsFromRecords}, " +
                  $"accumulator {lg.PointsFromScores}");
            Check("calibration: turnover metadata never drifts outside the classifier " +
                  "(a future TO label turns this red instead of leaking into OTHER)",
                  lg.MetadataDriftRecords == 0,
                  lg.MetadataDriftRecords == 0 ? "" : $"{lg.MetadataDriftRecords} drift records");
            Check("calibration: ending buckets conserve — made + FT-trip + miss->DREB + " +
                  "miss-OOB + turnover + other + excluded == total records",
                  lg.MadeN + lg.FtTripN + lg.MissDrebN + lg.MissOobN + lg.TurnoverN
                    + lg.OtherN + lg.ExcludedN == lg.PossessionRecords,
                  $"{lg.MadeN}+{lg.FtTripN}+{lg.MissDrebN}+{lg.MissOobN}+{lg.TurnoverN}" +
                  $"+{lg.OtherN}+{lg.ExcludedN} vs {lg.PossessionRecords} " +
                  $"(fixed-time sub-line {lg.FixedTimeN} of the turnover bucket)");
            Check("calibration: per-game elapsed guard — sum of record Elapsed matches " +
                  "TotalSeconds in every game (not a season aggregate)",
                  lg.ElapsedMismatchGames == 0,
                  $"{lg.ElapsedMismatchGames} mismatched game(s), max delta " +
                  lg.MaxElapsedMismatch.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
            Check("calibration: FGM >= 3PM, FTA >= FTM, every accumulated total non-negative",
                  lg.Fgm >= lg.ThreePm && lg.Fta >= lg.Ftm
                    && lg.Fga >= 0 && lg.Fgm >= 0 && lg.ThreePa >= 0 && lg.ThreePm >= 0
                    && lg.Fta >= 0 && lg.Ftm >= 0
                    && lg.OReb >= 0 && lg.DReb >= 0 && lg.Ast >= 0 && lg.Stl >= 0 && lg.Blk >= 0
                    && lg.PointsFromScores >= 0 && lg.PointsFromRecords >= 0
                    && lg.PossessionRecords >= 0 && lg.TurnoverPossessions >= 0
                    && lg.TotalSeconds >= 0,
                  $"FGM {lg.Fgm} vs 3PM {lg.ThreePm}, FTA {lg.Fta} vs FTM {lg.Ftm}");
            // Session 32: league-scale zone conservation. The per-possession
            // bins-sum-to-FGA identity is asserted per-seed in Program.Observation
            // (zone-attempt / zone-make bin checks); this proves the ACCUMULATOR
            // preserved it across a full fixture season. No make-rate value is
            // asserted, ever — the suite stays green across every re-tuning of
            // the make dial by design.
            Check("calibration: zone bins conserve at league scale — Rim+Short+Mid+Long+Three " +
                  "FGA == FGA, and the FGM twin",
                  lg.RimFga + lg.ShortFga + lg.MidFga + lg.LongFga + lg.ThreePa == lg.Fga
                    && lg.RimFgm + lg.ShortFgm + lg.MidFgm + lg.LongFgm + lg.ThreePm == lg.Fgm,
                  $"FGA {lg.RimFga}+{lg.ShortFga}+{lg.MidFga}+{lg.LongFga}+{lg.ThreePa} " +
                  $"vs {lg.Fga}; FGM {lg.RimFgm}+{lg.ShortFgm}+{lg.MidFgm}+{lg.LongFgm}" +
                  $"+{lg.ThreePm} vs {lg.Fgm}");

            // ── §3.8 Session 33: the OTHER whitelist guard. After the Roll K leak
            //        is returned home, OTHER must contain ONLY the named residuals —
            //        the four jump-ball labels (suffix exactly Home or Away), the
            //        offensive loose-ball foul, and the offense-OOB ending. Any other
            //        label entering OTHER (including any parked terminal) is a hard
            //        failure naming the label — no future ending can silently hide
            //        here again. parked:* is NOT accepted: every page to date reads
            //        zero parks, so a park is an engine dead-end to investigate.
            var otherOffenders = new List<string>();
            foreach (var kv in lg.OtherByLabel)
            {
                var label = kv.Key;
                var accepted =
                    label is "LooseBallFoulOnOffense" or "OutOfBoundsOffOffense"
                    || ((label.StartsWith("JumpBallTip:", StringComparison.Ordinal)
                         || label.StartsWith("JumpBallArrow:", StringComparison.Ordinal))
                        && label[(label.IndexOf(':') + 1)..] is "Home" or "Away");
                if (!accepted) otherOffenders.Add($"{label} (n={kv.Value.N})");
            }
            Check("calibration: OTHER whitelist — only jump-ball (Home/Away), offensive " +
                  "loose-ball foul, and offense-OOB may reside in OTHER; no label hides here",
                  otherOffenders.Count == 0,
                  otherOffenders.Count == 0 ? "" : string.Join("; ", otherOffenders));


        }
        catch (Exception ex)
        {
            Check("Phase 55 completed without exceptions", false, ex.Message);
        }

        return pass;
    }
}
