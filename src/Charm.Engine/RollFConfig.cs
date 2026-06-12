using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll F lives here — nothing is hardcoded in logic.
/// Loaded from the "RollF" section of config.json. The five base weights are
/// realistic PLACEHOLDERS; the real attribute-driven generator will replace them
/// without touching Roll F or the resolver.
/// </summary>
public sealed class RollFConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). Kept summing to 1 for clarity. ---
    public double BaseShotAttempt { get; set; } = 0.82;
    public double BaseTurnover { get; set; } = 0.09;
    public double BaseNonShootingFoul { get; set; } = 0.05;
    public double BaseBlocked { get; set; } = 0.035;
    public double BaseJumpBall { get; set; } = 0.005;

    // No live-wire scalar (unlike Roll B's physicality and Roll C's pressure):
    // the only thing that would tilt Roll F's pie is the deferred player/attribute
    // model, and Roll F sits one inch from it. Inventing a placeholder wire here
    // would pantomime the exact signal that is deliberately deferred — the same
    // call Roll E made. Ships flat; the real generator drops in later.

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollFConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollF");
        var cfg = JsonSerializer.Deserialize<RollFConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollF config at {path}.");
    }
}
