using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for the Governor lives here — nothing is hardcoded in
/// logic. Loaded from the "Governor" section of config.json, exactly like the
/// per-roll configs.
/// </summary>
public sealed class GovernorConfig
{
    /// <summary>Safety ceiling on the number of possessions per run. The clock is
    /// the real stop rule; this guard exists so a game that somehow never drains the
    /// clock throws rather than spinning forever. Should be well above any realistic
    /// possession count (~130–145 for a full college game across both teams).</summary>
    public int PossessionCap { get; set; } = 400;

    /// <summary>Number of halves per game. Two for a standard college game.</summary>
    public int Halves { get; set; } = 2;

    /// <summary>Length of each half in seconds. 1200 = 20 minutes × 60 seconds per
    /// minute, matching NCAA regulation. The Governor counts each half down from this
    /// value and moves to the next half when it reaches zero.</summary>
    public double HalfSeconds { get; set; } = 1200.0;

    public static GovernorConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Governor");
        var cfg = JsonSerializer.Deserialize<GovernorConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse Governor config at {path}.");
    }
}
