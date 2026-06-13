using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll K (offensive-rebound resolution) lives here —
/// nothing is hardcoded in logic. Loaded from the "RollK" section of config.json.
/// Mirrors <see cref="RollIConfig"/>: flat PLACEHOLDER weights, no live-wire scalar
/// (the only things that will tilt this pie are the deferred attribute model — who
/// grabbed the board, how big/athletic, the rim matchup — which replace the
/// flatness later WITHOUT touching Roll K or the resolver).
///
/// <para>The seven weights sum to 1. Two keep the ball with the offense and stay
/// alive as the loop (<see cref="OffensiveReboundOutcome.PutBack"/>,
/// <see cref="OffensiveReboundOutcome.ResetOffense"/>); the
/// <see cref="OffensiveReboundOutcome.DefensiveFoul"/> arm also keeps the offense's
/// ball (it forks on the bonus); three flip the ball (the two turnovers and the
/// offensive foul → terminals); <see cref="OffensiveReboundOutcome.JumpBall"/> is a
/// continue to the shared arrow node.</para>
///
/// <para>The PutBack/ResetOffense split is the headline calibration knob: too many
/// putbacks-and-resets inflate possessions and shots above the anchor, so it is
/// Emmett's to tune against the harness's possession-count and shot-rate readouts.
/// The actual make/foul/and-1 PERCENTAGES of a putback are NOT here — they live in
/// <see cref="RollHConfig"/>'s putback pie slot (Roll H is where a shot resolves);
/// this config only sizes how OFTEN a board becomes a putback attempt.</para>
/// </summary>
public sealed class RollKConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven generator
    //     will replace these). The seven sum to 1. ---
    public double PutBack { get; set; } = 0.40;
    public double JumpBall { get; set; } = 0.01;
    public double DefensiveFoul { get; set; } = 0.05;
    public double OffensiveFoul { get; set; } = 0.02;
    public double DeadBallTurnover { get; set; } = 0.03;
    public double LiveBallTurnover { get; set; } = 0.02;
    public double ResetOffense { get; set; } = 0.47;

    // --- Free-throw-source weights (placeholders; seeded CONSERVATIVE, Emmett's to
    //     tune). The SECOND weight set, a clean sibling to the live-ball set above —
    //     selected when an offensive board arrives stamped
    //     <see cref="OffensiveReboundSource.FreeThrow"/> (Roll M). Off a missed FREE
    //     THROW the offense is right under the rim, so MORE putback and LESS
    //     kick-it-out-and-reset than off a live field-goal board. The other five arms
    //     carry the same rare-event weights. The seven sum to 1. ---
    public double FreeThrowPutBack { get; set; } = 0.55;
    public double FreeThrowJumpBall { get; set; } = 0.01;
    public double FreeThrowDefensiveFoul { get; set; } = 0.05;
    public double FreeThrowOffensiveFoul { get; set; } = 0.02;
    public double FreeThrowDeadBallTurnover { get; set; } = 0.03;
    public double FreeThrowLiveBallTurnover { get; set; } = 0.02;
    public double FreeThrowResetOffense { get; set; } = 0.32;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollKConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollK");
        var cfg = JsonSerializer.Deserialize<RollKConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollK config at {path}.");
    }
}
