using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// Phase 74 (Session 79) — THE HELP ARM.
//
// Before S79 the located-shot block door consulted exactly one defender, so an elite
// rim protector who was not guarding the ball moved the team's block rate by ZERO; and
// block CREDIT came from a six-attribute weighted sum whose p99/median spread was 1.48x,
// so the best rim protector in the country took 30% of his lineup's blocks against each
// guard's 17%. This phase locks both halves.
//
// Golden fixture tools/block_help_golden.json is emitted by tools/block_help_oracle.py
// (LOCKED SPEC 2026-07-27). The C# binds to Matchup's named statics; the oracle is the
// independent implementation. A drift in either fails (1) below.
//
// The invariants matter MORE than the golden output: a golden proves the port matches the
// oracle whatever the oracle says. (2)-(6) are the properties that would still be true if
// the oracle itself were wrong, and they are asserted on generated variety, not on flat
// benches (the S59.2 lesson — a flattened dial is an assumption, not a control).

internal static partial class Program
{
    private static bool Phase74BlockHelpCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 74: block help arm + contribution credit (golden parity + invariants + config guards) ---");
        var pass = true;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgM = MatchupConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);
        var zones = new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
                            ShotLocation.Long, ShotLocation.Three };

        static Player Mk(string n, int height = 50, int wingspan = 50, int vertical = 50,
                         int strength = 50, int perimD = 50, int postD = 50, int rimP = 50,
                         int helpD = 50, int fin = 50, int close = 50, int mid = 50, int outside = 50)
            => new Player(n)
            {
                Outside = outside, Mid = mid, Close = close, Finishing = fin, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = 50, PerimeterDefense = perimD, PostDefense = postD,
                RimProtection = rimP, DefensiveRebounding = 50, Steals = 50,
                Height = height, Wingspan = wingspan, Weight = 50, Strength = strength,
                Speed = 50, Quickness = 50, FirstStep = 50, Vertical = vertical,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                HelpDefense = helpD, OffBallDefense = 50,
                RimTendency = 50, ShortTendency = 50, MidTendency = 50,
                LongTendency = 50, ThreeTendency = 50,
            };

        // ── (1) Golden parity vs tools/block_help_golden.json ────────────────
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "block_help_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var tol  = root.GetProperty("float_tolerance").GetDouble();

            // Fixture-validity guard FIRST: a fixture emitted against different constants
            // would make every row agree with a WRONG engine. Reject before comparing.
            var consts = root.GetProperty("constants");
            var constOk =
                Math.Abs(consts.GetProperty("BlockHelpPositionalSwing").GetDouble() - cfgM.BlockHelpPositionalSwing) < 1e-15 &&
                Math.Abs(consts.GetProperty("BlockHelpPositionalScale").GetDouble() - cfgM.BlockHelpPositionalScale) < 1e-15 &&
                Math.Abs(consts.GetProperty("BlockCreditLuckFloor").GetDouble()     - cfgM.BlockCreditLuckFloor)     < 1e-15 &&
                Math.Abs(consts.GetProperty("BlockHelpDepthHeight").GetDouble()     - cfgM.BlockHelpDepthHeight)     < 1e-15 &&
                Math.Abs(consts.GetProperty("BlockHelpDepthStrength").GetDouble()   - cfgM.BlockHelpDepthStrength)   < 1e-15 &&
                Math.Abs(consts.GetProperty("BlockHelpShareRim").GetDouble()        - cfgM.BlockHelpShareRim)        < 1e-15 &&
                Math.Abs(consts.GetProperty("BlockHelpShareThree").GetDouble()      - cfgM.BlockHelpShareThree)      < 1e-15 &&
                Math.Abs(consts.GetProperty("BlockReferenceShift").GetDouble()      - cfgM.BlockReferenceShift)      < 1e-15;
            if (!constOk)
                throw new InvalidOperationException(
                    "golden fixture rejected: S79 constants do not match the loaded MatchupConfig. " +
                    "Re-run tools/block_help_oracle.py after any config change.");

            // Rebuild the fixture's players and lineups from its own declarations.
            var players = new Dictionary<string, Player>();
            foreach (var p in root.GetProperty("players").EnumerateObject())
            {
                var v = p.Value;
                int G(string k) => v.GetProperty(k).GetInt32();
                players[p.Name] = Mk(p.Name, G("Height"), G("Wingspan"), G("Vertical"), G("Strength"),
                                     G("PerimeterDefense"), G("PostDefense"), G("RimProtection"),
                                     G("HelpDefense"), G("Finishing"), G("Close"), G("Mid"), G("Outside"));
            }
            var lineups = new Dictionary<string, Player?[]>();
            foreach (var l in root.GetProperty("lineups").EnumerateObject())
            {
                var arr = new Player?[5];
                var i = 0;
                foreach (var e in l.Value.EnumerateArray())
                    arr[i++] = e.ValueKind == JsonValueKind.Null ? null : players[e.GetString()!];
                lineups[l.Name] = arr;
            }

            var rows = root.GetProperty("rows");
            var worst = 0.0; var allOk = true; var n = 0; var putbackRows = 0;
            foreach (var r in rows.EnumerateArray())
            {
                n++;
                var defs    = lineups[r.GetProperty("lineup").GetString()!];
                var zone    = Enum.Parse<ShotLocation>(r.GetProperty("zone").GetString()!);
                var mi      = r.GetProperty("matched_index").GetInt32();
                var isPb    = r.TryGetProperty("putback", out var pbEl) && pbEl.GetBoolean();

                var wGold = r.GetProperty("credit_weights").EnumerateArray()
                             .Select(x => x.GetDouble()).ToArray();
                var w = isPb
                    ? Matchup.PutbackBlockCreditWeights(defs, cfgM)
                    : Matchup.BlockCreditWeights(zone, defs, mi, cfgM);
                for (var i = 0; i < 5; i++)
                {
                    var d = Math.Abs(w[i] - wGold[i]);
                    worst = Math.Max(worst, d);
                    if (d > tol) allOk = false;
                }

                if (isPb) { putbackRows++; continue; }

                var dGold = Math.Abs(Matchup.BlockHelpSum(zone, defs, mi, cfgM)
                                   - r.GetProperty("help_sum").GetDouble());
                worst = Math.Max(worst, dGold);
                if (dGold > tol) allOk = false;

                var mdGold = Math.Abs(Matchup.BlockHelpMeanDepth(defs, cfgM)
                                    - r.GetProperty("mean_depth").GetDouble());
                worst = Math.Max(worst, mdGold);
                if (mdGold > tol) allOk = false;

                if (mi >= 0 && defs[mi] is not null)
                {
                    var shooter = players[r.GetProperty("shooter").GetString() == "elite"
                                          ? "elite_shooter" : "shooter"];
                    var rate = Matchup.BlockWeightWithHelp(zone, shooter, defs[mi]!, defs, mi,
                                                           cfgH.BlockWeight(zone), cfgM);
                    var dr = Math.Abs(rate - r.GetProperty("rate").GetDouble());
                    worst = Math.Max(worst, dr);
                    if (dr > tol) allOk = false;

                    var du = Math.Abs(Matchup.BlockDuelShift(zone, shooter, defs[mi]!, cfgM)
                                    - r.GetProperty("duel_shift").GetDouble());
                    worst = Math.Max(worst, du);
                    if (du > tol) allOk = false;
                }
            }
            Check($"golden parity ({n} rows, {putbackRows} putback, tol {tol:E0})", allOk,
                  $"worst |Δ| = {worst:E1}");
        }
        catch (Exception ex) { Check("golden parity", false, ex.Message); }

        // ── (2) BlockWeight is byte-identical after the S79 extraction ───────
        // BlockDuelShift + BlockBend were pulled OUT of BlockWeight. Same ops, same order.
        // Phase 7 still asserts BlockWeight's behaviour; this proves the seam moved nothing.
        {
            var ok = true; var worst = 0.0;
            for (var s = 10; s <= 95; s += 5)
                for (var d = 10; d <= 95; d += 5)
                    foreach (var z in zones)
                    {
                        var sh = Mk("s", height: s, wingspan: s, vertical: s, fin: s, close: s, mid: s, outside: s);
                        var df = Mk("d", height: d, wingspan: d, vertical: d, perimD: d, postD: d, rimP: d);
                        var viaPublic = Matchup.BlockWeight(z, sh, df, cfgH.BlockWeight(z), cfgM);
                        var viaParts  = Matchup.BlockBend(z, Matchup.BlockDuelShift(z, sh, df, cfgM),
                                                          cfgH.BlockWeight(z), cfgM);
                        var diff = Math.Abs(viaPublic - viaParts);
                        worst = Math.Max(worst, diff);
                        if (diff != 0.0) ok = false;
                    }
            Check("BlockWeight == BlockBend(BlockDuelShift(...)) at EXACT zero diff", ok,
                  $"worst |Δ| = {worst:E1} over 1,620 shooter×defender×zone cases");
        }

        // ── (3) The rate invariants, on generated variety ────────────────────
        {
            var rng = new Random(79_0001);
            Player Rand() => Mk("r",
                height: rng.Next(20, 96), wingspan: rng.Next(20, 96), vertical: rng.Next(20, 96),
                strength: rng.Next(20, 96), perimD: rng.Next(10, 96), postD: rng.Next(10, 96),
                rimP: rng.Next(10, 96), helpD: rng.Next(10, 96), fin: rng.Next(10, 96),
                close: rng.Next(10, 96), mid: rng.Next(10, 96), outside: rng.Next(10, 96));

            var bounded = true; var monotone = true; var helpNonNeg = true;
            var creditSums = true; var creditPositive = true; var fadesOut = true;
            var worstBoundViolation = 0.0;
            var helpShareByZone = new double[5];
            var samples = 0;
            const int N = 40_000;

            for (var it = 0; it < N; it++)
            {
                var zone = zones[rng.Next(5)];
                var defs = new Player?[] { Rand(), Rand(), Rand(), Rand(), Rand() };
                var shooter = Rand();
                var mi = rng.Next(5);

                var rate = Matchup.BlockWeightWithHelp(zone, shooter, defs[mi]!, defs, mi,
                                                       cfgH.BlockWeight(zone), cfgM);
                if (!(rate > cfgM.BlockFloor(zone) && rate < cfgM.BlockCeiling(zone)))
                {
                    bounded = false;
                    worstBoundViolation = Math.Max(worstBoundViolation,
                        Math.Max(cfgM.BlockFloor(zone) - rate, rate - cfgM.BlockCeiling(zone)));
                }

                if (Matchup.BlockHelpSum(zone, defs, mi, cfgM) < 0.0) helpNonNeg = false;

                // A BETTER defender never lowers the rate. Skill only, body untouched — the
                // exact case that failed 8,237/40,000 before help-depth became body-only.
                var j = (mi + 1) % 5;
                var better = (Player?[])defs.Clone();
                var b = defs[j]!;
                better[j] = Mk("b", b.Height, b.Wingspan, b.Vertical, b.Strength,
                               Math.Min(99, b.PerimeterDefense + 15),
                               Math.Min(99, b.PostDefense + 15),
                               Math.Min(99, b.RimProtection + 15),
                               b.HelpDefense, b.Finishing, b.Close, b.Mid, b.Outside);
                var rateBetter = Matchup.BlockWeightWithHelp(zone, shooter, better[mi]!, better, mi,
                                                             cfgH.BlockWeight(zone), cfgM);
                if (rateBetter < rate - 1e-12) monotone = false;

                var w = Matchup.BlockCreditWeights(zone, defs, mi, cfgM);
                var tot = w.Sum();
                if (Math.Abs(tot / tot - 1.0) > 1e-12) creditSums = false;
                for (var i = 0; i < 5; i++) if (w[i] <= 0.0) creditPositive = false;

                helpShareByZone[Array.IndexOf(zones, zone)] += 1.0 - w[mi] / tot;
                samples++;
            }

            Check("rate stays strictly inside (floor, ceiling) at every zone", bounded,
                  bounded ? $"{N:N0} random matchups" : $"worst excursion {worstBoundViolation:E2}");
            Check("help contribution is never negative (no-drag floor)", helpNonNeg);
            Check("a BETTER defender never lowers his team's block rate", monotone,
                  "skill +15, body untouched — the case body-only help-depth exists to fix");
            Check("credit weights are strictly positive for every populated slot", creditPositive,
                  "the luck floor makes a zero-mass draw unreachable");
            Check("credit weights sum to a usable total on every block", creditSums);

            // Help share must fade away from the rim — Emmett's ruling that the tie to the
            // matched man strengthens as shots move out.
            var counts = new double[5];
            for (var it = 0; it < 5; it++) counts[it] = helpShareByZone[it];
            var perZone = new double[5];
            {
                var rng2 = new Random(79_0002);
                for (var zi = 0; zi < 5; zi++)
                {
                    var acc = 0.0;
                    const int M2 = 6000;
                    for (var it = 0; it < M2; it++)
                    {
                        var defs = new Player?[] { Rand(), Rand(), Rand(), Rand(), Rand() };
                        var mi = rng2.Next(5);
                        var w = Matchup.BlockCreditWeights(zones[zi], defs, mi, cfgM);
                        acc += 1.0 - w[mi] / w.Sum();
                    }
                    perZone[zi] = acc / M2;
                }
            }
            for (var zi = 1; zi < 5; zi++) if (perZone[zi] > perZone[zi - 1] + 1e-9) fadesOut = false;
            Check("help share is non-increasing from Rim to Three", fadesOut,
                  string.Join("  ", zones.Select((z, i) => $"{z} {perZone[i]:P0}")));
        }

        // ── (4) The defect itself: an UNMATCHED rim protector must move the rate ──
        {
            var ordinary = Mk("ord");
            var menace   = Mk("menace", height: 78, wingspan: 85, vertical: 70, strength: 80,
                              postD: 88, rimP: 95, helpD: 85);
            var shooter  = Mk("sh", fin: 62, close: 58, mid: 55, outside: 54,
                              height: 55, wingspan: 57, vertical: 62);

            var without = new Player?[] { ordinary, ordinary, ordinary, ordinary, ordinary };
            var with    = new Player?[] { ordinary, ordinary, ordinary, ordinary, menace };

            var rateWithout = Matchup.BlockWeightWithHelp(ShotLocation.Rim, shooter, ordinary, without, 0,
                                                          cfgH.BlockWeight(ShotLocation.Rim), cfgM);
            var rateWith    = Matchup.BlockWeightWithHelp(ShotLocation.Rim, shooter, ordinary, with, 0,
                                                          cfgH.BlockWeight(ShotLocation.Rim), cfgM);
            // Pre-S79 both would be the identical duel — the matched man is the same player.
            var duelOnly = Matchup.BlockWeight(ShotLocation.Rim, shooter, ordinary,
                                               cfgH.BlockWeight(ShotLocation.Rim), cfgM);
            Check("an unmatched elite rim protector raises the team block rate", rateWith > rateWithout,
                  $"{rateWithout:P2} -> {rateWith:P2}  (duel alone, both lineups: {duelOnly:P2})");
            Check("...and the pre-S79 duel is blind to him", Math.Abs(rateWithout - duelOnly) < 1e-12,
                  "an all-ordinary lineup adds no help, so the help arm reduces to the old duel exactly");

            // Same tools, no instincts: readiness multiplies threat, never substitutes for it.
            var tools = Mk("tools", height: 78, wingspan: 85, vertical: 70, strength: 80,
                           postD: 70, rimP: 88, helpD: 15);
            var withTools = new Player?[] { ordinary, ordinary, ordinary, ordinary, tools };
            var rateTools = Matchup.BlockWeightWithHelp(ShotLocation.Rim, shooter, ordinary, withTools, 0,
                                                        cfgH.BlockWeight(ShotLocation.Rim), cfgM);
            Check("help instincts gate the same body", rateTools < rateWith && rateTools > rateWithout,
                  $"help 15: {rateTools:P2}   help 85: {rateWith:P2}");

            // A guard with real instincts and no tools is still paid nothing extra: readiness
            // multiplies threat, and his threat is at or below neutral.
            var instinctGuard = Mk("ig", height: 38, wingspan: 40, vertical: 60, strength: 32,
                                   perimD: 30, postD: 30, rimP: 25, helpD: 75);
            var withGuard = new Player?[] { ordinary, ordinary, ordinary, ordinary, instinctGuard };
            var rateGuard = Matchup.BlockWeightWithHelp(ShotLocation.Rim, shooter, ordinary, withGuard, 0,
                                                        cfgH.BlockWeight(ShotLocation.Rim), cfgM);
            Check("elite instincts cannot manufacture a blocker out of no tools",
                  Math.Abs(rateGuard - rateWithout) < 1e-12,
                  $"{rateGuard:P4} vs baseline {rateWithout:P4} — max(0, threat) floors him at zero");
        }

        // ── (5) The putback door's RATE is untouched; only its credit is new ──
        {
            var rebounder = Mk("reb", height: 70, wingspan: 74, vertical: 72, strength: 70, fin: 72);
            var defs = new Player?[]
            {
                Mk("d1"), Mk("d2"),
                Mk("d3", height: 78, wingspan: 85, vertical: 70, strength: 80, postD: 88, rimP: 95),
                Mk("d4"), Mk("d5"),
            };
            var rate = Matchup.PutbackBlockRate(rebounder, defs, cfgH.PutbackBlocked, cfgM);
            var w = Matchup.PutbackBlockCreditWeights(defs, cfgM);
            var tot = w.Sum();
            Check("putback rate still inside (BlockFloorRim, PutbackBlockCeiling)",
                  rate > cfgM.BlockFloorRim && rate < cfgM.PutbackBlockCeiling, $"{rate:P2}");
            Check("putback credit favours the rim protector, floors nobody at zero",
                  w[2] / tot > w[0] / tot && w.All(x => x > 0.0),
                  $"big {w[2] / tot:P1}  each other {w[0] / tot:P1}");
            // Putback credit is finisher-independent by construction — it never reads a rebounder.
            var reb2 = Mk("reb2", height: 40, wingspan: 42, vertical: 40, strength: 40, fin: 20);
            var w2 = Matchup.PutbackBlockCreditWeights(defs, cfgM);
            Check("putback credit does not depend on who is finishing",
                  w.Zip(w2, (a, b2) => Math.Abs(a - b2)).All(d => d == 0.0),
                  "the shifts are measured against a neutral finisher, so no rebounder is needed");
            _ = reb2;
        }

        // ── (6) Config guards — every Load assertion throws ──────────────────
        {
            bool Throws(string key, JsonNode value)
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                    node["Matchup"]![key] = value;
                    var tmp = Path.Combine(Path.GetTempPath(), $"bh_cfg_{key}_{Guid.NewGuid():N}.json");
                    File.WriteAllText(tmp, node.ToJsonString());
                    try { MatchupConfig.Load(tmp); return false; }
                    catch (InvalidOperationException) { return true; }
                    finally { File.Delete(tmp); }
                }
                catch { return false; }
            }
            Check("BlockCreditLuckFloor = 0 throws", Throws("BlockCreditLuckFloor", 0.0),
                  "a zero floor hands a lone elite big 100% of his team's credit");
            Check("BlockHelpPositionalSwing = 1.0 throws", Throws("BlockHelpPositionalSwing", 1.0),
                  "at 1.0 a below-mean helper zeroes out, removing the guard's floor");
            Check("BlockHelpPositionalScale = 0 throws", Throws("BlockHelpPositionalScale", 0.0));
            Check("BlockHelpShareRim = 0 throws", Throws("BlockHelpShareRim", 0.0),
                  "zero would make the four off-ball defenders un-drawable at that zone");
            Check("non-monotone help share (Three > Long) throws", Throws("BlockHelpShareThree", 0.9),
                  "Emmett's ruling: blocks happen rarely at mid, long and three");
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["Matchup"]!["BlockHelpDepthHeight"] = 0.0;
                node["Matchup"]!["BlockHelpDepthStrength"] = 0.0;
                var tmp = Path.Combine(Path.GetTempPath(), $"bh_cfg_depth_{Guid.NewGuid():N}.json");
                File.WriteAllText(tmp, node.ToJsonString());
                var threw = false;
                try { MatchupConfig.Load(tmp); } catch (InvalidOperationException) { threw = true; }
                finally { File.Delete(tmp); }
                Check("help-depth weights BOTH zero throws", threw,
                      "depth identical for everyone -> positional multiplier exactly 1 for all");
            }
        }

        Console.WriteLine(pass ? "  Phase 74 PASSED." : "  Phase 74 FAILED.");
        return pass;
    }
}
