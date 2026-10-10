namespace Charm.Engine;

/// <summary>
/// ★ S120 — the game clock as a roll can see it: which period, and how many seconds are left in
/// it, read at the START of the current possession.
///
/// <para>The <see cref="Governor"/> owns the clock (the half and its seconds are locals of its
/// loops) and publishes this value onto <see cref="GameState.Clock"/> at the top of every
/// possession it runs. Nothing else writes it. A free throw comes partway through a possession,
/// so the value is a few seconds early by the time the lane is set; the ruling it serves (a man
/// in foul trouble with "more than 5 or so minutes left") does not care about those seconds.</para>
///
/// <para>Period 1 and 2 are the regulation halves; 3 and up are overtime periods. A game driven
/// without a <see cref="Governor"/> (a harness path that calls the resolver directly) never has a
/// clock, and every reader treats that as "no clock", never as a default period.</para>
/// </summary>
public readonly record struct PeriodClock(int Period, double SecondsLeft)
{
    /// <summary>True in the first regulation half.</summary>
    public bool IsFirstHalf => Period == 1;

    /// <summary>True in the second regulation half.</summary>
    public bool IsSecondHalf => Period == 2;

    /// <summary>True in any overtime period.</summary>
    public bool IsOvertime => Period >= 3;
}
