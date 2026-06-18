namespace Charm.Engine;

/// <summary>
/// Phase 27 — Defensive attention generator. Allocates a notional 100-point
/// defensive attention pie across the five offensive players, driven by gravity
/// (rim pressure — creates help/collapse attention) and usage (focal-point role —
/// the defense keys on whoever the offense runs through). Returns the full
/// five-player allocation plus three team-aggregate scalars stamped on
/// <see cref="PossessionState"/> at Roll E time.
///
/// <para><b>What attention represents.</b> Attention is a <em>relative</em>
/// allocation — where the defense spends its 100 points. Five equal threats split
/// ~20 each; a dominant focal point draws more. It is NOT "how scared the defense
/// is" — absolute offensive danger lives in the team gravity/spacing aggregates
/// and the openness interaction. Specifically, this pie captures help/collapse
/// attention (rim pressure) and focal-point attention (usage), NOT perimeter
/// attachment (represented through spacing and C2).</para>
///
/// <para><b>Normalization (required — prevents a silent scale bug).</b>
/// Gravity is raw [0,100]; usage FinalShares is [0,1]. A naive weighted sum lets
/// gravity overwhelm usage ~100× and defeats the focal-point correction while still
/// producing a valid pie. Both are normalized to [0,1] before blending:
/// gravitySignal = GravityContribution/100; usageSignal = FinalShares[i].</para>
///
/// <para><b>Floor constraint.</b> A floor (cfg.AttentionFloor) is enforced by the
/// same iterative constrained-redistribution water-fill as Roll E's
/// <c>ApplyFloorAndRail</c>. Copied per A2: extract only at a third consumer.
/// </para>
///
/// <para><b>Asymmetric team-openness interaction.</b>
/// GravitySource = sigmoid-gated top-threat term (one dominant value nearly maxes
/// it; diminishing returns on additional creators). SpacingField = accumulating
/// environment term across the four non-primary-gravity players. BaseOpenness =
/// GravitySource × (α + β × SpacingField), β &gt; α — gravity enables spacing;
/// without a dominant gravity source, spacing contributes little regardless of
/// quantity.</para>
///
/// <para><b>Passing converter (Phase 27 Session 2).</b> Computes
/// <c>TeamConversionQuality</c> from the five offensive players' Passing/Playmaking/IQ/
/// Quickness/FirstStep/GravityContribution ratings — the conversion quality that Roll H's
/// bonus-only passing block uses. Stamped separately from <c>TeamBaseOpenness</c>, which
/// reverts to the pure gravity×spacing value. Perimeter breakdown route is flat
/// (level-agnostic) this session; matchup-relative is the next session.</para>
/// </summary>
public sealed class AttentionGenerator
{
    private readonly AttentionConfig _cfg;
    private readonly GameState       _game;

    public AttentionGenerator(AttentionConfig cfg, GameState game)
    {
        _cfg  = cfg  ?? throw new ArgumentNullException(nameof(cfg));
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    /// <summary>
    /// Compute the full five-player attention allocation and team-aggregate
    /// scalars for the offense on this possession. Called at the Roll E seam
    /// (after <see cref="RollEGenerator.GenerateWithPressure"/> returns the
    /// usage FinalShares).
    ///
    /// <para>Returns: AttentionShares (length-5, indexed 0–4 matching Slot1–Slot5),
    /// TeamBaseOpenness, TeamGravityLevel, TeamSpacingLevel — all in [0,1].</para>
    ///
    /// <para>FastBreak: attention is still computed (the generator runs at the same
    /// Roll E seam regardless of break state). Roll H exempts C1/C2 via a
    /// FastBreak guard; attention values on a fast break should be treated as
    /// irrelevant by downstream consumers.</para>
    /// </summary>
    public AttentionResult Generate(PossessionState state, double[] finalShares)
    {
        var offRoster = _game.RosterFor(state.Offense);
        var offLineup = _game.LineupFor(state.Offense);

        var players = new Player?[]
        {
            offRoster.PlayerAt(offLineup.SlotAt(1)),
            offRoster.PlayerAt(offLineup.SlotAt(2)),
            offRoster.PlayerAt(offLineup.SlotAt(3)),
            offRoster.PlayerAt(offLineup.SlotAt(4)),
            offRoster.PlayerAt(offLineup.SlotAt(5)),
        };

        // Collect per-player gravity and spacing
        var gravities = new double[5];
        var spacings  = new double[5];
        for (var i = 0; i < 5; i++)
        {
            if (players[i] is Player p)
            {
                gravities[i] = p.GravityContribution;   // [0,100]
                spacings[i]  = p.SpacingContribution;   // [0,100]
            }
        }

        // ── Attention scores: gravity + usage, both normalized to [0,1] ───────
        // Normalization prevents gravity (~100 scale) overwhelming usage (~1 scale).
        var raw = new double[5];
        for (var i = 0; i < 5; i++)
        {
            if (players[i] is not null)
            {
                var gSig = gravities[i] / 100.0;   // → [0,1]
                var uSig = finalShares[i];          // already [0,1]
                raw[i] = _cfg.GravityBlendWeight * gSig + _cfg.UsageBlendWeight * uSig;
                raw[i] = Math.Max(raw[i], 0.0);
            }
        }

        var total = 0.0;
        foreach (var r in raw) total += r;
        var shares = new double[5];
        if (total > 0.0)
            for (var i = 0; i < 5; i++) shares[i] = raw[i] / total;
        else
        {
            // All null / zero — equal split (test-only guard, same as Roll E)
            var n = 0;
            for (var i = 0; i < 5; i++) if (players[i] is not null) n++;
            if (n > 0)
                for (var i = 0; i < 5; i++) if (players[i] is not null) shares[i] = 1.0 / n;
        }

        // Apply attention floor (iterative constrained redistribution — Copy of
        // RollEGenerator.ApplyFloorAndRail floor pass; rail not applied to attention)
        shares = ApplyAttentionFloor(shares, raw);

        // ── Team-aggregate scalars (§2.4) ─────────────────────────────────────
        // GravitySource: sigmoid-gated top-threat term
        var gravSorted = (double[])gravities.Clone();
        Array.Sort(gravSorted);
        var top    = gravSorted[4];   // highest
        var second = gravSorted[3];   // second-highest
        var gravRaw =   Sigmoid((top    - _cfg.GravitySigmoidCenter) / _cfg.GravitySigmoidSteepness)
                    + _cfg.SecondGravityFraction
                    * Sigmoid((second  - _cfg.GravitySigmoidCenter) / _cfg.GravitySigmoidSteepness);
        var gravMax    = 1.0 + _cfg.SecondGravityFraction;
        var gravSource = Math.Min(gravRaw / gravMax, 1.0);   // [0,1]

        // SpacingField: accumulating — mean of the four NON-primary-gravity players
        // Find index of the highest gravity player (first occurrence for ties)
        var maxGrav   = gravities[0];
        var maxGravIdx = 0;
        for (var i = 1; i < 5; i++) if (gravities[i] > maxGrav) { maxGrav = gravities[i]; maxGravIdx = i; }
        var spacingSum = 0.0;
        var spacCount  = 0;
        for (var i = 0; i < 5; i++)
        {
            if (i != maxGravIdx && players[i] is not null)
            {
                spacingSum += spacings[i];
                spacCount++;
            }
        }
        var spacField = spacCount > 0 ? Math.Min(spacingSum / (spacCount * 99.0), 1.0) : 0.0;

        // BaseOpenness: gravity enables spacing — asymmetric product form (§2.4)
        // PassingAmp removed (Phase 27 Session 2 bug fix): passing no longer multiplies
        // into TeamBaseOpenness. TeamBaseOpenness stamps the PURE gravity/spacing value
        // that C1 reads. The conversion signal rides a separate field (TeamConversionQuality).
        var baseOpenness = gravSource * (_cfg.GravityAloneYield + _cfg.SpacingMultiplier * spacField);
        baseOpenness     = Math.Min(baseOpenness, 1.0);

        var teamGravLevel  = gravSource;   // [0,1]
        var teamSpacLevel  = spacField;    // [0,1]
        var teamBaseOpen   = Math.Min(Math.Max(baseOpenness, 0.0), 1.0);

        // ── Passing converter — conversion quality (§2.3) ────────────────────
        // Computed here because this generator already has the full offensive lineup.
        // ConversionQuality = ConversionFloor
        //   + DirectPassingScale × PassingCompound             (direct: modest lift even with no activation)
        //   + ActivationScale × PlaymakingActivation × PassingCompound  (compounding: activation unlocks big term)
        // Clamped to [0,1].
        //
        // Perimeter route: flat authored quickness/first-step — level-agnostic this session.
        //   (Matchup-relative vs. the matched defender is DEFERRED to the next session —
        //   DefenderPicker.Pick throws pre-selection while this runs pre-selection.)
        // Post route: the player's own gravity contribution (already computed above).
        // Activation per player: effPlaymaking × max(perimeterRoute, postRoute).
        // Playmaking summed top-down with gentle geometric decay (PlaymakingDecay).
        // Passing: flat average of Passing/100 across five. BallHandling excluded.

        var activations = new double[5];
        var passingSum  = 0.0;
        var passingCount = 0;
        for (var i = 0; i < 5; i++)
        {
            if (players[i] is Player pp)
            {
                var iqFactor      = _cfg.IqMin + (_cfg.IqMax - _cfg.IqMin) * (pp.BasketballIQ / 100.0);
                var effPlaymaking = Math.Min(Math.Max((pp.Playmaking / 100.0) * iqFactor, 0.0), 1.0);

                // Perimeter route: flat quickness/first-step mean (level-agnostic)
                var perimeterRoute = ((pp.Quickness + pp.FirstStep) / 2.0) / 100.0;
                // Post route: player's own gravity draw (already computed; re-read here)
                var postRoute = pp.GravityContribution / 100.0;

                activations[i] = effPlaymaking * Math.Max(perimeterRoute, postRoute);

                passingSum += pp.Passing / 100.0;
                passingCount++;
            }
        }

        // Gentle top-down decay on playmaking (redundancy saturation)
        var sortedActivations = (double[])activations.Clone();
        Array.Sort(sortedActivations, (a, b) => b.CompareTo(a));  // high → low
        var playmakingActivation = 0.0;
        var decay = 1.0;
        for (var i = 0; i < 5; i++)
        {
            playmakingActivation += sortedActivations[i] * decay;
            decay *= _cfg.PlaymakingDecay;
        }

        var passingCompound = passingCount > 0 ? passingSum / passingCount : 0.0;

        var conversionQuality = _cfg.ConversionFloor
            + _cfg.DirectPassingScale * passingCompound
            + _cfg.ActivationScale * playmakingActivation * passingCompound;
        conversionQuality = Math.Min(Math.Max(conversionQuality, 0.0), 1.0);

        return new AttentionResult(shares, teamBaseOpen, teamGravLevel, teamSpacLevel, conversionQuality);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Floor enforcement (iterative constrained redistribution — floor pass only)
    // Copied from RollEGenerator.ApplyFloorAndRail floor pass per A2.
    // Extract to a shared helper only at a third consumer.
    // ─────────────────────────────────────────────────────────────────────────
    private double[] ApplyAttentionFloor(double[] shares, double[] rawScores)
    {
        var s     = (double[])shares.Clone();
        var floor = _cfg.AttentionFloor;

        for (var iter = 0; iter < 50; iter++)
        {
            var pinned      = new bool[5];
            var pinnedCount = 0;
            for (var i = 0; i < 5; i++)
            {
                if (rawScores[i] > 0.0 && s[i] <= floor)
                {
                    pinned[i] = true;
                    pinnedCount++;
                }
            }
            if (pinnedCount == 0) break;

            var floorMass = floor * pinnedCount;
            var freeMass  = 1.0  - floorMass;
            if (freeMass <= 0.0) break;

            var freeRawTotal = 0.0;
            for (var i = 0; i < 5; i++)
                if (rawScores[i] > 0.0 && !pinned[i]) freeRawTotal += rawScores[i];

            var changed = false;
            for (var i = 0; i < 5; i++)
            {
                double nv;
                if (rawScores[i] <= 0.0)
                    nv = 0.0;
                else if (pinned[i])
                    nv = floor;
                else
                    nv = freeRawTotal > 0.0
                        ? freeMass * (rawScores[i] / freeRawTotal)
                        : freeMass / Math.Max(1, 5 - pinnedCount);
                if (Math.Abs(nv - s[i]) > 1e-12) changed = true;
                s[i] = nv;
            }
            if (!changed) break;
        }
        return s;
    }

    private static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));
}

/// <summary>
/// The result of <see cref="AttentionGenerator.Generate"/>: the full five-player
/// attention allocation plus the three team-aggregate scalars.
/// </summary>
/// <param name="AttentionShares">Normalized attention allocation across the five
/// offensive slots (length 5, indexed 0–4 matching Slot1–Slot5). Sums to 1.0.
/// Zero for unpopulated slots.</param>
/// <param name="TeamBaseOpenness">The asymmetric gravity×spacing openness scalar,
/// normalized [0,1]. Low for both pure-gravity and pure-spacing lineups; peaks for
/// a lineup with both a dominant gravity source and high perimeter spacing.
/// PURE gravity×spacing value — passing no longer multiplies into this field
/// (Phase 27 Session 2 bug fix). C1 in Roll H reads this field.</param>
/// <param name="TeamGravityLevel">Normalized gravity-source term [0,1]. High when
/// the offense has at least one dominant rim-pressure threat.</param>
/// <param name="TeamSpacingLevel">Normalized spacing-field term [0,1]. Accumulates
/// across the four non-primary-gravity players.</param>
/// <param name="TeamConversionQuality">The passing/playmaking conversion quality
/// for this possession's offense, normalized [0,1]. Stamped by Roll E alongside
/// <see cref="TeamBaseOpenness"/>. Used by Roll H's bonus-only passing-converter
/// block (separate from C1; attention-independent). Neutral placeholder (0.0) until
/// Step 3 wires the full formula.</param>
public readonly record struct AttentionResult(
    double[] AttentionShares,
    double   TeamBaseOpenness,
    double   TeamGravityLevel,
    double   TeamSpacingLevel,
    double   TeamConversionQuality);
