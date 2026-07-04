# Project Charm — Roll G Matchup Displacement: Design Brief

Settled in the design conversation of 2026-07-04, immediately after Session 35
(the tendency-derivation v2 retune) closed; **sharpened across two outside
reviews the same day** (ChatGPT, per CONVENTIONS §6c). Round one folded four
math-agenda requirements into §6 and corrected two source overclaims (the
"actual defender" language and an unverified physical term). Round two corrected
two more claims against source: the level/shape split is Phase 9's conceptual
*target*, not its current raw-gap behavior (§6.1 now carries the Route A/B
decision), and usage widening is attention-*amplified*, not `UsagePressure`
alone — displacement is ruled onto plain `UsagePressure` (§6.5).

**The design-math conversation then ran the same day**, with a first-cut
exploratory oracle putting numbers on the seven archetypes. It produced three
further rulings (all Emmett's), now recorded in §3a: superiority is **always
relative** to the player himself; the **force/invite asymmetry** (outward push
unconditional, inward pull capability-gated); and the advantaged non-finisher's
edge cashing as a **small spread plus core efficiency**, not a rim assault. All
seven archetypes now behave per the rulings under the composed candidate math.
What remains open in §6 is engineering, not basketball. This is the design record for the
**third Roll G shot-diet effect**: the matchup-driven displacement of a featured
player's shot diet — toward the rim when he has the edge on his defense, away
from the rim when the defense has the edge on him — scaled smoothly by his usage
load. It is the deferral created by the shot-tendency brief's §3.5 resolution
("symmetric matchup displacement in Roll G"), now designed.

It is a brief, not a build prompt, and it is not yet a locked spec — the
**basketball intent is settled** (§3–§5 below, every ruling Emmett's); the
**math is open** (§6). Only once the math is settled — oracle-first, with
concrete archetypes on the page — does a build prompt get drafted, audited, and
reviewed per CONVENTIONS §6.

---

## 1. The frame — how this design was reached

Session 35 moved the league's 3PA rate from ~0.19 to ~0.32 against the ~0.39
real target by fixing the generation-side neutral baseline (tendencies now
derive from skills, with the modern-era profile). The residual was assigned to
Roll G at that session's close: the neutral baseline is what a player seeks
against an *average* defender; what the actual defending lineup does to that
diet is a runtime effect only Roll G — which reads the real defenders — can
express.

The design conversation opened on a framing question (is displacement a pure
matchup effect or matchup + situation?) and Emmett's first ruling reframed it
correctly: **the engine does not simulate good/bad/open/contested shots — it
simulates the aggregate of all the shots in a game.** "Situation" (a dying
clock, help swarming, nobody open) is part of the *story* for why a better
defense drags the aggregate down; it is not a thing to model
possession-by-possession. The modeled driver is **defensive quality** (size,
athleticism, real defensive skill), and it expresses as exactly two aggregate
facts: a worse shot **diet** and a lower **make %**.

A source read during the conversation established that both facts already have
machinery, which sharpened what is actually missing (§2). One correction to the
conversational framing, made after reading `LocationMultiplier`'s internals: the
existing location door feeds on **skill only** — zone offense skill
(`Outside / Mid / Close / Finishing`) against zone defensive skill
(`PerimeterDefense / PostDefense / RimProtection`, top-three blended). It carries
**no athleticism or size term** (unlike the make door's `EffectiveRating`, which
does). So the conversational shorthand "size, athleticism, real defensive skill"
describes the basketball intent, not the current inputs; whether displacement
adds a bounded physical term is an explicit math-pass question (§6), not an
assumed input. The brief does not promise a physical contribution the oracle has
not built.

---

## 2. What Roll G already does — verified against source this conversation

Roll G's generator (`RollGGenerator.GenerateWithResidual`) builds the five-zone
shot-location pie in this order today:

1. **Baseline tendencies** through the coaching seam (`CoachingPull.Apply` —
   the player's authored/derived diet, nudged by the coach's shot-selection
   bias).
2. **The per-zone matchup bend** (Phase 9): each zone's tendency is multiplied
   by `Matchup.LocationMultiplier` — the shooter's per-zone offense *skill*
   (`Outside` for three/long, `Mid`, `Close`, `Finishing`) vs the **defending
   lineup's** per-zone resistance (`DefenseRating` = a `PerimeterDefense /
   PostDefense / RimProtection` blend, read from the **top-three** defenders by
   zone, not a slot-matched man), through the gap function and a bounded ratio
   form (strictly positive, exactly 1.0 at zero gap), then renormalized. The
   door is **skill-only — no athleticism or size term** feeds it.
3. **The usage diet-widening** (Phase 17, `ApplyDietShift`): when the selected
   shooter carries above-equal volume, bounded mass moves *out of his dominant
   zone* into the rest of his diet — a versatile player diversifies under load;
   a one-zone specialist cannot, and the unabsorbed load rides to Roll H as
   `UsageResidualPressure`, a make-% penalty. **Verified against source:** its
   request is not `UsagePressure` alone — it is
   `UsagePressure × PressureShiftScale × (1 + attnPressure × AttentionShiftAmplifier)`
   (Phase 28), where `attnPressure` is the shooter's `ShooterAttentionShare`
   above the 0.20 equal share. So widening is **usage-gated but
   attention-amplified**; that amplifier matters for §6.5, where displacement
   must decide whether it reads it too (it does not — see §6.5).

Two structural facts that matter for this design, read from source:

- **The existing bend is lopsidedness-shaped.** Because step 2 multiplies
  per-zone and renormalizes, a defense that is better *everywhere by roughly
  the same amount* moves the diet very little — the multipliers shrink every
  zone together and renormalization washes the common factor out. Only an
  *unevenly* good defense (elite rim protection, ordinary perimeter) reshapes
  the diet today. Under Emmett's ruling (§3) that is exactly backwards for the
  uniformly-superior case: a defense that is simply better should *compress*
  the shooter, not leave his diet untouched.
- **The engine already carries the right usage knob.**
  `PossessionState.UsagePressure`, stamped by Roll E on every selected shooter,
  is `max(0, finalShare − equalShare)` — zero for any player at or below an
  even share of his team's volume, climbing smoothly above it. That is the
  ruling of §4 in one existing number: role players feel nothing by
  construction, featured players feel it in proportion, no threshold anywhere.
  (Cleared by Roll K's `ResetOffense`; never set on FastBreak — transition
  possessions short-circuit to the flat fast-break pie before any of this.)

**Displacement is therefore a third effect with its own distinct cause**, not a
rewrite of the two that exist: step 2's cause is *where the defending lineup is
uneven* (its zone-shape); step 3's cause is *the player's own volume load*;
displacement's cause is *how much better or worse the defending lineup is than
this shooter overall* (its level), gated by that same load.

---

## 3. The core ruling — a superior defense compresses; direction is rim-ward vs. rim-away

Emmett's two archetype rulings, which turned out to be one rule seen from two
sides:

- **The average three-point shooter with a middling drive**, against an
  excellent defense: the defense **takes away his fringe abilities**. The
  marginal drives disappear; he is forced back into what he is truly capable
  of — perimeter jump shots — and converts them at a lower rate. "There is a
  battle between what the player would ideally like to do and what the
  defense's strategy, athleticism, and size are forcing him to do — and that
  has always been resolved in lower efficiency."
- **The slasher with a great drive and no jumper**, against the same defense:
  he **keeps trying to get to the rim** — it is who he is — at lower
  efficiency, *and* he has to settle for more jump shots than he normally
  would. "In some ways, it's an inverse."

The one rule: **a superior defense pushes a featured player's shots outward,
away from the basket, anchored to his own tendency profile.** The shooter's
fringe (his drives) was marginal to begin with, so the outward push wipes it
out and he compresses to the arc. The slasher's rim share is so dominant that
even after leaking he is still rim-first — he just bleeds some volume outward
into jumpers he would never normally take. Same rule, two very
different-looking outcomes, because the anchor is the player's own diet. Nobody
becomes a different player; they get bent.

**The advantage side is symmetric and rim-ward.** When the featured player has
the edge — the defending lineup cannot contain him — the drift runs inward:
"the general rule of basketball is to try to get to the rim, to make things
happen — scoring, drawing a foul, collapsing the defense and kicking out,
causing scrambles."

**Efficiency needs nothing new.** The make curve already punishes the
overmatched shooter in every zone and rewards the advantaged one. Displacement
does not carry its own efficiency coupling — it makes the overmatched player
take the make curve's punishment *from worse spots* (and the advantaged player
collect its reward from better ones). This supersedes the tendency brief §3.5's
sketch of an explicit "efficiency coupling": no such coupling is built; the
curve prices it for free.

**Whole-defense, not a matched man (ruling, 2026-07-04).** Displacement responds
to the **defending lineup as a collective** — help, rim protection, rotations,
the top-three zone resistance the engine already computes — not to a
slot-assigned defender. Roll G has no defender-assignment machinery and this
build invents none. It matches Roll G's existing philosophy (shot location is a
team-defense read, not one-on-one), and it leaves individual matchup where it
already lives: downstream in the make curve and the block/foul doors. All brief
and oracle language reads "the defending lineup," never "his defender."

---

## 3a. The design-math rulings (2026-07-04, first numbers on the archetypes)

The design-math conversation ran an exploratory first-cut oracle (the engine's
exact location-door constants and primitives, plus a candidate displacement
stage) over the seven archetypes. Three rulings came out of reading the numbers,
and two apparent formula failures turned out to be **mislabeled test cases**,
convicted by the first ruling itself.

**R1 — Superiority is ALWAYS relative to the player himself.** Emmett: "always
relative — it's what allows a solid D1 defender to be an elite D3 defender."
The zone gap (his skill minus this lineup's resistance) already is that
comparison; the ruling's force is on what the *proof cases* must mean: "an
equal matchup" for a 78-rated star is a **~78-rated defense**, not an average
one — against an average defense the star genuinely IS advantaged and correctly
pulls rim-ward. Likewise "a wall" for an 80-rated finisher is an **elite**
defense (rim resistance above his finishing) — a uniform-70 defense is a
below-his-level rim defense he should feast on. Both first-cut "failures"
(the equal-matchup case firing, the slasher concentrating rim-ward against a
"wall") were tests mislabeled in absolute terms; relabeled relatively, the
composed math reproduces the §3 rulings: the star at a defense of his own level
reads level ≈ 0 and his diet is untouched; the featured slasher against a
genuinely elite defense stays rim-first while displacement leaks him outward
against the existing bend's concentration — "keeps attacking at lower
efficiency, settles some."

**R2 — The force/invite asymmetry (the design's spine).** The defense can
**force** a player outward regardless of his skill — the overmatched slasher is
shoved into jumpers he can't make; that IS the settling, no skill check. But
space only **invites** a player inward, and he accepts only if he can — the
inward pull is **gated by his own inside skills** (the tendency-oracle gate
primitive on Finishing/Close). Outward push: unconditional. Inward pull:
capability-gated.

**R3 — The advantaged non-finisher's edge is a small spread plus core
efficiency, not a rim assault.** Emmett, on the high-usage spot-up specialist
(74 shooting, 46 finishing) with the overall edge: "a small amount of more
other shots — he's able to have more space than he would otherwise. But a
higher efficiency on what he's really good at as well." Under R2's gate
(rim-gate ≈ 0.24 for a 46 finisher) the numbers land exactly there: everything
ticks up a point or less, the three share stays overwhelmingly dominant, no
manufactured drives even in a blowout. The efficiency half costs nothing —
his gaps are positive, so the make curve already pays him more on everything,
especially his core.

**Verified numerically alongside the rulings:** the blowout role player's
kickout threes (§5) fall out of the **existing** Phase 9 bend for free — at zero
usage pressure, displacement contributes nothing, and the existing bend alone
feeds the spot-up player's arc niche against a weak defense (threes 66 → ~74 in
the archetype). The §5 story requires no displacement machinery at all for the
low-usage half; displacement only supplies the featured-player half.

---

## 4. Usage is the volume knob — and the interaction is the design

Emmett: "this really plays into the ideal usage of the offensive team as well.
If I'm really feeding the slasher usage, he is going to continue to try to get
to the rim at lower efficiency, as well as settle for jump shots he's not
comfortable with. But if he's not a major part of my offensive strategy, he is
just going to do what he normally does."

And, settled explicitly for both directions: "**low usage implies the player
sticks to what they are comfortable with.** If the opposing defense is good or
bad, low-usage players are still going to look for their niche opportunities."

So:

- **Low-usage player, any matchup:** diet essentially untouched. He runs his
  niche; the make curve quietly taxes or rewards it. (Advantage side included —
  a role player with a dead defender in front of him drifts at most a little;
  he is still a role player.)
- **High-usage player, overmatched:** the load forces the confrontation. He
  keeps his core at lower efficiency AND leaks outward into uncomfortable
  shots. Diet moves and efficiency drops.
- **High-usage player, advantaged:** pulled hard toward the rim.
- **The scaling is smooth** (Emmett: "definitely smooth") — no line between
  role player and featured player; displacement grows continuously as usage
  climbs. `UsagePressure`'s shape (zero at/below equal share, smooth above)
  delivers this by construction.

**The timidity question is answered by usage, for now.** How much a player
"caves" is not a personality trait in this pass — it is his load. A personality
layer (the stubborn attacker vs. the guy who folds early) can bend this same
dial later without disturbing the structure. Deferred, §7.

---

## 5. The blowout case — both ends at once, with no openness modeling

The case that stress-tested the shape: a really good team against a really bad,
undersized one. Emmett: "they should attack the rim, and there should be a lot
of open three-point shots as well, because they'll be kicking out to open
shooters."

That is both drift directions at once on the same offense — attackers
collapsing the rim, shooters feasting at the arc — and the resolved design
produces it with **no team-level scalar and no openness modeling**:

- The dominant team's **featured attackers** are advantaged + high-usage →
  pulled rim-ward. That is the rim pressure.
- The dominant team's **spot-up role players** are low-usage → untouched,
  running their already arc-heavy niches. Their "extra open threes" are not a
  drift — they are the niche being *fed*, and their quality is already priced:
  the make curve scores those threes well because the shooters beat their
  matchups too. "Open" never needs to be modeled as openness; it is already in
  the mismatch.

Team-level realism (rim pressure + a healthy three rate at a good clip for the
better team) falls out of per-player rules aggregating — the no-scalar wall
holds.

One aggregate note, recorded honestly rather than promised: the league-wide
effect of displacement on the 3PA rate is **not obviously one-directional**
(every game's overmatched featured players leak arc-ward while its advantaged
featured players pull rim-ward). Whether it closes the ~0.32 → 0.39 residual,
and by how much, is a **page read after the build, never a target the math is
tuned to hit in advance** — the page-only principle applies as always.

---

## 6. The math pass — deliberately open, settled oracle-first

The basketball is settled; these are the questions the math conversation and
oracle must resolve, with concrete archetypes on the page (the proof set in
§6.6). Four are hard requirements the review (ChatGPT, 2026-07-04) sharpened and
Emmett accepted; they are no longer free choices.

### 6.1 The level signal is diet-weighted, and it is NOT a flat average (formula confirmed on archetypes; Route A/B still open)

The five zone gaps do not mean the same thing for every player. A slasher's rim
gap is far more relevant to whether he is pressured outward than his long-two or
three gap; for a shooter, the perimeter gaps carry most of the "forced away from
what I want" signal. So the whole-player level signal is a **diet-weighted
aggregate of the five zone gaps** — weighted by the player's *desired* diet, not
a plain mean.

```
zone gap (per zone) = shooter zone capability − defending-lineup zone resistance
overall level       = Σ (desiredDiet[zone] × zone gap[zone])    ← the displacement signal
zone-shape residual = zone gap[zone] − overall level            ← what Phase 9 already reacts to
```

The existing Phase 9 location bend is *intended* to react to the **residual
shape**; displacement reacts to the **overall level**. A common defensive
upgrade (better everywhere by the same amount) moves the level but not the
residual shape → displacement fires, Phase 9 barely moves. An elite rim
protector with ordinary perimeter defense moves **both** → he raises general
difficulty (level) *and* pushes shots off the rim specifically (shape) — both
correct, each owned by exactly one mechanism.

**But "Phase 9 reacts only to shape" is the conceptual target, not a
description of current code (hard requirement to resolve).** Today Phase 9 feeds
**raw** zone gaps through the nonlinear `GapFn` and the bounded ratio form.
Perfectly equal gaps across all five zones cancel under renormalization — but
with *unequal* gaps, adding a common level shift can still move the relative
multipliers, because the transform is nonlinear. So the clean level/shape split
is not automatically true of the existing bend. The oracle must choose,
deliberately, between two routes and prove the choice:

- **Route A — preserve existing Phase 9.** Raw zone gaps stay Phase 9's input;
  displacement takes the diet-weighted level; the oracle **quantifies and
  bounds** the residual level–shape interaction and shows it is acceptably
  small, rather than claiming perfect separation.
- **Route B — make the split exact.** Phase 9 consumes *residualized* gaps
  (each zone gap minus the overall level); displacement consumes the level; the
  two terms are orthogonal by construction. Cleaner, but it perturbs an
  already-calibrated bend.

Neither is pre-chosen here. Route A is likely less invasive (Phase 9 is
calibrated); Route B is mathematically cleaner. The oracle decides with numbers,
and in **either** case must *demonstrate* the overlap is bounded, never assert
it.

### 6.2 The diet weight is the PRE-BEND baseline, never the already-bent pie (hard requirement)

The `desiredDiet` weighting the level signal must be the player's coached /
derived **pre-matchup** baseline — the diet *before* the Phase 9 per-zone bend.
If displacement weighted by the already-bent pie, the level signal would inherit
Phase 9's bend and the zone-shape effect would be **paid twice**. This is the
single most important anti-double-count guard in the design.

### 6.3 Whether a physical/athletic term enters — an explicit decision, not an assumption

The location door is skill-only today (§2). "Cannot stay in front of him" is
inherently athletic as well as skill-based, so the level signal *may* warrant a
small, bounded physical component built from the existing athleticism-gap
primitive — but the oracle must decide this explicitly and, if it includes one,
answer: against what defensive aggregate (lineup mean, top-three physical
threat, or another existing read), and how gently. The constraint if it is
included: **materially gentler than the make-door physical shift.** Diet
displacement decides which locations become available over a possession
aggregate; the make curve remains where the harsh physical punishment lives. The
build prompt may claim "athleticism" **only if** the oracle contains a bounded,
sourceable physical term; otherwise the language shrinks to "defensive skill."

### 6.4 The direction vector — SETTLED (§3a R2/R3); anchor and magnitude still open

The direction is the **asymmetric ladder**: a negative overall level (defending
lineup superior) shifts mass outward along Rim–Short–Mid–Long–Three,
**unconditionally** — the defense forces you out regardless of skill. A
positive level shifts mass inward, but the inward pull on Rim and Short is
**gated by the player's own inside skills** (the tendency-oracle gate primitive
on Finishing and Close) — space invites; only the capable accept. Confirmed on
the archetypes: the star (gate ≈ 1.0) takes the full rim assault; the spot-up
specialist (gate ≈ 0.24) shows the R3 small spread instead.

Still open, the calibration half: the drift is **anchored to the player's own
baseline diet** (multiplicative on his profile, so nobody becomes a different
player — the slasher stays rim-first even while leaking) and **capped** — the
first-cut cap and gate anchors are placeholders; realistic magnitudes get
calibrated against the league's actual mismatch distribution, and the caps must
be proven binding at the extremes. Must reuse the existing matchup primitives
(the gap function, the bounded tanh/ratio forms) — no new math family without
cause; the first-cut oracle already honors this.

### 6.5 Composition order — likely pre-bend-parallel, but the oracle proves it

Two effects share inputs with displacement, and order decides whether a fact is
paid once or twice:

- **vs the Phase 9 per-zone bend:** they read the same matchup. The clean split
  (§6.1) is level vs shape. The danger the review named: if displacement is
  applied *after* Phase 9 and anchored to the *already-bent* pie, an elite-rim
  shape may have already pushed the slasher outward before the level term reads
  him — magnifying one defensive fact twice. The likely-safer structure, to be
  proven not presumed: compute **both** matchup effects from the **same
  pre-bend baseline**, compose their deltas under bounded caps, renormalize
  **once**, then apply usage widening. This is the current best guess, not an
  instruction.
- **vs usage widening (Phase 17):** both effects are **usage-gated** — but
  widening's request is not `UsagePressure` alone; it is additionally
  **attention-amplified** (`× (1 + attnPressure × AttentionShiftAmplifier)`,
  Phase 28), reading the shooter's above-equal attention share.
  **Ruling: displacement scales from `UsagePressure` alone; it does NOT read
  the attention amplifier.** Displacement answers "how much is this player
  being asked to carry"; widening's attention multiplier answers a separate,
  already-built idea — "how specifically concentrated is defensive attention on
  this shooter." Keeping the amplifier confined to widening preserves
  displacement's clean role and avoids silently turning it into a second
  attention model. Their jobs stay distinct: widening spreads the diet under
  load **regardless of matchup**; displacement points the diet somewhere
  **because of the matchup**. Widening stays **last** — a pressure-driven
  inability to remain narrowly specialized, applied to whatever diet the
  matchup effects have produced. The equal-matchup high-usage archetype (§6.6)
  is the guard that proves widening's existing behavior (attention amplifier
  included) is untouched.

### 6.6 The proof set (the oracle's obligation — adopted verbatim from the review)

**Labeling requirement, learned the hard way (§3a R1): every proof case's
defense is specified RELATIVE to the archetype's own level.** "A wall" for an
80-rated finisher means rim resistance above 80 (elite); "an equal matchup" for
a 78-rated star means a ~78-rated defense. Two first-cut cases mislabeled in
absolute terms produced false failures; the locked oracle's fixtures must carry
the relative labels explicitly.

The oracle must demonstrate bounds and composition on all of (all seven ran
correctly under the composed candidate math in the design-math conversation;
the locked oracle re-proves them as structural checks):

1. average shooter, overmatched (loses fringe drives, compresses to arc);
2. featured rim slasher versus a wall (stays rim-first, leaks some outward);
3. advantaged multi-level star (pulled rim-ward);
4. low-usage shooter in a blowout (essentially untouched — the "open threes"
   are his fed niche, not a drift);
5. uniform strong defense **vs** uneven rim-first strong defense (level moves in
   both; shape moves only in the second — the §6.1 separation, shown);
6. one-zone specialist under high usage (displacement + widening interacting
   without either double-counting);
7. equal matchup at high usage (displacement ≈ 0; existing widening behavior
   provably intact).

### 6.7 FastBreak and the zero-defender fallback (confirmed against source)

- **FastBreak:** Roll G returns the flat fast-break pie immediately, with
  residual 0.0, **before** it reads shooter tendencies, defenders, matchup
  multipliers, or the diet shift. Displacement does not apply on transition
  possessions — nothing to change, but the build asserts it.
- **Zero populated defenders:** the current fallback preserves the pure coached
  tendency diet and still permits usage widening, but cannot apply team-defense
  shaping (no defensive data). Displacement follows the same rule: **no location
  bend, no displacement, usage widening still applies** — consistent with the
  current fallback, manufacturing no "neutral defender" from missing data.

---

## 7. Deferred / adjacent — recorded, not built here

- **Personality / timidity** as a bend on the usage dial (the stubborn
  attacker vs. the early folder). The structure above accepts it later without
  rework.
- **Strategy layers** (a team that deliberately hunts huge three volume) —
  Emmett named "extreme strategic circumstances" as an exception to rim-ward
  advantage drift; that is a coaching/strategy concern, not this effect.
- **The open-only-guard context readout** (deferred from Session 35's outside
  review): realized 3PA/3P% under neutral vs. strong-defense contexts. This
  build creates exactly the context plumbing that readout needs — revisit it
  in the build prompt's instrumentation section rather than leaving it parked.
- **The literal `Outside == 0` heave** at pie time (the sliver Session 35's
  universal floor did not absorb) — Roll G territory, trivially small; decide
  at math time whether it rides this build or stays parked.
- **Teammate spacing / lineup-context bend** as a selection effect — the
  long-standing Roll G deferral, untouched by this design.

---

## 8. Process from here

Same pipeline as the tendency derivation. **Status: the design-math
conversation is done** — the basketball is fully settled (§3, §3a, §4, §5) and
an exploratory first-cut oracle exists (the engine's exact location-door
constants and primitives plus the candidate displacement stage; all seven
archetypes behaving per the rulings under the composed math). What remains
before the build prompt is engineering, Claude's lane, escalated to Emmett only
if a genuine basketball choice surfaces:

1. **Route A/B quantification** (§6.1) — measure the residual level–shape
   overlap on raw-gap Phase 9, choose, and bound it.
2. **The physical-term decision** (§6.3) — with numbers, per its constraints.
3. **Magnitude calibration** — realistic caps and gate anchors against the
   league's actual mismatch and usage-pressure distributions (the first-cut
   knobs are placeholders).
4. **Hardening into the locked oracle** — structural checks over the §6.6
   proof set (relatively labeled), a population sweep, and golden traces if
   the build warrants stage-wise parity, tendency-oracle style.

Then: outside review of the locked oracle (§6c), the build prompt drafted as
its own audited pass (CONVENTIONS §6a/6b), reviewed, and built behind the
check-in gate.
