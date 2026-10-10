using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll M (free-throw rebound resolution) lives here —
/// nothing is hardcoded in logic. Loaded from the "RollM" section of config.json.
/// Mirrors <see cref="RollIConfig"/>: flat PLACEHOLDER weights, no live-wire scalar
/// (the only things that will tilt this pie are the deferred attribute model — board
/// tilt by size / box-out / positioning along the lane — which replaces the flatness
/// later WITHOUT touching Roll M or the resolver).
///
/// <para>The seven weights sum to 1. They are seeded CONSERVATIVE and are Emmett's to
/// tune against the harness's rebound-rate and possession-count readouts. The split is
/// deliberately MORE DEFENSIVE than Roll I's field-goal board: off a free throw
/// everyone is lined calmly along the lane with the defense in the better box-out
/// spots and no offensive shooter crashing in, so the offensive-board share is lower
/// here than off a live miss. The added out-of-bounds PAIR (off-offense / off-defense)
/// has no analog in Roll I — a free-throw scramble kicks the ball out of bounds more
/// often than a normal rebound battle.</para>
///
/// <para>The offensive-rebound rate here is also a possession-count calibration knob
/// (an FT offensive board extends the possession via Roll K, exactly as a field-goal
/// offensive board does), so it is Emmett's to tune alongside Roll I's.</para>
/// </summary>
public sealed class RollMConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven generator
    //     will replace these). The seven sum to 1. One flips the ball on a LIVE board
    //     (DefensiveRebound -> transition terminal); two flip it on a DEAD ball
    //     (LooseBallFoulOnOffense, OutOfBoundsOffOffense -> terminals); the rest keep
    //     the offense's ball (OffensiveRebound -> Roll K; LooseBallFoulOnDefense ->
    //     the bonus fork; OutOfBoundsOffDefense -> sideline inbound; JumpBall -> the
    //     shared arrow node). ---
    public double DefensiveRebound { get; set; } = 0.715;
    public double OffensiveRebound { get; set; } = 0.20;
    public double LooseBallFoulOnDefense { get; set; } = 0.02;
    public double LooseBallFoulOnOffense { get; set; } = 0.01;
    public double OutOfBoundsOffOffense { get; set; } = 0.02;
    public double OutOfBoundsOffDefense { get; set; } = 0.03;
    public double JumpBall { get; set; } = 0.005;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    // --- ★ S120 — THE FREE-THROW LANE (Emmett's rulings, 2026-10-09). Six on the lane (four
    //     defenders, two offense), the shooter at the line, three back (one defender, two offense).
    //     See FreeThrowLane. ---

    /// <summary>The shooter's share of his own team's offensive boards off his miss ("rare").</summary>
    public double LaneShooterBoardShare { get; set; } = 0.03;
    /// <summary>Each offensive man back's share of his team's offensive boards.</summary>
    public double LaneOffenseBackBoardShare { get; set; } = 0.005;
    /// <summary>The defense's man back's share of his team's defensive boards.</summary>
    public double LaneDefenseBackBoardShare { get; set; } = 0.02;
    /// <summary>The shooter's share of the scramble fouls, drawn and committed ("exceedingly rare").
    /// The men back draw and commit none.</summary>
    public double LaneShooterScrambleShare { get; set; } = 0.005;

    /// <summary>Foul trouble keeps a man off the lane: this many fouls in the first half.</summary>
    public int LaneFoulTroubleFirstHalfFouls { get; set; } = 2;
    /// <summary>... or this many in the second half, while more than
    /// <see cref="LaneFoulTroubleSecondHalfSeconds"/> remain. Never in overtime.</summary>
    public int LaneFoulTroubleSecondHalfFouls { get; set; } = 4;
    public double LaneFoulTroubleSecondHalfSeconds { get; set; } = 300.0;

    /// <summary>The NORMAL LANE — the league as it played before S120 (stock season, seed
    /// 20260720, the pre-change engine, the lane selection run read-only at every missed last free
    /// throw): the mean lane totals, each trip counted once. Body is the sum of
    /// <see cref="Matchup.ReboundPhysical"/> over the lane men; rebounding is the sum of
    /// OffensiveRebounding (offense) or DefensiveRebounding (defense). Frozen; never recomputed in
    /// play. A lane with exactly these totals gets the default split.</summary>
    public double LaneNormalOffenseBody { get; set; } = 185.69113726619568;
    public double LaneNormalDefenseBody { get; set; } = 357.4895729659517;
    public double LaneNormalOffenseRebounding { get; set; } = 73.34045056749933;
    public double LaneNormalDefenseRebounding { get; set; } = 125.40139239575417;

    /// <summary>The swing's anchor: both offensive lane men this many points better on height,
    /// wingspan, strength and offensive rebounding move the split as much as a whole team this
    /// many points better does on the live-ball gap scale.</summary>
    public double LaneAnchorPoints { get; set; } = 10.0;

    /// <summary>The class defaults — what a Roll M pie generator that carries no config of its own
    /// (a harness test double) hands the resolver for the lane.</summary>
    public static RollMConfig ClassDefaults { get; } = new();

    public static RollMConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollM");
        var cfg = JsonSerializer.Deserialize<RollMConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollM config at {path}.");
    }
}
