namespace Charm.Engine;

/// <summary>
/// Contract for Roll B's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollBStubPieGenerator"/>) and the real pressure-and-
/// matchup-aware generator (<see cref="RollBGenerator"/>) implement this interface
/// so the resolver field can be typed to the interface, decoupling the resolver
/// from the concrete implementation. Same pattern as <see cref="IRollFPieGenerator"/>.
///
/// <para><b>Two-arg signature (Phase 13).</b> Unlike Roll F's one-arg interface,
/// Roll B's interface carries a <c>physicality</c> scalar. The scalar is a dormant
/// placeholder wire (fed 0.0 at both live dispatch sites) that may become a live
/// dial in a future session. Keeping it in the interface now costs nothing and avoids
/// an interface-breaking change later.</para>
/// </summary>
public interface IRollBPieGenerator
{
    /// <param name="state">The carried possession state. The real generator reads
    /// both rosters to compute slot-weighted team BallHandling and Steals aggregates.
    /// <see cref="PossessionState.SelectedSlot"/> is NOT read — Roll B precedes
    /// player selection (Roll E); no individual handler is known yet.</param>
    /// <param name="physicality">Dormant 0–1 scalar. Fed 0.0 at both live dispatch
    /// sites; the stub and real generator each apply it as a nudge on the Foul
    /// slice before renormalizing. Does nothing at 0.0.</param>
    Pie<HalfcourtOutcome> Generate(PossessionState state, double physicality);
}
