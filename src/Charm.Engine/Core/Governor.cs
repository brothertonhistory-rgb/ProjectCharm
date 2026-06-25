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
    int FtaShootingNoSlot = 0);

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
            int possessionFga = 0, possessionFgm = 0, possessionThreePa = 0, possessionThreePm = 0;
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
            var possessionOrbBySlot = new SlotGroup();
            int? possessionStealerSlot = null;
            int? possessionDefensiveRebounderSlot = null;
            var possessionBlkBySlot = new SlotGroup();
            var possessionAstBySlot = new SlotGroup();

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

                var rawElapsed = outcome.EndedOn?.ElapsedSeconds ?? DrawPossessionSeconds(outcome.ShotClockPeriods, st.Offense);
                applied = intent == EndOfHalfIntent.HoldShootLast
                    ? periodRemaining
                    : Math.Min(rawElapsed, periodRemaining);

                pointsThisPossession = outcome.Points;
                possessionFga             = outcome.Fga;
                possessionFgm             = outcome.Fgm;
                possessionThreePa         = outcome.ThreePa;
                possessionThreePm         = outcome.ThreePm;
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
                possessionOrbBySlot           = outcome.OrbBySlot;
                possessionStealerSlot         = outcome.StealerSlot;
                possessionDefensiveRebounderSlot = outcome.DefensiveRebounderSlot;
                possessionBlkBySlot = outcome.BlkBySlot;
                possessionAstBySlot = outcome.AstBySlot;
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

            records.Add(new PossessionRecord(
                st.PossessionNumber, st.Offense, st.Defense, st.Entry,
                endedOnTerminal, endLabel, consequence, pointsThisPossession, applied, periodNumber, intent,
                possessionFga, possessionFgm, possessionThreePa, possessionThreePm,
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
                possessionAstBySlot,
                possessionFtaBonusPicker, possessionFtaBonusSelected,
                possessionFtaBonusUnattributed, possessionFtaShootingSelected,
                possessionFtaShootingNoSlot));

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

            if (halfRemaining <= 0.0)
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
        var coach   = _game.CoachFor(offense);
        // Map PaceBias [1,10] to a center shift. Neutral (5.0) → 0.0.
        // (5.0 - bias) / 5.0 → positive for slow (bias < 5), negative for fast (bias > 5).
        var paceAdj = (5.0 - coach.PaceBias) / 5.0 * _clock.PaceCenterScale;
        var center  = Math.Max(_clock.Floor + 1.0, _clock.Center + paceAdj);

        var periods    = Math.Max(1, shotClockPeriods);
        var seconds    = ClockDraw.Sample(_rng, center, _clock.StdDev, _clock.Floor, _clock.FullClockSeconds);
        var resetScale = _clock.ResetClockSeconds / _clock.FullClockSeconds;
        for (var p = 2; p <= periods; p++)
            seconds += ClockDraw.Sample(_rng, center * resetScale, _clock.StdDev * resetScale,
                                        _clock.Floor, _clock.ResetClockSeconds);
        return seconds;
    }

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
