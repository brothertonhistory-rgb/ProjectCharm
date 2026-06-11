namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll C. Returns the configured base weights with one
/// live wire: a single 0–1 <c>pressure</c> scalar nudges the live-strip slice
/// (then renormalizes). Placeholder to prove the generator->roll seam carries
/// signal — NOT basketball logic. The real attribute-driven generator replaces
/// this without touching Roll C or the resolver.
/// </summary>
public sealed class RollCStubPieGenerator
{
    private readonly RollCConfig _cfg;

    public RollCStubPieGenerator(RollCConfig cfg) => _cfg = cfg;

    /// <param name="state">Carried for signature parity with real generators;
    /// the stub does not read it yet.</param>
    /// <param name="pressure">0–1 live wire. Higher pressure nudges the
    /// live-strip (LostBallLiveBall) slice up before renormalization.</param>
    public Pie<TurnoverOutcome> Generate(PossessionState state, double pressure)
    {
        var clamped = Math.Clamp(pressure, 0.0, 1.0);

        var weights = new Dictionary<TurnoverOutcome, double>
        {
            [TurnoverOutcome.BadPassDeadBall] = _cfg.BaseBadPassDeadBall,
            [TurnoverOutcome.BadPassIntercepted] = _cfg.BaseBadPassIntercepted,
            [TurnoverOutcome.LostBallDeadBall] = _cfg.BaseLostBallDeadBall,
            [TurnoverOutcome.LostBallLiveBall] =
                _cfg.BaseLostBallLiveBall + clamped * _cfg.PressureLostBallLiveBallNudge,
            [TurnoverOutcome.OffensiveFoul] = _cfg.BaseOffensiveFoul,
        };

        // Renormalize so the pie sums to 1 after the nudge. The Pie constructor
        // validates this; renormalizing here keeps the nudge from breaking it.
        var total = weights.Values.Sum();
        foreach (var key in weights.Keys.ToList())
            weights[key] /= total;

        return new Pie<TurnoverOutcome>(weights, _cfg.Epsilon);
    }
}
