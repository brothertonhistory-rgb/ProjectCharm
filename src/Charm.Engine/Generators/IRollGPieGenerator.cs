namespace Charm.Engine;

/// <summary>
/// Contract for Roll G's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollGStubPieGenerator"/>) and the real attribute-driven
/// generator (<see cref="RollGGenerator"/>) implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. Same pattern as <see cref="IRollHPieGenerator"/>.
/// </summary>
public interface IRollGPieGenerator
{
    /// <param name="state">The carried possession state. Implementations
    /// read <see cref="PossessionState.SelectedSlot"/> for the shooter
    /// lookup. The matchup-aware implementation also reads the defending
    /// roster via <see cref="GameState.RosterFor"/>.</param>
    Pie<ShotLocation> Generate(PossessionState state);
}
