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
/// A transition entry's TICKET MEMORY — the context a live-ball possession carries
/// from the terminal that spawned it into Roll J, which reads it to choose its
/// run-or-not pie. The cross-possession analog of Roll C's within-possession
/// turnover-context ticket: same ticket/station pattern (a station stamps it at
/// write time; the node reads it to pick a parameter set; the node NEVER queries
/// the upstream station), carried on the consequence/entry seam instead of a
/// <see cref="Continue"/> because it crosses a possession boundary.
///
/// <para>STRUCTURED so memory GROWS BY APPEND, never a teardown: it holds one fact,
/// <see cref="Source"/> (Rebound / FreeThrowRebound / Steal). Future memories are
/// clean optional-field appends — e.g. an <c>Origin</c> (did the steal that started
/// this come from an entry-stage / "Pool A" turnover? that pushes harder), and later
/// a rebounder/stealer slot reference (the attribute "who" tilt). Roll J reads
/// whichever fields exist; a ticket carrying fewer still resolves. (A bare enum
/// here would force enum-explosion or a teardown to add those — rejected.)</para>
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
    /// <summary>The defensive-rebound transition ticket (off a field-goal miss). A
    /// read-clear shorthand for terminal/consequence sites; equivalent to
    /// <c>new TransitionContext(TransitionSource.Rebound)</c>.</summary>
    public static TransitionContext Rebound { get; } = new(TransitionSource.Rebound);

    /// <summary>The free-throw-rebound transition ticket (off a missed final free
    /// throw — Roll M's DefensiveRebound arm). Selects Roll J's conservative pie. A
    /// read-clear shorthand; equivalent to
    /// <c>new TransitionContext(TransitionSource.FreeThrowRebound)</c>.</summary>
    public static TransitionContext FreeThrowRebound { get; } = new(TransitionSource.FreeThrowRebound);

    /// <summary>The steal transition ticket (a live-ball interception or strip — Roll
    /// C's <c>BadPassIntercepted</c> / <c>LostBallLiveBall</c>, Roll K's
    /// <c>LiveBallTurnover</c>). Selects Roll J's most run-happy pie. A read-clear
    /// shorthand; equivalent to <c>new TransitionContext(TransitionSource.Steal)</c>.</summary>
    public static TransitionContext Steal { get; } = new(TransitionSource.Steal);
}
