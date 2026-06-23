namespace Charm.Engine;

/// <summary>
/// A test-only hook for observing fatigue bookkeeping WITHOUT putting counters on the
/// gameplay surface. The real game constructs the tracker without one (the constructor
/// parameter defaults to <c>null</c>); only harness checks pass an implementation, to
/// verify the exactly-once-per-possession accrual contract and the fires-once-at-halftime
/// contract. Consumes no RNG and is absent from every production path.
/// </summary>
public interface IFatigueObserver
{
    /// <summary>Called once each time a single player accrues one possession of fatigue.</summary>
    void OnAccrue(int playerId);

    /// <summary>Called once each time halftime recovery is applied.</summary>
    void OnHalftimeRecovery();
}

/// <summary>
/// Owns per-player fatigue — the engine's FIRST piece of state that REMEMBERS at the
/// individual level across possessions (team fouls remember per-team; this remembers per
/// player). Structurally mirrors <see cref="FoulTracker"/>: a dedicated class held on
/// <see cref="GameState"/>, constructed with its config, mutated through methods, read
/// through an accessor. It differs only in STORAGE — fatigue is per-player, so the level
/// lives in a PlayerId-keyed dictionary rather than two team ints.
///
/// <para><b>What it models — this session the METER ONLY; nothing reads the level yet.</b>
/// A player's fatigue LEVEL rises while he is on the floor (a convex "trickle then cliff"
/// curve), falls when he rests or at halftime (fast but partial), and is scaled in both
/// directions by his Endurance. The future athleticism-effect session reads
/// <see cref="LevelFor"/> to discount effective athleticism; until then this changes no
/// game outcome.</para>
///
/// <para><b>Determinism.</b> Every operation is a pure function of (current level, the
/// player's authored Endurance, the elapsed/possession input). It draws no randomness, so
/// a replayed seed reproduces the meter trajectory exactly and the meter perturbs no other
/// RNG stream.</para>
///
/// <para><b>Empty construction.</b> Holds no entries until a player first accrues or
/// recovers; <see cref="LevelFor"/> returns 0.0 (fresh) for any unseen PlayerId. No roster
/// knowledge is baked in — reserves and substitutions will materialize naturally when they
/// first take the floor.</para>
/// </summary>
public sealed class FatigueTracker
{
    private readonly FatigueConfig _cfg;
    private readonly IFatigueObserver? _observer;

    // PlayerId -> current fatigue level in [0, Ceiling]. An absent key means fresh (0.0).
    private readonly Dictionary<int, double> _level = new();

    /// <param name="cfg">The fatigue curve/recovery knobs (placeholder magnitudes this
    /// session; the SHAPE is locked).</param>
    /// <param name="observer">Test-only accrual/halftime counter. The game constructs
    /// without one; only harness checks pass an implementation. Default <c>null</c>.</param>
    public FatigueTracker(FatigueConfig cfg, IFatigueObserver? observer = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _observer = observer;
    }

    /// <summary>The current fatigue level for <paramref name="playerId"/> (0 = fresh,
    /// rising toward <see cref="FatigueConfig.Ceiling"/>). Returns 0.0 for any player who
    /// has not yet accrued — the accessor the future athleticism-effect session reads.</summary>
    public double LevelFor(int playerId) =>
        _level.TryGetValue(playerId, out var f) ? f : 0.0;

    /// <summary>
    /// Accrue exactly ONE possession of on-floor fatigue for each player in
    /// <paramref name="onFloor"/>. The step is convex: small when fresh (a trickle), larger
    /// as the level rises (the cliff) — so the meter ITSELF carries the trickle-then-cliff
    /// shape. Step size scales with (low) Endurance: a lower-Endurance player takes bigger
    /// steps and reaches the cliff in fewer possessions. Null entries are skipped (an absent
    /// slot accrues nothing). Pace enters PURELY through how often this is called — once per
    /// top-level possession — not through any per-second or per-player-effort term.
    /// </summary>
    public void Accrue(IReadOnlyList<Player?> onFloor)
    {
        for (var i = 0; i < onFloor.Count; i++)
        {
            var p = onFloor[i];
            if (p is null) continue;   // absent slot accrues nothing (defensive; fixed lineups have none)

            var f      = LevelFor(p.PlayerId);
            var fNorm  = f / _cfg.Ceiling;
            var deltaF = _cfg.BaseDrain
                       * DrainFactor(p.Endurance)
                       * (1.0 + _cfg.Convexity * Math.Pow(fNorm, _cfg.Exponent));
            _level[p.PlayerId] = Math.Min(_cfg.Ceiling, f + deltaF);

            _observer?.OnAccrue(p.PlayerId);
        }
    }

    /// <summary>
    /// Recover each player in <paramref name="players"/> as a function of ELAPSED
    /// game-clock seconds off the floor — NOT possession count (design call D6: a fast game
    /// must not over-restore a benched player). Multiplicative decay of the current level:
    /// fast but partial — a heavily-fatigued player drops meaningfully, but a short rest
    /// never returns him to fresh, and the level never crosses below zero. Endurance scales
    /// recovery (higher Endurance recovers more). No in-game off-floor recovery runs this
    /// session (no subs); the signature takes elapsed seconds NOW so the meter never bakes in
    /// a pace-undoes-rest relationship.
    /// </summary>
    public void Recover(IReadOnlyList<Player?> players, double elapsedSeconds) =>
        ApplyRecovery(players, elapsedSeconds);

    /// <summary>
    /// The halftime recovery event: a large rest applied to everyone through the EXACT SAME
    /// multiplicative mechanism as <see cref="Recover"/> — a large rest-equivalent
    /// (<see cref="FatigueConfig.HalftimeRestEquivalentSeconds"/>), not a separate rule. The
    /// partial + Endurance-scaled behavior makes the most-gassed retain residual fatigue
    /// automatically (no carve-out). Fired by the Governor ONLY at the regulation
    /// half-1 -> half-2 boundary — never after the final regulation half, never in OT.
    /// </summary>
    public void ApplyHalftimeRecovery(IReadOnlyList<Player?> players)
    {
        ApplyRecovery(players, _cfg.HalftimeRestEquivalentSeconds);
        _observer?.OnHalftimeRecovery();
    }

    // The single recovery primitive. Both Recover and ApplyHalftimeRecovery route through
    // here, so halftime can never drift into a separately-expressed equation.
    private void ApplyRecovery(IReadOnlyList<Player?> players, double elapsedSeconds)
    {
        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p is null) continue;

            var f = LevelFor(p.PlayerId);
            if (f <= 0.0) { _level[p.PlayerId] = 0.0; continue; }   // fresh stays fresh

            var multiplier = 1.0 - _cfg.RecoveryRate * RecoveryFactor(p.Endurance) * elapsedSeconds;
            multiplier = Math.Clamp(multiplier, 0.0, 1.0);
            _level[p.PlayerId] = f * multiplier;
        }
    }

    // --- Endurance conversion (authored int -> bounded, monotone multipliers) -------------
    // Engine-standard /100.0 normalization, clamped to [0,1]: a raw 80 becomes 0.80.

    // Lower Endurance -> larger drain. Bounded [1, 1 + DrainEnduranceSensitivity]: 1.0 at
    // max Endurance, the maximum at min Endurance. Strictly decreasing in Endurance.
    private double DrainFactor(int endurance)
    {
        var e = Math.Clamp(endurance / 100.0, 0.0, 1.0);
        return 1.0 + _cfg.DrainEnduranceSensitivity * (1.0 - e);
    }

    // Higher Endurance -> larger recovery. Bounded [1, 1 + RecoveryEnduranceSensitivity]:
    // 1.0 at min Endurance, the maximum at max Endurance. Strictly increasing in Endurance.
    private double RecoveryFactor(int endurance)
    {
        var e = Math.Clamp(endurance / 100.0, 0.0, 1.0);
        return 1.0 + _cfg.RecoveryEnduranceSensitivity * e;
    }
}
