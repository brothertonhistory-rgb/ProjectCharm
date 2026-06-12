using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll J (transition-entry run-or-not gate) lives here —
/// nothing hardcoded. Loaded from the "RollJ" section of config.json. Mirrors
/// <see cref="RollIConfig"/>: flat PLACEHOLDER weights, no live-wire scalar (the
/// only things that will tilt this pie are Roll J's two deferred, INDEPENDENT
/// modifier seams — rebounder tilt and coach tempo — documented on the roll).
///
/// <para>The five weights are the REBOUND context's pie (a defensive rebound that
/// pushed into transition) — the only transition source fed this session. The
/// Steal context's pie (more Push, extra if the steal came from an entry-stage
/// turnover) is a SIBLING weight set added in the steal-feeder session, exactly as
/// <see cref="RollCConfig"/> grew a Transition set beside its Halfcourt set. One
/// context is fed now, so one set lives here.</para>
/// </summary>
public sealed class RollJConfig
{
    // --- Rebound-context run-or-not weights (placeholders; the rebounder-tilt and
    //     coach-tempo modifiers replace the flatness later). The five sum to 1.
    //     Settle is the "proceed" analog (-> player selection); Push runs (-> the
    //     parked transition stub); the other three are the rare live-ball events. ---
    public double Settle { get; set; } = 0.65;
    public double Push { get; set; } = 0.25;
    public double Turnover { get; set; } = 0.06;
    public double DefensiveFoul { get; set; } = 0.035;
    public double JumpBall { get; set; } = 0.005;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollJConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollJ");
        var cfg = JsonSerializer.Deserialize<RollJConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollJ config at {path}.");
    }
}
