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
    public double BaseBadPassDeadBall { get; set; } = 0.24;
    public double BaseBadPassIntercepted { get; set; } = 0.18;
    public double BaseLostBallDeadBall { get; set; } = 0.14;
    public double BaseLostBallLiveBall { get; set; } = 0.16;
    public double BaseOffensiveFoul { get; set; } = 0.09;

    // --- Contextification #6: the expanded loss set is now LIVE in the Halfcourt
    //     context — the realistic ways a SET possession dies (a travel, an over-and-
    //     back, a shot-clock violation, etc.). This governs EVERY halfcourt turnover
    //     (Roll A's frontcourt re-inbound, Roll B's halfcourt loss, Roll F's player
    //     action) — correct: a travel is a travel whoever caused it. The two
    //     backcourt-only violations (5-second inbound, 10-second backcourt) stay 0.0
    //     here — they cannot happen once the ball is across. Placeholder weights, the
    //     blessed 24/18/16/14/9 main + 8/2.5/2.5/2/1.5/1.5/0.5/0.5 minor shape; tuned
    //     later like every pie. ---
    public double BaseTravel { get; set; } = 0.08;
    public double BaseDoubleDribble { get; set; } = 0.015;
    public double BaseCarry { get; set; } = 0.015;
    public double BaseThreeSecondViolation { get; set; } = 0.025;
    public double BaseFiveSecondCloselyGuarded { get; set; } = 0.005;
    public double BaseOffensiveGoaltending { get; set; } = 0.005;
    public double BaseBackcourtViolation { get; set; } = 0.02;
    public double BaseShotClockViolation { get; set; } = 0.025;
    public double BaseFiveSecondInbound { get; set; } = 0.0;
    public double BaseTenSecondBackcourt { get; set; } = 0.0;

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

    // --- Contextification #5a: the expanded loss set, DORMANT in the Transition
    //     context too — every new type is 0.0 here, so the Transition pie stays
    //     the unchanged 25/15/20/35/05. (The transition outlet/push is a live-ball
    //     phase; which new types belong here, if any, is a #5b/tuning question.) ---
    public double TransitionTravel { get; set; } = 0.0;
    public double TransitionDoubleDribble { get; set; } = 0.0;
    public double TransitionCarry { get; set; } = 0.0;
    public double TransitionThreeSecondViolation { get; set; } = 0.0;
    public double TransitionFiveSecondCloselyGuarded { get; set; } = 0.0;
    public double TransitionOffensiveGoaltending { get; set; } = 0.0;
    public double TransitionBackcourtViolation { get; set; } = 0.0;
    public double TransitionShotClockViolation { get; set; } = 0.0;
    public double TransitionFiveSecondInbound { get; set; } = 0.0;
    public double TransitionTenSecondBackcourt { get; set; } = 0.0;

    // --- Contextification #5a: the Entry/Backcourt context (NEW, DORMANT — nothing
    //     routes here this session). This is the only context that gives the new
    //     types real weight; it is exercised solely by the isolation check until
    //     #5b stamps it on Roll A's loss exit. The phase-appropriate losses carry
    //     weight (bad pass / lost ball on the way up, plus the three backcourt-only
    //     violations); the halfcourt-only types (travel, over-and-back, 3-second,
    //     carry, closely-guarded, offensive goaltending) and the offensive foul are
    //     0.0 here. Placeholder values, sum to 1; tuned in #5b like every pie. ---
    public double EntryBackcourtBadPassDeadBall { get; set; } = 0.20;
    public double EntryBackcourtBadPassIntercepted { get; set; } = 0.15;
    public double EntryBackcourtLostBallDeadBall { get; set; } = 0.15;
    public double EntryBackcourtLostBallLiveBall { get; set; } = 0.15;
    public double EntryBackcourtOffensiveFoul { get; set; } = 0.0;
    public double EntryBackcourtTravel { get; set; } = 0.0;
    public double EntryBackcourtDoubleDribble { get; set; } = 0.0;
    public double EntryBackcourtCarry { get; set; } = 0.0;
    public double EntryBackcourtThreeSecondViolation { get; set; } = 0.0;
    public double EntryBackcourtFiveSecondCloselyGuarded { get; set; } = 0.0;
    public double EntryBackcourtOffensiveGoaltending { get; set; } = 0.0;
    public double EntryBackcourtBackcourtViolation { get; set; } = 0.0;
    public double EntryBackcourtShotClockViolation { get; set; } = 0.10;
    public double EntryBackcourtFiveSecondInbound { get; set; } = 0.10;
    public double EntryBackcourtTenSecondBackcourt { get; set; } = 0.15;

    // --- Contextification #5a: invariant elapsed for Roll C's three violation arms
    //     (the only Roll C arms that stamp time; turnovers defer to the future time
    //     roll). DORMANT copies of Roll A's values — #5b consolidates Roll A's
    //     violation terminals into Roll C and removes the duplication. ---
    public double ShotClockViolationElapsedSeconds { get; set; } = 30.0;
    public double FiveSecondInboundElapsedSeconds { get; set; } = 0.0;
    public double TenSecondBackcourtElapsedSeconds { get; set; } = 10.0;

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
