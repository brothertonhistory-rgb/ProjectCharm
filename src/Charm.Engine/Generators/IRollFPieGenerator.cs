namespace Charm.Engine;

/// <summary>
/// Contract for Roll F's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollFStubPieGenerator"/>) and the real pressure-aware
/// generator (<see cref="RollFGenerator"/>) implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. Same pattern as <see cref="IRollGPieGenerator"/> (single one-arg
/// <c>Generate(PossessionState state)</c> — Roll F needs no source enum and no bool).
/// </summary>
public interface IRollFPieGenerator
{
    /// <param name="state">The carried possession state. The real generator reads
    /// <see cref="PossessionState.SelectedSlot"/> for the handler lookup and derives
    /// the slot-matched defender locally. The stub ignores this parameter and returns
    /// the flat config baseline.</param>
    Pie<PlayerActionOutcome> Generate(PossessionState state);
}
