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
    /// <summary>★ S94 — the game gained its DATE, as a field on the record rather than a
    /// parallel table (a side table joined by position drifts out of order silently). The
    /// structural fingerprint hashes four fields BY NAME (see ScheduleFingerprint's S89
    /// note), so the fifth is invisible to it — proven by Phase 85 C1.</summary>
    /// <summary>★ S98 — <c>HasHost</c> is the SITE FACT, and it defaults to <c>true</c> so the
    /// one existing construction site and every <c>with</c> copy are unchanged. A tournament
    /// game sets it false and is the only thing that ever does. The default is deliberately
    /// the safe-for-existing-code value and deliberately the DANGEROUS one for new code — a
    /// forgotten <c>false</c> would silently host a neutral game — which is why every
    /// tournament fixture is built in exactly one factory (MteBuildTournamentGame).</summary>
    private sealed record SeasonGame(string Kind, int HomeId, int AwayId,
                                     SeasonId? SeasonId = null, GameId? GameId = null,
                                     DateOnly? Date = null, bool HasHost = true);

    /// <summary>★ S98 — ONE ORDERED PLAYED-GAME LIST, AND ITS INDEX IS THE FIXTURE ORDINAL.
    /// <c>PlayedGames[i]</c> is aligned one-to-one with <c>Results[i]</c> and
    /// <c>PossessionCounts[i]</c>, and <c>i</c> IS <c>FixtureOrdinal</c> — conference games at
    /// <c>0..N-1</c>, tournament games appended after.
    ///
    /// <para>A named record rather than a tuple of nullables: the conference entries simply
    /// leave the tournament fields absent. This single structure is what lets the
    /// conference-prefix fingerprint, the event fingerprint, the hosted-game denominator and
    /// the finish routing all read the same facts instead of three places reconstructing them
    /// differently.</para></summary>
    private sealed record PlayedSeasonGame(
        SeasonGame Game, int FixtureOrdinal,
        int? EventTier = null, int? EventId = null, int? BracketGameIndex = null,
        int? HomeOriginalSeed = null, int? AwayOriginalSeed = null)
    {
        public bool IsTournament => EventId is not null;
    }

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
        /// <summary>★ S95 — per-game possession-record counts, in schedule order. The
        /// season fingerprint hashes these alongside the scores so a change that reshapes
        /// a game's INTERNALS while landing on the same final score cannot slip past
        /// Phase 86's zero-path identity check. Observation only; nothing simulates from
        /// it.</summary>
        public List<int> PossessionCounts { get; init; } = new();
        /// <summary>★ S95 — how many games actually had a road side transformed. The
        /// counter increments only when the PREPARED away side is the shaved one, so it
        /// counts what played rather than what was intended. Phase 86 B8 reads it from
        /// the result; there is deliberately no global.</summary>
        public int HostedRoadSidesShaved { get; init; }
        /// <summary>★ S95 — the shave this run actually used. Carried out so the page
        /// reports the value that PLAYED rather than re-reading config.json and reporting
        /// a number that might not be the one the games were played at.</summary>
        public int RoadShave { get; init; }
        /// <summary>★ S96 — the dated fingerprint OF THE SCHEDULE THAT PLAYED. It used to be
        /// computed on the page from a throwaway preflight schedule; once memory can move a
        /// venue those two are different schedules, and the page must print the one whose
        /// games were actually played.</summary>
        public string DatedFingerprint { get; init; } = "";
        /// <summary>★ S96 — what host memory did this season. Page-facing; the suite reads
        /// the parts it asserts from the schedule itself, never from these counters.</summary>
        public SeasonMemoryOutcome Memory { get; init; } = SeasonMemoryOutcome.None;
        /// <summary>★ S99 — what the schedule rotation did this season. Page-facing, on the
        /// same terms as Memory above: the suite reads what it asserts from the slates
        /// themselves, never from a printed line.</summary>
        public SeasonRotationOutcome Rotation { get; init; } = SeasonRotationOutcome.None;
        /// <summary>★ S97 — which tournaments ran, who is in them, and what happened to the
        /// permanent record. Page-facing; the suite reads what it asserts from the seating
        /// result itself, never from a printed line.</summary>
        public EventSeasonOutcome Events { get; init; } = EventSeasonOutcome.None;
        /// <summary>★ S101 — classes and requests. Page-only cargo, exactly like Memory
        /// and Rotation above: computed once in RunSeasonCore, read by the page block
        /// and Phase 92, consumed by nothing that plays basketball.</summary>
        public NonConferenceReport NonConference { get; init; } = NonConferenceReport.Empty;
        /// <summary>★ S98 — every game that PLAYED, in fixture-ordinal order. See
        /// PlayedSeasonGame: index i is the fixture ordinal, and Results[i] /
        /// PossessionCounts[i] describe the same game.</summary>
        public List<PlayedSeasonGame> PlayedGames { get; init; } = new();
        /// <summary>★ S98 — the conference prefix length. <c>Schedule</c> is conference-only
        /// (a bracket cannot exist before it is played), so this is <c>Schedule.Count</c> —
        /// carried explicitly because the slice that C1 takes must name its own length rather
        /// than borrow one.</summary>
        public int ConferenceGameCount { get; init; }
        /// <summary>★ S98 — how many bracket games were played. Zero on every world that
        /// authors no events, which is what keeps the zero path a zero path.</summary>
        public int TournamentGameCount { get; init; }
        /// <summary>★ S98 — the tournament games' own fingerprint. The schedule fingerprint
        /// stays CONFERENCE-ONLY and after this session no longer describes the whole season;
        /// this is the other half, and the page says so in both labels.</summary>
        public string EventGamesFingerprint { get; init; } = "";
        /// <summary>★ S98 — final placings, keyed by event id and then by the S97 SEAT (never
        /// by seed, never by school). Empty when nothing played.</summary>
        public IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> EventFinishes { get; init; }
            = new Dictionary<int, IReadOnlyDictionary<int, int>>();
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
        /// <summary>★ S96 — how many residual venues host memory actually supplied to THIS
        /// slate. Zero everywhere memory is absent, and zero for a slate built from an
        /// explicit `fixedHosts` list (Phase 84's A9), so the season's "residuals flipped"
        /// counts what memory did and nothing else.</summary>
        public int MemoryFixedHosts { get; init; }

        // ── S99: what the rotation chooser did to THIS league ────────────────────
        //
        //  ★ TEST-OBSERVABLE, and that is the whole requirement. Without these a legal
        //  slate cannot be told from a bypassed chooser: every pre-S99 slate assertion
        //  describes the SHAPE of a season, and this session changes the CHOICE. They
        //  ride on the slate result because that is the narrowest seam that already
        //  exists — `MemoryFixedHosts` and `UsedCanonicalCirculant` are here for the
        //  same reason.
        //
        //  ★ THEIR ARITHMETIC RECONCILES, and it is asserted where they are built:
        //  Initial == Retained + Relaxations, and the terminal flag fires at most once.
        //  That is what keeps the page's counters from drifting away from the mechanism
        //  they claim to describe.

        /// <summary>The chooser ran on this league: it had a second meeting to give AND at
        /// least one valid historical fact about its own pairs. False means the league took
        /// the pre-S99 path, whatever the rest of the world did.</summary>
        public bool RotationActive { get; init; }
        /// <summary>Preferred pairs the greedy pass proposed, before any relaxation.</summary>
        public int RotationPreferredInitial { get; init; }
        /// <summary>Preferred pairs surviving in the slate that was built.</summary>
        public int RotationPreferredRetained { get; init; }
        /// <summary>Preferred pairs the relaxation loop removed. Counted as DISTINCT PAIRS,
        /// never as retry events — one pass that drops three pairs is three.</summary>
        public int RotationRelaxations { get; init; }
        /// <summary>The preferred set emptied and the league took the pre-S99 path with its
        /// rivalries intact. Rotation is a preference and never a ban, so this is a legal
        /// outcome rather than a failure.</summary>
        public bool RotationTerminalFallback { get; init; }
        /// <summary>★ S99 — memory-derived venues this league had to give up because the
        /// rotation moved which pairs own a residual. Zero on every pre-S99 path, and zero
        /// whenever the extra graph did not move. The page does not print it; Phase 90 asserts
        /// on it, so "the flips are soft" is a measured fact rather than a comment.</summary>
        public int MemoryFlipsDropped { get; init; }
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

    // ── S99: whose turn is it to be played twice ────────────────────────────────

    /// <summary>This league's history, offset by offset, as SETS OF EXTRA PAIRS on member
    /// indices — the point where a meeting count becomes "a second meeting".
    ///
    /// <para>★ "EXTRA" IS RELATIVE TO THIS SEASON'S q, and that is safe because the world
    /// fingerprint hashes every league's game count: an edited world cannot open the same
    /// career (O-84). So a past count is compared against the current q rather than a q
    /// re-derived from the log, which the log does not carry and must not be asked to.</para>
    ///
    /// <para>★ USABLE HISTORY IS LEAGUE-SPECIFIC. A year counts for this league only if it
    /// supplies a meeting count for at least one of THIS league's pairs. A career whose logs
    /// validate but say nothing about a given league must leave that league on the pre-S99
    /// path — otherwise every pair would tie at the maximum and the school-id tie-break would
    /// quietly pick the graph, which is exactly the bias this session removes.</para></summary>
    private static List<(int Offset, HashSet<(int, int)> Extra)> RotationLeagueHistory(
        List<int> members, int q, RotationHistory rotation)
    {
        var seasons = new List<(int, HashSet<(int, int)>)>();
        var n = members.Count;
        foreach (var offset in rotation.ByOffset.Keys.OrderBy(k => k))
        {
            var counts = rotation.ByOffset[offset];
            var extra = new HashSet<(int, int)>();
            var sawThisLeague = false;
            for (var i = 0; i < n - 1; i++)
                for (var j = i + 1; j < n; j++)
                {
                    var key = (Lo: Math.Min(members[i], members[j]), Hi: Math.Max(members[i], members[j]));
                    if (!counts.TryGetValue(key, out var met)) continue;
                    sawThisLeague = true;
                    if (met > q) extra.Add((i, j));
                }
            // ★ A YEAR THAT SAYS NOTHING ABOUT THIS LEAGUE IS NOT A YEAR. But a year that
            //   speaks about the league and reports NO second meetings is real evidence —
            //   it is kept, with an empty extra set.
            if (sawThisLeague) seasons.Add((offset, extra));
        }
        return seasons;
    }

    /// <summary>The preference: whose turn is it, best-first.
    ///
    /// <para>★ THE SCORE IS THE ABSOLUTE SEASON OFFSET OF A PAIR'S MOST RECENT SECOND MEETING.
    /// Doubled last season scores one; doubled four years ago scores four; doubled in several
    /// years takes the most recent. A pair with no recorded second meeting in any readable year
    /// scores W+1 — the maximum, because it is the most overdue thing there is.</para>
    ///
    /// <para>★ OVERDUE, NOT MERELY AVOIDANCE (Emmett's ruling). The rule is whose turn it is: a
    /// pair that has waited longest outranks a pair that merely did not double last season.
    /// Higher score first; ties by lower school id, then higher. The tie-break IS the old bias,
    /// and naming it explicitly is what makes the result deterministic — the eight-season window
    /// reduces reliance on it and does not pretend to eliminate it.</para>
    ///
    /// <para>★ RIVALRIES ARE NOT SCORED AND NOT COUNTED. A permanently forced pair has no turn
    /// to take, so it is excluded here, from the page's counts, and from every fairness
    /// measurement. Each school's ROTATING degree is r minus its hard-forced degree; a school
    /// whose rivalries already consume its whole r has nothing to rotate.</para>
    ///
    /// <para>★ THE GREEDY PASS IS ALLOWED TO END INCOMPLETE. It supplies preferred hard
    /// assignments, not a finished extra graph — the existing search completes every remaining
    /// degree. Measured at design time, it strands degree routinely: the Big Ten proposes 41 of
    /// a possible 44 pairs and the Big East 23 of 24.</para>
    ///
    /// <para>No global-optimality claim is made and none is needed: a highly overdue early pick
    /// can block a slightly lower-ranked one and survive. The promise is rotation by preference,
    /// not the maximum-total-overdue graph.</para></summary>
    private static List<(int Score, int Lo, int Hi, int I, int J)> RotationRankPairs(
        int n, List<int> members, HashSet<(int, int)> hardForced,
        List<(int Offset, HashSet<(int, int)> Extra)> seasons)
    {
        var mostRecent = new Dictionary<(int, int), int>();
        foreach (var (offset, extra) in seasons)
            foreach (var pair in extra)
                if (!mostRecent.TryGetValue(pair, out var seen) || offset < seen)
                    mostRecent[pair] = offset;

        var ranked = new List<(int Score, int Lo, int Hi, int I, int J)>();
        for (var i = 0; i < n - 1; i++)
            for (var j = i + 1; j < n; j++)
            {
                if (hardForced.Contains((i, j))) continue;
                // ★ THE MAXIMUM, for a pair no readable year ever saw doubled. Nothing scores
                //   higher, because nothing is more overdue than "not in living memory".
                var score = mostRecent.TryGetValue((i, j), out var offset)
                    ? offset
                    : RotationWindowSeasons + 1;
                ranked.Add((score, Math.Min(members[i], members[j]),
                            Math.Max(members[i], members[j]), i, j));
            }
        ranked.Sort((x, y) =>
        {
            var c = y.Score.CompareTo(x.Score);         // most overdue first
            if (c != 0) return c;
            c = x.Lo.CompareTo(y.Lo);                   // then the lower school id
            return c != 0 ? c : x.Hi.CompareTo(y.Hi);   // then the higher
        });
        return ranked;
    }

    /// <summary>The greedy pass over the ranking: take a pair whenever both schools still have
    /// rotating degree left. Split from the ranking above so Phase 90 can assert the SCORES —
    /// a chooser can produce a legal-looking set from a wrong scale, and the pair ages are the
    /// thing this session is actually about.</summary>
    private static List<(int I, int J)> RotationPreferredPairs(
        int n, int r, List<int> members, HashSet<(int, int)> hardForced,
        List<(int Offset, HashSet<(int, int)> Extra)> seasons)
    {
        var ranked = RotationRankPairs(n, members, hardForced, seasons);

        var hardDegree = new int[n];
        foreach (var (a, b) in hardForced) { hardDegree[a]++; hardDegree[b]++; }

        var left = new int[n];
        for (var v = 0; v < n; v++) left[v] = r - hardDegree[v];
        var taken = new List<(int, int)>();
        foreach (var (_, _, _, i, j) in ranked)
        {
            if (left[i] <= 0 || left[j] <= 0) continue;
            taken.Add((i, j));
            left[i]--; left[j]--;
        }
        return taken;
    }

    /// <summary>The degree assertions, run on the extra graph BEFORE orientation. Cheap, and
    /// they catch the "list of extra games" misreading of an r-regular simple graph — which is
    /// exactly the mistake a chooser that hands back a list rather than a matching would make.
    /// Returns null when the graph is sound, else the reason.</summary>
    private static string? RotationDegreeProblem(
        int n, int r, HashSet<(int, int)> extra, HashSet<(int, int)> hardForced)
    {
        var degree = new int[n];
        foreach (var (i, j) in extra)
        {
            if (i == j) return $"the extra graph pairs member {i} with itself";
            if (i < 0 || j < 0 || i >= n || j >= n)
                return $"the extra graph names member ({i},{j}), which is not in this league";
            if (i > j) return $"the extra graph holds the unnormalized pair ({i},{j})";
            degree[i]++; degree[j]++;
        }
        for (var v = 0; v < n; v++)
            if (degree[v] != r)
                return $"member {v} has {degree[v]} extra opponent(s), not {r}";
        foreach (var pair in hardForced)
            if (!extra.Contains(pair))
                return $"the rivalry pair ({pair.Item1},{pair.Item2}) is not at a second meeting";
        return null;
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
    /// <param name="fixedHosts">Venues decided by the caller. Phase 84's A9 is the only user;
    /// production supplies <paramref name="memory"/> instead.</param>
    /// <param name="debt">★ S96, widened at S100 — the readable window's residual hosts. The
    /// venue list cannot be computed by the caller because it needs this league's MEETING
    /// COUNTS, and those are not known until the extra-meeting shape has been solved a few
    /// lines below. So the record comes in whole and the pure `ResidualsToFlip` runs at the one
    /// point where both halves exist. Mutually exclusive with <paramref name="fixedHosts"/>:
    /// two sources of venue truth for one slate is a contradiction, not a merge.</param>
    /// <param name="rotation">★ S99 — up to eight seasons of pair meeting counts. Absent means
    /// the pre-S99 chooser, byte for byte.
    ///
    /// <para>★ ROTATION IS ORTHOGONAL TO THE HOST SOURCE. <paramref name="fixedHosts"/> and
    /// <paramref name="debt"/> are two answers to ONE question — who hosts — and stay mutually
    /// exclusive by the throw above. Rotation answers a different question — which pairs meet
    /// twice — and coexists with either. The exclusion throw deliberately does NOT grow a third
    /// arm.</para></param>
    private static ConferenceSlate BuildConferenceSlate(
        List<int> members, int games, int skip, List<(int Lo, int Hi)> rivalries,
        string label, List<FixedResidualHost>? fixedHosts = null, HostDebtHistory? debt = null,
        RotationHistory? rotation = null)
    {
        if (fixedHosts is not null && debt is not null)
            throw new InvalidOperationException(
                $"{label}both an explicit fixed-host list and host memory were supplied; " +
                "a slate has exactly one source of decided venues.");
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

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ S99 — WHOSE TURN IS IT. Two forced collections, never one.
        //
        //      hardForced      rivalry pairs. NEVER relaxed, NEVER scored, present in
        //                      every retry including the terminal fallback.
        //      preferredForced the rotation's choices. Relaxed one at a time; the only
        //                      thing relaxation may touch.
        //
        //  The search's signature is unchanged — it receives the UNION — but the
        //  relaxation loop draws exclusively from the preferred half. This separation
        //  is the whole reason two collections exist: the search treats every member of
        //  its forced set identically, so a single merged set would let the relaxation
        //  loop drop a rivalry to make a preference fit.
        // ══════════════════════════════════════════════════════════════════════════
        var hardForced = forced;
        var preferred = new List<(int I, int J)>();
        var rotationActive = false;
        var preferredInitial = 0;
        var retained = 0;
        var relaxations = 0;
        var terminalFallback = false;

        if (rotation is not null && r > 0)
        {
            var seasons = RotationLeagueHistory(members, q, rotation);
            if (seasons.Count > 0)
            {
                rotationActive = true;
                preferred = RotationPreferredPairs(n, r, members, hardForced, seasons);
                preferredInitial = preferred.Count;
            }
        }

        // ★ THE WHOLE SLATE IS THE UNIT OF FEASIBILITY, not the extra graph alone — and this
        //   is the one thing the design conversation did not foresee. A legal extra graph can
        //   still fail to ORIENT: host memory reverses last season's residual hosts, and
        //   before this session that was always possible because every school's odd pairs were
        //   frozen forever, so reversing a valid assignment was itself valid. Rotation unfreezes
        //   them. A school can end up forced onto the road in more of its surviving odd pairs
        //   than its home quota can absorb, and the flow correctly refuses.
        //
        //   ★ SO WHICH ONE YIELDS, AND WHY IT IS THE FLIPS. The first build made ROTATION yield
        //   — relax preferences until the flips fit — on the reasoning that rotation is
        //   explicitly "a preference, never a ban". Measuring the axis the change is about
        //   killed that: on the sixteen-school rig, the league whose five-season turn is the
        //   entire reason the window is eight, every season relaxed to empty and the schedule
        //   never moved at all. A session that does nothing in the deepest league is not a
        //   session.
        //
        //   The flips yield instead, and this is S96's OWN rule rather than a new concession:
        //   ResidualsToFlip already SILENTLY SKIPS a pair whose parity changed, on the stated
        //   grounds that a league changing shape is "an ordinary consequence, not an error".
        //   Rotation changes the shape every year, so the same reasoning covers the same case
        //   one step further along — and the alternative is a hard constraint that can refuse
        //   a season outright, which host memory was explicitly built never to do.
        //
        //   ★ S100 — AND WHICH VENUE IS SURRENDERED IS NOW A DECISION, not an accident of the
        //   pair id. Venues are dropped from the END of ResidualsToFlip's own ordering, one at
        //   a time, so the result is deterministic and the LONGEST honourable prefix survives.
        //   That list is now ordered STRONGEST CLAIM FIRST — biggest home-game debt, ties by
        //   ascending pair — so the school owed two home games keeps its game and the pair
        //   nearly square is the one that pays. The loop below is untouched: ordering the list
        //   at the point it is built is the whole implementation of that ruling.
        ConferenceSlate FinishSlate(
            HashSet<(int, int)> chosenExtra, HashSet<(int, int)> chosenSkips,
            long searchNodes, bool circulant, int keptPreferences, int droppedPreferences,
            bool terminal)
        {
            var meetingsLocal = new Dictionary<(int Lo, int Hi), int>();
            for (var i = 0; i < n - 1; i++)
                for (var j = i + 1; j < n; j++)
                    meetingsLocal[(members[i], members[j])] =
                        chosenSkips.Contains((i, j)) ? 0 : q + (chosenExtra.Contains((i, j)) ? 1 : 0);

            // ★ S96 — the one point where the record meets this league's actual meeting counts.
            var flipsLocal = debt is null ? fixedHosts : ResidualsToFlip(debt, meetingsLocal, members);

            ConferenceSlate Orient(List<FixedResidualHost>? venues, int dropped)
                // ★ ZERO-PATH CALL SHAPE PRESERVED. Null and empty behave identically
                //   downstream, but passing null keeps a memory-less season's call byte-for-byte
                //   the call it made before this session existed.
                => OrientConferenceSlate(
                    members, games, meetingsLocal, label,
                    venues is null || venues.Count == 0 ? null : venues,
                    searchNodes, circulant,
                    debt is null ? 0 : venues!.Count,
                    new RotationSlateDiagnostics(
                        rotationActive, preferredInitial, keptPreferences, droppedPreferences,
                        terminal, dropped));

            var attemptOrient = Orient(flipsLocal, 0);
            // ★ ONLY MEMORY-DERIVED VENUES ARE SOFT. An explicit fixedHosts list is a caller's
            //   instruction and keeps its refusal; only the venues this layer computed for
            //   itself may be given up, and only for the one refusal that means "these venues do
            //   not fit", never for a malformed configuration.
            if (debt is null || flipsLocal is null) return attemptOrient;
            var soft = new List<FixedResidualHost>(flipsLocal);
            var droppedFlips = 0;
            while (attemptOrient.Verdict == SlateVerdict.InfeasibleUnderConstraints && soft.Count > 0)
            {
                soft.RemoveAt(soft.Count - 1);
                droppedFlips++;
                attemptOrient = Orient(soft, droppedFlips);
            }
            return attemptOrient;
        }

        if (preferred.Count > 0)
        {
            // ★ RELAXATION IN EXACT REVERSE PREFERENCE ORDER. `preferred` is best-first, so
            //   removing from the END drops the worst-scored survivor each time and every
            //   retry keeps the LONGEST FEASIBLE PREFIX of the preference.
            var prefix = new List<(int I, int J)>(preferred);
            while (prefix.Count > 0)
            {
                var union = new HashSet<(int, int)>(hardForced);
                foreach (var pair in prefix) union.Add(pair);
                var attempt = SearchConferenceShape(
                    n, r, skip, union, new HashSet<(int, int)>(),
                    out var found, out var foundSkips, out nodes, out var attemptWhy);
                // ★ AN EXHAUSTED BUDGET PROVES NOTHING and must never be read as "this
                //   preference is impossible". The two verdicts are never merged, here least
                //   of all — relaxing on a budget failure would silently discard a legal
                //   preference and call it infeasible.
                if (attempt == SlateVerdict.SearchBudgetExhausted)
                    return new ConferenceSlate
                    {
                        Verdict = attempt, Reason = $"{label}{attemptWhy}", SearchNodes = nodes,
                    };
                if (attempt == SlateVerdict.Feasible)
                {
                    var degreeMiss = RotationDegreeProblem(n, r, found, hardForced);
                    // A wrong graph out of the search is a BUG, not an infeasible preference.
                    // It is never relaxed away; it stops the season by name.
                    if (degreeMiss is not null)
                        return new ConferenceSlate
                        {
                            Verdict = SlateVerdict.InvalidConfiguration,
                            Reason = $"{label}{degreeMiss}", SearchNodes = nodes,
                        };
                    // ★ FinishSlate owns the venue side and always orients: memory-derived
                    //   flips are soft, so a preference that last season's hosts cannot all
                    //   survive costs a flip rather than the preference. Anything that still
                    //   comes back refused is a real configuration error and is returned as
                    //   one — it is never relaxed into legality.
                    return FinishSlate(
                        found, foundSkips, nodes, circulant: false,
                        keptPreferences: prefix.Count,
                        droppedPreferences: relaxations, terminal: false);
                }
                prefix.RemoveAt(prefix.Count - 1);
                relaxations++;
            }
            // ★ THE TERMINAL FALLBACK: the preferred set emptied, the rivalries are intact,
            //   and what happens below is the pre-S99 path in full — INCLUDING the pinned
            //   shortcut. A feasibility floor, not a quality bound.
            terminalFallback = true;
            relaxations = preferredInitial;
        }
        else if (rotationActive)
        {
            // Rotation ran and had nothing to prefer — every pair is already a rivalry, or the
            // league's whole r is consumed by them. Retained zero IS terminal, by definition.
            terminalFallback = true;
        }

        // ── The pre-S99 path, unchanged, and still REACHABLE ──────────────────────
        //  ★ THIS IS WHAT PRESERVES EVERY GOLDEN. With no career, in a career's first
        //    season, or in a later season where zero valid rotation facts exist for this
        //    league, nothing above fired and the shortcut is taken exactly as it always
        //    was. A version that computed rotation data correctly and then took the
        //    shortcut anyway would be indistinguishable here — which is why Phase 90's
        //    controls, not this comment, are what prove the chooser ran.
        if (extra is null && skip == 0)
        {
            var candidate = CanonicalCirculant(n, r);
            // ★ THE SHORTCUT ONLY EVER ACCEPTS. It takes the pinned circulant when the
            //   circulant already satisfies every constraint the search would enforce, and
            //   otherwise falls through — so it can never mask the search's infeasibility proof.
            if (r == 0 || hardForced.All(candidate.Contains))
            {
                extra = candidate;
                usedCirculant = true;
            }
        }
        if (extra is null)
        {
            // r > 0: a rivalry must sit at q+1. r == 0: a rivalry must simply not be skipped.
            var forcedExtra = r > 0 ? hardForced : new HashSet<(int, int)>();
            var forbiddenSkip = r > 0 ? new HashSet<(int, int)>() : hardForced;
            var verdict = SearchConferenceShape(
                n, r, skip, forcedExtra, forbiddenSkip, out extra, out skipped, out nodes, out var why);
            if (verdict != SlateVerdict.Feasible)
                return new ConferenceSlate { Verdict = verdict, Reason = $"{label}{why}", SearchNodes = nodes };
        }

        // ★ THE DEGREE ASSERTIONS, before orientation and on every path — the shortcut's and
        //   the search's alike, so a wrong graph cannot reach the flow. Cheap, and they catch
        //   the "list of extra games" misreading of an r-regular simple graph.
        var degreeProblem = RotationDegreeProblem(
            n, r, extra, r > 0 ? hardForced : new HashSet<(int, int)>());
        if (degreeProblem is not null)
            return new ConferenceSlate
            {
                Verdict = SlateVerdict.InvalidConfiguration,
                Reason = $"{label}{degreeProblem}",
                SearchNodes = nodes,
            };

        // ★ THE ARITHMETIC RECONCILES, asserted where the counters are built rather than
        //   trusted by the page: initial preferred == retained + removed. Reaching here with
        //   the rotation active means retained is zero and every proposal was dropped.
        if (rotationActive && preferredInitial != retained + relaxations)
            return new ConferenceSlate
            {
                Verdict = SlateVerdict.InvalidConfiguration,
                Reason = $"{label}the rotation proposed {preferredInitial} preferred pair(s) but " +
                         $"kept {retained} and dropped {relaxations}",
                SearchNodes = nodes,
            };

        return FinishSlate(extra, skipped, nodes, usedCirculant,
                           keptPreferences: retained, droppedPreferences: relaxations,
                           terminal: terminalFallback);
    }

    /// <summary>★ S99 — what the chooser did to one league, carried to the slate result so a
    /// check can tell a legal slate from a bypassed chooser.</summary>
    private readonly record struct RotationSlateDiagnostics(
        bool Active, int Initial, int Retained, int Relaxations, bool TerminalFallback,
        int MemoryFlipsDropped = 0);

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
        string label, List<FixedResidualHost>? fixedHosts, long nodes, bool usedCirculant,
        int memoryFixedHosts = 0, RotationSlateDiagnostics rotationDiag = default)
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
            MemoryFixedHosts = memoryFixedHosts,
            RotationActive = rotationDiag.Active,
            RotationPreferredInitial = rotationDiag.Initial,
            RotationPreferredRetained = rotationDiag.Retained,
            RotationRelaxations = rotationDiag.Relaxations,
            RotationTerminalFallback = rotationDiag.TerminalFallback,
            MemoryFlipsDropped = rotationDiag.MemoryFlipsDropped,
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
        => BuildSeasonSchedule(world, seasonSeed, history, out _);

    /// <summary>★ S96 — the same builder, reporting what host memory did. A separate overload
    /// rather than a changed signature: sixteen existing call sites across the suite take the
    /// two- and three-argument forms and none of them care about memory.</summary>
    private static List<SeasonGame> BuildSeasonSchedule(
        WorldFile world, long seasonSeed, HistoryStore? history,
        out SeasonMemoryOutcome memoryOutcome)
        => BuildSeasonSchedule(world, seasonSeed, history, deferNumbering: false,
                               out memoryOutcome, out _);

    /// <summary>★ S99 — the same builder, additionally reporting what the rotation did. A
    /// separate overload rather than a changed signature, for the S96 reason unchanged: every
    /// existing call site takes the shorter forms and none of them care.</summary>
    private static List<SeasonGame> BuildSeasonSchedule(
        WorldFile world, long seasonSeed, HistoryStore? history, bool deferNumbering,
        out SeasonMemoryOutcome memoryOutcome)
        => BuildSeasonSchedule(world, seasonSeed, history, deferNumbering,
                               out memoryOutcome, out _);

    /// <summary>★ S97 — the numbering may now be DEFERRED, and only the production season
    /// runner defers it.
    ///
    /// <para>Why: the season used to take its official number the instant the league slates
    /// were legal, which was before the calendar had been laid over them. That was fine while
    /// nothing after the slate could refuse a season. It stopped being fine this session — a
    /// school seated in a tournament cannot also have a league game inside that window, and
    /// nothing knows what night a game is on until dating has run. A refusal that fires after
    /// the number is spent burns a season id permanently for a world that was simply authored
    /// wrong.</para>
    ///
    /// <para>So the production path builds unnumbered, dates, checks the tournament windows,
    /// checks no record already claims the pending season, and only THEN spends the number.
    /// Every other caller keeps the pre-S97 behaviour exactly, which is why this is a flag on
    /// a private overload rather than a change to the shape everyone uses. The season/game
    /// counters are independent of the person counter, so deferring moves no identity.</para></summary>
    /// <param name="debtWindowOverride">★ S100. Null — every production caller — means the full
    /// shared window. A check may cap it to run the pre-S100 one-hop rule as a negative control;
    /// it caps consumption only, so the rotation half keeps its full depth and the isolating
    /// control really does isolate the one thing that changed.</param>
    private static List<SeasonGame> BuildSeasonSchedule(
        WorldFile world, long seasonSeed, HistoryStore? history, bool deferNumbering,
        out SeasonMemoryOutcome memoryOutcome, out SeasonRotationOutcome rotationOutcome,
        int? debtWindowOverride = null)
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

        // ★ S96 — ONCE, before the loop, and therefore before ReserveSeason. That ordering is
        //   what the peek exists for: the season number this schedule will wear is read here,
        //   long before it is spent at the bottom of this method.
        // ★ S99 — and it is now ONE walk of the career file serving both halves. The window is
        //   read here rather than per league, so ninety-odd megabytes of retained logs are
        //   parsed once a season instead of thirty-two times.
        var career = ReadCareerMemory(history, RotationWindowSeasons);
        var memory = career.Hosts;
        // ★ S100 — the window is SHARED with the rotation (one depth, one read policy, one test
        //   matrix). The two halves need depth for different reasons — the rotation because
        //   second meetings are rare, host debt because a pair's run of single games gets
        //   interrupted — and they share it for coherence, not because the numbers happen to
        //   match. `debtWindowOverride` is test-only and caps CONSUMPTION, never the read, so a
        //   one-hop negative control costs no extra parse and cannot drift from production.
        var debt = career.Debt.Within(debtWindowOverride ?? RotationWindowSeasons);
        var residualsFlipped = 0;
        var leaguesFlipped = 0;
        var preferredHeld = 0;
        var rotatingLeagues = 0;
        var fellToFeasibility = 0;
        var terminalFallbacks = 0;
        var venuesGivenUp = 0;

        var games = new List<SeasonGame>();
        foreach (var c in world.Conferences.OrderBy(c => c.Id))
        {
            if (!byConf.TryGetValue(c.Id, out var members)) continue;
            var label = $"conference '{c.Name}' (id {c.Id}) ";
            var slate = BuildConferenceSlate(
                members, c.Games, c.Skip, ActiveRivalries(members, rivals, c.Games), label,
                debt: debt, rotation: career.Rotation);
            if (slate.Verdict != SlateVerdict.Feasible)
                throw new InvalidOperationException(
                    $"SEASON SCHEDULE {slate.Verdict.ToString().ToUpperInvariant()}: {slate.Reason}.");
            // Counted only past the verdict check, so these are venues that APPLIED to a
            // league that actually built — never venues that were merely offered.
            if (slate.MemoryFixedHosts > 0) { residualsFlipped += slate.MemoryFixedHosts; leaguesFlipped++; }
            venuesGivenUp += slate.MemoryFlipsDropped;
            // ★ S99 — same rule for the rotation: only a league that BUILT is counted, and
            //   the two participating outcomes partition cleanly. A league that retained at
            //   least one preference is a rotating league; one that retained none took the
            //   terminal fallback, by definition.
            if (slate.RotationActive)
            {
                preferredHeld += slate.RotationPreferredRetained;
                fellToFeasibility += slate.RotationRelaxations;
                if (slate.RotationPreferredRetained > 0) rotatingLeagues++;
                else terminalFallbacks++;
            }
            foreach (var (home, away) in slate.Games)
                games.Add(new SeasonGame("conf", home, away));
        }

        memoryOutcome = new SeasonMemoryOutcome(
            memory.Status, memory.SourceSeasonId, memory.AttemptedSeasonId, memory.Problem,
            residualsFlipped, leaguesFlipped);
        rotationOutcome = new SeasonRotationOutcome(
            history is not null, preferredHeld, rotatingLeagues, fellToFeasibility, terminalFallbacks,
            venuesGivenUp);

        if (history is null) return games;   // legacy mode: the fixtures stay unnumbered
        if (deferNumbering) return games;    // S97: the caller spends the number itself, later

        NumberSeasonSchedule(games, history);
        return games;
    }

    /// <summary>★ S97 — THE COMMIT. After this returns, the season id is spent whatever
    /// happens next: the reservation is durable and a season that then fails burns it
    /// permanently. That is S89's rule unchanged; all this session did was move the moment
    /// later, so that everything capable of refusing a season now runs before it.
    ///
    /// <para>One season number and one contiguous block of game numbers, reserved together
    /// and stamped in schedule order.</para></summary>
    private static long NumberSeasonSchedule(List<SeasonGame> games, HistoryStore history)
        => NumberSeasonSchedule(games, history, seating: null).SeasonNumber;

    /// <summary>★ S98 — what the commit hands back. The bracket reservations ride out with the
    /// season number because they are spent in the same breath and can be spent nowhere
    /// else.</summary>
    private sealed record SeasonNumbering(
        long SeasonNumber, SeasonId SeasonId,
        IReadOnlyDictionary<BracketSlotKey, GameId> Reservations);

    /// <summary>★ S98 — THE TOURNAMENT GAME NUMBERS ARE TAKEN HERE, AND THIS IS FORCED RATHER
    /// THAN PREFERRED. <c>CloseReservations()</c> runs before the first tip, so nothing can
    /// reserve an id once play begins; every tournament id must therefore be spent at this
    /// commit, alongside the conference block.
    ///
    /// <para>The pairings are unknown at this moment. The COUNT is not: twelve per active
    /// complete eight-team field, four per active complete four-team field, and ZERO for a
    /// dormant or short event — a short event cannot play, so it must not hold ids, and an id
    /// reserved for a field that never plays is wasted permanently and cannot be repaired later
    /// in the run.</para>
    ///
    /// <para>So the table is keyed by bracket POSITION and never by team: an id belongs to a
    /// slot rather than to whoever happens to win it.</para></summary>
    private static SeasonNumbering NumberSeasonSchedule(
        List<SeasonGame> games, HistoryStore history, EventSeatingOutcome? seating)
    {
        // ★ The peek's contract, asserted rather than assumed: while this run holds the
        //   lock, the value the peek returns IS the value the reservation hands back, and the
        //   counter moves by exactly one. `SeasonId`'s raw value is deliberately not public,
        //   so the counter is what proves it — which is the honest check anyway.
        var expected = history.PeekNextSeasonId;
        var seasonId = history.ReserveSeason();
        if (history.PeekNextSeasonId != expected + 1)
            throw new InvalidOperationException(
                $"SEASON INVARIANT VIOLATED: peeked season {expected} but the counter did not advance "
                + "by exactly one across the reservation.");

        var slots = seating is null ? new List<BracketSlotKey>() : MteExpectedBracketSlots(seating);
        var gameIds = history.ReserveGames(games.Count + slots.Count);
        for (var g = 0; g < games.Count; g++)
            games[g] = games[g] with { SeasonId = seasonId, GameId = gameIds[g] };

        var reservations = new Dictionary<BracketSlotKey, GameId>(slots.Count);
        for (var i = 0; i < slots.Count; i++)
            reservations[slots[i]] = gameIds[games.Count + i];
        // ★ The key set is checked AT THE POINT OF CREATION, so a surplus or a duplicated slot
        //   names itself here rather than after result routing has obscured where it came from.
        if (reservations.Count != slots.Count)
            throw new InvalidOperationException(
                $"SEASON INVARIANT VIOLATED: {slots.Count} bracket slots produced "
                + $"{reservations.Count} distinct reservations; a bracket position is unique.");

        return new SeasonNumbering(expected, seasonId, reservations);
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
    /// <param name="roadShaveOverride">★ S95. Null — every production caller — means
    /// "read the dial from config.json". Phase 86 passes a bare int so it can run the
    /// season at a chosen shave (0 for the zero-path identity check, on for determinism)
    /// WITHOUT rewriting config.json, which no check may ever do.</param>
    private static SeasonRunOutcome RunSeasonCore(
        WorldFile world, long seasonSeed, string engineConfigPath, bool verbose,
        HistoryStore? history = null, bool retainGameLog = false,
        int? roadShaveOverride = null, int? debtWindowOverride = null)
    {
        // ══════════════════════════════════════════════════════════════════════════
        //  ★ S97 — THE SEASON PIPELINE, IN THIS ORDER, AND THE ORDER IS THE CONTRACT.
        //
        //    1. peek the season about to be scheduled   (a read; nothing is spent)
        //    2. read the last four seasons' event records
        //    3. draw activation and seat every active field
        //    4. build the conference slate                        (unchanged)
        //    5. date it                                           (unchanged)
        //    6. refuse a seated school double-booked in its window
        //    7. refuse a record already claiming the pending season
        //    8. SPEND the season and game numbers                 ← the commit
        //    9. publish the permanent record
        //
        //    Anything that fails at 1-7 leaves NO season id spent and NO file written.
        //    Past 8 the season is committed: a record write that then fails does not
        //    invalidate the basketball, it leaves a deliberate hole in event history.
        // ══════════════════════════════════════════════════════════════════════════
        var pendingSeasonId = history?.PeekNextSeasonId ?? 0;
        var eventHistory = MteReadHistory(history, pendingSeasonId);
        var seating = MteSeatSeason(world, seasonSeed, eventHistory);

        // ★ S101 — classes and requests. Pure by signature (world + seating, nothing
        //   else), computed here because the event exemption needs the seating and the
        //   seating exists only past this line. Nothing downstream reads it — it rides
        //   out on the outcome and reaches the page and Phase 92, which is what makes
        //   the S101 zero-path byte-identity claim provable by construction.
        var nonConference = BuildNonConferenceRequests(world, seating);

        var schedule = BuildSeasonSchedule(
            world, seasonSeed, history, deferNumbering: true,
            out var memoryOutcome, out var rotationOutcome, debtWindowOverride);
        var fingerprint = ScheduleFingerprint(schedule);
        // ★ S94 — every game gains its night. Purely additive: the structural fingerprint
        //   above is computed from the four named fields and cannot see the date.
        var datedFingerprint = SeasonDateSchedule(world, schedule, SeasonDefaultStartYear);

        MteRefuseOverlap(world, seating, schedule);
        MteRefuseExistingRecord(history, pendingSeasonId);

        var recordStatus = EventRecordStatus.NotApplicable;
        string? recordDiagnostic = null;
        // ★ S98 — legacy mode plays its tournaments too. The fixtures simply stay unnumbered,
        //   exactly as legacy conference fixtures always have: basketball does not require a
        //   career. So the reservation table is empty here and the bracket factory asks for an
        //   id only when there is a season to hang it on.
        IReadOnlyDictionary<BracketSlotKey, GameId> reservations =
            new Dictionary<BracketSlotKey, GameId>();
        SeasonId? seasonIdOfRun = null;
        if (history is not null)
        {
            var numbering = NumberSeasonSchedule(schedule, history, seating);
            if (numbering.SeasonNumber != pendingSeasonId)
                throw new InvalidOperationException(
                    $"SEASON INVARIANT VIOLATED: peeked season {pendingSeasonId} but reserved " +
                    $"{numbering.SeasonNumber}.");
            reservations = numbering.Reservations;
            seasonIdOfRun = numbering.SeasonId;
            try
            {
                MtePublishRecord(history, numbering.SeasonNumber, seasonSeed, world, seating);
                recordStatus = EventRecordStatus.Written;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                recordStatus = EventRecordStatus.WriteFailed;
                recordDiagnostic = ex.GetType().Name;
            }
        }

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
        // ★ S95 — the home-court dial, read ONCE here and carried into the loop as a
        //   plain value. The loop never touches disk; 5,205 games do not re-read a file
        //   to learn the same integer 5,205 times.
        var roadShave = roadShaveOverride ?? HomeCourtConfig.Load(engineConfigPath).RoadShave;

        var wins = world.Schools.ToDictionary(s => s.Id, _ => 0);
        var losses = world.Schools.ToDictionary(s => s.Id, _ => 0);
        var results = new List<SeasonGameResult>(schedule.Count);
        var possessionCounts = new List<int>(schedule.Count);
        var hostedRoadSidesShaved = 0;
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

        var playedGames = new List<PlayedSeasonGame>(schedule.Count);

        //  ★ S98 — ONE GAME, RUN ONCE, WHATEVER KIND IT IS. This body is the pre-S98 loop
        //  body lifted whole into a local function so that a tournament fixture goes through
        //  the identical path: same side construction, same seed arithmetic, same
        //  accumulators, same retention block. The ONLY thing that differs between a
        //  conference game and a bracket game is what the fixture says about itself.
        //
        //  `pg.FixtureOrdinal` replaces the old loop variable `g` and is the same number for
        //  every conference game it ever was, which is what keeps the conference half of the
        //  season byte-identical.
        (int HomeScore, int AwayScore) PlayOneGame(PlayedSeasonGame pg)
        {
            var sg = pg.Game;
            var g = pg.FixtureOrdinal;
            // HomeSchool -> the engine's Home side, AwaySchool -> Away: the §1b
            // invariant. Home rows stamp PlayerIds 1..RosterShape.Size, away rows the
            // next Size — at S75's 13-man roster that is 1-13 and 14-26 (the comment
            // here said 1-10 / 11-20 until S77; the numbers had been wrong since S75
            // and sat directly above the code that maps them). Ids need uniqueness only
            // within a game; sides are stamped per matchup, never cached across games
            // where a school flips sides.
            var seatedHome = BuildSeasonSide(rowsBySchool[sg.HomeId], 0);
            var seatedAway = BuildSeasonSide(rowsBySchool[sg.AwayId], RosterShape.AwayIdOffset);
            // ★ S98 — home court, now read from the SCHEDULE'S OWN SITE FACT. This was a
            //   literal `true` from S95 until this session, with a note saying the tournament
            //   layer would replace it and nothing else here would move. That is exactly what
            //   happened: a conference game still says it has a host, a bracket game says it
            //   has none, and the shave path returns both sides untouched for the latter.
            var (sideHome, sideAway, awayShaved) =
                PrepareSeasonGameSides(seatedHome, seatedAway, roadShave, hasHost: sg.HasHost);
            if (awayShaved) hostedRoadSidesShaved++;
            // Everything below — the engine, the identity bundle, the occupancy walk —
            // reads the PREPARED sides, so the men who are attributed are the men who
            // played. Name and PlayerId survive the shave untouched, which is what keeps
            // the S77 Gate 2 identity comparison two independent paths to one person.
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
            possessionCounts.Add(result.Possessions.Count);
            playedGames.Add(pg);
            if (game.HomeScore > game.AwayScore) { wins[sg.HomeId]++; losses[sg.AwayId]++; }
            else if (game.AwayScore > game.HomeScore) { wins[sg.AwayId]++; losses[sg.HomeId]++; }
            else ties++;   // assumption-1 says impossible; counted so it can never hide

            return (game.HomeScore, game.AwayScore);
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ EXECUTED LAST, DATED FIRST — see the header of Program.Season.Brackets.cs.
        //
        //  The conference slate plays at loop index g = 0 .. N-1, UNCHANGED and bit for
        //  bit, so `baseSeed + 2g` hands every conference game the exact seeds it had
        //  before this session existed. The tournament games APPEND at N .. N+M-1 and
        //  carry NOVEMBER DATES.
        //
        //  This looks wrong and is correct. `g` is both the engine seed input and the
        //  retention log's fixture ordinal, so slotting tournament games into calendar
        //  order would shift every conference index and re-roll the entire season's
        //  basketball. Do not "fix" it.
        // ══════════════════════════════════════════════════════════════════════════
        for (var g = 0; g < schedule.Count; g++)
        {
            PlayOneGame(new PlayedSeasonGame(schedule[g], g));
            if (verbose && (g + 1) % 500 == 0)
                Console.WriteLine($"  ... {g + 1}/{schedule.Count} conference games played");
        }

        var prestigeById = world.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
        var brackets = MtePlayBrackets(
            seating, prestigeById, reservations, seasonIdOfRun, schedule.Count,
            pg => PlayOneGame(pg));
        if (verbose && brackets.GameCount > 0)
            Console.WriteLine($"  ... {brackets.GameCount} tournament games played");

        //  One block per fixture PLAYED and no other — conference plus tournament. The
        //  writer refuses to publish a partial season rather than leaving a
        //  plausible-looking short file behind.
        if (gameLog is not null)
        {
            gameLog.Finalize(schedule.Count + brackets.GameCount);
            gameLog.Dispose();
        }

        //  ★ S98 — THE SECOND WRITE, and the order below is the resolution of a real
        //  contradiction: the record replacement cannot be the last write AND have its
        //  failure reported on the page. It is not. The replacement is attempted here,
        //  its outcome is carried out on the run result, and the page — which prints
        //  after this method returns and after the career file is closed — says what
        //  happened. A failure leaves the NotPlayed record byte-identical, the season
        //  valid and played, and no retry inside the run.
        var finishStatus = EventRecordStatus.NotApplicable;
        string? finishDiagnostic = null;
        if (history is not null && recordStatus == EventRecordStatus.Written
            && brackets.FinishBySeat.Count > 0)
        {
            try
            {
                MteReplaceRecordWithFinishes(
                    history, pendingSeasonId, brackets.FinishBySeat, brackets.SeatsPlayed);
                finishStatus = EventRecordStatus.Written;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                finishStatus = EventRecordStatus.WriteFailed;
                finishDiagnostic = ex.GetType().Name;
            }
        }

        var eventOutcome = new EventSeasonOutcome(
            seating, recordStatus, recordDiagnostic, eventHistory.Diagnostics,
            finishStatus, finishDiagnostic);

        return new SeasonRunOutcome
        {
            Schedule = schedule, Fingerprint = fingerprint, Results = results,
            Wins = wins, Losses = losses, Divvy = divvy, League = league, Ties = ties,
            PossessionCounts = possessionCounts,
            HostedRoadSidesShaved = hostedRoadSidesShaved,
            RoadShave = roadShave,
            DatedFingerprint = datedFingerprint,
            Memory = memoryOutcome,
            Rotation = rotationOutcome,
            Events = eventOutcome,
            NonConference = nonConference,
            PlayedGames = playedGames,
            ConferenceGameCount = schedule.Count,
            TournamentGameCount = brackets.GameCount,
            EventGamesFingerprint =
                MteEventGamesFingerprint(playedGames, results, possessionCounts),
            EventFinishes = brackets.FinishBySeat,
        };
    }

    // ── The page ──────────────────────────────────────────────────────────────────

    private static readonly (int Lo, int Hi)[] SeasonBands =
        { (0, 19), (20, 39), (40, 59), (60, 79), (80, 99) };   // the divvy's exact five bands

    // ══════════════════════════════════════════════════════════════════════════════
    //  ★ S98 — THE STANDINGS ORDER, EXTRACTED SO THE SUITE CAN ASSERT IT.
    //
    //  These are not page decoration; they are the rule Emmett ruled on, and a rule the
    //  suite cannot reach is a rule nothing defends. The page calls exactly these, so
    //  what Phase 89 asserts is what prints.
    //
    //  Note what is and is not asserted: the MECHANISM (percentage not raw wins, the
    //  tie-break, where a school that played nothing sorts, who is inside a band
    //  average) is suite-asserted. No basketball VALUE ever is — page-only calibration
    //  is untouched.
    // ══════════════════════════════════════════════════════════════════════════════

    private static int SeasonGamesPlayed(SeasonRunOutcome run, int schoolId)
        => run.Wins[schoolId] + run.Losses[schoolId];

    /// <summary>★ A SCHOOL THAT PLAYED NOTHING PRINTS AN EM DASH, never <c>.000</c> — which
    /// would say it lost.</summary>
    private static string SeasonWinPctText(SeasonRunOutcome run, int schoolId)
        => SeasonGamesPlayed(run, schoolId) == 0
            ? "—"
            : (100.0 * run.Wins[schoolId] / SeasonGamesPlayed(run, schoolId))
                .ToString("00.0", CultureInfo.InvariantCulture);

    /// <summary>★ Win PERCENTAGE, by INTEGER CROSS-MULTIPLICATION and never a float division:
    /// <c>winsA * playedB</c> against <c>winsB * playedA</c>, widened to <c>long</c>
    /// deliberately. The ordering is then exact and platform-independent, which a double
    /// comparison is not. Ties break on the LOWER SCHOOL ID — the canonical tie-break
    /// everywhere in this codebase. A school with no games sorts below everyone who played,
    /// and two of them break by id.</summary>
    private static Comparison<int> SeasonStandingsOrder(SeasonRunOutcome run) => (a, b) =>
    {
        var pa = SeasonGamesPlayed(run, a);
        var pb = SeasonGamesPlayed(run, b);
        if (pa == 0 || pb == 0) return pa == pb ? a.CompareTo(b) : (pa == 0 ? 1 : -1);
        var cmp = ((long)run.Wins[b] * pa).CompareTo((long)run.Wins[a] * pb);
        return cmp != 0 ? cmp : a.CompareTo(b);
    };

    /// <summary>★ Average win percentage per prestige band, over schools that ACTUALLY PLAYED
    /// (Emmett's ruling, S98). A school that never took the floor is not a school that went
    /// winless, so it cannot be allowed to drag its band down — it is left out of the average
    /// entirely, and out of the escapes list that reads these averages.</summary>
    private static Dictionary<(int Lo, int Hi), double> SeasonBandWinPct(
        WorldFile world, SeasonRunOutcome run, out Dictionary<(int Lo, int Hi), int> counts)
    {
        var avgs = new Dictionary<(int Lo, int Hi), double>();
        counts = new Dictionary<(int Lo, int Hi), int>();
        foreach (var band in SeasonBands)
        {
            var members = world.Schools
                .Where(s => s.CurrentPrestige >= band.Lo && s.CurrentPrestige <= band.Hi)
                .Select(s => s.Id)
                .Where(id => SeasonGamesPlayed(run, id) > 0)
                .ToList();
            if (members.Count == 0) continue;
            avgs[band] = members.Average(id => 100.0 * run.Wins[id] / SeasonGamesPlayed(run, id));
            counts[band] = members.Count;
        }
        return avgs;
    }

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
            // ★ S96 — the two fingerprint lines USED TO PRINT HERE, off this preflight
            //   schedule. That was safe only while a schedule was a pure function of the
            //   world: host memory can now move a venue, so the preflight (which is built
            //   with no career attached and therefore no memory) and the schedule that
            //   actually plays are two different schedules. Both lines moved below the run,
            //   where they describe the games that happened. See the block after the loop.
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
            // ★ S95 — the second clause used to read "Neutral floors throughout (the road
            //   seam is 0)". That stopped being true this session. The S93 lesson applies:
            //   a banner that restates a constant keeps saying it long after it is false,
            //   so this now says what the schedule IS and the measured line below says
            //   what the dial DID.
            // ★ S98 — this line said "every game on this schedule is a real home game;
            //   neutral floors arrive with the tournament layer". They arrived. The S93
            //   lesson applies again: a banner that restates a constant keeps saying it long
            //   after it is false, so it now says what the CONFERENCE schedule is and leaves
            //   the tournament count to the block below, which knows it.
            Console.WriteLine(
                "  General non-conference scheduling does not exist yet — it is its own session. " +
                "Every game on the conference slate is a real home game; the early-season " +
                "tournaments play on neutral floors and are counted after it.");
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

        // ★ S96 — the schedule that PLAYED, described after the fact. The dated line keeps
        //   its S94 content: season year, dated fingerprint, and how much of the country
        //   plays in December.
        {
            var decemberGames = run.Schedule.Count(x => x.Date is { Month: 12 });
            // ★ S98 — BOTH LINES ARE RELABELLED, because after this session the old name is
            //   a lie. A bracket cannot be built before it is played (round two's pairings are
            //   round one's results), so tournament fixtures cannot exist when the schedule
            //   fingerprint is computed. It is now conference-only and says so, and the other
            //   half of the season gets its own hash beside it.
            Console.WriteLine($"Conference schedule fingerprint: {run.Fingerprint}");
            if (run.TournamentGameCount > 0)
                Console.WriteLine($"Tournament games fingerprint: {run.EventGamesFingerprint} " +
                                  $"({run.TournamentGameCount} games)");
            Console.WriteLine($"Dated: season {SeasonDefaultStartYear}-{SeasonDefaultStartYear + 1}, " +
                              $"{decemberGames} December games, dated fingerprint {run.DatedFingerprint}");
            // ★ PAGE-ONLY AND RUNTIME-DERIVED. Every number on this line comes from the run
            //   that produced it; no measured league constant appears here, and a category
            //   word is printed where a raw exception message must never be. Legacy mode
            //   prints nothing at all — there is no career for the line to be about.
            var memoryLine = HostMemoryPageLine(run.Memory);
            if (memoryLine is not null) Console.WriteLine(memoryLine);
            // ★ S99 — beside it, on the same terms, and printed even when every number is
            //   zero: a career with no usable history reads "0 preferred pairs held", which
            //   is a different fact from a legacy run that prints no line at all.
            var rotationLine = RotationPageLine(run.Rotation);
            if (rotationLine is not null) Console.WriteLine(rotationLine);
            Console.WriteLine();
        }

        // ★ S101 — the non-conference request block. PAGE-ONLY: every number derives from
        //   the report the run carried out; nothing here is ever suite-asserted (Phase 92
        //   asserts the report OBJECT — wiring and arithmetic, never rendered prose, never
        //   a basketball calibration value). The balance line is the finding this session
        //   exists to print: the class curve stays open (brief §8.1) until Emmett settles
        //   it by reading this page.
        {
            foreach (var line in NonConferencePageLines(run.NonConference))
                Console.WriteLine(line);
            Console.WriteLine();
        }

        // ★ S97 — the tournament block. PAGE-ONLY: no field composition is ever asserted by
        //   the suite. When the world authors no events this prints NOTHING — not a heading,
        //   not a blank line — which is what makes the zero-path byte-identity claim honest.
        {
            var eventLines = MtePageLines(run.Events, run.EventFinishes);
            if (eventLines.Count > 0)
            {
                Console.WriteLine("--- EARLY-SEASON EVENTS (played out to a full placement; " +
                                  "neutral floors, nobody hosts) ---");
                foreach (var line in eventLines) Console.WriteLine(line);
                Console.WriteLine();
            }
        }

        // ★ S95 — the home-court readout. PAGE-ONLY, NEVER ASSERTED: the ratified 59%
        //   is a calibration target, and the page-only principle keeps basketball target
        //   values out of the suite entirely.
        //
        // ★ S98 — THE DENOMINATOR NOW READS THE SITE FACT, exactly as the S95 note said it
        //   would. It counts games whose fixture says it HAS a host, not the conference
        //   prefix. Those are the same set today; they will stop being the same set the
        //   moment the schedule owns more site facts, and reading the fact is free while
        //   reading the prefix is a shortcut that would silently rot. Neutral games are
        //   excluded outright — a tournament game has no home team to win, so counting it
        //   would drag a measured home-court number toward 50% and look like a regression.
        {
            var hosted = run.PlayedGames
                .Where(p => p.Game.HasHost)
                .Select(p => run.Results[p.FixtureOrdinal])
                .ToList();
            var neutral = run.PlayedGames.Count - hosted.Count;
            var neutralNote = neutral > 0
                ? $" ({neutral.ToString(CultureInfo.InvariantCulture)} neutral-floor games excluded)"
                : "";
            if (hosted.Count == 0)
            {
                Console.WriteLine($"Home court: road shave {run.RoadShave} — " +
                                  "home wins 0/0 = n/a, margin n/a" + neutralNote);
            }
            else
            {
                var homeWins = hosted.Count(r => r.HomeScore > r.AwayScore);
                var pct = 100.0 * homeWins / hosted.Count;
                var margin = hosted.Average(r => (double)r.HomeScore - r.AwayScore);
                Console.WriteLine($"Home court: road shave {run.RoadShave} — home wins " +
                                  $"{homeWins}/{hosted.Count} = " +
                                  $"{pct.ToString("0.0", CultureInfo.InvariantCulture)}%, margin " +
                                  margin.ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture) +
                                  neutralNote);
            }
            Console.WriteLine();
        }

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

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ S98 — EVERY RANKING ON THIS PAGE MOVES TO WIN PERCENTAGE (Emmett's ruling).
        //
        //  Schedules stop being uniform this session. A school in a tournament plays
        //  three more games than one that is not, and under a raw-win ranking it would be
        //  rewarded simply for having played more. Percentage is the only honest order.
        //
        //  The comparison is INTEGER CROSS-MULTIPLICATION — winsA * playedB against
        //  winsB * playedA, widened to long deliberately — never a float division. The
        //  ordering is then exact and platform-independent, which a double is not.
        //
        //  ★ A SCHOOL THAT PLAYED NOTHING IS NOT A SCHOOL THAT LOST. It prints an em dash
        //  rather than .000, sorts below everyone who played, and — Emmett's ruling — is
        //  left OUT of the band averages and out of the escapes list entirely. It never
        //  played, so it cannot make its prestige band look worse. (The stock world has
        //  fourteen such schools, sitting in a league authored at zero games.)
        // ══════════════════════════════════════════════════════════════════════════
        int Played(int id) => SeasonGamesPlayed(run, id);
        string Pct(int id) => SeasonWinPctText(run, id);
        var ByWinPct = SeasonStandingsOrder(run);

        // (i) the full ranked table — every school by win percentage, ties broken by school id.
        Console.WriteLine($"--- STANDINGS (all {world.Schools.Count}, ranked by WIN PCT; ties broken by " +
                          "school id; a school that played nothing sorts last) ---");
        var ranked = world.Schools.Select(s => s.Id).ToList();
        ranked.Sort(ByWinPct);
        Console.WriteLine($"  {"rk",-5}{"school",-26}{"conf",-10}{"pres",5}   {"W-L",-8}pct");
        for (var i = 0; i < ranked.Count; i++)
        {
            var id = ranked[i];
            Console.WriteLine($"  {i + 1,-5}{names[id] + " (" + abbrs[id] + ")",-26}" +
                              $"{confShort[confOf[id]],-10}{prestige[id],5}   " +
                              $"{run.Wins[id] + "-" + run.Losses[id],-8}{Pct(id)}");
        }
        Console.WriteLine();

        // (ii) the proof table — average WIN PCT by prestige band (the divvy's bands),
        //      over schools that actually played.
        Console.WriteLine("--- PROOF TABLE (prestige buys access; does access buy wins?) ---");
        var bandAvg = SeasonBandWinPct(world, run, out var bandN);
        foreach (var band in SeasonBands)
        {
            if (!bandAvg.TryGetValue(band, out var avg)) continue;
            Console.WriteLine($"  prestige {band.Lo,2}-{band.Hi,-2}  avg win pct {avg,5:F1}   (n={bandN[band]})");
        }
        Console.WriteLine();

        // (iii) the escapes — top 10 by win pct over band average, leaked talent named.
        Console.WriteLine("--- ESCAPES (top 10 by win pct over band average; leaked top-decile talent named) ---");
        var ranks = run.Divvy.Pool.Select(p => p.ScoutRank).ToArray();
        var topDecile = new HashSet<int>(
            Enumerable.Range(0, ranks.Length).OrderByDescending(i => ranks[i]).Take(ranks.Length / 10));
        (int, int) BandOf(int p) => SeasonBands.First(b => p >= b.Lo && p <= b.Hi);
        var escapes = world.Schools
            .Where(s => Played(s.Id) > 0 && bandAvg.ContainsKey(BandOf(s.CurrentPrestige)))
            .Select(s => (s.Id, Dev: 100.0 * run.Wins[s.Id] / Played(s.Id)
                                     - bandAvg[BandOf(s.CurrentPrestige)]))
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
                              $"{run.Wins[id]}-{run.Losses[id]}, {dev:+0.0;-0.0} pts over band | " +
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
