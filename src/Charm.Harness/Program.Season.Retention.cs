using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S90 — WHERE A RETAINED GAME ROW IS BORN.
//
//  ★ SNAPSHOT AND DIFF, not a second accumulator. The season record is the one
//  place attribution happens; a per-game row is that record's DELTA across one
//  game. The rejected alternative was a parallel accumulator building rows
//  independently — which is precisely how the game log and the season line drift
//  apart, because two implementations of one rule get to disagree.
//
//  ★ THE BOUNDARY IS THE WHOLE LOOP ITERATION, AND THE ORDER MATTERS. The season
//  loop calls Accumulate -> AccumulateFouling -> NoteOccupancy. The box fields
//  land in the FIRST call; credits, games played and the four on-floor
//  denominators land in the THIRD. A boundary drawn between them would silently
//  drop every counter of one kind and every check would still pass, because
//  conservation only ever compares the fields that exist.
//
//  ★ 26 MEN, NOT 4,511. Only this game's two rosters can move, and that is
//  provable rather than assumed: both writes to a season record go through
//  `RecordFor`, whose only two callers resolve through `SeasonGameIdentity`,
//  which indexes THIS game's home and away rows and throws outside the stamped
//  range. Snapshotting the league instead would copy 4,511 records 5,205 times a
//  season for nothing. Phase 81 A11 asserts the bound rather than trusting this
//  paragraph.
//
//  ★ GAMES PLAYED IS THE EMISSION CRITERION, and it is never inferred. The
//  season record's own delta decides whether a man gets a row — not a credits
//  delta read after the fact, which would be a second definition of "played"
//  sitting beside the real one.
// ============================================================================

internal static partial class Program
{
    /// <summary>One man's counters at an instant. Twenty-two numbers: the twenty-one
    /// retained counters plus games played, which is the emission criterion and is NOT
    /// itself stored (a man played the games he has rows for — storing it too would
    /// create a second copy of one fact, and two copies can disagree).</summary>
    private readonly record struct RetentionSnapshot(
        long GamesPlayed, long Credits, long OffensiveCredits,
        long Fga, long Fgm, long Tpa, long Tpm, long Fta, long Ftm,
        long OReb, long DReb, long Ast, long Stl, long Blk, long To,
        long ShFoul, long NsFoul, long OffFoul, long FbBlk,
        long OpponentTwoPaOnFloor, long SecuredBoardsOnFloor, long OffensiveTeamFgmOnFloor)
    {
        internal static readonly RetentionSnapshot Zero = new();

        internal static RetentionSnapshot Of(SeasonPlayerRecord r) => new(
            r.GamesPlayed, r.Credits, r.OffensiveCredits,
            r.Fga, r.Fgm, r.Tpa, r.Tpm, r.Fta, r.Ftm,
            r.OReb, r.DReb, r.Ast, r.Stl, r.Blk, r.To,
            r.ShFoul, r.NsFoul, r.OffFoul, r.FbBlk,
            r.OpponentTwoPaOnFloor, r.SecuredBoardsOnFloor, r.OffensiveTeamFgmOnFloor);
    }

    /// <summary>The men whose season records this game is allowed to touch, captured
    /// before the accumulators run. Keyed by pool id, which is what the record is keyed
    /// by; a man with no record yet snapshots as all-zero, which is the truth.</summary>
    private static Dictionary<int, RetentionSnapshot> RetentionSnapshotBefore(
        SeasonLeagueStats league, SeasonGameIdentity identity)
    {
        var before = new Dictionary<int, RetentionSnapshot>(2 * RosterShape.Size);
        foreach (var rows in new[] { identity.HomeRows, identity.AwayRows })
            for (var i = 0; i < RosterShape.Size && i < rows.Count; i++)
            {
                var poolId = rows[i].PoolId;
                before[poolId] = league.PlayerSeasons.TryGetValue(poolId, out var rec)
                    ? RetentionSnapshot.Of(rec)
                    : RetentionSnapshot.Zero;
            }
        return before;
    }

    /// <summary>The delta for every man the game could have touched, turned into rows.
    /// Emits exactly where games played moved by one.</summary>
    private static List<PerGameStatRowV1> RetentionRowsAfter(
        SeasonLeagueStats league, Dictionary<int, RetentionSnapshot> before, int gameIndex)
    {
        var rows = new List<PerGameStatRowV1>(2 * RosterShape.Size);
        foreach (var (poolId, b) in before)
        {
            if (!league.PlayerSeasons.TryGetValue(poolId, out var rec)) continue;
            var a = RetentionSnapshot.Of(rec);

            var played = a.GamesPlayed - b.GamesPlayed;
            if (played is not (0 or 1))
                throw new GameLogException(GameLogError.InvalidRow,
                    $"game {gameIndex}: pool {poolId} moved games played by {played}; a man plays a " +
                    "game once or not at all.");

            // ★ Monotonicity, asserted at WRITE time rather than argued in a comment.
            // Every counter is a count of things that happened, so a game can only add.
            // A negative delta means the roll-up was reset, re-keyed, or double-counted,
            // and every conservation total would still balance while it happened.
            var d = Delta(a, b);
            var moved = AnyMoved(d);

            if (played == 0)
            {
                // ★ THE DELTA-0 AUDIT, live. If a man's counters move in a game the
                // participation predicate says he did not play, the two definitions have
                // parted company — and this is the only place that can see it. It stops
                // rather than filing the row somewhere plausible.
                if (moved)
                    throw new GameLogException(GameLogError.InvalidRow,
                        $"game {gameIndex}: pool {poolId} did not play but a counter moved. The " +
                        "participation predicate and the accumulator disagree about who was on the floor.");
                continue;
            }

            if (rec.PersonId is not { IsValid: true } person)
                throw new HistoryException(HistoryError.MissingIdentity,
                    $"game {gameIndex}: pool {poolId} played but carries no person id.");

            rows.Add(new PerGameStatRowV1(
                person, rec.SchoolId, rec.PoolId, rec.AcquisitionIndex,
                d.Credits, d.OffensiveCredits,
                d.Fga, d.Fgm, d.Tpa, d.Tpm, d.Fta, d.Ftm,
                d.OReb, d.DReb, d.Ast, d.Stl, d.Blk, d.To,
                d.ShFoul, d.NsFoul, d.OffFoul, d.FbBlk,
                d.OpponentTwoPaOnFloor, d.SecuredBoardsOnFloor, d.OffensiveTeamFgmOnFloor));
        }
        return rows;
    }

    private static RetentionSnapshot Delta(RetentionSnapshot a, RetentionSnapshot b) => new(
        a.GamesPlayed - b.GamesPlayed, a.Credits - b.Credits, a.OffensiveCredits - b.OffensiveCredits,
        a.Fga - b.Fga, a.Fgm - b.Fgm, a.Tpa - b.Tpa, a.Tpm - b.Tpm, a.Fta - b.Fta, a.Ftm - b.Ftm,
        a.OReb - b.OReb, a.DReb - b.DReb, a.Ast - b.Ast, a.Stl - b.Stl, a.Blk - b.Blk, a.To - b.To,
        a.ShFoul - b.ShFoul, a.NsFoul - b.NsFoul, a.OffFoul - b.OffFoul, a.FbBlk - b.FbBlk,
        a.OpponentTwoPaOnFloor - b.OpponentTwoPaOnFloor,
        a.SecuredBoardsOnFloor - b.SecuredBoardsOnFloor,
        a.OffensiveTeamFgmOnFloor - b.OffensiveTeamFgmOnFloor);

    private static bool AnyMoved(RetentionSnapshot d)
        => d.Credits != 0 || d.OffensiveCredits != 0 || d.Fga != 0 || d.Fgm != 0 || d.Tpa != 0
        || d.Tpm != 0 || d.Fta != 0 || d.Ftm != 0 || d.OReb != 0 || d.DReb != 0 || d.Ast != 0
        || d.Stl != 0 || d.Blk != 0 || d.To != 0 || d.ShFoul != 0 || d.NsFoul != 0
        || d.OffFoul != 0 || d.FbBlk != 0 || d.OpponentTwoPaOnFloor != 0
        || d.SecuredBoardsOnFloor != 0 || d.OffensiveTeamFgmOnFloor != 0;

    // ── The roster section ──────────────────────────────────────────────────

    /// <summary>Who every man WAS this season, built once from the same per-school rows the
    /// game loop plays from. Ratings are stamped at the START of the season (ruled) — which
    /// is free here, because these rows are built before the first tip and never rebuilt, so
    /// the start-of-season card is the only version that exists at write time.</summary>
    private static List<RosterEntryV1> BuildRetentionRoster(
        Dictionary<int, List<GenPlayerRow>> rowsBySchool, PersonIdentityMap personIds)
    {
        var entries = new List<RosterEntryV1>();
        foreach (var (schoolId, rows) in rowsBySchool.OrderBy(kv => kv.Key))
            foreach (var row in rows)
                entries.Add(new RosterEntryV1(
                    personIds[row.PoolId],
                    schoolId,
                    row.PoolId,
                    row.Slot,
                    row.Player.Name,
                    row.Role,
                    PositionOf(row.Pos),
                    row.Starter,
                    (short)row.Player.HierarchyRank,
                    row.ScoutRank,
                    RatingsOf(row.Player)));
        return entries;
    }

    private static RosterPosition PositionOf(string pos) => pos switch
    {
        "G" => RosterPosition.Guard,
        "W" => RosterPosition.Wing,
        "B" => RosterPosition.Big,
        _ => throw new GameLogException(GameLogError.InvalidPosition,
                 $"'{pos}' is not one of the three positions the archive stores."),
    };

    /// <summary>The 38 authored ratings, in the ORDER THE FORMAT PINS.
    ///
    /// <para>★ Assigned one by one, by name, deliberately — not projected out of a
    /// dictionary or reflected off the type. A future session reordering fields on
    /// `Player` must not be able to move a byte on disk, and the only way to guarantee
    /// that is for the disk order to be written down somewhere that a reorder does not
    /// touch. This list is that place.</para>
    ///
    /// <para>★ It is the same order, slot for slot, as `Player.Validate()` — which is a
    /// separately maintained list that this build checks against at Gate 2. Two
    /// independent lists agreeing is a stronger guarantee than a golden file alone,
    /// because a golden only proves the bytes did not change, not that they mean what
    /// the reader thinks.</para></summary>
    private static short[] RatingsOf(Player p) => new short[]
    {
        (short)p.Close, (short)p.Mid, (short)p.Outside, (short)p.Finishing,
        (short)p.FreeThrow, (short)p.FoulDrawing,
        (short)p.RimTendency, (short)p.ShortTendency, (short)p.MidTendency,
        (short)p.LongTendency, (short)p.ThreeTendency,
        (short)p.BallHandling, (short)p.Passing, (short)p.Playmaking, (short)p.SelfCreation,
        (short)p.PostMoves, (short)p.OffBallMovement, (short)p.Screening,
        (short)p.OffensiveRebounding,
        (short)p.PerimeterDefense, (short)p.PostDefense, (short)p.RimProtection,
        (short)p.DefensiveRebounding, (short)p.Steals, (short)p.HelpDefense,
        (short)p.OffBallDefense,
        (short)p.Height, (short)p.Wingspan, (short)p.Weight, (short)p.Strength,
        (short)p.Speed, (short)p.Quickness, (short)p.FirstStep, (short)p.Vertical,
        (short)p.Endurance, (short)p.Hustle, (short)p.BasketballIQ, (short)p.Discipline,
    };

    /// <summary>The rating names in serialized order — the semantic half of the contract.
    /// A golden pins the bytes; this says what they mean. Gate 2 checks it against
    /// `Player.Validate()`'s own list.</summary>
    internal static readonly string[] RetentionRatingOrder =
    {
        "Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
        "RimTendency", "ShortTendency", "MidTendency", "LongTendency", "ThreeTendency",
        "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
        "OffBallMovement", "Screening", "OffensiveRebounding",
        "PerimeterDefense", "PostDefense", "RimProtection", "DefensiveRebounding",
        "Steals", "HelpDefense", "OffBallDefense",
        "Height", "Wingspan", "Weight", "Strength", "Speed", "Quickness", "FirstStep",
        "Vertical", "Endurance", "Hustle", "BasketballIQ", "Discipline",
    };
}
