namespace Charm.Engine;

/// <summary>
/// The shared non-shooting defensive-foul charge-and-fork. Every roll that charges
/// a defensive foul and then routes on the bonus comes here: Roll D (the foul-type
/// node fed by Roll A's entry foul and Roll B's halfcourt foul), Roll I and Roll M
/// (loose-ball foul on the defense, off a field-goal and a free-throw rebound), and
/// Roll J and Roll K (a foul on the transition push / on an offensive-rebound
/// putback attempt). "Many feeders, one node" — exactly like <see cref="JumpBall"/>
/// and <see cref="FoulTracker"/>, this is cross-roll infrastructure that reads
/// <see cref="GameState.Fouls"/> and hands back a <see cref="Continue"/>; it never
/// knows which roll fed it.
///
/// It does the one thing all five did identically, in one place:
///   1. Charge the foul to the fouling team = <see cref="PossessionState.Defense"/>
///      this possession (a <see cref="TeamSide"/> identity, so it lands on the right
///      half-counter with no string mapping).
///   2. Read the bonus the fouling team is now in — a state read, not a roll.
///   3. Fork on it: below bonus -> a CALLER-SUPPLIED continuation; in bonus
///      (OneAndOne / Double) -> always <see cref="ContinuationKind.ResolveFreeThrows"/>.
/// The bonus type rides out on the <see cref="Continue"/> as functional payload the
/// future free-throw node consumes (shot count, reboundability).
///
/// Two things stay CALLER-OWNED, because the five feeders genuinely differ on them
/// and folding them in would be a behavior change wearing a refactor costume:
///
///   • The below-bonus continuation kind (<paramref name="belowBonusKind"/>). Roll D
///     resumes the inbound (<see cref="ContinuationKind.ResumeInbound"/> — the offense
///     keeps the ball after a pre-shot entry/halfcourt foul); the other four go to a
///     sideline throw-in (<see cref="ContinuationKind.ResolveSidelineInbound"/>). In
///     bonus all five are identical, so only the below-bonus arm is a parameter.
///
///   • The descriptive flavor (<paramref name="flavor"/>, optional). Roll D rolls a
///     flavor and carries it on its Continue for observability; the other four carry
///     none today. The node stamps the flavor only when supplied, so Roll D's Continue
///     is byte-for-byte unchanged and the other four stay flavor-free. (Every foul
///     eventually earning its own weighted descriptor set is logged future work; this
///     slot is the home it plugs into — nothing here changes when it lands.)
///
/// What this node does NOT do: attribute the foul to an individual defender. That
/// per-player accumulation is the deferred attribution layer's concern. It charges
/// the TEAM — all the bonus needs — and stops.
/// </summary>
public static class DefensiveFoulCharge
{
    /// <summary>Charge a non-shooting defensive foul to the fouling team, read the
    /// resulting bonus, and fork: below bonus -> <paramref name="belowBonusKind"/>;
    /// in bonus -> <see cref="ContinuationKind.ResolveFreeThrows"/>. The bonus rides
    /// out as <see cref="Continue.Bonus"/> on both arms (it is
    /// <see cref="BonusType.None"/> below bonus, recorded for observability); the
    /// optional <paramref name="flavor"/> rides out as <see cref="Continue.Flavor"/>
    /// when supplied. Mutates <paramref name="game"/> (increments the team foul).</summary>
    public static RollResult Resolve(
        PossessionState state, GameState game,
        ContinuationKind belowBonusKind, FoulFlavor? flavor = null)
    {
        // 1. Charge the foul to the fouling team = the defense this possession.
        var foulingTeam = state.Defense;
        game.Fouls.Increment(foulingTeam);

        // 2. Read the bonus the fouling team is now in — a state read, not a roll.
        var bonus = game.Fouls.BonusFor(foulingTeam);

        // 3. Fork: below bonus -> the caller's kind; in bonus -> free throws. The
        //    bonus type rides as functional payload on both arms (None below bonus);
        //    the flavor rides only when the caller supplied one (null = unset).
        var next = bonus == BonusType.None
            ? belowBonusKind
            : ContinuationKind.ResolveFreeThrows;

        return new Continue(next, state)
        {
            Bonus = bonus,
            Flavor = flavor
        };
    }
}
