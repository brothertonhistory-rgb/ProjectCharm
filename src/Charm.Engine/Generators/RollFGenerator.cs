namespace Charm.Engine;

/// <summary>
/// Real, pressure-and-matchup-aware Roll F generator (Phase 12). The defensive-disruption
/// twin of the block door: rim protection disrupts the shot; ball pressure disrupts the
/// possession before the shot gets off.
///
/// <para><b>Phase 12 — disruption face only.</b> Pressure has two faces. This generator
/// builds only the disruption face: pressure raises the <c>Turnover</c> slice and the
/// <c>NonShootingFoul</c> slice. The shot-quality face — beating high pressure yields
/// scrambled-defense rim busts; backed-off low pressure packs the paint and concedes the
/// perimeter — is deliberately DEFERRED to its own session. Do not confuse the two.
/// The split is cascade-avoidance: one dial bending four things in opposing directions is
/// exactly the interacting-variable trap that sank the project's two prior Python
/// attempts. One face, calibrated, before the other.</para>
///
/// <para><b>The matchup: handling vs. steals, with pressure as the master dial.</b>
/// Two attributes meet: the handler's <see cref="Player.BallHandling"/> against his
/// defender's <see cref="Player.Steals"/>. On top sits <b>pressure</b> — a per-team
/// defensive coach dial (1–10), the first piece of the eventual coach-settings layer.
/// Pressure does two jobs on the steal/turnover slice:
/// <list type="number">
///   <item><b>Flat, skill-independent lift</b> — pressing with bad hands still earns
///         steals off a low baseline. Even a zero matchup advantage produces a positive
///         lift when pressure is above neutral.</item>
///   <item><b>Gates how much the matchup matters</b> — at low pressure even great hands
///         generate almost nothing; pressure scales BOTH the flat lift AND the matchup
///         steepness. Backed off to 2 → few turnovers regardless of personnel. Cranked
///         to 9 → ball-hawks against weak handlers feast.</item>
/// </list>
/// A high-steals defender climbing faster AND a huge handling gap climbing faster are
/// the SAME lever — the gap through <see cref="Matchup.GapFn"/> captures both. One term,
/// not two.</para>
///
/// <para><b>Foul slice: pressure only.</b> The reach-in non-shooting foul (→ Roll D)
/// tracks aggression, not skill. The handling-vs-steals matchup does NOT steepen the
/// foul climb — pressure alone drives it. This is the correct basketball read: you can
/// reach in against anyone if you're playing that aggressively.</para>
///
/// <para><b>Three-way mass split — JumpBall held flat.</b> Roll F's four arms are
/// <c>ShotAttempt</c>, <c>Turnover</c>, <c>NonShootingFoul</c>, <c>JumpBall</c>. Pressure
/// bends the first three; <c>JumpBall</c> is pinned at <see cref="RollFConfig.BaseJumpBall"/>
/// exactly. This is NOT a four-way renormalization — it is a deliberate three-way
/// reweight with jump held out.</para>
///
/// <para><b>Changed calibration anchor (Phase 12 first).</b> Every prior door kept the
/// invariant "even matchup = config baseline." Here that sub-invariant only holds at
/// <em>neutral pressure</em>. The anchor is (neutral pressure + even matchup) = today's
/// flat rates. This is Emmett's basketball call — pressure is the new axis that moves the
/// rates, with the matchup gated underneath.</para>
///
/// <para><b>Defender derivation — local slot match, NOT DefenderPicker.</b> Phase 12 is
/// the second door to read the slot-matched defender (Roll H being the first). Per
/// <see cref="DefenderPicker"/>'s promotion flag, a second consumer technically meets
/// the bar for promoting the defender to a carried <c>PossessionState.DefenderSlot</c>.
/// But the pick is still <em>pure and deterministic</em> — same slot number, defense side
/// — so two doors deriving it independently produces the same defender with zero divergence
/// risk. Promotion is deferred until a door needs a non-deterministic or mismatch-hunting
/// pick. The pick here is: same slot number on <c>state.Defense</c>, derived without
/// routing through <see cref="DefenderPicker.Pick"/>.</para>
///
/// <para><b>Pressure home — v1 config scalar.</b> Pressure lives in
/// <see cref="MatchupConfig.HomePressure"/> / <see cref="MatchupConfig.AwayPressure"/>.
/// <see cref="CoachProfile"/> is the eventual owner (stubbed in Phase 9); migration path:
/// when the coach-settings layer arrives, these two scalars move to per-team
/// <see cref="CoachProfile"/> fields and the generator reads them from there instead.
/// No structural change to this class is needed at that time — only the read site in
/// <see cref="Generate"/> changes.</para>
///
/// <para><b>Fallback paths.</b>
/// <list type="number">
///   <item>Null <see cref="PossessionState.SelectedSlot"/> → flat baseline (wiring guard;
///         Roll E always runs before Roll F on the live path).</item>
///   <item>Slot stamped but handler player absent → flat baseline (DEC-6).</item>
///   <item>Slot-matched defender player absent → flat baseline (DEC-6; cannot compute
///         the one-on-one matchup against a phantom defender).</item>
/// </list>
/// In all fallback cases the returned pie is byte-for-byte identical to
/// <see cref="RollFStubPieGenerator"/>'s output.</para>
///
/// <para><b>Deferred: shot-quality face.</b> The shot-quality effects of pressure
/// (beat-the-press rim busts, packed-paint perimeter concession) are deliberately absent.
/// No hooks, no stubs — that pass comes later as its own calibration session.</para>
///
/// Implements <see cref="IRollFPieGenerator"/>.
/// </summary>
public sealed class RollFGenerator : IRollFPieGenerator
{
    private readonly RollFConfig   _cfgF;
    private readonly MatchupConfig _matchup;
    private readonly GameState     _game;

    public RollFGenerator(RollFConfig cfgF, MatchupConfig matchup, GameState game)
    {
        _cfgF    = cfgF    ?? throw new ArgumentNullException(nameof(cfgF));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));
    }

    public Pie<PlayerActionOutcome> Generate(PossessionState state)
    {
        // ── Fallback 1: no handler stamped yet (wiring guard) ────────────────
        // Roll E must run before Roll F on every live possession. A null slot here
        // means this generator was called from a test path that doesn't stamp one
        // (batch/handoff checks) — return the flat baseline exactly as the stub would.
        if (state.SelectedSlot is null)
            return BuildBaselinePie();

        // ── Resolve handler ─────────────────────────────────────────────────
        // Unwrap Nullable<Slot> — non-null confirmed by the guard above.
        var selectedSlot = state.SelectedSlot.Value;
        var handler = _game.RosterFor(state.Offense).PlayerAt(selectedSlot);
        if (handler is null)       // Fallback 2: slot stamped but player absent (DEC-6)
            return BuildBaselinePie();

        // ── Resolve slot-matched defender ───────────────────────────────────
        // Same slot-match logic as DefenderPicker.Pick (same number, defense side),
        // derived locally so this door does NOT route through DefenderPicker.
        // See Phase 12 doc-comment for why promotion is still deferred.
        var defSlot  = new Slot(state.Defense, selectedSlot.Number);
        var defender = _game.RosterFor(state.Defense).PlayerAt(defSlot);
        if (defender is null)     // Fallback 3: matched defender absent (DEC-6)
            return BuildBaselinePie();

        // ── Pressure for the DEFENDING team ─────────────────────────────────
        // The defense applies pressure to the offense's handler — so we read
        // pressure for the defending side. v1 home: MatchupConfig scalar.
        // Migration path: when CoachProfile is plumbed, swap this read to
        //   _game.CoachProfileFor(state.Defense).Pressure
        // and remove the scalars from MatchupConfig. Call site unchanged.
        var pressure = _matchup.PressureFor(state.Defense);

        // ── Disruption shares via the new Matchup method ────────────────────
        var actionMass       = _cfgF.BaseShotAttempt + _cfgF.BaseTurnover + _cfgF.BaseNonShootingFoul;
        var baseTurnoverShare = _cfgF.BaseTurnover        / actionMass;
        var baseFoulShare    = _cfgF.BaseNonShootingFoul  / actionMass;

        var (finalToShare, finalFoulShare) = Matchup.DisruptionShares(
            handler, defender, pressure, baseTurnoverShare, baseFoulShare, _matchup);

        // ── Overflow guard ───────────────────────────────────────────────────
        // At sane ceiling values this never fires, but a misconfigured ceiling
        // (e.g. TurnoverCeiling + FoulPressureCeiling > 1) would make ShotAttempt
        // negative. Fail loud rather than silently producing a broken pie.
        if (finalToShare + finalFoulShare >= 1.0)
            throw new InvalidOperationException(
                $"RollFGenerator: finalTurnoverShare ({finalToShare:F6}) + " +
                $"finalFoulShare ({finalFoulShare:F6}) >= 1.0 — " +
                "TurnoverCeiling and FoulPressureCeiling are misconfigured " +
                "(ShotAttempt share would be negative). Lower the ceilings in MatchupConfig.");

        // ── Three-way mass split; JumpBall held exactly flat ────────────────
        var finalShotShare = 1.0 - finalToShare - finalFoulShare;
        var weights = new Dictionary<PlayerActionOutcome, double>
        {
            [PlayerActionOutcome.ShotAttempt]     = actionMass * finalShotShare,
            [PlayerActionOutcome.Turnover]        = actionMass * finalToShare,
            [PlayerActionOutcome.NonShootingFoul] = actionMass * finalFoulShare,
            [PlayerActionOutcome.JumpBall]        = _cfgF.BaseJumpBall,   // EXACTLY flat
        };

        // Pie constructor validates sum-to-one within Epsilon — the tripwire for
        // any off-by-epsilon error in the three-way mass split.
        return new Pie<PlayerActionOutcome>(weights, _cfgF.Epsilon);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Flat baseline pie — byte-for-byte identical to
    /// <see cref="RollFStubPieGenerator"/>'s output. Returned on all fallback paths
    /// so that batch/handoff checks that construct bare possession states see the
    /// same rates regardless of which generator is wired in.</summary>
    private Pie<PlayerActionOutcome> BuildBaselinePie()
    {
        var weights = new Dictionary<PlayerActionOutcome, double>
        {
            [PlayerActionOutcome.ShotAttempt]     = _cfgF.BaseShotAttempt,
            [PlayerActionOutcome.Turnover]        = _cfgF.BaseTurnover,
            [PlayerActionOutcome.NonShootingFoul] = _cfgF.BaseNonShootingFoul,
            [PlayerActionOutcome.JumpBall]        = _cfgF.BaseJumpBall,
        };
        return new Pie<PlayerActionOutcome>(weights, _cfgF.Epsilon);
    }
}
