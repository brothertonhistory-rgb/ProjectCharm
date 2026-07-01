using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Player generation — Pass 1: base generation.
//
// A HARNESS-ONLY instrument (no engine file changes). It turns a program's single
// prestige number into a coherent, varied ~10-man roster on the three-leg model
// (athleticism / skill / size), assembles two programs, prints a roster-inspection
// sheet, then seats each program's five designated STARTERS and reuses the lab
// bench's matchup + readout so contrasting rosters can be simmed.
//
// Dispatched from Program.cs by the `gen` token (mirrors `bench`); it returns before
// the validation suite, so it is never part of the default run.
//
//   Initial compile:  dotnet build src/Charm.Harness
//   Run (explicit):   dotnet run --no-build --project src/Charm.Harness -- gen path/to/gen.json
//   Run (bare):       dotnet run --no-build --project src/Charm.Harness -- gen
//                     (resolves "gen.json" from the current directory and prints the path)
//
// EVERY numeric constant below is the same named constant the Python oracle
// (gen_oracle.py, this session) was validated against. The oracle proved, over a few
// thousand generated rosters, the leg-count mix at prestige 90 vs 30, the FT floor
// reached (~26), coverage on every roster, and zero fatal-hole escapes. This code
// mirrors that oracle.
//
// PLACEHOLDER NOTE: the prestige -> leg-count curve is an adjustable spread generator,
// NOT a calibration target. Recruiting will eventually settle prestige -> roster shape
// organically; these numbers are a stand-in so varied rosters exist to sim now.
// ============================================================================

internal static partial class Program
{
    // ── The three universal bands (0-99) ───────────────────────────────────────
    private const int GenStrongLo = 70, GenStrongHi = 88;   // a leg you HAVE
    private const int GenOrdLo    = 44, GenOrdHi    = 58;   // a leg you LACK (never broken)
    private const int GenHoleLo   = 0,  GenHoleHi   = 30;   // only where position permits
    private const int GenStrongPrimaryLo = 78;              // role-primary sits at the top of Strong

    // ── SIZE is position-scaled (Height/Wingspan feed ABSOLUTE engine math) ─────
    // A "plus size" guard is big FOR A GUARD (~64 max), never a rim protector.
    private static (int pLo, int pHi, int oLo, int oHi) GenSizeBand(string pos) => pos switch
    {
        "G" => (52, 64, 40, 52),
        "W" => (63, 76, 52, 64),
        _   => (76, 90, 66, 78),   // "B"
    };
    private const int GenBigAthDownshift = 8;   // bigs skew below elite-guard burst (flagged, reversible)

    // ── FreeThrow: fixed-pivot tier-DECOUPLED shape, floor lowered to ~25 ────────
    // Nudges are measured against a FIXED pivot (50), NOT the tier center, so FT is
    // Outside-coupled (good shooters shoot better FTs) but not tier-coupled. Floor
    // dropped from the old 45 to 25 per brief §6(a); shape preserved.
    private const int    GenFtCenter         = 66;
    private const int    GenFtMin            = 25;
    private const int    GenFtMax            = 95;
    private const int    GenFtPivot          = 50;
    private const double GenFtOutsideNudgeMax = 10.0;
    private const double GenFtHeightNudgeMax  = 3.0;
    private const double GenFtHalf            = 32.0;

    // ── LEG -> RATING map (brief §3; FreeThrow drawn specially, not from a band) ─
    private static readonly string[] GenSizeRatings =
        { "Height", "Wingspan", "Weight", "OffensiveRebounding", "DefensiveRebounding" };
    private static readonly string[] GenAthRatings =
        { "Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance", "Hustle" };
    private static readonly string[] GenSkillRatings =
        { "Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
          "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
          "OffBallMovement", "Screening", "PerimeterDefense", "PostDefense",
          "RimProtection", "Steals", "HelpDefense", "OffBallDefense",
          "BasketballIQ", "Discipline" };
    private static readonly string[] GenTendencies =
        { "RimTendency", "ShortTendency", "MidTendency", "LongTendency", "ThreeTendency" };

    private static readonly Dictionary<string, string> GenLegOf = BuildLegOf();
    private static Dictionary<string, string> BuildLegOf()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in GenSizeRatings)  d[r] = "SIZE";
        foreach (var r in GenAthRatings)   d[r] = "ATH";
        foreach (var r in GenSkillRatings) d[r] = "SKILL";
        return d;
    }

    // ── POSITION permitted-hole sets (only these may reach the Hole band; §4/§6) ─
    private static readonly Dictionary<string, HashSet<string>> GenPermittedHoles = new()
    {
        // a guard/wing may lack the interior game entirely; never his perimeter game
        ["G"] = new(StringComparer.Ordinal)
                { "RimProtection", "PostDefense", "PostMoves", "Screening", "OffensiveRebounding" },
        ["W"] = new(StringComparer.Ordinal)
                { "RimProtection", "PostMoves", "Screening" },
        // a traditional big may have ~0 perimeter creation / shooting / perimeter D
        ["B"] = new(StringComparer.Ordinal)
                { "Outside", "Mid", "BallHandling", "SelfCreation", "Playmaking",
                  "Passing", "PerimeterDefense", "Steals", "OffBallMovement" },
    };

    // ── ROLES (reuse the NAMES only, never the old rating logic; A0.4) ──────────
    private sealed record GenRoleDef(string Pos, string[] Emphasis, int[] Tendencies);
    private static readonly Dictionary<string, GenRoleDef> GenRoles = new(StringComparer.Ordinal)
    {
        ["FloorGeneral"]     = new("G", new[] { "Playmaking", "Passing", "BallHandling", "BasketballIQ", "Discipline" },
                                   new[] { 18, 18, 20, 18, 26 }),
        ["PassFirstGuard"]   = new("G", new[] { "Passing", "Playmaking", "BallHandling", "OffBallMovement" },
                                   new[] { 20, 18, 20, 16, 26 }),
        ["PerimeterShooter"] = new("G", new[] { "Outside", "OffBallMovement", "Mid" },
                                   new[] { 8, 10, 18, 24, 40 }),
        ["Slasher"]          = new("G", new[] { "FirstStep", "Finishing", "BallHandling", "SelfCreation" },
                                   new[] { 34, 22, 20, 14, 10 }),
        ["ThreeAndDWing"]    = new("W", new[] { "Outside", "PerimeterDefense", "OffBallDefense", "HelpDefense" },
                                   new[] { 14, 12, 16, 20, 38 }),
        ["WingScorer"]       = new("W", new[] { "Mid", "Outside", "SelfCreation", "Finishing" },
                                   new[] { 22, 16, 22, 22, 18 }),
        ["PostScorer"]       = new("B", new[] { "PostMoves", "Close", "Finishing", "Strength" },
                                   new[] { 30, 30, 22, 12, 6 }),
        ["RimRunner"]        = new("B", new[] { "Finishing", "Screening", "OffensiveRebounding", "Vertical" },
                                   new[] { 55, 22, 12, 7, 4 }),
        ["AthleticBig"]      = new("B", new[] { "Finishing", "RimProtection", "DefensiveRebounding", "Vertical", "Strength" },
                                   new[] { 50, 24, 12, 8, 6 }),
    };
    private static readonly string[] GenGuardRoles = { "FloorGeneral", "PassFirstGuard", "PerimeterShooter", "Slasher" };
    private static readonly string[] GenWingRoles  = { "ThreeAndDWing", "WingScorer" };
    private static readonly string[] GenBigRoles   = { "PostScorer", "RimRunner", "AthleticBig" };
    private static readonly string[] GenLeadRoles  = { "FloorGeneral", "PassFirstGuard" };   // reserved lead handler
    private const string GenWingDefenderRole = "ThreeAndDWing";                               // reserved wing defender

    // ── PRESTIGE -> leg count (§6 placeholder; steeper depth gap for back slots) ─
    // per depth-slot 1..10 anchors: P(>=2 legs) at prestige 30 and 90
    private static readonly double[] GenP2At30 = { 0.62, 0.42, 0.28, 0.16, 0.10, 0.06, 0.03, 0.02, 0.01, 0.01 };
    private static readonly double[] GenP2At90 = { 0.96, 0.90, 0.83, 0.75, 0.66, 0.56, 0.47, 0.39, 0.31, 0.25 };
    // P(3 legs) — only the top slots, only high prestige ("sometimes a star, not every roster")
    private static readonly double[] GenP3At30 = { 0.01, 0.00, 0.00, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly double[] GenP3At90 = { 0.15, 0.06, 0.02, 0, 0, 0, 0, 0, 0, 0 };
    private const double GenFrac30 = 30 / 99.0;
    private const double GenFrac90 = 90 / 99.0;

    // ── Floors ──────────────────────────────────────────────────────────────────
    private const int GenLegHealthFloor = 40;   // a leg below this has collapsed toward the Hole band;
                                                // ENFORCED (lifted) so "no broken leg" is a guarantee.

    // ── Lean (variety knob; §5): athletic/skilled/big tilt a leg; high/low shift prestige
    private static readonly HashSet<string> GenValidLeans =
        new(StringComparer.Ordinal) { "none", "athletic", "skilled", "big", "high", "low" };
    private const int GenLeanTilt     = 10;   // team-wide additive tilt to the leaned leg
    private const int GenLeanPrestige = 15;   // effective-prestige shift for high/low

    // ── Draw helpers ─────────────────────────────────────────────────────────────
    private static int DrawStrongPrimary(Random r)   => r.Next(GenStrongPrimaryLo, GenStrongHi + 1);
    private static int DrawStrongSecondary(Random r) => r.Next(GenStrongLo, GenStrongPrimaryLo + 1);
    private static int DrawOrdinary(Random r)        => r.Next(GenOrdLo, GenOrdHi + 1);
    private static int DrawPermittedLow(Random r)    => r.Next(GenHoleLo, GenOrdHi + 1);  // reaches 0, rarely sits there

    private static int DrawFreeThrowGen(int outside, int height, Random r)
    {
        double outsideNudge =  ((outside - GenFtPivot) / 49.0) * GenFtOutsideNudgeMax;
        double heightNudge  = -((height  - GenFtPivot) / 49.0) * GenFtHeightNudgeMax;
        double center = GenFtCenter + outsideNudge + heightNudge;
        double sum = 0.0;
        for (var i = 0; i < 3; i++)
            sum += center + (r.NextDouble() * 2.0 - 1.0) * GenFtHalf;
        return Math.Max(GenFtMin, Math.Min(GenFtMax, (int)Math.Round(sum / 3.0)));
    }

    // ============================================================================
    // Config model + strict parser (mirrors the bench's tree-walk strictness: a
    // silent typo must never quietly ship a different roster than intended).
    // ============================================================================

    private sealed record GenProgram(int Prestige, string Lean);

    private sealed class GenConfig
    {
        public int GameCount { get; init; }
        public int BaseSeed  { get; init; }
        public int GenSeed   { get; init; }
        public GenProgram ProgramA { get; init; } = new(50, "none");
        public GenProgram ProgramB { get; init; } = new(50, "none");
    }

    private const int GenDefaultSeed = 20260701;

    private static GenConfig ParseGenConfig(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException jx)
        {
            throw new InvalidOperationException($"gen config is not valid JSON — {jx.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("gen config root must be a JSON object.");

            RejectUnknownOrDuplicateKeys(root, "root",
                "gameCount", "baseSeed", "genSeed", "programA", "programB");

            var gameCount = RequireIntProperty(root, "gameCount", "root");
            if (gameCount <= 0)
                throw new InvalidOperationException(
                    $"gameCount must be a positive integer (got {gameCount}); a zero-game run has nothing to report.");

            var baseSeed = RequireIntProperty(root, "baseSeed", "root");

            var genSeed = GenDefaultSeed;
            if (root.TryGetProperty("genSeed", out _))
                genSeed = RequireIntProperty(root, "genSeed", "root");

            var programA = ParseGenProgram(root, "programA");
            var programB = ParseGenProgram(root, "programB");

            return new GenConfig
            {
                GameCount = gameCount,
                BaseSeed  = baseSeed,
                GenSeed   = genSeed,
                ProgramA  = programA,
                ProgramB  = programB,
            };
        }
    }

    private static GenProgram ParseGenProgram(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            throw new InvalidOperationException($"'{name}' is required (a program is prestige + optional lean).");
        if (el.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"'{name}' must be an object.");

        RejectUnknownOrDuplicateKeys(el, name, "prestige", "lean");

        var prestige = RequireIntProperty(el, "prestige", name);
        if (prestige < 1 || prestige > 99)
            throw new InvalidOperationException($"{name}.prestige must be 1–99 (got {prestige}).");

        var lean = "none";
        if (el.TryGetProperty("lean", out var leanEl))
        {
            if (leanEl.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"{name}.lean must be a string.");
            lean = leanEl.GetString() ?? "none";
            if (!GenValidLeans.Contains(lean))
                throw new InvalidOperationException(
                    $"{name}.lean '{lean}' is unknown (allowed: {string.Join(", ", GenValidLeans)}; case-sensitive).");
        }

        return new GenProgram(prestige, lean);
    }

    // ============================================================================
    // Per-player generation
    // ============================================================================

    // Order legs turn "plus" as the leg count rises (brief §4b). Guards: skill floored,
    // athleticism scarce. Bigs: size floored, skill scarce. Wings: skill first, then
    // athleticism OR size 50/50 ("between").
    private static List<string> GenLegPriority(string pos, Random r)
    {
        if (pos == "G") return new List<string> { "SKILL", "ATH", "SIZE" };
        if (pos == "B") return new List<string> { "SIZE", "SKILL", "ATH" };
        var second = r.NextDouble() < 0.5 ? "ATH" : "SIZE";
        var third  = second == "ATH" ? "SIZE" : "ATH";
        return new List<string> { "SKILL", second, third };
    }

    private static double GenFracOf(int prestige) => prestige / 99.0;

    private static double GenInterp(double a30, double a90, double frac)
    {
        var t = (frac - GenFrac30) / (GenFrac90 - GenFrac30);
        return a30 + (a90 - a30) * t;
    }

    private static int GenLegCountFor(int slotIdx, int prestige, Random r)
    {
        var frac = GenFracOf(prestige);
        var p2 = Math.Max(0.0, Math.Min(1.0, GenInterp(GenP2At30[slotIdx], GenP2At90[slotIdx], frac)));
        var p3 = Math.Max(0.0, Math.Min(1.0, GenInterp(GenP3At30[slotIdx], GenP3At90[slotIdx], frac)));
        p3 = Math.Min(p3, p2);   // 3-leg is a subset of >=2-leg
        var u = r.NextDouble();
        if (u < p3) return 3;
        if (u < p2) return 2;
        return 1;
    }

    // Draws the 38 ratings for one player. Returns the value map and the set of plus legs.
    private static (Dictionary<string, int> Ratings, HashSet<string> PlusLegs) GenRatings(
        string role, string pos, int legCount, Random r)
    {
        var priority = GenLegPriority(pos, r);
        var plus  = new HashSet<string>(priority.Take(legCount), StringComparer.Ordinal);
        var emph  = new HashSet<string>(GenRoles[role].Emphasis, StringComparer.Ordinal);
        var holes = GenPermittedHoles[pos];
        var (pLo, pHi, oLo, oHi) = GenSizeBand(pos);

        var v = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in new[] { GenSizeRatings, GenAthRatings, GenSkillRatings })
        {
            foreach (var rt in group)
            {
                if (rt == "FreeThrow") continue;   // drawn last (needs Outside + Height)
                var leg = GenLegOf[rt];
                int val;
                if (leg == "SIZE")
                {
                    val = plus.Contains("SIZE") ? r.Next(pLo, pHi + 1) : r.Next(oLo, oHi + 1);
                }
                else if (plus.Contains(leg))
                {
                    val = emph.Contains(rt) ? DrawStrongPrimary(r) : DrawStrongSecondary(r);
                    if (leg == "ATH" && pos == "B") val = Math.Max(0, val - GenBigAthDownshift);
                }
                else   // ordinary leg
                {
                    if (emph.Contains(rt))       val = DrawOrdinary(r);      // role wants it, but leg is ordinary
                    else if (holes.Contains(rt)) val = DrawPermittedLow(r);  // reachable zero
                    else                         val = DrawOrdinary(r);
                    if (leg == "ATH" && pos == "B") val = Math.Max(0, val - GenBigAthDownshift);
                }
                v[rt] = val;
            }
        }

        // tendencies from the role
        var tend = GenRoles[role].Tendencies;
        for (var i = 0; i < 5; i++) v[GenTendencies[i]] = tend[i];

        // free throw (fixed-pivot, uses the drawn Outside + Height)
        v["FreeThrow"] = DrawFreeThrowGen(v["Outside"], v["Height"], r);

        return (v, plus);
    }

    // Team-wide variety tilt. high/low are handled via effective prestige, not here.
    private static void GenApplyLean(Dictionary<string, int> v, string lean)
    {
        string[]? leg = lean switch
        {
            "athletic" => GenAthRatings,
            "skilled"  => GenSkillRatings,
            "big"      => GenSizeRatings,
            _          => null,
        };
        if (leg is null) return;
        foreach (var rt in leg)
        {
            if (rt == "FreeThrow") continue;   // FT has its own draw; leave it out of the tilt
            v[rt] = Math.Min(99, v[rt] + GenLeanTilt);
        }
    }

    private static double GenDrivingAccess(Dictionary<string, int> v)
        => (v["FirstStep"] + v["Finishing"] + v["SelfCreation"]) / 3.0;

    // Position-required floors (§4d) — clamp up so the assertion below cannot fire.
    private static void GenEnforceFloors(Dictionary<string, int> v, string pos)
    {
        if (pos == "G")
        {
            v["BallHandling"]     = Math.Max(v["BallHandling"], 45);
            v["PerimeterDefense"] = Math.Max(v["PerimeterDefense"], 40);
            if (!(v["Outside"] >= 45 || GenDrivingAccess(v) >= 45))
                v["Outside"] = Math.Max(v["Outside"], 45);   // give him a jumper
        }
        else if (pos == "W")
        {
            v["PerimeterDefense"] = Math.Max(v["PerimeterDefense"], 42);
            if (!(v["Outside"] >= 45 || GenDrivingAccess(v) >= 45))
                v["Outside"] = Math.Max(v["Outside"], 45);
        }
        else   // big
        {
            v["OffensiveRebounding"] = Math.Max(v["OffensiveRebounding"], 45);
            v["DefensiveRebounding"] = Math.Max(v["DefensiveRebounding"], 45);
            if (!(v["PostMoves"] >= 45 || v["RimProtection"] >= 45))
                v["RimProtection"] = Math.Max(v["RimProtection"], 45);   // give him a rim presence
        }
    }

    private static string[] GenLegOwned(string leg)
        => leg == "SIZE" ? GenSizeRatings : leg == "ATH" ? GenAthRatings : GenSkillRatings;

    // Aggregate leg health EXCLUDING position-permitted holes (the §4d fix: a big's
    // permitted perimeter zeros must not count against his Skill leg).
    private static double GenLegMeanExHoles(Dictionary<string, int> v, string leg, HashSet<string> holes)
    {
        long sum = 0; int n = 0;
        foreach (var rt in GenLegOwned(leg))
        {
            if (holes.Contains(rt)) continue;
            sum += v[rt]; n++;
        }
        return n == 0 ? 99.0 : (double)sum / n;
    }

    // Guarantee no leg is broken: lift any sub-floor leg's non-hole owned ratings by a
    // constant so the aggregate clears the floor. Round UP so integer ratings clear it.
    private static void GenEnforceLegHealth(Dictionary<string, int> v, string pos)
    {
        var holes = GenPermittedHoles[pos];
        foreach (var leg in new[] { "SIZE", "ATH", "SKILL" })
        {
            var m = GenLegMeanExHoles(v, leg, holes);
            if (m < GenLegHealthFloor)
            {
                var delta = (int)Math.Ceiling(GenLegHealthFloor - m);
                foreach (var rt in GenLegOwned(leg))
                {
                    if (holes.Contains(rt)) continue;
                    v[rt] = Math.Min(99, v[rt] + delta);
                }
            }
        }
    }

    // Typed object initializer reading every field from the value map. Mirrors the
    // shape of BenchSpecToPlayer / StampPlayerId. PlayerId is intentionally NOT set —
    // it is stamped by logical team at the sim seam.
    private static Player GenMapToPlayer(Dictionary<string, int> v, string name) => new Player(name)
    {
        Close               = v["Close"],
        Mid                 = v["Mid"],
        Outside             = v["Outside"],
        Finishing           = v["Finishing"],
        FreeThrow           = v["FreeThrow"],
        FoulDrawing         = v["FoulDrawing"],
        RimTendency         = v["RimTendency"],
        ShortTendency       = v["ShortTendency"],
        MidTendency         = v["MidTendency"],
        LongTendency        = v["LongTendency"],
        ThreeTendency       = v["ThreeTendency"],
        BallHandling        = v["BallHandling"],
        Passing             = v["Passing"],
        Playmaking          = v["Playmaking"],
        SelfCreation        = v["SelfCreation"],
        PostMoves           = v["PostMoves"],
        OffBallMovement     = v["OffBallMovement"],
        Screening           = v["Screening"],
        OffensiveRebounding = v["OffensiveRebounding"],
        PerimeterDefense    = v["PerimeterDefense"],
        PostDefense         = v["PostDefense"],
        RimProtection       = v["RimProtection"],
        DefensiveRebounding = v["DefensiveRebounding"],
        Steals              = v["Steals"],
        HelpDefense         = v["HelpDefense"],
        OffBallDefense      = v["OffBallDefense"],
        Height              = v["Height"],
        Wingspan            = v["Wingspan"],
        Weight              = v["Weight"],
        Strength            = v["Strength"],
        Speed               = v["Speed"],
        Quickness           = v["Quickness"],
        FirstStep           = v["FirstStep"],
        Vertical            = v["Vertical"],
        Endurance           = v["Endurance"],
        Hustle              = v["Hustle"],
        BasketballIQ        = v["BasketballIQ"],
        Discipline          = v["Discipline"],
        // HierarchyRank left at its default (5) unless a role needs otherwise (A0.7).
    };

    // ============================================================================
    // Roster assembly (§5): coverage-first, ~4G/3W/3B, 5 starters + 5 bench, NO sort.
    // ============================================================================

    // One generated player + the metadata the roster sheet reads. Slot is the roster
    // depth position 1..10 (NOT PlayerId; A0.7): 1 = top starter, 10 = last bench.
    private sealed record GenPlayerRow(
        int Slot, string Pos, string Role, bool Starter, int LegCount,
        HashSet<string> PlusLegs, Dictionary<string, int> Ratings, Player Player);

    private static List<GenPlayerRow> GenRoster(int prestige, string lean, Random r, string programTag)
    {
        // high/low lean shifts the effective prestige (deeper / shallower rosters).
        var effPrestige = prestige;
        if (lean == "high")      effPrestige = Math.Min(99, prestige + GenLeanPrestige);
        else if (lean == "low")  effPrestige = Math.Max(1,  prestige - GenLeanPrestige);

        // Reserved coverage roles (starters): a lead handler, a wing defender, an
        // interior body — so a nonsense roster (six guards, no big) cannot slip through.
        var plan = new List<(string Pos, string Role, bool Starter)>
        {
            ("G", GenLeadRoles[r.Next(GenLeadRoles.Length)], true),   // lead handler
            ("W", GenWingDefenderRole,                       true),   // wing defender
            ("B", GenBigRoles[r.Next(GenBigRoles.Length)],   true),   // interior body
        };

        // Remaining composition to reach 4G/3W/3B: 3 more guards, 2 wings, 2 bigs.
        // First two of the remaining join the starting five; the other five are bench.
        var remaining = new List<string> { "G", "G", "G", "W", "W", "B", "B" };
        GenShuffle(remaining, r);
        for (var i = 0; i < remaining.Count; i++)
        {
            var pos = remaining[i];
            var pool = pos == "G" ? GenGuardRoles : pos == "W" ? GenWingRoles : GenBigRoles;
            var role = pool[r.Next(pool.Length)];
            plan.Add((pos, role, i < 2));
        }

        // Order: starters first (depth slots 1-5), bench (6-10). Depth drives leg count.
        // There is NO rating sort and NO "best five" — inventing an overall would smuggle
        // in the scalar the whole engine forbids. A coherent five is generated as such.
        var starters = plan.Where(p => p.Starter).ToList();
        var bench    = plan.Where(p => !p.Starter).ToList();
        var ordered  = starters.Concat(bench).ToList();

        var rows = new List<GenPlayerRow>();
        for (var depth = 0; depth < ordered.Count; depth++)
        {
            var (pos, role, starter) = ordered[depth];
            var lc = GenLegCountFor(depth, effPrestige, r);
            var (v, plusLegs) = GenRatings(role, pos, lc, r);
            GenApplyLean(v, lean);
            GenEnforceFloors(v, pos);
            GenEnforceLegHealth(v, pos);

            var player = GenMapToPlayer(v, $"Prog{programTag}_S{depth + 1}");

            // Post-construction assertion (mirrors the bench): the engine's own player
            // validation (0-99 ranges + nonzero tendency sum) must pass. A failure here
            // is a generation bug, caught before any sim.
            var errs = player.Validate();
            if (errs.Count > 0)
                throw new InvalidOperationException(
                    $"generation bug — Program {programTag} slot {depth + 1} ({role}) failed Player.Validate():\n  " +
                    string.Join("\n  ", errs));

            rows.Add(new GenPlayerRow(depth + 1, pos, role, starter, lc, plusLegs, v, player));
        }

        return rows;
    }

    private static void GenShuffle<T>(List<T> list, Random r)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = r.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ============================================================================
    // Roster-inspection sheet (the one genuinely new print) — the depth gap on the page.
    // ============================================================================

    private static void PrintRosterSheet(string tag, GenProgram program, List<GenPlayerRow> rows)
    {
        Console.WriteLine($"=== ROSTER SHEET: Program {tag}  (prestige {program.Prestige}, lean {program.Lean}) ===");
        Console.WriteLine("  Legs: + = a strength (plus leg), ~ = ordinary. Size/Ath/Skl = leg strength");
        Console.WriteLine("  (mean of that leg's ratings, excluding position-permitted holes). Slot = roster");
        Console.WriteLine("  depth 1..10 (1 = top starter); this is NOT PlayerId.");
        Console.WriteLine($"  {"Slot",-9}{"Pos",-4}{"Role",-16}{"Legs",-22}{"Size",5}{"Ath",5}{"Skl",5}{"FT",6}  Depth");
        Console.WriteLine("  " + new string('-', 78));

        foreach (var row in rows)
        {
            var holes = GenPermittedHoles[row.Pos];
            var sizeM = GenLegMeanExHoles(row.Ratings, "SIZE",  holes);
            var athM  = GenLegMeanExHoles(row.Ratings, "ATH",   holes);
            var sklM  = GenLegMeanExHoles(row.Ratings, "SKILL", holes);
            var legs = $"SIZE{GenSym(row, "SIZE")} ATH{GenSym(row, "ATH")} SKILL{GenSym(row, "SKILL")}";
            var depth = row.LegCount == 1 ? "one-leg" : row.LegCount == 2 ? "two-leg" : "three-leg";
            var slotLabel = $"[{tag}] {row.Slot}";
            var mark = row.Starter ? "STARTER" : "bench";
            Console.WriteLine(
                $"  {slotLabel,-9}{row.Pos,-4}{row.Role,-16}{legs,-22}" +
                $"{sizeM,5:F0}{athM,5:F0}{sklM,5:F0}{row.Ratings["FreeThrow"],6}  {depth,-9} {mark}");
        }
        Console.WriteLine();
    }

    private static string GenSym(GenPlayerRow row, string leg) => row.PlusLegs.Contains(leg) ? "+" : "~";

    // ============================================================================
    // Entry point (called from the Program.cs `gen` dispatch)
    // ============================================================================

    private static void RunGen(string engineConfigPath, string? genPathArg)
    {
        string genPath;
        if (!string.IsNullOrWhiteSpace(genPathArg))
        {
            genPath = Path.GetFullPath(genPathArg);
        }
        else
        {
            genPath = Path.GetFullPath("gen.json");
            Console.WriteLine("No gen path given; resolving 'gen.json' from the current directory:");
            Console.WriteLine($"  {genPath}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Project Charm :: Player Generation (Pass 1: base generation) ===");
        Console.WriteLine($"Gen config: {genPath}");
        Console.WriteLine();

        if (!File.Exists(genPath))
        {
            Console.WriteLine($"Gen config not found at: {genPath}");
            Console.WriteLine("Pass an explicit path, e.g.:");
            Console.WriteLine("  dotnet run --no-build --project src/Charm.Harness -- gen path/to/gen.json");
            return;
        }

        GenConfig config;
        try
        {
            config = ParseGenConfig(File.ReadAllText(genPath));
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("GEN CONFIG ERROR:");
            Console.WriteLine("  " + ex.Message);
            return;
        }

        Console.WriteLine($"Generation seed: {config.GenSeed}  (roster generation is reproducible from this)");
        Console.WriteLine();

        // Generate both programs' ~10-man rosters. A generation bug (an invalid player)
        // surfaces loudly here, before any sim.
        List<GenPlayerRow> rowsA, rowsB;
        try
        {
            var genRng = new Random(config.GenSeed);
            rowsA = GenRoster(config.ProgramA.Prestige, config.ProgramA.Lean, genRng, "A");
            rowsB = GenRoster(config.ProgramB.Prestige, config.ProgramB.Lean, genRng, "B");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("GEN BUILD ERROR:");
            Console.WriteLine("  " + ex.Message);
            return;
        }

        PrintRosterSheet("A", config.ProgramA, rowsA);
        PrintRosterSheet("B", config.ProgramB, rowsB);

        // Seat each program's five DESIGNATED STARTERS (exactly as generated — no
        // selection, no sort). Stamp the two cohorts into the bench attribution
        // namespace via StampPlayerId (A -> 1-5, B -> 6-10), creating game-facing stamped
        // copies; the generated players are never mutated and a roster-slot number is
        // never used as a PlayerId (A0.7). The five bench players are not seated.
        var startersA = rowsA.Where(x => x.Starter).Select(x => x.Player).ToArray();
        var startersB = rowsB.Where(x => x.Starter).Select(x => x.Player).ToArray();
        if (startersA.Length != 5 || startersB.Length != 5)
            throw new InvalidOperationException(
                $"assembly bug — each program must have exactly five starters " +
                $"(got A={startersA.Length}, B={startersB.Length}).");

        for (var i = 0; i < 5; i++) startersA[i] = StampPlayerId(startersA[i], i + 1);
        for (var i = 0; i < 5; i++) startersB[i] = StampPlayerId(startersB[i], i + 6);

        Console.WriteLine(
            $"Simming the two starter cohorts (A slots 1–5 vs B slots 1–5): " +
            $"{config.GameCount} games, base seed {config.BaseSeed} ...");
        Console.WriteLine();

        // Reuse the bench matchup + readout verbatim. RunBenchMatchup reads only
        // GameCount/BaseSeed from the config and takes the two player arrays as params,
        // so a minimal BenchConfig is all it needs. The [A]/[B] Slot 1–5 labels in the
        // bench channels + box score now denote just the starter cohort (distinct from
        // the ten-slot roster sheet above).
        var benchConfig = new BenchConfig { GameCount = config.GameCount, BaseSeed = config.BaseSeed };
        var stats = RunBenchMatchup(benchConfig, startersA, startersB, engineConfigPath);

        PrintBenchChannels(stats, startersA, startersB);
        PrintBenchBoxScore(stats);
    }
}
