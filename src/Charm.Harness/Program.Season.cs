using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
// Season — the season loop, and as of S93 the CONFERENCE SLATE.
//
// A HARNESS-ONLY layer (no engine file changes). `season <world.json> <seed>`
// regenerates every school's divvied roster (world + seed — nothing persisted),
// builds the conference schedule, plays every game through the real engine via
// the extracted single-game body, and prints the standings page.
//
// ★ S93 — WHAT A SEASON IS NOW (Emmett, 2026-08-02): "we don't care about a
// 'season' right now, we care about games being scheduled." A team plays its
// own conference's authored number of games and NOTHING ELSE. The pre-S93
// non-conference filler — a flat 14-regular ring circulant with conflict repair
// — is DELETED, not disabled, and non-conference scheduling starts from nothing
// in its own future session.
//
// Three consequences, all honest, none of them bugs:
//   * A team plays 14, 16, 18 or 20 games, not 30. Its league says which.
//   * The fourteen Independent schools play ZERO games. Their conference is
//     authored at Games = 0 (R14), so they carry rosters and no record.
//   * ★ THE SCHEDULE CONSUMES NO RANDOMNESS. All of the old builder's RNG lived
//     in the filler, so the slate is now a pure function of the world file and
//     is IDENTICAL AT EVERY SEED. The seed still drives every outcome. What it
//     no longer does is decide who plays whom — which means the same pairs are
//     doubled and the same pairs skipped every season forever. Session 94's
//     host memory is the answer to that; it is recorded here, not solved here.
//
// THE SCHEDULE CONTRACT (mirrored by the Python oracle,
// tools/schedule_oracle.py — the oracle's docstring is the authoritative spec;
// this header restates it):
//
//   CONFERENCE SLATES (no RNG): conferences by id ascending, members by school
//   id ascending indexed 0..n-1. For Games = G and Skip = k: p = n-1-k played
//   opponents, q = G/p meetings each, r = G mod p opponents bumped to q+1, and
//   k opponents skipped entirely. Construction order inside a league is
//   load-bearing: resolve the active rivalries, build the r-regular EXTRA graph
//   so it CONTAINS the rivalry matching, build the k-regular SKIPPED graph on
//   the complement, everything else meets q times, orient. When k = 0 and no
//   rivalry constrains it the extra graph is the canonical circulant the
//   pre-S93 builder used — pinned deliberately, so an unchanged league's pair
//   multiset is reproducible against the old schedule. Otherwise an exhaustive
//   backtracking search finds it.
//
//   ORIENTATION (no RNG): R3 is a HARD LINE — every team plays an exactly even
//   home/away conference season, G/2 each. A pair meeting m times splits
//   floor(m/2) each way by construction, alternating from the lower school id;
//   only an ODD m leaves one game to decide, the RESIDUAL. Residuals go to an
//   integral flow with exact home quotas, which exists because it can honour
//   PRE-FIXED VENUES — a Eulerian walk cannot, and would land the same even
//   split while proving nothing.
//
//   FINGERPRINT: one record per game in schedule order (never re-sorted):
//   "{gameIndex}|{kind}|{homeSchoolId}|{awaySchoolId}\n", kind always conf
//   today, UTF-8, SHA-256, lowercase hex. Printed on the page; asserted by
//   Phase 55 against the oracle export.
//
//   ENGINE SEEDS (no RNG; uniqueness asserted in Phase 55): base =
//   unchecked((int)seasonSeed) (the smoke sim's pattern); resolver =
//   base + 2*gameIndex, governor = base + 2*gameIndex + 1. Distinct within a
//   season by construction; the stride-2 scheme also keeps resolver and
//   governor seed sets disjoint.
//
// THE HONEST WALL: the schedule is oracle-proven; game OUTCOMES are not
// oracle-mirrorable (SystemRng) — they are proven by harness invariants
// (conservation, determinism, completeness) in Phase 55. The prestige-vs-wins
// climb is a page-level finding, never a suite assertion.
// ============================================================================

internal static partial class Program
{

    // ★ S89 — the two identity fields are NULLABLE, and they are nullable for exactly one
    // reason: legacy mode. Run without a history and there is no career to belong to, so
    // the honest value is "absent" — never a zero, never a made-up number. Run WITH a
    // history and both are always present; that is validated once, at the top, rather than
    // by every reader downstream checking again.
    private sealed record SeasonGame(string Kind, int HomeId, int AwayId,
                                     SeasonId? SeasonId = null, GameId? GameId = null);

    private sealed record SeasonGameResult(
        int HomeId, int AwayId, int HomeScore, int AwayScore, int OvertimePeriods,
        SeasonId? SeasonId = null, GameId? GameId = null);

    private sealed class SeasonRunOutcome
    {
        public required List<SeasonGame> Schedule { get; init; }
        public required string Fingerprint { get; init; }
        public required List<SeasonGameResult> Results { get; init; }
        public required Dictionary<int, int> Wins { get; init; }
        public required Dictionary<int, int> Losses { get; init; }
        public required DivvyResult Divvy { get; init; }
        // Session 31: the calibration instrument's league-wide accumulator — fed once
        // per game inside RunSeasonCore; read by the page readout and Phase 55 §3.8.
        public required SeasonLeagueStats League { get; init; }
        public int Ties { get; init; }
    }

    /// <summary>Everything the two accumulators need to turn a stamped player id back into a
    /// PERSON, for one game. Passed rather than recomputed: the season loop already holds all
    /// six pieces at the call site and used to throw four of them away one line early.
    ///
    /// <para>★ S77 — why this is a bundle and not two ints. `Accumulate` needs the school to
    /// file a box line; `NoteOccupancy` needs it too, for floor time and games played, and had
    /// no way to know it. The S77 prompt's wall permitted one changed line, at `Accumulate`'s
    /// call site, but its own Gates 3 and 4 require per-player MINUTES and GAMES PLAYED, which
    /// are produced by the occupancy walk. The gates win; both call sites take the bundle.</para>
    ///
    /// <para>The rows are the season's own per-school tables, built ONCE before the game loop
    /// and never rebuilt (A1). The sides are the stamped copies actually handed to the engine —
    /// `StampPlayerId` returns `new Player(p.Name)`, so a stamped man and his row's man are
    /// distinct objects carrying the same name. That is what makes the Gate 2 identity check two
    /// independent paths rather than one path checked against itself.</para></summary>
    private sealed record SeasonGameIdentity(
        int HomeSchoolId, int AwaySchoolId,
        List<GenPlayerRow> HomeRows, List<GenPlayerRow> AwayRows,
        GenSideData HomeSide, GenSideData AwaySide)
    {
        /// <summary>Stamped id -> the school he plays for and the season row that IS him.
        /// Home ids are `1..Size`, away `Size+1..2*Size` (RosterShape). This is the one place
        /// the offset arithmetic lives for the stat layer; the roll-up and the identity gate
        /// both come through here, so a mapping error cannot disagree with itself.</summary>
        public (int SchoolId, GenPlayerRow Row) Resolve(int stampedId)
        {
            if (!RosterShape.IsLegalPlayerId(stampedId))
                throw new InvalidOperationException(
                    $"S77 identity: stamped id {stampedId} is outside 1..{RosterShape.MaxPlayerId}.");
            var isHome = stampedId <= RosterShape.Size;
            var index  = isHome ? stampedId : stampedId - RosterShape.AwayIdOffset;
            var rows   = isHome ? HomeRows : AwayRows;
            return (isHome ? HomeSchoolId : AwaySchoolId, rows[index - 1]);
        }

        /// <summary>Every stamped player handed to this game — all 26, starters AND reserves.
        /// ★ NOT read off `Roster`: a Roster knows only the five seats it is holding, so a man
        /// who never checks in is not there at all. Gate 2 checks all 26 unconditionally, which
        /// is only possible from the side data the roster is seated FROM.</summary>
        public IEnumerable<Player> StampedPlayers()
        {
            foreach (var p in HomeSide.Starters) yield return p;
            foreach (var p in HomeSide.Reserves) yield return p;
            foreach (var p in AwaySide.Starters) yield return p;
            foreach (var p in AwaySide.Reserves) yield return p;
        }
    }

    /// <summary>Assert, ONCE before the season loop, that a name identifies exactly one man on a
    /// roster, and a pool id exactly one man in the league. Gate 2 compares names across two
    /// paths; that comparison is only evidence if names are unique, so the uniqueness is proven
    /// here rather than argued. Pool ids are the season record's KEY, so a duplicate would merge
    /// two men's careers silently — the loudest possible failure is the cheapest one.</summary>
    private static void AssertSeasonIdentitiesDistinct(Dictionary<int, List<GenPlayerRow>> rowsBySchool)
    {
        var seenPool = new Dictionary<int, int>();
        foreach (var (schoolId, rows) in rowsBySchool.OrderBy(kv => kv.Key))
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                if (!names.Add(r.Player.Name))
                    throw new InvalidOperationException(
                        $"S77 identity: school {schoolId} carries two men named '{r.Player.Name}'. " +
                        "Gate 2 compares names across two paths and cannot tell them apart.");
                if (seenPool.TryGetValue(r.PoolId, out var other))
                    throw new InvalidOperationException(
                        $"S77 identity: pool id {r.PoolId} is on school {schoolId} AND school {other}. " +
                        "The season record is keyed by pool id; one man cannot hold two rosters.");
                seenPool[r.PoolId] = schoolId;
            }
        }
    }

    // =========================================================================================
    //  THE CONFERENCE SLATE (Session 93)
    //
    //  Locked contract: tools/schedule_oracle.py. That file is the spec; this is the port.
    //  ★ NO RANDOMNESS ENTERS THE SCHEDULE. The pre-S93 builder's only RNG lived in the
    //  non-conference filler, which is deleted. The slate is a pure function of the world
    //  file, so the same world produces the same schedule at every seed. The seed still
    //  drives every game's outcome; it no longer decides who plays whom. Recorded because it
    //  is a real basketball gap — the same pairs double and the same pairs skip every season
    //  forever — and Session 94's host memory is the answer to it.
    // =========================================================================================

    /// <summary>The four verdicts, kept strictly separate and never collapsed into each other.
    /// Confusing the third for the second is the exact dishonesty this type exists to prevent:
    /// "I did not find one" is not "there is none".</summary>
    private enum SlateVerdict
    {
        Feasible,
        InvalidConfiguration,
        InfeasibleUnderConstraints,
        SearchBudgetExhausted,
        UnsupportedConferenceSize,
    }

    /// <summary>★ THE SESSION 94 SEAM, BUILT NOW AND LEFT EMPTY. A decided residual: for the
    /// pair (Low, High), this school hosts. Legal only where the pair meets an ODD number of
    /// times — an even meeting count splits itself and never reaches the flow. In production
    /// the set is ALWAYS empty today because there is no memory of who hosted last time; it is
    /// non-empty only inside Phase 84's A9. Session 94 fills it and must not have to reopen
    /// this code to do so.</summary>
    private sealed record FixedResidualHost(int LowSchoolId, int HighSchoolId, int HostSchoolId);

    private sealed class ConferenceSlate
    {
        public SlateVerdict Verdict { get; init; } = SlateVerdict.Feasible;
        public string Reason { get; init; } = "";
        /// <summary>★ True only when the fixed hosts were refused by the cheap quota guard,
        /// BEFORE any flow structure existed. A9's negative control asserts this directly, so
        /// "without running the flow" is a provable fact rather than an intention.</summary>
        public bool RejectedBeforeFlow { get; init; }
        public Dictionary<(int Lo, int Hi), int> Meetings { get; init; } = new();
        /// <summary>Oriented games in emission order: (home, away).</summary>
        public List<(int Home, int Away)> Games { get; init; } = new();
        public long SearchNodes { get; init; }
        public bool UsedCanonicalCirculant { get; init; }
    }

    private const int SeasonConferenceSizeCap = 20;
    private const int SeasonMaxConferenceGames = 30;
    private const long SeasonSlateNodeBudget = 20_000_000;

    // ── Legality — a NECESSARY filter, not a promise ─────────────────────────────

    /// <summary>The half of legality that needs no school count, so the world validator can
    /// run it at load. Returns null when the numbers are acceptable, else the reason.</summary>
    private static string? ConferenceStaticLegality(int games, int skip)
    {
        if (games < 0) return $"Games {games} is negative";
        if (games > SeasonMaxConferenceGames)
            return $"Games {games} exceeds the {SeasonMaxConferenceGames}-game maximum for a regular season";
        if (games % 2 == 1) return $"Games {games} is odd — a conference season must be even";
        if (skip < 0) return $"Skip {skip} is negative";
        // A suspended conference carries a canonical k of zero: there is no shape for a skip
        // to live in when nobody plays.
        if (games == 0 && skip != 0) return $"Games 0 requires Skip 0 (got Skip {skip})";
        return null;
    }

    /// <summary>The full predicate, including everything that depends on how many schools are
    /// in the league. NECESSARY, never sufficient: passing means the configuration may go to
    /// the solver, not that a slate exists.</summary>
    private static string? ConferenceSlateLegality(int n, int games, int skip)
    {
        var stat = ConferenceStaticLegality(games, skip);
        if (stat is not null) return stat;
        if (games == 0) return null;                      // a conference of independents (R14)
        if (n < 2) return $"size {n} — a conference season needs an opponent";
        if (skip > n - 2)
            return $"Skip {skip} leaves no opponent (size {n}; Skip may not exceed {n - 2})";
        var p = n - 1 - skip;
        var q = games / p;
        var r = games - q * p;
        if (q < 1)
            return $"Games {games} over {p} played opponent(s) gives {q} meetings — " +
                   "every played opponent must get a game";
        if ((n * skip) % 2 == 1)
            return $"size {n} with Skip {skip} is odd on both — no {skip}-regular skipped graph exists";
        if ((n * r) % 2 == 1)
            return $"size {n} with {r} extra meeting(s) is odd on both — " +
                   $"no {r}-regular extra graph exists";
        return null;
    }

    private static (int P, int Q, int R) ConferenceShape(int n, int games, int skip)
    {
        var p = n - 1 - skip;
        var q = games / p;
        return (p, q, games - q * p);
    }

    // ── The extra-meeting shape ─────────────────────────────────────────────────

    /// <summary>The canonical r-regular circulant on member indices — offsets 1..r/2, plus the
    /// diameter matching (i, i+n/2) when r is odd (which forces n even). ★ PINNED DELIBERATELY:
    /// this is what the pre-S93 builder produced, so wherever a league's game count is
    /// unchanged the pair multiset is reproducible against the old schedule and a DIFFERENCE
    /// MEANS SOMETHING.</summary>
    private static HashSet<(int, int)> CanonicalCirculant(int n, int r)
    {
        var extra = new HashSet<(int, int)>();
        if (r <= 0) return extra;
        var half = r % 2 == 0 ? r / 2 : (r - 1) / 2;
        var diameter = r % 2 == 1;
        for (var i = 0; i < n; i++)
        {
            for (var off = 1; off <= half; off++)
            {
                var j = (i + off) % n;
                extra.Add((Math.Min(i, j), Math.Max(i, j)));
            }
            if (diameter && i < n / 2) extra.Add((i, i + n / 2));
        }
        return extra;
    }

    private const int SlateExtra = 0, SlateSkip = 1, SlateBase = 2;

    /// <summary>Exhaustive backtracking over the class of every unordered pair: EXTRA (meets
    /// q+1), SKIP (meets zero) or BASE (meets q). Every vertex takes exactly r EXTRA and
    /// exactly k SKIP. Pairs are visited in lexicographic order and classes tried
    /// EXTRA → SKIP → BASE, so the first solution found is a canonical one and two equal
    /// solutions can never both be returned.
    ///
    /// <para>★ THE SEARCH IS EXHAUSTIVE, which is what licenses the word "infeasible". Running
    /// the space out with no solution IS a proof. Running the node BUDGET out is not, and
    /// returns <c>SearchBudgetExhausted</c> instead — the two are never merged.</para></summary>
    private static SlateVerdict SearchConferenceShape(
        int n, int r, int k, HashSet<(int, int)> forcedExtra, HashSet<(int, int)> forbiddenSkip,
        out HashSet<(int, int)> extra, out HashSet<(int, int)> skipped, out long nodes, out string reason)
    {
        var pairs = new List<(int I, int J)>();
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                pairs.Add((i, j));
        var extraLeft = new int[n];
        var skipLeft = new int[n];
        var openPairs = new int[n];
        for (var v = 0; v < n; v++) { extraLeft[v] = r; skipLeft[v] = k; openPairs[v] = n - 1; }
        var cls = new int[pairs.Count];
        long visited = 0;
        var exhausted = false;

        bool Feasible()
        {
            for (var v = 0; v < n; v++)
                if (extraLeft[v] + skipLeft[v] > openPairs[v]) return false;
            return true;
        }

        bool Walk(int idx)
        {
            if (++visited > SeasonSlateNodeBudget) { exhausted = true; return false; }
            if (idx == pairs.Count)
            {
                for (var v = 0; v < n; v++)
                    if (extraLeft[v] != 0 || skipLeft[v] != 0) return false;
                return true;
            }
            var (i, j) = pairs[idx];
            // Row i-1 is fully decided by now, so it must be exactly satisfied.
            if (idx > 0)
            {
                var prevI = pairs[idx - 1].I;
                if (prevI != i && (extraLeft[prevI] != 0 || skipLeft[prevI] != 0)) return false;
            }
            var forced = forcedExtra.Contains((i, j));
            for (var c = SlateExtra; c <= SlateBase; c++)
            {
                if (forced && c != SlateExtra) continue;
                if (c == SlateExtra && (extraLeft[i] == 0 || extraLeft[j] == 0)) continue;
                if (c == SlateSkip && (forbiddenSkip.Contains((i, j))
                                       || skipLeft[i] == 0 || skipLeft[j] == 0)) continue;
                if (c == SlateExtra) { extraLeft[i]--; extraLeft[j]--; }
                else if (c == SlateSkip) { skipLeft[i]--; skipLeft[j]--; }
                openPairs[i]--; openPairs[j]--;
                cls[idx] = c;
                if (Feasible() && Walk(idx + 1)) return true;
                if (exhausted) return false;
                openPairs[i]++; openPairs[j]++;
                if (c == SlateExtra) { extraLeft[i]++; extraLeft[j]++; }
                else if (c == SlateSkip) { skipLeft[i]++; skipLeft[j]++; }
            }
            return false;
        }

        var solved = Walk(0);
        nodes = visited;
        extra = new HashSet<(int, int)>();
        skipped = new HashSet<(int, int)>();
        if (exhausted)
        {
            reason = $"the search budget of {SeasonSlateNodeBudget:N0} nodes ran out at size {n} — " +
                     "this proves nothing about whether a slate exists";
            return SlateVerdict.SearchBudgetExhausted;
        }
        if (!solved)
        {
            reason = $"no legal slate exists for size {n} with {r} extra meeting(s), {k} skip(s) " +
                     $"and {forcedExtra.Count + forbiddenSkip.Count} placed rivalry pair(s)";
            return SlateVerdict.InfeasibleUnderConstraints;
        }
        for (var idx = 0; idx < pairs.Count; idx++)
        {
            if (cls[idx] == SlateExtra) extra.Add(pairs[idx]);
            else if (cls[idx] == SlateSkip) skipped.Add(pairs[idx]);
        }
        reason = "";
        return SlateVerdict.Feasible;
    }

    // ── One conference: shape, then whose gym ───────────────────────────────────

    /// <summary>Which rivalries are ACTIVE for slate construction: mutual, both schools in THIS
    /// conference, and the conference actually plays. A cross-conference rivalry and a rivalry
    /// inside a zero-game conference are both DORMANT — never an error (R13 extended). The
    /// second cannot be placed in a shape that does not exist.</summary>
    private static List<(int Lo, int Hi)> ActiveRivalries(
        List<int> members, Dictionary<int, int?> rivals, int games)
    {
        var active = new List<(int, int)>();
        if (games == 0) return active;
        var inside = new HashSet<int>(members);
        foreach (var s in members)
            if (rivals.TryGetValue(s, out var rv) && rv is { } r && inside.Contains(r) && s < r)
                active.Add((s, r));
        return active;
    }

    /// <summary>Build one conference's slate: the meeting multiset, then the orientation.
    ///
    /// <para>Construction order, and the ORDER IS LOAD-BEARING: resolve the rivalries; build
    /// the r-regular extra graph so that it CONTAINS the rivalry matching (rivalries are placed
    /// by construction, never searched for); build the k-regular skipped graph on the
    /// complement; everything else meets q times; orient.</para></summary>
    private static ConferenceSlate BuildConferenceSlate(
        List<int> members, int games, int skip, List<(int Lo, int Hi)> rivalries,
        string label, List<FixedResidualHost>? fixedHosts = null)
    {
        var n = members.Count;
        var reason = ConferenceSlateLegality(n, games, skip);
        if (reason is not null)
            return new ConferenceSlate { Verdict = SlateVerdict.InvalidConfiguration, Reason = $"{label}{reason}" };
        if (games == 0)
            return new ConferenceSlate();   // a conference of independents: nothing to build
        // ★ PRECEDENCE, FIXED: static configuration validation → supported-size check →
        //   search. A configuration that is both illegal and oversized reports the illegality.
        if (n > SeasonConferenceSizeCap)
            return new ConferenceSlate
            {
                Verdict = SlateVerdict.UnsupportedConferenceSize,
                Reason = $"{label}size {n} is above the solver's hard cap of " +
                         $"{SeasonConferenceSizeCap}; no search was attempted",
            };

        var (p, q, r) = ConferenceShape(n, games, skip);
        var index = new Dictionary<int, int>();
        for (var i = 0; i < n; i++) index[members[i]] = i;
        var forced = new HashSet<(int, int)>();
        foreach (var (lo, hi) in rivalries)
        {
            var a = index[lo]; var b = index[hi];
            forced.Add((Math.Min(a, b), Math.Max(a, b)));
        }

        HashSet<(int, int)>? extra = null;
        HashSet<(int, int)> skipped = new();
        long nodes = 0;
        var usedCirculant = false;
        if (skip == 0)
        {
            var candidate = CanonicalCirculant(n, r);
            // ★ THE SHORTCUT ONLY EVER ACCEPTS. It takes the pinned circulant when the
            //   circulant already satisfies every constraint the search would enforce, and
            //   otherwise falls through — so it can never mask the search's infeasibility proof.
            if (r == 0 || forced.All(candidate.Contains))
            {
                extra = candidate;
                usedCirculant = true;
            }
        }
        if (extra is null)
        {
            // r > 0: a rivalry must sit at q+1. r == 0: a rivalry must simply not be skipped.
            var forcedExtra = r > 0 ? forced : new HashSet<(int, int)>();
            var forbiddenSkip = r > 0 ? new HashSet<(int, int)>() : forced;
            var verdict = SearchConferenceShape(
                n, r, skip, forcedExtra, forbiddenSkip, out extra, out skipped, out nodes, out var why);
            if (verdict != SlateVerdict.Feasible)
                return new ConferenceSlate { Verdict = verdict, Reason = $"{label}{why}", SearchNodes = nodes };
        }

        var meetings = new Dictionary<(int Lo, int Hi), int>();
        for (var i = 0; i < n - 1; i++)
            for (var j = i + 1; j < n; j++)
                meetings[(members[i], members[j])] =
                    skipped.Contains((i, j)) ? 0 : q + (extra.Contains((i, j)) ? 1 : 0);

        return OrientConferenceSlate(members, games, meetings, label, fixedHosts, nodes, usedCirculant);
    }

    /// <summary>★ R3 IS A HARD LINE: every team plays an exactly even home/away conference
    /// season, <c>Games/2</c> each.
    ///
    /// <para>A pair meeting m times contributes floor(m/2) home and floor(m/2) away BY
    /// CONSTRUCTION, alternating from the lower school id, and those games never enter the
    /// flow. Only an ODD m leaves one game undecided — the RESIDUAL, always the last of that
    /// pair's m games. Residuals are settled by an integral flow with exact quotas: one node
    /// per free residual at capacity one, each residual feeding its two schools at capacity
    /// one, each school feeding the sink at exactly its remaining home quota.</para>
    ///
    /// <para>★ A Eulerian walk would also produce an even split here and would tell you
    /// nothing — the pre-S93 orientation already landed all 347 schools at 8 home and 8 away
    /// by accident of even degrees. The flow exists because it can honour PRE-FIXED VENUES,
    /// which a Eulerian cannot, and that is the only thing that distinguishes it.</para></summary>
    private static ConferenceSlate OrientConferenceSlate(
        List<int> members, int games, Dictionary<(int Lo, int Hi), int> meetings,
        string label, List<FixedResidualHost>? fixedHosts, long nodes, bool usedCirculant)
    {
        var n = members.Count;
        var quota = members.ToDictionary(s => s, _ => games / 2);
        var homes = new List<int?>();
        var pairOf = new List<(int Lo, int Hi)>();
        var residualIndex = new Dictionary<(int Lo, int Hi), int>();
        for (var i = 0; i < n - 1; i++)
            for (var j = i + 1; j < n; j++)
            {
                var lo = members[i]; var hi = members[j];
                var m = meetings[(lo, hi)];
                for (var t = 0; t < m; t++)
                {
                    pairOf.Add((lo, hi));
                    if (m % 2 == 1 && t == m - 1)
                    {
                        residualIndex[(lo, hi)] = homes.Count;
                        homes.Add(null);
                    }
                    else
                    {
                        var h = t % 2 == 0 ? lo : hi;
                        homes.Add(h);
                        quota[h]--;
                    }
                }
            }

        var fixedByPair = new Dictionary<(int Lo, int Hi), int>();
        foreach (var f in fixedHosts ?? new List<FixedResidualHost>())
        {
            var key = (Math.Min(f.LowSchoolId, f.HighSchoolId), Math.Max(f.LowSchoolId, f.HighSchoolId));
            if (!meetings.ContainsKey(key))
                return new ConferenceSlate
                {
                    Verdict = SlateVerdict.InvalidConfiguration,
                    Reason = $"{label}a fixed host names the pair ({key.Item1},{key.Item2}), " +
                             "which is not a pair in this conference",
                };
            if (meetings[key] % 2 == 0)
                return new ConferenceSlate
                {
                    Verdict = SlateVerdict.InvalidConfiguration,
                    Reason = $"{label}a fixed host is named for the pair ({key.Item1},{key.Item2}), " +
                             $"which meets {meetings[key]} time(s) — only an odd meeting count " +
                             "leaves a residual to decide",
                };
            if (f.HostSchoolId != key.Item1 && f.HostSchoolId != key.Item2)
                return new ConferenceSlate
                {
                    Verdict = SlateVerdict.InvalidConfiguration,
                    Reason = $"{label}fixed host {f.HostSchoolId} is not one of the two schools in " +
                             $"({key.Item1},{key.Item2})",
                };
            if (fixedByPair.TryGetValue(key, out var already) && already != f.HostSchoolId)
                return new ConferenceSlate
                {
                    Verdict = SlateVerdict.InvalidConfiguration,
                    Reason = $"{label}two contradictory fixed hosts for the pair " +
                             $"({key.Item1},{key.Item2}): {already} and {f.HostSchoolId}",
                };
            fixedByPair[key] = f.HostSchoolId;
        }

        // ★ FIXED RESIDUALS CONSUME QUOTA BEFORE ANY FLOW STRUCTURE EXISTS, so an
        //   over-commitment is refused by the doorman rather than discovered by the solver.
        foreach (var key in fixedByPair.Keys.OrderBy(k => k.Lo).ThenBy(k => k.Hi))
        {
            var host = fixedByPair[key];
            quota[host]--;
            if (quota[host] < 0)
                return new ConferenceSlate
                {
                    Verdict = SlateVerdict.InfeasibleUnderConstraints,
                    RejectedBeforeFlow = true,
                    Reason = $"{label}the fixed hosts over-commit school {host}'s home quota of " +
                             $"{games / 2} — refused before any flow structure was built",
                };
            homes[residualIndex[key]] = host;
        }

        var free = residualIndex.Keys.Where(k => !fixedByPair.ContainsKey(k))
                                     .OrderBy(k => k.Lo).ThenBy(k => k.Hi).ToList();
        var quotaLeft = quota.Values.Sum();
        if (quotaLeft != free.Count)
            return new ConferenceSlate
            {
                Verdict = SlateVerdict.InfeasibleUnderConstraints,
                Reason = $"{label}the remaining home quota {quotaLeft} does not equal the " +
                         $"{free.Count} free residual game(s)",
            };

        // Integral flow by deterministic augmenting paths. Each free residual has exactly two
        // candidate hosts; a school may host at most its remaining quota. Candidate order is
        // fixed (lower school id first, then residual index ascending), so this returns one
        // specific orientation and never a choice between two.
        var assigned = new int?[free.Count];
        var holds = members.ToDictionary(s => s, _ => new List<int>());
        for (var start = 0; start < free.Count; start++)
        {
            var parent = new Dictionary<int, (int Ridx, int School)?> { [start] = null };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            var foundR = -1; var foundS = -1;
            while (queue.Count > 0 && foundR < 0)
            {
                var ridx = queue.Dequeue();
                foreach (var school in new[] { free[ridx].Lo, free[ridx].Hi })
                {
                    if (quota[school] > 0) { foundR = ridx; foundS = school; break; }
                    foreach (var other in holds[school])
                        if (!parent.ContainsKey(other))
                        {
                            parent[other] = (ridx, school);
                            queue.Enqueue(other);
                        }
                }
            }
            if (foundR < 0)
                return new ConferenceSlate
                {
                    Verdict = SlateVerdict.InfeasibleUnderConstraints,
                    Reason = $"{label}no legal orientation exists under the fixed set: the residual " +
                             $"({free[start].Lo},{free[start].Hi}) has nowhere left to host",
                };
            var curR = foundR; var curS = foundS;
            quota[curS]--;
            while (true)
            {
                if (assigned[curR] is { } was) holds[was].Remove(curR);
                assigned[curR] = curS;
                holds[curS].Add(curR);
                var step = parent[curR];
                if (step is null) break;
                curR = step.Value.Ridx; curS = step.Value.School;
            }
        }
        for (var i = 0; i < free.Count; i++) homes[residualIndex[free[i]]] = assigned[i];

        var oriented = new List<(int Home, int Away)>(homes.Count);
        for (var g = 0; g < homes.Count; g++)
        {
            if (homes[g] is not { } h)
                return new ConferenceSlate
                {
                    Verdict = SlateVerdict.InfeasibleUnderConstraints,
                    Reason = $"{label}the orientation left a game undecided",
                };
            var (lo, hi) = pairOf[g];
            oriented.Add((h, h == lo ? hi : lo));
        }
        return new ConferenceSlate
        {
            Meetings = meetings, Games = oriented,
            SearchNodes = nodes, UsedCanonicalCirculant = usedCirculant,
        };
    }

    // ── Preflight: the legality predicate over every conference in the world ────

    /// <summary>★ S93 — this REPLACES the hardcoded-sixteen preflight rather than layering on
    /// top of it. Every league is checked against the same predicate the solver uses, and an
    /// impossible one dies here, by name, before a single game is built.</summary>
    private static void SeasonPreflight(WorldFile world)
    {
        var byConf = new Dictionary<int, int>();
        foreach (var s in world.Schools)
            byConf[s.ConferenceId] = byConf.GetValueOrDefault(s.ConferenceId) + 1;
        foreach (var c in world.Conferences.OrderBy(c => c.Id))
        {
            var size = byConf.GetValueOrDefault(c.Id);
            var reason = ConferenceSlateLegality(size, c.Games, c.Skip);
            if (reason is not null)
                throw new InvalidOperationException(
                    $"SEASON PREFLIGHT INFEASIBLE: conference '{c.Name}' (id {c.Id}, size {size}) — {reason}.");
        }
    }

    // ── The schedule builder (preflight -> conference slates -> orient) ─────────

    /// <summary>★ S89 — the schedule is built identity-free FIRST, validated, and only then
    /// numbered. The order matters: a schedule that fails to build must not have already
    /// spent a season number on itself, and no half-validated fixture may ever become
    /// visible carrying an identity. Once the numbers ARE reserved they are durable, so a
    /// season that then fails burns them permanently — a gap, never a retry.
    ///
    /// <para>★ S93 — <paramref name="seasonSeed"/> no longer reaches the schedule at all. It
    /// stays in the signature because it names the season everywhere else and because the
    /// per-game engine seeds are derived from it.</para></summary>
    private static List<SeasonGame> BuildSeasonSchedule(
        WorldFile world, long seasonSeed, HistoryStore? history = null)
    {
        SeasonPreflight(world);
        var schools = world.Schools.OrderBy(s => s.Id).ToList();
        var rivals = schools.ToDictionary(s => s.Id, s => s.RivalId);
        var byConf = new Dictionary<int, List<int>>();
        foreach (var s in schools)
        {
            if (!byConf.TryGetValue(s.ConferenceId, out var list))
                byConf[s.ConferenceId] = list = new List<int>();
            list.Add(s.Id);
        }

        var games = new List<SeasonGame>();
        foreach (var c in world.Conferences.OrderBy(c => c.Id))
        {
            if (!byConf.TryGetValue(c.Id, out var members)) continue;
            var label = $"conference '{c.Name}' (id {c.Id}) ";
            var slate = BuildConferenceSlate(
                members, c.Games, c.Skip, ActiveRivalries(members, rivals, c.Games), label);
            if (slate.Verdict != SlateVerdict.Feasible)
                throw new InvalidOperationException(
                    $"SEASON SCHEDULE {slate.Verdict.ToString().ToUpperInvariant()}: {slate.Reason}.");
            foreach (var (home, away) in slate.Games)
                games.Add(new SeasonGame("conf", home, away));
        }

        if (history is null) return games;   // legacy mode: the fixtures stay unnumbered

        // The count is known and the slate is legal, so one season number and one block of
        // game numbers are reserved together and written once.
        var seasonId = history.ReserveSeason();
        var gameIds = history.ReserveGames(games.Count);
        for (var g = 0; g < games.Count; g++)
            games[g] = games[g] with { SeasonId = seasonId, GameId = gameIds[g] };
        return games;
    }

    /// <summary>★ S89 note — this hashes the four pre-S89 fields BY NAME (index, kind, home,
    /// away) and has always done so. That is what keeps the fingerprint at
    /// `93d8c853…` after the identity fields were added: had it used the record's own
    /// hashing it would have absorbed them and the A8 isolation check would have gone red
    /// with nothing wrong in the engine. Nothing here changed; the property is recorded so
    /// a later session does not "tidy" it into default record hashing.</summary>
    private static string ScheduleFingerprint(List<SeasonGame> games)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < games.Count; i++)
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(games[i].Kind).Append('|')
              .Append(games[i].HomeId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(games[i].AwayId.ToString(CultureInfo.InvariantCulture)).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Season preparation: divvied rosters -> per-school depth rows, built once ──

    private static Dictionary<int, List<GenPlayerRow>> BuildSeasonRows(
        DivvyResult res, WorldFile world, bool verbose)
    {
        var rows = new Dictionary<int, List<GenPlayerRow>>();
        foreach (var s in world.Schools.OrderBy(x => x.Id))
        {
            var roster = res.Rosters[s.Id];
            var five = new HashSet<int>(BuildOpeningFive(roster, pid => res.Pool[pid].Pos));
            var list = roster.Select((pid, i) =>
            {
                var p = res.Pool[pid];
                // LegCount 0 / empty PlusLegs = "not applicable" (S63; mechanically
                // dead on this path — the game consumes Player/Slot/Pos/Starter only).
                // S76: p.ScoutRank was in scope here and discarded one line before the
                // seam needed it. It is the stored-group depth order the minutes
                // allocator sorts each chart by.
                // S77: `pid` is the POOL index — the person. It was in scope here and
                // discarded one line early, exactly as ScoutRank was before S76. A season
                // record keyed by (school, acquisition index) is keyed by a SEAT: next
                // season school 200's seventh pick is a different human being, and a
                // transferring player's record would stay behind with the seat. Carrying
                // the pool id keys the stat layer by the man instead.
                return new GenPlayerRow(i + 1, p.Pos, p.Role, five.Contains(pid), 0,
                                        DivvyNoPlusLegs, p.Ratings, p.Player, p.ScoutRank, pid);
            }).ToList();
            if (verbose)
            {
                // Session 30.1: with the seating floor at 1B/2G/1W, no position can
                // be absent — a never-fire sentinel; a line here is a seating bug.
                var onFloor = new HashSet<string>(list.Where(r => r.Starter).Select(r => r.Pos));
                var absent = new[] { "G", "W", "B" }.Where(p => !onFloor.Contains(p)).ToList();
                if (absent.Count > 0)
                    Console.WriteLine($"  NOTE {s.Name}: no {string.Join("/", absent)} on the floor — " +
                                      $"benched {string.Join("/", absent)} cannot enter under the same-position fence.");
            }
            rows[s.Id] = list;
        }
        return rows;
    }

    private static GenSideData BuildSeasonSide(List<GenPlayerRow> rows, int idOffset)
    {
        var stamped = new Player[RosterShape.Size];
        for (var i = 0; i < RosterShape.Size; i++)
            stamped[i] = StampPlayerId(rows[i].Player, rows[i].Slot + idOffset);
        return BuildGenSideData(rows, stamped);
    }

    // ── The season runner (shared by the CLI page and Phase 55) ───────────────────

    /// <param name="retainGameLog">S90. OFF by default, and that default is load-bearing:
    /// every existing caller — Phase 55, Phase 80's identity checks, the turnover-clock
    /// check — keeps its exact prior behaviour and touches no folder. Only the season page
    /// and Phase 81 turn it on, so the retention layer cannot perturb a check that was
    /// green before it existed.</param>
    private static SeasonRunOutcome RunSeasonCore(
        WorldFile world, long seasonSeed, string engineConfigPath, bool verbose,
        HistoryStore? history = null, bool retainGameLog = false)
    {
        var schedule = BuildSeasonSchedule(world, seasonSeed, history);
        var fingerprint = ScheduleFingerprint(schedule);
        var divvy = RunDivvyDraft(world, seasonSeed, history);

        // ★ S89 — history mode's contract, validated ONCE, here. Past this line every
        // history-backed path may assume identities are present; none of them re-check.
        if (history is not null)
        {
            if (divvy.PersonIds is null || divvy.PersonIds.Count != divvy.Pool.Count)
                throw new HistoryException(HistoryError.MissingIdentity,
                    $"history mode: {divvy.PersonIds?.Count ?? 0} identities for " +
                    $"{divvy.Pool.Count} admitted people.");
            foreach (var sg in schedule)
            {
                IdentityGuard.Require(sg.SeasonId, "a scheduled fixture");
                IdentityGuard.Require(sg.GameId, "a scheduled fixture");
            }
            // Nothing after schedule construction issues an identity, so the lock is
            // released before the long simulation rather than held across it.
            history.CloseReservations();
        }

        var rowsBySchool = BuildSeasonRows(divvy, world, verbose);
        AssertSeasonIdentitiesDistinct(rowsBySchool);
        var cfgs = LoadGenEngineConfigs(engineConfigPath);

        var wins = world.Schools.ToDictionary(s => s.Id, _ => 0);
        var losses = world.Schools.ToDictionary(s => s.Id, _ => 0);
        var results = new List<SeasonGameResult>(schedule.Count);
        var ties = 0;
        var league = new SeasonLeagueStats();
        // ★ S89 — the map goes to the accumulator ONCE, before the loop, rather than being
        // threaded through both accumulator signatures every game. `RecordFor` is its only
        // consumer and it already holds the pool slot.
        league.PersonIds = divvy.PersonIds;
        var baseSeed = unchecked((int)seasonSeed);

        //  ── S90: the retention log. ────────────────────────────────────────────
        //  Built here, before the first tip, because the roster section is written
        //  before game one and the rows it lists are these rows — the start-of-season
        //  card, which is the only version that exists at write time (R8).
        //
        //  ★ The writer VALIDATES THE WHOLE ROSTER IN MEMORY before it touches the
        //  filesystem, so an overlong name or a bad domain refuses with nothing on disk
        //  to clean up. Publication of the roster is one deliberate transition.
        GameLogWriter? gameLog = null;
        if (retainGameLog && history is not null)
        {
            var roster = BuildRetentionRoster(rowsBySchool, divvy.PersonIds!);
            gameLog = GameLogWriter.Create(
                history.Path, history.HistoryId, history.WorldFingerprint, fingerprint,
                schedule[0].SeasonId!.Value, roster);
        }

        for (var g = 0; g < schedule.Count; g++)
        {
            var sg = schedule[g];
            // HomeSchool -> the engine's Home side, AwaySchool -> Away: the §1b
            // invariant. Home rows stamp PlayerIds 1..RosterShape.Size, away rows the
            // next Size — at S75's 13-man roster that is 1-13 and 14-26 (the comment
            // here said 1-10 / 11-20 until S77; the numbers had been wrong since S75
            // and sat directly above the code that maps them). Ids need uniqueness only
            // within a game; sides are stamped per matchup, never cached across games
            // where a school flips sides.
            var sideHome = BuildSeasonSide(rowsBySchool[sg.HomeId], 0);
            var sideAway = BuildSeasonSide(rowsBySchool[sg.AwayId], RosterShape.AwayIdOffset);
            var identity = new SeasonGameIdentity(
                sg.HomeId, sg.AwayId,
                rowsBySchool[sg.HomeId], rowsBySchool[sg.AwayId], sideHome, sideAway);
            // Snapshot BEFORE the first accumulator. The three calls below write different
            // halves of a man's line, so the boundary has to enclose all of them.
            var retentionBefore = gameLog is null ? null
                                : RetentionSnapshotBefore(league, identity);

            var (game, result, attributed, policy) = RunSingleGenGame(
                cfgs, sideHome, sideAway, TeamSide.Home, TeamSide.Away,
                resolverSeed: unchecked(baseSeed + 2 * g),
                governorSeed: unchecked(baseSeed + 2 * g + 1));

            // Session 31: keep the attribution the loop used to discard and feed the
            // calibration accumulator. Nothing else about the loop changes.
            league.Accumulate(game, result, attributed, identity);
            // S87: the foul-out layer — read from the tracker that ran and the policy that
            // enforced it, never re-derived.
            league.AccumulateFouling(game, result, policy);

            // S75: cross-position occupancy. Seat position is the seat's STARTER's
            // position and is fixed for the game (SlotPos), so it is read straight off
            // the side data rather than from the policy.
            var storedPos = new Dictionary<int, string>();
            var seatPos = new Dictionary<(TeamSide, int), string>();
            var seatH = new Dictionary<(TeamSide, int), int>();
            foreach (var (sd, sdSide) in new[] { (sideHome, TeamSide.Home), (sideAway, TeamSide.Away) })
            {
                for (var k = 0; k < sd.Starters.Length; k++)
                {
                    storedPos[sd.Starters[k].PlayerId] = sd.StarterPositions[k];
                    seatPos[(sdSide, k + 1)] = sd.StarterPositions[k];
                    seatH[(sdSide, k + 1)] = sd.Starters[k].Height;
                }
                for (var k = 0; k < sd.Reserves.Length; k++)
                    storedPos[sd.Reserves[k].PlayerId] = sd.ReservePositions[k];
            }
            league.NoteOccupancy(result.Possessions, game, storedPos, seatPos, seatH, identity);

            // ...and diff AFTER the last one. Emission is the games-played delta, never a
            // credits delta read after the fact.
            if (gameLog is not null && retentionBefore is not null)
                gameLog.AppendGame(
                    new GameBlockFactsV1(
                        sg.GameId!.Value, g, sg.HomeId, sg.AwayId,
                        string.Equals(sg.Kind, "conf", StringComparison.Ordinal),
                        game.HomeScore, game.AwayScore, (short)result.OvertimePeriods,
                        result.Possessions.Count),
                    RetentionRowsAfter(league, retentionBefore, g));

            // GameState.HomeScore is credited to HomeSchool, AwayScore to AwaySchool,
            // full stop (a flipped attribution passes conservation and determinism —
            // Phase 55's replay check exists to catch exactly that).
            // S89: the result carries the FIXTURE's numbers — the same game, not a new one.
            results.Add(new SeasonGameResult(
                sg.HomeId, sg.AwayId, game.HomeScore, game.AwayScore, result.OvertimePeriods,
                sg.SeasonId, sg.GameId));
            if (game.HomeScore > game.AwayScore) { wins[sg.HomeId]++; losses[sg.AwayId]++; }
            else if (game.AwayScore > game.HomeScore) { wins[sg.AwayId]++; losses[sg.HomeId]++; }
            else ties++;   // assumption-1 says impossible; counted so it can never hide

            if (verbose && (g + 1) % 500 == 0)
                Console.WriteLine($"  ... {g + 1}/{schedule.Count} games played");
        }

        //  One block per scheduled fixture and no other — the writer refuses to publish
        //  a partial season rather than leaving a plausible-looking short file behind.
        if (gameLog is not null)
        {
            gameLog.Finalize(schedule.Count);
            gameLog.Dispose();
        }

        return new SeasonRunOutcome
        {
            Schedule = schedule, Fingerprint = fingerprint, Results = results,
            Wins = wins, Losses = losses, Divvy = divvy, League = league, Ties = ties,
        };
    }

    // ── The page ──────────────────────────────────────────────────────────────────

    private static readonly (int Lo, int Hi)[] SeasonBands =
        { (0, 19), (20, 39), (40, 59), (60, 79), (80, 99) };   // the divvy's exact five bands

    private static void RunSeason(string engineConfigPath, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: season <world.json> <seed> [minutes-floor: 100|250|500|900] " +
                              "[--history <path>]");
            Console.WriteLine("  --history binds this season to a named career file. There is NO " +
                              "default: leave it off and the run behaves exactly as it always has.");
            return;
        }
        // S89: named, so it does not collide with the positional minutes floor at args[3].
        string? historyPath;
        try { historyPath = ParseHistoryArg(args, 3); }
        catch (HistoryException hx) { Console.WriteLine($"SEASON ERROR: {hx.Message}"); return; }
        // S77: reporting-only leaderboard filter. Applied after the roll-up is complete; it
        // touches neither simulation nor accumulation, and deliberately does NOT live in
        // config.json (Phase 71 parity-locks that file's key names).
        var minuteFloor = SeasonDefaultMinuteFloor;
        for (var i = 3; i < args.Length; i++)
        {
            if (IsHistoryArgAt(args, i)) continue;   // S89: the flag and its path are not the floor
            if (!int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out minuteFloor)
                || !SeasonMinuteTiers.Contains(minuteFloor))
            {
                Console.WriteLine($"SEASON ERROR: minutes floor '{args[i]}' must be one of " +
                                  string.Join(", ", SeasonMinuteTiers) + ".");
                return;
            }
        }
        if (!long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            Console.WriteLine($"SEASON ERROR: seed '{args[2]}' is not a valid integer.");
            return;
        }
        WorldFile world;
        try
        {
            world = LoadWorld(args[1]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.WriteLine($"SEASON ERROR: {ex.Message}");
            return;
        }

        // ★ S89 — the history opens BEFORE anything is printed, so a wrong world, a locked
        // career or a corrupt file stops the run instead of stopping it half a page in.
        // Legacy mode (no --history) leaves this null and nothing below touches a file.
        HistoryStore? history = null;
        try { history = OpenHistoryFor(world, historyPath); }
        catch (HistoryException hx)
        {
            Console.WriteLine($"SEASON ERROR [{hx.Error}]: {hx.Message}");
            return;
        }

        List<SeasonGame> schedule;
        SeasonRunOutcome run;
        try
        {
            // Preflight only — deliberately UNNUMBERED. RunSeasonCore builds the real
            // schedule; numbering here as well would burn a season number every run.
            schedule = BuildSeasonSchedule(world, seed);   // preflight + build (fails loudly)
            Console.WriteLine("=== Project Charm :: Season (Pass 2: minimal season loop) ===");
            Console.WriteLine($"World: {args[1]} ({world.Schools.Count} schools, {world.Conferences.Count} conferences)");
            Console.WriteLine($"Season seed: {seed}");
            Console.WriteLine($"Schedule fingerprint: {ScheduleFingerprint(schedule)}");
            // ★ S93 — the banner reads the WORLD rather than restating a constant. The old
            //   line said "16 conference + 14 non-conference per team, 15 home / 15 away" and
            //   would have kept saying it while every one of those numbers was false.
            var slateCounts = world.Conferences
                .Select(c => (c.Games, N: world.Schools.Count(s => s.ConferenceId == c.Id)))
                .Where(x => x.N > 0).ToList();
            var idleSchools = slateCounts.Where(x => x.Games == 0).Sum(x => x.N);
            var playedRange = slateCounts.Where(x => x.Games > 0).Select(x => x.Games).ToList();
            Console.WriteLine(
                $"Schedule: {schedule.Count} games — conference play only. Each team plays its own " +
                $"league's number ({(playedRange.Count == 0 ? "none" : $"{playedRange.Min()}–{playedRange.Max()}")}), " +
                $"exactly half of them at home." +
                (idleSchools > 0
                    ? $" {idleSchools} school(s) sit in a league authored at zero games and play none."
                    : ""));
            Console.WriteLine(
                "  Non-conference scheduling does not exist yet — it is its own session. " +
                "Neutral floors throughout (the road seam is 0).");
            Console.WriteLine();
            Console.WriteLine($"Regenerating divvied rosters (world + seed; nothing persisted) and playing " +
                              $"{schedule.Count} real engine games ...");
            // S90: the season page retains a per-game log whenever it is bound to a career.
            // The page itself gains NO output — the log is a file beside the history, and the
            // printed season is byte-identical to its pre-S90 self (Phase 81 A3).
            run = RunSeasonCore(world, seed, engineConfigPath, verbose: true, history,
                                retainGameLog: true);
        }
        catch (HistoryException hx)
        {
            Console.WriteLine($"SEASON ERROR [{hx.Error}]: {hx.Message}");
            history?.Dispose();
            return;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"SEASON ERROR: {ex.Message}");
            history?.Dispose();
            return;
        }
        finally { history?.Dispose(); }
        Console.WriteLine();

        // ★ S89 — printed only in history mode, so the legacy page is byte-identical to
        // every page before this session (A8/A11).
        if (history is not null)
        {
            Console.WriteLine($"History: {history.Path}");
            Console.WriteLine($"World fingerprint: {history.WorldFingerprint}");
            Console.WriteLine();
        }

        var prestige = world.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
        var names = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        var abbrs = world.Schools.ToDictionary(s => s.Id, s => s.Abbr.Trim());
        var confShort = world.Conferences.ToDictionary(c => c.Id, c => c.ShortName);
        var confOf = world.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);

        // (i) the full ranked table — every school by W-L, ties broken by school id.
        Console.WriteLine($"--- STANDINGS (all {world.Schools.Count}, ranked by W-L; ties broken by school id) ---");
        var ranked = world.Schools.Select(s => s.Id)
            .OrderByDescending(id => run.Wins[id]).ThenBy(id => id).ToList();
        Console.WriteLine($"  {"rk",-5}{"school",-26}{"conf",-10}{"pres",5}   W-L");
        for (var i = 0; i < ranked.Count; i++)
        {
            var id = ranked[i];
            Console.WriteLine($"  {i + 1,-5}{names[id] + " (" + abbrs[id] + ")",-26}" +
                              $"{confShort[confOf[id]],-10}{prestige[id],5}   {run.Wins[id]}-{run.Losses[id]}");
        }
        Console.WriteLine();

        // (ii) the proof table — average wins by prestige band (the divvy's bands).
        Console.WriteLine("--- PROOF TABLE (prestige buys access; does access buy wins?) ---");
        var bandAvg = new Dictionary<(int, int), double>();
        foreach (var band in SeasonBands)
        {
            var members = world.Schools.Where(s => s.CurrentPrestige >= band.Lo && s.CurrentPrestige <= band.Hi)
                                       .Select(s => s.Id).ToList();
            if (members.Count == 0) continue;
            var avg = members.Average(id => (double)run.Wins[id]);
            bandAvg[band] = avg;
            Console.WriteLine($"  prestige {band.Lo,2}-{band.Hi,-2}  avg wins {avg,5:F1}   (n={members.Count})");
        }
        Console.WriteLine();

        // (iii) the escapes — top 10 by wins over band average, leaked talent named.
        Console.WriteLine("--- ESCAPES (top 10 by wins over band average; leaked top-decile talent named) ---");
        var ranks = run.Divvy.Pool.Select(p => p.ScoutRank).ToArray();
        var topDecile = new HashSet<int>(
            Enumerable.Range(0, ranks.Length).OrderByDescending(i => ranks[i]).Take(ranks.Length / 10));
        (int, int) BandOf(int p) => SeasonBands.First(b => p >= b.Lo && p <= b.Hi);
        var escapes = world.Schools
            .Select(s => (s.Id, Dev: run.Wins[s.Id] - bandAvg[BandOf(s.CurrentPrestige)]))
            .OrderByDescending(t => t.Dev).ThenBy(t => t.Id).Take(10);
        foreach (var (id, dev) in escapes)
        {
            var leaked = run.Divvy.Rosters[id].Where(pid => topDecile.Contains(pid)).Select(pid =>
            {
                var p = run.Divvy.Pool[pid];
                return FormattableString.Invariant(
                    $"{p.Pos} {p.Role} (pool #{pid}, rank {p.ScoutRank:F1}, role {p.OffensiveRole})");
            }).ToList();
            var band = BandOf(prestige[id]);
            Console.WriteLine($"  {names[id]} (prestige {prestige[id]}, band {band.Item1}-{band.Item2}): " +
                              $"{run.Wins[id]}-{run.Losses[id]}, {dev:+0.0;-0.0} over band | " +
                              (leaked.Count > 0 ? "leaks: " + string.Join("; ", leaked)
                                                : "no top-decile players (overperformed without a leaked star)"));
        }
        Console.WriteLine();

        // (iv) the OT / scoring sanity pulse.
        var otGames = run.Results.Count(r => r.OvertimePeriods > 0);
        var otPeriods = run.Results.Sum(r => r.OvertimePeriods);
        var avgTotal = run.Results.Average(r => (double)(r.HomeScore + r.AwayScore));
        Console.WriteLine($"--- SANITY PULSE ---");
        Console.WriteLine($"  OT: {otGames} of {run.Results.Count} games needed overtime " +
                          $"({otPeriods} OT periods total); average total score {avgTotal:F1}" +
                          (run.Ties > 0 ? $"; ANOMALY: {run.Ties} unresolved ties" : ""));
        Console.WriteLine();

        // (v) Session 31: the calibration instrument — sim vs the D1 decade blend.
        PrintCalibrationReadout(run.League);
        Console.WriteLine();

        // (vi) Session 63: the baseline lines + the roster census.
        PrintBaselineReadout(run.League);
        Console.WriteLine();
        PrintRosterCensus(run.Divvy, world);
        Console.WriteLine();

        // (vii) Session 77: the season stat page. Appended AFTER every pre-existing section,
        // so the S76.1 reference page is byte-identical above this line.
        PrintSeasonStatPage(run.League, world, minuteFloor);
    }
}
