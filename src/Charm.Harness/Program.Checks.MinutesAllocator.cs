using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =====================================================================================
    // Phase 72 — THE MINUTES ALLOCATOR (S76).
    //
    // Max flow proves a static allocation EXISTS. It does not prove the bounded move set
    // can REACH it from a rank-blind opening five under one-move-per-dead-ball and a
    // protected minimum stint. So this phase runs all three reachable opening shapes and
    // checks mechanics AND convergence.
    //
    // ★ The convergence runs use the PRODUCTION dead-ball cadence — real Governor games,
    // where the allocator gets a substitution opportunity exactly where a real game offers
    // one. Calling the policy on every synthetic possession would prove reachability under
    // artificially generous substitution opportunities the season never provides.
    // =====================================================================================
    private static bool Phase72MinutesAllocatorCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 72 — minutes allocator (targets, cascades, stability, convergence) ==");
        var pass = true;

        var cfgFat = FatigueConfig.Load(configPath);
        var cfgD   = RollDConfig.Load(configPath);
        double halftimeEq = cfgFat.HalftimeRestEquivalentSeconds;

        // Shape -> the seat positions of slots 1..5, in seat order.
        var shapeSeats = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["3G/1W/1B"] = new[] { "G", "G", "G", "W", "B" },
            ["2G/2W/1B"] = new[] { "G", "G", "W", "W", "B" },
            ["2G/1W/2B"] = new[] { "G", "G", "W", "B", "B" },
        };

        static Player MkP(int id) => new Player($"alloc{id}")
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

        // A real 13-man 5G/4W/4B side seated into the given shape.
        //
        // ★ The opening five is deliberately built RANK-BLIND — it takes the first man of
        // each seat's own group in acquisition order, which is exactly what BuildOpeningFive
        // does. That collides with targets on purpose (A7): a starter may sit below a
        // reserve on his own chart. The tipoff five stands; correction happens only through
        // the ordinary stint and hysteresis rules, never a special early-game path.
        SideDepth Build13(GameState g, TeamSide side, int baseId, string[] seats)
        {
            // Stored groups, in acquisition order: 5G, 4W, 4B.
            var groups = new List<string>();
            for (var i = 0; i < RosterShape.Guards; i++) groups.Add("G");
            for (var i = 0; i < RosterShape.Wings;  i++) groups.Add("W");
            for (var i = 0; i < RosterShape.Bigs;   i++) groups.Add("B");

            var players = new List<Player>();
            var ranks   = new List<double>();
            for (var i = 0; i < RosterShape.Size; i++)
            {
                players.Add(MkP(baseId + i));
                // Ranks deliberately NOT aligned with acquisition order: within each group
                // the LAST acquired man ranks highest. This guarantees the tipoff five and
                // the depth chart disagree, so the A7 collision is exercised every run.
                ranks.Add(10.0 + i);
            }

            var used = new bool[RosterShape.Size];
            var starters = new List<Player>();  var starterPos = new List<string>();  var starterRank = new List<double>();
            foreach (var seatType in seats)
            {
                var idx = -1;
                for (var i = 0; i < RosterShape.Size; i++)
                    if (!used[i] && groups[i] == seatType) { idx = i; break; }
                if (idx < 0) throw new InvalidOperationException($"fixture bug — no free {seatType} for the {string.Join("", seats)} shape.");
                used[idx] = true;
                starters.Add(players[idx]); starterPos.Add(groups[idx]); starterRank.Add(ranks[idx]);
            }

            var reserves = new List<Player>();  var reservePos = new List<string>();  var reserveRank = new List<double>();
            for (var i = 0; i < RosterShape.Size; i++)
                if (!used[i]) { reserves.Add(players[i]); reservePos.Add(groups[i]); reserveRank.Add(ranks[i]); }

            var roster = g.RosterFor(side);
            var lineup = g.LineupFor(side);
            for (var i = 0; i < Lineup.Size; i++) roster.SetStarter(lineup.SlotAt(i + 1), starters[i]);

            return new SideDepth(side, starters, starterPos, starterRank, reserves, reservePos, reserveRank);
        }

        GameState NewGame() =>
            new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold),
                          ArrowState.Off, new FatigueTracker(cfgFat));

        Resolver BuildStubResolver(GameState g, IRng rng)
        {
            var a = RollAConfig.Load(configPath);
            return new Resolver(
                new StubPieGenerator(a), a,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
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
                MatchupConfig.Load(configPath), g, rng);
        }

        // ── Gate 3: flow feasibility, before any game is simulated ───────────────────
        //
        //  ★ A per-seat capacity check is INSUFFICIENT and would pass while the plan is
        //  impossible: one wing's 30 minutes appear available to the G, W and B seats when
        //  each is checked alone. Feasibility is a SIMULTANEOUS problem, so it is solved as
        //  max flow — source -> group (capacity = the group's summed targets), group -> each
        //  permitted seat type, seat type -> sink (capacity = 40 x seat count).
        Console.WriteLine("  Sub-check 1: flow feasibility — every shape's target table places all 200 minutes");
        {
            var lines = new List<string>();
            var allOk = true;
            foreach (var (shape, seats) in shapeSeats)
            {
                var g = NewGame();
                var depth = Build13(g, TeamSide.Home, 1, seats);
                var st = new MinutesAllocatorPolicy(depth, Build13(NewGame(), TeamSide.Away, 14, seats), halftimeEq)
                            .StateFor(TeamSide.Home);

                var groupTarget = new Dictionary<string, double>(StringComparer.Ordinal) { ["G"] = 0, ["W"] = 0, ["B"] = 0 };
                foreach (var kv in st.TargetMinutes) groupTarget[depth.PosById[kv.Key]] += kv.Value;

                var seatCount = new Dictionary<string, int>(StringComparer.Ordinal) { ["G"] = 0, ["W"] = 0, ["B"] = 0 };
                for (var slot = 1; slot <= Lineup.Size; slot++) seatCount[depth.SlotPos[slot]]++;

                var flow = MaxFlowMinutes(groupTarget, seatCount);
                var ok = Math.Abs(flow - MinutesAllocatorPolicy.SideMinutes) < 1e-9;
                allOk &= ok;
                lines.Add($"    {shape}: G {groupTarget["G"]:F0} / W {groupTarget["W"]:F0} / B {groupTarget["B"]:F0} " +
                          $"vs seats {seatCount["G"] * 40}/{seatCount["W"] * 40}/{seatCount["B"] * 40} -> max flow {flow:F0} {(ok ? "ok" : "FAIL")}");
            }
            foreach (var l in lines) Console.WriteLine(l);
            pass &= allOk;
        }

        // ── Gate 2 + Gate 5(a): target conservation and EXACT target-order fidelity ──
        Console.WriteLine("  Sub-check 2: one target per player; side sums to 200; targets follow stored-group rank exactly");
        {
            var allOk = true;
            foreach (var (shape, seats) in shapeSeats)
            {
                var g = NewGame();
                var depth = Build13(g, TeamSide.Home, 1, seats);
                var st = new MinutesAllocatorPolicy(depth, Build13(NewGame(), TeamSide.Away, 14, seats), halftimeEq)
                            .StateFor(TeamSide.Home);

                var sum = st.TargetMinutes.Values.Sum();
                var oneEach = st.TargetMinutes.Count == RosterShape.Size;
                var nonNegative = st.TargetMinutes.Values.All(v => v >= -1e-12);
                var zeros = st.TargetMinutes.Values.Count(v => v < 1e-12);

                // ★ The anti-id-fallback proof. Within each stored group, targets must be
                // NON-INCREASING down the depth chart. A silent fallback to id order would
                // produce a perfectly plausible ordered chart, so this inspects the assigned
                // TARGETS against the RANK ORDER, not the outcomes.
                var ordered = true;
                foreach (var grp in new[] { "G", "W", "B" })
                {
                    var chart = depth.DepthChartFor(grp);
                    for (var i = 1; i < chart.Count; i++)
                        if (st.TargetMinutes[chart[i]] > st.TargetMinutes[chart[i - 1]] + 1e-12) ordered = false;
                    // and the chart itself must be rank-descending
                    for (var i = 1; i < chart.Count; i++)
                        if (depth.RankById[chart[i]] > depth.RankById[chart[i - 1]] + 1e-12) ordered = false;
                }

                var ok = Math.Abs(sum - 200.0) < 1e-9 && oneEach && nonNegative && zeros == 3 && ordered;
                allOk &= ok;
                Console.WriteLine($"    {shape}: sum {sum:F1}, {st.TargetMinutes.Count} targets, {zeros} zero-target men, rank-ordered {ordered} {(ok ? "-> ok" : "-> FAIL")}");
            }
            pass &= allOk;
        }

        // ── Gates 4/6/9 + cascades: a real game per shape at production cadence ──────
        Console.WriteLine("  Sub-check 3: real games per shape — legality, seat-split conservation, stints, cascades");
        {
            var allOk = true;
            foreach (var (shape, seats) in shapeSeats)
            {
                var g = NewGame();
                var home = Build13(g, TeamSide.Home, 1, seats);
                var away = Build13(g, TeamSide.Away, 1 + RosterShape.AwayIdOffset, seats);
                var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

                var rng = new SystemRng(760001);
                var gov = new Governor(BuildStubResolver(g, new SystemRng(760001)), g, GovernorConfig.Load(configPath),
                                       RollClockConfig.Load(configPath), rng, EndOfHalfConfig.Load(configPath), policy);
                var result = gov.Run(TipPossession.CreateFromTip(g, rng, possessionNumber: 1));
                var st = policy.StateFor(TeamSide.Home);

                // Gate 9 — the live five is legal at the end of the game.
                var legal = LineupIsLegal(g, home);

                // Gate 4 — seat-split conservation, per player and per seat type.
                var perPlayerOk = st.CreditsBySeat.All(kv =>
                    kv.Value.Values.Sum() == st.ActualCredits.GetValueOrDefault(kv.Key));
                var seatTotals = new Dictionary<string, int>(StringComparer.Ordinal) { ["G"] = 0, ["W"] = 0, ["B"] = 0 };
                foreach (var kv in st.CreditsBySeat)
                    foreach (var sv in kv.Value) seatTotals[sv.Key] += sv.Value;
                var seatCount = new Dictionary<string, int>(StringComparer.Ordinal) { ["G"] = 0, ["W"] = 0, ["B"] = 0 };
                for (var slot = 1; slot <= Lineup.Size; slot++) seatCount[home.SlotPos[slot]]++;
                var perSeatOk = seatTotals.All(kv => kv.Value == seatCount[kv.Key] * st.Records);

                // Total credits identity: five men on the floor for every record.
                var creditIdentity = st.ActualCredits.Values.Sum() == Lineup.Size * st.Records;

                // Gate 6 — no completed stint shorter than the protected minimum.
                var shortStints = st.StintLengths.Count(n => n < MinutesAllocatorPolicy.MinimumStint);

                var ok = legal && perPlayerOk && perSeatOk && creditIdentity && shortStints == 0;
                allOk &= ok;
                Console.WriteLine(
                    $"    {shape}: {result.Possessions.Count} poss, R={st.Records}, {st.Substitutions} subs " +
                    $"({st.Straights} straight / {st.Cascades} cascade), cross-pos {(st.TotalSeatCredits > 0 ? st.CrossPositionCredits * 100.0 / st.TotalSeatCredits : 0.0):F1}%, " +
                    $"short stints {shortStints} {(ok ? "-> ok" : "-> FAIL")}");
                if (!ok)
                    Console.WriteLine($"      FAIL detail: legal={legal} perPlayer={perPlayerOk} perSeat={perSeatOk} creditIdentity={creditIdentity}");
            }
            pass &= allOk;
        }

        // ── Cascades fire, and all three legs are logged ─────────────────────────────
        Console.WriteLine("  Sub-check 4: a cascade fires; all three players are logged, legal and correctly stinted");
        {
            // 2G/1W/2B with a wing-heavy bench is the shape that leans hardest on the
            // ladder (14% minimum cross-position flow), so it is where a coordinated move
            // is most likely to beat a straight substitution.
            var g = NewGame();
            var seats = shapeSeats["2G/1W/2B"];
            var home = Build13(g, TeamSide.Home, 1, seats);
            var away = Build13(g, TeamSide.Away, 1 + RosterShape.AwayIdOffset, seats);
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

            var rng = new SystemRng(760002);
            var gov = new Governor(BuildStubResolver(g, new SystemRng(760002)), g, GovernorConfig.Load(configPath),
                                   RollClockConfig.Load(configPath), rng, EndOfHalfConfig.Load(configPath), policy);
            gov.Run(TipPossession.CreateFromTip(g, rng, possessionNumber: 1));

            var st = policy.StateFor(TeamSide.Home);
            var cascadeLines = st.Log.Where(l => l.StartsWith("CASCADE", StringComparison.Ordinal)).ToList();
            // Every cascade line names three men: an exit, a relocation and an entry.
            var threeLegged = cascadeLines.All(l =>
                l.Contains("exit ", StringComparison.Ordinal) &&
                l.Contains("relocate ", StringComparison.Ordinal) &&
                l.Contains("enter ", StringComparison.Ordinal));
            var legal = LineupIsLegal(g, home);

            var ok = st.Cascades > 0 && threeLegged && legal;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    {st.Cascades} cascade(s), every line three-legged, lineup legal -> ok"
                : $"    FAIL: cascades={st.Cascades} threeLegged={threeLegged} legal={legal}");
            if (cascadeLines.Count > 0)
                Console.WriteLine($"      e.g. {cascadeLines[0]}");
        }

        // ── ★ A rejected candidate leaves LITERALLY no trace ─────────────────────────
        //
        //  HONEST SCOPE NOTE. The prompt asked for a candidate cascade forced to FAIL final
        //  validation, with every counter asserted unchanged. That failure mode is not
        //  reachable in this implementation and the reason is worth stating rather than
        //  faking: legality is proved on the candidate BEFORE it is ever added to the move
        //  list, and the two seat writes commit only after a move has won the tie-break. A
        //  partially-applied cascade is therefore unrepresentable, not merely untaken —
        //  there is no code path that mutates and then validates. So what is tested here is
        //  the observable consequence, in two parts: a dead ball at which every candidate is
        //  rejected leaves zero residue, and every move that IS applied leaves a legal five.
        Console.WriteLine("  Sub-check 5: rejected candidates leave zero residue; every applied move leaves a legal five");
        {
            // (a) Early dead ball: residuals are still inside the hysteresis band, so
            // candidates are enumerated and every one is refused. Nothing may move.
            var g = NewGame();
            var seats = shapeSeats["2G/1W/2B"];
            var home = Build13(g, TeamSide.Home, 1, seats);
            var away = Build13(g, TeamSide.Away, 1 + RosterShape.AwayIdOffset, seats);
            var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);
            var st = policy.StateFor(TeamSide.Home);

            // Two live-ball records. The dead-ball call below credits a THIRD before it
            // evaluates, so the evaluation sees R = 3: every starter's protected stint
            // stands at 3 against a 4-record minimum, and the largest bench target (32 min)
            // has accumulated only 0.16 x 5 x 3 = 2.4 credits of residual against an enter
            // threshold of 2.5. Candidates are enumerated and refused on both counts.
            for (var i = 0; i < 2; i++)
                policy.OnPossessionBoundary(g, i + 2, 18.0, isDeadBall: false);

            var seatsBefore   = SeatIds(g, TeamSide.Home);
            var stintsBefore  = st.StintRecords.ToDictionary(k => k.Key, v => v.Value);
            var creditsBefore = st.ActualCredits.ToDictionary(k => k.Key, v => v.Value);
            var logBefore     = st.Log.Count;
            var subsBefore    = st.Substitutions;
            var cascBefore    = st.Cascades;
            var straightBefore= st.Straights;
            var relocBefore   = st.LastRelocationEvaluation.Count;

            policy.OnPossessionBoundary(g, 5, 18.0, isDeadBall: true);

            var seatsSame = SeatIds(g, TeamSide.Home).SequenceEqual(seatsBefore);
            var subsSame  = st.Substitutions == subsBefore && st.Cascades == cascBefore && st.Straights == straightBefore;
            var logSame   = st.Log.Count == logBefore;
            var relocSame = st.LastRelocationEvaluation.Count == relocBefore;
            // Credits and stints advance by exactly one record for the on-floor five and are
            // untouched for everyone else — the ordinary passage of time, not a side effect
            // of the rejected candidates.
            var creditsSane = st.ActualCredits.All(kv =>
                kv.Value == creditsBefore[kv.Key] + (seatsBefore.Contains(kv.Key) ? 1 : 0));
            var stintsSane = st.StintRecords.All(kv =>
                kv.Value == stintsBefore[kv.Key] + (seatsBefore.Contains(kv.Key) ? 1 : 0));
            var noResidue = seatsSame && subsSame && logSame && relocSame && creditsSane && stintsSane;

            // (b) Legality after EVERY evaluation, not merely at the final horn. A move that
            // produced an illegal five would otherwise be invisible if a later move happened
            // to repair it.
            var illegalAfterMove = 0;
            var movesSeen = 0;
            foreach (var (shape, sq) in shapeSeats)
            {
                var g2 = NewGame();
                var h2 = Build13(g2, TeamSide.Home, 1, sq);
                var a2 = Build13(g2, TeamSide.Away, 1 + RosterShape.AwayIdOffset, sq);
                var p2 = new MinutesAllocatorPolicy(h2, a2, halftimeEq);
                var s2 = p2.StateFor(TeamSide.Home);

                var lastSubs = 0;
                for (var i = 0; i < 200; i++)
                {
                    p2.OnPossessionBoundary(g2, i + 2, 18.0, isDeadBall: i % 3 == 2);
                    if (s2.Substitutions != lastSubs)
                    {
                        movesSeen += s2.Substitutions - lastSubs;
                        lastSubs = s2.Substitutions;
                        if (!LineupIsLegal(g2, h2)) illegalAfterMove++;
                    }
                }
            }

            var ok = noResidue && illegalAfterMove == 0 && movesSeen > 0;
            pass &= ok;
            Console.WriteLine(ok
                ? $"    no-op dead ball left lineup/log/counters untouched; {movesSeen} applied moves, 0 illegal fives -> ok"
                : $"    FAIL: seats={seatsSame} subs={subsSame} log={logSame} reloc={relocSame} credits={creditsSane} stints={stintsSane} illegalAfterMove={illegalAfterMove} moves={movesSeen}");
        }

        // ── Gates 7/8: convergence at production cadence, tier-aware tolerance ───────
        Console.WriteLine("  Sub-check 6: convergence — every positive-target man inside a tier-aware tolerance");
        {
            // ★ TIER-AWARE TOLERANCE. A fixed absolute error means very different things to
            // a 4-minute and a 32-minute man, while a fixed percentage is brutal on the tail
            // because possession granularity (~0.29 nominal minutes) dominates it. The rule
            // passes on whichever is MORE permissive: an absolute nominal-minute allowance
            // OR a relative one.
            const double AbsAllowanceMinutes = 2.5;
            const double RelAllowance        = 0.15;

            var allOk = true;
            foreach (var (shape, seats) in shapeSeats)
            {
                var errByPlayer = new Dictionary<int, List<double>>();
                var games = 12;
                var totalRecords = 0;
                var dnp = 0;
                var subsPerGame = new List<int>();

                for (var gi = 0; gi < games; gi++)
                {
                    var g = NewGame();
                    var home = Build13(g, TeamSide.Home, 1, seats);
                    var away = Build13(g, TeamSide.Away, 1 + RosterShape.AwayIdOffset, seats);
                    var policy = new MinutesAllocatorPolicy(home, away, halftimeEq);

                    var seed = 770000 + gi;
                    var rng = new SystemRng(seed);
                    var gov = new Governor(BuildStubResolver(g, new SystemRng(seed)), g, GovernorConfig.Load(configPath),
                                           RollClockConfig.Load(configPath), rng, EndOfHalfConfig.Load(configPath), policy);
                    gov.Run(TipPossession.CreateFromTip(g, rng, possessionNumber: 1));

                    var st = policy.StateFor(TeamSide.Home);
                    totalRecords += st.Records;
                    subsPerGame.Add(st.Substitutions);

                    foreach (var kv in st.TargetMinutes)
                    {
                        if (kv.Value <= 1e-12) continue;
                        // Realized nominal minutes = credits / records x 40.
                        var realized = st.Records > 0 ? st.ActualCredits.GetValueOrDefault(kv.Key) / (double)st.Records * 40.0 : 0.0;
                        if (st.ActualCredits.GetValueOrDefault(kv.Key) == 0) dnp++;
                        if (!errByPlayer.TryGetValue(kv.Key, out var list)) errByPlayer[kv.Key] = list = new List<double>();
                        list.Add(realized - kv.Value);
                    }
                }

                // Per player: the mean signed error across the games, judged against his own
                // target's tolerance band.
                var worst = 0.0; var worstTarget = 0.0; var outside = 0;
                {
                    var g0 = NewGame();
                    var d0 = Build13(g0, TeamSide.Home, 1, seats);
                    var s0 = new MinutesAllocatorPolicy(d0, Build13(NewGame(), TeamSide.Away, 14, seats), halftimeEq)
                                .StateFor(TeamSide.Home);
                    foreach (var kv in errByPlayer)
                    {
                        var target = s0.TargetMinutes[kv.Key];
                        var mean = kv.Value.Average();
                        var allowance = Math.Max(AbsAllowanceMinutes, RelAllowance * target);
                        if (Math.Abs(mean) > allowance) outside++;
                        if (Math.Abs(mean) > Math.Abs(worst)) { worst = mean; worstTarget = target; }
                    }
                }

                var ok = outside == 0;
                allOk &= ok;
                Console.WriteLine(
                    $"    {shape}: {games} games, mean R={totalRecords / (double)games:F0}, subs/game {subsPerGame.Average():F1}, " +
                    $"DNPs {dnp}, worst mean error {worst:+0.00;-0.00} min on a {worstTarget:F0}-min target, " +
                    $"{outside} outside tolerance {(ok ? "-> ok" : "-> FAIL")}");
            }
            pass &= allOk;
        }

        Console.WriteLine(pass ? "  Phase 72 minutes allocator: ok" : "  Phase 72 minutes allocator: FAIL");
        return pass;
    }

    /// <summary>Five unique men, all on this side, one seat each, every occupant eligible.</summary>
    private static bool LineupIsLegal(GameState g, SideDepth depth)
    {
        var roster = g.RosterFor(depth.Side);
        var seen = new HashSet<int>();
        for (var slot = 1; slot <= Lineup.Size; slot++)
        {
            var p = roster.PlayerAt(new Slot(depth.Side, slot));
            if (p is null) return false;
            if (!seen.Add(p.PlayerId)) return false;
            if (!depth.PlayerById.ContainsKey(p.PlayerId)) return false;
            if (!PositionalEligibility.IsEligibleForSeat(depth.PosById[p.PlayerId], depth.SlotPos[slot])) return false;
        }
        return true;
    }

    /// <summary>
    /// Max flow from the group targets to the seat-type capacities across the one-step
    /// eligibility ladder. Nodes: source, three stored groups, three seat types, sink.
    /// Returns the placeable minutes; a feasible table returns exactly 200.
    /// </summary>
    private static double MaxFlowMinutes(Dictionary<string, double> groupTarget, Dictionary<string, int> seatCount)
    {
        // 0 = source, 1..3 = groups G/W/B, 4..6 = seats G/W/B, 7 = sink.
        const int N = 8;
        var cap = new double[N, N];
        var order = new[] { "G", "W", "B" };

        for (var i = 0; i < 3; i++)
        {
            cap[0, 1 + i] = groupTarget[order[i]];
            cap[4 + i, 7] = 40.0 * seatCount[order[i]];
            for (var j = 0; j < 3; j++)
                if (PositionalEligibility.IsEligibleForSeat(order[i], order[j]))
                    cap[1 + i, 4 + j] = double.MaxValue / 4;
        }

        double flow = 0;
        while (true)
        {
            var parent = new int[N];
            for (var i = 0; i < N; i++) parent[i] = -1;
            parent[0] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                for (var v = 0; v < N; v++)
                    if (parent[v] == -1 && cap[u, v] > 1e-12) { parent[v] = u; queue.Enqueue(v); }
            }
            if (parent[7] == -1) break;

            var bottleneck = double.MaxValue;
            for (var v = 7; v != 0; v = parent[v]) bottleneck = Math.Min(bottleneck, cap[parent[v], v]);
            for (var v = 7; v != 0; v = parent[v]) { cap[parent[v], v] -= bottleneck; cap[v, parent[v]] += bottleneck; }
            flow += bottleneck;
        }
        return flow;
    }
}
