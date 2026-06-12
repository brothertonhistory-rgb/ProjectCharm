using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll G lives here — nothing is hardcoded in logic.
/// Loaded from the "RollG" section of config.json. The five base weights are
/// realistic PLACEHOLDERS (roughly real D1 attempt shares); the real
/// attribute-driven generator will replace them without touching Roll G or the
/// resolver.
/// </summary>
public sealed class RollGConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). Kept summing to 1 for clarity. ---
    public double BaseThree { get; set; } = 0.36;
    public double BaseLong { get; set; } = 0.08;
    public double BaseMid { get; set; } = 0.10;
    public double BaseShort { get; set; } = 0.11;
    public double BaseRim { get; set; } = 0.35;

    // No live-wire scalar (like Roll E and Roll F): the only thing that would tilt
    // Roll G's pie is the deferred player/attribute model (shot selection, role,
    // defensive pressure). Inventing a placeholder wire here would pantomime the
    // exact signal that is deliberately deferred. Ships flat-ish; the real
    // generator drops in later.

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollGConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollG");
        var cfg = JsonSerializer.Deserialize<RollGConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollG config at {path}.");
    }
}
