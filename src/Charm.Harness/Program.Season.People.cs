using Charm.Engine;
using Charm.History;
using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  Session 115 — THE PEOPLE SURVIVE.
//
//  A career now goes year to year with the same men. Season N's retained log
//  already records who played (S90's roster section); from S115 that section
//  also records each man's class and his two generator labels (roster schema
//  v2), which is everything the next run needs to take him back off the file.
//
//  Running a career season means: read last season's people off its log, turn
//  them over (S114's turnover — seniors leave, everyone advances a class, a
//  freshman class arrives position for position), give each freshman a
//  permanent number of his own, play, record. The chain of season logs IS the
//  save — there is no separate roster file and no end-of-season snapshot
//  (Emmett's ruling 3, 2026-10-05).
//
//  ★ THE PREVIOUS SEASON IS FOUND BY ARITHMETIC, NEVER BY LOOKING AROUND —
//  the same rule host memory follows (Program.Season.Memory.cs). Season N−1's
//  log is at one computable path. If it is missing, or it was saved by an older
//  version of the game (S117, C-60), the career cannot continue and the run
//  REFUSES BY NAME.
//  There is no fallback to a fresh pool: that would be a career that silently
//  forgot everybody, which is exactly what this session exists to end.
//
//  ★ EVERYTHING HERE HAPPENS BEFORE A NUMBER IS SPENT. The read and every
//  structural refusal run from step 1 of the S97 pipeline (the peek), so a
//  career that stops here has burned no season id, no game id and no person
//  id. The one reservation — the freshmen's numbers — happens at the draft
//  site, inside the window the history lock is still held.
//
//  ★ THE READ-BACK IS CONTEXTUAL VALIDATION, which the archive format
//  deliberately cannot do (GameLogReader's class header): that a school id is
//  a real school, that a roster is exactly the roster shape, that acquisition
//  order is a permutation of 1..13. Those are facts about THIS league, so they
//  live here, refused by name, each one.
// ============================================================================

internal static partial class Program
{
    /// <summary>Last season's people, as read off its log: a <see cref="DivvyResult"/> the
    /// turnover can take exactly as it takes the in-memory one, plus which season it came from.</summary>
    private sealed record CareerPeople(DivvyResult Previous, long PreviousSeasonId);

    /// <summary>The one page line a career season prints about its people.</summary>
    private sealed record PeopleSummary(long? FromSeason, int Returned, int Arrived, int Departed)
    {
        public bool IsBootstrap => FromSeason is null;

        public string Line => IsBootstrap
            ? "People: first season — the bootstrap pool"
            : string.Create(CultureInfo.InvariantCulture,
                $"People: {Returned} returned, {Arrived} arrived, {Departed} departed (from season {FromSeason})");

        internal static readonly PeopleSummary Bootstrap = new(null, 0, 0, 0);
    }

    // ── Reading last season ──────────────────────────────────────────────────────

    /// <summary>Season <paramref name="prev"/>'s people, or a refusal by name. The log is read
    /// through the production reader with this career's bindings, so a log from another
    /// career, another world or another season is refused by the reader before a man is read.</summary>
    private static CareerPeople ReadCareerPeople(HistoryStore history, WorldFile world, long prev)
    {
        var path = GameLogWriter.FinalPathFor(history.Path, prev);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"S115 career: season {prev} of this career kept no season log at '{path}', so there is nobody to " +
                "carry into this season. A career's people live in its season logs; without last season's log " +
                "the career cannot continue — start a new career.");

        var bindings = new GameLogBindings(history.HistoryId, history.WorldFingerprint, prev,
                                           ScheduleFingerprint: null);
        // A GameLogException here is already a classified refusal by name (damaged, truncated,
        // wrong lineage, wrong world, or — S117, C-60 — saved by an older version of the game);
        // it is let through untouched.
        var log = GameLogReader.ReadFinalized(path, bindings);
        var roster = log.RosterV2();
        return new CareerPeople(PoolRowsFromRoster(roster, world), prev);
    }

    // ── The read-back ────────────────────────────────────────────────────────────

    /// <summary>★ 3a-ii — one season's roster section turned back into pool rows: the exact
    /// reverse of <see cref="BuildRetentionRoster"/>. The pool comes out dense, 0..P−1, in
    /// (school id, acquisition index) order; each school's roster is its men in acquisition
    /// order; the identity map pairs every new index with the number the man already carries.
    ///
    /// <para>★ THE CARD IS REBUILT FROM THE 38 BY NAME, in the pinned order
    /// (<see cref="RetentionRatingOrder"/>), and the Player from that card through the same
    /// <see cref="GenMapToPlayer"/> every generated man goes through. The five shot-diet
    /// numbers are among the 38 — stored, not re-derived — so a man's card is bit for bit the
    /// card he was written with (Phase 105 C1 proves it key by key).</para></summary>
    private static DivvyResult PoolRowsFromRoster(IReadOnlyList<RosterEntryV2> entries, WorldFile world)
    {
        if (entries is null || entries.Count == 0)
            throw new InvalidOperationException("S115 career: the previous season's roster section is empty.");

        var schoolIds = new HashSet<int>(world.Schools.Select(s => s.Id));
        var bySchool = new Dictionary<int, List<RosterEntryV2>>();
        var personSchool = new Dictionary<PersonId, int>();
        foreach (var e in entries)
        {
            if (!schoolIds.Contains(e.SchoolId))
                throw new InvalidOperationException(
                    $"S115 career: the previous season lists {e.PersonId} on school {e.SchoolId}, which is not a school in this world.");
            if (personSchool.TryGetValue(e.PersonId, out var other))
                throw new InvalidOperationException(other == e.SchoolId
                    ? $"S115 career: {e.PersonId} appears twice on school {e.SchoolId} in the previous season's roster."
                    : $"S115 career: {e.PersonId} is on school {other} AND school {e.SchoolId} in the previous season's roster; one man cannot hold two rosters.");
            personSchool[e.PersonId] = e.SchoolId;
            if (!bySchool.TryGetValue(e.SchoolId, out var list)) bySchool[e.SchoolId] = list = new List<RosterEntryV2>();
            list.Add(e);
        }

        foreach (var s in world.Schools)
        {
            if (!bySchool.TryGetValue(s.Id, out var list))
                throw new InvalidOperationException(
                    $"S115 career: school {s.Id} ({s.Name}) has no players in the previous season's roster; every school carries {RosterShape.Size}.");
            if (list.Count != RosterShape.Size)
                throw new InvalidOperationException(
                    $"S115 career: school {s.Id} ({s.Name}) has {list.Count} players in the previous season's roster, not {RosterShape.Size}.");
            // Acquisition order must be exactly 1..Size once each: the opening five still walks it
            // (O-6), so a duplicate or a gap would reorder a tipoff. The count check cannot see a
            // duplicate (13 entries, 13 distinct men, two of them claiming the same place).
            var acq = list.Select(e => e.AcquisitionIndex).OrderBy(x => x).ToList();
            for (var i = 0; i < RosterShape.Size; i++)
                if (acq[i] != i + 1)
                {
                    var dup = list.GroupBy(e => e.AcquisitionIndex).FirstOrDefault(g => g.Count() > 1);
                    if (dup is not null)
                        throw new InvalidOperationException(
                            $"S115 career: school {s.Id} ({s.Name}) has two men at acquisition place {dup.Key}; the order is ambiguous.");
                    var missing = Enumerable.Range(1, RosterShape.Size).Except(acq).First();
                    throw new InvalidOperationException(
                        $"S115 career: school {s.Id} ({s.Name}) has nobody at acquisition place {missing}; the places are exactly 1..{RosterShape.Size}.");
                }
            var g = list.Count(e => e.Position == RosterPosition.Guard);
            var w = list.Count(e => e.Position == RosterPosition.Wing);
            var b = list.Count(e => e.Position == RosterPosition.Big);
            if (g != RosterShape.Guards || w != RosterShape.Wings || b != RosterShape.Bigs)
                throw new InvalidOperationException(
                    $"S115 career: school {s.Id} ({s.Name}) carries {g} G / {w} W / {b} B in the previous season's roster, " +
                    $"not {RosterShape.Guards}/{RosterShape.Wings}/{RosterShape.Bigs}.");
        }

        var pool = new List<PoolPlayer>(entries.Count);
        var rosters = new Dictionary<int, List<int>>(world.Schools.Count);
        var pairs = new List<KeyValuePair<int, PersonId>>(entries.Count);
        foreach (var s in world.Schools.OrderBy(x => x.Id))
        {
            var ids = new List<int>(RosterShape.Size);
            foreach (var e in bySchool[s.Id].OrderBy(x => x.AcquisitionIndex))
            {
                if (e.Ratings.Count != RetentionRatingOrder.Length)
                    throw new InvalidOperationException(
                        $"S115 career: {e.PersonId} carries {e.Ratings.Count} ratings; the pinned order names {RetentionRatingOrder.Length}.");
                var card = new Dictionary<string, int>(RetentionRatingOrder.Length, StringComparer.Ordinal);
                for (var i = 0; i < RetentionRatingOrder.Length; i++) card[RetentionRatingOrder[i]] = e.Ratings[i];
                var player = GenMapToPlayer(card, e.Name, e.HierarchyRank);
                var errs = player.Validate();
                if (errs.Count > 0)
                    throw new InvalidOperationException(
                        $"S115 career: {e.PersonId} read back from the previous season failed Player.Validate():\n  " + string.Join("\n  ", errs));
                var pid = pool.Count;
                pool.Add(new PoolPlayer(pid, PosOf(e.Position), e.Role, e.DefensivePlane, e.OffensiveRole,
                                        card, player, e.ScoutRank, (ClassYear)e.Class));
                ids.Add(pid);
                pairs.Add(new(pid, e.PersonId));
            }
            rosters[s.Id] = ids;
        }

        return new DivvyResult
        {
            Pool = pool, Rosters = rosters, Picks = new List<DivvyPick>(), NoiseScale = 0.0,
            PersonIds = PersonIdentityMap.Freeze(pairs),
        };
    }

    // ── The career turnover ──────────────────────────────────────────────────────

    /// <summary>★ 3a-iii — the turnover on a career: S114's pure turnover run on last season's
    /// read-back rosters, then the freshmen are numbered. Returners keep the number they were
    /// written with (found through the one object the turnover preserves per man — his
    /// <see cref="Player"/>, which `with`-clones carry by reference); freshmen take the issued
    /// numbers in generation order, which is their order in the season-two pool.
    ///
    /// <para>A freshman is named for the season he arrived in and his place in the class
    /// (`Pool_s&lt;season&gt;_&lt;k&gt;`): unique against every returner and every past season by
    /// construction, without the harness ever seeing a person's number (S89's wall holds).</para></summary>
    private static (DivvyResult Divvy, PeopleSummary Summary) CareerTurnover(
        WorldFile world, CareerPeople people, long seasonSeed, long pendingSeasonId, HistoryStore history)
    {
        var prev = people.Previous;
        var t = RunTurnover(world, prev, seasonSeed,
                            k => string.Create(CultureInfo.InvariantCulture, $"Pool_s{pendingSeasonId}_{k}"));
        var pool2 = t.SeasonTwo.Pool;

        // ★ ONE reservation for the whole class, durable before a number is handed out (S89).
        var issued = history.ReservePersons(t.Freshmen.Count);

        var keptId = new Dictionary<Player, PersonId>(ReferenceEqualityComparer.Instance);
        foreach (var row in prev.Pool) keptId[row.Player] = prev.PersonIds![row.PoolId];

        var pairs = new KeyValuePair<int, PersonId>[pool2.Count];
        for (var i = 0; i < pool2.Count; i++)
        {
            if (i < t.ReturnerCount)
            {
                if (!keptId.TryGetValue(pool2[i].Player, out var id))
                    throw new InvalidOperationException(
                        $"S115 career: season-two returner #{i} is not a man from last season's roster.");
                pairs[i] = new(i, id);
            }
            else
            {
                pairs[i] = new(i, issued[i - t.ReturnerCount]);
            }
        }
        var ids = PersonIdentityMap.Freeze(pairs);
        if (ids.Count != pool2.Count)
            throw new InvalidOperationException(
                $"S115 career: the identity map covers {ids.Count} of {pool2.Count} people after the turnover.");

        var divvy = new DivvyResult
        {
            Pool = pool2, Rosters = t.SeasonTwo.Rosters, Picks = t.SeasonTwo.Picks,
            NoiseScale = t.SeasonTwo.NoiseScale, PersonIds = ids,
            MinSlackLead = t.SeasonTwo.MinSlackLead, MinSlackTdw = t.SeasonTwo.MinSlackTdw,
        };
        var summary = new PeopleSummary(people.PreviousSeasonId, t.ReturnerCount, t.Freshmen.Count,
                                        prev.Pool.Count - t.ReturnerCount);
        return (divvy, summary);
    }
}
