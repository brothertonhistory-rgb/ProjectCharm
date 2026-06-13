using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll I (rebound / loose-ball resolution) lives here —
/// nothing is hardcoded in logic. Loaded from the "RollI" section of config.json.
/// TWO weight sets, both summing to 1 among themselves and selected by the
/// <see cref="ReboundSource"/> the loose ball arrived with: the live-miss set (a
/// clean field-goal carom) and the Block set (a swatted shot). All are best-guess
/// PLACEHOLDERS; the real attribute-driven generator will replace them (tilting the
/// offensive-rebound rate by matchup/fatigue) without touching Roll I or the resolver.
///
/// The block set is a DELIBERATELY-different placeholder, per the design call: more of
/// the swat stays with the defense or squirts OOB, a HIGHER offensive-recovery rate
/// than a clean miss (a blocked player often beats his man to his own loose ball), and
/// a minuscule jump-ball sliver (small but non-zero so the harness can see it fire).
/// Real numbers are Emmett's to tune against the harness.
///
/// No live-wire scalar (like Roll D/E/F/G/H): the only thing that would tilt these
/// pies is the deferred attribute model. The offensive-rebound rate is also a
/// possession-count calibration knob — too many offensive boards inflate possessions
/// per team above the ~67–70 anchor — so it is Emmett's to tune later against the
/// harness's possession-count and rebound-rate readouts.
/// </summary>
public sealed class RollIConfig
{
    // --- Live-miss pie (a clean field-goal carom). Seven weights summing to 1.
    //     Selected when the loose ball carries ReboundSource.LiveBall (or a null
    //     stamp). The original four were rebalanced slightly to make room for the
    //     jump-ball + OOB slivers added in the block-recovery session. ---
    public double DefensiveRebound { get; set; } = 0.66;
    public double OffensiveRebound { get; set; } = 0.27;
    public double LooseBallFoulOnDefense { get; set; } = 0.02;
    public double LooseBallFoulOnOffense { get; set; } = 0.01;
    public double OutOfBoundsOffOffense { get; set; } = 0.025;
    public double OutOfBoundsOffDefense { get; set; } = 0.01;
    public double JumpBall { get; set; } = 0.005;

    // --- Block pie (a swatted shot — a loose-ball scramble). Seven weights summing
    //     to 1. Selected when the loose ball carries ReboundSource.Block. Deliberately
    //     non-flat vs. the live-miss set (placeholders, to be tuned). ---
    public double BlockDefensiveRebound { get; set; } = 0.50;
    public double BlockOffensiveRebound { get; set; } = 0.32;
    public double BlockLooseBallFoulOnDefense { get; set; } = 0.03;
    public double BlockLooseBallFoulOnOffense { get; set; } = 0.02;
    public double BlockOutOfBoundsOffOffense { get; set; } = 0.07;
    public double BlockOutOfBoundsOffDefense { get; set; } = 0.05;
    public double BlockJumpBall { get; set; } = 0.01;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollIConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollI");
        var cfg = JsonSerializer.Deserialize<RollIConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollI config at {path}.");
    }
}
