using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =====================================================================================
    // Phase 49 — Fatigue → Effective Athleticism.
    //
    // Proves the fatigue meter (Phase 48) now BITES: a tired player's effective athleticism
    // is discounted (steeper on defense than offense, hard-floored, linear in the meter) at
    // ALL FIVE athleticism read-sites, and fresh players are untouched.
    //
    // The check is DUAL-MODE so it passes under BOTH the shipped active config (Offense 0.10 /
    // Defense 0.20) AND the all-zero inertness control the operator uses for the Phase-48
    // byte-compare:
    //   • active config  → asserts the documented MOVEMENT and the offense/defense asymmetry.
    //   • inert  config  → asserts NO movement (tired == fresh), i.e. the effect is fully off.
    // Either way the helper's value is asserted against the closed-form formula at every level.
    //
    // A1's invariant: a make-door-only test would pass even if RollE/A/J/K were never wired,
    // so each of the five sites is exercised INDEPENDENTLY through its own generator/primitive.
    // =====================================================================================
    private static bool FatigueAthleticismCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 49 — Fatigue -> Effective Athleticism ==");
        var pass = true;

        var cfgM   = MatchupConfig.Load(configPath);
        var cfgFat = FatigueConfig.Load(configPath);
        var cfgD   = RollDConfig.Load(configPath);
        var cfgE   = RollEConfig.Load(configPath);
        var cfgA   = RollAConfig.Load(configPath);
        var cfgJ   = RollJConfig.Load(configPath);
        var cfgK   = RollKConfig.Load(configPath);
        var cfgH   = RollHConfig.Load(configPath);

        var offDrop = cfgFat.OffenseAthleticismDrop;
        var defDrop = cfgFat.DefenseAthleticismDrop;
        var ceiling = cfgFat.Ceiling;
        var inert   = offDrop == 0.0 && defDrop == 0.0;

        Console.WriteLine(
            $"  config: OffenseAthleticismDrop={offDrop:F4}, DefenseAthleticismDrop={defDrop:F4}, " +
            $"Ceiling={ceiling:F1}{(inert ? "   [INERT CONTROL — expecting NO movement]" : "")}");

        // ── tiny helpers ───────────────────────────────────────────────────────────────
        // No-LINQ-dependent weight read of a pie slice (clear + cheap).
        static double W<T>(Pie<T> pie, T outcome) where T : struct, Enum
        {
            foreach (var s in pie.Slices)
                if (EqualityComparer<T>.Default.Equals(s.Outcome, outcome)) return s.Weight;
            return 0.0;
        }

        // Full-attribute fixture player: athletic five = ath (so Athleticism == ath), distinct
        // PlayerId (uniqueness is the harness's job — the meter is PlayerId-keyed), set Endurance.
        static Player Mk(int id, int ath, int endurance = 50) => new Player($"fa{id}")
        {
            PlayerId = id,
            Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
            RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
            BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
            OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
            PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
            OffBallDefense = 50, Height = 50, Wingspan = 50, Weight = 50,
            Strength = ath, Speed = ath, Quickness = ath, FirstStep = ath, Vertical = ath,
            Endurance = endurance, Hustle = 50, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
        };

        // Accrue a single player's fatigue n times (drives his meter up so he is "tired").
        static void Tire(FatigueTracker t, Player p, int n)
        {
            var one = new Player?[] { p };
            for (var i = 0; i < n; i++) t.Accrue(one);
        }

        static void Seat(GameState g, TeamSide side, Player[] five)
        {
            var roster = g.RosterFor(side);
            var lineup = g.LineupFor(side);
            for (var i = 0; i < 5; i++) roster.SetStarter(lineup.SlotAt(i + 1), five[i]);
        }

        FoulTracker Fouls() => new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold);

        // Directional assertion that flips meaning under the inert control.
        // active: expect (tired - fresh) to have sign `dir` with magnitude > 1e-6.
        // inert : expect tired == fresh (|delta| < 1e-9).
        bool Moved(string label, double fresh, double tired, int dir)
        {
            var delta = tired - fresh;
            bool ok;
            if (inert) ok = Math.Abs(delta) < 1e-9;
            else       ok = dir > 0 ? delta > 1e-9 : delta < -1e-9;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}: fresh={fresh:F6} tired={tired:F6} delta={delta:+0.000000;-0.000000}"
                              + (inert ? "  (inert: expect 0)" : $"  (expect {(dir > 0 ? "up" : "down")})"));
            return ok;
        }

        // ── Part 1 — the helper, direct ───────────────────────────────────────────────
        {
            Console.WriteLine("  Part 1 — helper (formula, fresh=full, monotone, defense-steeper, floor):");
            var t = new FatigueTracker(cfgFat);
            var p = Mk(900, 60);              // Athleticism = 60
            double A = p.Athleticism;

            // (a) fresh = full, both roles.
            bool freshFull = Math.Abs(t.EffectiveAthleticism(p, false) - A) < 1e-9
                          && Math.Abs(t.EffectiveAthleticism(p, true ) - A) < 1e-9;
            Console.WriteLine($"    {(freshFull ? "ok  " : "FAIL")} (a) fresh=full: off={t.EffectiveAthleticism(p, false):F6} def={t.EffectiveAthleticism(p, true):F6} (authored {A:F6})");
            pass &= freshFull;

            // (b/c/e) walk the meter up; at every level assert the closed-form value (linear),
            // non-increasing (monotone), and the role relationship.
            bool formula = true, monoOff = true, monoDef = true, role = true, sawStrict = false;
            double prevOff = double.PositiveInfinity, prevDef = double.PositiveInfinity;
            var one = new Player?[] { p };
            for (var step = 0; step <= 300; step++)
            {
                var lvl  = t.LevelFor(p.PlayerId);
                var eOff = t.EffectiveAthleticism(p, false);
                var eDef = t.EffectiveAthleticism(p, true);
                var xOff = A * (1.0 - offDrop * (lvl / ceiling));
                var xDef = A * (1.0 - defDrop * (lvl / ceiling));
                if (Math.Abs(eOff - xOff) > 1e-9 || Math.Abs(eDef - xDef) > 1e-9) formula = false;
                if (eOff > prevOff + 1e-12) monoOff = false;
                if (eDef > prevDef + 1e-12) monoDef = false;
                if (!double.IsInfinity(prevOff) && eOff < prevOff - 1e-12) sawStrict = true;
                if (lvl > 0.0)
                {
                    if (inert) { if (Math.Abs(eDef - eOff) > 1e-12) role = false; }    // equal when off
                    else       { if (eDef >= eOff - 1e-12)         role = false; }     // def strictly lower
                }
                prevOff = eOff; prevDef = eDef;
                t.Accrue(one);
            }
            Console.WriteLine($"    {(formula ? "ok  " : "FAIL")} (e) effective == A*(1 - drop*level/Ceiling) at every level (linear, no curvature)");
            Console.WriteLine($"    {(monoOff && monoDef ? "ok  " : "FAIL")} (b) monotone non-increasing, both roles" + ((!inert && sawStrict) ? " (strict decrease observed)" : inert ? " (inert: constant)" : ""));
            Console.WriteLine($"    {(role ? "ok  " : "FAIL")} (c) defense vs offense at level>0: {(inert ? "equal (inert)" : "defense strictly lower")}");
            pass &= formula && monoOff && monoDef && role;
            if (!inert) { pass &= sawStrict; if (!sawStrict) Console.WriteLine("    FAIL (b) expected a strict decrease under active drops but saw none"); }

            // (d) floor: accrue hard to clamp the meter to Ceiling, then check exact floors.
            for (var i = 0; i < 20000; i++) t.Accrue(one);
            var lvlMax   = t.LevelFor(p.PlayerId);
            var floorOff = t.EffectiveAthleticism(p, false);
            var floorDef = t.EffectiveAthleticism(p, true);
            bool clamped = Math.Abs(lvlMax - ceiling) < 1e-9;
            bool floorOk = clamped
                        && Math.Abs(floorOff - (1.0 - offDrop) * A) < 1e-9
                        && Math.Abs(floorDef - (1.0 - defDrop) * A) < 1e-9
                        && floorOff > 0.0 && floorDef > 0.0;
            Console.WriteLine($"    {(floorOk ? "ok  " : "FAIL")} (d) floor @Ceiling: off={floorOff:F6} (=> {(1.0 - offDrop) * A:F6}) def={floorDef:F6} (=> {(1.0 - defDrop) * A:F6}); >0 and never below");
            pass &= floorOk;
        }

        // ── Part 2 — the five sites, each exercised independently ──────────────────────
        Console.WriteLine("  Part 2 — five sites (tired vs fresh, through each generator/primitive):");

        // Site 1a — Make door, the primitive: Matchup.EffectiveRating(6-arg).
        {
            var shooter  = Mk(1, 60);
            var defender = Mk(2, 60);
            var zone = ShotLocation.Mid;

            var tS = new FatigueTracker(cfgFat);
            double EffS() => Matchup.EffectiveRating(zone, shooter, defender, cfgM,
                tS.EffectiveAthleticism(shooter, false), tS.EffectiveAthleticism(defender, true));
            var fresh = EffS();
            Tire(tS, shooter, 300);
            var tiredShooter = EffS();

            var tD = new FatigueTracker(cfgFat);
            double EffD() => Matchup.EffectiveRating(zone, shooter, defender, cfgM,
                tD.EffectiveAthleticism(shooter, false), tD.EffectiveAthleticism(defender, true));
            var fresh2 = EffD();
            Tire(tD, defender, 300);
            var tiredDefender = EffD();

            pass &= Moved("S1a make door: tired SHOOTER -> worse rating", fresh, tiredShooter, dir: -1);
            pass &= Moved("S1a make door: tired DEFENDER -> better rating", fresh2, tiredDefender, dir: +1);

            // Asymmetry: defender drop (0.20) bites harder than shooter drop (0.10) at equal level.
            var fall = fresh - tiredShooter;       // >=0
            var rise = tiredDefender - fresh2;     // >=0
            bool asym = inert ? (Math.Abs(fall) < 1e-9 && Math.Abs(rise) < 1e-9)
                              : (rise > fall + 1e-9);
            Console.WriteLine($"    {(asym ? "ok  " : "FAIL")} S1a asymmetry: defender swing {rise:F6} {(inert ? "==" : ">")} shooter swing {fall:F6}");
            pass &= asym;
        }

        // Site 1b — Make door, the WIRING: RollHGenerator passes effective values.
        {
            var g = new GameState(Fouls(), ArrowState.Off, new FatigueTracker(cfgFat));
            var shooter = Mk(1, 50);
            Seat(g, TeamSide.Home, new[] { shooter, Mk(2, 50), Mk(3, 50), Mk(4, 50), Mk(5, 50) });
            Seat(g, TeamSide.Away, new[] { Mk(6, 50), Mk(7, 50), Mk(8, 50), Mk(9, 50), Mk(10, 50) });
            var genH  = new RollHGenerator(cfgH, cfgM, g);
            var st    = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                            SelectedSlot: g.HomeLineup.SlotAt(1), ShotType: ShotLocation.Mid);
            var madeFresh = W(genH.Generate(st), ShotResult.Made);
            Tire(g.Fatigue, shooter, 300);
            var madeTired = W(genH.Generate(st), ShotResult.Made);
            pass &= Moved("S1b RollH make%: tired shooter -> lower Made", madeFresh, madeTired, dir: -1);
        }

        // Site 2 — RollE denial: defender's athletic edge denies; tiring him eases the denial.
        // The per-man denial lives in BendByAttention (GenerateWithPressure is offensive usage
        // intent ONLY — pre-denial). So we read the post-tilt Slot1 pie weight, the same
        // observable the Phase-46 denial check asserts on. Reading FinalShares here would see
        // only the flat pre-denial intent and never move.
        {
            var g = new GameState(Fouls(), ArrowState.Off, new FatigueTracker(cfgFat));
            // Offense slot1 ordinary; its matched defender (defense slot1) is the athletic denier.
            Seat(g, TeamSide.Home, new[] { Mk(1, 50), Mk(2, 50), Mk(3, 50), Mk(4, 50), Mk(5, 50) });
            var denier = Mk(6, 90);
            Seat(g, TeamSide.Away, new[] { denier, Mk(7, 50), Mk(8, 50), Mk(9, 50), Mk(10, 50) });
            var genE    = new RollEGenerator(cfgE, g);
            var attnGen = new AttentionGenerator(AttentionConfig.Load(configPath), g);
            var st      = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
            double Slot1Share()
            {
                var gen  = genE.GenerateWithPressure(st);
                var attn = attnGen.Generate(st, gen.FinalShares);
                var tilt = genE.BendByAttention(gen, attn.AttentionShares, g, cfgM, st);
                return W(tilt, SelectionOutcome.Slot1);
            }
            var shareFresh = Slot1Share();
            Tire(g.Fatigue, denier, 300);
            var shareTired = Slot1Share();
            pass &= Moved("S2 RollE denial: tired denier -> offense slot1 share recovers", shareFresh, shareTired, dir: +1);
        }

        // Site 3 — RollA entry disruption: athletic defense disrupts; tiring it lowers turnover.
        {
            var g = new GameState(Fouls(), ArrowState.Off, new FatigueTracker(cfgFat));
            Seat(g, TeamSide.Home, new[] { Mk(1, 50), Mk(2, 50), Mk(3, 50), Mk(4, 50), Mk(5, 50) });
            var def = new[] { Mk(6, 80), Mk(7, 80), Mk(8, 80), Mk(9, 80), Mk(10, 80) };
            Seat(g, TeamSide.Away, def);
            var genA = new RollAGenerator(cfgA, cfgM, g);
            var st   = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                            Frontcourt: false, PressMode: PressMode.Standard);
            var toFresh = W(genA.Generate(st, 0.0), EntryOutcome.Turnover);
            foreach (var d in def) Tire(g.Fatigue, d, 300);
            var toTired = W(genA.Generate(st, 0.0), EntryOutcome.Turnover);
            pass &= Moved("S3 RollA disruption: tired defense -> lower Turnover share", toFresh, toTired, dir: -1);
        }

        // Site 4 — RollJ transition: athletic offense pushes; tiring it lowers Push.
        {
            var g = new GameState(Fouls(), ArrowState.Off, new FatigueTracker(cfgFat));
            var coach = new CoachProfile(heliocentricBias: 5.0, shotSelectionBias: 5.0, paceBias: 5.0);
            g.SetCoach(TeamSide.Home, coach);
            g.SetCoach(TeamSide.Away, coach);
            var off = new[] { Mk(1, 80), Mk(2, 80), Mk(3, 80), Mk(4, 80), Mk(5, 80) };
            Seat(g, TeamSide.Home, off);
            Seat(g, TeamSide.Away, new[] { Mk(6, 50), Mk(7, 50), Mk(8, 50), Mk(9, 50), Mk(10, 50) });
            var genJ = new RollJGenerator(cfgJ, cfgM, g);
            var ctx  = new TransitionContext(TransitionSource.Rebound) { OffenseSide = TeamSide.Home };
            var pushFresh = W(genJ.Generate(ctx), TransitionOutcome.Push);
            foreach (var o in off) Tire(g.Fatigue, o, 300);
            var pushTired = W(genJ.Generate(ctx), TransitionOutcome.Push);
            pass &= Moved("S4 RollJ transition: tired offense -> lower Push", pushFresh, pushTired, dir: -1);
        }

        // Site 5 — RollK putback: athletic rebounder converts; tiring him lowers PutBack.
        {
            var g = new GameState(Fouls(), ArrowState.Off, new FatigueTracker(cfgFat));
            var rebounder = Mk(1, 80);
            Seat(g, TeamSide.Home, new[] { rebounder, Mk(2, 50), Mk(3, 50), Mk(4, 50), Mk(5, 50) });
            Seat(g, TeamSide.Away, new[] { Mk(6, 50), Mk(7, 50), Mk(8, 50), Mk(9, 50), Mk(10, 50) });
            var genK = new RollKGenerator(cfgK, cfgM, g);
            var st   = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound,
                            ReboundSlot: g.HomeLineup.SlotAt(1));
            var putFresh = W(genK.Generate(st, OffensiveReboundSource.LiveBall), OffensiveReboundOutcome.PutBack);
            Tire(g.Fatigue, rebounder, 300);
            var putTired = W(genK.Generate(st, OffensiveReboundSource.LiveBall), OffensiveReboundOutcome.PutBack);
            pass &= Moved("S5 RollK putback: tired rebounder -> lower PutBack", putFresh, putTired, dir: -1);
        }

        // ── Part 3 (in-process portion) — fresh-player anchor under real config ───────
        // With nonzero drops configured, a FRESH player (level 0) still reads EXACTLY authored
        // at the make door: the 6-arg overload fed fresh effective values equals the 4-arg raw
        // path, bit-for-bit. (The full all-sites zero-drop byte-compare is the operator's run —
        // Claude has no .NET SDK; see the "what to watch" note.)
        {
            var t = new FatigueTracker(cfgFat);
            var s = Mk(1, 63);
            var d = Mk(2, 41);
            var zone = ShotLocation.Three;
            var sixFresh = Matchup.EffectiveRating(zone, s, d, cfgM,
                t.EffectiveAthleticism(s, false), t.EffectiveAthleticism(d, true));
            var fourRaw  = Matchup.EffectiveRating(zone, s, d, cfgM);
            bool anchor = sixFresh == fourRaw;   // bit-exact: fresh discount is exactly 1.0
            Console.WriteLine($"    {(anchor ? "ok  " : "FAIL")} P3 fresh anchor: 6-arg(fresh effective) == 4-arg(raw) bit-exact ({sixFresh:R} vs {fourRaw:R})");
            pass &= anchor;
        }

        Console.WriteLine(pass
            ? "  FatigueAthleticismCheck: PASS"
            : "  FatigueAthleticismCheck: FAIL");
        return pass;
    }
}
