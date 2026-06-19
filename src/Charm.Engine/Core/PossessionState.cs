namespace Charm.Engine;

/// <summary>
/// How a possession began — the single, reconciled start-state enum. A terminal's
/// <see cref="PossessionConsequence"/> names one of these as the next possession's
/// entry, and the Governor reads it to choose how to start that possession.
/// <para>There is deliberately ONE enum here, not a parallel "start-state" concept
/// alongside it: a possession's entry IS its start-state. New entry kinds append
/// here as they are modelled.</para>
/// </summary>
public enum EntryType
{
    /// <summary>A dead-ball restart: the offense inbounds (after a made basket, a
    /// turnover out of bounds, a violation, a foul, a jump-ball award). Roll A is
    /// this entry's node.</summary>
    DeadBallInbound,

    /// <summary>A live-ball start: the new offense gets the ball in motion (a steal,
    /// a defensive rebound) and pushes the other way. Tagged here so a consequence
    /// can record it. The resolver routes a Transition entry (which always carries a
    /// recognized <see cref="TransitionContext.Source"/> — Rebound, FreeThrowRebound,
    /// or Steal) into ROLL J, the live transition-entry gate; a null-context
    /// Transition is produced by nothing and FAILS LOUD as a wiring-bug tripwire. It
    /// is NOT routed through Roll A's dead-ball entry.</summary>
    Transition,

    /// <summary>A dead-ball restart where the ball was already across the halfcourt
    /// line when the turnover occurred — the other team inbounds from the frontcourt
    /// (near their basket) and skips the backcourt bring-up entirely. Roll B is this
    /// entry's node; Roll A's bring-up (and all its backcourt-only ways to lose it)
    /// are bypassed. The pie odds on Roll B can later reflect the easier inbound
    /// situation (no full-court pressure possible).</summary>
    BallAdvanced
}

/// <summary>
/// The world a roll operates on. Deliberately lean: it carries only what Roll A
/// needs today. Future rolls will widen this record; that is expected and safe,
/// because rolls read the fields they care about and ignore the rest.
/// </summary>
/// <param name="PossessionNumber">Monotonic id for the possession (accounting anchor).</param>
/// <param name="Offense">The team with the ball this possession, as a fixed
/// per-game <see cref="TeamSide"/> identity (NOT a role-rotated label). Every
/// game — neutral court included — stamps both teams Home/Away up front; the
/// label is arbitrary on a neutral floor but stable, which is all the engine
/// needs. Offense/defense is the per-possession role layered over this identity.</param>
/// <param name="Defense">The defending team this possession, same identity basis.
/// On a foul this is the fouling team, so team fouls accumulate against identity
/// regardless of who holds the ball moment to moment.</param>
/// <param name="Entry">How this possession started.</param>
/// <param name="SelectedSlot">The on-court offensive slot the possession runs
/// through this time, stamped by Roll E (player selection). A per-possession
/// fact and a slot REFERENCE into the game-scoped lineup — never an owned or
/// attribute-bearing thing — the same shape as <see cref="Offense"/>/<see
/// cref="Defense"/> being TeamSide references rather than owned teams.
/// <para>Null until Roll E runs (and on possessions that never reach selection —
/// a turnover or foul at entry, a shot-clock violation: those end or divert
/// before a player is selected, so no slot is ever chosen). Non-null means "this
/// slot has the action," the durable fact the future shot/rebound rolls and the
/// attribution layer all read across the rest of the chain.</para></param>
/// <param name="ShotType">The location the shot comes from this possession,
/// stamped by Roll G (shot location) as one of five zones (Three / Long / Mid /
/// Short / Rim). The SECOND per-possession fact, layered after <see
/// cref="SelectedSlot"/> — a plain enum value, not a reference into anything.
/// <para>Null until Roll G runs (and on every possession that ends or diverts
/// before a clean shot attempt — a turnover, a foul, a block, a held ball: those
/// never reach shot location, so no zone is ever stamped). Non-null means "the
/// shot is from this zone," the durable fact the make/miss roll (Roll H) and the
/// downstream scoring layer read ALONGSIDE <see cref="SelectedSlot"/> to resolve
/// the matchup into points. Named ShotType (not ShotLocation) to read cleanly at
/// the call sites; its type is <see cref="ShotLocation"/>.</para></param>
/// <param name="Result">How the located shot resolved this possession, stamped by
/// Roll H (make/miss) as one of six outcomes (Made / MadeAndFouled / Miss /
/// MissFouled / MissOutOfBoundsLost / MissOutOfBoundsRetained). The THIRD
/// per-possession fact, layered after <see cref="ShotType"/> — a plain enum value.
/// <para>Null until Roll H runs (and on every possession that ends or diverts
/// before the shot resolves — a turnover, a foul, a block, a held ball, or a shot
/// that never got off: those never reach make/miss, so no result is ever stamped).
/// Non-null means "the shot resolved this way," the durable fact the future
/// scoring layer reads ALONGSIDE <see cref="ShotType"/> to DERIVE the point value
/// (2 vs. 3) and the free-throw count (1 / 2 / 3) — neither is stored here. Roll H
/// stamps the outcome; the derivations are downstream. Its type is <see
/// cref="ShotResult"/>.</para></param>
/// <param name="TransitionContext">The transition CONTEXT TICKET this possession
/// began with — the cross-possession ticket memory Roll J reads to choose its
/// run-or-not pie. Set by the spawning terminal's
/// <see cref="PossessionConsequence.TransitionContext"/> and threaded onto this
/// possession by the Governor. Appended after the per-possession facts because all
/// callers construct by name or <c>with</c>, so position is free; conceptually it
/// belongs with <see cref="Entry"/> (it is part of the start-state).
/// <para>Null on every possession that did NOT begin in transition — every dead-ball
/// inbound. Non-null means "this possession began in transition": the resolver routes
/// it to Roll J instead of Roll A, and the ticket's <see cref="TransitionContext.Source"/>
/// (Rebound, FreeThrowRebound, or — as of Contextification #3 — Steal) selects Roll J's
/// run-or-not pie.</para></param>
/// <param name="FastBreak">Whether the possession is RUNNING a live break RIGHT NOW —
/// the fast-break marker Roll J stamps on its <c>Push</c> arm (the decision to run).
/// Distinct from <see cref="TransitionContext"/>: that records how the possession
/// STARTED (off a rebound), and is non-null on BOTH a Push and a Settle (both entered
/// Roll J off a board), so it cannot tell "we ran" from "we pulled it out." This flag
/// is the decision Roll J made. Read by Roll E's generator to draw the transition
/// selection pie instead of the halfcourt pie; it also rides forward so Roll G (shot
/// location) and Roll H (make/miss) can read it for their transition tilts LATER (a
/// follow-up — those generators are transition-blind this session).
/// <para>A single bool because there is exactly one break flavor today — the same
/// "single bit suffices" call as <see cref="Continue.Putback"/>. Richer break memory
/// (numbers advantage, leak-out) appends later as a nullable field, no teardown.
/// False on every halfcourt possession (Roll B Proceed, Roll J Settle) and CLEARED on
/// a Roll K <c>ResetOffense</c> re-entry — a reset off a missed break is a fresh
/// halfcourt play, so it must NOT redraw the transition selection pie. The marker is
/// deliberately scoped to the break: it does not leak past a reset.</para></param>
/// <param name="Frontcourt">The COURT-STATE of the possession — false = BACKCOURT
/// (the offense is still bringing the ball up: the 10-second count, the backcourt
/// shot-clock, and the 5-second inbound are all in play), true = FRONTCOURT (the ball
/// is across and into the set: those backcourt-only ways to lose it are gone). The
/// origin signal Contextification #6 introduced so Roll A can pick its loss CONTEXT —
/// a turnover while still in the backcourt routes to Roll C's
/// <see cref="TurnoverContext.EntryBackcourt"/> pie (where the backcourt violations
/// live), a turnover already across routes to the <see cref="TurnoverContext.Halfcourt"/>
/// pie (where they cannot happen).
/// <para>FALSE on every brand-new possession (the Governor constructs by name and
/// never sets it, so a fresh inbound defaults to backcourt — it must be brought up).
/// LATCHES to true the instant Roll A's <c>CleanEntry</c> hands off to Roll B (the
/// offense successfully crossed into the set) and NEVER flips back within the
/// possession — there is no spatial "return to the backcourt" in the role-based model;
/// over-and-back is a Roll C frontcourt loss, not a court-state flip. A keep-the-ball
/// re-inbound (Roll D below-bonus <c>ResumeInbound</c>, an OOB-retained
/// <c>ResolveSidelineInbound</c>) re-runs Roll A carrying WHATEVER court-state is
/// current: a foul on the backcourt bring-up resumes backcourt (still must cross), a
/// foul or OOB-retain once across resumes frontcourt (no backcourt losses). A single
/// bool, the same "single bit suffices" shape as <see cref="FastBreak"/>; the finer
/// spot-flip (the OTHER team starting in front after a backcourt turnover) is deferred
/// to a later task.</para></param>
/// <param name="PressMode">Whether the defending team is applying a full-court press
/// on this specific possession, and with what profile. Stamped ONCE per possession by
/// <see cref="Resolver.RunPossession"/> before invoking
/// <see cref="IRollAPieGenerator.Generate"/> — the generator reads this stamp and
/// never draws its own RNG to decide.
/// <para>Defaults to <see cref="Charm.Engine.PressMode.None"/> so every possession
/// that is not a fresh dead-ball (Transition, BallAdvanced) and every continuation
/// state created via <c>with</c> from a brand-new possession arrives as non-pressed
/// until the Resolver stamps it. Because <see cref="Resolver.RunPossession"/> is
/// called exactly once per possession, the stamp is always set before any generator
/// is invoked, making the default safe and making an <c>Unassigned</c> sentinel
/// unnecessary. The field survives every <c>with</c> in the possession chain
/// (including the re-inbound paths <c>ResumeInbound</c> and
/// <c>ResolveSidelineInbound</c>) so Roll A at a re-inbound reads the same mode
/// the possession started with — the press decision does not change mid-possession.
/// </para>
/// <para>On Transition and BallAdvanced entries, <c>RunPossession</c> never stamps
/// the field (those entries skip the press roll entirely); the default <c>None</c>
/// is the correct value because a live-ball transition or a frontcourt inbound is
/// never preceded by a full-court press decision.</para></param>
public sealed record PossessionState(
    int PossessionNumber,
    TeamSide Offense,
    TeamSide Defense,
    EntryType Entry,
    Slot? SelectedSlot = null,
    ShotLocation? ShotType = null,
    ShotResult? Result = null,
    TransitionContext? TransitionContext = null,
    bool FastBreak = false,
    bool Frontcourt = false,
    PressMode PressMode = PressMode.None,
    /// <param name="UsagePressure">The FOURTH per-possession fact, stamped by Roll E
    /// alongside <see cref="SelectedSlot"/>. The selected shooter's <b>volume
    /// pressure</b> — <c>max(0, finalShare − equalShare)</c> — where
    /// <c>equalShare = 1.0 / populatedCount</c> (five on-court players → 0.20).
    /// <para>Null until Roll E runs (and on possessions that divert before
    /// selection — a turnover or foul at entry — it is never set). Non-null means
    /// Roll E ran: zero when the selected slot is at or below the equal share, or
    /// on a FastBreak (no volume load on a transition possession). Positive when
    /// the shooter is carrying above an even load.</para>
    /// <para>This is the shooter's OWN volume load. Gravity and spacing layers
    /// (deferred) will contribute their own NAMED components alongside this one —
    /// they are NOT merged into this field.</para>
    /// <para><b>Leak guard:</b> cleared to null by Roll K's <c>ResetOffense</c>
    /// — a reset restarts selection from scratch, so the stale pressure must
    /// not ride through.</para></param>
    double? UsagePressure = null,
    /// <param name="UsageResidualPressure">The FIFTH per-possession fact, stamped
    /// by Roll G alongside <see cref="ShotType"/>. The volume load Roll G could
    /// NOT absorb into a wider shot diet this possession — the load that stayed
    /// stuck after the bounded diet expansion.
    /// <para>Null until Roll G runs. After Roll G: <c>0.0</c> when the load was
    /// fully absorbed (a versatile shooter under ordinary defense), when there was
    /// no pressure (<see cref="UsagePressure"/> was zero), or on a FastBreak.
    /// Positive for a forced specialist (the load that could not drain into
    /// alternate zones). Read ONLY by Roll H's residual-penalty term.</para>
    /// <para><b>Leak guard:</b> cleared to null by Roll K's <c>ResetOffense</c>
    /// alongside <see cref="UsagePressure"/> — a reset restarts the shot-diet
    /// calculation from scratch.</para></param>
    double? UsageResidualPressure = null,
    /// <param name="ShooterAttentionShare">The SIXTH per-possession fact, stamped
    /// by Roll E alongside <see cref="UsagePressure"/>. The defensive attention
    /// share allocated to the selected shooter — the selected slot's value from
    /// the five-player attention pie (normalized [0,1]; equal share = 0.20).
    /// <para>Null until Roll E runs (same lifecycle as <see cref="UsagePressure"/>).
    /// Non-null means Roll E ran and the attention generator has allocated its
    /// 100-point pie. Values above 0.20 indicate above-average defensive focus;
    /// below 0.20 indicate the shooter was left relatively open.</para>
    /// <para><b>Leak guard:</b> cleared to null by Roll K's <c>ResetOffense</c>
    /// alongside <see cref="UsagePressure"/> — a reset restarts selection and
    /// the attention allocation from scratch.</para></param>
    double? ShooterAttentionShare = null,
    /// <param name="TeamBaseOpenness">The team-level openness scalar for this
    /// possession's offense, stamped by Roll E alongside
    /// <see cref="ShooterAttentionShare"/>. The asymmetric gravity×spacing
    /// interaction result normalized to [0,1]: low for both pure-spacing and
    /// pure-gravity lineups, high for a lineup with a dominant gravity source and
    /// four perimeter threats around it.
    /// <para>Null until Roll E runs. Cleared by Roll K's <c>ResetOffense</c>.</para>
    /// </param>
    double? TeamBaseOpenness = null,
    /// <param name="TeamGravityLevel">Normalized gravity-source term for the
    /// offensive lineup this possession [0,1]. Stamped by Roll E alongside
    /// <see cref="TeamBaseOpenness"/>. High when the offense has at least one
    /// dominant rim-pressure threat; near-zero for a pure-spacing lineup.
    /// <para>Null until Roll E runs. Cleared by Roll K's <c>ResetOffense</c>.</para>
    /// </param>
    double? TeamGravityLevel = null,
    /// <param name="TeamSpacingLevel">Normalized spacing-field term for the
    /// offensive lineup this possession [0,1]. Stamped by Roll E alongside
    /// <see cref="TeamBaseOpenness"/>. Accumulates across the four non-primary-
    /// gravity players; high when the floor is surrounded by credible perimeter
    /// shooters.
    /// <para>Null until Roll E runs. Cleared by Roll K's <c>ResetOffense</c>.</para>
    /// </param>
    double? TeamSpacingLevel = null,
    /// <param name="TeamConversionQuality">The passing/playmaking conversion quality
    /// for this possession's offense [0,1]. Stamped by Roll E alongside
    /// <see cref="TeamBaseOpenness"/> (Phase 27 Session 2). Used by Roll H's bonus-only
    /// passing-converter block — a separate, attention-independent make% consequence
    /// distinct from C1. Neutral (0.0) when the formula placeholder is in effect.
    /// <para>Null until Roll E runs. Cleared by Roll K's <c>ResetOffense</c>.</para>
    /// </param>
    double? TeamConversionQuality = null,
    /// <param name="ReboundSlot">The offensive slot that secured the offensive
    /// rebound this possession (Phase 31), stamped at the shared
    /// <see cref="ContinuationKind.ResolveOffensiveRebound"/> node — distinct from
    /// <see cref="SelectedSlot"/>, which is the SHOOTER. Null except between an
    /// offensive board being secured and the possession resetting or ending.
    /// <para>Null until an offensive rebound is secured. Cleared by Roll K's
    /// <c>ResetOffense</c>; carried forward on the PutBack arm so a future
    /// putback-attribution read (Phase 32) can use it.</para>
    /// </param>
    Slot? ReboundSlot = null);
