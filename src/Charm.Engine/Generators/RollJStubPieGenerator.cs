namespace Charm.Engine;

/// <summary>
/// Stub pie generator for Roll J (transition-entry run-or-not). Returns the
/// run-or-not pie chosen by the <see cref="TransitionContext"/> ticket the
/// possession arrived with — the SAME ticket/station pattern as Roll C, carried on
/// the cross-possession seam instead of a within-possession <see cref="Continue"/>.
///
/// This session ONE context is live — <see cref="TransitionSource.Rebound"/> — so
/// the generator builds the rebound pie and nothing else. The Steal context's pie
/// (more Push) is a sibling arm added in the steal-feeder session; until then no
/// ticket carries any other source, so no other arm is reachable (the <c>_</c> arm
/// fails loud if one ever sneaks in without its pie).
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
    /// <see cref="TransitionContext.Source"/> selects the weight set. Rebound is the
    /// only live source this session.</param>
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

            _ => throw new InvalidOperationException(
                $"No Roll J pie for transition source '{context.Source}'. Only Rebound is " +
                "modelled this session; Steal lands with the steal-feeder session.")
        };

        return new Pie<TransitionOutcome>(weights, _config.Epsilon);
    }
}
