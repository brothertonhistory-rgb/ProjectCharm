namespace Charm.Engine;

/// <summary>
/// Attribute-driven generator for Roll K (offensive-rebound resolution). Tilts the
/// <see cref="OffensiveReboundOutcome.PutBack"/> / <see cref="OffensiveReboundOutcome.ResetOffense"/>
/// mass split based on (a) the rebounder's physical profile and (b) the defensive
/// team's interior deterrence. A per-zone modifier then scales the putback weight
/// down for perimeter misses. All other arms stay flat at config (Phase 32 scope).
///
/// <para><b>Formula summary.</b>
/// <code>
/// offScore  = Σ(PutbackOff*Weight × rebounder attribute)
/// defScore  = self-weighted mean of all five defenders' interior composite
///             (each defender's own score is its weight → elite bigs dominate)
/// gap       = offScore − defScore
/// shift     = GapFn(gap, SkillSteepness, SkillExponent, ReferenceScale)
/// span      = shift ≥ 0 ? (PutbackCeiling − basePutback)
///                       : (basePutback − PutbackFloor)
/// bend      = span × tanh(shift / PutbackReferenceShift)
/// adjusted  = basePutback + bend
/// finalPutback = Clamp(adjusted × zoneMod, PutbackFloor, PutbackCeiling)
/// </code>
/// </para>
///
/// <para><b>Null-rebounder fallback.</b> If <see cref="PossessionState.ReboundSlot"/>
/// is null or the player at that slot is unpopulated, the flat config pie is returned
/// (identical to the stub). This is the DEC-6 equivalent for this generator.</para>
///
/// <para><b>Zone modifier.</b> <see cref="PossessionState.ShotType"/> is still
/// stamped from the original shot when Roll K fires — it is only cleared by
/// <see cref="OffensiveReboundOutcome.ResetOffense"/>, which Roll K hasn't executed
/// yet. Null ShotType (FT board) maps to modifier 1.0.</para>
///
/// <para><b>Self-weighted defense.</b> Each defender's interior composite is used
/// as its own weight in the team mean, so elite rim protectors dominate the team
/// score non-linearly.</para>
/// </summary>
public sealed class RollKGenerator : IRollKPieGenerator
{
    private readonly RollKConfig    _cfg;
    private readonly MatchupConfig  _matchup;
    private readonly GameState      _game;

    /// <summary>Construct the real Roll K generator.</summary>
    /// <param name="cfg">Roll K config — supplies base weights, floor/ceiling, epsilon.</param>
    /// <param name="matchup">Matchup config — supplies GapFn parameters, PutbackReferenceShift,
    /// per-attribute composite weights, and zone modifiers.</param>
    /// <param name="game">Live game state — used to read rosters for both teams.</param>
    public RollKGenerator(RollKConfig cfg, MatchupConfig matchup, GameState game)
    {
        _cfg     = cfg     ?? throw new ArgumentNullException(nameof(cfg));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));
    }

    /// <inheritdoc/>
    public Pie<OffensiveReboundOutcome> Generate(PossessionState state, OffensiveReboundSource source)
    {
        // ── Stage 1: null-rebounder fallback ─────────────────────────────────────
        // If Phase 31 didn't stamp a rebounder (slot is null) or the player at
        // that slot is unpopulated, fall back to the flat config pie — same
        // behavior as RollKStubPieGenerator. This is the DEC-6 equivalent.
        var reboundSlot = state.ReboundSlot;
        if (reboundSlot is null)
            return FlatConfigPie(source);

        var offenseRoster = _game.RosterFor(state.Offense);
        var rebounder     = offenseRoster.PlayerAt(reboundSlot.Value);
        if (rebounder is null)
            return FlatConfigPie(source);

        // ── Stage 2: offense composite ────────────────────────────────────────────
        var offScore = _matchup.PutbackOffStrengthWeight     * rebounder.Strength
                     + _matchup.PutbackOffHeightWeight       * rebounder.Height
                     + _matchup.PutbackOffAthleticismWeight  * rebounder.Athleticism
                     + _matchup.PutbackOffFinishingWeight    * rebounder.Finishing;

        // ── Stage 3: defense composite (self-weighted team mean) ──────────────────
        // Each defender's interior score is used as its own weight so elite
        // rim protectors dominate the team score non-linearly. Only populated
        // slots contribute; unpopulated slots are skipped entirely.
        var defenseRoster = _game.RosterFor(state.Defense);
        var defTotal      = 0.0;
        var defWeightedSq = 0.0;

        for (var i = 0; i < 5; i++)
        {
            var slot      = _game.LineupFor(state.Defense).SlotAt(i + 1);
            var defender  = defenseRoster.PlayerAt(slot);
            if (defender is null) continue;

            var interior = _matchup.PutbackDefRimProtectionWeight * defender.RimProtection
                         + _matchup.PutbackDefHeightWeight        * defender.Height
                         + _matchup.PutbackDefStrengthWeight      * defender.Strength;

            defTotal      += interior;
            defWeightedSq += interior * interior;
        }

        var defScore = defTotal > 0.0
            ? defWeightedSq / defTotal
            : 50.0;   // neutral fallback when no defenders are populated

        // ── Stage 4: gap → tilt via GapFn + tanh ─────────────────────────────────
        // GapFn uses the shared ReferenceScale (same as every other matchup door).
        // PutbackReferenceShift is used ONLY as the tanh denominator — it is NOT
        // the GapFn scale. This mirrors the existing block/foul pattern in Matchup.cs.
        var gap   = offScore - defScore;
        var shift = Matchup.GapFn(gap,
                                   _matchup.SkillSteepness,
                                   _matchup.SkillExponent,
                                   _matchup.ReferenceScale);

        var basePutback = source == OffensiveReboundSource.FreeThrow
            ? _cfg.FreeThrowPutBack
            : _cfg.PutBack;

        var span = shift >= 0.0
            ? _cfg.PutbackCeiling - basePutback
            : basePutback - _cfg.PutbackFloor;

        var bend           = span * Math.Tanh(shift / _matchup.PutbackReferenceShift);
        var adjustedPutback = basePutback + bend;

        // ── Stage 5: zone modifier ────────────────────────────────────────────────
        // ShotType is still stamped from the original shot; null (FT board) → 1.0.
        var zoneMod = state.ShotType switch
        {
            ShotLocation.Three => _matchup.PutbackZoneModifierThree,
            ShotLocation.Long  => _matchup.PutbackZoneModifierLong,
            ShotLocation.Mid   => _matchup.PutbackZoneModifierMid,
            ShotLocation.Short => _matchup.PutbackZoneModifierShort,
            ShotLocation.Rim   => _matchup.PutbackZoneModifierRim,
            null               => 1.0,
            _                  => 1.0,
        };

        var finalPutback = Math.Clamp(adjustedPutback * zoneMod,
                                      _cfg.PutbackFloor,
                                      _cfg.PutbackCeiling);

        // ── Stage 6: mass redistribution ─────────────────────────────────────────
        // Five flat arms are source-selected from config; ResetOffense absorbs the
        // complement. Overflow is caught loudly here (defense in depth — the config
        // Load already enforces the ceiling guard at startup).
        var (flatJump, flatDefFoul, flatOffFoul, flatDeadTO, flatLiveTO) =
            source == OffensiveReboundSource.FreeThrow
                ? (_cfg.FreeThrowJumpBall,
                   _cfg.FreeThrowDefensiveFoul,
                   _cfg.FreeThrowOffensiveFoul,
                   _cfg.FreeThrowDeadBallTurnover,
                   _cfg.FreeThrowLiveBallTurnover)
                : (_cfg.JumpBall,
                   _cfg.DefensiveFoul,
                   _cfg.OffensiveFoul,
                   _cfg.DeadBallTurnover,
                   _cfg.LiveBallTurnover);

        var flatTotal  = flatJump + flatDefFoul + flatOffFoul + flatDeadTO + flatLiveTO;
        var finalReset = Math.Max(0.0, 1.0 - finalPutback - flatTotal);

        if (finalPutback + flatTotal >= 1.0)
            throw new InvalidOperationException(
                $"RollKGenerator overflow: finalPutback ({finalPutback:F4}) + " +
                $"flatTotal ({flatTotal:F4}) >= 1.0. " +
                $"Reduce PutbackCeiling or flat arm weights in config.");

        // ── Stage 7: build pie ────────────────────────────────────────────────────
        return new Pie<OffensiveReboundOutcome>(new Dictionary<OffensiveReboundOutcome, double>
        {
            [OffensiveReboundOutcome.PutBack]          = finalPutback,
            [OffensiveReboundOutcome.JumpBall]         = flatJump,
            [OffensiveReboundOutcome.DefensiveFoul]    = flatDefFoul,
            [OffensiveReboundOutcome.OffensiveFoul]    = flatOffFoul,
            [OffensiveReboundOutcome.DeadBallTurnover] = flatDeadTO,
            [OffensiveReboundOutcome.LiveBallTurnover] = flatLiveTO,
            [OffensiveReboundOutcome.ResetOffense]     = finalReset,
        }, _cfg.Epsilon);
    }

    // ── Flat-config fallback (null-rebounder path) ────────────────────────────
    private Pie<OffensiveReboundOutcome> FlatConfigPie(OffensiveReboundSource source)
    {
        var weights = source switch
        {
            OffensiveReboundSource.LiveBall => new Dictionary<OffensiveReboundOutcome, double>
            {
                [OffensiveReboundOutcome.PutBack]          = _cfg.PutBack,
                [OffensiveReboundOutcome.JumpBall]         = _cfg.JumpBall,
                [OffensiveReboundOutcome.DefensiveFoul]    = _cfg.DefensiveFoul,
                [OffensiveReboundOutcome.OffensiveFoul]    = _cfg.OffensiveFoul,
                [OffensiveReboundOutcome.DeadBallTurnover] = _cfg.DeadBallTurnover,
                [OffensiveReboundOutcome.LiveBallTurnover] = _cfg.LiveBallTurnover,
                [OffensiveReboundOutcome.ResetOffense]     = _cfg.ResetOffense,
            },
            OffensiveReboundSource.FreeThrow => new Dictionary<OffensiveReboundOutcome, double>
            {
                [OffensiveReboundOutcome.PutBack]          = _cfg.FreeThrowPutBack,
                [OffensiveReboundOutcome.JumpBall]         = _cfg.FreeThrowJumpBall,
                [OffensiveReboundOutcome.DefensiveFoul]    = _cfg.FreeThrowDefensiveFoul,
                [OffensiveReboundOutcome.OffensiveFoul]    = _cfg.FreeThrowOffensiveFoul,
                [OffensiveReboundOutcome.DeadBallTurnover] = _cfg.FreeThrowDeadBallTurnover,
                [OffensiveReboundOutcome.LiveBallTurnover] = _cfg.FreeThrowLiveBallTurnover,
                [OffensiveReboundOutcome.ResetOffense]     = _cfg.FreeThrowResetOffense,
            },
            _ => throw new InvalidOperationException(
                $"No Roll K pie for offensive-rebound source '{source}'.")
        };
        return new Pie<OffensiveReboundOutcome>(weights, _cfg.Epsilon);
    }
}
