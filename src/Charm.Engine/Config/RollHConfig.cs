using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll H lives here — nothing is hardcoded in logic.
/// Loaded from the "RollH" section of config.json. The six base weights are
/// realistic PLACEHOLDERS (roughly real D1, location-BLIND — the stub does not
/// read ShotType); the real attribute-driven generator will replace them without
/// touching Roll H or the resolver.
/// </summary>
public sealed class RollHConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). These FOUR are the SHARED make/miss SHAPE
    //     for the non-foul, non-block outcomes and sum to less than 1 among themselves
    //     (foul and block are carved separately in Phase 8). Per zone the generator
    //     carves block(zone) and foul(zone) off the top and scales these three miss/OOB
    //     weights by the remaining share. Location-BLIND otherwise. ---
    public double BaseMade { get; set; } = 0.43;
    public double BaseMiss { get; set; } = 0.47;
    public double BaseMissOutOfBoundsLost { get; set; } = 0.02;
    public double BaseMissOutOfBoundsRetained { get; set; } = 0.01;

    // --- Per-zone block weight b(zone) (Session 13). A block depends on WHERE the
    //     shot comes from: rim attempts get swatted far more than threes. So the
    //     block slice of Roll H's pie is sized per zone — Rim highest, Three
    //     lowest — carved off the top, with the six make/miss weights above scaled
    //     by (1 − b(zone)). Best-guess placeholders; tune against the harness's
    //     zone-blended block-rate readout. Only block is zone-aware this pass.
    //     One flat number per zone (five), NOT a 35-number per-zone make/miss
    //     table. ---
    public double BlockRim { get; set; } = 0.12;
    public double BlockShort { get; set; } = 0.06;
    public double BlockMid { get; set; } = 0.03;
    public double BlockLong { get; set; } = 0.02;
    public double BlockThree { get; set; } = 0.01;

    /// <summary>The block weight b(zone) for a given shot location — the single
    /// place the zone→weight mapping lives, so the generator and the harness's
    /// blended-rate math read the same numbers.</summary>
    public double BlockWeight(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => BlockRim,
        ShotLocation.Short => BlockShort,
        ShotLocation.Mid   => BlockMid,
        ShotLocation.Long  => BlockLong,
        ShotLocation.Three => BlockThree,
        _ => throw new InvalidOperationException($"No block weight for zone '{zone}'.")
    };

    // --- Per-zone shooting-foul baseline (Phase 8). Replaces the flat
    //     per-shooting-foul base weights. Three-point fouls are rare; rim
    //     fouls are common — flatness was basketball-wrong. Placeholders.
    //     The matchup-aware foul rate (Matchup.FoulRate) bends this baseline
    //     toward a per-zone ceiling (foul-drawer edge) or floor (disciplined
    //     defender edge), using MatchupConfig's new foul floor/ceiling fields. ---
    public double FoulRim   { get; set; } = 0.20;
    public double FoulShort { get; set; } = 0.10;
    public double FoulMid   { get; set; } = 0.05;
    public double FoulLong  { get; set; } = 0.03;
    public double FoulThree { get; set; } = 0.015;

    /// <summary>The shooting-foul baseline for a given shot location.
    /// Single place the zone→foul-baseline mapping lives (generator and harness
    /// read the same numbers). Phase 8's Matchup.FoulRate bends this per matchup;
    /// this value is the DEC-6 fallback and the midpoint for the tanh bend.</summary>
    public double FoulRate(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => FoulRim,
        ShotLocation.Short => FoulShort,
        ShotLocation.Mid   => FoulMid,
        ShotLocation.Long  => FoulLong,
        ShotLocation.Three => FoulThree,
        _ => throw new InvalidOperationException($"No foul rate for zone '{zone}'.")
    };

    // --- Per-zone and-1 split (Phase 8). When a foul is drawn, the fraction
    //     that becomes MadeAndFouled (and-1); the rest becomes MissFouled.
    //     Rim fouls become and-1s often (layup through contact); three fouls
    //     rarely (the shot is disrupted). NOT matchup-aware — Emmett's call.
    //     Placeholders; magnitudes are calibration work. ---
    public double MafFractionRim   { get; set; } = 0.35;
    public double MafFractionShort { get; set; } = 0.28;
    public double MafFractionMid   { get; set; } = 0.18;
    public double MafFractionLong  { get; set; } = 0.12;
    public double MafFractionThree { get; set; } = 0.10;

    /// <summary>The and-1 fraction of a drawn foul (MadeAndFouled / total foul).
    /// The complement (1 − MafFraction) becomes MissFouled.
    /// Single place the zone→MAF-split lives; generator and harness read the same
    /// numbers. NOT matchup-aware — per-zone config only.</summary>
    public double MafFraction(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => MafFractionRim,
        ShotLocation.Short => MafFractionShort,
        ShotLocation.Mid   => MafFractionMid,
        ShotLocation.Long  => MafFractionLong,
        ShotLocation.Three => MafFractionThree,
        _ => throw new InvalidOperationException($"No MAF fraction for zone '{zone}'.")
    };

    // --- Putback pie (Session 17). A go-back-up off an offensive rebound is a
    //     DISTINCT shot population from a normal located attempt — point-blank,
    //     often through contact — so it gets its OWN seven-way make/miss/foul pie
    //     rather than reusing the at-the-rim numbers. Selected when Roll K's PutBack
    //     arm stamps the putback ticket (it also forces the zone to Rim). These seven
    //     sum to 1 among themselves and are best-guess PLACEHOLDERS: the real
    //     percentages — and the tilt by the putback-er's size / athleticism / rim
    //     rating and the defender contesting — are a future basketball call delivered
    //     by the attribute-driven generator, which reads the carried slot. Block is
    //     just a flat slice here (no per-zone carve: a putback is always Rim), unlike
    //     the located-shot pie above. ---
    public double PutbackMade { get; set; } = 0.50;
    public double PutbackMadeAndFouled { get; set; } = 0.08;
    public double PutbackMiss { get; set; } = 0.28;
    public double PutbackMissFouled { get; set; } = 0.05;
    public double PutbackMissOutOfBoundsLost { get; set; } = 0.01;
    public double PutbackMissOutOfBoundsRetained { get; set; } = 0.01;
    public double PutbackBlocked { get; set; } = 0.07;

    // --- Per-zone logistic parameters (Phase 2). The real attribute-driven
    //     generator reads the shooter's zone-relevant rating and runs a bounded
    //     logistic: makePct = Floor + (Ceiling - Floor) / (1 + exp(-K * (rating - Midpoint))).
    //     Five zones, each with its own four parameters.
    //
    //     CALIBRATED Session 50 (replaces the Phase 2 placeholders). Fitted to the
    //     agreed per-zone OBSERVED FG% anchors at rating 1 / 50 / 99 (even matchup):
    //       Three 22/34/50, Long 24/36/51, Mid 26/39/51, Short 30/43/55, Rim 48/61/73.
    //     50 = average; a rating-50 roster nets ~45% combined (real D1). The curves are
    //     deliberately gentle (a 16-point rating gap ~= 5% make, inside season noise).
    //     NOTE: these are clean-make (makePct) anchors carve-CORRECTED for each zone's
    //     block+foul rates, so the OBSERVED FG% (made/FGA, what the harness reports)
    //     lands on target — most visibly at Rim, where the make ceiling is raised to
    //     net ~73% observed after the rim block/foul carve. If the Roll H block or foul
    //     baselines change, the rim/short make anchors should be re-derived.
    //     Every tunable here — nothing hardcoded in RollHGenerator. ---

    // Three (reads player.Outside)
    public double ThreeFloor    { get; set; } = 0.1608;
    public double ThreeCeiling  { get; set; } = 0.6328;
    public double ThreeK        { get; set; } = 0.029646;
    public double ThreeMidpoint { get; set; } = 60.6;

    // Long (reads player.Outside)
    public double LongFloor    { get; set; } = 0.1934;
    public double LongCeiling  { get; set; } = 0.6034;
    public double LongK        { get; set; } = 0.034190;
    public double LongMidpoint { get; set; } = 59.5793;

    // Mid (reads player.Mid)
    public double MidFloor    { get; set; } = 0.1042;
    public double MidCeiling  { get; set; } = 0.6447;
    public double MidK        { get; set; } = 0.021592;
    public double MidMidpoint { get; set; } = 42.3369;

    // Short (reads player.Close)
    public double ShortFloor    { get; set; } = 0.1316;
    public double ShortCeiling  { get; set; } = 0.7045;
    public double ShortK        { get; set; } = 0.021592;
    public double ShortMidpoint { get; set; } = 42.3369;

    // Rim (reads player.Finishing)
    public double RimFloor    { get; set; } = 0.3582;
    public double RimCeiling  { get; set; } = 0.9527;
    public double RimK        { get; set; } = 0.024666;
    public double RimMidpoint { get; set; } = 43.9840;

    /// <summary>
    /// The bounded logistic make probability for a given zone and player rating.
    /// Single implementation owned by config so the generator and the harness's
    /// validation checks always read the same formula.
    ///
    /// <para>Formula: Floor + (Ceiling − Floor) / (1 + exp(−K × (rating − Midpoint)))</para>
    ///
    /// <para>Zone→attribute mapping (caller's responsibility to pass the right rating):
    /// Three/Long → player.Outside; Mid → player.Mid; Short → player.Close;
    /// Rim → player.Finishing.</para>
    /// </summary>
    public double MakeProbability(ShotLocation zone, double rating)
    {
        double floor, ceiling, k, midpoint;
        switch (zone)
        {
            case ShotLocation.Three:
                floor = ThreeFloor; ceiling = ThreeCeiling; k = ThreeK; midpoint = ThreeMidpoint; break;
            case ShotLocation.Long:
                floor = LongFloor;  ceiling = LongCeiling;  k = LongK;  midpoint = LongMidpoint;  break;
            case ShotLocation.Mid:
                floor = MidFloor;   ceiling = MidCeiling;   k = MidK;   midpoint = MidMidpoint;   break;
            case ShotLocation.Short:
                floor = ShortFloor; ceiling = ShortCeiling; k = ShortK; midpoint = ShortMidpoint; break;
            case ShotLocation.Rim:
                floor = RimFloor;   ceiling = RimCeiling;   k = RimK;   midpoint = RimMidpoint;   break;
            default:
                throw new InvalidOperationException($"No logistic parameters for zone '{zone}'.");
        }
        return floor + (ceiling - floor) / (1.0 + Math.Exp(-k * (rating - midpoint)));
    }

    // No live-wire scalar (like Roll E, F, and G): the only thing that would tilt
    // Roll H's pie is the deferred player/attribute model (the shooter-vs-defender
    // matchup, the other-four gravity term, the skill/athleticism gates, the
    // bounded logistic mapping, shot quality folded into the make %). Inventing a
    // placeholder wire here would pantomime the exact signal that is deliberately
    // deferred. Ships flat-ish; the real generator drops in later.

    // --- Usage-pressure efficiency penalty dials (Phase 17). Applied by
    //     RollHGenerator to makePct before BuildRealPie.
    //
    //     PressureVolumeTaxScale — the small all-shots reduction: makePct is
    //     multiplied by (1 − pressure × scale). Represents the baseline difficulty
    //     of sustaining a larger share. Tuned so a versatile player at the rail
    //     (~0.32 pressure) loses ~2–4 pts. Default 0.12. Named "volume-tax" NOT
    //     "attention" — attention is the deferred gravity layer.
    //
    //     PressureResidualPenaltyScale — the larger reduction for specialists:
    //     makePct -= residual × scale. Represents the efficiency loss of load
    //     that could not shift to alternate zones. Tuned so a specialist at the
    //     rail loses ~10–15 pts total. Default 2.0. ---
    public double PressureVolumeTaxScale        { get; set; } = 0.12;
    public double PressureResidualPenaltyScale  { get; set; } = 2.0;

    // ── Phase 27 — attention/openness make% knobs ────────────────────────────
    // C1: bonus-only openness nudge. AttentionRelief = max(0, 0.20 − a).
    //     ShooterOpenness = clamp(TeamBaseOpenness × AttentionRelief × ReliefScale, 0, 1).
    //     A well-spaced floor + under-attended shooter → positive make% lift.
    //     Equal-share or above → zero (bonus-only, never docks).
    //     [CALIBRATION PLACEHOLDER]
    public double C1ReliefScale { get; set; } = 2.0;

    // C2: zone-specific imbalance penalty.
    //     spacingExcess = max(0, TeamSpacingLevel − TeamGravityLevel)  → docks Three/Long
    //     gravityExcess = max(0, TeamGravityLevel − TeamSpacingLevel)  → docks Rim/Short
    //     penalty = excess × C2ImbalanceScale. Halfcourt and non-putback only (A5).
    //     [CALIBRATION PLACEHOLDER]
    public double C2ImbalanceScale { get; set; } = 0.08;

    // C3: amplifier on the Phase 17 usage penalty.
    //     AttentionPressure = max(0, attentionShare − 0.20).
    //     Both Phase 17 terms are multiplied by (1 + AttentionPressure × C3AttentionAmplifier).
    //     Equal-share neutral (multiplier = ×1); above-share amplifies both penalties.
    //     Zero usage pressure → zero penalty regardless of attention (C3 cannot create a penalty).
    //     [CALIBRATION PLACEHOLDER]
    public double C3AttentionAmplifier { get; set; } = 1.5;

    // ── Passing converter (Phase 27 Session 2) ────────────────────────────────
    // PassingBonus = MaxPassingBonus × conversionQuality × opportunityGate
    //   opportunityGate = lerp(PassingOpportunityFloor, 1.0, TeamBaseOpenness)
    // Bonus-only; attention-independent; halfcourt + non-putback only.

    /// <summary>Floor of the opportunity gate. When TeamBaseOpenness ≈ 0, the gate
    /// collapses to this floor (allows a small passing bonus even with no gravity/spacing
    /// engine). Invariant: in [0, 1). [CALIBRATION PLACEHOLDER]</summary>
    public double PassingOpportunityFloor { get; set; } = 0.10;

    /// <summary>Maximum absolute passing bonus added to makePct. Explicit ceiling on
    /// the converter's contribution to make%. Invariant: in (0, 1].
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double MaxPassingBonus { get; set; } = 0.08;

    // ── Session 03 — HelpDefense interior make% suppression (C6) ──────────────
    // The four off-ball defenders (matched defender excluded) rotate to help on
    // interior shots (Rim/Short). Their HelpDefense aggregates with an ACCELERATING
    // curve (one good helper ≈ a sliver; four compound) and reduces make%.
    // Halfcourt only. Standalone suppressor — the Screening counterweight lands next
    // session. [CALIBRATION PLACEHOLDER]

    /// <summary>Maximum make%-point suppression from perfect off-ball help defense.
    /// Invariant: in [0, 1] (above 1.0 is nonsensical as a maximum percentage-point
    /// reduction). [CALIBRATION PLACEHOLDER]</summary>
    public double HelpDefenseSuppressionScale  { get; set; } = 0.15;

    /// <summary>Exponent for the accelerating HelpDefense aggregate. Must be strictly
    /// greater than 1.0 — 1.0 is linear, below 1.0 is diminishing; both violate the
    /// locked accelerating-curve design. [CALIBRATION PLACEHOLDER]</summary>
    public double HelpDefenseAggregateExponent { get; set; } = 2.0;

    // ── Session 04 — Screening interior make% bonus (C5.5) ────────────────────
    // The five offensive players (shooter included) set screens that aggregate
    // with an ACCELERATING curve (one good screener ≈ a sliver; five compound)
    // and LIFT make% on ALL FIVE ZONES (Phase 44: gate removed). Halfcourt only.
    // Symmetric mirror of C7 by design: at full lineup capacity vs full off-ball
    // capacity (4.0 denominator), the bonus and suppression are analytically equal
    // at Long/Three. [CALIBRATION PLACEHOLDER]

    /// <summary>Maximum make%-point bonus from perfect five-player screening.
    /// Symmetric mirror of <see cref="OffBallDefenseSuppressionScale"/>: at full
    /// capacity on each side, the bonus and suppression cancel algebraically at
    /// perimeter zones. Invariant: in [0, 1]. [CALIBRATION PLACEHOLDER]</summary>
    public double ScreeningBonusScale         { get; set; } = 0.15;

    /// <summary>Exponent for the accelerating Screening aggregate. Must be
    /// strictly greater than 1.0 — 1.0 is linear, below 1.0 is diminishing;
    /// both violate the locked accelerating-curve design.
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double ScreeningAggregateExponent  { get; set; } = 2.0;

    /// <summary>Zone multiplier for HelpDefense suppression at Mid — partial effect
    /// (between 0 = no effect and 1 = full interior suppression). [CALIBRATION PLACEHOLDER]</summary>
    public double HelpDefenseMidMultiplier { get; set; } = 0.30;

    /// <summary>Maximum make%-point suppression from OffBallDefense at full aggregate.
    /// Symmetric counterpart to HelpDefenseSuppressionScale at the perimeter.
    /// [CALIBRATION PLACEHOLDER]</summary>
    public double OffBallDefenseSuppressionScale { get; set; } = 0.15;

    /// <summary>Exponent for OffBallDefense aggregate — must be > 1.0 for accelerating
    /// aggregation (same contract as HelpDefenseAggregateExponent). [CALIBRATION PLACEHOLDER]</summary>
    public double OffBallDefenseAggregateExponent { get; set; } = 2.0;

    /// <summary>Zone multiplier for OffBallDefense suppression at Mid — partial effect
    /// (between 0 = no effect and 1 = full perimeter suppression). [CALIBRATION PLACEHOLDER]</summary>
    public double OffBallDefenseMidMultiplier { get; set; } = 0.30;

    // ── Phase 50 — Basketball IQ make-door conversion bonus ───────────────────
    // IQ is the LAST make% term: a small, bounded, PROPORTIONAL sprinkle on the
    // already-settled make%. RollHGenerator computes
    //   bump = settledMakePct × IqMakeSensitivity × ZoneWeight × iqProgress,
    //   iqProgress = clamp((BasketballIQ − 50) / 49, 0, 1)
    // so a good look (high settled make%) gets a meaningful bump and a poor one a
    // rounding error — IQ rewards ability already on the plate, it never manufactures
    // it (a genius-IQ poor shooter is still a poor shooter). Driven by the SHOOTER's
    // OWN BasketballIQ (absolute, not relative). The per-zone ZoneWeights are fixed
    // CODE CONSTANTS in RollHGenerator (Three/Long 1.0, Mid 0.7, Short 0.3, Rim 0.0) —
    // deliberately NOT config fields, to avoid five extra user-facing knobs at locked
    // placeholder values. This single sensitivity is the only tunable. 0.08 lands the
    // ~34→37 jumper anchor at max IQ; the 0.20 ceiling bounds the most IQ alone may
    // ever swing one shot (~34→41). 0.0 = make-door IQ OFF (inertness anchor).

    /// <summary>Single tunable for the Phase 50 IQ make-door bonus — multiplied by
    /// fixed per-zone code constants and the IQ-progress scalar to size the
    /// proportional bump on the settled make%. Ships at 0.08; Load-guarded to
    /// [0.0, 0.20]. 0.0 = IQ make-door OFF. [CALIBRATION PLACEHOLDER]</summary>
    public double IqMakeSensitivity { get; set; } = 0.08;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollHConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollH");
        var cfg = JsonSerializer.Deserialize<RollHConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (cfg is null)
            throw new InvalidOperationException($"Could not parse RollH config at {path}.");

        // Phase 17 invariants: both penalty scales must be non-negative.
        if (cfg.PressureVolumeTaxScale < 0)
            throw new InvalidOperationException(
                "RollH PressureVolumeTaxScale must be >= 0.");
        if (cfg.PressureResidualPenaltyScale < 0)
            throw new InvalidOperationException(
                "RollH PressureResidualPenaltyScale must be >= 0.");

        // Phase 27 invariants
        if (cfg.C1ReliefScale < 0)
            throw new InvalidOperationException("RollH C1ReliefScale must be >= 0.");
        if (cfg.C2ImbalanceScale < 0)
            throw new InvalidOperationException("RollH C2ImbalanceScale must be >= 0.");
        if (cfg.C3AttentionAmplifier < 0)
            throw new InvalidOperationException("RollH C3AttentionAmplifier must be >= 0.");
        if (cfg.PassingOpportunityFloor < 0 || cfg.PassingOpportunityFloor >= 1.0)
            throw new InvalidOperationException(
                $"RollH PassingOpportunityFloor must be in [0, 1) (got {cfg.PassingOpportunityFloor}).");
        if (cfg.MaxPassingBonus <= 0 || cfg.MaxPassingBonus > 1.0)
            throw new InvalidOperationException(
                $"RollH MaxPassingBonus must be in (0, 1] (got {cfg.MaxPassingBonus}).");

        // Session 03 invariants
        // Scale: a make%-point suppressor; [0, 1] is the semantically valid range
        // (above 1.0 is nonsensical as a maximum percentage-point reduction; the make%
        // clamp would catch it, but it must not be expressible). Note: Roll H knobs are
        // NOT uniformly bounded above — C3AttentionAmplifier defaults to 1.5 and is only
        // bounded >= 0 — so there is no convention requiring an upper bound; [0,1] is
        // added here because the suppressor's units make an upper bound meaningful.
        if (cfg.HelpDefenseSuppressionScale < 0.0 || cfg.HelpDefenseSuppressionScale > 1.0)
            throw new InvalidOperationException(
                "RollH HelpDefenseSuppressionScale must be in [0, 1].");
        // Exponent: must be STRICTLY > 1.0 for accelerating aggregation. 1.0 is linear,
        // < 1.0 is diminishing — both violate the locked design and would fail harness
        // sub-check (b), which requires suppression(4 helpers) > 4 × suppression(1 helper).
        if (cfg.HelpDefenseAggregateExponent <= 1.0)
            throw new InvalidOperationException(
                "RollH HelpDefenseAggregateExponent must be > 1.0 for accelerating aggregation.");

        // Session 04 invariants
        // Scale: a make%-point bonus; [0, 1] is the semantically valid range
        // (above 1.0 is nonsensical as a maximum percentage-point lift; the make%
        // clamp at 1.0 would catch it, but it must not be expressible). Symmetric
        // with HelpDefenseSuppressionScale by design.
        if (cfg.ScreeningBonusScale < 0.0 || cfg.ScreeningBonusScale > 1.0)
            throw new InvalidOperationException(
                "RollH ScreeningBonusScale must be in [0, 1].");
        // Exponent: must be STRICTLY > 1.0 for accelerating aggregation — same
        // contract as HelpDefenseAggregateExponent. Linear or diminishing would
        // fail harness sub-check (b), which requires bonus(5 screeners) >
        // 5 × bonus(1 screener).
        if (cfg.ScreeningAggregateExponent <= 1.0)
            throw new InvalidOperationException(
                "RollH ScreeningAggregateExponent must be > 1.0 for accelerating aggregation.");

        // Session 06 invariants — HelpDefense Mid multiplier and OffBallDefense knobs
        if (cfg.HelpDefenseMidMultiplier < 0.0 || cfg.HelpDefenseMidMultiplier > 1.0)
            throw new InvalidOperationException(
                "RollH HelpDefenseMidMultiplier must be in [0, 1].");
        if (cfg.OffBallDefenseSuppressionScale < 0.0 || cfg.OffBallDefenseSuppressionScale > 1.0)
            throw new InvalidOperationException(
                "RollH OffBallDefenseSuppressionScale must be in [0, 1].");
        if (cfg.OffBallDefenseAggregateExponent <= 1.0)
            throw new InvalidOperationException(
                "RollH OffBallDefenseAggregateExponent must be > 1.0 for accelerating aggregation.");
        if (cfg.OffBallDefenseMidMultiplier < 0.0 || cfg.OffBallDefenseMidMultiplier > 1.0)
            throw new InvalidOperationException(
                "RollH OffBallDefenseMidMultiplier must be in [0, 1].");

        // Phase 50 invariant — IQ make-door sensitivity. Bounded on BOTH sides on
        // purpose: 0.0 = make-door IQ OFF (the inertness anchor for the zero-knob
        // byte-compare); the 0.20 ceiling is a DESIGN bound on the most IQ alone may
        // swing a single shot (~34→41 make% at max IQ on a jumper). Unlike most Roll H
        // knobs (bounded ≥ 0 only), this one has a meaningful upper limit.
        if (cfg.IqMakeSensitivity < 0.0 || cfg.IqMakeSensitivity > 0.20)
            throw new InvalidOperationException(
                $"RollH IqMakeSensitivity must be in [0.0, 0.20] (got {cfg.IqMakeSensitivity}).");

        return cfg;
    }
}
