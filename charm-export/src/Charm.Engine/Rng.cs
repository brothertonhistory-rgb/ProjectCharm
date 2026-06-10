namespace Charm.Engine;

/// <summary>
/// Source of randomness for rolls. Abstracted so batch runs and observability
/// are reproducible (seed in, same sequence out) and so the real engine can
/// later swap in a different generator without touching any roll.
/// </summary>
public interface IRng
{
    /// <summary>A uniform draw in [0, 1).</summary>
    double NextUnitInterval();
}

/// <summary>Seedable RNG backed by System.Random.</summary>
public sealed class SystemRng : IRng
{
    private readonly Random _random;

    public SystemRng(int seed) => _random = new Random(seed);

    public double NextUnitInterval() => _random.NextDouble();
}
