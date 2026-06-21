namespace Charm.Engine;

/// <summary>
/// The full generation result from Roll E's attribute-driven generator — the pie
/// PLUS the per-slot volume pressures computed in the same one-pass calculation.
/// Carried as a top-level type so the interface signature does not depend on any
/// concrete generator class.
/// </summary>
/// <param name="Pie">The finished five-way selection pie over
/// <see cref="SelectionOutcome"/>, identical to what
/// <see cref="IRollEPieGenerator.Generate"/> would return alone.</param>
/// <param name="FinalShares">The post-floor/rail per-slot shares that were used to
/// build the pie (length 5, indexed 0–4 matching Slot1–Slot5). Zero for
/// null/unpopulated slots. Carried so the resolver can compute pressures without
/// a second pass.</param>
/// <param name="Pressures">Per-slot volume pressure: <c>max(0, FinalShares[i] −
/// 1.0/populatedCount)</c>. Zero for null/empty/FastBreak slots. Length 5,
/// indexed 0–4 matching Slot1–Slot5.</param>
public readonly record struct RollEGeneration(
    Pie<SelectionOutcome> Pie,
    double[] FinalShares,
    double[] Pressures);

/// <summary>
/// Derived interface for Roll E's generator — extends
/// <see cref="IRollEPieGenerator"/> with the richer one-pass method that returns
/// the pie AND the per-slot pressures together.
/// <para>The resolver's <c>_rollEGenerator</c> field is widened to this interface
/// so it can call <see cref="GenerateWithPressure"/> at the two Roll E call sites
/// without a downcast. Both the real generator (<see cref="RollEGenerator"/>) and
/// the primary stub (<see cref="RollEStubPieGenerator"/>) implement this interface,
/// ensuring all 20 harness Resolver construction sites compile.</para>
/// <para>Callers that only need the pie (e.g. isolated test helpers typed to
/// <see cref="IRollEPieGenerator"/>) still work: the stub and real generator both
/// satisfy the base interface through inheritance.</para>
/// </summary>
public interface IRollEGenerationProvider : IRollEPieGenerator
{
    /// <summary>
    /// Generate the selection pie AND the per-slot pressures in one pass.
    /// The pie is identical to what <see cref="IRollEPieGenerator.Generate"/>
    /// would return; the pressures are the volume excess above the equal share
    /// for each populated slot.
    /// </summary>
    RollEGeneration GenerateWithPressure(PossessionState state);

    /// <summary>
    /// Phase 27 Session 2 — selection tilt. Bends the usage pie produced by
    /// <see cref="GenerateWithPressure"/> by the gap between usage intent
    /// (FinalShares) and defensive attention, re-enforcing the floor/rail
    /// constraint using the TILTED weights as the redistribution basis.
    /// Phase 44: adds selection compression via defensive OffBallDefense
    /// (perimeter focal points) and HelpDefense (interior focal points).
    /// Halfcourt-only: caller must NOT call this on the FastBreak branch.
    /// <para>The stub implementation returns the pie unchanged (no tilt in
    /// isolated harness checks that don't wire the full attention path).</para>
    /// </summary>
    Pie<SelectionOutcome> BendByAttention(
        RollEGeneration gen,
        double[] attentionShares,
        GameState game,
        MatchupConfig matchupCfg,
        PossessionState state);
}
