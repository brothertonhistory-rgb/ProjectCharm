namespace Charm.Engine;

/// <summary>
/// The flavor of an offensive foul — the slices of the offensive-foul theater pie.
/// This is descriptive/observability ONLY: it is stamped onto the OffensiveFoul
/// terminal at the resolver's single chokepoint and logged (like FoulFlavor on
/// defensive fouls), and it does NOT affect routing. The possession is already over
/// when this is drawn; it is pure narrative backfill.
///
/// Two mixes exist (front-court and back-court), selected by the court-state the
/// terminal already carries — no new signal required.
///
/// Declaration order is significant: <see cref="Pie{TOutcome}"/> walks slices in
/// this order, so the same RNG draw always maps to the same flavor.
/// </summary>
public enum OffensiveFoulFlavor
{
    /// <summary>A charge — the defender was set and the ball-handler ran through them.
    /// The most common offensive foul in the frontcourt; more common in the backcourt
    /// than an illegal screen (which requires a set play).</summary>
    Charge,

    /// <summary>A push-off — the ball-handler used a non-dribbling arm to create
    /// separation. Common in both courts on drives and post moves.</summary>
    PushOff,

    /// <summary>An illegal screen — the screener was still moving or made contact
    /// before the defender could stop. Heavily frontcourt-weighted (screens are a
    /// halfcourt-set play); nearly absent in the backcourt.</summary>
    IllegalScreen
}
