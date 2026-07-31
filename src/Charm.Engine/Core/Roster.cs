namespace Charm.Engine;

/// <summary>
/// One team's slot-to-player map for a single game. The bridge between the
/// engine's stable <see cref="Slot"/> identities and the rated <see cref="Player"/>
/// objects that fill them.
///
/// <para><b>Why a separate object (not on Lineup or GameState directly).</b>
/// <see cref="Lineup"/> is a pure identity layer — five stable, numbered seats.
/// <see cref="GameState"/> is the live game object, ephemeral and per-game. A
/// <see cref="Roster"/> is the thing that persists: the almanac holds a roster
/// per game per team, rosters point at players, players accumulate career stat
/// lines. The sports-reference shape is: player page → season logs → game logs,
/// navigable because the roster is the bridge. Building it here rather than
/// inline on Lineup or GameState means the live game and the historical archive
/// share exactly one seam, not two diverging ones.</para>
///
/// <para><b>Substitution model.</b> A slot is a stable seat for the whole game;
/// a substitution swaps WHO fills the seat, not what the seat IS. The roster owns
/// an append-only substitution log: when player B replaces player A in slot 3, a
/// <see cref="SubstitutionEntry"/> is appended (player A out, player B in, at
/// which possession). Stat attribution reads the log to assign credit correctly
/// across the sub. The current occupant is always the last entry for that slot.
/// </para>
///
/// <para><b>Phase 1 scope.</b> The substitution log is built and ready; no game
/// code drives it yet (subs are future infrastructure, exactly as Lineup's doc
/// notes). The harness populates via <see cref="SetStarter"/> — the same five-
/// player starting lineup for the whole check. <see cref="PlayerAt"/> resolves the
/// seam end to end.</para>
/// </summary>
public sealed class Roster
{
    // -------------------------------------------------------------------------
    // Substitution log entry
    // -------------------------------------------------------------------------

    /// <summary>
    /// One substitution event: a player entering a slot at a possession boundary.
    /// The log is append-only; the current occupant of a slot is the entry with the
    /// highest <see cref="AtPossession"/> for that slot. A starter's entry has
    /// <see cref="AtPossession"/> = 1 (they were "subbed in" at the tip-off).
    /// </summary>
    public sealed record SubstitutionEntry(
        Slot         Slot,
        Player       Player,
        int          AtPossession);

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly TeamSide _side;
    private readonly List<SubstitutionEntry> _log = new();

    // S87: the game's personal-foul tracker, injected by GameState. Consulted on every
    // seating so a disqualified man cannot be put back on the floor. Null only for a
    // roster built outside a game (no game, no disqualifications).
    private readonly PersonalFoulTracker? _personalFouls;

    // Fast lookup: current occupant per slot number (1–5). Null = not yet filled.
    private readonly Player?[] _current = new Player?[Lineup.Size];  // index 0 = slot 1

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <param name="side">Which team this roster belongs to. Must match the
    /// <see cref="Slot.Side"/> of every slot passed to <see cref="SetStarter"/>
    /// and <see cref="Substitute"/>.</param>
    /// <param name="personalFouls">S87: the game's personal-foul tracker. Supplied by
    /// <see cref="GameState"/>, which is the ONLY place a roster is constructed — so
    /// every roster in a live game carries it. Optional (null) so a roster built in
    /// isolation still works; a null tracker means no man can be disqualified and the
    /// guard is inert.</param>
    public Roster(TeamSide side, PersonalFoulTracker? personalFouls = null)
    {
        _side          = side;
        _personalFouls = personalFouls;
    }

    // -------------------------------------------------------------------------
    // Population
    // -------------------------------------------------------------------------

    /// <summary>
    /// Place a player in a slot at game start (possession 1). Equivalent to a
    /// substitution at the opening — the starting five are the first entries in the
    /// log. Call once per slot before the game begins; calling again for the same
    /// slot before any possession has run is a wiring bug and fails loud.
    /// </summary>
    public void SetStarter(Slot slot, Player player)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(player);

        var idx = slot.Number - 1;
        if (_current[idx] is not null)
            throw new InvalidOperationException(
                $"Slot {slot} already has a starter ({_current[idx]!.Name}). " +
                "Call Substitute to replace a player mid-game.");
        // S87: both seating doors carry the guard, so the rule is a property of the seat
        // rather than of one method. Inert at the tip in practice (personal fouls are
        // game-scoped, so nobody is disqualified before the game starts), but the rule
        // must not depend on that staying true.
        RefuseIfDisqualified(player);

        _current[idx] = player;
        _log.Add(new SubstitutionEntry(slot, player, AtPossession: 1));
    }

    /// <summary>
    /// Swap who occupies a slot mid-game. Appends a <see cref="SubstitutionEntry"/>
    /// to the log; from <paramref name="atPossession"/> onward, attribution for
    /// that slot goes to <paramref name="incoming"/>. The outgoing player's stat
    /// line is closed at <paramref name="atPossession"/> - 1.
    /// </summary>
    public void Substitute(Slot slot, Player incoming, int atPossession)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(incoming);
        if (atPossession < 2)
            throw new ArgumentOutOfRangeException(nameof(atPossession), atPossession,
                "Substitutions happen at possession 2 or later; use SetStarter for the opening lineup.");
        RefuseIfDisqualified(incoming);

        _current[slot.Number - 1] = incoming;
        _log.Add(new SubstitutionEntry(slot, incoming, atPossession));
    }

    // -------------------------------------------------------------------------
    // Lookup
    // -------------------------------------------------------------------------

    /// <summary>
    /// The player currently in <paramref name="slot"/>, or null if the slot has
    /// not been filled yet. This is the seam the resolver will walk when generators
    /// become attribute-driven: <c>GameState.RosterFor(side).PlayerAt(slot)</c>.
    /// </summary>
    public Player? PlayerAt(Slot slot)
    {
        ValidateSlot(slot);
        return _current[slot.Number - 1];
    }

    /// <summary>
    /// The player who occupied <paramref name="slot"/> AT a specific possession —
    /// for the almanac's attribution pass. Returns null if the slot was unfilled at
    /// that possession.
    /// </summary>
    public Player? PlayerAt(Slot slot, int atPossession)
    {
        ValidateSlot(slot);
        // Last entry for this slot with AtPossession <= the requested possession.
        return _log
            .Where(e => e.Slot == slot && e.AtPossession <= atPossession)
            .OrderByDescending(e => e.AtPossession)
            .FirstOrDefault()
            ?.Player;
    }

    /// <summary>The full substitution log, in append order. The almanac reads this
    /// to reconstruct per-player minutes and stat windows.</summary>
    public IReadOnlyList<SubstitutionEntry> Log => _log;

    /// <summary>The team this roster belongs to.</summary>
    public TeamSide Side => _side;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// S87: the disqualification guard. A man who has fouled out may never occupy a seat
    /// again in this game.
    ///
    /// <para><b>Why it lives here and not in the substitution policy.</b> A roster is
    /// constructed in exactly one place (<see cref="GameState"/>'s constructor) and its
    /// occupancy is mutated through exactly two doors — <see cref="SetStarter"/> at the
    /// tip and <see cref="Substitute"/> mid-game — with the mid-game door called from a
    /// single site. Putting the rule on the door means no present or future substitution
    /// policy can re-insert a disqualified man, whether by bug or by design; putting it
    /// in the policy would mean re-proving it for every policy ever written.</para>
    ///
    /// <para><b>Why it throws rather than declining quietly.</b> Reaching here is a
    /// wiring bug, not a game situation: the policy is required to filter disqualified
    /// men out of its candidate list before it ever selects one, and a silent refusal
    /// would leave a seat holding someone the caller believes it replaced. This is the
    /// same shape as <see cref="SetStarter"/>'s existing double-seat throw. It is NOT
    /// the escape hatch — a disqualified man who cannot be REPLACED simply stays on the
    /// floor and nothing is called here at all.</para>
    /// </summary>
    private void RefuseIfDisqualified(Player incoming)
    {
        if (_personalFouls is null) return;
        if (!_personalFouls.IsDisqualified(incoming.PlayerId)) return;

        throw new InvalidOperationException(
            $"Cannot seat {incoming.Name} (PlayerId {incoming.PlayerId}) on {_side}: he has " +
            $"{_personalFouls.CountFor(incoming.PlayerId)} personal fouls and the disqualification " +
            $"threshold is {_personalFouls.FoulOutThreshold}. A disqualified player may not return " +
            "to the floor; a substitution policy must exclude him from its candidates.");
    }

    private void ValidateSlot(Slot slot)
    {
        if (slot.Side != _side)
            throw new ArgumentException(
                $"Slot {slot} belongs to {slot.Side} but this roster is for {_side}.",
                nameof(slot));
        if (slot.Number < 1 || slot.Number > Lineup.Size)
            throw new ArgumentOutOfRangeException(nameof(slot),
                $"Slot number must be 1–{Lineup.Size}.");
    }
}
