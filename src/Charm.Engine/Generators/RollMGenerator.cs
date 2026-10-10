namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll M generator. Bends the free-throw board split toward the shared
/// ceiling or floor and returns a seven-way pie whose only moving parts are <c>DefensiveRebound</c>
/// and <c>OffensiveRebound</c> — the five slivers (fouls, out of bounds, jump ball) stay at their
/// config values, and the two board slices still split the same mass.
///
/// <para><b>★ S120 — the board is decided on the lane (O-118).</b> Phase 11 compared all ten men,
/// averaged. The engine now lines the lane up first (<see cref="FreeThrowLane"/>, stamped on the
/// state by the resolver): two offensive men against four defenders, the shooter at the line, three
/// men back. Emmett's ruling: <i>"there is still the natural odds if everyone is equal, and then it is
/// the competition of the 4 v 2, their size, strength, rebounding skill"</i> — compared in TOTALS,
/// laid over the default, where the default stands for a normal lane. So the split reads the lane's
/// body and rebounding totals against the normal lane's (<see cref="FreeThrowLane.OffensiveShare"/>);
/// no hustle and no leap term, and the men back count for nothing.</para>
///
/// <para><b>A state with no lane</b> (a harness call that hands the generator a hand-built state)
/// gets the no-clock lane: the best two of all five on the offense, no shooter, no foul trouble.</para>
///
/// <para><b>Fallback — empty roster ONLY.</b> If either team has nobody seated, or either side's lane
/// is empty, return the flat baseline pie. A real game always has both lanes filled.</para>
///
/// <para><b>Coaching seam (neutral).</b> The crash-glass / get-back sliders will bend the share
/// further when the strategy layer lands, between <see cref="FreeThrowLane.OffensiveShare"/> and the
/// mass split.</para>
///
/// Implements <see cref="IRollMPieGenerator"/>.
/// </summary>
public sealed class RollMGenerator : IRollMPieGenerator
{
    private readonly RollMConfig   _cfg;
    private readonly MatchupConfig _matchup;
    private readonly GameState     _game;

    public RollMGenerator(RollMConfig cfg, MatchupConfig matchup, GameState game)
    {
        _cfg     = cfg     ?? throw new ArgumentNullException(nameof(cfg));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));

        // Cross-config invariant: the natural FT off-share (= baseOff / (baseDef + baseOff))
        // must lie strictly inside [ReboundOffShareFloor, ReboundOffShareCeiling]. If a
        // future config edit pushes the baseline outside the bend band, the tanh direction
        // would invert silently — catch it loud at construction instead.
        // Roll M has ONE source (unlike Roll I's two), so one guard suffices.
        var mass         = _cfg.DefensiveRebound + _cfg.OffensiveRebound;
        var baseOffShare = _cfg.OffensiveRebound / mass;
        if (baseOffShare < _matchup.ReboundOffShareFloor ||
            baseOffShare > _matchup.ReboundOffShareCeiling)
            throw new InvalidOperationException(
                $"RollMGenerator: FT baseline off-share ({baseOffShare:F6}) falls outside " +
                $"[ReboundOffShareFloor={_matchup.ReboundOffShareFloor}, " +
                $"ReboundOffShareCeiling={_matchup.ReboundOffShareCeiling}]. " +
                "A config edit pushed the baseline out of the bend band — the tanh direction " +
                "would invert silently. Fix the config.");

        // ★ S120: the normal lane must be usable (positive totals, a positive anchor) — fail at
        // construction rather than on the first missed free throw.
        _ = FreeThrowLane.Scales(_cfg, _matchup);
    }

    /// <summary>★ S120 — the lane reads the same config the pie does.</summary>
    public RollMConfig LaneConfig => _cfg;

    public Pie<FreeThrowReboundOutcome> Generate(PossessionState state)
    {
        var baseDef      = _cfg.DefensiveRebound;
        var baseOff      = _cfg.OffensiveRebound;
        var mass         = baseDef + baseOff;
        var baseOffShare = baseOff / mass;

        // Fallback — empty roster ONLY. Do NOT key on SelectedSlot (a bonus trip has none).
        if (!AnySeated(state.Offense) || !AnySeated(state.Defense))
            return BuildBaselinePie();

        // ★ S120: the lane the resolver stamped; a hand-built state gets the no-clock lane.
        var lane = state.FreeThrowLane ?? FreeThrowLane.Build(_game, state, shooter: null, _cfg, _matchup);
        if (lane.OffenseLane.Length == 0 || lane.DefenseLane.Length == 0)
            return BuildBaselinePie();

        var (offBody, defBody, offReb, defReb) = lane.Totals(_game, _matchup);
        var finalOffShare = FreeThrowLane.OffensiveShare(offBody, defBody, offReb, defReb, baseOffShare, _cfg, _matchup);

        // [Coaching seam — identity]

        // Split the Def+Off mass by the new off-share; five flat slivers unchanged.
        var newOff = mass * finalOffShare;
        var newDef = mass * (1.0 - finalOffShare);

        var weights = new Dictionary<FreeThrowReboundOutcome, double>
        {
            [FreeThrowReboundOutcome.DefensiveRebound]       = newDef,
            [FreeThrowReboundOutcome.OffensiveRebound]       = newOff,
            [FreeThrowReboundOutcome.LooseBallFoulOnDefense] = _cfg.LooseBallFoulOnDefense,
            [FreeThrowReboundOutcome.LooseBallFoulOnOffense] = _cfg.LooseBallFoulOnOffense,
            [FreeThrowReboundOutcome.OutOfBoundsOffOffense]  = _cfg.OutOfBoundsOffOffense,
            [FreeThrowReboundOutcome.OutOfBoundsOffDefense]  = _cfg.OutOfBoundsOffDefense,
            [FreeThrowReboundOutcome.JumpBall]               = _cfg.JumpBall,
        };

        // Pie ctor validates sum-to-one within Epsilon — the tripwire for any
        // off-by-epsilon error in the mass split.
        return new Pie<FreeThrowReboundOutcome>(weights, _cfg.Epsilon);
    }

    private bool AnySeated(TeamSide side)
    {
        var roster = _game.RosterFor(side);
        var lineup = _game.LineupFor(side);
        for (var n = 1; n <= 5; n++)
            if (roster.PlayerAt(lineup.SlotAt(n)) is not null) return true;
        return false;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Return the flat baseline pie — byte-for-byte identical to
    /// <see cref="RollMStubPieGenerator"/>'s output. Used on the empty-roster
    /// short-circuit path.</summary>
    private Pie<FreeThrowReboundOutcome> BuildBaselinePie()
    {
        var weights = new Dictionary<FreeThrowReboundOutcome, double>
        {
            [FreeThrowReboundOutcome.DefensiveRebound]       = _cfg.DefensiveRebound,
            [FreeThrowReboundOutcome.OffensiveRebound]       = _cfg.OffensiveRebound,
            [FreeThrowReboundOutcome.LooseBallFoulOnDefense] = _cfg.LooseBallFoulOnDefense,
            [FreeThrowReboundOutcome.LooseBallFoulOnOffense] = _cfg.LooseBallFoulOnOffense,
            [FreeThrowReboundOutcome.OutOfBoundsOffOffense]  = _cfg.OutOfBoundsOffOffense,
            [FreeThrowReboundOutcome.OutOfBoundsOffDefense]  = _cfg.OutOfBoundsOffDefense,
            [FreeThrowReboundOutcome.JumpBall]               = _cfg.JumpBall,
        };
        return new Pie<FreeThrowReboundOutcome>(weights, _cfg.Epsilon);
    }
}
