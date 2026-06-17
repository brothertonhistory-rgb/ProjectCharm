namespace Charm.Engine;

/// <summary>
/// The full generation result from Roll G's attribute-driven generator — the pie
/// PLUS the residual pressure computed in the same bounded-shift calculation.
/// Carried as a top-level type so the interface signature does not depend on any
/// concrete generator class.
/// </summary>
/// <param name="Pie">The finished five-way shot-location pie over
/// <see cref="ShotLocation"/>, after the matchup bend and the usage-driven
/// diet-shift have been applied.</param>
/// <param name="ResidualPressure">The volume load Roll G could NOT absorb into a
/// wider shot diet this possession — <c>requestedShift − absorbedShift</c>.
/// Zero when the load was fully absorbed (versatile shooter, ordinary defense),
/// when there was no pressure, or on a FastBreak. Positive for a forced
/// specialist.</param>
public readonly record struct RollGGeneration(
    Pie<ShotLocation> Pie,
    double ResidualPressure);

/// <summary>
/// Derived interface for Roll G's generator — extends
/// <see cref="IRollGPieGenerator"/> with the richer method that returns the
/// diet-shifted pie AND the residual pressure together.
/// <para>The resolver's <c>_rollGGenerator</c> field is widened to this interface
/// so it can call <see cref="GenerateWithResidual"/> at the Roll G call site
/// without a downcast. Both the real generator (<see cref="RollGGenerator"/>) and
/// the primary stub (<see cref="RollGStubPieGenerator"/>) implement this
/// interface.</para>
/// </summary>
public interface IRollGGenerationProvider : IRollGPieGenerator
{
    /// <summary>
    /// Generate the shot-location pie AND the residual pressure in one pass.
    /// The pie incorporates the matchup bend and any usage-driven diet shift;
    /// the residual is the load that could not be shifted away.
    /// </summary>
    RollGGeneration GenerateWithResidual(PossessionState state);
}
