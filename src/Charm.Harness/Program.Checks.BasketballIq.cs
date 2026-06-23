using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =====================================================================================
    // Phase 50 — Basketball IQ at the make door.
    //
    // Proves the new LAST make% term: a small, bounded, PROPORTIONAL conversion bonus driven
    // by the SHOOTER's own BasketballIQ, applied on top of the fully-settled make% (after the
    // whole C1–C8 chain) and BEFORE block/foul are carved.
    //
    // Measurement strategy (no new engine diagnostic surface needed — Roll H exposes only a
    // Pie, confirmed): every quantity is recovered by DIFFERENCING controlled runs, the same
    // "read the result, recover the number" pattern Phase 47/49 use.
    //   • The CLEAN PRE-CARVE make% is recovered from a pie as Made / (1 − block − foul).
    //     block and foul do NOT depend on IQ or the knob, so this surface is comparable across
    //     runs and is exactly the surface the 34→37 magnitude is defined on.
    //   • The DIRECT IQ term is pre-carve(knob 0.08) − pre-carve(knob 0.0) on the SAME fixture:
    //     everything else (including the C4 passing leak) is identical between the two runs, so
    //     the difference IS the direct term, exactly.
    //   • ΔC4_IQ (the indirect leak) is a COUNTERFACTUAL on conversionQuality — shooter IQ 99 vs
    //     50, fixture held — turned into C4 points, NOT C4's whole bonus.
    //
    // Part A always forces the knob to its locked default (0.08) vs 0.0, so the check proves the
    // term regardless of the shipped config value (the operator sets IqMakeSensitivity = 0 in
    // config.json for the byte-identical corpus compare; this check still passes then).
    // =====================================================================================
    private static bool Phase50BasketballIqCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 50 — Basketball IQ at the make door ==");
        var pass = true;

        var cfgM     = MatchupConfig.Load(configPath);
        var cfgD     = RollDConfig.Load(configPath);
        var cfgAttn  = AttentionConfig.Load(configPath);
        var cfgHship = RollHConfig.Load(configPath);                                  // shipped (config.json)
        var cfgHon   = RollHConfig.Load(configPath); cfgHon.IqMakeSensitivity  = 0.08; // locked default, forced
        var cfgHoff  = RollHConfig.Load(configPath); cfgHoff.IqMakeSensitivity = 0.0;  // term off

        Console.WriteLine($"  shipped IqMakeSensitivity = {cfgHship.IqMakeSensitivity:F4}"
            + (cfgHship.IqMakeSensitivity == 0.0
                ? "   [shipped OFF — Part A forces 0.08 to prove the term is correct]"
                : ""));

        // Fixed zone weights, mirrored from RollHGenerator so the check owns the same constants.
        static double ZW(ShotLocation z) => z switch
        {
            ShotLocation.Three => 1.0,
            ShotLocation.Long  => 1.0,
            ShotLocation.Mid   => 0.7,
            ShotLocation.Short => 0.3,
            ShotLocation.Rim   => 0.0,
            _                  => 0.0
        };
        static double IqProgress(int iq) => Math.Clamp((iq - 50.0) / 49.0, 0.0, 1.0);

        // Pie slice weight (no LINQ).
        static double W(Pie<ShotResult> pie, ShotResult o)
        {
            foreach (var s in pie.Slices)
                if (s.Outcome == o) return s.Weight;
            return 0.0;
        }

        // Recover the clean PRE-CARVE make% from a Roll H pie: Made / (1 − block − foul).
        static double PreCarve(Pie<ShotResult> pie)
        {
            var block = W(pie, ShotResult.Blocked);
            var foul  = W(pie, ShotResult.MadeAndFouled) + W(pie, ShotResult.MissFouled);
            var nbnf  = 1.0 - block - foul;
            return nbnf > 0.0 ? W(pie, ShotResult.Made) / nbnf : 0.0;
        }

        FoulTracker Fouls() => new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);

        // Full-attribute fixture player. Outside drives Three/Long make%; iq drives the IQ term;
        // playmaking/passing/quickness/firstStep feed the C4 conversionQuality path for Part B.
        static Player Mk(string id, int iq, int outside = 50, int playmaking = 50, int passing = 50,
                         int quickness = 50, int firstStep = 50)
            => new Player(id)
            {
                Close = 50, Mid = 50, Outside = outside, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                BallHandling = 50, Passing = passing, Playmaking = playmaking, SelfCreation = 50, PostMoves = 50,
                OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
                PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
                OffBallDefense = 50, Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
                Quickness = quickness, FirstStep = firstStep, Vertical = 50, Endurance = 50, Hustle = 50,
                BasketballIQ = iq, Discipline = 50, HierarchyRank = 5,
            };

        void Seat(GameState g, TeamSide side, Player[] five)
        {
            var roster = g.RosterFor(side);
            var lineup = g.LineupFor(side);
            for (var i = 0; i < 5; i++) roster.SetStarter(lineup.SlotAt(i + 1), five[i]);
        }

        // Settled (pre-carve) make% for a shooter at a zone under a given Roll H config.
        // Bare halfcourt fixture (no attention stamped) — settled = matchup logistic + C5.5/C6/C7.
        double Settle(RollHConfig cfgHv, Player shooter, ShotLocation zone)
        {
            var g = new GameState(Fouls());
            Seat(g, TeamSide.Home, new[] { shooter, Mk("h2", 50), Mk("h3", 50), Mk("h4", 50), Mk("h5", 50) });
            Seat(g, TeamSide.Away, new[] { Mk("a1", 50), Mk("a2", 50), Mk("a3", 50), Mk("a4", 50), Mk("a5", 50) });
            var genH = new RollHGenerator(cfgHv, cfgM, g);
            var st   = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                           SelectedSlot: g.HomeLineup.SlotAt(1), ShotType: zone);
            return PreCarve(genH.Generate(st));
        }

        // Direct IQ bump on the real path = pre-carve(on) − pre-carve(off), same fixture.
        double Bump(Player shooter, ShotLocation zone)
            => Settle(cfgHon, shooter, zone) - Settle(cfgHoff, shooter, zone);

        // ── Part A — direct IQ term through the REAL Roll H path ──────────────────────────
        Console.WriteLine("  Part A — direct term (real make door, knob 0.08 vs 0.0):");

        // (a1) proportional FORMULA holds on the real path, max IQ on a Three.
        {
            var sh      = Mk("s", 99);
            var settled = Settle(cfgHoff, sh, ShotLocation.Three);
            var bump    = Bump(sh, ShotLocation.Three);
            var expect  = settled * 0.08 * ZW(ShotLocation.Three) * IqProgress(99);
            var ok = bump > 1e-9 && Math.Abs(bump - expect) < 1e-9;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} (a1) Three IQ99: settled={settled * 100:F2}% -> {(settled + bump) * 100:F2}% "
                + $"(bump {bump * 100:+0.000;-0.000}pt; formula expects {expect * 100:+0.000;-0.000}pt)");
            pass &= ok;
        }

        // (a2) zero at/below IQ 50 (iqProgress clamps to 0).
        {
            var bump = Bump(Mk("s", 50), ShotLocation.Three);
            var ok = Math.Abs(bump) < 1e-12;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} (a2) IQ50 -> zero bump ({bump * 100:+0.000000;-0.000000}pt)");
            pass &= ok;
        }

        // (a3) zero at the Rim, even at max IQ (ZoneWeight 0.0).
        {
            var bump = Bump(Mk("s", 99), ShotLocation.Rim);
            var ok = Math.Abs(bump) < 1e-12;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} (a3) Rim IQ99 -> zero bump ({bump * 100:+0.000000;-0.000000}pt)");
            pass &= ok;
        }

        // (a4) monotone non-decreasing in IQ; strictly increasing across the range.
        {
            var b50 = Bump(Mk("s", 50), ShotLocation.Three);
            var b70 = Bump(Mk("s", 70), ShotLocation.Three);
            var b99 = Bump(Mk("s", 99), ShotLocation.Three);
            var ok = b50 <= b70 + 1e-12 && b70 <= b99 + 1e-12 && b99 > b50 + 1e-9;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} (a4) monotone: IQ50={b50 * 100:+0.000} <= IQ70={b70 * 100:+0.000} <= IQ99={b99 * 100:+0.000}pt");
            pass &= ok;
        }

        // (a5) finding #1 — a LOW settled make% gets a SMALLER ABSOLUTE bump (and each is proportional).
        {
            var weak   = Mk("w", 99, outside: 10);   // low Three make%
            var strong = Mk("g", 99, outside: 99);   // high Three make%
            var sW = Settle(cfgHoff, weak,   ShotLocation.Three); var bW = Bump(weak,   ShotLocation.Three);
            var sG = Settle(cfgHoff, strong, ShotLocation.Three); var bG = Bump(strong, ShotLocation.Three);
            var fOk    = bW < bG - 1e-9;                                                    // low base, smaller absolute bump
            var propOk = Math.Abs(bW - sW * 0.08) < 1e-9 && Math.Abs(bG - sG * 0.08) < 1e-9; // each = settled × 0.08
            var ok = fOk && propOk;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} (a5) low-base smaller bump: weak settled={sW * 100:F2}% bump={bW * 100:+0.000}pt"
                + $" < strong settled={sG * 100:F2}% bump={bG * 100:+0.000}pt (each = settled x 0.08)");
            pass &= ok;
        }

        // (a6) zone taper — bump / settled == 0.08 × ZoneWeight at each zone (settled cancels).
        {
            var sh = Mk("s", 99);
            bool ok = true;
            foreach (var z in new[] { ShotLocation.Three, ShotLocation.Long, ShotLocation.Mid, ShotLocation.Short, ShotLocation.Rim })
            {
                var settled = Settle(cfgHoff, sh, z);
                var bump    = Bump(sh, z);
                var ratio   = settled > 0 ? bump / settled : 0.0;
                var expect  = 0.08 * ZW(z);
                var zok = Math.Abs(ratio - expect) < 1e-9;
                ok &= zok;
                Console.WriteLine($"      {(zok ? "ok  " : "FAIL")} {z,-5}: settled={settled * 100:F2}% bump={bump * 100:+0.000}pt  bump/settled={ratio:F4} (expect {expect:F4})");
            }
            pass &= ok;
        }

        // ── Part B — C4 partition (finding #4) ───────────────────────────────────────────
        // The direct term is distinct from the indirect IQ leak through C4
        // (IQ -> playmaking activation -> conversionQuality -> C4 passing bonus -> make%).
        Console.WriteLine("  Part B — C4 partition (high-IQ / high-playmaking shooter):");
        {
            var fixedShares = new double[5] { 0.2, 0.2, 0.2, 0.2, 0.2 };  // ignored by conversionQuality

            // conversionQuality for a lineup whose slot-1 shooter has BasketballIQ = iq
            // (PM85 / PASS75 / QCK80 / FS80) and four modest mates. Measured through the
            // production AttentionGenerator — the same observable Phase 47 asserts on.
            double Cq(int iq)
            {
                var g = new GameState(Fouls());
                Seat(g, TeamSide.Home, new[]
                {
                    Mk("s",  iq, playmaking: 85, passing: 75, quickness: 80, firstStep: 80),
                    Mk("m2", 50, playmaking: 40, passing: 60), Mk("m3", 50, playmaking: 40, passing: 60),
                    Mk("m4", 50, playmaking: 40, passing: 60), Mk("m5", 50, playmaking: 40, passing: 60),
                });
                Seat(g, TeamSide.Away, new[] { Mk("a1", 50), Mk("a2", 50), Mk("a3", 50), Mk("a4", 50), Mk("a5", 50) });
                var attn = new AttentionGenerator(cfgAttn, g);
                var st   = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
                return attn.Generate(st, fixedShares).TeamConversionQuality;
            }

            var cq99 = Cq(99);
            var cq50 = Cq(50);
            const double openness = 0.5;
            var gate   = cfgHship.PassingOpportunityFloor + (1.0 - cfgHship.PassingOpportunityFloor) * openness;
            var dC4    = cfgHship.MaxPassingBonus * (cq99 - cq50) * gate;   // counterfactual — NOT C4's whole bonus
            var fullC4 = cfgHship.MaxPassingBonus * cq99 * gate;            // whole C4 bonus (mostly Passing/PM/gravity)

            // direct term on the SAME shooter (Three, IQ99) through the real path
            var directSh   = Mk("s", 99, playmaking: 85, passing: 75, quickness: 80, firstStep: 80);
            var directBump = Bump(directSh, ShotLocation.Three);

            var dC4Ok  = dC4 > 0.0 && dC4 < fullC4;        // IQ is only a FRACTION of C4
            var partOk = directBump > 10.0 * dC4;          // direct term dwarfs the leak -> no meaningful double-count
            Console.WriteLine($"    deltaC4_IQ (counterfactual, openness 0.5) = {dC4 * 100:+0.000}pt  "
                + $"[{(fullC4 > 0 ? dC4 / fullC4 * 100 : 0):F1}% of the full C4 bonus {fullC4 * 100:F3}pt]");
            Console.WriteLine($"    direct IQ term (Three, IQ99)              = {directBump * 100:+0.000}pt");
            Console.WriteLine($"    combined IQ footprint                     = {(dC4 + directBump) * 100:+0.000}pt (= deltaC4_IQ + direct)");
            Console.WriteLine($"    {(dC4Ok ? "ok  " : "FAIL")} deltaC4_IQ is a fraction of C4 (not its whole bonus)");
            Console.WriteLine($"    {(partOk ? "ok  " : "FAIL")} direct term >> deltaC4_IQ -> no meaningful double-count");
            pass &= dC4Ok && partOk;
        }

        // ── Part C — zero-knob inertness (in-process portion) ────────────────────────────
        // With IqMakeSensitivity = 0 the term contributes EXACTLY zero at every zone, even for a
        // max-IQ shooter, so the operator's knob-0 corpus run is byte-identical to pre-Phase-50
        // (except config-hash / run-manifest metadata). The full byte-compare is the operator's
        // run — Claude has no .NET SDK (see the "what to watch" note).
        {
            var sh = Mk("s", 99);
            var lo = Mk("s2", 50);
            bool ok = true;
            foreach (var z in new[] { ShotLocation.Three, ShotLocation.Long, ShotLocation.Mid, ShotLocation.Short, ShotLocation.Rim })
            {
                var maxIq = Settle(cfgHoff, sh, z);
                var minIq = Settle(cfgHoff, lo, z);
                ok &= Math.Abs(maxIq - minIq) < 1e-12;   // knob 0 -> IQ has no effect at all
            }
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} Part C inertness: knob=0 -> IQ has zero effect at every zone (max-IQ == min-IQ pre-carve)");
            pass &= ok;
        }

        Console.WriteLine(pass ? "  Phase50BasketballIqCheck: PASS" : "  Phase50BasketballIqCheck: FAIL");
        return pass;
    }
}
