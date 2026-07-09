namespace Charm.Engine;

/// <summary>
/// Distribution samplers on <see cref="IRng"/> — the Phase-2 (S44) draw layer for the
/// Pass-2 live player generator. All three draw exclusively via
/// <see cref="IRng.NextUnitInterval"/>, so a seeded run reproduces end to end.
///
/// <para><b>Statistical parity, not bit parity.</b> The oracle
/// (<c>tools/gen_pass2_skillfirst_oracle.py</c>) draws from Python's <c>random</c>;
/// C#'s <c>System.Random</c> is a different stream and different internal algorithms,
/// so byte-identical draws are impossible BY DESIGN and never asserted. What is
/// asserted (Phase 60, gate (0)): each sampler's sample mean and variance match the
/// closed-form distribution moments at N≥200k — a swapped Beta α/β, a wrong Gaussian
/// variance, or an inverted Exponential λ dies there, before any cohort is trusted.</para>
///
/// <para><b>Gaussian</b> — Box-Muller cos branch, the same core as
/// <see cref="ClockDraw.Sample"/> but UNTRUNCATED (ClockDraw truncates + rejects for
/// the shot clock; generation noise must keep its full tails). The sin twin is
/// discarded — statistically valid, matches ClockDraw's own approach.</para>
///
/// <para><b>Exponential</b> — inverse-CDF, matching Python's <c>expovariate</c>:
/// <c>-ln(1-u)/λ</c> with the log guarded the way ClockDraw guards its log.</para>
///
/// <para><b>Beta</b> — the two-gamma identity <c>G(a)/(G(a)+G(b))</c> with
/// Marsaglia–Tsang gamma draws. Every Beta parameter pair this generator uses has
/// a ≥ 1 and b ≥ 1 (orientation 2.007/2.493, skill 2.3/2.7, athletic 2.2/2.2,
/// specialization 2.0/2.0), so only the Marsaglia–Tsang k ≥ 1 path exists; the
/// unused k &lt; 1 branch is guarded by an entry assertion instead of dead code
/// (the S44 honest-scope ruling).</para>
/// </summary>
public static class Sampling
{
    /// <summary>One untruncated normal draw ~ N(mu, sigma²). Box-Muller cos branch,
    /// one normal per call (the sin twin is discarded).</summary>
    public static double Gauss(IRng rng, double mu, double sigma)
    {
        var u1 = rng.NextUnitInterval();
        var u2 = rng.NextUnitInterval();
        if (u1 < 1e-12) u1 = 1e-12; // guard the log (ClockDraw's guard, verbatim)
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mu + sigma * z;
    }

    /// <summary>One exponential draw with rate lambda (mean 1/lambda). Inverse-CDF,
    /// matching Python's <c>expovariate</c>: <c>-ln(1-u)/λ</c>.</summary>
    public static double Expovariate(IRng rng, double lambda)
    {
        var u = rng.NextUnitInterval();          // [0,1) — so 1-u is in (0,1]
        var oneMinusU = 1.0 - u;
        if (oneMinusU < 1e-12) oneMinusU = 1e-12; // guard the log
        return -Math.Log(oneMinusU) / lambda;
    }

    /// <summary>One Beta(a,b) draw in (0,1) via the two-gamma identity:
    /// <c>G(a)/(G(a)+G(b))</c>, each gamma from <see cref="GammaMT"/>.</summary>
    public static double Betavariate(IRng rng, double a, double b)
    {
        var ga = GammaMT(rng, a);
        var gb = GammaMT(rng, b);
        return ga / (ga + gb);   // both strictly > 0 for k ≥ 1 (d ≥ 2/3, v > 0)
    }

    /// <summary>One Gamma(k, scale 1) draw, Marsaglia–Tsang (2000), k ≥ 1 path ONLY.
    /// Every Beta pair in the Pass-2 spec has both parameters ≥ 1 (least is 2.007),
    /// so the k &lt; 1 boost branch is deliberately not written — a future caller
    /// that needs it hits the loud assertion, not a silent wrong answer.</summary>
    public static double GammaMT(IRng rng, double k)
    {
        if (k < 1.0)
            throw new ArgumentOutOfRangeException(nameof(k),
                $"GammaMT implements only the Marsaglia–Tsang k >= 1 path (got k={k}). " +
                "Every Pass-2 Beta parameter is >= 1; add the k < 1 boost branch only " +
                "when a real caller needs it.");

        var d = k - 1.0 / 3.0;
        var c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x, v;
            do
            {
                x = Gauss(rng, 0.0, 1.0);
                v = 1.0 + c * x;
            } while (v <= 0.0);
            v = v * v * v;
            var u = rng.NextUnitInterval();
            if (u < 1.0 - 0.0331 * x * x * x * x)
                return d * v;
            if (Math.Log(Math.Max(u, 1e-300)) < 0.5 * x * x + d * (1.0 - v + Math.Log(v)))
                return d * v;
        }
    }
}
