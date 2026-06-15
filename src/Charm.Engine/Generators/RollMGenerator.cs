namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll M generator (Phase 11). Reads both teams' rosters,
/// bends the natural FT-offensive-rebound share toward a ceiling (offense crashes
/// successfully) or floor (defense locks the glass), and returns a seven-way pie
/// whose only moving parts are <c>DefensiveRebound</c> and <c>OffensiveRebound</c>
/// — the five flat slivers (fouls, OOB, jump-ball) stay at their config values.
///
/// <para><b>Phase 11 — matchup-aware FT rebounding ("the FT glass").</b> Roll M is
/// Roll I's two-touchpoint model (<see cref="Matchup.OffensiveReboundShare"/>) applied
/// to a more defensive board population. Two key differences from Roll I:
/// <list type="number">
///   <item><b>No shooter (Divergence 2).</b> Off a field-goal miss the shooter is
///         often near the rim. Off a free throw the shooter is behind the line by
///         rule; everyone else is lined calmly along the lane in assigned box-out
///         spots. The model expresses this via a lower offensive baseline (Roll M's
///         config ≈ 73.5 / 19.7, natural off-share ≈ 0.197 vs Roll I's ≈ 0.290)
///         and by always passing <c>shooterIdx = -1</c> to
///         <see cref="Matchup.OffensiveReboundShare"/> — the shooter nerf is
///         structurally off, because there is no crashing shooter.</item>
///   <item><b>No source selector (Divergence 1).</b> Roll I has two baselines
///         (live-miss and block); Roll M has exactly one (missed final FT). The
///         generator takes a one-arg <c>Generate(state)</c>, not two-arg, and
///         has a single baseline cross-config guard at construction.</item>
/// </list></para>
///
/// <para><b>The two-touchpoint model (reused from Phase 10, unchanged):</b>
/// <list type="number">
///   <item>Pre-staging size check (team-vs-team):
///         <see cref="Matchup.ReboundPhysical"/> composite (height + strength).
///         The bigger team tilts the split its way.</item>
///   <item>Positional-weighted skill shift (intra-team):
///         <see cref="Matchup.Postness"/> → <see cref="Matchup.PositionalWeight"/>
///         — posts above 1.0, guards below 1.0, exactly 1.0 at the lineup mean.
///         Applied to each player's rebounding rating before computing the
///         team-level weighted mean. Shooter nerf is structurally OFF
///         (<c>shooterIdx = -1</c>).</item>
/// </list>
/// Both shifts sum additively, then bend the off-share through a tanh saturation
/// (same shape as <see cref="Matchup.BlockWeight"/> and <see cref="Matchup.FoulRate"/>),
/// reaching a ceiling or floor without crossing.</para>
///
/// <para><b>Binary mass reweight (identical to Phase 10).</b> Only
/// <c>DefensiveRebound</c> and <c>OffensiveRebound</c> move; the five flat slivers
/// (fouls, OOB, jump-ball) stay at their config values — the pie still sums to 1
/// by construction, and the <see cref="Pie{TOutcome}"/> constructor validates it.</para>
///
/// <para><b>Fallback — empty-roster ONLY (Divergence 3).</b> The short-circuit fires
/// before any per-player read: if either team has zero populated players, return the
/// flat baseline pie. Do NOT key the fallback on
/// <see cref="PossessionState.SelectedSlot"/> — Roll M reads neither the slot nor the
/// shot zone, so a null slot is normal (every bonus FT trip has one) and must NOT
/// trigger a fallback. A real in-game FT rebound always has both rosters populated.</para>
///
/// <para><b>Coaching seam (neutral in v1).</b> The crash-glass / get-back sliders
/// will bend <c>finalOffShare</c> further from this method's result when the strategy
/// layer lands. v1 is matchup-only; the insertion point is after
/// <see cref="Matchup.OffensiveReboundShare"/> returns and before the mass split.
/// No code hook is needed — the seam is documented here and sits at identity.</para>
///
/// <para><b>Config reused verbatim.</b> All Phase 10 <see cref="MatchupConfig"/>
/// knobs (composites, positional swing, size/skill split, floor/ceiling, saturation
/// speed) are shared. Roll M adds NO new config fields. The only Roll-M-specific
/// numbers are its seven flat baseline weights in <see cref="RollMConfig"/>.</para>
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
    }

    public Pie<FreeThrowReboundOutcome> Generate(PossessionState state)
    {
        var baseDef      = _cfg.DefensiveRebound;
        var baseOff      = _cfg.OffensiveRebound;
        var mass         = baseDef + baseOff;
        var baseOffShare = baseOff / mass;

        // Read both rosters — null-tolerant; some players may be unpopulated.
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

        // Fallback — empty-roster ONLY (Divergence 3). Do NOT key on SelectedSlot:
        // Roll M never reads the slot or zone (Divergences 2), so a null slot (every
        // bonus FT trip) is normal, not a fallback trigger. A real game always has
        // both rosters populated; the empty-roster path is for isolated test calls.
        var offPopulated = 0; foreach (var p in offPlayers) if (p is not null) offPopulated++;
        var defPopulated = 0; foreach (var p in defPlayers) if (p is not null) defPopulated++;
        if (offPopulated == 0 || defPopulated == 0)
            return BuildBaselinePie();

        // Populated path: bend the off-share via the Phase 10 two-touchpoint model.
        // Pass shooterIdx = -1 (no crashing shooter on the FT glass) and any zone
        // (ShotLocation.Rim is the clean choice — the nerf gate keys on zone AND
        // shooterIdx, and since shooterIdx = -1 the nerf never fires regardless).
        var finalOffShare = Matchup.OffensiveReboundShare(
            offPlayers, defPlayers,
            shooterIdx: -1, zone: ShotLocation.Rim,
            baseOffShare, _matchup);

        // [Coaching seam — v1 identity]
        // When the strategy layer lands, apply the crash-glass / get-back sliders here,
        // bending finalOffShare further toward ceiling (aggressive crash) or floor
        // (conservative get-back). v1: no bend; finalOffShare is the matchup result.

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
