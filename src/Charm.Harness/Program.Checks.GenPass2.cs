using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 59 — Pass-2 generation math, S42.2 fixture replay parity (C# port Phase 1).
//
// The executable spec is tools/gen_pass2_skillfirst_oracle.py (LOCKED SPEC, S42.1);
// the committed replay fixture tools/gen_pass2_replay_fixture_s42_2.json (schema
// s42.2-v1, seed 20260706, 306 players: the first 300 of the canonical 46k cohort
// plus six targeted edge-case rows) records every raw draw and every checkpoint.
// This gate does exactly what tools/gen_pass2_replay_check.py proved sufficient:
// from the RECORDED DRAWS + FROZEN CONSTANTS ALONE, PlayerGenPass2.BuildFromDraws
// rebuilds every checkpoint, card value, latent/current/runway rating, and
// recruiting field for every player — integers EXACT, floats within the
// fixture-declared ABSOLUTE 1e-9 (the S43 ruling: same tolerance as the fixture
// header, no contract drift; the allowance exists for exp/tanh, which Python's
// libm and .NET may compute ~1 ULP apart — every other operation is plain
// IEEE-754 double arithmetic and lands bit-identical given the same order).
//
// If the C# and the oracle ever disagree, the oracle wins — a failure here is a
// PORT BUG, never a tolerance to widen and never a fixture to regenerate casually.
//
// Contract validation runs LOUDLY FIRST (a stale/malformed fixture is rejected,
// not silently half-tested): schema present, draw_order 40 slots exact, all five
// key_orders exact, the 57-entry constants echo vs the C# transcriptions (the
// S42.2 tripwire — itemized drift, replay not run), players non-empty.
//
// Seed-independent by construction: it reads the fixture; it generates nothing.
// Phase 1 touches NO live generation path — this gate and the transform layer are
// the session's whole surface.
// ============================================================================
internal static partial class Program
{
    private const string GenPass2FixtureFile = "gen_pass2_replay_fixture_s42_2.json";
    private const double GenPass2Tol = 1e-9;   // ABSOLUTE — the fixture header's declared convention

    // The fixture's draw_order contract: the 40 RNG slots per player, in stream order.
    private static readonly string[] GenPass2DrawOrder =
    {
        "o", "q", "a", "s",
        "height_branch_selector_raw", "height_noise_raw",
        "skill_noise.Close", "skill_noise.Mid", "skill_noise.Outside", "skill_noise.Finishing",
        "skill_noise.FoulDrawing", "skill_noise.BallHandling", "skill_noise.Passing",
        "skill_noise.Playmaking", "skill_noise.SelfCreation", "skill_noise.PostMoves",
        "skill_noise.OffBallMovement", "skill_noise.Screening", "skill_noise.PerimeterDefense",
        "skill_noise.PostDefense", "skill_noise.RimProtection", "skill_noise.Steals",
        "skill_noise.HelpDefense", "skill_noise.OffBallDefense", "skill_noise.BasketballIQ",
        "skill_noise.Discipline",
        "wingspan_noise",
        "ath_noise.Strength", "ath_noise.Speed", "ath_noise.Quickness", "ath_noise.FirstStep",
        "ath_noise.Vertical", "ath_noise.Endurance", "ath_noise.Hustle",
        "weight_noise", "oreb_noise", "dreb_noise",
        "arrival_draw_raw", "ft_idio", "age_noise_raw",
    };

    private static bool Phase59GenPass2ReplayParityCheck()
    {
        Console.WriteLine("\n--- Phase 59: Pass-2 generation math — fixture replay parity (C# port Phase 1) ---");
        try
        {
            RunGenPass2ReplayParity();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    /// <summary>The gate itself — throws on the first divergence (the tendency-parity
    /// precedent). Also runs at the start of RunGen beside RunTendencyGoldenParity.</summary>
    private static void RunGenPass2ReplayParity()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tools", GenPass2FixtureFile);
        if (!File.Exists(path))
            throw new InvalidOperationException($"gen pass2 replay fixture not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // ── fixture contract, validated loudly before any replay ────────────────
        if (!root.TryGetProperty("schema", out var schema))
            throw new InvalidOperationException("gen pass2 fixture rejected: no schema block.");

        if (!schema.TryGetProperty("draw_order", out var drawOrder)
            || drawOrder.GetArrayLength() != GenPass2DrawOrder.Length)
            throw new InvalidOperationException(
                "gen pass2 fixture rejected: draw_order missing or not exactly 40 slots.");
        for (var i = 0; i < GenPass2DrawOrder.Length; i++)
            if (drawOrder[i].GetString() != GenPass2DrawOrder[i])
                throw new InvalidOperationException(
                    $"gen pass2 fixture rejected: draw_order[{i}] is '{drawOrder[i].GetString()}', " +
                    $"expected '{GenPass2DrawOrder[i]}'. The fixture does not match the locked contract.");

        if (!schema.TryGetProperty("key_orders", out var keyOrders))
            throw new InvalidOperationException("gen pass2 fixture rejected: no key_orders block.");
        AssertKeyOrder(keyOrders, "DRAWN_SKILLS", PlayerGenPass2.DRAWN_SKILLS);
        AssertKeyOrder(keyOrders, "ATH_KEYS", PlayerGenPass2.ATH_KEYS);
        AssertKeyOrder(keyOrders, "SKILL_KEYS", PlayerGenPass2.SKILL_KEYS);
        AssertKeyOrder(keyOrders, "SIZE_KEYS", PlayerGenPass2.SIZE_KEYS);
        AssertKeyOrder(keyOrders, "ALL_KEYS", PlayerGenPass2.ALL_KEYS);

        // ── the constants tripwire (S42.2 guardrail): itemized drift, replay NOT run ──
        if (!schema.TryGetProperty("constants", out var constantsEcho))
            throw new InvalidOperationException("gen pass2 fixture rejected: no constants echo.");
        var csharp = PlayerGenPass2.ConstantsEcho();
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
            switch (live)
            {
                case double dv:
                    if (prop.Value.ValueKind != JsonValueKind.Number || prop.Value.GetDouble() != dv)
                        drift.Add($"    {prop.Name,-24} fixture echo={prop.Value}   C#={dv:R}");
                    break;
                case string[] sv:
                    var seqOk = prop.Value.ValueKind == JsonValueKind.Array
                                && prop.Value.GetArrayLength() == sv.Length;
                    if (seqOk)
                        for (var i = 0; i < sv.Length; i++)
                            if (prop.Value[i].GetString() != sv[i])
                            {
                                seqOk = false;
                                break;
                            }
                    if (!seqOk)
                        drift.Add($"    {prop.Name,-24} fixture echo={prop.Value}   C#=[{string.Join(", ", sv)}]");
                    break;
                case Dictionary<string, double> mv:
                    if (prop.Value.ValueKind != JsonValueKind.Object)
                    {
                        drift.Add($"    {prop.Name,-24} fixture echo is not an object; C# is a map");
                        break;
                    }
                    var echoKeys = 0;
                    foreach (var entry in prop.Value.EnumerateObject())
                    {
                        echoKeys++;
                        if (!mv.TryGetValue(entry.Name, out var lv) || entry.Value.GetDouble() != lv)
                            drift.Add($"    {prop.Name}[{entry.Name}]  fixture echo={entry.Value}   " +
                                      $"C#={(mv.TryGetValue(entry.Name, out var got) ? got.ToString("R") : "<missing>")}");
                    }
                    if (echoKeys != mv.Count)
                        drift.Add($"    {prop.Name,-24} fixture echo has {echoKeys} keys, C# has {mv.Count}");
                    break;
            }
        }
        if (echoCount != csharp.Count)
            drift.Add($"    <count>                 fixture echo has {echoCount} constants, C# transcribes {csharp.Count}");
        if (drift.Count > 0)
            throw new InvalidOperationException(
                $"GEN PASS2 CONSTANTS TRIPWIRE: {drift.Count} mismatch(es) between the fixture echo and the " +
                "C# transcription — replay NOT run. The oracle source is canonical; fix PlayerGenPass2:\n" +
                string.Join("\n", drift));
        Console.WriteLine($"constants echo vs C# transforms: {echoCount}/{echoCount} match — tripwire clear");

        if (!root.TryGetProperty("players", out var players) || players.GetArrayLength() == 0)
            throw new InvalidOperationException("gen pass2 fixture rejected: no players.");

        // ── replay every row; throw on the FIRST divergence (the oracle wins) ───
        var checks = 0;
        var maxDev = 0.0;

        void ChkInt(int idx, string field, long expected, long got)
        {
            checks++;
            if (expected != got)
                throw new InvalidOperationException(
                    $"GEN PASS2 REPLAY FAILURE — player {idx}, field {field}:\n" +
                    $"  expected  {expected}\n  actual    {got}\n" +
                    "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
        }
        void ChkFloat(int idx, string field, double expected, double got)
        {
            checks++;
            var d = Math.Abs(expected - got);
            if (d > maxDev) maxDev = d;
            if (d > GenPass2Tol)
                throw new InvalidOperationException(
                    $"GEN PASS2 REPLAY FAILURE — player {idx}, field {field}:\n" +
                    $"  expected  {expected:R}\n  actual    {got:R}\n  |diff|    {d:E3} (tolerance {GenPass2Tol:E0} absolute)\n" +
                    "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
        }
        void ChkStr(int idx, string field, string? expected, string? got)
        {
            checks++;
            if (!string.Equals(expected, got, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"GEN PASS2 REPLAY FAILURE — player {idx}, field {field}:\n" +
                    $"  expected  '{expected}'\n  actual    '{got}'\n" +
                    "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
        }

        foreach (var row in players.EnumerateArray())
        {
            var idx = row.GetProperty("index").GetInt32();
            var dEl = row.GetProperty("draws");
            var cp = row.GetProperty("checkpoints");
            var cardFx = row.GetProperty("card");
            var recFx = row.GetProperty("recruiting");

            // draws -> the transform layer's input shape
            var draws = new Pass2Draws
            {
                O = dEl.GetProperty("o").GetDouble(),
                Q = dEl.GetProperty("q").GetDouble(),
                A = dEl.GetProperty("a").GetDouble(),
                S = dEl.GetProperty("s").GetDouble(),
                HeightBranchSelectorRaw = dEl.GetProperty("height_branch_selector_raw").GetDouble(),
                HeightNoiseRaw = dEl.GetProperty("height_noise_raw").GetDouble(),
                WingspanNoise = dEl.GetProperty("wingspan_noise").GetDouble(),
                WeightNoise = dEl.GetProperty("weight_noise").GetDouble(),
                OrebNoise = dEl.GetProperty("oreb_noise").GetDouble(),
                DrebNoise = dEl.GetProperty("dreb_noise").GetDouble(),
                ArrivalDrawRaw = dEl.GetProperty("arrival_draw_raw").GetDouble(),
                FtIdio = dEl.GetProperty("ft_idio").GetDouble(),
                AgeNoiseRaw = dEl.GetProperty("age_noise_raw").GetDouble(),
            };
            var skillNoiseEl = dEl.GetProperty("skill_noise");
            foreach (var k in PlayerGenPass2.DRAWN_SKILLS)
                draws.SkillNoise[k] = skillNoiseEl.GetProperty(k).GetDouble();
            var athNoiseEl = dEl.GetProperty("ath_noise");
            foreach (var k in PlayerGenPass2.ATH_KEYS)
                draws.AthNoise[k] = athNoiseEl.GetProperty(k).GetDouble();

            var res = PlayerGenPass2.BuildFromDraws(draws);

            // ── asserts, in the reference reader's order ─────────────────────────
            ChkFloat(idx, "oaxis", cp.GetProperty("oaxis").GetDouble(), res.Oaxis);
            ChkFloat(idx, "oh", cp.GetProperty("oh").GetDouble(), res.Oh);
            ChkFloat(idx, "mu", cp.GetProperty("mu").GetDouble(), res.Mu);
            ChkFloat(idx, "sigma_up", cp.GetProperty("sigma_up").GetDouble(), res.SigmaUp);
            // recorded branch vs the branch the selector implies (consistency)
            ChkStr(idx, "height_branch(consistency)", dEl.GetProperty("height_branch").GetString(), res.HeightBranch);
            ChkFloat(idx, "h_raw", cp.GetProperty("h_raw").GetDouble(), res.HRaw);
            ChkInt(idx, "Height", cp.GetProperty("Height").GetInt32(), res.Height);

            var baseFx = cp.GetProperty("base");
            foreach (var k in PlayerGenPass2.DRAWN_SKILLS)
                ChkFloat(idx, $"base.{k}", baseFx.GetProperty(k).GetDouble(), res.Base[k]);

            // eligible: sequence-equal (one check, matching the reader) + the non-empty invariant
            checks++;
            var eligibleEl = cp.GetProperty("eligible");
            var eligibleOk = eligibleEl.GetArrayLength() == res.Eligible.Count;
            if (eligibleOk)
                for (var i = 0; i < res.Eligible.Count; i++)
                    if (!string.Equals(eligibleEl[i].GetString(), res.Eligible[i], StringComparison.Ordinal))
                    {
                        eligibleOk = false;
                        break;
                    }
            if (!eligibleOk)
            {
                var expectedList = new List<string>();
                foreach (var x in eligibleEl.EnumerateArray()) expectedList.Add(x.GetString() ?? "<null>");
                throw new InvalidOperationException(
                    $"GEN PASS2 REPLAY FAILURE — player {idx}, field eligible:\n" +
                    $"  expected  [{string.Join(", ", expectedList)}]\n" +
                    $"  actual    [{string.Join(", ", res.Eligible)}]\n" +
                    "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
            }
            ChkInt(idx, "eligible(non-empty invariant)", 1, res.Eligible.Count > 0 ? 1 : 0);

            ChkStr(idx, "weapon_raw", cp.GetProperty("weapon_raw").GetString(), res.WeaponRaw);
            ChkStr(idx, "weapon", cp.GetProperty("weapon").GetString(), res.Weapon);

            ChkInt(idx, "Wingspan", cp.GetProperty("Wingspan").GetInt32(), res.Wingspan);
            ChkFloat(idx, "ath_center", cp.GetProperty("ath_center").GetDouble(), res.AthCenter);
            var athRawFx = cp.GetProperty("ath_raw");
            foreach (var k in PlayerGenPass2.ATH_KEYS)
            {
                ChkFloat(idx, $"ath_raw.{k}", athRawFx.GetProperty(k).GetDouble(), res.AthRaw[k]);
                ChkInt(idx, $"ath.{k}", cardFx.GetProperty(k).GetInt32(), res.Ath[k]);
            }
            ChkInt(idx, "Weight", cp.GetProperty("Weight").GetInt32(), res.Weight);
            ChkInt(idx, "OffensiveRebounding", cp.GetProperty("OffensiveRebounding").GetInt32(), res.OffensiveRebounding);
            ChkInt(idx, "DefensiveRebounding", cp.GetProperty("DefensiveRebounding").GetInt32(), res.DefensiveRebounding);

            ChkFloat(idx, "arr_mean", cp.GetProperty("arr_mean").GetDouble(), res.ArrMean);
            ChkFloat(idx, "arrival", cp.GetProperty("arrival").GetDouble(), res.Arrival);
            ChkFloat(idx, "e", cp.GetProperty("e").GetDouble(), res.E);

            var latentFx = cp.GetProperty("latent");
            var currentFx = cp.GetProperty("current");
            var runwayFx = cp.GetProperty("runway");
            foreach (var k in PlayerGenPass2.SKILL_KEYS)
            {
                ChkInt(idx, $"latent.{k}", latentFx.GetProperty(k).GetInt32(), res.Latent[k]);
                ChkInt(idx, $"current.{k}", currentFx.GetProperty(k).GetInt32(), res.Current[k]);
                ChkInt(idx, $"runway.{k}", runwayFx.GetProperty(k).GetInt32(), res.Runway[k]);
            }
            ChkInt(idx, "runway_total", cp.GetProperty("runway_total").GetInt32(), res.RunwayTotal);

            foreach (var k in PlayerGenPass2.ALL_KEYS)
                ChkInt(idx, $"card.{k}", cardFx.GetProperty(k).GetInt32(), res.Card[k]);

            // age/class — PLACEHOLDER-OUTPUT asserts (S42.1 ruling: values checked; the
            // formula is NOT ported as spec — arrival is the ruled mechanism).
            ChkInt(idx, "age(placeholder)", cp.GetProperty("age").GetInt32(), res.Age);
            ChkStr(idx, "cls(placeholder)", cp.GetProperty("cls").GetString(), res.Cls);

            // recruiting line, recomputed end-to-end from the REPLAYED card
            ChkFloat(idx, "rscore", recFx.GetProperty("rscore").GetDouble(), res.Rscore);
            foreach (var part in recFx.GetProperty("rscore_parts").EnumerateObject())
            {
                if (part.Value.ValueKind == JsonValueKind.String)
                    ChkStr(idx, $"rscore_parts.{part.Name}", part.Value.GetString(), res.RscoreWhich);
                else
                    ChkFloat(idx, $"rscore_parts.{part.Name}", part.Value.GetDouble(), res.RscoreParts[part.Name]);
            }
        }

        Console.WriteLine(
            $"gen pass2 replay parity: {players.GetArrayLength()} players, {checks} field checks, 0 failures; " +
            $"max float deviation {maxDev:E3} (tolerance {GenPass2Tol:E0} absolute). " +
            "(oracle: tools/gen_pass2_skillfirst_oracle.py, LOCKED SPEC S42.1; fixture: s42.2-v1, seed 20260706)");
    }

    private static void AssertKeyOrder(JsonElement keyOrders, string name, string[] expected)
    {
        if (!keyOrders.TryGetProperty(name, out var el) || el.GetArrayLength() != expected.Length)
            throw new InvalidOperationException(
                $"gen pass2 fixture rejected: key_orders.{name} missing or wrong length " +
                $"(expected {expected.Length}).");
        for (var i = 0; i < expected.Length; i++)
            if (el[i].GetString() != expected[i])
                throw new InvalidOperationException(
                    $"gen pass2 fixture rejected: key_orders.{name}[{i}] is '{el[i].GetString()}', " +
                    $"expected '{expected[i]}'. The fixture does not match the locked contract.");
    }
}
