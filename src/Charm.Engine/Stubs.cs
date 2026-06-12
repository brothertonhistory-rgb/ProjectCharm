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

/// <summary>STUB for the rebound node — where Roll H's MISS lands. The big
/// dependency several stubs now wait on: an offensive board keeps the SAME
/// possession (the ~67–70 accounting anchor), a defensive board flips it, and the
/// Governor's "same team continues" branch hangs off this. No rebound logic lives
/// here yet — it is a holding pen. Echoes the carried slot, zone, AND the stamped
/// result so the harness confirms all three per-possession facts rode through. A
/// real node replaces this without Roll H changing.</summary>
public sealed class ReboundStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        ShotFacts.Describe("Rebound", continuation.State);
}

/// <summary>STUB for the shooting-free-throw node — where Roll H's two foul arms
/// land (an and-1 on a make, or a shooting foul on a miss). The free-throw COUNT
/// (and-1 = 1; fouled miss = 2; fouled miss on a three = 3) is DERIVED later from
/// the stamped (Result, ShotType) pair by the future FT-success roll — this stub
/// resolves nothing. Kept SEPARATE from <see cref="ResolveFreeThrowsStub"/> (Roll
/// D's bonus path) for now; possible future unification is an open fork. Echoes
/// slot, zone, AND result so the harness confirms all three facts rode through.</summary>
public sealed class ShootingFreeThrowsStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        ShotFacts.Describe("ShootingFreeThrows", continuation.State);
}

/// <summary>STUB for the sideline-inbound node — where Roll H's
/// MissOutOfBoundsRetained lands: the missed shot deflected OOB off the defender,
/// the offense keeps it and inbounds from the side. MAY eventually share a
/// loose-ball / inbound node with <see cref="BlockRecoveryStub"/> — flagged, not
/// merged. Echoes slot, zone, AND result so the harness confirms all three facts
/// rode through. A real node replaces this without Roll H changing.</summary>
public sealed class SidelineInboundStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        ShotFacts.Describe("SidelineInbound", continuation.State);
}

/// <summary>STUB for the jump-ball resolver (consults the possession arrow).</summary>
public sealed class JumpBallResolverStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:JumpBallResolver";
}

/// <summary>
/// Shared label-builder for the three post-Roll-H stubs (rebound, shooting free
/// throws, sideline inbound). Each lands AFTER all three per-possession facts are
/// stamped (slot by Roll E, zone by Roll G, result by Roll H), so each echoes all
/// three in the form <c>STUB:{node}:{Side}slot{N}:{Zone}:{Result}</c>, surfacing
/// any missing fact loud so the harness catches a dropped stamp. Centralized so
/// the three stubs stay identical in shape.
/// </summary>
internal static class ShotFacts
{
    public static string Describe(string node, PossessionState state)
    {
        var slot = state.SelectedSlot;
        var zone = state.ShotType;
        var result = state.Result;
        if (slot is not { } s)
            return $"STUB:{node}:NO_SLOT";                          // should never happen; surfaces a bug loud
        if (zone is not { } z)
            return $"STUB:{node}:{s.Side}slot{s.Number}:NO_ZONE";   // ditto
        if (result is not { } r)
            return $"STUB:{node}:{s.Side}slot{s.Number}:{z}:NO_RESULT"; // ditto
        return $"STUB:{node}:{s.Side}slot{s.Number}:{z}:{r}";
    }
}
