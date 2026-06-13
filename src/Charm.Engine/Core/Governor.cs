namespace Charm.Engine;

/// <summary>One resolved possession, recorded for validation and observability.</summary>
/// <param name="Number">The possession's monotonic id (the accounting anchor).</param>
/// <param name="Offense">Who had the ball this possession.</param>
/// <param name="Defense">Who defended (the other side).</param>
/// <param name="Entry">How this possession started. A Transition entry off a defensive
/// rebound now enters Roll J (the resolver routes it on the carried Rebound context);
/// every other entry enters Roll A.</param>
/// <param name="EndedOnTerminal">True if it reached a terminal; false if it parked at a stub.</param>
/// <param name="EndLabel">The terminal reason, or "parked:{stub}".</param>
/// <param name="Applied">The consequence used to spawn the NEXT possession (the
/// terminal's own consequence, or the default flip on a park).</param>
/// <param name="Points">Points scored on this possession (credited to <see cref="Offense"/>).</param>
/// <param name="Elapsed">Seconds this possession actually drained from the half clock —
/// the raw draw capped at the time remaining in the half, so the cap makes each half
/// sum to exactly <see cref="GovernorConfig.HalfSeconds"/>.</param>
/// <param name="Half">Which half this possession ran in (1 or 2).</param>
/// <param name="EndOfHalfIntent">Which end-of-half intent fired on this possession,
/// or <c>null</c> if the possession started with a full shot clock or more left
/// (the common case). Set to <see cref="EndOfHalfIntent.HoldShootLast"/>,
/// <see cref="EndOfHalfIntent.ShootEarly"/>, or <see cref="EndOfHalfIntent.NoShot"/>
/// only when <c>halfRemaining &lt; HoldThresholdSeconds</c> at the start of the
/// possession. Null on every normal possession; non-null only at the end of a half.</param>
public sealed record PossessionRecord(
    int Number,
    TeamSide Offense,
    TeamSide Defense,
    EntryType Entry,
    bool EndedOnTerminal,
    string EndLabel,
    PossessionConsequence Applied,
    int Points,
    double Elapsed,
    int Half,
    EndOfHalfIntent? EndOfHalfIntent);

/// <summary>The result of a Governor run — everything the harness validates and prints.</summary>
/// <param name="Possessions">Every resolved possession, in order. Count == the cap.</param>
/// <param name="TerminalEnded">How many ended on a real terminal.</param>
/// <param name="Parked">How many parked at a stub (and flipped on the default consequence).</param>
/// <param name="TotalSeconds">Total game time drained in seconds — the sum of each possession's
/// elapsed time (each capped at its half's remaining time). Equals
/// <see cref="GovernorConfig.Halves"/> × <see cref="GovernorConfig.HalfSeconds"/> when the
/// countdown completes normally.</param>
/// <param name="PerStubParks">Per-stub park breakdown: stub destination -> count. This
/// quantifies the FT / offensive-rebound / etc. volume still flowing through placeholder
/// flips — the point of printing it.</param>
public sealed record GovernorRunResult(
    IReadOnlyList<PossessionRecord> Possessions,
    int TerminalEnded,
    int Parked,
    double TotalSeconds,
    IReadOnlyDictionary<string, int> PerStubParks);

/// <summary>
/// The THIN Governor. It turns "resolve ONE possession" into "play a sequence of
/// possessions," and does nothing else. It owns the loop; it never picks a roll or
/// reaches inside a possession — it drops a START STATE at the top of the chain (via
/// <see cref="Resolver.RunPossession"/>) and reads what comes back.
///
/// <para>For each possession it asks the resolver to run it, then:</para>
/// <list type="bullet">
///   <item>If the possession ENDED ON A TERMINAL, it reads that terminal's
///   <see cref="PossessionConsequence"/> — who has the ball next and how that
///   possession starts.</item>
///   <item>If the possession PARKED at a stub (the resolver returns no terminal), it
///   applies the DEFAULT consequence: ball to the other team, dead-ball restart at
///   Roll A. This is deliberately wrong basketball (a parked FT possession should
///   resolve points and decide the next possession off the last free throw), kept
///   flat exactly like score = 0; it is replaced at this same seam when that pipe
///   resolves for real. The key property: this is ONE uniform path for EVERY stub
///   (keyed only on "no terminal"), so no per-stub branch exists to forget — the
///   Session-14 "only handled one landing" bug class cannot recur.</item>
/// </list>
///
/// <para>Either way it spawns the next possession — threading the consequence's
/// offense, entry, AND transition context onto the new start state — increments the
/// count, and loops until the config'd possession cap. The entry tag is now honored by
/// the resolver: a Transition consequence off a defensive rebound carries the Rebound
/// context and enters Roll J; every other entry enters Roll A. EVERY possession —
/// terminal or parked — produces exactly one next possession, so the count never
/// leaks.</para>
///
/// <para>The cross-possession invariants it must NOT disturb — the possession arrow,
/// the team-foul counts, and the lineups — all live on the shared <see cref="GameState"/>
/// and persist automatically because the same resolver (holding the same game) runs
/// every possession. The Governor never resets or clobbers them; it reaches the score
/// field to credit the offense with the resolver's tallied points each possession.</para>
///
/// <para>PROVISIONAL (see design.md teardown contract): the temp-route-all-to-Roll-A
/// and the parked→default-flip rule. PERMANENT: the loop shape — read the consequence
/// off the terminal (or the default on a park) and spawn — which a real game layer
/// swaps the guts behind without touching the seam.</para>
///
/// <para>END-OF-HALF INTENT: when a possession starts with less than a full shot clock
/// left (<see cref="EndOfHalfConfig.HoldThresholdSeconds"/>), the Governor draws a
/// three-way intent — <see cref="EndOfHalfIntent.HoldShootLast"/> (milk the clock;
/// force elapsed to the whole remaining time; the resolver still runs for points),
/// <see cref="EndOfHalfIntent.ShootEarly"/> (normal-tempo possession; opponent may get
/// a return trip), or <see cref="EndOfHalfIntent.NoShot"/> (run out the clock; no
/// resolver call; zero points; half ends). On all other possessions intent is null and
/// the S29 base-clock path runs byte-for-byte. The per-half drain invariant
/// (<see cref="GovernorConfig.HalfSeconds"/> per half) holds for every intent value.
/// Score-blind and tempo-blind — the split is a flat pie; a future score-aware layer
/// replaces it with a context-selected generator.</para>
/// </summary>
public sealed class Governor
{
    private readonly Resolver _resolver;
    private readonly GameState _game;
    private readonly GovernorConfig _cfg;
    private readonly RollClockConfig _clock;
    private readonly IRng _rng;
    private readonly EndOfHalfConfig _endOfHalf;
    private readonly Pie<EndOfHalfIntent> _endOfHalfPie;

    public Governor(Resolver resolver, GameState game, GovernorConfig cfg, RollClockConfig clock, IRng rng, EndOfHalfConfig endOfHalf)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _endOfHalf = endOfHalf ?? throw new ArgumentNullException(nameof(endOfHalf));
        _endOfHalfPie = new Pie<EndOfHalfIntent>(
            new Dictionary<EndOfHalfIntent, double>
            {
                [EndOfHalfIntent.HoldShootLast] = endOfHalf.HoldShootLast,
                [EndOfHalfIntent.ShootEarly]    = endOfHalf.ShootEarly,
                [EndOfHalfIntent.NoShot]        = endOfHalf.NoShot,
            },
            endOfHalf.Epsilon);
    }

    /// <summary>Run two halves against the clock, starting from <paramref name="first"/>.
    /// Each possession draws its elapsed time from a truncated normal; the Governor counts
    /// down from <see cref="GovernorConfig.HalfSeconds"/> per half and stops when both
    /// halves are spent. Returns the full record for validation.
    ///
    /// <para>When a possession starts with less than a full shot clock left, the Governor
    /// draws an end-of-half intent: <see cref="EndOfHalfIntent.HoldShootLast"/> forces
    /// elapsed to the whole remaining half time (half ends, no return trip); 
    /// <see cref="EndOfHalfIntent.ShootEarly"/> draws elapsed normally (opponent may
    /// return); <see cref="EndOfHalfIntent.NoShot"/> skips the resolver entirely (zero
    /// points, clock fully drained). Null intent = the S29 base-clock path, byte-for-byte.
    /// The per-half drain invariant holds for all values: each half sums to exactly
    /// <see cref="GovernorConfig.HalfSeconds"/>.</para></summary>
    public GovernorRunResult Run(PossessionState first)
    {
        var state = first;
        var records = new List<PossessionRecord>(_cfg.PossessionCap);
        var perStubParks = new Dictionary<string, int>();
        var terminalEnded = 0;
        var parked = 0;
        var totalSeconds = 0.0;
        var half = 1;
        var halfRemaining = _cfg.HalfSeconds;
        var guard = 0;

        while (half <= _cfg.Halves)
        {
            if (++guard > _cfg.PossessionCap)
                throw new InvalidOperationException(
                    $"Governor safety guard exceeded {_cfg.PossessionCap} possessions — the clock " +
                    "is not draining (check HalfSeconds and possession-time config).");

            // End-of-half intent: drawn only when this possession starts with less than
            // a full shot clock left; null on every normal possession (the common case).
            // When null, the S29 base-clock behavior runs byte-for-byte. NoShot
            // short-circuits the resolver; HoldShootLast and ShootEarly run it normally
            // and differ only in how much clock drains. §2a note: this draw consumes one
            // unit from _rng per end-of-half possession, so the rng stream (and therefore
            // the exact possession count and score) will differ from the S29 output once
            // the first intent fires — expected; the harness asserts bands, not exact values.
            EndOfHalfIntent? intent = halfRemaining < _endOfHalf.HoldThresholdSeconds
                ? _endOfHalfPie.Roll(_rng.NextUnitInterval())
                : null;

            PossessionConsequence consequence;
            bool endedOnTerminal;
            string endLabel;
            int pointsThisPossession;
            double applied;

            if (intent == EndOfHalfIntent.NoShot)
            {
                // No resolver call: the offense ran out the clock without a shot.
                // Synthesize a zero-point, no-terminal possession. Does NOT increment
                // terminalEnded or parked — it is a third class (§2a discipline: handle
                // every branch the loop can reach; the count assertion in the harness
                // accounts for it as noShotCount separately).
                endedOnTerminal = false;
                endLabel = "endOfHalf:NoShot";
                consequence = PossessionConsequence.DeadBallTo(state.Defense);
                pointsThisPossession = 0;
                applied = halfRemaining;   // drains the rest of the half to 0
            }
            else
            {
                // Normal / HoldShootLast / ShootEarly: run the resolver.
                var outcome = _resolver.RunPossession(state);

                if (outcome.EndedOn is { } term)
                {
                    // Ended on a terminal: read its consequence directly.
                    endedOnTerminal = true;
                    consequence = term.Consequence;
                    endLabel = term.Reason;
                    terminalEnded++;
                }
                else
                {
                    // Parked at a stub: no terminal, no consequence. Apply the DEFAULT —
                    // ball to the other team, dead-ball restart at Roll A. One uniform
                    // path for every stub.
                    endedOnTerminal = false;
                    consequence = PossessionConsequence.DeadBallTo(state.Defense);
                    endLabel = $"parked:{outcome.Destination}";
                    parked++;
                    perStubParks[outcome.Destination] =
                        perStubParks.GetValueOrDefault(outcome.Destination) + 1;
                }

                // Time: HoldShootLast forces the whole remaining clock (the offense
                // milked — forced even if the resolver produced an invariant terminal
                // elapsed, because the milk intent dominates). ShootEarly and normal
                // draw elapsed the S29 way (invariant terminal wins; otherwise truncated-
                // normal draw) and cap at halfRemaining.
                var rawElapsed = outcome.EndedOn?.ElapsedSeconds ?? DrawPossessionSeconds(outcome.ShotClockPeriods);
                applied = intent == EndOfHalfIntent.HoldShootLast
                    ? halfRemaining
                    : Math.Min(rawElapsed, halfRemaining);

                pointsThisPossession = outcome.Points;
            }

            // Shared by all three intent values + normal possessions.
            halfRemaining -= applied;
            totalSeconds  += applied;

            // Score write: points credit the offense (state.Offense). Zero for NoShot.
            if (state.Offense == TeamSide.Home) _game.HomeScore += pointsThisPossession;
            else _game.AwayScore += pointsThisPossession;

            records.Add(new PossessionRecord(
                state.PossessionNumber, state.Offense, state.Defense, state.Entry,
                endedOnTerminal, endLabel, consequence, pointsThisPossession, applied, half, intent));

            // Spawn possession N+1 from the consequence: offense named by it, defense
            // the other side, number +1, entry the consequence's tag, AND the transition
            // context ticket it carries (non-null only off a defensive rebound this
            // session — that ticket is what makes the resolver route the new possession
            // to Roll J instead of Roll A). Per-possession facts (slot / zone / result)
            // reset to null — a fresh possession. The final iteration spawns a state that
            // is never run (the loop exits), which is harmless.
            var nextOffense = consequence.NextOffense;
            state = new PossessionState(
                PossessionNumber: state.PossessionNumber + 1,
                Offense: nextOffense,
                Defense: Other(nextOffense),
                Entry: consequence.NextEntry,
                TransitionContext: consequence.TransitionContext);

            // Half boundary: when this half's clock is spent, advance to the next half.
            // Only the clock resets — fouls, arrow, and lineups carry (no halftime reset
            // this session, matching the existing persistence checks).
            if (halfRemaining <= 0.0)
            {
                half++;
                halfRemaining = _cfg.HalfSeconds;
            }
        }

        return new GovernorRunResult(records, terminalEnded, parked, totalSeconds, perStubParks);
    }

    /// <summary>Sum the truncated-normal draws for a possession's shot-clock periods:
    /// period 1 on the full clock, each offensive-rebound reset on the 20s clock (center
    /// and sd scaled to the shorter window). Outcome-blind — the draw never depends on how
    /// a period ended; an invariant terminal (handled by the caller) overrides this.</summary>
    private double DrawPossessionSeconds(int shotClockPeriods)
    {
        var periods = Math.Max(1, shotClockPeriods);
        var seconds = ClockDraw.Sample(_rng, _clock.Center, _clock.StdDev, _clock.Floor, _clock.FullClockSeconds);
        var resetScale = _clock.ResetClockSeconds / _clock.FullClockSeconds;
        for (var p = 2; p <= periods; p++)
            seconds += ClockDraw.Sample(_rng, _clock.Center * resetScale, _clock.StdDev * resetScale, _clock.Floor, _clock.ResetClockSeconds);
        return seconds;
    }

    private static TeamSide Other(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
}
