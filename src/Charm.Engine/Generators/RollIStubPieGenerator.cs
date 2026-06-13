namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll I (rebound / loose-ball resolution). Builds a flat
/// seven-way pie from config weights, selecting the weight set by the
/// <see cref="ReboundSource"/> the loose ball arrived with (LiveBall off a field-goal
/// miss, Block off a swatted shot — the latter keeps more with the defense, squirts
/// OOB more, recovers offensively more, and carries a minuscule jump-ball sliver). No
/// attribute model yet. The same ticket/station selection pattern Roll K uses for its
/// offensive-rebound source and Roll J uses for its transition source.
///
/// The real attribute-driven generator (which will tilt the offensive-rebound rate by
/// matchup, fatigue, etc.) replaces this without touching Roll I or the resolver. The
/// <see cref="Pie{TOutcome}"/> validates sum-to-one on construction, so any
/// misconfigured weights fail loudly here rather than silently warping odds.
/// </summary>
public sealed class RollIStubPieGenerator
{
    private readonly RollIConfig _config;

    public RollIStubPieGenerator(RollIConfig config) => _config = config;

    /// <summary>Generate the seven-way rebound pie for the arriving SOURCE. A null stamp
    /// (every legacy Roll H <c>Miss</c> feeder, and a missed putback re-entering Roll I)
    /// is mapped to <see cref="ReboundSource.LiveBall"/> by the resolver before calling,
    /// so the live-miss path is byte-for-byte unchanged; Roll H's <c>Blocked</c> arm
    /// stamps <see cref="ReboundSource.Block"/> for the block-recovery pie. The ROUTING
    /// in Roll I is identical for both — only the weights differ. The
    /// <see cref="Pie{TOutcome}"/> constructor walks the enum in declaration order, so
    /// slice order is fixed for reproducibility regardless of dictionary iteration
    /// order, and validates sum-to-one so any misconfigured weights fail loud here.</summary>
    /// <param name="source">Which loose ball this is — selects the weight set.</param>
    public Pie<ReboundOutcome> Generate(ReboundSource source)
    {
        var weights = source switch
        {
            ReboundSource.LiveBall => new Dictionary<ReboundOutcome, double>
            {
                [ReboundOutcome.DefensiveRebound]       = _config.DefensiveRebound,
                [ReboundOutcome.OffensiveRebound]       = _config.OffensiveRebound,
                [ReboundOutcome.LooseBallFoulOnDefense] = _config.LooseBallFoulOnDefense,
                [ReboundOutcome.LooseBallFoulOnOffense] = _config.LooseBallFoulOnOffense,
                [ReboundOutcome.OutOfBoundsOffOffense]  = _config.OutOfBoundsOffOffense,
                [ReboundOutcome.OutOfBoundsOffDefense]  = _config.OutOfBoundsOffDefense,
                [ReboundOutcome.JumpBall]               = _config.JumpBall,
            },

            ReboundSource.Block => new Dictionary<ReboundOutcome, double>
            {
                [ReboundOutcome.DefensiveRebound]       = _config.BlockDefensiveRebound,
                [ReboundOutcome.OffensiveRebound]       = _config.BlockOffensiveRebound,
                [ReboundOutcome.LooseBallFoulOnDefense] = _config.BlockLooseBallFoulOnDefense,
                [ReboundOutcome.LooseBallFoulOnOffense] = _config.BlockLooseBallFoulOnOffense,
                [ReboundOutcome.OutOfBoundsOffOffense]  = _config.BlockOutOfBoundsOffOffense,
                [ReboundOutcome.OutOfBoundsOffDefense]  = _config.BlockOutOfBoundsOffDefense,
                [ReboundOutcome.JumpBall]               = _config.BlockJumpBall,
            },

            _ => throw new InvalidOperationException(
                $"No Roll I pie for rebound source '{source}'.")
        };

        // The Pie constructor validates the sum is 1 within Epsilon, so any
        // misconfigured weights fail loud here rather than rolling skewed.
        return new Pie<ReboundOutcome>(weights, _config.Epsilon);
    }
}
