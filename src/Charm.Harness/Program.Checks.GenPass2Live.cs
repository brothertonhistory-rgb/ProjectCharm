using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 60 — Pass-2 LIVE generator: sampler moment checks + the 46k statistical
// population audit (C# port Phase 2, S44).
//
// Phase 59 proves the MATH (fixture replay, bit-for-bit). This phase proves the
// DRAWING: (0) each new Sampling.cs sampler matches its closed-form distribution
// moments at N=200k — a swapped Beta α/β, a wrong Gaussian variance, or an
// inverted Exponential λ dies HERE, before any cohort is trusted; then (1)-(2)
// one canonical 46k cohort (seed 20260706) must satisfy the DESIGN-INVARIANT
// BANDS the locked oracle's [A]-[G] audits established.
//
// Bands, not point values — C# System.Random is a different stream from Python's
// random, so the oracle's exact seed-specific numbers are unreproducible BY
// DESIGN and never asserted. Every band below is one a BROKEN wiring would fail
// (the "what breaks it" note rides in each failure message); a band an honest
// generator could plausibly miss on RNG luck has been widened on purpose — a
// false red here is worse than a loose true green. Class variation is legal by
// ruling (S44): e.g. a cohort with ZERO 7'3"+ players is an honest draw
// (canonical count is 2 per 46k); only an EXCESS (hundreds) is a wiring bug.
//
// Style: the Phase-59 gate — loud, throws on failure, the oracle wins.
// ============================================================================
internal static partial class Program
{
    private const int GenPass2LiveSeed = 20260706;   // the oracle's canonical seed
    private const int GenPass2LiveN = 46000;         // the oracle's N_CANDIDATE
    private const int SamplerMomentN = 200_000;

    private static bool Phase60GenPass2LiveCheck()
    {
        Console.WriteLine("\n--- Phase 60: Pass-2 LIVE generator — sampler moments + population audit (C# port Phase 2) ---");
        try
        {
            RunSamplerMomentChecks();
            RunGenPass2PopulationAudit();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    // ── (0) sampler moment checks — run FIRST, before any cohort is trusted ────

    private static void RunSamplerMomentChecks()
    {
        // Each check: N draws, sample mean/variance vs the closed form. Tolerances
        // are ~10+ standard errors at N=200k — generous against RNG luck, far
        // tighter than any real wiring bug (a swapped orientation α/β shifts the
        // Beta mean by 0.108; the tolerance is 0.006).
        void Moments(string name, Func<IRng, double> draw, int seed,
                     double wantMean, double wantVar, double tolMean, double tolVar)
        {
            var rng = new SystemRng(seed);
            double sum = 0, sumSq = 0;
            for (var i = 0; i < SamplerMomentN; i++)
            {
                var x = draw(rng);
                sum += x;
                sumSq += x * x;
            }
            var mean = sum / SamplerMomentN;
            var var_ = sumSq / SamplerMomentN - mean * mean;
            var okM = Math.Abs(mean - wantMean) <= tolMean;
            var okV = Math.Abs(var_ - wantVar) <= tolVar;
            Console.WriteLine($"  sampler {name,-34} mean {mean,8:F4} (want {wantMean:F4} ±{tolMean})   " +
                              $"var {var_,9:F5} (want {wantVar:F5} ±{tolVar})   {(okM && okV ? "OK" : "FAIL")}");
            if (!okM || !okV)
                throw new InvalidOperationException(
                    $"SAMPLER MOMENT FAILURE — {name}: sample mean {mean:F5} / variance {var_:F5} vs " +
                    $"closed-form {wantMean:F5} / {wantVar:F5}. A mis-parameterized or mis-implemented " +
                    "sampler mis-shapes EVERY downstream population figure — fix Sampling.cs before " +
                    "reading any cohort band.");
        }

        // Beta(a,b): mean a/(a+b), var ab/((a+b)²(a+b+1)) — ALL FOUR live parameter
        // pairs, so a bad orientation path can't hide behind a correct Beta(2,2).
        void BetaMoments(string name, double a, double b, int seed)
        {
            var mean = a / (a + b);
            var v = a * b / ((a + b) * (a + b) * (a + b + 1.0));
            Moments($"Beta {name} ({a:F3},{b:F3})",
                    r => Sampling.Betavariate(r, a, b), seed, mean, v, 0.006, 0.003);
        }

        var oriA = PlayerGenPass2.ORI_MEAN * PlayerGenPass2.ORI_CONC;          // 2.007
        var oriB = (1.0 - PlayerGenPass2.ORI_MEAN) * PlayerGenPass2.ORI_CONC;  // 2.493
        BetaMoments("orientation", oriA, oriB, 60_001);
        BetaMoments("skill-q", PlayerGenPass2.SKILL_Q_A, PlayerGenPass2.SKILL_Q_B, 60_002);
        BetaMoments("athletic-q", PlayerGenPass2.ATHQ_A, PlayerGenPass2.ATHQ_B, 60_003);
        BetaMoments("specialization", PlayerGenPass2.SPEC_A, PlayerGenPass2.SPEC_B, 60_004);

        // Normal(μ,σ): mean μ, var σ² — the two live shapes at the spread extremes
        // (wingspan's shifted mean; the FT idiosyncrasy's wide sigma).
        Moments("Gauss wingspan (4.0, 3.0)", r => Sampling.Gauss(r, 4.0, 3.0),
                60_005, 4.0, 9.0, 0.05, 0.30);
        Moments("Gauss ft-idio (0.0, 9.0)", r => Sampling.Gauss(r, 0.0, PlayerGenPass2.FT_SIGMA),
                60_006, 0.0, 81.0, 0.15, 2.50);

        // Exponential(λ): mean 1/λ, var 1/λ² — the live height lower-tail rate.
        var lam = 1.0 / PlayerGenPass2.HT_SCALE_DOWN;
        Moments("Exponential height-tail (λ=1/7)", r => Sampling.Expovariate(r, lam),
                60_007, 7.0, 49.0, 0.15, 3.00);
    }

    // ── (1)-(2) the canonical cohort + design-invariant bands ──────────────────

    private static void RunGenPass2PopulationAudit()
    {
        var coh = PlayerGenPass2Live.BuildCohort(GenPass2LiveSeed, GenPass2LiveN);
        var n = coh.Length;

        void Assert(bool ok, string band, string detail, string breaksIf)
        {
            Console.WriteLine($"  [{band}] {detail}   {(ok ? "OK" : "FAIL")}");
            if (!ok)
                throw new InvalidOperationException(
                    $"POPULATION AUDIT FAILURE — [{band}]: {detail}.\n  What a failure here means: {breaksIf}");
        }

        // [A] schema — all 33 keys on every card, every value in its legal range.
        var missing = 0;
        var outOfRange = 0;
        foreach (var p in coh)
        {
            foreach (var k in PlayerGenPass2.ALL_KEYS)
            {
                if (!p.Result.Card.TryGetValue(k, out var v)) { missing++; continue; }
                var lo = k switch
                {
                    "Height" or "Wingspan" => 40,   // HT_MIN
                    "Weight" => 20,
                    "FreeThrow" => 25,              // FT_MIN
                    _ => 8,                         // HOLE_FLOOR / ath floor
                };
                var hi = k == "FreeThrow" ? 95 : 99;
                if (v < lo || v > hi) outOfRange++;
            }
        }
        Assert(missing == 0 && outOfRange == 0,
            "A", $"schema: {n} players × 33 keys, missing={missing}, out-of-range={outOfRange}",
            "a transform emits a key outside its floor..99 domain — a port bug in card assembly.");

        // [B] orientation lean — perimeter share P(o<0.5) in [0.55, 0.65] (canonical 0.602).
        var perimShare = coh.Count(p => p.Draws.O < 0.5) / (double)n;
        Assert(perimShare is >= 0.55 and <= 0.65,
            "B", $"perimeter share P(o<0.5) = {perimShare:F3} (band 0.55-0.65)",
            "a swapped orientation Beta α/β — the population-scale twin of the sampler moment check.");

        // [E] quality ⊥ Height — |corr(q, Height)| < 0.05 (canonical -0.006). The core
        // architectural claim: quality never touches the body.
        double corr;
        {
            var qs = coh.Select(p => p.Draws.Q).ToArray();
            var hs = coh.Select(p => (double)p.Result.Height).ToArray();
            var mq = qs.Average();
            var mh = hs.Average();
            double sxy = 0, sxx = 0, syy = 0;
            for (var i = 0; i < n; i++)
            {
                var dx = qs[i] - mq;
                var dy = hs[i] - mh;
                sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
            }
            corr = sxy / Math.Sqrt(sxx * syy);
        }
        Assert(Math.Abs(corr) < 0.05,
            "E", $"corr(q, Height) = {corr:+0.000;-0.000} (band |corr| < 0.05)",
            "quality leaking into the body — the skill-first architecture's core claim is broken.");

        // [C] height pyramid — 7'3\"+ (Height >= 93) in [0, 40] per 46k. Canonical is 2;
        // ZERO is a legal honest draw by ruling (classes vary). The failure this catches
        // is EXCESS: a miswired upper tail (e.g. exponential up instead of Gaussian)
        // produces hundreds.
        var giants = coh.Count(p => p.Result.Height >= 93);
        Assert(giants <= 40,
            "C", $"7'3\"+ count (Height>=93) = {giants} (band 0-40 per 46k; canonical 2; zero is legal)",
            "the Gaussian upper height tail is miswired — the bell tail exists to kill 7'3\"+ excess.");

        // [C2] stretch bigs exist / point-centers stay rare — on the RECRUITABLE pool,
        // matching the oracle's [C2] definitions (canonical: 149 stretch bigs, 0 point-centers).
        var rec = coh.Where(p => p.Result.Rscore >= PlayerGenPass2.R_LINE).ToArray();
        var stretch = rec.Count(p => p.Result.Height >= 71
                                     && p.Result.Current["Outside"] >= 55
                                     && p.Result.Current["BallHandling"] < 55);
        var ptCenters = rec.Count(p => p.Result.Height >= 80
                                       && p.Result.Current["BallHandling"] >= 60
                                       && p.Result.Current["Outside"] >= 55);
        Assert(stretch > 0 && ptCenters <= 5,
            "C2", $"stretch bigs = {stretch} (must be >0; canonical 149)   point-centers = {ptCenters} (band <=5; canonical 0)",
            "shooting's orientation-neutrality or ball-handling's perimeter lock is miswired — " +
            "the two must diverge (tall shooters common, tall guard-handles near-impossible).");

        // [F2] specialization is real — mean top1-top2 current-skill gap for spike
        // players (s>0.66) strictly greater than for broad players (s<0.33).
        double MeanTopGap(IEnumerable<PlayerGenPass2Live.LivePlayer> g)
        {
            double total = 0;
            var count = 0;
            foreach (var p in g)
            {
                int top1 = int.MinValue, top2 = int.MinValue;
                foreach (var k in PlayerGenPass2.DRAWN_SKILLS)
                {
                    var v = p.Result.Current[k];
                    if (v > top1) { top2 = top1; top1 = v; }
                    else if (v > top2) top2 = v;
                }
                total += top1 - top2;
                count++;
            }
            return total / count;
        }
        var gapSpike = MeanTopGap(coh.Where(p => p.Draws.S > 0.66));
        var gapBroad = MeanTopGap(coh.Where(p => p.Draws.S < 0.33));
        Assert(gapSpike > gapBroad,
            "F2", $"top1-top2 gap: spike (s>0.66) = {gapSpike:F1}, broad (s<0.33) = {gapBroad:F1} (spike must exceed broad)",
            "the weapon bump/drain is miswired — high specialization must buy one distinct weapon.");

        // [F3] weapon census flatness — no identity exceeds 7% of the cohort, and
        // PostMoves is not materially rarer than OffBallDefense (the S42.1 census-offset
        // fix; without offsets PostMoves sits at 4.15% vs OBD 6.76%). The two shares are
        // near-parity by design (canonical 6.02% vs 5.93%, a 0.09pp gap inside RNG noise),
        // so the band is PostMoves >= OBD - 0.5pp: a broken port misses by 2.6pp; an
        // honest draw can't plausibly miss by 0.5pp (>3 SD). Deliberately widened —
        // asserting strict >= at near-parity would false-red ~30% of honest cohorts.
        var census = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in coh)
            census[p.Result.Weapon] = census.TryGetValue(p.Result.Weapon, out var c) ? c + 1 : 1;
        var maxShareKey = census.MaxBy(kv => kv.Value).Key;
        var maxShare = census[maxShareKey] / (double)n;
        var pmShare = (census.TryGetValue("PostMoves", out var pm) ? pm : 0) / (double)n;
        var obdShare = (census.TryGetValue("OffBallDefense", out var obd) ? obd : 0) / (double)n;
        Assert(maxShare <= 0.07 && pmShare >= obdShare - 0.005,
            "F3", $"census max = {maxShareKey} {maxShare:P2} (band <=7%)   " +
                  $"PostMoves {pmShare:P2} vs OffBallDefense {obdShare:P2} (PostMoves >= OBD - 0.5pp)",
            "the S42.1 weapon-census offsets did not port — the argmax is running on raw bases.");

        // [G] recruitable pool — RScore >= 17 count in [20k, 30k] (canonical 25,736).
        Assert(rec.Length is >= 20_000 and <= 30_000,
            "G", $"recruitable (RScore>={PlayerGenPass2.R_LINE:F1}) = {rec.Length} (band 20,000-30,000)",
            "the recruiting transform or a card input feeding it is wrong — the pathway-gated " +
            "line should admit roughly 56% of the candidate cohort.");

        // Determinism — same seed, same stream, identical cohort (the reproducibility
        // guard working-with-emmett §7 flags for the season layer).
        var coh2 = PlayerGenPass2Live.BuildCohort(GenPass2LiveSeed, GenPass2LiveN);
        var deterministic = true;
        for (var i = 0; i < n && deterministic; i++)
        {
            var a = coh[i].Result;
            var b = coh2[i].Result;
            if (a.Height != b.Height || a.Weapon != b.Weapon || a.Rscore != b.Rscore)
                deterministic = false;
            else
                foreach (var k in PlayerGenPass2.ALL_KEYS)
                    if (a.Card[k] != b.Card[k]) { deterministic = false; break; }
        }
        Assert(deterministic,
            "det", "two BuildCohort(20260706) runs produce identical cohorts",
            "the generator is non-reproducible — seeded regeneration is a future feature, not a debug tool.");

        Console.WriteLine(
            $"gen pass2 live population audit: {n} players, seed {GenPass2LiveSeed}, all bands green. " +
            "(bands test SHAPE — the oracle's exact seed numbers are a different RNG stream by design)");
    }
}
