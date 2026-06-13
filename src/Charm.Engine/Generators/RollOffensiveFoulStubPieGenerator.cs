namespace Charm.Engine;

/// <summary>
/// Stub pie generator for the offensive-foul flavor. Selects the configured
/// front-court or back-court weight set based on <see cref="PossessionState.Frontcourt"/>.
/// Theater only — the flavor does not route; wiring a signal here would imply
/// it mattered. The real attribute-driven generator (offensive tendency, play
/// type, etc. -> flavor mix) replaces this without touching the resolver.
/// </summary>
public sealed class RollOffensiveFoulStubPieGenerator
{
    private readonly RollOffensiveFoulConfig _cfg;

    public RollOffensiveFoulStubPieGenerator(RollOffensiveFoulConfig cfg) => _cfg = cfg;

    /// <summary>Generate the flavor pie for the given possession state. Reads
    /// <see cref="PossessionState.Frontcourt"/> to select the appropriate mix;
    /// the court-state is already stamped on the terminal that triggered the draw.</summary>
    public Pie<OffensiveFoulFlavor> Generate(PossessionState state)
    {
        var weights = state.Frontcourt
            ? new Dictionary<OffensiveFoulFlavor, double>
            {
                [OffensiveFoulFlavor.Charge]        = _cfg.FrontcourtCharge,
                [OffensiveFoulFlavor.PushOff]        = _cfg.FrontcourtPushOff,
                [OffensiveFoulFlavor.IllegalScreen]  = _cfg.FrontcourtIllegalScreen,
            }
            : new Dictionary<OffensiveFoulFlavor, double>
            {
                [OffensiveFoulFlavor.Charge]        = _cfg.BackcourtCharge,
                [OffensiveFoulFlavor.PushOff]        = _cfg.BackcourtPushOff,
                [OffensiveFoulFlavor.IllegalScreen]  = _cfg.BackcourtIllegalScreen,
            };

        return new Pie<OffensiveFoulFlavor>(weights, _cfg.Epsilon);
    }
}
