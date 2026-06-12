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

/// <summary>STUB for the free-throw node — RETIRED from the live chain as of Session
/// 18, when Roll L's FT-sequence driver took over both FT edges. Where a non-shooting
/// foul with the opponent in the bonus used to land; the bonus fork (Roll D / I / J /
/// K) now drives Roll L directly and the resolver no longer parks here. Kept ONLY as
/// a harness fact-echo helper: the I / J / K bonus-fork checks invoke
/// <see cref="Receive"/> directly on a bonus-foul continue to confirm the
/// <see cref="Continue.Bonus"/> payload (OneAndOne / Double) rode through, without
/// routing through the resolver's FT loop. The stub records the bonus so those checks
/// can confirm the right branch arrived.</summary>
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

/// <summary>STUB for the offensive-rebound node — RETIRED from the live chain as of
/// Session 17, when Roll K replaced it on the <c>ResolveOffensiveRebound</c> edge
/// (the same stub→roll swap as ReboundStub→Roll I a session earlier). Roll I's
/// <c>OffensiveRebound</c> continue now executes Roll K, which keeps the same
/// possession alive (putback / reset) or flips the ball — the resolver no longer
/// parks here. This class is kept ONLY as a harness fact-echo helper: a couple of
/// checks invoke <see cref="Receive"/> directly on a Roll I OffensiveRebound continue
/// to confirm slot+zone+result rode through, without routing through the resolver.
/// Echoes slot, zone, AND result via <see cref="ShotFacts.Describe"/>.</summary>
public sealed class OffensiveReboundStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        ShotFacts.Describe("OffensiveRebound", continuation.State);
}

/// <summary>STUB for the shooting-free-throw node — RETIRED from the live chain as
/// of Session 18, when Roll L's FT-sequence driver took over both FT edges (the same
/// stub→roll swap as OffensiveReboundStub→Roll K a session earlier). Roll H's two
/// foul arms (an and-1 on a make, or a shooting foul on a miss) now drive Roll L
/// directly; the resolver no longer parks here. Kept ONLY as a harness fact-echo
/// helper: a couple of checks invoke <see cref="Receive"/> directly on a Roll H
/// shooting-foul continue to confirm slot+zone+result rode through, without routing
/// through the resolver. The free-throw COUNT (and-1 = 1; fouled miss = 2; fouled
/// miss on a three = 3) is now derived at the resolver's FT entry edge. Echoes slot,
/// zone, AND result via <see cref="ShotFacts.Describe"/>.</summary>
public sealed class ShootingFreeThrowsStub : IContinuationNode
{
    public string Receive(Continue continuation) =>
        ShotFacts.Describe("ShootingFreeThrows", continuation.State);
}

/// <summary>STUB for the FT-rebound node — where a missed FINAL free throw lands
/// (the ball is live). NEW this session (Roll L): the resolver's FT-sequence driver
/// routes here when the last attempt of a trip misses — the shooting-foul last shot,
/// the double-bonus second, or a 1-and-1 front-end miss (which forfeits the second).
/// A PLAIN-label stub, deliberately NOT a <see cref="ShotFacts"/> echo: a bonus trip
/// has no shooter selected (the FT-shooter identity is a deferred seam) and no shot
/// zone/result, so reading slot/zone/result here would fire NO_SLOT and falsely flag
/// a dropped fact. The future FT-rebound roll — the offensive/defensive board split
/// off a missed FT plus any foul on that rebound — replaces this without the driver
/// or Roll L changing.</summary>
public sealed class FTReboundStub : IContinuationNode
{
    public string Receive(Continue continuation) => "STUB:FTRebound";
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
