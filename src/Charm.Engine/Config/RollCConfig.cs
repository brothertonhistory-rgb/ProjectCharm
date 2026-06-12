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

    // --- Transition-context weight set (selected when a turnover arrives stamped
    //     TurnoverContext.Transition — i.e. coughed up on the outlet/push). Flat
    //     placeholders, sum to 1. Rationale (Emmett's): transition turnovers are
    //     more often LIVE and going the other way — live strips (LostBallLiveBall)
    //     jump to 0.35 and the two live slices together are ~50% (vs. halfcourt's
    //     42%); offensive fouls nearly vanish (0.05) with little halfcourt contact
    //     to draw a charge. Tuned later like every pie. The Halfcourt set above is
    //     UNCHANGED, so the legacy path is byte-for-byte intact. ---
    public double TransitionBadPassDeadBall { get; set; } = 0.25;
    public double TransitionBadPassIntercepted { get; set; } = 0.15;
    public double TransitionLostBallDeadBall { get; set; } = 0.20;
    public double TransitionLostBallLiveBall { get; set; } = 0.35;
    public double TransitionOffensiveFoul { get; set; } = 0.05;

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
