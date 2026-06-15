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
    //     Five zones, each with its own four parameters. Fitted to three anchor
    //     points per zone (rating 1 / 50 / 99) in the Phase 2 design session.
    //     Every tunable here — nothing hardcoded in RollHGenerator. ---

    // Three (reads player.Outside)
    public double ThreeFloor    { get; set; } = 0.03;
    public double ThreeCeiling  { get; set; } = 0.65;
    public double ThreeK        { get; set; } = 0.057667;
    public double ThreeMidpoint { get; set; } = 49.6239;

    // Long (reads player.Outside)
    public double LongFloor    { get; set; } = 0.03;
    public double LongCeiling  { get; set; } = 0.63;
    public double LongK        { get; set; } = 0.061286;
    public double LongMidpoint { get; set; } = 45.4063;

    // Mid (reads player.Mid)
    public double MidFloor    { get; set; } = 0.05;
    public double MidCeiling  { get; set; } = 0.67;
    public double MidK        { get; set; } = 0.059158;
    public double MidMidpoint { get; set; } = 44.2696;

    // Short (reads player.Close)
    public double ShortFloor    { get; set; } = 0.08;
    public double ShortCeiling  { get; set; } = 0.83;
    public double ShortK        { get; set; } = 0.057781;
    public double ShortMidpoint { get; set; } = 46.3470;

    // Rim (reads player.Finishing)
    public double RimFloor    { get; set; } = 0.10;
    public double RimCeiling  { get; set; } = 0.93;
    public double RimK        { get; set; } = 0.061713;
    public double RimMidpoint { get; set; } = 42.1330;

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
        return cfg ?? throw new InvalidOperationException($"Could not parse RollH config at {path}.");
    }
}
