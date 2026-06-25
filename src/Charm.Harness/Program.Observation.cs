using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // --- Observability: print a few full A->...->terminal chains. ---
    private static void ShowSamples(
        RollAConfig cfg, RollEConfig cfgE, IRollAPieGenerator genA,
        IRollEPieGenerator genE, Resolver resolver,
        GameState game, PossessionState state, IRng rng)
    {
        Console.WriteLine("--- Observability: sample possessions (seeded, full chain) ---");
        const double pressure = 0.5;

        for (var i = 0; i < 8; i++)
        {
            var pie = genA.Generate(state, pressure);
            var result = RollA.Execute(state, pie, rng, cfg);
            var routing = resolver.Route(result);

            var kind = result is Terminal ? "TERMINAL" : "CONTINUE";
            var elapsed = result.ElapsedSeconds is { } s ? $"{s:0}s" : "deferred";
            Console.WriteLine(
                $"  rollA={kind,-8} | elapsed={elapsed,-8} | final -> {routing.Destination}" +
                (routing.PossessionEnded ? " (possession ended)" : ""));
        }
        Console.WriteLine();

        // Roll C observability: print its pie, an input, and a resolved outcome.
        Console.WriteLine("--- Observability: Roll C (turnover classification) ---");
        var cfgCForObs = RollCConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "config.json"));
        var genC = new RollCGenerator(cfgCForObs);
        var pieC = genC.Generate(state);
        Console.WriteLine($"  pie: {pieC}");
        var sampleRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 5; i++)
        {
            var r = RollC.Execute(state, pieC, sampleRng, cfgCForObs);
            var term = (Terminal)r;
            var elapsed = r.ElapsedSeconds is { } s ? $"{s:0}s" : "deferred";
            Console.WriteLine($"  input=turnover | result=TERMINAL | reason={term.Reason,-18} | elapsed={elapsed}");
        }
        Console.WriteLine();

        // Roll D observability: the flavor pie (theater), then a walk of the
        // foul count climbing on one team so the bonus crossings at 7 and 10 are
        // visible — count before/after, bonus state, and the resulting route.
        Console.WriteLine("--- Observability: Roll D (non-shooting defensive foul) ---");
        var cfgD = RollDConfig.Load(Path.Combine(AppContext.BaseDirectory, "config.json"));
        var genD = new RollDGenerator(cfgD);
        var pieD = genD.Generate(state);
        Console.WriteLine($"  flavor pie (theater, does not route): {pieD}");
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");

        // A fresh game whose AWAY team (the defense in `state`) keeps fouling.
        var obsGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var obsRng = new SystemRng(cfg.Seed);
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = obsGame.Fouls.FoulsFor(state.Defense);
            var r = (Continue)RollD.Execute(state, pieD, obsGame, obsRng);
            var after = obsGame.Fouls.FoulsFor(state.Defense);
            var route = r.Next == ContinuationKind.ResolveFreeThrows
                ? $"ResolveFreeThrows({r.Bonus})"
                : "ResumeInbound";
            Console.WriteLine(
                $"  foul#{after,2}: {before}->{after} | flavor={r.Flavor,-8} | bonus={r.Bonus,-9} | route={route}");
        }
        Console.WriteLine();

        // Roll E observability: the flat selection pie, then a few sample
        // selections showing a real named slot stamped on the possession and the
        // full chain landing on the player-action stub. This is the seam Session 7
        // left being walked for the first time: Proceed -> Roll E -> a named slot.
        Console.WriteLine("--- Observability: Roll E (player selection) ---");
        var pieE = genE.Generate(state);
        Console.WriteLine($"  selection pie (flat, no signal yet): {pieE}");
        var selRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 8; i++)
        {
            var r = (Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, selRng);
            var s = r.State.SelectedSlot!.Value;
            Console.WriteLine(
                $"  proceed -> selected {s.Side} slot {s.Number} | next={r.Next}");
        }
        Console.WriteLine();

        // Roll F observability: the flat-ish action pie, then a few sample
        // actions. Each starts from a SELECTED slot (rolled through Roll E first,
        // exactly as the live chain does), so the resolved action carries a real
        // slot forward. Shows the five-way classification and the kind each emits
        // — STUB:PlayerAction is gone; the action now resolves into one of five
        // real exits.
        Console.WriteLine("--- Observability: Roll F (player action) ---");
        var genF = new RollFStubPieGenerator(RollFConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "config.json")));
        var pieF = genF.Generate(state);
        Console.WriteLine($"  action pie (flat-ish, no signal yet): {pieF}");
        var actRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 8; i++)
        {
            // Select a player first (Roll E), then resolve the action (Roll F).
            var selected = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, actRng)).State;
            var r = (Continue)RollF.Execute(selected, pieF, actRng);
            var s = r.State.SelectedSlot!.Value;
            Console.WriteLine(
                $"  {s.Side} slot {s.Number} action -> {r.Next}");
        }
        Console.WriteLine();

        // Roll H observability: the per-zone block weights (the Session 13
        // headline), then a few sample shots. Each is rolled through the live chain
        // up to H — select a slot (Roll E), stamp a zone (Roll G) — so each resolved
        // shot carries a real slot AND zone; the generator sizes that shot's block
        // slice from its zone, and H stamps the result. Shows the seven-way outcome,
        // the zone it resolved against, and where it routes (terminal vs. one of the
        // four post-H stubs, block recovery included).
        Console.WriteLine("--- Observability: Roll H (make/miss) ---");
        var configPathH = Path.Combine(AppContext.BaseDirectory, "config.json");
        var genGForH = new RollGStubPieGenerator(RollGConfig.Load(configPathH));
        var cfgHForObs = RollHConfig.Load(configPathH);
        var genH = new RollHStubPieGenerator(cfgHForObs);
        Console.WriteLine("  per-zone block weight b(zone) (Rim highest, Three lowest):");
        foreach (var z in new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three })
            Console.WriteLine($"    {z,-6} {cfgHForObs.BlockWeight(z):P2}");
        var shotRng = new SystemRng(cfg.Seed);
        for (var i = 0; i < 8; i++)
        {
            // Walk E -> G to deliver a fully-stamped pre-H state, generate the pie
            // for THAT shot's zone, then resolve H.
            var selectedH = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, shotRng)).State;
            var withZone = ((Continue)RollG.Execute(selectedH, genGForH.Generate(selectedH), 0.0, shotRng)).State;
            var pieH = genH.Generate(withZone);
            var hr = RollH.Execute(withZone, pieH, shotRng);
            var carried = hr switch { Terminal t => t.State, Continue c => c.State, _ => withZone };
            var s2 = carried.SelectedSlot!.Value;
            var ending = hr is Terminal term
                ? $"TERMINAL ({term.Reason})"
                : $"CONTINUE -> {((Continue)hr).Next}";
            Console.WriteLine(
                $"  {s2.Side} slot {s2.Number} {carried.ShotType} shot -> {carried.Result} -> {ending}");
        }
        Console.WriteLine();

        // Roll I observability: the seven-way rebound pie, then a few sample MISSES
        // driven through the live chain (E -> G -> H), each landing on one of Roll
        // I's destinations. Only misses reach Roll I here (blocks do too, via the
        // block pie — exercised in the dedicated block checks). We keep rolling H
        // until a Miss lands, then route it and show where the rebound resolved:
        // a defensive board / offensive foul / OOB-off-offense ends the possession
        // (terminal), an offensive board / loose-ball-defense foul / OOB-off-defense /
        // jump ball keeps it alive (continue).
        Console.WriteLine("--- Observability: Roll I (rebound resolution) ---");
        var cfgIForObs = RollIConfig.Load(configPathH);
        var genI = new RollIStubPieGenerator(cfgIForObs);
        var pieIObs = genI.Generate(state, ReboundSource.LiveBall);
        Console.WriteLine($"  live-miss rebound pie (flat, no signal yet): {pieIObs}");
        var obsGameI = new GameState(new FoulTracker(
            RollDConfig.Load(configPathH).BonusThreshold,
            RollDConfig.Load(configPathH).DoubleBonusThreshold));
        var reboundRng = new SystemRng(cfg.Seed);
        var shown = 0;
        var guard = 0;
        while (shown < 8 && guard++ < 100000)
        {
            // Walk E -> G -> H; only act on a Miss (the one outcome that feeds I).
            var sel = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, obsGameI, reboundRng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genGForH.Generate(sel), 0.0, reboundRng)).State;
            var hRes = RollH.Execute(zoned, genH.Generate(zoned), reboundRng);
            if (hRes is not Continue { Next: ContinuationKind.ResolveRebound } missCont) continue;

            var iRes = RollI.Execute(missCont.State, pieIObs, obsGameI, reboundRng);
            var landing = iRes switch
            {
                Terminal t => $"TERMINAL ({t.Reason})",
                Continue { Next: ContinuationKind.ResolveOffensiveRebound } => "CONTINUE -> OffensiveRebound (same possession)",
                Continue { Next: ContinuationKind.ResolveSidelineInbound } c => $"CONTINUE -> SidelineInbound (bonus={c.Bonus})",
                Continue { Next: ContinuationKind.ResolveFreeThrows } c => $"CONTINUE -> FreeThrows (bonus={c.Bonus})",
                Continue { Next: ContinuationKind.ResolveJumpBall } => "CONTINUE -> JumpBall (arrow node)",
                _ => "?"
            };
            Console.WriteLine($"  miss rebounded -> {landing}");
            shown++;
        }
        Console.WriteLine();
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Observation Run v1 — macro sentinel harness (frozen-corpus-v1)
    //
    // Runs N full games against a frozen scenario corpus and emits one
    // self-describing macro-sentinel block. RECORDED, NOT JUDGED: no number
    // triggers a realism pass/fail. Only five mechanical checks assert anything.
    //
    // SCOPE WALLS:
    //   - No config values change in this method.
    //   - No new engine fields or instrumentation.
    //   - No judgment / target lines in the output.
    // ─────────────────────────────────────────────────────────────────────────

    private static void ObservationRunV1(string configPath)
    {
        const string CorpusId = "frozen-corpus-v1";
        const int    N         = 1_000;   // games per run — configurable constant

        Console.WriteLine();
        Console.WriteLine($"=== OBSERVATION RUN — {CorpusId} ===");

        // ── Configs — loaded once; immutable across all N games. ─────────
        var cfg          = RollAConfig.Load(configPath);
        var cfgB         = RollBConfig.Load(configPath);
        var cfgC         = RollCConfig.Load(configPath);
        var cfgD         = RollDConfig.Load(configPath);
        var cfgE         = RollEConfig.Load(configPath);
        var cfgF         = RollFConfig.Load(configPath);
        var cfgG         = RollGConfig.Load(configPath);
        var cfgH         = RollHConfig.Load(configPath);
        var cfgI         = RollIConfig.Load(configPath);
        var cfgJ         = RollJConfig.Load(configPath);
        var cfgK         = RollKConfig.Load(configPath);
        var cfgL         = RollLConfig.Load(configPath);
        var cfgM         = RollMConfig.Load(configPath);
        var cfgOffFoul   = RollOffensiveFoulConfig.Load(configPath);
        var cfgGov       = GovernorConfig.Load(configPath);
        var cfgClock     = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);
        var cfgMatchup   = MatchupConfig.Load(configPath);
        var cfgRoster    = RosterConfig.Load(configPath);

        // ── Config hash — ties every recorded number to the exact config. ─
        var configBytes = File.ReadAllBytes(configPath);
        var hashBytes   = System.Security.Cryptography.SHA256.HashData(configBytes);
        var configHash  = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // ── Metadata header ───────────────────────────────────────────────
        var homeFive = string.Join("/", cfgRoster.Home.Select(p => p.Name));
        var awayFive = string.Join("/", cfgRoster.Away.Select(p => p.Name));

        Console.WriteLine($"  config hash:      {configHash}");
        Console.WriteLine($"  corpus:           {CorpusId}  (seeds 1..{N}; rosters: {homeFive} vs {awayFive})");
        Console.WriteLine($"  sample size:      {N} games");
        Console.WriteLine($"  strategy/context: home-press={cfgMatchup.HomePressFrequency:F1}  away-press={cfgMatchup.AwayPressFrequency:F1}  halves={cfgGov.Halves}  half={cfgGov.HalfSeconds:F0}s");
        Console.WriteLine($"  session/commit:   <stamp at run time>");
        Console.WriteLine();

        // ── Per-game accumulators ─────────────────────────────────────────
        var totalPossList   = new List<int>(N);
        var homePossList    = new List<int>(N);
        var awayPossList    = new List<int>(N);
        var noShotList      = new List<int>(N);
        var homePPPList     = new List<double>(N);
        var awayPPPList     = new List<double>(N);
        var combinedPPPList = new List<double>(N);
        var homeScoreList   = new List<int>(N);
        var awayScoreList   = new List<int>(N);
        var marginList      = new List<int>(N);
        var transFreqList   = new List<double>(N);
        var aplList         = new List<double>(N);
        var homeFoulsList   = new List<int>(N);
        var awayFoulsList   = new List<int>(N);
        var holdList        = new List<int>(N);
        var earlyList       = new List<int>(N);
        var noShotIntList   = new List<int>(N);

        // ── v2 shooting / rebounding sentinel accumulators ──────────────
        var homeFgPctList     = new List<double>(N);
        var awayFgPctList     = new List<double>(N);
        var combinedFgPctList = new List<double>(N);
        var home3pPctList     = new List<double>(N);
        var away3pPctList     = new List<double>(N);
        var combined3pPctList = new List<double>(N);
        var homeFtPctList     = new List<double>(N);
        var awayFtPctList     = new List<double>(N);
        var combinedFtPctList = new List<double>(N);
        var threePaRateList   = new List<double>(N);  // 3PA / FGA combined
        var orbPctList        = new List<double>(N);  // ORB won / ORB chances combined
        var ftrList           = new List<double>(N);  // FTA / FGA combined

        // ── Per-zone shooting accumulators (combined; FG% and attempt share per zone) ──
        var rimFgPctList    = new List<double>(N);
        var shortFgPctList  = new List<double>(N);
        var midFgPctList    = new List<double>(N);
        var longFgPctList   = new List<double>(N);
        var threeFgPctList  = new List<double>(N);
        var rimShareList    = new List<double>(N);
        var shortShareList  = new List<double>(N);
        var midShareList    = new List<double>(N);
        var longShareList   = new List<double>(N);
        var threeShareList  = new List<double>(N);

        // Per-slot FGA running totals (aggregate, not per-game lists).
        // Aggregate ratio matches the stress-test calculation and reconciles
        // naturally: slot shares sum to exactly 100%.
        long totalHomeFga   = 0L;
        long totalHomeFgaS1 = 0L, totalHomeFgaS2 = 0L, totalHomeFgaS3 = 0L,
             totalHomeFgaS4 = 0L, totalHomeFgaS5 = 0L;
        long totalHomeUnattr = 0L;
        long totalAwayFga   = 0L;
        long totalAwayFgaS1 = 0L, totalAwayFgaS2 = 0L, totalAwayFgaS3 = 0L,
             totalAwayFgaS4 = 0L, totalAwayFgaS5 = 0L;
        long totalAwayUnattr = 0L;
        long totalHomeFgmS1 = 0L, totalHomeFgmS2 = 0L, totalHomeFgmS3 = 0L,
             totalHomeFgmS4 = 0L, totalHomeFgmS5 = 0L;
        long totalHomeUnattrFgm = 0L;
        long totalAwayFgmS1 = 0L, totalAwayFgmS2 = 0L, totalAwayFgmS3 = 0L,
             totalAwayFgmS4 = 0L, totalAwayFgmS5 = 0L;
        long totalAwayUnattrFgm = 0L;

        // Terminal mix — accumulated across all possessions, all games.
        var termBuckets = new Dictionary<string, long>
        {
            ["Made-FG"]          = 0L,
            ["FT-made"]          = 0L,
            ["DefensiveRebound"] = 0L,
            ["Turnover"]         = 0L,
            ["OOB"]              = 0L,
            ["JumpBall"]         = 0L,
            ["NoShot"]           = 0L,
            ["Parked"]           = 0L,
            ["Other"]            = 0L,
        };

        var mechanicsOk = true;

        // Phase 51: FTA-source running totals across all possessions, all games — the
        // corpus evidence that the pre-Roll-E "unattributed" FTA collapses to ~0 on the
        // populated observation rosters (it now lands in the bonus-picker bucket).
        long totalFtaBonusPicker = 0L, totalFtaBonusSelected = 0L,
             totalFtaBonusUnattributed = 0L, totalFtaShootingSelected = 0L,
             totalFtaShootingNoSlot = 0L;
        var gamesRun    = 0;

        // ── Phase 23 per-player box score accumulators ──────────────────────
        // Indexed by PlayerId - 1 (0..4 = Home S1–S5; 5..9 = Away S1–S5).
        // Exact stats (FGA/FGM/3PA/3PM/FTA/FTM): summed from PossessionRecord.
        // Weighted-credit stats (OReb/DReb/BLK/STL/TO): drawn post-run with
        //   attributionRng = new Random(seed + 2) — separate from all gameplay RNGs.
        var bsFga  = new long[10]; var bsFgm  = new long[10];
        var bs3pa  = new long[10]; var bs3pm  = new long[10];
        var bsFta  = new long[10]; var bsFtm  = new long[10];
        var bsTo   = new long[10]; var bsOReb = new long[10];
        var bsDReb = new long[10]; var bsStl  = new long[10];
        var bsBlk  = new long[10];
        // Phase 39: per-player assist counts — engine-stamped on-walk from AstBySlot.
        var bsAst  = new long[10];
        // Phase 25: shooting fouls committed (SFL) per player — weighted draw, seed+3 RNG.
        var bsShFoul = new long[10];
        var bsGames = 0;
        var attributionOk = true;   // hard-fail flag for post-loop attribution checks
        GovernorRunResult? seedOneResult = null;  // captured for reproducibility check

        // ── Frozen-corpus player display map ─────────────────────────────────
        // game is loop-scoped, so capture the 10 players on seed==1.
        // The frozen corpus is identical across all 1,000 games.
        var boxPlayers = new Player?[10];

        // ── Phase 23 unattributed running totals (for §5f CheckExact) ────────
        long totalHome3pa = 0L, totalAway3pa = 0L;
        long totalHome3pm = 0L, totalAway3pm = 0L;
        long totalHomeFta = 0L, totalAwayFta = 0L;
        long totalHomeFtm = 0L, totalAwayFtm = 0L;
        long totalHomeUnattr3pa = 0L, totalAwayUnattr3pa = 0L;
        long totalHomeUnattr3pm = 0L, totalAwayUnattr3pm = 0L;
        long totalHomeUnattrFta = 0L, totalAwayUnattrFta = 0L;
        long totalHomeUnattrFtm = 0L, totalAwayUnattrFtm = 0L;
        // Weighted-credit event totals
        long totalOrbWon   = 0L;
        long totalDrebPoss = 0L;
        long totalBlkCount = 0L;
        long totalStlPoss  = 0L;
        long totalToPoss   = 0L;
        long totalTeamViolToPoss = 0L;   // Phase 34: team violations (null TurnoverOffSlot — no individual credit)
        long totalAstBySlot = 0L;   // Phase 39: sum of AstBySlot.Total across all possessions

        Console.Write($"  Running {N} games");

        for (var seed = 1; seed <= N; seed++)
        {
            if (seed % 100 == 0) Console.Write(".");

            // Fresh game state per game: score, fouls, and arrow all start clean.
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            // Phase 23: seat starters with PlayerId stamped at construction time.
            // Home S1–S5 → IDs 1–5; Away S1–S5 → IDs 6–10.
            // IDs are set before SetStarter so no re-seating is needed.
            foreach (var idSide in new[] { TeamSide.Home, TeamSide.Away })
            {
                var idLineup  = game.LineupFor(idSide);
                var idRoster  = game.RosterFor(idSide);
                var idConfigs = idSide == TeamSide.Home ? cfgRoster.Home : cfgRoster.Away;
                for (var i = 0; i < Lineup.Size; i++)
                {
                    var newId = idSide == TeamSide.Home ? i + 1 : i + 6;
                    var player = StampPlayerId(idConfigs[i].ToPlayer(), newId);
                    idRoster.SetStarter(idLineup.SlotAt(i + 1), player);
                }
            }
            // Validate PlayerId contract once (seed==1) — catches unset IDs or duplicates
            // before 1,000 games run.
            if (seed == 1)
            {
                var seenIds = new HashSet<int>();
                foreach (var vs in new[] { TeamSide.Home, TeamSide.Away })
                    for (var vslot = 1; vslot <= 5; vslot++)
                    {
                        var p = game.RosterFor(vs).PlayerAt(new Slot(vs, vslot));
                        if (p is null) continue;
                        if (p.PlayerId < 1 || p.PlayerId > 10)
                            throw new InvalidOperationException($"Player {p.Name} has PlayerId {p.PlayerId} — must be 1–10");
                        if (!seenIds.Add(p.PlayerId))
                            throw new InvalidOperationException($"Duplicate PlayerId {p.PlayerId} ({p.Name})");
                    }
                if (seenIds.Count != 10)
                    throw new InvalidOperationException($"Expected 10 unique PlayerIds 1–10; got {seenIds.Count}");
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
                new RollEGenerator(cfgE, game),                        // Phase 19: attribute-driven usage selection
                new AttentionGenerator(AttentionConfig.Load(configPath), game), // Phase 27: defensive attention pie
                new RollFGenerator(cfgF, cfgMatchup, game),
                new RollGGenerator(cfgG, cfgMatchup, game),
                new RollHGenerator(cfgH, cfgMatchup, game),
                new RollIGenerator(cfgI, cfgMatchup, game),
                new RollJGenerator(cfgJ, cfgMatchup, game),
                new RollKGenerator(cfgK, cfgMatchup, game),
                new RollLGenerator(cfgL, game),                        // Phase 18: attribute-driven FT make%
                new RollMGenerator(cfgM, cfgMatchup, game),
                new RollOffensiveFoulGenerator(cfgOffFoul),
                cfgMatchup,
                game,
                resolverRng);

            var governor = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndOfHalf);

            GovernorRunResult result;
            try
            {
                result = governor.Run(firstState);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL-THROW] Seed {seed}: {ex.Message}");
                mechanicsOk = false;
                continue;
            }
            gamesRun++;

            var records = result.Possessions;
            var noShot  = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.NoShot);

            // ── Mechanical check 1: scoring reconciles ───────────────────
            var recHome = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Points);
            var recAway = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Points);
            if (game.HomeScore != recHome || game.AwayScore != recAway)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: score mismatch — state=({game.HomeScore},{game.AwayScore}) records=({recHome},{recAway})");
                mechanicsOk = false;
            }

            // ── Mechanical check 2: count invariant ─────────────────────
            if (result.TerminalEnded + result.Parked + noShot != records.Count)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: count invariant — {result.TerminalEnded}+{result.Parked}+{noShot} != {records.Count}");
                mechanicsOk = false;
            }

            // ── Mechanical check 3: zero parks ──────────────────────────
            if (result.Parked > 0)
            {
                Console.WriteLine();
                var stubDetail = string.Join(", ", result.PerStubParks.Select(kv => $"{kv.Key}:{kv.Value}"));
                Console.WriteLine($"  [FINDING] Seed {seed}: {result.Parked} parked — {stubDetail}");
                mechanicsOk = false;
            }

            // ── Mechanical check 4: loose sanity (NaN / ÷0 guard) ───────
            if (records.Count == 0) { mechanicsOk = false; continue; }
            if (records.Count > 200)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: pace exceeds outer bound ({records.Count})");
                mechanicsOk = false;
            }

            // ── Mechanical check — v2 counter reconciliation ────────────────────
            // Points == 2*(FGM - 3PM) + 3*3PM + FTM  (per game, summed across records)
            // This is the load-bearing check: proves new counters and the existing
            // points accumulator agree — every scoring event is tagged exactly once.
            var recFgm  = records.Sum(r => r.Fgm);
            var rec3pm  = records.Sum(r => r.ThreePm);
            var recFtm  = records.Sum(r => r.Ftm);
            var recPts  = records.Sum(r => r.Points);
            var expPts  = 2 * (recFgm - rec3pm) + 3 * rec3pm + recFtm;
            if (recPts != expPts)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: counter reconciliation — Points={recPts} expected={expPts} (FGM={recFgm} 3PM={rec3pm} FTM={recFtm})");
                mechanicsOk = false;
            }

            // ── Mechanical check — v2 denominator guard ─────────────────────────
            // FGA + MissFouled == ShotResolutions  (per game)
            // Also assert the per-counter inequalities.  These checks are blind to
            // the points reconciliation above (MissFouled scores zero), so they are
            // required to catch a wrong FGA definition.
            var recFga  = records.Sum(r => r.Fga);
            var recMf   = records.Sum(r => r.MissFouled);
            var recSr   = records.Sum(r => r.ShotResolutions);
            var rec3pa  = records.Sum(r => r.ThreePa);
            var recFta  = records.Sum(r => r.Fta);
            var recOrbW = records.Sum(r => r.OrbWon);
            var recOrbC = records.Sum(r => r.OrbChances);
            if (recFga + recMf != recSr)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: denominator guard — FGA({recFga}) + MissFouled({recMf}) != ShotResolutions({recSr})");
                mechanicsOk = false;
            }
            if (recFgm > recFga || rec3pm > rec3pa || rec3pa > recFga || recFtm > recFta || recOrbW > recOrbC)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: counter sanity — FGM={recFgm} FGA={recFga} 3PM={rec3pm} 3PA={rec3pa} FTM={recFtm} FTA={recFta} ORBwon={recOrbW} ORBchances={recOrbC}");
                mechanicsOk = false;
            }

            // ── Mechanical check — per-zone bin integrity ───────────────────────
            // Every FGA bins into exactly one of the five zones, and every FGM likewise.
            // (Three uses the existing ThreePa/ThreePm pair; the other four use the new
            // per-zone counters.) If a shot's zone ever failed to bin, these sums would
            // fall short of FGA/FGM and fail loud — the per-zone analog of the denominator
            // guard, pinning the zone splits to the totals.
            var recRimA   = records.Sum(r => r.RimFga);
            var recShortA = records.Sum(r => r.ShortFga);
            var recMidA   = records.Sum(r => r.MidFga);
            var recLongA  = records.Sum(r => r.LongFga);
            var recRimM   = records.Sum(r => r.RimFgm);
            var recShortM = records.Sum(r => r.ShortFgm);
            var recMidM   = records.Sum(r => r.MidFgm);
            var recLongM  = records.Sum(r => r.LongFgm);
            if (recRimA + recShortA + recMidA + recLongA + rec3pa != recFga)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: zone-attempt bin — Rim({recRimA})+Short({recShortA})+Mid({recMidA})+Long({recLongA})+Three({rec3pa}) != FGA({recFga})");
                mechanicsOk = false;
            }
            if (recRimM + recShortM + recMidM + recLongM + rec3pm != recFgm)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: zone-make bin — Rim({recRimM})+Short({recShortM})+Mid({recMidM})+Long({recLongM})+Three({rec3pm}) != FGM({recFgm})");
                mechanicsOk = false;
            }

            // ── Per-slot bin integrity ────────────────────────────────────────
            // Proves every FGA received exactly one valid slot bin (completeness).
            // Does NOT prove shooter identity — that is established by the Roll E
            // selection contract for normal attempts and the Roll K carry-through
            // contract for putbacks, as documented in the RoutingOutcome comment.
            var recS1 = records.Sum(r => r.Slot1Fga);
            var recS2 = records.Sum(r => r.Slot2Fga);
            var recS3 = records.Sum(r => r.Slot3Fga);
            var recS4 = records.Sum(r => r.Slot4Fga);
            var recS5 = records.Sum(r => r.Slot5Fga);
            var recSU = records.Sum(r => r.SlotUnattributedFga);
            if (recS1 + recS2 + recS3 + recS4 + recS5 + recSU != recFga)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: slot-attempt bin — " +
                    $"Slot1({recS1})+Slot2({recS2})+Slot3({recS3})+Slot4({recS4})+Slot5({recS5})+Unattr({recSU}) " +
                    $"!= FGA({recFga})");
                mechanicsOk = false;
            }

            // ── Per-slot FGM bin integrity (Phase 22) ─────────────────────────
            // Makes-only completeness: Slot1Fgm+…+Slot5Fgm+SlotUnattributedFgm == FGM.
            var recM1 = records.Sum(r => r.Slot1Fgm);
            var recM2 = records.Sum(r => r.Slot2Fgm);
            var recM3 = records.Sum(r => r.Slot3Fgm);
            var recM4 = records.Sum(r => r.Slot4Fgm);
            var recM5 = records.Sum(r => r.Slot5Fgm);
            var recMU = records.Sum(r => r.SlotUnattributedFgm);
            if (recM1 + recM2 + recM3 + recM4 + recM5 + recMU != recFgm)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: slot-make bin — " +
                    $"Slot1({recM1})+Slot2({recM2})+Slot3({recM3})+Slot4({recM4})+Slot5({recM5})+Unattr({recMU}) " +
                    $"!= FGM({recFgm})");
                mechanicsOk = false;
            }
            // ── Per-slot subset invariant (Phase 22) ──────────────────────────
            // STRUCTURAL, not a sanity bound: a make cannot exist without an attempt,
            // so per slot FGM <= FGA, and unattributed FGM <= unattributed FGA.
            // Completeness alone does NOT catch slot-level mis-attribution that nets
            // to the right global FGM (e.g. Slot1 FGM=6 > FGA=5 while Slot2 under-
            // counts); this subset check does. (recS1…recSU are the Phase 21 per-slot
            // FGA sums computed just above.)
            if (recM1 > recS1 || recM2 > recS2 || recM3 > recS3 ||
                recM4 > recS4 || recM5 > recS5 || recMU > recSU)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: slot FGM>FGA subset violation — " +
                    $"FGM(S1..S5,U)=({recM1},{recM2},{recM3},{recM4},{recM5},{recMU}) " +
                    $"FGA(S1..S5,U)=({recS1},{recS2},{recS3},{recS4},{recS5},{recSU})");
                mechanicsOk = false;
            }

            // ── Phase 23 attribution invariants ─────────────────────────────
            // 3PA completeness and subset
            var rec3paSlots = records.Sum(r => r.ThreePaBySlot.Total);
            var rec3pmSlots = records.Sum(r => r.ThreePmBySlot.Total);
            var rec3paCheck = records.Sum(r => r.ThreePa);
            var rec3pmCheck = records.Sum(r => r.ThreePm);
            if (rec3paSlots != rec3paCheck)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: 3PA slot total {rec3paSlots} != ThreePa {rec3paCheck}");
                mechanicsOk = false;
            }
            if (rec3pmSlots != rec3pmCheck)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: 3PM slot total {rec3pmSlots} != ThreePm {rec3pmCheck}");
                mechanicsOk = false;
            }
            if (rec3pmSlots > rec3paSlots)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: 3PM slots {rec3pmSlots} > 3PA slots {rec3paSlots}");
                mechanicsOk = false;
            }
            // FTA/FTM completeness and subset
            var recFtaSlots = records.Sum(r => r.FtaBySlot.Total);
            var recFtmSlots = records.Sum(r => r.FtmBySlot.Total);
            var recFtaCheck = records.Sum(r => r.Fta);
            var recFtmCheck = records.Sum(r => r.Ftm);
            if (recFtaSlots != recFtaCheck)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: FTA slot total {recFtaSlots} != Fta {recFtaCheck}");
                mechanicsOk = false;
            }
            if (recFtmSlots != recFtmCheck)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: FTM slot total {recFtmSlots} != Ftm {recFtmCheck}");
                mechanicsOk = false;
            }
            if (recFtmSlots > recFtaSlots)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: FTM slots {recFtmSlots} > FTA slots {recFtaSlots}");
                mechanicsOk = false;
            }
            // Phase 51: FTA-source classification reconciliation. Every FTA lands in
            // exactly one of five buckets (bonus-picker / bonus-selected /
            // bonus-unattributed / shooting-selected / shooting-no-slot), so per record
            // AND in aggregate they must sum to Fta. A mismatch means a FT trip was
            // counted into the wrong bucket or dropped.
            var recFtaBonusPicker       = records.Sum(r => r.FtaBonusPicker);
            var recFtaBonusSelected     = records.Sum(r => r.FtaBonusSelected);
            var recFtaBonusUnattributed = records.Sum(r => r.FtaBonusUnattributed);
            var recFtaShootingSelected  = records.Sum(r => r.FtaShootingSelected);
            var recFtaShootingNoSlot    = records.Sum(r => r.FtaShootingNoSlot);
            totalFtaBonusPicker       += recFtaBonusPicker;
            totalFtaBonusSelected     += recFtaBonusSelected;
            totalFtaBonusUnattributed += recFtaBonusUnattributed;
            totalFtaShootingSelected  += recFtaShootingSelected;
            totalFtaShootingNoSlot    += recFtaShootingNoSlot;
            var recFtaSourceTotal = recFtaBonusPicker + recFtaBonusSelected
                                  + recFtaBonusUnattributed + recFtaShootingSelected
                                  + recFtaShootingNoSlot;
            if (recFtaSourceTotal != recFtaCheck)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: FTA-source total {recFtaSourceTotal} != Fta {recFtaCheck}");
                mechanicsOk = false;
            }
            foreach (var r in records)
            {
                var perRec = r.FtaBonusPicker + r.FtaBonusSelected + r.FtaBonusUnattributed
                           + r.FtaShootingSelected + r.FtaShootingNoSlot;
                if (perRec != r.Fta)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  [FAIL] Seed {seed}: possession {r.Number} FTA-source total {perRec} != Fta {r.Fta}");
                    mechanicsOk = false;
                    break;
                }
            }
            // TurnoverWasLiveBall must match live-TO EndLabel exactly
            var recLiveToPoss = records.Count(r => r.TurnoverWasLiveBall);
            var recLiveToEndLabel = records.Count(r =>
                r.EndLabel is "BadPassIntercepted" or "LostBallLiveBall");
            if (recLiveToPoss != recLiveToEndLabel)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: TurnoverWasLiveBall count {recLiveToPoss} != live-TO endlabel count {recLiveToEndLabel}");
                mechanicsOk = false;
            }
            // Non-TO records must have null metadata — no leakage from prior possessions
            var nonToWithMeta = records.Count(r =>
                r.EndLabel is not ("BadPassDeadBall" or "BadPassIntercepted"
                    or "LostBallDeadBall" or "LostBallLiveBall" or "OffensiveFoul"
                    or "Travel" or "DoubleDribble" or "Carry" or "ThreeSecondViolation"
                    or "FiveSecondCloselyGuarded" or "OffensiveGoaltending"
                    or "BackcourtViolation" or "ShotClockViolation"
                    or "FiveSecondInbound" or "TenSecondBackcourt") &&
                (r.TurnoverOffSlot != null || r.TurnoverWasLiveBall));
            if (nonToWithMeta > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: {nonToWithMeta} non-TO records have turnover metadata set");
                mechanicsOk = false;
            }
            // Per-slot subset checks — catches slot-level corruption that aggregate totals miss
            for (var chkSlot = 0; chkSlot <= 5; chkSlot++)
            {
                var failed = false;
                foreach (var r in records)
                {
                    var slotFga = chkSlot == 0 ? r.SlotUnattributedFga : GetSlotFga(r, chkSlot);
                    var slotFgm = chkSlot == 0 ? r.SlotUnattributedFgm : GetSlotFgm(r, chkSlot);
                    if (r.ThreePmBySlot[chkSlot] > r.ThreePaBySlot[chkSlot])
                    {
                        Console.WriteLine(); Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} 3PM > 3PA in a possession");
                        mechanicsOk = false; failed = true; break;
                    }
                    if (r.ThreePaBySlot[chkSlot] > slotFga)
                    {
                        Console.WriteLine(); Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} 3PA > FGA in a possession");
                        mechanicsOk = false; failed = true; break;
                    }
                    if (r.ThreePmBySlot[chkSlot] > slotFgm)
                    {
                        Console.WriteLine(); Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} 3PM > FGM in a possession");
                        mechanicsOk = false; failed = true; break;
                    }
                    if (r.FtmBySlot[chkSlot] > r.FtaBySlot[chkSlot])
                    {
                        Console.WriteLine(); Console.WriteLine($"  [FAIL] Seed {seed}: slot {chkSlot} FTM > FTA in a possession");
                        mechanicsOk = false; failed = true; break;
                    }
                }
                if (failed) break;
            }

            var hPoss = records.Count(r => r.Offense == TeamSide.Home);
            var aPoss = records.Count(r => r.Offense == TeamSide.Away);
            var hPts  = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Points);
            var aPts  = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Points);
            var cPPP  = (double)(hPts + aPts) / records.Count;

            if (double.IsNaN(cPPP) || cPPP < 0.0 || cPPP > 3.0)
            {
                Console.WriteLine();
                Console.WriteLine($"  [FAIL] Seed {seed}: combined PPP out of outer bound ({cPPP:F4})");
                mechanicsOk = false;
            }

            // ── Accumulate sentinels ─────────────────────────────────────
            totalPossList.Add(records.Count);
            homePossList.Add(hPoss);
            awayPossList.Add(aPoss);
            noShotList.Add(noShot);
            homePPPList.Add(hPoss > 0 ? (double)hPts / hPoss : 0.0);
            awayPPPList.Add(aPoss > 0 ? (double)aPts / aPoss : 0.0);
            combinedPPPList.Add(cPPP);
            homeScoreList.Add(game.HomeScore);
            awayScoreList.Add(game.AwayScore);
            marginList.Add(game.HomeScore - game.AwayScore);

            var transCount = records.Count(r => r.Entry == EntryType.Transition);
            transFreqList.Add((double)transCount / records.Count);
            aplList.Add(result.TotalSeconds / records.Count);

            homeFoulsList.Add(game.Fouls.FoulsFor(TeamSide.Home));
            awayFoulsList.Add(game.Fouls.FoulsFor(TeamSide.Away));

            holdList.Add(records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.HoldShootLast));
            earlyList.Add(records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.ShootEarly));
            noShotIntList.Add(noShot);

            // ── v2 shooting / rebounding sentinel accumulation ───────────────────
            var hFga = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fga);
            var aFga = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fga);
            var hFgm = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fgm);
            var aFgm = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fgm);
            var h3pa = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePa);
            var a3pa = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePa);
            var h3pm = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePm);
            var a3pm = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePm);
            var hFta = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fta);
            var aFta = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fta);
            var hFtm = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Ftm);
            var aFtm = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Ftm);
            var tFga     = hFga + aFga;
            var t3pa     = h3pa + a3pa;
            var tFgm     = hFgm + aFgm;
            var t3pm     = h3pm + a3pm;
            var tFta     = hFta + aFta;
            var tFtm     = hFtm + aFtm;
            var tOrbWon  = records.Sum(r => r.OrbWon);
            var tOrbCh   = records.Sum(r => r.OrbChances);

            homeFgPctList.Add(hFga > 0     ? (double)hFgm / hFga     : 0.0);
            awayFgPctList.Add(aFga > 0     ? (double)aFgm / aFga     : 0.0);
            combinedFgPctList.Add(tFga > 0 ? (double)tFgm / tFga     : 0.0);
            home3pPctList.Add(h3pa > 0     ? (double)h3pm / h3pa     : 0.0);
            away3pPctList.Add(a3pa > 0     ? (double)a3pm / a3pa     : 0.0);
            combined3pPctList.Add(t3pa > 0 ? (double)t3pm / t3pa     : 0.0);  // 0.0 when zero 3PA (documented)
            homeFtPctList.Add(hFta > 0     ? (double)hFtm / hFta     : 0.0);
            awayFtPctList.Add(aFta > 0     ? (double)aFtm / aFta     : 0.0);
            combinedFtPctList.Add(tFta > 0 ? (double)tFtm / tFta     : 0.0);
            threePaRateList.Add(tFga > 0   ? (double)t3pa / tFga     : 0.0);
            orbPctList.Add(tOrbCh > 0      ? (double)tOrbWon / tOrbCh : 0.0);
            ftrList.Add(tFga > 0           ? (double)tFta / tFga     : 0.0);

            // Per-zone shooting (combined): FG% by zone and attempt share by zone.
            // Three reuses t3pa/t3pm (computed above); the other four sum the per-zone counters.
            var tRimA   = records.Sum(r => r.RimFga);
            var tShortA = records.Sum(r => r.ShortFga);
            var tMidA   = records.Sum(r => r.MidFga);
            var tLongA  = records.Sum(r => r.LongFga);
            var tRimM   = records.Sum(r => r.RimFgm);
            var tShortM = records.Sum(r => r.ShortFgm);
            var tMidM   = records.Sum(r => r.MidFgm);
            var tLongM  = records.Sum(r => r.LongFgm);
            rimFgPctList.Add(tRimA > 0     ? (double)tRimM / tRimA     : 0.0);
            shortFgPctList.Add(tShortA > 0 ? (double)tShortM / tShortA : 0.0);
            midFgPctList.Add(tMidA > 0     ? (double)tMidM / tMidA     : 0.0);
            longFgPctList.Add(tLongA > 0   ? (double)tLongM / tLongA   : 0.0);
            threeFgPctList.Add(t3pa > 0    ? (double)t3pm / t3pa       : 0.0);
            rimShareList.Add(tFga > 0      ? (double)tRimA / tFga      : 0.0);
            shortShareList.Add(tFga > 0    ? (double)tShortA / tFga    : 0.0);
            midShareList.Add(tFga > 0      ? (double)tMidA / tFga      : 0.0);
            longShareList.Add(tFga > 0     ? (double)tLongA / tFga     : 0.0);
            threeShareList.Add(tFga > 0    ? (double)t3pa / tFga       : 0.0);

            // Per-slot FGA running totals — accumulate for aggregate share.
            totalHomeFga   += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fga);
            totalHomeFgaS1 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot1Fga);
            totalHomeFgaS2 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot2Fga);
            totalHomeFgaS3 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot3Fga);
            totalHomeFgaS4 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot4Fga);
            totalHomeFgaS5 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot5Fga);
            totalHomeUnattr += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.SlotUnattributedFga);
            totalAwayFga   += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fga);
            totalAwayFgaS1 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot1Fga);
            totalAwayFgaS2 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot2Fga);
            totalAwayFgaS3 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot3Fga);
            totalAwayFgaS4 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot4Fga);
            totalAwayFgaS5 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot5Fga);
            totalAwayUnattr += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.SlotUnattributedFga);
            totalHomeFgmS1 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot1Fgm);
            totalHomeFgmS2 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot2Fgm);
            totalHomeFgmS3 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot3Fgm);
            totalHomeFgmS4 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot4Fgm);
            totalHomeFgmS5 += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Slot5Fgm);
            totalHomeUnattrFgm += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.SlotUnattributedFgm);
            totalAwayFgmS1 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot1Fgm);
            totalAwayFgmS2 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot2Fgm);
            totalAwayFgmS3 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot3Fgm);
            totalAwayFgmS4 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot4Fgm);
            totalAwayFgmS5 += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Slot5Fgm);
            totalAwayUnattrFgm += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.SlotUnattributedFgm);

            // ── Phase 23: unattributed running totals for §5f CheckExact ─────
            totalHome3pa += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePa);
            totalAway3pa += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePa);
            totalHome3pm += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePm);
            totalAway3pm += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePm);
            totalHomeFta += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Fta);
            totalAwayFta += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Fta);
            totalHomeFtm += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Ftm);
            totalAwayFtm += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Ftm);
            totalHomeUnattr3pa += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePaBySlot.Unattr);
            totalAwayUnattr3pa += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePaBySlot.Unattr);
            totalHomeUnattr3pm += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.ThreePmBySlot.Unattr);
            totalAwayUnattr3pm += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.ThreePmBySlot.Unattr);
            totalHomeUnattrFta += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.FtaBySlot.Unattr);
            totalAwayUnattrFta += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.FtaBySlot.Unattr);
            totalHomeUnattrFtm += records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.FtmBySlot.Unattr);
            totalAwayUnattrFtm += records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.FtmBySlot.Unattr);
            // Weighted-credit event totals
            totalOrbWon   += records.Sum(r => r.OrbWon);
            totalDrebPoss += records.Count(r => r.EndLabel == "DefensiveRebound");
            totalBlkCount += records.Sum(r => r.BlkCount);
            totalStlPoss  += records.Count(r => r.TurnoverWasLiveBall);
            totalToPoss   += records.Count(IsTurnoverPossession);
            totalTeamViolToPoss += records.Count(r =>
                r.EndLabel is "FiveSecondInbound" or "TenSecondBackcourt" or "ShotClockViolation");
            totalAstBySlot += records.Sum(r => r.AstBySlot.Total);

            // ── Phase 23: capture player display map on seed==1 ──────────────
            if (seed == 1)
            {
                for (var slot = 1; slot <= 5; slot++)
                {
                    boxPlayers[slot - 1] = game.RosterFor(TeamSide.Home)
                                               .PlayerAt(new Slot(TeamSide.Home, slot));
                    boxPlayers[slot + 4] = game.RosterFor(TeamSide.Away)
                                               .PlayerAt(new Slot(TeamSide.Away, slot));
                }
                seedOneResult = result;
            }

            // ── Phase 23: per-game attribution ───────────────────────────────
            // AttributeGame uses Random(seed+2) — independent of all gameplay RNGs.
            bsGames++;
            var attributed = AttributeGame(result, game, seed);
            for (var i = 0; i < 10; i++)
            {
                bsFga [i] += attributed.Fga [i]; bsFgm [i] += attributed.Fgm [i];
                bs3pa [i] += attributed.Tpa [i]; bs3pm [i] += attributed.Tpm [i];
                bsFta [i] += attributed.Fta [i]; bsFtm [i] += attributed.Ftm [i];
                bsOReb[i] += attributed.OReb[i]; bsDReb[i] += attributed.DReb[i];
                bsBlk [i] += attributed.Blk [i]; bsStl [i] += attributed.Stl [i];
                bsTo  [i] += attributed.To  [i];
                // Phase 25: accumulate shooting-foul credits (seed+3 RNG — seed+2 unchanged).
                bsShFoul[i] += attributed.ShFoul[i];
                // Phase 39: accumulate assist credits (engine-stamped — no additional RNG).
                bsAst [i] += attributed.Ast [i];
            }

            // ── Phase 25: shooting-foul per-possession validity checks ────────
            // ShooterSlot must be 0–5; zone must be a defined ShotLocation enum value.
            // These are event-level checks (per-possession); completeness is below.
            foreach (var r in records)
            {
                if (r.ShootingFouls is null) continue;
                foreach (var sf in r.ShootingFouls)
                {
                    if (sf.ShooterSlot < 0 || sf.ShooterSlot > 5)
                    {
                        Console.WriteLine($"  [FAIL] Seed {seed}: ShooterSlot {sf.ShooterSlot} outside 0–5");
                        attributionOk = false;
                    }
                    if (!Enum.IsDefined(typeof(ShotLocation), sf.Zone))
                    {
                        Console.WriteLine($"  [FAIL] Seed {seed}: ShootingFoulEvent.Zone {sf.Zone} is not a defined ShotLocation");
                        attributionOk = false;
                    }
                }
            }

            foreach (var r in records)
            {
                var b = ObsBucket(r.EndLabel);
                termBuckets[b] = termBuckets[b] + 1;
            }
        }

        Console.WriteLine($" done ({gamesRun}/{N} completed).");
        Console.WriteLine();

        // ── Mechanics verdict ─────────────────────────────────────────────
        Console.WriteLine(mechanicsOk
            ? "  Mechanics:  ALL OK — scoring reconciled, count invariant held, zero parks, no throws"
            : "  Mechanics:  FAILURES ABOVE — some sentinel values may be unreliable");
        Console.WriteLine();

        if (gamesRun == 0) { Console.WriteLine("  No games completed."); return; }

        // ── Sentinel block ────────────────────────────────────────────────

        Console.WriteLine("--- PACE (total possessions per game) ---");
        ObsPrintI("  Total",                              totalPossList);
        ObsPrintI("  Home",                               homePossList);
        ObsPrintI("  Away",                               awayPossList);
        ObsPrintI("  NoShot (end-of-half, per game)",    noShotList);
        Console.WriteLine("  Distribution (total):");
        ObsHistI(totalPossList, new[] { 90, 100, 110, 120, 130, 140, 150, 160 });

        Console.WriteLine();
        Console.WriteLine("--- PPP (points per possession) ---");
        ObsPrintD("  Home",     homePPPList);
        ObsPrintD("  Away",     awayPPPList);
        ObsPrintD("  Combined", combinedPPPList);
        Console.WriteLine("  Distribution (combined):");
        ObsHistD(combinedPPPList, new[] { 0.5, 0.7, 0.9, 1.1, 1.3, 1.5 });

        Console.WriteLine();
        Console.WriteLine("--- SCORE & MARGIN ---");
        ObsPrintI("  Home score",         homeScoreList);
        ObsPrintI("  Away score",         awayScoreList);
        ObsPrintI("  Margin (Home−Away)", marginList);

        Console.WriteLine();
        Console.WriteLine("--- TRANSITION FREQUENCY (Transition entries / total possessions) ---");
        ObsPrintD("  Transition freq", transFreqList);

        Console.WriteLine();
        Console.WriteLine("--- TERMINAL MIX (fractions across all possessions all games) ---");
        var totalMix = termBuckets.Values.Sum();
        foreach (var kv in termBuckets)
            Console.WriteLine($"  {kv.Key,-22} {(totalMix > 0 ? (double)kv.Value / totalMix : 0.0):F4}  ({kv.Value:N0})");

        Console.WriteLine();
        Console.WriteLine("--- END-OF-HALF INTENT (counts per game) ---");
        ObsPrintI("  HoldShootLast", holdList);
        ObsPrintI("  ShootEarly",    earlyList);
        ObsPrintI("  NoShot",        noShotIntList);

        Console.WriteLine();
        Console.WriteLine("--- APL (avg possession length, seconds) ---");
        ObsPrintD("  APL", aplList);

        Console.WriteLine();
        Console.WriteLine("--- FOULS PER TEAM PER GAME ---");
        ObsPrintI("  Home fouls", homeFoulsList);
        ObsPrintI("  Away fouls", awayFoulsList);

        Console.WriteLine();
        Console.WriteLine("--- SHOOTING SPLITS ---");
        Console.WriteLine("  FG%:");
        ObsPrintD("    Home",     homeFgPctList);
        ObsPrintD("    Away",     awayFgPctList);
        ObsPrintD("    Combined", combinedFgPctList);
        Console.WriteLine("  Distribution (FG% combined):");
        ObsHistD(combinedFgPctList, new[] { 0.20, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60 });
        Console.WriteLine("  3P%:  (games with zero 3PA report 0.0 — possible in principle)");
        ObsPrintD("    Home",     home3pPctList);
        ObsPrintD("    Away",     away3pPctList);
        ObsPrintD("    Combined", combined3pPctList);
        Console.WriteLine("  FT%:");
        ObsPrintD("    Home",     homeFtPctList);
        ObsPrintD("    Away",     awayFtPctList);
        ObsPrintD("    Combined", combinedFtPctList);
        Console.WriteLine("  Distribution (FT% combined):");
        ObsHistD(combinedFtPctList, new[] { 0.40, 0.50, 0.60, 0.70, 0.80, 0.90 });
        Console.WriteLine("  NOTE: FT% reflects authored FreeThrow ratings where SelectedSlot is non-null.");
        Console.WriteLine("  Bonus trips before Roll E retain the config.MakeProbability (72%) fallback.");
        Console.WriteLine("  This is a named remaining loose end — not a bug.");

        Console.WriteLine();
        Console.WriteLine("--- SHOT MIX ---");
        ObsPrintD("  3PA rate (3PA/FGA, combined)", threePaRateList);

        Console.WriteLine();
        Console.WriteLine("--- SHOOTING BY ZONE (combined, per game) ---");
        Console.WriteLine("  FG% by zone:");
        ObsPrintD("    Rim",   rimFgPctList);
        ObsPrintD("    Short", shortFgPctList);
        ObsPrintD("    Mid",   midFgPctList);
        ObsPrintD("    Long",  longFgPctList);
        ObsPrintD("    Three", threeFgPctList);
        Console.WriteLine("  Attempt share by zone (fraction of FGA from each zone):");
        ObsPrintD("    Rim",   rimShareList);
        ObsPrintD("    Short", shortShareList);
        ObsPrintD("    Mid",   midShareList);
        ObsPrintD("    Long",  longShareList);
        ObsPrintD("    Three", threeShareList);

        Console.WriteLine();
        Console.WriteLine("--- FREE-THROW SOURCE (FTA by entry edge + shooter identity, aggregate across all games) ---");
        var totalFtaAll = totalFtaBonusPicker + totalFtaBonusSelected + totalFtaBonusUnattributed
                        + totalFtaShootingSelected + totalFtaShootingNoSlot;
        double FtaSh(long n) => totalFtaAll > 0 ? (double)n / totalFtaAll : 0.0;
        Console.WriteLine($"  Total FTA: {totalFtaAll:N0}");
        Console.WriteLine($"  Bonus, picker shooter     : {totalFtaBonusPicker,10:N0}  ({FtaSh(totalFtaBonusPicker):P2})   <- Phase 51 (drew the foul, real rating)");
        Console.WriteLine($"  Bonus, selected shooter   : {totalFtaBonusSelected,10:N0}  ({FtaSh(totalFtaBonusSelected):P2})   (post-Roll-E bonus)");
        Console.WriteLine($"  Bonus, UNATTRIBUTED (72%) : {totalFtaBonusUnattributed,10:N0}  ({FtaSh(totalFtaBonusUnattributed):P2})   <- should be ~0 on populated rosters");
        Console.WriteLine($"  Shooting foul, selected   : {totalFtaShootingSelected,10:N0}  ({FtaSh(totalFtaShootingSelected):P2})");
        Console.WriteLine($"  Shooting foul, no-slot    : {totalFtaShootingNoSlot,10:N0}  ({FtaSh(totalFtaShootingNoSlot):P2})   (post-FT-rebound putback exception)");

        Console.WriteLine();
        Console.WriteLine("--- USAGE (per-slot FGA share, aggregate across all games) ---");
        var hS1sh = totalHomeFga > 0 ? (double)totalHomeFgaS1 / totalHomeFga : 0.0;
        var hS2sh = totalHomeFga > 0 ? (double)totalHomeFgaS2 / totalHomeFga : 0.0;
        var hS3sh = totalHomeFga > 0 ? (double)totalHomeFgaS3 / totalHomeFga : 0.0;
        var hS4sh = totalHomeFga > 0 ? (double)totalHomeFgaS4 / totalHomeFga : 0.0;
        var hS5sh = totalHomeFga > 0 ? (double)totalHomeFgaS5 / totalHomeFga : 0.0;
        var hSUsh = totalHomeFga > 0 ? (double)totalHomeUnattr  / totalHomeFga : 0.0;
        var aS1sh = totalAwayFga > 0 ? (double)totalAwayFgaS1 / totalAwayFga : 0.0;
        var aS2sh = totalAwayFga > 0 ? (double)totalAwayFgaS2 / totalAwayFga : 0.0;
        var aS3sh = totalAwayFga > 0 ? (double)totalAwayFgaS3 / totalAwayFga : 0.0;
        var aS4sh = totalAwayFga > 0 ? (double)totalAwayFgaS4 / totalAwayFga : 0.0;
        var aS5sh = totalAwayFga > 0 ? (double)totalAwayFgaS5 / totalAwayFga : 0.0;
        var aSUsh = totalAwayFga > 0 ? (double)totalAwayUnattr  / totalAwayFga : 0.0;
        Console.WriteLine($"  Home: Slot1={hS1sh:P1}  Slot2={hS2sh:P1}  Slot3={hS3sh:P1}  Slot4={hS4sh:P1}  Slot5={hS5sh:P1}  Unattr={hSUsh:P1}");
        Console.WriteLine($"  Away: Slot1={aS1sh:P1}  Slot2={aS2sh:P1}  Slot3={aS3sh:P1}  Slot4={aS4sh:P1}  Slot5={aS5sh:P1}  Unattr={aSUsh:P1}");
        Console.WriteLine("  Per-slot FG% (SlotFgm / SlotFga):");
        var hF1 = totalHomeFgaS1 > 0 ? (double)totalHomeFgmS1 / totalHomeFgaS1 : 0.0;
        var hF2 = totalHomeFgaS2 > 0 ? (double)totalHomeFgmS2 / totalHomeFgaS2 : 0.0;
        var hF3 = totalHomeFgaS3 > 0 ? (double)totalHomeFgmS3 / totalHomeFgaS3 : 0.0;
        var hF4 = totalHomeFgaS4 > 0 ? (double)totalHomeFgmS4 / totalHomeFgaS4 : 0.0;
        var hF5 = totalHomeFgaS5 > 0 ? (double)totalHomeFgmS5 / totalHomeFgaS5 : 0.0;
        var aF1 = totalAwayFgaS1 > 0 ? (double)totalAwayFgmS1 / totalAwayFgaS1 : 0.0;
        var aF2 = totalAwayFgaS2 > 0 ? (double)totalAwayFgmS2 / totalAwayFgaS2 : 0.0;
        var aF3 = totalAwayFgaS3 > 0 ? (double)totalAwayFgmS3 / totalAwayFgaS3 : 0.0;
        var aF4 = totalAwayFgaS4 > 0 ? (double)totalAwayFgmS4 / totalAwayFgaS4 : 0.0;
        var aF5 = totalAwayFgaS5 > 0 ? (double)totalAwayFgmS5 / totalAwayFgaS5 : 0.0;
        Console.WriteLine($"  Home: Slot1={hF1:P1}  Slot2={hF2:P1}  Slot3={hF3:P1}  Slot4={hF4:P1}  Slot5={hF5:P1}");
        Console.WriteLine($"  Away: Slot1={aF1:P1}  Slot2={aF2:P1}  Slot3={aF3:P1}  Slot4={aF4:P1}  Slot5={aF5:P1}");
        Console.WriteLine("  NOTE: per-slot, not per-player — valid while lineups are fixed. Unattr = bonus-FT putbacks (Roll E never ran).");

        Console.WriteLine();
        Console.WriteLine("--- ORB% (offensive rebound rate, FG-miss + block + FT misses combined) ---");
        ObsPrintD("  Combined", orbPctList);

        Console.WriteLine();
        Console.WriteLine("--- FTr (free-throw rate = FTA/FGA, combined) ---");
        ObsPrintD("  Combined", ftrList);

        Console.WriteLine();
        Console.WriteLine("--- DEFERRED SENTINELS (counter-plumbing needed — future session) ---");
        Console.WriteLine("  Press frequency / break rate at game level");

        // ── Phase 23 §5f: post-loop exact-stat reconciliation ────────────────
        Console.WriteLine();
        Console.WriteLine("--- PHASE 23 ATTRIBUTION CHECKS ---");
        void CheckExact(string label, long named, long total, long unattr) {
            if (named != total - unattr)
            { Console.WriteLine($"  [FAIL] {label}: named={named} != total-unattr={total - unattr}"); attributionOk = false; }
        }
        long NamedHome(long[] arr) => arr[0]+arr[1]+arr[2]+arr[3]+arr[4];
        long NamedAway(long[] arr) => arr[5]+arr[6]+arr[7]+arr[8]+arr[9];

        // FGA/FGM use Phase 21 unattributed scalars
        CheckExact("FGA Home", NamedHome(bsFga), totalHomeFga, totalHomeUnattr);
        CheckExact("FGA Away", NamedAway(bsFga), totalAwayFga, totalAwayUnattr);
        // FGM total = named S1..S5 + unattr; CheckExact verifies named == total - unattr
        var totalHomeFgm = totalHomeFgmS1+totalHomeFgmS2+totalHomeFgmS3+totalHomeFgmS4+totalHomeFgmS5+totalHomeUnattrFgm;
        var totalAwayFgm = totalAwayFgmS1+totalAwayFgmS2+totalAwayFgmS3+totalAwayFgmS4+totalAwayFgmS5+totalAwayUnattrFgm;
        CheckExact("FGM Home", NamedHome(bsFgm), totalHomeFgm, totalHomeUnattrFgm);
        CheckExact("FGM Away", NamedAway(bsFgm), totalAwayFgm, totalAwayUnattrFgm);
        CheckExact("3PA Home", NamedHome(bs3pa), totalHome3pa, totalHomeUnattr3pa);
        CheckExact("3PA Away", NamedAway(bs3pa), totalAway3pa, totalAwayUnattr3pa);
        CheckExact("3PM Home", NamedHome(bs3pm), totalHome3pm, totalHomeUnattr3pm);
        CheckExact("3PM Away", NamedAway(bs3pm), totalAway3pm, totalAwayUnattr3pm);
        CheckExact("FTA Home", NamedHome(bsFta), totalHomeFta, totalHomeUnattrFta);
        CheckExact("FTA Away", NamedAway(bsFta), totalAwayFta, totalAwayUnattrFta);
        CheckExact("FTM Home", NamedHome(bsFtm), totalHomeFtm, totalHomeUnattrFtm);
        CheckExact("FTM Away", NamedAway(bsFtm), totalAwayFtm, totalAwayUnattrFtm);

        // Weighted-credit totals: every event fires exactly one credit
        var bsORebTotal = bsOReb.Sum();
        var bsDRebTotal = bsDReb.Sum();
        var bsBlkTotal  = bsBlk.Sum();
        var bsStlTotal  = bsStl.Sum();
        var bsToTotal   = bsTo.Sum();
        if (bsORebTotal  != totalOrbWon)   { Console.WriteLine($"  [FAIL] Per-player OReb {bsORebTotal} != OrbWon {totalOrbWon}");   attributionOk = false; }
        if (bsDRebTotal  != totalDrebPoss) { Console.WriteLine($"  [FAIL] Per-player DReb {bsDRebTotal} != DReb possessions {totalDrebPoss}"); attributionOk = false; }
        if (bsBlkTotal   != totalBlkCount) { Console.WriteLine($"  [FAIL] Per-player BLK {bsBlkTotal} != BlkCount {totalBlkCount}");    attributionOk = false; }
        // NOTE: BLK proves downward (every BlkCount credit distributed) but not upward
        // (Resolver captured every block in BlkCount). Upward validation is by code placement.
        if (bsStlTotal   != totalStlPoss)  { Console.WriteLine($"  [FAIL] Per-player STL {bsStlTotal} != live-TO possessions {totalStlPoss}");  attributionOk = false; }
        if (bsToTotal    != totalToPoss - totalTeamViolToPoss)
        {
            Console.WriteLine($"  [FAIL] Per-player TO {bsToTotal} != individual-TO possessions {totalToPoss - totalTeamViolToPoss} " +
                              $"(total TO {totalToPoss}, team violations {totalTeamViolToPoss} correctly unattributed — Phase 34)");
            attributionOk = false;
        }
        // Phase 39: AST reconciliation — Σ per-player AST == Σ AstBySlot.Total across all possessions.
        // AstBySlot.Total <= FGM is asserted per-possession in Phase39AssistCheck.
        var bsAstTotal = bsAst.Sum();
        // totalAstBySlot accumulated per-game in the main loop above.
        if (bsAstTotal != totalAstBySlot)
        {
            Console.WriteLine($"  [FAIL] Per-player AST {bsAstTotal} != AstBySlot.Total {totalAstBySlot} — Phase 39 wiring break.");
            attributionOk = false;
        }

        // ── Phase 23 §5g: same-seed reproducibility ───────────────────────────
        if (seedOneResult != null)
        {
            var repGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            // Seat with PlayerId stamped at construction — same pattern as main loop.
            foreach (var repSide in new[] { TeamSide.Home, TeamSide.Away })
            {
                var repLineup  = repGame.LineupFor(repSide);
                var repRoster  = repGame.RosterFor(repSide);
                var repConfigs = repSide == TeamSide.Home ? cfgRoster.Home : cfgRoster.Away;
                for (var i = 0; i < Lineup.Size; i++)
                {
                    var newId = repSide == TeamSide.Home ? i + 1 : i + 6;
                    repRoster.SetStarter(repLineup.SlotAt(i + 1), StampPlayerId(repConfigs[i].ToPlayer(), newId));
                }
            }
            var rep1 = AttributeGame(seedOneResult, repGame, 1);
            var rep2 = AttributeGame(seedOneResult, repGame, 1);
            if (!PlayerBoxTotals.AllEqual(rep1, rep2))
            {
                Console.WriteLine("  [FAIL] Same-seed reproducibility: AttributeGame produced different results on identical inputs");
                attributionOk = false;
            }
            else
            {
                Console.WriteLine("  [OK] Same-seed reproducibility confirmed.");
            }
        }

        // ── Phase 25: shooting-foul completeness invariant ────────────────────
        // Side-specific reconciliation: credited fouls for each team must equal the
        // total shooting-foul events where that team was on defense. A global check
        // alone would pass a side-reversal bug; per-side checks catch it.
        {
            long totalHomeShFoulEvents = 0L;   // possessions where Home defended
            long totalAwayShFoulEvents = 0L;   // possessions where Away defended
            foreach (var r in seedOneResult!.Possessions)
            {
                if (r.ShootingFouls is null) continue;
                if (r.Defense == TeamSide.Home) totalHomeShFoulEvents += r.ShootingFouls.Count;
                else                            totalAwayShFoulEvents += r.ShootingFouls.Count;
            }
            // Note: bsShFoul is an aggregate across all 1,000 games, so we cannot
            // do a per-seed side check here without re-running attribution. The
            // invariant below therefore checks totals across all games. The strong
            // per-side check (requiring game-level defense side access) runs in
            // Phase25ShootingFoulAttributionCheck's 200-game end-to-end path.
            var creditedTotal = bsShFoul.Sum();
            // Re-compute total shooting-foul events across all games from accumulated data.
            // (ObservationRunV1 does not maintain a running total, so we rely on the
            // Phase 25 check's 200-game path for the hard per-side assertion.)
            Console.WriteLine();
            Console.WriteLine($"  Phase 25 shooting-foul summary (1,000 games):");
            Console.WriteLine($"    Seed-1 game — Home defense events: {totalHomeShFoulEvents}, Away defense events: {totalAwayShFoulEvents}");
            Console.WriteLine($"    Slot-0 events (bonus-FT putback, expected small): {seedOneResult.Possessions.Where(r => r.ShootingFouls != null).SelectMany(r => r.ShootingFouls!).Count(sf => sf.ShooterSlot == 0)}");
            Console.WriteLine($"    Total SFL credits across all 1,000 games: {creditedTotal}");
            if (creditedTotal == 0 && bsGames > 0)
            {
                Console.WriteLine("  [FAIL] Phase 25: zero SFL credits across all games — wiring break");
                attributionOk = false;
            }
            else
            {
                Console.WriteLine("  [OK] Phase 25: SFL credits populated (full per-side invariant in Phase25ShootingFoulAttributionCheck).");
            }
        }

        // ── Phase 23 §5h: per-player box score ───────────────────────────────
        Console.WriteLine();
        Console.WriteLine($"=== PER-PLAYER BOX SCORE (per-game averages, {bsGames} games) ===");
        Console.WriteLine("  Exact attribution: FGA, FGM, 3PA, 3PM, FTA, FTM.");
        Console.WriteLine("  Weighted credit (probabilistic): ORB, DRB, REB, STL, BLK, TO (post-Roll-E exact; pre-Roll-E by BallHandling weight).");
        Console.WriteLine("  SFL = shooting fouls committed; excludes all non-shooting and offensive fouls.");
        Console.WriteLine($"  {"Player",-22} {"PTS",5} {"FGA",5} {"FGM",5} {"FG%",5} {"3PA",5} {"3PM",5} {"3P%",5} {"FTA",5} {"FTM",5} {"FT%",5} {"ORB",5} {"DRB",5} {"REB",5} {"STL",5} {"BLK",5} {"AST",5} {"TO",5} {"SFL",5}");
        Console.WriteLine(new string('─', 121));
        for (var i = 0; i < 10; i++)
        {
            var player = boxPlayers[i];
            if (player is null) continue;
            {
                double g = bsGames;
                var fga  = bsFga[i]   / g;  var fgm  = bsFgm[i]   / g;
                var tpa  = bs3pa[i]   / g;  var tpm  = bs3pm[i]   / g;
                var fta  = bsFta[i]   / g;  var ftm  = bsFtm[i]   / g;
                var orb  = bsOReb[i]  / g;  var drb  = bsDReb[i]  / g;
                var stl  = bsStl[i]   / g;  var blk  = bsBlk[i]   / g;
                var to   = bsTo[i]    / g;
                var sfl  = bsShFoul[i]/ g;
                var ast  = bsAst[i]   / g;
                var pts  = (fgm - tpm) * 2.0 + tpm * 3.0 + ftm;
                var fgPct  = fga  > 0 ? fgm  / fga  * 100 : 0.0;
                var tpPct  = tpa  > 0 ? tpm  / tpa  * 100 : 0.0;
                var ftPct  = fta  > 0 ? ftm  / fta  * 100 : 0.0;
                var side = i < 5 ? TeamSide.Home : TeamSide.Away;
                var label = $"[{(side == TeamSide.Home ? "Home" : "Away")}] {player.Name}";
                Console.WriteLine(
                    $"  {label,-22} {pts,5:F1} {fga,5:F1} {fgm,5:F1} {fgPct,5:F1} " +
                    $"{tpa,5:F1} {tpm,5:F1} {tpPct,5:F1} {fta,5:F1} {ftm,5:F1} {ftPct,5:F1} " +
                    $"{orb,5:F1} {drb,5:F1} {(orb+drb),5:F1} {stl,5:F1} {blk,5:F1} {ast,5:F1} {to,5:F1} {sfl,5:F1}");
            }
        }
        Console.WriteLine($"=== END PER-PLAYER BOX SCORE ===");

        // ── Combined pass/fail banner ─────────────────────────────────────────
        Console.WriteLine();
        if (mechanicsOk && attributionOk)
            Console.WriteLine("  ALL CHECKS PASSED");
        else
            Console.WriteLine("  ONE OR MORE CHECKS FAILED — see [FAIL] lines above");

        Console.WriteLine();
        Console.WriteLine($"=== END OBSERVATION RUN — {CorpusId} ===");
    }


    /// <summary>
    /// Classify an EndLabel into one of the terminal-mix buckets. By prefix, coarse.
    /// "Turnover" catches all fouls, violations, and live/dead-ball turnovers — everything
    /// that ends the possession without a made shot or defensive rebound.
    /// </summary>
    private static string ObsBucket(string endLabel) =>
        endLabel == "Made"                             ? "Made-FG"
        : endLabel == "FreeThrowsMade"                 ? "FT-made"
        : endLabel == "DefensiveRebound"               ? "DefensiveRebound"
        : endLabel.StartsWith("JumpBall")             ? "JumpBall"
        : endLabel.StartsWith("MissOutOfBounds") ||
          endLabel.StartsWith("OutOfBounds")          ? "OOB"
        : endLabel == "endOfHalf:NoShot"              ? "NoShot"
        : endLabel.StartsWith("parked:")              ? "Parked"
        : "Turnover";  // fouls, violations, live/dead-ball turnovers

    private static void ObsPrintI(string label, List<int> v)
    {
        if (v.Count == 0) return;
        var mean = v.Average();
        var sd   = Math.Sqrt(v.Select(x => (x - mean) * (x - mean)).Average());
        Console.WriteLine($"  {label,-40} mean={mean,7:F1}  sd={sd,5:F1}  min={v.Min(),5}  max={v.Max(),5}");
    }

    private static void ObsPrintD(string label, List<double> v)
    {
        if (v.Count == 0) return;
        var mean = v.Average();
        var sd   = Math.Sqrt(v.Select(x => (x - mean) * (x - mean)).Average());
        Console.WriteLine($"  {label,-40} mean={mean,7:F4}  sd={sd,6:F4}  min={v.Min(),7:F4}  max={v.Max(),7:F4}");
    }

    private static void ObsHistI(List<int> v, int[] edges)
    {
        var n = v.Count;
        if (n == 0) return;
        var under = v.Count(x => x < edges[0]);
        if (under > 0) Console.WriteLine($"    <{edges[0],5}       {(double)under / n,6:P1}  ({under})");
        for (var i = 0; i < edges.Length - 1; i++)
        {
            var lo = edges[i]; var hi = edges[i + 1];
            var cnt = v.Count(x => x >= lo && x < hi);
            Console.WriteLine($"    [{lo,3},{hi,3})    {(double)cnt / n,6:P1}  ({cnt})");
        }
        var over = v.Count(x => x >= edges[^1]);
        if (over > 0) Console.WriteLine($"    >={edges[^1],5}     {(double)over / n,6:P1}  ({over})");
    }

    private static void ObsHistD(List<double> v, double[] edges)
    {
        var n = v.Count;
        if (n == 0) return;
        var under = v.Count(x => x < edges[0]);
        if (under > 0) Console.WriteLine($"    <{edges[0]:F1}        {(double)under / n,6:P1}  ({under})");
        for (var i = 0; i < edges.Length - 1; i++)
        {
            var lo = edges[i]; var hi = edges[i + 1];
            var cnt = v.Count(x => x >= lo && x < hi);
            Console.WriteLine($"    [{lo:F1},{hi:F1})    {(double)cnt / n,6:P1}  ({cnt})");
        }
        var over = v.Count(x => x >= edges[^1]);
        if (over > 0) Console.WriteLine($"    >={edges[^1]:F1}      {(double)over / n,6:P1}  ({over})");
    }

}
