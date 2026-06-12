namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll D's foul flavor. Returns the configured flavor
/// weights. Unlike Rolls B and C, there is no live "signal" wire here — flavor
/// is pure theater that does not route, so there is nothing functional for a
/// signal to move; wiring one in would imply flavor mattered. The real
/// attribute-driven generator (defender discipline, hand activity, etc. -> flavor
/// mix) replaces this without touching Roll D or the resolver.
///
/// The <paramref name="state"/> parameter is carried for signature parity with
/// the real generators (and the other rolls' generators); the stub does not read
/// it yet.
/// </summary>
public sealed class RollDStubPieGenerator
{
    private readonly RollDConfig _cfg;

    public RollDStubPieGenerator(RollDConfig cfg) => _cfg = cfg;

    public Pie<FoulFlavor> Generate(PossessionState state)
    {
        var weights = new Dictionary<FoulFlavor, double>
        {
            [FoulFlavor.ReachIn] = _cfg.FlavorReachIn,
            [FoulFlavor.Blocking] = _cfg.FlavorBlocking,
            [FoulFlavor.OffBall] = _cfg.FlavorOffBall,
        };

        // No nudge to renormalize here (no live wire). The Pie constructor still
        // validates sum-to-one, so a misconfigured flavor block fails loud at the
        // generator->roll seam rather than silently skewing the theater.
        return new Pie<FoulFlavor>(weights, _cfg.Epsilon);
    }
}
