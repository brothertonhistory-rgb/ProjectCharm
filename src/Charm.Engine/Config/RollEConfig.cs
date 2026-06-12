using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Roll E's tunable numbers, loaded from the <c>"RollE"</c> section of
/// <c>config.json</c>. Mirrors <c>RollCConfig</c> / <c>RollDConfig</c>: a plain
/// settings record with a <see cref="Load"/> that reads its own section.
///
/// The five base weights are FLAT (0.20 each) this session — the explicit,
/// visible, tunable expression of "no signal yet." They are written out one per
/// slot (rather than computed as 1/5) so the seam is real: the person can see and
/// change each weight, and a future attribute-driven generator overwrites these
/// numbers without any "uniform mode" flag to flip.
///
/// There is NO live-wire scalar here (unlike Roll B's physicality and Roll C's
/// pressure). Selection's first real signal is usage, which is part of the
/// deferred attribute model — so, like Roll D's flavor generator, there is
/// nothing functional for a signal to move yet, and adding one would falsely
/// imply a signal exists.
/// </summary>
public sealed class RollEConfig
{
    public double BaseSlot1 { get; init; }
    public double BaseSlot2 { get; init; }
    public double BaseSlot3 { get; init; }
    public double BaseSlot4 { get; init; }
    public double BaseSlot5 { get; init; }
    public double Epsilon { get; init; }

    /// <summary>Load the <c>"RollE"</c> section from the config file at
    /// <paramref name="path"/>. Mirrors the other rolls' loaders.</summary>
    public static RollEConfig Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var e = doc.RootElement.GetProperty("RollE");

        return new RollEConfig
        {
            BaseSlot1 = e.GetProperty("BaseSlot1").GetDouble(),
            BaseSlot2 = e.GetProperty("BaseSlot2").GetDouble(),
            BaseSlot3 = e.GetProperty("BaseSlot3").GetDouble(),
            BaseSlot4 = e.GetProperty("BaseSlot4").GetDouble(),
            BaseSlot5 = e.GetProperty("BaseSlot5").GetDouble(),
            Epsilon = e.GetProperty("Epsilon").GetDouble(),
        };
    }
}
