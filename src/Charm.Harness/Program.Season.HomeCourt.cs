using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  HOME COURT (S95) — the road penalty.
//
//  THE RULINGS THIS IMPLEMENTS (Emmett, closed):
//    1. "All of it, a little — death by a thousand cuts." Every contested pie the
//       road team touches leans a hair against them; no single door carries it.
//    2. "It needs to be a flat home court advantage." One dial; every real home
//       floor worth the same. Prestige/proximity/semi-home is DEFERRED to the
//       tournament layer, which is where a neutral or semi-home site fact will live.
//    3. A ROAD PENALTY, not a home boost. A floor with no host tilts NOBODY —
//       so the home side is handed to the engine as the very same object it
//       arrived as, never a copy, and a neutral game transforms neither side.
//    4. Skills, not bodies (2026-08-03). The road costs a man his touch, his hands,
//       his reads and his restraint. It never costs him his body or his effort.
//    5. Calibrated and ruled at 3 (2026-08-03) — 58.74% home wins across three full
//       stock seasons against a 50.08% control at zero shave. The measurement and
//       why it is not 2 live on HomeCourtConfig; this file holds no number.
//
//  ── Why the ratings and not the pies ────────────────────────────────────────
//  There is no layer in this engine between a man's ratings and the pie: every
//  contested door is built by reading the two men against each other, right then.
//  So shaving the ratings is not an ALTERNATIVE to leaning the pies — it is how you
//  lean them, with one dial instead of thirteen, each door leaning by exactly as
//  much as it actually reads the ratings involved.
//
//  ── The exempt set is the design, not an optimisation ───────────────────────
//  Seventeen of the forty numbered ratings are untouched, and the reason differs
//  by group:
//    • Height, Wingspan, Weight        — body facts. A man is the same size away.
//    • Strength, Speed, Quickness,
//      FirstStep, Vertical, Endurance  — the six physicals. Because ALL SIX are
//      exempt, `Player.Athleticism` (their mean, computed on read) does not move
//      one thousandth on the road. That is the ruling's teeth, and Phase 86 B4
//      asserts it as exact equality rather than trusting it.
//    • Hustle                          — effort travels. Emmett's call: a man
//      competes exactly as hard in a hostile gym.
//    • The five shot tendencies        — WHERE a man shoots from is identity. The
//      odds lean; the diet stays his.
//    • PlayerId, HierarchyRank         — bookkeeping, not performance. PlayerId in
//      particular MUST survive: the whole stat layer resolves a person by it.
//
//  Gravity and Spacing DO come down a hair, and that is the shave landing where it
//  was sent rather than leaking: both are computed on read from shooting and
//  scoring ratings (Outside, Mid, Close, Finishing) that are shaved by design. A
//  road shooter draws slightly less respect because he is slightly colder.
//
//  ── Scope honesty: the host fact does not exist yet ─────────────────────────
//  A scheduled game carries a home id and an away id and nothing else — there is
//  no host/no-host field to read, and this session does not invent one. The
//  applicator therefore takes an EXPLICIT `hasHost` flag and the season loop passes
//  `true` literally, because every game on today's schedule is a real home game.
//  When the tournament layer brings a schedule-owned site fact, that fact becomes
//  the flag's source and the page denominator narrows with it. Nothing here
//  pretends to filter today.
// ============================================================================

internal static partial class Program
{
    /// <summary>The ONE runtime copy of the shaved classification: the twenty-three
    /// ratings a road player loses two points of. Everything not named here is untouched.
    ///
    /// <para>★ Phase 86 B3 does NOT echo this list. It reflects the live public int
    /// surface of <see cref="Player"/>, spells the SEVENTEEN EXEMPT names independently,
    /// derives the expected shaved set by subtraction, and asserts this set equals it.
    /// That makes the check a correctness test of the classification rather than a
    /// consistency test of one list against a copy of itself — and it means a rating
    /// ADDED to Player in some future session lands, loudly, on one side or the other
    /// instead of being silently forgotten.</para></summary>
    private static readonly ImmutableHashSet<string> ShavedRatingNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, new[]
        {
        // Offense — the skills
        "Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
        "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
        "OffBallMovement", "Screening", "OffensiveRebounding",
        // Defense — the skills
        "PerimeterDefense", "PostDefense", "RimProtection", "DefensiveRebounding",
        "Steals", "HelpDefense", "OffBallDefense",
        // Intangible — the two that are reads and restraint, not effort
        "BasketballIQ", "Discipline",
        });

    /// <summary>One rating, shaved and floored. The floor is why the dial needs no upper
    /// bound: an oversized shave degrades into "the road team is replacement level"
    /// rather than into ratings the engine's own validator would refuse.</summary>
    private static int Shave(int rating, int by) => Math.Max(0, rating - by);

    /// <summary>Return a new Player identical to <paramref name="p"/> except that each of
    /// the twenty-three shaved ratings is <paramref name="roadShave"/> points lower,
    /// floored at 0. Player is a sealed class, not a record, so `with` is unavailable and
    /// every property is carried explicitly — the same shape as
    /// <see cref="StampPlayerId"/>, which is the sibling this is pattern-matched from.
    ///
    /// <para>★ ONE DELIBERATE DIVERGENCE FROM THAT SIBLING, named here because it is
    /// exactly the kind of quiet difference that costs a later session an afternoon:
    /// `StampPlayerId` does not carry the development state (LatentSkills, CurrentSkills,
    /// Runway, Arrival, PlayerClass), so a season player has already lost it long before
    /// this method ever sees him and preserving it changes nothing that is played. It is
    /// preserved anyway, because a cloning factory that silently drops state is a trap,
    /// and because Phase 86 B4 constructs a fully populated probe and checks it. The maps
    /// are carried BY REFERENCE: they are read-only development metadata that no roll
    /// touches (PlayerGenPass3 is their only reader), so copying the dictionaries would
    /// be allocation for its own sake.</para>
    ///
    /// <para>The source object is never written to — every property here is read, none
    /// assigned. B4 asserts the source is unchanged afterwards rather than trusting
    /// that.</para></summary>
    private static Player RoadShavedPlayer(Player p, int roadShave) => new Player(p.Name)
    {
        // ── Bookkeeping: untouched. PlayerId especially — the entire stat layer
        //    resolves a person by it, so a shaved man must still BE the same man.
        PlayerId            = p.PlayerId,
        HierarchyRank       = p.HierarchyRank,

        // ── Offense: the skills, shaved.
        Close               = Shave(p.Close,               roadShave),
        Mid                 = Shave(p.Mid,                 roadShave),
        Outside             = Shave(p.Outside,             roadShave),
        Finishing           = Shave(p.Finishing,           roadShave),
        FreeThrow           = Shave(p.FreeThrow,           roadShave),
        FoulDrawing         = Shave(p.FoulDrawing,         roadShave),
        BallHandling        = Shave(p.BallHandling,        roadShave),
        Passing             = Shave(p.Passing,             roadShave),
        Playmaking          = Shave(p.Playmaking,          roadShave),
        SelfCreation        = Shave(p.SelfCreation,        roadShave),
        PostMoves           = Shave(p.PostMoves,           roadShave),
        OffBallMovement     = Shave(p.OffBallMovement,     roadShave),
        Screening           = Shave(p.Screening,           roadShave),
        OffensiveRebounding = Shave(p.OffensiveRebounding, roadShave),

        // ── Defense: the skills, shaved.
        PerimeterDefense    = Shave(p.PerimeterDefense,    roadShave),
        PostDefense         = Shave(p.PostDefense,         roadShave),
        RimProtection       = Shave(p.RimProtection,       roadShave),
        DefensiveRebounding = Shave(p.DefensiveRebounding, roadShave),
        Steals              = Shave(p.Steals,              roadShave),
        HelpDefense         = Shave(p.HelpDefense,         roadShave),
        OffBallDefense      = Shave(p.OffBallDefense,      roadShave),

        // ── Intangible: reads and restraint shaved; EFFORT IS NOT.
        //    Discipline is the strongest single case in the whole set — the most real
        //    home-court effect in college basketball is the whistle, and Discipline is
        //    the dial that decides who gets called for the reach-in.
        BasketballIQ        = Shave(p.BasketballIQ,        roadShave),
        Discipline          = Shave(p.Discipline,          roadShave),
        Hustle              = p.Hustle,

        // ── The shot diet: WHERE he shoots from is identity, not form.
        RimTendency         = p.RimTendency,
        ShortTendency       = p.ShortTendency,
        MidTendency         = p.MidTendency,
        LongTendency        = p.LongTendency,
        ThreeTendency       = p.ThreeTendency,

        // ── The body: all nine untouched, which is what keeps Player.Athleticism
        //    (the mean of the six physicals) EXACTLY equal on the road.
        Height              = p.Height,
        Wingspan            = p.Wingspan,
        Weight              = p.Weight,
        Strength            = p.Strength,
        Speed               = p.Speed,
        Quickness           = p.Quickness,
        FirstStep           = p.FirstStep,
        Vertical            = p.Vertical,
        Endurance           = p.Endurance,

        // ── Development state: carried, unlike the sibling. See the note above.
        LatentSkills        = p.LatentSkills,
        CurrentSkills       = p.CurrentSkills,
        Runway              = p.Runway,
        Arrival             = p.Arrival,
        PlayerClass         = p.PlayerClass,
    };

    /// <summary>Transform one side for a road game: EVERY rostered player — starters and
    /// bench alike — replaced by his shaved clone, so every five-man lineup the road team
    /// can field carries the identical flat penalty and a coach cannot substitute his way
    /// out of it.
    ///
    /// <para>Two paths return the ORIGINAL REFERENCE, and both are load-bearing rather
    /// than micro-optimisation. No host means nobody is on the road, which is ruling 3.
    /// A zero shave means the dial is off, and returning the original object is what
    /// makes zero bit-for-bit the pre-S95 engine — the property Phase 86 B1 proves
    /// against a fingerprint captured from the pre-S95 tree.</para>
    ///
    /// <para>The four non-Player fields (StarterPositions, StarterRanks,
    /// ReservePositions, ReserveRanks — verified as the record's COMPLETE non-Player
    /// surface) are carried as the same references in the same order. Nothing here writes
    /// to the source side or to any collection it holds; B4 and B5 assert source
    /// preservation rather than trusting the absence of an assignment.</para></summary>
    private static GenSideData ApplyRoadShave(GenSideData side, int roadShave, bool hasHost)
    {
        if (!hasHost || roadShave == 0) return side;

        var starters = new Player[side.Starters.Length];
        for (var i = 0; i < side.Starters.Length; i++)
            starters[i] = RoadShavedPlayer(side.Starters[i], roadShave);

        var reserves = new Player[side.Reserves.Length];
        for (var i = 0; i < side.Reserves.Length; i++)
            reserves[i] = RoadShavedPlayer(side.Reserves[i], roadShave);

        return new GenSideData(
            starters, side.StarterPositions, side.StarterRanks,
            reserves, side.ReservePositions, side.ReserveRanks);
    }

    /// <summary>The observation seam. Prepares both sides for one game and reports whether
    /// the away side was actually transformed.
    ///
    /// <para>★ Why this exists as a named helper rather than two inline calls: it gives
    /// Phase 86 something to TEST. The alternative — a test-only global counter the
    /// production loop increments — would make the check an echo of an instrument instead
    /// of an assertion about the contract. The season loop builds its game from this
    /// tuple's Home and Away directly, so what B6 tests is what plays.</para>
    ///
    /// <para>Ruling 3, stated as a signature: `home` comes back as itself, always. There
    /// is no path through this method that copies the home side.</para></summary>
    private static (GenSideData Home, GenSideData Away, bool AwayShaved)
        PrepareSeasonGameSides(GenSideData home, GenSideData away, int roadShave, bool hasHost)
    {
        var shavedAway = ApplyRoadShave(away, roadShave, hasHost);
        return (home, shavedAway, !ReferenceEquals(shavedAway, away));
    }

    // ── The season fingerprint (S95) ──────────────────────────────────────────
    //
    //  ONE helper, used by BOTH the golden capture and Phase 86's B1/B7. A hash
    //  captured on one machine must verify on another, so the serialization is never
    //  hand-reproduced anywhere: every producer of this string calls this method.
    //
    //  Shape mirrors `ScheduleFingerprint` deliberately (§1 sibling rule): ordered
    //  lines, '|' between fields, '\n' terminator, invariant culture, UTF-8 WITHOUT a
    //  BOM (`Encoding.UTF8.GetBytes` emits no preamble), SHA-256, lowercase hex.
    //
    //  ★ Why the possession count is in the line at all. Scores alone would let a
    //  change that reshapes a game's INTERNALS while landing on the same final score
    //  slip through. The count is the authoritative per-game possession-record total
    //  the season accumulator already uses.
    //
    //  ★ Why a game-level fingerprint is SUFFICIENT here, proven not assumed: in
    //  `RunSeasonCore` both sides are rebuilt from the per-school row tables for every
    //  game, each game's two seeds are derived from the season seed alone
    //  (`baseSeed + 2g`, `baseSeed + 2g + 1`), and no accumulator feeds later play.
    //  Season game execution is therefore independent game to game, so reproducing
    //  every game's result reproduces the season.
    private static string SeasonFingerprint(
        IReadOnlyList<SeasonGameResult> results, IReadOnlyList<int> possessionCounts)
    {
        if (results.Count != possessionCounts.Count)
            throw new InvalidOperationException(
                $"S95 fingerprint: {results.Count} results but {possessionCounts.Count} " +
                "possession counts — the season loop must append to both per game.");

        var sb = new StringBuilder();
        for (var i = 0; i < results.Count; i++)
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(results[i].HomeId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(results[i].AwayId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(results[i].HomeScore.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(results[i].AwayScore.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(possessionCounts[i].ToString(CultureInfo.InvariantCulture)).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
