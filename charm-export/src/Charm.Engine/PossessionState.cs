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
/// <param name="Offense">Identifier of the team with the ball.</param>
/// <param name="Defense">Identifier of the defending team.</param>
/// <param name="Entry">How this possession started.</param>
public sealed record PossessionState(
    int PossessionNumber,
    string Offense,
    string Defense,
    EntryType Entry);
