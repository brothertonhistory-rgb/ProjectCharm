using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll A lives here — nothing is hardcoded in logic.
/// Loaded from config.json so values can be edited without touching code.
/// </summary>
public sealed class RollAConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven
    //     generator will replace these). Must sum to 1 within Epsilon. ---
    public double BaseClean { get; set; } = 0.90;
    public double BaseTurnover { get; set; } = 0.08;
    public double BaseViolation { get; set; } = 0.02;

    /// <summary>The single live wire proving the seam carries signal: how much
    /// a pressure of 1.0 adds to the turnover weight before renormalization.
    /// Placeholder, not basketball logic.</summary>
    public double PressureTurnoverNudge { get; set; } = 0.10;

    /// <summary>Invariant elapsed time stamped on a shot-clock-violation
    /// terminal: the full shot clock (NCAA men's = 30s).</summary>
    public double ViolationElapsedSeconds { get; set; } = 30.0;

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
