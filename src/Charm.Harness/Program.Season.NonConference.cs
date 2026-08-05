namespace Charm.Harness;

// ============================================================================
//  S101 — CLASSES AND REQUESTS (non-conference arc, session 1 of 4).
//
//  Every school gets a CLASS (read from prestige every season, with its
//  conference tier as a floor at every tier — Emmett's ruling, 2026-08-04) and
//  a REQUEST: counts of ordinary non-conference games it wants to arrange —
//  home, neutral, road. NOTHING IS SCHEDULED. No opponent is chosen, no site
//  is named, no game is emitted. The report exists so the national balance —
//  how many road games the country wants against how many gyms are open —
//  can be read off the season page before any matcher exists.
//
//  ★ THE SEAM IS PURE BY SIGNATURE. BuildNonConferenceRequests takes exactly
//  (WorldFile, EventSeatingOutcome): no seed, no RNG, no HistoryStore, no
//  schedule. Nothing downstream reads the report. That is what makes the
//  zero-path byte-identity claim (Phase 92 C9) provable by construction.
//
//  ★ EVERY QUANTITY BELOW IS A COUNT OF GAMES. The word "date" appears
//  nowhere: the brief's "a tournament costs one date and buys three games" is
//  the calendar motivation for the higher season total, not a unit this
//  session counts. Calendar dates belong to arc session 3.
//
//  ★ R8 — SHIP WITH CONSTANTS, REWIRE LATER. The numbers live in the single
//  block below so the later rewire (roster maturity, coach temperament —
//  brief §5) is a substitution, not a hunt.
// ============================================================================

internal static partial class Program
{
    // ── R8: the one seam. Every tunable number of S101 lives here. ──────────────────

    /// <summary>★ R2, literally — "seeded reaches 31; stayed home lands near 29." A school
    /// seated in an early-season event REALLY PLAYS 31 regular-season games; everyone else
    /// plays 29. The three event games come out of the higher total — reading R2 as
    /// "subtract three from a flat 29" hands six Big East schools an impossible slate
    /// (18 conference games + a seat leaves them fewer open games than their home band).</summary>
    private const int NonConSeasonGamesSeated = 31;
    private const int NonConSeasonGamesUnseated = 29;
    private const int NonConEventGames = 3;

    /// <summary>Class home bands and showcase allowances (brief §3, ruled shapes). Indexed
    /// by class ordinal — see <see cref="NonConClassNames"/>.</summary>
    private static readonly (int Lo, int Hi)[] NonConHomeBands =
        { (0, 2), (3, 5), (5, 7), (7, 10) };                  // Selling, Working, Solid, Marquee
    private static readonly int[] NonConShowcaseAllowance = { 0, 0, 1, 2 };

    // ── Class — the order is DEFINED HERE, centrally, and asserted by Phase 92 C2. ──
    //    Selling(0) < Working(1) < Solid(2) < Marquee(3). Independent is separate,
    //    neither above nor below: it is a different situation, not a rung.

    private static readonly string[] NonConClassNames = { "Selling", "Working", "Solid", "Marquee" };
    private const string NonConIndependent = "Independent";

    /// <summary>★ The tier floor — the ruling's second half: the WORST school in a league
    /// still schedules like its league (Emmett: "even the absolute worst power conference
    /// team gets the easy home games in non con if they want them"), and that holds at
    /// EVERY tier, not only the power line. The four ids are enforced at world load
    /// (Program.World.cs, tiers-must-be-exactly + unknown-tier refusal), so the default
    /// arm is a tripwire for a load-validation regression, never a code path.</summary>
    private static int NonConTierFloor(string tierId) => tierId switch
    {
        "power"   => 3,
        "highMid" => 2,
        "lowMid"  => 1,
        "low"     => 0,
        _ => throw new InvalidOperationException(
            $"NON-CONFERENCE INVARIANT VIOLATED: unknown tier id '{tierId}' reached the " +
            "classifier — world load validation should have refused this world."),
    };

    /// <summary>Prestige bands: 80+ Marquee, 55–79 Solid, 25–54 Working, under 25 Selling.
    /// Read from <c>CurrentPrestige</c> every season — never <c>HistoricalPrestige</c> —
    /// so a rising program earns its way up and a falling one is caught by its floor.</summary>
    private static int NonConPrestigeClass(int prestige) =>
        prestige >= 80 ? 3 : prestige >= 55 ? 2 : prestige >= 25 ? 1 : 0;

    // ── The report ──────────────────────────────────────────────────────────────────

    /// <summary>One school's class and request. For an Independent, the request numbers are
    /// all zero and meaningless — R13: their November is arc session 4, and the flag is the
    /// fact, not the zeros. <c>Compressed</c> = the home band asked for more games than the
    /// school has open (clamped down; zero on every committed world). <c>Impossible</c> =
    /// the school has more conference games than its season total (OPEN below zero; zero on
    /// every committed world; reported, never a throw).</summary>
    private sealed record NonConSchoolRequest(
        int SchoolId, string SchoolName, string ClassName, int ConferenceGames, bool Seated,
        int Open, int Home, int Neutral, int Road,
        bool IsIndependent, bool LiftedByFloor, bool Compressed, bool Impossible);

    /// <summary>Everything S101 decides, in one object. Page-only: the page renders what
    /// this contains and nothing else; Phase 92 asserts wiring and arithmetic on this
    /// object, never on rendered prose and never on a basketball calibration value.
    /// <c>HostGap = RoadTotal − HomeTotal</c>: positive means more travelers than hosts,
    /// negative more hosts than travelers, zero balanced.</summary>
    private sealed class NonConferenceReport
    {
        public required IReadOnlyList<NonConSchoolRequest> Schools { get; init; }
        public required int HomeTotal { get; init; }
        public required int NeutralTotal { get; init; }
        public required int RoadTotal { get; init; }
        public int HostGap => RoadTotal - HomeTotal;
        public required int SeatedCount { get; init; }

        public IEnumerable<NonConSchoolRequest> Targeted => Schools.Where(s => !s.IsIndependent);
        public int CountOf(string className) => Schools.Count(s => s.ClassName == className);

        public static readonly NonConferenceReport Empty = new()
        {
            Schools = Array.Empty<NonConSchoolRequest>(),
            HomeTotal = 0, NeutralTotal = 0, RoadTotal = 0, SeatedCount = 0,
        };
    }

    // ── The computation ─────────────────────────────────────────────────────────────

    /// <summary>★ THE RANK SPREAD, exactly as specified: final class members sorted
    /// ascending by (CurrentPrestige, Id); rank i of n maps to
    /// <c>lo + RoundHalfUp(i × (hi − lo) / (n − 1))</c>, a class of one takes the band
    /// floor. Computed in EXACT integer arithmetic — <c>(2·a + b) / (2·b)</c> is
    /// round-half-up of a/b for non-negative integers — so no floating-point midpoint can
    /// ever tip a school's home count differently on another machine. Phase 92 C11 asserts
    /// the sequences [7,8,9,9,10] and [0,1,1,2,2] against this directly.</summary>
    private static int[] NonConHomeSpread(int lo, int hi, int n)
    {
        if (n <= 0) return Array.Empty<int>();
        var outp = new int[n];
        if (n == 1) { outp[0] = lo; return outp; }
        for (var i = 0; i < n; i++)
        {
            var a = i * (hi - lo);          // numerator of the exact position
            var b = n - 1;                  // denominator
            outp[i] = lo + (2 * a + b) / (2 * b);
        }
        return outp;
    }

    /// <summary>★ S101's single entry point. Pure: same world + same seating in, same
    /// report out, no randomness, no shared state touched. Called from RunSeasonCore
    /// immediately after MteSeatSeason and before BuildSeasonSchedule; the result rides
    /// out on SeasonRunOutcome and reaches nothing but the page and Phase 92.</summary>
    private static NonConferenceReport BuildNonConferenceRequests(
        WorldFile world, EventSeatingOutcome seating)
    {
        // Seating exemption is BINARY SET MEMBERSHIP — seated in an event this season,
        // yes or no. Never a per-seat count: a malformed seating carrying a duplicate
        // must not hand a school a six-game exemption.
        var seated = seating.Active
            .SelectMany(e => e.Seats).Select(s => s.SchoolId).ToHashSet();

        var confById = world.Conferences.ToDictionary(c => c.Id);
        var requests = new Dictionary<int, NonConSchoolRequest>();
        var byClass = new Dictionary<int, List<WorldSchool>>
            { [0] = new(), [1] = new(), [2] = new(), [3] = new() };

        foreach (var s in world.Schools)
        {
            var conf = confById[s.ConferenceId];
            if (conf.Games == 0)
            {
                // ★ Games == 0 is the authoritative Independent marker — recorded
                //   convention (WorldConference's own R14 note), not an inference.
                requests[s.Id] = new NonConSchoolRequest(
                    s.Id, s.Name, NonConIndependent, 0, seated.Contains(s.Id),
                    0, 0, 0, 0, IsIndependent: true,
                    LiftedByFloor: false, Compressed: false, Impossible: false);
                continue;
            }
            var floor = NonConTierFloor(conf.TierId);
            var cls = Math.Max(floor, NonConPrestigeClass(s.CurrentPrestige));
            byClass[cls].Add(s);
        }

        // ★ Ranking happens AFTER final class assignment: a floor-promoted school ranks
        //   inside the class it landed in, and lands at its bottom because it sorts by
        //   prestige. Ordered, clamped, in games (prompt §4.3):
        //     OPEN    = SEASON_GAMES − conference games − EVENT_GAMES
        //     HOME    = band position, clamped to OPEN         (clamp counted: Compressed)
        //     NEUTRAL = min(allowance, OPEN − HOME)
        //     ROAD    = the remainder — never a band, so the acceptance measure stays a
        //               measurement instead of an input.
        foreach (var (cls, members) in byClass)
        {
            members.Sort((a, b) =>
            {
                var byPrestige = a.CurrentPrestige.CompareTo(b.CurrentPrestige);
                return byPrestige != 0 ? byPrestige : a.Id.CompareTo(b.Id);
            });
            var (lo, hi) = NonConHomeBands[cls];
            var spread = NonConHomeSpread(lo, hi, members.Count);
            for (var i = 0; i < members.Count; i++)
            {
                var s = members[i];
                var conf = confById[s.ConferenceId];
                var isSeated = seated.Contains(s.Id);
                var seasonGames = isSeated ? NonConSeasonGamesSeated : NonConSeasonGamesUnseated;
                var eventGames = isSeated ? NonConEventGames : 0;
                var open = seasonGames - conf.Games - eventGames;

                int home, neutral, road;
                bool compressed = false, impossible = false;
                if (open < 0)
                {
                    // A world authoring more conference games than the season total.
                    // No committed world does; reported, never a throw.
                    impossible = true;
                    home = neutral = road = 0;
                }
                else
                {
                    home = spread[i];
                    if (home > open) { home = open; compressed = true; }
                    neutral = Math.Min(NonConShowcaseAllowance[cls], open - home);
                    road = open - home - neutral;
                }
                requests[s.Id] = new NonConSchoolRequest(
                    s.Id, s.Name, NonConClassNames[cls], conf.Games, isSeated,
                    open, home, neutral, road, IsIndependent: false,
                    LiftedByFloor: NonConPrestigeClass(s.CurrentPrestige) < cls,
                    Compressed: compressed, Impossible: impossible);
            }
        }

        // Emitted in school-id order so the report is one canonical shape.
        var schools = world.Schools.OrderBy(s => s.Id).Select(s => requests[s.Id]).ToList();
        return new NonConferenceReport
        {
            Schools = schools,
            HomeTotal = schools.Sum(r => r.Home),
            NeutralTotal = schools.Sum(r => r.Neutral),
            RoadTotal = schools.Sum(r => r.Road),
            SeatedCount = schools.Count(r => r.Seated && !r.IsIndependent),
        };
    }

    // ── The page ────────────────────────────────────────────────────────────────────

    /// <summary>The brief's evidence table plus the two R5 exception cases. Looked up by
    /// name at render time; a school absent from this world is skipped silently, so the
    /// block renders validly on any world (fixtures included).</summary>
    private static readonly string[] NonConPageNamedSchools =
    {
        "Duke", "Wake Forest", "Missouri", "Texas Tech",
        "Drake", "Arkansas-Pine Bluff", "Northwestern", "Gonzaga",
    };

    /// <summary>★ PAGE-ONLY, and every number derives from the report — the page cannot
    /// print a total it did not carry. Renders validly on any world: zero Independents,
    /// zero targeted schools, either gap sign, missing named schools.</summary>
    private static List<string> NonConferencePageLines(NonConferenceReport r)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "--- NON-CONFERENCE REQUESTS (S101: classes and targets only — nothing is " +
            "scheduled; the matching is a later session) ---",
        };

        var lifted = r.Schools.Count(s => s.LiftedByFloor);
        var compressed = r.Schools.Count(s => s.Compressed);
        var impossible = r.Schools.Count(s => s.Impossible);
        lines.Add(
            $"  Classes: {r.CountOf("Marquee")} Marquee / {r.CountOf("Solid")} Solid / " +
            $"{r.CountOf("Working")} Working / {r.CountOf("Selling")} Selling / " +
            $"{r.CountOf(NonConIndependent)} Independent — {lifted} lifted by their league's floor" +
            (compressed > 0 ? $", {compressed} compressed to their open games" : "") +
            (impossible > 0 ? $", {impossible} IMPOSSIBLE (more conference games than the season holds)" : "") +
            ".");

        var targeted = r.Targeted.ToList();
        if (targeted.Count > 0)
        {
            lines.Add($"  Seated in an early-season event: {r.SeatedCount} school(s) — " +
                      $"a seated school plays {NonConSeasonGamesSeated} games, " +
                      $"{NonConEventGames} of them in its event; everyone else plays " +
                      $"{NonConSeasonGamesUnseated}.");
            lines.Add("  class      n    home  neutral  road   (average requested games; road is the remainder, never a quota)");
            foreach (var name in new[] { "Marquee", "Solid", "Working", "Selling" })
            {
                var members = targeted.Where(s => s.ClassName == name).ToList();
                if (members.Count == 0) continue;
                lines.Add(string.Format(inv, "  {0,-9}{1,4}{2,8:0.0}{3,9:0.0}{4,6:0.0}",
                    name, members.Count,
                    members.Average(m => (double)m.Home),
                    members.Average(m => (double)m.Neutral),
                    members.Average(m => (double)m.Road)));
            }

            // ★ THE BALANCE — the finding this session exists to print. Scope stated on
            //   the page: ordinary non-conference games still to arrange, after conference
            //   play and seated-event games are removed. Both gap signs rendered.
            var gap = r.HostGap;
            var balance =
                $"  Balance (ordinary non-conference games to arrange): home {r.HomeTotal}, " +
                $"neutral {r.NeutralTotal}, road {r.RoadTotal}";
            if (gap > 0)
                balance += $" — the country wants {gap} more road games than it has willing hosts; " +
                           $"~{gap / 2} games must be hosted by schools that wanted the road. " +
                           "Nowhere to land until the matching session exists.";
            else if (gap < 0)
                balance += $" — the country wants {-gap} more home games than it has willing " +
                           $"travelers; ~{-gap / 2} games must be played on the road by schools " +
                           "that wanted to host. Nowhere to land until the matching session exists.";
            else
                balance += " — hosts and travelers balance exactly.";
            lines.Add(balance);

            var named = NonConPageNamedSchools
                .Select(n => targeted.FirstOrDefault(s => s.SchoolName == n))
                .Where(s => s is not null).Cast<NonConSchoolRequest>().ToList();
            foreach (var s in named)
                lines.Add(string.Format(inv,
                    "    {0,-22}{1,-9}  conf {2,2}{3}  open {4,2}  ->  {5} home / {6} neutral / {7} road",
                    s.SchoolName, s.ClassName, s.ConferenceGames,
                    s.Seated ? " +event" : "       ", s.Open, s.Home, s.Neutral, s.Road));
        }

        var independents = r.Schools.Where(s => s.IsIndependent).ToList();
        if (independents.Count > 0)
            lines.Add($"  Independents ({independents.Count}, no request yet — their November " +
                      $"is its own session): " +
                      string.Join(", ", independents.Select(s => s.SchoolName)) + ".");
        return lines;
    }
}
