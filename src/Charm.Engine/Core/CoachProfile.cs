namespace Charm.Engine;

/// <summary>
/// Coaching configuration — properties expand with each coaching-layer session.
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
/// <para><b>ShotSelectionBias (1.0–10.0, default 5.0).</b>
/// Controls how strongly the coach bends a shooter's authored per-zone tendencies
/// toward an inside or outside system. Applied in <see cref="CoachingPull.Apply"/>
/// before Roll G's matchup multipliers run.
/// <list type="bullet">
///   <item><description><b>1.0 = most inside.</b> Rim and Short tendencies boosted
///   by ×1.32; Long and Three suppressed by ×0.68.</description></item>
///   <item><description><b>5.0 = neutral.</b> Returns authored tendencies exactly —
///   zero behavior change relative to an uncoached player.</description></item>
///   <item><description><b>10.0 = most outside.</b> Long and Three tendencies boosted
///   by ×1.40; Rim and Short suppressed by ×0.60.</description></item>
/// </list>
/// Mid is a neutral zone — unchanged at any bias value. A floor clamp of 1.0
/// prevents any zone from being suppressed to zero. Player identity is preserved:
/// a Shaq-style post player does not become a three-point shooter even at bias 10.</para>
///
/// <para><b>PaceBias (1.0–10.0, default 5.0).</b>
/// Controls the team's tempo preference. Wired into two seams:
/// <list type="bullet">
///   <item>Roll J transition modifier — fast coach (bias 10) increases Push share;
///   slow coach (bias 1) reduces it.</item>
///   <item>Governor possession-length draw — fast coach shifts the center down
///   (shorter possessions); slow coach shifts it up (longer possessions).</item>
/// </list>
/// Neutral (5.0) produces zero adjustment at both seams — byte-for-byte identical
/// to Phase 29 behavior.</para>
///
/// <para><b>Deferred seams.</b> FreelanceDial is not yet designed; it is absent
/// from this record deliberately.</para>
/// </summary>
public sealed record CoachProfile
{
    public CoachProfile(
        double heliocentricBias  = 5.0,
        double shotSelectionBias = 5.0,
        double paceBias          = 5.0)
    {
        ValidateBias(heliocentricBias,  nameof(HeliocentricBias));
        ValidateBias(shotSelectionBias, nameof(ShotSelectionBias));
        ValidateBias(paceBias,          nameof(PaceBias));
        HeliocentricBias  = heliocentricBias;
        ShotSelectionBias = shotSelectionBias;
        PaceBias          = paceBias;
    }

    public double HeliocentricBias  { get; init; }
    public double ShotSelectionBias { get; init; }
    public double PaceBias          { get; init; }

    private static void ValidateBias(double value, string name)
    {
        if (value < 1.0 || value > 10.0)
            throw new ArgumentOutOfRangeException(
                name, $"{name} must be in [1.0, 10.0] (got {value}).");
    }
}
