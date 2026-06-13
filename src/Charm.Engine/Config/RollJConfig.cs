using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll J (transition-entry run-or-not gate) lives here —
/// nothing hardcoded. Loaded from the "RollJ" section of config.json. Mirrors
/// <see cref="RollIConfig"/>: flat PLACEHOLDER weights, no live-wire scalar (the
/// only things that will tilt this pie are Roll J's two deferred, INDEPENDENT
/// modifier seams — rebounder tilt and coach tempo — documented on the roll).
///
/// <para>Three weight sets live here, one per live transition source. The REBOUND set
/// (a defensive rebound that pushed into transition) and the FreeThrowRebound set (a
/// tamer pie off a missed FT, fed by Roll M) were here already; the STEAL set (more
/// Push than either — a live theft runs hardest) was added in the steal-feeder session
/// (Contextification #3), exactly as <see cref="RollCConfig"/> grew a Transition set
/// beside its Halfcourt set. The three Push weights spread deliberately wide for
/// calibration: Steal 0.50 &gt; Rebound 0.30 &gt; FreeThrowRebound 0.08.</para>
/// </summary>
public sealed class RollJConfig
{
    // --- Rebound-context run-or-not weights (placeholders; the rebounder-tilt and
    //     coach-tempo modifiers replace the flatness later). The five sum to 1.
    //     Settle is the "proceed" analog (-> player selection); Push runs (-> the
    //     parked transition stub); the other three are the rare live-ball events.
    //     Push widened to 0.30 (Contextification #3) to spread the three transition
    //     contexts further apart for easier calibration — Steal 0.50 > Rebound 0.30 >
    //     FreeThrowRebound 0.08. ---
    public double Settle { get; set; } = 0.60;
    public double Push { get; set; } = 0.30;
    public double Turnover { get; set; } = 0.06;
    public double DefensiveFoul { get; set; } = 0.035;
    public double JumpBall { get; set; } = 0.005;

    // --- Free-throw-rebound-context run-or-not weights (placeholders; seeded
    //     CONSERVATIVE, Emmett's to tune). The SECOND weight set, a clean sibling to
    //     the Rebound set above — added with Roll M exactly as RollCConfig grew a
    //     Transition set beside its Halfcourt set. Off a missed FREE THROW the
    //     made/missed shot gave everyone time to get back, so the break runs LEAST: the
    //     lowest Push (0.08) and highest Settle of the three contexts. The five sum to
    //     1. ---
    public double FreeThrowSettle { get; set; } = 0.82;
    public double FreeThrowPush { get; set; } = 0.08;
    public double FreeThrowTurnover { get; set; } = 0.05;
    public double FreeThrowDefensiveFoul { get; set; } = 0.04;
    public double FreeThrowJumpBall { get; set; } = 0.01;

    // --- Steal-context run-or-not weights (placeholders; Contextification #3, Emmett's
    //     to tune). The THIRD weight set, a clean sibling to the two above. A live
    //     theft is the best fast-break trigger in basketball — the defender is already
    //     moving the other way with the offense caught upcourt — so the break runs MOST:
    //     the HIGHEST Push (0.50) and LOWEST Settle of the three contexts (Steal 0.50 >
    //     Rebound 0.30 > FreeThrowRebound 0.08). The rare live-ball events (turnover /
    //     foul / jump ball) mirror the Rebound set. The real speed/athleticism favoring
    //     ("who got the steal") is the deferred attribute seam; Roll J reads none yet.
    //     The five sum to 1. ---
    public double StealSettle { get; set; } = 0.40;
    public double StealPush { get; set; } = 0.50;
    public double StealTurnover { get; set; } = 0.06;
    public double StealDefensiveFoul { get; set; } = 0.035;
    public double StealJumpBall { get; set; } = 0.005;

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
