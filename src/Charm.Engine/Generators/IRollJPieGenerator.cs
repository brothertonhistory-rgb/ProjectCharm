namespace Charm.Engine;

/// <summary>
/// The generation contract for Roll J's run-or-not pie. The single per-call
/// input is the <see cref="TransitionContext"/> ticket the arriving possession
/// carries; all game context (config, lineups, matchup) is injected through
/// the implementing class's constructor, mirroring the
/// <see cref="RollGGenerator"/> / <see cref="RollHGenerator"/> pattern.
///
/// <para>Two implementations live in the engine:
/// <list type="bullet">
///   <item><see cref="RollJStubPieGenerator"/> — flat placeholder weights, one
///   per configured transition source. Zero modifier math.</item>
///   <item><see cref="RollJGenerator"/> — real attribute-driven generator
///   (Phase 28). Reads steal origin from the ticket, applies two independent
///   modifiers (coach pace, team athleticism-gap), and selects the matching
///   per-source base-weight set.</item>
/// </list></para>
///
/// <para>The <see cref="Resolver"/> and every harness construction site hold
/// this interface so swapping implementations requires only the construction
/// site — not Roll J itself, not the resolver's routing logic.</para>
/// </summary>
public interface IRollJPieGenerator
{
    /// <summary>
    /// Build the run-or-not pie for the arriving transition possession.
    /// The ticket's <see cref="TransitionContext.Source"/> selects the base
    /// weight set; the ticket's <see cref="TransitionContext.Origin"/> (if
    /// present) selects the steal-split variant; the ticket's
    /// <see cref="TransitionContext.OffenseSide"/> (if present) supplies the
    /// team identity needed for the athleticism-gap modifier.
    /// </summary>
    Pie<TransitionOutcome> Generate(TransitionContext context);
}
