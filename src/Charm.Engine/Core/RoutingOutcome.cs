namespace Charm.Engine;

/// <summary>What the resolver did with one result — for observability/harness.</summary>
/// <param name="PossessionEnded">True if the possession ended on a terminal; false
/// if it parked at a stub. Unchanged — every existing check reads this.</param>
/// <param name="Destination">The destination label ("END:{reason}" on a terminal,
/// "STUB:…" on a park). Unchanged — every existing check reads this.</param>
public readonly record struct RoutingOutcome(bool PossessionEnded, string Destination)
{
    /// <summary>
    /// The actual terminal the possession ENDED on — the object itself, carrying its
    /// <see cref="Terminal.State"/> and its <see cref="Terminal.Consequence"/> — so a
    /// caller (the Governor) can spawn the next possession without parsing the
    /// destination string. NULL when the possession PARKED at a stub (no terminal was
    /// reached); the Governor reads that null as "apply the default consequence."
    /// <para>Init-only with a null default, so every existing positional construction
    /// (<c>new RoutingOutcome(false, "STUB:…")</c>) and every existing read of
    /// <see cref="Destination"/> / <see cref="PossessionEnded"/> stays untouched —
    /// this is a pure append to the seam.</para>
    /// </summary>
    public Terminal? EndedOn { get; init; }

    /// <summary>
    /// How many PUTBACK attempts this possession's walk made before it ended or
    /// parked — the re-entrant-loop depth counter (PutBack → Roll H → miss → Roll I →
    /// OffensiveRebound → PutBack …). Zero on the overwhelming majority of
    /// possessions. The harness reads this to PROVE the nested putback↔rebound loop
    /// converges (a decaying tail and a bounded max). Init-only with a 0 default, so
    /// every existing construction is untouched — a pure append, like <see cref="EndedOn"/>.
    /// </summary>
    public int PutbackAttempts { get; init; }

    /// <summary>
    /// How many times Roll L was spun resolving this possession's trip to the line —
    /// the FT-loop spin count, an observability counter exactly parallel to
    /// <see cref="PutbackAttempts"/>. Zero on the overwhelming majority of possessions
    /// (no foul trip). And-1 = 1, fouled two / double bonus = 2, fouled three = 3,
    /// 1-and-1 = 1 (front miss) or 2 (front make). The harness reads this to PROVE the
    /// shot count derived at the FT entry edge is correct per trip type and that the
    /// hard ≤ 3 bound holds observably (not only via the in-engine assert). Init-only
    /// with a 0 default, so every existing construction is untouched — a pure append,
    /// like <see cref="EndedOn"/> and <see cref="PutbackAttempts"/>.
    /// </summary>
    public int FreeThrowSpins { get; init; }

    /// <summary>
    /// Points scored on this possession's walk — made field goals (2 or 3 by zone) plus
    /// made free throws (1 each) — accumulated by the resolver and surfaced here so the
    /// Governor can credit the offense without re-deriving from the terminal alone (the
    /// and-1 basket and intermediate FT makes are invisible on the terminal). Init-only
    /// with a 0 default, so every existing construction is untouched — a pure append,
    /// like <see cref="PutbackAttempts"/> and <see cref="FreeThrowSpins"/>.
    /// </summary>
    public int Points { get; init; }

    /// <summary>
    /// The number of shot-clock periods this possession's walk used — 1 for any
    /// possession that never got an offensive rebound, plus one additional period
    /// per offensive rebound (each rebound resets the clock to 20 and starts a new
    /// period). Init-only with a 0 default (the Governor treats it as
    /// <c>Max(1, periods)</c> defensively), so every existing construction is
    /// untouched — a pure append, like <see cref="Points"/> and
    /// <see cref="FreeThrowSpins"/>. The Governor reads this to draw per-period time.
    /// </summary>
    public int ShotClockPeriods { get; init; }

    // ── Shot and rebound counters (v2 observability) ───────────────────────────
    // All ten are init-only with 0 defaults — a pure append following the same
    // pattern as Points / FreeThrowSpins / ShotClockPeriods. Every existing
    // positional construction (new RoutingOutcome(false, "STUB:…")) is untouched.

    /// <summary>
    /// Field-goal attempts on this possession — box-score definition: all six
    /// <see cref="ShotResult"/> values EXCEPT <see cref="ShotResult.MissFouled"/>.
    /// A fouled miss sends the shooter to the line with no FGA charged (charging
    /// both a missed FGA and the resulting free throws would double-count the trip).
    /// A blocked shot, an and-1, and a ball deflected OOB all count as attempts.
    /// </summary>
    public int Fga { get; init; }

    /// <summary>
    /// Field goals made on this possession — <see cref="ShotResult.Made"/> and
    /// <see cref="ShotResult.MadeAndFouled"/> only. The and-1 basket counts as a
    /// make; the bonus free throw does not (that is an FTA/FTM).
    /// </summary>
    public int Fgm { get; init; }

    /// <summary>
    /// Three-point attempts on this possession — the subset of <see cref="Fga"/>
    /// whose stamped <see cref="ShotLocation"/> is <see cref="ShotLocation.Three"/>.
    /// A fouled missed three is NOT a 3PA (MissFouled is excluded from FGA).
    /// </summary>
    public int ThreePa { get; init; }

    /// <summary>
    /// Three-point makes on this possession — the subset of <see cref="Fgm"/>
    /// from the <see cref="ShotLocation.Three"/> zone.
    /// </summary>
    public int ThreePm { get; init; }

    /// <summary>
    /// Total Roll H resolutions on this possession — all seven
    /// <see cref="ShotResult"/> outcomes. Used as the denominator-guard identity:
    /// <c>Fga + MissFouled == ShotResolutions</c> (any deviation means the FGA
    /// definition drifted). Equal to <c>Fga + MissFouled</c> by construction.
    /// </summary>
    public int ShotResolutions { get; init; }

    /// <summary>
    /// Count of <see cref="ShotResult.MissFouled"/> resolutions on this possession
    /// — the one ShotResult excluded from <see cref="Fga"/>. Exists solely to close
    /// the denominator-guard identity (the reconciliation check cannot see it because
    /// MissFouled scores zero points).
    /// </summary>
    public int MissFouled { get; init; }

    /// <summary>
    /// Free-throw attempts on this possession — every Roll L spin across all FT
    /// trips (bonus and shooting-foul). A one-and-one front-end miss is 1 FTA; a
    /// made front end triggers a second spin (another FTA). An and-1 is 1 FTA.
    /// A fouled two/three is 2/3 FTA.
    /// </summary>
    public int Fta { get; init; }

    /// <summary>
    /// Free throws made on this possession — each Roll L spin that resolves to a
    /// make. Equal to the <c>ftPoints</c> value that <c>DriveFreeThrows</c> already
    /// tallies internally (<c>ftPoints == ftMakes</c> — surfacing an existing count,
    /// not deriving a new one).
    /// </summary>
    public int Ftm { get; init; }

    /// <summary>
    /// Phase 51 — FTA-source classification. Every free-throw attempt on this
    /// possession lands in exactly one of these five buckets, so they reconcile to
    /// <see cref="Fta"/> on every possession:
    /// <c>FtaBonusPicker + FtaBonusSelected + FtaBonusUnattributed +
    /// FtaShootingSelected + FtaShootingNoSlot == Fta</c> (asserted per-record and in
    /// aggregate by the Observation run). Init-only with a 0 default, so every existing
    /// positional construction is untouched — a pure append, like every prior field.
    /// <list type="bullet">
    ///   <item><see cref="FtaBonusPicker"/> — bonus trip whose shooter was named by
    ///         <see cref="FouledPlayerPicker"/> (the Phase 51 path; on populated rosters
    ///         this is where the old pre-Roll-E unattributed FTA now lands).</item>
    ///   <item><see cref="FtaBonusSelected"/> — bonus trip where Roll E had already
    ///         selected the shooter (a post-Roll-E bonus foul).</item>
    ///   <item><see cref="FtaBonusUnattributed"/> — bonus trip with no shooter at all
    ///         (an empty-roster isolation game — the residual flat-fallback FTA, which
    ///         collapses to ~0 on populated rosters).</item>
    ///   <item><see cref="FtaShootingSelected"/> — shooting-foul trip with the normal
    ///         selected shooter.</item>
    ///   <item><see cref="FtaShootingNoSlot"/> — shooting-foul trip with no selected
    ///         slot (the existing post-FT-rebound putback exception, unchanged).</item>
    /// </list>
    /// </summary>
    public int FtaBonusPicker { get; init; }

    /// <summary>See <see cref="FtaBonusPicker"/> — bonus trip, Roll E shooter selected.</summary>
    public int FtaBonusSelected { get; init; }

    /// <summary>See <see cref="FtaBonusPicker"/> — bonus trip, no shooter (empty roster).</summary>
    public int FtaBonusUnattributed { get; init; }

    /// <summary>See <see cref="FtaBonusPicker"/> — shooting-foul trip, selected shooter.</summary>
    public int FtaShootingSelected { get; init; }

    /// <summary>See <see cref="FtaBonusPicker"/> — shooting-foul trip, no-slot exception.</summary>
    public int FtaShootingNoSlot { get; init; }

    /// <summary>
    /// Offensive-rebound chances on this possession — the count of Roll I and Roll M
    /// resolutions that ended in either <see cref="ReboundOutcome.DefensiveRebound"/>
    /// (defense won) or <see cref="ReboundOutcome.OffensiveRebound"/> (offense won).
    /// Loose-ball-foul, OOB, and jump-ball arms are not a secured board and are
    /// excluded (matches the box-score convention for individual ORB%).
    /// </summary>
    public int OrbChances { get; init; }

    /// <summary>
    /// Offensive rebounds won on this possession — Roll I or Roll M resolutions
    /// where the <see cref="ContinuationKind.ResolveOffensiveRebound"/> arm fired
    /// (offense secured the board). The team rate is
    /// <c>OrbWon / OrbChances</c> across all possessions.
    /// </summary>
    public int OrbWon { get; init; }

    // ── Per-zone field-goal counters (shot-mix observability) ──────────────────
    // A make/attempt pair for each of the four non-Three zones. The Three zone
    // reuses the existing <see cref="ThreePa"/>/<see cref="ThreePm"/> pair, so all
    // five zones are covered without redundancy. Each FGA bins to exactly one zone
    // (the harness asserts RimFga+ShortFga+MidFga+LongFga+ThreePa == Fga), so per-zone
    // FG% (zoneFgm/zoneFga) and attempt share (zoneFga/Fga) both fall out. All eight
    // are init-only with 0 defaults — a pure append, like the v1 counters.

    /// <summary>Rim-zone field-goal attempts (the <see cref="ShotLocation.Rim"/> subset of <see cref="Fga"/>).</summary>
    public int RimFga { get; init; }
    /// <summary>Rim-zone field goals made (the <see cref="ShotLocation.Rim"/> subset of <see cref="Fgm"/>).</summary>
    public int RimFgm { get; init; }
    /// <summary>Short-zone field-goal attempts (the <see cref="ShotLocation.Short"/> subset of <see cref="Fga"/>).</summary>
    public int ShortFga { get; init; }
    /// <summary>Short-zone field goals made (the <see cref="ShotLocation.Short"/> subset of <see cref="Fgm"/>).</summary>
    public int ShortFgm { get; init; }
    /// <summary>Mid-zone field-goal attempts (the <see cref="ShotLocation.Mid"/> subset of <see cref="Fga"/>).</summary>
    public int MidFga { get; init; }
    /// <summary>Mid-zone field goals made (the <see cref="ShotLocation.Mid"/> subset of <see cref="Fgm"/>).</summary>
    public int MidFgm { get; init; }
    /// <summary>Long-two-zone field-goal attempts (the <see cref="ShotLocation.Long"/> subset of <see cref="Fga"/>).</summary>
    public int LongFga { get; init; }
    /// <summary>Long-two-zone field goals made (the <see cref="ShotLocation.Long"/> subset of <see cref="Fgm"/>).</summary>
    public int LongFgm { get; init; }

    // ── Per-slot FGA counters (usage observability) ──────────────────────────
    // One counter per on-court slot (1–5): the count of FGAs taken by that
    // slot on this possession. Accumulated at the Roll H chokepoint (the single
    // path every field-goal attempt passes through, including putbacks).
    //
    // Putback attribution: on a putback, Roll K's PutBack arm carries SelectedSlot
    // untouched by design ("same-player rebound tilt" — Roll K comment). So putback
    // FGAs are credited to the original Roll E selection, which is the correct
    // behavior for this model. This is slot observability under a fixed-lineup
    // assumption; it is NOT durable per-player identity tracking. When substitutions
    // arrive, a slot may be occupied by different players across a game, and this
    // counter will combine all of them — a separate player-ID layer is required then.
    //
    // Sums to Fga (the harness asserts this — slot-bin integrity check). That check
    // proves every FGA received exactly one valid slot bin (completeness). Shooter-
    // identity correctness is established by the Roll K source proof (see §0).
    // Init-only with 0 defaults — existing constructions untouched.
    public int Slot1Fga { get; init; }
    public int Slot2Fga { get; init; }
    public int Slot3Fga { get; init; }
    public int Slot4Fga { get; init; }
    public int Slot5Fga { get; init; }
    /// <summary>
    /// FGAs on this possession where <see cref="PossessionState.SelectedSlot"/> was null
    /// at the Roll H chokepoint — the shooter's slot could not be attributed. Occurs
    /// exclusively on bonus-free-throw possessions where Roll E was never called (a
    /// pre-shot foul sent the team to the line before a shooter was selected) and the
    /// last free throw was missed, producing an offensive rebound (Roll M) and a
    /// putback (Roll K PutBack arm). Tracked separately so the completeness invariant
    /// holds: Slot1Fga+…+Slot5Fga+SlotUnattributedFga == Fga (every FGA is accounted
    /// for even when no slot can be named). When per-player identity tracking lands,
    /// these will be attributed to whoever took the putback.
    /// </summary>
    public int SlotUnattributedFga { get; init; }

    // ── Per-slot FGM counters (efficiency observability — Phase 22) ───────────
    // One counter per on-court slot (1–5): the count of FGMs by that slot on this
    // possession. Accumulated at the same Roll H chokepoint as the per-slot FGA
    // counters, inside the Made/MadeAndFouled branch (makes only). Combined with
    // the Phase 21 per-slot FGA counters, per-slot FG% = SlotNFgm / SlotNFga.
    //
    // Same attribution model and same fixed-lineup caveat as the FGA counters:
    // a putback make is credited to the original Roll E shooter's slot (Roll K
    // carries SelectedSlot untouched). Null-slot makes (bonus-FT putback where
    // Roll E never ran) land in SlotUnattributedFgm — the make-side analog of
    // SlotUnattributedFga.
    //
    // Completeness invariant (harness-asserted): Slot1Fgm+…+Slot5Fgm+
    // SlotUnattributedFgm == Fgm. Subset invariant (ASSERTED in harness):
    // SlotUnattributedFgm <= SlotUnattributedFga and per-slot Fgm <= Fga (a make
    // requires an attempt; catches slot-level mis-attribution completeness misses).
    // Init-only with 0 defaults — existing constructions untouched.
    public int Slot1Fgm { get; init; }
    public int Slot2Fgm { get; init; }
    public int Slot3Fgm { get; init; }
    public int Slot4Fgm { get; init; }
    public int Slot5Fgm { get; init; }
    public int SlotUnattributedFgm { get; init; }

    // ── Phase 23 attribution support ──────────────────────────────────────────
    // 3PA/3PM/FTA/FTM: exact per-slot counters (no draws — same pattern as FGA/FGM).
    // BlkCount: how many shots were blocked (used for reconciliation — BlkBySlot.Total == BlkCount).
    // TurnoverOffSlot / TurnoverWasLiveBall: metadata for TO attribution.
    // No IRng is consumed anywhere in this group.
    public SlotGroup ThreePaBySlot  { get; init; }  // offense: 3PA per slot
    public SlotGroup ThreePmBySlot  { get; init; }  // offense: 3PM per slot
    public SlotGroup FtaBySlot      { get; init; }  // offense: FTA per slot
    public SlotGroup FtmBySlot      { get; init; }  // offense: FTM per slot
    /// <summary>How many shots were blocked this possession. Used by the harness
    /// for the reconciliation invariant: <see cref="BlkBySlot"/>.Total == BlkCount
    /// on every possession.</summary>
    public int BlkCount { get; init; }
    /// <summary>Per-slot block counts for this possession — stamped by
    /// <see cref="BlockerPicker"/> at the <c>ShotResult.Blocked</c> site (Phase 36),
    /// retiring the last harness <c>WeightedDraw</c>.
    /// <para><c>BlkBySlot.Total</c> equals <see cref="BlkCount"/> on every possession
    /// (the harness asserts this). Init-only with a <c>default</c> (all-zero)
    /// SlotGroup, so every existing positional construction
    /// (<c>new RoutingOutcome(false, "STUB:…")</c>) is untouched — a pure append,
    /// like every prior init field.</para></summary>
    public SlotGroup BlkBySlot { get; init; }
    /// <summary>The offensive slot that committed the turnover. Null for team
    /// violations (FiveSecondInbound / TenSecondBackcourt / ShotClockViolation —
    /// no individual credit). Set by TurnoverCommitterPicker (Phase 33) for
    /// ball-handler violations and by TurnoverInteriorPicker (Phase 34) for
    /// interior/post violations. When Roll E already selected a player the slot
    /// is read directly from SelectedSlot.</summary>
    public int? TurnoverOffSlot { get; init; }
    /// <summary>True if the possession ended on a live-ball steal terminal
    /// (BadPassIntercepted or LostBallLiveBall). When true, <see cref="StealerSlot"/>
    /// carries the engine-stamped stealer credit (Phase 34).</summary>
    public bool TurnoverWasLiveBall { get; init; }

    // ── Phase 25: shooting-foul events ───────────────────────────────────────
    /// <summary>
    /// Every shooting-foul event that occurred during this possession's walk —
    /// one entry per <see cref="ShotResult.MadeAndFouled"/> or
    /// <see cref="ShotResult.MissFouled"/> resolution. Empty (never null) on the
    /// overwhelming majority of possessions. A possession extended by a putback
    /// (Roll K → Roll H) can carry more than one entry.
    /// <para>Init-only with an empty-array default, so every existing positional
    /// construction (<c>new RoutingOutcome(false, "STUB:…")</c>) is untouched —
    /// a pure append, like every prior init field.</para>
    /// </summary>
    public IReadOnlyList<ShootingFoulEvent> ShootingFouls { get; init; } = Array.Empty<ShootingFoulEvent>();

    // ── Phase 31: offensive-rebounder attribution ─────────────────────────────
    /// <summary>
    /// Per-slot offensive-rebound counts for this possession — stamped by
    /// <see cref="OffensiveRebounderPicker"/> at the shared
    /// <c>ContinuationKind.ResolveOffensiveRebound</c> node each time the offense
    /// secures a board (both Roll I and Roll M feeders converge there).
    /// <para><c>OrbBySlot.Total</c> equals <see cref="OrbWon"/> on every possession
    /// (the harness asserts this). Init-only with a <c>default</c> (all-zero)
    /// SlotGroup, so every existing positional construction
    /// (<c>new RoutingOutcome(false, "STUB:…")</c>) is untouched — a pure append,
    /// like every prior init field.</para>
    /// </summary>
    public SlotGroup OrbBySlot { get; init; }

    /// <summary>The defensive slot that earned the steal on a live-ball turnover possession
    /// (BadPassIntercepted or LostBallLiveBall). Null on all other possession endings.
    /// Stamped by <see cref="StealerPicker"/> at the Terminal stamp block (Phase 34).</summary>
    public int? StealerSlot { get; init; }

    /// <summary>The defensive slot that earned the defensive rebound. Non-null on every
    /// possession ending on <c>"DefensiveRebound"</c>; null on all other endings.
    /// Stamped by <see cref="DefensiveRebounderPicker"/> at the Terminal stamp block
    /// (Phase 35) — retires the last post-hoc rebound draw.</summary>
    public int? DefensiveRebounderSlot { get; init; }

    /// <summary>Per-slot assist counts for this possession — stamped by
    /// <see cref="AssistPicker"/> on-walk at every eligible made field goal
    /// (Phase 39). An eligible make is non-putback with a non-null
    /// <c>SelectedSlot</c>; the shooter is excluded from the pick.
    /// <para><c>AstBySlot.Total</c> is at most <see cref="Fgm"/> on every
    /// possession (harness-asserted). Init-only with a <c>default</c> (all-zero)
    /// SlotGroup, so every existing positional construction
    /// (<c>new RoutingOutcome(false, "STUB:…")</c>) is untouched — a pure append,
    /// like every prior init field.</para></summary>
    public SlotGroup AstBySlot { get; init; }
}
