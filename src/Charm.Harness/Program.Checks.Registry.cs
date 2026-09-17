using System.Reflection;

namespace Charm.Harness;

// ============================================================================
//  Phase 100 — S110.1: THE REGISTRY GUARDS ITSELF.
//
//  This session's only real hazard is a table that silently stops matching the
//  suite. Every failure mode here is INVISIBLE FROM OUTPUT, because all of them
//  make the run faster and greener:
//
//    - a phase method that exists but was never registered (the S88 bug: Phase 79
//      shipped at S88 and never executed once until S89.1 found it);
//    - a row dropped in the refactor (five gated-but-untimed phases were nearly
//      dropped by the build prompt's own contract);
//    - a row renamed or reordered so the suite no longer runs what it used to;
//    - a selector that quietly matches the wrong rows;
//    - a partial run that presents itself as a suite pass.
//
//  So the checks below are deliberately of two kinds. The REFLECTION arms catch
//  a method/row mismatch. The FROZEN ORDER ORACLE catches everything else,
//  including the five phases reflection structurally cannot see (see C2c).
//
//  ★ THE FROZEN LIST IS A TEST ORACLE AND IS THE ONE PERMITTED SECOND LIST.
//  The registry is the single EXECUTION list; this is a frozen EXPECTATION and
//  must never drive execution. Before S110.1 the order lived as literal source
//  lines in Program.Main; after the refactor it does not exist anywhere else, so
//  an assertion about it needs its own copy or it asserts nothing.
//
//  Page-only calibration holds — nothing here touches basketball.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ THE EXECUTION ORDER, FROZEN. All 85 rows in the order the suite walks them.
    /// Derived mechanically from the pre-S110.1 source block (Program.cs:181-304 as it stood at
    /// S110) rather than typed out: Phase 0 is the chain block that always ran first, rows 1-99
    /// are the gated phases in their authored order with the five formerly-untimed phases sitting
    /// exactly where they always sat, Phase 100 follows Phase 99 so the numbers stay ascending,
    /// and the two number-less rows have always run last.
    /// ★ CHANGING THIS LIST IS ALLOWED ONLY WHEN THE SUITE DELIBERATELY GAINS OR LOSES A CHECK.
    /// If it goes red on a session that did not mean to touch the table, the table is wrong —
    /// not this list.</summary>
    private static readonly string[] FrozenCheckOrder =
    {
        "Phase0ChainChecks", "Phase1RosterCheck", "Phase2AttributeWiringCheck",
        "Phase6MatchupWiringCheck", "Phase7BlockDoorCheck", "Phase8FoulDoorCheck",
        "Phase9LocationDoorCheck", "Phase10ReboundDoorCheck", "Phase11FreeThrowReboundDoorCheck",
        "Phase12DisruptionDoorCheck", "Phase13TeamDisruptionDoorCheckRollB", "Phase15PressFrequencyStandardCheck",
        "Phase16PressBreakFastBreakCheck", "Phase17UsageEfficiencyCheck", "AttributionSanityCheck",
        "Phase25ShootingFoulAttributionCheck", "Phase29HierarchyBiasCheck", "Phase30CoachingLayer2Check",
        "Phase31RebounderPickerCheck", "Phase32PutbackAttemptRateCheck", "Phase33TurnoverCommitterCheck",
        "Phase34TurnoverAttributionCheck", "Phase35DefensiveReboundCheck", "Phase36BlockerCheck",
        "Phase39AssistCheck", "Phase41HelpDefenseCheck", "Phase42ScreeningCheck",
        "Phase43ReboundPhysicalWeightsCheck", "Phase44OffBallDefenseCheck", "Phase45HustleCheck",
        "Phase46IndividualDenialCheck", "PassingCompoundCheck", "FatigueMeterCheck",
        "FatigueAthleticismCheck", "Phase50BasketballIqCheck", "FreeThrowFoulDrawCheck",
        "Phase52SubstitutionsCheck", "Phase53WorldStructureCheck", "Phase54DivvyCheck",
        "Phase55SeasonCheck", "Phase56DisplacementCheck", "Phase57TurnoverClockCheck",
        "Phase58FastBreakDietCheck", "Phase61HeightOverDefenderCheck", "Phase62UnforcedTurnoverCheck",
        "Phase63PostMovesInteriorCheck", "Phase64StealFloorCheck", "Phase65DriveGateCheck",
        "Phase66UsageReliefCheck", "Phase67DisciplineShaveCheck", "Phase68NonShootingFoulCheck",
        "Phase69GenPass3ReplayParityCheck", "Phase70GenPass3LiveCheck", "Phase71ConfigKeyNameParityCheck",
        "Phase72MinutesAllocatorCheck", "Phase73SeasonStatsCheck", "Phase74BlockHelpCheck",
        "Phase75VerticalCheck", "Phase76TransitionReadoutCheck", "Phase77TransitionOpportunityCheck",
        "Phase78RealFoulsCheck", "Phase79TransitionDefenseCheck", "Phase80IdentityCheck",
        "Phase81GameLogCheck", "Phase82CalendarCheck", "Phase83GeographyCheck",
        "Phase84ConferenceSlateCheck", "Phase85ConferenceDatesCheck", "Phase86HomeCourtCheck",
        "Phase87SeasonMemoryCheck", "Phase88MteCheck", "Phase89BracketsCheck",
        "Phase90RotationCheck", "Phase91HostDebtCheck", "Phase92NonConferenceCheck",
        "Phase93MatchingCheck", "Phase94ContractsCheck", "Phase95ShowcasesCheck",
        "Phase96IndependentsCheck", "Phase97NonConferenceDatesCheck", "Phase98KnockoutCheck",
        "Phase99ConferenceTournamentsCheck", "Phase100RegistryCheck", "ObservationRunV1",
        "StressTestArchetypeRosters",    };

    /// <summary>Index of the first place two name sequences differ, or -1 when they are the same
    /// sequence AND the same length. Extracted so the negative controls below can drive the very
    /// same comparison C3b relies on — a check that cannot be made to go red discharges nothing.</summary>
    private static int FirstOrderDivergence(IReadOnlyList<string> frozen, IReadOnlyList<string> actual)
    {
        for (var i = 0; i < Math.Min(frozen.Count, actual.Count); i++)
            if (!string.Equals(frozen[i], actual[i], StringComparison.Ordinal)) return i;
        return frozen.Count == actual.Count ? -1 : Math.Min(frozen.Count, actual.Count);
    }

    private static bool Phase100RegistryCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 100 — S110.1: the check registry guards itself. Reflection closure both " +
                          "ways, the frozen execution order, selector parsing with a negative control each, " +
                          "and the partial-run flag ==");
        var pass = true;
        var assertions = 0;

        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            var reg = BuildRegistry(configPath);

            // ── C1: the shape of the table ─────────────────────────────────────────
            Check("C1a: the registry holds exactly 85 rows", reg.Count == 85, $"{reg.Count}");

            var names = reg.Select(r => r.Name).ToList();
            Check("C1b: every row name is distinct",
                  names.Distinct(StringComparer.Ordinal).Count() == names.Count,
                  $"{names.Count - names.Distinct(StringComparer.Ordinal).Count()} duplicates");

            var numbers = reg.Where(r => r.Number is not null).Select(r => r.Number!.Value).ToList();
            Check("C1c: numbers, where present, are distinct",
                  numbers.Distinct().Count() == numbers.Count);
            Check("C1d: numbers, where present, are STRICTLY ASCENDING in execution order",
                  numbers.Zip(numbers.Skip(1), (a, b) => b > a).All(x => x),
                  $"{numbers.Count} numbered rows, {reg.Count - numbers.Count} without a number");
            Check("C1e: exactly two rows carry no number (name-selectable only)",
                  reg.Count(r => r.Number is null) == 2);

            // ── C2: reflection closure, BOTH directions ────────────────────────────
            //  Forward catches the S88 bug — a phase method nobody registered. Reciprocal
            //  catches a stale or renamed row pointing at a method that no longer exists.
            //  Matching is by METHOD NAME, never by delegate signature: Phase 57's inputs
            //  changed this session, and a signature-shaped assertion would go red on a
            //  perfectly correct registry.
            var assemblyPhaseMethods = typeof(Program)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(m => m.Name)
                .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^(Run)?Phase\d+"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var registeredMethods = reg.Select(r => r.Method).ToList();

            var unregistered = assemblyPhaseMethods
                .Where(m => !registeredMethods.Contains(m, StringComparer.Ordinal)).ToList();
            Check("C2a: ★ FORWARD — every Phase-named method in the assembly is registered (the S88 bug)",
                  unregistered.Count == 0,
                  unregistered.Count == 0
                      ? $"{assemblyPhaseMethods.Count} methods, all registered"
                      : "unregistered: " + string.Join(", ", unregistered));

            var doubleRegistered = registeredMethods
                .Where(m => assemblyPhaseMethods.Contains(m, StringComparer.Ordinal))
                .GroupBy(m => m, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check("C2b: ★ FORWARD — and registered exactly ONCE, never twice",
                  doubleRegistered.Count == 0,
                  doubleRegistered.Count == 0 ? "" : "twice: " + string.Join(", ", doubleRegistered));

            var danglingRows = reg
                .Where(r => r.Number is not null)
                .Where(r => System.Text.RegularExpressions.Regex.IsMatch(r.Method, @"^(Run)?Phase\d+"))
                .Where(r => !assemblyPhaseMethods.Contains(r.Method, StringComparer.Ordinal))
                .Select(r => r.Name).ToList();
            Check("C2c: ★ RECIPROCAL — every Phase-named row resolves to a real method",
                  danglingRows.Count == 0,
                  danglingRows.Count == 0 ? "" : "dangling: " + string.Join(", ", danglingRows));

            //  ★ THE STATED LIMIT, not papered over. Five gated phases are named
            //  AttributionSanityCheck, PassingCompoundCheck, FatigueMeterCheck,
            //  FatigueAthleticismCheck and FreeThrowFoulDrawCheck — no "PhaseNN" prefix — so
            //  the forward arm CANNOT see them. They are covered by C3's frozen order and by
            //  nothing else. Do NOT widen the forward pattern to try to catch them: it would
            //  sweep in every helper in the harness and the arm would stop meaning anything.
            var legacyNamed = new[]
            {
                "AttributionSanityCheck", "PassingCompoundCheck", "FatigueMeterCheck",
                "FatigueAthleticismCheck", "FreeThrowFoulDrawCheck",
            };
            Check("C2d: the five legacy-named gated phases are registered (reflection cannot see these)",
                  legacyNamed.All(m => registeredMethods.Contains(m, StringComparer.Ordinal)),
                  string.Join(", ", legacyNamed.Where(m => !registeredMethods.Contains(m, StringComparer.Ordinal))));

            Check("C2e: the two number-less rows are exempt from the reciprocal arm by NUMBER, not by name",
                  reg.Where(r => r.Number is null).All(r =>
                      !System.Text.RegularExpressions.Regex.IsMatch(r.Method, @"^(Run)?Phase\d+")));

            // ── C3: ★ the frozen execution order ───────────────────────────────────
            //  The only check that can catch a dropped legacy-named phase, a reorder, or a
            //  rename. Asserted as a whole sequence, not a set.
            Check("C3a: the frozen order oracle itself holds 85 names", FrozenCheckOrder.Length == 85,
                  $"{FrozenCheckOrder.Length}");
            var firstDiff = FirstOrderDivergence(FrozenCheckOrder, names);
            Check("C3b: ★ EXECUTION ORDER is identical to the frozen pre-S110.1 sequence",
                  firstDiff < 0,
                  firstDiff < 0 ? "85 of 85 in order"
                                : $"first divergence at index {firstDiff}: frozen '{FrozenCheckOrder.ElementAtOrDefault(firstDiff)}' "
                                  + $"vs table '{names.ElementAtOrDefault(firstDiff)}'");
            Check("C3c: dropping the two rows this session ADDED leaves the pre-S110.1 order exactly",
                  names.Count(n => n is not "Phase0ChainChecks" and not "Phase100RegistryCheck") == 83);

            //  ── The negative controls for C3b. This is the ONLY check standing between the
            //     suite and a silently dropped phase, so it has to be shown firing rather than
            //     trusted. Each control constructs the exact failure it names.
            var dropped = names.Where(n => n != "PassingCompoundCheck").ToList();
            Check("C3d: ★ NEGATIVE CONTROL — dropping a legacy-named phase (Phase 47) is REJECTED "
                  + "(this is the failure reflection structurally cannot see)",
                  FirstOrderDivergence(FrozenCheckOrder, dropped) >= 0);

            var swapped = names.ToList();
            (swapped[40], swapped[41]) = (swapped[41], swapped[40]);
            Check("C3e: ★ NEGATIVE CONTROL — swapping two adjacent rows is REJECTED (a reorder keeps "
                  + "the same SET, so a set-shaped assertion would pass here)",
                  FirstOrderDivergence(FrozenCheckOrder, swapped) >= 0);

            var appended = names.Append("Phase101SomethingCheck").ToList();
            Check("C3f: ★ NEGATIVE CONTROL — an unannounced extra row is REJECTED",
                  FirstOrderDivergence(FrozenCheckOrder, appended) >= 0);

            Check("C3g: ★ NEGATIVE CONTROL — the comparison accepts an untouched copy (so C3d-f are "
                  + "rejecting the mutation, not rejecting everything)",
                  FirstOrderDivergence(FrozenCheckOrder, names.ToList()) < 0);

            var unregisteredControl = assemblyPhaseMethods
                .Where(m => !registeredMethods.Where(x => x != "Phase79TransitionDefenseCheck")
                                              .Contains(m, StringComparer.Ordinal)).ToList();
            Check("C3h: ★ NEGATIVE CONTROL — the FORWARD arm rejects an unregistered phase method "
                  + "(the S88 bug, reconstructed on Phase 79 itself)",
                  unregisteredControl.Count == 1 && unregisteredControl[0] == "Phase79TransitionDefenseCheck");

            // ── C4: selector parsing, each with its negative control ───────────────
            var one = SelectRows(reg, new[] { "99" });
            Check("C4a: '99' selects exactly one row, and it is Phase 99",
                  one.Refusals.Count == 0 && one.Selected.Count == 1 && one.Selected[0].Number == 99,
                  $"{one.Selected.Count} selected");

            var range = SelectRows(reg, new[] { "92-99" });
            Check("C4b: '92-99' selects eight (those eight numbers happen to be contiguous)",
                  range.Refusals.Count == 0 && range.Selected.Count == 8,
                  $"{range.Selected.Count} selected");

            var holed = SelectRows(reg, new[] { "18-23" });
            Check("C4c: a range over a HOLE is legal and selects what exists — 18-23 selects nothing "
                  + "registered, so it is refused by name rather than run empty",
                  holed.Refusals.Count == 1 && holed.Selected.Count == 0);

            var sub = SelectRows(reg, new[] { "knockout" });
            Check("C4d: 'knockout' selects Phase 98 and nothing else",
                  sub.Refusals.Count == 0 && sub.Selected.Count == 1 && sub.Selected[0].Number == 98,
                  $"{sub.Selected.Count} selected");

            var many = SelectRows(reg, new[] { "Transition" });
            Check("C4e: a substring matching MANY rows is not an error",
                  many.Refusals.Count == 0 && many.Selected.Count > 1, $"{many.Selected.Count} selected");

            var reversed = SelectRows(reg, new[] { "99-92" });
            Check("C4f: ★ '99-92' is REFUSED as reversed and never normalised",
                  reversed.Refusals.Count == 1 && reversed.Selected.Count == 0,
                  reversed.Refusals.Count == 1 ? reversed.Refusals[0] : "");

            var unknown = SelectRows(reg, new[] { "nosuchcheck" });
            Check("C4g: ★ an unknown selector is REFUSED BY NAME and selects zero — asserted as a "
                  + "refusal, never as an empty run",
                  unknown.Refusals.Count == 1 && unknown.Refusals[0].Contains("nosuchcheck")
                      && unknown.Selected.Count == 0);

            var mixed = SelectRows(reg, new[] { "99", "nosuchcheck" });
            Check("C4h: ★ ONE bad selector refuses the WHOLE run — resolve-then-execute, never partway",
                  mixed.Refusals.Count == 1 && mixed.Selected.Count == 0,
                  $"{mixed.Selected.Count} selected despite a valid '99'");

            var listOnly = SelectRows(reg, new[] { "list" });
            Check("C4i: 'list' alone is a list request and runs nothing",
                  listOnly.ListOnly && listOnly.Refusals.Count == 0 && listOnly.Selected.Count == 0);

            var listCombo = SelectRows(reg, new[] { "list", "99" });
            Check("C4j: ★ 'list 99' is REFUSED as a combination",
                  !listCombo.ListOnly && listCombo.Refusals.Count == 1 && listCombo.Selected.Count == 0);

            var empty = SelectRows(reg, Array.Empty<string>());
            Check("C4k: ★ 'checks' with no selector is REFUSED and runs nothing",
                  !empty.ListOnly && empty.Refusals.Count == 1 && empty.Selected.Count == 0);

            var dupe = SelectRows(reg, new[] { "99", "92-99" });
            Check("C4l: a row named by two selectors runs ONCE",
                  dupe.Refusals.Count == 0 && dupe.Selected.Count == 8);

            var order = SelectRows(reg, new[] { "99", "1" });
            Check("C4m: selection preserves TABLE order, not the order the selectors were typed",
                  order.Selected.Count == 2 && order.Selected[0].Number == 1 && order.Selected[1].Number == 99);

            var zero = SelectRows(reg, new[] { "0" });
            Check("C4n: Phase 0 is an ordinary selectable row — the prelude is a diagnostic target",
                  zero.Refusals.Count == 0 && zero.Selected.Count == 1 && zero.Selected[0].Number == 0);

            // ── C5: the partial flag ───────────────────────────────────────────────
            Check("C5a: a full walk reports IsPartial == false", !FullWalkSummary(85, true).IsPartial);
            Check("C5b: a selector run reports IsPartial == true", PartialSummary(85, 1, true).IsPartial);
            Check("C5c: ★ a selector that names EVERY row is STILL partial — `checks` can never "
                  + "become a second spelling of the delivery gate",
                  PartialSummary(85, 85, true).IsPartial);
            Check("C5d: the summary carries the executed and total counts",
                  PartialSummary(85, 8, true) is { Executed: 8, Total: 85 });

            // ── C6: the two name-shape traps that make pattern-derived matching wrong ──
            var p97 = reg.Single(r => r.Number == 97);
            Check("C6a: Phase 97's row name and METHOD name differ (RunPhase97…) and the row carries both",
                  p97.Name == "Phase97NonConferenceDatesCheck"
                      && p97.Method == "RunPhase97NonConferenceDatesCheck");
            var p13 = reg.Single(r => r.Number == 13);
            Check("C6b: Phase 13's method does NOT end in \"Check\" — a `Phase\\d+.*Check$` rule would miss it",
                  p13.Method == "Phase13TeamDisruptionDoorCheckRollB"
                      && !p13.Method.EndsWith("Check", StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            Check("Phase 100 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine(pass ? $"  Phase 100 PASS ({assertions} assertions)" : $"  Phase 100 FAIL ({assertions} assertions)");
        return pass;
    }
}
