using System.Globalization;
using System.Text;
using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S92 — PHASE 83: THE MAP.
//
//  ★ PAGE-ONLY PRINCIPLE HOLDS. No basketball target and no count of anything
//  the simulation produces is asserted anywhere in this phase. Whether the map is
//  PLAUSIBLE is Emmett's read off `dotnet run -- geography`; whether the trips are
//  schedulable is the scheduler's question, asked when it knows its own
//  constraints. A8 reports and asserts nothing.
//
//  ★ THE THREE INVARIANTS THAT WOULD STILL PASS IF THE SEMANTICS WERE WRONG, and
//  what is done about each:
//
//   1. A DISTANCE FUNCTION TESTED ONLY ON NEARBY AMERICAN CITIES PASSES WITH
//      FLAT-EARTH ARITHMETIC — two points 200 miles apart differ by under 1%
//      between planar and great-circle. So the golden table is DOMINATED by long
//      and exotic pairs, and A1's negative control builds the planar formula and
//      requires it to FAIL, reporting by how much. ★ The control is scoped to the
//      LONG rows by name, because on Duke <-> North Carolina Central the two
//      formulas agree to ten decimal places and always will.
//
//   2. A PLACE TABLE PASSES EVERY STRUCTURAL TEST WHILE THE AUTHORED ENTRIES ARE
//      SILENTLY DROPPED, because 293 of the 310 places come from schools and every
//      school still resolves. So A4 asserts all seventeen individually, BY NAME.
//
//   3. A GOLDEN MILEAGE TABLE SOURCED FROM AN ONLINE CALCULATOR FAILS FOR THE
//      RIGHT-LOOKING REASON — the implementation is correct and the table is
//      ellipsoidal. So the golden pins the MODEL, not just the numbers: spherical
//      haversine, radius 3958.7613, computed outside .NET from the exact
//      serialized coordinates. Every row carries its method and its tolerance.
//
//  ★ THE TOLERANCE IS EVIDENCED, NOT ASSERTED (the S81.3 rule). Math.Sin/Cos/Asin
//  are not bit-portable between Windows and Linux, so this phase reports three
//  numbers and they must order:
//
//        platform variance  <<  tolerance  <<  wrong-formula error
//
//  Measured at build time: perturbing EVERY trig call in the formula by 4 ULP in
//  every combination moves the worst row by 1.2E-011 miles; the tolerance is
//  1E-006; the smallest error the planar control produces on a long row is 44.4
//  miles. Six orders of headroom below, seven above.
//
//  ★ THE ONE PLACE THAT ORDERING DOES NOT HOLD is near-antipodal, and it is why
//  the antipodal probe asserts PROPERTIES and never a mileage. As the two points
//  approach opposite sides of the earth the haversine intermediate approaches 1,
//  where Asin's slope goes to infinity: a single last-bit wobble moves the answer
//  1.7E-004 miles, a hundred and seventy times the tolerance. A golden mileage
//  there would go red on Emmett's machine with nothing whatsoever wrong in the
//  engine. That row proves the clamp holds and the answer stays finite, which is
//  the only thing it was ever there to prove.
// ============================================================================

internal static partial class Program
{
    private sealed record GeoGoldenRow(
        string Label, double Lat1, double Long1, double Lat2, double Long2,
        double ExpectedMiles, string Method, double ToleranceMiles, bool DiscriminatesModel);

    private static bool Phase83GeographyCheck()
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 83 — geography: places, miles, and who is hosting ==");

        var inv = CultureInfo.InvariantCulture;
        var ok = true;
        void Check(string label, bool pass, string? why = null)
        {
            Console.WriteLine($"    {(pass ? "ok  " : "FAIL")} {label}" + (why is null ? "" : $"  ({why})"));
            ok &= pass;
        }
        void Report(string label, string value)
            => Console.WriteLine($"    --   {label}: {value}");

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var stockPath   = Path.Combine(baseDir, "worlds", "stock-d1.world.json");
            var tinyPath    = Path.Combine(baseDir, "worlds", "fixture-tiny.world.json");
            var formatPath  = Path.Combine(baseDir, "worlds", "fixture-format.world.json");
            var v1Path      = Path.Combine(baseDir, "worlds", "fixture-v1-retired.world.json");
            var teamsCsv    = Path.Combine(baseDir, "data", "teams.csv");
            var confCsv     = Path.Combine(baseDir, "data", "conf.csv");
            var placesCsv   = Path.Combine(baseDir, "data", "places.csv");
            var goldenPath  = Path.Combine(baseDir, "tools", "geo_distance_golden.json");

            // =========================================================================
            //  A1 — DISTANCE AGAINST A GOLDEN TABLE WHOSE MODEL IS PINNED
            // =========================================================================
            var golden = LoadGeoGolden(goldenPath);
            Check("A1 golden table loaded", golden.Count > 0, $"{golden.Count} rows");
            Check("A1 the table is dominated by long and exotic pairs (short American pairs "
                  + "cannot tell a correct implementation from flat-earth arithmetic)",
                  golden.Count(r => r.DiscriminatesModel) >= 5,
                  $"{golden.Count(r => r.DiscriminatesModel)} discriminating rows of {golden.Count}");
            Check("A1 every golden row records its external METHOD (an implementation of the "
                  + "same model, never an online calculator)",
                  golden.All(r => r.Method.Contains("haversine", StringComparison.Ordinal)
                                  && r.Method.Contains("3958.7613", StringComparison.Ordinal)));

            var worstAbs = 0.0; var worstRow = "";
            foreach (var r in golden)
            {
                var got = GeoDistance.DistanceMiles(
                    GeoCoordinate.Create(r.Lat1, r.Long1), GeoCoordinate.Create(r.Lat2, r.Long2));
                var d = Math.Abs(got - r.ExpectedMiles);
                if (d > worstAbs) { worstAbs = d; worstRow = r.Label; }
                if (d > r.ToleranceMiles)
                    Check($"A1 {r.Label}", false,
                        string.Format(inv, "expected {0:F6} got {1:F6}, |d| {2:E3} > tol {3:E3}",
                            r.ExpectedMiles, got, d, r.ToleranceMiles));
            }
            Check("A1 every golden row inside its stated tolerance", worstAbs <= golden.Max(r => r.ToleranceMiles),
                string.Format(inv, "worst |d| {0:E3} mi on {1}", worstAbs, worstRow));
            Report("A1 worst golden deviation", string.Format(inv, "{0:E3} mi ({1})", worstAbs, worstRow));

            // ── A1 negative control: the planar formula must FAIL the long rows. ──────
            var planarErrors = new List<(string Label, double Err)>();
            foreach (var r in golden.Where(r => r.DiscriminatesModel))
                planarErrors.Add((r.Label, Math.Abs(GeoPlanarMiles(r.Lat1, r.Long1, r.Lat2, r.Long2) - r.ExpectedMiles)));
            var minPlanar = planarErrors.Count == 0 ? 0.0 : planarErrors.Min(p => p.Err);
            Check("A1 NEGATIVE CONTROL: a planar approximation fails EVERY discriminating row",
                  planarErrors.Count > 0 && planarErrors.All(p => p.Err > golden.Max(r => r.ToleranceMiles)),
                  string.Join(" | ", planarErrors.Select(p => string.Format(inv, "{0} off by {1:F1} mi", p.Label, p.Err))));

            // ── The three-number tolerance evidence, ordered. ─────────────────────────
            var maxTol = golden.Max(r => r.ToleranceMiles);
            var platformBound = GeoWorstUlpDrift(golden.Where(r => r.DiscriminatesModel).ToList());
            Report("A1 tolerance evidence", string.Format(inv,
                "platform variance {0:E3} mi  <<  tolerance {1:E3} mi  <<  wrong-formula error {2:F1} mi",
                platformBound, maxTol, minPlanar));
            Check("A1 the tolerance sits strictly between measured platform noise and the "
                  + "smallest wrong-formula error (a tolerance that cannot be shown to sit "
                  + "between the two is tuning until green)",
                  platformBound < maxTol && maxTol < minPlanar);

            // =========================================================================
            //  A2 — DISTANCE IS A METRIC, STATED OVER COORDINATES NOT IDENTITIES
            // =========================================================================
            var probe = new[]
            {
                GeoCoordinate.Create(35.99, -78.91), GeoCoordinate.Create(21.31, -157.83),
                GeoCoordinate.Create(44.90, -68.67), GeoCoordinate.Create(61.2181, -149.9003),
                GeoCoordinate.Create(18.4655, -66.1057), GeoCoordinate.Create(-33.86, 151.21),
                GeoCoordinate.Create(0.0, 0.0), GeoCoordinate.Create(90.0, 0.0),
                GeoCoordinate.Create(-90.0, 180.0), GeoCoordinate.Create(20.8783, -156.6825),
            };

            Check("A2 EQUAL coordinates return exactly 0.0",
                  probe.All(p => GeoDistance.DistanceMiles(p, p) == 0.0));
            Check("A2 every distance is finite and non-negative",
                  probe.SelectMany(a => probe, (a, b) => GeoDistance.DistanceMiles(a, b))
                       .All(d => double.IsFinite(d) && d >= 0.0));
            Check("A2 symmetric",
                  probe.SelectMany(a => probe, (a, b) => (a, b))
                       .All(p => GeoDistance.DistanceMiles(p.a, p.b) == GeoDistance.DistanceMiles(p.b, p.a)));

            // ★ TWO DISTINCT PLACES MAY LEGITIMATELY BE ZERO MILES APART when their
            //   coordinates are equal — five Philadelphia schools share one point. The
            //   REVERSE implication is deliberately NOT asserted for arbitrary doubles:
            //   two distinct but extremely close pairs can round to zero, and the public
            //   function accepts any valid coordinate, not only this world's two-decimal
            //   data. Requiring it would mean discrete integer microdegrees for no benefit.
            var samePoint = GeoCoordinate.Create(39.95, -75.16);
            Check("A2 two DISTINCT places at the same coordinate are legitimately 0.0 apart "
                  + "(the implication runs one way only, on purpose)",
                  GeoDistance.DistanceMiles(samePoint, GeoCoordinate.Create(39.95, -75.16)) == 0.0);

            // Triangle inequality with a documented epsilon, deterministic sweep.
            const double TriangleEpsilonMiles = 1e-6;
            var worstViolation = 0.0; var worstTriple = "";
            for (var i = 0; i < probe.Length; i++)
                for (var j = 0; j < probe.Length; j++)
                    for (var k = 0; k < probe.Length; k++)
                    {
                        var slack = GeoDistance.DistanceMiles(probe[i], probe[k])
                                  + GeoDistance.DistanceMiles(probe[k], probe[j])
                                  - GeoDistance.DistanceMiles(probe[i], probe[j]);
                        if (-slack > worstViolation) { worstViolation = -slack; worstTriple = $"{i},{k},{j}"; }
                    }
            Check("A2 triangle inequality holds over the deterministic sweep, within a "
                  + "documented epsilon", worstViolation <= TriangleEpsilonMiles,
                  string.Format(inv, "max apparent violation {0:E3} mi (triple {1}), epsilon {2:E3}",
                      worstViolation, worstTriple, TriangleEpsilonMiles));
            Report("A2 max apparent triangle violation before tolerance",
                   string.Format(inv, "{0:E3} mi", worstViolation));

            // ★ Near-antipodal: PROPERTIES ONLY, never a mileage. See the file header.
            var antipodal = GeoDistance.DistanceMiles(
                GeoCoordinate.Create(21.31, -157.83), GeoCoordinate.Create(-21.31, 22.17));
            var exactAntipodal = GeoDistance.DistanceMiles(
                GeoCoordinate.Create(45.0, 0.0), GeoCoordinate.Create(-45.0, 180.0));
            Check("A2 a near-antipodal pair stays finite and never exceeds half the way round "
                  + "— the clamp holds (PROPERTIES asserted, not a mileage: the answer is "
                  + "ill-conditioned there and a golden number would go red for no reason)",
                  double.IsFinite(antipodal) && antipodal > 0.0
                  && antipodal <= GeoDistance.HalfCircumferenceMiles + TriangleEpsilonMiles
                  && double.IsFinite(exactAntipodal)
                  && exactAntipodal <= GeoDistance.HalfCircumferenceMiles + TriangleEpsilonMiles,
                  string.Format(inv, "near {0:F1} mi, exact-antipodal {1:F1} mi, half-circumference {2:F1} mi",
                      antipodal, exactAntipodal, GeoDistance.HalfCircumferenceMiles));

            // Coordinate construction: the guarantee the distance function relies on.
            Check("A2 the coordinate factory refuses NaN, infinity and off-globe values",
                  !GeoCoordinate.TryCreate(double.NaN, 0, out _)
                  && !GeoCoordinate.TryCreate(0, double.PositiveInfinity, out _)
                  && !GeoCoordinate.TryCreate(90.0001, 0, out _)
                  && !GeoCoordinate.TryCreate(0, -180.0001, out _)
                  && GeoCoordinate.TryCreate(90, 180, out _)
                  && GeoCoordinate.TryCreate(-90, -180, out _));
            var negZero = GeoCoordinate.Create(-0.0, -0.0);
            Check("A2 negative zero is normalised at the factory (a minus sign in front of a "
                  + "zero must not give the same world two fingerprints)",
                  double.IsPositive(negZero.LatitudeDegrees) && double.IsPositive(negZero.LongitudeDegrees));

            // =========================================================================
            //  A3 — THE PLACE TABLE IS COMPLETE AND KEYED CORRECTLY
            // =========================================================================
            var stock = LoadWorld(stockPath);
            var placeById = stock.Places.ToDictionary(p => p.PlaceId);
            Check("A3 every school resolves to exactly one place",
                  stock.Schools.All(s => placeById.ContainsKey(s.PlaceId)));
            Check("A3 (Name, Subdivision, Country) is unique across the place list",
                  stock.Places.Select(p => (p.Name, p.Subdivision, p.Country)).Distinct().Count() == stock.Places.Count);
            Check("A3 placeIds are positive and unique",
                  stock.Places.All(p => p.PlaceId > 0)
                  && stock.Places.Select(p => p.PlaceId).Distinct().Count() == stock.Places.Count);

            // ★ THE TRAP: 13 city names appear in TWO states. A naive key merges Durham NC
            //   with Durham NH and puts Duke in New Hampshire.
            var duplicateCityNames = new[]
            {
                "Conway", "Durham", "Athens", "Lexington", "Richmond", "Jacksonville",
                "Bowling Green", "Columbia", "Charleston", "Newark", "Greenville",
                "Huntsville", "Oxford",
            };
            var collapsed = duplicateCityNames
                .Where(n => stock.Places.Count(p => p.Name == n && p.Country == "US") < 2).ToList();
            Check("A3 all 13 repeated city names resolve to DIFFERENT places",
                  collapsed.Count == 0,
                  collapsed.Count == 0 ? "13 of 13 kept apart" : "collapsed: " + string.Join(", ", collapsed));
            var duke = stock.Schools.FirstOrDefault(s => s.Name == "Duke");
            Check("A3 Duke is in North Carolina",
                  duke is not null && placeById[duke.PlaceId].Name == "Durham"
                  && placeById[duke.PlaceId].Subdivision == "NC",
                  duke is null ? "Duke not found" : placeById[duke.PlaceId].Descriptor);

            // ★ A DELIBERATELY MISMATCHED SCHOOL/PLACE DESCRIPTOR IS REFUSED BY NAME.
            //   Asserting only that a placeId resolves cannot catch this: the id resolves
            //   perfectly, to the wrong city.
            Check("A3 the converter REFUSES a school whose csv city/state contradicts its "
                  + "placeId, and names the school",
                  GeoMismatchRefused(teamsCsv, confCsv, placesCsv, out var mismatchMsg), mismatchMsg);

            // =========================================================================
            //  A4 — THE AUTHORED PLACES ARE PRESENT BY NAME, BOTH LISTS, INDIVIDUALLY
            // =========================================================================
            var domestic = new[]
            {
                "Phoenix", "Oklahoma City", "Anaheim", "Brooklyn", "Grand Rapids",
                "Virginia Beach", "Hartford", "Uncasville", "Sioux Falls",
            };
            var exotic = new[]
            {
                "Lahaina", "Nassau", "Canc\u00FAn", "George Town", "Charlotte Amalie",
                "Montego Bay", "San Juan", "Anchorage",
            };
            foreach (var n in domestic)
                Check($"A4 domestic host place present: {n}",
                      stock.Places.Any(p => p.Name == n && p.Tags.Contains("domestic")));
            foreach (var n in exotic)
                Check($"A4 exotic host place present: {n}",
                      stock.Places.Any(p => p.Name == n && p.Tags.Contains("exotic")));
            Check("A4 Lahaina is a SEPARATE place from Honolulu (different islands, ~80 miles)",
                  stock.Places.Any(p => p.Name == "Lahaina") && stock.Places.Any(p => p.Name == "Honolulu")
                  && GeoDistance.DistanceMiles(
                        stock.Places.First(p => p.Name == "Lahaina").Coordinate,
                        stock.Places.First(p => p.Name == "Honolulu").Coordinate) > 50.0);
            Check("A4 tags are authored data that no distance and no predicate reads — every "
                  + "tag is in the vocabulary and canonically sorted",
                  stock.Places.All(p => p.Tags.All(t => t is "domestic" or "exotic")
                                        && p.Tags.SequenceEqual(p.Tags.OrderBy(t => t, StringComparer.Ordinal))
                                        && p.Tags.Distinct().Count() == p.Tags.Length));

            // =========================================================================
            //  A5 — COORDINATES ARE PLAUSIBLE WORLDWIDE, NOT AMERICAN
            // =========================================================================
            Check("A5 every place is inside -90..90 and -180..180",
                  stock.Places.All(p => GeoCoordinate.IsValid(
                      p.Coordinate.LatitudeDegrees, p.Coordinate.LongitudeDegrees)));

            // ★ A PLAUSIBILITY CHECK ON A COORDINATE MUST BE A WORLD CHECK, NEVER A US ONE,
            //   or the exotic list fails validation the day it lands. Proven as a NEGATIVE
            //   CONTROL: build the US-shaped validator and require it to REJECT every exotic
            //   place — because merely asserting they sit outside a rectangle does not work.
            //   ★ A LAT/LONG RECTANGLE AROUND THE LOWER 48 ALSO CONTAINS THE BAHAMAS, most
            //   of the Caribbean and a third of Mexico: Nassau is inside it. So the control
            //   has TWO arms, and every exotic place must be caught by at least one — the
            //   box catches the two that are US soil far from the mainland, the country code
            //   catches the six that are not US at all.
            static bool InContiguousUsBox(GeoCoordinate c)
                => c.LatitudeDegrees is >= 24.5 and <= 49.5
                   && c.LongitudeDegrees is >= -125.0 and <= -66.9;

            var exoticPlaces = stock.Places.Where(p => p.Tags.Contains("exotic")).OrderBy(p => p.PlaceId).ToList();
            var missedByBothArms = new List<string>();
            var caughtByBox = new List<string>();
            var caughtByCountry = new List<string>();
            foreach (var p in exoticPlaces)
            {
                var outsideBox = !InContiguousUsBox(p.Coordinate);
                var notUsSoil = p.Country != "US";
                if (outsideBox) caughtByBox.Add(p.Name);
                if (notUsSoil) caughtByCountry.Add(p.Name);
                if (!outsideBox && !notUsSoil) missedByBothArms.Add(p.Name);
            }
            Check("A5 NEGATIVE CONTROL: a US-shaped validator REJECTS every exotic place — "
                  + "so one could never have shipped in front of this list",
                  exoticPlaces.Count == 8 && missedByBothArms.Count == 0,
                  missedByBothArms.Count == 0
                    ? $"{exoticPlaces.Count} exotic places; box catches [{string.Join(", ", caughtByBox)}]; "
                      + $"non-US country catches [{string.Join(", ", caughtByCountry)}]"
                    : "a US-shaped validator would have ACCEPTED: " + string.Join(", ", missedByBothArms));
            Check("A5 the real validator accepts all 310 places, including every one a "
                  + "US-shaped check would have thrown out",
                  stock.Places.All(p => GeoCoordinate.IsValid(
                      p.Coordinate.LatitudeDegrees, p.Coordinate.LongitudeDegrees)));
            Check("A5 the map reaches beyond the mainland in both directions "
                  + "(a sub-tropical place and a sub-arctic one)",
                  stock.Places.Any(p => p.Coordinate.LatitudeDegrees < 20.0)
                  && stock.Places.Any(p => p.Coordinate.LatitudeDegrees > 55.0));
            Check("A5 country codes are ISO 3166-1 alpha-2, with territories on their OWN "
                  + "codes rather than filed under US",
                  stock.Places.Any(p => p.Name == "San Juan" && p.Country == "PR")
                  && stock.Places.Any(p => p.Name == "Charlotte Amalie" && p.Country == "VI")
                  && stock.Places.Any(p => p.Name == "George Town" && p.Country == "KY"));

            // =========================================================================
            //  A6 — HOSTING IS AN EXPLICIT TAGGED VALUE
            // =========================================================================
            var philly = stock.Places.First(p => p.Name == "Philadelphia");
            var phillySchools = stock.Schools.Where(s => s.PlaceId == philly.PlaceId).OrderBy(s => s.Id).ToList();
            Check("A6 the five Philadelphia schools share one place and it costs nothing",
                  phillySchools.Count == 5, $"{phillySchools.Count} schools at {philly.Descriptor}");

            var drexel = phillySchools.First(s => s.Name == "Drexel");
            var villanovaHome = stock.Schools.First(s => s.Name == "Saint Joseph's");
            var siteA = new GameSite(philly.PlaceId, new GameHost.School(drexel.Id));
            var siteB = new GameSite(philly.PlaceId, new GameHost.School(villanovaHome.Id));
            var siteC = new GameSite(philly.PlaceId, new GameHost.Nobody());
            Check("A6 all three cases construct: two different home games and a neutral site "
                  + "in the same city",
                  siteA.Host is GameHost.School && siteB.Host is GameHost.School && siteC.Host is GameHost.Nobody);
            Check("A6 Nobody and School(id) are distinguishable, and Nobody is a NAMED case "
                  + "rather than a null standing in for absence",
                  siteC.Host is GameHost.Nobody && siteC.Host is not null
                  && siteA.Host != siteB.Host && siteA.Host != siteC.Host);
            var validated = true; var hostMsg = "";
            try { ValidateGameSite(siteA, stock); ValidateGameSite(siteB, stock); ValidateGameSite(siteC, stock); }
            catch (InvalidOperationException ex) { validated = false; hostMsg = ex.Message; }
            Check("A6 all three legal sites validate", validated, hostMsg);

            var farAway = stock.Places.First(p => p.Name == "Lahaina");
            var rejected = false; var rejectMsg = "";
            try { ValidateGameSite(new GameSite(farAway.PlaceId, new GameHost.School(drexel.Id)), stock); }
            catch (InvalidOperationException ex) { rejected = true; rejectMsg = ex.Message; }
            Check("A6 host-implies-own-place rejects a mismatch, naming the school",
                  rejected && rejectMsg.Contains("Drexel", StringComparison.Ordinal), rejectMsg);
            Check("A6 a NEUTRAL SITE is not a category — the same value shapes a game nobody "
                  + "hosts in Lahaina as in Philadelphia",
                  GeoSiteConstructs(new GameSite(farAway.PlaceId, new GameHost.Nobody()), stock));

            // =========================================================================
            //  A7 — SCHEMA v3, BYTE-EXACT
            // =========================================================================
            var v1Refused = false; var v1Msg = "";
            try { LoadWorld(v1Path); }
            catch (InvalidOperationException ex) { v1Refused = true; v1Msg = ex.Message; }
            Check("A7 the retained schemaVersion 1 fixture is REFUSED with a named message",
                  v1Refused && v1Msg.Contains("schemaVersion 1", StringComparison.Ordinal), v1Msg);

            // ★ S93 — the v2 refusal gets a NEGATIVE CONTROL rather than a second committed
            //   retired fixture. The v1 file is CONSTRUCTED into a v2 one and required to be
            //   refused for the v2 reason (no conference game count) rather than the v1 one
            //   (no places table) — which is the discriminating part: a guard that fell
            //   through to the generic "unsupported version" message would still refuse the
            //   file and would still look green here without this check on the WORDS.
            var v2Path = Path.Combine(Path.GetTempPath(), $"charm_v2_retired_{Guid.NewGuid():N}.json");
            var v2Refused = false; var v2Msg = "";
            try
            {
                File.WriteAllText(v2Path,
                    File.ReadAllText(v1Path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2",
                                                     StringComparison.Ordinal));
                try { LoadWorld(v2Path); }
                catch (InvalidOperationException ex) { v2Refused = true; v2Msg = ex.Message; }
            }
            finally { if (File.Exists(v2Path)) File.Delete(v2Path); }
            Check("A7 a schemaVersion 2 world is REFUSED by name, for the v2 reason "
                  + "(no conference game count) and not the v1 one",
                  v2Refused && v2Msg.Contains("schemaVersion 2", StringComparison.Ordinal)
                    && v2Msg.Contains("games", StringComparison.Ordinal), v2Msg);

            // ★ S94 — the SAME negative-control pattern, one version up: a v3 document must
            //   be refused for the v3 reason (no playing nights / week count / wall — it
            //   cannot say WHEN its season is), never the generic message.
            var v3Path = Path.Combine(Path.GetTempPath(), $"charm_v3_retired_{Guid.NewGuid():N}.json");
            var v3Refused = false; var v3Msg = "";
            try
            {
                File.WriteAllText(v3Path,
                    File.ReadAllText(v1Path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 3",
                                                     StringComparison.Ordinal));
                try { LoadWorld(v3Path); }
                catch (InvalidOperationException ex) { v3Refused = true; v3Msg = ex.Message; }
            }
            finally { if (File.Exists(v3Path)) File.Delete(v3Path); }
            Check("A7 ★ a schemaVersion 3 world is REFUSED by name, for the v3 reason "
                  + "(no nights, no weeks, no wall — it cannot say WHEN its season is)",
                  v3Refused && v3Msg.Contains("schemaVersion 3", StringComparison.Ordinal)
                    && v3Msg.Contains("WHEN", StringComparison.Ordinal), v3Msg);

            var tiny = LoadWorld(tinyPath);
            var format = LoadWorld(formatPath);
            Check("A7 all three migrated world files are schemaVersion 4 and validate",
                  stock.SchemaVersion == 4 && tiny.SchemaVersion == 4 && format.SchemaVersion == 4);

            // ★ CANONICAL BYTES COMPARED, NEVER DECODED OBJECT EQUALITY. Object equality
            //   would pass while the key order, the indent or a number's spelling drifted,
            //   and it is the BYTES the world fingerprint hashes.
            foreach (var (label, path, world) in new[]
                     { ("stock-d1", stockPath, stock), ("fixture-tiny", tinyPath, tiny),
                       ("fixture-format", formatPath, format) })
            {
                var onDisk = File.ReadAllBytes(path);
                var rewritten = CanonicalWorldBytes(world);
                Check($"A7 {label}: load -> canonical bytes reproduces the committed file exactly",
                      onDisk.AsSpan().SequenceEqual(rewritten),
                      $"{onDisk.Length} bytes on disk, {rewritten.Length} rewritten");
                Check($"A7 {label}: a SECOND canonical write is byte-identical",
                      rewritten.AsSpan().SequenceEqual(CanonicalWorldBytes(world)));
            }

            // ★ The format fixture is what pins the RULE rather than the values: an
            //   integer-valued coordinate, a short decimal, a value needing many significant
            //   digits, one that emits in exponent notation, and a normalised zero.
            var formatText = File.ReadAllText(formatPath);
            Check("A7 the canonical form pins every number-formatting case that exists",
                  formatText.Contains("\"lat\": 45,", StringComparison.Ordinal)
                  && formatText.Contains("\"lat\": -33.86,", StringComparison.Ordinal)
                  && formatText.Contains("12.345678901234567", StringComparison.Ordinal)
                  && formatText.Contains("E-07", StringComparison.Ordinal)
                  && formatText.Contains("\"lat\": 0,", StringComparison.Ordinal)
                  && !formatText.Contains("-0,", StringComparison.Ordinal));

            var converted = ConvertWorld(teamsCsv, confCsv, placesCsv);
            Check("A7 ConvertWorld round-trips places: the converter's canonical bytes equal "
                  + "the committed stock world",
                  CanonicalWorldBytes(converted).AsSpan().SequenceEqual(File.ReadAllBytes(stockPath)));

            var seeded = SeedWorld(stock, 20260802);
            Check("A7 SeedWorld carries the place table through unchanged",
                  seeded.Places.Count == stock.Places.Count
                  && seeded.Places.OrderBy(p => p.PlaceId).Zip(stock.Places.OrderBy(p => p.PlaceId))
                          .All(z => z.First.PlaceId == z.Second.PlaceId
                                    && z.First.Coordinate == z.Second.Coordinate
                                    && z.First.Name == z.Second.Name));
            Check("A7 SeedWorld perturbs prestige and MOVES NO COORDINATE",
                  seeded.Schools.OrderBy(s => s.Id).Zip(stock.Schools.OrderBy(s => s.Id))
                        .All(z => z.First.PlaceId == z.Second.PlaceId)
                  && !seeded.Schools.OrderBy(s => s.Id).Select(s => s.CurrentPrestige)
                          .SequenceEqual(stock.Schools.OrderBy(s => s.Id).Select(s => s.CurrentPrestige)));

            // The tiny fixture is authored to DISCRIMINATE rather than to duplicate
            // production data (r1 §15).
            var tinyPlaceById = tiny.Places.ToDictionary(p => p.PlaceId);
            Check("A7 the tiny fixture carries two schools sharing ONE place",
                  tiny.Schools.GroupBy(s => s.PlaceId).Any(g => g.Count() == 2));
            Check("A7 the tiny fixture carries two places sharing a NAME, kept apart by "
                  + "jurisdiction",
                  tiny.Places.GroupBy(p => p.Name).Any(g => g.Count() == 2
                        && g.Select(p => p.Subdivision).Distinct().Count() == 2));
            Check("A7 the tiny fixture carries a place with NO school",
                  tiny.Places.Any(p => tiny.Schools.All(s => s.PlaceId != p.PlaceId)));
            Check("A7 the tiny fixture carries a long-distance pair",
                  tiny.Schools.SelectMany(a => tiny.Schools, (a, b) => (a, b))
                      .Any(p => GeoDistance.DistanceMiles(
                          tinyPlaceById[p.a.PlaceId].Coordinate,
                          tinyPlaceById[p.b.PlaceId].Coordinate) > 2000.0));
            Check("A7 the tiny fixture still has exactly 20 schools (school count drives the "
                  + "talent pool; adding one would move every season number downstream)",
                  tiny.Schools.Count == 20, $"got {tiny.Schools.Count}");

            // =========================================================================
            //  A8 — REPORTED, NOT JUDGED. ★ NOTHING BELOW IS ASSERTED.
            // =========================================================================
            var schools = stock.Schools.OrderBy(s => s.Id).ToList();
            double D(WorldSchool a, WorldSchool b)
                => GeoDistance.DistanceMiles(placeById[a.PlaceId].Coordinate, placeById[b.PlaceId].Coordinate);
            var pairs = AllPairs(schools).ToList();
            var lo = pairs.OrderBy(p => D(p.A, p.B)).ThenBy(p => p.A.Id).ThenBy(p => p.B.Id).First();
            var hi = pairs.OrderByDescending(p => D(p.A, p.B)).ThenBy(p => p.A.Id).ThenBy(p => p.B.Id).First();
            var ds = pairs.Select(p => D(p.A, p.B)).OrderBy(d => d).ToList();
            Report("A8 longest school-to-school",
                string.Format(inv, "{0} <-> {1}  {2:F1} mi", hi.A.Name, hi.B.Name, D(hi.A, hi.B)));
            Report("A8 shortest school-to-school",
                string.Format(inv, "{0} <-> {1}  {2:F1} mi", lo.A.Name, lo.B.Name, D(lo.A, lo.B)));
            Report("A8 mean / median over all pairs",
                string.Format(inv, "{0:F1} / {1:F1} mi over {2} pairs", ds.Average(), Median(ds), ds.Count));
            var confTrips = stock.Conferences.OrderBy(c => c.Id)
                .Select(c => (c, m: schools.Where(s => s.ConferenceId == c.Id).ToList()))
                .Where(x => x.m.Count >= 2)
                .Select(x => (x.c.Name, Best: AllPairs(x.m)
                    .OrderByDescending(p => D(p.A, p.B)).ThenBy(p => p.A.Id).ThenBy(p => p.B.Id).First()))
                .OrderByDescending(x => D(x.Best.A, x.Best.B)).ToList();
            Report("A8 longest intra-conference trip", string.Format(inv, "{0}: {1} <-> {2}  {3:F1} mi",
                confTrips[0].Name, confTrips[0].Best.A.Name, confTrips[0].Best.B.Name,
                D(confTrips[0].Best.A, confTrips[0].Best.B)));
            Report("A8 shortest longest-intra-conference trip", string.Format(inv, "{0}: {1:F1} mi",
                confTrips[^1].Name, D(confTrips[^1].Best.A, confTrips[^1].Best.B)));
            Report("A8 school pairs at zero miles (shared place)",
                ds.Count(d => d == 0.0).ToString(inv));

            // =========================================================================
            //  A9 — ISOLATION, and A10 — PURITY
            // =========================================================================
            if (!TryFindRepoRoot(out var root))
            {
                Check("A9/A10 source root located (walked up from the binary)", false,
                      "no repo root found above " + baseDir);
            }
            else
            {
                var seasonPath = new[]
                {
                    "src/Charm.Harness/Program.Season.cs",
                    "src/Charm.Harness/Program.Season.Stats.cs",
                    "src/Charm.Harness/Program.Season.Calibration.cs",
                    "src/Charm.Harness/Program.Season.Retention.cs",
                };
                var geoTypes = new[] { "GeoCoordinate", "GeoDistance", "GameSite", "GameHost", "WorldPlace" };
                var leaked = new List<string>();
                foreach (var rel in seasonPath)
                {
                    var full = Path.Combine(root!, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full)) { leaked.Add(rel + " MISSING"); continue; }
                    var text = File.ReadAllText(full);
                    foreach (var t in geoTypes)
                        if (text.Contains(t, StringComparison.Ordinal)) leaked.Add(rel + " -> " + t);
                }
                // ★ THE ISOLATION CHECK IS ON THE PAGE, NOT THE FINGERPRINT. The world
                //   fingerprint IS expected to move — that was ruled, and it breaks the
                //   binding of any history written before S92. It is free today because no
                //   career exists outside this repo, and permanently expensive the day one
                //   does.
                Check("A9 no file on the season path names a geography type — S92 cannot have "
                      + "moved the season page",
                      leaked.Count == 0,
                      leaked.Count == 0 ? $"{seasonPath.Length} files scanned" : string.Join(" | ", leaked));

                var geoSource = File.ReadAllText(Path.Combine(root!,
                    "src/Charm.Engine/Core/GeoDistance.cs".Replace('/', Path.DirectorySeparatorChar)));
                var dt = "DateTime"; var dto = "DateTimeOffset";
                var clockNeedles = new[]
                {
                    dt + ".Now", dt + ".UtcNow", dt + ".Today", dto + ".Now", dto + ".UtcNow",
                    "TimeZoneInfo" + ".Local", "TimeProvider" + ".System",
                };
                Check("A10 the distance file reads no wall clock",
                      clockNeedles.All(n => !geoSource.Contains(n, StringComparison.Ordinal)));
                // ★ THE NEEDLES ARE ASSEMBLED AT RUNTIME, and they are the spellings that
                //   actually READ something rather than the words that describe not reading
                //   it. The first version of this check searched for the word "config" and
                //   went red on GeoDistance.cs's own header line promising it reads no
                //   config — a scan tripping on its own documentation.
                var cfg = "Config";
                var configNeedles = new[]
                {
                    cfg + ".Load(", "config" + ".json", "configPath", "AppContext" + ".BaseDirectory",
                };
                Check("A10 the distance file reads no culture default and loads no config",
                      !geoSource.Contains("CultureInfo" + ".Current", StringComparison.Ordinal)
                      && configNeedles.All(n => !geoSource.Contains(n, StringComparison.Ordinal)));

                var geoCommand = File.ReadAllText(Path.Combine(root!,
                    "src/Charm.Harness/Program.Geography.cs".Replace('/', Path.DirectorySeparatorChar)));
                Check("A10 the geography command loads no config and plays no basketball",
                      !geoCommand.Contains("configPath", StringComparison.Ordinal)
                      && !geoCommand.Contains("Resolver", StringComparison.Ordinal)
                      && !geoCommand.Contains("Governor", StringComparison.Ordinal)
                      && !geoCommand.Contains("RunSeason", StringComparison.Ordinal));
            }

            // ★ Distance is culture-blind by construction — it does no formatting at all —
            //   but the printed page is the thing a foreign decimal separator would break.
            var before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                var reportInvariant = RenderGeographyReport(tiny, "fixture");
                System.Globalization.CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Check("A10 the printed map is byte-identical under a comma-decimal culture",
                      RenderGeographyReport(tiny, "fixture") == reportInvariant);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = before; }
        }
        catch (Exception ex)
        {
            Check($"unexpected exception: {ex.GetType().Name}: {ex.Message}", false);
        }

        Console.WriteLine(ok ? "  Phase 83: PASS" : "  Phase 83: FAIL");
        return ok;
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================
    private static List<GeoGoldenRow> LoadGeoGolden(string path)
    {
        var rows = new List<GeoGoldenRow>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var el in doc.RootElement.GetProperty("rows").EnumerateArray())
            rows.Add(new GeoGoldenRow(
                el.GetProperty("label").GetString() ?? "",
                el.GetProperty("lat1").GetDouble(), el.GetProperty("long1").GetDouble(),
                el.GetProperty("lat2").GetDouble(), el.GetProperty("long2").GetDouble(),
                el.GetProperty("expectedMiles").GetDouble(),
                el.GetProperty("method").GetString() ?? "",
                el.GetProperty("toleranceMiles").GetDouble(),
                el.GetProperty("discriminatesModel").GetBoolean()));
        return rows;
    }

    /// <summary>The wrong implementation, built on purpose: equirectangular projection —
    /// flatten the globe, use Pythagoras. It is what a reasonable person writes if they have
    /// not thought about the shape of the earth, and it is within 1% on every short American
    /// pair, which is exactly why the golden table is full of long ones.</summary>
    private static double GeoPlanarMiles(double lat1, double long1, double lat2, double long2)
    {
        const double p = Math.PI / 180.0;
        var x = (long2 - long1) * p * Math.Cos((lat1 + lat2) * 0.5 * p);
        var y = (lat2 - lat1) * p;
        return GeoDistance.EarthMeanRadiusMiles * Math.Sqrt(x * x + y * y);
    }

    /// <summary>Measures how far the answer can move if every library trig call the formula
    /// makes were off by up to 4 ULP — a generous stand-in for the Windows/Linux libm gap,
    /// since both runtimes document under 1 ULP. This is the LEFT-HAND number in the
    /// tolerance ordering, and it is MEASURED here rather than asserted from memory.</summary>
    private static double GeoWorstUlpDrift(List<GeoGoldenRow> rows)
    {
        static double Kick(double x, int n)
        {
            if (x == 0.0 || n == 0) return x;
            var bits = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(x > 0 ? bits + n : bits - n);
        }
        static double Perturbed(double lat1, double long1, double lat2, double long2, int n)
        {
            const double p = Math.PI / 180.0;
            var a1 = lat1 * p; var a2 = lat2 * p;
            var s1 = Kick(Math.Sin((a2 - a1) * 0.5), n);
            var s2 = Kick(Math.Sin((long2 - long1) * p * 0.5), n);
            var c1 = Kick(Math.Cos(a1), n);
            var c2 = Kick(Math.Cos(a2), n);
            var h = s1 * s1 + c1 * c2 * s2 * s2;
            if (h < 0.0) h = 0.0;
            if (h > 1.0) h = 1.0;
            return 2.0 * GeoDistance.EarthMeanRadiusMiles * Kick(Math.Asin(Math.Sqrt(h)), n);
        }

        var worst = 0.0;
        foreach (var r in rows)
        {
            var baseline = GeoDistance.DistanceMiles(
                GeoCoordinate.Create(r.Lat1, r.Long1), GeoCoordinate.Create(r.Lat2, r.Long2));
            foreach (var n in new[] { 4, -4 })
                worst = Math.Max(worst, Math.Abs(Perturbed(r.Lat1, r.Long1, r.Lat2, r.Long2, n) - baseline));
        }
        return worst;
    }

    /// <summary>Writes a teams csv whose city text contradicts its placeId and requires the
    /// converter to refuse it BY NAME. The mismatch class cannot be reached from the
    /// committed data — the descriptor IS the group key there — so it has to be built.</summary>
    private static bool GeoMismatchRefused(string teamsCsv, string confCsv, string placesCsv, out string message)
    {
        var dir = Path.Combine(Path.GetTempPath(), "charm-geo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var lines = File.ReadAllText(teamsCsv).Replace("\r\n", "\n").Split('\n').ToList();
            var idx = lines.FindIndex(l => l.StartsWith("72,Duke,", StringComparison.Ordinal));
            if (idx < 0) { message = "Duke row not found in teams.csv"; return false; }
            var cells = lines[idx].Split(',');
            cells[7] = "NH";                       // Duke now claims New Hampshire, placeId untouched
            lines[idx] = string.Join(",", cells);
            var rigged = Path.Combine(dir, "teams.csv");
            File.WriteAllText(rigged, string.Join("\n", lines));

            try { ConvertWorld(rigged, confCsv, placesCsv); }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
                return ex.Message.Contains("Duke", StringComparison.Ordinal);
            }
            message = "converted without complaint — a school had two answers for where it is";
            return false;
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static bool GeoSiteConstructs(GameSite site, WorldFile world)
    {
        try { ValidateGameSite(site, world); return true; }
        catch (InvalidOperationException) { return false; }
    }
}
