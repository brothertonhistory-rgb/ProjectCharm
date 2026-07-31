namespace Charm.Engine;

/// <summary>
/// Owns per-player PERSONAL foul accumulation for a single GAME, and answers the one
/// question the rest of the engine asks of it: "is this man disqualified?"
///
/// <para><b>Why this is not <see cref="FoulTracker"/>.</b> The two count different
/// things on different clocks and must never be conflated. <see cref="FoulTracker"/>
/// is TEAM fouls, HALF-scoped, and resets at the half (<see
/// cref="FoulTracker.ResetForNewHalf"/>) — that is the bonus's clock. Personal fouls
/// are per-player and GAME-scoped: a man who picks up three in the first half carries
/// all three into the second. Storing personals on the half-scoped tracker would
/// forgive everyone's first-half fouls at the break, and no existing bonus check would
/// notice.</para>
///
/// <para><b>Storage.</b> Structurally mirrors <see cref="FatigueTracker"/> — the other
/// piece of game-scoped per-player state — a PlayerId-keyed dictionary held on <see
/// cref="GameState"/>, mutated through <see cref="Increment"/>, read through <see
/// cref="CountFor"/>. Empty at construction; an unseen PlayerId reads 0.</para>
///
/// <para><b>Counts are NOT capped at the threshold.</b> A disqualified man who cannot
/// be replaced (no eligible reserve — the escape hatch) stays on the floor and keeps
/// fouling, and the record must say so. Six and seven are reachable and are reported
/// honestly rather than clamped, which is why the season page's personal-foul
/// distribution tops out at a "5+" bucket rather than "5".</para>
///
/// <para><b>Determinism.</b> Draws no randomness. Increments are a pure function of the
/// foul events the walk produced, so a replayed seed reproduces every count exactly and
/// this tracker perturbs no other RNG stream.</para>
/// </summary>
public sealed class PersonalFoulTracker
{
    /// <summary>The default disqualification threshold — NCAA classic, five and out.</summary>
    public const int DefaultFoulOutThreshold = 5;

    private readonly int _foulOutThreshold;

    // PlayerId -> personal fouls committed this game. An absent key means zero.
    private readonly Dictionary<int, int> _count = new();

    /// <param name="foulOutThreshold">The personal-foul count at which a player is
    /// disqualified. NCAA classic: 5. Must be at least 1 — a threshold below 1 would
    /// disqualify every player before he committed a foul, which is not a rule any
    /// basketball has.</param>
    public PersonalFoulTracker(int foulOutThreshold = DefaultFoulOutThreshold)
    {
        if (foulOutThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(foulOutThreshold), foulOutThreshold,
                "FoulOutThreshold must be at least 1: a player is disqualified on his " +
                "FoulOutThreshold-th personal foul, so a threshold below 1 would disqualify " +
                "every player before he committed one.");

        _foulOutThreshold = foulOutThreshold;
    }

    /// <summary>The configured disqualification threshold (5 = five and out).</summary>
    public int FoulOutThreshold => _foulOutThreshold;

    /// <summary>
    /// Charge one personal foul to <paramref name="playerId"/>. Never capped — see the
    /// class note.
    ///
    /// <para>A <paramref name="playerId"/> of 0 or below is the "no man to charge"
    /// sentinel and is a deliberate no-op. It arises only on the degenerate harness path
    /// where a foul resolves with ZERO seats occupied (a check that drives a foul node
    /// directly against a game with no roster seated); there is no one on the floor to
    /// charge, so nothing is charged. No real game path reaches it — every season and
    /// full-game path seats all ten men before the tip.</para>
    /// </summary>
    public void Increment(int playerId)
    {
        if (playerId <= 0) return;
        _count[playerId] = _count.GetValueOrDefault(playerId) + 1;
    }

    /// <summary>Personal fouls committed by <paramref name="playerId"/> this game.
    /// Returns 0 for any player who has not yet fouled.</summary>
    public int CountFor(int playerId) => _count.GetValueOrDefault(playerId);

    /// <summary>
    /// True once <paramref name="playerId"/> has reached <see cref="FoulOutThreshold"/>.
    /// Stays true for the rest of the game — a disqualification is never rescinded, and
    /// a man who keeps fouling past the threshold (the escape hatch) stays disqualified.
    /// </summary>
    public bool IsDisqualified(int playerId) => CountFor(playerId) >= _foulOutThreshold;

    /// <summary>Every player who has committed at least one personal foul, with his
    /// count. The harness reconciles this against the possession-record event streams;
    /// the season page bins it.</summary>
    public IReadOnlyDictionary<int, int> Counts => _count;
}
