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
    /// can record it; the live-ball entry node itself (the future transition roll)
    /// is not built yet, so the Governor TEMP-ROUTES a Transition start through
    /// Roll A's dead-ball entry for now — deliberately wrong basketball, replaced
    /// when that roll lands.</summary>
    Transition
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
/// inbound, and (this session) every not-yet-wired steal. Non-null
/// (<see cref="TransitionContext.Rebound"/>) means "this possession began on a
/// defensive rebound": the resolver routes it to Roll J instead of Roll A.</para></param>
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
public sealed record PossessionState(
    int PossessionNumber,
    TeamSide Offense,
    TeamSide Defense,
    EntryType Entry,
    Slot? SelectedSlot = null,
    ShotLocation? ShotType = null,
    ShotResult? Result = null,
    TransitionContext? TransitionContext = null,
    bool FastBreak = false);
