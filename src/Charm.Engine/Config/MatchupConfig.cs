using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for the matchup layer (Phase 6 + Phase 7) — nothing hardcoded in logic.
/// Loaded from the "Matchup" section of config.json, mirroring <see cref="RollHConfig.Load"/>.
///
/// <para><b>Three groups.</b> (1) The gap-function parameters (DEC-5): a steepness and an
/// exponent per axis plus a shared reference scale. (2) The per-zone defense-blend table
/// (CONF-1) as data — the blend is config, not a hardcoded switch, so it is tunable in
/// the calibration pass. (3) Phase 7 block-contest parameters: per-zone skill/length
/// weights, per-zone block floor/ceiling, a block reference shift (tanh saturation knob),
/// and the length-composite blend (Height / Wingspan / Vertical).</para>
///
/// <para><b>Defaults are PLACEHOLDERS.</b> The decision they encode is the shape and
/// parameterization (DEC-5) and the blend's perimeter→interior slide (CONF-1); the
/// magnitudes are a calibration-pass concern, set-and-left here. Physical is steeper
/// than skill via a higher exponent (the tail property that delivers "size
/// insurmountable"). The Rim split (0.35 post / 0.65 rim protection) is a placeholder
/// default; the rest of the blend table is spec.</para>
///
/// <para><b>Phase 7 block weights.</b> At Three the contest is 40% skill / 60% length
/// (Emmett's anchor); at Rim the reverse pair 40% skill / 60% physical is the starting
/// placeholder. The length composite (Height + Wingspan + Vertical) / 3 is block-specific
/// because length is what blocks shots; quickness and strength belong to the make door's
/// Athleticism read. Floors are non-zero — even a peak shooter against a stiff is
/// occasionally blocked. Ceilings are placeholders for the calibration pass.</para>
/// </summary>
public sealed class MatchupConfig
{
    // --- Gap function (DEC-5): shift = steepness · sign(gap) · (|gap| / scale)^exponent.
    //     Exponent > 1 is REQUIRED (convex, flat-bottomed) and enforced in Load.
    //     Physical steeper than skill via the larger exponent. Placeholders. ---
    public double SkillSteepness    { get; set; } = 6.0;
    public double SkillExponent     { get; set; } = 2.0;
    public double PhysicalSteepness { get; set; } = 6.0;
    public double PhysicalExponent  { get; set; } = 2.7;

    /// <summary>The reference gap (rating points) at which a shift equals its steepness —
    /// a fixed UNIT that keeps the steepness knobs legible and identifiable, moved rarely.
    /// Must be &gt; 0 (enforced in Load).</summary>
    public double ReferenceScale    { get; set; } = 25.0;

    // --- CONF-1 per-zone defense-blend weights, as data. Named {Zone}{Attr}, where Attr
    //     is the defensive attribute the weight scales: Perimeter→PerimeterDefense,
    //     Post→PostDefense, Rim→RimProtection. Slides perimeter→interior across the five
    //     zones. The three weights per zone need not sum to 1 (the blend is a weighted
    //     read, not a distribution), though the spec table happens to. ---
    public double ThreePerimeter { get; set; } = 1.00;
    public double ThreePost      { get; set; } = 0.00;
    public double ThreeRim       { get; set; } = 0.00;

    public double LongPerimeter  { get; set; } = 0.85;
    public double LongPost       { get; set; } = 0.15;
    public double LongRim        { get; set; } = 0.00;

    public double MidPerimeter   { get; set; } = 0.50;
    public double MidPost        { get; set; } = 0.50;
    public double MidRim         { get; set; } = 0.00;

    public double ShortPerimeter { get; set; } = 0.15;
    public double ShortPost      { get; set; } = 0.85;
    public double ShortRim       { get; set; } = 0.00;

    public double RimPerimeter   { get; set; } = 0.00;
    public double RimPost        { get; set; } = 0.35;   // placeholder — confirm with Emmett
    public double RimRim         { get; set; } = 0.65;   // placeholder — confirm with Emmett

    /// <summary>The (perimeter, post, rim) blend weights for a zone — the single place
    /// the zone→weights mapping lives, read by <see cref="Matchup.DefenseRating"/>.</summary>
    public (double perimeter, double post, double rim) BlendWeights(ShotLocation zone) => zone switch
    {
        ShotLocation.Three => (ThreePerimeter, ThreePost, ThreeRim),
        ShotLocation.Long  => (LongPerimeter,  LongPost,  LongRim),
        ShotLocation.Mid   => (MidPerimeter,   MidPost,   MidRim),
        ShotLocation.Short => (ShortPerimeter, ShortPost, ShortRim),
        ShotLocation.Rim   => (RimPerimeter,   RimPost,   RimRim),
        _ => throw new InvalidOperationException($"No defense-blend weights for zone '{zone}'.")
    };

    // --- Phase 7: block-contest skill/length weights, per zone.
    //     At Three the contest is 40% skill / 60% length (Emmett's anchor).
    //     At Rim the reverse: 40% skill / 60% length (physical anchor, same pair).
    //     As you move out from the rim, skill contributes less and length more.
    //     Each pair must sum to 1.0 (enforced in Load).
    //     Named Block{Zone}Skill / Block{Zone}Length. Placeholders for calibration. ---
    public double BlockRimSkill    { get; set; } = 0.40;
    public double BlockRimLength   { get; set; } = 0.60;

    public double BlockShortSkill  { get; set; } = 0.45;
    public double BlockShortLength { get; set; } = 0.55;

    public double BlockMidSkill    { get; set; } = 0.50;
    public double BlockMidLength   { get; set; } = 0.50;

    public double BlockLongSkill   { get; set; } = 0.42;
    public double BlockLongLength  { get; set; } = 0.58;

    public double BlockThreeSkill  { get; set; } = 0.40;
    public double BlockThreeLength { get; set; } = 0.60;

    /// <summary>The (skillWeight, lengthWeight) pair for the block contest at a given zone.
    /// Weights sum to 1.0 (enforced in Load). Read by <see cref="Matchup.BlockWeight"/>.</summary>
    public (double skillWeight, double lengthWeight) BlockContestWeights(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => (BlockRimSkill,   BlockRimLength),
        ShotLocation.Short => (BlockShortSkill, BlockShortLength),
        ShotLocation.Mid   => (BlockMidSkill,   BlockMidLength),
        ShotLocation.Long  => (BlockLongSkill,  BlockLongLength),
        ShotLocation.Three => (BlockThreeSkill, BlockThreeLength),
        _ => throw new InvalidOperationException($"No block contest weights for zone '{zone}'.")
    };

    // --- Phase 7: per-zone block floor and ceiling.
    //     Floor is non-zero — even a peak shooter against a stiff is occasionally blocked.
    //     Ceiling is the defender-edge asymptote. Emmett's anchors: Rim ceiling 0.30,
    //     Three ceiling 0.04. All placeholders; the calibration pass owns the magnitudes.
    //     Named BlockFloor{Zone} / BlockCeil{Zone}. ---
    public double BlockFloorRim   { get; set; } = 0.04;
    public double BlockCeilRim    { get; set; } = 0.30;

    public double BlockFloorShort { get; set; } = 0.02;
    public double BlockCeilShort  { get; set; } = 0.20;

    public double BlockFloorMid   { get; set; } = 0.01;
    public double BlockCeilMid    { get; set; } = 0.10;

    public double BlockFloorLong  { get; set; } = 0.005;
    public double BlockCeilLong   { get; set; } = 0.06;

    public double BlockFloorThree { get; set; } = 0.003;
    public double BlockCeilThree  { get; set; } = 0.04;

    /// <summary>The block floor (shooter-edge asymptote) for a zone.
    /// Non-zero — even an elite shooter is occasionally blocked.
    /// Read by <see cref="Matchup.BlockWeight"/>.</summary>
    public double BlockFloor(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => BlockFloorRim,
        ShotLocation.Short => BlockFloorShort,
        ShotLocation.Mid   => BlockFloorMid,
        ShotLocation.Long  => BlockFloorLong,
        ShotLocation.Three => BlockFloorThree,
        _ => throw new InvalidOperationException($"No block floor for zone '{zone}'.")
    };

    /// <summary>The block ceiling (defender-edge asymptote) for a zone.
    /// Read by <see cref="Matchup.BlockWeight"/>.</summary>
    public double BlockCeiling(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => BlockCeilRim,
        ShotLocation.Short => BlockCeilShort,
        ShotLocation.Mid   => BlockCeilMid,
        ShotLocation.Long  => BlockCeilLong,
        ShotLocation.Three => BlockCeilThree,
        _ => throw new InvalidOperationException($"No block ceiling for zone '{zone}'.")
    };

    // --- Phase 7: tanh saturation knob.
    //     Controls how fast the block weight approaches floor/ceiling as the matchup
    //     gap widens. A net shift of BlockReferenceShift gets you to tanh(1) ≈ 76% of
    //     the way from baseline toward ceiling (or floor). Default 20.0 (rating points).
    //     Must be > 0 (enforced in Load). ---
    /// <summary>The net shift (rating points) that reaches ~76% saturation toward floor/ceiling.
    /// Higher values → slower saturation (wider gap needed for large block-rate changes).
    /// Must be &gt; 0 (enforced in Load).</summary>
    public double BlockReferenceShift { get; set; } = 20.0;

    // --- Phase 7: length composite blend for the block contest.
    //     Length = (Height * LengthHeight + Wingspan * LengthWingspan + Vertical * LengthVertical).
    //     Equal thirds by default — all three contribute equally to blocking ability.
    //     Stored as config so the "tune the length composite" pass is trivial:
    //     change weights here without touching Matchup.LengthRating.
    //     Must sum to 1.0 (enforced in Load). ---
    public double LengthHeight   { get; set; } = 1.0 / 3.0;
    public double LengthWingspan { get; set; } = 1.0 / 3.0;
    public double LengthVertical { get; set; } = 1.0 / 3.0;

    // =========================================================================
    // Phase 8 — foul-door parameters
    // =========================================================================

    // --- Phase 8: per-zone foul floor and ceiling.
    //     Floor is close to baseline (small downward range — low FoulDrawing is
    //     NOT an active skill; it's absence of opportunity). Ceiling is far above
    //     baseline (large upward range — an elite foul-drawer can push the rate
    //     way up). All placeholders; calibration pass owns the magnitudes.
    //     Named FoulFloor{Zone} / FoulCeil{Zone}. ---
    public double FoulFloorRim   { get; set; } = 0.17;
    public double FoulCeilRim    { get; set; } = 0.35;

    public double FoulFloorShort { get; set; } = 0.075;
    public double FoulCeilShort  { get; set; } = 0.18;

    public double FoulFloorMid   { get; set; } = 0.035;
    public double FoulCeilMid    { get; set; } = 0.10;

    public double FoulFloorLong  { get; set; } = 0.02;
    public double FoulCeilLong   { get; set; } = 0.06;

    public double FoulFloorThree { get; set; } = 0.008;
    public double FoulCeilThree  { get; set; } = 0.04;

    /// <summary>The foul floor (disciplined-defender-edge asymptote) for a zone.
    /// Close to the baseline — low FoulDrawing is absence of opportunity, not
    /// active failure, so the downward range is narrow.
    /// Read by <see cref="Matchup.FoulRate"/>.</summary>
    public double FoulFloor(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => FoulFloorRim,
        ShotLocation.Short => FoulFloorShort,
        ShotLocation.Mid   => FoulFloorMid,
        ShotLocation.Long  => FoulFloorLong,
        ShotLocation.Three => FoulFloorThree,
        _ => throw new InvalidOperationException($"No foul floor for zone '{zone}'.")
    };

    /// <summary>The foul ceiling (foul-drawer-edge asymptote) for a zone.
    /// Far above the baseline — an elite foul-drawer against an undisciplined
    /// defender can push the foul rate well above baseline, especially at the rim.
    /// Read by <see cref="Matchup.FoulRate"/>.</summary>
    public double FoulCeiling(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => FoulCeilRim,
        ShotLocation.Short => FoulCeilShort,
        ShotLocation.Mid   => FoulCeilMid,
        ShotLocation.Long  => FoulCeilLong,
        ShotLocation.Three => FoulCeilThree,
        _ => throw new InvalidOperationException($"No foul ceiling for zone '{zone}'.")
    };

    // --- Phase 8: tanh saturation knob for the foul contest.
    //     Controls how fast the foul rate approaches floor/ceiling as the
    //     foul-drawing advantage widens. Default 20.0 (same as BlockReferenceShift).
    //     Must be > 0 (enforced in Load). ---
    /// <summary>The net shift (rating points) that reaches ~76% saturation toward
    /// foul floor/ceiling. Higher values → slower saturation.
    /// Must be &gt; 0 (enforced in Load).</summary>
    public double FoulReferenceShift { get; set; } = 20.0;

    // --- Phase 8: foul contest weights.
    //     Offense-dominant: FoulDrawing carries the bigger weight; Discipline (defense)
    //     carries a light tap. One GLOBAL pair (not per-zone) — per-zone variation in
    //     foul impact lives in the per-zone floors/ceilings, not the weights.
    //     Must sum to 1.0 (enforced in Load). ---
    public double OffenseFoulWeight { get; set; } = 0.80;
    public double DefenseFoulWeight { get; set; } = 0.20;

    // --- Phase 8: attribute midpoint for the foul contest.
    //     Both FoulDrawing and Discipline are expressed as deviations from this
    //     midpoint, so an average (50) player contributes zero contest value.
    //     Must be > 0 (enforced in Load). ---
    public double AttributeMidpoint { get; set; } = 50.0;

    // =========================================================================
    // Phase 9 — shot-location door parameters
    // =========================================================================

    // --- Phase 9: top-3 defender blend weights for per-zone defensive resistance.
    //     The best zone defender carries the most weight (he rotates over), but
    //     help arrives less than instantly so the second and third matter too.
    //     Fourth and fifth are too far from the action. Must sum to 1.0 (enforced
    //     in Load). Global (not per-zone) for v1; a per-zone variant is a
    //     calibration call deferred. ---
    public double LocationBlendFirst  { get; set; } = 0.55;
    public double LocationBlendSecond { get; set; } = 0.30;
    public double LocationBlendThird  { get; set; } = 0.15;

    // --- Phase 9: tanh saturation knob for the shot-location contest.
    //     Mirrors BlockReferenceShift / FoulReferenceShift. A net per-zone gap of
    //     LocationReferenceShift rating points reaches ~76% of the multiplier's
    //     log range. Default 20.0; must be > 0 (enforced in Load). ---
    public double LocationReferenceShift { get; set; } = 20.0;

    // --- Phase 9: per-zone multiplier upper asymptote (and 1 / this is the lower).
    //     The multiplier formula is the RATIO form:
    //         mult = exp(log(LocationMaxMultiplier) * tanh(shift / refShift))
    //     Bounded in (1 / LocationMaxMultiplier, LocationMaxMultiplier). With the
    //     default 2.5, multipliers asymptote toward (0.4, 2.5) and are exactly 1
    //     at zero gap. NEVER negative (the v1 additive form could go negative —
    //     fixed by the v2 ratio form). Must be > 1.0 (enforced in Load). ---
    public double LocationMaxMultiplier { get; set; } = 2.5;

    // =========================================================================
    // Phase 12 — pressure / disruption door (Roll F)
    // =========================================================================

    // --- Phase 12: per-team defensive pressure dials.
    //     Range 1–10; neutral at PressureNeutral (= 5 by default).
    //     v1 home: scalars in MatchupConfig.
    //     Migration path: when the coach-settings layer arrives, these move to
    //     per-team CoachProfile fields and RollFGenerator reads them from there.
    //     PressureFor(TeamSide) is the read seam; only that method changes.
    //     Must be in [1, 10] (enforced in Load). ---

    /// <summary>Home team's defensive pressure setting (1–10 scale). Neutral = 5.
    /// The defense applies pressure; read for the DEFENDING team at generate-time.
    /// Calibration placeholder — set to neutral so the door reproduces today's
    /// flat Roll F rates with even matchups.</summary>
    public double HomePressure { get; set; } = 5.0;

    /// <summary>Away team's defensive pressure setting (1–10 scale). Neutral = 5.
    /// Calibration placeholder.</summary>
    public double AwayPressure { get; set; } = 5.0;

    /// <summary>The pressure dial for the given team. The generator calls this for
    /// <c>state.Defense</c> — the defense applies pressure to the offense's handler.
    /// Migration seam: when CoachProfile is plumbed, this method reads from there
    /// instead; nothing else in the generator changes.</summary>
    public double PressureFor(TeamSide side) =>
        side == TeamSide.Home ? HomePressure : AwayPressure;

    // --- Phase 12: pressure normalization.
    //     pUnit = (pressure − PressureNeutral) / PressureScale.
    //     pUnit = 0 at neutral; negative = backed-off; positive = aggressive.
    //     PressureNeutral in [1, 10] (enforced in Load).
    //     PressureScale > 0 (enforced in Load). ---

    /// <summary>The pressure value that maps to pUnit = 0 (no disruption lift vs.
    /// today's flat baseline). Must be in [1, 10] (enforced in Load).
    /// Default 5.0 — midpoint of the 1–10 dial.</summary>
    public double PressureNeutral { get; set; } = 5.0;

    /// <summary>Divisor that converts the raw pressure dial into a signed unit.
    /// Higher = wider dial (same pressure difference → smaller pUnit → less sensitivity).
    /// Must be &gt; 0 (enforced in Load). Default 4.0.</summary>
    public double PressureScale { get; set; } = 4.0;

    // --- Phase 12: pressure saturation knob.
    //     Controls how fast TO and foul rates approach floor/ceiling as pUnit grows.
    //     Deliberately HIGH (relative to the pUnit range) to keep the climb gradual
    //     — the "low cap + high reference" encoding of "nobody gets 5 steals a game."
    //     Must be > 0 (enforced in Load). ---

    /// <summary>The tanh saturation knob for both the turnover and the foul disruption
    /// bends. Higher = slower saturation (gentler climb toward ceiling/floor). Default
    /// 1.2. The pUnit at max pressure ≈ 1.25; with ref = 1.2, tanh(1.04) ≈ 0.78 →
    /// climb reaches ~78% of ceiling headroom at max pressure with an even matchup.
    /// Must be &gt; 0 (enforced in Load). Calibration placeholder.</summary>
    public double PressureReferenceShift { get; set; } = 1.2;

    // --- Phase 12: steal/turnover slice ceiling and floor.
    //     LOW ceiling is the whole point — real steal rates cap out; nobody averages
    //     5 steals a game even in the fastest system. All values are shares of the
    //     action mass (= BaseShotAttempt + BaseTurnover + BaseNonShootingFoul), NOT
    //     the full pie. Enforced in Load: ceiling > floor >= 0. ---

    /// <summary>Maximum TO share within the action mass (defender-pressure asymptote).
    /// LOW by design — real steal rates cap out. Default 0.18 (18% of action-mass
    /// possessions ending in a Roll F turnover at maximum disruption). Must exceed
    /// <see cref="TurnoverFloor"/> (enforced in Load). Calibration placeholder.</summary>
    public double TurnoverCeiling { get; set; } = 0.18;

    /// <summary>Minimum TO share within the action mass (backed-off asymptote).
    /// Low pressure → few disruptions regardless of matchup. Default 0.02.
    /// Must be &gt;= 0 and &lt; <see cref="TurnoverCeiling"/> (enforced in Load).
    /// Calibration placeholder.</summary>
    public double TurnoverFloor { get; set; } = 0.02;

    // --- Phase 12: non-shooting foul slice ceiling and floor.
    //     Shares of the action mass. Enforced in Load: ceiling > floor >= 0. ---

    /// <summary>Maximum non-shooting-foul share within the action mass (max-pressure
    /// asymptote). Default 0.09 (about 1.8× the baseline of ≈0.0503). Must exceed
    /// <see cref="FoulPressureFloor"/> (enforced in Load). Calibration placeholder.</summary>
    public double FoulPressureCeiling { get; set; } = 0.09;

    /// <summary>Minimum non-shooting-foul share within the action mass (backed-off
    /// asymptote). Default 0.01. Must be &gt;= 0 and &lt; <see cref="FoulPressureCeiling"/>
    /// (enforced in Load). Calibration placeholder.</summary>
    public double FoulPressureFloor { get; set; } = 0.01;

    // --- Phase 13 — Roll B slot weights -----------------------------------
    // Slot-weighted team aggregates for BallHandling (offense) and Steals
    // (defense) at Roll B. Guards are weighted heaviest because halfcourt
    // initiation is guard-dominated on both sides of the ball.
    // Same weights apply to both offense (BallHandling) and defense (Steals).
    // Must be >= 0 and sum to 1.0 (enforced in Load). ---

    /// <summary>Weight for slot 1 (PG) in the Roll B team aggregate.
    /// Default 0.35 — guards handle and steal most. Must be &gt;= 0
    /// (enforced in Load).</summary>
    public double SlotWeight1 { get; set; } = 0.35;

    /// <summary>Weight for slot 2 (SG) in the Roll B team aggregate.
    /// Default 0.25. Must be &gt;= 0 (enforced in Load).</summary>
    public double SlotWeight2 { get; set; } = 0.25;

    /// <summary>Weight for slot 3 (SF) in the Roll B team aggregate.
    /// Default 0.20. Must be &gt;= 0 (enforced in Load).</summary>
    public double SlotWeight3 { get; set; } = 0.20;

    /// <summary>Weight for slot 4 (PF) in the Roll B team aggregate.
    /// Default 0.12. Must be &gt;= 0 (enforced in Load).</summary>
    public double SlotWeight4 { get; set; } = 0.12;

    /// <summary>Weight for slot 5 (C) in the Roll B team aggregate.
    /// Default 0.08 — centers have far fewer opportunities to handle or
    /// steal at halfcourt initiation. Must be &gt;= 0 (enforced in Load).</summary>
    public double SlotWeight5 { get; set; } = 0.08;

    /// <summary>Convenience array for iterating slot weights 1–5 (index 0 = slot 1).
    /// Sums to 1.0 (enforced in Load).</summary>
    public double[] SlotWeights => new[] { SlotWeight1, SlotWeight2, SlotWeight3, SlotWeight4, SlotWeight5 };

    // --- Phase 13 — Roll B pressure ceilings/floors (shares of action mass) ---
    // Roll B's baseline foul share (≈0.1206 of action mass) is much higher than
    // Roll F's (≈0.0538), and its baseline TO share (≈0.0302) is lower. The Phase 12
    // Roll-F ceilings/floors are wrong for Roll B — these Roll-B-specific keys are
    // used by Matchup.TeamDisruptionShares instead.
    // Shares of action mass (= BaseProceed + BaseFoul + BaseDeadBallTurnover).
    // Enforced in Load: ceiling > floor >= 0. Calibration placeholders. ---

    /// <summary>Upper limit on Roll B's turnover share (of action mass) under
    /// maximum disruption. Deliberately low — nobody strips 10% of halfcourt
    /// initiations even in a pressing scheme. Must exceed
    /// <see cref="RollBTurnoverFloor"/> (enforced in Load).</summary>
    public double RollBTurnoverCeiling { get; set; } = 0.10;

    /// <summary>Lower limit on Roll B's turnover share (of action mass) under
    /// minimum disruption. Must be &gt;= 0 and &lt;
    /// <see cref="RollBTurnoverCeiling"/> (enforced in Load).</summary>
    public double RollBTurnoverFloor { get; set; } = 0.01;

    /// <summary>Upper limit on Roll B's foul share (of action mass) under maximum
    /// pressure. Must exceed <see cref="RollBFoulPressureFloor"/>
    /// (enforced in Load). Calibration placeholder.</summary>
    public double RollBFoulPressureCeiling { get; set; } = 0.22;

    /// <summary>Lower limit on Roll B's foul share (of action mass) under minimum
    /// pressure. Must be &gt;= 0 and &lt; <see cref="RollBFoulPressureCeiling"/>
    /// (enforced in Load). Calibration placeholder.</summary>
    public double RollBFoulPressureFloor { get; set; } = 0.06;

    // --- Phase 14: full-court press dial (Roll A, backcourt entry).
    //     Distinct from HomePressure/AwayPressure (halfcourt pressure used by Roll B/F).
    //     A team can press full-court or pick up at halfcourt only -- independent decisions.
    //     Range 1-10; neutral at PressureNeutral (= 5 by default).
    //     Migration path: per-team CoachProfile fields own this eventually;
    //     FullCourtPressFor(TeamSide) is the only read seam that changes. ---

    /// <summary>Full-court press intensity for the HOME team (1-10; 5 = neutral).
    /// Distinct from <see cref="HomePressure"/> (halfcourt pressure). Roll A reads
    /// this when the defending team is Home.</summary>
    public double HomeFullCourtPress { get; set; } = 5.0;

    /// <summary>Full-court press intensity for the AWAY team (1-10; 5 = neutral).
    /// Distinct from <see cref="AwayPressure"/> (halfcourt pressure). Roll A reads
    /// this when the defending team is Away.</summary>
    public double AwayFullCourtPress { get; set; } = 5.0;

    /// <summary>Full-court press dial for <paramref name="side"/>. Mirrors
    /// <see cref="PressureFor"/> but reads the full-court press knobs, not the
    /// halfcourt pressure knobs. Roll A's only call site for this value.</summary>
    public double FullCourtPressFor(TeamSide side) =>
        side == TeamSide.Home ? HomeFullCourtPress : AwayFullCourtPress;

    // --- Phase 14: Roll-A-specific ceilings and floors (action-mass shares).
    //     At max press + worst matchup, shares approach (but do not exceed) the ceilings.
    //     The sum of the three ceilings must be < 1.0 (enforced in Load). ---

    /// <summary>Maximum turnover share of Roll A's action mass under maximum full-court
    /// press + worst matchup. Must exceed <see cref="RollATurnoverFloor"/>
    /// (enforced in Load).</summary>
    public double RollATurnoverCeiling { get; set; } = 0.20;

    /// <summary>Minimum turnover share of Roll A's action mass under minimum press.
    /// Must be &gt;= 0 and &lt; <see cref="RollATurnoverCeiling"/> (enforced in Load).</summary>
    public double RollATurnoverFloor { get; set; } = 0.005;

    /// <summary>Maximum defensive foul share of Roll A's action mass under maximum press.
    /// Must exceed <see cref="RollADefFoulFloor"/> (enforced in Load).</summary>
    public double RollADefFoulCeiling { get; set; } = 0.10;

    /// <summary>Minimum defensive foul share of Roll A's action mass under minimum press.
    /// Must be &gt;= 0 and &lt; <see cref="RollADefFoulCeiling"/> (enforced in Load).</summary>
    public double RollADefFoulFloor { get; set; } = 0.005;

    /// <summary>Maximum offensive foul share of Roll A's action mass under maximum press.
    /// Ceiling ~15% of <see cref="RollADefFoulCeiling"/> by design. Must exceed
    /// <see cref="RollAOffFoulFloor"/> (enforced in Load).</summary>
    public double RollAOffFoulCeiling { get; set; } = 0.015;

    /// <summary>Minimum offensive foul share of Roll A's action mass under minimum press.
    /// Must be &gt;= 0 and &lt; <see cref="RollAOffFoulCeiling"/> (enforced in Load).</summary>
    public double RollAOffFoulFloor { get; set; } = 0.001;

    // --- Phase 14: full-court press saturation constant.
    //     The tanh divisor for ALL THREE Roll A bends (turnover, DefFoul, OffFoul).
    //     SEPARATE from PressureReferenceShift (halfcourt) so the two dials are
    //     fully independent — neither the normalization nor the saturation speed of
    //     full-court press is coupled to halfcourt.
    //     Must be > 0 (enforced in Load). Same default as PressureReferenceShift
    //     (1.2) as a calibration placeholder. ---

    /// <summary>Tanh saturation divisor for Roll A's full-court press bends (Phase 14).
    /// Governs how fast turnover, DefFoul, and OffFoul shares approach their ceilings/floors
    /// as pUnit grows. SEPARATE from <see cref="PressureReferenceShift"/> (halfcourt) —
    /// the two dials stay fully independent. Must be &gt; 0 (enforced in Load).
    /// Calibration placeholder.</summary>
    public double FullCourtPressReferenceShift { get; set; } = 1.2;

    // --- Phase 14: turnover gap weights.
    //     Three sources compose into the team matchup shift for Roll A's TO slice:
    //       matchupShift = SkillWeight*skillShift + AthleticismWeight*athShift + SizeWeight*sizeShift
    //     SizeWeight is the smallest (size matters least in backcourt press).
    //     All three must be >= 0 (enforced in Load). Need not sum to 1. ---

    /// <summary>Weight of the skill gap (Steals − BallHandling) in Roll A's turnover
    /// matchup shift. The largest of the three — backcourt press is primarily a
    /// ball-handling skill contest. Must be &gt;= 0 (enforced in Load).
    /// Calibration placeholder.</summary>
    public double RollASkillWeight { get; set; } = 0.50;

    /// <summary>Weight of the athleticism gap (Athleticism − Athleticism) in Roll A's
    /// turnover matchup shift. Middle of the three. Must be &gt;= 0 (enforced in Load).
    /// Calibration placeholder.</summary>
    public double RollAAthleticismWeight { get; set; } = 0.35;

    /// <summary>Weight of the size gap (LengthRating − LengthRating) in Roll A's
    /// turnover matchup shift. Smallest of the three — size matters, but least for
    /// backcourt press disruption. Must be &gt;= 0 (enforced in Load).
    /// Calibration placeholder.</summary>
    public double RollASizeWeight { get; set; } = 0.15;

    public static MatchupConfig Load(string path)    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Matchup");
        var cfg = JsonSerializer.Deserialize<MatchupConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (cfg is null)
            throw new InvalidOperationException($"Could not parse Matchup config at {path}.");

        // DEC-5 invariant: the gap function is only convex/flat-bottomed for exponent > 1.
        // An exponent <= 1 silently breaks the contract (linear or concave), so fail loud.
        if (cfg.SkillExponent <= 1.0 || cfg.PhysicalExponent <= 1.0)
            throw new InvalidOperationException(
                "Matchup gap exponents must be > 1 for the convex/flat-bottom contract (DEC-5): " +
                $"SkillExponent={cfg.SkillExponent}, PhysicalExponent={cfg.PhysicalExponent}.");
        if (cfg.ReferenceScale <= 0.0)
            throw new InvalidOperationException(
                $"Matchup ReferenceScale must be > 0 (DEC-5): ReferenceScale={cfg.ReferenceScale}.");

        // Phase 7 invariants — fail loud so a mis-keyed config is caught at startup.
        if (cfg.BlockReferenceShift <= 0.0)
            throw new InvalidOperationException(
                $"BlockReferenceShift must be > 0: got {cfg.BlockReferenceShift}.");

        const double Eps = 1e-9;

        // Skill + length weights must sum to 1.0 per zone.
        foreach (var zone in new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
                                     ShotLocation.Long, ShotLocation.Three })
        {
            var (sw, lw) = cfg.BlockContestWeights(zone);
            if (Math.Abs(sw + lw - 1.0) > Eps)
                throw new InvalidOperationException(
                    $"Block contest weights for zone {zone} must sum to 1.0: " +
                    $"skill={sw}, length={lw}, sum={sw + lw}.");

            // Floor must be >= 0 and ceiling must exceed the RollHConfig baseline.
            // (We cannot read RollHConfig here, so we guard that floor >= 0 and ceiling > floor.)
            if (cfg.BlockFloor(zone) < 0.0)
                throw new InvalidOperationException(
                    $"BlockFloor for zone {zone} must be >= 0: got {cfg.BlockFloor(zone)}.");
            if (cfg.BlockCeiling(zone) <= cfg.BlockFloor(zone))
                throw new InvalidOperationException(
                    $"BlockCeiling for zone {zone} must exceed BlockFloor: " +
                    $"floor={cfg.BlockFloor(zone)}, ceiling={cfg.BlockCeiling(zone)}.");
        }

        // Length weights must sum to 1.0.
        var lenSum = cfg.LengthHeight + cfg.LengthWingspan + cfg.LengthVertical;
        if (Math.Abs(lenSum - 1.0) > Eps)
            throw new InvalidOperationException(
                $"LengthHeight + LengthWingspan + LengthVertical must sum to 1.0: got {lenSum}.");

        // Phase 8 invariants.
        if (cfg.FoulReferenceShift <= 0.0)
            throw new InvalidOperationException(
                $"FoulReferenceShift must be > 0: got {cfg.FoulReferenceShift}.");

        if (Math.Abs(cfg.OffenseFoulWeight + cfg.DefenseFoulWeight - 1.0) > Eps)
            throw new InvalidOperationException(
                $"OffenseFoulWeight + DefenseFoulWeight must sum to 1.0: " +
                $"offense={cfg.OffenseFoulWeight}, defense={cfg.DefenseFoulWeight}.");

        if (cfg.AttributeMidpoint <= 0.0)
            throw new InvalidOperationException(
                $"AttributeMidpoint must be > 0: got {cfg.AttributeMidpoint}.");

        foreach (var zone in new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
                                     ShotLocation.Long, ShotLocation.Three })
        {
            if (cfg.FoulFloor(zone) < 0.0)
                throw new InvalidOperationException(
                    $"FoulFloor for zone {zone} must be >= 0: got {cfg.FoulFloor(zone)}.");
            if (cfg.FoulCeiling(zone) <= cfg.FoulFloor(zone))
                throw new InvalidOperationException(
                    $"FoulCeiling for zone {zone} must exceed FoulFloor: " +
                    $"floor={cfg.FoulFloor(zone)}, ceiling={cfg.FoulCeiling(zone)}.");
        }

        // Phase 9 invariants.
        var blendSum = cfg.LocationBlendFirst + cfg.LocationBlendSecond + cfg.LocationBlendThird;
        if (Math.Abs(blendSum - 1.0) > Eps)
            throw new InvalidOperationException(
                $"LocationBlendFirst + LocationBlendSecond + LocationBlendThird must sum to 1.0: got {blendSum}.");

        if (cfg.LocationReferenceShift <= 0.0)
            throw new InvalidOperationException(
                $"LocationReferenceShift must be > 0: got {cfg.LocationReferenceShift}.");

        if (cfg.LocationMaxMultiplier <= 1.0)
            throw new InvalidOperationException(
                $"LocationMaxMultiplier must be > 1.0 (a max of 1.0 or below would be smaller than the neutral " +
                $"case; nonsensical): got {cfg.LocationMaxMultiplier}.");

        // Phase 10 invariants — rebound door (the glass).
        if (Math.Abs(cfg.ReboundSizeWeight + cfg.ReboundSkillWeight - 1.0) > Eps)
            throw new InvalidOperationException(
                $"ReboundSizeWeight + ReboundSkillWeight must sum to 1.0: " +
                $"size={cfg.ReboundSizeWeight}, skill={cfg.ReboundSkillWeight}, sum={cfg.ReboundSizeWeight + cfg.ReboundSkillWeight}.");

        if (cfg.ReboundOffShareFloor < 0.0 || cfg.ReboundOffShareFloor >= cfg.ReboundOffShareCeiling)
            throw new InvalidOperationException(
                $"ReboundOffShareFloor must be >= 0 and < ReboundOffShareCeiling: " +
                $"floor={cfg.ReboundOffShareFloor}, ceiling={cfg.ReboundOffShareCeiling}.");

        if (cfg.ReboundOffShareCeiling > 1.0)
            throw new InvalidOperationException(
                $"ReboundOffShareCeiling must be <= 1.0: got {cfg.ReboundOffShareCeiling}.");

        if (cfg.ReboundReferenceShift <= 0.0)
            throw new InvalidOperationException(
                $"ReboundReferenceShift must be > 0: got {cfg.ReboundReferenceShift}.");

        if (cfg.ReboundPositionalScale <= 0.0)
            throw new InvalidOperationException(
                $"ReboundPositionalScale must be > 0: got {cfg.ReboundPositionalScale}.");

        if (cfg.ReboundPositionalSwing < 0.0 || cfg.ReboundPositionalSwing >= 1.0)
            throw new InvalidOperationException(
                $"ReboundPositionalSwing must be >= 0 and < 1.0: got {cfg.ReboundPositionalSwing}.");

        if (cfg.ReboundShooterNerf < 0.0 || cfg.ReboundShooterNerf > 1.0)
            throw new InvalidOperationException(
                $"ReboundShooterNerf must be in [0.0, 1.0] (a nerf multiplier, never a boost or negative): " +
                $"got {cfg.ReboundShooterNerf}.");

        // Phase 12 invariants — disruption door (Roll F).
        if (cfg.PressureNeutral < 1.0 || cfg.PressureNeutral > 10.0)
            throw new InvalidOperationException(
                $"PressureNeutral must be in [1, 10]: got {cfg.PressureNeutral}.");

        if (cfg.PressureScale <= 0.0)
            throw new InvalidOperationException(
                $"PressureScale must be > 0: got {cfg.PressureScale}.");

        if (cfg.PressureReferenceShift <= 0.0)
            throw new InvalidOperationException(
                $"PressureReferenceShift must be > 0: got {cfg.PressureReferenceShift}.");

        if (cfg.TurnoverFloor < 0.0)
            throw new InvalidOperationException(
                $"TurnoverFloor must be >= 0: got {cfg.TurnoverFloor}.");

        if (cfg.TurnoverCeiling <= cfg.TurnoverFloor)
            throw new InvalidOperationException(
                $"TurnoverCeiling must exceed TurnoverFloor: " +
                $"floor={cfg.TurnoverFloor}, ceiling={cfg.TurnoverCeiling}.");

        if (cfg.FoulPressureFloor < 0.0)
            throw new InvalidOperationException(
                $"FoulPressureFloor must be >= 0: got {cfg.FoulPressureFloor}.");

        if (cfg.FoulPressureCeiling <= cfg.FoulPressureFloor)
            throw new InvalidOperationException(
                $"FoulPressureCeiling must exceed FoulPressureFloor: " +
                $"floor={cfg.FoulPressureFloor}, ceiling={cfg.FoulPressureCeiling}.");

        // Phase 13 — slot weights
        var slotWeightSum = cfg.SlotWeight1 + cfg.SlotWeight2 + cfg.SlotWeight3
                          + cfg.SlotWeight4 + cfg.SlotWeight5;
        if (cfg.SlotWeight1 < 0.0 || cfg.SlotWeight2 < 0.0 || cfg.SlotWeight3 < 0.0
            || cfg.SlotWeight4 < 0.0 || cfg.SlotWeight5 < 0.0)
            throw new InvalidOperationException(
                "All SlotWeight values must be >= 0.");
        if (Math.Abs(slotWeightSum - 1.0) > 1e-6)
            throw new InvalidOperationException(
                $"SlotWeight1–5 must sum to 1.0: got {slotWeightSum:F6}.");

        // Phase 13 — Roll-B-specific ceilings/floors
        if (cfg.RollBTurnoverFloor < 0.0)
            throw new InvalidOperationException(
                $"RollBTurnoverFloor must be >= 0: got {cfg.RollBTurnoverFloor}.");
        if (cfg.RollBTurnoverCeiling <= cfg.RollBTurnoverFloor)
            throw new InvalidOperationException(
                $"RollBTurnoverCeiling must exceed RollBTurnoverFloor: " +
                $"floor={cfg.RollBTurnoverFloor}, ceiling={cfg.RollBTurnoverCeiling}.");
        if (cfg.RollBFoulPressureFloor < 0.0)
            throw new InvalidOperationException(
                $"RollBFoulPressureFloor must be >= 0: got {cfg.RollBFoulPressureFloor}.");
        if (cfg.RollBFoulPressureCeiling <= cfg.RollBFoulPressureFloor)
            throw new InvalidOperationException(
                $"RollBFoulPressureCeiling must exceed RollBFoulPressureFloor: " +
                $"floor={cfg.RollBFoulPressureFloor}, ceiling={cfg.RollBFoulPressureCeiling}.");

        // Phase 14 -- full-court press dial
        if (cfg.HomeFullCourtPress < 1.0 || cfg.HomeFullCourtPress > 10.0)
            throw new InvalidOperationException(
                $"HomeFullCourtPress must be in [1, 10]: got {cfg.HomeFullCourtPress}.");
        if (cfg.AwayFullCourtPress < 1.0 || cfg.AwayFullCourtPress > 10.0)
            throw new InvalidOperationException(
                $"AwayFullCourtPress must be in [1, 10]: got {cfg.AwayFullCourtPress}.");

        // Phase 14 -- Roll-A-specific ceilings/floors
        if (cfg.RollATurnoverFloor < 0.0)
            throw new InvalidOperationException(
                $"RollATurnoverFloor must be >= 0: got {cfg.RollATurnoverFloor}.");
        if (cfg.RollATurnoverCeiling <= cfg.RollATurnoverFloor)
            throw new InvalidOperationException(
                $"RollATurnoverCeiling must exceed RollATurnoverFloor: " +
                $"floor={cfg.RollATurnoverFloor}, ceiling={cfg.RollATurnoverCeiling}.");
        if (cfg.RollADefFoulFloor < 0.0)
            throw new InvalidOperationException(
                $"RollADefFoulFloor must be >= 0: got {cfg.RollADefFoulFloor}.");
        if (cfg.RollADefFoulCeiling <= cfg.RollADefFoulFloor)
            throw new InvalidOperationException(
                $"RollADefFoulCeiling must exceed RollADefFoulFloor: " +
                $"floor={cfg.RollADefFoulFloor}, ceiling={cfg.RollADefFoulCeiling}.");
        if (cfg.RollAOffFoulFloor < 0.0)
            throw new InvalidOperationException(
                $"RollAOffFoulFloor must be >= 0: got {cfg.RollAOffFoulFloor}.");
        if (cfg.RollAOffFoulCeiling <= cfg.RollAOffFoulFloor)
            throw new InvalidOperationException(
                $"RollAOffFoulCeiling must exceed RollAOffFoulFloor: " +
                $"floor={cfg.RollAOffFoulFloor}, ceiling={cfg.RollAOffFoulCeiling}.");
        // Static twin of the generator's runtime overflow guard: the three ceilings
        // summing to >= 1.0 would allow CleanEntry to go negative at max press.
        if (cfg.RollATurnoverCeiling + cfg.RollADefFoulCeiling + cfg.RollAOffFoulCeiling >= 1.0)
            throw new InvalidOperationException(
                $"RollATurnoverCeiling + RollADefFoulCeiling + RollAOffFoulCeiling must be < 1.0: " +
                $"got {cfg.RollATurnoverCeiling + cfg.RollADefFoulCeiling + cfg.RollAOffFoulCeiling:F4}.");

        // Phase 14 -- full-court press saturation constant (SEPARATE from halfcourt)
        if (cfg.FullCourtPressReferenceShift <= 0.0)
            throw new InvalidOperationException(
                $"FullCourtPressReferenceShift must be > 0: got {cfg.FullCourtPressReferenceShift}.");

        // Phase 14 -- turnover gap weights (must be >= 0; need not sum to 1)
        if (cfg.RollASkillWeight < 0.0)
            throw new InvalidOperationException(
                $"RollASkillWeight must be >= 0: got {cfg.RollASkillWeight}.");
        if (cfg.RollAAthleticismWeight < 0.0)
            throw new InvalidOperationException(
                $"RollAAthleticismWeight must be >= 0: got {cfg.RollAAthleticismWeight}.");
        if (cfg.RollASizeWeight < 0.0)
            throw new InvalidOperationException(
                $"RollASizeWeight must be >= 0: got {cfg.RollASizeWeight}.");

        return cfg;
    }

    // =========================================================================
    // Phase 10 — rebound door (the glass)
    // =========================================================================

    // --- Phase 10: pre-staging team-size composite blend.
    //     ReboundPhysical(p) = ReboundStrengthWeight * p.Strength + ReboundHeightWeight * p.Height.
    //     Weights need not sum to 1 (a weighted read, like LengthRating). Placeholders. ---

    /// <summary>Weight of <see cref="Player.Strength"/> in the pre-staging size composite.
    /// Calibration placeholder — equal thirds for now.</summary>
    public double ReboundStrengthWeight { get; set; } = 0.5;

    /// <summary>Weight of <see cref="Player.Height"/> in the pre-staging size composite.
    /// Calibration placeholder — equal thirds for now.</summary>
    public double ReboundHeightWeight { get; set; } = 0.5;

    // --- Phase 10: positional composite (Postness) blend.
    //     Postness(p) = PostnessHeight * p.Height + PostnessPostDefense * p.PostDefense
    //                 + PostnessStrength * p.Strength.
    //     Weights need not sum to 1 (a weighted read). Placeholders. ---

    /// <summary>Weight of <see cref="Player.Height"/> in the postness composite.
    /// Calibration placeholder — equal thirds.</summary>
    public double PostnessHeight { get; set; } = 1.0 / 3.0;

    /// <summary>Weight of <see cref="Player.PostDefense"/> in the postness composite.
    /// Calibration placeholder — equal thirds.</summary>
    public double PostnessPostDefense { get; set; } = 1.0 / 3.0;

    /// <summary>Weight of <see cref="Player.Strength"/> in the postness composite.
    /// Calibration placeholder — equal thirds.</summary>
    public double PostnessStrength { get; set; } = 1.0 / 3.0;

    // --- Phase 10: positional weight swing.
    //     posWeight_i = 1.0 + ReboundPositionalSwing * tanh((postness_i − meanPostness) / scale).
    //     Bounded in (1 − swing, 1 + swing) ≈ (0.8, 1.2) at swing=0.2; exactly 1 at mean.
    //     swing < 1.0 is enforced in Load (keeps weights strictly positive).
    //     scale > 0 is enforced in Load. Both are calibration knobs. ---

    /// <summary>Half-amplitude of the positional weight swing. Default 0.2 → range (0.8, 1.2).
    /// Must be in [0, 1) (enforced in Load). Calibration placeholder.</summary>
    public double ReboundPositionalSwing { get; set; } = 0.2;

    /// <summary>Rating-point spread at which one positional-weight unit of swing is reached
    /// (tanh saturation knob). Default 15.0. Must be &gt; 0 (enforced in Load).
    /// Calibration placeholder.</summary>
    public double ReboundPositionalScale { get; set; } = 15.0;

    // --- Phase 10: size/skill split for the total shift composition.
    //     totalShift = ReboundSizeWeight * sizeShift + ReboundSkillWeight * skillShift.
    //     Must sum to 1.0 (enforced in Load). ---

    /// <summary>Weight of the pre-staging size shift in the total rebound shift.
    /// Must sum to 1.0 with <see cref="ReboundSkillWeight"/> (enforced in Load).
    /// Calibration placeholder.</summary>
    public double ReboundSizeWeight { get; set; } = 0.45;

    /// <summary>Weight of the positional-weighted skill shift in the total rebound shift.
    /// Must sum to 1.0 with <see cref="ReboundSizeWeight"/> (enforced in Load).
    /// Calibration placeholder.</summary>
    public double ReboundSkillWeight { get; set; } = 0.55;

    // --- Phase 10: shooter nerf multiplier.
    //     Applied to the shooter's OffensiveRebounding contribution when zone is Three/Long/Mid.
    //     A multiplier in [0, 1]: 0 = shooter contributes nothing, 1 = no nerf.
    //     Rim/Short: no nerf (shooter is already inside). Must be in [0, 1] (enforced in Load). ---

    /// <summary>Multiplier on the shooter's <see cref="Player.OffensiveRebounding"/> contribution
    /// when the shot came from <c>Three</c>, <c>Long</c>, or <c>Mid</c>. The shooter is outside
    /// and can't crash his own miss as easily. Default 0.35 (significant but not zeroed).
    /// Must be in [0.0, 1.0] (enforced in Load). Calibration placeholder.</summary>
    public double ReboundShooterNerf { get; set; } = 0.35;

    // --- Phase 10: off-share floor and ceiling.
    //     The tanh saturation asymptotes toward these values without crossing.
    //     Defined on the OFF-SHARE (= offWeight / (defWeight + offWeight)), shared across
    //     both live-miss and block sources. The two sources start at different baselines
    //     (≈0.290 and ≈0.390) but bend within the same band.
    //     floor >= 0, ceiling <= 1.0, floor < ceiling (enforced in Load). ---

    /// <summary>The minimum off-share the rebound bend can reach (defense-dominant asymptote).
    /// Default 0.08. Must be &gt;= 0 and &lt; <see cref="ReboundOffShareCeiling"/>
    /// (enforced in Load). Calibration placeholder.</summary>
    public double ReboundOffShareFloor { get; set; } = 0.08;

    /// <summary>The maximum off-share the rebound bend can reach (offense-dominant asymptote).
    /// Default 0.55. Must be &lt;= 1.0 and &gt; <see cref="ReboundOffShareFloor"/>
    /// (enforced in Load). Calibration placeholder.</summary>
    public double ReboundOffShareCeiling { get; set; } = 0.55;

    // --- Phase 10: tanh saturation knob for the rebound contest.
    //     A net shift of ReboundReferenceShift reaches tanh(1) ≈ 76% of span.
    //     Default 20.0 (rating points). Must be > 0 (enforced in Load). ---

    /// <summary>The net shift (rating points) that reaches ~76% saturation toward
    /// the rebound off-share floor/ceiling. Higher = slower saturation (wider gap
    /// needed for a large shift). Must be &gt; 0 (enforced in Load).
    /// Calibration placeholder.</summary>
    public double ReboundReferenceShift { get; set; } = 20.0;
}
