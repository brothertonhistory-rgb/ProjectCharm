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

    /// <summary>
    /// The matchup-aware foul rate for a shot attempt (Phase 8). Bends a per-zone
    /// foul baseline toward a per-zone ceiling (shooter-favorable contest) or floor
    /// (defender-favorable) using a tanh saturation.
    ///
    /// <para><b>Asymmetric contest (Phase 8 distinct shape).</b> Unlike Phase 6/7
    /// which used a raw attribute gap, the foul contest uses asymmetrically-weighted
    /// differences from a midpoint: offense-dominant (FoulDrawing carries the bigger
    /// weight) and defender-light (Discipline carries the smaller one). This encodes
    /// Emmett's basketball call that low foul-drawing isn't an active skill — it's
    /// absence of opportunity — so the shooter's contribution dominates and the
    /// defender's is a light tap. The single GLOBAL weight pair is uniform across
    /// zones; per-zone variation in impact lives in the per-zone floors/ceilings
    /// (narrow downward, wide upward).</para>
    ///
    /// <para><b>No physical anchor.</b> Unlike <see cref="EffectiveRating"/>
    /// (Athleticism) and <see cref="BlockWeight"/> (Length), foul-drawing has no
    /// physical term. The correlation between physical traits and foul-drawing lives
    /// in attribute generation (a strong post player gets a high FoulDrawing rating),
    /// not in the contest itself.</para>
    ///
    /// <para><b>Reuses <see cref="GapFn"/> with the skill parameters.</b>
    /// Foul-drawing IS a skill contest — the FoulDrawing vs Discipline gap goes
    /// through GapFn with SkillSteepness and SkillExponent, same as the make door.
    /// The separate FoulReferenceShift governs the tanh saturation speed.</para>
    ///
    /// <para><b>Empty-defender fallback (DEC-6) is the caller's responsibility.</b>
    /// This method assumes a populated defender; null-checking is upstream in
    /// RollHGenerator (same pattern as <see cref="BlockWeight"/>).</para>
    /// </summary>
    public static double FoulRate(ShotLocation zone, Player shooter, Player defender,
                                  double baseFoulRate, MatchupConfig cfg)
    {
        // Asymmetric contest: offense-dominant (FoulDrawing) minus defense-light (Discipline),
        // both expressed as deviations from AttributeMidpoint so an average player (50)
        // contributes zero. Positive contestValue = shooter edge = bends rate up.
        var contestValue = cfg.OffenseFoulWeight * (shooter.FoulDrawing - cfg.AttributeMidpoint)
                         - cfg.DefenseFoulWeight * (defender.Discipline  - cfg.AttributeMidpoint);

        // Reuse the skill gap-function parameters — foul-drawing IS a skill contest.
        var shift = GapFn(contestValue, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);

        // Tanh saturation toward ceiling (shooter edge) or floor (defender edge).
        var ceiling = cfg.FoulCeiling(zone);
        var floor   = cfg.FoulFloor(zone);
        var span    = shift >= 0.0 ? (ceiling - baseFoulRate) : (baseFoulRate - floor);
        var bend    = span * Math.Tanh(shift / cfg.FoulReferenceShift);

        // Plain addition — tanh supplies the sign. The Session 38 lesson:
        // do NOT write `bend if shift >= 0 else -bend` — bend is already negative
        // when shift is negative, and -bend would flip it the wrong way.
        return baseFoulRate + bend;
    }

    /// <summary>
    /// The defending team's per-zone defensive resistance (Phase 9). The blend
    /// of the top three defenders' <see cref="DefenseRating"/> at the given zone,
    /// weighted by <see cref="MatchupConfig.LocationBlendFirst"/>,
    /// <c>LocationBlendSecond</c>, and <c>LocationBlendThird</c>.
    ///
    /// <para><b>Why top-3, not the slot-matched defender.</b> Shot location is
    /// the LEAST one-on-one of the matchup doors. The offense reads where the
    /// defense is collectively weakest before deciding what to attack — a great
    /// rim protector pushes attempts outside even if HE isn't the slot-matched
    /// defender, because he'll rotate. Help arrives less than instantly, so the
    /// second and third options also matter. Fourth and fifth are too far from
    /// the action.</para>
    ///
    /// <para><b>Fewer than 3 populated defenders (DEC-6 partial case).</b> If
    /// only N defenders are populated (N in 1..3), the blend uses the first N
    /// weights renormalized to sum to 1.0. If N = 0, the caller must
    /// short-circuit BEFORE calling this method (it throws on no populated
    /// defenders).</para>
    ///
    /// <para><b>Pure and static.</b> No state, no RNG.</para>
    /// </summary>
    public static double DefensiveResistance(ShotLocation zone,
                                             IReadOnlyList<Player?> defenders,
                                             MatchupConfig cfg)
    {
        var scores = new List<double>();
        foreach (var d in defenders)
            if (d is not null)
                scores.Add(DefenseRating(zone, d, cfg));

        if (scores.Count == 0)
            throw new InvalidOperationException(
                $"DefensiveResistance for zone {zone}: no populated defenders. " +
                "Caller must short-circuit BEFORE calling this method.");

        scores.Sort((a, b) => b.CompareTo(a));   // descending — best first
        var take = Math.Min(3, scores.Count);

        var w = new[] { cfg.LocationBlendFirst, cfg.LocationBlendSecond, cfg.LocationBlendThird };
        var weightSum = 0.0;
        for (var i = 0; i < take; i++) weightSum += w[i];

        var blended = 0.0;
        for (var i = 0; i < take; i++)
            blended += (w[i] / weightSum) * scores[i];

        return blended;
    }

    /// <summary>
    /// The per-zone multiplier that bends a shooter's authored tendency for
    /// that zone (Phase 9). Computed via the ratio form so the multiplier is
    /// bounded in <c>(1/LocationMaxMultiplier, LocationMaxMultiplier)</c> —
    /// strictly positive and exactly 1.0 at zero gap.
    ///
    /// <para>The formula: read the per-zone gap (capability minus resistance),
    /// run through <see cref="GapFn"/> with the existing skill steepness/
    /// exponent (foul-drawing reused the same primitive in Phase 8; shot
    /// location reuses it again here), and pass through
    /// <c>exp(log(LocationMaxMultiplier) * tanh(shift / LocationReferenceShift))</c>.
    /// </para>
    ///
    /// <para><b>Public static so the harness can test the math directly.</b>
    /// Mirrors <see cref="BlockWeight"/> and <see cref="FoulRate"/> — the
    /// matchup primitive lives on <c>Matchup</c>, not buried in the generator.
    /// The generator's job is to call this method per zone, multiply
    /// tendencies, and renormalize.</para>
    ///
    /// <para>Caller's responsibility: ensure at least one defender is
    /// populated. With zero populated defenders, <see cref="DefensiveResistance"/>
    /// would throw — the generator short-circuits to pure-tendency normalization
    /// in that case.</para>
    /// </summary>
    public static double LocationMultiplier(ShotLocation zone,
                                            Player shooter,
                                            IReadOnlyList<Player?> defenders,
                                            MatchupConfig cfg)
    {
        var resistance = DefensiveResistance(zone, defenders, cfg);
        var capability = OffenseRating(zone, shooter);
        var gap        = capability - resistance;
        var shift      = GapFn(gap, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        // Ratio form: strictly positive, exactly 1.0 at zero shift, bounded in
        // (1 / LocationMaxMultiplier, LocationMaxMultiplier).
        return Math.Exp(Math.Log(cfg.LocationMaxMultiplier)
                      * Math.Tanh(shift / cfg.LocationReferenceShift));
    }

    // =========================================================================
    // Phase 10 — rebound door (the glass)
    // =========================================================================

    /// <summary>
    /// The pre-staging team-size composite for rebounding (Phase 10, stage 1;
    /// extended Phase 35 to include wingspan).
    /// A weighted read of a player's physical presence on the glass — height,
    /// strength, and wingspan. Mirrors <see cref="LengthRating"/> in shape;
    /// blend weights live in config so the "tune the size composite" pass is
    /// trivial.
    ///
    /// <para>Used as the external comparison (team A's mean vs team B's mean) to
    /// decide which team physically wins the board before skill enters. A 7-footer
    /// with long arms helps his team against a small lineup and hurts it against
    /// giants because the comparison is <em>relative</em>.</para>
    /// </summary>
    public static double ReboundPhysical(Player p, MatchupConfig cfg)
        => cfg.ReboundStrengthWeight  * p.Strength
         + cfg.ReboundHeightWeight    * p.Height
         + cfg.ReboundWingspanWeight  * p.Wingspan;

    /// <summary>
    /// The within-team wingspan tilt for individual rebound attribution (Phase 35).
    /// Returns a multiplier centered at 1.0: a player with longer arms than his
    /// lineup average pulls a slightly larger share; one with shorter arms pulls
    /// slightly less. The tanh asymptote keeps the effect gentle regardless of
    /// how extreme the wingspan gap is.
    ///
    /// <para><b>Formula.</b>
    /// <c>1 + ReboundWingspanSwing · tanh((playerWingspan − lineupMeanWingspan) / ReboundWingspanScale)</c>.
    /// At the default Swing = 0.10 the range is (0.90, 1.10) — a 10 % tilt at
    /// most. <see cref="MatchupConfig.ReboundWingspanSwing"/> and
    /// <see cref="MatchupConfig.ReboundWingspanScale"/> govern the magnitude.</para>
    ///
    /// <para><b>Rebounding-specific.</b> This helper is intentionally separate from
    /// <see cref="Postness"/> and <see cref="PositionalWeight"/> — adding it there
    /// would silently change turnover pickers and steals, which must not be touched
    /// here (Phase 35 invariant #4).</para>
    ///
    /// <para>Used by <see cref="OffensiveRebounderPicker"/> and
    /// <c>DefensiveRebounderPicker</c> at the attribution layer — not in the team
    /// battle, where wingspan already enters via <see cref="ReboundPhysical"/>.</para>
    /// </summary>
    public static double ReboundWingspanMultiplier(
        double playerWingspan,
        double lineupMeanWingspan,
        MatchupConfig cfg)
        => 1.0 + cfg.ReboundWingspanSwing
               * Math.Tanh((playerWingspan - lineupMeanWingspan) / cfg.ReboundWingspanScale);

    /// <summary>
    /// The positional composite for rebounding (Phase 10, stage 2). A weighted
    /// read of how "post-like" a player is — used to sort who within a lineup
    /// is positioned to snag a board. Combines height, post defense, and strength
    /// in config-tunable proportions.
    ///
    /// <para>The positional weight per player is computed <em>relative to the
    /// lineup mean</em> (see <see cref="OffensiveReboundShare"/>), so even a
    /// positionless 5-out lineup always has a relative post (the tallest/strongest
    /// player is the big). Blend weights need not sum to 1 — they are a weighted
    /// read, like <see cref="LengthRating"/>.</para>
    /// </summary>
    public static double Postness(Player p, MatchupConfig cfg)
        => cfg.PostnessHeight      * p.Height
         + cfg.PostnessPostDefense * p.PostDefense
         + cfg.PostnessStrength    * p.Strength;

    /// <summary>
    /// The positional weight for one player within a lineup (Phase 10, stage 2).
    /// Returns a value in <c>(1 − swing, 1 + swing)</c> ≈ <c>(0.8, 1.2)</c> at
    /// the default swing of 0.2. Exactly 1.0 at the lineup mean; monotonically
    /// increasing with post-ness; bounded by the tanh asymptote.
    ///
    /// <para>A post (above-mean post-ness) gets a weight above 1.0; a guard
    /// (below-mean) gets a weight below 1.0. The weighted mean over the whole
    /// lineup is exactly 1.0 when the weights are balanced — so the aggregate
    /// rebounding read is not inflated or deflated by this step.</para>
    ///
    /// <para><b>Public and static</b> so the harness can verify the math directly
    /// — same pattern as <see cref="BlockWeight"/> and <see cref="FoulRate"/>.</para>
    /// </summary>
    public static double PositionalWeight(double playerPostness, double lineupMeanPostness, MatchupConfig cfg)
        => 1.0 + cfg.ReboundPositionalSwing
               * Math.Tanh((playerPostness - lineupMeanPostness) / cfg.ReboundPositionalScale);

    /// <summary>
    /// The matchup-bent offensive-rebound share (Phase 10). Starts at
    /// <paramref name="baseOffShare"/> (the natural share of the Def+Off mass for
    /// this source) and bends it toward a ceiling (offense crashes successfully) or
    /// floor (defense locks the glass) via a tanh saturation.
    ///
    /// <para><b>Two contributions, additively composed (same shape as
    /// <see cref="BlockWeight"/>):</b>
    /// <list type="number">
    ///   <item>Size shift: team A's mean <see cref="ReboundPhysical"/> vs team B's.
    ///         Positive = offense bigger = off-share up.</item>
    ///   <item>Positional-weighted skill shift: each player's rebounding rating
    ///         multiplied by a <see cref="PositionalWeight"/> (posts up, guards down,
    ///         exactly 1 at the lineup mean), plus a shooter nerf on
    ///         <c>Three/Long/Mid</c>. The difference in the two teams' weighted
    ///         means goes through <see cref="GapFn"/> — positive = offense better
    ///         at crashing = off-share up.</item>
    /// </list>
    /// Weighted sum → tanh → added to <paramref name="baseOffShare"/> (plain
    /// addition; tanh is odd and supplies the sign — the Session 38 lesson).</para>
    ///
    /// <para><b>Degenerate aggregation.</b> A team with Σ posWeight = 0 cannot
    /// happen when swing &lt; 1 (all weights are in (0, 2)). A zero-populated team
    /// must already be short-circuited by the generator BEFORE this method is called
    /// — the generator documents that precondition, mirroring
    /// <see cref="DefensiveResistance"/>'s zero-defender precondition.</para>
    ///
    /// <para><b>Pure and static.</b> No state, no RNG. The harness calls this
    /// directly in <c>Phase10ReboundDoorCheck</c>.</para>
    /// </summary>
    public static double OffensiveReboundShare(
        IReadOnlyList<Player?> offense,
        IReadOnlyList<Player?> defense,
        int                    shooterIdx,   // index into offense list; -1 if unknown
        ShotLocation           zone,
        double                 baseOffShare,
        MatchupConfig          cfg)
    {
        // ── Stage 1: pre-staging size shift (team-vs-team) ──────────────────
        var offPhys = new List<double>();
        foreach (var p in offense) if (p is not null) offPhys.Add(ReboundPhysical(p, cfg));
        var defPhys = new List<double>();
        foreach (var p in defense) if (p is not null) defPhys.Add(ReboundPhysical(p, cfg));

        var offSize = offPhys.Count > 0 ? offPhys.Average() : 50.0;
        var defSize = defPhys.Count > 0 ? defPhys.Average() : 50.0;
        var sizeShift = GapFn(offSize - defSize, cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);

        // ── Stage 2: positional-weighted skill shift (intra-team) ─────────
        // Compute postness for each player, then lineup mean.
        var offPostness = new List<(double pn, double offReb, bool isShooter)>();
        for (var i = 0; i < offense.Count; i++)
        {
            var p = offense[i];
            if (p is not null)
                offPostness.Add((Postness(p, cfg), p.OffensiveRebounding, i == shooterIdx));
        }
        var defPostness = new List<(double pn, double defReb)>();
        foreach (var p in defense)
            if (p is not null)
                defPostness.Add((Postness(p, cfg), p.DefensiveRebounding));

        var offMeanPn = offPostness.Count > 0 ? offPostness.Average(x => x.pn) : 50.0;
        var defMeanPn = defPostness.Count > 0 ? defPostness.Average(x => x.pn) : 50.0;

        // Zones where the shooter nerf applies.
        var nerfZones = zone is ShotLocation.Three or ShotLocation.Long or ShotLocation.Mid;

        // Offense: weighted mean of OffensiveRebounding × posWeight × nerf
        var offWSum = 0.0; var offNumer = 0.0;
        foreach (var (pn, offReb, isShooter) in offPostness)
        {
            var pw   = PositionalWeight(pn, offMeanPn, cfg);
            var nerf = isShooter && nerfZones ? cfg.ReboundShooterNerf : 1.0;
            offNumer += offReb * pw * nerf;
            offWSum  += pw;
        }
        var offWeightedReb = offWSum > 0.0 ? offNumer / offWSum : 50.0;

        // Defense: weighted mean of DefensiveRebounding × posWeight
        var defWSum = 0.0; var defNumer = 0.0;
        foreach (var (pn, defReb) in defPostness)
        {
            var pw = PositionalWeight(pn, defMeanPn, cfg);
            defNumer += defReb * pw;
            defWSum  += pw;
        }
        var defWeightedReb = defWSum > 0.0 ? defNumer / defWSum : 50.0;

        var skillShift = GapFn(offWeightedReb - defWeightedReb, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);

        // ── Compose + bend (BlockWeight shape) ──────────────────────────────
        var totalShift = cfg.ReboundSizeWeight * sizeShift + cfg.ReboundSkillWeight * skillShift;
        var ceiling    = cfg.ReboundOffShareCeiling;
        var floor      = cfg.ReboundOffShareFloor;
        var span       = totalShift >= 0.0 ? (ceiling - baseOffShare) : (baseOffShare - floor);
        var bend       = span * Math.Tanh(totalShift / cfg.ReboundReferenceShift);

        // Plain addition — tanh is odd and supplies the sign.
        // The Session 38 lesson: do NOT write `bend if shift >= 0 else -bend`;
        // bend is already negative when totalShift is negative, and -bend would
        // flip it the wrong way.
        return baseOffShare + bend;
    }

    // =========================================================================
    // Phase 12 — pressure / disruption door (Roll F)
    // =========================================================================

    /// <summary>
    /// The disruption-face of the pressure model (Phase 12). Returns
    /// (<c>finalTurnoverShare</c>, <c>finalFoulShare</c>) as shares of the Roll F
    /// action mass (= BaseShotAttempt + BaseTurnover + BaseNonShootingFoul). The
    /// generator does the three-way mass split and pins JumpBall; this method only
    /// computes the two moving shares.
    ///
    /// <para><b>Two jobs of pressure on the steal/turnover slice.</b>
    /// <list type="number">
    ///   <item>A flat, skill-independent lift: even a neutral matchup produces a
    ///         positive TO lift when pressure is above neutral.</item>
    ///   <item>Pressure gates how much the matchup matters: at low pressure even great
    ///         hands generate almost nothing; at high pressure ball-hawks feast.
    ///         The gate is <c>pressureGate = max(0, pUnit)</c>, so at backed-off
    ///         pressure the matchup contribution is zeroed out entirely.</item>
    /// </list>
    /// Both jobs are captured by a single term: <c>pressureLift + pressureGate × matchupShift</c>,
    /// where <c>matchupShift = GapFn(defender.Steals − handler.BallHandling, ...)</c>.
    /// One term captures "high steals climbs faster" and "big gap climbs faster" —
    /// they are the same lever through <see cref="GapFn"/>.</para>
    ///
    /// <para><b>Foul slice: pressure only.</b> The non-shooting reach-in foul tracks
    /// aggression, not skill — the handling-vs-steals matchup does NOT steepen it.
    /// <c>foulShift = pressureLift</c> with NO matchup term.</para>
    ///
    /// <para><b>Gradual low cap.</b> <see cref="MatchupConfig.TurnoverCeiling"/> is
    /// deliberately LOW and <see cref="MatchupConfig.PressureReferenceShift"/> is
    /// deliberately HIGH relative to the pUnit range. Together they make the climb
    /// gradual and saturate well short of absurd steal rates. Nobody gets 5 steals
    /// a game no matter how high pressure goes.</para>
    ///
    /// <para><b>Changed calibration anchor.</b> Unlike prior doors where an even
    /// matchup always reproduces the config baseline, here that sub-invariant only
    /// holds at <em>neutral pressure</em>. (neutral pressure + even matchup) = today's
    /// flat rates. This is Emmett's basketball call — pressure is the new axis.</para>
    ///
    /// <para><b>Plain addition (Session 38 lesson).</b> <c>Math.Tanh</c> is odd and
    /// already negative when <c>shift</c> is negative — do NOT write
    /// <c>bend if shift ≥ 0 else -bend</c>. That flips the sign of an already-negative
    /// bend, pushing the result toward the ceiling instead of the floor. Same lesson
    /// as <see cref="BlockWeight"/> and <see cref="OffensiveReboundShare"/>.</para>
    ///
    /// <para><b>Caller responsibility.</b> The generator short-circuits to the flat
    /// baseline BEFORE calling this method for null slots, absent players, or empty
    /// defense — same precondition pattern as <see cref="BlockWeight"/> and
    /// <see cref="FoulRate"/>.</para>
    /// </summary>
    /// <param name="handler">The on-ball offensive player (the handler being pressed).</param>
    /// <param name="defender">The slot-matched defender.</param>
    /// <param name="pressure">The defending team's pressure dial (1–10 scale).</param>
    /// <param name="baseTurnoverShare">The natural TO share within the action mass
    /// (= BaseTurnover / actionMass). Reproduced exactly at neutral pressure + even matchup.</param>
    /// <param name="baseFoulShare">The natural foul share within the action mass
    /// (= BaseNonShootingFoul / actionMass). Reproduced exactly at neutral pressure.</param>
    /// <param name="cfg">The matchup config — pressure knobs, steal ceiling/floor,
    /// foul ceiling/floor, and the existing GapFn parameters.</param>
    public static (double turnoverShare, double foulShare) DisruptionShares(
        Player handler, Player defender, double pressure,
        double baseTurnoverShare, double baseFoulShare, MatchupConfig cfg)
    {
        // ── Pressure normalization ───────────────────────────────────────────
        // Map the 1–10 dial to a signed unit around neutral.
        // pUnit = 0 at neutral; negative = backed-off; positive = aggressive.
        var pUnit        = (pressure - cfg.PressureNeutral) / cfg.PressureScale;
        var pressureLift = pUnit;
        var pressureGate = Math.Max(0.0, pUnit);   // non-negative; 0 when backed off

        // ── Steal/turnover share — two jobs of pressure ──────────────────────
        // Skill matchup: defender Steals minus handler BallHandling.
        // Positive = defender edge = steals go up. GapFn captures both "high steals"
        // and "big gap" as one term (convex, flat-bottomed, no cap — the cap is the
        // tanh ceiling below).
        var stealGap       = (double)defender.Steals - handler.BallHandling;
        var matchupShift   = GapFn(stealGap, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);

        // Disruption shift: flat lift + pressure-gated matchup.
        // At low pressure, pressureGate ≈ 0 so matchup is muted regardless of attributes.
        // At high pressure, matchupShift is scaled in — ball-hawks feast.
        var disruptionShift = pressureLift + pressureGate * matchupShift;

        var toCeiling   = cfg.TurnoverCeiling;
        var toFloor     = cfg.TurnoverFloor;
        var toSpan      = disruptionShift >= 0.0
                          ? (toCeiling - baseTurnoverShare)
                          : (baseTurnoverShare - toFloor);
        // High PressureReferenceShift → small tanh argument → gradual climb.
        // The "low cap" is TurnoverCeiling; the "gradual" is the high ref shift.
        var toBend      = toSpan * Math.Tanh(disruptionShift / cfg.PressureReferenceShift);
        var finalToShare = baseTurnoverShare + toBend;   // plain addition; tanh supplies the sign

        // ── Foul share — flat-lift only, no matchup term ─────────────────────
        // Reach-in non-shooting fouls track aggression, not skill: any level of
        // BallHandling/Steals matchup produces the same foul rate at the same pressure.
        var foulShift    = pressureLift;                 // NO matchupShift term
        var foulCeiling  = cfg.FoulPressureCeiling;
        var foulFloor    = cfg.FoulPressureFloor;
        var foulSpan     = foulShift >= 0.0
                           ? (foulCeiling - baseFoulShare)
                           : (baseFoulShare - foulFloor);
        var foulBend     = foulSpan * Math.Tanh(foulShift / cfg.PressureReferenceShift);
        var finalFoulShare = baseFoulShare + foulBend;   // plain addition

        return (finalToShare, finalFoulShare);
    }

    /// <summary>
    /// Team-aggregate disruption shares for Roll B (Phase 13). Returns the pressure-
    /// and-matchup-bent turnover and foul shares of the Roll B action mass.
    ///
    /// <para><b>Pressure-only foul slice.</b> The foul slice tracks defensive
    /// aggression, not skill. <c>foulShift = pressureLift</c> with no matchup term —
    /// identical to the foul side of <see cref="DisruptionShares"/>.</para>
    ///
    /// <para><b>Team-aggregate turnover slice (Roll B's distinction from Roll F).</b>
    /// Because no individual player is selected at Roll B (Roll E runs later), the
    /// matchup uses pre-computed slot-weighted team scores: <paramref name="offenseHandling"/>
    /// (weighted BallHandling aggregate, offense) vs. <paramref name="defenseStealers"/>
    /// (weighted Steals aggregate, defense). The gap runs through <see cref="GapFn"/>
    /// with the shared skill parameters, then into the same pressure-gated
    /// disruption-shift formula as <see cref="DisruptionShares"/>.</para>
    ///
    /// <para><b>Roll-B-specific ceilings/floors.</b> Roll B's baseline foul rate
    /// (≈12% of the pie) is far higher than Roll F's (≈5%), and its baseline TO
    /// (≈3%) is lower. Using the Phase 12 ceilings directly would be wrong — this
    /// method reads <see cref="MatchupConfig.RollBTurnoverCeiling"/> etc. instead
    /// of the Phase 12 <see cref="MatchupConfig.TurnoverCeiling"/>.</para>
    ///
    /// <para><b>Plain addition (Session 38 lesson).</b> <c>Math.Tanh</c> is odd and
    /// already negative when the shift is negative. Do NOT flip the sign.</para>
    ///
    /// <para><b>Caller responsibility.</b> The generator falls back to the flat
    /// baseline BEFORE calling this method when either roster is empty.</para>
    /// </summary>
    /// <param name="offenseHandling">Slot-weighted BallHandling aggregate for the
    /// offensive team (guards weighted heaviest).</param>
    /// <param name="defenseStealers">Slot-weighted Steals aggregate for the defensive
    /// team (same weights as offense).</param>
    /// <param name="pressure">The defending team's pressure dial (1–10).</param>
    /// <param name="baseTurnoverShare">Natural TO share within action mass
    /// (= BaseDeadBallTurnover / actionMass). Reproduced exactly at neutral pressure
    /// + even aggregate.</param>
    /// <param name="baseFoulShare">Natural foul share within action mass
    /// (= BaseFoul / actionMass). Reproduced exactly at neutral pressure.</param>
    /// <param name="cfg">Matchup config — pressure knobs, Roll-B-specific
    /// ceilings/floors, slot weights, and shared GapFn parameters.</param>
    public static (double turnoverShare, double foulShare) TeamDisruptionShares(
        double offenseHandling, double defenseStealers, double pressure,
        double baseTurnoverShare, double baseFoulShare, MatchupConfig cfg)
    {
        // ── Pressure normalization ───────────────────────────────────────────
        var pUnit        = (pressure - cfg.PressureNeutral) / cfg.PressureScale;
        var pressureLift = pUnit;
        var pressureGate = Math.Max(0.0, pUnit);

        // ── Team aggregate steal/turnover share ──────────────────────────────
        // Defensive steals advantage → positive gap → more turnovers.
        // pressureGate ≈ 0 at low pressure: matchup is muted regardless of aggregates.
        // At high pressure the gate opens and the team gap drives the outcome.
        var teamGap        = defenseStealers - offenseHandling;
        var matchupShift   = GapFn(teamGap, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        var disruptionShift = pressureLift + pressureGate * matchupShift;

        var toCeiling  = cfg.RollBTurnoverCeiling;
        var toFloor    = cfg.RollBTurnoverFloor;
        var toSpan     = disruptionShift >= 0.0
                         ? (toCeiling - baseTurnoverShare)
                         : (baseTurnoverShare - toFloor);
        var toBend     = toSpan * Math.Tanh(disruptionShift / cfg.PressureReferenceShift);
        var finalToShare = baseTurnoverShare + toBend;   // plain addition; tanh supplies sign

        // ── Foul share — pressure-only, no matchup term ──────────────────────
        var foulShift   = pressureLift;
        var foulCeiling = cfg.RollBFoulPressureCeiling;
        var foulFloor   = cfg.RollBFoulPressureFloor;
        var foulSpan    = foulShift >= 0.0
                          ? (foulCeiling - baseFoulShare)
                          : (baseFoulShare - foulFloor);
        var foulBend    = foulSpan * Math.Tanh(foulShift / cfg.PressureReferenceShift);
        var finalFoulShare = baseFoulShare + foulBend;   // plain addition

        return (finalToShare, finalFoulShare);
    }

    /// <summary>
    /// Phase 15 — Roll A's four-way disruption bend (backcourt entry, Standard press).
    /// Returns the three bent action-mass shares: turnover, defensive foul, and offensive
    /// foul. The caller (<see cref="RollAGenerator"/>) uses these to split the four-way
    /// mass and pin JumpBall exactly flat.
    ///
    /// <para><b>Input contract.</b> <paramref name="baseTurnoverShare"/>,
    /// <paramref name="baseDefFoulShare"/>, and <paramref name="baseOffFoulShare"/> are
    /// <b>action-mass shares, not raw pie probabilities</b>. The caller is responsible for
    /// dividing Roll A's base masses by actionMass before calling. This mirrors the contract
    /// of <see cref="DisruptionShares"/> and <see cref="TeamDisruptionShares"/>.</para>
    ///
    /// <para><b>Turnover — Standard lift + three-gap matchup (Phase 15).</b>
    /// The press decision (whether to press) is made upstream by the Resolver and stamped
    /// on <see cref="PossessionState.PressMode"/> — this method is only called when
    /// <c>PressMode == Standard</c>. Three gap terms compose additively into one
    /// matchupShift, then the full disruption shift is:
    /// <c>disruptionShift = cfg.StandardLift + cfg.StandardGate × (skillWeight·skillShift
    /// + athWeight·athShift + sizeWeight·sizeShift)</c>.
    /// (1) <b>Skill</b>: slot-weighted Steals − BallHandling → <see cref="GapFn"/> with skill params.
    /// (2) <b>Athleticism</b>: slot-weighted Athleticism composite gap → GapFn with physical params.
    /// (3) <b>Size</b>: slot-weighted <see cref="LengthRating"/> gap → GapFn with physical params;
    /// weight is the smallest of the three (<see cref="MatchupConfig.StandardSizeWeight"/>).
    /// The tanh saturation uses <see cref="MatchupConfig.FullCourtPressReferenceShift"/> — a
    /// separate constant from the halfcourt <see cref="MatchupConfig.PressureReferenceShift"/>
    /// so the two layers stay fully independent.</para>
    ///
    /// <para><b>DefFoul — Standard lift only, no matchup term.</b> Reach-in fouls track
    /// defensive aggression, not skill. Uses <see cref="MatchupConfig.StandardDefFoulCeiling"/>
    /// and <see cref="MatchupConfig.StandardDefFoulFloor"/>. Saturation via
    /// <see cref="MatchupConfig.FullCourtPressReferenceShift"/>.</para>
    ///
    /// <para><b>OffFoul — Standard lift only, ceiling ≈ 15% of DefFoul ceiling.</b> Player-
    /// control fouls also track aggression, not skill, but are far rarer than reach-ins.
    /// Uses <see cref="MatchupConfig.StandardOffFoulCeiling"/> and
    /// <see cref="MatchupConfig.StandardOffFoulFloor"/>. Same saturation constant.</para>
    ///
    /// <para><b>Plain addition throughout</b> (Session 38 lesson — tanh supplies the sign).
    /// </para>
    /// </summary>
    /// <param name="offenseHandling">Slot-weighted BallHandling aggregate for the offense.</param>
    /// <param name="defenseStealers">Slot-weighted Steals aggregate for the defense.</param>
    /// <param name="offenseAthletic">Slot-weighted <see cref="Player.Athleticism"/> composite
    /// for the offense.</param>
    /// <param name="defenseAthletic">Slot-weighted <see cref="Player.Athleticism"/> composite
    /// for the defense.</param>
    /// <param name="offenseLength">Slot-weighted <see cref="LengthRating"/> composite for
    /// the offense.</param>
    /// <param name="defenseLength">Slot-weighted <see cref="LengthRating"/> composite for
    /// the defense.</param>
    /// <param name="baseTurnoverShare">BaseTurnover / actionMass (normalized share).</param>
    /// <param name="baseDefFoulShare">BaseDefensiveFoul / actionMass (normalized share).</param>
    /// <param name="baseOffFoulShare">BaseOffensiveFoul / actionMass (normalized share).</param>
    /// <param name="cfg">Matchup config supplying shared normalization knobs, Standard-specific
    /// ceilings/floors, gap weights, and the separate full-court saturation constant.</param>
    public static (double turnoverShare, double defFoulShare, double offFoulShare)
    EntryDisruptionShares(
        double offenseHandling, double defenseStealers,
        double offenseAthletic, double defenseAthletic,
        double offenseLength, double defenseLength,
        double baseTurnoverShare, double baseDefFoulShare, double baseOffFoulShare,
        MatchupConfig cfg)
    {
        // ── Turnover: Standard lift + gated THREE-GAP matchup ────────────────
        var skillShift = GapFn(defenseStealers - offenseHandling,
                               cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        var athShift   = GapFn(defenseAthletic - offenseAthletic,
                               cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);
        var sizeShift  = GapFn(defenseLength   - offenseLength,
                               cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);
        var matchupShift    = cfg.StandardSkillWeight       * skillShift
                            + cfg.StandardAthleticismWeight * athShift
                            + cfg.StandardSizeWeight        * sizeShift;
        var disruptionShift = cfg.StandardLift + cfg.StandardGate * matchupShift;

        var toCeiling    = cfg.StandardTurnoverCeiling;
        var toFloor      = cfg.StandardTurnoverFloor;
        var toSpan       = disruptionShift >= 0.0
                           ? (toCeiling - baseTurnoverShare)
                           : (baseTurnoverShare - toFloor);
        var toBend       = toSpan * Math.Tanh(disruptionShift / cfg.FullCourtPressReferenceShift);
        var finalToShare = baseTurnoverShare + toBend;   // plain addition; tanh supplies sign

        // ── DefFoul: Standard lift only, no matchup term ──────────────────────
        var dfCeiling         = cfg.StandardDefFoulCeiling;
        var dfFloor           = cfg.StandardDefFoulFloor;
        var dfSpan            = cfg.StandardLift >= 0.0
                                ? (dfCeiling - baseDefFoulShare)
                                : (baseDefFoulShare - dfFloor);
        var dfBend            = dfSpan * Math.Tanh(cfg.StandardLift / cfg.FullCourtPressReferenceShift);
        var finalDefFoulShare = baseDefFoulShare + dfBend;   // plain addition

        // ── OffFoul: Standard lift only, ceiling ~15% of DefFoul ceiling ──────
        var ofCeiling         = cfg.StandardOffFoulCeiling;
        var ofFloor           = cfg.StandardOffFoulFloor;
        var ofSpan            = cfg.StandardLift >= 0.0
                                ? (ofCeiling - baseOffFoulShare)
                                : (baseOffFoulShare - ofFloor);
        var ofBend            = ofSpan * Math.Tanh(cfg.StandardLift / cfg.FullCourtPressReferenceShift);
        var finalOffFoulShare = baseOffFoulShare + ofBend;   // plain addition

        return (finalToShare, finalDefFoulShare, finalOffFoulShare);
    }
}
