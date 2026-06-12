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
    //     generator will replace these). Kept summing to 1 for clarity.
    //     NOTE (Session 13): Blocked left Roll F — block is now a per-zone slice
    //     of Roll H (it depends on WHERE the shot comes from, which only exists
    //     after Roll G). The old 3.5% block weight folded into ShotAttempt. ---
    public double BaseShotAttempt { get; set; } = 0.855;
    public double BaseTurnover { get; set; } = 0.09;
    public double BaseNonShootingFoul { get; set; } = 0.05;
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
