namespace Charm.Engine;

/// <summary>
/// One team's on-court five. Per-team (NOT a shared both-sides bundle like
/// <see cref="FoulTracker"/>) because this is the attachment point everything
/// heavy hangs off later — per-player stat lines, the rated players that fill
/// slots, the selection roll. Each team's machinery grows independently; neither
/// lineup knows the other. (FoulTracker bundles both sides because team fouls are
/// a thin shared concern with nothing per-team to grow; a lineup is the opposite.)
///
/// Stands up holding five empty <see cref="Slot"/> identities, numbered 1–5. No
/// roster, no fill, no players yet — those are deferred content. This layer only
/// proves the five positions EXIST and are NAMEABLE.
///
/// Substitution (swapping who fills a slot) is future work — noted, not built.
/// When it lands it mutates this object; nothing else need change. Same deferral
/// shape as FoulTracker's half-reset.
/// </summary>
public sealed class Lineup
{
    public const int Size = 5;

    private readonly Slot[] _onCourt;

    public Lineup(TeamSide side)
    {
        Side = side;
        _onCourt = new Slot[Size];
        for (var i = 0; i < Size; i++)
            _onCourt[i] = new Slot(side, i + 1);   // slots numbered 1–5
    }

    public TeamSide Side { get; }

    /// <summary>The five active slots, numbered 1–5. Read-only: substitutions go
    /// through a future method on this object, never by reaching into the five.</summary>
    public IReadOnlyList<Slot> OnCourt => _onCourt;

    /// <summary>Name a single slot by its stable number 1–5. This is the entity a
    /// future stat attributes to and the selection roll picks among.</summary>
    public Slot SlotAt(int number)
    {
        if (number < 1 || number > Size)
            throw new ArgumentOutOfRangeException(nameof(number), number,
                $"Slot number must be 1–{Size}.");
        return _onCourt[number - 1];               // 1–5 maps to array 0–4
    }
}
