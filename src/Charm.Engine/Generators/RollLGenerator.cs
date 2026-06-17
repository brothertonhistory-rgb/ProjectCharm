namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll L generator (Phase 18). Reads the shooter's
/// <see cref="Player.FreeThrow"/> rating from the carried possession state and
/// converts it directly to the make probability (<c>FreeThrow / 100.0</c>).
/// Miss is the complement; the two slices always sum to 1.
///
/// <para><b>FreeThrow is absolute, not relative.</b> Unlike every other attribute
/// (50 = average on a relative scale), FreeThrow is literal: a 72-rated shooter
/// makes exactly 72% per attempt. There is no logistic, no matchup, no context
/// modifier — the authored rating IS the make%. This is the simplest real generator
/// in the engine.</para>
///
/// <para><b>Shooter resolution.</b> The generator reads
/// <see cref="PossessionState.SelectedSlot"/> and walks
/// <c>game.RosterFor(state.Offense).PlayerAt(slot)</c>. Two fallback paths use
/// <see cref="RollLConfig.MakeProbability"/> (the flat 72% D1 average):
/// <list type="bullet">
///   <item>Null slot — a bonus foul arrived before Roll E ran; the shooter is
///         unknown.</item>
///   <item>Unpopulated slot — an isolation-test game with an empty roster.</item>
/// </list>
/// These are named loose ends (Phase 18 build §7), not bugs. The reported FT% in
/// game runs is therefore a blend of player-attributed ratings (shooting-foul trips
/// and post-Roll-E bonus trips) and the 72% flat fallback (pre-Roll-E bonus trips).
/// </para>
///
/// <para><b>RoadMakePenalty is dormant.</b> The <see cref="RollLConfig.RoadMakePenalty"/>
/// field is a documented seam (currently 0.0) and is NOT read or applied here. Home/road
/// FT effects are outside Phase 18.</para>
///
/// <para><b>Clamp.</b> <see cref="Math.Clamp"/> is applied to the make probability as a
/// safety net against misconfigured authored ratings. <see cref="Player.Validate"/> is
/// the upstream guard for invalid values; the clamp just ensures the pie constructor
/// never receives an out-of-range weight.</para>
///
/// Implements <see cref="IRollLPieGenerator"/>.
/// </summary>
public sealed class RollLGenerator : IRollLPieGenerator
{
    private readonly RollLConfig _config;
    private readonly GameState   _game;

    public RollLGenerator(RollLConfig config, GameState game)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _game   = game   ?? throw new ArgumentNullException(nameof(game));
    }

    public Pie<FreeThrowOutcome> Generate(PossessionState state)
    {
        double makeProbability;

        if (state.SelectedSlot is null)
        {
            // Null slot: bonus foul before Roll E ran; shooter unknown.
            // Fall back to the flat config make%.
            makeProbability = _config.MakeProbability;
        }
        else
        {
            var player = _game.RosterFor(state.Offense).PlayerAt(state.SelectedSlot.Value);
            if (player is null)
            {
                // Unpopulated slot: isolation-test game with an empty roster.
                // Fall back to the flat config make%.
                makeProbability = _config.MakeProbability;
            }
            else
            {
                // Direct 1:1 — FreeThrow rating IS the make percentage × 100.
                // RoadMakePenalty: dormant, not read. Do not apply even conditionally.
                makeProbability = player.FreeThrow / 100.0;
            }
        }

        // Safety-net clamp. Player.Validate() is the upstream guard for invalid authored
        // ratings; this clamp is the last-resort defense before the Pie constructor.
        makeProbability = Math.Clamp(makeProbability, 0.0, 1.0);

        return new Pie<FreeThrowOutcome>(
            new Dictionary<FreeThrowOutcome, double>
            {
                [FreeThrowOutcome.Make] = makeProbability,
                [FreeThrowOutcome.Miss] = 1.0 - makeProbability,
            },
            _config.Epsilon);
    }
}
