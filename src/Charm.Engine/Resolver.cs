namespace Charm.Engine;

/// <summary>What the resolver did with one result — for observability/harness.</summary>
public readonly record struct RoutingOutcome(bool PossessionEnded, string Destination);

/// <summary>
/// The conductor. It walks the chain: route a ticket -> run that station ->
/// take its new ticket -> route again -> until a terminal ends the possession.
///
/// It owns all routing: a Terminal ends the possession; a Continue is mapped —
/// by its <see cref="ContinuationKind"/>, the only place that mapping lives —
/// to the next station. When a real station replaces a stub, only this mapping
/// changes; the rolls that emit tickets never reopen.
/// </summary>
public sealed class Resolver
{
    private readonly RollBStubPieGenerator _rollBGenerator;
    private readonly IRng _rng;
    private readonly IContinuationNode _resolveTurnoverType;
    private readonly IContinuationNode _resolveFoulType;
    private readonly IContinuationNode _resolveJumpBall;
    private readonly IContinuationNode _intoPlayerSelection;

    public Resolver(
        RollBStubPieGenerator rollBGenerator,
        IRng rng,
        IContinuationNode resolveTurnoverType,
        IContinuationNode resolveFoulType,
        IContinuationNode resolveJumpBall,
        IContinuationNode intoPlayerSelection)
    {
        _rollBGenerator = rollBGenerator;
        _rng = rng;
        _resolveTurnoverType = resolveTurnoverType;
        _resolveFoulType = resolveFoulType;
        _resolveJumpBall = resolveJumpBall;
        _intoPlayerSelection = intoPlayerSelection;
    }

    /// <summary>Walk the chain from <paramref name="result"/> until a terminal
    /// ends the possession. Returns the final routing outcome.</summary>
    public RoutingOutcome Route(RollResult result)
    {
        while (true)
        {
            switch (result)
            {
                case Terminal t:
                    return new RoutingOutcome(PossessionEnded: true, Destination: $"END:{t.Reason}");

                case Continue c:
                    switch (c.Next)
                    {
                        // Roll A's clean entry -> execute Roll B, loop.
                        case ContinuationKind.IntoHalfcourtSet:
                            var pie = _rollBGenerator.Generate(c.State, physicality: 0.0);
                            result = RollB.Execute(c.State, pie, _rng);
                            continue;

                        // Roll B's proceed -> player selection stub (chain ends here for now).
                        case ContinuationKind.IntoPlayerSelection:
                            return new RoutingOutcome(false, _intoPlayerSelection.Receive(c));

                        case ContinuationKind.ResolveTurnoverType:
                            return new RoutingOutcome(false, _resolveTurnoverType.Receive(c));

                        case ContinuationKind.ResolveFoulType:
                            return new RoutingOutcome(false, _resolveFoulType.Receive(c));

                        case ContinuationKind.ResolveJumpBall:
                            return new RoutingOutcome(false, _resolveJumpBall.Receive(c));

                        default:
                            throw new InvalidOperationException($"No route for continuation '{c.Next}'.");
                    }

                default:
                    throw new InvalidOperationException($"Unknown result type '{result.GetType().Name}'.");
            }
        }
    }
}
