namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll E. Returns the configured base weights as a
/// finished five-way pie over <see cref="SelectionOutcome"/>. FLAT this session —
/// 20% per slot — and with NO live-wire scalar.
///
/// Why no live wire (unlike Roll B's physicality and Roll C's pressure): the
/// first real selection signal is usage, which belongs to the deferred
/// player/attribute model. There is nothing functional for a signal to move yet,
/// so — exactly like Roll D's flavor generator — this generator takes no signal
/// argument. Adding one now would falsely imply selection had a live signal.
///
/// This is the seam, real and flat: the Pie validates on construction (bad
/// weights fail loud), and the real attribute/usage-driven generator replaces
/// this class later WITHOUT touching Roll E or the resolver — it just hands back
/// a non-flat pie over the same enum.
/// </summary>
public sealed class RollEStubPieGenerator
{
    private readonly RollEConfig _cfg;

    public RollEStubPieGenerator(RollEConfig cfg) => _cfg = cfg;

    /// <param name="state">Carried for signature parity with real generators;
    /// the stub does not read it yet. The real generator will use it to find the
    /// offense, walk its lineup, and weight by the filling players' attributes.</param>
    public Pie<SelectionOutcome> Generate(PossessionState state)
    {
        var weights = new Dictionary<SelectionOutcome, double>
        {
            [SelectionOutcome.Slot1] = _cfg.BaseSlot1,
            [SelectionOutcome.Slot2] = _cfg.BaseSlot2,
            [SelectionOutcome.Slot3] = _cfg.BaseSlot3,
            [SelectionOutcome.Slot4] = _cfg.BaseSlot4,
            [SelectionOutcome.Slot5] = _cfg.BaseSlot5,
        };

        // No nudge, so no renormalize step is strictly needed — but the Pie
        // constructor still validates the sum is 1 within Epsilon, so flat
        // weights that don't add up fail loud rather than rolling skewed.
        return new Pie<SelectionOutcome>(weights, _cfg.Epsilon);
    }
}
