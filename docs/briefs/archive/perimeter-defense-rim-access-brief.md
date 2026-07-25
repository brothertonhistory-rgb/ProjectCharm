# Project Charm — Perimeter Defense, Rim Access & the Two-Sided Failure: Design Brief

Settled in the design conversation of 2026-07-14, immediately after Session 58
(the live steal-forcing floor) closed. This is the design record for the **next
wiring item after S58** — teaching the engine that a good on-ball perimeter
defender keeps his man out of the paint, and that the *kind* of defensive
failure (getting driven past vs. giving up an open three) depends on *which*
defender is weak.

It is a **brief, not a build prompt, and not yet a locked spec.** The
**basketball intent is settled** (§3, every ruling Emmett's); the **math and the
engineering seams are open** (§5–§6). Per CONVENTIONS §6, this brief goes to
outside review (ChatGPT) next, then an oracle-first design-math pass puts numbers
on real archetypes, and only then is a build prompt drafted, audited, and
reviewed. The current-state section (§2) was pinned against a fresh pull this
conversation and names the real methods so the build cannot drift from it.

**Outside review adjudicated (2026-07-14, ChatGPT, CONVENTIONS §6c).** The review
was green-light-with-warnings for the math phase and reopened none of the §3
rulings. Its central finding — that **R5's post-feed reroute crosses a roll
boundary** (it changes *which player shoots*, not just *where a shot comes from*)
— is folded and was **source-verified**: `RollGGenerator` throws unless
`SelectedSlot` is already stamped ("Roll E must run before Roll G"), so the
shooter is fixed before location is decided and a true cross-player reroute
cannot live in Roll G. R5 is therefore split (§3, §5, §8): the same-player
location reroute is a Roll G item; the cross-player post-feed is its own upstream
design pass (Pass B). A second finding was also source-verified: `Player.Athleticism
= (Strength + Speed + Quickness + FirstStep + Vertical) / 5`, so listing Quickness
and FirstStep *and* Athleticism in the drive composite would double-count burst
(§6.1). Other folded items: gate composed **after** displacement as an access
constraint (§5.1); the eligible-drive-bucket ambiguity and the engine's lack of
in-diet route provenance (§6.2); suppression-primary rather than a symmetric
weak-defender bonus (§6.3); the off-ball lever measured by expected three-point
damage and probably not owned by Roll H (§6.4); a formal post-threat eligibility
definition keyed on post *matchup value* (§6.5); an expanded structural-invariant
set (§6.6); and a three-pass work split (§8). Nothing was rejected.

**Second review adjudicated (2026-07-14, ChatGPT) — Pass A cleared.** The
follow-up review verified the revision and green-lit the Pass A oracle-first
conversation with no architectural blocker. Its refinements are folded (all
Pass-A math-pass material or terminology precision, none reopening §3): relabel
"drive-derived paint mass" → **gate-eligible paint share** (the diet has no route
provenance, so it is a proxy bucket, §6.2); the drive composite starts as
**BallHandling + FirstStep + Quickness**, dropping raw Athleticism entirely (it
already contains the last two), a physical term added only if an archetype needs
it (§6.1); **shooter-orientation** gets a source-owned definition as its own open
item + oracle column (§6.1); the **neutral anchor is measured after displacement**
(oracle reports pre- and post-gate share, §6.6); **R5a redistribution** needs a
decided hierarchy, ≥2 forms compared (§6.5); and a concrete oracle **column +
archetype-row spec** (§6.6). R1's wording is left verbatim (Emmett's ruling) with
an implementation pointer added, rather than reworded.

---

## 1. The frame — how this design was reached

The S54 defensive reruns surfaced two findings that this design answers:
**perimeter defense is point-neutral without a rim deterrent**, and **the
location machinery pushes shots the wrong way** — stacking perimeter defenders
raised the defended team's rim share (28.8% → 33.0%) instead of walling the
paint off. Emmett's read: good on-ball perimeter defense should *put the
offensive player in an uncomfortable position* — keep him relegated to the
perimeter, deny the drive, and force a contested jumper or a pass-off — while
the failures of *bad* defense should split by role.

Emmett's model, in his words across the conversation:

- A really good perimeter defender **keeps his man relegated to the perimeter**
  and should shrink **both rim access and outside success** — *unless* the
  offensive player has the athletic tools, ball-handling, quickness, and skill
  to counteract it.
- A really bad **on-ball** perimeter defender is felt most by **getting driven
  past** — his man gets to the rim and causes problems (the highest-value
  failure: rim shot + fouls + help collapse).
- A really bad **off-ball** defender gives up **open threes**, and that channel
  should hit **harder** than bad on-ball defense giving up threes — an open
  catch-and-shoot three is a help/rotation/closeout failure, worth more damage
  than a contested pull-up.
- The emergent counter Emmett named, which the design must actually produce:
  a team with **good perimeter defenders but poor rim protection** should force
  the offense to **feed its post players** — the guards can't get downhill, but
  the soft interior is still there to be attacked through the post, a door the
  perimeter defenders don't guard.

The design goal is that all of this **falls out of per-man matchups**, with no
team-level scalar — the no-scalar wall holds, exactly as displacement (S36) and
the steal floor (S58) do.

---

## 2. What the engine does today — verified against source this conversation

Four machineries touch this area; **only one is per-man, and it is the one that
does not touch shot location.** (Files/methods named for the build's audit.)

**(a) The on-ball shot-quality contest — per-man, quality only.**
`Matchup.EffectiveRating(zone, attacker, defender)` (`Matchup.cs`) is the make
door's core. It reads the **single slot-matched defender** and blends his
`DefenseRating` (a per-zone Perimeter/Post/Rim mix) against the shooter's zone
skill. This is the only place an individual defender's rating meets his own man.
It lowers the **quality** of whatever shot the man takes — the S54 per-man
efficiency drop (covered man's FG 37.8→34.9, 3P 29.5→19.4). **It never touches
where the shot comes from.** So "a good perimeter defender contests the outside
shot he forces" — half of Emmett's model — **already exists**.

**(b) Where shots come from — a TEAM read, not per-man.** The five-zone shot
diet is bent by `Matchup.DeriveDisplacement` (`Matchup.cs`, S36), consumed by
`RollGGenerator`. Two levers inside it, both driven by the **defending lineup as
a collective** (top-three zone blend + lineup-mean athleticism), never a matched
man:
- **The residualized bend** (Phase 9 shape): shots flow toward the zone where
  the shooter has the biggest *relative* edge — i.e. **where the defense is
  thinnest**. This is the culprit behind the backwards S54 result: stack
  perimeter defenders and the rim becomes the relatively soft zone, so shots
  flow *there*. It is "attack where they're weak" scheme logic.
- **The displacement ladder** (level): the shooter's *overall* talent gap vs the
  lineup pushes a **featured** shooter (usage-gated) outward when overmatched
  (unconditional) or inward when advantaged (gated by his own Finishing/Close).
  Driven by overall level, not by any specific on-ball matchup.

Neither lever knows who is guarding whom. There is **no per-man rim-access gate
anywhere** — nothing where the individual defender in front of a guard makes it
harder for *that guard* to get into the paint.

**(c) Off-ball defense — two live wires, neither owning the open three.**
`OffBallDefense` reads in exactly two places:
- **Roll E denial** (`RollEGenerator.cs`): a getting-open contest,
  `OffBallDefense` vs the offensive player's `OffBallMovement` (perimeter side),
  blended with a post side and an athleticism gap. Affects who gets open / the
  attention share.
- **Roll H suppression** (`RollHGenerator.cs`): a make-quality suppression from
  the helpers' aggregate `OffBallDefense`, **weighted toward the interior/mid**
  (`OffBallDefenseMidMultiplier` 0.30, an accelerating aggregate exponent).

Neither singles out the **open catch-and-shoot three**. So the "off-ball owns
the open-three leak, as the bigger lever" half of Emmett's model **does not
cleanly exist** and is largely net-new.

**Two structural facts that matter most (source-verified 2026-07-14):**

- **Roll G's shot-location design has always been a team-defense read, not
  one-on-one.** This design **deliberately introduces the first per-man read into
  shot location** — the on-ball drive gate — for the drive channel only. An
  architectural shift, flagged honestly in §5/§6.
- **The shooter is FIXED before Roll G runs.** `RollGGenerator` throws unless
  `state.SelectedSlot` is already stamped ("Roll E must run before Roll G"), and
  it fetches that one shooter. **Roll G cannot reassign the shot to another
  player.** This is the load-bearing fact for R5: transferring a denied guard's
  drive to a *post* player is a change of *who shoots*, which is Roll E's job, not
  a location change Roll G can make. (The consequence is worked through in §3/§5.)

One input fact for §6.1: `Player.Athleticism` is the flat mean of five ratings —
`(Strength + Speed + Quickness + FirstStep + Vertical) / 5` — so **Quickness and
FirstStep already live inside it.** A drive-tools composite must not list them
both discretely *and* via Athleticism, or it double-counts burst.

---

## 3. The core rulings (2026-07-14, all Emmett's — the settled basketball)

**R1 — On-ball perimeter defense gates the guard's rim ACCESS, as a matchup.**
A good perimeter defender shrinks his man's paint attempts — fewer drives get to
the rim — *and* still contests the outside shot he forces (that contest is (a),
which exists). It is **not** a flat suppression: it is the defender's
`PerimeterDefense` vs the offensive player's drive tools (**BallHandling,
Quickness, FirstStep, athleticism**). Beat him on those and the drive gets to
the rim anyway — the "unless he has the tools to counteract it" clause is a real
matchup gate, not a constant. *(These are the drive tools in intent; the exact
composite — which ratings, weighted how, without double-counting burst — is an
implementation question settled in §6.1, not a mandated four-term average.)*

**R2 — The on-ball wall WINS over the soft-zone pull.** When a lockdown on-ball
defender and the team's-soft-spot logic (b) conflict, the individual matchup
wins: a great perimeter defender keeps *his* man out of the paint **even when the
paint is the softest spot on the floor**, because "can't get by his man" is the
whole point of on-ball defense. The team's interior weakness still shows up — but
through *other* doors (the post route, help rotations, the next man), not by
inviting this guard's drive. Emmett's rationale: *"If you don't have good rim
protection, it makes sense that you pressure the ball and try to keep guys from
getting to the rim at all."*

**R3 — The gate is DRIVE-SPECIFIC. It never touches the post route.** A
perimeter defender walls off *his man beating him off the dribble* — a guard
going downhill. It does **nothing** to a post player catching on the block: that
player isn't driving past a perimeter defender, he's working against the
interior. **The post route to the rim reads interior defense (rim protection /
post defense); perimeter defense never gates it.** This is what makes the
emergent counter real rather than cosmetic (R5).

**R4 — The two failures are routed by WHICH defender is weak.**
- **Weak on-ball** perimeter defender → **leaks the drive** → his man gets to the
  rim (the highest-value failure). This is the inverse of R1's gate.
- **Weak off-ball** defender → **leaks the open three** → and this is the
  **bigger** of the two three-prevention levers. On-ball defense's
  three-prevention is real but smaller (it contests the pull-up via (a)); the
  wide-open catch-and-shoot three is mostly an off-ball/rotation failure.

**R5 — The emergent counter: good perimeter D + weak rim protection ⇒ the offense
feeds the post.** *(Basketball intent settled; the review sharpened its
architecture — the intent is unchanged, the build is split.)* When the guards are
walled off (R1) but the interior is soft, the good-perimeter/weak-rim team should
*naturally* become a post-feeding team — no scripted strategy, pure matchup
emergence. **But this is two different operations, and only one lives in Roll G:**

- **R5a — same-player disposition (Roll G).** The share removed from a walled
  guard's drive goes *somewhere in his own diet* — mostly to his contested
  perimeter jumpers. This is a location change on a fixed shooter; Roll G owns it.
- **R5b — cross-player post-feed (UPSTREAM, its own pass).** The offense actually
  feeding a *different* player — the post man — is a change of **who shoots**.
  Source-verified: the shooter is chosen in Roll E *before* Roll G, so Roll G
  cannot hand the shot to the post man. R5b therefore cannot be a Roll G reroute;
  it must raise the post player's **selection odds upstream** (Roll E / the
  opportunity-selection layer) when perimeter creation is being denied — the gate
  and the post-feed living in separate rolls sharing one bounded "perimeter
  creation denied" signal. This is the larger architectural item and is split out
  as **Pass B** (§8); it does **not** block the drive gate.

The honest consequence: R5a is a small Roll G seam; R5b is a possession-allocation
design of its own. The drive gate (Pass A) can lock with R5a redistribution alone
— the soft interior stays independently exploitable through the *existing* post
selection even before R5b is built, so post feed may rise indirectly; R5b is what
makes the rise *reliable and matchup-driven* rather than incidental.

**Efficiency needs nothing new on the shot itself.** The contest on the forced
outside shot already exists (a); the make curve already prices a bad shot from a
bad spot. This design moves *where* the shot comes from and *who gets blamed for
the leak*; it does not add a second efficiency coupling.

---

## 4. What exists vs. what is net-new (the honest split)

| Piece of Emmett's model | Status today |
|---|---|
| On-ball D contests the outside shot it forces | **EXISTS** — make door (a), per-man, S54-measured |
| On-ball D denies the guard's rim access (drive gate) | **NET-NEW** — no per-man rim-access lever exists; location is team-only (b) |
| The gate is a matchup (perim D vs handle/quickness/first-step/ath) | **NET-NEW** — this specific contest does not exist |
| The gate wins over the soft-zone pull | **NET-NEW composition** — must override (b)'s residualized bend for the drive channel |
| The post route stays open, read by interior D | **PARTIALLY EXISTS** — interior D reads in (a)/(b); the "guard-gate skips the post" split is new |
| Walled guard's share redistributes in his own diet (R5a) | **NET-NEW seam** — small, Roll G |
| Offense feeds a *different* post player (R5b) | **NET-NEW, UPSTREAM** — a who-shoots change; Roll G can't do it (shooter fixed at Roll E); its own pass |
| Weak on-ball leaks the drive (rim) | **NET-NEW, suppression-relative** — today weak on-ball leaks *threes* via (b), the inverse; the leak is the *absence* of the wall, not a bonus (§6.3) |
| Weak off-ball leaks the open three, bigger lever | **NET-NEW** — off-ball wires (c) don't single out the open three |

Two clearly net-new builds sit at the center: **the drive-specific rim-access
gate**, and **an off-ball-owns-the-open-three lever**. Everything else is
composition and re-routing around them.

---

## 5. The seam risks — where this is delicate (open engineering)

The location machinery is the most calibrated part of the engine (the S36
displacement oracle, 28 structural checks, a 10-vector golden fixture). Adding a
per-man drive gate in front of it carries real risk, and these seams must be
proven, not presumed, in the math pass:

1. **Composition — a POST-displacement access constraint (folded from review).**
   The gate is applied **after** `DeriveDisplacement` runs, not as a pre-bend
   haircut and not inside the level math. Rationale (the review's, adopted):
   displacement should first express the defense's *collective* shape (where the
   paint is soft); then the matched defender constrains whether *this* player can
   convert that paint invitation into actual drive share. That ordering **is** R2
   in mechanism — identifying soft paint does not imply the ballhandler can get
   there — and it keeps S36's output bit-identical when the gate is neutral (a
   hard invariant, §6.6). Conceptually: existing machinery produces the normal
   location pie → identify only the **gate-eligible paint share** (a proxy bucket
   keyed on shooter orientation + zone, §6.2 — the diet carries no literal
   drive provenance)
   (§6.2) → compute the per-man drive-access multiplier → remove the denied
   portion → redistribute it (R5a) → renormalize. The oracle proves the
   double-count guard and the neutral identity.

2. **The removed-share disposition (R5) — split, per the review.** When the
   guard's paint access is choked, the removed share must be *fully accounted
   for* (conservation, §6.6). **R5a (Roll G):** it redistributes within the
   guard's own diet — mostly to his contested perimeter jumpers. Tractable. **R5b
   (upstream, Pass B):** actually feeding a *different* post player is a
   who-shoots change Roll G cannot make (§2, §3) — it must raise the post man's
   selection odds in Roll E from a shared "creation-denied" signal. This is the
   hardest piece, spans rolls, and is split out so it does not hold the drive gate
   hostage (§8). The oracle for Pass A proves R5a; Pass B is designed separately.

3. **The off-ball three lever (R4) — sizing it as the bigger lever.** Building
   "weak off-ball → open threes" as the dominant three-prevention channel means
   deciding where it lives (extend the Roll E denial? a new Roll G / Roll H
   term?) and sizing it **above** on-ball's three contest. Their relative
   magnitude is the load-bearing calibration and is a page-read, but the
   *structure* that makes off-ball dominant is a design choice for the math pass.

4. **The per-man-in-location architectural shift.** Roll G has never read a
   matched man for location. Introducing one for the drive channel breaks a
   standing philosophy deliberately (R1–R3). The build must keep it **confined
   to the drive gate** — the rest of location stays a team read — so the two
   philosophies coexist cleanly rather than the per-man read leaking into the
   whole diet.

5. **Realism of the emergent post-feed on a real population.** The counter only
   reads right if generated post threats and rim-protection ratings have the
   spread to make it matter (Emmett's own note: future generated posts will
   often carry >50 rim protection, changing the interior picture). This is a
   page-read after the build, listed so it isn't mistaken for a bench-provable
   claim.

---

## 6. The math pass — deliberately open, settled oracle-first

The basketball is settled (§3); these are the questions the math conversation
and oracle must resolve with concrete archetypes on the page. Same discipline as
the displacement and post-moves briefs: **every magnitude is a calibration
placeholder, tuned on the season page, never suite-asserted; the lock fixes the
STRUCTURE, not the dial values.** Reuse the existing matchup primitives (the gap
function, the bounded tanh/ratio forms, the `DisplacementGate` primitive) — no
new math family without cause.

Open items, grouped by the three-pass split (§8). **Pass A** (§6.1–6.3, 6.6) is
the drive gate and can lock independently. **Pass B** (§6.5) is the removed-share
disposition / post-feed. **Pass C** (§6.4) is the off-ball three lever.

- **6.1 The drive-gate matchup signal (Pass A).** `PerimeterDefense` (matched
  defender) vs the ballhandler's **drive-tools composite** — through the gap
  function, with a cap on how much paint access a lockdown defender can remove.
  The "unless he has the tools" clause is this gap going positive for the offense.
  **Composite default (folded, source-verified):** start with **BallHandling +
  FirstStep + Quickness** — the discrete ratings — and **do not use raw
  `Athleticism`** at all, since it already = `(Strength+Speed+Quickness+FirstStep+
  Vertical)/5` and would triple-count burst. Document each tool's unique role:
  **BallHandling** = control / change of direction under pressure; **FirstStep** =
  initial separation from a set defender; **Quickness** = sustained lateral change
  / secondary burst. Test whether those three alone produce the archetypes before
  adding any physical term (a small `Strength`/`Speed` contextual role) — add one
  **only** when a real archetype exposes a missing distinction. Prefer a weighted
  composite over a flat average unless the table earns the average (an
  elite-first-step / average-handle creator may win the initial step but fail more
  often after — the composite should not require all tools elite at once).
- **6.1a Shooter-orientation eligibility (open Pass A item — needs a source-owned
  definition).** The gate keys on the shooter's **perimeter-drive orientation**,
  but Pass A must pin that to an actual source field/formula and expose it as its
  own oracle column — it must **not** be position alone, height alone,
  BallHandling alone, usage alone, or low-PostMoves alone. A large wing or
  point-forward must stay eligible; a nominal perimeter guard with no drive game
  must **not** receive a large gate-eligible bucket merely for occupying a
  perimeter slot. This signal decides who the gate even applies to, so it is a
  first-class Pass A decision, not a detail.

- **6.2 The eligible drive bucket = "gate-eligible paint share" (Pass A) — a proxy,
  not literal drive mass (folded).** "Rim access / paint access / rim-short /
  getting downhill" are not the same in a five-zone model, and **a shooter's diet
  carries no per-shot provenance** — the engine cannot tell a drive-created Short
  floater from a post finish or a cut *within one shooter's Short share*. So the
  quantity the gate acts on is a **proxy bucket** — call it *gate-eligible paint
  share*, never "drive mass" — defined by (a) the **shooter's own perimeter-drive
  orientation** (§6.1a: it fires on a guard being walled, not on a post-oriented
  selected shooter), and (b) a zone weighting toward **Rim** (the clearest drive
  signal), treating Short cautiously since it also holds floaters, post finishes,
  and cuts. It must reduce direct rim attempts and some drive-created short shots
  (and perhaps some foul-drawing access) **without** touching post hooks/seals,
  offensive-rebound finishes, cuts behind the defense, or interior catches created
  by another player. R3 is protected at the *shooter-orientation* level, since
  those non-drive interior shots belong to differently-oriented selected shooters —
  but the bucket definition is where R3 could silently break, so the oracle tests
  it directly.

- **6.3 Suppression-primary, not a symmetric bonus (Pass A, folded).** Default
  shape: **elite defender = meaningful suppression, average = near-neutral, poor =
  little/no suppression.** The "weak on-ball leaks the drive" outcome (R4) is the
  *absence* of the wall letting the offense's existing drive advantage express —
  **not** an added positive rim-share term, which would double-count weakness
  against the displacement + offense ratings that already produce normal drive
  advantage. An affirmative weak-defender boost is added only if the archetype
  page shows poor defenders are otherwise insufficiently distinguishable. (This is
  an implementation choice; it does not reopen R4's basketball — the driven-past
  result stands, expressed relatively.)

- **6.4 The off-ball open-three lever (Pass C, least mature — folded).** Sized so
  off-ball dominates on-ball for open-three prevention — but **measured on a
  shared output**, not a raw coefficient: change in catch-and-shoot 3PA / open-
  three frequency / **expected three-point damage**. (On-ball already lowers
  *make quality* on the matched shooter; off-ball is meant to affect *occurrence /
  openness* — they can't be compared as coefficients in unrelated formulas.)
  **Probably not Roll H:** a generic helper make-suppression there would become
  another broad efficiency scalar, the exact thing this design avoids, unless Roll
  H can identify an *open catch-and-shoot* three. Likely ownership (source-decided
  in Pass C, not by preference): the opportunity/selection layer (Roll E) governs
  free availability, Roll G whether it expresses as a three, existing make quality
  afterward. Its relationship to the Roll E denial (which already reads
  OffBallDefense vs OffBallMovement) must be settled so it extends rather than
  duplicates.

- **6.5 Removed-share disposition & post-feed (Pass B).** R5a (Pass A): the walled
  guard's removed share redistributes in his own diet — and this needs a **decided
  hierarchy**, not "mostly to jumpers." Pass A settles: which zones receive denied
  Rim mass; whether Short mass redistributes differently from Rim mass; whether it
  follows the player's existing outside preference; and whether any share may go to
  Mid rather than Three. **Ruling (Emmett 2026-07-14):** denied drive share goes to
  **contested Long/Three** (skip Mid — it is not a pull-up), proportional to the
  shooter's own outer preference; the make door prices those as worse (contested)
  shots. R5b (Pass B): the cross-player post-feed — an upstream selection-odds
  change (§3), gated by a formal **post-threat eligibility** that is NOT tallest /
  center-position / highest-Rim / highest-PostMoves-alone. A credible post-threat
  score needs some of: PostMoves, Strength/size, Close/Finishing, usage willingness,
  ability to establish position / receive the entry — and, crucially, it must respond
  to **post matchup value vs the interior defender**, not raw offensive post talent:
  a good post man against an elite interior defender should NOT absorb all denied
  guard drives. Post-less lineups get exactly zero post-feed weight.
  - **6.5a Usage diffusion (Pass B input, logged 2026-07-14 — Emmett).** A strong
    on-ball defender should also **lower the guarded player's usage** — he is simply
    less likely to shoot at all against tight defense, not merely relocated. This is
    the guarded-side view of the R5b pass-off (his lost drives partly become
    teammates' opportunities, partly just a quieter possession for him). It is a
    **selection-layer (Roll E) effect**, out of Pass A scope by the same boundary as
    R5b, and belongs in Pass B: on-ball matchup quality feeds the shared
    "creation-denied" signal that both raises the post man's selection odds and
    trims the walled man's usage share. Do not build it in Pass A.

- **6.6 FastBreak / zero-defender bypass (all passes).** Transition returns the
  flat pie before any of this; a zero-defender fallback applies no gate. The build
  asserts both (same as the displacement brief §6.7).

**The oracle's structural invariants (folded from the review — beyond archetypes):**

- **Existing-displacement identity.** At neutral gate values, the S36 10-vector
  golden fixture is **unchanged** (bit-exact or tolerance-equivalent). The single
  most important regression guard.
- **Neutrality (measured AFTER displacement).** Average defender vs average
  creator ≈ the **already-displaced** pie — the gate's neutral point means "no
  additional access restriction relative to the pie displacement already
  produced," NOT equality to a flat diet. The oracle reports **pre-gate (displaced)
  and post-gate share both**, so a row never looks non-neutral merely because
  displacement moved it.
- **Monotonicity.** Holding the creator fixed, raising `PerimeterDefense` never
  *raises* his drive access; holding the defender fixed, raising the creator's
  drive-tools composite never *lowers* it.
- **Conservation.** Removed drive share is fully accounted for — no vanishing or
  duplicated probability mass.
- **No post contamination.** Changing the perimeter defender must not alter a true
  post-route selected shooter's rim-location pie directly (R3).
- **No cross-match contamination.** Improving defender A must not gate offensive
  player B, except through an explicitly documented reroute/help mechanism (R5b).
- **No post threat available.** Post-feed weight is exactly zero / structurally
  unavailable when the lineup lacks an eligible post threat — never fed to an
  arbitrary center for occupying a position.
- **Defender reassignment follows the slot.** If the offensive player is matched
  to a different defender, the gate follows the actual matchup slot, not position
  or lineup order.

**The Pass A oracle table — columns each row must expose (folded from review):**
defender `PerimeterDefense`; `BallHandling`; `FirstStep`; `Quickness`;
shooter-orientation eligibility (§6.1a); pre-gate (displaced) Rim; pre-gate Short;
gate signal; suppression multiplier; removed share; post-gate Rim; post-gate
Short; redistribution by destination zone (R5a); conservation delta. Exposing the
pre- and post-gate columns together is what makes neutrality and conservation
readable at a glance.

**Archetype rows (Pass A unless noted).** The composite-probing rows (3–5) are the
most important — they decide whether the composite behaves like basketball rather
than a generic average:
1. average creator vs average defender (neutral — post-gate ≈ displaced pie);
2. average creator vs elite defender (access shrinks, shot forced outside);
3. **elite handle, average burst vs elite defender;**
4. **average handle, elite FirstStep vs elite defender;**
5. **elite handle, elite burst vs elite defender** (the tools clause — drive still
   gets there);
6. poor creator vs poor defender;
7. elite creator vs poor defender;
8. post-oriented scorer vs elite perimeter defender (R3 — interior pie untouched);
9. large point-forward vs elite perimeter defender (§6.1a — stays eligible);
10. override — soft-paint team defense + elite matched perimeter defender (man
    kept out anyway, R2);
11. same offensive player reassigned from elite to weak defender (gate follows the
    slot; suppression relaxes — the R4 leak, expressed relatively);
12. FastBreak and zero-defender bypasses (gate absent).

**Pass C interaction rows:** weak on-ball / strong off-ball — leaks the drive,
not the three; strong on-ball / weak off-ball — leaks the open three, by more than
the prior row leaks any three (measured as expected three-point damage, §6.4).
**Pass B post-feed rows:** strong post vs weak interior (feed rises); strong post
vs strong interior (feed muted — matchup value, not talent); weak post vs weak
interior; no credible post player (feed exactly zero; share lands on the guard's
jumpers per R5a).

---

## 7. Deferred / adjacent — recorded, not built here

- **The pass-off as a distinct outcome.** Emmett named "forced to pass it off to
  another player" as a real result of good on-ball defense. In this design the
  pass-off is expressed as *the shot coming from a different player / a different
  spot* (the re-route), not as a modeled pass event — consistent with the engine
  simulating shot aggregates, not possessions. A literal "reset and swing it"
  outcome (a stalled drive producing no shot for this player) is adjacent and
  deferred.
- **Help-rotation / closeout modeling.** The off-ball three lever (R4) is the
  aggregate stand-in for rotation quality; an explicit rotation model is not
  built.
- **Generated rim-protection spread** (Emmett's note): whether future posts
  carry >50 rim protection by default is a generation-layer concern that changes
  how exploitable weak interiors are — feeds the season-page read, not this
  build.
- **Coaching / strategy layer** — a team that deliberately schemes to force the
  post feed, or one that gambles for steals off-ball — rides the coaching layer,
  same parking as the pressure dial.

---

## 8. Process from here — a THREE-PASS split (folded from the review)

The outside review is adjudicated (see header). Its structural recommendation —
adopted — is to split the math/build work into three independent passes so a
sound drive gate is **not held hostage** by the much larger shot-owner / post-feed
problem. Each pass is oracle-first (archetype table → Emmett's sign-off → locked
oracle + golden fixture) and each locks on its own.

**Pass A — Per-man drive access (the drive gate).** Settles: the eligible drive
bucket (§6.2), the de-overlapped offensive composite (§6.1), the defender matchup
signal, suppression-primary vs symmetric (§6.3), the cap, composition **after**
existing displacement (§5.1), R5a same-player redistribution, and all §6.6
invariants + bypasses. **This is the smallest, cleanest pass and can ship first.**

**Pass B — Removed-share disposition & post-feed (R5b).** Settles: the
cross-player reroute feasibility as an **upstream (Roll E) selection-odds** change
(source-verified that Roll G cannot reassign the shooter), post-threat eligibility
by **matchup value** (§6.5), conservation, and post-less behavior. **Do not assume
Pass B belongs in Roll G** — it is a possession-allocation design spanning rolls,
and the first source question of the pass is exactly *"how does a 'perimeter
creation denied' signal legitimately reach Roll E's selection without violating
roll responsibilities?"*

**Pass C — Off-ball open-three channel.** Settles: which roll owns openness,
catch-and-shoot eligibility, matched off-ball defender vs aggregate, its
relationship to the existing Roll E denial, and sizing **by expected three-point
damage**, not coefficient size (§6.4).

Each pass then becomes its own build prompt — an audited pass (CONVENTIONS
§6a/6b) against a fresh pull, outside-reviewed, built behind the check-in gate,
with the standing obligations: keep displacement's existing regression green,
confine any per-man read to its channel, assert the FastBreak / zero-defender
bypasses.

**Immediate next step:** the Pass A oracle-first design-math conversation — the
drive gate alone, numbers on the §6.6 archetype rows, Emmett reading the table.
Nothing here is a build instruction yet; the basketball is settled, the math and
the seams are the open work, and Pass B (the post-feed) is now explicitly its own
architectural item, not a Roll G afterthought.
