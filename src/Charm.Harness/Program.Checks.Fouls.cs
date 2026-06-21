using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // --- Batch: Roll D's three flavor rates match its pie, and every exit is a
    //     clean Continue (ResumeInbound or ResolveFreeThrows). Uses a fresh game
    //     per iteration so the foul count never climbs into the bonus — this
    //     check isolates FLAVOR conformance; routing is checked separately. ---
    private static bool RollDFlavorBatchCheck(
        RollAConfig cfg, RollDConfig cfgD, RollDGenerator genD, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} fouls through Roll D (flavor rates) ---");
        var rng = new SystemRng(cfg.Seed);
        var pieD = genD.Generate(state);

        var counts = new Dictionary<FoulFlavor, int>();
        foreach (var f in Enum.GetValues<FoulFlavor>()) counts[f] = 0;

        var unrouted = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Fresh single-foul game each time: stays below the bonus threshold,
            // so the route is always ResumeInbound and we measure flavor cleanly.
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var result = RollD.Execute(state, pieD, g, rng);

            if (result is not Continue c || c.Flavor is not { } flavor)
            {
                unrouted++;
                continue;
            }
            counts[flavor]++;

            // A single foul can never reach the bonus, so it must ResumeInbound.
            if (c.Next != ContinuationKind.ResumeInbound || c.Bonus != BonusType.None)
                unrouted++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll D flavors:");
        foreach (var (flavor, weight) in pieD.Slices)
        {
            var observed = counts[flavor] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {flavor,-10} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var routedOk = unrouted == 0;
        Console.WriteLine($"\n  every exit a clean below-bonus Continue: anomalies={unrouted} -> {(routedOk ? "ok" : "FAIL")}");
        return ratesOk && routedOk;
    }


    // --- Bonus routing: drive one team's foul count from 1 upward and confirm
    //     the route flips at exactly the configured thresholds: None below the
    //     bonus, OneAndOne on [bonus, double), Double at/above double. Also
    //     confirms the increment lands on the FOULING (defense) team only. ---
    private static bool RollDBonusRoutingCheck(
        RollDConfig cfgD, RollDGenerator genD, PossessionState state)
    {
        Console.WriteLine("\n--- Bonus routing: Roll D route vs. foul count ---");
        var rng = new SystemRng(42);
        var pieD = genD.Generate(state);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster

        var allOk = true;
        var offenseFoulsLeaked = false;

        for (var foul = 1; foul <= cfgD.DoubleBonusThreshold + 2; foul++)
        {
            var c = (Continue)RollD.Execute(state, pieD, game, rng);

            // Expected bonus for this post-increment count.
            var expectedBonus =
                foul >= cfgD.DoubleBonusThreshold ? BonusType.Double
                : foul >= cfgD.BonusThreshold ? BonusType.OneAndOne
                : BonusType.None;
            var expectedKind = expectedBonus == BonusType.None
                ? ContinuationKind.ResumeInbound
                : ContinuationKind.ResolveFreeThrows;

            var bonusOk = c.Bonus == expectedBonus;
            var kindOk = c.Next == expectedKind;
            allOk &= bonusOk && kindOk;

            // The foul must land on the defense (fouling team), never the offense.
            if (game.Fouls.FoulsFor(state.Offense) != 0) offenseFoulsLeaked = true;

            if (!bonusOk || !kindOk)
                Console.WriteLine($"    foul#{foul,2}: bonus={c.Bonus} (exp {expectedBonus}), kind={c.Next} (exp {expectedKind}) -> FAIL");
        }

        Console.WriteLine($"  routes match thresholds {cfgD.BonusThreshold}/{cfgD.DoubleBonusThreshold} across the climb -> {(allOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  fouls charged to defense only (offense stayed 0): {(!offenseFoulsLeaked ? "ok" : "FAIL")}");
        Console.WriteLine($"  final: defense fouls={game.Fouls.FoulsFor(state.Defense)}, bonus={game.Fouls.BonusFor(state.Defense)}");

        return allOk && !offenseFoulsLeaked;
    }


    // --- Direct unit check on the shared charge-and-fork (Core/DefensiveFoulCharge).
    //     This node is now the SINGLE place all five defensive-foul feeders (D, I, J,
    //     K, M) cross the bonus. Drive it across the foul thresholds with BOTH
    //     below-bonus kinds and with/without a flavor; confirm (a) the charge lands on
    //     the defense only, (b) the below/in-bonus split, (c) the Bonus payload on
    //     both arms, and (d) the optional flavor passes through (Roll D's shape) or
    //     stays null (I/J/K/M's shape). The five per-roll fork checks prove each
    //     caller still routes correctly THROUGH this node; this proves the node
    //     itself. ---
    private static bool DefensiveFoulChargeCheck(RollDConfig cfgD, PossessionState state)
    {
        Console.WriteLine("\n--- Shared node: DefensiveFoulCharge charge + fork + payload ---");

        bool RunPass(string label, ContinuationKind belowKind, FoulFlavor? flavor)
        {
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
            var allOk = true;
            var offenseLeaked = false;

            for (var foul = 1; foul <= cfgD.DoubleBonusThreshold + 2; foul++)
            {
                var c = (Continue)DefensiveFoulCharge.Resolve(state, game, belowKind, flavor);

                // Expected bonus for this post-increment count (mirrors FoulTracker).
                var expectedBonus =
                    foul >= cfgD.DoubleBonusThreshold ? BonusType.Double
                    : foul >= cfgD.BonusThreshold ? BonusType.OneAndOne
                    : BonusType.None;
                var expectedKind = expectedBonus == BonusType.None
                    ? belowKind
                    : ContinuationKind.ResolveFreeThrows;

                var bonusOk = c.Bonus == expectedBonus;
                var kindOk = c.Next == expectedKind;
                var flavorOk = c.Flavor == flavor;   // null == null when none supplied
                allOk &= bonusOk && kindOk && flavorOk;

                // The foul must land on the defense (fouling team), never the offense.
                if (game.Fouls.FoulsFor(state.Offense) != 0) offenseLeaked = true;

                if (!bonusOk || !kindOk || !flavorOk)
                    Console.WriteLine($"    [{label}] foul#{foul,2}: bonus={c.Bonus} (exp {expectedBonus}), " +
                        $"kind={c.Next} (exp {expectedKind}), flavor={c.Flavor?.ToString() ?? "<none>"} " +
                        $"(exp {flavor?.ToString() ?? "<none>"}) -> FAIL");
            }

            var pass = allOk && !offenseLeaked;
            Console.WriteLine($"  [{label}] below->{belowKind}, in-bonus->ResolveFreeThrows, " +
                $"flavor={(flavor?.ToString() ?? "<none>")}, charged defense only -> {(pass ? "ok" : "FAIL")}");
            return pass;
        }

        // Roll D's shape: below bonus resumes the inbound and carries a flavor.
        var dLike = RunPass("D: ResumeInbound+flavor", ContinuationKind.ResumeInbound, FoulFlavor.ReachIn);
        // I/J/K/M's shape: below bonus -> sideline throw-in, no flavor.
        var ijkmLike = RunPass("I/J/K/M: SidelineInbound", ContinuationKind.ResolveSidelineInbound, null);

        var ok = dLike && ijkmLike;
        Console.WriteLine($"  shared node reproduces BOTH caller shapes across the climb -> {(ok ? "ok" : "FAIL")}");
        return ok;
    }


    // --- Roll L: the free-throw loop. Drives each trip TYPE through the resolver's FT
    //     sequence and proves three things. (a) The RAW make/miss rate matches the flat
    //     config make% (Roll L spun in isolation). (b) Each trip spins Roll L the right
    //     number of times — and-1 = 1, fouled two / double = 2, fouled three = 3,
    //     1-and-1 = 1 (front miss) or 2 (front make) — and the global max never exceeds
    //     3 (read off RoutingOutcome.FreeThrowSpins, the observability counter parallel
    //     to PutbackAttempts). (c) The uniform last-shot routing holds, and the
    //     observable END-vs-FTRebound SPLIT is its signature: a fixed n-shot trip routes
    //     on the LAST shot only (intermediates are dead respins), so its END rate == p;
    //     a 1-and-1 ends at END only when BOTH shots make (p*p), a front miss forfeiting
    //     the second and going live to the rebound boundary (END rate == p*p). A made
    //     final FT hands the ball to the OPPONENT (DeadBallTo the defense). A missed final
    //     FT now flows into Roll M (Session 19), PINNED here to its DefensiveRebound
    //     terminal so the trip ends cleanly at the boundary — Roll L charges no foul and
    //     touches no arrow, and the pinned Roll M charges none either, so a fresh local
    //     game stays pristine across the batch and this check stays accumulation-free
    //     (CONVENTIONS §2a), unlike the foul forks upstream.
    private static bool RollLFreeThrowCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- Roll L: free-throw resolution ({cfg.BatchSize:N0} trips per type) ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgL = RollLConfig.Load(configPath);
        var p = cfgL.MakeProbability;

        // (a) Raw make/miss rate — spin Roll L directly against its flat pie.
        var rngRaw = new SystemRng(cfg.Seed);
        var pieL = new RollLStubPieGenerator(cfgL).Generate(new PossessionState(
            PossessionNumber: 0,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound));
        var rawMakes = 0;
        for (var i = 0; i < cfg.BatchSize; i++)
            if (RollL.Execute(pieL, rngRaw) == FreeThrowOutcome.Make) rawMakes++;
        var rawRate = (double)rawMakes / cfg.BatchSize;
        var rawOk = Math.Abs(rawRate - p) <= cfg.RateTolerance;
        Console.WriteLine($"  raw make rate: observed={rawRate:P3}  expected={p:P3}  -> {(rawOk ? "ok" : "FAIL")}");

        // A resolver to drive whole FT trips end to end (loop arithmetic + routing). A
        // FRESH local game; nothing in the FT path mutates it.
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var rng = new SystemRng(cfg.Seed);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCGenerator(cfgC),
            cfgC,
            new RollDGenerator(cfgD),
            new RollEStubPieGenerator(cfgE),
            new AttentionGenerator(AttentionConfig.Load(configPath), game),
            new RollFStubPieGenerator(cfgF),
            new RollGStubPieGenerator(cfgG),
            new RollHStubPieGenerator(RollHConfig.Load(configPath)),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(cfgL),
            // Roll M PINNED to its DefensiveRebound terminal for this check: a missed
            // final free throw must terminate cleanly at the rebound boundary so the FT
            // LOOP can be observed in isolation (exact spin bands, max <= 3, a clean
            // make/miss split). The live Roll M's loose-ball-defense arm would charge
            // fouls and its offensive-board arm would spin Roll K — both would pollute
            // spin counts and break the §2a accumulation-free property this check relies
            // on. Pinning one arm is the same isolation RollKBonusForkCheck uses with its
            // foulOnlyPie; Roll M's real distribution is RollMReboundBatchCheck's mandate.
            new RollMStubPieGenerator(new RollMConfig
            {
                DefensiveRebound = 1.0,
                OffensiveRebound = 0, LooseBallFoulOnDefense = 0, LooseBallFoulOnOffense = 0,
                OutOfBoundsOffOffense = 0, OutOfBoundsOffDefense = 0, JumpBall = 0
            }),
            new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        // Each trip type: the entry continuation, the expected exact spin-count band,
        // and the expected END (last-shot-make) rate.
        var trips = new (string Name, Continue Entry, int MinSpins, int MaxSpins, double ExpectedEnd)[]
        {
            ("and-1",        new Continue(ContinuationKind.ResolveShootingFreeThrows, state with { Result = ShotResult.MadeAndFouled, ShotType = ShotLocation.Rim }),   1, 1, p),
            ("fouled two",   new Continue(ContinuationKind.ResolveShootingFreeThrows, state with { Result = ShotResult.MissFouled, ShotType = ShotLocation.Mid }),      2, 2, p),
            ("fouled three", new Continue(ContinuationKind.ResolveShootingFreeThrows, state with { Result = ShotResult.MissFouled, ShotType = ShotLocation.Three }),    3, 3, p),
            ("double bonus", new Continue(ContinuationKind.ResolveFreeThrows, state) { Bonus = BonusType.Double },                                                      2, 2, p),
            ("one-and-one",  new Continue(ContinuationKind.ResolveFreeThrows, state) { Bonus = BonusType.OneAndOne },                                                   1, 2, p * p),
        };

        var allTripsOk = true;
        var globalMaxSpins = 0;
        Console.WriteLine("  per-trip: shot count, last-shot routing, made-FT consequence:");
        foreach (var trip in trips)
        {
            var ends = 0;
            var ftRebounds = 0;
            var other = 0;
            var minSeen = int.MaxValue;
            var maxSeen = 0;
            var spinOutOfRange = 0;
            var wrongConsequence = 0;

            for (var i = 0; i < cfg.BatchSize; i++)
            {
                var routing = resolver.Route(trip.Entry);
                var s = routing.FreeThrowSpins;
                if (s < minSeen) minSeen = s;
                if (s > maxSeen) maxSeen = s;
                if (s > globalMaxSpins) globalMaxSpins = s;
                if (s < trip.MinSpins || s > trip.MaxSpins) spinOutOfRange++;

                if (routing.Destination == "END:FreeThrowsMade")
                {
                    ends++;
                    // A made final FT gives the ball to the OPPONENT (the defense).
                    if (routing.EndedOn is { } term && term.Consequence.NextOffense != state.Defense)
                        wrongConsequence++;
                }
                else if (routing.Destination == "END:DefensiveRebound") ftRebounds++;
                else other++;
            }

            var endRate = (double)ends / cfg.BatchSize;
            var rateOk = Math.Abs(endRate - trip.ExpectedEnd) <= cfg.RateTolerance;
            var spinOk = spinOutOfRange == 0;
            var routedOk = other == 0;
            var consequenceOk = wrongConsequence == 0;
            var tripOk = rateOk && spinOk && routedOk && consequenceOk;
            allTripsOk &= tripOk;

            Console.WriteLine(
                $"    {trip.Name,-13} spins=[{minSeen}-{maxSeen}] (want {trip.MinSpins}-{trip.MaxSpins}) | " +
                $"END={endRate:P2} (want {trip.ExpectedEnd:P2})  FTReb={(double)ftRebounds / cfg.BatchSize:P2} | " +
                $"unrouted={other} wrongCons={wrongConsequence} -> {(tripOk ? "ok" : "FAIL")}");
        }

        var boundOk = globalMaxSpins <= 3;
        Console.WriteLine($"  spin count never exceeded 3: max={globalMaxSpins} -> {(boundOk ? "ok" : "FAIL")}");

        // (b) Real generator — attribute-driven make%
        Console.WriteLine("  (b) RollLGenerator — attribute-driven make%:");
        var cfgLb  = RollLConfig.Load(configPath);
        var gameB  = new GameState(new FoulTracker(RollDConfig.Load(configPath).BonusThreshold,
                                                    RollDConfig.Load(configPath).DoubleBonusThreshold));
        var homeLineup = gameB.LineupFor(TeamSide.Home);
        var homeRoster = gameB.RosterFor(TeamSide.Home);
        var slot1 = homeLineup.SlotAt(1);

        // p72: FreeThrow = 72
        var p72 = new Player("TestShooter72") { FreeThrow = 72 };
        homeRoster.SetStarter(slot1, p72);
        var genB72 = new RollLGenerator(cfgLb, gameB);
        var stateSlot1 = new PossessionState(
            PossessionNumber: 0,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound,
            SelectedSlot: slot1);
        var pie72  = genB72.Generate(stateSlot1);
        var make72 = pie72.Slices.First(s => s.Outcome == FreeThrowOutcome.Make).Weight;
        var b72ok  = Math.Abs(make72 - 0.72) <= cfgLb.Epsilon;
        Console.WriteLine($"    p72 (FreeThrow=72): make={make72:F6}  expected=0.720000  -> {(b72ok ? "ok" : "FAIL")}");

        // p85: FreeThrow = 85 — fresh game, fresh roster
        var gameB2 = new GameState(new FoulTracker(RollDConfig.Load(configPath).BonusThreshold,
                                                    RollDConfig.Load(configPath).DoubleBonusThreshold));
        var homeLineup2 = gameB2.LineupFor(TeamSide.Home);
        var homeRoster2 = gameB2.RosterFor(TeamSide.Home);
        var slot1b = homeLineup2.SlotAt(1);
        var p85 = new Player("TestShooter85") { FreeThrow = 85 };
        homeRoster2.SetStarter(slot1b, p85);
        var genB85      = new RollLGenerator(cfgLb, gameB2);
        var stateSlot1b = stateSlot1 with { SelectedSlot = slot1b };
        var pie85  = genB85.Generate(stateSlot1b);
        var make85 = pie85.Slices.First(s => s.Outcome == FreeThrowOutcome.Make).Weight;
        var b85ok  = Math.Abs(make85 - 0.85) <= cfgLb.Epsilon;
        Console.WriteLine($"    p85 (FreeThrow=85): make={make85:F6}  expected=0.850000  -> {(b85ok ? "ok" : "FAIL")}");

        // Null-slot fallback
        var gameB3   = new GameState(new FoulTracker(RollDConfig.Load(configPath).BonusThreshold,
                                                      RollDConfig.Load(configPath).DoubleBonusThreshold));
        var genBNull = new RollLGenerator(cfgLb, gameB3);
        var stateNull = stateSlot1 with { SelectedSlot = null };
        var pieNull  = genBNull.Generate(stateNull);
        var makeNull = pieNull.Slices.First(s => s.Outcome == FreeThrowOutcome.Make).Weight;
        var nullOk   = Math.Abs(makeNull - cfgLb.MakeProbability) <= cfgLb.Epsilon;
        Console.WriteLine($"    null-slot fallback: make={makeNull:F6}  expected={cfgLb.MakeProbability:F6}  -> {(nullOk ? "ok" : "FAIL")}");

        // Unpopulated-slot fallback (fresh game, no player seated in slot1)
        var gameB4     = new GameState(new FoulTracker(RollDConfig.Load(configPath).BonusThreshold,
                                                        RollDConfig.Load(configPath).DoubleBonusThreshold));
        var genBEmpty  = new RollLGenerator(cfgLb, gameB4);
        var slot1Empty = gameB4.LineupFor(TeamSide.Home).SlotAt(1);
        var stateEmpty = stateSlot1 with { SelectedSlot = slot1Empty };
        var pieEmpty   = genBEmpty.Generate(stateEmpty);
        var makeEmpty  = pieEmpty.Slices.First(s => s.Outcome == FreeThrowOutcome.Make).Weight;
        var emptyOk    = Math.Abs(makeEmpty - cfgLb.MakeProbability) <= cfgLb.Epsilon;
        Console.WriteLine($"    empty-slot fallback: make={makeEmpty:F6}  expected={cfgLb.MakeProbability:F6}  -> {(emptyOk ? "ok" : "FAIL")}");

        var realGenOk = b72ok && b85ok && nullOk && emptyOk;
        Console.WriteLine($"  real generator check: {(realGenOk ? "ok" : "FAIL")}");

        var ok = rawOk && allTripsOk && boundOk && realGenOk;
        Console.WriteLine($"  Roll L free-throw resolution: {(ok ? "ok" : "FAIL")}");
        return ok;
    }


    private static bool Phase8FoulDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 8: Foul door (matchup-aware foul rate) ---");
        var pass = true;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // Helper: build a player with all attributes at baseline b, overriding only
        // the named ones. FoulDrawing and Discipline are the foul-door attributes.
        // Height/Wingspan/Vertical are included so block math stays valid in regression.
        static Player Mk(int b,
                         int? fd = null, int? disc = null,
                         int? fin = null, int? rimP = null,
                         int? h = null, int? ws = null, int? v = null,
                         int? foulDrawing = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = fd ?? foulDrawing ?? b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = h ?? b, Wingspan = ws ?? b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = v ?? b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = disc ?? b, HelpDefense = b, OffBallDefense = b,
                RimTendency = rimT ?? b, ShortTendency = shortT ?? b, MidTendency = midT ?? b,
                LongTendency = longT ?? b, ThreeTendency = threeT ?? b,
            };

        // Shorthand: the matchup foul rate for a zone, shooter, defender.
        double FR(ShotLocation z, Player s, Player d)
            => Matchup.FoulRate(z, s, d, cfgH.FoulRate(z), cfgM);

        var rimBaseline = cfgH.FoulRate(ShotLocation.Rim);
        var rimCeiling  = cfgM.FoulCeiling(ShotLocation.Rim);
        var rimFloor    = cfgM.FoulFloor(ShotLocation.Rim);

        // ----------------------------------------------------------------
        // (a) Shooter FoulDrawing sweep @Rim (UP, large bend).
        //     Fix defender (all=50, Discipline=50). Sweep FD 20->95.
        //     Foul rate must rise monotonically and stay below ceiling.
        // ----------------------------------------------------------------
        Console.WriteLine("  (a) Shooter sweep @Rim (fix defender Disc=50, sweep FD 20..95):");
        var defA = Mk(50);
        double prevA = double.NegativeInfinity;
        var monoA = true;
        var ceilA = true;
        foreach (var fd in new[] { 20, 30, 40, 50, 60, 70, 80, 90, 95 })
        {
            var fr = FR(ShotLocation.Rim, Mk(50, fd: fd), defA);
            Console.WriteLine($"      FD={fd,2}  foulRate={fr:P3}");
            if (fr <= prevA) { monoA = false; Console.WriteLine($"        FAIL — not strictly increasing (prev={prevA:P3})"); }
            if (fr >= rimCeiling) { ceilA = false; Console.WriteLine($"        FAIL — crossed the ceiling {rimCeiling:P3}"); }
            prevA = fr;
        }
        var topA = FR(ShotLocation.Rim, Mk(50, fd: 95), defA);
        var topNearCeiling = topA > rimBaseline + 0.05;  // noticeably above baseline
        if (monoA && ceilA && topNearCeiling)
            Console.WriteLine($"      OK — monotonically rising; top={topA:P3} well above baseline={rimBaseline:P3}; below ceiling={rimCeiling:P3}.");
        if (!monoA) Console.WriteLine("  FAIL  (a) not monotonically increasing.");
        if (!ceilA) Console.WriteLine("  FAIL  (a) crossed the ceiling.");
        if (!topNearCeiling) Console.WriteLine($"  FAIL  (a) top of sweep ({topA:P3}) not noticeably above baseline ({rimBaseline:P3}).");
        pass &= monoA && ceilA && topNearCeiling;

        // ----------------------------------------------------------------
        // (b) Defender Discipline sweep @Rim (DOWN, small bend).
        //     Fix shooter (all=50, FD=50). Sweep Discipline 20->95.
        //     Foul rate must fall monotonically and stay above floor.
        // ----------------------------------------------------------------
        Console.WriteLine("  (b) Defender sweep @Rim (fix shooter FD=50, sweep Disc 20..95):");
        var shooterB = Mk(50);
        double prevB = double.PositiveInfinity;
        var monoB = true;
        var floorB = true;
        foreach (var disc in new[] { 20, 30, 40, 50, 60, 70, 80, 90, 95 })
        {
            var fr = FR(ShotLocation.Rim, shooterB, Mk(50, disc: disc));
            Console.WriteLine($"      Disc={disc,2}  foulRate={fr:P3}");
            if (fr >= prevB) { monoB = false; Console.WriteLine($"        FAIL — not strictly decreasing (prev={prevB:P3})"); }
            if (fr <= rimFloor) { floorB = false; Console.WriteLine($"        FAIL — crossed the floor {rimFloor:P3}"); }
            prevB = fr;
        }
        var bottomB = FR(ShotLocation.Rim, shooterB, Mk(50, disc: 95));
        var bottomSlightlyBelow = bottomB < rimBaseline && bottomB > rimFloor;
        if (monoB && floorB && bottomSlightlyBelow)
            Console.WriteLine($"      OK — monotonically falling; bottom={bottomB:P3} only slightly below baseline={rimBaseline:P3}; above floor={rimFloor:P3}.");
        if (!monoB) Console.WriteLine("  FAIL  (b) not monotonically decreasing.");
        if (!floorB) Console.WriteLine("  FAIL  (b) crossed the floor.");
        if (!bottomSlightlyBelow) Console.WriteLine($"  FAIL  (b) bottom ({bottomB:P3}) not in expected range ({rimFloor:P3}, {rimBaseline:P3}).");
        pass &= monoB && floorB && bottomSlightlyBelow;

        // ----------------------------------------------------------------
        // (c) Asymmetry — bend up >> bend down.
        //     An elite foul-drawer raises the rate more than an elite disciplined
        //     defender lowers it. Encodes "low FoulDrawing is not a skill."
        // ----------------------------------------------------------------
        Console.WriteLine("  (c) Asymmetry @Rim (up_bend > 3 x down_bend):");
        var even     = FR(ShotLocation.Rim, Mk(50), Mk(50));
        var upBend   = FR(ShotLocation.Rim, Mk(50, fd: 90), Mk(50)) - even;
        var downBend = even - FR(ShotLocation.Rim, Mk(50), Mk(50, disc: 90));
        Console.WriteLine($"      baseline={rimBaseline:P3}  up_bend (FD=90)={upBend:P3}  down_bend (Disc=90)={downBend:P3}");
        var asymOk = upBend > 3.0 * downBend;
        Console.WriteLine($"      up > 3x down? {upBend:P3} > {3.0 * downBend:P3} => {(asymOk ? "OK" : "FAIL")}");
        Console.WriteLine("      Basketball: a weak shooter doesn't suppress fouls as much as an elite drawer raises them.");
        if (!asymOk) Console.WriteLine("  FAIL  (c) asymmetry check failed.");
        pass &= asymOk;

        // ----------------------------------------------------------------
        // (d) Three sweep — much smaller spread than Rim.
        //     The per-zone foul impact is tiny at the three-point line.
        // ----------------------------------------------------------------
        Console.WriteLine("  (d) Three spread vs Rim spread (Three << Rim):");
        var defD  = Mk(50);
        var rimSpread = Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Rim,   Mk(50, fd: 20 + i * 5), defD)).Max()
                      - Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Rim,   Mk(50, fd: 20 + i * 5), defD)).Min();
        var thrSpread = Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Three, Mk(50, fd: 20 + i * 5), defD)).Max()
                      - Enumerable.Range(0, 16).Select(i => FR(ShotLocation.Three, Mk(50, fd: 20 + i * 5), defD)).Min();
        Console.WriteLine($"      Rim spread={rimSpread:P3}   Three spread={thrSpread:P3}");
        var spreadOk = thrSpread < rimSpread;
        Console.WriteLine($"      Three spread < Rim spread? {(spreadOk ? "ok" : "FAIL")}");
        if (!spreadOk) Console.WriteLine("  FAIL  (d) Three spread not smaller than Rim spread.");
        pass &= spreadOk;

        // ----------------------------------------------------------------
        // (e) DEC-6 fallback — baseline for empty defender; baseline for even matchup;
        //     elite foul-drawer > baseline + 0.02 at Rim.
        // ----------------------------------------------------------------
        Console.WriteLine("  (e) DEC-6 fallback and baseline checks @Rim:");
        var emptyFallback   = cfgH.FoulRate(ShotLocation.Rim);   // DEC-6: no matchup call
        var evenMatchup     = FR(ShotLocation.Rim, Mk(50), Mk(50));
        var eliteDrawer     = FR(ShotLocation.Rim, Mk(50, fd: 90), Mk(50));
        var fallbackOk      = Math.Abs(emptyFallback - rimBaseline) < 1e-9;
        var evenOk          = Math.Abs(evenMatchup   - rimBaseline) < 1e-9;
        var eliteOk         = eliteDrawer > rimBaseline + 0.02;
        Console.WriteLine($"      empty (DEC-6) = {emptyFallback:P3}  (expect {rimBaseline:P3}): {(fallbackOk ? "ok" : "FAIL")}");
        Console.WriteLine($"      even matchup  = {evenMatchup:P3}  (expect {rimBaseline:P3}): {(evenOk ? "ok" : "FAIL")}");
        Console.WriteLine($"      elite FD=90   = {eliteDrawer:P3}  (expect > {rimBaseline + 0.02:P3}): {(eliteOk ? "ok" : "FAIL")}");
        if (fallbackOk && evenOk && eliteOk)
            Console.WriteLine("      OK — empty==baseline; even==baseline; elite drawer > baseline+0.02.");
        pass &= fallbackOk && evenOk && eliteOk;

        // ----------------------------------------------------------------
        // (f) MAF/MissFouled split — batched check at Rim.
        //     Run 50,000 possessions through the real generator with an elite
        //     foul-drawer vs sloppy defender (high foul rate for signal). Of all
        //     fouled-shot outcomes, assert MAF fraction ~= MafFractionRim.
        // ----------------------------------------------------------------
        Console.WriteLine("  (f) MAF/MissFouled split @Rim (50k possessions, elite FD=90 vs Disc=20):");
        var fOk = true;
        try
        {
            var gF = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gF.SetPossessionArrow(TeamSide.Home);
            gF.HomeRoster.SetStarter(gF.HomeLineup.SlotAt(1), Mk(50, fd: 90));
            gF.AwayRoster.SetStarter(gF.AwayLineup.SlotAt(1), Mk(50, disc: 20));
            var genF    = new RollHGenerator(cfgH, cfgM, gF);
            var stateF  = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gF.HomeLineup.SlotAt(1));

            var mafCount  = 0;
            var mfCount   = 0;
            for (var i = 0; i < 50_000; i++)
            {
                var pie = genF.Generate(stateF, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result == ShotResult.MadeAndFouled) mafCount++;
                if (result == ShotResult.MissFouled)    mfCount++;
            }
            var totalFouled = mafCount + mfCount;
            if (totalFouled < 100)
            {
                fOk = false;
                Console.WriteLine($"  FAIL  (f) too few fouled outcomes ({totalFouled}) to measure split — check foul rate.");
            }
            else
            {
                var observedMafFrac = (double)mafCount / totalFouled;
                var expectedMafFrac = cfgH.MafFraction(ShotLocation.Rim);
                var splitGap = Math.Abs(observedMafFrac - expectedMafFrac);
                var splitOk  = splitGap < 0.04;  // loose tolerance — split from a random subsample
                Console.WriteLine($"      total fouled={totalFouled}  MAF={mafCount}  MissFouled={mfCount}");
                Console.WriteLine($"      MAF fraction: observed={observedMafFrac:P2}  expected={expectedMafFrac:P2}  gap={splitGap:P2}  {(splitOk ? "ok" : "FAIL")}");
                if (!splitOk) Console.WriteLine("  FAIL  (f) MAF fraction outside tolerance.");
                fOk = splitOk;
            }
        }
        catch (Exception ex)
        {
            fOk = false;
            Console.WriteLine($"  FAIL  (f) threw: {ex.Message}");
        }
        pass &= fOk;

        // ----------------------------------------------------------------
        // (g) Regression — Phase 6 and Phase 7 still valid after Phase 8 carve change.
        //     Elite finisher vs weak rim protector: make rate > 0.50, pie valid.
        // ----------------------------------------------------------------
        Console.WriteLine("  (g) Regression — Phase 6 + Phase 7 still valid after Phase 8 carve change:");
        var gOk = true;
        try
        {
            var gReg = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gReg.SetPossessionArrow(TeamSide.Home);
            gReg.HomeRoster.SetStarter(gReg.HomeLineup.SlotAt(1), Mk(50, fin: 99, h: 70, ws: 70, v: 70));
            gReg.AwayRoster.SetStarter(gReg.AwayLineup.SlotAt(1), Mk(50, rimP: 10, h: 55, ws: 55, v: 55));
            var genReg  = new RollHGenerator(cfgH, cfgM, gReg);
            var stateReg = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gReg.HomeLineup.SlotAt(1));

            var madeReg = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie = genReg.Generate(stateReg, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result is ShotResult.Made or ShotResult.MadeAndFouled) madeReg++;
            }
            var makeRate = (double)madeReg / 5_000;
            Console.WriteLine($"      elite finisher vs weak rim protector @Rim: make/and-1={makeRate:P1}  (valid pie)");
            var rateOk = makeRate > 0.50;
            if (rateOk)  Console.WriteLine("      OK — valid pie; make+and-1 > 0.50 for extreme shooter edge.");
            if (!rateOk) Console.WriteLine($"  FAIL  (g) make rate {makeRate:P1} is not > 0.50 for an extreme shooter edge.");
            gOk = rateOk;
        }
        catch (Exception ex)
        {
            gOk = false;
            Console.WriteLine($"  FAIL  (g) pie threw: {ex.Message}");
        }
        pass &= gOk;

        Console.WriteLine(pass ? "  Phase 8 PASSED." : "  Phase 8 FAILED.");
        return pass;
    }


    // ── Phase 25: Shooting-Foul Attribution Check ─────────────────────────────

    // ── Phase 25: Shooting-Foul Attribution Check ─────────────────────────────

    private static bool Phase25ShootingFoulAttributionCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("--- Phase 25: Shooting-Foul Attribution Check ---");
        Console.WriteLine("  Directional test: draws DrawFoulingDefender directly (100k draws per scenario, no end-to-end confound).");
        Console.WriteLine("  End-to-end test: 200 games with the Phase 24 controlled roster (completeness + no-zero check).");
        Console.WriteLine();

        var checkOk = true;

        // ── Load configs (same pattern as AttributionSanityCheck) ─────────────
        var cfg        = RollAConfig.Load(configPath);
        var cfgB       = RollBConfig.Load(configPath);
        var cfgC       = RollCConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);
        var cfgE       = RollEConfig.Load(configPath);
        var cfgF       = RollFConfig.Load(configPath);
        var cfgG       = RollGConfig.Load(configPath);
        var cfgH       = RollHConfig.Load(configPath);
        var cfgI       = RollIConfig.Load(configPath);
        var cfgJ       = RollJConfig.Load(configPath);
        var cfgK       = RollKConfig.Load(configPath);
        var cfgL       = RollLConfig.Load(configPath);
        var cfgM       = RollMConfig.Load(configPath);
        var cfgOffFoul = RollOffensiveFoulConfig.Load(configPath);
        var cfgGov     = GovernorConfig.Load(configPath);
        var cfgClock   = RollClockConfig.Load(configPath);
        var cfgEndHalf = EndOfHalfConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);

        // ── Controlled roster templates (identical to AttributionSanityCheck) ──
        var anchorTemplate = new Player("RimAnchor")
        {
            Height=92, Wingspan=92, Strength=88, Vertical=50,
            DefensiveRebounding=95, OffensiveRebounding=90,
            RimProtection=90, Finishing=90, FreeThrow=55,
            Outside=10, ThreeTendency=1, RimTendency=80,
            BallHandling=40, FoulDrawing=30,
            Close=50, Mid=50, ShortTendency=10, MidTendency=5, LongTendency=4,
            Passing=50, Playmaking=50, SelfCreation=50, PostMoves=50,
            OffBallMovement=50, Screening=50,
            PerimeterDefense=50, PostDefense=50, Steals=50,
            Weight=50, Speed=50, Quickness=50, FirstStep=50,
            Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HelpDefense=50, OffBallDefense=50,
        };
        var roleTemplate = new Player("PerimRole")
        {
            Height=35, Wingspan=35, Strength=30, Vertical=35,
            DefensiveRebounding=5, OffensiveRebounding=5,
            RimProtection=5, Finishing=35, FreeThrow=78,
            Outside=75, ThreeTendency=60, RimTendency=10,
            BallHandling=65, FoulDrawing=65,
            Close=50, Mid=50, ShortTendency=10, MidTendency=15, LongTendency=15,
            Passing=50, Playmaking=50, SelfCreation=50, PostMoves=50,
            OffBallMovement=50, Screening=50,
            PerimeterDefense=50, PostDefense=50, Steals=50,
            Weight=50, Speed=50, Quickness=50, FirstStep=50,
            Endurance=50, Hustle=50, BasketballIQ=50, Discipline=50, HelpDefense=50, OffBallDefense=50,
        };

        // ── §4.2 Directional test ─────────────────────────────────────────────
        // Build a minimal GameState for the directional test — just need a Roster
        // seeded with the five controlled players. No resolver or governor needed.
        const int DrawN = 100_000;
        const double Tol = 0.02;

        var dtGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        {
            var lineup = dtGame.LineupFor(TeamSide.Home);
            var roster = dtGame.RosterFor(TeamSide.Home);
            roster.SetStarter(lineup.SlotAt(1), StampPlayerId(anchorTemplate, 1));
            for (var i = 1; i <= 4; i++)
                roster.SetStarter(lineup.SlotAt(i + 1), StampPlayerId(roleTemplate, i + 1));
        }
        var dtRoster = dtGame.RosterFor(TeamSide.Home);

        static long[] TallyDraw(Random rng, TeamSide side, Roster roster,
            ShotLocation zone, int shooterSlot)
        {
            var counts = new long[6];
            for (var n = 0; n < DrawN; n++)
            {
                var s = DrawFoulingDefender(rng, side, roster, zone, shooterSlot);
                if (s >= 1 && s <= 5) counts[s]++;
            }
            return counts;
        }

        // Scenario 1: Guard shoots three (shooterSlot=2, zone=Three)
        {
            var rng = new Random(25001);
            var c = TallyDraw(rng, TeamSide.Home, dtRoster, ShotLocation.Three, 2);
            double ms = c[2]/(double)DrawN, s1 = c[1]/(double)DrawN;
            double s3 = c[3]/(double)DrawN, s4 = c[4]/(double)DrawN, s5 = c[5]/(double)DrawN;
            Console.WriteLine("  Scenario 1: guard shoots three (slot=2, zone=Three, seed=25001)");
            Console.WriteLine($"    Slot 1(int)={s1:F4}  Slot 2(mtch)={ms:F4}  Slot 3={s3:F4}  Slot 4={s4:F4}  Slot 5={s5:F4}");
            bool mOk = Math.Abs(ms - 0.80) <= Tol;
            bool dOk = s3 > s1 && s4 > s1 && s5 > s1;
            Console.WriteLine($"    [{(mOk ? "OK" : "FAIL")}] Matched share ~0.80 (observed {ms:F4})");
            Console.WriteLine($"    [{(dOk ? "OK" : "FAIL")}] Three residual: perimeter (3,4,5) > interior (1)");
            if (!mOk || !dOk) checkOk = false;
        }

        // Scenario 2: Guard drives rim (shooterSlot=2, zone=Rim)
        {
            var rng = new Random(25002);
            var c = TallyDraw(rng, TeamSide.Home, dtRoster, ShotLocation.Rim, 2);
            double ms = c[2]/(double)DrawN, s1 = c[1]/(double)DrawN;
            double s3 = c[3]/(double)DrawN, s4 = c[4]/(double)DrawN, s5 = c[5]/(double)DrawN;
            Console.WriteLine("  Scenario 2: guard drives rim (slot=2, zone=Rim, seed=25002)");
            Console.WriteLine($"    Slot 1(int)={s1:F4}  Slot 2(mtch)={ms:F4}  Slot 3={s3:F4}  Slot 4={s4:F4}  Slot 5={s5:F4}");
            bool mOk = Math.Abs(ms - 0.50) <= Tol;
            bool dOk = s1 > s3 && s1 > s4 && s1 > s5;
            Console.WriteLine($"    [{(mOk ? "OK" : "FAIL")}] Matched share ~0.50 (observed {ms:F4})");
            Console.WriteLine($"    [{(dOk ? "OK" : "FAIL")}] Rim residual: interior (1) > each perimeter (3,4,5)");
            if (!mOk || !dOk) checkOk = false;
        }

        // Scenario 3: Big shoots three (shooterSlot=1, zone=Three)
        {
            var rng = new Random(25003);
            var c = TallyDraw(rng, TeamSide.Home, dtRoster, ShotLocation.Three, 1);
            double ms = c[1]/(double)DrawN;
            double s2 = c[2]/(double)DrawN, s3 = c[3]/(double)DrawN, s4 = c[4]/(double)DrawN, s5 = c[5]/(double)DrawN;
            Console.WriteLine("  Scenario 3: big shoots three (slot=1, zone=Three, seed=25003)");
            Console.WriteLine($"    Slot 1(mtch)={ms:F4}  Slot 2={s2:F4}  Slot 3={s3:F4}  Slot 4={s4:F4}  Slot 5={s5:F4}");
            bool mOk = Math.Abs(ms - 0.80) <= Tol;
            Console.WriteLine($"    [{(mOk ? "OK" : "FAIL")}] Matched share ~0.80 (observed {ms:F4})");
            Console.WriteLine("    [NOTE] Residual (2-5) all perimeter — roughly equal expected.");
            if (!mOk) checkOk = false;
        }

        // Scenario 4: Big at rim (shooterSlot=1, zone=Rim)
        {
            var rng = new Random(25004);
            var c = TallyDraw(rng, TeamSide.Home, dtRoster, ShotLocation.Rim, 1);
            double ms = c[1]/(double)DrawN;
            double s2 = c[2]/(double)DrawN, s3 = c[3]/(double)DrawN, s4 = c[4]/(double)DrawN, s5 = c[5]/(double)DrawN;
            Console.WriteLine("  Scenario 4: big at rim (slot=1, zone=Rim, seed=25004)");
            Console.WriteLine($"    Slot 1(mtch)={ms:F4}  Slot 2={s2:F4}  Slot 3={s3:F4}  Slot 4={s4:F4}  Slot 5={s5:F4}");
            bool mOk = Math.Abs(ms - 0.50) <= Tol;
            Console.WriteLine($"    [{(mOk ? "OK" : "FAIL")}] Matched share ~0.50 (observed {ms:F4})");
            Console.WriteLine("    [NOTE] Residual (2-5) all perimeter — roughly equal expected.");
            if (!mOk) checkOk = false;
        }

        Console.WriteLine();
        Console.WriteLine("  --- End-to-end completeness (200 games, controlled roster) ---");

        const int Games25 = 200;
        var bsShFoul25 = new long[10];
        long totalHomeDefEvents = 0L;
        long totalAwayDefEvents = 0L;

        var firstState25 = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        Console.Write($"  Running {Games25} games");

        for (var seed = 1; seed <= Games25; seed++)
        {
            if (seed % 50 == 0) Console.Write(".");

            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));

            foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
            {
                var lineup = game.LineupFor(side);
                var roster = game.RosterFor(side);
                var idBase = side == TeamSide.Home ? 1 : 6;
                roster.SetStarter(lineup.SlotAt(1), StampPlayerId(anchorTemplate, idBase));
                for (var i = 1; i <= 4; i++)
                    roster.SetStarter(lineup.SlotAt(i + 1), StampPlayerId(roleTemplate, idBase + i));
            }

            game.SetPossessionArrow(TeamSide.Home);

            var resolver = new Resolver(
                new RollAGenerator(cfg, cfgMatchup, game), cfg,
                new RollBGenerator(cfgB, cfgMatchup, game),
                new RollCGenerator(cfgC), cfgC,
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, game),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFGenerator(cfgF, cfgMatchup, game),
                new RollGGenerator(cfgG, cfgMatchup, game),
                new RollHGenerator(cfgH, cfgMatchup, game),
                new RollIGenerator(cfgI, cfgMatchup, game),
                new RollJGenerator(cfgJ, cfgMatchup, game),
                new RollKGenerator(cfgK, cfgMatchup, game),
                new RollLGenerator(cfgL, game),
                new RollMGenerator(cfgM, cfgMatchup, game),
                new RollOffensiveFoulGenerator(cfgOffFoul),
                cfgMatchup, game, new SystemRng(seed));

            var governor = new Governor(resolver, game, cfgGov, cfgClock, new SystemRng(seed + 1), cfgEndHalf);
            var result   = governor.Run(firstState25);

            foreach (var r in result.Possessions)
            {
                if (r.ShootingFouls is null) continue;
                if (r.Defense == TeamSide.Home) totalHomeDefEvents += r.ShootingFouls.Count;
                else                             totalAwayDefEvents += r.ShootingFouls.Count;
            }

            var attributed = AttributeGame(result, game, seed);
            for (var i = 0; i < 10; i++) bsShFoul25[i] += attributed.ShFoul[i];
        }

        Console.WriteLine($" done ({Games25}/{Games25} completed).");
        Console.WriteLine();

        var creditedHome = bsShFoul25[0]+bsShFoul25[1]+bsShFoul25[2]+bsShFoul25[3]+bsShFoul25[4];
        var creditedAway = bsShFoul25[5]+bsShFoul25[6]+bsShFoul25[7]+bsShFoul25[8]+bsShFoul25[9];
        var globalCred   = creditedHome + creditedAway;
        var globalEvts   = totalHomeDefEvents + totalAwayDefEvents;

        Console.WriteLine($"    Shooting-foul events — Home defense: {totalHomeDefEvents}, Away defense: {totalAwayDefEvents}");
        Console.WriteLine($"    SFL credits — Home players: {creditedHome}, Away players: {creditedAway}");

        if (creditedHome == totalHomeDefEvents && creditedAway == totalAwayDefEvents)
            Console.WriteLine("  [OK] Side-specific reconciliation: creditedHome == totalHome AND creditedAway == totalAway");
        else
        {
            Console.WriteLine($"  [FAIL] Side-specific reconciliation: Home {creditedHome} != {totalHomeDefEvents} OR Away {creditedAway} != {totalAwayDefEvents}");
            checkOk = false;
        }

        if (globalCred == globalEvts)
            Console.WriteLine($"  [OK] Global completeness: total SFL == total events ({globalEvts})");
        else
        {
            Console.WriteLine($"  [FAIL] Global completeness: {globalCred} != {globalEvts}");
            checkOk = false;
        }

        var noZero = true;
        for (var i = 0; i < 10; i++)
        {
            if (bsShFoul25[i] == 0)
            {
                Console.WriteLine($"  [FAIL] Player index {i} (PlayerId {i+1}) has 0 SFL across {Games25} games");
                checkOk = false; noZero = false;
            }
        }
        if (noZero)
            Console.WriteLine($"  [OK] No-zero defender: all 10 players > 0 SFL across {Games25} games");

        Console.WriteLine();
        Console.WriteLine(checkOk
            ? "  Shooting-foul attribution check: PASSED"
            : "  Shooting-foul attribution check: FAILED (see [FAIL] lines above)");

        return checkOk;
    }

}
