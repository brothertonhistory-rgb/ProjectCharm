namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll G generator (Phase 9). Reads the shooter's
/// authored per-zone tendencies, runs each tendency through the coaching
/// seam (live in Phase 30 — applies ShotSelectionBias nudge), then bends them by the defending team's per-zone
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
/// <para><b>The math (v2 ratio form; Session 36 — Route B + displacement):</b>
/// <list type="number">
///   <item>Baseline: read the shooter's five tendency attributes. Route
///         through <see cref="CoachingPull.Apply"/> (live in Phase 30 —
///         applies the coach's ShotSelectionBias nudge).</item>
///   <item>Hand the coached baseline to <see cref="Matchup.DeriveDisplacement"/>
///         — the pure derivation mirroring the locked oracle
///         (tools/displacement_oracle.py): per-zone gaps via the existing
///         <see cref="Matchup.DefensiveResistance"/>/<see cref="Matchup.OffenseRating"/>
///         reads; the Phase 9 ratio-form bend applied to RESIDUALIZED gaps
///         (gap − diet-weighted skill level), so a uniform defensive upgrade
///         moves the shape by exactly zero; plus the usage-gated asymmetric
///         displacement ladder driven by the overall level (skill + gentle
///         physical term). Deltas composed from the same baseline, clamped,
///         renormalized once.</item>
///   <item>The usage-driven diet shift (Phase 17) applies downstream, LAST,
///         unchanged.</item>
/// </list></para>
///
/// <para><b>Level-mismatch behavior (Session 36):</b> the bend is shape-only —
/// residualization guarantees a level difference with a UNIFORM shape moves the
/// mix by exactly zero. Level now acts through the displacement ladder instead:
/// overmatched featured shooters are pushed outward unconditionally; advantaged
/// ones are invited inward and accept only per their own Finishing/Close. Both
/// effects are usage-gated (zero at/below an equal share) except the bend,
/// which applies whenever gaps are uneven.</para>
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

        // Session 38: fetch the shooter BEFORE the fast-break branch — the break diet now
        // bends to whoever is running it, so we need the shooter here. A null shooter must
        // still fall through to a flat fallback, never throw.
        var shooter = _game.RosterFor(state.Offense).PlayerAt(slot);

        // Session 38: fast-break shot location BENDS to the shooter. The break dictates a
        // modern base diet (rim-heavy, real three share, long twos nearly gone); the
        // shooter's own stored neutral tendencies pull it zone-by-zone and the offensive
        // coach's PaceBias tilts the three share. Residual stays 0.0 (no volume load on a
        // transition possession).
        //   - shooter present → shooter-bent, PaceBias-tilted pie.
        //   - shooter null    → flat configured base (BuildFlatFastBreakPie). NOT
        //                       BuildStubPie: that is the 35/36 HALFCOURT diet, and using it
        //                       here would silently turn a missing-shooter break into a
        //                       halfcourt-looking possession (a real regression).
        if (state.FastBreak)
            return new RollGGeneration(
                shooter is null
                    ? BuildFlatFastBreakPie()
                    : BuildFastBreakPie(shooter, _game.CoachFor(state.Offense)),
                0.0);

        // Non-fast-break, no shooter: ordinary halfcourt stub (unchanged).
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

        // Baseline tendencies through the coaching seam (live in Phase 30).
        // Pass the offensive coach so CoachingPull can apply ShotSelectionBias nudge.
        var offCoach = _game.CoachFor(state.Offense);
        var (tRim, tShort, tMid, tLong, tThree) =
            CoachingPull.Apply(shooter, offCoach, malleability: null);

        // Session 57 — PostMoves interior diet tilt. A high-PostMoves shooter hunts more
        // interior shots: his coached Rim + Short shares are multiplied by the SAME factor
        // (authored Rim:Short ratio preserved), so interior weight rises and Mid/Long/Three
        // shed share only via renormalization. This sits on the shot diet BEFORE the matchup
        // bend, so it flows through displacement naturally and NEVER calls OffenseRating —
        // make% is untouched. Fast-break/transition already returned above (a running
        // possession never posts up), and this feeds BOTH the zero-defender fallback and the
        // normal path below. Multiplicative → a zero authored Rim or Short stays zero (it
        // amplifies existing interior intent, never invents a post game).
        //
        // IDENTITY-PATH BRANCH lives inside the pure helper: PostDietSpan 0 or PostMoves <= 50
        // returns the coached vector UNCHANGED (bit-for-bit) — no multiply, no renormalize (a
        // renormalize by ×1 can still perturb float bits). The intrinsicCapacity read inside
        // ApplyDietShift stays on the shooter's RAW authored tendencies, deliberately NOT this
        // tilted vector: appetite is not flexibility.
        (tRim, tShort, tMid, tLong, tThree) = TiltInteriorDiet(
            tRim, tShort, tMid, tLong, tThree, shooter.PostMoves, _cfg.PostDietSpan);

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

        // 1–5 defenders: Session 36 — Route B residualized bend + matchup
        // displacement, both computed by the pure derivation on Matchup
        // (stage-for-stage mirror of tools/displacement_oracle.py; if they ever
        // disagree, the oracle wins).
        //
        // The derivation's desiredDiet is the COACHED pre-bend baseline — the
        // SAME five values the old bend multiplied. The raw authored read inside
        // ApplyDietShift (intrinsicCapacity) is a DIFFERENT read, deliberately:
        // the coach can bend where a player shoots from, not how flexible he
        // inherently is. Do not unify them.
        var dispDefenders = new List<DisplacementDefender>(populated);
        foreach (var d in defenders)
            if (d is not null) dispDefenders.Add(DisplacementDefender.FromPlayer(d));

        var coached = new double[] { tRim, tShort, tMid, tLong, tThree };

        // Usage source: null and 0 both mean mag = 0 — matches the oracle's gate.
        var trace = Matchup.DeriveDisplacement(
            coached, shooter, dispDefenders, state.UsagePressure ?? 0.0, _matchup);

        // Apply usage-driven diet shift (Phase 17 addition) — unchanged,
        // downstream, LAST. The derivation's final shares land exactly where
        // bentNorm went pre-Session-36.
        var (finalPie, residual) = ApplyDietShift(state, shooter, trace.Final);
        return new RollGGeneration(finalPie, residual, trace.Level);
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

        // Session 57 — PostMoves pressure resistance. A strong post player resists being
        // pushed OFF an interior spot: shrink the requested shift (which lowers `absorbed`),
        // never the intrinsicCapacity cap. Gated to an interior bent-dominant zone; the pure
        // helper returns requestedShift UNCHANGED on the identity path (span 0, PostMoves <= 50,
        // or a perimeter-dominant zone) so today's displacement is reproduced exactly. Reducing
        // requestedShift reduces `residual` in step, which is correct: less demand to vacate =
        // less spillover.
        requestedShift = ResistPressureShift(
            requestedShift, bentDomIdx, shooter.PostMoves, _cfg.PostPressureResistanceSpan);

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
    // Helpers (BuildStubPie, BuildFlatFastBreakPie, BuildFastBreakPie,
    //          DeriveFastBreakPie, BuildPureTendencyPie)
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

    private Pie<ShotLocation> BuildFlatFastBreakPie()
    {
        // Session 38: the flat, UNBENT fast-break base diet — the null-shooter fallback
        // (no shooter identity to bend). The five weights are config-driven calibration
        // placeholders summing to 1.0 (the load invariant in RollGConfig.Load backs this
        // up); the Pie constructor re-checks sum-to-1.
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

    /// <summary>
    /// The shooter-bent fast-break pie: the configured base diet pulled toward this
    /// shooter's own stored neutral tendencies (identity-relative ratio, the same
    /// multiplier idiom Roll G's matchup bend uses) and tilted on the three share by the
    /// offensive coach's PaceBias. Reads the RAW stored tendencies — NOT
    /// <see cref="CoachingPull.Apply"/> — because shot-selection philosophy is the future
    /// coach layer's job; only tempo (PaceBias) tilts the break this session.
    /// </summary>
    private Pie<ShotLocation> BuildFastBreakPie(Player shooter, CoachProfile offCoach) =>
        DeriveFastBreakPie(
            shooter.RimTendency, shooter.ShortTendency, shooter.MidTendency,
            shooter.LongTendency, shooter.ThreeTendency,
            offCoach.PaceBias, _cfg);

    /// <summary>
    /// LOCKED-SPEC PORT (Session 38) — constant-for-constant mirror of
    /// <c>tools/fastbreak_diet_oracle.py</c> (fb_pie). Zone order Rim, Short, Mid, Long,
    /// Three. If the C# and the oracle ever disagree, THE ORACLE WINS.
    ///
    /// <para>Math: each zone's neutral tendency is read as a share of 100 (the fixed
    /// constant the oracle uses — NOT the tendency sum; generated tendencies sum to 100).
    /// The identity ratio <c>(share / mean) ^ Beta</c> is clamped to [CapLo, CapHi] and
    /// multiplies the base diet. The three share is then tilted by
    /// <c>1 + PaceTilt * (PaceBias − 5)</c>. One renormalization yields the pie. There is
    /// NO additive floor: a non-shooter's tiny three ratio keeps his break ~rim-run.</para>
    ///
    /// <para>Public static so the Phase 58 golden-parity harness can reproduce every
    /// fixture vector directly — the same shape as <see cref="Matchup.DeriveDisplacement"/>.
    /// Constants are read from the passed <paramref name="cfg"/> so a config/default drift
    /// fails the parity check loudly.</para>
    /// </summary>
    public static Pie<ShotLocation> DeriveFastBreakPie(
        int rimTend, int shortTend, int midTend, int longTend, int threeTend,
        double paceBias, RollGConfig cfg)
    {
        var tend = new[] { rimTend, shortTend, midTend, longTend, threeTend };
        var baseDiet = new[]
        {
            cfg.FastBreakRim, cfg.FastBreakShort, cfg.FastBreakMid,
            cfg.FastBreakLong, cfg.FastBreakThree,
        };
        var mean = new[]
        {
            cfg.FastBreakMeanRim, cfg.FastBreakMeanShort, cfg.FastBreakMeanMid,
            cfg.FastBreakMeanLong, cfg.FastBreakMeanThree,
        };

        var blend = new double[5];
        for (var z = 0; z < 5; z++)
        {
            // Neutral share of 100 (fixed constant — oracle parity, NOT the tendency sum).
            var share = tend[z] / 100.0;
            var ratio = Math.Pow(share / mean[z], cfg.FastBreakShooterPull);
            ratio = Math.Clamp(ratio, cfg.FastBreakRatioCapLow, cfg.FastBreakRatioCapHigh);
            blend[z] = baseDiet[z] * ratio;
        }

        // PaceBias tilt on the three share only (index 4). Run-and-gun (>5) raises
        // transition threes; grind-it-out (<5) trims them. RollGConfig.Load validation
        // guarantees this multiplier stays strictly positive across PaceBias ∈ [1, 10].
        blend[4] *= 1.0 + cfg.FastBreakPaceTilt * (paceBias - 5.0);

        var sum = 0.0;
        foreach (var v in blend) sum += v;

        var weights = new Dictionary<ShotLocation, double>
        {
            [ShotLocation.Rim]   = blend[0] / sum,
            [ShotLocation.Short] = blend[1] / sum,
            [ShotLocation.Mid]   = blend[2] / sum,
            [ShotLocation.Long]  = blend[3] / sum,
            [ShotLocation.Three] = blend[4] / sum,
        };
        return new Pie<ShotLocation>(weights, cfg.Epsilon);
    }

    /// <summary>
    /// Session 57 — PostMoves interior diet tilt (pure, testable). Multiplies the Rim + Short
    /// shares by (1 + span · postLift), preserving their ratio, then renormalizes all five to
    /// sum 1. postLift = max(0, (PostMoves − 50) / 49). Returns the inputs UNCHANGED
    /// (bit-for-bit) on the identity path — span 0 or PostMoves ≤ 50 — with no multiply and no
    /// renormalize. Multiplicative: a zero Rim or Short stays zero (it amplifies existing
    /// interior intent, never invents a post game). Mid/Long/Three get no direct multiplier;
    /// their shares fall only via the renormalization.
    /// </summary>
    public static (double rim, double shortT, double mid, double lng, double three)
        TiltInteriorDiet(double rim, double shortT, double mid, double lng, double three,
                         int postMoves, double postDietSpan)
    {
        if (postDietSpan <= 0.0) return (rim, shortT, mid, lng, three);
        var postLift = Math.Max(0.0, (postMoves - 50.0) / 49.0);
        if (postLift <= 0.0) return (rim, shortT, mid, lng, three);

        var mult = 1.0 + postDietSpan * postLift;
        rim    *= mult;
        shortT *= mult;
        var sum = rim + shortT + mid + lng + three;
        if (sum <= 0.0) return (rim, shortT, mid, lng, three);
        return (rim / sum, shortT / sum, mid / sum, lng / sum, three / sum);
    }

    /// <summary>
    /// Session 57 — PostMoves pressure resistance (pure, testable). Shrinks the requested diet
    /// shift by (1 − span · postLift) when the bent-dominant zone is interior (Rim = index 0,
    /// Short = index 1). Returns requestedShift UNCHANGED on the identity path — span 0,
    /// PostMoves ≤ 50, or a perimeter-dominant zone. The [0,1] span bound (enforced in
    /// RollGConfig.Load) keeps the factor in [0,1], so mass is never added back onto the
    /// dominant zone. In a non-saturated case (requestedShift is the binding cap), absorbed
    /// equals this reduced shift, so its monotonicity in PostMoves is absorbed's monotonicity.
    /// </summary>
    public static double ResistPressureShift(double requestedShift, int bentDomIdx,
                                             int postMoves, double resistanceSpan)
    {
        if (resistanceSpan <= 0.0) return requestedShift;
        if (bentDomIdx != 0 && bentDomIdx != 1) return requestedShift;
        var postLift = Math.Max(0.0, (postMoves - 50.0) / 49.0);
        if (postLift <= 0.0) return requestedShift;
        return requestedShift * (1.0 - resistanceSpan * postLift);
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
