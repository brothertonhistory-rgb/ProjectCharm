namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll B. Returns the configured base weights as a
/// finished four-way pie over <see cref="HalfcourtOutcome"/>, with ONE live
/// wire: a 0–1 <c>physicality</c> scalar nudges the foul slice, then the whole
/// pie renormalizes to sum 1. Placeholder proving the seam carries signal — not
/// basketball logic. The real attribute-driven generator replaces this class
/// later WITHOUT touching Roll B or the resolver.
///
/// The jump-ball slice is a small sliver (no wire of its own); only the foul
/// slice is nudged.
/// </summary>
public sealed class RollBStubPieGenerator : IRollBPieGenerator
{
    private readonly RollBConfig _cfg;

    public RollBStubPieGenerator(RollBConfig cfg) => _cfg = cfg;

    /// <param name="physicality">0–1 live wire: how strongly to nudge the foul
    /// slice before renormalization. 0 leaves the base weights as-is.</param>
    public Pie<HalfcourtOutcome> Generate(PossessionState state, double physicality)
    {
        var weights = new Dictionary<HalfcourtOutcome, double>
        {
            [HalfcourtOutcome.Proceed] = _cfg.BaseProceed,
            [HalfcourtOutcome.Foul] = _cfg.BaseFoul,
            [HalfcourtOutcome.DeadBallTurnover] = _cfg.BaseDeadBallTurnover,
            [HalfcourtOutcome.JumpBall] = _cfg.BaseJumpBall,
        };

        // Live wire: physicality pushes the foul slice up.
        weights[HalfcourtOutcome.Foul] += physicality * _cfg.PhysicalityFoulNudge;

        // Renormalize so the nudged pie sums to 1 (the Pie constructor validates
        // this within Epsilon, so a bad nudge fails loud rather than rolling skewed).
        var total = weights.Values.Sum();
        foreach (var key in weights.Keys.ToList())
            weights[key] /= total;

        return new Pie<HalfcourtOutcome>(weights, _cfg.Epsilon);
    }
}
