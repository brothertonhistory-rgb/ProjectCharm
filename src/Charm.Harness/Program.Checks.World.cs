using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =====================================================================================
    // Phase 53 — World Structure Pass 1: the static skeleton.
    //
    // Proves the era-file layer: the csv converter produces a valid 347-school D1 world,
    // the tiny fixture proves nothing assumes 347, the validator fails LOUDLY on an
    // infeasible world and on a member below its conference floor, and the seeder is
    // deterministic, band-count-exact at both population sizes, floor-honoring by
    // construction (never a clamp), historical==current, and roughly tier-ordered.
    //
    // Fixed constants below (the canonical n=347 apportionment, the two check seeds) are
    // oracle-derived: the Python oracle mirrors WorldRng / WorldApportion / SeedWorld
    // bit-for-bit and verified every one of these values before this C# existed. A wrong
    // formula fails the constant; a wrong wiring fails the cross-reads.
    // =====================================================================================
    private static bool Phase53WorldStructureCheck()
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 53 — World Structure Pass 1 (schema / validator / seeder) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        // Oracle constant: canonical (seed-independent) largest-remainder at n=347.
        int[] expected347 = { 3, 21, 31, 49, 80, 73, 90 };

        try
        {
            // ── 1. Converter: the committed reference csvs -> a valid stock world. ──────
            var teamsCsv = Path.Combine(AppContext.BaseDirectory, "data", "teams.csv");
            var confCsv  = Path.Combine(AppContext.BaseDirectory, "data", "conf.csv");
            var placesCsv = Path.Combine(AppContext.BaseDirectory, "data", "places.csv");
            var stock = ConvertWorld(teamsCsv, confCsv, placesCsv);
            ValidateWorld(stock);
            Check("stock world converts and validates", true);
            Check("stock school count 347", stock.Schools.Count == 347, $"got {stock.Schools.Count}");
            Check("stock place count 310", stock.Places.Count == 310, $"got {stock.Places.Count}");
            Check("stock conference count 32", stock.Conferences.Count == 32, $"got {stock.Conferences.Count}");
            Check("every school division matches metadata (D1)",
                stock.Division == "D1" && stock.Schools.All(s => s.Division == "D1"));
            Check("stock is authored with no worldSeed", stock.Kind == "authored" && stock.WorldSeed is null);
            Check("stock historical == current everywhere",
                stock.Schools.All(s => s.HistoricalPrestige == s.CurrentPrestige));

            // ── 2. The tiny fixture: nothing assumes 347. ───────────────────────────────
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
            var tiny = LoadWorld(fixturePath);
            Check("tiny fixture loads and validates", true);
            Check("tiny fixture n=20", tiny.Schools.Count == 20, $"got {tiny.Schools.Count}");

            // ── 3. Infeasible world fails LOUDLY, naming the conflict. ──────────────────
            // 300 power-floor schools at n=347 need 40+ slots; the pyramid provides ~53%.
            var infeasible = BuildSyntheticWorld(powerCount: 300, lowCount: 47, powerPrestige: 40, lowPrestige: 5);
            Check("infeasible world rejected with a named conflict",
                ThrowsWorldError(infeasible, out var msg1) && msg1.Contains("infeasible world"), msg1);

            // ── 4. A member below its conference floor fails, naming the school. ────────
            var belowFloor = BuildSyntheticWorld(powerCount: 3, lowCount: 17, powerPrestige: 55, lowPrestige: 5);
            belowFloor.Schools[0] = belowFloor.Schools[0] with { Name = "Below Floor U", CurrentPrestige = 12, HistoricalPrestige = 12 };
            Check("below-floor member rejected naming the school",
                ThrowsWorldError(belowFloor, out var msg2) && msg2.Contains("Below Floor U") && msg2.Contains("floor"), msg2);

            // ── 5. Seeder determinism. ───────────────────────────────────────────────────
            var s1 = SeedWorld(stock, 20260702);
            var s2 = SeedWorld(stock, 20260702);
            var s3 = SeedWorld(stock, 99);
            Check("same seed -> identical assignment",
                s1.Schools.OrderBy(s => s.Id).Select(s => s.CurrentPrestige)
                  .SequenceEqual(s2.Schools.OrderBy(s => s.Id).Select(s => s.CurrentPrestige)));
            Check("different seed -> different assignment",
                !s1.Schools.OrderBy(s => s.Id).Select(s => s.CurrentPrestige)
                   .SequenceEqual(s3.Schools.OrderBy(s => s.Id).Select(s => s.CurrentPrestige)));
            Check("generated metadata correct",
                s1.Kind == "generated" && s1.WorldSeed == 20260702);
            ValidateWorld(s1);
            Check("seeded world itself validates", true);

            // ── 6. Pyramid exactness + floors + historical, both population sizes. ──────
            Check("canonical apportionment at n=347 matches the oracle constant",
                WorldApportion(347, null).SequenceEqual(expected347),
                $"got [{string.Join(",", WorldApportion(347, null))}]");
            Check("n=347 seeded band counts exact", WorldBandCountsExact(s1, 20260702), "");
            var t1 = SeedWorld(tiny, 7);
            ValidateWorld(t1);
            Check("n=20 seeded band counts exact", WorldBandCountsExact(t1, 7), "");
            Check("no seeded school below its conference floor (both worlds)",
                WorldFloorsHonored(s1) && WorldFloorsHonored(t1));
            Check("all seeded values 0-99 (both worlds)",
                s1.Schools.All(s => s.CurrentPrestige is >= 0 and <= 99) &&
                t1.Schools.All(s => s.CurrentPrestige is >= 0 and <= 99));
            Check("seeded historical == current (both worlds)",
                s1.Schools.All(s => s.HistoricalPrestige == s.CurrentPrestige) &&
                t1.Schools.All(s => s.HistoricalPrestige == s.CurrentPrestige));

            // ── 7. Rough tier ordering (means only — overlap is the design). ────────────
            Check("n=347 tier means strictly ordered", WorldTierMeansOrdered(s1, out var d347), d347);
            Check("n=20 tier means strictly ordered",  WorldTierMeansOrdered(t1, out var d20),  d20);
        }
        catch (Exception ex)
        {
            Check($"unexpected exception: {ex.Message}", false);
        }

        Console.WriteLine(pass ? "  Phase 53: PASS" : "  Phase 53: FAIL");
        return pass;
    }

    // A minimal valid-shaped world: all four canonical tiers, one power and one low
    // conference, powerCount + lowCount schools. Callers then break it on purpose.
    private static WorldFile BuildSyntheticWorld(int powerCount, int lowCount, int powerPrestige, int lowPrestige)
    {
        // ★ S92 — every school points at a place. The synthetic world used to put all of its
        //   schools at lat 0, long 0; now they all share ONE place, which is legal (five
        //   schools share Philadelphia in the real world) and keeps this fixture about
        //   prestige, which is all it was ever testing.
        var places = new List<WorldPlace>
        {
            new(1, "Synthetic City", "ST", "US", GeoCoordinate.Create(0.0, 0.0), Array.Empty<string>()),
        };
        var schools = new List<WorldSchool>();
        for (var i = 0; i < powerCount; i++)
            schools.Add(new WorldSchool(i + 1, $"Power {i + 1}", $"P{i + 1}", "#000000",
                1, 1, "D1", powerPrestige, powerPrestige));
        for (var i = 0; i < lowCount; i++)
            schools.Add(new WorldSchool(powerCount + i + 1, $"Low {i + 1}", $"L{i + 1}", "#000000",
                1, 2, "D1", lowPrestige, lowPrestige));
        return new WorldFile
        {
            SchemaVersion = 4, Kind = "authored", EraLabel = "synthetic", Division = "D1", WorldSeed = null,
            Places = places,
            Tiers = WorldTierDefaults.Select(t => new WorldTier(t.Id, t.Floor, t.Equilibrium, t.Pullback)).ToList(),
            Conferences = new List<WorldConference>
            {
                new(1, "Synthetic Power", "SP", "power", 16, 0, new[] { "sat", "wed", "mon" }, 9, 4),
                new(2, "Synthetic Low", "SL", "low", 16, 0, new[] { "sat", "wed", "mon" }, 9, 11),
            },
            Schools = schools,
        };
    }

    private static bool ThrowsWorldError(WorldFile w, out string message)
    {
        try { ValidateWorld(w); message = "(validated — no throw)"; return false; }
        catch (InvalidOperationException ex) { message = ex.Message; return true; }
    }

    // The seeded band counts must equal the seeded apportionment — recomputed here from
    // a fresh WorldRng of the same seed, whose FIRST seven draws are exactly the
    // tie-break draws SeedWorld consumed (the fixed consumption-order contract).
    private static bool WorldBandCountsExact(WorldFile seeded, long seed)
    {
        var expected = WorldApportion(seeded.Schools.Count, new WorldRng(seed));
        for (var i = 0; i < WorldBands.Length; i++)
        {
            var got = seeded.Schools.Count(s =>
                s.CurrentPrestige >= WorldBands[i].Lo && s.CurrentPrestige <= WorldBands[i].Hi);
            if (got != expected[i]) return false;
        }
        return true;
    }

    private static bool WorldFloorsHonored(WorldFile w)
    {
        var tierById = w.Tiers.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var confById = w.Conferences.ToDictionary(c => c.Id);
        return w.Schools.All(s => s.CurrentPrestige >= tierById[confById[s.ConferenceId].TierId].Floor);
    }

    private static bool WorldTierMeansOrdered(WorldFile w, out string detail)
    {
        var confById = w.Conferences.ToDictionary(c => c.Id);
        var means = WorldTierDefaults
            .Select(t => w.Schools.Where(s => confById[s.ConferenceId].TierId == t.Id)
                          .Select(s => (double)s.CurrentPrestige).DefaultIfEmpty(double.NaN).Average())
            .ToArray();
        detail = string.Join(" > ", WorldTierDefaults.Select((t, i) => $"{t.Id}={means[i]:F1}"));
        for (var i = 1; i < means.Length; i++)
            if (!(means[i - 1] > means[i])) return false;
        return true;
    }
}
