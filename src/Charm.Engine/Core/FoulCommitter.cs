namespace Charm.Engine;

/// <summary>
/// WHO COMMITTED THE FOUL. The Session 62 attribution weightings, moved into the engine
/// so the answer is decided at the whistle — which is what lets a foul have consequences
/// — instead of being re-drawn afterwards over a reconstructed lineup.
///
/// <para><b>The math is unchanged.</b> Same zone tables, same interior proxy, same
/// reach-in propensity, same cumulative walk, same one-draw-per-foul cost. S87 moved
/// this code; it did not tune it. The foul DISTRIBUTION keeps its exact character at the
/// moment its consequences turn real.</para>
///
/// <para><b>Why a separate class rather than private resolver methods.</b> Two reasons.
/// The weights are a pure function of the five men and the situation — separable from
/// the draw, and therefore checkable cell by cell against the surviving Session 62
/// reference at exact equality, which is the whole spine of Phase 78's parity check. And
/// the draw is exposed alongside them so the check can drive THIS code rather than a
/// transcription of it: a transcribed reference only ever proves a formula equals
/// itself.</para>
///
/// <para><b>Occupancy is the caller's business.</b> Every entry point takes the OCCUPIED
/// seats only, as two parallel lists — the men, and the slot numbers they sit in. There
/// is no null handling and no sentinel inside: a returned slot is always an occupied seat
/// of the side the caller passed. The degenerate "nobody is on the floor" case never
/// reaches here.</para>
/// </summary>
public static class FoulCommitter
{
    /// <summary>Denominator for the interior-deviation term in the shooting-foul tilt.
    /// Larger means a weaker tilt.
    /// <para><b>Session 62's flagged calibration debt, carried over verbatim.</b> With
    /// the Phase 24 roster (Anchor interior 230, Perimeter 115, mean 138) this value gives
    /// the Anchor about 58% of the rim residual — stronger than the ~37% estimated when
    /// the form was drafted, which assumed a scale nearer 100. That is Session 62's
    /// tuning debt and is deliberately NOT settled here.</para></summary>
    public const double InteriorTiltScale = 40.0;

    /// <summary>The share of blame given to the man guarding the shooter, by zone alone —
    /// he is more likely to be the one who fouled on a jumper he was contesting than on a
    /// drive to the rim where help arrives.</summary>
    public static double MatchedShare(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => 0.50,
        ShotLocation.Short => 0.65,
        ShotLocation.Mid   => 0.70,
        ShotLocation.Long  => 0.80,
        ShotLocation.Three => 0.80,
        _ => throw new InvalidOperationException($"FoulCommitter: unmapped zone '{zone}'.")
    };

    /// <summary>Direction and strength of the size tilt applied to everyone EXCEPT the
    /// matched man. Positive favours the interior (a foul at the rim is the big helping
    /// late); negative favours the perimeter (a foul on a three is a closeout).</summary>
    public static double SignedK(ShotLocation zone) => zone switch
    {
        ShotLocation.Rim   => +0.50,
        ShotLocation.Short => +0.25,
        ShotLocation.Mid   =>  0.00,
        ShotLocation.Long  => -0.25,
        ShotLocation.Three => -0.50,
        _ => throw new InvalidOperationException($"FoulCommitter: unmapped zone '{zone}'.")
    };

    /// <summary>The size proxy the shooting-foul tilt reads: Height + Strength +
    /// PostDefense, unweighted and with no config dependency.</summary>
    public static double InteriorScore(Player p) => p.Height + p.Strength + p.PostDefense;

    /// <summary>
    /// Per-seat weights for a SHOOTING foul, aligned to <paramref name="occupiedSlots"/>.
    ///
    /// <para><paramml name="shooterSlot"/> of 0 — the rare bonus-free-throw putback where
    /// no shooter was ever selected — and a shooter whose matching defensive seat is
    /// empty both route to a FLAT draw. That is an INPUT condition: the man who comes back
    /// is a real man either way.</para>
    /// </summary>
    public static double[] ShootingWeights(
        IReadOnlyList<Player> occupants, IReadOnlyList<int> occupiedSlots,
        ShotLocation zone, int shooterSlot)
    {
        var n = occupants.Count;
        var weights = new double[n];

        var matcherPopulated = false;
        for (var i = 0; i < n; i++) if (occupiedSlots[i] == shooterSlot) matcherPopulated = true;

        if (shooterSlot == 0 || !matcherPopulated)
        {
            for (var i = 0; i < n; i++) weights[i] = 1.0;
            return weights;
        }

        var ms       = MatchedShare(zone);
        var k        = SignedK(zone);
        var residual = 1.0 - ms;

        var interior = new double[n];
        var meanInt  = 0.0;
        for (var i = 0; i < n; i++) { interior[i] = InteriorScore(occupants[i]); meanInt += interior[i]; }
        meanInt /= n;

        var residualIdx = new List<int>(n);
        for (var i = 0; i < n; i++) if (occupiedSlots[i] != shooterSlot) residualIdx.Add(i);

        if (residualIdx.Count == 0)
        {
            // The matched man is the only defender on the floor — all of it is his.
            for (var i = 0; i < n; i++) weights[i] = occupiedSlots[i] == shooterSlot ? 1.0 : 0.0;
            return weights;
        }

        var rawResidual = new double[residualIdx.Count];
        var sumRaw = 0.0;
        for (var r = 0; r < residualIdx.Count; r++)
        {
            rawResidual[r] = Math.Exp(k * (interior[residualIdx[r]] - meanInt) / InteriorTiltScale);
            sumRaw += rawResidual[r];
        }

        for (var i = 0; i < n; i++)
        {
            if (occupiedSlots[i] == shooterSlot) { weights[i] = ms; continue; }
            var ri = residualIdx.IndexOf(i);
            weights[i] = residual * rawResidual[ri] / sumRaw;
        }
        return weights;
    }

    /// <summary>
    /// Per-seat weights for a NON-SHOOTING defensive foul. There is no shooter to anchor
    /// a matched man, so all five defenders are candidates.
    ///
    /// <para><paramref name="isReachIn"/> TRUE is the pre-shot reach-in — discipline
    /// first, a small athleticism term, a slight perimeter lean taken relative to the
    /// lineup's own mean. FALSE is the situational bump in a rebound scrum or in
    /// transition, which draws on discipline alone because the perimeter lean means
    /// nothing there.</para>
    /// </summary>
    public static double[] NonShootingWeights(
        IReadOnlyList<Player> occupants, bool isReachIn, MatchupConfig cfg)
    {
        var n = occupants.Count;
        var weights = new double[n];

        var meanPostness = 0.0;
        for (var i = 0; i < n; i++) meanPostness += Matchup.Postness(occupants[i], cfg);
        meanPostness /= n;

        for (var i = 0; i < n; i++)
        {
            var p = occupants[i];
            if (isReachIn)
            {
                var ath = ((double)p.Quickness + p.FirstStep) / 2.0;
                var o   = Matchup.ReachInPerimOrientation(Matchup.Postness(p, cfg), meanPostness, cfg);
                weights[i] = Matchup.ReachInPropensity(p.Discipline, ath, o, cfg);
            }
            else
            {
                weights[i] = Matchup.ReachInDisciplineFactor(p.Discipline, cfg);
            }
        }
        return weights;
    }

    /// <summary>
    /// Walk a cumulative weight table and return the chosen INDEX. Consumes exactly one
    /// draw. Identical in shape to the Session 62 draws and to the STL/BLK/DReb pickers:
    /// the last candidate is the implicit fallback that absorbs floating-point shortfall,
    /// so the walk always lands on somebody.
    /// </summary>
    public static int CumulativeDraw(double[] weights, IRng rng)
    {
        var total = 0.0;
        foreach (var w in weights) total += w;
        var draw  = rng.NextUnitInterval() * total;
        var cumul = 0.0;
        for (var i = 0; i < weights.Length - 1; i++)
        {
            cumul += weights[i];
            if (draw < cumul) return i;
        }
        return weights.Length - 1;
    }

    /// <summary>The defending SEAT that committed a shooting foul. One draw.</summary>
    public static int PickShootingSlot(
        IReadOnlyList<Player> occupants, IReadOnlyList<int> occupiedSlots,
        ShotLocation zone, int shooterSlot, IRng rng)
        => occupiedSlots[CumulativeDraw(ShootingWeights(occupants, occupiedSlots, zone, shooterSlot), rng)];

    /// <summary>The defending SEAT that committed a non-shooting foul. One draw.</summary>
    public static int PickNonShootingSlot(
        IReadOnlyList<Player> occupants, IReadOnlyList<int> occupiedSlots,
        bool isReachIn, MatchupConfig cfg, IRng rng)
        => occupiedSlots[CumulativeDraw(NonShootingWeights(occupants, isReachIn, cfg), rng)];
}
