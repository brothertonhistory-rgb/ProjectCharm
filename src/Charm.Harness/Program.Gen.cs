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
    // The role tendency table is RETIRED (this session): tendencies are no longer
    // authored per role — they are DERIVED from each player's own final ratings by
    // DeriveTendencies below (ported from tools/tendency_oracle.py, LOCKED SPEC
    // 2026-07-04). Roles keep only their position and skill emphasis.
    private sealed record GenRoleDef(string Pos, string[] Emphasis);
    private static readonly Dictionary<string, GenRoleDef> GenRoles = new(StringComparer.Ordinal)
    {
        ["FloorGeneral"]     = new("G", new[] { "Playmaking", "Passing", "BallHandling", "BasketballIQ", "Discipline" }),
        ["PassFirstGuard"]   = new("G", new[] { "Passing", "Playmaking", "BallHandling", "OffBallMovement" }),
        ["PerimeterShooter"] = new("G", new[] { "Outside", "OffBallMovement", "Mid" }),
        ["Slasher"]          = new("G", new[] { "FirstStep", "Finishing", "BallHandling", "SelfCreation" }),
        ["ThreeAndDWing"]    = new("W", new[] { "Outside", "PerimeterDefense", "OffBallDefense", "HelpDefense" }),
        ["WingScorer"]       = new("W", new[] { "Mid", "Outside", "SelfCreation", "Finishing" }),
        ["PostScorer"]       = new("B", new[] { "PostMoves", "Close", "Finishing", "Strength" }),
        ["RimRunner"]        = new("B", new[] { "Finishing", "Screening", "OffensiveRebounding", "Vertical" }),
        ["AthleticBig"]      = new("B", new[] { "Finishing", "RimProtection", "DefensiveRebounding", "Vertical", "Strength" }),
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

        // tendencies are NOT written here: GenRatings produces a half-finished
        // player (lean, floors, leg health, third-leg redraw still to run).
        // DeriveAndStampTendencies runs at both pipeline tails, after the last
        // rating mutator and before GenMapToPlayer.

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
    // The optional floor is the Session 29 seam: the divvy's pool path enforces a
    // lower floor (a leg can be bad, never broken — DivvyLegHealthFloor = 20) so the
    // scarce gradient band ships as drawn; every pre-existing call site omits the
    // argument and keeps the original 40 unchanged.
    private static void GenEnforceLegHealth(Dictionary<string, int> v, string pos, int floor = GenLegHealthFloor)
    {
        var holes = GenPermittedHoles[pos];
        foreach (var leg in new[] { "SIZE", "ATH", "SKILL" })
        {
            var m = GenLegMeanExHoles(v, leg, holes);
            if (m < floor)
            {
                var delta = (int)Math.Ceiling(floor - m);
                foreach (var rt in GenLegOwned(leg))
                {
                    if (holes.Contains(rt)) continue;
                    v[rt] = Math.Min(99, v[rt] + delta);
                }
            }
        }
    }

    // ============================================================================
    // Skill-derived shot tendencies — C# port of tools/tendency_oracle.py
    // (LOCKED SPEC ORACLE v2, 2026-07-04 — the modern-era retune: compressed
    // perimeter three frequency, universal capable floor, the era profile).
    // The oracle is authoritative: stage-for-stage,
    // constant-for-constant. If this port and the oracle ever disagree, the oracle
    // wins; future tuning happens in the oracle first (new approval), never here.
    //
    // DETERMINISM RULING (locked): DeriveTendencies is a PURE function of the final
    // rating map. No player-style seed, no manufactured tendency noise. Identical
    // final ratings yield identical integer diets; population variety comes from
    // varied drawn skills, and shot VOLUME differences are usage/hierarchy's job.
    //
    // Zone order everywhere: Rim, Short, Mid, Long, Three (== GenTendencies[]).
    // All tie-breaks (largest-remainder rounding, the 99-cap redistribution)
    // resolve in that fixed zone order.
    // ============================================================================

    // ---- v2 constants (oracle names + comments, verbatim) ----
    private const double TendCreationLo = 45, TendCreationHi = 78;   // what "having a creation game" means
    private const double TendMidCredLo = 44, TendMidCredHi = 62;     // a mid jumper is a real shot above here (catch-&-shoot credible)
    // THREE — v3 (S65 reversal): volume follows the rating. Two rules blended by
    // perimeter-ness; each rule's FLOOR half lives in the opportunity floor (share-
    // pinned, companion-independent), its RAW half here (the skill-responsive climb).
    private const double TendThreePerimMain = 38.0;                       // the shooter's climb: main ramp height
    private const double TendThreePerimMainLo = 34, TendThreePerimMainHi = 54; // where the three becomes HIS shot, not just in-the-flow
    private const double TendThreePerimTop = 26.0;                        // the elite extension on top of the main ramp
    private const double TendThreePerimTopLo = 50, TendThreePerimTopHi = 88;
    private const double TendThreeStretch = 46.0;                         // interior stretch ramp height (real arc volume)
    private const double TendThreeStretchLo = 38, TendThreeStretchHi = 63; // a big earns real arc volume only through here
    private const double TendRimFedW = 0.72, TendRimCreateW = 0.60;  // rim = fed finish + self-created downhill (downhill is primary for creators)
    private const double TendFloaterScale = 0.55;                    // the floater is a secondary/counter shot, below the rim it replaces
    private const double TendPostTouchLo = 55, TendPostTouchHi = 85; // a post touch needs a REAL post game, not ordinary PostMoves
    private const double TendLongGuardCap = 46.0;                    // max raw long from the star pull-up path
    private const double TendLongStretchCap = 46.0;                  // max raw long from the stretch-pop path (lower bar, modest size)
    private const double TendGuardCreateLo = 66, TendGuardCreateHi = 86;   // dominant-only: both factors must be near-elite
    private const double TendGuardPullupLo = 64, TendGuardPullupHi = 86;
    private const double TendStretchPlausLo = 58, TendStretchPlausHi = 80; // frontcourt body plausibility (NO shooting in here)
    private const double TendStretchCredLo = 48, TendStretchCredHi = 74;   // catch-&-shoot credibility (ALL shooting in here); lower bar
    private const double TendGammaBase = 1.10, TendGammaShape = 2.30, TendGammaDeficit = 1.90;
    private const double TendCredibleCeiling = 85.0;                 // a top-2 average at/above this earns full flatness
    private const double TendMarginBleed = 0.07;                     // porousness of the zone walls: each zone spills this
                                                                     // fraction of the gap into its distance-ladder neighbors
                                                                     // (foot on the line, bumped off the rim, chased off the arc)
    private const double TendFloorInside = 0.025;                    // the layup, floater, wide-open 12-footer basketball hands everyone
    private const double TendFloorLongPerim = 0.030;                 // a perimeter player pulls up from midrange a few times a year
    // The three floor — v3: a p-blend of the two rules' own floors (mirrors the raw
    // path's blend so each rule lives on its own side of BOTH stages):
    private const double TendFloorThreeEmergency = 0.012;            // interior, any Outside>0: the season's couple of desperation heaves
    private const double TendFloorThreeAllow = 0.055;                // the big's rare wide-open allowance (the threshold's small opening)...
    private const double TendFloorThreeAllowLo = 26, TendFloorThreeAllowHi = 40; // ...opening as Outside crosses ~30
    private const double TendFloorThreePerimBase = 0.055;            // everyone out there shoots his ~20 a season — no perimeter zero
    private const double TendFloorThreePerimRamp = 0.115;            // in-the-flow volume grows with credibility below the shooter threshold
    private const double TendFloorThreePerimRampLo = 14, TendFloorThreePerimRampHi = 46;

    // THE ERA PROFILE (v2 ruling 3): weight-space multipliers applied AFTER peakedness,
    // Rim/Short/Mid/Long/Three. Encodes the modern shot-selection culture, cleanly
    // separated from individual capability. An earlier-era league is these 5 numbers.
    private static readonly double[] TendEraProfile = { 1.00, 0.66, 0.44, 0.63, 2.44 };

    // The 13 rating-map inputs the derivation reads. A missing key throws loudly
    // (KeyNotFoundException) — never a silent default.
    private static readonly string[] TendInputs =
        { "Outside", "Mid", "Close", "Finishing", "PostMoves", "BallHandling",
          "SelfCreation", "FirstStep", "Speed", "Vertical", "Screening",
          "Height", "Weight" };

    private static double TendClamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;
    private static double TendGate(double x, double lo, double hi) => TendClamp((x - lo) / (hi - lo), 0.0, 1.0);

    // perimeter_ness: how perimeter-shaped a player is, 0..1 — small OR a real handle
    // qualifies. Shared by the three signal (path blend) and the opportunity floor.
    private static double TendPerimeterNess(Dictionary<string, int> a) =>
        TendClamp(Math.Max(1 - TendGate(a["Height"], 68, 79), TendGate(a["BallHandling"], 45, 70)), 0, 1);

    // raw_signals: dict of 0-99 attributes -> five raw per-zone capability signals (0-99)
    private static double[] TendRawSignals(Dictionary<string, int> a)
    {
        var creation = TendGate(0.50 * a["SelfCreation"] + 0.30 * a["BallHandling"] + 0.20 * a["FirstStep"],
                                TendCreationLo, TendCreationHi);
        var burst = (a["FirstStep"] + a["Speed"] + a["Vertical"]) / 3.0;

        // RIM: fed finish (no creation needed) + self-created downhill (creation-gated, primary for creators)
        double fedRim = a["Finishing"];
        var createRim = (0.55 * burst + 0.45 * a["BallHandling"]) * creation;
        var rRim = TendClamp(TendRimFedW * fedRim + TendRimCreateW * createRim, 0, 99);

        // MID: a real shot only if the jumper is credible (catch-&-shoot) OR he can create the pull-up
        var midAccess = TendClamp(TendGate(a["Mid"], TendMidCredLo, TendMidCredHi) + 0.70 * creation, 0, 1);
        var rMid = a["Mid"] * midAccess;

        // THREE (v3, the S65 reversal): volume follows the rating. Two ramps blended by
        // perimeter-ness — both are ZERO below their thresholds ON PURPOSE: a raw signal
        // here competes through gamma/era against the player's other signals, so any
        // nonzero raw handed to a below-threshold player becomes three-dominance for a
        // player with nothing else (the retired v2 paradox). Below-threshold volume is
        // the opportunity floor's job (share-pinned, companion-independent).
        //   Perimeter: the shooter's climb — main ramp plus an elite extension.
        //   Interior: the stretch ramp — a big earns arc volume only through a real rating.
        var p = TendPerimeterNess(a);
        var perimThree = TendThreePerimMain * TendGate(a["Outside"], TendThreePerimMainLo, TendThreePerimMainHi)
                         + TendThreePerimTop * TendGate(a["Outside"], TendThreePerimTopLo, TendThreePerimTopHi);
        var interThree = TendThreeStretch * TendGate(a["Outside"], TendThreeStretchLo, TendThreeStretchHi);
        var rThree = TendClamp(p * perimThree + (1 - p) * interThree, 0, 99);

        // SHORT: two routes that STACK (each earns its own volume), each near-zero without its real skill
        var postTouch = TendGate(a["PostMoves"], TendPostTouchLo, TendPostTouchHi)
                        * (0.70 * a["PostMoves"] + 0.30 * a["Close"]);
        var floater = (0.45 * Math.Max(a["Close"], a["Finishing"]) + 0.30 * a["BallHandling"]
                       + 0.25 * Math.Max(a["SelfCreation"], a["FirstStep"])) * creation * TendFloaterScale;
        var rShort = TendClamp(postTouch + floater, 0, 99);

        // LONG: two independent capped gated paths
        var creationStyle = 0.7 * a["SelfCreation"] + 0.3 * a["BallHandling"];
        var pullupShooting = 0.7 * a["Mid"] + 0.3 * a["Outside"];
        var gGuard = TendGate(creationStyle, TendGuardCreateLo, TendGuardCreateHi)
                     * TendGate(pullupShooting, TendGuardPullupLo, TendGuardPullupHi);
        var guardLong = TendLongGuardCap * gGuard;

        var plaus = 0.55 * a["Height"] + 0.20 * a["Weight"] + 0.15 * a["Screening"] + 0.10 * a["PostMoves"]; // NO shooting
        var cred = 0.7 * a["Mid"] + 0.3 * a["Outside"];                                                       // ALL shooting
        var gStretch = TendGate(plaus, TendStretchPlausLo, TendStretchPlausHi)
                       * TendGate(cred, TendStretchCredLo, TendStretchCredHi);
        var stretchLong = TendLongStretchCap * gStretch;

        var rLong = TendClamp(guardLong + stretchLong, 0, 99);

        return new[] { rRim, rShort, rMid, rLong, rThree };
    }

    // peakedness_gamma: two inputs — lopsided shape AND absolute capability. Both push spikier.
    private static double TendPeakednessGamma(double[] r)
    {
        var rmax = r.Max();
        var rmean = r.Sum() / r.Length;
        var lop = rmax == 0 ? 0.0 : (rmax - rmean) / rmax;                       // relative shape
        var top2 = r.OrderByDescending(x => x).Take(2).Sum() / 2.0;
        var defic = TendClamp(1 - top2 / TendCredibleCeiling, 0, 1);             // absolute capability deficit
        return TendClamp(TendGammaBase + TendGammaShape * lop + TendGammaDeficit * defic, 1.0, 6.0);
    }

    // bleed_margins: the zone walls are porous. Each zone spills TendMarginBleed of the
    // gap to its neighbors on the distance ladder Rim-Short-Mid-Long-Three. Conserves
    // the total; shaves impossible peaks; fills the in-between shots a clean diet drops to zero.
    private static double[] TendBleedMargins(double[] w)
    {
        var s = w.Sum();
        if (s <= 0) return w;
        var d = w.Select(x => x / s).ToArray();
        int[][] nbr = { new[] { 1 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 2, 4 }, new[] { 3 } }; // rim, short, mid, long, three
        var outW = new double[5];
        for (var i = 0; i < 5; i++)
            outW[i] = d[i] + TendMarginBleed * nbr[i].Sum(j => d[j] - d[i]);
        return outW;
    }

    // opportunity_floor: no zone a player can plausibly reach is ever exactly zero.
    // Inside shots are handed to everyone. The three (v3): a p-blend of the two rules'
    // own floors, mirroring the raw path's blend so neither rule contaminates the other —
    //   INTERIOR floor = emergency heaves (any Outside>0, a couple all season) + the rare
    //     wide-open ALLOWANCE that opens as Outside crosses ~30 (the threshold's small
    //     opening; the raw stretch ramp takes over above it);
    //   PERIMETER floor = the structural in-the-flow volume (kick-outs, rhythm — everyone
    //     out there shoots some), growing modestly with credibility below the shooter
    //     threshold. Share-pinned HERE, not in the raw path, so it is companion-
    //     independent: a skill-less perimeter player reads the floor, never dominance.
    // Only a literal Outside==0 INTERIOR player reads a zero three tendency (that
    // residual ~0.2% buzzer heave remains Roll G's, at pie time, if ever needed); a
    // perimeter player never reads zero (v3 ruling 1).
    private static double[] TendOpportunityFloor(double[] w, Dictionary<string, int> a)
    {
        var s = w.Sum();
        var d = s > 0 ? w.Select(x => x / s).ToArray() : (double[])w.Clone();
        var perim = TendPerimeterNess(a);
        var capable = a["Outside"] > 0 ? 1.0 : 0.0;
        var interiorFloor = TendFloorThreeEmergency * capable
                            + TendFloorThreeAllow * TendGate(a["Outside"], TendFloorThreeAllowLo, TendFloorThreeAllowHi);
        var perimFloor = TendFloorThreePerimBase
                         + TendFloorThreePerimRamp * TendGate(a["Outside"], TendFloorThreePerimRampLo, TendFloorThreePerimRampHi);
        var floors = new[] { TendFloorInside, TendFloorInside, TendFloorInside,
                             TendFloorLongPerim * perim,
                             perim * perimFloor + (1 - perim) * interiorFloor };
        var outW = new double[5];
        for (var i = 0; i < 5; i++) outW[i] = Math.Max(d[i], floors[i]);
        return outW;
    }

    // to_int_diet: normalize to ints summing to 100, each <= 99 (Player.Validate ceiling).
    // DETERMINISTIC TIE-BREAKS, locked to the oracle:
    //   - largest-remainder rounding: remainders sorted descending; equal remainders
    //     resolve in zone order (Rim, Short, Mid, Long, Three);
    //   - 99-cap redistribution: overflow moves to the smallest zone; equal smallest
    //     resolves to the earliest zone in the same fixed order.
    private static int[] TendToIntDiet(double[] weights)
    {
        var w = weights;
        var s = w.Sum();
        if (s <= 0) { w = new[] { 1.0, 1.0, 1.0, 1.0, 1.0 }; s = w.Length; }
        var raw = w.Select(x => 100 * x / s).ToArray();
        var floor = raw.Select(x => (int)x).ToArray();
        var rem = 100 - floor.Sum();
        var order = Enumerable.Range(0, raw.Length)
            .OrderByDescending(i => raw[i] - floor[i])
            .ThenBy(i => i)
            .ToArray();
        for (var k = 0; k < rem; k++) floor[order[k]] += 1;
        // ceiling guard: no single zone may be 100
        for (var i = 0; i < floor.Length; i++)
        {
            if (floor[i] >= 100)
            {
                var j = 0;
                for (var k = 1; k < floor.Length; k++)
                    if (floor[k] < floor[j]) j = k;   // smallest value; ties keep the earliest zone
                floor[i] -= 1; floor[j] += 1;
            }
        }
        return floor;
    }

    // The PURE derivation (what golden parity calls): final rating map -> five ints
    // in GenTendencies order (Rim, Short, Mid, Long, Three), sum 100, each in [0,99].
    private static int[] DeriveTendencies(Dictionary<string, int> v)
    {
        var r = TendRawSignals(v);
        var g = TendPeakednessGamma(r);
        var w = new double[5];
        for (var i = 0; i < 5; i++)
            w[i] = Math.Pow(r[i], g) * TendEraProfile[i];   // v2 ruling 3: the era stage
        w = TendBleedMargins(w);
        w = TendOpportunityFloor(w, v);
        return TendToIntDiet(w);
    }

    // Thin wrapper for the two generation pipelines: derive, then stamp the five
    // results into the value map under the GenTendencies keys. Runs AFTER the last
    // rating mutator (lean / floors / leg health / third-leg redraw) and before
    // GenMapToPlayer — deriving earlier would stamp a diet from a half-finished player.
    private static void DeriveAndStampTendencies(Dictionary<string, int> v)
    {
        var diet = DeriveTendencies(v);
        for (var i = 0; i < 5; i++) v[GenTendencies[i]] = diet[i];
    }

    // ============================================================================
    // Golden-vector parity — the port proof, stage-wise. Loads tools/tendency_golden.json
    // (copied beside the binary, the Phase 53 convention), validates the fixture
    // CONTRACT first (so a stale or malformed file is rejected loudly instead of
    // silently testing the wrong thing) — including the per-stage trace every vector
    // must now carry — then requires every C# stage to match the oracle's trace
    // (intermediate doubles at tight relative tolerance; Python ** and Math.Pow may
    // differ by ULPs) and every final diet to equal the oracle diet EXACTLY,
    // element-for-element in zone order. Two different implementations can round to
    // the same 5 ints; the trace proves the PIPELINE, not just the integers. Runs at
    // the start of RunGen, before either roster is generated. Seed-independent by
    // construction.
    // ============================================================================
    private static void RunTendencyGoldenParity()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tools", "tendency_golden.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"golden parity fixture not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // ── fixture contract ────────────────────────────────────────────────────
        string[] expectedZones = { "Rim", "Short", "Mid", "Long", "Three" };
        if (!root.TryGetProperty("zoneOrder", out var zo) || zo.GetArrayLength() != 5)
            throw new InvalidOperationException("golden fixture rejected: missing/short zoneOrder.");
        for (var i = 0; i < 5; i++)
            if (zo[i].GetString() != expectedZones[i])
                throw new InvalidOperationException(
                    $"golden fixture rejected: zoneOrder[{i}] is '{zo[i].GetString()}', expected '{expectedZones[i]}'. " +
                    "The fixture does not match the locked contract (GenTendencies order).");

        if (!root.TryGetProperty("vectors", out var vectors) || vectors.GetArrayLength() == 0)
            throw new InvalidOperationException("golden fixture rejected: no vectors.");

        // Cross-language float parity holds at tolerance, not equality:
        // |a-b| <= max(1e-9 * max(|a|,|b|), 1e-12) — Python ** and Math.Pow may differ
        // by ULPs. Final integers compare EXACTLY.
        static bool TendNear(double x, double y) =>
            Math.Abs(x - y) <= Math.Max(1e-9 * Math.Max(Math.Abs(x), Math.Abs(y)), 1e-12);
        void AssertStage(string vecName, string stage, double[] act, double[] exp)
        {
            for (var i = 0; i < 5; i++)
                if (!TendNear(act[i], exp[i]))
                    throw new InvalidOperationException(
                        $"GOLDEN PARITY FAILURE — vector '{vecName}', stage {stage}, index {i} ({expectedZones[i]}):\n" +
                        $"  expected  {exp[i]:R}\n" +
                        $"  actual    {act[i]:R}\n" +
                        "The C# port disagrees with the locked oracle at this stage. The oracle wins — fix the port.");
        }

        var run = 0;
        foreach (var vec in vectors.EnumerateArray())
        {
            var name = vec.GetProperty("name").GetString() ?? "(unnamed)";
            var ratingsEl = vec.GetProperty("ratings");
            var ratings = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in ratingsEl.EnumerateObject()) ratings[p.Name] = p.Value.GetInt32();
            foreach (var key in TendInputs)
                if (!ratings.ContainsKey(key))
                    throw new InvalidOperationException(
                        $"golden fixture rejected: vector '{name}' lacks derivation input '{key}'.");

            var expEl = vec.GetProperty("expected");
            if (expEl.GetArrayLength() != 5)
                throw new InvalidOperationException(
                    $"golden fixture rejected: vector '{name}' expected diet is not length five.");
            var expected = new int[5];
            var sum = 0;
            for (var i = 0; i < 5; i++)
            {
                expected[i] = expEl[i].GetInt32();
                if (expected[i] < 0 || expected[i] > 99)
                    throw new InvalidOperationException(
                        $"golden fixture rejected: vector '{name}' expected[{i}]={expected[i]} outside [0,99].");
                sum += expected[i];
            }
            if (sum != 100)
                throw new InvalidOperationException(
                    $"golden fixture rejected: vector '{name}' expected diet sums to {sum}, not 100.");

            // ── trace contract: every vector must carry the five per-stage fields ───
            if (!vec.TryGetProperty("trace", out var traceEl))
                throw new InvalidOperationException(
                    $"golden fixture rejected: vector '{name}' has no trace. " +
                    "Stage-wise parity requires the v2 fixture (re-run tools/tendency_oracle.py).");
            string[] traceVec = { "rawSignals", "postEraWeights", "postBleedWeights", "postFloorWeights" };
            var trace = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var field in traceVec)
            {
                if (!traceEl.TryGetProperty(field, out var fe) || fe.GetArrayLength() != 5)
                    throw new InvalidOperationException(
                        $"golden fixture rejected: vector '{name}' trace.{field} missing or not length five.");
                var vals = new double[5];
                for (var i = 0; i < 5; i++) vals[i] = fe[i].GetDouble();
                trace[field] = vals;
            }
            if (!traceEl.TryGetProperty("gamma", out var gammaEl))
                throw new InvalidOperationException(
                    $"golden fixture rejected: vector '{name}' trace.gamma missing.");
            var traceGamma = gammaEl.GetDouble();

            // ── the stage-wise parity asserts ────────────────────────────────────
            var r = TendRawSignals(ratings);
            AssertStage(name, "rawSignals", r, trace["rawSignals"]);

            var g = TendPeakednessGamma(r);
            if (!TendNear(g, traceGamma))
                throw new InvalidOperationException(
                    $"GOLDEN PARITY FAILURE — vector '{name}', stage gamma:\n" +
                    $"  expected  {traceGamma:R}\n" +
                    $"  actual    {g:R}\n" +
                    "The C# port disagrees with the locked oracle at this stage. The oracle wins — fix the port.");

            var wEra = new double[5];
            for (var i = 0; i < 5; i++)
                wEra[i] = Math.Pow(r[i], g) * TendEraProfile[i];
            AssertStage(name, "postEraWeights", wEra, trace["postEraWeights"]);

            var wBleed = TendBleedMargins(wEra);
            AssertStage(name, "postBleedWeights", wBleed, trace["postBleedWeights"]);

            var wFloor = TendOpportunityFloor(wBleed, ratings);
            AssertStage(name, "postFloorWeights", wFloor, trace["postFloorWeights"]);

            // final integers — through the real production entry point, compared EXACTLY
            var actual = DeriveTendencies(ratings);
            for (var i = 0; i < 5; i++)
            {
                if (actual[i] != expected[i])
                    throw new InvalidOperationException(
                        $"GOLDEN PARITY FAILURE — vector '{name}', stage finalDiet, index {i} ({expectedZones[i]}):\n" +
                        $"  expected  [{string.Join(", ", expected)}]\n" +
                        $"  actual    [{string.Join(", ", actual)}]\n" +
                        "The C# port disagrees with the locked oracle. The oracle wins — fix the port.");
            }
            run++;
        }
        Console.WriteLine($"golden tendency parity: {run} vectors, all stages within tolerance, all diets exact. (oracle: tools/tendency_oracle.py, LOCKED SPEC ORACLE v2, 2026-07-04)");
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
            DeriveAndStampTendencies(v);   // AFTER all rating mutation, BEFORE mapping

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
        // Port proof first: golden-vector parity against the locked oracle fixture,
        // before either roster is generated. Seed-independent; throws on any mismatch.
        RunTendencyGoldenParity();
        RunGenPass2ReplayParity();   // S43: Pass-2 generation math vs the S42.2 replay fixture

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
        // Phase 52: stamp ALL TEN of each program (not just the five starters) into the
        // gen attribution namespace. Roster depth slot 1..10 maps directly to PlayerId —
        // program A → 1..10, program B → 11..20 — so PlayerIds are stable per logical
        // player regardless of which physical side they take each game. The depth slot is
        // the stamping key; it is not reused as a PlayerId anywhere else (A0.7). Starters
        // (depth 1..5) seat into on-floor slots 1..5 in depth order; the five reserves
        // (depth 6..10) begin on the bench and check in only through the fatigue fence.
        if (rowsA.Count != 10 || rowsB.Count != 10)
            throw new InvalidOperationException(
                $"assembly bug — each program must have exactly ten players " +
                $"(got A={rowsA.Count}, B={rowsB.Count}).");

        var stampedA = new Player[10];
        var stampedB = new Player[10];
        for (var i = 0; i < 10; i++) stampedA[i] = StampPlayerId(rowsA[i].Player, rowsA[i].Slot);
        for (var i = 0; i < 10; i++) stampedB[i] = StampPlayerId(rowsB[i].Player, rowsB[i].Slot + 10);

        var sideA    = BuildGenSideData(rowsA, stampedA);
        var sideB    = BuildGenSideData(rowsB, stampedB);
        var identity = BuildGenIdentity(rowsA, rowsB);

        Console.WriteLine(
            $"Simming the two full ten-man rosters — five starters in slots 1–5, five reserves " +
            $"on the bench behind the fatigue fence: {config.GameCount} games, base seed {config.BaseSeed} ...");
        Console.WriteLine();

        var stats = RunGenMatchup(config, stampedA, stampedB, sideA, sideB, engineConfigPath);

        PrintGenChannels(stats);
        PrintGenBoxScore(stats, identity);
    }

    // ── Ten-man assembly for the gen matchup ─────────────────────────────────────

    // One logical program's depth data: the five starters (in on-floor slot order 1..5)
    // with their positions, and the five reserves with theirs. The stamped Player at index
    // i corresponds to rows[i] (depth slot i+1), so starters land in depth order.
    private sealed record GenSideData(
        Player[] Starters, string[] StarterPositions,
        Player[] Reserves, string[] ReservePositions);

    private static GenSideData BuildGenSideData(List<GenPlayerRow> rows, Player[] stamped)
    {
        var starters = new List<Player>();  var starterPos = new List<string>();
        var reserves = new List<Player>();  var reservePos = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Starter) { starters.Add(stamped[i]); starterPos.Add(rows[i].Pos); }
            else                 { reserves.Add(stamped[i]); reservePos.Add(rows[i].Pos); }
        }
        if (starters.Count != 5 || reserves.Count != 5)
            throw new InvalidOperationException(
                $"assembly bug — a program must split into five starters and five reserves " +
                $"(got {starters.Count} starters, {reserves.Count} reserves).");
        return new GenSideData(starters.ToArray(), starterPos.ToArray(), reserves.ToArray(), reservePos.ToArray());
    }

    // PlayerId → who this is, for the box-score row labels. A → depth slot (1..10),
    // B → depth slot + 10 (11..20).
    private sealed record GenIdentity(string Team, int Slot, string Pos, string Role, bool Starter);

    private static Dictionary<int, GenIdentity> BuildGenIdentity(List<GenPlayerRow> rowsA, List<GenPlayerRow> rowsB)
    {
        var map = new Dictionary<int, GenIdentity>();
        foreach (var row in rowsA) map[row.Slot]      = new GenIdentity("A", row.Slot, row.Pos, row.Role, row.Starter);
        foreach (var row in rowsB) map[row.Slot + 10] = new GenIdentity("B", row.Slot, row.Pos, row.Role, row.Starter);
        return map;
    }

    // ── The gen matchup runner ───────────────────────────────────────────────────
    //
    // A sibling of RunBenchMatchup, forked rather than widened so the five-a-side bench
    // instrument (and every byte-for-byte suite path that shares its accumulator/printer)
    // is left literally untouched. This runner seats the five starters, installs the
    // fatigue-fence policy with the correct logical→physical side mapping for the game, and
    // constructs the Governor WITH that policy. Attribution flows through the shared
    // AttributeGame (now 20-wide); a GenStats accumulates both the team-level channels and
    // the 20-player box score plus a possessions-played count.
    private static GenStats RunGenMatchup(
        GenConfig config, Player[] stampedA, Player[] stampedB,
        GenSideData sideA, GenSideData sideB, string engineConfigPath)
    {
        // Session 30 extraction: configs loaded ONCE per call (exactly as before —
        // same Load calls, same order), then the loop delegates each game to the
        // shared single-game body. One construction site, two callers (gen/smoke
        // here, the season runner in Program.Season.cs), zero drift.
        var c = LoadGenEngineConfigs(engineConfigPath);

        var stats = new GenStats();

        for (var i = 0; i < config.GameCount; i++)
        {
            int gameSeed = config.BaseSeed + i;

            // Same deterministic side balancing as the bench (D4): logical A is Home on
            // even indices, Away on odd. Any home/away asymmetry splits evenly across A/B.
            bool teamAIsHome = (i % 2 == 0);
            TeamSide teamASide = teamAIsHome ? TeamSide.Home : TeamSide.Away;
            TeamSide teamBSide = teamAIsHome ? TeamSide.Away : TeamSide.Home;

            var (game, result, attributed) = RunSingleGenGame(
                c, sideA, sideB, teamASide, teamBSide,
                resolverSeed: gameSeed, governorSeed: gameSeed + 1);

            stats.Accumulate(result.Possessions, game, attributed, teamASide, teamBSide);
        }

        return stats;
    }

    // ── The single-game body (Session 30 extraction) ────────────────────────────
    //
    // The pre-loaded engine configs a gen-style game needs. Loaded once per
    // RunGenMatchup call (the committed behavior) and once per SEASON — never per
    // game. Field order matches the original Load order in RunGenMatchup.
    private sealed record GenEngineConfigs(
        RollAConfig A, RollBConfig B, RollCConfig C, RollDConfig D, RollEConfig E,
        RollFConfig F, RollGConfig G, RollHConfig H, RollIConfig I, RollJConfig J,
        RollKConfig K, RollLConfig L, RollMConfig M,
        RollOffensiveFoulConfig OffFoul, GovernorConfig Gov, RollClockConfig Clock,
        EndOfHalfConfig EndOfHalf, MatchupConfig Matchup, AttentionConfig Attention,
        FatigueConfig Fat);

    private static GenEngineConfigs LoadGenEngineConfigs(string engineConfigPath) => new(
        RollAConfig.Load(engineConfigPath),
        RollBConfig.Load(engineConfigPath),
        RollCConfig.Load(engineConfigPath),
        RollDConfig.Load(engineConfigPath),
        RollEConfig.Load(engineConfigPath),
        RollFConfig.Load(engineConfigPath),
        RollGConfig.Load(engineConfigPath),
        RollHConfig.Load(engineConfigPath),
        RollIConfig.Load(engineConfigPath),
        RollJConfig.Load(engineConfigPath),
        RollKConfig.Load(engineConfigPath),
        RollLConfig.Load(engineConfigPath),
        RollMConfig.Load(engineConfigPath),
        RollOffensiveFoulConfig.Load(engineConfigPath),
        GovernorConfig.Load(engineConfigPath),
        RollClockConfig.Load(engineConfigPath),
        EndOfHalfConfig.Load(engineConfigPath),
        MatchupConfig.Load(engineConfigPath),
        AttentionConfig.Load(engineConfigPath),
        // The fence and the engine's fatigue both read this one config, so the fence's
        // recovery and the engine's halftime rest use identical magnitudes.
        FatigueConfig.Load(engineConfigPath));

    // One complete ten-man engine game — the body lifted verbatim from the committed
    // RunGenMatchup loop (Session 30; the behavioral wall: `gen` and the divvy smoke
    // sim must stay byte-for-byte, so every construction below is in the original
    // order). Attribution uses the resolver seed, exactly as the committed
    // AttributeGame(result, game, gameSeed) call did.
    private static (GameState Game, GovernorRunResult Result, PlayerBoxTotals Attributed) RunSingleGenGame(
        GenEngineConfigs c, GenSideData sideA, GenSideData sideB,
        TeamSide teamASide, TeamSide teamBSide, int resolverSeed, int governorSeed)
    {
        var game = new GameState(
            new FoulTracker(c.D.BonusThreshold, c.D.DoubleBonusThreshold),
            ArrowState.Off,
            new FatigueTracker(c.Fat));

        SeatRoster(game, teamASide, sideA.Starters);
        SeatRoster(game, teamBSide, sideB.Starters);

        // Build each side's depth chart with its PHYSICAL side for this game, then hand
        // the policy the Home/Away pair and the shared halftime-equivalent magnitude.
        var aDepth = new FlatFatigueFencePolicy.SideDepth(
            teamASide, sideA.Starters, sideA.StarterPositions, sideA.Reserves, sideA.ReservePositions);
        var bDepth = new FlatFatigueFencePolicy.SideDepth(
            teamBSide, sideB.Starters, sideB.StarterPositions, sideB.Reserves, sideB.ReservePositions);
        var homeDepth = teamASide == TeamSide.Home ? aDepth : bDepth;
        var awayDepth = teamASide == TeamSide.Home ? bDepth : aDepth;
        var policy = new FlatFatigueFencePolicy(homeDepth, awayDepth, c.Fat.HalftimeRestEquivalentSeconds);

        var resolverRng = new SystemRng(resolverSeed);
        var governorRng = new SystemRng(governorSeed);

        var resolver = new Resolver(
            new RollAGenerator(c.A, c.Matchup, game),
            c.A,
            new RollBGenerator(c.B, c.Matchup, game),
            new RollCGenerator(c.C),
            c.C,
            new RollDGenerator(c.D),
            new RollEGenerator(c.E, game),
            new AttentionGenerator(c.Attention, game),
            new RollFGenerator(c.F, c.Matchup, game),
            new RollGGenerator(c.G, c.Matchup, game),
            new RollHGenerator(c.H, c.Matchup, game),
            new RollIGenerator(c.I, c.Matchup, game),
            new RollJGenerator(c.J, c.Matchup, game),
            new RollKGenerator(c.K, c.Matchup, game),
            new RollLGenerator(c.L, game),
            new RollMGenerator(c.M, c.Matchup, game),
            new RollOffensiveFoulGenerator(c.OffFoul),
            c.Matchup,
            game,
            resolverRng);

        // The one line that differs from the bench runner: the Governor is given the
        // substitution policy (7th argument). Everything else is identical.
        var governor = new Governor(resolver, game, c.Gov, c.Clock, governorRng, c.EndOfHalf, policy);
        var firstState = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1);

        var result = governor.Run(firstState);
        var attributed = AttributeGame(result, game, resolverSeed, c.Matchup);

        return (game, result, attributed);
    }

    // ── Gen accumulator: team-level channels + 20-player box + possessions played ──
    //
    // Team-level channels mirror BenchStats (aggregated by possession Offense, position-
    // and count-agnostic). The 20-player arrays come straight from the shared AttributeGame
    // output. PlayerPoss counts, per player, how many possession records he was on the
    // floor for (either side of the ball) — the column that makes substitutions visible.
    private sealed class GenStats
    {
        public int Games;
        public int TeamAWins, TeamBWins, Ties;
        public readonly List<int> TeamAScores = new();
        public readonly List<int> TeamBScores = new();
        public readonly List<int> Margins     = new();   // A − B

        public long AOffPoss, BOffPoss, APoints, BPoints;
        public long AFga, BFga, AFgm, BFgm;
        public long ARimA,   BRimA,   ARimM,   BRimM;
        public long AShortA, BShortA, AShortM, BShortM;
        public long AMidA,   BMidA,   AMidM,   BMidM;
        public long ALongA,  BLongA,  ALongM,  BLongM;
        public long A3pa, B3pa, A3pm, B3pm;
        public long AFta, BFta, AFtm, BFtm;
        public long AOrbC, BOrbC, AOrbW, BOrbW;
        public long ATrans, BTrans;
        public long ATurnovers,   BTurnovers;
        public long ACommitterTo, BCommitterTo;
        public readonly long[] ASlotFga = new long[5];
        public readonly long[] BSlotFga = new long[5];

        // 20-player arrays (index = PlayerId - 1; A = 0..9, B = 10..19).
        public readonly long[] PlayerFga  = new long[20]; public readonly long[] PlayerFgm  = new long[20];
        public readonly long[] PlayerTpa  = new long[20]; public readonly long[] PlayerTpm  = new long[20];
        public readonly long[] PlayerFta  = new long[20]; public readonly long[] PlayerFtm  = new long[20];
        public readonly long[] PlayerOReb = new long[20]; public readonly long[] PlayerDReb = new long[20];
        public readonly long[] PlayerBlk  = new long[20]; public readonly long[] PlayerStl  = new long[20];
        public readonly long[] PlayerShFoul = new long[20];
        public readonly long[] PlayerAst  = new long[20]; public readonly long[] PlayerTo   = new long[20];
        public readonly long[] PlayerPoss = new long[20];   // possessions on the floor (either side)

        public void Accumulate(
            IReadOnlyList<PossessionRecord> records, GameState game,
            PlayerBoxTotals attributed, TeamSide teamASide, TeamSide teamBSide)
        {
            Games++;

            int aScore = teamASide == TeamSide.Home ? game.HomeScore : game.AwayScore;
            int bScore = teamASide == TeamSide.Home ? game.AwayScore : game.HomeScore;
            TeamAScores.Add(aScore);
            TeamBScores.Add(bScore);
            Margins.Add(aScore - bScore);
            if (aScore > bScore) TeamAWins++;
            else if (bScore > aScore) TeamBWins++;
            else Ties++;

            long SumA(Func<PossessionRecord, int> f) => records.Where(r => r.Offense == teamASide).Sum(r => (long)f(r));
            long SumB(Func<PossessionRecord, int> f) => records.Where(r => r.Offense == teamBSide).Sum(r => (long)f(r));

            AOffPoss += records.Count(r => r.Offense == teamASide);
            BOffPoss += records.Count(r => r.Offense == teamBSide);
            APoints  += SumA(r => r.Points);   BPoints  += SumB(r => r.Points);
            AFga += SumA(r => r.Fga);           BFga += SumB(r => r.Fga);
            AFgm += SumA(r => r.Fgm);           BFgm += SumB(r => r.Fgm);

            ARimA   += SumA(r => r.RimFga);     BRimA   += SumB(r => r.RimFga);
            ARimM   += SumA(r => r.RimFgm);     BRimM   += SumB(r => r.RimFgm);
            AShortA += SumA(r => r.ShortFga);   BShortA += SumB(r => r.ShortFga);
            AShortM += SumA(r => r.ShortFgm);   BShortM += SumB(r => r.ShortFgm);
            AMidA   += SumA(r => r.MidFga);     BMidA   += SumB(r => r.MidFga);
            AMidM   += SumA(r => r.MidFgm);     BMidM   += SumB(r => r.MidFgm);
            ALongA  += SumA(r => r.LongFga);    BLongA  += SumB(r => r.LongFga);
            ALongM  += SumA(r => r.LongFgm);    BLongM  += SumB(r => r.LongFgm);

            A3pa += SumA(r => r.ThreePa);       B3pa += SumB(r => r.ThreePa);
            A3pm += SumA(r => r.ThreePm);       B3pm += SumB(r => r.ThreePm);
            AFta += SumA(r => r.Fta);           BFta += SumB(r => r.Fta);
            AFtm += SumA(r => r.Ftm);           BFtm += SumB(r => r.Ftm);
            AOrbC += SumA(r => r.OrbChances);   BOrbC += SumB(r => r.OrbChances);
            AOrbW += SumA(r => r.OrbWon);       BOrbW += SumB(r => r.OrbWon);

            ATrans += records.Count(r => r.Offense == teamASide && r.Entry == EntryType.Transition);
            BTrans += records.Count(r => r.Offense == teamBSide && r.Entry == EntryType.Transition);

            ATurnovers   += records.Count(r => r.Offense == teamASide && IsTurnoverPossession(r));
            BTurnovers   += records.Count(r => r.Offense == teamBSide && IsTurnoverPossession(r));
            ACommitterTo += records.Count(r => r.Offense == teamASide && IsTurnoverPossession(r) && r.TurnoverOffSlot != null);
            BCommitterTo += records.Count(r => r.Offense == teamBSide && IsTurnoverPossession(r) && r.TurnoverOffSlot != null);

            for (var s = 0; s < 5; s++)
            {
                ASlotFga[s] += SumA(r => GetSlotFga(r, s + 1));
                BSlotFga[s] += SumB(r => GetSlotFga(r, s + 1));
            }

            for (var i = 0; i < 20; i++)
            {
                PlayerFga[i]    += attributed.Fga[i];
                PlayerFgm[i]    += attributed.Fgm[i];
                PlayerTpa[i]    += attributed.Tpa[i];
                PlayerTpm[i]    += attributed.Tpm[i];
                PlayerFta[i]    += attributed.Fta[i];
                PlayerFtm[i]    += attributed.Ftm[i];
                PlayerOReb[i]   += attributed.OReb[i];
                PlayerDReb[i]   += attributed.DReb[i];
                PlayerBlk[i]    += attributed.Blk[i];
                PlayerStl[i]    += attributed.Stl[i];
                PlayerShFoul[i] += attributed.ShFoul[i];
                PlayerAst[i]    += attributed.Ast[i];
                PlayerTo[i]     += attributed.To[i];
            }

            // Possessions-played: every on-floor player of BOTH sides, per record. A player
            // appears on exactly one side per possession, so each on-floor player gets +1;
            // a player subbed out stops accruing from the possession he leaves.
            foreach (var r in records)
                for (var slot = 1; slot <= 5; slot++)
                {
                    var op = game.RosterFor(r.Offense).PlayerAt(new Slot(r.Offense, slot), r.Number);
                    if (op != null && op.PlayerId >= 1 && op.PlayerId <= 20) PlayerPoss[op.PlayerId - 1]++;
                    var dp = game.RosterFor(r.Defense).PlayerAt(new Slot(r.Defense, slot), r.Number);
                    if (dp != null && dp.PlayerId >= 1 && dp.PlayerId <= 20) PlayerPoss[dp.PlayerId - 1]++;
                }
        }
    }

    // ── Gen readout: team-level channels + reconciliation ────────────────────────

    private static void PrintGenChannels(GenStats s)
    {
        Console.WriteLine("--- CHANNEL BREAKDOWN (team-level; roster-shape + outcome proof) ---");
        Console.WriteLine($"Games: {s.Games}");
        Console.WriteLine();

        PrintGenTeamChannels("Team A", s, isA: true);
        PrintGenTeamChannels("Team B", s, isA: false);

        // Turnover reconciliation across the FULL ten-man roster (A = ids 1–10 → indices
        // 0–9, B = ids 11–20 → indices 10–19). A mismatch would mean the logical→physical
        // side mapping inverted for some games, or a sub mis-attributed a committer.
        long aPlayerTo = 0, bPlayerTo = 0;
        for (var i = 0;  i < 10; i++) aPlayerTo += s.PlayerTo[i];
        for (var i = 10; i < 20; i++) bPlayerTo += s.PlayerTo[i];
        bool aOk = aPlayerTo == s.ACommitterTo;
        bool bOk = bPlayerTo == s.BCommitterTo;
        Console.WriteLine("Turnover reconciliation (per-player attribution vs. committer possessions):");
        Console.WriteLine($"  Team A: players={aPlayerTo}  committer={s.ACommitterTo}  team-violations={s.ATurnovers - s.ACommitterTo}  [{(aOk ? "OK" : "MISMATCH")}]");
        Console.WriteLine($"  Team B: players={bPlayerTo}  committer={s.BCommitterTo}  team-violations={s.BTurnovers - s.BCommitterTo}  [{(bOk ? "OK" : "MISMATCH")}]");
        Console.WriteLine();
    }

    private static void PrintGenTeamChannels(string label, GenStats s, bool isA)
    {
        long offPoss = isA ? s.AOffPoss : s.BOffPoss;
        long points  = isA ? s.APoints  : s.BPoints;
        long fga  = isA ? s.AFga  : s.BFga;   long fgm  = isA ? s.AFgm  : s.BFgm;
        long rimA = isA ? s.ARimA : s.BRimA;  long rimM = isA ? s.ARimM : s.BRimM;
        long shA  = isA ? s.AShortA : s.BShortA; long shM = isA ? s.AShortM : s.BShortM;
        long midA = isA ? s.AMidA : s.BMidA;  long midM = isA ? s.AMidM : s.BMidM;
        long lgA  = isA ? s.ALongA : s.BLongA; long lgM = isA ? s.ALongM : s.BLongM;
        long tpa  = isA ? s.A3pa  : s.B3pa;   long tpm  = isA ? s.A3pm  : s.B3pm;
        long fta  = isA ? s.AFta  : s.BFta;   long ftm  = isA ? s.AFtm  : s.BFtm;
        long orbC = isA ? s.AOrbC : s.BOrbC;  long orbW = isA ? s.AOrbW : s.BOrbW;
        long trans = isA ? s.ATrans : s.BTrans;
        long turns = isA ? s.ATurnovers : s.BTurnovers;
        long[] slotFga = isA ? s.ASlotFga : s.BSlotFga;
        int wins = isA ? s.TeamAWins : s.TeamBWins;
        var scores = isA ? s.TeamAScores : s.TeamBScores;
        double avgMargin = s.Margins.Count > 0 ? s.Margins.Average() * (isA ? 1 : -1) : 0.0;

        double Pct(long m, long a) => a > 0 ? 100.0 * m / a : 0.0;
        double Rate(long n, long d) => d > 0 ? (double)n / d : 0.0;
        double winPct = s.Games > 0 ? 100.0 * wins / s.Games : 0.0;
        double avgScore = scores.Count > 0 ? scores.Average() : 0.0;

        Console.WriteLine($"{label}:");
        Console.WriteLine($"  Result:     win% {winPct:F1}   avgScore {avgScore:F1}   avgMargin {avgMargin:+0.0;-0.0}   PPP {Rate(points, offPoss):F3}");
        Console.WriteLine($"  Shooting:   FG% {Pct(fgm, fga):F1}   Rim {Pct(rimM, rimA):F1}   Short {Pct(shM, shA):F1}   Mid {Pct(midM, midA):F1}   Long {Pct(lgM, lgA):F1}   Three {Pct(tpm, tpa):F1}   FT% {Pct(ftm, fta):F1}");
        Console.WriteLine($"  Shot mix:   Rim {Pct(rimA, fga):F1}%   Short {Pct(shA, fga):F1}%   Mid {Pct(midA, fga):F1}%   Long {Pct(lgA, fga):F1}%   Three {Pct(tpa, fga):F1}%");
        Console.WriteLine($"  Glass:      ORB% {Pct(orbW, orbC):F1}   (won {orbW} of {orbC} chances)");
        Console.WriteLine($"  Turnovers:  TO rate {Rate(turns, offPoss):F3}   ({turns} in {offPoss} off. poss)");
        Console.WriteLine($"  Transition: freq {Rate(trans, offPoss):F3}   ({trans} of {offPoss})");
        Console.WriteLine($"  Free throw: FTA/FGA {Rate(fta, fga):F3}   (FTA {fta})");
        Console.WriteLine($"  Usage:      starter slot FGA   1:{slotFga[0]}   2:{slotFga[1]}   3:{slotFga[2]}   4:{slotFga[3]}   5:{slotFga[4]}");
        Console.WriteLine();
    }

    // ── Gen readout: the 20-player box score with a possessions column ────────────
    //
    // Every player who logged at least one on-floor possession prints — up to twenty. A
    // reserve the fence never called on has POSS 0 and is omitted; the ten starters always
    // appear. The POSS column (per-game average possessions on the floor) is what makes the
    // substitution pattern legible: a starter reads near the full-game count, a used reserve
    // reads a fraction of it.
    private static void PrintGenBoxScore(GenStats s, Dictionary<int, GenIdentity> identity)
    {
        Console.WriteLine($"--- PER-PLAYER BOX SCORE (per-game averages, {s.Games} games) ---");
        Console.WriteLine("  Ten-man rosters: [A]/[B] with roster depth slot (1..10) and role. POSS = per-game");
        Console.WriteLine("  possessions on the floor (either side of the ball) — starters near the full count,");
        Console.WriteLine("  reserves a fraction if the fatigue fence used them; a reserve never used is omitted.");
        Console.WriteLine("  Exact attribution: FGA FGM 3PA 3PM FTA FTM ORB DRB STL BLK AST TO. Weighted: SFL only.");
        Console.WriteLine($"  {"Player",-24} {"POSS",5} {"PTS",5} {"FGA",5} {"FGM",5} {"FG%",5} {"3PA",5} {"3PM",5} {"3P%",5} {"FTA",5} {"FTM",5} {"FT%",5} {"ORB",5} {"DRB",5} {"REB",5} {"STL",5} {"BLK",5} {"AST",5} {"TO",5} {"SFL",5}");
        Console.WriteLine(new string('─', 133));

        double g = s.Games;
        for (var i = 0; i < 20; i++)
        {
            if (s.PlayerPoss[i] <= 0) continue;   // never took the floor

            var poss = s.PlayerPoss[i] / g;
            var fga = s.PlayerFga[i]    / g;  var fgm = s.PlayerFgm[i]  / g;
            var tpa = s.PlayerTpa[i]    / g;  var tpm = s.PlayerTpm[i]  / g;
            var fta = s.PlayerFta[i]    / g;  var ftm = s.PlayerFtm[i]  / g;
            var orb = s.PlayerOReb[i]   / g;  var drb = s.PlayerDReb[i] / g;
            var stl = s.PlayerStl[i]    / g;  var blk = s.PlayerBlk[i]  / g;
            var to  = s.PlayerTo[i]     / g;  var sfl = s.PlayerShFoul[i] / g;
            var ast = s.PlayerAst[i]    / g;
            var pts = (fgm - tpm) * 2.0 + tpm * 3.0 + ftm;
            var fgPct = fga > 0 ? fgm / fga * 100 : 0.0;
            var tpPct = tpa > 0 ? tpm / tpa * 100 : 0.0;
            var ftPct = fta > 0 ? ftm / fta * 100 : 0.0;

            string label;
            if (identity.TryGetValue(i + 1, out var id))
            {
                var mark = id.Starter ? "S" : "b";
                label = $"[{id.Team}]{id.Slot,2} {mark} {id.Pos} {id.Role}";
                if (label.Length > 24) label = label.Substring(0, 24);
            }
            else label = $"id {i + 1}";

            Console.WriteLine(
                $"  {label,-24} {poss,5:F1} {pts,5:F1} {fga,5:F1} {fgm,5:F1} {fgPct,5:F1} " +
                $"{tpa,5:F1} {tpm,5:F1} {tpPct,5:F1} {fta,5:F1} {ftm,5:F1} {ftPct,5:F1} " +
                $"{orb,5:F1} {drb,5:F1} {(orb + drb),5:F1} {stl,5:F1} {blk,5:F1} {ast,5:F1} {to,5:F1} {sfl,5:F1}");
        }

        // Per-player FGA reconciliation, raw accumulators (never the rounded display). Each
        // team's players' FGA must equal that team's starter-slot usage FGA total: both are
        // built from the same GetSlotFga binning. (As on the bench, this is NOT reconciled
        // against the channel FGA total, which includes the ~0.2% null-slot bonus-FT-putback
        // attempts that carry no slot attribution.)
        long aPlayerFga = 0, bPlayerFga = 0, aUsageFga = 0, bUsageFga = 0;
        for (var i = 0;  i < 10; i++) aPlayerFga += s.PlayerFga[i];
        for (var i = 10; i < 20; i++) bPlayerFga += s.PlayerFga[i];
        for (var i = 0;  i < 5;  i++) { aUsageFga += s.ASlotFga[i]; bUsageFga += s.BSlotFga[i]; }
        bool aFgaOk = aPlayerFga == aUsageFga;
        bool bFgaOk = bPlayerFga == bUsageFga;
        Console.WriteLine();
        Console.WriteLine("Per-player FGA reconciliation (all players' FGA vs. starter-slot usage FGA):");
        Console.WriteLine($"  Team A: players={aPlayerFga}  slotUsage={aUsageFga}  [{(aFgaOk ? "OK" : "MISMATCH")}]");
        Console.WriteLine($"  Team B: players={bPlayerFga}  slotUsage={bUsageFga}  [{(bFgaOk ? "OK" : "MISMATCH")}]");
        Console.WriteLine();
    }
}
