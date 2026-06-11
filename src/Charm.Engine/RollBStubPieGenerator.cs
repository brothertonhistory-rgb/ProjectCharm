namespace Charm.Engine;

/// <summary>
/// STUB. Not the real attribute-driven pie generator for Roll B. Exists only so
/// Roll B has a valid pie to consume and to prove the generator->roll seam
/// carries signal. The real generator (attributes -> matchup -> weighted odds)
/// will replace this without changing Roll B.
/// </summary>
public sealed class RollBStubPieGenerator
{
    private readonly RollBConfig _cfg;

    public RollBStubPieGenerator(RollBConfig cfg) => _cfg = cfg;

    /// <param name="state">Current possession (unused by the stub; the real
    /// generator will read attributes off it).</param>
    /// <param name="physicality">A single 0..1 scalar standing in for defensive
    /// physicality. The one live input, wired to a trivial nudge on the foul
    /// slice — a placeholder to prove the wire moves outcomes, not real logic.</param>
    public Pie<HalfcourtOutcome> Generate(PossessionState state, double physicality)
    {
        if (physicality < 0 || physicality > 1)
            throw new ArgumentOutOfRangeException(nameof(physicality), physicality, "Physicality must be in [0, 1].");

        var proceed = _cfg.BaseProceed;
        var foul = _cfg.BaseFoul + physicality * _cfg.PhysicalityFoulNudge;
        var deadBallTurnover = _cfg.BaseDeadBallTurnover;

        var total = proceed + foul + deadBallTurnover;
        var weights = new Dictionary<HalfcourtOutcome, double>
        {
            [HalfcourtOutcome.Proceed] = proceed / total,
            [HalfcourtOutcome.Foul] = foul / total,
            [HalfcourtOutcome.DeadBallTurnover] = deadBallTurnover / total,
        };

        return new Pie<HalfcourtOutcome>(weights, _cfg.Epsilon);
    }
}
