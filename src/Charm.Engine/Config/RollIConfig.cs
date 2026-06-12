using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll I (rebound resolution) lives here — nothing is
/// hardcoded in logic. Loaded from the "RollI" section of config.json. The four
/// base weights are best-guess PLACEHOLDERS and sum to 1 among themselves; the
/// real attribute-driven generator will replace them (tilting the offensive-rebound
/// rate by matchup/fatigue) without touching Roll I or the resolver.
///
/// No live-wire scalar (like Roll D/E/F/G/H): the only thing that would tilt this
/// pie is the deferred attribute model. The offensive-rebound rate is also a
/// possession-count calibration knob — too many offensive boards inflate
/// possessions per team above the ~67–70 anchor — so it is Emmett's to tune later
/// against the harness's possession-count and rebound-rate readouts.
/// </summary>
public sealed class RollIConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). The four sum to 1. Two flip the ball
    //     (DefensiveRebound, LooseBallFoulOnOffense → terminals); two keep it
    //     (OffensiveRebound, LooseBallFoulOnDefense → continues). ---
    public double DefensiveRebound { get; set; } = 0.68;
    public double OffensiveRebound { get; set; } = 0.29;
    public double LooseBallFoulOnDefense { get; set; } = 0.02;
    public double LooseBallFoulOnOffense { get; set; } = 0.01;

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
