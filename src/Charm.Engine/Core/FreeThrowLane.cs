namespace Charm.Engine;

/// <summary>Where a man stands on a missed last free throw.</summary>
public enum LaneSpot
{
    /// <summary>The man who just missed, at the line.</summary>
    Shooter,
    /// <summary>On the lane — two offense, four defense.</summary>
    Lane,
    /// <summary>Back behind the arc — two offense, one defense.</summary>
    Back,
}

/// <summary>One man a free-throw-lane draw can name, and his probability.</summary>
public readonly record struct LaneCandidate(Slot Slot, LaneSpot Spot, double Share);

/// <summary>
/// ★ S120 — THE FREE-THROW LANE (O-118). Who stands where on a missed last free throw, decided once
/// for the whole Roll M resolution and carried on <see cref="PossessionState.FreeThrowLane"/>.
///
/// <para><b>Emmett's rulings (2026-10-09).</b> <i>"In college there are 6 players on the lane. 4 for
/// the defense and two for the shooting team, and then the shooter makes 3. So there is one defender
/// behind the three point line and two offensive players beyond it."</i> Who gets a lane spot is
/// <i>"a combination of their rebounding skills and their size"</i> — the 5'8" point guard who
/// can't rebound stays back. Foul trouble keeps a man off the lane — two fouls in the first half,
/// four with more than about five minutes left — but never in overtime.</para>
///
/// <para><b>The lifetime.</b> Stamped in the resolver's <c>ResolveFTRebound</c> case before Roll M's
/// generator runs; read by every consumer of that one resolution (the board credit and the ticket that
/// names the rebounder, the scramble foul's committer, the man fouled on its bonus trip); cleared when
/// the resolution hands the ball back to live play. A lane alive in a later live-ball board would
/// quietly decide it — Phase 111 C4 counts that at zero.</para>
///
/// <para>Selection takes no randomness and reads only ratings, the personal-foul counts and the
/// clock the <see cref="Governor"/> published at the start of the possession.</para>
/// </summary>
public sealed record FreeThrowLane(
    TeamSide Offense,
    TeamSide Defense,
    Slot? Shooter,
    Slot[] OffenseLane,
    Slot[] OffenseBack,
    Slot[] DefenseLane,
    Slot[] DefenseBack,
    int FoulTroubleMoves)
{
    /// <summary>The ruled lane picture: two offensive men on the lane, four defenders.</summary>
    public const int OffenseLaneSize = 2;
    public const int DefenseLaneSize = 4;

    /// <summary>A man's claim to a lane spot (Emmett: size and rebounding, half each): half the
    /// rebound body (<see cref="Matchup.ReboundPhysical"/> divided by the sum of its three weights,
    /// so it sits on the 0–99 rating scale) and half the side's rebounding rating.</summary>
    public static double LaneScore(Player p, bool offense, MatchupConfig m)
    {
        var w = m.ReboundStrengthWeight + m.ReboundHeightWeight + m.ReboundWingspanWeight;
        var r = offense ? p.OffensiveRebounding : p.DefensiveRebounding;
        return 0.5 * Matchup.ReboundPhysical(p, m) / w + 0.5 * r;
    }

    /// <summary>Foul trouble on the clock the possession started with. No clock: never.</summary>
    public static bool InFoulTrouble(int fouls, PeriodClock? clock, RollMConfig cfg) => clock switch
    {
        null => false,
        { IsFirstHalf: true }  => fouls >= cfg.LaneFoulTroubleFirstHalfFouls,
        { IsSecondHalf: true } => fouls >= cfg.LaneFoulTroubleSecondHalfFouls
                                  && clock.Value.SecondsLeft > cfg.LaneFoulTroubleSecondHalfSeconds,
        _ => false,   // overtime: "in overtime you're going to put them on the lane"
    };

    /// <summary>
    /// Line up both sides for a missed last free throw by <paramref name="shooter"/>.
    /// <para>Each side is ordered by <see cref="LaneScore"/>, best first, men in foul trouble last,
    /// ties by slot number. Offense: the shooter at the line, the first two others on the lane, the
    /// rest back. Defense: the first four on the lane, the fifth back. A side with fewer than five
    /// men puts what it has on the lane first.</para>
    /// <para><b>A missing shooter.</b> With a clock (a <see cref="Governor"/>-driven game) a null
    /// shooter throws — a real game always knows who missed. With no clock (a hand-built state) the
    /// offense's lane is the best two of all five and the shooter's share goes to the lane.</para>
    /// </summary>
    public static FreeThrowLane Build(GameState game, PossessionState state, Slot? shooter,
                                      RollMConfig cfg, MatchupConfig m)
    {
        var clock = game.Clock;
        if (shooter is null && clock is not null)
            throw new InvalidOperationException(
                "FreeThrowLane: a missed last free throw with no shooter in a game with a clock — " +
                "a real game always knows who missed (S120 K6).");
        if (shooter is { } sh && sh.Side != state.Offense)
            throw new InvalidOperationException(
                $"FreeThrowLane: the shooter {sh} is not on the offense ({state.Offense}).");

        var (offLane, offBack, offMoves) = Order(game, state.Offense, offense: true,
                                                 exclude: shooter?.Number ?? 0, OffenseLaneSize, clock, cfg, m);
        var (defLane, defBack, defMoves) = Order(game, state.Defense, offense: false,
                                                 exclude: 0, DefenseLaneSize, clock, cfg, m);
        return new FreeThrowLane(state.Offense, state.Defense, shooter,
                                 offLane, offBack, defLane, defBack, offMoves + defMoves);
    }

    private static (Slot[] Lane, Slot[] Back, int Moves) Order(
        GameState game, TeamSide side, bool offense, int exclude, int laneSize,
        PeriodClock? clock, RollMConfig cfg, MatchupConfig m)
    {
        var lineup = game.LineupFor(side);
        var roster = game.RosterFor(side);
        var men = new List<(Slot Slot, double Score, bool Trouble)>(5);
        for (var n = 1; n <= 5; n++)
        {
            if (n == exclude) continue;
            var slot = lineup.SlotAt(n);
            var p = roster.PlayerAt(slot);
            if (p is null) continue;
            men.Add((slot, LaneScore(p, offense, m),
                     InFoulTrouble(game.PersonalFouls.CountFor(p.PlayerId), clock, cfg)));
        }

        static int ByScore((Slot Slot, double Score, bool Trouble) a, (Slot Slot, double Score, bool Trouble) b)
        {
            var c = b.Score.CompareTo(a.Score);              // best first
            return c != 0 ? c : a.Slot.Number.CompareTo(b.Slot.Number);   // ties by slot
        }

        var ordered = men.ToList();
        ordered.Sort((a, b) =>
        {
            var t = a.Trouble.CompareTo(b.Trouble);          // foul trouble last
            return t != 0 ? t : ByScore(a, b);
        });
        var take = Math.Min(laneSize, ordered.Count);
        var lane = ordered.Take(take).Select(x => x.Slot).ToArray();
        var back = ordered.Skip(take).Select(x => x.Slot).OrderBy(s => s.Number).ToArray();
        lane = lane.OrderBy(s => s.Number).ToArray();

        // Page-only: how many men foul trouble moved off the lane (the order with it ignored).
        var plain = men.ToList();
        plain.Sort(ByScore);
        var plainLane = plain.Take(take).Select(x => x.Slot.Number).ToHashSet();
        var moves = plainLane.Count(n => !lane.Any(s => s.Number == n));
        return (lane, back, moves);
    }

    /// <summary>The lane's totals: rebound body and rebounding, summed over its men.</summary>
    public (double OffBody, double DefBody, double OffReb, double DefReb) Totals(GameState game, MatchupConfig m)
    {
        double ob = 0, db = 0, or = 0, dr = 0;
        foreach (var s in OffenseLane)
        {
            var p = game.RosterFor(Offense).PlayerAt(s)!;
            ob += Matchup.ReboundPhysical(p, m); or += p.OffensiveRebounding;
        }
        foreach (var s in DefenseLane)
        {
            var p = game.RosterFor(Defense).PlayerAt(s)!;
            db += Matchup.ReboundPhysical(p, m); dr += p.DefensiveRebounding;
        }
        return (ob, db, or, dr);
    }

    // ── The split ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The offense's share of the board off a missed last free throw (Emmett: <i>"the natural odds if
    /// everyone is equal, and then it is the competition of the 4 v 2, their size, strength, rebounding
    /// skill"</i>, in totals, laid over the default). Body and rebounding are each compared as the
    /// offense's share of the lane's total, against the normal lane's share; each gap is scaled so two
    /// offensive lane men <see cref="RollMConfig.LaneAnchorPoints"/> better on height, wingspan,
    /// strength and rebounding read as that many points on the live-ball gap scale; then today's
    /// <see cref="Matchup.GapFn"/> weights and tanh bend between the shared floor and ceiling. No
    /// hustle and no leap term.
    /// </summary>
    public static double OffensiveShare(double offBody, double defBody, double offReb, double defReb,
                                        double baseOffShare, RollMConfig cfg, MatchupConfig m)
    {
        var (s0, ks, r0, kr) = Scales(cfg, m);
        var s = offBody / (offBody + defBody);
        var r = offReb / (offReb + defReb);
        var total = m.ReboundSizeWeight  * Matchup.GapFn((s - s0) * ks, m.PhysicalSteepness, m.PhysicalExponent, m.ReferenceScale)
                  + m.ReboundSkillWeight * Matchup.GapFn((r - r0) * kr, m.SkillSteepness,    m.SkillExponent,    m.ReferenceScale);
        var span = total >= 0.0 ? (m.ReboundOffShareCeiling - baseOffShare) : (baseOffShare - m.ReboundOffShareFloor);
        return baseOffShare + span * Math.Tanh(total / m.ReboundReferenceShift);
    }

    /// <summary>The normal lane's two shares and the two anchor scales (S120 K3, K4).</summary>
    public static (double S0, double Ks, double R0, double Kr) Scales(RollMConfig cfg, MatchupConfig m)
    {
        double ob = cfg.LaneNormalOffenseBody, db = cfg.LaneNormalDefenseBody;
        double or = cfg.LaneNormalOffenseRebounding, dr = cfg.LaneNormalDefenseRebounding;
        if (!(ob > 0 && db > 0 && or > 0 && dr > 0))
            throw new InvalidOperationException("RollM: the four normal-lane totals must all be positive.");
        var a = cfg.LaneAnchorPoints;
        var w = m.ReboundStrengthWeight + m.ReboundHeightWeight + m.ReboundWingspanWeight;
        var s0 = ob / (ob + db);
        var r0 = or / (or + dr);
        var obUp = ob + OffenseLaneSize * a * w;
        var orUp = or + OffenseLaneSize * a;
        var ks = a / (obUp / (obUp + db) - s0);
        var kr = a / (orUp / (orUp + dr) - r0);
        return (s0, ks, r0, kr);
    }

    // ── Who gets it ─────────────────────────────────────────────────────────────────────

    /// <summary>The offensive board: the shooter at his rare share, each man back at his, the rest to
    /// the lane men by the offensive rebounder weight over the lane only.</summary>
    public LaneCandidate[] OffensiveBoard(PossessionState state, GameState game, MatchupConfig m, RollMConfig cfg)
    {
        var w = OffenseLane.Length == 0 ? Array.Empty<double>() : LaneWeights(OffenseLane,
            OffensiveRebounderPicker.Weights(state, game, m, atTheLine: null, onlySlots: Numbers(OffenseLane)));
        return Compose(Shooter, cfg.LaneShooterBoardShare, OffenseBack, cfg.LaneOffenseBackBoardShare,
                       OffenseLane, w);
    }

    /// <summary>The defensive board: the man back at his rare share, the rest to the four on the lane
    /// by the defensive rebounder weight over the lane only.</summary>
    public LaneCandidate[] DefensiveBoard(PossessionState state, GameState game, MatchupConfig m, RollMConfig cfg)
    {
        var w = DefenseLane.Length == 0 ? Array.Empty<double>() : LaneWeights(DefenseLane,
            DefensiveRebounderPicker.Weights(state, game, m, onlySlots: Numbers(DefenseLane)));
        return Compose(null, 0.0, DefenseBack, cfg.LaneDefenseBackBoardShare, DefenseLane, w);
    }

    /// <summary>The man fouled on a loose ball off the miss (the defense's foul): the shooter at the
    /// scramble share, the men back never, the lane men by the offensive rebounder weight.</summary>
    public LaneCandidate[] ScrambleFouled(PossessionState state, GameState game, MatchupConfig m, RollMConfig cfg)
    {
        var w = OffenseLane.Length == 0 ? Array.Empty<double>() : LaneWeights(OffenseLane,
            OffensiveRebounderPicker.Weights(state, game, m, atTheLine: null, onlySlots: Numbers(OffenseLane)));
        return Compose(Shooter, cfg.LaneShooterScrambleShare, OffenseBack, 0.0, OffenseLane, w);
    }

    /// <summary>The offensive man who commits a loose-ball foul off the miss: the shooter at the
    /// scramble share, the men back never, the lane men by the interior weight over the lane.</summary>
    public LaneCandidate[] ScrambleOffensiveFouler(PossessionState state, GameState game, MatchupConfig m, RollMConfig cfg)
    {
        var w = OffenseLane.Length == 0 ? Array.Empty<double>() : LaneWeights(OffenseLane,
            TurnoverInteriorPicker.Weights(state, game, m, onlySlots: Numbers(OffenseLane)));
        return Compose(Shooter, cfg.LaneShooterScrambleShare, OffenseBack, 0.0, OffenseLane, w);
    }

    /// <summary>The defender charged with a loose-ball foul off the miss: a lane man, by the
    /// situational non-shooting weight over the four on the lane. The man back never.</summary>
    public LaneCandidate[] ScrambleDefensiveFouler(GameState game, MatchupConfig m)
    {
        var roster = game.RosterFor(Defense);
        var men = DefenseLane.Select(s => roster.PlayerAt(s)!).ToList();
        var w = men.Count == 0 ? Array.Empty<double>() : FoulCommitter.NonShootingWeights(men, isReachIn: false, m);
        return Compose(null, 0.0, DefenseBack, 0.0, DefenseLane, w);
    }

    /// <summary>One draw over a candidate list, in its order: the first candidate whose cumulative
    /// share exceeds the draw (strictly, so a zero share is never named); the last candidate with a
    /// positive share absorbs any floating-point shortfall.</summary>
    public static Slot Draw(LaneCandidate[] candidates, IRng rng)
    {
        var u = rng.NextUnitInterval();
        var cumul = 0.0;
        Slot? last = null;
        foreach (var c in candidates)
        {
            if (c.Share <= 0.0) continue;
            last = c.Slot;
            cumul += c.Share;
            if (u < cumul) return c.Slot;
        }
        return last ?? throw new InvalidOperationException("FreeThrowLane: a draw with no candidate.");
    }

    private static HashSet<int> Numbers(Slot[] slots) => slots.Select(s => s.Number).ToHashSet();

    private static double[] LaneWeights(Slot[] lane, (double[] W, bool[] Pop) weights)
    {
        var (w, pop) = weights;
        var o = new double[lane.Length];
        for (var i = 0; i < lane.Length; i++)
        {
            var k = lane[i].Number - 1;
            if (!pop[k]) throw new InvalidOperationException($"FreeThrowLane: lane man {lane[i]} has no weight.");
            o[i] = w[k];
        }
        return o;
    }

    /// <summary>The shooter and the men back at their fixed shares; the lane men split the rest by
    /// their weights. In candidate order: shooter, back (by slot), lane (by slot). An empty lane
    /// (a side with nobody else on the floor) leaves the rare men to share everything.</summary>
    private static LaneCandidate[] Compose(Slot? shooter, double shooterShare, Slot[] back, double backShare,
                                           Slot[] lane, double[] laneWeights)
    {
        var list = new List<LaneCandidate>(6);
        var rare = 0.0;
        if (shooter is { } s) { list.Add(new LaneCandidate(s, LaneSpot.Shooter, shooterShare)); rare += shooterShare; }
        foreach (var b in back) { list.Add(new LaneCandidate(b, LaneSpot.Back, backShare)); rare += backShare; }
        var wsum = laneWeights.Sum();
        if (lane.Length > 0 && wsum > 0.0)
        {
            var mass = 1.0 - rare;
            for (var i = 0; i < lane.Length; i++)
                list.Add(new LaneCandidate(lane[i], LaneSpot.Lane, mass * laneWeights[i] / wsum));
            return list.ToArray();
        }
        if (rare <= 0.0)
            throw new InvalidOperationException("FreeThrowLane: nobody on the floor to name.");
        return list.Select(c => c with { Share = c.Share / rare }).ToArray();
    }
}

/// <summary>
/// ★ S120, PAGE-ONLY — one missed last free throw as the lane saw it: the lane's totals, the split
/// Roll M rolled, and where the board and any scramble foul went. One per Roll M resolution, carried
/// out on <see cref="RoutingOutcome.FreeThrowLanes"/> and <see cref="PossessionRecord.FreeThrowLanes"/>.
/// Filled by the resolver as the resolution's consumers run; read by nothing in the engine (Phase 111's
/// season page and its game-by-game proof read it).
/// </summary>
public sealed class FreeThrowLaneObservation
{
    public TeamSide Offense { get; init; }
    public bool HadClock { get; init; }
    public bool ShooterMissing { get; init; }
    /// <summary>The lane totals: rebound body and rebounding over the two offensive and four
    /// defensive lane men.</summary>
    public double OffBody { get; init; }
    public double DefBody { get; init; }
    public double OffRebounding { get; init; }
    public double DefRebounding { get; init; }
    /// <summary>Men moved off the lane by foul trouble, both sides.</summary>
    public int FoulTroubleMoves { get; init; }
    /// <summary>Roll M's two board slices as rolled (they sum to the board mass).</summary>
    public double OffensiveBoardWeight { get; init; }
    public double DefensiveBoardWeight { get; init; }
    /// <summary>Roll M's arm.</summary>
    public string Outcome { get; init; } = "";
    /// <summary>Set when a board was credited: which side, where the man stood, and his rank by
    /// rebounding among his side's men on the floor (1 = best).</summary>
    public bool? BoardToOffense { get; set; }
    public LaneSpot? BoardSpot { get; set; }
    public int BoardReboundingRank { get; set; }
    /// <summary>Set on a scramble foul: where the man fouled (a bonus trip), the offensive committer,
    /// or the defensive committer stood.</summary>
    public LaneSpot? ScrambleFouledSpot { get; set; }
    public LaneSpot? ScrambleOffensiveFoulerSpot { get; set; }
    public LaneSpot? ScrambleDefensiveFoulerSpot { get; set; }
}
