namespace Charm.Engine;

/// <summary>
/// The outcomes Roll M (free-throw rebound resolution) can resolve to — the slices
/// of Roll M's pie. This is the node a MISSED FINAL free throw drains into (Roll L's
/// FT-sequence driver routes a live last-shot miss to <see cref="ContinuationKind.ResolveFTRebound"/>).
/// Declaration order is significant: <see cref="Pie{TOutcome}"/> walks slices in this
/// order, so the same RNG draw always maps to the same outcome (reproducibility).
///
/// <para>Roll M is Roll I's shape (a board battle gate with mixed terminals and
/// continues, plus the shared loose-ball foul fork) with two differences: a more
/// DEFENSIVE board population (everyone lined along the lane off a free throw, no
/// shooter crashing in) and an added OUT-OF-BOUNDS pair. It opens NO new stub — every
/// arm routes to a node that already exists.</para>
///
/// The split that matters is what happens to the ball:
/// <list type="bullet">
///   <item><see cref="DefensiveRebound"/> flips it on a LIVE board — TERMINAL, a
///   transition start carrying the conservative FT-rebound context (Roll J reads it).</item>
///   <item><see cref="OffensiveRebound"/> keeps it for the offense — CONTINUE to the
///   offensive-rebound node (Roll K), stamped with the FreeThrow source so Roll K
///   selects its FT-specific pie.</item>
///   <item><see cref="LooseBallFoulOnDefense"/> keeps the offense's ball — CONTINUE;
///   charges the defense and forks on the bonus (the shared charge-and-fork, the
///   5th feeder after D / I / J / K).</item>
///   <item><see cref="LooseBallFoulOnOffense"/> and <see cref="OutOfBoundsOffOffense"/>
///   flip it on a DEAD ball — both TERMINALS, dead-ball inbound to the defense at Roll
///   A. No foul charged (Roll C's OffensiveFoul precedent); the two differ only in the
///   reason label.</item>
///   <item><see cref="OutOfBoundsOffDefense"/> keeps the offense's ball — CONTINUE to
///   the sideline-inbound node, NO charge and NO bonus fork (no foul means no bonus
///   question).</item>
///   <item><see cref="JumpBall"/> is a CONTINUE to the shared jump-ball node, which
///   resolves the award against the arrow.</item>
/// </list>
/// </summary>
public enum FreeThrowReboundOutcome
{
    /// <summary>The defense secures the missed free throw. Ball switches teams on a
    /// LIVE board — the defense pushes the other way (more conservatively than off a
    /// field goal: the made/missed FT gave everyone time to get back). Carries the
    /// FreeThrowRebound transition context so Roll J selects its tamer run-or-not pie.
    /// -> TERMINAL.</summary>
    DefensiveRebound,

    /// <summary>The offense secures its own missed free throw. Same possession stays
    /// alive — CONTINUE to the offensive-rebound node (Roll K), stamped with the
    /// FreeThrow source so Roll K selects its FT-specific pie (more putback,
    /// point-blank). -> CONTINUE.</summary>
    OffensiveRebound,

    /// <summary>A loose-ball foul on the defense along the lane. Offense retains. The
    /// defensive team foul is charged; the bonus is read: below bonus -> sideline
    /// inbound; in bonus -> bonus free throws (Roll L). The FIFTH feeder into the
    /// shared charge-and-fork (after Roll D / I / J / K) — copied, not reinvented.
    /// -> CONTINUE.</summary>
    LooseBallFoulOnDefense,

    /// <summary>A loose-ball foul on the offense (over-the-back on the lane). Ball
    /// switches teams — dead ball. No foul is charged (Roll C's OffensiveFoul
    /// precedent). Next possession is a dead-ball inbound at Roll A. -> TERMINAL.</summary>
    LooseBallFoulOnOffense,

    /// <summary>The ball goes out of bounds last touched by the OFFENSE in the
    /// scramble. Ball switches teams — dead ball, NO foul charged. Lands exactly where
    /// the offensive loose-ball foul lands (defense's ball at Roll A), a different
    /// reason label for the same routing. -> TERMINAL.</summary>
    OutOfBoundsOffOffense,

    /// <summary>The ball goes out of bounds last touched by the DEFENSE in the
    /// scramble. Offense retains and inbounds from the side. NO foul charged, so NO
    /// bonus fork — it is always a plain sideline inbound (no foul means no bonus
    /// question), unlike the loose-ball-defense arm. -> CONTINUE.</summary>
    OutOfBoundsOffDefense,

    /// <summary>A tie-up on the loose ball along the lane. -> CONTINUE to the shared
    /// jump-ball node (consults the possession arrow), exactly as Roll K's tie-up arm.</summary>
    JumpBall
}
