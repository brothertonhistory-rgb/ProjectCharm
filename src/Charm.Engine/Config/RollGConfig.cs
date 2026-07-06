using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll G lives here — nothing is hardcoded in logic.
/// Loaded from the "RollG" section of config.json. The five base weights are
/// realistic PLACEHOLDERS (roughly real D1 attempt shares); the real
/// attribute-driven generator will replace them without touching Roll G or the
/// resolver.
/// </summary>
public sealed class RollGConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). Kept summing to 1 for clarity. ---
    public double BaseThree { get; set; } = 0.36;
    public double BaseLong { get; set; } = 0.08;
    public double BaseMid { get; set; } = 0.10;
    public double BaseShort { get; set; } = 0.11;
    public double BaseRim { get; set; } = 0.35;

    // No live-wire scalar (like Roll E and Roll F): the only thing that would tilt
    // Roll G's pie is the deferred player/attribute model (shot selection, role,
    // defensive pressure). Inventing a placeholder wire here would pantomime the
    // exact signal that is deliberately deferred. Ships flat-ish; the real
    // generator drops in later.

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    // --- Fast-break shot location pie (Phase 16 base; Session 38 bend). These five are
    //     now the BASE diet for a transition possession — a modern rim-heavy break with a
    //     real three share and long twos nearly gone. The base is BENT per shooter at
    //     runtime (RollGGenerator.DeriveFastBreakPie): the shooter's own stored neutral
    //     tendencies pull it zone-by-zone and the coach's PaceBias tilts the three share.
    //     The null-shooter fallback uses this base flat (unbent). All five are calibration
    //     placeholders summing to 1.0, pinned only by golden parity at the approved state. ---
    public double FastBreakRim   { get; set; } = 0.57;
    public double FastBreakShort { get; set; } = 0.08;
    public double FastBreakMid   { get; set; } = 0.03;
    public double FastBreakLong  { get; set; } = 0.02;
    public double FastBreakThree { get; set; } = 0.30;

    // --- Fast-break shooter-bend knobs (Session 38). The base diet above is pulled toward
    //     the shooter's own neutral tendencies via an identity-relative ratio, then the
    //     three share is tilted by the coach's PaceBias. All calibration placeholders,
    //     pinned by the golden fixture (tools/fastbreak_golden.json) — later tuning follows
    //     the oracle-first flow (new oracle calibration → regenerate fixture → sync
    //     defaults + config → parity stays green), never config-only.
    //
    //     FastBreakShooterPull (Beta) — pull strength. 0 = pure base for everyone; 1 = full
    //     ratio. The ratio (shooterShare / mean) ^ Beta bends each zone.
    //     FastBreakRatioCapLow / High — clamp on that ratio, guarding the extremes so a
    //     pure shooter cannot explode his corner three past the cap and a non-shooter's
    //     three cannot be floored below CapLow.
    //     FastBreakPaceTilt — three-share tilt per PaceBias point off 5. Run-and-gun (>5)
    //     raises transition threes; grind-it-out (<5) trims them. ShotSelectionBias is NOT
    //     read on the fast-break path (deferred to the future coach-philosophy layer).
    //     FastBreakMean{Zone} — the league mean neutral diet, PINNED to the tendency
    //     oracle's population diagnostic (Rim 33.5 / Short 15.5 / Mid 9.8 / Long 5.8 /
    //     Three 35.5%). This is the DENOMINATOR of the identity ratio, not a free knob. ---
    public double FastBreakShooterPull   { get; set; } = 0.70;
    public double FastBreakRatioCapLow   { get; set; } = 0.15;
    public double FastBreakRatioCapHigh  { get; set; } = 2.2;
    public double FastBreakPaceTilt      { get; set; } = 0.035;
    public double FastBreakMeanRim   { get; set; } = 0.335;
    public double FastBreakMeanShort { get; set; } = 0.155;
    public double FastBreakMeanMid   { get; set; } = 0.098;
    public double FastBreakMeanLong  { get; set; } = 0.058;
    public double FastBreakMeanThree { get; set; } = 0.355;

    // --- Usage-pressure diet-shift dials (Phase 17). Control how far Roll G
    //     bends the shot diet when a shooter is carrying above an equal load.
    //
    //     PressureShiftScale — requestedShift = pressure × scale. At 0.5, a
    //     player at the rail (~0.32 pressure) requests a 0.16 shift in his shot
    //     diet. Calibration placeholder; 0 = ablation (no shift, no residual).
    //
    //     PressureShiftCapFraction — the fraction of the bent-dominant zone's
    //     mass that may be moved off in one possession. Prevents the diet from
    //     being fully emptied even under maximum load. Default 0.8 (80%).
    //     Invariant > 0 and ≤ 1. ---
    public double PressureShiftScale        { get; set; } = 0.5;
    public double PressureShiftCapFraction  { get; set; } = 0.8;

    // --- Attention-location tilt (Phase 28). Amplifies the requested diet-shift
    //     magnitude when the selected shooter is carrying ABOVE-EQUAL defensive
    //     attention (attention > EqualShare = 0.20). The amplifier scales
    //     requestedShift BEFORE the intrinsicCapacity cap, so a one-trick player's
    //     larger amplified request spills to residual rather than being clamped away.
    //
    //     AttentionShiftAmplifier — scale factor for the attention-pressure term.
    //     amplifier = 1 + max(0, ShooterAttentionShare - 0.20) * AttentionShiftAmplifier.
    //     At equal/below-share attention: attentionPressure = 0 → amplifier = ×1 →
    //     Phase 17 diet-shift unchanged (regression anchor).
    //     Bonus-only: attention below EqualShare can never REDUCE the shift.
    //     0 = ablation (attention has no location effect). Default: 1.0 placeholder. ---
    public double AttentionShiftAmplifier   { get; set; } = 1.0;

    public static RollGConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollG");
        var cfg = JsonSerializer.Deserialize<RollGConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (cfg is null)
            throw new InvalidOperationException($"Could not parse RollG config at {path}.");

        const double Eps = 1e-9;

        // Phase 16 invariants: each FastBreak weight must be non-negative and
        // the five must sum to 1.0 — same pattern as MatchupConfig block-contest weights.
        if (cfg.FastBreakRim   < 0 || cfg.FastBreakShort < 0 || cfg.FastBreakMid   < 0 ||
            cfg.FastBreakLong  < 0 || cfg.FastBreakThree < 0)
            throw new InvalidOperationException(
                "All RollG FastBreak location weights must be >= 0.");

        var fastBreakSum = cfg.FastBreakRim + cfg.FastBreakShort + cfg.FastBreakMid
                         + cfg.FastBreakLong + cfg.FastBreakThree;
        if (Math.Abs(fastBreakSum - 1.0) > Eps)
            throw new InvalidOperationException(
                $"RollG FastBreak location weights must sum to 1.0: sum={fastBreakSum}.");

        // Phase 17 invariants: shift scale non-negative; cap fraction in (0, 1].
        if (cfg.PressureShiftScale < 0)
            throw new InvalidOperationException(
                "RollG PressureShiftScale must be >= 0 (0 = no diet shift, ablation-friendly).");
        if (cfg.PressureShiftCapFraction <= 0 || cfg.PressureShiftCapFraction > 1.0)
            throw new InvalidOperationException(
                $"RollG PressureShiftCapFraction must be > 0 and <= 1 (got {cfg.PressureShiftCapFraction}).");

        // Phase 28: AttentionShiftAmplifier >= 0 (0 = ablation, no location effect from attention).
        if (cfg.AttentionShiftAmplifier < 0)
            throw new InvalidOperationException(
                "RollG AttentionShiftAmplifier must be >= 0 (0 = ablation-friendly: attention has no location effect).");

        // Session 38: fast-break shooter-bend invariants.
        if (cfg.FastBreakShooterPull < 0)
            throw new InvalidOperationException(
                "RollG FastBreakShooterPull must be >= 0 (0 = pure base, no shooter bend).");
        if (cfg.FastBreakRatioCapLow <= 0 || cfg.FastBreakRatioCapLow > 1.0)
            throw new InvalidOperationException(
                $"RollG FastBreakRatioCapLow must be in (0, 1] (got {cfg.FastBreakRatioCapLow}).");
        if (cfg.FastBreakRatioCapHigh < 1.0)
            throw new InvalidOperationException(
                $"RollG FastBreakRatioCapHigh must be >= 1 (got {cfg.FastBreakRatioCapHigh}).");
        if (cfg.FastBreakMeanRim   <= 0 || cfg.FastBreakMeanRim   >= 1 ||
            cfg.FastBreakMeanShort <= 0 || cfg.FastBreakMeanShort >= 1 ||
            cfg.FastBreakMeanMid   <= 0 || cfg.FastBreakMeanMid   >= 1 ||
            cfg.FastBreakMeanLong  <= 0 || cfg.FastBreakMeanLong  >= 1 ||
            cfg.FastBreakMeanThree <= 0 || cfg.FastBreakMeanThree >= 1)
            throw new InvalidOperationException(
                "RollG FastBreakMean{Rim,Short,Mid,Long,Three} must each be in (0, 1).");
        // PaceTilt >= 0 AND strictly < 0.25 so the three-share multiplier
        // 1 + tilt*(PaceBias - 5) stays strictly positive at the worst case (PaceBias = 1 →
        // 1 - 4*tilt). CoachProfile.ValidateBias caps PaceBias at [1, 10]; a non-positive
        // pre-normalization three weight would not be a legitimate pie.
        if (cfg.FastBreakPaceTilt < 0 || cfg.FastBreakPaceTilt >= 0.25)
            throw new InvalidOperationException(
                $"RollG FastBreakPaceTilt must be in [0, 0.25) (got {cfg.FastBreakPaceTilt}).");

        return cfg;
    }
}
