namespace Charm.Engine;

/// <summary>
/// Pie generator for the offensive-foul flavor. Selects the configured
/// frontcourt or backcourt weight set based on
/// <see cref="PossessionState.Frontcourt"/>. Stays flat by deliberate design:
///
/// <para><b>Why flat:</b> Flavor is theater — it does not route, does not affect
/// scoring, and has no downstream consumer that reads it for functional purposes.
/// Adding player-attribute context (offensive tendency, play type, etc.) would
/// imply the flavor field matters to the simulation. It does not.</para>
///
/// <para><b>Why two contexts:</b> The Frontcourt split correctly captures the
/// dominant real-world pattern — illegal screens dominate halfcourt sets
/// (frontcourt), while charges and push-offs dominate backcourt bring-ups.
/// This is the right level of context for a theater-only field.</para>
///
/// <para><b>This is correct behavior.</b> Future sessions should not add
/// attribute-driven logic here without first establishing a downstream consumer
/// that reads <c>OffensiveFoulFlavor</c> for routing or scoring purposes.</para>
/// </summary>
public sealed class RollOffensiveFoulGenerator
{
    private readonly RollOffensiveFoulConfig _cfg;

    public RollOffensiveFoulGenerator(RollOffensiveFoulConfig cfg) => _cfg = cfg;

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
