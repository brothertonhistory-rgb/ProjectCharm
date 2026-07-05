using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Phase 57 — court-aware turnover clock (Session 37).
//
// A possession-ending turnover (and offensive foul) no longer draws the full
// shared possession clock; it draws a shorter, court-dependent band — backcourt
// [1,10)s, frontcourt [6,30)s — selected by a TimeProfile the emitting arm stamps.
// This check is TARGET-FREE (no basketball calibration value is asserted; the band
// centers are placeholders tuned off the season page). It proves the WIRING:
//
//   (A) Profile stamping in isolation — every one of the three emitter families
//       (Roll C's 12 drawn arms, Roll K's 3 turnover terminals, and — via the
//       season fixture no-leak guard in (C) — the resolver's ResolveOffensiveFoul)
//       stamps the court-correct profile; the three violation arms stamp NO profile
//       and keep their invariant 30/0/10s.
//   (B) A possession that has offensive-rebounded is timed as a FRONTCOURT turnover
//       even if it carries the backcourt court-state flag (transition / ball-advanced
//       possessions never latch frontcourt) — you cannot rebound in the backcourt.
//   (C) Season fixture: no drawn turnover leaks to the shared clock (the strong
//       cross-emitter guard — a missed stamp anywhere shows up here), both courts
//       are populated, and every raw single-period draw sits inside its band.
//
// Neutrality (a non-turnover possession draws exactly as before at the unchanged
// Center 17.0) is guaranteed by construction — the shared draw's arithmetic is
// untouched (CoachAdjustedCenter was extracted verbatim) — and enforced in practice
// by the rest of the suite: the possession-length and pace checks would fail if the
// shared draw had shifted. This phase does not re-assert it.
// ============================================================================
internal static partial class Program
{
    private static bool Phase57TurnoverClockCheck(
        string configPath, RollCConfig cfgC, GameState game, PossessionState baseState)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 57 — court-aware turnover clock (Session 37; target-free) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        const double eps = 1e-9;
        var stateBack  = baseState with { Frontcourt = false };
        var stateFront = baseState with { Frontcourt = true };

        // A pie forced to a single outcome. Pie<T> requires a weight for EVERY enum
        // member, so fill the rest with 0.0 rather than passing a one-entry dictionary.
        static Pie<T> OneHot<T>(T target, double epsilon) where T : struct, Enum
        {
            var d = new Dictionary<T, double>();
            foreach (var v in Enum.GetValues<T>()) d[v] = 0.0;
            d[target] = 1.0;
            return new Pie<T>(d, epsilon);
        }

        // ── (A) Roll C — the 12 drawn arms stamp court-correct; the 3 violations
        //        stamp no profile and keep their invariant elapsed. ────────────────
        var drawn = new[]
        {
            TurnoverOutcome.BadPassDeadBall, TurnoverOutcome.BadPassIntercepted,
            TurnoverOutcome.LostBallDeadBall, TurnoverOutcome.LostBallLiveBall,
            TurnoverOutcome.OffensiveFoul, TurnoverOutcome.Travel,
            TurnoverOutcome.DoubleDribble, TurnoverOutcome.Carry,
            TurnoverOutcome.ThreeSecondViolation, TurnoverOutcome.FiveSecondCloselyGuarded,
            TurnoverOutcome.OffensiveGoaltending, TurnoverOutcome.BackcourtViolation,
        };
        var violations = new (TurnoverOutcome o, double elapsed)[]
        {
            (TurnoverOutcome.ShotClockViolation, cfgC.ShotClockViolationElapsedSeconds),
            (TurnoverOutcome.FiveSecondInbound,  cfgC.FiveSecondInboundElapsedSeconds),
            (TurnoverOutcome.TenSecondBackcourt, cfgC.TenSecondBackcourtElapsedSeconds),
        };

        static Terminal DriveC(PossessionState st, TurnoverOutcome o, RollCConfig rollCCfg, double pieEps)
        {
            var pie = OneHot(o, pieEps);
            return (Terminal)RollC.Execute(st, pie, new SystemRng(20260703), rollCCfg);
        }

        var cAllOk = true;
        foreach (var o in drawn)
        {
            var tf = DriveC(stateFront, o, cfgC, eps);
            var tb = DriveC(stateBack,  o, cfgC, eps);
            var ok = tf.TimeProfile == PossessionTimeProfile.FrontcourtTurnover
                  && tf.ElapsedSeconds is null
                  && tb.TimeProfile == PossessionTimeProfile.BackcourtTurnover
                  && tb.ElapsedSeconds is null;
            if (!ok) { cAllOk = false; Check($"Roll C {o} stamps court-correct profile, no invariant time", false,
                                              $"front={tf.TimeProfile}/{tf.ElapsedSeconds}, back={tb.TimeProfile}/{tb.ElapsedSeconds}"); }
        }
        Check($"Roll C: all {drawn.Length} drawn arms stamp court-correct profile (both courts), no invariant time", cAllOk);

        var cVioOk = true;
        foreach (var (o, expected) in violations)
        {
            var t = DriveC(stateFront, o, cfgC, eps);
            var ok = t.TimeProfile is null && t.ElapsedSeconds is { } es && Math.Abs(es - expected) < 1e-9;
            if (!ok) { cVioOk = false; Check($"Roll C {o} keeps invariant {expected}s, no profile", false,
                                             $"profile={t.TimeProfile}, elapsed={t.ElapsedSeconds}"); }
        }
        Check("Roll C: the 3 violation arms stamp NO profile and keep invariant 30/0/10s", cVioOk);

        // ── (A) Roll K — the 3 turnover terminals are always FrontcourtTurnover. ──
        var kOutcomes = new[]
        {
            OffensiveReboundOutcome.OffensiveFoul,
            OffensiveReboundOutcome.DeadBallTurnover,
            OffensiveReboundOutcome.LiveBallTurnover,
        };
        var kOk = true;
        foreach (var o in kOutcomes)
        {
            var pie = OneHot(o, eps);
            var t = (Terminal)RollK.Execute(stateFront, pie, game, new SystemRng(20260703));
            var ok = t.TimeProfile == PossessionTimeProfile.FrontcourtTurnover && t.ElapsedSeconds is null;
            if (!ok) { kOk = false; Check($"Roll K {o} stamps FrontcourtTurnover", false,
                                         $"profile={t.TimeProfile}, elapsed={t.ElapsedSeconds}"); }
        }
        Check("Roll K: OffensiveFoul / DeadBallTurnover / LiveBallTurnover all stamp FrontcourtTurnover", kOk);

        // ── (B) The multi-period reclassification rule. A possession that has
        //        offensive-rebounded is physically in the frontcourt, so its turnover is
        //        timed as frontcourt regardless of the (stale) backcourt court-state flag
        //        carried by transition / ball-advanced possessions. ────────────────────
        Check("effective profile: backcourt stamp across 2 periods times as FRONTCOURT (ORB ⇒ frontcourt)",
              Governor.EffectiveTurnoverProfile(PossessionTimeProfile.BackcourtTurnover, 2)
                  == PossessionTimeProfile.FrontcourtTurnover);
        Check("effective profile: backcourt stamp, single period stays BACKCOURT",
              Governor.EffectiveTurnoverProfile(PossessionTimeProfile.BackcourtTurnover, 1)
                  == PossessionTimeProfile.BackcourtTurnover);
        Check("effective profile: frontcourt stamp stays FRONTCOURT at any period count",
              Governor.EffectiveTurnoverProfile(PossessionTimeProfile.FrontcourtTurnover, 3)
                  == PossessionTimeProfile.FrontcourtTurnover);

        // ── (C) Season fixture — the cross-emitter no-leak guard + band ranges. ──
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
            var tiny = LoadWorld(fixturePath);
            const long seed = 20260703;
            var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false);
            var L = outcome.League;

            // The strong guard: ANY drawn (non-fixed-time) turnover that reached the
            // Governor without a profile — including a missed resolver ResolveOffensiveFoul
            // stamp — is counted here. Zero is the only pass.
            Check("no drawn turnover leaked to the shared clock (every emitter stamps a profile)",
                  L.DrawnTurnoverNoProfileN == 0, $"leaked={L.DrawnTurnoverNoProfileN}");

            Check("both courts populated (backcourt and frontcourt turnovers both occur)",
                  L.BackcourtToN > 0 && L.FrontcourtToN > 0,
                  $"backcourt n={L.BackcourtToN}, frontcourt n={L.FrontcourtToN}");

            Check("backcourt raw draws all inside [1.0, 10.0)",
                  L.BackcourtToN > 0 && L.BackcourtRawMin >= 1.0 && L.BackcourtRawMax < 10.0,
                  $"min={L.BackcourtRawMin:F2}, max={L.BackcourtRawMax:F2}");

            Check("frontcourt single-period raw draws all inside [6.0, 30.0)",
                  L.FrontcourtToN > 0 && L.FrontcourtRawMin1P >= 6.0 && L.FrontcourtRawMax1P < 30.0,
                  $"min={L.FrontcourtRawMin1P:F2}, max={L.FrontcourtRawMax1P:F2}, multiPeriod n={L.FrontcourtMultiPeriodN}");

            // A6 with the new bands: applied = min(band draw, period-remaining), so the
            // per-game conservation identity (sum of applied == game seconds) must still
            // hold — the clamp now interacts with band draws, not just shared draws.
            Check("per-game elapsed still conserves with the new bands (A6)",
                  L.ElapsedMismatchGames == 0,
                  $"mismatched games={L.ElapsedMismatchGames}, max delta {L.MaxElapsedMismatch:E3}");

            // Eyeball vs the oracle (not asserted — placeholders, tuned off the page):
            // backcourt raw ~5s, frontcourt raw ~15s.
            var bkMean = L.BackcourtToN > 0 ? L.BackcourtToRawS / L.BackcourtToN : 0.0;
            var fcMean = L.FrontcourtToN > 0 ? L.FrontcourtToRawS / L.FrontcourtToN : 0.0;
            Console.WriteLine($"    (fixture raw means — backcourt {bkMean:F2}s / frontcourt {fcMean:F2}s; " +
                              $"oracle ~5.1 / ~15.2; not asserted)");
        }
        catch (Exception ex)
        {
            Check("season fixture ran for the turnover-clock guards", false, ex.Message);
        }

        Console.WriteLine($"  Phase 57: {(pass ? "ok" : "FAIL")}");
        return pass;
    }
}
