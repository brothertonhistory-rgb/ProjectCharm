using System.Globalization;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S102 — THE MATCHING (non-conference arc, session 2 of 4).
//
//  Every school's November gets PAIRED: who plays whom, who hosts, which pairs
//  are neutral. NO DATE, NO CITY, NO SeasonGame, NO POSSESSION. Sites and
//  nights are arc session 3. The vocabulary is GAMES, REQUESTS and TOKENS.
//
//  ★ THE SPEC IS tools/matching_oracle.py. That file's docstring is the
//  authority; this is its port, and Phase 93 C14 proves the port pair for pair
//  and ledger field for ledger field against tools/matching_golden.json. Where
//  the two disagree the oracle is right.
//
//  ★ THE MATCHER CONSUMES S101's REPORT AND NEVER RECOMPUTES IT. Two
//  computations drift; one input cannot. It also never WRITES to it — the
//  remaining-counters below are the matcher's own, and Phase 93 C1 asserts the
//  input report is unchanged after the match.
//
//  ★ THE DISTANCE KEY IS QUANTIZED. Every ordering here breaks ties on
//  distance, and Python's and C#'s trig differ by ULPs (the S81.3 lesson, now
//  cross-language), so two NEARLY equal distances could sort differently in the
//  two languages and break parity for a reason that has nothing to do with the
//  policy. All ordering uses floor(miles + 0.5) — an integer, that exact
//  formula in both languages. Math.Round is NOT used: it is ties-to-even, which
//  is the wrong function and a silent trap for a future session.
//
//  ★ THE INDEPENDENTS ARE ABSENT FROM EVERY PHASE, including the terminal
//  partner pool. A school with no S101 request has no target, and a school with
//  no target cannot be handed an over-target game.
//
//  ★ COMPLETES-OR-REPORTS. Combinatorial infeasibility NEVER throws. The result
//  carries the completed pairing list, every unrepaired token with its owner,
//  and the full ledger — a structured shortfall (Phase 93 C13).
//
//  ★ R8 — SHIP WITH CONSTANTS, REWIRE LATER. The bucket mixes and the class
//  order live in the single block below, so the later rewire (coach temperament
//  owns scheduling philosophy — brief §5) is a substitution, not a hunt.
// ============================================================================

internal static partial class Program
{
    // ── R8: the one seam. Every tunable number of S102 lives here. ──────────────────

    /// <summary>Buckets name the KIND of opponent a home request wants, by that
    /// opponent's PRESTIGE — never by its class. Class carries S101's conference-tier
    /// floor; a bucket is about who the opponent actually is, so Northwestern at
    /// prestige 53 schedules as a Marquee school but FILLS somebody else's Working
    /// bucket. Serialized in FULL, never as one-letter codes, so a ledger or a parity
    /// diff stays readable next to "Neutral".</summary>
    private static readonly string[] MatchBucketNames = { "Easy", "Working", "Decent", "Name" };
    private const string MatchBucketAny = "Any";

    /// <summary>The kinds that have no bucket at all: Neutral, Filler, Terminal.</summary>
    private const string MatchBucketNone = "";

    /// <summary>★ EMMETT'S RULING (2026-08-05). Shares of a school's home games by
    /// bucket — (Easy, Working, Decent). Selling sends every home game to ANY and has
    /// no mix. Nothing here is a game count: §0's largest-remainder split turns shares
    /// into counts against whatever home number S101 handed the school.</summary>
    private static readonly Dictionary<string, int[]> MatchBucketMix = new()
    {
        ["Marquee"] = new[] { 5, 2, 1 },
        ["Solid"]   = new[] { 3, 2, 1 },
        ["Working"] = new[] { 2, 2, 0 },
    };

    /// <summary>★ R4 AS A PICK ORDER — the top of the country states its schedule and
    /// everyone else adapts around it.</summary>
    private static readonly string[] MatchClassTraversal =
        { "Marquee", "Solid", "Working", "Selling" };

    /// <summary>Terminal repair prefers the bottom, spilling up the class ladder —
    /// aligning the repair with C-37's spirit.</summary>
    private static readonly string[] MatchTerminalClassPreference =
        { "Selling", "Working", "Solid", "Marquee" };

    /// <summary>★ floor(miles + 0.5). NOT Math.Round, which is ties-to-even.</summary>
    private static int MatchDistanceKey(double miles) => (int)Math.Floor(miles + 0.5);

    /// <summary>0 Easy (&lt;25), 1 Working (25–54), 2 Decent (55–79), 3 Name (80+) —
    /// the same thresholds S101's class bands use, deliberately: a bucket and a class
    /// are the same read of prestige, and only the tier floor tells them apart.</summary>
    private static int MatchPrestigeBand(int prestige) =>
        prestige >= 80 ? 3 : prestige >= 55 ? 2 : prestige >= 25 ? 1 : 0;

    // ── The report ──────────────────────────────────────────────────────────────────

    /// <summary>One paired game. <c>Kind</c> is Hosted | Neutral | Filler | Terminal.
    /// For a Neutral pair nobody hosts, so the two ids are normalised lower-first and
    /// the Host/Visitor names are positional only — Phase 93 C2 asserts that
    /// normalisation rather than assuming it.
    ///
    /// <para>★ PER-PAIR PROVENANCE IS CARRIED, not reconstructed: C7 and C11 cannot be
    /// derived from bare pairs. <c>OriginBucket</c> is where the request started,
    /// <c>FilledBucket</c> where it landed, and they differ exactly when the request
    /// spilled.</para></summary>
    private sealed record MatchPair(
        string Kind, int HostSchoolId, int VisitorSchoolId, int DistanceKey,
        string OriginBucket, string FilledBucket, bool WasSpill, bool WasConvertedNeutral);

    /// <summary>One school's disposition. ★ THE ANNOTATIONS RECORD PROVENANCE AND NEVER
    /// REPLACE A ROLE COUNT: a neutral token that converts and then fills as a home game
    /// increments BOTH <c>MatchedHome</c> and <c>ConvertedNeutralToHome</c>, and a
    /// spilled request still counts in <c>MatchedHome</c>. Adding an annotation into the
    /// total would double-count it, which is why <see cref="PairedTotal"/> names its
    /// terms explicitly.</summary>
    private sealed class MatchLedgerRow
    {
        public required int SchoolId { get; init; }
        public required string SchoolName { get; init; }
        public required string ClassName { get; init; }
        public required int RequestedHome { get; init; }
        public required int RequestedNeutral { get; init; }
        public required int RequestedRoad { get; init; }
        public int MatchedHome { get; set; }
        public int MatchedNeutral { get; set; }
        /// <summary>Games this school travelled to and did not host — hosted, filler and
        /// terminal visits alike.</summary>
        public int MatchedRoadAsVisitor { get; set; }
        /// <summary>★ Site-mix conversions. A filler host is ON TARGET, never over it:
        /// the game was already one of its road tokens.</summary>
        public int FillerHosted { get; set; }
        public int TerminalExtra { get; set; }
        public int ShortUnrepaired { get; set; }
        public int ConvertedNeutralToHome { get; set; }
        public int SpilledRequests { get; set; }

        public int PairedTotal => MatchedHome + MatchedNeutral + MatchedRoadAsVisitor
                                + FillerHosted + TerminalExtra;
    }

    /// <summary>Everything S102 decides, in one object. Page-only cargo: the page renders
    /// what this contains and nothing else, and Phase 93 asserts this object rather than
    /// rendered prose.</summary>
    private sealed class MatchingReport
    {
        public required IReadOnlyList<MatchPair> Pairs { get; init; }
        public required IReadOnlyList<MatchLedgerRow> Ledger { get; init; }
        /// <summary>Short tokens that found no partner anywhere — reported, never thrown.
        /// One entry per token, so a school short by two appears twice.</summary>
        public required IReadOnlyList<int> Unrepaired { get; init; }
        public required int SpilledRequests { get; init; }
        public required int ConvertedNeutrals { get; init; }

        public int CountOfKind(string kind) => Pairs.Count(p => p.Kind == kind);

        public static readonly MatchingReport Empty = new()
        {
            Pairs = Array.Empty<MatchPair>(),
            Ledger = Array.Empty<MatchLedgerRow>(),
            Unrepaired = Array.Empty<int>(),
            SpilledRequests = 0, ConvertedNeutrals = 0,
        };
    }

    // ── The computation ─────────────────────────────────────────────────────────────

    /// <summary>★ §4.0 — largest remainder, TIES TO THE LOWER BUCKET. Marquee home 9
    /// splits 6 Easy / 2 Working / 1 Decent; home 6 splits 4/1/1, because Easy and
    /// Decent tie at .75 and Easy is the lower bucket. Phase 93 C6 asserts both
    /// sequences literally.</summary>
    private static int[] MatchAllocate(int home, int[] shares)
    {
        var total = shares.Sum();
        var exact = shares.Select(s => home * (double)s / total).ToArray();
        var counts = exact.Select(e => (int)Math.Floor(e)).ToArray();
        var remaining = home - counts.Sum();
        var order = Enumerable.Range(0, shares.Length)
            .OrderByDescending(i => exact[i] - counts[i])
            .ThenBy(i => i)
            .ToArray();
        for (var k = 0; k < remaining; k++) counts[order[k]]++;
        return counts;
    }

    /// <summary>★ S102's single entry point. PURE AND TOTAL: same world + same report in,
    /// same matching out, no randomness, no clock, no config, and the report is read
    /// rather than written. Called from RunSeasonCore immediately after
    /// BuildNonConferenceRequests; the result rides out on SeasonRunOutcome and reaches
    /// nothing but the page and Phase 93.</summary>
    private static MatchingReport BuildNonConferenceMatching(
        WorldFile world, NonConferenceReport report)
    {
        var schoolById = world.Schools.ToDictionary(s => s.Id);
        var placeById = world.Places.ToDictionary(p => p.PlaceId);
        var reqById = report.Schools.ToDictionary(r => r.SchoolId);

        // The pool: every school with an S101 request. The Independents are not here
        // and therefore cannot be picked, cannot host, and cannot be a terminal partner.
        var ids = report.Targeted.Select(r => r.SchoolId).OrderBy(i => i).ToList();
        if (ids.Count == 0) return MatchingReport.Empty;

        var prestige = ids.ToDictionary(i => i, i => schoolById[i].CurrentPrestige);
        var conference = ids.ToDictionary(i => i, i => schoolById[i].ConferenceId);
        var classOf = ids.ToDictionary(i => i, i => reqById[i].ClassName);

        // DistanceKey for every ordered pair, computed once against the S92 ruler.
        var dk = new Dictionary<(int, int), int>();
        for (var a = 0; a < ids.Count; a++)
            for (var b = a + 1; b < ids.Count; b++)
            {
                var x = ids[a];
                var y = ids[b];
                var key = MatchDistanceKey(GeoDistance.DistanceMiles(
                    placeById[schoolById[x].PlaceId].Coordinate,
                    placeById[schoolById[y].PlaceId].Coordinate));
                dk[(x, y)] = key;
                dk[(y, x)] = key;
            }

        // ★ THE MATCHER'S OWN COUNTERS. The input report is never touched.
        var road = ids.ToDictionary(i => i, i => reqById[i].Road);
        var neutral = ids.ToDictionary(i => i, i => reqById[i].Neutral);
        var usedPairs = new HashSet<(int, int)>();

        var pairs = new List<MatchPair>();
        var ledger = ids.ToDictionary(i => i, i => new MatchLedgerRow
        {
            SchoolId = i,
            SchoolName = reqById[i].SchoolName,
            ClassName = reqById[i].ClassName,
            RequestedHome = reqById[i].Home,
            RequestedNeutral = reqById[i].Neutral,
            RequestedRoad = reqById[i].Road,
        });
        var unrepaired = new List<int>();
        var shortTokens = new List<int>();
        var spills = 0;
        var convertedNeutrals = 0;

        // ── The five legality tests, in every phase ────────────────────────────────
        bool Legal(int a, int b) =>
            a != b
            && conference[a] != conference[b]
            && !usedPairs.Contains((Math.Min(a, b), Math.Max(a, b)));

        void Take(int host, int visitor, string kind, string origin, string filled,
                  bool wasSpill, bool wasConvertedNeutral)
        {
            usedPairs.Add((Math.Min(host, visitor), Math.Max(host, visitor)));
            // ★ A neutral pair has no host; the two ids are normalised lower-first and
            //   the positions carry no hosting meaning (C2 asserts this).
            var (h, v) = kind == "Neutral"
                ? (Math.Min(host, visitor), Math.Max(host, visitor))
                : (host, visitor);
            pairs.Add(new MatchPair(kind, h, v, dk[(host, visitor)], origin, filled,
                                    wasSpill, wasConvertedNeutral));
        }

        int? PickRoadCandidate(int host, int? band)
        {
            int? best = null;
            (int, int, int) bestKey = default;
            foreach (var c in ids)
            {
                if (road[c] <= 0 || !Legal(host, c)) continue;
                if (band is int b && MatchPrestigeBand(prestige[c]) != b) continue;
                var key = (dk[(host, c)], prestige[c], c);
                if (best is null || key.CompareTo(bestKey) < 0) { bestKey = key; best = c; }
            }
            return best;
        }

        // One home request. originBand null = ANY, which never spills. Returns false
        // when the whole ladder holds no legal candidate — that is a short token.
        bool FillHomeRequest(int host, int? originBand)
        {
            var ladder = originBand is null
                ? new int?[] { null }
                : Enumerable.Range(originBand.Value, 4 - originBand.Value)
                            .Select(b => (int?)b).ToArray();
            foreach (var band in ladder)
            {
                var c = PickRoadCandidate(host, band);
                if (c is null) continue;
                road[c.Value]--;
                var spilled = originBand is not null && band != originBand;
                Take(host, c.Value, "Hosted",
                     originBand is null ? MatchBucketAny : MatchBucketNames[originBand.Value],
                     band is null ? MatchBucketAny : MatchBucketNames[band.Value],
                     spilled, false);
                ledger[host].MatchedHome++;
                ledger[c.Value].MatchedRoadAsVisitor++;
                if (spilled) { spills++; ledger[host].SpilledRequests++; }
                return true;
            }
            return false;
        }

        // ══ 1. TOP-DOWN HOME FILL ═════════════════════════════════════════════════
        var classRank = MatchClassTraversal
            .Select((name, i) => (name, i)).ToDictionary(t => t.name, t => t.i);
        var traversal = ids
            .OrderBy(i => classRank[classOf[i]])
            .ThenByDescending(i => prestige[i])
            .ThenBy(i => i)
            .ToList();

        foreach (var host in traversal)
        {
            var r = reqById[host];
            var requests = new List<int?>();
            if (classOf[host] == "Selling")
            {
                for (var k = 0; k < r.Home; k++) requests.Add(null);
            }
            else
            {
                var counts = MatchAllocate(r.Home, MatchBucketMix[classOf[host]]);
                for (var band = 0; band < 3; band++)
                    for (var k = 0; k < counts[band]; k++) requests.Add(band);
            }
            foreach (var originBand in requests)
                if (!FillHomeRequest(host, originBand)) shortTokens.Add(host);
        }

        // ══ 2. NEUTRAL PAIRING ════════════════════════════════════════════════════
        var neutralOrder = ids.Where(i => neutral[i] > 0)
            .OrderByDescending(i => prestige[i]).ThenBy(i => i).ToList();
        foreach (var a in neutralOrder)
        {
            while (neutral[a] > 0)
            {
                int? best = null;
                (int, int, int) bestKey = default;
                foreach (var c in ids)
                {
                    if (neutral[c] <= 0 || !Legal(a, c)) continue;
                    var key = (Math.Abs(prestige[c] - prestige[a]), dk[(a, c)], c);
                    if (best is null || key.CompareTo(bestKey) < 0) { bestKey = key; best = c; }
                }
                if (best is not null)
                {
                    neutral[a]--;
                    neutral[best.Value]--;
                    Take(a, best.Value, "Neutral", MatchBucketNone, MatchBucketNone, false, false);
                    ledger[a].MatchedNeutral++;
                    ledger[best.Value].MatchedNeutral++;
                    continue;
                }
                // ★ No partner — the odd national count, or a legality dead end. The
                //   token CONVERTS to one ANY home request and runs step 1's pick
                //   immediately. Nothing is discarded.
                neutral[a]--;
                convertedNeutrals++;
                ledger[a].ConvertedNeutralToHome++;
                if (FillHomeRequest(a, null))
                    pairs[^1] = pairs[^1] with { WasConvertedNeutral = true };
                else
                    shortTokens.Add(a);
            }
        }

        // ══ 3. BOTTOM HOSTS BOTTOM (C-37) ═════════════════════════════════════════
        while (true)
        {
            var pool = ids.Where(i => road[i] > 0)
                .OrderBy(i => prestige[i]).ThenBy(i => i).ToList();
            if (pool.Count == 0) break;
            var a = pool[0];

            int? best = null;
            (int, int, int) bestKey = default;
            foreach (var c in ids)
            {
                if (road[c] <= 0 || !Legal(a, c)) continue;
                var key = (dk[(a, c)], prestige[c], c);
                if (best is null || key.CompareTo(bestKey) < 0) { bestKey = key; best = c; }
            }
            if (best is null)
            {
                // Every remaining road game of a's becomes a short token; a leaves.
                for (var k = 0; k < road[a]; k++) shortTokens.Add(a);
                road[a] = 0;
                continue;
            }

            // ★ THE HOST RULE: the LOWER PRESTIGE school hosts; EQUAL prestige, the
            //   LOWER ID hosts. `a` satisfies it by the pool's order, but the rule is
            //   the rule — written out here, and C11 asserts it in these same words.
            var b2 = best.Value;
            var (host, visitor) =
                (prestige[a], a).CompareTo((prestige[b2], b2)) < 0 ? (a, b2) : (b2, a);
            road[a]--;
            road[b2]--;
            // ★ A FILLER GAME CHANGES A SCHOOL'S SITE MIX, NEVER ITS GAME COUNT.
            Take(host, visitor, "Filler", MatchBucketNone, MatchBucketNone, false, false);
            ledger[host].FillerHosted++;
            ledger[visitor].MatchedRoadAsVisitor++;
        }

        // ══ 4. TERMINAL REPAIR ════════════════════════════════════════════════════
        var terminalUsed = new HashSet<int>();
        foreach (var owner in shortTokens
                     .OrderByDescending(i => prestige[i]).ThenBy(i => i))
        {
            int? partner = null;
            foreach (var cls in MatchTerminalClassPreference)
            {
                int? best = null;
                (int, int, int) bestKey = default;
                foreach (var c in ids)
                {
                    if (terminalUsed.Contains(c) || classOf[c] != cls) continue;
                    if (!Legal(owner, c)) continue;
                    var key = (dk[(owner, c)], prestige[c], c);
                    if (best is null || key.CompareTo(bestKey) < 0) { bestKey = key; best = c; }
                }
                if (best is not null) { partner = best; break; }
            }
            if (partner is null)
            {
                unrepaired.Add(owner);
                ledger[owner].ShortUnrepaired++;
                continue;
            }
            // ★ THE PARTNER HOSTS and exceeds its own target by EXACTLY ONE GAME, and a
            //   partner is used AT MOST ONCE, so the repair can never pile onto one school.
            terminalUsed.Add(partner.Value);
            Take(partner.Value, owner, "Terminal", MatchBucketNone, MatchBucketNone, false, false);
            ledger[partner.Value].TerminalExtra++;
            ledger[owner].MatchedRoadAsVisitor++;
        }

        return new MatchingReport
        {
            Pairs = pairs,
            Ledger = ids.Select(i => ledger[i]).ToList(),
            Unrepaired = unrepaired,
            SpilledRequests = spills,
            ConvertedNeutrals = convertedNeutrals,
        };
    }

    // ── The page ────────────────────────────────────────────────────────────────────

    /// <summary>★ p90 is NEAREST RANK — sorted sample of size n, index ⌈0.9n⌉−1. The
    /// median is the LOWER MIDDLE of an even sample. Both display as the integer miles
    /// they are; an empty sample renders "n/a". Defined here once so the page and the
    /// oracle cannot drift.</summary>
    private static string MatchPercentile(IReadOnlyList<int> sorted, double q)
    {
        if (sorted.Count == 0) return "n/a";
        var index = q >= 1.0
            ? sorted.Count - 1
            : (int)Math.Ceiling(q * sorted.Count) - 1;
        if (index < 0) index = 0;
        return sorted[index].ToString(CultureInfo.InvariantCulture);
    }

    private static string MatchMedian(IReadOnlyList<int> sorted)
        => sorted.Count == 0 ? "n/a"
         : sorted[(sorted.Count - 1) / 2].ToString(CultureInfo.InvariantCulture);

    /// <summary>★ PAGE-ONLY, and every number derives from the report — the page cannot
    /// print a total it did not carry. Renders validly on any world: zero targeted
    /// schools, either gap sign, missing named schools, an empty sample.</summary>
    private static List<string> MatchingPageLines(MatchingReport m)
    {
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "--- NON-CONFERENCE MATCHING (S102: every November in the country pairs — who " +
            "plays whom and who hosts; no site and no night until the next session) ---",
        };
        if (m.Pairs.Count == 0 && m.Ledger.Count == 0)
        {
            lines.Add("  No school in this world holds a non-conference request.");
            return lines;
        }

        var nameById = m.Ledger.ToDictionary(l => l.SchoolId, l => l.SchoolName);
        string Name(int id) => nameById.TryGetValue(id, out var n) ? n : $"school {id}";

        // ── (1) The matched country ────────────────────────────────────────────────
        var hosted = m.CountOfKind("Hosted");
        var neutral = m.CountOfKind("Neutral");
        var filler = m.CountOfKind("Filler");
        var terminal = m.CountOfKind("Terminal");
        lines.Add($"  {m.Pairs.Count} games paired: {hosted} at a host's gym, {neutral} " +
                  $"neutral, {filler} filled by the bottom hosting the bottom, {terminal} " +
                  $"terminal repairs.");
        lines.Add($"  {m.SpilledRequests} home requests settled for a better opponent than " +
                  $"they asked for; {m.ConvertedNeutrals} neutral game(s) found no partner " +
                  "and became an ordinary home game.");

        var over = m.Ledger.Where(l => l.TerminalExtra > 0).ToList();
        var onTarget = m.Ledger.Count(l => l.TerminalExtra == 0 && l.ShortUnrepaired == 0);
        lines.Add($"  {onTarget} school(s) land exactly on their target; {over.Count} play " +
                  "one game more than they asked for" +
                  (over.Count > 0
                      ? " (" + string.Join(", ", over.Select(l => l.SchoolName)) + ")"
                      : "") + "; " +
                  $"{m.Unrepaired.Count} request(s) found nobody at all" +
                  (m.Unrepaired.Count > 0
                      ? " (" + string.Join(", ", m.Unrepaired.Select(Name)) + ")"
                      : "") + ".");

        // ── (2) The tilt ───────────────────────────────────────────────────────────
        //    Neutral pairs are EXCLUDED: no site exists until session 3, so there is no
        //    trip to measure.
        var trips = m.Pairs.Where(p => p.Kind is "Hosted" or "Terminal")
            .Select(p => p.DistanceKey).OrderBy(d => d).ToList();
        var fillerTrips = m.Pairs.Where(p => p.Kind == "Filler")
            .Select(p => p.DistanceKey).OrderBy(d => d).ToList();
        lines.Add($"  The visitor's trip: median {MatchMedian(trips)} mi, p90 " +
                  $"{MatchPercentile(trips, 0.9)} mi over {trips.Count} game(s); the " +
                  $"bottom-hosts-bottom games run {MatchMedian(fillerTrips)} mi at the " +
                  $"median over {fillerTrips.Count}.");

        // ── ★ THE AXIS C-40 IS ABOUT: how far does a school ITSELF travel? The ruling
        //    says a power school flies and a small school buses. Broken out by the
        //    TRAVELLING school's class so it cannot hide inside the national median.
        var classOf = m.Ledger.ToDictionary(l => l.SchoolId, l => l.ClassName);
        var byClass = new Dictionary<string, List<int>>();
        foreach (var p in m.Pairs)
        {
            if (p.Kind == "Neutral") continue;
            if (!classOf.TryGetValue(p.VisitorSchoolId, out var cls)) continue;
            if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = new List<int>();
            list.Add(p.DistanceKey);
        }
        lines.Add("  road trips taken, by the travelling school's class (the geographic " +
                  "tilt — measured, never asserted):");
        foreach (var cls in MatchClassTraversal)
        {
            if (!byClass.TryGetValue(cls, out var v) || v.Count == 0) continue;
            v.Sort();
            lines.Add(string.Format(inv, "    {0,-9}{1,4} trip(s)   median {2,5} mi   p90 {3,6} mi",
                cls, v.Count, MatchMedian(v), MatchPercentile(v, 0.9)));
        }

        // ── (3) The named eight's full Novembers ───────────────────────────────────
        var named = NonConPageNamedSchools
            .Select(n => m.Ledger.FirstOrDefault(l => l.SchoolName == n))
            .Where(l => l is not null).Cast<MatchLedgerRow>().ToList();
        if (named.Count > 0)
        {
            lines.Add("  the named schools' Novembers (opponents grouped by kind, then by id):");
            foreach (var l in named)
            {
                var mine = m.Pairs
                    .Where(p => p.HostSchoolId == l.SchoolId || p.VisitorSchoolId == l.SchoolId)
                    .ToList();
                string Group(string label, Func<MatchPair, bool> pick)
                {
                    var opponents = mine.Where(pick)
                        .Select(p => p.HostSchoolId == l.SchoolId ? p.VisitorSchoolId : p.HostSchoolId)
                        .OrderBy(i => i).Select(Name).ToList();
                    return opponents.Count == 0 ? "" : $"{label}: {string.Join(", ", opponents)}";
                }
                var parts = new[]
                {
                    Group("home", p => p.Kind == "Hosted" && p.HostSchoolId == l.SchoolId),
                    Group("neutral", p => p.Kind == "Neutral"),
                    Group("filler-hosted", p => p.Kind == "Filler" && p.HostSchoolId == l.SchoolId),
                    Group("terminal", p => p.Kind == "Terminal" && p.HostSchoolId == l.SchoolId),
                    Group("road", p => p.Kind != "Neutral" && p.VisitorSchoolId == l.SchoolId),
                }.Where(s => s.Length > 0);
                lines.Add($"    {l.SchoolName} ({l.ClassName}) — {string.Join(" | ", parts)}");
            }
        }

        // ── (4) ★ EVERY FILLER PAIR WITH ITS MILEAGE, grouped by host. ~175 lines on
        //    the stock world, DELIBERATELY: this is the surface Emmett judges C-37 from,
        //    and it prints once per season run.
        var fillerPairs = m.Pairs.Where(p => p.Kind == "Filler")
            .OrderBy(p => p.HostSchoolId).ThenBy(p => p.VisitorSchoolId).ToList();
        if (fillerPairs.Count > 0)
        {
            lines.Add($"  bottom hosts bottom — all {fillerPairs.Count} of them, by host " +
                      "(both schools wanted the road; the lower school eats the home game):");
            foreach (var p in fillerPairs)
                lines.Add(string.Format(inv, "    {0,-26} hosts {1,-26} {2,6} mi",
                    Trim(Name(p.HostSchoolId), 26), Trim(Name(p.VisitorSchoolId), 26),
                    p.DistanceKey));
        }

        return lines;
    }
}
