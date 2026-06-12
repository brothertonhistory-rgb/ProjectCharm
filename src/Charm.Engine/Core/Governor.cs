namespace Charm.Engine;

/// <summary>One resolved possession, recorded for validation and observability.</summary>
/// <param name="Number">The possession's monotonic id (the accounting anchor).</param>
/// <param name="Offense">Who had the ball this possession.</param>
/// <param name="Defense">Who defended (the other side).</param>
/// <param name="Entry">How this possession started (temp-routed through Roll A this session).</param>
/// <param name="EndedOnTerminal">True if it reached a terminal; false if it parked at a stub.</param>
/// <param name="EndLabel">The terminal reason, or "parked:{stub}".</param>
/// <param name="Applied">The consequence used to spawn the NEXT possession (the
/// terminal's own consequence, or the default flip on a park).</param>
public sealed record PossessionRecord(
    int Number,
    TeamSide Offense,
    TeamSide Defense,
    EntryType Entry,
    bool EndedOnTerminal,
    string EndLabel,
    PossessionConsequence Applied);

/// <summary>The result of a Governor run — everything the harness validates and prints.</summary>
/// <param name="Possessions">Every resolved possession, in order. Count == the cap.</param>
/// <param name="TerminalEnded">How many ended on a real terminal.</param>
/// <param name="Parked">How many parked at a stub (and flipped on the default consequence).</param>
/// <param name="TotalSeconds">Flat placeholder time accumulated (observability only).</param>
/// <param name="PerStubParks">Per-stub park breakdown: stub destination -> count. This
/// quantifies the FT / offensive-rebound / etc. volume still flowing through placeholder
/// flips — the point of printing it.</param>
public sealed record GovernorRunResult(
    IReadOnlyList<PossessionRecord> Possessions,
    int TerminalEnded,
    int Parked,
    double TotalSeconds,
    IReadOnlyDictionary<string, int> PerStubParks);

/// <summary>
/// The THIN Governor. It turns "resolve ONE possession" into "play a sequence of
/// possessions," and does nothing else. It owns the loop; it never picks a roll or
/// reaches inside a possession — it drops a START STATE at the top of the chain (via
/// <see cref="Resolver.RunPossession"/>) and reads what comes back.
///
/// <para>For each possession it asks the resolver to run it, then:</para>
/// <list type="bullet">
///   <item>If the possession ENDED ON A TERMINAL, it reads that terminal's
///   <see cref="PossessionConsequence"/> — who has the ball next and how that
///   possession starts.</item>
///   <item>If the possession PARKED at a stub (the resolver returns no terminal), it
///   applies the DEFAULT consequence: ball to the other team, dead-ball restart at
///   Roll A. This is deliberately wrong basketball (a parked FT possession should
///   resolve points and decide the next possession off the last free throw), kept
///   flat exactly like score = 0; it is replaced at this same seam when that pipe
///   resolves for real. The key property: this is ONE uniform path for EVERY stub
///   (keyed only on "no terminal"), so no per-stub branch exists to forget — the
///   Session-14 "only handled one landing" bug class cannot recur.</item>
/// </list>
///
/// <para>Either way it spawns the next possession (temp-routed through Roll A
/// regardless of the entry tag this session), increments the count, and loops until
/// the config'd possession cap. EVERY possession — terminal or parked — produces
/// exactly one next possession, so the count never leaks.</para>
///
/// <para>The cross-possession invariants it must NOT disturb — the possession arrow,
/// the team-foul counts, and the lineups — all live on the shared <see cref="GameState"/>
/// and persist automatically because the same resolver (holding the same game) runs
/// every possession. The Governor never resets or clobbers them; it only reaches the
/// score field, and only to write the placeholder 0.</para>
///
/// <para>PROVISIONAL (see design.md teardown contract): the flat clock, the zero
/// score, the possession-cap stop, the temp-route-all-to-Roll-A, and the
/// parked→default-flip rule. PERMANENT: the loop shape — read the consequence off the
/// terminal (or the default on a park) and spawn — which a real game layer swaps the
/// guts behind without touching the seam.</para>
/// </summary>
public sealed class Governor
{
    private readonly Resolver _resolver;
    private readonly GameState _game;
    private readonly GovernorConfig _cfg;

    public Governor(Resolver resolver, GameState game, GovernorConfig cfg)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
    }

    /// <summary>Play <see cref="GovernorConfig.PossessionCap"/> possessions starting
    /// from <paramref name="first"/>. Returns the full record for validation.</summary>
    public GovernorRunResult Run(PossessionState first)
    {
        var state = first;
        var records = new List<PossessionRecord>(_cfg.PossessionCap);
        var perStubParks = new Dictionary<string, int>();
        var terminalEnded = 0;
        var parked = 0;
        var totalSeconds = 0.0;

        for (var p = 0; p < _cfg.PossessionCap; p++)
        {
            var outcome = _resolver.RunPossession(state);

            PossessionConsequence consequence;
            bool endedOnTerminal;
            string endLabel;

            if (outcome.EndedOn is { } term)
            {
                // Ended on a terminal: read its consequence directly.
                endedOnTerminal = true;
                consequence = term.Consequence;
                endLabel = term.Reason;
                terminalEnded++;

                // Time: a terminal that carries an invariant elapsed (shot-clock /
                // backcourt violation) uses that real value; otherwise the flat
                // placeholder. (FiveSecondInbound carries 0.0 — non-null — so it
                // correctly contributes zero, not the placeholder.)
                totalSeconds += term.ElapsedSeconds ?? _cfg.SecondsPerPossession;
            }
            else
            {
                // Parked at a stub: no terminal, no consequence. Apply the DEFAULT —
                // ball to the other team, dead-ball restart at Roll A. One uniform
                // path for every stub.
                endedOnTerminal = false;
                consequence = PossessionConsequence.DeadBallTo(state.Defense);
                endLabel = $"parked:{outcome.Destination}";
                parked++;
                perStubParks[outcome.Destination] =
                    perStubParks.GetValueOrDefault(outcome.Destination) + 1;
                totalSeconds += _cfg.SecondsPerPossession;
            }

            // Score write: REAL field, PLACEHOLDER value. Exercises the path to the
            // GameState score (proving the seam) while the points derivation is still
            // future — so the value added is a literal 0, charged to the offense that
            // just played. When the scoring layer lands, this 0 becomes a real
            // (Result, ShotType) -> points derivation at this exact spot.
            const int pointsThisPossession = 0;
            if (state.Offense == TeamSide.Home) _game.HomeScore += pointsThisPossession;
            else _game.AwayScore += pointsThisPossession;

            records.Add(new PossessionRecord(
                state.PossessionNumber, state.Offense, state.Defense, state.Entry,
                endedOnTerminal, endLabel, consequence));

            // Spawn possession N+1 from the consequence: offense named by it, defense
            // the other side, number +1, entry the consequence's tag. Per-possession
            // facts (slot / zone / result) reset to null — a fresh possession. The
            // final iteration spawns a state that is never run (the loop exits), which
            // is harmless.
            var nextOffense = consequence.NextOffense;
            state = new PossessionState(
                PossessionNumber: state.PossessionNumber + 1,
                Offense: nextOffense,
                Defense: Other(nextOffense),
                Entry: consequence.NextEntry);
        }

        return new GovernorRunResult(records, terminalEnded, parked, totalSeconds, perStubParks);
    }

    private static TeamSide Other(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
}
