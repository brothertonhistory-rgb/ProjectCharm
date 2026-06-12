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

/// <summary>The possession is over. The ball will change hands.</summary>
/// <param name="Reason">Why it ended (e.g. "ShotClockViolation").</param>
/// <param name="State">The possession state as it ended.</param>
public sealed record Terminal(string Reason, PossessionState State) : RollResult;

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
}
