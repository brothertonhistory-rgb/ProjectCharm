using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll A lives here — nothing is hardcoded in logic.
/// Loaded from config.json so values can be edited without touching code.
/// </summary>
public sealed class RollAConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). Kept summing to 1 for clarity. The five
    //     reshaped outcomes (Contextification #6): the three former violation
    //     terminals are GONE — their loss now resolves inside Roll C via the
    //     Turnover exit's EntryBackcourt context, so the old violation mass folded
    //     into BaseTurnover. The old single foul slice split into offensive vs.
    //     defensive (≈15 / 85 — entry fouls are overwhelmingly defensive reach-ins;
    //     the split scales with pressure in the real generator later). ---
    public double BaseClean { get; set; } = 0.88;
    public double BaseTurnover { get; set; } = 0.08;

    /// <summary>Weight for an OFFENSIVE foul on the entry (charge / illegal screen).
    /// ≈15% of the entry-foul slice; the rest is defensive. A placeholder that scales
    /// up with backcourt pressure in the real generator.</summary>
    public double BaseOffensiveFoul { get; set; } = 0.0045;

    /// <summary>Weight for a non-shooting DEFENSIVE foul on the entry (reach-in, grab).
    /// ≈85% of the entry-foul slice. Routes to Roll D (charge-and-fork).</summary>
    public double BaseDefensiveFoul { get; set; } = 0.0255;

    public double BaseJumpBall { get; set; } = 0.01;

    /// <summary>The single live wire proving the seam carries signal: how much
    /// a pressure of 1.0 adds to the turnover weight before renormalization.
    /// Placeholder, not basketball logic.</summary>
    public double PressureTurnoverNudge { get; set; } = 0.10;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    // --- Batch harness settings ---
    public int BatchSize { get; set; } = 100_000;

    /// <summary>Allowed absolute gap between an observed rate and its configured
    /// weight in the batch harness.</summary>
    public double RateTolerance { get; set; } = 0.005;

    public int Seed { get; set; } = 20260610;

    public static RollAConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<RollAConfig>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse config at {path}.");
    }
}
