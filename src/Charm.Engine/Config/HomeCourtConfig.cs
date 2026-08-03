using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// The one home-court dial (S95). Loaded from the "HomeCourt" section of config.json.
///
/// <para><b>The ruling this exists to serve.</b> Home court is a ROAD PENALTY, not a
/// home boost — a floor with no host tilts nobody, so a neutral game is played by two
/// untouched teams. It is FLAT: every real home floor is worth exactly the same, and
/// prestige, travel distance and semi-home arrangements are deferred to the tournament
/// layer that will own the schedule's site fact.</para>
///
/// <para><b>Why one integer and not thirteen.</b> The ruling is "all of it, a little —
/// every contested pie the road team touches leans a hair against them, and no single
/// door carries it." This engine has no layer between a man's ratings and the pie: every
/// contested door is built by reading the two men against each other on the spot. So a
/// single subtraction from the road side's SKILL ratings makes all thirteen doors lean
/// at once, each by exactly as much as that door actually reads the ratings involved —
/// weighted by the engine's own sensitivities instead of by thirteen hand-picked
/// constants. The applicator and the exempt list live harness-side in
/// <c>Program.Season.HomeCourt.cs</c>; this class is only the dial's home.</para>
///
/// <para><b>Skills, not bodies (Emmett, 2026-08-03).</b> The road costs a man his touch,
/// his hands, his reads and his restraint. It never costs him his body or his effort: the
/// six physicals, the three body facts and Hustle are all exempt, which is why
/// <see cref="Player.Athleticism"/> — the mean of the six physicals — does not move an
/// inch on the road. A man is exactly as big, as fast and as bouncy in a hostile gym as
/// he is at home, and he competes exactly as hard.</para>
///
/// <para><b>Bounds.</b> Zero is VALID and operationally inert — at zero the applicator
/// hands back the original players untouched and the engine is bit-for-bit what it was
/// before this session, which is what Phase 86 B1 proves against a golden captured from
/// the pre-S95 tree. Negatives are refused: a negative shave is a road BOOST, which is
/// not a value of this dial but a different design. There is deliberately NO upper
/// bound — every shaved rating floors at 0, so a large shave degrades smoothly into
/// "the road team is replacement level" rather than into invalid ratings.</para>
/// </summary>
public sealed class HomeCourtConfig
{
    /// <summary>Points subtracted from each of the road side's shaved ratings, floored
    /// at 0.
    ///
    /// <para><b>Calibrated and ruled at 3</b> (Emmett, 2026-08-03): three full stock-world
    /// seasons, 8,454 games, 58.74% home wins at a mean margin of +3.66. The control at
    /// zero measures 50.08% and a margin of −0.05 — no penalty, no home court — which is
    /// what makes the number above a measurement rather than an assertion.</para>
    ///
    /// <para><b>Why not 2.</b> Two was the ratified value when the shave reached all
    /// thirty performance ratings including the body. Under the skills-not-bodies ruling
    /// it reaches twenty-three and no longer touches the athleticism channel that feeds
    /// every matchup gap, fatigue and displacement — and it measures 56.78%, short of the
    /// 59–61% evidence band. Three restores what was signed off (58.74% against 59.0%,
    /// +3.66 against +3.8). Four overshoots at 61.86% with every season above 61.3.</para>
    ///
    /// <para>THE DIAL IS RE-MEASURED WHENEVER THE SHAVED SET CHANGES. The set decides how
    /// much of the engine one subtraction reaches, so a number calibrated against one
    /// classification is not evidence about another.</para></summary>
    public int RoadShave { get; set; } = DefaultRoadShave;

    /// <summary>The compiled default, named once so the loader's missing-section and
    /// missing-key paths cannot drift apart from the property initializer.</summary>
    public const int DefaultRoadShave = 3;

    public static HomeCourtConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        // A missing section or a missing key is QUIET AT RUNTIME and LOUD AT TEST TIME —
        // the Phase 71 ruling. The compiled default applies and the game boots; the
        // absence is what Phase 71's key-name parity arm reports.
        if (!doc.RootElement.TryGetProperty("HomeCourt", out var section))
            return new HomeCourtConfig();

        var cfg = JsonSerializer.Deserialize<HomeCourtConfig>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (cfg is null)
            throw new InvalidOperationException($"Could not parse HomeCourt config at {path}.");

        if (cfg.RoadShave < 0)
            throw new InvalidOperationException(
                $"HomeCourt RoadShave must be >= 0 (got {cfg.RoadShave}). A negative shave " +
                "is a road boost, which is a different design, not a value of this dial.");

        return cfg;
    }
}
