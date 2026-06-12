namespace Charm.Engine;

/// <summary>
/// The outcomes Roll A (dead-ball inbound entry) can resolve to. This enum
/// defines the slices of Roll A's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
/// </summary>
public enum EntryOutcome
{
    /// <summary>Offense gets cleanly into its halfcourt set. -> CONTINUE.</summary>
    CleanEntry,

    /// <summary>Offense coughs it up on the entry. -> CONTINUE (to turnover-type resolver).</summary>
    Turnover,

    /// <summary>
    /// Shot clock expires in the backcourt — they never got it across in 30s.
    /// -> TERMINAL. Invariant: always the full clock off, so its elapsed time is
    /// known here and needs no separate time roll. (The 30s case; contrast the
    /// 5s and 10s violations below, which burn different fixed amounts.)
    /// </summary>
    ShotClockViolation,

    /// <summary>Failure to inbound within 5 seconds. -> TERMINAL. The clock never
    /// started (the entry pass never came in), so elapsed time is ZERO. A
    /// backcourt-phase violation; like the shot-clock case it is zero-variance and
    /// ends the possession here.</summary>
    FiveSecondInbound,

    /// <summary>Failure to advance the ball past the division line within 10
    /// seconds of a successful inbound. -> TERMINAL. Burns a fixed 10 seconds
    /// (the count ran before the whistle). A backcourt-phase violation; the ball
    /// was inbounded but never cleared the backcourt.</summary>
    TenSecondBackcourt,

    /// <summary>A foul on the inbound/entry. -> CONTINUE (to foul-type resolver,
    /// which will decide defensive non-shooting vs. offensive and what it
    /// triggers). Has real variance, so it is never resolved here.</summary>
    Foul,

    /// <summary>A tie-up / held ball on the inbound. -> CONTINUE (to jump-ball
    /// resolver, which consults the possession arrow on GameState). Rare but
    /// real on virtually every pie going forward.</summary>
    JumpBall
}

/// <summary>
/// The semantic category of a CONTINUE result. A roll classifies *what kind*
/// of continuation it produced; it never names the successor node. The
/// <see cref="Resolver"/> owns the mapping from a kind to the actual next node.
/// Adding a node later changes only that mapping, never the roll that emitted
/// the continuation. This is the seam that lets any roll be built before its
/// successors exist.
/// </summary>
public enum ContinuationKind
{
    /// <summary>Clean entry: hand off to Roll B (halfcourt initiation).</summary>
    IntoHalfcourtSet,

    /// <summary>Turnover: hand off to the shared turnover-type resolver (Roll C).</summary>
    ResolveTurnoverType,

    /// <summary>Foul: hand off to the shared foul-type resolver (Roll D).</summary>
    ResolveFoulType,

    /// <summary>Jump ball: hand off to the jump-ball node (consults the arrow).</summary>
    ResolveJumpBall,

    /// <summary>Halfcourt possession proceeds: hand off to Roll E (player
    /// selection), which picks which on-court offensive slot gets the action.</summary>
    IntoPlayerSelection,

    /// <summary>A player (slot) has been selected: hand off to Roll F (player
    /// action), which decides what the selected player's action becomes — shot
    /// attempt, turnover, non-shooting foul, blocked, or held ball. The selected
    /// slot rides on <see cref="PossessionState"/>, not on the continuation.</summary>
    IntoPlayerAction,

    /// <summary>A non-shooting defensive foul with the opponent NOT in the bonus:
    /// the offense keeps the ball and inbounds. Hand off to the (stubbed)
    /// resumed-inbound / possession-continues node.</summary>
    ResumeInbound,

    /// <summary>A non-shooting defensive foul with the opponent in the bonus
    /// (1-and-1 or double): hand off to the (stubbed) free-throw node. The
    /// <see cref="BonusType"/> rides on the continuation as the FT node's input.
    /// <para>NOTE: this is Roll D's (non-shooting / bonus) free-throw path. Roll H's
    /// SHOOTING fouls route to a SEPARATE node (<see cref="ResolveShootingFreeThrows"/>)
    /// because the shot-count rules differ. Whether the two unify into one FT
    /// resolution node later is an open fork — kept separate for now.</para></summary>
    ResolveFreeThrows,

    /// <summary>A shot attempt was blocked: hand off to the (stubbed)
    /// block-recovery node. A block is a live-ball event with its own fan-out
    /// (out of bounds off defense / off offense / scramble recovered by either
    /// team), so it continues into its own future roll rather than terminating.</summary>
    ResolveBlock,

    /// <summary>A clean shot attempt got off: hand off to Roll G (shot location),
    /// which stamps a <see cref="ShotLocation"/> onto <see cref="PossessionState"/>
    /// (the second per-possession fact after SelectedSlot) that the make/miss roll
    /// will read. The one Roll F outcome that proceeds DEEPER into the shot
    /// sequence.</summary>
    IntoShotType,

    /// <summary>A shot location has been stamped: hand off to Roll H (make/miss),
    /// which resolves the located shot into one of six outcomes and stamps a
    /// <see cref="ShotResult"/> onto <see cref="PossessionState"/> (the third
    /// per-possession fact after ShotType). Emitted by all five of Roll G's zones —
    /// like Roll E, every Roll G outcome stamps a fact and continues to the same
    /// next beat. (Was the chain's dead-end stub before Roll H; now live.)</summary>
    IntoShotResolution,

    /// <summary>A shot missed, live: hand off to the (stubbed) rebound node. The
    /// big dependency several stubs now wait on (block recovery, OOB-retain, the
    /// offensive-rebound-same-possession branch, the Governor's "same team
    /// continues"). An offensive board keeps the SAME possession — the ~67–70
    /// accounting anchor — which the rebound roll, not Roll H, will resolve. The
    /// stamped <see cref="ShotResult"/> rides on <see cref="PossessionState"/>.</summary>
    ResolveRebound,

    /// <summary>A SHOOTING foul was drawn (an and-1 on a make, or a foul on a miss):
    /// hand off to the (stubbed) shooting-free-throw node. The free-throw COUNT
    /// (and-1 = 1; fouled miss = 2; fouled miss on a three = 3) is derived later
    /// from the stamped (<see cref="ShotResult"/>, <see cref="ShotLocation"/>) pair —
    /// that is the future FT roll's job, not encoded here. Kept SEPARATE from
    /// <see cref="ResolveFreeThrows"/> (Roll D's bonus path) for now; possible
    /// future unification is an open fork.</summary>
    ResolveShootingFreeThrows,

    /// <summary>A missed shot deflected out of bounds off the defender and the
    /// offense retained it: hand off to the (stubbed) sideline-inbound node, where
    /// the offense inbounds from the side. MAY eventually share a loose-ball /
    /// inbound node with <see cref="ResolveBlock"/> (block recovery) — flagged, not
    /// merged. The stamped <see cref="ShotResult"/> rides on
    /// <see cref="PossessionState"/>.</summary>
    ResolveSidelineInbound,
}
