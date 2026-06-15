namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll G generator (Phase 9). Reads the shooter's
/// authored per-zone tendencies, runs each tendency through the coaching
/// seam (identity in v1), then bends them by the defending team's per-zone
/// resistance and renormalizes.
///
/// <para><b>Phase 9 — matchup-aware shot location.</b> The first matchup-
/// aware door that reads the WHOLE defending team's defensive shape rather
/// than just the slot-matched defender. Shot location is the least one-on-
/// one decision — the offense reads where the defense is collectively
/// weakest before deciding what to attack. The per-zone resistance read is
/// a top-3 blend (<see cref="Matchup.DefensiveResistance"/>) of the five
/// defenders' CONF-1 zone reads.</para>
///
/// <para><b>The math (settled in design conversation, v2 ratio form):</b>
/// <list type="number">
///   <item>Baseline: read the shooter's five tendency attributes. Route
///         through <see cref="CoachingPull.Apply"/> (identity in v1 —
///         coaching layer not yet built).</item>
///   <item>For each zone, compute the defending team's resistance via
///         <see cref="Matchup.DefensiveResistance"/>.</item>
///   <item>For each zone, compute the offensive capability via
///         <see cref="Matchup.OffenseRating"/> (the existing Phase 6
///         zone→attribute map).</item>
///   <item>Per-zone gap = capability − resistance. Run through
///         <see cref="Matchup.GapFn"/> with the existing skill
///         steepness/exponent. NO new gap function.</item>
///   <item>Per-zone multiplier = exp(log(LocationMaxMultiplier) ×
///         tanh(shift / LocationReferenceShift)). Bounded in
///         (1/Max, Max); exactly 1 at zero gap; NEVER negative.</item>
///   <item>Multiply each baseline tendency by its multiplier; renormalize
///         the five products to sum to 1.0.</item>
/// </list></para>
///
/// <para><b>Level-mismatch behavior:</b> the math is gap-relative AND
/// only redistributes mix when the gaps are UNEQUAL across zones. Same
/// gap everywhere → same multiplier everywhere → renormalization erases
/// the shift. Mismatches that involve uneven shape (a D1 finisher vs a
/// D3 defense with weak rim protection) produce per-zone gap inequality,
/// which shifts the mix toward the most-favorable zone. Level differences
/// matter to the degree they produce uneven gaps, not directly.</para>
///
/// <para><b>Fallback paths (DEC-6):</b>
/// <list type="bullet">
///   <item>Unpopulated offense (no shooter): fall back to flat config pie
///         (byte-for-byte identical to <see cref="RollGStubPieGenerator"/>).</item>
///   <item>Shooter present, zero populated defenders: short-circuit to
///         normalized player tendencies (routed through the coaching seam)
///         — NO matchup multiplier applied, but player identity preserved.</item>
///   <item>Shooter present, 1–2 populated defenders: the resistance read
///         renormalizes the top-3 weights to the available defenders
///         inside <see cref="Matchup.DefensiveResistance"/>.</item>
/// </list></para>
///
/// <para><b>Roll G itself unchanged.</b> <see cref="RollG.Execute"/> still
/// takes <c>(state, pie, rng)</c>; only the GENERATOR reads
/// <see cref="GameState"/>.</para>
///
/// Implements <see cref="IRollGPieGenerator"/>.
/// </summary>
public sealed class RollGGenerator : IRollGPieGenerator
{
    private readonly RollGConfig  _cfg;
    private readonly MatchupConfig _matchup;
    private readonly GameState    _game;

    public RollGGenerator(RollGConfig cfg, MatchupConfig matchup, GameState game)
    {
        _cfg     = cfg     ?? throw new ArgumentNullException(nameof(cfg));
        _matchup = matchup ?? throw new ArgumentNullException(nameof(matchup));
        _game    = game    ?? throw new ArgumentNullException(nameof(game));
    }

    public Pie<ShotLocation> Generate(PossessionState state)
    {
        var slot = state.SelectedSlot
            ?? throw new InvalidOperationException(
                "RollGGenerator requires a stamped SelectedSlot — Roll E must run before Roll G.");

        var shooter = _game.RosterFor(state.Offense).PlayerAt(slot);
        if (shooter is null)
            return BuildStubPie();

        // Read all five defending slots; some may be null.
        var defendingRoster = _game.RosterFor(state.Defense);
        var defendingLineup = _game.LineupFor(state.Defense);
        var defenders = new Player?[]
        {
            defendingRoster.PlayerAt(defendingLineup.SlotAt(1)),
            defendingRoster.PlayerAt(defendingLineup.SlotAt(2)),
            defendingRoster.PlayerAt(defendingLineup.SlotAt(3)),
            defendingRoster.PlayerAt(defendingLineup.SlotAt(4)),
            defendingRoster.PlayerAt(defendingLineup.SlotAt(5)),
        };

        var populated = 0;
        foreach (var d in defenders)
            if (d is not null) populated++;

        // Baseline tendencies through the coaching seam (identity in v1).
        var (tRim, tShort, tMid, tLong, tThree) =
            CoachingPull.Apply(shooter, coach: null, malleability: null);

        // Zero defenders populated: short-circuit. Player tendencies normalized,
        // no matchup multiplier applied. Player identity preserved.
        if (populated == 0)
            return BuildPureTendencyPie(tRim, tShort, tMid, tLong, tThree);

        // 1–5 defenders populated: bend tendencies by per-zone multipliers.
        // v3: calls public Matchup.LocationMultiplier — same pattern as
        // RollHGenerator calling Matchup.BlockWeight and Matchup.FoulRate.
        var rimMult   = Matchup.LocationMultiplier(ShotLocation.Rim,   shooter, defenders, _matchup);
        var shortMult = Matchup.LocationMultiplier(ShotLocation.Short, shooter, defenders, _matchup);
        var midMult   = Matchup.LocationMultiplier(ShotLocation.Mid,   shooter, defenders, _matchup);
        var longMult  = Matchup.LocationMultiplier(ShotLocation.Long,  shooter, defenders, _matchup);
        var threeMult = Matchup.LocationMultiplier(ShotLocation.Three, shooter, defenders, _matchup);

        var bentRim   = tRim   * rimMult;
        var bentShort = tShort * shortMult;
        var bentMid   = tMid   * midMult;
        var bentLong  = tLong  * longMult;
        var bentThree = tThree * threeMult;

        var total = bentRim + bentShort + bentMid + bentLong + bentThree;
        if (total <= 0.0)
            throw new InvalidOperationException(
                $"RollGGenerator: bent tendency total <= 0 ({total}). " +
                "Should be unreachable — multipliers are bounded strictly positive by the ratio form.");

        var weights = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Rim]   = bentRim   / total,
            [ShotLocation.Short] = bentShort / total,
            [ShotLocation.Mid]   = bentMid   / total,
            [ShotLocation.Long]  = bentLong  / total,
            [ShotLocation.Three] = bentThree / total,
        };

        return new Pie<ShotLocation>(weights, _cfg.Epsilon);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // v3 fix: NO private Multiplier helper here. The multiplier math lives on
    // Matchup as a public static (LocationMultiplier) — same pattern as
    // BlockWeight and FoulRate. The generator's job is to call it per zone.

    private Pie<ShotLocation> BuildStubPie()
    {
        var weights = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Three] = _cfg.BaseThree,
            [ShotLocation.Long]  = _cfg.BaseLong,
            [ShotLocation.Mid]   = _cfg.BaseMid,
            [ShotLocation.Short] = _cfg.BaseShort,
            [ShotLocation.Rim]   = _cfg.BaseRim,
        };
        return new Pie<ShotLocation>(weights, _cfg.Epsilon);
    }

    private Pie<ShotLocation> BuildPureTendencyPie(
        double tRim, double tShort, double tMid, double tLong, double tThree)
    {
        var total = tRim + tShort + tMid + tLong + tThree;
        // tendencySum > 0 is guaranteed by Player.Validate(); this is defense in depth.
        if (total <= 0.0)
            throw new InvalidOperationException(
                $"RollGGenerator: player tendency total <= 0 ({total}). Player.Validate() should have caught this.");

        var weights = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Rim]   = tRim   / total,
            [ShotLocation.Short] = tShort / total,
            [ShotLocation.Mid]   = tMid   / total,
            [ShotLocation.Long]  = tLong  / total,
            [ShotLocation.Three] = tThree / total,
        };
        return new Pie<ShotLocation>(weights, _cfg.Epsilon);
    }
}
