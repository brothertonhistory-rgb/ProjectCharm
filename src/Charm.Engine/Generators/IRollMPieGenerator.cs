namespace Charm.Engine;

/// <summary>
/// Contract for Roll M's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollMStubPieGenerator"/>) and the real attribute-driven
/// generator (<see cref="RollMGenerator"/>) implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. Same pattern as <see cref="IRollGPieGenerator"/> and
/// <see cref="IRollHPieGenerator"/>.
///
/// <para><b>One-arg signature (Phase 11 — mirroring Roll G/H, NOT Roll I).</b>
/// Unlike Roll I's two-arg <c>Generate(state, source)</c>, Roll M has exactly ONE
/// source (a missed final free throw) — there is no live-miss-vs-block fork.
/// The generator receives <see cref="PossessionState"/> for the roster reads that
/// drive the matchup bend. The stub ignores <paramref name="state"/> and returns
/// the flat config baseline; the real generator reads both rosters through it.</para>
///
/// <para><b>No shooter, no shot zone.</b> Unlike Roll I, Roll M has no crashing
/// shooter: off a free throw everyone is lined along the lane in assigned box-out
/// spots. Implementations always pass <c>shooterIdx = -1</c> and any zone
/// (conventionally <see cref="ShotLocation.Rim"/>) to
/// <see cref="Matchup.OffensiveReboundShare"/> — the nerf gate is structurally off.
/// Implementations must NOT read <see cref="PossessionState.SelectedSlot"/> or
/// <see cref="PossessionState.ShotType"/> for the matchup math (Divergence 2/3 from
/// the Roll I template).</para>
/// </summary>
public interface IRollMPieGenerator
{
    /// <param name="state">The carried possession state. The matchup-aware
    /// implementation reads both rosters via <see cref="GameState.RosterFor"/>;
    /// it does NOT read <see cref="PossessionState.SelectedSlot"/> or
    /// <see cref="PossessionState.ShotType"/> (Roll M has no shooter and no shot
    /// zone). The stub ignores this parameter entirely.</param>
    Pie<FreeThrowReboundOutcome> Generate(PossessionState state);
}
