using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll B lives here — nothing is hardcoded in logic.
/// Loaded from the "RollB" section of config.json.
/// </summary>
public sealed class RollBConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). Kept summing to 1 for clarity. ---
    public double BaseProceed { get; set; } = 0.85;
    public double BaseFoul { get; set; } = 0.12;
    public double BaseDeadBallTurnover { get; set; } = 0.03;

    /// <summary>The single live wire proving the seam carries signal: how much
    /// a physicality of 1.0 adds to the foul weight before renormalization.
    /// Placeholder — not basketball logic.</summary>
    public double PhysicalityFoulNudge { get; set; } = 0.10;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollBConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollB");
        var cfg = JsonSerializer.Deserialize<RollBConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollB config at {path}.");
    }
}
