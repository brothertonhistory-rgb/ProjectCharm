namespace Charm.Engine;

/// <summary>
/// The uniform result every roll returns: exactly one of <see cref="Terminal"/>
/// (the possession ends) or <see cref="Continue"/> (state carries forward). A
/// roll never names its successor; the <see cref="Resolver"/> routes a Continue.
/// </summary>
public abstract record RollResult
{
    /// <summary>
    /// Game-clock seconds this result consumed.
    /// <para>null = not yet apportioned; a future time roll owns it.</para>
    /// <para>non-null = the elapsed time is invariant and already known here
    /// (the shot-clock violation is the only such case in Roll A: always the
    /// full clock, never more or less, so it needs no time roll).</para>
    /// </summary>
    public double? ElapsedSeconds { get; init; }
}

/// <summary>
/// What a terminal MEANS for the next possession — the clean, MINIMAL seam the
/// Governor reads to spawn possession N+1. It carries ONLY what the thin Governor
/// needs today: who has the ball next, and how that possession starts.
///
/// <para>This lives where the terminal is GENERATED (each roll names its own
/// consequence), not parsed from a reason string by the Governor — the same
/// philosophy as "a roll names its continuation kind, the resolver maps it."</para>
///
/// <para>It is deliberately small and designed to GROW: points, clock, foul
/// context, and momentum are clean appends LATER, when their consumers exist. A
/// big speculative consequence now would be a bottleneck wearing a scalability
/// costume — rejected on purpose.</para>
/// </summary>
/// <param name="NextOffense">The team that has the ball on the next possession.
/// Its <see cref="PossessionState.Defense"/> is simply the other side.</param>
/// <param name="NextEntry">How that next possession starts — the single reconciled
/// <see cref="EntryType"/>. (The thin Governor temp-routes every entry through Roll
/// A this session regardless of this tag; the tag is honest for when the live-ball
/// entry node lands.)</param>
public sealed record PossessionConsequence(TeamSide NextOffense, EntryType NextEntry)
{
    /// <summary>
    /// The transition CONTEXT TICKET this consequence hands to the NEXT possession —
    /// the cross-possession ticket memory Roll J reads to choose its run-or-not pie.
    /// A clean APPEND to this deliberately-small seam (cf. the class summary: points,
    /// clock, foul context, momentum are the other planned appends). The terminal
    /// that spawns a transition possession distills it here; the Governor threads it
    /// onto the spawned <see cref="PossessionState.TransitionContext"/>.
    /// <para>Null on every dead-ball consequence, and — this session — on a plain
    /// <see cref="TransitionTo"/> (the steal path, still temp-routed through Roll A
    /// until the steal-feeder session). Set only by <see cref="TransitionReboundTo"/>,
    /// and only meaningful when <see cref="NextEntry"/> is
    /// <see cref="EntryType.Transition"/>.</para>
    /// </summary>
    public TransitionContext? TransitionContext { get; init; }

    /// <summary>Ball to <paramref name="team"/> on a dead-ball restart (the common
    /// case: made basket, dead-ball turnover, violation, foul, jump-ball award).</summary>
    public static PossessionConsequence DeadBallTo(TeamSide team) =>
        new(team, EntryType.DeadBallInbound);

    /// <summary>Ball to <paramref name="team"/> on a live-ball / transition start with
    /// NO context ticket — the STEAL path (a live-ball interception/strip). It carries
    /// no <see cref="TransitionContext"/>, so the resolver still temp-routes it through
    /// Roll A this session (rebound-first; steals get their own wiring + a Steal pie in
    /// the steal-feeder session). Kept distinct from <see cref="TransitionReboundTo"/>
    /// so that session is a one-line routing flip, not a terminal rewrite.</summary>
    public static PossessionConsequence TransitionTo(TeamSide team) =>
        new(team, EntryType.Transition);

    /// <summary>Ball to <paramref name="team"/> on a live-ball transition start off a
    /// DEFENSIVE REBOUND — carrying the <see cref="TransitionContext.Rebound"/> ticket
    /// so the resolver routes it to Roll J (live transition entry) and Roll J selects
    /// the rebound run-or-not pie. The one transition source wired this session.</summary>
    public static PossessionConsequence TransitionReboundTo(TeamSide team) =>
        new(team, EntryType.Transition) { TransitionContext = TransitionContext.Rebound };
}

/// <summary>The possession is over. The ball will change hands.</summary>
/// <param name="Reason">Why it ended (e.g. "ShotClockViolation").</param>
/// <param name="State">The possession state as it ended.</param>
/// <param name="Consequence">What this ending means for the next possession —
/// REQUIRED, so every terminal must state it. Required (not nullable) deliberately:
/// it makes an un-named consequence a COMPILE error at the construction site rather
/// than a silent null the Governor would have to guess at — omissions surface loud,
/// exactly when and where they happen.</param>
public sealed record Terminal(string Reason, PossessionState State, PossessionConsequence Consequence) : RollResult;

/// <summary>The possession continues. The resolver routes by <paramref name="Next"/>.</summary>
/// <param name="Next">Which kind of continuation this is (not which node).</param>
/// <param name="State">The state carried forward.</param>
public sealed record Continue(ContinuationKind Next, PossessionState State) : RollResult
{
    /// <summary>
    /// The bonus state a foul continuation carries to the (future) free-throw
    /// node — FUNCTIONAL payload, not theater. It is the complete contract for
    /// free-throw resolution: shot count, and whether a missed front end is
    /// reboundable, are all derivable from this one value, so nothing upstream
    /// encodes free-throw rules.
    /// <para>Null on every non-foul continuation (clean entry, turnover, jump
    /// ball, player selection): they have no bonus dimension. Set only by Roll D,
    /// and only meaningful when <see cref="Next"/> is <see cref="ContinuationKind.ResolveFreeThrows"/>
    /// (where it is OneAndOne or Double) — on a <see cref="ContinuationKind.ResumeInbound"/>
    /// it is <see cref="BonusType.None"/>, recorded for observability.</para>
    /// </summary>
    public BonusType? Bonus { get; init; }

    /// <summary>
    /// The descriptive flavor a foul continuation carries — THEATER, never read
    /// for routing. Logged like turnover-type for observability and future
    /// play-by-play. Null on every non-foul continuation; set only by Roll D.
    /// </summary>
    public FoulFlavor? Flavor { get; init; }

    /// <summary>
    /// The turnover CONTEXT TICKET a turnover continuation carries to Roll C — the
    /// within-possession ticket memory that selects which turnover pie Roll C uses.
    /// FUNCTIONAL payload (it changes the odds), the same optional-payload shape as
    /// <see cref="Bonus"/>/<see cref="Flavor"/>. Stamped by a feeding station; the
    /// node reads it and never queries the station back.
    /// <para>Null on every non-turnover continuation, and null on the legacy
    /// turnover feeders (Roll A, Roll B, Roll F) which stamp nothing — a null reads
    /// as <see cref="TurnoverContext.Halfcourt"/>, so their behavior is byte-for-byte
    /// unchanged. Set only by Roll J's <c>Turnover</c> arm
    /// (<see cref="TurnoverContext.Transition"/>), and only meaningful when
    /// <see cref="Next"/> is <see cref="ContinuationKind.ResolveTurnoverType"/>.</para>
    /// </summary>
    public TurnoverContext? TurnoverContext { get; init; }

    /// <summary>
    /// The PUTBACK CONTEXT TICKET an offensive-rebound putback carries into Roll H —
    /// the within-possession marker that tells Roll H's generator to select its
    /// distinct putback pie (its own make/foul/and-1 numbers) instead of the normal
    /// located-shot pie. FUNCTIONAL payload (it changes the odds), the same
    /// optional-payload shape as <see cref="Bonus"/>/<see cref="Flavor"/>/<see
    /// cref="TurnoverContext"/>. Stamped by Roll K's <c>PutBack</c> arm (which also
    /// forces the zone to <see cref="ShotLocation.Rim"/>); Roll H's generator reads
    /// it and never queries Roll K back.
    /// <para>A single bit suffices because there is exactly one putback flavor; the
    /// attribute tilts (who put it back — size, athleticism, rim rating, the
    /// defender) live in the deferred generator that reads the carried slot, not in
    /// more ticket variants. False on every normal located shot (Roll G stamps
    /// nothing here), so the regular Roll H pie path is byte-for-byte unchanged.
    /// Meaningful only when <see cref="Next"/> is
    /// <see cref="ContinuationKind.IntoShotResolution"/>.</para>
    /// </summary>
    public bool Putback { get; init; }
}
