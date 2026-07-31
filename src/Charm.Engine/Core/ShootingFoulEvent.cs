namespace Charm.Engine;

/// <summary>
/// One shooting-foul event recorded during the possession walk — the zone the
/// shot was taken from and the shooter's slot number. Appended to the walk's
/// <c>shootingFouls</c> list each time the resolver reaches the
/// <see cref="ContinuationKind.ResolveShootingFreeThrows"/> edge (exactly once
/// per <see cref="ShotResult.MadeAndFouled"/> or <see cref="ShotResult.MissFouled"/>
/// resolution). A possession that had no shooting foul carries an empty list.
///
/// <para><see cref="ShooterSlot"/> is 1–5 on the overwhelming majority of
/// possessions (Roll E ran and named a shooter). It is 0 on the rare bonus-FT
/// putback path where Roll E never fired — <see cref="PossessionState.SelectedSlot"/>
/// was null at the edge, and 0 is the "no matched man" sentinel that routes the
/// committer draw to its flat fallback. Note this is an INPUT condition, not an
/// output one: the man who committed the foul is always a real man.</para>
/// </summary>
public readonly record struct ShootingFoulEvent(ShotLocation Zone, int ShooterSlot)
{
    /// <summary>
    /// S87: the DEFENDING seat that committed this foul, drawn at the whistle by the
    /// resolver on its dedicated foul stream. 1–5 and occupied on every real game path.
    ///
    /// <para>Init-only with a 0 default, so the existing positional construction
    /// (<c>new ShootingFoulEvent(zone, shooterSlot)</c>) stays valid — a pure append,
    /// the same seam pattern <see cref="RoutingOutcome"/> uses.</para>
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
