namespace Charm.Engine;

/// <summary>
/// The coaching-malleability seam. Bends a player's authored per-zone shot
/// tendencies toward the team's coaching system preference, weighted by the
/// player's malleability rating.
///
/// <para><b>Phase 30 — seam is now live.</b> <see cref="CoachProfile.ShotSelectionBias"/>
/// drives a signed nudge that boosts inside or outside zones relative to neutral (5.0).
/// The body replaces the v1 identity stub; every call site is unchanged because the
/// signature already used the real placeholder types.</para>
///
/// <para><b>Nudge math.</b>
/// <c>nudge = (ShotSelectionBias − 5.0) / 5.0</c>, mapping [1,10] → [−0.8, +1.0].
/// <list type="bullet">
///   <item>Inside zones (Rim, Short): multiplied by <c>(1 − nudge × InsideScale)</c>.
///   Bias &lt; 5 boosts inside (nudge negative → factor &gt; 1); bias &gt; 5 suppresses.</item>
///   <item>Outside zones (Long, Three): multiplied by <c>(1 + nudge × OutsideScale)</c>.
///   Bias &gt; 5 boosts outside; bias &lt; 5 suppresses.</item>
///   <item>Mid is a neutral zone — returned unchanged at any bias value.</item>
///   <item>Floor clamp of 1.0 prevents any zone from being suppressed to zero.</item>
/// </list>
/// Player identity is preserved by design: a Shaq-style rim finisher retains a
/// dominant rim value even at bias 10 (outside); a Korver-style shooter retains a
/// dominant three value even at bias 1 (inside).</para>
///
/// <para><b>Normalization.</b> Returned values are raw adjusted tendencies — NOT
/// normalized. <see cref="Generators.RollGGenerator"/> owns normalization; this
/// method must not normalize so that the matchup multipliers in Roll G multiply
/// the same unnormalized scale.</para>
///
/// <para><b>Logged for a future session (do NOT act on these here):</b>
/// <list type="bullet">
///   <item>Malleability is a per-player attribute, not a constant. Stars conform
///         less; role players conform more. When wired: <c>nudge *= malleability</c>
///         before the zone calculations.</item>
///   <item>Coaching pull can be AGAINST the player's best interest (a coach running
///         three-heavy with a post-up big forces threes he can't make). The math
///         already allows this — it is a basketball feature, not a bug.</item>
///   <item>The system-identity question (five independent zone preferences vs a
///         coherent system that constrains the five together) is a design call for
///         a future session.</item>
/// </list></para>
/// </summary>
public static class CoachingPull
{
    private const double InsideScale  = 0.40;
    private const double OutsideScale = 0.40;

    /// <param name="shooter">The player whose tendencies are being read.</param>
    /// <param name="coach">The coaching profile. Null → neutral (5.0) fallback,
    /// producing identity behavior identical to v1.</param>
    /// <param name="malleability">The shooter's per-player malleability (null
    /// in Phase 30 — attribute not yet added to Player; treated as 1.0).</param>
    /// <returns>The five per-zone tendency values: (Rim, Short, Mid, Long, Three).
    /// Raw adjusted values — not normalized. Floor-clamped to 1.0 per zone.</returns>
    public static (double rim, double @short, double mid, double @long, double three) Apply(
        Player shooter, CoachProfile? coach, double? malleability)
    {
        // Null-coach fallback = neutral (5.0) = identity. Preserves behavior for
        // any call site that has not yet been wired to a real coach.
        var coachBias = coach?.ShotSelectionBias ?? 5.0;

        // Map ShotSelectionBias [1,10] to a signed nudge. Neutral (5.0) → 0.0.
        //   Bias 1  → nudge = −0.8  (most inside system)
        //   Bias 5  → nudge =  0.0  (neutral — identity)
        //   Bias 10 → nudge = +1.0  (most outside system)
        // Intentionally asymmetric: 5 is slightly closer to 1 than to 10 on this scale.
        var nudge = (coachBias - 5.0) / 5.0;

        // Malleability deferred (Phase 30): treated as 1.0.
        // When wired: nudge *= malleability.Value;

        var adjRim   = shooter.RimTendency   * (1.0 - nudge * InsideScale);
        var adjShort = shooter.ShortTendency * (1.0 - nudge * InsideScale);
        var adjMid   = (double)shooter.MidTendency;  // neutral zone — unchanged
        var adjLong  = shooter.LongTendency  * (1.0 + nudge * OutsideScale);
        var adjThree = shooter.ThreeTendency * (1.0 + nudge * OutsideScale);

        // Floor clamp: a coach cannot suppress a zone to zero.
        // Normalization in RollGGenerator owns the final pie; do not normalize here.
        return (
            Math.Max(1.0, adjRim),
            Math.Max(1.0, adjShort),
            Math.Max(1.0, adjMid),
            Math.Max(1.0, adjLong),
            Math.Max(1.0, adjThree));
    }
}
