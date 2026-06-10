namespace Charm.Engine;

/// <summary>What the resolver did with one result — for observability/harness.</summary>
public readonly record struct RoutingOutcome(bool PossessionEnded, string Destination);

/// <summary>
/// The thing that walks the chain. It owns all routing: a Terminal ends the
/// possession; a Continue is mapped — by its <see cref="ContinuationKind"/>, the
/// only place that mapping lives — to the next node. When real nodes replace the
/// stubs, only this mapping changes; the rolls that emit results never reopen.
/// </summary>
public sealed class Resolver
{
    private readonly IContinuationNode _intoHalfcourtSet;
    private readonly IContinuationNode _resolveTurnoverType;

    public Resolver(IContinuationNode intoHalfcourtSet, IContinuationNode resolveTurnoverType)
    {
        _intoHalfcourtSet = intoHalfcourtSet;
        _resolveTurnoverType = resolveTurnoverType;
    }

    public RoutingOutcome Route(RollResult result) => result switch
    {
        Terminal t => new RoutingOutcome(PossessionEnded: true, Destination: $"END:{t.Reason}"),

        Continue c => c.Next switch
        {
            ContinuationKind.IntoHalfcourtSet =>
                new RoutingOutcome(false, _intoHalfcourtSet.Receive(c)),
            ContinuationKind.ResolveTurnoverType =>
                new RoutingOutcome(false, _resolveTurnoverType.Receive(c)),
            _ => throw new InvalidOperationException($"No route for continuation '{c.Next}'.")
        },

        _ => throw new InvalidOperationException($"Unknown result type '{result.GetType().Name}'.")
    };
}
