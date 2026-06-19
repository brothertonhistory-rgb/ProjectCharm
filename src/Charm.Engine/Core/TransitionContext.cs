namespace Charm.Engine;

/// <summary>
/// The SOURCE axis of a transition entry — HOW a live-ball possession began. This
/// is the part of a transition ticket's memory that selects Roll J's run-or-not
/// pie. It is a tag, not a <see cref="Pie{TOutcome}"/> outcome.
///
/// <para>THREE values are live: <see cref="Rebound"/> (a defensive rebound off a
/// field-goal miss), <see cref="FreeThrowRebound"/> (a defensive rebound off a
/// missed free throw — Roll M), and <see cref="Steal"/> (a LIVE-BALL theft — an
/// intercepted pass or a strip of a live dribble, from Roll C's
/// <c>BadPassIntercepted</c> / <c>LostBallLiveBall</c> and Roll K's
/// <c>LiveBallTurnover</c>). Each arrives together with its pie and its routing,
/// never declared ahead of being produced or handled: an undeclared value cannot be
/// silently produced or half-handled (the "many feeders, one node" discipline,
/// applied to a source rather than a kind). The Steal source closed the last open
/// transition feed (Contextification #3).</para>
/// </summary>
public enum TransitionSource
{
    /// <summary>The possession began on a defensive rebound off a field-goal miss —
    /// the rebounding team pushes the other way.</summary>
    Rebound,

    /// <summary>The possession began on a defensive rebound off a missed FREE THROW
    /// (Roll M's DefensiveRebound arm). The same live-ball push, but Roll J selects a
    /// tamer, more conservative run-or-not pie for it: the made/missed free throw gave
    /// everyone time to get back, so the break is less likely to run than off a live
    /// field-goal board. Added this session alongside Roll M.</summary>
    FreeThrowRebound,

    /// <summary>The possession began on a LIVE-BALL STEAL — an intercepted pass or a
    /// strip of a live dribble (Roll C's <c>BadPassIntercepted</c> /
    /// <c>LostBallLiveBall</c>, Roll K's <c>LiveBallTurnover</c>). The best fast-break
    /// trigger in basketball: the defender is already moving the other way with the
    /// offense caught upcourt, so Roll J selects the most run-happy pie of the three —
    /// the highest Push, the lowest Settle. Added in the steal-feeder session
    /// (Contextification #3). The speed/athleticism "who got the steal" tilt is the
    /// deferred attribute seam; Roll J reads no attributes yet.</summary>
    Steal
}

/// <summary>
/// Phase 28 — steal-origin split. Discriminates on the VICTIM team's
/// <see cref="PossessionState.Frontcourt"/> flag at the instant of the steal,
/// read INVERTED for the thief (the new offense).
///
/// <para><b>Role-flip wire:</b> <see cref="PossessionState.Frontcourt"/> belongs
/// to the VICTIM (the team that lost the ball); the run odds belong to the THIEF
/// (the new offense), and the relationship is inverted:
/// <list type="bullet">
///   <item><see cref="BackcourtVictim"/> — victim was still in the backcourt
///   (<c>Frontcourt == false</c>). The thief already has the ball near his own
///   scoring basket → HIGH run odds.</item>
///   <item><see cref="FrontcourtVictim"/> — victim was in the halfcourt set
///   (<c>Frontcourt == true</c>). The thief must go the full length of the court
///   → LOW run odds.</item>
/// </list></para>
///
/// <para>Roll K's <c>LiveBallTurnover</c> is always <see cref="FrontcourtVictim"/>:
/// a live turnover off an offensive rebound happens in the frontcourt (the offense
/// had already shot and rebounded), so the new defense/thief must go the full
/// court. High-run odds require proof; a putback-traffic turnover is not a
/// pick-six.</para>
///
/// <para>Null on non-steal tickets and on the legacy
/// <see cref="TransitionContext.Steal"/> shorthand (null-origin fallback).</para>
/// </summary>
public enum StealOrigin
{
    /// <summary>Victim was in the backcourt (<c>Frontcourt == false</c>) —
    /// thief is already in scoring territory → HIGH run odds.</summary>
    BackcourtVictim,

    /// <summary>Victim was in the halfcourt set (<c>Frontcourt == true</c>) —
    /// thief must go the full court → LOW run odds. Also the safe default for
    /// Roll K's <c>LiveBallTurnover</c> (offensive rebound context).</summary>
    FrontcourtVictim
}

/// <summary>
/// A transition entry's TICKET MEMORY — the context a live-ball possession carries
/// from the terminal that spawned it into Roll J, which reads it to choose its
/// run-or-not pie. The cross-possession analog of Roll C's within-possession
/// turnover-context ticket: same ticket/station pattern (a station stamps it at
/// write time; the node reads it to pick a parameter set; the node NEVER queries
/// the upstream station), carried on the consequence/entry seam instead of a
/// <see cref="Continue"/> because it crosses a possession boundary.
///
/// <para>STRUCTURED so memory GROWS BY APPEND, never a teardown: the required field
/// is <see cref="Source"/> (Rebound / FreeThrowRebound / Steal). Optional fields
/// appended as the engine grows:
/// <list type="bullet">
///   <item><see cref="Origin"/> (Phase 28) — steal-origin split: which court state
///   the victim was in when the steal happened. Null on non-steal tickets and on
///   legacy unclassified-steal tickets.</item>
///   <item><see cref="OffenseSide"/> (Phase 28) — the team identity of the new
///   offense (the team that received the ball). Stamped by all three transition
///   helpers alongside their other fields; null only on hand-constructed test
///   tickets. Lets Roll J's generator compute the directional athleticism gap
///   without receiving a per-call TeamSide parameter — the ticket carries what
///   the node needs.</item>
/// </list></para>
///
/// <para>Roll J reads whichever fields exist; a ticket carrying fewer still
/// resolves (null defaults to neutral/fallback). (A bare enum here would force
/// enum-explosion or a teardown to add those fields — rejected.)</para>
///
/// <para>It rides the cross-possession seam: a terminal distills it into
/// <see cref="PossessionConsequence.TransitionContext"/>, the Governor threads it
/// onto the spawned <see cref="PossessionState.TransitionContext"/>, and the
/// resolver hands it to Roll J. Null everywhere a possession did NOT begin in
/// transition (every dead-ball inbound), so the legacy path is byte-for-byte
/// untouched. Every live-ball entry — rebound, free-throw rebound, AND (as of
/// Contextification #3) steal — carries a non-null ticket.</para>
/// </summary>
/// <param name="Source">How the transition began — Rebound, FreeThrowRebound, or
/// Steal.</param>
public sealed record TransitionContext(TransitionSource Source)
{
    /// <summary>Steal origin: which court state the VICTIM was in when the steal
    /// happened (Phase 28, steal-origin split). Null on non-steal tickets and on
    /// the legacy <see cref="Steal"/> shorthand ticket (null-origin fallback in the
    /// generator). See <see cref="StealOrigin"/> for the role-flip semantics.</summary>
    public StealOrigin? Origin { get; init; }

    /// <summary>The <see cref="TeamSide"/> of the new offense — the team that
    /// received the ball and is now running the transition (Phase 28). Stamped by
    /// all three transition consequence helpers (<see cref="PossessionConsequence.TransitionStealTo"/>,
    /// <see cref="PossessionConsequence.TransitionReboundTo"/>,
    /// <see cref="PossessionConsequence.TransitionFreeThrowReboundTo"/>) at the
    /// consequence site where the new offense is already known.
    /// <para>Null only on hand-constructed test tickets that do not set it; the
    /// generator treats null as "no athleticism-gap modifier" (neutral = gap of 0).
    /// This lets isolated harness checks (which construct tickets directly without a
    /// full game context) continue to work byte-for-byte at the regression
    /// anchor.</para></summary>
    public TeamSide? OffenseSide { get; init; }

    /// <summary>The defensive-rebound transition ticket (off a field-goal miss). A
    /// read-clear shorthand for terminal/consequence sites; equivalent to
    /// <c>new TransitionContext(TransitionSource.Rebound)</c>.
    /// Note: <see cref="OffenseSide"/> is null — production code uses
    /// <see cref="PossessionConsequence.TransitionReboundTo"/> which stamps it.</summary>
    public static TransitionContext Rebound { get; } = new(TransitionSource.Rebound);

    /// <summary>The free-throw-rebound transition ticket (off a missed final free
    /// throw — Roll M's DefensiveRebound arm). Selects Roll J's conservative pie.
    /// Note: <see cref="OffenseSide"/> is null — production code uses
    /// <see cref="PossessionConsequence.TransitionFreeThrowReboundTo"/>.</summary>
    public static TransitionContext FreeThrowRebound { get; } = new(TransitionSource.FreeThrowRebound);

    /// <summary>The steal transition ticket (a live-ball interception or strip — Roll
    /// C's <c>BadPassIntercepted</c> / <c>LostBallLiveBall</c>, Roll K's
    /// <c>LiveBallTurnover</c>). Null-origin fallback: the generator uses the old
    /// single steal baseline. Production code uses
    /// <see cref="PossessionConsequence.TransitionStealTo"/> which stamps both
    /// <see cref="Origin"/> and <see cref="OffenseSide"/>.</summary>
    public static TransitionContext Steal { get; } = new(TransitionSource.Steal);
}
