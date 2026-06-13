namespace Charm.Engine;

/// <summary>
/// Point-value derivations. The single home for "what is a made shot worth" so the
/// 2-vs-3 rule lives in exactly one place. A made FREE THROW is always 1 point and is
/// tallied directly where it occurs (the FT driver), not here.
/// </summary>
public static class Scoring
{
    /// <summary>The point value of a made field goal from <paramref name="zone"/>:
    /// 3 from <see cref="ShotLocation.Three"/>, 2 from every other zone (Long is a
    /// long TWO — worth 2 despite the name).</summary>
    public static int FieldGoalPoints(ShotLocation zone) =>
        zone == ShotLocation.Three ? 3 : 2;
}
