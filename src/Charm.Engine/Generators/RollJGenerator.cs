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
/// <para><b>S86 — THE OPPORTUNITY SCORE AND THE COACH BAR.</b> The two old additive
/// modifiers (a pace nudge and a five-way team athleticism gap) are RETIRED, not
/// supplemented. Pace was a nudge and is now the GATE; the athleticism composite is
/// replaced by SPEED specifically. Keeping either alongside its replacement would pay
/// twice — pace at both the nudge and the bar, and fast teams at both the composite and
/// the speed race (Speed is one of the five parts of
/// <see cref="Player.Athleticism"/>).</para>
///
/// <para>The players build an OPPORTUNITY; the coach sets the BAR it must clear
/// (ruling C-32/R1 — a grinding team runs only when the break is nearly free):
/// <list type="number">
///   <item><b>escape</b> — the man who won the ball has two routes out, and they
///   OVERLAP (R2): his legs to lead it himself, his outlet to move it ahead. The better
///   route counts fully, the second counts <see cref="RollJConfig.OverlapCredit"/> as
///   much, renormalised — so elite-at-both beats elite-at-one by a couple of points of
///   push, not double. His legs are fatigue-discounted; his passing is not, because
///   passing is not legs (Emmett's ruling, S86).</item>
///   <item><b>race</b> — his four TEAMMATES' speed against all five defenders' speed
///   getting back (R4). Specifically speed, not the athleticism blend. He is excluded
///   from his own team's mean: his legs already paid through escape.</item>
///   <item><b>bar</b> — <see cref="CoachProfile.PaceBias"/> off the offensive coach,
///   on the same [1,10] mapping as before, sets the height the opportunity must clear.
///   This pair of dials is the SINGLE lever a future coaching brain moves (O-57 era);
///   player math and coach bar are never pre-fused (R5).</item>
/// </list>
/// The signed margin (opportunity − bar) runs through a tanh and scales
/// <see cref="RollJConfig.PushSwing"/> into ONE bounded Settle↔Push transfer. Turnover,
/// DefensiveFoul, and JumpBall are fixed to their base values, exactly as before.</para>
///
/// <para><b>The free-throw-rebound source is deliberately EXEMPT</b> (Emmett's ruling,
/// S86). Its base Push is 0.08 against a swing of 0.22, so the new score would pin a
/// slow rebounder to exactly zero and send everyone with legs to ~28% — a source with no
/// middle. The locked oracle's approved archetype tables and its golden fixture cover
/// only the live board and the two steals, so the free-throw board keeps its configured
/// weights untouched and joins the wire in a later session with its own table. Note that
/// its ticket still gets a <see cref="TransitionContext.BallHandlerSlot"/> for free
/// (Roll M's arm shares the <c>DefensiveRebound</c> reason), so the plumbing is already
/// in place when it does.</para>
///
/// <para><b>Regression anchor (the neutral rule).</b> The margin is EXACTLY 0 — so the
/// transfer is exactly 0 and the configured base weights come back untouched — on the
/// free-throw source, on a null <see cref="TransitionContext.OffenseSide"/>, on a null
/// <see cref="TransitionContext.BallHandlerSlot"/>, and when the named seat resolves to no
/// player. Isolated harness checks that construct bare tickets therefore keep asserting
/// the configured rates byte-for-byte.</para>
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

        // ── 2. The opportunity score and the coach bar (S86) ──────────────────
        // THE NEUTRAL RULE, stated once. The margin stays EXACTLY 0.0 — so tanh(0) = 0,
        // the transfer is 0, and the configured base weights come back untouched — in
        // four cases, each a different early-out:
        //   (a) the FREE-THROW rebound source, ruled out of S86's wall (see class doc);
        //   (b) OffenseSide is null — a hand-constructed harness ticket with no game
        //       context, so there is no coach to read and no roster to resolve against;
        //   (c) BallHandlerSlot is null — nobody won the ball on the record. Roll K's
        //       LiveBallTurnover arm rides here (its reason misses the stealer-pick
        //       gate), as does every bare test ticket;
        //   (d) the named seat resolves to no player — an unseated harness roster.
        // This is the regression anchor the isolated Roll J checks depend on.
        var margin = 0.0;
        if (ctx.Source != TransitionSource.FreeThrowRebound &&
            ctx.OffenseSide is { } offenseSide &&
            ctx.BallHandlerSlot is { } handlerSlot)
        {
            var defenseSide = offenseSide == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
            var handler = _game.RosterFor(offenseSide)
                               .PlayerAt(_game.LineupFor(offenseSide).SlotAt(handlerSlot));
            if (handler is not null)
            {
                // ESCAPE — the ball-handler's two OVERLAPPING routes out (ruling R2). His
                // better route counts fully, his second counts OverlapCredit as much, and
                // the pair is renormalised by (1 + OverlapCredit) so the score stays on
                // [0,1] and elite-at-both beats elite-at-one by a couple of points of push
                // rather than double. Max/min are taken on the 0-100 scale and divided
                // once, mirroring the locked oracle's own order of operations.
                // His LEGS carry the fatigue discount; his PASSING does not — passing is
                // not legs (Emmett's ruling, S86), and no skill attribute in the engine
                // reads fatigue.
                var handlerSpeed   = _game.Fatigue.EffectiveSpeed(handler, isDefense: false);
                var handlerPassing = (double)handler.Passing;
                var betterRoute = Math.Max(handlerSpeed, handlerPassing) / 100.0;
                var secondRoute = Math.Min(handlerSpeed, handlerPassing) / 100.0;
                var escape = (betterRoute + _cfg.OverlapCredit * secondRoute)
                           / (1.0 + _cfg.OverlapCredit);

                // RACE — the four TEAMMATES' speed against all five defenders' speed
                // getting back (ruling R4: speed specifically, not the athleticism blend).
                // The ball-handler is excluded from his own team's mean because his legs
                // already paid through escape; counting them again would double-count him.
                // If either side has no populated seats the race is treated as EVEN rather
                // than letting an empty roster read as zero speed — that can only happen on
                // a partially-seated harness bench, never in a game.
                var matesSpeed = MeanEffectiveSpeed(offenseSide, isDefense: false, excludeSlot: handlerSlot);
                var defSpeed   = MeanEffectiveSpeed(defenseSide, isDefense: true,  excludeSlot: null);
                var race = matesSpeed is { } mates && defSpeed is { } defs
                    ? (mates - defs) / 100.0
                    : 0.0;

                var opportunity = _cfg.EscapeWeight * escape
                                + _cfg.RaceWeight   * (race + _cfg.RaceCenter);

                // THE BAR — the coach is the GATE, not a tiebreaker (rulings R1/R5). Same
                // [1,10] → signed mapping the old pace nudge used, so a neutral coach (5.0)
                // sits on BarBase exactly. Up-tempo LOWERS the bar; a grinder RAISES it.
                var mappedPace = (_game.CoachFor(offenseSide).PaceBias - 5.0) / 5.0;
                var bar = _cfg.BarBase - mappedPace * _cfg.BarPaceSwing;

                margin = opportunity - bar;
            }
        }

        // ── 3. One bounded Settle↔Push transfer ───────────────────────────────
        // tanh keeps the response soft near the bar and saturating far from it: a
        // comfortable clear runs, a bad miss almost never does, and no margin can move
        // more than PushSwing. The bound below is UNCHANGED from the pre-S86 wire — it is
        // the mass-conservation guard, not a basketball dial.
        var rawDelta       = _cfg.PushSwing * Math.Tanh(margin / _cfg.MarginScale);
        var transfer       = BoundPushSettleTransfer(basePush, baseSettle, rawDelta);
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
    /// The bounded Settle↔Push transfer — the mass-conservation guard, kept VERBATIM from
    /// the pre-S86 wire (S86 only lifted it out of <see cref="Generate"/> so a check can
    /// call it directly; the arithmetic is untouched).
    ///
    /// <para>The transfer is clamped to the room actually available: at most all of Settle
    /// may move into Push (<c>+baseSettle</c>), at most all of Push may move into Settle
    /// (<c>−basePush</c>). So BOTH weights stay in <c>[0, basePush + baseSettle]</c> AND the
    /// pair's mass is conserved EXACTLY at every margin —
    /// <c>modifiedPush + modifiedSettle == basePush + baseSettle</c> always, including the
    /// saturating extremes.</para>
    ///
    /// <para>This replaced an earlier two-independent-clamps form that conserved mass ONLY
    /// when the binding clamp was on Push: a large POSITIVE transfer floored Settle at 0
    /// while Push kept climbing, the five weights summed past 1, and Pie threw (it
    /// validates sum-to-one and refuses — it does not normalise). Do not "simplify" this
    /// back into two clamps.</para>
    ///
    /// <para>Reachability under S86's configured dials, measured rather than assumed: the
    /// POSITIVE clamp cannot bind (PushSwing 0.22 against the smallest configured Settle,
    /// the backcourt-steal 0.35). The NEGATIVE clamp cannot bind on any source S86 wires
    /// either — the smallest wired base Push is the frontcourt-steal 0.35, still above the
    /// 0.22 swing. It WOULD have bound on the free-throw source (base Push 0.08), which is
    /// one of the reasons that source is exempt.</para>
    /// </summary>
    public static double BoundPushSettleTransfer(double basePush, double baseSettle, double rawDelta) =>
        Math.Max(-basePush, Math.Min(baseSettle, rawDelta));

    /// <summary>
    /// Mean EFFECTIVE <see cref="Player.Speed"/> across the populated active-five slots for
    /// <paramref name="side"/> — authored speed discounted by each player's current fatigue,
    /// on the offensive or defensive drop per <paramref name="isDefense"/>. The fatigue
    /// discount is applied PER PLAYER before averaging, so a single gassed man is not
    /// diluted by four fresh ones.
    ///
    /// <para><paramref name="excludeSlot"/> drops one seat from the mean — used to keep the
    /// ball-handler out of his own teammates' average, since his legs already pay through
    /// the escape term.</para>
    ///
    /// <para>Empty seats are skipped exactly as the retired athleticism mean skipped them.
    /// Returns <c>null</c> when NO seat is populated, which the caller reads as "treat the
    /// race as even" — a partially-seated harness bench must not manufacture a speed gap out
    /// of an empty roster. A real game always seats five.</para>
    /// </summary>
    private double? MeanEffectiveSpeed(TeamSide side, bool isDefense, int? excludeSlot)
    {
        var roster = _game.RosterFor(side);
        var lineup = _game.LineupFor(side);
        var total  = 0.0;
        var count  = 0;
        for (var slot = 1; slot <= 5; slot++)
        {
            if (slot == excludeSlot) continue;
            var player = roster.PlayerAt(lineup.SlotAt(slot));
            if (player is not null)
            {
                total += _game.Fatigue.EffectiveSpeed(player, isDefense);
                count++;
            }
        }
        return count > 0 ? total / count : null;
    }
}
