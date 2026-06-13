namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll J (transition-entry run-or-not). Returns the
/// run-or-not pie chosen by the <see cref="TransitionContext"/> ticket the
/// possession arrived with — the SAME ticket/station pattern as Roll C, carried on
/// the cross-possession seam instead of a within-possession <see cref="Continue"/>.
///
/// This session THREE contexts are live — <see cref="TransitionSource.Rebound"/>,
/// <see cref="TransitionSource.FreeThrowRebound"/> (Roll M's defensive board, a tamer
/// pie), and <see cref="TransitionSource.Steal"/> (a live theft, the most run-happy
/// pie) — so the generator builds those three. The Steal pie leans hardest to Push of
/// the three (Steal Push &gt; Rebound Push &gt; FreeThrowRebound Push); the <c>_</c>
/// arm fails loud if an unmodelled source ever sneaks in without its pie.
///
/// Flat placeholder weights, no live-wire scalar (like Roll I): the only things
/// that will tilt this pie are Roll J's two deferred, INDEPENDENT modifier seams —
/// rebounder tilt (attribute) and coach tempo (strategy). The real attribute/
/// strategy generator replaces this without touching Roll J or the resolver.
/// </summary>
public sealed class RollJStubPieGenerator
{
    private readonly RollJConfig _config;

    public RollJStubPieGenerator(RollJConfig config) => _config = config;

    /// <summary>Build the run-or-not pie for the arriving transition ticket. The
    /// <see cref="Pie{TOutcome}"/> constructor walks the enum in declaration order
    /// (so slice order is fixed regardless of dictionary order) and validates
    /// sum-to-one, so any misconfigured weights fail loud here.</summary>
    /// <param name="context">The transition ticket's memory; its
    /// <see cref="TransitionContext.Source"/> selects the weight set. Rebound,
    /// FreeThrowRebound, and Steal are the three live sources.</param>
    public Pie<TransitionOutcome> Generate(TransitionContext context)
    {
        var weights = context.Source switch
        {
            TransitionSource.Rebound => new Dictionary<TransitionOutcome, double>
            {
                [TransitionOutcome.Settle]        = _config.Settle,
                [TransitionOutcome.Push]          = _config.Push,
                [TransitionOutcome.Turnover]      = _config.Turnover,
                [TransitionOutcome.DefensiveFoul] = _config.DefensiveFoul,
                [TransitionOutcome.JumpBall]      = _config.JumpBall,
            },

            TransitionSource.FreeThrowRebound => new Dictionary<TransitionOutcome, double>
            {
                [TransitionOutcome.Settle]        = _config.FreeThrowSettle,
                [TransitionOutcome.Push]          = _config.FreeThrowPush,
                [TransitionOutcome.Turnover]      = _config.FreeThrowTurnover,
                [TransitionOutcome.DefensiveFoul] = _config.FreeThrowDefensiveFoul,
                [TransitionOutcome.JumpBall]      = _config.FreeThrowJumpBall,
            },

            TransitionSource.Steal => new Dictionary<TransitionOutcome, double>
            {
                [TransitionOutcome.Settle]        = _config.StealSettle,
                [TransitionOutcome.Push]          = _config.StealPush,
                [TransitionOutcome.Turnover]      = _config.StealTurnover,
                [TransitionOutcome.DefensiveFoul] = _config.StealDefensiveFoul,
                [TransitionOutcome.JumpBall]      = _config.StealJumpBall,
            },

            _ => throw new InvalidOperationException(
                $"No Roll J pie for transition source '{context.Source}'. Rebound, " +
                "FreeThrowRebound, and Steal are modelled.")
        };

        return new Pie<TransitionOutcome>(weights, _config.Epsilon);
    }
}
