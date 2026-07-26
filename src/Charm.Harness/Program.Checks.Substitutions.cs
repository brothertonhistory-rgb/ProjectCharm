using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =====================================================================================
    // Phase 52 — Substitutions, Pass 1: the flat fatigue fence.
    //
    // Proves the substitution seam and the fence policy: subs fire only at dead balls, the
    // freshest same-position reserve checks in for a gassed starter, a recovered starter
    // reclaims his slot, attribution lands on the RIGHT player across the sub boundary, the
    // fatigue meter follows who is actually on the floor, and the Governor reports period
    // boundaries correctly — halftime rest reaches the bench, overtime has no rest chunk, and
    // no callback fires after a game-ending untied period.
    //
    // Split into two kinds of sub-check:
    //   • policy-direct — drive OnPossessionBoundary / OnPeriodBreak by hand and inspect the
    //     roster + meter. Deterministic, no resolver, no RNG.
    //   • Governor-integration — run real games: a counting policy proves the firing lifecycle
    //     and the terminal guard; the real fence proves a pre-tired starter is pulled and the
    //     reserve enters and accrues.
    //
    // The pull/return lines are placeholders (75 / 35); these checks assert the SYSTEM, never a
    // tuned value.
    // =====================================================================================
    private static bool Phase52SubstitutionsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 52 — Substitutions (fatigue fence + S75 one-step eligibility ladder) ==");
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

        // Seat five starters (ids baseId..baseId+4) into slots 1..5 and register five reserves
        // (ids baseId+5..baseId+9) with the policy. Returns the SideDepth.
        FlatFatigueFencePolicy.SideDepth BuildSide(GameState g, TeamSide side, int baseId, string[] starterPos, string[] reservePos)
        {
            var starters = new Player[5];
            var reserves = new Player[5];
            for (var i = 0; i < 5; i++) starters[i] = MkSub(baseId + i);
            for (var i = 0; i < 5; i++) reserves[i] = MkSub(baseId + 5 + i);
            var roster = g.RosterFor(side);
            var lineup = g.LineupFor(side);
            for (var i = 0; i < 5; i++) roster.SetStarter(lineup.SlotAt(i + 1), starters[i]);
            return new FlatFatigueFencePolicy.SideDepth(side, starters, starterPos, reserves, reservePos);
        }

        static void Tire(FatigueTracker t, Player p, int n)
        {
            var one = new Player?[] { p };
            for (var i = 0; i < n; i++) t.Accrue(one);
        }

        GameState NewGame() =>
            new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold),
                          ArrowState.Off, new FatigueTracker(cfgFat));

        // A stub resolver — the fence's decisions and the engine's per-possession accrual do
        // not depend on shot outcomes, and NoShot regulation barely calls it. The arg order
        // mirrors the game-lifecycle OT regression, so its fixed seed reproduces a tied
        // regulation → overtime.
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

        var posGGWWB = new[] { "G", "G", "W", "W", "B" };
        // A dummy opponent, all fresh — the policy iterates both sides, and a fresh side
        // triggers nothing (no pull, no reclaim), so it stays inert during policy-direct tests.
        FlatFatigueFencePolicy.SideDepth DummyAway(GameState g) =>
            BuildSide(g, TeamSide.Away, 11, posGGWWB, new[] { "G", "W", "B", "G", "W" });

        // ── Sub-check 1: dead-ball gating + across-sub attribution off-by-one ──────────
        Console.WriteLine("  Sub-check 1: sub only at a dead ball; boundary possession -> outgoing, next -> incoming");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = DummyAway(g);
            var policy = new FlatFatigueFencePolicy(home, away, halftimeEq);

            var s1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;   // slot-1 starter (G, id 1)
            Tire(g.Fatigue, s1, 500);   // push well past the pull-line

            // Live-ball transition boundary: NO sub is legal.
            policy.OnPossessionBoundary(g, nextPossessionNumber: 4, elapsedSeconds: 18.0, isDeadBall: false);
            var afterTransition = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;
            bool noSubOnTransition = afterTransition.PlayerId == s1.PlayerId;

            // Dead-ball boundary at possession 5: the freshest same-position (G) reserve enters.
            policy.OnPossessionBoundary(g, nextPossessionNumber: 5, elapsedSeconds: 18.0, isDeadBall: true);
            var occ = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;
            bool subbed = occ.PlayerId != s1.PlayerId;
            // The two guard reserves are ids 6 and 9; both fresh, tie broken by lowest id -> 6.
            bool correctIncoming = occ.PlayerId == 6;

            // Off-by-one: possession 4 still the outgoing starter; possession 5 the incoming reserve.
            var atBoundary = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1), 4)?.PlayerId;
            var atAfter    = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1), 5)?.PlayerId;
            bool offByOne = atBoundary == s1.PlayerId && atAfter == occ.PlayerId;

            bool ok = noSubOnTransition && subbed && correctIncoming && offByOne;
            pass &= ok;
            Console.WriteLine(ok
                ? "    transition->no sub; dead ball->freshest G reserve in; poss4->starter, poss5->reserve -> ok"
                : $"    FAIL: noSubOnTransition={noSubOnTransition} subbed={subbed} incoming={occ.PlayerId} atP4={atBoundary} atP5={atAfter}");
        }

        // ── Sub-check 2: the meter follows who is on the floor ─────────────────────────
        Console.WriteLine("  Sub-check 2: reserve fresh until entry; benched recovers; on-floor is never policy-recovered");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = DummyAway(g);
            var policy = new FlatFatigueFencePolicy(home, away, halftimeEq);

            var s1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;   // id 1
            bool reserveFreshBeforeEntry = g.Fatigue.LevelFor(6) == 0.0;                  // benched G reserve

            Tire(g.Fatigue, s1, 500);
            policy.OnPossessionBoundary(g, 5, 18.0, true);                               // id 6 enters slot 1
            var occ = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;
            bool entered = occ.PlayerId == 6;

            // Tire the now-on-floor reserve, note the benched starter's level. One more ordinary
            // boundary: the policy recovers the BENCHED (id 1) and must NOT recover the on-floor.
            Tire(g.Fatigue, occ, 40);
            double onFloorBefore  = g.Fatigue.LevelFor(occ.PlayerId);
            double benchedBefore  = g.Fatigue.LevelFor(1);
            policy.OnPossessionBoundary(g, 6, 18.0, true);
            double onFloorAfter   = g.Fatigue.LevelFor(occ.PlayerId);
            double benchedAfter   = g.Fatigue.LevelFor(1);

            bool onFloorUntouched = onFloorAfter == onFloorBefore;   // policy never recovers on-floor
            bool benchedRecovered = benchedAfter < benchedBefore;    // benched decays by the seconds

            bool ok = reserveFreshBeforeEntry && entered && onFloorUntouched && benchedRecovered;
            pass &= ok;
            Console.WriteLine(ok
                ? "    reserve 0 pre-entry; benched recovers; on-floor untouched by policy -> ok"
                : $"    FAIL: reserveFresh={reserveFreshBeforeEntry} entered={entered} onFloor {onFloorBefore:F2}->{onFloorAfter:F2} benched {benchedBefore:F2}->{benchedAfter:F2}");
        }

        // ── Sub-check 3: bench exhausted at a position -> no phantom sub ────────────────
        Console.WriteLine("  Sub-check 3: bench exhausted at a position -> the tired starter stays in (no phantom sub)");
        {
            var g = NewGame();
            // S75 (the ladder): a guard seat draws from guards AND wings, so exhausting it
            // now means NO guard and NO wing on the bench — every reserve must be a big,
            // the one group with no route to a guard seat. Under the old same-position
            // rule the wings here were ineligible; they are legal entrants now, which is
            // the ladder working, not the fallback breaking.
            var home = BuildSide(g, TeamSide.Home, 1, new[] { "G", "W", "W", "B", "B" }, new[] { "B", "B", "B", "B", "B" });
            var away = DummyAway(g);
            var policy = new FlatFatigueFencePolicy(home, away, halftimeEq);

            var s1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;   // id 1, G
            Tire(g.Fatigue, s1, 500);
            policy.OnPossessionBoundary(g, 5, 18.0, true);
            var occ = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;
            bool stayedIn = occ.PlayerId == s1.PlayerId;

            pass &= stayedIn;
            Console.WriteLine(stayedIn
                ? "    no guard or wing on the bench -> tired guard stays in -> ok"
                : $"    FAIL: slot-1 occupant is now id {occ.PlayerId} (expected the tired starter, id {s1.PlayerId})");
        }

        // ── Sub-check 4: a recovered benched starter reclaims his slot ─────────────────
        Console.WriteLine("  Sub-check 4: a recovered benched starter reclaims his slot from a more-tired occupant");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = DummyAway(g);
            var policy = new FlatFatigueFencePolicy(home, away, halftimeEq);

            var roster = g.RosterFor(TeamSide.Home);
            var s1 = roster.PlayerAt(new Slot(TeamSide.Home, 1))!;   // starter id 1 (fresh, level 0)
            // Simulate a prior pull: reserve id 6 (G) is on slot 1 from possession 2; s1 sits.
            roster.Substitute(new Slot(TeamSide.Home, 1), home.PlayerById[6], 2);
            // Tire the on-floor reserve so the fresh benched starter is strictly fresher.
            Tire(g.Fatigue, home.PlayerById[6], 80);

            bool starterBenchedBefore = roster.PlayerAt(new Slot(TeamSide.Home, 1))!.PlayerId == 6;
            // A dead-ball boundary: the fresh starter (0 <= return-line, fresher than the
            // occupant) reclaims slot 1.
            policy.OnPossessionBoundary(g, 6, 18.0, true);
            var occ = roster.PlayerAt(new Slot(TeamSide.Home, 1))!;
            bool reclaimed = occ.PlayerId == s1.PlayerId;

            bool ok = starterBenchedBefore && reclaimed;
            pass &= ok;
            Console.WriteLine(ok
                ? "    fresh benched starter reclaims his slot -> ok"
                : $"    FAIL: benchedBefore={starterBenchedBefore} occupantAfter={occ.PlayerId} (expected starter id {s1.PlayerId})");
        }

        // ── Sub-check 5: halftime rests the bench (slice+chunk); overtime is slice-only ─
        Console.WriteLine("  Sub-check 5: halftime rests the bench (final-poss slice + halftime chunk); overtime is slice-only");
        {
            double finalElapsed = 18.0;

            // Reference: a lone benched player tired identically, given ONLY the final-possession
            // slice. That is what an overtime break should leave, and strictly more fatigue than
            // a halftime break (which adds the halftime chunk on top).
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
            var polH = new FlatFatigueFencePolicy(homeH, DummyAway(gH), halftimeEq);
            Tire(gH.Fatigue, homeH.PlayerById[6], 200);   // reserve id 6 is benched
            polH.OnPeriodBreak(gH, nextPossessionNumber: 40, finalPossessionElapsedSeconds: finalElapsed, kind: PeriodBreakKind.Halftime);
            double halftimeLevel = gH.Fatigue.LevelFor(6);

            var gO = NewGame();
            var homeO = BuildSide(gO, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var polO = new FlatFatigueFencePolicy(homeO, DummyAway(gO), halftimeEq);
            Tire(gO.Fatigue, homeO.PlayerById[6], 200);
            polO.OnPeriodBreak(gO, nextPossessionNumber: 40, finalPossessionElapsedSeconds: finalElapsed, kind: PeriodBreakKind.Overtime);
            double overtimeLevel = gO.Fatigue.LevelFor(6);

            bool overtimeSliceOnly = Math.Abs(overtimeLevel - sliceOnly) < 1e-9;   // OT = slice only
            bool halftimeAddsChunk = halftimeLevel < sliceOnly - 1e-9;             // halftime rests further
            bool ok = overtimeSliceOnly && halftimeAddsChunk;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    OT slice-only ({overtimeLevel:F2}=={sliceOnly:F2}); halftime rests further ({halftimeLevel:F2}<{sliceOnly:F2}) -> ok"
                : $"    FAIL: sliceOnly={sliceOnly:F3} overtime={overtimeLevel:F3} halftime={halftimeLevel:F3}");
        }

        // ── Sub-check 6: Governor reports boundaries; terminal guard holds ─────────────
        Console.WriteLine("  Sub-check 6: halftime callback fires; overtime callback fires once per OT period, none after an untied end");
        {
            // (a) No overtime: NoShot regulation, pre-set 1-0 so regulation ends untied.
            var cfgGovNoOt = new GovernorConfig { PossessionCap = 400, Halves = 2, HalfSeconds = 1.0, OvertimeSeconds = 300.0 };
            var cfgEoHNoShot = new EndOfHalfConfig { HoldThresholdSeconds = 999.0, HoldShootLast = 0.0, ShootEarly = 0.0, NoShot = 1.0, Epsilon = 1e-9 };
            var gN = NewGame();
            SeedMinimalRoster(gN);
            gN.HomeScore = 1;                                   // untied -> no OT
            var countN = new CountingSubPolicy();
            var rngN = new SystemRng(4242);
            var govN = new Governor(BuildStubResolver(gN, new SystemRng(4242)), gN, cfgGovNoOt, RollClockConfig.Load(configPath), rngN, cfgEoHNoShot, countN);
            var resN = govN.Run(TipPossession.CreateFromTip(gN, rngN, possessionNumber: 1));
            bool noOtOk = resN.OvertimePeriods == 0 && countN.Halftime == cfgGovNoOt.Halves - 1 && countN.Overtime == 0;

            // (b) Overtime: NoShot regulation ends 0-0; real OT length scores. One overtime
            // callback per OT period, and none after the untied ending.
            const int OtSeed = 73001;   // lifecycle regression seed: tied regulation -> OT under this stub setup
            var cfgGovOt = new GovernorConfig { PossessionCap = 400, Halves = 2, HalfSeconds = 1.0, OvertimeSeconds = GovernorConfig.Load(configPath).OvertimeSeconds };
            var cfgEoHReal = EndOfHalfConfig.Load(configPath);
            var gO = NewGame();
            SeedMinimalRoster(gO);
            var countO = new CountingSubPolicy();
            var rngO = new SystemRng(OtSeed + 1);
            var govO = new Governor(BuildStubResolver(gO, new SystemRng(OtSeed)), gO, cfgGovOt, RollClockConfig.Load(configPath), rngO, cfgEoHReal, countO);
            var resO = govO.Run(TipPossession.CreateFromTip(gO, rngO, possessionNumber: 1));
            int finalNumber = resO.Possessions.Count > 0 ? resO.Possessions.Max(r => r.Number) : 0;
            bool otEntered   = resO.OvertimePeriods >= 1;
            bool otParity    = countO.Overtime == resO.OvertimePeriods;      // one break per OT, none after untied
            bool halftimeOnce = countO.Halftime == cfgGovOt.Halves - 1;
            bool monotone    = countO.MinNextP >= 2 && countO.MaxNextP <= finalNumber;

            bool ok;
            if (!otEntered)
            {
                Console.WriteLine($"    STOP -- seed {OtSeed} did not produce a tied regulation -> OT (OvertimePeriods={resO.OvertimePeriods}); a replacement seed needs a prompt revision.");
                ok = false;
            }
            else ok = noOtOk && otParity && halftimeOnce && monotone;

            pass &= ok;
            Console.WriteLine(ok
                ? $"    no-OT: halftime x{countN.Halftime}, OT x0; OT run: OT breaks {countO.Overtime}==periods {resO.OvertimePeriods}, nextP in [{countO.MinNextP},{countO.MaxNextP}]<={finalNumber} -> ok"
                : $"    FAIL: noOtOk={noOtOk} otEntered={otEntered} otBreaks={countO.Overtime} periods={resO.OvertimePeriods} halftimeOnce={halftimeOnce} monotone={monotone}");
        }

        // ── Sub-check 7: pre-tired starter is pulled in a live game; reserve enters + accrues ─
        Console.WriteLine("  Sub-check 7: a pre-tired starter is pulled in a live game; a reserve enters and accrues");
        {
            var g = NewGame();
            var home = BuildSide(g, TeamSide.Home, 1, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var away = BuildSide(g, TeamSide.Away, 11, posGGWWB, new[] { "G", "W", "B", "G", "W" });
            var policy = new FlatFatigueFencePolicy(home, away, halftimeEq);

            var s1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;   // id 1
            Tire(g.Fatigue, s1, 500);   // gassed before tip

            var cfgGov = GovernorConfig.Load(configPath);
            var rng = new SystemRng(5150);
            var gov = new Governor(BuildStubResolver(g, new SystemRng(5150)), g, cfgGov, RollClockConfig.Load(configPath), rng, EndOfHalfConfig.Load(configPath), policy);
            var result = gov.Run(TipPossession.CreateFromTip(g, rng, possessionNumber: 1));

            // A substitution happened (a log entry beyond the opening seats, which are possession 1).
            bool subLogged = g.RosterFor(TeamSide.Home).Log.Any(e => e.AtPossession >= 2);
            // A reserve (ids 6..10, never seated) accrued fatigue -> he took the floor after a sub,
            // which is exactly "reserve 0 until entry, then climbs".
            bool anyReserveAccrued = Enumerable.Range(6, 5).Any(id => g.Fatigue.LevelFor(id) > 0.0);

            bool ok = subLogged && anyReserveAccrued;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    starter pulled ({result.Possessions.Count} poss); a reserve entered and accrued -> ok"
                : $"    FAIL: subLogged={subLogged} anyReserveAccrued={anyReserveAccrued}");
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

        // ── Sub-check 9 (S75): a cross-position entrant is legal, and the lineup stays legal ──
        Console.WriteLine("  Sub-check 9: an adjacent-position reserve may enter, and the live five stays legal");
        {
            var g = NewGame();
            // Slot 1 is a Guard seat; the bench holds no guard but does hold wings, which
            // the ladder makes eligible. Under the old rule the tired guard would stay in.
            var home = BuildSide(g, TeamSide.Home, 1, new[] { "G", "W", "W", "B", "B" }, new[] { "W", "W", "W", "B", "B" });
            var away = DummyAway(g);
            var policy = new FlatFatigueFencePolicy(home, away, halftimeEq);

            var s1 = g.RosterFor(TeamSide.Home).PlayerAt(new Slot(TeamSide.Home, 1))!;
            Tire(g.Fatigue, s1, 500);
            policy.OnPossessionBoundary(g, 5, 18.0, true);

            var roster = g.RosterFor(TeamSide.Home);
            var occ = roster.PlayerAt(new Slot(TeamSide.Home, 1))!;
            var entered = occ.PlayerId != s1.PlayerId;
            var entrantEligible = PositionalEligibility.IsEligibleForSeat(
                home.PosById[occ.PlayerId], home.SlotPos[1]);

            // Full live-lineup legality: five unique players, all on this side, one seat
            // each, every occupant eligible for the seat he holds.
            var ids = new List<int>();
            var allEligible = true;
            for (var slot = 1; slot <= Lineup.Size; slot++)
            {
                var p = roster.PlayerAt(new Slot(TeamSide.Home, slot))!;
                ids.Add(p.PlayerId);
                if (!home.PlayerById.ContainsKey(p.PlayerId)) allEligible = false;
                else if (!PositionalEligibility.IsEligibleForSeat(home.PosById[p.PlayerId], home.SlotPos[slot]))
                    allEligible = false;
            }
            var unique = ids.Distinct().Count() == Lineup.Size;

            var ok8 = entered && entrantEligible && unique && allEligible;
            pass &= ok8;
            Console.WriteLine(ok8
                ? $"    wing id {occ.PlayerId} entered the guard seat; five unique, all eligible -> ok"
                : $"    FAIL: entered={entered} entrantEligible={entrantEligible} unique={unique} allEligible={allEligible}");
        }

        Console.WriteLine(pass ? "  Phase 52 substitutions: ok" : "  Phase 52 substitutions: FAIL");
        return pass;
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
