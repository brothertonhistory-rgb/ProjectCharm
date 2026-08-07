using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // dotnet run -- game  skips validation and plays one game.
        if (args.Length > 0 && args[0] == "game") { RunGame(configPath); return 0; }

        // dotnet run -- sizetest  runs the size experiment instrument (not in the validation suite).
        if (args.Length > 0 && args[0] == "sizetest") { RunSizeExperiment(configPath); return 0; }

        // dotnet run -- athtest  runs the athleticism ladder (make door; not in the validation suite).
        if (args.Length > 0 && args[0] == "athtest") { RunAthleticismExperiment(configPath); return 0; }

        // dotnet run -- deftest  runs the defender ladder (make door, Three; not in the validation suite).
        if (args.Length > 0 && args[0] == "deftest") { RunDefenderExperiment(configPath); return 0; }

        // dotnet run -- trtest  runs the transition ladder (run-or-not; not in the validation suite).
        if (args.Length > 0 && args[0] == "trtest") { RunTransitionExperiment(configPath); return 0; }

        // dotnet run -- pbtest  runs the putback conversion ladder (make rate, Rim; not in the validation suite).
        if (args.Length > 0 && args[0] == "pbtest") { RunPutbackConversionExperiment(configPath); return 0; }

        // dotnet run -- pbblocktest  runs the putback block ladder (block rate, Rim; not in the validation suite).
        if (args.Length > 0 && args[0] == "pbblocktest") { RunPutbackBlockExperiment(configPath); return 0; }

        // dotnet run -- sweep [path]  the general attribute-sweep findings bench (not in the validation
        // suite). Walks one rating on one Team A slot up its range (or runs named stress rows) through the
        // real engine and tabulates the outcome. configPath is the engine config.json (for the games);
        // args[1], if given, is the sweep config path — otherwise "sweep.json" resolves from the cwd.
        if (args.Length > 0 && args[0] == "sweep") { RunAttributeSweep(configPath, args.Length > 1 ? args[1] : null); return 0; }

        // dotnet run -- bench [path]  runs the player-generation lab bench (not in the validation suite).
        // configPath is the engine config.json (for the generators); args[1], if given, is the bench
        // config path — otherwise the bench resolves "bench.json" from the current directory.
        if (args.Length > 0 && args[0] == "bench") { RunBench(configPath, args.Length > 1 ? args[1] : null); return 0; }

        // dotnet run -- gen [path]  generates two programs' rosters from prestige, prints the
        // roster sheet, then sims the two starter cohorts on the bench (not in the validation
        // suite). configPath is the engine config.json (for the sim); args[1], if given, is the
        // gen config path — otherwise "gen.json" resolves from the current directory.
        if (args.Length > 0 && args[0] == "gen") { RunGen(configPath, args.Length > 1 ? args[1] : null); return 0; }

        // dotnet run -- world ...  the World Structure Pass 1 CLI: validate+report a world
        // file, convert the reference csvs to the stock world, or seed a generated world
        // from an existing world's structure. Returns before the validation suite; not in
        // the suite (the suite's Phase 53 block proves the same machinery in-memory).
        if (args.Length > 0 && args[0] == "world") { return RunWorld(args); }

        // dotnet run -- reachbench [world.json] [seed] [baseline.tsv]  S83's reach-term
        // stress bench: real pool cards through the live Roll H path, ceiling + guard floor.
        // Exploratory instrument, NOT in the validation suite — it asserts basketball target
        // values, which the page-only calibration principle keeps out of the suite.
        if (args.Length > 0 && args[0] == "reachbench") { RunReachBench(configPath, args); return 0; }

        // dotnet run -- divvy <world.json> <seed> [idA idB]  Roster Genesis Pass 1.5: builds
        // the national talent pool (10 x school count) and runs the prestige-weighted divvy
        // for every school in the world file — pool sheet, draft story, sample roster sheets,
        // variance readout; the two optional school ids run a smoke sim of drafted rosters.
        // Returns before the validation suite (the suite's Phase 54 block proves the same
        // machinery in-memory). configPath is the engine config.json (for the smoke sim).
        if (args.Length > 0 && args[0] == "divvy") { RunDivvy(configPath, args); return 0; }

        // dotnet run -- season <world.json> <seed>  World Structure Pass 2: the minimal
        // season loop. Regenerates every school's divvied roster (world + seed — nothing
        // persisted), builds the deterministic 30-game schedule (16 conference + 14
        // non-conference, 15 home / 15 away, neutral floors), plays every game through the
        // real engine, and prints the standings page: all schools ranked by W-L, the
        // prestige-band proof table, the overachievers with leaked talent named, and the
        // OT pulse. Returns before the validation suite (Phase 55 proves the same machinery
        // in-memory). configPath is the engine config.json (for the games).
        if (args.Length > 0 && args[0] == "season") { RunSeason(configPath, args); return 0; }

        // dotnet run -- calendar [year ...]  S91's printed year: a complete civil year with
        // correct weekdays and leap years, the season boundaries marked, Selection Sunday and
        // championship day named. With no arguments it prints the five years the design asks
        // for — a recent year, a leap year, 1900 (century, not leap), 2000 (century, leap) and
        // 1850. Returns before the validation suite and loads NO config and NO world: the
        // calendar must not be able to reach the season page (Phase 82 A10).
        if (args.Length > 0 && args[0] == "calendar") { RunCalendar(args); return 0; }

        // dotnet run -- geography <world.json>  S92's printed map: every place with its
        // jurisdiction, coordinate and tags; each school's nearest and furthest opponent;
        // the longest trip inside each conference; the league's longest and shortest pairs;
        // and the nearest campus to each authored event place. Returns before the validation
        // suite and loads NO config: the map must not be able to reach the season page
        // (Phase 83 A9). NOTHING CONSUMES THE MAP YET — no game is placed anywhere and there
        // is no home-court advantage at the end of S92.
        if (args.Length > 0 && args[0] == "geography") { return RunGeography(args); }

        var cfg = RollAConfig.Load(configPath);
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);
        var cfgI = RollIConfig.Load(configPath);
        var cfgJ = RollJConfig.Load(configPath);
        var cfgK = RollKConfig.Load(configPath);
        var cfgL = RollLConfig.Load(configPath);
        var cfgM = RollMConfig.Load(configPath);
        var cfgOffFoul = RollOffensiveFoulConfig.Load(configPath);
        var cfgGov = GovernorConfig.Load(configPath);
        var cfgClock = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // Roll A generator constructed below after SeatStartersFromConfig (Phase 14).
        // Roll B generator constructed below after SeatStartersFromConfig (Phase 13).
        var rollCGenerator = new RollCGenerator(cfgC);
        var rollDGenerator = new RollDGenerator(cfgD);
        // Roll E generator constructed below after game is created (Phase 15: needs GameState).
        // Roll F generator constructed below after SeatStartersFromConfig (Phase 12).
        // RollHGenerator, RollGGenerator, and RollIGenerator constructed below,
        // after game and cfgMatchup (need GameState and MatchupConfig).
        // RollKGenerator constructed below after SeatStartersFromConfig (Phase 32: needs game + cfgMatchup).
        var offensiveFoulGenerator = new RollOffensiveFoulGenerator(cfgOffFoul);

        // The half's foul tracker carries the config-driven bonus thresholds.
        var fouls = new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);
        var game = new GameState(fouls);  // arrow starts Off — first jump ball is the tip
        var cfgMatchup = MatchupConfig.Load(configPath);
        SeatStartersFromConfig(game, configPath);       // v2 fix: seat real rosters before generators
        var rollGGenerator = new RollGGenerator(cfgG, cfgMatchup, game);   // Phase 9: matchup-aware location
        var rollJGenerator = new RollJGenerator(cfgJ, cfgMatchup, game);   // Phase 28: real transition run decision
        var rollHGenerator = new RollHGenerator(cfgH, cfgMatchup, game);
        var rollIGenerator = new RollIGenerator(cfgI, cfgMatchup, game);   // Phase 10: matchup-aware rebounding
        var rollMGenerator = new RollMGenerator(cfgM, cfgMatchup, game);   // Phase 11: matchup-aware FT rebounding
        var rollLGenerator = new RollLGenerator(cfgL, game);               // Phase 18: attribute-driven FT make%
        var rollFGenerator = new RollFGenerator(cfgF, cfgMatchup, game);   // Phase 12: pressure-aware disruption
        var rollBGenerator = new RollBGenerator(cfgB, cfgMatchup, game);   // Phase 13: team-aggregate disruption
        var rollKGenerator = new RollKGenerator(cfgK, cfgMatchup, game);   // Phase 32: putback attempt rate
        var rollAGenerator = new RollAGenerator(cfg, cfgMatchup, game);    // Phase 14: full-court press disruption
        var rollEGenerator = new RollEGenerator(cfgE, game);               // Phase 15: attribute-driven halfcourt selection
        var cfgAttention   = AttentionConfig.Load(configPath);
        var attentionGenerator = new AttentionGenerator(cfgAttention, game); // Phase 27: defensive attention pie

        var resolver = new Resolver(
            rollAGenerator,
            cfg,
            rollBGenerator,
            rollCGenerator,
            cfgC,
            rollDGenerator,
            rollEGenerator,
            attentionGenerator,
            rollFGenerator,
            rollGGenerator,
            rollHGenerator,
            rollIGenerator,
            rollJGenerator,
            rollKGenerator,
            rollLGenerator,
            rollMGenerator,
            offensiveFoulGenerator,
            cfgMatchup,
            game,
            rng);

        var state = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        Console.WriteLine("=== Project Charm :: Roll A -> B -> C -> D -> E -> F -> G -> H -> I -> J -> K Chain ===\n");

        ShowSamples(cfg, cfgE, rollAGenerator, rollEGenerator, resolver, game, state, rng);
        var ok = BatchCheck(cfg, cfgB, new StubPieGenerator(cfg), new RollBStubPieGenerator(cfgB), resolver, state);
        ok &= RollCBatchCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollDFlavorBatchCheck(cfg, cfgD, rollDGenerator, state);
        ok &= RollDBonusRoutingCheck(cfgD, rollDGenerator, state);
        ok &= DefensiveFoulChargeCheck(cfgD, state);
        ok &= PhysicalitySignalCheck(cfgB, new RollBStubPieGenerator(cfgB), state);
        ok &= JumpBallCheck(cfg);
        ok &= SlotLayerCheck(game);
        ok &= RollESelectionBatchCheck(cfg, cfgE, cfgD, rollEGenerator, game, state);
        ok &= RollFActionBatchCheck(cfg, cfgF, new RollFStubPieGenerator(cfgF), state);
        ok &= RollFHandoffCheck(cfg, game, state);
        ok &= RollGLocationBatchCheck(cfg, cfgG, state);
        ok &= RollGHandoffCheck(cfg, state);
        ok &= RollHResolutionBatchCheck(cfg, cfgH, rollHGenerator, state);
        ok &= RollHHandoffCheck(cfg, state);
        ok &= RollIReboundBatchCheck(cfg, cfgI, new RollIStubPieGenerator(cfgI), game, state);
        ok &= RollIBonusForkCheck(cfg, cfgD, cfgI, new RollIStubPieGenerator(cfgI), state);
        ok &= RollIBlockReboundBatchCheck(cfg, cfgI, new RollIStubPieGenerator(cfgI), state);
        ok &= RollIBlockContextSelectionCheck(cfg, cfgI, state);
        ok &= RollJBatchCheck(cfg, cfgD, cfgJ, rollJGenerator, state);
        ok &= RollJBonusForkCheck(cfg, cfgD, cfgJ, state);
        ok &= RollJStealBatchCheck(cfg, cfgD, cfgJ, rollJGenerator, state);
        ok &= RollKReboundBatchCheck(cfg, cfgK, rollKGenerator, game, state);
        ok &= RollKPutbackPieCheck(cfg, cfgH, state);
        ok &= RollKBonusForkCheck(cfg, cfgD, cfgK, rollKGenerator, state);
        ok &= RollLFreeThrowCheck(cfg, state);
        ok &= ShootingFoulFeedsBonusCheck(cfg, state);
        ok &= RollMReboundBatchCheck(cfg, cfgM, new RollMStubPieGenerator(cfgM), game, state);
        ok &= RollMContextSelectionCheck(cfg, cfgK, cfgJ, rollJGenerator, state);
        ok &= OffensiveReboundConvergenceCheck(cfg, state);
        ok &= RollCContextCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollCExpansionCheck(cfg, cfgC, rollCGenerator, state);
        ok &= EndOfHalfIntentBatchCheck(cfg, cfgEndOfHalf);
        ok &= GovernorLoopCheck(cfg, cfgD, cfgGov, cfgClock, cfgEndOfHalf);
        ok &= GameBoundaryCheck(configPath);
        ok &= SuiteTimed("Phase1RosterCheck", () => Phase1RosterCheck(configPath));
        ok &= SuiteTimed("Phase2AttributeWiringCheck", () => Phase2AttributeWiringCheck(configPath));
        ok &= SuiteTimed("Phase6MatchupWiringCheck", () => Phase6MatchupWiringCheck(configPath));
        ok &= SuiteTimed("Phase7BlockDoorCheck", () => Phase7BlockDoorCheck(configPath));
        ok &= SuiteTimed("Phase8FoulDoorCheck", () => Phase8FoulDoorCheck(configPath));
        ok &= SuiteTimed("Phase9LocationDoorCheck", () => Phase9LocationDoorCheck(configPath));
        ok &= SuiteTimed("Phase10ReboundDoorCheck", () => Phase10ReboundDoorCheck(configPath));
        ok &= SuiteTimed("Phase11FreeThrowReboundDoorCheck", () => Phase11FreeThrowReboundDoorCheck(configPath));
        ok &= SuiteTimed("Phase12DisruptionDoorCheck", () => Phase12DisruptionDoorCheck(configPath));
        ok &= SuiteTimed("Phase13TeamDisruptionDoorCheckRollB", () => Phase13TeamDisruptionDoorCheckRollB(configPath));
        ok &= SuiteTimed("Phase15PressFrequencyStandardCheck", () => Phase15PressFrequencyStandardCheck(configPath));
        ok &= SuiteTimed("Phase16PressBreakFastBreakCheck", () => Phase16PressBreakFastBreakCheck(configPath));
        ok &= SuiteTimed("Phase17UsageEfficiencyCheck", () => Phase17UsageEfficiencyCheck(configPath));
        ok &= AttributionSanityCheck(configPath);            // Phase 24
        ok &= SuiteTimed("Phase25ShootingFoulAttributionCheck", () => Phase25ShootingFoulAttributionCheck(configPath)); // Phase 25
        ok &= SuiteTimed("Phase29HierarchyBiasCheck", () => Phase29HierarchyBiasCheck(configPath));           // Phase 29
        ok &= SuiteTimed("Phase30CoachingLayer2Check", () => Phase30CoachingLayer2Check(configPath));          // Phase 30
        ok &= SuiteTimed("Phase31RebounderPickerCheck", () => Phase31RebounderPickerCheck(configPath));         // Phase 31
        ok &= SuiteTimed("Phase32PutbackAttemptRateCheck", () => Phase32PutbackAttemptRateCheck(configPath));     // Phase 32
        ok &= SuiteTimed("Phase33TurnoverCommitterCheck", () => Phase33TurnoverCommitterCheck(configPath));      // Phase 33
        ok &= SuiteTimed("Phase34TurnoverAttributionCheck", () => Phase34TurnoverAttributionCheck(configPath));    // Phase 34
        ok &= SuiteTimed("Phase35DefensiveReboundCheck", () => Phase35DefensiveReboundCheck(configPath));       // Phase 35
        ok &= SuiteTimed("Phase36BlockerCheck", () => Phase36BlockerCheck(configPath));                 // Phase 36
        ok &= SuiteTimed("Phase39AssistCheck", () => Phase39AssistCheck(configPath));                  // Phase 39
        ok &= SuiteTimed("Phase41HelpDefenseCheck", () => Phase41HelpDefenseCheck(configPath));             // Phase 41
        ok &= SuiteTimed("Phase42ScreeningCheck", () => Phase42ScreeningCheck(configPath));               // Phase 42
        ok &= SuiteTimed("Phase43ReboundPhysicalWeightsCheck", () => Phase43ReboundPhysicalWeightsCheck(configPath));  // Phase 43
        ok &= SuiteTimed("Phase44OffBallDefenseCheck", () => Phase44OffBallDefenseCheck(configPath));          // Phase 44
        ok &= SuiteTimed("Phase45HustleCheck", () => Phase45HustleCheck(configPath));                  // Phase 45
        ok &= SuiteTimed("Phase46IndividualDenialCheck", () => Phase46IndividualDenialCheck(configPath));        // Phase 46
        ok &= PassingCompoundCheck(configPath);                // Phase 47
        ok &= FatigueMeterCheck(configPath);                   // Phase 48
        ok &= FatigueAthleticismCheck(configPath);             // Phase 49
        ok &= SuiteTimed("Phase50BasketballIqCheck", () => Phase50BasketballIqCheck(configPath));            // Phase 50
        ok &= FreeThrowFoulDrawCheck(configPath);              // Phase 51
        ok &= SuiteTimed("Phase52SubstitutionsCheck", () => Phase52SubstitutionsCheck(configPath));           // Phase 52
        ok &= SuiteTimed("Phase53WorldStructureCheck", () => Phase53WorldStructureCheck());                    // Phase 53
        ok &= SuiteTimed("Phase54DivvyCheck", () => Phase54DivvyCheck());                             // Phase 54
        ok &= SuiteTimed("Phase55SeasonCheck", () => Phase55SeasonCheck(configPath));                  // Phase 55
        ok &= SuiteTimed("Phase56DisplacementCheck", () => Phase56DisplacementCheck(configPath));            // Phase 56
        ok &= SuiteTimed("Phase57TurnoverClockCheck", () => Phase57TurnoverClockCheck(configPath, cfgC, game, state)); // Phase 57
        ok &= SuiteTimed("Phase58FastBreakDietCheck", () => Phase58FastBreakDietCheck(configPath));            // Phase 58
        ok &= SuiteTimed("Phase61HeightOverDefenderCheck", () => Phase61HeightOverDefenderCheck(configPath));       // Phase 61 (S55: height-over-defender make term, golden parity)
        ok &= SuiteTimed("Phase62UnforcedTurnoverCheck", () => Phase62UnforcedTurnoverCheck(configPath));         // Phase 62 (S56: unforced-turnover handling curve, golden parity)
        ok &= SuiteTimed("Phase63PostMovesInteriorCheck", () => Phase63PostMovesInteriorCheck(configPath));        // Phase 63 (S57: PostMoves interior self-creation — diet tilt + resistance + assist discount)
        ok &= SuiteTimed("Phase64StealFloorCheck", () => Phase64StealFloorCheck(configPath));               // Phase 64 (S58: live steal-forcing floor — athleticism mismatch + perimeter wingspan, golden parity)
        ok &= SuiteTimed("Phase65DriveGateCheck", () => Phase65DriveGateCheck(configPath));                // Phase 65 (S59: perimeter-defense drive gate — per-man rim-access wall in Roll G, golden parity)
        ok &= SuiteTimed("Phase66UsageReliefCheck", () => Phase66UsageReliefCheck(configPath));             // Phase 66 (S60: usage-relief bonus — the low-usage half of the usage↔efficiency curve, golden parity)
        ok &= SuiteTimed("Phase67DisciplineShaveCheck", () => Phase67DisciplineShaveCheck(configPath));          // Phase 67 (S61: Discipline make-% shave — small absolute per-man defensive-restraint reduction, golden parity)
        ok &= SuiteTimed("Phase68NonShootingFoulCheck", () => Phase68NonShootingFoulCheck(configPath));           // Phase 68 (S62: per-man non-shooting reach-in foul model — rate + committer, golden parity)
        ok &= SuiteTimed("Phase69GenPass3ReplayParityCheck", () => Phase69GenPass3ReplayParityCheck());                // Phase 69 (S69: Pass-3 two-plane budget generator port — fixture replay parity, STANDALONE)
        ok &= SuiteTimed("Phase70GenPass3LiveCheck", () => Phase70GenPass3LiveCheck());                        // Phase 70 (S69: Pass-3 live generator — sampler moments + exact invariants + ruled bands, STANDALONE)
        ok &= SuiteTimed("Phase71ConfigKeyNameParityCheck", () => Phase71ConfigKeyNameParityCheck(configPath));       // Phase 71 (S74: config KEY-NAME parity — registry completeness, bidirectional names, token kind, RollE binding; NOT value/semantic correctness)
        ok &= SuiteTimed("Phase72MinutesAllocatorCheck", () => Phase72MinutesAllocatorCheck(configPath));           // Phase 72 (S76: minutes allocator — per-position depth charts, residual control, bounded cascades)
        ok &= SuiteTimed("Phase73SeasonStatsCheck", () => Phase73SeasonStatsCheck(configPath));                // Phase 73 (S77: per-player season roll-up — conservation, two-path identity, minutes reconciliation, games played)
        ok &= SuiteTimed("Phase74BlockHelpCheck", () => Phase74BlockHelpCheck(configPath));                 // Phase 74 (S79: block help arm + contribution-based credit — golden parity, rate invariants, config guards)
        ok &= SuiteTimed("Phase75VerticalCheck", () => Phase75VerticalCheck(configPath));                  // Phase 75 (S81.2: the leap — isolation sweep, reach composite, three neutral points, picker strictness, config guards)
        ok &= SuiteTimed("Phase76TransitionReadoutCheck", () => Phase76TransitionReadoutCheck(configPath));         // Phase 76 (S85: the fast-break readout — entry/arm partition, three-way shot partition, nesting chains, event-scoping, press-born source)
        ok &= SuiteTimed("Phase77TransitionOpportunityCheck", () => Phase77TransitionOpportunityCheck(configPath));      // Phase 77 (S86: the transition opportunity score + coach bar — golden parity, neutral rule, conservation, monotonicity, overlap ruling, config guards)
        ok &= SuiteTimed("Phase78RealFoulsCheck", () => Phase78RealFoulsCheck(configPath));                 // Phase 78 (S87: real fouls — committer parity vs S62, totality, seat conservation, reset-proof reconciliation, five-and-out, escape hatch, negative control, config guards)
        ok &= SuiteTimed("Phase79TransitionDefenseCheck", () => Phase79TransitionDefenseCheck(configPath));         // Phase 79 (S88: who got back — the per-man transition-defence model; oracle parity, block credit/rate pairing, slot-number pairing, negative control, config guards). REGISTERED AT S89.1: S88 shipped this file but never wired it into the runner, so it had never executed once.
        ok &= SuiteTimed("Phase80IdentityCheck", () => Phase80IdentityCheck(configPath));                  // Phase 80 (S89: permanent identity + the history file — non-reuse across reload, type-surface enforcement, deterministic issuance, transport bijection, two-episode fixtures, domain guards, behavioural isolation with negative control, PoolId untouched, file lifecycle, legacy mode)
        ok &= SuiteTimed("Phase81GameLogCheck", () => Phase81GameLogCheck(configPath));                  // Phase 81 (S90: per-game retention — conservation from disk, the 26-man mutation bound with a real negative control, strict reader, writer state machine, roster round-trip including men who never played, v1->v2 migration through the production writer)
        ok &= SuiteTimed("Phase82CalendarCheck", () => Phase82CalendarCheck());                           // Phase 82 (S91: the calendar — proleptic Gregorian civil dates across 0001-9999 against independently-sourced weekdays, the exact leap rule, Selection Sunday and the ten D1 tournament dates as REFERENCE DATA, one continuous legal span from Nov 1 to championship Monday with a no-gaps walk and a negative control that rebuilds r3's gated version, overlapping periods, the three-way season lookup at both year edges, wall-clock and culture purity, renderer invariance). STANDALONE — no config, no world, no basketball.
        ok &= SuiteTimed("Phase83GeographyCheck", () => Phase83GeographyCheck());                          // Phase 83 (S92: the map — places, great-circle miles against a golden table whose MODEL is pinned, the metric properties, schema v2 with canonical BYTES, hosting as an explicit tagged value, worldwide coordinate bounds, and a planar negative control that must FAIL the long trips). STANDALONE — no config and no basketball.
        ok &= SuiteTimed("Phase84ConferenceSlateCheck", () => Phase84ConferenceSlateCheck(configPath));          // Phase 84 (S93: the conference slate — the authored game count, the k/q/r histogram per school, rivalry placement, both dormancy kinds, the zero-game league, exactly even home/away by construction, the four verdicts kept apart, and ★ A9: pre-fixed venues honoured by the flow, which a Eulerian walk cannot do)
        ok &= SuiteTimed("Phase85ConferenceDatesCheck", () => Phase85ConferenceDatesCheck(configPath));          // Phase 85 (S94: conference dates — loose windows from three authored numbers, the Mon-Sun week cap with both negative controls, exact weekly totals heavier-latest, the complete-week wall rule, rotation-spaced rematches at zero same-quarter collisions, the atomic-week discriminator, the year as a dial, seven named refusals, oracle golden parity EXACT)
        ok &= SuiteTimed("Phase86HomeCourtCheck", () => Phase86HomeCourtCheck(configPath));                // Phase 86 (S95: home court — the road penalty. Zero-path identity against a golden captured from the pre-S95 tree, neutral isolation, independently-derived 23/17 classification, an exhaustive clone sweep with Athleticism pinned EXACTLY equal (skills, not bodies), bench-inclusive side transformation with non-accumulation, the home-passthrough/away-transform asymmetry, determinism plus a discriminator that the dial does something, coverage, and the retirement of the dead road free-throw seam). The 59% is NEVER asserted — page-only calibration.
        ok &= SuiteTimed("Phase87SeasonMemoryCheck", () => Phase87SeasonMemoryCheck(configPath));            // Phase 87 (S96: host memory — a season reads season N-1's retained log, found by arithmetic and never by enumeration, and inverts every single-meeting host. Zero-path identity against a pre-S96 golden, the five statuses asserted as VALUES with their state table, the flip proven at four separated layers each with a control, R3 intact, the d/2 theorem over every playing league, parity change dropped rather than refused, the career lifecycle on real disk where an unlogged or damaged year must NOT reach an older log, the peek proven honest against a real reservation, and isolation proven behaviourally — two careers that played different basketball remember identical hosts)
        ok &= SuiteTimed("Phase88MteCheck", () => Phase88MteCheck(configPath));                     // Phase 88 (S97: the MTE pool — a world may author bracketed early-season tournaments; each season draws activation, seats every active field in tier order, and records it permanently. Zero-path identity against PRE-S97 goldens, load refusals by name including retired v4, the two absolutes held at every fallback level, four-year arithmetic at both boundaries, tier order beating id order, authored slot order load-bearing, a bounded 64-seed determinism sweep with the persistence endpoints, isolation on the COMPLETE per-game results, and the transaction proven negatively at both refusals). NO field composition is ever asserted — page-only calibration.

        ok &= SuiteTimed("Phase89BracketsCheck", () => Phase89BracketsCheck(configPath));                // Phase 89 (S98: the brackets play — each active complete field seeded by prestige with the id tie-break, played to a full placement on the window's own nights, EXECUTED LAST AND DATED FIRST so every conference game keeps its seed. The pre-S98 conference golden reproduced WITH the brackets on (the discriminating arm), the route tables walked literally down all 4,096 result paths, every team playing every round, the neutral floor asserted on the PREPARED sides with a hosted discriminator, the reservation ledger with dormant and short events holding nothing, the log's kind byte on the block and the Kind on the game asserted separately, host memory's blindness with a CONSTRUCTED leak that would otherwise close a live residual, the record's three refusals plus atomicity through an injected rename, and win percentage with a 3-1 over 4-2 discriminator). Page-only calibration holds — no field, finish or basketball value is ever asserted.

        ok &= SuiteTimed("Phase90RotationCheck", () => Phase90RotationCheck(configPath));                // Phase 90 (S99: who you play twice — the extra meeting rotates by whose turn it is, read from up to eight seasons of retained logs. Zero-path identity against PRE-S97 goldens on all three ways of having no facts, the pair ages asserted as arithmetic including a hole that must not compress time, twelve-season opponent coverage on a compact rig and on the Big East's shape against constants set by an independent oracle, the frozen-schedule negative control that the coverage predicate must REJECT, the two consumers failing differently on one damaged career, the relaxation loop proven to have run through its own instrumentation, rivalries surviving relaxation to an empty preferred set, coexistence with both sources of venue truth, and determinism over a bundle that includes the page line). Page-only calibration holds — no basketball value and no measured league constant is asserted.

        ok &= SuiteTimed("Phase91HostDebtCheck", () => Phase91HostDebtCheck(configPath));                // Phase 91 (S100: who is owed the home game — the alternation stops looking one year back and counts residual home games across the same window the rotation already reads, so a home-and-home year no longer erases the debt. Zero-path identity on all three states against the PRE-S98 goldens, ★ the single/home-and-home/single discriminator with the pre-S100 one-hop rule run as a negative control on the SAME two logs, the balance as arithmetic including a hole that must not compress time and 2-0 across both a hole and a doubled year, R3 over twelve seasons, ★ surrender priority asserted directly with the weakest claim proven to be the one that pays, ★ long-run balance measured from the games that PLAYED against an isolating rotation-on/window-1 control, ★ the debt proven to be read from what happened rather than what was intended, determinism over a bundle including both page lines, the rotation's coverage undisturbed, and O-90 measured and reported). Page-only calibration holds — the imbalance bound is measured from the fixture and reported beside its control, and no basketball value is asserted.

        ok &= SuiteTimed("Phase92NonConferenceCheck", () => Phase92NonConferenceCheck(configPath));           // Phase 92 (S101: classes and requests — every school gets a class read from prestige every season with its conference tier as a floor at EVERY tier, and a target November in games: home set by the class band positioned by prestige rank, neutral allowed, road the REMAINDER so the acceptance measure stays a measurement. Nothing is scheduled. Partition across stock and all six fixtures, the floor table and class order asserted directly, three-point synthetic monotonicity with the floor's negative control, seed-independence on an eventless world plus classes-only on stock, exact conservation, the clamp chain's invariants with zero compressed/impossible on committed worlds, the 31/3 exemption as set membership against the seating both directions, lopsided worlds reporting instead of throwing, ★ full-bundle zero-path identity against pre-S101 goldens including the results+possessions fingerprint, total reconciliation, and the rank formula asserted to exact sequences with the id tie-break). Page-only calibration holds — no class count, average, or gap value is asserted; the national balance is measured and printed, and the class curve stays open for Emmett to settle by reading the page.

        ok &= SuiteTimed("Phase93MatchingCheck", () => Phase93MatchingCheck(configPath));                // Phase 93 (S102: the matching — every school's November pairs. Who plays whom, who hosts, which pairs are neutral; NO site and NO night, which is arc session 3. A home request names the kind of opponent it wants by that opponent's PRESTIGE (Easy under 25, Working 25-54, Decent 55-79, Name 80+, plus Selling's unrestricted ANY) at Emmett's ruled mixes, and a request that finds nobody spills UP one tier at a time and never down. The top of the country picks first and everyone else adapts around it (R4); leftover neutral tokens convert to ordinary home games; whatever road games remain pair off with the lower school hosting (C-37, bottom hosts bottom); and any token still short is closed by one bounded +1 game whose partner hosts and is used at most once. Input identity AND immutability against the season's own S101 report with a mutated-report discriminator, pair structure, hard legality with the no-request schools held out of every phase including the terminal pool, determinism, constructed seed-independence, the allocation sequences literally including the lower-bucket tie, spill direction and count, neutral conversion accounting, ★ full-bundle zero-path identity against the pre-S102 goldens including tournament games and results+possessions, ★ BOTH conservation identities nationally with every pairing on exactly two ledgers, the filler host rule asserted in its own words with no filler host over target, terminal bounds, completes-or-reports on a constructed unmatchable world, and ★ pair-for-pair ORDERED oracle parity against tools/matching_golden.json with the golden's embedded S101 report asserted to be the live one). Page-only calibration holds — no distance, spill, pair count or class trip median is asserted to a basketball value anywhere; the geographic tilt is measured and printed by the travelling school's class, and the bottom's long trips are a printed finding, not a tuned one.

        ok &= SuiteTimed("Phase94ContractsCheck", () => Phase94ContractsCheck(configPath));               // Phase 94 (S103: contracts and the non-conference log — the engine keeps promises it cannot yet make. A contract is two schools, an EXPLICIT executor, an ordered leg list with stable ids, and a window; it persists in the season record (format v2, with the reader still accepting v1 so no existing career loses its tournament memory), is exercised before anything else touches a school's slate, and dies by exactly two rules that both fail closed. Oracle parity on the window state machine (tools/contracts_golden.json, 97 trajectories, all integers, exact — forced iff games==window, decrement at ROLLOVER after the decision), the specific-leg choice proven as a PERSISTENCE test through the real record, cross-season inheritance at the real reader/writer boundary with the older record deleted, nine authoring refusals by name plus the word-level parse refusals, the neutral fixture (an executor with no host at all) and the equal-host executor round-trip, discover/reserve/validate/commit with forced-over-optional priority and the canonical ascending-ContractId order proven enumeration-independent, forced overload as ONE hard failure with the collection frozen un-decremented, conference mates terminating BEFORE exercise, a damaged record reported as a COLLECTION-level loss naming no pairing, v1-reads-as-empty migration, the ruled charging chains including the road-less power school paying a HOME date, and the pairing log carried beside the contracts — Contracted and Matched entries both, normalised. NOTHING in the engine signs a contract; every contract here is fixture-authored). Page-only calibration holds — no contract count, split or distance is ever asserted.
        ok &= SuiteTimed("Phase95ShowcasesCheck", () => Phase95ShowcasesCheck(configPath));               // Phase 95 (S104: showcases — the event pool learns a second kind. A showcase invites four schools out for TWO STAND-ALONE GAMES IN ONE DAY: no bracket, no advancement, no placement, no champion. The authored shape and its refusals by name, the two per-kind walls (R25 — one tournament AND one showcase, never two of either), ★ the OVERLAPPING-WINDOW fixture with its non-overlapping negative control, ★ the R30 RELEASE fixture (a short showcase creates zero pairings, consumes nobody, burns no four-year clock, and its stranded invitees seat in a later showcase the same season), the kind owning the play path so a field of four can never route into BracketRoutes4, roles from STORED SEAT NUMBERS with a reversed-list control, ★ A1's tournament-only exemption with the worked tournament-plus-showcase November asserted, the neutral→road→home charge and its priority against a contract leg, the radius ladder with its exact inclusive boundary and orthogonal provenance words, and ★ THE ZERO PATH BY SUBTRACTION — the stock world with its showcases removed reproducing the pre-S104 event-games fingerprint exactly, which is what proves everything that moved was moved by showcases). Page-only calibration holds — no active count, seat quality, fallback or radius rate, distance or participation count is asserted anywhere.
        SuiteTimed("ObservationRunV1", () => { ObservationRunV1(configPath); return true; });
        SuiteTimed("StressTestArchetypeRosters", () => { StressTestArchetypeRosters(configPath); return true; });
        SuiteTimingReport();
        Console.WriteLine(ok ? "\nALL CHECKS PASSED." : "\nCHECKS FAILED.");
        return ok ? 0 : 1;
    }


    // ── Suite timing (S104.1) ───────────────────────────────────────────────────────
    //  ★ MEASURE BEFORE OPTIMISING. O-93 was opened on a HUNCH — that four phases each
    //  replaying the same stock season were the cost that compounds. Nothing in the
    //  harness had ever timed itself, so that hunch was never a number. This is the
    //  number. It is permanent rather than scaffolding because the useful moment is the
    //  session AFTER a phase gets expensive, not this one.
    //
    //  Wall-clock, single-threaded, whole-phase. Deliberately coarse: the question is
    //  "which phases dominate", and a profiler's precision would not change any answer
    //  this table is asked for.

    private sealed record SuiteTiming(string Name, long Ms);

    private static readonly List<SuiteTiming> SuiteTimings = new();

    /// <summary>Run one phase, record what it cost, hand its verdict straight back.
    /// Never swallows an exception — a phase that throws must still take the run down,
    /// exactly as it did before this wrapper existed.</summary>
    private static bool SuiteTimed(string name, Func<bool> phase)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { return phase(); }
        finally { sw.Stop(); SuiteTimings.Add(new SuiteTiming(name, sw.ElapsedMilliseconds)); }
    }

    private static void SuiteTimingReport()
    {
        if (SuiteTimings.Count == 0) return;
        var total = SuiteTimings.Sum(t => t.Ms);
        if (total <= 0) return;

        Console.WriteLine();
        Console.WriteLine("=== SUITE TIMING — where the run actually goes ===");
        Console.WriteLine($"  {SuiteTimings.Count} timed sections, {total / 1000.0:F1}s total");
        Console.WriteLine();
        Console.WriteLine($"  {"section",-42}{"seconds",9}{"share",8}   cumulative");

        var cum = 0L;
        foreach (var t in SuiteTimings.OrderByDescending(t => t.Ms).Take(15))
        {
            cum += t.Ms;
            Console.WriteLine($"  {t.Name,-42}{t.Ms / 1000.0,9:F1}{100.0 * t.Ms / total,7:F1}%{100.0 * cum / total,12:F1}%");
        }

        var shown = SuiteTimings.OrderByDescending(t => t.Ms).Take(15).Sum(t => t.Ms);
        var rest = SuiteTimings.Count - Math.Min(15, SuiteTimings.Count);
        if (rest > 0)
            Console.WriteLine($"  {$"({rest} remaining sections)",-42}{(total - shown) / 1000.0,9:F1}{100.0 * (total - shown) / total,7:F1}%");
        Console.WriteLine("=== END SUITE TIMING ===");
    }

}
