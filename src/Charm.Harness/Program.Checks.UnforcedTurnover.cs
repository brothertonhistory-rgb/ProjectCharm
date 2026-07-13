using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 62 (Session 56) — unforced-turnover handling curve: golden parity.
//
//  The term: a dimensionless factor g(handling) that multiplies each door's own
//  FLAT neutral turnover base share (Roll B team-initiation, Roll F individual):
//      lift(h) = (1 - tanh((h - UnforcedMid) / UnforcedScale)) / 2
//      g       = 1 + UnforcedSpanFrac * (lift(h) - lift(50)),  clamped at UnforcedFloorFrac
//  Anchored: g(50) = 1 for any span; g == 1 for all h when SpanFrac = 0 (kill switch).
//
//  The committed golden fixture tools/unforced_turnover_golden.json is emitted by
//  tools/unforced_turnover_oracle.py (LOCKED shape, provisional magnitudes). 30 cases:
//  2 doors x (7 handlings x 2 spans + 1 out-of-range boundary row). The flat base share
//  the oracle assumed (0.030151 B / 0.090452 F) is cross-checked against the LIVE
//  RollB/RollF config before a single number is trusted, so silent drift between fixture
//  and config fails loudly, not as mysterious mismatches.
//
//  Parity per case (|Δ| <= 1e-12 on Math.Tanh across runtimes): the C# factor matches
//  clamped_g, and flat_base * factor matches final_share.
// ============================================================================
internal static partial class Program
{
    private static bool Phase62UnforcedTurnoverCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 62: unforced-turnover handling curve (golden parity + helpers + config guards) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgB = RollBConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);

        // Flat base shares, recomputed from the LIVE config numerators (the same expression
        // the generators use). These must match what the oracle assumed.
        var flatB = cfgB.BaseDeadBallTurnover / (cfgB.BaseProceed + cfgB.BaseFoul + cfgB.BaseDeadBallTurnover);
        var flatF = cfgF.BaseTurnover        / (cfgF.BaseShotAttempt + cfgF.BaseTurnover + cfgF.BaseNonShootingFoul);

        // ----------------------------------------------------------------
        // (1) Golden parity vs tools/unforced_turnover_golden.json.
        // ----------------------------------------------------------------
        Console.WriteLine("  (1) Golden parity (30 cases, |Δ| <= 1e-12):");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "unforced_turnover_golden.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"golden parity fixture not found: {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // ── fixture contract — validated loudly BEFORE trusting a single number ──
            var liveCfg = MatchupConfig.Load(configPath);
            var kc = root.GetProperty("constants");
            if (kc.GetProperty("UnforcedMid").GetDouble()       != liveCfg.UnforcedMid ||
                kc.GetProperty("UnforcedScale").GetDouble()     != liveCfg.UnforcedScale ||
                kc.GetProperty("UnforcedSpanFrac").GetDouble()  != liveCfg.UnforcedSpanFrac ||
                kc.GetProperty("UnforcedFloorFrac").GetDouble() != liveCfg.UnforcedFloorFrac)
                throw new InvalidOperationException(
                    "golden fixture rejected: Unforced constants do not match the loaded MatchupConfig. " +
                    "Regenerate the fixture or fix the config.");

            var fb = root.GetProperty("flat_base_shares");
            if (Math.Abs(fb.GetProperty("RollB").GetDouble() - flatB) > 1e-12 ||
                Math.Abs(fb.GetProperty("RollF").GetDouble() - flatF) > 1e-12)
                throw new InvalidOperationException(
                    $"golden fixture rejected: flat base shares (B {fb.GetProperty("RollB").GetDouble():R}, " +
                    $"F {fb.GetProperty("RollF").GetDouble():R}) do not match the live config " +
                    $"(B {flatB:R}, F {flatF:R}). Regenerate the fixture or fix the config.");

            var tol = root.GetProperty("tolerance").GetProperty("final_share").GetDouble();
            var cases = root.GetProperty("cases");
            if (cases.GetArrayLength() != 30)
                throw new InvalidOperationException(
                    $"golden fixture rejected: expected 30 cases, got {cases.GetArrayLength()}.");

            var factorOk = true; var shareOk = true; var worst = 0.0;
            foreach (var c in cases.EnumerateArray())
            {
                var door     = c.GetProperty("door").GetString()!;
                var handling = c.GetProperty("handling").GetDouble();
                var span     = c.GetProperty("spanFrac").GetDouble();
                var flat     = door == "RollB" ? flatB : flatF;

                // Apply the case's own span (the fixture exercises SpanFrac 0 and live).
                var pcfg = MatchupConfig.Load(configPath);
                pcfg.UnforcedSpanFrac = span;

                var gC     = Matchup.UnforcedFactor(handling, pcfg);
                var finalC = flat * gC;

                var dFactor = Math.Abs(gC     - c.GetProperty("clamped_g").GetDouble());
                var dShare  = Math.Abs(finalC - c.GetProperty("final_share").GetDouble());
                worst = Math.Max(worst, Math.Max(dFactor, dShare));
                if (dFactor > tol) { factorOk = false; Console.WriteLine($"      factor miss {door}/H={handling}/span={span}: {gC:R} vs {c.GetProperty("clamped_g").GetDouble():R}"); }
                if (dShare  > tol) { shareOk  = false; Console.WriteLine($"      share miss {door}/H={handling}/span={span}: {finalC:R} vs {c.GetProperty("final_share").GetDouble():R}"); }
            }
            Check($"factor parity, all 30 cases within {tol:0e0}", factorOk, $"worst {worst:0.0e0}");
            Check($"final-share parity, all 30 cases within {tol:0e0}", shareOk);
        }
        catch (Exception ex) { pass = false; Console.WriteLine($"  FAIL  (1) threw: {ex.Message}"); }

        // ----------------------------------------------------------------
        // (2) Helper-level tests — anchor, kill switch, monotonicity, shape, floor.
        // ----------------------------------------------------------------
        Console.WriteLine("  (2) Helpers (anchor, span-0 identity, monotone, diminishing returns, floor):");
        {
            var live = MatchupConfig.Load(configPath);   // live span (0.443)

            // Anchor: g(50) == 1 exactly, at live span AND at span 0.
            Check("g(50) == 1 (anchor, live span)", Matchup.UnforcedFactor(50.0, live) == 1.0);

            var zero = MatchupConfig.Load(configPath); zero.UnforcedSpanFrac = 0.0;
            var identity = true;
            for (var h = 0; h <= 99; h++) if (Matchup.UnforcedFactor(h, zero) != 1.0) identity = false;
            Check("span-0 kill switch: g == 1 for every handling 0..99", identity);

            // Strictly decreasing across the walk: bad hands raise, good hands lower.
            var walk = new[] { 0, 20, 35, 50, 70, 85, 99 };
            var mono = true;
            for (var i = 1; i < walk.Length; i++)
                if (!(Matchup.UnforcedFactor(walk[i - 1], live) > Matchup.UnforcedFactor(walk[i], live))) mono = false;
            Check("curve strictly decreasing 0 -> 99", mono);

            // Diminishing returns above ~80: 85->99 improvement < 50->70 improvement.
            var d8599 = Matchup.UnforcedFactor(85, live) - Matchup.UnforcedFactor(99, live);
            var d5070 = Matchup.UnforcedFactor(50, live) - Matchup.UnforcedFactor(70, live);
            Check("diminishing returns: (85->99) < (50->70)", d8599 < d5070, $"{d8599:F4} < {d5070:F4}");

            // Elite floor is the asymptote, not the clamp: g(99) strictly ABOVE FloorFrac
            // (so the clamp never binds for any authored 0..99 rating), but an out-of-range
            // rating drops below and the clamp pins it exactly at the floor.
            Check("g(99) strictly above FloorFrac (clamp inactive in-range)",
                Matchup.UnforcedFactor(99, live) > live.UnforcedFloorFrac,
                $"{Matchup.UnforcedFactor(99, live):F5} > {live.UnforcedFloorFrac}");
            Check("out-of-range rating pinned to FloorFrac (clamp active)",
                Matchup.UnforcedFactor(140, live) == live.UnforcedFloorFrac);
        }

        // ----------------------------------------------------------------
        // (3) Config guards — Load throws on out-of-range; SpanFrac = 0 stays legal.
        // ----------------------------------------------------------------
        Console.WriteLine("  (3) Config guards (Load validation):");
        {
            static string MutatedConfig(string configPath, string key, double value)
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath))!;
                node["Matchup"]![key] = value;
                var tmp = Path.Combine(Path.GetTempPath(), $"unf_cfg_{key}_{Guid.NewGuid():N}.json");
                File.WriteAllText(tmp, node.ToJsonString());
                return tmp;
            }

            static bool Throws(string path)
            {
                try { MatchupConfig.Load(path); return false; }
                catch (InvalidOperationException) { return true; }
                finally { try { File.Delete(path); } catch { /* temp cleanup best-effort */ } }
            }

            Check("UnforcedScale = 0 throws",        Throws(MutatedConfig(configPath, "UnforcedScale", 0.0)));
            Check("negative UnforcedSpanFrac throws", Throws(MutatedConfig(configPath, "UnforcedSpanFrac", -0.1)));
            Check("UnforcedFloorFrac = 0 throws",     Throws(MutatedConfig(configPath, "UnforcedFloorFrac", 0.0)));
            Check("UnforcedFloorFrac > 1 throws",     Throws(MutatedConfig(configPath, "UnforcedFloorFrac", 1.5)));
            Check("UnforcedMid out of range throws",  Throws(MutatedConfig(configPath, "UnforcedMid", 120.0)));

            // SpanFrac = 0 must remain LEGAL — the clean kill switch.
            var killPath = MutatedConfig(configPath, "UnforcedSpanFrac", 0.0);
            var killOk = false;
            try { MatchupConfig.Load(killPath); killOk = true; }
            catch (InvalidOperationException) { killOk = false; }
            finally { try { File.Delete(killPath); } catch { /* temp cleanup best-effort */ } }
            Check("UnforcedSpanFrac = 0 loads (kill switch stays legal)", killOk);
        }

        Console.WriteLine(pass
            ? "  Phase 62 unforced-turnover: ALL OK"
            : "  Phase 62 unforced-turnover: FAILURES ABOVE");
        return pass;
    }
}
