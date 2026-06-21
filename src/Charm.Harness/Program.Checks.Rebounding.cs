using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{

    // --- Batch: Roll I's four-way rebound distribution converges within tolerance,
    //     and every exit is a clean terminal-or-continue of the expected kind, with
    //     slot+zone+result riding through the stub landings. Driven through a REAL
    // --- Batch: Roll I's seven-way LIVE-MISS rebound distribution converges within
    //     tolerance, and every arm routes as designed, with slot+zone+result riding
    //     through the fact-carrying stub landings. Driven through a REAL Miss so the
    //     carried state holds slot + zone + result (the chain walks E -> G -> H, keeping
    //     only the misses, then resolves I on the LiveBall pie). The seven rates are
    //     checked against the configured pie; routing is checked per outcome:
    //     DefensiveRebound / LooseBallFoulOnOffense / OutOfBoundsOffOffense -> END
    //     terminals; OffensiveRebound -> STUB:OffensiveRebound (facts); OutOfBoundsOffDefense
    //     -> STUB:SidelineInbound with NO charge; LooseBallFoulOnDefense -> SidelineInbound
    //     (below bonus) or ResolveFreeThrows (in bonus, §2a); JumpBall -> the arrow node.
    //     The OOB-off-defense and below-bonus loose-ball-defense arms both land on a
    //     plain sideline inbound and are separated by the foul DELTA (the foul arm
    //     charged 1, the OOB arm 0 — also the proof the OOB pair charges nothing).
    //     The block pie is exercised separately by RollIBlockReboundBatchCheck. ---
    private static bool RollIReboundBatchCheck(
        RollAConfig cfg, RollIConfig cfgI, RollIStubPieGenerator genI,
        GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} rebounds routed through Roll I ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the foul charge here does not perturb the shared game and
        // so the defense never reaches the bonus (keeping the loose-ball-defense arm
        // on its SidelineInbound branch for this batch).
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);
        var genH = new RollHStubPieGenerator(cfgH);
        var pieE = genE.Generate(state);
        var pieI = genI.Generate(state, ReboundSource.LiveBall);

        var counts = new Dictionary<ReboundOutcome, int>();
        foreach (var o in Enum.GetValues<ReboundOutcome>()) counts[o] = 0;

        // Where each outcome routed, and whether facts rode through the stubs.
        var defTerminals = 0;       // END:DefensiveRebound
        var offFoulTerminals = 0;   // END:LooseBallFoulOnOffense
        var oobOffTerminals = 0;    // END:OutOfBoundsOffOffense
        var offReboundStubs = 0;    // STUB:OffensiveRebound
        var oobDefSideline = 0;     // STUB:SidelineInbound (OOB-off-defense, no charge)
        var defFoulSideline = 0;    // STUB:SidelineInbound (loose-ball-defense, below bonus)
        var defFoulFreeThrows = 0;  // STUB:ResolveFreeThrows (loose-ball-defense, in bonus)
        var jumpBalls = 0;          // ResolveJumpBall
        var unrecognized = 0;
        var missingFact = 0;
        var badCharge = 0;          // a non-foul arm moved the team-foul count
        var resolved = 0;

        // We resolve Roll I's continuations against the same stub set the resolver
        // uses, to confirm the facts ride through. The loose-ball-defense arm splits
        // by bonus: below the threshold it routes to SidelineInbound (fact-carrying),
        // and once the SHARED game's defense crosses the bonus it routes to the bonus
        // FT stub (carrying a Bonus payload) — both are correct.
        var offReboundStub = new OffensiveReboundStub();
        var sidelineStub = new SidelineInboundStub();
        var freeThrowStub = new ResolveFreeThrowsStub();

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Walk E -> G -> H; keep only the misses (the one feed into Roll I).
            var sel = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), 0.0, rng)).State;
            var hRes = RollH.Execute(zoned, genH.Generate(zoned), rng);
            if (hRes is not Continue { Next: ContinuationKind.ResolveRebound } miss) continue;

            resolved++;
            var before = game.Fouls.FoulsFor(miss.State.Defense);
            var iRes = RollI.Execute(miss.State, pieI, game, rng);
            var delta = game.Fouls.FoulsFor(miss.State.Defense) - before;

            switch (iRes)
            {
                case Terminal { Reason: "DefensiveRebound" }:
                    counts[ReboundOutcome.DefensiveRebound]++;
                    if (delta != 0) badCharge++;
                    defTerminals++;
                    break;
                case Terminal { Reason: "LooseBallFoulOnOffense" }:
                    counts[ReboundOutcome.LooseBallFoulOnOffense]++;
                    if (delta != 0) badCharge++;   // an offensive foul charges nothing
                    offFoulTerminals++;
                    break;
                case Terminal { Reason: "OutOfBoundsOffOffense" }:
                    counts[ReboundOutcome.OutOfBoundsOffOffense]++;
                    if (delta != 0) badCharge++;   // OOB charges nothing
                    oobOffTerminals++;
                    break;
                case Continue { Next: ContinuationKind.ResolveOffensiveRebound } orc:
                    counts[ReboundOutcome.OffensiveRebound]++;
                    if (delta != 0) badCharge++;
                    var od = offReboundStub.Receive(orc);
                    if (od.EndsWith("NO_SLOT") || od.EndsWith("NO_ZONE") || od.EndsWith("NO_RESULT")) missingFact++;
                    else offReboundStubs++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound } sic:
                    // BOTH the OOB-off-defense arm and the below-bonus loose-ball-defense
                    // arm land on a plain sideline inbound; the foul DELTA separates them
                    // (the foul arm charged 1, the OOB arm charged 0). This is also the
                    // proof the OOB-off-defense arm charges nothing.
                    var sdLabel = sidelineStub.Receive(sic);
                    if (sdLabel.EndsWith("NO_SLOT") || sdLabel.EndsWith("NO_ZONE") || sdLabel.EndsWith("NO_RESULT")) missingFact++;
                    if (delta == 0)
                    {
                        counts[ReboundOutcome.OutOfBoundsOffDefense]++;
                        if (!sdLabel.EndsWith("NO_SLOT") && !sdLabel.EndsWith("NO_ZONE") && !sdLabel.EndsWith("NO_RESULT")) oobDefSideline++;
                    }
                    else
                    {
                        counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                        if (delta != 1) badCharge++;
                        if (!sdLabel.EndsWith("NO_SLOT") && !sdLabel.EndsWith("NO_ZONE") && !sdLabel.EndsWith("NO_RESULT")) defFoulSideline++;
                    }
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows } ftc:
                    // The loose-ball-defense foul with the defense IN the bonus — the
                    // shared game crosses the threshold partway through the batch, so
                    // this is expected, not an anomaly. Charges the bonus FT stub,
                    // carrying the Bonus payload (OneAndOne / Double). The OOB-off-defense
                    // arm can NEVER reach this branch (it reads no bonus, charges no foul).
                    counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                    if (delta != 1) badCharge++;
                    _ = freeThrowStub.Receive(ftc);
                    defFoulFreeThrows++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[ReboundOutcome.JumpBall]++;
                    if (delta != 0) badCharge++;
                    jumpBalls++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        Console.WriteLine($"  misses resolved through Roll I: {resolved:N0}");
        var n = (double)resolved;
        var ratesOk = true;
        Console.WriteLine("  Roll I outcomes:");
        foreach (var (outcome, weight) in pieI.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    DefensiveRebound       -> END terminal       {defTerminals,8:N0}  {(defTerminals > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    LooseBallFoulOnOffense -> END terminal       {offFoulTerminals,8:N0}  {(offFoulTerminals > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffOffense  -> END terminal       {oobOffTerminals,8:N0}  {(oobOffTerminals > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OffensiveRebound       -> STUB (same poss.)  {offReboundStubs,8:N0}  {(offReboundStubs > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffDefense  -> SidelineInbound     {oobDefSideline,8:N0}  {(oobDefSideline > 0 ? "ok" : "NONE")}  (no charge)");
        // The loose-ball-defense foul splits on the bonus: SidelineInbound below it,
        // ResolveFreeThrows once the shared game crosses the threshold mid-batch (§2a).
        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine($"    LooseBallFoulOnDefense -> SidelineInbound     {defFoulSideline,8:N0}  (below bonus)");
        Console.WriteLine($"                           -> ResolveFreeThrows  {defFoulFreeThrows,7:N0}  (in bonus)");
        Console.WriteLine($"    JumpBall               -> arrow node         {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var allArms = defTerminals > 0 && offFoulTerminals > 0 && oobOffTerminals > 0
                      && offReboundStubs > 0 && oobDefSideline > 0 && defFoulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var factOk = missingFact == 0;
        var chargeOk = badCharge == 0;
        var bonusCrossOk = defFoulSideline > 0 && defFoulFreeThrows > 0;
        Console.WriteLine($"\n  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  slot+zone+result intact on stub landings: missing={missingFact} -> {(factOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  foul-charge discipline (only loose-ball-defense charges): bad={badCharge} -> {(chargeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: loose-ball-defense crossed the bonus mid-batch (sideline -> FT): {(bonusCrossOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll I arms reached: {(allArms ? "ok" : "FAIL")}");

        return ratesOk && routedOk && factOk && chargeOk && allArms && bonusCrossOk;
    }


    // --- Bonus fork: the loose-ball-foul-on-defense arm charges the DEFENSIVE team
    //     foul and routes on the bonus exactly as Roll D does. Drive the defense's
    //     foul count up across the bonus thresholds and confirm: below bonus ->
    //     SidelineInbound (Bonus=None); at/above the bonus threshold ->
    //     ResolveFreeThrows carrying OneAndOne; at/above the double-bonus threshold
    //     -> ResolveFreeThrows carrying Double. Mirrors RollDBonusRoutingCheck. ---
    private static bool RollIBonusForkCheck(
        RollAConfig cfg, RollDConfig cfgD, RollIConfig cfgI,
        RollIStubPieGenerator genI, PossessionState state)
    {
        Console.WriteLine($"\n--- Bonus fork: Roll I loose-ball-defense foul across the thresholds ---");

        // Build a pie that ALWAYS rolls LooseBallFoulOnDefense, so every draw
        // exercises the foul arm regardless of RNG. (All mass on the
        // foul-on-defense slice; the others sit at zero — the Pie constructor
        // still requires every enum member to be present.)
        var foulOnlyPie = new Pie<ReboundOutcome>(new Dictionary<ReboundOutcome, double>
        {
            [ReboundOutcome.DefensiveRebound] = 0.0,
            [ReboundOutcome.OffensiveRebound] = 0.0,
            [ReboundOutcome.LooseBallFoulOnDefense] = 1.0,
            [ReboundOutcome.LooseBallFoulOnOffense] = 0.0,
            [ReboundOutcome.OutOfBoundsOffOffense] = 0.0,
            [ReboundOutcome.OutOfBoundsOffDefense] = 0.0,
            [ReboundOutcome.JumpBall] = 0.0
        }, cfgI.Epsilon);

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var rng = new SystemRng(cfg.Seed);

        var ok = true;
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var r = RollI.Execute(state, foulOnlyPie, game, rng);
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


    // --- Batch: Roll I's seven-way BLOCK-RECOVERY distribution converges within
    //     tolerance, and every arm routes exactly as the live-miss path does (same
    //     routes, different weights). Driven by calling RollI.Execute DIRECTLY on the
    //     base state with the BLOCK pie (the RollM batch pattern), so all seven arms
    //     are exercised every draw. A FRESH game crosses the bonus mid-batch, so the
    //     LooseBallFoulOnDefense arm splits sideline/FT (§2a). Per-arm routing is
    //     asserted: DefensiveRebound -> Terminal whose consequence is a TRANSITION to
    //     the defense carrying the Rebound context (NOT FreeThrowRebound — that is Roll
    //     M's); OffensiveRebound -> Continue(ResolveOffensiveRebound) with NO source
    //     stamp (a block reuses the LiveBall offensive-rebound pie — a distinct block
    //     source is deferred); LooseBallFoulOnOffense / OutOfBoundsOffOffense -> Terminal
    //     DeadBallTo the defense; OutOfBoundsOffDefense -> Continue(ResolveSidelineInbound)
    //     with NO charge even in the bonus; LooseBallFoulOnDefense -> charge the defense
    //     + bonus fork; JumpBall -> Continue(ResolveJumpBall). Foul-charge discipline is
    //     asserted PER DRAW off the team-foul delta: ONLY the loose-ball-defense arm
    //     increments the defensive team foul; the OOB pair and every other arm charge
    //     nothing. ---
    private static bool RollIBlockReboundBatchCheck(
        RollAConfig cfg, RollIConfig cfgI, RollIStubPieGenerator genI, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} BLOCK recoveries routed through Roll I ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgD = RollDConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the charge here does not perturb the shared game. The
        // loose-ball-defense arm's charge crosses the bonus partway through THIS game,
        // exercising the §2a split (sideline below the bonus, FTs in it).
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var pieI = genI.Generate(state, ReboundSource.Block);

        var counts = new Dictionary<ReboundOutcome, int>();
        foreach (var o in Enum.GetValues<ReboundOutcome>()) counts[o] = 0;

        var defTransition = 0;     // DefensiveRebound -> transition terminal to defense
        var offReboundCont = 0;    // OffensiveRebound -> Roll K cont, NO source (LiveBall)
        var offFoulDead = 0;       // LooseBallFoulOnOffense -> dead-ball terminal
        var oobOffDead = 0;        // OutOfBoundsOffOffense -> dead-ball terminal
        var oobDefSideline = 0;    // OutOfBoundsOffDefense -> sideline (no fork, no charge)
        var defFoulSideline = 0;   // LooseBallFoulOnDefense below bonus -> sideline
        var defFoulFreeThrows = 0; // LooseBallFoulOnDefense in bonus -> FTs
        var jumpBalls = 0;         // JumpBall -> arrow node
        var unrecognized = 0;
        var badConsequence = 0;    // wrong team / entry / context on a terminal or ticket
        var badCharge = 0;         // a non-foul arm moved the team-foul count (or the
                                   //  foul arm failed to charge exactly 1)

        var sidelineStub = new SidelineInboundStub();
        var freeThrowStub = new ResolveFreeThrowsStub();

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var iRes = RollI.Execute(state, pieI, game, rng);
            var delta = game.Fouls.FoulsFor(state.Defense) - before;

            switch (iRes)
            {
                case Terminal { Reason: "DefensiveRebound" } dr:
                    counts[ReboundOutcome.DefensiveRebound]++;
                    if (delta != 0) badCharge++;
                    if (dr.Consequence.NextOffense != state.Defense
                        || dr.Consequence.NextEntry != EntryType.Transition
                        || dr.Consequence.TransitionContext?.Source != TransitionSource.Rebound)
                        badConsequence++;
                    else defTransition++;
                    break;

                case Continue { Next: ContinuationKind.ResolveOffensiveRebound } orc:
                    counts[ReboundOutcome.OffensiveRebound]++;
                    if (delta != 0) badCharge++;
                    // A block reuses the LiveBall offensive-rebound pie: NO source stamp
                    // (a distinct block source is deferred). A null reads as LiveBall.
                    if (orc.OffensiveReboundSource is not null) badConsequence++;
                    else offReboundCont++;
                    break;

                case Terminal { Reason: "LooseBallFoulOnOffense" } lbo:
                    counts[ReboundOutcome.LooseBallFoulOnOffense]++;
                    if (delta != 0) badCharge++;       // an offensive foul charges nothing
                    if (lbo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else offFoulDead++;
                    break;

                case Terminal { Reason: "OutOfBoundsOffOffense" } oobo:
                    counts[ReboundOutcome.OutOfBoundsOffOffense]++;
                    if (delta != 0) badCharge++;       // OOB charges nothing
                    if (oobo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else oobOffDead++;
                    break;

                case Continue { Next: ContinuationKind.ResolveSidelineInbound } sc:
                    // BOTH the OOB-off-defense arm and the below-bonus loose-ball-defense
                    // arm land on a plain sideline inbound; the foul DELTA separates them
                    // (the foul arm charged 1, the OOB arm charged 0). This is also the
                    // proof the OOB pair charges nothing.
                    if (delta == 0)
                    {
                        counts[ReboundOutcome.OutOfBoundsOffDefense]++;
                        oobDefSideline++;
                    }
                    else
                    {
                        counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                        if (delta != 1) badCharge++;
                        defFoulSideline++;
                    }
                    _ = sidelineStub.Receive(sc);
                    break;

                case Continue { Next: ContinuationKind.ResolveFreeThrows } ftc:
                    // ONLY the loose-ball-defense arm IN THE BONUS lands here. The
                    // OOB-off-defense arm can NEVER reach this branch — it reads no bonus
                    // and charges no foul — so a delta of 0 here would be a routing bug.
                    counts[ReboundOutcome.LooseBallFoulOnDefense]++;
                    if (delta != 1) badCharge++;
                    defFoulFreeThrows++;
                    _ = freeThrowStub.Receive(ftc);
                    break;

                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[ReboundOutcome.JumpBall]++;
                    if (delta != 0) badCharge++;
                    jumpBalls++;
                    break;

                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll I block outcomes:");
        foreach (var (outcome, weight) in pieI.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    DefensiveRebound       -> transition terminal (defense) {defTransition,8:N0}  {(defTransition > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OffensiveRebound       -> Roll K cont (no source stamp)  {offReboundCont,8:N0}  {(offReboundCont > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    LooseBallFoulOnOffense -> dead-ball terminal (defense)   {offFoulDead,8:N0}  {(offFoulDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffOffense  -> dead-ball terminal (defense)   {oobOffDead,8:N0}  {(oobOffDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffDefense  -> sideline inbound (no charge)   {oobDefSideline,8:N0}  {(oobDefSideline > 0 ? "ok" : "NONE")}");
        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine($"    LooseBallFoulOnDefense -> SidelineInbound (below bonus)  {defFoulSideline,8:N0}");
        Console.WriteLine($"                           -> ResolveFreeThrows (in bonus)   {defFoulFreeThrows,8:N0}");
        Console.WriteLine($"    JumpBall               -> arrow node                     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var allArms = defTransition > 0 && offReboundCont > 0 && offFoulDead > 0 && oobOffDead > 0
                      && oobDefSideline > 0 && defFoulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var consequenceOk = badConsequence == 0;
        var chargeOk = badCharge == 0;
        var bonusCrossOk = defFoulSideline > 0 && defFoulFreeThrows > 0;
        Console.WriteLine($"\n  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  consequence/ticket correct on every terminal+continue: bad={badConsequence} -> {(consequenceOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  foul-charge discipline (only loose-ball-defense charges): bad={badCharge} -> {(chargeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: loose-ball-defense crossed the bonus mid-batch (sideline -> FT): {(bonusCrossOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll I block arms reached: {(allArms ? "ok" : "FAIL")}");

        var okAll = ratesOk && routedOk && consequenceOk && chargeOk && allArms && bonusCrossOk;
        Console.WriteLine($"  Roll I block-recovery resolution: {(okAll ? "ok" : "FAIL")}");
        return okAll;
    }


    // --- Context: the block source SELECTS the block pie, and a blocked shot actually
    //     REACHES Roll I (never the retired block-recovery stub). Two parts, the
    //     RollMContextSelectionCheck pattern:
    //     (1) Pie selection. Roll I's generator, given ReboundSource.Block, returns the
    //     block weight set; given LiveBall (the legacy default a null stamp maps to), the
    //     live-miss set. Each is proven two ways: the SELECTED pie's weights equal the
    //     configured set (right SET picked), and a draw sample off that pie converges to
    //     those weights (the pie is actually consumed). The two sources must also DIFFER
    //     (a null/legacy stamp can never accidentally get the block odds).
    //     (2) Route proof. Roll H's Blocked arm now emits Continue(ResolveRebound) carrying
    //     ReboundSource.Block — asserted directly off the emission — and routing that
    //     continuation through a REAL resolver lands at a Roll-I-family destination, NEVER
    //     at STUB:BlockRecovery (retired to the corner), with zero unrouted. ---
    private static bool RollIBlockContextSelectionCheck(
        RollAConfig cfg, RollIConfig cfgI, PossessionState state)
    {
        Console.WriteLine($"\n--- Context: Roll I block pie selection + Blocked -> Roll I routing ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var ok = true;

        // ---- (1) Pie selection: LiveBall (legacy default) vs Block ----
        var genI = new RollIStubPieGenerator(cfgI);
        var contexts = new (ReboundSource src, (ReboundOutcome o, double w)[] expected)[]
        {
            (ReboundSource.LiveBall, new[]
            {
                (ReboundOutcome.DefensiveRebound,       cfgI.DefensiveRebound),
                (ReboundOutcome.OffensiveRebound,       cfgI.OffensiveRebound),
                (ReboundOutcome.LooseBallFoulOnDefense, cfgI.LooseBallFoulOnDefense),
                (ReboundOutcome.LooseBallFoulOnOffense, cfgI.LooseBallFoulOnOffense),
                (ReboundOutcome.OutOfBoundsOffOffense,  cfgI.OutOfBoundsOffOffense),
                (ReboundOutcome.OutOfBoundsOffDefense,  cfgI.OutOfBoundsOffDefense),
                (ReboundOutcome.JumpBall,               cfgI.JumpBall),
            }),
            (ReboundSource.Block, new[]
            {
                (ReboundOutcome.DefensiveRebound,       cfgI.BlockDefensiveRebound),
                (ReboundOutcome.OffensiveRebound,       cfgI.BlockOffensiveRebound),
                (ReboundOutcome.LooseBallFoulOnDefense, cfgI.BlockLooseBallFoulOnDefense),
                (ReboundOutcome.LooseBallFoulOnOffense, cfgI.BlockLooseBallFoulOnOffense),
                (ReboundOutcome.OutOfBoundsOffOffense,  cfgI.BlockOutOfBoundsOffOffense),
                (ReboundOutcome.OutOfBoundsOffDefense,  cfgI.BlockOutOfBoundsOffDefense),
                (ReboundOutcome.JumpBall,               cfgI.BlockJumpBall),
            }),
        };

        double Defensive(ReboundSource s) =>
            genI.Generate(state, s).Slices.First(x => x.Outcome == ReboundOutcome.DefensiveRebound).Weight;

        foreach (var (src, expected) in contexts)
        {
            var pie = genI.Generate(state, src);
            var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var selectionOk = true;
            foreach (var (o, w) in expected)
                if (Math.Abs(pieMap[o] - w) > cfgI.Epsilon) selectionOk = false;

            var rng = new SystemRng(cfg.Seed);
            var counts = new Dictionary<ReboundOutcome, int>();
            foreach (var o in Enum.GetValues<ReboundOutcome>()) counts[o] = 0;
            for (var i = 0; i < cfg.BatchSize; i++) counts[pie.Roll(rng.NextUnitInterval())]++;

            var ratesOk = true;
            Console.WriteLine($"  Roll I source={src} (selection {(selectionOk ? "ok" : "FAIL")}):");
            foreach (var (o, w) in expected)
            {
                var observed = counts[o] / (double)cfg.BatchSize;
                var pass = Math.Abs(observed - w) <= cfg.RateTolerance;
                ratesOk &= pass;
                Console.WriteLine($"    {o,-24} observed={observed:P3}  expected={w:P3}  {(pass ? "ok" : "FAIL")}");
            }
            ok &= selectionOk && ratesOk;
        }
        var differ = Math.Abs(Defensive(ReboundSource.Block) - Defensive(ReboundSource.LiveBall)) > cfgI.Epsilon;
        Console.WriteLine($"  block pie differs from live-miss pie (DefensiveRebound): {(differ ? "ok" : "FAIL")}");
        ok &= differ;

        // ---- (2) Route proof: a Blocked shot reaches Roll I, never STUB:BlockRecovery ----
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);

        var rngR = new SystemRng(cfg.Seed);
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
            new RollHStubPieGenerator(cfgH),
            new RollIStubPieGenerator(RollIConfig.Load(configPath)),
            new RollJGenerator(RollJConfig.Load(configPath), MatchupConfig.Load(configPath), game),
            new RollKStubPieGenerator(RollKConfig.Load(configPath)),
            new RollLStubPieGenerator(RollLConfig.Load(configPath)),
            new RollMStubPieGenerator(RollMConfig.Load(configPath)),
            new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
            MatchupConfig.Load(configPath),
            game,
            rngR);

        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);
        var genH = new RollHStubPieGenerator(cfgH);
        var pieE = genE.Generate(state);

        var blocks = 0;
        var blockStampOk = 0;
        var blockRecoveryHits = 0;
        var unrouted = 0;
        var landedAtRollI = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var sel = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rngR)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), 0.0, rngR)).State;
            var hRes = RollH.Execute(zoned, genH.Generate(zoned), rngR);

            // Keep only the BLOCKED shots — the arm this check is about.
            if (hRes is not Continue { Next: ContinuationKind.ResolveRebound } cont) continue;
            if (cont.ReboundSource != ReboundSource.Block) continue;   // a plain miss; skip

            blocks++;
            // Re-point proof: Roll H's Blocked arm emits ResolveRebound + Block, NOT the
            // retired ResolveBlock edge.
            if (cont is { Next: ContinuationKind.ResolveRebound, ReboundSource: ReboundSource.Block })
                blockStampOk++;

            // Route it through the real resolver and inspect the landing.
            var d = resolver.Route(cont).Destination;
            if (d.StartsWith("STUB:BlockRecovery")) blockRecoveryHits++;
            // A blocked shot now lands at a Roll-I-family destination: a rebound terminal
            // (END:DefensiveRebound / LooseBallFoulOnOffense / OutOfBoundsOffOffense), a
            // made/missed putback or reset deeper through Roll K, a sideline inbound, a
            // jump-ball terminal, or a bonus-FT landing. The one thing it must NEVER be is
            // the retired block-recovery stub, and it must always route somewhere.
            if (d.StartsWith("END:") || d.StartsWith("STUB:")) landedAtRollI++;
            else unrouted++;
        }

        Console.WriteLine($"  blocked shots observed: {blocks:N0}");
        Console.WriteLine($"  each Blocked emits Continue(ResolveRebound) + ReboundSource.Block: {blockStampOk:N0}/{blocks:N0} -> {(blockStampOk == blocks && blocks > 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"  blocked shots routed to a Roll-I destination: {landedAtRollI:N0}");
        Console.WriteLine($"  blocked shots landing at the retired STUB:BlockRecovery: {blockRecoveryHits} -> {(blockRecoveryHits == 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"  zero unrouted blocked shots: {unrouted} -> {(unrouted == 0 ? "ok" : "FAIL")}");

        var routeOk = blocks > 0 && blockStampOk == blocks && blockRecoveryHits == 0 && unrouted == 0;
        ok &= routeOk;
        Console.WriteLine($"  Roll I block context selection + routing: {(ok ? "ok" : "FAIL")}");
        return ok;
    }


    // --- Batch: Roll K's seven-way offensive-rebound distribution converges within
    //     tolerance, and every arm routes as designed. Driven by calling
    //     RollK.Execute DIRECTLY on a fully-stamped post-miss state (the RollI batch
    //     pattern), so all seven arms are exercised every draw. A FRESH game crosses
    //     the bonus mid-batch, so the DefensiveFoul arm splits sideline/FT. Per-arm
    //     routing is asserted: PutBack -> Continue(IntoShotResolution) with the putback
    //     ticket set AND the zone forced to Rim; JumpBall -> Continue(ResolveJumpBall);
    //     DefensiveFoul -> sideline/FT fork charging the defense; OffensiveFoul /
    //     DeadBallTurnover / LiveBallTurnover -> terminals whose consequence flips the
    //     ball to the defense; ResetOffense -> Continue(IntoPlayerSelection) with the
    //     prior shot's facts WIPED (ShotType and Result null) AND FastBreak cleared (a
    //     reset off a missed break is a fresh halfcourt play — the marker leak guard). ---
    private static bool RollKReboundBatchCheck(
        RollAConfig cfg, RollKConfig cfgK, IRollKPieGenerator genK,
        GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} offensive rebounds routed through Roll K ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the foul charge here does not perturb the shared game; it
        // crosses the bonus partway through, exercising both fork branches.
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var genE = new RollEStubPieGenerator(cfgE);
        var genG = new RollGStubPieGenerator(cfgG);
        var pieE = genE.Generate(state);
        var pieK = genK.Generate(state, OffensiveReboundSource.LiveBall);

        var counts = new Dictionary<OffensiveReboundOutcome, int>();
        foreach (var o in Enum.GetValues<OffensiveReboundOutcome>()) counts[o] = 0;

        var putbackTicketOk = 0;   // PutBack arms with Putback==true && ShotType==Rim
        var resetWipedOk = 0;      // ResetOffense arms with ShotType==null && Result==null && FastBreak==false
        var resetClearedBreak = 0; // ResetOffense arms with FastBreak cleared (the leak guard)
        var defFoulSideline = 0;   // DefensiveFoul below bonus
        var defFoulFreeThrows = 0; // DefensiveFoul in bonus
        var flipToDefenseOk = 0;   // terminals whose consequence hands the ball to the defense
        var liveTurnoverStealCtxOk = 0; // LiveBallTurnover carrying the Steal context (-> Roll J)
        var liveTurnoverCtxBad = 0;     // LiveBallTurnover MISSING the steal context (a FAIL)
        var unrecognized = 0;

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Build a fully-stamped post-miss state: slot (E) + zone (G) + a Miss
            // result, exactly what arrives at the offensive-rebound node in the live
            // chain. The zone is a REAL non-Rim mix, so the PutBack arm's force-to-Rim
            // and the ResetOffense arm's wipe are both observable. FastBreak=true
            // simulates a possession that PUSHED, missed, and grabbed its own board —
            // so the ResetOffense leak-guard (FastBreak must clear) is exercised.
            var sel = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), 0.0, rng)).State;
            var stamped = zoned with { Result = ShotResult.Miss, FastBreak = true };

            var kRes = RollK.Execute(stamped, pieK, game, rng);

            switch (kRes)
            {
                case Continue { Next: ContinuationKind.IntoShotResolution } pb:
                    counts[OffensiveReboundOutcome.PutBack]++;
                    if (pb.Putback && pb.State.ShotType == ShotLocation.Rim) putbackTicketOk++;
                    break;
                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[OffensiveReboundOutcome.JumpBall]++;
                    break;
                case Continue { Next: ContinuationKind.ResolveSidelineInbound }:
                    counts[OffensiveReboundOutcome.DefensiveFoul]++;
                    defFoulSideline++;
                    break;
                case Continue { Next: ContinuationKind.ResolveFreeThrows }:
                    counts[OffensiveReboundOutcome.DefensiveFoul]++;
                    defFoulFreeThrows++;
                    break;
                case Terminal { Reason: "OffensiveFoul" } t1:
                    counts[OffensiveReboundOutcome.OffensiveFoul]++;
                    if (t1.Consequence.NextOffense == stamped.Defense) flipToDefenseOk++;
                    break;
                case Terminal { Reason: "DeadBallTurnover" } t2:
                    counts[OffensiveReboundOutcome.DeadBallTurnover]++;
                    if (t2.Consequence.NextOffense == stamped.Defense) flipToDefenseOk++;
                    break;
                case Terminal { Reason: "LiveBallTurnover" } t3:
                    counts[OffensiveReboundOutcome.LiveBallTurnover]++;
                    if (t3.Consequence.NextOffense == stamped.Defense) flipToDefenseOk++;
                    // Contextification #3: a live turnover off the board now carries the
                    // Steal context, so the resolver routes the spawn to Roll J (not Roll A).
                    if (t3.Consequence.NextEntry == EntryType.Transition
                        && t3.Consequence.TransitionContext?.Source == TransitionSource.Steal)
                        liveTurnoverStealCtxOk++;
                    else liveTurnoverCtxBad++;
                    break;
                case Continue { Next: ContinuationKind.IntoPlayerSelection } rs:
                    counts[OffensiveReboundOutcome.ResetOffense]++;
                    // The reset must wipe the prior shot's facts AND clear FastBreak so
                    // the fresh play draws the HALFCOURT pie — even though it arrived on
                    // a FastBreak=true state. The marker must not leak past the reset.
                    if (rs.State.ShotType is null && rs.State.Result is null && !rs.State.FastBreak) resetWipedOk++;
                    if (!rs.State.FastBreak) resetClearedBreak++;
                    break;
                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll K outcomes:");
        foreach (var (outcome, weight) in pieK.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-20} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine("\n  routing per arm:");
        Console.WriteLine($"    PutBack        -> Roll H, putback ticket + Rim forced  {putbackTicketOk,8:N0} / {counts[OffensiveReboundOutcome.PutBack]:N0}");
        Console.WriteLine($"    ResetOffense   -> Roll E, facts wiped (zone+result null, FastBreak cleared) {resetWipedOk,7:N0} / {counts[OffensiveReboundOutcome.ResetOffense]:N0}");
        Console.WriteLine($"                      FastBreak cleared (leak guard) {resetClearedBreak,8:N0} / {counts[OffensiveReboundOutcome.ResetOffense]:N0}  {(resetClearedBreak == counts[OffensiveReboundOutcome.ResetOffense] ? "ok" : "FAIL")}");
        Console.WriteLine($"    DefensiveFoul  -> SidelineInbound {defFoulSideline,8:N0} (below bonus) / ResolveFreeThrows {defFoulFreeThrows,7:N0} (in bonus)");
        Console.WriteLine($"    terminals flip ball to defense: {flipToDefenseOk,8:N0}");

        var putbackShapeOk = putbackTicketOk == counts[OffensiveReboundOutcome.PutBack] && putbackTicketOk > 0;
        var resetShapeOk = resetWipedOk == counts[OffensiveReboundOutcome.ResetOffense] && resetWipedOk > 0;
        var flipOk = flipToDefenseOk ==
            counts[OffensiveReboundOutcome.OffensiveFoul]
            + counts[OffensiveReboundOutcome.DeadBallTurnover]
            + counts[OffensiveReboundOutcome.LiveBallTurnover]
            && flipToDefenseOk > 0;
        var allArms = counts.Values.All(c => c > 0);
        var foulForkOk = defFoulSideline > 0 && defFoulFreeThrows > 0;
        var routedOk = unrecognized == 0;

        Console.WriteLine($"\n  every PutBack carries the ticket + Rim: {(putbackShapeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every ResetOffense wipes the prior shot: {(resetShapeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  every flip terminal hands the ball to the defense: {(flipOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  DefensiveFoul fork exercises both branches: {(foulForkOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll K arms reached: {(allArms ? "ok" : "FAIL")}");
        Console.WriteLine($"  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");

        var liveTurnoverCtxOk = liveTurnoverCtxBad == 0 && liveTurnoverStealCtxOk > 0;
        Console.WriteLine($"  LiveBallTurnover carries Steal context (-> Roll J): ok={liveTurnoverStealCtxOk:N0} bad={liveTurnoverCtxBad} -> {(liveTurnoverCtxOk ? "ok" : "FAIL")}");

        return ratesOk && putbackShapeOk && resetShapeOk && flipOk && foulForkOk && allArms && routedOk && liveTurnoverCtxOk;
    }


    // --- Context: the putback ticket selects a DISTINCT shot pie. Proves the
    //     ticket/station seam for Roll K -> Roll H end to end: at the SAME Rim zone,
    //     Roll H's generator returns the located-shot pie WITHOUT the ticket and the
    //     putback pie WITH it, and the two differ. The putback pie is asserted equal
    //     to its configured Putback* weights (selection correct), then driven through
    //     Roll H (consumption correct). Mirrors RollCContextCheck. ---
    private static bool RollKPutbackPieCheck(RollAConfig cfg, RollHConfig cfgH, PossessionState state)
    {
        Console.WriteLine($"\n--- Context: Roll H putback pie selected by the putback ticket ---");

        var genH = new RollHStubPieGenerator(cfgH);
        // Both pies at the SAME zone (Rim) — so any difference is the TICKET's doing,
        // not the zone's. A putback always forces Rim, so this is the honest compare.
        var rimState = state with { ShotType = ShotLocation.Rim };

        var normalPie = genH.Generate(rimState, putback: false);
        var putbackPie = genH.Generate(rimState, putback: true);

        var normalMap = normalPie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        var putbackMap = putbackPie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

        // Phase 8: expected values use the same carve-then-convert math as BuildPutbackPie.
        // PutbackMade is the conversion rate GIVEN not blocked AND not fouled.
        var pbBlock           = cfgH.PutbackBlocked;
        var pbFoul            = cfgH.FoulRate(ShotLocation.Rim);
        var pbNonBlockNonFoul = 1.0 - pbBlock - pbFoul;
        var pbMade        = cfgH.PutbackMade * pbNonBlockNonFoul;
        var pbMafFrac     = cfgH.MafFraction(ShotLocation.Rim);
        var pbMaf         = pbFoul * pbMafFrac;
        var pbMissFouled  = pbFoul * (1.0 - pbMafFrac);
        var pbNonMadeBase = cfgH.PutbackMiss + cfgH.PutbackMissOutOfBoundsLost + cfgH.PutbackMissOutOfBoundsRetained;
        var pbNonMadeShare = pbNonBlockNonFoul - pbMade;
        var pbScale       = pbNonMadeBase > 0.0 ? pbNonMadeShare / pbNonMadeBase : 0.0;

        var expected = new[]
        {
            (ShotResult.Made,                    pbMade),
            (ShotResult.MadeAndFouled,           pbMaf),
            (ShotResult.Miss,                    cfgH.PutbackMiss                    * pbScale),
            (ShotResult.MissFouled,              pbMissFouled),
            (ShotResult.MissOutOfBoundsLost,     cfgH.PutbackMissOutOfBoundsLost     * pbScale),
            (ShotResult.MissOutOfBoundsRetained, cfgH.PutbackMissOutOfBoundsRetained * pbScale),
            (ShotResult.Blocked,                 pbBlock),
        };

        // 1) Selection: the putback pie matches the Phase 8 carve-computed weights.
        var selectionOk = true;
        foreach (var (o, w) in expected)
            if (Math.Abs(putbackMap[o] - w) > cfgH.Epsilon) selectionOk = false;

        // 2) Distinctness: the putback pie is NOT the normal Rim pie — at least one
        //    slice differs materially. (Proves the ticket switched the pie, not that
        //    two equal pies happened to validate.)
        var distinct = expected.Any(e => Math.Abs(putbackMap[e.Item1] - normalMap[e.Item1]) > cfg.RateTolerance);

        // 3) Consumption: driving the putback pie through Roll H reproduces the rates.
        var rng = new SystemRng(cfg.Seed);
        var counts = new Dictionary<ShotResult, int>();
        foreach (var r in Enum.GetValues<ShotResult>()) counts[r] = 0;
        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var res = RollH.Execute(rimState, putbackPie, rng);
            // Both the terminal and continue arms of Roll H stamp the chosen Result
            // onto their carried state, so recover it uniformly.
            var resState = res switch
            {
                Terminal t => t.State,
                Continue c => c.State,
                _ => throw new InvalidOperationException("unexpected Roll H result.")
            };
            var result = resState.Result ?? throw new InvalidOperationException("Roll H result lost its Result stamp.");
            counts[result]++;
        }

        var nn = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine($"  putback pie (selection {(selectionOk ? "ok" : "FAIL")}, distinct from rim pie {(distinct ? "ok" : "FAIL")}):");
        foreach (var (o, w) in expected)
        {
            var observed = counts[o] / nn;
            var gap = Math.Abs(observed - w);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {o,-26} observed={observed:P3}  expected={w:P3}  normal={normalMap[o]:P3}  {(pass ? "ok" : "FAIL")}");
        }

        var pass2 = selectionOk && distinct && ratesOk;
        Console.WriteLine($"  putback ticket selects its own pie: {(pass2 ? "ok" : "FAIL")}");
        return pass2;
    }


    // --- Bonus fork: Roll K's DefensiveFoul arm charges the DEFENSIVE team foul and
    //     routes on the bonus exactly as Rolls D / I / J do (the FOURTH feeder into
    //     the shared charge-and-fork). All-mass-on-DefensiveFoul pie across the
    //     thresholds. Mirrors RollIBonusForkCheck / RollJBonusForkCheck. ---
    private static bool RollKBonusForkCheck(
        RollAConfig cfg, RollDConfig cfgD, RollKConfig cfgK,
        IRollKPieGenerator genK, PossessionState state)
    {
        var foulOnlyPie = new Pie<OffensiveReboundOutcome>(new Dictionary<OffensiveReboundOutcome, double>
        {
            [OffensiveReboundOutcome.PutBack] = 0.0,
            [OffensiveReboundOutcome.JumpBall] = 0.0,
            [OffensiveReboundOutcome.DefensiveFoul] = 1.0,
            [OffensiveReboundOutcome.OffensiveFoul] = 0.0,
            [OffensiveReboundOutcome.DeadBallTurnover] = 0.0,
            [OffensiveReboundOutcome.LiveBallTurnover] = 0.0,
            [OffensiveReboundOutcome.ResetOffense] = 0.0,
        }, cfgK.Epsilon);

        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var rng = new SystemRng(cfg.Seed);

        var ok = true;
        Console.WriteLine($"  thresholds: bonus>={cfgD.BonusThreshold}, double>={cfgD.DoubleBonusThreshold}");
        for (var i = 1; i <= cfgD.DoubleBonusThreshold + 1; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var r = RollK.Execute(state, foulOnlyPie, game, rng);
            var after = game.Fouls.FoulsFor(state.Defense);

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


    // --- Batch: Roll M's seven-way free-throw-rebound distribution converges within
    //     tolerance, and every arm routes as designed. Driven by calling RollM.Execute
    //     DIRECTLY on a post-FT state (the RollI / RollK batch pattern), so all seven
    //     arms are exercised every draw. A FRESH game crosses the bonus mid-batch, so the
    //     LooseBallFoulOnDefense arm splits sideline/FT (§2a accumulation). Per-arm
    //     routing is asserted: DefensiveRebound -> Terminal whose consequence is a
    //     TRANSITION to the defense carrying the FreeThrowRebound context; OffensiveRebound
    //     -> Continue(ResolveOffensiveRebound) stamped OffensiveReboundSource.FreeThrow;
    //     LooseBallFoulOnDefense -> charge the defense + bonus fork; LooseBallFoulOnOffense
    //     / OutOfBoundsOffOffense -> Terminal DeadBallTo the defense; OutOfBoundsOffDefense
    //     -> Continue(ResolveSidelineInbound) with NO fork even in the bonus; JumpBall ->
    //     Continue(ResolveJumpBall). Foul-charge discipline is asserted PER DRAW off the
    //     team-foul delta: ONLY the loose-ball-defense arm increments the defensive team
    //     foul; the OOB pair and every other arm charge nothing. ---
    private static bool RollMReboundBatchCheck(
        RollAConfig cfg, RollMConfig cfgM, RollMStubPieGenerator genM,
        GameState sharedGame, PossessionState state)
    {
        Console.WriteLine($"\n--- Batch: {cfg.BatchSize:N0} free-throw rebounds routed through Roll M ---");
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var cfgD = RollDConfig.Load(configPath);

        var rng = new SystemRng(cfg.Seed);
        // A FRESH game so the charge here does not perturb the shared game. The
        // loose-ball-defense arm's charge crosses the bonus partway through THIS game,
        // exercising the §2a split (sideline below the bonus, FTs in it).
        var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
        SeedMinimalRoster(game);  // Phase 31: picker needs populated roster
        var pieM = genM.Generate(state);

        var counts = new Dictionary<FreeThrowReboundOutcome, int>();
        foreach (var o in Enum.GetValues<FreeThrowReboundOutcome>()) counts[o] = 0;

        var defTransition = 0;     // DefensiveRebound -> transition terminal to defense
        var offReboundCont = 0;    // OffensiveRebound -> Roll K cont, FreeThrow source
        var offFoulDead = 0;       // LooseBallFoulOnOffense -> dead-ball terminal
        var oobOffDead = 0;        // OutOfBoundsOffOffense -> dead-ball terminal
        var oobDefSideline = 0;    // OutOfBoundsOffDefense -> sideline (no fork, no charge)
        var defFoulSideline = 0;   // LooseBallFoulOnDefense below bonus -> sideline
        var defFoulFreeThrows = 0; // LooseBallFoulOnDefense in bonus -> FTs
        var jumpBalls = 0;         // JumpBall -> arrow node
        var unrecognized = 0;
        var badConsequence = 0;    // wrong team / entry / context on a terminal or ticket
        var badCharge = 0;         // a non-foul arm moved the team-foul count (or the
                                   //  foul arm failed to charge exactly 1)

        var sidelineStub = new SidelineInboundStub();
        var freeThrowStub = new ResolveFreeThrowsStub();

        for (var i = 0; i < cfg.BatchSize; i++)
        {
            var before = game.Fouls.FoulsFor(state.Defense);
            var mRes = RollM.Execute(state, pieM, game, rng);
            var delta = game.Fouls.FoulsFor(state.Defense) - before;

            switch (mRes)
            {
                case Terminal { Reason: "DefensiveRebound" } dr:
                    counts[FreeThrowReboundOutcome.DefensiveRebound]++;
                    if (delta != 0) badCharge++;
                    if (dr.Consequence.NextOffense != state.Defense
                        || dr.Consequence.NextEntry != EntryType.Transition
                        || dr.Consequence.TransitionContext?.Source != TransitionSource.FreeThrowRebound)
                        badConsequence++;
                    else defTransition++;
                    break;

                case Continue { Next: ContinuationKind.ResolveOffensiveRebound } orc:
                    counts[FreeThrowReboundOutcome.OffensiveRebound]++;
                    if (delta != 0) badCharge++;
                    // The FreeThrow source ticket must be stamped (selects Roll K's FT pie).
                    if (orc.OffensiveReboundSource != OffensiveReboundSource.FreeThrow) badConsequence++;
                    else offReboundCont++;
                    break;

                case Terminal { Reason: "LooseBallFoulOnOffense" } lbo:
                    counts[FreeThrowReboundOutcome.LooseBallFoulOnOffense]++;
                    if (delta != 0) badCharge++;       // an offensive foul charges nothing
                    if (lbo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else offFoulDead++;
                    break;

                case Terminal { Reason: "OutOfBoundsOffOffense" } oobo:
                    counts[FreeThrowReboundOutcome.OutOfBoundsOffOffense]++;
                    if (delta != 0) badCharge++;       // OOB charges nothing
                    if (oobo.Consequence.NextOffense != state.Defense) badConsequence++;
                    else oobOffDead++;
                    break;

                case Continue { Next: ContinuationKind.ResolveSidelineInbound } sc:
                    // BOTH the OOB-off-defense arm and the below-bonus loose-ball-defense
                    // arm land on a plain sideline inbound; the foul DELTA separates them
                    // (the foul arm charged 1, the OOB arm charged 0). This is also the
                    // proof the OOB pair charges nothing.
                    if (delta == 0)
                    {
                        counts[FreeThrowReboundOutcome.OutOfBoundsOffDefense]++;
                        oobDefSideline++;
                    }
                    else
                    {
                        counts[FreeThrowReboundOutcome.LooseBallFoulOnDefense]++;
                        if (delta != 1) badCharge++;
                        defFoulSideline++;
                    }
                    _ = sidelineStub.Receive(sc);
                    break;

                case Continue { Next: ContinuationKind.ResolveFreeThrows } ftc:
                    // ONLY the loose-ball-defense arm IN THE BONUS lands here (it charged
                    // a foul and the defense has crossed the threshold). The OOB-off-defense
                    // arm can NEVER reach this branch — it reads no bonus and charges no
                    // foul — so a delta of 0 here would be a routing bug.
                    counts[FreeThrowReboundOutcome.LooseBallFoulOnDefense]++;
                    if (delta != 1) badCharge++;
                    defFoulFreeThrows++;
                    _ = freeThrowStub.Receive(ftc);
                    break;

                case Continue { Next: ContinuationKind.ResolveJumpBall }:
                    counts[FreeThrowReboundOutcome.JumpBall]++;
                    if (delta != 0) badCharge++;
                    jumpBalls++;
                    break;

                default:
                    unrecognized++;
                    break;
            }
        }

        var n = (double)cfg.BatchSize;
        var ratesOk = true;
        Console.WriteLine("  Roll M outcomes:");
        foreach (var (outcome, weight) in pieM.Slices)
        {
            var observed = counts[outcome] / n;
            var gap = Math.Abs(observed - weight);
            var pass = gap <= cfg.RateTolerance;
            ratesOk &= pass;
            Console.WriteLine($"    {outcome,-24} observed={observed:P3}  expected={weight:P3}  gap={gap:P3}  {(pass ? "ok" : "FAIL")}");
        }

        Console.WriteLine("\n  routing per outcome:");
        Console.WriteLine($"    DefensiveRebound       -> transition terminal (defense) {defTransition,8:N0}  {(defTransition > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OffensiveRebound       -> Roll K cont (FreeThrow source) {offReboundCont,8:N0}  {(offReboundCont > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    LooseBallFoulOnOffense -> dead-ball terminal (defense)   {offFoulDead,8:N0}  {(offFoulDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffOffense  -> dead-ball terminal (defense)   {oobOffDead,8:N0}  {(oobOffDead > 0 ? "ok" : "NONE")}");
        Console.WriteLine($"    OutOfBoundsOffDefense  -> sideline inbound (no charge)   {oobDefSideline,8:N0}  {(oobDefSideline > 0 ? "ok" : "NONE")}");
        // The loose-ball-defense foul splits on the bonus: sideline below it, FTs once
        // the fresh game crosses the threshold mid-batch (§2a).
        var defFoulTotal = defFoulSideline + defFoulFreeThrows;
        Console.WriteLine($"    LooseBallFoulOnDefense -> SidelineInbound (below bonus)  {defFoulSideline,8:N0}");
        Console.WriteLine($"                           -> ResolveFreeThrows (in bonus)   {defFoulFreeThrows,8:N0}");
        Console.WriteLine($"    JumpBall               -> arrow node                     {jumpBalls,8:N0}  {(jumpBalls > 0 ? "ok" : "NONE")}");

        var allArms = defTransition > 0 && offReboundCont > 0 && offFoulDead > 0 && oobOffDead > 0
                      && oobDefSideline > 0 && defFoulTotal > 0 && jumpBalls > 0;
        var routedOk = unrecognized == 0;
        var consequenceOk = badConsequence == 0;
        var chargeOk = badCharge == 0;
        Console.WriteLine($"\n  zero unrouted / unexpected exits: {unrecognized} -> {(routedOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  consequence/ticket correct on every terminal+continue: bad={badConsequence} -> {(consequenceOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  foul-charge discipline (only loose-ball-defense charges): bad={badCharge} -> {(chargeOk ? "ok" : "FAIL")}");
        Console.WriteLine($"  §2a: loose-ball-defense crossed the bonus mid-batch (sideline -> FT): {(defFoulSideline > 0 && defFoulFreeThrows > 0 ? "ok" : "FAIL")}");
        Console.WriteLine($"  all seven Roll M arms reached: {(allArms ? "ok" : "FAIL")}");

        var ok = ratesOk && routedOk && consequenceOk && chargeOk && allArms
                 && defFoulSideline > 0 && defFoulFreeThrows > 0;
        Console.WriteLine($"  Roll M free-throw rebound resolution: {(ok ? "ok" : "FAIL")}");
        return ok;
    }


    // --- Context: the two FT tickets Roll M stamps SELECT the right pies downstream.
    //     (1) Roll K's generator, given OffensiveReboundSource.FreeThrow, returns the
    //     FT-specific weight set (more putback, less reset); given LiveBall (the legacy
    //     default), it returns the byte-for-byte original set. (2) Roll J's generator,
    //     given TransitionSource.FreeThrowRebound, returns the conservative run-or-not
    //     set (more Settle, less Push); given Rebound, the original set. Each is proven
    //     two ways, the RollCContextCheck pattern: the SELECTED pie's weights equal the
    //     configured set (right SET picked), and a draw sample off that pie converges to
    //     those weights (the pie is actually consumed). The two sources must also DIFFER
    //     (a null/legacy stamp can never accidentally get the FT odds). ---
    private static bool RollMContextSelectionCheck(
        RollAConfig cfg, RollKConfig cfgK, RollJConfig cfgJ, IRollJPieGenerator genJ, PossessionState state)
    {
        Console.WriteLine($"\n--- Context: Roll K + Roll J pie selection by the FT tickets ---");
        var ok = true;

        // ---- Roll K: LiveBall (legacy default) vs FreeThrow ----
        var genK = new RollKStubPieGenerator(cfgK);
        var kContexts = new (OffensiveReboundSource src, (OffensiveReboundOutcome o, double w)[] expected)[]
        {
            (OffensiveReboundSource.LiveBall, new[]
            {
                (OffensiveReboundOutcome.PutBack,          cfgK.PutBack),
                (OffensiveReboundOutcome.JumpBall,         cfgK.JumpBall),
                (OffensiveReboundOutcome.DefensiveFoul,    cfgK.DefensiveFoul),
                (OffensiveReboundOutcome.OffensiveFoul,    cfgK.OffensiveFoul),
                (OffensiveReboundOutcome.DeadBallTurnover, cfgK.DeadBallTurnover),
                (OffensiveReboundOutcome.LiveBallTurnover, cfgK.LiveBallTurnover),
                (OffensiveReboundOutcome.ResetOffense,     cfgK.ResetOffense),
            }),
            (OffensiveReboundSource.FreeThrow, new[]
            {
                (OffensiveReboundOutcome.PutBack,          cfgK.FreeThrowPutBack),
                (OffensiveReboundOutcome.JumpBall,         cfgK.FreeThrowJumpBall),
                (OffensiveReboundOutcome.DefensiveFoul,    cfgK.FreeThrowDefensiveFoul),
                (OffensiveReboundOutcome.OffensiveFoul,    cfgK.FreeThrowOffensiveFoul),
                (OffensiveReboundOutcome.DeadBallTurnover, cfgK.FreeThrowDeadBallTurnover),
                (OffensiveReboundOutcome.LiveBallTurnover, cfgK.FreeThrowLiveBallTurnover),
                (OffensiveReboundOutcome.ResetOffense,     cfgK.FreeThrowResetOffense),
            }),
        };

        double KPutBack(OffensiveReboundSource s) =>
            genK.Generate(state, s).Slices.First(x => x.Outcome == OffensiveReboundOutcome.PutBack).Weight;

        foreach (var (src, expected) in kContexts)
        {
            var pie = genK.Generate(state, src);
            var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var selectionOk = true;
            foreach (var (o, w) in expected)
                if (Math.Abs(pieMap[o] - w) > cfgK.Epsilon) selectionOk = false;

            var rng = new SystemRng(cfg.Seed);
            var counts = new Dictionary<OffensiveReboundOutcome, int>();
            foreach (var o in Enum.GetValues<OffensiveReboundOutcome>()) counts[o] = 0;
            for (var i = 0; i < cfg.BatchSize; i++) counts[pie.Roll(rng.NextUnitInterval())]++;

            var ratesOk = true;
            Console.WriteLine($"  Roll K source={src} (selection {(selectionOk ? "ok" : "FAIL")}):");
            foreach (var (o, w) in expected)
            {
                var observed = counts[o] / (double)cfg.BatchSize;
                var pass = Math.Abs(observed - w) <= cfg.RateTolerance;
                ratesOk &= pass;
                Console.WriteLine($"    {o,-18} observed={observed:P3}  expected={w:P3}  {(pass ? "ok" : "FAIL")}");
            }
            ok &= selectionOk && ratesOk;
        }
        var kDiffer = Math.Abs(KPutBack(OffensiveReboundSource.FreeThrow) - KPutBack(OffensiveReboundSource.LiveBall)) > cfgK.Epsilon;
        Console.WriteLine($"  Roll K FT pie differs from live-ball pie (PutBack): {(kDiffer ? "ok" : "FAIL")}");
        ok &= kDiffer;

        // ---- Roll J: Rebound vs FreeThrowRebound vs Steal (at neutral modifiers) ----
        var jContexts = new (TransitionContext ctx, (TransitionOutcome o, double w)[] expected)[]
        {
            (TransitionContext.Rebound, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.Settle),
                (TransitionOutcome.Push,          cfgJ.Push),
                (TransitionOutcome.Turnover,      cfgJ.Turnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.DefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.JumpBall),
            }),

            (TransitionContext.FreeThrowRebound, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.FreeThrowSettle),
                (TransitionOutcome.Push,          cfgJ.FreeThrowPush),
                (TransitionOutcome.Turnover,      cfgJ.FreeThrowTurnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.FreeThrowDefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.FreeThrowJumpBall),
            }),

            // Null-origin steal → legacy StealPush/StealSettle fallback.
            (TransitionContext.Steal, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.StealSettle),
                (TransitionOutcome.Push,          cfgJ.StealPush),
                (TransitionOutcome.Turnover,      cfgJ.StealTurnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.StealDefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.StealJumpBall),
            }),

            // Phase 28 split: BackcourtVictim (high-run) baseline.
            (new TransitionContext(TransitionSource.Steal) { Origin = StealOrigin.BackcourtVictim }, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.BackcourtVictimSettle),
                (TransitionOutcome.Push,          cfgJ.BackcourtVictimPush),
                (TransitionOutcome.Turnover,      cfgJ.BackcourtVictimTurnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.BackcourtVictimDefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.BackcourtVictimJumpBall),
            }),

            // Phase 28 split: FrontcourtVictim (low-run) baseline.
            (new TransitionContext(TransitionSource.Steal) { Origin = StealOrigin.FrontcourtVictim }, new[]
            {
                (TransitionOutcome.Settle,        cfgJ.FrontcourtVictimSettle),
                (TransitionOutcome.Push,          cfgJ.FrontcourtVictimPush),
                (TransitionOutcome.Turnover,      cfgJ.FrontcourtVictimTurnover),
                (TransitionOutcome.DefensiveFoul, cfgJ.FrontcourtVictimDefensiveFoul),
                (TransitionOutcome.JumpBall,      cfgJ.FrontcourtVictimJumpBall),
            }),
        };

        double JPush(TransitionContext c) =>
            genJ.Generate(c).Slices.First(x => x.Outcome == TransitionOutcome.Push).Weight;

        foreach (var (ctx, expected) in jContexts)
        {
            var pie = genJ.Generate(ctx);
            var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);

            var selectionOk = true;
            foreach (var (o, w) in expected)
                if (Math.Abs(pieMap[o] - w) > cfgJ.Epsilon) selectionOk = false;

            var rng = new SystemRng(cfg.Seed);
            var counts = new Dictionary<TransitionOutcome, int>();
            foreach (var o in Enum.GetValues<TransitionOutcome>()) counts[o] = 0;
            for (var i = 0; i < cfg.BatchSize; i++) counts[pie.Roll(rng.NextUnitInterval())]++;

            var ratesOk = true;
            Console.WriteLine($"  Roll J source={ctx.Source} (selection {(selectionOk ? "ok" : "FAIL")}):");
            foreach (var (o, w) in expected)
            {
                var observed = counts[o] / (double)cfg.BatchSize;
                var pass = Math.Abs(observed - w) <= cfg.RateTolerance;
                ratesOk &= pass;
                Console.WriteLine($"    {o,-18} observed={observed:P3}  expected={w:P3}  {(pass ? "ok" : "FAIL")}");
            }
            ok &= selectionOk && ratesOk;
        }
        var jDiffer = Math.Abs(JPush(TransitionContext.FreeThrowRebound) - JPush(TransitionContext.Rebound)) > cfgJ.Epsilon;
        Console.WriteLine($"  Roll J FT pie differs from rebound pie (Push): {(jDiffer ? "ok" : "FAIL")}");
        ok &= jDiffer;

        // Steal is the third source (Contextification #3): its pie must DIFFER from the
        // rebound pie, and the run-happiness must order Steal > Rebound > FreeThrowRebound
        // on Push (the locked pie intent — a steal runs hardest, a missed FT least).
        var jStealDiffer = Math.Abs(JPush(TransitionContext.Steal) - JPush(TransitionContext.Rebound)) > cfgJ.Epsilon;
        var jPushOrderOk = JPush(TransitionContext.Steal) > JPush(TransitionContext.Rebound)
                           && JPush(TransitionContext.Rebound) > JPush(TransitionContext.FreeThrowRebound);
        Console.WriteLine($"  Roll J Steal pie differs from rebound pie (Push): {(jStealDiffer ? "ok" : "FAIL")}");
        Console.WriteLine(
            $"  Roll J Push order Steal({JPush(TransitionContext.Steal):P1}) > Rebound({JPush(TransitionContext.Rebound):P1}) " +
            $"> FT({JPush(TransitionContext.FreeThrowRebound):P1}): {(jPushOrderOk ? "ok" : "FAIL")}");
        ok &= jStealDiffer && jPushOrderOk;

        // Phase 28: steal-split direction — BackcourtVictim > FrontcourtVictim >= Rebound (A5 / role-flip).
        var bcCtx = new TransitionContext(TransitionSource.Steal) { Origin = StealOrigin.BackcourtVictim };
        var fcCtx = new TransitionContext(TransitionSource.Steal) { Origin = StealOrigin.FrontcourtVictim };
        var jSplitOk = JPush(bcCtx) > JPush(fcCtx) && JPush(fcCtx) >= JPush(TransitionContext.Rebound);
        Console.WriteLine(
            $"  Phase 28 steal split: BC({JPush(bcCtx):P1}) > FC({JPush(fcCtx):P1}) >= Rebound({JPush(TransitionContext.Rebound):P1}): " +
            $"{(jSplitOk ? "ok" : "FAIL")}");
        ok &= jSplitOk;

        Console.WriteLine($"  Roll M context selection: {(ok ? "ok" : "FAIL")}");
        return ok;
    }


    // --- Convergence: the putback <-> rebound LOOP bleeds out. Roll K is the first
    //     possession-EXTENDING node, so a single resolver walk can cycle PutBack ->
    //     Roll H -> miss -> Roll I -> OffensiveRebound -> PutBack ... (and reset back
    //     through Roll E). This is the §2a "watch the accumulation across iterations"
    //     check applied to a RE-ENTRANT chain: the shared thing changing is the
    //     possession's own depth. Drive many possessions that ENTER Roll K (feed the
    //     resolver a fully-stamped Continue(ResolveOffensiveRebound)), read
    //     RoutingOutcome.PutbackAttempts, and assert the survival distribution
    //     reachedAtLeast[n] (# possessions with >= n putbacks) STRICTLY DECREASES on
    //     its populated levels, the max is comfortably bounded (< 20), and the
    //     resolver's loud iteration guard is NEVER hit (the harness completing IS that
    //     proof — a non-converging walk would have thrown). ---
    private static bool OffensiveReboundConvergenceCheck(RollAConfig cfg, PossessionState state)
    {
        Console.WriteLine($"\n--- Convergence: {cfg.BatchSize:N0} offensive rebounds driven through the resolver loop ---");
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

        var maxAttempts = 0;
        var hist = new Dictionary<int, int>(); // putbackAttempts value -> # possessions

        // The harness completing this loop at all is the convergence proof: if any
        // possession failed to converge, the resolver's IterationCeiling guard would
        // have THROWN out of resolver.Route below, crashing the harness loudly.
        for (var i = 0; i < cfg.BatchSize; i++)
        {
            // Build a fully-stamped post-miss state and ENTER Roll K directly by
            // handing the resolver the offensive-rebound continuation — the resolver
            // then walks the whole loop internally and returns once it ends/parks.
            var sel = ((Continue)RollE.Execute(state, pieE, new double[5], new double[5], 0.0, 0.0, 0.0, 0.0, game, rng)).State;
            var zoned = ((Continue)RollG.Execute(sel, genG.Generate(sel), 0.0, rng)).State;
            var stamped = zoned with { Result = ShotResult.Miss };
            var entry = new Continue(ContinuationKind.ResolveOffensiveRebound, stamped);

            var outcome = resolver.Route(entry);
            var k = outcome.PutbackAttempts;
            hist[k] = hist.TryGetValue(k, out var v) ? v + 1 : 1;
            if (k > maxAttempts) maxAttempts = k;
        }

        // Survival: reachedAtLeast[n] = # possessions with putbackAttempts >= n.
        var reachedAtLeast = new int[maxAttempts + 2];
        foreach (var (k, count) in hist)
            for (var n = 0; n <= k; n++) reachedAtLeast[n] += count;

        Console.WriteLine("  putback-depth distribution (exactly n / at least n):");
        for (var n = 0; n <= maxAttempts; n++)
        {
            var exactly = hist.TryGetValue(n, out var e) ? e : 0;
            Console.WriteLine($"    n={n,2}: exactly {exactly,8:N0}   at-least {reachedAtLeast[n],8:N0}");
        }

        // Strict decay on the populated levels: each deeper cycle is rarer than the
        // last. Asserted where the SHALLOWER level still has a meaningful sample
        // (>= 20) so sampling noise at the sparse tail can't flake the check; the
        // tail is reported above for the eye.
        const int floor = 20;
        var strictDecay = true;
        for (var n = 1; n < maxAttempts; n++)
            if (reachedAtLeast[n] >= floor && !(reachedAtLeast[n + 1] < reachedAtLeast[n]))
                strictDecay = false;

        var boundedMax = maxAttempts < 20;

        Console.WriteLine($"\n  max putback depth observed: {maxAttempts} -> {(boundedMax ? "ok (< 20)" : "FAIL (>= 20)")}");
        Console.WriteLine($"  survival strictly decreasing on populated levels: {(strictDecay ? "ok" : "FAIL")}");
        Console.WriteLine($"  iteration guard never hit (harness reached here): ok");

        return strictDecay && boundedMax;
    }


    // =========================================================================
    // Phase 10 — rebound door check (Roll G matchup-aware rebounding)
    // =========================================================================
    // Deterministic assertions — NOT a Monte Carlo batch. Proves the two-touchpoint
    // model bends the off-share in the correct direction across seven sub-checks:
    // (a) neutral, (b) size check, (c) skill check, (d) positional weight isolated,
    // (e) shooter nerf + zone gate, (f) flat slivers unchanged, (g) block baseline.
    // Mirrors the structure of Phase9LocationDoorCheck exactly.
    // =========================================================================

    private static bool Phase10ReboundDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 10: Rebound door (Roll I matchup-aware) ---");
        var pass = true;

        var cfgI       = RollIConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Config baselines.
        var liveMass    = cfgI.DefensiveRebound + cfgI.OffensiveRebound;
        var liveBase    = cfgI.OffensiveRebound / liveMass;   // ≈ 0.290
        var blockMass   = cfgI.BlockDefensiveRebound + cfgI.BlockOffensiveRebound;
        var blockBase   = cfgI.BlockOffensiveRebound / blockMass; // ≈ 0.390

        // Helper: build a player with all attributes at baseline b; override specific ones.
        static Player Mk(int b,
                         int? str = null, int? height = null, int? offReb = null,
                         int? defReb = null, int? postDef = null, int? wingspan = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = offReb  ?? b,
                PerimeterDefense = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = defReb  ?? b,
                Steals = b,
                Height = height ?? b, Wingspan = wingspan ?? b, Weight = b,
                Strength = str ?? b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline = b, HelpDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: build a minimal GameState, seat offense in Home 1–5, defense in Away 1–5.
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

        // Helper: build a PossessionState for Home offense, Away defense, with a stamped
        // shooter slot (slot 1 by default) and zone.
        static PossessionState St(GameState g, ShotLocation zone, int shooterSlot = 1)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(shooterSlot),
                ShotType: zone);

        // Helper: generate the pie and return the seven weights as a dict.
        static Dictionary<ReboundOutcome, double> Split(
            RollIConfig cfgI, MatchupConfig cfgMatchup,
            GameState g, PossessionState state,
            ReboundSource src = ReboundSource.LiveBall)
        {
            var gen = new RollIGenerator(cfgI, cfgMatchup, g);
            var pie = gen.Generate(state, src);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // ── (a) Neutral matchup: all-50 teams, Rim zone (nerf off) ──────────
        Console.WriteLine("  (a) Neutral (all-50 teams, Rim zone): off-share == baseline:");
        bool aOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);
            var d  = Split(cfgI, cfgMatchup, g, st);

            var offShare = d[ReboundOutcome.OffensiveRebound] / liveMass;
            var isNeutral = Math.Abs(offShare - liveBase) < Eps;

            // Other five slivers must equal config exactly.
            var flatOk =
                Math.Abs(d[ReboundOutcome.LooseBallFoulOnDefense] - cfgI.LooseBallFoulOnDefense) < Eps &&
                Math.Abs(d[ReboundOutcome.LooseBallFoulOnOffense] - cfgI.LooseBallFoulOnOffense) < Eps &&
                Math.Abs(d[ReboundOutcome.OutOfBoundsOffOffense]  - cfgI.OutOfBoundsOffOffense)  < Eps &&
                Math.Abs(d[ReboundOutcome.OutOfBoundsOffDefense]  - cfgI.OutOfBoundsOffDefense)  < Eps &&
                Math.Abs(d[ReboundOutcome.JumpBall]               - cfgI.JumpBall)               < Eps;

            aOk = isNeutral && flatOk;
            Console.WriteLine($"    off-share={offShare:F8}  baseline={liveBase:F8}  neutral? {(isNeutral ? "OK" : "FAIL")}");
            Console.WriteLine($"    five flat slivers == config: {(flatOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ── (b) Size check: offense big (85), defense small (35), equal ratings ──
        Console.WriteLine("  (b) Size check (off Str/Ht=85 vs def Str/Ht=35, equal ratings):");
        bool bOk;
        try
        {
            var off5 = new[] { Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85) };
            var def5 = new[] { Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);
            var d  = Split(cfgI, cfgMatchup, g, st);

            var offShare = d[ReboundOutcome.OffensiveRebound] / liveMass;
            bOk = offShare > liveBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={liveBase:F6}  rises above baseline? {(bOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ── (c) Skill check: equal size, off OffReb=85 vs def DefReb=35 ────
        Console.WriteLine("  (c) Skill check (equal size, off OffReb=85 vs def DefReb=35):");
        bool cOk;
        try
        {
            var off5 = new[] { Mk(50, offReb: 85), Mk(50, offReb: 85), Mk(50, offReb: 85),
                               Mk(50, offReb: 85), Mk(50, offReb: 85) };
            var def5 = new[] { Mk(50, defReb: 35), Mk(50, defReb: 35), Mk(50, defReb: 35),
                               Mk(50, defReb: 35), Mk(50, defReb: 35) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);
            var d  = Split(cfgI, cfgMatchup, g, st);

            var offShare = d[ReboundOutcome.OffensiveRebound] / liveMass;
            cOk = offShare > liveBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={liveBase:F6}  rises above baseline? {(cOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ── (d) Positional weight — isolated from size check ────────────────
        // Both teams: identical height AND strength (so ReboundPhysical is a wash).
        // Separate them with PostDefense alone (in Postness but NOT in ReboundPhysical).
        // Offense A: concentrate OffReb in high-PostDef player (stark 90 vs 10).
        // Offense B: spread same total flat. Expected: A > B (positional weight rewarded).
        Console.WriteLine("  (d) Positional weight isolated (PostDefense only, equal Str/Height):");
        bool dOk;
        try
        {
            // Offense A: post player has PostDef=90 and OffReb=90; guards have PostDef=10 and OffReb=10.
            var offA = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 90, postDef: 90),  // the post
            };
            // Total OffReb in A: 4*10 + 90 = 130. Spread flat: 5*26 = 130.
            var offB = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
            };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };

            // Shooter at slot 1 (a guard with low PostDef), nerf OFF (Rim zone).
            var gA = BuildGame(offA, def5);
            var stA = St(gA, ShotLocation.Rim, shooterSlot: 1);
            var dA = Split(cfgI, cfgMatchup, gA, stA);
            var shareA = dA[ReboundOutcome.OffensiveRebound] / liveMass;

            var gB = BuildGame(offB, def5);
            var stB = St(gB, ShotLocation.Rim, shooterSlot: 1);
            var dB = Split(cfgI, cfgMatchup, gB, stB);
            var shareB = dB[ReboundOutcome.OffensiveRebound] / liveMass;

            dOk = shareA > shareB;
            Console.WriteLine($"    concentrated (OffReb in big): share={shareA:F6}");
            Console.WriteLine($"    flat spread:                   share={shareB:F6}");
            Console.WriteLine($"    concentrated > flat: {(dOk ? "OK — positional weight rewards OffReb in bigs" : "FAIL")}");
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ── (e) Shooter nerf + zone gate ────────────────────────────────────
        // Identical matchup; Three (nerf on) vs Rim (nerf off). Shooter is slot 1.
        Console.WriteLine("  (e) Shooter nerf: Three (nerf on) yields lower off-share than Rim (nerf off):");
        bool eOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g = BuildGame(off5, def5);

            var stThree = St(g, ShotLocation.Three, shooterSlot: 1);
            var dThree  = Split(cfgI, cfgMatchup, g, stThree);
            var shareThree = dThree[ReboundOutcome.OffensiveRebound] / liveMass;

            var stRim = St(g, ShotLocation.Rim, shooterSlot: 1);
            var dRim  = Split(cfgI, cfgMatchup, g, stRim);
            var shareRim = dRim[ReboundOutcome.OffensiveRebound] / liveMass;

            eOk = shareThree < shareRim;
            Console.WriteLine($"    Three (nerf on): off-share={shareThree:F6}");
            Console.WriteLine($"    Rim  (nerf off): off-share={shareRim:F6}");
            Console.WriteLine($"    Three < Rim: {(eOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ── (f) Other arms flat across (b)–(e) ─────────────────────────────
        // Only Def/Off should move; the five flat slivers must equal config in every case.
        Console.WriteLine("  (f) Flat slivers unchanged across (b)–(e) cases:");
        bool fOk = true;
        try
        {
            var testCases = new (string label, Player[] off, Player[] def, ShotLocation zone)[]
            {
                ("size-check (b)",
                    new[] { Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85) },
                    new[] { Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35) },
                    ShotLocation.Rim),
                ("skill-check (c)",
                    new[] { Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85) },
                    new[] { Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35) },
                    ShotLocation.Rim),
                ("shooter-nerf Three (e)",
                    new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) },
                    new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) },
                    ShotLocation.Three),
            };

            foreach (var (label, off, def, zone) in testCases)
            {
                var g  = BuildGame(off, def);
                var st = St(g, zone, shooterSlot: 1);
                var d  = Split(cfgI, cfgMatchup, g, st);
                var rowOk =
                    Math.Abs(d[ReboundOutcome.LooseBallFoulOnDefense] - cfgI.LooseBallFoulOnDefense) < Eps &&
                    Math.Abs(d[ReboundOutcome.LooseBallFoulOnOffense] - cfgI.LooseBallFoulOnOffense) < Eps &&
                    Math.Abs(d[ReboundOutcome.OutOfBoundsOffOffense]  - cfgI.OutOfBoundsOffOffense)  < Eps &&
                    Math.Abs(d[ReboundOutcome.OutOfBoundsOffDefense]  - cfgI.OutOfBoundsOffDefense)  < Eps &&
                    Math.Abs(d[ReboundOutcome.JumpBall]               - cfgI.JumpBall)               < Eps;
                fOk &= rowOk;
                Console.WriteLine($"    {label}: flat slivers == config: {(rowOk ? "OK" : "FAIL")}");
            }
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ── (g) Block source baseline preserved ─────────────────────────────
        // At neutral, the block source's off-share ≈ blockBase (≈0.390), distinct from live-miss (≈0.290).
        Console.WriteLine("  (g) Block source baseline (neutral matchup):");
        bool gOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(off5, def5);
            var st = St(g, ShotLocation.Rim);

            var dBlock = Split(cfgI, cfgMatchup, g, st, ReboundSource.Block);
            var blockOffShare = dBlock[ReboundOutcome.OffensiveRebound] / blockMass;

            var atBlockBase  = Math.Abs(blockOffShare - blockBase) < Eps;
            var distinctFrom = Math.Abs(blockBase - liveBase) > 0.05;
            gOk = atBlockBase && distinctFrom;
            Console.WriteLine($"    block off-share={blockOffShare:F8}  blockBase={blockBase:F8}  matches? {(atBlockBase ? "OK" : "FAIL")}");
            Console.WriteLine($"    block baseline distinct from live-miss (>0.05 gap): {(distinctFrom ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        // ── (h) Wingspan direction in the battle (Phase 35) ─────────────────────
        // Two otherwise-identical all-50 lineups; vary ONLY wingspan between them.
        // Longer DEFENSE (wingspan=80) vs baseline offense (50) → off-share DROPS.
        // Longer OFFENSE (wingspan=80) vs baseline defense (50) → off-share RISES.
        // (All other attributes equal, so ReboundPhysical height/strength cancel out.)
        Console.WriteLine("  (h) Phase 35 wingspan direction (battle): longer defense lowers off-share; longer offense raises it:");
        bool hOk;
        try
        {
            var baseOff5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var baseDef5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var longDef5 = new[] { Mk(50, wingspan: 80), Mk(50, wingspan: 80), Mk(50, wingspan: 80),
                                   Mk(50, wingspan: 80), Mk(50, wingspan: 80) };
            var longOff5 = new[] { Mk(50, wingspan: 80), Mk(50, wingspan: 80), Mk(50, wingspan: 80),
                                   Mk(50, wingspan: 80), Mk(50, wingspan: 80) };

            var stRim = ShotLocation.Rim;

            var gNeutral   = BuildGame(baseOff5, baseDef5);
            var stNeutral  = St(gNeutral, stRim);
            var dNeutral   = Split(cfgI, cfgMatchup, gNeutral, stNeutral);
            var shareNeutral = dNeutral[ReboundOutcome.OffensiveRebound] / liveMass;

            var gLongDef   = BuildGame(baseOff5, longDef5);
            var stLongDef  = St(gLongDef, stRim);
            var dLongDef   = Split(cfgI, cfgMatchup, gLongDef, stLongDef);
            var shareLongDef = dLongDef[ReboundOutcome.OffensiveRebound] / liveMass;

            var gLongOff   = BuildGame(longOff5, baseDef5);
            var stLongOff  = St(gLongOff, stRim);
            var dLongOff   = Split(cfgI, cfgMatchup, gLongOff, stLongOff);
            var shareLongOff = dLongOff[ReboundOutcome.OffensiveRebound] / liveMass;

            hOk = shareLongDef < shareNeutral && shareLongOff > shareNeutral;
            Console.WriteLine($"    neutral off-share={shareNeutral:F6}");
            Console.WriteLine($"    longer defense:    off-share={shareLongDef:F6}  (should be < neutral: {(shareLongDef < shareNeutral ? "OK" : "FAIL")})");
            Console.WriteLine($"    longer offense:    off-share={shareLongOff:F6}  (should be > neutral: {(shareLongOff > shareNeutral ? "OK" : "FAIL")})");
        }
        catch (Exception ex) { hOk = false; Console.WriteLine($"  FAIL  (h) threw: {ex.Message}"); }
        pass &= hOk;

        Console.WriteLine(pass ? "  Phase 10 PASSED." : "  Phase 10 FAILED.");
        return pass;
    }


    // =========================================================================
    // Phase 11 — FT rebound door (Roll M matchup-aware)
    // Mirrors Phase10ReboundDoorCheck. Key differences from the Roll I template:
    //   • One source only (no ReboundSource arg; no block baseline)
    //   • No shooter nerf (shooterIdx=-1 always; St() stamps no slot/zone)
    //   • Baseline ≈ 0.197 (more defensive than Roll I's live-miss ≈ 0.290)
    //   • Sub-check (e) = no-shooter invariance (replaces Roll I's nerf check)
    //   • Sub-check (g) = FT baseline < field-goal baseline (replaces block check)
    // =========================================================================

    private static bool Phase11FreeThrowReboundDoorCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 11: FT rebound door (Roll M matchup-aware) ---");
        var pass = true;

        var cfgM       = RollMConfig.Load(configPath);
        var cfgI       = RollIConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        const double Eps = 1e-9;

        // Config baselines.
        var mMass = cfgM.DefensiveRebound + cfgM.OffensiveRebound;
        var mBase = cfgM.OffensiveRebound / mMass;   // ≈ 0.197

        // Helper: build a player with all attributes at baseline b; override specific ones.
        static Player Mk(int b,
                         int? str = null, int? height = null, int? offReb = null,
                         int? defReb = null, int? postDef = null)
            => new Player("p")
            {
                Outside = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = offReb  ?? b,
                PerimeterDefense = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = defReb  ?? b,
                Steals = b,
                Height = height ?? b, Wingspan = b, Weight = b,
                Strength = str ?? b, Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline = b, HelpDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: build a minimal GameState, seat offense in Home 1–5, defense in Away 1–5.
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

        // Helper: build a PossessionState for Home offense, Away defense.
        // ⚠ Do NOT stamp SelectedSlot or ShotType — Roll M reads neither.
        // Leaving them null proves the generator's only fallback is empty rosters,
        // not a null-slot check (Divergence 3).
        static PossessionState St()
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: null, ShotType: null);

        // Helper: construct RollMGenerator, call Generate, return seven weights as a dict.
        static Dictionary<FreeThrowReboundOutcome, double> Split(
            RollMConfig cfgM, MatchupConfig cfgMatchup, GameState g, PossessionState state)
        {
            var gen = new RollMGenerator(cfgM, cfgMatchup, g);
            var pie = gen.Generate(state);
            return pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
        }

        // ── (a) Neutral: all-50 teams, no slot → off-share == Roll M baseline ────
        Console.WriteLine("  (a) Neutral (all-50 teams, no slot): off-share == Roll M baseline:");
        bool aOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g  = BuildGame(off5, def5);
            var st = St();
            var d  = Split(cfgM, cfgMatchup, g, st);

            var offShare  = d[FreeThrowReboundOutcome.OffensiveRebound] / mMass;
            var isNeutral = Math.Abs(offShare - mBase) < Eps;

            var flatOk =
                Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnDefense] - cfgM.LooseBallFoulOnDefense) < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnOffense] - cfgM.LooseBallFoulOnOffense) < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffOffense]  - cfgM.OutOfBoundsOffOffense)  < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffDefense]  - cfgM.OutOfBoundsOffDefense)  < Eps &&
                Math.Abs(d[FreeThrowReboundOutcome.JumpBall]               - cfgM.JumpBall)               < Eps;

            aOk = isNeutral && flatOk;
            Console.WriteLine($"    off-share={offShare:F8}  baseline={mBase:F8}  neutral? {(isNeutral ? "OK" : "FAIL")}");
            Console.WriteLine($"    five flat slivers == config: {(flatOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { aOk = false; Console.WriteLine($"  FAIL  (a) threw: {ex.Message}"); }
        pass &= aOk;

        // ── (b) Size check: bigger offense → off-share rises ────────────────────
        Console.WriteLine("  (b) Size check (off Str/Height=85 vs def Str/Height=35):");
        bool bOk;
        try
        {
            var off5 = new[] { Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85), Mk(50, str: 85, height: 85),
                               Mk(50, str: 85, height: 85) };
            var def5 = new[] { Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35), Mk(50, str: 35, height: 35),
                               Mk(50, str: 35, height: 35) };
            var g  = BuildGame(off5, def5);
            var st = St();
            var d  = Split(cfgM, cfgMatchup, g, st);

            var offShare = d[FreeThrowReboundOutcome.OffensiveRebound] / mMass;
            bOk = offShare > mBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={mBase:F6}  rises above baseline? {(bOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { bOk = false; Console.WriteLine($"  FAIL  (b) threw: {ex.Message}"); }
        pass &= bOk;

        // ── (c) Skill check: better rebounders → off-share rises ────────────────
        Console.WriteLine("  (c) Skill check (equal size, off OffReb=85 vs def DefReb=35):");
        bool cOk;
        try
        {
            var off5 = new[] { Mk(50, offReb: 85), Mk(50, offReb: 85), Mk(50, offReb: 85),
                               Mk(50, offReb: 85), Mk(50, offReb: 85) };
            var def5 = new[] { Mk(50, defReb: 35), Mk(50, defReb: 35), Mk(50, defReb: 35),
                               Mk(50, defReb: 35), Mk(50, defReb: 35) };
            var g  = BuildGame(off5, def5);
            var st = St();
            var d  = Split(cfgM, cfgMatchup, g, st);

            var offShare = d[FreeThrowReboundOutcome.OffensiveRebound] / mMass;
            cOk = offShare > mBase;
            Console.WriteLine($"    off-share={offShare:F6}  baseline={mBase:F6}  rises above baseline? {(cOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { cOk = false; Console.WriteLine($"  FAIL  (c) threw: {ex.Message}"); }
        pass &= cOk;

        // ── (d) Positional weight isolated (PostDefense, no size diff, no shooter) ─
        // Both teams: identical Strength and Height (ReboundPhysical is a wash).
        // Separate with PostDefense alone. Offense A: concentrate OffReb in high-PostDef
        // player; Offense B: same total OffReb spread flat. Expected: A > B.
        // ⚠ Cleaner than Phase 10 (d) — no shooter slot to choose.
        Console.WriteLine("  (d) Positional weight isolated (PostDefense only, equal Str/Height, no shooter slot):");
        bool dOk;
        try
        {
            var offA = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 10, postDef: 10),
                Mk(50, str: 50, height: 50, offReb: 90, postDef: 90),  // the post
            };
            // Total OffReb in A: 4*10 + 90 = 130. Spread flat: 5*26 = 130.
            var offB = new[]
            {
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
                Mk(50, str: 50, height: 50, offReb: 26, postDef: 50),
            };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var st = St();

            var gA = BuildGame(offA, def5);
            var dA = Split(cfgM, cfgMatchup, gA, st);
            var shareA = dA[FreeThrowReboundOutcome.OffensiveRebound] / mMass;

            var gB = BuildGame(offB, def5);
            var dB = Split(cfgM, cfgMatchup, gB, st);
            var shareB = dB[FreeThrowReboundOutcome.OffensiveRebound] / mMass;

            dOk = shareA > shareB;
            Console.WriteLine($"    concentrated (OffReb in post): share={shareA:F6}");
            Console.WriteLine($"    flat spread:                   share={shareB:F6}");
            Console.WriteLine($"    concentrated > flat: {(dOk ? "OK — positional weight rewards OffReb in bigs on FT glass" : "FAIL")}");
        }
        catch (Exception ex) { dOk = false; Console.WriteLine($"  FAIL  (d) threw: {ex.Message}"); }
        pass &= dOk;

        // ── (e) No-shooter invariance ────────────────────────────────────────────
        // ⚠ Replaces Phase 10's shooter-nerf sub-check. Prove Roll M is structurally
        // slot-blind: identical matchup with null slot vs. stamped slot+zone must
        // produce byte-identical off-shares. Positive proof of Divergences 2 and 3.
        Console.WriteLine("  (e) No-shooter invariance: null-slot and stamped-slot produce identical off-share:");
        bool eOk;
        try
        {
            var off5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var def5 = new[] { Mk(50), Mk(50), Mk(50), Mk(50), Mk(50) };
            var g = BuildGame(off5, def5);

            // No slot (normal bonus FT trip path).
            var stNoSlot = St();

            // Stamped slot + nerf-eligible zone (shooting-foul FT trip path).
            var stWithSlot = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(1),
                ShotType: ShotLocation.Three);

            var dNoSlot   = Split(cfgM, cfgMatchup, g, stNoSlot);
            var dWithSlot = Split(cfgM, cfgMatchup, g, stWithSlot);

            var shareNoSlot   = dNoSlot[FreeThrowReboundOutcome.OffensiveRebound]   / mMass;
            var shareWithSlot = dWithSlot[FreeThrowReboundOutcome.OffensiveRebound] / mMass;

            eOk = Math.Abs(shareNoSlot - shareWithSlot) < Eps;
            Console.WriteLine($"    null-slot   off-share = {shareNoSlot:F10}");
            Console.WriteLine($"    stamped-slot off-share = {shareWithSlot:F10}");
            Console.WriteLine($"    identical? {(eOk ? "OK — Roll M is slot-blind" : "FAIL — slot is affecting Roll M (bug)")}");
        }
        catch (Exception ex) { eOk = false; Console.WriteLine($"  FAIL  (e) threw: {ex.Message}"); }
        pass &= eOk;

        // ── (f) Other arms flat across (b)–(e) ──────────────────────────────────
        Console.WriteLine("  (f) Flat slivers unchanged across (b)–(c) cases:");
        bool fOk = true;
        try
        {
            var testCases = new (string label, Player[] off, Player[] def)[]
            {
                ("size-check (b)",
                    new[] { Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85), Mk(50,str:85,height:85) },
                    new[] { Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35), Mk(50,str:35,height:35) }),
                ("skill-check (c)",
                    new[] { Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85), Mk(50,offReb:85) },
                    new[] { Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35), Mk(50,defReb:35) }),
            };

            foreach (var (label, off, def) in testCases)
            {
                var g  = BuildGame(off, def);
                var st = St();
                var d  = Split(cfgM, cfgMatchup, g, st);
                var rowOk =
                    Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnDefense] - cfgM.LooseBallFoulOnDefense) < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.LooseBallFoulOnOffense] - cfgM.LooseBallFoulOnOffense) < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffOffense]  - cfgM.OutOfBoundsOffOffense)  < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.OutOfBoundsOffDefense]  - cfgM.OutOfBoundsOffDefense)  < Eps &&
                    Math.Abs(d[FreeThrowReboundOutcome.JumpBall]               - cfgM.JumpBall)               < Eps;
                fOk &= rowOk;
                Console.WriteLine($"    {label}: flat slivers == config: {(rowOk ? "OK" : "FAIL")}");
            }
        }
        catch (Exception ex) { fOk = false; Console.WriteLine($"  FAIL  (f) threw: {ex.Message}"); }
        pass &= fOk;

        // ── (g) FT baseline < field-goal baseline ────────────────────────────────
        // ⚠ Replaces Phase 10's block-source sub-check.
        // At neutral, Roll M's off-share (≈ 0.197) must be strictly lower than
        // Roll I's live-miss off-share (≈ 0.290) — the FT board is more defensive.
        Console.WriteLine("  (g) FT baseline (Roll M) strictly lower than field-goal baseline (Roll I live-miss):");
        bool gOk;
        try
        {
            var iMass = cfgI.DefensiveRebound + cfgI.OffensiveRebound;
            var iBase = cfgI.OffensiveRebound / iMass;

            var gOk1 = mBase < iBase;
            var gOk2 = mBase > 0.15 && mBase < 0.25;   // sensibility band
            gOk = gOk1 && gOk2;
            Console.WriteLine($"    Roll M off-share = {mBase:F6}  Roll I off-share = {iBase:F6}");
            Console.WriteLine($"    M < I: {(gOk1 ? "OK" : "FAIL")}   M in sensibility band (0.15, 0.25): {(gOk2 ? "OK" : "FAIL")}");
        }
        catch (Exception ex) { gOk = false; Console.WriteLine($"  FAIL  (g) threw: {ex.Message}"); }
        pass &= gOk;

        Console.WriteLine(pass ? "  Phase 11 PASSED." : "  Phase 11 FAILED.");
        return pass;
    }


    // ── Phase 31: offensive rebounder picker ──────────────────────────────────
    private static bool Phase31RebounderPickerCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 31: offensive rebounder picker (OffensiveRebounderPicker) ---");
        var ok = true;
        const int N = 100_000;

        var matchupCfg = MatchupConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);

        // Helper: player with all attributes at b; override specific rebounding attributes.
        static Player MkP(int id, int b,
                          int? height = null, int? postDef = null,
                          int? str    = null, int? orb     = null, int? wingspan = null)
            => new Player($"p{id}")
            {
                PlayerId             = id,
                Outside              = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing          = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation         = b, PostMoves    = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = orb     ?? b,
                PerimeterDefense     = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = b,
                Steals               = b,
                Height               = height  ?? b, Wingspan = wingspan ?? b, Weight = b,
                Strength             = str     ?? b,
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
                var slot = g.HomeLineup.SlotAt(i + 1);
                g.HomeRoster.SetStarter(slot, off[i]);
                // Away side: neutral all-50 players (picker is offense-only, away side unused).
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), MkP(i + 6, 50));
            }
            return g;
        }

        // Helper: build a state with Home offense, shot zone and shooter slot.
        static PossessionState MkState(GameState g, ShotLocation zone, int shooterSlot)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: g.HomeLineup.SlotAt(shooterSlot),
                ShotType: zone);

        // ── Sub-check 1: Direction ───────────────────────────────────────────────
        // Dominant big (slot 5, H90/PD80/S88/ORB85) vs four weak guards (H40/PD42/S44/ORB12).
        // Shooter is slot 1 on a Rim miss → no nerf on the dominant big.
        // Python-confirmed: dominant ≈70.3%, each weak ≈7.4%, multiplier ≈9.47× >> 3×.
        {
            Console.WriteLine("  Sub-check 1: direction (dominant ≈70%, each weak ≈7%, multiplier >> 3×)");
            var off = new[]
            {
                MkP(1, 40, height: 40, postDef: 42, str: 44, orb: 12),
                MkP(2, 40, height: 40, postDef: 42, str: 44, orb: 12),
                MkP(3, 40, height: 40, postDef: 42, str: 44, orb: 12),
                MkP(4, 40, height: 40, postDef: 42, str: 44, orb: 12),
                MkP(5, 50, height: 90, postDef: 80, str: 88, orb: 85),  // dominant big
            };
            var game  = BuildGame(off);
            var state = MkState(game, ShotLocation.Rim, shooterSlot: 1);  // nerf off: rim zone
            var rng   = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = OffensiveRebounderPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var dominantShare = (double)counts[4] / N;
            var maxWeakShare  = counts[..4].Max() / (double)N;
            var multiplier    = maxWeakShare > 0 ? dominantShare / maxWeakShare : double.PositiveInfinity;
            Console.WriteLine($"    dominant (slot 5): {dominantShare:P2}  max-weak: {maxWeakShare:P2}  mult: {multiplier:F2}×");
            var sub1Ok = multiplier > 3.0;
            ok &= sub1Ok;
            Console.WriteLine(sub1Ok ? "    [OK]" : "    [FAIL] multiplier did not clear 3×");
        }

        // ── Sub-check 2: Neutral ─────────────────────────────────────────────────
        // Five identical all-50 players → each slot should be ≈20%.
        {
            Console.WriteLine("  Sub-check 2: neutral (5 identical → ~20% each)");
            var off   = Enumerable.Range(1, 5).Select(i => MkP(i, 50)).ToArray();
            var game  = BuildGame(off);
            var state = MkState(game, ShotLocation.Rim, shooterSlot: 1);
            var rng   = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = OffensiveRebounderPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var shares = counts.Select(c => (double)c / N).ToArray();
            const double NeutralTol = 0.01;  // ±1%
            var sub2Ok = shares.All(s => Math.Abs(s - 0.20) <= NeutralTol);
            foreach (var (s, i) in shares.Select((s, i) => (s, i)))
                Console.WriteLine($"    slot {i + 1}: {s:P2}");
            ok &= sub2Ok;
            Console.WriteLine(sub2Ok ? "    [OK]" : "    [FAIL] shares not uniform");
        }

        // ── Sub-check 3: Buried guard ────────────────────────────────────────────
        // Small no-ORB guard (slot 1, H38/PD42/S42/ORB10) among four elite rebounders.
        // Python-confirmed: guard's share ≈3.1–3.5% — assert it stays below 6%.
        {
            Console.WriteLine("  Sub-check 3: buried guard (share < 6%)");
            var off = new[]
            {
                MkP(1, 50, height: 38, postDef: 42, str: 42, orb: 10),  // buried PG
                MkP(2, 60, height: 85, postDef: 80, str: 82, orb: 88),
                MkP(3, 60, height: 85, postDef: 80, str: 82, orb: 88),
                MkP(4, 60, height: 85, postDef: 80, str: 82, orb: 88),
                MkP(5, 60, height: 85, postDef: 80, str: 82, orb: 88),
            };
            var game  = BuildGame(off);
            // Shooter is slot 2 (an elite big), so slot 1 guard is NOT nerfed.
            var state = MkState(game, ShotLocation.Rim, shooterSlot: 2);
            var rng   = new SystemRng(42);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = OffensiveRebounderPicker.Pick(state, game, matchupCfg, rng);
                counts[pick.Number - 1]++;
            }
            var guardShare = (double)counts[0] / N;
            Console.WriteLine($"    buried guard (slot 1) share: {guardShare:P2}  (bound: < 6%)");
            var sub3Ok = guardShare < 0.06;
            ok &= sub3Ok;
            Console.WriteLine(sub3Ok ? "    [OK]" : "    [FAIL] buried guard share exceeded 6%");
        }

        // ── Sub-check 4: Shooter nerf ────────────────────────────────────────────
        // Dominant shooter-big in slot 5. Compare share: Three (nerfed) vs Rim (un-nerfed)
        // vs FT board (ShotType null → un-nerfed). Python-confirmed: ~24.8% → ~10.3% on Three.
        {
            Console.WriteLine("  Sub-check 4: shooter nerf (share drops on Three vs Rim/FT board)");
            var off = new[]
            {
                MkP(1, 50), MkP(2, 50), MkP(3, 50), MkP(4, 50),
                MkP(5, 50, height: 90, postDef: 80, str: 88, orb: 85),  // dominant big = shooter
            };
            var game = BuildGame(off);

            // Rim miss — no nerf; big is in slot 5, shooter slot 5.
            var stateRim = MkState(game, ShotLocation.Rim,   shooterSlot: 5);
            // Three miss — nerf fires on slot 5.
            var stateThree = MkState(game, ShotLocation.Three, shooterSlot: 5);
            // FT board — ShotType null → no nerf.
            var stateFt = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: game.HomeLineup.SlotAt(5),
                ShotType: null);  // free-throw miss board — no zone → no nerf

            static double ShareSlot5(PossessionState st, GameState gm, MatchupConfig mc, int n)
            {
                var rng = new SystemRng(42);
                var count = 0;
                for (var i = 0; i < n; i++)
                    if (OffensiveRebounderPicker.Pick(st, gm, mc, rng).Number == 5) count++;
                return (double)count / n;
            }

            var rimShare   = ShareSlot5(stateRim,   game, matchupCfg, N);
            var threeShare = ShareSlot5(stateThree, game, matchupCfg, N);
            var ftShare    = ShareSlot5(stateFt,    game, matchupCfg, N);
            Console.WriteLine($"    slot-5 share — Rim (un-nerfed): {rimShare:P2}  Three (nerfed): {threeShare:P2}  FT board (un-nerfed): {ftShare:P2}");
            // Nerf should reduce share on Three; Rim and FT board should be comparable.
            var sub4Ok = threeShare < rimShare && Math.Abs(ftShare - rimShare) < 0.05;
            ok &= sub4Ok;
            Console.WriteLine(sub4Ok ? "    [OK]" : "    [FAIL] nerf did not behave as expected");
        }

        // ── Sub-check 5: Floor / throw ───────────────────────────────────────────
        {
            Console.WriteLine("  Sub-check 5: floor (null slots skipped) and throw (all-null offense)");
            // 5a: only slots 1 and 3 populated → picks must always be slot 1 or 3.
            var game5 = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            game5.HomeRoster.SetStarter(game5.HomeLineup.SlotAt(1), MkP(1, 50));
            game5.HomeRoster.SetStarter(game5.HomeLineup.SlotAt(3), MkP(3, 50));
            game5.AwayRoster.SetStarter(game5.AwayLineup.SlotAt(1), MkP(6, 50));
            var stateFloor = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound, ShotType: ShotLocation.Rim);
            var rng5 = new SystemRng(42);
            var counts5 = new int[5];
            for (var i = 0; i < N; i++)
            {
                var pick = OffensiveRebounderPicker.Pick(stateFloor, game5, matchupCfg, rng5);
                counts5[pick.Number - 1]++;
            }
            var floorOk = counts5[0] + counts5[2] == N
                          && counts5[1] == 0 && counts5[3] == 0 && counts5[4] == 0;
            Console.WriteLine($"    slot distribution (only 1&3 seated): s1={counts5[0]:N0} s2={counts5[1]:N0} s3={counts5[2]:N0} s4={counts5[3]:N0} s5={counts5[4]:N0}");
            ok &= floorOk;
            Console.WriteLine(floorOk ? "    floor [OK]" : "    floor [FAIL] picks landed in empty slots");

            // 5b: all-null offense → must throw.
            var gameEmpty = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            gameEmpty.AwayRoster.SetStarter(gameEmpty.AwayLineup.SlotAt(1), MkP(6, 50));
            bool threwOk;
            try
            {
                OffensiveRebounderPicker.Pick(stateFloor, gameEmpty, matchupCfg, new SystemRng(1));
                threwOk = false;
            }
            catch (InvalidOperationException) { threwOk = true; }
            ok &= threwOk;
            Console.WriteLine(threwOk ? "    throw  [OK]" : "    throw  [FAIL] did not throw on all-null offense");
        }

        // ── Sub-check 6: Reproducibility ─────────────────────────────────────────
        // Same seed → identical pick sequence over 1,000 draws.
        {
            Console.WriteLine("  Sub-check 6: reproducibility (same seed → identical sequence)");
            var off   = new[] { MkP(1,50), MkP(2,60), MkP(3,70), MkP(4,65), MkP(5,80) };
            var game  = BuildGame(off);
            var state = MkState(game, ShotLocation.Mid, shooterSlot: 3);
            const int Rep = 1_000;
            var seq1 = new int[Rep];
            var seq2 = new int[Rep];
            var rngA = new SystemRng(77);
            var rngB = new SystemRng(77);
            for (var i = 0; i < Rep; i++) seq1[i] = OffensiveRebounderPicker.Pick(state, game, matchupCfg, rngA).Number;
            for (var i = 0; i < Rep; i++) seq2[i] = OffensiveRebounderPicker.Pick(state, game, matchupCfg, rngB).Number;
            var sub6Ok = seq1.SequenceEqual(seq2);
            ok &= sub6Ok;
            Console.WriteLine(sub6Ok ? "    [OK]" : "    [FAIL] sequences diverged");
        }

        // ── OrbBySlot total == OrbWon invariant ──────────────────────────────────
        // Drive a full governor run with real generators so possessions actually reach
        // Roll I and produce offensive boards. Assert OrbBySlot.Total == OrbWon on
        // every possession record.
        {
            Console.WriteLine("  OrbBySlot.Total == OrbWon invariant (governor run)");
            var cfgA       = RollAConfig.Load(configPath);
            var cfgGov     = GovernorConfig.Load(configPath);
            var cfgClock   = RollClockConfig.Load(configPath);
            var cfgEoH     = EndOfHalfConfig.Load(configPath);
            var cfgE       = RollEConfig.Load(configPath);

            // Build a game with five players per side so real generators have attributes.
            var govGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var offPlayers = new[]
            {
                MkP(1,50), MkP(2,55), MkP(3,60), MkP(4,65),
                MkP(5,55, height:75, postDef:70, str:70, orb:75),
            };
            var defPlayers = Enumerable.Range(6, 5).Select(i => MkP(i, 50)).ToArray();
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

            var invariantFails = 0;
            var orbPossessions = 0;
            foreach (var r in result.Possessions)
            {
                if (r.OrbBySlot.Total != r.OrbWon) invariantFails++;
                if (r.OrbWon > 0) orbPossessions++;
            }
            var invOk = invariantFails == 0;
            ok &= invOk;
            Console.WriteLine(invOk
                ? $"    [OK] OrbBySlot.Total == OrbWon on all {result.Possessions.Count:N0} possessions ({orbPossessions} with ORB > 0)"
                : $"    [FAIL] {invariantFails} possessions violated OrbBySlot.Total == OrbWon");
        }

        // ── Sub-check 7 — Phase 35 wingspan tilt in offensive picker ─────────────
        // Two otherwise-identical offensive players, one with longer arms. After the
        // wingspan factor is added, the long-armed player's share must exceed the
        // short-armed player's share. Shooter nerf is off (Rim zone, slot 1 is not
        // long-armed). Confirms wingspan is rebounding-specific and in the right direction.
        {
            Console.WriteLine("  Sub-check 7 (Phase 35): wingspan tilt — long-armed offensive rebounder gets larger share");
            var off = new[]
            {
                MkP(1, 50, height: 60, postDef: 60, str: 60, orb: 60, wingspan: 50),  // short-armed
                MkP(2, 50, height: 60, postDef: 60, str: 60, orb: 60, wingspan: 70),  // long-armed
                MkP(3, 50, height: 60, postDef: 60, str: 60, orb: 60, wingspan: 50),
                MkP(4, 50, height: 60, postDef: 60, str: 60, orb: 60, wingspan: 50),
                MkP(5, 50, height: 60, postDef: 60, str: 60, orb: 60, wingspan: 50),
            };
            var game  = BuildGame(off);
            // Shooter is slot 3 (average-wingspan player) so the two test players are not nerfed.
            var state = MkState(game, ShotLocation.Rim, shooterSlot: 3);
            var rng   = new SystemRng(35001);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[OffensiveRebounderPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var shortArmShare = (double)counts[0] / N;  // slot 1: wingspan 50
            var longArmShare  = (double)counts[1] / N;  // slot 2: wingspan 70
            Console.WriteLine($"    short-arm (wingspan=50) share: {shortArmShare:P2}");
            Console.WriteLine($"    long-arm  (wingspan=70) share: {longArmShare:P2}");
            var sub7Ok = longArmShare > shortArmShare;
            ok &= sub7Ok;
            Console.WriteLine(sub7Ok ? "    [OK] long-armed player gets larger share" : "    [FAIL] wingspan tilt wrong direction");
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 31 rebounder picker check: PASSED" : "  Phase 31 rebounder picker check: FAILED (see [FAIL] lines above)");
        return ok;
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Phase 32 — putback attempt rate check
    // ─────────────────────────────────────────────────────────────────────────
    private static bool Phase32PutbackAttemptRateCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 32: putback attempt rate (RollKGenerator) ---");
        var ok = true;

        var cfgK     = RollKConfig.Load(configPath);
        var cfgMatch = MatchupConfig.Load(configPath);
        var cfgD     = RollDConfig.Load(configPath);

        // Helper: build a player with specified key attributes; all others at base b.
        // spdBase controls Speed/Quickness/FirstStep/Vertical (Athleticism composite).
        static Player MkP32(int id, int b,
            int? str = null, int? ht = null, int? spdBase = null,
            int? fin = null, int? rimProt = null)
            => new Player($"p{id}")
            {
                PlayerId            = id,
                Outside = b, Mid = b, Close = b, Finishing = fin ?? b, FreeThrow = b,
                FoulDrawing = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation = b, PostMoves = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding = b,
                PerimeterDefense = b, PostDefense = b, RimProtection = rimProt ?? b,
                DefensiveRebounding = b, Steals = b,
                Height = ht ?? b, Wingspan = b, Weight = b,
                Strength = str ?? b,
                Speed = spdBase ?? b, Quickness = spdBase ?? b,
                FirstStep = spdBase ?? b, Vertical = spdBase ?? b,
                Endurance = b, Hustle = b, BasketballIQ = b, Discipline = b, HelpDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Helper: seat five offensive (Home) and five defensive (Away) players.
        GameState BuildGame(Player[] off, Player[] def)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), def[i]);
            }
            return g;
        }

        // Helper: state with rebounder in Home slot 1.
        static PossessionState MkState(GameState g, ShotLocation? zone)
            => new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                ShotType: zone,
                ReboundSlot: g.HomeLineup.SlotAt(1));

        // Helper: flat-arms sum for a given source.
        static double FlatArmsSum(RollKConfig cfg, OffensiveReboundSource src)
            => src == OffensiveReboundSource.FreeThrow
                ? cfg.FreeThrowJumpBall + cfg.FreeThrowDefensiveFoul + cfg.FreeThrowOffensiveFoul
                  + cfg.FreeThrowDeadBallTurnover + cfg.FreeThrowLiveBallTurnover
                : cfg.JumpBall + cfg.DefensiveFoul + cfg.OffensiveFoul
                  + cfg.DeadBallTurnover + cfg.LiveBallTurnover;

        // Helper: get PutBack weight from a pie.
        static double PBWeight(Pie<OffensiveReboundOutcome> pie)
            => pie.Slices.First(x => x.Outcome == OffensiveReboundOutcome.PutBack).Weight;

        // ── Sub-check 1: Offense dominant (Short, pure matchup) ──────────────────
        // Big rebounder (Str=90, Ht=92, spdBase→Athl≈80, Fin=55) vs 5 weak guards
        // (RimProt=10, Ht=65, Str=30). Short modifier=1.0 isolates the matchup.
        // Assert: PutBack > cfg.PutBack (baseline).
        {
            Console.WriteLine("  Sub-check 1: offense dominant — big rebounder vs weak guards (Short)");
            var rebounder = MkP32(1, 50, str: 90, ht: 92, spdBase: 78, fin: 55);
            var off = new[] { rebounder, MkP32(2,50), MkP32(3,50), MkP32(4,50), MkP32(5,50) };
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50, str: 30, ht: 65, rimProt: 10)).ToArray();
            var game  = BuildGame(off, def);
            var state = MkState(game, ShotLocation.Short);
            var gen   = new RollKGenerator(cfgK, cfgMatch, game);
            var putback = PBWeight(gen.Generate(state, OffensiveReboundSource.LiveBall));
            var pass = putback > cfgK.PutBack;
            ok &= pass;
            Console.WriteLine(pass
                ? $"    [OK]   PutBack={putback:F4} > baseline {cfgK.PutBack:F4}"
                : $"    [FAIL] PutBack={putback:F4} not > baseline {cfgK.PutBack:F4}");
        }

        // ── Sub-check 2: Defense dominant (Short, pure matchup) ──────────────────
        // Guard rebounder (Str=30, Ht=72, spdBase=72, Fin=40) vs 5 elite rim protectors
        // (RimProt=88, Ht=90, Str=82). Assert: PutBack < cfg.PutBack.
        {
            Console.WriteLine("  Sub-check 2: defense dominant — guard rebounder vs elite rim protectors (Short)");
            var rebounder = MkP32(1, 50, str: 30, ht: 72, spdBase: 72, fin: 40);
            var off = new[] { rebounder, MkP32(2,50), MkP32(3,50), MkP32(4,50), MkP32(5,50) };
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50, str: 82, ht: 90, rimProt: 88)).ToArray();
            var game  = BuildGame(off, def);
            var state = MkState(game, ShotLocation.Short);
            var gen   = new RollKGenerator(cfgK, cfgMatch, game);
            var putback = PBWeight(gen.Generate(state, OffensiveReboundSource.LiveBall));
            var pass = putback < cfgK.PutBack;
            ok &= pass;
            Console.WriteLine(pass
                ? $"    [OK]   PutBack={putback:F4} < baseline {cfgK.PutBack:F4}"
                : $"    [FAIL] PutBack={putback:F4} not < baseline {cfgK.PutBack:F4}");
        }

        // ── Sub-check 3: Neutral baseline at Short ───────────────────────────────
        // All-50 rebounder vs all-50 defense. Short modifier=1.0.
        // Assert: |PutBack - cfg.PutBack| < 0.02.
        {
            Console.WriteLine("  Sub-check 3: neutral all-50 at Short ≈ baseline");
            var off = Enumerable.Range(0, 5).Select(i => MkP32(i+1, 50)).ToArray();
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50)).ToArray();
            var game  = BuildGame(off, def);
            var state = MkState(game, ShotLocation.Short);
            var gen   = new RollKGenerator(cfgK, cfgMatch, game);
            var putback = PBWeight(gen.Generate(state, OffensiveReboundSource.LiveBall));
            var pass = Math.Abs(putback - cfgK.PutBack) < 0.02;
            ok &= pass;
            Console.WriteLine(pass
                ? $"    [OK]   PutBack={putback:F4} ≈ baseline {cfgK.PutBack:F4} (|diff|<0.02)"
                : $"    [FAIL] PutBack={putback:F4} vs baseline {cfgK.PutBack:F4} (|diff|={Math.Abs(putback-cfgK.PutBack):F4})");
        }

        // ── Sub-check 3b: Neutral at Rim is above baseline ───────────────────────
        // Same all-50 lineup, but ShotType=Rim (modifier 1.10 > 1.0).
        // Assert: PutBack > cfg.PutBack and PutBack ≤ PutbackCeiling.
        {
            Console.WriteLine("  Sub-check 3b: neutral all-50 at Rim > baseline (rim-board boost)");
            var off = Enumerable.Range(0, 5).Select(i => MkP32(i+1, 50)).ToArray();
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50)).ToArray();
            var game  = BuildGame(off, def);
            var state = MkState(game, ShotLocation.Rim);
            var gen   = new RollKGenerator(cfgK, cfgMatch, game);
            var putback = PBWeight(gen.Generate(state, OffensiveReboundSource.LiveBall));
            var pass = putback > cfgK.PutBack && putback <= cfgK.PutbackCeiling;
            ok &= pass;
            Console.WriteLine(pass
                ? $"    [OK]   PutBack={putback:F4} > baseline {cfgK.PutBack:F4}, ≤ ceiling {cfgK.PutbackCeiling:F4}"
                : $"    [FAIL] PutBack={putback:F4} (baseline={cfgK.PutBack:F4}, ceiling={cfgK.PutbackCeiling:F4})");
        }

        // ── Sub-check 4: Zone modifier — Three < Rim ─────────────────────────────
        // Moderate rebounder (Str=80, Ht=85, spdBase=75, Fin=50) vs moderate defense
        // (RimProt=40, Ht=75, Str=45 × 5). Same matchup, different zones.
        // Assert: PutBack(Three) < PutBack(Rim).
        {
            Console.WriteLine("  Sub-check 4: zone modifier — Three < Rim for same matchup");
            var rebounder = MkP32(1, 50, str: 80, ht: 85, spdBase: 75, fin: 50);
            var off = new[] { rebounder, MkP32(2,50), MkP32(3,50), MkP32(4,50), MkP32(5,50) };
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50, str: 45, ht: 75, rimProt: 40)).ToArray();
            var game      = BuildGame(off, def);
            var stateThree = MkState(game, ShotLocation.Three);
            var stateRim   = MkState(game, ShotLocation.Rim);
            var gen        = new RollKGenerator(cfgK, cfgMatch, game);
            var pbThree = PBWeight(gen.Generate(stateThree, OffensiveReboundSource.LiveBall));
            var pbRim   = PBWeight(gen.Generate(stateRim,   OffensiveReboundSource.LiveBall));
            var pass = pbThree < pbRim;
            ok &= pass;
            Console.WriteLine(pass
                ? $"    [OK]   Three={pbThree:F4} < Rim={pbRim:F4}"
                : $"    [FAIL] Three={pbThree:F4} not < Rim={pbRim:F4}");
        }

        // ── Sub-check 5: Null ShotType (FT board) ────────────────────────────────
        // Same rebounder as sub-check 1. ShotType=null → zone modifier 1.0.
        // Assert: no crash; result in [PutbackFloor, PutbackCeiling].
        {
            Console.WriteLine("  Sub-check 5: null ShotType (FT board) → valid range, no crash");
            var rebounder = MkP32(1, 50, str: 90, ht: 92, spdBase: 78, fin: 55);
            var off = new[] { rebounder, MkP32(2,50), MkP32(3,50), MkP32(4,50), MkP32(5,50) };
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50, str: 30, ht: 65, rimProt: 10)).ToArray();
            var game  = BuildGame(off, def);
            var state = MkState(game, null);   // null ShotType
            var gen   = new RollKGenerator(cfgK, cfgMatch, game);
            var putback = PBWeight(gen.Generate(state, OffensiveReboundSource.LiveBall));
            var pass = putback >= cfgK.PutbackFloor && putback <= cfgK.PutbackCeiling;
            ok &= pass;
            Console.WriteLine(pass
                ? $"    [OK]   PutBack={putback:F4} in [{cfgK.PutbackFloor:F4}, {cfgK.PutbackCeiling:F4}]"
                : $"    [FAIL] PutBack={putback:F4} out of [{cfgK.PutbackFloor:F4}, {cfgK.PutbackCeiling:F4}]");
        }

        // ── Sub-check 6: Null ReboundSlot fallback (both sources) ────────────────
        // ReboundSlot=null → generator short-circuits to flat config pie.
        // LiveBall: PutBack == cfg.PutBack exactly.
        // FreeThrow: PutBack == cfg.FreeThrowPutBack exactly.
        {
            Console.WriteLine("  Sub-check 6: null ReboundSlot → flat config fallback (both sources)");
            var off = Enumerable.Range(0, 5).Select(i => MkP32(i+1, 50)).ToArray();
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50)).ToArray();
            var game = BuildGame(off, def);

            // null ReboundSlot state — ReboundSlot defaults to null
            var stateNull = new PossessionState(
                PossessionNumber: 1,
                Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                ShotType: ShotLocation.Short
                // ReboundSlot: null (default)
            );
            var gen = new RollKGenerator(cfgK, cfgMatch, game);

            var pbLive = PBWeight(gen.Generate(stateNull, OffensiveReboundSource.LiveBall));
            var pbFT   = PBWeight(gen.Generate(stateNull, OffensiveReboundSource.FreeThrow));

            var passLive = pbLive == cfgK.PutBack;
            var passFT   = pbFT   == cfgK.FreeThrowPutBack;
            ok &= passLive && passFT;

            Console.WriteLine(passLive
                ? $"    [OK]   LiveBall fallback PutBack={pbLive:F4} == cfg.PutBack={cfgK.PutBack:F4}"
                : $"    [FAIL] LiveBall fallback PutBack={pbLive:F4} != cfg.PutBack={cfgK.PutBack:F4}");
            Console.WriteLine(passFT
                ? $"    [OK]   FreeThrow fallback PutBack={pbFT:F4} == cfg.FreeThrowPutBack={cfgK.FreeThrowPutBack:F4}"
                : $"    [FAIL] FreeThrow fallback PutBack={pbFT:F4} != cfg.FreeThrowPutBack={cfgK.FreeThrowPutBack:F4}");
        }

        // ── Sub-check 7: Flat arms unchanged across sub-checks 1–5 ───────────────
        // The five non-PutBack/non-ResetOffense arms must equal the config sum in all runs.
        {
            Console.WriteLine("  Sub-check 7: flat arms unchanged across sub-checks 1–5");
            var cfgFlatLive = FlatArmsSum(cfgK, OffensiveReboundSource.LiveBall);
            var off = Enumerable.Range(0, 5).Select(i => MkP32(i+1, 50)).ToArray();
            var def = Enumerable.Range(0, 5).Select(i => MkP32(10+i, 50)).ToArray();
            var game = BuildGame(off, def);
            var gen  = new RollKGenerator(cfgK, cfgMatch, game);

            var zones = new[] {
                ShotLocation.Three, ShotLocation.Long, ShotLocation.Mid,
                ShotLocation.Short, ShotLocation.Rim };
            var allPass = true;
            foreach (var zone in zones)
            {
                var state = MkState(game, zone);
                var pie   = gen.Generate(state, OffensiveReboundSource.LiveBall);
                var pieMap = pie.Slices.ToDictionary(s => s.Outcome, s => s.Weight);
                var flatInPie = pieMap[OffensiveReboundOutcome.JumpBall]
                              + pieMap[OffensiveReboundOutcome.DefensiveFoul]
                              + pieMap[OffensiveReboundOutcome.OffensiveFoul]
                              + pieMap[OffensiveReboundOutcome.DeadBallTurnover]
                              + pieMap[OffensiveReboundOutcome.LiveBallTurnover];
                if (Math.Abs(flatInPie - cfgFlatLive) > cfgK.Epsilon * 10)
                {
                    Console.WriteLine($"    [FAIL] {zone}: flat arms sum={flatInPie:F6} != config {cfgFlatLive:F6}");
                    allPass = false;
                }
            }
            ok &= allPass;
            Console.WriteLine(allPass
                ? $"    [OK]   Flat arms sum={cfgFlatLive:F6} constant across all zones"
                : "    (see FAIL lines above)");
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 32 putback attempt rate check: PASSED" : "  Phase 32 putback attempt rate check: FAILED (see [FAIL] lines above)");
        return ok;
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Phase 35 — defensive-rebound attribution on-walk (DefensiveRebounderPicker)
    // ─────────────────────────────────────────────────────────────────────────
    private static bool Phase35DefensiveReboundCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 35: defensive rebounder picker (DefensiveRebounderPicker) + on-walk attribution ---");
        var ok = true;
        const int N = 100_000;

        var matchupCfg = MatchupConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);

        // Helper: player with all attributes at b; override specific attributes.
        static Player MkP35(int id, int b,
                            int? height = null, int? postDef = null,
                            int? str    = null, int? drb     = null, int? wingspan = null)
            => new Player($"p{id}")
            {
                PlayerId             = id,
                Outside              = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing          = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation         = b, PostMoves    = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = b,
                PerimeterDefense     = b, PostDefense = postDef ?? b, RimProtection = b,
                DefensiveRebounding  = drb ?? b,
                Steals               = b,
                Height               = height  ?? b, Wingspan = wingspan ?? b, Weight = b,
                Strength             = str     ?? b,
                Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline           = b, HelpDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Build a GameState with defPlayers on Away (state.Defense = Away).
        GameState BuildGame35(Player[] offPlayers, Player[] defPlayers)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < offPlayers.Length && i < 5; i++)
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), offPlayers[i]);
            for (var i = 0; i < defPlayers.Length && i < 5; i++)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), defPlayers[i]);
            return g;
        }

        // Possession state: Home offends, Away defends.
        static PossessionState MkState35()
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);

        var offDummy = Enumerable.Range(6, 5).Select(i => MkP35(i, 50)).ToArray();

        // ── Sub-check 1 — Bigs favored (C/PF pull disproportionate share) ────────
        {
            Console.WriteLine("  Sub-check 1: defensive picker bigs favored (C > PG, PF > SF)");
            var def = new[]
            {
                MkP35(1, 50, height: 40, postDef: 42, str: 44, drb: 35),  // PG
                MkP35(2, 50, height: 45, postDef: 44, str: 46, drb: 42),  // SG
                MkP35(3, 50, height: 55, postDef: 55, str: 55, drb: 52),  // SF
                MkP35(4, 50, height: 72, postDef: 70, str: 72, drb: 72),  // PF
                MkP35(5, 50, height: 86, postDef: 82, str: 82, drb: 85),  // C
            };
            var game  = BuildGame35(offDummy, def);
            var state = MkState35();
            var rng   = new SystemRng(35101);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[DefensiveRebounderPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var shares = counts.Select(c => (double)c / N).ToArray();
            Console.WriteLine($"    PG={shares[0]:P2}  SG={shares[1]:P2}  SF={shares[2]:P2}  PF={shares[3]:P2}  C={shares[4]:P2}");
            var sub1Ok = shares[4] > shares[0] && shares[3] > shares[2];
            ok &= sub1Ok;
            Console.WriteLine(sub1Ok ? "    [OK]" : "    [FAIL] big-favored direction wrong");
        }

        // ── Sub-check 2 — Guard floor holds ──────────────────────────────────────
        {
            Console.WriteLine("  Sub-check 2: defensive picker guard floor holds (PG > 1%)");
            var def = new[]
            {
                MkP35(1, 50, height: 40, postDef: 42, str: 44, drb: 35),
                MkP35(2, 50, height: 45, postDef: 44, str: 46, drb: 42),
                MkP35(3, 50, height: 55, postDef: 55, str: 55, drb: 52),
                MkP35(4, 50, height: 72, postDef: 70, str: 72, drb: 72),
                MkP35(5, 50, height: 86, postDef: 82, str: 82, drb: 85),
            };
            var game  = BuildGame35(offDummy, def);
            var state = MkState35();
            var rng   = new SystemRng(35102);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[DefensiveRebounderPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var pgShare = (double)counts[0] / N;
            Console.WriteLine($"    PG share={pgShare:P2}  (bound: > 1%)");
            var sub2Ok = pgShare > 0.01;
            ok &= sub2Ok;
            Console.WriteLine(sub2Ok ? "    [OK]" : "    [FAIL] PG share below floor");
        }

        // ── Sub-check 3 — Wingspan tilt ───────────────────────────────────────────
        {
            Console.WriteLine("  Sub-check 3: wingspan tilt — long-armed defender > short-armed identical teammate");
            var def = new[]
            {
                MkP35(1, 50, height: 60, postDef: 60, str: 60, drb: 60, wingspan: 50),  // short-armed
                MkP35(2, 50, height: 60, postDef: 60, str: 60, drb: 60, wingspan: 70),  // long-armed
                MkP35(3, 50, height: 60, postDef: 60, str: 60, drb: 60, wingspan: 50),
                MkP35(4, 50, height: 60, postDef: 60, str: 60, drb: 60, wingspan: 50),
                MkP35(5, 50, height: 60, postDef: 60, str: 60, drb: 60, wingspan: 50),
            };
            var game  = BuildGame35(offDummy, def);
            var state = MkState35();
            var rng   = new SystemRng(35103);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[DefensiveRebounderPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var shortShare = (double)counts[0] / N;
            var longShare  = (double)counts[1] / N;
            Console.WriteLine($"    short-arm (wingspan=50) share={shortShare:P2}  long-arm (wingspan=70) share={longShare:P2}");
            var sub3Ok = longShare > shortShare;
            ok &= sub3Ok;
            Console.WriteLine(sub3Ok ? "    [OK]" : "    [FAIL] wingspan tilt wrong direction");
        }

        // ── Sub-check 4 — Reproducibility ────────────────────────────────────────
        {
            Console.WriteLine("  Sub-check 4: same seed → identical sequence");
            var def = new[]
            {
                MkP35(1, 60, height: 42, postDef: 44, str: 50, drb: 35),
                MkP35(2, 60, height: 78, postDef: 76, str: 80, drb: 85),
            };
            var game  = BuildGame35(offDummy, def);
            var state = MkState35();
            const int RepSeed = 35104;
            var run1 = new List<int>(); var run2 = new List<int>();
            var rng1 = new SystemRng(RepSeed); var rng2 = new SystemRng(RepSeed);
            for (var i = 0; i < 200; i++)
            {
                run1.Add(DefensiveRebounderPicker.Pick(state, game, matchupCfg, rng1).Number);
                run2.Add(DefensiveRebounderPicker.Pick(state, game, matchupCfg, rng2).Number);
            }
            var sub4Ok = run1.SequenceEqual(run2);
            ok &= sub4Ok;
            Console.WriteLine(sub4Ok ? "    [OK]" : "    [FAIL] same seed produced different sequences");
        }

        // ── Sub-check 5 — Empty-defense throw ────────────────────────────────────
        {
            Console.WriteLine("  Sub-check 5: empty defense throws");
            var game  = BuildGame35(offDummy, Array.Empty<Player>());  // no Away players
            var state = MkState35();
            var rng   = new SystemRng(1);
            var threw = false;
            try { DefensiveRebounderPicker.Pick(state, game, matchupCfg, rng); }
            catch (InvalidOperationException) { threw = true; }
            ok &= threw;
            Console.WriteLine(threw ? "    [OK]" : "    [FAIL] empty defense did not throw");
        }

        // ── Sub-check 6 — Defensive matches offensive shape (minus shooterNerf) ───
        // Same roster through both pickers. Ordering of shares must agree:
        // whoever ranks highest on the offense side also ranks highest on the defense side
        // (both are driven by the same DefReb=DRb / OffReb=ORb placeholder; set them equal).
        {
            Console.WriteLine("  Sub-check 6: defensive and offensive share ordering agree (same roster, no nerf)");
            var players = new[]
            {
                MkP35(1, 50, height: 40, postDef: 42, str: 44, drb: 35, wingspan: 50),
                MkP35(2, 50, height: 55, postDef: 55, str: 55, drb: 55, wingspan: 55),
                MkP35(3, 50, height: 72, postDef: 70, str: 70, drb: 72, wingspan: 70),
                MkP35(4, 50, height: 86, postDef: 82, str: 82, drb: 85, wingspan: 80),
                MkP35(5, 50, height: 40, postDef: 42, str: 44, drb: 35, wingspan: 50),
            };
            // Mirror OffReb = DRb so the two pickers see identical skill inputs.
            var playersOffSide = players.Select((p, i) =>
                new Player($"p{i + 1}")
                {
                    PlayerId = p.PlayerId, Outside = p.Outside, Mid = p.Mid, Close = p.Close,
                    Finishing = p.Finishing, FreeThrow = p.FreeThrow, FoulDrawing = p.FoulDrawing,
                    BallHandling = p.BallHandling, Passing = p.Passing, Playmaking = p.Playmaking,
                    SelfCreation = p.SelfCreation, PostMoves = p.PostMoves,
                    OffBallMovement = p.OffBallMovement, Screening = p.Screening,
                    OffensiveRebounding = p.DefensiveRebounding,  // mirror DRb as ORb
                    PerimeterDefense = p.PerimeterDefense, PostDefense = p.PostDefense,
                    RimProtection = p.RimProtection, DefensiveRebounding = p.DefensiveRebounding,
                    Steals = p.Steals, Height = p.Height, Wingspan = p.Wingspan, Weight = p.Weight,
                    Strength = p.Strength, Speed = p.Speed, Quickness = p.Quickness,
                    FirstStep = p.FirstStep, Vertical = p.Vertical, Endurance = p.Endurance,
                    Hustle = p.Hustle, BasketballIQ = p.BasketballIQ, Discipline = p.Discipline, HelpDefense = p.HelpDefense,
                    RimTendency = p.RimTendency, ShortTendency = p.ShortTendency,
                    MidTendency = p.MidTendency, LongTendency = p.LongTendency,
                    ThreeTendency = p.ThreeTendency,
                }).ToArray();

            var game = BuildGame35(playersOffSide, players);
            // Offense = Home (playersOffSide), Defense = Away (players).
            // Rim zone, no shooter-nerf triggering slot (use slot 1 which is a guard).
            var stateOff = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                SelectedSlot: game.HomeLineup.SlotAt(1),
                ShotType: ShotLocation.Rim);   // nerf off: Rim zone
            var stateDef = MkState35();

            var orbRng = new SystemRng(35106); var drbRng = new SystemRng(35107);
            var orbCounts = new int[5]; var drbCounts = new int[5];
            for (var i = 0; i < N; i++)
            {
                orbCounts[OffensiveRebounderPicker.Pick(stateOff, game, matchupCfg, orbRng).Number - 1]++;
                drbCounts[DefensiveRebounderPicker.Pick(stateDef, game, matchupCfg, drbRng).Number - 1]++;
            }
            var orbShares = orbCounts.Select(c => (double)c / N).ToArray();
            var drbShares = drbCounts.Select(c => (double)c / N).ToArray();
            Console.WriteLine($"    ORB shares: {string.Join("  ", orbShares.Select((s, i) => $"s{i + 1}={s:P1}"))}");
            Console.WriteLine($"    DRB shares: {string.Join("  ", drbShares.Select((s, i) => $"s{i + 1}={s:P1}"))}");
            // Robust check: the dominant slot (slot 4 — tallest, strongest, most wingspan)
            // must lead on BOTH sides. Slots 1 and 5 are intentionally identical so a
            // rank-ordering comparison would be tie-sensitive; checking the dominant slot
            // is the right level of assertion here.
            var orbDominantLeads = orbShares[3] > orbShares[0] && orbShares[3] > orbShares[1]
                                && orbShares[3] > orbShares[2] && orbShares[3] > orbShares[4];
            var drbDominantLeads = drbShares[3] > drbShares[0] && drbShares[3] > drbShares[1]
                                && drbShares[3] > drbShares[2] && drbShares[3] > drbShares[4];
            // Shares should also be close between the two pickers (within 3%) for each slot,
            // confirming the two formulas agree quantitatively, not just directionally.
            var sharesClose = Enumerable.Range(0, 5).All(i => Math.Abs(orbShares[i] - drbShares[i]) < 0.03);
            var sub6Ok = orbDominantLeads && drbDominantLeads && sharesClose;
            ok &= sub6Ok;
            if (!sub6Ok)
            {
                if (!orbDominantLeads) Console.WriteLine("    [FAIL] ORB dominant slot (4) does not lead");
                if (!drbDominantLeads) Console.WriteLine("    [FAIL] DRB dominant slot (4) does not lead");
                if (!sharesClose)      Console.WriteLine("    [FAIL] ORB/DRB shares diverge > 3% for some slot");
            }
            Console.WriteLine(sub6Ok ? "    [OK] dominant slot leads on both sides; shares agree within 3%" : "    [FAIL] see detail above");
        }

        // ── Governor run invariants A and B ───────────────────────────────────────
        {
            Console.WriteLine("  Governor run invariants (Phase 35):");
            var cfgA     = RollAConfig.Load(configPath);
            var cfgGov   = GovernorConfig.Load(configPath);
            var cfgClock = RollClockConfig.Load(configPath);
            var cfgEoH   = EndOfHalfConfig.Load(configPath);
            var cfgE     = RollEConfig.Load(configPath);

            var govGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var homePlayers = new[]
            {
                MkP35(1, 50, height: 40, postDef: 42, str: 44, drb: 35),
                MkP35(2, 50, height: 45, postDef: 44, str: 46, drb: 42),
                MkP35(3, 50, height: 55, postDef: 55, str: 55, drb: 52),
                MkP35(4, 50, height: 72, postDef: 70, str: 72, drb: 72),
                MkP35(5, 50, height: 86, postDef: 82, str: 82, drb: 85),
            };
            var awayPlayers = Enumerable.Range(6, 5).Select(i => MkP35(i, 50)).ToArray();
            for (var i = 0; i < 5; i++)
            {
                govGame.HomeRoster.SetStarter(govGame.HomeLineup.SlotAt(i + 1), homePlayers[i]);
                govGame.AwayRoster.SetStarter(govGame.AwayLineup.SlotAt(i + 1), awayPlayers[i]);
            }
            govGame.SetPossessionArrow(TeamSide.Home);

            var rng      = new SystemRng(35200);
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

            var governor = new Governor(resolver, govGame, cfgGov, cfgClock, new SystemRng(35201), cfgEoH);
            var first    = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            var result = governor.Run(first);

            // Invariant A: every DefensiveRebound possession has a non-null DefensiveRebounderSlot,
            // and the count equals the count of DefensiveRebound terminals (1:1).
            var drebPossessions = result.Possessions
                .Where(r => r.EndLabel == "DefensiveRebound")
                .ToList();
            var nullSlotCount = drebPossessions.Count(r => r.DefensiveRebounderSlot is null);
            var invAOk = nullSlotCount == 0;
            ok &= invAOk;
            Console.WriteLine(invAOk
                ? $"    [OK] Invariant A: all {drebPossessions.Count} DefensiveRebound possessions have non-null DefensiveRebounderSlot"
                : $"    [FAIL] Invariant A: {nullSlotCount} DefensiveRebound possessions had null DefensiveRebounderSlot");

            // Invariant B: every non-DefensiveRebound possession has a null DefensiveRebounderSlot.
            var nonDreb = result.Possessions
                .Where(r => r.EndLabel != "DefensiveRebound")
                .ToList();
            var badNonDrebSlots = nonDreb.Count(r => r.DefensiveRebounderSlot is not null);
            var invBOk = badNonDrebSlots == 0;
            ok &= invBOk;
            Console.WriteLine(invBOk
                ? $"    [OK] Invariant B: all {nonDreb.Count} non-DReb possessions have null DefensiveRebounderSlot"
                : $"    [FAIL] Invariant B: {badNonDrebSlots} non-DReb possessions had non-null DefensiveRebounderSlot");
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 35 defensive rebound check: PASSED" : "  Phase 35 defensive rebound check: FAILED (see [FAIL] lines above)");
        return ok;
    }


    private static bool Phase36BlockerCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 36: blocker picker (BlockerPicker) + on-walk attribution ---");
        var ok = true;
        const int N = 100_000;

        var matchupCfg = MatchupConfig.Load(configPath);
        var cfgD       = RollDConfig.Load(configPath);

        // Helper: player with all attributes at b; override specific attributes.
        static Player MkP36(int id, int b,
                            int? rimProt  = null, int? perimDef = null,
                            int? postDef  = null, int? height   = null,
                            int? wingspan = null, int? vertical = null)
            => new Player($"p{id}")
            {
                PlayerId             = id,
                Outside              = b, Mid = b, Close = b, Finishing = b, FreeThrow = b,
                FoulDrawing          = b, BallHandling = b, Passing = b, Playmaking = b,
                SelfCreation         = b, PostMoves    = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = b,
                PerimeterDefense = perimDef ?? b, PostDefense = postDef ?? b,
                RimProtection    = rimProt  ?? b,
                DefensiveRebounding  = b,
                Steals               = b,
                Height               = height   ?? b, Wingspan = wingspan ?? b, Weight = b,
                Strength             = b,
                Speed = b, Quickness = b, FirstStep = b,
                Vertical = vertical ?? b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline           = b, HelpDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // Build a GameState with defPlayers on Away (state.Defense = Away).
        GameState BuildGame36(Player[] offPlayers, Player[] defPlayers)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < offPlayers.Length && i < 5; i++)
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), offPlayers[i]);
            for (var i = 0; i < defPlayers.Length && i < 5; i++)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), defPlayers[i]);
            return g;
        }

        // Possession state: Home offends, Away defends, with specified ShotType.
        static PossessionState MkState36(ShotLocation? shotType = null)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound,
                ShotType: shotType);

        var offDummy = Enumerable.Range(6, 5).Select(i => MkP36(i, 50)).ToArray();

        // ── Sub-check 1 — Zone-aware direction (Rim): rim protector dominant ─────
        {
            Console.WriteLine("  Sub-check 1: zone-aware direction (Rim) — rim protector leads at Rim zone");
            var def = new[]
            {
                MkP36(1, 50, rimProt: 85),  // dominant rim protector
                MkP36(2, 50),
                MkP36(3, 50),
                MkP36(4, 50),
                MkP36(5, 50),
            };
            var game  = BuildGame36(offDummy, def);
            var state = MkState36(ShotLocation.Rim);
            var rng   = new SystemRng(36101);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[BlockerPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var shares = counts.Select(c => (double)c / N).ToArray();
            Console.WriteLine($"    RimProtector(s1)={shares[0]:P2}  others={shares[1]:P2}/{shares[2]:P2}/{shares[3]:P2}/{shares[4]:P2}");
            var sub1Ok = shares[0] > shares[1] && shares[0] > shares[2]
                      && shares[0] > shares[3] && shares[0] > shares[4];
            ok &= sub1Ok;
            Console.WriteLine(sub1Ok ? "    [OK]" : "    [FAIL] rim protector did not lead at Rim zone");
        }

        // ── Sub-check 2 — Zone-aware direction (Three): perimeter defender dominant
        {
            Console.WriteLine("  Sub-check 2: zone-aware direction (Three) — perimeter defender leads at Three zone");
            var def = new[]
            {
                MkP36(1, 50, perimDef: 85),  // dominant perimeter defender
                MkP36(2, 50),
                MkP36(3, 50),
                MkP36(4, 50),
                MkP36(5, 50),
            };
            var game  = BuildGame36(offDummy, def);
            var state = MkState36(ShotLocation.Three);
            var rng   = new SystemRng(36102);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[BlockerPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
            var shares = counts.Select(c => (double)c / N).ToArray();
            Console.WriteLine($"    PerimDefender(s1)={shares[0]:P2}  others={shares[1]:P2}/{shares[2]:P2}/{shares[3]:P2}/{shares[4]:P2}");
            var sub2Ok = shares[0] > shares[1] && shares[0] > shares[2]
                      && shares[0] > shares[3] && shares[0] > shares[4];
            ok &= sub2Ok;
            Console.WriteLine(sub2Ok ? "    [OK]" : "    [FAIL] perimeter defender did not lead at Three zone");
        }

        // ── Sub-check 3 — Wingspan meaningful at all zones ────────────────────────
        {
            Console.WriteLine("  Sub-check 3: Wingspan meaningful at all zones (Wingspan=85 > Wingspan=50 at every zone)");
            var sub3Ok = true;
            foreach (var zone in new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
                                         ShotLocation.Long, ShotLocation.Three })
            {
                var def = new[]
                {
                    MkP36(1, 50, wingspan: 85),  // long-armed
                    MkP36(2, 50),                // baseline
                    MkP36(3, 50), MkP36(4, 50), MkP36(5, 50),
                };
                var game  = BuildGame36(offDummy, def);
                var state = MkState36(zone);
                var rng   = new SystemRng(36103 + (int)zone);
                var counts = new int[5];
                for (var i = 0; i < N; i++)
                    counts[BlockerPicker.Pick(state, game, matchupCfg, rng).Number - 1]++;
                var longShare  = (double)counts[0] / N;
                var shortShare = (double)counts[1] / N;
                var zoneOk = longShare > shortShare;
                sub3Ok &= zoneOk;
                Console.WriteLine($"    {zone}: wingspan85={longShare:P2} > wingspan50={shortShare:P2}  {(zoneOk ? "[OK]" : "[FAIL]")}");
            }
            ok &= sub3Ok;
            Console.WriteLine(sub3Ok ? "    [OK] all zones" : "    [FAIL] see zone detail above");
        }

        // ── Sub-check 4 — Floor holds: guard floor > 0 in big-dominant lineup ─────
        {
            Console.WriteLine("  Sub-check 4: floor holds — guard gets nonzero share in big-dominant lineup");
            var def = new[]
            {
                MkP36(1, 50, rimProt: 95, height: 95, wingspan: 90, vertical: 85),  // dominant big
                MkP36(2, 50, rimProt: 95, height: 95, wingspan: 90, vertical: 85),
                MkP36(3, 50, rimProt: 95, height: 95, wingspan: 90, vertical: 85),
                MkP36(4, 50, rimProt: 95, height: 95, wingspan: 90, vertical: 85),
                MkP36(5, 50, rimProt: 10, height: 55, wingspan: 55, vertical: 50),  // guard
            };
            var game  = BuildGame36(offDummy, def);
            var stateRim   = MkState36(ShotLocation.Rim);
            var stateThree = MkState36(ShotLocation.Three);
            var rngRim   = new SystemRng(36104);
            var rngThree = new SystemRng(36105);
            var countsRim   = new int[5];
            var countsThree = new int[5];
            for (var i = 0; i < N; i++)
            {
                countsRim[BlockerPicker.Pick(stateRim,   game, matchupCfg, rngRim).Number   - 1]++;
                countsThree[BlockerPicker.Pick(stateThree, game, matchupCfg, rngThree).Number - 1]++;
            }
            var guardRimShare   = (double)countsRim[4]   / N;
            var guardThreeShare = (double)countsThree[4] / N;
            Console.WriteLine($"    guard share at Rim={guardRimShare:P2}  Three={guardThreeShare:P2}  (both must be > 0)");
            var sub4Ok = guardRimShare > 0.0 && guardThreeShare > 0.0;
            ok &= sub4Ok;
            Console.WriteLine(sub4Ok ? "    [OK]" : "    [FAIL] guard floor was zero");
        }

        // ── Sub-check 5 — Reproducibility ────────────────────────────────────────
        {
            Console.WriteLine("  Sub-check 5: same seed → identical sequence");
            var def = new[]
            {
                MkP36(1, 50, rimProt: 80, height: 75),
                MkP36(2, 50, perimDef: 80, wingspan: 75),
            };
            var game  = BuildGame36(offDummy, def);
            var state = MkState36(ShotLocation.Mid);
            const int RepSeed = 36200;
            var run1 = new List<int>(); var run2 = new List<int>();
            var rng1 = new SystemRng(RepSeed); var rng2 = new SystemRng(RepSeed);
            for (var i = 0; i < 200; i++)
            {
                run1.Add(BlockerPicker.Pick(state, game, matchupCfg, rng1).Number);
                run2.Add(BlockerPicker.Pick(state, game, matchupCfg, rng2).Number);
            }
            var sub5Ok = run1.SequenceEqual(run2);
            ok &= sub5Ok;
            Console.WriteLine(sub5Ok ? "    [OK]" : "    [FAIL] same seed produced different sequences");
        }

        // ── Sub-check 6 — Empty-defense throw ────────────────────────────────────
        {
            Console.WriteLine("  Sub-check 6: empty defense throws");
            var game  = BuildGame36(offDummy, Array.Empty<Player>());
            var state = MkState36(ShotLocation.Rim);
            var rng   = new SystemRng(1);
            var threw = false;
            try { BlockerPicker.Pick(state, game, matchupCfg, rng); }
            catch (InvalidOperationException) { threw = true; }
            ok &= threw;
            Console.WriteLine(threw ? "    [OK]" : "    [FAIL] empty defense did not throw");
        }

        // ── Sub-check 7 — Null ShotType fallback: no throw, Rim blend fires ───────
        {
            Console.WriteLine("  Sub-check 7: null ShotType fallback — no throw, result matches Rim zone");
            var def = new[]
            {
                MkP36(1, 50, rimProt: 85),  // rim protector
                MkP36(2, 50), MkP36(3, 50), MkP36(4, 50), MkP36(5, 50),
            };
            var game       = BuildGame36(offDummy, def);
            var stateNull  = MkState36(null);              // ShotType == null
            var stateRim   = MkState36(ShotLocation.Rim);  // explicit Rim

            var countsNull = new int[5];
            var countsRim  = new int[5];
            var rngNull = new SystemRng(36301);
            var rngRim  = new SystemRng(36301);  // same seed — results must match
            var threw = false;
            try
            {
                for (var i = 0; i < N; i++)
                {
                    countsNull[BlockerPicker.Pick(stateNull, game, matchupCfg, rngNull).Number - 1]++;
                    countsRim [BlockerPicker.Pick(stateRim,  game, matchupCfg, rngRim ).Number - 1]++;
                }
            }
            catch (Exception) { threw = true; }

            var sharesMatch = !threw && Enumerable.Range(0, 5).All(i => countsNull[i] == countsRim[i]);
            ok &= sharesMatch;
            Console.WriteLine(threw
                ? "    [FAIL] null ShotType threw an exception"
                : sharesMatch
                    ? "    [OK] null ShotType → Rim fallback; results identical to explicit Rim"
                    : "    [FAIL] null ShotType results differ from Rim zone results");
        }

        // ── Governor run invariants A and B ───────────────────────────────────────
        {
            Console.WriteLine("  Governor run invariants (Phase 36):");
            var cfgA     = RollAConfig.Load(configPath);
            var cfgGov   = GovernorConfig.Load(configPath);
            var cfgClock = RollClockConfig.Load(configPath);
            var cfgEoH   = EndOfHalfConfig.Load(configPath);
            var cfgE     = RollEConfig.Load(configPath);

            var govGame = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            var homePlayers = new[]
            {
                MkP36(1, 50, rimProt: 40, height: 40),
                MkP36(2, 50, rimProt: 45, height: 45),
                MkP36(3, 50, rimProt: 55, height: 55),
                MkP36(4, 50, rimProt: 72, height: 72),
                MkP36(5, 50, rimProt: 86, height: 86),
            };
            var awayPlayers = Enumerable.Range(6, 5).Select(i => MkP36(i, 50)).ToArray();
            for (var i = 0; i < 5; i++)
            {
                govGame.HomeRoster.SetStarter(govGame.HomeLineup.SlotAt(i + 1), homePlayers[i]);
                govGame.AwayRoster.SetStarter(govGame.AwayLineup.SlotAt(i + 1), awayPlayers[i]);
            }
            govGame.SetPossessionArrow(TeamSide.Home);

            var rng      = new SystemRng(36400);
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

            var governor = new Governor(resolver, govGame, cfgGov, cfgClock, new SystemRng(36401), cfgEoH);
            var first    = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            var result = governor.Run(first);

            // Invariant A: BlkBySlot.Total == BlkCount on every possession with BlkCount > 0.
            var blkPossessions = result.Possessions.Where(r => r.BlkCount > 0).ToList();
            var invAFails = blkPossessions.Count(r => r.BlkBySlot.Total != r.BlkCount);
            var invAOk = invAFails == 0;
            ok &= invAOk;
            Console.WriteLine(invAOk
                ? $"    [OK] Invariant A: all {blkPossessions.Count} block possessions have BlkBySlot.Total == BlkCount"
                : $"    [FAIL] Invariant A: {invAFails} possessions had BlkBySlot.Total != BlkCount");

            // Invariant B: BlkBySlot is default (all zeros) on every possession with BlkCount == 0.
            var noBlkPossessions = result.Possessions.Where(r => r.BlkCount == 0).ToList();
            var invBFails = noBlkPossessions.Count(r => r.BlkBySlot.Total != 0);
            var invBOk = invBFails == 0;
            ok &= invBOk;
            Console.WriteLine(invBOk
                ? $"    [OK] Invariant B: all {noBlkPossessions.Count} non-block possessions have BlkBySlot default (all zeros)"
                : $"    [FAIL] Invariant B: {invBFails} non-block possessions had non-zero BlkBySlot");
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 36 blocker check: PASSED" : "  Phase 36 blocker check: FAILED (see [FAIL] lines above)");
        return ok;
    }

}
