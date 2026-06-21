using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    // =========================================================================
    // Stress-Test: Archetype Generator + Multi-Bucket Simulation
    // 8 buckets × 10 variants × 50 games = 4,000 games
    // Harness-only; no engine files changed.
    // =========================================================================

    // ── Enums ─────────────────────────────────────────────────────────────────

    private enum PlayerArchetype
    {
        RimRunner,
        PerimeterShooter,
        Slasher,
        PostScorer,
        ThreeAndDWing,
        PassFirstGuard,
        AthleticBig,
        FloorGeneral,
    }

    private enum TalentTier { Elite, Good, Average, Weak }

    // ── Tier parameters ───────────────────────────────────────────────────────

    private readonly record struct TierParams(int BaseCenter, int StrMin, int StrMax, int WeaknessPenalty);

    private static TierParams GetTierParams(TalentTier tier) => tier switch
    {
        TalentTier.Elite   => new TierParams(55, 75, 88, 15),
        TalentTier.Good    => new TierParams(48, 63, 76, 12),
        TalentTier.Average => new TierParams(40, 50, 63, 10),
        TalentTier.Weak    => new TierParams(28, 35, 48,  8),
        _                  => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    // ── Standard roster compositions ─────────────────────────────────────────

    private static readonly PlayerArchetype[] BalancedRoster =
    {
        PlayerArchetype.PassFirstGuard,
        PlayerArchetype.PerimeterShooter,
        PlayerArchetype.ThreeAndDWing,
        PlayerArchetype.PostScorer,
        PlayerArchetype.RimRunner,
    };

    private static readonly PlayerArchetype[] ShootingRoster =
    {
        PlayerArchetype.PassFirstGuard,
        PlayerArchetype.PerimeterShooter,
        PlayerArchetype.PerimeterShooter,
        PlayerArchetype.ThreeAndDWing,
        PlayerArchetype.FloorGeneral,
    };

    private static readonly PlayerArchetype[] AthleticRoster =
    {
        PlayerArchetype.Slasher,
        PlayerArchetype.AthleticBig,
        PlayerArchetype.Slasher,
        PlayerArchetype.RimRunner,
        PlayerArchetype.AthleticBig,
    };

    private static readonly PlayerArchetype[] SkillRoster =
    {
        PlayerArchetype.FloorGeneral,
        PlayerArchetype.PerimeterShooter,
        PlayerArchetype.ThreeAndDWing,
        PlayerArchetype.PostScorer,
        PlayerArchetype.FloorGeneral,
    };

    // ── Player factory ────────────────────────────────────────────────────────

    // Phase 22: tier-DECOUPLED FreeThrow draw. Distribution centered at 70, clamped
    // to [45, 95]. Two soft center-nudges, both measured against a FIXED pivot of 50
    // (NOT the tier center). This removes the DIRECT tier coupling of the old
    // Clamp(AtBaseline()/AtStrength()) authoring. A MILD INDIRECT tier correlation
    // may remain because Outside/Height are themselves tier-scaled, but it is
    // negligible (measured corr(FT,tier) ≈ 0.016, ~0.5pt mean spread Elite→Weak —
    // §4d tier-leak experiment). This is NOT "utterly independent"; it is direct
    // decoupling with a mild, intended position/shooting correlation.
    //   - Outside nudge UP   (±FtOutsideNudgeMax at the extremes), stronger input.
    //   - Height  nudge DOWN (±FtHeightNudgeMax  at the extremes), weaker input.
    // The nudges shift the center only; the spread is identical for every player, so
    // the extremes (a 95-shooting non-prospect, an 80%-shooting stiff) stay reachable
    // — but note the clamp piles small mass at exactly 45 and 95 (§4d). All six
    // constants (center, min, max, two nudges, plus `half` below) are placeholders.
    private const int FtCenter        = 72;
    private const int FtMin           = 45;
    private const int FtMax           = 95;
    private const int FtPivot         = 50;   // fixed yardstick — NOT the tier center
    private const double FtOutsideNudgeMax = 4.0;
    private const double FtHeightNudgeMax  = 1.0;

    private static int DrawFreeThrow(int outsideRating, int heightRating, Random rng)
    {
        // Center nudges, fixed-pivot (range of (rating - 50) is roughly [-40, +49];
        // divide by ~49 to map the extreme to ±1, then scale by the max nudge).
        double outsideNudge =  ((outsideRating - FtPivot) / 49.0) * FtOutsideNudgeMax;
        double heightNudge  = -((heightRating  - FtPivot) / 49.0) * FtHeightNudgeMax;
        double center = FtCenter + outsideNudge + heightNudge;

        // Spread via mean of 3 independent uniform draws (~bell), centered on
        // `center`. The summed-uniform mean has SD ≈ half/3, so the spread is set by
        // `half`. half=30 → SD ≈ 10, which (clamped to [45,95]) gives a clear peak
        // near 70 (~40% in [65,75]) with the distribution reaching both bounds.
        //
        // NOTE: the clamp piles a small amount of mass at exactly 45 and exactly 95
        // (values that would fall outside the range collapse to the bound). This is
        // accepted for a first-pass authoring model; the Python validation (§4d)
        // reports the exact-45/exact-95 pile sizes to confirm they are small.
        // DO NOT use a small half (e.g. 12 gives SD≈4, so the distribution never
        // reaches 45/95 and the extremes vanish entirely — the failure to avoid).
        double half = 30.0;   // Python-validated starting value (§4d); a calibration placeholder
        double sum = 0.0;
        for (var i = 0; i < 3; i++)
            sum += center + (rng.NextDouble() * 2.0 - 1.0) * half;
        double draw = sum / 3.0;

        return Math.Max(FtMin, Math.Min(FtMax, (int)Math.Round(draw)));
    }

    private static Player MakePlayer(PlayerArchetype archetype, TalentTier tier, int playerSeed, string name)
    {
        var rng    = new Random(playerSeed);
        var tp     = GetTierParams(tier);

        int AtStrength() => rng.Next(tp.StrMin,          tp.StrMax + 1);
        int AtBaseline() => rng.Next(tp.BaseCenter - 8,  tp.BaseCenter + 8 + 1);
        int AtWeakness() => Math.Max(10, rng.Next(tp.BaseCenter - tp.WeaknessPenalty - 6,
                                                   tp.BaseCenter - tp.WeaknessPenalty + 6 + 1));
        int Clamp(int v) => Math.Max(10, Math.Min(99, v));

        // Tendency helpers — independent of elevated/weakness
        int TStr(int lo, int hi) => Clamp(rng.Next(lo, hi + 1));

        // ── Attribute assignment by archetype ─────────────────────────────
        Player p;
        switch (archetype)
        {
            case PlayerArchetype.RimRunner:
                int rrOutside = Clamp(AtWeakness());
                int rrHeight  = Clamp(AtStrength());
                p = new Player(name)
                {
                    Finishing          = Clamp(AtStrength()),
                    Close              = Clamp(AtStrength()),
                    Height             = rrHeight,
                    Wingspan           = Clamp(AtStrength()),
                    Weight             = Clamp(AtStrength()),
                    Strength           = Clamp(AtStrength()),
                    Vertical           = Clamp(AtStrength()),
                    RimProtection      = Clamp(AtStrength()),
                    DefensiveRebounding= Clamp(AtStrength()),
                    OffensiveRebounding= Clamp(AtStrength()),
                    PostDefense        = Clamp(AtStrength()),
                    Outside            = rrOutside,
                    Mid                = Clamp(AtBaseline()),
                    FreeThrow          = DrawFreeThrow(rrOutside, rrHeight, rng),
                    FoulDrawing        = Clamp(AtBaseline()),
                    BallHandling       = Clamp(AtBaseline()),
                    Passing            = Clamp(AtBaseline()),
                    Playmaking         = Clamp(AtBaseline()),
                    SelfCreation       = Clamp(AtBaseline()),
                    PostMoves          = Clamp(AtBaseline()),
                    OffBallMovement    = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    PerimeterDefense   = Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    Speed              = Clamp(AtBaseline()),
                    Quickness          = Clamp(AtBaseline()),
                    FirstStep          = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    BasketballIQ       = Clamp(AtBaseline()),
                    Discipline         = Clamp(AtBaseline()),
                    RimTendency        = TStr(65, 85),
                    ShortTendency      = TStr(35, 50),
                    MidTendency        = TStr(15, 25),
                    LongTendency       = TStr(5,  10),
                    ThreeTendency      = Clamp(AtWeakness()),  // weakness
                };
                break;

            case PlayerArchetype.PerimeterShooter:
                int psOutside = Clamp(AtStrength());
                int psHeight  = Clamp(AtWeakness());
                p = new Player(name)
                {
                    Outside            = psOutside,
                    FreeThrow          = DrawFreeThrow(psOutside, psHeight, rng),
                    Speed              = Clamp(AtStrength()),
                    Quickness          = Clamp(AtStrength()),
                    OffBallMovement    = Clamp(AtStrength()),
                    FoulDrawing        = Clamp(AtStrength()),
                    Height             = psHeight,
                    Weight             = Clamp(AtWeakness()),
                    Strength           = Clamp(AtWeakness()),
                    RimProtection      = Clamp(AtWeakness()),
                    PostMoves          = Clamp(AtWeakness()),
                    PostDefense        = Clamp(AtWeakness()),
                    OffensiveRebounding= Clamp(AtWeakness()),
                    DefensiveRebounding= Clamp(AtWeakness()),
                    Close              = Clamp(AtBaseline()),
                    Mid                = Clamp(AtBaseline()),
                    Finishing          = Clamp(AtBaseline()),
                    BallHandling       = Clamp(AtBaseline()),
                    Passing            = Clamp(AtBaseline()),
                    Playmaking         = Clamp(AtBaseline()),
                    SelfCreation       = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    Wingspan           = Clamp(AtBaseline()),
                    PerimeterDefense   = Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    FirstStep          = Clamp(AtBaseline()),
                    Vertical           = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    BasketballIQ       = Clamp(AtBaseline()),
                    Discipline         = Clamp(AtBaseline()),
                    ThreeTendency      = TStr(65, 85),
                    LongTendency       = TStr(30, 45),
                    MidTendency        = TStr(15, 25),
                    ShortTendency      = TStr(5,  10),
                    RimTendency        = TStr(5,  10),
                };
                break;

            case PlayerArchetype.Slasher:
                int slOutside = Clamp(AtBaseline()); // Phase 26: raised from AtWeakness — "hits the open one"
                int slHeight  = Clamp(AtWeakness());
                p = new Player(name)
                {
                    Speed              = Clamp(AtStrength()),
                    FirstStep          = Clamp(AtStrength()),
                    Quickness          = Clamp(AtStrength()),
                    Finishing          = Clamp(AtStrength()),
                    FoulDrawing        = Clamp(AtStrength()),
                    SelfCreation       = Clamp(AtStrength()),
                    Vertical           = Clamp(AtStrength()),
                    Outside            = slOutside,
                    PostMoves          = Clamp(AtWeakness()),
                    Height             = slHeight,
                    Close              = Clamp(AtBaseline()),
                    Mid                = Clamp(AtBaseline()),
                    FreeThrow          = DrawFreeThrow(slOutside, slHeight, rng),
                    BallHandling       = Clamp(AtBaseline()),
                    Passing            = Clamp(AtBaseline()),
                    Playmaking         = Clamp(AtBaseline()),
                    OffBallMovement    = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    OffensiveRebounding= Clamp(AtBaseline()),
                    Wingspan           = Clamp(AtBaseline()),
                    Weight             = Clamp(AtBaseline()),
                    Strength           = Clamp(AtBaseline()),
                    PerimeterDefense   = Clamp(AtBaseline()),
                    PostDefense        = Clamp(AtBaseline()),
                    RimProtection      = Clamp(AtBaseline()),
                    DefensiveRebounding= Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    BasketballIQ       = Clamp(AtBaseline()),
                    Discipline         = Clamp(AtBaseline()),
                    RimTendency        = TStr(65, 85),
                    ShortTendency      = TStr(30, 45),
                    MidTendency        = TStr(15, 25),
                    LongTendency       = TStr(5,  10),
                    ThreeTendency      = TStr(25, 40), // Phase 26: raised from TStr(5,10) — takes open corner threes
                };
                break;

            case PlayerArchetype.PostScorer:
                int poscOutside = Clamp(AtWeakness());
                int poscHeight  = Clamp(AtStrength());
                p = new Player(name)
                {
                    PostMoves          = Clamp(AtStrength()),
                    Close              = Clamp(AtStrength()),
                    Strength           = Clamp(AtStrength()),
                    Weight             = Clamp(AtStrength()),
                    Height             = poscHeight,
                    Wingspan           = Clamp(AtStrength()),
                    FoulDrawing        = Clamp(AtStrength()),
                    OffensiveRebounding= Clamp(AtStrength()),
                    PostDefense        = Clamp(AtStrength()),
                    Speed              = Clamp(AtWeakness()),
                    Quickness          = Clamp(AtWeakness()),
                    Outside            = poscOutside,
                    Mid                = Clamp(AtBaseline()),
                    Finishing          = Clamp(AtBaseline()),
                    FreeThrow          = DrawFreeThrow(poscOutside, poscHeight, rng),
                    BallHandling       = Clamp(AtBaseline()),
                    Passing            = Clamp(AtBaseline()),
                    Playmaking         = Clamp(AtBaseline()),
                    SelfCreation       = Clamp(AtBaseline()),
                    OffBallMovement    = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    PerimeterDefense   = Clamp(AtBaseline()),
                    RimProtection      = Clamp(AtBaseline()),
                    DefensiveRebounding= Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    FirstStep          = Clamp(AtBaseline()),
                    Vertical           = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    BasketballIQ       = Clamp(AtBaseline()),
                    Discipline         = Clamp(AtBaseline()),
                    ShortTendency      = TStr(65, 85),
                    RimTendency        = TStr(30, 45),
                    MidTendency        = TStr(15, 25),
                    LongTendency       = TStr(5,  10),
                    ThreeTendency      = Clamp(AtWeakness()),  // weakness
                };
                break;

            case PlayerArchetype.ThreeAndDWing:
                int tdwOutside = Clamp(AtStrength());
                int tdwHeight  = Clamp(AtStrength());
                p = new Player(name)
                {
                    Outside            = tdwOutside,
                    PerimeterDefense   = Clamp(AtStrength()),
                    Height             = tdwHeight,
                    Wingspan           = Clamp(AtStrength()),
                    Speed              = Clamp(AtStrength()),
                    FreeThrow          = DrawFreeThrow(tdwOutside, tdwHeight, rng),
                    PostMoves          = Clamp(AtWeakness()),
                    PostDefense        = Clamp(AtWeakness()),
                    RimProtection      = Clamp(AtWeakness()),
                    Weight             = Clamp(AtWeakness()),
                    Close              = Clamp(AtBaseline()),
                    Mid                = Clamp(AtBaseline()),
                    Finishing          = Clamp(AtBaseline()),
                    FoulDrawing        = Clamp(AtBaseline()),
                    BallHandling       = Clamp(AtBaseline()),
                    Passing            = Clamp(AtBaseline()),
                    Playmaking         = Clamp(AtBaseline()),
                    SelfCreation       = Clamp(AtBaseline()),
                    OffBallMovement    = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    OffensiveRebounding= Clamp(AtBaseline()),
                    DefensiveRebounding= Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    Strength           = Clamp(AtBaseline()),
                    Quickness          = Clamp(AtBaseline()),
                    FirstStep          = Clamp(AtBaseline()),
                    Vertical           = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    BasketballIQ       = Clamp(AtBaseline()),
                    Discipline         = Clamp(AtBaseline()),
                    ThreeTendency      = TStr(65, 85),
                    LongTendency       = TStr(30, 45),
                    MidTendency        = TStr(15, 25),
                    ShortTendency      = TStr(5,  10),
                    RimTendency        = TStr(5,  10),
                };
                break;

            case PlayerArchetype.PassFirstGuard:
                int pfgOutside = Clamp(AtWeakness());
                int pfgHeight  = Clamp(AtWeakness());
                p = new Player(name)
                {
                    Playmaking         = Clamp(AtStrength()),
                    Passing            = Clamp(AtStrength()),
                    BallHandling       = Clamp(AtStrength()),
                    BasketballIQ       = Clamp(AtStrength()),
                    Speed              = Clamp(AtStrength()),
                    Quickness          = Clamp(AtStrength()),
                    Discipline         = Clamp(AtStrength()),
                    SelfCreation       = Clamp(AtWeakness()),
                    Outside            = pfgOutside,
                    Height             = pfgHeight,
                    Finishing          = Clamp(AtWeakness()),
                    Close              = Clamp(AtBaseline()),
                    Mid                = Clamp(AtBaseline()),
                    FreeThrow          = DrawFreeThrow(pfgOutside, pfgHeight, rng),
                    FoulDrawing        = Clamp(AtBaseline()),
                    PostMoves          = Clamp(AtBaseline()),
                    OffBallMovement    = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    OffensiveRebounding= Clamp(AtBaseline()),
                    Wingspan           = Clamp(AtBaseline()),
                    Weight             = Clamp(AtBaseline()),
                    Strength           = Clamp(AtBaseline()),
                    PerimeterDefense   = Clamp(AtBaseline()),
                    PostDefense        = Clamp(AtBaseline()),
                    RimProtection      = Clamp(AtBaseline()),
                    DefensiveRebounding= Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    FirstStep          = Clamp(AtBaseline()),
                    Vertical           = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    // Spread tendencies — mild ThreeTendency bias
                    RimTendency        = TStr(15, 25),
                    ShortTendency      = TStr(15, 25),
                    MidTendency        = TStr(15, 25),
                    LongTendency       = TStr(15, 25),
                    ThreeTendency      = TStr(20, 30),
                };
                break;

            case PlayerArchetype.AthleticBig:
                int abOutside = Clamp(AtWeakness());
                int abHeight  = Clamp(AtStrength());
                p = new Player(name)
                {
                    Speed              = Clamp(AtStrength()),
                    Quickness          = Clamp(AtStrength()),
                    FirstStep          = Clamp(AtStrength()),
                    Vertical           = Clamp(AtStrength()),
                    Strength           = Clamp(AtStrength()),
                    Height             = abHeight,
                    Wingspan           = Clamp(AtStrength()),
                    Finishing          = Clamp(AtStrength()),
                    RimProtection      = Clamp(AtStrength()),
                    DefensiveRebounding= Clamp(AtStrength()),
                    OffensiveRebounding= Clamp(AtStrength()),
                    // Explicit weaknesses — critical for contrast
                    Outside            = abOutside,
                    Mid                = Clamp(AtWeakness()),
                    PostMoves          = Clamp(AtWeakness()),
                    Playmaking         = Clamp(AtWeakness()),
                    BallHandling       = Clamp(AtWeakness()),
                    Passing            = Clamp(AtWeakness()),
                    Close              = Clamp(AtBaseline()),
                    FreeThrow          = DrawFreeThrow(abOutside, abHeight, rng),
                    FoulDrawing        = Clamp(AtBaseline()),
                    SelfCreation       = Clamp(AtBaseline()),
                    OffBallMovement    = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    Weight             = Clamp(AtBaseline()),
                    PerimeterDefense   = Clamp(AtBaseline()),
                    PostDefense        = Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    BasketballIQ       = Clamp(AtBaseline()),
                    Discipline         = Clamp(AtBaseline()),
                    RimTendency        = TStr(75, 88),   // top of elevated range
                    ShortTendency      = TStr(10, 20),
                    MidTendency        = TStr(5,  10),
                    LongTendency       = TStr(5,  10),
                    ThreeTendency      = TStr(5,  10),
                };
                break;

            case PlayerArchetype.FloorGeneral:
                int fgOutside = Clamp(AtStrength());
                int fgHeight  = Clamp(AtWeakness());
                p = new Player(name)
                {
                    BasketballIQ       = Clamp(AtStrength()),
                    Passing            = Clamp(AtStrength()),
                    Playmaking         = Clamp(AtStrength()),
                    BallHandling       = Clamp(AtStrength()),
                    Discipline         = Clamp(AtStrength()),
                    FreeThrow          = DrawFreeThrow(fgOutside, fgHeight, rng),
                    Outside            = fgOutside,
                    // Explicit weaknesses — critical for contrast
                    Speed              = Clamp(AtWeakness()),
                    Quickness          = Clamp(AtWeakness()),
                    FirstStep          = Clamp(AtWeakness()),
                    Vertical           = Clamp(AtWeakness()),
                    Strength           = Clamp(AtWeakness()),
                    Height             = fgHeight,
                    Close              = Clamp(AtBaseline()),
                    Mid                = Clamp(AtBaseline()),
                    Finishing          = Clamp(AtBaseline()),
                    FoulDrawing        = Clamp(AtBaseline()),
                    SelfCreation       = Clamp(AtBaseline()),
                    PostMoves          = Clamp(AtBaseline()),
                    OffBallMovement    = Clamp(AtBaseline()),
                    Screening          = Clamp(AtBaseline()),
                    OffensiveRebounding= Clamp(AtBaseline()),
                    Wingspan           = Clamp(AtBaseline()),
                    Weight             = Clamp(AtBaseline()),
                    PerimeterDefense   = Clamp(AtBaseline()),
                    PostDefense        = Clamp(AtBaseline()),
                    RimProtection      = Clamp(AtBaseline()),
                    DefensiveRebounding= Clamp(AtBaseline()),
                    Steals             = Clamp(AtBaseline()),
                    HelpDefense        = Clamp(AtBaseline()),
                    Endurance          = Clamp(AtBaseline()),
                    Hustle             = Clamp(AtBaseline()),
                    // Spread with ThreeTendency and MidTendency slightly elevated
                    RimTendency        = TStr(10, 20),
                    ShortTendency      = TStr(15, 25),
                    MidTendency        = TStr(20, 30),
                    LongTendency       = TStr(15, 25),
                    ThreeTendency      = TStr(20, 30),
                };
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(archetype));
        }

        var violations = p.Validate();
        if (violations.Count > 0)
            throw new InvalidOperationException(
                $"Player {name} failed Validate(): {string.Join("; ", violations)}");

        return p;
    }

    // ── Roster builders ───────────────────────────────────────────────────────

    private static Player[] BuildArchetypeRoster(
        PlayerArchetype[] archetypes,
        TalentTier        tier,
        int               baseSeed,
        string            logicalTeamLabel)
    {
        var players = new Player[5];
        for (var i = 0; i < 5; i++)
            players[i] = MakePlayer(archetypes[i], tier, baseSeed + i, $"{logicalTeamLabel}_Slot{i + 1}");
        return players;
    }

    private static Player[] BuildStarDrivenRoster(
        PlayerArchetype   starArchetype,
        PlayerArchetype[] roleArchetypes,   // length 4
        int               baseSeed,
        string            logicalTeamLabel)
    {
        var players = new Player[5];
        players[0] = MakePlayer(starArchetype, TalentTier.Elite, baseSeed,     $"{logicalTeamLabel}_Slot1");
        for (var i = 0; i < 4; i++)
            players[i + 1] = MakePlayer(roleArchetypes[i], TalentTier.Good, baseSeed + i + 1,
                                        $"{logicalTeamLabel}_Slot{i + 2}");
        return players;
    }

    // ── Roster seating ────────────────────────────────────────────────────────

    private static void SeatRoster(GameState game, TeamSide physicalSide, IReadOnlyList<Player> players)
    {
        var lineup = game.LineupFor(physicalSide);
        var roster = game.RosterFor(physicalSide);
        for (var i = 0; i < 5; i++)
            roster.SetStarter(lineup.SlotAt(i + 1), players[i]);
    }

    // ── Roster fingerprint ────────────────────────────────────────────────────

    private readonly record struct RosterFingerprint(
        double Outside, double Finishing, double Mid, double Close, double SelfCreation,
        double Speed, double Quickness, double Vertical, double Strength,
        double Height, double Wingspan,
        double Passing, double Playmaking, double BallHandling, double BasketballIQ,
        double PerimeterDefense, double PostDefense, double RimProtection,
        double OffensiveRebounding, double DefensiveRebounding);

    private static RosterFingerprint ComputeFingerprint(IReadOnlyList<Player> players)
    {
        double Avg(Func<Player, int> f) => players.Average(p => (double)f(p));
        return new RosterFingerprint(
            Outside:            Avg(p => p.Outside),
            Finishing:          Avg(p => p.Finishing),
            Mid:                Avg(p => p.Mid),
            Close:              Avg(p => p.Close),
            SelfCreation:       Avg(p => p.SelfCreation),
            Speed:              Avg(p => p.Speed),
            Quickness:          Avg(p => p.Quickness),
            Vertical:           Avg(p => p.Vertical),
            Strength:           Avg(p => p.Strength),
            Height:             Avg(p => p.Height),
            Wingspan:           Avg(p => p.Wingspan),
            Passing:            Avg(p => p.Passing),
            Playmaking:         Avg(p => p.Playmaking),
            BallHandling:       Avg(p => p.BallHandling),
            BasketballIQ:       Avg(p => p.BasketballIQ),
            PerimeterDefense:   Avg(p => p.PerimeterDefense),
            PostDefense:        Avg(p => p.PostDefense),
            RimProtection:      Avg(p => p.RimProtection),
            OffensiveRebounding:Avg(p => p.OffensiveRebounding),
            DefensiveRebounding:Avg(p => p.DefensiveRebounding));
    }

    private static void PrintFingerprint(string label, RosterFingerprint fp)
    {
        Console.WriteLine($"  {label}:");
        Console.WriteLine($"    Scoring:  Outside={fp.Outside:F0}  Finishing={fp.Finishing:F0}  Mid={fp.Mid:F0}  Close={fp.Close:F0}  SelfCreation={fp.SelfCreation:F0}");
        Console.WriteLine($"    Athletic: Speed={fp.Speed:F0}  Quickness={fp.Quickness:F0}  Vertical={fp.Vertical:F0}  Strength={fp.Strength:F0}");
        Console.WriteLine($"    Physical: Height={fp.Height:F0}  Wingspan={fp.Wingspan:F0}");
        Console.WriteLine($"    Skill:    Passing={fp.Passing:F0}  Playmaking={fp.Playmaking:F0}  BallHandling={fp.BallHandling:F0}  BasketballIQ={fp.BasketballIQ:F0}");
        Console.WriteLine($"    Defense:  PerimeterDefense={fp.PerimeterDefense:F0}  PostDefense={fp.PostDefense:F0}  RimProtection={fp.RimProtection:F0}");
        Console.WriteLine($"    Boards:   OffensiveRebounding={fp.OffensiveRebounding:F0}  DefensiveRebounding={fp.DefensiveRebounding:F0}");
    }

    // ── Per-variant stat accumulator ──────────────────────────────────────────

    private sealed class VariantStats
    {
        public int     ValidGames;
        // Win/tie tracking for Team A (logical)
        public int     TeamAWins, TeamBWins, Ties;
        // Physical home wins (for side-neutrality diagnostic)
        public int     PhysicalHomeWins;
        // Score lists (per game, Team A and Team B logical scores)
        public readonly List<int>    TeamAScores = new();
        public readonly List<int>    TeamBScores = new();
        public readonly List<int>    Margins     = new();   // A - B
        // Per-team offensive possession and points totals (aggregate)
        public long    TeamAOffPoss, TeamBOffPoss;
        public long    TeamAPoints,  TeamBPoints;
        // PPP per game (for mean/sd)
        public readonly List<double> TeamAPPP = new();
        public readonly List<double> TeamBPPP = new();
        // FG: aggregate attempts/makes per team
        public long    TeamAFga, TeamBFga, TeamAFgm, TeamBFgm;
        // FG% per game
        public readonly List<double> TeamAFgPct = new();
        public readonly List<double> TeamBFgPct = new();
        // 3P: aggregate
        public long    TeamA3pa, TeamB3pa, TeamA3pm, TeamB3pm;
        // 3P% per game
        public readonly List<double> TeamA3pPct = new();
        public readonly List<double> TeamB3pPct = new();
        // FT: aggregate
        public long    TeamAFta, TeamBFta, TeamAFtm, TeamBFtm;
        // FT% per game
        public readonly List<double> TeamAFtPct = new();
        public readonly List<double> TeamBFtPct = new();
        // Shot mix: aggregate zone attempts per team
        public long    TeamARimA,  TeamBRimA;
        public long    TeamAShortA,TeamBShortA;
        public long    TeamAMidA,  TeamBMidA;
        public long    TeamALongA, TeamBLongA;
        // ORB: aggregate
        public long    TeamAOrbW, TeamBOrbW, TeamAOrbC, TeamBOrbC;
        // FTr: FTA/FGA aggregate (combined team A + B together — computed from totals)
        // Transition: per-game
        public readonly List<double> TeamATransFreq = new();
        public readonly List<double> TeamBTransFreq = new();
        // Per-slot FGA (usage observability): aggregate FGA per slot per logical team.
        // Per-slot, not per-player — valid under fixed lineups.
        public long TeamASlot1Fga, TeamASlot2Fga, TeamASlot3Fga, TeamASlot4Fga, TeamASlot5Fga;
        public long TeamBSlot1Fga, TeamBSlot2Fga, TeamBSlot3Fga, TeamBSlot4Fga, TeamBSlot5Fga;
        // Unattributed slot FGAs: bonus-FT putbacks where Roll E was never called.
        public long TeamASlotUnattributedFga;
        public long TeamBSlotUnattributedFga;
        // Per-slot FGM (Phase 22): aggregate makes per slot per logical team.
        public long TeamASlot1Fgm, TeamASlot2Fgm, TeamASlot3Fgm, TeamASlot4Fgm, TeamASlot5Fgm;
        public long TeamBSlot1Fgm, TeamBSlot2Fgm, TeamBSlot3Fgm, TeamBSlot4Fgm, TeamBSlot5Fgm;
        public long TeamASlotUnattributedFgm;
        public long TeamBSlotUnattributedFgm;
    }

    // ── Per-variant game outcomes for the bucket-7/8 paired diagnostic ────────

    private sealed record GameOutcome(int GameIndex, bool TeamAWon, bool TeamBWon, bool Tied, bool Failed);

    // ── The main stress-test entry point ─────────────────────────────────────

    private static void StressTestArchetypeRosters(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("=== STRESS TEST: Archetype Generator + Multi-Bucket Simulation (4,000 games) ===");
        Console.WriteLine();

        // ── Load configs once ─────────────────────────────────────────────────
        var cfg          = RollAConfig.Load(configPath);
        var cfgB         = RollBConfig.Load(configPath);
        var cfgC         = RollCConfig.Load(configPath);
        var cfgD         = RollDConfig.Load(configPath);
        var cfgE         = RollEConfig.Load(configPath);
        var cfgF         = RollFConfig.Load(configPath);
        var cfgG         = RollGConfig.Load(configPath);
        var cfgH         = RollHConfig.Load(configPath);
        var cfgI         = RollIConfig.Load(configPath);
        var cfgJ         = RollJConfig.Load(configPath);
        var cfgK         = RollKConfig.Load(configPath);
        var cfgL         = RollLConfig.Load(configPath);
        var cfgM         = RollMConfig.Load(configPath);
        var cfgOffFoul   = RollOffensiveFoulConfig.Load(configPath);
        var cfgGov       = GovernorConfig.Load(configPath);
        var cfgClock     = RollClockConfig.Load(configPath);
        var cfgEndOfHalf = EndOfHalfConfig.Load(configPath);
        var cfgMatchup   = MatchupConfig.Load(configPath);

        // ── Hypotheses block — printed before results ─────────────────────────
        Console.WriteLine("--- HYPOTHESES (observe, do not grade) ---");
        Console.WriteLine("  EliteVsWeak: expect Team A win rate substantially above 50%. Magnitude unknown.");
        Console.WriteLine("  ShootingVsAthletic: expect Shooting higher Three%, lower Rim% than Athletic.");
        Console.WriteLine("  AthleticVsSkill (buckets 7+8): DEC-5 physical exponent may produce an athletic edge,");
        Console.WriteLine("    but rosters also differ across many attributes. Reports what engine currently expresses.");
        Console.WriteLine("  StarVsBalanced: PPP SD may be higher or lower than AverageVsAverage.");
        Console.WriteLine("  PassFirstGuard/FloorGeneral: deferred channels (playmaking, IQ) not fully wired;");
        Console.WriteLine("    underperformance is a finding, not a failure.");
        Console.WriteLine("  Mirror gap (buckets 7/8): aggregate win-rate gap near 0% expected.");
        Console.WriteLine("    Individual game disagreement is expected even in a side-neutral engine.");
        Console.WriteLine();

        // ── Failure log ───────────────────────────────────────────────────────
        var failures = new List<string>();

        // ── Role archetypes for star-driven roster ────────────────────────────
        var starRoleArchetypes = new PlayerArchetype[]
        {
            PlayerArchetype.ThreeAndDWing,
            PlayerArchetype.PerimeterShooter,
            PlayerArchetype.RimRunner,
            PlayerArchetype.PassFirstGuard,
        };

        // ── Bucket definitions ────────────────────────────────────────────────
        // Each bucket: (name, buildTeamA, buildTeamB)
        // buildTeamA/B: Func<int variantSeed, string label, Player[]>
        // Buckets 7 and 8 share roster pairs (see §2c logic below)

        // ── Storage for cross-bucket summary table ────────────────────────────
        var summaryRows = new List<(string Name, double WinARate, double TieRate,
            double PppA, double PppB, double FgA, double FgB,
            double ThreePaRateA, double ThreePaRateB,
            double RimShareA, double RimShareB,
            double OrbA, double OrbB,
            double Pace, double TransA, double TransB)>();

        // ── Per-game outcomes for bucket 7 and 8 (paired diagnostic) ─────────
        // Key: variantIndex * 50 + (gameIndex-1) → GameOutcome
        var bucket7Outcomes = new Dictionary<int, GameOutcome>();
        var bucket8Outcomes = new Dictionary<int, GameOutcome>();

        // ── Bucket 7/8 aggregate stats (needed for paired diagnostic) ─────────
        // We'll collect these during bucket runs
        int b7ValidGames = 0, b8ValidGames = 0;
        int b7TeamAWins  = 0, b8TeamAWins  = 0;
        int b7TeamBWins  = 0, b8TeamBWins  = 0;

        // ── Run all 8 buckets ─────────────────────────────────────────────────
        for (var bucketNum = 1; bucketNum <= 8; bucketNum++)
        {
            // Bucket seed base
            // Buckets 7 and 8 both use bucket 7's seed space (§2c)
            int bucketSeedBase = bucketNum == 8 ? 7 * 10_000 : bucketNum * 10_000;

            string bucketName = bucketNum switch
            {
                1 => "AverageVsAverage",
                2 => "EliteVsWeak",
                3 => "EliteVsElite",
                4 => "WeakVsWeak",
                5 => "StarVsBalanced",
                6 => "ShootingVsAthletic",
                7 => "AthleticVsSkill",
                8 => "SkillVsAthletic",
                _ => "Unknown",
            };

            Console.WriteLine($"--- BUCKET {bucketNum}: {bucketName} ---");
            Console.WriteLine($"  Running 10 variants × 50 games ...");

            // Accumulators across all variants for this bucket
            var allVariantStats = new List<VariantStats>();

            // For fingerprint averaging
            var fpA_acc = new double[20];
            var fpB_acc = new double[20];

            // ── Archetype composition for cohort box score labels (fixed per bucket) ──────
            PlayerArchetype[] teamAArchetypes, teamBArchetypes;
            switch (bucketNum)
            {
                case 5: // StarVsBalanced — Slot 1 is the Slasher star; slots 2–5 are starRoleArchetypes
                    teamAArchetypes = new[] { PlayerArchetype.Slasher, starRoleArchetypes[0], starRoleArchetypes[1], starRoleArchetypes[2], starRoleArchetypes[3] };
                    teamBArchetypes = BalancedRoster;
                    break;
                case 6: teamAArchetypes = ShootingRoster; teamBArchetypes = AthleticRoster; break;
                case 7: teamAArchetypes = AthleticRoster; teamBArchetypes = SkillRoster;    break;
                case 8: teamAArchetypes = SkillRoster;    teamBArchetypes = AthleticRoster; break;
                default: teamAArchetypes = BalancedRoster; teamBArchetypes = BalancedRoster; break;
            }

            // ── Per-bucket cohort accumulators (indexed by PlayerId-1; reset per bucket) ──
            var cohortFga    = new long[10]; var cohortFgm    = new long[10];
            var cohortTpa    = new long[10]; var cohortTpm    = new long[10];
            var cohortFta    = new long[10]; var cohortFtm    = new long[10];
            var cohortOReb   = new long[10]; var cohortDReb   = new long[10];
            var cohortBlk    = new long[10]; var cohortStl    = new long[10];
            var cohortTo     = new long[10]; var cohortShFoul = new long[10];
            var cohortAst    = new long[10];

            for (var variantIdx = 0; variantIdx < 10; variantIdx++)
            {
                int variantSeed = bucketSeedBase + variantIdx * 100;

                // ── Build rosters for this variant ─────────────────────────
                Player[] teamAPlayers, teamBPlayers;

                switch (bucketNum)
                {
                    case 1: // AverageVsAverage
                        teamAPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Average, variantSeed,      "TeamA");
                        teamBPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Average, variantSeed + 50, "TeamB");
                        break;
                    case 2: // EliteVsWeak
                        teamAPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Elite, variantSeed,      "TeamA");
                        teamBPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Weak,  variantSeed + 50, "TeamB");
                        break;
                    case 3: // EliteVsElite
                        teamAPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Elite, variantSeed,      "TeamA");
                        teamBPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Elite, variantSeed + 50, "TeamB");
                        break;
                    case 4: // WeakVsWeak
                        teamAPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Weak, variantSeed,      "TeamA");
                        teamBPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Weak, variantSeed + 50, "TeamB");
                        break;
                    case 5: // StarVsBalanced
                        teamAPlayers = BuildStarDrivenRoster(PlayerArchetype.Slasher, starRoleArchetypes, variantSeed, "TeamA");
                        teamBPlayers = BuildArchetypeRoster(BalancedRoster, TalentTier.Good, variantSeed + 50, "TeamB");
                        break;
                    case 6: // ShootingVsAthletic
                        teamAPlayers = BuildArchetypeRoster(ShootingRoster,  TalentTier.Average, variantSeed,      "TeamA");
                        teamBPlayers = BuildArchetypeRoster(AthleticRoster,  TalentTier.Average, variantSeed + 50, "TeamB");
                        break;
                    case 7: // AthleticVsSkill — Athletic=TeamA, Skill=TeamB, shared seeds
                    {
                        var athleticPlayers = BuildArchetypeRoster(AthleticRoster, TalentTier.Average, variantSeed,      "TeamA");
                        var skillPlayers    = BuildArchetypeRoster(SkillRoster,    TalentTier.Average, variantSeed + 50, "TeamB");
                        teamAPlayers = athleticPlayers;
                        teamBPlayers = skillPlayers;
                        break;
                    }
                    case 8: // SkillVsAthletic — Skill=TeamA, Athletic=TeamB, SAME players as bucket 7
                    {
                        // Rebuild from bucket 7's seed space using same seeds — same players
                        var athleticPlayers = BuildArchetypeRoster(AthleticRoster, TalentTier.Average, variantSeed,      "TeamB");
                        var skillPlayers    = BuildArchetypeRoster(SkillRoster,    TalentTier.Average, variantSeed + 50, "TeamA");
                        teamAPlayers = skillPlayers;    // Skill is Team A in bucket 8
                        teamBPlayers = athleticPlayers; // Athletic is Team B in bucket 8
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unknown bucket {bucketNum}");
                }

                // ── Stamp PlayerId by LOGICAL team (Team A → 1–5, Team B → 6–10) ──────────
                // Done once per variant at build time so the ID is stable across the home/away
                // flip. The stamped Player[] arrays are what SeatRoster seats every game (A1, A3).
                for (var si = 0; si < 5; si++) teamAPlayers[si] = StampPlayerId(teamAPlayers[si], si + 1);
                for (var si = 0; si < 5; si++) teamBPlayers[si] = StampPlayerId(teamBPlayers[si], si + 6);

                // ── Per-variant PlayerId contract validation (A2) ─────────────────────────
                {
                    var seenIds = new HashSet<int>();
                    foreach (var p in teamAPlayers) seenIds.Add(p.PlayerId);
                    foreach (var p in teamBPlayers) seenIds.Add(p.PlayerId);
                    var aIds = teamAPlayers.Select(p => p.PlayerId).OrderBy(x => x).ToList();
                    var bIds = teamBPlayers.Select(p => p.PlayerId).OrderBy(x => x).ToList();
                    bool contractOk = seenIds.Count == 10 && seenIds.Min() == 1 && seenIds.Max() == 10
                        && aIds.SequenceEqual(new[] { 1, 2, 3, 4, 5 })
                        && bIds.SequenceEqual(new[] { 6, 7, 8, 9, 10 });
                    if (!contractOk)
                        failures.Add($"B{bucketNum} V{variantIdx}: PlayerId contract violated — A=[{string.Join(",", aIds)}] B=[{string.Join(",", bIds)}]");
                }

                // Per-variant FGA tracker (reset each variant; primary corruption catch — A2)
                var variantFga = new long[10];

                // ── Accumulate fingerprint ─────────────────────────────────
                var fpA = ComputeFingerprint(teamAPlayers);
                var fpB = ComputeFingerprint(teamBPlayers);
                AccumulateFingerprint(fpA_acc, fpA);
                AccumulateFingerprint(fpB_acc, fpB);

                // ── Play 50 games for this variant ─────────────────────────
                var vs = new VariantStats();

                for (var gameIndex = 1; gameIndex <= 50; gameIndex++)
                {
                    int gameSeed = variantSeed + gameIndex;

                    // Physical home/away assignment for this game
                    bool teamAIsHome = gameIndex % 2 == 1;
                    TeamSide teamASide = teamAIsHome ? TeamSide.Home : TeamSide.Away;
                    TeamSide teamBSide = teamAIsHome ? TeamSide.Away : TeamSide.Home;

                    // Build fresh GameState
                    var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                    SeatRoster(game, teamASide, teamAPlayers);
                    SeatRoster(game, teamBSide, teamBPlayers);
                    var resolverRng = new SystemRng(gameSeed);
                    var governorRng = new SystemRng(gameSeed + 1);

                    var resolver = new Resolver(
                        new RollAGenerator(cfg, cfgMatchup, game),
                        cfg,
                        new RollBGenerator(cfgB, cfgMatchup, game),
                        new RollCGenerator(cfgC),
                        cfgC,
                        new RollDGenerator(cfgD),
                        new RollEGenerator(cfgE, game),                    // Phase 19: attribute-driven usage selection
                        new AttentionGenerator(AttentionConfig.Load(configPath), game), // Phase 27: defensive attention pie
                        new RollFGenerator(cfgF, cfgMatchup, game),
                        new RollGGenerator(cfgG, cfgMatchup, game),
                        new RollHGenerator(cfgH, cfgMatchup, game),
                        new RollIGenerator(cfgI, cfgMatchup, game),
                        new RollJGenerator(cfgJ, cfgMatchup, game),
                        new RollKGenerator(cfgK, cfgMatchup, game),
                        new RollLGenerator(cfgL, game),                    // Phase 18: attribute-driven FT make%
                        new RollMGenerator(cfgM, cfgMatchup, game),
                        new RollOffensiveFoulGenerator(cfgOffFoul),
                        cfgMatchup,
                        game,
                        resolverRng);

                    var governor = new Governor(resolver, game, cfgGov, cfgClock, governorRng, cfgEndOfHalf);

                    var firstState = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1);

                    GovernorRunResult result;
                    try
                    {
                        result = governor.Run(firstState);
                    }
                    catch (Exception ex)
                    {
                        var msg = $"B{bucketNum} V{variantIdx} seed={gameSeed}: {ex.Message}";
                        failures.Add(msg);
                        // Record failure in paired diagnostic structures
                        if (bucketNum == 7) bucket7Outcomes[variantIdx * 50 + (gameIndex - 1)] =
                            new GameOutcome(gameIndex, false, false, false, true);
                        if (bucketNum == 8) bucket8Outcomes[variantIdx * 50 + (gameIndex - 1)] =
                            new GameOutcome(gameIndex, false, false, false, true);
                        continue;
                    }

                    var records = result.Possessions;

                    // ── Mechanical checks ──────────────────────────────────
                    var noShot = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.NoShot);

                    // Score reconciliation
                    var recHome = records.Where(r => r.Offense == TeamSide.Home).Sum(r => r.Points);
                    var recAway = records.Where(r => r.Offense == TeamSide.Away).Sum(r => r.Points);
                    if (game.HomeScore != recHome || game.AwayScore != recAway)
                    {
                        failures.Add($"B{bucketNum} V{variantIdx} seed={gameSeed}: score mismatch state=({game.HomeScore},{game.AwayScore}) records=({recHome},{recAway})");
                        continue;
                    }

                    // Zero parks
                    if (result.Parked > 0)
                    {
                        failures.Add($"B{bucketNum} V{variantIdx} seed={gameSeed}: {result.Parked} parked possessions");
                        continue;
                    }

                    // Count invariant
                    if (result.TerminalEnded + result.Parked + noShot != records.Count)
                    {
                        failures.Add($"B{bucketNum} V{variantIdx} seed={gameSeed}: count invariant failed");
                        continue;
                    }

                    // ── Attribution — atomic acceptance boundary ────────────────────────────
                    // AttributeGame must succeed before any accumulator (cohort or team-level)
                    // is touched, and before vs.ValidGames is incremented. A throw here causes
                    // a continue so the game contributes to neither numerator nor denominator.
                    PlayerBoxTotals attributed;
                    try { attributed = AttributeGame(result, game, gameSeed); }
                    catch (Exception attrEx)
                    {
                        failures.Add($"B{bucketNum} V{variantIdx} seed={gameSeed}: AttributeGame failed: {attrEx.Message}");
                        continue;  // game touches neither denominator nor any accumulator
                    }

                    // Accumulate per-variant FGA gate and per-bucket cohort totals.
                    // No continue/failure branch is possible between here and vs.ValidGames++.
                    for (var i = 0; i < 10; i++)
                    {
                        variantFga  [i] += attributed.Fga  [i];
                        cohortFga   [i] += attributed.Fga  [i]; cohortFgm   [i] += attributed.Fgm  [i];
                        cohortTpa   [i] += attributed.Tpa  [i]; cohortTpm   [i] += attributed.Tpm  [i];
                        cohortFta   [i] += attributed.Fta  [i]; cohortFtm   [i] += attributed.Ftm  [i];
                        cohortOReb  [i] += attributed.OReb [i]; cohortDReb  [i] += attributed.DReb [i];
                        cohortBlk   [i] += attributed.Blk  [i]; cohortStl   [i] += attributed.Stl  [i];
                        cohortTo    [i] += attributed.To   [i]; cohortShFoul[i] += attributed.ShFoul[i];
                        cohortAst   [i] += attributed.Ast  [i];
                    }

                    vs.ValidGames++;

                    // ── Logical team scores ────────────────────────────────
                    // Map physical home/away scores to logical team A/B
                    int teamAScore = teamAIsHome ? game.HomeScore : game.AwayScore;
                    int teamBScore = teamAIsHome ? game.AwayScore : game.HomeScore;

                    // Win/tie
                    bool teamAWon  = teamAScore > teamBScore;
                    bool teamBWon  = teamBScore > teamAScore;
                    bool tied      = teamAScore == teamBScore;
                    bool physHomeWon = game.HomeScore > game.AwayScore;

                    if (teamAWon)  vs.TeamAWins++;
                    else if (teamBWon) vs.TeamBWins++;
                    else           vs.Ties++;
                    if (physHomeWon) vs.PhysicalHomeWins++;

                    vs.TeamAScores.Add(teamAScore);
                    vs.TeamBScores.Add(teamBScore);
                    vs.Margins.Add(teamAScore - teamBScore);

                    // Store for paired diagnostic (buckets 7/8)
                    if (bucketNum == 7) bucket7Outcomes[variantIdx * 50 + (gameIndex - 1)] =
                        new GameOutcome(gameIndex, teamAWon, teamBWon, tied, false);
                    if (bucketNum == 8) bucket8Outcomes[variantIdx * 50 + (gameIndex - 1)] =
                        new GameOutcome(gameIndex, teamAWon, teamBWon, tied, false);

                    // ── Per-team possession stats ──────────────────────────
                    // "Team A's offensive possessions" = possessions where offense was teamASide
                    var teamAPoss = records.Count(r => r.Offense == teamASide);
                    var teamBPoss = records.Count(r => r.Offense == teamBSide);
                    var teamAPts  = records.Where(r => r.Offense == teamASide).Sum(r => r.Points);
                    var teamBPts  = records.Where(r => r.Offense == teamBSide).Sum(r => r.Points);

                    vs.TeamAOffPoss += teamAPoss;
                    vs.TeamBOffPoss += teamBPoss;
                    vs.TeamAPoints  += teamAPts;
                    vs.TeamBPoints  += teamBPts;

                    vs.TeamAPPP.Add(teamAPoss > 0 ? (double)teamAPts / teamAPoss : 0.0);
                    vs.TeamBPPP.Add(teamBPoss > 0 ? (double)teamBPts / teamBPoss : 0.0);

                    // FG
                    var aFga = records.Where(r => r.Offense == teamASide).Sum(r => r.Fga);
                    var bFga = records.Where(r => r.Offense == teamBSide).Sum(r => r.Fga);
                    var aFgm = records.Where(r => r.Offense == teamASide).Sum(r => r.Fgm);
                    var bFgm = records.Where(r => r.Offense == teamBSide).Sum(r => r.Fgm);
                    vs.TeamAFga += aFga; vs.TeamBFga += bFga;
                    vs.TeamAFgm += aFgm; vs.TeamBFgm += bFgm;
                    vs.TeamAFgPct.Add(aFga > 0 ? (double)aFgm / aFga : 0.0);
                    vs.TeamBFgPct.Add(bFga > 0 ? (double)bFgm / bFga : 0.0);

                    // 3P
                    var a3pa = records.Where(r => r.Offense == teamASide).Sum(r => r.ThreePa);
                    var b3pa = records.Where(r => r.Offense == teamBSide).Sum(r => r.ThreePa);
                    var a3pm = records.Where(r => r.Offense == teamASide).Sum(r => r.ThreePm);
                    var b3pm = records.Where(r => r.Offense == teamBSide).Sum(r => r.ThreePm);
                    vs.TeamA3pa += a3pa; vs.TeamB3pa += b3pa;
                    vs.TeamA3pm += a3pm; vs.TeamB3pm += b3pm;
                    vs.TeamA3pPct.Add(a3pa > 0 ? (double)a3pm / a3pa : 0.0);
                    vs.TeamB3pPct.Add(b3pa > 0 ? (double)b3pm / b3pa : 0.0);

                    // FT
                    var aFta = records.Where(r => r.Offense == teamASide).Sum(r => r.Fta);
                    var bFta = records.Where(r => r.Offense == teamBSide).Sum(r => r.Fta);
                    var aFtm = records.Where(r => r.Offense == teamASide).Sum(r => r.Ftm);
                    var bFtm = records.Where(r => r.Offense == teamBSide).Sum(r => r.Ftm);
                    vs.TeamAFta += aFta; vs.TeamBFta += bFta;
                    vs.TeamAFtm += aFtm; vs.TeamBFtm += bFtm;
                    vs.TeamAFtPct.Add(aFta > 0 ? (double)aFtm / aFta : 0.0);
                    vs.TeamBFtPct.Add(bFta > 0 ? (double)bFtm / bFta : 0.0);

                    // Zone shot mix (attempts only — shares computed from FGA total)
                    vs.TeamARimA   += records.Where(r => r.Offense == teamASide).Sum(r => r.RimFga);
                    vs.TeamBRimA   += records.Where(r => r.Offense == teamBSide).Sum(r => r.RimFga);
                    vs.TeamAShortA += records.Where(r => r.Offense == teamASide).Sum(r => r.ShortFga);
                    vs.TeamBShortA += records.Where(r => r.Offense == teamBSide).Sum(r => r.ShortFga);
                    vs.TeamAMidA   += records.Where(r => r.Offense == teamASide).Sum(r => r.MidFga);
                    vs.TeamBMidA   += records.Where(r => r.Offense == teamBSide).Sum(r => r.MidFga);
                    vs.TeamALongA  += records.Where(r => r.Offense == teamASide).Sum(r => r.LongFga);
                    vs.TeamBLongA  += records.Where(r => r.Offense == teamBSide).Sum(r => r.LongFga);

                    // Per-slot FGA (usage)
                    vs.TeamASlot1Fga += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot1Fga);
                    vs.TeamBSlot1Fga += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot1Fga);
                    vs.TeamASlot2Fga += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot2Fga);
                    vs.TeamBSlot2Fga += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot2Fga);
                    vs.TeamASlot3Fga += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot3Fga);
                    vs.TeamBSlot3Fga += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot3Fga);
                    vs.TeamASlot4Fga += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot4Fga);
                    vs.TeamBSlot4Fga += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot4Fga);
                    vs.TeamASlot5Fga += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot5Fga);
                    vs.TeamBSlot5Fga += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot5Fga);
                    vs.TeamASlotUnattributedFga += records.Where(r => r.Offense == teamASide).Sum(r => r.SlotUnattributedFga);
                    vs.TeamBSlotUnattributedFga += records.Where(r => r.Offense == teamBSide).Sum(r => r.SlotUnattributedFga);
                    vs.TeamASlot1Fgm += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot1Fgm);
                    vs.TeamBSlot1Fgm += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot1Fgm);
                    vs.TeamASlot2Fgm += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot2Fgm);
                    vs.TeamBSlot2Fgm += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot2Fgm);
                    vs.TeamASlot3Fgm += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot3Fgm);
                    vs.TeamBSlot3Fgm += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot3Fgm);
                    vs.TeamASlot4Fgm += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot4Fgm);
                    vs.TeamBSlot4Fgm += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot4Fgm);
                    vs.TeamASlot5Fgm += records.Where(r => r.Offense == teamASide).Sum(r => r.Slot5Fgm);
                    vs.TeamBSlot5Fgm += records.Where(r => r.Offense == teamBSide).Sum(r => r.Slot5Fgm);
                    vs.TeamASlotUnattributedFgm += records.Where(r => r.Offense == teamASide).Sum(r => r.SlotUnattributedFgm);
                    vs.TeamBSlotUnattributedFgm += records.Where(r => r.Offense == teamBSide).Sum(r => r.SlotUnattributedFgm);

                    // ORB — attribute to the offense on each possession
                    vs.TeamAOrbW += records.Where(r => r.Offense == teamASide).Sum(r => r.OrbWon);
                    vs.TeamBOrbW += records.Where(r => r.Offense == teamBSide).Sum(r => r.OrbWon);
                    vs.TeamAOrbC += records.Where(r => r.Offense == teamASide).Sum(r => r.OrbChances);
                    vs.TeamBOrbC += records.Where(r => r.Offense == teamBSide).Sum(r => r.OrbChances);

                    // Transition frequency per team
                    var transA = records.Count(r => r.Offense == teamASide && r.Entry == EntryType.Transition);
                    var transB = records.Count(r => r.Offense == teamBSide && r.Entry == EntryType.Transition);
                    vs.TeamATransFreq.Add(teamAPoss > 0 ? (double)transA / teamAPoss : 0.0);
                    vs.TeamBTransFreq.Add(teamBPoss > 0 ? (double)transB / teamBPoss : 0.0);
                }   // end game loop

                // ── Per-variant FGA gate (primary PlayerId/seating corruption catch — A2) ──
                // Zero valid games is already captured as a failure upstream; skip per-slot
                // messages in that case to avoid 10 misleading zero-FGA reports.
                if (vs.ValidGames == 0)
                {
                    failures.Add($"B{bucketNum} V{variantIdx}: zero valid games — skipping per-slot FGA check");
                }
                else
                {
                    for (var i = 0; i < 10; i++)
                    {
                        if (variantFga[i] == 0)
                        {
                            var side = i < 5 ? "A" : "B";
                            var slot = i < 5 ? i + 1 : i - 4;
                            failures.Add($"B{bucketNum} V{variantIdx} [{side}]Slot{slot}: zero FGA across " +
                                $"{vs.ValidGames} valid games — indicates probable seating/PlayerId corruption " +
                                "or an unexpectedly unreachable slot; inspect before accepting the run.");
                        }
                    }
                }

                allVariantStats.Add(vs);
            }   // end variant loop

            // ── Aggregate across all 10 variants ──────────────────────────────
            int totalValid  = allVariantStats.Sum(v => v.ValidGames);
            int totalAWins  = allVariantStats.Sum(v => v.TeamAWins);
            int totalBWins  = allVariantStats.Sum(v => v.TeamBWins);
            int totalTies   = allVariantStats.Sum(v => v.Ties);
            int physHomeWins= allVariantStats.Sum(v => v.PhysicalHomeWins);

            // ── Store for paired diagnostic ────────────────────────────────────
            if (bucketNum == 7) { b7ValidGames = totalValid; b7TeamAWins = totalAWins; b7TeamBWins = totalBWins; }
            if (bucketNum == 8) { b8ValidGames = totalValid; b8TeamAWins = totalAWins; b8TeamBWins = totalBWins; }

            // ── Print roster fingerprints (averaged across 10 variants) ────────
            var fpA_mean = DivideFingerprint(fpA_acc, 10);
            var fpB_mean = DivideFingerprint(fpB_acc, 10);
            Console.WriteLine();
            PrintFingerprint("Team A roster profile (mean across 10 variants)", fpA_mean);
            PrintFingerprint("Team B roster profile (mean across 10 variants)", fpB_mean);
            Console.WriteLine();

            // ── Print per-bucket results ──────────────────────────────────────
            Console.WriteLine($"  validGames={totalValid}/500");

            if (totalValid == 0) { Console.WriteLine("  No valid games."); continue; }

            double vg = totalValid;
            double wARt = totalAWins / vg;
            double wBRt = totalBWins / vg;
            double tieRt = totalTies / vg;
            double physHomeRt = physHomeWins / vg;

            Console.WriteLine($"  Team A wins: {totalAWins}/{totalValid} = {wARt:P1}");
            Console.WriteLine($"  Team B wins: {totalBWins}/{totalValid} = {wBRt:P1}");
            Console.WriteLine($"  Ties:        {totalTies}/{totalValid} = {tieRt:P1}");
            Console.WriteLine($"  Physical home win rate: {physHomeRt:P1}  (side-neutrality diagnostic)");

            // Score/margin
            var allAScores = allVariantStats.SelectMany(v => v.TeamAScores).ToList();
            var allBScores = allVariantStats.SelectMany(v => v.TeamBScores).ToList();
            var allMargins = allVariantStats.SelectMany(v => v.Margins).ToList();
            Console.WriteLine($"  Team A score: mean={Mean(allAScores):F1}  sd={Sd(allAScores):F1}  min={allAScores.Min()}  max={allAScores.Max()}");
            Console.WriteLine($"  Team B score: mean={Mean(allBScores):F1}  sd={Sd(allBScores):F1}  min={allBScores.Min()}  max={allBScores.Max()}");
            Console.WriteLine($"  Margin (A-B): mean={Mean(allMargins):F1}  sd={Sd(allMargins):F1}");

            // PPP aggregate
            long aggAOff = allVariantStats.Sum(v => v.TeamAOffPoss);
            long aggBOff = allVariantStats.Sum(v => v.TeamBOffPoss);
            long aggAPts = allVariantStats.Sum(v => v.TeamAPoints);
            long aggBPts = allVariantStats.Sum(v => v.TeamBPoints);
            double aggPppA = aggAOff > 0 ? (double)aggAPts / aggAOff : 0.0;
            double aggPppB = aggBOff > 0 ? (double)aggBPts / aggBOff : 0.0;
            var allAPpp = allVariantStats.SelectMany(v => v.TeamAPPP).ToList();
            var allBPpp = allVariantStats.SelectMany(v => v.TeamBPPP).ToList();
            Console.WriteLine($"  PPP Team A: agg={aggPppA:F3}  mean-game={Mean(allAPpp):F3}  sd={Sd(allAPpp):F3}");
            Console.WriteLine($"  PPP Team B: agg={aggPppB:F3}  mean-game={Mean(allBPpp):F3}  sd={Sd(allBPpp):F3}");

            // FG%
            long aggAFga = allVariantStats.Sum(v => v.TeamAFga);
            long aggBFga = allVariantStats.Sum(v => v.TeamBFga);
            long aggAFgm = allVariantStats.Sum(v => v.TeamAFgm);
            long aggBFgm = allVariantStats.Sum(v => v.TeamBFgm);
            double aggFgA = aggAFga > 0 ? (double)aggAFgm / aggAFga : 0.0;
            double aggFgB = aggBFga > 0 ? (double)aggBFgm / aggBFga : 0.0;
            var allAFg = allVariantStats.SelectMany(v => v.TeamAFgPct).ToList();
            var allBFg = allVariantStats.SelectMany(v => v.TeamBFgPct).ToList();
            Console.WriteLine($"  FG%  Team A: agg={aggFgA:P1}  mean-game={Mean(allAFg):P1}  sd={Sd(allAFg):P1}");
            Console.WriteLine($"  FG%  Team B: agg={aggFgB:P1}  mean-game={Mean(allBFg):P1}  sd={Sd(allBFg):P1}");

            // 3P%
            long aggA3pa = allVariantStats.Sum(v => v.TeamA3pa);
            long aggB3pa = allVariantStats.Sum(v => v.TeamB3pa);
            long aggA3pm = allVariantStats.Sum(v => v.TeamA3pm);
            long aggB3pm = allVariantStats.Sum(v => v.TeamB3pm);
            double agg3pA = aggA3pa > 0 ? (double)aggA3pm / aggA3pa : 0.0;
            double agg3pB = aggB3pa > 0 ? (double)aggB3pm / aggB3pa : 0.0;
            var allA3p = allVariantStats.SelectMany(v => v.TeamA3pPct).ToList();
            var allB3p = allVariantStats.SelectMany(v => v.TeamB3pPct).ToList();
            Console.WriteLine($"  3P%  Team A: agg={agg3pA:P1}  mean-game={Mean(allA3p):P1}  sd={Sd(allA3p):P1}");
            Console.WriteLine($"  3P%  Team B: agg={agg3pB:P1}  mean-game={Mean(allB3p):P1}  sd={Sd(allB3p):P1}");

            // FT%
            long aggAFta = allVariantStats.Sum(v => v.TeamAFta);
            long aggBFta = allVariantStats.Sum(v => v.TeamBFta);
            long aggAFtm = allVariantStats.Sum(v => v.TeamAFtm);
            long aggBFtm = allVariantStats.Sum(v => v.TeamBFtm);
            double aggFtA = aggAFta > 0 ? (double)aggAFtm / aggAFta : 0.0;
            double aggFtB = aggBFta > 0 ? (double)aggBFtm / aggBFta : 0.0;
            var allAFt = allVariantStats.SelectMany(v => v.TeamAFtPct).ToList();
            var allBFt = allVariantStats.SelectMany(v => v.TeamBFtPct).ToList();
            Console.WriteLine($"  FT%  Team A: agg={aggFtA:P1}  mean-game={Mean(allAFt):P1}  sd={Sd(allAFt):P1}");
            Console.WriteLine($"  FT%  Team B: agg={aggFtB:P1}  mean-game={Mean(allBFt):P1}  sd={Sd(allBFt):P1}");
            Console.WriteLine("  NOTE: FT% reflects authored FreeThrow ratings where SelectedSlot is non-null.");
            Console.WriteLine("  Bonus trips before Roll E retain the config.MakeProbability (72%) fallback.");
            Console.WriteLine("  This is a named remaining loose end — not a bug.");

            // 3PA rate (3PA/FGA aggregate) — per team
            double threePaRateA = aggAFga > 0 ? (double)aggA3pa / aggAFga : 0.0;
            double threePaRateB = aggBFga > 0 ? (double)aggB3pa / aggBFga : 0.0;
            Console.WriteLine($"  3PA rate  Team A: {threePaRateA:P1}  Team B: {threePaRateB:P1}");

            // Shot mix (aggregate share of FGA by zone, per team)
            long aggARimA   = allVariantStats.Sum(v => v.TeamARimA);
            long aggBRimA   = allVariantStats.Sum(v => v.TeamBRimA);
            long aggAShortA = allVariantStats.Sum(v => v.TeamAShortA);
            long aggBShortA = allVariantStats.Sum(v => v.TeamBShortA);
            long aggAMidA   = allVariantStats.Sum(v => v.TeamAMidA);
            long aggBMidA   = allVariantStats.Sum(v => v.TeamBMidA);
            long aggALongA  = allVariantStats.Sum(v => v.TeamALongA);
            long aggBLongA  = allVariantStats.Sum(v => v.TeamBLongA);

            double rimShareA   = aggAFga > 0 ? (double)aggARimA   / aggAFga : 0.0;
            double rimShareB   = aggBFga > 0 ? (double)aggBRimA   / aggBFga : 0.0;
            double shortShareA = aggAFga > 0 ? (double)aggAShortA / aggAFga : 0.0;
            double shortShareB = aggBFga > 0 ? (double)aggBShortA / aggBFga : 0.0;
            double midShareA   = aggAFga > 0 ? (double)aggAMidA   / aggAFga : 0.0;
            double midShareB   = aggBFga > 0 ? (double)aggBMidA   / aggBFga : 0.0;
            double longShareA  = aggAFga > 0 ? (double)aggALongA  / aggAFga : 0.0;
            double longShareB  = aggBFga > 0 ? (double)aggBLongA  / aggBFga : 0.0;

            Console.WriteLine($"  Shot mix Team A: Rim={rimShareA:P0} Short={shortShareA:P0} Mid={midShareA:P0} Long={longShareA:P0} Three={threePaRateA:P0}");
            Console.WriteLine($"  Shot mix Team B: Rim={rimShareB:P0} Short={shortShareB:P0} Mid={midShareB:P0} Long={longShareB:P0} Three={threePaRateB:P0}");

            // Slot FGA share (usage)
            long aggASlot1 = allVariantStats.Sum(v => v.TeamASlot1Fga);
            long aggASlot2 = allVariantStats.Sum(v => v.TeamASlot2Fga);
            long aggASlot3 = allVariantStats.Sum(v => v.TeamASlot3Fga);
            long aggASlot4 = allVariantStats.Sum(v => v.TeamASlot4Fga);
            long aggASlot5 = allVariantStats.Sum(v => v.TeamASlot5Fga);
            long aggASlotU = allVariantStats.Sum(v => v.TeamASlotUnattributedFga);
            long aggBSlot1 = allVariantStats.Sum(v => v.TeamBSlot1Fga);
            long aggBSlot2 = allVariantStats.Sum(v => v.TeamBSlot2Fga);
            long aggBSlot3 = allVariantStats.Sum(v => v.TeamBSlot3Fga);
            long aggBSlot4 = allVariantStats.Sum(v => v.TeamBSlot4Fga);
            long aggBSlot5 = allVariantStats.Sum(v => v.TeamBSlot5Fga);
            long aggBSlotU = allVariantStats.Sum(v => v.TeamBSlotUnattributedFga);
            // Aggregate slot-to-FGA reconciliation: slot1+…+slot5+unattributed must equal FGA.
            // Print before shares so a mismatch is visible alongside the slot values.
            var slotSumA = aggASlot1 + aggASlot2 + aggASlot3 + aggASlot4 + aggASlot5 + aggASlotU;
            var slotSumB = aggBSlot1 + aggBSlot2 + aggBSlot3 + aggBSlot4 + aggBSlot5 + aggBSlotU;
            if (slotSumA != aggAFga)
            {
                var msg = $"Bucket {bucketNum} {bucketName}: slot-sum Team A ({slotSumA}) != FGA ({aggAFga})";
                failures.Add(msg);
                Console.WriteLine($"  [FAIL] {msg}");
            }
            if (slotSumB != aggBFga)
            {
                var msg = $"Bucket {bucketNum} {bucketName}: slot-sum Team B ({slotSumB}) != FGA ({aggBFga})";
                failures.Add(msg);
                Console.WriteLine($"  [FAIL] {msg}");
            }
            double slotA1 = aggAFga > 0 ? (double)aggASlot1 / aggAFga : 0.0;
            double slotA2 = aggAFga > 0 ? (double)aggASlot2 / aggAFga : 0.0;
            double slotA3 = aggAFga > 0 ? (double)aggASlot3 / aggAFga : 0.0;
            double slotA4 = aggAFga > 0 ? (double)aggASlot4 / aggAFga : 0.0;
            double slotA5 = aggAFga > 0 ? (double)aggASlot5 / aggAFga : 0.0;
            double slotAU = aggAFga > 0 ? (double)aggASlotU / aggAFga : 0.0;
            double slotB1 = aggBFga > 0 ? (double)aggBSlot1 / aggBFga : 0.0;
            double slotB2 = aggBFga > 0 ? (double)aggBSlot2 / aggBFga : 0.0;
            double slotB3 = aggBFga > 0 ? (double)aggBSlot3 / aggBFga : 0.0;
            double slotB4 = aggBFga > 0 ? (double)aggBSlot4 / aggBFga : 0.0;
            double slotB5 = aggBFga > 0 ? (double)aggBSlot5 / aggBFga : 0.0;
            double slotBU = aggBFga > 0 ? (double)aggBSlotU / aggBFga : 0.0;
            Console.WriteLine($"  Usage Team A: Slot1={slotA1:P1} Slot2={slotA2:P1} Slot3={slotA3:P1} Slot4={slotA4:P1} Slot5={slotA5:P1} Unattr={slotAU:P1}");
            Console.WriteLine($"  Usage Team B: Slot1={slotB1:P1} Slot2={slotB2:P1} Slot3={slotB3:P1} Slot4={slotB4:P1} Slot5={slotB5:P1} Unattr={slotBU:P1}");

            // Slot FGM aggregates + reconciliation (Phase 22)
            long aggASlot1Fgm = allVariantStats.Sum(v => v.TeamASlot1Fgm);
            long aggASlot2Fgm = allVariantStats.Sum(v => v.TeamASlot2Fgm);
            long aggASlot3Fgm = allVariantStats.Sum(v => v.TeamASlot3Fgm);
            long aggASlot4Fgm = allVariantStats.Sum(v => v.TeamASlot4Fgm);
            long aggASlot5Fgm = allVariantStats.Sum(v => v.TeamASlot5Fgm);
            long aggASlotUFgm = allVariantStats.Sum(v => v.TeamASlotUnattributedFgm);
            long aggBSlot1Fgm = allVariantStats.Sum(v => v.TeamBSlot1Fgm);
            long aggBSlot2Fgm = allVariantStats.Sum(v => v.TeamBSlot2Fgm);
            long aggBSlot3Fgm = allVariantStats.Sum(v => v.TeamBSlot3Fgm);
            long aggBSlot4Fgm = allVariantStats.Sum(v => v.TeamBSlot4Fgm);
            long aggBSlot5Fgm = allVariantStats.Sum(v => v.TeamBSlot5Fgm);
            long aggBSlotUFgm = allVariantStats.Sum(v => v.TeamBSlotUnattributedFgm);
            var slotFgmSumA = aggASlot1Fgm + aggASlot2Fgm + aggASlot3Fgm + aggASlot4Fgm + aggASlot5Fgm + aggASlotUFgm;
            var slotFgmSumB = aggBSlot1Fgm + aggBSlot2Fgm + aggBSlot3Fgm + aggBSlot4Fgm + aggBSlot5Fgm + aggBSlotUFgm;
            if (slotFgmSumA != aggAFgm)
            {
                var msg = $"Bucket {bucketNum} {bucketName}: slot-FGM-sum Team A ({slotFgmSumA}) != FGM ({aggAFgm})";
                failures.Add(msg);
                Console.WriteLine($"  [FAIL] {msg}");
            }
            if (slotFgmSumB != aggBFgm)
            {
                var msg = $"Bucket {bucketNum} {bucketName}: slot-FGM-sum Team B ({slotFgmSumB}) != FGM ({aggBFgm})";
                failures.Add(msg);
                Console.WriteLine($"  [FAIL] {msg}");
            }
            // Subset invariant (Phase 22): per-slot FGM <= per-slot FGA (aggregate),
            // and unattributed FGM <= unattributed FGA. Structural — catches slot-level
            // over-crediting that completeness alone would miss. aggASlot1… are the
            // Phase 21 per-slot FGA aggregates; aggASlotU/aggBSlotU the unattributed FGA.
            if (aggASlot1Fgm > aggASlot1 || aggASlot2Fgm > aggASlot2 || aggASlot3Fgm > aggASlot3 ||
                aggASlot4Fgm > aggASlot4 || aggASlot5Fgm > aggASlot5 || aggASlotUFgm > aggASlotU)
            {
                var msg = $"Bucket {bucketNum} {bucketName}: Team A slot FGM>FGA subset violation";
                failures.Add(msg);
                Console.WriteLine($"  [FAIL] {msg}");
            }
            if (aggBSlot1Fgm > aggBSlot1 || aggBSlot2Fgm > aggBSlot2 || aggBSlot3Fgm > aggBSlot3 ||
                aggBSlot4Fgm > aggBSlot4 || aggBSlot5Fgm > aggBSlot5 || aggBSlotUFgm > aggBSlotU)
            {
                var msg = $"Bucket {bucketNum} {bucketName}: Team B slot FGM>FGA subset violation";
                failures.Add(msg);
                Console.WriteLine($"  [FAIL] {msg}");
            }
            // Per-slot FG% = SlotFgm / SlotFga (Phase 21 FGA aggregates as denominators)
            double fgA1 = aggASlot1 > 0 ? (double)aggASlot1Fgm / aggASlot1 : 0.0;
            double fgA2 = aggASlot2 > 0 ? (double)aggASlot2Fgm / aggASlot2 : 0.0;
            double fgA3 = aggASlot3 > 0 ? (double)aggASlot3Fgm / aggASlot3 : 0.0;
            double fgA4 = aggASlot4 > 0 ? (double)aggASlot4Fgm / aggASlot4 : 0.0;
            double fgA5 = aggASlot5 > 0 ? (double)aggASlot5Fgm / aggASlot5 : 0.0;
            double fgB1 = aggBSlot1 > 0 ? (double)aggBSlot1Fgm / aggBSlot1 : 0.0;
            double fgB2 = aggBSlot2 > 0 ? (double)aggBSlot2Fgm / aggBSlot2 : 0.0;
            double fgB3 = aggBSlot3 > 0 ? (double)aggBSlot3Fgm / aggBSlot3 : 0.0;
            double fgB4 = aggBSlot4 > 0 ? (double)aggBSlot4Fgm / aggBSlot4 : 0.0;
            double fgB5 = aggBSlot5 > 0 ? (double)aggBSlot5Fgm / aggBSlot5 : 0.0;
            Console.WriteLine($"  Slot FG% Team A: Slot1={fgA1:P1} Slot2={fgA2:P1} Slot3={fgA3:P1} Slot4={fgA4:P1} Slot5={fgA5:P1}");
            Console.WriteLine($"  Slot FG% Team B: Slot1={fgB1:P1} Slot2={fgB2:P1} Slot3={fgB3:P1} Slot4={fgB4:P1} Slot5={fgB5:P1}");            // ORB%
            long aggAOrbW = allVariantStats.Sum(v => v.TeamAOrbW);
            long aggBOrbW = allVariantStats.Sum(v => v.TeamBOrbW);
            long aggAOrbC = allVariantStats.Sum(v => v.TeamAOrbC);
            long aggBOrbC = allVariantStats.Sum(v => v.TeamBOrbC);
            double orbA = aggAOrbC > 0 ? (double)aggAOrbW / aggAOrbC : 0.0;
            double orbB = aggBOrbC > 0 ? (double)aggBOrbW / aggBOrbC : 0.0;
            Console.WriteLine($"  ORB%  Team A: {orbA:P1}  Team B: {orbB:P1}");

            // FTr (FTA/FGA aggregate, per team)
            double ftrA = aggAFga > 0 ? (double)aggAFta / aggAFga : 0.0;
            double ftrB = aggBFga > 0 ? (double)aggBFta / aggBFga : 0.0;
            Console.WriteLine($"  FTr   Team A: {ftrA:F3}  Team B: {ftrB:F3}");

            // Transition frequency
            var allATransFreq = allVariantStats.SelectMany(v => v.TeamATransFreq).ToList();
            var allBTransFreq = allVariantStats.SelectMany(v => v.TeamBTransFreq).ToList();
            // Pace = mean total possessions per game (both teams)
            double pace = (aggAOff + aggBOff) / (double)Math.Max(1, totalValid);
            Console.WriteLine($"  Transition Team A: {Mean(allATransFreq):P1}  Team B: {Mean(allBTransFreq):P1}");
            Console.WriteLine($"  Pace (total poss/game): {pace:F1}");

            // Between-variant diagnostic (per-variant win rates and PPP ranges)
            var variantWinRatesA = allVariantStats.Select(v =>
                v.ValidGames > 0 ? (double)v.TeamAWins / v.ValidGames : 0.0).ToList();
            var variantPppB = allVariantStats.SelectMany(v => v.TeamBPPP).ToList();
            // Per-variant means for Team B PPP
            var variantMeanPppB = allVariantStats.Select(v =>
                v.TeamBPPP.Count > 0 ? v.TeamBPPP.Average() : 0.0).ToList();
            Console.WriteLine($"  Between-variant:  Team A win-rate range: min={variantWinRatesA.Min():P0}  max={variantWinRatesA.Max():P0}");
            Console.WriteLine($"  Between-variant:  Team B PPP range: min={variantMeanPppB.Min():F3}  max={variantMeanPppB.Max():F3}");

            Console.WriteLine();

            // ── Bucket-level no-zero FGA gate (redundant outer protection — A2) ─────────
            for (var i = 0; i < 10; i++)
            {
                if (cohortFga[i] == 0)
                {
                    var side = i < 5 ? "A" : "B";
                    var slot = i < 5 ? i + 1 : i - 4;
                    var msg  = $"Bucket {bucketNum} {bucketName}: [{side}]Slot{slot} has 0 FGA across {totalValid} games — PlayerId wiring issue";
                    failures.Add(msg);
                    Console.WriteLine($"  [FAIL] {msg}");
                }
            }

            // ── Slot/archetype cohort box score (per-game averages over bucket valid games) ─
            if (totalValid > 0)
            {
                Console.WriteLine($"=== COHORT BOX SCORE — Bucket {bucketNum}: {bucketName} ===");
                Console.WriteLine("  Rows pool 10 generated roster variants. Each row is the per-game average");
                Console.WriteLine($"  for one logical slot/archetype cohort, not one persistent named player. ({totalValid} accepted games)");
                if (bucketNum == 5)
                    Console.WriteLine("  Note: Team A Slot 1 is an Elite-tier Slasher star; all other Team A slots are Good tier.");
                Console.WriteLine($"  {"Row",-22} {"PTS",5} {"FGA",5} {"FGM",5} {"FG%",5} {"3PA",5} {"3PM",5} {"3P%",5} {"FTA",5} {"FTM",5} {"FT%",5} {"ORB",5} {"DRB",5} {"REB",5} {"STL",5} {"BLK",5} {"AST",5} {"TO",5} {"SFL",5}");
                Console.WriteLine(new string('─', 121));
                double g = totalValid;
                for (var i = 0; i < 10; i++)
                {
                    var side      = i < 5 ? "A" : "B";
                    var slotNum   = i < 5 ? i + 1 : i - 4;
                    var archetype = i < 5 ? teamAArchetypes[i] : teamBArchetypes[i - 5];
                    var label     = $"[{side}] Slot{slotNum} — {archetype}";

                    var fga  = cohortFga  [i] / g; var fgm  = cohortFgm  [i] / g;
                    var tpa  = cohortTpa  [i] / g; var tpm  = cohortTpm  [i] / g;
                    var fta  = cohortFta  [i] / g; var ftm  = cohortFtm  [i] / g;
                    var orb  = cohortOReb [i] / g; var drb  = cohortDReb [i] / g;
                    var blk  = cohortBlk  [i] / g; var stl  = cohortStl  [i] / g;
                    var to   = cohortTo   [i] / g; var sfl  = cohortShFoul[i] / g;
                    var ast  = cohortAst  [i] / g;
                    var pts  = (fgm - tpm) * 2.0 + tpm * 3.0 + ftm;
                    var fgPct = fga > 0 ? fgm / fga * 100 : 0.0;
                    var tpPct = tpa > 0 ? tpm / tpa * 100 : 0.0;
                    var ftPct = fta > 0 ? ftm / fta * 100 : 0.0;
                    Console.WriteLine(
                        $"  {label,-22} {pts,5:F1} {fga,5:F1} {fgm,5:F1} {fgPct,5:F1} " +
                        $"{tpa,5:F1} {tpm,5:F1} {tpPct,5:F1} {fta,5:F1} {ftm,5:F1} {ftPct,5:F1} " +
                        $"{orb,5:F1} {drb,5:F1} {(orb+drb),5:F1} {stl,5:F1} {blk,5:F1} {ast,5:F1} {to,5:F1} {sfl,5:F1}");
                }
                Console.WriteLine($"=== END COHORT BOX SCORE — Bucket {bucketNum} ===");
                Console.WriteLine();
            }

            // ── Accumulate summary row ─────────────────────────────────────────
            summaryRows.Add((bucketName,
                wARt, tieRt,
                aggPppA, aggPppB,
                aggFgA, aggFgB,
                threePaRateA, threePaRateB,
                rimShareA, rimShareB,
                orbA, orbB,
                pace,
                Mean(allATransFreq), Mean(allBTransFreq)));
        }   // end bucket loop

        // ── Buckets 7/8 paired diagnostic ─────────────────────────────────────
        Console.WriteLine("--- BUCKETS 7/8 PAIRED DIAGNOSTIC ---");

        double b7WinRateAthletic = b7ValidGames > 0 ? (double)b7TeamAWins / b7ValidGames : 0.0;
        double b8WinRateAthletic = b8ValidGames > 0 ? (double)b8TeamBWins / b8ValidGames : 0.0;
        double mirrorGap = Math.Abs(b7WinRateAthletic - b8WinRateAthletic);

        Console.WriteLine($"  Athletic paired win rate:");
        Console.WriteLine($"    As Team A (Bucket 7): {b7WinRateAthletic:P1}   (= Bucket 7 Team A wins / Bucket 7 validGames)");
        Console.WriteLine($"    As Team B (Bucket 8): {b8WinRateAthletic:P1}   (= Bucket 8 Team B wins / Bucket 8 validGames)");
        Console.WriteLine($"    NOTE: Bucket 8 Athletic win rate computed directly from Team B wins — NOT 1 − TeamAWinRate.");
        Console.WriteLine($"  Mirror gap: abs(B7_AthleticWinRate − B8_AthleticWinRate) = {mirrorGap:P1}");
        Console.WriteLine($"  (A large gap suggests physical-side asymmetry, seed-stream sensitivity, or remapping error;");
        Console.WriteLine($"   it does not by itself identify which cause is responsible.)");

        // Paired game outcome agreement
        int pairedValid      = 0;
        int mirrored         = 0;
        int disagreement     = 0;
        int unpairedFailures = 0;

        for (var variantIdx = 0; variantIdx < 10; variantIdx++)
        {
            for (var gameIndex = 1; gameIndex <= 50; gameIndex++)
            {
                int key = variantIdx * 50 + (gameIndex - 1);
                bool has7 = bucket7Outcomes.TryGetValue(key, out var g7);
                bool has8 = bucket8Outcomes.TryGetValue(key, out var g8);

                if (!has7 || !has8)                      { unpairedFailures++; continue; }
                if (g7!.Failed || g8!.Failed)             { unpairedFailures++; continue; }

                pairedValid++;

                // In bucket 7: Athletic = Team A.  In bucket 8: Athletic = Team B.
                // "Athletic won" in bucket 7 means TeamAWon.
                // "Athletic won" in bucket 8 means TeamBWon (NOT TeamAWon).
                bool athleticWon7  = g7.TeamAWon;
                bool athleticWon8  = g8.TeamBWon;
                bool skillWon7     = g7.TeamBWon;
                bool skillWon8     = g8.TeamAWon;
                bool tied7         = g7.Tied;
                bool tied8         = g8.Tied;

                // Mirrored: same team wins both, or both tie
                bool sameMirror =
                    (athleticWon7 && athleticWon8) ||
                    (skillWon7    && skillWon8)    ||
                    (tied7        && tied8);

                if (sameMirror) mirrored++;
                else            disagreement++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Paired-game outcome agreement (same game seed, rosters swapped):");
        Console.WriteLine($"    Mirrored result (Athletic wins both, Skill wins both, or both tie): {mirrored} / {pairedValid}");
        Console.WriteLine($"    Disagreement (changing presentation changes the winner):             {disagreement} / {pairedValid}");
        Console.WriteLine($"    Unpaired failures (one bucket game failed, no match):                {unpairedFailures}");
        Console.WriteLine($"    (pairedValidGames = games where both Bucket 7 and Bucket 8 game N succeeded)");
        Console.WriteLine($"    (Disagreement count is a sensitivity diagnostic, not a pass/fail condition.)");
        Console.WriteLine($"  Interpretation: a large mirror gap or high disagreement count suggests physical-side");
        Console.WriteLine($"    asymmetry, seed-stream sensitivity, or an incorrect logical remapping.");
        Console.WriteLine();

        // ── Cross-bucket summary table ────────────────────────────────────────
        Console.WriteLine("--- CROSS-BUCKET SUMMARY TABLE ---");
        Console.WriteLine($"  {"Bucket",-22} {"WinA%",6} {"Ties%",6} {"PPP_A",6} {"PPP_B",6} {"FG%_A",6} {"FG%_B",6} {"3PA%A",6} {"3PA%B",6} {"Rim%A",6} {"Rim%B",6} {"ORB%A",6} {"ORB%B",6} {"Pace",5} {"Tr%A",5} {"Tr%B",5}");
        foreach (var r in summaryRows)
        {
            Console.WriteLine($"  {r.Name,-22} {r.WinARate,6:P0} {r.TieRate,6:P0} {r.PppA,6:F3} {r.PppB,6:F3} {r.FgA,6:P0} {r.FgB,6:P0} {r.ThreePaRateA,6:P0} {r.ThreePaRateB,6:P0} {r.RimShareA,6:P0} {r.RimShareB,6:P0} {r.OrbA,6:P0} {r.OrbB,6:P0} {r.Pace,5:F0} {r.TransA,5:P0} {r.TransB,5:P0}");
        }
        Console.WriteLine();

        // ── Final verdict ─────────────────────────────────────────────────────
        if (failures.Count == 0)
        {
            Console.WriteLine("STRESS TEST PASSED");
        }
        else
        {
            Console.WriteLine($"STRESS TEST FAILED — {failures.Count} failure(s):");
            foreach (var f in failures)
                Console.WriteLine($"  {f}");
        }
        Console.WriteLine("=== END STRESS TEST ===");
    }

    // ── Stress-test helpers ────────────────────────────────────────────────────

    private static double Mean(List<int>    v) => v.Count > 0 ? v.Average()                                         : 0.0;
    private static double Mean(List<double> v) => v.Count > 0 ? v.Average()                                         : 0.0;
    private static double Sd(List<int>      v) { if (v.Count < 2) return 0.0; var m = v.Average(); return Math.Sqrt(v.Average(x => (x - m) * (x - m))); }
    private static double Sd(List<double>   v) { if (v.Count < 2) return 0.0; var m = v.Average(); return Math.Sqrt(v.Average(x => (x - m) * (x - m))); }

    // Fingerprint accumulation helpers (indexed array matching RosterFingerprint field order)
    private static void AccumulateFingerprint(double[] acc, RosterFingerprint fp)
    {
        acc[0]  += fp.Outside;  acc[1]  += fp.Finishing; acc[2]  += fp.Mid;     acc[3]  += fp.Close;
        acc[4]  += fp.SelfCreation; acc[5] += fp.Speed;  acc[6]  += fp.Quickness; acc[7] += fp.Vertical;
        acc[8]  += fp.Strength; acc[9]  += fp.Height;    acc[10] += fp.Wingspan;
        acc[11] += fp.Passing;  acc[12] += fp.Playmaking; acc[13] += fp.BallHandling; acc[14] += fp.BasketballIQ;
        acc[15] += fp.PerimeterDefense; acc[16] += fp.PostDefense; acc[17] += fp.RimProtection;
        acc[18] += fp.OffensiveRebounding; acc[19] += fp.DefensiveRebounding;
    }

    private static RosterFingerprint DivideFingerprint(double[] acc, int n)
    {
        double D(int i) => acc[i] / n;
        return new RosterFingerprint(
            Outside:D(0), Finishing:D(1), Mid:D(2), Close:D(3), SelfCreation:D(4),
            Speed:D(5), Quickness:D(6), Vertical:D(7), Strength:D(8),
            Height:D(9), Wingspan:D(10),
            Passing:D(11), Playmaking:D(12), BallHandling:D(13), BasketballIQ:D(14),
            PerimeterDefense:D(15), PostDefense:D(16), RimProtection:D(17),
            OffensiveRebounding:D(18), DefensiveRebounding:D(19));
    }

}
