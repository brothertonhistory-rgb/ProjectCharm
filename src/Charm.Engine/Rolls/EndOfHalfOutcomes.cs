namespace Charm.Engine;

/// <summary>
/// What the offense does on a possession that STARTS with less than a full shot
/// clock left in the half — the slices of the Governor's end-of-half intent pie.
/// Drawn only when <c>halfRemaining &lt; HoldThresholdSeconds</c>; on every other
/// possession the Governor draws and resolves exactly as the base clock does.
///
/// This is a CLOCK decision, not a shot-quality one: HoldShootLast and ShootEarly
/// both resolve a real shot through the normal chain (same shot quality) — they
/// differ only in how much clock drains, i.e. whether the opponent gets a return
/// trip. NoShot is the one that produces nothing.
///
/// Declaration order is significant: <see cref="Pie{TOutcome}"/> walks slices in
/// this order, so the same RNG draw always maps to the same intent.
/// </summary>
public enum EndOfHalfIntent
{
    /// <summary>Milk the clock and shoot last. The possession resolves normally for
    /// its points, but the Governor forces its elapsed to the WHOLE remaining half
    /// time, so the half ends and the opponent gets no return trip. The majority.</summary>
    HoldShootLast,

    /// <summary>Take a normal-tempo possession. Elapsed is drawn the base way and
    /// capped at the remaining time, so if time is left the opponent gets a return
    /// trip. A minority.</summary>
    ShootEarly,

    /// <summary>Run out the clock without a shot — over-dribbling, a held ball, a
    /// turnover that no longer matters. Zero points; elapsed is the whole remaining
    /// half time; the resolver is NOT run. A small slice.</summary>
    NoShot
}
