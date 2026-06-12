namespace Charm.Engine;

/// <summary>
/// The two outcomes Roll L (free-throw resolution) can resolve a single free-throw
/// attempt to. This enum defines the slices of Roll L's pie. Declaration order is
/// significant: <see cref="Pie{TOutcome}"/> walks slices in this order, so the same
/// RNG draw always maps to the same outcome (reproducibility).
///
/// Roll L is the engine's purest primitive: a flat, context-free coin against the
/// shooter's free-throw rating. Unlike every shot-bearing enum, this carries NO
/// downstream fan-out of its own — a Make or a Miss is a bare FACT. The resolver,
/// not Roll L, owns what a make/miss MEANS for the possession (does this attempt
/// end the trip, retrigger the next shot, or send a live miss to the FT-rebound
/// node). A made attempt is 1 point and a miss is 0, but the point value — like the
/// 2/3 of a field goal — is a DOWNSTREAM derivation read by the future scoring
/// pass; Roll L records neither score nor count, only the make/miss outcome.
/// </summary>
public enum FreeThrowOutcome
{
    /// <summary>The free throw goes in. Worth 1 point (a downstream derivation, not
    /// recorded here). On the LAST shot of a trip this ends the possession; on an
    /// intermediate shot it simply retriggers the next attempt.</summary>
    Make,

    /// <summary>The free throw misses. On the LAST shot of a trip the ball is live —
    /// the resolver routes it to the FT-rebound node; on an intermediate shot the
    /// ball stays dead and the next attempt is retriggered.</summary>
    Miss
}
