using Charm.Engine;
using Charm.History;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Charm.Harness;

// ============================================================================
//  Phase 112 — S121: THE OFFSEASON CAMP.
//
//  What must be proven:
//    C1  oracle parity — the K5 streams, the cohort descriptors and the potential roll,
//        the work ethic, the allocation, the camp odds, whole camps, the free-throw delta,
//        config and points refusals, a permuted AttributeOrder, and the archetype careers:
//        EXACT on every discrete outcome, within 1e-12 on every double intermediate;
//        free throws the one documented exception (tanh, CONVENTIONS §2), excused only at
//        a half-boundary. Negative controls: a lowest-promise allocator, a camp that keeps
//        progress past 99.
//    C1b funded athleticism is never below unfunded — the pure per-attribute rule, called
//        directly on identical draws over the whole grid.
//    C2  the rulings hold on every returner of a stock career's season two (control: a camp
//        that lets athleticism slip).
//    C3  camp outcomes and potential tiers within 4 SE of each man's OWN odds (control:
//        tiers rolled without the body-first rule must be caught).
//    C4  the scouting file round-trips exactly and refuses by name; the logs carry no potential.
//    C5  the career and the stacked command camp identically from the same minutes; a new
//        seed gives new camps; order-free.
//    C5b a four-season career: camps 1, 2, 3 in order, the rotation fires, and a copy resumed
//        from disk camps exactly as the original (control: zeroed streaks on disk change camps).
//    C6  season one unmoved.
//    C7  page-only: concentration, per-40 change, archetypes, busts, drift.
// ============================================================================

internal static partial class Program
{
    private const long DevelopmentCheckSeed = 20260720;

    /// <summary>Phase 112's check reporter, passed to its helpers (the detail is optional).</summary>
    private delegate void DevCheck(string name, bool ok, string detail = "");

    private static bool Phase112DevelopmentCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 112 — S121: the offseason camp. Oracle parity exact on every outcome, funded athleticism never " +
                          "below unfunded, the rulings on every returner, the odds per man, the scouting file, career = stacked, " +
                          "three camps in order with the rotation and the reload, season one unmoved ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static string Inv(FormattableString f) => FormattableString.Invariant(f);

        var cfg = DevelopmentConfig.Load(configPath);
        var scratch = Path.Combine(Path.GetTempPath(), "charm-s121-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            DevC1Parity(configPath, cfg, Check);
            DevC1bAthleticism(cfg, Check);

            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var mte = LoadWorld(WorldPath("fixture-mte.world.json"));
            var seedTwo = SeasonTwoSeed(DevelopmentCheckSeed);
            SeasonRunOutcome Career(WorldFile w, long seed, string path)
            {
                using var store = HistoryStore.Open(path, WorldFingerprint(w));
                return RunSeasonCore(w, seed, configPath, verbose: false, store, retainGameLog: true);
            }
            GameLogV1 ReadLog(WorldFile w, string path, long season)
            {
                using var store = HistoryStore.Open(path, WorldFingerprint(w));
                return GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(path, season),
                    new GameLogBindings(store.HistoryId, store.WorldFingerprint, season));
            }
            IReadOnlyList<ScoutingRecord> ReadScout(WorldFile w, string path, long season)
            {
                using var store = HistoryStore.Open(path, WorldFingerprint(w));
                return ScoutingFile.Read(path, new ScoutingBindings(store.HistoryId, store.WorldFingerprint, season));
            }
            static string Digest(SeasonRunOutcome run) => RatingSha(string.Concat(run.Results.Select((x, i) =>
                $"{i}|{x.HomeId}|{x.AwayId}|{x.HomeScore}|{x.AwayScore}|{run.PossessionCounts[i]}\n")));

            // ═══ The stock career: two seasons ═══════════════════════════════════
            var stockPath = Path.Combine(scratch, "stock", "career.json");
            var one = Career(stock, DevelopmentCheckSeed, stockPath);
            Check("C6a: ★ season one of a career plays the S120 capture game for game — nothing in the camp reaches it",
                  Digest(one) == RatingGoldenPreS112GameDigest, $"{one.PlayedGames.Count} games");
            var two = Career(stock, seedTwo, stockPath);
            var log1 = ReadLog(stock, stockPath, 1);
            var log2 = ReadLog(stock, stockPath, 2);
            var scout1 = ReadScout(stock, stockPath, 1);
            var scout2 = ReadScout(stock, stockPath, 2);
            var dev = two.Development!;
            var R = dev.ReturnerCount;

            // ── C4: the scouting file round trip ─────────────────────────────────
            {
                bool RoundTrips(DivvyResult d, DevState[] states, IReadOnlyList<ScoutingRecord> recs)
                {
                    var by = recs.ToDictionary(r => r.Person);
                    return recs.Count == d.Pool.Count && Enumerable.Range(0, d.Pool.Count).All(i =>
                        by.TryGetValue(d.PersonIds![i], out var r) && DevStateFromRecord(r).SameAs(states[i]));
                }
                Check("C4a: ★ season one's scouting file holds every man of the bootstrap pool, bit for bit the state the season rolled",
                      RoundTrips(one.Divvy, one.DevStates!, scout1), $"{scout1.Count} men");
                Check("C4b: ★ season two's scouting file holds every man of season two, bit for bit the state after camp (hidden progress exact)",
                      RoundTrips(two.Divvy, two.DevStates!, scout2), $"{scout2.Count} men");
                var r1 = log1.RosterV2(); var r2 = log2.RosterV2();
                var seniors = r1.Where(e => e.Class == 3).Select(e => e.PersonId).ToHashSet();
                Check("C4c: every senior is gone from season two's scouting file and every man of season two's roster is in it",
                      !scout2.Any(r => seniors.Contains(r.Person)) && r2.Select(e => e.PersonId).ToHashSet().SetEquals(scout2.Select(r => r.Person)),
                      $"{seniors.Count} seniors absent");
                var forbidden = new[] { "Tier", "Potential", "WorkEthic", "DevSeed", "Progress", "Streak" };
                var leaks = new[] { typeof(RosterEntryV2), typeof(PerGameStatRowV1), typeof(GameBlockFactsV1) }
                    .SelectMany(t => t.GetProperties().Select(p => t.Name + "." + p.Name))
                    .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal))).ToList();
                Check("C4d: the season log's roster, block and row types carry no potential, work ethic, progress or streak (A6)",
                      leaks.Count == 0, leaks.Count == 0 ? "none" : string.Join(", ", leaks));
            }

            // ── C2: the rulings, on every returner of season two ─────────────────
            {
                var card1 = Enumerable.Range(0, one.Divvy.Pool.Count).ToDictionary(i => one.Divvy.PersonIds![i], i => one.Divvy.Pool[i].Ratings);
                var prevState = scout1.ToDictionary(r => r.Person, DevStateFromRecord);
                string? Violation(int i, IReadOnlyDictionary<string, int> after)
                {
                    var id = two.Divvy.PersonIds![i];
                    var before = card1[id];
                    var camp = dev.Camps[i]!;
                    var st0 = prevState[id];
                    var st1 = dev.States[i];
                    foreach (var k in DevelopmentConfig.AthleticAttributes)
                        if (after[k] < before[k]) return $"{k} went down";
                    if (after["BasketballIQ"] < before["BasketballIQ"] || after["Discipline"] < before["Discipline"]) return "IQ or Discipline went down";
                    if (after["BasketballIQ"] > Math.Max(before["BasketballIQ"], Math.Min(99, st0.ArrivalIq + cfg.IqCapAboveArrival))) return "IQ above arrival + cap";
                    if (after["Discipline"] > Math.Max(before["Discipline"], Math.Min(99, st0.ArrivalDiscipline + cfg.DisciplineCapAboveArrival))) return "Discipline above arrival + cap";
                    if (after["Hustle"] != before["Hustle"]) return "Hustle moved";
                    for (var c = 0; c < DevFundedCount; c++)
                    {
                        var k = DevFunded[c];
                        if (camp.Points[c] > 0 && after[k] < before[k]) return $"funded {k} fell";
                        if (camp.Points[c] == 0 && DevGroupOf(c) != DevGroup.Athleticism && after[k] < before[k] - 1) return $"unfunded {k} fell by more than one";
                        if (st1.Streaks[c] != (camp.Points[c] > 0 ? st0.Streaks[c] + 1 : 0)) return $"{k}'s streak is not last summer's + 1 / 0";
                    }
                    var dh = after["Height"] - before["Height"]; var dw = after["Wingspan"] - before["Wingspan"];
                    var wantH = camp.Spurt ? Math.Min(99, before["Height"] + cfg.HeightSpurtRating) - before["Height"] : 0;
                    var wantW = camp.Spurt ? Math.Min(99, before["Wingspan"] + cfg.HeightSpurtWingspan) - before["Wingspan"] : 0;
                    if (dh != wantH || dw != wantW) return "height or wingspan moved other than by a whole spurt";
                    if (st1.Progress.Any(p => p < 0 || p >= 1)) return "hidden progress outside [0, 1)";
                    return null;
                }
                var bad = Enumerable.Range(0, R).Select(i => (i, v: Violation(i, two.Divvy.Pool[i].Ratings))).Where(x => x.v is not null).ToList();
                Check("C2a: ★ every returner — athleticism, IQ and Discipline never down; IQ and Discipline never above arrival + 8; Hustle " +
                      "unchanged; a funded attribute never fell; an unfunded skill or body attribute fell at most one; height and wingspan " +
                      "only by whole spurts; every streak last summer's + 1 where funded, else 0; every progress in [0, 1)",
                      bad.Count == 0, bad.Count == 0 ? $"{R} returners" : $"{bad.Count} violations, first #{bad[0].i}: {bad[0].v}");
                var fr = Enumerable.Range(R, two.Divvy.Pool.Count - R).ToList();
                Check("C2b: every freshman is uncamped (arrives as drafted), with tiers, a work ethic, no streak and no progress",
                      fr.All(i => dev.Camps[i] is null && dev.States[i].WorkEthic is >= 1 and <= 99
                                  && dev.States[i].Tiers.All(t => t is >= 0 and <= 4)
                                  && dev.States[i].Streaks.All(s => s == 0) && dev.States[i].Progress.All(p => p == 0)),
                      $"{fr.Count} freshmen");
                var rebuilt = Enumerable.Range(0, R).All(i =>
                {
                    var row = two.Divvy.Pool[i];
                    var card = new Dictionary<string, int>(row.Ratings, StringComparer.Ordinal);
                    DeriveAndStampTendencies(card);
                    return GenTendencies.All(k => card[k] == row.Ratings[k])
                           && RatingsOf(row.Player).SequenceEqual(RetentionRatingOrder.Select(k => (short)row.Ratings[k]));
                });
                Check("C2d: every developed man is his developed card — the five shot-diet tendencies re-derived from it, the Player built from it",
                      rebuilt, $"{R} returners");
                // Negative control: the same predicate on a card where one athletic rating slipped.
                var victim = Enumerable.Range(0, R).First(i => two.Divvy.Pool[i].Ratings["Quickness"] > 0);
                var slipped = new Dictionary<string, int>(two.Divvy.Pool[victim].Ratings, StringComparer.Ordinal);
                slipped["Quickness"] = card1[two.Divvy.PersonIds![victim]]["Quickness"] - 1;
                Check("C2c: ★ NEGATIVE CONTROL — a camp that lets athleticism slip is caught by C2a's predicate",
                      Violation(victim, slipped) is not null, Violation(victim, slipped) ?? "(not caught)");
            }

            // ── C3: the odds, per man ────────────────────────────────────────────
            {
                var outcomes = new[] { "bad", "normal", "good", "breakout" };
                var camps = Enumerable.Range(0, R).Select(i => dev.Camps[i]!).ToList();
                var worst = 0.0;
                var all = true;
                for (var o = 0; o < 4; o++)
                {
                    var e = camps.Sum(c => c.Odds[o]);
                    var se = Math.Sqrt(camps.Sum(c => c.Odds[o] * (1 - c.Odds[o])));
                    var obs = camps.Count(c => c.Outcome == o);
                    var z = (obs - e) / se;
                    worst = Math.Max(worst, Math.Abs(z));
                    all &= Math.Abs(z) <= 4;
                    Console.WriteLine(Inv($"    (page) camps {outcomes[o],-9} observed {obs,5}  expected {e,8:F1}  z {z,6:F2}"));
                }
                Check("C3a: ★ every camp outcome within 4 SE of the sum of each returner's OWN tilted odds", all, Inv($"worst |z| {worst:F2} over {R} camps"));

                var (states, rolls, desc) = DevBootstrap(one.Divvy, DevelopmentCheckSeed, cfg);
                Check("C3b: the bootstrap roll recomputed is the roll the career kept (same seed, same pool)",
                      Enumerable.Range(0, states.Length).All(i => states[i].SameAs(one.DevStates![i])));
                var (fails, worstZ, tests) = DevTierTests(one.Divvy, desc, states, cfg);
                Check("C3c: ★ every skill tier count within 4 SE of the sum of each man's exact tier distribution (his shift odds, " +
                      "the player-wide odds, the offset odds, his skill shifts); every body and athletic tier against BodyTierOdds",
                      fails == 0, Inv($"{tests} tests, worst |z| {worstZ:F2}"));
                var noRule = DevelopmentConfig.Load(configPath);
                noRule.BodyFirstNotchDown = 0.0;
                var (statesNo, _, _) = DevBootstrap(one.Divvy, DevelopmentCheckSeed, noRule);
                var (failsNo, worstNo, _) = DevTierTests(one.Divvy, desc, statesNo, cfg);
                Check("C3d: ★ NEGATIVE CONTROL — tiers rolled WITHOUT the body-first rule are caught by C3c (the test can see the bust rule)",
                      failsNo > 0, Inv($"{failsNo} test(s) fail, worst |z| {worstNo:F2}"));
            }

            // ── C5: the career and the stacked command agree ─────────────────────
            {
                var stacked = StackedTurnoverAndCamp(stock, one, DevelopmentCheckSeed, seedTwo, cfg);
                var sd = stacked.Development;
                Check("C5a: ★ the stacked command and the career camp every returner identically — same minutes share bit for bit, " +
                      "same outcome, same points, same card after camp, same hidden state, index for index",
                      sd.ReturnerCount == R && sd.Divvy.Pool.Count == two.Divvy.Pool.Count
                      && Enumerable.Range(0, R).All(i =>
                          BitConverter.DoubleToInt64Bits(sd.Camps[i]!.Share) == BitConverter.DoubleToInt64Bits(dev.Camps[i]!.Share)
                          && sd.Camps[i]!.Outcome == dev.Camps[i]!.Outcome && sd.Camps[i]!.Points.SequenceEqual(dev.Camps[i]!.Points)
                          && RetentionRatingOrder.All(k => sd.Divvy.Pool[i].Ratings[k] == two.Divvy.Pool[i].Ratings[k])
                          && sd.States[i].SameAs(dev.States[i])),
                      $"{R} returners");
                // The minutes, read two ways: the season's own capture vs the log the career reads.
                var logSeconds = new Dictionary<PersonId, long>();
                foreach (var b in log1.Blocks) foreach (var r in b.Rows)
                    logSeconds[r.PersonId] = (logSeconds.TryGetValue(r.PersonId, out var s) ? s : 0) + CareerGameSeconds(r, b.Facts);
                var sameSecs = Enumerable.Range(0, one.Divvy.Pool.Count).All(i =>
                    (one.SecondsByPool.TryGetValue(i, out var a) ? a : 0)
                    == (logSeconds.TryGetValue(one.Divvy.PersonIds![i], out var b) ? b : 0));
                Check("C5b: every man's seconds captured in memory equal his seconds read off the season log (overtime counted)",
                      sameSecs && one.SecondsByPool.Values.Sum() > 0, Inv($"{one.SecondsByPool.Values.Sum() / 60:N0} minutes league-wide"));
                var other = StackedTurnoverAndCamp(stock, one, DevelopmentCheckSeed, seedTwo + 1, cfg).Development;
                var differ = Enumerable.Range(0, R).Count(i => other.Camps[i]!.Outcome != sd.Camps[i]!.Outcome
                                                              || !other.Camps[i]!.Gains.SequenceEqual(sd.Camps[i]!.Gains));
                Check("C5c: a different season seed gives different camps to the same returners",
                      differ > R * 9 / 10, $"{differ} of {R} differ");
                // Order-free: each man camped alone, in reverse, with returner 0 removed.
                var t = stacked.Turnover;
                var free = true;
                for (var i = R - 1; i >= 1; i--)
                {
                    var st = stacked.SeasonOneStates[stacked.PreviousIndex[i]].Clone();
                    var card = new Dictionary<string, int>(t.SeasonTwo.Pool[i].Ratings, StringComparer.Ordinal);
                    var rng = DevCampStream(st.DevSeed, seedTwo);
                    DevRunCamp(st, card, DevCampNumber(t.SeasonTwo.Pool[i].Class), sd.Camps[i]!.Share, rng.NextDouble, cfg);
                    DeriveAndStampTendencies(card);
                    if (!RetentionRatingOrder.All(k => card[k] == sd.Divvy.Pool[i].Ratings[k]) || !st.SameAs(sd.States[i])) { free = false; break; }
                }
                Check("C5d: ★ order-free — every returner camped alone, in reverse order, with returner #0 removed, gets exactly his camp",
                      free, $"{R - 1} men");
            }

            // ═══ C5b — a four-season career on fixture-mte ═══════════════════════
            DevC5bFourSeasons(configPath, cfg, mte, scratch, Career, ReadScout, Check);

            // ═══ C7 — page only ═══════════════════════════════════════════════════
            DevPageOnly(stock, one, two, log1, log2, cfg, configPath);
        }
        catch (Exception ex)
        {
            Check("Phase 112 ran to completion", false, ex.GetType().Name + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
        Console.WriteLine($"  Phase 112 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }

    // ── C1 — the oracle ──────────────────────────────────────────────────────

    private static void DevC1Parity(string configPath, DevelopmentConfig cfg, DevCheck check)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static bool Close12(double a, double b) => Math.Abs(a - b) <= 1e-12;
        var path = Path.Combine(AppContext.BaseDirectory, "tools", "development_golden.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var g = doc.RootElement;
        var perm = DevelopmentConfig.Load(configPath);
        perm.AttributeOrder = g.GetProperty("permutedOrder").EnumerateArray().Select(x => x.GetString()!).ToArray();
        DevelopmentConfig Cfg(JsonElement c) => c.TryGetProperty("order", out var o) && o.GetString() == "permuted" ? perm : cfg;
        static Dictionary<string, int> Card(JsonElement e) => e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
        static int[] Ints(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        static double[] Dbls(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        static Func<double> Feed(double[] us) { var i = 0; return () => us[i++]; }
        var worstD = 0.0;
        void Note(double a, double b) => worstD = Math.Max(worstD, Math.Abs(a - b));

        // Streams (K5)
        {
            var ok = true; var n = 0;
            foreach (var s in g.GetProperty("streams").EnumerateArray())
            {
                n++;
                WorldRng rng;
                if (s.GetProperty("kind").GetString() == "potential")
                {
                    rng = DevPotentialStream(s.GetProperty("seed").GetInt64(), s.GetProperty("index").GetInt32());
                    ok &= rng.NextU64() == ulong.Parse(s.GetProperty("devSeed").GetString()!, CultureInfo.InvariantCulture);
                }
                else rng = DevCampStream(ulong.Parse(s.GetProperty("devSeed").GetString()!, CultureInfo.InvariantCulture), s.GetProperty("seasonSeed").GetInt64());
                ok &= Dbls(s.GetProperty("next")).All(x => BitConverter.DoubleToInt64Bits(rng.NextDouble()) == BitConverter.DoubleToInt64Bits(x));
            }
            check("C1a: the K5 streams — the potential stream's development seed and draws, the camp stream's draws — bit for bit", ok, $"{n} streams");
        }
        // Cohorts and the potential roll
        {
            var ok = true; var men = 0; var minMargin = double.MaxValue;
            foreach (var c in g.GetProperty("cohorts").EnumerateArray())
            {
                var cc = Cfg(c);
                var rows = c.GetProperty("men").EnumerateArray().ToList();
                var cohort = rows.Select(r => new DevCohortMan(r.GetProperty("idx").GetInt32(), r.GetProperty("pos").GetString()!, Card(r.GetProperty("card")))).ToList();
                var desc = DevCohortDescriptors(cohort, cc);
                for (var i = 0; i < rows.Count; i++)
                {
                    men++;
                    var e = rows[i].GetProperty("expect");
                    var d = desc[i];
                    foreach (var (k, v) in new[] { ("body", d.Body), ("skill", d.Skill), ("bz", d.Bz), ("sz", d.Sz), ("gap", d.Gap), ("talent", d.Talent) })
                    { Note(v, e.GetProperty(k).GetDouble()); ok &= Math.Abs(v - e.GetProperty(k).GetDouble()) <= 1e-12; }
                    ok &= d.TalentTop == e.GetProperty("talentTop").GetBoolean();
                    var draws = Dbls(rows[i].GetProperty("draws"));
                    var roll = DevRollPotential(d, cohort[i].Pos, cohort[i].Card, Feed(draws), cc);
                    ok &= roll.RawShift == e.GetProperty("rawShift").GetInt32() && roll.PlayerTier == e.GetProperty("playerTier").GetInt32()
                          && roll.BestSkill == e.GetProperty("best").GetInt32() && roll.Tiers.SequenceEqual(Ints(e.GetProperty("tiers")))
                          && roll.WorkEthic == e.GetProperty("workEthic").GetInt32();
                    minMargin = Math.Min(minMargin, Math.Abs(draws[0] - cc.BodyFirstNotchDown * DevClamp(d.Gap / cc.BodyFirstFullGap, 0, 1)));
                    minMargin = Math.Min(minMargin, Math.Abs(draws[1] - cc.ReadyNotchDown * DevClamp(d.Sz / cc.ReadyFullZ, 0, 1)));
                }
            }
            check("C1b: ★ cohort descriptors within 1e-12 and talentTop exact; the shift, the player-wide tier, the best skill, all 27 tiers " +
                  "and the work ethic EXACT — default and permuted AttributeOrder", ok, Inv($"{men} men; smallest margin to a shift threshold {minMargin:E2}"));
            var weOk = g.GetProperty("workEthic").EnumerateArray()
                        .All(w => DevWorkEthic(Dbls(w.GetProperty("us")), cfg.WorkEthicBetaShape) == w.GetProperty("expect").GetInt32());
            check("C1c: work ethic for fixed draws (Beta by order statistic) exact", weOk);
        }
        // Allocation, with the lowest-promise control
        {
            var ok = true; var n = 0; var lowestCaught = 0;
            foreach (var a in g.GetProperty("allocations").EnumerateArray())
            {
                n++;
                var cc = Cfg(a);
                var tiers = Ints(a.GetProperty("tiers")); var card = Card(a.GetProperty("card"));
                var streaks = Ints(a.GetProperty("streaks")); var campNo = a.GetProperty("campNumber").GetInt32();
                var expect = Ints(a.GetProperty("expect"));
                ok &= DevAllocate(tiers, card, campNo, streaks, cc).SequenceEqual(expect);
                if (!DevAllocate(tiers, card, campNo, streaks, cc, lowestFirstForTest: true).SequenceEqual(expect)) lowestCaught++;
            }
            check("C1d: ★ allocations exact — ties on promise, fewer eligible than priorities, every camp number and one past the list, " +
                  "the rotation, body against skill — default and permuted order", ok, $"{n} cases");
            check("C1e: ★ NEGATIVE CONTROL — an allocator that funds the LOWEST promise first fails the parity", lowestCaught > 0,
                  $"{lowestCaught} of {n} cases reject it");
        }
        // Tilts
        {
            var ok = g.GetProperty("tilts").EnumerateArray().All(t =>
            {
                var got = DevCampOdds(t.GetProperty("workEthic").GetInt32(), t.GetProperty("share").GetDouble(), cfg);
                var e = Dbls(t.GetProperty("expect"));
                for (var i = 0; i < 4; i++) Note(got[i], e[i]);
                return Enumerable.Range(0, 4).All(i => Close12(got[i], e[i]));
            });
            check("C1f: the tilted camp odds within 1e-12 at the four extremes, at zero tilt and between", ok);
        }
        // Whole camps, with the keep-past-99 control
        {
            var ok = true; var n = 0; var ftExcused = new List<string>(); var past99Caught = false;
            foreach (var c in g.GetProperty("camps").EnumerateArray())
            {
                n++;
                var cc = Cfg(c);
                var s = c.GetProperty("state");
                DevState MakeState() => new()
                {
                    DevSeed = 0, WorkEthic = s.GetProperty("workEthic").GetInt32(), ArrivalIq = s.GetProperty("arrivalIq").GetInt32(),
                    ArrivalDiscipline = s.GetProperty("arrivalDiscipline").GetInt32(), Tiers = Ints(s.GetProperty("tiers")),
                    Streaks = Ints(s.GetProperty("streaks")), Progress = Dbls(s.GetProperty("progress")),
                };
                var hand = c.GetProperty("hand").ValueKind == JsonValueKind.Null ? null : Ints(c.GetProperty("hand"));
                var draws = Dbls(c.GetProperty("draws"));
                var st = MakeState(); var card = Card(c.GetProperty("card"));
                var rec = DevRunCamp(st, card, c.GetProperty("campNumber").GetInt32(), c.GetProperty("share").GetDouble(), Feed(draws), cc, hand);
                var e = c.GetProperty("expect");
                var ec = Card(e.GetProperty("card"));
                ok &= rec.Outcome == e.GetProperty("outcome").GetInt32() && rec.Points.SequenceEqual(Ints(e.GetProperty("points")))
                      && rec.Slipped.SequenceEqual(e.GetProperty("slipped").EnumerateArray().Select(x => x.GetBoolean()))
                      && rec.Spurt == e.GetProperty("spurt").GetBoolean() && st.Streaks.SequenceEqual(Ints(e.GetProperty("streaks")));
                var eo = Dbls(e.GetProperty("odds")); var eg = Dbls(e.GetProperty("gains")); var ep = Dbls(e.GetProperty("progress"));
                for (var i = 0; i < 4; i++) { Note(rec.Odds[i], eo[i]); ok &= Close12(rec.Odds[i], eo[i]); }
                for (var i = 0; i < eg.Length; i++) { Note(rec.Gains[i], eg[i]); ok &= Close12(rec.Gains[i], eg[i]); }
                for (var i = 0; i < ep.Length; i++) { Note(st.Progress[i], ep[i]); ok &= Close12(st.Progress[i], ep[i]); }
                foreach (var (k, v) in ec)
                {
                    if (card[k] == v) continue;
                    var raw = e.GetProperty("ftRaw").GetDouble();
                    if (k == "FreeThrow" && Math.Abs(Math.Abs(raw - Math.Floor(raw)) - 0.5) < 1e-9)
                        ftExcused.Add(Inv($"{c.GetProperty("name").GetString()}: raw delta {raw:R}"));
                    else ok = false;
                }
                if (c.GetProperty("name").GetString() == "the 99 discard")
                {
                    var st2 = MakeState(); var card2 = Card(c.GetProperty("card"));
                    DevRunCamp(st2, card2, c.GetProperty("campNumber").GetInt32(), c.GetProperty("share").GetDouble(), Feed(draws), cc, hand,
                               keepPastNinetyNineForTest: true);
                    past99Caught |= !ec.All(kv => card2[kv.Key] == kv.Value);
                }
            }
            check("C1g: ★ whole camps EXACT on the outcome, the points, every slip, the spurt, every rating and every streak; odds, gains " +
                  "and hidden progress within 1e-12 — funded, unfunded, slips, the breakout floor, the 99 discard, the IQ cap, a spurt at " +
                  "the ceiling; default and permuted order", ok,
                  $"{n} camps; free-throw half-boundary excusals: {(ftExcused.Count == 0 ? "none" : string.Join("; ", ftExcused))}");
            check("C1h: ★ NEGATIVE CONTROL — a camp that keeps progress past 99 fails the 99 case", past99Caught);
            var ftOk = g.GetProperty("freeThrows").EnumerateArray().All(f =>
            {
                var o = Ints(f.GetProperty("old")); var nw = Ints(f.GetProperty("new"));
                var (d, raw) = DevFtDelta(o[0], o[1], nw[0], nw[1]);
                var eraw = f.GetProperty("raw").GetDouble();
                return Math.Abs(raw - eraw) <= 1e-12 && (d == f.GetProperty("delta").GetInt32()
                       || Math.Abs(Math.Abs(eraw - Math.Floor(eraw)) - 0.5) < 1e-9);
            });
            check("C1i: the free-throw delta — the formula's own terms, the per-man idiosyncrasy cancelled; the one tanh exception " +
                  "excused only within 1e-9 of a half", ftOk);
        }
        // Config and points refusals
        {
            JsonObject root;
            using (var fs = File.OpenRead(configPath)) root = JsonNode.Parse(fs)!.AsObject();
            var section = root["Development"]!.AsObject();
            var ok = true; var n = 0; var firstBad = "";
            foreach (var r in g.GetProperty("configRefusals").EnumerateArray())
            {
                n++;
                var edited = JsonNode.Parse(section.ToJsonString())!.AsObject();
                foreach (var ed in r.GetProperty("edits").EnumerateObject()) edited[ed.Name] = JsonNode.Parse(ed.Value.GetRawText());
                var want = r.GetProperty("expectKeys").EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
                HashSet<string> got;
                try
                {
                    using var ed2 = JsonDocument.Parse(edited.ToJsonString());
                    DevelopmentConfig.FromSection(ed2.RootElement);
                    got = new HashSet<string>(StringComparer.Ordinal);
                }
                catch (InvalidOperationException ex)
                {
                    got = ex.Message["Development config refused: ".Length..].Split("; ")
                            .Select(m => m.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
                }
                if (!got.SetEquals(want)) { ok = false; firstBad = firstBad.Length > 0 ? firstBad : $"{r.GetProperty("name").GetString()}: got [{string.Join(", ", got)}]"; }
            }
            check("C1j: ★ every bad config edit refused at load naming EXACTLY the keys the oracle names — one per key class, and one " +
                  "config with several bad keys refused with every one of them listed at once", ok, ok ? $"{n} configs" : firstBad);
            var nan = DevelopmentConfig.Load(configPath);
            nan.TierRate = new[] { 0.0, double.NaN, 0.22, 0.35, 0.5 };
            nan.MinutesSpan = double.PositiveInfinity;
            var nanErr = nan.Validate();
            check("C1k: NaN and infinity refused first, by key (values JSON cannot carry, set in memory)",
                  nanErr.Count == 2 && nanErr.Any(m => m.StartsWith("TierRate:", StringComparison.Ordinal))
                  && nanErr.Any(m => m.StartsWith("MinutesSpan:", StringComparison.Ordinal)), string.Join(" | ", nanErr));
            var pOk = g.GetProperty("pointsRefusals").EnumerateArray().All(p =>
            {
                var byName = p.GetProperty("points").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetInt32());
                bool refused;
                try { DevValidatePoints(DevPointsByName(byName), cfg); refused = false; }
                catch (InvalidOperationException) { refused = true; }
                return refused == p.GetProperty("refused").GetBoolean();
            });
            check("C1l: hand-set camp points — over the budget, over the per-attribute cap, negative, not a funded attribute — refused; a legal camp accepted", pOk);
        }
        // Archetype careers (K5 end to end) — exact percentiles, printed
        {
            var seed = g.GetProperty("archetypeSeed").GetInt64();
            var N = g.GetProperty("archetypeCareers").GetInt32();
            var expectRows = g.GetProperty("archetypes").EnumerateArray().ToList();
            var got = new List<(string Player, string Attr, int Fr, int P10, int P50, int P90)>();
            var defs = g.GetProperty("archetypeDefs").EnumerateArray().ToList();
            for (var a = 0; a < defs.Count; a++)
            {
                var def = defs[a];
                var pos = def.GetProperty("pos").GetString()!;
                var d0 = def.GetProperty("desc");
                var desc = new DevDescriptor(0, 0, 0, d0.GetProperty("sz").GetDouble(), d0.GetProperty("gap").GetDouble(), 0, d0.GetProperty("talentTop").GetBoolean());
                var baseCard = Card(def.GetProperty("card"));
                var fixedTiers = def.GetProperty("tiers").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
                int? we = def.GetProperty("workEthic").ValueKind == JsonValueKind.Null ? null : def.GetProperty("workEthic").GetInt32();
                var share = def.GetProperty("share").GetDouble();
                var hand = def.GetProperty("hand").ValueKind == JsonValueKind.Null ? null : Ints(def.GetProperty("hand"));
                var targets = def.GetProperty("targets").EnumerateArray().Select(x => x.GetString()!).ToList();
                var finals = targets.ToDictionary(t => t, _ => new List<int>(N));
                for (var n = 0; n < N; n++)
                {
                    var rng = DevPotentialStream(seed + a, n);
                    var devSeed = rng.NextU64();
                    var roll = DevRollPotential(desc, pos, baseCard, rng.NextDouble, cfg);
                    var tiers = (int[])roll.Tiers.Clone();
                    foreach (var (k, t) in fixedTiers) tiers[DevIndexOf[k]] = t;
                    var st = new DevState
                    {
                        DevSeed = devSeed, WorkEthic = we ?? roll.WorkEthic, ArrivalIq = baseCard["BasketballIQ"],
                        ArrivalDiscipline = baseCard["Discipline"], Tiers = tiers, Streaks = new int[DevFundedCount],
                        Progress = new double[DevProgressCount],
                    };
                    var card = new Dictionary<string, int>(baseCard, StringComparer.Ordinal);
                    for (var campNo = 1; campNo <= 3; campNo++)
                    {
                        var cs = DevCampStream(devSeed, seed + 100 * (a + 1) + campNo);
                        DevRunCamp(st, card, campNo, share, cs.NextDouble, cfg, hand);
                    }
                    foreach (var t in targets) finals[t].Add(card[t]);
                }
                foreach (var t in targets)
                {
                    var xs = finals[t].OrderBy(x => x).ToList();
                    got.Add((def.GetProperty("name").GetString()!, t, baseCard[t], xs[(N - 1) * 1 / 10], xs[(N - 1) * 5 / 10], xs[(N - 1) * 9 / 10]));
                }
            }
            var same = got.Count == expectRows.Count && got.Zip(expectRows).All(x =>
                x.First.P10 == x.Second.GetProperty("p10").GetInt32() && x.First.P50 == x.Second.GetProperty("p50").GetInt32()
                && x.First.P90 == x.Second.GetProperty("p90").GetInt32() && x.First.Attr == x.Second.GetProperty("attribute").GetString());
            check("C1m: ★ the archetype careers end to end — K5 streams, the roll, three camps — give the oracle's percentiles EXACTLY",
                  same, $"{defs.Count} player types × {N} careers");
            Console.WriteLine("    (page) the archetype table — freshman rating -> start of senior year, three camps; bad / typical / great = 10th / 50th / 90th percentile");
            foreach (var r in got)
                Console.WriteLine(Inv($"    (page)   {r.Player,-86} {r.Attr,-14} Fr {r.Fr,2}  Sr {r.P10,2} / {r.P50,2} / {r.P90,2}"));
        }
        check("C1n: the largest difference between any C# and oracle double intermediate", worstD <= 1e-12, Inv($"{worstD:E2}"));
    }

    // ── C1b — funded athleticism is never below unfunded ─────────────────────

    private static void DevC1bAthleticism(DevelopmentConfig cfg, DevCheck check)
    {
        var draws = new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 0.9, 0.999999999 };
        var cases = 0; var bad = 0; var slips = 0;
        for (var c = DevBodyEnd; c < DevFundedCount; c++)
            for (var tier = 0; tier <= 4; tier++)
                for (var outcome = 0; outcome < 4; outcome++)
                    for (var pts = 1; pts <= 20; pts++)
                        foreach (var ua in draws)
                            foreach (var ub in draws)
                            {
                                cases++;
                                var (f, _) = DevGrowOne(c, tier, pts, outcome, ua, ub, cfg);
                                var (u, s) = DevGrowOne(c, tier, 0, outcome, ua, ub, cfg);
                                if (f < u) bad++;
                                if (s) slips++;
                            }
        check("C1o: ★ funded athleticism never grows less than unfunded — the per-attribute rule on IDENTICAL draws, every athletic " +
              "attribute × tier × outcome × points 1–20 × 49 draw pairs; and unfunded athleticism never slips",
              bad == 0 && slips == 0, $"{cases:N0} cases, {bad} below, {slips} slips");
    }

    // ── C3 — exact tier distributions ────────────────────────────────────────

    /// <summary>Per skill and tier (and per body/athletic attribute and tier): observed count vs Σ of each
    /// man's exact probability, 4 SE.</summary>
    private static (int Fails, double WorstZ, int Tests) DevTierTests(
        DivvyResult pool, DevDescriptor[] desc, DevState[] states, DevelopmentConfig cfg)
    {
        var n = pool.Pool.Count;
        var orderIndex = cfg.AttributeOrder.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => x.i, StringComparer.Ordinal);
        var exp = new double[DevFundedCount, 5];
        var vr = new double[DevFundedCount, 5];
        for (var m = 0; m < n; m++)
        {
            var d = desc[m]; var row = pool.Pool[m];
            var pa = cfg.BodyFirstNotchDown * DevClamp(d.Gap / cfg.BodyFirstFullGap, 0, 1);
            var pb = cfg.ReadyNotchDown * DevClamp(d.Sz / cfg.ReadyFullZ, 0, 1);
            var pc = d.TalentTop ? cfg.TalentNotchUp : 0.0;
            var net = new double[3];                                   // net shift -1, 0, +1
            for (var x = 0; x < 2; x++) for (var y = 0; y < 2; y++) for (var z = 0; z < 2; z++)
            {
                var p = (x == 1 ? pa : 1 - pa) * (y == 1 ? pb : 1 - pb) * (z == 1 ? pc : 1 - pc);
                net[DevClampInt(-x - y + z, -1, 1) + 1] += p;
            }
            var P = new double[5];
            for (var b = 0; b < 5; b++) for (var s = 0; s < 3; s++) P[DevClampInt(b + s - 1, 0, 4)] += cfg.PlayerTierOdds[b] * net[s];
            var best = PlayerGenPass3.SPEND_SKILLS.OrderByDescending(k => row.Ratings[k]).ThenBy(k => orderIndex[k]).First();
            for (var c = 0; c < DevFundedCount; c++)
            {
                var k = DevFunded[c];
                var pt = new double[5];
                if (DevGroupOf(c) == DevGroup.Skill)
                {
                    var sh = 0;
                    if (row.Pos == "G" && cfg.GuardDownSkills.Contains(k, StringComparer.Ordinal)) sh -= 1;
                    if (row.Pos == "B" && cfg.BigDownSkills.Contains(k, StringComparer.Ordinal)) sh -= 1;
                    if (k == best) sh -= 1;
                    sh = DevClampInt(sh, -1, 1);
                    for (var p0 = 0; p0 < 5; p0++) for (var o = 0; o < 5; o++)
                        pt[DevClampInt(p0 + o - 2 + sh, 0, 4)] += P[p0] * cfg.SkillOffsetOdds[o];
                }
                else for (var t = 0; t < 5; t++) pt[t] = cfg.BodyTierOdds[t];
                for (var t = 0; t < 5; t++) { exp[c, t] += pt[t]; vr[c, t] += pt[t] * (1 - pt[t]); }
            }
        }
        var fails = 0; var worst = 0.0; var tests = 0;
        for (var c = 0; c < DevFundedCount; c++)
            for (var t = 0; t < 5; t++)
            {
                var obs = states.Count(s => s.Tiers[c] == t);
                tests++;
                if (vr[c, t] <= 0) { if (Math.Abs(obs - exp[c, t]) > 0.5) fails++; continue; }
                var zz = Math.Abs((obs - exp[c, t]) / Math.Sqrt(vr[c, t]));
                worst = Math.Max(worst, zz);
                if (zz > 4) fails++;
            }
        return (fails, worst, tests);
    }

    // ── C5b — four seasons, three camps, the reload ──────────────────────────

    private static void DevC5bFourSeasons(
        string configPath, DevelopmentConfig cfg, WorldFile mte, string scratch,
        Func<WorldFile, long, string, SeasonRunOutcome> career,
        Func<WorldFile, string, long, IReadOnlyList<ScoutingRecord>> readScout,
        DevCheck check)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        var dir = Path.Combine(scratch, "mte4");
        var path = Path.Combine(dir, "career.json");
        var seeds = Enumerable.Range(0, 4).Select(k => DevelopmentCheckSeed + 1000 + k).ToArray();
        var s1 = career(mte, seeds[0], path);
        var s2 = career(mte, seeds[1], path);
        // Snapshot the career after season two, before season three exists, for the reload arms.
        var copyDir = Path.Combine(scratch, "mte4-copy");
        var missDir = Path.Combine(scratch, "mte4-missing");
        foreach (var d in new[] { copyDir, missDir }) DevCopyDirectory(dir, d);
        var s3 = career(mte, seeds[2], path);
        // ...and again after season three: the first summer a streak can reach two is season four's camp.
        var copy3Dir = Path.Combine(scratch, "mte4-copy3");
        var negDir = Path.Combine(scratch, "mte4-zeroed");
        foreach (var d in new[] { copy3Dir, negDir }) DevCopyDirectory(dir, d);
        var s4 = career(mte, seeds[3], path);
        var runs = new[] { s1, s2, s3, s4 };

        // (1) Every man who arrived a freshman in season one: camp 1, 2, 3 in order, each its own split.
        var freshmen = Enumerable.Range(0, s1.Divvy.Pool.Count).Where(i => s1.Divvy.Pool[i].Class == ClassYear.Fr)
                                 .Select(i => s1.Divvy.PersonIds![i]).ToList();
        var inOrder = true; var followed = 0; string? why = null;
        foreach (var id in freshmen)
        {
            followed++;
            for (var season = 2; season <= 4; season++)
            {
                var run = runs[season - 1];
                var idx = Enumerable.Range(0, run.Divvy.Pool.Count).First(i => run.Divvy.PersonIds![i] == id);
                var camp = run.Development!.Camps[idx];
                var campNo = season - 1;
                var split = cfg.PrioritySplits[Math.Min(campNo, cfg.PrioritySplits.Length) - 1];
                var funded = camp?.Points.Where(p => p > 0).OrderByDescending(p => p).ToArray() ?? Array.Empty<int>();
                if (camp is null || camp.CampNumber != campNo || !funded.SequenceEqual(split.Take(funded.Length).OrderByDescending(p => p)))
                { inOrder = false; why ??= $"{id} season {season}: camp {camp?.CampNumber}, points [{string.Join(",", funded)}]"; }
            }
        }
        check("C5b-1: ★ every man who arrived a freshman in season one camps 1, 2, 3 in order on a four-season career — each summer " +
              "spending exactly that camp's split (20/15/15, then 15/15/10/10, then 15/10/10/10/5)", inOrder && followed > 0,
              why ?? $"{followed} men followed through three camps");

        // The rotation fires: an attribute funded two summers running is outranked by one it would otherwise beat.
        var rotations = 0;
        for (var season = 3; season <= 4; season++)
        {
            var run = runs[season - 1];
            var prev = readScout(mte, path, season - 1).ToDictionary(r => r.Person, DevStateFromRecord);
            for (var i = 0; i < run.Development!.ReturnerCount; i++)
            {
                var st0 = prev[run.Divvy.PersonIds![i]];
                var pts = run.Development.Camps[i]!.Points;
                double Promise(int c) => cfg.TierRate[st0.Tiers[c]] * DevGroupFactor(cfg, DevGroupOf(c));
                for (var a = 0; a < DevFundedCount; a++)
                {
                    if (st0.Streaks[a] < cfg.RepeatAfter) continue;
                    if (Enumerable.Range(0, DevFundedCount).Any(b => Promise(b) < Promise(a) && pts[b] > pts[a])) { rotations++; break; }
                }
            }
        }
        check("C5b-2: ★ the rotation fires league-wide — men whose camp funded an attribute it had hammered two summers running " +
              "below one with less promise", rotations > 0, $"{rotations} camps rotated");

        // (2) The reload: a copy of the career taken after season two, resumed, camps exactly as the original.
        static bool SameSeason(SeasonRunOutcome a, SeasonRunOutcome b)
            => a.Divvy.Pool.Count == b.Divvy.Pool.Count
               && Enumerable.Range(0, b.Divvy.Pool.Count).All(i => RetentionRatingOrder.All(k => a.Divvy.Pool[i].Ratings[k] == b.Divvy.Pool[i].Ratings[k])
                                                                   && a.DevStates![i].SameAs(b.DevStates![i]))
               && Enumerable.Range(0, b.Development!.ReturnerCount).All(i => a.Development!.Camps[i]!.Points.SequenceEqual(b.Development.Camps[i]!.Points));
        var c3 = career(mte, seeds[2], Path.Combine(copyDir, "career.json"));
        var c4 = career(mte, seeds[3], Path.Combine(copy3Dir, "career.json"));
        check("C5b-3: ★ the reload — copies of the career's files taken after season two and after season three, each resumed, camp " +
              "exactly as the original: same cards, same allocations, same hidden state (every input comes off disk)",
              SameSeason(c3, s3) && SameSeason(c4, s4), $"{s3.Development!.ReturnerCount} + {s4.Development!.ReturnerCount} returners");

        // Negative control: the copy with every streak zeroed in season three's scouting file camps differently in season four.
        {
            var negPath = Path.Combine(negDir, "career.json");
            var scoutPath = ScoutingFile.PathFor(negPath, 3);
            var zeroed = readScout(mte, negPath, 3).Select(r => r with { Streaks = new int[r.Streaks.Count] }).ToList();
            File.Delete(scoutPath);
            using (var store = HistoryStore.Open(negPath, WorldFingerprint(mte)))
                ScoutingFile.Write(negPath, store.HistoryId, store.WorldFingerprint, s3.Schedule[0].SeasonId!.Value, zeroed);
            var n4 = career(mte, seeds[3], negPath);
            var changed = Enumerable.Range(0, s4.Development.ReturnerCount)
                                    .Count(i => !n4.Development!.Camps[i]!.Points.SequenceEqual(s4.Development.Camps[i]!.Points));
            check("C5b-4: ★ NEGATIVE CONTROL — the same reload with every streak zeroed on disk spends camps differently (the rotation's " +
                  "memory lives in the file)", changed > 0, $"{changed} camps changed");
        }

        // C4 — the refusals.
        {
            var missPath = Path.Combine(missDir, "career.json");
            File.Delete(ScoutingFile.PathFor(missPath, 2));
            var before = PeekState(missPath);
            Exception? refused = null;
            try { career(mte, seeds[2], missPath); } catch (Exception ex) { refused = ex; }
            var after = PeekState(missPath);
            check("C4e: ★ a career whose last scouting file is missing refuses the next season BY NAME — never re-rolled — with no " +
                  "season, game or person number spent and no file written",
                  refused is ScoutingException { Error: ScoutingError.Missing } && after.NextSeasonId == before.NextSeasonId
                  && after.NextPersonId == before.NextPersonId && !File.Exists(GameLogWriter.FinalPathFor(missPath, 3))
                  && !File.Exists(ScoutingFile.PathFor(missPath, 3)),
                  refused?.Message is { } m ? m[..Math.Min(120, m.Length)] : "(no refusal)");

            var good = ScoutingFile.PathFor(path, 2);
            var bytes = File.ReadAllBytes(good);
            using var store = HistoryStore.Open(path, WorldFingerprint(mte));
            var bind = new ScoutingBindings(store.HistoryId, store.WorldFingerprint, 2);
            ScoutingError? Refusal(Action a)
            {
                try { a(); return null; } catch (ScoutingException ex) { return ex.Error; }
            }
            var probe = Path.Combine(scratch, "probe", "career.json");
            var probeFile = ScoutingFile.PathFor(probe, 2);
            Directory.CreateDirectory(Path.GetDirectoryName(probeFile)!);
            ScoutingError? With(byte[] b, ScoutingBindings bb)
            {
                File.WriteAllBytes(probeFile, b);
                return Refusal(() => ScoutingFile.Read(probe, bb));
            }
            var damaged = (byte[])bytes.Clone();
            damaged[^5] ^= 0x01;
            var older = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("version 1\n", "version 0\n"));
            var otherCareer = bind with { HistoryId = new string('a', 32) };
            var asSeason3 = ScoutingFile.PathFor(probe, 3);
            File.WriteAllBytes(asSeason3, bytes);
            var results = new (string Name, ScoutingError? Got, ScoutingError Want)[]
            {
                ("a damaged byte", With(damaged, bind), ScoutingError.Damaged),
                ("an older version", With(older, bind), ScoutingError.UnsupportedVersion),
                ("another career's", With(bytes, otherCareer), ScoutingError.HistoryIdMismatch),
                ("another season's", Refusal(() => ScoutingFile.Read(probe, bind with { SeasonId = 3 })), ScoutingError.SeasonMismatch),
                ("a truncated file", With(bytes[..(bytes.Length / 2)], bind), ScoutingError.Damaged),
            };
            check("C4f: ★ a damaged, older-version, other-career, other-season or truncated scouting file is refused by name",
                  results.All(r => r.Got == r.Want), string.Join("; ", results.Select(r => $"{r.Name} -> {r.Got?.ToString() ?? "read"}")));
            var exists = Refusal(() => ScoutingFile.Write(path, store.HistoryId, store.WorldFingerprint, s2.Schedule[0].SeasonId!.Value,
                                                          ScoutingFile.Read(path, bind)));
            check("C4g: an existing scouting file is never overwritten", exists == ScoutingError.AlreadyExists
                  && File.ReadAllBytes(good).SequenceEqual(bytes), exists?.ToString() ?? "(written)");
        }

        // Page: development concentration across three camps.
        var distinct = freshmen.Select(id =>
        {
            var set = new HashSet<int>();
            for (var season = 2; season <= 4; season++)
            {
                var run = runs[season - 1];
                var idx = Enumerable.Range(0, run.Divvy.Pool.Count).First(i => run.Divvy.PersonIds![i] == id);
                var pts = run.Development!.Camps[idx]!.Points;
                for (var c = 0; c < DevFundedCount; c++) if (pts[c] > 0) set.Add(c);
            }
            return set.Count;
        }).ToList();
        Console.WriteLine("    (page) development concentration — distinct attributes funded across a three-camp career (fixture-mte, " +
                          $"{distinct.Count} men): " + string.Join("  ", distinct.GroupBy(x => x).OrderBy(gr => gr.Key)
                              .Select(gr => Inv($"{gr.Key}: {100.0 * gr.Count() / distinct.Count:F0}%"))));
    }

    private static void DevCopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (f.EndsWith(".lock", StringComparison.Ordinal)) continue;
            var rel = Path.GetRelativePath(from, f);
            var dest = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest);
        }
    }

    // ── C7 — page only ───────────────────────────────────────────────────────

    private static void DevPageOnly(WorldFile stock, SeasonRunOutcome one, SeasonRunOutcome two, GameLogV1 log1, GameLogV1 log2,
                                    DevelopmentConfig cfg, string configPath)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        var dev = two.Development!;
        var R = dev.ReturnerCount;
        var (_, _, desc) = DevBootstrap(one.Divvy, DevelopmentCheckSeed, cfg);

        // Groups (the bust ruling): athletic/size bigs = top quarter of bigs by body-over-skill; solid-arriving
        // guards = top quarter of guards by skill; everyone else.
        string GroupOf(int i)
        {
            var row = one.Divvy.Pool[i];
            if (row.Pos == "B")
            {
                var gaps = Enumerable.Range(0, desc.Length).Where(j => one.Divvy.Pool[j].Pos == "B").Select(j => desc[j].Gap).OrderByDescending(x => x).ToList();
                if (desc[i].Gap >= gaps[gaps.Count / 4]) return "athletic/size bigs";
            }
            if (row.Pos == "G")
            {
                var sz = Enumerable.Range(0, desc.Length).Where(j => one.Divvy.Pool[j].Pos == "G").Select(j => desc[j].Sz).OrderByDescending(x => x).ToList();
                if (desc[i].Sz >= sz[sz.Count / 4]) return "solid-arriving guards";
            }
            return "everyone else";
        }
        var groupOfPerson = Enumerable.Range(0, one.Divvy.Pool.Count).ToDictionary(i => one.Divvy.PersonIds![i], GroupOf);
        var groups = new[] { "athletic/size bigs", "solid-arriving guards", "everyone else" };

        // Camp outcomes, breakouts, drift.
        var outcomes = Enumerable.Range(0, R).GroupBy(i => dev.Camps[i]!.Outcome).ToDictionary(gr => gr.Key, gr => gr.Count());
        Console.WriteLine(Inv($"    (page) stock season two camps: bad {outcomes.GetValueOrDefault(0)}, normal {outcomes.GetValueOrDefault(1)}, good {outcomes.GetValueOrDefault(2)}, breakout {outcomes.GetValueOrDefault(3)}; height spurts {Enumerable.Range(0, R).Count(i => dev.Camps[i]!.Spurt)}"));
        var card1 = Enumerable.Range(0, one.Divvy.Pool.Count).ToDictionary(i => one.Divvy.PersonIds![i], i => one.Divvy.Pool[i].Ratings);
        double Best5(IReadOnlyDictionary<string, int> c) => PlayerGenPass3.SPEND_SKILLS.Select(k => c[k]).OrderByDescending(x => x).Take(5).Average();
        foreach (var cls in new[] { ClassYear.So, ClassYear.Jr, ClassYear.Sr })
            foreach (var pos in new[] { "G", "W", "B" })
            {
                var men = Enumerable.Range(0, R).Where(i => two.Divvy.Pool[i].Class == cls && two.Divvy.Pool[i].Pos == pos).ToList();
                if (men.Count == 0) continue;
                var before = men.Select(i => card1[two.Divvy.PersonIds![i]]).ToList();
                var after = men.Select(i => two.Divvy.Pool[i].Ratings).ToList();
                var avg = men.Select((i, j) => DevFunded.Average(k => after[j][k] - before[j][k])).Average();
                var b5 = men.Select((i, j) => Best5(after[j]) - Best5(before[j])).Average();
                Console.WriteLine(Inv($"    (page) {cls} {pos} (n={men.Count,4}): average funded-attribute change {avg:+0.00;-0.00}, best-five skills {b5:+0.00;-0.00}"));
            }

        // Functional change — the per-40 box line, by group, read off the logs (the engine judges what the ratings bought).
        Dictionary<PersonId, double[]> Per40(GameLogV1 log)
        {
            var acc = new Dictionary<PersonId, double[]>();
            foreach (var b in log.Blocks)
                foreach (var r in b.Rows)
                {
                    if (!acc.TryGetValue(r.PersonId, out var a)) acc[r.PersonId] = a = new double[10];
                    a[0] += CareerGameMinutes(r, b.Facts); a[1] += 2 * r.Fgm + r.Tpm + r.Ftm; a[2] += r.OReb + r.DReb; a[3] += r.Ast;
                    a[4] += r.Stl; a[5] += r.Blk; a[6] += r.To; a[7] += r.Fga; a[8] += r.Fgm; a[9] += r.Tpa;
                }
            return acc;
        }
        var p1 = Per40(log1); var p2 = Per40(log2);
        Console.WriteLine("    (page) functional change, returners with 200+ minutes both seasons — per 40: pts / reb / ast / stl / blk / tov / FG% (season one -> season two)");
        foreach (var grp in groups)
        {
            var ids = Enumerable.Range(0, R).Select(i => two.Divvy.PersonIds![i])
                .Where(id => groupOfPerson[id] == grp && p1.TryGetValue(id, out var a) && a[0] >= 200 && p2.TryGetValue(id, out var b) && b[0] >= 200).ToList();
            if (ids.Count == 0) continue;
            string Line(Dictionary<PersonId, double[]> p)
            {
                var m = ids.Sum(id => p[id][0]);
                double R40(int k) => ids.Sum(id => p[id][k]) / m * 40;
                return Inv($"{R40(1),5:F1} / {R40(2),4:F1} / {R40(3),3:F1} / {R40(4),3:F1} / {R40(5),3:F1} / {R40(6),3:F1} / {100 * ids.Sum(id => p[id][8]) / Math.Max(1, ids.Sum(id => p[id][7])),4:F1}%");
            }
            Console.WriteLine(Inv($"    (page)   {grp,-22} (n={ids.Count,4}): {Line(p1)}  ->  {Line(p2)}"));
        }

        // The bust table through the engine: every man of the stock bootstrap pool, three camps on his real season-one minutes.
        var (states, _, _) = DevBootstrap(one.Divvy, DevelopmentCheckSeed, cfg);
        var schoolOf = DevSchoolOf(one.Divvy);
        var inside = new[] { "Finishing", "PostMoves", "RimProtection", "OffensiveRebounding", "DefensiveRebounding" };
        var tally = groups.ToDictionary(gname => gname, _ => new int[4]);   // n, flat, some +20, inside +15
        var useful = 0; var monsters = 0;
        for (var i = 0; i < one.Divvy.Pool.Count; i++)
        {
            var st = states[i].Clone();
            var card = new Dictionary<string, int>(one.Divvy.Pool[i].Ratings, StringComparer.Ordinal);
            var start = new Dictionary<string, int>(card, StringComparer.Ordinal);
            one.SecondsByPool.TryGetValue(i, out var secs);
            one.TeamGames.TryGetValue(schoolOf[i], out var games);
            var share = DevMinutesShare(secs, games);
            for (var campNo = 1; campNo <= 3; campNo++)
                DevRunCamp(st, card, campNo, share, DevCampStream(st.DevSeed, DevelopmentCheckSeed + 500 + campNo).NextDouble, cfg);
            var t = tally[GroupOf(i)];
            t[0]++;
            var gains = DevelopmentConfig.Skills.Select(k => card[k] - start[k]).ToList();
            if (gains.All(gn => gn < 8)) t[1]++;
            if (gains.Any(gn => gn >= 20)) t[2]++;
            if (inside.Any(k => card[k] - start[k] >= 15)) t[3]++;
            var monster = desc[i].Bz >= 1.0 && desc[i].Sz <= -0.5;
            if (monster) { monsters++; if (PlayerGenPass3.SPEND_SKILLS.Any(k => card[k] >= 60 && start[k] < 60)) useful++; }
        }
        Console.WriteLine("    (page) the bust table — the stock pool, three camps each on his real season-one minutes; flat = no skill gained 8");
        foreach (var grp in groups)
        {
            var t = tally[grp];
            if (t[0] == 0) continue;
            Console.WriteLine(Inv($"    (page)   {grp,-22} (n={t[0],4}): flat {100.0 * t[1] / t[0],5:F1}%   some skill +20 {100.0 * t[2] / t[0],5:F1}%   inside skill +15 {100.0 * t[3] / t[0],5:F1}%"));
        }
        Console.WriteLine(Inv($"    (page) monster athletes (body z >= 1, skill z <= -0.5): {monsters}; {useful} grew a skill past 60 they arrived without"));
    }
}
