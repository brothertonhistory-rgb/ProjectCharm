namespace Charm.Engine;

/// <summary>
/// Contract for Roll A's pie generator — the single method the resolver calls.
/// Both the stub (<see cref="StubPieGenerator"/>) and the real press-and-matchup-aware
/// generator (<see cref="RollAGenerator"/>) implement this interface so the resolver
/// field can be typed to the interface, decoupling the resolver from the concrete
/// implementation. Same pattern as <see cref="IRollBPieGenerator"/> and
/// <see cref="IRollFPieGenerator"/>.
///
/// <para><b>Two-arg signature (Phase 15).</b> The interface carries a dormant
/// <c>pressure</c> scalar — a proof-of-seam wire established by the stub and kept
/// so the interface contract is stable. The real generator discards it (via
/// <c>_ = pressure</c>) because the press decision (whether to press this possession)
/// is made upstream by <see cref="Resolver.RunPossession"/> — a single RNG draw
/// against <see cref="MatchupConfig.PressProbabilityFor"/> — and stamped on
/// <see cref="PossessionState.PressMode"/> before <c>Generate</c> is called.
/// All live dispatch sites pass <c>pressure: 0.0</c>.</para>
///
/// <para><b>Stricter than IRollBPieGenerator.</b> Roll B's physicality parameter
/// is still applied as a zero-valued nudge inside the real generator. Roll A's
/// pressure parameter is fully discarded — allowing it to influence the press math
/// would create a second, accidental pressure input on top of the real
/// <see cref="PossessionState.PressMode"/> stamp, with no defined semantics.
/// The [0,1] guard still runs so the interface contract matches the stub's behavior
/// in every code path.</para>
/// </summary>
public interface IRollAPieGenerator
{
    /// <param name="state">The carried possession state. The real generator reads
    /// <see cref="PossessionState.Frontcourt"/> to gate the press computation: when
    /// <c>true</c> (offense already crossed half), the flat baseline is returned
    /// immediately. When <c>false</c>, it reads <see cref="PossessionState.PressMode"/>:
    /// <c>None</c> → flat baseline (no roster read); <c>Standard</c> → full three-gap
    /// matchup bend within the Standard lift/gate band; <c>Desperate</c> → throws.</param>
    /// <param name="pressure">Dormant 0–1 scalar. Validated with the same
    /// <see cref="ArgumentOutOfRangeException"/> guard as the stub, then discarded.
    /// Fed 0.0 at all live dispatch sites. Does not influence the press math.</param>
    Pie<EntryOutcome> Generate(PossessionState state, double pressure);
}
