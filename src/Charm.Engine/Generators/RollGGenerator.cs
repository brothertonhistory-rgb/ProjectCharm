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
public sealed class RollGGenerator : IRollGGenerationProvider
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

    /// <summary>
    /// Generate the shot-location pie (matchup-bent, then usage-shifted) AND the
    /// residual pressure in one pass. The residual is the volume load that could not
    /// be absorbed into a wider shot diet this possession. Delegates the base
    /// interface's <see cref="Generate"/> here.
    /// </summary>
    public RollGGeneration GenerateWithResidual(PossessionState state)
    {
        var slot = state.SelectedSlot
            ?? throw new InvalidOperationException(
                "RollGGenerator requires a stamped SelectedSlot — Roll E must run before Roll G.");

        // Phase 16: fast-break shot location — flat rim-heavy pie, no diet shift,
        // residual 0.0 (no volume load on a transition possession).
        if (state.FastBreak)
            return new RollGGeneration(BuildFastBreakPie(), 0.0);

        var shooter = _game.RosterFor(state.Offense).PlayerAt(slot);
        if (shooter is null)
            return new RollGGeneration(BuildStubPie(), 0.0);

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
        foreach (var d in defenders) if (d is not null) populated++;

        // Baseline tendencies through the coaching seam (identity in v1).
        var (tRim, tShort, tMid, tLong, tThree) =
            CoachingPull.Apply(shooter, coach: null, malleability: null);

        // Zero defenders populated: short-circuit to pure-tendency pie.
        // Implementer's call: the shooter IS real and IS under load, so the
        // volume-driven diet shift still applies (the load is real even when
        // defensive data is incomplete). Flag: this is the zero-defender fallback.
        if (populated == 0)
        {
            var purePie = BuildPureTendencyPie(tRim, tShort, tMid, tLong, tThree);
            var pureBent = new double[]
            {
                purePie.Slices.First(s => s.Outcome == ShotLocation.Rim).Weight,
                purePie.Slices.First(s => s.Outcome == ShotLocation.Short).Weight,
                purePie.Slices.First(s => s.Outcome == ShotLocation.Mid).Weight,
                purePie.Slices.First(s => s.Outcome == ShotLocation.Long).Weight,
                purePie.Slices.First(s => s.Outcome == ShotLocation.Three).Weight,
            };
            var (shiftedPurePie, pureResidual) = ApplyDietShift(state, shooter, pureBent);
            return new RollGGeneration(shiftedPurePie, pureResidual);
        }

        // 1–5 defenders: bend tendencies by per-zone multipliers (existing Phase 9 math).
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

        var bentNorm = new double[]
        {
            bentRim   / total,
            bentShort / total,
            bentMid   / total,
            bentLong  / total,
            bentThree / total,
        };

        // Apply usage-driven diet shift (Phase 17 addition).
        // Insert AFTER matchup multiply+renormalize, BEFORE building the final pie.
        var (finalPie, residual) = ApplyDietShift(state, shooter, bentNorm);
        return new RollGGeneration(finalPie, residual);
    }

    /// <inheritdoc cref="IRollGPieGenerator.Generate"/>
    public Pie<ShotLocation> Generate(PossessionState state) =>
        GenerateWithResidual(state).Pie;

    // -------------------------------------------------------------------------
    // Diet shift — usage pressure → bounded shot-diet expansion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Apply the usage-driven diet shift to the already-bent profile and return
    /// the resulting pie plus the residual pressure.
    ///
    /// <para><b>Zero-pressure short-circuit.</b> When <see cref="PossessionState.UsagePressure"/>
    /// is null or zero, the bent profile is returned unchanged and residual is 0.0.
    /// This is an EXACT branch-skip — zero-pressure possessions are numerically
    /// identical to pre-build behavior.</para>
    ///
    /// <para><b>Bounded shift math (§4a):</b>
    /// <list type="bullet">
    ///   <item>Authored tendencies are normalized to [0,1] (sum 1) — mandatory; the
    ///   0–99 scale is 100× off without this step.</item>
    ///   <item><c>intrinsicCapacity = 1 − a[authoredDom]</c> — how much the player
    ///   CAN diversify, from his authored tendency profile. A one-zone player ≈ 0;
    ///   a spread player ≈ 0.77 or higher.</item>
    ///   <item><c>requestedShift = pressure × PressureShiftScale</c></item>
    ///   <item><c>availableMass = bentDom × PressureShiftCapFraction</c> (cap so the
    ///   dominant zone is never completely emptied).</item>
    ///   <item><b>Zero-destination guard:</b> if the sum of all non-dominant bent zone
    ///   weights ≤ Epsilon, set absorbed = 0 (nowhere to redistribute; residual =
    ///   requestedShift). Never divide by zero; never count a shift as absorbed with
    ///   no destination.</item>
    ///   <item><c>absorbed = min(requested, intrinsic, available)</c></item>
    ///   <item><c>residual = requested − absorbed</c></item>
    /// </list></para>
    /// </summary>
    private (Pie<ShotLocation> pie, double residual) ApplyDietShift(
        PossessionState state, Player shooter, double[] bentNorm)
    {
        const double Eps = 1e-9;
        var pressure = state.UsagePressure ?? 0.0;

        // Zero-pressure branch-skip: return exact bent profile, residual 0.
        if (pressure <= 0.0)
            return (BuildPieFromNorm(bentNorm), 0.0);

        // Normalize authored tendencies to [0,1] — mandatory before any math.
        double tendencyTotal = shooter.RimTendency + shooter.ShortTendency
                             + shooter.MidTendency + shooter.LongTendency
                             + shooter.ThreeTendency;
        if (tendencyTotal <= 0.0)
            return (BuildPieFromNorm(bentNorm), 0.0);  // degenerate player; no shift

        var aNorm = new double[]
        {
            shooter.RimTendency   / tendencyTotal,
            shooter.ShortTendency / tendencyTotal,
            shooter.MidTendency   / tendencyTotal,
            shooter.LongTendency  / tendencyTotal,
            shooter.ThreeTendency / tendencyTotal,
        };

        // Authored dominant zone = the zone the player WANTS to shoot from.
        var authoredDomIdx    = Array.IndexOf(aNorm, aNorm.Max());
        var intrinsicCapacity = 1.0 - aNorm[authoredDomIdx];

        // Base requested shift (how much the load demands).
        var requestedShift = pressure * _cfg.PressureShiftScale;

        // Phase 28 — attention-location tilt (A1/A2/A3/A4).
        // Amplify the requested shift by the shooter's above-equal attention.
        // Insertion point is HERE — inside the pressure gate, BEFORE the
        // intrinsicCapacity cap — so a one-trick player's larger amplified request
        // spills to residual rather than being absorbed silently.
        //
        // EqualShare = 0.20: reuse the SAME named constant Roll H uses for C1/C3
        // so selection-tilt, C1, C3, and this tilt all share one neutral point.
        // Stale-reference note: a future cleanup should centralize EqualShare across
        // C1/C3/selection-tilt/Roll G into one shared named constant; it is
        // intentionally local-but-acknowledged until then.
        //
        // Bonus-only: attention below EqualShare → attentionPressure = 0 → amplifier ×1.
        const double EqualShare = 0.20;
        var attentionShare   = state.ShooterAttentionShare ?? 0.0;
        var attnPressure     = Math.Max(0.0, attentionShare - EqualShare);
        var attnAmplifier    = 1.0 + attnPressure * _cfg.AttentionShiftAmplifier;
        requestedShift      *= attnAmplifier;

        // Bent-profile dominant zone = the zone with the most mass AFTER the matchup bend.
        var bentDomIdx  = Array.IndexOf(bentNorm, bentNorm.Max());
        var bentDomMass = bentNorm[bentDomIdx];

        // Zero-destination guard: if nothing exists to redistribute into, the full
        // request becomes residual (no crash, no silent mis-count).
        var destinationMass = 0.0;
        for (var i = 0; i < 5; i++)
            if (i != bentDomIdx) destinationMass += bentNorm[i];

        double absorbed;
        if (destinationMass <= Eps)
        {
            // Nowhere to send the mass — full residual.
            absorbed = 0.0;
        }
        else
        {
            var availableMass = bentDomMass * _cfg.PressureShiftCapFraction;
            absorbed = Math.Min(requestedShift, Math.Min(intrinsicCapacity, availableMass));
        }

        var residual = requestedShift - absorbed;

        // If nothing was absorbed, return the original bent pie unchanged.
        if (absorbed <= Eps)
            return (BuildPieFromNorm(bentNorm), residual);

        // Apply shift: remove from dominant zone, redistribute proportionally to others.
        var shifted = (double[])bentNorm.Clone();
        shifted[bentDomIdx] -= absorbed;
        for (var i = 0; i < 5; i++)
        {
            if (i != bentDomIdx)
                shifted[i] += absorbed * (bentNorm[i] / destinationMass);
        }

        // Renormalize (floating-point safety).
        var shiftedTotal = 0.0;
        foreach (var v in shifted) shiftedTotal += v;
        for (var i = 0; i < 5; i++) shifted[i] /= shiftedTotal;

        return (BuildPieFromNorm(shifted), residual);
    }

    /// <summary>Build a <see cref="Pie{ShotLocation}"/> from an already-normalized
    /// five-element array indexed [Rim, Short, Mid, Long, Three].</summary>
    private Pie<ShotLocation> BuildPieFromNorm(double[] norm)
    {
        var weights = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Rim]   = norm[0],
            [ShotLocation.Short] = norm[1],
            [ShotLocation.Mid]   = norm[2],
            [ShotLocation.Long]  = norm[3],
            [ShotLocation.Three] = norm[4],
        };
        return new Pie<ShotLocation>(weights, _cfg.Epsilon);
    }

    // -------------------------------------------------------------------------
    // Helpers (BuildStubPie, BuildFastBreakPie, BuildPureTendencyPie)
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

    private Pie<ShotLocation> BuildFastBreakPie()
    {
        // Phase 16: flat rim-heavy pie for press-break possessions. The five
        // weights are config-driven calibration placeholders; the Pie constructor
        // enforces sum-to-1 (load invariant in RollGConfig.Load backs this up).
        var weights = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Rim]   = _cfg.FastBreakRim,
            [ShotLocation.Short] = _cfg.FastBreakShort,
            [ShotLocation.Mid]   = _cfg.FastBreakMid,
            [ShotLocation.Long]  = _cfg.FastBreakLong,
            [ShotLocation.Three] = _cfg.FastBreakThree,
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
