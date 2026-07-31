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
/// <para><b>The committer is not chosen at the charge site.</b>
/// <see cref="DefensiveFoulCharge.Resolve"/> is static and RNG-free and stays that way
/// — it emits this event bare, carrying only <see cref="IsReachIn"/>. The resolver
/// ENRICHES it with the committer at the single point where every such event is
/// harvested off its continuation, which is the one place the foul stream, the live
/// game and the defense are all in hand at once. So the charge helper keeps its exact
/// signature and its "consumes no randomness" contract, and every existing check that
/// drives it directly is untouched.</para>
/// </summary>
public readonly record struct NonShootingFoulEvent(bool IsReachIn)
{
    /// <summary>
    /// S87: the DEFENDING seat that committed this foul, drawn at the whistle by the
    /// resolver on its dedicated foul stream, using the weighting <see cref="IsReachIn"/>
    /// selects. 1–5 and occupied on every real game path.
    ///
    /// <para>Init-only with a 0 default, so the charge helper's existing construction
    /// (<c>new NonShootingFoulEvent(IsReachIn: …)</c>) stays valid and the enrichment is
    /// a <c>with</c>-expression at the harvest point — a pure append, the same seam
    /// pattern <see cref="RoutingOutcome"/> uses.</para>
    /// </summary>
    public int CommitterSlot { get; init; }

    /// <summary>
    /// S87: the PlayerId of the man in <see cref="CommitterSlot"/> at the moment of the
    /// whistle. Carried alongside the seat because the personal-foul count is keyed by
    /// player, and the seat can change hands later in the game. 0 is the degenerate
    /// harness-only "no one on the floor" sentinel.
    /// </summary>
    public int CommitterPlayerId { get; init; }
}
