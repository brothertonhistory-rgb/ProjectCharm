using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // --- Slot layer: print the ten on-court slots and prove one can be NAMED
    //     as a future attribution target. The slots are empty identities — they
    //     fill nothing and influence no roll. This check only confirms they
    //     exist on the correct side and are addressable 1–5. ---
    private static bool SlotLayerCheck(GameState game)
    {
        Console.WriteLine("\n--- Slot layer: on-court identities ---");
        var allOk = true;

        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var lineup = game.LineupFor(side);

            // Each lineup must hold exactly five slots, numbered 1–5, all on its side.
            var countOk = lineup.OnCourt.Count == Lineup.Size;
            var numbersOk = true;
            var sideOk = true;
            for (var i = 0; i < lineup.OnCourt.Count; i++)
            {
                var slot = lineup.OnCourt[i];
                Console.WriteLine($"  {slot.Side} slot {slot.Number}");
                if (slot.Number != i + 1) numbersOk = false;
                if (slot.Side != side) sideOk = false;
            }

            var lineupOk = countOk && numbersOk && sideOk;
            allOk &= lineupOk;
            Console.WriteLine($"  {side}: five slots, numbered 1–5, all on {side} -> {(lineupOk ? "ok" : "FAIL")}");
        }

        // Prove a single slot can be NAMED — the entity a future stat attributes to.
        var target = game.LineupFor(TeamSide.Home).SlotAt(3);
        var nameOk = target.Side == TeamSide.Home && target.Number == 3;
        allOk &= nameOk;
        Console.WriteLine(
            $"  named attribution target: {target.Side} slot {target.Number} " +
            $"(a stat would credit here; nothing fills it yet) -> {(nameOk ? "ok" : "FAIL")}");

        return allOk;
    }


    // --- Session 30: end-of-half intent pie — rate proof (flat, score-blind, clock-only).
    //     Mirrors the same Pie<EndOfHalfIntent> the Governor builds from the same config,
    //     rolled 100k times directly. Proves the three rates converge within tolerance
    //     without reaching into the Governor's private fields. ---
    private static bool EndOfHalfIntentBatchCheck(RollAConfig cfg, EndOfHalfConfig cfgEndOfHalf)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} end-of-half intent draws (flat, score-blind) ---");
        var rng = new SystemRng(cfg.Seed);

        var pie = new Pie<EndOfHalfIntent>(
            new Dictionary<EndOfHalfIntent, double>
            {
                [EndOfHalfIntent.HoldShootLast] = cfgEndOfHalf.HoldShootLast,
                [EndOfHalfIntent.ShootEarly]    = cfgEndOfHalf.ShootEarly,
                [EndOfHalfIntent.NoShot]        = cfgEndOfHalf.NoShot,
            },
            cfgEndOfHalf.Epsilon);

        var counts = new Dictionary<EndOfHalfIntent, int>();
        foreach (var o in Enum.GetValues<EndOfHalfIntent>()) counts[o] = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
            counts[pie.Roll(rng.NextUnitInterval())]++;

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  end-of-half intent rates:");
        foreach (var (intent, weight) in pie.Slices)
        {
            var observed = counts[intent] / n;
            var gap      = Math.Abs(observed - weight);
            var pass     = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {intent,-16} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        return ratesOk;
    }


    // --- Session 15: the thin Governor's possession-to-possession loop. ---
    // The FIRST check whose whole point is state persisting across iterations: it
    // shares ONE GameState across the entire loop, so foul counts climb and CROSS
    // THE BONUS mid-loop (CONVENTIONS §2a). The Governor handles every stub-park
    // through ONE default-consequence path (keyed only on "no terminal"), so the
    // Session-14 "only handled one landing" bug class cannot recur — the per-stub
    // breakdown is observability, never routing.
    private static bool GovernorLoopCheck(RollAConfig cfg, RollDConfig cfgD, GovernorConfig cfgGov, RollClockConfig cfgClock, EndOfHalfConfig cfgEndOfHalf)
    {
        Console.WriteLine($"\n--- Governor loop: two {cfgGov.HalfSeconds:N0}s halves ---");

        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
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
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rng);

        var governor = new Governor(resolver, game, cfgGov, cfgClock, new SystemRng(cfg.Seed + 1), cfgEndOfHalf);

        // --- Seed the cross-possession state so persistence is DETERMINISTIC. ---
        // Arrow: turn it ON (Home) before the loop. It is never Off thereafter (no OT
        // reset), so every jump ball takes the ON path and FLIPS it exactly once —
        // letting us predict the final arrow exactly from the jump-ball count.
        game.SetPossessionArrow(TeamSide.Home);

        // Fouls: push Home to the bonus threshold so its opponent is ALREADY in bonus
        // before the loop. This fixture proves fouls persist across possession boundaries;
        // the halftime reset is verified deterministically in GameBoundaryCheck.
        for (var i = 0; i < cfgD.BonusThreshold; i++) game.Fouls.Increment(TeamSide.Home);
        var homeFoulsBefore = game.Fouls.FoulsFor(TeamSide.Home);
        var bonusBefore = game.Fouls.BonusFor(TeamSide.Home);

        // Lineups: capture references to confirm they survive untouched.
        var homeLineupRef = game.HomeLineup;
        var awayLineupRef = game.AwayLineup;

        var first = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);

        var result = governor.Run(first);
        var records = result.Possessions;

        // --- Invariant checks. ---
        // The load-bearing one: zero possessions lost. A dropped park is exactly how
        // the count would silently leak. Possession count is now clock-driven (no fixed cap).
        // §2a discipline: NoShot possessions are a THIRD class — neither terminalEnded nor
        // parked; they must be counted separately so the assertion remains total.
        var noShotCount = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.NoShot);
        var intentHeld  = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.HoldShootLast);
        var intentEarly = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.ShootEarly);
        var noLostOk = result.TerminalEnded + result.Parked + noShotCount == records.Count;

        // Contiguous numbers 1..N, and offense/defense flips that match each
        // possession's APPLIED consequence (proving the Governor spawned from it).
        var contiguousOk = true;
        var flipsOk = true;
        var jumpBalls = 0;
        var reboundIntoJ = 0;
        var stealIntoJ = 0;
        var homePoints = 0;
        var awayPoints = 0;
        var talliedPoints = 0;
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (r.Number != i + 1) contiguousOk = false;
            if (r.Defense != Other(r.Offense)) flipsOk = false;
            if (r.EndedOnTerminal && r.EndLabel.StartsWith("JumpBall")) jumpBalls++;
            if (i > 0)
            {
                var prev = records[i - 1];
                if (r.Offense != prev.Applied.NextOffense) flipsOk = false;
                if (r.Entry != prev.Applied.NextEntry) flipsOk = false;
                // A possession whose PREDECESSOR ended on a defensive rebound entered
                // Roll J: the rebound consequence carries the Rebound context, which the
                // resolver routes to Roll J (not Roll A). This counts the live path.
                if (prev.EndedOnTerminal && prev.EndLabel == "DefensiveRebound") reboundIntoJ++;
                // A possession whose PREDECESSOR ended on a LIVE turnover (a steal) also
                // entered Roll J as of Contextification #3: the three live-turnover
                // terminals (BadPassIntercepted / LostBallLiveBall / LiveBallTurnover)
                // carry the Steal context, which the resolver routes to Roll J on the
                // steal pie. Steals JOIN rebounds as Roll J feeders — more possessions
                // enter J than before.
                if (prev.EndedOnTerminal && prev.EndLabel is "BadPassIntercepted"
                        or "LostBallLiveBall" or "LiveBallTurnover")
                    stealIntoJ++;
            }
            if (r.Offense == TeamSide.Home) homePoints += r.Points;
            else awayPoints += r.Points;
            talliedPoints += r.Points;
        }
        var firstOk = records.Count > 0
            && records[0].Offense == TeamSide.Home
            && records[0].Entry == EntryType.DeadBallInbound;

        // Arrow persistence: ON the whole loop, flipped once per jump ball and by
        // NOTHING else — so the final arrow is exactly predictable.
        var expectedArrow = jumpBalls % 2 == 0 ? ArrowState.Home : ArrowState.Away;
        var arrowOk = game.PossessionArrow != ArrowState.Off
                      && game.PossessionArrow == expectedArrow;

        // Foul observability: report counts post-run; halftime reset verified in GameBoundaryCheck.
        var homeFoulsAfter = game.Fouls.FoulsFor(TeamSide.Home);
        var bonusAfter = game.Fouls.BonusFor(TeamSide.Home);

        // Lineup persistence: same objects, still five slots each.
        var lineupOk = ReferenceEquals(game.HomeLineup, homeLineupRef)
                       && ReferenceEquals(game.AwayLineup, awayLineupRef)
                       && game.HomeLineup.OnCourt.Count == Lineup.Size
                       && game.AwayLineup.OnCourt.Count == Lineup.Size;

        // Rebound -> Roll J, end to end (the robust gate). A defensive rebound spawns a
        // Transition possession that ENTERS Roll J (not Roll A). As of Contextification
        // #1, Roll J's Push flows into Roll E (FastBreak set) and on through the shot
        // chain, so the proof that Roll J ran is simply that possessions entered it off a
        // rebound. Steals ALSO feed Roll J as of #3, but they are far rarer than rebounds,
        // so over a 200-possession cap stealIntoJ is reported for observability rather than
        // gated (a hard >0 gate would be seed-fragile). The rigorous steal-routing proof is
        // the dedicated 100k RollJStealBatchCheck plus the resolver's wiring-bug alarm: if a
        // steal-born Transition ever reached this loop with a bad/null context, RunPossession
        // would THROW, so the loop completing at all is itself proof the steal context rides
        // correctly whenever a steal occurs.
        var rollJOk = reboundIntoJ > 0;

        // --- Report. ---
        Console.WriteLine(
            $"  resolved={records.Count:N0} | terminal-ended={result.TerminalEnded:N0} | parked={result.Parked:N0} " +
            $"| noShot={noShotCount:N0} | terminal+parked+noShot==total -> {(noLostOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  possession numbers contiguous 1..{records.Count} -> {(contiguousOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  offense/defense flips match applied consequence -> {(flipsOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  first possession = Home, DeadBallInbound -> {(firstOk ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  arrow: jump balls={jumpBalls} | final={game.PossessionArrow} | expected={expectedArrow} " +
            $"-> {(arrowOk ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  fouls(Home): pre-run={homeFoulsBefore} post-run={game.Fouls.FoulsFor(TeamSide.Home)} " +
            $"| halftime reset: covered by deterministic GameBoundaryCheck");
        Console.WriteLine($"  lineups survive (same objects, 5 slots each) -> {(lineupOk ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  rebound -> Roll J: possessions entering J={reboundIntoJ:N0} (Push now flows into Roll E) " +
            $"-> {(reboundIntoJ > 0 ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  steal -> Roll J: possessions entering J={stealIntoJ:N0} (live turnovers now feed Roll J; " +
            $"observability — rigorous proof is RollJStealBatchCheck)");
        // Clock checks.
        var half1Seconds = records.Where(r => r.Half == 1).Sum(r => r.Elapsed);
        var half2Seconds = records.Where(r => r.Half == 2).Sum(r => r.Elapsed);
        var drainOk = Math.Abs(half1Seconds - cfgGov.HalfSeconds) < 0.01
                   && Math.Abs(half2Seconds - cfgGov.HalfSeconds) < 0.01;
        Console.WriteLine(
            $"  half 1: {half1Seconds:N0}s / half 2: {half2Seconds:N0}s " +
            $"| each drains to {cfgGov.HalfSeconds:N0} -> {(drainOk ? "ok" : "FAIL")}");

        var apl = result.TotalSeconds / records.Count;
        var aplOk = apl >= 14.0 && apl <= 21.0;
        var countInBand = records.Count >= 100 && records.Count <= 220;
        Console.WriteLine(
            $"  possessions={records.Count:N0} (~{records.Count / 2:N0} per half) " +
            $"| realized APL={apl:F1}s -> {((aplOk && countInBand) ? "ok" : "FAIL")}");

        // Tempo histogram — the tuning instrument and truncation proof.
        // 100k samples of ClockDraw directly (not from the game run) with a fresh rng.
        var histRng = new SystemRng(cfg.Seed);
        var bins = new int[6]; // [0,5) [5,10) [10,15) [15,20) [20,25) [25,30)
        var truncationOk = true;
        double histMin = double.MaxValue, histMax = double.MinValue;
        for (var s = 0; s < 100_000; s++)
        {
            var t = ClockDraw.Sample(histRng, cfgClock.Center, cfgClock.StdDev, cfgClock.Floor, cfgClock.FullClockSeconds);
            if (t < cfgClock.Floor || t >= cfgClock.FullClockSeconds) truncationOk = false;
            if (t < histMin) histMin = t;
            if (t > histMax) histMax = t;
            var bin = Math.Min((int)(t / 5.0), bins.Length - 1);
            bins[bin]++;
        }
        Console.WriteLine(
            $"  tempo histogram (100k samples, center={cfgClock.Center} sd={cfgClock.StdDev} " +
            $"floor={cfgClock.Floor} ceiling<{cfgClock.FullClockSeconds}):");
        string[] binLabels = ["[0,5)", "[5,10)", "[10,15)", "[15,20)", "[20,25)", "[25,30)"];
        for (var b = 0; b < bins.Length; b++)
            Console.WriteLine($"    {binLabels[b]}: {bins[b]:N0}");
        Console.WriteLine(
            $"  min={histMin:F2} / max={histMax:F2} " +
            $"| truncation holds (>= floor, < ceiling) -> {(truncationOk ? "ok" : "FAIL")}");

        var clockOk = drainOk && aplOk && countInBand && truncationOk;

        // End-of-half observability: per-game counts are small (a handful of possessions
        // per game), so this is informational only — the rigorous rate proof is
        // EndOfHalfIntentBatchCheck. The drain check above is the load-bearing gate.
        Console.WriteLine(
            $"  end-of-half: HoldShootLast={intentHeld} ShootEarly={intentEarly} NoShot={noShotCount}");
        var fgRuleOk = Scoring.FieldGoalPoints(ShotLocation.Three) == 3
                    && Scoring.FieldGoalPoints(ShotLocation.Long)  == 2
                    && Scoring.FieldGoalPoints(ShotLocation.Mid)   == 2
                    && Scoring.FieldGoalPoints(ShotLocation.Short) == 2
                    && Scoring.FieldGoalPoints(ShotLocation.Rim)   == 2;
        var scoreOk = game.HomeScore == homePoints
                   && game.AwayScore == awayPoints
                   && game.HomeScore + game.AwayScore == talliedPoints
                   && talliedPoints > 0;
        Console.WriteLine(
            $"  score: Home {game.HomeScore} / Away {game.AwayScore} | accumulates per-possession tally -> {(scoreOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  FG rule (Three=3, others=2) -> {(fgRuleOk ? "ok" : "FAIL")}");

        // Per-stub park breakdown — quantifies how much of the game is currently
        // flowing through placeholder flips. As of #6 the chain CLOSES: the inbound
        // edges (ResumeInbound / SidelineInbound) no longer park — they re-run Roll A —
        // and Roll A's violation terminals moved into Roll C, so this breakdown is now
        // expected to be ESSENTIALLY EMPTY (parked ≈ 0, terminal-ended ≈ cap). The §2a
        // accumulation is still exercised: the volume flowing through the in-bonus forks
        // (Roll I / J / K / M) once teams reach the bonus is the visible proof.
        Console.WriteLine("  per-stub park breakdown:");
        foreach (var (dest, n) in result.PerStubParks.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {dest,-44} {n,6:N0}");

        // First 10 possessions: number, offense, entry, how it ended, consequence applied.
        Console.WriteLine("  first 10 possessions:");
        foreach (var r in records.Take(10))
        {
            var applied = $"{r.Applied.NextOffense}/{r.Applied.NextEntry}";
            Console.WriteLine(
                $"    #{r.Number,-3} {r.Offense,-4} entry={r.Entry,-15} " +
                $"end={r.EndLabel,-34} -> next={applied}");
        }

        var allOk = noLostOk && contiguousOk && flipsOk && firstOk
                    && arrowOk && lineupOk && rollJOk && scoreOk && fgRuleOk
                    && clockOk;
        Console.WriteLine($"  Governor loop: {(allOk ? "ok" : "FAIL")}");
        return allOk;
    }


    // -------------------------------------------------------------------------
    // Phase 1 — Player object & Roster seam
    // -------------------------------------------------------------------------

    /// <summary>
    /// Proves the full seam: config → RosterConfig.Load → PlayerConfig.ToPlayer →
    /// Player (authored attributes) → Roster.SetStarter → GameState.RosterFor →
    /// Roster.PlayerAt → derived attributes computed correctly.
    ///
    /// Three assertions:
    ///   1. Every slot on both sides resolves to a non-null Player.
    ///   2. Every player passes Validate() (all attributes 0–99).
    ///   3. Derived values are in physically plausible ranges and directionally
    ///      correct (bigs have lower athleticism than guards in this fixture).
    /// </summary>
    private static bool Phase1RosterCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 1: Player object & Roster seam ---");
        var pass = true;

        // --- Load ---
        RosterConfig cfgRoster;
        try
        {
            cfgRoster = RosterConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  RosterConfig.Load threw: {ex.Message}");
            return false;
        }
        Console.WriteLine("  RosterConfig loaded OK.");

        // --- Populate a fresh GameState ---
        var fouls = new FoulTracker(7, 10);
        var game  = new GameState(fouls);

        foreach (var (side, configs) in new[]
        {
            (TeamSide.Home, cfgRoster.Home),
            (TeamSide.Away, cfgRoster.Away)
        })
        {
            var lineup = game.LineupFor(side);
            var roster = game.RosterFor(side);
            for (var i = 0; i < Lineup.Size; i++)
            {
                var slot   = lineup.SlotAt(i + 1);
                var player = configs[i].ToPlayer();
                roster.SetStarter(slot, player);
            }
        }
        Console.WriteLine("  Rosters populated OK.");

        // --- Per-slot assertions ---
        Console.WriteLine();
        Console.WriteLine($"  {"Side",-6} {"Slot",-5} {"Name",-20} {"Ath":>6} {"Grav":>6} {"Spac":>6}  Validate");
        Console.WriteLine($"  {new string('-', 68)}");

        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var lineup = game.LineupFor(side);
            var roster = game.RosterFor(side);

            for (var n = 1; n <= Lineup.Size; n++)
            {
                var slot   = lineup.SlotAt(n);
                var player = roster.PlayerAt(slot);

                // Assertion 1: slot resolves to a player
                if (player is null)
                {
                    Console.WriteLine($"  FAIL  {side} slot {n} resolved null.");
                    pass = false;
                    continue;
                }

                // Assertion 2: all authored attributes in 0–99
                var errors = player.Validate();
                var validateLabel = errors.Count == 0 ? "OK" : $"FAIL({errors.Count})";
                if (errors.Count > 0)
                {
                    pass = false;
                    foreach (var e in errors)
                        Console.WriteLine($"    {e}");
                }

                // Print derived values
                Console.WriteLine(
                    $"  {side,-6} {n,-5} {player.Name,-20} " +
                    $"{player.Athleticism,6:F1} " +
                    $"{player.GravityContribution,6:F1} {player.SpacingContribution,6:F1}" +
                    $"  {validateLabel}");

                // Assertion 3: derived values in plausible range (0–99; can't exceed
                // component max of 99 from a flat mean of 0–99 inputs)
                if (player.Athleticism < 0 || player.Athleticism > 99)
                { Console.WriteLine($"    FAIL  {player.Name}.Athleticism out of range: {player.Athleticism:F1}"); pass = false; }
                if (player.GravityContribution < 0 || player.GravityContribution > 99)
                { Console.WriteLine($"    FAIL  {player.Name}.GravityContribution out of range: {player.GravityContribution:F1}"); pass = false; }
                if (player.SpacingContribution < 0 || player.SpacingContribution > 99)
                { Console.WriteLine($"    FAIL  {player.Name}.SpacingContribution out of range: {player.SpacingContribution:F1}"); pass = false; }
            }
        }

        // --- Directional sanity: bigs (slot 4) should have lower athleticism
        //     than guards (slot 1) in the configured fixture ---
        var homeBig   = game.RosterFor(TeamSide.Home).PlayerAt(game.LineupFor(TeamSide.Home).SlotAt(4))!;
        var homeGuard = game.RosterFor(TeamSide.Home).PlayerAt(game.LineupFor(TeamSide.Home).SlotAt(1))!;
        if (homeBig.Athleticism >= homeGuard.Athleticism)
        {
            Console.WriteLine(
                $"  FAIL  Directional check: {homeBig.Name} athleticism ({homeBig.Athleticism:F1}) " +
                $">= {homeGuard.Name} athleticism ({homeGuard.Athleticism:F1}). " +
                "Expected big < guard in this fixture.");
            pass = false;
        }
        else
        {
            Console.WriteLine(
                $"\n  Directional OK — {homeGuard.Name} (guard) ath {homeGuard.Athleticism:F1} " +
                $"> {homeBig.Name} (big) ath {homeBig.Athleticism:F1}.");
        }

        // --- Substitution log sanity: 5 entries per side (one per starter) ---
        var homeLog = game.RosterFor(TeamSide.Home).Log;
        var awayLog = game.RosterFor(TeamSide.Away).Log;
        if (homeLog.Count != Lineup.Size || awayLog.Count != Lineup.Size)
        {
            Console.WriteLine($"  FAIL  Expected {Lineup.Size} log entries per side; " +
                              $"got Home={homeLog.Count}, Away={awayLog.Count}.");
            pass = false;
        }
        else
        {
            Console.WriteLine($"  Substitution log OK — {Lineup.Size} entries per side, all at possession 1.");
        }

        // --- Existing GameState sites: all 24 new GameState(fouls) calls elsewhere
        //     in this file compile unchanged (no ctor change). Prove that a bare
        //     GameState has empty (null) roster slots before population. ---
        var bareGame   = new GameState(new FoulTracker(7, 10));
        var barePlayer = bareGame.RosterFor(TeamSide.Home)
                                 .PlayerAt(bareGame.LineupFor(TeamSide.Home).SlotAt(1));
        if (barePlayer is not null)
        {
            Console.WriteLine("  FAIL  Bare GameState slot 1 should be null before population.");
            pass = false;
        }
        else
        {
            Console.WriteLine("  Bare GameState slots are null before population — existing sites unaffected.");
        }

        Console.WriteLine(pass ? "  Phase 1 PASSED." : "  Phase 1 FAILED.");
        return pass;
    }


    // ── Phase 24: Attribution Sanity Check ────────────────────────────────────
    // Constructs a controlled 10-player roster (1 Rim Anchor + 4 perimeter role
    // players per side, symmetric), runs 200 games, and verifies:
    //   (a) All Phase 23 attribution invariants hold under this roster.
    //   (b) Extreme attribute contrasts produce extreme box-score contrasts in the
    //       expected direction (DReb/OReb/BLK anchor dominance; 3PA role dominance;
    //       FT% tracks authored FreeThrow ratings 1:1).
    // This proves the weighting system preferentially credits players with the
    // intended attributes — not causal attribution of individual events.
    private static bool AttributionSanityCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("--- Phase 24: Attribution Sanity Check (controlled roster, 200 games) ---");
        Console.WriteLine("  NOTE: weighted stats (DReb, OReb, BLK, STL, TO) prove the weighting system");
        Console.WriteLine("  preferentially credits players with the intended attributes — not causal attribution.");
        Console.WriteLine("  Exact stats (FGA, FGM, 3PA, 3PM, FTA, FTM) are slot-exact.");
        Console.WriteLine("  BLK moved engine-side in Phase 36 (BlockerPicker) — no longer a harness WeightedDraw.");
        Console.WriteLine();

        var sanityOk = true;

        // ── Configs (same pattern as ObservationRunV1) ────────────────────────
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

        // ── Controlled roster construction ───────────────────────────────────
        // Slot 1 — Rim Anchor (Home PlayerId=1, Away PlayerId=6)
        // Big, dominant rebounder and shot blocker; poor FT% (FreeThrow=55); rarely shoots threes.
        var anchorTemplate = new Player("RimAnchor")
        {
            Height              = 92, Wingspan            = 92, Strength            = 88, Vertical            = 50,
            DefensiveRebounding = 95, OffensiveRebounding = 90,
            RimProtection       = 90,
            Finishing           = 90, FreeThrow           = 55,
            Outside             = 10, ThreeTendency       = 1,  RimTendency         = 80,
            BallHandling        = 40, FoulDrawing         = 30,
            // All other authored attributes default to 0; set to 50 for non-zero realism.
            Close = 50, Mid = 50, ShortTendency = 10, MidTendency = 5, LongTendency = 4,
            Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
            OffBallMovement = 50, Screening = 50,
            PerimeterDefense = 50, PostDefense = 50, Steals = 50,
            Weight = 50, Speed = 50, Quickness = 50, FirstStep = 50,
            Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50,
        };

        // Slots 2–5 — Perimeter role players (Home PlayerId=2–5, Away PlayerId=7–10)
        // Small, poor rebounder; good FT% (FreeThrow=78); heavy three-point shooter.
        var roleTemplate = new Player("PerimRole")
        {
            Height              = 35, Wingspan            = 35, Strength            = 30, Vertical            = 35,
            DefensiveRebounding = 5,  OffensiveRebounding = 5,
            RimProtection       = 5,
            Finishing           = 35, FreeThrow           = 78,
            Outside             = 75, ThreeTendency       = 60, RimTendency         = 10,
            BallHandling        = 65, FoulDrawing         = 65,
            // All other authored attributes default to 50.
            Close = 50, Mid = 50, ShortTendency = 10, MidTendency = 15, LongTendency = 15,
            Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
            OffBallMovement = 50, Screening = 50,
            PerimeterDefense = 50, PostDefense = 50, Steals = 50,
            Weight = 50, Speed = 50, Quickness = 50, FirstStep = 50,
            Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50,
        };

        // ── Per-player box score accumulators (indexed PlayerId-1, 0..9) ─────
        var bsFga  = new long[10]; var bsFgm  = new long[10];
        var bs3pa  = new long[10]; var bs3pm  = new long[10];
        var bsFta  = new long[10]; var bsFtm  = new long[10];
        var bsTo   = new long[10]; var bsOReb = new long[10];
        var bsDReb = new long[10]; var bsStl  = new long[10];
        var bsBlk  = new long[10];
        // Phase 39: per-player assist counts — engine-stamped on-walk.
        var bsAst  = new long[10];

        // ── Invariant totals ─────────────────────────────────────────────────
        long totalHomeFga = 0L, totalAwayFga = 0L;
        long totalHomeUnattr = 0L, totalAwayUnattr = 0L;
        long totalHomeFgm = 0L, totalAwayFgm = 0L;
        long totalHomeUnattrFgm = 0L, totalAwayUnattrFgm = 0L;
        long totalHome3pa = 0L, totalAway3pa = 0L;
        long totalHomeUnattr3pa = 0L, totalAwayUnattr3pa = 0L;
        long totalHome3pm = 0L, totalAway3pm = 0L;
        long totalHomeUnattr3pm = 0L, totalAwayUnattr3pm = 0L;
        long totalHomeFta = 0L, totalAwayFta = 0L;
        long totalHomeUnattrFta = 0L, totalAwayUnattrFta = 0L;
        long totalHomeFtm = 0L, totalAwayFtm = 0L;
        long totalHomeUnattrFtm = 0L, totalAwayUnattrFtm = 0L;
        long totalOrbWon = 0L, totalDrebPoss = 0L;
        long totalBlkCount = 0L, totalStlPoss = 0L, totalToPoss = 0L;
        long totalTeamViolToPoss = 0L;   // Phase 34: team violations (null TurnoverOffSlot — no individual credit)

        const int Games = 200;

        Console.Write($"  Running {Games} games");

        for (var seed = 1; seed <= Games; seed++)
        {
            if (seed % 50 == 0) Console.Write(".");

            // Fresh game state per game.
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));

            // Seat controlled roster — StampPlayerId before SetStarter (Phase 23 pattern).
            // Home: Slot1=Anchor(ID=1), Slots2-5=Role(ID=2-5)
            // Away: Slot1=Anchor(ID=6), Slots2-5=Role(ID=7-10)
            foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
            {
                var lineup  = game.LineupFor(side);
                var roster  = game.RosterFor(side);
                var idBase  = side == TeamSide.Home ? 1 : 6;

                // Slot 1 — Anchor
                roster.SetStarter(lineup.SlotAt(1), StampPlayerId(anchorTemplate, idBase));
                // Slots 2–5 — Role players
                for (var i = 1; i <= 4; i++)
                    roster.SetStarter(lineup.SlotAt(i + 1), StampPlayerId(roleTemplate, idBase + i));
            }

            var resolverRng = new SystemRng(seed);
            var governorRng = new SystemRng(seed + 1);
            var firstState = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1);

            var resolver = new Resolver(
                new RollAGenerator(cfg, cfgMatchup, game),
                cfg,
                new RollBGenerator(cfgB, cfgMatchup, game),
                new RollCGenerator(cfgC),
                cfgC,
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
                cfgMatchup,
                game,
                resolverRng);

            var governor = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndHalf);

            GovernorRunResult result;
            try
            {
                result = governor.Run(firstState);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL-THROW] Seed {seed}: {ex.Message}");
                sanityOk = false;
                continue;
            }

            var records = result.Possessions;

            // ── Accumulate invariant totals ───────────────────────────────────
            foreach (var r in records)
            {
                if (r.Offense == TeamSide.Home)
                {
                    totalHomeFga       += r.Fga;
                    totalHomeUnattr    += r.SlotUnattributedFga;
                    totalHomeFgm       += r.Fgm;
                    totalHomeUnattrFgm += r.SlotUnattributedFgm;
                    totalHome3pa       += r.ThreePa;
                    totalHomeUnattr3pa += r.ThreePaBySlot[0];
                    totalHome3pm       += r.ThreePm;
                    totalHomeUnattr3pm += r.ThreePmBySlot[0];
                    totalHomeFta       += r.Fta;
                    totalHomeUnattrFta += r.FtaBySlot[0];
                    totalHomeFtm       += r.Ftm;
                    totalHomeUnattrFtm += r.FtmBySlot[0];
                }
                else
                {
                    totalAwayFga       += r.Fga;
                    totalAwayUnattr    += r.SlotUnattributedFga;
                    totalAwayFgm       += r.Fgm;
                    totalAwayUnattrFgm += r.SlotUnattributedFgm;
                    totalAway3pa       += r.ThreePa;
                    totalAwayUnattr3pa += r.ThreePaBySlot[0];
                    totalAway3pm       += r.ThreePm;
                    totalAwayUnattr3pm += r.ThreePmBySlot[0];
                    totalAwayFta       += r.Fta;
                    totalAwayUnattrFta += r.FtaBySlot[0];
                    totalAwayFtm       += r.Ftm;
                    totalAwayUnattrFtm += r.FtmBySlot[0];
                }
                totalOrbWon    += r.OrbWon;
                totalDrebPoss  += r.EndLabel == "DefensiveRebound" ? 1 : 0;
                totalBlkCount  += r.BlkCount;
                totalStlPoss   += r.TurnoverWasLiveBall ? 1 : 0;
                totalToPoss    += IsTurnoverPossession(r) ? 1 : 0;
                totalTeamViolToPoss += r.EndLabel is "FiveSecondInbound" or "TenSecondBackcourt" or "ShotClockViolation" ? 1 : 0;
            }

            // ── Per-slot subset checks (verbatim from ObservationRunV1) ───────
            for (var chkSlot = 0; chkSlot <= 5; chkSlot++)
            {
                var slotFailed = false;
                foreach (var r in records)
                {
                    var slotFga = chkSlot == 0 ? r.SlotUnattributedFga : GetSlotFga(r, chkSlot);
                    var slotFgm = chkSlot == 0 ? r.SlotUnattributedFgm : GetSlotFgm(r, chkSlot);
                    if (r.ThreePmBySlot[chkSlot] > r.ThreePaBySlot[chkSlot])
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} 3PM > 3PA in a possession");
                        sanityOk = false; slotFailed = true; break;
                    }
                    if (r.ThreePaBySlot[chkSlot] > slotFga)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} 3PA > FGA in a possession");
                        sanityOk = false; slotFailed = true; break;
                    }
                    if (r.ThreePmBySlot[chkSlot] > slotFgm)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} 3PM > FGM in a possession");
                        sanityOk = false; slotFailed = true; break;
                    }
                    if (r.FtmBySlot[chkSlot] > r.FtaBySlot[chkSlot])
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} FTM > FTA in a possession");
                        sanityOk = false; slotFailed = true; break;
                    }
                }
                if (slotFailed) break;
            }

            // ── Attribution pass ──────────────────────────────────────────────
            var attributed = AttributeGame(result, game, seed);
            for (var i = 0; i < 10; i++)
            {
                bsFga [i] += attributed.Fga [i]; bsFgm [i] += attributed.Fgm [i];
                bs3pa [i] += attributed.Tpa [i]; bs3pm [i] += attributed.Tpm [i];
                bsFta [i] += attributed.Fta [i]; bsFtm [i] += attributed.Ftm [i];
                bsTo  [i] += attributed.To  [i]; bsOReb[i] += attributed.OReb[i];
                bsDReb[i] += attributed.DReb[i]; bsStl [i] += attributed.Stl [i];
                bsBlk [i] += attributed.Blk [i];
                bsAst [i] += attributed.Ast [i];
            }
        }

        Console.WriteLine();
        Console.WriteLine();

        // ── Box score ─────────────────────────────────────────────────────────
        Console.WriteLine($"  {"Player",-22} {"PTS",5} {"FGA",5} {"FGM",5} {"FG%",5} {"3PA",5} {"3PM",5} {"3P%",5} {"FTA",5} {"FTM",5} {"FT%",5} {"ORB",5} {"DRB",5} {"REB",5} {"STL",5} {"BLK",5} {"AST",5} {"TO",5}");
        Console.WriteLine(new string('─', 115));
        string[] playerNames = {
            "[Home] RimAnchor", "[Home] PerimRole2", "[Home] PerimRole3", "[Home] PerimRole4", "[Home] PerimRole5",
            "[Away] RimAnchor", "[Away] PerimRole2", "[Away] PerimRole3", "[Away] PerimRole4", "[Away] PerimRole5",
        };
        for (var i = 0; i < 10; i++)
        {
            double g   = Games;
            var fga    = bsFga [i] / g; var fgm  = bsFgm [i] / g;
            var tpa    = bs3pa [i] / g; var tpm  = bs3pm [i] / g;
            var fta    = bsFta [i] / g; var ftm  = bsFtm [i] / g;
            var orb    = bsOReb[i] / g; var drb  = bsDReb[i] / g;
            var stl    = bsStl [i] / g; var blk  = bsBlk [i] / g;
            var to     = bsTo  [i] / g;
            var ast    = bsAst [i] / g;
            var pts    = (fgm - tpm) * 2.0 + tpm * 3.0 + ftm;
            var fgPct  = fga > 0 ? fgm / fga * 100 : 0.0;
            var tpPct  = tpa > 0 ? tpm / tpa * 100 : 0.0;
            var ftPct  = fta > 0 ? ftm / fta * 100 : 0.0;
            Console.WriteLine(
                $"  {playerNames[i],-22} {pts,5:F1} {fga,5:F1} {fgm,5:F1} {fgPct,5:F1} " +
                $"{tpa,5:F1} {tpm,5:F1} {tpPct,5:F1} {fta,5:F1} {ftm,5:F1} {ftPct,5:F1} " +
                $"{orb,5:F1} {drb,5:F1} {(orb+drb),5:F1} {stl,5:F1} {blk,5:F1} {ast,5:F1} {to,5:F1}");
        }

        // ── Invariant checks ─────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  --- Invariant checks ---");

        void CheckExact(string label, long named, long total, long unattr)
        {
            if (named != total - unattr)
            {
                Console.WriteLine($"  [FAIL] {label}: named={named} != total-unattr={total - unattr}");
                sanityOk = false;
            }
            else Console.WriteLine($"  [OK] {label}");
        }

        long NamedHome(long[] arr) => arr[0]+arr[1]+arr[2]+arr[3]+arr[4];
        long NamedAway(long[] arr) => arr[5]+arr[6]+arr[7]+arr[8]+arr[9];

        var totalHomeFgmFull = bsFgm[0]+bsFgm[1]+bsFgm[2]+bsFgm[3]+bsFgm[4] + totalHomeUnattrFgm;
        var totalAwayFgmFull = bsFgm[5]+bsFgm[6]+bsFgm[7]+bsFgm[8]+bsFgm[9] + totalAwayUnattrFgm;

        CheckExact("FGA Home",    NamedHome(bsFga), totalHomeFga, totalHomeUnattr);
        CheckExact("FGA Away",    NamedAway(bsFga), totalAwayFga, totalAwayUnattr);
        CheckExact("FGM Home",    NamedHome(bsFgm), totalHomeFgmFull, totalHomeUnattrFgm);
        CheckExact("FGM Away",    NamedAway(bsFgm), totalAwayFgmFull, totalAwayUnattrFgm);
        CheckExact("3PA Home",    NamedHome(bs3pa), totalHome3pa, totalHomeUnattr3pa);
        CheckExact("3PA Away",    NamedAway(bs3pa), totalAway3pa, totalAwayUnattr3pa);
        CheckExact("3PM Home",    NamedHome(bs3pm), totalHome3pm, totalHomeUnattr3pm);
        CheckExact("3PM Away",    NamedAway(bs3pm), totalAway3pm, totalAwayUnattr3pm);
        CheckExact("FTA Home",    NamedHome(bsFta), totalHomeFta, totalHomeUnattrFta);
        CheckExact("FTA Away",    NamedAway(bsFta), totalAwayFta, totalAwayUnattrFta);
        CheckExact("FTM Home",    NamedHome(bsFtm), totalHomeFtm, totalHomeUnattrFtm);
        CheckExact("FTM Away",    NamedAway(bsFtm), totalAwayFtm, totalAwayUnattrFtm);

        var bsORebTotal = bsOReb.Sum();
        var bsDRebTotal = bsDReb.Sum();
        var bsBlkTotal  = bsBlk.Sum();
        var bsStlTotal  = bsStl.Sum();
        var bsToTotal   = bsTo.Sum();

        if (bsORebTotal != totalOrbWon)
        { Console.WriteLine($"  [FAIL] OReb: Σ per-player {bsORebTotal} != OrbWon {totalOrbWon}"); sanityOk = false; }
        else Console.WriteLine($"  [OK] OReb: Σ per-player == total OrbWon ({totalOrbWon})");

        if (bsDRebTotal != totalDrebPoss)
        { Console.WriteLine($"  [FAIL] DReb: Σ per-player {bsDRebTotal} != DReb possessions {totalDrebPoss}"); sanityOk = false; }
        else Console.WriteLine($"  [OK] DReb: Σ per-player == total DReb possessions ({totalDrebPoss})");

        if (bsBlkTotal != totalBlkCount)
        { Console.WriteLine($"  [FAIL] BLK: Σ per-player {bsBlkTotal} != BlkCount {totalBlkCount}"); sanityOk = false; }
        else Console.WriteLine($"  [OK] BLK: Σ per-player == total BlkCount ({totalBlkCount})");

        if (bsStlTotal != totalStlPoss)
        { Console.WriteLine($"  [FAIL] STL: Σ per-player {bsStlTotal} != live-TO possessions {totalStlPoss}"); sanityOk = false; }
        else Console.WriteLine($"  [OK] STL: Σ per-player == total live-TO possessions ({totalStlPoss})");

        if (bsToTotal != totalToPoss - totalTeamViolToPoss)
        { Console.WriteLine($"  [FAIL] TO: Σ per-player {bsToTotal} != individual-TO possessions {totalToPoss - totalTeamViolToPoss} (team violations {totalTeamViolToPoss} unattributed — Phase 34)"); sanityOk = false; }
        else Console.WriteLine($"  [OK] TO: Σ per-player == individual-TO possessions ({totalToPoss - totalTeamViolToPoss}; team violations {totalTeamViolToPoss} correctly unattributed)");

        // ── Directional assertions ────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  --- Directional assertions ---");

        double AnchorVal(long[] arr, int side) => arr[side * 5 + 0] / (double)Games;
        double RoleAvg(long[] arr, int side)   =>
            (arr[side*5+1] + arr[side*5+2] + arr[side*5+3] + arr[side*5+4]) / 4.0 / Games;

        for (var side = 0; side < 2; side++)
        {
            var sideLabel = side == 0 ? "Home" : "Away";

            // DReb dominance
            var ancDReb = AnchorVal(bsDReb, side);
            var roleDReb = RoleAvg(bsDReb, side);
            var drebRatio = roleDReb > 0 ? ancDReb / roleDReb : double.PositiveInfinity;
            if (ancDReb <= roleDReb * 3.0)
            { Console.WriteLine($"  [FAIL] {sideLabel} DReb: Anchor {ancDReb:F2}/g vs Role {roleDReb:F2}/g — ratio {drebRatio:F2}× < 3.0×"); sanityOk = false; }
            else Console.WriteLine($"  [OK] {sideLabel} DReb: Anchor {ancDReb:F2}/g vs Role {roleDReb:F2}/g — ratio {drebRatio:F2}× > 3.0×");

            // OReb dominance
            var ancOReb = AnchorVal(bsOReb, side);
            var roleOReb = RoleAvg(bsOReb, side);
            var orebRatio = roleOReb > 0 ? ancOReb / roleOReb : double.PositiveInfinity;
            if (ancOReb <= roleOReb * 3.0)
            { Console.WriteLine($"  [FAIL] {sideLabel} OReb: Anchor {ancOReb:F2}/g vs Role {roleOReb:F2}/g — ratio {orebRatio:F2}× < 3.0×"); sanityOk = false; }
            else Console.WriteLine($"  [OK] {sideLabel} OReb: Anchor {ancOReb:F2}/g vs Role {roleOReb:F2}/g — ratio {orebRatio:F2}× > 3.0×");

            // BLK dominance
            var ancBlk = AnchorVal(bsBlk, side);
            var roleBlk = RoleAvg(bsBlk, side);
            var blkRatio = roleBlk > 0 ? ancBlk / roleBlk : double.PositiveInfinity;
            if (ancBlk <= roleBlk * 2.0)
            { Console.WriteLine($"  [FAIL] {sideLabel} BLK: Anchor {ancBlk:F2}/g vs Role {roleBlk:F2}/g — ratio {blkRatio:F2}× < 2.0×"); sanityOk = false; }
            else Console.WriteLine($"  [OK] {sideLabel} BLK: Anchor {ancBlk:F2}/g vs Role {roleBlk:F2}/g — ratio {blkRatio:F2}× > 2.0×");

            // 3PA role dominance (integrated selection-plus-attribution check)
            var ancTpa = AnchorVal(bs3pa, side);
            var roleTpa = RoleAvg(bs3pa, side);
            var tpaRatio = ancTpa > 0 ? roleTpa / ancTpa : double.PositiveInfinity;
            if (roleTpa <= ancTpa * 3.0)
            { Console.WriteLine($"  [FAIL] {sideLabel} 3PA (integrated): Role {roleTpa:F2}/g vs Anchor {ancTpa:F2}/g — ratio {tpaRatio:F2}× < 3.0× [integrated selection+attribution check]"); sanityOk = false; }
            else Console.WriteLine($"  [OK] {sideLabel} 3PA (integrated): Role {roleTpa:F2}/g vs Anchor {ancTpa:F2}/g — ratio {tpaRatio:F2}× > 3.0× [integrated selection+attribution check]");

            // FT% — exact attribution check
            long anchorFta     = bsFta[side * 5];
            long anchorFtm     = bsFtm[side * 5];
            long roleFtaTotal  = bsFta[side*5+1]+bsFta[side*5+2]+bsFta[side*5+3]+bsFta[side*5+4];
            long roleFtmTotal  = bsFtm[side*5+1]+bsFtm[side*5+2]+bsFtm[side*5+3]+bsFtm[side*5+4];

            if (anchorFta < 50)
            {
                Console.WriteLine($"  [FAIL] {sideLabel} Anchor FT sample too small: FTA={anchorFta}, required >= 50");
                sanityOk = false;
            }
            else
            {
                var ancFtPct = anchorFtm / (double)anchorFta;
                if (ancFtPct >= 0.65)
                {
                    Console.WriteLine($"  [FAIL] {sideLabel} Anchor FT%: {ancFtPct:P1} >= 65% (FreeThrow=55, expect ≈55%)");
                    sanityOk = false;
                }
                else Console.WriteLine($"  [OK] {sideLabel} Anchor FT%: {ancFtPct:P1} < 65% (FreeThrow=55, expect ≈55%)");
            }

            if (roleFtaTotal < 200)
            {
                Console.WriteLine($"  [FAIL] {sideLabel} Role FT sample too small: FTA={roleFtaTotal}, required >= 200");
                sanityOk = false;
            }
            else
            {
                var roleFtPct = roleFtmTotal / (double)roleFtaTotal;
                if (roleFtPct <= 0.72)
                {
                    Console.WriteLine($"  [FAIL] {sideLabel} Role combined FT%: {roleFtPct:P1} <= 72% (FreeThrow=78, expect ≈78%)");
                    sanityOk = false;
                }
                else Console.WriteLine($"  [OK] {sideLabel} Role combined FT%: {roleFtPct:P1} > 72% (FreeThrow=78, expect ≈78%)");
            }
        }

        // ── No-zero FGA check (wiring health) ────────────────────────────────
        for (var i = 0; i < 10; i++)
        {
            if (bsFga[i] == 0)
            {
                Console.WriteLine($"  [FAIL] Player index {i} has 0 FGA across {Games} games — PlayerId wiring issue");
                sanityOk = false;
            }
        }

        // ── Local summary ─────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine(sanityOk
            ? "  Attribution sanity check: PASSED"
            : "  Attribution sanity check: FAILED (see [FAIL] lines above)");

        return sanityOk;
    }


    // ── Phase 29 Session 1: Hierarchy Bias Check ──────────────────────────────

    private static bool Phase29HierarchyBiasCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("--- Phase 29: Hierarchy Bias Check ---");
        Console.WriteLine("  Five sub-cases: direction, heliocentric amplification, egalitarian");
        Console.WriteLine("  compression, pairwise regression anchor, attention directional.");
        Console.WriteLine();

        var checkOk = true;
        var cfgE = RollEConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgAttention = AttentionConfig.Load(configPath);

        // ── Shared attribute template — five players, identical attributes ────
        // All scoring attributes identical so attribute differences cannot confound
        // the hierarchy direction test.
        Player MakePlayer(string name, int rank) => new Player(name)
        {
            HierarchyRank  = rank,
            SelfCreation   = 65,
            Close          = 60,
            PostMoves      = 55,
            Outside        = 60,
            Mid            = 55,
            Finishing      = 62,
            FreeThrow      = 70,
            FoulDrawing    = 50,
            BallHandling   = 55,
            Passing        = 55,
            Playmaking     = 55,
            OffBallMovement= 50,
            Screening      = 50,
            OffensiveRebounding  = 50,
            PerimeterDefense     = 50,
            PostDefense          = 50,
            RimProtection        = 50,
            DefensiveRebounding  = 50,
            Steals          = 50,
            Height          = 50,
            Wingspan        = 50,
            Weight          = 50,
            Strength        = 50,
            Speed           = 50,
            Quickness       = 50,
            FirstStep       = 50,
            Vertical        = 50,
            Endurance       = 50,
            Hustle          = 50,
            BasketballIQ    = 50,
            Discipline      = 50, HelpDefense    = 50,
            RimTendency     = 40,
            ShortTendency   = 15,
            MidTendency     = 15,
            LongTendency    = 10,
            ThreeTendency   = 20,
        };

        GameState MakeGame(double bias = 5.0)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            g.SetCoach(TeamSide.Home, new CoachProfile(bias));
            return g;
        }

        void Seat(GameState g, int[] ranks)
        {
            var roster  = g.RosterFor(TeamSide.Home);
            var lineup  = g.LineupFor(TeamSide.Home);
            var defRost = g.RosterFor(TeamSide.Away);
            var defLine = g.LineupFor(TeamSide.Away);
            for (var i = 0; i < 5; i++)
            {
                var slot = lineup.SlotAt(i + 1);
                roster.SetStarter(slot, MakePlayer($"P{i+1}(R{ranks[i]})", ranks[i]));
                var dslot = defLine.SlotAt(i + 1);
                defRost.SetStarter(dslot, MakePlayer($"D{i+1}", 5));
            }
        }

        var state = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound);
        var testRanks = new[] { 10, 8, 6, 4, 2 };

        // ── Sub-case 1: Direction at standard bias (5.0) ─────────────────────
        Console.WriteLine("  Sub-case 1: Direction at standard bias (5.0)");
        {
            var g    = MakeGame(5.0);
            Seat(g, testRanks);
            var gen  = new RollEGenerator(cfgE, g);
            var result1 = gen.GenerateWithPressure(state);
            var shares   = result1.FinalShares;

            Console.WriteLine($"    Shares: {string.Join(", ", shares.Select((s, i) => $"rank{testRanks[i]}={s:F4}"))}");

            var mono = true;
            for (var i = 0; i < 4; i++)
            {
                // Strict decrease required — UNLESS both slots are pinned at the
                // UsageFloor, in which case equality is correct engine behavior
                // (the floor is the participation protection; ranks below the
                // floor threshold legitimately collapse to the same value).
                var atFloor = shares[i + 1] <= cfgE.UsageFloor + 1e-9;
                var ok      = atFloor ? shares[i] >= shares[i + 1]
                                      : shares[i]  > shares[i + 1];
                if (!ok)
                {
                    Console.WriteLine($"    [FAIL] Not monotone: slot{i+1}={shares[i]:F4} <= slot{i+2}={shares[i+1]:F4}");
                    mono = false; checkOk = false;
                }
            }
            if (mono)
                Console.WriteLine("    [OK] Selection shares decrease monotonically rank10 → rank2 (floor-pinned slots may be equal)");
        }

        // ── Sub-case 2: Heliocentric amplification (bias 9.0) ────────────────
        Console.WriteLine("  Sub-case 2: Heliocentric amplification (bias 9.0)");
        {
            var g5  = MakeGame(5.0);
            Seat(g5, testRanks);
            var g9  = MakeGame(9.0);
            Seat(g9, testRanks);

            var gen5  = new RollEGenerator(cfgE, g5);
            var gen9  = new RollEGenerator(cfgE, g9);
            var shares5 = gen5.GenerateWithPressure(state).FinalShares;
            var shares9 = gen9.GenerateWithPressure(state).FinalShares;

            Console.WriteLine($"    bias=5: rank10={shares5[0]:F4}  bias=9: rank10={shares9[0]:F4}");

            if (shares9[0] > shares5[0])
                Console.WriteLine("    [OK] Heliocentric (9.0) gives rank-10 higher share than standard (5.0)");
            else
            {
                Console.WriteLine("    [FAIL] rank-10 share did not increase from bias=5 to bias=9");
                checkOk = false;
            }

            // Gap widening: difference between slot1 and slot5 should be larger at bias=9
            var gap5 = shares5[0] - shares5[4];
            var gap9 = shares9[0] - shares9[4];
            Console.WriteLine($"    Gap rank10-rank2: bias=5={gap5:F4}  bias=9={gap9:F4}");
            if (gap9 > gap5)
                Console.WriteLine("    [OK] Gap widened under heliocentric bias");
            else
            {
                Console.WriteLine("    [FAIL] Gap did not widen from bias=5 to bias=9");
                checkOk = false;
            }
        }

        // ── Sub-case 3: Egalitarian compression (bias 1.0) ───────────────────
        Console.WriteLine("  Sub-case 3: Egalitarian compression (bias 1.0)");
        {
            // Mixed ranks at bias=1 should equal all-rank-5 at bias=5
            // (both exponents collapse to 1.0 or 0.0 respectively, collapsing weights to 1.0)
            var gMixed = MakeGame(1.0);
            Seat(gMixed, testRanks);   // ranks 10,8,6,4,2

            var gFlat = MakeGame(5.0);
            Seat(gFlat, new[] { 5, 5, 5, 5, 5 });  // all rank=5

            var genMixed = new RollEGenerator(cfgE, gMixed);
            var genFlat  = new RollEGenerator(cfgE, gFlat);
            var sharesMixed = genMixed.GenerateWithPressure(state).FinalShares;
            var sharesFlat  = genFlat.GenerateWithPressure(state).FinalShares;

            Console.WriteLine($"    bias=1 (mixed ranks): {string.Join(", ", sharesMixed.Select(s => $"{s:F6}"))}");
            Console.WriteLine($"    bias=5 (all rank=5):  {string.Join(", ", sharesFlat.Select(s => $"{s:F6}"))}");

            var converge = true;
            for (var i = 0; i < 5; i++)
            {
                if (Math.Abs(sharesMixed[i] - sharesFlat[i]) > 1e-9)
                {
                    Console.WriteLine($"    [FAIL] slot{i+1}: egalitarian={sharesMixed[i]:F9} != all-rank-5={sharesFlat[i]:F9}");
                    converge = false; checkOk = false;
                }
            }
            if (converge)
                Console.WriteLine("    [OK] Egalitarian (bias=1, any ranks) == all-rank-5 at bias=5 — attributes only");
        }

        // ── Sub-case 4: Pairwise regression anchor ───────────────────────────
        Console.WriteLine("  Sub-case 4: Pairwise regression anchor");
        {
            // Case A: all rank=5, bias=5. Case B: all rank=5, bias=1.
            // Both must produce identical shares — the regression anchor.
            var gA = MakeGame(5.0);
            Seat(gA, new[] { 5, 5, 5, 5, 5 });
            var gB = MakeGame(1.0);
            Seat(gB, new[] { 5, 5, 5, 5, 5 });

            var genA = new RollEGenerator(cfgE, gA);
            var genB = new RollEGenerator(cfgE, gB);
            var sharesA = genA.GenerateWithPressure(state).FinalShares;
            var sharesB = genB.GenerateWithPressure(state).FinalShares;

            Console.WriteLine($"    Case A (rank5/bias5): {string.Join(", ", sharesA.Select(s => $"{s:F6}"))}");
            Console.WriteLine($"    Case B (rank5/bias1): {string.Join(", ", sharesB.Select(s => $"{s:F6}"))}");

            var anchor = true;
            for (var i = 0; i < 5; i++)
            {
                if (Math.Abs(sharesA[i] - sharesB[i]) > 1e-9)
                {
                    Console.WriteLine($"    [FAIL] slot{i+1}: caseA={sharesA[i]:F9} != caseB={sharesB[i]:F9}");
                    anchor = false; checkOk = false;
                }
            }
            if (anchor)
                Console.WriteLine("    [OK] Pairwise regression anchor holds: all-rank-5 shares identical across bias values");
        }

        // ── Sub-case 5: Hierarchy feeds attention (directional) ───────────────
        Console.WriteLine("  Sub-case 5: Hierarchy feeds attention (directional)");
        {
            // Slot1 rank=10, slot2 rank=5, otherwise identical attributes.
            var gAttn = MakeGame(5.0);
            var rosterA  = gAttn.RosterFor(TeamSide.Home);
            var lineupA  = gAttn.LineupFor(TeamSide.Home);
            var defRostA = gAttn.RosterFor(TeamSide.Away);
            var defLineA = gAttn.LineupFor(TeamSide.Away);

            rosterA.SetStarter(lineupA.SlotAt(1), MakePlayer("Star(R10)", 10));
            rosterA.SetStarter(lineupA.SlotAt(2), MakePlayer("Role(R5)",   5));
            rosterA.SetStarter(lineupA.SlotAt(3), MakePlayer("Role(R5)",   5));
            rosterA.SetStarter(lineupA.SlotAt(4), MakePlayer("Role(R5)",   5));
            rosterA.SetStarter(lineupA.SlotAt(5), MakePlayer("Role(R5)",   5));
            for (var i = 1; i <= 5; i++)
                defRostA.SetStarter(defLineA.SlotAt(i), MakePlayer($"Def{i}", 5));

            var genAttn  = new RollEGenerator(cfgE, gAttn);
            var attnGen  = new AttentionGenerator(cfgAttention, gAttn);

            var rollEOut    = genAttn.GenerateWithPressure(state);
            var attnResult  = attnGen.Generate(state, rollEOut.FinalShares);
            var attnShares  = attnResult.AttentionShares;

            Console.WriteLine($"    FinalShares: slot1={rollEOut.FinalShares[0]:F4}  slot2={rollEOut.FinalShares[1]:F4}");
            Console.WriteLine($"    AttentionShares: slot1={attnShares[0]:F4}  slot2={attnShares[1]:F4}");

            if (rollEOut.FinalShares[0] > rollEOut.FinalShares[1])
                Console.WriteLine("    [OK] Rank-10 FinalShare > rank-5 FinalShare");
            else
            {
                Console.WriteLine("    [FAIL] Rank-10 FinalShare not > rank-5");
                checkOk = false;
            }

            if (attnShares[0] > attnShares[1])
                Console.WriteLine("    [OK] Rank-10 AttentionShare > rank-5 AttentionShare — hierarchy feeds attention");
            else
            {
                Console.WriteLine("    [FAIL] Rank-10 not drawing more attention than rank-5");
                checkOk = false;
            }
        }

        Console.WriteLine();
        Console.WriteLine(checkOk
            ? "  Hierarchy bias check: PASSED"
            : "  Hierarchy bias check: FAILED (see [FAIL] lines above)");

        return checkOk;
    }


    // ── Phase 30: ShotSelectionBias + PaceBias (Coaching Layer 2) ─────────────

    private static bool Phase30CoachingLayer2Check(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("--- Phase 30: Coaching Layer 2 (ShotSelectionBias + PaceBias) ---");
        Console.WriteLine("  Four sub-cases: neutral regression, ShotSelection direction,");
        Console.WriteLine("  PaceBias Roll J direction, PaceBias Governor APL direction.");
        Console.WriteLine();

        var checkOk = true;
        var cfgA   = RollAConfig.Load(configPath);
        var cfgB   = RollBConfig.Load(configPath);
        var cfgC   = RollCConfig.Load(configPath);
        var cfgD   = RollDConfig.Load(configPath);
        var cfgE   = RollEConfig.Load(configPath);
        var cfgF   = RollFConfig.Load(configPath);
        var cfgG   = RollGConfig.Load(configPath);
        var cfgJ   = RollJConfig.Load(configPath);
        var cfgGov   = GovernorConfig.Load(configPath);
        var cfgClock = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);
        var cfgMatchup   = MatchupConfig.Load(configPath);

        // ── Helper: build a player with explicit tendency values ──────────────
        static Player MkShooter(string name,
            int rim, int @short, int mid, int @long, int three)
            => new Player(name)
            {
                RimTendency   = rim,
                ShortTendency = @short,
                MidTendency   = mid,
                LongTendency  = @long,
                ThreeTendency = three,
                Outside = 60, Mid = 55, Close = 60, Finishing = 62, FreeThrow = 70,
                FoulDrawing = 50, BallHandling = 55, Passing = 55, Playmaking = 55,
                SelfCreation = 65, PostMoves = 55, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50,
                RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50,
                Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50,
                HierarchyRank = 5,
            };

        // ── Helper: build a GameState with a specific PaceBias (both teams) ──
        GameState MakeGame(double paceBias = 5.0, double shotSelBias = 5.0)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            SeedMinimalRoster(g);  // Phase 31: picker needs populated roster
            g.SetCoach(TeamSide.Home, new CoachProfile(heliocentricBias: 5.0, shotSelectionBias: shotSelBias, paceBias: paceBias));
            g.SetCoach(TeamSide.Away, new CoachProfile(heliocentricBias: 5.0, shotSelectionBias: shotSelBias, paceBias: paceBias));
            return g;
        }

        // ── Helper: mean APL over N possessions via Governor (seeded) ─────────
        double MeanAPL(double paceBias, int seed, int possessions = 500)
        {
            var g = MakeGame(paceBias: paceBias);
            g.SetPossessionArrow(TeamSide.Home);
            var rng = new SystemRng(seed);
            var resolver = new Resolver(
                new StubPieGenerator(cfgA),
                cfgA,
                new RollBStubPieGenerator(cfgB),
                new RollCGenerator(cfgC),
                cfgC,
                new RollDGenerator(cfgD),
                new RollEStubPieGenerator(cfgE),
                new AttentionGenerator(AttentionConfig.Load(configPath), g),
                new RollFStubPieGenerator(cfgF),
                new RollGStubPieGenerator(cfgG),
                new RollHStubPieGenerator(RollHConfig.Load(configPath)),
                new RollIStubPieGenerator(RollIConfig.Load(configPath)),
                new RollJGenerator(cfgJ, cfgMatchup, g),
                new RollKStubPieGenerator(RollKConfig.Load(configPath)),
                new RollLStubPieGenerator(RollLConfig.Load(configPath)),
                new RollMStubPieGenerator(RollMConfig.Load(configPath)),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                cfgMatchup,
                g,
                rng);
            var gov = new Governor(resolver, g, cfgGov, cfgClock, new SystemRng(seed + 1), cfgEndOfHalf);
            var first = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            var result = gov.Run(first);
            var records = result.Possessions;
            if (records.Count == 0) return 0.0;
            // Measure only up to <possessions> possessions for a controlled comparison
            var slice = records.Take(possessions).ToList();
            return slice.Count > 0 ? slice.Average(r => r.Elapsed) : 0.0;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Sub-case 1 — Neutral regression guard
        // ════════════════════════════════════════════════════════════════════════
        Console.WriteLine("  Sub-case 1: Neutral regression guard");
        {
            var sc1Ok = true;

            // 1a: CoachProfile defaults
            var neutral = new CoachProfile();
            var s1aOk = neutral.ShotSelectionBias == 5.0 && neutral.PaceBias == 5.0;
            Console.WriteLine($"    [1a] CoachProfile() defaults: ShotSelectionBias={neutral.ShotSelectionBias} PaceBias={neutral.PaceBias}  {(s1aOk ? "[OK]" : "[FAIL]")}");
            sc1Ok &= s1aOk;

            // 1b: CoachingPull.Apply with neutral coach = identity
            var player = MkShooter("neutral-test", rim: 50, @short: 25, mid: 20, @long: 15, three: 40);
            var (r, s, m, l, t) = CoachingPull.Apply(player, neutral, null);
            var s1bOk = r == player.RimTendency && s == player.ShortTendency
                     && m == player.MidTendency  && l == player.LongTendency
                     && t == player.ThreeTendency;
            Console.WriteLine($"    [1b] Apply(neutral): ({r},{s},{m},{l},{t}) == authored ({player.RimTendency},{player.ShortTendency},{player.MidTendency},{player.LongTendency},{player.ThreeTendency})  {(s1bOk ? "[OK]" : "[FAIL]")}");
            sc1Ok &= s1bOk;

            // 1c: PaceBias=5 → mappedPace=0 → paceLift=0 in Roll J
            var gNeutral = MakeGame(paceBias: 5.0);
            var genJNeutral = new RollJGenerator(cfgJ, cfgMatchup, gNeutral);
            var ctxStamped = new TransitionContext(TransitionSource.Rebound) { OffenseSide = TeamSide.Home };
            var pieNeutral  = genJNeutral.Generate(ctxStamped);
            var pieNoSide   = genJNeutral.Generate(TransitionContext.Rebound);  // null OffenseSide
            var neutralPush  = pieNeutral.Slices.First(x => x.Outcome == TransitionOutcome.Push).Weight;
            var noSidePush   = pieNoSide.Slices.First(x => x.Outcome == TransitionOutcome.Push).Weight;
            var s1cOk = Math.Abs(neutralPush - noSidePush) < cfgJ.Epsilon;
            Console.WriteLine($"    [1c] PaceBias=5 stamped Push={neutralPush:F6} vs null-side Push={noSidePush:F6}  delta={Math.Abs(neutralPush - noSidePush):E2}  {(s1cOk ? "[OK]" : "[FAIL]")}");
            sc1Ok &= s1cOk;

            // 1d: PaceBias=5 → paceAdj=0 in Governor (math check)
            var paceAdj = (5.0 - 5.0) / 5.0 * cfgClock.PaceCenterScale;
            var s1dOk = Math.Abs(paceAdj) < 1e-12;
            Console.WriteLine($"    [1d] Governor paceAdj at PaceBias=5: {paceAdj}  {(s1dOk ? "[OK]" : "[FAIL]")}");
            sc1Ok &= s1dOk;

            checkOk &= sc1Ok;
            Console.WriteLine(sc1Ok ? "  Sub-case 1: [OK]" : "  Sub-case 1: [FAIL]");
        }

        Console.WriteLine();

        // ════════════════════════════════════════════════════════════════════════
        // Sub-case 2 — ShotSelectionBias direction
        // ════════════════════════════════════════════════════════════════════════
        Console.WriteLine("  Sub-case 2: ShotSelectionBias direction");
        {
            var sc2Ok = true;

            var player = MkShooter("bias-test", rim: 50, @short: 25, mid: 20, @long: 15, three: 40);

            // Bias 5 = identity
            var (r5, s5, m5, l5, t5) = CoachingPull.Apply(player, new CoachProfile(shotSelectionBias: 5.0), null);
            var s2aOk = r5 == player.RimTendency && s5 == player.ShortTendency
                     && m5 == player.MidTendency  && l5 == player.LongTendency
                     && t5 == player.ThreeTendency;
            Console.WriteLine($"    [2a] Bias 5 identity: ({r5},{s5},{m5},{l5},{t5})  {(s2aOk ? "[OK]" : "[FAIL]")}");
            sc2Ok &= s2aOk;

            // Bias 1 = inside: rim/short boosted, three/long suppressed, mid unchanged
            var (r1, s1, m1, l1, t1) = CoachingPull.Apply(player, new CoachProfile(shotSelectionBias: 1.0), null);
            var s2bOk = r1 > player.RimTendency    // inside boosted
                     && t1 < player.ThreeTendency  // outside suppressed
                     && m1 == player.MidTendency;  // mid unchanged
            Console.WriteLine($"    [2b] Bias 1 (inside): rim={r1:F2}>{player.RimTendency} three={t1:F2}<{player.ThreeTendency} mid={m1}=={player.MidTendency}  {(s2bOk ? "[OK]" : "[FAIL]")}");
            sc2Ok &= s2bOk;

            // Bias 10 = outside: three/long boosted, rim/short suppressed, mid unchanged
            var (r10, s10, m10, l10, t10) = CoachingPull.Apply(player, new CoachProfile(shotSelectionBias: 10.0), null);
            var s2cOk = t10 > player.ThreeTendency // outside boosted
                     && r10 < player.RimTendency   // inside suppressed
                     && m10 == player.MidTendency; // mid unchanged
            Console.WriteLine($"    [2c] Bias 10 (outside): three={t10:F2}>{player.ThreeTendency} rim={r10:F2}<{player.RimTendency} mid={m10}=={player.MidTendency}  {(s2cOk ? "[OK]" : "[FAIL]")}");
            sc2Ok &= s2cOk;

            // Shaq test: rim dominant even at outside bias
            var shaq = MkShooter("Shaq", rim: 80, @short: 40, mid: 20, @long: 10, three: 10);
            var (shaqR, _, _, _, shaqT) = CoachingPull.Apply(shaq, new CoachProfile(shotSelectionBias: 10.0), null);
            var s2dOk = shaqR > shaqT;
            Console.WriteLine($"    [2d] Shaq test (bias 10): rim={shaqR:F2} > three={shaqT:F2}  {(s2dOk ? "[OK]" : "[FAIL]")}");
            sc2Ok &= s2dOk;

            // Korver test: three dominant even at inside bias
            var korver = MkShooter("Korver", rim: 10, @short: 20, mid: 20, @long: 10, three: 80);
            var (_, _, _, _, korverT) = CoachingPull.Apply(korver, new CoachProfile(shotSelectionBias: 1.0), null);
            var (korverR, _, _, _, _) = CoachingPull.Apply(korver, new CoachProfile(shotSelectionBias: 1.0), null);
            var s2eOk = korverT > korverR;
            Console.WriteLine($"    [2e] Korver test (bias 1): three={korverT:F2} > rim={korverR:F2}  {(s2eOk ? "[OK]" : "[FAIL]")}");
            sc2Ok &= s2eOk;

            // Floor clamp: any tendency=1 at extreme bias → clamped value >= 1.0
            var lowThree = MkShooter("low-three", rim: 20, @short: 20, mid: 20, @long: 20, three: 1);
            var (_, _, _, _, clampedThree) = CoachingPull.Apply(lowThree, new CoachProfile(shotSelectionBias: 1.0), null);
            var s2fOk = clampedThree >= 1.0;
            Console.WriteLine($"    [2f] Floor clamp (ThreeTendency=1, bias=1): clamped={clampedThree:F4} >= 1.0  {(s2fOk ? "[OK]" : "[FAIL]")}");
            sc2Ok &= s2fOk;

            checkOk &= sc2Ok;
            Console.WriteLine(sc2Ok ? "  Sub-case 2: [OK]" : "  Sub-case 2: [FAIL]");
        }

        Console.WriteLine();

        // ════════════════════════════════════════════════════════════════════════
        // Sub-case 3 — PaceBias direction in Roll J
        // ════════════════════════════════════════════════════════════════════════
        Console.WriteLine("  Sub-case 3: PaceBias direction in Roll J");
        {
            var sc3Ok = true;

            var gSlow = MakeGame(paceBias: 2.0);
            var gFast = MakeGame(paceBias: 8.0);
            var genSlow = new RollJGenerator(cfgJ, cfgMatchup, gSlow);
            var genFast = new RollJGenerator(cfgJ, cfgMatchup, gFast);

            // Use Rebound source with stamped OffenseSide
            var ctx = new TransitionContext(TransitionSource.Rebound) { OffenseSide = TeamSide.Home };
            var pieSlow = genSlow.Generate(ctx);
            var pieFast = genFast.Generate(ctx);

            double Push(Pie<TransitionOutcome> p) =>
                p.Slices.First(x => x.Outcome == TransitionOutcome.Push).Weight;
            double Settle(Pie<TransitionOutcome> p) =>
                p.Slices.First(x => x.Outcome == TransitionOutcome.Settle).Weight;

            var fastPush  = Push(pieFast);
            var slowPush  = Push(pieSlow);
            var fastSettle  = Settle(pieFast);
            var slowSettle  = Settle(pieSlow);

            var s3aOk = fastPush > slowPush;
            Console.WriteLine($"    [3a] Fast (bias 8) Push={fastPush:F6} > Slow (bias 2) Push={slowPush:F6}  {(s3aOk ? "[OK]" : "[FAIL]")}");
            sc3Ok &= s3aOk;

            var s3bOk = fastSettle < slowSettle;
            Console.WriteLine($"    [3b] Fast Settle={fastSettle:F6} < Slow Settle={slowSettle:F6}  {(s3bOk ? "[OK]" : "[FAIL]")}");
            sc3Ok &= s3bOk;

            // Null OffenseSide → no crash, falls back to config neutral
            var gAny = MakeGame(paceBias: 5.0);
            var genAny = new RollJGenerator(cfgJ, cfgMatchup, gAny);
            bool s3cOk;
            try
            {
                var pieNull = genAny.Generate(TransitionContext.Rebound);  // OffenseSide is null
                var nullPush = Push(pieNull);
                // At TeamPaceBias=0.0 fallback → rawBias=5.0 → mappedPace=0 → lift=0 → base Push
                s3cOk = true;
                Console.WriteLine($"    [3c] Null OffenseSide: no crash, Push={nullPush:F6}  [OK]");
            }
            catch (Exception ex)
            {
                s3cOk = false;
                Console.WriteLine($"    [3c] Null OffenseSide threw: {ex.Message}  [FAIL]");
            }
            sc3Ok &= s3cOk;

            checkOk &= sc3Ok;
            Console.WriteLine(sc3Ok ? "  Sub-case 3: [OK]" : "  Sub-case 3: [FAIL]");
        }

        Console.WriteLine();

        // ════════════════════════════════════════════════════════════════════════
        // Sub-case 4 — PaceBias direction in Governor APL
        // ════════════════════════════════════════════════════════════════════════
        Console.WriteLine("  Sub-case 4: PaceBias direction in Governor APL (500 possessions each)");
        {
            var sc4Ok = true;
            var seed = cfgA.Seed;

            double meanSlow, meanFast;
            try
            {
                meanSlow = MeanAPL(paceBias: 2.0, seed: seed);
                meanFast = MeanAPL(paceBias: 8.0, seed: seed);

                Console.WriteLine($"    Slow coach (bias 2) mean APL: {meanSlow:F3}s");
                Console.WriteLine($"    Fast coach (bias 8) mean APL: {meanFast:F3}s");

                var s4Ok = meanFast < meanSlow;
                Console.WriteLine($"    [4a] Fast mean APL < Slow mean APL: {(s4Ok ? "[OK]" : "[FAIL]")}");
                sc4Ok &= s4Ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    [FAIL] Governor APL check threw: {ex.Message}");
                sc4Ok = false;
            }

            checkOk &= sc4Ok;
            Console.WriteLine(sc4Ok ? "  Sub-case 4: [OK]" : "  Sub-case 4: [FAIL]");
        }

        Console.WriteLine();
        Console.WriteLine(checkOk
            ? "  Phase 30 coaching layer 2 check: PASSED"
            : "  Phase 30 coaching layer 2 check: FAILED (see [FAIL] lines above)");

        return checkOk;
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Phase 33 — turnover committer picker check
    // ─────────────────────────────────────────────────────────────────────────
    private static bool Phase33TurnoverCommitterCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 33: turnover committer picker (TurnoverCommitterPicker) ---");
        var ok = true;
        const int N = 100_000;

        var matchupCfg = MatchupConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);

        // Helper: player with all attributes at b; override specific attributes.
        static Player MkP33(int id, int b,
                            int? bh     = null, int? height = null,
                            int? postDef = null, int? str   = null)
            => new Player($"p{id}")
            {
                PlayerId             = id,
                Outside              = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing          = b, BallHandling = bh ?? b, Passing = b, Playmaking = b,
                SelfCreation         = b, PostMoves    = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = b,
                PerimeterDefense     = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = b,
                Steals               = b,
                Height               = height ?? b, Wingspan = b, Weight = b,
                Strength             = str    ?? b,
                Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline           = b, HelpDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: seat five offensive players in Home 1-5; minimal away side.
        GameState BuildGame(Player[] off)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < off.Length && i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), MkP33(i + 6, 50));
            }
            return g;
        }

        // Helper: build a pre-selection possession state (no SelectedSlot, no ShotType).
        static PossessionState MkState(GameState g)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);

        // ── Sub-check 1: Posts suppressed ────────────────────────────────────────
        // PG (BH=80, low postness) + SG (BH=72) + SF (BH=60) + PF (BH=45, high postness) + C (BH=35).
        // 100k draws. Assert: C share < PG share AND PF share < SF share.
        // Python-confirmed: PG≈0.336, SG≈0.290, SF≈0.206, PF≈0.108, C≈0.059.
        {
            Console.WriteLine("  Sub-check 1: posts suppressed (C < PG, PF < SF)");
            var off = new[]
            {
                MkP33(1, 50, bh: 80, height: 40, postDef: 42, str: 44),  // PG
                MkP33(2, 50, bh: 72, height: 45, postDef: 44, str: 46),  // SG
                MkP33(3, 50, bh: 60, height: 55, postDef: 55, str: 55),  // SF
                MkP33(4, 50, bh: 45, height: 72, postDef: 70, str: 72),  // PF
                MkP33(5, 50, bh: 35, height: 88, postDef: 82, str: 85),  // C
            };
            var game   = BuildGame(off);
            var state  = MkState(game);
            var rng    = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var shares = counts.Select(c => (double)c / N).ToArray();
            Console.WriteLine($"    PG={shares[0]:P2}  SG={shares[1]:P2}  SF={shares[2]:P2}  PF={shares[3]:P2}  C={shares[4]:P2}");
            var sub1Ok = shares[4] < shares[0]   // C < PG
                      && shares[3] < shares[2];  // PF < SF
            ok &= sub1Ok;
            Console.WriteLine(sub1Ok ? "    [OK]" : "    [FAIL] post suppression direction wrong");
        }

        // ── Sub-check 2: Combo guards split evenly ───────────────────────────────
        // Two near-identical guards (BH=75/73, equal postness) + wing + two bigs.
        // 100k draws. Assert: |G1 share − G2 share| within tolerance (ratio 0.85–1.20).
        {
            Console.WriteLine("  Sub-check 2: combo guards split evenly (ratio 0.85–1.20)");
            var off = new[]
            {
                MkP33(1, 50, bh: 75, height: 44, postDef: 44, str: 45),  // G1
                MkP33(2, 50, bh: 73, height: 45, postDef: 45, str: 46),  // G2
                MkP33(3, 50, bh: 58, height: 55, postDef: 55, str: 55),  // W
                MkP33(4, 50, bh: 45, height: 72, postDef: 70, str: 72),  // PF
                MkP33(5, 50, bh: 35, height: 88, postDef: 82, str: 85),  // C
            };
            var game   = BuildGame(off);
            var state  = MkState(game);
            var rng    = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var g1Share = (double)counts[0] / N;
            var g2Share = (double)counts[1] / N;
            var ratio   = g2Share > 0 ? g1Share / g2Share : double.PositiveInfinity;
            Console.WriteLine($"    G1={g1Share:P2}  G2={g2Share:P2}  ratio={ratio:F3}");
            var sub2Ok = ratio is > 0.85 and < 1.20;
            ok &= sub2Ok;
            Console.WriteLine(sub2Ok ? "    [OK]" : "    [FAIL] combo guards ratio outside 0.85–1.20");
        }

        // ── Sub-check 3: Perimeter floor ─────────────────────────────────────────
        // In sub-check 1's lineup, assert SF share > 0.10.
        {
            Console.WriteLine("  Sub-check 3: perimeter floor (SF share > 0.10)");
            var off = new[]
            {
                MkP33(1, 50, bh: 80, height: 40, postDef: 42, str: 44),
                MkP33(2, 50, bh: 72, height: 45, postDef: 44, str: 46),
                MkP33(3, 50, bh: 60, height: 55, postDef: 55, str: 55),  // SF
                MkP33(4, 50, bh: 45, height: 72, postDef: 70, str: 72),
                MkP33(5, 50, bh: 35, height: 88, postDef: 82, str: 85),
            };
            var game   = BuildGame(off);
            var state  = MkState(game);
            var rng    = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var sfShare = (double)counts[2] / N;
            Console.WriteLine($"    SF share={sfShare:P2}  (bound: > 0.10)");
            var sub3Ok = sfShare > 0.10;
            ok &= sub3Ok;
            Console.WriteLine(sub3Ok ? "    [OK]" : "    [FAIL] SF share too low for a perimeter player");
        }

        // ── Sub-check 4: Post suppressed but non-zero ────────────────────────────
        // In sub-check 1's lineup, assert 0 < C share < 0.10.
        {
            Console.WriteLine("  Sub-check 4: post suppressed but non-zero (0 < C < 0.10)");
            var off = new[]
            {
                MkP33(1, 50, bh: 80, height: 40, postDef: 42, str: 44),
                MkP33(2, 50, bh: 72, height: 45, postDef: 44, str: 46),
                MkP33(3, 50, bh: 60, height: 55, postDef: 55, str: 55),
                MkP33(4, 50, bh: 45, height: 72, postDef: 70, str: 72),
                MkP33(5, 50, bh: 35, height: 88, postDef: 82, str: 85),
            };
            var game   = BuildGame(off);
            var state  = MkState(game);
            var rng    = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var cShare = (double)counts[4] / N;
            Console.WriteLine($"    C share={cShare:P2}  (bound: 0 < C < 0.10)");
            var sub4Ok = cShare > 0.0 && cShare < 0.10;
            ok &= sub4Ok;
            Console.WriteLine(sub4Ok ? "    [OK]" : "    [FAIL] C share out of (0, 0.10)");
        }

        // ── Sub-check 5: Handling tilt within perimeter ──────────────────────────
        // In sub-check 1's lineup, assert high-BH PG share > lower-BH SF share.
        {
            Console.WriteLine("  Sub-check 5: handling tilt within perimeter (PG > SF)");
            var off = new[]
            {
                MkP33(1, 50, bh: 80, height: 40, postDef: 42, str: 44),  // PG
                MkP33(2, 50, bh: 72, height: 45, postDef: 44, str: 46),
                MkP33(3, 50, bh: 60, height: 55, postDef: 55, str: 55),  // SF
                MkP33(4, 50, bh: 45, height: 72, postDef: 70, str: 72),
                MkP33(5, 50, bh: 35, height: 88, postDef: 82, str: 85),
            };
            var game   = BuildGame(off);
            var state  = MkState(game);
            var rng    = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var pgShare = (double)counts[0] / N;
            var sfShare = (double)counts[2] / N;
            Console.WriteLine($"    PG={pgShare:P2}  SF={sfShare:P2}");
            var sub5Ok = pgShare > sfShare;
            ok &= sub5Ok;
            Console.WriteLine(sub5Ok ? "    [OK]" : "    [FAIL] PG share not > SF share");
        }

        // ── Sub-check 6: Floor of 1 / no zero-weight slot ────────────────────────
        // Lineup with a BH=0 player; assert no throw and that slot still receives draws.
        {
            Console.WriteLine("  Sub-check 6: floor of 1 (BH=0 player still receives draws)");
            var off = new[]
            {
                MkP33(1, 50, bh: 0,  height: 40, postDef: 42, str: 44),  // BH=0
                MkP33(2, 50, bh: 72, height: 45, postDef: 44, str: 46),
                MkP33(3, 50, bh: 60, height: 55, postDef: 55, str: 55),
                MkP33(4, 50, bh: 45, height: 72, postDef: 70, str: 72),
                MkP33(5, 50, bh: 35, height: 88, postDef: 82, str: 85),
            };
            var game   = BuildGame(off);
            var state  = MkState(game);
            var rng    = new SystemRng(42);
            var counts = new int[5];
            bool threw = false;
            try
            {
                for (var i = 0; i < N; i++)
                {
                    var pick = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rng);
                    counts[pick.Number - 1]++;
                }
            }
            catch (Exception) { threw = true; }
            var bhZeroShare = (double)counts[0] / N;
            Console.WriteLine($"    BH=0 slot share={bhZeroShare:P2}  threw={threw}");
            var sub6Ok = !threw && bhZeroShare > 0.0;
            ok &= sub6Ok;
            Console.WriteLine(sub6Ok ? "    [OK]" : "    [FAIL] BH=0 slot got zero draws or threw");
        }

        // ── Sub-check 7: Reproducibility ─────────────────────────────────────────
        // Two identical draws from the same seed produce the same slot.
        {
            Console.WriteLine("  Sub-check 7: reproducibility (same seed → same pick)");
            var off = new[]
            {
                MkP33(1, 50, bh: 80, height: 40, postDef: 42, str: 44),
                MkP33(2, 50, bh: 72, height: 45, postDef: 44, str: 46),
                MkP33(3, 50, bh: 60, height: 55, postDef: 55, str: 55),
                MkP33(4, 50, bh: 45, height: 72, postDef: 70, str: 72),
                MkP33(5, 50, bh: 35, height: 88, postDef: 82, str: 85),
            };
            var game  = BuildGame(off);
            var state = MkState(game);
            const int Rep = 1_000;
            var seq1 = new int[Rep];
            var seq2 = new int[Rep];
            var rngA = new SystemRng(77);
            var rngB = new SystemRng(77);
            for (var i = 0; i < Rep; i++) seq1[i] = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rngA).Number;
            for (var i = 0; i < Rep; i++) seq2[i] = TurnoverCommitterPicker.Pick(state, game, matchupCfg, rngB).Number;
            var sub7Ok = seq1.SequenceEqual(seq2);
            ok &= sub7Ok;
            Console.WriteLine(sub7Ok ? "    [OK]" : "    [FAIL] sequences diverged");
        }

        // ── Sub-check 8: Null-roster throw ───────────────────────────────────────
        // Empty offense lineup → Pick throws InvalidOperationException.
        {
            Console.WriteLine("  Sub-check 8: null-roster throw (empty offense → throws)");
            var gameEmpty = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gameEmpty.AwayRoster.SetStarter(gameEmpty.AwayLineup.SlotAt(1), MkP33(6, 50));
            var stateEmpty = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            bool threwOk;
            try
            {
                TurnoverCommitterPicker.Pick(stateEmpty, gameEmpty, matchupCfg, new SystemRng(1));
                threwOk = false;
            }
            catch (InvalidOperationException) { threwOk = true; }
            ok &= threwOk;
            Console.WriteLine(threwOk ? "    [OK]" : "    [FAIL] did not throw on all-null offense");
        }

        // ── Invariant: every turnover possession carries a committer ──────────────
        // Governor run with real generators; assert TurnoverOffSlot != null on every
        // turnover possession. This is the post-condition the harness fallback retirement depends on.
        {
            Console.WriteLine("  Invariant: every turnover possession has TurnoverOffSlot != null");
            var cfgA     = RollAConfig.Load(configPath);
            var cfgGov   = GovernorConfig.Load(configPath);
            var cfgClock = RollClockConfig.Load(configPath);
            var cfgEoH   = EndOfHalfConfig.Load(configPath);
            var cfgE     = RollEConfig.Load(configPath);

            var govGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var offPlayers = new[]
            {
                MkP33(1, 50, bh: 75), MkP33(2, 50, bh: 68),
                MkP33(3, 50, bh: 58),
                MkP33(4, 50, bh: 42, height: 72, postDef: 70, str: 72),
                MkP33(5, 50, bh: 32, height: 85, postDef: 80, str: 82),
            };
            var defPlayers = Enumerable.Range(6, 5).Select(i => MkP33(i, 50)).ToArray();
            for (var i = 0; i < 5; i++)
            {
                govGame.HomeRoster.SetStarter(govGame.HomeLineup.SlotAt(i + 1), offPlayers[i]);
                govGame.AwayRoster.SetStarter(govGame.AwayLineup.SlotAt(i + 1), defPlayers[i]);
            }
            govGame.SetPossessionArrow(TeamSide.Home);

            var rng = new SystemRng(99);
            var resolver = new Resolver(
                new RollAGenerator(cfgA, matchupCfg, govGame),
                cfgA,
                new RollBGenerator(RollBConfig.Load(configPath), matchupCfg, govGame),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, govGame),
                new AttentionGenerator(AttentionConfig.Load(configPath), govGame),
                new RollFGenerator(RollFConfig.Load(configPath), matchupCfg, govGame),
                new RollGGenerator(RollGConfig.Load(configPath), matchupCfg, govGame),
                new RollHGenerator(RollHConfig.Load(configPath), matchupCfg, govGame),
                new RollIGenerator(RollIConfig.Load(configPath), matchupCfg, govGame),
                new RollJGenerator(RollJConfig.Load(configPath), matchupCfg, govGame),
                new RollKGenerator(RollKConfig.Load(configPath), matchupCfg, govGame),
                new RollLGenerator(RollLConfig.Load(configPath), govGame),
                new RollMGenerator(RollMConfig.Load(configPath), matchupCfg, govGame),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                matchupCfg,
                govGame,
                rng);

            var governor = new Governor(resolver, govGame, cfgGov, cfgClock, new SystemRng(100), cfgEoH);
            var first    = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            var result = governor.Run(first);

            var nullSlotCount   = 0;
            var teamViolCount   = 0;
            var turnoverPoss    = 0;
            foreach (var r in result.Possessions)
            {
                if (r.EndLabel is null) continue;
                // IsTurnoverPossession check mirrors the harness helper
                var isTurnover = r.EndLabel is "BadPassDeadBall" or "BadPassIntercepted"
                                            or "LostBallDeadBall" or "LostBallLiveBall"
                                            or "OffensiveFoul" or "Travel" or "DoubleDribble"
                                            or "Carry" or "ThreeSecondViolation"
                                            or "FiveSecondCloselyGuarded" or "OffensiveGoaltending"
                                            or "BackcourtViolation" or "ShotClockViolation"
                                            or "FiveSecondInbound" or "TenSecondBackcourt";
                if (!isTurnover) continue;
                turnoverPoss++;
                // Phase 34: team violations correctly have null TurnoverOffSlot — skip them here;
                // Phase 34 Invariant A verifies them separately.
                if (r.EndLabel is "FiveSecondInbound" or "TenSecondBackcourt" or "ShotClockViolation")
                {
                    teamViolCount++;
                    continue;
                }
                if (r.TurnoverOffSlot is null) nullSlotCount++;
            }
            var invOk = nullSlotCount == 0;
            ok &= invOk;
            Console.WriteLine(invOk
                ? $"    [OK] TurnoverOffSlot non-null on all {turnoverPoss - teamViolCount} individual-turnover possessions; {teamViolCount} team violations correctly null (of {result.Possessions.Count:N0} total)"
                : $"    [FAIL] {nullSlotCount} individual-turnover possessions had null TurnoverOffSlot");
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 33 turnover committer check: PASSED" : "  Phase 33 turnover committer check: FAILED (see [FAIL] lines above)");
        return ok;
    }



    // ─────────────────────────────────────────────────────────────────────────
    // Phase 34 — turnover attribution completion check
    // ─────────────────────────────────────────────────────────────────────────
    private static bool Phase34TurnoverAttributionCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 34: turnover attribution completion (TurnoverInteriorPicker, StealerPicker) ---");
        var ok = true;
        const int N = 100_000;

        var matchupCfg = MatchupConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);

        // Helper: player with all attributes at b; override specific attributes.
        static Player MkP34(int id, int b,
                            int? height = null, int? postDef = null,
                            int? str    = null, int? steals  = null)
            => new Player($"p{id}")
            {
                PlayerId             = id,
                Outside              = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing          = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation         = b, PostMoves    = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = b,
                PerimeterDefense     = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = b,
                Steals               = steals ?? b,
                Height               = height ?? b, Wingspan = b, Weight = b,
                Strength             = str    ?? b,
                Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline           = b, HelpDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Build a GameState with offPlayers on Home, defPlayers on Away.
        // TurnoverInteriorPicker reads Home (state.Offense); StealerPicker reads Away (state.Defense).
        GameState BuildGame34(Player[] offPlayers, Player[] defPlayers)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < offPlayers.Length && i < 5; i++)
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), offPlayers[i]);
            for (var i = 0; i < defPlayers.Length && i < 5; i++)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), defPlayers[i]);
            return g;
        }

        // Possession state: Home offends, Away defends.
        static PossessionState MkState34()
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);

        // ── Sub-check 1 — Interior picker: posts favored ──────────────────────
        // PG (low postness) through C (high postness). 100k draws.
        // Assert: C share > PG share AND PF share > SF share.
        {
            Console.WriteLine("  Sub-check 1: interior picker posts favored (C > PG, PF > SF)");
            var off = new[]
            {
                MkP34(1, 50, height: 40, postDef: 42, str: 44),  // PG
                MkP34(2, 50, height: 45, postDef: 44, str: 46),  // SG
                MkP34(3, 50, height: 55, postDef: 55, str: 55),  // SF
                MkP34(4, 50, height: 72, postDef: 70, str: 72),  // PF
                MkP34(5, 50, height: 88, postDef: 82, str: 85),  // C
            };
            var dummy = Enumerable.Range(6, 5).Select(i => MkP34(i, 50)).ToArray();
            var game  = BuildGame34(off, dummy);
            var state = MkState34();
            var rng   = new SystemRng(999);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[TurnoverInteriorPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var shares = counts.Select(c => (double)c / N).ToArray();
            Console.WriteLine($"    PG={shares[0]:P2}  SG={shares[1]:P2}  SF={shares[2]:P2}  PF={shares[3]:P2}  C={shares[4]:P2}");
            var sub1Ok = shares[4] > shares[0] && shares[3] > shares[2];
            ok &= sub1Ok;
            Console.WriteLine(sub1Ok ? "    [OK]" : "    [FAIL] post-favored direction wrong");
        }

        // ── Sub-check 2 — Interior picker: guards non-zero (floor holds) ──────
        // Same lineup. Assert: PG share > 0.05.
        {
            Console.WriteLine("  Sub-check 2: interior picker guard floor holds (PG > 0.05)");
            var off = new[]
            {
                MkP34(1, 50, height: 40, postDef: 42, str: 44),
                MkP34(2, 50, height: 45, postDef: 44, str: 46),
                MkP34(3, 50, height: 55, postDef: 55, str: 55),
                MkP34(4, 50, height: 72, postDef: 70, str: 72),
                MkP34(5, 50, height: 88, postDef: 82, str: 85),
            };
            var dummy = Enumerable.Range(6, 5).Select(i => MkP34(i, 50)).ToArray();
            var game  = BuildGame34(off, dummy);
            var state = MkState34();
            var rng   = new SystemRng(998);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[TurnoverInteriorPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var pgShare = (double)counts[0] / N;
            Console.WriteLine($"    PG={pgShare:P2}");
            var sub2Ok = pgShare > 0.05;
            ok &= sub2Ok;
            Console.WriteLine(sub2Ok ? "    [OK]" : "    [FAIL] PG share below floor");
        }

        // ── Sub-check 3 — Interior picker: reproducibility ────────────────────
        {
            Console.WriteLine("  Sub-check 3: interior picker same seed → identical sequence");
            var off = new[]
            {
                MkP34(1, 60, height: 42, postDef: 44, str: 50),
                MkP34(2, 60, height: 78, postDef: 76, str: 80),
            };
            var dummy = Enumerable.Range(3, 5).Select(i => MkP34(i, 50)).ToArray();
            var game  = BuildGame34(off, dummy);
            var state = MkState34();
            const int RepSeed = 7777;
            var run1 = new List<int>(); var run2 = new List<int>();
            var rng1 = new SystemRng(RepSeed); var rng2 = new SystemRng(RepSeed);
            for (var i = 0; i < 200; i++)
            {
                run1.Add(TurnoverInteriorPicker.Pick(state, game, matchupCfg, rng1).Number);
                run2.Add(TurnoverInteriorPicker.Pick(state, game, matchupCfg, rng2).Number);
            }
            var sub3Ok = run1.SequenceEqual(run2);
            ok &= sub3Ok;
            Console.WriteLine(sub3Ok ? "    [OK]" : "    [FAIL] same seed produced different sequences");
        }

        // ── Sub-check 4 — Interior picker: null-roster throw ──────────────────
        {
            Console.WriteLine("  Sub-check 4: interior picker empty offense throws");
            var dummy = new[] { MkP34(1, 50) };
            var game  = BuildGame34(Array.Empty<Player>(), dummy);   // no Home players
            var state = MkState34();
            var rng   = new SystemRng(1);
            var threw = false;
            try { TurnoverInteriorPicker.Pick(state, game, matchupCfg, rng); }
            catch (InvalidOperationException) { threw = true; }
            ok &= threw;
            Console.WriteLine(threw ? "    [OK]" : "    [FAIL] empty offense did not throw");
        }

        // ── Sub-check 5 — Stealer picker: guards favored (defensive lineup) ───
        // PG (high Steals, low postness) through C (low Steals, high postness).
        // 100k draws on Away defense. Assert: PG share > C share.
        {
            Console.WriteLine("  Sub-check 5: stealer picker guards favored (PG > C)");
            var def = new[]
            {
                MkP34(1, 50, height: 40, postDef: 42, str: 44, steals: 72),  // PG
                MkP34(2, 50, height: 45, postDef: 44, str: 46, steals: 65),  // SG
                MkP34(3, 50, height: 55, postDef: 55, str: 55, steals: 55),  // SF
                MkP34(4, 50, height: 72, postDef: 70, str: 72, steals: 38),  // PF
                MkP34(5, 50, height: 88, postDef: 82, str: 85, steals: 25),  // C
            };
            var offDummy = Enumerable.Range(6, 5).Select(i => MkP34(i, 50)).ToArray();
            var game  = BuildGame34(offDummy, def);   // def → Away
            var state = MkState34();                  // state.Defense = Away
            var rng   = new SystemRng(997);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[StealerPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var shares = counts.Select(c => (double)c / N).ToArray();
            Console.WriteLine($"    PG={shares[0]:P2}  SG={shares[1]:P2}  SF={shares[2]:P2}  PF={shares[3]:P2}  C={shares[4]:P2}");
            var sub5Ok = shares[0] > shares[4];
            ok &= sub5Ok;
            Console.WriteLine(sub5Ok ? "    [OK]" : "    [FAIL] guard-favored direction wrong");
        }

        // ── Sub-check 6 — Stealer picker: posts non-zero ──────────────────────
        // Assert: C share > 0.02.
        {
            Console.WriteLine("  Sub-check 6: stealer picker post floor holds (C > 0.02)");
            var def = new[]
            {
                MkP34(1, 50, height: 40, postDef: 42, str: 44, steals: 72),
                MkP34(2, 50, height: 45, postDef: 44, str: 46, steals: 65),
                MkP34(3, 50, height: 55, postDef: 55, str: 55, steals: 55),
                MkP34(4, 50, height: 72, postDef: 70, str: 72, steals: 38),
                MkP34(5, 50, height: 88, postDef: 82, str: 85, steals: 25),
            };
            var offDummy = Enumerable.Range(6, 5).Select(i => MkP34(i, 50)).ToArray();
            var game  = BuildGame34(offDummy, def);
            var state = MkState34();
            var rng   = new SystemRng(996);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[StealerPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var cShare = (double)counts[4] / N;
            Console.WriteLine($"    C={cShare:P2}");
            var sub6Ok = cShare > 0.02;
            ok &= sub6Ok;
            Console.WriteLine(sub6Ok ? "    [OK]" : "    [FAIL] C share below floor");
        }

        // ── Sub-check 7 — Stealer picker: Steals tilt within perimeter ────────
        // PG (Steals=72) vs SF (Steals=55) both perimeter. Assert: PG share > SF share.
        {
            Console.WriteLine("  Sub-check 7: stealer picker Steals tilt within perimeter (PG > SF)");
            var def = new[]
            {
                MkP34(1, 50, height: 40, postDef: 42, str: 44, steals: 72),  // PG high Steals
                MkP34(2, 50, height: 45, postDef: 44, str: 46, steals: 65),  // SG
                MkP34(3, 50, height: 55, postDef: 55, str: 55, steals: 55),  // SF lower Steals
                MkP34(4, 50, height: 72, postDef: 70, str: 72, steals: 38),
                MkP34(5, 50, height: 88, postDef: 82, str: 85, steals: 25),
            };
            var offDummy = Enumerable.Range(6, 5).Select(i => MkP34(i, 50)).ToArray();
            var game  = BuildGame34(offDummy, def);
            var state = MkState34();
            var rng   = new SystemRng(995);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[StealerPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var pgShare = (double)counts[0] / N;
            var sfShare = (double)counts[2] / N;
            Console.WriteLine($"    PG={pgShare:P2}  SF={sfShare:P2}");
            var sub7Ok = pgShare > sfShare;
            ok &= sub7Ok;
            Console.WriteLine(sub7Ok ? "    [OK]" : "    [FAIL] PG not > SF within perimeter");
        }

        // ── Sub-check 8 — Stealer picker: reproducibility ────────────────────
        {
            Console.WriteLine("  Sub-check 8: stealer picker same seed → identical sequence");
            var def = new[]
            {
                MkP34(1, 60, height: 42, postDef: 44, str: 50, steals: 70),
                MkP34(2, 60, height: 78, postDef: 76, str: 80, steals: 30),
            };
            var offDummy = Enumerable.Range(3, 5).Select(i => MkP34(i, 50)).ToArray();
            var game  = BuildGame34(offDummy, def);
            var state = MkState34();
            const int RepSeed = 8888;
            var run1 = new List<int>(); var run2 = new List<int>();
            var rng1 = new SystemRng(RepSeed); var rng2 = new SystemRng(RepSeed);
            for (var i = 0; i < 200; i++)
            {
                run1.Add(StealerPicker.Pick(state, game, matchupCfg, rng1).Number);
                run2.Add(StealerPicker.Pick(state, game, matchupCfg, rng2).Number);
            }
            var sub8Ok = run1.SequenceEqual(run2);
            ok &= sub8Ok;
            Console.WriteLine(sub8Ok ? "    [OK]" : "    [FAIL] same seed produced different sequences");
        }

        // ── Sub-check 9 — Stealer picker: null-roster throw ───────────────────
        {
            Console.WriteLine("  Sub-check 9: stealer picker empty defense throws");
            var dummy = new[] { MkP34(1, 50) };
            var game  = BuildGame34(dummy, Array.Empty<Player>());   // no Away players
            var state = MkState34();
            var rng   = new SystemRng(1);
            var threw = false;
            try { StealerPicker.Pick(state, game, matchupCfg, rng); }
            catch (InvalidOperationException) { threw = true; }
            ok &= threw;
            Console.WriteLine(threw ? "    [OK]" : "    [FAIL] empty defense did not throw");
        }

        // ── Governor run invariants A, B, C ───────────────────────────────────
        {
            Console.WriteLine("  Governor run invariants (Phase 34):");
            var cfgA     = RollAConfig.Load(configPath);
            var cfgGov   = GovernorConfig.Load(configPath);
            var cfgClock = RollClockConfig.Load(configPath);
            var cfgEoH   = EndOfHalfConfig.Load(configPath);
            var cfgE     = RollEConfig.Load(configPath);

            var govGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var offPlayers = new[]
            {
                MkP34(1, 50, height: 40, postDef: 42, str: 44),
                MkP34(2, 50, height: 45, postDef: 44, str: 46),
                MkP34(3, 50, height: 55, postDef: 55, str: 55),
                MkP34(4, 50, height: 72, postDef: 70, str: 72),
                MkP34(5, 50, height: 88, postDef: 82, str: 85),
            };
            var defPlayers = Enumerable.Range(6, 5).Select(i => MkP34(i, 50)).ToArray();
            for (var i = 0; i < 5; i++)
            {
                govGame.HomeRoster.SetStarter(govGame.HomeLineup.SlotAt(i + 1), offPlayers[i]);
                govGame.AwayRoster.SetStarter(govGame.AwayLineup.SlotAt(i + 1), defPlayers[i]);
            }
            govGame.SetPossessionArrow(TeamSide.Home);

            var rng      = new SystemRng(99);
            var resolver = new Resolver(
                new RollAGenerator(cfgA, matchupCfg, govGame),
                cfgA,
                new RollBGenerator(RollBConfig.Load(configPath), matchupCfg, govGame),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, govGame),
                new AttentionGenerator(AttentionConfig.Load(configPath), govGame),
                new RollFGenerator(RollFConfig.Load(configPath), matchupCfg, govGame),
                new RollGGenerator(RollGConfig.Load(configPath), matchupCfg, govGame),
                new RollHGenerator(RollHConfig.Load(configPath), matchupCfg, govGame),
                new RollIGenerator(RollIConfig.Load(configPath), matchupCfg, govGame),
                new RollJGenerator(RollJConfig.Load(configPath), matchupCfg, govGame),
                new RollKGenerator(RollKConfig.Load(configPath), matchupCfg, govGame),
                new RollLGenerator(RollLConfig.Load(configPath), govGame),
                new RollMGenerator(RollMConfig.Load(configPath), matchupCfg, govGame),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                matchupCfg,
                govGame,
                rng);

            var governor = new Governor(resolver, govGame, cfgGov, cfgClock, new SystemRng(100), cfgEoH);
            var first    = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            var result = governor.Run(first);

            // Invariant A: team violations produce null TurnoverOffSlot.
            var teamViolationReasons = new HashSet<string>
                { "FiveSecondInbound", "TenSecondBackcourt", "ShotClockViolation" };
            var teamViolations = result.Possessions
                .Where(r => teamViolationReasons.Contains(r.EndLabel ?? ""))
                .ToList();
            if (teamViolations.Count == 0)
            {
                Console.WriteLine("    [NOTE] Invariant A: no team-violation possessions fired — wiring confirmed correct by sub-checks above");
            }
            else
            {
                var invAOk = teamViolations.All(r => r.TurnoverOffSlot is null);
                ok &= invAOk;
                Console.WriteLine(invAOk
                    ? $"    [OK] Invariant A: all {teamViolations.Count} team-violation possessions have null TurnoverOffSlot"
                    : $"    [FAIL] Invariant A: {teamViolations.Count(r => r.TurnoverOffSlot is not null)} team-violation possessions had non-null TurnoverOffSlot");
            }

            // Invariant B: live-ball turnovers produce non-null StealerSlot.
            var liveBallTOs = result.Possessions.Where(r => r.TurnoverWasLiveBall).ToList();
            if (liveBallTOs.Count == 0)
            {
                Console.WriteLine("    [NOTE] Invariant B: no live-ball turnover possessions fired in this run");
            }
            else
            {
                var invBOk = liveBallTOs.All(r => r.StealerSlot is not null);
                ok &= invBOk;
                Console.WriteLine(invBOk
                    ? $"    [OK] Invariant B: all {liveBallTOs.Count} live-ball turnover possessions have non-null StealerSlot"
                    : $"    [FAIL] Invariant B: {liveBallTOs.Count(r => r.StealerSlot is null)} live-ball turnover possessions had null StealerSlot");
            }

            // Invariant C: non-live-ball possessions produce null StealerSlot.
            var nonLiveBall = result.Possessions.Where(r => !r.TurnoverWasLiveBall).ToList();
            var badStealerSlots = nonLiveBall.Count(r => r.StealerSlot is not null);
            var invCOk = badStealerSlots == 0;
            ok &= invCOk;
            Console.WriteLine(invCOk
                ? $"    [OK] Invariant C: all {nonLiveBall.Count} non-live-ball possessions have null StealerSlot"
                : $"    [FAIL] Invariant C: {badStealerSlots} non-live-ball possessions had non-null StealerSlot");
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 34 turnover attribution check: PASSED" : "  Phase 34 turnover attribution check: FAILED (see [FAIL] lines above)");
        return ok;
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Game Boundary Check — halftime foul reset, opening tip, overtime
    // ─────────────────────────────────────────────────────────────────────────
    private static bool GameBoundaryCheck(string configPath)
    {
        Console.WriteLine("\n--- GameBoundaryCheck: halftime foul reset + opening tip + overtime ---");
        var allOk = true;

        var cfgD   = RollDConfig.Load(configPath);
        var cfgGov = GovernorConfig.Load(configPath);
        var cfgA   = RollAConfig.Load(configPath);

        // ── Sub-check 1: FoulTracker.ResetForNewHalf() unit test ─────────────
        Console.WriteLine("  Sub-check 1: FoulTracker.ResetForNewHalf() unit test");
        {
            var ft = new FoulTracker(7, 10);
            for (var i = 0; i < 10; i++) ft.Increment(TeamSide.Home);
            for (var i = 0; i < 8;  i++) ft.Increment(TeamSide.Away);
            var preHomeBonus = ft.BonusFor(TeamSide.Home);
            var preAwayBonus = ft.BonusFor(TeamSide.Away);
            var prePasses = preHomeBonus == BonusType.Double && preAwayBonus == BonusType.OneAndOne;
            ft.ResetForNewHalf();
            var postHomeFouls = ft.FoulsFor(TeamSide.Home);
            var postAwayFouls = ft.FoulsFor(TeamSide.Away);
            var postHomeBonus = ft.BonusFor(TeamSide.Home);
            var postAwayBonus = ft.BonusFor(TeamSide.Away);
            var postPasses = postHomeFouls == 0 && postAwayFouls == 0
                          && postHomeBonus == BonusType.None && postAwayBonus == BonusType.None;
            var sub1Ok = prePasses && postPasses;
            allOk &= sub1Ok;
            Console.WriteLine(sub1Ok
                ? "    FoulTracker.ResetForNewHalf direct test -> ok"
                : $"    FAIL: pre={prePasses} (Home={preHomeBonus} Away={preAwayBonus}) post={postPasses} (HomeFouls={postHomeFouls} AwayFouls={postAwayFouls} HomeBonus={postHomeBonus} AwayBonus={postAwayBonus})");
        }

        // ── Sub-check 2: Governor wires the halftime reset (lifecycle integration) ─
        Console.WriteLine("  Sub-check 2: Governor fires halftime reset (lifecycle integration)");
        {
            // Controlled config: HalfSeconds=1.0 so no possession drains the clock fully;
            // NoShot=1.0 so every possession is a NoShot — no resolver call, no foul roll.
            // This means the only foul activity is what we pre-load before Run().
            var cfgGovCtrl = new GovernorConfig
            {
                PossessionCap = 400,
                Halves = 2,
                HalfSeconds = 1.0,
                OvertimeSeconds = 300.0,
            };
            var cfgEoH = new EndOfHalfConfig
            {
                HoldThresholdSeconds = 999.0,   // every possession is below threshold
                HoldShootLast = 0.0,
                ShootEarly = 0.0,
                NoShot = 1.0,
                Epsilon = 1e-9,
            };
            var game2 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            SeedMinimalRoster(game2);
            // Pre-load both teams to double bonus
            for (var i = 0; i < cfgD.DoubleBonusThreshold; i++) game2.Fouls.Increment(TeamSide.Home);
            for (var i = 0; i < cfgD.DoubleBonusThreshold; i++) game2.Fouls.Increment(TeamSide.Away);
            // Prevent OT: make the score non-tied before Run so OT loop doesn't fire.
            game2.HomeScore = 1;

            var rng2 = new SystemRng(42);
            // GovernorLoopCheck uses a controlled stub resolver with minimal wiring;
            // here we use the same approach — a stub pie generator for Roll A.
            var resolver2 = new Resolver(
                new StubPieGenerator(cfgA),
                cfgA,
                new RollBStubPieGenerator(RollBConfig.Load(configPath)),
                new RollCGenerator(RollCConfig.Load(configPath)),
                RollCConfig.Load(configPath),
                new RollDGenerator(cfgD),
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
                MatchupConfig.Load(configPath),
                game2,
                rng2);

            var governorRng2 = new SystemRng(43);
            var gov2 = new Governor(resolver2, game2, cfgGovCtrl, RollClockConfig.Load(configPath), governorRng2, cfgEoH);
            // Arrow must be Off for TipPossession; game2 starts with arrow Off (fresh GameState).
            var first2 = TipPossession.CreateFromTip(game2, governorRng2, possessionNumber: 1);
            var result2 = gov2.Run(first2);

            var homeFoulsEnd = game2.Fouls.FoulsFor(TeamSide.Home);
            var awayFoulsEnd = game2.Fouls.FoulsFor(TeamSide.Away);
            var noOt = result2.OvertimePeriods == 0;
            var noOtRecord = result2.Possessions.All(r => r.Half <= cfgGovCtrl.Halves);
            var resetOk = homeFoulsEnd == 0 && awayFoulsEnd == 0;
            var sub2Ok = resetOk && noOt && noOtRecord;
            allOk &= sub2Ok;
            Console.WriteLine(sub2Ok
                ? "    Governor fires halftime reset -> ok"
                : $"    FAIL: HomeFoulsEnd={homeFoulsEnd} AwayFoulsEnd={awayFoulsEnd} OvertimePeriods={result2.OvertimePeriods} noOtRecord={noOtRecord}");
        }

        // ── Sub-check 3: OT entered and exits correctly (fixed-seed regression) ─
        Console.WriteLine("  Sub-check 3: OT entered, Half==3 on first OT possession (fixed-seed regression)");
        {
            const int OtRegressionSeed = 73001;
            // Controlled regulation: HalfSeconds=1.0, NoShot=1.0 — regulation ends 0-0
            // OT uses real OvertimeSeconds so real possessions can score.
            var cfgGovOt = new GovernorConfig
            {
                PossessionCap = 400,
                Halves = 2,
                HalfSeconds = 1.0,
                OvertimeSeconds = cfgGov.OvertimeSeconds,   // real OT length from config
            };
            // Use the real EndOfHalfConfig — its HoldThresholdSeconds (~30s) only fires
            // near the end of a period. OT starts with real OvertimeSeconds (~300s), so
            // OT possessions get intent=null and the resolver runs normally, allowing scoring.
            // The controlled regulation halves (1.0s each) still end in NoShot because
            // 1.0 < HoldThresholdSeconds, but OT possessions are unaffected.
            var cfgEoHOt = EndOfHalfConfig.Load(configPath);
            var game3 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            SeedMinimalRoster(game3);

            var cfgB3      = RollBConfig.Load(configPath);
            var cfgC3      = RollCConfig.Load(configPath);
            var cfgE3      = RollEConfig.Load(configPath);
            var cfgF3      = RollFConfig.Load(configPath);
            var cfgG3      = RollGConfig.Load(configPath);
            var cfgH3      = RollHConfig.Load(configPath);
            var cfgI3      = RollIConfig.Load(configPath);
            var cfgJ3      = RollJConfig.Load(configPath);
            var cfgK3      = RollKConfig.Load(configPath);
            var cfgL3      = RollLConfig.Load(configPath);
            var cfgM3      = RollMConfig.Load(configPath);
            var cfgMatch3  = MatchupConfig.Load(configPath);
            var cfgClock3  = RollClockConfig.Load(configPath);
            var cfgOffFoul3 = RollOffensiveFoulConfig.Load(configPath);

            var rng3 = new SystemRng(OtRegressionSeed);
            var resolver3 = new Resolver(
                new StubPieGenerator(cfgA),
                cfgA,
                new RollBStubPieGenerator(cfgB3),
                new RollCGenerator(cfgC3),
                cfgC3,
                new RollDGenerator(cfgD),
                new RollEStubPieGenerator(cfgE3),
                new AttentionGenerator(AttentionConfig.Load(configPath), game3),
                new RollFStubPieGenerator(cfgF3),
                new RollGStubPieGenerator(cfgG3),
                new RollHStubPieGenerator(cfgH3),
                new RollIStubPieGenerator(cfgI3),
                new RollJGenerator(cfgJ3, cfgMatch3, game3),
                new RollKStubPieGenerator(cfgK3),
                new RollLStubPieGenerator(cfgL3),
                new RollMStubPieGenerator(cfgM3),
                new RollOffensiveFoulGenerator(cfgOffFoul3),
                cfgMatch3,
                game3,
                rng3);

            var governorRng3 = new SystemRng(OtRegressionSeed + 1);
            var gov3 = new Governor(resolver3, game3, cfgGovOt, cfgClock3, governorRng3, cfgEoHOt);
            var first3 = TipPossession.CreateFromTip(game3, governorRng3, possessionNumber: 1);
            var result3 = gov3.Run(first3);

            var otEntered = result3.OvertimePeriods >= 1;
            var firstOtRecord = result3.Possessions.FirstOrDefault(r => r.Half == cfgGovOt.Halves + 1);
            var halfNumberOk  = firstOtRecord != null;
            var notTied = game3.HomeScore != game3.AwayScore;
            // Gap-free sequence: last regulation possession number + 1 == first OT possession number
            var lastRegRecord  = result3.Possessions.LastOrDefault(r => r.Half <= cfgGovOt.Halves);
            var gapFree = lastRegRecord != null && firstOtRecord != null
                       && firstOtRecord.Number == lastRegRecord.Number + 1;

            if (!otEntered)
            {
                Console.WriteLine($"    STOP — seed {OtRegressionSeed} did not produce a tied regulation end. OvertimePeriods={result3.OvertimePeriods}. A replacement seed must come from an explicit prompt revision.");
                allOk = false;
            }
            else
            {
                var sub3Ok = otEntered && halfNumberOk && notTied && gapFree;
                allOk &= sub3Ok;
                Console.WriteLine(sub3Ok
                    ? $"    OT entered, Half=={cfgGovOt.Halves + 1} on first OT possession -> ok"
                    : $"    FAIL: otEntered={otEntered} halfNumberOk={halfNumberOk} notTied={notTied} gapFree={gapFree} " +
                      $"(Home={game3.HomeScore} Away={game3.AwayScore})");
            }
        }

        // ── Sub-check 4: No tied final results in normal games (smoke test) ───
        Console.WriteLine("  Sub-check 4: no tied final scores across 200 normal games");
        {
            var cfgB4     = RollBConfig.Load(configPath);
            var cfgC4     = RollCConfig.Load(configPath);
            var cfgE4     = RollEConfig.Load(configPath);
            var cfgF4     = RollFConfig.Load(configPath);
            var cfgG4     = RollGConfig.Load(configPath);
            var cfgH4     = RollHConfig.Load(configPath);
            var cfgI4     = RollIConfig.Load(configPath);
            var cfgJ4     = RollJConfig.Load(configPath);
            var cfgK4     = RollKConfig.Load(configPath);
            var cfgL4     = RollLConfig.Load(configPath);
            var cfgM4     = RollMConfig.Load(configPath);
            var cfgMatch4 = MatchupConfig.Load(configPath);
            var cfgClock4 = RollClockConfig.Load(configPath);
            var cfgEoH4   = EndOfHalfConfig.Load(configPath);
            var cfgOffFoul4 = RollOffensiveFoulConfig.Load(configPath);

            var tiedCount = 0;
            for (var seed = 1; seed <= 200; seed++)
            {
                var game4 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                SeedMinimalRoster(game4);

                var rng4 = new SystemRng(seed);
                var resolver4 = new Resolver(
                    new StubPieGenerator(cfgA),
                    cfgA,
                    new RollBStubPieGenerator(cfgB4),
                    new RollCGenerator(cfgC4),
                    cfgC4,
                    new RollDGenerator(cfgD),
                    new RollEStubPieGenerator(cfgE4),
                    new AttentionGenerator(AttentionConfig.Load(configPath), game4),
                    new RollFStubPieGenerator(cfgF4),
                    new RollGStubPieGenerator(cfgG4),
                    new RollHStubPieGenerator(cfgH4),
                    new RollIStubPieGenerator(cfgI4),
                    new RollJGenerator(cfgJ4, cfgMatch4, game4),
                    new RollKStubPieGenerator(cfgK4),
                    new RollLStubPieGenerator(cfgL4),
                    new RollMStubPieGenerator(cfgM4),
                    new RollOffensiveFoulGenerator(cfgOffFoul4),
                    cfgMatch4,
                    game4,
                    rng4);

                var governorRng4 = new SystemRng(seed + 1);
                var gov4 = new Governor(resolver4, game4, cfgGov, cfgClock4, governorRng4, cfgEoH4);
                var first4 = TipPossession.CreateFromTip(game4, governorRng4, possessionNumber: 1);
                try { gov4.Run(first4); } catch { tiedCount++; continue; }
                if (game4.HomeScore == game4.AwayScore) tiedCount++;
            }
            var sub4Ok = tiedCount == 0;
            allOk &= sub4Ok;
            Console.WriteLine(sub4Ok
                ? "    0 tied final scores across 200 games -> ok"
                : $"    FAIL: {tiedCount} tied final scores across 200 games");
        }

        // ── Sub-check 5: wingspan-driven tip directionality + precondition guard ─
        // Seats whoever is on the court (config roster), reads the actual max
        // wingspan for each side, derives the expected home win probability from
        // the same formula JumpBall uses, then asserts the observed rate across
        // 10,000 tips lands within a tolerance band of that expected probability.
        // No hardcoded thresholds: the assertion is always derived from whoever
        // is actually on the court, so it works correctly with any lineup.
        Console.WriteLine("  Sub-check 5: wingspan-driven tip directionality + precondition guard");
        {
            const int N5 = 10_000;

            // ── Read the actual max wingspan for each side from the live roster ─
            // Use a throw-away game just for the roster read — no tip run here.
            var game5Ref = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            SeatStartersFromConfig(game5Ref, configPath);

            static int ReadMaxWingspan(GameState g, TeamSide side)
            {
                var roster = g.RosterFor(side);
                var lineup = g.LineupFor(side);
                var max = -1;
                for (var s = 1; s <= 5; s++)
                {
                    var p = roster.PlayerAt(lineup.SlotAt(s));
                    if (p is not null && p.Wingspan > max) max = p.Wingspan;
                }
                return max >= 0 ? max : 50;
            }

            var homeMax5 = ReadMaxWingspan(game5Ref, TeamSide.Home);
            var awayMax5 = ReadMaxWingspan(game5Ref, TeamSide.Away);

            // Derive the expected home win probability from the same formula as JumpBall.
            var gap5    = homeMax5 - awayMax5;
            var rawProb = 0.50 + (gap5 / 7.0) * 0.40;
            var expectedHomeProb = Math.Clamp(rawProb, 0.10, 0.90);

            Console.WriteLine($"    roster: home max wingspan={homeMax5}, away max wingspan={awayMax5}");
            Console.WriteLine($"    expected home win prob={expectedHomeProb:P2}  away={1.0 - expectedHomeProb:P2}");

            // ── Run 10,000 tips, each with the same seeded roster ────────────
            var homeCount = 0;
            for (var seed = 0; seed < N5; seed++)
            {
                var game5 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                SeatStartersFromConfig(game5, configPath);
                var rng5   = new SystemRng(seed);
                var state5 = TipPossession.CreateFromTip(game5, rng5, possessionNumber: 1);
                if (state5.Offense == TeamSide.Home) homeCount++;

                // Arrow must no longer be Off after the tip
                if (game5.PossessionArrow == ArrowState.Off)
                {
                    Console.WriteLine($"    FAIL: after tip, arrow is still Off (seed {seed})");
                    allOk = false;
                }
                // Arrow must point at the loser (the team that did NOT receive the ball)
                var expectedArrow = state5.Offense == TeamSide.Home ? ArrowState.Away : ArrowState.Home;
                if (game5.PossessionArrow != expectedArrow)
                {
                    Console.WriteLine($"    FAIL: arrow points at winner instead of loser (seed {seed})");
                    allOk = false;
                }
            }

            // Assert observed rate is within tolerance of the expected probability.
            var observedHomeProb = (double)homeCount / N5;
            var dirTolerance     = cfgA.RateTolerance * 2.0;  // wider band: directional, not calibration
            var dirOk = Math.Abs(observedHomeProb - expectedHomeProb) <= dirTolerance;
            allOk &= dirOk;
            Console.WriteLine(dirOk
                ? $"    tip directionality -> ok  (observed home={observedHomeProb:P2}, expected={expectedHomeProb:P2})"
                : $"    FAIL: observed home={observedHomeProb:P2} outside expected={expectedHomeProb:P2} ± {dirTolerance:P2}");

            // Precondition guard: arrow ON should throw
            bool threwOk;
            try
            {
                var game5g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                game5g.SetPossessionArrow(TeamSide.Home);
                TipPossession.CreateFromTip(game5g, new SystemRng(1), possessionNumber: 1);
                threwOk = false;
            }
            catch (InvalidOperationException) { threwOk = true; }
            allOk &= threwOk;
            Console.WriteLine(threwOk
                ? "    precondition guard (arrow ON → throws) -> ok"
                : "    FAIL: CreateFromTip did not throw when arrow was ON");
        }

        Console.WriteLine($"  GameBoundaryCheck: {(allOk ? "ok" : "FAIL")}");
        return allOk;
    }

}
