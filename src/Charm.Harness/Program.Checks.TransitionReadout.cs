using Charm.Engine;

namespace Charm.Harness;

internal static partial class Program
{
    /// <summary>
    /// Phase 76 (Session 85) — THE FAST-BREAK READOUT'S WIRING. Conservation and wiring only:
    /// this file asserts NO basketball target and no rate. Every basketball number the session
    /// added lives on the season page, unasserted, per the page-only calibration principle.
    ///
    /// <para>What it is guarding against. The readout adds a three-way shot partition and a
    /// per-arm split of Roll J's run-or-not pie. Both are the kind of instrument that can be
    /// wired wrong and still look plausible: a mis-scoped gate double-counts or drops attempts
    /// while every existing total stays balanced, and a break counter that reads the
    /// possession's HISTORY rather than the EVENT would credit a break shot to a possession
    /// that pushed and then kicked it back out to run a set.</para>
    ///
    /// <para>Deliberately absent: any break-subset block conservation assertion. Phase 36
    /// already asserts <c>BlkBySlot.Total == BlkCount</c> on every possession, which holds on
    /// the break subset by construction — a second copy would be decoration. What IS asserted
    /// here is the containment the new per-seat accumulator introduces
    /// (<c>FastBreakBlkBySlot &lt;= BlkBySlot</c>, seat by seat), because that is a new
    /// relationship nothing else checks.</para>
    ///
    /// <para>Two of the checks are EXISTENCE checks, and they are the point (the S73.1 lesson:
    /// "nothing moved" must be paired with a discriminating signal only the new tree can
    /// produce). B5 needs a possession that carries BOTH a break shot and a non-break shot —
    /// only reachable by pushing, missing, rebounding, and kicking it back out, which clears
    /// the break. B6 needs a break-stamped possession whose entry is not a transition, which
    /// only a beaten press produces. If either goes missing, the corresponding half of the
    /// wiring is unproven, so absence FAILS rather than passing quietly.</para>
    /// </summary>
    private static bool Phase76TransitionReadoutCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("Phase 76 — transition / fast-break readout wiring (Session 85):");

        var ok = true;

        var cfgA     = RollAConfig.Load(configPath);
        var cfgB     = RollBConfig.Load(configPath);
        var cfgC     = RollCConfig.Load(configPath);
        var cfgD     = RollDConfig.Load(configPath);
        var cfgE     = RollEConfig.Load(configPath);
        var cfgGov   = GovernorConfig.Load(configPath);
        var cfgClock = RollClockConfig.Load(configPath);
        var cfgEoH   = EndOfHalfConfig.Load(configPath);
        var matchupCfg = MatchupConfig.Load(configPath);

        // A batch of whole games, each on a FRESH GameState. Fresh state per game is not
        // cosmetic: foul counts, the possession arrow and the lineup all live on GameState, so
        // sharing one across the batch would push the league into the bonus and route arms
        // differently late in the run than early (§2a). The batch is sized to make the two
        // existence checks reliable rather than lucky — a beaten press is roughly a twentieth
        // of possessions, and a push that misses, is rebounded and then reset is rarer still.
        const int Games = 40;
        var records = new List<PossessionRecord>();
        for (var gi = 0; gi < Games; gi++)
        {
            var game = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 1; i <= 5; i++)
            {
                game.HomeRoster.SetStarter(game.HomeLineup.SlotAt(i), Mk58(50 + i, 30, 30));
                game.AwayRoster.SetStarter(game.AwayLineup.SlotAt(i), Mk58(50 - i, 30, 30));
            }
            game.SetPossessionArrow(TeamSide.Home);

            var rng = new SystemRng(760000 + gi);
            var resolver = new Resolver(
                new RollAGenerator(cfgA, matchupCfg, game),
                cfgA,
                new RollBGenerator(cfgB, matchupCfg, game),
                new RollCGenerator(cfgC),
                cfgC,
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, game),
                new AttentionGenerator(AttentionConfig.Load(configPath), game),
                new RollFGenerator(RollFConfig.Load(configPath), matchupCfg, game),
                new RollGGenerator(RollGConfig.Load(configPath), matchupCfg, game),
                new RollHGenerator(RollHConfig.Load(configPath), matchupCfg, game),
                new RollIGenerator(RollIConfig.Load(configPath), matchupCfg, game),
                new RollJGenerator(RollJConfig.Load(configPath), matchupCfg, game),
                new RollKGenerator(RollKConfig.Load(configPath), matchupCfg, game),
                new RollLGenerator(RollLConfig.Load(configPath), game),
                new RollMGenerator(RollMConfig.Load(configPath), matchupCfg, game),
                new RollOffensiveFoulGenerator(RollOffensiveFoulConfig.Load(configPath)),
                matchupCfg,
                game,
                rng);

            var governor = new Governor(resolver, game, cfgGov, cfgClock, new SystemRng(770000 + gi), cfgEoH);
            var first = new PossessionState(
                PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                Entry: EntryType.DeadBallInbound);
            records.AddRange(governor.Run(first).Possessions);
        }

        Console.WriteLine($"    fixture: {Games} whole games, fresh GameState each, {records.Count} possession records");

        void Check(string label, bool pass, string detail)
        {
            ok &= pass;
            Console.WriteLine(pass ? $"    [OK] {label}" : $"    [FAIL] {label} — {detail}");
        }

        // ── B1 — the entry partition is exhaustive, and the arm label tracks it exactly ──────
        // The count half of B1 is arithmetic; the half that can actually fail is whether the
        // carried arm label agrees with the entry type. Roll J is the only stamper and it runs
        // only on a transition entry, so the label must be non-null on exactly those records.
        {
            var trans    = records.Count(r => r.Entry == EntryType.Transition);
            var nonTrans = records.Count(r => r.Entry != EntryType.Transition);
            Check("B1 entry partition sums to every record",
                  trans + nonTrans == records.Count,
                  $"{trans} + {nonTrans} != {records.Count}");

            // The label appears on exactly the transition entries the resolver WALKED. An
            // end-of-half possession that kills the clock without shooting is recorded but never
            // resolved at all — the Governor short-circuits before the walk, so Roll J does not
            // run and there is no arm to report. That is correct engine behaviour, so it is
            // carved out by name here rather than by loosening the check: an unresolved entry
            // MUST carry no label, and a resolved one MUST carry one.
            var labelledNonTrans = records.Count(r => r.Entry != EntryType.Transition && r.TransitionArm is not null);
            var unlabelledWalked = records.Count(r => r.Entry == EntryType.Transition &&
                                                      r.EndOfHalfIntent != EndOfHalfIntent.NoShot &&
                                                      r.TransitionArm is null);
            var labelledUnwalked = records.Count(r => r.EndOfHalfIntent == EndOfHalfIntent.NoShot &&
                                                      r.TransitionArm is not null);
            Check("B1 the run-or-not label appears on exactly the RESOLVED transition entries",
                  labelledNonTrans == 0 && unlabelledWalked == 0 && labelledUnwalked == 0,
                  $"{labelledNonTrans} non-transition labelled; {unlabelledWalked} resolved entries unlabelled; {labelledUnwalked} unresolved entries labelled");
            var noShotTrans = records.Count(r => r.Entry == EntryType.Transition &&
                                                 r.EndOfHalfIntent == EndOfHalfIntent.NoShot);
            Console.WriteLine($"      transition entries {trans} of {records.Count}" +
                              $" ({noShotTrans} never resolved: end-of-half, no shot)");
        }

        // ── B2 — the five sibling arms sum to the transition-entry count, no residual ────────
        // True by REPRESENTATION with one categorical label rather than by five mutually
        // exclusive booleans, which is why the interesting failure would be a label the switch
        // does not recognise — hence the explicit "unrecognised" bucket.
        {
            // Denominator: the transition entries the resolver walked (see B1).
            var trans = records.Count(r => r.Entry == EntryType.Transition &&
                                           r.EndOfHalfIntent != EndOfHalfIntent.NoShot);
            var push    = records.Count(r => r.TransitionArm == TransitionOutcome.Push);
            var settle  = records.Count(r => r.TransitionArm == TransitionOutcome.Settle);
            var to      = records.Count(r => r.TransitionArm == TransitionOutcome.Turnover);
            var foul     = records.Count(r => r.TransitionArm == TransitionOutcome.DefensiveFoul);
            var jump    = records.Count(r => r.TransitionArm == TransitionOutcome.JumpBall);
            var known   = push + settle + to + foul + jump;
            var unrecognised = records.Count(r => r.TransitionArm is { } a &&
                                                  a is not (TransitionOutcome.Push or TransitionOutcome.Settle
                                                            or TransitionOutcome.Turnover or TransitionOutcome.DefensiveFoul
                                                            or TransitionOutcome.JumpBall));
            Check("B2 five sibling arms sum exactly to the resolved transition-entry count",
                  known == trans && unrecognised == 0,
                  $"arms {known} vs resolved entries {trans}, {unrecognised} unrecognised labels");
            Console.WriteLine($"      push {push} / settle {settle} / turnover {to} / def foul {foul} / jump ball {jump}");
            // Every arm must actually occur, or a zero bucket is indistinguishable from a
            // bucket that is never wired.
            Check("B2 every arm occurs at least once in the fixture",
                  push > 0 && settle > 0 && to > 0 && foul > 0 && jump > 0,
                  "one or more arms never fired — a zero bucket cannot be told from an unwired one");
        }

        // ── B3 — the three-way shot partition sums exactly to FGA, per possession ────────────
        // The check that catches a mis-scoped gate: no attempt in two buckets, none in neither.
        // Asserted PER RECORD, not only in aggregate, because two opposite-signed errors on
        // different possessions cancel in a league total.
        {
            var bad = records.Count(r => r.FastBreakFga + r.BreakPutbackFga + r.NonBreakFga != r.Fga);
            Check("B3 three shot buckets sum to FGA on every possession", bad == 0,
                  $"{bad} possessions had a residual");
            var badBlk = records.Count(r => r.FastBreakBlk + r.BreakPutbackBlk + r.NonBreakBlk != r.BlkCount);
            Check("B3 three block buckets sum to BlkCount on every possession", badBlk == 0,
                  $"{badBlk} possessions had a residual");
        }

        // ── B4 — the two nesting chains, per possession ──────────────────────────────────────
        // Two chains rather than one on purpose: a made break three must appear in BOTH
        // siblings (the three-point make AND the bucket-wide make), which is exactly the wiring
        // a single chain cannot catch.
        {
            var chain1 = records.Count(r => !(0 <= r.FastBreakThreePm &&
                                              r.FastBreakThreePm <= r.FastBreakThreePa &&
                                              r.FastBreakThreePa <= r.FastBreakFga));
            var chain2 = records.Count(r => !(r.FastBreakThreePm <= r.FastBreakFgm &&
                                              r.FastBreakFgm <= r.FastBreakFga &&
                                              r.FastBreakFga <= r.Fga));
            var chain3 = records.Count(r => !(0 <= r.BreakPutbackFgm &&
                                              r.BreakPutbackFgm <= r.BreakPutbackFga));
            var chain4 = records.Count(r => !(0 <= r.NonBreakFgm && r.NonBreakFgm <= r.NonBreakFga));
            Check("B4 3PM <= 3PA <= break FGA", chain1 == 0, $"{chain1} possessions broke the chain");
            Check("B4 3PM <= break FGM <= break FGA <= FGA", chain2 == 0, $"{chain2} possessions broke the chain");
            Check("B4 break putback and non-break makes nest inside their attempts",
                  chain3 == 0 && chain4 == 0, $"{chain3} putback / {chain4} non-break broke the chain");

            // The new per-seat break-block accumulator: totals to the bucket count, and is
            // contained in the per-seat block accumulator seat by seat. Nothing else checks
            // either relationship.
            var seatTotal = records.Count(r => r.FastBreakBlkBySlot.Total != r.FastBreakBlk);
            var seatContain = records.Count(r => Enumerable.Range(1, 5)
                                                  .Any(sl => r.FastBreakBlkBySlot[sl] > r.BlkBySlot[sl]));
            Check("B4 per-seat break blocks total to the break-block count", seatTotal == 0,
                  $"{seatTotal} possessions disagreed");
            Check("B4 per-seat break blocks are contained in per-seat blocks", seatContain == 0,
                  $"{seatContain} possessions had a seat with more break blocks than blocks");
        }

        // ── B5 — the counters are EVENT-scoped, not history-scoped ───────────────────────────
        // The discriminating signal: a possession that pushed, missed, was rebounded, and then
        // kicked it back out to run a set. The break flag is cleared at the kick-out, so the
        // second shot MUST land in non-break. A history-scoped counter would put it in the
        // break bucket and this possession would not exist.
        {
            var mixed = records.Count(r => r.FastBreakFga > 0 && r.NonBreakFga > 0);
            Check("B5 a possession carrying BOTH a break shot and a non-break shot exists",
                  mixed > 0,
                  "none found — the event-vs-history distinction is unproven by this fixture");
            Console.WriteLine($"      possessions with a break shot AND a non-break shot: {mixed}");

            // And the converse: a possession that never carried a break contributes nothing to
            // any break counter.
            var leak = records.Count(r => r.FastBreakFga == 0 && r.BreakPutbackFga == 0 &&
                                          (r.FastBreakFgm > 0 || r.FastBreakThreePa > 0 ||
                                           r.FastBreakThreePm > 0 || r.BreakPutbackFgm > 0 ||
                                           r.FastBreakBlk > 0 || r.BreakPutbackBlk > 0 ||
                                           r.FastBreakBlkBySlot.Total > 0));
            Check("B5 a possession with no break attempt contributes zero to every break counter",
                  leak == 0, $"{leak} possessions leaked");
        }

        // ── B6 — press-born breaks are in the break totals and out of the Roll J denominator ─
        // The second break source. A beaten press stamps the break flag on a possession whose
        // entry is not a transition, so it must appear in the break-side counts and must carry
        // no run-or-not label at all.
        {
            var pressBorn = records.Where(r => r.Entry != EntryType.Transition &&
                                               (r.FastBreakFga > 0 || r.BreakPutbackFga > 0)).ToList();
            Check("B6 press-born break shots exist in the fixture", pressBorn.Count > 0,
                  "none found — the second break source is unexercised, so the source split is unproven");
            Check("B6 no press-born break carries a run-or-not label",
                  pressBorn.All(r => r.TransitionArm is null),
                  $"{pressBorn.Count(r => r.TransitionArm is not null)} press-born possessions were labelled");
            Console.WriteLine($"      press-born break possessions {pressBorn.Count}" +
                              $", carrying {pressBorn.Sum(r => r.FastBreakFga + r.BreakPutbackFga)} attempts");
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "  Phase 76 transition readout check: PASSED"
                             : "  Phase 76 transition readout check: FAILED (see [FAIL] lines above)");
        return ok;
    }
}
