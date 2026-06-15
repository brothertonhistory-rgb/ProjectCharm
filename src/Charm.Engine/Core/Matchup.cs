namespace Charm.Engine;

/// <summary>
/// The matchup primitive (Phase 6) — the first place two players' attributes meet.
/// Turns a shooter and the defender contesting him into a single matchup-adjusted
/// <see cref="EffectiveRating"/> that slides along the one per-zone make-curve
/// (<see cref="RollHConfig.MakeProbability"/>), which is reused untouched. The curve
/// is never reshaped; a contest is just the shooter sliding up or down the shared
/// scale (axes.md Phase 4, "effective-rating SHIFT, not a curve change").
///
/// <para><b>Composition (DEC-2, additive — axes.md Phase 4).</b>
/// <c>effective = baseline + skillShift + physicalShift</c>, where the baseline is the
/// shooter's own zone rating, the skill shift is the gap between his zone rating and the
/// defender's blended per-zone defensive read, and the physical shift is the athletic
/// gap. The shifts can be large; the make-curve's floor/ceiling — not any cap in here —
/// bounds the payoff.</para>
///
/// <para><b>Gap → shift (DEC-5, signed power law).</b> <see cref="GapFn"/> is signed,
/// monotonic, and convex/accelerating with a flat bottom (a marginal edge is
/// imperceptible) and NO imposed cap (only the make-curve's asymptote bounds make%).
/// Physical is steeper than skill via a higher exponent — "size insurmountable" is a
/// tail property — while the curve's floor delivers "skill never extinguished."</para>
///
/// <para><b>Single source of the zone → offense-skill map.</b> <see cref="OffenseRating"/>
/// is the one place the location→skill pairing lives; RollHGenerator's baseline read
/// delegates here rather than keeping a parallel copy.</para>
///
/// Pure and static — no state, no RNG. Every tunable (steepness, exponent, scale, the
/// per-zone blend weights) lives in <see cref="MatchupConfig"/>.
/// </summary>
public static class Matchup
{
    /// <summary>
    /// The shooter's own skill rating for a zone — location (where the shot comes from)
    /// mapped to the skill that converts it. The single source of this pairing
    /// (RollHGenerator's baseline read delegates here).
    /// Three/Long → Outside; Mid → Mid; Short → Close; Rim → Finishing.
    /// </summary>
    public static double OffenseRating(ShotLocation zone, Player p) => zone switch
    {
        ShotLocation.Three => p.Outside,
        ShotLocation.Long  => p.Outside,
        ShotLocation.Mid   => p.Mid,
        ShotLocation.Short => p.Close,
        ShotLocation.Rim   => p.Finishing,
        _ => throw new InvalidOperationException($"No offense rating mapping for zone '{zone}'.")
    };

    /// <summary>
    /// The defender's per-zone defensive read (CONF-1) — a weighted blend of his three
    /// defensive attributes that slides perimeter→interior across the five zones. The
    /// blend (not a single attribute per zone) rewards a two-way defender everywhere and
    /// lets a mid/long shooter exploit a defender's weaker sub-skill (a rim-protector-only
    /// big gives up the perimeter share at Mid/Long). Weights are config data.
    /// </summary>
    public static double DefenseRating(ShotLocation zone, Player d, MatchupConfig cfg)
    {
        var (perimeter, post, rim) = cfg.BlendWeights(zone);
        return perimeter * d.PerimeterDefense
             + post      * d.PostDefense
             + rim       * d.RimProtection;
    }

    /// <summary>
    /// The signed power-law gap → shift primitive (DEC-5):
    /// <c>shift = steepness · sign(gap) · (|gap| / scale)^exponent</c>, exponent &gt; 1.
    ///
    /// <para>Odd (an even matchup yields zero shift); flat-bottomed (zero slope at
    /// gap 0, so a marginal edge is imperceptible — this requires exponent &gt; 1);
    /// convex/accelerating (the effect grows faster than the gap); and uncapped (no
    /// asymptote here — the make-curve's logistic supplies the only ceiling). The
    /// exponent &gt; 1 invariant is enforced by <see cref="MatchupConfig.Load"/>.</para>
    /// </summary>
    public static double GapFn(double gap, double steepness, double exponent, double scale)
    {
        var magnitude = Math.Pow(Math.Abs(gap) / scale, exponent);
        return Math.Sign(gap) * steepness * magnitude;
    }

    /// <summary>
    /// The matchup-adjusted effective rating fed to the make-curve (DEC-2). Builds the
    /// shooter's baseline, the defender's blended defensive read, the skill gap and the
    /// physical (athletic) gap, runs each through <see cref="GapFn"/> with its own
    /// steepness/exponent, and sums additively onto the baseline. The caller passes the
    /// result to <see cref="RollHConfig.MakeProbability"/>, which is untouched.
    /// </summary>
    public static double EffectiveRating(ShotLocation zone, Player attacker, Player defender, MatchupConfig cfg)
    {
        var baseline = OffenseRating(zone, attacker);
        var defense  = DefenseRating(zone, defender, cfg);

        var skillShift    = GapFn(baseline - defense,
                                  cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        var physicalShift = GapFn(attacker.Athleticism - defender.Athleticism,
                                  cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);

        return baseline + skillShift + physicalShift;
    }
}
