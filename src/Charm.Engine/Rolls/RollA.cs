namespace Charm.Engine;

/// <summary>
/// Roll A — Entry: Inbounds (Dead Ball). The first node of the engine and the
/// template every later roll follows: receive state + a finished pie, roll
/// against the pie, return exactly one typed result, name no successor.
/// </summary>
public static class RollA
{
    public static RollResult Execute(PossessionState state, Pie<EntryOutcome> pie, IRng rng, RollAConfig cfg)
    {
        // The pie is valid by construction (see Pie&lt;T&gt;), so receiving it is
        // already the "validate on receipt" guarantee — an invalid pie could not
        // have been built and handed here.
        var draw = rng.NextUnitInterval();
        var outcome = pie.Roll(draw);

        return outcome switch
        {
            // Clean entry -> CONTINUE. The halfcourt set is opaque to Roll A.
            EntryOutcome.CleanEntry =>
                new Continue(ContinuationKind.IntoHalfcourtSet, state),

            // Turnover -> CONTINUE. The turnover-type resolver is opaque to Roll A.
            EntryOutcome.Turnover =>
                new Continue(ContinuationKind.ResolveTurnoverType, state),

            // Shot-clock violation -> TERMINAL. Elapsed time is invariant (full
            // clock), so it is stamped here and needs no separate time roll.
            EntryOutcome.ShotClockViolation =>
                new Terminal("ShotClockViolation", state) { ElapsedSeconds = cfg.ViolationElapsedSeconds },

            // 5-second inbound violation -> TERMINAL. The inbound never came in,
            // so the clock never started: elapsed time is ZERO, stamped here.
            EntryOutcome.FiveSecondInbound =>
                new Terminal("FiveSecondInbound", state) { ElapsedSeconds = 0.0 },

            // 10-second backcourt violation -> TERMINAL. The count ran before the
            // whistle, so a fixed 10 seconds elapsed, stamped here.
            EntryOutcome.TenSecondBackcourt =>
                new Terminal("TenSecondBackcourt", state) { ElapsedSeconds = cfg.TenSecondElapsedSeconds },

            // Foul -> CONTINUE. A future foul-type resolver decides defensive vs.
            // offensive and what it triggers. Real variance, never resolved here.
            EntryOutcome.Foul =>
                new Continue(ContinuationKind.ResolveFoulType, state),

            // Jump ball -> CONTINUE. A future jump-ball resolver consults the
            // possession arrow on GameState.
            EntryOutcome.JumpBall =>
                new Continue(ContinuationKind.ResolveJumpBall, state),

            _ => throw new InvalidOperationException($"Unhandled entry outcome '{outcome}'.")
        };
    }
}
