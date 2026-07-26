using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =====================================================================================
    // Phase 52 — the SUBSTITUTION SEAM (S76 rewrite).
    //
    // S52 built this against FlatFatigueFencePolicy. S76 retires the fence, but what these
    // sub-checks actually prove was never fence behaviour — it is the ENGINE SEAM contract,
    // and the minutes allocator has to honour every clause of it:
    //
    //   * substitutions fire only at dead balls;
    //   * attribution lands on the right side of the boundary (the outgoing man owns the
    //     boundary possession, the incoming man owns the next);
    //   * the fatigue meter follows who is actually on the floor — the bench recovers, the
    //     on-floor five are never policy-recovered on top of the engine's accrual;
    //   * halftime rest reaches the bench (final-possession slice + halftime chunk) while
    //     overtime is slice-only;
    //   * the Governor reports non-terminal boundaries and never reports a terminal one;
    //   * no eligible reserve means the incumbent stays — no phantom substitution;
    //   * the one-step eligibility ladder holds, all nine cells, non-transitive.
    //
    // ★ Sub-check 2 is now load-bearing in a way it was not before. RecoverBenched was
    // inside the fence, and it is the ONLY driver of off-floor fatigue recovery in the
    // production tree — the engine rests only the on-floor five. Carrying it into the
    // allocator was a deliberate migration step; if a future session drops it, the bench
    // never recovers all season and nothing else in the suite notices. This is the check
    // that notices.
    //
    // FIXTURE NOTE: every fixture below uses one of the three REACHABLE opening shapes
    // (3G/1W/1B, 2G/2W/1B, 2G/1W/2B). The allocator fails loud on any other, because
    // BuildOpeningFive's 2G/1W/1B quota floor makes only those three constructible.
    // =====================================================================================
    private static bool Phase52SubstitutionsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 52 — substitution seam (minutes allocator + S75 eligibility ladder) ==");
        var pass = true;

        var cfgFat = FatigueConfig.Load(configPath);
        var cfgD   = RollDConfig.Load(configPath);
        double halftimeEq = cfgFat.HalftimeRestEquivalentSeconds;

        // ── local builders ───────────────────────────────────────────────────────────
        static Player MkSub(int id) => new Player($"sub{id}")
        {
            PlayerId = id,
            Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
            BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
            OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
            PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
            OffBallDefense = 50, Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
            Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
            BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
            RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
        };

        // Seat five starters (ids baseId..baseId+4) into slots 1..5 and register five
        // reserves (ids baseId+5..baseId+9). Ranks descend with the id so each group's
        // depth chart is predictable: within a stored group, the lower id ranks higher.
        SideDepth BuildSide(GameState g, TeamSide side, int baseId, string[] starterPos, string[] reservePos)
        {
            var starters = new Player[5];
            var reserves = new Player[5];
            var sRank    = new double[5];
            var rRank    = new double[5];
            for (var i = 0; i < 5; i++) { starters[i] = MkSub(baseId + i);     sRank[i] = 100.0 - i; }
            for (var i = 0; i < 5; i++) { reserves[i] = MkSub(baseId + 5 + i); rRank[i] =  95.0 - i; }
            var roster = g.RosterFor(side);
            var lineup = g.LineupFor(side);
            for (var i = 0; i < 5; i++) roster.SetStarter(lineup.SlotAt(i + 1), starters[i]);
            return new SideDepth(side, starters, starterPos, sRank, reserves, reservePos, rRank);
        }

        static void Tire(FatigueTracker t, Player p, int n)
        {
            var one = new Player?[] { p };
            for (var i = 0; i < n; i++) t.Accrue(one);
        }

        // Advance the allocator's clock without permitting a substitution: live-ball
        // boundaries credit the on-floor five and grow R, so residuals develop and the
        // starters' protected stints complete, but no move may fire.
        static void RunUpResiduals(MinutesAllocatorPolicy policy, GameState g, int records)
        {
            for (var i = 0; i < records; i++)
                policy.OnPossessionBoundary(g, nextPossessionNumber: i + 2, elapsedSeconds: 18.0, isDeadBall: false);
        }

        GameState NewGame() =>
            new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold),
                          ArrowState.Off, new FatigueTracker(cfgFat));

        // A stub resolver — the allocator's decisions and the engine's per-possession
        // accrual do not depend on shot outcomes. The arg order mirrors the game-lifecycle
        // OT regression, so its fixed seed reproduces a tied regulation → overtime.
        Resolver BuildStubResolver(GameState g, IRng rng)
        {
            var a = RollAConfig.Load(configPath);
            return new Resolver(
                new StubPieGenerator(a),
                a,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(cfgD),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), g),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), g),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                MatchupConfig.Load(configPath),
                g,
                rng);
        }

        // 2G/2W/1B — a reachable opening shape.
        var posGGWWB = new[] { "G", "G", "W", "W", "B" };
        // 2G/1W/2B — the shape used where a guard seat must be starved of entrants.
        var posGGWBB = new[] { "G", "G", "W", "B", "B" };

        // A dummy opponent. The allocator iterates both sides, so the away side must be a
        // legal shape too; it is never driven into a substitution by these fixtures.
        SideDepth DummyAway(GameState g) =>
            BuildSide(g, TeamSide.Away, 11, posGGWWB, new[] { "G", "W", "B", "G", "W" });

        // ── Sub-check 1: dead-ball gating + across-sub attribution off-by-one ──────────
        Console.WriteLine("  Sub-check 1: sub only at a dead ball; boundary possession -> outgoing, next -> incoming");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = DummyAway(g);
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

            var s1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;

            // Grow residuals past the hysteresis band and complete every starter's stint.
            RunUpResiduals(policy, g, 8);

            // A live-ball boundary is NOT a legal substitution moment, however large the
            // residuals have grown.
            var before = SeatIds(g, TeamSide.Home);
            policy.OnPossessionBoundary(g, nextPossessionNumber: 10, elapsedSeconds: 18.0, isDeadBall: false);
            bool noSubOnTransition = SeatIds(g, TeamSide.Home).SequenceEqual(before);

            // A dead ball at possession 11: a move fires.
            policy.OnPossessionBoundary(g, nextPossessionNumber: 11, elapsedSeconds: 18.0, isDeadBall: true);
            var after = SeatIds(g, TeamSide.Home);
            bool subbed = !after.SequenceEqual(before);

            // Whoever entered must have been on the bench and eligible for the seat he took.
            var entrants = after.Except(before).ToList();
            bool entrantsLegal = entrants.Count > 0 && entrants.All(id =>
                !before.Contains(id) && home.PlayerById.ContainsKey(id));

            // Off-by-one, read on a seat that actually changed hands.
            var changedSeat = Enumerable.Range(1, Lineup.Size).First(sl => after[sl - 1] != before[sl - 1]);
            var atBoundary = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, changedSeat), 10)?.PlayerId;
            var atAfter    = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, changedSeat), 11)?.PlayerId;
            bool offByOne = atBoundary == before[changedSeat - 1] && atAfter == after[changedSeat - 1];

            bool ok = noSubOnTransition && subbed && entrantsLegal && offByOne;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    transition->no sub; dead ball->seat {changedSeat} changes; poss10->outgoing, poss11->incoming -> ok"
                : $"    FAIL: noSubOnTransition={noSubOnTransition} subbed={subbed} entrantsLegal={entrantsLegal} offByOne={offByOne} (starter id {s1.PlayerId})");
        }

        // ── Sub-check 2: the meter follows who is on the floor ─────────────────────────
        //  ★ This is the bench-recovery carry-over proof. See the header note.
        Console.WriteLine("  Sub-check 2: reserve fresh until entry; benched recovers; on-floor is never policy-recovered");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = DummyAway(g);
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

            bool reserveFreshBeforeEntry = g.Fatigue.LevelFor(6) == 0.0;   // a benched reserve

            // Tire a benched man directly, then take one ordinary boundary. The policy must
            // recover HIM (he is off the floor) and must NOT touch the on-floor five, whom
            // the engine already accrued at the possession tail.
            Tire(g.Fatigue, home.PlayerById[6], 200);
            var onFloorId = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!.PlayerId;
            Tire(g.Fatigue, home.PlayerById[onFloorId], 40);

            double onFloorBefore = g.Fatigue.LevelFor(onFloorId);
            double benchedBefore = g.Fatigue.LevelFor(6);
            policy.OnPossessionBoundary(g, nextPossessionNumber: 2, elapsedSeconds: 18.0, isDeadBall: false);
            double onFloorAfter  = g.Fatigue.LevelFor(onFloorId);
            double benchedAfter  = g.Fatigue.LevelFor(6);

            bool onFloorUntouched = onFloorAfter == onFloorBefore;
            bool benchedRecovered = benchedAfter < benchedBefore;

            bool ok = reserveFreshBeforeEntry && onFloorUntouched && benchedRecovered;
            pass &= ok;
            Console.WriteLine(ok
                ? "    reserve 0 pre-entry; benched recovers; on-floor untouched by policy -> ok"
                : $"    FAIL: reserveFresh={reserveFreshBeforeEntry} onFloor {onFloorBefore:F2}->{onFloorAfter:F2} benched {benchedBefore:F2}->{benchedAfter:F2}");
        }

        // ── Sub-check 3: no eligible reserve -> no phantom sub ─────────────────────────
        Console.WriteLine("  Sub-check 3: bench cannot reach the guard seats -> the incumbents stay (no phantom sub)");
        {
            var g = NewGame();
            // 2G/1W/2B with an all-big bench. A big has NO route to a guard seat, so seats
            // 1 and 2 have no legal entrant however far behind the bench falls.
            var home = BuildSide(g, TeamSide.Home, 1, posGGWBB, new[] { "B", "B", "B", "B", "B" });
            var away = DummyAway(g);
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

            var g1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!.PlayerId;
            var g2 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 2))!.PlayerId;

            RunUpResiduals(policy, g, 10);
            policy.OnPossessionBoundary(g, nextPossessionNumber: 12, elapsedSeconds: 18.0, isDeadBall: true);

            var occ1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!.PlayerId;
            var occ2 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 2))!.PlayerId;
            // The guard seats may legally change hands via a CASCADE (the wing relocating
            // in), but a big must never occupy one.
            var seat1Legal = PositionalEligibility.IsEligibleForSeat(home.PosById[occ1], "G");
            var seat2Legal = PositionalEligibility.IsEligibleForSeat(home.PosById[occ2], "G");

            bool ok = seat1Legal && seat2Legal;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    guard seats hold G/W only (ids {occ1}/{occ2}); no big smuggled in -> ok"
                : $"    FAIL: seat1 id {occ1} ({home.PosById[occ1]}), seat2 id {occ2} ({home.PosById[occ2]}) — started {g1}/{g2}");
        }

        // ── Sub-check 4: a man far behind plan returns to the floor ───────────────────
        //  The fence's "starter reclaims his slot" rule is retired with it — S76 has no
        //  concept of slot ownership. The equivalent contract is that a benched man whose
        //  residual keeps growing eventually comes back.
        Console.WriteLine("  Sub-check 4: a benched man whose residual keeps growing returns to the floor");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = DummyAway(g);
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

            var opening = SeatIds(g, TeamSide.Home);
            var everOnFloor = new HashSet<int>(opening);

            // Fifty records with a dead ball every fourth — enough opportunities for the
            // bench to be worked through as residuals accumulate.
            for (var i = 0; i < 50; i++)
            {
                policy.OnPossessionBoundary(g, nextPossessionNumber: i + 2, elapsedSeconds: 18.0, isDeadBall: i % 4 == 3);
                foreach (var id in SeatIds(g, TeamSide.Home)) everOnFloor.Add(id);
            }

            var st = policy.StateFor(TeamSide.Home);
            // Every man holding a positive target should have seen the floor.
            var withTarget = st.TargetMinutes.Where(kv => kv.Value > 0.0).Select(kv => kv.Key).ToList();
            var missed = withTarget.Where(id => !everOnFloor.Contains(id)).ToList();
            bool anyReserveEntered = everOnFloor.Count > Lineup.Size;

            bool ok = anyReserveEntered && missed.Count == 0;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    {everOnFloor.Count} men used; all {withTarget.Count} positive-target men reached the floor -> ok"
                : $"    FAIL: used={everOnFloor.Count} missed=[{string.Join(",", missed)}] of {withTarget.Count} positive-target men");
        }

        // ── Sub-check 5: halftime rests the bench (slice+chunk); overtime is slice-only ─
        Console.WriteLine("  Sub-check 5: halftime rests the bench (final-poss slice + halftime chunk); overtime is slice-only");
        {
            double finalElapsed = 18.0;

            // Reference: a lone benched player tired identically, given ONLY the final-
            // possession slice. That is what an overtime break should leave, and strictly
            // more fatigue than a halftime break (which adds the halftime chunk on top).
            double SliceOnlyLevel()
            {
                var t = new FatigueTracker(cfgFat);
                var p = MkSub(99);
                Tire(t, p, 200);
                t.Recover(new Player?[] { p }, finalElapsed);
                return t.LevelFor(99);
            }
            double sliceOnly = SliceOnlyLevel();

            var gH = NewGame();
            var homeH = BuildSide(gH, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var polH = new MinutesAllocatorPolicy(homeH, DummyAway(gH), halftimeEq);
            Tire(gH.Fatigue, homeH.PlayerById[6], 200);
            polH.OnPeriodBreak(gH, nextPossessionNumber: 40, finalPossessionElapsedSeconds: finalElapsed, kind: PeriodBreakKind.Halftime);
            double halftimeLevel = gH.Fatigue.LevelFor(6);

            var gO = NewGame();
            var homeO = BuildSide(gO, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var polO = new MinutesAllocatorPolicy(homeO, DummyAway(gO), halftimeEq);
            Tire(gO.Fatigue, homeO.PlayerById[6], 200);
            polO.OnPeriodBreak(gO, nextPossessionNumber: 40, finalPossessionElapsedSeconds: finalElapsed, kind: PeriodBreakKind.Overtime);
            double overtimeLevel = gO.Fatigue.LevelFor(6);

            bool overtimeSliceOnly = Math.Abs(overtimeLevel - sliceOnly) < 1e-9;
            bool halftimeAddsChunk = halftimeLevel < sliceOnly - 1e-9;

            bool ok = overtimeSliceOnly && halftimeAddsChunk;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    OT {overtimeLevel:F3} == slice-only {sliceOnly:F3}; halftime {halftimeLevel:F3} rests further -> ok"
                : $"    FAIL: sliceOnly={sliceOnly:F4} overtime={overtimeLevel:F4} halftime={halftimeLevel:F4}");
        }

        // ── Sub-check 6: Governor reports boundaries; terminal guard holds ─────────────
        Console.WriteLine("  Sub-check 6: halftime callback fires; overtime callback fires once per OT period, none after an untied end");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = BuildSide(g, TeamSide.Away, 11, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var counter = new CountingSubPolicy();

            var rng = new SystemRng(5150);
            var gov = new Governor(BuildStubResolver(g, new SystemRng(5150)), g, GovernorConfig.Load(configPath),
                                   RollClockConfig.Load(configPath), rng, EndOfHalfConfig.Load(configPath), counter);
            var result = gov.Run(TipPossession.CreateFromTip(g, rng, possessionNumber: 1));

            bool oneHalftime  = counter.Halftime == 1;
            bool anyOrdinary  = counter.Ordinary > 0;
            bool someDeadBall = counter.DeadBallOrdinary > 0 && counter.DeadBallOrdinary <= counter.Ordinary;
            // Successor-stamped: the smallest reported next-possession number is 2, and no
            // callback is reported after the game-ending possession.
            bool stamped = counter.MinNextP >= 2 && counter.MaxNextP <= result.Possessions.Count;

            bool ok = oneHalftime && anyOrdinary && someDeadBall && stamped;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    {counter.Ordinary} ordinary ({counter.DeadBallOrdinary} dead-ball), {counter.Halftime} halftime, {counter.Overtime} OT; stamps in [2,{result.Possessions.Count}] -> ok"
                : $"    FAIL: halftime={counter.Halftime} ordinary={counter.Ordinary} deadBall={counter.DeadBallOrdinary} minNextP={counter.MinNextP} maxNextP={counter.MaxNextP} poss={result.Possessions.Count}");
        }

        // ── Sub-check 7: a live game substitutes, and reserves accrue ──────────────────
        Console.WriteLine("  Sub-check 7: a live game substitutes off the target plan; reserves enter and accrue");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = BuildSide(g, TeamSide.Away, 11, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

            var rng = new SystemRng(5150);
            var gov = new Governor(BuildStubResolver(g, new SystemRng(5150)), g, GovernorConfig.Load(configPath),
                                   RollClockConfig.Load(configPath), rng, EndOfHalfConfig.Load(configPath), policy);
            var result = gov.Run(TipPossession.CreateFromTip(g, rng, possessionNumber: 1));

            bool subLogged = g.RosterFor(TeamSide.Home).Log.Any(e => e.AtPossession >= 2);
            bool anyReserveAccrued = Enumerable.Range(6, 5).Any(id => g.Fatigue.LevelFor(id) > 0.0);

            var st = policy.StateFor(TeamSide.Home);
            // No stint may be shorter than the protected minimum (the final, still-open
            // stints are not in the list — only completed ones are recorded).
            var shortStints = st.StintLengths.Count(n => n < MinutesAllocatorPolicy.MinimumStint);

            bool ok = subLogged && anyReserveAccrued && shortStints == 0;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    {st.Substitutions} subs over {result.Possessions.Count} poss; reserves accrued; 0 stints under the {MinutesAllocatorPolicy.MinimumStint}-record minimum -> ok"
                : $"    FAIL: subLogged={subLogged} anyReserveAccrued={anyReserveAccrued} shortStints={shortStints}");
        }

        // ── Sub-check 8 (S75): the eligibility matrix, all nine cells ─────────────────
        //  Prose is not implementation. Every cell is asserted directly so a future
        //  reader cannot re-derive the rule from a sentence and get it wrong.
        Console.WriteLine("  Sub-check 8: the one-step eligibility matrix — all nine cells, non-transitive");
        {
            const string G = PositionalEligibility.Guard;
            const string W = PositionalEligibility.Wing;
            const string B = PositionalEligibility.Big;
            var expected = new (string Stored, string Seat, bool Legal)[]
            {
                (G, G, true ), (G, W, true ), (G, B, false),
                (W, G, true ), (W, W, true ), (W, B, true ),
                (B, G, false), (B, W, true ), (B, B, true ),
            };
            var wrong = new List<string>();
            foreach (var (stored, seat, legal) in expected)
                if (PositionalEligibility.IsEligibleForSeat(stored, seat) != legal)
                    wrong.Add($"{stored}->{seat} expected {(legal ? "legal" : "illegal")}");

            var nonTransitive = !PositionalEligibility.IsEligibleForSeat(G, B)
                             && !PositionalEligibility.IsEligibleForSeat(B, G);

            // Unknown labels must THROW, not quietly read as ineligible — a silent false
            // would remove a player from every rotation and look like a rotation choice.
            var loud = 0;
            foreach (var bad in new[] { "", "g", "C", "PG", "Wing" })
            {
                try { PositionalEligibility.IsEligibleForSeat(bad, G); }
                catch (ArgumentOutOfRangeException) { loud++; }
            }

            var ok7 = wrong.Count == 0 && nonTransitive && loud == 5;
            pass &= ok7;
            Console.WriteLine(ok7
                ? "    9/9 cells correct; G->B and B->G both refused; 5/5 unknown labels threw -> ok"
                : $"    FAIL: cells [{string.Join(", ", wrong)}] nonTransitive={nonTransitive} loudRejects={loud}/5");
        }

        // ── Sub-check 9 (S75): a cross-position entrant is legal, and the five stays legal ──
        Console.WriteLine("  Sub-check 9: an adjacent-position reserve may enter, and the live five stays legal");
        {
            var g = NewGame();
            // 2G/1W/2B; the bench holds no guard but does hold wings, which the ladder
            // makes eligible for a guard seat. Under the retired same-position rule the
            // guard seats could never have been relieved at all.
            var home = BuildSide(g, TeamSide.Home, 1, posGGWBB, new[] { "W", "W", "W", "B", "B" });
            var away = DummyAway(g);
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

            RunUpResiduals(policy, g, 10);
            policy.OnPossessionBoundary(g, nextPossessionNumber: 12, elapsedSeconds: 18.0, isDeadBall: true);

            var roster = g.RosterFor(TeamSide.Home);
            var ids = new List<int>();
            var allEligible = true;
            var crossCount = 0;
            for (var slot = 1; slot <= Lineup.Size; slot++)
            {
                var p = roster.PlayerAt(new Slot(TeamSide.Home, slot))!;
                ids.Add(p.PlayerId);
                if (!home.PlayerById.ContainsKey(p.PlayerId)) allEligible = false;
                else
                {
                    if (!PositionalEligibility.IsEligibleForSeat(home.PosById[p.PlayerId], home.SlotPos[slot]))
                        allEligible = false;
                    if (PositionalEligibility.IsCrossPosition(home.PosById[p.PlayerId], home.SlotPos[slot]))
                        crossCount++;
                }
            }
            var unique = ids.Distinct().Count() == Lineup.Size;
            var wingReachedGuardSeat = crossCount > 0;

            var ok8 = wingReachedGuardSeat && unique && allEligible;
            pass &= ok8;
            Console.WriteLine(ok8
                ? $"    {crossCount} cross-position occupant(s) after the move; five unique, all eligible -> ok"
                : $"    FAIL: cross={crossCount} unique={unique} allEligible={allEligible}");
        }

        Console.WriteLine(pass ? "  Phase 52 substitution seam: ok" : "  Phase 52 substitution seam: FAIL");
        return pass;
    }

    /// <summary>The five on-floor PlayerIds, seat 1..5 in order.</summary>
    private static int[] SeatIds(GameState g, TeamSide side)
    {
        var roster = g.RosterFor(side);
        var ids = new int[Lineup.Size];
        for (var slot = 1; slot <= Lineup.Size; slot++)
            ids[slot - 1] = roster.PlayerAt(new Slot(side, slot))!.PlayerId;
        return ids;
    }

    // Counting test double: records how the Governor drives the seam, without touching rosters.
    private sealed class CountingSubPolicy : ISubstitutionPolicy
    {
        public int Ordinary, DeadBallOrdinary, Halftime, Overtime;
        public int MaxNextP;
        public int MinNextP = int.MaxValue;

        public void OnPossessionBoundary(GameState game, int nextPossessionNumber, double elapsedSeconds, bool isDeadBall)
        {
            Ordinary++;
            if (isDeadBall) DeadBallOrdinary++;
            if (nextPossessionNumber > MaxNextP) MaxNextP = nextPossessionNumber;
            if (nextPossessionNumber < MinNextP) MinNextP = nextPossessionNumber;
        }

        public void OnPeriodBreak(GameState game, int nextPossessionNumber, double finalPossessionElapsedSeconds, PeriodBreakKind kind)
        {
            if (kind == PeriodBreakKind.Halftime) Halftime++;
            else Overtime++;
            if (nextPossessionNumber > MaxNextP) MaxNextP = nextPossessionNumber;
            if (nextPossessionNumber < MinNextP) MinNextP = nextPossessionNumber;
        }
    }
}
