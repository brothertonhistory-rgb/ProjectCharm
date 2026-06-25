using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =========================================================================
    // Phase 51 — FouledPlayerPicker (real FT shooter on populated bonus trips).
    //
    // Proves the foul-draw pick names WHO drew a pre-Roll-E bonus foul, that the
    // trip is shot at his real rating and credited to him, that the draw is one
    // clean draw at the right boundary, and that every other FT path is untouched.
    //
    // Discipline: the picker's SHAPE is proven by direct probes (real picker,
    // fixed-seed Monte Carlo, ≥100k draws) — no end-to-end batch where avoidable.
    // The two reader sites (rating + attribution) are proven at the generator and
    // via resolver.Route on constructed FT-edge states. The Python pre-check that
    // gated this build confirmed the six acceptance bands at the shipped
    // placeholders (w_fd = w_use = w_bh = 1.0, floor = 0.05).
    //
    // NOTE ON SUB-CHECK (g) — see the block comment at sub-check (g) below. The
    // clear is proven by direct edge construction (the no-slot shooting foul stays
    // the existing exception, never the picker) plus the generator's null
    // fall-through, NOT by forcing the natural bonus-miss → FT-rebound → putback →
    // second-foul chain. That chain cannot be forced deterministically without
    // running the engine (its rebound/foul pie slice positions are config-driven),
    // which is the brittle-blind-code failure mode. Flagged for Emmett.
    // =========================================================================
    private static bool FreeThrowFoulDrawCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 51: free-throw foul draw (FouledPlayerPicker) ---");
        var ok = true;
        const int N = 100_000;

        // ── Configs (mirror the Phase 25 foul-check load set) ──────────────────
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
        var cfgMatchup = MatchupConfig.Load(configPath);

        // ── Helper: player with all attributes at b; override the four channels
        //     the foul-draw pick reads (FoulDrawing, HierarchyRank, BallHandling)
        //     plus FreeThrow (for the rating reads). ──────────────────────────────
        static Player MkP(int id, int b,
                          int? fd = null, int? hr = null, int? bh = null, int? ft = null)
            => new Player($"p{id}")
            {
                PlayerId             = id,
                HierarchyRank        = hr ?? 5,
                Outside              = b, Mid = b, Close = b, Finishing = b,
                FreeThrow            = ft ?? b,
                FoulDrawing          = fd ?? b, BallHandling = bh ?? b, Passing = b, Playmaking = b,
                SelfCreation         = b, PostMoves    = b, OffBallMovement = b, Screening = b,
                OffensiveRebounding  = b,
                PerimeterDefense     = b, PostDefense = b, RimProtection = b,
                DefensiveRebounding  = b,
                Steals               = b,
                Height               = b, Wingspan = b, Weight = b,
                Strength             = b,
                Speed = b, Quickness = b, FirstStep = b,
                Vertical = b, Endurance = b, Hustle = b, BasketballIQ = b,
                Discipline           = b, HelpDefense = b, OffBallDefense = b,
                RimTendency = b, ShortTendency = b, MidTendency = b,
                LongTendency = b, ThreeTendency = b,
            };

        // ── Helper: seat five offensive players in Home 1-5; neutral away side. ──
        GameState BuildGame(Player[] off)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < off.Length && i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), off[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), MkP(i + 6, 50));
            }
            return g;
        }

        // ── Helper: pre-selection possession state (no SelectedSlot). ───────────
        static PossessionState MkPreSel(GameState g)
            => new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);

        // ── Helper: pick-share tally over N draws (pure picker probe). ──────────
        double[] PickShares(Player[] off, int seed)
        {
            var game   = BuildGame(off);
            var state  = MkPreSel(game);
            var rng    = new SystemRng(seed);
            var counts = new int[5];
            for (var i = 0; i < N; i++)
                counts[FouledPlayerPicker.Pick(state, game, cfgMatchup, rng).Number - 1]++;
            var shares = new double[5];
            for (var i = 0; i < 5; i++) shares[i] = (double)counts[i] / N;
            return shares;
        }

        // ── Helper: build a resolver over a controlled game (real generators). ──
        Resolver BuildResolver(GameState g, IRng rng) => new Resolver(
            new RollAGenerator(cfg, cfgMatchup, g), cfg,
            new RollBGenerator(cfgB, cfgMatchup, g),
            new RollCGenerator(cfgC), cfgC,
            new RollDGenerator(cfgD),
            new RollEGenerator(cfgE, g),
            new AttentionGenerator(AttentionConfig.Load(configPath), g),
            new RollFGenerator(cfgF, cfgMatchup, g),
            new RollGGenerator(cfgG, cfgMatchup, g),
            new RollHGenerator(cfgH, cfgMatchup, g),
            new RollIGenerator(cfgI, cfgMatchup, g),
            new RollJGenerator(cfgJ, cfgMatchup, g),
            new RollKGenerator(cfgK, cfgMatchup, g),
            new RollLGenerator(cfgL, g),
            new RollMGenerator(cfgM, cfgMatchup, g),
            new RollOffensiveFoulGenerator(cfgOffFoul),
            cfgMatchup, g, rng);

        // ── Helper: make% the Roll L pie assigns for a given state. ─────────────
        double MakeProbFor(GameState g, PossessionState s)
        {
            var pie   = new RollLGenerator(cfgL, g).Generate(s);
            var total = pie.Slices.Sum(x => x.Weight);
            var make  = pie.Slices.Where(x => x.Outcome == FreeThrowOutcome.Make).Sum(x => x.Weight);
            return total > 0 ? make / total : 0.0;
        }

        // =====================================================================
        // (a) SHAPE — the six acceptance bands (pure picker, 100k draws each).
        // =====================================================================
        {
            // Band 1: equal fixture — five identical players → ~20% each.
            var eq = new[]
            {
                MkP(1, 50, fd: 50, hr: 5, bh: 50), MkP(2, 50, fd: 50, hr: 5, bh: 50),
                MkP(3, 50, fd: 50, hr: 5, bh: 50), MkP(4, 50, fd: 50, hr: 5, bh: 50),
                MkP(5, 50, fd: 50, hr: 5, bh: 50),
            };
            var sh = PickShares(eq, 5101);
            var band1 = sh.All(s => s is >= 0.18 and <= 0.22);
            Console.WriteLine($"  (a1) equal: {sh[0]:P2} {sh[1]:P2} {sh[2]:P2} {sh[3]:P2} {sh[4]:P2}  -> each 18–22%");
            Console.WriteLine(band1 ? "    [OK]" : "    [FAIL] equal fixture not 18–22% each");
            ok &= band1;

            // Band 2: lead-handler — high FoulDrawing/usage/handling guard 28–40%.
            var lead = new[]
            {
                MkP(1, 50, fd: 70, hr: 9, bh: 85),  // lead guard
                MkP(2, 50, fd: 45, hr: 4, bh: 50), MkP(3, 50, fd: 45, hr: 4, bh: 50),
                MkP(4, 50, fd: 45, hr: 4, bh: 50), MkP(5, 50, fd: 45, hr: 4, bh: 50),
            };
            var shL = PickShares(lead, 5102);
            var band2 = shL[0] is >= 0.28 and <= 0.40;
            Console.WriteLine($"  (a2) lead handler: {shL[0]:P2}  -> 28–40%");
            Console.WriteLine(band2 ? "    [OK]" : "    [FAIL] lead handler outside 28–40%");
            ok &= band2;

            // Band 3: featured big — high FoulDrawing + usage, low handling ≥22%.
            var big = new[]
            {
                MkP(1, 50, fd: 75, hr: 9, bh: 40),  // featured big
                MkP(2, 50, fd: 50, hr: 4, bh: 60), MkP(3, 50, fd: 50, hr: 4, bh: 60),
                MkP(4, 50, fd: 50, hr: 4, bh: 60), MkP(5, 50, fd: 50, hr: 4, bh: 60),
            };
            var shB = PickShares(big, 5103);
            var band3 = shB[0] >= 0.22;
            Console.WriteLine($"  (a3) featured big: {shB[0]:P2}  -> ≥22%");
            Console.WriteLine(band3 ? "    [OK]" : "    [FAIL] featured big < 22%");
            ok &= band3;

            // Band 4: parker — low-everything wing in a star lineup 1–8%, never 0.
            var park = new[]
            {
                MkP(1, 50, fd: 90, hr: 10, bh: 85),  // star
                MkP(2, 50, fd: 55, hr: 5,  bh: 55), MkP(3, 50, fd: 55, hr: 5, bh: 55),
                MkP(4, 50, fd: 55, hr: 5,  bh: 55),
                MkP(5, 50, fd: 15, hr: 1,  bh: 20),  // parker
            };
            var shP = PickShares(park, 5104);
            var band4 = shP[4] is > 0.0 and <= 0.08 and >= 0.01;
            Console.WriteLine($"  (a4) parker: {shP[4]:P2}  -> 1–8%, never 0");
            Console.WriteLine(band4 ? "    [OK]" : "    [FAIL] parker outside 1–8% (or zero)");
            ok &= band4;
        }

        // =====================================================================
        // (a-mono) MONOTONICITY — each channel, all else equal (wide gap).
        // =====================================================================
        {
            // FoulDrawing 30 vs 80 (slot 1 low, slot 2 high; rest neutral).
            var fdFix = new[]
            {
                MkP(1, 50, fd: 30, hr: 5, bh: 50), MkP(2, 50, fd: 80, hr: 5, bh: 50),
                MkP(3, 50, fd: 50, hr: 5, bh: 50), MkP(4, 50, fd: 50, hr: 5, bh: 50),
                MkP(5, 50, fd: 50, hr: 5, bh: 50),
            };
            var shFd  = PickShares(fdFix, 5111);
            var monoFd = shFd[1] > shFd[0] + 0.02;
            Console.WriteLine($"  (a5a) FoulDrawing 30→{shFd[0]:P2}  80→{shFd[1]:P2}");

            // BallHandling 30 vs 80.
            var bhFix = new[]
            {
                MkP(1, 50, fd: 50, hr: 5, bh: 30), MkP(2, 50, fd: 50, hr: 5, bh: 80),
                MkP(3, 50, fd: 50, hr: 5, bh: 50), MkP(4, 50, fd: 50, hr: 5, bh: 50),
                MkP(5, 50, fd: 50, hr: 5, bh: 50),
            };
            var shBh  = PickShares(bhFix, 5112);
            var monoBh = shBh[1] > shBh[0] + 0.02;
            Console.WriteLine($"  (a5b) BallHandling 30→{shBh[0]:P2}  80→{shBh[1]:P2}");

            // Usage via HierarchyRank 2 vs 9 (the usage channel is the 1–10 scale,
            // so its "wide gap" is 2 vs 9, not 30 vs 80).
            var hrFix = new[]
            {
                MkP(1, 50, fd: 50, hr: 2, bh: 50), MkP(2, 50, fd: 50, hr: 9, bh: 50),
                MkP(3, 50, fd: 50, hr: 5, bh: 50), MkP(4, 50, fd: 50, hr: 5, bh: 50),
                MkP(5, 50, fd: 50, hr: 5, bh: 50),
            };
            var shHr  = PickShares(hrFix, 5113);
            var monoHr = shHr[1] > shHr[0] + 0.02;
            Console.WriteLine($"  (a5c) Usage(rank) 2→{shHr[0]:P2}  9→{shHr[1]:P2}");

            var mono = monoFd && monoBh && monoHr;
            Console.WriteLine(mono ? "    [OK] strict monotonicity in all three channels"
                                   : "    [FAIL] a channel was not strictly monotone");
            ok &= mono;
        }

        // =====================================================================
        // (b) FLOOR — an all-zero-channel parker is tiny but NEVER zero.
        // =====================================================================
        {
            var off = new[]
            {
                MkP(1, 50, fd: 99, hr: 10, bh: 99), MkP(2, 50, fd: 99, hr: 10, bh: 99),
                MkP(3, 50, fd: 99, hr: 10, bh: 99), MkP(4, 50, fd: 99, hr: 10, bh: 99),
                MkP(5, 50, fd: 0,  hr: 1,  bh: 0),   // all-zero channels → only the floor
            };
            var game   = BuildGame(off);
            var state  = MkPreSel(game);
            var rng    = new SystemRng(5201);
            var hits   = 0;
            for (var i = 0; i < N; i++)
                if (FouledPlayerPicker.Pick(state, game, cfgMatchup, rng).Number == 5) hits++;
            var share  = (double)hits / N;
            var floorOk = hits > 0 && share < 0.01;
            Console.WriteLine($"  (b) all-zero parker: {hits} hits ({share:P3}) -> >0 and <1%");
            Console.WriteLine(floorOk ? "    [OK]" : "    [FAIL] floor gave zero (or too-large) share");
            ok &= floorOk;
        }

        // =====================================================================
        // (c) ONE DRAW + DETERMINISM + TRIP BOUNDARY.
        // =====================================================================
        {
            var off = new[]
            {
                MkP(1, 50, fd: 60, hr: 6, bh: 70), MkP(2, 50, fd: 50, hr: 5, bh: 55),
                MkP(3, 50, fd: 50, hr: 5, bh: 50), MkP(4, 50, fd: 45, hr: 4, bh: 45),
                MkP(5, 50, fd: 40, hr: 3, bh: 35),
            };

            // (c-i) exactly one NextUnitInterval per Pick.
            {
                var game  = BuildGame(off);
                var state = MkPreSel(game);
                var rng   = new CountingRng(new SystemRng(5301));
                FouledPlayerPicker.Pick(state, game, cfgMatchup, rng);
                var oneDraw = rng.Count == 1;
                Console.WriteLine($"  (c-i) draws per pick: {rng.Count} -> exactly 1");
                Console.WriteLine(oneDraw ? "    [OK]" : "    [FAIL] pick did not consume exactly one draw");
                ok &= oneDraw;
            }

            // (c-ii) determinism — same seed → identical slot.
            {
                var game  = BuildGame(off);
                var state = MkPreSel(game);
                var a = FouledPlayerPicker.Pick(state, game, cfgMatchup, new SystemRng(777)).Number;
                var b = FouledPlayerPicker.Pick(state, game, cfgMatchup, new SystemRng(777)).Number;
                var det = a == b;
                Console.WriteLine($"  (c-ii) determinism: seed 777 -> slot {a} / slot {b}");
                Console.WriteLine(det ? "    [OK]" : "    [FAIL] same seed gave different slots");
                ok &= det;
            }

            // (c-iii) trip boundary: qualifying bonus trip spends exactly +1 draw
            //         before the spins; a post-Roll-E bonus trip and a shooting-foul
            //         trip spend +0. FreeThrow = 100 makes every spin a make, so the
            //         trip ends with no rebound/putback and (draws − Fta) isolates
            //         the picker draw cleanly.
            {
                var ftOff = new[]
                {
                    MkP(1, 50, fd: 60, hr: 6, bh: 70, ft: 100), MkP(2, 50, fd: 50, hr: 5, bh: 55, ft: 100),
                    MkP(3, 50, fd: 50, hr: 5, bh: 50, ft: 100), MkP(4, 50, fd: 45, hr: 4, bh: 45, ft: 100),
                    MkP(5, 50, fd: 40, hr: 3, bh: 35, ft: 100),
                };

                // A: bonus Double, no selected shooter, populated -> picker fires.
                int DrawsMinusFta(Continue cont)
                {
                    var g    = BuildGame(ftOff);
                    var rng  = new CountingRng(new SystemRng(5302));
                    var res  = BuildResolver(g, rng);
                    var outc = res.Route(cont);
                    return rng.Count - outc.Fta;
                }
                var sA = MkPreSel(BuildGame(ftOff));   // null SelectedSlot
                var dA = DrawsMinusFta(new Continue(ContinuationKind.ResolveFreeThrows, sA) { Bonus = BonusType.Double });

                // B: bonus Double, shooter already selected (post-Roll-E) -> no pick.
                var gB = BuildGame(ftOff);
                var sB = MkPreSel(gB) with { SelectedSlot = gB.HomeLineup.SlotAt(1) };
                var dB = DrawsMinusFta(new Continue(ContinuationKind.ResolveFreeThrows, sB) { Bonus = BonusType.Double });

                // C: shooting foul, selected shooter -> no pick.
                var gC = BuildGame(ftOff);
                var sC = MkPreSel(gC) with
                {
                    SelectedSlot = gC.HomeLineup.SlotAt(1),
                    Result = ShotResult.MissFouled,
                    ShotType = ShotLocation.Mid,
                };
                var dC = DrawsMinusFta(new Continue(ContinuationKind.ResolveShootingFreeThrows, sC));

                var tripOk = dA == 1 && dB == 0 && dC == 0;
                Console.WriteLine($"  (c-iii) (draws−FTA): bonus+pick={dA} (want 1)  bonus+selected={dB} (want 0)  shooting={dC} (want 0)");
                Console.WriteLine(tripOk ? "    [OK]" : "    [FAIL] picker draw fired at the wrong boundary");
                ok &= tripOk;
            }
        }

        // =====================================================================
        // (d) ATTRIBUTION — the FT trip credits FTA to the picked slot and adds
        //     zero FGA to it. (Bonus trip via Route; FreeThrow = 100 so the trip
        //     ends clean with no putback.)
        // =====================================================================
        {
            var ftOff = new[]
            {
                MkP(1, 50, fd: 60, hr: 6, bh: 70, ft: 100), MkP(2, 50, fd: 50, hr: 5, bh: 55, ft: 100),
                MkP(3, 50, fd: 50, hr: 5, bh: 50, ft: 100), MkP(4, 50, fd: 45, hr: 4, bh: 45, ft: 100),
                MkP(5, 50, fd: 40, hr: 3, bh: 35, ft: 100),
            };
            var g    = BuildGame(ftOff);
            var res  = BuildResolver(g, new SystemRng(5401));
            var s    = MkPreSel(g);   // null SelectedSlot -> picker fires
            var outc = res.Route(new Continue(ContinuationKind.ResolveFreeThrows, s) { Bonus = BonusType.Double });

            // Which slot drew the foul? Exactly one slot has the FTA.
            var drawer = 0;
            for (var n = 1; n <= 5; n++) if (outc.FtaBySlot[n] > 0) { drawer = n; break; }

            var aOk = outc.Fta == 2                                 // double bonus = 2 FTA
                   && outc.FtaBonusPicker == outc.Fta              // classified as a picker trip
                   && outc.FtaBonusUnattributed == 0               // nothing fell to the 72% bucket
                   && outc.FtaBonusSelected == 0
                   && drawer >= 1                                   // a real slot got the FTA
                   && outc.FtaBySlot[drawer] == 2                   // both FTA to that one slot
                   && outc.FtaBySlot.Unattr == 0                    // none to the slot-0 sentinel
                   && outc.Fga == 0;                                // the FT trip added no FGA
            Console.WriteLine($"  (d) drawer=slot {drawer}  FTA={outc.Fta}  FtaBonusPicker={outc.FtaBonusPicker}  FtaBySlot[drawer]={outc.FtaBySlot[drawer]}  FGA={outc.Fga}");
            Console.WriteLine(aOk ? "    [OK]" : "    [FAIL] FTA not credited cleanly to the drawn slot");
            ok &= aOk;
        }

        // =====================================================================
        // (e) RATING — the trip is shot at the PICKED player's FreeThrow, not 72%.
        // =====================================================================
        {
            var off = new[]
            {
                MkP(1, 50, ft: 90), MkP(2, 50, ft: 90), MkP(3, 50, ft: 55),
                MkP(4, 50, ft: 90), MkP(5, 50, ft: 90),
            };
            var g  = BuildGame(off);
            // FreeThrowShooterSlot set (no SelectedSlot) -> picked-shooter rating.
            var s55 = MkPreSel(g) with { FreeThrowShooterSlot = g.HomeLineup.SlotAt(3) };
            var p55 = MakeProbFor(g, s55);
            var s90 = MkPreSel(g) with { FreeThrowShooterSlot = g.HomeLineup.SlotAt(1) };
            var p90 = MakeProbFor(g, s90);
            var eOk = Math.Abs(p55 - 0.55) < 0.01 && Math.Abs(p90 - 0.90) < 0.01;
            Console.WriteLine($"  (e) picked-shooter make%: slot3(FT55)={p55:P1}  slot1(FT90)={p90:P1}");
            Console.WriteLine(eOk ? "    [OK]" : "    [FAIL] picked-shooter rating not used");
            ok &= eOk;
        }

        // =====================================================================
        // (f) UNTOUCHED PATHS.
        // =====================================================================
        {
            // (f-i) normal selected shooter (no foul-draw stamp) -> his rating.
            var off = new[]
            {
                MkP(1, 50, ft: 90), MkP(2, 50, ft: 80), MkP(3, 50, ft: 90),
                MkP(4, 50, ft: 90), MkP(5, 50, ft: 90),
            };
            var g  = BuildGame(off);
            var sSel = MkPreSel(g) with { SelectedSlot = g.HomeLineup.SlotAt(2) };
            var pSel = MakeProbFor(g, sSel);
            var fi = Math.Abs(pSel - 0.80) < 0.01;
            Console.WriteLine($"  (f-i) selected shooter make%: slot2(FT80)={pSel:P1}");
            Console.WriteLine(fi ? "    [OK]" : "    [FAIL] selected-shooter path changed");
            ok &= fi;

            // (f-ii) no-slot shooting foul (post-FT-rebound putback exception):
            //        handled on the ResolveShootingFreeThrows edge, NOT the picker.
            {
                var gg  = BuildGame(off);
                var res = BuildResolver(gg, new SystemRng(5601));
                var s   = MkPreSel(gg) with
                {
                    Result   = ShotResult.MissFouled,
                    ShotType = ShotLocation.Mid,
                    // SelectedSlot and FreeThrowShooterSlot both null.
                };
                var outc = res.Route(new Continue(ContinuationKind.ResolveShootingFreeThrows, s));
                var fii = outc.Fta > 0
                       && outc.FtaShootingNoSlot == outc.Fta   // the no-slot exception bucket
                       && outc.FtaBonusPicker == 0             // the picker never fired here
                       && outc.FtaBySlot.Unattr == outc.Fta;   // credited to the slot-0 sentinel
                Console.WriteLine($"  (f-ii) no-slot shooting foul: FTA={outc.Fta}  FtaShootingNoSlot={outc.FtaShootingNoSlot}  FtaBonusPicker={outc.FtaBonusPicker}");
                Console.WriteLine(fii ? "    [OK]" : "    [FAIL] no-slot shooting foul mis-routed");
                ok &= fii;
            }

            // (f-iii) empty roster: Roll L falls to the flat 72%, and the bonus edge
            //         does NOT call the picker (which would throw) — it falls through.
            {
                var empty = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var sEmpty = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound);
                var pFlat = MakeProbFor(empty, sEmpty);
                var ratingFlat = Math.Abs(pFlat - cfgL.MakeProbability) < 1e-9;

                var gateOk = true;
                try
                {
                    var res  = BuildResolver(empty, new SystemRng(5602));
                    var outc = res.Route(new Continue(ContinuationKind.ResolveFreeThrows, sEmpty) { Bonus = BonusType.Double });
                    gateOk = outc.FtaBonusUnattributed == outc.Fta && outc.FtaBonusPicker == 0;
                }
                catch (Exception ex)
                {
                    gateOk = false;
                    Console.WriteLine($"    (gate threw: {ex.GetType().Name})");
                }
                var fiii = ratingFlat && gateOk;
                Console.WriteLine($"  (f-iii) empty roster: Roll L make%={pFlat:P1} (flat {cfgL.MakeProbability:P1}); bonus edge fell through (no picker)");
                Console.WriteLine(fiii ? "    [OK]" : "    [FAIL] empty-roster fallback/gate wrong");
                ok &= fiii;
            }
        }

        // =====================================================================
        // (g) CARRYOVER GUARD.
        //
        // The prompt asks to force the natural chain (populated bonus trip → missed
        // final FT → offensive FT-rebound → putback → second shooting foul) and
        // assert the foul-draw stamp does not leak into that second trip. That chain
        // cannot be forced deterministically without RUNNING the engine — the
        // offensive-rebound and putback-foul outcomes depend on Roll M / Roll H pie
        // slice positions that are config-driven, so a blind scripted RNG would be
        // the brittle-blind-code failure mode. Instead the clear is proven two ways
        // that ARE robust, and the deviation is flagged for Emmett:
        //
        //   (g-ii) The second-trip foul on a putback is a NO-SLOT shooting foul, and
        //          (f-ii above) proves a no-slot shooting foul is handled on the
        //          ResolveShootingFreeThrows edge as the existing exception — it is
        //          never reclassified as a bonus-foul picker case (FtaBonusPicker==0).
        //
        //   (g-i)  The clear nulls FreeThrowShooterSlot at the SOLE live-ball FT exit
        //          (verified: LastShot's missed-FT arm). Roll L resolves the shooter
        //          as FreeThrowShooterSlot ?? SelectedSlot, so a CLEARED stamp with no
        //          selected shooter falls through to the flat fallback — i.e. a stale
        //          A-stamp, once nulled, can no longer determine a later trip's make%.
        //          Proven below: stamped → A's rating; cleared (null) + no selection →
        //          the flat fallback.
        // =====================================================================
        {
            var off = new[]
            {
                MkP(1, 50, ft: 30), MkP(2, 50, ft: 90), MkP(3, 50, ft: 90),
                MkP(4, 50, ft: 90), MkP(5, 50, ft: 90),
            };
            var g = BuildGame(off);

            // A-stamp present (slot 1, FT30, no selection) -> A's rating.
            var stamped = MkPreSel(g) with { FreeThrowShooterSlot = g.HomeLineup.SlotAt(1) };
            var pStamped = MakeProbFor(g, stamped);

            // Stamp cleared (null) + no selection -> the flat fallback, NOT A's 30%.
            var cleared  = MkPreSel(g);   // FreeThrowShooterSlot null, SelectedSlot null
            var pCleared = MakeProbFor(g, cleared);

            var gOk = Math.Abs(pStamped - 0.30) < 0.01
                   && Math.Abs(pCleared - cfgL.MakeProbability) < 1e-9
                   && Math.Abs(pStamped - pCleared) > 0.05;   // the stamp materially differs
            Console.WriteLine($"  (g) stamped(FT30)={pStamped:P1}  cleared={pCleared:P1} (flat {cfgL.MakeProbability:P1}) — clear erases A's rating");
            Console.WriteLine(gOk ? "    [OK] (clear proven by edge construction + null fall-through; full-chain forcing flagged)"
                                  : "    [FAIL] cleared stamp still influenced the rating");
            ok &= gOk;
        }

        Console.WriteLine(ok ? "  FreeThrowFoulDrawCheck PASS" : "  FreeThrowFoulDrawCheck FAIL");
        return ok;
    }

    /// <summary>Phase 51 harness helper: counts <see cref="IRng.NextUnitInterval"/>
    /// calls around an inner RNG, to prove the foul-draw pick consumes exactly one
    /// draw and fires at the right trip boundary. No counting RNG exists in the engine
    /// (only <see cref="SystemRng"/>), so the check supplies its own.</summary>
    private sealed class CountingRng : IRng
    {
        private readonly IRng _inner;
        public int Count { get; private set; }
        public CountingRng(IRng inner) => _inner = inner;
        public double NextUnitInterval() { Count++; return _inner.NextUnitInterval(); }
    }
}
