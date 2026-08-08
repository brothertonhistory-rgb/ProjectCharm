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
    /// plays 29. The event games come out of the higher total — reading R2 as
    /// "subtract three from a flat 29" hands six Big East schools an impossible slate
    /// (18 conference games + a seat leaves them fewer open games than their home band).</summary>
    private const int NonConSeasonGamesSeated = 31;
    private const int NonConSeasonGamesUnseated = 29;

    /// <summary>★ S105.1 — THE MOST GAMES ANY TOURNAMENT CAN COST A TEAM, and deliberately
    /// NOT the number a given school is charged. Used only by the two CAPACITY GUARDS that
    /// run BEFORE any request exists — the showcase seating floor and the contract gate —
    /// where the question is "could this school afford the worst case?" and the seat it will
    /// eventually take is not yet known. Assuming the worst case there is what keeps the
    /// seating draw byte-identical to the pre-S105.1 world.
    /// <para>The exact per-school charge is <see cref="TournamentGamesFor"/> and lives
    /// downstream, where the field size IS known. Two names because they are two different
    /// questions; one name is how the flat three survived S101 and S104.</para></summary>
    private const int MaxTournamentGamesPerTeam = 3;

    /// <summary>★ S105.1 — HOW MANY GAMES A FIELD OF THIS SIZE GUARANTEES EACH TEAM IN IT.
    /// Eight teams is three rounds, four teams is two, and every team plays every round —
    /// the rule the route tables in <c>Program.Season.Brackets.cs</c> encode literally.
    /// <para>★ THIS IS A GUARANTEE, NEVER AN OUTCOME. It is read while the request is being
    /// built, before a single tournament game has been played, because it is an accounting
    /// fact about the bracket and not a fact about anybody's November.</para>
    /// <para>★ IT REFUSES WHAT IT DOES NOT KNOW, matching <c>BracketRoutesFor</c>'s own
    /// philosophy. <c>fieldSize == 4 ? 2 : 3</c> is forbidden here: a future field of six
    /// would silently take three, which is precisely the bug this method exists to end,
    /// reintroduced one layer up.</para></summary>
    private static int TournamentGamesFor(int fieldSize) => fieldSize switch
    {
        8 => 3,
        4 => 2,
        _ => throw new InvalidOperationException(
                 $"TOURNAMENT EXEMPTION: no guaranteed-game count for a field of " +
                 $"{fieldSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}; " +
                 "only 8 and 4 are authorable."),
    };

    /// <summary>Class home bands and showcase allowances (brief §3, ruled shapes). Indexed
    /// by class ordinal — see <see cref="NonConClassNames"/>.</summary>
    private static readonly (int Lo, int Hi)[] NonConHomeBands =
        { (0, 2), (3, 5), (5, 7), (7, 10) };                  // Selling, Working, Solid, Marquee
    private static readonly int[] NonConShowcaseAllowance = { 0, 0, 1, 2 };

    // ── ★ S105: the Independent seam. Emmett's rulings, 2026-08-07. ─────────────────
    //
    //    R-a  An Independent plays a FULL SEASON — 29 games, 31 if a tournament seats it.
    //         "Teams should only fall under the 29-31 range if forced to. It shouldn't be
    //         the norm." So OPEN uses the shared rule (their conference games are zero)
    //         and ROAD IS THE REMAINDER, never a fixed number.
    //    R-b  HOME is read STRAIGHT OFF PRESTIGE, not spread across whoever happens to be
    //         independent that season — a rank spread would hand the top of a field of
    //         nobodies the ceiling purely by rank, and would move a school's home count
    //         because some OTHER school became independent.
    //    R-c  ZERO neutral games. The neutral allowance is a privilege of CLASS and an
    //         Independent has no league to lift it; events are its only neutral floor.
    //    R-d  An Independent classes as a LOW MAJOR — its own prestige, NO TIER FLOOR.
    //
    //    ★ THE FORK: the CLASS decides what KIND of opponent the home requests shop for;
    //      the CURVE decides HOW MANY. An Independent never uses NonConHomeBands and never
    //      uses NonConShowcaseAllowance. Those two lines are the whole difference.
    private const int NonConIndependentHomeLo = 7;
    private const int NonConIndependentHomeHi = 13;
    private const int NonConIndependentHomeAnchor = 80;
    private const int NonConIndependentNeutral = 0;

    /// <summary>★ R-b. <c>lo</c> at prestige 0 rising to <c>hi</c> at the anchor, round-half-up
    /// in EXACT integer arithmetic — <c>(2·a + b) / (2·b)</c> — the same guard
    /// <see cref="NonConHomeSpread"/> uses, so no floating-point midpoint can tip a school's
    /// home count differently on another machine. Monotone non-decreasing in prestige and
    /// clamped to [lo, hi] by construction: prestige is capped at the anchor first, and a
    /// negative prestige (which world load refuses) is floored so the function stays total.</summary>
    private static int NonConIndependentHome(int prestige)
    {
        var p = prestige < 0 ? 0
              : prestige > NonConIndependentHomeAnchor ? NonConIndependentHomeAnchor
              : prestige;
        var a = p * (NonConIndependentHomeHi - NonConIndependentHomeLo);
        var b = NonConIndependentHomeAnchor;
        return NonConIndependentHomeLo + (2 * a + b) / (2 * b);
    }

    // ── Class — the order is DEFINED HERE, centrally, and asserted by Phase 92 C2. ──
    //    Selling(0) < Working(1) < Solid(2) < Marquee(3). Independent is separate,
    //    neither above nor below: it is a different situation, not a rung.

    private static readonly string[] NonConClassNames = { "Selling", "Working", "Solid", "Marquee" };
    /// <summary>★ S105 — RETIRED AS A CLASS NAME. "Independent" was never a rung on the
    /// ladder, and once Independents entered the matcher's class traversal it would have
    /// been a lookup miss. Being independent is a fact about a school's LEAGUE, carried by
    /// <c>IsIndependent</c>; its CLASS is now its own prestige band with no floor (R-d).
    /// The constant survives only so Phase 92 C2 can assert the name is GONE from every
    /// emitted class.</summary>
    private const string NonConRetiredIndependentClassName = "Independent";

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
    /// <para>★ S104 — <c>Seated</c> means seated in a TOURNAMENT and nothing else, because it
    /// is the flag that buys the 31-game season. <c>ShowcaseGames</c> is the separate fact: 0
    /// or 1, a game already spent rather than a game added.</para>
    private sealed record NonConSchoolRequest(
        int SchoolId, string SchoolName, string ClassName, int ConferenceGames, bool Seated,
        int Open, int Home, int Neutral, int Road,
        bool IsIndependent, bool LiftedByFloor, bool Compressed, bool Impossible,
        int ShowcaseGames = 0);

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

        /// <summary>★ S105 — schools that are IN A LEAGUE. Formerly <c>Targeted</c>, which
        /// meant "has a request"; after S105 every school has one, so the old name named
        /// nothing. Renamed deliberately so every consumer had to be re-decided rather than
        /// silently inheriting the wrong domain.</summary>
        public IEnumerable<NonConSchoolRequest> Conventional => Schools.Where(s => !s.IsIndependent);
        public int IndependentCount => Schools.Count(s => s.IsIndependent);
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
        WorldFile world, EventSeatingOutcome seating,
        IReadOnlyDictionary<int, ContractChargeSet>? contractCharges = null)
    {
        // Seating exemption is BINARY SET MEMBERSHIP — seated in an event this season,
        // yes or no. Never a per-seat count: a malformed seating carrying a duplicate
        // must not hand a school a six-game exemption.
        //
        // ★ S104 / A1 — TOURNAMENT SEATS ONLY. R26 is the whole point: a tournament seat
        //   makes the season genuinely BIGGER (31 games, its event's games among them); a
        //   showcase seat does not — "it just counts as one of your games". Left
        //   unfiltered, a showcase seat would hand its school a 31-game season and a
        //   handful of phantom event games, which is the exact opposite of the ruling. The
        //   failure would have HIDDEN: on schools whose home band absorbs the difference
        //   the totals still reconcile and every conservation check stays green.
        //
        // ★ S105.1 — AND HOW MANY GAMES THE SEAT ACTUALLY BUYS DEPENDS ON THE FIELD. Eight
        //   teams is three rounds and four teams is two, so a flat three charged a four-team
        //   school for a game its bracket never guaranteed: it removed three from what the
        //   school had to arrange while the bracket promised two, and the school played 30
        //   inside a season the engine believed was 31. Membership in this map is exactly
        //   what `Seated` used to mean; the value is what the seat is worth.
        var seatFieldOf = MteTournamentFieldSizes(seating);

        // ★ The other half of the same fact: a showcase seat is a fixed obligation that
        //   already exists, and it is CHARGED against the games this school already had.
        var showcaseGamesOf = MteShowcaseObligations(seating);

        var confById = world.Conferences.ToDictionary(c => c.Id);
        var requests = new Dictionary<int, NonConSchoolRequest>();
        var byClass = new Dictionary<int, List<WorldSchool>>
            { [0] = new(), [1] = new(), [2] = new(), [3] = new() };
        var independents = new List<WorldSchool>();

        foreach (var s in world.Schools)
        {
            var conf = confById[s.ConferenceId];
            if (conf.Games == 0)
            {
                // ★ Games == 0 is still the authoritative Independent marker — recorded
                //   convention (WorldConference's own R14 note), not an inference. What
                //   changed in S105 is that it no longer means "no request". Held aside
                //   because the class arms RANK inside their class and this arm does not
                //   rank at all: it reads prestige directly (R-b).
                independents.Add(s);
                continue;
            }
            var floor = NonConTierFloor(conf.TierId);
            var cls = Math.Max(floor, NonConPrestigeClass(s.CurrentPrestige));
            byClass[cls].Add(s);
        }

        // ── ★ S105 — THE INDEPENDENT ARM ────────────────────────────────────────────
        //    The SAME open rule as everyone else (conference games are zero), the prestige
        //    curve for home (R-b), zero neutral (R-c), road the remainder — so the total is
        //    a FULL SEASON (R-a). Class is the prestige band with NO FLOOR (R-d).
        //
        //    ★ Both charge chains run UNCHANGED. ApplyShowcaseCharges already falls
        //      neutral → road → home and ApplyContractCharges already falls through to road
        //      when the neutral bucket is empty, so a zero neutral bucket needed no new rule
        //      at either site. That is also what closes a live hole: before S105 the contract
        //      layer would exercise a contract with an Independent and charge it to nothing,
        //      because this branch returned before the charge ran.
        foreach (var s in independents)
        {
            var isSeated = seatFieldOf.TryGetValue(s.Id, out var indFieldSize);
            var seasonGames = isSeated ? NonConSeasonGamesSeated : NonConSeasonGamesUnseated;
            var eventGames = isSeated ? TournamentGamesFor(indFieldSize) : 0;
            var open = seasonGames - 0 - eventGames;
            var cls = NonConPrestigeClass(s.CurrentPrestige);

            int home, neutral, road;
            bool compressed = false, impossible = false;
            if (open < 0)
            {
                impossible = true;
                home = neutral = road = 0;
            }
            else
            {
                home = NonConIndependentHome(s.CurrentPrestige);
                if (home > open) { home = open; compressed = true; }
                neutral = Math.Min(NonConIndependentNeutral, open - home);
                road = open - home - neutral;
                if (contractCharges is not null
                    && contractCharges.TryGetValue(s.Id, out var icharge)
                    && icharge.Total > 0)
                    (home, neutral, road) = ApplyContractCharges(home, neutral, road, icharge);
                if (showcaseGamesOf.TryGetValue(s.Id, out var ishowcase) && ishowcase > 0)
                    (home, neutral, road) =
                        ApplyShowcaseCharges(home, neutral, road, ishowcase);
            }
            requests[s.Id] = new NonConSchoolRequest(
                s.Id, s.Name, NonConClassNames[cls], 0, isSeated,
                open, home, neutral, road, IsIndependent: true,
                LiftedByFloor: false, Compressed: compressed, Impossible: impossible,
                ShowcaseGames: showcaseGamesOf.GetValueOrDefault(s.Id, 0));
        }

        // ★ Ranking happens AFTER final class assignment: a floor-promoted school ranks
        //   inside the class it landed in, and lands at its bottom because it sorts by
        //   prestige. Ordered, clamped, in games (prompt §4.3):
        //     OPEN    = SEASON_GAMES − conference games − the games its field guarantees
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
                var isSeated = seatFieldOf.TryGetValue(s.Id, out var fieldSize);
                var seasonGames = isSeated ? NonConSeasonGamesSeated : NonConSeasonGamesUnseated;
                var eventGames = isSeated ? TournamentGamesFor(fieldSize) : 0;
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
                    // ★ S103 — a contracted game is one of the games the school already
                    //   wanted, not an eleventh home date. Each exercised leg is charged
                    //   against the bucket it belongs to (Emmett's ruling), AFTER the
                    //   ordinary split, so a contract year keeps the shape of a normal
                    //   year and the road-less power school pays a HOME date for its
                    //   away leg. The counts the matcher then reads are post-charge,
                    //   which is what keeps a contracted pairing out of the request
                    //   pool by arithmetic rather than by exception.
                    if (contractCharges is not null
                        && contractCharges.TryGetValue(s.Id, out var charge)
                        && charge.Total > 0)
                        (home, neutral, road) = ApplyContractCharges(home, neutral, road, charge);

                    // ★ S104 / R26 — the showcase charge, SECOND. The ruled priority
                    //   (extending R23's order): contracted games charge first, showcase
                    //   games second. Order matters because it decides which obligation
                    //   ate the neutral bucket, and that must never be settled by the
                    //   arbitrary order a collection happened to enumerate in.
                    if (showcaseGamesOf.TryGetValue(s.Id, out var showcaseGames) && showcaseGames > 0)
                        (home, neutral, road) =
                            ApplyShowcaseCharges(home, neutral, road, showcaseGames);
                }
                requests[s.Id] = new NonConSchoolRequest(
                    s.Id, s.Name, NonConClassNames[cls], conf.Games, isSeated,
                    open, home, neutral, road, IsIndependent: false,
                    LiftedByFloor: NonConPrestigeClass(s.CurrentPrestige) < cls,
                    Compressed: compressed, Impossible: impossible,
                    ShowcaseGames: showcaseGamesOf.GetValueOrDefault(s.Id, 0));
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
            $"{r.IndependentCount} of them Independent — {lifted} lifted by their league's floor" +
            (compressed > 0 ? $", {compressed} compressed to their open games" : "") +
            (impossible > 0 ? $", {impossible} IMPOSSIBLE (more conference games than the season holds)" : "") +
            ".");

        var targeted = r.Conventional.ToList();
        if (targeted.Count > 0)
        {
            lines.Add($"  Seated in an early-season event: {r.SeatedCount} school(s) — " +
                      $"a seated school plays {NonConSeasonGamesSeated} games, and its event " +
                      "accounts for as many as its bracket guarantees: three in a field of " +
                      "eight, two in a field of four. Everyone else plays " +
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

        // ★ S105 — the Independents now carry a real request, so the page prints the
        //   arithmetic rather than a placeholder. Requested H/N/R and total per school;
        //   what they actually got is the MATCHING page's job, not this one's.
        var independents = r.Schools.Where(s => s.IsIndependent).ToList();
        if (independents.Count > 0)
        {
            lines.Add($"  Independents ({independents.Count}) — no conference, so the whole " +
                      $"season is arranged here. Home reads off prestige; neutral is zero " +
                      $"(no league to lift them); road is the remainder, so the total is a " +
                      $"full season unless the market fails.");
            lines.Add("    school                class     open   home  neutral   road   total");
            foreach (var s in independents
                         .OrderBy(s => s.Home).ThenBy(s => s.SchoolId))
                lines.Add(string.Format(inv,
                    "    {0,-22}{1,-9}{2,5}{3,7}{4,9}{5,7}{6,8}",
                    s.SchoolName, s.ClassName, s.Open, s.Home, s.Neutral, s.Road,
                    s.Home + s.Neutral + s.Road));
        }
        return lines;
    }
}
