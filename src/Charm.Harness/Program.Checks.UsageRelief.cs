using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 66 (Session 60) — the USAGE-RELIEF bonus: the low-usage half of the
//  usage↔efficiency curve.
//
//  The wire. Roll E already stamps the volume TAX (Phase 17/27):
//      pressure = max(0, finalShare − equalShare),  equalShare = 1.0 / populated
//  computed on POST-floor/rail shares. Nothing paid for the other side, so every
//  below-share player read exactly 0 and a 13%-usage specialist shot identically to a
//  20%-usage one. Session 60 adds the mirror, in the same pass off the same array:
//      relief   = max(0, equalShare − finalShare)            → RollEGenerator.ReliefAt
//      makePct  = clamp01(makePct × (1 + relief × Scale))    → RollHGenerator.ApplyUsageRelief
//  applied in Roll H AFTER the C3 penalty block and BEFORE the C4 passing converter.
//
//  Golden fixture tools/usage_relief_golden.json is emitted by tools/usage_relief_oracle.py
//  (LOCKED shape; the magnitude is a placeholder, page-tuned later, never suite-asserted).
//  8 cases: the signed-off archetypes + a synthetic clamp boundary + two kill-switch rows.
//  Constants are cross-checked against the loaded RollHConfig before a single number is
//  trusted, so silent drift between fixture and config fails loudly.
//
//  Parity binds to the ENGINE, not to a copy: the transform is a named static that the
//  live Roll H calls and this check calls. If the C# and the oracle ever disagree, THE
//  ORACLE WINS — a failure here is a PORT BUG, never a tolerance to widen.
//
//  Sub-checks:
//    (1) Golden parity — relief, multiplier, and makePctAfterRelief per case, 1e-12,
//        with the identity rows additionally required to be BIT-exact.
//    (2) Formula invariants — the oracle's, ported.
//    (3) Pivot consistency — the tax and the relief share one pivot, probed at exact
//        points (equalShare ± ε) for populated = 5 and 4. No numeric transition search.
//    (4) Wiring proofs — the things the oracle cannot test: the real Roll E→H path, the
//        untouched tax side, the null/reset/putback/FastBreak identities, and
//        gravity-separability with C4 neutralized and nothing clamping.
//    (5) Config guards — negative scale throws; 0 (the kill switch) loads cleanly.
// ============================================================================
internal static partial class Program
{
    private static bool Phase66UsageReliefCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 66: usage-relief bonus (golden parity + invariants + pivot + wiring + config guards) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgH = RollHConfig.Load(configPath);
        var cfgM = MatchupConfig.Load(configPath);
        var scale = cfgH.UsageReliefBonusScale;

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/usage_relief_golden.json.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity (8 cases, |Δ| <= 1e-12; identity rows BIT-exact):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "usage_relief_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // ── fixture contract — constants validated loudly BEFORE trusting a number ──
            var kc = root.GetProperty("constants");
            if (kc.GetProperty("UsageReliefBonusScale").GetDouble() != scale)
                throw new InvalidOperationException(
                    "golden fixture rejected: UsageReliefBonusScale does not match the loaded RollHConfig " +
                    $"(fixture {kc.GetProperty("UsageReliefBonusScale").GetDouble()}, config {scale}). " +
                    "Regenerate the fixture or fix the config.");

            var tol = root.GetProperty("tolerance").GetDouble();
            var cases = root.GetProperty("cases");
            if (cases.GetArrayLength() != 8)
                throw new InvalidOperationException($"golden fixture rejected: expected 8 cases, got {cases.GetArrayLength()}.");

            var worst = 0.0; var allOk = true; var identitySeen = 0; var clampSeen = 0;
            foreach (var c in cases.EnumerateArray())
            {
                var name       = c.GetProperty("name").GetString()!;
                var populated  = c.GetProperty("populated").GetInt32();
                var finalShare = c.GetProperty("finalShare").GetDouble();
                var caseScale  = c.GetProperty("scale").GetDouble();
                var before     = c.GetProperty("makePctBeforeRelief").GetDouble();
                var isIdentity = c.GetProperty("identity").GetBoolean();
                var isClamped  = c.GetProperty("clamped").GetBoolean();

                // The engine's own primitives — the SAME statics the live path calls.
                var relief   = RollEGenerator.ReliefAt(finalShare, populated);
                var pressure = RollEGenerator.PressureAt(finalShare, populated);
                var after    = RollHGenerator.ApplyUsageRelief(before, relief, caseScale);
                var mult     = relief > 0.0 && caseScale > 0.0 ? 1.0 + relief * caseScale : 1.0;

                var dR = Math.Abs(relief   - c.GetProperty("relief").GetDouble());
                var dP = Math.Abs(pressure - c.GetProperty("pressure").GetDouble());
                var dM = Math.Abs(mult     - c.GetProperty("multiplier").GetDouble());
                var dA = Math.Abs(after    - c.GetProperty("makePctAfterRelief").GetDouble());
                var d  = Math.Max(Math.Max(dR, dP), Math.Max(dM, dA));
                worst  = Math.Max(worst, d);

                var ok = d <= tol;
                // The identity rows are held to a HARDER bar than the tolerance: the branch
                // must return the input object-identical, not merely within 1e-12 of it.
                if (isIdentity) { identitySeen++; ok = ok && after == before; }
                if (isClamped)  { clampSeen++;    ok = ok && after == 1.0; }
                allOk = allOk && ok;

                if (!ok)
                    Console.WriteLine($"      MISMATCH [{name}]: relief Δ{dR:e2} pressure Δ{dP:e2} " +
                                      $"mult Δ{dM:e2} after Δ{dA:e2} (got {after:R}, want " +
                                      $"{c.GetProperty("makePctAfterRelief").GetDouble():R})");
            }

            Check($"golden parity, 8 cases", allOk, $"worst |Δ| = {worst:e2} (tol {tol:e0})");
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
            // Relief is exactly 0 at and above the equal share.
            var zeroAbove = new[] { 0.20, 0.2000000001, 0.25, 0.43, 1.0 }
                .All(s => RollEGenerator.ReliefAt(s, 5) == 0.0);
            Check("relief exactly 0.0 at and above equal share", zeroAbove);

            // The three monotonicities, share rising, everything else fixed.
            var shares = new[] { 0.00, 0.05, 0.09, 0.135, 0.18, 0.20, 0.25, 0.43 };
            bool Monotone(double before, out string detail)
            {
                double? pr = null, pm = null, pa = null; var ok = true;
                foreach (var s in shares)
                {
                    var r = RollEGenerator.ReliefAt(s, 5);
                    var m = r > 0.0 && scale > 0.0 ? 1.0 + r * scale : 1.0;
                    var a = RollHGenerator.ApplyUsageRelief(before, r, scale);
                    if (pr is not null) ok = ok && r <= pr && m <= pm && a <= pa;
                    pr = r; pm = m; pa = a;
                }
                detail = $"before={before:P2}";
                return ok;
            }
            Check("relief / multiplier / makePctAfterRelief all non-increasing as final share rises",
                  Monotone(0.45, out var d1), d1);
            // A saturated input: the flat region at the clamp is permitted, a rise is not.
            Check("monotone holds through the clamp (flat saturated region permitted)",
                  Monotone(0.95, out var d2), d2);

            // Equal-share player: BIT-identity on a LIVE scale, through the identity branch.
            var eq = RollHGenerator.ApplyUsageRelief(0.45, RollEGenerator.ReliefAt(0.20, 5), scale);
            Check("equal-share player -> BIT-identity on a live scale", eq == 0.45, $"delta={eq - 0.45:e1}");

            // Kill switch: BIT-identity at every relief and every input, including the
            // clamping-capable one — the identity branch proven exactly where a live scale
            // would saturate.
            var ks = true;
            foreach (var s in new[] { 0.00, 0.05, 0.09, 0.135, 0.20, 0.43 })
                foreach (var b in new[] { 0.10, 0.45, 0.95, cfgH.RimCeiling })
                    ks = ks && RollHGenerator.ApplyUsageRelief(b, RollEGenerator.ReliefAt(s, 5), 0.0) == b;
            Check("kill switch (scale = 0) -> BIT-identity at every relief and every input", ks);

            // Bonus-only: the term can never lower make%.
            var bonusOnly = true;
            foreach (var s in new[] { 0.00, 0.09, 0.135, 0.20, 0.43 })
                foreach (var b in new[] { 0.0, 0.10, 0.45, 0.95, 1.0 })
                    bonusOnly = bonusOnly &&
                        RollHGenerator.ApplyUsageRelief(b, RollEGenerator.ReliefAt(s, 5), scale) >= b;
            Check("bonus-only: makePct never falls", bonusOnly);

            // The clamp zone is REACHABLE against the live config — so the clamp branch is
            // production code, not a defensive no-op. (No claim that the live range cannot
            // clamp; the point is exactly that it can.)
            var reliefMax5 = RollEGenerator.ReliefAt(0.09, 5);
            var raw = cfgH.RimCeiling * (1.0 + reliefMax5 * scale);
            Check("configured make domain CAN reach the clamp (RimCeiling x multiplier > 1)",
                  raw > 1.0, $"{cfgH.RimCeiling} x {1.0 + reliefMax5 * scale:F4} = {raw:F4}");
            Check("clamp saturates at EXACTLY 1.0",
                  RollHGenerator.ApplyUsageRelief(0.95, reliefMax5, scale) == 1.0);
        }

        // ----------------------------------------------------------------
        // (3) Pivot consistency — exact semantics, both lineup sizes.
        //     The tax and the relief must agree about where "equal share" sits: below it
        //     relief only, at it both exactly 0, above it tax only. Probed at exact points,
        //     never searched for numerically.
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Pivot consistency (tax and relief share one pivot):");
        foreach (var populated in new[] { 5, 4 })
        {
            var eqShare = 1.0 / populated;
            const double Eps = 1e-9;

            var below = RollEGenerator.ReliefAt(eqShare - Eps, populated) > 0.0
                     && RollEGenerator.PressureAt(eqShare - Eps, populated) == 0.0;
            var at    = RollEGenerator.ReliefAt(eqShare, populated) == 0.0
                     && RollEGenerator.PressureAt(eqShare, populated) == 0.0;
            var above = RollEGenerator.PressureAt(eqShare + Eps, populated) > 0.0
                     && RollEGenerator.ReliefAt(eqShare + Eps, populated) == 0.0;

            Check($"populated={populated}: equalShare−ε -> relief only", below);
            Check($"populated={populated}: exactly equalShare -> BOTH exactly 0", at);
            Check($"populated={populated}: equalShare+ε -> tax only", above);

            // The algebraic identity that makes the shared pivot structural rather than a
            // coincidence of two similar-looking formulas.
            var identity = new[] { 0.0, 0.05, 0.09, 0.135, eqShare, 0.30, 0.43, 1.0 }.All(s =>
                RollEGenerator.PressureAt(s, populated) - RollEGenerator.ReliefAt(s, populated) == s - eqShare
                && RollEGenerator.PressureAt(s, populated) * RollEGenerator.ReliefAt(s, populated) == 0.0);
            Check($"populated={populated}: pressure − relief == share − equalShare exactly, and never both non-zero",
                  identity, $"equalShare={eqShare:F4}");
        }
        // The pivot MOVES with the populated count — the four-man relief is the larger one.
        Check("pivot follows populated count (four-man 9% relief 0.16 > five-man 0.11)",
              RollEGenerator.ReliefAt(0.09, 4) == 0.16 && Math.Abs(RollEGenerator.ReliefAt(0.09, 5) - 0.11) < 1e-15,
              $"4man={RollEGenerator.ReliefAt(0.09, 4):F4} 5man={RollEGenerator.ReliefAt(0.09, 5):F4}");

        // ----------------------------------------------------------------
        // (4) Wiring proofs — through the REAL Roll E→H path and direct-stamp probes.
        // ----------------------------------------------------------------
        Console.WriteLine("  (4) Wiring proofs:");
        {
            var cfgKill = LoadWithRollHOverride(configPath, ("UsageReliefBonusScale", 0.0));

            // A uniform all-50 player; the usage-score inputs and the make/screen/help reads
            // are the only overrides, so nothing else confounds the probe.
            static Player Mk(string id, int selfCreation = 50, int close = 50, int postMoves = 50,
                             int outside = 50, int mid = 50, int finishing = 50,
                             int screening = 50, int helpDef = 50, int offBallDef = 50,
                             int hierarchyRank = 5)
                => new Player(id)
                {
                    PlayerId = Math.Abs(id.GetHashCode()) % 100000,
                    Close = close, Mid = mid, Outside = outside, Finishing = finishing,
                    FreeThrow = 50, FoulDrawing = 50,
                    RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
                    BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = selfCreation,
                    PostMoves = postMoves, OffBallMovement = 50, Screening = screening,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50, HelpDefense = helpDef, OffBallDefense = offBallDef,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50, Quickness = 50,
                    FirstStep = 50, Vertical = 50, Endurance = 50, Hustle = 50, BasketballIQ = 50,
                    Discipline = 50, HierarchyRank = hierarchyRank,
                };

            // Back-calculate the pre-block/pre-foul makePct out of Roll H's seven-way pie.
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

            // ── (4a) The REAL Roll E→H path: a below-share big's make% rises. ──
            //    A star with elite scoring attributes drains the usage pie; the four
            //    ordinary teammates land below the equal share and earn relief.
            //    HierarchyRank 10 = primary option, 1 = kept out of the offense (Player.cs:61);
            //    the four role players stay at the rank-5 regression anchor (weight 1.0 at any
            //    heliocentric exponent), so the tilt here is the star's rank + attributes.
            {
                var game = new GameState(new FoulTracker(7, 10));
                game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(1),
                    Mk("star", selfCreation: 95, close: 92, postMoves: 90, outside: 92, mid: 92, finishing: 95, hierarchyRank: 10));
                for (var i = 2; i <= 5; i++)
                    game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"role{i}", hierarchyRank: 5));
                for (var i = 1; i <= 5; i++)
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"def{i}"));

                var genE = new RollEGenerator(RollEConfig.Load(configPath), game);
                var st = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound);
                var gen = genE.GenerateWithPressure(st);

                // The generator must actually produce the asymmetry the check depends on.
                var starAbove = gen.Pressures[0] > 0.0 && gen.Reliefs[0] == 0.0;
                var bigBelow  = gen.Reliefs[4] > 0.0 && gen.Pressures[4] == 0.0;
                Check("live Roll E: the star is taxed (pressure > 0, relief == 0)",
                      starAbove, $"share={gen.FinalShares[0]:P2} pressure={gen.Pressures[0]:F4}");
                Check("live Roll E: the role big is relieved (relief > 0, pressure == 0)",
                      bigBelow, $"share={gen.FinalShares[4]:P2} relief={gen.Reliefs[4]:F4}");

                // Every slot, every possession: the two terms are exact mirrors off one array.
                var mirrored = true;
                for (var i = 0; i < 5; i++)
                    mirrored = mirrored
                        && gen.Pressures[i] * gen.Reliefs[i] == 0.0
                        && gen.Pressures[i] - gen.Reliefs[i] == gen.FinalShares[i] - 0.20;
                Check("live Roll E: pressure/relief exact mirrors on the SAME post-rail shares (all 5 slots)", mirrored);

                // Now walk Roll E → Roll H on the relieved slot and compare live vs kill.
                double MakeForSlot(int slotNumber, RollHConfig h)
                {
                    var stamped = st with
                    {
                        SelectedSlot  = game.HomeLineup.SlotAt(slotNumber),
                        ShotType      = ShotLocation.Mid,
                        UsagePressure = gen.Pressures[slotNumber - 1],
                        UsageRelief   = gen.Reliefs[slotNumber - 1],
                    };
                    return MakePct(new RollHGenerator(h, cfgM, game).Generate(stamped));
                }

                var bigLive = MakeForSlot(5, cfgH);
                var bigKill = MakeForSlot(5, cfgKill);
                Check("below-share big: make% RISES vs kill switch",
                      bigLive > bigKill,
                      $"kill={bigKill:P2} live={bigLive:P2} (+{(bigLive - bigKill) * 100:F2}pp, relief={gen.Reliefs[4]:F4})");

                // ── (4b) The anchor ruling as an assertion: the TAX SIDE DOES NOT MOVE. ──
                var starLive = MakeForSlot(1, cfgH);
                var starKill = MakeForSlot(1, cfgKill);
                Check("above-share star: make% BIT-IDENTICAL live vs kill (the tax side is untouched)",
                      starLive == starKill, $"{starLive:R}");
            }

            // ── (4c) Direct-stamp harness states: null relief -> 0.0 -> bit-unchanged. ──
            //    Every existing Roll H probe constructs PossessionState without UsageRelief,
            //    so it reads null. That must be numerically identical to pre-S60.
            {
                var game = new GameState(new FoulTracker(7, 10));
                for (var i = 1; i <= 5; i++)
                {
                    game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"o{i}"));
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"d{i}"));
                }
                var stNull = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: game.HomeLineup.SlotAt(1), ShotType: ShotLocation.Mid);

                Check("null UsageRelief reads as 0.0 (state default)", stNull.UsageRelief is null);

                var live = new RollHGenerator(cfgH, cfgM, game).Generate(stNull);
                var kill = new RollHGenerator(cfgKill, cfgM, game).Generate(stNull);
                Check("null-stamped state: pie BIT-identical live vs kill (existing probes hold)",
                      PieBitEqual(live, kill), $"make={MakePct(live):P4}");

                var stZero = stNull with { UsageRelief = 0.0 };
                Check("explicit UsageRelief = 0.0: pie BIT-identical to null-stamped",
                      PieBitEqual(new RollHGenerator(cfgH, cfgM, game).Generate(stZero), live));

                // ── (4d) FastBreak: relief stamped 0 on a break -> bit-unchanged. ──
                var stBreak = stNull with { FastBreak = true, UsageRelief = 0.0 };
                Check("FastBreak: pie BIT-identical live vs kill",
                      PieBitEqual(new RollHGenerator(cfgH, cfgM, game).Generate(stBreak),
                                  new RollHGenerator(cfgKill, cfgM, game).Generate(stBreak)));

                // ── (4e) Putback: Roll H short-circuits before the C-block, so even a
                //    stale relief (the latent-leak case) cannot reach the make%. ──
                var stPutback = stNull with
                {
                    ReboundSlot = game.HomeLineup.SlotAt(1),
                    UsageRelief = 0.11,   // deliberately stale/hostile
                };
                var pbLive = new RollHGenerator(cfgH, cfgM, game).Generate(stPutback, putback: true);
                var pbKill = new RollHGenerator(cfgKill, cfgM, game).Generate(stPutback, putback: true);
                Check("putback probe: pie BIT-identical live vs kill even with a hostile stale relief",
                      PieBitEqual(pbLive, pbKill), "short-circuit holds above the C-block");
            }

            // ── (4f) RollK's ResetOffense clears the relief (the other half of the leak guard). ──
            {
                var game = new GameState(new FoulTracker(7, 10));
                for (var i = 1; i <= 5; i++)
                {
                    game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"ko{i}"));
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"kd{i}"));
                }
                var dirty = new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: game.HomeLineup.SlotAt(1), ShotType: ShotLocation.Mid)
                    with { UsagePressure = 0.11, UsageRelief = 0.11, UsageResidualPressure = 0.02 };

                // Drive RollK to the ResetOffense arm with a certainty pie.
                var weights = new System.Collections.Generic.Dictionary<OffensiveReboundOutcome, double>();
                foreach (var o in Enum.GetValues<OffensiveReboundOutcome>()) weights[o] = 0.0;
                weights[OffensiveReboundOutcome.ResetOffense] = 1.0;
                var resetPie = new Pie<OffensiveReboundOutcome>(weights, 1e-9);
                var res = RollK.Execute(dirty, resetPie, game, new SystemRng(1));
                var after = res switch { Continue c => c.State, Terminal t => t.State, _ => dirty };
                Check("RollK ResetOffense clears UsageRelief to null (alongside UsagePressure)",
                      after.UsageRelief is null && after.UsagePressure is null,
                      $"relief={(after.UsageRelief?.ToString() ?? "null")}");
            }

            // ── (4g) Gravity separability — C4 NEUTRALIZED and nothing clamping. ──
            //    The architectural claim is INPUT independence: relief reads no gravity
            //    field. It is NOT a claim that final ratios survive arbitrary downstream
            //    transforms — C4 runs after relief and would legitimately break raw ratio
            //    equality, which is exactly why it is zeroed here (TeamConversionQuality = 0
            //    makes the passing bonus exactly 0). The zone is Three, so C6 is skipped by
            //    its zone multiplier; Screening and OffBallDefense are 0 so C5.5 and C7
            //    contribute exactly 0. Whatever is left is g(gravity) x multiplier, so the
            //    live/kill ratio must be the multiplier itself — in BOTH gravity states.
            {
                var game = new GameState(new FoulTracker(7, 10));
                for (var i = 1; i <= 5; i++)
                {
                    game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk($"go{i}", screening: 0));
                    game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk($"gd{i}", offBallDef: 0, helpDef: 0));
                }

                const double Relief = 0.11;
                var mult = 1.0 + Relief * scale;

                PossessionState Probe(bool gravityOn) => new PossessionState(
                    PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                    Entry: EntryType.DeadBallInbound,
                    SelectedSlot: game.HomeLineup.SlotAt(1), ShotType: ShotLocation.Three)
                    with
                    {
                        UsageRelief           = Relief,
                        ShooterAttentionShare = 0.10,
                        TeamBaseOpenness      = gravityOn ? 0.80 : 0.0,
                        TeamGravityLevel      = gravityOn ? 0.70 : 0.0,
                        TeamSpacingLevel      = gravityOn ? 0.65 : 0.0,
                        TeamConversionQuality = 0.0,   // C4 neutralized EXACTLY
                    };

                var ok = true; var detail = "";
                foreach (var gravityOn in new[] { false, true })
                {
                    var st   = Probe(gravityOn);
                    var live = MakePct(new RollHGenerator(cfgH, cfgM, game).Generate(st));
                    var kill = MakePct(new RollHGenerator(cfgKill, cfgM, game).Generate(st));
                    // Non-clamping precondition — a saturated branch would flatten the ratio
                    // and the check would pass for the wrong reason.
                    var nonClamping = live < 1.0 && kill * mult < 1.0;
                    var ratio = kill > 0.0 ? live / kill : double.NaN;
                    ok = ok && nonClamping && Math.Abs(ratio - mult) <= 1e-9;
                    detail += $"[gravity {(gravityOn ? "on " : "off")}: kill={kill:P2} live={live:P2} ratio={ratio:F9}] ";
                }
                Check($"gravity separability: live/kill ratio == the relief multiplier ({mult:F4}) in BOTH gravity states",
                      ok, detail.Trim());

                // And the compounding is real, not just separable: gravity on gives a
                // strictly bigger absolute gain than gravity off, because relief multiplies
                // a make% that C1 already lifted. That IS the design.
                var offLive = MakePct(new RollHGenerator(cfgH, cfgM, game).Generate(Probe(false)));
                var offKill = MakePct(new RollHGenerator(cfgKill, cfgM, game).Generate(Probe(false)));
                var onLive  = MakePct(new RollHGenerator(cfgH, cfgM, game).Generate(Probe(true)));
                var onKill  = MakePct(new RollHGenerator(cfgKill, cfgM, game).Generate(Probe(true)));
                Check("gravity and relief COMPOUND (the open shooter's relief gain is the larger one)",
                      (onLive - onKill) > (offLive - offKill),
                      $"gravity off: +{(offLive - offKill) * 100:F2}pp  |  gravity on: +{(onLive - onKill) * 100:F2}pp");
            }
        }

        // ----------------------------------------------------------------
        // (5) Config guards.
        // ----------------------------------------------------------------
        Console.WriteLine("  (5) Config guards:");
        {
            static string MutatedConfig(string configPath, string key, double value)
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["RollH"]![key] = value;
                var tmp = Path.Combine(Path.GetTempPath(), $"ur_cfg_{key}_{Guid.NewGuid():N}.json");
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

            Check("negative UsageReliefBonusScale throws",
                  Throws(MutatedConfig(configPath, "UsageReliefBonusScale", -0.1)));
            // Zero is INTENTIONALLY legal — it is the kill switch every identity check above
            // runs against, so the guard must not reject it.
            Check("kill switch (UsageReliefBonusScale = 0) loads cleanly",
                  LoadsCleanly(MutatedConfig(configPath, "UsageReliefBonusScale", 0.0)));
        }

        Console.WriteLine($"  Phase 66 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    /// <summary>Load a RollHConfig with one or more RollH knobs overridden — the
    /// LoadWithMatchupOverride pattern, pointed at the RollH block.</summary>
    private static RollHConfig LoadWithRollHOverride(string configPath, params (string key, double val)[] overrides)
    {
        var node = JsonNode.Parse(File.ReadAllText(configPath))!;
        foreach (var (k, v) in overrides) node["RollH"]![k] = v;
        var tmp = Path.Combine(Path.GetTempPath(), $"ur_h_{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, node.ToJsonString());
        try { return RollHConfig.Load(tmp); }
        finally { try { File.Delete(tmp); } catch { /* best-effort */ } }
    }
}
