namespace Charm.Engine;

/// <summary>
/// C# port, S69: the LIVE Pass-3 two-plane budget player generator. This class owns
/// exactly one thing — DRAWING. It fills the fixture-shaped <see cref="Pass3Draws"/>
/// from an <see cref="IRng"/> in the locked 68-slot order and hands it to
/// <see cref="PlayerGenPass3.BuildFromDraws"/>, so the math that runs live is the math
/// the Phase-69 fixture replay proves against the locked oracle every harness run.
///
/// <para><b>The 68-slot draw order is a contract</b> (the fixture's <c>draw_order</c>,
/// asserted by Phase 69; single home: the oracle's <c>_flat_draws</c>): height uniform,
/// wingspan gauss, athletic-quality beta, 7 athletic gauss in ATH_KEYS order, weight
/// gauss, def-plane gauss, role uniform, talent beta, concentration beta, 7 family-pull
/// gauss in FAMILY_ORDER, 22 within-member gauss in SPEND_SKILLS order (= family order,
/// member order inside — the oracle interleaves these with allocation arithmetic that
/// draws NOTHING, so the flat fill below consumes the identical stream), 22 base-jitter
/// gauss in SPEND_SKILLS order, arrival gauss, ft-idio gauss. The slots are SEMANTIC
/// values, not uniform calls (each Gaussian consumes a uniform pair internally).
/// Do not reorder.</para>
///
/// <para><b>LIVE SINCE S70 (the bridge swap):</b> the divvy's <c>BuildRecruitedCohort</c>
/// consumes this stream — first-past-the-line at <c>PlayerGenPass3.R_LINE</c>, positions
/// by defensive-plane rank. <b>The A5 card shift, executed:</b> the card contract
/// shifted — OffensiveRebounding/DefensiveRebounding left the size card (the old
/// <c>post_bonus</c> height/strength stamps of the retired Pass-2 generator — removed
/// from the tree at S73, archived under <c>tools/archive/pass2/</c> — are
/// retired from the live path) and arrive here as SPENDABLE, current-expressed skills
/// under the SAME key names. Downstream readers kept compiling; their values changed
/// meaning. That is the S70 page's baseline story.</para>
/// </summary>
public static class PlayerGenPass3Live
{
    /// <summary>One live player: the raw draws it consumed plus the full deterministic
    /// result. Draws ride along because the Phase-70 population audit needs the latent
    /// dials (q, c) — and because draws-in-hand keeps every player fully replayable.</summary>
    public sealed class LivePlayer
    {
        public required Pass3Draws Draws { get; init; }
        public required Pass3Result Result { get; init; }
    }

    /// <summary>Draw one player: fill the 68 slots in fixture order, then run the locked
    /// transform. This method contains ZERO arithmetic beyond the oracle's own draw
    /// parameters — the one computed input (the arrival MEAN, which follows the body per
    /// D2) comes from <see cref="PlayerGenPass3.ComputeArrivalMean"/>, the same shared
    /// home <see cref="PlayerGenPass3.BuildFromDraws"/> sits beside.</summary>
    public static LivePlayer GeneratePlayer(IRng rng)
    {
        var d = new Pass3Draws();

        // slot 1: height uniform — inverse CDF of the preserved marginal (oracle :121-124)
        d.HeightU = rng.NextUnitInterval();

        // slot 2: wingspan gauss, mean INCLUDED in the drawn value (oracle :344)
        d.WsNoise = Sampling.Gauss(rng, PlayerGenPass3.WS_NOISE_MEAN, PlayerGenPass3.WS_NOISE_SIGMA);

        // slot 3: athletic-quality beta (oracle :347)
        d.A = Sampling.Betavariate(rng, PlayerGenPass3.ATHQ_A, PlayerGenPass3.ATHQ_B);

        // slots 4-10: the 7 athletic noises in ATH_KEYS order (oracle :352)
        foreach (var k in PlayerGenPass3.ATH_KEYS)
            d.AthNoise[k] = Sampling.Gauss(rng, 0.0, PlayerGenPass3.ATH_SIGMA[k]);

        // slot 11: weight gauss (oracle :356)
        d.WeightNoise = Sampling.Gauss(rng, 0.0, PlayerGenPass3.WEIGHT_NOISE_SIGMA);

        // slot 12: def-plane gauss (oracle :176)
        d.DefNoise = Sampling.Gauss(rng, 0.0, PlayerGenPass3.DEF_NOISE);

        // slot 13: role uniform (oracle :212)
        d.RoleU = rng.NextUnitInterval();

        // slots 14-15: talent beta, concentration beta (oracle :232, :366)
        d.Q = Sampling.Betavariate(rng, PlayerGenPass3.TALENT_A, PlayerGenPass3.TALENT_B);
        d.C = Sampling.Betavariate(rng, PlayerGenPass3.CONC_A, PlayerGenPass3.CONC_B);

        // slots 16-22: the 7 family-pull gauss in FAMILY_ORDER (oracle :268-270)
        foreach (var f in PlayerGenPass3.FAMILY_ORDER)
            d.PullGauss[f] = Sampling.Gauss(rng, 0.0, PlayerGenPass3.PULL_DICE_SIGMA);

        // slots 23-44: the 22 within-member gauss, family order + member order inside
        // (= SPEND_SKILLS order; the oracle's interleaved allocation draws nothing)
        foreach (var k in PlayerGenPass3.SPEND_SKILLS)
            d.WithinGauss[k] = Sampling.Gauss(rng, 0.0, PlayerGenPass3.WITHIN_DICE_SIGMA);

        // slots 45-66: the 22 base-jitter gauss in SPEND_SKILLS order (oracle :392)
        foreach (var k in PlayerGenPass3.SPEND_SKILLS)
            d.BaseJitterGauss[k] = Sampling.Gauss(rng, 0.0, PlayerGenPass3.BASE_JITTER);

        // slot 67: arrival gauss — the MEAN follows the BODY (D2, oracle :400-403);
        // Height is a deterministic function of slot 1, so no extra RNG is consumed.
        var height = PlayerGenPass3.HeightFromU(d.HeightU);
        d.ArrivalRaw = Sampling.Gauss(rng, PlayerGenPass3.ComputeArrivalMean(height), PlayerGenPass3.ARR_SIGMA);

        // slot 68: the ONE persistent FT idiosyncrasy (oracle :413)
        d.FtIdio = Sampling.Gauss(rng, 0.0, PlayerGenPass3.FT_SIGMA);

        return new LivePlayer { Draws = d, Result = PlayerGenPass3.BuildFromDraws(d) };
    }

    /// <summary>The canonical cohort: one seeded stream, n players in sequence — the
    /// C# twin of the oracle's <c>build_cohort</c>. Phase 70 audits this at the
    /// oracle's own seed and N.</summary>
    public static LivePlayer[] BuildCohort(int seed, int n)
    {
        var rng = new SystemRng(seed);
        var cohort = new LivePlayer[n];
        for (var i = 0; i < n; i++)
            cohort[i] = GeneratePlayer(rng);
        return cohort;
    }
}
