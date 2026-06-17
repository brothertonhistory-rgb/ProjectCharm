namespace Charm.Engine;

/// <summary>
/// Contract for Roll L's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="RollLStubPieGenerator"/>) and the real attribute-driven
/// generator (<see cref="RollLGenerator"/>) implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. Same pattern as <see cref="IRollMPieGenerator"/>.
///
/// <para><b>One-arg signature (Phase 18).</b> Roll L is the simplest real generator
/// in the engine: the shooter's <see cref="Player.FreeThrow"/> rating IS the make
/// percentage × 100 — no logistic, no matchup, no context modifier. The generator
/// receives <see cref="PossessionState"/> to read
/// <see cref="PossessionState.SelectedSlot"/> and walk to the shooter's rating.
/// The stub ignores <paramref name="state"/> and returns the flat config make%.</para>
///
/// <para><b>FreeThrow is absolute, not relative.</b> Every other attribute is on a
/// 50 = average relative scale. FreeThrow is literal: a 72-rated shooter makes
/// exactly 72% of free throws. No opponent attribute is involved.</para>
///
/// <para><b>Null-slot fallback.</b> If <see cref="PossessionState.SelectedSlot"/> is
/// null (a bonus foul before Roll E ran) or the slot is unpopulated, implementations
/// fall back to <see cref="RollLConfig.MakeProbability"/>. This is a named loose end,
/// not a bug — see §7 of the Phase 18 build prompt.</para>
/// </summary>
public interface IRollLPieGenerator
{
    /// <param name="state">Carried possession state. The real generator reads
    /// <see cref="PossessionState.SelectedSlot"/> to reach the shooter's
    /// <see cref="Player.FreeThrow"/> rating. The stub ignores this parameter
    /// and returns the flat config make%.</param>
    Pie<FreeThrowOutcome> Generate(PossessionState state);
}
