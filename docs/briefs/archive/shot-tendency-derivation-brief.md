# Project Charm — Skill-Derived Shot Tendencies: Design Brief

Settled in the design conversation of 2026-07-03, immediately after Session 33
(the OTHER-bucket fix) closed; **revised the same day after an outside review**
(ChatGPT, per CONVENTIONS §6c) sharpened the math agenda; and **updated again as
the design-math conversation began** and resolved the first agenda question (§3.5:
tendency is the neutral-matchup baseline; access and displacement belong to
Roll G). This is the design record for a **player-generation Pass 2 family
redesign**: the five shot-location tendencies stop being authored from a fixed
per-role table and become **derived from the player's own skills**, with
per-player variation. It is a brief, not a build prompt, and it is not yet a
locked spec — the intent is settled, and the math is being settled question by
question (§8). Only once the math is locked does a build prompt get drafted,
audited, and reviewed per CONVENTIONS §6.

---

## 1. The frame — how this design was reached

The conversation started as a calibration question: the season card reads 3PA rate
0.21 vs the real 0.39 (the "shot-diet dial," deferred since Session 32). The
investigation traced the league's shot mix to its actual source and found:

- Tendencies come from a **fixed table**: `GenRoles[role].Tendencies` in
  `Program.Gen.cs` — nine hand-authored five-number vectors, one per role. Every
  player of a role gets the identical vector. VERIFIED against source: the divvy
  path (`Program.Divvy.cs` → `GenRatings(role, …)`) uses this same table, so the
  season's ~3,470 players carry exactly **nine distinct shot diets**, cloned.
- The first proposed fix was to recenter those nine vectors (less long two
  everywhere, less mid for the pure shooters). Emmett's successive corrections
  dismantled that framing in three steps:
  1. *"Too many long shots for virtually all players"* — the miscalibration is
     the long two, not the three-point column.
  2. *"Tendencies should be varied across players … there should be PGs that
     shoot 80% threes, and ones that drive all the time"* — the fixed table has
     zero within-role spread, which no vector edit can fix.
  3. *"It really depends on the other skills. A perimeter player with high
     three-point shooting and weak everything else should generate extreme
     three-point tendencies. The guard with handles and athleticism and no shot
     should roll a rim-heavy tendency spread"* — tendencies should be
     **downstream of skills**, not authored beside them.

The third step is the design. It dissolves the first two problems as side
effects: the averages problem (wrong table values) and the spread problem (no
within-role variation) both vanish when the table is deleted and tendencies are
derived from skills that already vary per player.

**Supersession, recorded loudly:** the "recenter the nine vectors" session is
**superseded, not deferred**. No future session should tune `GenRoles[…].Tendencies`
— the table is slated for deletion, not correction.

---

## 2. The design reversal this makes (must be blessed, not slid past)

The current model *deliberately* decouples tendency from skill. The
`ThreeTendency` docstring on `Player` states it explicitly: authored, 0–99,
INDEPENDENT of conversion skill — "Klay Thompson and Steph Curry can have similar
three-point conversion skill but very different ThreeTendency values." A high-
Outside/low-ThreeTendency player is a catch-and-shoot role player; a low-Outside/
high-ThreeTendency player is a volume chucker.

This brief **reverses that principle**: tendency becomes a function of skill
(no variation term — see the §4.2 determinism ruling). The reversal is Emmett's
call, made in this conversation, and the build updated the docstrings to match — a
design principle stated in source must never silently contradict the generator.
What the old independent-authoring principle was reaching for (players who want
different shots) is delivered by the varied *skills* the generator draws, not by a
same-skill variation term: identical final ratings yield an identical diet.

---

## 3. The derivation — per-zone attribute keys (intent settled; math open)

> **SUPERSEDED (Session 35) for the three signal and the three floor.** The locked
> **oracle v2** (`tools/tendency_oracle.py`) is the spec of record: it derives three
> FREQUENCY from a compressed perimeter path blended with a stretch-gated interior path,
> gives every player with any outside rating a universal capable floor, and applies an
> explicit era profile — replacing the single credibility-gate description below. The
> section body is kept as the reasoning that led there; the oracle file, not this text, is
> authoritative for the three column.

One source fact shapes everything: **the skill side has three shooting buckets
(Close / Mid / Outside) but the tendency side has five zones** — and the
`Outside` docstring says "threes and long twos folded into one bucket." There is
no separate long-two skill. So shooting skill alone cannot distinguish a long-two
shooter from a three shooter; the discriminator has to be something else. Emmett
confirmed the discriminator: **shot creation**.

**A principle the math pass must honor per zone (from the review): access is not
the same as reason.** Each zone's expectation has two conceptually separable
signals — an *ability-to-access* signal (can he get to this shot) and an
*ability-to-make* signal (can he punish from here). **SUPERSEDED in part by §3.5:**
the design-math conversation resolved that *access is not a generation concern* —
the engine already models getting-open (Phase 46 denial, the openness layer) and
defers matchup displacement to Roll G. So the zone keys below are read as
**make/skill signals only**. The access/make split is preserved here as the
reasoning that *led* to that resolution, not as a live instruction; the long two,
which looked like the sharpest access case, is reframed in §3.5 as a
*seek-credibility* question instead.

- **Three** ← `Outside`, pushed *higher* by weakness elsewhere. The pure
  specialist — high Outside, low SelfCreation, low athleticism — lands at an
  extreme three tendency precisely because it is the only thing he does.
  (Interaction flag for the math pass: "pushed higher by weakness elsewhere" is
  itself a lopsidedness effect, so the specialist's three-spike must not be
  double-counted — once in the three signal and again in the peakedness
  transform (§4.1). One of the two owns it; the design pass decides which.)
- **Rim** ← `Finishing` + athleticism (`Speed` / `FirstStep` / `Vertical`) +
  `BallHandling`. The handle-and-hops-no-jumper guard rolls rim-heavy
  automatically.
- **Mid** ← `Mid`. The one clean 1:1.
- **Short** ← **two distinct routes into the same zone, not one sum.** A bruising
  post scorer and a small touch guard both live in the short paint but arrive by
  different skills, and a single `Close + PostMoves` sum would push every good
  floater guard (low PostMoves) out to rim-or-three:
  - *post-touch route* ← `Close` + `PostMoves` (hooks, short post, interior
    face-ups).
  - *floater route* ← `Close`/`Finishing` + `BallHandling` + `SelfCreation` /
    `FirstStep` (runners, teardrops from the small quick guard).
  - Short expectation = a combination of the two routes (the exact blend is open
    math; the point settled now is that both archetypes must have a path in).
- **Long** ← the special one, and the fullest treatment is in **§3.6** (cap, the
  two independent paths, the teammate-spacing deferral); this entry lists the
  attribute keys, read through §3.5 (these are *style/identity* signals, not
  opponent-relative access). **Two independent paths into the zone, not one
  blended product** (§3.6b):
  - *guard/wing pull-up* = **creation style × pull-up shooting** (a product — both
    required). Creation style ← `SelfCreation` + smaller `BallHandling` (does he
    live off the dribble). Pull-up shooting ← `Mid` + some `Outside` (can he
    punish from there). **A high-SelfCreation / weak-Mid / weak-Outside player does
    NOT become a long-two shooter** — he drives, floats, gets to the rim; he is not
    a bad long-two artist. This is the exact pathology the session exists to remove,
    and creation-as-sole-gate would have reintroduced it. Capped low, dominant-
    player-only (§3.6a).
  - *stretch-post* = **frontcourt/screener plausibility × catch-and-shoot
    shooting credibility** (not `PostMoves × shooting` — see §3.6b; post moves and
    floor-spacing are adjacent, not identical). Its own term, **lower bar** than
    the guard path (§3.6b), because the value is floor-spacing, not dominance.
    Authored from his *own* jumper only; the teammate-relative "he spaces because
    his teammates can't" amplification is a runtime selection effect, deferred
    (§3.6c).
  - Everyone lacking both a creation style and the stretch-post profile collapses
    to near-zero long, with the freed volume flowing to three (if he can shoot)
    or rim (if he can't). In modern offenses nobody else stands in that spot
    waiting for a pass.

All inputs already exist on `Player`. Nothing new needs to be authored — the
attribute surface was checked against source in this conversation and confirmed
sufficient.

### 3.5 RESOLVED (§8 Q1): tendency is the NEUTRAL-MATCHUP baseline — access and displacement belong to Roll G

The design-math conversation of 2026-07-03 resolved the first agenda question, and
the resolution simplifies the whole derivation. The route to it, recorded because
it is load-bearing:

- The first instinct was to split every zone into an *access* signal (can he get
  to the shot) and a *make* signal (can he punish it). Emmett corrected the
  framing by pointing at machinery that already exists. **Verified against source:**
  - **Phase 46 individual denial** (`RollEGenerator`, per-slot denial pass): after
    the usage tilt, each offensive player's touches are multiplied down or up by
    his one-on-one matchup — a *skill channel* (defender `OffBallDefense` vs
    offense `OffBallMovement`, blended with a post channel) and a *physical
    channel* (the athleticism gap). The overmatched or blanketed player already
    gets **fewer selection tickets**. Getting-open is already modeled.
  - The **attention / openness** layer (`AttentionGenerator`) already models
    gravity, spacing, and focal-point defense at make time.
- So "can he get to this shot / get open" is **already owned upstream**, at
  selection and make time, reading the actual defender. If the tendency layer also
  encoded access, it would **double-count** Phase 46. Therefore tendency's only
  job is: *given that he shoots, where does he seek it, from his own scoring
  skills* — access excluded by design.

**Displacement is matchup-relative and symmetric — so it is Roll G's job, not
generation's.** Emmett's two cases pin this down:
- *Overmatched* → the defense pushes him off the rim; his diet is shoved outward
  toward the **three, as a low-efficiency bailout** (a fallback he's allowed to
  take, not one he earned).
- *Undermatched* (the Korver-in-D3 case) → a weak defense lets him get to shots he
  never could otherwise; his spot-up baseline **breaks toward the rim**, at high
  efficiency.

Both are the *same player with identical attributes* shooting a different
distribution purely because the defender differs. A generation-time tendency is
authored with **no defender in the room**, so it structurally cannot express this
— only Roll G, which reads the actual defender, can. This is the same machinery
the brief already credits: Roll G's per-zone defensive-resistance bend is the seed
of exactly this behavior.

**The resolution, locked:**
- **Generation-time tendency (this brief) = the player's neutral-matchup baseline**
  — what he seeks against an *average* defender, derived purely from his own
  scoring skills. Every access, getting-open, and displacement effect is
  deliberately **excluded**, because Phase 46, the openness layer, and Roll G own
  them.
- Consequently the per-zone keys in §3 are read as **make/skill signals only**
  (Outside for three, Finishing+burst for rim, Mid for mid, the two short routes,
  the long-two credibility product). The "access signal" half of the §3 principle
  is retired: access is not a generation concern.
- **"Make/skill signals only" does NOT ban SelfCreation, BallHandling, or
  athleticism from the derivation** — and the final spec must state this once
  explicitly, because "access excluded" could otherwise be misread as "these
  attributes cannot influence tendency," which would gut the long-two credibility
  product and the rim/floater routes we just built. Those attributes belong in the
  baseline as **style / seek-credibility signals, not opponent-relative access:**
  - `SelfCreation` in Long = "does this player voluntarily live in pull-up
    decisions" (identity), not "can he beat this defender to the pull-up" (Roll G).
  - `BallHandling` in Rim / Short = "does his scoring identity include on-ball
    penetration or runners."
  - `FirstStep` / `Speed` in Rim = "does his neutral profile lean downhill."
  The test that keeps the line clean: an attribute is a *style* signal if it
  describes what the player *seeks against an average defender*; it is an *access*
  signal (excluded) only if it describes *beating a specific defender to the shot*.
  The same attribute can be a style signal here and feed an access computation in
  Phase 46 / Roll G — different jobs, no conflict.
- The long-two gate is reframed accordingly — see §3's Long entry, now read not as
  "can he create access" but as "does he *seek* the pull-up as part of his
  identity" (SelfCreation + pull-up shooting = credibility, not access).

**New deferral created by this resolution (recorded in §7):** symmetric
matchup displacement in Roll G, with **efficiency coupling** — pushed-out bailout
threes convert low, pulled-in rim shots convert high — of which the existing
defensive-resistance bend is the seed. Belongs to a future Roll G / possession
pass alongside the clock-heave, not this generation pass.

### 3.6 RESOLVED (§8 Q2): the long two — capped small, two independent paths, teammate-spacing deferred

The design-math conversation resolved the long two. Three rulings:

**(a) The long-two tendency is CAPPED small, and the cap is reachable only by a
dominant, self-limiting player.** The modern long two is the least-taken shot on
the floor; even a genuine pull-up scorer takes far more threes and rim attempts.
So the qualifier gets a *modest* slice, never a large one. The archetype of the
high-volume mid-range maestro (prime CP3 / DeRozan) still exists — a player
off-the-charts at *both* creation and mid-range pull-up can approach the cap — but
that skill level is one-and-done to the pros, so at any moment the college
universe has almost none of them. The cap does double duty: unfashionable for the
ordinary qualifier, and rarely even touched because only the elite reach it. The
**product form is confirmed for the gate** (creation style × pull-up shooting —
both required, either missing collapses it, no sum papering over a weakness); the
cap governs magnitude on top of the product.

**(b) The guard/wing path and the stretch-post path are INDEPENDENT terms with
different availability — not one formula with a small add-on.** They are different
basketball shots:
- *Guard/wing pull-up long two* — a **dominant-player** shot. Gated hard, capped
  low, reachable only by the near-elite creator-plus-shooter. Unfashionable, and
  only stars overcome that.
- *Stretch-post long two* — a **floor-spacing / role-value** shot, framed by
  Emmett as "a way they can spread the floor a bit." NOT about dominance. An
  ordinary big with a real jumper earns a modest long two because the value is the
  spacing, not shot-making dominance — so this term switches on at a **lower bar**
  than the guard's and does not require the player to be dominant.
  - **Gate correction (outside review, accepted): the stretch-post term is NOT
    `PostMoves × shooting`.** Post moves and floor-spacing are *adjacent, not
    identical* — a real pick-and-pop big is valuable precisely because he does
    *not* need a back-to-the-basket game. Gating on PostMoves would invert the
    intent: it would hand the *traditional* post scorer the spacing shot and
    starve the *actual* floor-spacer (good jumper, modest post game). The term is
    **frontcourt/screener plausibility × catch-and-shoot shooting credibility**,
    and the two factors must **partition their inputs with no attribute in both**
    (second review — otherwise a factored product double-counts shooting, once
    inside "plausibility" and again inside "credibility," squaring its influence
    for bigs):
    - *frontcourt/screener plausibility* ← **structural** frontcourt identity only:
      physical profile (`Height` / `Weight` / frame) + role *eligibility* (is he a
      frontcourt-eligible body) + `Screening` (if live enough to rely on) + a small
      `PostMoves` contribution. **This must be derived from physical profile and
      role eligibility, NOT from a hard-coded shot-diet role category** (second
      review) — the whole redesign deletes role→shot-behavior authoring, so
      "frontcourt identity" here means *structural plausibility that he's a spacing
      big at all*, never a disguised role table. Roles still seed skill emphasis;
      they must not re-enter as a shot-diet lookup through this door. **No shooting
      attributes in this factor** — it answers only "is he plausibly a
      frontcourt screener/popper."
    - *catch-and-shoot credibility* ← `Mid` + a smaller `Outside` contribution.
      This factor owns **all** the shooting — "can he actually make the pop."
    Exact blend and weights are item-2 math (§8).
Running both through one cap/steepness would either starve the floor-spacing four
or make the guard long two too common (breaking (a)). Two terms, two availabilities.

**(c) The stretch-post's TEAMMATE-relative amplification is deferred to the
SELECTION layer (Roll G), NOT the make layer — and NOT a generation-time term
(§7).** Emmett's sharpening: a middling-shooting big gets catch-and-shoot long
twos *because his teammates can't shoot* — he's the least-bad spacer on that unit
— and the same big is redundant on a unit full of shooters. That is a
**team-composition** effect that changes with every lineup swap, off identical
attributes — structurally the same shape as the Korver-in-D3 displacement
(context-dependent redistribution, not birth-time identity), pointed at teammates
instead of defenders. Ruling **(i)**, locked:
- **Generation** authors the big's long two from his *own jumper alone* — a
  small, capped, personal-credibility baseline (per (a)/(b)). Context-free.
- The **teammate-relative amplification** is a **runtime lineup effect** — but its
  owning layer is a correction from the outside review, resolved against source:
  - The effect is not only "the big's shot gets more open" (a *make* condition) —
    it is "the offense *selects* the big's catch-and-pop more often" (a *shot-diet
    / zone-selection* change). By this brief's own rule, a context that changes
    *what shot is selected* belongs to the **selection** layer; only a context
    that changes *how likely it goes in* belongs to the **make** layer. Assigning
    it to the attention layer (an earlier draft's error) would route a selection
    effect into a layer that cannot select.
  - **VERIFIED against source:** the team spacing/gravity/openness fields
    (`TeamSpacingLevel`, `TeamBaseOpenness`, `TeamGravityLevel`,
    `TeamConversionQuality`) are consumed **only by Roll H** — they touch make
    probability, not shot location. Roll G (shot location) reads *no* spacing
    field; its location decision is tendency + defensive-resistance bend, with
    `ShooterAttentionShare` used only as a usage-pressure amplifier. **The
    attention/spacing system today has no path into shot-location selection.**
  - Therefore the clean future seam: the **attention/spacing system computes the
    lineup-relative spacing context** → a future **Roll G lineup-context bend
    consumes it to move the zone ticket** (selection) → **Roll H consumes openness
    for conversion** (make). This sits alongside the deferred Roll G matchup
    displacement (§3.5) — both are runtime redistributions of the neutral diet.

This collapses the "how good must a big's jumper be" threshold question back to
the same **personal-credibility gate** as the guard — just a lower bar, because a
big's catch-and-shoot is a simpler shot than a guard's pull-up. "Is he the best
spacer in this lineup" is not generation's question.

---

## 4. The two shape properties (the hard parts)

### 4.1 Peakedness = relative shape AND absolute capability (not lopsidedness alone)

Emmett: skilled offensive players who can score from multiple levels have
**more flattened-out tendencies** — the flexibility is itself the weapon; they
take what the defense gives. The specialist is spiky (one weapon); the complete
scorer is flat (all of them).

The review sharpened this with a trap the first draft walked into: if flatness
came from *relative shape alone* (low variance among the five signals), a
merely-average, low-ceiling player whose mediocre skills happen to be even would
read as an "unguardable multi-level scorer" — nonsense. He is not versatile; he
is mediocre everywhere. **Peakedness therefore needs two inputs, not one:**

1. **Relative shape** — how uneven are the player's zone-relevant offensive
   tools? (A lopsided profile spikes toward its strength.)
2. **Absolute capability** — are those tools actually good enough to make him a
   credible threat in several places? (Evenness only earns flatness when the
   even tools are *good*.)

The complete scorer flattens because he has **multiple credible weapons**, not
merely because his numbers have low variance. The resulting diet shapes:

| Player | Skill pattern | Correct diet shape |
|---|---|---|
| Pure shooter | Outside excellent, rest limited | highly three-peaked |
| Rim athlete | Finishing/burst/handle excellent, shooting limited | highly rim-peaked |
| Multi-level scorer | several strong zone pathways | broad / flatter |
| Even but mediocre | several *average* pathways | somewhat balanced, but NOT "unguardable" |
| Weak player | no strong pathway | mostly constrained toward his least-bad credible scoring option, small variation |

The weak player's correct shape is *not* flat — it is constrained toward his
least-bad credible scoring option, which is itself a form of peaking. Flatness is
reserved for genuine multi-zone capability.

This connects forward: the flat-tendency multi-level scorer is the player whose
diet the defense cannot scheme against, which is the "reacting to what the
defense is giving" realism Emmett named — Roll G's existing defensive-resistance
bend already supplies the in-game reaction; the flat spread is what gives that
bend multiple plausible destinations to move his shots toward.

### 4.2 Same ratings mean the same tendencies — the determinism ruling (SUPERSEDED, 2026-07-04)

> **This section is superseded by the build-time determinism ruling (Session 34).**
> The original §4.2 argued for a per-player style draw so that two players with
> identical ratings would still shoot different diets. That was reversed before the
> build. The text below records the final ruling; the old reasoning is preserved in
> the journal.

**The derivation IS a pure function of the final rating map.** No player-style
seed, no manufactured tendency noise. Two players with identical final ratings
come out with the **identical** integer diet. Population variety comes entirely
from the varied skills the generator already draws independently per player —
which is abundant — not from a separate noise term layered on top.

Why the reversal: the thing the old §4.2 wanted to preserve (Klay-vs-Curry
distinction) is already delivered by the skills themselves. Two shooters with
genuinely different creation, mid, and outside numbers already derive different
diets; if their ratings are *truly identical*, there is no basketball reason they
should want different shots, and inventing one would only reintroduce the
pathology the disciplined variation was trying to avoid (a player accidentally
acquiring a shot his skills don't support). The cleaner rule is: **skills carry
all the variation; the derivation is deterministic on top of them.**

The other half of the old distinction — a skilled shooter who nonetheless *takes*
few shots — is not a tendency-shape question at all. That is **shot volume**, and
volume is usage/hierarchy's job (a separate existing layer): a low-usage spot-up
shooter and a high-usage one can share the same *diet* (what fraction of his own
shots are threes) while differing enormously in *how many* shots he gets. The
derivation sets the diet; usage sets the volume. Keeping them separate is what
lets both be modeled honestly.

The pipeline shape, as built:

```
skill-derived zone signals   (raw capability per zone, §3)
  → peakedness transform      (relative shape × absolute capability, §4.1)
  → margin bleed              (porous zone walls — no false zeros)
  → opportunity floor         (inside for everyone; perimeter only for perimeter players)
  → integerize to sum 100     (deterministic tie-breaks, fixed zone order)
```

No variation term appears anywhere in that chain — the reversal removed it.

---

## 5. The heave — DECIDED: not a generation-floor mechanic (deferred to Roll G)

The standing rule stands, restated by Emmett with the canonical example: even
Shaq with zero three-point skill needs a ~0.05% chance of a three — the
desperation heave off a broken play — taking **virtually the max shot-clock
time** (~28 seconds in). The question was whether a nonzero tendency floor at
generation is the right home for it. The review supplied a decisive argument, and
this is now a **decision, not an open question:**

**A generation floor cannot represent a heave, and calling it one is wrong on two
counts.**

1. *Semantic:* a static tendency floor produces a tiny chance of an
   **ill-timed *normal* offensive selection** — an early-clock Shaq three — not a
   late-clock desperation heave. A Shaq taking a broken-play buzzer heave is real;
   a Shaq receiving a regular early-clock three ticket because every tendency must
   be nonzero is a different and unrealistic thing.
2. *Representational:* the tendency fields are integer-ish values on a 0–99
   surface (the current role vectors are whole integers copied straight into the
   player fields). A "0.05% chance" is **not naturally representable** as one of
   those weights without knowing exactly how the downstream pie normalizes them —
   it does not live in that integer surface.

**The ruling:**
- Do **not** call a generation floor a heave mechanic.
- For this pass, allow **zero** zone tendencies — or, only if the engine
  structurally requires nonzero zone values (to be checked at build against the
  pie/normalization), a harmless numerical minimum with no pretense of being a
  heave.
- **True late-clock heaves are recorded as a Roll G / clock-context behavior**
  for the later possession-length work — a runtime mechanic that reads the shot
  clock, not a generation-time weight. A heave possession is a long possession,
  so this also ties into the deferred turnover-clock / possession-length dial.

This is the one place the brief favors a clean deferral over an approximate
first-pass representation.

---

## 6. Engineering shape (Claude's calls, flagged)

- **Derive at generation time; the `Player` surface is unchanged.** The five
  tendency attributes stay on `Player` exactly as they are; what changes is how
  the generator authors them (derivation instead of table lookup). Roll G, the
  coaching seam, the defensive bend, and every downstream consumer are untouched.
  This keeps the change fenced inside player generation.
- **The nine-role vocabulary survives; the tendency column dies.** Roles still
  drive skill emphasis, position quotas, lead-handler reservations, and the
  divvy's coverage logic — none of that is touched. Only
  `GenRoleDef.Tendencies` is deleted.
- **Oracle-first is a hard gate.** The derivation is deterministic math over
  drawn ratings plus a seeded variation draw — exactly the shape the Python
  oracle discipline exists for. The oracle mirrors the derivation
  constant-for-constant and proves the **structural** distributional claims
  (specialists spiky, multi-level scorers flat, no-creation players suppressed at
  long, weights conserve) across seeds before any C# is written. League 3PA rate
  is printed as a diagnostic, not gated on — see §8.
- **The stress-test archetype buckets keep working.** The archetype rosters are
  a lab instrument, not the population; their fixed hand-authored tendencies in
  `Program.Stress.cs` are independent of `GenRoles` and can stay as controlled
  fixtures — or be migrated to the derivation later. Not this pass's problem;
  recorded so the build doesn't "helpfully" touch them.

---

## 7. What this pass explicitly does NOT do (scope walls, provisional)

- **No calibration of the make curves or any Roll H parameter.** The Session 32
  midpoints stand; if the derived diets shift zone volumes enough to move
  per-zone FG%, that is a reading, not a thing to chase mid-pass.
- **No Roll G changes.** Three runtime behaviors are deferred to a future
  Roll G / possession pass, NOT smuggled into this generation pass:
  - the clock-conditioned late-clock heave (§5);
  - **symmetric matchup displacement with efficiency coupling** (§3.5) — a strong
    defender pushes a player's diet outward to low-efficiency bailout threes; a
    weak defender pulls it inward to high-efficiency rim shots (the Korver-in-D3
    case). The existing per-zone defensive-resistance bend is the seed; what's
    deferred is its magnitude and the efficiency coupling. Generation authors only
    the neutral-matchup baseline this bends.
  - **teammate-relative floor-spacing amplification** (§3.6c) — a middling-shooting
    big gets more catch-and-shoot long twos when his teammates can't shoot, none
    when they can. This is a *selection* effect (it changes which shot the offense
    picks), so it belongs to a future Roll G lineup-context bend, NOT the make
    layer. VERIFIED against source: the attention/spacing fields feed only Roll H
    (make); Roll G reads no spacing field today, so the attention layer has no path
    into shot-location selection. The seam: attention/spacing computes the
    lineup-relative context → Roll G consumes it for selection. Generation authors
    only the big's context-free personal-credibility long two (§3.6a/b).
- **No coach-layer work.** The coaching ShotSelectionBias nudge continues to
  operate on whatever tendencies the player carries.
- **No tweener-post or versatility work.** Those remain their own parked Pass 2
  items (see the deferred design notes of 2026-07-03); this pass and those share
  a family but not a session.

---

## 8. The design-math agenda (ordered) and the oracle's proof obligations

The intent above is settled. The math is not. The follow-up design conversation
answers these **in order** — each builds on the one before:

1. ~~**Per-zone signals, make vs access, defined separately.**~~ **RESOLVED (§3.5).**
   Access is not a generation concern — Phase 46 denial and the openness layer
   already own getting-open, and matchup displacement is deferred to Roll G. The
   per-zone keys are read as **make/skill signals only**; the access half is
   retired. Remaining under this item: pin the exact make-signal weighting per
   zone (in what space, normalized to sum ~100).
2. ~~**Long two = seek-credibility, not access**~~ **RESOLVED (§3.6).** Capped
   small, reachable near-cap only by a dominant self-limiting player; two
   independent paths (guard pull-up = SelfCreation×pull-up-shooting product,
   dominant-only; stretch-post = frontcourt/screener plausibility × shooting
   credibility, lower bar — NOT PostMoves-gated, §3.6b); teammate-relative
   floor-spacing deferred to a future Roll G lineup-context selection bend
   (a *selection* effect, source-verified — NOT the make/attention layer, §3.6c).
   Remaining under this
   item: pin the two paths' exact curves, the cap value, and the stretch-post
   bar — as concrete numbers, alongside item 1's make-signal weighting, since they
   share the sum-to-~100 normalization.
3. **Short = post-touch route + floater route** (§3). Settle both routes and how
   they combine so neither the bruising post nor the small touch guard is pushed
   out of the zone.
4. **Peakedness from relative shape AND absolute capability** (§4.1). Settle how
   each is measured and how they jointly set the spread — and resolve the
   double-count flag: the specialist's three-spike must be owned by *either* the
   three signal *or* the peakedness transform, not both.
5. **Determinism** (§4.2, superseded ruling): the derivation is a pure function of
   the final ratings — no variation term, no style seed. Variety comes from varied
   skills; the clock-conditioned heave stays deferred (§5).

### The oracle's proof obligations (structural claims, NOT a calibration gate)

Oracle-first is a hard gate, but the review corrected *what* it proves. The
oracle mirrors the derivation constant-for-constant and proves **structural**
claims across seeds before any C# is written:

- High Outside + weak alternatives → high three tendency.
- Strong handle/burst/finishing + weak shooting → high rim tendency.
- High SelfCreation *without* pull-up shooting → does **not** produce excessive
  long two (the pathology guard).
- Multi-level strong players are flatter than true specialists; even-but-mediocre
  players are NOT flat (§4.1).
- Same ratings + different seeded identity draws → varied diets, all inside
  logical bounds.
- Long-two share is sharply suppressed for low-creation, non-stretch players.
- All five weights conserve (sum ~100) and pass `Player.Validate()`.

**League-aggregate 3PA rate is an oracle *diagnostic*, printed as a sanity read —
NOT a pre-build acceptance assertion.** This is a firm ruling, not an open
question. If the oracle gated on "the redesign succeeds only if league 3PA
reaches 0.39," the generation pass would quietly become a **calibration** pass
before the coach layer and selection mechanics have had their own turns — exactly
the tune-against-a-moving-target trap this project exists to avoid. The standing
principle (calibration proper waits for a real population) applies with full
force, and *this pass is what makes the population real*. Print the number; do not
tune to it.
