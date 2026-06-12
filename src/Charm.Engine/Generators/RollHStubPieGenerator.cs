namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll H. Returns the configured weights as a finished
/// SEVEN-way pie over <see cref="ShotResult"/>. The six make/miss outcomes are
/// realistic placeholders; the seventh, <c>Blocked</c>, is sized PER ZONE.
///
/// Zone-awareness (Session 13): this generator reads ONE stamp — the shot zone
/// (<see cref="PossessionState.ShotType"/>, stamped by Roll G) — and uses it to
/// size the block slice: Rim highest, Three lowest. The block weight b(zone) is
/// carved off the top and the six make/miss weights are scaled by (1 − b(zone)),
/// so within a zone the make/miss SHAPE is unchanged except for the block
/// carve-out. The six make/miss outcomes themselves remain location-BLIND — a
/// future shooting-% pass owns per-zone make/miss tuning. Apart from the zone, the
/// generator reads no stamps and has NO live-wire scalar.
///
/// Why no further live wire: the rest of what tilts Roll H's pie is the deferred
/// player/attribute model — the shooter-vs-defender matchup, the other-six
/// defensive-attention (gravity) term, the skill/athleticism gates, the bounded
/// logistic make/miss mapping, and shot quality folded into the make percentage.
/// Roll H sits exactly where that model expresses, so a placeholder wire here
/// would pantomime the precise signal being deferred.
///
/// This is the seam, real and (mostly) flat: the Pie validates on construction
/// (bad weights fail loud), and the real attribute-driven generator replaces this
/// class later WITHOUT touching Roll H or the resolver — it just hands back a
/// richer pie over the same enum (one that reads the carried SelectedSlot +
/// ShotType to tilt the make %).
/// </summary>
public sealed class RollHStubPieGenerator
{
    private readonly RollHConfig _cfg;

    public RollHStubPieGenerator(RollHConfig cfg) => _cfg = cfg;

    /// <param name="state">The carried possession state. The generator reads ONE
    /// stamp off it — the shot ZONE (<see cref="PossessionState.ShotType"/>,
    /// stamped by Roll G) — to size the block slice per zone. It still does NOT
    /// read SelectedSlot; the full attribute-driven matchup tilt is the deferred
    /// real generator. ShotType must be present (Roll G runs before Roll H in the
    /// live chain); a null zone is a wiring bug and fails loud.</param>
    public Pie<ShotResult> Generate(PossessionState state)
    {
        // Read the zone Roll G stamped, then look up its block weight b(zone).
        var zone = state.ShotType
            ?? throw new InvalidOperationException(
                "RollH generator requires a stamped ShotType — Roll G must run before Roll H.");
        var block = _cfg.BlockWeight(zone);

        // Carve the block weight off the top; the six make/miss outcomes keep
        // their RELATIVE proportions, scaled by (1 − block). Because the six base
        // weights sum to 1, the scaled six sum to (1 − block), and adding the
        // block slice brings the pie back to exactly 1 — so the Pie constructor's
        // sum-to-one validation holds for any block in [0, 1).
        var scale = 1.0 - block;
        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made] = _cfg.BaseMade * scale,
            [ShotResult.MadeAndFouled] = _cfg.BaseMadeAndFouled * scale,
            [ShotResult.Miss] = _cfg.BaseMiss * scale,
            [ShotResult.MissFouled] = _cfg.BaseMissFouled * scale,
            [ShotResult.MissOutOfBoundsLost] = _cfg.BaseMissOutOfBoundsLost * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.BaseMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked] = block,
        };

        // The Pie constructor validates the sum is 1 within Epsilon, so a bad
        // shape (six bases not summing to 1, or a block ≥ 1) fails loud rather
        // than rolling skewed.
        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }
}
