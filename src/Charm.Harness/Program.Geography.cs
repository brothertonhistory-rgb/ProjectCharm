using System.Globalization;
using System.Text;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S92 — `dotnet run -- geography <world.json>`
//
//  ★ ITS OWN COMMAND, and it returns before the validation suite ever loads a
//  config — the same shape as `calendar`, `season`, `divvy` and `world`. The
//  season page must stay byte-identical to its pre-S92 self, and the cheapest
//  way to guarantee that is for the map to have no way to reach it.
//
//  ★ NOTHING CONSUMES THE MAP YET. No game is placed anywhere, no crowd is
//  modelled, and there is NO HOME-COURT ADVANTAGE at the end of this session.
//  The host fact below is defined and unused on purpose: home court is the next
//  build and the scheduler is the one after.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ R3 — HOSTING IS A SEPARATE FACT FROM LOCATION. Emmett: *"There should
    /// just be a flag of an actual home court… If Villanova plays Drexel @ Drexel, that's
    /// different than a neutral site at a big Philly arena."*
    ///
    /// <para>Two independent facts. WHERE the game is, which is what travel and crowd reach
    /// read off, and WHOSE GYM it is, which is a school or nobody.</para>
    ///
    /// <para>★ A NEUTRAL SITE STOPS BEING A CATEGORY. It is a game nobody hosts — identical
    /// machinery in Kansas City, in Lahaina, and in a city where four schools live. That is
    /// what makes the five Philadelphia schools sharing one point cost nothing.</para></summary>
    private abstract record GameHost
    {
        /// <summary>★ A NAMED CASE, never a null standing in for absence. "Nobody hosts this"
        /// and "we have not worked out who hosts this" are different facts, and one silently
        /// standing in for the other is exactly what surfaces as a wrong home-court call
        /// years into a career.</summary>
        public sealed record Nobody : GameHost;

        public sealed record School(int SchoolId) : GameHost;
    }

    /// <summary>Where a game is played and whose floor it is. ★ S92 DEFINES THIS AND NOTHING
    /// USES IT — no game record gains a field, because the scheduler owns that.</summary>
    private sealed record GameSite(int PlaceId, GameHost Host);

    /// <summary>★ WORLD-LEVEL RULE, and it lives here rather than on the value because a
    /// pure value cannot resolve a school: if a school hosts, the game is at THAT SCHOOL'S
    /// OWN PLACE, because R3 defines host as the actual home court.
    ///
    /// <para>★ STATED LIMITATION, NOT A BUG: this makes a DISPLACED home game
    /// unrepresentable — a team hosting in a downtown arena one city over, or Hawaii hosting
    /// the Diamond Head Classic. At city-level precision most of those collapse harmlessly.
    /// If displaced home games are ever wanted, that is a ruling, not a defect to fix
    /// quietly.</para></summary>
    private static void ValidateGameSite(GameSite site, WorldFile world)
    {
        var place = world.Places.FirstOrDefault(p => p.PlaceId == site.PlaceId)
            ?? throw new InvalidOperationException($"game site references unknown placeId {site.PlaceId}.");

        if (site.Host is not GameHost.School host) return;

        var school = world.Schools.FirstOrDefault(s => s.Id == host.SchoolId)
            ?? throw new InvalidOperationException($"game site is hosted by unknown school id {host.SchoolId}.");

        if (school.PlaceId != site.PlaceId)
        {
            var own = world.Places.First(p => p.PlaceId == school.PlaceId);
            throw new InvalidOperationException(
                $"school '{school.Name}' is listed as HOSTING a game at '{place.Descriptor}', but its own home " +
                $"is '{own.Descriptor}'. A host is the actual home court; a game somewhere else is hosted by " +
                "nobody.");
        }
    }

    // =====================================================================================
    // The printed map
    // =====================================================================================
    private static int RunGeography(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("usage: dotnet run -- geography <world.json>");
            return 1;
        }

        try
        {
            var world = LoadWorld(args[1]);
            Console.Out.Write(RenderGeographyReport(world, args[1]));
            Console.Out.Flush();
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        {
            Console.WriteLine($"GEOGRAPHY ERROR: {ex.Message}");
            return 1;
        }
    }

    /// <summary>★ DETERMINISTIC BY CONSTRUCTION. Places in placeId order; school pairs
    /// normalised lower id first and tie-broken by school id; conferences in conference-id
    /// order; invariant numeric formatting; literal "\n" newlines rather than
    /// Environment.NewLine, so the report is byte-identical on Windows and Linux.</summary>
    private static string RenderGeographyReport(WorldFile w, string sourceLabel)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append('\n');

        var placeById = w.Places.ToDictionary(p => p.PlaceId);
        var schools = w.Schools.OrderBy(s => s.Id).ToList();
        var confById = w.Conferences.ToDictionary(c => c.Id);
        double Dist(WorldSchool a, WorldSchool b)
            => GeoDistance.DistanceMiles(placeById[a.PlaceId].Coordinate, placeById[b.PlaceId].Coordinate);

        Line();
        Line($"=== GEOGRAPHY: {sourceLabel} ===");
        Line($"{w.Places.Count} places | {schools.Count} schools | " +
             $"{w.Places.Count(p => schools.Any(s => s.PlaceId == p.PlaceId))} places with a campus | " +
             $"distances in miles (great-circle, spherical earth, " +
             GeoDistance.EarthMeanRadiusMiles.ToString(inv) + " mi mean radius)");

        // ── 1. Every place, in placeId order. ────────────────────────────────────────────
        Line();
        Line("PLACES");
        Line($"  {"id",4}  {"place",-30} {"country",-7} {"lat",9} {"long",10}  {"campuses",8}  tags");
        foreach (var p in w.Places.OrderBy(p => p.PlaceId))
        {
            var campuses = schools.Count(s => s.PlaceId == p.PlaceId);
            var label = p.Subdivision.Length > 0 ? $"{p.Name}, {p.Subdivision}" : p.Name;
            Line(string.Format(inv, "  {0,4}  {1,-30} {2,-7} {3,9:F4} {4,10:F4}  {5,8}  {6}",
                p.PlaceId, label.Length > 30 ? label[..30] : label, p.Country,
                p.Coordinate.LatitudeDegrees, p.Coordinate.LongitudeDegrees,
                campuses, p.Tags.Length == 0 ? "-" : string.Join(",", p.Tags)));
        }

        // ── 2. Nearest and furthest opponent for every school. ★ "OPPONENT" MEANS EVERY
        //      OTHER SCHOOL IN THE WORLD FILE — the scheduler is out of scope, so no other
        //      meaning is available. Schools sharing a place correctly read zero. ─────────
        Line();
        Line("NEAREST AND FURTHEST OPPONENT (every other school in the file)");
        Line($"  {"id",4}  {"school",-28} {"home",-26} {"nearest",-28} {"mi",9}  {"furthest",-28} {"mi",9}");
        foreach (var s in schools)
        {
            var others = schools.Where(o => o.Id != s.Id).ToList();
            if (others.Count == 0) continue;
            var near = others.OrderBy(o => Dist(s, o)).ThenBy(o => o.Id).First();
            var far = others.OrderByDescending(o => Dist(s, o)).ThenBy(o => o.Id).First();
            Line(string.Format(inv, "  {0,4}  {1,-28} {2,-26} {3,-28} {4,9:F1}  {5,-28} {6,9:F1}",
                s.Id, Trim(s.Name, 28), Trim(placeById[s.PlaceId].Descriptor, 26),
                Trim(near.Name, 28), Dist(s, near), Trim(far.Name, 28), Dist(s, far)));
        }

        // ── 3. The longest trip inside each conference. ──────────────────────────────────
        Line();
        Line("LONGEST TRIP INSIDE EACH CONFERENCE");
        Line($"  {"conference",-36} {"n",3}  {"pair",-52} {"mi",9}");
        foreach (var c in w.Conferences.OrderBy(c => c.Id))
        {
            var members = schools.Where(s => s.ConferenceId == c.Id).ToList();
            if (members.Count < 2) continue;
            var best = AllPairs(members).OrderByDescending(pr => Dist(pr.A, pr.B))
                                        .ThenBy(pr => pr.A.Id).ThenBy(pr => pr.B.Id).First();
            Line(string.Format(inv, "  {0,-36} {1,3}  {2,-52} {3,9:F1}",
                Trim(c.Name, 36), members.Count,
                Trim($"{best.A.Name} <-> {best.B.Name}", 52), Dist(best.A, best.B)));
        }

        // ── 4. The league's extremes. ★ ZERO-MILE SHARED-PLACE PAIRS ARE INCLUDED IN THE
        //      SHORTEST. That a Philadelphia pair reads zero is useful confirmation that
        //      shared places work, not a degenerate result to filter out. ─────────────────
        var all = AllPairs(schools).ToList();
        var longest = all.OrderByDescending(pr => Dist(pr.A, pr.B)).ThenBy(pr => pr.A.Id).ThenBy(pr => pr.B.Id).First();
        var shortest = all.OrderBy(pr => Dist(pr.A, pr.B)).ThenBy(pr => pr.A.Id).ThenBy(pr => pr.B.Id).First();
        var dists = all.Select(pr => Dist(pr.A, pr.B)).OrderBy(d => d).ToList();
        Line();
        Line("THE LEAGUE");
        Line(string.Format(inv, "  longest pair   {0} <-> {1}   {2:F1} mi",
            longest.A.Name, longest.B.Name, Dist(longest.A, longest.B)));
        Line(string.Format(inv, "  shortest pair  {0} <-> {1}   {2:F1} mi",
            shortest.A.Name, shortest.B.Name, Dist(shortest.A, shortest.B)));
        Line(string.Format(inv, "  mean {0:F1} mi | median {1:F1} mi | {2} pairs | {3} pairs at zero (shared place)",
            dists.Average(), Median(dists), dists.Count, dists.Count(d => d == 0.0)));

        // ── 5. Each authored event place and the nearest campus to it. ───────────────────
        var authored = w.Places.Where(p => p.Tags.Length > 0).OrderBy(p => p.PlaceId).ToList();
        if (authored.Count > 0 && schools.Count > 0)
        {
            Line();
            Line("AUTHORED EVENT PLACES — the nearest campus to each");
            Line($"  {"id",4}  {"place",-30} {"tags",-18} {"nearest school",-28} {"mi",9}");
            foreach (var p in authored)
            {
                var near = schools
                    .OrderBy(s => GeoDistance.DistanceMiles(p.Coordinate, placeById[s.PlaceId].Coordinate))
                    .ThenBy(s => s.Id).First();
                Line(string.Format(inv, "  {0,4}  {1,-30} {2,-18} {3,-28} {4,9:F1}",
                    p.PlaceId, Trim(p.Descriptor, 30), string.Join(",", p.Tags), Trim(near.Name, 28),
                    GeoDistance.DistanceMiles(p.Coordinate, placeById[near.PlaceId].Coordinate)));
            }
        }

        Line();
        return sb.ToString();
    }

    private readonly record struct SchoolPair(WorldSchool A, WorldSchool B);

    /// <summary>Every unordered pair, normalised lower id first — the deterministic-ordering
    /// rule that makes "the longest pair" the same answer on every machine.</summary>
    private static IEnumerable<SchoolPair> AllPairs(List<WorldSchool> schools)
    {
        var ordered = schools.OrderBy(s => s.Id).ToList();
        for (var i = 0; i < ordered.Count; i++)
            for (var j = i + 1; j < ordered.Count; j++)
                yield return new SchoolPair(ordered[i], ordered[j]);
    }

    private static double Median(List<double> sorted)
        => sorted.Count == 0 ? 0.0
         : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
         : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;

    private static string Trim(string s, int n) => s.Length > n ? s[..n] : s;
}
