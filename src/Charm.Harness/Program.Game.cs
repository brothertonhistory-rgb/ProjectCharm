using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // -----------------------------------------------------------------
    // dotnet run --project "..." -- game
    // Plays one full game using the real engine. Fresh seed every run.
    // -----------------------------------------------------------------
    private static void RunGame(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("=== PROJECT CHARM :: Game Simulation ===");
        Console.WriteLine();

        var cfg      = RollAConfig.Load(configPath);
        var cfgB     = RollBConfig.Load(configPath);
        var cfgC     = RollCConfig.Load(configPath);
        var cfgD     = RollDConfig.Load(configPath);
        var cfgE     = RollEConfig.Load(configPath);
        var cfgF     = RollFConfig.Load(configPath);
        var cfgG     = RollGConfig.Load(configPath);
        var cfgGov   = GovernorConfig.Load(configPath);
        var cfgClock = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);

        var seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        Console.WriteLine($"  seed: {seed}");
        Console.WriteLine();

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        SeatStartersFromConfig(game, configPath);       // seat real rosters (generators currently use stubs; seating future-proofs RunGame)

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
            new RollLGenerator(RollLConfig.Load(configPath), game),    // Phase 18: attribute-driven FT make%
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            new SystemRng(seed));

        var governorRng = new SystemRng(seed + 1);
        var governor = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndOfHalf);

        var first = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1);

        var result  = governor.Run(first);
        var records = result.Possessions;

        var htHome = records.Where(r => r.Half == 1 && r.Offense == TeamSide.Home).Sum(r => r.Points);
        var htAway = records.Where(r => r.Half == 1 && r.Offense == TeamSide.Away).Sum(r => r.Points);

        var homeScore = 0;
        var awayScore = 0;
        var lastHalf  = 0;

        foreach (var r in records)
        {
            if (r.Half != lastHalf)
            {
                if (lastHalf == 1)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  -- HALFTIME  Home {htHome,3}  -  Away {htAway,3} --");
                    Console.WriteLine();
                }
                lastHalf = r.Half;
                var label = r.Half == 1 ? "FIRST HALF" : "SECOND HALF";
                Console.WriteLine($"--- {label} ({cfgGov.HalfSeconds:N0}s) ---");
                Console.WriteLine($"  {"#",-4} {"Team",-5} {"Entry",-18} {"Outcome",-28} {"Pts",-4} {"Score",-20} Time");
                Console.WriteLine($"  {new string('-', 90)}");
            }

            if (r.Offense == TeamSide.Home) homeScore += r.Points;
            else                            awayScore  += r.Points;

            var pts     = r.Points > 0 ? $"+{r.Points}" : "  ";
            var score   = $"Home {homeScore,3} - Away {awayScore,3}";
            var offTeam = r.Offense == TeamSide.Home ? "Home" : "Away";
            var outcome = r.EndLabel.Length > 27 ? r.EndLabel[..27] : r.EndLabel;
            var intentMarker = r.EndOfHalfIntent switch
            {
                EndOfHalfIntent.HoldShootLast => " (held)",
                EndOfHalfIntent.ShootEarly    => " (early)",
                EndOfHalfIntent.NoShot        => " (no shot)",
                _                             => ""
            };

            Console.WriteLine($"  {r.Number,-4} {offTeam,-5} {r.Entry,-18} {outcome,-28} {pts,-4} {score,-20} {r.Elapsed:F0}s{intentMarker}");
        }

        Console.WriteLine();
        Console.WriteLine($"  ==========================================");
        Console.WriteLine($"  FINAL:  Home {game.HomeScore,3}  -  Away {game.AwayScore,3}");
        Console.WriteLine($"  ==========================================");
        Console.WriteLine();
        Console.WriteLine($"  {records.Count} possessions  |  APL {result.TotalSeconds / records.Count:F1}s  |  total {result.TotalSeconds:N0}s");
    }

}
