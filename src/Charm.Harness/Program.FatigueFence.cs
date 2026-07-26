using Charm.Engine;

namespace Charm.Harness;

/// <summary>
/// The flat fatigue fence (Substitutions, Pass 1). A harness-side
/// <see cref="ISubstitutionPolicy"/>: the engine reports possession boundaries and hands
/// over the game; all basketball — the fatigue lines, positional pairing, reclaim, the
/// reserve list — lives here, alongside generation and positions.
///
/// <para><b>The fence.</b> When an on-floor player tires to or past the <see cref="PullLine"/>,
/// his freshest benched same-position teammate checks in at the next dead ball. When a
/// benched starter recovers below the <see cref="ReturnLine"/> (and is fresher than his
/// slot's current occupant), he reclaims his own slot. Starters stay starters; subs stay
/// within position, so the floor keeps its starting positional shape.</para>
///
/// <para><b>These lines are placeholders, not calibration.</b> Getting the system running —
/// subs firing, stats attributing across them, the suite green — is the job. The line
/// values and the fatigue magnitudes are tuned later, once a realistic population exists.</para>
///
/// <para><b>Determinism.</b> The policy draws no RNG and reads no wall clock. Every choice is
/// a pure function of (fatigue levels, positions, slot occupancy); ties break by lowest
/// fatigue then lowest PlayerId. A replayed seed reproduces the identical substitution
/// sequence, so the meter and attribution stay reproducible.</para>
/// </summary>
internal sealed class FlatFatigueFencePolicy : ISubstitutionPolicy
{
    // Placeholders, tunable — NOT calibration. The gap (75 → 35) is the anti-thrash buffer:
    // a pulled player must recover well below the pull-line before reclaiming, so a genuine
    // breather sits between leaving and returning.
    private const double PullLine   = 75.0;
    private const double ReturnLine = 35.0;

    private readonly SideDepth _home;
    private readonly SideDepth _away;

    // The engine's halftime rest-equivalent (same value the engine feeds
    // ApplyHalftimeRecovery for the on-floor five). Using the identical magnitude here for
    // the benched five is what makes the halftime rest reach everyone equally.
    private readonly double _halftimeRestEquivalentSeconds;

    public FlatFatigueFencePolicy(SideDepth home, SideDepth away, double halftimeRestEquivalentSeconds)
    {
        _home = home ?? throw new ArgumentNullException(nameof(home));
        _away = away ?? throw new ArgumentNullException(nameof(away));
        _halftimeRestEquivalentSeconds = halftimeRestEquivalentSeconds;
    }

    // ── ISubstitutionPolicy ─────────────────────────────────────────────────────

    public void OnPossessionBoundary(GameState game, int nextPossessionNumber, double elapsedSeconds, bool isDeadBall)
    {
        foreach (var side in new[] { _home, _away })
        {
            // Every possession, dead ball or not: recover the currently-benched players by
            // this possession's elapsed seconds. On-floor players were already accrued by
            // the engine at the possession tail — never recover them.
            RecoverBenched(game, side, elapsedSeconds);

            // Substitutions are legal only from a dead ball.
            if (isDeadBall)
                EvaluateSlots(game, side, nextPossessionNumber, allowPull: true);
        }
    }

    public void OnPeriodBreak(GameState game, int nextPossessionNumber, double finalPossessionElapsedSeconds, PeriodBreakKind kind)
    {
        foreach (var side in new[] { _home, _away })
        {
            // Step 2: the final possession's recovery slice for the benched players — the
            // per-possession recovery the (suppressed) ordinary boundary callback would
            // otherwise have applied. Mirrors the on-floor five, who accrued that possession.
            RecoverBenched(game, side, finalPossessionElapsedSeconds);

            // Step 4: the matching period rest for the benched players. The engine has
            // already rested the on-floor five (halftime only). Halftime → the same chunk
            // for the bench; overtime → nothing (recovery is regulation-only, never in OT,
            // so benched and on-floor are treated alike).
            if (kind == PeriodBreakKind.Halftime)
                RecoverBenched(game, side, _halftimeRestEquivalentSeconds);

            // Step 5: reclaim only. A benched starter who is now under the return-line (and
            // fresher than his slot's current occupant) reclaims his slot for the first
            // possession of the next period. Any remaining pull is handled at the period's
            // subsequent dead balls by the ordinary boundary callback.
            EvaluateSlots(game, side, nextPossessionNumber, allowPull: false);
        }
    }

    // ── Recovery ────────────────────────────────────────────────────────────────

    private static void RecoverBenched(GameState game, SideDepth side, double elapsedSeconds)
    {
        var benched = BenchedPlayers(game, side);
        if (benched.Count > 0)
            game.Fatigue.Recover(benched, elapsedSeconds);
    }

    // ── The fence (pull + reclaim), evaluated per slot, at most one sub per slot ──

    private void EvaluateSlots(GameState game, SideDepth side, int atPossession, bool allowPull)
    {
        var roster = game.RosterFor(side.Side);

        for (var slot = 1; slot <= 5; slot++)
        {
            var occupant = roster.PlayerAt(new Slot(side.Side, slot));
            if (occupant is null) continue;   // defensive; a seated slot is never null
            var occupantLevel = game.Fatigue.LevelFor(occupant.PlayerId);

            // Recompute occupancy per slot so a reserve moved into an earlier slot this same
            // dead ball is not also offered to a later slot (two slots can share a position).
            var onFloor = OnFloorIds(game, side);

            // Reclaim first (starters stay starters). The slot's starter, if benched and
            // recovered below the return-line and fresher than the current occupant, takes
            // his slot back.
            var starterId = side.SlotStarterId[slot];
            if (starterId != occupant.PlayerId && !onFloor.Contains(starterId))
            {
                var starterLevel = game.Fatigue.LevelFor(starterId);
                if (starterLevel <= ReturnLine && starterLevel < occupantLevel)
                {
                    roster.Substitute(new Slot(side.Side, slot), side.PlayerById[starterId], atPossession);
                    continue;   // one sub per slot per dead ball
                }
            }

            if (!allowPull) continue;

            // Pull: the occupant is gassed to/past the pull-line. Bring in the freshest
            // benched same-position teammate, provided he is genuinely fresher than the
            // occupant. If none is available (bench exhausted at that position), the tired
            // player stays in — real basketball, no phantom sub.
            if (occupantLevel < PullLine) continue;

            var slotPos = side.SlotPos[slot];
            int   bestId = -1;
            double bestLevel = double.MaxValue;
            foreach (var pid in side.PlayerById.Keys)
            {
                if (onFloor.Contains(pid)) continue;             // must be benched
                // S75: the one-step ladder replaces the same-position filter. Evaluated
                // from the player's STORED position, never transitively — see
                // PositionalEligibility. Same-position remains a subcase.
                if (!PositionalEligibility.IsEligibleForSeat(side.PosById[pid], slotPos)) continue;
                var lvl = game.Fatigue.LevelFor(pid);
                if (lvl < bestLevel || (lvl == bestLevel && pid < bestId))
                {
                    bestLevel = lvl;
                    bestId = pid;
                }
            }

            if (bestId != -1 && bestLevel < occupantLevel)
                roster.Substitute(new Slot(side.Side, slot), side.PlayerById[bestId], atPossession);
        }
    }

    // ── Occupancy helpers (derived from the live roster — cannot drift) ───────────

    private static HashSet<int> OnFloorIds(GameState game, SideDepth side)
    {
        var roster = game.RosterFor(side.Side);
        var ids = new HashSet<int>();
        for (var slot = 1; slot <= 5; slot++)
        {
            var p = roster.PlayerAt(new Slot(side.Side, slot));
            if (p is not null) ids.Add(p.PlayerId);
        }
        return ids;
    }

    private static List<Player?> BenchedPlayers(GameState game, SideDepth side)
    {
        var onFloor = OnFloorIds(game, side);
        var benched = new List<Player?>();
        foreach (var kv in side.PlayerById)
            if (!onFloor.Contains(kv.Key))
                benched.Add(kv.Value);
        return benched;
    }

    // ── Per-side depth chart for one game ─────────────────────────────────────────

    /// <summary>
    /// One physical side's depth information for a single game: the ten players, each
    /// player's position, and — for the five starters — which on-floor slot they own
    /// (fixing that slot's position for the whole game). Built by the gen matchup runner,
    /// which knows the logical→physical side assignment for the game.
    /// </summary>
    internal sealed class SideDepth
    {
        public TeamSide Side { get; }
        public IReadOnlyDictionary<int, Player> PlayerById { get; }
        public IReadOnlyDictionary<int, string> PosById { get; }
        /// <summary>Slot 1..5 → the PlayerId of that slot's starter (its permanent owner).</summary>
        public IReadOnlyDictionary<int, int> SlotStarterId { get; }
        /// <summary>Slot 1..5 → position (the starter's position; fixed all game).</summary>
        public IReadOnlyDictionary<int, string> SlotPos { get; }

        public SideDepth(
            TeamSide side,
            IReadOnlyList<Player> starters, IReadOnlyList<string> starterPositions,
            IReadOnlyList<Player> reserves, IReadOnlyList<string> reservePositions)
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
            var slotOwn = new Dictionary<int, int>();
            var slotPos = new Dictionary<int, string>();

            for (var i = 0; i < starters.Count; i++)
            {
                var s = starters[i];
                byId[s.PlayerId]    = s;
                pos[s.PlayerId]     = starterPositions[i];
                slotOwn[i + 1]      = s.PlayerId;      // starters seated into slots 1..5 in order
                slotPos[i + 1]      = starterPositions[i];
            }
            for (var i = 0; i < reserves.Count; i++)
            {
                var r = reserves[i];
                byId[r.PlayerId] = r;
                pos[r.PlayerId]  = reservePositions[i];
            }

            PlayerById    = byId;
            PosById       = pos;
            SlotStarterId = slotOwn;
            SlotPos       = slotPos;
        }
    }
}
