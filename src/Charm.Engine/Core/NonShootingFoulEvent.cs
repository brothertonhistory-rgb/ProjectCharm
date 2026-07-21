namespace Charm.Engine;

/// <summary>
/// One non-shooting defensive-foul event recorded during the possession walk —
/// the parallel of <see cref="ShootingFoulEvent"/> for the foul side the shot
/// never reaches. Emitted exactly once per team-foul increment at the single
/// choke point <see cref="DefensiveFoulCharge.Resolve"/>, so every non-shooting
/// foul in the possession — entry (Roll A), halfcourt (Roll B), player-action
/// (Roll F), and the situational loose-ball / transition / putback fouls (Rolls
/// I / J / K / M) — carries exactly one event, regardless of the bonus branch.
///
/// <para><see cref="IsReachIn"/> distinguishes the two attribution weightings.
/// TRUE on the pre-shot reach-in bucket (the A/B/F fouls that route through Roll
/// D and therefore carry a <see cref="FoulFlavor"/>): the committer is drawn in
/// proportion to each defender's full reach-in propensity
/// (<see cref="Matchup.ReachInPropensity"/>) — Discipline-primary, with the small
/// athleticism secondary and the slight perimeter lean. FALSE on the situational
/// bucket (Rolls I/J/K/M, which carry no flavor): the committer is drawn on the
/// Discipline factor alone (<see cref="Matchup.ReachInDisciplineFactor"/>), since
/// the perimeter lean is meaningless in a rebound scrum or a transition bump.</para>
///
/// <para>The committer is NOT chosen here — like the shooting-foul path, the draw
/// happens post-hoc in the harness attribution pass over the five defenders who
/// were on the floor at this possession. This event only records that a
/// non-shooting foul occurred and which weighting applies.</para>
/// </summary>
public readonly record struct NonShootingFoulEvent(bool IsReachIn);
