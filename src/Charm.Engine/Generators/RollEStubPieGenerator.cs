namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll E. Returns a finished five-way pie over
/// <see cref="SelectionOutcome"/>, selected by the possession's
/// <see cref="PossessionState.FastBreak"/> marker: the flat halfcourt pie (20% per
/// slot) normally, or the transition selection pie (a non-flat placeholder) when the
/// possession is running a break — the same context-selects-a-pie pattern as Roll
/// C/J/K. Still NO live-wire scalar.
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
public sealed class RollEStubPieGenerator : IRollEPieGenerator
{
    private readonly RollEConfig _cfg;

    public RollEStubPieGenerator(RollEConfig cfg) => _cfg = cfg;

    /// <param name="state">The carried possession state. The stub reads ONE field:
    /// <see cref="PossessionState.FastBreak"/>, to choose between the halfcourt pie
    /// (the flat Base* weights) and the transition pie (the Transition* weights) —
    /// the SAME context-selects-a-pie pattern as Roll C/J/K. The rest is carried for
    /// signature parity; the real generator will use the lineup + attributes.</param>
    public Pie<SelectionOutcome> Generate(PossessionState state)
    {
        // FastBreak=true (Roll J pushed) draws the transition selection pie; otherwise
        // the flat halfcourt pie. The transition tilt is a placeholder this session —
        // the real speed/athleticism favoring is the deferred attribute seam, which
        // replaces this class without touching Roll E or the resolver.
        var weights = state.FastBreak
            ? new Dictionary<SelectionOutcome, double>
            {
                [SelectionOutcome.Slot1] = _cfg.TransitionSlot1,
                [SelectionOutcome.Slot2] = _cfg.TransitionSlot2,
                [SelectionOutcome.Slot3] = _cfg.TransitionSlot3,
                [SelectionOutcome.Slot4] = _cfg.TransitionSlot4,
                [SelectionOutcome.Slot5] = _cfg.TransitionSlot5,
            }
            : new Dictionary<SelectionOutcome, double>
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
