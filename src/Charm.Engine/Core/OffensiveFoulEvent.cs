namespace Charm.Engine;

/// <summary>
/// One OFFENSIVE foul recorded during the possession walk — the third foul ledger,
/// alongside <see cref="ShootingFoulEvent"/> and <see cref="NonShootingFoulEvent"/>.
/// Before S87 an offensive foul left no foul record at all: the charge left a TURNOVER
/// record (the committer was named, but as a turnover, not a foul) and the loose-ball
/// foul on the offense left nothing whatsoever. Neither reached any foul count.
///
/// <para><b>Two kinds, one man, no team foul.</b> Both count toward the committer's
/// five and NEITHER touches the team-foul stream — Emmett's ruling: an offensive foul
/// is charged to the man, never to the team, so it moves no bonus and no half counter.
/// The team-foul code paths are untouched by this event's existence, and the harness
/// asserts that rather than assuming it.</para>
///
/// <para><see cref="IsLooseBall"/> separates them:
/// <list type="bullet">
///   <item>FALSE — the CHARGE family: a player-control foul, push-off, or illegal
///   screen. Reaches the walk as an <c>OffensiveFoul</c> terminal from the entry
///   (Roll A), the turnover-type pie (Roll C), or the offensive-rebound scrum (Roll K),
///   and the resolver rolls a descriptive flavor for it. The committer is the man the
///   engine ALREADY names for this terminal (the selected shooter if one was selected,
///   otherwise <see cref="TurnoverInteriorPicker"/>) — S87 reuses that man rather than
///   inventing a second, disagreeing answer to "who charged?". Consumes no new
///   randomness.</item>
///   <item>TRUE — the SCRUM foul: a loose-ball foul on the offense off a field-goal or
///   free-throw rebound (Rolls I and M). No man was named for this before S87. The
///   committer is drawn on the same interior weighting the charge uses — Emmett's
///   ruling: it is the men in the scrum, not the guard standing at the top of the
///   key — so the two kinds share one rule for who commits an offensive foul.</item>
/// </list></para>
///
/// <para><see cref="CommitterSlot"/> is an OCCUPIED seat of the OFFENSE on every real
/// game path; <see cref="CommitterPlayerId"/> is that man's id, carried alongside the
/// seat because the personal-foul tracker is keyed by player and the identity must
/// survive a later substitution into the same seat. A <see cref="CommitterPlayerId"/>
/// of 0 is the degenerate harness-only "no one on the floor" case (see
/// <see cref="PersonalFoulTracker.Increment"/>).</para>
/// </summary>
public readonly record struct OffensiveFoulEvent(
    int  CommitterSlot,
    int  CommitterPlayerId,
    bool IsLooseBall);
