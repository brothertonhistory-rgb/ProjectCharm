namespace Charm.Engine;

/// <summary>
/// Generator contract for Roll K (offensive-rebound resolution). Returns a
/// seven-way <see cref="Pie{TOutcome}"/> over <see cref="OffensiveReboundOutcome"/>.
///
/// <para><paramref name="state"/> provides <see cref="PossessionState.ReboundSlot"/>
/// (the Phase 31 picker result — which offensive player secured the board) and
/// <see cref="PossessionState.ShotType"/> (the zone modifier input). The stub
/// ignores <paramref name="state"/>; the real generator reads both fields.</para>
///
/// <para>A null <see cref="PossessionState.ReboundSlot"/> causes the real
/// generator to fall back to flat config weights (same behavior as the stub).</para>
///
/// <para>A null <see cref="PossessionState.ShotType"/> (free-throw board) maps
/// to a zone modifier of 1.0 in the real generator.</para>
///
/// <para><paramref name="source"/> selects the LiveBall or FreeThrow base weight
/// set. The routing in Roll K is identical for both; only the weights differ.</para>
/// </summary>
public interface IRollKPieGenerator
{
    /// <summary>Generate the seven-way offensive-rebound pie.</summary>
    /// <param name="state">Current possession state — provides <c>ReboundSlot</c>
    /// and <c>ShotType</c>. Null <c>ReboundSlot</c> → flat config fallback.</param>
    /// <param name="source">Which board the offense secured — selects the LiveBall
    /// or FreeThrow base weight set.</param>
    Pie<OffensiveReboundOutcome> Generate(PossessionState state, OffensiveReboundSource source);
}
