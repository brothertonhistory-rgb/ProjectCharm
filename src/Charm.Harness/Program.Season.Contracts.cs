using System.Globalization;
using System.Text.Json;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S103 — CONTRACTS AND THE NON-CONFERENCE LOG (non-conference arc, session 3).
//
//  The engine learns to KEEP PROMISES IT CANNOT YET MAKE. A contract —
//  home-and-home, 2-for-1, five-in-eight, neutral series — is two schools, an
//  explicit executor, an ordered list of legs, and a window. It persists in the
//  season record, is exercised before anything else touches a school's slate,
//  and dies by exactly two rules that both fail closed. NOTHING IN THE ENGINE
//  SIGNS A CONTRACT — fixture-authored contracts prove the honouring; the
//  negotiation layer is a future, coach-adjacent session.
//
//  ★ THE SPEC FOR THE WINDOW MACHINE IS tools/contracts_oracle.py. Its
//  docstring is the authority; ContractSeasonStep + RollContractsForward below
//  are its port, and Phase 94 C1 replays tools/contracts_golden.json through
//  them trajectory for trajectory. All integers — parity is exact by
//  construction, never ULP-bounded.
//
//  ★ THE LEG LIST PERSISTS; gamesRemaining IS DERIVED, NEVER STORED (brief r3
//  §2). A stored count and a leg list can disagree, and the disagreement
//  surfaces a career later. The one stored counter is the window.
//
//  ★ THE WINDOW INCLUDES THE CURRENT SEASON and decrements at ROLLOVER, after
//  the decision — never before it. Forced iff outstanding legs == window.
//
//  ★ DISCOVER, RESERVE, VALIDATE, COMMIT. Forced legs are never placed before
//  the engine knows they ALL fit: every forced leg is discovered and reserved
//  against both schools, capacity is validated globally, and only then is
//  anything committed. The naive place-then-validate order produces an
//  order-dependent partial schedule; this produces one hard world-state
//  failure with nothing committed.
//
//  ★ ON A FORCED-CAPACITY FAILURE THE TRANSITION FREEZES. No exercise, no
//  completion, and NO WINDOW DECREMENT: decrementing an unexercised forced
//  contract would manufacture outstanding > window, which the next season's
//  load correctly refuses as damage. A broken world is reported and carried,
//  never laundered into a corrupt record.
//
//  ★ BOTH DEATHS FAIL CLOSED (R24). Same conference terminates hard — before
//  any exercise, so an equality-forced contract is never exercised through the
//  wall. A damaged record drops the collection and reports a COLLECTION-LEVEL
//  loss, never a named pairing it could not read — naming one would require a
//  partial-salvage path that contradicts failing closed.
//
//  ★ TWO COLLECTIONS, NEVER ONE (brief §5). The pairing log is an append-only
//  played... paired FACT — one entry per non-conference pairing this season,
//  written into this season's record. The live-contract collection is mutable
//  FORWARD state. They share the record file and nothing else; neither is ever
//  inferred from the other.
//
//  ★ "ARCHIVE" MEANS REMOVAL. Completed and dead contracts are simply omitted
//  from the survivors written forward; page rows are emitted from this
//  season's result. No third persisted collection exists.
// ============================================================================

internal static partial class Program
{
    // ── R8: the one seam. S103's tunable behaviour lives here. ──────────────────────

    /// <summary>★ THE PLACEHOLDER EXERCISE POLICY — the R8 seam coach temperament
    /// inherits (brief §2, Claude's recommendation, accepted). FALSE = an optional
    /// contract declines until the window forces it, which is the naive policy the
    /// brief names when it accepts the away-leg drift as realistic. Forced contracts
    /// always exercise regardless of this constant. An injected leg choice (a fixture
    /// today, a human-controlled program later) also always exercises.</summary>
    private const bool ContractOptionalPolicyExercise = false;

    // ── The object ──────────────────────────────────────────────────────────────────

    /// <summary>One game of a contract. <c>IsNeutral</c> is stored AS THE WORD
    /// ("Home"/"Neutral") on disk, never as a blank host: on a file read six years
    /// from now, "nobody hosts this" and "somebody forgot to write it" must not look
    /// identical. <c>HostId</c> is meaningful ONLY when the site is Home, and the
    /// serializer enforces that both directions. <c>Order</c> is the authored order,
    /// stored explicitly — it is the tie-breaker the leg choice consults, and leaning
    /// on array position silently changes the rule the day something sorts the
    /// list.</summary>
    private sealed record ContractLeg(int LegId, int Order, bool IsNeutral, int? HostId, bool Completed);

    /// <summary>A live contract (brief r3 §2, R22/R22a). Two schools, an EXPLICIT
    /// executor (host plurality cannot resolve a home-and-home, a neutral series, or
    /// any even split), an ordered leg list, and the window — the one stored counter.
    /// Completed legs stay in the list while the contract lives: they are how the
    /// page says "two of five played, one in Tulsa" and how the specific-leg check
    /// proves the executor's choice survived a save.</summary>
    private sealed record LiveContract(
        int ContractId, int SchoolAId, int SchoolBId, int ExecutorId,
        int WindowRemaining, IReadOnlyList<ContractLeg> Legs)
    {
        /// <summary>★ Derived, never stored — the brief's own invariant:
        /// gamesRemaining == count(Outstanding legs).</summary>
        public int GamesRemaining => Legs.Count(l => !l.Completed);
    }

    /// <summary>One exercised leg, as a fixed pairing. This bypasses matching
    /// entirely — it already names both schools and the host (or that it is
    /// neutral), so there is nothing to match.</summary>
    private sealed record ContractedGame(
        int ContractId, int LegId, int SchoolAId, int SchoolBId, bool IsNeutral, int? HostId);

    /// <summary>What contracts charge against one school's November, by the ruled
    /// buckets (Emmett, 2026-08-05): a leg is charged to its own bucket first, and
    /// when that bucket is empty, along a fixed chain ending at the home band. See
    /// <see cref="ApplyContractCharges"/> for the chains.</summary>
    private sealed record ContractChargeSet(int Hosted, int Away, int Neutral)
    {
        public static readonly ContractChargeSet None = new(0, 0, 0);
        public int Total => Hosted + Away + Neutral;
    }

    // ── Load: where last season's promises come from ────────────────────────────────

    /// <summary>Why this season does or does not have live contracts. The state-not-
    /// exception shape follows S96's HostMemoryStatus deliberately (brief §7.4): one
    /// story for "the record is unreadable so we do not guess," not two.</summary>
    private enum ContractLoadStatus
    {
        /// <summary>Legacy mode — no career, so there is no previous season.</summary>
        NoHistory,
        /// <summary>Season 1 of the career. Season 0 was never a candidate.</summary>
        FirstSeason,
        /// <summary>Season N-1 exists as a number but published no record.</summary>
        NoRecord,
        /// <summary>Season N-1's record is the retired v1 shape — a pre-contract
        /// career, and it reads as EMPTY, never as unknown (A2).</summary>
        PreContractFormat,
        /// <summary>★ R24(b): the record was present and could not be trusted. The
        /// COLLECTION is lost; no contract inside it is ever named, because naming one
        /// would require the partial salvage that failing closed forbids.</summary>
        CollectionLost,
        /// <summary>A valid v2 record was read whole. Its collection may legitimately
        /// be empty.</summary>
        Loaded,
    }

    private sealed record ContractLoad(
        ContractLoadStatus Status, IReadOnlyList<LiveContract> Contracts, string? Diagnostic)
    {
        public static ContractLoad Empty(ContractLoadStatus status, string? diagnostic = null)
            => new(status, Array.Empty<LiveContract>(), diagnostic);
    }

    /// <summary>★ Read season N-1's live contracts. EXACTLY N-1, found by arithmetic
    /// like every other memory read — a missing or unreadable year yields the empty
    /// or lost status, never a reach for an older record. The tournament reader walks
    /// four seasons; contracts live only in the immediately previous record because
    /// each record carries its own COMPLETE forward state (R23a).</summary>
    private static ContractLoad ReadLiveContracts(HistoryStore? history, long pendingSeasonId)
    {
        if (history is null) return ContractLoad.Empty(ContractLoadStatus.NoHistory);
        if (pendingSeasonId <= 1) return ContractLoad.Empty(ContractLoadStatus.FirstSeason);
        var seasonId = pendingSeasonId - 1;
        var path = MteRecordPathFor(history.Path, seasonId);
        if (!File.Exists(path)) return ContractLoad.Empty(ContractLoadStatus.NoRecord);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ContractLoad.Empty(ContractLoadStatus.CollectionLost, "record is not an object");
            if (!root.TryGetProperty("formatVersion", out var fv) || !fv.TryGetInt32(out var version))
                return ContractLoad.Empty(ContractLoadStatus.CollectionLost, "record names no format version");
            if (version == 1)
                return ContractLoad.Empty(ContractLoadStatus.PreContractFormat);
            if (version != MteRecordFormatVersion)
                return ContractLoad.Empty(ContractLoadStatus.CollectionLost,
                    $"unsupported record version {version.ToString(CultureInfo.InvariantCulture)}");
            if (!root.TryGetProperty("historyId", out var hid) || hid.ValueKind != JsonValueKind.String
                || !string.Equals(hid.GetString(), history.HistoryId, StringComparison.Ordinal))
                return ContractLoad.Empty(ContractLoadStatus.CollectionLost, "record belongs to another career");
            if (!root.TryGetProperty("seasonId", out var sid) || !sid.TryGetInt64(out var embedded)
                || embedded != seasonId)
                return ContractLoad.Empty(ContractLoadStatus.CollectionLost, "record names a different season");
            // ★ A v2 record ALWAYS carries the array, even empty — the writer below
            //   guarantees it. Absence is damage, not "no contracts": a missing field
            //   must never deserialize into a guess (A2).
            if (!root.TryGetProperty("liveContracts", out var lc) || lc.ValueKind != JsonValueKind.Array)
                return ContractLoad.Empty(ContractLoadStatus.CollectionLost, "record carries no live-contract collection");
            var contracts = ParseLiveContracts(lc);
            ValidateContractCollection(contracts, loaded: true);
            return new ContractLoad(ContractLoadStatus.Loaded, contracts, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            // ★ COLLECTION-LEVEL, deliberately: the diagnostic carries the exception
            //   type or the validator's structural complaint, never a school or a
            //   pairing out of the damaged data.
            return ContractLoad.Empty(ContractLoadStatus.CollectionLost, ex.GetType().Name);
        }
    }

    /// <summary>Strict parse of the persisted collection. Any missing field, wrong
    /// type, or unknown site/status WORD throws — the caller classifies the whole
    /// collection as lost. Words, never ordinals: an ordinal is fragile across a
    /// reorder and this value lives in a permanent career record.</summary>
    private static List<LiveContract> ParseLiveContracts(JsonElement array)
    {
        var result = new List<LiveContract>();
        foreach (var el in array.EnumerateArray())
        {
            int ReadInt(string name)
            {
                if (!el.TryGetProperty(name, out var p) || !p.TryGetInt32(out var v))
                    throw new InvalidOperationException($"contract is missing integer '{name}'");
                return v;
            }
            if (!el.TryGetProperty("legs", out var legsEl) || legsEl.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("contract carries no leg list");
            var legs = new List<ContractLeg>();
            foreach (var lg in legsEl.EnumerateArray())
            {
                int LegInt(string name)
                {
                    if (!lg.TryGetProperty(name, out var p) || !p.TryGetInt32(out var v))
                        throw new InvalidOperationException($"leg is missing integer '{name}'");
                    return v;
                }
                var site = lg.TryGetProperty("site", out var st) && st.ValueKind == JsonValueKind.String
                    ? st.GetString() : null;
                var isNeutral = site switch
                {
                    "Home" => false,
                    "Neutral" => true,
                    _ => throw new InvalidOperationException($"leg carries unknown site '{site}'"),
                };
                int? hostId = null;
                var hasHost = lg.TryGetProperty("hostId", out var h) && h.ValueKind == JsonValueKind.Number;
                if (isNeutral && hasHost)
                    throw new InvalidOperationException("a Neutral leg must carry no host");
                if (!isNeutral)
                {
                    if (!hasHost || !h.TryGetInt32(out var hv))
                        throw new InvalidOperationException("a Home leg must name its host");
                    hostId = hv;
                }
                var status = lg.TryGetProperty("status", out var su) && su.ValueKind == JsonValueKind.String
                    ? su.GetString() : null;
                var completed = status switch
                {
                    "Outstanding" => false,
                    "Completed" => true,
                    _ => throw new InvalidOperationException($"leg carries unknown status '{status}'"),
                };
                legs.Add(new ContractLeg(LegInt("legId"), LegInt("order"), isNeutral, hostId, completed));
            }
            result.Add(new LiveContract(
                ReadInt("contractId"), ReadInt("schoolAId"), ReadInt("schoolBId"),
                ReadInt("executorId"), ReadInt("windowRemaining"), legs));
        }
        return result;
    }

    /// <summary>★ THE ONE VALIDATOR, TWO CONSUMERS. Authoring (a fixture, a check, one
    /// day a negotiation layer) calls it directly and a violation refuses loudly by
    /// name. The record reader calls it with <paramref name="loaded"/> and a violation
    /// classifies the collection as lost. Rejected at authoring: duplicate contract
    /// id; duplicate leg id within a contract; executor not one of the two schools; a
    /// Home leg hosted by neither school; the same school on both sides; no legs;
    /// zero or negative window; outstanding legs exceeding the window. A LOADED
    /// collection additionally refuses a fully-completed contract (rollover omits
    /// them, so one on disk is damage) — the same rule that makes windowRemaining
    /// zero-or-less damage rather than a state.</summary>
    private static void ValidateContractCollection(IReadOnlyList<LiveContract> contracts, bool loaded = false)
    {
        var ids = new HashSet<int>();
        foreach (var c in contracts)
        {
            void Refuse(string why) => throw new InvalidOperationException(
                $"CONTRACT COLLECTION REFUSED: contract {c.ContractId.ToString(CultureInfo.InvariantCulture)} {why}.");

            if (!ids.Add(c.ContractId)) Refuse("duplicates another contract's id");
            if (c.SchoolAId == c.SchoolBId) Refuse("names the same school on both sides");
            if (c.ExecutorId != c.SchoolAId && c.ExecutorId != c.SchoolBId)
                Refuse("names an executor that is not one of its two schools");
            if (c.Legs.Count == 0) Refuse("carries no legs");
            if (c.WindowRemaining <= 0) Refuse("carries a zero or negative window");
            var legIds = new HashSet<int>();
            foreach (var l in c.Legs)
            {
                if (!legIds.Add(l.LegId)) Refuse($"duplicates leg id {l.LegId.ToString(CultureInfo.InvariantCulture)}");
                if (!l.IsNeutral && l.HostId != c.SchoolAId && l.HostId != c.SchoolBId)
                    Refuse($"leg {l.LegId.ToString(CultureInfo.InvariantCulture)} is hosted by neither school");
            }
            if (c.GamesRemaining > c.WindowRemaining)
                Refuse($"has {c.GamesRemaining.ToString(CultureInfo.InvariantCulture)} outstanding legs " +
                       $"against a window of {c.WindowRemaining.ToString(CultureInfo.InvariantCulture)}");
            if (loaded && c.GamesRemaining == 0)
                Refuse("is fully completed — rollover omits completed contracts, so one on disk is damage");
        }
    }

    // ── The season step: terminate → discover → reserve → validate → commit ─────────

    private sealed record ContractTermination(int ContractId, int SchoolAId, int SchoolBId, int LegsLost);

    /// <summary>Everything this season's contract phase decided. Page-facing plus the
    /// two facts downstream consumes: <c>Charges</c> for the request builder and
    /// <c>UsedPairs</c> for the matcher.</summary>
    private sealed class ContractSeasonOutcome
    {
        public required ContractLoad Load { get; init; }
        public required IReadOnlyList<ContractedGame> Exercised { get; init; }
        public required IReadOnlyList<ContractTermination> Terminated { get; init; }
        public required IReadOnlyList<int> PolicyDeclined { get; init; }
        public required IReadOnlyList<int> CapacityBlocked { get; init; }
        public required bool ForcedCapacityFailure { get; init; }
        public required string? ForcedCapacityDetail { get; init; }
        /// <summary>The live collection written forward — post-termination,
        /// post-completion, post-decrement, completed omitted.</summary>
        public required IReadOnlyList<LiveContract> Survivors { get; init; }
        public required IReadOnlyDictionary<int, ContractChargeSet> Charges { get; init; }
        public IReadOnlyList<(int Lo, int Hi)> UsedPairs =>
            Exercised.Select(g => (Math.Min(g.SchoolAId, g.SchoolBId), Math.Max(g.SchoolAId, g.SchoolBId)))
                     .ToList();

        public static readonly ContractSeasonOutcome None = new()
        {
            Load = ContractLoad.Empty(ContractLoadStatus.NoHistory),
            Exercised = Array.Empty<ContractedGame>(),
            Terminated = Array.Empty<ContractTermination>(),
            PolicyDeclined = Array.Empty<int>(),
            CapacityBlocked = Array.Empty<int>(),
            ForcedCapacityFailure = false,
            ForcedCapacityDetail = null,
            Survivors = Array.Empty<LiveContract>(),
            Charges = new Dictionary<int, ContractChargeSet>(),
        };
    }

    /// <summary>★ THE PURE SEASON STEP — pure over supplied state, which is what makes
    /// "the contract phase runs exactly once per season build" a property of the spine
    /// rather than a discipline: a second call with the same inputs is merely a second
    /// computation of the same answer, and mutates nothing.
    ///
    /// <para>Order (R23a, exactly): terminate same-conference → determine every
    /// forced leg → reserve against BOTH schools → validate globally → commit → only
    /// then evaluate optionals, ascending ContractId — a canonical order, because when
    /// two optionals want one last opening the result cannot be order-independent, so
    /// it must be canonical.</para></summary>
    /// <param name="sameLeague">Whether two schools are conference mates for the
    /// termination wall. Supplied, not derived, so the step stays pure.</param>
    /// <param name="openGamesOf">A school's total open games this season — the only
    /// hard bound (verified: S101's split is derived from what remains, never a cap a
    /// contract could violate).</param>
    /// <param name="choiceOverride">Injected explicit leg choice by contract id. An
    /// injection is an instruction: it selects the leg AND exercises the contract
    /// (capacity permitting) regardless of the placeholder policy — this is the seam a
    /// human-controlled program uses later with no restructuring.</param>
    /// <param name="wantsExercise">The optional policy, per contract id. Production
    /// passes the R8 constant; checks pass what the trajectory demands.</param>
    private static ContractSeasonOutcome ContractSeasonStep(
        ContractLoad load,
        Func<int, int, bool> sameLeague,
        Func<int, int> openGamesOf,
        IReadOnlyDictionary<int, int>? choiceOverride,
        Func<int, bool> wantsExercise)
    {
        if (load.Status != ContractLoadStatus.Loaded || load.Contracts.Count == 0)
            return new ContractSeasonOutcome
            {
                Load = load,
                Exercised = Array.Empty<ContractedGame>(),
                Terminated = Array.Empty<ContractTermination>(),
                PolicyDeclined = Array.Empty<int>(),
                CapacityBlocked = Array.Empty<int>(),
                ForcedCapacityFailure = false,
                ForcedCapacityDetail = null,
                Survivors = Array.Empty<LiveContract>(),
                Charges = new Dictionary<int, ContractChargeSet>(),
            };

        // ★ Canonical processing order everywhere below: ascending ContractId,
        //   independent of the enumeration order of the source collection.
        var live = load.Contracts.OrderBy(c => c.ContractId).ToList();

        // 1. ★ Same-conference termination, BEFORE any exercise (R24a) — so an
        //    equality-forced contract is never exercised through the wall. Two
        //    Independents are NOT league-mates: sameLeague is false for the
        //    conference that plays no league schedule.
        var terminated = new List<ContractTermination>();
        var alive = new List<LiveContract>();
        foreach (var c in live)
        {
            if (sameLeague(c.SchoolAId, c.SchoolBId))
                terminated.Add(new ContractTermination(c.ContractId, c.SchoolAId, c.SchoolBId, c.GamesRemaining));
            else alive.Add(c);
        }

        ContractLeg ChooseLeg(LiveContract c)
        {
            var outstanding = c.Legs.Where(l => !l.Completed).ToList();
            if (choiceOverride is not null && choiceOverride.TryGetValue(c.ContractId, out var legId))
            {
                var chosen = outstanding.FirstOrDefault(l => l.LegId == legId);
                if (chosen is null)
                    throw new InvalidOperationException(
                        $"CONTRACT CHOICE REFUSED: contract {c.ContractId.ToString(CultureInfo.InvariantCulture)} " +
                        $"has no outstanding leg {legId.ToString(CultureInfo.InvariantCulture)}.");
                return chosen;
            }
            // ★ The placeholder order (Emmett, r3): the executor's home leg, then
            //   neutral, then the away leg — a school reaches for the home date and
            //   travels when it must. Authored order, then leg id, breaks ties.
            //   Applies to EVERY exercised contract: forced status decides WHETHER,
            //   never HOW the leg is chosen.
            return outstanding
                .OrderBy(l => l.IsNeutral ? 1 : (l.HostId == c.ExecutorId ? 0 : 2))
                .ThenBy(l => l.Order).ThenBy(l => l.LegId)
                .First();
        }

        // 2. DISCOVER every forced contract and its chosen leg.
        var forced = alive.Where(c => c.GamesRemaining == c.WindowRemaining)
                          .Select(c => (Contract: c, Leg: ChooseLeg(c))).ToList();

        // 3. RESERVE the obligations against BOTH schools; 4. VALIDATE globally.
        var remaining = new Dictionary<int, int>();
        int Remaining(int schoolId) =>
            remaining.TryGetValue(schoolId, out var v) ? v : remaining[schoolId] = openGamesOf(schoolId);
        foreach (var (c, _) in forced)
        {
            remaining[c.SchoolAId] = Remaining(c.SchoolAId) - 1;
            remaining[c.SchoolBId] = Remaining(c.SchoolBId) - 1;
        }
        var overdrawn = remaining.Where(kv => kv.Value < 0).Select(kv => kv.Key).OrderBy(x => x).ToList();
        if (overdrawn.Count > 0)
        {
            // ★ ONE HARD WORLD-STATE FAILURE, NOTHING COMMITTED — and the transition
            //   FREEZES: survivors ride forward exactly as loaded, un-decremented,
            //   so the broken world cannot also manufacture a corrupt record.
            return new ContractSeasonOutcome
            {
                Load = load,
                Exercised = Array.Empty<ContractedGame>(),
                Terminated = terminated,
                PolicyDeclined = Array.Empty<int>(),
                CapacityBlocked = Array.Empty<int>(),
                ForcedCapacityFailure = true,
                ForcedCapacityDetail =
                    "forced contract obligations exceed open games for school id(s) " +
                    string.Join(", ", overdrawn.Select(x => x.ToString(CultureInfo.InvariantCulture))),
                Survivors = alive,
                Charges = new Dictionary<int, ContractChargeSet>(),
            };
        }

        // 5. COMMIT forced, then evaluate optionals in the same canonical order.
        var exercised = new List<(LiveContract Contract, ContractLeg Leg)>(forced);
        var policyDeclined = new List<int>();
        var capacityBlocked = new List<int>();
        foreach (var c in alive.Where(c => c.GamesRemaining < c.WindowRemaining))
        {
            var wants = (choiceOverride is not null && choiceOverride.ContainsKey(c.ContractId))
                        || wantsExercise(c.ContractId);
            if (!wants) { policyDeclined.Add(c.ContractId); continue; }
            if (Remaining(c.SchoolAId) <= 0 || Remaining(c.SchoolBId) <= 0)
            {
                // ★ A different diagnostic from the forced failure and from a policy
                //   decline: no exercise, no mutation, the contract stays live.
                capacityBlocked.Add(c.ContractId);
                continue;
            }
            remaining[c.SchoolAId] = Remaining(c.SchoolAId) - 1;
            remaining[c.SchoolBId] = Remaining(c.SchoolBId) - 1;
            exercised.Add((c, ChooseLeg(c)));
        }

        var games = exercised
            .OrderBy(x => x.Contract.ContractId)
            .Select(x => new ContractedGame(
                x.Contract.ContractId, x.Leg.LegId,
                x.Contract.SchoolAId, x.Contract.SchoolBId,
                x.Leg.IsNeutral, x.Leg.HostId))
            .ToList();

        // Charges by role, per school. Order of application inside the request
        // builder is hosted → neutral → away; counted here, applied there.
        var charges = new Dictionary<int, ContractChargeSet>();
        void Charge(int schoolId, int hosted, int away, int neutral)
        {
            var prior = charges.TryGetValue(schoolId, out var p) ? p : ContractChargeSet.None;
            charges[schoolId] = new ContractChargeSet(prior.Hosted + hosted, prior.Away + away, prior.Neutral + neutral);
        }
        foreach (var g in games)
        {
            if (g.IsNeutral) { Charge(g.SchoolAId, 0, 0, 1); Charge(g.SchoolBId, 0, 0, 1); }
            else
            {
                var host = g.HostId!.Value;
                var visitor = host == g.SchoolAId ? g.SchoolBId : g.SchoolAId;
                Charge(host, 1, 0, 0);
                Charge(visitor, 0, 1, 0);
            }
        }

        // 6. Rollover: completions applied to survivors, dead already removed,
        //    each surviving window decremented exactly once, completed omitted.
        var exercisedLegByContract = exercised.ToDictionary(x => x.Contract.ContractId, x => x.Leg.LegId);
        var survivors = RollContractsForward(alive, exercisedLegByContract);

        return new ContractSeasonOutcome
        {
            Load = load,
            Exercised = games,
            Terminated = terminated,
            PolicyDeclined = policyDeclined,
            CapacityBlocked = capacityBlocked,
            ForcedCapacityFailure = false,
            ForcedCapacityDetail = null,
            Survivors = survivors,
            Charges = charges,
        };
    }

    /// <summary>★ THE ROLLOVER, in the ruled order: the dead have ALREADY left (a
    /// terminated agreement no longer owns a window, so decrementing it would be
    /// meaningless mutation that muddies termination fixtures) → apply this season's
    /// completions to the survivors → decrement each surviving window exactly once →
    /// omit the completed. What returns is the next season's complete live
    /// collection.</summary>
    private static IReadOnlyList<LiveContract> RollContractsForward(
        IReadOnlyList<LiveContract> alive, IReadOnlyDictionary<int, int> exercisedLegByContract)
    {
        var survivors = new List<LiveContract>();
        foreach (var c in alive.OrderBy(c => c.ContractId))
        {
            var legs = c.Legs;
            if (exercisedLegByContract.TryGetValue(c.ContractId, out var legId))
                legs = legs.Select(l => l.LegId == legId ? l with { Completed = true } : l).ToList();
            var rolled = c with { Legs = legs, WindowRemaining = c.WindowRemaining - 1 };
            if (rolled.GamesRemaining == 0) continue;   // complete — never written forward
            survivors.Add(rolled);
        }
        return survivors;
    }

    // ── The production wrapper: the spine calls this once ───────────────────────────

    /// <summary>The spine's single entry point. Derives the two supplied facts —
    /// league walls and open games — from the world and the seating, then runs the
    /// pure step. Open games is S101's own arithmetic: 31 seated / 29 not, minus
    /// conference games, minus the three event games — the existing seam, no new
    /// event abstraction. An Independent (league games 0) opens at the full unseated
    /// season: its November belongs to a later arc session, but a contracted game
    /// bypasses matching entirely, so an Independent can honour one today.</summary>
    private static ContractSeasonOutcome RunContractSeason(
        WorldFile world, ContractLoad load, EventSeatingOutcome seating,
        IReadOnlyDictionary<int, int>? choiceOverride)
    {
        var confById = world.Conferences.ToDictionary(c => c.Id);
        var schoolById = world.Schools.ToDictionary(s => s.Id);
        // ★ S104 / A1 — THE SECOND HOME OF THE EXEMPTION, and it is easy to miss. This set
        //   feeds OpenGamesOf below, which is the contract phase's only hard bound. Filtered
        //   to TOURNAMENT seats for exactly the reason the request builder is: a showcase
        //   does not buy a 31-game season, so counting one here would let a contract overdraw
        //   a November that was never that big.
        var seated = seating.Active
            .Where(e => !e.IsShowcase)
            .SelectMany(e => e.Seats).Select(s => s.SchoolId).ToHashSet();
        // ★ A showcase pairing is materialized at seating — BEFORE this phase runs — so it is
        //   a fixed obligation the capacity gate must already see. Subtracting it here is what
        //   keeps "contracts charge first" a statement about BUCKETS rather than about who
        //   gets to overdraw whom.
        var showcaseGamesOf = MteShowcaseObligations(seating);

        bool SameLeague(int a, int b)
        {
            if (!schoolById.TryGetValue(a, out var sa) || !schoolById.TryGetValue(b, out var sb))
                throw new InvalidOperationException(
                    "CONTRACT INVARIANT VIOLATED: a live contract names a school this world does not have " +
                    "— the record binds to the career and a renamed world must be handled at load, not here.");
            if (sa.ConferenceId != sb.ConferenceId) return false;
            // ★ The games==0 conference is the Independent marker (the recorded R14
            //   convention), and two Independents are NOT league-mates — the wall
            //   exists because a league dictates its members' meetings, and that
            //   conference dictates nothing.
            return confById[sa.ConferenceId].Games > 0;
        }

        int OpenGamesOf(int schoolId)
        {
            var s = schoolById[schoolId];
            var isSeated = seated.Contains(schoolId);
            var seasonGames = isSeated ? NonConSeasonGamesSeated : NonConSeasonGamesUnseated;
            var eventGames = isSeated ? NonConEventGames : 0;
            return seasonGames - confById[s.ConferenceId].Games - eventGames
                   - showcaseGamesOf.GetValueOrDefault(schoolId, 0);
        }

        return ContractSeasonStep(load, SameLeague, OpenGamesOf, choiceOverride,
                                  _ => ContractOptionalPolicyExercise);
    }

    // ── The charging chains (Emmett's rulings, 2026-08-05) ──────────────────────────

    /// <summary>★ A leg is charged to its own bucket first; an empty bucket falls
    /// along a fixed chain that ends at the home band. Ruled directly: a hosted leg
    /// comes out of home; an away leg comes out of road, AND WHEN THE SCHOOL HAS NO
    /// ROAD GAMES, OUT OF HOME — Michigan State's trip to Duke costs it a home date,
    /// because that date was spent traveling instead of hosting. Chain tails beyond
    /// the ruled steps (a Claude call, flagged): Home → road → neutral; Away → home →
    /// neutral; Neutral → road → home. Application order hosted → neutral → away.
    /// The capacity gate guarantees total charges never exceed open games, so a
    /// charge with all three buckets empty is an invariant violation, not a
    /// case.</summary>
    private static (int Home, int Neutral, int Road) ApplyContractCharges(
        int home, int neutral, int road, ContractChargeSet charges)
    {
        void Take(ref int a, ref int b, ref int c, string what)
        {
            if (a > 0) a -= 1;
            else if (b > 0) b -= 1;
            else if (c > 0) c -= 1;
            else throw new InvalidOperationException(
                $"CONTRACT INVARIANT VIOLATED: a {what} leg has nothing to charge — the capacity " +
                "gate should have refused this world before the request builder ran.");
        }
        for (var i = 0; i < charges.Hosted; i++) Take(ref home, ref road, ref neutral, "hosted");
        for (var i = 0; i < charges.Neutral; i++) Take(ref neutral, ref road, ref home, "neutral");
        for (var i = 0; i < charges.Away; i++) Take(ref road, ref home, ref neutral, "away");
        return (home, neutral, road);
    }

    // ── The pairing log ─────────────────────────────────────────────────────────────

    /// <summary>One non-conference pairing, as the season record persists it: the
    /// pair (normalised lower-id-first), the site AS A WORD, the host when hosted,
    /// and the source — Matched or Contracted, one word that keeps a future repeat
    /// ceiling from demoting a pair a contract forces (a Claude call, flagged). The
    /// season is the record's own; the minimum persisted fact, per the ruling: a
    /// pair with no season is useless to both consumers, a copy of the whole game is
    /// waste.</summary>
    private sealed record NonConPairingEntry(int SchoolAId, int SchoolBId, bool IsNeutral, int? HostId, string Source);

    /// <summary>This season's pairing log: every exercised contract leg, then every
    /// matcher pair, each normalised. ★ APPEND-ONLY BY CONSTRUCTION — each season's
    /// record carries its own season's pairings and nothing ever rewrites a prior
    /// record. HONEST NAMING: these are games as PAIRED; non-conference games do not
    /// yet play (sites and nights are the next arc session), so this is the
    /// matcher's word, not a box score's.</summary>
    private static IReadOnlyList<NonConPairingEntry> BuildPairingLog(
        ContractSeasonOutcome contracts, MatchingReport matching)
    {
        var log = new List<NonConPairingEntry>();
        foreach (var g in contracts.Exercised)
            log.Add(new NonConPairingEntry(
                Math.Min(g.SchoolAId, g.SchoolBId), Math.Max(g.SchoolAId, g.SchoolBId),
                g.IsNeutral, g.IsNeutral ? null : g.HostId, "Contracted"));
        foreach (var p in matching.Pairs)
        {
            var neutral = p.Kind == "Neutral";
            log.Add(new NonConPairingEntry(
                Math.Min(p.HostSchoolId, p.VisitorSchoolId), Math.Max(p.HostSchoolId, p.VisitorSchoolId),
                neutral, neutral ? null : p.HostSchoolId, "Matched"));
        }
        return log;
    }

    // ── Serialization into the season record ────────────────────────────────────────

    private static void WriteLiveContracts(Utf8JsonWriter w, IReadOnlyList<LiveContract> contracts)
    {
        w.WriteStartArray("liveContracts");
        foreach (var c in contracts.OrderBy(c => c.ContractId))
        {
            w.WriteStartObject();
            w.WriteNumber("contractId", c.ContractId);
            w.WriteNumber("schoolAId", c.SchoolAId);
            w.WriteNumber("schoolBId", c.SchoolBId);
            w.WriteNumber("executorId", c.ExecutorId);
            w.WriteNumber("windowRemaining", c.WindowRemaining);
            w.WriteStartArray("legs");
            foreach (var l in c.Legs)   // authored order preserved as stored Order; list order is presentation
            {
                w.WriteStartObject();
                w.WriteNumber("legId", l.LegId);
                w.WriteNumber("order", l.Order);
                w.WriteString("site", l.IsNeutral ? "Neutral" : "Home");
                if (!l.IsNeutral) w.WriteNumber("hostId", l.HostId!.Value);
                w.WriteString("status", l.Completed ? "Completed" : "Outstanding");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteNonConferencePairings(Utf8JsonWriter w, IReadOnlyList<NonConPairingEntry> pairings)
    {
        w.WriteStartArray("nonConferencePairings");
        foreach (var p in pairings)
        {
            w.WriteStartObject();
            w.WriteNumber("schoolAId", p.SchoolAId);
            w.WriteNumber("schoolBId", p.SchoolBId);
            w.WriteString("site", p.IsNeutral ? "Neutral" : "Hosted");
            if (!p.IsNeutral) w.WriteNumber("hostId", p.HostId!.Value);
            w.WriteString("source", p.Source);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    // ── The page ────────────────────────────────────────────────────────────────────

    /// <summary>★ SILENT WHEN THERE IS NOTHING TO SAY — no heading on a legacy run, a
    /// first season, or a career whose collection is legitimately empty — which is
    /// what keeps every existing zero-path byte-identity claim honest. It speaks for
    /// exactly three reasons: contracts existed this season, the collection was LOST
    /// (R24: neither death is silent), or forced obligations exceeded capacity.</summary>
    private static IReadOnlyList<string> ContractPageLines(ContractSeasonOutcome o, WorldFile world)
    {
        var lost = o.Load.Status == ContractLoadStatus.CollectionLost;
        var any = o.Load.Contracts.Count > 0 || o.Terminated.Count > 0;
        if (!lost && !any && !o.ForcedCapacityFailure) return Array.Empty<string>();

        var nameOf = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        string Name(int id) => nameOf.TryGetValue(id, out var n) ? n : $"school {id.ToString(CultureInfo.InvariantCulture)}";
        var lines = new List<string> { "Contracts:" };
        if (lost)
        {
            lines.Add($"  LIVE CONTRACTS LOST — last season's record could not be read " +
                      $"({o.Load.Diagnostic}). The collection is dropped whole; no pairing in it can be named.");
            return lines;
        }
        if (o.ForcedCapacityFailure)
            lines.Add($"  FORCED OBLIGATIONS EXCEED CAPACITY — {o.ForcedCapacityDetail}. " +
                      "Nothing was exercised; the collection rides forward unchanged.");
        foreach (var g in o.Exercised)
        {
            var site = g.IsNeutral ? "neutral floor"
                : $"at {Name(g.HostId!.Value)}";
            var other = g.IsNeutral ? $"{Name(g.SchoolAId)} vs {Name(g.SchoolBId)}"
                : $"{Name(g.HostId == g.SchoolAId ? g.SchoolBId : g.SchoolAId)}";
            lines.Add(g.IsNeutral
                ? $"  exercised: {other}, {site} (contract {g.ContractId.ToString(CultureInfo.InvariantCulture)})"
                : $"  exercised: {other} {site} (contract {g.ContractId.ToString(CultureInfo.InvariantCulture)})");
        }
        foreach (var t in o.Terminated)
            lines.Add($"  TERMINATED — {Name(t.SchoolAId)} and {Name(t.SchoolBId)} are now conference " +
                      $"mates; {t.LegsLost.ToString(CultureInfo.InvariantCulture)} remaining leg(s) void " +
                      $"(contract {t.ContractId.ToString(CultureInfo.InvariantCulture)})");
        foreach (var id in o.CapacityBlocked)
            lines.Add($"  option blocked by capacity: contract {id.ToString(CultureInfo.InvariantCulture)} (stays live)");
        foreach (var id in o.PolicyDeclined)
            lines.Add($"  option declined: contract {id.ToString(CultureInfo.InvariantCulture)}");
        if (o.Survivors.Count > 0)
            lines.Add($"  carried forward: {o.Survivors.Count.ToString(CultureInfo.InvariantCulture)} live " +
                      $"contract(s), {o.Survivors.Sum(c => c.GamesRemaining).ToString(CultureInfo.InvariantCulture)} " +
                      "outstanding leg(s)");
        return lines;
    }
}
