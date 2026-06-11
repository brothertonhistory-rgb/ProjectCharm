using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll C lives here — nothing is hardcoded in logic.
/// Loaded from the "RollC" section of config.json.
/// </summary>
public sealed class RollCConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). Kept summing to 1 for clarity. ---
    public double BaseBadPassDeadBall { get; set; } = 0.30;
    public double BaseBadPassIntercepted { get; set; } = 0.22;
    public double BaseLostBallDeadBall { get; set; } = 0.18;
    public double BaseLostBallLiveBall { get; set; } = 0.20;
    public double BaseOffensiveFoul { get; set; } = 0.10;

    /// <summary>The single live wire proving the seam carries signal: how much a
    /// pressure of 1.0 adds to the live-strip weight before renormalization.
    /// Defensive ball pressure -> more live strips. Placeholder — not basketball
    /// logic.</summary>
    public double PressureLostBallLiveBallNudge { get; set; } = 0.10;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollCConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollC");
        var cfg = JsonSerializer.Deserialize<RollCConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollC config at {path}.");
    }
}
