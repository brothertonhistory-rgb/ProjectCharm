namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll H. Returns the configured weights as a finished
/// SEVEN-way pie over <see cref="ShotResult"/>. The make/miss outcomes are
/// realistic placeholders; <c>Blocked</c> is sized PER ZONE.
///
/// Zone-awareness (Session 13): this generator reads ONE stamp — the shot zone
/// (<see cref="PossessionState.ShotType"/>, stamped by Roll G) — and uses it to
/// size the block slice: Rim highest, Three lowest.
///
/// Phase 8: the foul slice is also now per-zone, not flat. Block AND foul are carved
/// off the top; the remaining three slices (Miss, OOBLost, OOBRetained) fill the rest,
/// preserving their relative proportions. The foul total is split into MadeAndFouled
/// (and-1) and MissFouled (two-shot trip) by the per-zone MafFraction — not matchup-
/// aware. This path fires only when no roster is populated (matchup-blind by design).
///
/// This is the seam, real and (mostly) flat: the Pie validates on construction
/// (bad weights fail loud), and the real attribute-driven generator replaces this
/// class later WITHOUT touching Roll H or the resolver.
///
/// Implements <see cref="IRollHPieGenerator"/> so the resolver holds the interface,
/// not the concrete stub.
/// </summary>
public sealed class RollHStubPieGenerator : IRollHPieGenerator
{
    private readonly RollHConfig _cfg;

    public RollHStubPieGenerator(RollHConfig cfg) => _cfg = cfg;

    /// <param name="state">The carried possession state. The generator reads ONE
    /// stamp off it — the shot ZONE (<see cref="PossessionState.ShotType"/>,
    /// stamped by Roll G) — to size the block and foul slices per zone. ShotType
    /// must be present (Roll G runs before Roll H in the live chain); a null zone
    /// is a wiring bug and fails loud.</param>
    /// <param name="putback">When true, the shot is an offensive-rebound PUTBACK:
    /// return the DISTINCT putback pie (always Rim, Phase 8 carve applied with Rim
    /// foul baseline). Defaults to false.</param>
    public Pie<ShotResult> Generate(PossessionState state, bool putback = false)
    {
        if (putback)
            return BuildPutbackPie();

        var zone = state.ShotType
            ?? throw new InvalidOperationException(
                "RollH generator requires a stamped ShotType — Roll G must run before Roll H.");

        return BuildLocatedPie(zone);
    }

    private Pie<ShotResult> BuildLocatedPie(ShotLocation zone)
    {
        var block           = _cfg.BlockWeight(zone);
        var foul            = _cfg.FoulRate(zone);
        var nonBlockNonFoul = 1.0 - block - foul;

        var made        = _cfg.BaseMade * nonBlockNonFoul;

        var mafFraction = _cfg.MafFraction(zone);
        var maf         = foul * mafFraction;
        var missFouled  = foul * (1.0 - mafFraction);

        var nonMadeBase  = _cfg.BaseMiss
                         + _cfg.BaseMissOutOfBoundsLost
                         + _cfg.BaseMissOutOfBoundsRetained;
        var nonMadeShare = nonBlockNonFoul - made;
        var scale        = nonMadeBase > 0.0 ? nonMadeShare / nonMadeBase : 0.0;

        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = made,
            [ShotResult.MadeAndFouled]           = maf,
            [ShotResult.Miss]                    = _cfg.BaseMiss                    * scale,
            [ShotResult.MissFouled]              = missFouled,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.BaseMissOutOfBoundsLost     * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.BaseMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked]                 = block,
        };

        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }

    /// <summary>
    /// Putback pie — Phase 8 carve applied using Rim foul baseline and MafFraction.
    /// PutbackMade is the conversion rate given not blocked AND not fouled.
    /// </summary>
    private Pie<ShotResult> BuildPutbackPie()
    {
        var block           = _cfg.PutbackBlocked;
        var foul            = _cfg.FoulRate(ShotLocation.Rim);
        var nonBlockNonFoul = 1.0 - block - foul;

        var made        = _cfg.PutbackMade * nonBlockNonFoul;

        var mafFraction = _cfg.MafFraction(ShotLocation.Rim);
        var maf         = foul * mafFraction;
        var missFouled  = foul * (1.0 - mafFraction);

        var nonMadeBase  = _cfg.PutbackMiss
                         + _cfg.PutbackMissOutOfBoundsLost
                         + _cfg.PutbackMissOutOfBoundsRetained;
        var nonMadeShare = nonBlockNonFoul - made;
        var scale        = nonMadeBase > 0.0 ? nonMadeShare / nonMadeBase : 0.0;

        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = made,
            [ShotResult.MadeAndFouled]           = maf,
            [ShotResult.Miss]                    = _cfg.PutbackMiss                    * scale,
            [ShotResult.MissFouled]              = missFouled,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.PutbackMissOutOfBoundsLost     * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.PutbackMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked]                 = block,
        };

        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }
}
