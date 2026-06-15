namespace Charm.Engine;

/// <summary>
/// Contract for Roll I's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollIStubPieGenerator"/>) and the real attribute-driven
/// generator (<see cref="RollIGenerator"/>) implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. Same pattern as <see cref="IRollGPieGenerator"/> and
/// <see cref="IRollHPieGenerator"/>.
///
/// <para><b>Two-arg signature (Phase 10 Divergence 1).</b> Unlike Roll G/H whose
/// generators take only <c>Generate(PossessionState state)</c>, Roll I's generator
/// needs both the <see cref="ReboundSource"/> (which baseline pie — live-miss or
/// block) AND the <see cref="PossessionState"/> (the rosters, shooter slot, and
/// shot zone for the matchup bend). The stub ignores <paramref name="state"/> and
/// operates flat; the real generator reads it fully.</para>
/// </summary>
public interface IRollIPieGenerator
{
    /// <param name="state">The carried possession state. The matchup-aware
    /// implementation reads <see cref="PossessionState.SelectedSlot"/> for the
    /// shooter, <see cref="PossessionState.ShotType"/> for the shooter-nerf gate,
    /// and the rosters via <see cref="GameState.RosterFor"/>. The stub ignores
    /// this parameter.</param>
    /// <param name="source">Which loose ball this is — selects the baseline weight
    /// set (live-miss vs block). A null stamp is coalesced to
    /// <see cref="ReboundSource.LiveBall"/> by the resolver before calling.</param>
    Pie<ReboundOutcome> Generate(PossessionState state, ReboundSource source);
}
