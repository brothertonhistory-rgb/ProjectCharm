using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for the thin Governor lives here — nothing is hardcoded in
/// logic. Loaded from the "Governor" section of config.json, exactly like the per-roll
/// configs.
///
/// <para>Both values are PROVISIONAL guts of the thin Governor (see design.md's
/// teardown contract):</para>
/// <list type="bullet">
///   <item><see cref="PossessionCap"/> is the stop rule — a flat possession count,
///   NOT a real clock. The real stop condition (clock expiry, overtime) replaces it
///   later.</item>
///   <item><see cref="SecondsPerPossession"/> is a flat placeholder drained per
///   possession for observability only. The real per-possession time comes from a
///   future time roll; this number is the "score = 0" of the clock.</item>
/// </list>
/// </summary>
public sealed class GovernorConfig
{
    /// <summary>How many possessions the Governor plays before stopping. The thin
    /// Governor's entire stop rule (a real clock is future work). A full college game
    /// is roughly 130–145 possessions across both teams; 200 over-covers it so the
    /// bonus is comfortably crossed mid-loop and the accumulation invariants are
    /// exercised.</summary>
    public int PossessionCap { get; set; } = 200;

    /// <summary>Flat placeholder seconds drained per possession (observability only;
    /// not written to any clock — there is no clock field yet). Where a terminal
    /// already carries an invariant <c>ElapsedSeconds</c> (a shot-clock or backcourt
    /// violation), that real value is used instead of this placeholder.</summary>
    public double SecondsPerPossession { get; set; } = 18.0;

    public static GovernorConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Governor");
        var cfg = JsonSerializer.Deserialize<GovernorConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse Governor config at {path}.");
    }
}
