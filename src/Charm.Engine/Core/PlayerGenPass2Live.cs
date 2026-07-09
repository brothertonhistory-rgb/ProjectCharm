namespace Charm.Engine;

/// <summary>
/// C# port, Phase 2 (S44): the LIVE Pass-2 skill-first player generator. This class
/// owns exactly one thing — DRAWING. It fills the fixture-shaped <see cref="Pass2Draws"/>
/// from an <see cref="IRng"/> in the locked 40-slot order and hands it to the Phase-1
/// <see cref="PlayerGenPass2.BuildFromDraws"/>, so the math that runs live is the math
/// the Phase-59 fixture replay proves bit-for-bit every harness run.
///
/// <para><b>The 40-slot draw order is a contract</b> (the fixture's <c>draw_order</c>,
/// asserted by Phase 59): o, q, a, s, height-branch selector, height noise (ONE draw on
/// either branch), 20 skill noises in DRAWN_SKILLS order, wingspan, 7 athletic noises in
/// ATH_KEYS order, weight, OREB, DREB, arrival, ft_idio, age. Statistical parity makes
/// reordering harmless TODAY, but the constant per-player budget of 40 semantic draws is
/// what keeps a future seeded cross-check against the fixture possible — do not reorder.
/// (The 40 slots are SEMANTIC values, not uniform calls: each Gaussian slot consumes a
/// Box-Muller uniform pair internally, so the raw uniform count per player is higher,
/// and that is correct.)</para>
///
/// <para><b>Standalone by ruling (S44 scope wall):</b> nothing downstream reads this.
/// The season talent pool, the divvy, and the gen demo still run the Pass-1 position-based
/// path with its enforcers. Bridging this positionless cohort onto the position-quota
/// divvy is Phase 3, which opens with the positions-from-orientation design conversation.</para>
/// </summary>
public static class PlayerGenPass2Live
{
    /// <summary>The oracle's inline noise shapes (oracle :348, :367-372) — hardcoded at
    /// their draw sites in Python, named here so the loop reads like the spec.</summary>
    public const double WINGSPAN_NOISE_MEAN = 4.0;   // mean INCLUDED in the drawn value (oracle :348)
    public const double WINGSPAN_NOISE_SIGMA = 3.0;
    public const double WEIGHT_NOISE_SIGMA = 6.0;    // oracle :367
    public const double OREB_NOISE_SIGMA = 7.0;      // oracle :370
    public const double DREB_NOISE_SIGMA = 7.0;      // oracle :372

    /// <summary>One live player: the raw draws it consumed plus the full deterministic
    /// result. The draws ride along because the population audit needs the latent dials
    /// (q, s) that <see cref="Pass2Result"/> deliberately does not carry — and because
    /// draws-in-hand is what makes any future player fully replayable.</summary>
    public sealed class LivePlayer
    {
        public required Pass2Draws Draws { get; init; }
        public required Pass2Result Result { get; init; }
    }

    /// <summary>Draw one player: fill the 40 slots in fixture order, then run the locked
    /// Phase-1 transform. All math lives in <see cref="PlayerGenPass2.BuildFromDraws"/>;
    /// this method contains ZERO arithmetic beyond the oracle's own draw parameters.</summary>
    public static LivePlayer GeneratePlayer(IRng rng)
    {
        var d = new Pass2Draws();

        // slots 1-4: the four independent latent dials (oracle :287-290)
        d.O = Sampling.Betavariate(rng,
            PlayerGenPass2.ORI_MEAN * PlayerGenPass2.ORI_CONC,
            (1.0 - PlayerGenPass2.ORI_MEAN) * PlayerGenPass2.ORI_CONC);
        d.Q = Sampling.Betavariate(rng, PlayerGenPass2.SKILL_Q_A, PlayerGenPass2.SKILL_Q_B);
        d.A = Sampling.Betavariate(rng, PlayerGenPass2.ATHQ_A, PlayerGenPass2.ATHQ_B);
        d.S = Sampling.Betavariate(rng, PlayerGenPass2.SPEC_A, PlayerGenPass2.SPEC_B);

        // slots 5-6: height branch — ONE noise draw on either branch (oracle :303-310).
        // sigma_up comes from the SHARED shape helper (the S44 extraction), so the loop
        // and the locked transform can never drift on the height shape.
        d.HeightBranchSelectorRaw = rng.NextUnitInterval();
        var (_, _, sigmaUp) = PlayerGenPass2.ComputeHeightShape(d.O);
        d.HeightNoiseRaw = d.HeightBranchSelectorRaw < 0.5
            ? Sampling.Gauss(rng, 0.0, sigmaUp)                                  // upper: pre-abs gauss
            : Sampling.Expovariate(rng, 1.0 / PlayerGenPass2.HT_SCALE_DOWN);     // lower: the expovariate value

        // slots 7-26: per-skill idiosyncrasy, DRAWN_SKILLS list order (oracle :326)
        foreach (var k in PlayerGenPass2.DRAWN_SKILLS)
            d.SkillNoise[k] = Sampling.Gauss(rng, 0.0, PlayerGenPass2.SKILL_NOISE);

        // slot 27: wingspan (oracle :348 — the 4.0 mean is IN the drawn value)
        d.WingspanNoise = Sampling.Gauss(rng, WINGSPAN_NOISE_MEAN, WINGSPAN_NOISE_SIGMA);

        // slots 28-34: athletic noise, ATH_KEYS list order (oracle :354)
        foreach (var k in PlayerGenPass2.ATH_KEYS)
            d.AthNoise[k] = Sampling.Gauss(rng, 0.0, PlayerGenPass2.ATH_SIGMA[k]);

        // slots 35-37: weight, OREB, DREB (oracle :367-372)
        d.WeightNoise = Sampling.Gauss(rng, 0.0, WEIGHT_NOISE_SIGMA);
        d.OrebNoise = Sampling.Gauss(rng, 0.0, OREB_NOISE_SIGMA);
        d.DrebNoise = Sampling.Gauss(rng, 0.0, DREB_NOISE_SIGMA);

        // slot 38: arrival, pre-clamp (oracle :373-374 — the clamp lives in the transform)
        var arrMean = PlayerGenPass2.ARR_PERIM
                    - d.O * (PlayerGenPass2.ARR_PERIM - PlayerGenPass2.ARR_POST);
        d.ArrivalDrawRaw = Sampling.Gauss(rng, arrMean, PlayerGenPass2.ARR_SIGMA);

        // slot 39: the ONE per-player FT idiosyncrasy (oracle :384, S42.1 ruling)
        d.FtIdio = Sampling.Gauss(rng, 0.0, PlayerGenPass2.FT_SIGMA);

        // slot 40: age noise — placeholder machinery (oracle :396, S42.1 ruling)
        d.AgeNoiseRaw = Sampling.Gauss(rng, 0.0, PlayerGenPass2.AGE_NOISE);

        return new LivePlayer { Draws = d, Result = PlayerGenPass2.BuildFromDraws(d) };
    }

    /// <summary>The oracle's <c>build_cohort</c> (oracle :481-486): n players from one
    /// seeded stream. Same seed → identical cohort (Phase 60 asserts it).</summary>
    public static LivePlayer[] BuildCohort(int seed, int n = 46000)
    {
        var rng = new SystemRng(seed);
        var cohort = new LivePlayer[n];
        for (var i = 0; i < n; i++)
            cohort[i] = GeneratePlayer(rng);
        return cohort;
    }
}
