namespace Charm.Engine;

/// <summary>
/// The matchup primitive (Phase 6 + Phase 7) — the first place two players' attributes meet.
///
/// <para><b>Phase 6 — make door.</b> Turns a shooter and the defender contesting him into
/// a single matchup-adjusted <see cref="EffectiveRating"/> that slides along the one per-zone
/// make-curve (<see cref="RollHConfig.MakeProbability"/>), which is reused untouched. The curve
/// is never reshaped; a contest is just the shooter sliding up or down the shared scale
/// (axes.md Phase 4, "effective-rating SHIFT, not a curve change").</para>
///
/// <para><b>Phase 7 — block door.</b> <see cref="BlockWeight"/> bends the configured
/// per-zone block-rate baseline toward a per-zone ceiling (defender edge) or floor
/// (shooter edge). The bend is driven by an additive composition of a skill shift
/// (shooter zone-skill vs defender blend — the same attributes Phase 6 reads) and a
/// length shift (the new block-specific <see cref="LengthRating"/> composite). Both use
/// <see cref="GapFn"/>. Zone-specific weights govern the skill/length split — at Three
/// the contest is 60% length / 40% skill; at Rim the same pair. The saturation is tanh
/// so the result asymptotes smoothly toward floor/ceiling without crossing.</para>
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
/// per-zone blend weights, block weights) lives in <see cref="MatchupConfig"/>.
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

    /// <summary>
    /// The block-specific length composite (Phase 7) — the single place the
    /// Height / Wingspan / Vertical → length mapping lives.
    ///
    /// <para>Length is what blocks shots; quickness and strength belong to the make door's
    /// <see cref="Player.Athleticism"/> read. This is intentionally asymmetric — the
    /// Athleticism composite (five attributes) and the length composite (three attributes)
    /// serve different physical reads and are not unified. Blend weights live in config so
    /// a future "tune the length composite" pass only touches <see cref="MatchupConfig"/>,
    /// not this method.</para>
    /// </summary>
    public static double LengthRating(Player p, MatchupConfig cfg)
        => p.Height   * cfg.LengthHeight
         + p.Wingspan * cfg.LengthWingspan
         + p.Vertical * cfg.LengthVertical;

    /// <summary>
    /// The matchup-aware block weight for a shot attempt (Phase 7). Starts at the
    /// configured per-zone baseline (<paramref name="baseBlockWeight"/>) and bends it
    /// toward a per-zone ceiling (defender edge) or floor (shooter edge) using a tanh
    /// saturation.
    ///
    /// <para><b>Two contributions, additively composed.</b>
    /// Skill: defender's zone blend (<see cref="DefenseRating"/>) minus shooter's
    /// zone skill (<see cref="OffenseRating"/>) — positive when the defender is better,
    /// which raises block rate. Length: defender's <see cref="LengthRating"/> minus
    /// shooter's — positive when the defender is longer. Both use <see cref="GapFn"/>
    /// with the make door's gap-function parameters; the per-zone weights then scale
    /// each contribution before summing.</para>
    ///
    /// <para><b>The bend formula.</b>
    /// <c>totalShift = skillWeight·skillShift + lengthWeight·lengthShift</c>.
    /// Positive totalShift (defender edge): bend from baseline toward ceiling.
    /// Negative totalShift (shooter edge): bend from baseline toward floor.
    /// <c>span = (totalShift ≥ 0) ? (ceiling − baseline) : (baseline − floor)</c>.
    /// <c>bend = span · tanh(totalShift / BlockReferenceShift)</c>.
    /// tanh is odd and bounded in (−1, +1), so the result never crosses floor or ceiling
    /// regardless of how extreme the gap is.</para>
    ///
    /// <para><b>Empty-slot fallback (DEC-6).</b> When the defending slot is empty the
    /// caller passes the configured baseline for <paramref name="baseBlockWeight"/> and
    /// does NOT call this method — it uses that value directly. This method assumes a
    /// populated defender and has no null guard; null-checking is the caller's
    /// responsibility (same pattern as <see cref="EffectiveRating"/>).</para>
    ///
    /// <para><b>All existing methods are untouched.</b> This extends the primitive;
    /// nothing in Phase 6 is modified.</para>
    /// </summary>
    public static double BlockWeight(ShotLocation zone, Player shooter, Player defender,
                                     double baseBlockWeight, MatchupConfig cfg)
    {
        // Skill contribution: defender's zone defensive read minus shooter's zone skill.
        // Positive = defender advantage = raises block rate.
        var skillGap   = DefenseRating(zone, defender, cfg) - OffenseRating(zone, shooter);
        var skillShift = GapFn(skillGap, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);

        // Length contribution: defender's length composite minus shooter's.
        // Positive = defender longer = raises block rate.
        var lengthGap   = LengthRating(defender, cfg) - LengthRating(shooter, cfg);
        var lengthShift = GapFn(lengthGap, cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);

        // Weighted sum: per-zone skill/length split (e.g. 40/60 at Rim and Three).
        var (sw, lw) = cfg.BlockContestWeights(zone);
        var totalShift = sw * skillShift + lw * lengthShift;

        // Tanh saturation toward ceiling (defender edge) or floor (shooter edge).
        // span is the headroom available in the relevant direction from the baseline.
        var ceiling = cfg.BlockCeiling(zone);
        var floor   = cfg.BlockFloor(zone);
        var span    = totalShift >= 0.0 ? (ceiling - baseBlockWeight) : (baseBlockWeight - floor);
        var bend    = span * Math.Tanh(totalShift / cfg.BlockReferenceShift);

        // Add bend: positive totalShift bends up, negative bends down.
        // bend is naturally negative when totalShift is negative (tanh is odd), so a plain
        // addition bends down toward floor for shooter edge and up toward ceiling for defender
        // edge — no sign flip needed. The spec's "(shift >= 0 ? bend : -bend)" was wrong:
        // -bend when bend is already negative returns a positive value, bending the wrong way.
        return baseBlockWeight + bend;
    }
}
