namespace Charm.Engine;

/// <summary>
/// S88 — WHO GOT BACK. The per-man transition-defence model: the C# port of the LOCKED
/// oracle <c>tools/transition_defense_oracle.py</c>, constant for constant.
///
/// <para><b>The basketball.</b> Before S88 a fast break was ONE TEAM AVERAGE against
/// another — the defence's mean Hustle against the offence's shaved a couple of points off
/// the make and that was the entire transition defence. Nobody guarded anybody and the
/// engine could not tell a break against an elite rim protector from a break against a
/// shooting guard. It was the one place left where a team was a scalar instead of five men.
/// S88 replaces it: each of the five carries a GOT-BACK number — his legs, and how deep the
/// man he is guarding starts — and that one number does three jobs.</para>
///
/// <list type="number">
///   <item><b>WHO</b> is the defender on this break (relative, within the lineup — the
///   weight <see cref="TransitionDefenderPicker"/> draws on).</item>
///   <item><b>HOW SET</b> he is when he arrives (absolute — <see cref="ArrivalQuality"/>;
///   a man who sprinted back is closer to a set defender than one who arrived late).</item>
///   <item><b>HOW MANY</b> of them got back (<see cref="TeamAggregate"/> — the dominant
///   channel on conversion, and the one the old Hustle wire was reaching for).</item>
/// </list>
///
/// <para><b>Rulings encoded</b> (Emmett, S88 design conversation; full text in the oracle
/// header). R1 nobody is ever impossible, hence a luck floor and a weighted draw rather than
/// a threshold. R2 depth is set by THE MAN YOU ARE GUARDING, never by your own body — read
/// off the OPPOSING lineup, so a defence is not stranded under the rim against a team that
/// goes small. R3 the break block is a CHASE-DOWN: speed and length lead, rim protection is
/// the junior partner. R4 a break stays a better look than a halfcourt set, so the defensive
/// read is discounted — but it is not free, which is what it was. R5 being there is worth
/// something on its own, independent of any rating. R6 Hustle rides INSIDE the legs term and
/// does not get its own channel; the old team-mean Hustle wire retires into this. R7 where he
/// shot from moves him on this trip. R8 no discrete "nobody got back" branch — a very weak
/// contest IS the runaway dunk.</para>
///
/// <para><b>Two layers on purpose.</b> The RAW numeric primitives take doubles and are what
/// golden parity binds to, so the fixture drives THIS code rather than a transcription of it
/// (same reason <see cref="FoulCommitter"/> is shaped this way). The Player-facing overloads
/// sit on top and own the one mapping question the oracle does not answer: the oracle's
/// <c>post</c> is <see cref="Matchup.Postness"/> and its <c>length</c> is
/// <see cref="Matchup.LengthRating"/>, both of which blend to weights summing to 1.0 and so
/// share the raw 0–99 rating scale the oracle's constants assume.</para>
///
/// <para><b>All magnitudes are CALIBRATION PLACEHOLDERS</b> living on
/// <see cref="MatchupConfig"/>. None is ever suite-asserted (page-only calibration
/// principle); the suite asserts parity against the oracle and the wiring invariants.</para>
/// </summary>
public static class TransitionDefense
{
    /// <summary>The oracle's <c>clamp</c> — symmetric about zero, bounds [-1, +1]. Every
    /// rating read below is normalised as <c>(rating − 50) / 49</c> and passed through
    /// this, so a 0-rated and a 99-rated player sit exactly at the two ends.</summary>
    public static double Clamp(double x) => Math.Clamp(x, -1.0, 1.0);

    /// <summary>The got-back number of a league-average man on a neutral assignment —
    /// <c>LuckFloor + 1</c>. The team aggregate normalises by the occupied count times this,
    /// so five average men on an average offence give exactly 1.0 and the anchor holds.
    /// Same anchoring discipline as S62's per-man reach-in aggregate.</summary>
    public static double ReferenceGotBack(MatchupConfig cfg)
        => cfg.TransitionGotBackLuckFloor + 1.0;

    // ─────────────────────────────────────────────────────────────────────────
    //  RAW PRIMITIVES — golden parity binds here.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>R6 — HIS LEGS. Speed-primary with Hustle riding inside it (never its own
    /// channel; keeping both would pay a fast, high-effort team twice, the S86
    /// double-count). Symmetric about 50: an average-effort man returns exactly 1.0.</summary>
    public static double LegsFactor(double speed, double hustle, MatchupConfig cfg)
    {
        var effort = cfg.TransitionEffortSpeedShare * speed
                   + (1.0 - cfg.TransitionEffortSpeedShare) * hustle;
        return 1.0 + cfg.TransitionLegsSpan * Clamp((effort - 50.0) / 49.0);
    }

    /// <summary>R2 — HOW DEEP THE MAN HE IS GUARDING STARTS, measured against that man's OWN
    /// lineup. Above 1.0 when he is on a perimeter man, below 1.0 when he is on their post.
    /// Read off the OPPOSING lineup, never off the defender's own body: reading it off his
    /// own size would be a scalar wearing a costume, and would leave a big stranded deep
    /// against a team with no post to be stuck guarding.</summary>
    public static double DepthFactor(double oppPostness, double oppMeanPostness, MatchupConfig cfg)
    {
        var o = 0.5 - 0.5 * Math.Tanh((oppPostness - oppMeanPostness) / cfg.TransitionPostnessScale);
        return 1.0 + cfg.TransitionDepthSpan * (2.0 * o - 1.0);
    }

    /// <summary>R7 — WHERE THE SHOOTER SHOT FROM, applied to exactly one defender: the one
    /// whose slot number equals the shooter's. A man who just shot at the rim is standing
    /// under it; a man who just shot a three is already at the arc, halfway back. This is why
    /// a stretch big is genuinely better transition defence than a back-to-the-basket big of
    /// identical speed.</summary>
    public static double ShooterZoneMultiplier(ShotLocation zone, MatchupConfig cfg) => zone switch
    {
        ShotLocation.Rim   => cfg.TransitionShooterZoneRim,
        ShotLocation.Short => cfg.TransitionShooterZoneShort,
        ShotLocation.Mid   => cfg.TransitionShooterZoneMid,
        ShotLocation.Long  => cfg.TransitionShooterZoneLong,
        ShotLocation.Three => cfg.TransitionShooterZoneThree,
        _ => throw new InvalidOperationException($"TransitionDefense: unhandled shot zone {zone}."),
    };

    /// <summary>
    /// One man's GOT-BACK number: <c>LuckFloor + legs × depth × zone</c>. Strictly positive
    /// for any legal configuration (all three factors are strictly positive because both
    /// spans are guarded below 1 and every zone multiplier above 0), so a lineup can never
    /// present a zero total weight and the picker needs no all-zero branch.
    /// </summary>
    /// <param name="oppPostness">The post-ness of the man in the SAME-NUMBERED offensive
    /// seat. <b>Null is the empty-seat rule (occupancy matrix row 3):</b> a defender whose
    /// man is not on the floor has nobody to guard, so nothing holds him back — depth is
    /// neutral 1.0, his legs alone carry him, and he stays a candidate in the draw.</param>
    /// <param name="shooterZone">Non-null only for the defender whose slot number equals the
    /// shooter's offensive slot (R7). He is not necessarily the man the picker selects.</param>
    public static double GotBack(
        double speed, double hustle,
        double? oppPostness, double oppMeanPostness,
        ShotLocation? shooterZone,
        MatchupConfig cfg)
    {
        var f = LegsFactor(speed, hustle, cfg)
              * (oppPostness is null ? 1.0 : DepthFactor(oppPostness.Value, oppMeanPostness, cfg));
        if (shooterZone is not null)
            f *= ShooterZoneMultiplier(shooterZone.Value, cfg);
        return cfg.TransitionGotBackLuckFloor + f;
    }

    /// <summary>
    /// Job 3 — HOW MANY GOT BACK. <c>Σ gotBack / (count × ReferenceGotBack)</c> over the
    /// defenders actually ON THE FLOOR. Exactly 1.0 at five average men on an average
    /// offence; above 1.0 when the lineup out-runs average, below when it does not.
    ///
    /// <para><b>Emergent, not a team rating.</b> This is a sum of five individual numbers,
    /// each of which reads one man against one opponent — not a mean of ratings. It is S62's
    /// per-man aggregate idiom, and like S62 the denominator is the OCCUPIED count, so a
    /// short harness lineup is not silently diluted toward the floor. (S62's own summary
    /// comment says "5 ×"; its code has always divided by the occupied count. The code is
    /// the convention.)</para>
    /// </summary>
    public static double TeamAggregate(IReadOnlyList<double> weights, MatchupConfig cfg)
    {
        if (weights.Count == 0)
            throw new InvalidOperationException(
                "TransitionDefense.TeamAggregate: no defenders on the floor — " +
                "the got-back path must not be entered with an empty defensive lineup (matrix row 1).");

        var sum = 0.0;
        for (var i = 0; i < weights.Count; i++) sum += weights[i];
        return sum / (weights.Count * ReferenceGotBack(cfg));
    }

    /// <summary>Job 2 — HOW SET HE IS WHEN HE ARRIVES. 1.0 is a league-average man on a
    /// neutral assignment. Scales his own contest: a man who sprinted back and got there is
    /// closer to a set defender than one who arrived late and gasping.</summary>
    public static double ArrivalQuality(double gotBack, MatchupConfig cfg)
    {
        var reference = ReferenceGotBack(cfg);
        return 1.0 + cfg.TransitionArrivalSpan * Clamp((gotBack - reference) / reference);
    }

    /// <summary>
    /// The break's make rate. Two subtractions off the base: WHO CONTESTED IT (his rim
    /// protection, scaled by how set he arrived, and discounted — R4: a backpedalling man is
    /// not a set defender, and a break stays a better look than a halfcourt set) and HOW MANY
    /// GOT BACK (R5 + job 3, the dominant channel — being there is worth something on its own
    /// even from a man who will never block it).
    /// </summary>
    public static double BreakMakePct(double rimProtection, double gotBack, double aggregate, MatchupConfig cfg)
    {
        var a  = ArrivalQuality(gotBack, cfg);
        var rp = Clamp((rimProtection - 50.0) / 49.0);
        return Math.Clamp(
            cfg.TransitionBaseBreakMake
            - cfg.TransitionRimProtectionSwing * rp * cfg.TransitionContestDiscount * a
            - cfg.TransitionTeamPresenceSwing * (aggregate - 1.0),
            0.0, 1.0);
    }

    /// <summary>
    /// R3 — THE CHASE-DOWN. Length leads, speed pays directly through how well he arrived,
    /// and rim protection is the junior partner. This is the block a rangy fast wing gets in
    /// transition even though his block rating gives him nothing in the halfcourt.
    /// </summary>
    public static double BreakBlockPct(double rimProtection, double length, double gotBack, MatchupConfig cfg)
    {
        var a  = ArrivalQuality(gotBack, cfg);
        var rp = Clamp((rimProtection - 50.0) / 49.0);
        var lg = Clamp((length - 50.0) / 49.0);
        return Math.Max(0.0,
            cfg.TransitionBaseBreakBlock
            + cfg.TransitionChaseSwing * a
              * (cfg.TransitionChaseLengthWeight * lg + cfg.TransitionChaseRimProtWeight * rp)
            + cfg.TransitionChaseSpeedSwing * (a - 1.0));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PLAYER-FACING — the one place the rating mapping lives.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One man's got-back number against the man in his own slot number. A null
    /// <paramref name="opponent"/> is the empty-seat rule (matrix row 3): neutral depth.</summary>
    public static double GotBack(
        Player defender, Player? opponent, double oppMeanPostness,
        ShotLocation? shooterZone, MatchupConfig cfg)
        => GotBack(defender.Speed, defender.Hustle,
                   opponent is null ? null : Matchup.Postness(opponent, cfg),
                   oppMeanPostness, shooterZone, cfg);

    /// <inheritdoc cref="BreakMakePct(double,double,double,MatchupConfig)"/>
    public static double BreakMakePct(Player defender, double gotBack, double aggregate, MatchupConfig cfg)
        => BreakMakePct(defender.RimProtection, gotBack, aggregate, cfg);

    /// <inheritdoc cref="BreakBlockPct(double,double,double,MatchupConfig)"/>
    public static double BreakBlockPct(Player defender, double gotBack, MatchupConfig cfg)
        => BreakBlockPct(defender.RimProtection, Matchup.LengthRating(defender, cfg), gotBack, cfg);

    /// <summary>
    /// The mean post-ness of the men ACTUALLY ON THE FLOOR for the offence — the yardstick
    /// every defender's depth is measured against. Occupied seats only (matrix row 3): an
    /// empty offensive seat is not a zero-post-ness man, it is nobody.
    /// </summary>
    /// <returns>Null when the offence has nobody on the floor — matrix row 2, where the
    /// got-back path is never entered and this value must never be computed.</returns>
    public static double? OpponentMeanPostness(IReadOnlyList<Player?> offence, MatchupConfig cfg)
    {
        var sum = 0.0;
        var count = 0;
        for (var i = 0; i < offence.Count; i++)
        {
            if (offence[i] is null) continue;
            sum += Matchup.Postness(offence[i]!, cfg);
            count++;
        }
        return count == 0 ? null : sum / count;
    }

    /// <summary>
    /// The five got-back weights for a defensive lineup, BUILT BY SLOT NUMBER — never by
    /// enumeration order of a compacted list. Index <c>i</c> is slot <c>i + 1</c> on both
    /// sides, so defensive slot 3 is always paired with offensive slot 3 (the engine's own
    /// same-number assignment convention, as used by <see cref="DefenderPicker"/>).
    ///
    /// <para>Pairing by compacted order instead is identical whenever both lineups are full —
    /// which is every real game and every golden case — and wrong the moment a seat is
    /// empty. That is why the pairing is written against the slot index here rather than a
    /// loop over gathered players.</para>
    /// </summary>
    /// <param name="defenders">Five entries, index <c>i</c> = defensive slot <c>i + 1</c>;
    /// null is an empty seat.</param>
    /// <param name="offence">Five entries, index <c>i</c> = offensive slot <c>i + 1</c>;
    /// null is an empty seat.</param>
    /// <param name="shooterSlotNumber">The shooter's offensive slot number (1–5), for R7.</param>
    /// <param name="shooterZone">The zone the shooter shot from, for R7.</param>
    /// <returns>Five weights; <b>exactly 0.0 marks an empty defensive seat</b> and is
    /// unambiguous because an occupied seat's got-back number is strictly positive.</returns>
    public static double[] LineupGotBack(
        IReadOnlyList<Player?> defenders,
        IReadOnlyList<Player?> offence,
        int? shooterSlotNumber,
        ShotLocation? shooterZone,
        MatchupConfig cfg)
    {
        var oppMean = OpponentMeanPostness(offence, cfg)
            ?? throw new InvalidOperationException(
                "TransitionDefense.LineupGotBack: the offence has nobody on the floor — " +
                "the got-back path must not be entered (matrix row 2).");

        var weights = new double[5];
        for (var n = 1; n <= 5; n++)
        {
            var defender = n - 1 < defenders.Count ? defenders[n - 1] : null;
            if (defender is null) continue;                      // empty defensive seat → 0.0

            var opponent = n - 1 < offence.Count ? offence[n - 1] : null;
            var zone     = (shooterSlotNumber == n) ? shooterZone : null;
            weights[n - 1] = GotBack(defender, opponent, oppMean, zone, cfg);
        }
        return weights;
    }
}
