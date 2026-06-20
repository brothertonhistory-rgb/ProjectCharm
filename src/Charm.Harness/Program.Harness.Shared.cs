using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    private static TurnoverOutcome MapTurnover(string reason) => reason switch
    {
        "BadPassDeadBall" => TurnoverOutcome.BadPassDeadBall,
        "BadPassIntercepted" => TurnoverOutcome.BadPassIntercepted,
        "LostBallDeadBall" => TurnoverOutcome.LostBallDeadBall,
        "LostBallLiveBall" => TurnoverOutcome.LostBallLiveBall,
        "OffensiveFoul" => TurnoverOutcome.OffensiveFoul,
        "Travel" => TurnoverOutcome.Travel,
        "DoubleDribble" => TurnoverOutcome.DoubleDribble,
        "Carry" => TurnoverOutcome.Carry,
        "ThreeSecondViolation" => TurnoverOutcome.ThreeSecondViolation,
        "FiveSecondCloselyGuarded" => TurnoverOutcome.FiveSecondCloselyGuarded,
        "OffensiveGoaltending" => TurnoverOutcome.OffensiveGoaltending,
        "BackcourtViolation" => TurnoverOutcome.BackcourtViolation,
        "ShotClockViolation" => TurnoverOutcome.ShotClockViolation,
        "FiveSecondInbound" => TurnoverOutcome.FiveSecondInbound,
        "TenSecondBackcourt" => TurnoverOutcome.TenSecondBackcourt,
        _ => throw new InvalidOperationException($"Unmapped Roll C reason '{reason}'.")
    };

    private static TeamSide Other(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;

    // -----------------------------------------------------------------
    // Phase 9 helper: seat starting fives from config into a GameState.
    // Must be called after the GameState is constructed but before any
    // generator that reads PlayerAt is used — otherwise those generators
    // silently fall back to their stub pies and the matchup machinery
    // never runs. Mirrors the seating loop in Phase1RosterCheck.
    // -----------------------------------------------------------------
    // Phase 31: seeds a bare GameState with five identical all-50 players per side
    // so OffensiveRebounderPicker has a non-empty roster to draw from. Called by
    // harness checks that create a fresh GameState for routing verification and
    // don't care about specific attribute values. SetStarter throws on already-
    // occupied slots, so only call this on a freshly constructed GameState.
    private static void SeedMinimalRoster(GameState g)
    {
        static Player Mk50(int id) => new Player($"min{id}")
        {
            PlayerId = id,
            Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 50,
            FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
            SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
            OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50,
            RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
            Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
            Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50,
            Hustle = 50, BasketballIQ = 50, Discipline = 50,
            RimTendency = 50, ShortTendency = 50, MidTendency = 50,
            LongTendency = 50, ThreeTendency = 50,
        };
        for (var i = 0; i < 5; i++)
        {
            g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), Mk50(i + 1));
            g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), Mk50(i + 6));
        }
    }

    private static void SeatStartersFromConfig(GameState game, string configPath)
    {
        var rosterCfg = RosterConfig.Load(configPath);
        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var lineup  = game.LineupFor(side);
            var roster  = game.RosterFor(side);
            var configs = side == TeamSide.Home ? rosterCfg.Home : rosterCfg.Away;
            for (var i = 0; i < Lineup.Size; i++)
                roster.SetStarter(lineup.SlotAt(i + 1), configs[i].ToPlayer());
        }
    }


    // ── Phase 23 static helpers ──────────────────────────────────────────────

    private static int GetSlotFga(PossessionRecord r, int slot) => slot switch
    {
        1 => r.Slot1Fga, 2 => r.Slot2Fga, 3 => r.Slot3Fga,
        4 => r.Slot4Fga, 5 => r.Slot5Fga, _ => 0
    };
    private static int GetSlotFgm(PossessionRecord r, int slot) => slot switch
    {
        1 => r.Slot1Fgm, 2 => r.Slot2Fgm, 3 => r.Slot3Fgm,
        4 => r.Slot4Fgm, 5 => r.Slot5Fgm, _ => 0
    };
    private static bool IsTurnoverPossession(PossessionRecord r) =>
        r.EndLabel is "BadPassDeadBall" or "BadPassIntercepted"
            or "LostBallDeadBall" or "LostBallLiveBall" or "OffensiveFoul"
            or "Travel" or "DoubleDribble" or "Carry" or "ThreeSecondViolation"
            or "FiveSecondCloselyGuarded" or "OffensiveGoaltending"
            or "BackcourtViolation" or "ShotClockViolation"
            or "FiveSecondInbound" or "TenSecondBackcourt";

    private static int BoxIdx(TeamSide side, int slot) =>
        side == TeamSide.Home ? slot - 1 : slot + 4;

    /// <summary>Return a new Player identical to <paramref name="p"/> but with
    /// <see cref="Player.PlayerId"/> set to <paramref name="id"/>. Player is a
    /// sealed class (not a record), so 'with' is unavailable — copy all authored
    /// attributes explicitly via init-setters.</summary>
    private static Player StampPlayerId(Player p, int id) => new Player(p.Name)
    {
        PlayerId            = id,
        HierarchyRank       = p.HierarchyRank,
        Close               = p.Close,
        Mid                 = p.Mid,
        Outside             = p.Outside,
        Finishing           = p.Finishing,
        FreeThrow           = p.FreeThrow,
        FoulDrawing         = p.FoulDrawing,
        RimTendency         = p.RimTendency,
        ShortTendency       = p.ShortTendency,
        MidTendency         = p.MidTendency,
        LongTendency        = p.LongTendency,
        ThreeTendency       = p.ThreeTendency,
        BallHandling        = p.BallHandling,
        Passing             = p.Passing,
        Playmaking          = p.Playmaking,
        SelfCreation        = p.SelfCreation,
        PostMoves           = p.PostMoves,
        OffBallMovement     = p.OffBallMovement,
        Screening           = p.Screening,
        OffensiveRebounding = p.OffensiveRebounding,
        PerimeterDefense    = p.PerimeterDefense,
        PostDefense         = p.PostDefense,
        RimProtection       = p.RimProtection,
        DefensiveRebounding = p.DefensiveRebounding,
        Steals              = p.Steals,
        Height              = p.Height,
        Wingspan            = p.Wingspan,
        Weight              = p.Weight,
        Strength            = p.Strength,
        Speed               = p.Speed,
        Quickness           = p.Quickness,
        FirstStep           = p.FirstStep,
        Vertical            = p.Vertical,
        Endurance           = p.Endurance,
        Hustle              = p.Hustle,
        BasketballIQ        = p.BasketballIQ,
        Discipline          = p.Discipline,
    };

    /// <summary>Per-player stat totals for one game. Indexed by PlayerId - 1 (0–9).</summary>
    private sealed class PlayerBoxTotals
    {
        public long[] Fga  = new long[10]; public long[] Fgm  = new long[10];
        public long[] Tpa  = new long[10]; public long[] Tpm  = new long[10];
        public long[] Fta  = new long[10]; public long[] Ftm  = new long[10];
        public long[] OReb = new long[10]; public long[] DReb = new long[10];
        public long[] Blk  = new long[10]; public long[] Stl  = new long[10];
        public long[] To   = new long[10];
        // Phase 25: shooting fouls committed (SFL) — weighted draw, separate seed+3 RNG.
        public long[] ShFoul = new long[10];
        // Phase 39: assist counts — engine-stamped on-walk from AstBySlot.
        public long[] Ast  = new long[10];
        public static bool AllEqual(PlayerBoxTotals a, PlayerBoxTotals b) =>
            a.Fga.SequenceEqual(b.Fga)   && a.Fgm.SequenceEqual(b.Fgm) &&
            a.Tpa.SequenceEqual(b.Tpa)   && a.Tpm.SequenceEqual(b.Tpm) &&
            a.Fta.SequenceEqual(b.Fta)   && a.Ftm.SequenceEqual(b.Ftm) &&
            a.OReb.SequenceEqual(b.OReb) && a.DReb.SequenceEqual(b.DReb) &&
            a.Blk.SequenceEqual(b.Blk)   && a.Stl.SequenceEqual(b.Stl) &&
            a.To.SequenceEqual(b.To)     && a.ShFoul.SequenceEqual(b.ShFoul) &&
            a.Ast.SequenceEqual(b.Ast);
    }

    /// <summary>Run the full per-game attribution pass. Calling twice with the same
    /// (result, game, seed) must produce AllEqual output — that is the reproducibility contract.</summary>
    private static PlayerBoxTotals AttributeGame(
        GovernorRunResult result, GameState game, int seed)
    {
        var t = new PlayerBoxTotals();
        // Phase 36: seed+2 RNG (BLK WeightedDraw) retired — BlockerPicker now runs engine-side.
        // seed+3 (shooting fouls) is the only remaining harness-side attribution draw.
        var foulRng = new Random(seed + 3);
        var homeRoster = game.RosterFor(TeamSide.Home);
        var awayRoster = game.RosterFor(TeamSide.Away);
        Roster RosterFor(TeamSide s) => s == TeamSide.Home ? homeRoster : awayRoster;

        foreach (var r in result.Possessions)
        {
            var offRoster = RosterFor(r.Offense);
            var defRoster = RosterFor(r.Defense);

            // Exact per-slot stats (offense side)
            for (var slot = 1; slot <= 5; slot++)
            {
                var op = offRoster.PlayerAt(new Slot(r.Offense, slot));
                if (op is null) continue;
                var oi = op.PlayerId - 1;
                if (oi < 0 || oi >= 10) continue; // guard: unset PlayerId
                t.Fga [oi] += GetSlotFga(r, slot); t.Fgm [oi] += GetSlotFgm(r, slot);
                t.Tpa [oi] += r.ThreePaBySlot[slot]; t.Tpm [oi] += r.ThreePmBySlot[slot];
                t.Fta [oi] += r.FtaBySlot[slot];    t.Ftm [oi] += r.FtmBySlot[slot];
            }
            // TO — Phase 34: null TurnoverOffSlot = team violation (no individual credit).
            if (IsTurnoverPossession(r))
            {
                if (r.TurnoverOffSlot is { } toSlot)
                {
                    var top = offRoster.PlayerAt(new Slot(r.Offense, toSlot));
                    if (top != null && top.PlayerId >= 1 && top.PlayerId <= 10) t.To[top.PlayerId - 1]++;
                }
                // else: team violation (FiveSecondInbound / TenSecondBackcourt / ShotClockViolation)
                // — no individual credit; team TO count tracked at aggregate level only.
            }
            // STL — Phase 34: read engine-stamped stealer from StealerSlot.
            if (r.TurnoverWasLiveBall)
            {
                var stlSlot = r.StealerSlot
                    ?? throw new InvalidOperationException(
                        "Phase 34: StealerSlot null on a live-ball turnover — the engine stealer " +
                        "pick should stamp every live-ball possession. Wiring break.");
                var stlp = defRoster.PlayerAt(new Slot(r.Defense, stlSlot));
                if (stlp != null && stlp.PlayerId >= 1 && stlp.PlayerId <= 10) t.Stl[stlp.PlayerId - 1]++;
            }
            // DReb — Phase 35: read engine-stamped slot from DefensiveRebounderSlot.
            if (r.EndLabel == "DefensiveRebound")
            {
                var drebSlot = r.DefensiveRebounderSlot
                    ?? throw new InvalidOperationException(
                        "Phase 35: DefensiveRebounderSlot null on a defensive-rebound possession — " +
                        "the engine defensive-rebound pick should stamp every DReb possession. Wiring break.");
                var dp = defRoster.PlayerAt(new Slot(r.Defense, drebSlot));
                if (dp != null && dp.PlayerId >= 1 && dp.PlayerId <= 10) t.DReb[dp.PlayerId - 1]++;
            }
            // OReb — Phase 31: read engine-stamped picks from OrbBySlot rather than
            // drawing post-hoc. OrbBySlot.Total == r.OrbWon on every possession
            // (asserted in Phase31RebounderPickerCheck). DReb moved engine-side in Phase 35.
            for (var s = 1; s <= 5; s++)
            {
                var orbCount = r.OrbBySlot[s];
                if (orbCount <= 0) continue;
                var op2 = offRoster.PlayerAt(new Slot(r.Offense, s));
                if (op2 != null && op2.PlayerId >= 1 && op2.PlayerId <= 10)
                    t.OReb[op2.PlayerId - 1] += orbCount;
            }
            // BLK — Phase 36: read engine-stamped slots from BlkBySlot (BlockerPicker).
            for (var s = 1; s <= 5; s++)
            {
                var blkCount36 = r.BlkBySlot[s];
                if (blkCount36 <= 0) continue;
                var bp = defRoster.PlayerAt(new Slot(r.Defense, s));
                if (bp != null && bp.PlayerId >= 1 && bp.PlayerId <= 10)
                    t.Blk[bp.PlayerId - 1] += blkCount36;
            }
            // AST — Phase 39: read engine-stamped slots from AstBySlot (AssistPicker).
            for (var s = 1; s <= 5; s++)
            {
                var astCount = r.AstBySlot[s];
                if (astCount <= 0) continue;
                var ap = offRoster.PlayerAt(new Slot(r.Offense, s));
                if (ap != null && ap.PlayerId >= 1 && ap.PlayerId <= 10)
                    t.Ast[ap.PlayerId - 1] += astCount;
            }
            // Phase 25: shooting-foul attribution. seed+3 RNG (foulRng). seed+2 stream
            // (BLK WeightedDraw) retired in Phase 36 — BlockerPicker now runs engine-side.
            // OReb moved to engine-stamped in Phase 31; TO committer moved in Phase 33;
            // STL moved in Phase 34; DReb moved in Phase 35; BLK moved in Phase 36.
            if (r.ShootingFouls is { } sfs)
                foreach (var sf in sfs)
                {
                    var fSlot = DrawFoulingDefender(foulRng, r.Defense, defRoster, sf.Zone, sf.ShooterSlot);
                    var fp = defRoster.PlayerAt(new Slot(r.Defense, fSlot));
                    if (fp != null && fp.PlayerId >= 1 && fp.PlayerId <= 10) t.ShFoul[fp.PlayerId - 1]++;
                }
        }
        return t;
    }

    /// <summary>
    /// Draw the defending slot that committed a shooting foul, given the shot zone and
    /// the shooter's slot number. Returns a slot 1–5 for the defending team.
    ///
    /// <para>Logic: the defender at the same slot index as the shooter (the "matched man")
    /// gets a fixed share of the probability determined by zone alone. The remaining
    /// probability is spread across the other four defenders with an interior-ness tilt
    /// whose direction flips by zone — rim fouls favor the interior big helping late,
    /// three-point fouls favor the perimeter defenders closing out or switching.</para>
    ///
    /// <para>Interior proxy: <c>Height + Strength + PostDefense</c> (unweighted, no
    /// MatchupConfig dependency). The exponential form is the same shape as the existing
    /// STL/BLK/DReb weighted draws but with a signed coefficient.</para>
    ///
    /// <para>Placeholders (calibration targets — wire the form, tune in a later session):
    /// matched-share table, signedK table, SCALE = 40.0.</para>
    /// </summary>
    private static int DrawFoulingDefender(
        Random rng, TeamSide side, Roster roster,
        ShotLocation zone, int shooterSlot)
    {
        // ── Zone lookup tables (CALIBRATION PLACEHOLDERS) ────────────────────
        // matchedShare: fraction of probability given to the defender at the same slot
        //   index as the shooter. Fixed by zone regardless of shooter's interior-ness.
        // signedK: controls direction and strength of the interior tilt on the residual.
        //   Positive = favor interior (rim); negative = favor perimeter (three).
        // SCALE: denominator for the interior-deviation term. Larger → weaker tilt.
        // NOTE: with the Phase 24 roster (Anchor interior=230, Perim interior=115,
        // meanInt=138), SCALE=40 gives the Anchor ~58% of the rim residual — stronger
        // than the ~37% estimated at draft time (which assumed SCALE≈100). Flagged for
        // calibration; wire-the-form session does not tune these values.
        static double MatchedShare(ShotLocation z) => z switch
        {
            ShotLocation.Rim   => 0.50,
            ShotLocation.Short => 0.65,
            ShotLocation.Mid   => 0.70,
            ShotLocation.Long  => 0.80,
            ShotLocation.Three => 0.80,
            _ => throw new InvalidOperationException($"DrawFoulingDefender: unmapped zone '{z}'.")
        };
        static double SignedK(ShotLocation z) => z switch
        {
            ShotLocation.Rim   => +0.50,
            ShotLocation.Short => +0.25,
            ShotLocation.Mid   =>  0.00,
            ShotLocation.Long  => -0.25,
            ShotLocation.Three => -0.50,
            _ => throw new InvalidOperationException($"DrawFoulingDefender: unmapped zone '{z}'.")
        };
        const double Scale = 40.0;

        // ── Populate the five defending slots ────────────────────────────────
        // Gather (slot index, interior score) for every populated slot.
        var slots = new List<(int Slot, double Interior)>(5);
        for (var s = 1; s <= 5; s++)
        {
            var p = roster.PlayerAt(new Slot(side, s));
            if (p != null)
                slots.Add((s, p.Height + p.Strength + p.PostDefense));
        }

        if (slots.Count == 0)
            throw new InvalidOperationException(
                $"DrawFoulingDefender: team {side} has no populated slots — cannot attribute shooting foul.");

        // ── Fail-soft fallback: shooterSlot == 0 (bonus-FT putback, Roll E never ─
        // ran) or its defending slot is unpopulated. Draw flat over all populated
        // defenders — attribution must never crash a completed game.
        bool matcherPopulated = shooterSlot >= 1 && shooterSlot <= 5
            && slots.Any(x => x.Slot == shooterSlot);

        double[] weights = new double[slots.Count];
        if (shooterSlot == 0 || !matcherPopulated)
        {
            // Flat fallback.
            for (var i = 0; i < slots.Count; i++) weights[i] = 1.0;
        }
        else
        {
            // ── Normal path: matched man + interior-tilt residual ────────────
            var ms       = MatchedShare(zone);
            var k        = SignedK(zone);
            var residual = 1.0 - ms;

            // Mean interior-ness over all populated slots (denominator for deviation).
            var meanInt = slots.Average(x => x.Interior);

            // Residual slots = everyone except the matched man.
            var residualSlots = slots.Where(x => x.Slot != shooterSlot).ToList();

            if (residualSlots.Count == 0)
            {
                // Edge: matched man is the only populated defender — give them 100%.
                for (var i = 0; i < slots.Count; i++)
                    weights[i] = slots[i].Slot == shooterSlot ? 1.0 : 0.0;
            }
            else
            {
                // Compute raw exponential weights for residual defenders.
                var rawResidual = residualSlots
                    .Select(x => Math.Exp(k * (x.Interior - meanInt) / Scale))
                    .ToArray();
                var sumRaw = rawResidual.Sum();

                for (var i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Slot == shooterSlot)
                    {
                        weights[i] = ms;
                    }
                    else
                    {
                        var ri = residualSlots.FindIndex(x => x.Slot == slots[i].Slot);
                        weights[i] = residual * rawResidual[ri] / sumRaw;
                    }
                }
            }
        }

        // ── Cumulative draw (same shape as WeightedDraw) ─────────────────────
        var total = weights.Sum();
        var draw  = rng.NextDouble() * total;
        var cumul = 0.0;
        for (var i = 0; i < slots.Count - 1; i++)
        {
            cumul += weights[i];
            if (draw < cumul) return slots[i].Slot;
        }
        return slots[slots.Count - 1].Slot;
    }

}
