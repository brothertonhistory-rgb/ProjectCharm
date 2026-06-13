using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every end-of-half clock-management number lives here — nothing is hardcoded in
/// logic. Loaded from the <c>"EndOfHalf"</c> section of config.json, exactly like
/// the per-roll configs.
///
/// <para>These are <b>uncalibrated starting knobs</b> Emmett tunes by watching games.
/// The three intent weights must sum to 1 (the <see cref="Pie{TOutcome}"/> validates
/// this on construction). A future score-aware layer will make the split depend on the
/// margin and the time — at which point this flat pie becomes a context-selected one
/// (a generator), and the three weight properties here become the context-neutral
/// fallback. That seam is deferred; today the Governor builds the pie once from these
/// weights, blind to score and tempo.</para>
/// </summary>
public sealed class EndOfHalfConfig
{
    /// <summary>The clock level (in seconds) at or above which nothing changes.
    /// Below this threshold the offense can hold the ball to the buzzer and deny the
    /// opponent a return trip, so the intent pie fires. Default = 30s = one full shot
    /// clock. <b>Tunable.</b></summary>
    public double HoldThresholdSeconds { get; set; } = 30.0;

    /// <summary>Weight of the milk-and-shoot-last intent: the offense holds the ball
    /// and shoots at the buzzer; the Governor forces elapsed to the WHOLE remaining
    /// half time so the half ends immediately. The majority. <b>Starting knob.</b></summary>
    public double HoldShootLast { get; set; } = 0.70;

    /// <summary>Weight of the normal-tempo intent: the offense takes a shot at the
    /// usual pace; elapsed is drawn and capped normally, so the opponent may get a
    /// return trip if time remains. A minority. <b>Starting knob.</b></summary>
    public double ShootEarly { get; set; } = 0.20;

    /// <summary>Weight of the run-out-the-clock intent: the offense lets the clock
    /// expire without a shot attempt. Zero points; elapsed equals the whole remaining
    /// half time; the resolver is not called. A small slice. <b>Starting knob.</b></summary>
    public double NoShot { get; set; } = 0.10;

    /// <summary>Tolerance for the intent-pie sum-to-one check, matching every other
    /// config.</summary>
    public double Epsilon { get; set; } = 1e-09;

    public static EndOfHalfConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("EndOfHalf");
        var cfg = JsonSerializer.Deserialize<EndOfHalfConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse EndOfHalf config at {path}.");
    }
}
