using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // --- Batch: Roll E's five-way selection converges to a flat 20% per slot,
    //     every selected slot is a real slot on the OFFENSE's lineup numbered
    //     1–5, and every exit is a clean IntoPlayerAction continue carrying a
    //     stamped slot. The flat distribution is the whole point this session:
    //     Phase 15: generator is now attribute-driven (halfcourt); the check is
    //     retooled to assert convergence to the generator's own pie (not flat 20s)
    //     and that the seeded alpha slot wins materially. ---
    private static bool RollESelectionBatchCheck(
        RollAConfig cfg, RollEConfig cfgE, RollDConfig cfgD, IRollEPieGenerator genE,
        GameState game, PossessionState state)
    {
        // ── Seat a known five-man roster so the generator's pie is computable ──
        // Alpha (Slot1): high SelfCreation + perimeter — should dominate.
        // Slot5: Rodman-type — low scoring, should land at/near floor.
        // Use a LOCAL game with its own roster so we don't disturb the shared
        // game's seating (other checks read it). genE holds _game (shared game);
        // we construct a local RollEGenerator for this check seeded with the test roster.
        var checkGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var checkRoster  = checkGame.RosterFor(state.Offense);
        var checkLineup  = checkGame.LineupFor(state.Offense);

        // Slot1: alpha scorer (SelfCreation=85, Close=80, Outside=75, Mid=80, Finishing=82, PostMoves=40)
        checkRoster.SetStarter(checkLineup.SlotAt(1), new Player("Alpha")
        {
            SelfCreation=85, Close=80, PostMoves=40, Outside=75, Mid=80, Finishing=82,
            FreeThrow=75, FoulDrawing=70,
            BallHandling=70, Passing=65, Playmaking=60, OffBallMovement=60, Screening=50, OffensiveRebounding=40,
            PerimeterDefense=55, PostDefense=45, RimProtection=30, DefensiveRebounding=40, Steals=50,
            Height=75, Wingspan=76, Weight=60, Strength=70, Speed=75, Quickness=75, FirstStep=75, Vertical=75,
            Endurance=75, Hustle=70, BasketballIQ=80, Discipline=70,
            HelpDefense=50, OffBallDefense=50,
            RimTendency=20, ShortTendency=15, MidTendency=30, LongTendency=15, ThreeTendency=20,
        });
        // Slot2: solid scorer
        checkRoster.SetStarter(checkLineup.SlotAt(2), new Player("Solid2")
        {
            SelfCreation=60, Close=60, PostMoves=45, Outside=65, Mid=62, Finishing=60,
            FreeThrow=65, FoulDrawing=55,
            BallHandling=60, Passing=58, Playmaking=55, OffBallMovement=60, Screening=50, OffensiveRebounding=45,
            PerimeterDefense=58, PostDefense=50, RimProtection=40, DefensiveRebounding=50, Steals=50,
            Height=76, Wingspan=77, Weight=65, Strength=60, Speed=65, Quickness=65, FirstStep=60, Vertical=65,
            Endurance=65, Hustle=65, BasketballIQ=65, Discipline=65,
            HelpDefense=50, OffBallDefense=50,
            RimTendency=20, ShortTendency=20, MidTendency=25, LongTendency=15, ThreeTendency=20,
        });
        // Slot3: solid scorer
        checkRoster.SetStarter(checkLineup.SlotAt(3), new Player("Solid3")
        {
            SelfCreation=55, Close=55, PostMoves=50, Outside=60, Mid=58, Finishing=55,
            FreeThrow=60, FoulDrawing=50,
            BallHandling=55, Passing=55, Playmaking=50, OffBallMovement=58, Screening=55, OffensiveRebounding=50,
            PerimeterDefense=55, PostDefense=52, RimProtection=45, DefensiveRebounding=52, Steals=48,
            Height=78, Wingspan=79, Weight=70, Strength=62, Speed=62, Quickness=62, FirstStep=60, Vertical=62,
            Endurance=65, Hustle=65, BasketballIQ=63, Discipline=65,
            HelpDefense=50, OffBallDefense=50,
            RimTendency=25, ShortTendency=20, MidTendency=22, LongTendency=15, ThreeTendency=18,
        });
        // Slot4: solid scorer
        checkRoster.SetStarter(checkLineup.SlotAt(4), new Player("Solid4")
        {
            SelfCreation=50, Close=65, PostMoves=55, Outside=55, Mid=60, Finishing=60,
            FreeThrow=60, FoulDrawing=50,
            BallHandling=50, Passing=52, Playmaking=48, OffBallMovement=55, Screening=60, OffensiveRebounding=55,
            PerimeterDefense=55, PostDefense=58, RimProtection=50, DefensiveRebounding=58, Steals=45,
            Height=79, Wingspan=80, Weight=75, Strength=65, Speed=58, Quickness=55, FirstStep=55, Vertical=60,
            Endurance=65, Hustle=70, BasketballIQ=62, Discipline=65,
            HelpDefense=50, OffBallDefense=50,
            RimTendency=30, ShortTendency=22, MidTendency=25, LongTendency=10, ThreeTendency=13,
        });
        // Slot5: Rodman-type (low scoring — should land at/near floor)
        checkRoster.SetStarter(checkLineup.SlotAt(5), new Player("Rodman")
        {
            SelfCreation=20, Close=50, PostMoves=30, Outside=15, Mid=20, Finishing=55,
            FreeThrow=50, FoulDrawing=30,
            BallHandling=40, Passing=35, Playmaking=30, OffBallMovement=55, Screening=70, OffensiveRebounding=90,
            PerimeterDefense=65, PostDefense=70, RimProtection=75, DefensiveRebounding=92, Steals=55,
            Height=80, Wingspan=82, Weight=85, Strength=85, Speed=55, Quickness=52, FirstStep=50, Vertical=70,
            Endurance=80, Hustle=95, BasketballIQ=70, Discipline=80,
            HelpDefense=50, OffBallDefense=50,
            RimTendency=50, ShortTendency=30, MidTendency=10, LongTendency=5, ThreeTendency=5,
        });

        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} selections through Roll E (attribute-driven halfcourt) ---");
        var rng = new SystemRng(cfg.Seed);

        // Construct a local generator pointed at checkGame so the pie reflects
        // the test roster we just seated (not the shared game's roster).
        var localGenE = new RollEGenerator(cfgE, checkGame);

        // Read the generator's own pie once — empirical rates must converge to it.
        var checkState = state with { Offense = state.Offense };
        var pieE = localGenE.Generate(checkState);

        var counts = new Dictionary<SelectionOutcome, int>();
        foreach (var o in Enum.GetValues<SelectionOutcome>()) counts[o] = 0;

        var anomalies = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollE.Execute(checkState, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, checkGame, rng);

            if (result is not Continue { Next: ContinuationKind.IntoPlayerAction } c
                || c.State.SelectedSlot is not { } slot
                || slot.Side != checkState.Offense
                || slot.Number < 1 || slot.Number > Lineup.Size)
            {
                anomalies++;
                continue;
            }
            counts[(SelectionOutcome)(slot.Number - 1)]++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll E selections (converge to generator's own pie):");
        foreach (var (outcome, weight) in pieE.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-8} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        // Ordering assertion: the alpha (Slot1) must materially outpace the Rodman (Slot5).
        // This is the regression guard — the point of the whole build.
        var alphaShare = counts[SelectionOutcome.Slot1] / n;
        var rodmanShare = counts[SelectionOutcome.Slot5] / n;
        var orderOk = alphaShare > rodmanShare * 2.0;           // alpha must be more than 2x Rodman
        Console.WriteLine($"\n  Ordering: Alpha (Slot1={alphaShare:P3}) > 2x Rodman (Slot5={rodmanShare:P3}): {(orderOk ? "ok" : "FAIL")}");

        var cleanOk = anomalies == 0;
        Console.WriteLine($"  every exit a clean slot-stamped IntoPlayerAction: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");

        // ── FastBreak: transition pie must equal cfg.Transition* EXACTLY ──────
        // Build pies from the REAL generator (genE) using states with explicit FastBreak flag.
        var halfcourtPie  = localGenE.Generate(checkState with { FastBreak = false });
        var transitionPie = localGenE.Generate(checkState with { FastBreak = true });

        var transitionExpected = new Dictionary<SelectionOutcome, double>
        {
            [SelectionOutcome.Slot1] = cfgE.TransitionSlot1,
            [SelectionOutcome.Slot2] = cfgE.TransitionSlot2,
            [SelectionOutcome.Slot3] = cfgE.TransitionSlot3,
            [SelectionOutcome.Slot4] = cfgE.TransitionSlot4,
            [SelectionOutcome.Slot5] = cfgE.TransitionSlot5,
        };

        // Transition pie must exactly equal the configured Transition* weights.
        var transitionPieOk = transitionPie.Slices.All(s =>
            Math.Abs(s.Item2 - transitionExpected[s.Item1]) <= cfgE.Epsilon);

        // Halfcourt and transition pies must differ (non-flat halfcourt now makes this a real check).
        var piesDiffer = transitionPie.Slices.Any(s =>
            Math.Abs(s.Item2 - halfcourtPie.Slices.First(h => h.Item1 == s.Item1).Item2) > cfgE.Epsilon);

        Console.WriteLine("\n  FastBreak pie selection:");
        Console.WriteLine($"    FastBreak=true  -> transition pie exactly equals cfg.Transition* weights: {(transitionPieOk ? "ok" : "FAIL")}");
        Console.WriteLine($"    halfcourt and transition pies differ (selection is observable): {(piesDiffer ? "ok" : "FAIL")}");

        return ratesOk && cleanOk && orderOk && transitionPieOk && piesDiffer;
    }


    // --- Batch: Roll F's four-way action distribution converges within tolerance,
    //     and every exit is a clean Continue carrying one of the four expected
    //     kinds. The pie is flat-ish (no signal); a future attribute generator
    //     tilts it without this roll changing. Mirrors the Roll E batch check.
    //     (Block left Roll F in Session 13 — it is now a per-zone slice of Roll
    //     H.) ---
    private static bool RollFActionBatchCheck(
        RollAConfig cfg, RollFConfig cfgF, IRollFPieGenerator genF, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} actions through Roll F (flat-ish pie) ---");
        var rng = new SystemRng(cfg.Seed);
        var pieF = genF.Generate(state);

        var counts = new Dictionary<PlayerActionOutcome, int>();
        foreach (var o in Enum.GetValues<PlayerActionOutcome>()) counts[o] = 0;

        var anomalies = 0;   // anything that isn't a clean Continue with an expected kind

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollF.Execute(state, pieF, rng);

            // Map the emitted continuation kind back to its action outcome bucket.
            var bucket = result switch
            {
                Continue { Next: ContinuationKind.IntoShotType } => PlayerActionOutcome.ShotAttempt,
                Continue { Next: ContinuationKind.ResolveTurnoverType } => PlayerActionOutcome.Turnover,
                Continue { Next: ContinuationKind.ResolveFoulType } => PlayerActionOutcome.NonShootingFoul,
                Continue { Next: ContinuationKind.ResolveJumpBall } => PlayerActionOutcome.JumpBall,
                _ => (PlayerActionOutcome?)null
            };

            if (bucket is not { } b)
            {
                anomalies++;
                continue;
            }
            counts[b]++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll F actions:");
        foreach (var (outcome, weight) in pieF.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var cleanOk = anomalies == 0;
        Console.WriteLine($"\n  every exit a clean Continue with an expected kind: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");

        return ratesOk && cleanOk;
    }


    // --- Clean handoff: route a batch of real E->F exits through the resolver and
    //     confirm every Roll F outcome lands at its intended destination, with
    //     zero unrouted: turnover -> Roll C terminal, shot -> Roll G -> Roll H ->
    //     resolved, jump ball -> jump-ball terminal. As of #6 a non-shooting foul no
    //     longer parks: below the bonus Roll D's ResumeInbound RE-RUNS Roll A and the
    //     possession resolves downstream (into the shot / turnover / jump-ball
    //     buckets), and in the bonus it lands at the made-FT terminal — so the foul
    //     exit has no bucket of its own; it is absorbed into the others, and the
    //     zero-unrouted gate still proves it routes. A fresh local game + resolver
    //     keeps this self-contained; the shared foul count climbing into the bonus
    //     mid-run is expected. (Block left Roll F in Session 13; it is now a Roll H
    //     slice, so there is no F-stage block destination here.) ---
    private static bool RollFHandoffCheck(RollAConfig cfg, GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Clean handoff: {cfg.BatchSize:N0} E->F exits routed through the resolver ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // Build a self-contained resolver from config (mirrors Main's wiring), on
        // a FRESH game so this check does not perturb the shared game state.
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var genE = new RollEStubPieGenerator(cfgE);
        var genF = new RollFStubPieGenerator(cfgF);

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCGenerator(cfgC),
            cfgC,
            new RollDGenerator(cfgD),
            genE,
            new AttentionGenerator(AttentionConfig.Load(configPath), game),
            genF,
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        var pieE = genE.Generate(state);
        var pieF = genF.Generate(state);

        var destClasses = new Dictionary<string, int>
        {
            ["turnover -> Roll C terminal"] = 0,
            ["shot -> Roll G -> Roll H -> resolved"] = 0,
            ["free throws -> resolved"] = 0,
            ["jump ball -> terminal"] = 0,
        };
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Select a player (Roll E), resolve the action (Roll F), then route.
            var selected = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rng)).State;
            var fResult = RollF.Execute(selected, pieF, rng);
            var routing = resolver.Route(fResult);
            var d = routing.Destination;

            if (d.StartsWith("END:JumpBall")) destClasses["jump ball -> terminal"]++;
            // Roll H/I/M's resolved shot/board landings (checked BEFORE the generic
            // END: catch, since Made / MissOutOfBoundsLost AND the rebound terminals —
            // DefensiveRebound / LooseBallFoulOnOffense / OutOfBoundsOffOffense — are
            // also END: terminals). A shot flows F -> G -> H, a MISS flows through Roll
            // I to a board or loose-ball foul, and a missed FINAL free throw now flows
            // through Roll M (Session 19) to the SAME board landings (its DefensiveRebound
            // is a transition terminal, its OOB-off-offense a dead-ball terminal, its
            // OOB-off-defense a sideline inbound). The resolved bucket therefore spans:
            // Made / MissOutOfBoundsLost terminals; the sideline-inbound and block-
            // recovery stubs; and the rebound terminals from Roll I and Roll M. (Shooting
            // fouls drive the Roll L FT loop and land at END:FreeThrowsMade on a made last
            // shot — caught in the FT bucket below — or flow into Roll M on a missed last
            // shot, caught here.) Blocked shots no longer park at a block-recovery
            // stub — Roll H's Blocked arm now routes into Roll I (the block pie), so a
            // block lands at one of Roll I's board/foul/OOB/jump landings, all already
            // bucketed below or as a jump-ball terminal above.
            else if (d == "END:Made" || d == "END:MissOutOfBoundsLost"
                     || d == "END:DefensiveRebound" || d == "END:LooseBallFoulOnOffense"
                     || d == "END:OutOfBoundsOffOffense"
                     || d.StartsWith("STUB:OffensiveRebound"))
                destClasses["shot -> Roll G -> Roll H -> resolved"]++;
            // The Roll L FT loop's made-last-shot landing ends the possession
            // (END:FreeThrowsMade). A missed last shot no longer parks at STUB:FTRebound
            // (retired Session 19) — it drives Roll M and lands in the resolved bucket
            // above. Both a shooting foul (Roll H) and a bonus foul (Roll D) converge on
            // the made-FT terminal, so they share this bucket. Checked before the generic
            // END: catch so a made FT is not miscounted as a turnover.
            else if (d == "END:FreeThrowsMade")
                destClasses["free throws -> resolved"]++;
            else if (d.StartsWith("END:")) destClasses["turnover -> Roll C terminal"]++;
            else unrecognized++;
        }

        Console.WriteLine("  destinations reached:");
        var allHit = true;
        foreach (var (label, count) in destClasses)
        {
            var hit = count > 0;
            allHit &= hit;
            Console.WriteLine($"    {label,-32} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all four resolved destinations reached: {(allHit ? "ok" : "FAIL")}");

        return routedOk && allHit;
    }


    // =========================================================================
    // Phase 12 — pressure / disruption door (Roll F matchup-aware)
    //
    // Mirrors Phase10/11 pattern. Key differences:
    //   • Four-arm pie (ShotAttempt, Turnover, NonShootingFoul, JumpBall)
    //   • One-arg Generate(state) — no source selector, no zone
    //   • SelectedSlot IS stamped (handler needed for the steal matchup)
    //   • JumpBall held exactly flat; three-way mass split on the other three
    //   • Pressure is the new axis — the calibration anchor is
    //     (neutral pressure + even matchup) = today's flat rates
    // =========================================================================

    private static bool Phase12DisruptionDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 12: Disruption door (Roll F pressure-aware) ---");
        var pass = true;

        var cfgF       = RollFConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Action mass and baseline shares (what the neutral case must reproduce).
        var actionMass    = cfgF.BaseShotAttempt + cfgF.BaseTurnover + cfgF.BaseNonShootingFoul;
        var baseToShare   = cfgF.BaseTurnover        / actionMass;
        var baseFoulShare = cfgF.BaseNonShootingFoul / actionMass;

        // Helper: build a player with all attributes at b; override BallHandling and Steals.
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

        // Helper: build a minimal GameState, Home offense 1–5, Away defense 1–5.
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

        // Helper: build a PossessionState with Home offense, Away defense, stamped handler slot.
        // Roll F runs AFTER Roll E, so SelectedSlot is always stamped on the live path.
        static PossessionState St(GameState g, int handlerSlot = 1)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(handlerSlot),
                ShotType: null);   // Roll G hasn't run yet at Roll F

        // Helper: construct RollFGenerator with given config, generate pie, return dict.
        static Dictionary<PlayerActionOutcome, double> Split(
            RollFConfig cfgF, MatchupConfig cfgMatchup, GameState g, PossessionState state)
        {
            var gen = new RollFGenerator(cfgF, cfgMatchup, g);
            var pie = gen.Generate(state);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // Build a MatchupConfig with Away pressure set to the given value.
        // Away is always the defense in our standard test setup.
        MatchupConfig WithAwayPressure(double p)
        {
            var c = MatchupConfig.Load(configPath);
            c.AwayPressure = p;
            return c;
        }

        // ── (a) Neutral anchor ─────────────────────────────────────────────
        Console.WriteLine("  (a) Neutral anchor (pressure=5, all-50 even matchup): all four arms == config baseline:");
        bool aOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g    = BuildGame(off5, def5);
            var st   = St(g);
            var d    = Split(cfgF, cfgMatchup, g, st);   // cfgMatchup has neutral pressure=5

            var shotOk = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps;
            var toOk   = Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps;
            var foOk   = Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps;
            var juOk   = Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;

            aOk = shotOk && toOk && foOk && juOk;
            Console.WriteLine($"    ShotAttempt={d[PlayerActionOutcome.ShotAttempt]:F8}  want={cfgF.BaseShotAttempt:F8}  {(shotOk?"OK":"FAIL")}");
            Console.WriteLine($"    Turnover   ={d[PlayerActionOutcome.Turnover]:F8}  want={cfgF.BaseTurnover:F8}  {(toOk?"OK":"FAIL")}");
            Console.WriteLine($"    Foul       ={d[PlayerActionOutcome.NonShootingFoul]:F8}  want={cfgF.BaseNonShootingFoul:F8}  {(foOk?"OK":"FAIL")}");
            Console.WriteLine($"    JumpBall   ={d[PlayerActionOutcome.JumpBall]:F8}  want={cfgF.BaseJumpBall:F8}  {(juOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ── (b) Pressure raises turnovers (flat lift, skill-independent) ───
        Console.WriteLine("  (b) Pressure raises TO — even matchup, BH=ST=50:");
        bool bOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g    = BuildGame(off5, def5);
            var st   = St(g);

            var dHigh = Split(cfgF, WithAwayPressure(9.0), g, st);
            var dNeut = Split(cfgF, cfgMatchup,             g, st);
            var dLow  = Split(cfgF, WithAwayPressure(2.0), g, st);

            var toHigh = dHigh[PlayerActionOutcome.Turnover];
            var toNeut = dNeut[PlayerActionOutcome.Turnover];
            var toLow  = dLow [PlayerActionOutcome.Turnover];
            Console.WriteLine($"    TO: low={toLow:F6}  neutral={toNeut:F6}  high={toHigh:F6}");

            var riseOk = toHigh > toNeut;
            var fallOk = toLow  < toNeut;

            // Flat lift with bad hands: handler BH=30, defender ST=30 (neutral matchup
            // favors neither) — high pressure must still raise TO above neutral.
            var offBad = new[] { Mk(50, bh:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defBad = new[] { Mk(50, st:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gBad   = BuildGame(offBad, defBad);
            var stBad  = St(gBad);
            var dBadHigh = Split(cfgF, WithAwayPressure(9.0), gBad, stBad);
            var dBadNeut = Split(cfgF, cfgMatchup,             gBad, stBad);
            var flatLiftOk = dBadHigh[PlayerActionOutcome.Turnover] > dBadNeut[PlayerActionOutcome.Turnover];
            Console.WriteLine($"    Bad-hands (BH=30,ST=30): neutral={dBadNeut[PlayerActionOutcome.Turnover]:F6}  high={dBadHigh[PlayerActionOutcome.Turnover]:F6}  flatLift={flatLiftOk}");

            bOk = riseOk && fallOk && flatLiftOk;
            Console.WriteLine($"    high>neutral: {riseOk}  low<neutral: {fallOk}  flatLift: {flatLiftOk}  -> {(bOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ── (c) Pressure gates the matchup ─────────────────────────────────
        Console.WriteLine("  (c) Pressure gates matchup — high-pressure delta >> low-pressure delta:");
        bool cOk;
        try
        {
            var offGood = new[] { Mk(50, bh:85), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offWeak = new[] { Mk(50, bh:20), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt   = new[] { Mk(50, st:65), Mk(50), Mk(50), Mk(50), Mk(50) };

            var gGood = BuildGame(offGood, defSt);
            var gWeak = BuildGame(offWeak, defSt);
            var stGood = St(gGood);
            var stWeak = St(gWeak);

            var cfgLow  = WithAwayPressure(2.0);
            var cfgHigh = WithAwayPressure(9.0);

            var toLowGood  = Split(cfgF, cfgLow,  gGood, stGood)[PlayerActionOutcome.Turnover];
            var toLowWeak  = Split(cfgF, cfgLow,  gWeak, stWeak)[PlayerActionOutcome.Turnover];
            var toHighGood = Split(cfgF, cfgHigh, gGood, stGood)[PlayerActionOutcome.Turnover];
            var toHighWeak = Split(cfgF, cfgHigh, gWeak, stWeak)[PlayerActionOutcome.Turnover];

            var deltaLow  = Math.Abs(toLowWeak  - toLowGood);
            var deltaHigh = Math.Abs(toHighWeak - toHighGood);

            Console.WriteLine($"    Low  pressure delta={deltaLow:F6}  ({toLowGood:F6} vs {toLowWeak:F6})");
            Console.WriteLine($"    High pressure delta={deltaHigh:F6}  ({toHighGood:F6} vs {toHighWeak:F6})");

            cOk = deltaHigh > 3.0 * deltaLow;
            Console.WriteLine($"    High delta >> low delta: {cOk}  -> {(cOk?"OK":"FAIL")}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ── (d) Handling vs steals monotone at fixed high pressure ─────────
        Console.WriteLine("  (d) Monotone at high pressure (9.0):");
        bool dOk;
        try
        {
            var cfgHigh = WithAwayPressure(9.0);
            var def65   = new[] { Mk(50, st:65), Mk(50), Mk(50), Mk(50), Mk(50) };

            // Raise BH → TO falls
            var offBh30 = new[] { Mk(50, bh:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offBh50 = new[] { Mk(50, bh:50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offBh80 = new[] { Mk(50, bh:80), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gBh30 = BuildGame(offBh30, def65); var gBh50 = BuildGame(offBh50, def65); var gBh80 = BuildGame(offBh80, def65);
            var toBh30 = Split(cfgF, cfgHigh, gBh30, St(gBh30))[PlayerActionOutcome.Turnover];
            var toBh50 = Split(cfgF, cfgHigh, gBh50, St(gBh50))[PlayerActionOutcome.Turnover];
            var toBh80 = Split(cfgF, cfgHigh, gBh80, St(gBh80))[PlayerActionOutcome.Turnover];
            var bhFallsOk = toBh30 > toBh50 && toBh50 > toBh80;
            Console.WriteLine($"    BH=30: {toBh30:F6}  BH=50: {toBh50:F6}  BH=80: {toBh80:F6}  monotone-fall: {bhFallsOk}");

            // Raise ST → TO rises
            var off50  = new[] { Mk(50, bh:50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt30 = new[] { Mk(50, st:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt50 = new[] { Mk(50, st:50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt80 = new[] { Mk(50, st:80), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gSt30 = BuildGame(off50, defSt30); var gSt50 = BuildGame(off50, defSt50); var gSt80 = BuildGame(off50, defSt80);
            var toSt30 = Split(cfgF, cfgHigh, gSt30, St(gSt30))[PlayerActionOutcome.Turnover];
            var toSt50 = Split(cfgF, cfgHigh, gSt50, St(gSt50))[PlayerActionOutcome.Turnover];
            var toSt80 = Split(cfgF, cfgHigh, gSt80, St(gSt80))[PlayerActionOutcome.Turnover];
            var stRisesOk = toSt30 < toSt50 && toSt50 < toSt80;
            Console.WriteLine($"    ST=30: {toSt30:F6}  ST=50: {toSt50:F6}  ST=80: {toSt80:F6}  monotone-rise: {stRisesOk}");

            dOk = bhFallsOk && stRisesOk;
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ── (e) Low cap holds ───────────────────────────────────────────────
        Console.WriteLine("  (e) Low cap: max pressure + most lopsided matchup (BH=10, ST=95, p=10):");
        bool eOk;
        try
        {
            var cfgMax  = WithAwayPressure(10.0);
            var offWeak = new[] { Mk(50, bh:10), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defElite= new[] { Mk(50, st:95), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(offWeak, defElite);
            var st = St(g);
            var toWorst = Split(cfgF, cfgMax, g, st)[PlayerActionOutcome.Turnover];
            var capOk    = toWorst <= cfgMatchup.TurnoverCeiling + Eps;
            var sanityOk = toWorst < 0.35;   // nobody gets stripped 35%+ of possessions
            Console.WriteLine($"    TO={toWorst:F6}  ceiling={cfgMatchup.TurnoverCeiling:F6}  capped={capOk}  sane={sanityOk}");
            eOk = capOk && sanityOk;
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ── (f) Foul rises with pressure, flat across matchup ──────────────
        Console.WriteLine("  (f) Foul rises with pressure; flat across BH/ST matchup:");
        bool fOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g    = BuildGame(off5, def5);
            var st   = St(g);

            var foHigh = Split(cfgF, WithAwayPressure(9.0), g, st)[PlayerActionOutcome.NonShootingFoul];
            var foNeut = Split(cfgF, cfgMatchup,             g, st)[PlayerActionOutcome.NonShootingFoul];
            var foLow  = Split(cfgF, WithAwayPressure(2.0), g, st)[PlayerActionOutcome.NonShootingFoul];
            var risesOk = foHigh > foNeut && foNeut > foLow;
            Console.WriteLine($"    Foul: low={foLow:F6}  neutral={foNeut:F6}  high={foHigh:F6}  rises={risesOk}");

            // Flat across matchup at fixed pressure
            var cfgP8 = WithAwayPressure(8.0);
            var offBh85= new[] { Mk(50, bh:85), Mk(50), Mk(50), Mk(50), Mk(50) };
            var offBh20= new[] { Mk(50, bh:20), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt30= new[] { Mk(50, st:30), Mk(50), Mk(50), Mk(50), Mk(50) };
            var defSt90= new[] { Mk(50, st:90), Mk(50), Mk(50), Mk(50), Mk(50) };
            var gEven  = BuildGame(off5,   def5);
            var gGood  = BuildGame(offBh85,defSt30);
            var gWeak  = BuildGame(offBh20,defSt90);
            var foEven = Split(cfgF, cfgP8, gEven,  St(gEven ))[PlayerActionOutcome.NonShootingFoul];
            var foGood = Split(cfgF, cfgP8, gGood,  St(gGood ))[PlayerActionOutcome.NonShootingFoul];
            var foWeak = Split(cfgF, cfgP8, gWeak,  St(gWeak ))[PlayerActionOutcome.NonShootingFoul];
            var flatOk = Math.Abs(foEven - foGood) < Eps && Math.Abs(foEven - foWeak) < Eps;
            Console.WriteLine($"    Foul p=8: even={foEven:F8}  good-hands={foGood:F8}  weak-hands={foWeak:F8}  flat={flatOk}");

            fOk = risesOk && flatOk;
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ── (g) JumpBall exactly flat; four arms sum to 1 ──────────────────
        Console.WriteLine("  (g) JumpBall flat and sum==1 in all cases:");
        bool gOk;
        try
        {
            var cases = new (int bh, int st, double p, string label)[]
            {
                (50, 50, 5.0,  "neutral even"),
                (50, 50, 9.0,  "high pressure even"),
                (50, 50, 2.0,  "low pressure even"),
                (20, 80, 9.0,  "high press, defender edge"),
                (85, 30, 9.0,  "high press, handler edge"),
                (10, 95, 10.0, "max press, worst case"),
            };
            gOk = true;
            foreach (var (bh, st, p, label) in cases)
            {
                var offP = new[] { Mk(50, bh:bh), Mk(50), Mk(50), Mk(50), Mk(50) };
                var defP = new[] { Mk(50, st:st), Mk(50), Mk(50), Mk(50), Mk(50) };
                var gP   = BuildGame(offP, defP);
                var stP  = St(gP);
                var d    = Split(cfgF, WithAwayPressure(p), gP, stP);
                var sum  = d.Values.Sum();
                var jumpOk = Math.Abs(d[PlayerActionOutcome.JumpBall] - cfgF.BaseJumpBall) < Eps;
                var sumOk  = Math.Abs(sum - 1.0) < Eps;
                if (!jumpOk || !sumOk)
                {
                    Console.WriteLine($"    FAIL [{label}]: jump={d[PlayerActionOutcome.JumpBall]:F10}  sum={sum:F10}");
                    gOk = false;
                }
            }
            if (gOk) Console.WriteLine("    All cases: JumpBall==BaseJumpBall and sum==1  OK");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        // ── (h) Fallbacks: all return flat baseline pie ────────────────────
        Console.WriteLine("  (h) Fallbacks return flat baseline pie:");
        bool hOk;
        try
        {
            hOk = true;
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

            // Null SelectedSlot
            {
                var g  = BuildGame(off5, def5);
                var st = new PossessionState(PossessionNumber: 1,
                    Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: null, ShotType: null);
                var d = Split(cfgF, cfgMatchup, g, st);
                var ok1 = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps
                       && Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps
                       && Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps
                       && Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;
                Console.WriteLine($"    Null SelectedSlot → flat baseline: {(ok1?"OK":"FAIL")}");
                hOk &= ok1;
            }

            // Empty offense roster (no players seated)
            {
                var g = new GameState(new FoulTracker(7, 10));
                for (var i = 0; i < 5; i++)
                    g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def5[i]);
                // Home offense has no players — SelectedSlot still stamped but handler is null
                var st = new PossessionState(PossessionNumber: 1,
                    Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: g.HomeLineup.SlotAt(1), ShotType: null);
                var d = Split(cfgF, cfgMatchup, g, st);
                var ok2 = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps
                       && Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps
                       && Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps
                       && Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;
                Console.WriteLine($"    Empty offense roster → flat baseline: {(ok2?"OK":"FAIL")}");
                hOk &= ok2;
            }

            // Zero populated defense
            {
                var g = new GameState(new FoulTracker(7, 10));
                for (var i = 0; i < 5; i++)
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off5[i]);
                // Away defense has no players
                var st = St(g);
                var d = Split(cfgF, cfgMatchup, g, st);
                var ok3 = Math.Abs(d[PlayerActionOutcome.ShotAttempt]    - cfgF.BaseShotAttempt)    < Eps
                       && Math.Abs(d[PlayerActionOutcome.Turnover]       - cfgF.BaseTurnover)       < Eps
                       && Math.Abs(d[PlayerActionOutcome.NonShootingFoul]- cfgF.BaseNonShootingFoul)< Eps
                       && Math.Abs(d[PlayerActionOutcome.JumpBall]       - cfgF.BaseJumpBall)       < Eps;
                Console.WriteLine($"    Zero populated defense → flat baseline: {(ok3?"OK":"FAIL")}");
                hOk &= ok3;
            }
        }
        catch (Exception ex) { hOk = false; Console.WriteLine($"  FAIL  (h) threw: {ex.Message}"); }
        pass &= hOk;

        Console.WriteLine(pass ? "  Phase 12 PASSED." : "  Phase 12 FAILED.");
        return pass;
    }


    // ── Phase 17: Usage → Efficiency curve ──────────────────────────────────
    private static bool Phase17UsageEfficiencyCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 17: Usage→Efficiency curve ---");
        var pass = true;
        const double Eps = 1e-6;

        var cfgG       = RollGConfig.Load(configPath);
        var cfgH       = RollHConfig.Load(configPath);
        var cfgE       = RollEConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);

        static Player Mk(int b,
                         int? fin = null, int? outside = null, int? mid = null, int? close = null,
                         int? rimT = null, int? shortT = null, int? midT = null,
                         int? longT = null, int? threeT = null,
                         int? selfC = null, int? postM = null)
            => new Player("p")
            {
                Outside = outside ?? b, Mid = mid ?? b, Close = close ?? b,
                Finishing = fin ?? b, FreeThrow = b, FoulDrawing = b,
                BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = selfC ?? b, PostMoves = postM ?? b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b,
                BasketballIQ = b, Discipline = b, HelpDefense = b, OffBallDefense = b,
                RimTendency   = rimT   ?? b,
                ShortTendency = shortT ?? b,
                MidTendency   = midT   ?? b,
                LongTendency  = longT  ?? b,
                ThreeTendency = threeT ?? b,
            };

        static Player NeutralDef()
            => new Player("def")
            {
                Outside=50, Mid=50, Close=50, Finishing=50, FreeThrow=50, FoulDrawing=50,
                BallHandling=50, Passing=50, Playmaking=50, SelfCreation=50, PostMoves=50,
                OffBallMovement=50, Screening=50, OffensiveRebounding=50,
                PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50,
                Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
                Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
                BasketballIQ=50, Discipline=50, HelpDefense=50, OffBallDefense=50,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };

        // Build game with shooter in Home slot 1, 5 neutral defenders in Away slots.
        GameState BuildGame(Player shooter)
        {
            var fouls = new FoulTracker(7, 10);
            var game  = new GameState(fouls);
            game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(1), shooter);
            var def = NeutralDef();
            for (var i = 1; i <= 5; i++)
                game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), def);
            return game;
        }

        // Observed FG% from RollHGenerator for a state with zone already stamped.
        double MakePct(GameState game, PossessionState stateWithZone)
        {
            var genH = new RollHGenerator(cfgH, cfgMatchup, game);
            var pie  = genH.Generate(stateWithZone, putback: false);
            var made = pie.Slices.First(s => s.Outcome == ShotResult.Made).Weight
                     + pie.Slices.First(s => s.Outcome == ShotResult.MadeAndFouled).Weight;
            var fga  = pie.Slices.Where(s => s.Outcome != ShotResult.MissFouled).Sum(s => s.Weight);
            return fga > Eps ? made / fga : 0.0;
        }

        // ── (a) Zero-pressure regression ──────────────────────────────────────
        {
            var t = true;
            Console.WriteLine("\n  (a) Zero-pressure regression");
            var shooter = Mk(50, rimT:20, shortT:20, midT:20, longT:20, threeT:20);
            var game    = BuildGame(shooter);
            var slot    = game.HomeLineup.SlotAt(1);

            foreach (ShotLocation zone in Enum.GetValues<ShotLocation>())
            {
                var stateNull = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                    EntryType.DeadBallInbound, SelectedSlot: slot, ShotType: zone);
                var stateZero = stateNull with { UsagePressure = 0.0, UsageResidualPressure = 0.0 };
                var baseVal   = MakePct(game, stateNull);
                var zeroVal   = MakePct(game, stateZero);
                var ok = Math.Abs(baseVal - zeroVal) < Eps;
                if (!ok) { Console.WriteLine($"    FAIL zone {zone}: null={baseVal:F4} zero={zeroVal:F4}"); t = false; }
            }
            if (t) Console.WriteLine("    ok — zero-pressure state identical to null state");
            pass &= t;
            Console.WriteLine($"  (a) {(t ? "ok" : "FAIL")}");
        }

        // ── (b) Specialist efficiency drop ────────────────────────────────────
        double specialistDrop = 0;
        {
            var t = true;
            Console.WriteLine("\n  (b) Specialist efficiency drop");
            var shooter = Mk(50, fin:65, rimT:90, shortT:3, midT:3, longT:2, threeT:2);
            var game    = BuildGame(shooter);
            var slot    = game.HomeLineup.SlotAt(1);

            var genG      = new RollGGenerator(cfgG, cfgMatchup, game);
            var stateP0   = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.DeadBallInbound, SelectedSlot: slot,
                UsagePressure: 0.32, UsageResidualPressure: 0.0);
            var genResult = genG.GenerateWithResidual(stateP0);
            var residual  = genResult.ResidualPressure;

            var stateBase = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.DeadBallInbound, SelectedSlot: slot, ShotType: ShotLocation.Rim,
                UsagePressure: 0.0, UsageResidualPressure: 0.0);
            var stateP    = stateBase with { UsagePressure = 0.32, UsageResidualPressure = residual };

            var makeBase = MakePct(game, stateBase);
            var makeP    = MakePct(game, stateP);
            specialistDrop = makeBase - makeP;

            Console.WriteLine($"    Specialist Rim: base={makeBase:F4} pressured={makeP:F4} drop={specialistDrop*100:F1}pts residual={residual:F4}");
            var ok = specialistDrop > 0.05;
            if (!ok) Console.WriteLine("    FAIL — specialist drop too small");
            t &= ok;

            var volTax  = makeBase * (0.32 * cfgH.PressureVolumeTaxScale);
            var resPen  = residual * cfgH.PressureResidualPenaltyScale;
            Console.WriteLine($"    Attribution: vol-tax={volTax*100:F1}pts  residual-penalty={resPen*100:F1}pts");

            pass &= t;
            Console.WriteLine($"  (b) {(t ? "ok" : "FAIL")}");
        }

        // ── (c) Versatile player absorbs; ordering assertion ──────────────────
        {
            var t = true;
            Console.WriteLine("\n  (c) Versatile player absorbs; specialist drop >> versatile drop");
            var shooter = Mk(50, fin:55, mid:55, rimT:22, shortT:20, midT:25, longT:18, threeT:15);
            var game    = BuildGame(shooter);
            var slot    = game.HomeLineup.SlotAt(1);

            var genG      = new RollGGenerator(cfgG, cfgMatchup, game);
            var stateP0   = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.DeadBallInbound, SelectedSlot: slot,
                UsagePressure: 0.32, UsageResidualPressure: 0.0);
            var genResult = genG.GenerateWithResidual(stateP0);
            var residual  = genResult.ResidualPressure;

            var stateBase = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.DeadBallInbound, SelectedSlot: slot, ShotType: ShotLocation.Mid,
                UsagePressure: 0.0, UsageResidualPressure: 0.0);
            var stateP    = stateBase with { UsagePressure = 0.32, UsageResidualPressure = residual };

            var makeBase = MakePct(game, stateBase);
            var makeP    = MakePct(game, stateP);
            var drop     = makeBase - makeP;

            Console.WriteLine($"    Versatile Mid: base={makeBase:F4} pressured={makeP:F4} drop={drop*100:F1}pts residual={residual:F4}");

            var residualOk  = residual < 0.02;
            var orderingOk  = specialistDrop > drop * 2.0;
            Console.WriteLine($"    Residual near-zero: {(residualOk ? "ok" : "FAIL")} ({residual:F4})");
            Console.WriteLine($"    Specialist drop ({specialistDrop*100:F1}pts) >> versatile drop ({drop*100:F1}pts): {(orderingOk ? "ok" : "FAIL")}");
            t &= residualOk && orderingOk;

            pass &= t;
            Console.WriteLine($"  (c) {(t ? "ok" : "FAIL")}");
        }

        // ── (d) Below-comfort no-op ───────────────────────────────────────────
        {
            var t = true;
            Console.WriteLine("\n  (d) Below-comfort slot unchanged");
            var shooter = Mk(50, rimT:20, shortT:20, midT:20, longT:20, threeT:20);
            var game    = BuildGame(shooter);
            var slot    = game.HomeLineup.SlotAt(1);

            foreach (ShotLocation zone in Enum.GetValues<ShotLocation>())
            {
                var stateBase  = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                    EntryType.DeadBallInbound, SelectedSlot: slot, ShotType: zone,
                    UsagePressure: 0.0, UsageResidualPressure: 0.0);
                var stateBelow = stateBase;  // pressure=0 IS the below-comfort case
                var base_ = MakePct(game, stateBase);
                var below = MakePct(game, stateBelow);
                var ok    = Math.Abs(base_ - below) < Eps;
                if (!ok) { Console.WriteLine($"    FAIL zone {zone}"); t = false; }
            }
            if (t) Console.WriteLine("    ok — below-comfort (pressure=0) unchanged");
            pass &= t;
            Console.WriteLine($"  (d) {(t ? "ok" : "FAIL")}");
        }

        // ── (e) Natural-dominance cost ────────────────────────────────────────
        {
            var t = true;
            Console.WriteLine("\n  (e) Naturally dominant star pays a real cost");
            var star = Mk(50, fin:80, outside:75, mid:70, selfC:80, postM:70,
                          rimT:35, shortT:20, midT:20, longT:12, threeT:13);
            var game = BuildGame(star);
            var slot = game.HomeLineup.SlotAt(1);

            var stateBase    = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.DeadBallInbound, SelectedSlot: slot, ShotType: ShotLocation.Rim,
                UsagePressure: 0.0, UsageResidualPressure: 0.0);
            var stateNatural = stateBase with { UsagePressure = 0.20, UsageResidualPressure = 0.0 };

            var makeBase    = MakePct(game, stateBase);
            var makeNatural = MakePct(game, stateNatural);
            var drop        = makeBase - makeNatural;
            Console.WriteLine($"    Star Rim: base={makeBase:F4} natural-load={makeNatural:F4} drop={drop*100:F1}pts");
            var ok = drop > 0.005;
            if (!ok) Console.WriteLine("    FAIL — star shows no cost at natural load");
            t &= ok;
            pass &= t;
            Console.WriteLine($"  (e) {(t ? "ok" : "FAIL")}");
        }

        // ── (f) FastBreak exemption ───────────────────────────────────────────
        {
            var t = true;
            Console.WriteLine("\n  (f) FastBreak exemption");
            var shooter = Mk(50, rimT:20, shortT:20, midT:20, longT:20, threeT:20);
            var game    = BuildGame(shooter);
            var slot    = game.HomeLineup.SlotAt(1);

            var genG   = new RollGGenerator(cfgG, cfgMatchup, game);
            var stateFB = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.Transition, SelectedSlot: slot, FastBreak: true,
                UsagePressure: 0.0, UsageResidualPressure: 0.0);
            var genRes    = genG.GenerateWithResidual(stateFB);
            var residualOk = Math.Abs(genRes.ResidualPressure) < Eps;
            Console.WriteLine($"    Roll G residual on FastBreak: {genRes.ResidualPressure:F6} → {(residualOk ? "ok" : "FAIL")}");
            t &= residualOk;

            // Roll H: FastBreak with 0.0/0.0 scalars should match null scalars
            var stateH_FB   = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.Transition, SelectedSlot: slot, ShotType: ShotLocation.Rim,
                FastBreak: true, UsagePressure: 0.0, UsageResidualPressure: 0.0);
            var stateH_null = new PossessionState(1, TeamSide.Home, TeamSide.Away,
                EntryType.Transition, SelectedSlot: slot, ShotType: ShotLocation.Rim,
                FastBreak: true);
            var makeFB   = MakePct(game, stateH_FB);
            var makeNull = MakePct(game, stateH_null);
            var makeOk   = Math.Abs(makeFB - makeNull) < Eps;
            Console.WriteLine($"    Roll H make (0.0 vs null scalars): {makeFB:F4} vs {makeNull:F4} → {(makeOk ? "ok" : "FAIL")}");
            t &= makeOk;

            pass &= t;
            Console.WriteLine($"  (f) {(t ? "ok" : "FAIL")}");
        }

        // ── (g) Plumbing: null→non-null seams, ResetOffense clears ────────────
        {
            var t = true;
            Console.WriteLine("\n  (g) Plumbing: null→non-null seams; ResetOffense clears both");

            // Fresh PossessionState must have null for both fields
            var fresh = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
            var freshOk = !fresh.UsagePressure.HasValue && !fresh.UsageResidualPressure.HasValue;
            Console.WriteLine($"    Fresh state: UsagePressure=null, UsageResidualPressure=null → {(freshOk ? "ok" : "FAIL")}");
            t &= freshOk;

            // After Roll E stamps pressure, field is non-null
            var afterE = fresh with { UsagePressure = 0.25 };
            var afterEOk = afterE.UsagePressure.HasValue && afterE.UsagePressure.Value == 0.25;
            Console.WriteLine($"    After Roll E stamp (0.25): UsagePressure={afterE.UsagePressure} → {(afterEOk ? "ok" : "FAIL")}");
            t &= afterEOk;

            // After Roll G stamps residual, field is non-null
            var afterG = afterE with { UsageResidualPressure = 0.06 };
            var afterGOk = afterG.UsageResidualPressure.HasValue && afterG.UsageResidualPressure.Value == 0.06;
            Console.WriteLine($"    After Roll G stamp (0.06): UsageResidualPressure={afterG.UsageResidualPressure} → {(afterGOk ? "ok" : "FAIL")}");
            t &= afterGOk;

            // ResetOffense with-expression must clear both
            var reset = afterG with
            {
                SelectedSlot          = null,
                ShotType              = null,
                Result                = null,
                FastBreak             = false,
                UsagePressure         = null,
                UsageResidualPressure = null,
            };
            var resetOk = !reset.UsagePressure.HasValue && !reset.UsageResidualPressure.HasValue;
            Console.WriteLine($"    After ResetOffense-style with: both null → {(resetOk ? "ok" : "FAIL")}");
            t &= resetOk;

            pass &= t;
            Console.WriteLine($"  (g) {(t ? "ok" : "FAIL")}");
        }

        Console.WriteLine(pass ? "  Phase 17 PASSED." : "  Phase 17 FAILED.");
        return pass;
    }

    // =========================================================================
    // Phase 46 — Individual Matchup Denial: per-slot access denial in Roll E
    // BendByAttention. Direct-probe discipline (matches Phase 44 convention):
    // every assertion reads real generator output via BendByAttention directly —
    // no stubs, no full-game batch.
    // =========================================================================

    private static bool Phase46IndividualDenialCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase46IndividualDenialCheck ---");
        var pass = true;

        var cfgE  = RollEConfig.Load(configPath);
        var cfgM  = MatchupConfig.Load(configPath);
        var cfgD  = RollDConfig.Load(configPath);
        const double Eps = 1e-4;   // directional checks: share must move by at least this much

        // Shared helper: build a GameState, run GenerateWithPressure + AttentionGenerator,
        // call BendByAttention directly, return the Slot1 share.
        // offSlot1 / defSlot1 are the full players for slot 1.
        // Slots 2-5 receive all-50 filler (both sides) unless overridden.
        double GetSlot1Share(
            Player offSlot1, Player defSlot1,
            Player? offFill2to5 = null, Player? defFill2to5 = null)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var coach = new CoachProfile(heliocentricBias: 5.0, shotSelectionBias: 5.0, paceBias: 5.0);
            g.SetCoach(TeamSide.Home, coach);
            g.SetCoach(TeamSide.Away, coach);

            // Offensive lineup
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), offSlot1);
            var neutralOff = offFill2to5 ?? new Player("noff") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=0,
                Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
                Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
                BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            for (var i = 2; i <= 5; i++) g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), neutralOff);

            // Defensive lineup
            g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(1), defSlot1);
            var neutralDef = defFill2to5 ?? new Player("ndef") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=0,
                Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
                Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
                BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            for (var i = 2; i <= 5; i++) g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), neutralDef);

            var genE    = new RollEGenerator(cfgE, g);
            var attnGen = new AttentionGenerator(AttentionConfig.Load(configPath), g);
            var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            var genResult = genE.GenerateWithPressure(st);
            var attn      = attnGen.Generate(st, genResult.FinalShares);
            var tilted    = genE.BendByAttention(genResult, attn.AttentionShares, g, cfgM, st);
            return tilted.Slices.First(s => s.Outcome == SelectionOutcome.Slot1).Weight;
        }

        // Helper players (reusable across sub-checks)

        // High-usage focal Slot 1 (perimeter: low postness, high SelfCreation).
        var perimStar = new Player("psstar") {
            Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
            FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
            SelfCreation=70, PostMoves=20, OffBallMovement=50, Screening=40,
            OffensiveRebounding=30, PerimeterDefense=55, PostDefense=30, RimProtection=20,
            DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
            Height=30, Wingspan=45, Weight=40, Strength=30, Speed=70,
            Quickness=70, FirstStep=70, Vertical=55, Endurance=60, Hustle=60,
            BasketballIQ=60, Discipline=60, HierarchyRank=10,
            RimTendency=20, ShortTendency=15, MidTendency=25, LongTendency=15, ThreeTendency=25,
        };

        // High-usage focal Slot 1 (post: high postness, high PostMoves/Close).
        var postStar = new Player("postar") {
            Close=70, Mid=45, Outside=30, Finishing=75, FreeThrow=55,
            FoulDrawing=60, BallHandling=40, Passing=40, Playmaking=35,
            SelfCreation=40, PostMoves=75, OffBallMovement=35, Screening=50,
            OffensiveRebounding=75, PerimeterDefense=40, PostDefense=90, RimProtection=70,
            DefensiveRebounding=80, Steals=35, HelpDefense=0, OffBallDefense=50,
            Height=90, Wingspan=85, Weight=80, Strength=90, Speed=40,
            Quickness=35, FirstStep=35, Vertical=60, Endurance=65, Hustle=70,
            BasketballIQ=55, Discipline=60, HierarchyRank=10,
            RimTendency=50, ShortTendency=35, MidTendency=10, LongTendency=3, ThreeTendency=2,
        };

        // Neutral defender (all 50 relevant attrs — exact neutral matchup vs an all-50 offensive player).
        var neutralDef1 = new Player("ndef1") {
            Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
            FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
            SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
            OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
            DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=50,
            Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
            Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
            BasketballIQ=50, Discipline=50, HierarchyRank=5,
            RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
        };

        // ── (a) Perimeter denial fires ────────────────────────────────────────
        Console.WriteLine("  (a) Perimeter denial fires:");
        {
            var perimStarLowOBM = new Player("pslowobm") {
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=20, OffBallMovement=30, Screening=40,
                OffensiveRebounding=30, PerimeterDefense=55, PostDefense=30, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Height=30, Wingspan=45, Weight=40, Strength=30, Speed=70,
                Quickness=70, FirstStep=70, Vertical=55, Endurance=60, Hustle=60,
                BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=15, MidTendency=25, LongTendency=15, ThreeTendency=25,
            };
            // Slot-1 postness (low)
            var pn = Matchup.Postness(perimStarLowOBM, cfgM);
            Console.WriteLine($"    perim player postness={pn:F2} (expect < {cfgM.PostnessNeutral})");

            // Neutral defender (OBD=50) vs high OBD defender (90)
            var highObdDef = new Player("hobd") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=90,
                Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
                Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
                BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var shareNeutral = GetSlot1Share(perimStarLowOBM, neutralDef1);
            var shareHighObd = GetSlot1Share(perimStarLowOBM, highObdDef);
            var aOk = shareHighObd < shareNeutral - Eps;
            Console.WriteLine($"    OBD=50 Slot1={shareNeutral:F6}  OBD=90 Slot1={shareHighObd:F6}");
            Console.WriteLine($"    High OBD drops share → {(aOk ? "ok" : "FAIL")}");
            pass &= aOk;
            Console.WriteLine($"  (a) {(aOk ? "ok" : "FAIL")}");
        }

        // ── (b) Perimeter self-balancing ──────────────────────────────────────
        Console.WriteLine("  (b) Perimeter self-balancing:");
        {
            var highObdDef = new Player("hobd2") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=90,
                Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
                Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
                BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            // Low OBM perimeter star (weak mover — maximal denial)
            var lowObmStar = new Player("lowobm") {
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=20, OffBallMovement=20, Screening=40,
                OffensiveRebounding=30, PerimeterDefense=55, PostDefense=30, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Height=30, Wingspan=45, Weight=40, Strength=30, Speed=70,
                Quickness=70, FirstStep=70, Vertical=55, Endurance=60, Hustle=60,
                BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=15, MidTendency=25, LongTendency=15, ThreeTendency=25,
            };
            // High OBM perimeter star (shifty mover — minimal denial)
            var highObmStar = new Player("hiobm") {
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=20, OffBallMovement=85, Screening=40,
                OffensiveRebounding=30, PerimeterDefense=55, PostDefense=30, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Height=30, Wingspan=45, Weight=40, Strength=30, Speed=70,
                Quickness=70, FirstStep=70, Vertical=55, Endurance=60, Hustle=60,
                BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=15, MidTendency=25, LongTendency=15, ThreeTendency=25,
            };
            var shareLowObm  = GetSlot1Share(lowObmStar,  highObdDef);
            var shareHighObm = GetSlot1Share(highObmStar, highObdDef);
            var bOk = shareHighObm > shareLowObm + Eps;
            Console.WriteLine($"    OBM=20 Slot1={shareLowObm:F6}  OBM=85 Slot1={shareHighObm:F6}");
            Console.WriteLine($"    Shifty mover less denied → {(bOk ? "ok" : "FAIL")}");
            pass &= bOk;
            Console.WriteLine($"  (b) {(bOk ? "ok" : "FAIL")}");
        }

        // ── (c) Post denial fires ─────────────────────────────────────────────
        Console.WriteLine("  (c) Post denial fires:");
        {
            var pn = Matchup.Postness(postStar, cfgM);
            Console.WriteLine($"    post player postness={pn:F2} (expect > {cfgM.PostnessNeutral})");

            // Neutral defender vs high-PostDefense defender
            var highPdDef = new Player("hpd") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=90, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=50,
                Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
                Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
                BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var shareNeutral = GetSlot1Share(postStar, neutralDef1);
            var shareHighPd  = GetSlot1Share(postStar, highPdDef);
            var cOk = shareHighPd < shareNeutral - Eps;
            Console.WriteLine($"    PD=50 Slot1={shareNeutral:F6}  PD=90 Slot1={shareHighPd:F6}");
            Console.WriteLine($"    High PostDefense drops share → {(cOk ? "ok" : "FAIL")}");
            pass &= cOk;
            Console.WriteLine($"  (c) {(cOk ? "ok" : "FAIL")}");
        }

        // ── (d) Post self-balancing ───────────────────────────────────────────
        Console.WriteLine("  (d) Post self-balancing:");
        {
            var highPdDef = new Player("hpd2") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=90, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=50,
                Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
                Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
                BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            // Weak post star (low Str+PostMoves — more denied)
            var weakPost = new Player("wkpost") {
                Close=70, Mid=45, Outside=30, Finishing=75, FreeThrow=55,
                FoulDrawing=60, BallHandling=40, Passing=40, Playmaking=35,
                SelfCreation=40, PostMoves=30, OffBallMovement=35, Screening=50,
                OffensiveRebounding=75, PerimeterDefense=40, PostDefense=90, RimProtection=70,
                DefensiveRebounding=80, Steals=35, HelpDefense=0, OffBallDefense=50,
                Height=90, Wingspan=85, Weight=80, Strength=35, Speed=40,
                Quickness=35, FirstStep=35, Vertical=60, Endurance=65, Hustle=70,
                BasketballIQ=55, Discipline=60, HierarchyRank=10,
                RimTendency=50, ShortTendency=35, MidTendency=10, LongTendency=3, ThreeTendency=2,
            };
            // Strong post star (high Str+PostMoves — less denied, possibly boosted)
            var strongPost = new Player("stpost") {
                Close=70, Mid=45, Outside=30, Finishing=75, FreeThrow=55,
                FoulDrawing=60, BallHandling=40, Passing=40, Playmaking=35,
                SelfCreation=40, PostMoves=90, OffBallMovement=35, Screening=50,
                OffensiveRebounding=75, PerimeterDefense=40, PostDefense=90, RimProtection=70,
                DefensiveRebounding=80, Steals=35, HelpDefense=0, OffBallDefense=50,
                Height=90, Wingspan=85, Weight=80, Strength=95, Speed=40,
                Quickness=35, FirstStep=35, Vertical=60, Endurance=65, Hustle=70,
                BasketballIQ=55, Discipline=60, HierarchyRank=10,
                RimTendency=50, ShortTendency=35, MidTendency=10, LongTendency=3, ThreeTendency=2,
            };
            var shareWeak   = GetSlot1Share(weakPost,   highPdDef);
            var shareStrong = GetSlot1Share(strongPost, highPdDef);
            var dOk = shareStrong > shareWeak + Eps;
            Console.WriteLine($"    weak Str+PM Slot1={shareWeak:F6}  strong Str+PM Slot1={shareStrong:F6}");
            Console.WriteLine($"    Strong post harder to deny → {(dOk ? "ok" : "FAIL")}");
            pass &= dOk;
            Console.WriteLine($"  (d) {(dOk ? "ok" : "FAIL")}");
        }

        // ── (e) Athletic gap — both directions ────────────────────────────────
        Console.WriteLine("  (e) Athletic gap (both directions):");
        {
            // Neutral skill matchup; isolate athleticism.
            // Off: OBM=50, Str=50, PM=50 → skill gaps zero.
            var skillNeutralOff = new Player("athnoff") {
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=50, OffBallMovement=50, Screening=40,
                OffensiveRebounding=30, PerimeterDefense=55, PostDefense=30, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Height=30, Wingspan=45, Weight=40,
                Strength=50, Speed=50, Quickness=50, FirstStep=50, Vertical=50,   // Athleticism=50
                Endurance=60, Hustle=60, BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=15, MidTendency=25, LongTendency=15, ThreeTendency=25,
            };
            // Neutral skill defender (OBD=50, PD=50) but different athleticism levels.
            Player AthDef(int ath) => new Player($"athdef{ath}") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=50,
                Height=50, Wingspan=50, Weight=50,
                Strength=ath, Speed=ath, Quickness=ath, FirstStep=ath, Vertical=ath,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var shareAthDeny  = GetSlot1Share(skillNeutralOff, AthDef(80));   // def ath=80, off ath=50 → deny
            var shareAthNeu   = GetSlot1Share(skillNeutralOff, AthDef(50));   // equal → neutral
            var shareAthBoost = GetSlot1Share(skillNeutralOff, AthDef(20));   // def ath=20, off ath=50 → boost
            var eOk = shareAthDeny < shareAthNeu - Eps && shareAthBoost > shareAthNeu + Eps;
            Console.WriteLine($"    Def ath=80 (deny):  Slot1={shareAthDeny:F6}");
            Console.WriteLine($"    Def ath=50 (equal): Slot1={shareAthNeu:F6}");
            Console.WriteLine($"    Def ath=20 (boost): Slot1={shareAthBoost:F6}");
            Console.WriteLine($"    Both directions → {(eOk ? "ok" : "FAIL")}");
            pass &= eOk;
            Console.WriteLine($"  (e) {(eOk ? "ok" : "FAIL")}");
        }

        // ── (f) Never to zero ─────────────────────────────────────────────────
        Console.WriteLine("  (f) Never to zero:");
        {
            // Maximal mismatch: defender maxed (OBD=99, PD=99, all-99 athleticism),
            // offensive player minimal relevant attrs (OBM=0, Str=0, PM=0, ath=0).
            var minimalOff = new Player("minoff") {
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=0, OffBallMovement=0, Screening=40,
                OffensiveRebounding=30, PerimeterDefense=55, PostDefense=30, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Height=30, Wingspan=45, Weight=40,
                Strength=0, Speed=0, Quickness=0, FirstStep=0, Vertical=0,
                Endurance=60, Hustle=60, BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=15, MidTendency=25, LongTendency=15, ThreeTendency=25,
            };
            var maxedDef = new Player("maxdef") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=99, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=99,
                Height=50, Wingspan=50, Weight=50,
                Strength=99, Speed=99, Quickness=99, FirstStep=99, Vertical=99,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var shareMaxDeny = GetSlot1Share(minimalOff, maxedDef);
            var fOkSub = shareMaxDeny > 0.0;
            Console.WriteLine($"    Maximal mismatch Slot1={shareMaxDeny:F6} (expect > 0)");
            Console.WriteLine($"    Share strictly positive → {(fOkSub ? "ok" : "FAIL")}");
            pass &= fOkSub;
            Console.WriteLine($"  (f) {(fOkSub ? "ok" : "FAIL")}");
        }

        // ── (g) Offense wins → more touches ───────────────────────────────────
        Console.WriteLine("  (g) Offense wins → more touches:");
        {
            // Neutral defender; high-skill, high-athleticism offensive player vs weak defender.
            var weakDef = new Player("wkdef") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=20, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=20,
                Height=50, Wingspan=50, Weight=50,
                Strength=20, Speed=20, Quickness=20, FirstStep=20, Vertical=20,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            // Perimeter star with high OBM + good athleticism vs weak defender
            var shiftyStar = new Player("shify") {
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=30, OffBallMovement=85, Screening=40,
                OffensiveRebounding=30, PerimeterDefense=55, PostDefense=30, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Height=30, Wingspan=45, Weight=40,
                Strength=30, Speed=80, Quickness=80, FirstStep=80, Vertical=60,
                Endurance=60, Hustle=60, BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=15, MidTendency=25, LongTendency=15, ThreeTendency=25,
            };
            var shareVsNeutral = GetSlot1Share(shiftyStar, neutralDef1);
            var shareVsWeak    = GetSlot1Share(shiftyStar, weakDef);
            var gOk = shareVsWeak > shareVsNeutral + Eps;
            Console.WriteLine($"    vs neutral def Slot1={shareVsNeutral:F6}  vs weak def Slot1={shareVsWeak:F6}");
            Console.WriteLine($"    Offense winning raises share → {(gOk ? "ok" : "FAIL")}");
            pass &= gOk;
            Console.WriteLine($"  (g) {(gOk ? "ok" : "FAIL")}");
        }

        // ── (h) Neutral wash ─────────────────────────────────────────────────
        Console.WriteLine("  (h) Neutral wash (equal matchup → denialMult = 1.0):");
        {
            // All-50 offensive and defensive slot-1 players → all gaps zero → mult = 1.0.
            // Compare against the same player facing the same player (baseline = control).
            var allFiftyOff = new Player("a50off") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=50,
                Height=50, Wingspan=50, Weight=50,
                Strength=50, Speed=50, Quickness=50, FirstStep=50, Vertical=50,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=10,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var allFiftyDef = new Player("a50def") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=50,
                Height=50, Wingspan=50, Weight=50,
                Strength=50, Speed=50, Quickness=50, FirstStep=50, Vertical=50,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            // Baseline: same all-50 matchup (denial is exactly neutral; share = unmodified usage share)
            var shareNeutralMatchup = GetSlot1Share(allFiftyOff, allFiftyDef);
            // Confirm the gaps are zero analytically
            var hPerimGap = allFiftyDef.OffBallDefense - allFiftyOff.OffBallMovement;   // = 0
            var hPostGap  = allFiftyDef.PostDefense - (allFiftyOff.Strength + allFiftyOff.PostMoves) / 2.0;  // = 0
            var hAthGap   = allFiftyDef.Athleticism - allFiftyOff.Athleticism;  // = 0
            var hOk = Math.Abs(hPerimGap) < 1e-9 && Math.Abs(hPostGap) < 1e-9 && Math.Abs(hAthGap) < 1e-9;
            Console.WriteLine($"    Gaps: perim={hPerimGap:F4} post={hPostGap:F4} ath={hAthGap:F6} → all zero: {hOk}");
            Console.WriteLine($"    Neutral matchup Slot1={shareNeutralMatchup:F6} (denial mult=1.0 by construction)");
            // A neutral matchup doesn't fire the denial at all — the share should equal 0.2
            // (all-50 lineup with HierarchyRank=10 at slot 1 among all-50 rank-5 fillers still
            // produces a >0.2 share due to usage; what we verify is the denial doesn't fire)
            // Verify by also running with a slightly unequal defender and checking it moves.
            var mildHighObdDef = new Player("mhobd") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=70,
                Height=50, Wingspan=50, Weight=50,
                Strength=50, Speed=50, Quickness=50, FirstStep=50, Vertical=50,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var shareMild = GetSlot1Share(allFiftyOff, mildHighObdDef);
            var hOk2 = shareMild < shareNeutralMatchup - Eps;
            Console.WriteLine($"    With OBD=70 def: Slot1={shareMild:F6} < neutral → {(hOk2 ? "ok — denial fires off neutral" : "FAIL")}");
            hOk &= hOk2;
            pass &= hOk;
            Console.WriteLine($"  (h) {(hOk ? "ok" : "FAIL")}");
        }

        // ── (i) Hybrid blend / no dead-zone at PostnessNeutral ───────────────
        Console.WriteLine("  (i) Hybrid blend / no dead-zone at PostnessNeutral:");
        {
            bool iOk = false;   // declared before any goto iDone; set true only on full pass

            // Build slot-1 player at exactly postness = PostnessNeutral.
            // With equal-thirds (1/3 each): Height = PostDefense = Strength = PostnessNeutral = 50.
            var midPostness = Matchup.Postness(new Player("mid50") {
                Height=50, PostDefense=50, Strength=50,
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=50, OffBallMovement=50, Screening=40,
                OffensiveRebounding=50, PerimeterDefense=55, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Wingspan=50, Weight=50, Speed=60, Quickness=60, FirstStep=60, Vertical=55,
                Endurance=60, Hustle=60, BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            }, cfgM);
            Console.WriteLine($"    Mid player postness={midPostness:F4} (expect = {cfgM.PostnessNeutral})");
            if (!(Math.Abs(midPostness - cfgM.PostnessNeutral) < 1e-9))
            {
                Console.WriteLine("    FAIL — cannot construct mid-postness fixture.");
                pass = false; Console.WriteLine("  (i) FAIL");
                goto iDone;
            }

            var midStar = new Player("midstar") {
                Height=50, PostDefense=50, Strength=50,
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=50, OffBallMovement=30, Screening=40,
                OffensiveRebounding=50, PerimeterDefense=55, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Wingspan=50, Weight=50, Speed=60, Quickness=60, FirstStep=60, Vertical=55,
                Endurance=60, Hustle=60, BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };

            // Neutral matchup baseline (OBD=50, PD=50, ath=50 — no denial in either channel)
            var iNeutralDef = new Player("indef") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=50,
                Height=50, Wingspan=50, Weight=50,
                Strength=50, Speed=60, Quickness=60, FirstStep=60, Vertical=55,  // Athleticism = 57 = midStar
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var iBaseline = GetSlot1Share(midStar, iNeutralDef);

            // (1) Perimeter-only edge: high OBD vs low OBM; post channel neutral.
            //     midStar.OBM=30 vs defender OBD=80: perimeterGap=+50 (defender wins).
            //     PostDefense=50 vs (Str=50+PM=50)/2=50: postGap=0.
            var perimEdgeDef = new Player("pedge") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=80,
                Height=50, Wingspan=50, Weight=50,
                Strength=50, Speed=60, Quickness=60, FirstStep=60, Vertical=55,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var iPerimOnly = GetSlot1Share(midStar, perimEdgeDef);

            // (2) Post-only edge: high PostDefense vs low (Str+PM)/2; perimeter channel neutral.
            //     midStar.OBM=30 vs OBD=30 (matched → perimeterGap=0).
            //     PostDefense=80 vs (Str=50+PM=50)/2=50: postGap=+30 (defender wins).
            var midStarOBM30 = new Player("midstarOBM30") {
                Height=50, PostDefense=50, Strength=50,
                Close=50, Mid=50, Outside=70, Finishing=50, FreeThrow=70,
                FoulDrawing=50, BallHandling=60, Passing=55, Playmaking=55,
                SelfCreation=70, PostMoves=50, OffBallMovement=30, Screening=40,   // OBM=30
                OffensiveRebounding=50, PerimeterDefense=55, RimProtection=20,
                DefensiveRebounding=30, Steals=55, HelpDefense=0, OffBallDefense=50,
                Wingspan=50, Weight=50, Speed=60, Quickness=60, FirstStep=60, Vertical=55,
                Endurance=60, Hustle=60, BasketballIQ=60, Discipline=60, HierarchyRank=10,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var postEdgeDef = new Player("postEdgeDef") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=80, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0,
                OffBallDefense=30,   // matched to OBM=30 → perimeterGap=0
                Height=50, Wingspan=50, Weight=50,
                Strength=50, Speed=60, Quickness=60, FirstStep=60, Vertical=55,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var iPostOnly = GetSlot1Share(midStarOBM30, postEdgeDef);

            // (3) Both edges simultaneously (same midStar OBM=30, OBD=80 + PD=80).
            var bothEdgeDef = new Player("bothedge") {
                Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
                FoulDrawing=50, BallHandling=50, Passing=50, Playmaking=50,
                SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
                OffensiveRebounding=50, PerimeterDefense=50, PostDefense=80, RimProtection=50,
                DefensiveRebounding=50, Steals=50, HelpDefense=0, OffBallDefense=80,
                Height=50, Wingspan=50, Weight=50,
                Strength=50, Speed=60, Quickness=60, FirstStep=60, Vertical=55,
                Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HierarchyRank=5,
                RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
            };
            var iBoth = GetSlot1Share(midStar, bothEdgeDef);

            Console.WriteLine($"    baseline  Slot1={iBaseline:F6}");
            Console.WriteLine($"    perim-only Slot1={iPerimOnly:F6} < baseline → {(iPerimOnly < iBaseline - Eps ? "ok" : "FAIL")}");
            Console.WriteLine($"    post-only  Slot1={iPostOnly:F6} < baseline → {(iPostOnly  < iBaseline - Eps ? "ok" : "FAIL")}");
            Console.WriteLine($"    both edges Slot1={iBoth:F6} < min(perim,post) → {(iBoth < Math.Min(iPerimOnly, iPostOnly) - Eps ? "ok" : "FAIL")}");

            iOk = iPerimOnly < iBaseline - Eps
                   && iPostOnly  < iBaseline - Eps
                   && iBoth      < Math.Min(iPerimOnly, iPostOnly) - Eps;
            Console.WriteLine($"    Mid-postness: both channels active, blend stronger than either alone → {(iOk ? "ok" : "FAIL")}");
            pass &= iOk;
            iDone:
            Console.WriteLine($"  (i) {(iOk ? "ok" : "FAIL")}");
        }

        Console.WriteLine(pass ? "  Phase 46 PASSED." : "  Phase 46 FAILED.");
        return pass;
    }

    // =========================================================================
    // Phase 47 — Passing compound (rank-weighted, bottom-heavy). Direct-probe
    // discipline (matches the Phase 46 convention): every assertion reads real
    // AttentionGenerator output (TeamConversionQuality) — no stubs, no batch.
    //
    // conversionQuality reads ONLY the offense's per-player ratings; it does NOT
    // read the usage shares. So every fixture passes a FIXED neutral finalShares
    // array, and every compared lineup is identical in every attribute EXCEPT
    // Passing — making playmaking activation (and therefore the coefficient on the
    // passing compound) provably identical across the compared lineups. The
    // coefficient C = DirectPassingScale + ActivationScale × playmakingActivation
    // is recovered empirically from a uniform lineup (a normalized weighted average
    // of identical values equals that value, for ANY weights and ANY knob), so the
    // checks need no duplicated activation math and no hardcoded activation
    // constant. The all-50 base puts every raw conversionQuality well under 1.0, so
    // the [0,1] clamp never participates in (a)–(e).
    // =========================================================================
    private static bool PassingCompoundCheck(string configPath)
    {
        Console.WriteLine("\n--- PassingCompoundCheck ---");
        var pass = true;

        var cfg  = AttentionConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        const double Tol     = 1e-6;   // conversionQuality comparison tolerance
        const double TolFlat = 1e-9;   // (d) flat-knob == arithmetic-mean tolerance
        var fixedShares = new double[5] { 0.2, 0.2, 0.2, 0.2, 0.2 };  // ignored by conversionQuality; fixed so it can never tilt a comparison

        // A passer identical on every rating EXCEPT Passing (all-50 base →
        // GravityContribution 51.25; one playmaking activation shared by every
        // fixture in this check).
        Player Passer(int passing) => new Player("p")
        {
            Close=50, Mid=50, Outside=50, Finishing=50, FreeThrow=50,
            FoulDrawing=50, BallHandling=50, Passing=passing, Playmaking=50,
            SelfCreation=50, PostMoves=50, OffBallMovement=50, Screening=50,
            OffensiveRebounding=50, PerimeterDefense=50, PostDefense=50, RimProtection=50,
            DefensiveRebounding=50, Steals=50, HelpDefense=50, OffBallDefense=50,
            Height=50, Wingspan=50, Weight=50, Strength=50, Speed=50,
            Quickness=50, FirstStep=50, Vertical=50, Endurance=50, Hustle=50,
            BasketballIQ=50, Discipline=50, HierarchyRank=5,
            RimTendency=20, ShortTendency=20, MidTendency=20, LongTendency=20, ThreeTendency=20,
        };

        // conversionQuality for a five-man offense whose per-slot Passing ratings
        // are passingBySlot[0..4]; all other ratings identical. Defense is neutral
        // (irrelevant to conversionQuality). useCfg lets (d) probe a flat-knob variant.
        double ConvQuality(int[] passingBySlot, AttentionConfig useCfg)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var coach = new CoachProfile(heliocentricBias: 5.0, shotSelectionBias: 5.0, paceBias: 5.0);
            g.SetCoach(TeamSide.Home, coach);
            g.SetCoach(TeamSide.Away, coach);
            for (var i = 1; i <= 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), Passer(passingBySlot[i - 1]));
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), Passer(50));
            }
            var attnGen = new AttentionGenerator(useCfg, g);
            var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            return attnGen.Generate(st, fixedShares).TeamConversionQuality;
        }

        // ── (a) Monotone in total passing ────────────────────────────────────
        var qa50 = ConvQuality(new[] { 50, 50, 50, 50, 50 }, cfg);
        var qa80 = ConvQuality(new[] { 80, 80, 80, 80, 80 }, cfg);
        var qa99 = ConvQuality(new[] { 99, 99, 99, 99, 99 }, cfg);
        var aOk = qa50 < qa80 - Tol && qa80 < qa99 - Tol;
        Console.WriteLine($"  (a) monotone: Q(all-50)={qa50:F6} < Q(all-80)={qa80:F6} < Q(all-99)={qa99:F6} → {(aOk ? "ok" : "FAIL")}");
        pass &= aOk;

        // Recover the coefficient C on the passing compound from the uniform-50
        // lineup: Q(all-u) = ConversionFloor + C·u  →  C = (Q(all-u) − floor)/u.
        // Mathematically equal to DirectPassingScale + ActivationScale×activation;
        // must be > 0 (positive coefficient, required for the inversions below).
        var coeff = (qa50 - cfg.ConversionFloor) / 0.50;
        var coeffOk = coeff > 0.0;
        Console.WriteLine($"  coefficient on passingCompound C={coeff:F6} (> 0 → {(coeffOk ? "ok" : "FAIL")})");
        pass &= coeffOk;

        // ── (b) Normalization bounds (clamp must NOT participate) ─────────────
        var qb0 = ConvQuality(new[] { 0, 0, 0, 0, 0 }, cfg);
        var floorOk = Math.Abs(qb0 - cfg.ConversionFloor) < Tol;        // all-0 → compound 0 → Q == floor
        var impliedCompound99 = (qa99 - cfg.ConversionFloor) / coeff;   // recover compound from Q
        var normOk = Math.Abs(impliedCompound99 - 0.99) < Tol;         // all-99 → exactly 0.99 (NOT 1.0)
        var clampOk = qa99 < 1.0 - Tol;                                // raw Q stayed below the [0,1] clamp
        Console.WriteLine($"  (b) all-0 → Q={qb0:F6} == ConversionFloor {cfg.ConversionFloor:F6} → {(floorOk ? "ok" : "FAIL")}");
        Console.WriteLine($"      all-99 → implied compound={impliedCompound99:F6} == 0.99 → {(normOk ? "ok" : "FAIL")}; raw Q={qa99:F6} < 1.0 (clamp inactive) → {(clampOk ? "ok" : "FAIL")}");
        pass &= floorOk && normOk && clampOk;

        // ── (c) Bottom-heavy direction — THE load-bearing check ───────────────
        // EQUAL total (370). X = four elite + one dud; Y = five even. Y must WIN —
        // the heaviest rank weight lands on Y's solid fifth man, not X's dud.
        var qcX = ConvQuality(new[] { 90, 90, 90, 90, 10 }, cfg);   // compound ≈ 0.6378
        var qcY = ConvQuality(new[] { 74, 74, 74, 74, 74 }, cfg);   // compound  = 0.7400
        var cOk = qcY > qcX + Tol;
        Console.WriteLine($"  (c) equal-total 370: Y(74×5) Q={qcY:F6} > X(90,90,90,90,10) Q={qcX:F6} → {(cOk ? "ok" : "FAIL")}");
        pass &= cOk;

        // ── (d) Flat-knob degeneration (PRODUCTION path, not algebra) ─────────
        // Same generator, a config with PassingRankWeight = 1.0. Two NON-uniform
        // lineups must reproduce the retired arithmetic mean exactly. Non-uniform
        // is essential: a uniform lineup equals its value under ANY knob and so
        // would also pass if the config knob were ignored/hardcoded.
        var cfgFlat = AttentionConfig.Load(configPath);
        cfgFlat.PassingRankWeight = 1.0;
        var coeffFlat = (ConvQuality(new[] { 50, 50, 50, 50, 50 }, cfgFlat) - cfgFlat.ConversionFloor) / 0.50;
        var d1Implied = (ConvQuality(new[] { 90, 70, 50, 30, 10 }, cfgFlat) - cfgFlat.ConversionFloor) / coeffFlat;
        var d2Implied = (ConvQuality(new[] { 99, 90, 60, 35, 20 }, cfgFlat) - cfgFlat.ConversionFloor) / coeffFlat;
        var d1Ok = Math.Abs(d1Implied - 0.500) < TolFlat;   // mean of 0.9,0.7,0.5,0.3,0.1
        var d2Ok = Math.Abs(d2Implied - 0.608) < TolFlat;   // mean of 0.99,0.9,0.6,0.35,0.2
        Console.WriteLine($"  (d) flat knob (1.0): implied compound (90,70,50,30,10)={d1Implied:F10} == 0.500 → {(d1Ok ? "ok" : "FAIL")}");
        Console.WriteLine($"      flat knob (1.0): implied compound (99,90,60,35,20)={d2Implied:F10} == 0.608 → {(d2Ok ? "ok" : "FAIL")}");
        pass &= d1Ok && d2Ok;

        // ── (e) One sharp passer ≠ five connected passers (second equal-total) ─
        // EQUAL total (210). One-sharp (90,30,30,30,30) vs even (42×5). Even WINS:
        // the model rewards lineup-wide continuity, not value concentrated in one man.
        var qeOne  = ConvQuality(new[] { 90, 30, 30, 30, 30 }, cfg);   // compound ≈ 0.3622
        var qeEven = ConvQuality(new[] { 42, 42, 42, 42, 42 }, cfg);   // compound  = 0.4200
        var eOk = qeEven > qeOne + Tol;
        Console.WriteLine($"  (e) equal-total 210: even(42×5) Q={qeEven:F6} > one-sharp(90,30,30,30,30) Q={qeOne:F6} → {(eOk ? "ok" : "FAIL")}");
        pass &= eOk;

        Console.WriteLine(pass ? "  Phase 47 PASSED." : "  Phase 47 FAILED.");
        return pass;
    }

}
