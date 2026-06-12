namespace Charm.Engine;

/// <summary>
/// The flavor of a non-shooting defensive foul — the slices of Roll D's pie.
/// This is descriptive/observability ONLY: it is theater backfilled onto the
/// result and logged (like turnover-type), and it does NOT affect routing.
/// Routing is the bonus read, a state check, never this roll.
///
/// Declaration order is significant: <see cref="Pie{TOutcome}"/> walks slices in
/// this order, so the same RNG draw always maps to the same flavor.
/// </summary>
public enum FoulFlavor
{
    /// <summary>A reach-in on the ball-handler. The common case.</summary>
    ReachIn,

    /// <summary>A blocking foul — defender beaten, contact on the drive.</summary>
    Blocking,

    /// <summary>An off-ball foul — hand-check, hold, or bump away from the ball.</summary>
    OffBall
}

/// <summary>
/// What bonus state a team's accumulated fouls put their opponent into. This is
/// the COMPLETE interface between the foul/bonus layer and the (future) free-throw
/// node: every downstream rule — including whether a missed front end is
/// reboundable — is derivable from this single value, so nothing upstream needs
/// to encode free-throw mechanics.
///
/// <para><see cref="None"/>: under the bonus threshold — no free throws; the
/// offense simply keeps the ball and inbounds.</para>
/// <para><see cref="OneAndOne"/>: the 1-and-1. The FT node shoots a front end;
/// a MISS is a live ball -> rebound roll. (Reboundability is the FT node's rule,
/// derived from this value — it is not encoded here.)</para>
/// <para><see cref="Double"/>: the double bonus — two guaranteed attempts. A
/// missed FIRST is NOT reboundable (dead ball, immediate second attempt); only a
/// missed final attempt is live. Again, the FT node owns that; this value is all
/// it needs.</para>
/// </summary>
public enum BonusType
{
    None,
    OneAndOne,
    Double
}
