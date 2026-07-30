using System.Globalization;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    /// <summary>
    /// Phase 77 (Session 86) — THE TRANSITION OPPORTUNITY SCORE AND THE COACH BAR.
    /// Golden parity against the locked oracle plus the structural guards around it. No
    /// basketball TARGET is asserted here: every rate the session moves lives on the season
    /// page, unasserted, per the page-only calibration principle. What is asserted is that
    /// the C# port reproduces the approved oracle, that the neutral rule holds on all four
    /// of its early-outs, that the pie's mass survives saturation, and that each ruling in
    /// C-32 is true as an INEQUALITY on the actual function rather than as prose.
    ///
    /// <para>Why golden parity is the spine. The escape/race/bar shape was signed off by
    /// reading three archetype tables, so the tables ARE the spec. A check that re-derived
    /// the formula would only prove the formula equals itself; comparing against the Python
    /// oracle's own printed output proves the PORT. The bar is 1e-6 absolute on a
    /// probability — far above Python-vs-.NET <c>tanh</c> libm noise (~1e-16), and far below
    /// the 0.05pp a rendered one-decimal table could silently absorb. This is deliberately
    /// NOT a bitwise bar: S81.3 shipped a bit-exact cross-platform fixture and produced a red
    /// suite on Emmett's machine with nothing wrong in the engine.</para>
    ///
    /// <para>Two things worth knowing about what is NOT here. The FREE-THROW rebound source
    /// is exempt from the whole wire (Emmett's ruling, S86) — B8 asserts that exemption is
    /// real rather than assuming it, because an exemption that silently lapses is exactly the
    /// kind of change a green suite would hide. And B5's overlap quad uses hard-coded shared
    /// inputs rather than the approved table rows: those rows vary teammate speed, so they
    /// cannot isolate the overlap ruling, and near saturation the strict inequalities would
    /// pass for the wrong reason.</para>
    /// </summary>
    private static bool Phase77TransitionOpportunityCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("Phase 77 — transition opportunity score + coach bar (Session 86):");

        var ok = true;
        var cfgJ = RollJConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgF = FatigueConfig.Load(configPath);

        void Check(string label, bool pass, string? why = null)
        {
            Console.WriteLine($"    {(pass ? "ok  " : "FAIL")} {label}"
                              + (why is null ? "" : $"  ({why})"));
            ok &= pass;
        }

        // ── Local bench ───────────────────────────────────────────────────────────────
        // A player whose Speed and Passing are set independently and everything else flat.
        // Fatigue is NEVER accrued on these benches, so the discount is exactly 1.0 and an
        // authored integer Speed reaches the wire unchanged — which is what lets the oracle's
        // integer inputs be compared to the C# port at all.
        static Player Man(int id, int speed, int passing) => new Player($"s86_{id}")
        {
            PlayerId = id,
            Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
            RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
            BallHandling = 50, Passing = passing, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
            OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
            PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
            Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = speed,
            Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50,
            Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50, OffBallDefense = 50,
            HierarchyRank = 5,
        };

        // Seat the ball-handler in slot 1, four teammates at matesSpeed, five defenders at
        // defSpeed, and stamp the offensive coach's pace. Slots are seated the way
        // RollJGenerator reads them (roster.PlayerAt(lineup.SlotAt(slot))).
        GameState Bench(int handlerSpeed, int handlerPassing, int matesSpeed, int defSpeed, double pace,
                        bool seatOffense = true)
        {
            var g = new GameState(
                new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold),
                fatigue: new FatigueTracker(cfgF));
            g.SetCoach(TeamSide.Home, new CoachProfile(paceBias: pace));
            g.SetCoach(TeamSide.Away, new CoachProfile(paceBias: 5.0));
            if (seatOffense)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), Man(1, handlerSpeed, handlerPassing));
                for (var s = 2; s <= 5; s++)
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(s), Man(s, matesSpeed, 50));
            }
            for (var s = 1; s <= 5; s++)
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(s), Man(10 + s, defSpeed, 50));
            return g;
        }

        static TransitionContext Ticket(TransitionSource source, StealOrigin? origin, int? handlerSlot) =>
            new TransitionContext(source) { Origin = origin, OffenseSide = TeamSide.Home, BallHandlerSlot = handlerSlot };

        static double PushOf(Pie<TransitionOutcome> pie)
        {
            foreach (var (outcome, weight) in pie.Slices)
                if (outcome == TransitionOutcome.Push) return weight;
            throw new InvalidOperationException("Roll J pie has no Push slice.");
        }

        static double SettleOf(Pie<TransitionOutcome> pie)
        {
            foreach (var (outcome, weight) in pie.Slices)
                if (outcome == TransitionOutcome.Settle) return weight;
            throw new InvalidOperationException("Roll J pie has no Settle slice.");
        }

        double Push(int spd, int pass, int mates, int def, double pace,
                    TransitionSource source = TransitionSource.Rebound, StealOrigin? origin = null)
        {
            var g = Bench(spd, pass, mates, def, pace);
            return PushOf(new RollJGenerator(cfgJ, cfgM, g).Generate(Ticket(source, origin, 1)));
        }

        // ── B1 — golden parity against the locked oracle ──────────────────────────────
        // The fixture is the oracle's OWN --emit-golden output, byte for byte, so its
        // provenance needs no interpretation. Regenerate with:
        //   python3 tools/transition_opportunity_oracle.py --emit-golden > tools/transition_opportunity_golden.txt
        {
            const double Tol = 1e-6;
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "transition_opportunity_golden.txt");
            if (!File.Exists(path))
            {
                Check("B1 golden fixture present", false, $"missing {path}");
            }
            else
            {
                var lines = File.ReadAllLines(path)
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToArray();
                var worst = 0.0;
                var worstLabel = "";
                var parsed = 0;
                var mismatches = 0;
                foreach (var line in lines)
                {
                    // REB spd=85 pass=55 mates=60 def=50 pace=2 push=0.467390095233
                    var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length != 7) { mismatches++; continue; }
                    static int I(string kv) => int.Parse(kv.Split('=')[1], CultureInfo.InvariantCulture);
                    static double D(string kv) => double.Parse(kv.Split('=')[1], CultureInfo.InvariantCulture);

                    var (source, origin) = f[0] switch
                    {
                        "REB" => (TransitionSource.Rebound, (StealOrigin?)null),
                        "BCS" => (TransitionSource.Steal, StealOrigin.BackcourtVictim),
                        "FCS" => (TransitionSource.Steal, StealOrigin.FrontcourtVictim),
                        _     => throw new InvalidOperationException($"unknown golden tag '{f[0]}'"),
                    };
                    var actual = Push(I(f[1]), I(f[2]), I(f[3]), I(f[4]), D(f[5]), source, origin);
                    var delta = Math.Abs(actual - D(f[6]));
                    if (delta > worst) { worst = delta; worstLabel = line; }
                    parsed++;
                }
                Check($"B1 golden parity: {parsed} cases, worst |delta| {worst:E2} <= {Tol:E0}",
                      parsed == 45 && mismatches == 0 && worst <= Tol,
                      parsed == 45 ? worstLabel : $"expected 45 parsed cases, got {parsed} ({mismatches} unparsable)");
            }
        }

        // ── B2 — the neutral rule, on each of its four early-outs separately ──────────
        // Four assertions, not one: they take four DIFFERENT routes out of the wire, and a
        // single combined check would pass while three of them were broken.
        {
            var g = Bench(90, 90, 90, 10, 9.0);   // inputs that would move Push hard if read
            var gen = new RollJGenerator(cfgJ, cfgM, g);

            Check("B2a null OffenseSide -> configured base weights exactly",
                  PushOf(gen.Generate(TransitionContext.Rebound with { BallHandlerSlot = 1 })) == cfgJ.Push);
            Check("B2b null BallHandlerSlot -> configured base weights exactly",
                  PushOf(gen.Generate(Ticket(TransitionSource.Rebound, null, null))) == cfgJ.Push);

            var unseated = new RollJGenerator(cfgJ, cfgM, Bench(90, 90, 90, 10, 9.0, seatOffense: false));
            Check("B2c named seat resolves to no player -> configured base weights exactly",
                  PushOf(unseated.Generate(Ticket(TransitionSource.Rebound, null, 1))) == cfgJ.Push);

            Check("B2d steal ticket with no ball-handler -> configured steal base exactly",
                  PushOf(gen.Generate(Ticket(TransitionSource.Steal, StealOrigin.BackcourtVictim, null)))
                      == cfgJ.BackcourtVictimPush);
        }

        // ── B8 — the free-throw board is EXEMPT, and the exemption is real ────────────
        // Emmett's ruling (S86): base Push 0.08 against a 0.22 swing would pin a slow
        // rebounder to exactly zero and send everyone with legs to ~28% — a source with no
        // middle. The locked oracle's tables and golden fixture cover only the live board and
        // the two steals. Asserted across the extremes because an exemption that lapses would
        // otherwise show up first as a moved season number nobody could explain.
        {
            var inert = true;
            foreach (var (spd, pass, mates, def) in new[]
                     { (99, 99, 99, 1), (1, 1, 1, 99), (52, 45, 52, 52), (30, 30, 50, 50) })
                foreach (var pace in new[] { 1.0, 5.0, 10.0 })
                {
                    var g = Bench(spd, pass, mates, def, pace);
                    var pie = new RollJGenerator(cfgJ, cfgM, g)
                        .Generate(Ticket(TransitionSource.FreeThrowRebound, null, 1));
                    inert &= PushOf(pie) == cfgJ.FreeThrowPush && SettleOf(pie) == cfgJ.FreeThrowSettle;
                }
            Check("B8 free-throw rebound source inert at every extreme (12 cells)", inert,
                  "ruled out of S86's wall; joins with its own archetype table later");
        }

        // ── B3 — mass conservation, including the clamps the dials cannot reach ───────
        // The configured dials reach NEITHER clamp on any wired source (swing 0.22 against a
        // smallest base Push of 0.35 and a smallest base Settle of 0.35), so the clamps can
        // only be proven by calling the helper directly. That is why the bound was extracted.
        {
            var clampOk = true;
            foreach (var (bp, bs) in new[] { (0.30, 0.60), (0.55, 0.35), (0.35, 0.55), (0.08, 0.82) })
                foreach (var raw in new[] { -10.0, -1.0, -bp - 1e-9, 0.0, bs + 1e-9, 1.0, 10.0 })
                {
                    var t = RollJGenerator.BoundPushSettleTransfer(bp, bs, raw);
                    var push = bp + t;
                    var settle = bs - t;
                    clampOk &= push + settle == bp + bs          // mass conserved EXACTLY
                            && push >= 0.0 && settle >= 0.0
                            && push <= bp + bs && settle <= bp + bs;
                }
            Check("B3a bounded transfer: both clamps bind, mass conserved exactly, weights in range",
                  clampOk);

            Check("B3b lower clamp binds at -basePush", RollJGenerator.BoundPushSettleTransfer(0.30, 0.60, -5.0) == -0.30);
            Check("B3c upper clamp binds at +baseSettle", RollJGenerator.BoundPushSettleTransfer(0.30, 0.60, 5.0) == 0.60);

            // End-to-end saturation through Generate: the five weights still sum to 1. Pie
            // validates sum-to-one and THROWS rather than normalising, so a returned pie is
            // itself part of the proof.
            var sumOk = true;
            foreach (var (spd, pass, mates, def, pace) in new[]
                     { (99, 99, 99, 1, 10.0), (1, 1, 1, 99, 1.0) })
                foreach (var (source, origin) in new[]
                         { (TransitionSource.Rebound, (StealOrigin?)null),
                           (TransitionSource.Steal, StealOrigin.BackcourtVictim),
                           (TransitionSource.Steal, StealOrigin.FrontcourtVictim) })
                {
                    var pie = new RollJGenerator(cfgJ, cfgM, Bench(spd, pass, mates, def, pace))
                        .Generate(Ticket(source, origin, 1));
                    var sum = 0.0;
                    foreach (var (_, w) in pie.Slices) sum += w;
                    sumOk &= Math.Abs(sum - 1.0) <= cfgJ.Epsilon;
                }
            Check("B3d end-to-end saturation: five weights still sum to 1", sumOk);
        }

        // ── B4 — monotonicity, one dial at a time ────────────────────────────────────
        {
            static bool Rising(IEnumerable<double> xs)
            {
                double? prev = null;
                foreach (var x in xs) { if (prev is { } p && x <= p) return false; prev = x; }
                return true;
            }
            static bool Falling(IEnumerable<double> xs)
            {
                double? prev = null;
                foreach (var x in xs) { if (prev is { } p && x >= p) return false; prev = x; }
                return true;
            }
            var rungs = new[] { 20, 35, 50, 65, 80 };

            Check("B4a push rises in ball-handler SPEED",
                  Rising(rungs.Select(v => Push(v, 50, 50, 50, 5.0))));
            Check("B4b push rises in ball-handler PASSING",
                  Rising(rungs.Select(v => Push(50, v, 50, 50, 5.0))));
            Check("B4c push rises in TEAMMATE speed",
                  Rising(rungs.Select(v => Push(50, 50, v, 50, 5.0))));
            Check("B4d push FALLS in DEFENDER speed",
                  Falling(rungs.Select(v => Push(50, 50, 50, v, 5.0))));
            Check("B4e push rises in coach PACE",
                  Rising(new[] { 1.0, 3.0, 5.0, 7.0, 10.0 }.Select(p => Push(50, 50, 50, 50, p))));
        }

        // ── B5 — the overlap ruling, as inequalities on the actual function ──────────
        // Race, pace and base pie held IDENTICAL across all four rows, hard-coded away from
        // saturation so no clamp or tanh flattening can make these pass for the wrong reason.
        // Rulings 2 and 3: a second escape route ALWAYS pays, and always pays LESS than a
        // first one. ("LeBron gets the best of both worlds" — but not double.)
        {
            var lowLow      = Push(30, 30, 50, 50, 5.0);
            var speedOnly   = Push(90, 30, 50, 50, 5.0);
            var passingOnly = Push(30, 90, 50, 50, 5.0);
            var both        = Push(90, 90, 50, 50, 5.0);

            Console.WriteLine(
                $"      overlap quad: low-low {100 * lowLow:F4}%  speed-only {100 * speedOnly:F4}%  " +
                $"passing-only {100 * passingOnly:F4}%  both {100 * both:F4}%");

            Check("B5a a second route always pays (both > speed-only)", both - speedOnly > 0.0);
            Check("B5b and pays LESS than the first (both-speedOnly < passingOnly-lowLow)",
                  both - speedOnly < passingOnly - lowLow);
            Check("B5c a second route always pays (both > passing-only)", both - passingOnly > 0.0);
            Check("B5d and pays LESS than the first (both-passingOnly < speedOnly-lowLow)",
                  both - passingOnly < speedOnly - lowLow);
            Check("B5e escape is symmetric in legs and outlet (speed-only == passing-only)",
                  Math.Abs(speedOnly - passingOnly) <= 1e-12);
        }

        // ── B6 — config guards throw on load ─────────────────────────────────────────
        {
            bool Throws(string key, JsonNode value)
            {
                var tmp = Path.Combine(Path.GetTempPath(), $"s86_cfg_{key}_{Guid.NewGuid():N}.json");
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                    node["RollJ"]![key] = value;
                    File.WriteAllText(tmp, node.ToJsonString());
                    try { RollJConfig.Load(tmp); return false; }
                    catch (InvalidOperationException) { return true; }
                }
                catch { return false; }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            Check("B6a OverlapCredit = 1 throws", Throws("OverlapCredit", 1.0),
                  "at 1 the two escape routes count equally — the pre-fused form R2 rejects");
            Check("B6b OverlapCredit < 0 throws", Throws("OverlapCredit", -0.01));
            Check("B6c EscapeWeight+RaceWeight != 1 throws", Throws("EscapeWeight", 0.60),
                  "they are shares of ONE score, not independent scales");
            Check("B6d PushSwing < 0 throws", Throws("PushSwing", -0.01));
            Check("B6e MarginScale = 0 throws", Throws("MarginScale", 0.0), "tanh denominator");
            Check("B6f BarPaceSwing < 0 throws", Throws("BarPaceSwing", -0.01),
                  "negative would make a run-and-gun coach RAISE the bar");

            // PushSwing = 0 is the KILL SWITCH: it must LOAD CLEANLY and reproduce the
            // configured base weights everywhere (the Phase 75 pattern). A guard that
            // rejected zero would remove the one dial setting that proves the wire is
            // additive-on-top rather than a rewrite of the pie.
            var killTmp = Path.Combine(Path.GetTempPath(), $"s86_kill_{Guid.NewGuid():N}.json");
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["RollJ"]!["PushSwing"] = 0.0;
                File.WriteAllText(killTmp, node.ToJsonString());
                var zeroCfg = RollJConfig.Load(killTmp);
                var killOk = true;
                foreach (var (source, origin, expected) in new[]
                         { (TransitionSource.Rebound, (StealOrigin?)null, zeroCfg.Push),
                           (TransitionSource.Steal, StealOrigin.BackcourtVictim, zeroCfg.BackcourtVictimPush),
                           (TransitionSource.Steal, StealOrigin.FrontcourtVictim, zeroCfg.FrontcourtVictimPush),
                           (TransitionSource.FreeThrowRebound, null, zeroCfg.FreeThrowPush) })
                {
                    var pie = new RollJGenerator(zeroCfg, cfgM, Bench(99, 99, 99, 1, 10.0))
                        .Generate(Ticket(source, origin, 1));
                    killOk &= PushOf(pie) == expected;
                }
                Check("B6g PushSwing = 0 loads and reproduces base weights everywhere (kill switch)", killOk);
            }
            catch (Exception ex)
            {
                Check("B6g PushSwing = 0 loads and reproduces base weights everywhere (kill switch)",
                      false, ex.Message);
            }
            finally { if (File.Exists(killTmp)) File.Delete(killTmp); }
        }

        // ── B9 — fatigue reaches the race, and only through legs ─────────────────────
        // Tired legs run less; a tired man's OUTLET PASS is unaffected (Emmett's ruling: passing
        // is not legs). The second half is the discriminating one — without it, "fatigue is
        // wired" would pass even if the discount had been applied to the whole player.
        {
            var g = Bench(80, 80, 80, 50, 5.0);
            var gen = new RollJGenerator(cfgJ, cfgM, g);
            var ctx = Ticket(TransitionSource.Rebound, null, 1);
            var fresh = PushOf(gen.Generate(ctx));

            // Tire the offense only. Fatigue lives on GameState, so this is a real mid-check
            // state change (§2a) — deliberate, and the read below is the only one after it.
            for (var i = 0; i < 400; i++)
                g.Fatigue.Accrue(new[] { g.HomeRoster.PlayerAt(g.HomeLineup.SlotAt(1)),
                                         g.HomeRoster.PlayerAt(g.HomeLineup.SlotAt(2)),
                                         g.HomeRoster.PlayerAt(g.HomeLineup.SlotAt(3)),
                                         g.HomeRoster.PlayerAt(g.HomeLineup.SlotAt(4)),
                                         g.HomeRoster.PlayerAt(g.HomeLineup.SlotAt(5)) });
            var tired = PushOf(gen.Generate(ctx));
            Check($"B9a tired offense pushes less ({100 * fresh:F3}% -> {100 * tired:F3}%)", tired < fresh);

            // A gassed man's OUTLET PASS is undiscounted — and this needs a check that can
            // tell the right wire from the wrong one, not merely one that passes.
            //
            // Take a pure passer (Speed 1, Passing 90). Gassing him MUST still move his push a
            // little, because the overlap credit reads his legs as his SECOND route — so "did
            // not move at all" is the wrong bar and an earlier draft of this check failed on
            // exactly that mistake. What distinguishes the wires is the SIZE of the move. If
            // only legs are discounted, his better route stays at 90 and the move is a hair.
            // If passing were discounted too, his better route would fall to ~81, which is a
            // move roughly two orders of magnitude larger. The third bench below constructs
            // that wrong wire by hand, so the check rejects it rather than trusting the comment.
            var gFresh  = Bench(1, 90, 50, 50, 5.0);
            var gGassed = Bench(1, 90, 50, 50, 5.0);
            var gWrong  = Bench(1, 81, 50, 50, 5.0);   // what a discounted-passing wire produces
            var passer = gGassed.HomeRoster.PlayerAt(gGassed.HomeLineup.SlotAt(1));
            for (var i = 0; i < 400; i++) gGassed.Fatigue.Accrue(new[] { passer });
            var pFresh  = PushOf(new RollJGenerator(cfgJ, cfgM, gFresh).Generate(ctx));
            var pGassed = PushOf(new RollJGenerator(cfgJ, cfgM, gGassed).Generate(ctx));
            var pWrong  = PushOf(new RollJGenerator(cfgJ, cfgM, gWrong).Generate(ctx));
            var legsOnly = Math.Abs(pGassed - pFresh);
            var wouldBe  = Math.Abs(pWrong - pFresh);
            Console.WriteLine(
                $"      pure passer: fresh {100 * pFresh:F4}%  gassed {100 * pGassed:F4}%  " +
                $"(a discounted-passing wire would read {100 * pWrong:F4}%)");
            Check("B9b gassing a pure passer moves push only through his SECOND route",
                  pGassed < pFresh && legsOnly > 0.0);
            Check("B9c and that move is far smaller than a discounted-passing wire's",
                  wouldBe > 20.0 * legsOnly,
                  $"legs-only {legsOnly:E2} vs discounted-passing {wouldBe:E2}");
        }

        Console.WriteLine(ok ? "  Phase 77: [OK]" : "  Phase 77: [FAIL]");
        return ok;
    }
}
