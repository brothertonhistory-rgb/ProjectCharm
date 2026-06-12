using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll D lives here — nothing is hardcoded in logic.
/// Loaded from the "RollD" section of config.json. Covers both the flavor pie
/// weights (descriptive theater) and the bonus thresholds (functional routing).
/// </summary>
public sealed class RollDConfig
{
    // --- Flavor pie weights (descriptive/observability only; they do NOT route).
    //     Placeholders; a real attribute-driven generator will replace these.
    //     Kept summing to 1 for clarity. ---
    public double FlavorReachIn { get; set; } = 0.60;
    public double FlavorBlocking { get; set; } = 0.30;
    public double FlavorOffBall { get; set; } = 0.10;

    // --- Bonus thresholds (functional: they decide the route). NCAA classic:
    //     7th team foul -> 1-and-1; 10th -> double bonus. Tunable. ---

    /// <summary>Team-foul count at which the opponent enters the 1-and-1 bonus.</summary>
    public int BonusThreshold { get; set; } = 7;

    /// <summary>Team-foul count at which the opponent enters the double bonus.</summary>
    public int DoubleBonusThreshold { get; set; } = 10;

    /// <summary>Tolerance for the flavor-pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollDConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollD");
        var cfg = JsonSerializer.Deserialize<RollDConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollD config at {path}.");
    }
}
