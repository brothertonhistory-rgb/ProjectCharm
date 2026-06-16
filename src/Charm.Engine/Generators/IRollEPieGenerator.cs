namespace Charm.Engine;

/// <summary>
/// Contract for Roll E's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollEStubPieGenerator"/>) and the eventual
/// attribute/usage-driven real generator implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. Same pattern as <see cref="IRollBPieGenerator"/>.
/// </summary>
public interface IRollEPieGenerator
{
    /// <param name="state">The carried possession state. The stub reads
    /// <see cref="PossessionState.FastBreak"/> to choose between the halfcourt
    /// and transition selection pies. The real generator will read the lineup
    /// and usage attributes.</param>
    Pie<SelectionOutcome> Generate(PossessionState state);
}
