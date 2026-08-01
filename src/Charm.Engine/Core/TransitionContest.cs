namespace Charm.Engine;

/// <summary>
/// S88 — the contest on a fast break: WHO got back, HOW SET he is, and HOW MANY of them made
/// it. Built once per in-scope break shot by the Resolver and handed to Roll H's generator,
/// which reads it for BOTH the make rate and the block rate.
///
/// <para><b>Not to be confused with <see cref="TransitionContext"/></b>, which is the ticket a
/// possession carries about HOW it began (rebound, free-throw rebound, steal). This is the
/// defensive contest on one shot. Different question, adjacent name, deliberately kept
/// apart.</para>
///
/// <para><b>Why it exists: the contested man and the credited man must be the same person.</b>
/// The Resolver is the only place where both the shot pie and the block credit are in scope,
/// so it owns the draw. It snapshots the defensive lineup ONCE, computes the weights from that
/// snapshot, draws exactly one defender, and carries the resulting man here. Roll H must never
/// recompute the weights and never re-resolve the player from a fresh lineup read — a slot that
/// survives while the player behind it is looked up again from a later snapshot would put one
/// man's attributes into the pie and credit a different man for the block, and every
/// conservation check in the suite would still pass. Carrying the <see cref="Player"/> itself,
/// rather than only his seat, is what makes that failure impossible rather than merely
/// unlikely. (<see cref="Player"/> is immutable — every rating is set at construction and there
/// is no setter anywhere on it — so the snapshot needs no defensive copy.)</para>
///
/// <para><b>Null means "not an in-scope break".</b> Roll H's parameter is a defaulted null, and
/// null is the whole signal: a halfcourt shot, a break putback, a post-reset shot, or a break
/// with nobody on the floor to defend it. There is deliberately no numeric sentinel to
/// misread.</para>
/// </summary>
/// <param name="DefenderSlot">The seat of the man who got back — a full <see cref="Slot"/>, so
/// the side is explicit and can never be inferred wrongly downstream. This is the slot credited
/// if the shot comes back blocked.</param>
/// <param name="Defender">THE MAN, taken from the exact snapshot the weights were computed
/// from. His ratings are what enter the make and block rates.</param>
/// <param name="DefenderGotBack">His own got-back number — how set he is when he arrives
/// (job 2), which scales his own contest.</param>
/// <param name="TeamAggregate">How many of them got back (job 3) — the emergent per-man
/// aggregate over the defenders on the floor, exactly 1.0 for five average men against an
/// average offence. The dominant channel on conversion.</param>
public sealed record TransitionContest(
    Slot   DefenderSlot,
    Player Defender,
    double DefenderGotBack,
    double TeamAggregate);

/// <summary>
/// S88, PAGE-ONLY — one in-scope break shot's transition observation. The observation unit
/// the season page reports on: one per break shot that actually ran the got-back model
/// (putbacks and post-reset shots excluded, press-born breaks included).
///
/// <para>Carries the team aggregate that shot faced plus how it finished, so the page can
/// report the league mean, the by-team band, and break FG% / block rate BY got-back bin
/// without re-deriving anything. Nothing here is ever asserted — every number it feeds is a
/// calibration placeholder living on the page.</para>
/// </summary>
/// <param name="TeamAggregate">How many of the defence got back on this shot.</param>
/// <param name="DefenderGotBack">The drawn defender's own got-back number.</param>
/// <param name="DefenderRimProtection">His rim protection, for the R4 sanity line.</param>
/// <param name="Made">True if the shot went in.</param>
/// <param name="Blocked">True if it came back blocked.</param>
public readonly record struct BreakContestObservation(
    double TeamAggregate,
    double DefenderGotBack,
    int    DefenderRimProtection,
    bool   Made,
    bool   Blocked);
