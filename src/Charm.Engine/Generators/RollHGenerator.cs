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
/// slice — location, turnovers, rebounds, and the tip remain matchup-blind. Gravity,
/// spacing, the athletic/big split, and the carried-defender promotion are still deferred
/// and drop in here without touching Roll H's structure or the resolver.</para>
///
/// <para><b>Phase 7 — matchup-aware block door.</b> The block weight is now computed
/// via Matchup.BlockWeight rather than read flat from config. A 7'1" rim protector blocks
/// more rim attempts than a 6'2" guard (same shooter). Two contributions — skill
/// (shooter zone-skill vs defender blend, same attributes as the make door) and length
/// (Height/Wingspan/Vertical composite, block-specific because length blocks shots;
/// quickness belongs to the make door's Athleticism read) — are weighted per zone and
/// run through a tanh saturation that asymptotes toward per-zone floor/ceiling. The
/// DEC-6 empty-slot fallback keeps the configured baseline, same shape as the make door.
/// OOB rates, foul rates, and all other Roll H weights remain flat from config this
/// session.</para>
///
/// <para><b>Zone→attribute mapping.</b>
/// Three and Long read <see cref="Player.Outside"/>;
/// Mid reads <see cref="Player.Mid"/>;
/// Short reads <see cref="Player.Close"/>;
/// Rim reads <see cref="Player.Finishing"/>.
/// The zone/location distinction is intentional: ShotLocation names WHERE the
/// shot comes from; the player attribute names the SKILL needed to convert it.</para>
///
/// <para><b>Make weight substitution (carve-then-convert).</b> Block is carved off the
/// top first; the logistic result is the conversion rate GIVEN the shot is not blocked,
/// so the Made weight is <c>makePct × (1 − block)</c> — the same shape the stub uses
/// (<c>BaseMade × (1 − block)</c>). The other five outcomes keep their relative
/// proportions and fill the rest of the non-block share. The pie always sums to 1
/// and never goes negative for any (makePct, blockWeight) pair in [0, 1).
/// (The earlier form set Made = makePct and added block on top, which went negative
/// once makePct + block exceeded 1 — reachable at the rim. The fix: blockWeight is
/// carved first, then makePct is the conversion rate GIVEN not blocked.)
/// Phase 7 changes blockWeight per matchup, so the observed make rate shifts slightly
/// because the non-block headroom (1 − block) now varies per shot — the calibration
/// pass fits it as observed-FG% ≈ curve × (1 − block) regardless.</para>
///
/// <para><b>Fallback when no player is present.</b> If the roster is not populated
/// (a harness that constructs a Resolver without calling SetStarter), PlayerAt
/// returns null and the generator falls back to the flat stub behaviour — identical
/// to what RollHStubPieGenerator produces. This keeps all existing harness sites
/// that do not populate a roster passing unchanged. Separately (DEC-6), if the shooter
/// is present but the matched defending slot is empty, both the make door and the block
/// door read the raw own-rating / configured baseline with no matchup term.</para>
///
/// <para><b>Fail-loud on null slot.</b> If SelectedSlot is null when a possession
/// reaches Roll H, that is a wiring bug upstream (the selection roll must run
/// before Roll H). The generator throws rather than silently falling back.</para>
///
/// <para><b>Putback path unchanged.</b> Putback shots return the flat putback pie
/// from config (the real putback tilt by size/athleticism/rim rating is Phase 4
/// work). The generator returns early when <paramref name="putback"/> is true,
/// exactly as the stub does. BuildPutbackPie uses _cfg.BlockWeight(zone) directly —
/// putback has no carried defender slot yet.</para>
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

        // Phase 7 — matchup-aware block door. Compute the bent block weight from the
        // matchup, or fall back to the configured baseline if the defending slot is empty
        // (DEC-6, same guard as the make door above). The block weight is computed here,
        // before BuildRealPie, so the pie builder receives it as a plain double and does
        // not need to re-examine the matchup.
        var blockWeight = defender is null
            ? _cfg.BlockWeight(zone)                                      // DEC-6 fallback: no matchup term
            : Matchup.BlockWeight(zone, player, defender,
                                  _cfg.BlockWeight(zone), _matchup);      // Phase 7: bent by matchup

        return BuildRealPie(zone, makePct, blockWeight);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build the real seven-way pie. Block is carved off the top first (carve-then-convert);
    /// the logistic make probability is the conversion rate GIVEN the shot is not blocked, so
    /// Made = makePct × (1 − block) — the same shape the stub uses (BaseMade × (1 − block)).
    /// The other five outcomes keep their relative proportions and fill the rest of the
    /// non-block share. Overflow-safe: the pie always sums to 1 with no negative weight.
    ///
    /// <para>Phase 7: <paramref name="blockWeight"/> is now the matchup-aware value computed
    /// in Generate (or the configured baseline for the DEC-6 fallback). BuildRealPie does not
    /// read _cfg.BlockWeight(zone) — it receives the pre-computed value so that the carve math
    /// is unchanged and the caller owns the matchup logic.</para>
    /// </summary>
    private Pie<ShotResult> BuildRealPie(ShotLocation zone, double makePct, double blockWeight)
    {
        var block        = blockWeight;
        var nonBlock     = 1.0 - block;
        var made         = makePct * nonBlock;                 // make% = conversion given not blocked

        // The five non-Made, non-Blocked base weights sum to (1 − BaseMade); scale them
        // to fill the rest of the non-block share, preserving their relative shape.
        var nonMadeBase  = _cfg.BaseMadeAndFouled
                         + _cfg.BaseMiss
                         + _cfg.BaseMissFouled
                         + _cfg.BaseMissOutOfBoundsLost
                         + _cfg.BaseMissOutOfBoundsRetained;   // = 1 − BaseMade

        var nonMadeShare = nonBlock - made;                    // = nonBlock × (1 − makePct), ≥ 0
        var scale        = nonMadeBase > 0.0
                             ? nonMadeShare / nonMadeBase
                             : 0.0;

        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = made,
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
    /// Used when the roster is unpopulated — preserves all existing harness checks.
    /// Block weight comes directly from config (no matchup) — this path only fires
    /// when there is no shooter, so there is certainly no defender to contest with.
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
    /// Block weight stays flat from config — the putback matchup (who is contesting the
    /// put-back) is Phase 4 work; there is no defender slot to read here yet.
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
