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

/// <summary>STUB for the foul-type resolver (defensive non-shooting vs. offensive).</summary>
public sealed class FoulTypeResolverStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:FoulTypeResolver";
}

/// <summary>STUB for the jump-ball resolver (consults the possession arrow).</summary>
public sealed class JumpBallResolverStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:JumpBallResolver";
}
