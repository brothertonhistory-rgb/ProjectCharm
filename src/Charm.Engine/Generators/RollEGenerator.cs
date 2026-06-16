namespace Charm.Engine;

/// <summary>
/// Real, attribute-driven Roll E generator (Phase 15). Replaces the flat 20%
/// halfcourt pie with a usage-weighted selection pie: a better scorer/creator
/// naturally commands a larger share of shot attempts; a Rodman-type falls to
/// a floor; five comparable players stay near-even.
///
/// <para><b>FastBreak passthrough — unchanged.</b> When
/// <see cref="PossessionState.FastBreak"/> is true, the generator returns the
/// existing transition pie (cfg.TransitionSlot1..5) byte-for-byte — exactly
/// what <see cref="RollEStubPieGenerator"/> does today. Only the halfcourt
/// branch is attribute-driven.</para>
///
/// <para><b>Usage score formula.</b>
/// score = 0.35 * SelfCreation
///       + 0.30 * (Close + PostMoves) / 2
///       + 0.35 * (Outside + Mid + Finishing) / 3
/// All inputs are 0–99 authored attributes. The (Close + PostMoves) / 2 term
/// gives a high-post-moves, high-close center meaningful usage even when his
/// SelfCreation and perimeter numbers are modest — Emmett's basketball call.
/// Passing, Playmaking, BallHandling, and BasketballIQ are intentionally
/// excluded (Roll E is who TAKES the shot, not who RUNS the offense).</para>
///
/// <para><b>Tilt: sharpening exponent.</b> Raw scores are raised to
/// cfg.UsageExponent before normalizing. At exponent = 1 the distribution
/// is proportional to raw scores (mild tilt); higher exponents sharpen the
/// gap between players. At the calibrated default a realistic D1 alpha lands
/// ~35% and a Rodman-type is held by the floor.</para>
///
/// <para><b>Floor and rail — hard constraints via constrained redistribution.</b>
/// The floor (cfg.UsageFloor) is a guaranteed minimum for every populated slot;
/// the rail (cfg.UsageRail) is a hard cap on any single slot. Both are enforced
/// by iterative constrained redistribution (water-filling), not naive double-
/// renormalization — naive renorm can push a low slot back under the floor after
/// the rail clamps a high one. See <c>ApplyFloorAndRail</c>.</para>
///
/// <para><b>Null-slot handling.</b> Null / unpopulated slots receive 0.0 and can
/// never be selected. Floor and rail apply only to populated slots. A real game
/// always has all five offense slots populated; the partial and empty paths are
/// test-only guards.</para>
///
/// <para><b>Rail feasibility standdown.</b> When
/// populatedCount * cfg.UsageRail &lt; 1.0 (only possible with a thin test
/// roster — never in a real five-man game), the rail is skipped entirely for
/// that call.</para>
///
/// Implements <see cref="IRollEPieGenerator"/>.
/// </summary>
public sealed class RollEGenerator : IRollEPieGenerator
{
    private readonly RollEConfig _cfg;
    private readonly GameState   _game;

    public RollEGenerator(RollEConfig cfg, GameState game)
    {
        _cfg  = cfg  ?? throw new ArgumentNullException(nameof(cfg));
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    public Pie<SelectionOutcome> Generate(PossessionState state)
    {
        // ── FastBreak passthrough — byte-for-byte stub behaviour ────────────
        if (state.FastBreak)
        {
            var transWeights = new Dictionary<SelectionOutcome, double>
            {
                [SelectionOutcome.Slot1] = _cfg.TransitionSlot1,
                [SelectionOutcome.Slot2] = _cfg.TransitionSlot2,
                [SelectionOutcome.Slot3] = _cfg.TransitionSlot3,
                [SelectionOutcome.Slot4] = _cfg.TransitionSlot4,
                [SelectionOutcome.Slot5] = _cfg.TransitionSlot5,
            };
            return new Pie<SelectionOutcome>(transWeights, _cfg.Epsilon);
        }

        // ── Read five offense players (Roll B access shape) ─────────────────
        var offRoster = _game.RosterFor(state.Offense);
        var offLineup = _game.LineupFor(state.Offense);

        var players = new Player?[]
        {
            offRoster.PlayerAt(offLineup.SlotAt(1)),
            offRoster.PlayerAt(offLineup.SlotAt(2)),
            offRoster.PlayerAt(offLineup.SlotAt(3)),
            offRoster.PlayerAt(offLineup.SlotAt(4)),
            offRoster.PlayerAt(offLineup.SlotAt(5)),
        };

        // ── Fallback: zero populated slots → flat Base* pie ─────────────────
        var populated = 0;
        foreach (var p in players) if (p is not null) populated++;

        if (populated == 0)
        {
            var baseWeights = new Dictionary<SelectionOutcome, double>
            {
                [SelectionOutcome.Slot1] = _cfg.BaseSlot1,
                [SelectionOutcome.Slot2] = _cfg.BaseSlot2,
                [SelectionOutcome.Slot3] = _cfg.BaseSlot3,
                [SelectionOutcome.Slot4] = _cfg.BaseSlot4,
                [SelectionOutcome.Slot5] = _cfg.BaseSlot5,
            };
            return new Pie<SelectionOutcome>(baseWeights, _cfg.Epsilon);
        }

        // ── Compute raw usage scores ─────────────────────────────────────────
        // Null slots get score 0.0 — they are excluded from all subsequent math.
        var rawScores = new double[5];
        for (var i = 0; i < 5; i++)
        {
            if (players[i] is Player p)
            {
                var score = p.SelfCreation * 0.35
                          + (p.Close + p.PostMoves) / 2.0 * 0.30
                          + (p.Outside + p.Mid + p.Finishing) / 3.0 * 0.35;
                rawScores[i] = Math.Max(score, _cfg.MinUsageScore);
            }
            // else stays 0.0
        }

        // ── Apply sharpening exponent ────────────────────────────────────────
        var expScores = new double[5];
        var expTotal  = 0.0;
        for (var i = 0; i < 5; i++)
        {
            if (rawScores[i] > 0.0)
            {
                expScores[i] = Math.Pow(rawScores[i], _cfg.UsageExponent);
                expTotal    += expScores[i];
            }
        }

        // ── Normalize to initial shares ──────────────────────────────────────
        var shares = new double[5];
        for (var i = 0; i < 5; i++)
            shares[i] = expTotal > 0.0 ? expScores[i] / expTotal : 0.0;

        // ── Apply floor + rail (hard constraints, constrained redistribution) ─
        shares = ApplyFloorAndRail(shares, expScores, populated);

        // ── Build the pie ────────────────────────────────────────────────────
        var outcomes = Enum.GetValues<SelectionOutcome>();
        var weights  = new Dictionary<SelectionOutcome, double>();
        for (var i = 0; i < 5; i++)
            weights[outcomes[i]] = shares[i];

        return new Pie<SelectionOutcome>(weights, _cfg.Epsilon);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constrained redistribution (water-filling)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enforces floor and rail as hard constraints via iterative constrained
    /// redistribution. Null slots (rawExpScores[i] == 0) remain 0 and are
    /// excluded from all constraint logic.
    ///
    /// <para><b>Floor pass:</b> Pins every populated slot at or above
    /// cfg.UsageFloor. Excess mass is redistributed proportionally (by raw
    /// exponentiated score) among above-floor slots. Iterates until stable.</para>
    ///
    /// <para><b>Rail pass:</b> Clamps any slot above cfg.UsageRail down to the
    /// rail. Excess is redistributed proportionally among non-railed slots,
    /// respecting the floor. Iterates until stable. Rail is skipped entirely
    /// when populatedCount * rail &lt; 1.0 (mathematically infeasible for thin
    /// test rosters).</para>
    /// </summary>
    private double[] ApplyFloorAndRail(double[] shares, double[] expScores, int populatedCount)
    {
        var s = (double[])shares.Clone();
        var floor = _cfg.UsageFloor;
        var rail  = _cfg.UsageRail;

        // ── Floor: iterative constrained redistribution ──────────────────────
        for (var iter = 0; iter < 50; iter++)
        {
            // Identify slots pinned at floor
            var pinned = new bool[5];
            var pinnedCount = 0;
            for (var i = 0; i < 5; i++)
            {
                if (expScores[i] > 0.0 && s[i] <= floor)
                {
                    pinned[i] = true;
                    pinnedCount++;
                }
            }

            if (pinnedCount == 0) break;  // all above floor — stable

            var floorMass = floor * pinnedCount;
            var freeMass  = 1.0 - floorMass;
            if (freeMass <= 0.0) break;   // infeasible guard (config invariant prevents this)

            var freeExpTotal = 0.0;
            for (var i = 0; i < 5; i++)
                if (expScores[i] > 0.0 && !pinned[i]) freeExpTotal += expScores[i];

            if (freeExpTotal <= 0.0) break;

            var changed = false;
            for (var i = 0; i < 5; i++)
            {
                double newVal;
                if (expScores[i] <= 0.0)
                    newVal = 0.0;  // null slot: stay 0
                else if (pinned[i])
                    newVal = floor;
                else
                    newVal = freeMass * (expScores[i] / freeExpTotal);

                if (Math.Abs(newVal - s[i]) > 1e-12) changed = true;
                s[i] = newVal;
            }
            if (!changed) break;
        }

        // ── Rail: water-filling (skip if infeasible for thin test rosters) ───
        var useRail = (populatedCount * rail >= 1.0);
        if (!useRail) return s;

        for (var iter = 0; iter < 20; iter++)
        {
            var anyOver = false;
            var excess  = 0.0;
            var railed  = new bool[5];
            for (var i = 0; i < 5; i++)
            {
                if (s[i] > rail)
                {
                    excess   += s[i] - rail;
                    s[i]      = rail;
                    railed[i] = true;
                    anyOver   = true;
                }
            }
            if (!anyOver) break;

            // Distribute excess to non-railed populated slots proportionally,
            // clamping to floor.
            var freeExpTotal = 0.0;
            for (var i = 0; i < 5; i++)
                if (expScores[i] > 0.0 && !railed[i]) freeExpTotal += expScores[i];

            for (var i = 0; i < 5; i++)
            {
                if (expScores[i] <= 0.0 || railed[i]) continue;
                s[i] += freeExpTotal > 0.0
                    ? excess * (expScores[i] / freeExpTotal)
                    : excess / (populatedCount - railed.Count(r => r));
                s[i] = Math.Max(s[i], floor);
            }

            // Renormalize to restore exact sum = 1
            var total = 0.0;
            foreach (var v in s) total += v;
            if (total > 0.0)
                for (var i = 0; i < 5; i++) s[i] /= total;
        }

        return s;
    }
}
