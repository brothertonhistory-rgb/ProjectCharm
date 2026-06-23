namespace Charm.Engine;

/// <summary>
/// Real, press-and-matchup-aware Roll A generator (Phase 15). Backcourt entry's
/// turnover and foul rates now reflect the defending team's per-possession
/// <b>press decision</b> (<see cref="PossessionState.PressMode"/>) and the
/// <b>slot-weighted aggregate three-gap matchup</b> across all ten on-court players.
///
/// <para><b>Phase 15 — disruption face, frequency + Standard mode.</b> The press
/// decision is made upstream in the Resolver once per possession (a single RNG draw
/// against <see cref="MatchupConfig.PressProbabilityFor"/>) and stamped as
/// <see cref="PressMode"/> on the state. This generator is a pure pie builder —
/// it reads the stamp, never rolls RNG itself.</para>
///
/// <para><b>PressMode switch.</b>
/// <list type="bullet">
/// <item><term>None</term><description> → flat config baseline (early return, no
/// roster read).</description></item>
/// <item><term>Standard</term><description> → read six slot-weighted aggregates,
/// call <see cref="Matchup.EntryDisruptionShares"/> with the Standard lift/gate
/// constants (<see cref="MatchupConfig.StandardLift"/> /
/// <see cref="MatchupConfig.StandardGate"/>), build the four-way bent
/// pie.</description></item>
/// <item><term>Desperate</term><description> → throw
/// <see cref="InvalidOperationException"/> (reserved; must never be produced by
/// any live path in Phase 15).</description></item>
/// <item><term>default</term><description> → throw
/// <see cref="ArgumentOutOfRangeException"/> (unrecognized value; wiring
/// bug).</description></item>
/// </list></para>
///
/// <para><b>Court-state gate (survives Phase 15).</b> <c>Frontcourt == true</c>
/// returns the flat config baseline immediately, even under Standard — the press
/// is irrelevant once the offense has crossed half. Gate fires BEFORE the PressMode
/// switch and before any roster read.</para>
///
/// <para><b>Full-court vs. halfcourt (separate layers).</b>
/// Roll B and Roll F read <see cref="MatchupConfig.HomePressure"/> /
/// <see cref="MatchupConfig.AwayPressure"/> — halfcourt pressure. Roll A reads
/// <see cref="PossessionState.PressMode"/> stamped by the Resolver — the per-
/// possession result of the frequency dial. The two layers are fully independent.
/// </para>
///
/// <para><b>Four-way bend (three rising arms).</b> Unlike Roll B (two arms), Roll A
/// has three: <c>Turnover</c> (StandardLift + gate × three-gap matchup),
/// <c>DefensiveFoul</c> (StandardLift only), and <c>OffensiveFoul</c> (StandardLift
/// only, ceiling ≈ 15% of DefFoul ceiling). <c>CleanEntry</c> absorbs the complement.
/// <c>JumpBall</c> is pinned exactly flat.</para>
///
/// <para><b>Why team aggregate, not per-player (same as Phase 13).</b>
/// Roll A runs before player selection (Roll E). <see cref="PossessionState.SelectedSlot"/>
/// is null — no individual handler is known. The slot-weighted aggregate (BallHandling
/// offense, Steals defense, guard-heavy weights [0.35, 0.25, 0.20, 0.12, 0.08]) is
/// the same DQ2 Option B resolution used at Roll B.</para>
///
/// <para><b>Action-mass normalization.</b> Base shares are normalized over
/// <c>actionMass = BaseClean + BaseTurnover + BaseOffFoul + BaseDefFoul</c> (= 0.99).
/// The bends operate on shares, not raw masses, so the neutral anchor (even aggregate,
/// StandardLift at its midpoint) reproduces the expected Standard baseline exactly.
/// </para>
///
/// <para><b>Dormant <c>pressure</c> parameter.</b>
/// The interface parameter is validated with the same [0,1] guard as the stub,
/// then discarded via <c>_ = pressure</c>. The press decision comes from
/// <see cref="PossessionState.PressMode"/>, not from this parameter.</para>
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
        // including the Frontcourt=true and PressMode.None early-return paths.
        if (pressure < 0.0 || pressure > 1.0)
            throw new ArgumentOutOfRangeException(nameof(pressure), pressure,
                "Pressure must be in [0, 1].");
        _ = pressure;   // dormant seam; the press decision comes from state.PressMode

        // ── Court-state gate ─────────────────────────────────────────────────
        // Once the offense has crossed half, the full-court press is irrelevant.
        // Return the flat config baseline immediately — before reading PressMode,
        // before reading rosters.
        if (state.Frontcourt)
            return FlatBaseline();

        // ── PressMode switch ─────────────────────────────────────────────────
        // None  : no press this possession → flat baseline, no roster read.
        // Standard : fixed StandardLift + three-gap matchup within Standard band.
        // Desperate: reserved for the late-game module — must never arrive here.
        // default  : unrecognized value — wiring bug, fail loud.
        switch (state.PressMode)
        {
            case PressMode.None:
                return FlatBaseline();

            case PressMode.Standard:
                break;   // fall through to the full computation below

            case PressMode.Desperate:
                throw new InvalidOperationException(
                    "PressMode.Desperate reached RollAGenerator.Generate — Desperate is " +
                    "reserved for the late-game module and must never be stamped by any live " +
                    "code path in Phase 15. This is a wiring bug.");

            default:
                throw new ArgumentOutOfRangeException(nameof(state),
                    $"Unrecognized PressMode '{state.PressMode}' in RollAGenerator.Generate.");
        }

        // ── Standard mode: read both rosters — null-tolerant ────────────────
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
        // Six aggregates cover the three gap terms: BallHandling vs. Steals (skill),
        // Athleticism composite vs. Athleticism (physical), LengthRating vs. LengthRating (size).
        // All use the same guard-heavy weights [0.35, 0.25, 0.20, 0.12, 0.08].
        // Same DQ2 Option B resolution as Phase 13 (Roll B).
        var offHandling  = WeightedAggregate(offPlayers, p => p.BallHandling);
        var defStealers  = WeightedAggregate(defPlayers, p => p.Steals);
        var offAthletic  = WeightedAggregate(offPlayers, p => _game.Fatigue.EffectiveAthleticism(p, isDefense: false));
        var defAthletic  = WeightedAggregate(defPlayers, p => _game.Fatigue.EffectiveAthleticism(p, isDefense: true));
        var offLength    = WeightedAggregate(offPlayers, p => Matchup.LengthRating(p, _matchup));
        var defLength    = WeightedAggregate(defPlayers, p => Matchup.LengthRating(p, _matchup));

        // ── Action-mass normalization ────────────────────────────────────────
        // Shares are normalized over actionMass (= 0.99), not over the full pie.
        // The Standard bends operate on shares so they compose correctly with the
        // JumpBall pin (which is held at BaseJumpBall regardless of the bend).
        var actionMass       = _cfgA.BaseClean + _cfgA.BaseTurnover
                             + _cfgA.BaseOffensiveFoul + _cfgA.BaseDefensiveFoul;
        var baseTurnoverShare = _cfgA.BaseTurnover      / actionMass;
        var baseDefFoulShare  = _cfgA.BaseDefensiveFoul / actionMass;
        var baseOffFoulShare  = _cfgA.BaseOffensiveFoul / actionMass;

        // ── Four-way disruption shares (three bends) ─────────────────────────
        // StandardLift and StandardGate are fixed config constants — the dial
        // (frequency) was consumed entirely by the upstream press-roll in the
        // Resolver and does NOT enter this math.
        var (finalToShare, finalDefFoulShare, finalOffFoulShare) =
            Matchup.EntryDisruptionShares(
                offHandling, defStealers,
                offAthletic, defAthletic,
                offLength, defLength,
                baseTurnoverShare, baseDefFoulShare, baseOffFoulShare,
                _matchup);

        // ── Overflow guard ───────────────────────────────────────────────────
        // With sane Standard ceilings (sum < 1.0 enforced at Load) this never fires.
        // A misconfigured ceiling set with sum >= 1 would make CleanEntry negative.
        if (finalToShare + finalDefFoulShare + finalOffFoulShare >= 1.0)
            throw new InvalidOperationException(
                $"RollAGenerator: finalTurnoverShare ({finalToShare:F6}) + " +
                $"finalDefFoulShare ({finalDefFoulShare:F6}) + " +
                $"finalOffFoulShare ({finalOffFoulShare:F6}) >= 1.0 — " +
                "StandardTurnoverCeiling + StandardDefFoulCeiling + StandardOffFoulCeiling are " +
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
    /// the offense has already crossed (Frontcourt=true), PressMode is None, or
    /// either roster is completely empty (isolated test path).</summary>
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
