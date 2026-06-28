namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll J generator (Phase 28). Replaces
/// <see cref="RollJStubPieGenerator"/> with a pie that applies two INDEPENDENT
/// modifiers on top of per-source base weights, consuming the now-enriched
/// <see cref="TransitionContext"/> ticket.
///
/// <para><b>Base weight selection.</b> Three sources, each with its own weight set
/// in <see cref="RollJConfig"/>:
/// <list type="bullet">
///   <item><see cref="TransitionSource.Rebound"/> — the existing rebound set.</item>
///   <item><see cref="TransitionSource.FreeThrowRebound"/> — the existing conservative set.</item>
///   <item><see cref="TransitionSource.Steal"/> — now split by
///   <see cref="TransitionContext.Origin"/>:
///     <list type="bullet">
///       <item><see cref="StealOrigin.BackcourtVictim"/> → <c>BackcourtVictim*</c>
///       weights (highest Push of any source).</item>
///       <item><see cref="StealOrigin.FrontcourtVictim"/> → <c>FrontcourtVictim*</c>
///       weights (above Rebound, below BackcourtVictim).</item>
///       <item>Null (legacy / test tickets) → old <c>Steal*</c> weights (fallback).</item>
///     </list></item>
/// </list></para>
///
/// <para><b>Two INDEPENDENT modifiers — never pre-fused (the standing rule).</b>
/// Each contributes an additive delta to Push; Settle absorbs the mirror.
/// Turnover, DefensiveFoul, and JumpBall are fixed to their base values.
/// <list type="number">
///   <item><b>Coach pace</b> — live in Phase 30. Reads <see cref="CoachProfile.PaceBias"/>
///   from the offensive coach via <see cref="GameState.CoachFor"/>. Maps [1,10] to a signed
///   lift: neutral (5.0) → 0.0; up-tempo (10) → positive lift; slow (1) → negative lift.
///   Null <see cref="TransitionContext.OffenseSide"/> falls back to the config knob
///   (<see cref="RollJConfig.TeamPaceBias"/> + 5.0 → neutral lift = 0.0).</item>
///   <item><b>Team athleticism-gap</b> — relative:
///   <c>offenseFiveAthl − defenseFiveAthl</c> (mean of active five players' derived
///   <see cref="Player.Athleticism"/>). Read from <see cref="GameState"/> via the
///   <see cref="TransitionContext.OffenseSide"/> stamp on the ticket. Null
///   OffenseSide → gap = 0 (neutral anchor for isolated harness tests). Direction:
///   more-athletic offense → more Push; less athletic → less Push.</item>
/// </list></para>
///
/// <para><b>Regression anchor.</b> At neutral pace (<c>0.0</c>) and neutral
/// athleticism-gap (<c>0</c>), the Rebound and FreeThrowRebound pies reproduce the
/// configured base weights exactly — byte-for-byte backward compatible. The steal
/// baselines are intentionally replaced by the two split variants; exact reproduction
/// of the old single Steal value is NOT expected and is NOT the anchor.</para>
///
/// <para><b>Constructor injection, not per-call parameters.</b>
/// Config, matchup, and game are injected at construction, mirroring
/// <see cref="RollGGenerator"/>. Lineup access goes through
/// <see cref="GameState.LineupFor"/> / <see cref="GameState.RosterFor"/>; no lineup
/// objects are passed to <see cref="Generate"/>.</para>
///
/// Implements <see cref="IRollJPieGenerator"/>.
/// </summary>
public sealed class RollJGenerator : IRollJPieGenerator
{
    private readonly RollJConfig  _cfg;
    private readonly MatchupConfig _matchup;
    private readonly GameState    _game;

    public RollJGenerator(RollJConfig cfg, MatchupConfig matchup, GameState game)
    {
        _cfg     = cfg     ?? throw new ArgumentNullException(nameof(cfg));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));
    }

    /// <inheritdoc cref="IRollJPieGenerator.Generate"/>
    public Pie<TransitionOutcome> Generate(TransitionContext ctx)
    {
        // ── 1. Select base weights by source + steal origin ───────────────────
        double basePush, baseSettle, baseTurnover, baseDefFoul, baseJumpBall;

        switch (ctx.Source)
        {
            case TransitionSource.Rebound:
                basePush     = _cfg.Push;
                baseSettle   = _cfg.Settle;
                baseTurnover = _cfg.Turnover;
                baseDefFoul  = _cfg.DefensiveFoul;
                baseJumpBall = _cfg.JumpBall;
                break;

            case TransitionSource.FreeThrowRebound:
                basePush     = _cfg.FreeThrowPush;
                baseSettle   = _cfg.FreeThrowSettle;
                baseTurnover = _cfg.FreeThrowTurnover;
                baseDefFoul  = _cfg.FreeThrowDefensiveFoul;
                baseJumpBall = _cfg.FreeThrowJumpBall;
                break;

            case TransitionSource.Steal:
                // Split by Origin: BackcourtVictim > FrontcourtVictim >= Rebound.
                // Null origin → old single Steal baseline (legacy/test fallback).
                (basePush, baseSettle, baseTurnover, baseDefFoul, baseJumpBall) = ctx.Origin switch
                {
                    StealOrigin.BackcourtVictim  => (_cfg.BackcourtVictimPush,   _cfg.BackcourtVictimSettle,
                                                     _cfg.BackcourtVictimTurnover, _cfg.BackcourtVictimDefensiveFoul,
                                                     _cfg.BackcourtVictimJumpBall),
                    StealOrigin.FrontcourtVictim => (_cfg.FrontcourtVictimPush,  _cfg.FrontcourtVictimSettle,
                                                     _cfg.FrontcourtVictimTurnover, _cfg.FrontcourtVictimDefensiveFoul,
                                                     _cfg.FrontcourtVictimJumpBall),
                    _                            => (_cfg.StealPush,   _cfg.StealSettle,
                                                     _cfg.StealTurnover, _cfg.StealDefensiveFoul,
                                                     _cfg.StealJumpBall),    // null-origin fallback
                };
                break;

            default:
                throw new InvalidOperationException(
                    $"RollJGenerator: no pie for transition source '{ctx.Source}'. " +
                    "Rebound, FreeThrowRebound, and Steal are modelled.");
        }

        // ── 2. Modifier 1: Coach pace (live in Phase 30, independent) ─────────
        // Normal path: read PaceBias from the offensive coach stamped on the ticket.
        // Fallback / isolated harness path: if OffenseSide is null, fall back to the
        // config knob (TeamPaceBias=0.0 → rawBias=5.0 → mappedPace=0.0 → lift=0.0).
        // _cfg.TeamPaceBias remains a signed fallback knob — NOT a coach-scale knob.
        var rawPaceBias = ctx.OffenseSide.HasValue
            ? _game.CoachFor(ctx.OffenseSide.Value).PaceBias
            : _cfg.TeamPaceBias + 5.0;
        // Map [1,10] to signed pace domain. Neutral (5.0) → 0.0.
        var mappedPace = (rawPaceBias - 5.0) / 5.0;
        var paceLift   = mappedPace * _cfg.PaceScale;

        // ── 3. Modifier 2: Team athleticism-gap (relative, directional) ───────
        // Gap = offenseFiveAthl − defenseFiveAthl (mean of active five).
        // OffenseSide is stamped on the ticket by the consequence helpers; null
        // on hand-constructed test tickets → gap = 0 (regression anchor).
        var athlLift = 0.0;
        if (ctx.OffenseSide.HasValue)
        {
            var offenseSide = ctx.OffenseSide.Value;
            var defenseSide = offenseSide == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
            var offenseAthl = MeanEffectiveAthleticism(offenseSide, isDefense: false);
            var defenseAthl = MeanEffectiveAthleticism(defenseSide, isDefense: true);
            athlLift = (offenseAthl - defenseAthl) * _cfg.AthleticismGapScale;
        }

        // ── 4. Apply both modifiers to Push/Settle (NEVER pre-fused) ─────────
        // Each modifier contributes its OWN additive delta, computed blind to the
        // other (above); only the COMBINED delta is bounded here, as a single
        // transfer between Settle and Push. The transfer is clamped to the room
        // actually available — at most all of Settle may move into Push (+baseSettle),
        // at most all of Push may move into Settle (−basePush) — so BOTH weights stay
        // in [0, basePush+baseSettle] AND the pair's mass is conserved EXACTLY at
        // every gap: modifiedPush + modifiedSettle == basePush + baseSettle always,
        // including the extremes a stronger AthleticismGapScale or a GapFn can reach.
        //
        // This replaces an earlier two-independent-clamps form that conserved mass
        // ONLY when the binding clamp was on Push: a large POSITIVE transfer floored
        // Settle at 0 while Push kept climbing, the five weights summed past 1, and
        // Pie threw (it validates sum-to-1 and refuses — it does not normalise). The
        // bounded transfer removes that latent crash while leaving the other three
        // arms untouched and keeping conditional Push% == modifiedPush at every gap.
        var rawDelta       = paceLift + athlLift;
        var transfer       = Math.Max(-basePush, Math.Min(baseSettle, rawDelta));
        var modifiedPush   = basePush   + transfer;
        var modifiedSettle = baseSettle - transfer;

        var weights = new Dictionary<TransitionOutcome, double>
        {
            [TransitionOutcome.Settle]        = modifiedSettle,
            [TransitionOutcome.Push]          = modifiedPush,
            [TransitionOutcome.Turnover]      = baseTurnover,
            [TransitionOutcome.DefensiveFoul] = baseDefFoul,
            [TransitionOutcome.JumpBall]      = baseJumpBall,
        };

        return new Pie<TransitionOutcome>(weights, _cfg.Epsilon);
    }

    // ── Helper ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mean EFFECTIVE <see cref="Player.Athleticism"/> across the populated active-five
    /// slots for <paramref name="side"/> — authored athleticism discounted by each player's
    /// current fatigue, on the offensive or defensive drop per <paramref name="isDefense"/>.
    /// Returns 0.0 when the roster is unpopulated (isolated harness tests, pre-seating) —
    /// produces a neutral athleticism gap that leaves the configured base weights unchanged,
    /// preserving the regression anchor on any check that does not seat full rosters. Fresh
    /// players read at full athleticism, so an all-fresh transition is unchanged from before.
    /// </summary>
    private double MeanEffectiveAthleticism(TeamSide side, bool isDefense)
    {
        var roster = _game.RosterFor(side);
        var lineup = _game.LineupFor(side);
        var total  = 0.0;
        var count  = 0;
        for (var slot = 1; slot <= 5; slot++)
        {
            var player = roster.PlayerAt(lineup.SlotAt(slot));
            if (player is not null)
            {
                total += _game.Fatigue.EffectiveAthleticism(player, isDefense);
                count++;
            }
        }
        return count > 0 ? total / count : 0.0;
    }
}
