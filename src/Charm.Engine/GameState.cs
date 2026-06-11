namespace Charm.Engine;

/// <summary>Which side a team is on. Used by the possession arrow and, later,
/// by score/foul/timeout tracking.</summary>
public enum TeamSide
{
    Home,
    Away
}

/// <summary>
/// Persistent game-wide state that survives ACROSS possessions — unlike
/// <see cref="PossessionState"/>, which is per-possession. This is where the
/// possession arrow has to live, because a jump ball in one possession sets who
/// gets the ball in a later one.
///
/// SKELETON ONLY this session. The possession arrow has real behavior (it
/// flips). Score, fouls, and timeouts are placeholder fields — typed and named
/// so the shape is defined, but NOT yet read or written during possession
/// resolution. They are future infrastructure, deliberately inert for now.
/// </summary>
public sealed class GameState
{
    /// <summary>Who is awarded the ball on the next arrow-decided jump ball.</summary>
    public TeamSide PossessionArrow { get; private set; }

    public GameState(TeamSide initialArrow) => PossessionArrow = initialArrow;

    /// <summary>Flip the arrow to the other team. A future jump-ball resolver
    /// calls this when the arrow is consumed; nothing in Roll A touches it.</summary>
    public void FlipPossessionArrow() =>
        PossessionArrow = PossessionArrow == TeamSide.Home ? TeamSide.Away : TeamSide.Home;

    // --- Placeholder fields: defined shape, not yet wired to anything. ---
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int HomeTeamFouls { get; set; }
    public int AwayTeamFouls { get; set; }
    public int HomeTimeouts { get; set; }
    public int AwayTimeouts { get; set; }
}
