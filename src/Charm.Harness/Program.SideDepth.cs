using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Session 76 — SIDE DEPTH, extracted from the retired fatigue fence.
//
//  Until S76 this was a class nested INSIDE FlatFatigueFencePolicy. It was never
//  fence-specific: it is the side's seat/roster structure — who is on the roster,
//  what each man's stored group is, which slot each starter owns, and what
//  position each seat is frozen at for the game. The fence merely happened to be
//  the first policy that needed it.
//
//  It is extracted BEFORE the fence is deleted, because deleting the fence file
//  first would take this structure with it.
//
//  NOTE ON PLACEMENT: like RosterShape / PositionalEligibility, this is a
//  standalone class and NOT part of `internal static partial class Program`.
//  The substitution policies are standalone classes (they implement an engine
//  interface and are constructed by the harness), so a private member of
//  `Program` would be unreachable from them.
//
//  ── S76 addition: the stored-group depth rank ────────────────────────────────
//  `RankById` carries each player's scout rank to the seam. Before S76 the depth
//  ORDER was computed by the divvy and then discarded one line before the seam
//  needed it (`BuildSeasonRows` read `res.Pool[pid]`, which carries ScoutRank,
//  and built its row without it). The minutes allocator cannot order a depth
//  chart without it, and a silent fallback to player-id order would produce a
//  perfectly plausible, perfectly stable, perfectly meaningless rotation.
//
//  ★ RANK IS COMPARABLE WITHIN A STORED GROUP, NEVER ACROSS ONE. `DivvyScoutRank`
//  builds its legs with position-specific hole sets, a position-specific size
//  transform and a big-only athleticism add-back, and only then calls the
//  position-agnostic `DivvyRankFromLegs`. Two players of the SAME stored group
//  went through identical transforms, so their ranks are comparable. Two players
//  of DIFFERENT groups did not, so theirs are not. A global sort across all
//  thirteen would yield a clean, monotone, entirely plausible chart that mostly
//  encodes position mix. Sort within a group; never across.
// ============================================================================

internal sealed class SideDepth
{
    public TeamSide Side { get; }
    public IReadOnlyDictionary<int, Player> PlayerById { get; }
    public IReadOnlyDictionary<int, string> PosById { get; }

    /// <summary>Slot 1..5 → the PlayerId of that slot's starter (its permanent owner).</summary>
    public IReadOnlyDictionary<int, int> SlotStarterId { get; }

    /// <summary>Slot 1..5 → position (the starter's position; fixed all game).</summary>
    public IReadOnlyDictionary<int, string> SlotPos { get; }

    /// <summary>
    /// PlayerId → scout rank, HIGHER IS BETTER. Meaningful only when compared against
    /// another player of the SAME stored group (see the header note).
    /// </summary>
    public IReadOnlyDictionary<int, double> RankById { get; }

    public SideDepth(
        TeamSide side,
        IReadOnlyList<Player> starters, IReadOnlyList<string> starterPositions, IReadOnlyList<double> starterRanks,
        IReadOnlyList<Player> reserves, IReadOnlyList<string> reservePositions, IReadOnlyList<double> reserveRanks)
    {
        // S75: the STARTER count is a rule of basketball and stays exact. The RESERVE
        // count is not asserted here — SideDepth is generic and serves both real
        // 13-man divvied rosters (8 reserves) and the synthetic archetype fixtures
        // the stress and observation runs build (5). Roster size is asserted where it
        // is actually a contract: the divvy and season checks.
        if (starters.Count != Lineup.Size || starterPositions.Count != Lineup.Size)
            throw new ArgumentException(
                $"A side has exactly {Lineup.Size} starters with {Lineup.Size} positions " +
                $"(got {starters.Count} / {starterPositions.Count}).");
        if (reserves.Count != reservePositions.Count)
            throw new ArgumentException(
                $"reserve count and position count disagree " +
                $"(got {reserves.Count} / {reservePositions.Count}).");

        // S76: rank counts must match man-for-man. A short list would otherwise be
        // padded with a default, and a default rank is exactly the silent failure this
        // field exists to prevent — every player equal, chart ordered by id, plausible.
        if (starterRanks.Count != starters.Count || reserveRanks.Count != reserves.Count)
            throw new ArgumentException(
                $"rank count must match player count exactly " +
                $"(starters {starterRanks.Count}/{starters.Count}, reserves {reserveRanks.Count}/{reserves.Count}).");

        // S75: every stored position must be a label the eligibility ladder knows.
        // Positions are plain strings, so an empty string or a typo would otherwise
        // read as "ineligible everywhere" and silently remove a player from every
        // rotation instead of failing.
        foreach (var q in starterPositions.Concat(reservePositions))
            if (!PositionalEligibility.IsPosition(q))
                throw new ArgumentException($"unrecognised roster position label \"{q}\".");

        Side = side;
        var byId    = new Dictionary<int, Player>();
        var pos     = new Dictionary<int, string>();
        var rank    = new Dictionary<int, double>();
        var slotOwn = new Dictionary<int, int>();
        var slotPos = new Dictionary<int, string>();

        for (var i = 0; i < starters.Count; i++)
        {
            var s = starters[i];
            byId[s.PlayerId]    = s;
            pos[s.PlayerId]     = starterPositions[i];
            rank[s.PlayerId]    = starterRanks[i];
            slotOwn[i + 1]      = s.PlayerId;      // starters seated into slots 1..5 in order
            slotPos[i + 1]      = starterPositions[i];
        }
        for (var i = 0; i < reserves.Count; i++)
        {
            var r = reserves[i];
            byId[r.PlayerId]  = r;
            pos[r.PlayerId]   = reservePositions[i];
            rank[r.PlayerId]  = reserveRanks[i];
        }

        PlayerById    = byId;
        PosById       = pos;
        RankById      = rank;
        SlotStarterId = slotOwn;
        SlotPos       = slotPos;
    }

    /// <summary>
    /// The players of one stored group, in DEPTH ORDER — scout rank descending, ties
    /// broken by ascending PlayerId so the chart is deterministic under replay.
    /// </summary>
    public List<int> DepthChartFor(string storedGroup)
    {
        var ids = new List<int>();
        foreach (var kv in PosById)
            if (kv.Value == storedGroup) ids.Add(kv.Key);

        ids.Sort((a, b) =>
        {
            var c = RankById[b].CompareTo(RankById[a]);   // descending: best first
            return c != 0 ? c : a.CompareTo(b);           // deterministic tie-break
        });
        return ids;
    }
}
