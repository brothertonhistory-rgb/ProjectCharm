using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 54 — Roster Genesis Pass 1.5: national pool + prestige-weighted divvy.
//
// Every numeric constant asserted below was EXPORTED BY THE PYTHON ORACLE
// (S29 oracle run, 260 seeded drafts green at n=347 and n=20). The pattern is
// S23/S28's: a wrong formula fails the constant, wrong wiring fails the
// cross-read. What a green block proves: wiring correctness, formula fidelity
// to the oracle, determinism, and the legality invariants — NOT that the pool
// magnitudes are basketball truth (placeholders by design, tuned at burn-in).
// ============================================================================

internal static partial class Program
{
    private static bool Phase54DivvyCheck()
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 54 — Roster Genesis Pass 1.5 (national pool + prestige divvy) ==");
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

            // ── 4. The pool's shape matches the oracle's apportionment constants. ──────
            // (hierarchical largest-remainder: top-level mix over the pool, gradient
            // tiers over the realized two-leg count — canonical-order ties)
            var top347 = DivvyApportion(3470, new[] { DivvyThreeLegFrac, DivvyTwoLegFrac, DivvyOneLegFrac });
            var grad347 = DivvyApportion(top347[1], new[] { DivvyBorderlineFrac, DivvyUsefulFrac, DivvyScarceFrac });
            Check("apportionment n=347: 28 / 1110 / 2332 (3/2/1-leg)",
                top347.SequenceEqual(new[] { 28, 1110, 2332 }), string.Join("/", top347));
            Check("apportionment n=347 gradient: 167 / 388 / 555 (borderline/useful/scarce)",
                grad347.SequenceEqual(new[] { 167, 388, 555 }), string.Join("/", grad347));
            var top20 = DivvyApportion(200, new[] { DivvyThreeLegFrac, DivvyTwoLegFrac, DivvyOneLegFrac });
            var grad20 = DivvyApportion(top20[1], new[] { DivvyBorderlineFrac, DivvyUsefulFrac, DivvyScarceFrac });
            Check("apportionment n=20: 2 / 64 / 134", top20.SequenceEqual(new[] { 2, 64, 134 }), string.Join("/", top20));
            Check("apportionment n=20 gradient: 10 / 22 / 32", grad20.SequenceEqual(new[] { 10, 22, 32 }), string.Join("/", grad20));
            foreach (var (tag, res, t, g) in new[] { ("fixture", tinyA, top20, grad20), ("stock", stockA, top347, grad347) })
            {
                var pool = res.Pool;
                Check($"{tag} pool: generated leg counts match apportionment",
                    pool.Count(p => p.LegCount == 3) == t[0] &&
                    pool.Count(p => p.LegCount == 2) == t[1] &&
                    pool.Count(p => p.LegCount == 1) == t[2]);
                Check($"{tag} pool: generated gradient tiers match apportionment",
                    pool.Count(p => p.GradientTier == "borderline") == g[0] &&
                    pool.Count(p => p.GradientTier == "useful") == g[1] &&
                    pool.Count(p => p.GradientTier == "scarce") == g[2]);
                var n = res.Rosters.Count;
                Check($"{tag} pool: positional quotas exact",
                    pool.Count(p => p.Pos == "G") == 4 * n &&
                    pool.Count(p => p.Pos == "W") == 3 * n &&
                    pool.Count(p => p.Pos == "B") == 3 * n);
                var quota = (int)Math.Ceiling(DivvyRoleHeadroom * n);
                Check($"{tag} pool: coverage-role quotas honored",
                    pool.Count(p => GenLeadRoles.Contains(p.Role)) >= quota &&
                    pool.Count(p => p.Role == GenWingDefenderRole) >= quota);
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
            // group ordering (29.1, per Emmett's rulings): three > borderline >
            // useful > scarce strictly on means, one-leg mean below useful — but NO
            // order asserted between scarce and one-leg (fair grading revealed they
            // sit even: two strong legs + one bad leg ≈ one strong leg + two passable
            // ones; a future coach layer bends boards toward its own preference).
            // Overlap: with positions co-located inside each tier, spreads narrowed —
            // true-rank overlap is only robust low on the board (oracle, 60 stock
            // seeds: scarce/one-leg 60/60, useful/scarce 55/60; borderline/useful 0/60,
            // three/borderline 24/60). Asserted: the two robust true-rank overlaps,
            // plus the borderline-useful gap sitting UNDER the board-noise sigma —
            // the tiers separate in truth and blur through scouting error.
            var groups = new[] { "three-leg", "two (borderline)", "two (useful)", "two (scarce)", "one-leg" };
            var ranksByGroup = groups.Select(g =>
                stockA.Pool.Where(p => DivvyGroupOf(p) == g).Select(p => p.ScoutRank).ToList()).ToArray();
            var gm = ranksByGroup.Select(r => r.Average()).ToArray();
            Check("stock pool: means strictly ordered three > borderline > useful > scarce",
                gm[0] > gm[1] && gm[1] > gm[2] && gm[2] > gm[3],
                string.Join(" > ", gm.Take(4).Select(m => m.ToString("F0"))));
            Check("stock pool: one-leg mean below useful mean (scarce vs one-leg deliberately unordered)",
                gm[4] < gm[2], $"one-leg {gm[4]:F1} vs useful {gm[2]:F1} (scarce {gm[3]:F1})");
            Check("stock pool: useful/scarce true-rank overlap",
                ranksByGroup[3].Max() > ranksByGroup[2].Min());
            Check("stock pool: scarce/one-leg true-rank overlap",
                ranksByGroup[4].Max() > ranksByGroup[3].Min());
            var buGap = ranksByGroup[1].Min() - ranksByGroup[2].Max();
            var noiseSigma = stockA.NoiseScale / Math.Sqrt(6.0);
            Check("stock pool: borderline-useful gap under board-noise sigma (tiers blur through scouting error)",
                buGap < noiseSigma, $"gap {buGap:F2} < sigma {noiseSigma:F2}");

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
                leaks >= 1, $"leaks = {leaks} (oracle at this seed: 54)");

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

            // 9d. The seating floor: every roster's opening five (both worlds) has
            //     >= 1 B and >= 2 G; deterministic; rank-blind by signature (inputs:
            //     acquisition order + positions); equals the raw first five whenever
            //     that five already satisfies the quotas.
            var floorOk = true; var rawEqOk = true; var rawLegalSeen = 0;
            foreach (var res in new[] { tinyA, stockA })
            {
                foreach (var roster in res.Rosters.Values)
                {
                    var five = BuildOpeningFive(roster, pid => res.Pool[pid].Pos);
                    var b = five.Count(pid => res.Pool[pid].Pos == "B");
                    var g = five.Count(pid => res.Pool[pid].Pos == "G");
                    if (five.Length != 5 || b < 1 || g < 2) floorOk = false;
                    var raw = roster.Take(5).ToArray();
                    if (raw.Count(pid => res.Pool[pid].Pos == "B") >= 1 &&
                        raw.Count(pid => res.Pool[pid].Pos == "G") >= 2)
                    {
                        rawLegalSeen++;
                        if (!five.SequenceEqual(raw)) rawEqOk = false;
                    }
                }
            }
            Check("opening five: >= 1 B and >= 2 G on every roster, both worlds", floorOk);
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
