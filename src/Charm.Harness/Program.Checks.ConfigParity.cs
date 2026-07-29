using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 71 (Session 74) — CONFIG KEY-NAME PARITY.
//
//  What this phase proves, stated narrowly and honestly:
//    (1) bidirectional KEY-NAME parity between each JSON surface in config.json
//        and its config class — an orphan key (silently ignored today) or an
//        absent key (silently falls back to the compiled default today) fails loud;
//    (2) TOKEN-KIND compatibility for keys present on both sides;
//    (3) KEY→PROPERTY BINDING for RollEConfig, the one hand-written loader.
//
//  What it does NOT prove: value correctness, range sanity, or that the sectioned
//  deserializer honours intent beyond name matching. Those belong to each Load's
//  own invariant guards, which are untouched here.
//
//  THE RULING (Emmett, 2026-07-25). A missing key stays QUIET AT RUNTIME — the
//  compiled default applies and the game boots — and becomes LOUD AT TEST TIME,
//  here. Refuse-to-boot was considered and rejected: it would force every future
//  dial into two places forever.
//
//  ── Three loader shapes, not one (the trap this phase is built around) ──────
//  A check written against the common shape reports drift that does not exist:
//    • Sectioned (18 parity-checked + Rosters): JsonDocument.Parse →
//      GetProperty("RollX") → Deserialize<T>.
//    • Root-flat (RollAConfig): ten scalars live at the ROOT, no section of its own.
//    • Manual (RollEConfig): no Deserialize at all — nineteen explicit
//      e.GetProperty("Name").GetDouble() assignments.
//  The registry below records the shape per surface; the divergence is DELIBERATE
//  and left un-normalized (normalizing three loaders is its own session).
//
//  ── Why RollE needs a behavioural test and nobody else does ────────────────
//  Name parity does NOT prove binding. A reflected name comparison is perfectly
//  green even if the loader reads BaseSlot1 = e.GetProperty("BaseSlot2") — crossed
//  wires, both names present on both sides. Sectioned loaders bind by name through
//  the serializer and CANNOT cross wires this way. RollE's nineteen hand-written
//  assignments can, so RollE gets a real load-and-compare (arm 5).
//
//  ── Two proof layers (an unexercised failure path may simply not work) ─────
//  Measured on the S74 tree: zero orphan keys exist anywhere, so the orphan arm
//  cannot go red against production. Layer 1 therefore plants drift into in-memory
//  fixtures and asserts all four arms fire; layer 2 prints a coverage summary
//  proving the phase actually looked at the real surfaces.
// ============================================================================

internal static partial class Program
{
    private enum LoaderShape { Sectioned, RootFlat, Manual }

    /// <summary>One row of the explicit loader-contract registry. Reflection cannot
    /// reliably infer that "RollB" maps to RollBConfig, that root scalars belong only
    /// to RollAConfig, or that Rosters is excluded — so the mapping is declared, and
    /// the registry's COMPLETENESS is asserted instead (arm 1).</summary>
    private sealed record ConfigContract(
        string Surface,
        Type ConfigType,
        LoaderShape Shape,
        bool ParityChecked = true,
        string? ExclusionReason = null,
        bool ContainersLegal = false);

    /// <summary>Sentinel surface name for RollA's ten root-level scalars.</summary>
    private const string RootSurface = "(root scalars)";

    private static readonly ConfigContract[] LoaderContracts =
    {
        new(RootSurface,          typeof(RollAConfig),              LoaderShape.RootFlat),
        new("RollE",              typeof(RollEConfig),              LoaderShape.Manual),
        new("RollB",              typeof(RollBConfig),              LoaderShape.Sectioned),
        new("RollC",              typeof(RollCConfig),              LoaderShape.Sectioned),
        new("RollD",              typeof(RollDConfig),              LoaderShape.Sectioned),
        new("RollF",              typeof(RollFConfig),              LoaderShape.Sectioned),
        new("RollG",              typeof(RollGConfig),              LoaderShape.Sectioned),
        new("RollH",              typeof(RollHConfig),              LoaderShape.Sectioned),
        new("RollI",              typeof(RollIConfig),              LoaderShape.Sectioned),
        new("RollJ",              typeof(RollJConfig),              LoaderShape.Sectioned),
        new("RollK",              typeof(RollKConfig),              LoaderShape.Sectioned),
        new("RollL",              typeof(RollLConfig),              LoaderShape.Sectioned),
        new("RollM",              typeof(RollMConfig),              LoaderShape.Sectioned),
        new("Attention",          typeof(AttentionConfig),          LoaderShape.Sectioned),
        new("Matchup",            typeof(MatchupConfig),            LoaderShape.Sectioned),
        new("Governor",           typeof(GovernorConfig),           LoaderShape.Sectioned),
        new("OffensiveFoulFlavor",typeof(RollOffensiveFoulConfig),  LoaderShape.Sectioned),
        new("Clock",              typeof(RollClockConfig),          LoaderShape.Sectioned),
        new("EndOfHalf",          typeof(EndOfHalfConfig),          LoaderShape.Sectioned),
        new("Fatigue",            typeof(FatigueConfig),            LoaderShape.Sectioned),
        // EXCLUDED. RosterConfig itself declares only Home/Away — against it the section
        // is a clean 2 = 2. But the section's real content is ARRAYS OF PLAYER OBJECTS
        // (the forty rating properties live on the separate PlayerConfig class), and
        // key-name parity has no way to inspect inside them. Excluded by name, with the
        // reason recorded, rather than silently skipped.
        new("Rosters",            typeof(RosterConfig),             LoaderShape.Sectioned,
            ParityChecked: false,
            ExclusionReason: "nested player-object arrays — parity cannot inspect inside them",
            ContainersLegal: true),
    };

    /// <summary>The twelve Matchup dials made explicit in S74 (seven Height terms from
    /// S55, five ReachIn terms from S62). Arm 6 proves all twelve are present in
    /// config.json, and proves the value of each against the right yardstick — the
    /// compiled default for the eight still sitting on it, the RULED value for the four
    /// S83 moved deliberately.</summary>
    private static readonly string[] S74NewlyExplicitMatchupKeys =
    {
        "HeightMaxBonus", "HeightReferenceScale", "HeightWeightRim", "HeightWeightShort",
        "HeightWeightMid", "HeightWeightLong", "HeightWeightThree",
        "ReachInDiscSpan", "ReachInAthSpan", "ReachInPerimSpan", "ReachInLuckFloor",
        "ReachInPostnessScale",
    };

    /// <summary>The four dials S83 tuned away from their compiled defaults, with the value
    /// Emmett ruled. Arm 6 pins these to the RULING instead of to the class default.
    ///
    /// <para><b>Why not simply update the class defaults to match.</b> The compiled defaults
    /// are the Session 55 signed-off numbers and stay that way: config.json is where a
    /// ruling lives, and a default quietly chasing every tuning pass would erase the
    /// distinction between "never touched" and "deliberately moved."</para>
    ///
    /// <para><b>Why not simply drop these four from the arm.</b> The arm exists as a
    /// programmatic typo-catcher, because the season page is not a sufficient one — a
    /// mistyped weight on a rare event might not move a seeded season at all. Dropping the
    /// four would remove that guard from precisely the numbers being changed. Pinning them
    /// keeps the guard and records the ruling in the same line.</para>
    ///
    /// <para>HeightWeightRim (1.00) and HeightWeightThree (0.00) were REVIEWED by S83 and
    /// deliberately left alone, so they stay on the compiled-default side.</para></summary>
    private static readonly (string Key, double Ruled)[] S83RuledMatchupValues =
    {
        ("HeightMaxBonus",    110.0),
        ("HeightWeightShort", 0.109090909090909),
        ("HeightWeightMid",   0.040909090909091),
        ("HeightWeightLong",  0.006818181818182),
    };

    // ── The configuration-property contract ──────────────────────────────────
    //  Defined by the SERIALIZATION contract, not by today's shortcut: a public
    //  instance property, non-indexed, with a public set OR init accessor, not
    //  [JsonIgnore], its effective name taken from [JsonPropertyName] when present.
    //
    //  The init clause is load-bearing RIGHT NOW, not hypothetically: RollEConfig is
    //  the only config class declared with { get; init; }, and a rule written as
    //  "has a public set" would see ZERO properties there and pass RollE vacuously.
    //  System.Text.Json binds init-only properties, so the contract must too.
    //
    //  The getter-only clause is also live today: MatchupConfig.SlotWeights is a
    //  computed convenience array with no setter. It is derived, not a dial, and the
    //  serializer cannot write it either — so it is correctly outside the set.

    private static bool IsConfigurationProperty(PropertyInfo p)
        => p.GetIndexParameters().Length == 0
        && p.SetMethod is not null
        && p.SetMethod.IsPublic
        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null;

    private static string EffectivePropertyName(PropertyInfo p)
        => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name;

    private static (string Name, Type Type)[] ConfigurationProperties(Type t)
        => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsConfigurationProperty)
            .Select(p => (EffectivePropertyName(p), p.PropertyType))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToArray();

    // ── Token-kind compatibility (bounded deliberately) ──────────────────────
    //  Asserts only that the JSON token kind CAN bind to the property type. No range,
    //  precision, or overflow checking — those belong to each Load's existing guards.
    //  The awkward cases are defined up front rather than discovered later: today every
    //  config property is a plain double/int/bool, but encoding only today's shortcut
    //  would false-fail the first legitimately-typed property added later.

    private static readonly HashSet<Type> NumericTypes = new()
    {
        typeof(double), typeof(float), typeof(decimal),
        typeof(int), typeof(long), typeof(short), typeof(sbyte),
        typeof(uint), typeof(ulong), typeof(ushort), typeof(byte),
    };

    private static bool TokenKindCompatible(JsonValueKind kind, Type type, bool containersLegal)
    {
        // Nullable<T> accepts its underlying kind OR null.
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            if (kind == JsonValueKind.Null) return true;
            type = underlying;
        }

        if (type == typeof(JsonElement)) return true;              // accepts any kind by definition
        if (type.IsEnum) return kind is JsonValueKind.Number or JsonValueKind.String;
        if (type == typeof(bool)) return kind is JsonValueKind.True or JsonValueKind.False;
        if (type == typeof(string)) return kind is JsonValueKind.String or JsonValueKind.Null;
        if (NumericTypes.Contains(type)) return kind == JsonValueKind.Number;

        // Arrays / objects are legal only where the registry entry says so.
        return containersLegal && kind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Null;
    }

    // ── The comparison algorithm, factored out so the self-tests can drive it ──

    private sealed record ParityFindings(
        List<string> Orphans,     // key present in JSON, no such property
        List<string> Absent,      // property declared, no such key
        List<string> CaseOnly,    // names match case-INSENSITIVELY only
        List<string> TokenKind);  // present on both sides, kind cannot bind

    /// <summary>Compare one JSON surface against one class's configuration-property set.
    /// Exact-case matching is DELIBERATE and stricter than production — every loader sets
    /// PropertyNameCaseInsensitive = true, so a case-drifted key would bind at runtime by
    /// accident. Catching that is the point. A case-only mismatch is reported as exactly
    /// that, not double-counted as an orphan plus an absence.</summary>
    private static ParityFindings CompareSurface(
        IReadOnlyList<(string Name, JsonValueKind Kind)> jsonKeys,
        IReadOnlyList<(string Name, Type Type)> classProps,
        bool containersLegal)
    {
        var jsonByExact  = jsonKeys.ToDictionary(k => k.Name, k => k.Kind, StringComparer.Ordinal);
        var propsByExact = classProps.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        var propsByFold  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in classProps) propsByFold.TryAdd(p.Name, p.Name);
        var jsonByFold = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in jsonKeys) jsonByFold.TryAdd(k.Name, k.Name);

        var orphans = new List<string>();
        var absent = new List<string>();
        var caseOnly = new List<string>();
        var tokenKind = new List<string>();

        foreach (var (name, kind) in jsonKeys)
        {
            if (propsByExact.TryGetValue(name, out var propType))
            {
                if (!TokenKindCompatible(kind, propType, containersLegal))
                    tokenKind.Add($"{name} (json {kind} vs {propType.Name})");
            }
            else if (propsByFold.TryGetValue(name, out var propName))
            {
                caseOnly.Add($"{name} (class declares {propName})");
            }
            else
            {
                orphans.Add(name);
            }
        }

        foreach (var (name, _) in classProps)
            if (!jsonByExact.ContainsKey(name) && !jsonByFold.ContainsKey(name))
                absent.Add(name);

        orphans.Sort(StringComparer.Ordinal);
        absent.Sort(StringComparer.Ordinal);
        caseOnly.Sort(StringComparer.Ordinal);
        tokenKind.Sort(StringComparer.Ordinal);
        return new ParityFindings(orphans, absent, caseOnly, tokenKind);
    }

    private static (string Name, JsonValueKind Kind)[] SurfaceKeys(JsonElement obj)
        => obj.EnumerateObject()
              .Select(p => (p.Name, p.Value.ValueKind))
              .OrderBy(x => x.Name, StringComparer.Ordinal)
              .ToArray();

    // ========================================================================
    //  The phase
    // ========================================================================

    private static bool Phase71ConfigKeyNameParityCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 71: config key-name parity (registry completeness + bidirectional names + token kind + RollE binding + planted-drift self-tests) ---");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = doc.RootElement;

        var rootObjects = root.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Object)
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var rootScalars = root.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Object)
            .Select(p => (p.Name, p.Value.ValueKind))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();

        // ── Arm 1: the registry is complete in both directions ───────────────
        //  Hardcoding the mapping is safe only BECAUSE this arm exists. A new config
        //  class or a new section that nobody registered fails the phase.
        //
        //  The discovery universe is defined NARROWLY (an unconstrained "type name ends
        //  in Config" scan would sweep in helper option objects and nested value types —
        //  PlayerConfig among them — and turn an honest completeness assertion into
        //  noise). The universe is the classes the production loaders actually construct:
        //  those exposing `public static T Load(string path)` returning their own type.
        {
            var discovered = typeof(MatchupConfig).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t =>
                {
                    var m = t.GetMethod("Load", BindingFlags.Public | BindingFlags.Static,
                                        binder: null, types: new[] { typeof(string) }, modifiers: null);
                    return m is not null && m.ReturnType == t;
                })
                .ToArray();

            var registered = LoaderContracts.Select(c => c.ConfigType).ToArray();
            var unregistered = discovered.Except(registered).Select(t => t.Name)
                                         .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var phantom = registered.Except(discovered).Select(t => t.Name)
                                    .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var duplicated = LoaderContracts.GroupBy(c => c.ConfigType).Where(g => g.Count() > 1)
                                            .Select(g => g.Key.Name)
                                            .OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Check("every config class with a static Load(string) is registered exactly once",
                  unregistered.Length == 0 && phantom.Length == 0 && duplicated.Length == 0,
                  unregistered.Length + phantom.Length + duplicated.Length == 0
                      ? $"{discovered.Length} discovered = {registered.Length} registered"
                      : $"unregistered: [{string.Join(", ", unregistered)}] phantom: [{string.Join(", ", phantom)}] duplicated: [{string.Join(", ", duplicated)}]");

            // Root ownership has a precise boundary: the root holds BOTH RollA's scalars
            // AND the section objects. These are two different assertions over the same
            // root and are kept separate here and in the failure messages.
            var claimedSections = LoaderContracts.Where(c => c.Shape != LoaderShape.RootFlat)
                                                 .Select(c => c.Surface).ToArray();
            var unclaimedSections = rootObjects.Except(claimedSections, StringComparer.Ordinal)
                                               .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var claimedButMissing = claimedSections.Except(rootObjects, StringComparer.Ordinal)
                                                   .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var doubleClaimed = claimedSections.GroupBy(s => s, StringComparer.Ordinal)
                                               .Where(g => g.Count() > 1).Select(g => g.Key).ToArray();

            Check("every top-level config.json section is claimed by exactly one contract",
                  unclaimedSections.Length == 0 && claimedButMissing.Length == 0 && doubleClaimed.Length == 0,
                  unclaimedSections.Length + claimedButMissing.Length + doubleClaimed.Length == 0
                      ? $"{rootObjects.Length} sections"
                      : $"unclaimed: [{string.Join(", ", unclaimedSections)}] registered-but-absent: [{string.Join(", ", claimedButMissing)}] double-claimed: [{string.Join(", ", doubleClaimed)}]");

            var rootFlatCount = LoaderContracts.Count(c => c.Shape == LoaderShape.RootFlat);
            Check("exactly one contract owns the root scalar surface",
                  rootFlatCount == 1, $"{rootFlatCount} root-flat contract(s), {rootScalars.Length} root scalars");
        }

        // ── Arms 2–4: bidirectional names, case, token kind — every parity surface ──
        var perSurface = new List<string>();
        foreach (var contract in LoaderContracts.OrderBy(c => c.Surface, StringComparer.Ordinal))
        {
            if (!contract.ParityChecked) continue;

            var keys = contract.Shape == LoaderShape.RootFlat
                ? rootScalars
                : SurfaceKeys(root.GetProperty(contract.Surface));
            var props = ConfigurationProperties(contract.ConfigType);

            var f = CompareSurface(keys, props, contract.ContainersLegal);
            var clean = f.Orphans.Count == 0 && f.Absent.Count == 0
                     && f.CaseOnly.Count == 0 && f.TokenKind.Count == 0;

            var detail = clean
                ? $"{keys.Length} keys / {props.Length} properties"
                : string.Join("  ",
                    new[]
                    {
                        f.Orphans.Count   > 0 ? $"ORPHAN key(s) silently ignored today: [{string.Join(", ", f.Orphans)}]" : null,
                        f.Absent.Count    > 0 ? $"ABSENT key(s) silently defaulting today: [{string.Join(", ", f.Absent)}]" : null,
                        f.CaseOnly.Count  > 0 ? $"CASE-ONLY match — binds at runtime by accident, rejected deliberately: [{string.Join(", ", f.CaseOnly)}]" : null,
                        f.TokenKind.Count > 0 ? $"TOKEN-KIND mismatch: [{string.Join(", ", f.TokenKind)}]" : null,
                    }.Where(s => s is not null));

            Check($"{contract.Surface} ({contract.Shape}) key-name parity", clean, detail);
            perSurface.Add($"{contract.Surface}: {keys.Length}/{props.Length}");
        }

        // ── Arm 5: RollE binding — the one surface where wires can cross ─────
        //  A behavioural test, not source parsing: load a config whose RollE section
        //  carries a DISTINCT value per field and assert every property came back
        //  holding ITS OWN key's value.
        //
        //  The obvious "1.0, 2.0, 3.0 …" sentinel scheme is ILLEGAL — RollEConfig.Load
        //  enforces nine invariants and would throw before proving anything. Each value
        //  below sits inside its own field's legal band, comfortably separated from the
        //  others (smallest gap 0.625) and off every boundary: UsageRail is 0.25 clear
        //  of 1.0, MaxTiltMultiplier 4.0 clear of its minimum, HierarchyExponentMax a
        //  full 1.0 above Neutral, and 5 × UsageFloor = 0.625 well under 1.0.
        //
        //  Every sentinel is exactly representable in binary and JSON round-trips it
        //  exactly, so the comparison is EXACT equality — stronger than a tolerance,
        //  and it invents no new numerical convention.
        {
            var sentinels = new (string Key, double Value)[]
            {
                ("BaseSlot1", 101.0), ("BaseSlot2", 102.0), ("BaseSlot3", 103.0),
                ("BaseSlot4", 104.0), ("BaseSlot5", 105.0),
                ("TransitionSlot1", 201.0), ("TransitionSlot2", 202.0), ("TransitionSlot3", 203.0),
                ("TransitionSlot4", 204.0), ("TransitionSlot5", 205.0),
                ("Epsilon", 301.0),
                ("UsageExponent", 3.0),            // > 0
                ("UsageFloor", 0.125),             // >= 0 and 5x < 1.0
                ("UsageRail", 0.75),               // > UsageFloor, <= 1.0
                ("MinUsageScore", 4.0),            // > 0
                ("MaxTiltMultiplier", 5.0),        // > 1.0
                ("TiltReferenceShift", 6.0),       // > 0
                ("HierarchyExponentNeutral", 7.0), // >= 0
                ("HierarchyExponentMax", 8.0),     // >= Neutral
            };

            var distinct = sentinels.Select(s => s.Value).Distinct().Count() == sentinels.Length;
            Check("RollE sentinels are distinct (a crossed wire must be visible)",
                  distinct, $"{sentinels.Length} fields");

            var eProps = ConfigurationProperties(typeof(RollEConfig));
            Check("RollE sentinel set covers every RollE configuration property",
                  eProps.Length == sentinels.Length
                  && eProps.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                       .SequenceEqual(sentinels.Select(s => s.Key).OrderBy(n => n, StringComparer.Ordinal),
                                      StringComparer.Ordinal),
                  $"{eProps.Length} properties / {sentinels.Length} sentinels");

            var section = new JsonObject();
            foreach (var (k, v) in sentinels) section[k] = v;
            var probeDoc = new JsonObject { ["RollE"] = section };
            var tmp = Path.Combine(Path.GetTempPath(), $"s74_rolle_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(tmp, probeDoc.ToJsonString());
                var loaded = RollEConfig.Load(tmp);

                var crossed = new List<string>();
                foreach (var (key, expected) in sentinels)
                {
                    var prop = typeof(RollEConfig).GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
                    var actual = (double)prop!.GetValue(loaded)!;
                    if (actual != expected)
                    {
                        var sourced = sentinels.FirstOrDefault(s => s.Value == actual).Key ?? "nothing";
                        crossed.Add($"{key} got {actual} (the value of {sourced}), expected {expected}");
                    }
                }
                crossed.Sort(StringComparer.Ordinal);

                Check("RollE hand-written loader binds each key to its OWN property",
                      crossed.Count == 0,
                      crossed.Count == 0
                          ? $"{sentinels.Length} fields, exact equality"
                          : $"CROSSED WIRE(S): [{string.Join("; ", crossed)}]");
            }
            catch (Exception ex)
            {
                Check("RollE hand-written loader binds each key to its OWN property", false, ex.Message);
            }
            finally { try { File.Delete(tmp); } catch { /* best-effort */ } }
        }

        // ── Arm 6: the twelve newly-explicit values, each against its own yardstick ──
        //  Eight are still on the compiled default; the four S83 tuned are pinned to the
        //  ruled value instead (see S83RuledMatchupValues for why neither dropping them
        //  nor moving the class defaults was the right answer).
        //  The season page is NOT a sufficient typo-catcher — a mistyped weight on a
        //  rare event might not move a seeded season at all — so the proof is
        //  programmatic, never a manual delivery-note claim.
        //
        //  This is only valid because new MatchupConfig() is PURE: the class declares no
        //  constructor, its only static member is Load, and all 269 defaults are numeric
        //  literals or compile-time literal fractions. No static state, no environment,
        //  no culture.
        {
            var live = MatchupConfig.Load(configPath);
            var fresh = new MatchupConfig();
            var mismatches = new List<string>();
            var missingProp = new List<string>();
            var ruledMismatches = new List<string>();

            var ruled = S83RuledMatchupValues.ToDictionary(r => r.Key, r => r.Ruled, StringComparer.Ordinal);

            foreach (var key in S74NewlyExplicitMatchupKeys)
            {
                var prop = typeof(MatchupConfig).GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
                if (prop is null) { missingProp.Add(key); continue; }
                var a = (double)prop.GetValue(live)!;
                if (ruled.TryGetValue(key, out var r))
                {
                    // S83-tuned: the yardstick is the RULING, not the compiled default.
                    if (a != r) ruledMismatches.Add($"{key}: config {a} vs ruled {r}");
                }
                else
                {
                    var b = (double)prop.GetValue(fresh)!;
                    if (a != b) mismatches.Add($"{key}: config {a} vs compiled default {b}");
                }
            }
            mismatches.Sort(StringComparer.Ordinal);
            ruledMismatches.Sort(StringComparer.Ordinal);
            missingProp.Sort(StringComparer.Ordinal);

            var untunedCount = S74NewlyExplicitMatchupKeys.Length - S83RuledMatchupValues.Length;
            Check($"the {untunedCount} untuned newly-explicit Matchup values equal a fresh MatchupConfig",
                  mismatches.Count == 0 && missingProp.Count == 0,
                  mismatches.Count == 0 && missingProp.Count == 0
                      ? $"{untunedCount} fields, exact equality"
                      : $"mismatched: [{string.Join("; ", mismatches)}] unknown property: [{string.Join(", ", missingProp)}]");

            Check($"the {S83RuledMatchupValues.Length} S83-tuned Matchup values equal the RULED values",
                  ruledMismatches.Count == 0,
                  ruledMismatches.Count == 0
                      ? $"{S83RuledMatchupValues.Length} fields, exact equality"
                      : $"mismatched: [{string.Join("; ", ruledMismatches)}]");

            var present = S74NewlyExplicitMatchupKeys
                .Where(k => root.GetProperty("Matchup").TryGetProperty(k, out _)).Count();
            Check("all twelve are present in config.json's Matchup section",
                  present == S74NewlyExplicitMatchupKeys.Length,
                  $"{present}/{S74NewlyExplicitMatchupKeys.Length}");
        }

        // ── Layer 1: planted-drift self-tests ────────────────────────────────
        //  Zero orphan keys exist on the real tree, so the orphan arm cannot go red
        //  against production and an unexercised failure path may simply not work.
        //  These fixtures are in-memory and NEVER touch the live config.json.
        {
            var props = new (string Name, Type Type)[]
            {
                ("Alpha", typeof(double)), ("Beta", typeof(int)), ("Gamma", typeof(bool)),
            };

            static (string, JsonValueKind)[] Keys(params (string, JsonValueKind)[] k) => k;

            var clean = CompareSurface(
                Keys(("Alpha", JsonValueKind.Number), ("Beta", JsonValueKind.Number), ("Gamma", JsonValueKind.True)),
                props, containersLegal: false);
            Check("self-test: control fixture is clean",
                  clean.Orphans.Count == 0 && clean.Absent.Count == 0
                  && clean.CaseOnly.Count == 0 && clean.TokenKind.Count == 0);

            var orphan = CompareSurface(
                Keys(("Alpha", JsonValueKind.Number), ("Beta", JsonValueKind.Number),
                     ("Gamma", JsonValueKind.True), ("Delta", JsonValueKind.Number)),
                props, containersLegal: false);
            Check("self-test: ORPHAN arm fires on a planted unknown key",
                  orphan.Orphans.Count == 1 && orphan.Orphans[0] == "Delta"
                  && orphan.Absent.Count == 0,
                  $"[{string.Join(", ", orphan.Orphans)}]");

            var absent = CompareSurface(
                Keys(("Alpha", JsonValueKind.Number), ("Gamma", JsonValueKind.True)),
                props, containersLegal: false);
            Check("self-test: ABSENT arm fires on a planted missing key",
                  absent.Absent.Count == 1 && absent.Absent[0] == "Beta"
                  && absent.Orphans.Count == 0,
                  $"[{string.Join(", ", absent.Absent)}]");

            var kind = CompareSurface(
                Keys(("Alpha", JsonValueKind.String), ("Beta", JsonValueKind.Number), ("Gamma", JsonValueKind.True)),
                props, containersLegal: false);
            Check("self-test: TOKEN-KIND arm fires on a string where a double is declared",
                  kind.TokenKind.Count == 1 && kind.Orphans.Count == 0 && kind.Absent.Count == 0,
                  $"[{string.Join(", ", kind.TokenKind)}]");

            var casing = CompareSurface(
                Keys(("alpha", JsonValueKind.Number), ("Beta", JsonValueKind.Number), ("Gamma", JsonValueKind.True)),
                props, containersLegal: false);
            Check("self-test: CASE arm fires on a case-only key, without double-counting it",
                  casing.CaseOnly.Count == 1 && casing.Orphans.Count == 0 && casing.Absent.Count == 0,
                  $"[{string.Join(", ", casing.CaseOnly)}]");

            // The container rule is contract-driven, not global: an array is legal only
            // where the registry entry permits it.
            var containerProps = new (string Name, Type Type)[] { ("Home", typeof(List<int>)) };
            var arrayBanned = CompareSurface(Keys(("Home", JsonValueKind.Array)), containerProps, containersLegal: false);
            var arrayAllowed = CompareSurface(Keys(("Home", JsonValueKind.Array)), containerProps, containersLegal: true);
            Check("self-test: container kinds are legal only where the contract permits",
                  arrayBanned.TokenKind.Count == 1 && arrayAllowed.TokenKind.Count == 0);
        }

        // ── Layer 2: coverage summary — proof it looked at the REAL surfaces ──
        {
            var excluded = LoaderContracts.Where(c => !c.ParityChecked).ToArray();
            var sectioned = LoaderContracts.Count(c => c.Shape == LoaderShape.Sectioned && c.ParityChecked);
            var matchupKeys = SurfaceKeys(root.GetProperty("Matchup")).Length;
            var matchupProps = ConfigurationProperties(typeof(MatchupConfig)).Length;
            var rollEProps = ConfigurationProperties(typeof(RollEConfig)).Length;

            Console.WriteLine();
            Console.WriteLine("  Phase 71: config key-name parity");
            Console.WriteLine($"    contracts: {LoaderContracts.Length} registered, {excluded.Length} excluded"
                + (excluded.Length > 0
                    ? $" ({string.Join("; ", excluded.Select(e => $"{e.Surface} — {e.ExclusionReason}"))})"
                    : ""));
            Console.WriteLine($"    shapes exercised: root-flat (RollA, {rootScalars.Length} root scalars) | manual (RollE, {rollEProps} fields) | sectioned x{sectioned}");
            Console.WriteLine($"    Matchup: {matchupKeys} keys / {matchupProps} properties");
            Console.WriteLine($"    surfaces: {string.Join(", ", perSurface)}");
            Console.WriteLine($"    proves: key names, token kind, RollE binding. does NOT prove: values, ranges, semantics.");
        }

        Console.WriteLine($"\n  Phase 71 {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
