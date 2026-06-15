namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll I generator (Phase 10). Reads both teams' rosters,
/// bends the natural offensive-rebound share toward a ceiling (offense crashes
/// successfully) or floor (defense locks the glass), and returns a seven-way pie
/// whose only moving parts are <c>DefensiveRebound</c> and <c>OffensiveRebound</c>
/// — the five flat slivers (fouls, OOB, jump-ball) stay at their config values.
///
/// <para><b>Phase 10 — matchup-aware rebounding (the glass).</b> The two-touchpoint
/// model (settled design):
/// <list type="number">
///   <item><b>Pre-staging size check (team-vs-team).</b> Compare both teams'
///         <see cref="Matchup.ReboundPhysical"/> composite (height + strength).
///         The bigger team tilts the split its way — a relative comparison so a
///         7-foot stiff helps against a small lineup and hurts against giants.</item>
///   <item><b>Positional-weighted skill shift (intra-team).</b> Within each lineup,
///         posts (high <see cref="Matchup.Postness"/>) carry a positional weight
///         above 1.0 and guards below 1.0 — exactly 1.0 at the lineup mean.
///         Applied to each player's rebounding rating before computing the
///         team-level weighted mean. The offensive side also applies a shooter
///         nerf on <c>Three/Long/Mid</c> (the shooter is already outside and
///         can't crash his own miss as easily).</item>
/// </list>
/// The two shifts sum additively, then bend the off-share through a tanh saturation
/// (the same shape as <see cref="Matchup.BlockWeight"/> and
/// <see cref="Matchup.FoulRate"/>), reaching a ceiling (offense edge) or floor
/// (defense edge) without crossing.</para>
///
/// <para><b>Binary mass reweight (Divergence 3).</b> Unlike Roll G (which
/// renormalizes all five location slices), Roll I only moves the
/// <c>Def+Off</c> mass. The five flat slivers are unchanged — the pie still sums
/// to 1 by construction, and the <see cref="Pie{TOutcome}"/> constructor validates
/// it.</para>
///
/// <para><b>Source selection (Divergence 1).</b> The generator receives both
/// <see cref="PossessionState"/> (for roster and shooter reads) and the
/// <see cref="ReboundSource"/> (for the baseline pie). Live-miss and block have
/// different natural off-shares; each bends from its own baseline.</para>
///
/// <para><b>Fallback path (Divergence vs Roll G).</b> The empty-roster short-circuit
/// fires BEFORE any <c>SelectedSlot</c>/<c>ShotType</c> read. An isolated
/// empty-game generator call (like the one the batch checks use via
/// <see cref="RollIStubPieGenerator"/>) never throws — it returns the flat baseline.
/// A real game always has both teams populated; if it doesn't, returning flat is
/// the correct safe default.</para>
///
/// <para><b>Coaching seam (neutral in v1).</b> The crash-glass / get-back-and-break
/// coaching sliders will bend <c>finalOffShare</c> further from this method's
/// result when the strategy layer lands. v1 is matchup-only; the insertion point
/// is after <c>OffensiveReboundShare</c> returns and before the mass split. No
/// code hook is needed — the seam is documented here and sits at identity.</para>
///
/// <para><b>Roll I itself unchanged.</b> <see cref="RollI.Execute"/> still takes
/// <c>(state, pie, game, rng)</c>; only this generator reads
/// <see cref="GameState"/>.</para>
///
/// Implements <see cref="IRollIPieGenerator"/>.
/// </summary>
public sealed class RollIGenerator : IRollIPieGenerator
{
    private readonly RollIConfig    _cfg;
    private readonly MatchupConfig  _matchup;
    private readonly GameState      _game;

    public RollIGenerator(RollIConfig cfg, MatchupConfig matchup, GameState game)
    {
        _cfg     = cfg     ?? throw new ArgumentNullException(nameof(cfg));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));

        // Cross-config invariant: for both sources, the natural off-share (= baseOff /
        // (baseDef + baseOff)) must lie strictly inside [Floor, Ceiling]. If a future
        // config edit pushes either baseline outside the band, the tanh bend's direction
        // would invert silently — catch it loud at construction instead.
        ValidateBaselineInBand(
            _cfg.DefensiveRebound, _cfg.OffensiveRebound,
            _matchup.ReboundOffShareFloor, _matchup.ReboundOffShareCeiling,
            "live-miss");
        ValidateBaselineInBand(
            _cfg.BlockDefensiveRebound, _cfg.BlockOffensiveRebound,
            _matchup.ReboundOffShareFloor, _matchup.ReboundOffShareCeiling,
            "block");
    }

    private static void ValidateBaselineInBand(
        double baseDef, double baseOff, double floor, double ceiling, string sourceName)
    {
        var mass        = baseDef + baseOff;
        var baseOffShare = baseOff / mass;
        if (baseOffShare < floor || baseOffShare > ceiling)
            throw new InvalidOperationException(
                $"RollIGenerator: {sourceName} baseline off-share ({baseOffShare:F6}) " +
                $"falls outside [ReboundOffShareFloor={floor}, ReboundOffShareCeiling={ceiling}]. " +
                "A config edit pushed the baseline out of the bend band — the tanh direction would " +
                "invert silently. Fix the config.");
    }

    public Pie<ReboundOutcome> Generate(PossessionState state, ReboundSource source)
    {
        // Select the baseline weight dictionary for this source (same as the stub).
        var (baseDef, baseOff) = source switch
        {
            ReboundSource.LiveBall => (_cfg.DefensiveRebound,      _cfg.OffensiveRebound),
            ReboundSource.Block    => (_cfg.BlockDefensiveRebound,  _cfg.BlockOffensiveRebound),
            _ => throw new InvalidOperationException($"No Roll I baseline for source '{source}'.")
        };
        var mass         = baseDef + baseOff;
        var baseOffShare = baseOff / mass;

        // Read both rosters — null-tolerant; may be empty for testing.
        var offRoster  = _game.RosterFor(state.Offense);
        var defRoster  = _game.RosterFor(state.Defense);
        var offLineup  = _game.LineupFor(state.Offense);
        var defLineup  = _game.LineupFor(state.Defense);

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

        // Fallback — BEFORE any SelectedSlot/ShotType read. Two conditions trigger it:
        // (a) Either team has zero populated players (isolated empty-game test calls).
        // (b) SelectedSlot is null (batch-check / harness paths that call through the
        //     resolver with a bare PossessionState and real rosters but no shooter
        //     stamped — these test routing and convergence, not matchup math).
        // Both return the flat baseline: byte-for-byte identical to the stub.
        // A real in-game possession always has both rosters populated AND a slot stamped
        // (Roll E runs before Roll I on every live path).
        var offPopulated = 0; foreach (var p in offPlayers) if (p is not null) offPopulated++;
        var defPopulated = 0; foreach (var p in defPlayers) if (p is not null) defPopulated++;
        if (offPopulated == 0 || defPopulated == 0 || state.SelectedSlot is null)
            return BuildBaselinePie(source);

        // Populated path with a stamped shooter: require shot zone too (Roll G always
        // stamps it before Roll H, so a null ShotType here is a genuine wiring bug).
        var shooterSlot = state.SelectedSlot;   // non-null confirmed above
        var zone = state.ShotType
            ?? throw new InvalidOperationException(
                "RollIGenerator (populated path): ShotType is null. " +
                "Roll G must stamp the shot zone before Roll I is reached.");

        // Identify shooter's index among the offense array (slot-matched).
        // shooterSlot is one of the five lineup positions; find which array index it is.
        var shooterIdx = -1;
        for (var i = 0; i < 5; i++)
            if (offLineup.SlotAt(i + 1) == shooterSlot) { shooterIdx = i; break; }
        // shooterIdx == -1 would mean the stamped slot isn't in the lineup — a wiring bug.
        // The math still works (no slot matches the nerf gate), but log defensively.

        // Compute matchup-bent off-share via the pure static method on Matchup.
        var finalOffShare = Matchup.OffensiveReboundShare(
            offPlayers, defPlayers, shooterIdx, zone, baseOffShare, _matchup);

        // [Coaching seam — v1 identity]
        // When the strategy layer lands, apply the crash-glass / get-back sliders here,
        // bending finalOffShare further toward ceiling (aggressive crash) or floor
        // (conservative get-back). v1: no bend; finalOffShare is the matchup result.

        // Split the Def+Off mass by the new off-share; five flat slivers unchanged.
        var newOff = mass * finalOffShare;
        var newDef = mass * (1.0 - finalOffShare);

        var (flatFoulDef, flatFoulOff, flatOobOff, flatOobDef, flatJump) = source switch
        {
            ReboundSource.LiveBall => (
                _cfg.LooseBallFoulOnDefense,
                _cfg.LooseBallFoulOnOffense,
                _cfg.OutOfBoundsOffOffense,
                _cfg.OutOfBoundsOffDefense,
                _cfg.JumpBall),
            ReboundSource.Block => (
                _cfg.BlockLooseBallFoulOnDefense,
                _cfg.BlockLooseBallFoulOnOffense,
                _cfg.BlockOutOfBoundsOffOffense,
                _cfg.BlockOutOfBoundsOffDefense,
                _cfg.BlockJumpBall),
            _ => throw new InvalidOperationException($"No flat slivers for source '{source}'.")
        };

        var weights = new Dictionary<ReboundOutcome, double>
        {
            [ReboundOutcome.DefensiveRebound]       = newDef,
            [ReboundOutcome.OffensiveRebound]       = newOff,
            [ReboundOutcome.LooseBallFoulOnDefense] = flatFoulDef,
            [ReboundOutcome.LooseBallFoulOnOffense] = flatFoulOff,
            [ReboundOutcome.OutOfBoundsOffOffense]  = flatOobOff,
            [ReboundOutcome.OutOfBoundsOffDefense]  = flatOobDef,
            [ReboundOutcome.JumpBall]               = flatJump,
        };

        // Pie ctor validates sum-to-one within Epsilon — the tripwire for any
        // off-by-epsilon error in the mass split.
        return new Pie<ReboundOutcome>(weights, _cfg.Epsilon);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Return the flat baseline pie — byte-for-byte identical to
    /// <see cref="RollIStubPieGenerator"/>'s output for this source.
    /// Used on the empty-roster short-circuit path.</summary>
    private Pie<ReboundOutcome> BuildBaselinePie(ReboundSource source)
    {
        var weights = source switch
        {
            ReboundSource.LiveBall => new Dictionary<ReboundOutcome, double>
            {
                [ReboundOutcome.DefensiveRebound]       = _cfg.DefensiveRebound,
                [ReboundOutcome.OffensiveRebound]       = _cfg.OffensiveRebound,
                [ReboundOutcome.LooseBallFoulOnDefense] = _cfg.LooseBallFoulOnDefense,
                [ReboundOutcome.LooseBallFoulOnOffense] = _cfg.LooseBallFoulOnOffense,
                [ReboundOutcome.OutOfBoundsOffOffense]  = _cfg.OutOfBoundsOffOffense,
                [ReboundOutcome.OutOfBoundsOffDefense]  = _cfg.OutOfBoundsOffDefense,
                [ReboundOutcome.JumpBall]               = _cfg.JumpBall,
            },
            ReboundSource.Block => new Dictionary<ReboundOutcome, double>
            {
                [ReboundOutcome.DefensiveRebound]       = _cfg.BlockDefensiveRebound,
                [ReboundOutcome.OffensiveRebound]       = _cfg.BlockOffensiveRebound,
                [ReboundOutcome.LooseBallFoulOnDefense] = _cfg.BlockLooseBallFoulOnDefense,
                [ReboundOutcome.LooseBallFoulOnOffense] = _cfg.BlockLooseBallFoulOnOffense,
                [ReboundOutcome.OutOfBoundsOffOffense]  = _cfg.BlockOutOfBoundsOffOffense,
                [ReboundOutcome.OutOfBoundsOffDefense]  = _cfg.BlockOutOfBoundsOffDefense,
                [ReboundOutcome.JumpBall]               = _cfg.BlockJumpBall,
            },
            _ => throw new InvalidOperationException($"No baseline pie for source '{source}'.")
        };
        return new Pie<ReboundOutcome>(weights, _cfg.Epsilon);
    }
}
