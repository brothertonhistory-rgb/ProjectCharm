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
    /// <para>Null on every dead-ball consequence. Every transition consequence carries
    /// a ticket: <see cref="TransitionReboundTo"/> (Rebound),
    /// <see cref="TransitionFreeThrowReboundTo"/> (FreeThrowRebound), and — as of
    /// Contextification #3 — <see cref="TransitionStealTo"/> (Steal). No
    /// transition consequence carries a null context, so a <see cref="EntryType.Transition"/>
    /// entry ALWAYS routes to Roll J; meaningful only when <see cref="NextEntry"/> is
    /// <see cref="EntryType.Transition"/>.</para>
    /// </summary>
    public TransitionContext? TransitionContext { get; init; }

    /// <summary>Ball to <paramref name="team"/> on a dead-ball restart (the common
    /// case: made basket, dead-ball turnover, violation, foul, jump-ball award).</summary>
    public static PossessionConsequence DeadBallTo(TeamSide team) =>
        new(team, EntryType.DeadBallInbound);

    /// <summary>Ball to <paramref name="team"/> already advanced — the other team
    /// lost it dead BEFORE crossing the halfcourt line, so the new offense starts
    /// their inbound from the frontcourt (near their basket) and skips Roll A's
    /// bring-up entirely. Roll B is the entry node; backcourt-only violations
    /// (10-second, full-court press) are unreachable. Parallel to
    /// <see cref="DeadBallTo"/>.</summary>
    public static PossessionConsequence BallAdvancedTo(TeamSide team) =>
        new(team, EntryType.BallAdvanced);

    /// <summary>Ball to <paramref name="team"/> on a live-ball / transition start off a
    /// STEAL — a live-ball interception or a strip of a live dribble (Roll C's
    /// <c>BadPassIntercepted</c> / <c>LostBallLiveBall</c>, Roll K's
    /// <c>LiveBallTurnover</c>). Carries a <see cref="TransitionContext"/> ticket
    /// (Source=Steal) stamped with the steal <paramref name="origin"/> (Phase 28:
    /// BackcourtVictim = high-run odds; FrontcourtVictim = low-run odds) and the new
    /// offense's <see cref="TransitionContext.OffenseSide"/> so Roll J's real generator
    /// can compute the directional athleticism-gap modifier. As of Contextification #3
    /// every one of its three callers is a steal; the origin further discriminates the
    /// two sub-cases. Parallel to <see cref="TransitionReboundTo"/> /
    /// <see cref="TransitionFreeThrowReboundTo"/>.</summary>
    public static PossessionConsequence TransitionStealTo(TeamSide team, StealOrigin origin) =>
        new(team, EntryType.Transition)
        {
            TransitionContext = new TransitionContext(TransitionSource.Steal)
            {
                Origin      = origin,
                OffenseSide = team
            }
        };

    /// <summary>Ball to <paramref name="team"/> on a live-ball transition start off a
    /// DEFENSIVE REBOUND of a field-goal miss — carrying the
    /// <see cref="TransitionContext.Rebound"/> ticket so the resolver routes it to Roll
    /// J (live transition entry) and Roll J selects the rebound run-or-not pie.
    /// Phase 28: stamps <see cref="TransitionContext.OffenseSide"/> so Roll J's real
    /// generator can compute the directional athleticism-gap modifier.</summary>
    public static PossessionConsequence TransitionReboundTo(TeamSide team) =>
        new(team, EntryType.Transition)
        {
            TransitionContext = new TransitionContext(TransitionSource.Rebound)
            {
                OffenseSide = team
            }
        };

    /// <summary>Ball to <paramref name="team"/> on a live-ball transition start off a
    /// DEFENSIVE REBOUND of a missed FREE THROW (Roll M's DefensiveRebound arm) —
    /// carrying the <see cref="TransitionContext.FreeThrowRebound"/> ticket so the
    /// resolver routes it to Roll J and Roll J selects its tamer, conservative pie (off
    /// an FT the break is less likely to run). Parallel to
    /// <see cref="TransitionReboundTo"/>; the second transition source wired.
    /// Phase 28: stamps <see cref="TransitionContext.OffenseSide"/>.</summary>
    public static PossessionConsequence TransitionFreeThrowReboundTo(TeamSide team) =>
        new(team, EntryType.Transition)
        {
            TransitionContext = new TransitionContext(TransitionSource.FreeThrowRebound)
            {
                OffenseSide = team
            }
        };
}

/// <summary>The possession is over. The ball will change hands.</summary>
/// <param name="Reason">Why it ended (e.g. "ShotClockViolation").</param>
/// <param name="State">The possession state as it ended.</param>
/// <param name="Consequence">What this ending means for the next possession —
/// REQUIRED, so every terminal must state it. Required (not nullable) deliberately:
/// it makes an un-named consequence a COMPILE error at the construction site rather
/// than a silent null the Governor would have to guess at — omissions surface loud,
/// exactly when and where they happen.</param>
public sealed record Terminal(string Reason, PossessionState State, PossessionConsequence Consequence) : RollResult
{
    /// <summary>
    /// The descriptive flavor of an OffensiveFoul terminal — THEATER, never read
    /// for routing. Logged for observability and future play-by-play exactly as
    /// <see cref="Continue.Flavor"/> is on a defensive-foul continuation. Null on
    /// every non-offensive-foul terminal; stamped by the resolver at its single
    /// OffensiveFoul chokepoint (where all three emitters converge) so no emitter
    /// carries it directly. Meaningful only when <see cref="Reason"/> is
    /// "OffensiveFoul".
    /// </summary>
    public OffensiveFoulFlavor? Flavor { get; init; }
}

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

    /// <summary>
    /// The OFFENSIVE-REBOUND SOURCE TICKET an offensive board carries into Roll K — the
    /// within-possession label that tells Roll K's generator WHICH pie to select: the
    /// live-ball field-goal pie or the FT-specific pie (more putback, point-blank).
    /// FUNCTIONAL payload (it changes the odds), the same optional-payload shape as
    /// <see cref="Bonus"/>/<see cref="Flavor"/>/<see cref="TurnoverContext"/>/<see
    /// cref="Putback"/>. A LABELED tag rather than a bool so it grows by append if a
    /// third source (e.g. off a tip) ever feeds in. Stamped by Roll M's
    /// <c>OffensiveRebound</c> arm (<see cref="OffensiveReboundSource.FreeThrow"/>);
    /// Roll K's generator reads it and never queries the stamping station back.
    /// <para>NULL on every legacy feeder — Roll I (the field-goal rebound) stamps
    /// nothing — and a null reads as <see cref="OffensiveReboundSource.LiveBall"/>, so
    /// the regular Roll K pie path is byte-for-byte unchanged. Meaningful only when
    /// <see cref="Next"/> is <see cref="ContinuationKind.ResolveOffensiveRebound"/>.</para>
    /// </summary>
    public OffensiveReboundSource? OffensiveReboundSource { get; init; }

    /// <summary>
    /// The REBOUND SOURCE TICKET a loose ball carries into Roll I — the
    /// within-possession label that tells Roll I's generator WHICH pie to select: the
    /// live-miss pie (a clean field-goal carom) or the block pie (a swatted shot, which
    /// keeps more with the defense, squirts OOB more, recovers offensively more, and
    /// carries a minuscule jump-ball sliver). FUNCTIONAL payload (it changes the odds),
    /// the same optional-payload shape as <see cref="Bonus"/>/<see cref="Flavor"/>/<see
    /// cref="TurnoverContext"/>/<see cref="Putback"/>/<see cref="OffensiveReboundSource"/>.
    /// A LABELED tag rather than a bool so it grows by append if a third loose-ball
    /// source ever feeds in. Stamped by Roll H's <c>Blocked</c> arm
    /// (<see cref="ReboundSource.Block"/>); Roll I's generator reads it and never queries
    /// the stamping station back.
    /// <para>NULL on every legacy feeder — Roll H's <c>Miss</c> arm stamps nothing, and a
    /// missed putback re-entering Roll I stamps nothing — and a null reads as
    /// <see cref="ReboundSource.LiveBall"/>, so the live-miss pie path is byte-for-byte
    /// unchanged. Meaningful only when <see cref="Next"/> is
    /// <see cref="ContinuationKind.ResolveRebound"/>.</para>
    /// </summary>
    public ReboundSource? ReboundSource { get; init; }
}
