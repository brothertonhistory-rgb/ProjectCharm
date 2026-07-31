using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll D lives here — nothing is hardcoded in logic.
/// Loaded from the "RollD" section of config.json. Covers both the flavor pie
/// weights (descriptive theater) and the bonus thresholds (functional routing).
/// </summary>
public sealed class RollDConfig
{
    // --- Flavor pie weights (descriptive/observability only; they do NOT route).
    //     Placeholders; a real attribute-driven generator will replace these.
    //     Kept summing to 1 for clarity. ---
    public double FlavorReachIn { get; set; } = 0.60;
    public double FlavorBlocking { get; set; } = 0.30;
    public double FlavorOffBall { get; set; } = 0.10;

    // --- Bonus thresholds (functional: they decide the route). NCAA classic:
    //     7th team foul -> 1-and-1; 10th -> double bonus. Tunable. ---

    /// <summary>Team-foul count at which the opponent enters the 1-and-1 bonus.</summary>
    public int BonusThreshold { get; set; } = 7;

    /// <summary>Team-foul count at which the opponent enters the double bonus.</summary>
    public int DoubleBonusThreshold { get; set; } = 10;

    // --- S87: the PERSONAL-foul disqualification threshold. Lives beside the bonus
    //     thresholds because they are the same kind of number — a foul count that
    //     changes what the game does — even though they run on different clocks
    //     (team fouls reset at the half; personal fouls are game-scoped). ---

    /// <summary>Personal-foul count at which a player is disqualified. NCAA classic: 5.
    /// Validated at <see cref="PersonalFoulTracker"/> construction (must be at least 1).
    /// Setting this very large is the INERT MODE used by the regression check — no
    /// separate flag: nobody reaches the threshold, so nothing is ever replaced and the
    /// game replays exactly as it did before S87.</summary>
    public int FoulOutThreshold { get; set; } = PersonalFoulTracker.DefaultFoulOutThreshold;

    /// <summary>Tolerance for the flavor-pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollDConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollD");
        var cfg = JsonSerializer.Deserialize<RollDConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollD config at {path}.");
    }
}
