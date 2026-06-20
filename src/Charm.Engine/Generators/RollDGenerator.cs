namespace Charm.Engine;

/// <summary>
/// Pie generator for Roll D's foul flavor. Returns the configured flat weight
/// set (ReachIn / Blocking / OffBall). Stays flat by deliberate design, not by
/// accident or incompleteness:
///
/// <para><b>Why no zone context:</b> <c>ResolveFoulType</c> is emitted by Roll A,
/// Roll B, and Roll F — all three fire before Roll G runs. Roll G is the step
/// that stamps <c>ShotType</c> onto <c>PossessionState</c>, so
/// <c>state.ShotType</c> is null at every Roll D call site. There is no zone
/// information available to branch on.</para>
///
/// <para><b>Why no position context:</b> <c>SelectedSlot</c> can be non-null on
/// the Roll F feeder path (Roll E may have already run), but flavor is
/// non-routing theater with no downstream consumer that would justify turning
/// a flat split into a player-attribute model. Adding position context here
/// would imply flavor matters — it does not.</para>
///
/// <para><b>This is correct behavior.</b> The flat single weight set is the
/// right answer for the current architecture. Future sessions should not
/// re-litigate this without first establishing a downstream consumer that
/// actually reads the flavor field for routing or scoring purposes.</para>
///
/// The <paramref name="state"/> parameter is carried for signature parity with
/// attribute-driven generators elsewhere in the engine.
/// </summary>
public sealed class RollDGenerator
{
    private readonly RollDConfig _cfg;

    public RollDGenerator(RollDConfig cfg) => _cfg = cfg;

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
