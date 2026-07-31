using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Session 76 — THE MINUTES ALLOCATOR.
//
//  Replaces FlatFatigueFencePolicy (S52), which substituted on a fatigue line and
//  nothing else: five men took ~88% of every team's possessions, so every number on
//  the season page described a league where the average man on the floor plays 35
//  minutes. This policy distributes minutes toward a per-player target instead.
//
//  ★ THE MINUTE VALUES BELOW ARE PLACEHOLDERS, NOT CALIBRATION. What is ruled is the
//  MODEL'S SHAPE — one target per player, targets assigned by stored-group depth,
//  a residual control signal, bounded cascades, game-local horizon — because shape is
//  expensive to change later and numbers are not. The eventual coaching layer sets the
//  target vector; this is the controller that chases whatever vector it is handed.
//
//  ── The control signal: a time-dependent residual ────────────────────────────
//  After R completed possession records IN THIS GAME:
//
//      planned[i]  = targetShare[i] x Lineup.Size x R
//      residual[i] = planned[i] - actual[i]          POSITIVE MEANS BEHIND PLAN
//
//  This is NOT "realized share minus target share", which is wildly volatile early —
//  after one record every on-floor player reads 20% and every bench player 0%, so a
//  4-minute man and a 32-minute man show the identical error. The residual grows
//  smoothly with elapsed possessions and asks the right question: who is furthest
//  behind the floor time he should have accumulated BY NOW? Overtime needs no special
//  case; R simply keeps increasing.
//
//  ── ★ The horizon is GAME-LOCAL (Emmett's ruling) ───────────────────────────
//  R and the credits are THIS GAME'S. The residual resets at tipoff and no debt
//  crosses a game boundary. The criterion was "whatever makes the eventual coaching
//  layer simpler": every deferred coaching behaviour (foul trouble, a cold shooter,
//  a blowout, a close finish) is a WITHIN-GAME reactor, and a season ledger would
//  force that layer to reason about two horizons at once and to FIGHT the books —
//  the coach wants to sit the 13th man in a two-point game while the ledger says he
//  is owed forty credits from three weeks ago. DNPs are the coaching layer's job to
//  produce, not a bookkeeping artifact.
//
//  ── ★ THE FATIGUE RECOVERY CARRY-OVER (do not delete this) ──────────────────
//  RecoverBenched below is the ONLY driver of off-floor fatigue recovery in the whole
//  production tree. The engine rests only the ON-FLOOR five (Governor's halftime
//  call); every other Recover call site is inside a check fixture. It lived in the
//  fence, and deleting the fence without carrying this across would mean benched
//  players never recover for the rest of the season. That compiles, runs, and passes
//  every existing check — it merely makes the entire bench permanently exhausted.
//
//  ── Determinism ─────────────────────────────────────────────────────────────
//  The policy draws no RNG and reads no wall clock. Every choice is a pure function
//  of (targets, credits, stints, fatigue, positions, seat occupancy); ties break down
//  a fully-specified ladder ending in a (playerId, seat) tuple. A replayed seed
//  reproduces the identical substitution sequence.
// ============================================================================

internal sealed class MinutesAllocatorPolicy : ISubstitutionPolicy
{
    // ── Stability constants (placeholders, approved at the S76 check-in gate) ────
    //
    // Expressed in PLAYER-POSSESSION CREDITS, never ambiguously in "share points" or
    // "projected minute error". One credit is one possession record with the player on
    // the floor; at a typical 140-record game a team issues 700 credits, and a nominal
    // minute is 700/40 = 17.5 credits per 40 minutes, i.e. ~0.286 nominal minutes per
    // credit.
    //
    // The pairing is what matters, not either value alone. The SMALLEST target sets the
    // ceiling on ExitThreshold: a man on the floor gains 1 actual credit per record while
    // earning only (target/200) x 5 planned, so he drifts toward surplus at (1 - rate)
    // per record. Entering at residual E and serving N records he leaves at E - N(1-rate).
    // For the 4-minute tail (rate 0.10) at Enter 2.5 / stint 4 that is -1.10, so
    // ExitThreshold must be at or under 1.10 or he cannot leave at first eligibility and
    // overshoots. 0.75 is chosen over the 1.10 ceiling to leave real headroom.
    internal const double EnterThreshold = 2.5;   // entrant must be behind plan by at least this
    internal const double ExitThreshold  = 0.75;  // exiting man must be AHEAD of plan by at least this
    internal const int    MinimumStint   = 4;     // records an entrant is protected for (~1.14 min)

    // ★ SIGN CONVENTION, stated so it cannot be inverted in code.
    //      residual = planned - actual, so POSITIVE MEANS BEHIND PLAN.
    //      entrant:  residual >=  EnterThreshold     (behind by at least this much)
    //      exiting:  residual <= -ExitThreshold      (ahead  by at least this much)
    // "Ahead by at least ExitThreshold" is unambiguous in prose and a one-character
    // mistake in code. Use the signed form.

    // ── ★ PLACEHOLDER TARGET TABLES — a ten-man rotation ─────────────────────────
    //
    // Emmett's ruling (2026-07-26): the rotation is TEN. The bottom guard, wing and big
    // on each roster hold a target of ZERO. A 13th man on a scholarship roster gets a
    // DNP most nights in real basketball; giving him a token minute was a testing
    // convenience, not a basketball reason.
    //
    // ★ NAMED CONSEQUENCE, recorded on purpose: until foul-outs land (S77) and injuries
    // exist, NOTHING can call on the three zero-target men. A zero target means planned
    // is always 0, so their residual can never reach a positive EnterThreshold and they
    // are STRUCTURALLY unable to check in. They are inert code until S77 gives them a
    // reason to play. This is accepted; realism at the bottom of the roster is the
    // coaching layer's job.
    //
    // The ladder is a fixed ten-slot shape summing to exactly 200:
    //     32 30 28 26 24 20 16 12 8 4
    // distributed across the stored groups in the proportion the opening-lineup
    // composition implies, so a shape with three guard seats gives group G three of the
    // five leading slots. Four guards, three wings and three bigs hold targets.
    //
    // ★ HOW TARGETS ARE ASSIGNED AT RUNTIME: strictly by stored-group depth rank.
    // "Guards 1..4" means the four guards ranked WITHIN GROUP G. Tipoff status and
    // acquisition order do not affect target ownership — the rank-blind opening five
    // gets no claim on the leading targets merely by starting. The seat composition
    // decided the table's SHAPE; it never decides WHICH PLAYER holds a value.
    private static readonly Dictionary<string, (double[] G, double[] W, double[] B)> ShapeTargets = new()
    {
        // shape        guards                wings              bigs
        ["3G/1W/1B"] = (new[] { 32.0, 30, 28, 20 }, new[] { 26.0, 16, 8 }, new[] { 24.0, 12, 4 }),
        ["2G/2W/1B"] = (new[] { 32.0, 30, 20,  8 }, new[] { 28.0, 26, 16 }, new[] { 24.0, 12, 4 }),
        ["2G/1W/2B"] = (new[] { 32.0, 30, 20,  8 }, new[] { 28.0, 16,  4 }, new[] { 26.0, 24, 12 }),
    };

    /// <summary>Full-game minutes a side distributes across its five seats.</summary>
    internal const double SideMinutes = 200.0;

    private readonly SideState _home;
    private readonly SideState _away;
    private readonly double _halftimeRestEquivalentSeconds;

    public MinutesAllocatorPolicy(SideDepth home, SideDepth away, double halftimeRestEquivalentSeconds)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(away);
        _home = new SideState(home);
        _away = new SideState(away);
        _halftimeRestEquivalentSeconds = halftimeRestEquivalentSeconds;
    }

    /// <summary>Per-side accounting, for the diagnostics and the Phase 72 gates.</summary>
    internal SideState StateFor(TeamSide side) =>
        side == TeamSide.Home ? _home
      : side == TeamSide.Away ? _away
      : throw new ArgumentOutOfRangeException(nameof(side), side, "Home or Away.");

    // ── ISubstitutionPolicy ──────────────────────────────────────────────────────

    public void OnPossessionBoundary(GameState game, int nextPossessionNumber, double elapsedSeconds, bool isDeadBall)
    {
        foreach (var st in new[] { _home, _away })
        {
            // The possession that just ended is a completed RECORD: credit the five who
            // were on the floor for it, then advance R. The engine already accrued their
            // fatigue at the possession tail — never recover an on-floor player here.
            CreditOnFloor(game, st);

            // Off-floor recovery by this possession's elapsed seconds (see the header
            // note — this is the only production driver of bench recovery).
            RecoverBenched(game, st, elapsedSeconds);

            // ★ S87: a foul-out is not a rotation decision, and it does not wait for a
            // dead ball. Every foul IS a whistle, so a man who has just fouled out is
            // leaving the floor at the next trip whatever the ball is doing — this runs
            // ahead of the ordinary move and ignores the isDeadBall gate (Emmett's
            // ruling). The one gap that remains is that he finishes the trip his fifth
            // foul landed in: the engine's only substitution seam is between trips.
            ForceFoulOutReplacements(game, st, nextPossessionNumber);

            // Ordinary rotation substitutions are legal only from a dead ball.
            if (isDeadBall)
                EvaluateOneMove(game, st, nextPossessionNumber);
        }
    }

    public void OnPeriodBreak(GameState game, int nextPossessionNumber, double finalPossessionElapsedSeconds, PeriodBreakKind kind)
    {
        foreach (var st in new[] { _home, _away })
        {
            // The possession that ENDED the period is also a completed record.
            CreditOnFloor(game, st);

            // The final possession's recovery slice for the benched — the per-possession
            // recovery the (suppressed) ordinary boundary callback would otherwise apply.
            RecoverBenched(game, st, finalPossessionElapsedSeconds);

            // The matching period rest. The engine has already rested the on-floor five
            // (halftime only). Halftime -> the same chunk for the bench; overtime ->
            // nothing, because recovery is regulation-only and benched and on-floor are
            // treated alike there.
            if (kind == PeriodBreakKind.Halftime)
                RecoverBenched(game, st, _halftimeRestEquivalentSeconds);

            // S87: foul-outs clear first here too — a period break is a boundary like
            // any other, and it is also the recovery point for a man the escape hatch
            // left on the floor because no reserve was available earlier.
            ForceFoulOutReplacements(game, st, nextPossessionNumber);

            // A period break is a dead ball and a perfectly ordinary substitution
            // opportunity — unlike the retired fence, which allowed reclaim only.
            EvaluateOneMove(game, st, nextPossessionNumber);
        }
    }

    // ── Credit accounting ────────────────────────────────────────────────────────

    private static void CreditOnFloor(GameState game, SideState st)
    {
        var roster = game.RosterFor(st.Depth.Side);
        for (var slot = 1; slot <= Lineup.Size; slot++)
        {
            var p = roster.PlayerAt(new Slot(st.Depth.Side, slot));
            if (p is null) continue;

            st.ActualCredits[p.PlayerId] = st.ActualCredits.GetValueOrDefault(p.PlayerId) + 1;
            st.StintRecords[p.PlayerId]  = st.StintRecords.GetValueOrDefault(p.PlayerId) + 1;

            // S87: a trip played by a man who has already fouled out. Non-zero only via
            // the escape hatch (no eligible reserve) or the one trip he finishes after
            // his fifth foul. The season page reports it; nothing asserts a target.
            if (game.PersonalFouls.IsDisqualified(p.PlayerId))
                st.PossessionsPlayedWhileDisqualified++;

            // Seat-split accounting: which SEAT TYPE the credit was earned in, so a man
            // who reaches his target through strange role usage is visible rather than
            // hidden inside a correct total.
            var seatType = st.Depth.SlotPos[slot];
            st.CreditsBySeat[p.PlayerId][seatType] = st.CreditsBySeat[p.PlayerId][seatType] + 1;

            if (PositionalEligibility.IsCrossPosition(st.Depth.PosById[p.PlayerId], seatType))
                st.CrossPositionCredits++;
            st.TotalSeatCredits++;
        }
        st.Records++;
    }

    private static void RecoverBenched(GameState game, SideState st, double elapsedSeconds)
    {
        var onFloor = OnFloorIds(game, st.Depth);
        var benched = new List<Player?>();
        foreach (var kv in st.Depth.PlayerById)
            if (!onFloor.Contains(kv.Key)) benched.Add(kv.Value);
        if (benched.Count > 0)
            game.Fatigue.Recover(benched, elapsedSeconds);
    }

    // ── S87: foul-out replacement ────────────────────────────────────────────────
    //
    //  A man who has fouled out comes off. This is NOT the rotation deciding something
    //  — it is a rule, and it runs ahead of the rotation with a different set of
    //  filters:
    //
    //    KEPT   — positional eligibility, and the legal-lineup check. You never get four
    //             bigs because somebody fouled out.
    //    DROPPED— the minutes plan (Residual / EnterThreshold / ExitThreshold), the
    //             minimum stint, and the one-move-per-boundary limit. A coach filling a
    //             hole is not managing minutes, and if two men are somehow both
    //             disqualified they both come off in the same pass.
    //
    //  SELECTION — "best available man at that position" (Emmett's ruling). Rank is
    //  comparable only WITHIN a stored group (see SideDepth's header), so the choice is
    //  tiered: exact positional match first, ordered by rank; cross-position eligible
    //  men only if no exact match exists. PlayerId breaks any tie, so the choice is
    //  deterministic and draws no randomness.
    //
    //  THE ESCAPE HATCH — if no eligible reserve exists (bench exhausted, or every
    //  candidate would break the lineup), the disqualified man STAYS ON THE FLOOR and
    //  nothing throws. That is counted once per man, and it is not permanent: every
    //  later boundary retries, so the moment a body frees up he is removed.

    private void ForceFoulOutReplacements(GameState game, SideState st, int atPossession)
    {
        // A substitution is legal only from possession 2 onward (possession 1 is the
        // opening seat, which is SetStarter's job) — the same rule EvaluateOneMove uses.
        if (atPossession < 2) return;

        var roster = game.RosterFor(st.Depth.Side);
        var pf     = game.PersonalFouls;

        var seatOcc = new int[Lineup.Size + 1];
        for (var slot = 1; slot <= Lineup.Size; slot++)
        {
            var p = roster.PlayerAt(new Slot(st.Depth.Side, slot));
            if (p is null) return;                 // defensive; a seated slot is never null
            seatOcc[slot] = p.PlayerId;
        }

        for (var seat = 1; seat <= Lineup.Size; seat++)
        {
            var occupantId = seatOcc[seat];
            if (!pf.IsDisqualified(occupantId)) continue;

            var seatType = st.Depth.SlotPos[seat];
            var onFloor  = new HashSet<int>(seatOcc.Skip(1));

            var replacementId = PickForcedReplacement(st, pf, onFloor, seatOcc, seat, seatType);

            if (replacementId < 0)
            {
                // The escape hatch. Count the man once, never again — a second boundary
                // that still cannot replace him is the SAME unresolved situation, not a
                // second one.
                if (st.UnreplaceableDisqualified.Add(occupantId))
                {
                    st.R4Occurrences++;
                    st.Log.Add($"P{atPossession} FOUL-OUT UNREPLACED seat{seat} id{occupantId} " +
                               $"({pf.CountFor(occupantId)} PF) — no eligible reserve, stays on floor");
                }
                continue;
            }

            roster.Substitute(new Slot(st.Depth.Side, seat), st.Depth.PlayerById[replacementId], atPossession);

            // Bookkeeping mirrors ApplyMove's: the entrant starts a fresh stint, the
            // exiting man's stint is closed and recorded.
            if (st.StintRecords.TryGetValue(occupantId, out var stint) && stint > 0)
                st.StintLengths.Add(stint);
            st.StintRecords[occupantId]    = 0;
            st.StintRecords[replacementId] = 0;

            seatOcc[seat] = replacementId;
            st.Substitutions++;
            st.FoulOutReplacements++;
            // If he was previously stranded by the escape hatch, he is no longer — the
            // recovery rule fired. Clearing the flag keeps the R4 count a count of MEN
            // stranded, not of boundaries.
            st.UnreplaceableDisqualified.Remove(occupantId);
            st.Log.Add($"P{atPossession} FOUL-OUT seat{seat} out id{occupantId} " +
                       $"({pf.CountFor(occupantId)} PF) in id{replacementId}");
        }
    }

    /// <summary>
    /// Best available man for a seat whose occupant has fouled out, or -1 if there is
    /// none. Never returns a disqualified man, a man already on the floor, or a man whose
    /// entry would make the lineup illegal.
    /// </summary>
    private static int PickForcedReplacement(
        SideState st, PersonalFoulTracker pf, HashSet<int> onFloor,
        int[] seatOcc, int seat, string seatType)
    {
        var exact = -1; var exactRank = double.NegativeInfinity;
        var cross = -1; var crossRank = double.NegativeInfinity;

        foreach (var id in st.Depth.PlayerById.Keys)
        {
            if (onFloor.Contains(id)) continue;
            if (pf.IsDisqualified(id)) continue;                       // never seat a fouled-out man
            var pos = st.Depth.PosById[id];
            if (!PositionalEligibility.IsEligibleForSeat(pos, seatType)) continue;

            var after = (int[])seatOcc.Clone();
            after[seat] = id;
            if (!IsLegalLineup(st, after)) continue;

            var rank = st.Depth.RankById.GetValueOrDefault(id, double.NegativeInfinity);

            if (pos == seatType)
            {
                // Same stored group as the seat — ranks are comparable here.
                if (rank > exactRank || (rank == exactRank && (exact < 0 || id < exact)))
                { exact = id; exactRank = rank; }
            }
            else
            {
                if (rank > crossRank || (rank == crossRank && (cross < 0 || id < cross)))
                { cross = id; crossRank = rank; }
            }
        }

        return exact >= 0 ? exact : cross;
    }

    // ── The move set ─────────────────────────────────────────────────────────────
    //
    //  STRAIGHT SUBSTITUTION — a bench player replaces a seat's occupant; both must be
    //  eligible for that seat type.
    //
    //  ★ CASCADE — three players, two seats. Choose destination seat A and source seat B:
    //      1. the occupant of A EXITS to the bench;
    //      2. the occupant of B RELOCATES into A, and must be eligible for A's seat type;
    //      3. a BENCH player ENTERS B, and must be eligible for B's seat type.
    //  This is "the backup center checks in, my starting center moves to the 4". All
    //  THREE players enter objective scoring, stint validation, legality checks, logging
    //  and target accounting — naming only the relocating and entering players would
    //  leave the displaced occupant unlogged, unprotected by minimum stint, and possibly
    //  still on the floor as a sixth man.
    //
    //  A cascade is ONE rotation move for the one-move-per-dead-ball limit. Chains of
    //  length three or more are refused.

    private readonly record struct Move(
        bool IsCascade,
        int  DestSeat,        // seat A — the one whose occupant exits
        int  SourceSeat,      // seat B — vacated by the relocating man (cascade only)
        int  ExitingId,
        int  RelocatingId,    // -1 for a straight substitution
        int  EnteringId,
        double Improvement,
        int  CrossAfter);

    private void EvaluateOneMove(GameState game, SideState st, int atPossession)
    {
        st.DeadBallsEvaluated++;

        // A substitution is legal only from possession 2 onward (possession 1 is the
        // opening seat, which is SetStarter's job, not Substitute's).
        if (atPossession < 2) return;

        var roster  = game.RosterFor(st.Depth.Side);
        var seatOcc = new int[Lineup.Size + 1];
        for (var slot = 1; slot <= Lineup.Size; slot++)
        {
            var p = roster.PlayerAt(new Slot(st.Depth.Side, slot));
            if (p is null) return;                 // defensive; a seated slot is never null
            seatOcc[slot] = p.PlayerId;
        }
        var onFloor = new HashSet<int>(seatOcc.Skip(1));

        var candidates = new List<Move>();
        var blockedByStint = false;
        var blockedByHysteresis = false;

        // ── Straight substitutions ───────────────────────────────────────────────
        for (var seat = 1; seat <= Lineup.Size; seat++)
        {
            var exitingId = seatOcc[seat];
            var seatType  = st.Depth.SlotPos[seat];

            if (st.StintRecords.GetValueOrDefault(exitingId) < MinimumStint) { blockedByStint = true; continue; }
            if (st.Residual(exitingId) > -ExitThreshold)                     { blockedByHysteresis = true; continue; }

            foreach (var entrantId in st.Depth.PlayerById.Keys)
            {
                if (onFloor.Contains(entrantId)) continue;
                // S87: a fouled-out man is never a candidate. The seat itself refuses him
                // too, so this filter is about not WANTING him rather than not being able
                // to seat him — but the rotation must not spend its one move on a man the
                // seat would reject.
                if (game.PersonalFouls.IsDisqualified(entrantId)) continue;
                if (!PositionalEligibility.IsEligibleForSeat(st.Depth.PosById[entrantId], seatType)) continue;
                if (st.Residual(entrantId) < EnterThreshold) { blockedByHysteresis = true; continue; }

                var after = (int[])seatOcc.Clone();
                after[seat] = entrantId;
                if (!IsLegalLineup(st, after)) continue;

                var improvement =
                      st.ProjectedAbs(exitingId, onFloorNext: true)  + st.ProjectedAbs(entrantId, onFloorNext: false)
                    - st.ProjectedAbs(exitingId, onFloorNext: false) - st.ProjectedAbs(entrantId, onFloorNext: true);

                candidates.Add(new Move(false, seat, 0, exitingId, -1, entrantId, improvement, CrossCount(st, after)));
            }
        }

        // ── Cascades ─────────────────────────────────────────────────────────────
        for (var dest = 1; dest <= Lineup.Size; dest++)
        {
            var exitingId = seatOcc[dest];
            var destType  = st.Depth.SlotPos[dest];

            if (st.StintRecords.GetValueOrDefault(exitingId) < MinimumStint) { blockedByStint = true; continue; }
            if (st.Residual(exitingId) > -ExitThreshold)                     { blockedByHysteresis = true; continue; }

            for (var source = 1; source <= Lineup.Size; source++)
            {
                if (source == dest) continue;
                var relocatingId = seatOcc[source];
                var sourceType   = st.Depth.SlotPos[source];

                // ★ The relocating player is EXEMPT from both thresholds — he never
                // leaves the floor, so he is neither an entrant nor an exit. His stint
                // does not reset either (see ApplyMove).
                if (!PositionalEligibility.IsEligibleForSeat(st.Depth.PosById[relocatingId], destType)) continue;

                // Anti-thrash: a man who relocated at the previous evaluation may not be
                // moved again at this one. Cascades widen the move set, so the anti-thrash
                // rules are TIGHTER, not looser.
                if (st.LastRelocationEvaluation.GetValueOrDefault(relocatingId, -99) == st.DeadBallsEvaluated - 1) continue;

                foreach (var entrantId in st.Depth.PlayerById.Keys)
                {
                    if (onFloor.Contains(entrantId)) continue;
                    if (game.PersonalFouls.IsDisqualified(entrantId)) continue;   // S87
                    if (!PositionalEligibility.IsEligibleForSeat(st.Depth.PosById[entrantId], sourceType)) continue;
                    if (st.Residual(entrantId) < EnterThreshold) { blockedByHysteresis = true; continue; }

                    var after = (int[])seatOcc.Clone();
                    after[dest]   = relocatingId;
                    after[source] = entrantId;
                    if (!IsLegalLineup(st, after)) continue;

                    // The relocating man is on the floor in BOTH scenarios, so his terms
                    // cancel — he is included for completeness, never for effect.
                    var improvement =
                          st.ProjectedAbs(exitingId, true)  + st.ProjectedAbs(entrantId, false) + st.ProjectedAbs(relocatingId, true)
                        - st.ProjectedAbs(exitingId, false) - st.ProjectedAbs(entrantId, true)  - st.ProjectedAbs(relocatingId, true);

                    candidates.Add(new Move(true, dest, source, exitingId, relocatingId, entrantId, improvement, CrossCount(st, after)));
                }
            }
        }

        if (blockedByStint)      st.BlockedByStint++;
        if (blockedByHysteresis) st.BlockedByHysteresis++;

        // ★ If no surviving move has strictly positive projected improvement, make NO
        // substitution. "Greatest reduction" must never mean "least harmful".
        var improving = candidates.Where(m => m.Improvement > 1e-12).ToList();
        if (improving.Count == 0) { st.NoImprovingMove++; return; }

        st.ImprovingMoveAvailable++;

        // ── Tie-break ladder, in order ───────────────────────────────────────────
        //  greatest improvement -> greatest entrant residual -> greatest exiting surplus
        //  -> lower entrant fatigue -> higher exiting fatigue -> fewer cross-position
        //  occupants after the move -> deterministic (player id, seat) tuple.
        //
        //  Ordering is over MOVES, not candidates: a cascade has three players, so a
        //  per-player key like "greatest deficit" is undefined for it until the move is
        //  the unit being scored. This creates no global quality scalar — residual is a
        //  planning remainder, not a rating.
        var best = improving
            .OrderByDescending(m => m.Improvement)
            .ThenByDescending(m => st.Residual(m.EnteringId))
            .ThenBy(m => st.Residual(m.ExitingId))
            .ThenBy(m => game.Fatigue.LevelFor(m.EnteringId))
            .ThenByDescending(m => game.Fatigue.LevelFor(m.ExitingId))
            .ThenBy(m => m.CrossAfter)
            .ThenBy(m => m.EnteringId)
            .ThenBy(m => m.DestSeat)
            .ThenBy(m => m.SourceSeat)
            .First();

        ApplyMove(game, st, best, atPossession);
    }

    // ★ A cascade applies ATOMICALLY. Legality was proved on the candidate before this
    // is called, so the two seat writes commit with no validation between them and no
    // intermediate state is ever visible to the resolver, the fatigue tracker, the stint
    // clocks, the substitution log, bench ordering or any diagnostic counter. A candidate
    // that failed validation was discarded above with literally zero side effects — it
    // never reached this method.
    private static void ApplyMove(GameState game, SideState st, Move m, int atPossession)
    {
        var roster = game.RosterFor(st.Depth.Side);

        if (m.IsCascade)
        {
            roster.Substitute(new Slot(st.Depth.Side, m.DestSeat),   st.Depth.PlayerById[m.RelocatingId], atPossession);
            roster.Substitute(new Slot(st.Depth.Side, m.SourceSeat), st.Depth.PlayerById[m.EnteringId],   atPossession);

            // ★ The relocating man's stint does NOT reset — he never left the floor.
            st.LastRelocationEvaluation[m.RelocatingId] = st.DeadBallsEvaluated;
            st.Cascades++;
            st.Log.Add($"CASCADE p{atPossession} {st.Depth.Side}: " +
                       $"exit id{m.ExitingId} seat{m.DestSeat} | " +
                       $"relocate id{m.RelocatingId} seat{m.SourceSeat}->seat{m.DestSeat} | " +
                       $"enter id{m.EnteringId} seat{m.SourceSeat}");
        }
        else
        {
            roster.Substitute(new Slot(st.Depth.Side, m.DestSeat), st.Depth.PlayerById[m.EnteringId], atPossession);
            st.Straights++;
            st.Log.Add($"STRAIGHT p{atPossession} {st.Depth.Side}: " +
                       $"exit id{m.ExitingId} seat{m.DestSeat} | enter id{m.EnteringId} seat{m.DestSeat}");
        }

        // The exiting man's stint ends; the entrant begins a new PROTECTED stint and
        // cannot be removed until it completes. The entrant has no PRE-entry stint
        // requirement — he is on the bench and has no on-court stint to have completed;
        // requiring one would either mean nothing or silently invent a minimum BENCH
        // duration.
        st.StintLengths.Add(st.StintRecords.GetValueOrDefault(m.ExitingId));
        st.StintRecords[m.ExitingId]  = 0;
        st.StintRecords[m.EnteringId] = 0;
        st.Substitutions++;
    }

    // ── Legality (applies to LIVE lineups, never to candidate construction) ──────
    //
    //  The lineup before the move and the atomically committed lineup after it must
    //  each contain five unique legal occupants, all on that side, one seat each, every
    //  occupant eligible for his seat. Temporary vacancies while BUILDING an uncommitted
    //  candidate are not live states — a three-step cascade necessarily passes through a
    //  four-player arrangement in scratch state, and requiring five there is impossible.
    private static bool IsLegalLineup(SideState st, int[] seatOcc)
    {
        var seen = new HashSet<int>();
        for (var slot = 1; slot <= Lineup.Size; slot++)
        {
            var pid = seatOcc[slot];
            if (!seen.Add(pid)) return false;                        // five UNIQUE
            if (!st.Depth.PlayerById.ContainsKey(pid)) return false; // all on THIS side
            if (!PositionalEligibility.IsEligibleForSeat(st.Depth.PosById[pid], st.Depth.SlotPos[slot]))
                return false;                                        // eligible for his seat
        }
        return true;
    }

    private static int CrossCount(SideState st, int[] seatOcc)
    {
        var n = 0;
        for (var slot = 1; slot <= Lineup.Size; slot++)
            if (PositionalEligibility.IsCrossPosition(st.Depth.PosById[seatOcc[slot]], st.Depth.SlotPos[slot])) n++;
        return n;
    }

    private static HashSet<int> OnFloorIds(GameState game, SideDepth side)
    {
        var roster = game.RosterFor(side.Side);
        var ids = new HashSet<int>();
        for (var slot = 1; slot <= Lineup.Size; slot++)
        {
            var p = roster.PlayerAt(new Slot(side.Side, slot));
            if (p is not null) ids.Add(p.PlayerId);
        }
        return ids;
    }

    // ── Per-side state ───────────────────────────────────────────────────────────

    internal sealed class SideState
    {
        public SideDepth Depth { get; }
        public string Shape { get; }

        /// <summary>PlayerId → his single planned share of the side's 200 minutes.</summary>
        public Dictionary<int, double> TargetShare { get; } = new();

        /// <summary>PlayerId → his target in nominal minutes (the readable form).</summary>
        public Dictionary<int, double> TargetMinutes { get; } = new();

        public Dictionary<int, int> ActualCredits { get; } = new();
        public Dictionary<int, int> StintRecords  { get; } = new();
        public Dictionary<int, Dictionary<string, int>> CreditsBySeat { get; } = new();
        public Dictionary<int, int> LastRelocationEvaluation { get; } = new();

        public int Records;
        public int Substitutions, Straights, Cascades;
        // S87: foul-out bookkeeping. FoulOutReplacements is a SUBSET of Substitutions
        // (a forced replacement is still a substitution). R4Occurrences counts MEN the
        // escape hatch stranded, never boundaries — UnreplaceableDisqualified is the
        // set that makes that true, and a man leaves it when the recovery rule fires.
        public int FoulOutReplacements;
        public int R4Occurrences;
        public int PossessionsPlayedWhileDisqualified;
        public readonly HashSet<int> UnreplaceableDisqualified = new();
        public int DeadBallsEvaluated, ImprovingMoveAvailable, BlockedByStint, BlockedByHysteresis, NoImprovingMove;
        public int CrossPositionCredits, TotalSeatCredits;
        public readonly List<int> StintLengths = new();
        public readonly List<string> Log = new();

        public SideState(SideDepth depth)
        {
            Depth = depth;
            Shape = ShapeOf(depth);
            AssignTargets();

            foreach (var pid in depth.PlayerById.Keys)
            {
                ActualCredits[pid] = 0;
                StintRecords[pid]  = 0;
                CreditsBySeat[pid] = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [PositionalEligibility.Guard] = 0,
                    [PositionalEligibility.Wing]  = 0,
                    [PositionalEligibility.Big]   = 0,
                };
            }
        }

        /// <summary>
        /// The seat composition, as a table key. ★ Exactly three shapes are reachable and
        /// that is provable, not observed: BuildOpeningFive seats under a quota floor of
        /// 2G/1W/1B, which fixes four of the five seats, so the fifth is a guard, a wing
        /// or a big and there is no fourth possibility. A fourth shape therefore means a
        /// seating bug, and this FAILS LOUD rather than defaulting.
        /// </summary>
        private static string ShapeOf(SideDepth depth)
        {
            int g = 0, w = 0, b = 0;
            for (var slot = 1; slot <= Lineup.Size; slot++)
            {
                switch (depth.SlotPos[slot])
                {
                    case PositionalEligibility.Guard: g++; break;
                    case PositionalEligibility.Wing:  w++; break;
                    default:                          b++; break;
                }
            }
            var key = $"{g}G/{w}W/{b}B";
            if (!ShapeTargets.ContainsKey(key))
                throw new InvalidOperationException(
                    $"MINUTES ALLOCATOR: unrecognised opening shape \"{key}\" — the quota floor in " +
                    $"BuildOpeningFive makes only 3G/1W/1B, 2G/2W/1B and 2G/1W/2B reachable, so this " +
                    $"is a seating bug, not a rotation choice.");
            return key;
        }

        /// <summary>
        /// ★ ONE TARGET PER PLAYER, NEVER ONE PER CHART. A player has exactly one total
        /// planned share and his minutes in EVERY seat count toward it. Appearing on
        /// several seat candidate lists is ORDERING ONLY, never additional target — a
        /// side's targets can sum to 200 while individual targets are triple-counted, and
        /// the total still prints correctly, which is precisely why this is stated once
        /// and enforced by construction: the vectors below are walked exactly once each.
        /// </summary>
        private void AssignTargets()
        {
            var (vg, vw, vb) = ShapeTargets[Shape];
            var assigned = 0.0;

            foreach (var (group, vector) in new[]
                     {
                         (PositionalEligibility.Guard, vg),
                         (PositionalEligibility.Wing,  vw),
                         (PositionalEligibility.Big,   vb),
                     })
            {
                var chart = Depth.DepthChartFor(group);
                for (var i = 0; i < chart.Count; i++)
                {
                    // Beyond the vector's length the target is ZERO — the bottom man of
                    // each group, under the ten-man ruling.
                    var minutes = i < vector.Length ? vector[i] : 0.0;
                    TargetMinutes[chart[i]] = minutes;
                    assigned += minutes;
                }
            }

            // On a real 5G/4W/4B roster every group holds MORE players than its vector has
            // entries, so every entry is placed and this scale is exactly 1.0. It exists
            // for the synthetic fixtures, whose group counts are arbitrary and can leave
            // trailing vector entries unplaceable; without it a fixture side's targets
            // would not sum to 200 and the conservation gate would fail on the instrument
            // rather than on the engine.
            var scale = assigned > 0 ? SideMinutes / assigned : 0.0;
            foreach (var pid in TargetMinutes.Keys.ToList())
            {
                TargetMinutes[pid] *= scale;
                TargetShare[pid]    = TargetMinutes[pid] / SideMinutes;
            }
        }

        /// <summary>planned - actual, in credits. POSITIVE MEANS BEHIND PLAN.</summary>
        public double Residual(int pid) =>
            TargetShare[pid] * Lineup.Size * Records - ActualCredits.GetValueOrDefault(pid);

        /// <summary>
        /// |residual| for one player, advanced ONE possession record forward.
        /// ★ The comparison baseline for a move is the NO-MOVE state advanced by the SAME
        /// one record: improvement is |residual if no substitution| - |residual if this
        /// move|, both projected forward. Comparing a move against the current UNADVANCED
        /// state would fold the ordinary passage of time into the apparent effect of
        /// substituting.
        /// </summary>
        public double ProjectedAbs(int pid, bool onFloorNext)
        {
            var planned = TargetShare[pid] * Lineup.Size * (Records + 1);
            var actual  = ActualCredits.GetValueOrDefault(pid) + (onFloorNext ? 1 : 0);
            return Math.Abs(planned - actual);
        }
    }
}
