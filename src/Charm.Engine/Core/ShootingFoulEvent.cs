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
/// harness draw to the flat fallback in
/// <c>DrawFoulingDefender</c>.</para>
/// </summary>
public readonly record struct ShootingFoulEvent(ShotLocation Zone, int ShooterSlot);
