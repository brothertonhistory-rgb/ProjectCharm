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
public sealed record Continue(ContinuationKind Next, PossessionState State) : RollResult;
