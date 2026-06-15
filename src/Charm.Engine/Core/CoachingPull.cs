namespace Charm.Engine;

/// <summary>
/// The coaching-malleability seam (Phase 9 scaffold). Bends a player's
/// authored per-zone shot tendencies toward the team's coaching preferences,
/// weighted by the player's malleability rating.
///
/// <para><b>v1 is the IDENTITY function.</b> The coaching layer
/// (<see cref="CoachProfile"/> fields, per-player malleability attribute,
/// the actual pull math) does not yet exist; Phase 9 ships the seam so the
/// future coaching session is a clean append, not a teardown. When that
/// session lands, the BODY of <see cref="Apply"/> changes; every call site
/// stays untouched because the signature already uses the real placeholder
/// type.</para>
///
/// <para><b>Logged for the coaching session (do NOT act on these here):</b>
/// <list type="bullet">
///   <item>Malleability is a per-player attribute, not a constant. Stars
///         conform less; role players conform more.</item>
///   <item>Coaching pull can be AGAINST the player's best interest (a coach
///         running three-heavy with a post-up big is making the big shoot
///         threes he can't make). The mechanism must allow this — it is a
///         basketball feature, not a bug.</item>
///   <item>The "system" question (five independent zone preferences vs a
///         coherent system identity that constrains the five together) is a
///         design call for that session, not this one.</item>
/// </list></para>
/// </summary>
public static class CoachingPull
{
    /// <param name="shooter">The player whose tendencies are being read.</param>
    /// <param name="coach">The coaching profile (null in v1 — coaching layer
    /// not yet built).</param>
    /// <param name="malleability">The shooter's per-player malleability (null
    /// in v1 — attribute not yet added to Player).</param>
    /// <returns>The five per-zone tendency values: (Rim, Short, Mid, Long,
    /// Three). In v1, returns the shooter's authored values unchanged.</returns>
    public static (double rim, double @short, double mid, double @long, double three) Apply(
        Player shooter, CoachProfile? coach, double? malleability)
    {
        // v1: identity. The coaching session replaces this body; the call
        // sites in RollGGenerator do not change because the signature
        // already uses the real placeholder types.
        return (
            shooter.RimTendency,
            shooter.ShortTendency,
            shooter.MidTendency,
            shooter.LongTendency,
            shooter.ThreeTendency);
    }
}
