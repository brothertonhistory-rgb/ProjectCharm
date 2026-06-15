using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for the matchup layer (Phase 6) — nothing hardcoded in logic.
/// Loaded from the "Matchup" section of config.json, mirroring <see cref="RollHConfig.Load"/>.
///
/// <para><b>Two groups.</b> (1) The gap-function parameters (DEC-5): a steepness and an
/// exponent per axis plus a shared reference scale. (2) The per-zone defense-blend table
/// (CONF-1) as data — the blend is config, not a hardcoded switch, so it is tunable in
/// the calibration pass.</para>
///
/// <para><b>Defaults are PLACEHOLDERS.</b> The decision they encode is the shape and
/// parameterization (DEC-5) and the blend's perimeter→interior slide (CONF-1); the
/// magnitudes are a calibration-pass concern, set-and-left here. Physical is steeper
/// than skill via a higher exponent (the tail property that delivers "size
/// insurmountable"). The Rim split (0.35 post / 0.65 rim protection) is a placeholder
/// default; the rest of the blend table is spec.</para>
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

        return cfg;
    }
}
