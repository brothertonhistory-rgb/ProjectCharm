using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // --- Batch: confirm A->B rates and clean hand-offs throughout. ---
    private static bool BatchCheck(
        RollAConfig cfg, RollBConfig cfgB,
        IRollAPieGenerator genA, IRollBPieGenerator genB,
        Resolver resolver, PossessionState state)
    {
        Console.WriteLine($"--- Batch: {cfg.BatchSize:N0} possessions (full chain, physicality=0.00) ---");
        var rng = new SystemRng(cfg.Seed);
        const double pressure = 0.0;
        var pieA = genA.Generate(state, pressure);

        var aCounts = new Dictionary<EntryOutcome, int>();
        foreach (var o in Enum.GetValues<EntryOutcome>()) aCounts[o] = 0;

        var bCounts = new Dictionary<HalfcourtOutcome, int>();
        foreach (var o in Enum.GetValues<HalfcourtOutcome>()) bCounts[o] = 0;

        var ended = 0;
        var routedToStub = 0;
        var unrouted = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollA.Execute(state, pieA, rng, cfg);

            var aOutcome = result switch
            {
                Continue { Next: ContinuationKind.IntoHalfcourtSet } => EntryOutcome.CleanEntry,
                Continue { Next: ContinuationKind.ResolveTurnoverType } => EntryOutcome.Turnover,
                Continue { Next: ContinuationKind.ResolveOffensiveFoul } => EntryOutcome.OffensiveFoul,
                Continue { Next: ContinuationKind.ResolveFoulType } => EntryOutcome.DefensiveFoul,
                Continue { Next: ContinuationKind.ResolveJumpBall } => EntryOutcome.JumpBall,
                _ => throw new InvalidOperationException("Unmapped Roll A result.")
            };
            aCounts[aOutcome]++;

            if (aOutcome == EntryOutcome.CleanEntry)
            {
                var pieBSample = genB.Generate(state, physicality: 0.0);
                var bResult = RollB.Execute(state, pieBSample, rng);
                var bOutcome = bResult switch
                {
                    Continue { Next: ContinuationKind.IntoPlayerSelection } => HalfcourtOutcome.Proceed,
                    Continue { Next: ContinuationKind.ResolveFoulType } => HalfcourtOutcome.Foul,
                    Continue { Next: ContinuationKind.ResolveTurnoverType } => HalfcourtOutcome.DeadBallTurnover,
                    Continue { Next: ContinuationKind.ResolveJumpBall } => HalfcourtOutcome.JumpBall,
                    _ => throw new InvalidOperationException("Unmapped Roll B result.")
                };
                bCounts[bOutcome]++;
            }

            var routing = resolver.Route(result);
            if (routing.PossessionEnded) ended++;
            else if (routing.Destination.StartsWith("STUB:")) routedToStub++;
            else unrouted++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;

        Console.WriteLine("  Roll A outcomes:");
        foreach (var (outcome, weight) in pieA.Slices)
        {
            var observed = aCounts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-18} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var cleanEntries = aCounts[EntryOutcome.CleanEntry];
        var pieB = genB.Generate(state, physicality: 0.0);
        Console.WriteLine($"\n  Roll B outcomes (of {cleanEntries:N0} clean entries):");
        foreach (var (outcome, weight) in pieB.Slices)
        {
            var observed = bCounts[outcome] / (double)cleanEntries;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfgB.Epsilon + cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-18} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        // As of Contextification #6 the possession chain CLOSES: the two inbound
        // edges that used to park (Roll D below-bonus ResumeInbound, OOB-retained
        // ResolveSidelineInbound) now RE-RUN Roll A and resolve downstream, and Roll
        // A's three former violation terminals moved into Roll C. So nothing parks at
        // a stub in the live chain anymore — every possession ends on a terminal.
        // `routed-to-stub` therefore drops to ~0 and `ended` rises to ≈ BatchSize.
        // (The shared game's fouls climb across the batch and cross the bonus within
        // the first few possessions, so almost every defensive foul becomes a
        // free-throw trip rather than a re-inbound — both terminate.) The invariant is
        // unchanged: ended + routed-to-stub == BatchSize, unrouted == 0.
        var handoffOk = unrouted == 0 && (ended + routedToStub) == cfg.BatchSize;
        Console.WriteLine($"\n  handoff: ended={ended:N0}, routed-to-stub={routedToStub:N0}, unrouted={unrouted} -> {(handoffOk ? "ok" : "FAIL")}");

        return ratesOk && handoffOk;
    }


    // --- Prove Roll B's seam carries signal: foul rate must rise with physicality. ---
    private static bool PhysicalitySignalCheck(RollBConfig cfgB, RollBStubPieGenerator genB, PossessionState state)
    {
        Console.WriteLine("\n--- Seam signal: Roll B foul rate vs. physicality ---");
        var rng = new SystemRng(42);
        var low = RollBFoulRate(cfgB, genB, state, rng, physicality: 0.0);
        rng = new SystemRng(42);
        var high = RollBFoulRate(cfgB, genB, state, rng, physicality: 1.0);

        Console.WriteLine($"  foul rate @ physicality 0.00 = {low:P3}");
        Console.WriteLine($"  foul rate @ physicality 1.00 = {high:P3}");

        var moved = high > low;
        Console.WriteLine($"  signal is live (high > low): {(moved ? "ok" : "FAIL")}");
        return moved;
    }


    private static double RollBFoulRate(
        RollBConfig cfgB, RollBStubPieGenerator genB,
        PossessionState state, IRng rng, double physicality)
    {
        var pie = genB.Generate(state, physicality);
        var fouls = 0;
        const int n = 100_000;
        for (var i = 0; i < n; i++)
            if (RollB.Execute(state, pie, rng) is Continue { Next: ContinuationKind.ResolveFoulType })
                fouls++;
        return fouls / (double)n;
    }


    // --- Jump ball: verify the three arrow behaviors directly. ---
    private static bool JumpBallCheck(RollAConfig cfg)
    {
        Console.WriteLine("\n--- Jump ball: possession arrow behavior ---");
        var allOk = true;

        // Jump-ball behavior is independent of fouls; supply any valid tracker.
        // A local factory keeps the four constructions below readable.
        static GameState NewGame(ArrowState arrow = ArrowState.Off) =>
            new(new FoulTracker(bonusThreshold: 7, doubleBonusThreshold: 10), arrow);

        // (1) Opening tip is ~50/50, and the arrow is set to the tip LOSER.
        var rng = new SystemRng(cfg.Seed);
        var homeWins = 0;
        var arrowToLoser = 0;
        const int tips = 100_000;
        for (var i = 0; i < tips; i++)
        {
            var g = NewGame();  // Off -> tip
            var award = JumpBall.Resolve(g, rng);
            if (award.AwardedTo == TeamSide.Home) homeWins++;

            // Arrow must point at the loser (the team NOT awarded the ball).
            var arrowTeam = g.PossessionArrow == ArrowState.Home ? TeamSide.Home : TeamSide.Away;
            if (arrowTeam != award.AwardedTo) arrowToLoser++;

            // Every tip must be flagged a contest and must turn the arrow ON.
            if (!award.WasTipContest || g.PossessionArrow == ArrowState.Off) allOk = false;
        }
        var homeRate = homeWins / (double)tips;
        var tipFair = Math.Abs(homeRate - 0.5) <= cfg.RateTolerance;
        var loserOk = arrowToLoser == tips;
        allOk &= tipFair && loserOk;
        Console.WriteLine($"  tip coin-flip: home wins {homeRate:P3} (expected 50.000%) -> {(tipFair ? "ok" : "FAIL")}");
        Console.WriteLine($"  arrow set to tip-loser every time: {arrowToLoser:N0}/{tips:N0} -> {(loserOk ? "ok" : "FAIL")}");

        // (2) On-arrow jump ball awards the pointed-at team, then flips.
        var g2 = NewGame(ArrowState.Home);
        var a2 = JumpBall.Resolve(g2, rng);
        var awardOk = a2.AwardedTo == TeamSide.Home && !a2.WasTipContest;
        var flipOk = g2.PossessionArrow == ArrowState.Away;
        allOk &= awardOk && flipOk;
        Console.WriteLine($"  on-arrow(Home): awarded={a2.AwardedTo} contest={a2.WasTipContest} -> {(awardOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  arrow flipped Home->{g2.PossessionArrow}: {(flipOk ? "ok" : "FAIL")}");

        // (3) A run of alternating-possession situations strictly alternates.
        var g3 = NewGame(ArrowState.Home);
        var expected = new[] { TeamSide.Home, TeamSide.Away, TeamSide.Home, TeamSide.Away, TeamSide.Home };
        var alternates = true;
        foreach (var exp in expected)
            if (JumpBall.Resolve(g3, rng).AwardedTo != exp) alternates = false;
        allOk &= alternates;
        Console.WriteLine($"  five awards strictly alternate from Home: {(alternates ? "ok" : "FAIL")}");

        // (4) Flipping an Off arrow must throw (guard against silent misuse).
        var threw = false;
        try { NewGame().FlipPossessionArrow(); }
        catch (InvalidOperationException) { threw = true; }
        allOk &= threw;
        Console.WriteLine($"  flipping an Off arrow throws: {(threw ? "ok" : "FAIL")}");

        return allOk;
    }


    // --- Batch: Roll J's five-way run-or-not distribution (rebound context) converges
    //     within tolerance; every exit is a clean Continue of the expected kind (Roll J
    //     names no terminal — all five arms are continues). Push and Settle now BOTH
    //     exit via IntoPlayerSelection, split by the FastBreak marker (Push stamps it,
    //     Settle does not) — so the batch proves they are distinguishable downstream.
    //     The Turnover arm stamps the Transition context for Roll C; and the
    //     DefensiveFoul arm charges the defense and forks on the bonus (this batch's own
    //     game crosses the threshold partway, exercising the §2a crossing). ---
    private static bool RollJBatchCheck(
        RollAConfig cfg, RollDConfig cfgD, RollJConfig cfgJ,
        IRollJPieGenerator genJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} transition entries through Roll J (rebound context) ---");

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game (does not perturb Main's shared game). Its defense foul count
        // climbs as the DefensiveFoul arm fires and CROSSES the bonus partway through
        // — so both fork branches (sideline below bonus, FT in bonus) are exercised.
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var pieJ = genJ.Generate(TransitionContext.Rebound);

        var counts = new Dictionary<TransitionOutcome, int>();
        foreach (var o in Enum.GetValues<TransitionOutcome>()) counts[o] = 0;

        var settleSel = 0;          // IntoPlayerSelection, FastBreak=false (Settle -> halfcourt)
        var pushSel = 0;            // IntoPlayerSelection, FastBreak=true  (Push   -> transition)
        var pushFastBreakOk = 0;    // Push continues that correctly carry FastBreak=true
        var turnoverStamped = 0;    // ResolveTurnoverType carrying TurnoverContext.Transition
        var turnoverUnstamped = 0;  // ResolveTurnoverType MISSING the stamp (a FAIL)
        var foulSideline = 0;       // ResolveSidelineInbound (below bonus)
        var foulFreeThrows = 0;     // ResolveFreeThrows (in bonus)
        var jumpBalls = 0;          // ResolveJumpBall
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var r = RollJ.Execute(state, pieJ, game, rng);
            switch (r)
            {
                // Push AND Settle now BOTH exit via IntoPlayerSelection — the split is
                // the FastBreak marker on the carried state (Push stamps it true, Settle
                // leaves it false). Counting by FastBreak is the proof the two are
                // distinguishable downstream despite sharing the edge.
                case Continue { Next: ContinuationKind.IntoPlayerSelection } sel:
                    if (sel.State.FastBreak)
                    {
                        counts[TransitionOutcome.Push]++;
                        pushSel++;
                        pushFastBreakOk++;   // by construction FastBreak is true here
                    }
                    else
                    {
                        counts[TransitionOutcome.Settle]++;
                        settleSel++;
                    }
                    break;
                case Continue { Next: ContinuationKind.ResolveTurnoverType } tc:
                    counts[TransitionOutcome.Turnover]++;
                    if (tc.TurnoverContext == TurnoverContext.Transition) turnoverStamped++;
                    else turnoverUnstamped++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulSideline++;
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulFreeThrows++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[TransitionOutcome.JumpBall]++;
                    jumpBalls++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll J outcomes:");
        foreach (var (outcome, weight) in pieJ.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    Settle        -> IntoPlayerSelection (halfcourt) {settleSel,8:N0}  {(settleSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    Push          -> IntoPlayerSelection (FastBreak)  {pushSel,8:N0}  {(pushSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"      of which carry FastBreak=true: {pushFastBreakOk,8:N0} / {pushSel:N0}  {(pushFastBreakOk == pushSel ? "ok" : "FAIL")}");
        Console.WriteLine($"    Turnover      -> Roll C (stamped)    {turnoverStamped,8:N0}  {(turnoverStamped > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    DefensiveFoul -> SidelineInbound     {foulSideline,8:N0}  (below bonus)");
        Console.WriteLine($"                  -> ResolveFreeThrows   {foulFreeThrows,8:N0}  (in bonus)");
        Console.WriteLine($"    JumpBall      -> ResolveJumpBall     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var foulTotal = foulSideline + foulFreeThrows;
        var pushOk = pushSel > 0 && pushFastBreakOk == pushSel;   // every Push exit carried the marker
        var allArms = settleSel > 0 && pushOk && turnoverStamped > 0 && foulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var stampOk = turnoverUnstamped == 0;
        Console.WriteLine($"\n  zero unrouted exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every Turnover stamped Transition for Roll C: unstamped={turnoverUnstamped} -> {(stampOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all five Roll J arms reached: {(allArms ? "ok" : "FAIL")}");

        return ratesOk && routedOk && stampOk && allArms;
    }


    // --- Bonus fork: Roll J's DefensiveFoul arm charges the DEFENSE team foul and
    //     forks on the bonus exactly as Roll I and Roll D do — the third feeder into
    //     the shared charge-and-fork. All-mass-on-DefensiveFoul pie so every draw
    //     exercises the arm regardless of RNG. Mirrors RollIBonusForkCheck. ---
    private static bool RollJBonusForkCheck(
        RollAConfig cfg, RollDConfig cfgD, RollJConfig cfgJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Bonus fork: Roll J defensive foul across the thresholds ---");

        var foulOnlyPie = new Pie<TransitionOutcome>(new Dictionary<TransitionOutcome, double>
        {
            [TransitionOutcome.Settle] = 0.0,
            [TransitionOutcome.Push] = 0.0,
            [TransitionOutcome.Turnover] = 0.0,
            [TransitionOutcome.DefensiveFoul] = 1.0,
            [TransitionOutcome.JumpBall] = 0.0,
        }, cfgJ.Epsilon);

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var rng = new SystemRng(cfg.Seed);

        var ok = true;
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var r = RollJ.Execute(state, foulOnlyPie, game, rng);
            var after = game.Fouls.FoulsFor(state.Defense);

            // Expected routing from the POST-increment foul count.
            var expectedBonus = after >= cfgD.DoubleBonusThreshold ? BonusType.Double
                              : after >= cfgD.BonusThreshold ? BonusType.OneAndOne
                              : BonusType.None;
            var expectedKind = expectedBonus == BonusType.None
                ? ContinuationKind.ResolveSidelineInbound
                : ContinuationKind.ResolveFreeThrows;

            var c = r as Continue;
            var kindOk = c is not null && c.Next == expectedKind;
            var bonusOk = c is not null && c.Bonus == expectedBonus;
            var chargedOk = after == before + 1;
            var rowOk = kindOk && bonusOk && chargedOk;
            ok &= rowOk;

            var route = c is null ? "(not a continue!)"
                : c.Next == ContinuationKind.ResolveFreeThrows
                    ? $"ResolveFreeThrows({c.Bonus})"
                    : $"SidelineInbound({c.Bonus})";
            Console.WriteLine(
                $"  foul#{after,2}: {before}->{after} | route={route,-28} | expected={expectedKind} ({expectedBonus}) -> {(rowOk ? "ok" : "FAIL")}");
        }

        Console.WriteLine($"  bonus fork charges the defense and routes correctly: {(ok ? "ok" : "FAIL")}");
        return ok;
    }


    // --- Batch: Roll J's five-way distribution under the STEAL context (Contextification
    //     #3). Identical in shape to the rebound batch — Roll J's five arms and routing are
    //     source-agnostic; only the pie differs (the steal pie leans hardest to Push). Proves
    //     (a) the steal pie's five arms all route as designed, (b) the Turnover arm still
    //     stamps Transition for Roll C, and (c) the §2a bonus crossing on the STEAL path: this
    //     batch's own foul-accumulating game crosses the threshold partway, so the DefensiveFoul
    //     arm forks BOTH sideline (below bonus) and FT (in bonus) — the same charge-and-fork the
    //     rebound DefensiveFoul arm feeds. ---
    private static bool RollJStealBatchCheck(
        RollAConfig cfg, RollDConfig cfgD, RollJConfig cfgJ,
        IRollJPieGenerator genJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} transition entries through Roll J (STEAL context) ---");

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game (does not perturb Main's shared game). Its defense foul count
        // climbs as the DefensiveFoul arm fires and CROSSES the bonus partway through —
        // the §2a stateful-accumulation check on the steal path: an arm that routes to
        // sideline early routes to FT once the shared game enters the bonus, so BOTH
        // fork branches must be exercised.
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var pieJ = genJ.Generate(TransitionContext.Steal);

        var counts = new Dictionary<TransitionOutcome, int>();
        foreach (var o in Enum.GetValues<TransitionOutcome>()) counts[o] = 0;

        var settleSel = 0;          // IntoPlayerSelection, FastBreak=false (Settle -> halfcourt)
        var pushSel = 0;            // IntoPlayerSelection, FastBreak=true  (Push   -> transition)
        var pushFastBreakOk = 0;    // Push continues that correctly carry FastBreak=true
        var turnoverStamped = 0;    // ResolveTurnoverType carrying TurnoverContext.Transition
        var turnoverUnstamped = 0;  // ResolveTurnoverType MISSING the stamp (a FAIL)
        var foulSideline = 0;       // ResolveSidelineInbound (below bonus)
        var foulFreeThrows = 0;     // ResolveFreeThrows (in bonus)
        var jumpBalls = 0;          // ResolveJumpBall
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var r = RollJ.Execute(state, pieJ, game, rng);
            switch (r)
            {
                case Continue { Next: ContinuationKind.IntoPlayerSelection } sel:
                    if (sel.State.FastBreak)
                    {
                        counts[TransitionOutcome.Push]++;
                        pushSel++;
                        pushFastBreakOk++;   // by construction FastBreak is true here
                    }
                    else
                    {
                        counts[TransitionOutcome.Settle]++;
                        settleSel++;
                    }
                    break;
                case Continue { Next: ContinuationKind.ResolveTurnoverType } tc:
                    counts[TransitionOutcome.Turnover]++;
                    if (tc.TurnoverContext == TurnoverContext.Transition) turnoverStamped++;
                    else turnoverUnstamped++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulSideline++;
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows }:
                    counts[TransitionOutcome.DefensiveFoul]++;
                    foulFreeThrows++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[TransitionOutcome.JumpBall]++;
                    jumpBalls++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll J outcomes (steal pie):");
        foreach (var (outcome, weight) in pieJ.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    Settle        -> IntoPlayerSelection (halfcourt) {settleSel,8:N0}  {(settleSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    Push          -> IntoPlayerSelection (FastBreak)  {pushSel,8:N0}  {(pushSel > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"      of which carry FastBreak=true: {pushFastBreakOk,8:N0} / {pushSel:N0}  {(pushFastBreakOk == pushSel ? "ok" : "FAIL")}");
        Console.WriteLine($"    Turnover      -> Roll C (stamped)    {turnoverStamped,8:N0}  {(turnoverStamped > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    DefensiveFoul -> SidelineInbound     {foulSideline,8:N0}  (below bonus)");
        Console.WriteLine($"                  -> ResolveFreeThrows   {foulFreeThrows,8:N0}  (in bonus)");
        Console.WriteLine($"    JumpBall      -> ResolveJumpBall     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var pushOk = pushSel > 0 && pushFastBreakOk == pushSel;   // every Push exit carried the marker
        var foulForkOk = foulSideline > 0 && foulFreeThrows > 0;  // §2a crossing on the steal path
        var allArms = settleSel > 0 && pushOk && turnoverStamped > 0 && foulForkOk && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var stampOk = turnoverUnstamped == 0;
        Console.WriteLine($"\n  zero unrouted exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every Turnover stamped Transition for Roll C: unstamped={turnoverUnstamped} -> {(stampOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: steal DefensiveFoul crossed the bonus mid-batch (sideline -> FT): {(foulForkOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all five Roll J steal arms reached: {(allArms ? "ok" : "FAIL")}");

        return ratesOk && routedOk && stampOk && allArms;
    }


    private static bool Phase13TeamDisruptionDoorCheckRollB(string configPath)
    {
        Console.WriteLine("\n--- Phase 13: Team-aggregate disruption door (Roll B) ---");
        var pass = true;

        var cfgB       = RollBConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        var actionMass    = cfgB.BaseProceed + cfgB.BaseFoul + cfgB.BaseDeadBallTurnover;
        var baseToShare   = cfgB.BaseDeadBallTurnover / actionMass;
        var baseFoulShare = cfgB.BaseFoul             / actionMass;

        // Helper: player with all attributes at b; override BallHandling and Steals.
        static Player Mk(int b, int? bh = null, int? st = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = bh ?? b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b,
                Steals = st ?? b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline = b, HelpDefense = b, OffBallDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: minimal GameState with Home offense and Away defense.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        // Helper: PossessionState. Roll B does NOT read SelectedSlot, so null is the
        // natural state. We also test with a stamped slot (check f).
        static PossessionState St(Slot? slot = null)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: slot,
                ShotType: null);

        // Helper: construct RollBGenerator, generate pie, return dict.
        static Dictionary<HalfcourtOutcome, double> Split(
            RollBConfig cfgB, MatchupConfig cfgMatchup, GameState g, PossessionState state)
        {
            var gen = new RollBGenerator(cfgB, cfgMatchup, g);
            var pie = gen.Generate(state, physicality: 0.0);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // Helper: MatchupConfig with Away (defense) pressure set to p.
        MatchupConfig WithAwayPressure(double p)
        {
            var c = MatchupConfig.Load(configPath);
            c.AwayPressure = p;
            return c;
        }

        var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
        var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

        // ── (a) Neutral anchor ─────────────────────────────────────────────
        Console.WriteLine("  (a) Neutral anchor (pressure=5, all-50 even aggregate): all four arms == config baseline:");
        bool aOk;
        try
        {
            var g = BuildGame(off5, def5);
            var d = Split(cfgB, cfgMatchup, g, St());   // cfgMatchup has neutral pressure=5

            var prOk = Math.Abs(d[HalfcourtOutcome.Proceed]          - cfgB.BaseProceed)          < Eps;
            var toOk = Math.Abs(d[HalfcourtOutcome.DeadBallTurnover] - cfgB.BaseDeadBallTurnover) < Eps;
            var foOk = Math.Abs(d[HalfcourtOutcome.Foul]             - cfgB.BaseFoul)             < Eps;
            var juOk = Math.Abs(d[HalfcourtOutcome.JumpBall]         - cfgB.BaseJumpBall)         < Eps;
            aOk = prOk && toOk && foOk && juOk;
            Console.WriteLine($"    Proceed       ={d[HalfcourtOutcome.Proceed]:F8}  want={cfgB.BaseProceed:F8}  {(prOk?"OK":"FAIL")}");
            Console.WriteLine($"    DeadBallTO    ={d[HalfcourtOutcome.DeadBallTurnover]:F8}  want={cfgB.BaseDeadBallTurnover:F8}  {(toOk?"OK":"FAIL")}");
            Console.WriteLine($"    Foul          ={d[HalfcourtOutcome.Foul]:F8}  want={cfgB.BaseFoul:F8}  {(foOk?"OK":"FAIL")}");
            Console.WriteLine($"    JumpBall      ={d[HalfcourtOutcome.JumpBall]:F8}  want={cfgB.BaseJumpBall:F8}  {(juOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;
        Console.WriteLine($"  (a) {(aOk ? "ok" : "FAIL")}");

        // ── (b) Pressure raises turnovers ──────────────────────────────────
        Console.WriteLine("  (b) Pressure raises TO — even aggregate:");
        bool bOk;
        try
        {
            var g = BuildGame(off5, def5);
            var dLow  = Split(cfgB, WithAwayPressure(2.0), g, St());
            var dNeut = Split(cfgB, cfgMatchup,             g, St());
            var dHigh = Split(cfgB, WithAwayPressure(9.0), g, St());
            var toRise = dLow[HalfcourtOutcome.DeadBallTurnover]
                       < dNeut[HalfcourtOutcome.DeadBallTurnover]
                       && dNeut[HalfcourtOutcome.DeadBallTurnover]
                       < dHigh[HalfcourtOutcome.DeadBallTurnover];
            bOk = toRise;
            Console.WriteLine($"    TO @ p=2: {dLow[HalfcourtOutcome.DeadBallTurnover]:F6}  p=5: {dNeut[HalfcourtOutcome.DeadBallTurnover]:F6}  p=9: {dHigh[HalfcourtOutcome.DeadBallTurnover]:F6}  rise={toRise}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;
        Console.WriteLine($"  (b) {(bOk ? "ok" : "FAIL")}");

        // ── (c) Pressure raises fouls ──────────────────────────────────────
        Console.WriteLine("  (c) Pressure raises Foul:");
        bool cOk;
        try
        {
            var g = BuildGame(off5, def5);
            var fLow  = Split(cfgB, WithAwayPressure(2.0), g, St())[HalfcourtOutcome.Foul];
            var fNeut = Split(cfgB, cfgMatchup,             g, St())[HalfcourtOutcome.Foul];
            var fHigh = Split(cfgB, WithAwayPressure(9.0), g, St())[HalfcourtOutcome.Foul];
            cOk = fLow < fNeut && fNeut < fHigh;
            Console.WriteLine($"    Foul @ p=2: {fLow:F6}  p=5: {fNeut:F6}  p=9: {fHigh:F6}  rise={cOk}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;
        Console.WriteLine($"  (c) {(cOk ? "ok" : "FAIL")}");

        // ── (d) Cap holds ──────────────────────────────────────────────────
        Console.WriteLine("  (d) Cap holds at max pressure + worst matchup:");
        bool dOk;
        try
        {
            var offWeak  = new[] { Mk(50, bh: 1), Mk(50, bh: 1), Mk(50, bh: 1), Mk(50, bh: 1), Mk(50, bh: 1) };
            var defElite = new[] { Mk(50, st: 99), Mk(50, st: 99), Mk(50, st: 99), Mk(50, st: 99), Mk(50, st: 99) };
            var g   = BuildGame(offWeak, defElite);
            var d   = Split(cfgB, WithAwayPressure(10.0), g, St());
            var toShare  = d[HalfcourtOutcome.DeadBallTurnover] / actionMass;
            var foShare  = d[HalfcourtOutcome.Foul]             / actionMass;
            var capTo    = toShare  <= cfgMatchup.RollBTurnoverCeiling    + Eps;
            var capFo    = foShare  <= cfgMatchup.RollBFoulPressureCeiling + Eps;
            var procPos  = d[HalfcourtOutcome.Proceed] > 0.0;
            dOk = capTo && capFo && procPos;
            Console.WriteLine($"    TO share={toShare:F6}  ceiling={cfgMatchup.RollBTurnoverCeiling}  capped={capTo}");
            Console.WriteLine($"    Fo share={foShare:F6}  ceiling={cfgMatchup.RollBFoulPressureCeiling}  capped={capFo}");
            Console.WriteLine($"    Proceed > 0: {procPos}");
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;
        Console.WriteLine($"  (d) {(dOk ? "ok" : "FAIL")}");

        // ── (e) JumpBall exactly flat; all arms sum to 1 ──────────────────
        Console.WriteLine("  (e) JumpBall exactly flat, sum = 1:");
        bool eOk = true;
        try
        {
            var cases = new (string label, MatchupConfig cfg, Player[] off, Player[] def)[]
            {
                ("p=2 even",     WithAwayPressure(2.0),  off5, def5),
                ("p=5 even",     cfgMatchup,              off5, def5),
                ("p=9 even",     WithAwayPressure(9.0),  off5, def5),
                ("p=9 def_adv",  WithAwayPressure(9.0),
                    new[]{Mk(50,bh:20),Mk(50,bh:20),Mk(50,bh:20),Mk(50,bh:20),Mk(50,bh:20)},
                    new[]{Mk(50,st:80),Mk(50,st:80),Mk(50,st:80),Mk(50,st:80),Mk(50,st:80)}),
            };
            foreach (var (label, cfg, off, def) in cases)
            {
                var g   = BuildGame(off, def);
                var d   = Split(cfgB, cfg, g, St());
                var jbOk  = Math.Abs(d[HalfcourtOutcome.JumpBall] - cfgB.BaseJumpBall) < Eps;
                var sumOk = Math.Abs(d.Values.Sum() - 1.0) < Eps;
                var ok2   = jbOk && sumOk;
                eOk &= ok2;
                Console.WriteLine($"    {label,-14}  JumpBall={d[HalfcourtOutcome.JumpBall]:F8}  sum={d.Values.Sum():F10}  {(ok2?"ok":"FAIL")}");
            }
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;
        Console.WriteLine($"  (e) {(eOk ? "ok" : "FAIL")}");

        // ── (f) SelectedSlot does not affect the aggregate ─────────────────
        // Roll B reads all 5 slots, not the selected slot. A null SelectedSlot
        // and a stamped SelectedSlot must produce identical pies.
        Console.WriteLine("  (f) SelectedSlot-blind (Roll B reads all slots, not the selected one):");
        bool fOk;
        try
        {
            var g       = BuildGame(off5, def5);
            var dNull   = Split(cfgB, cfgMatchup, g, St(null));
            var dStamped = Split(cfgB, cfgMatchup, g, St(g.HomeLineup.SlotAt(1)));
            var allMatch = dNull.Keys.All(k => Math.Abs(dNull[k] - dStamped[k]) < Eps);
            fOk = allMatch;
            Console.WriteLine($"    null slot vs stamped slot → identical pie: {(allMatch?"OK":"FAIL")}");
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;
        Console.WriteLine($"  (f) {(fOk ? "ok" : "FAIL")}");

        // ── (g) Matchup matters — Option B invariant ───────────────────────
        // Elite defense Steals vs. average BH offense at high pressure must
        // produce more turnovers than an even matchup (proves team aggregate fires).
        Console.WriteLine("  (g) Matchup matters (Option B): def-adv > even at high pressure:");
        bool gOk;
        try
        {
            var cfgHigh  = WithAwayPressure(9.0);
            var offAvg   = new[] { Mk(50, bh: 50), Mk(50, bh: 50), Mk(50, bh: 50), Mk(50, bh: 50), Mk(50, bh: 50) };
            var defElite = new[] { Mk(50, st: 80), Mk(50, st: 80), Mk(50, st: 80), Mk(50, st: 80), Mk(50, st: 80) };
            var defAvg   = new[] { Mk(50, st: 50), Mk(50, st: 50), Mk(50, st: 50), Mk(50, st: 50), Mk(50, st: 50) };
            var gElite = BuildGame(offAvg, defElite);
            var gAvg   = BuildGame(offAvg, defAvg);
            var toElite = Split(cfgB, cfgHigh, gElite, St())[HalfcourtOutcome.DeadBallTurnover];
            var toAvg   = Split(cfgB, cfgHigh, gAvg,   St())[HalfcourtOutcome.DeadBallTurnover];
            gOk = toElite > toAvg;
            Console.WriteLine($"    TO @ elite-steal def={toElite:F6}  average-steal def={toAvg:F6}  defAdv lifts: {gOk}");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;
        Console.WriteLine($"  (g) {(gOk ? "ok" : "FAIL")}");

        Console.WriteLine(pass ? "  Phase 13 PASSED." : "  Phase 13 FAILED.");
        return pass;
    }


    // ── Phase 14: Full-court press disruption door (Roll A, backcourt entry) ──
    // Proves: (a) neutral anchor; (b) press raises TO; (c) press raises DefFoul;
    // (d) press raises OffFoul; (e) OffFoul < DefFoul at every press level;
    // (f) cap/overflow sanity; (g) JumpBall exactly flat off-neutral;
    // (h) five arms sum to 1; (i) Frontcourt=true returns exact baseline;
    // (j) SelectedSlot-blind (Roll A is pre-selection);
    // (k) per-axis wiring: skill, athleticism, size each move TO when varied alone,
    //     and size produces the smallest movement of the three.
    private static bool Phase15PressFrequencyStandardCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 15: Press frequency + Standard mode (Roll A) ---");
        var pass = true;
        const double Eps = 1e-9;

        // ── Shared helpers ────────────────────────────────────────────────────
        // Player builder: all attributes at b; override as needed.
        static Player Mk(int b, int? bh = null, int? st = null,
                         int? ath = null, int? len = null)
        {
            var a = ath ?? b;
            var l = len ?? b;
            return new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = bh ?? b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b, Steals = st ?? b,
                Height = l, Wingspan = l, Weight = b,
                Strength = a, Speed = a, Quickness = a, FirstStep = a,
                Vertical = l,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b, HelpDefense = b, OffBallDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };
        }

        // GameState builder: Home offense slots 1-5, Away defense slots 1-5.
        // Defense = Away, so PressProbabilityFor reads AwayPressFrequency.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        var even5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

        // ── (1) PressProbabilityFor — pure function ───────────────────────────
        Console.WriteLine("  (1) PressProbabilityFor — pure function (no simulation):");
        bool t1Ok;
        try
        {
            var cfgM = MatchupConfig.Load(configPath);

            cfgM.AwayPressFrequency = 1.0;
            var atOne = cfgM.PressProbabilityFor(TeamSide.Away);
            cfgM.AwayPressFrequency = 10.0;
            var atTen = cfgM.PressProbabilityFor(TeamSide.Away);
            cfgM.AwayPressFrequency = 5.5;
            var atMid = cfgM.PressProbabilityFor(TeamSide.Away);
            var expectedMid = cfgM.PressProbabilityAtOne
                            + 4.5 / 9.0 * (cfgM.PressProbabilityAtTen - cfgM.PressProbabilityAtOne);

            var atOneOk = Math.Abs(atOne - cfgM.PressProbabilityAtOne) < Eps;
            var atTenOk = Math.Abs(atTen - cfgM.PressProbabilityAtTen) < Eps;
            var midOk   = Math.Abs(atMid - expectedMid)                  < Eps;
            var rangeOk = atOne >= 0.0 && atOne <= 1.0 && atTen >= 0.0 && atTen <= 1.0;
            var monoOk  = atOne <= atMid && atMid <= atTen;

            t1Ok = atOneOk && atTenOk && midOk && rangeOk && monoOk;
            Console.WriteLine($"    freq=1  → {atOne:F6}  want={cfgM.PressProbabilityAtOne:F6}  {(atOneOk?"OK":"FAIL")}");
            Console.WriteLine($"    freq=5.5→ {atMid:F6}  want={expectedMid:F6}  {(midOk?"OK":"FAIL")}");
            Console.WriteLine($"    freq=10 → {atTen:F6}  want={cfgM.PressProbabilityAtTen:F6}  {(atTenOk?"OK":"FAIL")}");
            Console.WriteLine($"    in [0,1]={rangeOk}  monotone={monoOk}");
        }
        catch (Exception ex) { t1Ok = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }
        pass &= t1Ok;
        Console.WriteLine($"  (1) {(t1Ok ? "ok" : "FAIL")}");

        // ── (2) Frequency gates pressed fraction — spy captures PressMode ─────
        Console.WriteLine("  (2) Frequency gates pressed fraction (spy-based):");
        bool t2Ok;
        try
        {
            const double TestFreq = 5.0;
            const int    N        = 5000;

            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            cfgM.AwayPressFrequency = TestFreq;
            var expectedProb = cfgM.PressProbabilityFor(TeamSide.Away);

            var safePie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = cfgA.BaseClean,
                    [EntryOutcome.Turnover]      = cfgA.BaseTurnover,
                    [EntryOutcome.DefensiveFoul] = cfgA.BaseDefensiveFoul,
                    [EntryOutcome.OffensiveFoul] = cfgA.BaseOffensiveFoul,
                    [EntryOutcome.JumpBall]      = cfgA.BaseJumpBall,
                }, cfgA.Epsilon);

            var spy  = new PressModeSpyGenerator(safePie);
            var game = new GameState(new FoulTracker(7, 10));
            SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
            var rng  = new SystemRng(cfgA.Seed);

            var resolver = new Resolver(
                spy,
                cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM,
                game,
                rng);

            var state = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);

            for (var i = 0; i < N; i++)
                resolver.RunPossession(state);

            var standardCount  = spy.Log.Count(m => m == PressMode.Standard);
            var noneCount      = spy.Log.Count(m => m == PressMode.None);
            var desperateCount = spy.Log.Count(m => m == PressMode.Desperate);
            var total          = spy.Log.Count;

            var observedFrac = (double)standardCount / total;
            // Tolerance: 5σ of binomial + 0.02 slack for re-inbound ratio bias
            var sigma5 = 5.0 * Math.Sqrt(expectedProb * (1.0 - expectedProb) / total);
            var fracOk = Math.Abs(observedFrac - expectedProb) < sigma5 + 0.02;
            var noDesperateLive = desperateCount == 0;

            t2Ok = fracOk && noDesperateLive;
            Console.WriteLine($"    freq={TestFreq}  expectedProb={expectedProb:F4}  observed={observedFrac:F4}  5σ={sigma5:F4}  fracOk={fracOk}");
            Console.WriteLine($"    logLen={total}  Standard={standardCount}  None={noneCount}  Desperate={desperateCount}  noDesperate={noDesperateLive}");
        }
        catch (Exception ex) { t2Ok = false; Console.WriteLine($"  FAIL  (2) threw: {ex.Message}"); }
        pass &= t2Ok;
        Console.WriteLine($"  (2) {(t2Ok ? "ok" : "FAIL")}");

        // ── (3) PressMode.None → exact config baseline ────────────────────────
        Console.WriteLine("  (3) PressMode.None → exact config baseline:");
        bool t3Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var g    = BuildGame(even5, even5);
            var gen  = new RollAGenerator(cfgA, cfgM, g);
            var st   = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);

            var d = gen.Generate(st, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var clOk = Math.Abs(d[EntryOutcome.CleanEntry]    - cfgA.BaseClean)         < Eps;
            var toOk = Math.Abs(d[EntryOutcome.Turnover]      - cfgA.BaseTurnover)      < Eps;
            var dfOk = Math.Abs(d[EntryOutcome.DefensiveFoul] - cfgA.BaseDefensiveFoul) < Eps;
            var ofOk = Math.Abs(d[EntryOutcome.OffensiveFoul] - cfgA.BaseOffensiveFoul) < Eps;
            var jbOk = Math.Abs(d[EntryOutcome.JumpBall]      - cfgA.BaseJumpBall)      < Eps;

            t3Ok = clOk && toOk && dfOk && ofOk && jbOk;
            Console.WriteLine($"    CleanEntry={d[EntryOutcome.CleanEntry]:F8}  want={cfgA.BaseClean:F8}  {(clOk?"OK":"FAIL")}");
            Console.WriteLine($"    Turnover  ={d[EntryOutcome.Turnover]:F8}  want={cfgA.BaseTurnover:F8}  {(toOk?"OK":"FAIL")}");
            Console.WriteLine($"    DefFoul   ={d[EntryOutcome.DefensiveFoul]:F8}  want={cfgA.BaseDefensiveFoul:F8}  {(dfOk?"OK":"FAIL")}");
            Console.WriteLine($"    OffFoul   ={d[EntryOutcome.OffensiveFoul]:F8}  want={cfgA.BaseOffensiveFoul:F8}  {(ofOk?"OK":"FAIL")}");
            Console.WriteLine($"    JumpBall  ={d[EntryOutcome.JumpBall]:F8}  want={cfgA.BaseJumpBall:F8}  {(jbOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { t3Ok = false; Console.WriteLine($"  FAIL  (3) threw: {ex.Message}"); }
        pass &= t3Ok;
        Console.WriteLine($"  (3) {(t3Ok ? "ok" : "FAIL")}");

        // ── (4) Standard pie shape ────────────────────────────────────────────
        Console.WriteLine("  (4) Standard pie shape:");
        bool t4Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            const int Gap = 20;
            var actionMass = cfgA.BaseClean + cfgA.BaseTurnover
                           + cfgA.BaseOffensiveFoul + cfgA.BaseDefensiveFoul;

            // Helper: build pie with PressMode.Standard for a given game.
            Dictionary<EntryOutcome, double> StdSplit(GameState gg)
            {
                var gen = new RollAGenerator(cfgA, cfgM, gg);
                var st  = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
                return gen.Generate(st, pressure: 0.0)
                          .Slices.ToDictionary(s => s.Outcome, s => s.Weight);
            }

            // 4a: even agg — StandardLift lifts all three arms above baseline
            var dEven   = StdSplit(BuildGame(even5, even5));
            var toAbove = dEven[EntryOutcome.Turnover]      / actionMass > cfgA.BaseTurnover      / actionMass;
            var dfAbove = dEven[EntryOutcome.DefensiveFoul] / actionMass > cfgA.BaseDefensiveFoul / actionMass;
            var ofAbove = dEven[EntryOutcome.OffensiveFoul] / actionMass > cfgA.BaseOffensiveFoul / actionMass;
            Console.WriteLine($"    (4a) StandardLift lifts: TO={toAbove}  DF={dfAbove}  OF={ofAbove}");

            // 4b: per-axis: each gap lifts TO; size produces smallest lift
            var toEven  = dEven[EntryOutcome.Turnover];
            var toSkill = StdSplit(BuildGame(
                new[] { Mk(50,bh:50-Gap), Mk(50,bh:50-Gap), Mk(50,bh:50-Gap), Mk(50,bh:50-Gap), Mk(50,bh:50-Gap) },
                new[] { Mk(50,st:50+Gap), Mk(50,st:50+Gap), Mk(50,st:50+Gap), Mk(50,st:50+Gap), Mk(50,st:50+Gap) }
            ))[EntryOutcome.Turnover];
            var toAth = StdSplit(BuildGame(
                new[] { Mk(50,ath:50-Gap), Mk(50,ath:50-Gap), Mk(50,ath:50-Gap), Mk(50,ath:50-Gap), Mk(50,ath:50-Gap) },
                new[] { Mk(50,ath:50+Gap), Mk(50,ath:50+Gap), Mk(50,ath:50+Gap), Mk(50,ath:50+Gap), Mk(50,ath:50+Gap) }
            ))[EntryOutcome.Turnover];
            var toSz = StdSplit(BuildGame(
                new[] { Mk(50,len:50-Gap), Mk(50,len:50-Gap), Mk(50,len:50-Gap), Mk(50,len:50-Gap), Mk(50,len:50-Gap) },
                new[] { Mk(50,len:50+Gap), Mk(50,len:50+Gap), Mk(50,len:50+Gap), Mk(50,len:50+Gap), Mk(50,len:50+Gap) }
            ))[EntryOutcome.Turnover];

            var skillLifts   = toSkill > toEven;
            var athLifts     = toAth   > toEven;
            var sizeLifts    = toSz    > toEven;
            var sizeSmallest = (toSz - toEven) < (toAth - toEven) && (toSz - toEven) < (toSkill - toEven);
            Console.WriteLine($"    (4b) even={toEven:F6}  +skill={toSkill-toEven:F6}lifts={skillLifts}  +ath={toAth-toEven:F6}lifts={athLifts}  +sz={toSz-toEven:F6}lifts={sizeLifts}  szSmallest={sizeSmallest}");

            // 4c: OffFoul < DefFoul at even
            var ofLtDf = dEven[EntryOutcome.OffensiveFoul] < dEven[EntryOutcome.DefensiveFoul];
            Console.WriteLine($"    (4c) OffFoul<DefFoul: {ofLtDf}  ({dEven[EntryOutcome.OffensiveFoul]:F6} < {dEven[EntryOutcome.DefensiveFoul]:F6})");

            // 4d: ceilings hold at worst matchup; CleanEntry > 0
            var offWeak  = new[] { Mk(50,bh:1,st:1,ath:1,len:1), Mk(50,bh:1,st:1,ath:1,len:1),
                                    Mk(50,bh:1,st:1,ath:1,len:1), Mk(50,bh:1,st:1,ath:1,len:1), Mk(50,bh:1,st:1,ath:1,len:1) };
            var defElite = new[] { Mk(50,bh:99,st:99,ath:99,len:99), Mk(50,bh:99,st:99,ath:99,len:99),
                                    Mk(50,bh:99,st:99,ath:99,len:99), Mk(50,bh:99,st:99,ath:99,len:99), Mk(50,bh:99,st:99,ath:99,len:99) };
            var dWorst  = StdSplit(BuildGame(offWeak, defElite));
            var toShare = dWorst[EntryOutcome.Turnover]      / actionMass;
            var dfShare = dWorst[EntryOutcome.DefensiveFoul] / actionMass;
            var ofShare = dWorst[EntryOutcome.OffensiveFoul] / actionMass;
            var capTO   = toShare <= cfgM.StandardTurnoverCeiling  + Eps;
            var capDF   = dfShare <= cfgM.StandardDefFoulCeiling   + Eps;
            var capOF   = ofShare <= cfgM.StandardOffFoulCeiling   + Eps;
            var cleanOk = dWorst[EntryOutcome.CleanEntry] > 0.0;
            Console.WriteLine($"    (4d) worst: TO={toShare:F4}<={cfgM.StandardTurnoverCeiling}={capTO}  DF={dfShare:F4}<={cfgM.StandardDefFoulCeiling}={capDF}  OF={ofShare:F4}<={cfgM.StandardOffFoulCeiling}={capOF}  cleanPos={cleanOk}");

            // 4e: five arms sum to 1; JumpBall exactly flat
            var sumEven = dEven.Values.Sum();
            var sumOk   = Math.Abs(sumEven - 1.0) < Eps;
            var jbOk    = Math.Abs(dEven[EntryOutcome.JumpBall] - cfgA.BaseJumpBall) < Eps;
            Console.WriteLine($"    (4e) sum={sumEven:F12}={sumOk}  JumpBall={dEven[EntryOutcome.JumpBall]:F10}=base={jbOk}");

            t4Ok = toAbove && dfAbove && ofAbove
                && skillLifts && athLifts && sizeLifts && sizeSmallest
                && ofLtDf
                && capTO && capDF && capOF && cleanOk
                && sumOk && jbOk;
        }
        catch (Exception ex) { t4Ok = false; Console.WriteLine($"  FAIL  (4) threw: {ex.Message}"); }
        pass &= t4Ok;
        Console.WriteLine($"  (4) {(t4Ok ? "ok" : "FAIL")}");

        // ── (5) Frontcourt=true gates Standard → exact baseline ────────────────
        Console.WriteLine("  (5) Frontcourt=true gates Standard (high-freq, worst matchup):");
        bool t5Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            cfgM.AwayPressFrequency = 10.0;

            var offWeak  = new[] { Mk(1), Mk(1), Mk(1), Mk(1), Mk(1) };
            var defElite = new[] { Mk(99), Mk(99), Mk(99), Mk(99), Mk(99) };
            var g   = BuildGame(offWeak, defElite);
            var gen = new RollAGenerator(cfgA, cfgM, g);
            var st  = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                Frontcourt: true, PressMode: PressMode.Standard);

            var d = gen.Generate(st, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var clOk = Math.Abs(d[EntryOutcome.CleanEntry]    - cfgA.BaseClean)         < Eps;
            var toOk = Math.Abs(d[EntryOutcome.Turnover]      - cfgA.BaseTurnover)      < Eps;
            var dfOk = Math.Abs(d[EntryOutcome.DefensiveFoul] - cfgA.BaseDefensiveFoul) < Eps;
            var ofOk = Math.Abs(d[EntryOutcome.OffensiveFoul] - cfgA.BaseOffensiveFoul) < Eps;
            var jbOk = Math.Abs(d[EntryOutcome.JumpBall]      - cfgA.BaseJumpBall)      < Eps;

            t5Ok = clOk && toOk && dfOk && ofOk && jbOk;
            Console.WriteLine($"    CL={clOk}  TO={toOk}  DF={dfOk}  OF={ofOk}  JB={jbOk}  (all must be true)");
        }
        catch (Exception ex) { t5Ok = false; Console.WriteLine($"  FAIL  (5) threw: {ex.Message}"); }
        pass &= t5Ok;
        Console.WriteLine($"  (5) {(t5Ok ? "ok" : "FAIL")}");

        // ── (6) SelectedSlot-blind — null vs stamped slot gives identical pie ──
        Console.WriteLine("  (6) SelectedSlot-blind (Standard: null vs stamped slot):");
        bool t6Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var g    = BuildGame(even5, even5);
            var gen  = new RollAGenerator(cfgA, cfgM, g);

            var stNull = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
            var stSlot = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard,
                SelectedSlot: g.HomeLineup.SlotAt(1));

            var dNull = gen.Generate(stNull, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);
            var dSlot = gen.Generate(stSlot, pressure: 0.0).Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var allMatch = dNull.Keys.All(k => Math.Abs(dNull[k] - dSlot[k]) < Eps);
            t6Ok = allMatch;
            Console.WriteLine($"    null slot vs stamped slot → identical pie: {(allMatch?"OK":"FAIL")}");
        }
        catch (Exception ex) { t6Ok = false; Console.WriteLine($"  FAIL  (6) threw: {ex.Message}"); }
        pass &= t6Ok;
        Console.WriteLine($"  (6) {(t6Ok ? "ok" : "FAIL")}");

        // ── (7) PressMode threads through re-inbounds ─────────────────────────
        // Route a ResumeInbound/ResolveSidelineInbound Continue directly — the spy
        // records which PressMode c.State carries. This directly verifies that the
        // stamp placed in RunPossession survives every `with` in the chain.
        Console.WriteLine("  (7) PressMode threads through ResumeInbound and ResolveSidelineInbound:");
        bool t7Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);

            var fixedPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = cfgA.BaseClean,
                    [EntryOutcome.Turnover]      = cfgA.BaseTurnover,
                    [EntryOutcome.DefensiveFoul] = cfgA.BaseDefensiveFoul,
                    [EntryOutcome.OffensiveFoul] = cfgA.BaseOffensiveFoul,
                    [EntryOutcome.JumpBall]      = cfgA.BaseJumpBall,
                }, cfgA.Epsilon);

            var spy  = new PressModeSpyGenerator(fixedPie);
            var game = new GameState(new FoulTracker(7, 10));
            SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
            var rng  = new SystemRng(42);

            var resolver = new Resolver(
                spy,
                cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM,
                game,
                rng);

            // State stamped Standard; state stamped None.
            var stStd  = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                             Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
            var stNone = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                             Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);

            // 7a: ResumeInbound with Standard — spy must record Standard on every call
            spy.Log.Clear();
            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stStd));
            var resumeStdOk = spy.Log.Count > 0 && spy.Log.All(m => m == PressMode.Standard);

            // 7b: ResumeInbound with None — spy must record None on every call
            spy.Log.Clear();
            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stNone));
            var resumeNoneOk = spy.Log.Count > 0 && spy.Log.All(m => m == PressMode.None);

            // 7c: ResolveSidelineInbound with Standard — Phase 16 clears PressMode to None
            //     before calling Generate (dead-ball re-inbound ends the press stamp).
            //     The spy must record None, not Standard.
            spy.Log.Clear();
            resolver.Route(new Continue(ContinuationKind.ResolveSidelineInbound, stStd));
            var sidelineStdOk = spy.Log.Count > 0 && spy.Log.All(m => m == PressMode.None);

            t7Ok = resumeStdOk && resumeNoneOk && sidelineStdOk;
            Console.WriteLine($"    ResumeInbound(Standard)         → all spy entries Standard: {resumeStdOk}");
            Console.WriteLine($"    ResumeInbound(None)             → all spy entries None:     {resumeNoneOk}");
            Console.WriteLine($"    ResolveSidelineInbound(Standard)→ all spy entries None (Phase 16 dead-ball clear): {sidelineStdOk}");
        }
        catch (Exception ex) { t7Ok = false; Console.WriteLine($"  FAIL  (7) threw: {ex.Message}"); }
        pass &= t7Ok;
        Console.WriteLine($"  (7) {(t7Ok ? "ok" : "FAIL")}");

        // ── (8) Desperate fail-loud — reserved, must never be produced live ────
        Console.WriteLine("  (8) Desperate fail-loud:");
        bool t8Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var g    = BuildGame(even5, even5);
            var gen  = new RollAGenerator(cfgA, cfgM, g);

            // 8a: stamping Desperate and calling Generate must throw
            var stDesp = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Desperate);
            bool threwOk;
            try
            {
                gen.Generate(stDesp, pressure: 0.0);
                threwOk = false;   // should have thrown
            }
            catch (InvalidOperationException) { threwOk = true; }
            catch { threwOk = false; }

            // 8b: the live path (low frequency → RunPossession) never produces Desperate.
            // Use the spy over N possessions and assert desperateCount == 0 (verified in (2) above,
            // re-confirmed here for clarity).
            var cfgA2 = RollAConfig.Load(configPath);
            var cfgM2 = MatchupConfig.Load(configPath);
            var safePie2 = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = cfgA2.BaseClean,
                    [EntryOutcome.Turnover]      = cfgA2.BaseTurnover,
                    [EntryOutcome.DefensiveFoul] = cfgA2.BaseDefensiveFoul,
                    [EntryOutcome.OffensiveFoul] = cfgA2.BaseOffensiveFoul,
                    [EntryOutcome.JumpBall]      = cfgA2.BaseJumpBall,
                }, cfgA2.Epsilon);
            var spy2  = new PressModeSpyGenerator(safePie2);
            var game2 = new GameState(new FoulTracker(7, 10));
            SeedMinimalRoster(game2);  // Phase 31: picker needs populated roster
            var rng2  = new SystemRng(cfgA2.Seed);
            var res2  = new Resolver(
                spy2, cfgA2,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game2),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game2),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM2, game2, rng2);
            var st2 = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                         Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound);
            for (var i = 0; i < 1000; i++) res2.RunPossession(st2);
            var noDesperateLive = spy2.Log.All(m => m != PressMode.Desperate);

            t8Ok = threwOk && noDesperateLive;
            Console.WriteLine($"    Desperate stamp → Generate throws InvalidOperationException: {threwOk}");
            Console.WriteLine($"    Live RunPossession (1000×) never produces Desperate:         {noDesperateLive}");
        }
        catch (Exception ex) { t8Ok = false; Console.WriteLine($"  FAIL  (8) threw: {ex.Message}"); }
        pass &= t8Ok;
        Console.WriteLine($"  (8) {(t8Ok ? "ok" : "FAIL")}");

        Console.WriteLine(pass ? "  Phase 15 PASSED." : "  Phase 15 FAILED.");
        return pass;
    }


    private static bool Phase16PressBreakFastBreakCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 16: Press-break fast break ---");
        var pass = true;
        const double Eps = 1e-9;

        // ── Shared helpers ─────────────────────────────────────────────────────
        // Player builder: all attributes at b, with optional rim/three tendency overrides.
        static Player Mk16(int b, int? rim = null, int? three = null)
        {
            return new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b, HelpDefense = b, OffBallDefense = b,
                RimTendency   = rim   ?? b,
                ShortTendency = b,
                MidTendency   = b,
                LongTendency  = b,
                ThreeTendency = three ?? b,
            };
        }

        GameState BuildGame16(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(7, 10));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        var even5 = new[] { Mk16(50), Mk16(50), Mk16(50), Mk16(50), Mk16(50) };

        // ── (1) Standard + IntoHalfcourtSet → Roll E sees FastBreak=true, PressMode=None ──
        Console.WriteLine("  (1) Standard press + IntoHalfcourtSet → Roll E receives FastBreak=true AND PressMode=None:");
        bool t1Ok;
        try
        {
            var cfgA = RollAConfig.Load(configPath);
            var cfgE = RollEConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);
            var game = BuildGame16(even5, even5);
            var rng  = new SystemRng(1);

            var fixedE = new Pie<SelectionOutcome>(
                new Dictionary<SelectionOutcome, double>
                {
                    [SelectionOutcome.Slot1] = cfgE.BaseSlot1,
                    [SelectionOutcome.Slot2] = cfgE.BaseSlot2,
                    [SelectionOutcome.Slot3] = cfgE.BaseSlot3,
                    [SelectionOutcome.Slot4] = cfgE.BaseSlot4,
                    [SelectionOutcome.Slot5] = cfgE.BaseSlot5,
                }, cfgE.Epsilon);
            var spyE = new RollESpyGenerator(fixedE);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                spyE,
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stStandard = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);

            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stStandard));

            var fbOk = spyE.Log.Count > 0 && spyE.Log[0].FastBreak == true;
            var pmOk = spyE.Log.Count > 0 && spyE.Log[0].Press     == PressMode.None;
            t1Ok = fbOk && pmOk;
            Console.WriteLine($"    spy called {spyE.Log.Count} time(s) on this routing");
            Console.WriteLine($"    spy.Log[0].FastBreak == true:   {fbOk}");
            Console.WriteLine($"    spy.Log[0].Press == None:       {pmOk}");
        }
        catch (Exception ex) { t1Ok = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }
        pass &= t1Ok;
        Console.WriteLine($"  (1) {(t1Ok ? "ok" : "FAIL")}");

        // ── (2) None press + IntoHalfcourtSet → Roll B fires, Roll E sees FastBreak=false ──
        Console.WriteLine("  (2) None press + IntoHalfcourtSet → Roll B fires, Roll E sees FastBreak=false:");
        bool t2Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgB  = RollBConfig.Load(configPath);
            var cfgE  = RollEConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(2);

            var fixedE = new Pie<SelectionOutcome>(
                new Dictionary<SelectionOutcome, double>
                {
                    [SelectionOutcome.Slot1] = cfgE.BaseSlot1,
                    [SelectionOutcome.Slot2] = cfgE.BaseSlot2,
                    [SelectionOutcome.Slot3] = cfgE.BaseSlot3,
                    [SelectionOutcome.Slot4] = cfgE.BaseSlot4,
                    [SelectionOutcome.Slot5] = cfgE.BaseSlot5,
                }, cfgE.Epsilon);
            var spyE = new RollESpyGenerator(fixedE);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new AlwaysProceedRollBGenerator(cfgB),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                spyE,
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stNone = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);

            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stNone));

            // AlwaysProceedRollBGenerator guarantees Roll B → Proceed → IntoPlayerSelection → spy
            var calledOk = spyE.Log.Count > 0;
            var fbOk     = calledOk && spyE.Log[0].FastBreak == false;
            var pmOk     = calledOk && spyE.Log[0].Press     == PressMode.None;
            t2Ok = calledOk && fbOk && pmOk;
            Console.WriteLine($"    Roll E spy called (confirms Roll B path taken): {calledOk}");
            Console.WriteLine($"    spy.Log[0].FastBreak == false:  {fbOk}");
            Console.WriteLine($"    spy.Log[0].Press == None:       {pmOk}");
        }
        catch (Exception ex) { t2Ok = false; Console.WriteLine($"  FAIL  (2) threw: {ex.Message}"); }
        pass &= t2Ok;
        Console.WriteLine($"  (2) {(t2Ok ? "ok" : "FAIL")}");

        // ── (3) PressMode consumed — second IntoHalfcourtSet cannot re-fire ────
        Console.WriteLine("  (3) PressMode consumed — second IntoHalfcourtSet with PressMode=None → Roll B fires:");
        bool t3Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgB  = RollBConfig.Load(configPath);
            var cfgE  = RollEConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(3);

            var fixedE = new Pie<SelectionOutcome>(
                new Dictionary<SelectionOutcome, double>
                {
                    [SelectionOutcome.Slot1] = cfgE.BaseSlot1,
                    [SelectionOutcome.Slot2] = cfgE.BaseSlot2,
                    [SelectionOutcome.Slot3] = cfgE.BaseSlot3,
                    [SelectionOutcome.Slot4] = cfgE.BaseSlot4,
                    [SelectionOutcome.Slot5] = cfgE.BaseSlot5,
                }, cfgE.Epsilon);
            var spyE = new RollESpyGenerator(fixedE);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new AlwaysProceedRollBGenerator(cfgB),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                spyE,
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            // First routing: Standard press → press-break fires. Spy sees (FastBreak=true, None).
            var stStandard = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.Standard);
            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stStandard));
            var firstFireFb = spyE.Log.Count > 0 && spyE.Log[0].FastBreak == true;
            var firstFirePm = spyE.Log.Count > 0 && spyE.Log[0].Press     == PressMode.None;

            // Second routing: consumed state (PressMode=None) → Roll B fires, not press-break.
            spyE.Log.Clear();
            var stConsumed = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, PressMode: PressMode.None);
            resolver.Route(new Continue(ContinuationKind.IntoHalfcourtSet, stConsumed));
            var secondFired    = spyE.Log.Count > 0;
            var secondFbFalse  = secondFired && spyE.Log[0].FastBreak == false;

            t3Ok = firstFireFb && firstFirePm && secondFired && secondFbFalse;
            Console.WriteLine($"    first routing:  press-break fires (FastBreak=true, Press=None): {firstFireFb && firstFirePm}");
            Console.WriteLine($"    second routing: Roll B path taken (FastBreak=false at Roll E):  {secondFired && secondFbFalse}");
        }
        catch (Exception ex) { t3Ok = false; Console.WriteLine($"  FAIL  (3) threw: {ex.Message}"); }
        pass &= t3Ok;
        Console.WriteLine($"  (3) {(t3Ok ? "ok" : "FAIL")}");

        // ── (4) ResolveSidelineInbound clears both markers ────────────────────────
        Console.WriteLine("  (4) ResolveSidelineInbound clears FastBreak and PressMode:");
        bool t4Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(4);

            var cleanPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = 1.0,
                    [EntryOutcome.Turnover]      = 0.0,
                    [EntryOutcome.DefensiveFoul] = 0.0,
                    [EntryOutcome.OffensiveFoul] = 0.0,
                    [EntryOutcome.JumpBall]      = 0.0,
                }, cfgA.Epsilon);
            var spyA = new FullStateRollASpyGenerator(cleanPie);

            var resolver = new Resolver(
                spyA, cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stBoth = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, FastBreak: true, PressMode: PressMode.Standard);

            resolver.Route(new Continue(ContinuationKind.ResolveSidelineInbound, stBoth));

            var fbOk = spyA.Log.Count > 0 && spyA.Log[0].FastBreak == false;
            var pmOk = spyA.Log.Count > 0 && spyA.Log[0].Press     == PressMode.None;
            t4Ok = fbOk && pmOk;
            Console.WriteLine($"    Roll A sees FastBreak=false: {fbOk}");
            Console.WriteLine($"    Roll A sees PressMode=None:  {pmOk}");
        }
        catch (Exception ex) { t4Ok = false; Console.WriteLine($"  FAIL  (4) threw: {ex.Message}"); }
        pass &= t4Ok;
        Console.WriteLine($"  (4) {(t4Ok ? "ok" : "FAIL")}");

        // ── (5) ResumeInbound (frontcourt) clears both markers ────────────────────
        Console.WriteLine("  (5) ResumeInbound (frontcourt) clears FastBreak and PressMode:");
        bool t5Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(5);

            var cleanPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = 1.0,
                    [EntryOutcome.Turnover]      = 0.0,
                    [EntryOutcome.DefensiveFoul] = 0.0,
                    [EntryOutcome.OffensiveFoul] = 0.0,
                    [EntryOutcome.JumpBall]      = 0.0,
                }, cfgA.Epsilon);
            var spyA = new FullStateRollASpyGenerator(cleanPie);

            var resolver = new Resolver(
                spyA, cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stFrontcourt = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, Frontcourt: true, FastBreak: true, PressMode: PressMode.None);

            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stFrontcourt));

            var fbOk = spyA.Log.Count > 0 && spyA.Log[0].FastBreak == false;
            var pmOk = spyA.Log.Count > 0 && spyA.Log[0].Press     == PressMode.None;
            t5Ok = fbOk && pmOk;
            Console.WriteLine($"    Roll A sees FastBreak=false: {fbOk}");
            Console.WriteLine($"    Roll A sees PressMode=None:  {pmOk}");
        }
        catch (Exception ex) { t5Ok = false; Console.WriteLine($"  FAIL  (5) threw: {ex.Message}"); }
        pass &= t5Ok;
        Console.WriteLine($"  (5) {(t5Ok ? "ok" : "FAIL")}");

        // ── (6) ResumeInbound (backcourt) preserves active Standard press ─────────
        Console.WriteLine("  (6) ResumeInbound (backcourt) preserves active Standard press:");
        bool t6Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(6);

            var cleanPie = new Pie<EntryOutcome>(
                new Dictionary<EntryOutcome, double>
                {
                    [EntryOutcome.CleanEntry]    = 1.0,
                    [EntryOutcome.Turnover]      = 0.0,
                    [EntryOutcome.DefensiveFoul] = 0.0,
                    [EntryOutcome.OffensiveFoul] = 0.0,
                    [EntryOutcome.JumpBall]      = 0.0,
                }, cfgA.Epsilon);
            var spyA = new FullStateRollASpyGenerator(cleanPie);

            var resolver = new Resolver(
                spyA, cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            var stBackcourt = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, Frontcourt: false, FastBreak: false, PressMode: PressMode.Standard);

            resolver.Route(new Continue(ContinuationKind.ResumeInbound, stBackcourt));

            // Press must survive — Roll A gets PressMode=Standard (can still be beaten on CleanEntry).
            var pmOk = spyA.Log.Count > 0 && spyA.Log[0].Press == PressMode.Standard;
            t6Ok = pmOk;
            Console.WriteLine($"    Roll A sees PressMode=Standard (press survived backcourt foul): {pmOk}");
        }
        catch (Exception ex) { t6Ok = false; Console.WriteLine($"  FAIL  (6) threw: {ex.Message}"); }
        pass &= t6Ok;
        Console.WriteLine($"  (6) {(t6Ok ? "ok" : "FAIL")}");

        // ── (7) Roll G with FastBreak=true → flat fast-break pie, bypasses tendencies ──
        Console.WriteLine("  (7) Roll G with FastBreak=true → flat fast-break pie regardless of shooter tendencies:");
        bool t7Ok;
        try
        {
            var cfgG = RollGConfig.Load(configPath);
            var cfgM = MatchupConfig.Load(configPath);

            var shooterA = Mk16(50, rim: 99, three: 1);
            var shooterB = Mk16(50, rim: 1,  three: 99);

            var gameA = BuildGame16(new[] { shooterA, Mk16(50), Mk16(50), Mk16(50), Mk16(50) }, even5);
            var gameB = BuildGame16(new[] { shooterB, Mk16(50), Mk16(50), Mk16(50), Mk16(50) }, even5);

            var genA = new RollGGenerator(cfgG, cfgM, gameA);
            var genB = new RollGGenerator(cfgG, cfgM, gameB);

            var slot = gameA.HomeLineup.SlotAt(1);   // Slot(Home,1) — same value type in both games

            var stBreak   = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                                Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound,
                                SelectedSlot: slot, FastBreak: true);
            var stNoBreak = stBreak with { FastBreak = false };

            var pieABreak   = genA.Generate(stBreak);
            var pieBBreak   = genB.Generate(stBreak);
            var pieANoBreak = genA.Generate(stNoBreak);
            var pieBNoBreak = genB.Generate(stNoBreak);

            double Wt(Pie<ShotLocation> p, ShotLocation loc) =>
                p.Slices.First(s => s.Outcome == loc).Weight;

            var aRimOk   = Math.Abs(Wt(pieABreak, ShotLocation.Rim)   - cfgG.FastBreakRim)   < Eps;
            var aThreeOk = Math.Abs(Wt(pieABreak, ShotLocation.Three) - cfgG.FastBreakThree) < Eps;
            var aSumOk   = Math.Abs(pieABreak.Slices.Sum(s => s.Weight) - 1.0) < Eps;
            var bRimOk   = Math.Abs(Wt(pieBBreak, ShotLocation.Rim)   - cfgG.FastBreakRim)   < Eps;
            var bThreeOk = Math.Abs(Wt(pieBBreak, ShotLocation.Three) - cfgG.FastBreakThree) < Eps;
            var bSumOk   = Math.Abs(pieBBreak.Slices.Sum(s => s.Weight) - 1.0) < Eps;
            var samePie  = pieABreak.Slices.All(s => Math.Abs(s.Weight - Wt(pieBBreak, s.Outcome)) < Eps);
            // Non-FastBreak: rim-dominant and three-dominant shooters must get different rim weights.
            var noBreakDiffer = Math.Abs(Wt(pieANoBreak, ShotLocation.Rim) - Wt(pieBNoBreak, ShotLocation.Rim)) > 0.01;

            t7Ok = aRimOk && aThreeOk && aSumOk && bRimOk && bThreeOk && bSumOk && samePie && noBreakDiffer;
            Console.WriteLine($"    ShooterA FB: rim={Wt(pieABreak,ShotLocation.Rim):F4} want={cfgG.FastBreakRim}  three={Wt(pieABreak,ShotLocation.Three):F4} want={cfgG.FastBreakThree}  sum=1:{aSumOk}");
            Console.WriteLine($"    ShooterB FB: rim={Wt(pieBBreak,ShotLocation.Rim):F4} want={cfgG.FastBreakRim}  three={Wt(pieBBreak,ShotLocation.Three):F4} want={cfgG.FastBreakThree}  sum=1:{bSumOk}");
            Console.WriteLine($"    Both shooters same fast-break pie:             {samePie}");
            Console.WriteLine($"    Non-FastBreak pies differ (tendencies active): {noBreakDiffer}");
        }
        catch (Exception ex) { t7Ok = false; Console.WriteLine($"  FAIL  (7) threw: {ex.Message}"); }
        pass &= t7Ok;
        Console.WriteLine($"  (7) {(t7Ok ? "ok" : "FAIL")}");

        // ── (8) End-to-end smoke — 1 000-possession batch, unrouted == 0 ──────────
        Console.WriteLine("  (8) End-to-end smoke (1 000 possessions, AwayPressFreq=10):");
        bool t8Ok;
        try
        {
            var cfgA  = RollAConfig.Load(configPath);
            var cfgM  = MatchupConfig.Load(configPath);
            cfgM.AwayPressFrequency = 10.0;   // ~80% press probability
            var game  = BuildGame16(even5, even5);
            var rng   = new SystemRng(cfgA.Seed);

            var resolver = new Resolver(
                new RollAGenerator(cfgA, cfgM, game), cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)), RollCConfig.Load(configPath),
                new RollDGenerator(RollDConfig.Load(configPath)),
                new RollEStubPieGenerator(RollEConfig.Load(configPath)),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFStubPieGenerator(RollFConfig.Load(configPath)),
                new RollGStubPieGenerator(RollGConfig.Load(configPath)),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgM, game, rng);

            const int N = 1_000;
            var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home,
                         Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound);

            var ended    = 0;
            var parked   = 0;
            var unrouted = 0;
            for (var i = 0; i < N; i++)
            {
                var r = resolver.RunPossession(st);
                if (r.PossessionEnded)                  ended++;
                else if (r.Destination.StartsWith("STUB:")) parked++;
                else                                    unrouted++;
            }

            t8Ok = unrouted == 0;
            Console.WriteLine($"    ended={ended:N0}  parked={parked:N0}  unrouted={unrouted} → {(t8Ok ? "ok" : "FAIL")}");
        }
        catch (Exception ex) { t8Ok = false; Console.WriteLine($"  FAIL  (8) threw: {ex.Message}"); }
        pass &= t8Ok;
        Console.WriteLine($"  (8) {(t8Ok ? "ok" : "FAIL")}");

        Console.WriteLine(pass ? "  Phase 16 PASSED." : "  Phase 16 FAILED.");
        return pass;
    }


    /// <summary>
    /// Spy implementation of <see cref="IRollAPieGenerator"/> for Phase 15 testing.
    /// Records every <see cref="PossessionState.PressMode"/> it receives (proving
    /// the Resolver stamped BEFORE calling Generate), then returns a caller-supplied
    /// fixed pie. Injected in place of <see cref="RollAGenerator"/> so the press
    /// roll can be observed directly without inferring it from possession outcomes.
    /// </summary>
    private sealed class PressModeSpyGenerator : IRollAPieGenerator
    {
        private readonly Pie<EntryOutcome> _fixedPie;
        public readonly List<PressMode> Log = new();

        public PressModeSpyGenerator(Pie<EntryOutcome> fixedPie) => _fixedPie = fixedPie;

        public Pie<EntryOutcome> Generate(PossessionState state, double pressure)
        {
            Log.Add(state.PressMode);
            return _fixedPie;
        }
    }


    /// <summary>
    /// Spy implementation of <see cref="IRollAPieGenerator"/> for Phase 16 testing.
    /// Records every (FastBreak, PressMode) pair it receives, then returns a caller-supplied
    /// fixed pie. Used to verify what state the Resolver passes to Roll A at
    /// ResumeInbound and ResolveSidelineInbound edges.
    /// </summary>
    private sealed class FullStateRollASpyGenerator : IRollAPieGenerator
    {
        private readonly Pie<EntryOutcome> _fixedPie;
        public readonly List<(bool FastBreak, PressMode Press)> Log = new();

        public FullStateRollASpyGenerator(Pie<EntryOutcome> fixedPie) => _fixedPie = fixedPie;

        public Pie<EntryOutcome> Generate(PossessionState state, double pressure)
        {
            Log.Add((state.FastBreak, state.PressMode));
            return _fixedPie;
        }
    }


    /// <summary>
    /// Spy implementation of <see cref="IRollEGenerationProvider"/> for Phase 16 testing.
    /// Records every (FastBreak, PressMode) pair it receives, then returns a caller-supplied
    /// fixed pie. Used to verify that the Resolver stamps FastBreak=true and PressMode=None
    /// on the breakState it passes to Roll E at the IntoHalfcourtSet press-break gate.
    /// Implements the full provider interface so it can be passed to the widened Resolver
    /// constructor without a cast.
    /// </summary>
    private sealed class RollESpyGenerator : IRollEGenerationProvider
    {
        private readonly Pie<SelectionOutcome> _fixedPie;
        public readonly List<(bool FastBreak, PressMode Press)> Log = new();

        public RollESpyGenerator(Pie<SelectionOutcome> fixedPie) => _fixedPie = fixedPie;

        public Pie<SelectionOutcome> Generate(PossessionState state)
        {
            Log.Add((state.FastBreak, state.PressMode));
            return _fixedPie;
        }

        // Provider method: spy always returns zero pressures (no usage modelling).
        public RollEGeneration GenerateWithPressure(PossessionState state)
        {
            var pie = Generate(state);
            var finalShares = new double[] { 0.2, 0.2, 0.2, 0.2, 0.2 };
            return new RollEGeneration(pie, finalShares, new double[5]);
        }

        // Tilt passthrough — spy returns the pie unchanged (no attention modelling in test helper).
        public Pie<SelectionOutcome> BendByAttention(
            RollEGeneration gen,
            double[] attentionShares,
            GameState game,
            MatchupConfig matchupCfg,
            PossessionState state)
            => gen.Pie;
    }


    /// <summary>
    /// Test-only Roll B generator that always returns a 100% Proceed pie.
    /// Used in Phase 16 test 2 to make the None-press halfcourt path deterministic:
    /// Roll B always Proceeds, guaranteeing Roll E is reached exactly once so the
    /// spy log length and content can be asserted without distributional variance.
    /// </summary>
    private sealed class AlwaysProceedRollBGenerator : IRollBPieGenerator
    {
        private readonly RollBConfig _cfg;
        public AlwaysProceedRollBGenerator(RollBConfig cfg) => _cfg = cfg;

        public Pie<HalfcourtOutcome> Generate(PossessionState state, double physicality)
        {
            var weights = new Dictionary<HalfcourtOutcome, double>
            {
                [HalfcourtOutcome.Proceed]          = 1.0,
                [HalfcourtOutcome.Foul]             = 0.0,
                [HalfcourtOutcome.DeadBallTurnover] = 0.0,
                [HalfcourtOutcome.JumpBall]         = 0.0,
            };
            return new Pie<HalfcourtOutcome>(weights, _cfg.Epsilon);
        }
    }

}
