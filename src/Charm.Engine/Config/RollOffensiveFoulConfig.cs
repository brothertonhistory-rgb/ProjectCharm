using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for the offensive-foul flavor pie lives here — nothing
/// is hardcoded in logic. Loaded from the "OffensiveFoulFlavor" section of
/// config.json. Two weight sets exist: one for frontcourt fouls (where illegal
/// screens dominate) and one for backcourt fouls (where charges and push-offs
/// dominate). Theater only — these never affect routing.
/// </summary>
public sealed class RollOffensiveFoulConfig
{
    // --- Frontcourt mix (ball already across halfcourt) ---
    public double FrontcourtCharge        { get; set; } = 0.30;
    public double FrontcourtPushOff       { get; set; } = 0.20;
    public double FrontcourtIllegalScreen { get; set; } = 0.50;

    // --- Backcourt mix (ball still being brought up) ---
    public double BackcourtCharge         { get; set; } = 0.40;
    public double BackcourtPushOff        { get; set; } = 0.50;
    public double BackcourtIllegalScreen  { get; set; } = 0.10;

    /// <summary>Tolerance for each pie's sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollOffensiveFoulConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("OffensiveFoulFlavor");
        var cfg = JsonSerializer.Deserialize<RollOffensiveFoulConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException(
            $"Could not parse OffensiveFoulFlavor config at {path}.");
    }
}
