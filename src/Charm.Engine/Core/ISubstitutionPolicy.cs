namespace Charm.Engine;

/// <summary>
/// Which kind of non-terminal period boundary the Governor is reporting to a
/// <see cref="ISubstitutionPolicy"/>. A boundary is reported ONLY when a successor
/// period will actually run — i.e. the period that just ended did so with the score
/// TIED (regulation → overtime, or overtime → overtime) or it is a normal halftime.
/// A period that ends the game (regulation or overtime ending UNtied) is terminal and
/// is never reported.
/// </summary>
public enum PeriodBreakKind
{
    /// <summary>A regulation halftime boundary. The engine applies its halftime rest to
    /// the on-floor five before the callback (see <see cref="Governor"/>); the policy is
    /// expected to apply the matching rest to its benched players.</summary>
    Halftime,

    /// <summary>An overtime boundary (regulation → OT or OT → OT), reached only when the
    /// prior period ended tied. There is NO engine rest at an overtime boundary — recovery
    /// is regulation-only, never in OT — so the policy applies no rest chunk here either;
    /// benched and on-floor players are treated alike.</summary>
    Overtime
}

/// <summary>
/// The single seam by which the position-agnostic engine hands control to a
/// substitution policy. The engine knows nothing about guards, reserves, fatigue lines,
/// or rotations; it only calls these two methods and passes the live game. Everything
/// basketball — the fatigue fence, positional pairing, reclaim rules, the reserve list —
/// lives in the policy (in the harness, alongside generation and positions).
///
/// <para>Both methods are optional in practice: the Governor holds an
/// <c>ISubstitutionPolicy?</c> and calls nothing when it is null, so every existing
/// construction path (which passes no policy) is a strict no-op.</para>
///
/// <para><b>Ordering contract.</b> A possession's on-floor fatigue accrual happens at the
/// possession tail, before either callback fires for that boundary. So the players who
/// PLAYED possession N have already accrued N when the boundary after N is reported; a
/// player subbed in "effective possession N+1" first accrues in N+1 (the possession he
/// actually plays), and the outgoing player's stat line is closed at N. Substitutions are
/// therefore always stamped with the SUCCESSOR possession number.</para>
/// </summary>
public interface ISubstitutionPolicy
{
    /// <summary>
    /// Reported at every within-period possession boundary that has a real successor
    /// possession in the SAME period. Not reported at a period break (that is
    /// <see cref="OnPeriodBreak"/>) and not after a game-ending possession.
    /// </summary>
    /// <param name="game">The live game — the policy reads <c>game.Fatigue</c> and
    /// <c>game.RosterFor(side)</c>, and calls <c>Roster.Substitute</c> /
    /// <c>FatigueTracker.Recover</c>.</param>
    /// <param name="nextPossessionNumber">The successor possession's number. Any
    /// substitution made here is stamped with this value (the incoming player first
    /// plays this possession).</param>
    /// <param name="elapsedSeconds">The just-ended possession's actual (capped) elapsed
    /// game-clock seconds. Off-floor recovery is a function of these seconds, never a
    /// per-possession constant.</param>
    /// <param name="isDeadBall">True when the successor possession begins from a dead ball
    /// (a normal inbound or a frontcourt "ball advanced" inbound) — the only moments a
    /// substitution is legal. False for a live-ball transition start.</param>
    void OnPossessionBoundary(GameState game, int nextPossessionNumber, double elapsedSeconds, bool isDeadBall);

    /// <summary>
    /// Reported at every NON-terminal period boundary — a normal halftime, or any
    /// overtime boundary reached because the prior period ended tied. Never reported
    /// after a game-ending (untied) regulation or overtime period.
    /// </summary>
    /// <param name="game">The live game.</param>
    /// <param name="nextPossessionNumber">The first possession number of the NEXT period.
    /// A reclaim substitution made here is stamped with this value.</param>
    /// <param name="finalPossessionElapsedSeconds">The elapsed game-clock seconds of the
    /// possession that ENDED the prior period — the per-possession recovery slice the
    /// (suppressed) ordinary boundary callback would otherwise have applied to benched
    /// players.</param>
    /// <param name="kind">Halftime or Overtime — see <see cref="PeriodBreakKind"/>.</param>
    void OnPeriodBreak(GameState game, int nextPossessionNumber, double finalPossessionElapsedSeconds, PeriodBreakKind kind);
}
