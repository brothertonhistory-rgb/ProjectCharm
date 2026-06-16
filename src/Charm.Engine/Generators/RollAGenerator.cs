namespace Charm.Engine;

/// <summary>
/// Real, press-and-matchup-aware Roll A generator (Phase 14). Backcourt entry's
/// turnover and foul rates now reflect the defending team's
/// <b>full-court press setting</b> and the <b>slot-weighted aggregate Steals vs.
/// BallHandling matchup</b> across all ten on-court players.
///
/// <para><b>Phase 14 — disruption face only.</b> Press has two faces. This generator
/// builds only the disruption face: full-court press raises the <c>Turnover</c>,
/// <c>DefensiveFoul</c>, and <c>OffensiveFoul</c> slices. The shot-quality face
/// (beat-the-press transition shots) is deliberately deferred. No hooks, no stubs.</para>
///
/// <para><b>Full-court press vs. halfcourt pressure (separate dials).</b>
/// Roll B and Roll F read <see cref="MatchupConfig.HomePressure"/> /
/// <see cref="MatchupConfig.AwayPressure"/> — how hard the defense guards in
/// the halfcourt. Roll A reads <see cref="MatchupConfig.HomeFullCourtPress"/> /
/// <see cref="MatchupConfig.AwayFullCourtPress"/> — the distinct, independent
/// tactical decision to press the full court. The two dials share the same
/// PressureNeutral/PressureScale/PressureReferenceShift normalization (which
/// describes the dial, not the roll) but are otherwise completely independent.</para>
///
/// <para><b>Court-state gating.</b> Roll A fires at three moments: initial entry
/// (Frontcourt=false), ResumeInbound (may be either), and SidelineInbound (always
/// Frontcourt=true). When <see cref="PossessionState.Frontcourt"/> is <c>true</c>,
/// the offense has already crossed half — the press is irrelevant. The generator
/// returns the flat config baseline immediately. When <c>false</c>, the full
/// press+matchup computation runs.</para>
///
/// <para><b>Four-way bend (three rising arms).</b> Unlike Roll B (two rising arms),
/// Roll A has three: <c>Turnover</c> (press + team matchup), <c>DefensiveFoul</c>
/// (press only — reach-ins), and <c>OffensiveFoul</c> (press only, ceiling ≈ 15%
/// of DefFoul ceiling). <c>CleanEntry</c> absorbs the complement.
/// <c>JumpBall</c> is pinned exactly flat.</para>
///
/// <para><b>Near-zero floors.</b> Backcourt turnovers and fouls are near-zero
/// without a press. All floors are set low (configurable in
/// <see cref="MatchupConfig"/>).</para>
///
/// <para><b>Why team aggregate, not per-player (same as Phase 13).</b>
/// Roll A runs before player selection (Roll E). <see cref="PossessionState.SelectedSlot"/>
/// is null — no individual handler is known. The slot-weighted aggregate (BallHandling
/// offense, Steals defense, guard-heavy weights [0.35, 0.25, 0.20, 0.12, 0.08]) is
/// the same DQ2 Option B resolution used at Roll B.</para>
///
/// <para><b>Action-mass normalization.</b> Base shares are normalized over
/// <c>actionMass = BaseClean + BaseTurnover + BaseOffFoul + BaseDefFoul</c> (= 0.99).
/// The bends operate on shares, not raw masses, so the neutral anchor (press 5.0 +
/// even aggregate) reproduces the config baseline exactly.</para>
///
/// <para><b>Dormant <c>pressure</c> parameter — stricter than Roll B.</b>
/// The interface parameter is validated with the same [0,1] guard as the stub,
/// then DISCARDED via <c>_ = pressure</c>. Unlike <see cref="RollBGenerator"/>,
/// which applies its dormant <c>physicality</c> as a zero-valued nudge, this
/// generator does not let the placeholder touch the press math at all. Allowing
/// it would create a second, accidental pressure input on top of the real
/// <see cref="MatchupConfig.FullCourtPressFor"/> dial, with no defined semantics.
/// See <see cref="IRollAPieGenerator"/> for the full rationale.</para>
///
/// <para><b>Pressure home — v1 config scalar.</b>
/// <see cref="MatchupConfig.HomeFullCourtPress"/> / <see cref="MatchupConfig.AwayFullCourtPress"/>.
/// <see cref="CoachProfile"/> is the eventual owner; migration: swap
/// <see cref="MatchupConfig.FullCourtPressFor"/> to read per-team CoachProfile fields.
/// Only that one call site changes.</para>
///
/// Implements <see cref="IRollAPieGenerator"/>.
/// </summary>
public sealed class RollAGenerator : IRollAPieGenerator
{
    private readonly RollAConfig   _cfgA;
    private readonly MatchupConfig _matchup;
    private readonly GameState     _game;

    public RollAGenerator(RollAConfig cfgA, MatchupConfig matchup, GameState game)
    {
        _cfgA    = cfgA    ?? throw new ArgumentNullException(nameof(cfgA));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));
    }

    public Pie<EntryOutcome> Generate(PossessionState state, double pressure)
    {
        // ── [0,1] pressure guard — FIRST, before any early return ───────────
        // Preserves the same interface contract as the stub in every code path,
        // including the Frontcourt=true and empty-roster flat-baseline paths.
        if (pressure < 0.0 || pressure > 1.0)
            throw new ArgumentOutOfRangeException(nameof(pressure), pressure,
                "Pressure must be in [0, 1].");
        _ = pressure;   // dormant seam; real press comes from MatchupConfig.FullCourtPressFor

        // ── Court-state gate ─────────────────────────────────────────────────
        // Once the offense has crossed half, the full-court press is irrelevant.
        // Return the flat config baseline immediately.
        if (state.Frontcourt)
            return FlatBaseline();

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

        // ── Fallback: completely empty roster ────────────────────────────────
        // A real game always has both rosters populated. Empty-roster calls come
        // from isolated test paths (BatchCheck with a fresh state). Roll A reads
        // no SelectedSlot and no per-player matchup, so the only condition that
        // requires a fallback is a completely empty roster (zero populated players).
        // Partial rosters (some slots null) proceed with weights renormalized over
        // the populated slots.
        var offPopulated = 0; foreach (var p in offPlayers) if (p is not null) offPopulated++;
        var defPopulated = 0; foreach (var p in defPlayers) if (p is not null) defPopulated++;
        if (offPopulated == 0 || defPopulated == 0)
            return FlatBaseline();

        // ── Slot-weighted team aggregates ────────────────────────────────────
        // Offense: weighted BallHandling (guards dominate — slot 1 = 35%).
        // Defense: weighted Steals (same guard-heavy weights).
        // Same DQ2 Option B resolution as Phase 13 (Roll B).
        var offHandling = WeightedAggregate(offPlayers, p => p.BallHandling);
        var defStealers = WeightedAggregate(defPlayers, p => p.Steals);

        // ── Full-court press for the DEFENDING team ──────────────────────────
        // Distinct from the halfcourt pressure dial (PressureFor) used by Roll B/F.
        // Migration path: when CoachProfile is plumbed, swap to
        //   _game.CoachProfileFor(state.Defense).FullCourtPress
        var fullCourtPress = _matchup.FullCourtPressFor(state.Defense);

        // ── Action-mass normalization ────────────────────────────────────────
        // Shares are normalized over actionMass (= 0.99), not over the full pie.
        // At neutral press + even aggregate the generator reproduces the config
        // baseline exactly: newTO = actionMass × baseTOShare = BaseTurnover = 0.08.
        // Do NOT pass raw BaseTurnover as baseTOShare — that breaks the neutral anchor.
        var actionMass       = _cfgA.BaseClean + _cfgA.BaseTurnover
                             + _cfgA.BaseOffensiveFoul + _cfgA.BaseDefensiveFoul;
        var baseTurnoverShare = _cfgA.BaseTurnover      / actionMass;
        var baseDefFoulShare  = _cfgA.BaseDefensiveFoul / actionMass;
        var baseOffFoulShare  = _cfgA.BaseOffensiveFoul / actionMass;

        // ── Four-way disruption shares (three bends) ─────────────────────────
        var (finalToShare, finalDefFoulShare, finalOffFoulShare) =
            Matchup.EntryDisruptionShares(
                offHandling, defStealers, fullCourtPress,
                baseTurnoverShare, baseDefFoulShare, baseOffFoulShare,
                _matchup);

        // ── Overflow guard ───────────────────────────────────────────────────
        // With sane Roll-A-specific ceilings (sum = 0.315) this never fires.
        // A misconfigured ceiling set with sum >= 1 would make CleanEntry negative.
        // The Load invariant (RollATurnoverCeiling + RollADefFoulCeiling +
        // RollAOffFoulCeiling < 1.0) is the static twin of this runtime guard.
        if (finalToShare + finalDefFoulShare + finalOffFoulShare >= 1.0)
            throw new InvalidOperationException(
                $"RollAGenerator: finalTurnoverShare ({finalToShare:F6}) + " +
                $"finalDefFoulShare ({finalDefFoulShare:F6}) + " +
                $"finalOffFoulShare ({finalOffFoulShare:F6}) >= 1.0 — " +
                "RollATurnoverCeiling + RollADefFoulCeiling + RollAOffFoulCeiling are " +
                "misconfigured (CleanEntry share would be negative). Lower the ceilings.");

        // ── Four-way mass split; JumpBall held exactly flat ──────────────────
        var finalCleanShare = 1.0 - finalToShare - finalDefFoulShare - finalOffFoulShare;
        var weights = new Dictionary<EntryOutcome, double>
        {
            [EntryOutcome.CleanEntry]    = actionMass * finalCleanShare,
            [EntryOutcome.Turnover]      = actionMass * finalToShare,
            [EntryOutcome.DefensiveFoul] = actionMass * finalDefFoulShare,
            [EntryOutcome.OffensiveFoul] = actionMass * finalOffFoulShare,
            [EntryOutcome.JumpBall]      = _cfgA.BaseJumpBall,  // EXACTLY flat
        };

        return new Pie<EntryOutcome>(weights, _cfgA.Epsilon);
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

    /// <summary>Flat config baseline pie — byte-for-byte identical to
    /// <see cref="StubPieGenerator"/>'s output at pressure=0.0. Returned when
    /// the offense has already crossed (Frontcourt=true) or when either roster
    /// is completely empty (isolated test path).</summary>
    private Pie<EntryOutcome> FlatBaseline()
    {
        var weights = new Dictionary<EntryOutcome, double>
        {
            [EntryOutcome.CleanEntry]    = _cfgA.BaseClean,
            [EntryOutcome.Turnover]      = _cfgA.BaseTurnover,
            [EntryOutcome.DefensiveFoul] = _cfgA.BaseDefensiveFoul,
            [EntryOutcome.OffensiveFoul] = _cfgA.BaseOffensiveFoul,
            [EntryOutcome.JumpBall]      = _cfgA.BaseJumpBall,
        };
        return new Pie<EntryOutcome>(weights, _cfgA.Epsilon);
    }
}
