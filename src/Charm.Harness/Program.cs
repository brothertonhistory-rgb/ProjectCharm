using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // dotnet run -- game  skips validation and plays one game.
        if (args.Length > 0 && args[0] == "game") { RunGame(configPath); return 0; }

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
        ok &= RollMReboundBatchCheck(cfg, cfgM, new RollMStubPieGenerator(cfgM), game, state);
        ok &= RollMContextSelectionCheck(cfg, cfgK, cfgJ, rollJGenerator, state);
        ok &= OffensiveReboundConvergenceCheck(cfg, state);
        ok &= RollCContextCheck(cfg, cfgC, rollCGenerator, state);
        ok &= RollCExpansionCheck(cfg, cfgC, rollCGenerator, state);
        ok &= EndOfHalfIntentBatchCheck(cfg, cfgEndOfHalf);
        ok &= GovernorLoopCheck(cfg, cfgD, cfgGov, cfgClock, cfgEndOfHalf);
        ok &= GameBoundaryCheck(configPath);
        ok &= Phase1RosterCheck(configPath);
        ok &= Phase2AttributeWiringCheck(configPath);
        ok &= Phase6MatchupWiringCheck(configPath);
        ok &= Phase7BlockDoorCheck(configPath);
        ok &= Phase8FoulDoorCheck(configPath);
        ok &= Phase9LocationDoorCheck(configPath);
        ok &= Phase10ReboundDoorCheck(configPath);
        ok &= Phase11FreeThrowReboundDoorCheck(configPath);
        ok &= Phase12DisruptionDoorCheck(configPath);
        ok &= Phase13TeamDisruptionDoorCheckRollB(configPath);
        ok &= Phase15PressFrequencyStandardCheck(configPath);
        ok &= Phase16PressBreakFastBreakCheck(configPath);
        ok &= Phase17UsageEfficiencyCheck(configPath);
        ok &= AttributionSanityCheck(configPath);            // Phase 24
        ok &= Phase25ShootingFoulAttributionCheck(configPath); // Phase 25
        ok &= Phase29HierarchyBiasCheck(configPath);           // Phase 29
        ok &= Phase30CoachingLayer2Check(configPath);          // Phase 30
        ok &= Phase31RebounderPickerCheck(configPath);         // Phase 31
        ok &= Phase32PutbackAttemptRateCheck(configPath);     // Phase 32
        ok &= Phase33TurnoverCommitterCheck(configPath);      // Phase 33
        ok &= Phase34TurnoverAttributionCheck(configPath);    // Phase 34
        ok &= Phase35DefensiveReboundCheck(configPath);       // Phase 35
        ok &= Phase36BlockerCheck(configPath);                 // Phase 36
        ok &= Phase39AssistCheck(configPath);                  // Phase 39
        ok &= Phase41HelpDefenseCheck(configPath);             // Phase 41
        ok &= Phase42ScreeningCheck(configPath);               // Phase 42

        ObservationRunV1(configPath);
        StressTestArchetypeRosters(configPath);
        Console.WriteLine(ok ? "\nALL CHECKS PASSED." : "\nCHECKS FAILED.");
        return ok ? 0 : 1;
    }

}
