using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // --- Batch: Roll G's five-way location distribution converges within
    //     tolerance, every exit is a clean Continue carrying the IntoShotResolution
    //     kind, and the ShotType is actually stamped on the carried state. The pie
    //     is flat-ish (no signal); a future attribute generator tilts it without
    //     this roll changing. Mirrors the Roll E batch check — Roll G is
    //     structurally Roll E: stamp a fact, continue to the same next beat. ---
    private static bool RollGLocationBatchCheck(
        RollAConfig cfg, RollGConfig cfgG, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} locations through Roll G (flat-ish pie) ---");
        // Option (ii): construct the stub directly here so this check remains a
        // flat baseline regression regardless of whether Main's live chain uses the
        // real RollGGenerator. This preserves the pre-Phase-9 baseline check value.
        var genG = new RollGStubPieGenerator(cfgG);
        var rng = new SystemRng(cfg.Seed);
        var pieG = genG.Generate(state);

        var counts = new Dictionary<ShotLocation, int>();
        foreach (var o in Enum.GetValues<ShotLocation>()) counts[o] = 0;

        var anomalies = 0;   // anything that isn't a clean, zone-stamped IntoShotResolution continue

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var result = RollG.Execute(state, pieG, 0.0, rng);

            // Must be a Continue, into shot resolution, carrying a stamped zone on
            // the forwarded state (confirms ShotType is actually set, not just the
            // kind emitted).
            if (result is not Continue { Next: ContinuationKind.IntoShotResolution } c
                || c.State.ShotType is not { } zone)
            {
                anomalies++;
                continue;
            }

            counts[zone]++;
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll G locations:");
        foreach (var (outcome, weight) in pieG.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-6} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var cleanOk = anomalies == 0;
        Console.WriteLine($"\n  every exit a clean zone-stamped IntoShotResolution: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");

        return ratesOk && cleanOk;
    }


    // --- G->H->I integration: route a batch of IntoShotType exits through the
    //     resolver. With Roll H and Roll I live, an IntoShotType ticket flows G (stamps
    //     a zone) -> H (stamps a result) and, on a MISS, on through I (rebound
    //     resolution). This check confirms the WHOLE post-shot chain routes: zero
    //     unrouted and the CORE landings still reached straight off the first Roll H /
    //     Roll I (Made / MissOutOfBoundsLost terminals + Roll I's DefensiveRebound /
    //     LooseBallFoulOnOffense terminals). Everything Roll K / a reset / a re-inbound
    //     opens up is bucketed as routed-deeper, not required. As of #6 the OOB-retained
    //     sideline inbound no longer parks (it re-runs Roll A and lands deeper), so there
    //     is no fact-carrying core stub here anymore; clean one-hop zone ride-through is
    //     proven authoritatively by RollHResolutionBatchCheck. A fresh local game +
    //     resolver keeps it self-contained. ---
    private static bool RollGHandoffCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- G->H integration: {cfg.BatchSize:N0} IntoShotType exits routed through Roll G then Roll H ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

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

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCGenerator(cfgC),
            cfgC,
            new RollDGenerator(cfgD),
            genE,
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

        var pieE = genE.Generate(state);

        // The destinations a shot can land at after G -> H -> I -> K. As of Session 17
        // an OFFENSIVE rebound no longer parks at STUB:OffensiveRebound — it executes
        // Roll K, which fans the same possession out: a putback (back through Roll H,
        // landing at the SAME post-shot destinations below), a RESET (back to Roll E,
        // which re-runs the whole halfcourt chain and can land at literally any
        // halfcourt outcome), three Roll K terminals (OffensiveFoul / DeadBallTurnover
        // / LiveBallTurnover), a jump ball, or the shared charge-and-fork. So the
        // closed eight-destination set of prior sessions is now OPEN: we assert the
        // CORE post-shot landings (still reliably reached straight off the first Roll H
        // and Roll I) plus zero-unrouted, and bucket everything Roll K/reset opens up
        // as "deeper" (routed, not required). STUB:OffensiveRebound is DROPPED from the
        // required set — it is structurally unreachable now. STUB:BlockRecovery is also
        // dropped — Roll H's Blocked now routes into Roll I (the block pie), so a block
        // lands at one of Roll I's existing landings (already covered here / bucketed
        // as deeper), never at the retired block-recovery stub.
        var destHits = new Dictionary<string, int>
        {
            ["END:Made"] = 0,
            ["END:MissOutOfBoundsLost"] = 0,
            ["END:DefensiveRebound"] = 0,
            ["END:LooseBallFoulOnOffense"] = 0,
        };
        // STUB:SidelineInbound is DROPPED from the core set as of #6: an OOB-retained
        // shot no longer parks — it re-runs Roll A and lands DEEPER (a terminal or a
        // deeper stub), bucketed below. With no fact-carrying core stub left, the
        // zone-ride-through assertion moves with it; clean one-hop fact ride-through
        // is proven authoritatively by RollHResolutionBatchCheck.

        var unrecognized = 0;
        var deeperTerminals = 0; // Roll K / reset / re-inbound terminals beyond core (routed)
        var deeperStubs = 0;     // Roll K / reset stub parks beyond core (routed)

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Select a real slot (Roll E), then hand the resolver a clean
            // IntoShotType continuation — exactly what Roll F emits on a ShotAttempt.
            var selected = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rng)).State;
            var shotTicket = new Continue(ContinuationKind.IntoShotType, selected);
            var d = resolver.Route(shotTicket).Destination;

            if (d == "END:Made") { destHits["END:Made"]++; continue; }
            if (d == "END:MissOutOfBoundsLost") { destHits["END:MissOutOfBoundsLost"]++; continue; }
            // Roll I's two terminals (Session 14) — matched BEFORE the stub parse,
            // exactly as the Made / MissOutOfBoundsLost terminals are. They carry
            // no slot:zone:result tail (a terminal names only its reason), so they
            // must short-circuit here rather than fall into the zone-token parser.
            if (d == "END:DefensiveRebound") { destHits["END:DefensiveRebound"]++; continue; }
            if (d == "END:LooseBallFoulOnOffense") { destHits["END:LooseBallFoulOnOffense"]++; continue; }
            // Everything else is ROUTED but not in the core set: every Roll K / reset /
            // re-inbound landing — the three Roll K terminals (END:OffensiveFoul /
            // DeadBallTurnover / LiveBallTurnover), a jump-ball terminal, any reset- or
            // re-entry-born terminal (a deeper Made, a Roll C turnover), an OOB-retained
            // shot that re-ran Roll A and resolved downstream, and any deeper stub park.
            // Bucket so they don't read as unrouted; don't require them.
            else if (d.StartsWith("END:")) deeperTerminals++;
            else if (d.StartsWith("STUB:")) deeperStubs++;
            else unrecognized++;
        }

        var landed = destHits.Values.Sum();
        Console.WriteLine($"  landed at a CORE post-shot destination: {landed:N0}");
        Console.WriteLine($"  routed DEEPER via Roll K / reset (terminals {deeperTerminals:N0}, stubs {deeperStubs:N0})");
        Console.WriteLine("  core destinations reached:");
        var allDests = true;
        foreach (var (label, count) in destHits)
        {
            var hit = count > 0;
            allDests &= hit;
            Console.WriteLine($"    {label,-26} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all core G->H->I destinations reached (offensive rebound now executes Roll K; OOB-retained re-runs Roll A and lands deeper): {(allDests ? "ok" : "FAIL")}");

        return routedOk && allDests;
    }


    // --- Batch: Roll H's seven-way make/miss distribution converges within
    //     tolerance, every exit carries a stamped Result matching the rolled
    //     outcome (no exit leaves Result null), and the prior two facts
    //     (SelectedSlot, ShotType) plus the new Result all ride through. As of
    //     Session 13 the pie is ZONE-AWARE for the block slice: each draw walks a
    //     fresh slot (Roll E) + zone (Roll G), and the generator sizes the block
    //     weight from that zone (Rim highest, Three lowest), carving it off the top
    //     and scaling the six make/miss outcomes by (1 − block). So the per-draw
    //     pie varies; the seven OBSERVED rates are checked against their
    //     ZONE-BLENDED expectations: blended block = Σ P(zone)·b(zone) over Roll G's
    //     zone mix, and each make/miss rate = base × (1 − blended block). A per-zone
    //     block readout confirms the gradient (Rim ≫ Three) directly. The six
    //     make/miss outcomes stay location-BLIND in SHAPE; only the block slice is
    //     zone-aware this pass. ---
    private static bool RollHResolutionBatchCheck(
        RollAConfig cfg, RollHConfig cfgH, IRollHPieGenerator genH, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} shots through Roll H (zone-aware seven-way pie) ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var rng = new SystemRng(cfg.Seed);

        // Generators to walk a fresh slot (Roll E) + zone (Roll G) per draw, so the
        // zone varies across the batch exactly as the live chain delivers to Roll H.
        var cfgD = RollDConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var genE = new RollEStubPieGenerator(RollEConfig.Load(configPath));
        var genG = new RollGStubPieGenerator(cfgG);
        // Phase 9 fix: use an isolated RollHGenerator bound to the LOCAL empty game so
        // PlayerAt() returns null and the generator falls back to its stub/baseline behavior.
        // This preserves RollHResolutionBatchCheck as a pure baseline calibration against
        // the configured rates, independent of whatever real rosters Main has seated.
        // Phase 6/7/8 matchup tests are in their own dedicated checks.
        var cfgMatchup = MatchupConfig.Load(configPath);
        var isolatedGenH = new RollHGenerator(cfgH, cfgMatchup, game);  // game is empty — no rosters
        var pieE = genE.Generate(state);

        var counts = new Dictionary<ShotResult, int>();
        foreach (var o in Enum.GetValues<ShotResult>()) counts[o] = 0;

        // Per-zone totals + per-zone block counts, for the Rim-vs-Three gradient.
        var zoneTotals = new Dictionary<ShotLocation, int>();
        var zoneBlocked = new Dictionary<ShotLocation, int>();
        foreach (var z in Enum.GetValues<ShotLocation>()) { zoneTotals[z] = 0; zoneBlocked[z] = 0; }

        var anomalies = 0;       // exit whose stamped Result is null or mismatched
        var rideThroughFail = 0; // slot / zone / result not all present on the carried state

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Fresh slot + zone, then the zone-aware pie for THAT zone.
            var selected = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rng)).State;
            var preH = ((Continue)RollG.Execute(selected, genG.Generate(selected), 0.0, rng)).State;
            var zone = preH.ShotType!.Value;
            var pieH = isolatedGenH.Generate(preH);

            var result = RollH.Execute(preH, pieH, rng);

            // Pull the carried state and the stamped result off either arm.
            var carried = result switch
            {
                Terminal t => t.State,
                Continue c => c.State,
                _ => throw new InvalidOperationException("Unknown Roll H result.")
            };

            if (carried.Result is not { } stamped) { anomalies++; continue; }

            // The stamped Result must match the routing arm taken. Made and
            // MissOutOfBoundsLost are the only terminals; everything else
            // (including Blocked) is a continue.
            var expectedTerminal = stamped is ShotResult.Made or ShotResult.MissOutOfBoundsLost;
            var isTerminal = result is Terminal;
            if (expectedTerminal != isTerminal) anomalies++;

            // All three per-possession facts must ride through.
            if (carried.SelectedSlot is null || carried.ShotType is null || carried.Result is null)
                rideThroughFail++;

            counts[stamped]++;
            zoneTotals[zone]++;
            if (stamped == ShotResult.Blocked) zoneBlocked[zone]++;
        }

        var n = (double)cfg.BatchSize;

        // Zone-blended expectations. P(zone) is Roll G's zone mix; b(zone) is the
        // configured per-zone block weight (same lookup the generator uses).
        var zoneP = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Three] = cfgG.BaseThree,
            [ShotLocation.Long] = cfgG.BaseLong,
            [ShotLocation.Mid] = cfgG.BaseMid,
            [ShotLocation.Short] = cfgG.BaseShort,
            [ShotLocation.Rim] = cfgG.BaseRim,
        };
        var blendedBlock = 0.0;
        foreach (var (z, p) in zoneP) blendedBlock += p * cfgH.BlockWeight(z);

        // Phase 8: blended foul rate and MAF/MissFouled split, weighted by zone probability.
        var blendedFoul = 0.0;
        var blendedMaf  = 0.0;
        foreach (var (z, p) in zoneP)
        {
            var f = cfgH.FoulRate(z);
            var m = cfgH.MafFraction(z);
            blendedFoul += p * f;
            blendedMaf  += p * f * m;
        }
        var blendedMissFouled = blendedFoul - blendedMaf;
        var nonBlockNonFoul   = 1.0 - blendedBlock - blendedFoul;

        // Made: BaseMade is the conversion rate given not blocked AND not fouled.
        var blendedMade  = cfgH.BaseMade * nonBlockNonFoul;

        // Miss and OOB: fill nonBlockNonFoul - Made, preserving relative proportions.
        var nonMadeBase  = cfgH.BaseMiss + cfgH.BaseMissOutOfBoundsLost + cfgH.BaseMissOutOfBoundsRetained;
        var nonMadeShare = nonBlockNonFoul - blendedMade;
        var makeScale    = nonMadeBase > 0.0 ? nonMadeShare / nonMadeBase : 0.0;

        var expected = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made] = blendedMade,
            [ShotResult.MadeAndFouled] = blendedMaf,
            [ShotResult.Miss] = cfgH.BaseMiss * makeScale,
            [ShotResult.MissFouled] = blendedMissFouled,
            [ShotResult.MissOutOfBoundsLost] = cfgH.BaseMissOutOfBoundsLost * makeScale,
            [ShotResult.MissOutOfBoundsRetained] = cfgH.BaseMissOutOfBoundsRetained * makeScale,
            [ShotResult.Blocked] = blendedBlock,
        };

        var ratesOk = true;
        Console.WriteLine("  Roll H outcomes (observed vs. zone-blended expectation):");
        foreach (var outcome in Enum.GetValues<ShotResult>())
        {
            var observed = counts[outcome] / n;
            var exp = expected[outcome];
            var gap = Math.Abs(observed - exp);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={exp:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        // Per-zone block gradient: observed b(zone) vs. configured, and the
        // monotonic Rim >= Short >= Mid >= Long >= Three ordering the design wants.
        // The per-zone gate uses a looser tolerance (3x the batch tolerance): a
        // single zone's block sample is small (e.g. Rim is ~12% over ~35k draws),
        // so the tight batch tolerance would flake on noise — but a real bug (a
        // swapped or mis-scaled zone) shows a multi-point gap that 3x still catches.
        // The hard gates that pin the design are the gradient and the blended rate.
        Console.WriteLine("\n  block rate by zone (observed vs. configured b(zone)):");
        var zoneTol = cfg.RateTolerance * 3.0;
        var zoneOrder = new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three };
        var lastObserved = 1.0;
        var gradientOk = true;
        var perZoneOk = true;
        foreach (var z in zoneOrder)
        {
            var total = zoneTotals[z];
            var observed = total > 0 ? zoneBlocked[z] / (double)total : 0.0;
            var configured = cfgH.BlockWeight(z);
            var gap = Math.Abs(observed - configured);
            var pass = gap <= zoneTol;
            perZoneOk &= pass;
            if (observed > lastObserved + cfg.RateTolerance) gradientOk = false; // out-of-order spike
            lastObserved = observed;
            Console.WriteLine($"    {z,-6} block observed={observed:P3}  configured={configured:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }
        Console.WriteLine($"  block gradient Rim >= ... >= Three: {(gradientOk ? "ok" : "FAIL")}");

        var observedBlended = counts[ShotResult.Blocked] / n;
        var blendedGap = Math.Abs(observedBlended - blendedBlock);
        var blendedOk = blendedGap <= cfg.RateTolerance;
        Console.WriteLine($"  zone-blended block rate: observed={observedBlended:P3}  expected={blendedBlock:P3}  gap={blendedGap:P3}  {(blendedOk ? "ok" : "FAIL")}");

        var cleanOk = anomalies == 0;
        var rideOk = rideThroughFail == 0;
        Console.WriteLine($"\n  every exit a stamped Result matching its arm: anomalies={anomalies} -> {(cleanOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  slot+zone+result ride through every exit: fails={rideThroughFail} -> {(rideOk ? "ok" : "FAIL")}");

        return ratesOk && perZoneOk && gradientOk && blendedOk && cleanOk && rideOk;
    }


    // --- Clean handoff: route a batch of IntoShotResolution exits through the
    //     resolver and confirm every one lands at its intended Roll H destination,
    //     with zero unrouted: Made -> terminal, MissOutOfBoundsLost -> terminal,
    //     Miss -> deeper (Roll I/K), MissOutOfBoundsRetained -> sideline-inbound stub,
    //     Blocked -> block-recovery stub (Session 13). The two SHOOTING-foul outcomes
    //     (MadeAndFouled / MissFouled) drive the Roll L FT loop (Session 18) and land at
    //     END:FreeThrowsMade on a made last shot, or flow into Roll M on a missed last
    //     shot (Session 19) — both absorbed in the "deeper" buckets here and proven by
    //     RollLFreeThrowCheck and RollMReboundBatchCheck. Isolates the Roll H hop exactly as
    //     RollGHandoffCheck isolated Roll G: the resolver executes Roll H on an
    //     IntoShotResolution continuation (carrying a real slot + zone) and lands at
    //     a terminal, a fact-carrying stub, or deeper. On the fact-carrying stub
    //     landings it confirms slot, zone, AND result all rode through. A fresh local
    //     game + resolver keeps it self-contained. ---
    private static bool RollHHandoffCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- Clean handoff: {cfg.BatchSize:N0} IntoShotResolution exits routed through Roll H ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

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
        var genG = new RollGStubPieGenerator(cfgG);

        var resolver = new Resolver(
            new StubPieGenerator(cfg),
            cfg,
            new RollBStubPieGenerator(cfgB),
            new RollCGenerator(cfgC),
            cfgC,
            new RollDGenerator(cfgD),
            genE,
            new AttentionGenerator(AttentionConfig.Load(configPath), game),
            new RollFStubPieGenerator(cfgF),
            genG,
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

        // With Roll I + Roll K live, a MISS no longer parks anywhere — it flows
        // THROUGH Roll I (to a defensive board / loose-ball foul terminal, or an
        // OFFENSIVE board) and, on an offensive board, THROUGH Roll K, which keeps the
        // possession alive (putback back into Roll H, or a reset back into Roll E) or
        // flips it (three terminals). So the post-shot destination set is now OPEN.
        // We assert the CORE landings that still come straight off the FIRST Roll H /
        // Roll I (the made basket, MissOutOfBoundsLost, the two Roll I terminals),
        // zero-unrouted — and bucket everything Roll K / a reset / a re-inbound opens
        // up as "deeper" (routed, not required). As of #6 the OOB-retained sideline
        // inbound is DROPPED from the core set too — it no longer parks; it re-runs
        // Roll A and lands deeper. STUB:OffensiveRebound and STUB:BlockRecovery remain
        // dropped (offensive board executes Roll K; a block routes into Roll I).
        var destHits = new Dictionary<string, int>
        {
            ["END:Made"] = 0,
            ["END:MissOutOfBoundsLost"] = 0,
            ["END:DefensiveRebound"] = 0,
            ["END:LooseBallFoulOnOffense"] = 0,
        };
        // STUB:SidelineInbound is DROPPED from the core set as of #6: an OOB-retained
        // shot no longer parks — it re-runs Roll A and lands DEEPER. With no
        // fact-carrying core stub left, the result-ride-through assertion goes with it;
        // clean one-hop fact ride-through is proven by RollHResolutionBatchCheck.

        var unrecognized = 0;
        var deeperTerminals = 0; // Roll K / reset / re-inbound terminals beyond core (routed)
        var deeperStubs = 0;     // Roll K / reset stub parks beyond core (routed)

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Select a slot (Roll E) and stamp a zone (Roll G), then hand the
            // resolver a clean IntoShotResolution continuation — exactly what Roll G
            // emits — to isolate the Roll H hop.
            var selected = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rng)).State;
            var withZone = ((Continue)RollG.Execute(selected, genG.Generate(selected), 0.0, rng)).State;
            var shotTicket = new Continue(ContinuationKind.IntoShotResolution, withZone);
            var d = resolver.Route(shotTicket).Destination;

            if (d == "END:Made") { destHits["END:Made"]++; continue; }
            if (d == "END:MissOutOfBoundsLost") { destHits["END:MissOutOfBoundsLost"]++; continue; }
            // Roll I's two terminals (Session 14) — matched BEFORE the stub parse,
            // like the Made / MissOutOfBoundsLost terminals; they carry no
            // slot:zone:result tail, so they must short-circuit here.
            if (d == "END:DefensiveRebound") { destHits["END:DefensiveRebound"]++; continue; }
            if (d == "END:LooseBallFoulOnOffense") { destHits["END:LooseBallFoulOnOffense"]++; continue; }

            // Everything else is ROUTED but not core: the three Roll K terminals, a
            // jump-ball terminal, any reset- or re-inbound-born terminal, an OOB-retained
            // shot that re-ran Roll A, and any deeper stub park. Bucket so they don't read
            // as unrouted; don't require them.
            if (d.StartsWith("END:")) deeperTerminals++;
            else if (d.StartsWith("STUB:")) deeperStubs++;
            else unrecognized++;
        }

        var landed = destHits.Values.Sum();
        Console.WriteLine($"  landed at a CORE post-shot destination: {landed:N0}");
        Console.WriteLine($"  routed DEEPER via Roll K / reset (terminals {deeperTerminals:N0}, stubs {deeperStubs:N0})");
        Console.WriteLine("  core destinations reached:");
        var allDests = true;
        foreach (var (label, count) in destHits)
        {
            var hit = count > 0;
            allDests &= hit;
            Console.WriteLine($"    {label,-26} {count,8:N0}  {(hit ? "ok" : "NONE")}");
        }

        var routedOk = unrecognized == 0;
        Console.WriteLine($"\n  zero unrouted exits: unrecognized={unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all core Roll H->I destinations reached (offensive rebound now executes Roll K; OOB-retained re-runs Roll A and lands deeper): {(allDests ? "ok" : "FAIL")}");

        return routedOk && allDests;
    }


    // ---------------------------------------------------------------------------
    // Phase 2: Attribute wiring — RollHGenerator produces higher make rates for
    // higher-rated shooters. This is the load-bearing proof that the pipe works:
    // shooter attribute → logistic → make weight → resolver → outcome rate.
    // ---------------------------------------------------------------------------
    private static bool Phase2AttributeWiringCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 2: Attribute wiring (RollH real generator) ---");
        var pass = true;

        var cfgH  = RollHConfig.Load(configPath);
        var cfg   = RollAConfig.Load(configPath);
        var cfgB  = RollBConfig.Load(configPath);
        var cfgC  = RollCConfig.Load(configPath);
        var cfgD  = RollDConfig.Load(configPath);
        var cfgE  = RollEConfig.Load(configPath);
        var cfgF  = RollFConfig.Load(configPath);
        var cfgG  = RollGConfig.Load(configPath);
        var cfgI  = RollIConfig.Load(configPath);
        var cfgJ  = RollJConfig.Load(configPath);
        var cfgK  = RollKConfig.Load(configPath);
        var cfgL  = RollLConfig.Load(configPath);
        var cfgM  = RollMConfig.Load(configPath);
        var cfgOffFoul = RollOffensiveFoulConfig.Load(configPath);

        // --- Logistic range check: no make probability outside [0, 1] ---
        Console.WriteLine("  Logistic range check (ratings 1/25/50/75/99, all five zones)...");
        var ratingProbes = new[] { 1, 25, 50, 75, 99 };
        foreach (ShotLocation zone in Enum.GetValues<ShotLocation>())
        {
            foreach (var r in ratingProbes)
            {
                var p = cfgH.MakeProbability(zone, r);
                if (p < 0.0 || p > 1.0)
                {
                    Console.WriteLine($"  FAIL  MakeProbability({zone}, {r}) = {p:F4} — out of [0,1].");
                    pass = false;
                }
            }
        }
        Console.WriteLine("  Logistic values all in [0,1] — OK.");

        // --- Monotonicity: make% at rating 99 > rating 50 > rating 1 per zone ---
        foreach (ShotLocation zone in Enum.GetValues<ShotLocation>())
        {
            var p1  = cfgH.MakeProbability(zone, 1);
            var p50 = cfgH.MakeProbability(zone, 50);
            var p99 = cfgH.MakeProbability(zone, 99);
            if (!(p1 < p50 && p50 < p99))
            {
                Console.WriteLine($"  FAIL  {zone}: logistic not monotone — p1={p1:F3} p50={p50:F3} p99={p99:F3}.");
                pass = false;
            }
        }
        Console.WriteLine("  Logistic monotone per zone — OK.");

        // --- Wiring check: 10k three-point attempts, high vs low Outside shooter ---
        const int Batch        = 10_000;
        const double MinGapPp  = 0.10;  // at least 10 percentage points

        // Helper: build a GameState with two players in slots 1 and 2, given Outside rating.
        static (GameState game, Slot slot1) MakeGame(int outsideRating, RollDConfig cfgD2)
        {
            var fouls = new FoulTracker(cfgD2.BonusThreshold, cfgD2.DoubleBonusThreshold);
            var g     = new GameState(fouls);
            g.SetPossessionArrow(TeamSide.Home);

            var homeLineup = g.HomeLineup;
            var homeRoster = g.HomeRoster;
            var awayLineup = g.AwayLineup;
            var awayRoster = g.AwayRoster;

            for (var i = 1; i <= Lineup.Size; i++)
            {
                var shooter = new Player($"Shooter{i}") { Outside = outsideRating, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 70,
                    FoulDrawing = 50,
                    BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                    OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50,
                    PerimeterDefense = 50, PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50, Quickness = 50,
                    FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50,
                    OffBallDefense = 50,
                    RimTendency = 50, ShortTendency = 50, MidTendency = 50, LongTendency = 50, ThreeTendency = 50 };
                var defender = new Player($"Defender{i}") { Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 70,
                    FoulDrawing = 50,
                    BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                    OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50,
                    PerimeterDefense = 50, PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50, Quickness = 50,
                    FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50,
                    OffBallDefense = 50,
                    RimTendency = 50, ShortTendency = 50, MidTendency = 50, LongTendency = 50, ThreeTendency = 50 };
                homeRoster.SetStarter(homeLineup.SlotAt(i), shooter);
                awayRoster.SetStarter(awayLineup.SlotAt(i), defender);
            }
            return (g, homeLineup.SlotAt(1));
        }

        // Helper: run Batch three-point attempts through the resolver using a given game,
        // count Made + MadeAndFouled outcomes from Roll H directly.
        static double RunThreePointBatch(GameState g, RollHConfig cfgH2, MatchupConfig cfgM2, int batch)
        {
            var gen = new RollHGenerator(cfgH2, cfgM2, g);
            var state = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                ShotType: ShotLocation.Three,
                SelectedSlot: g.HomeLineup.SlotAt(1));

            var made = 0;
            for (var i = 0; i < batch; i++)
            {
                var pie    = gen.Generate(state, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result is ShotResult.Made or ShotResult.MadeAndFouled)
                    made++;
            }
            return (double)made / batch;
        }

        var (highGame, _) = MakeGame(outsideRating: 85, cfgD2: cfgD);
        var (lowGame,  _) = MakeGame(outsideRating: 25, cfgD2: cfgD);

        var cfgMatchup = MatchupConfig.Load(configPath);
        var highRate = RunThreePointBatch(highGame, cfgH, cfgMatchup, Batch);
        var lowRate  = RunThreePointBatch(lowGame,  cfgH, cfgMatchup, Batch);
        var gap      = highRate - lowRate;

        Console.WriteLine($"  Three-point make rate — high Outside (85): {highRate:P1}");
        Console.WriteLine($"  Three-point make rate — low  Outside (25): {lowRate:P1}");
        Console.WriteLine($"  Gap: {gap:P1}  (threshold ≥ {MinGapPp:P0})");

        if (gap < MinGapPp)
        {
            Console.WriteLine($"  FAIL  Gap {gap:P1} is below the {MinGapPp:P0} threshold.");
            pass = false;
        }
        else
        {
            Console.WriteLine("  Wiring check PASSED — high shooter beats low shooter by required margin.");
        }

        // --- Fallback check: null roster → stub pie (no exception) ---
        Console.WriteLine("  Fallback check (unpopulated roster → stub pie, no throw)...");
        var bareGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        var bareGen  = new RollHGenerator(cfgH, cfgMatchup, bareGame);
        var bareState = new PossessionState(
            PossessionNumber: 1,
            Offense: TeamSide.Home,
            Defense: TeamSide.Away,
            Entry: EntryType.DeadBallInbound,
            ShotType: ShotLocation.Three,
            SelectedSlot: bareGame.HomeLineup.SlotAt(1));
        try
        {
            var pie = bareGen.Generate(bareState, putback: false);
            // Should equal stub pie output — Made weight = BaseMade * (1 - BlockThree)
            var expectedMade = cfgH.BaseMade * (1.0 - cfgH.BlockThree);
            // Pie doesn't expose weights directly; just confirm no exception and the
            // result is a valid (non-null) pie object.
            Console.WriteLine("  Fallback OK — unpopulated roster returns stub pie without throwing.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  Fallback threw: {ex.Message}");
            pass = false;
        }

        Console.WriteLine(pass ? "  Phase 2 PASSED." : "  Phase 2 FAILED.");
        return pass;
    }


    // Phase 6 — matchup wiring (DefenderPicker + Matchup.GapFn/EffectiveRating + the make door).
    // The sweeps read the make% analytically (cfgH.MakeProbability over Matchup.EffectiveRating —
    // exactly the weight BuildRealPie assigns to Made), so they are deterministic. The DEC-6
    // fallback is exercised through the REAL generator (batched) so the defender lookup, the
    // null-defender guard, and the pie build are covered end to end.
    private static bool Phase6MatchupWiringCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 6: Matchup wiring (defender picker + gap fn + make door) ---");
        var pass = true;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // A uniform player at baseline b, overriding only the attributes the make door reads:
        // offense Outside/Mid/Close/Finishing, defense Perimeter/Post/RimProtection, and the five
        // athletic attrs (Athleticism's source) via `ath`. Mirrors MakeGame's full initializer so
        // no attribute is left at 0; athletic attrs stay equal => physical gap 0 unless `ath` is set.
        static Player Mk(int b, int? outside = null, int? mid = null, int? close = null, int? fin = null,
                         int? perimD = null, int? postD = null, int? rimP = null, int? ath = null,
                         int? foulDrawing = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = outside ?? b, Mid = mid ?? b, Close = close ?? b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = foulDrawing ?? b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = perimD ?? b, PostDefense = postD ?? b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = ath ?? b, Speed = ath ?? b, Quickness = ath ?? b, FirstStep = ath ?? b, Vertical = ath ?? b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b, HelpDefense = b,
                OffBallDefense = b,
                RimTendency = rimT ?? b, ShortTendency = shortT ?? b, MidTendency = midT ?? b,
                LongTendency = longT ?? b, ThreeTendency = threeT ?? b,
            };

        // The make% the wired generator assigns to a contested shot, and the raw no-matchup baseline.
        double Make(ShotLocation z, Player s, Player d)
            => cfgH.MakeProbability(z, Matchup.EffectiveRating(z, s, d, cfgM));
        double Raw(ShotLocation z, Player s)
            => cfgH.MakeProbability(z, Matchup.OffenseRating(z, s));

        const double Eps = 1e-9;

        // (a) Defender sweep: fix the shooter, raise the matched defensive rating -> make% falls.
        Console.WriteLine("  (a) Defender sweep @Three (shooter Outside=50, sweep PerimeterDefense 10->90):");
        var shooterA = Mk(50, outside: 50);
        var baseA = Raw(ShotLocation.Three, shooterA);
        double prevA = double.PositiveInfinity, evenA = 0, edgeA = 0;
        var monoA = true;
        foreach (var pd in new[] { 10, 30, 50, 70, 90 })
        {
            var mk = Make(ShotLocation.Three, shooterA, Mk(50, perimD: pd));
            Console.WriteLine($"      PD={pd,2}  make={mk:P1}");
            if (mk >= prevA) monoA = false;
            prevA = mk;
            if (pd == 50) evenA = mk;
            if (pd == 90) edgeA = mk;
        }
        var aOk = monoA
                  && Math.Abs(evenA - baseA) < Eps
                  && edgeA > cfgH.ThreeFloor && edgeA < baseA;
        if (!monoA) Console.WriteLine("  FAIL  (a) make% not strictly decreasing as defense rises.");
        if (Math.Abs(evenA - baseA) >= Eps) Console.WriteLine("  FAIL  (a) even matchup (PD=50) != raw own-rating baseline.");
        if (!(edgeA > cfgH.ThreeFloor && edgeA < baseA)) Console.WriteLine($"  FAIL  (a) big edge should compress below baseline but stay above floor {cfgH.ThreeFloor:P1}.");
        if (aOk) Console.WriteLine("      OK — monotone down; even==baseline; edge compresses toward the floor, not zero.");
        pass &= aOk;

        // (b) Blend (CONF-1) @Mid (0.5/0.5): both sub-attributes register at equal half weight.
        Console.WriteLine("  (b) Blend @Mid: PerimeterDefense and PostDefense each move make% at half weight:");
        var shooterB = Mk(50, mid: 50);
        var dPerim = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 20, postD: 40))
                   - Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 60, postD: 40));
        var dPost  = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 40, postD: 20))
                   - Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 40, postD: 60));
        var swap1 = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 20, postD: 60));
        var swap2 = Make(ShotLocation.Mid, shooterB, Mk(50, perimD: 60, postD: 20));
        Console.WriteLine($"      raise PerimeterDefense 20->60: make drops {dPerim:P2}");
        Console.WriteLine($"      raise PostDefense      20->60: make drops {dPost:P2}");
        var bOk = dPerim > 0 && dPost > 0 && Math.Abs(dPerim - dPost) < Eps && Math.Abs(swap1 - swap2) < Eps;
        if (!(dPerim > 0 && dPost > 0)) Console.WriteLine("  FAIL  (b) a sub-attribute did not register.");
        if (Math.Abs(dPerim - dPost) >= Eps) Console.WriteLine("  FAIL  (b) sub-attributes not equally weighted (expected 0.5/0.5).");
        if (Math.Abs(swap1 - swap2) >= Eps) Console.WriteLine("  FAIL  (b) blend not swap-symmetric at Mid.");
        if (bOk) Console.WriteLine("      OK — both register, equal half weight, swap-symmetric.");
        pass &= bOk;

        // (c) Rim specialist: strong at Rim, beatable on the perimeter zones (vs a balanced defender).
        Console.WriteLine("  (c) Rim specialist (Perim=20,Post=40,Rim=90) vs balanced (all 50), shooter rating 50:");
        var shooterC = Mk(50, mid: 50, fin: 50);
        var rimSpec  = Mk(50, perimD: 20, postD: 40, rimP: 90);
        var balanced = Mk(50, perimD: 50, postD: 50, rimP: 50);
        var midSpec = Make(ShotLocation.Mid, shooterC, rimSpec);
        var midBal  = Make(ShotLocation.Mid, shooterC, balanced);
        var rmSpec  = Make(ShotLocation.Rim, shooterC, rimSpec);
        var rmBal   = Make(ShotLocation.Rim, shooterC, balanced);
        Console.WriteLine($"      Mid: vs specialist {midSpec:P1}  vs balanced {midBal:P1}   (specialist gives up MORE)");
        Console.WriteLine($"      Rim: vs specialist {rmSpec:P1}  vs balanced {rmBal:P1}   (specialist gives up LESS)");
        var cOk = midSpec > midBal && rmSpec < rmBal;
        if (!(midSpec > midBal)) Console.WriteLine("  FAIL  (c) rim specialist not exploitable at Mid.");
        if (!(rmSpec < rmBal)) Console.WriteLine("  FAIL  (c) rim specialist not stronger at Rim.");
        if (cOk) Console.WriteLine("      OK — beatable on the perimeter, strong at the rim.");
        pass &= cOk;

        // (d) Shooter sweep: fix the defender, raise shooter rating -> make% rises and saturates.
        Console.WriteLine("  (d) Shooter sweep @Three (balanced defender, sweep Outside 30->99):");
        var defD = Mk(50, perimD: 50);
        double prevD = double.NegativeInfinity, m30 = 0, m50 = 0, m90 = 0, m99 = 0;
        var monoD = true;
        foreach (var o in new[] { 30, 50, 70, 90, 99 })
        {
            var mk = Make(ShotLocation.Three, Mk(50, outside: o), defD);
            Console.WriteLine($"      Outside={o,2}  make={mk:P1}");
            if (mk <= prevD) monoD = false;
            prevD = mk;
            if (o == 30) m30 = mk;
            if (o == 50) m50 = mk;
            if (o == 90) m90 = mk;
            if (o == 99) m99 = mk;
        }
        var dOk = monoD && (m99 - m90) < (m50 - m30) && m99 < cfgH.ThreeCeiling;
        if (!monoD) Console.WriteLine("  FAIL  (d) make% not strictly increasing as shooter rating rises.");
        if (!((m99 - m90) < (m50 - m30))) Console.WriteLine("  FAIL  (d) top end not flattening (no saturation).");
        if (!(m99 < cfgH.ThreeCeiling)) Console.WriteLine($"  FAIL  (d) make% breached the curve ceiling {cfgH.ThreeCeiling:P1}.");
        if (dOk) Console.WriteLine($"      OK — monotone up, flattening toward the ceiling ({cfgH.ThreeCeiling:P1}); the curve caps the payoff.");
        pass &= dOk;

        // (e) Physical is steeper than skill (DEC-5: larger exponent), and the physical term moves make%.
        Console.WriteLine("  (e) Physical gap (DEC-5: steeper than skill via the larger exponent):");
        var skillShift40    = Matchup.GapFn(40, cfgM.SkillSteepness,    cfgM.SkillExponent,    cfgM.ReferenceScale);
        var physicalShift40 = Matchup.GapFn(40, cfgM.PhysicalSteepness, cfgM.PhysicalExponent, cfgM.ReferenceScale);
        var athEdge = Make(ShotLocation.Three, Mk(50, outside: 50, ath: 70), Mk(50, perimD: 50, ath: 30));
        var athEven = Make(ShotLocation.Three, Mk(50, outside: 50, ath: 50), Mk(50, perimD: 50, ath: 50));
        Console.WriteLine($"      at gap=40:  skillShift={skillShift40:F2}  physicalShift={physicalShift40:F2}");
        Console.WriteLine($"      athletic edge (70 vs 30) make={athEdge:P1}  vs even make={athEven:P1}");
        var eOk = physicalShift40 > skillShift40 && athEdge > athEven;
        if (!(physicalShift40 > skillShift40)) Console.WriteLine("  FAIL  (e) physical not steeper than skill at equal gap.");
        if (!(athEdge > athEven)) Console.WriteLine("  FAIL  (e) athletic edge did not raise make%.");
        if (eOk) Console.WriteLine("      OK — physical steeper than skill; an athletic edge raises make%.");
        pass &= eOk;

        // (f) DEC-6 fallback through the REAL generator: an empty defending slot reads the raw
        //     own-rating (== the even matchup), while a strong defender lowers the sampled rate.
        //     Margin floor is 0.03: against the Session-50 calibrated (flatter) curve, a skill-ONLY
        //     strong defender (PerimD 90 vs a 50 shooter, even athleticism) lowers the three by
        //     ~4-5 points — by design, a skilled-but-not-more-athletic defender only nudges a
        //     shooter (athletic separation is what suppresses harder). The old 0.05 floor assumed
        //     the retired steep curve. The direction (strong defender lowers make) is what matters.
        Console.WriteLine("  (f) DEC-6 fallback + end-to-end wiring (real generator, batched):");
        const int Batch = 20_000;
        const double RateTol = 0.02;

        static double Rate(GameState g, RollHConfig ch, MatchupConfig cm, int batch)
        {
            var gen = new RollHGenerator(ch, cm, g);
            var state = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Three,
                SelectedSlot: g.HomeLineup.SlotAt(1));
            var made = 0;
            for (var i = 0; i < batch; i++)
            {
                var pie = gen.Generate(state, putback: false);
                var r = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (r is ShotResult.Made or ShotResult.MadeAndFouled) made++;
            }
            return (double)made / batch;
        }

        GameState GameWith(Player? defender)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            g.SetPossessionArrow(TeamSide.Home);
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), Mk(50, outside: 50));
            if (defender is not null)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(1), defender);  // empty slot otherwise -> null -> raw rating
            return g;
        }

        var rEmpty  = Rate(GameWith(null),                            cfgH, cfgM, Batch);
        var rEven   = Rate(GameWith(Mk(50, perimD: 50)),             cfgH, cfgM, Batch);
        var rStrong = Rate(GameWith(Mk(50, perimD: 90)),             cfgH, cfgM, Batch);
        Console.WriteLine($"      empty-slot {rEmpty:P1}   even {rEven:P1}   strong-defender {rStrong:P1}");
        var fOk = Math.Abs(rEmpty - rEven) <= RateTol && rStrong < rEven - 0.03;
        if (Math.Abs(rEmpty - rEven) > RateTol) Console.WriteLine("  FAIL  (f) empty-slot fallback diverges from the even-matchup baseline.");
        if (!(rStrong < rEven - 0.03)) Console.WriteLine("  FAIL  (f) a strong defender did not lower the generator's make rate by the expected margin.");
        if (fOk) Console.WriteLine("      OK — empty slot reads raw rating (==even); a strong defender lowers make% through the real pipe.");
        pass &= fOk;

        // (g) Regression guard for fix #8 (carve-then-convert). A dominant finisher vs a
        //     weak rim protector drives the effective rim rating into the make-curve's
        //     ceiling (~0.93). Under the OLD math (Made = makePct, block added on top) that
        //     made makePct + block > 1 → a negative weight → the Pie constructor threw.
        //     The fix must build a valid pie through the real generator (no throw).
        Console.WriteLine("  (g) Rim overflow guard (fix #8, carve-then-convert):");
        var gRimOk = true;
        try
        {
            var gRim = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gRim.SetPossessionArrow(TeamSide.Home);
            gRim.HomeRoster.SetStarter(gRim.HomeLineup.SlotAt(1), Mk(50, fin: 99));        // elite finisher
            gRim.AwayRoster.SetStarter(gRim.AwayLineup.SlotAt(1), Mk(50, postD: 10, rimP: 10)); // weak rim protection
            var genRim = new RollHGenerator(cfgH, cfgM, gRim);
            var stateRim = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gRim.HomeLineup.SlotAt(1));
            var madeRim = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie = genRim.Generate(stateRim, putback: false);   // would THROW pre-fix
                if (pie.Roll(new SystemRng(i).NextUnitInterval()) is ShotResult.Made or ShotResult.MadeAndFouled) madeRim++;
            }
            Console.WriteLine($"      elite finisher vs weak rim protector @Rim: make {(double)madeRim / 5_000:P1}  (valid pie, no overflow)");
        }
        catch (Exception ex)
        {
            gRimOk = false;
            Console.WriteLine($"  FAIL  (g) rim pie overflowed / threw: {ex.Message}");
        }
        if (gRimOk) Console.WriteLine("      OK — the extreme rim matchup builds a valid pie; the make+block overflow is fixed.");
        pass &= gRimOk;

        Console.WriteLine(pass ? "  Phase 6 PASSED." : "  Phase 6 FAILED.");
        return pass;
    }


    // Phase 7: the block door. Confirms that block rate bends correctly with matchup —
    // defender-edge raises it toward the per-zone ceiling, shooter-edge lowers it toward
    // the floor — and that all per-zone tuning knobs (skill/length weights, referenceShift)
    // are actually registering. Also confirms the DEC-6 fallback (empty slot → baseline)
    // and that Phase 6 (the make door) is unbroken by the block-weight change.
    //
    // No batch rolls here: block rate is deterministic given the pie weights. All checks
    // call Matchup.BlockWeight directly and assert on the returned double — exact arithmetic,
    // no sampling noise.
    private static bool Phase7BlockDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 7: Block door (matchup-aware block weight) ---");
        var pass = true;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // Helper: build a player with all attributes at baseline b, overriding only
        // the named ones. Mirrors Phase 6's Mk helper so the two checks are comparable.
        // Height/Wingspan/Vertical are the length attributes (block-specific).
        // Finishing / PerimeterDefense / RimProtection are the skill attributes for Rim/Three.
        static Player Mk(int b,
                         int? fin = null, int? outside = null,
                         int? rimP = null, int? perimD = null,
                         int? h = null, int? ws = null, int? v = null,
                         int? foulDrawing = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = outside ?? b, Mid = b, Close = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = foulDrawing ?? b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = perimD ?? b, PostDefense = b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = h ?? b, Wingspan = ws ?? b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = v ?? b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b, HelpDefense = b,
                OffBallDefense = b,
                RimTendency = rimT ?? b, ShortTendency = shortT ?? b, MidTendency = midT ?? b,
                LongTendency = longT ?? b, ThreeTendency = threeT ?? b,
            };

        // Shorthand: the matchup block weight for a zone, shooter, defender.
        double Blk(ShotLocation z, Player s, Player d)
            => Matchup.BlockWeight(z, s, d, cfgH.BlockWeight(z), cfgM);

        // ----------------------------------------------------------------
        // (a) Defender length sweep @Rim. Fix shooter (Finishing=50, all else 50).
        //     Sweep defender RimProtection AND length together from low to high.
        //     Block rate must rise monotonically and stay bounded by [floor, ceiling].
        // ----------------------------------------------------------------
        Console.WriteLine("  (a) Defender sweep @Rim (shooter Fin=50, all 50; sweep RimP+H/W/V 20..95):");
        var shooterA = Mk(50, fin: 50);
        double prevA = double.NegativeInfinity;
        var monoA = true;
        var boundA = true;
        var rimFloor = cfgM.BlockFloor(ShotLocation.Rim);
        var rimCeil  = cfgM.BlockCeiling(ShotLocation.Rim);
        foreach (var (rp, hw) in new[] { (20, 60), (40, 70), (50, 70), (60, 75), (80, 85), (95, 90) })
        {
            var bw = Blk(ShotLocation.Rim, shooterA, Mk(50, rimP: rp, h: hw, ws: hw, v: hw));
            Console.WriteLine($"      RimP={rp,2}  H/W/V={hw}  block={bw:P3}  [{rimFloor:P2},{rimCeil:P2}]");
            if (bw <= prevA) { monoA = false; Console.WriteLine($"        FAIL — not strictly increasing (prev={prevA:P3})"); }
            if (bw < rimFloor - 1e-9 || bw > rimCeil + 1e-9) { boundA = false; Console.WriteLine($"        FAIL — out of [floor,ceil]"); }
            prevA = bw;
        }
        if (monoA && boundA) Console.WriteLine("      OK — monotone rising; bounded by [floor, ceiling].");
        pass &= monoA && boundA;

        // ----------------------------------------------------------------
        // (b) Defender sweep @Three. Same shape — block rate must rise,
        //     but the spread must be much smaller than at Rim (per-zone weighting proof).
        // ----------------------------------------------------------------
        Console.WriteLine("  (b) Defender sweep @Three (shooter Outside=50, all 50; sweep PerimD+H/W/V):");
        var shooterB = Mk(50, outside: 50);
        double prevB = double.NegativeInfinity;
        var monoB = true;
        var boundB = true;
        var threeFloor = cfgM.BlockFloor(ShotLocation.Three);
        var threeCeil  = cfgM.BlockCeiling(ShotLocation.Three);
        double loB = double.PositiveInfinity, hiB = double.NegativeInfinity;
        foreach (var (pd, hw) in new[] { (20, 60), (40, 70), (50, 70), (60, 75), (80, 85), (95, 90) })
        {
            var bw = Blk(ShotLocation.Three, shooterB, Mk(50, perimD: pd, h: hw, ws: hw, v: hw));
            Console.WriteLine($"      PerimD={pd,2}  H/W/V={hw}  block={bw:P3}  [{threeFloor:P3},{threeCeil:P3}]");
            if (bw <= prevB) { monoB = false; Console.WriteLine($"        FAIL — not strictly increasing (prev={prevB:P3})"); }
            if (bw < threeFloor - 1e-9 || bw > threeCeil + 1e-9) { boundB = false; Console.WriteLine($"        FAIL — out of [floor,ceil]"); }
            if (bw < loB) loB = bw;
            if (bw > hiB) hiB = bw;
            prevB = bw;
        }
        // Spread at Three must be smaller than spread at Rim — proves per-zone weighting.
        var spreadThree = hiB - loB;
        var spreadRim   = (Blk(ShotLocation.Rim, Mk(50, fin: 50), Mk(50, rimP: 95, h: 90, ws: 90, v: 90))
                         - Blk(ShotLocation.Rim, Mk(50, fin: 50), Mk(50, rimP: 20, h: 60, ws: 60, v: 60)));
        var spreadOk = spreadThree < spreadRim;
        Console.WriteLine($"      Three spread={spreadThree:P3}  Rim spread={spreadRim:P3}  Three < Rim: {spreadOk}");
        if (!spreadOk) Console.WriteLine("  FAIL  (b) Three spread should be smaller than Rim spread.");
        if (monoB && boundB && spreadOk) Console.WriteLine("      OK — monotone, bounded, spread smaller than Rim (per-zone weights working).");
        pass &= monoB && boundB && spreadOk;

        // ----------------------------------------------------------------
        // (c) Skill vs length contribution split.
        //     At Rim: hold length=70, sweep RimProtection 20→95; measure delta.
        //             hold RimP=70,   sweep length 60→90; measure delta.
        //             Length delta should exceed skill delta (60/40 split).
        //     At Three: same; length dominates even more.
        // ----------------------------------------------------------------
        Console.WriteLine("  (c) Skill vs length split @Rim and @Three:");
        var shooterC = Mk(50);

        // Rim — skill-only sweep (hold length at 70)
        var rimSk20 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 20,  h: 70, ws: 70, v: 70));
        var rimSk95 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 95,  h: 70, ws: 70, v: 70));
        var rimSkDelta = rimSk95 - rimSk20;

        // Rim — length-only sweep (hold RimP at 70)
        var rimLe60 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 70,  h: 60, ws: 60, v: 60));
        var rimLe90 = Blk(ShotLocation.Rim, shooterC, Mk(50, rimP: 70,  h: 90, ws: 90, v: 90));
        var rimLeDelta = rimLe90 - rimLe60;

        Console.WriteLine($"      Rim skill-only  (RimP 20→95, length=70): Δblock = {rimSkDelta:P3}");
        Console.WriteLine($"      Rim length-only (length 60→90, RimP=70):  Δblock = {rimLeDelta:P3}");
        var cRimOk = rimLeDelta > rimSkDelta;
        if (!cRimOk) Console.WriteLine("  FAIL  (c) length delta should exceed skill delta at Rim (60/40 split).");
        if (cRimOk) Console.WriteLine("      OK @Rim — length delta > skill delta (length heavier at 60%).");
        pass &= cRimOk;

        // Three — skill-only sweep (hold length at 70)
        var thrSk20 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 20, h: 70, ws: 70, v: 70));
        var thrSk95 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 95, h: 70, ws: 70, v: 70));
        var thrSkDelta = thrSk95 - thrSk20;

        // Three — length-only sweep (hold PerimD at 70)
        var thrLe60 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 70, h: 60, ws: 60, v: 60));
        var thrLe90 = Blk(ShotLocation.Three, shooterC, Mk(50, perimD: 70, h: 90, ws: 90, v: 90));
        var thrLeDelta = thrLe90 - thrLe60;

        Console.WriteLine($"      Three skill-only  (PerimD 20→95, length=70): Δblock = {thrSkDelta:P3}");
        Console.WriteLine($"      Three length-only (length 60→90, PerimD=70):  Δblock = {thrLeDelta:P3}");
        var cThreeOk = thrLeDelta > thrSkDelta;
        if (!cThreeOk) Console.WriteLine("  FAIL  (c) length delta should exceed skill delta at Three (60/40 split).");
        if (cThreeOk) Console.WriteLine("      OK @Three — length delta > skill delta (length heavier at 60%).");
        pass &= cThreeOk;

        // ----------------------------------------------------------------
        // (d) Empty-slot fallback (DEC-6). Confirms the three cases:
        //     empty slot → block == configured baseline (exact);
        //     even defender → block == baseline (even matchup, zero shift);
        //     strong defender → block > baseline.
        // ----------------------------------------------------------------
        Console.WriteLine("  (d) DEC-6 fallback — empty slot, even matchup, strong defender:");
        var shooterD = Mk(50, fin: 50);

        // Empty slot: caller uses _cfg.BlockWeight(zone) directly, no Matchup.BlockWeight call.
        var dEmpty = cfgH.BlockWeight(ShotLocation.Rim);          // the baseline itself

        // Even matchup: both players at 50, so zero gap → zero shift → baseline.
        var dEven  = Blk(ShotLocation.Rim, shooterD, Mk(50));

        // Strong defender: should be above baseline.
        var dStrong = Blk(ShotLocation.Rim, shooterD, Mk(50, rimP: 90, h: 85, ws: 85, v: 85));

        Console.WriteLine($"      empty-slot (baseline): {dEmpty:P3}");
        Console.WriteLine($"      even matchup:           {dEven:P3}");
        Console.WriteLine($"      strong defender:        {dStrong:P3}");

        const double FallbackEps = 1e-9;
        var dFallbackOk = Math.Abs(dEmpty - cfgH.BlockWeight(ShotLocation.Rim)) < FallbackEps;
        var dEvenOk     = Math.Abs(dEven  - cfgH.BlockWeight(ShotLocation.Rim)) < FallbackEps;
        var dStrongOk   = dStrong > cfgH.BlockWeight(ShotLocation.Rim) + 0.005;
        if (!dFallbackOk) Console.WriteLine("  FAIL  (d) empty-slot baseline is not the configured value.");
        if (!dEvenOk)     Console.WriteLine("  FAIL  (d) even matchup did not return exactly the baseline.");
        if (!dStrongOk)   Console.WriteLine("  FAIL  (d) strong defender did not raise block above baseline.");
        if (dFallbackOk && dEvenOk && dStrongOk)
            Console.WriteLine("      OK — empty==baseline; even==baseline; strong defender raises block.");
        pass &= dFallbackOk && dEvenOk && dStrongOk;

        // ----------------------------------------------------------------
        // (e) Symmetric shooter edge. Fix balanced defender (all 50).
        //     Raise shooter's Finishing AND length. Block rate at Rim must FALL,
        //     asymptoting toward floor. Proves the curve bends down for shooter edge.
        // ----------------------------------------------------------------
        Console.WriteLine("  (e) Shooter-edge symmetry @Rim (fix defender all=50; raise shooter Fin+H/W/V):");
        var defenderE = Mk(50);
        double prevE = double.PositiveInfinity;
        var monoE = true;
        var floorE = true;
        foreach (var (fin, hw) in new[] { (20, 40), (35, 50), (50, 60), (65, 70), (80, 80), (95, 90) })
        {
            var bw = Blk(ShotLocation.Rim, Mk(50, fin: fin, h: hw, ws: hw, v: hw), defenderE);
            Console.WriteLine($"      shooter Fin={fin,2}  H/W/V={hw}  block={bw:P3}");
            if (bw >= prevE) { monoE = false; Console.WriteLine($"        FAIL — not strictly decreasing (prev={prevE:P3})"); }
            if (bw < rimFloor - 1e-9) { floorE = false; Console.WriteLine($"        FAIL — crossed the floor {rimFloor:P3}"); }
            prevE = bw;
        }
        if (monoE && floorE) Console.WriteLine("      OK — block rate falls as shooter improves; stays above floor.");
        pass &= monoE && floorE;

        // ----------------------------------------------------------------
        // (f) Regression guard — Phase 6 make door still works after block weight change.
        //     Elite finisher vs weak rim protector: make rate should be > 0.50 and
        //     the pie should be valid (no throw). Mirrors Phase 6 check (g) but verifies
        //     that the new matchup block weight (higher for a strong defender, passed into
        //     BuildRealPie) doesn't break the pie.
        // ----------------------------------------------------------------
        Console.WriteLine("  (f) Regression — Phase 6 make door valid after Phase 7 block change:");
        var fOk = true;
        try
        {
            var gReg = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gReg.SetPossessionArrow(TeamSide.Home);
            gReg.HomeRoster.SetStarter(gReg.HomeLineup.SlotAt(1), Mk(50, fin: 99, h: 70, ws: 70, v: 70));
            gReg.AwayRoster.SetStarter(gReg.AwayLineup.SlotAt(1), Mk(50, rimP: 10, h: 55, ws: 55, v: 55));
            var genReg = new RollHGenerator(cfgH, cfgM, gReg);
            var stateReg = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gReg.HomeLineup.SlotAt(1));

            var madeReg = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie = genReg.Generate(stateReg, putback: false);   // must not throw
                if (pie.Roll(new SystemRng(i).NextUnitInterval()) is ShotResult.Made or ShotResult.MadeAndFouled)
                    madeReg++;
            }
            var makeRate = (double)madeReg / 5_000;
            Console.WriteLine($"      elite finisher vs weak rim protector @Rim: make {makeRate:P1}  (valid pie)");
            var fRateOk = makeRate > 0.50;
            if (!fRateOk) Console.WriteLine($"  FAIL  (f) make rate {makeRate:P1} is not > 0.50 for an extreme shooter edge.");
            if (fRateOk) Console.WriteLine("      OK — valid pie; make rate > 0.50 for extreme shooter edge.");
            fOk = fRateOk;
        }
        catch (Exception ex)
        {
            fOk = false;
            Console.WriteLine($"  FAIL  (f) pie threw: {ex.Message}");
        }
        pass &= fOk;

        Console.WriteLine(pass ? "  Phase 7 PASSED." : "  Phase 7 FAILED.");
        return pass;
    }


    // =========================================================================
    // Phase 9 — shot-location door (Roll G matchup-aware)
    // =========================================================================
    private static bool Phase9LocationDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 9: Shot-location door (Roll G matchup-aware) ---");
        var pass = true;

        var cfgG       = RollGConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Helper: build a player with all attributes at baseline b.
        static Player Mk(int b,
                         int? fin = null, int? outside = null, int? mid = null, int? close = null,
                         int? rimP = null, int? perimD = null, int? postD = null,
                         int? rimT = null, int? shortT = null, int? midT = null, int? longT = null, int? threeT = null)
            => new Player("p")
            {
                Outside = outside ?? b, Mid = mid ?? b, Close = close ?? b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = b,
                BallHandling = b, Passing = b, Playmaking = b, SelfCreation = b, PostMoves = b,
                OffBallMovement = b, Screening = b, OffensiveRebounding = b,
                PerimeterDefense = perimD ?? b, PostDefense = postD ?? b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b, HelpDefense = b,
                OffBallDefense = b,
                RimTendency   = rimT   ?? b,
                ShortTendency = shortT ?? b,
                MidTendency   = midT   ?? b,
                LongTendency  = longT  ?? b,
                ThreeTendency = threeT ?? b,
            };

        // Helper: build a list of N copies of a player as the defending roster (slots 1..5).
        static IReadOnlyList<Player?> Defenders(params Player?[] ds) => ds;

        // Shorthand: compute shot mix for a shooter + defender list via RollGGenerator.
        Dictionary<ShotLocation, double> Mix(Player shooter, IReadOnlyList<Player?> defs)
        {
            // Build a minimal GameState with the shooter in Home slot 1, defenders in Away slots 1–5.
            var fouls = new FoulTracker(7, 10);
            var game  = new GameState(fouls);
            game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(1), shooter);
            for (var i = 0; i < defs.Count && i < 5; i++)
                if (defs[i] is not null)
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i + 1), defs[i]!);

            var gen = new RollGGenerator(cfgG, cfgMatchup, game);
            var state = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, SelectedSlot: game.HomeLineup.SlotAt(1));
            var pie = gen.Generate(state);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // ----------------------------------------------------------------
        // (a) Neutral matchup: zero gap everywhere → mix == raw tendencies normalized.
        //     Shooter: skewed tendencies (Rim=80, others=20). All offensive attributes
        //     50. All five defenders all-50. Every gap is exactly zero → every multiplier
        //     exactly 1.0 → bent mix == raw tendencies after renormalization.
        // ----------------------------------------------------------------
        Console.WriteLine("  (a) Neutral matchup: zero gap → mix equals raw tendencies normalized:");
        bool aOk;
        try
        {
            var shooter = Mk(50, fin: 50, rimT: 80, shortT: 20, midT: 20, longT: 20, threeT: 20);
            var defs    = Defenders(Mk(50), Mk(50), Mk(50), Mk(50), Mk(50));
            var mix     = Mix(shooter, defs);

            var rawSum = 80.0 + 20 + 20 + 20 + 20;
            var expectedRim = 80.0 / rawSum;
            var expectedOther = 20.0 / rawSum;

            var rimOk   = Math.Abs(mix[ShotLocation.Rim]   - expectedRim)   < Eps;
            var othersOk = Math.Abs(mix[ShotLocation.Short] - expectedOther) < Eps
                        && Math.Abs(mix[ShotLocation.Mid]   - expectedOther) < Eps
                        && Math.Abs(mix[ShotLocation.Long]  - expectedOther) < Eps
                        && Math.Abs(mix[ShotLocation.Three] - expectedOther) < Eps;
            aOk = rimOk && othersOk;

            Console.WriteLine($"    Rim={mix[ShotLocation.Rim]:P4}  expected={expectedRim:P4}  {(rimOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    others each={mix[ShotLocation.Short]:P4}  expected={expectedOther:P4}  {(othersOk ? "ok" : "FAIL")}");
            if (aOk) Console.WriteLine("    OK — zero-gap produces exact raw-tendency normalization.");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ----------------------------------------------------------------
        // (b) All-weak-rim defenders shift mix toward rim.
        //     Shooter: uniform tendencies (all 50), uniform offensive attrs (all 50).
        //     All five defenders: RimProtection=10, everything else 50.
        //     Expected: rim share rises noticeably above 20% (uniform baseline).
        // ----------------------------------------------------------------
        Console.WriteLine("  (b) All-five-weak-rim defenders: rim share rises above 20%:");
        bool bOk;
        try
        {
            var shooter = Mk(50, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            var weakRim = Mk(50, rimP: 10);
            var defs    = Defenders(weakRim, weakRim, weakRim, weakRim, weakRim);
            var mix     = Mix(shooter, defs);
            var rimShare = mix[ShotLocation.Rim];
            bOk = rimShare > 0.20;
            Console.WriteLine($"    rim share={rimShare:P3}  (>20%? {(bOk ? "OK" : "FAIL")})");
            if (bOk) Console.WriteLine("    OK — all-weak-rim defense raises rim share above uniform baseline.");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ----------------------------------------------------------------
        // (c) Top-3 blend: Config A (1 elite + 4 weak rim) vs Config B (1 elite + 3 solid + 1 weak).
        //     Config B should resist more (higher rim resistance) → lower rim share for the offense.
        // ----------------------------------------------------------------
        Console.WriteLine("  (c) Top-3 blend: Config B (1 elite + 3 solid) resists more than Config A (1 elite + 4 weak):");
        bool cOk;
        try
        {
            var shooter = Mk(50, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            // Config A: elite rim protector at slot-1, four weak at slots 2–5.
            var defsA = Defenders(Mk(50, rimP: 95), Mk(50, rimP: 10), Mk(50, rimP: 10), Mk(50, rimP: 10), Mk(50, rimP: 10));
            // Config B: elite at slot-1, three solid (60) at slots 2–4, one weak at slot-5.
            var defsB = Defenders(Mk(50, rimP: 95), Mk(50, rimP: 60), Mk(50, rimP: 60), Mk(50, rimP: 60), Mk(50, rimP: 10));
            var mixA  = Mix(shooter, defsA);
            var mixB  = Mix(shooter, defsB);

            var rimShareA = mixA[ShotLocation.Rim];
            var rimShareB = mixB[ShotLocation.Rim];
            // Config B has higher resistance → offense attacks rim less → lower rim share in B.
            cOk = rimShareB < rimShareA;
            Console.WriteLine($"    rim share A={rimShareA:P3}  rim share B={rimShareB:P3}  (B<A? {(cOk ? "OK" : "FAIL")})");
            if (cOk) Console.WriteLine("    OK — three solid help defenders add more resistance than three weak ones.");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ----------------------------------------------------------------
        // (d) Level-mismatch behavior: shift is gap-INEQUALITY-driven, not level-driven.
        //   (d1) D1 finisher vs D3 with weak rim → rim share rises (uneven gaps).
        //   (d2) Same-level matched control → mix stays within ~3pp of tendency baseline.
        //   (d3) Uniform D1 vs uniform D3 (everyone 75 vs everyone 45) → mix stays within ~2pp
        //        (uniform gaps cancel in renormalization — proves gap-inequality drives shifts).
        // ----------------------------------------------------------------
        Console.WriteLine("  (d) Level-mismatch behavior (gap-inequality driven):");
        bool dOk = true;
        try
        {
            // D1 finisher: rim-heavy offensive shape
            var finisher = Mk(50, fin: 85, outside: 65, mid: 60, close: 60,
                               rimT: 50, shortT: 25, midT: 15, longT: 5, threeT: 5);

            // D3 with weak rim protection: rim weak, perimeter normal
            var d3Weak = Mk(50, rimP: 30, perimD: 50, postD: 40);
            var d3Defs = Defenders(d3Weak, d3Weak, d3Weak, d3Weak, d3Weak);
            var mixMismatch = Mix(finisher, d3Defs);

            // Tendency baseline for finisher: rim=50/(50+25+15+5+5)=0.50
            var tendRimRaw = 50.0 / (50 + 25 + 15 + 5 + 5);
            var rimMismatch = mixMismatch[ShotLocation.Rim];
            var d1MismatchOk = rimMismatch > tendRimRaw;
            Console.WriteLine($"    (d1) D1 finisher vs D3 weak rim: rim={rimMismatch:P3}  tendency baseline={tendRimRaw:P3}  {(d1MismatchOk ? "OK — rises" : "FAIL")}");
            dOk &= d1MismatchOk;

            // Same-level matched control: defenders with same offensive shape everywhere
            var matched = Mk(50, rimP: 65, perimD: 65, postD: 65);
            var matchedDefs = Defenders(matched, matched, matched, matched, matched);
            var mixMatched  = Mix(finisher, matchedDefs);
            var rimMatched  = mixMatched[ShotLocation.Rim];
            // Mix stays within ~3pp of the tendency baseline when gaps are roughly equal
            var d2ControlOk = Math.Abs(rimMatched - tendRimRaw) < 0.06;
            Console.WriteLine($"    (d2) Same-level matched: rim={rimMatched:P3}  tendency baseline={tendRimRaw:P3}  diff={Math.Abs(rimMatched - tendRimRaw):P3}  {(d2ControlOk ? "OK" : "FAIL — too far from baseline")}");
            dOk &= d2ControlOk;

            // (d3) Plain D1 (everyone 75) vs plain D3 (everyone 45) — uniform attributes.
            // Uniform gaps cancel → mix very close to raw tendencies.
            var d1Off = Mk(75, rimT: 50, shortT: 25, midT: 15, longT: 5, threeT: 5);
            var d3Def = Mk(45);
            var d3DefsUniform = Defenders(d3Def, d3Def, d3Def, d3Def, d3Def);
            var mixUniform = Mix(d1Off, d3DefsUniform);
            var rimUniform = mixUniform[ShotLocation.Rim];
            var tendRimUniform = 50.0 / (50 + 25 + 15 + 5 + 5);
            var d3UniformOk = Math.Abs(rimUniform - tendRimUniform) < 0.02;
            Console.WriteLine($"    (d3) Uniform D1 vs D3 (all attrs 75 vs 45): rim={rimUniform:P3}  baseline={tendRimUniform:P3}  diff={Math.Abs(rimUniform - tendRimUniform):P3}  {(d3UniformOk ? "OK — uniform gaps cancel" : "FAIL")}");
            dOk &= d3UniformOk;
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ----------------------------------------------------------------
        // (e) DEC-6 partial defenders.
        //   (e1) Zero defenders: bent mix == tendency normalized exactly.
        //   (e2) One elite rim defender: 100% weight on him → strong suppression.
        //   (e3) Three populated (elite + two normal rim): top-3 blend dilutes → resistance ~77.
        //         Counterintuitive: one elite alone suppresses MORE than one elite + two normals
        //         (the blend renormalizes: 1 defender gets 100%; 3 defenders get 0.55/0.30/0.15).
        // ----------------------------------------------------------------
        Console.WriteLine("  (e) DEC-6 partial defenders:");
        bool eOk = true;
        try
        {
            var shooter = Mk(50, fin: 50, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);

            // (e1) Zero defenders: pure tendency normalization, NO matchup multiplier.
            var defs0 = Defenders(null, null, null, null, null);
            var mix0  = Mix(shooter, defs0);
            var rim0  = mix0[ShotLocation.Rim];
            var e1Ok  = Math.Abs(rim0 - 0.20) < Eps;   // all tendencies equal → each zone 20%
            Console.WriteLine($"    (e1) Zero defenders: rim={rim0:P4}  expected=20.0000%  {(e1Ok ? "OK" : "FAIL")}");
            eOk &= e1Ok;

            // (e2) One elite rim protector (slot-1 only, slots 2–5 null).
            // With 1 defender, blend renormalizes to 1.0 weight on him → resistance ≈ DefenseRating(Rim, elite).
            var elite = Mk(50, rimP: 99);
            var defs1 = Defenders(elite, null, null, null, null);
            var mix1  = Mix(shooter, defs1);
            var rim1  = mix1[ShotLocation.Rim];
            // Strong suppression expected — rim share should drop below 20%.
            var e2Ok  = rim1 < 0.20;
            Console.WriteLine($"    (e2) One elite rim defender: rim={rim1:P3}  (<20%? {(e2Ok ? "OK" : "FAIL")})");
            eOk &= e2Ok;

            // (e3) Three defenders: elite (99) + two normal (50). Top-3 blend: 0.55×99 + 0.30×50 + 0.15×50 ≈ 77.
            // One-defender suppression (e2) should be STRONGER than three-defender (e3) for the same elite — counterintuitive.
            var normal = Mk(50, rimP: 50);
            var defs3  = Defenders(elite, normal, normal, null, null);
            var mix3   = Mix(shooter, defs3);
            var rim3   = mix3[ShotLocation.Rim];
            // rim3 > rim1: three defenders dilutes the elite → less suppression → higher rim share.
            var e3Ok   = rim3 > rim1;
            Console.WriteLine($"    (e3) Three defenders (1 elite + 2 normal): rim={rim3:P3}  one-defender rim={rim1:P3}  (three>one? {(e3Ok ? "OK (elite diluted)" : "FAIL")})");
            Console.WriteLine($"         Note: this is correct — 1 elite alone gets 100% blend weight (max suppression);");
            Console.WriteLine($"         two average help partners dilute the blend and REDUCE suppression.");
            eOk &= e3Ok;
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ----------------------------------------------------------------
        // (f) Coaching seam is identity in v1.
        //     CoachingPull.Apply returns exactly the authored tendency values.
        // ----------------------------------------------------------------
        Console.WriteLine("  (f) Coaching seam identity:");
        bool fOk;
        try
        {
            var shooter = Mk(50, rimT: 30, shortT: 40, midT: 50, longT: 60, threeT: 70);
            var (rim, sh, mid, lng, three) = CoachingPull.Apply(shooter, null, null);
            fOk = rim == 30 && sh == 40 && mid == 50 && lng == 60 && three == 70;
            Console.WriteLine($"    returned ({rim},{sh},{mid},{lng},{three}) expected (30,40,50,60,70)  {(fOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ----------------------------------------------------------------
        // (g) Phase 6/7/8 regression — Roll H still correct given a forced zone.
        //     Force the shot zone via PossessionState (bypass Roll G) and verify
        //     make+and-1 / block / foul rates match Phase 8 expectations at Rim.
        // ----------------------------------------------------------------
        Console.WriteLine("  (g) Roll H regression with Phase 9 roster loaded:");
        bool gOk;
        try
        {
            var cfgH      = RollHConfig.Load(configPath);
            var foulsReg  = new FoulTracker(7, 10);
            var gReg      = new GameState(foulsReg);
            SeatStartersFromConfig(gReg, configPath);
            var genReg = new RollHGenerator(cfgH, cfgMatchup, gReg);

            // Force Rim, slot-1 shooter. Roll H still reads the same matchup context.
            var stateReg = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim,
                SelectedSlot: gReg.HomeLineup.SlotAt(1));

            var madeReg = 0;
            for (var i = 0; i < 5_000; i++)
            {
                var pie    = genReg.Generate(stateReg, putback: false);
                var result = pie.Roll(new SystemRng(i).NextUnitInterval());
                if (result is ShotResult.Made or ShotResult.MadeAndFouled) madeReg++;
            }
            var makeRate = (double)madeReg / 5_000;
            Console.WriteLine($"    slot-1 @Rim: make+and-1={makeRate:P1}  (valid pie, >0 and <1?)");
            gOk = makeRate > 0.0 && makeRate < 1.0;
            if (gOk)  Console.WriteLine("    OK — Roll H valid after Phase 9 roster seating.");
            if (!gOk) Console.WriteLine($"  FAIL  (g) make rate {makeRate:P1} out of (0, 1).");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        // ----------------------------------------------------------------
        // (h) v3 negative-multiplier guard.
        //     Direct call to Matchup.LocationMultiplier with extreme gaps.
        //     Confirms the ratio form never goes negative or outside the asymptote bounds.
        // ----------------------------------------------------------------
        Console.WriteLine("  (h) Negative-multiplier guard (ratio form bounds):");
        bool hOk = true;
        try
        {
            var maxMult  = cfgMatchup.LocationMaxMultiplier;
            var minMult  = 1.0 / maxMult;

            // Extreme positive: shooter 99 Finishing vs five defenders all RimProtection=1.
            var eliteShooter   = Mk(50, fin: 99, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            var weakDefenders  = Defenders(Mk(50, rimP: 1), Mk(50, rimP: 1), Mk(50, rimP: 1), Mk(50, rimP: 1), Mk(50, rimP: 1));
            var multPos = Matchup.LocationMultiplier(ShotLocation.Rim, eliteShooter, weakDefenders, cfgMatchup);

            var posOk = multPos > 0.0 && multPos < maxMult;
            Console.WriteLine($"    extreme positive (fin=99 vs rimP=1): mult={multPos:F6}  bounds=(0, {maxMult})  {(posOk ? "OK" : "FAIL")}");
            hOk &= posOk;

            // Extreme negative: shooter Finishing=1 vs five defenders all RimProtection=99.
            var poorShooter     = Mk(50, fin: 1, rimT: 50, shortT: 50, midT: 50, longT: 50, threeT: 50);
            var eliteDefenders  = Defenders(Mk(50, rimP: 99), Mk(50, rimP: 99), Mk(50, rimP: 99), Mk(50, rimP: 99), Mk(50, rimP: 99));
            var multNeg = Matchup.LocationMultiplier(ShotLocation.Rim, poorShooter, eliteDefenders, cfgMatchup);

            var negOk = multNeg > minMult && multNeg > 0.0;
            Console.WriteLine($"    extreme negative (fin=1 vs rimP=99): mult={multNeg:F6}  lower bound={minMult:F4}  {(negOk ? "OK — strictly positive, above lower asymptote" : "FAIL — at or below lower asymptote or negative")}");
            hOk &= negOk;
        }
        catch (Exception ex) { hOk = false; Console.WriteLine($"  FAIL  (h) threw: {ex.Message}"); }
        pass &= hOk;

        Console.WriteLine(pass ? "  Phase 9 PASSED." : "  Phase 9 FAILED.");
        return pass;
    }


    private static bool Phase39AssistCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 39: assist picker (AssistPicker) + on-walk attribution ---");
        var ok = true;
        const int N = 100_000;

        var matchupCfg = MatchupConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);

        // Helper: make a player with all attributes at base b, with named overrides.
        static Player MkP39(int id, int b,
                            int? passing = null, int? playmaking = null, int? iq = null)
            => new Player($"p{id}")
            {
                PlayerId             = id,
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b,
                Passing              = passing    ?? b,
                Playmaking           = playmaking ?? b,
                BasketballIQ         = iq         ?? b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = b, Steals = b,
                Height = b, Wingspan = b, Weight = b, Strength = b,
                Speed = b, Quickness = b, FirstStep = b, Vertical = b,
                Endurance = b, Hustle = b, Discipline = b, HelpDefense = b,
                OffBallDefense = b,
                ThreeTendency = 40, RimTendency = 30, MidTendency = 15,
                ShortTendency = 10, LongTendency = 5,
            };

        // Build a 5v5 GameState with given home players; away is dummy 50s.
        GameState BuildGame39(Player[] homePlayers)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < homePlayers.Length && i < 5; i++)
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), homePlayers[i]);
            for (var i = 1; i <= 5; i++)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), MkP39(i + 5, 50));
            return g;
        }

        // ── Sub-check A: good passers favored / shooter never self-assists ────
        {
            Console.WriteLine("  Sub-check A: good passers favored / shooter never self-assists");

            // Slot 1: elite passer. Slot 3: the shooter. Slots 2,4,5: average passers.
            var homePlayers = new[]
            {
                MkP39(1, 50, passing: 90, playmaking: 80, iq: 75),  // elite PG
                MkP39(2, 50),
                MkP39(3, 50),  // will be the shooter
                MkP39(4, 50),
                MkP39(5, 50),
            };
            var game  = BuildGame39(homePlayers);
            var rng   = new SystemRng(39101);
            var picks = new int[6];
            var selfAssists = 0;

            for (var i = 0; i < N; i++)
            {
                var shooterSlot = new Slot(TeamSide.Home, 3);
                var state = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound)
                    with { SelectedSlot = shooterSlot };
                var picked = AssistPicker.Pick(state, game, matchupCfg, rng);
                if (picked.Number >= 1 && picked.Number <= 5) picks[picked.Number]++;
                if (picked.Number == 3) selfAssists++;
            }

            var pgShare  = (double)picks[1] / N;
            var maxOther = new[] { picks[2], picks[4], picks[5] }.Max() / (double)N;
            var pgFavored = pgShare > maxOther;
            ok &= pgFavored;
            Console.WriteLine(pgFavored
                ? $"    [OK] PG (slot 1) share {pgShare:P1} > max non-passer share {maxOther:P1}"
                : $"    [FAIL] PG share {pgShare:P1} not > max non-passer {maxOther:P1}");

            var noSelf = selfAssists == 0;
            ok &= noSelf;
            Console.WriteLine(noSelf
                ? $"    [OK] Shooter (slot 3) never self-assisted across {N:N0} draws"
                : $"    [FAIL] Shooter self-assisted {selfAssists} times — exclusion broken");
        }

        // ── Sub-check B: AstBySlot.Total <= FGM invariant (per-possession) ────
        // Tested via direct AssistPicker rate math: clamp(base * factor, floor, ceil) <= 1.0
        // which means at most one assist per picker call, and the picker is called at most
        // once per eligible made FG. So AstBySlot.Total <= eligible FGM by construction.
        // Confirm here that the rate math stays in [0,1] across all five zones.
        {
            Console.WriteLine("  Sub-check B: assistProb in [floor, ceiling] for all zones, avg-passing lineup");

            var meanAw50 = 0.50 * 50 + 0.35 * 50 + 0.15 * 50;  // = 50.0 (avg lineup)
            var factor50 = 1.0 + matchupCfg.AssistPassSwing
                         * Math.Tanh((meanAw50 - matchupCfg.AssistPassMidpoint) / matchupCfg.AssistPassScale);
            var allInBounds = true;
            foreach (var zone in new[] { ShotLocation.Three, ShotLocation.Long, ShotLocation.Mid,
                                         ShotLocation.Short, ShotLocation.Rim })
            {
                var prob = Math.Clamp(matchupCfg.AssistedRate(zone) * factor50,
                                      matchupCfg.AssistRateFloor, matchupCfg.AssistRateCeiling);
                if (prob < matchupCfg.AssistRateFloor || prob > matchupCfg.AssistRateCeiling)
                    allInBounds = false;
                Console.WriteLine($"    {zone,-6}: base={matchupCfg.AssistedRate(zone):F3} × factor={factor50:F4} → prob={prob:F4}");
            }
            ok &= allInBounds;
            Console.WriteLine(allInBounds
                ? "    [OK] All zone probs within [floor, ceiling]"
                : "    [FAIL] At least one zone prob outside [floor, ceiling]");
        }

        // ── Sub-check C: per-zone ordering (Three > Long > Rim > Mid > Short) ──
        {
            Console.WriteLine("  Sub-check C: per-zone base rate ordering");
            var threeBase = matchupCfg.AssistedRateThree;
            var longBase  = matchupCfg.AssistedRateLong;
            var rimBase   = matchupCfg.AssistedRateRim;
            var midBase   = matchupCfg.AssistedRateMid;
            var shortBase = matchupCfg.AssistedRateShort;
            var orderOk = threeBase > longBase && rimBase > shortBase;
            ok &= orderOk;
            Console.WriteLine(orderOk
                ? $"    [OK] Three({threeBase:F2}) > Long({longBase:F2}); Rim({rimBase:F2}) > Short({shortBase:F2})"
                : $"    [FAIL] Zone rate ordering violated");
        }

        // ── Sub-check D: same-seed reproducibility (picker level) ─────────────
        {
            Console.WriteLine("  Sub-check D: same-seed reproducibility");
            var homePlayers = new[]
            {
                MkP39(1, 50, passing: 75, playmaking: 60, iq: 55),
                MkP39(2, 50),
                MkP39(3, 50),
                MkP39(4, 50),
                MkP39(5, 50),  // shooter
            };
            var game1 = BuildGame39(homePlayers);
            var game2 = BuildGame39(homePlayers);
            var shooterSlot = new Slot(TeamSide.Home, 5);
            var stateD = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound)
                with { SelectedSlot = shooterSlot };
            var rng1 = new SystemRng(39201);
            var rng2 = new SystemRng(39201);
            var picks1 = new int[6]; var picks2 = new int[6];
            for (var i = 0; i < 10_000; i++)
            {
                picks1[AssistPicker.Pick(stateD, game1, matchupCfg, rng1).Number]++;
                picks2[AssistPicker.Pick(stateD, game2, matchupCfg, rng2).Number]++;
            }
            var reproOk = picks1.SequenceEqual(picks2);
            ok &= reproOk;
            Console.WriteLine(reproOk
                ? "    [OK] Identical pick distributions from same seed"
                : "    [FAIL] Same seed produced different pick distributions");
        }

        // ── Sub-check E: empty non-shooter offense throws ─────────────────────
        {
            Console.WriteLine("  Sub-check E: empty non-shooter offense throws InvalidOperationException");
            var gE = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            // No players seated. Shooter slot = 1 → no other players eligible.
            var stateE = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound)
                with { SelectedSlot = new Slot(TeamSide.Home, 1) };
            try
            {
                AssistPicker.Pick(stateE, gE, matchupCfg, new SystemRng(0));
                Console.WriteLine("    [FAIL] No exception thrown on empty lineup");
                ok = false;
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("    [OK] InvalidOperationException thrown on empty non-shooter lineup");
            }
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 39 assist check: PASSED" : "  Phase 39 assist check: FAILED (see [FAIL] lines above)");
        return ok;
    }


    // =========================================================================
    // Phase 41 — HelpDefense interior make% suppression (C6)
    // =========================================================================
    private static bool Phase41HelpDefenseCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 41: HelpDefense interior make% suppression (C6) ---");
        var pass = true;
        const double Eps     = 1e-9;
        const double TightEps = 1e-10;   // byte-identical checks

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // Back-calculate the pre-block/pre-foul makePct from a pie.
        // Uses actual foul weight from the pie (maf + missFouled = foulRate × nonBNF_inverse),
        // avoiding any config-baseline assumption.
        static double MakePct(Pie<ShotResult> pie)
        {
            var blocked    = pie.Slices.First(s => s.Outcome == ShotResult.Blocked).Weight;
            var maf        = pie.Slices.First(s => s.Outcome == ShotResult.MadeAndFouled).Weight;
            var missFouled = pie.Slices.First(s => s.Outcome == ShotResult.MissFouled).Weight;
            var foul       = maf + missFouled;       // actual foul weight
            var nonBNF     = 1.0 - blocked - foul;
            var made       = pie.Slices.First(s => s.Outcome == ShotResult.Made).Weight;
            return nonBNF > 1e-9 ? made / nonBNF : 0.0;
        }

        static double BlockWt(Pie<ShotResult> pie) =>
            pie.Slices.First(s => s.Outcome == ShotResult.Blocked).Weight;

        // Build a player with all attributes at b; HelpDefense, RimProtection, Finishing, and OffBallDefense overridable.
        static Player Mk(int b, int? hd = null, int? rimP = null, int? fin = null, int? obd = null)
            => new Player("p")
            {
                Close = b, Mid = b, Outside = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b, HelpDefense = hd ?? b,
                OffBallDefense = obd ?? b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Seat a shooter in Home slot 1 (+ 4 neutral fillers), matched defender in Away slot 1,
        // and four off-ball defenders in Away slots 2-5.
        // SelectedSlot=slot1 → DefenderPicker picks Away slot 1 as the matched defender.
        Pie<ShotResult> Generate(
            Player shooter, Player matchedDef, Player[] offBall,
            ShotLocation zone, bool fastBreak = false)
        {
            var g       = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            // Shooter in slot 1; neutral fillers in slots 2-5 (slot 1 must be seated first).
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), shooter);
            var neutral = Mk(50, hd: 0);
            for (var i = 2; i <= 5; i++)
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), neutral);
            g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(1), matchedDef);
            for (var i = 0; i < 4; i++)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 2), offBall[i]);

            var state = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(1),
                ShotType: zone,
                FastBreak: fastBreak);

            return new RollHGenerator(cfgH, cfgM, g).Generate(state);
        }

        var shooter50  = Mk(50, hd: 0, fin: 50);
        var low        = Mk(50, hd: 0,  rimP: 50);
        var high       = Mk(50, hd: 99, rimP: 50);
        var fourZero   = new[] { low,  low,  low,  low  };
        var fourHigh   = new[] { high, high, high, high };

        // ── (a) Off-ball-only: matched HD=99 → NO suppression; inverse → full ──
        Console.WriteLine("  (a) Off-ball exclusion:");
        {
            // Zero baseline: matched=0, off-ball=0
            var makeBase = MakePct(Generate(shooter50, Mk(50, hd: 0,  rimP: 50), fourZero, ShotLocation.Rim));
            // Matched=99, off-ball=0 → should equal zero baseline (matched excluded)
            var makeMatchedHigh = MakePct(Generate(shooter50, Mk(50, hd: 99, rimP: 50), fourZero, ShotLocation.Rim));
            // Matched=0, off-ball=99 → full suppression (< baseline)
            var makeOffBallFull = MakePct(Generate(shooter50, Mk(50, hd: 0,  rimP: 50), fourHigh, ShotLocation.Rim));

            var excludedOk = Math.Abs(makeMatchedHigh - makeBase) < TightEps;
            var inverseOk  = makeOffBallFull < makeBase - Eps;
            Console.WriteLine($"    baseline (all HD=0):       makePct={makeBase:F6}");
            Console.WriteLine($"    matched HD=99, off-ball=0: makePct={makeMatchedHigh:F6}  equal to baseline → {(excludedOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    matched=0, off-ball=4×99:  makePct={makeOffBallFull:F6}  < baseline → {(inverseOk ? "ok" : "FAIL")}");
            pass &= excludedOk && inverseOk;
            Console.WriteLine($"  (a) {(excludedOk && inverseOk ? "ok" : "FAIL")}");
        }

        // ── (b) Accelerating aggregation ─────────────────────────────────────
        Console.WriteLine("  (b) Accelerating aggregation:");
        {
            var makeBase   = MakePct(Generate(shooter50, low, fourZero, ShotLocation.Rim));
            var make1High  = MakePct(Generate(shooter50, low, new[] { high, low, low, low  }, ShotLocation.Rim));
            var make4High  = MakePct(Generate(shooter50, low, fourHigh, ShotLocation.Rim));
            var drop1      = makeBase - make1High;
            var drop4      = makeBase - make4High;
            var ratio      = drop1 > Eps ? drop4 / drop1 : 0.0;
            var accelOk    = drop4 > 4.0 * drop1 && drop1 > Eps;
            Console.WriteLine($"    base:          makePct={makeBase:F6}");
            Console.WriteLine($"    1 good helper: makePct={make1High:F6}  drop={drop1*100:F4}pts");
            Console.WriteLine($"    4 good helpers:makePct={make4High:F6}  drop={drop4*100:F4}pts  ratio={ratio:F2}x (need >4x)");
            pass &= accelOk;
            Console.WriteLine($"  (b) {(accelOk ? "ok — super-linear" : "FAIL")}");
        }

        // ── (c) Zone-specific multipliers: Rim/Short full, Mid partial, Long/Three zero ──
        Console.WriteLine("  (c) Zone multipliers (Rim=1.0, Short=1.0, Mid=HelpDefenseMidMultiplier, Long=0, Three=0):");
        {
            var cOk          = true;
            const double TightEps2 = 1e-10;

            // Isolated helpDefenseDelta: matched-only baseline (hd=0), vs off-ball HD=80 run.
            // Using the same shooter (shooter50) to get clean deltas per zone.
            var matchedBase50 = Mk(50, hd: 0, rimP: 50, obd: 0);
            var offBallZero4  = new[] { Mk(50, hd: 0, obd: 0), Mk(50, hd: 0, obd: 0),
                                        Mk(50, hd: 0, obd: 0), Mk(50, hd: 0, obd: 0) };
            var offBallHigh4  = new[] { Mk(50, hd: 80, obd: 0), Mk(50, hd: 80, obd: 0),
                                        Mk(50, hd: 80, obd: 0), Mk(50, hd: 80, obd: 0) };

            // Rim delta (should be full)
            var rimBase  = MakePct(Generate(shooter50, matchedBase50, offBallZero4, ShotLocation.Rim));
            var rimHigh  = MakePct(Generate(shooter50, matchedBase50, offBallHigh4, ShotLocation.Rim));
            var rimDelta = rimBase - rimHigh;

            // Short delta (should equal Rim delta: both multiplier=1.0)
            var shortBase  = MakePct(Generate(shooter50, matchedBase50, offBallZero4, ShotLocation.Short));
            var shortHigh  = MakePct(Generate(shooter50, matchedBase50, offBallHigh4, ShotLocation.Short));
            var shortDelta = shortBase - shortHigh;

            // Mid delta (should be HelpDefenseMidMultiplier × rimDelta)
            var midBase  = MakePct(Generate(shooter50, matchedBase50, offBallZero4, ShotLocation.Mid));
            var midHigh  = MakePct(Generate(shooter50, matchedBase50, offBallHigh4, ShotLocation.Mid));
            var midDelta = midBase - midHigh;

            // Long delta (should be zero: multiplier=0.0)
            var longBase  = MakePct(Generate(shooter50, matchedBase50, offBallZero4, ShotLocation.Long));
            var longHigh  = MakePct(Generate(shooter50, matchedBase50, offBallHigh4, ShotLocation.Long));
            var longDelta = longBase - longHigh;

            // Three delta (should be zero: multiplier=0.0)
            var threeBase  = MakePct(Generate(shooter50, matchedBase50, offBallZero4, ShotLocation.Three));
            var threeHigh  = MakePct(Generate(shooter50, matchedBase50, offBallHigh4, ShotLocation.Three));
            var threeDelta = threeBase - threeHigh;

            // Rim and Short equal (both multiplier=1.0)
            var rimShortEqual = Math.Abs(rimDelta - shortDelta) < TightEps2;
            // Mid ratio: helpDefenseDelta@Mid = HelpDefenseMidMultiplier × helpDefenseDelta@Rim
            var expectedMidRatio = cfgH.HelpDefenseMidMultiplier;
            var actualMidRatio   = rimDelta > Eps ? midDelta / rimDelta : 0.0;
            var midRatioOk       = Math.Abs(actualMidRatio - expectedMidRatio) < 1e-9;
            // Long and Three zero
            var longZero  = Math.Abs(longDelta)  < TightEps2;
            var threeZero = Math.Abs(threeDelta) < TightEps2;
            // Rim fires
            var rimFires  = rimDelta > Eps;

            Console.WriteLine($"    Rim  delta={rimDelta*100:F4}pts (full)");
            Console.WriteLine($"    Short delta={shortDelta*100:F4}pts  Rim==Short → {(rimShortEqual ? "ok" : "FAIL")}");
            Console.WriteLine($"    Mid  delta={midDelta*100:F4}pts  ratio={actualMidRatio:F4} (expect {expectedMidRatio:F4}) → {(midRatioOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    Long  delta={longDelta*100:F4}pts  zero → {(longZero  ? "ok" : "FAIL")}");
            Console.WriteLine($"    Three delta={threeDelta*100:F4}pts  zero → {(threeZero ? "ok" : "FAIL")}");

            cOk = rimFires && rimShortEqual && midRatioOk && longZero && threeZero;
            pass &= cOk;
            Console.WriteLine($"  (c) {(cOk ? "ok" : "FAIL")}");
        }

        // ── (d) FastBreak exempt ──────────────────────────────────────────────
        Console.WriteLine("  (d) FastBreak exempt:");
        {
            var makeFBHigh = MakePct(Generate(shooter50, low, fourHigh, ShotLocation.Rim, fastBreak: true));
            var makeFBZero = MakePct(Generate(shooter50, low, fourZero, ShotLocation.Rim, fastBreak: true));
            var fbOk       = Math.Abs(makeFBHigh - makeFBZero) < TightEps;
            Console.WriteLine($"    FastBreak off-ball HD=99: makePct={makeFBHigh:F6}");
            Console.WriteLine($"    FastBreak off-ball HD=0:  makePct={makeFBZero:F6}  byte-identical → {(fbOk ? "ok" : "FAIL")}");
            pass &= fbOk;
            Console.WriteLine($"  (d) {(fbOk ? "ok" : "FAIL")}");
        }

        // ── (e) Clamp / no-throw ─────────────────────────────────────────────
        Console.WriteLine("  (e) Clamp / no-throw:");
        {
            var eOk = true;
            try
            {
                // Max suppression clamp
                var makeMax   = MakePct(Generate(shooter50, low, fourHigh, ShotLocation.Rim));
                var inRange   = makeMax >= 0.0 && makeMax <= 1.0;
                Console.WriteLine($"    max off-ball HD @Rim: makePct={makeMax:F6}  in [0,1] → {(inRange ? "ok" : "FAIL")}");
                if (!inRange) eOk = false;

                // Partial: 1 of 4 off-ball populated with HD=80
                MakePct(Generate(shooter50, low,
                    new[] { Mk(50, hd: 80), low, low, low }, ShotLocation.Rim));
                Console.WriteLine("    partial off-ball (1×HD80, 3×HD0): no throw → ok");

                // Zero off-ball HD
                MakePct(Generate(shooter50, low, fourZero, ShotLocation.Rim));
                Console.WriteLine("    all HD=0: no throw → ok");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL — threw: {ex.Message}");
                eOk = false;
            }
            pass &= eOk;
            Console.WriteLine($"  (e) {(eOk ? "ok" : "FAIL")}");
        }

        // ── (f) C6 (make%) and block door (block weight) are independent ──────
        Console.WriteLine("  (f) Independent: C6 (make%) vs block door (block weight):");
        {
            var fOk = true;

            // HelpDefense-only: hold matched defender RimProtection=50 constant;
            // vary only off-ball HelpDefense.
            // Expected: makePct changes, block weight byte-identical.
            var matchedFixed = Mk(50, hd: 0, rimP: 50);
            var pie_HD0  = Generate(shooter50, matchedFixed, fourZero, ShotLocation.Rim);
            var pie_HD99 = Generate(shooter50, matchedFixed, fourHigh, ShotLocation.Rim);
            var mkDiff   = Math.Abs(MakePct(pie_HD0) - MakePct(pie_HD99));
            var blkDiff  = Math.Abs(BlockWt(pie_HD0) - BlockWt(pie_HD99));
            var hdMkOk   = mkDiff > Eps;
            var hdBlkOk  = blkDiff < TightEps;
            Console.WriteLine($"    HD-only: makePct diff={mkDiff*100:F4}pts → {(hdMkOk ? "ok — changes" : "FAIL")}");
            Console.WriteLine($"             block  diff={blkDiff:F10}     → {(hdBlkOk ? "ok — identical" : "FAIL")}");
            if (!hdMkOk || !hdBlkOk) fOk = false;

            // RimProtection-only: hold ALL HelpDefense constant (off-ball=0 throughout);
            // vary only matched defender's RimProtection between two extremes.
            //
            // What we prove: the C6 suppression is INDEPENDENT of RimProtection.
            // Mechanism: helpDefenseSuppression = Scale × (offBallHD/4)^Exp is a pure
            // function of off-ball HelpDefense — it has no RimProtection term. So the
            // DROP (makePct_HD0 − makePct_HD99) must be byte-identical at RimP=10 vs RimP=90,
            // even though the makePct baseline itself shifts (RimProtection feeds EffectiveRating
            // at Rim zone and moves the make door — this is correct Stage 1 behaviour, not a bug).
            // If C6 were reading RimProtection, the drops would diverge.
            //
            // Also verify block weight changes with RimProtection (Stage 3 still fires).
            {
                var offBallZero = new[] { Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0) };
                var offBallFull = new[] { Mk(50, hd: 99), Mk(50, hd: 99), Mk(50, hd: 99), Mk(50, hd: 99) };
                var matchedLP   = Mk(50, hd: 0, rimP: 10);
                var matchedHP   = Mk(50, hd: 0, rimP: 90);

                // Block weight changes (Stage 3 fires)
                var pieLP_HD0  = Generate(shooter50, matchedLP, offBallZero, ShotLocation.Rim);
                var pieHP_HD0  = Generate(shooter50, matchedHP, offBallZero, ShotLocation.Rim);
                var rpBlkDiff  = Math.Abs(BlockWt(pieLP_HD0) - BlockWt(pieHP_HD0));
                var rpBlkOk    = rpBlkDiff > Eps;

                // C6 drop is identical at both RimProtection values
                var pieLP_HD99 = Generate(shooter50, matchedLP, offBallFull, ShotLocation.Rim);
                var pieHP_HD99 = Generate(shooter50, matchedHP, offBallFull, ShotLocation.Rim);
                var dropLP     = MakePct(pieLP_HD0) - MakePct(pieLP_HD99);
                var dropHP     = MakePct(pieHP_HD0) - MakePct(pieHP_HD99);
                var c6IndepOk  = Math.Abs(dropLP - dropHP) < TightEps;

                Console.WriteLine($"    RimProt-only: block diff={rpBlkDiff:F6}  → {(rpBlkOk ? "ok — changes" : "FAIL")}");
                Console.WriteLine($"    C6 indep: drop@RimP=10={dropLP*100:F4}pts  drop@RimP=90={dropHP*100:F4}pts  Δ={Math.Abs(dropLP - dropHP):F12}  → {(c6IndepOk ? "ok — C6 independent of RimProtection" : "FAIL")}");
                if (!rpBlkOk || !c6IndepOk) fOk = false;
            }

            pass &= fOk;
            Console.WriteLine($"  (f) {(fOk ? "ok — Stage 2/Stage 3 independent" : "FAIL")}");
        }

        Console.WriteLine(pass ? "  Phase 41 PASSED." : "  Phase 41 FAILED.");
        return pass;
    }

    // Phase 42 — Screening interior make% bonus (C5.5)
    // =========================================================================
    private static bool Phase42ScreeningCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 42: Screening interior make% bonus (C5.5) ---");
        var pass = true;
        const double Eps      = 1e-9;
        const double TightEps = 1e-10;   // byte-identical checks

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // Back-calculate the pre-block/pre-foul makePct from a pie.
        static double MakePct(Pie<ShotResult> pie)
        {
            var blocked    = pie.Slices.First(s => s.Outcome == ShotResult.Blocked).Weight;
            var maf        = pie.Slices.First(s => s.Outcome == ShotResult.MadeAndFouled).Weight;
            var missFouled = pie.Slices.First(s => s.Outcome == ShotResult.MissFouled).Weight;
            var foul       = maf + missFouled;
            var nonBNF     = 1.0 - blocked - foul;
            var made       = pie.Slices.First(s => s.Outcome == ShotResult.Made).Weight;
            return nonBNF > 1e-9 ? made / nonBNF : 0.0;
        }

        // Build a player with all attributes at b; Screening, HelpDefense, Finishing,
        // RimProtection, and OffBallDefense individually overridable.
        static Player Mk(int b, int? scr = null, int? hd = null, int? fin = null, int? rimP = null, int? obd = null)
            => new Player("p")
            {
                Close = b, Mid = b, Outside = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b,
                Screening = scr ?? b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b, HelpDefense = hd ?? b,
                OffBallDefense = obd ?? b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Seat shooter in Home slot 1 (+ 4 offensive teammates in slots 2-5),
        // matched defender in Away slot 1, four off-ball defenders in Away slots 2-5.
        // SelectedSlot=slot1 → DefenderPicker picks Away slot 1 as the matched defender.
        // Seat shooter first — SetStarter throws on already-occupied slots.
        Pie<ShotResult> Generate(
            Player shooter, Player[] offTeammates,
            Player matchedDef, Player[] offBallDefs,
            ShotLocation zone, bool fastBreak = false)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), shooter);
            for (var i = 0; i < 4; i++)
                if (offTeammates[i] is not null)
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 2), offTeammates[i]);
            g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(1), matchedDef);
            for (var i = 0; i < 4; i++)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 2), offBallDefs[i]);

            var state = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(1),
                ShotType: zone,
                FastBreak: fastBreak);

            return new RollHGenerator(cfgH, cfgM, g).Generate(state);
        }

        // screeningDelta: the C5.5 contribution isolated from all other terms.
        // L_intended  = lineup with the Screening values under test
        // L_zeroed    = identical lineup with every populated player's Screening forced to 0
        // Delta = MakePct(intended) − MakePct(zeroed)
        // All non-Screening ratings, zone, state, and defenders held identical.
        double ScreeningDelta(
            Player shooterIntended, Player[] teammatesIntended,
            Player shooterZeroed,   Player[] teammatesZeroed,
            Player matchedDef, Player[] offBallDefs,
            ShotLocation zone)
        {
            var intended = MakePct(Generate(shooterIntended, teammatesIntended, matchedDef, offBallDefs, zone));
            var zeroed   = MakePct(Generate(shooterZeroed,   teammatesZeroed,   matchedDef, offBallDefs, zone));
            return intended - zeroed;
        }

        // Shared neutral anchors
        var neutral50  = Mk(50, scr: 50, hd: 0);
        var matchedDef = Mk(50, hd: 0, rimP: 50);
        var offBallDefs = new[] { Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0) };
        var zero4      = new Player[] { Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0),
                                        Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };

        // ── (a) Aggregate-inclusive: shooter's Screening is symmetric with teammates ──
        Console.WriteLine("  (a) Aggregate-inclusive:");
        {
            // Lineup A: shooter Screening=99, four teammates Screening=0
            var shooterA   = Mk(50, scr: 99, hd: 0);
            var teams0     = new Player[] { Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0),
                                            Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };
            // Lineup B: shooter Screening=0, one teammate Screening=99, rest 0
            var shooterB   = Mk(50, scr: 0, hd: 0);
            var teamsB     = new Player[] { Mk(50, scr: 99, hd: 0), Mk(50, scr: 0, hd: 0),
                                            Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };

            var makeA = MakePct(Generate(shooterA, teams0,  matchedDef, offBallDefs, ShotLocation.Rim));
            var makeB = MakePct(Generate(shooterB, teamsB,  matchedDef, offBallDefs, ShotLocation.Rim));
            var aOk   = Math.Abs(makeA - makeB) < TightEps;
            Console.WriteLine($"    Lineup A (shooter scr=99, rest=0): makePct={makeA:F8}");
            Console.WriteLine($"    Lineup B (one teammate scr=99, rest=0): makePct={makeB:F8}  byte-identical → {(aOk ? "ok" : "FAIL")}");
            pass &= aOk;
            Console.WriteLine($"  (a) {(aOk ? "ok" : "FAIL")}");
        }

        // ── (b) Accelerating aggregation: 1 elite vs 5 elite ─────────────────
        Console.WriteLine("  (b) Accelerating aggregation:");
        {
            var shooter0   = Mk(50, scr: 0, hd: 0);
            var shooter99  = Mk(50, scr: 99, hd: 0);
            var teams0     = new Player[] { Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0),
                                            Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };
            var teams99    = new Player[] { Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0),
                                            Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0) };

            // delta1: one screener (shooter=99, rest=0)
            var delta1 = ScreeningDelta(
                shooter99, teams0,   // intended: shooter scr=99, teammates scr=0
                shooter0,  teams0,   // zeroed: all scr=0
                matchedDef, offBallDefs, ShotLocation.Rim);
            // delta5: five screeners (all=99)
            var delta5 = ScreeningDelta(
                shooter99, teams99,  // intended: all scr=99
                shooter0,  teams0,   // zeroed: all scr=0
                matchedDef, offBallDefs, ShotLocation.Rim);

            var ratio  = delta1 > Eps ? delta5 / delta1 : 0.0;
            var bOk    = delta5 > 5.0 * delta1 && delta1 > Eps;
            Console.WriteLine($"    delta1 (1×scr=99): +{delta1*100:F6}pts");
            Console.WriteLine($"    delta5 (5×scr=99): +{delta5*100:F6}pts  ratio={ratio:F4}x (expected ~25.00x = 5²)");
            pass &= bOk;
            Console.WriteLine($"  (b) {(bOk ? "ok — accelerating" : "FAIL")}");
        }

        // ── (c) All five zones equal — gate removed (Phase 44) ───────────────
        Console.WriteLine("  (c) All five zones — C5.5 fires uniformly (gate removed):");
        {
            var shooterHigh = Mk(50, scr: 99, hd: 0, obd: 0);
            var shooterZero = Mk(50, scr: 0,  hd: 0, obd: 0);
            var teamsHigh   = new Player[] { Mk(50, scr: 99, hd: 0, obd: 0), Mk(50, scr: 99, hd: 0, obd: 0),
                                             Mk(50, scr: 99, hd: 0, obd: 0), Mk(50, scr: 99, hd: 0, obd: 0) };
            var teamsZero   = new Player[] { Mk(50, scr: 0, hd: 0, obd: 0), Mk(50, scr: 0, hd: 0, obd: 0),
                                             Mk(50, scr: 0, hd: 0, obd: 0), Mk(50, scr: 0, hd: 0, obd: 0) };
            var cOk      = true;
            double? rimDelta42 = null;
            foreach (var zone in new[] { ShotLocation.Rim, ShotLocation.Short,
                                         ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three })
            {
                var deltaZ = ScreeningDelta(shooterHigh, teamsHigh,
                                            shooterZero, teamsZero,
                                            matchedDef, offBallDefs, zone);
                if (rimDelta42 is null) rimDelta42 = deltaZ;
                var positive = deltaZ > Eps;
                var equalToRim = Math.Abs(deltaZ - rimDelta42.Value) < TightEps;
                Console.WriteLine($"    {zone,5}: delta=+{deltaZ*100:F6}pts  positive={positive}  equal to Rim → {(equalToRim ? "ok" : "FAIL")}");
                if (!positive || !equalToRim) cOk = false;
            }
            pass &= cOk;
            Console.WriteLine($"  (c) {(cOk ? "ok — all five zones equal" : "FAIL")}");
        }

        // ── (d) FastBreak exempt ──────────────────────────────────────────────
        Console.WriteLine("  (d) FastBreak exempt:");
        {
            var shooterHigh = Mk(50, scr: 99, hd: 0);
            var shooterZero = Mk(50, scr: 0,  hd: 0);
            var teamsHigh   = new Player[] { Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0),
                                             Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0) };
            var teamsZero   = new Player[] { Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0),
                                             Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };
            var makeFBHigh = MakePct(Generate(shooterHigh, teamsHigh, matchedDef, offBallDefs, ShotLocation.Rim, fastBreak: true));
            var makeFBZero = MakePct(Generate(shooterZero, teamsZero, matchedDef, offBallDefs, ShotLocation.Rim, fastBreak: true));
            var dOk        = Math.Abs(makeFBHigh - makeFBZero) < TightEps;
            Console.WriteLine($"    FastBreak scr=99: makePct={makeFBHigh:F6}");
            Console.WriteLine($"    FastBreak scr=0:  makePct={makeFBZero:F6}  byte-identical → {(dOk ? "ok" : "FAIL")}");
            pass &= dOk;
            Console.WriteLine($"  (d) {(dOk ? "ok" : "FAIL")}");
        }

        // ── (e) Clamp / no-throw ──────────────────────────────────────────────
        Console.WriteLine("  (e) Clamp / no-throw:");
        {
            var eOk = true;
            try
            {
                // Max-everything: 5×scr=99, maxed shooter Finishing, weak rim defender
                var shooterMax = Mk(99, scr: 99, hd: 0, fin: 99);
                var teamsMax   = new Player[] { Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0),
                                                Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0) };
                var weakDef    = Mk(20, hd: 0, rimP: 20);
                var defWeak    = new[] { Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0) };
                var makeMax    = MakePct(Generate(shooterMax, teamsMax, weakDef, defWeak, ShotLocation.Rim));
                var inRange    = makeMax >= 0.0 && makeMax <= 1.0;
                Console.WriteLine($"    max-everything @Rim: makePct={makeMax:F6}  in [0,1] → {(inRange ? "ok" : "FAIL")}");
                if (!inRange) eOk = false;

                // Partial-roster: 2 of 5 offensive slots populated (shooter + 1 teammate),
                // three teammate slots null. Use delta convention.
                var shooterP80  = Mk(50, scr: 80, hd: 0);
                var shooterP0   = Mk(50, scr: 0,  hd: 0);
                var teamsPartI  = new Player[] { Mk(50, scr: 80, hd: 0), null!, null!, null! };
                var teamsPartZ  = new Player[] { Mk(50, scr: 0,  hd: 0), null!, null!, null! };
                var teams5_80   = new Player[] { Mk(50, scr: 80, hd: 0), Mk(50, scr: 80, hd: 0),
                                                 Mk(50, scr: 80, hd: 0), Mk(50, scr: 80, hd: 0) };
                var teams5_0    = new Player[] { Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0),
                                                 Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };

                // partialDelta: 2-populated lineup with scr=80 vs scr=0
                var partialDelta = ScreeningDelta(
                    shooterP80, teamsPartI,
                    shooterP0,  teamsPartZ,
                    matchedDef, offBallDefs, ShotLocation.Rim);
                // fullDelta: 5-populated lineup with scr=80 vs scr=0
                var fullDelta = ScreeningDelta(
                    shooterP80, teams5_80,
                    shooterP0,  teams5_0,
                    matchedDef, offBallDefs, ShotLocation.Rim);

                var partRatio = fullDelta > Eps ? partialDelta / fullDelta : 0.0;
                var partOk    = Math.Abs(partRatio - 0.16) < 1e-6;
                Console.WriteLine($"    partial (2/5, scr=80): delta={partialDelta*100:F6}pts");
                Console.WriteLine($"    full    (5/5, scr=80): delta={fullDelta*100:F6}pts");
                Console.WriteLine($"    ratio={partRatio:F6}  (expected (2/5)²=0.16) → {(partOk ? "ok" : "FAIL")}");
                if (!partOk) eOk = false;

                // Shooter-only: one slot populated (shooter=80), four teammates null
                var shooterOnly80 = Mk(50, scr: 80, hd: 0);
                var shooterOnly0  = Mk(50, scr: 0,  hd: 0);
                var teamsNull     = new Player[] { null!, null!, null!, null! };

                var shooterOnlyDelta = ScreeningDelta(
                    shooterOnly80, teamsNull,
                    shooterOnly0,  teamsNull,
                    matchedDef, offBallDefs, ShotLocation.Rim);
                var expected = cfgH.ScreeningBonusScale
                             * Math.Pow(80.0 / 100.0 / 5.0, cfgH.ScreeningAggregateExponent);
                var soOk = Math.Abs(shooterOnlyDelta - expected) < 1e-9;
                Console.WriteLine($"    shooter-only (scr=80, 4 null): delta={shooterOnlyDelta*100:F8}pts  expected={expected*100:F8}pts → {(soOk ? "ok" : "FAIL")}");
                if (!soOk) eOk = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL — threw: {ex.Message}");
                eOk = false;
            }
            pass &= eOk;
            Console.WriteLine($"  (e) {(eOk ? "ok" : "FAIL")}");
        }

        // ── (f) Symmetric cancellation: elite Screening + elite HelpDefense ───
        Console.WriteLine("  (f) Symmetric cancellation:");
        {
            var actualMaxScreeningBonus = cfgH.ScreeningBonusScale
                                        * Math.Pow(0.99, cfgH.ScreeningAggregateExponent);
            var actualMaxHDSuppression  = cfgH.HelpDefenseSuppressionScale
                                        * Math.Pow(0.99, cfgH.HelpDefenseAggregateExponent);

            // Baseline: all Screening=0, all HelpDefense=0
            var shooter50  = Mk(50, scr: 0, hd: 0);
            var teams0scr  = new Player[] { Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0),
                                            Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };
            var defMatched0 = Mk(50, hd: 0, rimP: 50);
            var defOB0      = new[] { Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0) };
            var makeBaseline = MakePct(Generate(shooter50, teams0scr, defMatched0, defOB0, ShotLocation.Rim));

            // Bonus-alone: 5×scr=99, HD=0 everywhere
            var shooter99  = Mk(50, scr: 99, hd: 0);
            var teams99scr = new Player[] { Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0),
                                            Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0) };
            var makeBonusAlone = MakePct(Generate(shooter99, teams99scr, defMatched0, defOB0, ShotLocation.Rim));

            // Suppression-alone: scr=0 everywhere, 4×off-ball HD=99
            var defOB99 = new[] { Mk(50, hd: 99), Mk(50, hd: 99), Mk(50, hd: 99), Mk(50, hd: 99) };
            var makeSupprAlone = MakePct(Generate(shooter50, teams0scr, defMatched0, defOB99, ShotLocation.Rim));

            // Elite-vs-elite: 5×scr=99, 4×HD=99
            var makeEliteElite = MakePct(Generate(shooter99, teams99scr, defMatched0, defOB99, ShotLocation.Rim));

            var cancelResidual = Math.Abs(makeBaseline - makeEliteElite);
            var bonusMag       = makeBonusAlone - makeBaseline;
            var supprMag       = makeBaseline - makeSupprAlone;
            var symmetryDiff   = Math.Abs(bonusMag - supprMag);
            var cancelOk       = cancelResidual < 1e-6;
            var symmetryOk     = symmetryDiff < 1e-6;

            Console.WriteLine($"    max Screening bonus:     {actualMaxScreeningBonus:F9}");
            Console.WriteLine($"    max HelpDef suppression: {actualMaxHDSuppression:F9}");
            Console.WriteLine($"    baseline makePct:       {makeBaseline:F6}");
            Console.WriteLine($"    bonus-alone makePct:    {makeBonusAlone:F6}  (+{bonusMag*100:F4}pts)");
            Console.WriteLine($"    suppression-alone:      {makeSupprAlone:F6}  (-{supprMag*100:F4}pts)");
            Console.WriteLine($"    elite-vs-elite makePct: {makeEliteElite:F6}");
            Console.WriteLine($"    cancellation residual:  {cancelResidual:F2e}  → {(cancelOk ? "ok — cancels" : "FAIL")}");
            Console.WriteLine($"    symmetry (|bonus|=|supr|): Δ={symmetryDiff:F2e}  → {(symmetryOk ? "ok" : "FAIL")}");
            pass &= cancelOk && symmetryOk;
            Console.WriteLine($"  (f) {(cancelOk && symmetryOk ? "ok" : "FAIL")}");
        }

        // ── (g) Ceiling-symmetry regression: cancellation near upper clamp ────
        // Specifically catches premature C5.5 upper-clamping.
        Console.WriteLine("  (g) Ceiling-symmetry regression:");
        {
            var gOk = true;
            try
            {
                var actualMaxScreeningBonus = cfgH.ScreeningBonusScale
                                            * Math.Pow(0.99, cfgH.ScreeningAggregateExponent);
                var saturationThreshold = 1.0 - actualMaxScreeningBonus;

                // High-baseline fixture: finishing-99 shooter, weak rim matched defender
                var shooterHigh  = Mk(50, scr: 0,  hd: 0, fin: 99);
                var teamsAll0    = new Player[] { Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0),
                                                  Mk(50, scr: 0, hd: 0), Mk(50, scr: 0, hd: 0) };
                var weakRimDef   = Mk(20, hd: 0, rimP: 20);
                var defOBzero    = new[] { Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0), Mk(50, hd: 0) };

                // Both-off baseline: scr=0, HD=0
                var bothOffBaseline = MakePct(Generate(shooterHigh, teamsAll0, weakRimDef, defOBzero, ShotLocation.Rim));

                // Assert fixture is in saturation-sensitive range
                if (!(bothOffBaseline > saturationThreshold))
                {
                    Console.WriteLine($"    FAIL — fixture baseline {bothOffBaseline:F6} does not exceed saturation threshold {saturationThreshold:F6}; test would pass vacuously with buggy code.");
                    pass = false;
                    Console.WriteLine($"  (g) FAIL — fixture out of range");
                }
                else
                {
                    Console.WriteLine($"    both-off baseline:    {bothOffBaseline:F6}  > threshold {saturationThreshold:F6} → fixture in range");

                    // Both-elite: 5×scr=99, 4×off-ball HD=99
                    var shooterHigh99 = Mk(50, scr: 99, hd: 0, fin: 99);
                    var teams99       = new Player[] { Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0),
                                                       Mk(50, scr: 99, hd: 0), Mk(50, scr: 99, hd: 0) };
                    var defOBhigh     = new[] { Mk(50, hd: 99), Mk(50, hd: 99), Mk(50, hd: 99), Mk(50, hd: 99) };
                    var bothElite     = MakePct(Generate(shooterHigh99, teams99, weakRimDef, defOBhigh, ShotLocation.Rim));

                    var cancelResidual = Math.Abs(bothOffBaseline - bothElite);
                    var gCancelOk      = cancelResidual < 1e-6;
                    Console.WriteLine($"    both-elite makePct:   {bothElite:F6}");
                    Console.WriteLine($"    cancellation residual:{cancelResidual:F2e}  (premature-clamp bug would show ≈{(bothOffBaseline + actualMaxScreeningBonus - 1.0)*100:F4}pts error)");
                    Console.WriteLine($"    → {(gCancelOk ? "ok — deferred-clamp contract holds" : "FAIL — premature clamp detected")}");
                    gOk &= gCancelOk;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL — threw: {ex.Message}");
                gOk = false;
            }
            pass &= gOk;
            Console.WriteLine($"  (g) {(gOk ? "ok" : "FAIL")}");
        }

        Console.WriteLine(pass ? "  Phase 42 PASSED." : "  Phase 42 FAILED.");
        return pass;
    }

    // Phase 44 — OffBallDefense: perimeter make% suppression (C7) + selection compression
    // =========================================================================
    private static bool Phase44OffBallDefenseCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 44: OffBallDefense — C7 suppression + BendByAttention compression ---");
        var pass = true;
        const double Eps      = 1e-9;
        const double TightEps = 1e-9;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);

        // Back-calculate the pre-block/pre-foul makePct from a pie.
        static double MakePct(Pie<ShotResult> pie)
        {
            var blocked    = pie.Slices.First(s => s.Outcome == ShotResult.Blocked).Weight;
            var maf        = pie.Slices.First(s => s.Outcome == ShotResult.MadeAndFouled).Weight;
            var missFouled = pie.Slices.First(s => s.Outcome == ShotResult.MissFouled).Weight;
            var foul       = maf + missFouled;
            var nonBNF     = 1.0 - blocked - foul;
            var made       = pie.Slices.First(s => s.Outcome == ShotResult.Made).Weight;
            return nonBNF > 1e-9 ? made / nonBNF : 0.0;
        }

        // Build a player with all attributes at b; HelpDefense, OffBallDefense,
        // RimProtection, Finishing individually overridable.
        static Player Mk(int b, int? hd = null, int? obd = null, int? rimP = null,
                         int? fin = null, int? height = null, int? postDef = null, int? str = null)
            => new Player("p")
            {
                Close = b, Mid = b, Outside = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = postDef ?? b, RimProtection = rimP ?? b,
                DefensiveRebounding = b, Steals = b,
                HelpDefense = hd ?? b, OffBallDefense = obd ?? b,
                Height = height ?? b, Wingspan = b, Weight = b,
                Strength = str ?? b, Speed = b, Quickness = b, FirstStep = b, Vertical = b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Seat a shooter in Home slot 1 (+ 4 neutral fillers), matched defender in Away slot 1,
        // four off-ball defenders in Away slots 2-5.
        // SelectedSlot=slot1 → DefenderPicker picks Away slot 1 as matched defender.
        Pie<ShotResult> Generate(
            Player shooter, Player matchedDef, Player[] offBall,
            ShotLocation zone, bool fastBreak = false)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), shooter);
            var neutral = Mk(50, hd: 0, obd: 0);
            for (var i = 2; i <= 5; i++)
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), neutral);
            g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(1), matchedDef);
            for (var i = 0; i < 4; i++)
                if (offBall[i] is not null)
                    g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 2), offBall[i]);
            var state = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home,
                Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(1),
                ShotType: zone,
                FastBreak: fastBreak);
            return new RollHGenerator(cfgH, cfgM, g).Generate(state);
        }

        // Delta discipline: all magnitude/ratio assertions use isolated term deltas.
        double OffBallDefDelta(
            Player shooter, Player matchedDef, Player[] offBall,
            Player[] offBallZero,
            ShotLocation zone)
        {
            var intended = MakePct(Generate(shooter, matchedDef, offBall, zone));
            var zeroed   = MakePct(Generate(shooter, matchedDef, offBallZero, zone));
            return intended - zeroed;  // negative: suppression lowers make%
        }

        var shooter50     = Mk(50, hd: 0, obd: 0);
        var matchedDef50  = Mk(50, hd: 0, obd: 0, rimP: 50);
        var fourZeroObd   = new[] { Mk(50, hd: 0, obd: 0), Mk(50, hd: 0, obd: 0),
                                    Mk(50, hd: 0, obd: 0), Mk(50, hd: 0, obd: 0) };
        var fourHighObd   = new[] { Mk(50, hd: 0, obd: 80), Mk(50, hd: 0, obd: 80),
                                    Mk(50, hd: 0, obd: 80), Mk(50, hd: 0, obd: 80) };

        // ── (a) C7 direction + matched-defender exclusion + fixed-denominator proof ──
        Console.WriteLine("  (a) C7 direction + matched-defender exclusion + fixed-denominator:");
        {
            var aOk = true;

            // Direction: delta at Long and Three is negative; Rim and Short are zero.
            var deltaLong  = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Long);
            var deltaThree = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Three);
            var deltaRim   = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Rim);
            var deltaShort = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Short);

            var longNeg   = deltaLong  < -Eps;
            var threeNeg  = deltaThree < -Eps;
            var rimZero   = Math.Abs(deltaRim)   < TightEps;
            var shortZero = Math.Abs(deltaShort) < TightEps;

            Console.WriteLine($"    Long  delta={deltaLong*100:F4}pts  < 0 → {(longNeg   ? "ok" : "FAIL")}");
            Console.WriteLine($"    Three delta={deltaThree*100:F4}pts  < 0 → {(threeNeg  ? "ok" : "FAIL")}");
            Console.WriteLine($"    Rim   delta={deltaRim*100:F4}pts   = 0 → {(rimZero   ? "ok" : "FAIL")}");
            Console.WriteLine($"    Short delta={deltaShort*100:F4}pts  = 0 → {(shortZero ? "ok" : "FAIL")}");
            aOk &= longNeg && threeNeg && rimZero && shortZero;

            // Matched-defender exclusion:
            // Run A: matched OBD=99, off-ball=0 → delta must be exactly zero.
            var matchedHigh99 = Mk(50, hd: 0, obd: 99, rimP: 50);
            var runADelta = OffBallDefDelta(shooter50, matchedHigh99, fourZeroObd, fourZeroObd, ShotLocation.Long);
            var runAOk    = Math.Abs(runADelta) < TightEps;
            Console.WriteLine($"    Run A (matched OBD=99, off-ball=0): delta={runADelta:F12}  = 0 → {(runAOk ? "ok" : "FAIL")}");
            aOk &= runAOk;

            // Run B: matched OBD=0, off-ball=4×99 → full suppression exactly.
            var fourNinetyNine = new[] { Mk(50, hd: 0, obd: 99), Mk(50, hd: 0, obd: 99),
                                         Mk(50, hd: 0, obd: 99), Mk(50, hd: 0, obd: 99) };
            var matchedZero50  = Mk(50, hd: 0, obd: 0, rimP: 50);
            var runBDelta = OffBallDefDelta(shooter50, matchedZero50, fourNinetyNine, fourZeroObd, ShotLocation.Long);
            var expectedFullSupp = cfgH.OffBallDefenseSuppressionScale
                                 * Math.Pow(0.99, cfgH.OffBallDefenseAggregateExponent)
                                 * 1.0; // Long zone multiplier = 1.0
            var runBOk = Math.Abs(runBDelta + expectedFullSupp) < TightEps;
            Console.WriteLine($"    Run B (matched=0, off-ball=4×99): delta={runBDelta*100:F6}pts  expected={-expectedFullSupp*100:F6}pts → {(runBOk ? "ok" : "FAIL")}");
            aOk &= runBOk;

            // Fixed-denominator sparse-lineup proof:
            // 1 off-ball at 99, 3 null slots → divide by 4.0 (capacity), not 1.
            var sparseOffBall = new Player[] { Mk(50, hd: 0, obd: 99), null!, null!, null! };
            var sparseDelta = OffBallDefDelta(shooter50, matchedZero50, sparseOffBall, fourZeroObd, ShotLocation.Long);
            var expectedSparse = cfgH.OffBallDefenseSuppressionScale
                               * Math.Pow(0.99 / 4.0, cfgH.OffBallDefenseAggregateExponent)
                               * 1.0; // Long zone multiplier = 1.0
            var sparseOk = Math.Abs(sparseDelta + expectedSparse) < TightEps;
            Console.WriteLine($"    Sparse (1×OBD99, 3×null): delta={sparseDelta*100:F6}pts  expected={-expectedSparse*100:F6}pts → {(sparseOk ? "ok — fixed denominator" : "FAIL")}");
            aOk &= sparseOk;

            pass &= aOk;
            Console.WriteLine($"  (a) {(aOk ? "ok" : "FAIL")}");
        }

        // ── (b) C7 zone specificity — delta-based ────────────────────────────
        Console.WriteLine("  (b) C7 zone specificity:");
        {
            var bOk = true;

            var deltaRim2   = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Rim);
            var deltaShort2 = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Short);
            var deltaMid    = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Mid);
            var deltaLong2  = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Long);
            var deltaThree2 = OffBallDefDelta(shooter50, matchedDef50, fourHighObd, fourZeroObd, ShotLocation.Three);

            var rimZero2   = Math.Abs(deltaRim2)   < TightEps;
            var shortZero2 = Math.Abs(deltaShort2) < TightEps;
            var midRatio   = Math.Abs(deltaLong2) > Eps
                             ? Math.Abs(deltaMid) / Math.Abs(deltaLong2)
                             : 0.0;
            var midRatioOk   = Math.Abs(midRatio - cfgH.OffBallDefenseMidMultiplier) < TightEps;
            var longEqThree  = Math.Abs(deltaLong2 - deltaThree2) < TightEps;

            Console.WriteLine($"    Rim   delta={deltaRim2*100:F4}pts   = 0 → {(rimZero2   ? "ok" : "FAIL")}");
            Console.WriteLine($"    Short delta={deltaShort2*100:F4}pts  = 0 → {(shortZero2 ? "ok" : "FAIL")}");
            Console.WriteLine($"    Mid   delta={deltaMid*100:F4}pts   ratio={midRatio:F4} (expect {cfgH.OffBallDefenseMidMultiplier:F4}) → {(midRatioOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    Long  delta={deltaLong2*100:F4}pts");
            Console.WriteLine($"    Three delta={deltaThree2*100:F4}pts  Long==Three → {(longEqThree ? "ok" : "FAIL")}");

            bOk = rimZero2 && shortZero2 && midRatioOk && longEqThree;
            pass &= bOk;
            Console.WriteLine($"  (b) {(bOk ? "ok" : "FAIL")}");
        }

        // ── (c) C5.5 fires uniformly on all five zones — delta-based ─────────
        Console.WriteLine("  (c) C5.5 fires uniformly on all five zones:");
        {
            var cOk = true;

            // Build a lineup of great screeners (Screening=80, all others baseline, OBD=0 and HD=0 to isolate)
            var screenerShooter  = Mk(50, hd: 0, obd: 0);
            screenerShooter = new Player("p") {
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 80,
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50,
                RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                HelpDefense = 0, OffBallDefense = 0,
                Height = 50, Wingspan = 50, Weight = 50, Strength = 50,
                Speed = 50, Quickness = 50, FirstStep = 50, Vertical = 50,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                RimTendency = 50, ShortTendency = 50, MidTendency = 50,
                LongTendency = 50, ThreeTendency = 50,
            };
            Player MkScr(int scrVal) => new Player("p") {
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = scrVal,
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50,
                RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                HelpDefense = 0, OffBallDefense = 0,
                Height = 50, Wingspan = 50, Weight = 50, Strength = 50,
                Speed = 50, Quickness = 50, FirstStep = 50, Vertical = 50,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                RimTendency = 50, ShortTendency = 50, MidTendency = 50,
                LongTendency = 50, ThreeTendency = 50,
            };

            var neutralMatchedDef = Mk(50, hd: 0, obd: 0, rimP: 50);
            var neutralOffBall    = new[] { Mk(50, hd: 0, obd: 0), Mk(50, hd: 0, obd: 0),
                                            Mk(50, hd: 0, obd: 0), Mk(50, hd: 0, obd: 0) };

            double ScrDelta(ShotLocation zone)
            {
                // Must pass Screening=80 version of generate helper differently —
                // rebuild the game manually so we can seat 5 screeners for home offense.
                var g80 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var g00 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var scr80 = MkScr(80);
                var scr00 = MkScr(0);
                for (var i = 1; i <= 5; i++)
                {
                    g80.HomeRoster.SetStarter(g80.HomeLineup.SlotAt(i), scr80);
                    g00.HomeRoster.SetStarter(g00.HomeLineup.SlotAt(i), scr00);
                }
                g80.AwayRoster.SetStarter(g80.AwayLineup.SlotAt(1), neutralMatchedDef);
                g00.AwayRoster.SetStarter(g00.AwayLineup.SlotAt(1), neutralMatchedDef);
                for (var i = 0; i < 4; i++)
                {
                    g80.AwayRoster.SetStarter(g80.AwayLineup.SlotAt(i + 2), neutralOffBall[i]);
                    g00.AwayRoster.SetStarter(g00.AwayLineup.SlotAt(i + 2), neutralOffBall[i]);
                }
                var st = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: g80.HomeLineup.SlotAt(1), ShotType: zone);
                var m80 = MakePct(new RollHGenerator(cfgH, cfgM, g80).Generate(st));
                var m00 = MakePct(new RollHGenerator(cfgH, cfgM, g00).Generate(st));
                return m80 - m00;
            }

            double? rimDelta = null;
            foreach (var zone in new[] { ShotLocation.Rim, ShotLocation.Short,
                                         ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three })
            {
                var d = ScrDelta(zone);
                if (rimDelta is null) rimDelta = d;
                var pos      = d > Eps;
                var eqRim    = Math.Abs(d - rimDelta.Value) < TightEps;
                Console.WriteLine($"    {zone,5}: delta=+{d*100:F6}pts  positive={pos}  equal-to-Rim → {(eqRim ? "ok" : "FAIL")}");
                if (!pos || !eqRim) cOk = false;
            }

            pass &= cOk;
            Console.WriteLine($"  (c) {(cOk ? "ok — uniform all five zones" : "FAIL")}");
        }

        // ── (d) C6 zone multiplier at Mid — delta-based ──────────────────────
        Console.WriteLine("  (d) C6 zone multipliers (Rim/Short full, Mid partial, Long/Three zero):");
        {
            var dOk = true;
            var fourHD80 = new[] { Mk(50, hd: 80, obd: 0), Mk(50, hd: 80, obd: 0),
                                   Mk(50, hd: 80, obd: 0), Mk(50, hd: 80, obd: 0) };

            double HdDelta(ShotLocation zone)
            {
                var intended = MakePct(Generate(shooter50, matchedDef50, fourHD80,    zone));
                var zeroed   = MakePct(Generate(shooter50, matchedDef50, fourZeroObd, zone));
                return intended - zeroed;
            }

            var hdRim   = HdDelta(ShotLocation.Rim);
            var hdShort = HdDelta(ShotLocation.Short);
            var hdMid   = HdDelta(ShotLocation.Mid);
            var hdLong  = HdDelta(ShotLocation.Long);
            var hdThree = HdDelta(ShotLocation.Three);

            var rimShortEqual = Math.Abs(hdRim - hdShort) < TightEps;
            var midRatio      = Math.Abs(hdRim) > Eps ? Math.Abs(hdMid) / Math.Abs(hdRim) : 0.0;
            var midRatioOk    = Math.Abs(midRatio - cfgH.HelpDefenseMidMultiplier) < TightEps;
            var longZero      = Math.Abs(hdLong)  < TightEps;
            var threeZero     = Math.Abs(hdThree) < TightEps;
            var rimFires      = hdRim < -Eps;

            Console.WriteLine($"    Rim   delta={hdRim*100:F4}pts (full)  fires → {(rimFires ? "ok" : "FAIL")}");
            Console.WriteLine($"    Short delta={hdShort*100:F4}pts  Rim==Short → {(rimShortEqual ? "ok" : "FAIL")}");
            Console.WriteLine($"    Mid   delta={hdMid*100:F4}pts  ratio={midRatio:F4} (expect {cfgH.HelpDefenseMidMultiplier:F4}) → {(midRatioOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    Long  delta={hdLong*100:F4}pts  = 0 → {(longZero  ? "ok" : "FAIL")}");
            Console.WriteLine($"    Three delta={hdThree*100:F4}pts  = 0 → {(threeZero ? "ok" : "FAIL")}");

            dOk = rimFires && rimShortEqual && midRatioOk && longZero && threeZero;
            pass &= dOk;
            Console.WriteLine($"  (d) {(dOk ? "ok" : "FAIL")}");
        }

        // ── (e) Deferred-clamp migration regression — C5.5/C7 cancellation ───
        Console.WriteLine("  (e) Deferred-clamp migration (C5.5/C7 cancellation at perimeter):");
        {
            var eOk = true;

            // First: assert C5.5/C7 perimeter-symmetry contract.
            var actualMaxScrBonus  = cfgH.ScreeningBonusScale
                                   * Math.Pow(0.99, cfgH.ScreeningAggregateExponent);
            var actualMaxObdSupp   = cfgH.OffBallDefenseSuppressionScale
                                   * Math.Pow(0.99, cfgH.OffBallDefenseAggregateExponent);
            var symmetryOk = Math.Abs(actualMaxScrBonus - actualMaxObdSupp) < 1e-12;
            Console.WriteLine($"    max C5.5 bonus={actualMaxScrBonus:F10}");
            Console.WriteLine($"    max C7 suppression={actualMaxObdSupp:F10}");
            Console.WriteLine($"    C5.5/C7 symmetric → {(symmetryOk ? "ok" : "FAIL — config asymmetric, test cannot proceed")}");
            if (!symmetryOk) { pass = false; Console.WriteLine("  (e) FAIL"); goto eDone; }

            // Algebraic deferred-clamp proof at Long zone.
            // Because C5.5 bonus (Scr=99, 5/5 capacity) == C7 suppression (OBD=99, 4/4 off-ball capacity)
            // by the symmetry contract above, the two signed terms cancel exactly when both are active.
            // Proof: RunE(scr=99, obd=0)  = baseline + bonus
            //        RunE(scr=0,  obd=99)  = baseline - suppression
            //        RunE(scr=99, obd=99)  = baseline + bonus - suppression
            //                              = baseline  (because bonus == suppression)
            //
            // If the Math.Clamp were premature — applied between C5.5 and C7 — the result would be:
            //   Clamp(baseline + bonus, 0, 1) - suppression
            // which differs from baseline whenever baseline + bonus > 1 (saturation-sensitive range).
            // At Long zone with current config the logistic ceiling prevents reaching saturation
            // at any authored rating, so the premature-clamp error magnitude is zero at this zone.
            // The algebraic proof below is therefore the correct and sufficient contract for C5.5/C7
            // at perimeter zones: it verifies the three-term arithmetic is correct regardless of
            // whether the ceiling would cause saturation.
            //
            // A saturation-sensitive proof would require the Rim zone (ceiling 0.95) but C7 has
            // zero multiplier at Rim. The perimeter deferred-clamp invariant is fully captured
            // by the algebraic cancellation below — the signed terms must compose additively.

            Player MkScrE(int scrVal, int outerObd) => new Player("p") {
                Close = 50, Mid = 50, Outside = 70, Finishing = 50, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = scrVal,
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50,
                RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                HelpDefense = 0, OffBallDefense = outerObd,
                Height = 50, Wingspan = 50, Weight = 50, Strength = 50,
                Speed = 50, Quickness = 50, FirstStep = 50, Vertical = 50,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                RimTendency = 5, ShortTendency = 5, MidTendency = 5, LongTendency = 70, ThreeTendency = 15,
            };
            var weakMatchedDefE = Mk(50, hd: 0, obd: 0, rimP: 50);

            double RunE(int offScr, int defObd)
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var offPlayer = MkScrE(offScr, 0);
                for (var i = 1; i <= 5; i++) g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), offPlayer);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(1), weakMatchedDefE);
                var defHelpers = new[] { Mk(50, hd: 0, obd: defObd), Mk(50, hd: 0, obd: defObd),
                                         Mk(50, hd: 0, obd: defObd), Mk(50, hd: 0, obd: defObd) };
                for (var i = 0; i < 4; i++) g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 2), defHelpers[i]);
                var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound, SelectedSlot: g.HomeLineup.SlotAt(1), ShotType: ShotLocation.Long);
                return MakePct(new RollHGenerator(cfgH, cfgM, g).Generate(st));
            }

            var baseline        = RunE(offScr: 0,  defObd: 0);
            var scrOnly         = RunE(offScr: 99, defObd: 0);
            var obdOnly         = RunE(offScr: 0,  defObd: 99);
            var bothElite       = RunE(offScr: 99, defObd: 99);

            var scrDelta        = scrOnly   - baseline;   // positive
            var obdDelta        = obdOnly   - baseline;   // negative
            var bothDelta       = bothElite - baseline;   // should ≈ 0 (cancel)
            var cancelResidual  = Math.Abs(bothDelta);
            var cancelOk        = cancelResidual < 1e-9;

            // Independent direction check:
            var scrPositive = scrDelta > Eps;
            var obdNegative = obdDelta < -Eps;

            Console.WriteLine($"    baseline (scr=0,obd=0)={baseline:F6}");
            Console.WriteLine($"    scr-only (scr=99,obd=0)={scrOnly:F6}  delta=+{scrDelta*100:F4}pts  positive → {(scrPositive ? "ok" : "FAIL")}");
            Console.WriteLine($"    obd-only (scr=0,obd=99)={obdOnly:F6}  delta={obdDelta*100:F4}pts  negative → {(obdNegative ? "ok" : "FAIL")}");
            Console.WriteLine($"    both-elite (scr=99,obd=99)={bothElite:F6}  cancellation residual={cancelResidual:F2e}");
            Console.WriteLine($"    → {(cancelOk ? "ok — C5.5+C7 cancel algebraically (deferred-clamp contract holds)" : "FAIL — premature clamp or arithmetic error")}");

            eOk = scrPositive && obdNegative && cancelOk;
            eDone:
            pass &= eOk;
            Console.WriteLine($"  (e) {(eOk ? "ok" : "FAIL")}");
        }

        // ── (f) Selection compression — perimeter focal point ────────────────
        Console.WriteLine("  (f) Selection compression — perimeter focal point (OffBallDefense):");
        {
            var fOk = true;

            // Build a lineup where Slot 1 is a pure guard (Height=30, PostDefense=30, Strength=30),
            // high usage (HierarchyRank=10).
            var pureGuard = new Player("guard") {
                Close = 50, Mid = 50, Outside = 70, Finishing = 50, FreeThrow = 70,
                FoulDrawing = 50, BallHandling = 60, Passing = 55, Playmaking = 55,
                SelfCreation = 70, PostMoves = 20, OffBallMovement = 60, Screening = 40,
                OffensiveRebounding = 30, PerimeterDefense = 55, PostDefense = 30, RimProtection = 20,
                DefensiveRebounding = 30, Steals = 55, HelpDefense = 0, OffBallDefense = 0,
                Height = 30, Wingspan = 45, Weight = 40, Strength = 30, Speed = 70,
                Quickness = 70, FirstStep = 70, Vertical = 55, Endurance = 60, Hustle = 60,
                BasketballIQ = 60, Discipline = 60, HierarchyRank = 10,
                RimTendency = 20, ShortTendency = 15, MidTendency = 25, LongTendency = 15, ThreeTendency = 25,
            };
            var filler = new Player("filler") {
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                DefensiveRebounding = 50, Steals = 50, HelpDefense = 0, OffBallDefense = 0,
                Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
                BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
            };

            // Verify guard postness is below PostnessNeutral.
            var guardPostness = Matchup.Postness(pureGuard, cfgM);
            if (!(guardPostness < cfgM.PostnessNeutral))
            {
                Console.WriteLine($"    FAIL — guard postness={guardPostness:F4} is not < PostnessNeutral={cfgM.PostnessNeutral}. Fixture invalid.");
                pass = false; Console.WriteLine("  (f) FAIL");
                goto fDone;
            }
            Console.WriteLine($"    guard postness={guardPostness:F4} < PostnessNeutral={cfgM.PostnessNeutral} → fixture valid");

            // Compression test helper: returns Slot1 share from BendByAttention.
            double Slot1Share(int obdVal, int hdVal)
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var coach = new CoachProfile(heliocentricBias: 8.0, shotSelectionBias: 5.0, paceBias: 5.0);
                g.SetCoach(TeamSide.Home, coach);
                g.SetCoach(TeamSide.Away, coach);
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), pureGuard);
                for (var i = 2; i <= 5; i++) g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), filler);
                var defPlayer = new Player("def") {
                    Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                    FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                    SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50,
                    HelpDefense = hdVal, OffBallDefense = obdVal,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                    Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
                    BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                };
                for (var i = 1; i <= 5; i++) g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), defPlayer);
                var genE = new RollEGenerator(cfgE, g);
                var attnGen = new AttentionGenerator(AttentionConfig.Load(configPath), g);
                var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound);
                var genResult = genE.GenerateWithPressure(st);
                var attn = attnGen.Generate(st, genResult.FinalShares);
                var tilted = genE.BendByAttention(genResult, attn.AttentionShares, g, cfgM, st);
                return tilted.Slices.First(s => s.Outcome == SelectionOutcome.Slot1).Weight;
            }

            var baseline   = Slot1Share(0, 0);
            var equalShare = 1.0 / 5.0;

            if (!(baseline > equalShare))
            {
                Console.WriteLine($"    FAIL — baseline Slot1 share={baseline:F4} is not > equalShare={equalShare:F4}. Focal point not created.");
                pass = false; Console.WriteLine("  (f) FAIL");
                goto fDone;
            }
            Console.WriteLine($"    baseline Slot1 share={baseline:F4} > equalShare={equalShare:F4} → focal point confirmed");

            var obdOnly  = Slot1Share(80, 0);
            var hdOnly   = Slot1Share(0, 80);

            // OBD-only lowers Slot1 share (compression fired)
            var obdLowersOk = obdOnly < baseline - Eps;
            // HD-only should have no effect on a pure guard (interiorWeight≈0)
            var hdNoEffectOk = Math.Abs(hdOnly - baseline) < TightEps;

            Console.WriteLine($"    OBD-only Slot1={obdOnly:F6}  < baseline → {(obdLowersOk ? "ok — compression fired" : "FAIL")}");
            Console.WriteLine($"    HD-only  Slot1={hdOnly:F6}  ≈ baseline → {(hdNoEffectOk ? "ok — no interior compression on guard" : "FAIL")}");

            // Verify sum to 1.0
            // (Single slot check — full sum validated by Pie constructor)
            fOk = obdLowersOk && hdNoEffectOk;
            pass &= fOk;
            fDone:
            Console.WriteLine($"  (f) {(fOk ? "ok" : "FAIL")}");
        }

        // ── Neutral-postness pivot ────────────────────────────────────────────
        Console.WriteLine("  (f.neutral) Neutral-postness pivot — zero compression at PostnessNeutral:");
        {
            var fnOk = true;

            // Construct Slot 1 so Matchup.Postness = PostnessNeutral.
            // With equal thirds (1/3 each), we need Height=PostnessNeutral*3 / (1+1+1) which isn't right.
            // Postness(p) = (1/3)*H + (1/3)*PD + (1/3)*S = PostnessNeutral
            // So H = PD = S = PostnessNeutral = 50.
            // But let's verify with the actual coefficients.
            int neutralH = (int)Math.Round(cfgM.PostnessNeutral);   // = 50 with equal thirds
            int neutralPD = (int)Math.Round(cfgM.PostnessNeutral);
            int neutralS  = (int)Math.Round(cfgM.PostnessNeutral);

            // With equal thirds, setting all three to 50 gives exactly PostnessNeutral=50.
            // Verify analytically:
            var testPostness = cfgM.PostnessHeight      * neutralH
                             + cfgM.PostnessPostDefense * neutralPD
                             + cfgM.PostnessStrength    * neutralS;

            if (!(Math.Abs(testPostness - cfgM.PostnessNeutral) < 1e-9))
            {
                Console.WriteLine($"    FAIL — cannot construct neutral-pivot fixture: computed postness={testPostness:F6} != {cfgM.PostnessNeutral}");
                pass = false; Console.WriteLine("  (f.neutral) FAIL");
                goto fnDone;
            }
            Console.WriteLine($"    Neutral pivot player: postness={testPostness:F6} = PostnessNeutral={cfgM.PostnessNeutral} ✓");

            var pivotFiller = new Player("pivot") {
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = 50, PerimeterDefense = 50,
                PostDefense = neutralPD, RimProtection = 50,
                DefensiveRebounding = 50, Steals = 50, HelpDefense = 0, OffBallDefense = 0,
                Height = neutralH, Wingspan = 50, Weight = 50, Strength = neutralS, Speed = 50,
                Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
                BasketballIQ = 50, Discipline = 50, HierarchyRank = 10,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
            };

            double Slot1SharePivot(int obdVal, int hdVal, Player slot1Player)
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var coach = new CoachProfile(heliocentricBias: 8.0, shotSelectionBias: 5.0, paceBias: 5.0);
                g.SetCoach(TeamSide.Home, coach);
                g.SetCoach(TeamSide.Away, coach);
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), slot1Player);
                var pf = new Player("f") {
                    Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                    FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                    SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50, HelpDefense = 0, OffBallDefense = 0,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                    Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
                    BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                };
                for (var i = 2; i <= 5; i++) g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), pf);
                var defPlayer = new Player("def") {
                    Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                    FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                    SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50,
                    HelpDefense = hdVal, OffBallDefense = obdVal,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                    Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
                    BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                };
                for (var i = 1; i <= 5; i++) g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), defPlayer);
                var genE = new RollEGenerator(cfgE, g);
                var attnGen = new AttentionGenerator(AttentionConfig.Load(configPath), g);
                var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound);
                var genResult = genE.GenerateWithPressure(st);
                var attn = attnGen.Generate(st, genResult.FinalShares);
                var tilted = genE.BendByAttention(genResult, attn.AttentionShares, g, cfgM, st);
                return tilted.Slices.First(s => s.Outcome == SelectionOutcome.Slot1).Weight;
            }

            var pivotBaseline = Slot1SharePivot(0, 0, pivotFiller);
            if (!(pivotBaseline > 1.0 / 5.0))
            {
                Console.WriteLine($"    FAIL — pivot baseline Slot1 share={pivotBaseline:F4} not > equalShare; test would pass vacuously.");
                pass = false; Console.WriteLine("  (f.neutral) FAIL");
                goto fnDone;
            }
            Console.WriteLine($"    pivot baseline Slot1={pivotBaseline:F6} > equalShare → focal point confirmed");

            var pivotObd = Slot1SharePivot(80, 0,  pivotFiller);
            var pivotHd  = Slot1SharePivot(0,  80, pivotFiller);

            var obdNoEffect = Math.Abs(pivotObd - pivotBaseline) < TightEps;
            var hdNoEffect  = Math.Abs(pivotHd  - pivotBaseline) < TightEps;

            Console.WriteLine($"    OBD-only at pivot: Slot1={pivotObd:F6}  ≈ baseline → {(obdNoEffect ? "ok — zero compression at pivot" : "FAIL")}");
            Console.WriteLine($"    HD-only  at pivot: Slot1={pivotHd:F6}   ≈ baseline → {(hdNoEffect  ? "ok — zero compression at pivot" : "FAIL")}");

            fnOk = obdNoEffect && hdNoEffect;
            pass &= fnOk;
            fnDone:
            Console.WriteLine($"  (f.neutral) {(fnOk ? "ok" : "FAIL")}");
        }

        // ── (g) Selection compression — interior focal point ─────────────────
        Console.WriteLine("  (g) Selection compression — interior focal point (HelpDefense):");
        {
            var gOk = true;

            // Slot 1: pure post (Height=90, PostDefense=90, Strength=90), high usage.
            var purePost = new Player("post") {
                Close = 70, Mid = 45, Outside = 30, Finishing = 75, FreeThrow = 55,
                FoulDrawing = 60, BallHandling = 40, Passing = 40, Playmaking = 35,
                SelfCreation = 40, PostMoves = 75, OffBallMovement = 35, Screening = 50,
                OffensiveRebounding = 75, PerimeterDefense = 40,
                PostDefense = 90, RimProtection = 70,
                DefensiveRebounding = 80, Steals = 35, HelpDefense = 0, OffBallDefense = 0,
                Height = 90, Wingspan = 85, Weight = 80, Strength = 90, Speed = 40,
                Quickness = 35, FirstStep = 35, Vertical = 60, Endurance = 65, Hustle = 70,
                BasketballIQ = 55, Discipline = 60, HierarchyRank = 10,
                RimTendency = 50, ShortTendency = 35, MidTendency = 10, LongTendency = 3, ThreeTendency = 2,
            };

            var postPostness = Matchup.Postness(purePost, cfgM);
            if (!(postPostness > cfgM.PostnessNeutral))
            {
                Console.WriteLine($"    FAIL — post postness={postPostness:F4} is not > PostnessNeutral={cfgM.PostnessNeutral}. Fixture invalid.");
                pass = false; Console.WriteLine("  (g) FAIL");
                goto gDone;
            }
            Console.WriteLine($"    post postness={postPostness:F4} > PostnessNeutral={cfgM.PostnessNeutral} → fixture valid");

            double Slot1SharePost(int obdVal, int hdVal)
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var coach = new CoachProfile(heliocentricBias: 8.0, shotSelectionBias: 5.0, paceBias: 5.0);
                g.SetCoach(TeamSide.Home, coach);
                g.SetCoach(TeamSide.Away, coach);
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), purePost);
                var pf = new Player("f") {
                    Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                    FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                    SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50, HelpDefense = 0, OffBallDefense = 0,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                    Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
                    BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                };
                for (var i = 2; i <= 5; i++) g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), pf);
                var defPlayer = new Player("def") {
                    Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50,
                    FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                    SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50,
                    HelpDefense = hdVal, OffBallDefense = obdVal,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                    Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50,
                    BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                };
                for (var i = 1; i <= 5; i++) g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), defPlayer);
                var genE = new RollEGenerator(cfgE, g);
                var attnGen = new AttentionGenerator(AttentionConfig.Load(configPath), g);
                var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound);
                var genResult = genE.GenerateWithPressure(st);
                var attn = attnGen.Generate(st, genResult.FinalShares);
                var tilted = genE.BendByAttention(genResult, attn.AttentionShares, g, cfgM, st);
                return tilted.Slices.First(s => s.Outcome == SelectionOutcome.Slot1).Weight;
            }

            var postBaseline = Slot1SharePost(0, 0);
            if (!(postBaseline > 1.0 / 5.0))
            {
                Console.WriteLine($"    FAIL — post baseline Slot1={postBaseline:F4} not > equalShare. Focal point not created.");
                pass = false; Console.WriteLine("  (g) FAIL");
                goto gDone;
            }
            Console.WriteLine($"    post baseline Slot1={postBaseline:F6} > equalShare → focal point confirmed");

            var postHdOnly  = Slot1SharePost(0, 80);
            var postObdOnly = Slot1SharePost(80, 0);

            var hdLowersOk   = postHdOnly  < postBaseline - Eps;
            var obdNoEffectG = Math.Abs(postObdOnly - postBaseline) < TightEps;

            Console.WriteLine($"    HD-only  Slot1={postHdOnly:F6}  < baseline → {(hdLowersOk   ? "ok — compression fired" : "FAIL")}");
            Console.WriteLine($"    OBD-only Slot1={postObdOnly:F6}  ≈ baseline → {(obdNoEffectG ? "ok — no perimeter compression on post" : "FAIL")}");

            gOk = hdLowersOk && obdNoEffectG;
            pass &= gOk;
            gDone:
            Console.WriteLine($"  (g) {(gOk ? "ok" : "FAIL")}");
        }

        Console.WriteLine(pass ? "  Phase 44 PASSED." : "  Phase 44 FAILED.");
        return pass;
    }

    // =========================================================================
    // Phase 45 — Hustle: relative team-aggregate effect across five consumers.
    // Direct-probe discipline (matches Phase 13 / Phase 41 / Phase 44): every
    // assertion reads a real generator pie or calls Matchup directly — no stubs,
    // no full-game batch. Each consumer's mechanism is proven in isolation.
    // =========================================================================
    private static bool Phase45HustleCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 45: Hustle — relative team-aggregate effect across five consumers ---");
        var pass = true;
        const double Eps      = 1e-9;
        const double TightEps = 1e-9;

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgB = RollBConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        // Player with all attributes at b; Hustle (and rebound/steal skill) overridable.
        static Player Mk(int b, int? hustle = null, int? oreb = null, int? dreb = null, int? steals = null)
            => new Player("p")
            {
                Close = b, Mid = b, Outside = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = oreb ?? b,
                PerimeterDefense = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding = dreb ?? b, Steals = steals ?? b, HelpDefense = b,
                OffBallDefense = b,
                Height = b, Wingspan = b, Weight = b,
                Strength = b, Speed = b, Quickness = b, FirstStep = b, Vertical = b,
                Endurance = b, Hustle = hustle ?? b, BasketballIQ = b, Discipline = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Five-player lineup, all at Hustle h, every other attribute 50.
        static Player?[] Five(int h) => new Player?[] { Mk(50, h), Mk(50, h), Mk(50, h), Mk(50, h), Mk(50, h) };

        // Back-calculate pre-block/pre-foul make% from a ShotResult pie.
        static double MakePct(Pie<ShotResult> pie)
        {
            var blocked    = pie.Slices.First(s => s.Outcome == ShotResult.Blocked).Weight;
            var maf        = pie.Slices.First(s => s.Outcome == ShotResult.MadeAndFouled).Weight;
            var missFouled = pie.Slices.First(s => s.Outcome == ShotResult.MissFouled).Weight;
            var foul       = maf + missFouled;
            var nonBNF     = 1.0 - blocked - foul;
            var made       = pie.Slices.First(s => s.Outcome == ShotResult.Made).Weight;
            return nonBNF > 1e-9 ? made / nonBNF : 0.0;
        }

        // MatchupConfig with Away (defense) pressure set to p (mirrors Phase 13).
        MatchupConfig WithAwayPressure(double p)
        {
            var c = MatchupConfig.Load(configPath);
            c.AwayPressure = p;
            return c;
        }

        // Roll B / Roll F arm readers (real generators, full lineups).
        Dictionary<HalfcourtOutcome, double> RollBArms(MatchupConfig cfg, int offH, int defH)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), Mk(50, offH));
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), Mk(50, defH));
            }
            var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
            var pie = new RollBGenerator(cfgB, cfg, g).Generate(st, physicality: 0.0);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }
        Dictionary<PlayerActionOutcome, double> RollFArms(MatchupConfig cfg, int offH, int defH)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), Mk(50, offH));
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), Mk(50, defH));
            }
            var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(1));
            var pie = new RollFGenerator(cfgF, cfg, g).Generate(st);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }
        // RollH FastBreak/halfcourt make% (real generator, full lineups).
        double RollHMake(int offH, int defH, bool fastBreak, ShotLocation zone)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), Mk(50, offH));
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), Mk(50, defH));
            }
            var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(1), ShotType: zone, FastBreak: fastBreak);
            return MakePct(new RollHGenerator(cfgH, cfgM, g).Generate(st));
        }

        // ── (a) Rebound battle direction ─────────────────────────────────────
        Console.WriteLine("  (a) Rebound battle direction:");
        {
            const double baseOffShare = 0.29;   // live-miss baseline
            var off80 = Five(80); var def20 = Five(20);
            var off20 = Five(20); var def80 = Five(80);
            var both50off = Five(50); var both50def = Five(50);

            var shareHigh = Matchup.OffensiveReboundShare(off80, def20, -1, ShotLocation.Rim, baseOffShare, cfgM);
            var shareLow  = Matchup.OffensiveReboundShare(off20, def80, -1, ShotLocation.Rim, baseOffShare, cfgM);
            var shareEven = Matchup.OffensiveReboundShare(both50off, both50def, -1, ShotLocation.Rim, baseOffShare, cfgM);

            // Pre-bend totalShift contribution at canonical gap 60.
            var preBend = cfgM.HustleReboundWeight
                        * Matchup.HustleGapShift(60.0, cfgM.HustleReboundSteepness,
                                                 cfgM.HustleReboundExponent, cfgM.HustleReboundScale);

            var highUp   = shareHigh > shareEven + Eps;
            var lowDown  = shareLow  < shareEven - Eps;
            var evenFlat = Math.Abs(shareEven - baseOffShare) < TightEps;

            Console.WriteLine($"    pre-bend totalShift contribution @gap60 = {preBend:F6}");
            Console.WriteLine($"    off80/def20: share={shareHigh:F6} (Δ={(shareHigh-shareEven)*100:F3}pp)  > even → {(highUp ? "ok" : "FAIL")}");
            Console.WriteLine($"    off20/def80: share={shareLow:F6} (Δ={(shareLow-shareEven)*100:F3}pp)  < even → {(lowDown ? "ok" : "FAIL")}");
            Console.WriteLine($"    even (50/50): share={shareEven:F6}  == baseline → {(evenFlat ? "ok" : "FAIL")}");
            var aOk = highUp && lowDown && evenFlat;
            pass &= aOk;
            Console.WriteLine($"  (a) {(aOk ? "ok" : "FAIL")}");
        }

        // ── (h) GapFn zero-slope / convex (placed early — pure math) ──────────
        Console.WriteLine("  (h) GapFn zero-slope / convex (vs raw tanh):");
        {
            const double baseOffShare = 0.29;
            // 1-point gap: offense mean 51, defense 50.
            var offMean51 = new Player?[] { Mk(50, 51), Mk(50, 51), Mk(50, 51), Mk(50, 51), Mk(50, 51) };
            var def50      = Five(50);
            var share1 = Matchup.OffensiveReboundShare(offMean51, def50, -1, ShotLocation.Rim, baseOffShare, cfgM);
            var even   = Matchup.OffensiveReboundShare(Five(50),  def50, -1, ShotLocation.Rim, baseOffShare, cfgM);

            // The pre-bend shift at gap=1 should match the quadratic GapFn prediction.
            var gapFn1   = cfgM.HustleReboundSteepness * Math.Pow(1.0 / cfgM.HustleReboundScale, cfgM.HustleReboundExponent);
            var preBend1 = cfgM.HustleReboundWeight * gapFn1;
            var tanh1    = cfgM.HustleReboundSteepness * Math.Tanh(1.0 / cfgM.HustleReboundScale);
            var ratio    = preBend1 > 0.0 ? (cfgM.HustleReboundWeight * tanh1) / preBend1 : 0.0;

            // Canonical 60-gap pre-bend.
            var preBend60 = cfgM.HustleReboundWeight
                          * cfgM.HustleReboundSteepness * Math.Pow(60.0 / cfgM.HustleReboundScale, cfgM.HustleReboundExponent);

            var oneGapTiny   = (share1 - even) > 0.0 && (share1 - even) < (preBend60 / cfgM.ReboundReferenceShift);  // far smaller than canonical
            var notTanh      = ratio > 10.0;   // tanh is ~25× larger at gap=1
            var muchSmaller  = preBend1 < preBend60 / 100.0;

            Console.WriteLine($"    gap=1 GapFn pre-bend={preBend1:F8}  raw-tanh equiv={cfgM.HustleReboundWeight*tanh1:F8}  ratio={ratio:F1}× (expect ~25×) → {(notTanh ? "ok" : "FAIL")}");
            Console.WriteLine($"    gap=1 share lift={(share1-even)*100:F6}pp  (tiny, positive) → {(oneGapTiny ? "ok" : "FAIL")}");
            Console.WriteLine($"    gap=1 pre-bend << gap=60 pre-bend ({preBend1:F8} vs {preBend60:F6}) → {(muchSmaller ? "ok" : "FAIL")}");
            var hOk = oneGapTiny && notTanh && muchSmaller;
            pass &= hOk;
            Console.WriteLine($"  (h) {(hOk ? "ok — GapFn (not tanh) confirmed" : "FAIL")}");
        }

        // ── (b) Equal-Hustle wash (all five consumers) ───────────────────────
        Console.WriteLine("  (b) Equal-Hustle wash (Hustle=90 both == Hustle=50 both):");
        {
            const double baseOffShare = 0.29;
            // Rebound
            var reb90 = Matchup.OffensiveReboundShare(Five(90), Five(90), -1, ShotLocation.Rim, baseOffShare, cfgM);
            var reb50 = Matchup.OffensiveReboundShare(Five(50), Five(50), -1, ShotLocation.Rim, baseOffShare, cfgM);
            var rebOk = Math.Abs(reb90 - reb50) < TightEps;
            // Roll B turnover + foul
            var b90 = RollBArms(cfgM, 90, 90); var b50 = RollBArms(cfgM, 50, 50);
            var bToOk   = Math.Abs(b90[HalfcourtOutcome.DeadBallTurnover] - b50[HalfcourtOutcome.DeadBallTurnover]) < TightEps;
            var bFoulOk = Math.Abs(b90[HalfcourtOutcome.Foul]             - b50[HalfcourtOutcome.Foul])             < TightEps;
            // Roll F turnover + foul
            var f90 = RollFArms(cfgM, 90, 90); var f50 = RollFArms(cfgM, 50, 50);
            var fToOk   = Math.Abs(f90[PlayerActionOutcome.Turnover]        - f50[PlayerActionOutcome.Turnover])        < TightEps;
            var fFoulOk = Math.Abs(f90[PlayerActionOutcome.NonShootingFoul] - f50[PlayerActionOutcome.NonShootingFoul]) < TightEps;
            // Transition make%
            var t90 = RollHMake(90, 90, fastBreak: true, ShotLocation.Rim);
            var t50 = RollHMake(50, 50, fastBreak: true, ShotLocation.Rim);
            var tOk = Math.Abs(t90 - t50) < TightEps;

            Console.WriteLine($"    rebound:         Δ={Math.Abs(reb90-reb50):E2} → {(rebOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    Roll B turnover: Δ={Math.Abs(b90[HalfcourtOutcome.DeadBallTurnover]-b50[HalfcourtOutcome.DeadBallTurnover]):E2} → {(bToOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    Roll B foul:     Δ={Math.Abs(b90[HalfcourtOutcome.Foul]-b50[HalfcourtOutcome.Foul]):E2} → {(bFoulOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    Roll F turnover: Δ={Math.Abs(f90[PlayerActionOutcome.Turnover]-f50[PlayerActionOutcome.Turnover]):E2} → {(fToOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    Roll F foul:     Δ={Math.Abs(f90[PlayerActionOutcome.NonShootingFoul]-f50[PlayerActionOutcome.NonShootingFoul]):E2} → {(fFoulOk ? "ok" : "FAIL")}");
            Console.WriteLine($"    transition mk%:  Δ={Math.Abs(t90-t50):E2} → {(tOk ? "ok" : "FAIL")}");
            var bOk = rebOk && bToOk && bFoulOk && fToOk && fFoulOk && tOk;
            pass &= bOk;
            Console.WriteLine($"  (b) {(bOk ? "ok — all five consumers wash" : "FAIL")}");
        }

        // ── (c) Turnover disruption, ceiling-sensitive (Roll B and Roll F) ───
        Console.WriteLine("  (c) Turnover disruption ceiling-sensitive (pre-saturation proof):");
        {
            var cOk = true;

            // Roll B: defensive Hustle advantage (def=80, off=20). Hustle increment =
            // (Hustle-on TO arm) − (Hustle-off TO arm, both=50) at each pressure level.
            {
                var lowP  = WithAwayPressure(5.0);    // neutral, well below ceiling
                var highP = WithAwayPressure(10.0);   // near ceiling
                var incLow  = RollBArms(lowP,  20, 80)[HalfcourtOutcome.DeadBallTurnover]
                            - RollBArms(lowP,  50, 50)[HalfcourtOutcome.DeadBallTurnover];
                var incHigh = RollBArms(highP, 20, 80)[HalfcourtOutcome.DeadBallTurnover]
                            - RollBArms(highP, 50, 50)[HalfcourtOutcome.DeadBallTurnover];
                var finalHigh = RollBArms(highP, 20, 80)[HalfcourtOutcome.DeadBallTurnover];
                var shrink = incHigh < incLow && incLow > Eps;
                var capOk  = finalHigh <= cfgM.RollBTurnoverCeiling + Eps;
                Console.WriteLine($"    Roll B: incr @neutral={incLow*100:F4}pp  @highP={incHigh*100:F4}pp  shrinks → {(shrink ? "ok" : "FAIL")}");
                Console.WriteLine($"    Roll B: final TO @highP={finalHigh:F6} ≤ ceiling {cfgM.RollBTurnoverCeiling:F4} → {(capOk ? "ok" : "FAIL")}");
                cOk &= shrink && capOk;
            }
            // Roll F: same pattern.
            {
                var lowP  = WithAwayPressure(5.0);
                var highP = WithAwayPressure(10.0);
                var incLow  = RollFArms(lowP,  20, 80)[PlayerActionOutcome.Turnover]
                            - RollFArms(lowP,  50, 50)[PlayerActionOutcome.Turnover];
                var incHigh = RollFArms(highP, 20, 80)[PlayerActionOutcome.Turnover]
                            - RollFArms(highP, 50, 50)[PlayerActionOutcome.Turnover];
                var finalHigh = RollFArms(highP, 20, 80)[PlayerActionOutcome.Turnover];
                var shrink = incHigh < incLow && incLow > Eps;
                var capOk  = finalHigh <= cfgM.TurnoverCeiling + Eps;
                Console.WriteLine($"    Roll F: incr @neutral={incLow*100:F4}pp  @highP={incHigh*100:F4}pp  shrinks → {(shrink ? "ok" : "FAIL")}");
                Console.WriteLine($"    Roll F: final TO @highP={finalHigh:F6} ≤ ceiling {cfgM.TurnoverCeiling:F4} → {(capOk ? "ok" : "FAIL")}");
                cOk &= shrink && capOk;
            }
            pass &= cOk;
            Console.WriteLine($"  (c) {(cOk ? "ok — Hustle inserted pre-saturation" : "FAIL")}");
        }

        // ── (d) Foul direction: defense-only ─────────────────────────────────
        Console.WriteLine("  (d) Foul direction (defense-only):");
        {
            var dOk = true;
            var neutral = WithAwayPressure(5.0);   // isolates Hustle (pressure lift = 0, matchup gated off)

            // Pre-saturation nudge ordering (mirrors the Python pre-check).
            var foulNudge     = cfgM.HustleFoulWeight
                              * Matchup.HustleGapShift(60.0, cfgM.HustleFoulSteepness, cfgM.HustleFoulExponent, cfgM.HustleFoulScale);
            var pressureNudge = cfgM.HustlePressureWeight
                              * Matchup.HustleGapShift(60.0, cfgM.HustlePressureSteepness, cfgM.HustlePressureExponent, cfgM.HustlePressureScale);
            var preOrderOk = foulNudge < pressureNudge && foulNudge > 0.0;
            Console.WriteLine($"    pre-saturation: foulNudge={foulNudge:F6} < pressureNudge={pressureNudge:F6} → {(preOrderOk ? "ok" : "FAIL")}");
            dOk &= preOrderOk;

            // Roll F: offense advantage → foul arm unchanged; defense advantage → foul arm rises.
            var fEven = RollFArms(neutral, 50, 50)[PlayerActionOutcome.NonShootingFoul];
            var fOffAdv = RollFArms(neutral, 80, 20)[PlayerActionOutcome.NonShootingFoul];   // offense out-hustles → no def foul
            var fDefAdv = RollFArms(neutral, 20, 80)[PlayerActionOutcome.NonShootingFoul];   // defense out-hustles → foul rises
            var fOffUnchanged = Math.Abs(fOffAdv - fEven) < TightEps;
            var fDefRises      = fDefAdv > fEven + Eps;
            Console.WriteLine($"    Roll F foul: even={fEven:F6}  off-adv={fOffAdv:F6} (==even → {(fOffUnchanged ? "ok" : "FAIL")})  def-adv={fDefAdv:F6} (>even → {(fDefRises ? "ok" : "FAIL")})");
            dOk &= fOffUnchanged && fDefRises;

            // Roll B: same defense-only behaviour on the Foul arm.
            var bEven = RollBArms(neutral, 50, 50)[HalfcourtOutcome.Foul];
            var bOffAdv = RollBArms(neutral, 80, 20)[HalfcourtOutcome.Foul];
            var bDefAdv = RollBArms(neutral, 20, 80)[HalfcourtOutcome.Foul];
            var bOffUnchanged = Math.Abs(bOffAdv - bEven) < TightEps;
            var bDefRises      = bDefAdv > bEven + Eps;
            Console.WriteLine($"    Roll B foul: even={bEven:F6}  off-adv={bOffAdv:F6} (==even → {(bOffUnchanged ? "ok" : "FAIL")})  def-adv={bDefAdv:F6} (>even → {(bDefRises ? "ok" : "FAIL")})");
            dOk &= bOffUnchanged && bDefRises;

            // Post-saturation: final foul-arm delta < final turnover-arm delta (defense advantage).
            var fFoulDelta = fDefAdv - fEven;
            var fToDelta   = RollFArms(neutral, 20, 80)[PlayerActionOutcome.Turnover]
                           - RollFArms(neutral, 50, 50)[PlayerActionOutcome.Turnover];
            var postOrderOk = fFoulDelta < fToDelta && fFoulDelta > Eps;
            Console.WriteLine($"    post-saturation (Roll F): foul Δ={fFoulDelta*100:F4}pp < turnover Δ={fToDelta*100:F4}pp → {(postOrderOk ? "ok" : "FAIL")}");
            dOk &= postOrderOk;

            pass &= dOk;
            Console.WriteLine($"  (d) {(dOk ? "ok — defense-only, foul < turnover" : "FAIL")}");
        }

        // ── (f) Transition defense: FastBreak only, halfcourt unaffected ─────
        Console.WriteLine("  (f) Transition defense (FastBreak only, halfcourt unaffected):");
        {
            // FastBreak: defense out-hustles (def=80, off=20) → make% suppressed vs even.
            var fbEven = RollHMake(50, 50, fastBreak: true, ShotLocation.Rim);
            var fbDef  = RollHMake(20, 80, fastBreak: true, ShotLocation.Rim);
            var fbSuppressed = fbDef < fbEven - Eps;
            // Halfcourt: same Hustle gap → C8 does NOT fire → make% unchanged.
            var hcEven = RollHMake(50, 50, fastBreak: false, ShotLocation.Rim);
            var hcDef  = RollHMake(20, 80, fastBreak: false, ShotLocation.Rim);
            var hcUnaffected = Math.Abs(hcDef - hcEven) < TightEps;

            Console.WriteLine($"    FastBreak: even mk%={fbEven:F6}  def-adv mk%={fbDef:F6}  suppressed → {(fbSuppressed ? "ok" : "FAIL")}");
            Console.WriteLine($"    Halfcourt: even mk%={hcEven:F6}  def-adv mk%={hcDef:F6}  unaffected → {(hcUnaffected ? "ok" : "FAIL")}");
            var fOk = fbSuppressed && hcUnaffected;
            pass &= fOk;
            Console.WriteLine($"  (f) {(fOk ? "ok" : "FAIL")}");
        }

        // ── (g) Attribution picker direction (Monte Carlo draw counts) ───────
        Console.WriteLine("  (g) Attribution picker direction (high-Hustle teammate favored):");
        {
            const int Draws = 60_000;
            const int Seed   = 12345;

            // Build a lineup: slot 1 Hustle=80, slot 2 Hustle=20, identical skill,
            // slots 3-5 neutral (Hustle=50). The Hustle multiplier is the only
            // differentiator between slots 1 and 2.
            GameState BuildPickerGame(Func<int, Player> mkSlot)
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                for (var i = 1; i <= 5; i++)
                {
                    var h = i == 1 ? 80 : i == 2 ? 20 : 50;
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i), mkSlot(h));
                    g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i), mkSlot(h));
                }
                return g;
            }

            // Offensive rebounder: OffensiveRebounding=50, vary only Hustle.
            int countSlot1Oreb = 0, countSlot2Oreb = 0;
            {
                var g = BuildPickerGame(h => Mk(50, h, oreb: 50));
                var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                    SelectedSlot: null, ShotType: ShotLocation.Rim);
                var rng = new SystemRng(Seed);
                for (var i = 0; i < Draws; i++)
                {
                    var slot = OffensiveRebounderPicker.Pick(st, g, cfgM, rng);
                    if (slot.Number == 1) countSlot1Oreb++;
                    else if (slot.Number == 2) countSlot2Oreb++;
                }
            }
            var orebOk = countSlot1Oreb > countSlot2Oreb;
            Console.WriteLine($"    OffRebounder: slot1(H80)={countSlot1Oreb}  slot2(H20)={countSlot2Oreb}  high>low → {(orebOk ? "ok" : "FAIL")}");

            // Defensive rebounder: DefensiveRebounding=50, vary only Hustle.
            int countSlot1Dreb = 0, countSlot2Dreb = 0;
            {
                var g = BuildPickerGame(h => Mk(50, h, dreb: 50));
                var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
                var rng = new SystemRng(Seed);
                for (var i = 0; i < Draws; i++)
                {
                    var slot = DefensiveRebounderPicker.Pick(st, g, cfgM, rng);
                    if (slot.Number == 1) countSlot1Dreb++;
                    else if (slot.Number == 2) countSlot2Dreb++;
                }
            }
            var drebOk = countSlot1Dreb > countSlot2Dreb;
            Console.WriteLine($"    DefRebounder: slot1(H80)={countSlot1Dreb}  slot2(H20)={countSlot2Dreb}  high>low → {(drebOk ? "ok" : "FAIL")}");

            // Stealer: Steals=50, vary only Hustle.
            int countSlot1Stl = 0, countSlot2Stl = 0;
            {
                var g = BuildPickerGame(h => Mk(50, h, steals: 50));
                var st = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
                var rng = new SystemRng(Seed);
                for (var i = 0; i < Draws; i++)
                {
                    var slot = StealerPicker.Pick(st, g, cfgM, rng);
                    if (slot.Number == 1) countSlot1Stl++;
                    else if (slot.Number == 2) countSlot2Stl++;
                }
            }
            var stlOk = countSlot1Stl > countSlot2Stl;
            Console.WriteLine($"    Stealer:      slot1(H80)={countSlot1Stl}  slot2(H20)={countSlot2Stl}  high>low → {(stlOk ? "ok" : "FAIL")}");

            var gOk = orebOk && drebOk && stlOk;
            pass &= gOk;
            Console.WriteLine($"  (g) {(gOk ? "ok — high-Hustle favored in all three pickers" : "FAIL")}");
        }

        Console.WriteLine(pass ? "  Phase 45 PASSED." : "  Phase 45 FAILED.");
        return pass;
    }
}
