namespace Charm.Engine;

/// <summary>
/// Roll L — Free-Throw Resolution. The engine's purest primitive: one free-throw
/// attempt against a flat <see cref="FreeThrowOutcome.Make"/> / <see
/// cref="FreeThrowOutcome.Miss"/> pie, spun once per attempt. Every trip to the
/// line — and-1, fouled two, fouled three, bonus 1-and-1, double bonus — lands
/// here; it is the "many feeders, one node" terminal for free throws, closing the
/// two longest-standing parked FT stubs (Roll H's shooting fouls and the Roll
/// D/I/J/K bonus fork) at once.
///
/// <para>It deliberately BREAKS the usual roll contract in one way: it returns a
/// bare <see cref="FreeThrowOutcome"/>, NOT a <see cref="RollResult"/>. A free throw
/// is context-free — the make% is identical whether the trip came from an and-1, a
/// fouled three, or a bonus foul — so Roll L reads NO state, takes NO ticket, names
/// NO successor, and selects NO parameter set. It is just "does this attempt go in."
/// All sequencing (how many spins, whether the last spin is live, where a live miss
/// goes) is plain loop arithmetic the <see cref="Resolver"/> already owns and reads
/// at the entry edge — never a stamp Roll L sees. Modelling that here, as a
/// context-consuming node, would invent the very wire the design rejects.</para>
///
/// <para>What it does NOT do: it counts no shots, sums no score (a make is 1 point —
/// a downstream derivation for the future scoring pass, exactly like a field goal's
/// 2/3), charges no fouls, touches no arrow, and resolves no rebound. It produces
/// the make/miss FACT and nothing else.</para>
///
/// <para>The attribute seam is the most DIRECT in the engine — a literal 1:1 from
/// the shooter's FT rating — but it lives in the deferred generator (which will read
/// the carried shooter slot), not here. This skeleton reads only its finished pie.
/// Roll L itself never changes when that generator lands.</para>
/// </summary>
public static class RollL
{
    /// <summary>Spin the make/miss pie once and return the outcome. Context-free:
    /// no state, no routing — the resolver's FT-sequence driver interprets the
    /// result against the trip's shot count.</summary>
    public static FreeThrowOutcome Execute(Pie<FreeThrowOutcome> pie, IRng rng) =>
        pie.Roll(rng.NextUnitInterval());
}
