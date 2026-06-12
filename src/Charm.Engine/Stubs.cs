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

/// <summary>STUB for the block-recovery node — where Roll F's blocked attempt
/// lands. A block is a LIVE-BALL event with its own future fan-out (ball out of
/// bounds off defense / off offense / scramble recovered by either team), so
/// this is the holding pen for that future roll. The selected slot rides on the
/// carried <see cref="PossessionState"/>; the stub records WHICH slot's attempt
/// was blocked, letting the harness confirm a real slot was named. A real node
/// replaces this without Roll F changing.</summary>
public sealed class BlockRecoveryStub : IContinuationNode
{
    public string Receive(Continue continuation)
    {
        var slot = continuation.State.SelectedSlot;
        return slot is { } s
            ? $"STUB:BlockRecovery:{s.Side}slot{s.Number}"
            : "STUB:BlockRecovery:NO_SLOT";   // should never happen; surfaces a bug loud
    }
}

/// <summary>STUB for the make/miss node (the future Roll H) — where Roll G's
/// stamped shot lands, after a location has been chosen. Roll H will read BOTH
/// <see cref="PossessionState.SelectedSlot"/> AND <see cref="PossessionState.ShotType"/>
/// to resolve the matchup into points. The stub echoes WHICH slot is shooting and
/// from WHICH zone, letting the harness confirm both per-possession facts rode
/// through. A real node replaces this without Roll G changing — this is the chain's
/// new dead-end / next frontier.</summary>
public sealed class ShotResolutionStub : IContinuationNode
{
    public string Receive(Continue continuation)
    {
        var slot = continuation.State.SelectedSlot;
        var zone = continuation.State.ShotType;
        if (slot is not { } s)
            return "STUB:ShotResolution:NO_SLOT";   // should never happen; surfaces a bug loud
        if (zone is not { } z)
            return $"STUB:ShotResolution:{s.Side}slot{s.Number}:NO_ZONE";   // ditto
        return $"STUB:ShotResolution:{s.Side}slot{s.Number}:{z}";
    }
}

/// <summary>STUB for the jump-ball resolver (consults the possession arrow).</summary>
public sealed class JumpBallResolverStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:JumpBallResolver";
}
