namespace Charm.Engine;

/// <summary>
/// Real, pressure-and-matchup-aware Roll B generator (Phase 13). Halfcourt
/// initiation's turnover and foul rates now reflect the defending team's
/// <b>pressure setting</b> and the <b>slot-weighted aggregate Steals vs.
/// BallHandling matchup</b> across all ten on-court players.
///
/// <para><b>Phase 13 — disruption face only.</b> Pressure has two faces. This
/// generator builds only the disruption face: pressure raises the
/// <c>DeadBallTurnover</c> slice and the <c>Foul</c> slice. The shot-quality
/// face is deliberately deferred. No hooks, no stubs for that face.</para>
///
/// <para><b>Why no selected player (DQ1 — Option B resolution).</b>
/// Roll B runs BEFORE player selection (Roll E). <see cref="PossessionState.SelectedSlot"/>
/// is null at Roll B — there is no individual handler to read <c>BallHandling</c>
/// from and no slot-matched defender to read <c>Steals</c> from. The Phase 12
/// one-on-one contest is therefore structurally impossible here. Instead the
/// generator uses a <b>slot-weighted team aggregate</b> on both sides: the five
/// offensive players' BallHandling values (weighted toward guards) vs. the five
/// defensive players' Steals values (same weights). Guards dominate both aggregates
/// because they have the most opportunities to handle and steal at this stage.
/// See <see cref="MatchupConfig.SlotWeight1"/> through <see cref="MatchupConfig.SlotWeight5"/>.</para>
///
/// <para><b>The matchup model.</b> Two team scores meet: slot-weighted
/// <c>BallHandling</c> (offense) against slot-weighted <c>Steals</c> (defense),
/// both with the same guard-heavy weights [0.35, 0.25, 0.20, 0.12, 0.08] for
/// slots 1–5. The gap (<c>defSteals − offHandling</c>) runs through
/// <see cref="Matchup.GapFn"/> with the shared skill parameters. Pressure then
/// does two jobs: (1) flat, skill-independent lift — a pressing team earns turnovers
/// even with average hands; (2) gates how much the team matchup matters — backed-off
/// defenses see almost no matchup contribution regardless of Steals ratings.</para>
///
/// <para><b>Foul slice: pressure only, no matchup term.</b> Reach-in fouls at
/// halfcourt initiation track defensive aggression, not skill.</para>
///
/// <para><b>Three-way mass split — JumpBall held exactly flat.</b>
/// <c>Proceed</c> absorbs the complement of the two bent arms. <c>JumpBall</c>
/// is pinned at <see cref="RollBConfig.BaseJumpBall"/> exactly.</para>
///
/// <para><b>Changed calibration anchor.</b> (neutral pressure + even aggregate) =
/// today's flat Roll B rates. Not a bug — pressure is the axis that moves them.</para>
///
/// <para><b>Physicality wire — dormant (DQ2 resolution).</b> The <c>physicality</c>
/// parameter is kept in the interface and applied as a secondary nudge on the Foul
/// slice (before renormalization), exactly as the stub did. It is fed 0.0 at both
/// live dispatch sites; at 0.0 it has no effect. It is preserved because physicality
/// and pressure are distinct basketball concepts (how rough the game is vs. how
/// aggressively the defense hounds the ball), and the wire may become a live dial
/// in a future session.</para>
///
/// <para><b>No roster-dependent fallback needed.</b> Roll B reads no
/// <see cref="PossessionState.SelectedSlot"/> and no per-player matchup. The only
/// condition that triggers a fallback is a completely empty roster (isolated test
/// calls). At neutral pressure (5.0) the generator reproduces the flat config
/// baseline exactly, so an "unconfigured" game is byte-for-byte the stub without a
/// special fallback path.</para>
///
/// <para><b>Pressure home — v1 config scalar.</b>
/// <see cref="MatchupConfig.HomePressure"/> / <see cref="MatchupConfig.AwayPressure"/>.
/// <see cref="CoachProfile"/> is the eventual owner; migration: swap
/// <see cref="MatchupConfig.PressureFor"/> to read per-team <c>CoachProfile</c>
/// fields. Only that one call site changes.</para>
///
/// Implements <see cref="IRollBPieGenerator"/>.
/// </summary>
public sealed class RollBGenerator : IRollBPieGenerator
{
    private readonly RollBConfig   _cfgB;
    private readonly MatchupConfig _matchup;
    private readonly GameState     _game;

    public RollBGenerator(RollBConfig cfgB, MatchupConfig matchup, GameState game)
    {
        _cfgB    = cfgB    ?? throw new ArgumentNullException(nameof(cfgB));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));
    }

    public Pie<HalfcourtOutcome> Generate(PossessionState state, double physicality)
    {
        // ── Read both rosters — null-tolerant ───────────────────────────────
        var offRoster = _game.RosterFor(state.Offense);
        var defRoster = _game.RosterFor(state.Defense);
        var offLineup = _game.LineupFor(state.Offense);
        var defLineup = _game.LineupFor(state.Defense);

        var offPlayers = new Player?[]
        {
            offRoster.PlayerAt(offLineup.SlotAt(1)),
            offRoster.PlayerAt(offLineup.SlotAt(2)),
            offRoster.PlayerAt(offLineup.SlotAt(3)),
            offRoster.PlayerAt(offLineup.SlotAt(4)),
            offRoster.PlayerAt(offLineup.SlotAt(5)),
        };
        var defPlayers = new Player?[]
        {
            defRoster.PlayerAt(defLineup.SlotAt(1)),
            defRoster.PlayerAt(defLineup.SlotAt(2)),
            defRoster.PlayerAt(defLineup.SlotAt(3)),
            defRoster.PlayerAt(defLineup.SlotAt(4)),
            defRoster.PlayerAt(defLineup.SlotAt(5)),
        };

        // ── Fallback: completely empty roster ───────────────────────────────
        // A real game always has both rosters populated. Empty-roster calls come
        // from isolated test paths (BatchCheck with a fresh state). At neutral
        // pressure the baseline is reproduced exactly, so this is only needed
        // for zero-population. Partial rosters (some slots null) proceed with
        // normalized weights over the populated slots.
        var offPopulated = 0; foreach (var p in offPlayers) if (p is not null) offPopulated++;
        var defPopulated = 0; foreach (var p in defPlayers) if (p is not null) defPopulated++;
        if (offPopulated == 0 || defPopulated == 0)
            return BuildBaselinePie(physicality);

        // ── Slot-weighted team aggregates ───────────────────────────────────
        // Offense: weighted BallHandling (guards dominate — slot 1 = 35%).
        // Defense: weighted Steals (same guard-heavy weights).
        // Partial-roster: weights renormalize over non-null slots automatically.
        var offHandling  = WeightedAggregate(offPlayers, p => p.BallHandling);
        var defStealers  = WeightedAggregate(defPlayers, p => p.Steals);

        // ── Pressure for the DEFENDING team ─────────────────────────────────
        // Migration path: when CoachProfile is plumbed, swap to
        //   _game.CoachProfileFor(state.Defense).Pressure
        var pressure = _matchup.PressureFor(state.Defense);

        // ── Team disruption shares (turnover + foul bends) ──────────────────
        var actionMass       = _cfgB.BaseProceed + _cfgB.BaseFoul + _cfgB.BaseDeadBallTurnover;
        var baseTurnoverShare = _cfgB.BaseDeadBallTurnover / actionMass;
        var baseFoulShare    = _cfgB.BaseFoul              / actionMass;

        // ── Phase 45: Hustle disruption + defensive foul cost ───────────────
        // Reuse the offPlayers/defPlayers arrays already built above. Team-aggregate
        // Hustle gap (offense mean − defense mean), fixed-denominator-5 discipline.
        // Both nudges feed the pre-saturation shifts inside TeamDisruptionShares so
        // they respect the Roll-B-specific ceilings (never a raw post-bend addition).
        var hustleGap = Matchup.HustleGap(offPlayers, defPlayers);

        // Turnover: -hustleGap is positive when the defense out-hustles → more turnovers.
        var hustlePressureNudge = _matchup.HustlePressureWeight
            * Matchup.HustleGapShift(-hustleGap,
                                     _matchup.HustlePressureSteepness,
                                     _matchup.HustlePressureExponent,
                                     _matchup.HustlePressureScale);

        // Defensive foul cost (defense-only): positive only when the defense out-hustles.
        // hustleGap = offense − defense, so the defense's advantage is max(0, -hustleGap).
        // If the offense has equal or greater Hustle, this is exactly 0.0.
        var defensiveHustleAdvantage = Math.Max(0.0, -hustleGap);
        var defensiveFoulNudge = _matchup.HustleFoulWeight
            * Matchup.HustleGapShift(defensiveHustleAdvantage,
                                     _matchup.HustleFoulSteepness,
                                     _matchup.HustleFoulExponent,
                                     _matchup.HustleFoulScale);

        var (finalToShare, finalFoulShare) = Matchup.TeamDisruptionShares(
            offHandling, defStealers, pressure,
            baseTurnoverShare, baseFoulShare, _matchup,
            hustlePressureNudge, defensiveFoulNudge);

        // ── Overflow guard ───────────────────────────────────────────────────
        // With sane Roll-B-specific ceilings this never fires, but a misconfigured
        // RollBTurnoverCeiling + RollBFoulPressureCeiling > 1 would make Proceed
        // negative. Fail loud rather than silently producing a broken pie.
        if (finalToShare + finalFoulShare >= 1.0)
            throw new InvalidOperationException(
                $"RollBGenerator: finalTurnoverShare ({finalToShare:F6}) + " +
                $"finalFoulShare ({finalFoulShare:F6}) >= 1.0 — " +
                "RollBTurnoverCeiling and RollBFoulPressureCeiling are misconfigured " +
                "(Proceed share would be negative). Lower the ceilings in MatchupConfig.");

        // ── Three-way mass split; JumpBall held exactly flat ────────────────
        var finalProceedShare = 1.0 - finalToShare - finalFoulShare;
        var weights = new Dictionary<HalfcourtOutcome, double>
        {
            [HalfcourtOutcome.Proceed]          = actionMass * finalProceedShare,
            [HalfcourtOutcome.Foul]             = actionMass * finalFoulShare,
            [HalfcourtOutcome.DeadBallTurnover] = actionMass * finalToShare,
            [HalfcourtOutcome.JumpBall]         = _cfgB.BaseJumpBall,  // EXACTLY flat
        };

        // ── Physicality nudge — dormant (fed 0.0 at both dispatch sites) ────
        // Applied after the pressure bend so the two dials are additive on the
        // final Foul weight. At physicality = 0.0 this is a no-op.
        weights[HalfcourtOutcome.Foul] += physicality * _cfgB.PhysicalityFoulNudge;
        var total = weights.Values.Sum();
        foreach (var key in weights.Keys.ToList())
            weights[key] /= total;

        return new Pie<HalfcourtOutcome>(weights, _cfgB.Epsilon);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Slot-weighted average of <paramref name="attr"/> across the five
    /// on-court players. Null slots are skipped and their weights redistributed to
    /// the populated slots (normalized weighted average). Returns 50.0 if no player
    /// is populated — the caller's empty-roster fallback guard prevents this in
    /// normal use.</summary>
    private double WeightedAggregate(Player?[] players, Func<Player, double> attr)
    {
        var weights     = _matchup.SlotWeights;   // [0.35, 0.25, 0.20, 0.12, 0.08]
        var weightedSum = 0.0;
        var totalWeight = 0.0;
        for (var i = 0; i < 5; i++)
        {
            if (players[i] is Player p)
            {
                weightedSum += weights[i] * attr(p);
                totalWeight += weights[i];
            }
        }
        return totalWeight > 0.0 ? weightedSum / totalWeight : 50.0;
    }

    /// <summary>Flat baseline pie — byte-for-byte identical to
    /// <see cref="RollBStubPieGenerator"/>'s output at physicality=0. Returned when
    /// either roster is completely empty (isolated test path).</summary>
    private Pie<HalfcourtOutcome> BuildBaselinePie(double physicality)
    {
        var weights = new Dictionary<HalfcourtOutcome, double>
        {
            [HalfcourtOutcome.Proceed]          = _cfgB.BaseProceed,
            [HalfcourtOutcome.Foul]             = _cfgB.BaseFoul,
            [HalfcourtOutcome.DeadBallTurnover] = _cfgB.BaseDeadBallTurnover,
            [HalfcourtOutcome.JumpBall]         = _cfgB.BaseJumpBall,
        };
        weights[HalfcourtOutcome.Foul] += physicality * _cfgB.PhysicalityFoulNudge;
        var total = weights.Values.Sum();
        foreach (var key in weights.Keys.ToList())
            weights[key] /= total;
        return new Pie<HalfcourtOutcome>(weights, _cfgB.Epsilon);
    }
}
