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

    // --- Phase 32: putback attempt rate floor and ceiling.
    //     The tanh tilt asymptotes toward these without crossing.
    //     floor >= 0, ceiling <= 1.0, floor < ceiling (enforced in Load).
    //     Calibration placeholders — direction is what matters now. ---

    /// <summary>The minimum putback weight the tilt can reach (defense-dominant
    /// asymptote). Must be &gt;= 0 and &lt; PutbackCeiling (enforced in Load).
    /// Calibration placeholder.</summary>
    public double PutbackFloor   { get; set; } = 0.15;

    /// <summary>The maximum putback weight the tilt can reach (offense-dominant
    /// asymptote). Must be &lt;= 1.0 and &gt; PutbackFloor (enforced in Load).
    /// Calibration placeholder.</summary>
    public double PutbackCeiling { get; set; } = 0.70;

    public static RollKConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollK");
        var cfg = JsonSerializer.Deserialize<RollKConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not parse RollK config at {path}.");

        // Phase 32: floor/ceiling invariants
        if (cfg.PutbackFloor < 0.0)
            throw new InvalidOperationException(
                $"PutbackFloor must be >= 0: got {cfg.PutbackFloor}.");
        if (cfg.PutbackCeiling > 1.0)
            throw new InvalidOperationException(
                $"PutbackCeiling must be <= 1.0: got {cfg.PutbackCeiling}.");
        if (cfg.PutbackFloor >= cfg.PutbackCeiling)
            throw new InvalidOperationException(
                $"PutbackFloor must be < PutbackCeiling: floor={cfg.PutbackFloor}, ceiling={cfg.PutbackCeiling}.");

        // Phase 32: startup overflow guards — the ceiling plus the five flat arms
        // must leave room for a non-negative ResetOffense in both source modes.
        var liveFlatTotal = cfg.JumpBall + cfg.DefensiveFoul + cfg.OffensiveFoul
                          + cfg.DeadBallTurnover + cfg.LiveBallTurnover;
        if (cfg.PutbackCeiling + liveFlatTotal >= 1.0)
            throw new InvalidOperationException(
                $"LiveBall: PutbackCeiling ({cfg.PutbackCeiling:F4}) + flat arms ({liveFlatTotal:F4}) " +
                $">= 1.0 — no room for ResetOffense. Reduce PutbackCeiling or flat arm weights.");

        var ftFlatTotal = cfg.FreeThrowJumpBall + cfg.FreeThrowDefensiveFoul + cfg.FreeThrowOffensiveFoul
                        + cfg.FreeThrowDeadBallTurnover + cfg.FreeThrowLiveBallTurnover;
        if (cfg.PutbackCeiling + ftFlatTotal >= 1.0)
            throw new InvalidOperationException(
                $"FreeThrow: PutbackCeiling ({cfg.PutbackCeiling:F4}) + flat arms ({ftFlatTotal:F4}) " +
                $">= 1.0 — no room for FreeThrowResetOffense. Reduce PutbackCeiling or FreeThrow flat arm weights.");

        return cfg;
    }
}
