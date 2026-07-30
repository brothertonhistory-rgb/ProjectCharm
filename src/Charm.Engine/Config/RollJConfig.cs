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

    // --- Phase 28: steal-origin split. Two new weight sets replacing the single
    //     Steal set for classified steal tickets. The old Steal* set remains as a
    //     null-origin fallback (legacy tickets and isolated harness checks).
    //
    //     Required direction: BackcourtVictimPush > FrontcourtVictimPush >= Rebound.Push.
    //     BackcourtVictim: victim in backcourt → thief near basket → HIGH run.
    //     FrontcourtVictim: victim in halfcourt set → thief goes full court → LOW run.
    //     Both ≥ Rebound baseline (a steal still runs more than a board).
    //     Combined behavior near old Steal baseline (0.50) — not a strict midpoint. ---
    public double BackcourtVictimSettle { get; set; } = 0.35;
    public double BackcourtVictimPush { get; set; } = 0.55;
    public double BackcourtVictimTurnover { get; set; } = 0.06;
    public double BackcourtVictimDefensiveFoul { get; set; } = 0.035;
    public double BackcourtVictimJumpBall { get; set; } = 0.005;

    public double FrontcourtVictimSettle { get; set; } = 0.55;
    public double FrontcourtVictimPush { get; set; } = 0.35;
    public double FrontcourtVictimTurnover { get; set; } = 0.06;
    public double FrontcourtVictimDefensiveFoul { get; set; } = 0.035;
    public double FrontcourtVictimJumpBall { get; set; } = 0.005;

    // --- Phase 28: real-generator modifier seams. Two INDEPENDENT tilts on the
    //     Push/Settle balance; never pre-fused (each contributes its own additive
    //     delta, applied to Push and subtracted from Settle before the Pie clamp).
    //
    //     TeamPaceBias — signed fallback scalar (not CoachProfile, not Team, not Player).
    //     Default 0.0 = neutral. Only used when TransitionContext.OffenseSide is null
    //     (isolated harness checks without a stamped game context). Phase 30 live:
    //     real coaching source reads CoachProfile.PaceBias via GameState.CoachFor.
    //     Invariant: no enforced sign restriction (both directions are valid).
    //
    //     S86 RETIRED both of the old additive lifts. PaceScale is gone because pace is
    //     no longer a nudge — it is the BAR the players' opportunity must clear (ruling
    //     C-32/R1), and keeping both would let pace pay twice. AthleticismGapScale is
    //     gone because the five-way athleticism composite has been replaced by the SPEED
    //     race specifically (ruling R4), and Speed sits inside that composite
    //     (Player.Athleticism), so keeping both would double-count fast teams. ---
    public double TeamPaceBias { get; set; } = 0.0;

    // --- S86: the transition OPPORTUNITY score and the coach BAR. Values are ported
    //     verbatim from the locked oracle header (tools/transition_opportunity_oracle.py,
    //     Emmett-approved 2026-07-30); this build does NOT retune them.
    //
    //     The shape, in one line:
    //       opportunity = EscapeWeight × escape(handlerSpeed, handlerPassing)
    //                   + RaceWeight   × ((matesSpeed − defSpeed)/100 + RaceCenter)
    //       margin      = opportunity − bar(paceBias)
    //       bar(pace)   = BarBase − ((pace − 5)/5) × BarPaceSwing
    //       transfer    = PushSwing × tanh(margin / MarginScale)   (then bounded)
    //
    //     OverlapCredit — R2: the rebounder's two escape routes OVERLAP. His better route
    //     counts fully; his second counts a third as much, and the pair is renormalised by
    //     (1 + OverlapCredit) so elite-at-both beats elite-at-one by a couple of points of
    //     push rather than double. Must be in [0,1).
    //
    //     EscapeWeight / RaceWeight — the rebounder-dominant blend (ruled: ball-handler
    //     first, teammates second). MUST sum to exactly 1: they are shares of one score,
    //     not independent scales, so a pair that does not sum to 1 silently rescales the
    //     whole opportunity axis against the bar.
    //
    //     RaceCenter — an even race contributes its half, which keeps opportunity inside
    //     roughly [0,1] so BarBase reads as a comparable number.
    //
    //     BarBase / BarPaceSwing — R1/R5: the coach is the GATE. BarBase is tuned so a
    //     league-average team at neutral pace lands on S85's realised 33.5% push. The swing
    //     gives grind 0.535 / neutral 0.475 / run 0.395. This pair is the SINGLE lever a
    //     future coaching brain moves (O-57 era); player math and coach bar are never
    //     pre-fused.
    //
    //     PushSwing — the maximum settle↔push transfer before the bounded clamp. ZERO IS
    //     VALID and is the kill switch: at 0 every margin produces no transfer and all five
    //     sources reproduce their configured base weights (the Phase 75 pattern).
    //
    //     MarginScale — tanh softness. A comfortable clear runs; a bad miss almost never
    //     does. Must be strictly positive (it is a denominator). ---
    public double OverlapCredit { get; set; } = 0.35;
    public double EscapeWeight { get; set; } = 0.55;
    public double RaceWeight { get; set; } = 0.45;
    public double RaceCenter { get; set; } = 0.50;
    public double BarBase { get; set; } = 0.475;
    public double BarPaceSwing { get; set; } = 0.10;
    public double PushSwing { get; set; } = 0.22;
    public double MarginScale { get; set; } = 0.16;

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
        if (cfg is null)
            throw new InvalidOperationException($"Could not parse RollJ config at {path}.");

        // Phase 28: direction invariants for the steal-origin split.
        // BackcourtVictimPush > FrontcourtVictimPush and both >= Rebound Push.
        if (cfg.BackcourtVictimPush <= cfg.FrontcourtVictimPush)
            throw new InvalidOperationException(
                $"RollJ BackcourtVictimPush ({cfg.BackcourtVictimPush}) must be > FrontcourtVictimPush ({cfg.FrontcourtVictimPush}).");
        if (cfg.FrontcourtVictimPush < cfg.Push)
            throw new InvalidOperationException(
                $"RollJ FrontcourtVictimPush ({cfg.FrontcourtVictimPush}) must be >= Rebound Push ({cfg.Push}).");

        // S86 guards (B6). Each one catches a settings-file edit that would silently
        // change the SHAPE of the opportunity score rather than merely mistune it.
        if (cfg.OverlapCredit < 0.0 || cfg.OverlapCredit >= 1.0)
            throw new InvalidOperationException(
                $"RollJ OverlapCredit must be in [0,1) (got {cfg.OverlapCredit}). At 1 the two " +
                "escape routes would count equally, which is the pre-fused form R2 rejects.");
        if (Math.Abs(cfg.EscapeWeight + cfg.RaceWeight - 1.0) > 1e-12)
            throw new InvalidOperationException(
                $"RollJ EscapeWeight ({cfg.EscapeWeight}) + RaceWeight ({cfg.RaceWeight}) must " +
                "equal 1 — they are shares of ONE opportunity score, not independent scales. A " +
                "pair that does not sum to 1 rescales the whole score against a fixed bar.");
        // PushSwing == 0 is VALID and is the kill switch (base weights everywhere).
        if (cfg.PushSwing < 0.0)
            throw new InvalidOperationException(
                $"RollJ PushSwing must be >= 0 (got {cfg.PushSwing}). Zero is valid and is the " +
                "kill switch; negative would invert every ruling in C-32.");
        if (cfg.MarginScale <= 0.0)
            throw new InvalidOperationException(
                $"RollJ MarginScale must be > 0 (got {cfg.MarginScale}) — it is the tanh denominator.");
        if (cfg.BarPaceSwing < 0.0)
            throw new InvalidOperationException(
                $"RollJ BarPaceSwing must be >= 0 (got {cfg.BarPaceSwing}). Negative would make a " +
                "run-and-gun coach RAISE the bar.");

        return cfg;
    }
}
