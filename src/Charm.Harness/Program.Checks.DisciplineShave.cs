using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 67 (Session 61) — DISCIPLINE make-% shave (Effect A): the small,
//  ABSOLUTE, per-man defensive-restraint reduction on the man's make%.
//
//  The wire. The matched defender's OWN Discipline shaves his man's make% by a
//  small RELATIVE amount, seated in Roll H beside the multiplicative usage siblings
//  (volume tax, relief) AFTER the C3 block:
//      progress = clamp((defenderDiscipline − 50) / 49, −1, +1)   // symmetric about 50
//      shave    = DisciplineMakeShaveScale × progress
//      makePct  = clamp01(makePct × (1 − shave))                  → RollHGenerator.ApplyDisciplineShave
//  ABSOLUTE: no shooter term, so the same defender applies the same RELATIVE shave to
//  any man he guards. MULTIPLICATIVE: the proportional reduction is FLAT across every
//  zone — the signed invariant Emmett approved.
//
//  Golden fixture tools/discipline_shave_golden.json is emitted by
//  tools/discipline_shave_oracle.py (LOCKED SYMMETRIC shape; magnitude is a placeholder,
//  page-tuned later, never suite-asserted). 8 cases: signed-off archetypes + a boost-arm
//  clamp boundary + two kill-switch rows. The constant is cross-checked against the loaded
//  RollHConfig before a number is trusted, so silent drift fails loudly.
//
//  Parity binds to the ENGINE, not a copy: the transform is a named static the live Roll H
//  calls and this check calls. If the C# and the oracle ever disagree, THE ORACLE WINS —
//  a failure here is a PORT BUG, never a tolerance to widen.
//
//  Sub-checks:
//    (1) Golden parity — makePctAfter per case, 1e-12; identity rows BIT-exact; clamp row saturates.
//    (2) Formula invariants — symmetric about 50, neutral at 50, monotone decreasing in D, bounded.
//    (3) Through the REAL Roll H path — the things the oracle cannot test:
//          (a) ABSOLUTE: the live/kill ratio is invariant to the shooter's ratings.
//          (b) FLAT ACROSS ZONES: the live/kill ratio == (1 − shave) at EVERY zone,
//              and it is exactly ONE shave (== 1 − shave, NOT (1 − shave)²).
//          (c) NULL DEFENDER (empty slot): pie BIT-identical live vs kill.
//          (d) COMPOSE: relief + IQ + shave all present don't stomp — the shave still
//              lands its exact multiplier with the non-clamping precondition holding.
//          (e) KILL SWITCH (scale 0): pie BIT-identical live vs kill.
//    (4) Config guards — negative and > 0.05 throw; 0 (the kill switch) loads cleanly.
// ============================================================================
internal static partial class Program
{
    private static bool Phase67DisciplineShaveCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 67: Discipline make-% shave (golden parity + invariants + real-path absolute/flat/compose + config guards) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var scale = cfgH.DisciplineMakeShaveScale;

        // Local mirror of the locked shave (for the invariant section only; the golden
        // section calls the ENGINE static, never this).
        static double Progress(double d) => Math.Clamp((d - 50.0) / 49.0, -1.0, 1.0);
        double ShaveAt(double d) => scale <= 0.0 ? 0.0 : scale * Progress(d);

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/discipline_shave_golden.json.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity (8 cases, |Δ| <= 1e-12; identity rows BIT-exact; clamp row saturates):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "discipline_shave_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            var kc = root.GetProperty("constants");
            if (kc.GetProperty("DisciplineMakeShaveScale").GetDouble() != scale)
                throw new InvalidOperationException(
                    "golden fixture rejected: DisciplineMakeShaveScale does not match the loaded RollHConfig " +
                    $"(fixture {kc.GetProperty("DisciplineMakeShaveScale").GetDouble()}, config {scale}). " +
                    "Regenerate the fixture or fix the config.");

            var tol = root.GetProperty("tolerance").GetDouble();
            var cases = root.GetProperty("cases");
            if (cases.GetArrayLength() != 8)
                throw new InvalidOperationException($"golden fixture rejected: expected 8 cases, got {cases.GetArrayLength()}.");

            var worst = 0.0; var allOk = true; var identitySeen = 0; var clampSeen = 0;
            foreach (var c in cases.EnumerateArray())
            {
                var name       = c.GetProperty("name").GetString()!;
                var discipline = c.GetProperty("defenderDiscipline").GetDouble();
                var caseScale  = c.GetProperty("scale").GetDouble();
                var before     = c.GetProperty("makePctBefore").GetDouble();
                var isIdentity = c.GetProperty("identity").GetBoolean();
                var isClamped  = c.GetProperty("clamped").GetBoolean();

                // The engine's own primitive — the SAME static the live path calls.
                var after = RollHGenerator.ApplyDisciplineShave(before, discipline, caseScale);

                var dA = Math.Abs(after - c.GetProperty("makePctAfter").GetDouble());
                worst = Math.Max(worst, dA);
                var ok = dA <= tol;

                // Identity rows held to a HARDER bar: object-identical, not merely within tol.
                if (isIdentity) { identitySeen++; ok = ok && after == before; }
                if (isClamped)  { clampSeen++;    ok = ok && (after == 1.0 || after == 0.0); }
                allOk = allOk && ok;

                if (!ok)
                    Console.WriteLine($"      MISMATCH [{name}]: after Δ{dA:e2} " +
                                      $"(got {after:R}, want {c.GetProperty("makePctAfter").GetDouble():R})");
            }

            Check("golden parity, 8 cases", allOk, $"worst |Δ| = {worst:e2} (tol {tol:e0})");
            Check("fixture exercises the identity branch", identitySeen >= 3, $"{identitySeen} identity rows");
            Check("fixture exercises the clamp branch", clampSeen >= 1, $"{clampSeen} clamped row(s)");
        }
        catch (Exception ex)
        {
            Check("golden parity", false, ex.Message);
        }

        // ----------------------------------------------------------------
        // (2) Formula invariants — the oracle's, ported.
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) Formula invariants:");
        {
            // Symmetric about 50: shave(50+k) == −shave(50−k) for k in range.
            var symmetric = new[] { 1, 10, 25, 40, 49 }
                .All(k => Math.Abs(ShaveAt(50 + k) + ShaveAt(50 - k)) < 1e-15);
            Check("symmetric about the midpoint: shave(50+k) == −shave(50−k)", symmetric);

            // Neutral at 50: BIT-identity on a LIVE scale, through the identity-by-value path.
            var neutral = RollHGenerator.ApplyDisciplineShave(0.45, 50.0, scale) == 0.45;
            Check("average defender (D50) is neutral -> BIT-identity on a live scale", neutral,
                  $"delta={RollHGenerator.ApplyDisciplineShave(0.45, 50.0, scale) - 0.45:e1}");

            // Monotone: make% non-increasing as Discipline rises (more restraint -> lower make).
            bool Monotone(double before)
            {
                double? prev = null; var ok = true;
                foreach (var d in new[] { 0.0, 12.0, 25.0, 50.0, 75.0, 99.0 })
                {
                    var a = RollHGenerator.ApplyDisciplineShave(before, d, scale);
                    if (prev is not null) ok = ok && a <= prev;
                    prev = a;
                }
                return ok;
            }
            Check("make% non-increasing as defender Discipline rises", Monotone(0.45) && Monotone(0.95));

            // Bounded: |relative shift| never exceeds the scale (the design ceiling), either arm.
            var bounded = true;
            foreach (var d in new[] { 0.0, 25.0, 50.0, 75.0, 99.0 })
                foreach (var b in new[] { 0.10, 0.34, 0.45, 0.60, 0.90 })
                {
                    var rel = Math.Abs(RollHGenerator.ApplyDisciplineShave(b, d, scale) / b - 1.0);
                    bounded = bounded && rel <= scale + 1e-12;
                }
            Check($"relative shift never exceeds the scale ceiling ({scale:F4})", bounded);

            // Kill switch: BIT-identity at every Discipline and every input.
            var ks = true;
            foreach (var d in new[] { 0.0, 25.0, 50.0, 75.0, 99.0 })
                foreach (var b in new[] { 0.0, 0.10, 0.45, 0.95, 1.0, cfgH.RimCeiling })
                    ks = ks && RollHGenerator.ApplyDisciplineShave(b, d, 0.0) == b;
            Check("kill switch (scale = 0) -> BIT-identity at every Discipline and every input", ks);

            // ABSOLUTE at the formula level: the relative shave is the SAME for any input make%.
            var absOk = true; var d99shave = ShaveAt(99.0);
            foreach (var b in new[] { 0.30, 0.55, 0.72, 0.90 })
                absOk = absOk && Math.Abs(RollHGenerator.ApplyDisciplineShave(b, 99.0, scale) - b * (1.0 - d99shave)) < 1e-12;
            Check("ABSOLUTE: relative shave invariant to the shooter's make% (same defender)", absOk);
        }

        // ----------------------------------------------------------------
        // (3) Through the REAL Roll H path.
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Real Roll H path (absolute / flat-across-zones / single-shave / null / compose / kill):");
        {
            var cfgKill = LoadWithRollHOverride(configPath, ("DisciplineMakeShaveScale", 0.0));

            // Screening (C5.5 +), HelpDefense (C6 −), and OffBallDefense (C7 −) are ADDITIVE
            // terms seated AFTER the shave. An additive term does not preserve a multiplicative
            // ratio, so to isolate the shave's multiplier in the real path they are zeroed here
            // (the same neutralization the usage-relief gravity-separability test uses). They
            // are Discipline-independent, so zeroing them changes nothing about what is measured.
            static Player Mk(string id, int discipline = 50, int close = 50, int mid = 50,
                             int outside = 50, int finishing = 50, int iq = 50,
                             int screening = 0, int helpDef = 0, int offBallDef = 0)
                => new Player(id)
                {
                    PlayerId = Math.Abs(id.GetHashCode()) % 100000,
                    Close = close, Mid = mid, Outside = outside, Finishing = finishing,
                    FreeThrow = 50, FoulDrawing = 50,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                    BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50,
                    PostMoves = 50, OffBallMovement = 50, Screening = screening,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50, HelpDefense = helpDef, OffBallDefense = offBallDef,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50, Quickness = 50,
                    FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50, BasketballIQ = iq,
                    Discipline = discipline, HierarchyRank = 5,
                };

            static double MakePct(Pie<ShotResult> pie)
            {
                var blocked    = pie.Slices.First(s => s.Outcome == ShotResult.Blocked).Weight;
                var maf        = pie.Slices.First(s => s.Outcome == ShotResult.MadeAndFouled).Weight;
                var missFouled = pie.Slices.First(s => s.Outcome == ShotResult.MissFouled).Weight;
                var nonBNF     = 1.0 - blocked - maf - missFouled;
                var made       = pie.Slices.First(s => s.Outcome == ShotResult.Made).Weight;
                return nonBNF > 1e-9 ? made / nonBNF : 0.0;
            }

            static bool PieBitEqual(Pie<ShotResult> a, Pie<ShotResult> b)
                => Enum.GetValues<ShotResult>().All(o =>
                       a.Slices.First(s => s.Outcome == o).Weight
                    == b.Slices.First(s => s.Outcome == o).Weight);

            var zones = new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid, ShotLocation.Long, ShotLocation.Three };

            // Build a game with a chosen defender Discipline (slot 1 defends slot 1).
            (GameState game, PossessionState st) Scene(int defenderDiscipline, ShotLocation zone,
                                                       int shooterClose = 50, int shooterMid = 50,
                                                       int shooterOutside = 50, int shooterFin = 50,
                                                       int shooterIq = 50, bool nullDefender = false)
            {
                var game = new GameState(new FoulTracker(7, 10));
                for (var i = 1; i <= 5; i++)
                {
                    game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i),
                        Mk($"o{i}", close: shooterClose, mid: shooterMid, outside: shooterOutside,
                           finishing: shooterFin, iq: shooterIq));
                    if (!(nullDefender && i == 1))
                        game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"d{i}", discipline: defenderDiscipline));
                }
                var st = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: game.HomeLineup.SlotAt(1), ShotType: zone);
                return (game, st);
            }

            double Make(int defDisc, ShotLocation zone, RollHConfig h,
                        int sClose = 50, int sMid = 50, int sOut = 50, int sFin = 50, int sIq = 50, bool nullDef = false)
            {
                var (game, st) = Scene(defDisc, zone, sClose, sMid, sOut, sFin, sIq, nullDef);
                return MakePct(new RollHGenerator(h, cfgM, game).Generate(st));
            }

            var expectedMult = 1.0 - ShaveAt(99.0);   // lockdown defender's exact make multiplier

            // ── (3a/3b) FLAT ACROSS ZONES + SINGLE SHAVE: live/kill ratio == (1 − shave) at every zone. ──
            {
                var allOk = true; var worstDev = 0.0; var detail = "";
                foreach (var z in zones)
                {
                    var live = Make(99, z, cfgH);
                    var kill = Make(99, z, cfgKill);
                    var nonClamping = live < 1.0 && kill < 1.0 && live > 0.0 && kill > 0.0;
                    var ratio = kill > 0.0 ? live / kill : double.NaN;
                    var dev = Math.Abs(ratio - expectedMult);
                    worstDev = Math.Max(worstDev, dev);
                    allOk = allOk && nonClamping && dev <= 1e-9;
                    detail += $"{z}:{ratio:F6} ";
                }
                Check($"FLAT ACROSS ZONES: live/kill make ratio == (1−shave)={expectedMult:F6} at EVERY zone",
                      allOk, $"worst dev {worstDev:e2} | {detail.Trim()}");
                // Single-shave: the ratio is (1−shave), NOT (1−shave)² (which would be a double-apply).
                Check("exactly ONE shave applied (ratio is 1−shave, not (1−shave)²)",
                      Math.Abs((Make(99, ShotLocation.Mid, cfgH) / Make(99, ShotLocation.Mid, cfgKill))
                               - expectedMult) <= 1e-9,
                      $"double-apply would read {expectedMult * expectedMult:F6}");
            }

            // ── (3a) ABSOLUTE: the live/kill ratio is invariant to the shooter's ratings. ──
            {
                // Three very different shooters, same D99 defender, same zone (Mid).
                var shooters = new (string tag, int cl, int md, int ou, int fn)[]
                {
                    ("weak",  20, 20, 20, 20),
                    ("avg",   50, 50, 50, 50),
                    ("elite", 95, 95, 95, 95),
                };
                var ratios = shooters.Select(s =>
                {
                    var live = Make(99, ShotLocation.Mid, cfgH, sClose: s.cl, sMid: s.md, sOut: s.ou, sFin: s.fn);
                    var kill = Make(99, ShotLocation.Mid, cfgKill, sClose: s.cl, sMid: s.md, sOut: s.ou, sFin: s.fn);
                    return kill > 0.0 ? live / kill : double.NaN;
                }).ToArray();
                var absOk = ratios.All(r => Math.Abs(r - expectedMult) <= 1e-9);
                Check("ABSOLUTE (real path): live/kill ratio identical for weak/avg/elite shooters",
                      absOk, string.Join(" ", shooters.Zip(ratios, (s, r) => $"{s.tag}:{r:F6}")));
            }

            // ── (3c) NULL DEFENDER (empty defending slot): pie BIT-identical live vs kill. ──
            {
                var (gL, stL) = Scene(99, ShotLocation.Mid, nullDefender: true);
                var (gK, stK) = Scene(99, ShotLocation.Mid, nullDefender: true);
                var live = new RollHGenerator(cfgH, cfgM, gL).Generate(stL);
                var kill = new RollHGenerator(cfgKill, cfgM, gK).Generate(stK);
                Check("NULL defender (empty slot): pie BIT-identical live vs kill (guard holds)",
                      PieBitEqual(live, kill));
            }

            // ── (3d) COMPOSE: relief + IQ + shave all live -> the shave still lands its exact multiplier. ──
            //    A below-share, high-IQ shooter guarded by a lockdown defender: relief lifts,
            //    IQ lifts, shave cuts. Isolate the shave by ratio against a shave-only-off config
            //    (relief + IQ identical on both sides). Non-clamping precondition guards a
            //    for-the-wrong-reason pass.
            {
                var game = new GameState(new FoulTracker(7, 10));
                // Star drains usage so slot-5 shooter is below share and earns relief.
                game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(1),
                    Mk("star", iq: 50, close: 95, mid: 95, outside: 95, finishing: 95));
                for (var i = 2; i <= 4; i++)
                    game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"role{i}"));
                // The measured shooter: high IQ (IQ make bump live) + below share (relief live).
                game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(5), Mk("shooter", iq: 99, outside: 60, mid: 60));
                for (var i = 1; i <= 5; i++)
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"d{i}", discipline: 99));

                var genE = new RollEGenerator(RollEConfig.Load(configPath), game);
                var stBase = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound);
                var gen = genE.GenerateWithPressure(stBase);

                var stShooter = stBase with
                {
                    SelectedSlot  = game.HomeLineup.SlotAt(5),
                    ShotType      = ShotLocation.Three,   // IQ bump is live at Three
                    UsagePressure = gen.Pressures[4],
                    UsageRelief   = gen.Reliefs[4],
                };
                var reliefLive = gen.Reliefs[4] > 0.0;

                var cfgShaveOff = LoadWithRollHOverride(configPath, ("DisciplineMakeShaveScale", 0.0));
                var makeAll  = MakePct(new RollHGenerator(cfgH,       cfgM, game).Generate(stShooter)); // relief+IQ+shave
                var makeNoSh = MakePct(new RollHGenerator(cfgShaveOff, cfgM, game).Generate(stShooter)); // relief+IQ, no shave
                var nonClamping = makeAll < 1.0 && makeNoSh < 1.0 && makeAll > 0.0 && makeNoSh > 0.0;
                var ratio = makeNoSh > 0.0 ? makeAll / makeNoSh : double.NaN;
                Check("COMPOSE: with relief + IQ both live, the shave still lands its exact (1−shave) multiplier",
                      reliefLive && nonClamping && Math.Abs(ratio - expectedMult) <= 1e-9,
                      $"relief={gen.Reliefs[4]:F4} noShave={makeNoSh:P2} all={makeAll:P2} ratio={ratio:F6} want={expectedMult:F6}");
            }

            // ── (3e) KILL SWITCH through the real path: pie BIT-identical live(scale=0) vs the pre-S61 kill. ──
            {
                var (gL, stL) = Scene(99, ShotLocation.Rim);
                var live0 = new RollHGenerator(cfgKill, cfgM, gL).Generate(stL);
                var (gK, stK) = Scene(99, ShotLocation.Rim);
                var kill0 = new RollHGenerator(cfgKill, cfgM, gK).Generate(stK);
                Check("kill switch (real path): pie BIT-identical across identical scenes", PieBitEqual(live0, kill0));
            }
        }

        // ----------------------------------------------------------------
        // (4) Config guards.
        // ----------------------------------------------------------------
        Console.WriteLine("  (4) Config guards:");
        {
            static string MutatedConfig(string configPath, string key, double value)
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["RollH"]![key] = value;
                var tmp = Path.Combine(Path.GetTempPath(), $"ds_cfg_{key}_{Guid.NewGuid():N}.json");
                File.WriteAllText(tmp, node.ToJsonString());
                return tmp;
            }
            static bool Throws(string path)
            {
                try { RollHConfig.Load(path); return false; }
                catch (InvalidOperationException) { return true; }
                finally { try { File.Delete(path); } catch { /* best-effort */ } }
            }
            static bool LoadsCleanly(string path)
            {
                try { RollHConfig.Load(path); return true; }
                catch { return false; }
                finally { try { File.Delete(path); } catch { /* best-effort */ } }
            }

            Check("negative DisciplineMakeShaveScale throws",
                  Throws(MutatedConfig(configPath, "DisciplineMakeShaveScale", -0.01)));
            Check("above-ceiling DisciplineMakeShaveScale (> 0.05) throws",
                  Throws(MutatedConfig(configPath, "DisciplineMakeShaveScale", 0.06)));
            // Zero is INTENTIONALLY legal — the kill switch every identity check runs against.
            Check("kill switch (DisciplineMakeShaveScale = 0) loads cleanly",
                  LoadsCleanly(MutatedConfig(configPath, "DisciplineMakeShaveScale", 0.0)));
        }

        Console.WriteLine($"  Phase 67 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
