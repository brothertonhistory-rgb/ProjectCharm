using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 55 — Season Pass 2 checks (Session 30).
//
// What a green Phase 55 PROVES: the stock schedule is legal and matches the
// Python oracle's fingerprint bit-for-bit at the fixed seed; the builder is
// deterministic; the preflight rejects an impossible world by NAME (and does
// not over-reject a merely small one); the season prep path serves all 347
// stock schools without mutating the world; a full fixture season conserves
// results (every team 30, wins == losses == games, zero ties), credits scores
// to the right schools (the attribution replay), and reproduces exactly.
// Session 31 adds §3.8: the calibration accumulator conserves — points
// reconcile three independent ways, the ending buckets partition the records,
// per-game elapsed matches TotalSeconds — while asserting ZERO basketball
// target values (those are page-only by design; see Program.Season.Calibration.cs).
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
                Path.Combine(AppContext.BaseDirectory, "data", "conf.csv"));
            const long seed = 20260703;   // Session 30's fixed seed (Phase 54 used 20260702)

            // Oracle exports (tools/schedule_oracle.py, fixed seed 20260703):
            const string stockOracleFp   = "29fd9e2584aaa714bb1dbebd8d5d00e9585b82f0057d209c21b326bee7fb3c3f";
            const string fixtureOracleFp = "50167eae4a08754a323afcd2104b1924b411e1fc7391838de6419f612996b304";

            // ── §3.1 Stock schedule legality (schedule only — no games played). ──────
            var schedule = BuildSeasonSchedule(stock, seed);
            Check("stock: 5205 games total (347 × 30 / 2)", schedule.Count == 5205,
                  $"got {schedule.Count}");

            var confOf = stock.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
            var byConf = new Dictionary<int, List<int>>();
            foreach (var s in stock.Schools.OrderBy(x => x.Id))
            {
                if (!byConf.TryGetValue(s.ConferenceId, out var list))
                    byConf[s.ConferenceId] = list = new List<int>();
                list.Add(s.Id);
            }

            var total = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            var confN = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            var nonconfN = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            var homeN = stock.Schools.ToDictionary(s => s.Id, _ => 0);
            var pairConf = new Dictionary<(int, int), int>();
            var pairNonconf = new Dictionary<(int, int), int>();
            var selfGames = 0; var confCross = 0; var nonconfMates = 0;
            foreach (var g in schedule)
            {
                if (g.HomeId == g.AwayId) { selfGames++; continue; }
                total[g.HomeId]++; total[g.AwayId]++; homeN[g.HomeId]++;
                var key = (Math.Min(g.HomeId, g.AwayId), Math.Max(g.HomeId, g.AwayId));
                if (g.Kind == "conf")
                {
                    confN[g.HomeId]++; confN[g.AwayId]++;
                    if (confOf[g.HomeId] != confOf[g.AwayId]) confCross++;
                    pairConf[key] = pairConf.GetValueOrDefault(key) + 1;
                }
                else
                {
                    nonconfN[g.HomeId]++; nonconfN[g.AwayId]++;
                    if (confOf[g.HomeId] == confOf[g.AwayId]) nonconfMates++;
                    pairNonconf[key] = pairNonconf.GetValueOrDefault(key) + 1;
                }
            }
            Check("stock: no self-games", selfGames == 0, $"{selfGames} found");
            Check("stock: every team plays exactly 30 (16 conf + 14 nonconf)",
                  total.Values.All(v => v == 30) && confN.Values.All(v => v == 16)
                    && nonconfN.Values.All(v => v == 14),
                  $"totals {total.Values.Min()}-{total.Values.Max()}, conf {confN.Values.Min()}-{confN.Values.Max()}, " +
                  $"nonconf {nonconfN.Values.Min()}-{nonconfN.Values.Max()}");
            Check("stock: exactly 15 home / 15 away for every team",
                  homeN.Values.All(v => v == 15),
                  $"home range {homeN.Values.Min()}-{homeN.Values.Max()}");
            Check("stock: conference games never cross conferences", confCross == 0, $"{confCross} crossed");
            Check("stock: no non-conference game pairs conference-mates", nonconfMates == 0, $"{nonconfMates} found");
            Check("stock: no non-conference pair meets twice",
                  pairNonconf.Values.All(v => v == 1),
                  $"max meetings {(pairNonconf.Count > 0 ? pairNonconf.Values.Max() : 0)}");

            var meetingRuleOk = true; var meetingDetail = "";
            foreach (var (cid, members) in byConf)
            {
                var s = members.Count;
                var baseMeet = 16 / (s - 1);
                for (var i = 0; i < members.Count - 1 && meetingRuleOk; i++)
                    for (var j = i + 1; j < members.Count; j++)
                    {
                        var c = pairConf.GetValueOrDefault((members[i], members[j]));
                        if (c != baseMeet && c != baseMeet + 1)
                        {
                            meetingRuleOk = false;
                            meetingDetail = $"conf {cid} pair ({members[i]},{members[j]}) meets {c}, " +
                                            $"expected {baseMeet} or {baseMeet + 1}";
                            break;
                        }
                    }
            }
            Check("stock: every conference pair meets floor or ceil of 16/(s-1) times",
                  meetingRuleOk, meetingDetail);

            var fp = ScheduleFingerprint(schedule);
            Check("stock: schedule fingerprint matches the Python oracle at seed 20260703",
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
            Check("stock: 5205 distinct resolver seeds, 5205 distinct governor seeds, sets disjoint",
                  resolverSeeds.Count == 5205 && governorSeeds.Count == 5205
                    && !resolverSeeds.Overlaps(governorSeeds));

            // ── §3.2 Build determinism: the schedule is a pure function of (world, seed). ──
            var schedule2 = BuildSeasonSchedule(stock, seed);
            Check("stock: building twice yields the identical schedule (fingerprint + sequence)",
                  ScheduleFingerprint(schedule2) == fp && schedule.SequenceEqual(schedule2));

            // ── §3.3 Preflight: impossible world rejected BY NAME; small world is not. ──
            var lowTier = tiny.Tiers.OrderBy(t => t.Floor).First().Id;
            WorldFile Rig(params int[] movedIdx)
            {
                var moved = movedIdx.ToHashSet();
                return new WorldFile
                {
                    SchemaVersion = 1, Kind = tiny.Kind, EraLabel = tiny.EraLabel,
                    Division = tiny.Division, WorldSeed = tiny.WorldSeed, Tiers = tiny.Tiers,
                    Conferences = tiny.Conferences
                        .Append(new WorldConference(999, "Lonely", "LON", lowTier)).ToList(),
                    Schools = tiny.Schools
                        .Select((s, i) => moved.Contains(i) ? s with { ConferenceId = 999 } : s).ToList(),
                };
            }

            var lonely = Rig(0);   // one school alone in conference 999
            var lonelyValid = true; var lonelyValidMsg = "";
            try { ValidateWorld(lonely); }
            catch (InvalidOperationException ex) { lonelyValid = false; lonelyValidMsg = ex.Message; }
            Check("rigged one-school-conference world passes the Pass 1 validator " +
                  "(so the red below is the preflight's, not a load error)", lonelyValid, lonelyValidMsg);

            var rejected = false; var rejectMsg = "";
            try { SeasonPreflight(lonely); }
            catch (InvalidOperationException ex) { rejected = true; rejectMsg = ex.Message; }
            Check("preflight rejects the one-school conference, naming it",
                  rejected && rejectMsg.Contains("Lonely") && rejectMsg.Contains("s-1 = 0"),
                  rejectMsg);

            var pair = Rig(0, 5);   // a TWO-school conference: legal (16 meetings of one opponent)
            var pairOk = true; var pairMsg = "";
            try { SeasonPreflight(pair); }
            catch (InvalidOperationException ex) { pairOk = false; pairMsg = ex.Message; }
            Check("preflight does NOT reject a two-school conference (s=2 is legal: base=16, r=0)",
                  pairOk, pairMsg);

            // ── §3.4 Stock season prep (adapted — see the header note). ──────────────
            var prestigeBefore = stock.Schools.OrderBy(s => s.Id)
                .Select(s => (s.Id, s.CurrentPrestige)).ToList();
            var stockDivvy = RunDivvyDraft(stock, seed);
            var stockRows = BuildSeasonRows(stockDivvy, stock, verbose: false);
            var prestigeAfter = stock.Schools.OrderBy(s => s.Id)
                .Select(s => (s.Id, s.CurrentPrestige)).ToList();
            Check("stock prep: rows built for all 347 schools, ten players each",
                  stockRows.Count == 347 && stockRows.Values.All(r => r.Count == 10));
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
            var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false);
            Check("fixture: schedule fingerprint matches the oracle (20 schools, 300 games)",
                  outcome.Fingerprint == fixtureOracleFp && outcome.Results.Count == 300,
                  outcome.Fingerprint == fixtureOracleFp ? "" : $"got {outcome.Fingerprint}");
            Check("fixture: every team has exactly 30 results (W+L == 30)",
                  tiny.Schools.All(s => outcome.Wins[s.Id] + outcome.Losses[s.Id] == 30));
            Check("fixture: results conserve — total wins == total losses == 300, zero ties",
                  outcome.Wins.Values.Sum() == 300 && outcome.Losses.Values.Sum() == 300
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
            var (replayGame, _, _) = RunSingleGenGame(
                cfgs,
                BuildSeasonSide(replayRows[g0.HomeId], 0),
                BuildSeasonSide(replayRows[g0.AwayId], 10),
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
            Check("fixture: a second full season run is identical (schedule, every score, standings)",
                  outcome2.Fingerprint == outcome.Fingerprint
                    && outcome2.Results.SequenceEqual(outcome.Results)
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
