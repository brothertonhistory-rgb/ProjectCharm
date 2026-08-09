using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S106 — NIGHTS: every non-conference game gets a date (O-92 session 6a).
//
//  Every non-conference pairing carried an opponent and a host and NOTHING ELSE.
//  Conference play has been dated since S94; every event owns its window. This
//  file joins them, so a school's year reads start to finish for the first time.
//
//  ★ THE SPEC IS tools/nonconference_dates_oracle.py. That file's docstring is
//  the authority; this is its port, and Phase 97 proves it game for game against
//  tools/nonconference_dates_golden.json. Where the two disagree the oracle is
//  right.
//
//  ★ S106 CHANGES *WHEN*, AS HARD AS IT NEEDS TO — NEVER WHO, NEVER WHERE, NEVER
//  A RESULT. Sites, cities and semi-home are S107. A neutral game gets a date and
//  no city.
//
//  ★ A2 — IT CONSUMES PAIRINGS AND NEVER CREATES, DISSOLVES OR RE-HOSTS ONE.
//  There are no cancellations (Emmett's ruling). A pairing that cannot be seated
//  is REPORTED with its failure class — three of the four classes are design
//  findings and only the last is an implementation bug.
//
//  ★ A3 — CONFERENCE DATES AND EVENT WINDOWS ARE IMMOVABLE. They are read-only
//  input here; nothing in this file nudges a league game or an event to make
//  room, which is why the S94 dated fingerprint cannot move because of it.
//
//  ★ PURELY ADDITIVE BY CONSTRUCTION. This layer reads the dated schedule, the
//  seating and the matching report, and writes only its own result object onto
//  the outcome as page cargo. It mutates nothing any earlier fingerprint hashes,
//  so `6f79d663…`, `46d89bf8…` and `898d9fe8…` are conserved by structure rather
//  than by a promise.
//
//  ★ NO RNG AND NO RANDOMIZED SEARCH. Every ordering is total and every tie-break
//  is explicit — a spec that anneals is not a spec, and a port of one could not
//  be proved. All ordering here mirrors the oracle's literally, including the
//  stable secondary sort in the seating pass.
// ============================================================================

internal static partial class Program
{
    // ── R8: the one seam. Every tunable number of S106 lives here. ──────────────────

    /// <summary>R-n1 — at most THREE games in a Mon-Sun week, counting ALL of a team's
    /// non-event games, conference and non-conference together (Emmett: "All games").
    /// Event games do not count; the conference weekday/weekend rule still binds the
    /// league games inside the same week and this sits on top as a total ceiling.</summary>
    private const int NonConWeeklyLoadCeiling = 3;

    /// <summary>R-n2 — one clear day either side of an ordinary non-conference game, i.e.
    /// a gap of at least two calendar days, whatever kind of game is adjacent. Saturday
    /// league game then Monday buy game is LEGAL; then Sunday is not.
    /// <para>★ League-vs-league adjacency is NOT governed here. The conference dater owns
    /// it and the Ivy Friday/Saturday pair stays legal — this file never compares two
    /// conference games to each other, which makes that exemption structural rather than
    /// a special case somebody could delete.</para></summary>
    private const int NonConSpacingClearDays = 1;

    /// <summary>R-n3 — two clear days either side of an event window, symmetric: the
    /// previous ordinary game on or before FirstDay-3, the next on or after LastDay+3.
    /// <para>★ EXACT ARITHMETIC, NOT AN ESTIMATE. Every seated team plays every round and
    /// the final round is asserted onto the window's LastDay (Program.Season.Brackets.cs
    /// MteRoundDate), so a window's last night is genuinely its last night.</para></summary>
    private const int NonConEventClearDays = 2;

    /// <summary>R-n8 — THE SEATING BEND (Emmett's ruling, 2026-08-08). A pairing whose
    /// quota week holds no night both calendars share slides to the nearest week that
    /// does, and never further than this many weeks.
    /// <para>The cause was measured before the rule was made: a school playing two league
    /// games in a week has the whole week closed by R-n2 except one night, and two such
    /// schools paired into the same week can hold no night in common. On the stock world
    /// 46 pairings were in exactly that state. Hard quotas would fail them; the bend seats
    /// them and PRINTS what it cost.</para></summary>
    private const int NonConSlideRadius = 3;

    /// <summary>★ EMMETT'S CURVE (2026-08-08) — ONE national calendar shape, authored as
    /// WEIGHTS and allocated as exact quotas (R-n4). Busy November, a Thanksgiving week
    /// held down because a third of the country is at an event, an exam dip, ZERO on the
    /// Christmas week, and a January hand-off past each league opener.
    ///
    /// <para>★ THE JANUARY TAIL IS LOAD-BEARING, NOT DECORATION. Emmett's ruling followed
    /// a measurement: with the tail at weight 1-2 the allocator floored it to nothing —
    /// a school owing twelve games gets 0.3 of a game in a weight-2 week — every January
    /// week came out EMPTY, and all fourteen Independents became undatable, short by
    /// 2-5 games each. That failure is arithmetic and not search: an Independent owes 29
    /// games against a member's 12 and draws its nights from the same November-heavy
    /// pool, so at the national shape its busiest week wanted 5.5 games against a ceiling
    /// of 3. Below roughly weight 7 on this curve a week is a week you wrote down and
    /// will not get. Do not thin this tail without re-running the Independents.</para>
    ///
    /// <para>Keyed by MONTH and DAY-OF-MONTH of the week's Monday, resolved onto whatever
    /// season year is being played — never a hardcoded 2026.</para></summary>
    private static readonly (int Month, int Day, int Weight)[] NonConCurve =
    {
        (11,  2, 10),   // opening week
        (11,  9, 13),   // the heaviest stretch
        (11, 16, 13),
        (11, 23, 11),   // Thanksgiving — the events absorb a third of the country
        (11, 30, 11),
        (12,  7,  8),   // league play opens; the buy games continue
        (12, 14,  5),   // the exam dip
        (12, 21,  0),   // ★ Christmas — forced to zero by R-n4 regardless
        (12, 28,  6),   // the post-Christmas buy-game bump
        ( 1,  4,  5),   // ★ the tail — see the note above
        ( 1, 11,  4),
        ( 1, 18,  4),
        ( 1, 25,  3),
        ( 2,  1,  3),
    };

    /// <summary>★ THE CANONICAL SOURCE RANK, an INTERNAL ORDERING and never a display
    /// string. The full-season fingerprint sorts on it, so a later session renaming what
    /// the page prints must not be able to reorder the hash.</summary>
    private static int NonConSourceRank(string source) => source switch
    {
        "conference" => 0,
        "tournament" => 1,
        "showcase" => 2,
        "contract" => 3,
        "exchange" => 4,
        "matched" => 5,
        _ => throw new InvalidOperationException($"S106: unknown source kind '{source}'."),
    };

    // ── The report ──────────────────────────────────────────────────────────────────

    /// <summary>Why one pairing found no night. ★ A2 — three of these four are DESIGN
    /// FINDINGS for Emmett and only <see cref="SearchDefect"/> is an implementation bug.
    /// Under every one of them cancellation and re-pairing remain forbidden.</summary>
    private enum NonConFailureClass
    {
        EndpointPhysicalCapacity,
        QuotaIncompatibility,
        EmptyPairwiseDateIntersection,
        SearchDefect,
    }

    private sealed record NonConUnseated(int PairIndex, int HostId, int VisitorId,
                                         NonConFailureClass Class);

    /// <summary>One dated non-conference game. The site is deliberately absent: S107 adds
    /// cities as enrichment and must not be able to reorder anything here.</summary>
    private sealed record NonConDatedGame(
        int PairIndex, string Kind, int HostSchoolId, int VisitorSchoolId,
        DateOnly Date, int WeeksSlid);

    /// <summary>Everything S106 decides. Page-only cargo on exactly the terms S102's
    /// report is: the page renders what this holds and nothing else, and the phase asserts
    /// this object rather than rendered prose.</summary>
    private sealed class NonConDateReport
    {
        public required IReadOnlyList<NonConDatedGame> Games { get; init; }
        public required IReadOnlyList<NonConUnseated> Unseated { get; init; }

        /// <summary>★ HOW FAR THE CALENDARS PUSHED THE QUOTAS OFF THE PURE CURVE:
        /// ½·Σ|capacityAware − pureWeights|, summed over the country.</summary>
        public required double AllocationBend { get; init; }

        /// <summary>★ HOW MANY WEEK-STEPS GAMES HAD TO SLIDE to find a night (R-n8).
        /// <para>★ A DIFFERENT NUMBER FROM <see cref="AllocationBend"/>, deliberately not
        /// sharing its name: that one measures the QUOTAS being bent by capacity before
        /// anything is seated, this one measures GAMES being moved off their quota week
        /// because two calendars held no night in common. Reading one for the other would
        /// be a real misdiagnosis, which is why they are two words.</para></summary>
        public required int SeatingBend { get; init; }

        /// <summary>How many games slid 0, 1, 2 … weeks. Indexed by distance.</summary>
        public required IReadOnlyList<int> BendHistogram { get; init; }

        /// <summary>Schools whose owed games exceed their whole window's capacity — an
        /// A1b failure, reported and never thrown.</summary>
        public required IReadOnlyList<(int SchoolId, int Short)> AllocationShortfalls { get; init; }

        public required string DatedFingerprint { get; init; }

        public static readonly NonConDateReport Empty = new()
        {
            Games = Array.Empty<NonConDatedGame>(),
            Unseated = Array.Empty<NonConUnseated>(),
            AllocationBend = 0, SeatingBend = 0,
            BendHistogram = Array.Empty<int>(),
            AllocationShortfalls = Array.Empty<(int, int)>(),
            DatedFingerprint = "",
        };
    }

    // ── Calendar helpers ────────────────────────────────────────────────────────────

    /// <summary>★ REUSED, NEVER RESTATED — the Christmas week is the Mon-Sun week
    /// containing December 25, exactly as the S94 dater defines it. Two definitions of one
    /// week is how the two layers would quietly disagree.</summary>
    private static DateOnly NonConChristmasWeek(int startYear)
        => SeasonMonday(new DateOnly(startYear, 12, 25));

    /// <summary>R4 — November 1 is the first legal day of play.</summary>
    private static DateOnly NonConSeasonFloor(int startYear)
        => new(startYear, CharmCalendar.FirstLegalMonth, CharmCalendar.FirstLegalDay);

    /// <summary>A curve entry's Monday resolved onto the season being played: months from
    /// July on belong to the opening civil year, the rest to the following one — the same
    /// halving <see cref="MteWindowDate"/> uses, so a window and a weight compare.</summary>
    private static DateOnly NonConCurveMonday(int month, int day, int startYear)
        => new(month >= 7 ? startYear : startYear + 1, month, day);

    // ── The computation ─────────────────────────────────────────────────────────────

    /// <summary>Dates every non-conference pairing. Reads the dated conference schedule,
    /// the event seating and the matching report; writes nothing back to any of them.</summary>
    private static NonConDateReport DateNonConferenceGames(
        WorldFile world, MatchingReport matching, ContractSeasonOutcome contracts,
        EventSeatingOutcome seating, IReadOnlyList<SeasonGame> datedSchedule, int startYear)
    {
        var schools = world.Schools.Select(s => s.Id).OrderBy(x => x).ToList();
        if (matching.Pairs.Count == 0) return NonConDateReport.Empty;

        var confGames = world.Conferences.ToDictionary(c => c.Id, c => c.Games);
        var isIndependent = world.Schools.ToDictionary(s => s.Id, s => confGames[s.ConferenceId] == 0);

        // ── the pairing set, in the matcher's own order. A2: consumed, never created ──
        var pairs = matching.Pairs
            .Select(p => (Host: p.HostSchoolId, Visitor: p.VisitorSchoolId, p.Kind)).ToList();
        foreach (var leg in contracts.Exercised)
        {
            var host = leg.HostId ?? leg.SchoolAId;
            var visitor = host == leg.SchoolAId ? leg.SchoolBId : leg.SchoolAId;
            pairs.Add((host, visitor, "Contract"));
        }

        // ── A3: the immovable calendar, read only ──
        var fixedNights = schools.ToDictionary(s => s, _ => new HashSet<DateOnly>());
        foreach (var g in datedSchedule)
        {
            if (g.Date is not { } d) continue;
            fixedNights[g.HomeId].Add(d);
            fixedNights[g.AwayId].Add(d);
        }
        var windows = schools.ToDictionary(s => s, _ => new List<(DateOnly First, DateOnly Last)>());
        foreach (var e in seating.Active)
            foreach (var seat in e.Seats)
                windows[seat.SchoolId].Add((MteWindowDate(e.FirstDay), MteWindowDate(e.LastDay)));

        // ── the nights an ordinary game may NOT take, from the immovable calendar alone ──
        var blocked = new Dictionary<int, HashSet<DateOnly>>();
        foreach (var s in schools)
        {
            var bad = new HashSet<DateOnly>();
            foreach (var d in fixedNights[s])
                for (var k = -NonConSpacingClearDays; k <= NonConSpacingClearDays; k++)
                    bad.Add(d.AddDays(k));
            foreach (var (first, last) in windows[s])
                for (var d = first.AddDays(-NonConEventClearDays);
                     d <= last.AddDays(NonConEventClearDays); d = d.AddDays(1))
                    bad.Add(d);
            blocked[s] = bad;
        }

        // ── the weeks, and the horizon READ off the league season rather than authored ──
        var horizon = datedSchedule.Where(g => g.Date is not null).Max(g => g.Date!.Value);
        var weeks = new List<DateOnly>();
        for (var w = SeasonMonday(NonConSeasonFloor(startYear)); w <= horizon; w = w.AddDays(7))
            weeks.Add(w);
        var xmas = NonConChristmasWeek(startYear);
        var floor = NonConSeasonFloor(startYear);

        var weights = new int[weeks.Count];
        foreach (var (m, day, weight) in NonConCurve)
        {
            var monday = NonConCurveMonday(m, day, startYear);
            var i = weeks.IndexOf(monday);
            if (i >= 0) weights[i] = weight;
        }

        // ── per-week physical capacity: NEVER a raw count of free nights, because three
        //    open nights in a row seat one game and not three ──
        var capacity = new Dictionary<int, int[]>();
        foreach (var s in schools)
        {
            var per = new int[weeks.Count];
            for (var i = 0; i < weeks.Count; i++)
            {
                var league = 0;
                var open = new List<DateOnly>();
                for (var k = 0; k < 7; k++)
                {
                    var d = weeks[i].AddDays(k);
                    if (fixedNights[s].Contains(d)) league++;
                    if (d >= floor && d <= horizon && !blocked[s].Contains(d)) open.Add(d);
                }
                var run = 0;
                DateOnly? last = null;
                foreach (var d in open)
                    if (last is null || d.DayNumber - last.Value.DayNumber >= 2) { run++; last = d; }
                per[i] = Math.Max(0, Math.Min(NonConWeeklyLoadCeiling - league, run));
            }
            capacity[s] = per;
        }

        var owed = schools.ToDictionary(s => s, _ => 0);
        foreach (var (h, v, _) in pairs) { owed[h]++; owed[v]++; }

        // ── R-n4: exact quotas, and the allocation bend against the pure curve ──
        var quotas = new Dictionary<int, int[]>();
        var shortfalls = new List<(int, int)>();
        var allocationBend = 0.0;
        foreach (var s in schools)
        {
            if (owed[s] == 0) { quotas[s] = new int[weeks.Count]; continue; }
            var q = NonConAllocate(owed[s], capacity[s], weights, weeks, xmas, out var shortBy);
            quotas[s] = q;
            if (shortBy > 0) shortfalls.Add((s, shortBy));
            // ★ "PURE WEIGHTS" MEANS THE CURVE ALONE — capacity ignored entirely, so the
            //   comparison is against the shape Emmett authored rather than against one
            //   his calendars had already edited. Summed over EVERY week, not only the
            //   weeks that survived capacity: a week the curve wanted and the calendar
            //   could not give is exactly the displacement this number exists to report,
            //   and restricting the sum to surviving weeks hides it.
            var unlimited = Enumerable.Repeat(int.MaxValue, weeks.Count).ToArray();
            var pure = NonConAllocate(owed[s], unlimited, weights, weeks, xmas, out _);
            allocationBend += 0.5 * Enumerable.Range(0, weeks.Count)
                                              .Sum(i => Math.Abs(q[i] - pure[i]));
        }

        // ── the search ──
        var assign = NonConAssignWeeks(pairs, schools, quotas, capacity, weights, weeks,
                                       xmas, owed, isIndependent, startYear);
        var seated = NonConSeatNights(pairs, schools, assign, blocked, fixedNights, weeks,
                                      xmas, floor, horizon, owed);

        var games = new List<NonConDatedGame>();
        var unseated = new List<NonConUnseated>();
        var histogram = new int[NonConSlideRadius + 1];
        for (var gi = 0; gi < pairs.Count; gi++)
        {
            var (h, v, kind) = pairs[gi];
            if (seated.TryGetValue(gi, out var hit))
            {
                games.Add(new NonConDatedGame(gi, kind, h, v, hit.Date, hit.Slid));
                histogram[hit.Slid]++;
            }
            else
            {
                unseated.Add(new NonConUnseated(gi, h, v,
                    NonConClassify(gi, pairs, assign, blocked, weeks, xmas, floor, horizon,
                                   capacity, quotas, owed)));
            }
        }

        return new NonConDateReport
        {
            Games = games,
            Unseated = unseated,
            AllocationBend = allocationBend,
            SeatingBend = games.Sum(g => g.WeeksSlid),
            BendHistogram = histogram,
            AllocationShortfalls = shortfalls,
            DatedFingerprint = NonConFingerprint(games),
        };
    }

    /// <summary>The weeks a school may be allocated into: positive capacity, positive
    /// weight, and never the Christmas week.</summary>
    private static List<int> NonConLiveWeeks(
        int[] capacity, int[] weights, List<DateOnly> weeks, DateOnly xmas)
    {
        var live = new List<int>();
        for (var i = 0; i < weeks.Count; i++)
            if (capacity[i] > 0 && weeks[i] != xmas && weights[i] > 0) live.Add(i);
        return live;
    }

    /// <summary>★ R-n4's allocator, PINNED so oracle/C# parity cannot fail on a tie:
    /// (1) drop zero-capacity weeks; (2) force the Christmas week to 0; (3) normalize the
    /// positive weights over what remains; (4) floor the proportional shares; (5) hand out
    /// the remainder by fractional part DESCENDING then EARLIEST WEEK first; (6) a week at
    /// capacity spills its excess by the same rule over remaining capacity, repeating
    /// until placed; (7) report a shortfall when capacity cannot hold the games owed.</summary>
    private static int[] NonConAllocate(
        int total, int[] capacity, int[] weights, List<DateOnly> weeks, DateOnly xmas,
        out int shortBy)
    {
        shortBy = 0;
        var quota = new int[weeks.Count];
        var live = NonConLiveWeeks(capacity, weights, weeks, xmas);
        if (live.Count == 0) { shortBy = total; return quota; }

        var weightSum = (double)live.Sum(i => weights[i]);
        var exact = live.ToDictionary(i => i, i => total * weights[i] / weightSum);
        foreach (var i in live) quota[i] = (int)exact[i];
        var remainder = total - live.Sum(i => quota[i]);
        foreach (var i in live.OrderByDescending(i => exact[i] - (int)exact[i]).ThenBy(i => i)
                              .Take(remainder))
            quota[i]++;

        for (var pass = 0; pass < live.Count + 2; pass++)
        {
            var over = live.Where(i => quota[i] > capacity[i]).ToList();
            if (over.Count == 0) return quota;
            foreach (var i in over)
            {
                var excess = quota[i] - capacity[i];
                quota[i] = capacity[i];
                var room = live.Where(j => quota[j] < capacity[j]).ToList();
                if (room.Count == 0) { shortBy = excess; return quota; }
                var rw = (double)room.Sum(j => weights[j]);
                var share = room.ToDictionary(j => j, j => excess * weights[j] / rw);
                foreach (var j in room) quota[j] += (int)share[j];
                var left = excess - room.Sum(j => (int)share[j]);
                foreach (var j in room.OrderByDescending(j => share[j] - (int)share[j])
                                      .ThenBy(j => j).Take(left))
                    quota[j]++;
            }
        }
        shortBy = live.Sum(i => Math.Max(0, quota[i] - capacity[i]));
        return quota;
    }

    /// <summary>A total order with no tie left to chance: the busiest pairs first, then by
    /// the two school ids, then by position in the matcher's own list.</summary>
    private static List<int> NonConCanonicalOrder(
        List<(int Host, int Visitor, string Kind)> pairs, Dictionary<int, int> owed)
        => Enumerable.Range(0, pairs.Count)
            .OrderByDescending(gi => owed[pairs[gi].Host] + owed[pairs[gi].Visitor])
            .ThenBy(gi => Math.Min(pairs[gi].Host, pairs[gi].Visitor))
            .ThenBy(gi => Math.Max(pairs[gi].Host, pairs[gi].Visitor))
            .ThenBy(gi => gi)
            .ToList();

    /// <summary>Every pairing gets a WEEK. A conference member is held to its exact quota;
    /// an Independent is held to its availability only (R-n6), and two Independents may
    /// not meet before January 1 (Emmett's ruling, 2026-08-08) — those schools play each
    /// other during everyone else's conference season, never in November or December.</summary>
    private static Dictionary<int, int> NonConAssignWeeks(
        List<(int Host, int Visitor, string Kind)> pairs, List<int> schools,
        Dictionary<int, int[]> quotas, Dictionary<int, int[]> capacity, int[] weights,
        List<DateOnly> weeks, DateOnly xmas, Dictionary<int, int> owed,
        Dictionary<int, bool> isIndependent, int startYear)
    {
        var jan1 = new DateOnly(startYear + 1, 1, 1);
        var active = new List<int>();
        var january = new List<int>();
        for (var i = 0; i < weeks.Count; i++)
        {
            if (weeks[i] == xmas) continue;
            if (weights[i] > 0) active.Add(i);
            if (weeks[i] >= jan1) january.Add(i);
        }

        List<int> LegalWeeks(int gi)
        {
            var (a, b, _) = pairs[gi];
            if (isIndependent[a] && isIndependent[b])
                return january.Where(i => capacity[a][i] > 0 && capacity[b][i] > 0).ToList();
            return active;
        }

        int Target(int s, int i) => isIndependent[s] ? capacity[s][i] : quotas[s][i];

        var load = new Dictionary<(int, int), int>();
        int Load(int s, int i) => load.TryGetValue((s, i), out var v) ? v : 0;
        void Bump(int s, int i, int d) => load[(s, i)] = Load(s, i) + d;

        var assign = new Dictionary<int, int>();
        foreach (var gi in NonConCanonicalOrder(pairs, owed))
        {
            var (a, b, _) = pairs[gi];
            var legal = LegalWeeks(gi);
            if (legal.Count == 0) legal = active;
            var best = legal[0];
            (int Room, int Weight, int Neg) bestKey =
                (Math.Min(Target(a, best) - Load(a, best), Target(b, best) - Load(b, best)),
                 weights[best], -best);
            foreach (var i in legal)
            {
                var key = (Math.Min(Target(a, i) - Load(a, i), Target(b, i) - Load(b, i)),
                           weights[i], -i);
                if (key.CompareTo(bestKey) > 0) { bestKey = key; best = i; }
            }
            assign[gi] = best;
            Bump(a, best, 1); Bump(b, best, 1);
        }

        // ── bounded deterministic repair: walk the schools in id order and move a game
        //    out of every over-quota week into the first under-quota week that takes it ──
        var incident = schools.ToDictionary(s => s, _ => new List<int>());
        for (var gi = 0; gi < pairs.Count; gi++)
        {
            incident[pairs[gi].Host].Add(gi);
            incident[pairs[gi].Visitor].Add(gi);
        }
        for (var pass = 0; pass < 8; pass++)
        {
            var moved = false;
            foreach (var s in schools)
            {
                if (isIndependent[s]) continue;
                foreach (var i in active)
                {
                    while (Load(s, i) > quotas[s][i])
                    {
                        var placedOne = false;
                        foreach (var gi in incident[s].Where(g => assign[g] == i)
                                                      .OrderBy(g => g).ToList())
                        {
                            var (a, b, _) = pairs[gi];
                            foreach (var j in LegalWeeks(gi))
                            {
                                if (j == i) continue;
                                if (Load(a, j) >= Target(a, j) || Load(b, j) >= Target(b, j))
                                    continue;
                                Bump(a, i, -1); Bump(b, i, -1);
                                Bump(a, j, 1); Bump(b, j, 1);
                                assign[gi] = j;
                                placedOne = moved = true;
                                break;
                            }
                            if (placedOne) break;
                        }
                        if (!placedOne) break;
                    }
                }
            }
            if (!moved) break;
        }
        return assign;
    }

    /// <summary>★ R-n8's slide order, exactly: the quota week first, then outward one week
    /// at a time, LATER before EARLIER, Christmas always skipped, never past the
    /// radius.</summary>
    private static List<int> NonConSlideOrder(
        int home, List<DateOnly> weeks, DateOnly xmas)
    {
        var order = new List<int> { home };
        for (var k = 1; k <= NonConSlideRadius; k++)
            foreach (var j in new[] { home + k, home - k })
                if (j >= 0 && j < weeks.Count && weeks[j] != xmas) order.Add(j);
        return order;
    }

    /// <summary>Every pairing gets a NIGHT. Games are seated in canonical order within
    /// ascending assigned week — a STABLE secondary sort, matching the oracle's — and each
    /// takes the first legal night in the first week its slide order allows.</summary>
    private static Dictionary<int, (DateOnly Date, int Slid)> NonConSeatNights(
        List<(int Host, int Visitor, string Kind)> pairs, List<int> schools,
        Dictionary<int, int> assign, Dictionary<int, HashSet<DateOnly>> blocked,
        Dictionary<int, HashSet<DateOnly>> fixedNights, List<DateOnly> weeks, DateOnly xmas,
        DateOnly floor, DateOnly horizon, Dictionary<int, int> owed)
    {
        var played = schools.ToDictionary(s => s, _ => new List<DateOnly>());
        var weekLoad = new Dictionary<(int, DateOnly), int>();
        var leagueInWeek = new Dictionary<(int, DateOnly), int>();
        foreach (var s in schools)
            foreach (var d in fixedNights[s])
            {
                var key = (s, SeasonMonday(d));
                leagueInWeek[key] = leagueInWeek.TryGetValue(key, out var v) ? v + 1 : 1;
            }
        int WeekLoad(int s, DateOnly w) => weekLoad.TryGetValue((s, w), out var v) ? v : 0;
        int League(int s, DateOnly w) => leagueInWeek.TryGetValue((s, w), out var v) ? v : 0;

        var seated = new Dictionary<int, (DateOnly, int)>();
        var order = NonConCanonicalOrder(pairs, owed).OrderBy(gi => assign[gi]).ToList();
        foreach (var gi in order)
        {
            var (a, b, _) = pairs[gi];
            foreach (var i in NonConSlideOrder(assign[gi], weeks, xmas))
            {
                var w = weeks[i];
                DateOnly? chosen = null;
                for (var k = 0; k < 7; k++)
                {
                    var d = w.AddDays(k);
                    if (d < floor || d > horizon) continue;
                    if (blocked[a].Contains(d) || blocked[b].Contains(d)) continue;
                    if (played[a].Any(x => Math.Abs(d.DayNumber - x.DayNumber) <= NonConSpacingClearDays))
                        continue;
                    if (played[b].Any(x => Math.Abs(d.DayNumber - x.DayNumber) <= NonConSpacingClearDays))
                        continue;
                    if (WeekLoad(a, w) + League(a, w) >= NonConWeeklyLoadCeiling) continue;
                    if (WeekLoad(b, w) + League(b, w) >= NonConWeeklyLoadCeiling) continue;
                    chosen = d;
                    break;
                }
                if (chosen is not { } date) continue;
                seated[gi] = (date, Math.Abs(i - assign[gi]));
                played[a].Add(date); played[b].Add(date);
                weekLoad[(a, w)] = WeekLoad(a, w) + 1;
                weekLoad[(b, w)] = WeekLoad(b, w) + 1;
                break;
            }
        }
        return seated;
    }

    /// <summary>★ A2 — a dead end is a STOP AND DIAGNOSE, never automatically a bug. Only
    /// <see cref="NonConFailureClass.SearchDefect"/> indicts this file; the other three are
    /// design findings for Emmett, and under all four cancellation stays forbidden.</summary>
    private static NonConFailureClass NonConClassify(
        int gi, List<(int Host, int Visitor, string Kind)> pairs, Dictionary<int, int> assign,
        Dictionary<int, HashSet<DateOnly>> blocked, List<DateOnly> weeks, DateOnly xmas,
        DateOnly floor, DateOnly horizon, Dictionary<int, int[]> capacity,
        Dictionary<int, int[]> quotas, Dictionary<int, int> owed)
    {
        var (a, b, _) = pairs[gi];
        foreach (var i in NonConSlideOrder(assign[gi], weeks, xmas))
            for (var k = 0; k < 7; k++)
            {
                var d = weeks[i].AddDays(k);
                if (d >= floor && d <= horizon
                    && !blocked[a].Contains(d) && !blocked[b].Contains(d))
                    return NonConFailureClass.SearchDefect;
            }
        foreach (var s in new[] { a, b })
            if (capacity[s].Sum() < owed[s]) return NonConFailureClass.EndpointPhysicalCapacity;
        for (var i = 0; i < weeks.Count; i++)
            if (quotas[a][i] > 0 && quotas[b][i] > 0)
                return NonConFailureClass.EmptyPairwiseDateIntersection;
        return NonConFailureClass.QuotaIncompatibility;
    }

    /// <summary>★ THE SORT KEY IS LOCKED AND EXCLUDES SITE DATA: (date, canonical source
    /// RANK, ordered school ids, host designation, event identity with a fixed empty
    /// token). S107 adds cities as enrichment and must not be able to reorder this.</summary>
    private static string NonConFingerprint(IReadOnlyList<NonConDatedGame> games)
    {
        var lines = games.Select(g => string.Join("|",
            g.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            NonConSourceRank("matched").ToString(CultureInfo.InvariantCulture),
            Math.Min(g.HostSchoolId, g.VisitorSchoolId).ToString(CultureInfo.InvariantCulture),
            Math.Max(g.HostSchoolId, g.VisitorSchoolId).ToString(CultureInfo.InvariantCulture),
            g.HostSchoolId.ToString(CultureInfo.InvariantCulture),
            "-")).ToList();
        lines.Sort(StringComparer.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
