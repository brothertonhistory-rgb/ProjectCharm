namespace Charm.Engine;

/// <summary>
/// The SOURCE axis of a transition entry — HOW a live-ball possession began. This
/// is the part of a transition ticket's memory that selects Roll J's run-or-not
/// pie. It is a tag, not a <see cref="Pie{TOutcome}"/> outcome.
///
/// <para>ONE value is live this session: <see cref="Rebound"/> (a defensive
/// rebound that pushed the other way). A live-ball STEAL (Roll C's
/// <c>BadPassIntercepted</c> / <c>LostBallLiveBall</c>) is the next source and
/// lands in the steal-feeder session — when those terminals begin stamping it,
/// Roll J grows a steal pie, and the resolver routes it. It is deliberately NOT
/// declared yet: an undeclared value cannot be silently produced or half-handled,
/// so the source, its pie, and its routing arrive together (the "many feeders, one
/// node" discipline, applied to a source rather than a kind).</para>
/// </summary>
public enum TransitionSource
{
    /// <summary>The possession began on a defensive rebound — the rebounding team
    /// pushes the other way. The only live transition source this session.</summary>
    Rebound
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
/// <para>STRUCTURED so memory GROWS BY APPEND, never a teardown: this session it
/// holds one fact, <see cref="Source"/> (Rebound). Future memories are clean
/// optional-field appends — e.g. an <c>Origin</c> (did the steal that started this
/// come from an entry-stage / "Pool A" turnover? that pushes harder), and later a
/// rebounder/stealer slot reference (the attribute "who" tilt). Roll J reads
/// whichever fields exist; a ticket carrying fewer still resolves. (A bare enum
/// here would force enum-explosion or a teardown to add those — rejected.)</para>
///
/// <para>It rides the cross-possession seam: a terminal distills it into
/// <see cref="PossessionConsequence.TransitionContext"/>, the Governor threads it
/// onto the spawned <see cref="PossessionState.TransitionContext"/>, and the
/// resolver hands it to Roll J. Null everywhere a possession did NOT begin in
/// transition (every dead-ball inbound, and — this session — every not-yet-wired
/// steal), so the legacy path is byte-for-byte untouched.</para>
/// </summary>
/// <param name="Source">How the transition began. <see cref="TransitionSource.Rebound"/>
/// this session.</param>
public sealed record TransitionContext(TransitionSource Source)
{
    /// <summary>The defensive-rebound transition ticket — the only live context this
    /// session. A read-clear shorthand for terminal/consequence sites; equivalent to
    /// <c>new TransitionContext(TransitionSource.Rebound)</c>.</summary>
    public static TransitionContext Rebound { get; } = new(TransitionSource.Rebound);
}
