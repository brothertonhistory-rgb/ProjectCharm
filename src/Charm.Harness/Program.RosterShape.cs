using System;

namespace Charm.Harness;

// ============================================================================
//  Session 75 — THE ROSTER SHAPE, in one place.
//
//  Before S75 the roster was ten players at 4G/3W/3B, and that shape was not a
//  constant anywhere — it was ~30 hardcoded literals spread across the divvy, the
//  gen path, the box-score arrays and two check files. Worst of all it appeared in
//  TWO INCOMPATIBLE FORMS: group counts (`4 * n`, `3 * n`) and an index BOUNDARY
//  (`pid < 7 * n`) that had to agree with them. Changing one without the other
//  reclassifies wings as bigs while every total still sums correctly and the
//  divvy's own infeasibility assertion passes green.
//
//  So both forms now derive from the same four numbers, and nothing else may
//  restate them.
//
//  NOTE ON PLACEMENT: this is a standalone static class, NOT part of the
//  `internal static partial class Program`. 45 of the 46 harness files are that
//  partial; `Program.FatigueFence.cs` is the one exception, and it is precisely
//  the file that needs the eligibility matrix. A private member of `Program`
//  would be unreachable from there.
// ============================================================================

internal static class RosterShape
{
    /// <summary>Scholarship players per school. S75: 10 -> 13.</summary>
    public const int Size = 13;

    // 5G / 4W / 4B — Emmett's ruling, 2026-07-25. Real programs carry 7-9 perimeter
    // and 4-6 post; 9/4 sits inside both. The perimeter lean is deliberate: going
    // small covers a post in foul trouble, so a fifth big would sit all year.
    public const int Guards = 5;
    public const int Wings  = 4;
    public const int Bigs   = 4;

    /// <summary>Total national pool for a world of <paramref name="schoolCount"/> schools.</summary>
    public static int PoolSize(int schoolCount) => Size * schoolCount;

    // The pool is rank-ordered on the defensive plane, so position is an INDEX RANGE:
    // the first Guards*n are guards, the next Wings*n are wings, the rest are bigs.
    // These two boundaries are the form that used to drift out of step with the counts.
    public static int GuardCount(int schoolCount) => Guards * schoolCount;
    public static int WingCount(int schoolCount)  => Wings  * schoolCount;
    public static int BigCount(int schoolCount)   => Bigs   * schoolCount;

    /// <summary>First pool index that is a wing (== end of the guard block).</summary>
    public static int WingBlockStart(int schoolCount) => Guards * schoolCount;

    /// <summary>First pool index that is a big (== end of the wing block).</summary>
    public static int BigBlockStart(int schoolCount) => (Guards + Wings) * schoolCount;

    /// <summary>Position of a pool index, derived from the same numbers as the counts.</summary>
    public static string PositionForPoolIndex(int pid, int schoolCount)
        => pid < WingBlockStart(schoolCount) ? PositionalEligibility.Guard
         : pid < BigBlockStart(schoolCount)  ? PositionalEligibility.Wing
                                             : PositionalEligibility.Big;

    // ── Stamped player ids ───────────────────────────────────────────────────
    //  Convention: home program A = 1..Size, away program B = Size+1..2*Size.
    //  At S75 that is A = 1..13, B = 14..26.
    //
    //  ★ This is the session's most dangerous number. Every box-score array was
    //  `new long[20]` behind a guard reading `if (id >= 1 && id <= 20)`. A missed
    //  site does not throw — it SILENTLY DROPS the players above the ceiling, and
    //  the season's conservation checks verify wins and losses rather than player
    //  stats, so they stay green while six players a game accumulate nothing.
    //  Nothing may be measured until MaxPlayerId is asserted.

    /// <summary>Added to an away player's roster index to get his stamped id.</summary>
    public const int AwayIdOffset = Size;

    /// <summary>Highest legal stamped player id across both programs.</summary>
    public const int MaxPlayerId = 2 * Size;

    /// <summary>Width for a per-player array indexed by `stampedId - 1`.</summary>
    public const int PlayerArrayWidth = MaxPlayerId;

    /// <summary>True when a stamped id is inside the legal range.</summary>
    public static bool IsLegalPlayerId(int stampedId)
        => stampedId >= 1 && stampedId <= MaxPlayerId;
}

// ============================================================================
//  Session 75 — POSITIONAL ELIGIBILITY (the one-step ladder).
//
//  Emmett's ruling, 2026-07-25: "Every PG can play SG. Every SG can play SF,
//  etc... not well, but there is real position flexibility baked into basketball."
//
//  In the three-bucket taxonomy that is one step along G - W - B.
//
//      stored group | guard seat | wing seat | big seat
//      -------------+------------+-----------+----------
//      Guard        |    yes     |    yes    |    NO
//      Wing         |    yes     |    yes    |    yes
//      Big          |    NO      |    yes    |    yes
//
//  ★ NOT TRANSITIVE. Eligibility is evaluated from the player's STORED position
//  only. A guard does not become a wing and then reach a big seat; a guard cannot
//  fill a big seat at all. Do not implement this recursively.
//
//  WHY THIS EXISTS AT ALL (S75 measurement): each seat's position is frozen for
//  the game, so under same-position-only substitution a position group can consume
//  only `40 x its seat count` minutes. Measured against the live 347-school world,
//  every observed opening shape leaves at least one group holding a single seat,
//  and that bottleneck caps a uniform thirteen-man distribution at 10.0 minutes a
//  man against a parity of 15.4. With this ladder all three observed shapes reach
//  exactly 15.4 — the theoretical maximum. Without it, S76 has nothing feasible to
//  allocate.
//
//  WHAT THIS DOES **NOT** DO: it adds no out-of-position penalty, because there is
//  nowhere to put one. The engine has no concept of position at all — seats are
//  numbered, and `DefenderPicker` matches seat N against the opponent's seat N. So
//  an occupant is priced against whoever actually holds the mirror seat, through
//  the existing size/strength/post-defense math. The DEFENSIVE CORE of a role
//  difference is therefore already paid for: `Matchup.Postness` reads Height,
//  PostDefense and Strength, and the generator assigns position by defensive plane
//  — the same axis. What is NOT priced is screening, off-ball movement, interior
//  spacing, rotation responsibility and ballhandling out of position. Those are
//  absent rather than mispriced, and S75 MEASURES whether their absence shows up
//  rather than assuming it does not.
//
//  ★ CONSTRAINT ON ANY FUTURE ROLE-COST MODEL (Emmett's relative-pricing premise:
//  "You only get punished for size if the other team can punish it"). Every size
//  term in this engine is a GAP between the two teams — `Matchup` composes
//  sizeShift/skillShift/hustleShift as GapFn(offense - defense) and bends by
//  tanh(total / referenceShift), so equal teams get zero bend and two five-guard
//  teams get a neutral rebounding split. A flat "worse at playing the 3" penalty
//  would be the FIRST ABSOLUTE PHYSICAL TERM in the codebase and would break
//  exactly the property that makes small-ball coherent. Any future role cost must
//  be gap-shaped.
// ============================================================================

internal static class PositionalEligibility
{
    public const string Guard = "G";
    public const string Wing  = "W";
    public const string Big   = "B";

    /// <summary>True for a recognised stored/seat position label.</summary>
    public static bool IsPosition(string? p)
        => p is Guard or Wing or Big;

    /// <summary>Throws on an unrecognised label rather than letting it masquerade as
    /// an ineligible matchup. Positions are plain strings, so an empty string, a typo
    /// or a future fourth category would otherwise silently read as "not eligible" and
    /// quietly remove a player from every rotation.</summary>
    private static void RequirePosition(string? p, string role)
    {
        if (!IsPosition(p))
            throw new ArgumentOutOfRangeException(
                role, p, $"unrecognised position label — expected \"{Guard}\", \"{Wing}\" or \"{Big}\".");
    }

    /// <summary>May a player whose STORED position is <paramref name="storedPosition"/>
    /// occupy a seat whose position is <paramref name="seatPosition"/>? One step only,
    /// evaluated from the stored position, never transitively.</summary>
    public static bool IsEligibleForSeat(string? storedPosition, string? seatPosition)
    {
        RequirePosition(storedPosition, nameof(storedPosition));
        RequirePosition(seatPosition,   nameof(seatPosition));

        if (storedPosition == seatPosition) return true;

        return (storedPosition, seatPosition) switch
        {
            (Guard, Wing) => true,
            (Wing, Guard) => true,
            (Wing, Big)   => true,
            (Big,  Wing)  => true,
            (Guard, Big)  => false,   // a guard never reaches a big seat
            (Big,  Guard) => false,   // a big never reaches a guard seat
            _             => false,
        };
    }

    /// <summary>`"G->W"` etc. — the transition label used by the S75 occupancy
    /// readouts, so it is visible whether the ladder is actually being used or is
    /// merely present in the code.</summary>
    public static string TransitionLabel(string? storedPosition, string? seatPosition)
    {
        RequirePosition(storedPosition, nameof(storedPosition));
        RequirePosition(seatPosition,   nameof(seatPosition));
        return $"{storedPosition}->{seatPosition}";
    }

    /// <summary>The seven legal transitions, in report order.</summary>
    public static readonly string[] LegalTransitions =
    {
        "G->G", "G->W", "W->G", "W->W", "W->B", "B->W", "B->B",
    };

    /// <summary>True when the occupant is sitting outside his stored position.</summary>
    public static bool IsCrossPosition(string? storedPosition, string? seatPosition)
    {
        RequirePosition(storedPosition, nameof(storedPosition));
        RequirePosition(seatPosition,   nameof(seatPosition));
        return storedPosition != seatPosition;
    }
}
