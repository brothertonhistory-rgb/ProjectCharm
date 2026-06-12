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

/// <summary>STUB for the block-recovery node — where Roll H's BLOCKED shot lands.
/// A block is a LIVE-BALL event with its own future fan-out (ball out of bounds
/// off defense / off offense / scramble recovered by either team), so this is the
/// holding pen for that future roll. As of Session 13 the block moved from Roll F
/// to Roll H, so it now lands AFTER all three per-possession facts are stamped
/// (slot by Roll E, zone by Roll G, result by Roll H) — so it echoes slot, zone,
/// AND result via the shared <see cref="ShotFacts"/> helper, exactly like the
/// other post-Roll-H stubs, letting the harness confirm all three rode through.
/// MAY eventually share a loose-ball / inbound node with the sideline-inbound stub
/// — flagged, not merged. A real node replaces this without Roll H changing.</summary>
public sealed class BlockRecoveryStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        ShotFacts.Describe("BlockRecovery", continuation.State);
}

/// <summary>STUB for the offensive-rebound node — where Roll I's
/// <c>OffensiveRebound</c> outcome lands. The offense secured the board; the same
/// possession stays alive. The real offensive-rebound roll (its own odds, one
/// branch looping back to the halfcourt roll → player selection) is a later
/// session. Echoes slot, zone, AND result via <see cref="ShotFacts.Describe"/> so
/// the harness confirms all three per-possession facts rode through Roll I intact.
/// A real node replaces this without Roll I changing.
/// <para>NOTE: <c>ReboundStub</c> was RETIRED this session — Roll I replaced it.
/// <c>OffensiveReboundStub</c> is its successor for the offensive-rebound continue
/// arm only; the terminal arms (<c>DefensiveRebound</c> /
/// <c>LooseBallFoulOnOffense</c>) end the possession directly and need no stub.</para>
/// </summary>
public sealed class OffensiveReboundStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        ShotFacts.Describe("OffensiveRebound", continuation.State);
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

/// <summary>STUB for the transition node — where Roll J's <c>Push</c> lands: the
/// possession decided to RUN. This is the future transition roll's holding pen (what
/// the fast break PRODUCES — numbers, leak-outs, transition shot mix). A plain label
/// stub like the pre-shot stubs (no per-possession facts to echo: a transition entry
/// that pushes has not selected a player/zone/result — Settle, not Push, is what feeds
/// player selection). The real transition roll replaces this without Roll J or the
/// result contract changing.</summary>
public sealed class TransitionStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:Transition";
}

/// <summary>
/// Shared label-builder for the four post-Roll-H stubs (rebound, shooting free
/// throws, sideline inbound, and — as of Session 13 — block recovery). Each lands
/// AFTER all three per-possession facts are stamped (slot by Roll E, zone by Roll
/// G, result by Roll H), so each echoes all three in the form
/// <c>STUB:{node}:{Side}slot{N}:{Zone}:{Result}</c>, surfacing any missing fact
/// loud so the harness catches a dropped stamp. Centralized so the stubs stay
/// identical in shape.
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
