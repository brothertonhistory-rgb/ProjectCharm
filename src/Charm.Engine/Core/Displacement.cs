namespace Charm.Engine;

/// <summary>
/// The defensive read the displacement derivation consumes for one defender —
/// the three zone-defense attributes plus the athleticism composite
/// (Session 36, Roll G matchup displacement).
///
/// <para><b>Why not <see cref="Player"/> directly:</b> the golden parity fixture
/// (emitted by the locked oracle <c>tools/displacement_oracle.py</c>) carries
/// non-integral defender attributes — the level-matched vector 5b solves
/// PostDefense to a fractional value by construction. <see cref="Player"/>
/// attributes are <c>int</c>, so the fixture cannot be represented as Players.
/// This record carries the same four reads as doubles; the production path maps
/// Players through <see cref="FromPlayer"/> (lossless int→double), and the
/// parity check builds them from the fixture's raw doubles. The aggregation math
/// (top-3 zone blend, five-man athleticism mean) lives in
/// <see cref="Matchup.DeriveDisplacement"/> either way — one source, two feeders.</para>
/// </summary>
/// <param name="PerimeterDefense">The defender's perimeter-defense attribute.</param>
/// <param name="PostDefense">The defender's post-defense attribute.</param>
/// <param name="RimProtection">The defender's rim-protection attribute.</param>
/// <param name="Athleticism">The defender's athleticism composite — the same
/// five-attribute mean as <see cref="Player.Athleticism"/>.</param>
public readonly record struct DisplacementDefender(
    double PerimeterDefense,
    double PostDefense,
    double RimProtection,
    double Athleticism)
{
    /// <summary>Build the displacement read from a live <see cref="Player"/> —
    /// the production path. Lossless: int attributes widen to double; the
    /// athleticism composite is <see cref="Player.Athleticism"/> verbatim.</summary>
    public static DisplacementDefender FromPlayer(Player p) =>
        new(p.PerimeterDefense, p.PostDefense, p.RimProtection, p.Athleticism);
}

/// <summary>
/// The full stage trace of one displacement derivation (Session 36) — every
/// intermediate the locked oracle's <c>derive()</c> returns, mirrored
/// stage-for-stage so the Phase 56 golden parity check can compare each stage
/// (not just the final shares) against the fixture.
///
/// <para>All five-element arrays are indexed in the fixed zone order
/// [Rim, Short, Mid, Long, Three] — the same convention as
/// <c>RollGGenerator</c>'s internal arrays.</para>
/// </summary>
/// <param name="Base">The normalized pre-bend baseline diet (the coached
/// tendencies, normalized to sum 1). Both deltas below are composed from
/// THIS baseline.</param>
/// <param name="Gaps">Per-zone raw matchup gap: shooter zone skill minus the
/// top-3-blended lineup resistance (the existing Phase 9 reads).</param>
/// <param name="SkillLevel">The diet-weighted skill level: Σ Base[z]·Gaps[z].
/// The level residuals are stripped against — physical term NOT included.</param>
/// <param name="PhysLevel">The gentle athleticism term: shooter composite vs the
/// defending lineup's MEAN composite, through GapFn at displacement steepness.</param>
/// <param name="Level">SkillLevel + PhysLevel — the overall superiority signal
/// that drives displacement magnitude (and the §7 observation stamp).</param>
/// <param name="Residuals">Per-zone gap minus SkillLevel — the shape-only signal
/// the Phase 9 bend consumes under Route B.</param>
/// <param name="BentShapeOnly">The baseline bent by the residualized multipliers
/// and renormalized — the pure shape bend, before displacement.</param>
/// <param name="Mag">The bounded displacement magnitude:
/// MaxMagnitude · tanh(Level / LevelReference) · min(1, UsageScale · usage).</param>
/// <param name="Ladder">The per-zone ladder weights actually applied — the
/// configured weights, with the Rim/Short entries gated by the shooter's own
/// Finishing/Close when Mag &gt; 0 (inward pull gated; outward push ungated).</param>
/// <param name="Final">Baseline + Δbend + Δdisplacement, clamped ≥ 0 and
/// renormalized once — the five shares handed to the diet shift.</param>
public readonly record struct DisplacementTrace(
    double[] Base,
    double[] Gaps,
    double SkillLevel,
    double PhysLevel,
    double Level,
    double[] Residuals,
    double[] BentShapeOnly,
    double Mag,
    double[] Ladder,
    double[] Final);
