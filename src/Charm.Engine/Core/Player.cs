namespace Charm.Engine;

/// <summary>
/// A rated player: the fill that occupies a <see cref="Slot"/> on a
/// <see cref="Roster"/>. Carries the full authored attribute set from
/// <c>attributes.md</c> plus the derived values the engine computes from them.
///
/// <para><b>Authored vs. derived — the load-bearing line.</b> Authored attributes
/// are integers typed per player (0–99 scale). Derived attributes are computed
/// properties; they are NEVER authored and NEVER stored — the engine recomputes
/// them on read from the authored values they depend on. This mirrors the
/// stats-layer discipline: primitives stored, derived computed on read. A derived
/// value that depends on another derived value (transition depends on athleticism,
/// which is already derived) is fine as long as the dependency order is explicit
/// here — nothing cycles.</para>
///
/// <para><b>Scale: 0–99 integer.</b> 99 is the ceiling; 100 is impossible. This
/// holds for free throws too: a 99-rated free-throw shooter is historically elite,
/// not infallible. The 1:1 calibration note in <c>attributes.md</c> (a 72 makes
/// ~72%) is a rough Phase-2 anchor, not a hard formula — the real mapping is a
/// bounded logistic, tuned in Phase 6.</para>
///
/// <para><b>Wiring status.</b> All authored attributes are carried and validated
/// on construction. Live-on-arrival attributes are ready to be consumed the moment
/// a real generator replaces its stub. Dormant-pending-module attributes
/// (Endurance, Gravity, Spacing, HelpDefense, OffBallDefense, Transition) are real
/// fields on this object — authored or computed — but no generator reads them yet.
/// They sit here as proven, occupied seats, not future placeholders.</para>
///
/// <para><b>Phase 1 wall: NO pie wiring.</b> This object is data only. No
/// generator reads it yet; that is Phase 2. The seam from
/// <c>GameState.RosterFor(side).PlayerAt(slot)</c> to this object exists and
/// resolves end to end; nothing on the roll side is wired to it.</para>
/// </summary>
public sealed class Player
{
    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <param name="name">Display name. Used by the harness and almanac; not an
    /// engine input to any roll.</param>
    public Player(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    public string Name { get; }

    /// <summary>Stable numeric identity within the frozen observation corpus. Never
    /// read by any engine roll. Used by the attribution layer to index per-player stat
    /// arrays. 0 = unset sentinel. No global allocation policy exists yet — IDs are
    /// assigned sequentially by the harness when constructing the frozen-corpus roster.
    /// Uniqueness is the harness's responsibility, not the engine's.</summary>
    public int PlayerId { get; init; }

    // -------------------------------------------------------------------------
    // Offense — authored individual
    // -------------------------------------------------------------------------

    /// <summary>Converting close-range looks (inside the paint, not at the rim).
    /// One of three distance buckets; rim conversion is <see cref="Finishing"/>.
    /// </summary>
    public int Close { get; init; }

    /// <summary>Mid-range shooting (elbow, short corner, pull-up).</summary>
    public int Mid { get; init; }

    /// <summary>Perimeter shooting — threes and long twos folded into one bucket.
    /// Corner vs. above-the-break is a future slice-split; one bucket now.
    /// This is also the per-player input to <see cref="SpacingContribution"/>.
    /// </summary>
    public int Outside { get; init; }

    /// <summary>Converting rim attempts. Distinct from raw athleticism (which gates
    /// HOW HIGH you finish) and from "and-1 ability" (finishing THROUGH contact,
    /// which the MadeAndFouled outcome handles separately).</summary>
    public int Finishing { get; init; }

    /// <summary>Free-throw conversion. The cleanest 1:1 in the model: a 72-rated
    /// shooter converts roughly 72% per attempt, no gravity or matchup term.</summary>
    public int FreeThrow { get; init; }

    /// <summary>
    /// The skill of generating shooting fouls — initiating contact, going up
    /// strong, selling the call. Dominant on contact-heavy zones (Rim, Short),
    /// nearly inert on perimeter (Three, Long) where shooting fouls are rare.
    /// Read against the defender's <see cref="Discipline"/> in
    /// <see cref="Matchup.FoulRate"/> with an offense-dominant weight pair.
    ///
    /// <para><b>Asymmetry (Emmett's basketball call).</b> Low FoulDrawing is NOT
    /// a skill — it's absence of opportunity. The model encodes this through
    /// per-zone foul floors close to baselines (see MatchupConfig.FoulFloor),
    /// NOT by lowering this attribute's effect to zero. A 50-rated FoulDrawing
    /// is "average opportunity for his role," not "actively bad at it."</para>
    /// </summary>
    public int FoulDrawing { get; init; }

    // -------------------------------------------------------------------------
    // Phase 9 — per-zone shot tendencies (authored, 0–99)
    // -------------------------------------------------------------------------

    /// <summary>
    /// How often this player wants to take rim attempts when given the choice.
    /// Authored, 0–99, INDEPENDENT of <see cref="Finishing"/> (which governs
    /// CONVERSION at the rim). Klay Thompson and Steph Curry can have similar
    /// three-point conversion skill but very different ThreeTendency values — the
    /// shot mix the engine produces reflects what a player WANTS to take, not
    /// just what they're good at.
    ///
    /// <para><b>Phase 9 read:</b> RimTendency is one of the five per-zone
    /// tendency baselines RollGGenerator normalizes and bends by the matchup
    /// before producing the shot pie.</para>
    /// </summary>
    public int RimTendency { get; init; }

    /// <summary>How often this player wants to take short attempts (floaters,
    /// runners, hooks inside the paint but not at the rim). See
    /// <see cref="RimTendency"/> for the skill/tendency-are-independent rule.</summary>
    public int ShortTendency { get; init; }

    /// <summary>How often this player wants to take mid-range attempts. See
    /// <see cref="RimTendency"/>.</summary>
    public int MidTendency { get; init; }

    /// <summary>How often this player wants to take long-two attempts. See
    /// <see cref="RimTendency"/>.</summary>
    public int LongTendency { get; init; }

    /// <summary>How often this player wants to take three-point attempts. See
    /// <see cref="RimTendency"/>. A high <see cref="Outside"/> + low
    /// ThreeTendency player is a skilled shooter who doesn't shoot much (catch-
    /// and-shoot role); a low Outside + high ThreeTendency player is a volume
    /// chucker.</summary>
    public int ThreeTendency { get; init; }

    /// <summary>Ball security and control — turnover resistance, beating pressure.
    /// </summary>
    public int BallHandling { get; init; }

    /// <summary>Delivering the ball accurately — assist quality, turnover avoidance
    /// on the pass.</summary>
    public int Passing { get; init; }

    /// <summary>Reading the floor and creating FOR OTHERS — the decision skill the
    /// IQ modifier amplifies.</summary>
    public int Playmaking { get; init; }

    /// <summary>Generating one's OWN shot off the dribble — creating with the ball,
    /// the counterpart to <see cref="OffBallMovement"/>'s creating without it.
    /// </summary>
    public int SelfCreation { get; init; }

    /// <summary>Back-to-basket scoring as a STYLE: footwork, leverage, post
    /// technique. Distinct from the Close zone because it is a skill population,
    /// not a distance bucket.</summary>
    public int PostMoves { get; init; }

    /// <summary>Getting open WITHOUT the ball — cutting, relocating, catch-and-
    /// finish. The counterpart to <see cref="SelfCreation"/>.</summary>
    public int OffBallMovement { get; init; }

    /// <summary>Screen quality — affects what the ball-handler gets off the screen.
    /// Physical-adjacent (strength + timing) but authored as a skill.</summary>
    public int Screening { get; init; }

    /// <summary>Pursuing and securing the team's own misses. Feeds Roll I / Roll K's
    /// offensive-board arms.</summary>
    public int OffensiveRebounding { get; init; }

    // -------------------------------------------------------------------------
    // Defense — authored individual
    // -------------------------------------------------------------------------

    /// <summary>Contesting and staying with ball-handlers and shooters on the
    /// perimeter (quickness + discipline vs. the drive and the jumper).</summary>
    public int PerimeterDefense { get; init; }

    /// <summary>Defending the interior scorer — a different physical/skill blend
    /// from perimeter defense (strength + position vs. quickness).</summary>
    public int PostDefense { get; init; }

    /// <summary>Contesting and altering shots at the basket. The clearest home of
    /// any defensive attribute: Roll H's block weight is already zone-aware, and rim
    /// protection is what should tilt it.</summary>
    public int RimProtection { get; init; }

    /// <summary>Securing the opponent's misses. Feeds Roll I and Roll M's board
    /// split.</summary>
    public int DefensiveRebounding { get; init; }

    /// <summary>Forcing live-ball turnovers. Feeds Roll C's live-strip arms, Roll
    /// J/K's live-turnover paths, and Roll B's strip.</summary>
    public int Steals { get; init; }

    // -------------------------------------------------------------------------
    // Physical — authored individual
    // -------------------------------------------------------------------------

    /// <summary>Vertical size. One of the two-axis size inputs (alongside
    /// <see cref="Wingspan"/>).</summary>
    public int Height { get; init; }

    /// <summary>Reach. Absorbs "standing reach" entirely; generators read wingspan
    /// directly rather than a separate derived reach value.</summary>
    public int Wingspan { get; init; }

    /// <summary>Mass — contact absorption, post leverage, screening.</summary>
    public int Weight { get; init; }

    /// <summary>Force — rebounding through contact, finishing through contact,
    /// holding position.</summary>
    public int Strength { get; init; }

    /// <summary>Straight-line, north-south speed.</summary>
    public int Speed { get; init; }

    /// <summary>Side-to-side, east-west movement — the defensive quickness trait.
    /// </summary>
    public int Quickness { get; init; }

    /// <summary>Initial acceleration / blow-by burst.</summary>
    public int FirstStep { get; init; }

    /// <summary>Explosiveness off the floor (rim finishing, blocks, the glass).
    /// </summary>
    public int Vertical { get; init; }

    /// <summary>Stamina. <b>Dormant-pending-module.</b> Authored per player; no
    /// generator reads it until the stamina module is built. Seated and proven to
    /// exist before anything consumes it — the same discipline as Roll C's Session-24
    /// expansion.</summary>
    public int Endurance { get; init; }

    // -------------------------------------------------------------------------
    // Intangible — authored individual / modifier
    // -------------------------------------------------------------------------

    /// <summary>How hard a player competes. <b>Modifier (effort family).</b> Does
    /// not produce an outcome on its own — amplifies rebounding, steals, defense,
    /// loose balls. A high-hustle poor rebounder is still a poor rebounder, nudged.
    /// </summary>
    public int Hustle { get; init; }

    /// <summary>Reading the floor, off-ball decision-making, set execution.
    /// <b>Modifier (decision family).</b> Amplifies playmaking and help defense.
    /// Strategy-enabling: the smart-team build path (high-IQ, skilled, modest
    /// athleticism) needs this to be more than a rounding error.</summary>
    public int BasketballIQ { get; init; }

    /// <summary>Foul avoidance and defensive soundness. NOT shot selection — that
    /// is a tendency and lives in the Tendencies doc. Strictly foul-avoidance here.
    /// </summary>
    public int Discipline { get; init; }

    // -------------------------------------------------------------------------
    // Derived attributes — computed, never authored
    // -------------------------------------------------------------------------

    /// <summary>
    /// The composite of the explosive/movement physicals: the average of
    /// <see cref="Strength"/>, <see cref="Speed"/>, <see cref="Quickness"/>,
    /// <see cref="FirstStep"/>, and <see cref="Vertical"/>.
    ///
    /// <para>This is the locked ceiling principle: athleticism acts as a ceiling
    /// that limits how far skill can express against a given competition level —
    /// not the other way around. A high-athleticism, low-skill player has a high
    /// ceiling; a high-skill, low-athleticism player has a lower ceiling on how far
    /// that skill can express in contested situations.</para>
    ///
    /// <para><b>Placeholder formula.</b> Weights are provisional; Phase 6 tunes
    /// them. The composition is correct (these five and only these five); the flat
    /// mean is the Phase-1 placeholder.</para>
    /// </summary>
    public double Athleticism =>
        (Strength + Speed + Quickness + FirstStep + Vertical) / 5.0;

    /// <summary>
    /// Open-floor scoring ability — the confluence of <see cref="Athleticism"/>
    /// (itself derived) and <see cref="Finishing"/>. The engine's existing
    /// <c>FastBreak</c> marker is what makes transition a separable context.
    ///
    /// <para>Derived-from-derived: fine as long as the dependency order is
    /// explicit. Athleticism is computed first, Transition reads it.
    /// <b>Dormant-pending-module:</b> no generator reads Transition until Roll J's
    /// real generator is built.</para>
    ///
    /// <para><b>Placeholder formula.</b> Equal-weight mean of athleticism and
    /// finishing. Phase 6 tunes.</para>
    /// </summary>
    public double Transition =>
        (Athleticism + Finishing) / 2.0;

    /// <summary>
    /// The defensive attention this player's scoring threat commands — the
    /// per-player contribution to the team-aggregate gravity value.
    ///
    /// <para>Gravity is a team-aggregate whose effect is distributional: five
    /// credible threats distribute defensive attention so nobody can be doubled;
    /// one star concentrates attention and that is where help defense bites. This
    /// property is the per-player INPUT to that aggregation, not the aggregated
    /// team value itself (which is Phase 4 work).</para>
    ///
    /// <para><b>Dormant-pending-module:</b> no generator reads this yet.</para>
    ///
    /// <para><b>Placeholder formula.</b> Average of the three scoring zones and
    /// finishing — a rough "how dangerous is this player as a scorer." Phase 6
    /// tunes, and the real formula may weight zones differently.</para>
    /// </summary>
    public double GravityContribution =>
        (Close + Mid + Outside + Finishing) / 4.0;

    /// <summary>
    /// How much this player opens the floor for teammates — the per-player
    /// contribution to the team-aggregate spacing value.
    ///
    /// <para>Spacing is about shooting THREAT on the floor: a non-shooter does not
    /// space the floor regardless of other attributes. The team aggregate is
    /// non-linear (count of credible threats matters more than flat mean). This
    /// property is the per-player INPUT to that aggregation.</para>
    ///
    /// <para><b>Dormant-pending-module:</b> no generator reads this yet.</para>
    ///
    /// <para><b>Placeholder formula.</b> The player's outside shooting rating —
    /// spacing is directly whether the defense must stay honest on the perimeter.
    /// Phase 6 tunes (may fold in mid-range to credit stretch bigs, etc.).</para>
    /// </summary>
    public double SpacingContribution => Outside;

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Confirms every authored rating is on the 0–99 scale. Call after
    /// construction in the harness and anywhere a player is deserialized from
    /// external input. Returns a list of violation strings (empty = valid).
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Check(nameof(Close),               Close);
        Check(nameof(Mid),                 Mid);
        Check(nameof(Outside),             Outside);
        Check(nameof(Finishing),           Finishing);
        Check(nameof(FreeThrow),           FreeThrow);
        Check(nameof(FoulDrawing),         FoulDrawing);
        Check(nameof(RimTendency),         RimTendency);
        Check(nameof(ShortTendency),       ShortTendency);
        Check(nameof(MidTendency),         MidTendency);
        Check(nameof(LongTendency),        LongTendency);
        Check(nameof(ThreeTendency),       ThreeTendency);
        // v2: every shooter must have non-zero total tendency. A JSON config
        // missing all five fields would deserialize them as 0; without this rule
        // Roll G would build an invalid pie at runtime instead of failing here.
        var tendencySum = RimTendency + ShortTendency + MidTendency + LongTendency + ThreeTendency;
        if (tendencySum <= 0)
            errors.Add($"{Name}: tendency sum must be > 0 (all five zone tendencies are 0; check config).");
        Check(nameof(BallHandling),        BallHandling);
        Check(nameof(Passing),             Passing);
        Check(nameof(Playmaking),          Playmaking);
        Check(nameof(SelfCreation),        SelfCreation);
        Check(nameof(PostMoves),           PostMoves);
        Check(nameof(OffBallMovement),     OffBallMovement);
        Check(nameof(Screening),           Screening);
        Check(nameof(OffensiveRebounding), OffensiveRebounding);
        Check(nameof(PerimeterDefense),    PerimeterDefense);
        Check(nameof(PostDefense),         PostDefense);
        Check(nameof(RimProtection),       RimProtection);
        Check(nameof(DefensiveRebounding), DefensiveRebounding);
        Check(nameof(Steals),              Steals);
        Check(nameof(Height),              Height);
        Check(nameof(Wingspan),            Wingspan);
        Check(nameof(Weight),              Weight);
        Check(nameof(Strength),            Strength);
        Check(nameof(Speed),               Speed);
        Check(nameof(Quickness),           Quickness);
        Check(nameof(FirstStep),           FirstStep);
        Check(nameof(Vertical),            Vertical);
        Check(nameof(Endurance),           Endurance);
        Check(nameof(Hustle),              Hustle);
        Check(nameof(BasketballIQ),        BasketballIQ);
        Check(nameof(Discipline),          Discipline);
        return errors;

        void Check(string attr, int value)
        {
            if (value < 0 || value > 99)
                errors.Add($"{Name}.{attr} = {value} is outside the 0–99 range.");
        }
    }
}
