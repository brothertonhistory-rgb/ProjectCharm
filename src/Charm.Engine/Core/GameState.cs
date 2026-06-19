namespace Charm.Engine;

/// <summary>Which team — a stable identity that does NOT rotate with
/// offense/defense. The possession arrow points at one of these.</summary>
public enum TeamSide
{
    Home,
    Away
}

/// <summary>
/// The possession arrow's state. Three-valued because of the opening tip: the
/// arrow is <see cref="Off"/> until the first jump ball turns it on. After that
/// it always points at a team (<see cref="Home"/> / <see cref="Away"/>).
/// </summary>
public enum ArrowState
{
    /// <summary>Not yet set — before the opening tip is resolved (and reset each
    /// overtime). A jump ball in this state is a real contest (a coin flip for
    /// now), not an arrow read.</summary>
    Off,
    Home,
    Away
}

/// <summary>
/// Persistent game-wide state that survives ACROSS possessions — unlike
/// <see cref="PossessionState"/>, which is per-possession. This is where the
/// possession arrow has to live, because a jump ball in one possession sets who
/// gets the ball in a later one.
///
/// The arrow now has real, complete behavior. Team fouls now have real behavior
/// too, via <see cref="Fouls"/> (a <see cref="FoulTracker"/>). The on-court five
/// per team now lives here too, via <see cref="HomeLineup"/> / <see cref="AwayLineup"/>
/// (each a <see cref="Lineup"/>) — persistent game-scoped state that will mutate
/// via future subs, like the foul count, not fixed like team identity. Score and
/// timeouts remain placeholder fields — typed and named so the shape is defined,
/// but NOT yet read or written during possession resolution.
/// </summary>
public sealed class GameState
{
    /// <summary>The arrow's current state: Off (pre-tip), or pointing at a team.</summary>
    public ArrowState PossessionArrow { get; private set; }

    /// <param name="initialArrow">Start state. A fresh game / overtime starts
    /// <see cref="ArrowState.Off"/> so the first jump ball is a contest.</param>
    /// <param name="fouls">The half's foul tracker (owns both teams' counts and
    /// the bonus read). Required: the bonus thresholds are config-driven, so the
    /// tracker is constructed with them and handed in rather than defaulted here.</param>
    public GameState(FoulTracker fouls, ArrowState initialArrow = ArrowState.Off)
    {
        Fouls = fouls ?? throw new ArgumentNullException(nameof(fouls));
        PossessionArrow = initialArrow;
        HomeLineup  = new Lineup(TeamSide.Home);
        AwayLineup  = new Lineup(TeamSide.Away);
        HomeRoster  = new Roster(TeamSide.Home);
        AwayRoster  = new Roster(TeamSide.Away);
    }

    /// <summary>Turn the arrow ON, pointing at <paramref name="team"/>. Used by
    /// the jump-ball node after the opening tip is decided: per NCAA the arrow
    /// points at the team that LOST the tip (they are owed the next award).</summary>
    public void SetPossessionArrow(TeamSide team) =>
        PossessionArrow = team == TeamSide.Home ? ArrowState.Home : ArrowState.Away;

    /// <summary>Flip the arrow to the other team. Called when an on-arrow jump
    /// ball is consumed (the pointed-at team is awarded the ball, then the arrow
    /// flips away from them).</summary>
    public void FlipPossessionArrow() => PossessionArrow = PossessionArrow switch
    {
        ArrowState.Home => ArrowState.Away,
        ArrowState.Away => ArrowState.Home,
        _ => throw new InvalidOperationException(
            "Cannot flip the possession arrow while it is Off — it must be set by a tip first.")
    };

    /// <summary>Reset the arrow to Off. Called at the start of each overtime so
    /// the OT tip is a fresh contest (NCAA: arrow resets, OT begins with a jump
    /// ball).</summary>
    public void ResetPossessionArrow() => PossessionArrow = ArrowState.Off;

    /// <summary>The half's team-foul accumulation and bonus read — now live,
    /// incremented by Roll D and read for bonus routing. Per-half: a future
    /// half-reset replaces this tracker (or resets it) at the break.</summary>
    public FoulTracker Fouls { get; }

    /// <summary>Each team's on-court five. Persistent game-scoped state (will
    /// mutate via future subs), one per team. The attachment point the
    /// player/attribute model and the selection roll consume later; five empty
    /// numbered slots for now, reading nothing and influencing no roll.</summary>
    public Lineup HomeLineup { get; }
    public Lineup AwayLineup { get; }

    /// <summary>The lineup for a given identity — lets a slot-aware path get from
    /// a <see cref="TeamSide"/> to its five, the way <c>state.Defense</c> indexes
    /// the foul counter. This is the seam the future attribute generator walks:
    /// possession role -> LineupFor -> SlotAt -> (later) the filling player.</summary>
    public Lineup LineupFor(TeamSide side) =>
        side == TeamSide.Home ? HomeLineup : AwayLineup;

    /// <summary>Home team's slot-to-player map for this game. Populated by the
    /// harness (or future game-setup layer) before possession resolution begins;
    /// constructed empty so all 24 existing <c>new GameState(...)</c> sites in the
    /// harness compile and run byte-for-byte unchanged. The seam the attribute
    /// generator will walk: <c>game.RosterFor(side).PlayerAt(slot)</c>.</summary>
    public Roster HomeRoster { get; }

    /// <summary>Away team's slot-to-player map for this game. Same construction
    /// contract as <see cref="HomeRoster"/>.</summary>
    public Roster AwayRoster { get; }

    /// <summary>The roster for a given team identity — mirrors
    /// <see cref="LineupFor"/> so the call sites stay symmetric:
    /// <c>game.RosterFor(side).PlayerAt(slot)</c>.</summary>
    public Roster RosterFor(TeamSide side) =>
        side == TeamSide.Home ? HomeRoster : AwayRoster;

    /// <summary>The home team's coaching profile for this game. Initialized to the
    /// default <see cref="CoachProfile"/> (HeliocentricBias = 5.0) so all 59 existing
    /// <c>new GameState(...)</c> sites compile and run unchanged. Assigned via
    /// <see cref="SetCoach"/> before possession resolution begins, mirroring the
    /// <see cref="SetPossessionArrow"/> pattern.</summary>
    public CoachProfile HomeCoach { get; private set; } = new CoachProfile();

    /// <summary>The away team's coaching profile for this game. Same contract as
    /// <see cref="HomeCoach"/>.</summary>
    public CoachProfile AwayCoach { get; private set; } = new CoachProfile();

    /// <summary>Assign a coaching profile to one team. Mirrors the
    /// <see cref="SetPossessionArrow"/> pattern — a mutator that keeps the constructor
    /// signature unchanged. Harness checks call this to test non-default bias values
    /// without touching any existing construction site.</summary>
    public void SetCoach(TeamSide side, CoachProfile coach)
    {
        if (coach is null) throw new ArgumentNullException(nameof(coach));
        if (side == TeamSide.Home) HomeCoach = coach;
        else                       AwayCoach = coach;
    }

    /// <summary>The coaching profile for a given team identity — mirrors
    /// <see cref="RosterFor"/> so call sites stay symmetric:
    /// <c>game.CoachFor(state.Offense)</c>.</summary>
    public CoachProfile CoachFor(TeamSide side) =>
        side == TeamSide.Home ? HomeCoach : AwayCoach;

    // --- Placeholder fields: defined shape, not yet wired to anything. ---
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int HomeTimeouts { get; set; }
    public int AwayTimeouts { get; set; }
}
