namespace Charm.Engine;

/// <summary>
/// Roll D — Non-Shooting Defensive Foul. The shared foul-type node: every roll
/// that produces a generic <c>Foul</c> (Roll A's entry foul, Roll B's halfcourt
/// foul) routes here. "Many feeders, one node" — Roll D never knows who fed it.
///
/// By position in the chain every foul reaching Roll D is PRE-SHOT: no player is
/// selected, no shot is up. So it never classifies offensive-vs-defensive
/// (offensive fouls are Roll C's, as turnovers) or shooting-vs-non-shooting
/// (shooting fouls belong to a future post-player-selection roll). By
/// construction every foul it sees is a non-shooting defensive foul; there are
/// no such branches here, by settled design.
///
/// Roll D does three things:
///   1. Rolls a descriptive FLAVOR (ReachIn / Blocking / OffBall) against a pie.
///      This is theater — logged like turnover-type, it does NOT route.
///   2. Increments the FOULING (defensive) team's foul count for the half.
///   3. Reads the bonus the fouling team is now in and ROUTES on it (a state
///      check, not a roll): None -> resume the inbound (offense keeps the ball);
///      OneAndOne / Double -> resolve free throws.
///
/// The bonus type rides out on the <see cref="Continue"/> as functional payload
/// (not theater): the future free-throw node consumes it to decide everything
/// from shot count to whether a missed front end is reboundable. That single
/// value is the complete contract; Roll D encodes none of those FT rules.
///
/// Unlike Roll C, Roll D's exits are CONTINUEs, not terminals — a foul does not
/// end the possession (the offense either keeps the ball or goes to the line).
/// It follows the uniform roll contract otherwise: receives state + a finished
/// pie, rolls against it, names no successor (the resolver maps the kind).
///
/// What Roll D does NOT do: attribute the foul to an individual defender. That
/// per-player accumulation is a counting-stat concern for the future attribution
/// layer (same as turnover/steal credit). Roll D charges the TEAM — which is all
/// the bonus needs — and stops.
/// </summary>
public static class RollD
{
    public static RollResult Execute(
        PossessionState state, Pie<FoulFlavor> flavorPie, GameState game, IRng rng)
    {
        // 1. Flavor — pure theater. Rolled and carried on the result for logging;
        //    it changes nothing about the route below.
        var flavor = flavorPie.Roll(rng.NextUnitInterval());

        // 2. Charge the foul and route on the bonus — the shared charge-and-fork
        //    (Core/DefensiveFoulCharge). Roll D's below-bonus kind is ResumeInbound
        //    (offense keeps the ball after a pre-shot entry/halfcourt foul), and it
        //    is the one feeder that carries a flavor: the node stamps both the bonus
        //    payload and the rolled flavor onto the returned Continue. The same
        //    flavor draw routes identically; only the foul count (state) decides the
        //    branch.
        return DefensiveFoulCharge.Resolve(
            state, game, ContinuationKind.ResumeInbound, flavor);
    }
}
