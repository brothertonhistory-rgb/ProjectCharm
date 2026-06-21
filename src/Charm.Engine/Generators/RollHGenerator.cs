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
/// DEC-6 empty-slot fallback keeps the configured baseline, same shape as the make door.</para>
///
/// <para><b>Phase 8 — matchup-aware foul door.</b> The foul rate is now computed via
/// Matchup.FoulRate rather than read flat from config. The contest is offense-dominant
/// (FoulDrawing carries 0.80 weight) and defender-light (Discipline carries 0.20),
/// both expressed as deviations from AttributeMidpoint (50). Low FoulDrawing is NOT a
/// skill — it's absence of opportunity — so the per-zone foul floor is close to the
/// baseline (small downward range) while the ceiling is far above (wide upward range),
/// encoding the asymmetry in config rather than in the contest weights.
///
/// The per-zone and-1 split (MafFraction) further divides each fouled outcome into
/// MadeAndFouled (and-1, shot went in) and MissFouled (two-shot trip). The split varies
/// sharply by zone — rim fouls often become and-1s (layup through contact); three fouls
/// rarely (shot disrupted). NOT matchup-aware; Emmett's call.
///
/// The carve-then-convert math now carves BOTH block and foul off the top:
/// nonBlockNonFoul = 1 − block − foul. Made = makePct × nonBlockNonFoul. The three
/// remaining slices (Miss, OOBLost, OOBRetained) fill nonBlockNonFoul − Made, preserving
/// their relative proportions. The pie always sums to 1 for any (makePct, block, foul)
/// triple where block + foul &lt; 1.</para>
///
/// <para><b>Zone→attribute mapping.</b>
/// Three and Long read <see cref="Player.Outside"/>;
/// Mid reads <see cref="Player.Mid"/>;
/// Short reads <see cref="Player.Close"/>;
/// Rim reads <see cref="Player.Finishing"/>.
/// The zone/location distinction is intentional: ShotLocation names WHERE the
/// shot comes from; the player attribute names the SKILL needed to convert it.</para>
///
/// <para><b>Fallback when no player is present.</b> If the roster is not populated
/// (a harness that constructs a Resolver without calling SetStarter), PlayerAt
/// returns null and the generator falls back to the flat stub behaviour — identical
/// to what RollHStubPieGenerator produces. Separately (DEC-6), if the shooter
/// is present but the matched defending slot is empty, the make, block, and foul
/// doors all read the raw own-rating / configured baseline with no matchup term.</para>
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
        // Putback path — return the putback pie unchanged (Phase 4 work).
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
        // raw own-rating read — no matchup term.
        var defenderSlot = DefenderPicker.Pick(state);
        var defender     = _game.RosterFor(state.Defense).PlayerAt(defenderSlot);

        var effectiveRating = defender is null
            ? Matchup.OffenseRating(zone, player)
            : Matchup.EffectiveRating(zone, player, defender, _matchup);

        var makePct = _cfg.MakeProbability(zone, effectiveRating);

        // Phase 27 — attention/openness make% adjustments (C1, C2, C3).
        // FastBreak: C1 and C2 are halfcourt effects only — skip on breaks.
        //   C3 is skipped automatically because UsagePressure = 0.0 on FastBreak.
        // Putback: already short-circuited above (line 81) — never reached here.
        //
        // Read the four stamped attention scalars (null = Roll E has not run = 0.0/safe default).
        var shooterAttn   = state.ShooterAttentionShare ?? 0.0;
        var teamOpenness  = state.TeamBaseOpenness      ?? 0.0;
        var teamSpac      = state.TeamSpacingLevel      ?? 0.0;
        var teamGrav      = state.TeamGravityLevel      ?? 0.0;

        // ── C1: bonus-only openness nudge ────────────────────────────────────
        // Measures attention against the equal-share neutral point (0.20).
        // AttentionRelief = max(0, 0.20 − a)  →  a=0.10 → relief; a=0.20 → 0; a=0.35 → 0.
        // ShooterOpenness = clamp(TeamBaseOpenness × AttentionRelief × ReliefScale, 0, 1).
        // Adjustment is ALWAYS ≥ 0 (bonus-only; never docks make% on a heavily-attended shooter).
        // Gravity is NOT re-read here — it has already done its work upstream through
        // TeamGravityLevel, TeamBaseOpenness, the attention pie, and C2. Reading raw
        // GravityContribution in C1 would be a fifth bite at the same input.
        // Slots between the matchup logistic and the Phase 17 block (A4).
        const double EqualShare = 0.20;
        var c1Bonus = 0.0;
        if (!state.FastBreak)
        {
            var relief     = Math.Max(0.0, EqualShare - shooterAttn);
            var c1Openness = Math.Min(teamOpenness * relief * _cfg.C1ReliefScale, 1.0);
            c1Bonus        = Math.Max(c1Openness, 0.0);   // guaranteed non-negative
            makePct       += c1Bonus;
            if (makePct > 1.0) makePct = 1.0;
        }

        // ── C2: zone-specific imbalance penalty ──────────────────────────────
        // Spacing-heavy lineup (spac > grav): halfcourt threes contested — no gravity
        //   scrambles the defense; perimeter shots below open rate.
        // Gravity-heavy lineup (grav > spac): packed paint — rim attempts suffer.
        // Halfcourt + non-putback only (A5 — putback already short-circuited above).
        var c2Penalty = 0.0;
        if (!state.FastBreak)
        {
            var imbalance = teamSpac - teamGrav;   // positive = spacing-heavy; negative = gravity-heavy
            if (zone == ShotLocation.Three || zone == ShotLocation.Long)
                c2Penalty = Math.Max(0.0, imbalance) * _cfg.C2ImbalanceScale;
            else if (zone == ShotLocation.Rim || zone == ShotLocation.Short)
                c2Penalty = Math.Max(0.0, -imbalance) * _cfg.C2ImbalanceScale;
            // Mid: no zone-imbalance penalty
            makePct -= c2Penalty;
            if (makePct < 0.0) makePct = 0.0;
        }

        // Phase 17 — usage-pressure efficiency penalties (C3 amplification on both terms).
        // C3: AttentionPressure = max(0, a − 0.20). Both Phase 17 terms are multiplied
        // by (1 + AttentionPressure × C3AttentionAmplifier):
        //   - Equal/below-share attention → amplifier = ×1 → Phase 17 penalty unchanged (anchor).
        //   - Above-share attention → amplifier > ×1 → both penalties amplified.
        //   - Zero usage pressure → zero penalty regardless of attention (C3 cannot create a penalty).
        //   - Attention alone → no C3 penalty (only fires inside the usage-pressure gate).
        //
        // Read the two stamped scalars (null = Roll E/G has not run = 0.0 safe default).
        // If BOTH are zero, this block is branch-skipped — zero-pressure possessions
        // are numerically identical to pre-build behavior (the regression guard).
        //
        // Attribution note (§5 / §6 observability — six separable values):
        //   (1) matchup baseline     → makePct after logistic (before C1/C2/C3)
        //   (2) C1 bonus             → c1Bonus (≥ 0)
        //   (3) C2 zone penalty      → c2Penalty (≥ 0)
        //   (4) Phase 17 base penalty → without C3: pressure × taxScale + residual × residualScale
        //   (5) C3 incremental       → (amplifier − 1) × base penalty
        //   (6) final make%          → makePct after all adjustments
        var usagePressure = state.UsagePressure         ?? 0.0;
        var usageResidual = state.UsageResidualPressure ?? 0.0;

        if (usagePressure > 0.0 || usageResidual > 0.0)
        {
            // C3 amplifier: above equal-share attention amplifies both penalty terms.
            // At equal/below share, AttentionPressure = 0 → amplifier = ×1 → Phase 17 unchanged.
            var attentionPressure = Math.Max(0.0, shooterAttn - EqualShare);
            var c3Amplifier       = 1.0 + attentionPressure * _cfg.C3AttentionAmplifier;

            // (b) Volume-tax: small all-shots reduction scaled by volume pressure.
            // A versatile player feels this almost exclusively (residual ≈ 0).
            // C3 amplifies: high usage + above-share attention → larger vol-tax.
            makePct *= (1.0 - usagePressure * _cfg.PressureVolumeTaxScale * c3Amplifier);

            // (c) Residual penalty: larger reduction for the load that could not
            // shift to alternate zones. A forced specialist feels this heavily.
            // Applied to whatever zone was actually taken (including the preferred zone).
            // C3 amplifies: a forced specialist under defensive attention takes the biggest hit.
            makePct -= usageResidual * _cfg.PressureResidualPenaltyScale * c3Amplifier;

            // Clamp to [0, 1] — a heavily-pressured specialist approaches but
            // cannot cross zero. Clamp is flagged; reaching 0 means over-tuning.
            if (makePct < 0.0) makePct = 0.0;
            if (makePct > 1.0) makePct = 1.0;
        }

        // ── Passing converter: bonus-only, attention-independent (C4) ─────────
        // Passing CONVERTS the gravity/spacing advantage into a made shot — it does
        // NOT generate the advantage (that is C1's domain via teamOpenness).
        // Therefore this bonus fires regardless of attention allocation and is
        // SEPARATE from C1 (folding it into C1 would zero it whenever the defense
        // plays evenly — the v1–v3 bug fixed in v4).
        //
        // PassingBonus = MaxPassingBonus × conversionQuality × opportunityGate
        //   opportunityGate = lerp(OpportunityFloor, 1.0, BaseOpenness)
        //     — small floor at zero opportunity; → 1.0 as the gravity/spacing engine roars
        //   conversionQuality = DIRECT term (passing × floor) + activation-gated compound
        //     — stamped on PossessionState at Roll E time
        //
        // Putback: already short-circuited above (line 81) — never reached here.
        // FastBreak: skipped — halfcourt converter only (no gravity/spacing engine on a break).
        if (!state.FastBreak)
        {
            var conversionQuality = state.TeamConversionQuality ?? 0.0;
            var opportunityGate   = _cfg.PassingOpportunityFloor
                                  + (1.0 - _cfg.PassingOpportunityFloor) * teamOpenness;
            var passingBonus      = _cfg.MaxPassingBonus * conversionQuality * opportunityGate;
            passingBonus          = Math.Max(passingBonus, 0.0);   // bonus-only guarantee
            makePct              += passingBonus;
            if (makePct > 1.0) makePct = 1.0;
        }

        // ── C5.5: Screening interior make% bonus (Phase 42) ──────────────────
        // Stage 2 offensive counterweight: all five offensive players (shooter
        // included — a screen-setting shooter still contributes to the team's
        // screening environment before the release) aggregate their Screening
        // with an ACCELERATING curve and LIFT make% on interior shots.
        //
        // All-five-aggregate (different from C6's off-ball-only): no exclusions.
        // Fixed denominator 5.0 (full lineup capacity) — same "fixed at capacity"
        // discipline as C6's 4.0. Missing/unpopulated slots contribute 0.0;
        // one elite screener is a sliver, five compound.
        //
        // Bonus-only (cannot lower make%). Halfcourt + interior-zone only this
        // session — the perimeter unlock lands when OffBallDefense is authored.
        //
        // DO NOT upper-clamp here. C6 follows immediately and may offset this
        // signed bonus. The single Math.Clamp(0, 1) lives at the end of C6,
        // settling the paired signed terms together. If C5.5 clamped to 1.0
        // before C6 ran, the symmetric-cancellation contract would break in the
        // upper make% range (pre=0.95 + bonus=0.147 → premature clamp to 1.0 →
        // C6 subtracts 0.147 → 0.853 instead of the correct 0.950).
        if (!state.FastBreak &&
            (zone == ShotLocation.Rim || zone == ShotLocation.Short))
        {
            var offRoster = _game.RosterFor(state.Offense);
            var offLineup = _game.LineupFor(state.Offense);

            var screeningSum = 0.0;
            for (var i = 1; i <= 5; i++)
            {
                var screener = offRoster.PlayerAt(offLineup.SlotAt(i));
                screeningSum += screener is not null ? screener.Screening / 100.0 : 0.0;
            }

            var screeningShare = screeningSum / 5.0;   // always full five-screener capacity
            var screeningBonus = _cfg.ScreeningBonusScale
                               * Math.Pow(screeningShare, _cfg.ScreeningAggregateExponent);
            makePct += screeningBonus;
            // No clamp here — C6 follows and may offset. See deferred-clamp note above.
        }

        // ── C6: HelpDefense interior make% suppression (Phase 41) ────────────
        // Stage 2 of the interior defensive sequence: the four off-ball defenders
        // rotate to help after the primary defender (Stage 1) is beaten. Their
        // HelpDefense aggregates with an ACCELERATING curve — one good helper is a
        // sliver; four compound into a meaningful identity (roster-identity principle).
        //
        // Off-ball-only: the matched defender (defenderSlot.Number) had his Stage 1
        // contest above; exclude him unconditionally here. Every remaining null/
        // unpopulated slot contributes 0.0 — the denominator is always the full
        // four-helper capacity (4.0), never the count of populated helpers. This makes
        // partial lineups degrade safely, keeps one elite helper a sliver, and makes
        // four elite helpers a genuine defensive identity.
        //
        // Halfcourt + interior-zone only. Compounds-with-but-separate-from RimProtection:
        // C6 touches make% (Stage 2); the block door below touches block weight (Stage 3).
        // No double-subtraction.
        //
        // Paired with C5.5 (Screening +) this session. The single Math.Clamp below
        // settles both signed terms together — neither C5.5 nor C6 clamps alone,
        // preserving the symmetric-cancellation contract across the full make% range.
        if (!state.FastBreak &&
            (zone == ShotLocation.Rim || zone == ShotLocation.Short))
        {
            var defRoster = _game.RosterFor(state.Defense);
            var defLineup = _game.LineupFor(state.Defense);

            var offBallSum = 0.0;
            for (var i = 1; i <= 5; i++)
            {
                if (i == defenderSlot.Number) continue;   // exclude the matched defender
                var helper = defRoster.PlayerAt(defLineup.SlotAt(i));
                offBallSum += helper is not null ? helper.HelpDefense / 100.0 : 0.0;
            }

            var offBallShare          = offBallSum / 4.0;   // always full four-helper capacity
            var helpDefenseSuppression = _cfg.HelpDefenseSuppressionScale
                                       * Math.Pow(offBallShare, _cfg.HelpDefenseAggregateExponent);
            makePct -= helpDefenseSuppression;
            // After both C5.5 (Screening +) and C6 (HelpDefense −) have applied,
            // settle makePct to [0,1] once. This is the saturation guard for the
            // paired signed terms — neither C5.5 nor C6 clamps alone, so that the
            // symmetric-cancellation contract holds across the full make% range.
            makePct = Math.Clamp(makePct, 0.0, 1.0);
        }

        // Phase 7 — matchup-aware block door. Compute the bent block weight from the
        // matchup, or fall back to the configured baseline if the defending slot is empty
        // (DEC-6, same guard as the make door above).
        var blockWeight = defender is null
            ? _cfg.BlockWeight(zone)
            : Matchup.BlockWeight(zone, player, defender,
                                  _cfg.BlockWeight(zone), _matchup);

        // Phase 8 — matchup-aware foul door. Compute the bent foul rate from the
        // matchup, or fall back to the configured per-zone baseline (DEC-6, same
        // empty-slot shape as the make and block doors).
        var foulRate = defender is null
            ? _cfg.FoulRate(zone)
            : Matchup.FoulRate(zone, player, defender,
                               _cfg.FoulRate(zone), _matchup);

        return BuildRealPie(zone, makePct, blockWeight, foulRate);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build the real seven-way pie (Phase 8). Block AND foul are carved off the top
    /// (carve-then-convert); the logistic make probability is the conversion rate GIVEN
    /// the shot is not blocked AND not fouled: Made = makePct × (1 − block − foul).
    ///
    /// <para><b>Foul split.</b> The carved foul is divided into MadeAndFouled (and-1)
    /// and MissFouled (two-shot trip) by the per-zone MafFraction — NOT matchup-aware.</para>
    ///
    /// <para><b>Remaining slices.</b> Miss, OOBLost, OOBRetained fill
    /// nonBlockNonFoul − Made, preserving relative proportions from BaseMiss / BaseMissOOB*.</para>
    ///
    /// <para><b>Overflow guard.</b> Throws if block + foul ≥ 1. Unreachable given
    /// configured ceilings, but defense in depth.</para>
    /// </summary>
    private Pie<ShotResult> BuildRealPie(ShotLocation zone, double makePct,
                                         double blockWeight, double foulRate)
    {
        var block           = blockWeight;
        var foul            = foulRate;
        var nonBlockNonFoul = 1.0 - block - foul;

        if (nonBlockNonFoul <= 0.0)
            throw new InvalidOperationException(
                $"RollHGenerator: block + foul = {block + foul:F4} >= 1 for zone {zone}. " +
                "Ceiling configuration permits an impossible matchup.");

        var made        = makePct * nonBlockNonFoul;   // clean conversion given not blocked, not fouled

        var mafFraction = _cfg.MafFraction(zone);
        var maf         = foul * mafFraction;           // MadeAndFouled (and-1)
        var missFouled  = foul * (1.0 - mafFraction);  // MissFouled (two-shot trip)

        // Miss and OOB slices fill nonBlockNonFoul - Made, preserving relative proportions.
        var nonMadeBase  = _cfg.BaseMiss
                         + _cfg.BaseMissOutOfBoundsLost
                         + _cfg.BaseMissOutOfBoundsRetained;

        var nonMadeShare = nonBlockNonFoul - made;     // = nonBlockNonFoul x (1 - makePct), >= 0
        var scale        = nonMadeBase > 0.0 ? nonMadeShare / nonMadeBase : 0.0;

        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = made,
            [ShotResult.MadeAndFouled]           = maf,
            [ShotResult.Miss]                    = _cfg.BaseMiss                    * scale,
            [ShotResult.MissFouled]              = missFouled,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.BaseMissOutOfBoundsLost     * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.BaseMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked]                 = block,
        };

        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }

    /// <summary>
    /// Stub fallback: flat pie identical to what RollHStubPieGenerator produces.
    /// Used when the roster is unpopulated. Block and foul come from config; no matchup.
    /// </summary>
    private Pie<ShotResult> BuildStubPie(ShotLocation zone)
    {
        var block           = _cfg.BlockWeight(zone);
        var foul            = _cfg.FoulRate(zone);
        var nonBlockNonFoul = 1.0 - block - foul;

        var made        = _cfg.BaseMade * nonBlockNonFoul;

        var mafFraction = _cfg.MafFraction(zone);
        var maf         = foul * mafFraction;
        var missFouled  = foul * (1.0 - mafFraction);

        var nonMadeBase  = _cfg.BaseMiss
                         + _cfg.BaseMissOutOfBoundsLost
                         + _cfg.BaseMissOutOfBoundsRetained;
        var nonMadeShare = nonBlockNonFoul - made;
        var scale        = nonMadeBase > 0.0 ? nonMadeShare / nonMadeBase : 0.0;

        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = made,
            [ShotResult.MadeAndFouled]           = maf,
            [ShotResult.Miss]                    = _cfg.BaseMiss                    * scale,
            [ShotResult.MissFouled]              = missFouled,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.BaseMissOutOfBoundsLost     * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.BaseMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked]                 = block,
        };
        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }

    /// <summary>
    /// Putback pie — applies Phase 8 carve-then-convert to the putback shot population,
    /// using Rim foul baseline and MafFraction (a putback is always at the Rim).
    /// PutbackMade is the conversion rate given not blocked AND not fouled.
    /// The real putback matchup tilt is Phase 4 work.
    /// </summary>
    private Pie<ShotResult> BuildPutbackPie()
    {
        var block           = _cfg.PutbackBlocked;
        var foul            = _cfg.FoulRate(ShotLocation.Rim);
        var nonBlockNonFoul = 1.0 - block - foul;

        var made        = _cfg.PutbackMade * nonBlockNonFoul;

        var mafFraction = _cfg.MafFraction(ShotLocation.Rim);
        var maf         = foul * mafFraction;
        var missFouled  = foul * (1.0 - mafFraction);

        var nonMadeBase  = _cfg.PutbackMiss
                         + _cfg.PutbackMissOutOfBoundsLost
                         + _cfg.PutbackMissOutOfBoundsRetained;
        var nonMadeShare = nonBlockNonFoul - made;
        var scale        = nonMadeBase > 0.0 ? nonMadeShare / nonMadeBase : 0.0;

        var weights = new Dictionary<ShotResult, double>
        {
            [ShotResult.Made]                    = made,
            [ShotResult.MadeAndFouled]           = maf,
            [ShotResult.Miss]                    = _cfg.PutbackMiss                    * scale,
            [ShotResult.MissFouled]              = missFouled,
            [ShotResult.MissOutOfBoundsLost]     = _cfg.PutbackMissOutOfBoundsLost     * scale,
            [ShotResult.MissOutOfBoundsRetained] = _cfg.PutbackMissOutOfBoundsRetained * scale,
            [ShotResult.Blocked]                 = block,
        };
        return new Pie<ShotResult>(weights, _cfg.Epsilon);
    }
}
