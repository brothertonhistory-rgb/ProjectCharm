namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll L (free-throw resolution). Builds a flat two-way
/// make/miss pie directly from the config make% — no signal argument, no attribute
/// model. Structurally the simplest generator in the engine (cf.
/// <see cref="RollKStubPieGenerator"/>, which is flat but seven-way).
///
/// The real attribute-driven generator is the most DIRECT in the engine: a free
/// throw is a literal 1:1 from the shooter's FT rating (a 71-rated shooter →
/// Make = .71), with no skill/athleticism interaction to fold. It will read the
/// carried shooter slot and divide the rating by 100; the optional road penalty
/// (<see cref="RollLConfig.RoadMakePenalty"/>, currently 0 and unread) subtracts
/// from that. It replaces this stub without touching Roll L or the resolver. The
/// <see cref="Pie{TOutcome}"/> validates sum-to-one on construction, so any
/// misconfigured make% fails loudly here rather than silently warping odds.
/// </summary>
public sealed class RollLStubPieGenerator
{
    private readonly RollLConfig _config;

    public RollLStubPieGenerator(RollLConfig config) => _config = config;

    /// <summary>Generate the two-way free-throw pie from the flat config make%. No
    /// signal argument — the make% is the same for every trip. Miss is the
    /// complement, so the two slices sum to 1 by construction. The road penalty is a
    /// documented seam (0, unread) this session.</summary>
    public Pie<FreeThrowOutcome> Generate()
    {
        var make = _config.MakeProbability;

        var weights = new Dictionary<FreeThrowOutcome, double>
        {
            [FreeThrowOutcome.Make] = make,
            [FreeThrowOutcome.Miss] = 1.0 - make,
        };

        // The Pie constructor validates the sum is 1 within Epsilon, so a
        // misconfigured make% fails loud here rather than rolling skewed.
        return new Pie<FreeThrowOutcome>(weights, _config.Epsilon);
    }
}
