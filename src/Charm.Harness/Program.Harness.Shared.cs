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
            RimProtection = 50, DefensiveRebounding = 50, Steals = 50, HelpDefense = 50,
            Height = 50, Wingspan = 50, Weight = 50, Strength = 50, Speed = 50,
            Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50,
            Hustle = 50, BasketballIQ = 50, Discipline = 50,
            OffBallDefense = 50,
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
    // Session 33: the two Roll K post-offensive-rebound turnover labels join the
    // classifier. Both flip possession (DeadBallTurnover -> dead-ball inbound to the
    // defense; LiveBallTurnover -> transition steal to the defense) — turnovers by
    // the engine's own contract, previously miscounted as OTHER. Aggregate-only: the
    // records carry no per-player TO metadata, so no individual credit changes.
    private static bool IsTurnoverPossession(PossessionRecord r) =>
        r.EndLabel is "BadPassDeadBall" or "BadPassIntercepted"
            or "LostBallDeadBall" or "LostBallLiveBall" or "OffensiveFoul"
            or "Travel" or "DoubleDribble" or "Carry" or "ThreeSecondViolation"
            or "FiveSecondCloselyGuarded" or "OffensiveGoaltending"
            or "BackcourtViolation" or "ShotClockViolation"
            or "FiveSecondInbound" or "TenSecondBackcourt"
            or "DeadBallTurnover" or "LiveBallTurnover";

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
        HelpDefense         = p.HelpDefense,
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
        OffBallDefense      = p.OffBallDefense,
    };

    /// <summary>Per-player stat totals for one game. Indexed by PlayerId - 1, width
    /// <see cref="RosterShape.PlayerArrayWidth"/> (= MaxPlayerId, 26 at S75's 13-man roster:
    /// home 1–13, away 14–26). A narrower run (a five-per-side bench at PlayerIds 1–10) fills
    /// only the low indices and leaves the rest zero, so the equality/reproducibility check
    /// (SequenceEqual over the full arrays) is unaffected.
    /// S77: the width has always been derived, but ONE consumer guard was still the literal 20
    /// and dropped ids 21–26 — see the note in AttributeGame. Nothing here restates a size.</summary>
    private sealed class PlayerBoxTotals
    {
        public long[] Fga  = new long[RosterShape.PlayerArrayWidth]; public long[] Fgm  = new long[RosterShape.PlayerArrayWidth];
        public long[] Tpa  = new long[RosterShape.PlayerArrayWidth]; public long[] Tpm  = new long[RosterShape.PlayerArrayWidth];
        public long[] Fta  = new long[RosterShape.PlayerArrayWidth]; public long[] Ftm  = new long[RosterShape.PlayerArrayWidth];
        public long[] OReb = new long[RosterShape.PlayerArrayWidth]; public long[] DReb = new long[RosterShape.PlayerArrayWidth];
        public long[] Blk  = new long[RosterShape.PlayerArrayWidth]; public long[] Stl  = new long[RosterShape.PlayerArrayWidth];
        public long[] To   = new long[RosterShape.PlayerArrayWidth];
        // Phase 25: shooting fouls committed (SFL) — weighted draw, separate seed+3 RNG.
        public long[] ShFoul = new long[RosterShape.PlayerArrayWidth];
        // Session 62: non-shooting fouls committed (NSF). S87: no longer a draw — read
        // from the engine-recorded committer.
        public long[] NsFoul = new long[RosterShape.PlayerArrayWidth];
        // S87: OFFENSIVE fouls committed (OFF) — charges and scrum fouls. New column;
        // these reached no foul count at all before this session.
        public long[] OffFoul = new long[RosterShape.PlayerArrayWidth];
        // Phase 39: assist counts — engine-stamped on-walk from AstBySlot.
        public long[] Ast  = new long[RosterShape.PlayerArrayWidth];
        // Session 85, PAGE-ONLY: the fast-break SUBSET of Blk, engine-stamped on-walk from
        // FastBreakBlkBySlot. Filled by the same seat-to-man translation as Blk immediately
        // below it, so FbBlk[i] <= Blk[i] holds for every man by construction rather than by
        // agreement between two independent passes.
        public long[] FbBlk = new long[RosterShape.PlayerArrayWidth];
        public static bool AllEqual(PlayerBoxTotals a, PlayerBoxTotals b) =>
            a.Fga.SequenceEqual(b.Fga)   && a.Fgm.SequenceEqual(b.Fgm) &&
            a.Tpa.SequenceEqual(b.Tpa)   && a.Tpm.SequenceEqual(b.Tpm) &&
            a.Fta.SequenceEqual(b.Fta)   && a.Ftm.SequenceEqual(b.Ftm) &&
            a.OReb.SequenceEqual(b.OReb) && a.DReb.SequenceEqual(b.DReb) &&
            a.Blk.SequenceEqual(b.Blk)   && a.Stl.SequenceEqual(b.Stl) &&
            a.To.SequenceEqual(b.To)     && a.ShFoul.SequenceEqual(b.ShFoul) &&
            a.NsFoul.SequenceEqual(b.NsFoul) &&
            // S87: the new column joins the reproducibility contract, for the same reason
            // S85's did — a per-player array two identical runs never check would drift
            // silently.
            a.OffFoul.SequenceEqual(b.OffFoul) &&
            a.Ast.SequenceEqual(b.Ast) &&
            // Session 85: the new column joins the reproducibility contract. Omitting it would
            // leave a per-player array that two identical runs are never checked to agree on.
            a.FbBlk.SequenceEqual(b.FbBlk);
    }

    /// <summary>Run the full per-game attribution pass. Calling twice with the same
    /// (result, game, seed) must produce AllEqual output — that is the reproducibility contract.</summary>
    private static PlayerBoxTotals AttributeGame(
        GovernorRunResult result, GameState game, int seed, MatchupConfig matchupCfg)
    {
        var t = new PlayerBoxTotals();
        // Phase 36: seed+2 RNG (BLK WeightedDraw) retired — BlockerPicker now runs engine-side.
        // seed+3 (shooting fouls), and Session 62 seed+4 (non-shooting fouls), are the
        // harness-side attribution draws. Distinct streams keep each byte-for-byte stable.
        // S87: the seed+3 / seed+4 post-hoc foul draws are retired — the engine records
        // the committer at the whistle now. Kept unconsumed here would be misleading, so
        // they are gone from this pass entirely; DrawFoulingDefender /
        // DrawNonShootingFouler survive as the Phase 78 same-platform parity reference.
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
                var op = offRoster.PlayerAt(new Slot(r.Offense, slot), r.Number);
                if (op is null) continue;
                // S77: was `oi < 0 || oi >= 20` — the LAST surviving hardcoded 20-ceiling,
                // and the one site S75 missed when the roster went 10 -> 13. It silently
                // dropped FGA/FGM/3PA/3PM/FTA/FTM for stamped ids 21-26 (away acquisition
                // indices 8-13) — 56,714 shot attempts on the S76 stock season, ~11 per road
                // team per game, while the same men's REB/AST/STL/BLK/TO/fouls recorded
                // normally because those ten sites already used the RosterShape guard. Every
                // per-player array is PlayerArrayWidth (= MaxPlayerId) wide; this guard is now
                // the same one its ten siblings use, so a future roster change moves one number.
                if (!RosterShape.IsLegalPlayerId(op.PlayerId)) continue; // guard: unset PlayerId
                var oi = op.PlayerId - 1;
                t.Fga [oi] += GetSlotFga(r, slot); t.Fgm [oi] += GetSlotFgm(r, slot);
                t.Tpa [oi] += r.ThreePaBySlot[slot]; t.Tpm [oi] += r.ThreePmBySlot[slot];
                t.Fta [oi] += r.FtaBySlot[slot];    t.Ftm [oi] += r.FtmBySlot[slot];
            }
            // TO — Phase 34: null TurnoverOffSlot = team violation (no individual credit).
            if (IsTurnoverPossession(r))
            {
                if (r.TurnoverOffSlot is { } toSlot)
                {
                    var top = offRoster.PlayerAt(new Slot(r.Offense, toSlot), r.Number);
                    if (top != null && RosterShape.IsLegalPlayerId(top.PlayerId)) t.To[top.PlayerId - 1]++;
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
                var stlp = defRoster.PlayerAt(new Slot(r.Defense, stlSlot), r.Number);
                if (stlp != null && RosterShape.IsLegalPlayerId(stlp.PlayerId)) t.Stl[stlp.PlayerId - 1]++;
            }
            // DReb — Phase 35: read engine-stamped slot from DefensiveRebounderSlot.
            if (r.EndLabel == "DefensiveRebound")
            {
                var drebSlot = r.DefensiveRebounderSlot
                    ?? throw new InvalidOperationException(
                        "Phase 35: DefensiveRebounderSlot null on a defensive-rebound possession — " +
                        "the engine defensive-rebound pick should stamp every DReb possession. Wiring break.");
                var dp = defRoster.PlayerAt(new Slot(r.Defense, drebSlot), r.Number);
                if (dp != null && RosterShape.IsLegalPlayerId(dp.PlayerId)) t.DReb[dp.PlayerId - 1]++;
            }
            // OReb — Phase 31: read engine-stamped picks from OrbBySlot rather than
            // drawing post-hoc. OrbBySlot.Total == r.OrbWon on every possession
            // (asserted in Phase31RebounderPickerCheck). DReb moved engine-side in Phase 35.
            for (var s = 1; s <= 5; s++)
            {
                var orbCount = r.OrbBySlot[s];
                if (orbCount <= 0) continue;
                var op2 = offRoster.PlayerAt(new Slot(r.Offense, s), r.Number);
                if (op2 != null && RosterShape.IsLegalPlayerId(op2.PlayerId))
                    t.OReb[op2.PlayerId - 1] += orbCount;
            }
            // BLK — Phase 36: read engine-stamped slots from BlkBySlot (BlockerPicker).
            for (var s = 1; s <= 5; s++)
            {
                var blkCount36 = r.BlkBySlot[s];
                var fbBlk85    = r.FastBreakBlkBySlot[s];   // Session 85: the break subset
                if (blkCount36 <= 0) continue;
                var bp = defRoster.PlayerAt(new Slot(r.Defense, s), r.Number);
                if (bp != null && RosterShape.IsLegalPlayerId(bp.PlayerId))
                {
                    t.Blk[bp.PlayerId - 1] += blkCount36;
                    // Session 85: same seat, same man, same guard — so a man's break blocks
                    // can never exceed his blocks, and can never be credited to a different
                    // man than the one the block itself went to. The `continue` above is
                    // safe for this line because FastBreakBlkBySlot is a subset of BlkBySlot:
                    // a seat with no block cannot hold a break block.
                    t.FbBlk[bp.PlayerId - 1] += fbBlk85;
                }
            }
            // AST — Phase 39: read engine-stamped slots from AstBySlot (AssistPicker).
            for (var s = 1; s <= 5; s++)
            {
                var astCount = r.AstBySlot[s];
                if (astCount <= 0) continue;
                var ap = offRoster.PlayerAt(new Slot(r.Offense, s), r.Number);
                if (ap != null && RosterShape.IsLegalPlayerId(ap.PlayerId))
                    t.Ast[ap.PlayerId - 1] += astCount;
            }
            // ── S87: foul attribution is now a READ, not a draw ──────────────────
            // Phase 25 / Session 62 drew the committer here, post-hoc, over a
            // reconstructed lineup. The engine now names him at the whistle — which is
            // what lets a foul have consequences — so this pass reads the recorded man
            // instead. The PlayerId is taken from the event rather than re-resolved
            // through the seat, so the credit survives a later substitution into that
            // same seat. The seed+3 / seed+4 streams are retired from this path; the
            // Draw* helpers below are kept as Phase 78's parity reference.
            if (r.ShootingFouls is { } sfs)
                foreach (var sf in sfs)
                    if (RosterShape.IsLegalPlayerId(sf.CommitterPlayerId)) t.ShFoul[sf.CommitterPlayerId - 1]++;

            if (r.NonShootingFouls is { } nsfs)
                foreach (var nsf in nsfs)
                    if (RosterShape.IsLegalPlayerId(nsf.CommitterPlayerId)) t.NsFoul[nsf.CommitterPlayerId - 1]++;

            // S87: the third ledger. Offensive fouls reached NO foul count before this
            // session — a charge was recorded as a turnover only, and the scrum foul was
            // recorded nowhere at all. They count for the man; they never touch the team.
            if (r.OffensiveFouls is { } offs)
                foreach (var off in offs)
                    if (RosterShape.IsLegalPlayerId(off.CommitterPlayerId)) t.OffFoul[off.CommitterPlayerId - 1]++;
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
        ShotLocation zone, int shooterSlot, int atPossession)
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
        // Phase 52: read the defenders who were on the floor AT this possession, not the
        // roster's final occupants — under substitutions the two differ, and the fouler is
        // selected from the size profile of the five actually defending. Identical to the
        // current occupant on any no-sub game (the starter's log entry wins every lookup).
        var slots = new List<(int Slot, double Interior)>(5);
        for (var s = 1; s <= 5; s++)
        {
            var p = roster.PlayerAt(new Slot(side, s), atPossession);
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

    /// <summary>
    /// Session 62: draw the defending slot that committed a non-shooting foul. Unlike the
    /// shooting-foul draw there is no shooter to anchor a "matched man" — a non-shooting
    /// foul is a property of the defense alone, so all five defenders are candidates,
    /// weighted by their reach-in propensity.
    ///
    /// <para><paramref name="isReachIn"/> selects the weighting. Reach-in fouls (A/B/F) draw
    /// in proportion to each defender's FULL reach-in propensity
    /// (<see cref="Matchup.ReachInPropensity"/>) — Discipline-primary, small athleticism
    /// secondary, slight perimeter lean, orientation taken relative to the lineup mean.
    /// Situational fouls (I/J/K/M) draw on the Discipline factor alone
    /// (<see cref="Matchup.ReachInDisciplineFactor"/>): candidate (b), since the perimeter
    /// lean is meaningless in a rebound scrum or transition bump. Weights are always
    /// positive (propensity ≥ LuckFloor; the Discipline factor ≥ 1 − DiscSpan &gt; 0), so
    /// the draw never degenerates.</para>
    ///
    /// <para>Reads the defenders on the floor AT this possession (post-substitution), the
    /// same as <see cref="DrawFoulingDefender"/>.</para>
    /// </summary>
    private static int DrawNonShootingFouler(
        Random rng, TeamSide side, Roster roster,
        bool isReachIn, int atPossession, MatchupConfig cfg)
    {
        // Gather the five defenders on the floor at this possession.
        var slots = new List<(int Slot, Player P)>(5);
        for (var s = 1; s <= 5; s++)
        {
            var p = roster.PlayerAt(new Slot(side, s), atPossession);
            if (p != null) slots.Add((s, p));
        }
        if (slots.Count == 0)
            throw new InvalidOperationException(
                $"DrawNonShootingFouler: team {side} has no populated slots — cannot attribute non-shooting foul.");

        // Lineup mean postness — denominator for the lineup-relative perimeter orientation.
        var meanPostness = slots.Average(x => Matchup.Postness(x.P, cfg));

        double[] weights = new double[slots.Count];
        for (var i = 0; i < slots.Count; i++)
        {
            var p = slots[i].P;
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

        // ── Cumulative draw (same shape as DrawFoulingDefender / WeightedDraw) ──
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
