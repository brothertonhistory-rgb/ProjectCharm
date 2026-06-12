namespace Charm.Engine;

/// <summary>
/// Roll F — Player Action. The beat right after a player (slot) is selected:
/// decides what the selected player's action BECOMES — a clean shot attempt, a
/// turnover, a non-shooting foul drawn, or a held ball.
///
/// A pure GATE, structurally a clone of Roll B: no terminal outcome, every
/// result is a CONTINUE, because each outcome has downstream work. Three reuse
/// existing shared nodes (turnover -> Roll C, foul -> Roll D, jump ball -> the
/// jump-ball node); one opens the shot pipe (shot type -> Roll G). This is the
/// "many feeders, one node" principle paying off again — Roll F becomes a third
/// feeder into C and D for free.
///
/// NOTE (Session 13): the block left Roll F. A block depends on the shot's zone,
/// which does not exist until Roll G stamps it — so Blocked now lives in Roll H
/// (make/miss) as a per-zone weighted slice. Roll F's old block weight folded
/// into ShotAttempt.
///
/// Follows the uniform roll contract: receives state + a finished pie, rolls
/// against it, returns one typed result, names no successor (the resolver maps
/// the kind). Like Roll B, it is a flat gate that reads nothing off GameState
/// and mutates nothing — so it takes only (state, pie, rng), NOT GameState. The
/// jump-ball RESOLUTION (reading/flipping the arrow) happens in the jump-ball
/// node the resolver owns; the foul's team-foul charge happens in Roll D. Roll F
/// only classifies the action and emits a kind.
///
/// It stamps NOTHING onto PossessionState (unlike Roll E's SelectedSlot or the
/// future Roll G's ShotType). The selected slot stamped by Roll E rides forward
/// on the carried state untouched.
///
/// What it does NOT do: tilt the odds. WHAT makes an action more likely to be
/// blocked or turned over (the handle, defender length/hands, rim protection,
/// shot selection) is the deferred player/attribute model, delivered later as a
/// smarter generator that hands Roll F a non-flat pie over the same enum. Roll F
/// itself never changes when that lands.
/// </summary>
public static class RollF
{
    public static RollResult Execute(PossessionState state, Pie<PlayerActionOutcome> pie, IRng rng)
    {
        var outcome = pie.Roll(rng.NextUnitInterval());

        return outcome switch
        {
            // A clean attempt gets off — proceed DEEPER into the shot sequence.
            PlayerActionOutcome.ShotAttempt =>
                new Continue(ContinuationKind.IntoShotType, state),

            // Live turnover by the handler — hand off to the shared turnover node.
            PlayerActionOutcome.Turnover =>
                new Continue(ContinuationKind.ResolveTurnoverType, state),

            // Non-shooting defensive foul (pre-shot) — hand off to the shared
            // foul node. Roll D charges the team foul and reads the bonus.
            PlayerActionOutcome.NonShootingFoul =>
                new Continue(ContinuationKind.ResolveFoulType, state),

            // Held ball / tie-up — hand off to the existing jump-ball node, which
            // consults the possession arrow.
            PlayerActionOutcome.JumpBall =>
                new Continue(ContinuationKind.ResolveJumpBall, state),

            _ => throw new InvalidOperationException($"Unhandled player-action outcome '{outcome}'.")
        };
    }
}
