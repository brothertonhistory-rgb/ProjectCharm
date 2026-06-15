namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll F. Returns the configured base weights as a
/// finished four-way pie over <see cref="PlayerActionOutcome"/>. Realistic
/// placeholder weights this session, and — like Roll E — with NO live-wire
/// scalar. (Block left Roll F in Session 13 — it is now a per-zone slice of
/// Roll H — so the old fifth slice folded into ShotAttempt.)
///
/// Why no live wire (unlike Roll B's physicality and Roll C's pressure): the
/// only thing that tilts Roll F's pie is the deferred player/attribute model
/// (handle, defender length/hands, rim protection, shot selection). Roll F sits
/// one inch from that model — the beat right after selection — so a placeholder
/// wire here would pantomime the exact signal that is deliberately deferred. A
/// signal like defensive pressure is really a possession-level INPUT that Roll F
/// is only one reader of (it also pushes shot quality on the back end); wiring it
/// into F alone would bake in the wrong ownership. So, exactly like Roll D's
/// flavor generator and Roll E's selection generator, this generator takes no
/// signal argument.
///
/// This is the seam, real and flat: the Pie validates on construction (bad
/// weights fail loud), and the real attribute-driven generator replaces this
/// class later WITHOUT touching Roll F or the resolver — it just hands back a
/// non-flat pie over the same enum.
/// </summary>
public sealed class RollFStubPieGenerator : IRollFPieGenerator
{
    private readonly RollFConfig _cfg;

    public RollFStubPieGenerator(RollFConfig cfg) => _cfg = cfg;

    /// <param name="state">Carried for signature parity with real generators;
    /// the stub does not read it yet. The real generator will use it (and the
    /// selected slot it carries) to weight by the matchup's attributes.</param>
    public Pie<PlayerActionOutcome> Generate(PossessionState state)
    {
        var weights = new Dictionary<PlayerActionOutcome, double>
        {
            [PlayerActionOutcome.ShotAttempt] = _cfg.BaseShotAttempt,
            [PlayerActionOutcome.Turnover] = _cfg.BaseTurnover,
            [PlayerActionOutcome.NonShootingFoul] = _cfg.BaseNonShootingFoul,
            [PlayerActionOutcome.JumpBall] = _cfg.BaseJumpBall,
        };

        // No nudge, so no renormalize step is strictly needed — but the Pie
        // constructor still validates the sum is 1 within Epsilon, so weights
        // that don't add up fail loud rather than rolling skewed.
        return new Pie<PlayerActionOutcome>(weights, _cfg.Epsilon);
    }
}
