namespace Charm.Engine;

/// <summary>
/// The outcomes Roll C (turnover classification) can resolve to. This enum
/// defines the slices of Roll C's pie. Declaration order is significant:
/// <see cref="Pie{TOutcome}"/> walks slices in this order, so the same RNG draw
/// always maps to the same outcome (reproducibility).
///
/// The split is along the dead-ball vs. live-ball axis, because that is the
/// distinction that drives what the *next* possession looks like: a dead-ball
/// turnover resumes on an inbound (a future entry roll picks the spot), while a
/// live-ball turnover hands the defense the ball live, in transition. A future
/// entry roll and a future attribution layer both consume this classification;
/// Roll C only names the type and ends the possession.
///
/// Every outcome here is a TERMINAL — the possession is over and the ball
/// changes hands regardless of which slice lands. ShotClockViolation is NOT a
/// slice here: Roll A already terminates it (invariant, full clock), so
/// duplicating it would be wrong.
/// </summary>
public enum TurnoverOutcome
{
    /// <summary>Errant pass that sails out of bounds. Dead ball: the next
    /// possession resumes on an inbound. -> TERMINAL.</summary>
    BadPassDeadBall,

    /// <summary>Errant pass picked off by a defender. Live ball: the defense has
    /// it in transition. A steal is attributable to the defense (the attribution
    /// layer, not Roll C, decides which defender). -> TERMINAL.</summary>
    BadPassIntercepted,

    /// <summary>Ball lost off the dribble and knocked out of bounds. Dead ball:
    /// next possession resumes on an inbound. -> TERMINAL.</summary>
    LostBallDeadBall,

    /// <summary>Ball stripped off the dribble and corralled live by the defense.
    /// Live ball: defense in transition. A steal is attributable to the defense.
    /// -> TERMINAL.</summary>
    LostBallLiveBall,

    /// <summary>Charge, illegal screen, etc. Always a dead ball; no steal is
    /// credited to the defense. -> TERMINAL.</summary>
    OffensiveFoul,

    // --- Contextification #5a: the full set of ways a possession is lost
    //     WITHOUT a shot, seated here so Roll C is the single canonical loss
    //     node. APPENDED after OffensiveFoul so every existing RNG draw maps to
    //     the same outcome it did before (declaration order is significant). All
    //     new members are DORMANT this session: zero weight in every live context
    //     (Halfcourt, Transition), nothing routes to them yet. #5b turns them
    //     live by rewiring Roll A's loss exit and setting real Halfcourt weights.
    //
    //     Every new member is a DEAD-ball loss -> DeadBallTo(defense). The seven
    //     turnover types defer elapsed time (null) like the existing turnovers;
    //     the three violation types carry their own INVARIANT elapsed (the only
    //     arms in Roll C that stamp time), mirroring Roll A's violation terminals.

    /// <summary>Travel. Dead ball; next possession resumes on an inbound.
    /// -> TERMINAL.</summary>
    Travel,

    /// <summary>Double dribble. Dead ball. -> TERMINAL.</summary>
    DoubleDribble,

    /// <summary>Carry / palming. Dead ball. -> TERMINAL.</summary>
    Carry,

    /// <summary>Three-second violation (offensive player in the lane too long).
    /// Dead ball. -> TERMINAL.</summary>
    ThreeSecondViolation,

    /// <summary>Five-second closely-guarded violation (NCAA men's: ball-handler
    /// held in the frontcourt with a defender within six feet). A settled-set
    /// type. Dead ball. -> TERMINAL.</summary>
    FiveSecondCloselyGuarded,

    /// <summary>Offensive goaltending / offensive basket interference — the
    /// basket is waved off and the ball goes to the defense. Dead ball.
    /// -> TERMINAL. (DEFENSIVE goaltending is NOT here: it AWARDS the basket, so
    /// it belongs to the make/miss roll, not the loss node.)</summary>
    OffensiveGoaltending,

    /// <summary>Backcourt violation / over-and-back — the offense returns the
    /// ball to the backcourt after establishing the frontcourt. Dead ball.
    /// -> TERMINAL. A halfcourt-phase type: only possible once the frontcourt is
    /// established, never on the entry itself.</summary>
    BackcourtViolation,

    /// <summary>Shot-clock violation. Dead ball. -> TERMINAL. INVARIANT elapsed:
    /// the full shot clock (30s). As of Contextification #6 this is the SOLE home of
    /// a shot-clock violation — Roll A's old twin terminal was retired and its loss
    /// now routes here via the Turnover exit's EntryBackcourt context.</summary>
    ShotClockViolation,

    /// <summary>Five-second inbound violation (failure to inbound in time). Dead
    /// ball. -> TERMINAL. INVARIANT elapsed: ZERO (the clock never started). As of #6
    /// this is the SOLE home of the violation; Roll A's old twin terminal was retired
    /// (its loss routes here via the EntryBackcourt context).</summary>
    FiveSecondInbound,

    /// <summary>Ten-second backcourt violation (failure to advance past the
    /// division line in time). Dead ball. -> TERMINAL. INVARIANT elapsed: a fixed
    /// 10s (the count ran before the whistle). As of #6 this is the SOLE home of the
    /// violation; Roll A's old twin terminal was retired (its loss routes here via the
    /// EntryBackcourt context).</summary>
    TenSecondBackcourt
}

/// <summary>
/// The CONTEXT TICKET a turnover carries into Roll C — the within-possession ticket
/// memory that selects WHICH turnover pie Roll C uses. The first instance of the
/// ticket/station mechanism: a feeding station stamps this on the <see cref="Continue"/>
/// that routes to <see cref="ContinuationKind.ResolveTurnoverType"/>, and Roll C's
/// generator reads it to pick a parameter set. Roll C NEVER queries who fed it —
/// "many feeders, one node," now with route-specific weights.
///
/// Declaration order is significant: <see cref="Halfcourt"/> is FIRST so it is the
/// default/legacy context. The ticket is carried as an optional payload
/// (<see cref="Continue.TurnoverContext"/>); a null/absent stamp reads as
/// <see cref="Halfcourt"/>, so EVERY existing feeder (Roll A, Roll B, Roll F),
/// which stamps nothing, keeps today's exact behavior byte-for-byte.
/// </summary>
public enum TurnoverContext
{
    /// <summary>A turnover in a settled halfcourt possession — the default/legacy
    /// context (the unchanged 30/22/18/20/10 pie). Every pre-Roll-J feeder lands
    /// here by stamping nothing.</summary>
    Halfcourt,

    /// <summary>A turnover on a transition outlet/push — stamped by Roll J's
    /// <see cref="TransitionOutcome.Turnover"/> arm. Selects Roll C's transition pie:
    /// more often LIVE and going the other way (live strips up, offensive fouls down).</summary>
    Transition,

    /// <summary>A loss while bringing the ball up from a backcourt start — the
    /// post-made-basket / inbound-in-the-backcourt phase. Selects Roll C's
    /// entry/backcourt pie, where the backcourt-only losses live: the 5-second
    /// inbound, 10-second backcourt, backcourt shot-clock, plus a bad pass / lost
    /// ball on the way up. Added DORMANT in Contextification #5a: nothing routes
    /// here yet. #5b's Roll A reshape stamps this on its loss exit, selecting the
    /// context by inbound origin (made basket vs. foul past halfcourt).</summary>
    EntryBackcourt
}
