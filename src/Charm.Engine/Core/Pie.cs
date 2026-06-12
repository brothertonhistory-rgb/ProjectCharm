namespace Charm.Engine;

/// <summary>Thrown when a pie is malformed. Fails loud at the generator->roll
/// seam so a future buggy generator can never silently roll garbage.</summary>
public sealed class PieValidationException : Exception
{
    public PieValidationException(string message) : base(message) { }
}

/// <summary>
/// A finished set of weighted odds over the outcomes of one roll. The uniform
/// currency every roll consumes: a roll *receives* a pie and rolls against it;
/// it never computes its own odds.
///
/// Validation happens on construction. Because a Pie cannot exist unless it is
/// valid, "validate on receipt" is enforced by the type itself — a roll holding
/// a Pie is holding a valid one. The buggy-generator failure surfaces at the
/// moment of construction, which is exactly the generator->roll handoff.
/// </summary>
public sealed class Pie<TOutcome> where TOutcome : struct, Enum
{
    private readonly (TOutcome Outcome, double Weight)[] _slices;

    public IReadOnlyList<(TOutcome Outcome, double Weight)> Slices => _slices;

    /// <param name="weights">Weight per outcome. Must cover every slice, be
    /// non-negative, and sum to 1 within <paramref name="epsilon"/>.</param>
    /// <param name="epsilon">Tolerance for the sum-to-one check.</param>
    public Pie(IReadOnlyDictionary<TOutcome, double> weights, double epsilon)
    {
        if (weights is null || weights.Count == 0)
            throw new PieValidationException("Pie has no slices.");

        // Fix a stable order = enum declaration order, so a given draw always
        // maps to the same outcome regardless of dictionary iteration order.
        var ordered = Enum.GetValues<TOutcome>();
        var built = new List<(TOutcome, double)>(ordered.Length);
        double sum = 0;

        foreach (var outcome in ordered)
        {
            if (!weights.TryGetValue(outcome, out var w))
                throw new PieValidationException($"Pie is missing a weight for '{outcome}'.");
            if (double.IsNaN(w) || double.IsInfinity(w))
                throw new PieValidationException($"Pie weight for '{outcome}' is not a finite number ({w}).");
            if (w < 0)
                throw new PieValidationException($"Pie weight for '{outcome}' is negative ({w}).");

            built.Add((outcome, w));
            sum += w;
        }

        if (Math.Abs(sum - 1.0) > epsilon)
            throw new PieValidationException($"Pie weights sum to {sum:R}, not 1 (epsilon {epsilon:R}).");

        _slices = built.ToArray();
    }

    /// <summary>Map a uniform draw u in [0, 1) to an outcome by cumulative walk.</summary>
    public TOutcome Roll(double u)
    {
        if (u < 0 || u >= 1)
            throw new ArgumentOutOfRangeException(nameof(u), u, "Draw must be in [0, 1).");

        double cumulative = 0;
        foreach (var (outcome, weight) in _slices)
        {
            cumulative += weight;
            if (u < cumulative)
                return outcome;
        }

        // Reached only by floating-point drift at the very top of the range.
        return _slices[^1].Outcome;
    }

    public override string ToString() =>
        string.Join(", ", _slices.Select(s => $"{s.Outcome}={s.Weight:P2}"));
}
