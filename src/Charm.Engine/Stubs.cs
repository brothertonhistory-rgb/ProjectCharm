namespace Charm.Engine;

/// <summary>
/// STUBS for the nodes a CONTINUE routes to. They contain no basketball logic —
/// each only records that it received a clean hand-off, so the batch harness can
/// confirm every continue lands somewhere. They will be replaced by real nodes
/// without any roll or the result contract changing.
/// </summary>
public interface IContinuationNode
{
    /// <summary>Receive a routed continuation. Returns a label for observability.</summary>
    string Receive(Continue continuation);
}

/// <summary>STUB for the player-selection roll (the next station after Roll B's
/// Proceed outcome).</summary>
public sealed class PlayerSelectionStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:PlayerSelection";
}

/// <summary>STUB for the turnover-type resolver.</summary>
public sealed class TurnoverTypeResolverStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:TurnoverTypeResolver";
}

/// <summary>STUB for the resumed-inbound / possession-continues node — where a
/// non-shooting foul with the opponent NOT in the bonus lands: the offense keeps
/// the ball and inbounds. A real node replaces this without Roll D changing.</summary>
public sealed class ResumeInboundStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:ResumeInbound";
}

/// <summary>STUB for the free-throw node — where a non-shooting foul with the
/// opponent in the bonus lands. The <see cref="Continue.Bonus"/> payload
/// (OneAndOne / Double) is this node's input; the stub records the bonus so the
/// harness can confirm the right branch arrived.</summary>
public sealed class ResolveFreeThrowsStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        $"STUB:ResolveFreeThrows:{continuation.Bonus}";
}

/// <summary>STUB for the jump-ball resolver (consults the possession arrow).</summary>
public sealed class JumpBallResolverStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:JumpBallResolver";
}
