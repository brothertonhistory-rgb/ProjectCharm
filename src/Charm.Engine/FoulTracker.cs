namespace Charm.Engine;

/// <summary>
/// Owns team-foul accumulation for a single half and answers the only question
/// the rest of the engine asks of it: "what bonus state is this team's opponent
/// in?" Bundled as one object (rather than loose ints on <see cref="GameState"/>)
/// for the same reason the possession arrow's behavior lives on GameState — the
/// half-scoped concern is a unit: both counts, the thresholds that read them,
/// and (future) the per-half reset that clears them all at once.
///
/// Deliberately ignorant of free throws. It reports <see cref="BonusType"/> and
/// stops; whether a 1-and-1 front end is reboundable, how many shots a double
/// bonus is, etc. are the free-throw node's rules, derived from the bonus type.
/// Letting any of that leak in here would couple this layer to a downstream node
/// it must not know about.
///
/// Per-half: a real game resets these counts at the half (and the bonus resets
/// with them). That reset is future infrastructure — noted, not built. When it
/// lands it clears this one object; nothing else need change.
/// </summary>
public sealed class FoulTracker
{
    private readonly int _bonusThreshold;
    private readonly int _doubleBonusThreshold;

    private int _homeFouls;
    private int _awayFouls;

    /// <param name="bonusThreshold">The team-foul count at which the opponent
    /// enters the (1-and-1) bonus. NCAA classic: the 7th team foul of the half.</param>
    /// <param name="doubleBonusThreshold">The team-foul count at which the
    /// opponent enters the double bonus (two guaranteed FTs). NCAA classic: the
    /// 10th team foul of the half.</param>
    public FoulTracker(int bonusThreshold, int doubleBonusThreshold)
    {
        if (bonusThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(bonusThreshold), bonusThreshold,
                "Bonus threshold must be positive.");
        if (doubleBonusThreshold <= bonusThreshold)
            throw new ArgumentOutOfRangeException(nameof(doubleBonusThreshold), doubleBonusThreshold,
                $"Double-bonus threshold ({doubleBonusThreshold}) must exceed the bonus threshold ({bonusThreshold}).");

        _bonusThreshold = bonusThreshold;
        _doubleBonusThreshold = doubleBonusThreshold;
    }

    /// <summary>Current accumulated team fouls for <paramref name="team"/> this half.</summary>
    public int FoulsFor(TeamSide team) =>
        team == TeamSide.Home ? _homeFouls : _awayFouls;

    /// <summary>Charge one team foul to <paramref name="team"/> (the fouling
    /// team — i.e. the defense on the possession the foul occurred in).</summary>
    public void Increment(TeamSide team)
    {
        if (team == TeamSide.Home) _homeFouls++;
        else _awayFouls++;
    }

    /// <summary>
    /// The bonus the FOULING team's accumulated fouls put their opponent into.
    /// Pass the fouling (defensive) team; the count read is that team's, because
    /// it is their fouls that send the offense to the line.
    ///
    /// Banded on the POST-increment count: &lt; bonus = None; [bonus,
    /// doubleBonus) = OneAndOne; &gt;= doubleBonus = Double. So with the classic
    /// 7/10 thresholds, the fouling team's 7th–9th foul yields OneAndOne and the
    /// 10th onward yields Double.
    /// </summary>
    public BonusType BonusFor(TeamSide foulingTeam)
    {
        var fouls = FoulsFor(foulingTeam);
        if (fouls >= _doubleBonusThreshold) return BonusType.Double;
        if (fouls >= _bonusThreshold) return BonusType.OneAndOne;
        return BonusType.None;
    }
}
