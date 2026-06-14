using System.Text.Json;
using System.Text.Json.Serialization;

namespace Charm.Engine;

/// <summary>
/// JSON-deserialisable shape for one player's authored attributes. Lives inside
/// <see cref="RosterConfig"/> and maps 1:1 to the authored fields on
/// <see cref="Player"/>. Property names match the JSON keys in config.json's
/// <c>"Rosters"</c> section (case-insensitive on deserialise).
///
/// <para>This is a data-transfer object only: it carries raw integers from the
/// config file and is converted to a <see cref="Player"/> via
/// <see cref="ToPlayer"/>. The separation keeps JSON concerns out of the domain
/// object.</para>
/// </summary>
public sealed class PlayerConfig
{
    // Identity
    public string Name { get; set; } = "Unknown";

    // Offense
    public int Close               { get; set; }
    public int Mid                 { get; set; }
    public int Outside             { get; set; }
    public int Finishing           { get; set; }
    public int FreeThrow           { get; set; }
    public int BallHandling        { get; set; }
    public int Passing             { get; set; }
    public int Playmaking          { get; set; }
    public int SelfCreation        { get; set; }
    public int PostMoves           { get; set; }
    public int OffBallMovement     { get; set; }
    public int Screening           { get; set; }
    public int OffensiveRebounding { get; set; }

    // Defense
    public int PerimeterDefense    { get; set; }
    public int PostDefense         { get; set; }
    public int RimProtection       { get; set; }
    public int DefensiveRebounding { get; set; }
    public int Steals              { get; set; }

    // Physical
    public int Height              { get; set; }
    public int Wingspan            { get; set; }
    public int Weight              { get; set; }
    public int Strength            { get; set; }
    public int Speed               { get; set; }
    public int Quickness           { get; set; }
    public int FirstStep           { get; set; }
    public int Vertical            { get; set; }
    public int Endurance           { get; set; }

    // Intangible
    public int Hustle              { get; set; }
    public int BasketballIQ        { get; set; }
    public int Discipline          { get; set; }

    /// <summary>
    /// Convert this config DTO into the engine's domain <see cref="Player"/>
    /// object. All authored fields are transferred via init-setters; derived
    /// attributes are computed on read by <see cref="Player"/>.
    /// </summary>
    public Player ToPlayer() => new Player(Name)
    {
        Close               = Close,
        Mid                 = Mid,
        Outside             = Outside,
        Finishing           = Finishing,
        FreeThrow           = FreeThrow,
        BallHandling        = BallHandling,
        Passing             = Passing,
        Playmaking          = Playmaking,
        SelfCreation        = SelfCreation,
        PostMoves           = PostMoves,
        OffBallMovement     = OffBallMovement,
        Screening           = Screening,
        OffensiveRebounding = OffensiveRebounding,
        PerimeterDefense    = PerimeterDefense,
        PostDefense         = PostDefense,
        RimProtection       = RimProtection,
        DefensiveRebounding = DefensiveRebounding,
        Steals              = Steals,
        Height              = Height,
        Wingspan            = Wingspan,
        Weight              = Weight,
        Strength            = Strength,
        Speed               = Speed,
        Quickness           = Quickness,
        FirstStep           = FirstStep,
        Vertical            = Vertical,
        Endurance           = Endurance,
        Hustle              = Hustle,
        BasketballIQ        = BasketballIQ,
        Discipline          = Discipline,
    };
}

/// <summary>
/// The <c>"Rosters"</c> section of config.json: two arrays of five
/// <see cref="PlayerConfig"/> objects (home and away). Loaded once at harness
/// startup; each player is converted to a <see cref="Player"/> via
/// <see cref="PlayerConfig.ToPlayer"/> and seated in the matching
/// <see cref="Roster"/> on <see cref="GameState"/>.
///
/// <para>This is the embryo of the dynasty save format. Every future layer that
/// writes or reads a starting lineup (the coach screen, the save file, the
/// almanac) points at the same JSON contract — the property names here ARE the
/// schema.</para>
/// </summary>
public sealed class RosterConfig
{
    /// <summary>Home team's starting five, in slot order (index 0 = slot 1).
    /// Must contain exactly 5 entries.</summary>
    public List<PlayerConfig> Home { get; set; } = new();

    /// <summary>Away team's starting five, in slot order (index 0 = slot 1).
    /// Must contain exactly 5 entries.</summary>
    public List<PlayerConfig> Away { get; set; } = new();

    /// <summary>
    /// Load the <c>"Rosters"</c> section from <paramref name="path"/>.
    /// Validates that both arrays carry exactly 5 players and that every player
    /// passes the 0–99 range check before returning.
    /// </summary>
    public static RosterConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("Rosters");
        var cfg = JsonSerializer.Deserialize<RosterConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        cfg = cfg ?? throw new InvalidOperationException(
            $"Could not parse Rosters config at {path}.");

        // Validate lineup sizes.
        if (cfg.Home.Count != Lineup.Size)
            throw new InvalidOperationException(
                $"Rosters.Home must contain exactly {Lineup.Size} players " +
                $"(found {cfg.Home.Count}).");
        if (cfg.Away.Count != Lineup.Size)
            throw new InvalidOperationException(
                $"Rosters.Away must contain exactly {Lineup.Size} players " +
                $"(found {cfg.Away.Count}).");

        // Validate every player's attribute ranges.
        var allErrors = new List<string>();
        foreach (var pc in cfg.Home.Concat(cfg.Away))
        {
            var errors = pc.ToPlayer().Validate();
            allErrors.AddRange(errors);
        }
        if (allErrors.Count > 0)
            throw new InvalidOperationException(
                "Roster config contains out-of-range attributes:\n" +
                string.Join("\n", allErrors));

        return cfg;
    }
}
