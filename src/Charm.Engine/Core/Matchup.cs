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
        => DefenseRatingRaw(zone, d.PerimeterDefense, d.PostDefense, d.RimProtection, cfg);

    /// <summary>
    /// The same per-zone defensive blend over raw attribute values (Session 36
    /// extraction). <see cref="DefenseRating"/> delegates here with the Player's
    /// int attributes (lossless widening — behavior byte-identical); the
    /// displacement derivation feeds it <see cref="DisplacementDefender"/> reads,
    /// which may be non-integral (the golden fixture's level-matched vector
    /// solves PostDefense to a fraction). One blend, two feeders.
    /// </summary>
    public static double DefenseRatingRaw(ShotLocation zone,
                                          double perimeterDefense,
                                          double postDefense,
                                          double rimProtection,
                                          MatchupConfig cfg)
    {
        var (perimeter, post, rim) = cfg.BlendWeights(zone);
        return perimeter * perimeterDefense
             + post      * postDefense
             + rim       * rimProtection;
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

    // =========================================================================
    // Phase 45 — Hustle helpers (centralized, list-based)
    // =========================================================================

    /// <summary>
    /// Fixed-five Hustle mean. Slots 0–4; null/missing → neutral 50.
    /// Denominator always 5 — matches the fixed-denominator discipline of all
    /// team aggregates. One absent player on a full roster contributes 50, not
    /// an omission, so a single elite hustler is a sliver (56.0), not the full 80.
    /// </summary>
    public static double TeamMeanHustle(IReadOnlyList<Player?> players)
    {
        var sum = 0.0;
        for (var i = 0; i < 5; i++)
        {
            var p = i < players.Count ? players[i] : null;
            sum += p?.Hustle ?? 50.0;
        }
        return sum / 5.0;
    }

    /// <summary>
    /// Signed Hustle gap: offense mean minus defense mean.
    /// Positive = offense out-hustles. Consumers flip sign as needed.
    /// </summary>
    public static double HustleGap(
        IReadOnlyList<Player?> offense,
        IReadOnlyList<Player?> defense)
        => TeamMeanHustle(offense) - TeamMeanHustle(defense);

    /// <summary>
    /// Hustle gap shift via <see cref="GapFn"/>. Do NOT substitute raw
    /// <c>Math.Tanh</c>: tanh is near-linear at zero, making a 1-point gap
    /// ~25× more perceptible than the intended zero-slope / convex GapFn behavior.
    /// </summary>
    public static double HustleGapShift(
        double gap, double steepness, double exponent, double scale)
        => GapFn(gap, steepness, exponent, scale);

    /// <summary>
    /// The matchup-adjusted effective rating fed to the make-curve (DEC-2). Builds the
    /// shooter's baseline, the defender's blended defensive read, the skill gap and the
    /// physical (athletic) gap, runs each through <see cref="GapFn"/> with its own
    /// steepness/exponent, and sums additively onto the baseline. The caller passes the
    /// result to <see cref="RollHConfig.MakeProbability"/>, which is untouched.
    /// </summary>
    public static double EffectiveRating(ShotLocation zone, Player attacker, Player defender, MatchupConfig cfg)
        => EffectiveRating(zone, attacker, defender, cfg,
                           attacker.Athleticism, defender.Athleticism);

    /// <summary>
    /// Fatigue-aware overload (Phase 49). Identical to the 4-arg
    /// <see cref="EffectiveRating(ShotLocation,Player,Player,MatchupConfig)"/> except the
    /// PHYSICAL gap uses the caller-supplied EFFECTIVE athleticism values — authored
    /// athleticism discounted by each player's current fatigue and role — instead of the raw
    /// composites. The skill baseline and skill gap are untouched: fatigue rides the athletic
    /// axis only. Kept pure/static — the caller (which holds the GameState) computes the
    /// effective values via <see cref="FatigueTracker.EffectiveAthleticism"/> and passes them
    /// in; Matchup never reaches the fatigue tracker. The 4-arg path delegates here with raw
    /// athleticism, which is exactly the fresh (no-fatigue) case, so the analytic make-curve
    /// sweep and the no-defender fallback keep their existing behavior.
    /// </summary>
    public static double EffectiveRating(ShotLocation zone, Player attacker, Player defender,
                                         MatchupConfig cfg,
                                         double attackerEffectiveAthleticism,
                                         double defenderEffectiveAthleticism)
    {
        var baseline = OffenseRating(zone, attacker);
        var defense  = DefenseRating(zone, defender, cfg);

        var skillShift    = GapFn(baseline - defense,
                                  cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        var physicalShift = GapFn(attackerEffectiveAthleticism - defenderEffectiveAthleticism,
                                  cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);
        var heightShift   = HeightOverDefenderShift(zone, attacker, defender, cfg);

        return baseline + skillShift + physicalShift + heightShift;
    }

    /// <summary>
    /// Session 55 — the height-over-defender make term (v1). A one-sided, zone-weighted,
    /// saturating reach advantage added to <see cref="EffectiveRating"/>. Standing reach is
    /// (Height + Wingspan) / 2 — the float divide is deliberate (both are int ratings; an
    /// odd sum like 85+88 is 86.5, not 86). The gap is ONE-SIDED: it rewards the taller
    /// shooter and is exactly zero when the shooter is equal or shorter (the shorter
    /// shooter is already handled by the block channel; the negative side is a parked v2
    /// call). tanh saturates the advantage so an extreme mismatch approaches, but never
    /// exceeds, the per-zone cap (HeightMaxBonus × zone weight). Zero at Three by weight.
    /// This is deliberately NOT <see cref="LengthRating"/>: reach here excludes Vertical.
    /// </summary>
    public static double HeightOverDefenderShift(
        ShotLocation zone, Player attacker, Player defender, MatchupConfig cfg)
    {
        var gap = Math.Max(0.0, Reach(attacker) - Reach(defender));   // ONE-SIDED (v1)
        return cfg.HeightZoneWeight(zone) * cfg.HeightMaxBonus
             * Math.Tanh(gap / cfg.HeightReferenceScale);
    }

    /// <summary>Standing reach (Session 55) — the single source of the (Height + Wingspan)/2
    /// read for the height-over-defender make term. The divide is by 2.0 on purpose: Height
    /// and Wingspan are int ratings, so an odd sum (e.g. 85+88) is 86.5, never truncated to
    /// 86. Deliberately excludes Vertical (that is <see cref="LengthRating"/>'s job, for the
    /// block door).</summary>
    public static double Reach(Player p) => (p.Height + p.Wingspan) / 2.0;

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
        => BlockBend(zone, BlockDuelShift(zone, shooter, defender, cfg), baseBlockWeight, cfg);

    /// <summary>
    /// The shooter-vs-matched-defender duel shift (Session 79 extraction) — the pre-tanh
    /// quantity <see cref="BlockWeight"/> has always computed, pulled out under a name so the
    /// S79 help arm can be added to it in the SAME shift space and so the oracle/harness bind
    /// to this exact expression rather than a copy of it.
    ///
    /// <para><b>Byte-identical to the pre-S79 inline arithmetic.</b> Same operations, same
    /// order, same association — an extraction, not a rewrite. Phase 7 is unmoved.</para>
    ///
    /// <para><b>Shooter-RELATIVE.</b> Unlike <see cref="BlockDefenderThreat"/> (which reads a
    /// defender against a neutral attacker), this is a duel: both terms are differences against
    /// THIS shooter. It is negative whenever the shooter wins the matchup.</para>
    /// </summary>
    public static double BlockDuelShift(ShotLocation zone, Player shooter, Player defender,
                                        MatchupConfig cfg)
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
        return sw * skillShift + lw * lengthShift;
    }

    /// <summary>
    /// The tanh saturation from a total pre-bend shift to a block rate (Session 79 extraction).
    /// span is the headroom from the baseline toward the ceiling (defender edge) or the floor
    /// (shooter edge); tanh is odd and bounded in (−1, +1), so the result never crosses either
    /// asymptote however extreme the shift.
    ///
    /// <para>bend is naturally negative when the shift is negative, so a plain addition bends
    /// down toward the floor for a shooter edge and up toward the ceiling for a defender edge —
    /// no sign flip. The original spec's <c>(shift >= 0 ? bend : -bend)</c> was wrong: negating
    /// an already-negative bend returns a positive value, bending the wrong way (Session 38).</para>
    ///
    /// <para><b>Monotone across the sign change.</b> The span switches at shift 0, but tanh(0) = 0
    /// on both sides and both slopes are positive, so the rate is continuous and non-decreasing in
    /// the shift — the invariant the S79 help arm relies on when it pushes a negative duel
    /// positive.</para>
    /// </summary>
    public static double BlockBend(ShotLocation zone, double totalShift,
                                   double baseBlockWeight, MatchupConfig cfg)
    {
        var ceiling = cfg.BlockCeiling(zone);
        var floor   = cfg.BlockFloor(zone);
        var span    = totalShift >= 0.0 ? (ceiling - baseBlockWeight) : (baseBlockWeight - floor);
        var bend    = span * Math.Tanh(totalShift / cfg.BlockReferenceShift);
        return baseBlockWeight + bend;
    }

    // =========================================================================
    // Session 79 — THE HELP ARM: a weakside shot blocker who is guarding nobody
    // =========================================================================
    //
    // Emmett's model (2026-07-26): at the rim roughly half of blocks are not the man
    // guarding the shooter; the tie to the matched man strengthens as shots move out.
    // High help defense PLUS high rim protection is a shot-blocking menace. Help scales
    // with how deep a man plays — a 5's help is worth more than a 1's — with a floor so a
    // point guard with real instincts still gets paid.
    //
    // Before S79 the located-shot block rate consulted exactly ONE defender, so an elite
    // rim protector not guarding the ball changed the team block rate by ZERO. The help arm
    // is composed with the duel in PRE-TANH SHIFT SPACE, before the existing floor/ceiling
    // transform, so there is one probability calculus, the zone ceiling still binds, and the
    // shape matches PutbackBlockRate (which has always been a team stack at the rim).

    /// <summary>
    /// How deep a man plays (Session 79) — a BODY-ONLY read, deliberately distinct from
    /// <see cref="Postness"/>.
    ///
    /// <para><b>Why not Postness.</b> Postness folds in PostDefense, which is also a
    /// <see cref="BlockDefenderThreat"/> input. Because the positional multiplier is
    /// LINEUP-RELATIVE, a shared input couples the two: improving one man's post defense
    /// raised his depth, which raised the lineup mean, which shrank all four teammates'
    /// multipliers — and the team blocked FEWER shots because one defender got better. That
    /// violated the ruled invariant "a better defender never lowers the rate" on 8,237 of
    /// 40,000 sampled real matchups. Reading depth off the body alone decouples them
    /// completely: a skill-only improvement cannot move any depth, so the invariant holds
    /// exactly. Where a man plays is a body fact, not a skill.</para>
    ///
    /// <para>Rebounding's <see cref="Postness"/> is UNCHANGED and still includes PostDefense —
    /// it is a different read for a different job, and the two must not be unified.</para>
    /// </summary>
    public static double BlockHelpDepth(Player p, MatchupConfig cfg)
        => cfg.BlockHelpDepthHeight * p.Height + cfg.BlockHelpDepthStrength * p.Strength;

    /// <summary>The mean <see cref="BlockHelpDepth"/> over the POPULATED defenders — the
    /// denominator for the lineup-relative positional multiplier. Unpopulated slots are
    /// skipped (never counted as a zero-depth player, which would drag the mean down and
    /// silently inflate everyone's multiplier). Returns 0 on an all-empty defense, which the
    /// callers never reach with a live block.</summary>
    public static double BlockHelpMeanDepth(IReadOnlyList<Player?> defenders, MatchupConfig cfg)
    {
        var sum = 0.0;
        var count = 0;
        for (var i = 0; i < defenders.Count; i++)
        {
            if (defenders[i] is null) continue;
            sum += BlockHelpDepth(defenders[i]!, cfg);
            count++;
        }
        return count == 0 ? 0.0 : sum / count;
    }

    /// <summary>
    /// One defender's blocking threat against a NEUTRAL attacker
    /// (<see cref="MatchupConfig.AttributeMidpoint"/>) — the defender-only read
    /// <see cref="PutbackBlockRate"/> has always used, now shared with the located-shot help
    /// arm and with block CREDIT.
    ///
    /// <para>Finisher-independent on purpose: a defender's blocking tools are the same
    /// whatever he is blocking. The shooter enters the RATE once, through
    /// <see cref="BlockDuelShift"/>; he does not enter credit at all (who was in position to
    /// swat is a defender property).</para>
    /// </summary>
    public static double BlockDefenderThreat(ShotLocation zone, Player d, MatchupConfig cfg)
    {
        var (sw, lw) = cfg.BlockContestWeights(zone);
        var skill  = GapFn(DefenseRating(zone, d, cfg) - cfg.AttributeMidpoint,
                           cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        var length = GapFn(LengthRating(d, cfg) - cfg.AttributeMidpoint,
                           cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);
        return sw * skill + lw * length;
    }

    /// <summary>
    /// A helper's readiness to rotate (Session 79) — his help instincts, scaled by how deep he
    /// plays relative to his own lineup: <c>(HelpDefense/100) · (1 + swing·tanh((depth −
    /// lineupMeanDepth)/scale))</c>.
    ///
    /// <para><b>Readiness MULTIPLIES threat, it never substitutes for it</b> (see
    /// <see cref="BlockHelpShift"/>) — elite help defense cannot manufacture a shot blocker out
    /// of a man with no tools. tanh supplies the floor Emmett asked for: a point guard with real
    /// instincts is damped, never zeroed. Lineup-relative, the same idiom as
    /// <see cref="PositionalWeight"/>, so a short D3 frontline still has a relative big.</para>
    /// </summary>
    public static double BlockHelpReadiness(Player d, double lineupMeanDepth, MatchupConfig cfg)
        => (d.HelpDefense / 100.0)
         * (1.0 + cfg.BlockHelpPositionalSwing
                * Math.Tanh((BlockHelpDepth(d, cfg) - lineupMeanDepth) / cfg.BlockHelpPositionalScale));

    /// <summary>One off-ball defender's contribution to the block rate:
    /// <c>max(0, threat) · readiness</c>. The per-defender no-drag floor is
    /// <see cref="PutbackBlockRate"/>'s rule — a below-average helper contributes nothing rather
    /// than dragging the team total down.</summary>
    public static double BlockHelpShift(ShotLocation zone, Player d, double lineupMeanDepth,
                                        MatchupConfig cfg)
        => Math.Max(0.0, BlockDefenderThreat(zone, d, cfg))
         * BlockHelpReadiness(d, lineupMeanDepth, cfg);

    /// <summary>The summed help contribution of the four NON-matched defenders. Null slots
    /// contribute zero and are NOT renormalized away — the sum IS the design (same rule as
    /// <see cref="PutbackBlockRate"/>; contrast the AVERAGING aggregates in RollHGenerator's
    /// C5.5/C6/C7, which divide by capacity).</summary>
    public static double BlockHelpSum(ShotLocation zone, IReadOnlyList<Player?> defenders,
                                      int matchedIndex, MatchupConfig cfg)
    {
        var meanDepth = BlockHelpMeanDepth(defenders, cfg);
        var sum = 0.0;
        for (var i = 0; i < defenders.Count; i++)
        {
            if (i == matchedIndex) continue;      // his contest is the duel arm, not the help arm
            var d = defenders[i];
            if (d is null) continue;
            sum += BlockHelpShift(zone, d, meanDepth, cfg);
        }
        return sum;
    }

    /// <summary>
    /// The Session 79 located-shot block rate: the matched-defender duel PLUS the zone-weighted
    /// help of the other four, composed before the tanh.
    ///
    /// <c>totalShift = duelShift + BlockHelpShare(zone) · Σ helpShift(d)</c>, then
    /// <see cref="BlockBend"/> with the EXISTING per-zone floor, ceiling and reference shift.
    ///
    /// <para>Help is non-negative by the no-drag floor, so adding or improving a helper can
    /// never lower the rate; and because the bend is the existing one, help can never push the
    /// rate past the zone ceiling.</para>
    ///
    /// <para><b>Empty matched slot.</b> Callers keep the DEC-6 fallback and do NOT reach this
    /// method — same contract as <see cref="BlockWeight"/>, which this replaces at the Roll H
    /// call site.</para>
    /// </summary>
    public static double BlockWeightWithHelp(ShotLocation zone, Player shooter, Player defender,
                                             IReadOnlyList<Player?> defenders, int matchedIndex,
                                             double baseBlockWeight, MatchupConfig cfg)
    {
        var duel = BlockDuelShift(zone, shooter, defender, cfg);
        var help = cfg.BlockHelpShare(zone) * BlockHelpSum(zone, defenders, matchedIndex, cfg);
        return BlockBend(zone, duel + help, baseBlockWeight, cfg);
    }

    /// <summary>
    /// WHO gets credited for a located-shot block (Session 79) — the raw per-slot weights
    /// <see cref="BlockerPicker"/> normalizes. Retires <c>BlockerWeight</c>, the six-attribute
    /// weighted sum whose p99/median spread was only 1.48×, so the best rim protector in the
    /// country took 30% of his lineup's blocks against each guard's 17%.
    ///
    /// <para><b>Credit is DEFENDER-ONLY — the shooter is deliberately absent.</b> The prompt's
    /// original rule scored the matched man by <c>max(0, duelShift)</c>; measured on a real
    /// population that is exactly zero 44% of the time (whenever the shooter wins the duel),
    /// which pins the help arm at 100% of the credit in those cases and makes the split
    /// untunable at any share. The shooter decides WHETHER a shot is blocked; he does not decide
    /// WHICH defender got there.</para>
    ///
    /// <para><b>The luck floor.</b> <c>max(0, threat)</c> alone is a hard zero on roughly half
    /// the population — a single elite big beside four average men took 100% of his team's
    /// blocks. <see cref="MatchupConfig.BlockCreditLuckFloor"/> keeps every populated defender
    /// drawable, the same device as <see cref="ReachInPropensity"/>'s LuckFloor and the retired
    /// <c>max(1, …)</c> in the picker. It also makes a zero-mass draw unreachable rather than a
    /// live ~2% path.</para>
    ///
    /// <para>Matched man: <c>LuckFloor + max(0, threat)</c> — no readiness factor, he is ON the
    /// ball and his rotation instincts are irrelevant to his own contest. Helper:
    /// <c>BlockHelpShare(zone) · (LuckFloor + helpShift)</c> — the zone weight scales the whole
    /// helper term, floor included, which is what makes the help share fall away from the rim
    /// (Rim 64% / Short 61% / Mid 37% / Long 20% / Three 14%).</para>
    ///
    /// <para><paramref name="matchedIndex"/> is −1 when no matched defender is resolvable; every
    /// populated slot is then a helper. Null slots weigh exactly 0.</para>
    /// </summary>
    public static double[] BlockCreditWeights(ShotLocation zone, IReadOnlyList<Player?> defenders,
                                              int matchedIndex, MatchupConfig cfg)
    {
        var meanDepth = BlockHelpMeanDepth(defenders, cfg);
        var share     = cfg.BlockHelpShare(zone);
        var w         = new double[5];
        for (var i = 0; i < 5; i++)
        {
            var d = i < defenders.Count ? defenders[i] : null;
            if (d is null) { w[i] = 0.0; continue; }

            w[i] = i == matchedIndex
                 ? cfg.BlockCreditLuckFloor + Math.Max(0.0, BlockDefenderThreat(zone, d, cfg))
                 : share * (cfg.BlockCreditLuckFloor + BlockHelpShift(zone, d, meanDepth, cfg));
        }
        return w;
    }

    /// <summary>
    /// WHO gets credited for a PUTBACK block (Session 79). The putback RATE is unchanged — a
    /// five-defender stack against a neutral finisher — and its per-defender shifts, which
    /// <see cref="PutbackBlockRate"/> computed and then discarded, are exactly the credit.
    ///
    /// <para><b>No matched arm, no zone share.</b> A go-back-up at the rim is contested by the
    /// whole interior; the defender matched to the rebounder has no special role in the putback
    /// rate (RollHGenerator says so explicitly), so he gets none in the credit either. The
    /// shifts are finisher-independent, so this needs no rebounder — which also sidesteps the
    /// bonus-FT putback edge, where the offensive shooter slot is null.</para>
    /// </summary>
    public static double[] PutbackBlockCreditWeights(IReadOnlyList<Player?> defenders,
                                                     MatchupConfig cfg)
    {
        var w = new double[5];
        for (var i = 0; i < 5; i++)
        {
            var d = i < defenders.Count ? defenders[i] : null;
            w[i] = d is null
                 ? 0.0
                 : cfg.BlockCreditLuckFloor + Math.Max(0.0, PutbackDefenderShift(d, cfg));
        }
        return w;
    }

    /// <summary>
    /// The team putback block rate (Putback CONTESTER door) — the go-back-up's block rate as a
    /// FIVE-DEFENDER additive stack, NOT the single matched-defender duel of <see cref="BlockWeight"/>.
    ///
    /// <para><b>Why a team property, not a duel.</b> Whether a putback is swatted is a property of
    /// the whole interior, not just the one defender matched to the rebounder: every defender's
    /// length and rim defense contributes. A frontline of rim protectors swats more putbacks than
    /// one rim protector surrounded by guards. (The located-shot block path stays a duel —
    /// <see cref="BlockWeight"/> — because a located jumper is contested by the matched man; only
    /// the putback go-back-up at the rim is contested by the whole interior. This method is
    /// additive and does NOT modify <see cref="BlockWeight"/>.)</para>
    ///
    /// <para><b>Stack, do NOT average (no dilution).</b> Each defender's blocking threat is
    /// measured against a NEUTRAL (<see cref="MatchupConfig.AttributeMidpoint"/>) finisher —
    /// finisher-independent team strength: a defender's blocking is the same regardless of who he
    /// is blocking; the finisher applies once, separately. The per-defender threats are SUMMED and
    /// NEVER divided by the populated count — one elite shot blocker is undiluted by four weak
    /// teammates, and a missing slot simply contributes zero. A future refactor must NOT
    /// "helpfully" renormalize by player count: the sum IS the design (contrast the AVERAGING
    /// team aggregates in RollHGenerator's C5.5/C6/C7, which divide by capacity).</para>
    ///
    /// <para><b>Per-defender floor (no drag).</b> Each defender's contribution is floored at zero
    /// (<c>max(0, shift)</c>) before summing, so a below-average defender contributes nothing
    /// rather than dragging the team total negative. Weak teammates cannot lower the block rate
    /// below what the capable defenders alone produce.</para>
    ///
    /// <para><b>Finisher resistance.</b> The finisher's own finishing/length above neutral reduces
    /// the block once (signed — a SHORT finisher RAISES it). Net drive is
    /// <c>teamDrive − finisherResist</c>, bent toward the putback ceiling (defense edge) or the
    /// shared rim floor (finisher edge) by the same tanh saturation <see cref="BlockWeight"/> uses,
    /// scaled by <see cref="MatchupConfig.PutbackBlockReferenceShift"/>.</para>
    ///
    /// <para><b>Cross-config baseline guard (A6).</b> The baseline lives in
    /// <see cref="RollHConfig.PutbackBlocked"/> while the ceiling lives in
    /// <see cref="MatchupConfig.PutbackBlockCeiling"/> — two config families. If the baseline were
    /// not strictly between the floor and ceiling, a positive team advantage would bend the rate
    /// the WRONG way (downward). This call site is the only place both values are visible, so the
    /// guard lives here; <see cref="MatchupConfig.Load"/> cannot enforce it (it cannot read
    /// RollHConfig).</para>
    ///
    /// <para><b>Empty-slot fallback (subsumes S22 DEC-6).</b> A null defensive slot contributes
    /// zero threat; an (unreachable) all-empty defense yields zero team drive. The caller's
    /// null-rebounder / unpopulated-roster fallback returns the flat legacy putback pie BEFORE
    /// reaching this method, so it is only ever called with a real rebounder.</para>
    /// </summary>
    /// <summary>
    /// One defender's PRE-FLOOR putback blocking threat (Session 79 extraction) — his rim
    /// defensive read and length composite above neutral, blended by the Rim skill/length split.
    /// Byte-identical to the arithmetic <see cref="PutbackBlockRate"/> ran inline; pulled out so
    /// the rate and <see cref="PutbackBlockCreditWeights"/> bind to ONE expression instead of two
    /// copies that can drift.
    ///
    /// <para>Identical in form to <see cref="BlockDefenderThreat"/> at the Rim zone, and kept
    /// separate on purpose: the putback door is pinned to Rim by construction, while
    /// BlockDefenderThreat is zone-parameterised. Both read the same neutral.</para>
    /// </summary>
    public static double PutbackDefenderShift(Player d, MatchupConfig cfg)
    {
        var (skillW, lengthW) = cfg.BlockContestWeights(ShotLocation.Rim);
        var skill  = GapFn(DefenseRating(ShotLocation.Rim, d, cfg) - cfg.AttributeMidpoint,
                           cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        var length = GapFn(LengthRating(d, cfg) - cfg.AttributeMidpoint,
                           cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);
        return skillW * skill + lengthW * length;
    }

    public static double PutbackBlockRate(
        Player rebounder, IReadOnlyList<Player?> defenders,
        double baseBlockWeight, MatchupConfig cfg)
    {
        // A6 — baseline must lie strictly inside (floor, ceiling), or "more team threat" bends
        // the rate DOWN. Guarded here because the baseline (RollHConfig) and the ceiling
        // (MatchupConfig) are only both visible at this call site.
        var floor = cfg.BlockFloor(ShotLocation.Rim);
        if (baseBlockWeight <= floor || baseBlockWeight >= cfg.PutbackBlockCeiling)
            throw new InvalidOperationException(
                $"PutbackBlockRate: baseline {baseBlockWeight:F4} must lie strictly inside " +
                $"(BlockFloorRim {floor:F4}, PutbackBlockCeiling {cfg.PutbackBlockCeiling:F4}); " +
                "otherwise a stronger defense would lower the block rate.");

        // Per-defender blocking threat vs a NEUTRAL finisher (finisher-independent team strength).
        // Skill = rim defensive read above neutral; length = length composite above neutral;
        // weighted by the Rim skill/length split (the same 40/60 BlockWeight uses at the Rim).
        // SUMMED and floored per defender — NEVER divided by the populated count.
        var (skillW, lengthW) = cfg.BlockContestWeights(ShotLocation.Rim);
        var teamDrive = 0.0;
        for (var i = 0; i < defenders.Count; i++)
        {
            var d = defenders[i];
            if (d is null) continue;   // a missing slot contributes zero (no renormalization)

            // no-drag floor: a weak defender adds nothing rather than dragging the total down
            teamDrive += Math.Max(0.0, PutbackDefenderShift(d, cfg));
        }

        // Finisher resistance vs a NEUTRAL defense — the finisher's finishing/length above neutral
        // reduces the block once (signed: a short finisher RAISES it).
        var finSkill  = GapFn(rebounder.Finishing - cfg.AttributeMidpoint,
                              cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        var finLength = GapFn(LengthRating(rebounder, cfg) - cfg.AttributeMidpoint,
                              cfg.PhysicalSteepness, cfg.PhysicalExponent, cfg.ReferenceScale);
        var finResist = skillW * finSkill + lengthW * finLength;

        // Net drive bent toward ceiling (defense edge) or the shared rim floor (finisher edge) by
        // tanh — same saturation shape as BlockWeight, with the putback ceiling and reference shift.
        var net  = teamDrive - finResist;
        var span = net >= 0.0
            ? (cfg.PutbackBlockCeiling - baseBlockWeight)
            : (baseBlockWeight - floor);
        return baseBlockWeight + span * Math.Tanh(net / cfg.PutbackBlockReferenceShift);
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

        return BlendTopThree(scores, cfg);
    }

    /// <summary>
    /// The top-3 descending blend over a list of per-defender zone scores
    /// (Session 36 extraction — the tail of <see cref="DefensiveResistance"/>,
    /// arithmetic unchanged). Shared by <see cref="DefensiveResistance"/>
    /// (Player-fed) and <see cref="DeriveDisplacement"/>
    /// (<see cref="DisplacementDefender"/>-fed). Sorts the list in place.
    /// </summary>
    private static double BlendTopThree(List<double> scores, MatchupConfig cfg)
    {
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
        return LocationMultiplierFromGap(capability - resistance, cfg);
    }

    /// <summary>
    /// The gap-parameterized entry to the Phase 9 ratio form (Session 36
    /// extraction): <c>exp(log(LocationMaxMultiplier) · tanh(GapFn(gap) /
    /// LocationReferenceShift))</c>. Strictly positive, exactly 1.0 at zero gap,
    /// bounded in <c>(1/LocationMaxMultiplier, LocationMaxMultiplier)</c>.
    ///
    /// <para><see cref="LocationMultiplier"/> delegates here with the raw
    /// capability−resistance gap (behavior unchanged); the displacement
    /// derivation calls it with the RESIDUALIZED gap (Route B) — the same bend,
    /// a different input.</para>
    /// </summary>
    public static double LocationMultiplierFromGap(double gap, MatchupConfig cfg)
    {
        var shift = GapFn(gap, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
        // Ratio form: strictly positive, exactly 1.0 at zero shift, bounded in
        // (1 / LocationMaxMultiplier, LocationMaxMultiplier).
        return Math.Exp(Math.Log(cfg.LocationMaxMultiplier)
                      * Math.Tanh(shift / cfg.LocationReferenceShift));
    }

    // =========================================================================
    // Session 36 — Roll G matchup displacement (Route B residualized bend +
    // the usage-gated asymmetric ladder). Executable spec:
    // tools/displacement_oracle.py (LOCKED SPEC ORACLE v1, 2026-07-04). If this
    // code and the oracle ever disagree, the oracle wins. Design record:
    // docs/rollg-displacement-brief.md.
    // =========================================================================

    /// <summary>The fixed zone order every displacement array uses:
    /// [Rim, Short, Mid, Long, Three] — matches RollGGenerator's convention.</summary>
    private static readonly ShotLocation[] DisplacementZones =
    {
        ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
        ShotLocation.Long, ShotLocation.Three,
    };

    /// <summary>Linear gate: 0 at/below <paramref name="lo"/>, 1 at/above
    /// <paramref name="hi"/>, linear between. Mirrors the oracle's gate().</summary>
    private static double DisplacementGate(double x, double lo, double hi)
    {
        var t = (x - lo) / (hi - lo);
        return t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
    }

    /// <summary>
    /// The full Roll G displacement derivation (Session 36) — mirrors the locked
    /// oracle's <c>derive()</c> stage-for-stage. Pure: reads only its arguments.
    ///
    /// <para><b>The decomposition (Route B, ruled 2026-07-04):</b>
    /// <list type="number">
    ///   <item>Normalize the coached pre-bend baseline (any positive scale in;
    ///         shares summing to 1 out).</item>
    ///   <item>Per-zone raw gap: shooter zone skill − top-3-blended lineup
    ///         resistance (the existing Phase 9 reads, over
    ///         <see cref="DisplacementDefender"/> values).</item>
    ///   <item>Diet-weighted skill level = Σ base[z]·gap[z]; physical level =
    ///         GapFn(shooter athleticism − lineup MEAN athleticism, displacement
    ///         steepness) — gentle by design, the make curve owns the harsh
    ///         physical punishment. Level = skill + physical.</item>
    ///   <item>Residual[z] = gap[z] − SKILL level (physical term feeds the level
    ///         only — the zone-shape read stays purely the skill read).</item>
    ///   <item>The Phase 9 bend runs on RESIDUALS: a uniform defensive upgrade
    ///         moves the shape by exactly zero.</item>
    ///   <item>Displacement: mag = MaxMagnitude · tanh(level/LevelReference) ·
    ///         min(1, UsageScale·usage); the asymmetric ladder multiplies the
    ///         SAME baseline — inward Rim/Short entries gated by the shooter's
    ///         own Finishing/Close when mag &gt; 0, outward push ungated.</item>
    ///   <item>Compose base + Δbend + Δdisplacement, clamp ≥ 0, renormalize
    ///         ONCE. The usage widening (Phase 17) stays downstream, LAST,
    ///         untouched.</item>
    /// </list></para>
    ///
    /// <para><b>Caller's responsibility:</b> at least one defender (the
    /// zero-defender path short-circuits in the generator — no manufactured
    /// neutral defense), and a positive diet total.</para>
    /// </summary>
    /// <param name="dietRaw">The five coached pre-bend baseline tendencies in
    /// zone order [Rim, Short, Mid, Long, Three] — the SAME five values the old
    /// bend multiplied. NOT the raw authored tendencies (those stay the diet
    /// shift's private read; two different reads, deliberate).</param>
    /// <param name="shooter">The shooter — zone skills, Finishing/Close gates,
    /// and the athleticism composite are read.</param>
    /// <param name="defenders">The POPULATED defenders (nulls already filtered)
    /// as <see cref="DisplacementDefender"/> reads.</param>
    /// <param name="usagePressure">This possession's usage pressure; 0 (or a
    /// null coalesced to 0 by the caller) means mag is exactly 0.</param>
    /// <param name="cfg">The matchup config carrying both the Phase 9 bend
    /// constants and the Displacement* block.</param>
    public static DisplacementTrace DeriveDisplacement(
        IReadOnlyList<double> dietRaw,
        Player shooter,
        IReadOnlyList<DisplacementDefender> defenders,
        double usagePressure,
        MatchupConfig cfg)
    {
        if (dietRaw.Count != 5)
            throw new InvalidOperationException(
                $"DeriveDisplacement: dietRaw must have exactly 5 entries (got {dietRaw.Count}).");
        if (defenders.Count == 0)
            throw new InvalidOperationException(
                "DeriveDisplacement: no defenders. Caller must short-circuit the " +
                "zero-defender path BEFORE calling (RollGGenerator does).");

        // ── Stage 1: normalize the baseline. ─────────────────────────────
        var dietTotal = 0.0;
        for (var i = 0; i < 5; i++) dietTotal += dietRaw[i];
        if (dietTotal <= 0.0)
            throw new InvalidOperationException(
                $"DeriveDisplacement: diet total <= 0 ({dietTotal}). " +
                "Player.Validate() and the coaching floor clamp should make this unreachable.");
        var baseDiet = new double[5];
        for (var i = 0; i < 5; i++) baseDiet[i] = dietRaw[i] / dietTotal;

        // ── Stage 2: per-zone raw gaps (existing Phase 9 reads). ─────────
        var gaps = new double[5];
        for (var i = 0; i < 5; i++)
        {
            var zone   = DisplacementZones[i];
            var scores = new List<double>(defenders.Count);
            foreach (var d in defenders)
                scores.Add(DefenseRatingRaw(zone, d.PerimeterDefense, d.PostDefense, d.RimProtection, cfg));
            gaps[i] = OffenseRating(zone, shooter) - BlendTopThree(scores, cfg);
        }

        // ── Stage 3: the level — diet-weighted skill + gentle physical. ──
        var skillLevel = 0.0;
        for (var i = 0; i < 5; i++) skillLevel += baseDiet[i] * gaps[i];

        var lineupMeanAth = 0.0;
        foreach (var d in defenders) lineupMeanAth += d.Athleticism;
        lineupMeanAth /= defenders.Count;
        var physLevel = GapFn(shooter.Athleticism - lineupMeanAth,
                              cfg.DisplacementPhysicalSteepness,
                              cfg.PhysicalExponent,
                              cfg.ReferenceScale);
        var level = skillLevel + physLevel;

        // ── Stage 4: residuals against the SKILL level only (Route B). ───
        var residuals = new double[5];
        for (var i = 0; i < 5; i++) residuals[i] = gaps[i] - skillLevel;

        // ── Stage 5: the Phase 9 bend on residualized gaps. ──────────────
        var bent    = new double[5];
        var bentSum = 0.0;
        for (var i = 0; i < 5; i++)
        {
            bent[i]  = baseDiet[i] * LocationMultiplierFromGap(residuals[i], cfg);
            bentSum += bent[i];
        }
        if (bentSum <= 0.0)
            throw new InvalidOperationException(
                $"DeriveDisplacement: bent total <= 0 ({bentSum}). Should be unreachable — " +
                "multipliers are bounded strictly positive by the ratio form.");
        for (var i = 0; i < 5; i++) bent[i] /= bentSum;

        // ── Stage 6: displacement — bounded, usage-gated, asymmetric ladder. ──
        var mag = cfg.DisplacementMaxMagnitude
                * Math.Tanh(level / cfg.DisplacementLevelReference)
                * Math.Min(1.0, cfg.DisplacementUsageScale * usagePressure);

        var ladder = new[]
        {
            cfg.DisplacementLadderRim,
            cfg.DisplacementLadderShort,
            cfg.DisplacementLadderMid,
            cfg.DisplacementLadderLong,
            cfg.DisplacementLadderThree,
        };
        if (mag > 0.0)
        {
            // §3a R2: the INWARD pull (an inferior lineup inviting the shooter in)
            // is accepted only per his own inside skills; the outward push is
            // unconditional, so mag <= 0 leaves the ladder ungated.
            ladder[0] = cfg.DisplacementLadderRim
                      * DisplacementGate(shooter.Finishing, cfg.DisplacementRimGateLow, cfg.DisplacementRimGateHigh);
            ladder[1] = cfg.DisplacementLadderShort
                      * DisplacementGate(shooter.Close, cfg.DisplacementShortGateLow, cfg.DisplacementShortGateHigh);
        }

        var disp    = new double[5];
        var dispSum = 0.0;
        for (var i = 0; i < 5; i++)
        {
            disp[i]  = Math.Max(0.0, baseDiet[i] * (1.0 + mag * ladder[i]));
            dispSum += disp[i];
        }
        if (dispSum <= 0.0)
            throw new InvalidOperationException(
                $"DeriveDisplacement: displacement total <= 0 ({dispSum}). Should be unreachable — " +
                "|mag·ladder| is bounded well below 1 at the validated constants and the " +
                "baseline sums to 1.");
        for (var i = 0; i < 5; i++) disp[i] /= dispSum;

        // ── Stage 7: compose both deltas from the SAME baseline, clamp,
        //             renormalize ONCE. ──────────────────────────────────
        var final    = new double[5];
        var finalSum = 0.0;
        for (var i = 0; i < 5; i++)
        {
            var dBend = bent[i] - baseDiet[i];
            var dDisp = disp[i] - baseDiet[i];
            final[i]  = Math.Max(0.0, baseDiet[i] + dBend + dDisp);
            finalSum += final[i];
        }
        if (finalSum <= 0.0)
            throw new InvalidOperationException(
                $"DeriveDisplacement: final total <= 0 ({finalSum}). Should be unreachable — " +
                "the pre-clamp entries sum to exactly 1, so at least one is positive.");
        for (var i = 0; i < 5; i++) final[i] /= finalSum;

        return new DisplacementTrace(
            baseDiet, gaps, skillLevel, physLevel, level,
            residuals, bent, mag, ladder, final);
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

    // ── Session 62: per-man NON-SHOOTING (reach-in) foul propensity ───────────
    // Each defender's own propensity to draw a reach-in whistle. Discipline is the
    // PRIMARY driver (symmetric about 50, low D → more fouls — a hacker); athleticism a
    // SMALL secondary; a SLIGHT perimeter lean. Base is fixed at 1.0; the LuckFloor is the
    // only additive knob, keeping every propensity > 0 (no defender un-drawable). Public
    // and static so the harness/oracle can bind to these exact expressions — same pattern
    // as BlockWeight / FoulRate. The team reach-in RATE scales by the per-man aggregate of
    // the five (Rolls A/B/F), and the same propensities weight WHO committed the foul.

    /// <summary>The discipline factor of the reach-in propensity — PRIMARY, symmetric about
    /// 50. <c>1 − DiscSpan·clamp((D−50)/49, −1, +1)</c>: D=0 → 1+span (hacker), D=99 → 1−span
    /// (lockdown), D=50 → 1. Also the situational-foul committer weight (candidate (b)).</summary>
    public static double ReachInDisciplineFactor(double discipline, MatchupConfig cfg)
        => 1.0 - cfg.ReachInDiscSpan * Math.Clamp((discipline - 50.0) / 49.0, -1.0, 1.0);

    /// <summary>The athleticism factor of the reach-in propensity — SMALL secondary.
    /// <c>1 − AthSpan·clamp((A−50)/49, −1, +1)</c>: higher athleticism → slightly fewer
    /// reach-ins. Athleticism is the defender's own Quickness+FirstStep composite.</summary>
    public static double ReachInAthFactor(double athleticism, MatchupConfig cfg)
        => 1.0 - cfg.ReachInAthSpan * Math.Clamp((athleticism - 50.0) / 49.0, -1.0, 1.0);

    /// <summary>Map a defender's raw <see cref="Postness"/> and his lineup's mean postness
    /// into a [0,1] PERIMETER orientation (0 = deepest post, 1 = furthest perimeter, 0.5 at
    /// the lineup mean): <c>0.5 − 0.5·tanh((postness − mean)/PostnessScale)</c>. Lineup-
    /// relative (same idiom as <see cref="PositionalWeight"/>) so the perimeter lean nets to
    /// zero across a balanced lineup — it reweights WHO fouls, not the team rate.</summary>
    public static double ReachInPerimOrientation(double postness, double lineupMeanPostness, MatchupConfig cfg)
        => 0.5 - 0.5 * Math.Tanh((postness - lineupMeanPostness) / cfg.ReachInPostnessScale);

    /// <summary>The perimeter factor of the reach-in propensity — SLIGHT lean.
    /// <c>1 + PerimSpan·(2o − 1)</c> for orientation o in [0,1]: o=1 (perimeter) → 1+span,
    /// o=0 (post) → 1−span, o=0.5 → 1.</summary>
    public static double ReachInPerimFactor(double orientation, MatchupConfig cfg)
        => 1.0 + cfg.ReachInPerimSpan * (2.0 * orientation - 1.0);

    /// <summary>One defender's full reach-in propensity:
    /// <c>LuckFloor + disciplineFactor·athFactor·perimFactor</c> (Base fixed at 1.0). Always
    /// &gt; 0. Drives both the reach-in RATE (via the per-man aggregate over the five
    /// defenders) and the reach-in committer draw. <paramref name="orientation"/> is the
    /// [0,1] value from <see cref="ReachInPerimOrientation"/>.</summary>
    public static double ReachInPropensity(double discipline, double athleticism, double orientation, MatchupConfig cfg)
        => cfg.ReachInLuckFloor
         + ReachInDisciplineFactor(discipline, cfg)
         * ReachInAthFactor(athleticism, cfg)
         * ReachInPerimFactor(orientation, cfg);

    /// <summary>The reference propensity of an average defender (D=50, A=50, o=0.5) —
    /// <c>LuckFloor + 1</c>. The per-man aggregate normalizes by 5× this, so a five-average
    /// lineup yields exactly 1.0 (today's reach-in rate is preserved).</summary>
    public static double ReachInReferencePropensity(MatchupConfig cfg)
        => cfg.ReachInLuckFloor + 1.0;

    /// <summary>The team reach-in RATE multiplier: <c>Σ propensityᵢ / (5 · refProp)</c> over
    /// the five defenders. Exactly 1.0 at five-average (anchor preserved); above 1.0 when the
    /// lineup out-hacks average (a hacker ADDS fouls, does not merely redistribute), below
    /// when it out-disciplines. Linear in each defender's propensity (stackable).</summary>
    public static double ReachInPerManAggregate(IReadOnlyList<Player?> defenders, MatchupConfig cfg)
    {
        if (defenders is null) return 1.0;

        // Mean postness over the populated defenders (denominator for the lineup-relative
        // perimeter orientation). Unpopulated slots are skipped — in production all five are
        // present, but a short lineup must never crash or skew the anchor.
        var meanPostness = 0.0;
        var count = 0;
        for (var i = 0; i < defenders.Count; i++)
        {
            if (defenders[i] is null) continue;
            meanPostness += Postness(defenders[i]!, cfg);
            count++;
        }
        if (count == 0) return 1.0;
        meanPostness /= count;

        var sum = 0.0;
        for (var i = 0; i < defenders.Count; i++)
        {
            var p = defenders[i];
            if (p is null) continue;
            var ath = ((double)p.Quickness + p.FirstStep) / 2.0;
            var o   = ReachInPerimOrientation(Postness(p, cfg), meanPostness, cfg);
            sum += ReachInPropensity(p.Discipline, ath, o, cfg);
        }
        return sum / (count * ReachInReferencePropensity(cfg));
    }

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
    ///
    /// <para><b>Phase 45 — Hustle.</b> A third pre-bend contribution — the team Hustle
    /// gap (offense mean minus defense mean) through <see cref="HustleGapShift"/>, scaled
    /// by <see cref="MatchupConfig.HustleReboundWeight"/> — is added to <c>totalShift</c>
    /// alongside the size and skill shifts, before the tanh, so it respects the same
    /// off-share ceiling/floor. Computed in-method from the <paramref name="offense"/> and
    /// <paramref name="defense"/> lists already passed in (no signature change). Equal-Hustle
    /// teams yield <c>GapFn(0) = 0</c> → no change, so the Phase 10/43 harness checks
    /// (equal Hustle on both sides) are byte-unaffected.</para>
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
        // Phase 45: Hustle gap contribution, computed in-method from the offense/defense
        // lists already passed in (no signature change). Same pre-bend units as sizeShift
        // and skillShift; added to totalShift before the tanh so it respects the off-share
        // ceiling/floor. Equal-Hustle teams → GapFn(0) = 0 → no change. The Phase 10/43
        // harness checks (equal Hustle on both sides) are therefore byte-unaffected.
        var hustleShift = cfg.HustleReboundWeight
                        * HustleGapShift(HustleGap(offense, defense),
                                         cfg.HustleReboundSteepness,
                                         cfg.HustleReboundExponent,
                                         cfg.HustleReboundScale);
        var totalShift = cfg.ReboundSizeWeight * sizeShift + cfg.ReboundSkillWeight * skillShift
                       + hustleShift;
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
    // Unforced-turnover handling curve (v1)
    // =========================================================================

    /// <summary>
    /// Dimensionless handling multiplier on a <b>neutral-pressure</b> turnover base share.
    /// Multiplies each door's own FLAT base (Roll B team-initiation, Roll F individual
    /// action) so that a butterfingers handler coughs it up more and a sure-handed one
    /// less, even against a passive defense — the base was handling-blind before this.
    ///
    /// <para><b>Anchored form.</b>
    /// <c>lift(h) = (1 − tanh((h − Mid) / Scale)) / 2</c>;
    /// <c>g = 1 + SpanFrac · (lift(h) − lift(50))</c>, floor-clamped at <c>UnforcedFloorFrac</c>.
    /// Two anchors fall out for free: <c>g(50) = 1</c> for any span (a league-average
    /// handler reproduces today's rate), and <c>g ≡ 1</c> for every handling when
    /// <c>SpanFrac = 0</c> (the kill switch reproduces today bit-for-bit).</para>
    ///
    /// <para><b>Same curve, both doors.</b> Each door supplies its own flat base, so the
    /// team effect is proportional and exposure-free on a uniform-handling lineup — no
    /// per-door allocation, no exposure measurement. Bad hands raise, good hands lower,
    /// on ONE continuous curve (not one-sided); diminishing returns above ~80. The elite
    /// "floor" is the curve's own asymptote (~0.72× at the shipped span), not the safety
    /// clamp — the clamp sits just below it and does not fire for any authored 0–99 rating.</para>
    /// </summary>
    /// <param name="handling">The BallHandling rating driving the curve (Roll F: the named
    /// handler's; Roll B: the slot-weighted team BallHandling aggregate).</param>
    /// <param name="cfg">Matchup config — supplies UnforcedMid/Scale/SpanFrac/FloorFrac.</param>
    /// <returns>A multiplier applied to the flat turnover base share. 1.0 at handling 50
    /// or at SpanFrac 0.</returns>
    public static double UnforcedFactor(double handling, MatchupConfig cfg)
    {
        double Lift(double h) => (1.0 - Math.Tanh((h - cfg.UnforcedMid) / cfg.UnforcedScale)) / 2.0;
        var g = 1.0 + cfg.UnforcedSpanFrac * (Lift(handling) - Lift(50.0));
        return Math.Max(cfg.UnforcedFloorFrac, g);
    }

    // =========================================================================
    // Session 58 — steal-forcing FLOOR (live at neutral pressure)
    // =========================================================================

    /// <summary>
    /// Continuous perimeter weight for the wingspan deflection term. Slides from
    /// <c>1.0</c> for a guard (postness at/below <see cref="MatchupConfig.WingStealPostnessPivot"/>)
    /// down to <see cref="MatchupConfig.WingStealPerimFloor"/> for a big (postness at/above
    /// pivot + <see cref="MatchupConfig.WingStealPostnessRange"/>). Built on
    /// <see cref="Postness"/> (Height/PostDefense/Strength — reads NO Wingspan, so a
    /// wingspan term gated by this is not circular). Absolute pivot, no lineup handle.
    /// <c>postUnit = clamp((postness − pivot) / range, 0, 1)</c>;
    /// <c>perimW = 1 − (1 − PerimFloor) × postUnit</c>.
    /// </summary>
    public static double WingStealPerimWeight(double postness, MatchupConfig cfg)
    {
        var postUnit = Math.Clamp(
            (postness - cfg.WingStealPostnessPivot) / cfg.WingStealPostnessRange, 0.0, 1.0);
        return 1.0 - (1.0 - cfg.WingStealPerimFloor) * postUnit;
    }

    /// <summary>
    /// The un-gated steal-forcing FLOOR shift (Session 58). Replaces the old
    /// pressure-gated skill contest, which was inert at today's neutral pressure
    /// (<c>pressureGate = 0</c>). Three inputs, each a defender-minus-offense gap:
    /// <list type="bullet">
    ///   <item><b>Athleticism mismatch</b> (PRIMARY): (Quickness+FirstStep)/2 gap,
    ///         through <see cref="GapFn"/> with the Ath knobs. Steepest of the three.</item>
    ///   <item><b>Steal vs ball-control</b> (secondary): Steals − BallHandling, through
    ///         <see cref="GapFn"/> with the StealFloor knobs (reusing ReferenceScale).</item>
    ///   <item><b>Wingspan deflection</b> (two-sided, on top): a <c>tanh</c> of the
    ///         perimeter-gated signed wingspan. Long arms add, short arms cost a little;
    ///         perimeter-gated so a short-armed big is barely affected.</item>
    /// </list>
    /// Pure/static: both the 1v1 door (<see cref="DisruptionShares"/>) and the aggregate
    /// door (<see cref="TeamDisruptionShares"/>) compute their three inputs and call this,
    /// so the two contests cannot drift. <paramref name="wingSigned"/> is
    /// <c>(Wingspan − WingStealRef) × perimW</c>, already perimeter-weighted by the caller
    /// (per-defender in Roll F; per-player inside the weighted aggregate in Roll B).
    /// </summary>
    public static double StealFloorShift(
        double athGap, double stealGap, double wingSigned, MatchupConfig cfg)
        => GapFn(athGap,   cfg.AthStealSteepness,   cfg.AthStealExponent,   cfg.AthStealScale)
         + GapFn(stealGap, cfg.StealFloorSteepness, cfg.StealFloorExponent, cfg.ReferenceScale)
         + cfg.WingStealWeight * Math.Tanh(wingSigned / cfg.WingStealScale);

    // =========================================================================
    // Session 59 — Pass A: the perimeter-defense DRIVE GATE (Roll G, shot DIET only)
    //
    // LOCKED-SPEC PORT — constant-for-constant mirror of tools/drive_gate_oracle.py.
    // If the C# and the oracle ever disagree, THE ORACLE WINS: a Phase 65 parity failure
    // is a PORT BUG, never a tolerance to widen and never a fixture to regenerate.
    // Every magnitude below is a CALIBRATION PLACEHOLDER (page-tuned later, never
    // suite-asserted).
    //
    // WHAT IT IS. A per-man location transform applied AFTER displacement and BEFORE the
    // usage diet shift: given a shooter's post-displacement shot diet and the ONE matched
    // perimeter defender, it removes some of a perimeter driver's rim/short access and
    // re-routes it to his contested Long/Three. Shot DIET only — it NEVER touches make%
    // (no OffenseRating call lives here).
    //
    // WHY IT IS PER-MAN, unlike the rest of Roll G. Displacement reads the whole defending
    // team's shape (where is the defense collectively soft?). This reads exactly ONE
    // defender, because "can this man get past the guy in front of him" is the single most
    // one-on-one decision in the possession. The per-man read is deliberately confined to
    // the drive channel; nothing else in Roll G gains a matched-defender dependency.
    // =========================================================================

    /// <summary>
    /// The drive-tools composite (Session 59) — how well this shooter beats his man off the
    /// dribble. <c>beat = FsW·FirstStep + QW·Quickness</c>, times a handle UNLOCK ramp
    /// <c>clamp01((BallHandling − HandleLo)/(HandleHi − HandleLo))</c>.
    ///
    /// <para><b>The ruling it encodes (Emmett, 2026-07-14): first step BEATS him; the handle
    /// only UNLOCKS it.</b> The weights are asymmetric on purpose (FirstStep primary,
    /// Quickness support), so a burst edge buys more than a lateral-quickness edge. The
    /// handle is a multiplicative gate, not an additive term: an elite handle with no burst
    /// is walled like an average driver (it scales a mediocre beat), while a quick first
    /// step with NO handle scores exactly 0 and is walled HARDEST — he never gets the ball
    /// past the first man (and is turnover-prone elsewhere, via the S56 unforced channel).
    /// BallHandling at/above HandleHi changes this by exactly 0 — the unlock is capped at 1,
    /// so a great handle is a permission slip, never a scoring edge.</para>
    /// </summary>
    public static double DriveTools(Player shooter, MatchupConfig cfg)
    {
        var beat = cfg.DriveBeatFirstStepWeight * shooter.FirstStep
                 + cfg.DriveBeatQuicknessWeight * shooter.Quickness;
        var unlock = Math.Clamp(
            (shooter.BallHandling - cfg.DriveHandleUnlockLo)
                / (cfg.DriveHandleUnlockHi - cfg.DriveHandleUnlockLo),
            0.0, 1.0);
        return beat * unlock;
    }

    /// <summary>
    /// Drive ORIENTATION (Session 59) — is this shooter's scoring identity perimeter-based?
    /// <c>clamp01(1 − (offPostness − Pivot)/Range)</c> over
    /// <c>offPostness = (Height + Strength + PostMoves)/3</c>. Perimeter guard → 1 (fully
    /// gate-eligible); post scorer → 0 (immune — the post route is untouched by a perimeter
    /// wall); point-forward → partial.
    ///
    /// <para><b>Deliberately NOT <see cref="Postness"/>.</b> That helper reads
    /// Height/<b>PostDefense</b>/Strength — a DEFENSE-side "is he a big" read, used by the
    /// rebounding and wing-steal gates. This one asks an OFFENSE-side question — "is HE a
    /// post player" — so it must read <b>PostMoves</b>, his post SCORING skill. The two
    /// agree on most players and diverge exactly where it matters (a post scorer who can't
    /// guard the post, a rim-protecting big with no post game). Reusing Postness here
    /// compiles, looks right, and silently breaks the golden's POST-scorer case. Do not.</para>
    ///
    /// <para>Only the composite converts the removed mass; orientation only decides WHO is
    /// eligible for the gate at all.</para>
    /// </summary>
    public static double DriveOrientation(Player shooter, MatchupConfig cfg)
    {
        var offPostness = (shooter.Height + shooter.Strength + shooter.PostMoves) / 3.0;
        return Math.Clamp(
            1.0 - (offPostness - cfg.DriveOrientPostnessPivot) / cfg.DriveOrientPostnessRange,
            0.0, 1.0);
    }

    /// <summary>
    /// The drive gate itself (Session 59) — pure transform, post-displacement pie →
    /// post-gate pie. Zone order Rim, Short, Mid, Long, Three (the engine's fixed order).
    ///
    /// <para><b>Suppression-PRIMARY.</b> <c>gap = DriveTools − matched.PerimeterDefense</c>;
    /// only the WALL side fires: <c>supp = GapFn(max(0, −gap), …)</c>. Elite D suppresses,
    /// average is ~neutral, and poor D does NOT help — the weak-defender "leak" is the
    /// ABSENCE of a wall, never an added bonus. An offense edge (gap ≥ 0) removes exactly 0.</para>
    ///
    /// <para><b>The transform.</b> <c>mult = 1 − Cap·tanh(supp/TanhRef)</c> (1.0 = no
    /// suppression). Remove <c>orient·(1−mult)</c> of Rim and <c>ShortElig·orient·(1−mult)</c>
    /// of Short — Short is only PARTLY drive-derived (floaters, yes; post-ups and cuts, no),
    /// hence the eligibility fraction. The removed mass redistributes to the CONTESTED
    /// Long/Three only, proportional to the shooter's own pre-gate outer preference (equal
    /// 50/50 when both outer zones are exactly zero). <b>Mid NEVER moves</b> — a denied drive
    /// becomes a contested jumper from distance, not a pull-up. (The "passed it / lost the
    /// possession" outcome is Pass B, not here.)</para>
    ///
    /// <para><b>IDENTITY BRANCH.</b> When <c>removed &lt;= 0</c> — nothing to move: orient 0
    /// (post scorer), suppression 0 (gap ≥ 0, or a flat-50 world), Cap 0 (the kill switch),
    /// no eligible Rim/Short mass, or a null matched defender (bypass) — the INPUT array is
    /// returned untouched: no subtract-0, no redistribute-0, no renormalize. This is what
    /// makes a LIVE gate at flat-50 bit-identical to the Cap-0 kill switch for the RIGHT
    /// reason, rather than differing by a renormalization ULP on a pie that does not sum to
    /// exactly 1.0.</para>
    ///
    /// <para><b>Conservation lives PRE-renormalization.</b> The raw removed mass equals the
    /// raw added mass, so <see cref="DriveGateTrace.RawSum"/> is 1.0 within tolerance on any
    /// normalized input. The renormalize that follows is float hygiene ONLY — never the
    /// conservation mechanism (Phase 65 asserts the raw sum, not just the final one).</para>
    ///
    /// <para><paramref name="matchedDefender"/> is the same-number defending slot resolved by
    /// <see cref="DefenderPicker"/>; a null (that slot is empty) is the BYPASS — the gate does
    /// NOT fall back to a phantom default-50 defender.</para>
    /// </summary>
    public static DriveGateTrace ApplyDriveGate(
        IReadOnlyList<double> pie5, Player shooter, Player? matchedDefender, MatchupConfig cfg)
    {
        if (pie5.Count != 5)
            throw new InvalidOperationException(
                $"ApplyDriveGate: pie5 must have exactly 5 entries (got {pie5.Count}).");

        var input = new double[5];
        for (var i = 0; i < 5; i++) input[i] = pie5[i];

        // BYPASS — no matched man (empty defending slot), FastBreak, or zero populated
        // defenders (the last two already returned upstream in RollGGenerator).
        if (matchedDefender is null)
            return new DriveGateTrace(0.0, 0.0, 0.0, 1.0, 0.0, 1.0, true, input);

        var comp   = DriveTools(shooter, cfg);
        var gap    = comp - matchedDefender.PerimeterDefense;
        var supp   = GapFn(Math.Max(0.0, -gap),
                           cfg.DriveGateSteepness, cfg.DriveGateExponent, cfg.ReferenceScale);
        var mult   = 1.0 - cfg.DriveGateCap * Math.Tanh(supp / cfg.DriveGateTanhRef);
        var orient = DriveOrientation(shooter, cfg);

        var rimRemoved   = input[0] * orient * (1.0 - mult);
        var shortRemoved = input[1] * cfg.DriveGateShortEligibility * orient * (1.0 - mult);
        var removed      = rimRemoved + shortRemoved;

        // IDENTITY BRANCH — see the summary. Return the INPUT untouched.
        if (removed <= 0.0)
            return new DriveGateTrace(comp, gap, orient, mult, 0.0, 1.0, false, input);

        var after = new double[5];
        for (var i = 0; i < 5; i++) after[i] = input[i];
        after[0] -= rimRemoved;
        after[1] -= shortRemoved;

        // Redistribute to the contested outer zones ONLY (Long = 3, Three = 4), proportional
        // to the shooter's pre-gate outer preference. Both-zero is a REAL branch, not a
        // divide-by-zero guard bolted on: with no outer preference to read, the denied drive
        // splits evenly rather than vanishing.
        var outerLong  = input[3];
        var outerThree = input[4];
        var outerSum   = outerLong + outerThree;
        if (outerSum <= 0.0) { outerLong = 1.0; outerThree = 1.0; outerSum = 2.0; }
        after[3] += removed * outerLong  / outerSum;
        after[4] += removed * outerThree / outerSum;

        var rawSum = 0.0;
        for (var i = 0; i < 5; i++) rawSum += after[i];      // conservation lives HERE, pre-renorm
        for (var i = 0; i < 5; i++) after[i] /= rawSum;      // renormalize = float hygiene only

        return new DriveGateTrace(comp, gap, orient, mult, removed, rawSum, false, after);
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
    /// <param name="hustlePressureNudge">Phase 45: pre-saturation Hustle contribution
    /// to the turnover disruption shift. Positive when the defense out-hustles. Added to
    /// disruptionShift BEFORE the tanh so it respects the turnover ceiling. Default 0.0.</param>
    public static (double turnoverShare, double foulShare) DisruptionShares(
        Player handler, Player defender, double pressure,
        double baseTurnoverShare, double baseFoulShare, MatchupConfig cfg,
        double hustlePressureNudge = 0.0)
    {
        // ── Pressure normalization ───────────────────────────────────────────
        // Map the 1–10 dial to a signed unit around neutral.
        // pUnit = 0 at neutral; negative = backed-off; positive = aggressive.
        var pUnit        = (pressure - cfg.PressureNeutral) / cfg.PressureScale;
        var pressureLift = pUnit;
        var pressureGate = Math.Max(0.0, pUnit);   // non-negative; 0 when backed off

        // ── Steal/turnover share — the live attribute FLOOR (Session 58) ─────
        // The old model gated the skill matchup behind pressure (pressureGate = 0 at
        // neutral → inert). It is replaced by a floor that is LIVE at neutral: an
        // athleticism mismatch (primary), the steal-vs-handle contest (secondary), and
        // a two-sided perimeter-gated wingspan term. pressureLift (0 at neutral) stays
        // for the future coaching layer; the old pressureGate × matchupShift is removed.
        // Phase 45: hustlePressureNudge stays, added pre-saturation (not pressure-gated).
        double disruptionShift;
        if (cfg.AthStealSteepness == 0.0 && cfg.StealFloorSteepness == 0.0 && cfg.WingStealWeight == 0.0)
        {
            // Identity path (S57 discipline): all three floor knobs off → reproduce
            // today's ORIGINAL pressure-gated expression byte-for-byte. No GapFns on the
            // floor, no 0 × tanh, no re-associated sum. At neutral pressure this is the
            // flat/hustle path (pressureGate = 0).
            var stealGapKill  = (double)defender.Steals - handler.BallHandling;
            var matchupShift  = GapFn(stealGapKill, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
            disruptionShift   = pressureLift + pressureGate * matchupShift + hustlePressureNudge;
        }
        else
        {
            var athGap     = ((double)defender.Quickness + defender.FirstStep) / 2.0
                           - ((double)handler.Quickness  + handler.FirstStep)  / 2.0;
            var stealGap   = (double)defender.Steals - handler.BallHandling;
            var perimW     = WingStealPerimWeight(Postness(defender, cfg), cfg);
            var wingSigned = ((double)defender.Wingspan - cfg.WingStealRef) * perimW;
            disruptionShift = StealFloorShift(athGap, stealGap, wingSigned, cfg)
                            + pressureLift + hustlePressureNudge;
        }

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
        // Session 62: the Hustle foul nudge is RETIRED. The reach-in RATE is now per-man —
        // the caller (RollFGenerator) scales the returned foul share by the five defenders'
        // aggregate reach-in propensity. Only pressureLift (the coach layer) bends it here.
        var foulShift    = pressureLift;   // NO matchupShift term
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
    /// <param name="hustlePressureNudge">Phase 45: pre-saturation Hustle contribution to
    /// the turnover disruption shift. Positive when the defense out-hustles. Added to
    /// disruptionShift BEFORE the tanh so it respects the turnover ceiling. Default 0.0.</param>
    public static (double turnoverShare, double foulShare) TeamDisruptionShares(
        double offenseHandling, double defenseStealers,
        double offenseAthletic, double defenseAthletic, double defenseWingSigned,
        double pressure,
        double baseTurnoverShare, double baseFoulShare, MatchupConfig cfg,
        double hustlePressureNudge = 0.0)
    {
        // ── Pressure normalization ───────────────────────────────────────────
        var pUnit        = (pressure - cfg.PressureNeutral) / cfg.PressureScale;
        var pressureLift = pUnit;
        var pressureGate = Math.Max(0.0, pUnit);

        // ── Team aggregate steal/turnover share — live FLOOR (Session 58) ────
        // The aggregate analogue of Roll F's defender-vs-handler floor. The guard-heavy
        // SlotWeights already tilt both aggregates toward the players who handle the ball,
        // so a non-handling center barely enters the resistance side and athleticism rides
        // the same touch-weighting as the steal-rating contest beside it. pressureLift
        // (0 at neutral) stays for the coaching layer; the old pressure-gated matchup is
        // removed. hustlePressureNudge stays, added pre-saturation (not pressure-gated).
        double disruptionShift;
        if (cfg.AthStealSteepness == 0.0 && cfg.StealFloorSteepness == 0.0 && cfg.WingStealWeight == 0.0)
        {
            // Identity path (S57 discipline): all three floor knobs off → reproduce
            // today's ORIGINAL pressure-gated aggregate expression byte-for-byte.
            var teamGap      = defenseStealers - offenseHandling;
            var matchupShift = GapFn(teamGap, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
            disruptionShift  = pressureLift + pressureGate * matchupShift + hustlePressureNudge;
        }
        else
        {
            var athGap   = defenseAthletic - offenseAthletic;
            var stealGap = defenseStealers - offenseHandling;
            disruptionShift = StealFloorShift(athGap, stealGap, defenseWingSigned, cfg)
                            + pressureLift + hustlePressureNudge;
        }

        var toCeiling  = cfg.RollBTurnoverCeiling;
        var toFloor    = cfg.RollBTurnoverFloor;
        var toSpan     = disruptionShift >= 0.0
                         ? (toCeiling - baseTurnoverShare)
                         : (baseTurnoverShare - toFloor);
        var toBend     = toSpan * Math.Tanh(disruptionShift / cfg.PressureReferenceShift);
        var finalToShare = baseTurnoverShare + toBend;   // plain addition; tanh supplies sign

        // ── Foul share — pressure-only, no matchup term ──────────────────────
        // Session 62: the Hustle foul nudge is RETIRED. The reach-in RATE is now per-man —
        // the caller (RollBGenerator) scales the returned foul share by the five defenders'
        // aggregate reach-in propensity. Only pressureLift (the coach/pressure layer) bends
        // the share here; team-aggression fouls belong to the coach layer, not this seam.
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

    // ── Phase 36: BLK attribution weight — RETIRED at Session 79 ─────────────
    //
    // Matchup.BlockerWeight was a straight weighted sum of six blocking attributes with
    // thirty per-zone coefficients. It was structurally incapable of expressing a shot
    // blocker: averaging six broadly-correlated ratings compressed the whole population
    // into a p99/median spread of 1.48x, and because BlockerPicker normalizes the weights
    // into shares, ANY affine rescale of the coefficients was a no-op — the fix could
    // never have been a re-tune. Credit is now contribution-based: see
    // BlockCreditWeights and PutbackBlockCreditWeights above. The thirty Blk* config
    // keys are deleted with it.
}

/// <summary>
/// The Session 59 drive-gate trace — every internal the Phase 65 golden pins, so a port bug
/// fails at the stage that broke rather than only at the final pie. Mirrors the dict the
/// locked oracle's <c>gate()</c> returns alongside its pie.
/// </summary>
/// <param name="Composite">The shooter's drive-tools read
/// (<see cref="Matchup.DriveTools"/>) — 0 on the bypass path.</param>
/// <param name="Gap">Composite − matched defender's PerimeterDefense. Positive = the
/// offense beats his man (no suppression); negative = walled.</param>
/// <param name="Orient">Perimeter-orientation eligibility
/// (<see cref="Matchup.DriveOrientation"/>): 1 = guard, 0 = post scorer (immune).</param>
/// <param name="SuppressionMult">1 − Cap·tanh(supp/TanhRef). 1.0 = no suppression.</param>
/// <param name="Removed">Total pie mass taken off Rim + eligible Short. Exactly 0 on the
/// identity branch and on bypass.</param>
/// <param name="RawSum">The pie sum BEFORE renormalization — where conservation actually
/// lives. 1.0 (within tolerance) on any normalized input; pinned to 1.0 on the identity and
/// bypass branches, which never renormalize.</param>
/// <param name="Bypass">True when there was no matched man (empty defending slot). The
/// FastBreak and zero-defender paths return before Roll G ever calls the gate.</param>
/// <param name="Final">The post-gate five shares handed on to the usage diet shift. On the
/// identity and bypass branches this is the INPUT array, untouched.</param>
public readonly record struct DriveGateTrace(
    double Composite,
    double Gap,
    double Orient,
    double SuppressionMult,
    double Removed,
    double RawSum,
    bool Bypass,
    double[] Final);
