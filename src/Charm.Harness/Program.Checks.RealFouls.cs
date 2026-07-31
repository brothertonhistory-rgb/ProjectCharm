using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    /// <summary>
    /// Phase 78 (Session 87) — REAL FOULS: every whistle names a man, five and he's done.
    ///
    /// <para><b>What this phase is for.</b> S87 moved committer selection out of a post-hoc
    /// harness pass and into the engine at whistle time, added the third foul ledger
    /// (offensive fouls, which previously reached no foul count at all), and made a fifth
    /// personal foul disqualify a player. No basketball TARGET is asserted here — foul-outs
    /// per game is a page finding, not a dial, per the page-only calibration principle.
    /// What is asserted is that the moved math is the SAME math, that every recorded
    /// committer was really on the floor, that the three ledgers reconcile against the team
    /// counter, and that the disqualification rule and its escape hatch behave as ruled.</para>
    ///
    /// <para><b>Why parity is a sequence comparison, not a transcription.</b> A1a drives the
    /// engine's committer unit and Session 62's surviving <c>DrawFoulingDefender</c> /
    /// <c>DrawNonShootingFouler</c> from identically-seeded streams and compares the chosen
    /// seat draw for draw. Both consume exactly one uniform per call, and <c>SystemRng</c>
    /// is <c>System.Random</c>, so identical weights force an identical SEQUENCE and any
    /// divergence anywhere in the weight table shows up as a mismatched seat. A check that
    /// re-implemented the weights would only prove a formula equals itself. This is a
    /// SAME-PLATFORM comparison — both sides run in the same process on the same machine —
    /// so exact equality is a bar that holds, unlike the cross-platform fixture that went
    /// red in S81.3.</para>
    ///
    /// <para><b>A3 reads a reset-proof observable.</b> The team-foul tracker resets at the
    /// half, so a final-minus-initial read would silently lose the entire first half. The
    /// reconciliation accumulates POSITIVE deltas across every possession boundary instead:
    /// the halftime reset appears as one negative delta and is ignored, and the sum equals
    /// total increments regardless of how the reset and the callbacks are ordered. That
    /// means no Governor ordering has to be verified to trust the number.</para>
    /// </summary>
    private static bool Phase78RealFoulsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 78 — real fouls (committer at the whistle, five and out) ==");

        var ok = true;
        var cfgD  = RollDConfig.Load(configPath);
        var cfgM  = MatchupConfig.Load(configPath);
        var cfgF  = FatigueConfig.Load(configPath);

        void Check(string label, bool pass, string? why = null)
        {
            Console.WriteLine($"    {(pass ? "ok  " : "FAIL")} {label}"
                              + (why is null ? "" : $"  ({why})"));
            ok &= pass;
        }

        // ── Bench ─────────────────────────────────────────────────────────────────────
        // Ratings vary on exactly the axes the two weightings read: Height/Strength/
        // PostDefense drive the shooting tilt, Discipline/Quickness/FirstStep drive the
        // reach-in propensity. Everything else is flat, so a divergence is attributable.
        static Player Mk(int id, int h, int str, int postD, int disc, int quick, int first) =>
            new Player($"s87_{id}")
            {
                PlayerId = id,
                Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 50, FoulDrawing = 50,
                BallHandling = 50, Passing = 50, Playmaking = 50, SelfCreation = 50, PostMoves = 50,
                OffBallMovement = 50, Screening = 50, OffensiveRebounding = 50, PerimeterDefense = 50,
                PostDefense = postD, RimProtection = 50, DefensiveRebounding = 50, Steals = 50,
                HelpDefense = 50, OffBallDefense = 50, Height = h, Wingspan = 50, Weight = 50,
                Strength = str, Speed = 50, Quickness = quick, FirstStep = first, Vertical = 50,
                Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = disc, HierarchyRank = 5,
                RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20, ThreeTendency = 20,
            };

        // Five distinct defenders: a big, a stretch four, two wings, a small quick guard.
        Player[] Five(int baseId) => new[]
        {
            Mk(baseId + 0, 88, 85, 82, 35, 40, 42),   // banger — high interior, low discipline
            Mk(baseId + 1, 78, 70, 68, 50, 50, 50),
            Mk(baseId + 2, 66, 55, 50, 55, 60, 58),
            Mk(baseId + 3, 60, 48, 40, 70, 72, 70),
            Mk(baseId + 4, 52, 35, 28, 90, 88, 90),   // disciplined quick guard
        };

        GameState NewGame(int threshold = PersonalFoulTracker.DefaultFoulOutThreshold) =>
            new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold),
                          ArrowState.Off, new FatigueTracker(cfgF),
                          new PersonalFoulTracker(threshold));

        // ══════════════════════════════════════════════════════════════════════════════
        // A1a — WEIGHT PARITY. The moved math is the same math.
        // ══════════════════════════════════════════════════════════════════════════════
        {
            const int Draws = 20_000;
            var zones = new[] { ShotLocation.Rim, ShotLocation.Short, ShotLocation.Mid,
                                ShotLocation.Long, ShotLocation.Three };

            var g = NewGame();
            var men = Five(1);
            for (var i = 0; i < 5; i++) g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), men[i]);

            var slots = new List<int> { 1, 2, 3, 4, 5 };
            var occ   = new List<Player>(men);

            // Shooting: every zone, and every shooter slot including the 0 sentinel path.
            var shootMismatch = 0; var shootCells = 0;
            foreach (var zone in zones)
                foreach (var shooterSlot in new[] { 0, 1, 3, 5 })
                {
                    var engineRng = new SystemRng(4242);
                    var refRng    = new Random(4242);
                    for (var d = 0; d < Draws; d++)
                    {
                        var a = FoulCommitter.PickShootingSlot(occ, slots, zone, shooterSlot, engineRng);
                        var b = DrawFoulingDefender(refRng, TeamSide.Away, g.AwayRoster, zone, shooterSlot, 1);
                        shootCells++;
                        if (a != b) shootMismatch++;
                    }
                }
            Check("A1a shooting-foul parity — engine unit == S62 reference, draw for draw",
                  shootMismatch == 0,
                  $"{shootCells:N0} draws over 5 zones x 4 shooter slots (incl. the 0 sentinel), {shootMismatch} mismatches");

            // Non-shooting: both buckets.
            var nsMismatch = 0; var nsCells = 0;
            foreach (var isReachIn in new[] { true, false })
            {
                var engineRng = new SystemRng(909);
                var refRng    = new Random(909);
                for (var d = 0; d < Draws; d++)
                {
                    var a = FoulCommitter.PickNonShootingSlot(occ, slots, isReachIn, cfgM, engineRng);
                    var b = DrawNonShootingFouler(refRng, TeamSide.Away, g.AwayRoster, isReachIn, 1, cfgM);
                    nsCells++;
                    if (a != b) nsMismatch++;
                }
            }
            Check("A1a non-shooting parity — reach-in and situational buckets both",
                  nsMismatch == 0, $"{nsCells:N0} draws, {nsMismatch} mismatches");

            // The tilt actually flips direction by zone — a parity check alone would pass
            // just as happily against a reference that had lost the flip, since it would
            // have lost it on both sides.
            var wRim   = FoulCommitter.ShootingWeights(occ, slots, ShotLocation.Rim,   1);
            var wThree = FoulCommitter.ShootingWeights(occ, slots, ShotLocation.Three, 1);
            // Slot 1 is the matched man in both; compare the BANGER-vs-GUARD split of the
            // residual. Slot 2 is the next-biggest man, slot 5 the smallest.
            var rimFavoursBig   = wRim[1]   > wRim[4];
            var threeFavoursSml = wThree[4] > wThree[1];
            Check("A1a the zone tilt still flips — rim favours size, three favours the perimeter",
                  rimFavoursBig && threeFavoursSml,
                  $"rim big {wRim[1]:F4} vs small {wRim[4]:F4}; three big {wThree[1]:F4} vs small {wThree[4]:F4}");
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // A1b — PRODUCTION TOTALITY. The output is never a sentinel.
        //
        // Parity and totality are separate on purpose: the S62 reference may sentinel on
        // its INPUT (shooterSlot 0), but the production OUTPUT never does — every event
        // must carry an occupied seat of the correct team.
        // ══════════════════════════════════════════════════════════════════════════════
        {
            var allReal = true; var cases = 0;
            foreach (var occupied in new[] {
                new[] { 1, 2, 3, 4, 5 }, new[] { 2, 3, 4, 5 }, new[] { 1, 5 }, new[] { 3 } })
            {
                var men = Five(101);
                var occ = new List<Player>(); var slots = new List<int>();
                foreach (var sIdx in occupied) { occ.Add(men[sIdx - 1]); slots.Add(sIdx); }

                var rng = new SystemRng(77);
                foreach (var zone in new[] { ShotLocation.Rim, ShotLocation.Three })
                    for (var shooterSlot = 0; shooterSlot <= 5; shooterSlot++)
                        for (var d = 0; d < 500; d++)
                        {
                            var s = FoulCommitter.PickShootingSlot(occ, slots, zone, shooterSlot, rng);
                            cases++;
                            if (!slots.Contains(s)) allReal = false;
                        }
            }
            Check("A1b totality — the selector always returns an OCCUPIED seat",
                  allReal, $"{cases:N0} draws across four occupancy shapes, incl. the matched-man-absent branch");
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // A2 / A3 / A6 — a real game: conservation, reconciliation, and the accumulation
        // that carries a man across the threshold mid-run.
        // ══════════════════════════════════════════════════════════════════════════════
        {
            var g = NewGame();
            var home = Five(1); var away = Five(11);
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), home[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), away[i]);
            }
            g.SetPossessionArrow(TeamSide.Home);

            var (result, teamFoulDeltaTotal) = RunGameWithFoulWatch(g, configPath, seed: 5150);

            // ── A2: every recorded committer was really on that seat at that possession.
            var mismatched = 0; var events = 0; var sentinels = 0;
            foreach (var r in result.Possessions)
            {
                void Verify(int slot, int playerId, TeamSide side)
                {
                    events++;
                    if (playerId == 0) { sentinels++; return; }
                    var p = g.RosterFor(side).PlayerAt(new Slot(side, slot), r.Number);
                    if (p is null || p.PlayerId != playerId) mismatched++;
                }
                if (r.ShootingFouls is { } sfs)    foreach (var e in sfs)  Verify(e.CommitterSlot, e.CommitterPlayerId, r.Defense);
                if (r.NonShootingFouls is { } nsfs) foreach (var e in nsfs) Verify(e.CommitterSlot, e.CommitterPlayerId, r.Defense);
                if (r.OffensiveFouls is { } offs)   foreach (var e in offs) Verify(e.CommitterSlot, e.CommitterPlayerId, r.Offense);
            }
            Check("A2 conservation — every foul's committer occupied that seat, correct team, all three ledgers",
                  mismatched == 0 && events > 0,
                  $"{events:N0} foul events, {mismatched} mismatched, {sentinels} no-man sentinels");

            // ── A3: reconciliation, per bucket so opposite misses cannot cancel.
            long shEvents = 0, nsEvents = 0, offEvents = 0, offLooseBall = 0, lbfTerminals = 0;
            foreach (var r in result.Possessions)
            {
                shEvents  += r.ShootingFouls?.Count    ?? 0;
                nsEvents  += r.NonShootingFouls?.Count ?? 0;
                offEvents += r.OffensiveFouls?.Count   ?? 0;
                if (r.OffensiveFouls is { } offs)
                    foreach (var e in offs) if (e.IsLooseBall) offLooseBall++;
            }
            foreach (var r in result.Possessions)
                if (r.EndLabel == "LooseBallFoulOnOffense") lbfTerminals++;

            Check("A3 team fouls == shooting + non-shooting events (positive-delta accumulation, reset-proof)",
                  teamFoulDeltaTotal == shEvents + nsEvents,
                  $"deltas {teamFoulDeltaTotal} vs events {shEvents} + {nsEvents} = {shEvents + nsEvents}");

            Check("A3 the scrum-foul ledger matches its terminal count",
                  offLooseBall == lbfTerminals,
                  $"{offLooseBall} loose-ball events vs {lbfTerminals} terminals");

            // R3 asserted, not assumed: offensive fouls move the team counter by ZERO.
            // The identity above already proves it — the delta total is fully explained by
            // the other two ledgers with the offensive events sitting outside it — but state
            // it as its own line so a future change that starts charging them fails HERE
            // with the right message rather than as an unexplained arithmetic slip.
            Check("A3 offensive fouls move the team-foul total by exactly ZERO (R3)",
                  teamFoulDeltaTotal - (shEvents + nsEvents) == 0 && offEvents > 0,
                  $"{offEvents} offensive fouls charged, team-foul residual {teamFoulDeltaTotal - (shEvents + nsEvents)}");

            // ── Total personal increments == sum of all three ledgers.
            long personalTotal = 0;
            foreach (var kv in g.PersonalFouls.Counts) personalTotal += kv.Value;
            Check("A3 personal fouls == every event in all three ledgers",
                  personalTotal == shEvents + nsEvents + offEvents,
                  $"{personalTotal} personals vs {shEvents + nsEvents + offEvents} events");

            // ── A6: the stateful-accumulation check (CONVENTIONS §2a is mandatory here —
            // personal fouls are exactly cross-possession state). One run must show a man
            // BELOW the threshold early and AT or past it later, so both sides of the
            // branch are exercised in a single batch rather than in two tidy runs.
            var crossed = 0; var maxPf = 0;
            foreach (var kv in g.PersonalFouls.Counts)
            {
                if (kv.Value >= g.PersonalFouls.FoulOutThreshold) crossed++;
                if (kv.Value > maxPf) maxPf = kv.Value;
            }
            Check("A6 stateful accumulation — the threshold is crossed mid-run, both sides seen",
                  crossed > 0 && maxPf >= g.PersonalFouls.FoulOutThreshold,
                  $"{crossed} men reached {g.PersonalFouls.FoulOutThreshold}+, deepest {maxPf}");
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // A9 — NEGATIVE CONTROL. Conservation must be able to SEE a mis-wire.
        //
        // Hand-build the exact bug the check exists to catch — a defensive foul whose
        // committer was drawn from the OFFENSE — and confirm A2's test rejects it. Without
        // this, "0 mismatches" could mean the check is looking at nothing.
        // ══════════════════════════════════════════════════════════════════════════════
        {
            var g = NewGame();
            var home = Five(1); var away = Five(11);
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), home[i]);
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), away[i]);
            }
            // The wrong wire: a foul committed by the AWAY defense, credited to a HOME man.
            var wrong = new ShootingFoulEvent(ShotLocation.Rim, 1)
            {
                CommitterSlot     = 1,
                CommitterPlayerId = home[0].PlayerId          // offense's man on a defensive foul
            };
            var seated = g.RosterFor(TeamSide.Away).PlayerAt(new Slot(TeamSide.Away, wrong.CommitterSlot), 1);
            var caught = seated is null || seated.PlayerId != wrong.CommitterPlayerId;
            Check("A9 negative control — a committer drawn from the wrong team FAILS conservation",
                  caught, "hand-built mis-wire rejected by the A2 test");
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // A4 / A5 — five-then-out, the escape hatch, and its recovery rule.
        // ══════════════════════════════════════════════════════════════════════════════
        {
            var g = NewGame(threshold: 3);       // low threshold so the rule fires quickly
            var men = Five(1);
            for (var i = 0; i < 5; i++) g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), men[i]);

            var victim = men[0];
            for (var i = 0; i < 3; i++) g.PersonalFouls.Increment(victim.PlayerId);

            Check("A4 five-then-out — the threshold marks him disqualified",
                  g.PersonalFouls.IsDisqualified(victim.PlayerId),
                  $"{g.PersonalFouls.CountFor(victim.PlayerId)} PF vs threshold 3");

            // The seat refuses him. This is the guarantee that no policy — present or
            // future, correct or buggy — can put him back on the floor.
            var refused = false;
            try { g.HomeRoster.Substitute(g.HomeLineup.SlotAt(2), victim, 5); }
            catch (InvalidOperationException) { refused = true; }
            Check("A4 the SEAT refuses a disqualified man, so no policy can re-insert him",
                  refused, "Roster.Substitute threw as designed");

            // He keeps counting past the threshold — the escape hatch means 5+ is reachable
            // and the record must say so rather than clamping.
            g.PersonalFouls.Increment(victim.PlayerId);
            Check("A4 counts are not capped — a stranded man reaches the 5+ bucket",
                  g.PersonalFouls.CountFor(victim.PlayerId) == 4 && g.PersonalFouls.IsDisqualified(victim.PlayerId),
                  $"{g.PersonalFouls.CountFor(victim.PlayerId)} PF past a threshold of 3");

            // A5: a legal replacement is still a legal replacement — an eligible man is
            // seated without complaint, which is the other half of the guard being right.
            var spare = Mk(50, 70, 60, 55, 60, 55, 55);
            var seatedOk = true;
            try { g.HomeRoster.Substitute(g.HomeLineup.SlotAt(1), spare, 5); }
            catch (InvalidOperationException) { seatedOk = false; }
            Check("A5 replacement legality — an eligible, non-disqualified man seats normally",
                  seatedOk && g.HomeRoster.PlayerAt(g.HomeLineup.SlotAt(1))?.PlayerId == 50);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // A7 — INERT-MODE ISOLATION. Foul attribution cannot move the game.
        //
        // The claim S87 rests on is that committer selection draws from its OWN stream and
        // therefore perturbs nothing. A stored pre-S87 baseline would test that too, but it
        // rots the moment anything else legitimately changes and it cannot say WHY a
        // mismatch happened. So the property is tested directly instead: with the threshold
        // raised out of reach (the inert mode — no disqualifications, no replacements), run
        // the SAME game twice changing ONLY the foul seed.
        //
        // ★ The check has two halves and needs both. Everything that existed before S87 must
        // be bit-identical — if the foul stream leaked into gameplay, it would not be. AND
        // the committer columns must DIFFER — otherwise the first half would pass just as
        // happily against a wire that never draws at all, which is exactly the "nothing
        // moved" acceptance test S73.1 warned about.
        // ══════════════════════════════════════════════════════════════════════════════
        {
            const int Inert = 1_000_000;

            (GovernorRunResult R, GameState G) RunWithFoulSeed(int foulSeed)
            {
                var g = NewGame(Inert);
                var home = Five(1); var away = Five(11);
                for (var i = 0; i < 5; i++)
                {
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), home[i]);
                    g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), away[i]);
                }
                g.SetPossessionArrow(TeamSide.Home);
                var r = RunGameWithFoulWatch(g, configPath, seed: 31337, foulSeedOverride: foulSeed);
                return (r.Result, g);
            }

            var (rA, gA) = RunWithFoulSeed(1);
            var (rB, gB) = RunWithFoulSeed(999_983);

            var identical =
                rA.Possessions.Count == rB.Possessions.Count &&
                gA.HomeScore == gB.HomeScore && gA.AwayScore == gB.AwayScore &&
                Math.Abs(rA.TotalSeconds - rB.TotalSeconds) == 0.0 &&
                rA.OvertimePeriods == rB.OvertimePeriods &&
                gA.Fouls.FoulsFor(TeamSide.Home) == gB.Fouls.FoulsFor(TeamSide.Home) &&
                gA.Fouls.FoulsFor(TeamSide.Away) == gB.Fouls.FoulsFor(TeamSide.Away);

            var firstDiff = -1;
            if (identical)
                for (var i = 0; i < rA.Possessions.Count; i++)
                {
                    var a = rA.Possessions[i]; var b = rB.Possessions[i];
                    if (a.EndLabel == b.EndLabel && a.Points == b.Points &&
                        a.Fga == b.Fga && a.Fgm == b.Fgm && a.ThreePa == b.ThreePa && a.ThreePm == b.ThreePm &&
                        a.Fta == b.Fta && a.Ftm == b.Ftm && a.OrbWon == b.OrbWon && a.BlkCount == b.BlkCount &&
                        a.TurnoverOffSlot == b.TurnoverOffSlot && a.StealerSlot == b.StealerSlot &&
                        a.DefensiveRebounderSlot == b.DefensiveRebounderSlot &&
                        a.Elapsed == b.Elapsed &&
                        (a.ShootingFouls?.Count ?? 0) == (b.ShootingFouls?.Count ?? 0) &&
                        (a.NonShootingFouls?.Count ?? 0) == (b.NonShootingFouls?.Count ?? 0) &&
                        (a.OffensiveFouls?.Count ?? 0) == (b.OffensiveFouls?.Count ?? 0)) continue;
                    firstDiff = i; identical = false; break;
                }

            Check("A7 inert mode — changing ONLY the foul seed leaves every pre-S87 fact bit-identical",
                  identical,
                  identical
                    ? $"{rA.Possessions.Count} possessions, {gA.HomeScore}-{gA.AwayScore}, team fouls " +
                      $"{gA.Fouls.FoulsFor(TeamSide.Home)}/{gA.Fouls.FoulsFor(TeamSide.Away)} — matched"
                    : $"first divergence at possession index {firstDiff}");

            // The discriminating half: the committer columns MUST move, or the check above
            // is passing for the wrong reason.
            var committerDiffs = 0; var committerTotal = 0;
            for (var i = 0; i < Math.Min(rA.Possessions.Count, rB.Possessions.Count); i++)
            {
                var a = rA.Possessions[i]; var b = rB.Possessions[i];
                void Compare(int x, int y) { committerTotal++; if (x != y) committerDiffs++; }
                if (a.ShootingFouls is { } sa && b.ShootingFouls is { } sb)
                    for (var k = 0; k < Math.Min(sa.Count, sb.Count); k++) Compare(sa[k].CommitterSlot, sb[k].CommitterSlot);
                if (a.NonShootingFouls is { } na && b.NonShootingFouls is { } nb)
                    for (var k = 0; k < Math.Min(na.Count, nb.Count); k++) Compare(na[k].CommitterSlot, nb[k].CommitterSlot);
            }
            Check("A7 discriminating signal — the committer columns DO move with the foul seed",
                  committerDiffs > 0,
                  $"{committerDiffs} of {committerTotal} committers changed — a never-drawing wire would show 0");

            // And inert really is inert: nobody fouled out, so nothing was ever replaced.
            var anyDq = false;
            foreach (var kv in gA.PersonalFouls.Counts) if (gA.PersonalFouls.IsDisqualified(kv.Key)) anyDq = true;
            Check("A7 the inert mode is genuinely inert — no disqualifications at a huge threshold",
                  !anyDq, $"threshold {Inert:N0}");
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // A8 — CONFIG GUARDS.
        // ══════════════════════════════════════════════════════════════════════════════
        {
            var threw = false; var named = false;
            try { _ = new PersonalFoulTracker(0); }
            catch (ArgumentOutOfRangeException ex)
            {
                threw = true;
                named = ex.Message.Contains("FoulOutThreshold") && ex.Message.Contains("at least 1");
            }
            Check("A8 config guard — a threshold below 1 throws, and the message names the rule",
                  threw && named);

            var okAtOne = true;
            try { _ = new PersonalFoulTracker(1); } catch { okAtOne = false; }
            Check("A8 the boundary itself is legal — a threshold of 1 constructs", okAtOne);
        }

        Console.WriteLine();
        return ok;
    }

    /// <summary>
    /// Drive a full game and, alongside it, accumulate the RESET-PROOF team-foul observable
    /// A3 reconciles against: both teams' counts read at every possession boundary, with only
    /// the POSITIVE deltas summed. The halftime reset shows up as one negative delta and is
    /// ignored, so the total equals every increment charged across the whole game regardless
    /// of how the reset and the Governor's callbacks are ordered.
    /// </summary>
    private static (GovernorRunResult Result, long TeamFoulDeltaTotal) RunGameWithFoulWatch(
        GameState game, string configPath, int seed, int? foulSeedOverride = null)
    {
        var cfgA = RollAConfig.Load(configPath);
        var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);
        var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);
        var cfgI = RollIConfig.Load(configPath);
        var cfgJ = RollJConfig.Load(configPath);
        var cfgK = RollKConfig.Load(configPath);
        var cfgL = RollLConfig.Load(configPath);
        var cfgM = RollMConfig.Load(configPath);
        var cfgMatchup = MatchupConfig.Load(configPath);
        var cfgOffFoul = RollOffensiveFoulConfig.Load(configPath);

        var resolver = new Resolver(
            new RollAGenerator(cfgA, cfgMatchup, game), cfgA,
            new RollBGenerator(cfgB, cfgMatchup, game),
            new RollCGenerator(cfgC), cfgC,
            new RollDGenerator(cfgD),
            new RollEGenerator(cfgE, game),
            new AttentionGenerator(AttentionConfig.Load(configPath), game),
            new RollFGenerator(cfgF, cfgMatchup, game),
            new RollGGenerator(cfgG, cfgMatchup, game),
            new RollHGenerator(cfgH, cfgMatchup, game),
            new RollIGenerator(cfgI, cfgMatchup, game),
            new RollJGenerator(cfgJ, cfgMatchup, game),
            new RollKGenerator(cfgK, cfgMatchup, game),
            new RollLGenerator(cfgL, game),
            new RollMGenerator(cfgM, cfgMatchup, game),
            new RollOffensiveFoulGenerator(cfgOffFoul),
            cfgMatchup, game, new SystemRng(seed),
            new SystemRng(foulSeedOverride ?? unchecked(seed + 5)));

        var governor = new Governor(
            resolver, game, GovernorConfig.Load(configPath), RollClockConfig.Load(configPath),
            new SystemRng(seed + 1), EndOfHalfConfig.Load(configPath),
            new FoulWatchPolicy());

        var firstState = new PossessionState(1, TeamSide.Home, TeamSide.Away, EntryType.DeadBallInbound);
        var result = governor.Run(firstState);

        // ★ One final sample AFTER the run. The Governor reports a boundary only when a
        // successor possession will actually run, so the LAST possession of the game has
        // no boundary behind it and its fouls would never be sampled. The first version of
        // this check missed exactly that and came up one increment short — the shortfall
        // was in the instrument, not the engine, which is precisely the failure the
        // positive-delta design was meant to make legible.
        FoulWatchPolicy.Instance!.SampleFinal(game);

        return (result, FoulWatchPolicy.LastTotal);
    }

    /// <summary>
    /// A substitution policy that substitutes NOBODY — it exists only to be handed the
    /// boundary callbacks, which is where the reset-proof team-foul observable is sampled.
    /// Making no substitutions keeps the seat-to-man mapping constant, so A2's conservation
    /// test is measuring committer selection rather than the roster log.
    /// </summary>
    private sealed class FoulWatchPolicy : ISubstitutionPolicy
    {
        internal static long LastTotal;
        internal static FoulWatchPolicy? Instance;
        private int _prevHome, _prevAway;

        public FoulWatchPolicy() { LastTotal = 0; _prevHome = 0; _prevAway = 0; Instance = this; }

        /// <summary>The closing read — see the note at the call site.</summary>
        internal void SampleFinal(GameState game) => Sample(game);

        private void Sample(GameState game)
        {
            var h = game.Fouls.FoulsFor(TeamSide.Home);
            var a = game.Fouls.FoulsFor(TeamSide.Away);
            if (h > _prevHome) LastTotal += h - _prevHome;   // negative delta = halftime reset, ignored
            if (a > _prevAway) LastTotal += a - _prevAway;
            _prevHome = h; _prevAway = a;
        }

        public void OnPossessionBoundary(GameState game, int nextPossessionNumber, double elapsedSeconds, bool isDeadBall)
            => Sample(game);

        public void OnPeriodBreak(GameState game, int nextPossessionNumber, double finalPossessionElapsedSeconds, PeriodBreakKind kind)
            => Sample(game);
    }
}
