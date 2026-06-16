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

    // --- Fast-break shot location pie (Phase 16). A flat rim-heavy pie for
    //     press-break possessions; bypasses the tendency-matchup calculation
    //     entirely. The break dictates the shot, not the shooter's profile.
    //     All five are calibration placeholders summing to 1.0. ---
    public double FastBreakRim   { get; set; } = 0.70;
    public double FastBreakShort { get; set; } = 0.10;
    public double FastBreakMid   { get; set; } = 0.10;
    public double FastBreakLong  { get; set; } = 0.05;
    public double FastBreakThree { get; set; } = 0.05;

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

        return cfg;
    }
}
