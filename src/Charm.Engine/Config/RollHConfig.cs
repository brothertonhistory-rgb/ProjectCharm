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
    //     generator will replace these). Kept summing to 1 for clarity. ---
    public double BaseMade { get; set; } = 0.43;
    public double BaseMadeAndFouled { get; set; } = 0.03;
    public double BaseMiss { get; set; } = 0.47;
    public double BaseMissFouled { get; set; } = 0.04;
    public double BaseMissOutOfBoundsLost { get; set; } = 0.02;
    public double BaseMissOutOfBoundsRetained { get; set; } = 0.01;

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
