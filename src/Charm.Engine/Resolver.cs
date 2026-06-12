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
    private readonly RollCStubPieGenerator _rollCGenerator;
    private readonly RollDStubPieGenerator _rollDGenerator;
    private readonly RollEStubPieGenerator _rollEGenerator;
    private readonly RollFStubPieGenerator _rollFGenerator;
    private readonly GameState _game;
    private readonly IRng _rng;
    private readonly IContinuationNode _resumeInbound;
    private readonly IContinuationNode _resolveFreeThrows;
    private readonly IContinuationNode _resolveBlock;
    private readonly IContinuationNode _intoShotType;

    public Resolver(
        RollBStubPieGenerator rollBGenerator,
        RollCStubPieGenerator rollCGenerator,
        RollDStubPieGenerator rollDGenerator,
        RollEStubPieGenerator rollEGenerator,
        RollFStubPieGenerator rollFGenerator,
        GameState game,
        IRng rng,
        IContinuationNode resumeInbound,
        IContinuationNode resolveFreeThrows,
        IContinuationNode resolveBlock,
        IContinuationNode intoShotType)
    {
        _rollBGenerator = rollBGenerator;
        _rollCGenerator = rollCGenerator;
        _rollDGenerator = rollDGenerator;
        _rollEGenerator = rollEGenerator;
        _rollFGenerator = rollFGenerator;
        _game = game;
        _rng = rng;
        _resumeInbound = resumeInbound;
        _resolveFreeThrows = resolveFreeThrows;
        _resolveBlock = resolveBlock;
        _intoShotType = intoShotType;
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
                            var pieB = _rollBGenerator.Generate(c.State, physicality: 0.0);
                            result = RollB.Execute(c.State, pieB, _rng);
                            continue;

                        // Turnover (from any feeder: Roll A, Roll B, Roll F) ->
                        // execute Roll C, loop. Roll C always returns a Terminal,
                        // so the loop's Terminal case ends the possession on the
                        // next pass. Roll C integrates exactly like Roll B
                        // (execute + feed result back), not like a stub.
                        case ContinuationKind.ResolveTurnoverType:
                            var pieC = _rollCGenerator.Generate(c.State, pressure: 0.0);
                            result = RollC.Execute(c.State, pieC, _rng);
                            continue;

                        // Roll B's proceed -> execute Roll E (player selection),
                        // loop. Roll E returns a CONTINUE (IntoPlayerAction)
                        // carrying the selected slot stamped on its state — so
                        // feeding it back re-enters this switch and lands on the
                        // IntoPlayerAction case below (now Roll F). Roll E reaches
                        // GameState to name a real slot on the offense's lineup.
                        case ContinuationKind.IntoPlayerSelection:
                            var pieE = _rollEGenerator.Generate(c.State);
                            result = RollE.Execute(c.State, pieE, _game, _rng);
                            continue;

                        // Roll E's selection -> execute Roll F (player action),
                        // loop. Roll F is a flat gate: it returns a CONTINUE
                        // (IntoShotType / ResolveTurnoverType / ResolveFoulType /
                        // ResolveBlock / ResolveJumpBall), never a terminal of its
                        // own, so feeding it back re-enters this switch and lands
                        // on the matching case. Roll F reads nothing off GameState
                        // and stamps nothing, so it takes only (state, pie, rng) —
                        // like Roll B, not Roll D/E. This is the "many feeders, one
                        // node" payoff: Roll F becomes a third feeder into C and D
                        // (and a feeder into the jump-ball node) at once.
                        case ContinuationKind.IntoPlayerAction:
                            var pieF = _rollFGenerator.Generate(c.State);
                            result = RollF.Execute(c.State, pieF, _rng);
                            continue;

                        // Foul (from any feeder: Roll A entry, Roll B halfcourt,
                        // Roll F player action) -> execute Roll D, loop. Roll D
                        // returns a CONTINUE (ResumeInbound or ResolveFreeThrows),
                        // not a terminal — so feeding it back re-enters this switch
                        // and lands on the matching stub below. Roll D mutates
                        // GameState (it charges the team foul), hence it takes _game.
                        case ContinuationKind.ResolveFoulType:
                            var pieD = _rollDGenerator.Generate(c.State);
                            result = RollD.Execute(c.State, pieD, _game, _rng);
                            continue;

                        // Roll D, opponent not in bonus -> offense keeps the ball
                        // and inbounds (stub). Chain ends here for now.
                        case ContinuationKind.ResumeInbound:
                            return new RoutingOutcome(false, _resumeInbound.Receive(c));

                        // Roll D, opponent in bonus -> free-throw node (stub). The
                        // Bonus payload on c is the FT node's input. Chain ends
                        // here for now.
                        case ContinuationKind.ResolveFreeThrows:
                            return new RoutingOutcome(false, _resolveFreeThrows.Receive(c));

                        // Roll F, blocked attempt -> block-recovery node (stub). A
                        // live-ball event with its own future fan-out. Chain ends
                        // here for now.
                        case ContinuationKind.ResolveBlock:
                            return new RoutingOutcome(false, _resolveBlock.Receive(c));

                        // Roll F, clean attempt got off -> shot-type node (stub),
                        // the future Roll G. The one Roll F outcome that proceeds
                        // deeper. Chain ends here for now (the new frontier).
                        case ContinuationKind.IntoShotType:
                            return new RoutingOutcome(false, _intoShotType.Receive(c));

                        // Jump ball (from any feeder: Roll A, Roll B, Roll F) ->
                        // resolve against the possession arrow, then END the
                        // possession. A held ball ends the current possession; the
                        // awarded team's ensuing possession is a NEW possession
                        // (future work), not a continuation of this one. Mutates
                        // the arrow as a side effect (sets it on the opening tip,
                        // flips it otherwise).
                        case ContinuationKind.ResolveJumpBall:
                            var award = JumpBall.Resolve(_game, _rng);
                            var reason = award.WasTipContest
                                ? $"JumpBallTip:{award.AwardedTo}"
                                : $"JumpBallArrow:{award.AwardedTo}";
                            result = new Terminal(reason, c.State);
                            continue;

                        default:
                            throw new InvalidOperationException($"No route for continuation '{c.Next}'.");
                    }

                default:
                    throw new InvalidOperationException($"Unknown result type '{result.GetType().Name}'.");
            }
        }
    }
}
