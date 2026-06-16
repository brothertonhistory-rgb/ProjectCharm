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
    /// the offense keeps the ball and re-inbounds. As of Contextification #6 this no
    /// longer parks at a stub: the resolver re-runs Roll A carrying the current
    /// court-state (a backcourt entry foul resumes backcourt; a frontcourt foul
    /// resumes frontcourt, where the backcourt losses are unreachable). A foul on
    /// the re-inbound can cross the bonus mid-loop, routing to
    /// <see cref="ResolveFreeThrows"/> instead.</summary>
    ResumeInbound,

    /// <summary>A non-shooting defensive foul with the opponent in the bonus
    /// (1-and-1 or double): driven by the resolver's Roll L FT-sequence driver
    /// (<c>DriveFreeThrows</c>). The <see cref="BonusType"/> rides on the continuation
    /// as the FT count input.
    /// <para>NOTE: this is Roll D's (non-shooting / bonus) free-throw path. Roll H's
    /// SHOOTING fouls route to a SEPARATE kind (<see cref="ResolveShootingFreeThrows"/>)
    /// because the shot-count rules differ. Whether the two unify into one FT
    /// resolution node later is an open fork — kept separate for now.</para></summary>
    ResolveFreeThrows,

    /// <summary>RETIRED (Contextification #2). A blocked shot no longer routes here:
    /// Roll H's Blocked arm routes into <see cref="ResolveRebound"/> carrying
    /// <c>ReboundSource.Block</c>, and Roll I's block-weighted pie resolves it.
    /// The resolver throws <see cref="System.InvalidOperationException"/> if anything
    /// routes to this kind — it is a dead wiring path, kept as a named kind so the
    /// throw is legible.</summary>
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

    /// <summary>A shot missed, live: executes Roll I (rebound resolution). Roll I is
    /// a gate with mixed ends: TERMINALS (DefensiveRebound / LooseBallFoulOnOffense —
    /// possession ends) and CONTINUES (ResolveOffensiveRebound / ResolveSidelineInbound
    /// / ResolveFreeThrows) that re-enter the walk. The stamped
    /// <see cref="ShotResult"/> rides on <see cref="PossessionState"/>. The
    /// <c>ReboundSource</c> on the continuation selects Roll I's pie — a null stamp
    /// (standard missed field goal) reads as LiveBall; Roll H's Blocked arm stamps
    /// Block for the block-weighted pie.</summary>
    ResolveRebound,

    /// <summary>A SHOOTING foul was drawn (an and-1 on a make, or a foul on a miss):
    /// driven by the resolver's Roll L FT-sequence driver (<c>DriveFreeThrows</c>).
    /// The free-throw count is derived from the stamped (<see cref="ShotResult"/>,
    /// <see cref="ShotLocation"/>) pair: and-1 = 1, fouled miss = 2, fouled miss on a
    /// three = 3 — never a 1-and-1. Kept SEPARATE from
    /// <see cref="ResolveFreeThrows"/> (Roll D's bonus path) for now; possible
    /// future unification is an open fork.</summary>
    ResolveShootingFreeThrows,

    /// <summary>A missed shot deflected out of bounds off the defender and the
    /// offense retained it: as of Contextification #6, the resolver re-runs Roll A
    /// carrying the current court-state rather than parking at a stub. Post-cross
    /// (frontcourt is already latched), so the backcourt losses are unreachable and
    /// the re-inbound almost always CleanEntry's back into the set. Same loop shape
    /// as <see cref="ResumeInbound"/>.</summary>
    ResolveSidelineInbound,

    /// <summary>The offense secured the offensive rebound: executes Roll K
    /// (offensive-rebound resolution). Roll K is a gate with mixed ends:
    /// TERMINALS (OffensiveFoul / DeadBallTurnover / LiveBallTurnover — ball flips)
    /// and CONTINUES (PutBack → Roll H with a putback ticket; ResetOffense → Roll E
    /// on a blank slate; DefensiveFoul → the charge-and-fork; JumpBall → the arrow
    /// node). PutBack and ResetOffense keep the SAME possession alive — the
    /// re-entrant loop the Governor never sees.</summary>
    ResolveOffensiveRebound,

    /// <summary>RETIRED (Contextification #1). Roll J's Push no longer emits this
    /// kind: it routes into <see cref="IntoPlayerSelection"/> with FastBreak stamped,
    /// so a break produces a shot through the shared rolls. The resolver throws
    /// <see cref="System.InvalidOperationException"/> if anything routes here — it is
    /// a dead wiring path, kept as a named kind so the throw is legible.</summary>
    IntoTransition,

    /// <summary>A missed FINAL free throw left the ball live: executes Roll M
    /// (free-throw rebound resolution). Roll M is a gate with mixed ends (the Roll I
    /// shape): TERMINALS (DefensiveRebound → transition; LooseBallFoulOnOffense /
    /// OutOfBoundsOffOffense → dead ball to the defense) and CONTINUES
    /// (OffensiveRebound → Roll K with FreeThrow source; LooseBallFoulOnDefense →
    /// the charge-and-fork; OutOfBoundsOffDefense → sideline inbound; JumpBall →
    /// the arrow node). Distinct from <see cref="ResolveRebound"/> (field-goal
    /// rebound) because the FT-rebound population differs (everyone lined up along
    /// the lane, no shooter crashing) — flagged, not merged.</summary>
    ResolveFTRebound,
}
