using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 54 — the national pool + prestige-weighted divvy, SPLIT at Session 63.
//
// The pool is now the REAL Pass-2 skill-first cohort (positions by exact-count
// orientation rank, roles at the old pool's density — Emmett rulings 2026-07-20).
// KEPT from the S29 suite: draft determinism, the order-free board-noise
// fixtures (oracle exports, still bit-valid — the noise stream is untouched),
// roster legality, the infeasibility throws (rigged from REAL pool rows, so
// they now prove rejection against the new distribution), the scout-rank
// formula fixtures + convexity + monotonicity (formula-level, pool-independent),
// prestige-as-access, protected supply, and the 29.1 fair-scouting checks.
// RETIRED: every old-pool-SHAPE assertion (leg-count apportionment, gradient
// tiers, tier ordering/overlap — that authored distribution no longer exists).
// ADDED: the new pool's own guards — exact 4n/3n/3n position counts, the
// orientation BOUNDARY invariants, role counts == the ruling-0.2 density target
// and >= the quota floor, and byte-identical pool determinism.
// What a green block proves: wiring correctness, determinism, and the legality
// invariants — NOT that the cohort is basketball truth (the S63 baseline read
// is the instrument for that, page-only).
// ============================================================================

internal static partial class Program
{
    private static bool Phase54DivvyCheck()
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 54 — National pool + prestige divvy (S63: the Pass-2 cohort bridge) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            // The two worlds: the tiny fixture (n=20 — small-N is proven explicitly,
            // never assumed to be a miniature stock world) and the stock world built
            // from the committed reference csvs beside the binary (the Phase 53 path).
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
            var tiny = LoadWorld(fixturePath);
            var stock = ConvertWorld(
                Path.Combine(AppContext.BaseDirectory, "data", "teams.csv"),
                Path.Combine(AppContext.BaseDirectory, "data", "conf.csv"));
            const long seed = 20260702;

            // ── 1. Determinism: the divvy is a pure function of (world, seed). ─────────
            var tinyA = RunDivvyDraft(tiny, seed);
            var tinyB = RunDivvyDraft(tiny, seed);
            var stockA = RunDivvyDraft(stock, seed);
            var stockB = RunDivvyDraft(stock, seed);
            bool SameDraft(DivvyResult x, DivvyResult y) =>
                x.Picks.Count == y.Picks.Count &&
                x.Picks.Zip(y.Picks).All(p => p.First.SchoolId == p.Second.SchoolId && p.First.PoolId == p.Second.PoolId);
            Check("fixture: same seed twice -> identical draft", SameDraft(tinyA, tinyB));
            Check("stock: same seed twice -> identical draft", SameDraft(stockA, stockB));
            var tinyC = RunDivvyDraft(tiny, seed + 1);
            Check("fixture: different seed -> different draft", !SameDraft(tinyA, tinyC));

            // Session 63: the pool itself is byte-identical under the same seed —
            // every position, every role, every rating, orientation, and rank.
            bool SamePool(DivvyResult x, DivvyResult y) =>
                x.Pool.Count == y.Pool.Count &&
                x.Pool.Zip(y.Pool).All(p =>
                    p.First.PoolId == p.Second.PoolId &&
                    p.First.Pos == p.Second.Pos &&
                    p.First.Role == p.Second.Role &&
                    p.First.Oaxis == p.Second.Oaxis &&
                    p.First.Weapon == p.Second.Weapon &&
                    p.First.ScoutRank == p.Second.ScoutRank &&
                    p.First.Ratings.Count == p.Second.Ratings.Count &&
                    p.First.Ratings.All(kv =>
                        p.Second.Ratings.TryGetValue(kv.Key, out var v2) && v2 == kv.Value));
            Check("fixture: same seed twice -> byte-identical pool (pos/role/ratings/rank/orientation)",
                SamePool(tinyA, tinyB));
            Check("stock: same seed twice -> byte-identical pool", SamePool(stockA, stockB));

            // ── 2. Board noise: stable, order-free, matches the oracle bit-for-bit. ────
            Check("noise fixture (20260702, 1, 0)",
                Math.Abs(DivvyNoiseU(20260702, 1, 0) - (-0.76927554037784052)) < 1e-15);
            Check("noise fixture (20260702, 347, 3469)",
                Math.Abs(DivvyNoiseU(20260702, 347, 3469) - (-0.14319460549815766)) < 1e-15);
            Check("noise fixture (99, 42, 1000)",
                Math.Abs(DivvyNoiseU(99, 42, 1000) - (-0.56199114673984529)) < 1e-15);
            var forward = new List<double>();
            for (var s = 1; s <= 3; s++) for (var p = 0; p < 40; p++) forward.Add(DivvyNoiseU(seed, s, p));
            // reading any (school, player) pair again, in reversed traversal order,
            // must yield the identical value — the noise is a coherent alternate
            // board, never a per-pick reroll
            var orderFree = true;
            for (var s = 3; s >= 1; s--)
                for (var p = 39; p >= 0; p--)
                    if (DivvyNoiseU(seed, s, p) != forward[(s - 1) * 40 + p]) orderFree = false;
            Check("noise is read-order independent (revisit any pair -> identical)", orderFree);

            // ── 3. Every roster legal, at both scales. ─────────────────────────────────
            foreach (var (tag, res) in new[] { ("fixture", tinyA), ("stock", stockA) })
            {
                var pool = res.Pool;
                var all = res.Rosters.Values.SelectMany(r => r).ToList();
                Check($"{tag}: every roster exactly 10", res.Rosters.Values.All(r => r.Count == 10));
                Check($"{tag}: every pool player drafted exactly once",
                    all.Count == pool.Count && all.Distinct().Count() == pool.Count);
                Check($"{tag}: every roster 4G/3W/3B", res.Rosters.Values.All(r =>
                    r.Count(pid => pool[pid].Pos == "G") == 4 &&
                    r.Count(pid => pool[pid].Pos == "W") == 3 &&
                    r.Count(pid => pool[pid].Pos == "B") == 3));
                Check($"{tag}: every roster covered (lead handler + wing defender + interior bodies)",
                    res.Rosters.Values.All(r =>
                        r.Any(pid => GenLeadRoles.Contains(pool[pid].Role)) &&
                        r.Any(pid => pool[pid].Role == GenWingDefenderRole)));
            }
            Check("fixture: every drafted player passes Player.Validate()",
                tinyA.Pool.All(p => p.Player.Validate().Count == 0));

            // ── 4. The Session 63 pool guards: exact counts, orientation boundaries,
            //       role density (target AND floor). The old apportionment/leg-tier
            //       assertions are RETIRED — that authored shape no longer exists. ──────
            foreach (var (tag, res) in new[] { ("fixture", tinyA), ("stock", stockA) })
            {
                var pool = res.Pool;
                var n = res.Rosters.Count;
                Check($"{tag} pool: positions by EXACT COUNT (4n/3n/3n)",
                    pool.Count(p => p.Pos == "G") == 4 * n &&
                    pool.Count(p => p.Pos == "W") == 3 * n &&
                    pool.Count(p => p.Pos == "B") == 3 * n,
                    $"{pool.Count(p => p.Pos == "G")}G/{pool.Count(p => p.Pos == "W")}W/{pool.Count(p => p.Pos == "B")}B");

                // Orientation boundaries (ruling 0.1, A2-resolved direction: +1 = post):
                // every Big at least as interior as every Wing, every Wing at least as
                // interior as every Guard. Non-strict — equal-Oaxis players may straddle
                // a cut (the cohort-index tiebreak decides which side they land on).
                var maxG = pool.Where(p => p.Pos == "G").Max(p => p.Oaxis);
                var minW = pool.Where(p => p.Pos == "W").Min(p => p.Oaxis);
                var maxW = pool.Where(p => p.Pos == "W").Max(p => p.Oaxis);
                var minB = pool.Where(p => p.Pos == "B").Min(p => p.Oaxis);
                Check($"{tag} pool: orientation boundaries monotone (B >= W >= G, post-positive)",
                    minB >= maxW && minW >= maxG,
                    FormattableString.Invariant($"G max {maxG:F4} <= W [{minW:F4}..{maxW:F4}] <= B min {minB:F4}"));

                // Roles at the old pool's density (ruling 0.2): count == target, >= floor.
                var quota = (int)Math.Ceiling(DivvyRoleHeadroom * n);
                var lead = pool.Count(p => GenLeadRoles.Contains(p.Role));
                var tdw = pool.Count(p => p.Role == GenWingDefenderRole);
                Check($"{tag} pool: lead handlers == old-density target and >= quota floor",
                    lead == DivvyLeadRoleTarget(n) && lead >= quota,
                    $"{lead} (target {DivvyLeadRoleTarget(n)}, floor {quota})");
                Check($"{tag} pool: wing defenders == old-density target and >= quota floor",
                    tdw == DivvyTdwRoleTarget(n) && tdw >= quota,
                    $"{tdw} (target {DivvyTdwRoleTarget(n)}, floor {quota})");
            }

            // ── 4b. Session 66: the recruiting line at the bridge. ─────────────────────
            // Every drafted player clears the generator's own line; the census stays
            // exact; and the CONTRACT is proven, not just same-seed reproducibility —
            // prefix stability means the growth chunk size is invisible, so two
            // different initial chunk sizes must yield the IDENTICAL ordered pool.
            {
                var rlSeed = unchecked((int)(seed ^ DivvyCohortSeedXor));
                const int rlP = 3470;
                var recA = BuildRecruitedCohort(rlSeed, rlP, 2 * rlP);   // no doubling at ~56% acceptance
                var recB = BuildRecruitedCohort(rlSeed, rlP, 4000);      // forces >=1 doubling (4000 draws accept ~2,240)
                Check("recruited cohort: exactly 10n accepted", recA.Length == rlP, $"{recA.Length}");
                Check("recruited cohort: every accepted player clears the line",
                    recA.All(lp => lp.Result.Rscore >= PlayerGenPass2.R_LINE),
                    $"Rscore >= {PlayerGenPass2.R_LINE:F1}");
                Check("recruited cohort: two growth chunk sizes -> identical ordered pool (prefix stability)",
                    recA.Length == recB.Length && recA.Zip(recB).All(p =>
                        p.First.Result.Rscore == p.Second.Result.Rscore &&
                        p.First.Result.Height == p.Second.Result.Height &&
                        p.First.Result.Oaxis == p.Second.Result.Oaxis &&
                        p.First.Result.Weapon == p.Second.Result.Weapon));
                // Draw order preserved: the accepted pool equals the first-past-the-line
                // subsequence of the raw stream, independently refiltered.
                var raw = PlayerGenPass2Live.BuildCohort(rlSeed, 16000);
                var refiltered = raw.Where(lp => lp.Result.Rscore >= PlayerGenPass2.R_LINE)
                                    .Take(rlP).ToArray();
                Check("recruited cohort: accepted draw order preserved (matches an independent refilter)",
                    refiltered.Length == rlP && recA.Zip(refiltered).All(p =>
                        p.First.Result.Rscore == p.Second.Result.Rscore &&
                        p.First.Result.Height == p.Second.Result.Height &&
                        p.First.Result.Oaxis == p.Second.Result.Oaxis &&
                        p.First.Result.Weapon == p.Second.Result.Weapon));
            }

            // ── 5. An infeasible pool is rejected LOUDLY, naming the shortfall,
            //       before the first pick (never a stranded team at pick 3,400). ────────
            var rigged = new List<PoolPlayer>(tinyA.Pool);
            var lastBig = rigged.FindLastIndex(p => p.Pos == "B");
            rigged[lastBig] = rigged[lastBig] with { Pos = "G" };   // 81G / 60W / 59B
            var posMsg = "";
            try { ValidateDivvyPool(rigged, 20); }
            catch (InvalidOperationException ex) { posMsg = ex.Message; }
            Check("rigged positional quota rejected, naming the shortfall",
                posMsg.Contains("DIVVY INFEASIBLE") && posMsg.Contains("shortfall"), posMsg);
            var rigged2 = tinyA.Pool.Select(p =>
                GenLeadRoles.Contains(p.Role) ? p with { Role = "Slasher" } : p).ToList();
            var roleMsg = "";
            try { ValidateDivvyPool(rigged2, 20); }
            catch (InvalidOperationException ex) { roleMsg = ex.Message; }
            Check("rigged lead-handler supply rejected, naming the quota",
                roleMsg.Contains("DIVVY INFEASIBLE") && roleMsg.Contains("lead-handler"), roleMsg);

            // ── 6. The scout rank: oracle fixtures, convexity, monotonicity, ordering. ─
            Check("rank fixture (76,76,40) = 164.900000", Math.Abs(DivvyRankFromLegs(40, 76, 76) - 164.9) < 1e-9);
            Check("rank fixture (76,76,50) = 182.400000", Math.Abs(DivvyRankFromLegs(50, 76, 76) - 182.4) < 1e-9);
            Check("rank fixture (76,76,63) = 218.605000", Math.Abs(DivvyRankFromLegs(63, 76, 76) - 218.605) < 1e-9);
            Check("rank fixture (77,76,76) = 271.020000", Math.Abs(DivvyRankFromLegs(76, 76, 77) - 271.02) < 1e-9);
            Check("rank fixture (76,51,47.7) = 155.078050", Math.Abs(DivvyRankFromLegs(47.7, 51, 76) - 155.07805) < 1e-9);
            // convexity: per-point rank delta strictly increasing across the gradient
            // bands (the third leg's every ounce is worth more the more you have)
            var d1 = (DivvyRankFromLegs(45, 76, 76) - DivvyRankFromLegs(34, 76, 76)) / 11.0;
            var d2 = (DivvyRankFromLegs(55, 76, 76) - DivvyRankFromLegs(46, 76, 76)) / 9.0;
            var d3 = (DivvyRankFromLegs(70, 76, 76) - DivvyRankFromLegs(56, 76, 76)) / 14.0;
            Check("rank convex in the third leg (per-point deltas strictly increase)",
                d1 < d2 && d2 < d3, $"{d1:F3} < {d2:F3} < {d3:F3}");
            // monotone in every rating that feeds it (spot check on generated players)
            var mono = true;
            foreach (var p in tinyA.Pool.Take(20))
            {
                var baseRank = DivvyScoutRank(p.Ratings, p.Pos);
                foreach (var rt in p.Ratings.Keys.ToList())
                {
                    if (GenPermittedHoles[p.Pos].Contains(rt)) continue;
                    p.Ratings[rt] += 1;
                    if (DivvyScoutRank(p.Ratings, p.Pos) < baseRank - 1e-12) mono = false;
                    p.Ratings[rt] -= 1;
                }
            }
            Check("rank monotone in every feeding rating (20-player spot check)", mono);
            // (The S29 leg-tier group ordering/overlap assertions are RETIRED with the
            //  tiers themselves — the S63 pool has no authored tier structure to order.)

            // ── 7. Access sanity on the fixed seed: prestige buys cracks, not players. ─
            var prestige = stock.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
            var ranks = stockA.Pool.Select(p => p.ScoutRank).ToArray();
            var topDecile = new HashSet<int>(
                Enumerable.Range(0, ranks.Length).OrderByDescending(i => ranks[i]).Take(ranks.Length / 10));
            var bySchool = stock.Schools.OrderByDescending(s => s.CurrentPrestige).ToList();
            var dec = bySchool.Count / 10;
            var topMean = bySchool.Take(dec).SelectMany(s => stockA.Rosters[s.Id]).Average(pid => ranks[pid]);
            var botMean = bySchool.TakeLast(dec).SelectMany(s => stockA.Rosters[s.Id]).Average(pid => ranks[pid]);
            Check("stock: top-prestige-decile mean drafted rank > bottom decile",
                topMean > botMean, $"{topMean:F1} > {botMean:F1}");
            var med = stock.Schools.Select(s => s.CurrentPrestige).OrderBy(x => x).ElementAt(stock.Schools.Count / 2);
            var leaks = stockA.Rosters.Where(kv => prestige[kv.Key] < med)
                                      .Sum(kv => kv.Value.Count(pid => topDecile.Contains(pid)));
            Check("stock: at least one top-decile player leaks below median prestige",
                leaks >= 1, $"leaks = {leaks} (recruited pool at this seed: 64)");

            // ── 8. Protected supply + the opening five's contract. ─────────────────────
            // The coverage guarantee is a hard constraint: remaining supply of a
            // protected role never dips below remaining unmet obligations, at any
            // point of any draft (the adversarial greedy-grab is blocked by legality —
            // oracle-proven under a rigged board; asserted here on live drafts).
            var slackOk = tinyA.MinSlackLead >= 0 && tinyA.MinSlackTdw >= 0
                       && stockA.MinSlackLead >= 0 && stockA.MinSlackTdw >= 0;
            for (long s = 1000; s < 1005; s++)
            {
                var r = RunDivvyDraft(tiny, s);
                if (r.MinSlackLead < 0 || r.MinSlackTdw < 0) slackOk = false;
                if (!r.Rosters.Values.All(ro =>
                        ro.Any(pid => GenLeadRoles.Contains(r.Pool[pid].Role)) &&
                        ro.Any(pid => r.Pool[pid].Role == GenWingDefenderRole))) slackOk = false;
            }
            Check("protected-supply slack never negative (both worlds + 5 fixture seeds)", slackOk);

            // ── 9. Session 29.1 — fair scouting + the opening five's playable floor. ───
            // 9a. FT-blindness: the rank is bit-identical under any FreeThrow value
            //     (a generated player's real dict; == on the doubles, no tolerance).
            var ftp = tinyA.Pool[0];
            var savedFt = ftp.Ratings["FreeThrow"];
            ftp.Ratings["FreeThrow"] = 25;
            var rankFt25 = DivvyScoutRank(ftp.Ratings, ftp.Pos);
            ftp.Ratings["FreeThrow"] = 95;
            var rankFt95 = DivvyScoutRank(ftp.Ratings, ftp.Pos);
            ftp.Ratings["FreeThrow"] = savedFt;
            Check("rank is FreeThrow-blind (FT=25 vs FT=95 bit-identical)", rankFt25 == rankFt95);

            // 9b. Full-pipeline per-position fixtures — fixed hand-specified dicts,
            //     oracle constants to 1e-9. One read proves size map + big-ATH
            //     add-back + FT exclusion, per position. (The formula fixtures in §6
            //     feed leg means directly and would stay green without the wiring —
            //     these three go through DivvyScoutRank itself.)
            Dictionary<string, int> FixtureDict(int sizeBase, int athBase, int skillBase, int ft)
            {
                var d = new Dictionary<string, int>(StringComparer.Ordinal);
                var sizeR = new[] { "Height", "Wingspan", "Weight", "OffensiveRebounding", "DefensiveRebounding" };
                var athR = new[] { "Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance", "Hustle" };
                var skillR = new[] { "Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
                                     "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
                                     "OffBallMovement", "Screening", "PerimeterDefense", "PostDefense",
                                     "RimProtection", "Steals", "HelpDefense", "OffBallDefense",
                                     "BasketballIQ", "Discipline" };
                for (var i = 0; i < sizeR.Length; i++) d[sizeR[i]] = sizeBase + i;
                for (var i = 0; i < athR.Length; i++) d[athR[i]] = athBase + i;
                for (var i = 0; i < skillR.Length; i++) d[skillR[i]] = skillBase + (i % 5);
                d["FreeThrow"] = ft;
                return d;
            }
            Check("full-pipeline rank fixture B = 164.551060606...",
                Math.Abs(DivvyScoutRank(FixtureDict(80, 40, 55, 30), "B") - 164.5510606060606) < 1e-9);
            Check("full-pipeline rank fixture G = 200.777786458...",
                Math.Abs(DivvyScoutRank(FixtureDict(46, 74, 70, 88), "G") - 200.7777864583333) < 1e-9);
            Check("full-pipeline rank fixture W = 186.946666666...",
                Math.Abs(DivvyScoutRank(FixtureDict(58, 62, 66, 50), "W") - 186.94666666666666) < 1e-9);

            // 9c. Post access at the fixed seed: elite programs get their pick of
            //     the post litter — top-prestige-decile mean drafted-big rank
            //     strictly above bottom decile (oracle at this seed: 182.3 vs 154.0).
            var bigRanksTop = bySchool.Take(dec).SelectMany(s => stockA.Rosters[s.Id])
                .Where(pid => stockA.Pool[pid].Pos == "B").Select(pid => ranks[pid]).ToList();
            var bigRanksBot = bySchool.TakeLast(dec).SelectMany(s => stockA.Rosters[s.Id])
                .Where(pid => stockA.Pool[pid].Pos == "B").Select(pid => ranks[pid]).ToList();
            Check("stock: top-prestige-decile mean drafted-BIG rank > bottom decile",
                bigRanksTop.Average() > bigRanksBot.Average(),
                $"{bigRanksTop.Average():F1} > {bigRanksBot.Average():F1}");

            // 9d. The seating floor (amended 30.1): every roster's opening five
            //     (both worlds) has >= 1 B, >= 2 G, and >= 1 W; deterministic;
            //     rank-blind by signature (inputs: acquisition order + positions);
            //     equals the raw first five whenever that five already satisfies
            //     the quotas.
            var floorOk = true; var rawEqOk = true; var rawLegalSeen = 0;
            foreach (var res in new[] { tinyA, stockA })
            {
                foreach (var roster in res.Rosters.Values)
                {
                    var five = BuildOpeningFive(roster, pid => res.Pool[pid].Pos);
                    var b = five.Count(pid => res.Pool[pid].Pos == "B");
                    var g = five.Count(pid => res.Pool[pid].Pos == "G");
                    var w = five.Count(pid => res.Pool[pid].Pos == "W");
                    if (five.Length != 5 || b < 1 || g < 2 || w < 1) floorOk = false;
                    var raw = roster.Take(5).ToArray();
                    if (raw.Count(pid => res.Pool[pid].Pos == "B") >= 1 &&
                        raw.Count(pid => res.Pool[pid].Pos == "G") >= 2 &&
                        raw.Count(pid => res.Pool[pid].Pos == "W") >= 1)
                    {
                        rawLegalSeen++;
                        if (!five.SequenceEqual(raw)) rawEqOk = false;
                    }
                }
            }
            Check("opening five: >= 1 B, >= 2 G, and >= 1 W on every roster, both worlds", floorOk);
            Check("opening five: equals raw first five whenever raw already satisfies the quotas",
                rawEqOk && rawLegalSeen > 0, $"raw-legal rosters seen: {rawLegalSeen}");
            var anyRoster = tinyA.Rosters.Values.First();
            var five1 = BuildOpeningFive(anyRoster, pid => tinyA.Pool[pid].Pos);
            var five2 = BuildOpeningFive(anyRoster, pid => tinyA.Pool[pid].Pos);
            Check("opening five deterministic (same roster twice -> identical five)",
                five1.SequenceEqual(five2));
        }
        catch (Exception ex)
        {
            Check("Phase 54 completed without an unexpected exception", false, ex.Message);
        }

        Console.WriteLine(pass ? "  Phase 54: PASS" : "  Phase 54: FAIL");
        return pass;
    }
}
