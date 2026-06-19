namespace Charm.Engine;

/// <summary>
/// Coaching configuration — first real properties land in Phase 29.
///
/// <para><b>HeliocentricBias (1.0–10.0, default 5.0).</b>
/// Controls how strongly the coach amplifies the authored player-hierarchy
/// rankings when building the offensive usage pie in Roll E.
/// <list type="bullet">
///   <item><description><b>1.0 = egalitarian / hierarchy-off.</b> The hierarchy
///   exponent collapses to 0, so every player's hierarchy weight is 1.0 regardless
///   of their <see cref="Player.HierarchyRank"/>. Attributes drive usage
///   entirely.</description></item>
///   <item><description><b>5.0 = standard authored-hierarchy expression.</b> The
///   exponent is <c>HierarchyExponentNeutral</c> (default 1.0). Rank 10 gets 2×
///   the weight of rank 5; rank 1 gets 0.2×.</description></item>
///   <item><description><b>10.0 = full heliocentric.</b> The exponent reaches
///   <c>HierarchyExponentMax</c> (default 2.0), further amplifying the rank gap.
///   </description></item>
/// </list>
/// The interpolation is piecewise-linear: bias [1,5] maps to exponent
/// [0, Neutral]; bias [5,10] maps to exponent [Neutral, Max]. Monotone and
/// continuous through bias = 5 — no discontinuity.</para>
///
/// <para><b>Deferred seams (no code yet — documented for future sessions).</b>
/// ShotSelectionBias, FreelanceDial, and PaceBias will live here when their
/// respective coaching layers land. They are absent from this record deliberately;
/// adding a comment-only stub would produce dead weight without the generator
/// that reads it.</para>
/// </summary>
public sealed record CoachProfile
{
    public CoachProfile(double heliocentricBias = 5.0)
    {
        if (heliocentricBias < 1.0 || heliocentricBias > 10.0)
            throw new ArgumentOutOfRangeException(
                nameof(heliocentricBias),
                $"HeliocentricBias must be in [1.0, 10.0] (got {heliocentricBias}).");
        HeliocentricBias = heliocentricBias;
    }

    public double HeliocentricBias { get; init; }
}
