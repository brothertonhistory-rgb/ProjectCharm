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

    public static MatchupConfig Load(string path)
    {
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

        return cfg;
    }
}
