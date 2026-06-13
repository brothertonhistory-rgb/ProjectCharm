using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// Every tunable number for Roll M (free-throw rebound resolution) lives here —
/// nothing is hardcoded in logic. Loaded from the "RollM" section of config.json.
/// Mirrors <see cref="RollIConfig"/>: flat PLACEHOLDER weights, no live-wire scalar
/// (the only things that will tilt this pie are the deferred attribute model — board
/// tilt by size / box-out / positioning along the lane — which replaces the flatness
/// later WITHOUT touching Roll M or the resolver).
///
/// <para>The seven weights sum to 1. They are seeded CONSERVATIVE and are Emmett's to
/// tune against the harness's rebound-rate and possession-count readouts. The split is
/// deliberately MORE DEFENSIVE than Roll I's field-goal board: off a free throw
/// everyone is lined calmly along the lane with the defense in the better box-out
/// spots and no offensive shooter crashing in, so the offensive-board share is lower
/// here than off a live miss. The added out-of-bounds PAIR (off-offense / off-defense)
/// has no analog in Roll I — a free-throw scramble kicks the ball out of bounds more
/// often than a normal rebound battle.</para>
///
/// <para>The offensive-rebound rate here is also a possession-count calibration knob
/// (an FT offensive board extends the possession via Roll K, exactly as a field-goal
/// offensive board does), so it is Emmett's to tune alongside Roll I's.</para>
/// </summary>
public sealed class RollMConfig
{
    // --- Stub pie base weights (placeholders; the real attribute-driven generator
    //     will replace these). The seven sum to 1. One flips the ball on a LIVE board
    //     (DefensiveRebound -> transition terminal); two flip it on a DEAD ball
    //     (LooseBallFoulOnOffense, OutOfBoundsOffOffense -> terminals); the rest keep
    //     the offense's ball (OffensiveRebound -> Roll K; LooseBallFoulOnDefense ->
    //     the bonus fork; OutOfBoundsOffDefense -> sideline inbound; JumpBall -> the
    //     shared arrow node). ---
    public double DefensiveRebound { get; set; } = 0.735;
    public double OffensiveRebound { get; set; } = 0.18;
    public double LooseBallFoulOnDefense { get; set; } = 0.02;
    public double LooseBallFoulOnOffense { get; set; } = 0.01;
    public double OutOfBoundsOffOffense { get; set; } = 0.02;
    public double OutOfBoundsOffDefense { get; set; } = 0.03;
    public double JumpBall { get; set; } = 0.005;

    /// <summary>Tolerance for the pie sum-to-one validation.</summary>
    public double Epsilon { get; set; } = 1e-9;

    public static RollMConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("RollM");
        var cfg = JsonSerializer.Deserialize<RollMConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return cfg ?? throw new InvalidOperationException($"Could not parse RollM config at {path}.");
    }
}
