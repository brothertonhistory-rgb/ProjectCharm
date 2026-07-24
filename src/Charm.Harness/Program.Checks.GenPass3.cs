using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 69 — Pass-3 two-plane budget generation math, fixture replay parity.
// Phase 70 — Pass-3 LIVE generator: sampler moments + the 46k invariant/band audit.
//
// The executable spec is tools/gen_pass3_budget_oracle.py (LOCKED SPEC 2026-07-24,
// S68); the committed replay fixture tools/gen_pass3_replay_fixture_s69.json
// (schema s69-1, seed 20260724, 301 players: 300 branch-representative rows off the
// canonical 46k cohort — every role x plane pairing, concentration extremes,
// cap-binding interior spend, the short post-role and tall-shooter cards — plus ONE
// SYNTHETIC row for the epsilon pull floor, which no real player reaches at the
// locked constants (a ~-7-sigma dice draw), checkpointed through the oracle's own
// generate_player). Phase 69 does exactly what tools/gen_pass3_replay_check.py
// proved sufficient: from the RECORDED DRAWS + FROZEN CONSTANTS ALONE,
// PlayerGenPass3.BuildFromDraws rebuilds every checkpoint for every player —
// integers EXACT, floats within the fixture-declared ABSOLUTE 1e-9 (the S43 ruling:
// the allowance exists for exp/tanh/pow libm gaps; everything else is plain IEEE-754
// and lands bit-identical given the same operation order). The committed edge table
// probes every height-CDF cumulative boundary at ±1e-12, so a '<' vs '<=' mismatch
// in the inverse-CDF lookup cannot survive.
//
// Phase 70 proves the DRAWING (the Phase-60 pattern): each live Beta pair vs its
// closed-form moments at N=200k; then ONE canonical 46k cohort (the oracle's own
// seed) must satisfy (a) the EXACT structural invariants — true on ANY honest
// cohort, failed absolutely — and (b) the DISTRIBUTIONAL BANDS the locked oracle's
// audits ruled, each printed with seed, cohort size, observed value, and band.
// Bands, not point values: C# System.Random is a different stream from Python's
// random, so the oracle's seed-specific numbers are unreproducible BY DESIGN.
//
// If the C# and the oracle ever disagree, the oracle wins — a failure here is a
// PORT BUG, never a tolerance to widen and never a fixture to regenerate casually.
// Both phases touch NO live gameplay path: the Pass-3 generator is STANDALONE by
// the S69 scope wall (bridge swap is the next session's whole story).
// ============================================================================
internal static partial class Program
{
    private const string GenPass3FixtureFile = "gen_pass3_replay_fixture_s69.json";
    private const double GenPass3Tol = 1e-9;         // ABSOLUTE — the fixture header's declared convention
    private const int GenPass3LiveSeed = 20260724;   // the oracle's canonical seed
    private const int GenPass3LiveN = 46000;         // the oracle's N_CANDIDATE
    private const int GenPass3MomentN = 200_000;

    // The fixture's draw_order contract: the 68 semantic RNG slots per player, in
    // stream order (single home: the oracle's _flat_draws).
    private static readonly string[] GenPass3DrawOrder = BuildGenPass3DrawOrder();
    private static string[] BuildGenPass3DrawOrder()
    {
        var slots = new List<string> { "height_u", "ws_noise", "a" };
        foreach (var k in PlayerGenPass3.ATH_KEYS) slots.Add($"ath_noise.{k}");
        slots.AddRange(new[] { "weight_noise", "def_noise", "role_u", "q", "c" });
        foreach (var f in PlayerGenPass3.FAMILY_ORDER) slots.Add($"pull_gauss.{f}");
        foreach (var k in PlayerGenPass3.SPEND_SKILLS) slots.Add($"within_gauss.{k}");
        foreach (var k in PlayerGenPass3.SPEND_SKILLS) slots.Add($"base_jitter_gauss.{k}");
        slots.AddRange(new[] { "arrival_raw", "ft_idio" });
        return slots.ToArray();   // 68
    }

    private static bool Phase69GenPass3ReplayParityCheck()
    {
        Console.WriteLine("\n--- Phase 69: Pass-3 budget generation math — fixture replay parity (standalone port) ---");
        try
        {
            RunGenPass3ReplayParity();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    /// <summary>The gate itself — throws on the first divergence (the oracle wins).</summary>
    private static void RunGenPass3ReplayParity()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tools", GenPass3FixtureFile);
        if (!File.Exists(path))
            throw new InvalidOperationException($"gen pass3 replay fixture not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // ── fixture contract, validated loudly before any replay ────────────────
        if (!root.TryGetProperty("schema", out var schema))
            throw new InvalidOperationException("gen pass3 fixture rejected: no schema block.");

        if (!schema.TryGetProperty("draw_order", out var drawOrder)
            || drawOrder.GetArrayLength() != GenPass3DrawOrder.Length)
            throw new InvalidOperationException(
                "gen pass3 fixture rejected: draw_order missing or not exactly 68 slots.");
        for (var i = 0; i < GenPass3DrawOrder.Length; i++)
            if (drawOrder[i].GetString() != GenPass3DrawOrder[i])
                throw new InvalidOperationException(
                    $"gen pass3 fixture rejected: draw_order[{i}] is '{drawOrder[i].GetString()}', " +
                    $"expected '{GenPass3DrawOrder[i]}'. The fixture does not match the locked contract.");

        if (!schema.TryGetProperty("key_orders", out var keyOrders))
            throw new InvalidOperationException("gen pass3 fixture rejected: no key_orders block.");
        AssertKeyOrder(keyOrders, "ATH_KEYS", PlayerGenPass3.ATH_KEYS);
        AssertKeyOrder(keyOrders, "SPEND_SKILLS", PlayerGenPass3.SPEND_SKILLS);
        AssertKeyOrder(keyOrders, "FAMILY_ORDER", PlayerGenPass3.FAMILY_ORDER);
        AssertKeyOrder(keyOrders, "ROLES", PlayerGenPass3.ROLES);

        // ── the constants tripwire: itemized drift, replay NOT run ──────────────
        if (!schema.TryGetProperty("constants", out var constantsEcho))
            throw new InvalidOperationException("gen pass3 fixture rejected: no constants echo.");
        var csharp = PlayerGenPass3.ConstantsEcho();
        var drift = new List<string>();
        var echoCount = 0;
        foreach (var prop in constantsEcho.EnumerateObject())
        {
            echoCount++;
            if (!csharp.TryGetValue(prop.Name, out var live))
            {
                drift.Add($"    {prop.Name,-24} in fixture echo, MISSING from C# transcription");
                continue;
            }
            CompareEchoEntry(prop.Name, prop.Value, live, drift);
        }
        if (echoCount != csharp.Count)
            drift.Add($"    <count>                 fixture echo has {echoCount} constants, C# transcribes {csharp.Count}");
        if (drift.Count > 0)
            throw new InvalidOperationException(
                $"GEN PASS3 CONSTANTS TRIPWIRE: {drift.Count} mismatch(es) between the fixture echo and the " +
                "C# transcription — replay NOT run. The oracle source is canonical; fix PlayerGenPass3:\n" +
                string.Join("\n", drift));
        Console.WriteLine($"constants echo vs C# transforms: {echoCount}/{echoCount} match — tripwire clear");

        if (!root.TryGetProperty("players", out var players) || players.GetArrayLength() == 0)
            throw new InvalidOperationException("gen pass3 fixture rejected: no players.");

        // ── replay every row; throw on the FIRST divergence (the oracle wins) ───
        var checks = 0;
        var maxDev = 0.0;

        void ChkInt(int idx, string field, long expected, long got)
        {
            checks++;
            if (expected != got)
                throw new InvalidOperationException(
                    $"GEN PASS3 REPLAY FAILURE — player {idx}, field {field}:\n" +
                    $"  expected  {expected}\n  actual    {got}\n" +
                    "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
        }
        void ChkFloat(int idx, string field, double expected, double got)
        {
            checks++;
            var dv = Math.Abs(expected - got);
            if (dv > maxDev) maxDev = dv;
            if (dv > GenPass3Tol)
                throw new InvalidOperationException(
                    $"GEN PASS3 REPLAY FAILURE — player {idx}, field {field}:\n" +
                    $"  expected  {expected:R}\n  actual    {got:R}\n  |diff|    {dv:E3} (tolerance {GenPass3Tol:E0} absolute)\n" +
                    "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
        }
        void ChkStr(int idx, string field, string? expected, string? got)
        {
            checks++;
            if (!string.Equals(expected, got, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"GEN PASS3 REPLAY FAILURE — player {idx}, field {field}:\n" +
                    $"  expected  '{expected}'\n  actual    '{got}'\n" +
                    "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
        }

        var syntheticSeen = 0;
        foreach (var row in players.EnumerateArray())
        {
            var idx = row.GetProperty("index").GetInt32();
            if (idx < 0) syntheticSeen++;
            var dEl = row.GetProperty("draws");
            var cp = row.GetProperty("checkpoints");

            var draws = new Pass3Draws
            {
                HeightU = dEl.GetProperty("height_u").GetDouble(),
                WsNoise = dEl.GetProperty("ws_noise").GetDouble(),
                A = dEl.GetProperty("a").GetDouble(),
                WeightNoise = dEl.GetProperty("weight_noise").GetDouble(),
                DefNoise = dEl.GetProperty("def_noise").GetDouble(),
                RoleU = dEl.GetProperty("role_u").GetDouble(),
                Q = dEl.GetProperty("q").GetDouble(),
                C = dEl.GetProperty("c").GetDouble(),
                ArrivalRaw = dEl.GetProperty("arrival_raw").GetDouble(),
                FtIdio = dEl.GetProperty("ft_idio").GetDouble(),
            };
            var athEl = dEl.GetProperty("ath_noise");
            foreach (var k in PlayerGenPass3.ATH_KEYS)
                draws.AthNoise[k] = athEl.GetProperty(k).GetDouble();
            var pullEl = dEl.GetProperty("pull_gauss");
            foreach (var f in PlayerGenPass3.FAMILY_ORDER)
                draws.PullGauss[f] = pullEl.GetProperty(f).GetDouble();
            var withinEl = dEl.GetProperty("within_gauss");
            foreach (var k in PlayerGenPass3.SPEND_SKILLS)
                draws.WithinGauss[k] = withinEl.GetProperty(k).GetDouble();
            var jitterEl = dEl.GetProperty("base_jitter_gauss");
            foreach (var k in PlayerGenPass3.SPEND_SKILLS)
                draws.BaseJitterGauss[k] = jitterEl.GetProperty(k).GetDouble();

            var res = PlayerGenPass3.BuildFromDraws(draws);

            // ── asserts, in the reference reader's order ─────────────────────────
            ChkInt(idx, "Height", cp.GetProperty("Height").GetInt32(), res.Height);
            ChkInt(idx, "Wingspan", cp.GetProperty("Wingspan").GetInt32(), res.Wingspan);
            ChkInt(idx, "Weight", cp.GetProperty("Weight").GetInt32(), res.Weight);
            var athCp = cp.GetProperty("ath");
            foreach (var k in PlayerGenPass3.ATH_KEYS)
                ChkInt(idx, $"ath.{k}", athCp.GetProperty(k).GetInt32(), res.Ath[k]);
            ChkFloat(idx, "dplane", cp.GetProperty("dplane").GetDouble(), res.DPlane);
            ChkStr(idx, "dcat", cp.GetProperty("dcat").GetString(), res.DCat);
            ChkStr(idx, "role", cp.GetProperty("role").GetString(), res.Role);
            ChkFloat(idx, "budget", cp.GetProperty("budget").GetDouble(), res.Budget);
            ChkFloat(idx, "gamma", cp.GetProperty("gamma").GetDouble(), res.Gamma);
            var pullsCp = cp.GetProperty("pulls");
            var shareCp = cp.GetProperty("fam_share");
            foreach (var f in PlayerGenPass3.FAMILY_ORDER)
            {
                ChkFloat(idx, $"pulls.{f}", pullsCp.GetProperty(f).GetDouble(), res.Pulls[f]);
                ChkFloat(idx, $"fam_share.{f}", shareCp.GetProperty(f).GetDouble(), res.FamShare[f]);
            }
            var spendCp = cp.GetProperty("spend");
            var capsCp = cp.GetProperty("caps");
            var latentCp = cp.GetProperty("latent");
            var currentCp = cp.GetProperty("current");
            foreach (var k in PlayerGenPass3.SPEND_SKILLS)
            {
                ChkFloat(idx, $"spend.{k}", spendCp.GetProperty(k).GetDouble(), res.Spend[k]);
                ChkFloat(idx, $"caps.{k}", capsCp.GetProperty(k).GetDouble(), res.Caps[k]);
                ChkInt(idx, $"latent.{k}", latentCp.GetProperty(k).GetInt32(), res.Latent[k]);
                ChkInt(idx, $"current.{k}", currentCp.GetProperty(k).GetInt32(), res.Current[k]);
            }
            ChkInt(idx, "latent_ft", cp.GetProperty("latent_ft").GetInt32(), res.LatentFt);
            ChkInt(idx, "current_ft", cp.GetProperty("current_ft").GetInt32(), res.CurrentFt);
            ChkFloat(idx, "arrival", cp.GetProperty("arrival").GetDouble(), res.Arrival);
            ChkFloat(idx, "e", cp.GetProperty("e").GetDouble(), res.E);
            ChkInt(idx, "runway_total", cp.GetProperty("runway_total").GetInt32(), res.RunwayTotal);
            ChkFloat(idx, "rscore", cp.GetProperty("rscore").GetDouble(), res.Rscore);
            ChkStr(idx, "rscore_which", cp.GetProperty("rscore_which").GetString(), res.RscoreWhich);
        }
        if (syntheticSeen != 1)
            throw new InvalidOperationException(
                $"gen pass3 fixture rejected: expected exactly 1 synthetic pull-floor row, found {syntheticSeen}.");

        // ── the inverse-CDF edge table: every boundary at ±1e-12 ────────────────
        if (!root.TryGetProperty("edge_table", out var edges) || edges.GetArrayLength() == 0)
            throw new InvalidOperationException("gen pass3 fixture rejected: no edge_table.");
        var edgeCount = 0;
        foreach (var rowEl in edges.EnumerateArray())
        {
            edgeCount++;
            var u = rowEl.GetProperty("u").GetDouble();
            var expected = rowEl.GetProperty("expected_height").GetInt32();
            var got = PlayerGenPass3.HeightFromU(u);
            checks++;
            if (got != expected)
                throw new InvalidOperationException(
                    $"GEN PASS3 EDGE-TABLE FAILURE — HeightFromU({u:R}): expected {expected}, got {got}.\n" +
                    "The inverse-CDF boundary rule is u <= cum (boundary lands in the LOWER bin). Fix the port.");
        }

        Console.WriteLine($"players replayed: {players.GetArrayLength()} (incl. the synthetic pull-floor row)   " +
                          $"edge probes: {edgeCount}   field checks: {checks}");
        Console.WriteLine($"max float deviation observed: {maxDev:E3}   (tolerance {GenPass3Tol:E0} absolute)");
        Console.WriteLine("[OK] Phase 69: every checkpoint of every fixture player reproduced from recorded draws alone.");
    }

    /// <summary>The tripwire comparator — handles every shape the Pass-3 echo carries:
    /// double, string[], Dictionary&lt;string,double&gt;, Dictionary&lt;string,string[]&gt;
    /// (FAMILIES), and Dictionary&lt;string,Dictionary&lt;string,double&gt;&gt;
    /// (ROLE_FAM_PREF / WITHIN_PREF).</summary>
    private static void CompareEchoEntry(string name, JsonElement echoed, object live, List<string> drift)
    {
        switch (live)
        {
            case double dv:
                if (echoed.ValueKind != JsonValueKind.Number || echoed.GetDouble() != dv)
                    drift.Add($"    {name,-24} fixture echo={echoed}   C#={dv:R}");
                break;
            case string[] sv:
                var seqOk = echoed.ValueKind == JsonValueKind.Array && echoed.GetArrayLength() == sv.Length;
                if (seqOk)
                    for (var i = 0; i < sv.Length; i++)
                        if (echoed[i].GetString() != sv[i]) { seqOk = false; break; }
                if (!seqOk)
                    drift.Add($"    {name,-24} fixture echo={echoed}   C#=[{string.Join(", ", sv)}]");
                break;
            case Dictionary<string, double> mv:
            {
                if (echoed.ValueKind != JsonValueKind.Object)
                {
                    drift.Add($"    {name,-24} fixture echo is not an object; C# is a map");
                    break;
                }
                var echoKeys = 0;
                foreach (var entry in echoed.EnumerateObject())
                {
                    echoKeys++;
                    if (!mv.TryGetValue(entry.Name, out var lv) || entry.Value.GetDouble() != lv)
                        drift.Add($"    {name}[{entry.Name}]  fixture echo={entry.Value}   " +
                                  $"C#={(mv.TryGetValue(entry.Name, out var got) ? got.ToString("R") : "<missing>")}");
                }
                if (echoKeys != mv.Count)
                    drift.Add($"    {name,-24} fixture echo has {echoKeys} keys, C# has {mv.Count}");
                break;
            }
            case Dictionary<string, string[]> fv:
            {
                if (echoed.ValueKind != JsonValueKind.Object)
                {
                    drift.Add($"    {name,-24} fixture echo is not an object; C# is a map of lists");
                    break;
                }
                var echoKeys = 0;
                foreach (var entry in echoed.EnumerateObject())
                {
                    echoKeys++;
                    if (!fv.TryGetValue(entry.Name, out var lv))
                    {
                        drift.Add($"    {name}[{entry.Name}]  in fixture echo, missing from C#");
                        continue;
                    }
                    var ok = entry.Value.ValueKind == JsonValueKind.Array && entry.Value.GetArrayLength() == lv.Length;
                    if (ok)
                        for (var i = 0; i < lv.Length; i++)
                            if (entry.Value[i].GetString() != lv[i]) { ok = false; break; }
                    if (!ok)
                        drift.Add($"    {name}[{entry.Name}]  fixture echo={entry.Value}   C#=[{string.Join(", ", lv)}]");
                }
                if (echoKeys != fv.Count)
                    drift.Add($"    {name,-24} fixture echo has {echoKeys} keys, C# has {fv.Count}");
                break;
            }
            case Dictionary<string, Dictionary<string, double>> nv:
            {
                if (echoed.ValueKind != JsonValueKind.Object)
                {
                    drift.Add($"    {name,-24} fixture echo is not an object; C# is a nested map");
                    break;
                }
                var echoKeys = 0;
                foreach (var entry in echoed.EnumerateObject())
                {
                    echoKeys++;
                    if (!nv.TryGetValue(entry.Name, out var inner))
                    {
                        drift.Add($"    {name}[{entry.Name}]  in fixture echo, missing from C#");
                        continue;
                    }
                    var innerKeys = 0;
                    foreach (var e2 in entry.Value.EnumerateObject())
                    {
                        innerKeys++;
                        if (!inner.TryGetValue(e2.Name, out var lv) || e2.Value.GetDouble() != lv)
                            drift.Add($"    {name}[{entry.Name}][{e2.Name}]  fixture echo={e2.Value}   " +
                                      $"C#={(inner.TryGetValue(e2.Name, out var got) ? got.ToString("R") : "<missing>")}");
                    }
                    if (innerKeys != inner.Count)
                        drift.Add($"    {name}[{entry.Name}]  fixture echo has {innerKeys} keys, C# has {inner.Count}");
                }
                if (echoKeys != nv.Count)
                    drift.Add($"    {name,-24} fixture echo has {echoKeys} keys, C# has {nv.Count}");
                break;
            }
            default:
                drift.Add($"    {name,-24} C# echo entry has unhandled type {live.GetType().Name}");
                break;
        }
    }

    // ========================================================================
    // Phase 70 — LIVE generator: sampler moments + the 46k invariant/band audit
    // ========================================================================

    private static bool Phase70GenPass3LiveCheck()
    {
        Console.WriteLine("\n--- Phase 70: Pass-3 LIVE generator — sampler moments + population audit (standalone) ---");
        try
        {
            RunGenPass3SamplerMoments();
            RunGenPass3PopulationAudit();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    // ── (0) sampler moment checks — the three live Beta pairs, run FIRST ───────
    private static void RunGenPass3SamplerMoments()
    {
        void BetaMoments(string name, double a, double b, int seed)
        {
            var wantMean = a / (a + b);
            var wantVar = a * b / ((a + b) * (a + b) * (a + b + 1.0));
            var rng = new SystemRng(seed);
            double sum = 0, sumSq = 0;
            for (var i = 0; i < GenPass3MomentN; i++)
            {
                var x = Sampling.Betavariate(rng, a, b);
                sum += x;
                sumSq += x * x;
            }
            var mean = sum / GenPass3MomentN;
            var var_ = sumSq / GenPass3MomentN - mean * mean;
            var okM = Math.Abs(mean - wantMean) <= 0.006;
            var okV = Math.Abs(var_ - wantVar) <= 0.003;
            Console.WriteLine($"  sampler Beta {name,-22} ({a:F1},{b:F1})  mean {mean,7:F4} (want {wantMean:F4} ±0.006)   " +
                              $"var {var_,8:F5} (want {wantVar:F5} ±0.003)   {(okM && okV ? "OK" : "FAIL")}");
            if (!okM || !okV)
                throw new InvalidOperationException(
                    $"SAMPLER MOMENT FAILURE — Beta {name}: a mis-parameterized draw mis-shapes every downstream " +
                    "population figure — fix the Pass-3 live draw parameters before reading any cohort band.");
        }
        BetaMoments("talent", PlayerGenPass3.TALENT_A, PlayerGenPass3.TALENT_B, 70_001);
        BetaMoments("athletic-quality", PlayerGenPass3.ATHQ_A, PlayerGenPass3.ATHQ_B, 70_002);
        BetaMoments("concentration", PlayerGenPass3.CONC_A, PlayerGenPass3.CONC_B, 70_003);
    }

    // ── (1) exact invariants + (2) ruled distributional bands ──────────────────
    private static void RunGenPass3PopulationAudit()
    {
        var coh = PlayerGenPass3Live.BuildCohort(GenPass3LiveSeed, GenPass3LiveN);
        var n = coh.Length;
        Console.WriteLine($"  canonical cohort: seed {GenPass3LiveSeed}, n={n}");

        void Exact(bool ok, string inv, string detail, string breaksIf)
        {
            Console.WriteLine($"  [EXACT {inv}] {detail}   {(ok ? "OK" : "FAIL")}");
            if (!ok)
                throw new InvalidOperationException(
                    $"EXACT INVARIANT FAILURE — [{inv}]: {detail}.\n  What a failure here means: {breaksIf}");
        }
        void Band(bool ok, string band, string detail, string breaksIf)
        {
            Console.WriteLine($"  [BAND {band}] seed {GenPass3LiveSeed} n={n}  {detail}   {(ok ? "OK" : "FAIL")}");
            if (!ok)
                throw new InvalidOperationException(
                    $"POPULATION BAND FAILURE — [{band}]: {detail}.\n  What a failure here means: {breaksIf}");
        }

        // [EXACT budget] Σspend == nominal budget per player (allocation shares sum to 1;
        // NOTE: this is spend conservation, NOT rating-gain conservation — pricing is
        // concave and caps bind, so realized rating never equals budget by design).
        var worstBudgetGap = 0.0;
        foreach (var p in coh)
        {
            var s = 0.0;
            foreach (var k in PlayerGenPass3.SPEND_SKILLS) s += p.Result.Spend[k];
            var gap = Math.Abs(s - p.Result.Budget);
            if (gap > worstBudgetGap) worstBudgetGap = gap;
        }
        Exact(worstBudgetGap < 1e-6, "budget-conservation",
            $"max |Σspend − budget| = {worstBudgetGap:E2} (< 1e-6) across all {n}",
            "the two-stage share allocation leaks or double-spends points — Sharpen or the family split is broken");

        // [EXACT caps] interior-defense and rebounding latents honor the body cap on EVERY player.
        var capViolations = 0;
        foreach (var p in coh)
            foreach (var k in new[] { "PostDefense", "RimProtection", "OffensiveRebounding", "DefensiveRebounding" })
                if (p.Result.Latent[k] > p.Result.Caps[k] + 0.51)
                    capViolations++;
        Exact(capViolations == 0, "body-caps",
            $"latent > body_cap + 0.51 on {capViolations} player-skills (must be 0)",
            "small bodies are walling off centers — BodyCap or the Price cap argument is miswired");

        // [EXACT bounds] every rating in [8,99]-legal range and current <= latent everywhere.
        var boundsBad = 0;
        foreach (var p in coh)
            foreach (var k in PlayerGenPass3.SPEND_SKILLS)
            {
                var (L, C) = (p.Result.Latent[k], p.Result.Current[k]);
                if (L < 8 || L > 99 || C < 8 || C > 99 || C > L) boundsBad++;
            }
        Exact(boundsBad == 0, "bounds",
            $"out-of-range or current>latent on {boundsBad} player-skills (must be 0)",
            "the pricing/expression chain violates the rating contract");

        // [EXACT anti-target] the flat 35-92 no-weapon-no-hole card does not exist in the
        // top 347 by budget (the S66/S68 design target: every top player has real shape).
        var byBudget = coh.OrderByDescending(p => p.Result.Budget).Take(347);
        var antiTarget = 0;
        foreach (var p in byBudget)
        {
            int min = int.MaxValue, max = int.MinValue;
            foreach (var k in PlayerGenPass3.SPEND_SKILLS)
            {
                var L = p.Result.Latent[k];
                if (L < min) min = L;
                if (L > max) max = L;
            }
            if (min >= 35 && max < 92) antiTarget++;
        }
        Exact(antiTarget == 0, "anti-target",
            $"flat no-weapon-no-hole latent cards in the top 347 by budget: {antiTarget} (must be EXACTLY 0)",
            "concentration/sharpening is too weak — the roster-filler shape reached the top of the class");

        // [EXACT label-flip] Rscore is label-free BY CONSTRUCTION (ComputeRscoreParts takes
        // only current/ath/height — role and plane are not parameters); executed numerically
        // on every player anyway so a future signature change that sneaks a label in fails here.
        var flipMoved = 0;
        foreach (var p in coh)
        {
            var (r2, _, _) = PlayerGenPass3.ComputeRscoreParts(p.Result.Current, p.Result.Ath, p.Result.Height);
            if (r2 != p.Result.Rscore) flipMoved++;
        }
        Exact(flipMoved == 0, "label-flip",
            $"Rscore recomputed independent of role/plane labels moved on {flipMoved} players (must be EXACTLY 0)",
            "Rscore is reading a stored label — the D3 label-freedom ruling is violated");

        // ── distributional bands (each: seed, n, observed, band) ────────────────
        // [BAND height] per-bin cohort share vs the preserved marginal, ≤ 0.6pp per bin.
        var bins = new (string Name, int Lo, int Hi)[]
        {
            ("5'8-5'9", 40, 44), ("5'10-5'11", 45, 50), ("6'0-6'1", 51, 56),
            ("6'2-6'5", 57, 65), ("6'6-6'7", 66, 70), ("6'8-6'9", 71, 79),
            ("6'10-7'0", 80, 86), ("7'1-7'2", 87, 92), ("7'3+", 93, 99),
        };
        var margTotal = 0.0;
        for (var h = 40; h <= 99; h++) margTotal += PlayerGenPass3.HEIGHT_MARGINAL[h.ToString()];
        var worstBin = 0.0;
        var worstBinName = "";
        foreach (var (name, lo, hi) in bins)
        {
            var want = 0.0;
            for (var h = lo; h <= hi; h++) want += PlayerGenPass3.HEIGHT_MARGINAL[h.ToString()];
            want /= margTotal;
            var got = coh.Count(p => p.Result.Height >= lo && p.Result.Height <= hi) / (double)n;
            var dev = Math.Abs(got - want);
            if (dev > worstBin) { worstBin = dev; worstBinName = name; }
        }
        Band(worstBin <= 0.006, "height-marginal",
            $"worst per-bin deviation {worstBin * 100:F2}pp at '{worstBinName}' (band ≤ 0.60pp per bin)",
            "the D1 preservation constraint is broken — the inverse-CDF draw is not reproducing the fitted marginal");

        // [BAND line-17] share of the cohort clearing the standing recruiting line.
        var line17 = coh.Count(p => p.Result.Rscore >= PlayerGenPass3.R_LINE) / (double)n;
        Band(Math.Abs(line17 - 0.795) <= 0.015, "line-17",
            $"Rscore ≥ 17 share {line17 * 100:F1}% (band 79.5% ± 1.5pp; oracle canonical 79.5%)",
            "the Rscore pathway magnitudes moved — the recruiting funnel feeds a different-sized pool");

        // [BAND asymmetry] the S67 ruled asymmetry: undersized post identities are uncommon,
        // oversized perimeter identities are rare. Oracle canonical: 973 vs 51.
        var shortPost = coh.Count(p => p.Result.Height <= 56 && p.Result.Role == "PostScorer");
        var tallPerim = coh.Count(p => p.Result.Height >= 80 &&
                                       (p.Result.Role == "Creator" || p.Result.Role == "Shooter"));
        Band(Math.Abs(shortPost - 973) <= 973 * 0.25, "asymmetry-short-post",
            $"sub-6'2\" PostScorers {shortPost} (band 973 ± 25% = [730, 1216])",
            "the POST_DECAY side of the asymmetric role slide is miswired");
        Band(Math.Abs(tallPerim - 51) <= Math.Max(1.0, 51 * 0.25), "asymmetry-tall-perim",
            $"6'10\"+ Creators/Shooters {tallPerim} (band 51 ± 25% = [38, 64])",
            "the PERIM_DECAY side of the asymmetric role slide is miswired");

        // [BAND ceiling] share of the cohort holding any latent ≥ 95 (oracle observed 23.9%).
        var ceiling = coh.Count(p =>
        {
            foreach (var k in PlayerGenPass3.SPEND_SKILLS)
                if (p.Result.Latent[k] >= 95)
                    return true;
            return false;
        }) / (double)n;
        Band(Math.Abs(ceiling - 0.23) <= 0.03, "ceiling-pressure",
            $"any-latent-≥95 share {ceiling * 100:F1}% (band 23% ± 3pp; oracle canonical 23.9%)",
            "the budget/pricing top end moved — elite ceilings are too common or too rare");

        // [BAND independence] concentration ⊥ talent (drawn independently by design).
        double sumQ = 0, sumC = 0, sumQQ = 0, sumCC = 0, sumQC = 0;
        foreach (var p in coh)
        {
            var (q, c) = (p.Draws.Q, p.Draws.C);
            sumQ += q; sumC += c; sumQQ += q * q; sumCC += c * c; sumQC += q * c;
        }
        var mq = sumQ / n;
        var mc = sumC / n;
        var corr = (sumQC / n - mq * mc) / Math.Sqrt((sumQQ / n - mq * mq) * (sumCC / n - mc * mc));
        Band(Math.Abs(corr) < 0.02, "conc-independence",
            $"corr(talent, concentration) = {corr:F4} (band |corr| < 0.02)",
            "the two dice share state — concentration is leaking talent information");

        Console.WriteLine("[OK] Phase 70: live drawing proven — moments, exact invariants, and every ruled band green.");
    }
}
