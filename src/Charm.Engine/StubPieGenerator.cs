namespace Charm.Engine;

/// <summary>
/// STUB. Not the real attribute-driven pie generator. It exists only so Roll A
/// has a valid pie to consume and so we can prove the generator->roll seam
/// carries signal. The real generator (attributes -> matchup -> weighted odds)
/// is out of scope and will replace this without changing Roll A.
/// </summary>
public sealed class StubPieGenerator
{
    private readonly RollAConfig _cfg;

    public StubPieGenerator(RollAConfig cfg) => _cfg = cfg;

    /// <param name="state">Current possession (unused by the stub; the real
    /// generator will read attributes off it).</param>
    /// <param name="pressure">A single 0..1 scalar standing in for "defensive
    /// pressure". The one live input, wired to a trivial nudge on the turnover
    /// slice — a placeholder to prove the wire moves outcomes, not real logic.</param>
    public Pie<EntryOutcome> Generate(PossessionState state, double pressure)
    {
        if (pressure < 0 || pressure > 1)
            throw new ArgumentOutOfRangeException(nameof(pressure), pressure, "Pressure must be in [0, 1].");

        // The single live wire: pressure nudges turnover up. Everything else is
        // the configured base. Then renormalize so the slices sum to 1.
        var clean = _cfg.BaseClean;
        var turnover = _cfg.BaseTurnover + pressure * _cfg.PressureTurnoverNudge;
        var violation = _cfg.BaseViolation;
        var foul = _cfg.BaseFoul;
        var jumpBall = _cfg.BaseJumpBall;

        var total = clean + turnover + violation + foul + jumpBall;
        var weights = new Dictionary<EntryOutcome, double>
        {
            [EntryOutcome.CleanEntry] = clean / total,
            [EntryOutcome.Turnover] = turnover / total,
            [EntryOutcome.ShotClockViolation] = violation / total,
            [EntryOutcome.Foul] = foul / total,
            [EntryOutcome.JumpBall] = jumpBall / total,
        };

        return new Pie<EntryOutcome>(weights, _cfg.Epsilon);
    }
}
