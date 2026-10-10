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
/// <para><b>The lane, not the field-goal shooter (★ S120).</b> Off a free throw the men stand
/// on the lane: the resolver stamps <see cref="PossessionState.FreeThrowLane"/> before calling
/// <see cref="Generate"/>, and the real generator reads the lane's totals
/// (<see cref="FreeThrowLane.OffensiveShare"/>). Implementations must NOT read
/// <see cref="PossessionState.SelectedSlot"/> or <see cref="PossessionState.ShotType"/> for the
/// matchup math — the man at the line arrives on the lane.</para>
/// </summary>
public interface IRollMPieGenerator
{
    /// <param name="state">The carried possession state, with the free-throw lane stamped on it.
    /// The matchup-aware implementation reads the lane's men through <see cref="GameState.RosterFor"/>;
    /// it does NOT read <see cref="PossessionState.SelectedSlot"/> or
    /// <see cref="PossessionState.ShotType"/>. The stub ignores this parameter entirely.</param>
    Pie<FreeThrowReboundOutcome> Generate(PossessionState state);

    /// <summary>★ S120 — the Roll M settings the resolver uses to line up the free-throw lane and to
    /// name who gets the board and the scramble fouls. The stub and the real generator return the
    /// config they were built with; a test double that carries none gets the class defaults.</summary>
    RollMConfig LaneConfig => RollMConfig.ClassDefaults;
}
