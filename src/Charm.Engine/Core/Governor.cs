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
/// <param name="Fga">Field-goal attempts credited to this possession — the six-outcome
/// box-score count (all <see cref="ShotResult"/> values except
/// <see cref="ShotResult.MissFouled"/>, which sends the shooter to the line with no
/// FGA charged). Credited to <see cref="Offense"/>. Zero on NoShot possessions.</param>
/// <param name="Fgm">Field goals made on this possession —
/// <see cref="ShotResult.Made"/> and <see cref="ShotResult.MadeAndFouled"/> only.
/// The and-1 basket counts; the bonus free throw does not. Credited to
/// <see cref="Offense"/>. Zero on NoShot possessions.</param>
/// <param name="ThreePa">Three-point attempts on this possession — the subset of
/// <see cref="Fga"/> from the <see cref="ShotLocation.Three"/> zone. A fouled missed
/// three is NOT a 3PA. Zero on NoShot possessions.</param>
/// <param name="ThreePm">Three-point makes on this possession — the subset of
/// <see cref="Fgm"/> from the <see cref="ShotLocation.Three"/> zone. Zero on NoShot
/// possessions.</param>
/// <param name="ShotResolutions">Total Roll H resolutions on this possession — all
/// seven <see cref="ShotResult"/> outcomes. Equals <see cref="Fga"/> +
/// <see cref="MissFouled"/> by construction; exists solely for the denominator-guard
/// mechanical check. Zero on NoShot possessions.</param>
/// <param name="MissFouled">Count of <see cref="ShotResult.MissFouled"/> resolutions
/// on this possession — the one outcome excluded from <see cref="Fga"/>. Zero on
/// NoShot possessions.</param>
/// <param name="Fta">Free-throw attempts on this possession — every Roll L spin across
/// all FT trips (bonus and shooting-foul). Zero on NoShot possessions.</param>
/// <param name="Ftm">Free throws made on this possession — each Roll L spin that
/// resolved to a make. Zero on NoShot possessions.</param>
/// <param name="OrbChances">Offensive-rebound chances on this possession — Roll I and
/// Roll M resolutions that ended in either <see cref="ReboundOutcome.DefensiveRebound"/>
/// or <see cref="ReboundOutcome.OffensiveRebound"/> (secured boards only; fouls, OOB,
/// and jump-ball excluded). Zero on NoShot possessions.</param>
/// <param name="OrbWon">Offensive rebounds won on this possession — Roll I or Roll M
/// resolutions where the offense secured the board. The team offensive-rebound rate is
/// <c>OrbWon / OrbChances</c> across possessions. Zero on NoShot possessions.</param>
/// <param name="OrbWon">Offensive rebounds won on this possession — Roll I or Roll M
/// resolutions where the offense secured the board. The team offensive-rebound rate is
/// <c>OrbWon / OrbChances</c> across possessions. Zero on NoShot possessions.</param>
/// <param name="RimFga">Rim-zone field-goal attempts on this possession (the
/// <see cref="ShotLocation.Rim"/> subset of <see cref="Fga"/>). Zero on NoShot.</param>
/// <param name="RimFgm">Rim-zone field goals made on this possession. Zero on NoShot.</param>
/// <param name="ShortFga">Short-zone field-goal attempts on this possession. Zero on NoShot.</param>
/// <param name="ShortFgm">Short-zone field goals made on this possession. Zero on NoShot.</param>
/// <param name="MidFga">Mid-zone field-goal attempts on this possession. Zero on NoShot.</param>
/// <param name="MidFgm">Mid-zone field goals made on this possession. Zero on NoShot.</param>
/// <param name="LongFga">Long-two-zone field-goal attempts on this possession. Zero on NoShot.</param>
/// <param name="LongFgm">Long-two-zone field goals made on this possession. Zero on NoShot.</param>
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
    EndOfHalfIntent? EndOfHalfIntent,
    int Fga = 0,
    int Fgm = 0,
    int ThreePa = 0,
    int ThreePm = 0,
    // Session 36: displacement-context bucket counts — read-only observation
    // instrumentation copied through from RoutingOutcome. Every FGA whose state
    // carried a populated ShotDisplacementLevel lands in exactly one bucket;
    // null-level attempts are excluded, so Low+Mid+High <= Fga.
    int DispLowFga = 0,
    int DispLowThreePa = 0,
    int DispLowThreePm = 0,
    int DispMidFga = 0,
    int DispMidThreePa = 0,
    int DispMidThreePm = 0,
    int DispHighFga = 0,
    int DispHighThreePa = 0,
    int DispHighThreePm = 0,
    int ShotResolutions = 0,
    int MissFouled = 0,
    int Fta = 0,
    int Ftm = 0,
    int OrbChances = 0,
    int OrbWon = 0,
    int RimFga = 0,
    int RimFgm = 0,
    int ShortFga = 0,
    int ShortFgm = 0,
    int MidFga = 0,
    int MidFgm = 0,
    int LongFga = 0,
    int LongFgm = 0,
    int Slot1Fga = 0,
    int Slot2Fga = 0,
    int Slot3Fga = 0,
    int Slot4Fga = 0,
    int Slot5Fga = 0,
    int SlotUnattributedFga = 0,
    int Slot1Fgm = 0,
    int Slot2Fgm = 0,
    int Slot3Fgm = 0,
    int Slot4Fgm = 0,
    int Slot5Fgm = 0,
    int SlotUnattributedFgm = 0,
    SlotGroup ThreePaBySlot  = default,
    SlotGroup ThreePmBySlot  = default,
    SlotGroup FtaBySlot      = default,
    SlotGroup FtmBySlot      = default,
    int       BlkCount            = 0,
    int?      TurnoverOffSlot     = null,
    bool      TurnoverWasLiveBall = false,
    // Phase 25: shooting-foul events recorded by the resolver walk. Null on NoShot
    // possessions (no resolver call); empty list on possessions with no shooting foul;
    // one or more entries when MadeAndFouled / MissFouled fired. Nullable to mirror the
    // Phase 23 additions pattern and to keep the NoShot path zero-allocation.
    IReadOnlyList<ShootingFoulEvent>? ShootingFouls = null,
    // Phase 31: per-slot offensive-rebound counts. OrbBySlot.Total == OrbWon on every
    // possession (harness-asserted). Default (all zeros) on NoShot possessions and any
    // possession that secured no offensive board.
    SlotGroup OrbBySlot = default,
    // Phase 34: engine-stamped stealer slot for live-ball turnovers.
    int? StealerSlot = null,
    // Phase 35: engine-stamped defensive rebounder slot. Non-null on every
    // DefensiveRebound possession; null on all others.
    int? DefensiveRebounderSlot = null,
    // Phase 36: engine-stamped per-slot block counts. BlkBySlot.Total == BlkCount on every
    // possession (harness-asserted). Default (all zeros) on possessions with no blocks.
    SlotGroup BlkBySlot = default,

    // Session 79, PAGE-ONLY: block credit split by whether the credited defender was the man
    // guarding the shooter, near the rim (Rim/Short) vs out (Mid/Long/Three). Never asserted;
    // it separates credit REDISTRIBUTION from a real RATE change on the season page.
    int       BlkMatchedNear = 0,
    int       BlkHelperNear  = 0,
    int       BlkMatchedOut  = 0,
    int       BlkHelperOut   = 0,
    // Phase 39: engine-stamped per-slot assist counts. AstBySlot.Total <= Fgm on every
    // possession (harness-asserted). Default (all zeros) on possessions with no made FGs
    // or possessions where every make was a putback or null-SelectedSlot (bonus-FT edge).
    SlotGroup AstBySlot = default,
    // Phase 51: FTA-source classification — every FTA on the possession lands in exactly
    // one of these five buckets, so they reconcile to Fta
    // (FtaBonusPicker + FtaBonusSelected + FtaBonusUnattributed + FtaShootingSelected +
    //  FtaShootingNoSlot == Fta) — asserted by the Observation run. Defaults 0.
    int FtaBonusPicker = 0,
    int FtaBonusSelected = 0,
    int FtaBonusUnattributed = 0,
    int FtaShootingSelected = 0,
    int FtaShootingNoSlot = 0,
    // Session 37: court-aware turnover clock instrumentation.
    //  TimeProfile        — the turnover timing class (backcourt/frontcourt) if this
    //                       possession ended on a profile-stamped turnover-family
    //                       terminal; null otherwise. The page groups turnover records
    //                       by this to split the length line by court.
    //  TurnoverRawElapsed — the PRE-CLAMP band draw for a profile possession (Elapsed
    //                       is min(this, periodRemaining)); null otherwise. Lets the
    //                       page report raw band means (the oracle's prediction target)
    //                       alongside the clamped applied mean.
    //  ShotClockPeriods   — shot-clock periods this possession spanned (1 = single
    //                       period; >1 = one reset per offensive rebound). Lets the raw
    //                       band assertion filter to single-period frontcourt draws.
    PossessionTimeProfile? TimeProfile = null,
    double? TurnoverRawElapsed = null,
    int ShotClockPeriods = 1,
    // Session 38: fast-break shot-diet counters, copied through from RoutingOutcome —
    // read-only page instrumentation. FastBreakThreePm <= FastBreakThreePa <=
    // FastBreakFga <= Fga on every possession. Excludes Roll K putbacks (a transition
    // possession's putback carries FastBreak but is rim-forced, not a diet shot).
    int FastBreakFga = 0,
    int FastBreakThreePa = 0,
    int FastBreakThreePm = 0,
    // Session 62: non-shooting-foul events for this possession (reach-in A/B/F + situational
    // I/J/K/M), the parallel of ShootingFouls. Appended last so every existing positional
    // construction is unaffected; empty/null on possessions with no non-shooting foul.
    IReadOnlyList<NonShootingFoulEvent>? NonShootingFouls = null,
    // Session 84, PAGE-ONLY: the lineup passing factor the assist door applied, summed over
    // this possession's assist-eligible made field goals, and the matching event count. The
    // page divides one by the other at league and team scale to report the REALIZED factor.
    // Appended last so every existing positional construction is unaffected (the S62
    // convention). Never asserted; a sum-and-count pair rather than a mean because a
    // possession can carry more than one eligible make and the roll-up is event-weighted.
    double AssistPassFactorSum = 0.0,
    int AssistPassFactorEvents = 0,
    // Session 85, PAGE-ONLY — the fast-break readout. Appended last with defaults so every
    // existing positional construction is unaffected (the S62/S84 convention).
    //
    // TransitionArm: which arm of Roll J's run-or-not pie opened this possession, or null if
    // it did not start off a rebound / free-throw rebound / steal. ONE label rather than five
    // flags, because Roll J runs exactly once per transition entry and never otherwise — so
    // "the five buckets sum to the transition-entry count" is true by representation.
    //
    // The rest is the three-way shot partition — fast break, break putback (a second-chance
    // shot taken while the break was still live; Roll K's PutBack arm does not clear the flag),
    // and non-break — with makes and blocked attempts for each. All three are counted
    // explicitly rather than one being derived by subtraction, so the partition identity is a
    // real check rather than arithmetic asserting itself. FastBreakBlkBySlot is the per-seat
    // subset of BlkBySlot for break blocks; its Total equals FastBreakBlk.
    TransitionOutcome? TransitionArm = null,
    int FastBreakFgm = 0,
    int BreakPutbackFga = 0,
    int BreakPutbackFgm = 0,
    int NonBreakFga = 0,
    int NonBreakFgm = 0,
    int FastBreakBlk = 0,
    int BreakPutbackBlk = 0,
    int NonBreakBlk = 0,
    SlotGroup FastBreakBlkBySlot = default,
    // S87: offensive fouls charged on this possession — charges (from the entry, the
    // turnover pie, or the rebound scrum) and scrum fouls, each carrying the man who
    // committed it. The third foul ledger; before S87 an offensive foul reached no foul
    // count at all. Appended last with a null default so every existing positional
    // construction is unaffected (the S62/S84/S85 convention).
    IReadOnlyList<OffensiveFoulEvent>? OffensiveFouls = null);

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
/// <param name="OvertimePeriods">Number of overtime periods played. 0 = regulation finish;
/// 1 = one OT; 2 = double OT; etc.</param>
public sealed record GovernorRunResult(
    IReadOnlyList<PossessionRecord> Possessions,
    int TerminalEnded,
    int Parked,
    double TotalSeconds,
    IReadOnlyDictionary<string, int> PerStubParks,
    int OvertimePeriods);

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

    // Phase 52: optional substitution seam. Null on every existing construction path, so
    // every existing run is a strict no-op (the hook is never invoked when null). The
    // engine stays position-agnostic — it only reports boundaries and hands over the game.
    private readonly ISubstitutionPolicy? _substitutionPolicy;

    public Governor(Resolver resolver, GameState game, GovernorConfig cfg, RollClockConfig clock, IRng rng, EndOfHalfConfig endOfHalf,
        ISubstitutionPolicy? substitutionPolicy = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _endOfHalf = endOfHalf ?? throw new ArgumentNullException(nameof(endOfHalf));
        _substitutionPolicy = substitutionPolicy;   // may be null → no substitutions
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

        // Phase 52: the elapsed (capped) seconds of the most-recently-resolved possession,
        // captured by RunOnePossession. The loops read it to feed the substitution seam:
        // the ordinary boundary callback recovers benched players by the just-ended
        // possession's clock, and a period-break callback needs the final possession's
        // clock (its ordinary callback is suppressed across the break).
        var lastApplied = 0.0;

        // ── Local function: resolve one possession and append its record. ──────
        // Captures and updates run-level accumulators naturally. Only the values
        // that differ between the regulation and OT callers are passed explicitly:
        // state (the current possession), periodRemaining (the clock for this period),
        // and periodNumber (stamped as PossessionRecord.Half).
        //
        // Extraction boundary: includes intent draw → resolver (or NoShot short-circuit)
        // → score write → record creation → state spawn. Does NOT include the period-
        // transition block (half increment, foul reset, clock reset) — those belong
        // exclusively to the caller loops.
        void RunOnePossession(ref PossessionState st, ref double periodRemaining, int periodNumber)
        {
            if (++guard > _cfg.PossessionCap)
                throw new InvalidOperationException(
                    $"Governor safety guard exceeded {_cfg.PossessionCap} possessions — the clock " +
                    "is not draining (check HalfSeconds and possession-time config).");

            EndOfHalfIntent? intent = periodRemaining < _endOfHalf.HoldThresholdSeconds
                ? _endOfHalfPie.Roll(_rng.NextUnitInterval())
                : null;

            PossessionConsequence consequence;
            bool endedOnTerminal;
            string endLabel;
            int pointsThisPossession;
            double applied;
            // Session 37: turnover-clock record locals. Survive both fork branches so
            // the record ctor can read them. Profile null / raw null / periods 1 unless
            // a profile-stamped terminal sets them in the resolved branch below.
            PossessionTimeProfile? recordTimeProfile = null;
            double? recordTurnoverRaw = null;
            int recordShotClockPeriods = 1;
            int possessionFga = 0, possessionFgm = 0, possessionThreePa = 0, possessionThreePm = 0;
            int possessionFastBreakFga = 0, possessionFastBreakThreePa = 0, possessionFastBreakThreePm = 0;
            // Session 36: displacement-context bucket locals (copied from RoutingOutcome).
            int possessionDispLowFga = 0, possessionDispLowThreePa = 0, possessionDispLowThreePm = 0;
            int possessionDispMidFga = 0, possessionDispMidThreePa = 0, possessionDispMidThreePm = 0;
            int possessionDispHighFga = 0, possessionDispHighThreePa = 0, possessionDispHighThreePm = 0;
            int possessionShotResolutions = 0, possessionMissFouled = 0;
            int possessionFta = 0, possessionFtm = 0, possessionOrbChances = 0, possessionOrbWon = 0;
            // Phase 51: FTA-source classification locals (reconcile to possessionFta).
            int possessionFtaBonusPicker = 0, possessionFtaBonusSelected = 0,
                possessionFtaBonusUnattributed = 0, possessionFtaShootingSelected = 0,
                possessionFtaShootingNoSlot = 0;
            int possessionRimFga = 0, possessionRimFgm = 0, possessionShortFga = 0, possessionShortFgm = 0;
            int possessionMidFga = 0, possessionMidFgm = 0, possessionLongFga = 0, possessionLongFgm = 0;
            int possessionSlot1Fga = 0, possessionSlot2Fga = 0, possessionSlot3Fga = 0,
                possessionSlot4Fga = 0, possessionSlot5Fga = 0;
            int possessionSlotUnattributedFga = 0;
            int possessionSlot1Fgm = 0, possessionSlot2Fgm = 0, possessionSlot3Fgm = 0,
                possessionSlot4Fgm = 0, possessionSlot5Fgm = 0;
            int possessionSlotUnattributedFgm = 0;
            var possessionThreePaBySlot      = new SlotGroup();
            var possessionThreePmBySlot      = new SlotGroup();
            var possessionFtaBySlot          = new SlotGroup();
            var possessionFtmBySlot          = new SlotGroup();
            var possessionBlkCount           = 0;
            int?  possessionTurnoverOffSlot    = null;
            var   possessionTurnoverWasLiveBall = false;
            IReadOnlyList<ShootingFoulEvent>? possessionShootingFouls = null;
            IReadOnlyList<NonShootingFoulEvent>? possessionNonShootingFouls = null;   // Session 62
            IReadOnlyList<OffensiveFoulEvent>? possessionOffensiveFouls = null;       // S87
            var possessionOrbBySlot = new SlotGroup();
            int? possessionStealerSlot = null;
            int? possessionDefensiveRebounderSlot = null;
            var possessionBlkBySlot = new SlotGroup();
            var possessionBlkMatchedNear = 0; var possessionBlkHelperNear = 0;
            var possessionBlkMatchedOut  = 0; var possessionBlkHelperOut  = 0;
            var possessionAstBySlot = new SlotGroup();
            var possessionAssistPassFactorSum    = 0.0;
            var possessionAssistPassFactorEvents = 0;
            // Session 85, PAGE-ONLY — the fast-break readout's carriers.
            TransitionOutcome? possessionTransitionArm = null;
            var possessionFastBreakFgm    = 0;
            var possessionBreakPutbackFga = 0; var possessionBreakPutbackFgm = 0;
            var possessionNonBreakFga     = 0; var possessionNonBreakFgm     = 0;
            var possessionFastBreakBlk    = 0;
            var possessionBreakPutbackBlk = 0; var possessionNonBreakBlk     = 0;
            var possessionFastBreakBlkBySlot = new SlotGroup();

            if (intent == EndOfHalfIntent.NoShot)
            {
                endedOnTerminal = false;
                endLabel = "endOfHalf:NoShot";
                consequence = PossessionConsequence.DeadBallTo(st.Defense);
                pointsThisPossession = 0;
                applied = periodRemaining;
            }
            else
            {
                var outcome = _resolver.RunPossession(st);

                if (outcome.EndedOn is { } term)
                {
                    endedOnTerminal = true;
                    consequence = term.Consequence;
                    endLabel = term.Reason;
                    terminalEnded++;
                }
                else
                {
                    endedOnTerminal = false;
                    consequence = PossessionConsequence.DeadBallTo(st.Defense);
                    endLabel = $"parked:{outcome.Destination}";
                    parked++;
                    perStubParks[outcome.Destination] =
                        perStubParks.GetValueOrDefault(outcome.Destination) + 1;
                }

                // Elapsed-time precedence, exactly one RNG-consuming draw per possession:
                //   1. invariant ElapsedSeconds (the three violation arms) — no draw;
                //   2. a profile-stamped turnover-band draw (Session 37, court-aware);
                //   3. the shared possession draw (everything else).
                // A non-turnover or invariant terminal takes the SAME path it took before
                // this session — identical draw, identical RNG consumption — so the
                // neutrality anchor holds.
                double rawElapsed;
                recordShotClockPeriods = outcome.ShotClockPeriods;
                if (outcome.EndedOn?.ElapsedSeconds is { } invariantElapsed)
                {
                    rawElapsed = invariantElapsed;
                }
                else if (outcome.EndedOn?.TimeProfile is { } stampedProfile)
                {
                    // A possession that has offensive-rebounded (ShotClockPeriods > 1) is
                    // physically in the frontcourt regardless of the court-state flag —
                    // that flag only latches on the halfcourt entry and stays "backcourt"
                    // for transition / ball-advanced possessions that skip it. Promote
                    // such a turnover to frontcourt so its clock reflects reality.
                    var profile = EffectiveTurnoverProfile(stampedProfile, outcome.ShotClockPeriods);
                    rawElapsed = DrawTurnoverSeconds(profile, outcome.ShotClockPeriods, st.Offense);
                    recordTimeProfile = profile;
                    recordTurnoverRaw = rawElapsed;
                }
                else
                {
                    rawElapsed = DrawPossessionSeconds(outcome.ShotClockPeriods, st.Offense);
                }
                applied = intent == EndOfHalfIntent.HoldShootLast
                    ? periodRemaining
                    : Math.Min(rawElapsed, periodRemaining);

                pointsThisPossession = outcome.Points;
                possessionFga             = outcome.Fga;
                possessionFgm             = outcome.Fgm;
                possessionThreePa         = outcome.ThreePa;
                possessionThreePm         = outcome.ThreePm;
                possessionFastBreakFga     = outcome.FastBreakFga;
                possessionFastBreakThreePa = outcome.FastBreakThreePa;
                possessionFastBreakThreePm = outcome.FastBreakThreePm;
                possessionDispLowFga      = outcome.DispLowFga;
                possessionDispLowThreePa  = outcome.DispLowThreePa;
                possessionDispLowThreePm  = outcome.DispLowThreePm;
                possessionDispMidFga      = outcome.DispMidFga;
                possessionDispMidThreePa  = outcome.DispMidThreePa;
                possessionDispMidThreePm  = outcome.DispMidThreePm;
                possessionDispHighFga     = outcome.DispHighFga;
                possessionDispHighThreePa = outcome.DispHighThreePa;
                possessionDispHighThreePm = outcome.DispHighThreePm;
                possessionShotResolutions = outcome.ShotResolutions;
                possessionMissFouled      = outcome.MissFouled;
                possessionFta             = outcome.Fta;
                possessionFtm             = outcome.Ftm;
                // Phase 51: carry the FTA-source classification through to the record.
                possessionFtaBonusPicker       = outcome.FtaBonusPicker;
                possessionFtaBonusSelected     = outcome.FtaBonusSelected;
                possessionFtaBonusUnattributed = outcome.FtaBonusUnattributed;
                possessionFtaShootingSelected  = outcome.FtaShootingSelected;
                possessionFtaShootingNoSlot    = outcome.FtaShootingNoSlot;
                possessionOrbChances      = outcome.OrbChances;
                possessionOrbWon          = outcome.OrbWon;
                possessionRimFga          = outcome.RimFga;
                possessionRimFgm          = outcome.RimFgm;
                possessionShortFga        = outcome.ShortFga;
                possessionShortFgm        = outcome.ShortFgm;
                possessionMidFga          = outcome.MidFga;
                possessionMidFgm          = outcome.MidFgm;
                possessionLongFga         = outcome.LongFga;
                possessionLongFgm         = outcome.LongFgm;
                possessionSlot1Fga        = outcome.Slot1Fga;
                possessionSlot2Fga        = outcome.Slot2Fga;
                possessionSlot3Fga        = outcome.Slot3Fga;
                possessionSlot4Fga        = outcome.Slot4Fga;
                possessionSlot5Fga        = outcome.Slot5Fga;
                possessionSlotUnattributedFga = outcome.SlotUnattributedFga;
                possessionSlot1Fgm        = outcome.Slot1Fgm;
                possessionSlot2Fgm        = outcome.Slot2Fgm;
                possessionSlot3Fgm        = outcome.Slot3Fgm;
                possessionSlot4Fgm        = outcome.Slot4Fgm;
                possessionSlot5Fgm        = outcome.Slot5Fgm;
                possessionSlotUnattributedFgm = outcome.SlotUnattributedFgm;
                possessionThreePaBySlot      = outcome.ThreePaBySlot;
                possessionThreePmBySlot      = outcome.ThreePmBySlot;
                possessionFtaBySlot          = outcome.FtaBySlot;
                possessionFtmBySlot          = outcome.FtmBySlot;
                possessionBlkCount           = outcome.BlkCount;
                possessionTurnoverOffSlot     = outcome.TurnoverOffSlot;
                possessionTurnoverWasLiveBall = outcome.TurnoverWasLiveBall;
                possessionShootingFouls       = outcome.ShootingFouls;
                possessionNonShootingFouls    = outcome.NonShootingFouls;
                possessionOffensiveFouls      = outcome.OffensiveFouls;
                possessionOrbBySlot           = outcome.OrbBySlot;
                possessionStealerSlot         = outcome.StealerSlot;
                possessionDefensiveRebounderSlot = outcome.DefensiveRebounderSlot;
                possessionBlkBySlot = outcome.BlkBySlot;
                possessionBlkMatchedNear = outcome.BlkMatchedNear;
                possessionBlkHelperNear  = outcome.BlkHelperNear;
                possessionBlkMatchedOut  = outcome.BlkMatchedOut;
                possessionBlkHelperOut   = outcome.BlkHelperOut;
                possessionAstBySlot = outcome.AstBySlot;
                possessionAssistPassFactorSum    = outcome.AssistPassFactorSum;
                possessionAssistPassFactorEvents = outcome.AssistPassFactorEvents;
                possessionTransitionArm       = outcome.TransitionArm;
                possessionFastBreakFgm        = outcome.FastBreakFgm;
                possessionBreakPutbackFga     = outcome.BreakPutbackFga;
                possessionBreakPutbackFgm     = outcome.BreakPutbackFgm;
                possessionNonBreakFga         = outcome.NonBreakFga;
                possessionNonBreakFgm         = outcome.NonBreakFgm;
                possessionFastBreakBlk        = outcome.FastBreakBlk;
                possessionBreakPutbackBlk     = outcome.BreakPutbackBlk;
                possessionNonBreakBlk         = outcome.NonBreakBlk;
                possessionFastBreakBlkBySlot  = outcome.FastBreakBlkBySlot;
            }

            periodRemaining -= applied;
            totalSeconds    += applied;

            if (st.Offense == TeamSide.Home) _game.HomeScore += pointsThisPossession;
            else _game.AwayScore += pointsThisPossession;

            // Phase 48: accrue exactly ONE possession of fatigue to every on-floor player of
            // BOTH sides. This sits at the top-level possession tail, after the outcome
            // resolved (applied known) — so it fires once per possession, never per Roll, free
            // throw, rebound continuation, retained inbound, or internal retry. It reads no
            // RNG and nothing reads the level this session, so it changes no outcome.
            _game.Fatigue.Accrue(OnFloorBothSides());

            // Phase 52: publish this possession's elapsed clock for the substitution seam.
            // Set AFTER accrual so ordering holds — the five who played this possession have
            // already accrued it before any boundary callback that reads this value fires.
            lastApplied = applied;

            records.Add(new PossessionRecord(
                st.PossessionNumber, st.Offense, st.Defense, st.Entry,
                endedOnTerminal, endLabel, consequence, pointsThisPossession, applied, periodNumber, intent,
                possessionFga, possessionFgm, possessionThreePa, possessionThreePm,
                possessionDispLowFga, possessionDispLowThreePa, possessionDispLowThreePm,
                possessionDispMidFga, possessionDispMidThreePa, possessionDispMidThreePm,
                possessionDispHighFga, possessionDispHighThreePa, possessionDispHighThreePm,
                possessionShotResolutions, possessionMissFouled,
                possessionFta, possessionFtm, possessionOrbChances, possessionOrbWon,
                possessionRimFga, possessionRimFgm, possessionShortFga, possessionShortFgm,
                possessionMidFga, possessionMidFgm, possessionLongFga, possessionLongFgm,
                possessionSlot1Fga, possessionSlot2Fga, possessionSlot3Fga,
                possessionSlot4Fga, possessionSlot5Fga,
                possessionSlotUnattributedFga,
                possessionSlot1Fgm, possessionSlot2Fgm, possessionSlot3Fgm,
                possessionSlot4Fgm, possessionSlot5Fgm,
                possessionSlotUnattributedFgm,
                possessionThreePaBySlot, possessionThreePmBySlot,
                possessionFtaBySlot,     possessionFtmBySlot,
                possessionBlkCount,
                possessionTurnoverOffSlot, possessionTurnoverWasLiveBall,
                possessionShootingFouls,
                possessionOrbBySlot,
                possessionStealerSlot,
                possessionDefensiveRebounderSlot,
                possessionBlkBySlot,
                possessionBlkMatchedNear,
                possessionBlkHelperNear,
                possessionBlkMatchedOut,
                possessionBlkHelperOut,
                possessionAstBySlot,
                possessionFtaBonusPicker, possessionFtaBonusSelected,
                possessionFtaBonusUnattributed, possessionFtaShootingSelected,
                possessionFtaShootingNoSlot,
                recordTimeProfile, recordTurnoverRaw, recordShotClockPeriods,
                possessionFastBreakFga, possessionFastBreakThreePa, possessionFastBreakThreePm,
                possessionNonShootingFouls,
                possessionAssistPassFactorSum,
                possessionAssistPassFactorEvents,
                possessionTransitionArm,
                possessionFastBreakFgm,
                possessionBreakPutbackFga,
                possessionBreakPutbackFgm,
                possessionNonBreakFga,
                possessionNonBreakFgm,
                possessionFastBreakBlk,
                possessionBreakPutbackBlk,
                possessionNonBreakBlk,
                possessionFastBreakBlkBySlot,
                possessionOffensiveFouls));

            var nextOffense = consequence.NextOffense;
            st = new PossessionState(
                PossessionNumber: st.PossessionNumber + 1,
                Offense: nextOffense,
                Defense: Other(nextOffense),
                Entry: consequence.NextEntry,
                TransitionContext: consequence.TransitionContext);
        }

        // ── Regulation loop ───────────────────────────────────────────────────
        while (half <= _cfg.Halves)
        {
            RunOnePossession(ref state, ref halfRemaining, half);

            if (halfRemaining > 0.0)
            {
                // Phase 52: within-period boundary — a successor possession runs THIS half.
                // `state` already holds that successor (RunOnePossession spawned it): its
                // number, and its entry (dead-ball inbound / ball-advanced vs live-ball
                // transition). Substitutions are legal only from a dead ball.
                _substitutionPolicy?.OnPossessionBoundary(
                    _game, state.PossessionNumber, lastApplied,
                    state.Entry != EntryType.Transition);
            }
            else
            {
                // Reset fouls AND apply halftime fatigue recovery only when moving from one
                // regulation half to another — never after the final regulation half. Fouls
                // carry into overtime (NCAA rule); halftime recovery fires exactly once and
                // never in OT. Same guard, same boundary — the overtime loop below is separate,
                // so this block never executes during OT.
                if (half < _cfg.Halves)
                {
                    _game.Fouls.ResetForNewHalf();
                    _game.Fatigue.ApplyHalftimeRecovery(OnFloorBothSides());
                    // Phase 52: halftime period break. The engine has just rested the
                    // on-floor five above; the policy rests its benched players (final-
                    // possession slice + the matching halftime chunk) and reclaims starters
                    // for the first possession of the second half. NOT the terminal boundary:
                    // a successor half follows. The final regulation half (half == Halves)
                    // never enters this block — if the game reaches OT it is a tied ending,
                    // handled as an overtime break below; if untied, it is terminal.
                    _substitutionPolicy?.OnPeriodBreak(
                        _game, state.PossessionNumber, lastApplied, PeriodBreakKind.Halftime);
                }
                half++;
                halfRemaining = _cfg.HalfSeconds;
            }
        }

        // ── Overtime loop ─────────────────────────────────────────────────────
        // NCAA: each OT starts with a fresh tip; team fouls do NOT reset.
        var otPeriod = 0;
        while (_game.HomeScore == _game.AwayScore)
        {
            // Phase 52: overtime period break. Entering this loop body means the PRIOR
            // period (the final regulation half on the first iteration, the prior OT
            // afterward) ended TIED — so a successor period runs and this is a non-terminal
            // boundary. `state.PossessionNumber` is the first possession of this OT period
            // (the tip below uses it); `lastApplied` is the prior period's final possession
            // clock. There is NO rest chunk at an OT boundary — the policy applies only the
            // final-possession recovery slice, then reclaims. If a period instead ends
            // UNtied, this loop body never runs, so no callback fires after a game-ending
            // possession (the terminal guard).
            _substitutionPolicy?.OnPeriodBreak(
                _game, state.PossessionNumber, lastApplied, PeriodBreakKind.Overtime);

            otPeriod++;
            _game.ResetPossessionArrow();   // fresh contest (arrow -> Off)
            // TipPossession.CreateFromTip sets the arrow to the tip loser and returns
            // the OT opening possession. After RunOnePossession, state already holds the
            // next unplayed sequential number — pass it directly, do not add one.
            state = TipPossession.CreateFromTip(_game, _rng,
                possessionNumber: state.PossessionNumber);   // state already holds the next unplayed number

            var otRemaining = _cfg.OvertimeSeconds;
            while (otRemaining > 0.0)
                RunOnePossession(ref state, ref otRemaining, _cfg.Halves + otPeriod);
            // No foul reset at OT boundaries (NCAA rule: fouls carry forward).
        }

        return new GovernorRunResult(records, terminalEnded, parked, totalSeconds, perStubParks, otPeriod);
    }

    /// <summary>Sum the truncated-normal draws for a possession's shot-clock periods:
    /// period 1 on the full clock, each offensive-rebound reset on the 20s clock (center
    /// and sd scaled to the shorter window). Outcome-blind — the draw never depends on how
    /// a period ended; an invariant terminal (handled by the caller) overrides this.
    ///
    /// <para><b>Phase 30 — coach pace adjustment.</b> The offensive coach's
    /// <see cref="CoachProfile.PaceBias"/> shifts the center before sampling.
    /// Neutral (5.0) → zero shift; fast (10) → center down; slow (1) → center up.
    /// The floor guard ensures center never drops below <c>Floor + 1.0</c>.</para></summary>
    private double DrawPossessionSeconds(int shotClockPeriods, TeamSide offense)
    {
        var center     = CoachAdjustedCenter(offense);
        var periods    = Math.Max(1, shotClockPeriods);
        var seconds    = ClockDraw.Sample(_rng, center, _clock.StdDev, _clock.Floor, _clock.FullClockSeconds);
        var resetScale = _clock.ResetClockSeconds / _clock.FullClockSeconds;
        for (var p = 2; p <= periods; p++)
            seconds += ClockDraw.Sample(_rng, center * resetScale, _clock.StdDev * resetScale,
                                        _clock.Floor, _clock.ResetClockSeconds);
        return seconds;
    }

    /// <summary>The coach-pace-adjusted center for a possession's period-1 draw —
    /// factored out so the turnover-band draw's prior periods (§ Session 37) reuse the
    /// identical center the shared draw uses. Numerically identical to the inline
    /// computation it replaces, so non-turnover draws stay byte-for-byte unchanged.</summary>
    private double CoachAdjustedCenter(TeamSide offense)
    {
        var coach   = _game.CoachFor(offense);
        // Map PaceBias [1,10] to a center shift. Neutral (5.0) → 0.0.
        // (5.0 - bias) / 5.0 → positive for slow (bias < 5), negative for fast (bias > 5).
        var paceAdj = (5.0 - coach.PaceBias) / 5.0 * _clock.PaceCenterScale;
        return Math.Max(_clock.Floor + 1.0, _clock.Center + paceAdj);
    }

    /// <summary>Session 37 — the court-aware turnover clock draw. A profile-stamped
    /// terminal (backcourt or frontcourt turnover / offensive foul) draws its elapsed
    /// time from a shorter, court-dependent band instead of the shared possession
    /// clock. Called only when a terminal carries a <see cref="PossessionTimeProfile"/>
    /// and no invariant <see cref="RollResult.ElapsedSeconds"/> (invariant time takes
    /// precedence — see the applied fork).
    ///
    /// <para><b>Backcourt</b> (band <c>[BackcourtFloor, BackcourtCeiling)</c>) is a
    /// single-period event by construction: the ball cannot be in the backcourt after
    /// an offensive rebound, so <c>shotClockPeriods &gt; 1</c> is physically impossible
    /// and throws — a silent reclassification would hide a real routing bug.</para>
    ///
    /// <para><b>Frontcourt</b>: N = 1 (the vast majority) draws the frontcourt band
    /// directly. A multi-period possession (a frontcourt turnover after one or more
    /// offensive rebounds) draws its prior periods exactly as a normal possession
    /// (period 1 full clock, intermediates on the reset clock) and its FINAL period on
    /// the frontcourt band scaled to the reset window — so its whole-possession elapsed
    /// can legitimately exceed 30s (a prior clock period plus a reset period).</para></summary>
    private double DrawTurnoverSeconds(PossessionTimeProfile profile, int shotClockPeriods, TeamSide offense)
    {
        var periods = Math.Max(1, shotClockPeriods);

        if (profile == PossessionTimeProfile.BackcourtTurnover)
        {
            // Backcourt reaches the draw only single-period: EffectiveTurnoverProfile
            // has already promoted any multi-period possession to frontcourt (you cannot
            // grab an offensive rebound in the backcourt). A multi-period backcourt here
            // means a caller skipped that rule — fail loud rather than draw a wrong band.
            if (periods > 1)
                throw new InvalidOperationException(
                    $"BackcourtTurnover reached the clock draw across {periods} shot-clock periods — " +
                    "EffectiveTurnoverProfile should have promoted it to frontcourt first. Routing bug.");
            return ClockDraw.Sample(_rng, _clock.BackcourtTurnoverCenter, _clock.BackcourtTurnoverStdDev,
                                    _clock.BackcourtTurnoverFloor, _clock.BackcourtTurnoverCeiling);
        }

        // FrontcourtTurnover.
        if (periods == 1)
            return ClockDraw.Sample(_rng, _clock.FrontcourtTurnoverCenter, _clock.FrontcourtTurnoverStdDev,
                                    _clock.FrontcourtTurnoverFloor, _clock.FullClockSeconds);

        // N > 1: prior periods drawn exactly as a normal possession (period 1 full,
        // intermediates reset), the FINAL period on the frontcourt band scaled to the
        // reset window (center×rs, sd×rs, floor = Floor, ceiling = ResetClockSeconds).
        var center     = CoachAdjustedCenter(offense);
        var resetScale = _clock.ResetClockSeconds / _clock.FullClockSeconds;
        var seconds    = ClockDraw.Sample(_rng, center, _clock.StdDev, _clock.Floor, _clock.FullClockSeconds);
        for (var p = 2; p <= periods - 1; p++)
            seconds += ClockDraw.Sample(_rng, center * resetScale, _clock.StdDev * resetScale,
                                        _clock.Floor, _clock.ResetClockSeconds);
        seconds += ClockDraw.Sample(_rng,
            _clock.FrontcourtTurnoverCenter * resetScale, _clock.FrontcourtTurnoverStdDev * resetScale,
            _clock.Floor, _clock.ResetClockSeconds);
        return seconds;
    }

    /// <summary>The clock profile a turnover-family terminal is actually TIMED by,
    /// given how many shot-clock periods the possession spanned. The court-state flag a
    /// terminal is stamped with (<see cref="PossessionTimeProfile.BackcourtTurnover"/> vs
    /// frontcourt) reflects whether the possession came up through the halfcourt entry —
    /// it stays "backcourt" for transition and ball-advanced possessions that never take
    /// that entry, even after they cross half. But a possession that has recorded an
    /// offensive rebound (<paramref name="shotClockPeriods"/> &gt; 1) was physically in
    /// the frontcourt — you cannot rebound your own miss in the backcourt — so its
    /// turnover is timed as a frontcourt turnover regardless of the stale flag. Pure and
    /// public so the harness proves the rule directly.</summary>
    public static PossessionTimeProfile EffectiveTurnoverProfile(
        PossessionTimeProfile stamped, int shotClockPeriods) =>
        shotClockPeriods > 1 ? PossessionTimeProfile.FrontcourtTurnover : stamped;

    // Gather the on-floor players for BOTH sides — the fatigue meter accrues to all ten and
    // recovers all ten at halftime. Walks the same lineup -> roster seam the attribution
    // layer uses. An absent slot contributes null and is skipped by the tracker (defensive;
    // with fixed lineups there are no absent slots). Reads no RNG.
    private List<Player?> OnFloorBothSides()
    {
        var players = new List<Player?>(2 * Lineup.Size);
        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var lineup = _game.LineupFor(side);
            var roster = _game.RosterFor(side);
            foreach (var slot in lineup.OnCourt)
                players.Add(roster.PlayerAt(slot));
        }
        return players;
    }

    private static TeamSide Other(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
}
