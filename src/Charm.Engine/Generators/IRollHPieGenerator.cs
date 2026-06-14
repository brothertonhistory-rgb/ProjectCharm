namespace Charm.Engine;

/// <summary>
/// Contract for Roll H's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollHStubPieGenerator"/>) and the real attribute-driven
/// generator (<see cref="RollHGenerator"/>) implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. When a future phase builds another real generator (matchup effects,
/// gravity, etc.), only the construction site changes — the resolver and Roll H are
/// untouched.
/// </summary>
public interface IRollHPieGenerator
{
    /// <param name="state">The carried possession state. Implementations read
    /// <see cref="PossessionState.ShotType"/> for the zone and
    /// <see cref="PossessionState.SelectedSlot"/> for the shooter lookup.</param>
    /// <param name="putback">When true, the shot is an offensive-rebound putback;
    /// implementations return the distinct putback pie instead of the located-shot pie.</param>
    Pie<ShotResult> Generate(PossessionState state, bool putback = false);
}
