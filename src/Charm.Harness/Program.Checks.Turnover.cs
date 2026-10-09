using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 104 — S114: THE TURNOVER, IN MEMORY.
//
//  Season one plays, the rosters turn over (seniors leave, everyone else advances a
//  class, freshmen arrive position for position by prestige), season two plays on
//  the result — all inside one run, nothing saved. What is proven:
//    C1  every senior left and nobody else did
//    C2  every returner is the same man, one class on, same school, same order
//    C3  freshmen are exactly the vacancies, position for position; every roster full
//    C4  classes rolled, no draw involved
//    C5  the freshman class is the same crop (with a negative control at R_LINE + 5)
//    C6  handed out by prestige, like the divvy — mechanism, outcome, equal-weight control
//    C7  deterministic, and the season-two seed matters
//    C8  ★ THE FINGERPRINT WALL — season one inside the stacked run is the standalone
//        season, and the bootstrap divvy is byte-identical through the generalized loop
//    C9  season two plays and reconciles
//    C10 the runner without rosters in hand is unchanged
//    C11 negative controls, each firing the rule it names
//  Page-only: the turnover report and the by-band table are printed, never asserted.
// ============================================================================
internal static partial class Program
{
    private const long TurnoverCheckSeed = 20260720;

    /// <summary>★ C5's negative control, as the prompt proposed it, drew the class at R_LINE + 5.
    /// Measured at the gate that moves the class only +1.5 to +2.9 SE — inside the 3.5 SE bar —
    /// and no higher line fixes it for guards (R_LINE + 20 moves guards +0.0 SE): the scholarship
    /// line reads the generator's budget score, the bar reads the scout rank, and for guards the
    /// two disagree. A raised line is not a different crop by the instrument the bar uses. The
    /// control that fires is a class hand-picked BY scout rank (C5c); the line movement is printed
    /// so the finding stays on the page.</summary>

    private static bool Phase104TurnoverCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 104 — S114: the turnover in memory. Seniors leave, classes advance, freshmen arrive " +
                          "position for position by prestige, season two plays on the result; season one is the " +
                          "standalone season bit for bit, and a negative control per rule ==");
        var pass = true;
        var assertions = 0;

        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static string GamesDigest(SeasonRunOutcome run) => RatingSha(string.Concat(run.Results.Select((x, i) =>
            $"{i}|{x.HomeId}|{x.AwayId}|{x.HomeScore}|{x.AwayScore}|{run.PossessionCounts[i]}\n")));
        static string PageDigest(SeasonRunOutcome run) => RatingSha(
            GamesDigest(run) + run.Fingerprint + run.DatedFingerprint + run.EventGamesFingerprint +
            run.NonConferenceDates.DatedFingerprint + run.ConferenceTournamentFingerprint + run.BuyGamesFingerprint);
        static bool SameRow(PoolPlayer a, PoolPlayer b)
            => a.Player.Name == b.Player.Name && a.Pos == b.Pos && a.Role == b.Role
               && a.DefensivePlane == b.DefensivePlane && a.OffensiveRole == b.OffensiveRole
               && a.ScoutRank == b.ScoutRank && a.Class == b.Class && a.PoolId == b.PoolId
               && a.Ratings.Count == b.Ratings.Count && a.Ratings.All(kv => b.Ratings.TryGetValue(kv.Key, out var v) && v == kv.Value);
        static string Blame(InvalidOperationException? ex) => ex?.Message is { } m ? m[..Math.Min(90, m.Length)] : "(no refusal)";
        static InvalidOperationException? Refusal(Action a)
        {
            try { a(); return null; } catch (InvalidOperationException ex) { return ex; }
        }

        try
        {
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var seedTwo = SeasonTwoSeed(TurnoverCheckSeed);
            var n = stock.Schools.Count;
            var posList = new[] { "G", "W", "B" };

            var one = RunSeasonCore(stock, TurnoverCheckSeed, configPath, verbose: false);
            var pool1 = one.Divvy.Pool;
            var t = RunTurnover(stock, one.Divvy, seedTwo);
            var pool2 = t.SeasonTwo.Pool;
            var two = RunSeasonCore(stock, seedTwo, configPath, verbose: false, rostersInHand: t.SeasonTwo);

            // ── C1: every senior left and nobody else did ───────────────────────
            {
                var seniors = pool1.Count(p => p.Class == ClassYear.Sr);
                Check("C1a: departures = season-one seniors", t.Lines.Sum(l => l.Left) == seniors, $"{seniors}");
                var seniorPlayers = new HashSet<Player>(pool1.Where(p => p.Class == ClassYear.Sr).Select(p => p.Player), ReferenceEqualityComparer.Instance);
                Check("C1b: no season-one senior appears in season two", !pool2.Any(p => seniorPlayers.Contains(p.Player)));
                var perSchool = stock.Schools.All(s =>
                {
                    var r1 = one.Divvy.Rosters[s.Id].Select(pid => pool1[pid]).Where(p => p.Class == ClassYear.Sr).ToList();
                    var l = t.Lines.Single(x => x.SchoolId == s.Id);
                    return l.LeftG == r1.Count(p => p.Pos == "G") && l.LeftW == r1.Count(p => p.Pos == "W") && l.LeftB == r1.Count(p => p.Pos == "B");
                });
                Check("C1c: per school, departures by position = that school's seniors by position", perSchool);
            }

            // ── C2: every returner is the same man ──────────────────────────────
            {
                var ok = true; var count = 0;
                foreach (var s in stock.Schools)
                {
                    var ret1 = one.Divvy.Rosters[s.Id].Select(pid => pool1[pid]).Where(p => p.Class != ClassYear.Sr).ToList();
                    var r2 = t.SeasonTwo.Rosters[s.Id].Select(pid => pool2[pid]).ToList();
                    for (var i = 0; i < ret1.Count; i++)
                    {
                        var a = ret1[i]; var b = r2[i]; count++;
                        ok &= ReferenceEquals(a.Player, b.Player) && ReferenceEquals(a.Ratings, b.Ratings)
                              && a.Role == b.Role && a.DefensivePlane == b.DefensivePlane
                              && a.OffensiveRole == b.OffensiveRole && a.ScoutRank == b.ScoutRank && a.Pos == b.Pos
                              && b.Class == NextClass(a.Class);
                    }
                }
                Check("C2: every non-senior has exactly one season-two row — same Player and Ratings objects, same Role, " +
                      "DefensivePlane, OffensiveRole, ScoutRank, class +1, same school, same order among returners",
                      ok && count == t.ReturnerCount && t.ReturnerCount == pool1.Count(p => p.Class != ClassYear.Sr),
                      $"{count} returners");
            }

            // ── C3: freshmen are exactly the vacancies, position for position ──
            {
                var fresh = pool2.Skip(t.ReturnerCount).ToList();
                Check("C3a: freshman pool size = departures", fresh.Count == t.Lines.Sum(l => l.Left), $"{fresh.Count}");
                Check("C3b: freshman G/W/B = guard/wing/big vacancies",
                      posList.All(p => fresh.Count(f => f.Pos == p) == t.Lines.Sum(l => p == "G" ? l.LeftG : p == "W" ? l.LeftW : l.LeftB)),
                      $"{fresh.Count(f => f.Pos == "G")}/{fresh.Count(f => f.Pos == "W")}/{fresh.Count(f => f.Pos == "B")}");
                Check("C3c: per school, arrivals by position = departures by position",
                      t.Lines.All(l => l.ArrivedG == l.LeftG && l.ArrivedW == l.LeftW && l.ArrivedB == l.LeftB));
                var drafted = t.SeasonTwo.Rosters.Values.SelectMany(r => r).ToList();
                Check("C3d: every freshman drafted, every person on exactly one roster",
                      drafted.Count == pool2.Count && drafted.Distinct().Count() == pool2.Count);
                Check("C3e: every roster 13 at 5/4/4 after the turnover",
                      t.SeasonTwo.Rosters.Values.All(r => r.Count == RosterShape.Size
                          && r.Count(pid => pool2[pid].Pos == "G") == RosterShape.Guards
                          && r.Count(pid => pool2[pid].Pos == "W") == RosterShape.Wings
                          && r.Count(pid => pool2[pid].Pos == "B") == RosterShape.Bigs));
                Check("C3f: every school still covers lead guard and wing defender",
                      t.SeasonTwo.Rosters.Values.All(r => r.Any(pid => GenLeadRoles.Contains(pool2[pid].Role))
                                                        && r.Any(pid => pool2[pid].Role == GenWingDefenderRole)),
                      $"{t.SchoolsNeedingLead} school(s) needed a lead from the class, {t.SchoolsNeedingTdw} a wing defender");
                Check("C3g: the season-two pool is dense 0..P-1 with P unchanged",
                      pool2.Count == pool1.Count && pool2.Select((p, i) => p.PoolId == i).All(x => x));
            }

            // ── C4: classes rolled, no draw involved ────────────────────────────
            {
                int C1(ClassYear c) => pool1.Count(p => p.Class == c);
                int C2(ClassYear c) => pool2.Count(p => p.Class == c);
                Check("C4a: in total Sr2 = Jr1, Jr2 = So1, So2 = Fr1, Fr2 = departures",
                      C2(ClassYear.Sr) == C1(ClassYear.Jr) && C2(ClassYear.Jr) == C1(ClassYear.So)
                      && C2(ClassYear.So) == C1(ClassYear.Fr) && C2(ClassYear.Fr) == C1(ClassYear.Sr),
                      $"Fr {C2(ClassYear.Fr)} So {C2(ClassYear.So)} Jr {C2(ClassYear.Jr)} Sr {C2(ClassYear.Sr)}");
                Check("C4b: per school, the same", stock.Schools.All(s =>
                {
                    var a = one.Divvy.Rosters[s.Id].Select(pid => pool1[pid].Class).ToList();
                    var b = t.SeasonTwo.Rosters[s.Id].Select(pid => pool2[pid].Class).ToList();
                    return b.Count(c => c == ClassYear.Sr) == a.Count(c => c == ClassYear.Jr)
                        && b.Count(c => c == ClassYear.Jr) == a.Count(c => c == ClassYear.So)
                        && b.Count(c => c == ClassYear.So) == a.Count(c => c == ClassYear.Fr);
                }));
                Check("C4c: every freshman is Fr", pool2.Skip(t.ReturnerCount).All(p => p.Class == ClassYear.Fr));
            }

            // ── C5: the freshman class is the same crop ─────────────────────────
            {
                var F = t.Freshmen.Count;
                var gG = t.Freshmen.Count(p => p.Pos == "G"); var gW = t.Freshmen.Count(p => p.Pos == "W");
                foreach (var pos in posList)
                {
                    var b = pool1.Where(p => p.Pos == pos).Select(p => p.ScoutRank).ToList();
                    var f = t.Freshmen.Where(p => p.Pos == pos).Select(p => p.ScoutRank).ToList();
                    var se = SampleSd(b) / Math.Sqrt(f.Count);
                    var z = (f.Average() - b.Average()) / se;
                    var ratio = SampleSd(f) / SampleSd(b);
                    Check($"C5a: {pos} freshman rank mean within 3.5 SE of the starting pool's", Math.Abs(z) <= 3.5,
                          Inv($"{f.Average():F2} vs {b.Average():F2}, {z:+0.00;-0.00} SE (n={f.Count})"));
                    Check($"C5b: {pos} freshman rank sd within ±10% of the starting pool's", Math.Abs(ratio - 1) <= 0.10,
                          Inv($"{SampleSd(f):F2} vs {SampleSd(b):F2}, ratio {ratio:F3}"));
                }
                // NEGATIVE CONTROL: the same stream, drawn at a HIGHER scholarship line — a better
                // crop, same quotas. The gate prints how far each line moves the class.
                var cohortSeed = unchecked((int)(seedTwo ^ DivvyCohortSeedXor));
                Dictionary<string, double> ControlZ(double lineOffset)
                {
                    var rigged = new List<PlayerGenPass3Live.LivePlayer>(F);
                    for (var m = 2 * F; rigged.Count < F; m *= 2)
                    {
                        rigged.Clear();
                        foreach (var lp in PlayerGenPass3Live.BuildCohort(cohortSeed, m))
                        {
                            if (lp.Result.Rscore < PlayerGenPass3.R_LINE + lineOffset) continue;
                            rigged.Add(lp);
                            if (rigged.Count == F) break;
                        }
                    }
                    var order = Enumerable.Range(0, F).OrderBy(i => rigged[i].Result.DPlane).ThenBy(i => i).ToArray();
                    var rz = new Dictionary<string, double>();
                    foreach (var pos in posList)
                    {
                        var ks = Enumerable.Range(0, F).Where(k => (k < gG ? "G" : k < gG + gW ? "W" : "B") == pos).ToList();
                        var ranks = ks.Select(k =>
                        {
                            var v = new Dictionary<string, int>(rigged[order[k]].Result.Card, StringComparer.Ordinal);
                            DeriveAndStampTendencies(v);
                            return DivvyScoutRank(v, pos);
                        }).ToList();
                        var b = pool1.Where(p => p.Pos == pos).Select(p => p.ScoutRank).ToList();
                        rz[pos] = (ranks.Average() - b.Average()) / (SampleSd(b) / Math.Sqrt(ranks.Count));
                    }
                    return rz;
                }
                foreach (var off in new[] { 5.0, 10.0, 20.0 })
                {
                    var z = ControlZ(off);
                    Console.WriteLine(Inv($"    a class drawn at R_LINE + {off:F0} moves the mean (printed, not asserted): ") +
                                      string.Join(", ", posList.Select(p => Inv($"{p} {z[p]:+0.0;-0.0} SE"))));
                }
                // ★ THE CONTROL THAT FIRES: the scouts' own pick. Twice the class clears the line;
                //   positions by plane rank at the doubled quotas; within each position the best
                //   gX by scout rank are taken. A class hand-picked by the instrument the bar reads
                //   is a different crop at every position, and the bar must say so.
                var rz2 = new Dictionary<string, double>();
                {
                    var twoF = BuildRecruitedCohort(cohortSeed, 2 * F, 4 * F);
                    var order2 = Enumerable.Range(0, 2 * F).OrderBy(i => twoF[i].Result.DPlane).ThenBy(i => i).ToArray();
                    var ranked = Enumerable.Range(0, 2 * F).Select(k =>
                    {
                        var pos = k < 2 * gG ? "G" : k < 2 * (gG + gW) ? "W" : "B";
                        var v = new Dictionary<string, int>(twoF[order2[k]].Result.Card, StringComparer.Ordinal);
                        DeriveAndStampTendencies(v);
                        return (Pos: pos, Rank: DivvyScoutRank(v, pos));
                    }).ToList();
                    foreach (var pos in posList)
                    {
                        var quota = t.Freshmen.Count(p => p.Pos == pos);
                        var picked = ranked.Where(x => x.Pos == pos).OrderByDescending(x => x.Rank).Take(quota).Select(x => x.Rank).ToList();
                        var b = pool1.Where(p => p.Pos == pos).Select(p => p.ScoutRank).ToList();
                        rz2[pos] = (picked.Average() - b.Average()) / (SampleSd(b) / Math.Sqrt(picked.Count));
                    }
                }
                Check("C5c: ★ NEGATIVE CONTROL — a class hand-picked by scout rank from twice the crop FAILS the mean bar at every position",
                      rz2.Values.All(z => Math.Abs(z) > 3.5),
                      string.Join(", ", posList.Select(p => Inv($"{p} {rz2[p]:+0.0;-0.0} SE"))));
            }

            // ── C6: handed out by prestige, like the divvy ──────────────────────
            {
                Check("C6a: the pick weight is (prestige + 10)^2 for every school in the world",
                      stock.Schools.All(s => DivvyWinnerWeight(s.CurrentPrestige) == Math.Pow(s.CurrentPrestige + 10.0, 2.0)));
                var flat = stock.Schools.Select(s => DivvyWinnerWeight(50)).ToList();
                Check("C6a: set every prestige equal and every weight is equal", flat.Distinct().Count() == 1);
                var raised = stock.Schools.Select(s => DivvyWinnerWeight(s.Id == stock.Schools[0].Id ? 99 : 50)).ToList();
                Check("C6a: raise one school's prestige and only its weight moves",
                      raised[0] > flat[0] && raised.Skip(1).SequenceEqual(flat.Skip(1)));

                (double Gap, double Se, string Table) BandGap(TurnoverResult tr, WorldFile w)
                {
                    var bands = new (int Lo, int Hi)[] { (0, 19), (20, 39), (40, 59), (60, 79), (80, 99) };
                    var lines = new List<string>();
                    var means = new Dictionary<(int, int), (double M, double Sd, int N)>();
                    foreach (var band in bands)
                    {
                        var ids = w.Schools.Where(s => s.CurrentPrestige >= band.Lo && s.CurrentPrestige <= band.Hi).Select(s => s.Id);
                        var ranks = ids.SelectMany(id => tr.SeasonTwo.Rosters[id].Skip(tr.SeasonTwo.Rosters[id].Count
                                                   - tr.Lines.Single(l => l.SchoolId == id).Arrived))
                                       .Select(pid => tr.SeasonTwo.Pool[pid].ScoutRank).ToList();
                        if (ranks.Count < 2) continue;
                        means[band] = (ranks.Average(), SampleSd(ranks), ranks.Count);
                        lines.Add(Inv($"{band.Lo}-{band.Hi}: {ranks.Average():F1} (n={ranks.Count})"));
                    }
                    var top = means[(80, 99)]; var bot = means[(0, 19)];
                    var se = Math.Sqrt(top.Sd * top.Sd / top.N + bot.Sd * bot.Sd / bot.N);
                    return (top.M - bot.M, se, string.Join("  ", lines));
                }
                var (gap, se, table) = BandGap(t, stock);
                Console.WriteLine("    freshman rank by prestige band at the stock seed (printed, not asserted): " + table);
                Check("C6b: top prestige band's mean freshman rank exceeds the bottom band's by at least 4 SE",
                      gap >= 4 * se, Inv($"gap {gap:F1} = {gap / se:F1} SE"));
                for (var k = 1; k <= 4; k++)
                {
                    var tk = RunTurnover(stock, one.Divvy, seedTwo + k);
                    Console.WriteLine($"    seed {seedTwo + k} (diagnostic): " + BandGap(tk, stock).Table);
                }
                // NEGATIVE CONTROL: every school's draft weight set equal (one prestige for all),
                // through the real path — the same rosters, the same seed, flat odds.
                var flatWorld = new WorldFile
                {
                    SchemaVersion = stock.SchemaVersion, Kind = stock.Kind, EraLabel = stock.EraLabel, Division = stock.Division,
                    WorldSeed = stock.WorldSeed, Tiers = stock.Tiers, Conferences = stock.Conferences, Places = stock.Places,
                    Events = stock.Events, Schools = stock.Schools.Select(s => s with { CurrentPrestige = 50 }).ToList(),
                };
                var tf = RunTurnover(flatWorld, one.Divvy, seedTwo);
                var (flatGap, flatSe, _) = BandGap(tf, stock);   // bands by the REAL prestige, drafted at flat odds
                Check("C6c: ★ NEGATIVE CONTROL — with every weight equal the top-minus-bottom gap is less than a quarter of the weighted gap",
                      flatGap < gap / 4, Inv($"flat gap {flatGap:F1} ({flatGap / flatSe:F1} SE) vs weighted {gap:F1}"));
            }

            // ── C7: deterministic, and the season-two seed matters ──────────────
            {
                var again = RunTurnover(stock, one.Divvy, seedTwo);
                Check("C7a: the same two seeds give identical season-two rosters and pool rows",
                      again.SeasonTwo.Rosters.All(kv => kv.Value.SequenceEqual(t.SeasonTwo.Rosters[kv.Key]))
                      && again.SeasonTwo.Pool.Zip(pool2).All(x => SameRow(x.First, x.Second)));
                var twoAgain = RunSeasonCore(stock, seedTwo, configPath, verbose: false, rostersInHand: again.SeasonTwo);
                Check("C7b: season two replayed gives an identical page digest (games, scores, possessions, seven fingerprints)",
                      PageDigest(twoAgain) == PageDigest(two), PageDigest(two)[..16]);
                var other = RunTurnover(stock, one.Divvy, seedTwo + 1);
                var freshDiffer = other.Freshmen.Zip(t.Freshmen).Count(x => x.First.ScoutRank != x.Second.ScoutRank);
                var rostersDiffer = other.SeasonTwo.Rosters.Count(kv => !kv.Value.SequenceEqual(t.SeasonTwo.Rosters[kv.Key]));
                Check("C7c: a different season-two seed changes the freshmen and the draft",
                      freshDiffer > t.Freshmen.Count / 2 && rostersDiffer > n / 2,
                      $"{freshDiffer} of {t.Freshmen.Count} freshman rows differ, {rostersDiffer} of {n} rosters differ");
                Check("C7d: ...and the returners' classes are unmoved",
                      other.SeasonTwo.Pool.Take(t.ReturnerCount).Zip(pool2.Take(t.ReturnerCount)).All(x => SameRow(x.First, x.Second)));
            }

            // ── C8: ★ THE FINGERPRINT WALL ──────────────────────────────────────
            {
                var prefix = one.ConferenceGameCount + one.TournamentGameCount;
                var resultsFp = SeasonFingerprint(one.Results.Take(prefix).ToList(), one.PossessionCounts.Take(prefix).ToList());
                Check("C8a: #1 conference schedule UNMOVED", one.Fingerprint == MatchGoldenConferenceFp);
                Check("C8b: #2 conference dated UNMOVED", one.DatedFingerprint == MatchGoldenDatedFp);
                Check("C8c: #3 event games UNMOVED", one.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C8d: #4 results+possessions UNMOVED over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C8e: #5 non-conference dated UNMOVED", one.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C8f: #6 conference tournaments UNMOVED", one.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C8g: #7 buy games UNMOVED", one.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                var digest = GamesDigest(one);
                Check("C8h: ★ every one of season one's games identical to the S118.1 capture — teams, scores, possessions",
                      digest == RatingGoldenPreS112GameDigest, $"{one.PlayedGames.Count} games, {digest[..16]}");
                var standalone = RunDivvyDraft(stock, TurnoverCheckSeed);
                Check("C8i: ★ the bootstrap divvy through the generalized loop is byte-identical — every roster, every pick, every pool row",
                      standalone.Rosters.All(kv => kv.Value.SequenceEqual(one.Divvy.Rosters[kv.Key]))
                      && standalone.Picks.Count == one.Divvy.Picks.Count
                      && standalone.Picks.Zip(one.Divvy.Picks).All(x => x.First == x.Second)
                      && standalone.NoiseScale == one.Divvy.NoiseScale
                      && standalone.Pool.Zip(pool1).All(x => x.First.PoolId == x.Second.PoolId && x.First.Pos == x.Second.Pos
                                                           && x.First.Role == x.Second.Role && x.First.ScoutRank == x.Second.ScoutRank
                                                           && x.First.Class == x.Second.Class));
            }

            // ── C9: season two plays and reconciles ─────────────────────────────
            {
                Check("C9a: season two played every scheduled conference game plus its tournaments and buy games",
                      two.PlayedGames.Count == two.Results.Count && two.Results.Count >= two.Schedule.Count && two.Ties == 0,
                      $"{two.PlayedGames.Count} games");
                Check("C9b: every school's record derived, wins = losses nationally",
                      two.Wins.Count == n && two.Losses.Count == n && two.Wins.Values.Sum() == two.Losses.Values.Sum()
                      && two.Wins.Values.Sum() == two.Results.Count);
                var rex = Refusal(() =>
                {
                    var r = ComputeSeasonRatings(BuildSeasonRecord(RecordInputsFromRun(two), stock.Schools.Select(s => s.Id)));
                    if (r.Ranked.Count + r.Excluded.Count != n) throw new InvalidOperationException("rating did not cover the league");
                });
                Check("C9c: the S112 record built and the rating computed without refusal", rex is null, Blame(rex));
                Check("C9d: season two's rosters are the turned-over rosters (same objects)", ReferenceEquals(two.Divvy, t.SeasonTwo));
            }

            // ── C10: the runner without rosters in hand is unchanged ────────────
            {
                Check("C10: RunSeasonCore with the new parameter omitted produces the S118 digest (C8h said the other way)",
                      GamesDigest(one) == RatingGoldenPreS112GameDigest);
            }

            // ── C11: negative controls, each firing the rule it names ───────────
            {
                DivvyResult With(List<PoolPlayer>? pool = null, Dictionary<int, List<int>>? rosters = null) => new()
                {
                    Pool = pool ?? pool2, Rosters = rosters ?? t.SeasonTwo.Rosters, Picks = t.SeasonTwo.Picks,
                    NoiseScale = t.SeasonTwo.NoiseScale, MinSlackLead = t.SeasonTwo.MinSlackLead, MinSlackTdw = t.SeasonTwo.MinSlackTdw,
                };
                TurnoverResult Tamper(DivvyResult d) => new()
                {
                    SeasonTwo = d, Freshmen = t.Freshmen, ReturnerCount = t.ReturnerCount, Lines = t.Lines,
                    SchoolsNeedingLead = t.SchoolsNeedingLead, SchoolsNeedingTdw = t.SchoolsNeedingTdw,
                    FreshmanLeadTarget = t.FreshmanLeadTarget, FreshmanTdwTarget = t.FreshmanTdwTarget,
                };
                var school = stock.Schools.First(s => t.Lines.Single(l => l.SchoolId == s.Id).Left > 0);
                var roster = t.SeasonTwo.Rosters[school.Id];
                var lastFresh = roster[^1];
                var senior = one.Divvy.Rosters[school.Id].Select(pid => pool1[pid]).First(p => p.Class == ClassYear.Sr);

                var kept = pool2.ToList(); kept[lastFresh] = senior with { PoolId = lastFresh };
                var ex1 = Refusal(() => ValidateTurnover(stock, one.Divvy, Tamper(With(pool: kept))));
                Check("C11a: ★ a kept senior is REFUSED by name", ex1?.Message.Contains("a senior stayed") == true, Blame(ex1));

                var soph = pool2.ToList(); soph[lastFresh] = pool2[lastFresh] with { Class = ClassYear.So };
                var ex2 = Refusal(() => ValidateTurnover(stock, one.Divvy, Tamper(With(pool: soph))));
                Check("C11b: ★ a freshman labelled So is REFUSED by name", ex2?.Message.Contains("a freshman is not Fr") == true, Blame(ex2));

                var bootstrapped = pool2.Select(p => p.PoolId >= t.ReturnerCount ? p with { Class = InitialPlayerClass(seedTwo, p.PoolId) } : p).ToList();
                var wrong = bootstrapped.Skip(t.ReturnerCount).Count(p => p.Class != ClassYear.Fr);
                var ex3 = Refusal(() => ValidateTurnover(stock, one.Divvy, Tamper(With(pool: bootstrapped))));
                Check("C11c: ★ freshmen classed through the bootstrap draw are REFUSED (the draw and \"all Fr\" disagree)",
                      wrong > 0 && ex3?.Message.Contains("a freshman is not Fr") == true, $"{wrong} of {t.Freshmen.Count} ids classed wrong; {Blame(ex3)}");

                var shortRosters = t.SeasonTwo.Rosters.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
                shortRosters[school.Id].RemoveAt(shortRosters[school.Id].Count - 1);
                var ex4 = Refusal(() => ValidateTurnover(stock, one.Divvy, Tamper(With(rosters: shortRosters))));
                Check("C11d: ★ a school handed one freshman too few is REFUSED by name", ex4?.Message.Contains($"not {RosterShape.Size}") == true, Blame(ex4));

                var noLeads = t.Freshmen.Select(p => GenLeadRoles.Contains(p.Role) ? p with { Role = GenGuardRoles[2] } : p).ToList();
                var ex5 = Refusal(() => ValidateFreshmanClass(noLeads, Math.Max(1, t.SchoolsNeedingLead), t.SchoolsNeedingTdw));
                Check("C11e: ★ a freshman class whose lead supply is below the obligation is REFUSED at preflight",
                      ex5?.Message.Contains("TURNOVER INFEASIBLE") == true, Blame(ex5));

                var refusal = SeasonsRefuseHistory(new[] { "seasons", "worlds/stock-d1.world.json", "20260720", "--history", "x.charm" });
                Check("C11f: ★ --history on the stacked command is REFUSED by name", refusal?.Contains("--history") == true,
                      refusal?[..Math.Min(70, refusal.Length)] ?? "(accepted)");
                var ex7 = Refusal(() => SeasonTwoSeed(long.MaxValue));
                Check("C11g: ★ a season-one seed at the type's ceiling is REFUSED rather than wrapped", ex7 is not null, Blame(ex7));
            }
        }
        catch (InvalidOperationException ex)
        {
            Check("Phase 104 ran to completion", false, ex.Message);
        }

        Console.WriteLine($"  Phase 104: {assertions} assertions — {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
