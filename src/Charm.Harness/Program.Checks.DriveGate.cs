using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 65 (Session 59) — Pass A: the perimeter-defense DRIVE GATE.
//
//  The transform (Matchup.ApplyDriveGate), applied inside Roll G to the DISPLACED pie
//  and BEFORE the usage diet shift. Shot DIET only — no make% wire anywhere:
//      comp   = (FsW·FirstStep + QW·Quickness) × clamp01((BH − HandleLo)/(HandleHi − HandleLo))
//      gap    = comp − matched.PerimeterDefense
//      supp   = GapFn(max(0, −gap), DriveGateSteepness, DriveGateExponent, ReferenceScale)
//      mult   = 1 − DriveGateCap · tanh(supp / DriveGateTanhRef)
//      orient = clamp01(1 − ((Height+Strength+PostMoves)/3 − Pivot) / Range)
//      remove orient·(1−mult) of Rim and ShortElig·orient·(1−mult) of Short; redistribute
//      to Long+Three proportional to their PRE-gate weights (50/50 if both are zero);
//      renormalize. Mid NEVER moves.
//
//  Golden fixture tools/drive_gate_golden.json is emitted by tools/drive_gate_oracle.py
//  (LOCKED shape; every magnitude a placeholder). 13 cases: 12 signed-off archetypes + 1
//  both-zero-outer boundary row. The fixture's "before" pies are FIXED INPUTS, so this
//  phase tests the GATE in isolation — the displacement regression stays Phase 56's job.
//  Constants are cross-checked against the loaded MatchupConfig before a single number is
//  trusted, so silent drift between fixture and config fails loudly.
//
//  If the C# and the oracle ever disagree, THE ORACLE WINS: a parity failure here is a PORT
//  BUG, never a tolerance to widen and never a fixture to regenerate casually.
//
//  Sub-checks:
//    (1) Golden parity — after-pie per zone AND internals (comp/orient/mult/removed), 1e-12.
//    (2) Structural invariants — the oracle's, ported.
//    (3) Wiring proofs — the things the oracle cannot test: placement, the flat-50 anchor,
//        the null-matched bypass, and the gate surviving ApplyDietShift downstream.
//    (4) Config guards — Load throws on each bound; both kill switches load cleanly.
// ============================================================================
internal static partial class Program
{
    private static bool Phase65DriveGateCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 65: perimeter-defense drive gate (golden parity + invariants + wiring + config guards) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgM = MatchupConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);

        // A uniform all-50 player, overriding only what the gate reads. Every other rating is
        // 50 so the isolated reads are unconfounded.
        static Player Mk(string id, int fs = 50, int q = 50, int bh = 50, int height = 50,
                         int strength = 50, int postMoves = 50, int perimD = 50)
            => new Player(id)
            {
                PlayerId = Math.Abs(id.GetHashCode()) % 100000,
                Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                BallHandling = bh, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = postMoves,
                OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = perimD,
                PostDefense = 50, RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                HelpDefense = 50, OffBallDefense = 50, Height = height, Wingspan = 50, Weight = 50,
                Strength = strength, Speed = 50, Quickness = q, FirstStep = fs, Vertical = 50,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
            };

        // The zone order every pie in the engine uses.
        var zoneNames = new[] { "Rim", "Short", "Mid", "Long", "Three" };

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/drive_gate_golden.json.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity (13 cases, |Δ| <= 1e-12 on after-pie + internals):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "drive_gate_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // ── fixture contract — constants validated loudly BEFORE trusting a number ──
            var kc = root.GetProperty("constants");
            bool ConstOk(string key, double live) => kc.GetProperty(key).GetDouble() == live;
            if (!(ConstOk("FS_W", cfgM.DriveBeatFirstStepWeight) &&
                  ConstOk("Q_W", cfgM.DriveBeatQuicknessWeight) &&
                  ConstOk("HANDLE_LO", cfgM.DriveHandleUnlockLo) &&
                  ConstOk("HANDLE_HI", cfgM.DriveHandleUnlockHi) &&
                  ConstOk("GATE_STEEP", cfgM.DriveGateSteepness) &&
                  ConstOk("GATE_EXP", cfgM.DriveGateExponent) &&
                  ConstOk("GATE_TANH_REF", cfgM.DriveGateTanhRef) &&
                  ConstOk("GATE_CAP", cfgM.DriveGateCap) &&
                  ConstOk("SHORT_ELIG", cfgM.DriveGateShortEligibility) &&
                  ConstOk("POST_PIVOT", cfgM.DriveOrientPostnessPivot) &&
                  ConstOk("POST_RANGE", cfgM.DriveOrientPostnessRange) &&
                  ConstOk("REF_SCALE", cfgM.ReferenceScale)))
                throw new InvalidOperationException(
                    "golden fixture rejected: drive-gate constants do not match the loaded MatchupConfig. " +
                    "Regenerate the fixture or fix the config.");

            var tol = root.GetProperty("tolerance").GetDouble();
            var cases = root.GetProperty("cases");
            if (cases.GetArrayLength() != 13)
                throw new InvalidOperationException($"golden fixture rejected: expected 13 cases, got {cases.GetArrayLength()}.");

            var worstPie = 0.0; var worstInternal = 0.0; var allOk = true; var bypassSeen = 0;
            foreach (var c in cases.EnumerateArray())
            {
                var s = c.GetProperty("shooter");
                var shooter = Mk("gsh",
                    fs: s.GetProperty("FirstStep").GetInt32(),
                    q: s.GetProperty("Quickness").GetInt32(),
                    bh: s.GetProperty("BallHandling").GetInt32(),
                    height: s.GetProperty("Height").GetInt32(),
                    strength: s.GetProperty("Strength").GetInt32(),
                    postMoves: s.GetProperty("PostMoves").GetInt32());

                var bypass = c.GetProperty("bypass").GetBoolean();
                if (bypass) bypassSeen++;

                // The bypass case is exercised through the BYPASS PATH — a null matched
                // defender — not by a flag. Its fixture internals are the oracle's bypass
                // sentinels (comp 0, orient 0, mult 1, removed 0), which the null branch
                // reproduces exactly.
                var matchedPerimD = c.GetProperty("matchedPerimeterDefense").GetDouble();
                Player? matched = bypass ? null : Mk("gdf", perimD: (int)matchedPerimD);

                var before = c.GetProperty("before");
                var pie = new double[5];
                for (var z = 0; z < 5; z++) pie[z] = before.GetProperty(zoneNames[z]).GetDouble();

                var t = Matchup.ApplyDriveGate(pie, shooter, matched, cfgM);

                var after = c.GetProperty("after");
                var caseWorstPie = 0.0;
                for (var z = 0; z < 5; z++)
                    caseWorstPie = Math.Max(caseWorstPie, Math.Abs(t.Final[z] - after.GetProperty(zoneNames[z]).GetDouble()));

                var caseWorstInternal = Math.Max(
                    Math.Max(Math.Abs(t.Composite       - c.GetProperty("driveComposite").GetDouble()),
                             Math.Abs(t.Orient          - c.GetProperty("orient").GetDouble())),
                    Math.Max(Math.Abs(t.SuppressionMult - c.GetProperty("suppressionMult").GetDouble()),
                             Math.Abs(t.Removed         - c.GetProperty("removed").GetDouble())));

                worstPie      = Math.Max(worstPie, caseWorstPie);
                worstInternal = Math.Max(worstInternal, caseWorstInternal);

                var caseOk = caseWorstPie <= tol && caseWorstInternal <= tol;
                allOk &= caseOk;
                if (!caseOk)
                    Console.WriteLine($"      FAIL {c.GetProperty("name").GetString()}: " +
                                      $"pie|Δ|={caseWorstPie:E3} internals|Δ|={caseWorstInternal:E3}");
            }

            Check("bypass case reached through the null-matched path", bypassSeen == 1,
                  $"bypass rows={bypassSeen}");
            Check($"golden parity (13 cases, tol {tol:E0})", allOk,
                  $"worst pie|Δ|={worstPie:E2}  worst internals|Δ|={worstInternal:E2}");
        }
        catch (Exception ex)
        {
            Check("golden parity", false, ex.Message);
        }

        // ----------------------------------------------------------------
        // (2) Structural invariants — the oracle's, ported. All on the pure transform.
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) Structural invariants (the locked oracle's, ported):");
        {
            // The oracle's probe pie and its guard builder.
            double[] P() => new[] { 0.28, 0.15, 0.14, 0.11, 0.32 };
            Player Drv(int bh, int fs, int q, int h = 44, int st = 44, int pm = 25)
                => Mk("dg", fs: fs, q: q, bh: bh, height: h, strength: st, postMoves: pm);
            Player Def(int perimD) => Mk("dgd", perimD: perimD);

            // ── neutral gap-0 identity, BIT (not "close") ──
            {
                var p = P();
                var t = Matchup.ApplyDriveGate(p, Drv(50, 50, 50), Def(50), cfgM);
                var bit = true;
                for (var z = 0; z < 5; z++) bit &= t.Final[z] == p[z];
                Check("neutral (comp == perimD) -> pie BIT-unchanged", bit && t.Removed == 0.0,
                      $"mult={t.SuppressionMult:F3} removed={t.Removed:R}");
            }

            // ── conservation + monotone in perimD + suppression-primary, across a grid ──
            {
                var cons = true; var monoPd = true; var monoRim = true; var asym = true;
                double? prevRemoved = null; double? prevRim = null;
                foreach (var pd in new[] { 25, 40, 50, 65, 85, 99 })
                {
                    var t = Matchup.ApplyDriveGate(P(), Drv(50, 50, 50), Def(pd), cfgM);
                    var sum = 0.0; for (var z = 0; z < 5; z++) sum += t.Final[z];
                    cons &= Math.Abs(sum - 1.0) < 1e-12;
                    // Raising PerimeterDefense never DECREASES removal ...
                    if (prevRemoved is not null) monoPd &= t.Removed >= prevRemoved - 1e-12;
                    // ... and never RAISES post-gate Rim.
                    if (prevRim is not null) monoRim &= t.Final[0] <= prevRim + 1e-12;
                    if (t.Gap >= 0.0) asym &= t.Removed < 1e-12;
                    prevRemoved = t.Removed; prevRim = t.Final[0];
                }
                Check("conservation (Σafter == 1) across a perimD grid", cons);
                Check("monotone in perimD: removal non-decreasing", monoPd);
                Check("monotone in perimD: post-gate Rim never rises", monoRim);
                Check("suppression-primary (gap >= 0 removes EXACTLY 0)", asym);
            }

            // ── monotone in the drive-tools composite, orientation held fixed ──
            {
                var monoRemoved = true; var monoRim = true;
                double? prevRemoved = null; double? prevRim = null;
                foreach (var tv in new[] { 30, 50, 70, 88, 99 })
                {
                    var t = Matchup.ApplyDriveGate(P(), Drv(tv, tv, tv), Def(85), cfgM);
                    // Raising the composite never INCREASES removal ...
                    if (prevRemoved is not null) monoRemoved &= t.Removed <= prevRemoved + 1e-12;
                    // ... and never LOWERS post-gate Rim.
                    if (prevRim is not null) monoRim &= t.Final[0] >= prevRim - 1e-12;
                    prevRemoved = t.Removed; prevRim = t.Final[0];
                }
                Check("monotone in drive tools: removal non-increasing", monoRemoved);
                Check("monotone in drive tools: post-gate Rim never falls", monoRim);
            }

            // ── first step BEATS, handle only UNLOCKS ──
            {
                var eh = Matchup.ApplyDriveGate(P(), Drv(88, 50, 50), Def(85), cfgM);
                var ef = Matchup.ApplyDriveGate(P(), Drv(50, 88, 72), Def(85), cfgM);
                Check("elite handle / avg burst is walled at least as hard as avg handle / elite first step",
                      eh.Removed >= ef.Removed,
                      $"eliteHandle={eh.Removed * 100:F1}pp eliteFirstStep={ef.Removed * 100:F1}pp");

                // Concretely: FsW 0.62 > QW 0.38, so the SAME +Δ buys more on FirstStep.
                var baseT = Matchup.DriveTools(Drv(60, 50, 50), cfgM);
                var plusFs = Matchup.DriveTools(Drv(60, 60, 50), cfgM);
                var plusQ  = Matchup.DriveTools(Drv(60, 50, 60), cfgM);
                Check("+10 FirstStep raises DriveTools more than +10 Quickness",
                      plusFs - baseT > plusQ - baseT,
                      $"ΔFS={plusFs - baseT:F2} ΔQ={plusQ - baseT:F2}");

                // Handle above HandleHi is worth EXACTLY zero more.
                var atHi = Matchup.DriveTools(Drv((int)cfgM.DriveHandleUnlockHi, 70, 70), cfgM);
                var wayOver = Matchup.DriveTools(Drv(99, 70, 70), cfgM);
                Check("BallHandling above HandleHi changes DriveTools by EXACTLY 0",
                      wayOver == atHi, $"atHi={atHi:R} at99={wayOver:R}");

                // Elite burst with no handle: composite exactly 0, and therefore walled.
                var nh = Matchup.ApplyDriveGate(P(), Drv((int)cfgM.DriveHandleUnlockLo, 85, 72), Def(50), cfgM);
                Check("elite burst with BallHandling <= HandleLo -> DriveTools EXACTLY 0, and walled",
                      nh.Composite == 0.0 && nh.Removed > 0.0,
                      $"comp={nh.Composite:R} removed={nh.Removed * 100:F1}pp");
            }

            // ── orientation: post scorer immune, guard eligible ──
            {
                var post  = Matchup.ApplyDriveGate(P(), Drv(32, 45, 45, h: 82, st: 80, pm: 85), Def(85), cfgM);
                var guard = Matchup.ApplyDriveGate(P(), Drv(50, 50, 50), Def(85), cfgM);
                Check("post-oriented shooter immune (orient EXACTLY 0 -> zero removal)",
                      post.Orient == 0.0 && post.Removed == 0.0);
                Check("perimeter guard eligible (orient > 0)", guard.Orient > 0.0,
                      $"orient={guard.Orient:F3}");
            }

            // ── the shape of the move: Mid bit-untouched, Rim/Short down, Long/Three up ──
            {
                var p = P();
                var t = Matchup.ApplyDriveGate(p, Drv(50, 50, 50), Def(85), cfgM);
                Check("Mid BIT-untouched; Rim/Short fall; Long/Three rise",
                      t.Final[2] == p[2] && t.Final[0] < p[0] && t.Final[1] < p[1]
                      && t.Final[3] > p[3] && t.Final[4] > p[4],
                      $"rim {p[0]:P1}->{t.Final[0]:P1}  mid {p[2]:R}->{t.Final[2]:R}  three {p[4]:P1}->{t.Final[4]:P1}");
            }

            // ── raw mass conserved BEFORE renormalize (renorm is hygiene, not the mechanism) ──
            {
                var t = Matchup.ApplyDriveGate(P(), Drv(50, 50, 50), Def(85), cfgM);
                Check("raw mass conserved BEFORE renormalize (|Σraw − 1| < 1e-12)",
                      Math.Abs(t.RawSum - 1.0) < 1e-12, $"Σraw={t.RawSum:R}");
            }

            // ── both-zero outer: an exact 50/50 split, a real branch (golden case 13) ──
            {
                var bz = new[] { 0.55, 0.30, 0.15, 0.0, 0.0 };
                var t = Matchup.ApplyDriveGate(bz, Drv(50, 50, 50), Def(85), cfgM);
                var sum = 0.0; for (var z = 0; z < 5; z++) sum += t.Final[z];
                Check("both-zero outer: removed splits 50/50 to Long/Three; Mid bit-identical; conserved",
                      Math.Abs(t.Final[3] - t.Final[4]) < 1e-12 && t.Final[2] == bz[2]
                      && Math.Abs(sum - 1.0) < 1e-12 && t.Removed > 0.0,
                      $"Long={t.Final[3]:F4} Three={t.Final[4]:F4}");
            }

            // ── kill switch (Cap = 0) -> BIT identity ──
            {
                var cfgKill = LoadWithMatchupOverride(configPath, ("DriveGateCap", 0.0));
                var p = P();
                var t = Matchup.ApplyDriveGate(p, Drv(50, 50, 50), Def(85), cfgKill);
                var bit = true;
                for (var z = 0; z < 5; z++) bit &= t.Final[z] == p[z];
                Check("kill switch (DriveGateCap = 0) -> pie BIT-unchanged", bit && t.Removed == 0.0);
            }

            // ── the identity branch: a LIVE gate with zero suppression is BIT-identical to
            //    the input, not merely close (the whole point of the branch) ──
            {
                var p = P();
                var t = Matchup.ApplyDriveGate(p, Drv(50, 50, 50), Def(50), cfgM);
                var bit = true;
                for (var z = 0; z < 5; z++) bit &= t.Final[z] == p[z];
                Check("zero-suppression LIVE gate is BIT-identical to the input (identity branch)",
                      bit && t.RawSum == 1.0 && !t.Bypass);
            }

            // ── bypass (null matched man) -> BIT identity, and flagged as bypass ──
            {
                var p = P();
                var t = Matchup.ApplyDriveGate(p, Drv(50, 50, 50), null, cfgM);
                var bit = true;
                for (var z = 0; z < 5; z++) bit &= t.Final[z] == p[z];
                Check("bypass (null matched man) -> pie BIT-unchanged, trace flagged",
                      bit && t.Bypass && t.Removed == 0.0);
            }
        }

        // ----------------------------------------------------------------
        // (3) Wiring proofs — what the oracle cannot test. These run the REAL Roll G
        //     pipeline (coaching pull -> S57 tilt -> displacement -> gate -> diet shift).
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Wiring proofs (the real Roll G pipeline):");
        {
            // Build a Roll G game: shooter in Home slot 1; the five Away slots as given
            // (a null entry leaves that defending slot EMPTY).
            (RollGGenerator gen, GameState game) BuildGame(Player shooter, Player?[] defenders, MatchupConfig m)
            {
                var g = new GameState(new FoulTracker(7, 10));
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(1), shooter);
                for (var i = 0; i < 5; i++)
                    if (defenders[i] is not null)
                        g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), defenders[i]!);
                return (new RollGGenerator(cfgG, m, g), g);
            }

            double[] RollGPie(Player shooter, Player?[] defenders, MatchupConfig m, double? usage = null)
            {
                var (gen, g) = BuildGame(shooter, defenders, m);
                var st = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound, SelectedSlot: g.HomeLineup.SlotAt(1),
                    UsagePressure: usage);
                var pie = gen.Generate(st);
                return new[]
                {
                    pie.Slices.First(s => s.Outcome == ShotLocation.Rim).Weight,
                    pie.Slices.First(s => s.Outcome == ShotLocation.Short).Weight,
                    pie.Slices.First(s => s.Outcome == ShotLocation.Mid).Weight,
                    pie.Slices.First(s => s.Outcome == ShotLocation.Long).Weight,
                    pie.Slices.First(s => s.Outcome == ShotLocation.Three).Weight,
                };
            }

            static bool BitEqual(double[] a, double[] b)
            {
                for (var z = 0; z < 5; z++) if (a[z] != b[z]) return false;
                return true;
            }

            var cfgKill = LoadWithMatchupOverride(configPath, ("DriveGateCap", 0.0));

            // ── (3a) The flat-50 anchor: a LIVE gate is bit-identical to the kill switch,
            //    by arithmetic (comp 50 == perimD 50 -> gap 0), never by a config switch. ──
            {
                var flat = Mk("f50");
                var defs = new Player?[] { Mk("fd1"), Mk("fd2"), Mk("fd3"), Mk("fd4"), Mk("fd5") };
                var live = RollGPie(flat, defs, cfgM);
                var kill = RollGPie(flat, defs, cfgKill);
                Check("flat-50 world: live gate BIT-identical to DriveGateCap = 0 end-to-end",
                      BitEqual(live, kill),
                      $"rim live={live[0]:R} kill={kill[0]:R}");
            }

            // ── (3b) The null-matched bypass. Four defenders on the floor (populated != 0,
            //    so the zero-defender short-circuit does NOT fire), but the shooter's own
            //    slot-1 man is absent. The shooter is deliberately tools-POOR, so a bug that
            //    gated against a phantom default-50 defender WOULD move his pie — that is the
            //    tripwire this case exists to trip. The positive control proves the same
            //    shooter DOES get walled once a real man is in that slot. ──
            {
                var weakTools = Mk("wt", fs: 20, q: 20, bh: 60);   // comp = 20, fully unlocked
                var elite     = Mk("ed", perimD: 85);
                var noMan     = new Player?[] { null, elite, elite, elite, elite };
                var withMan   = new Player?[] { elite, elite, elite, elite, elite };

                var bypassLive = RollGPie(weakTools, noMan, cfgM);
                var bypassKill = RollGPie(weakTools, noMan, cfgKill);
                Check("null matched man: gate bypasses (BIT-identical to kill switch) even with 4 defenders up",
                      BitEqual(bypassLive, bypassKill),
                      $"rim={bypassLive[0]:P2}");

                var manLive = RollGPie(weakTools, withMan, cfgM);
                var manKill = RollGPie(weakTools, withMan, cfgKill);
                Check("positive control: the SAME shooter IS walled once slot 1 is populated",
                      !BitEqual(manLive, manKill) && manLive[0] < manKill[0],
                      $"rim live={manLive[0]:P2} vs kill={manKill[0]:P2}");
            }

            // ── (3c) The walled guard, end-to-end at zero usage (ApplyDietShift runs and
            //    takes its exact zero-pressure branch): rim down, three up, Mid BIT-unmoved. ──
            {
                var avgGuard = Mk("ag");                       // comp 50, orient 0.846
                var elite    = Mk("ep", perimD: 85);
                var defs     = new Player?[] { elite, elite, elite, elite, elite };
                var live = RollGPie(avgGuard, defs, cfgM);
                var kill = RollGPie(avgGuard, defs, cfgKill);
                Check("walled guard end-to-end (usage 0): rim down, three up, Mid BIT-unmoved",
                      live[0] < kill[0] && live[4] > kill[4] && live[2] == kill[2],
                      $"rim {kill[0]:P2}->{live[0]:P2}  mid {kill[2]:R}  three {kill[4]:P2}->{live[4]:P2}");
            }

            // ── (3d) The same walled guard WITH usage pressure, so ApplyDietShift genuinely
            //    runs downstream: the gate's rim->three move must SURVIVE it, not be undone.
            //    Mid is deliberately NOT asserted here — the diet shift legitimately moves it. ──
            {
                var avgGuard = Mk("ag2");
                var elite    = Mk("ep2", perimD: 85);
                var defs     = new Player?[] { elite, elite, elite, elite, elite };
                var live = RollGPie(avgGuard, defs, cfgM, usage: 0.30);
                var kill = RollGPie(avgGuard, defs, cfgKill, usage: 0.30);
                Check("gate survives ApplyDietShift downstream (usage 0.30): rim still down, three still up",
                      live[0] < kill[0] && live[4] > kill[4],
                      $"rim {kill[0]:P2}->{live[0]:P2}  three {kill[4]:P2}->{live[4]:P2}");
            }

            // ── (3e) The post scorer is untouched end-to-end — the perimeter wall does not
            //    reach the post route, even against the same lockdown man. ──
            {
                var postScorer = Mk("ps", fs: 45, q: 45, bh: 32, height: 82, strength: 80, postMoves: 85);
                var elite      = Mk("ep3", perimD: 85);
                var defs       = new Player?[] { elite, elite, elite, elite, elite };
                var live = RollGPie(postScorer, defs, cfgM);
                var kill = RollGPie(postScorer, defs, cfgKill);
                Check("post scorer BIT-untouched end-to-end vs the same lockdown man (orient 0)",
                      BitEqual(live, kill), $"rim={live[0]:P2}");
            }

            // ── (3f) Placement proof by construction: the gate composes on the DISPLACED pie.
            //    A soft-paint defending SHAPE invites the shooter inward (displacement), and a
            //    lockdown matched man then takes that invitation away — the S54 finding this
            //    pass exists to answer. If the gate ran on the pre-displacement baseline, the
            //    soft-paint invitation would survive untouched. ──
            {
                var avgGuard = Mk("ag3");
                // Lockdown on the ball, but a SOFT paint behind him (RimProtection 30).
                Player Soft(string id, int perimD) => new Player(id)
                {
                    PlayerId = Math.Abs(id.GetHashCode()) % 100000,
                    Close = 50, Mid = 50, Outside = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                    BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                    OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = perimD,
                    PostDefense = 50, RimProtection = 30, DefensiveRebounding = 50, Steals = 50,
                    HelpDefense = 50, OffBallDefense = 50, Height = 50, Wingspan = 50, Weight = 50,
                    Strength = 50, Speed = 50, Quickness = 50, FirstStep = 50, Vertical = 50,
                    Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50, HierarchyRank = 5,
                };
                var softLockdown = new Player?[]
                    { Soft("s1", 85), Soft("s2", 85), Soft("s3", 85), Soft("s4", 85), Soft("s5", 85) };
                var live = RollGPie(avgGuard, softLockdown, cfgM);
                var kill = RollGPie(avgGuard, softLockdown, cfgKill);
                Check("soft paint invites him in (kill rim > tendency 20%), the matched wall takes it back",
                      kill[0] > 0.20 && live[0] < kill[0],
                      $"tendency=20.00%  displaced={kill[0]:P2}  after gate={live[0]:P2}");
            }
        }

        // ----------------------------------------------------------------
        // (4) Config guards — Load throws on each bound; both kill switches load cleanly.
        // ----------------------------------------------------------------
        Console.WriteLine("  (4) Config guards:");
        {
            static string MutatedConfig(string configPath, string key, double value)
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["Matchup"]![key] = value;
                var tmp = Path.Combine(Path.GetTempPath(), $"dg_cfg_{key}_{Guid.NewGuid():N}.json");
                File.WriteAllText(tmp, node.ToJsonString());
                return tmp;
            }
            static bool Throws(string path)
            {
                try { MatchupConfig.Load(path); return false; }
                catch (InvalidOperationException) { return true; }
                finally { try { File.Delete(path); } catch { /* best-effort */ } }
            }
            static bool LoadsCleanly(string path)
            {
                try { MatchupConfig.Load(path); return true; }
                catch { return false; }
                finally { try { File.Delete(path); } catch { /* best-effort */ } }
            }

            Check("negative DriveBeatFirstStepWeight throws", Throws(MutatedConfig(configPath, "DriveBeatFirstStepWeight", -0.1)));
            Check("negative DriveBeatQuicknessWeight throws", Throws(MutatedConfig(configPath, "DriveBeatQuicknessWeight", -0.1)));
            Check("beat weights not summing to 1.0 throws",   Throws(MutatedConfig(configPath, "DriveBeatFirstStepWeight", 0.70)));
            Check("DriveHandleUnlockHi <= Lo throws",         Throws(MutatedConfig(configPath, "DriveHandleUnlockHi", 28.0)));
            Check("negative DriveGateSteepness throws",       Throws(MutatedConfig(configPath, "DriveGateSteepness", -0.1)));
            Check("DriveGateExponent = 0 throws",             Throws(MutatedConfig(configPath, "DriveGateExponent", 0.0)));
            Check("DriveGateTanhRef = 0 throws",              Throws(MutatedConfig(configPath, "DriveGateTanhRef", 0.0)));
            Check("DriveGateCap > 1 throws",                  Throws(MutatedConfig(configPath, "DriveGateCap", 1.5)));
            Check("DriveGateCap < 0 throws",                  Throws(MutatedConfig(configPath, "DriveGateCap", -0.1)));
            Check("DriveGateShortEligibility > 1 throws",     Throws(MutatedConfig(configPath, "DriveGateShortEligibility", 1.5)));
            Check("DriveGateShortEligibility < 0 throws",     Throws(MutatedConfig(configPath, "DriveGateShortEligibility", -0.1)));
            Check("DriveOrientPostnessRange = 0 throws",      Throws(MutatedConfig(configPath, "DriveOrientPostnessRange", 0.0)));

            // Both kill states are INTENTIONALLY legal.
            Check("kill switch (DriveGateCap = 0) loads cleanly",
                  LoadsCleanly(MutatedConfig(configPath, "DriveGateCap", 0.0)));
            Check("redundant kill state (DriveGateSteepness = 0) loads cleanly",
                  LoadsCleanly(MutatedConfig(configPath, "DriveGateSteepness", 0.0)));
        }

        Console.WriteLine($"  Phase 65 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
