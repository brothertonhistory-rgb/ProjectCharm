namespace Charm.Engine;

/// <summary>How the possession began. Roll A only handles dead-ball inbounds;
/// transition / live-ball entries are out of scope for this node.</summary>
public enum EntryType
{
    DeadBallInbound
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
/// shot is from this zone," the durable fact the future make/miss roll (Roll H)
/// reads ALONGSIDE <see cref="SelectedSlot"/> to resolve the matchup into points.
/// Named ShotType (not ShotLocation) to read cleanly at the call sites; its type
/// is <see cref="ShotLocation"/>.</para></param>
public sealed record PossessionState(
    int PossessionNumber,
    TeamSide Offense,
    TeamSide Defense,
    EntryType Entry,
    Slot? SelectedSlot = null,
    ShotLocation? ShotType = null);
