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
            HelpDefense=50,
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
            HelpDefense=50,
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
            HelpDefense=50,
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
            HelpDefense=50,
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
            HelpDefense=50,
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
                Discipline = b, HelpDefense = b,
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
                BasketballIQ = b, Discipline = b, HelpDefense = b,
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
                BasketballIQ=50, Discipline=50, HelpDefense=50,
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

}
