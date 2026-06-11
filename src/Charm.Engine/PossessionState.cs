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
public sealed record PossessionState(
    int PossessionNumber,
    TeamSide Offense,
    TeamSide Defense,
    EntryType Entry);
