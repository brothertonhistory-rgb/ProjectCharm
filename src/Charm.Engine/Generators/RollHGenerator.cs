namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll H generator (Phase 2). Reads the shooter's own
/// rating and produces a make probability via a per-zone bounded logistic.
///
/// <para><b>Phase 6 — matchup-aware make door.</b> The make% is now read off a
/// matchup-adjusted effective rating: the shooter's baseline slid by the skill gap
/// (his zone rating vs the matched defender's blended per-zone defensive read) and the
/// physical (athletic) gap, composed additively (Matchup.EffectiveRating). The defender
/// is resolved by DefenderPicker (v1 slot-guards-slot). Only the make door is wired this
/// slice — location, turnovers, rebounds, blocks, and the tip remain matchup-blind.
/// Gravity, spacing, the athletic/big split, and the carried-defender promotion are
/// still deferred and drop in here without touching Roll H's structure or the resolver.</para>
///
/// <para><b>Zone→attribute mapping.</b>
/// Three and Long read <see cref="Player.Outside"/>;
/// Mid reads <see cref="Player.Mid"/>;
/// Short reads <see cref="Player.Close"/>;
/// Rim reads <see cref="Player.Finishing"/>.
/// The zone/location distinction is intentional: ShotLocation names WHERE the
/// shot comes from; the player attribute names the SKILL needed to convert it.</para>
///
/// <para><b>Make weight substitution.</b> The logistic result replaces only the
/// <c>BaseMade</c> weight. All other pie structure — block carve, foul slices,
/// OOB pair, putback path — is preserved unchanged from the stub. The make weight
/// is renormalised: the six non-block outcomes sum to 1, so replacing BaseMade
/// while keeping the other five proportional (scaling by the remaining share)
/// produces a valid pie. The block carve is applied on top, exactly as the stub
/// does it.</para>
///
/// <para><b>Fallback when no player is present.</b> If the roster is not populated
/// (a harness that constructs a Resolver without calling SetStarter), PlayerAt
/// returns null and the generator falls back to the flat stub behaviour — identical
/// to what RollHStubPieGenerator produces. This keeps all 14 existing harness sites
/// that do not populate a roster passing unchanged. Separately (DEC-6), if the shooter
/// is present but the matched defending slot is empty, the make door reads the raw
/// own-rating with no matchup term — the pre-Phase-6 behaviour.</para>
///
/// <para><b>Fail-loud on null slot.</b> If SelectedSlot is null when a possession
/// reaches Roll H, that is a wiring bug upstream (the selection roll must run
/// before Roll H). The generator throws rather than silently falling back.</para>
///
/// <para><b>Putback path unchanged.</b> Putback shots return the flat putback pie
/// from config (the real putback tilt by size/athleticism/rim rating is Phase 4
/// work). The generator returns early when <paramref name="putback"/> is true,
/// exactly as the stub does.</para>
///
/// Implements <see cref="IRollHPieGenerator"/> — the resolver holds the interface,
/// so swapping this for a richer Phase 3/4/5 generator only changes the
/// construction site.
/// </summary>
public sealed class RollHGenerator : IRollHPieGenerator
{
    private readonly RollHConfig _cfg;
    private readonly MatchupConfig _matchup;
    private readonly GameState _game;

    public RollHGenerator(RollHConfig cfg, MatchupConfig matchup, GameState game)
    {
        _cfg     = cfg     ?? throw new ArgumentNullException(nameof(cfg));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));
    }

    /// <inheritdoc cref="IRollHPieGenerator.Generate"/>
    public Pie<ShotResult> Generate(PossessionState state, bool putback = false)
    {
        // Putback path — return the flat putback pie unchanged (Phase 4 work).
        if (putback)
            return BuildPutbackPie();

        // Zone is required — Roll G must have run before Roll H.
        var zone = state.ShotType
            ?? throw new InvalidOperationException(
                "RollHGenerator requires a stamped ShotType — Roll G must run before Roll H.");

        // Slot is required — the selection roll must have run before Roll H.
        var slot = state.SelectedSlot
            ?? throw new InvalidOperationException(
                "RollHGenerator requires a stamped SelectedSlot — the selection roll must run before Roll H.");

        // Look up the shooter. Null means the roster is not populated (harness
        // without SetStarter) — fall back to stub behaviour so existing checks pass.
        var player = _game.RosterFor(state.Offense).PlayerAt(slot);
        if (player is null)
            return BuildStubPie(zone);

        // Phase 6 — matchup-aware make door. Resolve the contesting defender via the
        // (swappable) picker, then read make% off a matchup-adjusted effective rating.
        // DEC-6 fallback: if no defender is present (an empty defending slot), use the
        // raw own-rating read — no matchup term — exactly as pre-Phase-6. (The
        // unpopulated-roster case is already handled above by the shooter-null return.)
        var defenderSlot = DefenderPicker.Pick(state);
        var defender     = _game.RosterFor(state.Defense).PlayerAt(defenderSlot);

        var effectiveRating = defender is null
            ? Matchup.OffenseRating(zone, player)
            : Matchup.EffectiveRating(zone, player, defender, _matchup);

        var makePct = _cfg.MakeProbability(zone, effectiveRating);

        return BuildRealPie(zone, makePct);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build the real seven-way pie using the logistic make probability.
    /// The make weight replaces BaseMade; the other five make/miss outcomes keep
    /// their relative proportions scaled to fill the non-make, non-block share.
    /// The block carve is applied identically to the stub.
    /// </summary>
    private Pie<ShotResult> BuildRealPie(ShotLocation zone, double makePct)
    {
        var block = _cfg.BlockWeight(zone);

        // The remaining share after carving block and make.
        // The five non-Made, non-Blocked base weights sum to (1 − BaseMade).
        // We scale them so they fill (1 − block − makePct), preserving shape.
        var nonMadeBase  = _cfg.BaseMadeAndFouled
                         + _cfg.BaseMiss
                         + _cfg.BaseMissFouled
                         + _cfg.BaseMissOutOfBoundsLost
                         + _cfg.BaseMissOutOfBoundsRetained;   // = 1 − BaseMade

        var nonMadeShare = 1.0 - block - makePct;              // share left for the five
        var scale        = nonMadeBase > 0.0
                             ? nonMadeShare / nonMadeBase
                             : 0.0;

        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = makePct,
            [ShotResult.MadeAndFouled]           = _cfg.BaseMadeAndFouled           * scale,
            [ShotResult.Miss]                    = _cfg.BaseMiss                    * scale,
            [ShotResult.MissFouled]              = _cfg.BaseMissFouled              * scale,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.BaseMissOutOfBoundsLost     * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.BaseMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked]                 = block,
        };

        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }

    /// <summary>
    /// Stub fallback: flat pie identical to what RollHStubPieGenerator produces.
    /// Used when the roster is unpopulated — preserves all 14 existing harness checks.
    /// </summary>
    private Pie<ShotResult> BuildStubPie(ShotLocation zone)
    {
        var block = _cfg.BlockWeight(zone);
        var scale = 1.0 - block;
        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = _cfg.BaseMade                    * scale,
            [ShotResult.MadeAndFouled]           = _cfg.BaseMadeAndFouled           * scale,
            [ShotResult.Miss]                    = _cfg.BaseMiss                    * scale,
            [ShotResult.MissFouled]              = _cfg.BaseMissFouled              * scale,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.BaseMissOutOfBoundsLost     * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.BaseMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked]                 = block,
        };
        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }

    /// <summary>
    /// Putback pie — flat, from config, no per-zone block carve.
    /// Identical to RollHStubPieGenerator.BuildPutbackPie().
    /// </summary>
    private Pie<ShotResult> BuildPutbackPie()
    {
        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = _cfg.PutbackMade,
            [ShotResult.MadeAndFouled]           = _cfg.PutbackMadeAndFouled,
            [ShotResult.Miss]                    = _cfg.PutbackMiss,
            [ShotResult.MissFouled]              = _cfg.PutbackMissFouled,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.PutbackMissOutOfBoundsLost,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.PutbackMissOutOfBoundsRetained,
            [ShotResult.Blocked]                 = _cfg.PutbackBlocked,
        };
        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }
}
