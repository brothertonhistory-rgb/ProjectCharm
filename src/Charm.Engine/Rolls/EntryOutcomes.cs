namespace Charm.Engine;

/// <summary>
/// The outcomes Roll A (dead-ball inbound entry) can resolve to. This enum
/// defines the slices of Roll A's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
/// </summary>
public enum EntryOutcome
{
    /// <summary>Offense gets cleanly into its halfcourt set. -> CONTINUE. Latches the
    /// possession's court-state to FRONTCOURT on the way to Roll B — the offense has
    /// crossed, so the backcourt-only ways to lose it are gone from here on.</summary>
    CleanEntry,

    /// <summary>Offense coughs it up on the entry. -> CONTINUE (to the shared
    /// turnover-type resolver, Roll C). Roll A stamps the loss CONTEXT by the current
    /// court-state: a backcourt bring-up routes to Roll C's EntryBackcourt pie (the
    /// backcourt violations live there), a frontcourt re-inbound to the Halfcourt
    /// pie. Roll A no longer terminates any violation itself — the loss resolves in
    /// Roll C (Contextification #6 consolidated the three former violation terminals
    /// there).</summary>
    Turnover,

    /// <summary>An OFFENSIVE foul on the entry — a charge or illegal screen by the team
    /// bringing it up. -> CONTINUE (to the OffensiveFoul resolution: a player-control
    /// foul is a dead-ball turnover to the other team, no free throws, no bonus). The
    /// foul is still attributed to the individual player by the future attribution
    /// layer; the possession-level effect is just a loss. Split out from the old single
    /// <c>Foul</c> slice in Contextification #6.</summary>
    OffensiveFoul,

    /// <summary>A non-shooting DEFENSIVE foul on the entry — a reach-in, a grab, a bump
    /// on the ball-handler. -> CONTINUE (to the shared foul-type node, Roll D, which
    /// charges the team foul and forks on the bonus). Below the bonus the offense keeps
    /// the ball and re-inbounds in the CURRENT court-state; in the bonus it goes to the
    /// line. Split out from the old single <c>Foul</c> slice in Contextification #6.</summary>
    DefensiveFoul,

    /// <summary>A tie-up / held ball on the inbound. -> CONTINUE (to the jump-ball
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

    /// <summary>An OFFENSIVE foul (a charge / illegal screen) was drawn on the entry:
    /// hand off to the offensive-foul resolution. Deterministic — the resolver maps
    /// this straight to the same dead-ball loss terminal Roll C names for an offensive
    /// foul (ball to the other team, no free throws, no bonus), with no pie roll. Kept
    /// as a CONTINUATION KIND rather than a Roll A terminal so the "one node names the
    /// loss" rule holds and a future flavor tag (charge vs. off-arm vs. illegal screen)
    /// has a single home. Added in Contextification #6 when Roll A's foul slice split
    /// into offensive vs. defensive.</summary>
    ResolveOffensiveFoul,

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

    /// <summary>The offense secured the offensive rebound: hand off to the
    /// (stubbed) offensive-rebound node. The same possession stays alive — the
    /// offense retains the ball and the rebound roll keeps the chain going. The
    /// real offensive-rebound roll (with its own odds, one branch looping back to
    /// the halfcourt roll → player selection) is a later session.</summary>
    ResolveOffensiveRebound,

    /// <summary>A transition possession decided to RUN (Roll J's <c>Push</c>): hand
    /// off to the (stubbed) transition node — the future transition roll's holding
    /// pen, where what the fast break PRODUCES (numbers, leak-outs, transition shot
    /// mix) will resolve. An <c>Into*</c> kind (proceed DEEPER into a live beat, like
    /// <see cref="IntoHalfcourtSet"/> / <see cref="IntoPlayerSelection"/> /
    /// <see cref="IntoShotType"/>), not a <c>Resolve*</c> hand-off to a terminal-ish
    /// node. The real transition roll replaces the stub without Roll J changing.</summary>
    IntoTransition,

    /// <summary>A missed FINAL free throw left the ball live: hand off to the
    /// (stubbed) FT-rebound node. Emitted by the resolver's FT-sequence driver (Roll
    /// L's loop), not by a roll — the last attempt of a trip missed, so the board is
    /// contested exactly like a missed field goal. The future FT-rebound roll owns
    /// the offensive/defensive board split off a missed FT plus any foul on that
    /// rebound; this kind just parks there for now. Distinct from
    /// <see cref="ResolveRebound"/> (the field-goal rebound) because the FT-rebound
    /// population differs (everyone lined up along the lane, no shooter crashing) —
    /// flagged, not merged.</summary>
    ResolveFTRebound,
}
