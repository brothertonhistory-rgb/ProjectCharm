using Charm.Engine;
using System.Reflection;

namespace Charm.Harness;

// ============================================================================
//  Phase 103 — S113: EVERY PLAYER GETS A CLASS.
//
//  Emmett's rulings (2026-10-04): classes are an even random split, independent of
//  talent. Nothing about play changes. What is proven:
//    C1  every pool player has exactly one class
//    C2  the draw is even (the mechanism, over 100,000 synthetic ids)
//    C3  class is independent of talent — with a negative control that is not
//    C4  deterministic, and responsive to the divvy seed
//    C5  the draw reads nothing but (seed, pool id)
//    C6  the dormant Player.PlayerClass seat stays dormant (the A2 call, made visible)
//    C7  ★ the fingerprint wall: seven fingerprints and every stock game unmoved
//  The stock world's own split and the seniors-per-team spread are PRINTED, never asserted.
// ============================================================================
internal static partial class Program
{
    private const long ClassCheckSeed = 20260720;

    private static bool Phase103PlayerClassesCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 103 — S113: every player gets a class. One class each, an even draw, independent " +
                          "of talent with a negative control, deterministic and seed-responsive, reads only (seed, id), " +
                          "the Player seat stays dormant, and seven fingerprints and every game unmoved ==");
        var pass = true;
        var assertions = 0;

        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        static string Inv(FormattableString f) => FormattableString.Invariant(f);

        static double Corr(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
            for (var i = 0; i < a.Count; i++)
            {
                sab += (a[i] - ma) * (b[i] - mb);
                saa += (a[i] - ma) * (a[i] - ma);
                sbb += (b[i] - mb) * (b[i] - mb);
            }
            return sab / Math.Sqrt(saa * sbb);
        }

        try
        {
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var run = RunSeasonCore(stock, ClassCheckSeed, configPath, verbose: false);
            var pool = run.Divvy.Pool;
            var classes = Enum.GetValues<ClassYear>();

            // ── C1: one class each ──────────────────────────────────────────────
            {
                var expected = RosterShape.PoolSize(stock.Schools.Count);
                Check("C1a: the stock pool is the full pool", pool.Count == expected, $"{pool.Count} of {expected}");
                Check("C1b: every pool player holds exactly one DEFINED class (none missing, none out of range)",
                      pool.All(p => Enum.IsDefined(p.Class)), $"{pool.Count(p => Enum.IsDefined(p.Class))} classed");
                Console.WriteLine("    stock split (printed, not asserted): " + string.Join("  ", classes.Select(c =>
                    Inv($"{c} {pool.Count(p => p.Class == c)} ({100.0 * pool.Count(p => p.Class == c) / pool.Count:F1}%)"))));
            }

            // ── C2: the draw is even — the mechanism, not one seed's luck ────────
            {
                const int N = 100_000;
                var counts = new int[4];
                for (var id = 0; id < N; id++) counts[(int)InitialPlayerClass(ClassCheckSeed, id)]++;
                var pcts = counts.Select(k => 100.0 * k / N).ToArray();
                Check("C2: over 100,000 synthetic ids every class is 25% ± 0.5 pp",
                      pcts.All(x => Math.Abs(x - 25.0) <= 0.5),
                      string.Join(" / ", pcts.Select((x, i) => Inv($"{(ClassYear)i} {x:F2}%"))));
            }

            // ── C3: independent of talent, and the check can tell ────────────────
            {
                var ci = pool.Select(p => (double)(int)p.Class).ToList();
                var rank = pool.Select(p => p.ScoutRank).ToList();
                var plane = pool.Select(p => p.DefensivePlane).ToList();
                var rRank = Corr(ci, rank);
                var rPlane = Corr(ci, plane);
                Check("C3a: |r(class, scout rank)| < 0.06", Math.Abs(rRank) < 0.06, Inv($"r = {rRank:F4}"));
                Check("C3b: |r(class, defensive plane)| < 0.06", Math.Abs(rPlane) < 0.06, Inv($"r = {rPlane:F4}"));

                // NEGATIVE CONTROL: class by scout-rank quartile (best quarter = seniors).
                var byRank = pool.OrderBy(p => p.ScoutRank).ThenBy(p => p.PoolId)
                                 .Select((p, i) => (p.PoolId, Q: Math.Min(3, 4 * i / pool.Count)))
                                 .ToDictionary(x => x.PoolId, x => (double)x.Q);
                var rigged = pool.Select(p => byRank[p.PoolId]).ToList();
                var rRig = Corr(rigged, rank);
                Check("C3c: ★ NEGATIVE CONTROL — class assigned by scout-rank quartile is REJECTED by the same bar",
                      Math.Abs(rRig) >= 0.06, Inv($"r = {rRig:F4}"));
            }

            // ── C4: deterministic, and the seed matters ──────────────────────────
            {
                var again = RunDivvyDraft(stock, ClassCheckSeed).Pool;
                Check("C4a: the same divvy seed gives identical classes",
                      again.Count == pool.Count && again.Zip(pool).All(x => x.First.Class == x.Second.Class));
                var next = RunDivvyDraft(stock, ClassCheckSeed + 1).Pool;
                var changed = next.Zip(pool).Count(x => x.First.Class != x.Second.Class);
                var frac = (double)changed / pool.Count;
                Check("C4b: divvy seed 20260721 changes at least 60% of classes (expected 75%)",
                      frac >= 0.60, Inv($"{100.0 * frac:F1}% changed"));
            }

            // ── C5: the draw reads nothing but (seed, pool id) ───────────────────
            {
                var ps = typeof(Program).GetMethod(nameof(InitialPlayerClass), BindingFlags.NonPublic | BindingFlags.Static)!
                                        .GetParameters();
                Check("C5a: the draw's only inputs are (long seed, int pool id) — no player, rating or position can reach it",
                      ps.Length == 2 && ps[0].ParameterType == typeof(long) && ps[1].ParameterType == typeof(int));
                Check("C5b: every stock row's class is exactly the draw at (seed, its own pool id)",
                      pool.All(p => p.Class == InitialPlayerClass(ClassCheckSeed, p.PoolId)));

                // Two DIFFERENT people at the same pool id: the tiny world's pool id k is usually a
                // different player (different ratings, and past its guard block a different position)
                // from the stock world's id k. Both pools come off the same cohort stream, so a few ids
                // land on the very same person; they are counted, not required to differ. Same seed,
                // same id, so the same class either way.
                var tiny = RunDivvyDraft(LoadWorld(WorldPath("fixture-tiny.world.json")), ClassCheckSeed).Pool;
                var differs = tiny.Count(t => t.Pos != pool[t.PoolId].Pos
                                              || t.ScoutRank != pool[t.PoolId].ScoutRank);
                Check("C5c: at least 200 of the constructed pairs really are different people (position or rating differs)",
                      differs >= 200, $"{differs} of {tiny.Count} pairs differ");
                Check("C5d: ...and every pair shares its class regardless",
                      tiny.All(t => t.Class == pool[t.PoolId].Class), $"{tiny.Count} of {tiny.Count}");
            }

            // ── C6: the dormant seat stays dormant (the A2 call, visible) ────────
            {
                Check("C6a: every pool player's Player.PlayerClass is \"\" — class lives on the pool row only",
                      pool.All(p => p.Player.PlayerClass == ""));
                var probe = new Player("ClassProbe") { PlayerClass = "Sr" };
                Check("C6b: the per-game copy drops the seat, which is WHY class is not on Player " +
                      "(if this ever goes red, the copy changed — revisit the A2 call on purpose)",
                      StampPlayerId(probe, 1).PlayerClass == "");
            }

            // ── C7: ★ the fingerprint wall ───────────────────────────────────────
            {
                var prefix = run.ConferenceGameCount + run.TournamentGameCount;
                var resultsFp = SeasonFingerprint(run.Results.Take(prefix).ToList(), run.PossessionCounts.Take(prefix).ToList());
                Check("C7a: #1 conference schedule UNMOVED", run.Fingerprint == MatchGoldenConferenceFp);
                Check("C7b: #2 conference dated UNMOVED", run.DatedFingerprint == MatchGoldenDatedFp);
                Check("C7c: #3 event games UNMOVED", run.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C7d: #4 results+possessions UNMOVED over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C7e: #5 non-conference dated UNMOVED", run.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C7f: #6 conference tournaments UNMOVED", run.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C7g: #7 buy games UNMOVED", run.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                var games = RatingSha(string.Concat(run.Results.Select((x, i) =>
                    $"{i}|{x.HomeId}|{x.AwayId}|{x.HomeScore}|{x.AwayScore}|{run.PossessionCounts[i]}\n")));
                Check("C7h: ★ every one of the season's games identical to the S118.1 capture — teams, scores, " +
                      "possessions", games == RatingGoldenPreS112GameDigest, $"{run.PlayedGames.Count} games, {games[..16]}");
            }
        }
        catch (InvalidOperationException ex)
        {
            Check("Phase 103 ran to completion", false, ex.Message);
        }

        Console.WriteLine($"  Phase 103: {assertions} assertions — {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
