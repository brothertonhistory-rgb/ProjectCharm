using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll L (free-throw resolution) lives here — nothing is
/// hardcoded in logic. Loaded from the "RollL" section of config.json. Mirrors the
/// flat-placeholder shape of <see cref="RollKConfig"/>: a single make% drives the
/// two-way pie, no live-wire scalar.
///
/// <para>The attribute seam here is unusually DIRECT, and worth stating: where
/// other rolls map attributes to odds through a skill/athleticism interaction, a
/// free throw is a LITERAL 1:1 — a 71-rated shooter makes 71% per spin, full stop.
/// So the real generator is the simplest in the engine: read the shooter's FT
/// rating, divide by 100, done. This config ships a flat placeholder make% (~.72,
/// roughly D1 average) until that generator and a named FT shooter exist.</para>
///
/// <para>The make probability carries NO context: it is identical whether the trip
/// came from an and-1, a fouled three, or a bonus foul. There is deliberately no
/// per-context make% here — modelling one would invent the very ticket the design
/// rejects.</para>
/// </summary>
public sealed class RollLConfig
{
    // --- Stub pie make% (placeholder; the real attribute-driven generator will
    //     read the shooter's FT rating straight through). The two slices are
    //     (Make = MakeProbability) and (Miss = 1 − MakeProbability), so they sum to
    //     1 by construction. ---
    public double MakeProbability { get; set; } = 0.72;

    // --- Road make-penalty (DOCUMENTED SEAM ONLY). A small negative modifier to the
    //     make% on the road, IF it ever proves a real statistical effect. Set to 0
    //     and NOT applied this session — the stub generator ignores it entirely. It
    //     lives here so the seam is named and a future pass can wire it without a
    //     config migration. ---
    public double RoadMakePenalty { get; set; } = 0.0;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollLConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollL");
        var cfg = JsonSerializer.Deserialize<RollLConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollL config at {path}.");
    }
}
